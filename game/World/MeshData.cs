using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds;

/// <summary>Surface looks of the stadium shader (UV2.x). Each is a branch in stadium.gdshader.</summary>
public enum Look
{
    Plain = 0, Roof = 1, Glass = 2, Curtain = 3, Fascia = 4, Ribbon = 5, Lamp = 6, RoofLight = 7,
    Board = 8, Tier = 9, Screen = 10, Cloth = 11, Stipple = 12, Unlit = 13, Grass = 14,
}

/// <summary>
/// Static geometry for one draw: triangles with a linear albedo (COLOR.rgb), the baked sun
/// visibility (COLOR.a, filled in by <see cref="LightBake"/>), uv in metres (UV) and the look
/// plus one parameter (UV2). Built on the CPU once, committed as a single ArrayMesh.
/// </summary>
public sealed class MeshData
{
    public readonly List<Vector3> V = new();
    public readonly List<Vector3> N = new();
    public readonly List<Color> C = new();
    public readonly List<Vector2> UV = new();
    public readonly List<Vector2> UV2 = new();

    /// <summary>Current albedo (linear), look and parameter for what's added next.</summary>
    public Color Col = Colors.White;
    public Look Look = Look.Plain;
    public float Param;
    /// <summary>Darkening baked into the albedo (ambient occlusion, a roof's shade).</summary>
    public float Shade = 1f;

    public int Count => V.Count;

    /// <summary>Sets the albedo from an sRGB hex colour.</summary>
    public MeshData Hex(uint rgb, Look look = Look.Plain, float param = 0)
    {
        Col = Srgb(rgb);
        Look = look;
        Param = param;
        return this;
    }

    public static Color Srgb(uint rgb) =>
        new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f).SrgbToLinear();

    void Vert(Vector3 p, Vector3 n, Vector2 uv)
    {
        V.Add(p);
        N.Add(n);
        var c = Col;
        C.Add(new Color(c.R * Shade, c.G * Shade, c.B * Shade, 1f));
        UV.Add(uv);
        UV2.Add(new Vector2((float)Look, Param));
    }

    /// <summary>A triangle; its front is the side its normal faces (counter-clockwise seen from there).</summary>
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
    {
        var n = (b - a).Cross(c - a);
        if (n.LengthSquared() < 1e-12f) return;
        n = n.Normalized();
        // Godot's front faces wind clockwise.
        Vert(a, n, ua); Vert(c, n, uc); Vert(b, n, ub);
    }

    /// <summary>A quad a-b-c-d (counter-clockwise seen from the front). uv: a (0,0), b (uw,0),
    /// c (uw,uh), d (0,uh), so u runs a to b and v runs a to d.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uw = 1, float uh = 1)
    {
        Tri(a, b, c, new(0, 0), new(uw, 0), new(uw, uh));
        Tri(a, c, d, new(0, 0), new(uw, uh), new(0, uh));
    }

    public void QuadUV(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        Tri(a, b, c, ua, ub, uc);
        Tri(a, c, d, ua, uc, ud);
    }

    /// <summary>An axis-aligned box (all six faces).</summary>
    public void Box(Vector3 mn, Vector3 mx) => Box(Transform3D.Identity.Translated((mn + mx) / 2), mx - mn);

    /// <summary>A box of `size` centred on t's origin, along t's axes. `faces` masks
    /// +X -X +Y -Y +Z -Z (bits 0..5).</summary>
    public void Box(Transform3D t, Vector3 size, int faces = 63)
    {
        var h = size / 2;
        Vector3 P(float x, float y, float z) => t * new Vector3(x * h.X, y * h.Y, z * h.Z);
        if ((faces & 1) != 0) Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), size.Z, size.Y);
        if ((faces & 2) != 0) Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), size.Z, size.Y);
        if ((faces & 4) != 0) Quad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), size.X, size.Z);
        if ((faces & 8) != 0) Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), size.X, size.Z);
        if ((faces & 16) != 0) Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), size.X, size.Y);
        if ((faces & 32) != 0) Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), size.X, size.Y);
    }

    /// <summary>A beam of cross-section w x h from a to b.</summary>
    public void Beam(Vector3 a, Vector3 b, float w, float h)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 0.05f) return;
        var z = d / len;
        var up = Mathf.Abs(z.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        var x = up.Cross(z).Normalized();
        var y = z.Cross(x);
        Box(new Transform3D(new Basis(x, y, z), (a + b) / 2), new Vector3(w, h, len));
    }

    /// <summary>A cylinder (no caps) of radius r from a to b.</summary>
    public void Cylinder(Vector3 a, Vector3 b, float r, int sides = 6)
    {
        var z = (b - a).Normalized();
        var up = Mathf.Abs(z.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        var x = up.Cross(z).Normalized();
        var y = z.Cross(x);
        float len = (b - a).Length();
        for (int i = 0; i < sides; i++)
        {
            float a0 = Mathf.Tau * i / sides, a1 = Mathf.Tau * (i + 1) / sides;
            var o0 = (x * Mathf.Cos(a0) + y * Mathf.Sin(a0)) * r;
            var o1 = (x * Mathf.Cos(a1) + y * Mathf.Sin(a1)) * r;
            Quad(a + o0, a + o1, b + o1, b + o0, r * Mathf.Tau / sides, len);
        }
    }

    public void Append(MeshData o)
    {
        V.AddRange(o.V); N.AddRange(o.N); C.AddRange(o.C); UV.AddRange(o.UV); UV2.AddRange(o.UV2);
    }

    public ArrayMesh Commit()
    {
        var mesh = new ArrayMesh();
        if (V.Count == 0) return mesh;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = V.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = N.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = C.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = UV.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = UV2.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
