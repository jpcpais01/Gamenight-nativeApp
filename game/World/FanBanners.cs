using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds;

/// <summary>
/// The supporters' own banner (the PWA's fanBanners): their picture printed on cloth with a
/// stitched border in the club colour, held up between two poles in the home end and along
/// the far side. Only when they've made one (Tifos: Fan).
/// </summary>
public sealed class FanBanners
{
    readonly List<(Transform3D at, float w)> _spots = new();

    public int Count => _spots.Count;

    /// <summary>A banner `w` m wide held up at the fans' spot `foot` on the tier, facing the pitch
    /// from `p`, leaning back a little with the rake. Its poles go into the static mesh `m`.</summary>
    public void Hold(MeshData m, PathPt p, Vector3 foot, float w)
    {
        var t = new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(-p.NX, -p.NZ)) * new Basis(Vector3.Right, -0.12f), foot);
        float h = w / 2;
        m.Hex(0x2a2a2e);
        foreach (int sx in new[] { -1, 1 })
            m.Beam(t * new Vector3(sx * w / 2, 0, 0.02f), t * new Vector3(sx * w / 2, h + 1.9f, 0.02f), 0.08f, 0.08f);
        _spots.Add((t, w));
    }

    public void Build(Node3D root, Texture2D photo, uint home)
    {
        if (_spots.Count == 0 || photo == null) return;
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/giant.gdshader") };
        mat.SetShaderParameter("tie", 1f);
        mat.SetShaderParameter("amp", 0.16f);
        mat.SetShaderParameter("self_lit", 0.55f);
        foreach (var (at, w) in _spots)
            root.AddChild(new MeshInstance3D { Mesh = Cloth(w, w / 2), MaterialOverride = mat, Transform = at, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        // A painted cloth border in the club colour with white stitching, the photo inside.
        root.AddChild(new Signage(new Vector2I(680, 360), s =>
        {
            s.Rect(new Rect2(0, 0, 680, 360), home);
            s.Picture(photo, new Rect2(20, 20, 640, 320));
            for (float x = 9; x < 671; x += 22)
            {
                s.Rect(new Rect2(x, 7, 14, 4), 0xf3eee2);
                s.Rect(new Rect2(x, 349, 14, 4), 0xf3eee2);
            }
            for (float y = 9; y < 351; y += 22)
            {
                s.Rect(new Rect2(7, y, 4, 14), 0xf3eee2);
                s.Rect(new Rect2(669, y, 4, 14), 0xf3eee2);
            }
        }, tex => mat.SetShaderParameter("art", tex)));
    }

    /// <summary>The cloth, w x h, its foot 1.6 m up the poles, facing +z, uv as the picture.</summary>
    static ArrayMesh Cloth(float w, float h)
    {
        const int nx = 24, ny = 12;
        var v = new Vector3[(nx + 1) * (ny + 1)];
        var uv = new Vector2[v.Length];
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
            {
                float u = i / (float)nx, t = j / (float)ny;
                v[j * (nx + 1) + i] = new Vector3((u - 0.5f) * w, 1.6f + h * (1 - t), 0);
                uv[j * (nx + 1) + i] = new Vector2(u, t);
            }
        var idx = new List<int>();
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                idx.AddRange(new[] { a, b, c, b, d, c });
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
