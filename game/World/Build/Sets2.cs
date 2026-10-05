using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds.Build;

/// <summary>A steady 0..1 from a place and a salt (so a stand is built the same every time).</summary>
static class Hash
{
    public static float At(Vector3 p, int salt = 0)
    {
        uint n = (uint)(Mathf.RoundToInt(p.X * 7) * 374761393 + Mathf.RoundToInt(p.Y * 5) * 1442695041 + Mathf.RoundToInt(p.Z * 7) * 668265263 + salt * 2246822519u);
        n = (n ^ (n >> 13)) * 1274126177;
        return ((n ^ (n >> 16)) & 0xffff) / 65535f;
    }
}

/// <summary>
/// Harbour: the dockside ground. One deep tier backed by shipping containers stacked two and
/// three high in every colour, harbour lamps on poles above them, gantry cranes in the corners
/// carrying the floodlights, and a ship's bridge and funnel on the main stand. A small ground.
/// </summary>
public sealed class Harbour : StandSet
{
    const uint Navy = 0x23345e, Rust = 0x8a4b2e, Deck = 0x2a2f36;
    static readonly uint[] Boxes = { 0xc0392b, 0x2f7fb8, 0xe0a030, 0x2e8b57, 0xd35400, 0x8e44ad, 0xbdc3c7, 0x1f6f8b, 0xb03a2e, 0xf1c40f };
    public override string Name => "Harbour";
    public override Outside Outside => new(0x5d4a3e, Look.Plain, 0, Stairs.Zigzag, 0x4a5058);
    public override string About => "Dockside: stacked shipping containers, cranes for floodlights";
    public override uint Swatch => 0x2f7fb8;
    public override uint[] Mains => new uint[] { Navy, 0x1d2a3a, Rust };
    public override float Natural(Kind k) => k == Kind.End ? 19.3f : 16.7f;
    public override Vector2 Range => new(14, 22);
    public override (uint col, Look look) Front(BuiltGround g) => (Navy, Look.Wall);
    public override (uint col, Look look) Cap => (Rust, Look.Roof);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        x.Top = T; x.Ue = 20; x.Back = 23.8f; x.BackH = T; x.Edge = 21;
        // The containers (Dress) stand on the concourse from 20.5 to 23 m back; the wall behind them.
        x.To(new(23, 11.5f), Kit.Concrete)
            .To(new(23, T), 0x1d2a3a, Look.Roof)
            .To(new(23.8f, T), Deck)
            .To(new(23.8f, 0), Rust, Look.Roof, flip: true);
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        // The containers, end to end along the back of the tier, as high as the stand goes.
        p.Each(6.2f, s => new(21.75f, 11.5f), (pos, n, s) =>
        {
            int rows = Mathf.Max(1, (int)((s.Top - 11.5f) / 2.6f + 0.05f));
            var basis = Parts.Facing(n);
            for (int r = 0; r < rows; r++)
            {
                m.Hex(Boxes[(int)(Hash.At(pos, r) * Boxes.Length) % Boxes.Length], Look.Roof);
                m.Box(new Transform3D(basis, pos + new Vector3(0, 1.3f + r * 2.6f, 0)), new Vector3(6.0f, 2.55f, 2.5f), 1 | 2 | 32 | (r == rows - 1 ? 4 : 0));
            }
        });
        if (p.Kind == Kind.Corner)
        {
            if (p.Partial) return;
            // A gantry crane: four legs, a beam over them, the boom out over the stand, the
            // floodlights slung under its tip.
            var mid = p.Path[p.Mid];
            var s = p.Sec[p.Mid];
            var o = new Vector3(mid.NX, 0, mid.NZ);
            var t = new Vector3(-o.Z, 0, o.X);
            float head = s.Top + 18;
            var foot = mid.At(29, 0);
            uint col = g.Home;
            m.Hex(col);
            foreach (var (a, b) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                m.Beam(foot + t * (a * 5) + o * (b * 4), foot + t * (a * 3.5f) + o * (b * 3) + new Vector3(0, head, 0), 0.7f, 0.7f);
            m.Beam(foot + t * -3.5f + o * -3 + new Vector3(0, head * 0.45f, 0), foot + t * 3.5f + o * -3 + new Vector3(0, head * 0.45f, 0), 0.5f, 0.5f);
            m.Box(new Transform3D(Parts.Facing(o), foot + new Vector3(0, head + 0.8f, 0)), new Vector3(9, 1.6f, 8));
            var top = foot + new Vector3(0, head + 1.4f, 0);
            var tip = top - o * 34;
            m.Beam(top + o * 12, tip, 1.2f, 1.6f);
            m.Beam(top + new Vector3(0, 7, 0), tip + new Vector3(0, 0.6f, 0), 0.2f, 0.2f);
            m.Beam(top + new Vector3(0, 7, 0), top + o * 12, 0.2f, 0.2f);
            m.Beam(top, top + new Vector3(0, 7, 0), 0.6f, 0.6f);
            m.Hex(0xf3eee2);
            m.Box(new Transform3D(Parts.Facing(o), top - o * 4 - new Vector3(0, 2.4f, 0)), new Vector3(3, 2.6f, 3));
            g.AddLamp(Parts.LampBank(m, tip + o * 3 - new Vector3(0, 1.6f, 0), -o, 7, 2.6f, 0.8f), p);
            return;
        }
        // Harbour lamps on poles above the containers.
        p.Spread(p.Length > 70 ? 3 : 2, s => new(22.4f, s.Top), (pos, n, s) =>
        {
            m.Hex(0x30353c);
            m.Beam(pos, pos + new Vector3(0, 11, 0), 0.35f, 0.35f);
            g.AddLamp(Parts.LampBank(m, pos + new Vector3(0, 11.6f, 0) - n * 0.5f, -n, 5, 2.2f, 0.6f), p);
        });
        if (p.Slot == Slot.Main && !p.Hidden)
        {
            // A ship's bridge on the stack in the middle, the funnel in the club's colour.
            var mid = p.Path[p.Mid];
            var s = p.Sec[p.Mid];
            var face = new Vector3(-mid.NX, 0, -mid.NZ);
            var basis = Parts.Facing(face);
            var c = mid.At(22.4f, s.Top);
            m.Hex(0xf3eee2);
            m.Box(new Transform3D(basis, c + new Vector3(0, 2, 0)), new Vector3(16, 4, 4.5f));
            m.Hex(0xffffff, Look.Glass);
            m.Box(new Transform3D(basis, c + new Vector3(0, 2.4f, 0) + face * 2.27f), new Vector3(15, 3, 0.02f), 32);
            m.Hex(0xf3eee2);
            m.Box(new Transform3D(basis, c + new Vector3(0, 4.25f, 0)), new Vector3(18, 0.5f, 6));
            m.Hex(g.Home);
            m.Column(c + new Vector3(0, 4.5f, 0) - face * 1.5f, 2.2f, 2.0f, 6, 10);
            m.Hex(0x16161a);
            m.Column(c + new Vector3(0, 10.5f, 0) - face * 1.5f, 2.0f, 1.95f, 1.4f, 10);
        }
    }
}

