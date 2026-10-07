using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GameNight.Grounds.Build;

/// <summary>
/// The club's own stadium, put together from its plan: a stand from one of the five sets on
/// each side and each end, and in each corner. Every piece is a cross-section swept along its
/// stretch of the shared front edge (<see cref="Kit"/>), dressed by its set, then the whole
/// ground goes through the same bake and single draw as any other.
///
/// In a match the near side is behind the camera: only its low paddock is drawn, the stand
/// itself casts the evening shadow unseen (as at the big stadium). The builder's preview
/// draws it all.
/// </summary>
public sealed class BuiltGround : Ground
{
    readonly StadiumPlan _plan;
    public StadiumPlan Plan => _plan;
    readonly bool _preview;
    readonly List<Vector3> _seen = new(), _unseen = new();
    readonly List<PathPt> _front = new();

    public BuiltGround(StadiumPlan plan, bool preview)
    {
        _plan = plan ?? new StadiumPlan();
        _preview = preview;
        // Debug: `-- --plan=0,1,2,3,4,0,1,2` (a set per slot, in Slot order).
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--plan=")) _plan = new StadiumPlan { Sets = Array.ConvertAll(arg[7..].Split(','), int.Parse), Paint = _plan.Paint, Area = _plan.Area };
            // `--paint=0,b8322a,...` (a main colour per set, 0 its own, 1 the club's).
            if (arg.StartsWith("--area=")) _plan.Area = int.Parse(arg[7..]);
            if (arg.StartsWith("--paint=")) _plan.Paint = Array.ConvertAll(arg[8..].Split(','), h => Convert.ToUInt32(h, 16));
        }
    }

    public MeshData M => Static;
    /// <summary>Where the near side goes in a match: drawn, but cut away wherever it would
    /// stand between the camera and the pitch.</summary>
    public MeshData Shadow => Near;
    public uint Home => HomeColor;
    public uint Seat => Kit.Darken(HomeColor, 0.62f);
    public uint WallCol => Kit.Darken(HomeColor, 0.8f);
    /// <summary>A piece hung a big screen (the ground then shows the live score on it).</summary>
    public bool Screens;

    /// <summary>A floodlight bank (for the pitch's light pools; a glow if the camera can see it).</summary>
    public void AddLamp(Vector3 p, Piece piece) => (piece.Hidden || piece.Slot == Slot.Near ? _unseen : _seen).Add(p);

    protected override Vector3[] GlowSpots => _seen.ToArray();

    StandSet SetOf(Slot s) => Kit.Sets[_plan.Get(s)];

    /// <summary>Builds in this set's colours as the club chose them (null: as they are).</summary>
    void Painted(StandSet set)
    {
        uint want = set == null ? 0 : _plan.PaintOf(Array.IndexOf(Kit.Sets, set));
        Static.Paint = Near.Paint = Kit.Repaint(set, want == Kit.ClubPaint ? HomeColor : want);
    }

    float TopOf(Slot s) => Clamp(SetOf(s), SetOf(s).Natural(Kit.KindOf(s)));

    static float Clamp(StandSet set, float top) => Mathf.Clamp(top, set.Range.X, set.Range.Y);

    protected override void Setup()
    {
        // The builder's camera stands well back: thin the haze for it.
        // The mountains and the open country carry on further before the haze takes them.
        bool far = _plan.Area is 4 or 5 or 6 or 7 or 11 or 12 or 13 or 14 or 15;
        NoLand = _plan.Area == 11;
        Heaven = _plan.Area switch { 15 => 1, 14 => 2, _ => 0 };
        FogRange = _preview ? new(320, far ? 1700 : 1500) : far ? new(240, 1600) : new(180, 1200);
        // What's round it (Surroundings): its land, a long view.
        Land = Surroundings.LandOf(_plan.Area);
        ViewRange = 1800;
        // Room for the biggest sets' backs and towers in the sun's height map.
        BakeArea = new Rect2(-150, -140, 300, 280);
        Banners = new[]
        {
            new BannerArt(ClubName.ToUpperInvariant(), HomeColor, 0xf3eee2, 1),
            new BannerArt("ULTRAS", 0x14123a, HomeColor, 0),
            new BannerArt("OUR GROUND", 0xf3eee2, HomeColor, 2),
            new BannerArt("WE BUILT THIS", HomeColor, 0xf3eee2, 0),
            new BannerArt("GAMENIGHT", 0x14123a, 0xffd447, 2),
            new BannerArt("BIG NIGHT", 0xf3eee2, 0x14123a, 1),
            new BannerArt("ATLANTIC 1903", AwayColor, 0xf3eee2, 0),
            new BannerArt("ROVERS TILL I DIE", 0x14123a, 0xf3eee2, 1),
            new BannerArt("AWAY DAYS", AwayColor, 0xf3eee2, 2),
            new BannerArt("ONE CLUB · ONE NIGHT", HomeColor, 0xf3eee2, 2),
        };
    }

    // ---------------------------------------------------------------- the front edge

    static List<PathPt> Line(float x0, float z0, float x1, float z1, float nx, float nz, int zone)
    {
        var pts = new List<PathPt>();
        int n = Math.Max(1, (int)Mathf.Ceil(new Vector2(x1 - x0, z1 - z0).Length() / 4));
        for (int i = 0; i <= n; i++) pts.Add(new PathPt(x0 + (x1 - x0) * i / n, z0 + (z1 - z0) * i / n, nx, nz, zone));
        return pts;
    }

    /// <summary>Part of a corner's quarter circle, from angle a0 to a1 (multiples of pi); `q0`
    /// is where the quarter starts, so each point knows how far round it is (0..1).</summary>
    static (List<PathPt> pts, List<float> t) Arc(float ox, float oz, float a0, float a1, float q0, Func<float, int> zone)
    {
        var pts = new List<PathPt>();
        var ts = new List<float>();
        const float P = Mathf.Pi;
        int n = Math.Max(1, (int)Mathf.Ceil(Mathf.Abs(a1 - a0) * P / 0.12f));
        for (int i = 0; i <= n; i++)
        {
            float a = (a0 + (a1 - a0) * i / n) * P;
            float nx = Mathf.Cos(a), nz = Mathf.Sin(a);
            pts.Add(new PathPt(ox + nx * Kit.R, oz + nz * Kit.R, nx, nz, zone(a / P)));
            ts.Add((a / P - q0) / 0.5f);
        }
        return (pts, ts);
    }

    // ---------------------------------------------------------------- pieces

    Section SectionFor(StandSet set, float top, Kind kind)
    {
        var x = new Section();
        var (fc, fl) = set.Front(this);
        var (lc, lp) = set.Lower(this);
        x.To(new(0, 1.4f), fc, fl)
            .To(Kit.Lower0, Kit.Concrete)
            .To(Kit.Lower1, lc, Look.Tier, lp, 6);
        set.Upper(x, top, kind, this);
        return x;
    }

    Piece Straight(Slot slot, List<PathPt> path, bool hidden = false)
    {
        var set = SetOf(slot);
        var kind = Kit.KindOf(slot);
        var sec = SectionFor(set, TopOf(slot), kind);
        return new Piece { Slot = slot, Kind = kind, Set = set, Path = path, Hidden = hidden, Sec = path.Select(_ => sec).ToArray() };
    }

    /// <summary>A corner: its top eases from the neighbour at its start (t = 0) to its own and on
    /// to the neighbour at its end (t = 1), within what its set can do.</summary>
    Piece Corner(Slot slot, (List<PathPt> pts, List<float> t) arc, Slot from, Slot to, bool hidden = false, bool partial = false)
    {
        var set = SetOf(slot);
        float own = Clamp(set, set.Natural(Kind.Corner)), ta = TopOf(from), tb = TopOf(to);
        var sec = arc.t.Select(t =>
        {
            float wa = 1 - Kit.Smooth(t / 0.45f), wb = Kit.Smooth((t - 0.55f) / 0.45f);
            return SectionFor(set, Clamp(set, own + (ta - own) * wa + (tb - own) * wb), Kind.Corner);
        }).ToArray();
        return new Piece { Slot = slot, Kind = Kind.Corner, Set = set, Path = arc.pts, Hidden = hidden, Partial = partial, Sec = sec };
    }

    /// <summary>Sweeps a piece's sections along its path and closes its ends.</summary>
    static void Sweep(MeshData m, Piece p)
    {
        int segs = p.Sec[0].Segs.Count;
        for (int s = 0; s < segs; s++)
        {
            var g0 = p.Sec[0].Segs[s];
            m.Hex(g0.Col, g0.Look, g0.Par);
            float u = 0;
            for (int i = 0; i + 1 < p.Path.Count; i++)
            {
                Seg a = p.Sec[i].Segs[s], b = p.Sec[i + 1].Segs[s];
                PathPt p0 = p.Path[i], p1 = p.Path[i + 1];
                float u1 = u + (p1.At(b.A.X, b.A.Y) - p0.At(a.A.X, a.A.Y)).Length();
                float slope = (a.B - a.A).Length();
                for (int k = 0; k < a.N; k++)
                {
                    float f0 = k / (float)a.N, f1 = (k + 1) / (float)a.N;
                    Vector2 a0 = a.A.Lerp(a.B, f0), a1 = a.A.Lerp(a.B, f1), b0 = b.A.Lerp(b.B, f0), b1 = b.A.Lerp(b.B, f1);
                    m.QuadUV(p0.At(a0.X, a0.Y), p1.At(b0.X, b0.Y), p1.At(b1.X, b1.Y), p0.At(a1.X, a1.Y),
                        new(u, slope * f0), new(u1, slope * f0), new(u1, slope * f1), new(u, slope * f1));
                }
                u = u1;
            }
        }
        var (col, look) = p.Set.Cap;
        m.Hex(col, look);
        Bowl.Caps(m, new[] { p.Path[0] }, p.Sec[0].Outline.ToArray());
        Bowl.Caps(m, new[] { p.Path[^1] }, p.Sec[^1].Outline.ToArray());
    }

    protected override void Build()
    {
        var m = Static;
        const float P = 1;
        float cx = Kit.CX, cz = Kit.CZ;
        var pieces = new List<Piece>();
        Func<float, int> homeZone = _ => 1, awayZone = _ => 2;

        // The far side, the ends and the far corners: always seen.
        var home = Straight(Slot.Home, Line(-Kit.BX, cz, -Kit.BX, -cz, -1, 0, 1));
        var homeFar = Corner(Slot.HomeFar, Arc(-cx, -cz, P, 1.5f * P, 1, a => a < 1.22f ? 1 : 0), Slot.Home, Slot.Main);
        var main = Straight(Slot.Main, Line(-cx, -Kit.BZ, cx, -Kit.BZ, 0, -1, 0));
        var awayFar = Corner(Slot.AwayFar, Arc(cx, -cz, 1.5f * P, 2 * P, 1.5f, a => a > 1.78f ? 2 : 0), Slot.Main, Slot.Away);
        var away = Straight(Slot.Away, Line(Kit.BX, -cz, Kit.BX, cz, 1, 0, 2));
        // The near corners: in a match only the part the camera sees is drawn, the rest is
        // shadow; the builder shows them whole.
        Piece homeNear, awayNear;
        if (_preview)
        {
            homeNear = Corner(Slot.HomeNear, Arc(-cx, cz, 0.5f, 1, 0.5f, homeZone), Slot.Near, Slot.Home);
            awayNear = Corner(Slot.AwayNear, Arc(cx, cz, 2, 2.5f, 2, awayZone), Slot.Away, Slot.Near);
        }
        else
        {
            homeNear = Corner(Slot.HomeNear, Arc(-cx, cz, 0.7f, 1, 0.5f, homeZone), Slot.Near, Slot.Home, partial: true);
            awayNear = Corner(Slot.AwayNear, Arc(cx, cz, 2, 2.3f, 2, awayZone), Slot.Away, Slot.Near, partial: true);
            pieces.Add(Corner(Slot.HomeNear, Arc(-cx, cz, 0.5f, 0.7f, 0.5f, homeZone), Slot.Near, Slot.Home, hidden: true, partial: true));
            pieces.Add(Corner(Slot.AwayNear, Arc(cx, cz, 2.3f, 2.5f, 2, awayZone), Slot.Away, Slot.Near, hidden: true, partial: true));
        }
        var near = Straight(Slot.Near, Line(cx, Kit.BZ, -cx, Kit.BZ, 0, 1, 0), hidden: !_preview);
        var seen = new[] { homeNear, home, homeFar, main, awayFar, away, awayNear };
        pieces.AddRange(seen);
        pieces.Add(near);
        foreach (var p in seen) _front.AddRange(p.Path);

        foreach (var p in pieces)
        {
            Painted(p.Set);
            Sweep(p.Mesh(this), p);
            p.Set.Dress(p, this);
            Exterior.Dress(p, this);
        }
        Painted(null);

        // The giant tifo, from a main stand tall enough to hang it over the lower tier (cut down
        // to fit, never below two thirds of the big stadium's).
        if (main.Set.TifoAt(main.Sec[main.Mid]) is Vector2 at)
        {
            float h = Mathf.Min(38, at.Y - Kit.LowerAt(at.X) - 1.2f);
            if (h >= 25) Giant = new GiantTifo(main.Path[main.Mid], at.X, at.Y, h / 38);
        }

        Surroundings.Build(m, HomeColor, _plan.Area);
        Pitchside.Tunnel(m, -Kit.BZ, Kit.DarkConcrete);
        Pitchside.AdBoards(m);
        Pitchside.CornerFlags(m);
        Dugouts(m);
        RailBanners(m, _front, 20.9f, 13.7f, 34, 4.2f);
        HasScreen = Screens;

        // ---- the crowd
        float lowerSlope = (Kit.Lower1 - Kit.Lower0).Length();
        foreach (var p in pieces)
        {
            var fans = p.Set.LowerFans();
            fans.Near = p.Hidden;
            if (!p.Hidden && p.Slot is not (Slot.Main or Slot.Near))
                fans.Tifo = p.Set is Curva ? CurvaTifos(cz, lowerSlope) : EndTifos(cz, lowerSlope);
            Crowd.Tier(p.Path, Kit.Lower0, Kit.Lower1, fans);
            for (int j = 0; j < p.Sec[0].Tiers.Count; j++) UpperFans(p, j);
        }
        WaveFlags(_front, Kit.Lower0, Kit.Lower1);
        HoldBanner(home.Path[home.Mid], 6.5f, Kit.Lower0, Kit.Lower1, 10);
        HoldBanner(main.Path[(int)(main.Path.Count * 0.38f)], 4, Kit.Lower0, Kit.Lower1, 8);

        // The near side's banks light the grass from behind the camera.
        if (!_preview && _unseen.Count == 0)
            foreach (float x in new[] { -30f, 0, 30 }) _unseen.Add(new Vector3(x, TopOf(Slot.Near) - 2, Kit.BZ + 9));
        Lamps = _seen.Concat(_unseen).ToArray();
        GD.Print($"Built stadium: {string.Join(", ", Enum.GetValues<Slot>().Select(s => $"{s}={SetOf(s).Name}"))}");
    }

    /// <summary>Fans on an upper tier: one run where the tier's the same all along, else a run
    /// per stretch between path points (a corner that changes height).</summary>
    void UpperFans(Piece p, int j)
    {
        var (a, b, fans) = p.Sec[0].Tiers[j];
        fans.Near = p.Hidden;
        bool same = p.Sec.All(s => s.Tiers[j].a == a && s.Tiers[j].b == b);
        if (same)
        {
            Crowd.Tier(p.Path, a, b, fans);
            return;
        }
        fans.Aisles = false;
        for (int i = 0; i + 1 < p.Path.Count; i++)
        {
            var (a0, b0, _) = p.Sec[i].Tiers[j];
            var (a1, b1, _) = p.Sec[i + 1].Tiers[j];
            Crowd.Tier(new List<PathPt> { p.Path[i], p.Path[i + 1] }, (a0 + a1) / 2, (b0 + b1) / 2, fans);
        }
    }
}
