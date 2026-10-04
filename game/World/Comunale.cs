using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GameNight.Grounds;

/// <summary>
/// Stadio Comunale (the PWA's comunale.ts): an Italian city's municipal ground in concrete and
/// travertine. One continuous two-tier bowl, open to the sky everywhere but the main stand.
/// - The curve behind each goal: steep open tiers, the ultras behind the home goal.
/// - The Tribuna on the far side under a thin coffered concrete canopy, cantilevered off a row
///   of raking fins that come down the back like buttresses; the club's name along its edge.
/// - The Torre behind it, the crest on its face and the club's flag on top.
/// - A band of arches between the tiers; outside, an ochre arcade four storeys high.
/// - Spiral ramp towers in the corners, each carrying a floodlight mast; umbrella pines and
///   cypresses all round, and a hill town on the horizon with its bell tower and dome.
/// </summary>
public sealed class Comunale : Ground
{
    const float HL = 52.5f, HW = 34f;
    const float BowlX = HL + 8.5f, BowlZ = HW + 7.5f, BowlR = 10f;
    static readonly Vector2 Lower0 = new(0.4f, 1.4f), Lower1 = new(20, 11.5f);
    static readonly Vector2 Upper0 = new(21.5f, 15.8f), Upper1 = new(38, 28.5f);
    const float Back = 40.5f, Rim = 30f, Edge = 10f, Head = 57f;
    static float CanUnder(float o) => 31.8f + (Back - o) / (Back - Edge) * 1.2f;
    static float CanTop(float o) => 33.4f + (Back - o) / (Back - Edge) * 1.4f;

    const uint Concrete = 0xa9a294, Section = 0x77716a, Travertine = 0xd8ccb2, Ochre = 0xc99362,
        Steel = 0x3e444c, Terracotta = 0xa4532f, FinStone = 0xe8c29a;
    const float Concourse = 3.6f + 3000, Facade = 4.5f + 7500;

    protected override void Setup()
    {
        BoardArt = new (string, uint, uint)[]
        {
            ("CAFFÈ CENTRALE", 0xf3ecd8, 0x1f4e8c), ("PASTIFICIO ROSSI", 0x1f6b3a, 0xffffff), ("BANCA DEL PORTO", 0xb8262c, 0xffd447),
            ("GAMENIGHT", 0x26282c, 0xf2ede1), ("GELATERIA LUNA", 0xffd447, 0x1d1d1d), ("MOTO VELOCE", 0x23345e, 0xf2ede1),
            ("VINI DEL COLLE", 0xe9e1cc, 0x8a2f1d), ("ACQUA FONTE", 0x0e7c86, 0xffffff),
        };
        Banners = new[]
        {
            new BannerArt("CURVA ROSSA", HomeColor, 0xf3eee2, 1), new BannerArt("ULTRAS 1903", 0x14123a, HomeColor, 0),
            new BannerArt("SEMPRE CON VOI", 0xf3eee2, HomeColor, 2), new BannerArt(ClubName.ToUpperInvariant(), HomeColor, 0xf3eee2, 0),
            new BannerArt("FORZA RAGAZZI", 0x14123a, 0xffd447, 2), new BannerArt("BIG NIGHT", 0xf3eee2, 0x14123a, 1),
            new BannerArt("ATLANTIC 1903", AwayColor, 0xf3eee2, 0), new BannerArt("ROVERS TILL I DIE", 0x14123a, 0xf3eee2, 1),
            new BannerArt("AWAY DAYS", AwayColor, 0xf3eee2, 2), new BannerArt("ONE CLUB · ONE CITY", HomeColor, 0xf3eee2, 2),
        };
        Haze = 0.9f;
    }

