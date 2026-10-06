using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds.Build;

/// <summary>Bits the later sets share.</summary>
static class Shared
{
    /// <summary>A row of lamps under a roof's front (every `every` path points, `o` back from
    /// the edge and `down` under the top), and the lights they throw on the pitch.</summary>
    public static void RoofLamps(Piece p, BuiltGround g, float o, float down, float w = 1.5f, int every = 2)
    {
        var m = p.Mesh(g);
        m.Hex(0xffffff, Look.Lamp);
        for (int i = 0; i < p.Path.Count; i += every)
        {
            var pt = p.Path[i];
            var s = p.Sec[i];
            var c = pt.At(s.Edge + o, s.Top - down);
            var basis = new Basis(Vector3.Up, pt.FacePitch + Mathf.Pi) * new Basis(Vector3.Right, 0.8f);
            var bx = basis.X * w; var by = basis.Y * 0.45f;
            m.QuadUV(c - bx - by, c + bx - by, c + bx + by, c - bx + by, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }
        p.Spread(Mathf.Max(1, Mathf.RoundToInt(p.Length / 28)), s => new(s.Edge + o, s.Top - down - 0.2f), (pos, n, s) => g.AddLamp(pos, p));
    }

    /// <summary>Spots every `step` metres along the piece, collected (so members can join one to the next).</summary>
    public static List<(Vector3 pos, Vector3 n, Section s)> Spots(Piece p, float step, Func<Section, Vector2> where)
    {
        var l = new List<(Vector3, Vector3, Section)>();
        p.Each(step, where, (pos, n, s) => l.Add((pos, n, s)), 0);
        return l;
    }
}

/// <summary>
/// Nest: a red bowl wrapped in a woven lattice of silver steel. The girders cross and recross
/// up the outside, bulge out at mid-height and run on over the roof to its inner edge, a few
/// strays wandering through like twigs in a nest.
/// </summary>
public sealed class Nest : StandSet
{
    const uint Silver = 0xa3a8af, Red = 0x9e2b25, RoofCol = 0x3a3f46;
    public override string Name => "Nest";
    public override string About => "A woven steel lattice wrapped round a red bowl";
    public override uint Swatch => 0xb4b9c0;
    public override uint[] Mains => new uint[] { Silver, Red };
    public override Vector2? TifoAt(Section x) => new(x.Edge + 0.6f, x.Top - 2.6f);
    public override float Natural(Kind k) => k == Kind.Side ? 40 : k == Kind.End ? 38 : 37;
    public override Vector2 Range => new(32, 46);
    public override TierFans LowerFans() => new() { Vom = new(7.2f, 10.2f), Shade = new(9, 17) };
    public override (uint col, Look look) Cap => (Red, Look.Plain);
    public override Outside Outside => new(0x5e2420, Look.Plain, 0, Stairs.None, 0);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float uh = T - 6, ue = 21.5f + (uh - 15.8f) / 0.8f;
        x.Top = T; x.Ue = ue; x.Back = ue + 4; x.BackH = T + 1; x.Edge = 10;
        x.To(new(22, 11.5f), Kit.Concrete)
            .To(new(22, 14.5f), 0x2a3440, Look.Glass)
            .To(new(21, 14.5f), Kit.Concrete)
            .To(new(21, 15.8f), g.WallCol, Look.Wall)
            .To(new(21.5f, 15.8f), Kit.Concrete)
            .To(new(ue, uh), g.Seat, Look.Tier, 1, 6)
            .To(new(ue, T), Red)
            .To(new(x.Back, T + 1), Red)
            .To(new(x.Back, 0), Red, Look.Curtain, flip: true);
        x.Sheet(new(x.Back, T + 0.6f), new(x.Edge, T), RoofCol, Look.Roof)
            .Sheet(new(x.Edge + 0.3f, T - 0.25f), new(x.Edge + 4, T - 0.05f), 0xffffff, Look.RoofLight);
        x.Fans(new(21.5f, 15.8f), new(ue, uh), new TierFans { Shade = new(-2, 9) });
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        Shared.RoofLamps(p, g, 1.4f, 1.6f);
        // The lattice stands 3 m off the red wall: feet on the ground, a bulge at mid-height,
        // a crown above the roof's back and a rim at its front edge.
        var f = Shared.Spots(p, 7, s => new(s.Back + 3, 0));
        int n = f.Count;
        if (n < 2) return;
        var up = Vector3.Up;
        Vector3 Foot(int k) => f[k].pos;
        Vector3 Mid(int k) => f[k].pos + f[k].n * 1.6f + up * (f[k].s.Top * 0.5f);
        Vector3 Crown(int k) => f[k].pos + up * (f[k].s.Top + 2.4f);
        Vector3 Rim(int k) => f[k].pos - f[k].n * (f[k].s.Back + 3 - f[k].s.Edge - 1) + up * (f[k].s.Top + 1.4f);
        m.Hex(Silver);
        for (int k = 0; k < n; k++)
        {
            // The great crossing girders, each over two bays.
            if (k + 2 < n)
            {
                m.Beam(Foot(k), Mid(k + 1), 1.1f, 1.1f);
                m.Beam(Mid(k + 1), Crown(k + 2), 1.1f, 1.1f);
                m.Beam(Foot(k + 2), Mid(k + 1), 1.1f, 1.1f);
                m.Beam(Mid(k + 1), Crown(k), 1.1f, 1.1f);
            }
            // Over the roof, criss-cross to the rim.
            if (k + 1 < n)
            {
                m.Beam(Crown(k), Rim(k + 1), 0.9f, 0.9f);
                m.Beam(Crown(k + 1), Rim(k), 0.9f, 0.9f);
                m.Beam(Crown(k), Crown(k + 1), 0.8f, 0.8f);
                m.Beam(Rim(k), Rim(k + 1), 0.8f, 0.8f);
            }
            // The strays: thinner members at odd angles, three bays long.
            float h = Hash.At(Foot(k), 5);
            if (k + 3 < n && h < 0.6f)
                m.Beam(h < 0.3f ? Foot(k) : Mid(k), h < 0.3f ? Mid(k + 3) : Crown(k + 3), 0.55f, 0.55f);
            if (k >= 3 && h > 0.45f)
                m.Beam(Mid(k), Foot(k - 3) + up * (f[k - 3].s.Top * 0.2f), 0.55f, 0.55f);
        }
    }
}

