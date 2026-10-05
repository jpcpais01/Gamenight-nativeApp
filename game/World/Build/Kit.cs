using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds.Build;

/// <summary>The eight places round the pitch the club picks a stand for: the three stands the
/// camera faces, the near side behind it, and the four corners.</summary>
public enum Slot { Main, Home, Away, Near, HomeFar, AwayFar, HomeNear, AwayNear }

/// <summary>What a piece is: a side stand (along a touchline), an end (behind a goal), a corner.</summary>
public enum Kind { Side, End, Corner }

/// <summary>The club's stadium: which set each slot is built from (saved with the club).</summary>
public sealed class StadiumPlan
{
    /// <summary>Set index per <see cref="Slot"/>.</summary>
    public int[] Sets = { 0, 3, 2, 0, 3, 2, 3, 2 };

    public int Get(Slot s) => Sets != null && (int)s < Sets.Length ? Math.Clamp(Sets[(int)s], 0, Kit.Sets.Length - 1) : 0;

    public void Set(Slot s, int set)
    {
        if (Sets == null || Sets.Length < 8) Sets = (int[])new StadiumPlan().Sets.Clone();
        Sets[(int)s] = set;
    }

    /// <summary>What's round the ground (an index into Surroundings.Names).</summary>
    public int Area;

    /// <summary>The main colour chosen per set (0 its own, <see cref="Kit.ClubPaint"/> the club's).</summary>
    public uint[] Paint;

    public uint PaintOf(int set) => Paint != null && set < Paint.Length ? Paint[set] : 0;

    public void SetPaint(int set, uint col)
    {
        if (Paint == null || Paint.Length < Kit.Sets.Length) Array.Resize(ref Paint, Kit.Sets.Length);
        Paint[set] = col;
    }
}

/// <summary>One surface of a cross-section: from A to B (offset back from the stand's front
/// edge, height), in a colour and look, split N times across.</summary>
public readonly record struct Seg(Vector2 A, Vector2 B, uint Col, Look Look, float Par, int N);

/// <summary>
/// A stand's cross-section at one point of its front edge. The solid part is drawn as a chain
/// (<see cref="To"/>) that starts at the foot of the pitch wall and ends on the ground at the
/// back; it is also the outline of the end caps. Roofs and other sheets hang off it as loose
/// surfaces (<see cref="Sheet"/>). Every section a set makes has the same surfaces in the same
/// order whatever its height, so a corner can sweep from one height to another.
/// </summary>
public sealed class Section
{
    public readonly List<Seg> Segs = new();
    public readonly List<Vector2> Outline = new() { Vector2.Zero };
    public readonly List<(Vector2 a, Vector2 b, TierFans fans)> Tiers = new();
    Vector2 _pen;

    /// <summary>Height of the stand (its roof's front edge or its top), where the roof's back and front are.</summary>
    public float Top, Back, BackH, Edge;
    /// <summary>Where the upper tier ends (offset).</summary>
    public float Ue;

    /// <summary>The roof's height at offset o (a straight sheet from its back to its edge).</summary>
    public float RoofAt(float o) => BackH + (o - Back) / (Edge - Back) * (Top - BackH);

    /// <summary>Carries the solid outline on to p. `flip` runs the surface's v from p back
    /// (so walls painted bottom-up can be drawn top-down).</summary>
    public Section To(Vector2 p, uint col, Look look = Look.Plain, float par = 0, int n = 1, bool flip = false)
    {
        Segs.Add(flip ? new Seg(p, _pen, col, look, par, n) : new Seg(_pen, p, col, look, par, n));
        Outline.Add(p);
        _pen = p;
        return this;
    }

    /// <summary>A loose surface (a roof, a fascia).</summary>
    public Section Sheet(Vector2 a, Vector2 b, uint col, Look look = Look.Plain, float par = 0, int n = 1)
    {
        Segs.Add(new Seg(a, b, col, look, par, n));
        return this;
    }

    public Section Fans(Vector2 a, Vector2 b, TierFans fans)
    {
        Tiers.Add((a, b, fans));
        return this;
    }
}

