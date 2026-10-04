using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace GameNight.Grounds;

/// <summary>
/// Baked light for a ground, done once when it's built:
/// - a height map of everything solid (the stands, roofs, the near stand behind the camera),
///   so the sun's visibility anywhere is a short march along a ray toward it;
/// - every stadium vertex and every fan gets its visibility written into its colour;
/// - the light map for the pitch, players and ball: the stand shadows (r) and the floodlight
///   banks' aimed pools (g), as a small texture the live shaders read (light_map.gdshaderinc).
/// </summary>
public sealed class LightBake
{
    const float Cell = 0.5f;
    readonly float _x0, _z0;
    readonly int _w, _h;
    readonly float[] _height;
    float _maxH;
    readonly Vector3 _toSun;

    public LightBake(float x0, float z0, float x1, float z1)
    {
        _x0 = x0;
        _z0 = z0;
        _w = (int)Mathf.Ceil((x1 - x0) / Cell);
        _h = (int)Mathf.Ceil((z1 - z0) / Cell);
        _height = new float[_w * _h];
        _toSun = -Atmosphere.SunDir;
    }

    /// <summary>Adds solid geometry to the height map (the max height over each cell).</summary>
    public void AddCaster(MeshData m) => AddCaster(m.V);

    public void AddCaster(List<Vector3> v)
    {
        for (int i = 0; i + 2 < v.Count; i += 3)
        {
            Vector3 a = v[i], b = v[i + 1], c = v[i + 2];
            // Edges (walls and thin slivers have no area seen from above), then the inside.
            Edge(a, b); Edge(b, c); Edge(c, a);
            float area = (b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z);
            if (Mathf.Abs(area) < 1e-4f) continue;
            int i0 = Math.Max(0, (int)((Mathf.Min(a.X, Mathf.Min(b.X, c.X)) - _x0) / Cell));
            int i1 = Math.Min(_w - 1, (int)((Mathf.Max(a.X, Mathf.Max(b.X, c.X)) - _x0) / Cell));
            int j0 = Math.Max(0, (int)((Mathf.Min(a.Z, Mathf.Min(b.Z, c.Z)) - _z0) / Cell));
            int j1 = Math.Min(_h - 1, (int)((Mathf.Max(a.Z, Mathf.Max(b.Z, c.Z)) - _z0) / Cell));
            for (int j = j0; j <= j1; j++)
                for (int k = i0; k <= i1; k++)
                {
                    float px = _x0 + (k + 0.5f) * Cell, pz = _z0 + (j + 0.5f) * Cell;
                    float w1 = ((b.X - px) * (c.Z - pz) - (c.X - px) * (b.Z - pz)) / area;
                    float w2 = ((c.X - px) * (a.Z - pz) - (a.X - px) * (c.Z - pz)) / area;
                    float w3 = 1 - w1 - w2;
                    if (w1 < 0 || w2 < 0 || w3 < 0) continue;
                    Put(k, j, a.Y * w1 + b.Y * w2 + c.Y * w3);
                }
        }
    }

    void Edge(Vector3 a, Vector3 b)
    {
        int n = Math.Max(1, (int)Mathf.Ceil(new Vector2(b.X - a.X, b.Z - a.Z).Length() / (Cell * 0.5f)));
        for (int s = 0; s <= n; s++)
        {
            var p = a.Lerp(b, s / (float)n);
            int cx = (int)((p.X - _x0) / Cell), cz = (int)((p.Z - _z0) / Cell);
            if (cx >= 0 && cz >= 0 && cx < _w && cz < _h) Put(cx, cz, p.Y);
        }
    }

    void Put(int cx, int cz, float y)
    {
        int k = cz * _w + cx;
        if (y > _height[k]) _height[k] = y;
        if (y > _maxH) _maxH = y;
    }

    /// <summary>1 if the sun reaches p, 0 if something solid is in the way.</summary>
    public float SunVisibility(Vector3 p)
    {
        float hor = Mathf.Sqrt(_toSun.X * _toSun.X + _toSun.Z * _toSun.Z);
        var step = _toSun * (Cell / hor);
        // Start a cell out, so a surface doesn't shade itself.
        p += step;
        for (int i = 0; i < 1000; i++)
        {
            if (p.Y > _maxH) return 1;
            int cx = (int)((p.X - _x0) / Cell), cz = (int)((p.Z - _z0) / Cell);
            if (cx < 0 || cz < 0 || cx >= _w || cz >= _h) return 1;
            if (_height[cz * _w + cx] > p.Y) return 0;
            p += step;
        }
        return 1;
    }

