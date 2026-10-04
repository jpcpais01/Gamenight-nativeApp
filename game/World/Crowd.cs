using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds;

/// <summary>How a tier is filled with fans.</summary>
public sealed class TierFans
{
    /// <summary>Seat width and row depth (along the slope), metres.</summary>
    public float SeatW = 0.62f, RowD = 0.8f;
    /// <summary>Share of seats taken, on average: the empty ones come in random singles and in
    /// patchy gaps, and the ends behind the goals are packed fuller.</summary>
    public float Fill = 0.9f;
    /// <summary>Aisle steps every 15 m (matches the tier look).</summary>
    public bool Aisles = true;
    /// <summary>Vomitories every 30 m between these slope distances (none if y &lt;= x).</summary>
    public Vector2 Vom;
    /// <summary>Slope distance where the roof's shade starts / is full.</summary>
    public Vector2 Shade = new(999, 1000);
    /// <summary>Card displays: given a fan's spot, how far up the slope he is and his zone,
    /// the atlas uv (0..1) of the card he holds up, or null.</summary>
    public Func<Vector3, float, int, Vector2?> Tifo;
    /// <summary>Overrides the path's zones (e.g. a stand that's all away fans).</summary>
    public int? Zone;
}

/// <summary>
/// The crowd: every fan is an upright sprite (CrowdSprites) on his step, in a dozen static meshes (wedges
/// round the pitch, culled whole when out of shot). The vertex shader dresses and animates each from a hash of where he stands: sitting
/// or standing, bouncing with the ultras, up out of their seats when it gets close, arms and
/// scarves up, the scoring side going wild while the other sits in silence, cards held up
/// for the kick-off tifo, phone torches at night. The CPU never touches it after building.
/// </summary>
public sealed class Crowd
{
    readonly List<Vector3> _v = new();
    readonly List<Vector3> _n = new();
    readonly List<Vector2> _uv = new();
    readonly List<Vector2> _uv2 = new();
    readonly List<Color> _c = new();
    readonly List<int> _idx = new();

    public int Fans => _v.Count / 4;

    /// <summary>Fans on the slope of a tier swept along `path` from section point a to b.</summary>
    public void Tier(List<PathPt> path, Vector2 a, Vector2 b, TierFans o)
    {
        float slope = (b - a).Length();
        var dir = (b - a) / slope;
        int rows = (int)((slope - 0.4f) / o.RowD);
        // Running u along the front edge (the tier strip's uv) is the same at every offset
        // only approximately; aisles and tifo rects use the front edge's u, as the look does.
        var rng = new Random(path.Count * 7919 + (int)(a.X * 13 + a.Y * 31));
        int seed = rng.Next(1000);
        for (int k = 0; k < rows; k++)
        {
            float sv = (k + 0.6f) * o.RowD;
            var sec = a + dir * sv;
            float shade = 1f - 0.38f * Mathf.SmoothStep(o.Shade.X, o.Shade.Y, sv);
            Bowl.Walk(path, sec.X, o.SeatW, (u, p, nrm) =>
            {
                // The strip's u at the front edge runs a little shorter or longer than at this
                // offset round the corners; close enough for aisles every 15 m.
                if (o.Aisles && Mathf.Abs(Mathf.PosMod(u, 15f) - 7.5f) > 7.5f - 0.6f) return;
                if (o.Vom.Y > o.Vom.X && sv > o.Vom.X - 0.4f && sv < o.Vom.Y + 0.2f && Mathf.Abs(Mathf.PosMod(u / 30f + 0.25f, 1f) - 0.5f) * 30f < 1.6f) return;
                int zone = o.Zone ?? ZoneAt(path, p);
                float fill = o.Fill + (Patch(u / 7f, k / 2.5f, seed) - 0.5f) * 0.32f + (zone != 0 ? 0.06f : 0);
                if (rng.NextDouble() > Mathf.Min(fill, 0.995f)) return;
                var pos = new Vector3(p.X, sec.Y, p.Z);
                var tifo = o.Tifo?.Invoke(pos, sv, zone) ?? new Vector2(-1, -1);
                Add(pos, new Vector3(-nrm.X, 0, -nrm.Y), zone, shade, tifo);
            });
        }
    }

    /// <summary>Smooth value noise in 0..1: where the gaps in a stand bunch up.</summary>
    static float Patch(float x, float y, int seed)
    {
        static float H(int i, int j, int s)
        {
            uint n = (uint)(i * 374761393 + j * 668265263 + s * 2147483647);
            n = (n ^ (n >> 13)) * 1274126177;
            return ((n ^ (n >> 16)) & 0xffff) / 65535f;
        }
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float a = Mathf.Lerp(H(ix, iy, seed), H(ix + 1, iy, seed), fx);
        float b = Mathf.Lerp(H(ix, iy + 1, seed), H(ix + 1, iy + 1, seed), fx);
        return Mathf.Lerp(a, b, fy);
    }

