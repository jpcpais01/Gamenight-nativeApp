using System;
using Godot;

namespace GameNight.Grounds.Build;

/// <summary>
/// Membrane: a light modern stand under a tensile fabric roof, pulled up into peaks by white
/// masts behind the stand and sagging between them, pennants in the club's colour on the masts.
/// </summary>
public sealed class Membrane : StandSet
{
    const uint Fabric = 0xefebe0, Back = 0x8d949b, Mast = 0xdfe3e6;
    const float Bay = 18;
    public override string Name => "Membrane";
    public override Outside Outside => new(0x8d949b, Look.Plain, 0, Stairs.Zigzag, 0xdfe3e6);
    public override string About => "Tensile fabric peaks pulled up by white masts";
    public override uint Swatch => 0xf2efe6;
    public override uint[] Mains => new uint[] { Fabric, Back };
    public override float Natural(Kind k) => k == Kind.Side ? 26 : k == Kind.End ? 24 : 22;
    public override Vector2 Range => new(20, 32);
    public override TierFans LowerFans() => new() { Vom = new(7.2f, 10.2f), Shade = new(6, 14) };
    public override (uint col, Look look) Cap => (Back, Look.Plain);

    static float UpperH(float T) => Mathf.Max(T - 7.5f, 17);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float uh = UpperH(T), ue = 21.5f + (uh - 15.8f) / 0.72f;
        x.Top = T; x.Ue = ue; x.Back = ue + 1.5f; x.BackH = T; x.Edge = 6;
        x.To(new(22, 11.5f), Kit.Concrete)
            .To(new(22, 14.5f), 0x2a3440, Look.Glass)
            .To(new(21, 14.5f), Kit.Concrete)
            .To(new(21, 15.8f), g.WallCol, Look.Wall)
            .To(new(21.5f, 15.8f), Kit.Concrete)
            .To(new(ue, uh), g.Seat, Look.Tier, 1, 6)
            .To(new(ue, uh + 1.2f), Kit.Concrete)
            .To(new(ue + 1.5f, uh + 1.2f), Kit.Concrete)
            .To(new(ue + 1.5f, 0), Back, Look.Curtain, flip: true);
        x.Fans(new(21.5f, 15.8f), new(ue, uh), new TierFans { Shade = new(-2, 6) });
    }

    /// <summary>The fabric's height `u` metres along the piece, at offset o: up at the masts,
    /// sagging between them, most at the front edge.</summary>
    static float Cloth(float u, Section s, float o)
    {
        float w = (1 - Mathf.Cos(Mathf.Tau * u / Bay)) / 2;
        float f = Mathf.Clamp((o - s.Edge) / (s.Back - s.Edge), 0, 1);
        return s.Top + 1.2f * f - w * (2.6f * (1 - f) + 0.8f);
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        // The fabric: a grid from the front edge to the back, two steps per path segment.
        const int K = 4;
        m.Hex(Fabric);
        float u = 0;
        for (int i = 0; i + 1 < p.Path.Count; i++)
        {
            PathPt a = p.Path[i], b = p.Path[i + 1];
            Section sa = p.Sec[i], sb = p.Sec[i + 1];
            float len = new Vector2(b.X - a.X, b.Z - a.Z).Length();
            for (int h = 0; h < 2; h++)
            {
                float t0 = h / 2f, t1 = (h + 1) / 2f;
                Vector3 P(float t, int k)
                {
                    var s = t < 0.5f ? sa : sb;
                    float o = Mathf.Lerp(s.Edge, s.Back, k / (float)K);
                    float y = Cloth(u + t * len, s, o);
                    return a.At(o, y).Lerp(b.At(o, y), t);
                }
                for (int k = 0; k < K; k++)
                    m.Quad(P(t0, k), P(t1, k), P(t1, k + 1), P(t0, k + 1), len / 2, 1);
            }
            u += len;
        }
        // Lamps under the fabric's front edge.
        m.Hex(0xffffff, Look.Lamp);
        for (int i = 0; i < p.Path.Count; i += 3)
        {
            var pt = p.Path[i];
            var s = p.Sec[i];
            var c = pt.At(s.Edge + 1.6f, s.Top - 3.8f);
            var basis = new Basis(Vector3.Up, pt.FacePitch + Mathf.Pi) * new Basis(Vector3.Right, 0.8f);
            var bx = basis.X * 1.4f; var by = basis.Y * 0.4f;
            m.QuadUV(c - bx - by, c + bx - by, c + bx + by, c - bx + by, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }
        p.Spread(Mathf.Max(1, Mathf.RoundToInt(p.Length / 28)), s => new(s.Edge + 1.6f, s.Top - 4), (pos, n, s) => g.AddLamp(pos, p));
        if (p.Partial && p.Kind == Kind.Corner) return;
        // The masts at every peak, stayed to the fabric's front and back and to the ground.
        p.Each(Bay, s => new(s.Back + 3, 0), (foot, n, s) =>
        {
            float h = s.Top + 9;
            var top = foot + new Vector3(0, h, 0);
            m.Hex(Mast);
            m.Column(foot, 0.55f, 0.3f, h, 6);
            m.Hex(0x9aa0a6);
            m.Beam(top, foot - n * (s.Back + 3 - s.Edge) + new Vector3(0, s.Top - 0.8f, 0), 0.12f, 0.12f);
            m.Beam(top, foot - n * 3 + new Vector3(0, s.Top + 0.4f, 0), 0.12f, 0.12f);
            m.Beam(top, foot + n * 9, 0.14f, 0.14f);
            Parts.Flag(m, top, 3, new Vector3(-n.Z, 0, n.X), 2.4f, 1.3f, g.Home);
        }, 0);
    }
}

