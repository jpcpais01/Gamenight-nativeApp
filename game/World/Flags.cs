using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GameNight.Grounds;

/// <summary>
/// Flags waved in the crowd (the PWA's crowdFlags): the ultras behind the home goal wave the
/// most, a few in the away end, some along the sides. Club colours in seven patterns, a share
/// with the club crest when there is one. One MultiMesh draw (World/Shaders/flags.gdshader).
/// </summary>
public sealed class Flags
{
    readonly struct Spot
    {
        public readonly Vector3 Pos;
        public readonly float Yaw, Size;
        public readonly uint C, C2;
        public readonly int Pat;

        public Spot(Vector3 pos, float yaw, float size, uint c, uint c2, int pat) =>
            (Pos, Yaw, Size, C, C2, Pat) = (pos, yaw, size, c, c2, pat);
    }

    const uint W = 0xf3eee2, N = 0x14123a;
    readonly List<Spot> _spots = new();
    long _seed = 11;

    float Rnd() => (_seed = _seed * 16807 % 2147483647) / 2147483647f;

    public int Count => _spots.Count;

    /// <summary>Flags over a tier from a to b (offset, height) along `path`, by zone: the home
    /// end (1) waves 34, the away end (2) 12, the sides (0) 18.</summary>
    public void Add(List<PathPt> path, Vector2 a, Vector2 b, uint home, uint away, float crestShare)
    {
        void Zone(int zone, int n, uint[][] cols, float big, float crest)
        {
            var pts = path.Where(p => p.Zone == zone).ToList();
            if (pts.Count == 0) return;
            float o0 = a.X + 1.1f, o1 = Mathf.Min(b.X - 1, a.X + 16.1f);
            for (int i = 0; i < n; i++)
            {
                var c = cols[(int)(Rnd() * cols.Length)];
                bool withCrest = Rnd() < crest;
                var p = pts[(int)(Rnd() * pts.Count)];
                float o = o0 + Rnd() * (o1 - o0);
                float h = a.Y + (o - a.X) / (b.X - a.X) * (b.Y - a.Y);
                _spots.Add(new Spot(p.At(o, h + 1.1f), Mathf.Atan2(-p.NX, -p.NZ), 0.8f + Rnd() * big,
                    withCrest ? home : c[0], withCrest ? W : c[1], withCrest ? 4 : (int)(Rnd() * 7)));
            }
        }
        Zone(1, 34, new[] { new[] { home, W }, new[] { home, N }, new[] { W, home }, new[] { 0xffd447u, home } }, 0.7f, crestShare);
        Zone(2, 12, new[] { new[] { away, W }, new[] { W, away }, new[] { away, N } }, 0.4f, 0);
        Zone(0, 18, new[] { new[] { home, W }, new[] { W, home }, new[] { away, W } }, 0.35f, crestShare * 0.6f);
    }

    public void Build(Node3D root, LightBake bake, Texture2D crest)
    {
        if (_spots.Count == 0) return;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = FlagMesh(),
            InstanceCount = _spots.Count,
        };
        for (int i = 0; i < _spots.Count; i++)
        {
            var s = _spots[i];
            float vis = bake?.SunVisibility(s.Pos + new Vector3(0, 2.5f, 0)) ?? 1;
            mm.SetInstanceTransform(i, new Transform3D(new Basis(Vector3.Up, s.Yaw).Scaled(Vector3.One * s.Size), s.Pos));
            var c = MeshData.Srgb(s.C);
            var c2 = MeshData.Srgb(s.C2);
            mm.SetInstanceColor(i, new Color(c.R, c.G, c.B, s.Pat + Mathf.Clamp(vis, 0, 1) * 0.9f));
            mm.SetInstanceCustomData(i, new Color(c2.R, c2.G, c2.B, i * 2.39f));
        }
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/flags.gdshader") };
        if (crest != null)
        {
            mat.SetShaderParameter("crest", crest);
            mat.SetShaderParameter("has_crest", 1f);
        }
        root.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 4,
        });
    }

    /// <summary>The cloth (2.4 x 1.5 m, 8 x 3 cells, tied to the pole at x = 0) and the pole.</summary>
    static ArrayMesh FlagMesh()
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var idx = new List<int>();
        const int nx = 8, ny = 3;
        for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
            {
                v.Add(new Vector3(2.4f * i / nx, 1.7f + 1.5f * j / ny, 0));
                uv.Add(new Vector2(i / (float)nx, j / (float)ny));
                uv2.Add(new Vector2(1, 0));
            }
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                idx.AddRange(new[] { a, c, b, b, c, d });
            }
        // The pole: a thin square post, 3.3 m.
        int o = v.Count;
        const float r = 0.03f;
        for (int k = 0; k < 4; k++)
        {
            float x = (k & 1) == 0 ? -r : r, z = (k & 2) == 0 ? -r : r;
            v.Add(new Vector3(x, 0, z)); v.Add(new Vector3(x, 3.3f, z));
            uv.Add(Vector2.Zero); uv.Add(Vector2.Zero); uv2.Add(Vector2.Zero); uv2.Add(Vector2.Zero);
        }
        int[] ring = { 0, 1, 3, 2 };
        for (int k = 0; k < 4; k++)
        {
            int p0 = o + ring[k] * 2, p1 = o + ring[(k + 1) % 4] * 2;
            idx.AddRange(new[] { p0, p0 + 1, p1, p1, p0 + 1, p1 + 1 });
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = Enumerable.Repeat(Vector3.Back, v.Count).ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