/// <summary>
/// Arch: two tiers with a band of boxes between, under a big roof, and one vast white lattice
/// arch over the whole stand holding the roof's front on cables. Only the first of the main
/// stand and the two ends built from it carries the arch (a ground has one).
/// </summary>
public sealed class ArchStand : StandSet
{
    const uint White = 0xf2f3f4, Facade = 0x3c4450, RoofCol = 0x2e333a, Panels = 0xb7c3cc;
    public override string Name => "Arch";
    public override string About => "Two tiers and a huge white arch that holds the roof";
    public override uint Swatch => 0xe4e8ec;
    public override uint[] Mains => new uint[] { Facade, RoofCol };
    public override Vector2? TifoAt(Section x) => new(x.Edge + 0.6f, x.Top - 2.6f);
    public override float Natural(Kind k) => k == Kind.Side ? 42 : k == Kind.End ? 40 : 38;
    public override Vector2 Range => new(34, 48);
    public override TierFans LowerFans() => new() { Vom = new(7.2f, 10.2f), Shade = new(9, 17) };
    public override (uint col, Look look) Cap => (Facade, Look.Curtain);
    public override Outside Outside => new(0x2c323a, Look.Plain, 0, Stairs.Drum, 0xd8dbe0);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float uh = T - 5, ue = 31 + (uh - 26.2f) / 0.78f;
        x.Top = T; x.Ue = ue; x.Back = ue + 2; x.BackH = T - 0.6f; x.Edge = 6;
        x.To(new(22, 11.5f), Kit.Concrete)
            .To(new(22, 14.5f), 0x2a3440, Look.Glass)
            .To(new(21, 14.5f), Kit.Concrete)
            .To(new(21, 15.8f), White, Look.Ribbon)
            .To(new(21.5f, 15.8f), Kit.Concrete)
            .To(new(31.5f, 21.8f), g.Seat, Look.Tier, 1, 4)
            .To(new(31.5f, 25), White, Look.Glass)
            .To(new(30.5f, 25), Kit.Concrete)
            .To(new(30.5f, 26.2f), White, Look.Ribbon)
            .To(new(31, 26.2f), Kit.Concrete)
            .To(new(ue, uh), g.Seat, Look.Tier, 1, 4)
            .To(new(ue, T - 1.2f), Kit.DarkConcrete)
            .To(new(x.Back, x.BackH), Kit.DarkConcrete)
            .To(new(x.Back, 0), Facade, Look.Curtain, flip: true);
        float panel = 12;
        x.Sheet(new(x.Back, x.BackH), new(panel, x.RoofAt(panel)), RoofCol, Look.Roof)
            .Sheet(new(panel, x.RoofAt(panel)), new(x.Edge, T), Panels)
            .Sheet(new(x.Edge, T - 2), new(x.Edge, T), White, Look.Fascia)
            .Sheet(new(x.Edge + 0.3f, T - 2.05f), new(x.Edge + 3.4f, T - 1.85f), 0xffffff, Look.RoofLight);
        x.Fans(new(21.5f, 15.8f), new(31.5f, 21.8f), new TierFans { Shade = new(6, 14) });
        x.Fans(new(31, 26.2f), new(ue, uh), new TierFans { Shade = new(-2, 8) });
    }

    /// <summary>The slot whose stand carries the arch: the main stand if it's this set, else an end.</summary>
    Slot? ArchSlot(BuiltGround g)
    {
        int me = Array.IndexOf(Kit.Sets, this);
        foreach (var s in new[] { Slot.Main, Slot.Home, Slot.Away })
            if (g.Plan.Get(s) == me) return s;
        return null;
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        Shared.RoofLamps(p, g, 1.3f, 2.4f);
        if (p.Kind == Kind.Corner || p.Hidden || p.Partial || p.Slot != ArchSlot(g)) return;

        // The arch springs from concrete feet well beyond each end of the stand.
        PathPt a0 = p.Path[0], a1 = p.Path[^1];
        Section s0 = p.Sec[0], s1 = p.Sec[^1], ms = p.Sec[p.Mid];
        var along = new Vector3(a1.X - a0.X, 0, a1.Z - a0.Z).Normalized();
        var A = a0.At(s0.Back + 6, 0) - along * 14;
        var B = a1.At(s1.Back + 6, 0) + along * 14;
        var mid = p.Path[p.Mid];
        var outN = new Vector3(mid.NX, 0, mid.NZ);
        float H = ms.Top + 30;
        // Leaning in over the roof so its cables reach the front edge.
        var lift = Vector3.Up * H - outN * (ms.Back + 6 - ms.Edge) * 0.55f;
        Vector3 At(float t) => A.Lerp(B, t) + lift * Mathf.Sin(Mathf.Pi * t);
        var plane = (B - A).Normalized().Cross(lift.Normalized()).Normalized();

        m.Hex(Kit.Concrete);
        foreach (var foot in new[] { A, B })
            m.Box(new Transform3D(Basis.Identity, foot + new Vector3(0, 1.5f, 0)), new Vector3(7, 3, 7));

        // A triangular lattice tube: three chords, rings and diagonals.
        const int N = 26;
        const float Rr = 2.1f;
        m.Hex(White);
        Vector3[] Ring(int i)
        {
            float t = i / (float)N;
            var c = At(t);
            var tan = (At(Mathf.Min(1, t + 0.01f)) - At(Mathf.Max(0, t - 0.01f))).Normalized();
            var b = tan.Cross(plane).Normalized();
            var r = new Vector3[3];
            for (int k = 0; k < 3; k++)
            {
                float ang = Mathf.Tau * k / 3 + 0.5f;
                r[k] = c + (plane * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * Rr;
            }
            return r;
        }
        var prev = Ring(0);
        for (int i = 1; i <= N; i++)
        {
            var cur = Ring(i);
            for (int k = 0; k < 3; k++)
            {
                m.Beam(prev[k], cur[k], 0.5f, 0.5f);
                m.Beam(cur[k], cur[(k + 1) % 3], 0.22f, 0.22f);
                m.Beam(prev[k], cur[(k + 1) % 3], 0.18f, 0.18f);
            }
            prev = cur;
        }

        // Cables down from the arch to the roof's front edge and its back.
        m.Hex(0xc8ccd2);
        for (int i = 3; i <= N - 3; i += 2)
        {
            float t = i / (float)N;
            var top = At(t) - Vector3.Up * Rr;
            int j = Mathf.Clamp(Mathf.RoundToInt(t * (p.Path.Count - 1)), 0, p.Path.Count - 1);
            var s = p.Sec[j];
            m.Beam(top, p.Path[j].At(s.Edge + 1, s.Top + 0.3f), 0.2f, 0.2f);
            if (i % 4 == 1) m.Beam(top, p.Path[j].At(s.Back - 2, s.RoofAt(s.Back - 2) + 0.3f), 0.2f, 0.2f);
        }
    }
}