/// <summary>
/// Brutalist: raw board-marked concrete. One steep upper tier under a deep cantilever roof,
/// held from behind by giant raking concrete frames whose girders run out over the roof;
/// service cores like slabs in the corners, a press box hung under the main stand's roof.
/// </summary>
public sealed class Brutalist : StandSet
{
    const uint Raw = 0x8e8c86, Dark = 0x6c6a65, Stain = 0x77756f;
    public override string Name => "Brutalist";
    public override Outside Outside => new(0x6c6a65, Look.Plain, 0, Stairs.Tower, 0x8e8c86);
    public override string About => "Raw concrete: a deep cantilever roof on giant raker frames";
    public override uint Swatch => 0x9a978f;
    public override uint[] Mains => new uint[] { Raw, Dark, Stain };
    public override Vector2? TifoAt(Section x) => new(x.Edge + 0.6f, x.Top - 3.2f);
    public override float Natural(Kind k) => k == Kind.Side ? 36 : k == Kind.End ? 32 : 30;
    public override Vector2 Range => new(26, 42);
    public override TierFans LowerFans() => new() { Vom = new(7.2f, 10.2f), Shade = new(9, 17) };
    public override (uint col, Look look) Cap => (Raw, Look.Plain);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float uh = T - 8, ue = 21.5f + (uh - 16.2f) / 0.75f;
        x.Top = T; x.Ue = ue; x.Back = ue + 2; x.BackH = T + 0.5f; x.Edge = 8;
        x.To(new(22, 11.5f), Raw)
            .To(new(22, 15), 0x26282c, Look.Glass)
            .To(new(21, 15), Raw)
            .To(new(21, 16.2f), Raw)
            .To(new(21.5f, 16.2f), Kit.Concrete)
            .To(new(ue, uh), g.Seat, Look.Tier, 1, 6)
            .To(new(ue, T - 1.6f), Stain)
            .To(new(x.Back, T + 0.5f), Raw)
            .To(new(x.Back, 0), Raw, Look.Tower, 2, flip: true);
        // The roof: a deep slab, coffered underneath, a tall fascia.
        x.Sheet(new(x.Back, T - 1.2f), new(x.Edge, T - 2.8f), Raw, Look.Coffer)
            .Sheet(new(x.Edge, T - 2.8f), new(x.Edge, T + 0.5f), Raw)
            .Sheet(new(x.Edge, T + 0.5f), new(x.Back, T + 0.5f), Dark);
        x.Fans(new(21.5f, 16.2f), new(ue, uh), new TierFans { Shade = new(-2, 12) });
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        // Lamps under the roof's front.
        m.Hex(0xffffff, Look.Lamp);
        for (int i = 0; i < p.Path.Count; i += 2)
        {
            var pt = p.Path[i];
            var s = p.Sec[i];
            var c = pt.At(s.Edge + 1.4f, s.Top - 3.3f);
            var basis = new Basis(Vector3.Up, pt.FacePitch + Mathf.Pi) * new Basis(Vector3.Right, 0.75f);
            var bx = basis.X * 1.6f; var by = basis.Y * 0.5f;
            m.QuadUV(c - bx - by, c + bx - by, c + bx + by, c - bx + by, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }
        p.Spread(Mathf.Max(1, Mathf.RoundToInt(p.Length / 28)), s => new(s.Edge + 1.4f, s.Top - 3.5f), (pos, n, s) => g.AddLamp(pos, p));

        // The raker frames: a raking strut from the ground well out behind up to the roof's
        // back, a leg straight down, the girder over the roof.
        m.Hex(Raw);
        p.Each(12, s => new(s.Back, 0), (pos, n, s) =>
        {
            var up = new Vector3(0, 1, 0);
            var top = pos + up * (s.Top + 1.6f) + n * 0.8f;
            m.Beam(pos + n * 15, top, 1.3f, 2.0f);
            m.Beam(pos + n * 1.2f, pos + n * 1.2f + up * (s.Top + 0.5f), 1.1f, 1.4f);
            m.Beam(top, pos - n * (s.Back - s.Edge - 1.5f) + up * (s.Top + 1.2f), 0.9f, 1.6f);
        });
        var mid = p.Path[p.Mid];
        var ms = p.Sec[p.Mid];
        var face = new Vector3(-mid.NX, 0, -mid.NZ);
        if (p.Kind == Kind.Corner)
        {
            if (p.Partial) return;
            // A service core: a slab tower with slit windows, the floodlights on its top.
            var c = mid.At(ms.Back + 9, 0);
            float h = ms.Top + 12;
            m.Hex(Raw, Look.Curtain);
            m.Box(new Transform3D(Parts.Facing(face), c + new Vector3(0, h / 2, 0)), new Vector3(12, h, 7), 63 & ~8);
            m.Hex(Dark);
            m.Box(new Transform3D(Parts.Facing(face), c + new Vector3(0, h + 0.4f, 0)), new Vector3(13, 0.8f, 8));
            g.AddLamp(Parts.LampBank(m, c + new Vector3(0, h + 3.4f, 0) + face * 3, face, 9, 3.6f, 0.7f), p);
            return;
        }
        if (p.Slot == Slot.Main && !p.Hidden)
        {
            // The press box hung under the roof.
            var c = mid.At(ms.Edge + 11, ms.Top - 4.8f);
            m.Hex(Dark);
            m.Box(new Transform3D(Parts.Facing(face), c), new Vector3(34, 3.2f, 4), 63);
            m.Hex(0x26282c, Look.Glass);
            m.Box(new Transform3D(Parts.Facing(face), c + face * 2.02f + new Vector3(0, -0.2f, 0)), new Vector3(32, 2.2f, 0.02f), 16 | 32);
        }
    }
}