/// <summary>
/// Pagoda: the temple ground. A single great tier under two swept, tiled roofs with upturned
/// eaves, red-lacquered pillars along the front with paper lanterns between them, paper
/// screens glowing in the pavilion behind, and five-storey pagodas in the corners.
/// </summary>
public sealed class Pagoda : StandSet
{
    const uint Lacquer = 0xb8322a, Tile = 0x34433f, Paper = 0xe7dcc0, Timber = 0x6e2a22, Lantern = 0xff8a3a;
    public override string Name => "Pagoda";
    public override Outside Outside => new(0x3a2e2a, Look.Plain, 0, Stairs.Tower, 0x9b2d20);
    public override string About => "Temple: swept tiled roofs, red pillars, glowing lanterns";
    public override uint Swatch => 0xd0453a;
    public override uint[] Mains => new uint[] { Lacquer, Timber };
    public override float Natural(Kind k) => k == Kind.Side ? 22 : k == Kind.End ? 20 : 19;
    public override Vector2 Range => new(18, 28);
    public override (uint col, Look look) Front(BuiltGround g) => (Timber, Look.Wall);
    public override TierFans LowerFans() => new() { Vom = new(7.2f, 10.2f), Shade = new(-2, 6) };
    public override (uint col, Look look) Cap => (Timber, Look.Plain);