    protected override void Build()
    {
        var m = Static;
        var path = Bowl.Path(BowlX, BowlZ, BowlR);
        var (left, main, right) = Bowl.Split(path);
        uint seat = Darken(HomeColor, 0.62f);
        uint wall = Darken(HomeColor, 0.8f);

        void S(MeshData d, List<PathPt> pts, Vector2 a, Vector2 b, uint col, Look look = Look.Plain, float par = 0, int segs = 1)
        {
            d.Hex(col, look, par);
            Bowl.Strip(d, pts, a, b, segs);
        }
        // The bowl's section, front wall to arcade.
        void BowlSection(MeshData d, List<PathPt> pts)
        {
            S(d, pts, new(0, 0), new(0, 1.4f), wall, Look.Wall);
            S(d, pts, new(0, 1.4f), Lower0, Concrete);
            S(d, pts, Lower0, Lower1, seat, Look.Tier, 2, 6);
            S(d, pts, Lower1, new(22, 11.5f), Concrete);
            S(d, pts, new(22, 11.5f), new(22, 14.5f), Travertine, Look.Arcade, Concourse);
            S(d, pts, new(22, 14.5f), new(21, 14.5f), Travertine);
            S(d, pts, new(21, 14.5f), new(21, 15.8f), Travertine);
            S(d, pts, new(21, 15.8f), Upper0, Concrete);
            S(d, pts, Upper0, Upper1, seat, Look.Tier, 1, 6);
            S(d, pts, Upper1, new(38, Rim), Travertine);
            S(d, pts, new(38, Rim), new(Back, Rim), Concrete);
            S(d, pts, new(Back, 0), new(Back, Rim), Ochre, Look.Arcade, Facade);
        }
        BowlSection(m, path);
        m.Hex(Section);
        Bowl.Caps(m, new[] { path[0], path[^1] }, new Vector2[]
        {
            new(0, 0), new(0, 1.4f), new(0.4f, 1.4f), new(20, 11.5f), new(22, 11.5f), new(22, 14.5f), new(21, 14.5f),
            new(21, 15.8f), new(21.5f, 15.8f), new(38, 28.5f), new(38, Rim), new(Back, Rim), new(Back, 0),
        });

        // The Tribuna's canopy: the press box at the back, a thin coffered slab out to the edge.
        S(m, main, new(Back, Rim), new(Back, CanUnder(Back)), 0xffffff, Look.Glass);
        S(m, main, new(Back, CanUnder(Back)), new(Edge, CanUnder(Edge)), 0xe6b98c, Look.Coffer);
        S(m, main, new(Edge, CanUnder(Edge)), new(Edge, CanTop(Edge)), 0xffffff, Look.Fascia);
        S(m, main, new(Edge, CanTop(Edge)), new(Back, CanTop(Back)), Travertine);
        S(m, main, new(Back, CanTop(Back)), new(Back, CanUnder(Back)), Travertine);
        Bowl.Caps(m, new[] { main[0], main[^1] }, new Vector2[] { new(Back, CanUnder(Back)), new(Back, CanTop(Back)), new(Edge, CanTop(Edge)), new(Edge, CanUnder(Edge)) });

        // The fins: a rib under the slab, deep at the back and thinning to nothing at the edge,
        // carrying on down the back of the stand to the ground like a buttress.
        var fin = new Vector2[]
        {
            new(Back, 0), new(Back + 4.8f, 0), new(Back + 0.8f, CanTop(Back)), new(Edge + 0.6f, CanTop(Edge) - 0.1f),
            new(Edge + 0.6f, CanUnder(Edge) - 0.3f), new(Back - 2, CanUnder(Back - 2) - 2.1f), new(Back, CanUnder(Back) - 2.1f),
        };
        {
            var a = main[0];
            var b = main[^1];
            float len = new Vector2(b.X - a.X, b.Z - a.Z).Length();
            int n = Mathf.RoundToInt(len / 6.4f);
            var basis = new Basis(new Vector3(a.NX, 0, a.NZ), Vector3.Up, new Vector3(-a.NZ, 0, a.NX));
            m.Hex(FinStone);
            for (int i = 0; i <= n; i++)
            {
                float f = 0.01f + 0.98f * i / n;
                m.Prism(new Transform3D(basis, new Vector3(a.X + (b.X - a.X) * f, 0, a.Z + (b.Z - a.Z) * f)), fin, 0.7f);
            }
        }

        // The Torre behind the Tribuna.
        var mid = main[main.Count / 2];
        var tower = mid.At(54, 0);
        const float TowerH = 64;
        m.Hex(Ochre);
        m.Box(tower - new Vector3(3, 0, 3), tower + new Vector3(3, TowerH, 3));
        m.Hex(Travertine);
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            m.Box(tower + new Vector3(sx * 3 - 0.45f, 0, sz * 3 - 0.45f), tower + new Vector3(sx * 3 + 0.45f, TowerH, sz * 3 + 0.45f));
            m.Box(tower + new Vector3(sx * 3.1f - 0.4f, TowerH, sz * 3.1f - 0.4f), tower + new Vector3(sx * 3.1f + 0.4f, TowerH + 5, sz * 3.1f + 0.4f));
        }
        for (float y = 10; y < TowerH; y += 10) m.Box(tower + new Vector3(-3.4f, y, -3.4f), tower + new Vector3(3.4f, y + 0.6f, 3.4f));
        m.Box(tower + new Vector3(-3.8f, TowerH + 5, -3.8f), tower + new Vector3(3.8f, TowerH + 5.9f, 3.8f));
        m.Hex(Terracotta);
        m.Column(tower + new Vector3(0, TowerH + 5.9f, 0), 5.2f, 0, 3.6f, 4, Mathf.Pi / 4);
        m.Hex(Steel);
        m.Box(tower + new Vector3(-0.12f, TowerH + 9, -0.12f), tower + new Vector3(0.12f, TowerH + 20, 0.12f));
        // The crest disc on its face, and the club's flag on top.
        var face = tower + new Vector3(-mid.NX, 0, -mid.NZ) * 3.06f + new Vector3(0, 55, 0);
        Disc(m, face, new Vector3(-mid.NX, 0, -mid.NZ), 2.4f, 0xe4dac4);
        Disc(m, face + new Vector3(-mid.NX, 0, -mid.NZ) * 0.02f, new Vector3(-mid.NX, 0, -mid.NZ), 2.0f, HomeColor);
        Disc(m, face + new Vector3(-mid.NX, 0, -mid.NZ) * 0.04f, new Vector3(-mid.NX, 0, -mid.NZ), 1.3f, 0xf3eee2);
        Flag(m, tower + new Vector3(0.15f, TowerH + 15.4f, 0), 7, 4.4f, HomeColor);