/// <summary>
/// Barrio: the neighbourhood ground squeezed between the streets. Three sheer tiers stacked
/// one on another, each with its painted wall and a row of boxes under the next, no roof,
/// floodlights on a rail along the top, the outside painted in big stripes like a mural, and
/// apartment blocks pressed up against the corners.
/// </summary>
public sealed class Barrio : StandSet
{
    const uint Blue = 0x2b5fa8, Gold = 0xf2c230, Boxes = 0x1e2630;
    static readonly uint[] Flats = { 0xd98c5f, 0xe2c28f, 0x9fb8a0, 0xc96f6f, 0xe8e0cf };
    public override string Name => "Barrio";
    public override Outside Outside => new(0x1f4f8f, Look.Plain, 0, Stairs.Zigzag, 0xf2c230);
    public override string About => "Three sheer stacked tiers and boxes, a mural outside";
    public override uint Swatch => Blue;
    public override uint[] Mains => new uint[] { Blue };
    public override float Natural(Kind k) => k == Kind.End ? 36 : k == Kind.Side ? 34 : 33;
    public override Vector2 Range => new(26, 42);
    public override (uint col, Look look) Cap => (Blue, Look.Plain);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float h = (T - 11.5f) / 3, o = 20.6f, y = 11.5f;
        x.To(new(o, y), Kit.Concrete);
        for (int k = 0; k < 3; k++)
        {
            float wall = 1.1f, box = Mathf.Min(2.6f, h * 0.3f), rise = h - wall - box;
            x.To(new(o, y + wall), g.WallCol, Look.Wall)
                .To(new(o + 0.4f, y + wall), Kit.Concrete);
            var a = new Vector2(o + 0.4f, y + wall);
            var b = new Vector2(a.X + rise / 0.95f, a.Y + rise);
            x.To(b, g.Seat, Look.Tier, 1, 4);
            x.Fans(a, b, new TierFans { Fill = 0.96f });
            x.To(new(b.X, b.Y + box), Boxes, Look.Glass);
            o = b.X;
            y = b.Y + box;
        }
        x.To(new(o, T + 1), g.WallCol, Look.Wall)
            .To(new(o + 0.8f, T + 1), Kit.Concrete)
            .To(new(o + 0.8f, 0), Blue, Look.Tower, 2, flip: true);
        x.Top = T; x.Ue = o; x.Back = o + 0.8f; x.BackH = T + 1; x.Edge = o;
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        // The mural: tall stripes in the club's colour and gold down the outside wall.
        int k = 0;
        p.Each(9, s => new(s.Back + 0.07f, s.Top * 0.42f), (pos, n, s) =>
        {
            m.Hex(k++ % 2 == 0 ? g.Home : Gold);
            m.Box(new Transform3D(Parts.Facing(-n), pos), new Vector3(5.5f, s.Top * 0.7f, 0.1f), 16 | 32);
        });
        // Floodlights on a rail of posts along the top.
        p.Spread(Mathf.Max(1, Mathf.RoundToInt(p.Length / 16)), s => new(s.Back - 0.4f, s.Top + 1), (pos, n, s) =>
        {
            m.Hex(0x30353c);
            m.Beam(pos, pos + new Vector3(0, 7, 0), 0.3f, 0.3f);
            g.AddLamp(Parts.LampBank(m, pos + new Vector3(0, 7.4f, 0) - n * 0.4f, -n, 4.4f, 2, 0.75f), p);
        });
        if (p.Kind != Kind.Corner || p.Partial) return;
        // Flats pressed against the corner, washing on the balconies' rails.
        var mid = p.Path[p.Mid];
        var ms = p.Sec[p.Mid];
        var face = new Vector3(-mid.NX, 0, -mid.NZ);
        var basis = Parts.Facing(face);
        for (int i = -1; i <= 1; i++)
        {
            var c = mid.At(ms.Back + 7.5f, 0) + basis.X * (i * 13);
            float h = ms.Top + 4 - Mathf.Abs(i) * 6 + Hash.At(c) * 5;
            m.Hex(Flats[(int)(Hash.At(c, 3) * Flats.Length) % Flats.Length], Look.Tower, 2);
            m.Box(new Transform3D(basis, c + new Vector3(0, h / 2, 0)), new Vector3(12, h, 12), 1 | 2 | 16 | 32);
            m.Hex(0x77746e);
            m.Box(new Transform3D(basis, c + new Vector3(0, h, 0)), new Vector3(12, 0.01f, 12), 4);
            m.Hex(0x9a9690);
            m.Box(new Transform3D(basis, c + new Vector3(0, h + 1.4f, 0) - face * 2), new Vector3(3, 2.8f, 3), 1 | 2 | 4 | 16 | 32);
        }
    }
}