    static float LowRoof(float T, float t) => T - 3.2f * t + 2.0f * t * t * t * t;
    static float HighRoof(float T, float t) => T + 4.5f - 2.6f * t + 1.0f * t * t * t * t;

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        x.Top = T; x.Back = 25; x.BackH = T; x.Edge = 2.5f; x.Ue = 20;
        x.To(new(22, 11.5f), Timber)
            .To(new(22, T - 3), Paper, Look.Curtain)
            .To(new(24, T - 3), Timber)
            .To(new(24, 0), Lacquer, Look.Plain, flip: true);
        // The lower roof sweeps down from the pavilion and turns up at the eave.
        Vector2 Low(float t) => new(25 + (2.5f - 25) * t, LowRoof(T, t));
        for (int i = 0; i < 6; i++) x.Sheet(Low(i / 6f), Low((i + 1) / 6f), Tile, Look.Roof);
        x.Sheet(new(2.5f, LowRoof(T, 1) - 0.8f), new(2.5f, LowRoof(T, 1)), Lacquer);
        // The pavilion's paper screens, and its own roof above.
        float sh = LowRoof(T, (25 - 18) / 22.5f);
        x.Sheet(new(18, sh), new(18, HighRoof(T, 0.55f)), Paper, Look.Curtain);
        Vector2 High(float t) => new(25.5f + (12 - 25.5f) * t, HighRoof(T, t));
        for (int i = 0; i < 4; i++) x.Sheet(High(i / 4f), High((i + 1) / 4f), Tile, Look.Roof);
        x.Sheet(new(12, HighRoof(T, 1) - 0.6f), new(12, HighRoof(T, 1)), Lacquer);
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        // Red pillars along the eave, a paper lantern hung between each pair.
        m.Hex(Lacquer);
        p.Each(8, s => new(2.9f, Kit.LowerAt(2.9f)), (pos, n, s) => m.Column(pos, 0.38f, 0.32f, LowRoof(s.Top, 0.98f) - 0.8f - pos.Y, 6));
        p.Each(8, s => new(2.9f, LowRoof(s.Top, 0.98f) - 2.6f), (pos, n, s) =>
        {
            m.Hex(Lantern, Look.Unlit);
            m.Box(new Transform3D(Parts.Facing(n), pos), new Vector3(0.8f, 1.1f, 0.8f), 63 & ~8 & ~4);
            m.Hex(0x16161a);
            m.Box(new Transform3D(Parts.Facing(n), pos + new Vector3(0, 0.65f, 0)), new Vector3(0.9f, 0.2f, 0.9f));
        }, 4);
        var mid = p.Path[p.Mid];
        var ms = p.Sec[p.Mid];
        if (p.Kind == Kind.Corner)
        {
            if (p.Partial) return;
            // A five-storey pagoda out on the corner, the floodlights on its fourth floor.
            var c = mid.At(36, 0);
            var face = new Vector3(-mid.NX, 0, -mid.NZ);
            float y = 0, w = 9;
            float phase = Mathf.Atan2(face.Z, face.X) + Mathf.Pi / 4;
            for (int k = 0; k < 5; k++)
            {
                float h = k == 0 ? 7 : 4.6f;
                m.Hex(k == 0 ? Timber : Lacquer);
                m.Box(new Transform3D(Parts.Facing(face), c + new Vector3(0, y + h / 2, 0)), new Vector3(w, h, w), 63 & ~8);
                y += h;
                m.Hex(Tile, Look.Roof);
                m.Column(c + new Vector3(0, y, 0), w * 1.02f, w * 0.5f, 1.8f, 4, phase);
                y += 1.8f;
                if (k == 3) g.AddLamp(Parts.LampBank(m, c + face * (w * 0.5f + 0.6f) + new Vector3(0, y - 4, 0), face, 6, 2.2f, 0.5f), p);
                w -= 1.4f;
            }
            m.Hex(0xd4a63a);
            m.Column(c + new Vector3(0, y, 0), 0.4f, 0.1f, 7, 6);
            return;
        }
        // Lamps along the high roof's ridge.
        p.Spread(p.Length > 70 ? 3 : 2, s => new(25.2f, HighRoof(s.Top, 0) + 0.2f), (pos, n, s) =>
        {
            m.Hex(Timber);
            m.Beam(pos, pos + new Vector3(0, 3.4f, 0), 0.3f, 0.3f);
            g.AddLamp(Parts.LampBank(m, pos + new Vector3(0, 4, 0) - n * 0.5f, -n, 5, 2, 0.6f), p);
        });
        if (p.Slot == Slot.Main && !p.Hidden)
        {
            // A gold-ridged hall over the middle of the high roof.
            var face = new Vector3(-mid.NX, 0, -mid.NZ);
            var basis = Parts.Facing(face);
            var c = mid.At(21, HighRoof(ms.Top, 0.3f));
            m.Hex(Lacquer);
            m.Box(new Transform3D(basis, c + new Vector3(0, 1.8f, 0)), new Vector3(16, 3.6f, 7));
            m.Hex(Tile, Look.Roof);
            m.Prism(new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(face.X, face.Z) + Mathf.Pi / 2), c + new Vector3(0, 3.6f, 0)), new Vector2[] { new(-5.2f, 0), new(5.2f, 0), new(0, 3.2f) }, 19);
            m.Hex(0xd4a63a);
            m.Beam(c + new Vector3(0, 6.85f, 0) - basis.X * 9.5f, c + new Vector3(0, 6.85f, 0) + basis.X * 9.5f, 0.35f, 0.35f);
        }
    }
}