/// <summary>
/// The connection grammar every set is built to, so any mix of sets joins up:
/// - one footprint: a rectangle round the pitch with 16 m quadrant corners; every piece is a
///   cross-section swept along its stretch of that front edge;
/// - one lower tier all the way round: the 1.4 m pitch wall, the step, the rake up to the
///   concourse at 20 m back and 11.5 m up (each set dresses it: seats, a standing terrace,
///   painted or stone walls);
/// - above the concourse each set does what it likes, up to a height (its top) chosen per
///   piece from the set's own range;
/// - corners sweep their top from one neighbour's to the other's, and every piece closes its
///   ends with caps, so a taller neighbour shows its flank as a real stand would.
/// </summary>
public static class Kit
{
    public const float HL = 52.5f, HW = 34f;
    /// <summary>The front edge: half length, half width and corner radius.</summary>
    public const float BX = HL + 8.5f, BZ = HW + 7.5f, R = 16f;
    public const float CX = BX - R, CZ = BZ - R;
    public static readonly Vector2 Lower0 = new(0.4f, 1.4f), Lower1 = new(20, 11.5f);
    /// <summary>Rise per metre back of the lower tier.</summary>
    public const float Rake = 10.1f / 19.6f;
    public static float LowerAt(float o) => Lower0.Y + (o - Lower0.X) * Rake;

    public const uint Concrete = 0x8b8f96, DarkConcrete = 0x5c6068, Steel = 0x4a5058;

    public static readonly StandSet[] Sets = { new Arena(), new Terrace(), new Curva(), new TheWall(), new Citadel(),
        new Harbour(), new Pagoda(), new Deco(), new Crater(), new Orbital(),
        new Membrane(), new Brutalist(), new Barrio(), new Timber(), new Lumen() };

    /// <summary>Which way the builder's camera looks from to see a slot (yaw round the pitch).</summary>
    public static float ViewAngle(Slot s) => s switch
    {
        Slot.Main => 0,
        Slot.Home => Mathf.Pi / 2,
        Slot.Away => -Mathf.Pi / 2,
        Slot.Near => Mathf.Pi,
        Slot.HomeFar => Mathf.Pi / 4,
        Slot.AwayFar => -Mathf.Pi / 4,
        Slot.HomeNear => 3 * Mathf.Pi / 4,
        _ => -3 * Mathf.Pi / 4,
    };

    public static Kind KindOf(Slot s) => s switch
    {
        Slot.Main or Slot.Near => Kind.Side,
        Slot.Home or Slot.Away => Kind.End,
        _ => Kind.Corner,
    };

    public static readonly string[] SlotNames = { "Main stand", "Home end", "Away end", "Near side", "Home corner, far", "Away corner, far", "Home corner, near", "Away corner, near" };

    /// <summary>The colours a set's main colour can be changed to (0 keeps its own; ClubPaint is the club's).</summary>
    public const uint ClubPaint = 1;
    public static readonly uint[] Paints =
    {
        0, ClubPaint, 0xeceae4, 0x2a2c33, 0x8c8f95, 0xb8322a, 0x7a1f3a, 0xd8702a, 0xe0b030, 0xd8c49a,
        0x2e7d4f, 0x2f9c9a, 0x6fb3e0, 0x2f5fb8, 0x1f2c5c, 0x6a3fa0,
    };

    /// <summary>How a set's colours change when its main colour is `want`: each of its main
    /// colours keeps its lightness against the first (darker ones a shade of `want`, lighter
    /// ones `want` washed toward white), everything else is untouched.</summary>
    public static Func<uint, uint> Repaint(StandSet set, uint want)
    {
        if (want == 0 || set == null || set.Mains.Length == 0) return null;
        var mains = set.Mains;
        float refL = Mathf.Max(Lum(mains[0]), 0.02f);
        var map = new Dictionary<uint, uint>();
        foreach (var c in mains)
        {
            float k = Lum(c) / refL;
            map[c] = k <= 1 ? Darken(want, k) : Mix(want, 0xffffff, 1 - 1 / k);
        }
        return c => map.TryGetValue(c, out var o) ? o : c;
    }

    static float Lum(uint c) => (0.3f * ((c >> 16) & 255) + 0.59f * ((c >> 8) & 255) + 0.11f * (c & 255)) / 255f;

    static uint Mix(uint a, uint b, float t)
    {
        uint Ch(int sh) => (uint)Mathf.Round(Mathf.Lerp((a >> sh) & 255, (b >> sh) & 255, t)) << sh;
        return Ch(16) | Ch(8) | Ch(0);
    }

    public static uint Darken(uint hex, float k) =>
        ((uint)Mathf.Min(255, ((hex >> 16) & 255) * k) << 16) | ((uint)Mathf.Min(255, ((hex >> 8) & 255) * k) << 8) | (uint)Mathf.Min(255, (hex & 255) * k);