/// <summary>
/// Timber: the small eco ground. No upper tier; a gentle arched roof of glulam timber with a
/// planted green top, held on forked timber posts, the stand clad in wooden slats; timber stair
/// towers with trees on their roofs in the corners, solar panels and a wind turbine by the main
/// stand.
/// </summary>
public sealed class Timber : StandSet
{
    const uint Wood = 0xb07a48, Wood2 = 0x8a5a34, Sedum = 0x6f8a3c, Leaf = 0x4c7a38;
    public override string Name => "Timber";
    public override Outside Outside => new(0x6e4a2c, Look.Plain, 0, Stairs.Zigzag, 0x8a5a34);
    public override string About => "Eco stand: timber arches, wooden slats, a planted roof";
    public override uint Swatch => 0xc08a50;
    public override uint[] Mains => new uint[] { Wood, Wood2 };
    public override float Natural(Kind k) => k == Kind.Side ? 18 : k == Kind.End ? 16 : 15;
    public override Vector2 Range => new(14, 22);
    public override TierFans LowerFans() => new() { Vom = new(7.2f, 10.2f), Shade = new(5, 13) };
    public override (uint col, Look look) Cap => (Wood, Look.Roof);

    /// <summary>The arched roof at step j of 4 (from the back), y on its top.</summary>
    static Vector2 Arch(float T, int j)
    {
        float t = j / 4f;
        return new(Mathf.Lerp(22.5f, 4, t), Mathf.Lerp(T + 0.6f, T - 1.6f, t) + 1.4f * Mathf.Sin(Mathf.Pi * t));
    }

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        x.Top = T; x.Ue = 20.5f; x.Back = 22.5f; x.BackH = T + 0.6f; x.Edge = 4;
        x.To(new(20.5f, 11.5f), Kit.Concrete)
            .To(new(20.5f, T - 1.2f), Wood, Look.Roof)
            .To(new(22.5f, T + 0.6f), Wood2)
            .To(new(22.5f, 0), Wood, Look.Roof, flip: true);
        for (int j = 0; j < 4; j++)
        {
            Vector2 a = Arch(T, j), b = Arch(T, j + 1);
            x.Sheet(a - new Vector2(0, 0.3f), b - new Vector2(0, 0.3f), Wood, Look.Roof);
            x.Sheet(a, b, Sedum);
        }
        x.Sheet(new(4, T - 2.2f), new(4, T - 1.6f), Wood2);
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        // Glulam ribs under the roof, and forked posts holding its front.
        m.Hex(Wood2);
        p.Each(6, s => new(4, s.Top), (pos, n, s) =>
        {
            for (int j = 0; j < 4; j++)
            {
                Vector2 a = Arch(s.Top, j) - new Vector2(0, 0.7f), b = Arch(s.Top, j + 1) - new Vector2(0, 0.7f);
                m.Beam(pos + n * (a.X - 4) + new Vector3(0, a.Y - s.Top, 0), pos + n * (b.X - 4) + new Vector3(0, b.Y - s.Top, 0), 0.35f, 0.8f);
            }
        });
        p.Each(12, s => new(8, Kit.LowerAt(8)), (foot, n, s) =>
        {
            var fork = foot + new Vector3(0, s.Top - 6 - Kit.LowerAt(8), 0);
            m.Beam(foot, fork, 0.6f, 0.6f);
            foreach (float d in new[] { -3f, 3f })
            {
                var r = Arch(s.Top, 3);
                float o = 8 + d;
                float y = Mathf.Lerp(Arch(s.Top, 2).Y, Arch(s.Top, 4).Y, (Arch(s.Top, 2).X - o) / (Arch(s.Top, 2).X - 4)) - 0.8f;
                m.Beam(fork, foot + n * d + new Vector3(0, y - Kit.LowerAt(8), 0), 0.4f, 0.4f);
            }
        });
        // Lamps under the roof's front.
        m.Hex(0xffffff, Look.Lamp);
        for (int i = 0; i < p.Path.Count; i += 3)
        {
            var pt = p.Path[i];
            var s = p.Sec[i];
            var c = pt.At(5.4f, s.Top - 2.4f);
            var basis = new Basis(Vector3.Up, pt.FacePitch + Mathf.Pi) * new Basis(Vector3.Right, 0.9f);
            var bx = basis.X * 1.3f; var by = basis.Y * 0.4f;
            m.QuadUV(c - bx - by, c + bx - by, c + bx + by, c - bx + by, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }
        p.Spread(Mathf.Max(1, Mathf.RoundToInt(p.Length / 26)), s => new(5.4f, s.Top - 2.6f), (pos, n, s) => g.AddLamp(pos, p));

        var mid = p.Path[p.Mid];
        var ms = p.Sec[p.Mid];
        var face = new Vector3(-mid.NX, 0, -mid.NZ);
        var basis2 = Parts.Facing(face);
        if (p.Kind == Kind.Corner)
        {
            if (p.Partial) return;
            // A timber stair tower, a garden on its roof.
            var c = mid.At(ms.Back + 6, 0);
            float h = ms.Top + 5;
            m.Hex(Wood, Look.Roof);
            m.Box(new Transform3D(basis2, c + new Vector3(0, h / 2, 0)), new Vector3(9, h, 9), 1 | 2 | 16 | 32);
            m.Hex(Sedum);
            m.Box(new Transform3D(basis2, c + new Vector3(0, h + 0.3f, 0)), new Vector3(9.6f, 0.6f, 9.6f));
            m.Hex(Leaf);
            m.Blob(c + new Vector3(-1.5f, h + 3.2f, 1), new Vector3(2.4f, 2.8f, 2.4f), 5, 2);
            m.Blob(c + new Vector3(2, h + 2.6f, -1.5f), new Vector3(1.9f, 2.2f, 1.9f), 5, 2);
            return;
        }
        if (p.Slot != Slot.Main || p.Hidden) return;
        // Solar panels in rows on the planted roof.
        m.Hex(0x1d2b4a);
        for (int i = 1; i + 1 < p.Path.Count; i += 2)
        {
            var pt = p.Path[i];
            var s = p.Sec[i];
            foreach (int j in new[] { 1, 2 })
            {
                var a = Arch(s.Top, j);
                var c = pt.At(a.X - 1, a.Y + 0.6f);
                var b = new Basis(Vector3.Up, pt.FacePitch + Mathf.Pi) * new Basis(Vector3.Right, -0.5f);
                m.Box(new Transform3D(b, c), new Vector3(3.2f, 0.1f, 2), 4 | 16);
            }
        }
        // A wind turbine behind the stand.
        var tb = mid.At(ms.Back + 26, 0) + basis2.X * 30;
        float th = ms.Top + 34;
        m.Hex(0xeef0f2);
        m.Column(tb, 1.3f, 0.6f, th, 8);
        var hub = tb + new Vector3(0, th, 0) + face * 1.6f;
        m.Box(new Transform3D(basis2, tb + new Vector3(0, th, 0)), new Vector3(1.6f, 1.8f, 4.4f));
        for (int i = 0; i < 3; i++)
        {
            float a = 0.4f + i * Mathf.Tau / 3;
            var dir = basis2.X * Mathf.Cos(a) + Vector3.Up * Mathf.Sin(a);
            m.Beam(hub, hub + dir * 17, 0.9f, 0.25f);
        }
    }
}