/// <summary>
/// Neon: a sheer black box striped with neon tubes that glow in the club's colour, a wave of
/// light running along them (racing after a goal). A single steep tier, a flat roof behind a
/// deep neon fascia, white light fins down the back, black towers in the corners.
/// </summary>
public sealed class Neon : StandSet
{
    const uint Black = 0x18191d, Panel = 0x25272d;
    public override string Name => "Neon";
    public override string About => "A black box striped with neon in your colours";
    public override uint Swatch => 0x3b3e48;
    public override uint[] Mains => new uint[] { Panel, Black };
    public override Vector2? TifoAt(Section x) => new(x.Edge + 0.6f, x.Top - 4.2f);
    public override float Natural(Kind k) => k == Kind.Side ? 34 : k == Kind.End ? 32 : 30;
    public override Vector2 Range => new(26, 42);
    public override (uint col, Look look) Front(BuiltGround g) => (0xffffff, Look.Ribbon);
    public override TierFans LowerFans() => new() { Vom = new(7.2f, 10.2f), Shade = new(9, 17) };
    public override (uint col, Look look) Cap => (Black, Look.Neon);
    public override Outside Outside => new(Black, Look.Neon, 1, Stairs.Tower, Panel);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float uh = T - 6, ue = 21.5f + (uh - 15.8f) / 0.82f;
        x.Top = T; x.Ue = ue; x.Back = ue + 2; x.BackH = T + 0.5f; x.Edge = 7;
        x.To(new(22, 11.5f), Panel)
            .To(new(22, 14.5f), 0x2a3440, Look.Glass)
            .To(new(21, 14.5f), Panel)
            .To(new(21, 15.8f), 0xffffff, Look.Ribbon)
            .To(new(21.5f, 15.8f), Panel)
            .To(new(ue, uh), g.Seat, Look.Tier, 1, 6)
            .To(new(ue, T - 1.5f), Black)
            .To(new(x.Back, T + 0.5f), Black)
            .To(new(x.Back, 0), Black, Look.Neon, flip: true);
        // A flat roof behind a deep black fascia striped like the walls.
        x.Sheet(new(x.Back, T - 0.9f), new(x.Edge, T - 0.9f), Panel, Look.Roof)
            .Sheet(new(x.Edge, T - 3.6f), new(x.Edge, T + 0.5f), Black, Look.Neon)
            .Sheet(new(x.Edge, T + 0.5f), new(x.Back, T + 0.5f), Panel)
            .Sheet(new(x.Edge + 0.3f, T - 3.65f), new(x.Edge + 3.2f, T - 3.45f), 0xffffff, Look.RoofLight);
        x.Fans(new(21.5f, 15.8f), new(ue, uh), new TierFans { Shade = new(-2, 10) });
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        Shared.RoofLamps(p, g, 1.2f, 4.4f);
        // White light fins standing off the back wall.
        p.Each(24, s => new(s.Back + 0.7f, s.Top * 0.5f), (pos, n, s) =>
        {
            m.Hex(0x0f1013, Look.Neon, 1);
            m.Box(new Transform3D(Parts.Facing(-n), pos), new Vector3(0.7f, s.Top + 0.5f, 1.4f));
        });
        if (p.Kind != Kind.Corner || p.Partial) return;
        // A black tower in the corner, its neon running up to a lamp crown.
        var mid = p.Path[p.Mid];
        var ms = p.Sec[p.Mid];
        var face = new Vector3(-mid.NX, 0, -mid.NZ);
        var c = mid.At(ms.Back + 6, 0);
        float h = ms.Top + 14;
        m.Hex(Black, Look.Neon);
        m.Box(new Transform3D(Parts.Facing(face), c + new Vector3(0, h / 2, 0)), new Vector3(8, h, 8), 63 & ~8);
        m.Hex(Panel);
        m.Box(new Transform3D(Parts.Facing(face), c + new Vector3(0, h + 0.4f, 0)), new Vector3(9, 0.8f, 9));
        g.AddLamp(Parts.LampBank(m, c + new Vector3(0, h + 3, 0) + face * 3, face, 8, 3.2f, 0.7f), p);
    }
}

