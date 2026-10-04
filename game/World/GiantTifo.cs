using System;
using Godot;

namespace GameNight.Grounds;

/// <summary>
/// The home fans' giant tifo (the PWA's hangingTifo): a portrait banner the size of a stand,
/// hung from the main stand's roof front over the tiers. It unrolls from a roll at the roof
/// (the roll runs down its foot as it unfurls) and is wound back up once play is under way.
/// The artwork is the supporters' own picture when they've made one, else the club's design:
/// the name, a sunburst behind the crest, the motto.
/// </summary>
public sealed class GiantTifo
{
    const float W = 32, H = 38;
    const int ArtW = 432, ArtH = 512;
    readonly Transform3D _top;
    readonly Vector3 _face;
    Node3D _cloth;
    MeshInstance3D _roll;
    ShaderMaterial _mat;

    /// <summary>Hung from `mid` of the main stand, `o` m out from its front, its top at `top`.</summary>
    public GiantTifo(PathPt mid, float o, float top)
    {
        _face = new Vector3(-mid.NX, 0, -mid.NZ);
        _top = new Transform3D(new Basis(Vector3.Up.Cross(_face), Vector3.Up, _face), mid.At(o, top));
    }

    public void Attach(Node3D root, ClubArt art, string name, uint home, string motto)
    {
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/giant.gdshader") };
        _cloth = new MeshInstance3D { Mesh = Cloth(), MaterialOverride = _mat, Transform = _top, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false, ExtraCullMargin = 4 };
        root.AddChild(_cloth);
        var m = new MeshData();
        m.Hex(Darken(home, 0.7f));
        m.Beam(new Vector3(-W / 2 - 0.4f, 0, 0), new Vector3(W / 2 + 0.4f, 0, 0), 1.2f, 1.2f);
        _roll = new MeshInstance3D
        {
            Mesh = m.Commit(),
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/stadium.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        root.AddChild(_roll);
        root.AddChild(new Signage(new Vector2I(ArtW, ArtH), s => Paint(s, art, name, home, motto), tex => _mat.SetShaderParameter("art", tex), art));
    }

    /// <summary>How far it has unrolled: 0 wound up, 1 hanging full length.</summary>
    public void Set(float drop)
    {
        bool show = drop > 0.001f;
        _cloth.Visible = _roll.Visible = show;
        if (!show) return;
        float y = _top.Origin.Y - drop * H;
        _mat.SetShaderParameter("cut_y", y);
        var p = _top.Origin + _face * 0.5f;
        _roll.Transform = new Transform3D(_top.Basis, new Vector3(p.X, y, p.Z));
    }

    /// <summary>The cloth: W x H hanging down from its top edge (local y 0 to -H), facing +z.
    /// uv as the picture (v down).</summary>
    static ArrayMesh Cloth()
    {
        const int nx = 32, ny = 24;
        var v = new Vector3[(nx + 1) * (ny + 1)];
        var uv = new Vector2[v.Length];
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
            {
                float u = i / (float)nx, t = j / (float)ny;
                v[j * (nx + 1) + i] = new Vector3((u - 0.5f) * W, -t * H, 0);
                uv[j * (nx + 1) + i] = new Vector2(u, t);
            }
        var idx = new int[nx * ny * 6];
        int k = 0;
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                idx[k++] = a; idx[k++] = b; idx[k++] = c;
                idx[k++] = b; idx[k++] = d; idx[k++] = c;
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    static void Paint(Signage s, ClubArt art, string name, uint home, string motto)
    {
        const uint Navy = 0x14123a, Cream = 0xf4efe2, Gold = 0xffd447;
        if (art.GiantTifo != null)
        {
            s.Picture(art.GiantTifo, new Rect2(0, 0, ArtW, ArtH));
            Hem(s);
            return;
        }
        s.Rect(new Rect2(0, 0, ArtW, ArtH), home);
        // A sunburst behind the crest: every other ray a shade darker.
        var c = new Vector2(ArtW / 2f, 262);
        for (int i = 0; i < 28; i += 2)
        {
            float a0 = i / 28f * Mathf.Tau, a1 = (i + 1) / 28f * Mathf.Tau;
            s.Poly(new Color(0, 0, 0, 0.2f), c, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 600, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 600);
        }
        // The name band on top and the motto band at the bottom, chevrons on their inner edges.
        void Band(float y, float h, string text, int size, bool up)
        {
            s.Rect(new Rect2(0, y, ArtW, h), Navy);
            float edge = up ? y : y + h;
            for (float x = 0; x < ArtW; x += 36)
                s.Poly(Cream, new(x, edge), new(x + 18, edge + (up ? -14 : 14)), new(x + 36, edge));
            s.Text(new Rect2(28, y + h * 0.08f, ArtW - 56, h), text.ToUpperInvariant(), Gold, size);
        }
        Band(0, 104, name, 84, false);
        Band(ArtH - 78, 78, motto, 50, true);
        // The crest on a cream disc.
        s.Poly(Navy, Signage.Circle(c, 124));
        s.Poly(Cream, Signage.Circle(c, 114));
        s.Crest(c, 176, home);
        if (!string.IsNullOrWhiteSpace(art.Founded))
            s.Text(new Rect2(c.X - 100, c.Y + 132, 200, 36), $"EST. {art.Founded}", Cream, 30);
        // A cream frame with a dark keyline.
        s.Line(Cream, 16, new(8, 8), new(ArtW - 8, 8), new(ArtW - 8, ArtH - 8), new(8, ArtH - 8), new(8, 8));
        s.Line(Navy, 3, new(18, 18), new(ArtW - 18, 18), new(ArtW - 18, ArtH - 18), new(18, ArtH - 18), new(18, 18));
        Hem(s);
    }

    /// <summary>The hem along the top, with the eyelets it's tied to the roof by.</summary>
    static void Hem(Signage s)
    {
        s.Rect(new Rect2(0, 0, ArtW, 6), 0x000000, 0.3f);
        for (float x = 22; x < ArtW; x += 48) s.Poly(0xc9c4b8, Signage.Circle(new Vector2(x, 4), 3));
    }

    static uint Darken(uint hex, float k) =>
        ((uint)(((hex >> 16) & 255) * k) << 16) | ((uint)(((hex >> 8) & 255) * k) << 8) | (uint)((hex & 255) * k);
}