    static int ZoneAt(List<PathPt> path, Vector3 p)
    {
        // Nearest path point's zone (paths are short: a linear scan at build time is fine).
        int best = 0;
        float bd = float.MaxValue;
        for (int i = 0; i < path.Count; i++)
        {
            float dx = path[i].X - p.X, dz = path[i].Z - p.Z;
            // Compare along the normal-free plane: distance to the line through the point.
            float d = Mathf.Abs(dx * path[i].NZ - dz * path[i].NX) + 0.01f * (dx * dx + dz * dz);
            if (d < bd) { bd = d; best = i; }
        }
        return path[best].Zone;
    }

    /// <summary>Someone working at the match, on the flat round the pitch (zone 3): a steward
    /// watching the crowd, a photographer crouched behind the goal line, a ball boy.</summary>
    public enum Role { Steward, Photographer, BallBoy }

    public void Staff(Vector3 at, Vector3 face, Role role) => Add(at, face, 3, 1f, new Vector2(-1, -1), (int)role / 4f + 0.01f);

    /// <summary>Spots in the stands, a few hundred picked at random (for the rain's steam).</summary>
    public Vector3[] Sample(int n)
    {
        var rng = new Random(77);
        int fans = _v.Count / 4;
        if (fans == 0) return Array.Empty<Vector3>();
        var a = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            int f = rng.Next(fans) * 4;
            if (_c[f].G > 0.7f) { i--; continue; }
            a[i] = _v[f];
        }
        return a;
    }

    /// <summary>One person. COLOR = (role / 4 for staff, zone / 4, roof shade, sun visibility).</summary>
    void Add(Vector3 root, Vector3 face, int zone, float shade, Vector2 tifo, float role = 0)
    {
        int i0 = _v.Count;
        var col = new Color(role, zone * 0.25f, shade, 1f);
        for (int c = 0; c < 4; c++)
        {
            _v.Add(root);
            _n.Add(face);
            _uv.Add(new Vector2((c & 1) == 0 ? -0.5f : 0.5f, c < 2 ? 0f : 1f));
            _uv2.Add(tifo);
            _c.Add(col);
        }
        _idx.Add(i0); _idx.Add(i0 + 2); _idx.Add(i0 + 1);
        _idx.Add(i0 + 1); _idx.Add(i0 + 2); _idx.Add(i0 + 3);
    }

    static ShaderMaterial Material()
    {
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/crowd.gdshader") };
        mat.SetShaderParameter("sprites", CrowdSprites.Texture());
        return mat;
    }

    /// <summary>Bakes each fan's share of the sun (through the stands) and builds the draw.</summary>
    public MeshInstance3D Build(Node3D root, LightBake bake)
    {
        if (bake != null)
            System.Threading.Tasks.Parallel.For(0, _v.Count / 4, i =>
            {
                int f = i * 4;
                float vis = bake.SunVisibility(_v[f] + new Vector3(0, 1.3f, 0) + _n[f] * 0.3f);
                for (int c = 0; c < 4; c++) { var col = _c[f + c]; col.A = vis; _c[f + c] = col; }
            });
        // In wedges round the pitch, so the camera only draws the stands it looks at (the
        // one under it and the ends out of shot are culled whole). All share one material.
        const int Wedges = 12;
        var mat = Material();
        var fans = new List<int>[Wedges];
        for (int w = 0; w < Wedges; w++) fans[w] = new List<int>();
        for (int f = 0; f < _v.Count; f += 4)
        {
            float a = Mathf.Atan2(_v[f].Z, _v[f].X) / Mathf.Tau + 0.5f;
            fans[Math.Min(Wedges - 1, (int)(a * Wedges))].Add(f);
        }
        MeshInstance3D first = null;
        foreach (var list in fans)
        {
            if (list.Count == 0) continue;
            int n = list.Count * 4;
            var v = new Vector3[n]; var nn = new Vector3[n]; var uv = new Vector2[n]; var uv2 = new Vector2[n]; var c = new Color[n];
            var idx = new int[list.Count * 6];
            for (int k = 0; k < list.Count; k++)
            {
                int f = list[k];
                for (int j = 0; j < 4; j++)
                {
                    v[k * 4 + j] = _v[f + j]; nn[k * 4 + j] = _n[f + j]; uv[k * 4 + j] = _uv[f + j]; uv2[k * 4 + j] = _uv2[f + j]; c[k * 4 + j] = _c[f + j];
                }
                int i0 = k * 4;
                idx[k * 6] = i0; idx[k * 6 + 1] = i0 + 2; idx[k * 6 + 2] = i0 + 1;
                idx[k * 6 + 3] = i0 + 1; idx[k * 6 + 4] = i0 + 2; idx[k * 6 + 5] = i0 + 3;
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = v;
            arrays[(int)Mesh.ArrayType.Normal] = nn;
            arrays[(int)Mesh.ArrayType.TexUV] = uv;
            arrays[(int)Mesh.ArrayType.TexUV2] = uv2;
            arrays[(int)Mesh.ArrayType.Color] = c;
            arrays[(int)Mesh.ArrayType.Index] = idx;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var mi = new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                // Fans are moved up to ~2 m by the shader.
                ExtraCullMargin = 3,
            };
            root.AddChild(mi);
            first ??= mi;
        }
        if (first == null) root.AddChild(first = new MeshInstance3D { MaterialOverride = mat });
        return first;
    }
}