    /// <summary>Writes the sun's visibility into each vertex's alpha (offset toward the sun's
    /// side of the surface, so two-sided sheets read their lit face).</summary>
    public void BakeVertices(MeshData m)
    {
        var vis = new float[m.V.Count];
        Parallel.For(0, m.V.Count, i =>
        {
            var n = m.N[i];
            if (n.Dot(_toSun) < 0) n = -n;
            vis[i] = SunVisibility(m.V[i] + n * 0.35f);
        });
        for (int i = 0; i < vis.Length; i++)
        {
            var c = m.C[i];
            c.A = vis[i];
            m.C[i] = c;
        }
    }

    /// <summary>
    /// The light map over the pitch and its surrounds (x0..x1, z0..z1 at `texel` metres):
    /// r = sun visibility (softened a little), g = floodlight pools from `lamps` (aimed at the
    /// pitch, normalised to average 1 over it, stored halved). Sets the shader globals.
    /// </summary>
    public void PublishLightMap(Vector3[] lamps, float x0 = -84, float z0 = -58, float x1 = 84, float z1 = 58, float texel = 0.5f)
    {
        int w = (int)((x1 - x0) / texel), h = (int)((z1 - z0) / texel);
        var sun = new float[w * h];
        Parallel.For(0, h, j =>
        {
            for (int i = 0; i < w; i++)
                sun[j * w + i] = SunVisibility(new Vector3(x0 + (i + 0.5f) * texel, 0.02f, z0 + (j + 0.5f) * texel));
        });
        // Penumbra: a 5x5 box blur (a metre or so, like a low sun through a stand's edge).
        var soft = new float[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                float s = 0; int n = 0;
                for (int dj = -2; dj <= 2; dj++)
                    for (int di = -2; di <= 2; di++)
                    {
                        int ii = Math.Clamp(i + di, 0, w - 1), jj = Math.Clamp(j + dj, 0, h - 1);
                        s += sun[jj * w + ii]; n++;
                    }
                soft[j * w + i] = s / n;
            }

        var aims = new Vector3[lamps.Length];
        for (int k = 0; k < lamps.Length; k++)
            aims[k] = (new Vector3(lamps[k].X * 0.4f, 0, lamps[k].Z * 0.32f) - lamps[k]).Normalized();
        float Pool(float x, float z)
        {
            float e = 0;
            for (int k = 0; k < lamps.Length; k++)
            {
                var d = lamps[k] - new Vector3(x, 0, z);
                float r2 = d.LengthSquared();
                var l = d / Mathf.Sqrt(r2);
                float beam = Mathf.SmoothStep(0.8f, 0.96f, -l.Dot(aims[k]));
                e += l.Y / r2 * (0.3f + beam);
            }
            return e;
        }
        float norm = 1;
        if (lamps.Length > 0)
        {
            float sum = 0; int cnt = 0;
            for (float x = -50; x <= 50; x += 5)
                for (float z = -32; z <= 32; z += 4) { sum += Pool(x, z); cnt++; }
            norm = sum > 0 ? cnt / sum : 1;
        }

        var data = new byte[w * h * 2];
        Parallel.For(0, h, j =>
        {
            for (int i = 0; i < w; i++)
            {
                float x = x0 + (i + 0.5f) * texel, z = z0 + (j + 0.5f) * texel;
                float pool = lamps.Length > 0 ? Pool(x, z) * norm : 1f;
                data[(j * w + i) * 2] = (byte)Mathf.Clamp(soft[j * w + i] * 255 + 0.5f, 0, 255);
                data[(j * w + i) * 2 + 1] = (byte)Mathf.Clamp(pool * 0.5f * 255 + 0.5f, 0, 255);
            }
        });
        var img = Image.CreateFromData(w, h, false, Image.Format.Rg8, data);
        var tex = ImageTexture.CreateFromImage(img);
        RenderingServer.GlobalShaderParameterSet("gn_light_map", tex);
        RenderingServer.GlobalShaderParameterSet("gn_light_rect", new Vector4(x0, z0, 1f / (x1 - x0), 1f / (z1 - z0)));
    }
}
