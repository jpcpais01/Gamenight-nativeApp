using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds;

/// <summary>
/// The old ground (the PWA's oldGround.ts): a second-division club's home squeezed in between
/// the terraced streets. Four separate stands with the corners left open, a lattice floodlight
/// pylon in each corner.
/// - Far side: the old main stand, one tier of seats under a roof on pillars, the directors'
///   lounge along the back and a painted gable with the crest and the year the club began.
/// - Behind the home goal, the Shed: a covered standing terrace under a barrel roof, clad in
///   the club's colours, crush barriers down the steps.
/// - The away end: an open terrace behind a cage, the old clock on its posts at the back.
/// - Over the roofs: terraced houses with their windows lighting up, chimneys, trees, a spire.
/// </summary>
public sealed class OldGround : Ground
{
    const float HL = 52.5f, HW = 34f;
    const float FX = HL + 8.5f, FZ = HW + 7.5f;
    const float Rake = 10.1f / 19.6f;
    static float Tier(float o) => 1.4f + (o - 0.4f) * Rake;

    const uint Concrete = 0x8b8f96, DarkConcrete = 0x5c6068, BrickCol = 0x8c4f3c, RoofCol = 0x3a4048, SteelCol = 0x4a5058;

    // Atlas: the gable (16 x 5 m) and the clock (12 x 4.5 m), side by side under the ribbon.
    static readonly Rect2 GablePx = new(0, 640, 410, 128), ClockPx = new(512, 640, 341, 128);

    protected override void Setup()
    {
        BoardArt = new (string, uint, uint)[]
        {
            ("DAVE'S MOTORS", 0xf1ebdc, 0xb3262c), ("THE RED LION", 0x1d5c3a, 0xffd447), ("KEBAB KING", 0xc8393b, 0xffffff),
            ("GAMENIGHT", 0x26282c, 0xf2ede1), ("CITY TAXIS", 0xffd447, 0x1d1d1d), ("PLUMB-RITE", 0x23345e, 0xf2ede1),
            ("FISH & CHIPS", 0xe9e1cc, 0x23345e), ("W. HOLT & SON", 0x6b2737, 0xf2ede1),
        };
        Banners = new[]
        {
            new BannerArt("THE SHED", HomeColor, 0xf3eee2, 1), new BannerArt("UP THE REDS", 0x14123a, HomeColor, 0),
            new BannerArt("HOME SINCE 1903", 0xf3eee2, HomeColor, 2), new BannerArt(ClubName.ToUpperInvariant(), HomeColor, 0xf3eee2, 0),
            new BannerArt("NO SURRENDER", 0x14123a, 0xffd447, 2), new BannerArt("WE ARE THE TOWN", 0xf3eee2, 0x14123a, 1),
            new BannerArt("ATLANTIC 1903", AwayColor, 0xf3eee2, 0), new BannerArt("ROVERS TILL I DIE", 0x14123a, 0xf3eee2, 1),
            new BannerArt("AWAY DAYS", AwayColor, 0xf3eee2, 2), new BannerArt("ONE CLUB · ONE TOWN", HomeColor, 0xf3eee2, 2),
        };
    }