/// <summary>
/// Deco: the grand 1930s stand. Cream stucco stepped back in tiers, gold trim and fins, green
/// glass over the concourse, tall arched windows round the outside, a streamlined canopy, a
/// clock tower on the main stand and stepped towers in the corners.
/// </summary>
public sealed class Deco : StandSet
{
    const uint Cream = 0xe8dcc0, Gold = 0xd4a63a, Green = 0x2d5e4f;
    public override string Name => "Deco";
    public override Outside Outside => new(0xd8c8a8, Look.Arcade, 4004, Stairs.Tower, 0xe2d6bc);
    public override string About => "1930s grandeur: cream steps, gold fins, a clock tower";
    public override uint Swatch => 0xe8d6a8;
    public override uint[] Mains => new uint[] { Cream };
    public override float Natural(Kind k) => k == Kind.Side ? 30 : k == Kind.End ? 27 : 25;
    public override Vector2 Range => new(22, 34);
    public override (uint col, Look look) Front(BuiltGround g) => (Green, Look.Wall);
    public override TierFans LowerFans() => new() { Vom = new(7.2f, 10.2f), Shade = new(9, 17) };
    public override (uint col, Look look) Cap => (Cream, Look.Plain);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float uh = T - 5.5f, ue = 21.5f + (uh - 15.8f) / 0.7f;
        x.Top = T; x.Ue = ue; x.Back = ue + 1.5f; x.BackH = T - 0.6f; x.Edge = 7.6f;
        x.To(new(22, 11.5f), Cream)
            .To(new(22, 14.5f), Green, Look.Curtain)
            .To(new(21, 14.5f), Gold)
            .To(new(21, 15.8f), Cream)
            .To(new(21.5f, 15.8f), Kit.Concrete)
            .To(new(ue, uh), g.Seat, Look.Tier, 1, 6)
            .To(new(ue, T - 0.6f), Cream)
            .To(new(ue + 1.5f, T - 0.6f), Cream)
            .To(new(ue + 1.5f, T + 1.8f), Cream)
            .To(new(ue + 2.7f, T + 1.8f), Gold)
            .To(new(ue + 2.7f, T + 3.6f), Cream)
            .To(new(ue + 3.9f, T + 3.6f), Cream)
            .To(new(ue + 3.9f, 0), Cream, Look.Arcade, 8003, flip: true);
        // The streamlined canopy: a coffered slab with a rounded gold nose.
        x.Sheet(new(ue + 1.5f, T - 0.6f), new(8, T - 0.6f), Cream, Look.Coffer)
            .Sheet(new(8, T - 0.6f), new(7.6f, T - 0.2f), Gold)
            .Sheet(new(7.6f, T - 0.2f), new(7.7f, T + 0.3f), Gold)
            .Sheet(new(7.7f, T + 0.3f), new(8.3f, T + 0.5f), Cream)
            .Sheet(new(8.3f, T + 0.5f), new(ue + 1.5f, T + 0.5f), Cream);
        x.Fans(new(21.5f, 15.8f), new(ue, uh), new TierFans { Shade = new(-2, 8) });
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        // Gold fins rising off the stepped parapet.
        m.Hex(Gold);
        p.Each(9, s => new(s.Ue + 2.1f, s.Top + 1.8f), (pos, n, s) =>
            m.Box(new Transform3D(Parts.Facing(n), pos + new Vector3(0, 2.6f, 0)), new Vector3(0.45f, 5.2f, 1.4f)));
        // Lamps under the canopy's nose.
        m.Hex(0xffffff, Look.Lamp);
        for (int i = 0; i < p.Path.Count; i += 3)
        {
            var pt = p.Path[i];
            var s = p.Sec[i];
            var c = pt.At(9.4f, s.Top - 0.9f);
            var basis = new Basis(Vector3.Up, pt.FacePitch + Mathf.Pi) * new Basis(Vector3.Right, 1.1f);
            var bx = basis.X * 1.5f; var by = basis.Y * 0.4f;
            m.QuadUV(c - bx - by, c + bx - by, c + bx + by, c - bx + by, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }
        p.Spread(Mathf.Max(1, Mathf.RoundToInt(p.Length / 28)), s => new(9.4f, s.Top - 1.2f), (pos, n, s) => g.AddLamp(pos, p));
        var mid = p.Path[p.Mid];
        var ms = p.Sec[p.Mid];
        var face = new Vector3(-mid.NX, 0, -mid.NZ);
        if (p.Kind == Kind.Corner)
        {
            if (p.Partial) return;
            // A stepped tower, gold-tipped.
            var c = mid.At(ms.Back + 7, 0);
            float y = 0;
            foreach (var (w, h) in new[] { (9f, ms.Top + 6), (6.6f, 5f), (4.4f, 4f) })
            {
                m.Hex(Cream, Look.Arcade, 8003);
                m.Box(new Transform3D(Parts.Facing(face), c + new Vector3(0, y + h / 2, 0)), new Vector3(w, h, w), 63 & ~8);
                y += h;
                m.Hex(Gold);
                m.Box(new Transform3D(Parts.Facing(face), c + new Vector3(0, y + 0.2f, 0)), new Vector3(w + 0.4f, 0.4f, w + 0.4f), 63 & ~8);
            }
            m.Column(c + new Vector3(0, y, 0), 0.9f, 0.1f, 8, 6);
            return;
        }
        if (p.Slot == Slot.Main && !p.Hidden)
        {
            // The clock tower over the middle of the stand.
            var basis = Parts.Facing(face);
            var c = mid.At(ms.Ue + 2.5f, 0);
            float h = ms.Top + 16;
            m.Hex(Cream);
            m.Box(new Transform3D(basis, c + new Vector3(0, h / 2, 0)), new Vector3(8, h, 6), 63 & ~8);
            m.Box(new Transform3D(basis, c + new Vector3(0, h + 2, 0)), new Vector3(5.6f, 4, 4.4f), 63 & ~8);
            m.Hex(Gold);
            m.Box(new Transform3D(basis, c + new Vector3(0, h + 0.2f, 0)), new Vector3(8.6f, 0.5f, 6.6f));
            m.Column(c + new Vector3(0, h + 4, 0), 1.2f, 0.1f, 6, 6);
            var dial = c + new Vector3(0, h - 4.2f, 0) + face * 3.02f;
            Parts.Disc(m, dial, face, 2.9f, Gold);
            Parts.Disc(m, dial + face * 0.02f, face, 2.5f, 0xf6f0de);
            m.Hex(0x16161a);
            m.Beam(dial + face * 0.05f, dial + face * 0.05f + new Vector3(0, 1.9f, 0), 0.22f, 0.12f);
            m.Beam(dial + face * 0.05f, dial + face * 0.05f + basis.X * 1.3f, 0.22f, 0.12f);
        }
    }
}

