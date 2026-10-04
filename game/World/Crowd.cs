using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds;

/// <summary>How a tier is filled with fans.</summary>
public sealed class TierFans
{
    /// <summary>Seat width and row depth (along the slope), metres.</summary>
    public float SeatW = 0.62f, RowD = 0.8f;
    /// <summary>Share of seats taken.</summary>
    public float Fill = 0.92f;
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
/// The crowd: every fan is an upright card on his step, all of them in one static mesh (one
/// draw). The vertex shader dresses and animates each from a hash of where he stands: sitting
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
                if (rng.NextDouble() > o.Fill) return;
                int zone = o.Zone ?? ZoneAt(path, p);
                var pos = new Vector3(p.X, sec.Y, p.Z);
                var tifo = o.Tifo?.Invoke(pos, sv, zone) ?? new Vector2(-1, -1);
                Add(pos, new Vector3(-nrm.X, 0, -nrm.Y), zone, shade, tifo, o.SeatW);
            });
        }
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

    void Add(Vector3 root, Vector3 face, int zone, float shade, Vector2 tifo, float seatW)
    {
        int i0 = _v.Count;
        var col = new Color(seatW, zone * 0.5f, shade, 1f);
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
        var mesh = new ArrayMesh();
        if (_v.Count > 0)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = _n.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV2] = _uv2.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = _c.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = _idx.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
        var mi = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/crowd.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // Fans are moved up to ~2 m by the shader.
            ExtraCullMargin = 3,
        };
        root.AddChild(mi);
        return mi;
    }
}