        // Spiral ramp towers in the corners, the floodlight masts rising out of them.
        var lamps = new List<Vector3>();
        float tcx = BowlX - 10, tcz = BowlZ - 10, d = 10 + Back + 7.5f;
        var corners = new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) }.Select(c => new Vector3(c.Item1 * (tcx + d * Mathf.Sqrt2 / 2), 0, c.Item2 * (tcz + d * Mathf.Sqrt2 / 2))).ToArray();
        foreach (var c in corners)
        {
            m.Hex(Ochre);
            m.Column(c, 3.2f, 3.2f, Rim + 1, 10);
            m.Hex(Travertine);
            m.Column(c + new Vector3(0, Rim + 1, 0), 3.8f, 3.8f, 0.7f, 10);
            Helix(m, c, 3.2f, 7, Rim, 3.5f, Mathf.Atan2(-c.Z, -c.X));
            int i = 0;
            for (float y = Rim + 1.7f; y < Head - 2; y += 6, i++)
            {
                float w = 2.4f - i * 0.22f;
                m.Box(c + new Vector3(-w / 2, y, -w / 2), c + new Vector3(w / 2, Mathf.Min(y + 6, Head - 2), w / 2));
            }
            var basis = new Basis(Vector3.Up, Mathf.Atan2(c.X, c.Z) + Mathf.Pi) * new Basis(Vector3.Right, 0.45f);
            var c0 = c + new Vector3(0, Head + 1.5f, 0);
            m.Hex(Steel);
            m.Box(new Transform3D(basis, c0 - basis.Z * 0.2f), new Vector3(10.4f, 5.8f, 0.35f));
            m.Hex(0xffffff, Look.Lamp);
            for (int r = 0; r < 2; r++)
            {
                var p = c0 + basis.Y * ((r - 0.5f) * 2.6f) + basis.Z * 0.25f;
                var x = basis.X * 4.8f; var yv = basis.Y * 1.2f;
                m.QuadUV(p - x - yv, p + x - yv, p + x + yv, p - x + yv, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
            }
            lamps.Add(c0 + basis.Z * 1.5f);
        }
        Lamps = lamps.ToArray();

        Scenery(m, corners, tower);
        Pitchside.Tunnel(m, -BowlZ, Section);

        // The paddock on the near side.
        float nearX = BowlX - 10 - 8;
        var near = Bowl.Straight(nearX, BowlZ, -nearX, BowlZ, 0, 1);
        S(m, near, new(0, 0), new(0, 1.4f), wall, Look.Wall);
        S(m, near, new(0, 1.4f), new(0.4f, 1.4f), Concrete);
        S(m, near, new(0.4f, 1.4f), new(11, 6.9f), seat, Look.Tier, 1, 3);
        S(m, near, new(11, 6.9f), new(12, 6.9f), Concrete);
        m.Hex(Concrete);
        Bowl.Caps(m, new[] { near[0], near[^1] }, new Vector2[] { new(0, 0), new(0, 1.4f), new(0.4f, 1.4f), new(11, 6.9f), new(12, 6.9f), new(12, 0) });
        // The near curve of the bowl behind the camera: solid for the sun, never drawn.
        BowlSection(ShadowOnly, Bowl.NearPath(BowlX, BowlZ, BowlR));

        Pitchside.AdBoards(m);
        Pitchside.CornerFlags(m);
        Pitchside.Dugouts(m, HomeKit, AwayKit);
        RailBanners(m, path, 20.9f, 13.7f, 34, 4.2f);

        float lowerSlope = (Lower1 - Lower0).Length();
        Crowd.Tier(path, Lower0, Lower1, new TierFans { Vom = new(7.2f, 10.2f), Tifo = EndTifos(BowlZ - BowlR, lowerSlope) });
        WaveFlags(path, Lower0, Lower1);
        Crowd.Tier(left, Upper0, Upper1, new TierFans());
        Crowd.Tier(right, Upper0, Upper1, new TierFans());
        Crowd.Tier(main, Upper0, Upper1, new TierFans { Shade = new(4, 16), Fill = 0.92f });
        Crowd.Tier(near, new(0.4f, 1.4f), new(11, 6.9f), new TierFans { Fill = 0.85f });
    }

    static uint Darken(uint hex, float k) =>
        ((uint)(((hex >> 16) & 255) * k) << 16) | ((uint)(((hex >> 8) & 255) * k) << 8) | (uint)((hex & 255) * k);

    /// <summary>A flat disc facing `n`.</summary>
    static void Disc(MeshData m, Vector3 c, Vector3 n, float r, uint col)
    {
        m.Hex(col);
        var x = Vector3.Up.Cross(n).Normalized();
        var y = n.Cross(x);
        const int N = 20;
        for (int i = 0; i < N; i++)
        {
            float a0 = Mathf.Tau * i / N, a1 = Mathf.Tau * (i + 1) / N;
            m.Tri(c, c + (x * Mathf.Cos(a0) + y * Mathf.Sin(a0)) * r, c + (x * Mathf.Cos(a1) + y * Mathf.Sin(a1)) * r, new(0, 0), new(1, 0), new(0, 1));
        }
    }

    /// <summary>A flag tied to a pole on its left edge at `pole` (its foot of the cloth).</summary>
    static void Flag(MeshData m, Vector3 pole, float w, float h, uint col)
    {
        m.Hex(col, Look.Cloth, -1);
        const int seg = 8;
        for (int s = 0; s < seg; s++)
        {
            float u0 = s / (float)seg, u1 = (s + 1) / (float)seg;
            var a = pole + new Vector3(w * u0, 0, 0);
            var b = pole + new Vector3(w * u1, 0, 0);
            m.QuadUV(a, b, b + new Vector3(0, h, 0), a + new Vector3(0, h, 0), new(u0, 0), new(u1, 0), new(u1, 1), new(u0, 1));
        }
        m.Hex(0xf3eee2);
        m.Box(pole + new Vector3(0, h * 0.42f, -0.02f), pole + new Vector3(w, h * 0.58f, 0.02f));
    }

    /// <summary>A spiral ramp: the deck winding up between radii r0 and r1, and its parapet.</summary>
    static void Helix(MeshData m, Vector3 c, float r0, float r1, float h, float turns, float phase)
    {
        int n = (int)Mathf.Ceil(turns * 20);
        m.Hex(Travertine);
        Vector3 P(float r, float a, float y) => c + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        for (int i = 0; i < n; i++)
        {
            float t0 = i / (float)n, t1 = (i + 1) / (float)n;
            float a0 = phase + t0 * turns * Mathf.Tau, a1 = phase + t1 * turns * Mathf.Tau;
            float y0 = t0 * h, y1 = t1 * h;
            m.Quad(P(r0, a0, y0), P(r0, a1, y1), P(r1, a1, y1), P(r1, a0, y0));
            m.Quad(P(r1, a0, y0), P(r1, a1, y1), P(r1, a1, y1 + 1.2f), P(r1, a0, y0 + 1.2f));
        }
    }

    /// <summary>Umbrella pines and cypresses round the ground, low hills and a hill town.</summary>
    void Scenery(MeshData m, Vector3[] corners, Vector3 tower)
    {
        var rng = new Random(11);
        float R() => (float)rng.NextDouble();
        bool Clear(float x, float z) => corners.All(c => new Vector2(x - c.X, z - c.Z).Length() > 13) && new Vector2(x - tower.X, z - tower.Z).Length() > 10;
        void Pine(float x, float z, float s)
        {
            float lean = (R() - 0.5f) * 0.25f;
            var top = new Vector3(x - lean * 8 * s, 8 * s, z);
            m.Hex(0x5a4636);
            m.Beam(new Vector3(x, 0, z), top, 0.6f * s, 0.6f * s);
            m.Hex(0x3f5a32);
            m.Blob(top + new Vector3(0, 0.6f * s, 0), new Vector3(4.6f, 1.6f, 4.6f) * s, 7, 3);
            m.Blob(top + new Vector3(2 * s * (R() - 0.5f), 1.6f * s, 2 * s * (R() - 0.5f)), new Vector3(3, 1.2f, 3) * s, 6, 3);
        }
        void Cypress(float x, float z, float s)
        {
            m.Hex(0x2c4429);
            m.Blob(new Vector3(x, 6 * s, z), new Vector3(1.5f, 6.5f, 1.5f) * s, 6, 4);
        }
        for (int i = 0; i < 64; i++)
        {
            float a = i / 64f * Mathf.Tau + R() * 0.05f;
            float rr = 1 + R() * 0.25f;
            float x = Mathf.Cos(a) * 126 * rr, z = Mathf.Sin(a) * 108 * rr;
            if (!Clear(x, z)) continue;
            if (i % 4 == 3)
            {
                float tx = -Mathf.Sin(a), tz = Mathf.Cos(a);
                for (int j = -1; j <= 1; j++) Cypress(x + tx * j * 4, z + tz * j * 4, 0.9f + R() * 0.3f);
            }
            else
            {
                Pine(x, z, 0.9f + R() * 0.45f);
                if (R() < 0.5f) Pine(x + (R() - 0.5f) * 14, z + (R() - 0.5f) * 14, 0.75f + R() * 0.3f);
            }
        }
        for (int i = 0; i < 9; i++)
        {
            float a = i / 9f * Mathf.Tau + 0.4f;
            float r = 70 + R() * 50, dd = 300 + R() * 60;
            m.Hex(i % 2 == 1 ? 0x6b7448u : 0x58633du);
            m.Blob(new Vector3(Mathf.Cos(a) * dd, -1, Mathf.Sin(a) * dd), new Vector3(r, r * (0.32f + R() * 0.12f), r), 12, 3, half: true);
        }
        // The town on its hill.
        float hx = 215, hz = -205, hr = 85, hh = 26;
        m.Hex(0x6b7448);
        m.Blob(new Vector3(hx, -1, hz), new Vector3(hr, hh, hr), 14, 4, half: true);
        uint[] walls = { 0xe2cfa6, 0xd29a5a, 0xc98a72 };
        float GroundAt(float x, float z) => hh * Mathf.Sqrt(Mathf.Max(0, 1 - ((x - hx) * (x - hx) + (z - hz) * (z - hz)) / (hr * hr))) - 2;
        for (int i = 0; i < 34; i++)
        {
            float a = R() * Mathf.Tau, dd = 6 + Mathf.Sqrt(R()) * 40;
            float x = hx + Mathf.Cos(a) * dd, z = hz + Mathf.Sin(a) * dd;
            float w = 6 + R() * 6, dp = 6 + R() * 4, h = 6 + R() * 8;
            float ry = Mathf.Round(R() * 2) * Mathf.Pi / 4;
            float y = GroundAt(x, z);
            m.Hex(walls[i % 3]);
            m.Box(new Transform3D(new Basis(Vector3.Up, ry), new Vector3(x, y + (h + 2) / 2, z)), new Vector3(w, h + 2, dp));
            m.Hex(Terracotta);
            m.Column(new Vector3(x, y + h + 2, z), new Vector2(w, dp).Length() / 2, 0, 2.4f, 4, ry + Mathf.Pi / 4);
        }
        float ty = GroundAt(hx, hz);
        m.Hex(walls[0]);
        m.Box(new Vector3(hx + 5.5f, ty, hz - 6.5f), new Vector3(hx + 10.5f, ty + 34, hz - 1.5f));
        m.Hex(Terracotta);
        m.Column(new Vector3(hx + 8, ty + 34, hz - 4), 3.8f, 0, 6, 4, Mathf.Pi / 4);
        m.Hex(walls[2]);
        m.Box(new Vector3(hx - 15, ty, hz - 9), new Vector3(hx - 1, ty + 9, hz + 13));
        m.Hex(walls[0]);
        m.Column(new Vector3(hx - 8, ty + 9, hz + 2), 7, 7, 6, 12);
        m.Hex(Terracotta);
        m.Blob(new Vector3(hx - 8, ty + 15, hz + 2), new Vector3(7, 7, 7), 12, 3, half: true);
    }
}