/// <summary>
/// Crater: a stadium cut into a volcano's bowl. Black rock all round, one steep tier of carved
/// steps, glowing lava seams, a jagged rim, rock spires with the floodlights and fire on top.
/// A big ground.
/// </summary>
public sealed class Crater : StandSet
{
    const uint Rock = 0x3a3533, Rock2 = 0x4d4642, Steps = 0x5a524d, Lava = 0xff6a1a, Fire = 0xffb347;
    public override string Name => "Crater";
    public override Outside Outside => new(0x3a3533, Look.Plain, 0, Stairs.None, 0);
    public override string About => "A volcano's bowl: black rock, lava seams, fire-topped spires";
    public override uint Swatch => 0xe0602a;
    public override uint[] Mains => new uint[] { Rock, Rock2, Steps };
    public override float Natural(Kind k) => k == Kind.End ? 40 : k == Kind.Side ? 34 : 37;
    public override Vector2 Range => new(28, 44);
    public override (uint col, Look look) Front(BuiltGround g) => (Rock, Look.Plain);
    public override (uint col, Look look) Cap => (Rock, Look.Plain);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float uh = T - 6, ue = 21 + (uh - 12.6f) / 0.82f;
        x.Top = T; x.Ue = ue; x.Back = ue + 6; x.BackH = T + 1.5f; x.Edge = ue;
        x.To(new(21, 11.5f), Rock)
            .To(new(21, 12.6f), Rock2)
            .To(new(ue, uh), Steps, Look.Tier, 1, 8)
            .To(new(ue, T - 3), Rock)
            .To(new(ue + 3, T), Rock2)
            .To(new(ue + 6, T + 1.5f), Rock)
            .To(new(ue + 10, T - 6), Rock2)
            .To(new(ue + 16, 0), Rock);
        // Lava seams glowing in the rock: along the concourse, and across the face above the tier.
        x.Sheet(new(20.95f, 11.75f), new(20.95f, 12.45f), Lava, Look.Unlit)
            .Sheet(new(ue - 0.05f, T - 4.9f), new(ue - 0.05f, T - 4.1f), Lava, Look.Unlit);
        x.Fans(new(21, 12.6f), new(ue, uh), new TierFans { Fill = 0.96f });
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        // The jagged rim.
        m.Hex(Rock);
        p.Each(6.5f, s => new(s.Ue + 5.2f, s.Top + 0.8f), (pos, n, s) =>
            m.Column(pos, 2.2f + Hash.At(pos, 1) * 1.6f, 0, 2.5f + Hash.At(pos, 2) * 5.5f, 4, Hash.At(pos, 3) * 3));
        if (p.Partial) return;
        // Spires of rock with the floodlights and a fire on top.
        p.Spread(p.Kind == Kind.Corner ? 1 : p.Length > 70 ? 3 : 2, s => new(s.Ue + 9, 0), (pos, n, s) =>
        {
            float h = s.Top + 16 + Hash.At(pos, 4) * 6;
            m.Hex(Rock2);
            m.Column(pos, 4.2f, 1.2f, h, 5, Hash.At(pos, 5) * 3);
            m.Hex(Lava, Look.Unlit);
            m.Column(pos + new Vector3(0, h, 0), 1.5f, 0.2f, 2.4f, 5);
            m.Hex(Fire, Look.Unlit);
            m.Column(pos + new Vector3(0, h + 1.4f, 0), 0.9f, 0, 3.4f, 4);
            g.AddLamp(Parts.LampBank(m, pos + new Vector3(0, h - 3, 0) - n * 1.8f, -n, 6, 2.4f, 0.55f), p);
        });
    }
}