/// <summary>
/// Adobe: the Sahel ground of sun-baked mud brick. One open tier behind a parapet, the back
/// wall ribbed with tapering buttresses that rise into pinnacles, timber beams sticking out of
/// the walls in rows (the scaffolding for each year's replastering), a minaret-like lamp tower
/// in each corner, striped shade cloths over the main stand.
/// </summary>
public sealed class Adobe : StandSet
{
    const uint Mud = 0xbf8b58, MudDark = 0x9c6c42, Wood = 0x4f3420, Steps = 0xa97a4c;
    public override string Name => "Adobe";
    public override string About => "Sun-baked mud brick, pinnacles and timber beams";
    public override uint Swatch => 0xcf9a62;
    public override uint[] Mains => new uint[] { Mud, MudDark };
    public override float Natural(Kind k) => k == Kind.Side ? 22 : k == Kind.End ? 20 : 19;
    public override Vector2 Range => new(16, 28);
    public override (uint col, float par) Lower(BuiltGround g) => (Steps, 1);
    public override TierFans LowerFans() => new() { Fill = 0.95f };
    public override (uint col, Look look) Cap => (Mud, Look.Plain);
    public override Outside Outside => new(MudDark, Look.Plain, 0, Stairs.Tower, Mud);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float uh = T - 2.5f, ue = 21 + (uh - 12.7f) / 0.7f;
        x.Top = T; x.Ue = ue; x.Back = ue + 1.4f; x.BackH = T + 1.2f; x.Edge = ue;
        x.To(new(20.6f, 11.5f), Mud)
            .To(new(20.6f, 12.7f), g.WallCol, Look.Wall)
            .To(new(21, 12.7f), Mud)
            .To(new(ue, uh), Steps, Look.Tier, 1, 4)
            .To(new(ue, T + 1.2f), Mud)
            .To(new(x.Back, T + 1.2f), MudDark)
            .To(new(x.Back, 0), Mud, flip: true);
        x.Fans(new(21, 12.7f), new(ue, uh), new TierFans { Fill = 0.95f });
    }

    /// <summary>The tier's height `o` back from the front edge.</summary>
    static float TierY(Section s, float o) => 12.7f + (o - 21) * 0.7f;

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        var up = Vector3.Up;
        // Buttresses up the back, each rising into a pinnacle over the parapet.
        p.Each(7, s => new(s.Back + 0.9f, 0), (pos, n, s) =>
        {
            float h = s.Top + 3.2f;
            m.Hex(0xd29c68);
            m.Column(pos, 1.9f, 0.9f, h, 4, Mathf.Pi / 4);
            m.Column(pos + up * h, 0.9f, 0, 2.6f, 4, Mathf.Pi / 4);
        });
        // The beams poking out of the wall in rows.
        m.Hex(Wood);
        p.Each(3, s => new(s.Back, 0), (pos, n, s) =>
        {
            for (float y = 3.4f; y < s.Top - 0.5f; y += 3.6f)
                m.Beam(pos + up * y - n * 0.2f, pos + up * y + n * 1.1f, 0.25f, 0.25f);
        }, 1.5f);
        // Floodlights on timber posts along the parapet.
        p.Spread(Mathf.Max(1, Mathf.RoundToInt(p.Length / 22)), s => new(s.Back - 0.7f, s.Top + 1.2f), (pos, n, s) =>
        {
            m.Hex(Wood);
            m.Beam(pos, pos + up * 6, 0.4f, 0.4f);
            g.AddLamp(Parts.LampBank(m, pos + up * 6.4f - n * 0.4f, -n, 4, 1.8f, 0.75f), p);
        });

        var mid = p.Path[p.Mid];
        var ms = p.Sec[p.Mid];
        var face = new Vector3(-mid.NX, 0, -mid.NZ);
        var basis = Parts.Facing(face);
        if (p.Kind == Kind.Corner)
        {
            if (p.Partial) return;
            // The lamp tower: a tapering mud tower ribbed with beams, a crown of pinnacles.
            var c = mid.At(ms.Back + 6, 0);
            float h = ms.Top + 15;
            m.Hex(Mud);
            m.Column(c, 5, 3.4f, h, 4, Mathf.Pi / 4);
            m.Hex(MudDark);
            m.Column(c + up * h, 3.6f, 3.6f, 1.2f, 4, Mathf.Pi / 4);
            m.Hex(Mud);
            for (int i = 0; i < 4; i++)
            {
                var corner = c + up * (h + 1.2f) + new Basis(up, i * Mathf.Pi / 2) * new Vector3(2.5f, 0, 0);
                m.Column(corner, 0.6f, 0, 2.4f, 4, Mathf.Pi / 4);
            }
            m.Hex(Wood);
            for (float y = 3; y < h - 1; y += 3.4f)
                for (int i = 0; i < 4; i++)
                {
                    var d = new Basis(up, i * Mathf.Pi / 2 + Mathf.Pi / 4) * new Vector3(1, 0, 0);
                    float r = Mathf.Lerp(5, 3.4f, y / h) * 0.72f;
                    m.Beam(c + up * y + d * (r - 0.3f), c + up * y + d * (r + 1.1f), 0.25f, 0.25f);
                }
            g.AddLamp(Parts.LampBank(m, c + up * (h + 4.4f) + face * 2, face, 6, 2.6f, 0.7f), p);
            return;
        }
        if (p.Slot != Slot.Main || p.Hidden) return;
        // Shade cloths over the main stand: striped in the club's colour and sand, sloping
        // from the parapet to posts on the tier.
        int k = 0;
        p.Each(9, s => new(s.Back - 0.6f, s.Top + 2.6f), (back, n, s) =>
        {
            float o = s.Ue - 8.5f, fy = TierY(s, o);
            var fpost = back - n * (s.Back - 0.6f - o);
            fpost.Y = fy;
            var side = new Vector3(n.Z, 0, -n.X) * 4.3f;
            var front = fpost + up * 4.6f;
            m.Hex(k++ % 2 == 0 ? g.Home : 0xe9dfc8);
            m.Quad(back - side, back + side, front + side, front - side, 8.6f, 9);
            m.Hex(Wood);
            m.Beam(fpost, front, 0.3f, 0.3f);
        }, 4.5f);
    }
}