    protected override void Build()
    {
        var m = Static;
        uint seat = Darken(HomeColor, 0.62f);
        uint wall = Darken(HomeColor, 0.8f);
        var main = Bowl.Straight(-42, -FZ, 42, -FZ, 0, -1, 0);
        var shed = Bowl.Straight(-FX, 30, -FX, -30, -1, 0, 1);
        var away = Bowl.Straight(FX, -26, FX, 26, 1, 0, 2);
        var near = Bowl.Straight(36, FZ, -36, FZ, 0, 1, 0);

        void S(MeshData d, List<PathPt> pts, Vector2 a, Vector2 b, uint col, Look look = Look.Plain, float par = 0, int segs = 1)
        {
            d.Hex(col, look, par);
            Bowl.Strip(d, pts, a, b, segs);
        }
        // A point `f` of the way along a straight stand, at offset o and height h.
        static Vector3 Along(List<PathPt> pts, float f, float o, float h)
        {
            var a = pts[0]; var b = pts[^1];
            return new PathPt(a.X + (b.X - a.X) * f, a.Z + (b.Z - a.Z) * f, a.NX, a.NZ, a.Zone).At(o, h);
        }
        static float Len(List<PathPt> pts) => new Vector2(pts[^1].X - pts[0].X, pts[^1].Z - pts[0].Z).Length();
        void Pillars(List<PathPt> pts, float o, float top, float every)
        {
            m.Hex(SteelCol);
            int n = Mathf.RoundToInt(Len(pts) / every);
            for (int i = 1; i < n; i++) m.Beam(Along(pts, i / (float)n, o, Tier(o)), Along(pts, i / (float)n, o, top), 0.32f, 0.32f);
        }
        // Crush barriers: waist-high rails in short runs down the terrace, each on two posts.
        void Barriers(List<PathPt> pts, float[] rows)
        {
            m.Hex(SteelCol);
            float len = Len(pts);
            for (int r = 0; r < rows.Length; r++)
            {
                float o = rows[r], h = Tier(o);
                for (float s = 1.5f + r % 2 * 2.5f; s + 3.5f < len - 1; s += 5)
                {
                    var p0 = Along(pts, s / len, o, h); var p1 = Along(pts, (s + 3.5f) / len, o, h);
                    var up = new Vector3(0, 1.05f, 0);
                    m.Beam(p0 + up, p1 + up, 0.09f, 0.09f);
                    m.Beam(p0, p0 + up, 0.08f, 0.08f);
                    m.Beam(p1, p1 + up, 0.08f, 0.08f);
                }
            }
        }

        // ---- the main stand: one tier of seats under a roof on pillars
        S(m, main, new(0, 0), new(0, 1.4f), wall, Look.Wall);
        S(m, main, new(0, 1.4f), new(0.4f, 1.4f), Concrete);
        S(m, main, new(0.4f, 1.4f), new(15, Tier(15)), seat, Look.Tier, 1, 4);
        S(m, main, new(15, Tier(15)), new(16.5f, Tier(15)), Concrete);
        S(m, main, new(16.5f, Tier(15)), new(16.5f, 10.2f), DarkConcrete);
        S(m, main, new(16.5f, 10.2f), new(16.5f, 12.6f), 0xffffff, Look.Glass); // the directors' lounge
        S(m, main, new(16.5f, 12.6f), new(16.5f, 15.8f), DarkConcrete);
        S(m, main, new(16.5f, 15.8f), new(17, 15.8f), Concrete);
        S(m, main, new(17, 15.8f), new(1.2f, 13.2f), RoofCol, Look.Roof);
        S(m, main, new(1.2f, 11), new(1.2f, 13.2f), 0xffffff, Look.Fascia);
        S(m, main, new(17, 0), new(17, 15.8f), BrickCol, Look.Brick);
        m.Hex(BrickCol, Look.Brick);
        Bowl.Caps(m, new[] { main[0], main[^1] }, new Vector2[] { new(0, 0), new(0, 1.4f), new(0.4f, 1.4f), new(1.2f, Tier(1.2f)), new(1.2f, 13.2f), new(17, 15.8f), new(17, 0) });
        Pillars(main, 1.2f, 11, 12);

        // The gable over the middle of the roof, crest and year painted on, its ridge behind.
        var mid = main[main.Count / 2];
        var face = new Vector3(-mid.NX, 0, -mid.NZ);
        var basis = new Basis(Vector3.Up.Cross(face), Vector3.Up, face);
        {
            var c = mid.At(1.1f, 13.2f);
            Vector2 U(float x, float y) => new Vector2(GablePx.Position.X + x * GablePx.Size.X, GablePx.Position.Y + y * GablePx.Size.Y) / 1024f;
            m.Hex(0xffffff, Look.Decal, 0);
            m.Tri(c + basis.X * -8, c + basis.X * 8, c + Vector3.Up * 5, U(0, 1), U(1, 1), U(0.5f, 0));
            m.Hex(RoofCol);
            m.Prism(new Transform3D(basis, mid.At(4.2f, 13.2f)), new Vector2[] { new(-8, 0), new(8, 0), new(0, 5) }, 6);
        }
        Pitchside.Tunnel(m, -FZ, DarkConcrete);

        // ---- the Shed: covered standing terrace behind the home goal, under a barrel roof
        uint cladding = Darken(HomeColor, 0.7f);
        float shedSlope = new Vector2(18 - 0.4f, Tier(18) - 1.4f).Length();
        S(m, shed, new(0, 0), new(0, 1.4f), wall, Look.Wall);
        S(m, shed, new(0, 1.4f), new(0.4f, 1.4f), Concrete);
        S(m, shed, new(0.4f, 1.4f), new(18, Tier(18)), seat, Look.Tier, 0, 4);
        S(m, shed, new(18, Tier(18)), new(19, Tier(18)), Concrete);
        S(m, shed, new(19, Tier(18)), new(19, 14.8f), DarkConcrete);
        var arc = new Vector2[9];
        for (int i = 0; i < 9; i++)
        {
            float t = i / 8f;
            arc[i] = new(19.5f + (3 - 19.5f) * t, 14.8f + (12.6f - 14.8f) * t + 2.6f * Mathf.Sin(Mathf.Pi * t));
        }
        for (int i = 0; i < 8; i++) S(m, shed, arc[i], arc[i + 1], RoofCol, Look.Roof);
        S(m, shed, new(19, 14.8f), new(19.5f, 14.8f), Concrete);
        S(m, shed, new(3, 10.4f), new(3, 12.6f), 0xffffff, Look.Fascia);
        S(m, shed, new(19.5f, 0), new(19.5f, 14.8f), cladding, Look.Roof);
        m.Hex(cladding);
        var shedCap = new List<Vector2> { new(0, 0), new(0, 1.4f), new(0.4f, 1.4f), new(3, Tier(3)) };
        for (int i = 8; i >= 0; i--) shedCap.Add(arc[i]);
        shedCap.Add(new(19.5f, 0));
        Bowl.Caps(m, new[] { shed[0], shed[^1] }, shedCap.ToArray());
        Pillars(shed, 3, 10.4f, 10);
        Barriers(shed, new[] { 3.6f, 7.2f, 10.8f, 14.4f });

        // ---- the away end: an open terrace behind a cage, the clock at the back
        float awaySlope = new Vector2(17 - 0.4f, Tier(17) - 1.4f).Length();
        float top = Tier(17);
        S(m, away, new(0, 0), new(0, 1.4f), wall, Look.Wall);
        S(m, away, new(0, 1.4f), new(0.4f, 1.4f), Concrete);
        S(m, away, new(0.4f, 1.4f), new(17, top), seat, Look.Tier, 0, 4);
        S(m, away, new(17, top), new(17.8f, top), Concrete);
        S(m, away, new(17.8f, top), new(17.8f, top + 1.2f), Concrete);
        S(m, away, new(17.8f, top + 1.2f), new(18.2f, top + 1.2f), Concrete);
        S(m, away, new(18.2f, 0), new(18.2f, top + 1.2f), BrickCol, Look.Brick);
        m.Hex(Concrete);
        Bowl.Caps(m, new[] { away[0], away[^1] }, new Vector2[] { new(0, 0), new(0, 1.4f), new(0.4f, 1.4f), new(17, top), new(17.8f, top), new(17.8f, top + 1.2f), new(18.2f, top + 1.2f), new(18.2f, 0) });
        Barriers(away, new[] { 4f, 8f, 12f });
        // The cage: posts and a top rail along the front wall, see-through mesh between.
        m.Hex(SteelCol);
        for (int i = 0; i <= 17; i++) m.Beam(Along(away, i / 17f, 0.15f, 1.4f), Along(away, i / 17f, 0.15f, 4.4f), 0.1f, 0.1f);
        m.Beam(Along(away, 0, 0.15f, 4.4f), Along(away, 1, 0.15f, 4.4f), 0.1f, 0.1f);
        S(m, away, new(0.15f, 1.4f), new(0.15f, 4.4f), 0xa4aab2, Look.Stipple);
        // The clock on its two posts.
        var awayMid = away[away.Count / 2];
        var clockFace = new Vector3(-awayMid.NX, 0, -awayMid.NZ);
        Sign(m, awayMid.At(18, top + 4.4f), clockFace, 12, 4.5f, ClockPx, selfLit: true);
        m.Hex(0x0f1a14);
        m.Box(new Transform3D(new Basis(Vector3.Up.Cross(clockFace), Vector3.Up, clockFace), awayMid.At(18.2f, top + 4.4f)), new Vector3(12.3f, 4.8f, 0.3f));
        m.Hex(SteelCol);
        foreach (float s in new[] { -4.5f, 4.5f })
            m.Beam(awayMid.At(18.2f, top + 1.2f) + new Vector3(0, 0, s), awayMid.At(18.2f, top + 2.2f) + new Vector3(0, 0, s), 0.3f, 0.3f);

        // ---- floodlight pylons in the open corners: tapering lattice towers, a bank of lamps on top
        const float Head = 40;
        var lamps = new List<Vector3>();
        foreach (var (px, pz) in new[] { (-65.3f, -45.8f), (65.3f, -45.8f), (-65.3f, 45.8f), (65.3f, 45.8f) })
        {
            var outv = new Vector2(px, pz).Normalized();
            var side = new Vector2(-outv.Y, outv.X);
            Vector3 Leg(int i, float y)
            {
                float r = 1.6f - y / Head * 0.9f;
                float sx = (i & 1) != 0 ? 1 : -1, sz = (i & 2) != 0 ? 1 : -1;
                return new Vector3(px + (outv.X * sx + side.X * sz) * r, y, pz + (outv.Y * sx + side.Y * sz) * r);
            }
            int[] corners = { 0, 1, 3, 2 };
            m.Hex(SteelCol);
            for (float y = 0; y < Head; y += 4)
            {
                float y1 = Mathf.Min(Head, y + 4);
                for (int c = 0; c < 4; c++)
                {
                    int i = corners[c], j = corners[(c + 1) % 4];
                    m.Beam(Leg(i, y), Leg(i, y1), 0.22f, 0.22f);
                    m.Beam(Leg(i, y1), Leg(j, y1), 0.1f, 0.1f);
                    bool even = (int)y % 8 == 0;
                    m.Beam(even ? Leg(i, y) : Leg(j, y), even ? Leg(j, y1) : Leg(i, y1), 0.08f, 0.08f);
                }
            }
            // The headframe: two rows of lamps facing the centre spot, tilted down at the pitch.
            var b = new Basis(Vector3.Up, Mathf.Atan2(-px, -pz)) * new Basis(Vector3.Right, 0.42f);
            var c0 = new Vector3(px, Head + 2.8f, pz);
            m.Box(new Transform3D(b, c0 - b.Z * 0.2f), new Vector3(9, 5.8f, 0.35f));
            m.Hex(0xffffff, Look.Lamp);
            for (int r = 0; r < 2; r++)
            {
                var p = c0 + b.Y * ((r - 0.5f) * 2.7f) + b.Z * 0.25f;
                var x = b.X * 4.2f; var yv = b.Y * 1.3f;
                m.QuadUV(p - x - yv, p + x - yv, p + x + yv, p - x + yv, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
            }
            lamps.Add(c0 + b.Z * 1.5f);
        }
        Lamps = lamps.ToArray();

        // ---- the near side: a low paddock in play; the covered stand behind it casts its shadow only
        S(m, near, new(0, 0), new(0, 1.4f), wall, Look.Wall);
        S(m, near, new(0, 1.4f), new(0.4f, 1.4f), Concrete);
        S(m, near, new(0.4f, 1.4f), new(8, Tier(8)), seat, Look.Tier, 1, 2);
        S(m, near, new(8, Tier(8)), new(9, Tier(8)), Concrete);
        m.Hex(Concrete);
        Bowl.Caps(m, new[] { near[0], near[^1] }, new Vector2[] { new(0, 0), new(0, 1.4f), new(0.4f, 1.4f), new(8, Tier(8)), new(9, Tier(8)), new(9, 0) });
        S(ShadowOnly, near, new(0.4f, 1.4f), new(11, Tier(11)), Concrete);
        S(ShadowOnly, near, new(12.5f, 0), new(12.5f, 10.8f), Concrete);
        S(ShadowOnly, near, new(12.5f, 10.8f), new(1.2f, 9.9f), Concrete);
        Bowl.Caps(ShadowOnly, new[] { near[0], near[^1] }, new Vector2[] { new(0, 0), new(0, 1.4f), new(1.2f, Tier(1.2f)), new(1.2f, 9.9f), new(12.5f, 10.8f), new(12.5f, 0) });

        Town(m);
        Pitchside.AdBoards(m);
        Pitchside.CornerFlags(m);
        Dugouts(m);
        var all = new List<PathPt>(main);
        all.AddRange(shed);
        all.AddRange(away);
        RailBanners(m, all, 18.9f, 12.4f, 30, 2.8f);

        Crowd.Tier(main, new(0.4f, 1.4f), new(15, Tier(15)), new TierFans { Fill = 0.88f, Shade = new(-2, 6) });
        Crowd.Tier(shed, new(0.4f, 1.4f), new(18, Tier(18)), new TierFans { Aisles = false, Shade = new(-1, 9), Tifo = EndTifos(29, shedSlope) });
        Crowd.Tier(away, new(0.4f, 1.4f), new(17, top), new TierFans { Aisles = false, Fill = 0.72f, Tifo = EndTifos(20, awaySlope * 0.55f + 1) });
        Crowd.Tier(near, new(0.4f, 1.4f), new(8, Tier(8)), new TierFans { Fill = 0.85f });
        WaveFlags(main, new(0.4f, 1.4f), new(15, Tier(15)));
        WaveFlags(shed, new(0.4f, 1.4f), new(18, Tier(18)));
        HoldBanner(shed[shed.Count / 2], 6.5f, new(0.4f, 1.4f), new(18, Tier(18)), 10);
        HoldBanner(main[(int)(main.Count * 0.38f)], 4, new(0.4f, 1.4f), new(15, Tier(15)), 8);
        WaveFlags(away, new(0.4f, 1.4f), new(17, top));
    }

    /// <summary>Rows of terraced houses, the trees between them and the church.</summary>
    static void Town(MeshData m)
    {
        long seed = 7;
        float Rnd() => (seed = seed * 16807 % 2147483647) / 2147483647f;
        void Block(float w, float h, float d, float x, float y, float z, float ry) =>
            m.Box(new Transform3D(new Basis(Vector3.Up, ry), new Vector3(x, y + h / 2, z)), new Vector3(w, h, d));
        // A pitched roof over a w x d block, the ridge along w.
        void Roof(float w, float d, float rise, float x, float y, float z, float ry) =>
            m.Prism(new Transform3D(new Basis(Vector3.Up, ry + Mathf.Pi / 2), new Vector3(x, y, z)),
                new Vector2[] { new(-d / 2 - 0.3f, 0), new(d / 2 + 0.3f, 0), new(0, rise) }, w);
        void Street(float len, Func<float, Vector2> along, float ry)
        {
            float t = -len / 2;
            while (t < len / 2)
            {
                float blockLen = Mathf.Min(len / 2 - t, 18 + Mathf.Floor(Rnd() * 5) * 5);
                if (blockLen < 8) break;
                var p = along(t + blockLen / 2);
                float tall = Rnd() < 0.2f ? 8.6f : 6.2f;
                m.Hex(0xffffff, Look.House);
                Block(blockLen, tall, 8, p.X, 0, p.Y, ry);
                for (float c = -blockLen / 2 + 5; c < blockLen / 2 - 1; c += 10)
                    Block(0.9f, 2.2f, 1.6f, p.X + Mathf.Cos(ry) * c, tall + 1.6f, p.Y - Mathf.Sin(ry) * c, ry);
                m.Hex(0x3a3e47);
                Roof(blockLen, 8, 3, p.X, tall, p.Y, ry);
                t += blockLen + 7 + Rnd() * 5;
            }
        }
        Street(170, t => new(FX + 32, t), Mathf.Pi / 2);
        Street(190, t => new(t, -FZ - 34), 0);
        Street(170, t => new(-FX - 36, t), Mathf.Pi / 2);
        // Trees in the open corners.
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            for (int i = 0; i < 5; i++)
            {
                float x = sx * (72 + Rnd() * 12), z = sz * (50 + Rnd() * 14), r = 2.6f + Rnd() * 2;
                m.Hex(0x5a4636);
                Block(0.5f, 3, 0.5f, x, 0, z, 0);
                m.Hex(0x3b5230);
                m.Blob(new Vector3(x, 3 + r * 0.8f, z), new Vector3(r, r, r), 7, 4);
            }
        // The church: a stone tower and its spire over the roofs behind the main stand.
        m.Hex(0x8f897a);
        Block(7, 22, 7, -38, 0, -112, 0);
        Block(14, 9, 24, -38, 0, -96, 0);
        m.Hex(0x3a3e47);
        Roof(24, 14, 5, -38, 9, -96, Mathf.Pi / 2);
        m.Column(new Vector3(-38, 22, -112), 5, 0, 18, 4, Mathf.Pi / 4);
    }

    protected override void Paint(Signage s)
    {
        // The gable: cream, club-coloured trim up the slopes, the crest and the year.
        float x0 = GablePx.Position.X, y0 = GablePx.Position.Y, k = GablePx.Size.X / 512f;
        Vector2 P(float x, float y) => new(x0 + x * k, y0 + y * k);
        s.Rect(GablePx, 0xefe8d6);
        s.Line(HomeColor, 14 * k, P(0, 160), P(256, 0), P(512, 160));
        s.Rect(new Rect2(P(0, 153), new Vector2(GablePx.Size.X, 7 * k)), HomeColor);
        s.Line(0x14123a, 3 * k, P(40, 146), P(256, 16), P(472, 146));
        s.Crest(P(256, 80), 74 * k, HomeColor);
        s.Text(new Rect2(P(150, 120), new Vector2(212 * k, 32 * k)), string.IsNullOrWhiteSpace(Art.Founded) ? "FOOTBALL CLUB" : $"EST. {Art.Founded}", 0x14123a, 22);

        // The clock: quarter to eight (kick-off), the club's name, a welcome.
        x0 = ClockPx.Position.X; y0 = ClockPx.Position.Y; k = ClockPx.Size.X / 512f;
        s.Rect(ClockPx, 0x0f1a14);
        s.Line(0xe9e2cf, 6 * k, P(6, 6), P(506, 6), P(506, 186), P(6, 186), P(6, 6));
        var c = P(96, 96);
        var face = Signage.Circle(c, 70 * k);
        s.Poly(0xf3eee2, face);
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * Mathf.Tau;
            var d = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
            s.Line(0x14123a, 4 * k, c + d * 56 * k, c + d * 64 * k);
        }
        void Hand(float a, float r, float w) => s.Line(0x14123a, w * k, c, c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * r * k);
        Hand(7.75f / 12 * Mathf.Tau, 36, 7);
        Hand(45f / 60 * Mathf.Tau, 54, 5);
        s.Text(new Rect2(P(190, 40), new Vector2(300 * k, 76 * k)), ClubName.ToUpperInvariant(), 0xffd447, 36);
        s.Text(new Rect2(P(190, 116), new Vector2(300 * k, 40 * k)), "WELCOME TO THE OLD GROUND", 0xe9e2cf, 20);
    }

    static uint Darken(uint hex, float k) =>
        ((uint)(((hex >> 16) & 255) * k) << 16) | ((uint)(((hex >> 8) & 255) * k) << 8) | (uint)((hex & 255) * k);
}