/// <summary>
/// Orbital: the stadium from the future. White tiers, a halo roof floating free above them on
/// slender V struts, its underside glowing at the edge and an LED band round its rim, needle
/// towers with light rings in the corners. The biggest of all.
/// </summary>
public sealed class Orbital : StandSet
{
    const uint White = 0xe6eaee, Under = 0xd8dde3, Night = 0x1a2230;
    public override string Name => "Orbital";
    public override Outside Outside => new(0xd6dbe0, Look.Plain, 0, Stairs.Drum, 0xe6eaee);
    public override string About => "From the future: a floating halo roof, needle towers";
    public override uint Swatch => 0x8fe8ff;
    public override uint[] Mains => new uint[] { White, Under };
    public override Vector2? TifoAt(Section x) => new(5.6f, x.Top - 1.6f);
    public override float Natural(Kind k) => k == Kind.Side ? 44 : k == Kind.End ? 40 : 38;
    public override Vector2 Range => new(30, 48);
    public override (uint col, Look look) Front(BuiltGround g) => (White, Look.Plain);
    public override TierFans LowerFans() => new() { Vom = new(7.2f, 10.2f), Shade = new(9, 17) };
    public override (uint col, Look look) Cap => (White, Look.Plain);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float uh = T - 12, ue = 21.5f + (uh - 15.8f) / 0.72f;
        x.Top = T; x.Ue = ue; x.Back = ue + 6; x.BackH = T - 1.4f; x.Edge = 3;
        x.To(new(22, 11.5f), White)
            .To(new(22, 14.5f), Night, Look.Glass)
            .To(new(21, 14.5f), White)
            .To(new(21, 15.8f), 0xffffff, Look.Ribbon)
            .To(new(21.5f, 15.8f), White)
            .To(new(ue, uh), g.Seat, Look.Tier, 1, 6)
            .To(new(ue, uh + 1.2f), White)
            .To(new(ue + 2, uh + 1.2f), White)
            .To(new(ue + 2, 0), Night, Look.Curtain, flip: true);
        // The halo: floating clear of the stand, glowing along its inner edge.
        x.Sheet(new(ue + 6, T - 1.4f), new(5, T - 1.05f), Under, Look.Coffer)
            .Sheet(new(5, T - 1.05f), new(3, T - 1f), 0xffffff, Look.RoofLight)
            .Sheet(new(3, T - 1f), new(3, T + 0.2f), 0xffffff, Look.Ribbon)
            .Sheet(new(3, T + 0.2f), new(ue + 6, T + 0.6f), White)
            .Sheet(new(ue + 6, T + 0.6f), new(ue + 6, T - 1.4f), White);
        x.Fans(new(21.5f, 15.8f), new(ue, uh), new TierFans { Shade = new(-2, 10) });
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        // V struts from behind the stand up to the halo.
        m.Hex(White);
        p.Each(15, s => new(s.Ue + 11, 0), (foot, n, s) =>
        {
            var t = new Vector3(-n.Z, 0, n.X);
            var top = foot - n * 6 + new Vector3(0, s.Top - 1.4f, 0);
            m.Beam(foot, top + t * 5, 0.6f, 0.6f);
            m.Beam(foot, top - t * 5, 0.6f, 0.6f);
        });
        // Lamps tucked under the halo's edge.
        m.Hex(0xffffff, Look.Lamp);
        for (int i = 0; i < p.Path.Count; i += 2)
        {
            var pt = p.Path[i];
            var s = p.Sec[i];
            var c = pt.At(6.5f, s.Top - 1.6f);
            var basis = new Basis(Vector3.Up, pt.FacePitch + Mathf.Pi) * new Basis(Vector3.Right, 1.1f);
            var bx = basis.X * 1.6f; var by = basis.Y * 0.45f;
            m.QuadUV(c - bx - by, c + bx - by, c + bx + by, c - bx + by, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }
        p.Spread(Mathf.Max(1, Mathf.RoundToInt(p.Length / 28)), s => new(6.5f, s.Top - 1.8f), (pos, n, s) => g.AddLamp(pos, p));
        var mid = p.Path[p.Mid];
        var ms = p.Sec[p.Mid];
        var face = new Vector3(-mid.NX, 0, -mid.NZ);
        if (p.Kind == Kind.Corner)
        {
            if (p.Partial) return;
            // A needle tower, ringed with light, a beacon in the club's colour on top.
            var c = mid.At(ms.Back + 8, 0);
            float h = ms.Top + 32;
            m.Hex(White);
            m.Column(c, 2.4f, 0.3f, h, 6);
            m.Hex(0xffffff, Look.RoofLight);
            foreach (float k in new[] { 0.45f, 0.62f, 0.78f })
            {
                float r = Mathf.Lerp(2.4f, 0.3f, k) + 0.9f;
                m.Column(c + new Vector3(0, h * k, 0), r, r, 0.6f, 8);
            }
            m.Hex(g.Home, Look.Unlit);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(0, h + 0.6f, 0)), new Vector3(1.2f, 1.2f, 1.2f));
            return;
        }
        if (p.Slot == Slot.Main && !p.Hidden)
        {
            // A screen slung under the middle of the halo.
            var c = mid.At(10, ms.Top - 7);
            var basis = new Basis(Vector3.Up, mid.FacePitch + Mathf.Pi) * new Basis(Vector3.Right, 0.15f);
            m.Hex(Night);
            m.Box(new Transform3D(basis, c + new Vector3(mid.NX, 0, mid.NZ) * 0.3f), new Vector3(16.4f, 7.4f, 0.5f));
            m.Hex(White);
            m.Beam(c + new Vector3(0, 3.7f, 0) + basis.X * 6, mid.At(10, ms.Top - 1.3f) + basis.X * 6, 0.25f, 0.25f);
            m.Beam(c + new Vector3(0, 3.7f, 0) - basis.X * 6, mid.At(10, ms.Top - 1.3f) - basis.X * 6, 0.25f, 0.25f);
            m.Hex(0xffffff, Look.Screen);
            var bx = basis.X * 7.8f; var by = basis.Y * 3.4f; var f = basis.Z * 0.02f;
            m.QuadUV(c - bx - by + f, c + bx - by + f, c + bx + by + f, c - bx + by + f, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
            g.Screens = true;
        }
    }
}