/// <summary>
/// Meadow: football in the fields. Grass terraces round the pitch behind a white rail, a grass
/// bank up to a post-and-rail fence and trees along the top, slim floodlight poles, a little
/// green tin shed of a stand on the main side, big old trees in the corners and a hedge outside.
/// </summary>
public sealed class Meadow : StandSet
{
    const uint Grass = 0x5f8a3a, Bank = 0x6c9642, Rail = 0xebe7dc, Gravel = 0xb8ad94, Wood = 0x6e4c2e, Tin = 0x3f6b4a;
    public override string Name => "Meadow";
    public override string About => "Grass banks, a white rail and trees: a ground in the fields";
    public override uint Swatch => 0x82b24c;
    public override uint[] Mains => new uint[] { Rail };
    public override float Natural(Kind k) => k == Kind.Side ? 13 : k == Kind.End ? 12.5f : 12;
    public override Vector2 Range => new(12, 16);
    public override (uint col, Look look) Front(BuiltGround g) => (Rail, Look.Plain);
    public override (uint col, float par) Lower(BuiltGround g) => (Grass, 0);
    public override TierFans LowerFans() => new() { Fill = 0.5f, Aisles = false };
    public override (uint col, Look look) Cap => (Grass, Look.Plain);
    public override Outside Outside => new(0x3f6a32, Look.Plain, 0, Stairs.None, 0);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        x.Top = T; x.Ue = 20.4f; x.Back = 24; x.BackH = T; x.Edge = 20.4f;
        x.To(new(20.4f, 11.5f), Grass)
            .To(new(23, 11.5f), Gravel)
            .To(new(24, T), Bank)
            .To(new(24 + T * 1.3f, 0), Bank, flip: true);
    }

    static Vector3 Tree(MeshData m, Vector3 foot, float size)
    {
        m.Hex(0x5a4030);
        m.Beam(foot, foot + new Vector3(0, size * 0.55f, 0), size * 0.12f, size * 0.12f);
        m.Hex(Hash.At(foot, 2) < 0.5f ? 0x3f6e30u : 0x4c7a36u);
        var c = foot + new Vector3(0, size * 0.85f, 0);
        m.Blob(c, new Vector3(size * 0.5f, size * 0.42f, size * 0.5f), 6, 3);
        return c;
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        var up = Vector3.Up;
        // Post-and-rail fence along the top of the bank.
        var posts = Shared.Spots(p, 3, s => new(24.4f, s.Top));
        m.Hex(Wood);
        for (int i = 0; i < posts.Count; i++)
        {
            m.Beam(posts[i].pos, posts[i].pos + up * 1.2f, 0.18f, 0.18f);
            if (i + 1 < posts.Count)
                foreach (float y in new[] { 0.55f, 1.05f })
                    m.Beam(posts[i].pos + up * y, posts[i + 1].pos + up * y, 0.1f, 0.14f);
        }
        // Trees down the back of the bank.
        p.Each(10, s => new(29, s.Top - 5 / 1.3f), (pos, n, s) =>
        {
            float h = Hash.At(pos, 1);
            if (h < 0.2f) return;
            Tree(m, pos + n * (h * 6) - up * (h * 6 / 1.3f + 0.3f), 9 + h * 5);
        });
        // Slim floodlight poles on the bank.
        p.Spread(Mathf.Max(1, Mathf.RoundToInt(p.Length / 32)), s => new(23.4f, s.Top), (pos, n, s) =>
        {
            m.Hex(0x8a8f96);
            m.Beam(pos, pos + up * 17, 0.35f, 0.35f);
            g.AddLamp(Parts.LampBank(m, pos + up * 17.4f - n * 0.4f, -n, 3.6f, 1.6f, 0.7f), p);
        });

        var mid = p.Path[p.Mid];
        var ms = p.Sec[p.Mid];
        var face = new Vector3(-mid.NX, 0, -mid.NZ);
        var basis = Parts.Facing(face);
        if (p.Kind == Kind.Corner)
        {
            if (p.Partial) return;
            // A great old tree.
            Tree(m, mid.At(31, ms.Top - 7 / 1.3f - 0.3f), 20);
            return;
        }
        if (p.Slot != Slot.Main || p.Hidden) return;
        // The little stand: a green tin shed on posts on the gravel path, a clock on its gable.
        var c = mid.At(22.6f, 11.5f);
        const float L = 28, D = 4.4f, Hh = 4.2f;
        m.Hex(Tin, Look.Roof);
        m.Box(new Transform3D(basis, c + face * (-D / 2) + up * (Hh / 2)), new Vector3(L, Hh, 0.3f), 63);
        var sideV = basis.X * (L / 2);
        Vector3 back = c - face * D + up * (Hh + 0.4f), front = c + up * (Hh - 0.6f);
        m.Quad(back - sideV, back + sideV, front + sideV, front - sideV, L, D);
        m.Quad(front - sideV, front + sideV, back + sideV, back - sideV, L, D);
        m.Hex(Rail);
        for (int i = 0; i <= 4; i++)
        {
            var f = c + basis.X * (-L / 2 + i * L / 4) + up * 0;
            m.Beam(f, f + up * (Hh - 0.6f), 0.25f, 0.25f);
        }
        m.Box(new Transform3D(basis, front + up * 0.5f + face * 0.05f), new Vector3(L, 0.9f, 0.15f), 63);
        // The clock: a white face over the middle.
        m.Hex(0x2a2c30);
        m.Box(new Transform3D(basis, front + up * 1.7f + face * 0.1f), new Vector3(1.9f, 1.9f, 0.3f), 63);
        Parts.Disc(m, front + up * 1.7f + face * 0.3f, face, 0.75f, 0xf4f1e6);
    }
}