    public static float Smooth(float t)
    {
        t = Mathf.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>A stand set: one character, built to the grammar in <see cref="Kit"/>.</summary>
public abstract class StandSet
{
    public abstract string Name { get; }
    public abstract string About { get; }
    /// <summary>Its colour on the builder's plan.</summary>
    public abstract uint Swatch { get; }
    /// <summary>Its own height for a side, an end and a corner.</summary>
    public abstract float Natural(Kind k);
    /// <summary>How low and how high it can go (a corner meeting a neighbour).</summary>
    public abstract Vector2 Range { get; }
    /// <summary>Its main colours, the first the one the builder shows: the club can change them.</summary>
    public abstract uint[] Mains { get; }

    /// <summary>The pitch wall's colour and look.</summary>
    public virtual (uint col, Look look) Front(BuiltGround g) => (g.WallCol, Look.Wall);
    /// <summary>The lower tier's colour and tier look (0 a terrace, 1 aisles, 2 aisles and vomitories).</summary>
    public virtual (uint col, float par) Lower(BuiltGround g) => (g.Seat, 2);
    /// <summary>Fans on the lower tier.</summary>
    public virtual TierFans LowerFans() => new() { Vom = new(7.2f, 10.2f) };
    /// <summary>The flank of the stand (its end caps).</summary>
    public virtual (uint col, Look look) Cap => (Kit.DarkConcrete, Look.Plain);

    /// <summary>Everything above the concourse (the pen is at 20 m back, 11.5 m up): must end on the ground.</summary>
    public abstract void Upper(Section x, float top, Kind kind, BuiltGround g);

    /// <summary>Where it hangs the club's giant tifo as the main stand (offset, top of the
    /// cloth), or null if it can't carry one.</summary>
    public virtual Vector2? TifoAt(Section x) => null;
    public bool CarriesTifo => TifoAt(new Section { Top = Natural(Kind.Side), Edge = 8 }) != null;

    /// <summary>The details a sweep can't make: pillars, girders, lamps, towers, flags.</summary>
    public virtual void Dress(Piece p, BuiltGround g) { }
}

/// <summary>A stand or corner as built: its stretch of front edge, its sections, its set.</summary>
public sealed class Piece
{
    public Slot Slot;
    public Kind Kind;
    public StandSet Set;
    public List<PathPt> Path;
    public Section[] Sec;
    /// <summary>Built into the near mesh (the near side behind the camera, cut where it hides the pitch).</summary>
    public bool Hidden;
    /// <summary>Only part of the corner is built (the near corners stop where the camera is).</summary>
    public bool Partial;

    public MeshData Mesh(BuiltGround g) => Hidden ? g.Shadow : g.M;
    public int Mid => Path.Count / 2;

    /// <summary>Length of the front edge.</summary>
    public float Length
    {
        get
        {
            float l = 0;
            for (int i = 0; i + 1 < Path.Count; i++) l += new Vector2(Path[i + 1].X - Path[i].X, Path[i + 1].Z - Path[i].Z).Length();
            return l;
        }
    }

    /// <summary>Spots every `step` metres along the front edge (half a step in), each with the
    /// point's index, the blend to the next and its outward normal: `where` picks the place in
    /// the section (offset, height), which may differ from point to point.</summary>
    public void Each(float step, Func<Section, Vector2> where, Action<Vector3, Vector3, Section> at, float start = -1)
    {
        float u = 0, next = start < 0 ? step * 0.5f : start;
        for (int i = 0; i + 1 < Path.Count; i++)
        {
            var a = Path[i]; var b = Path[i + 1];
            float len = new Vector2(b.X - a.X, b.Z - a.Z).Length();
            while (next <= u + len)
            {
                float t = len > 0 ? (next - u) / len : 0;
                var wa = where(Sec[i]); var wb = where(Sec[i + 1]);
                var pos = a.At(wa.X, wa.Y).Lerp(b.At(wb.X, wb.Y), t);
                var n = new Vector3(Mathf.Lerp(a.NX, b.NX, t), 0, Mathf.Lerp(a.NZ, b.NZ, t)).Normalized();
                at(pos, n, t < 0.5f ? Sec[i] : Sec[i + 1]);
                next += step;
            }
            u += len;
        }
    }

    /// <summary>`count` spots spread evenly along the piece (at (k + 0.5) / count of its length).</summary>
    public void Spread(int count, Func<Section, Vector2> where, Action<Vector3, Vector3, Section> at)
    {
        float len = Length;
        if (count <= 0 || len <= 0) return;
        int k = 0;
        Each(len / count, where, (p, n, s) => { if (k++ < count) at(p, n, s); });
    }
}