/// <summary>
/// Lumen: a bowl wrapped in a skin of inflated diamond cushions from the ground up over the
/// roof, pale by day and lit from inside in the club's colour at night. One steep upper tier.
/// A big ground.
/// </summary>
public sealed class Lumen : StandSet
{
    const uint Skin = 0xe8eef2, Core = 0x3a3f46;
    public override string Name => "Lumen";
    public override Outside Outside => new(0x3a3f46, Look.Plain, 0, Stairs.None, 0);
    public override string About => "A skin of cushions that glows in your colours at night";
    public override uint Swatch => 0xcfe3f2;
    public override uint[] Mains => new uint[] { Skin, Core };
    public override Vector2? TifoAt(Section x) => new(x.Edge + 0.6f, x.Top - 2.6f);
    public override float Natural(Kind k) => k == Kind.End ? 44 : k == Kind.Side ? 42 : 40;
    public override Vector2 Range => new(34, 48);
    public override TierFans LowerFans() => new() { Vom = new(7.2f, 10.2f), Shade = new(9, 17) };
    public override (uint col, Look look) Cap => (Skin, Look.Cushion);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float uh = T - 7, ue = 21.5f + (uh - 15.8f) / 0.78f;
        x.Top = T; x.Ue = ue; x.Back = ue + 1; x.BackH = T; x.Edge = 9;
        x.To(new(22, 11.5f), Kit.Concrete)
            .To(new(22, 14.5f), 0x2a3440, Look.Glass)
            .To(new(21, 14.5f), Kit.Concrete)
            .To(new(21, 15.8f), 0xffffff, Look.Ribbon)
            .To(new(21.5f, 15.8f), Kit.Concrete)
            .To(new(ue, uh), g.Seat, Look.Tier, 1, 6)
            .To(new(ue, T), Core);
        // The skin swells out and down to the ground.
        float bulge = 0.3f * T;
        for (int j = 1; j <= 6; j++)
        {
            float a = j / 6f * Mathf.Pi / 2;
            x.To(new(ue + bulge * Mathf.Sin(a), T * Mathf.Cos(a)), Skin, Look.Cushion);
        }
        // Over the roof to the front edge, a light strip under its rim.
        x.Sheet(new(ue, T), new(x.Edge, T - 1.2f), Skin, Look.Cushion)
            .Sheet(new(x.Edge, T - 1.3f), new(x.Edge + 2.5f, T - 1.1f), 0xffffff, Look.RoofLight);
        x.Fans(new(21.5f, 15.8f), new(ue, uh), new TierFans { Shade = new(-2, 10) });
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        m.Hex(0xffffff, Look.Lamp);
        for (int i = 0; i < p.Path.Count; i += 2)
        {
            var pt = p.Path[i];
            var s = p.Sec[i];
            var c = pt.At(s.Edge + 3.4f, s.Top - 2.1f);
            var basis = new Basis(Vector3.Up, pt.FacePitch + Mathf.Pi) * new Basis(Vector3.Right, 0.85f);
            var bx = basis.X * 1.5f; var by = basis.Y * 0.4f;
            m.QuadUV(c - bx - by, c + bx - by, c + bx + by, c - bx + by, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }
        p.Spread(Mathf.Max(1, Mathf.RoundToInt(p.Length / 28)), s => new(s.Edge + 3.4f, s.Top - 2.3f), (pos, n, s) => g.AddLamp(pos, p));
    }
}
