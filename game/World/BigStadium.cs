using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GameNight.Grounds;

/// <summary>
/// The big stadium (the PWA's stadium.ts): an old English ground on a big night. Four stands
/// tight to the touchlines with quadrant corners.
/// - The far side is the great main stand: three tiers, two bands of executive boxes, a tall
///   cantilever roof with the TV gantry slung under it, the players' tunnel in its middle.
/// - The ends and corners: two tiers under a lower roof; where the main stand rises above
///   them its flank is a glazed curtain wall.
/// - Cantilever roofs: steel girders on top, translucent panels at the front, the floodlights
///   a line of lamps along the roof front over the club's name on the fascia.
/// - LED ribbons on the tier fronts, big screens in the corners, a low paddock on the camera
///   side (the near stand behind the camera is never drawn, but it casts its evening shadow).
/// The home ultras pack the end behind the left goal, banners hang off the railings.
/// </summary>
public sealed class BigStadium : Ground
{
    const float HL = 52.5f, HW = 34f;
    const float BowlX = HL + 8.5f, BowlZ = HW + 7.5f, BowlR = 10f;
    static readonly Vector2 Lower0 = new(0.4f, 1.4f), Lower1 = new(20, 11.5f);
    static readonly Vector2 Upper0 = new(21.5f, 15.8f), Upper1 = new(38, 28.5f);
    static readonly Vector2 RoofBack = new(40, 33.2f);
    const float RoofEdge = 8, RoofH = 34.4f;
    static readonly Vector2 Top0 = new(39.6f, 32.4f), Top1 = new(58, 46);
    static readonly Vector2 MainBack = new(60, 50.8f);
    const float MainEdge = 11, MainH = 52.5f;

    static float RoofAt(Vector2 back, float edge, float h, float o) => back.Y + (o - back.X) / (edge - back.X) * (h - back.Y);

    const uint Concrete = 0x8b8f96, DarkConcrete = 0x5c6068, RoofCol = 0x30353d, Panels = 0xaab6c0, Steel = 0x4a5058;

    public BigStadium()
    {
        Banners = new[]
        {
            new BannerArt("CURVA ROSSA", HomeColor, 0xf3eee2, 1),
            new BannerArt("ULTRAS 1903", 0x14123a, HomeColor, 0),
            new BannerArt("SEMPRE CON VOI", 0xf3eee2, HomeColor, 2),
            new BannerArt(ClubName.ToUpperInvariant(), HomeColor, 0xf3eee2, 0),
            new BannerArt("GAMENIGHT", 0x14123a, 0xffd447, 2),
            new BannerArt("BIG NIGHT", 0xf3eee2, 0x14123a, 1),
            new BannerArt("ATLANTIC 1903", AwayColor, 0xf3eee2, 0),
            new BannerArt("ROVERS TILL I DIE", 0x14123a, 0xf3eee2, 1),
            new BannerArt("AWAY DAYS", AwayColor, 0xf3eee2, 2),
            new BannerArt("ONE CLUB · ONE NIGHT", HomeColor, 0xf3eee2, 2),
        };
    }

    protected override void Build()
    {
        var m = Static;
        var path = Bowl.Path(BowlX, BowlZ, BowlR);
        var (left, main, right) = Bowl.Split(path);
        var ends = new[] { left, right };
        uint seat = Darken(HomeColor, 0.62f);

        void S(List<PathPt> pts, Vector2 a, Vector2 b, uint col, Look look = Look.Plain, float par = 0, int segs = 1)
        {
            m.Hex(col, look, par);
            Bowl.Strip(m, pts, a, b, segs);
        }

        // Lower and middle tiers all the way round.
        S(path, new(0, 0), new(0, 1.4f), 0xffffff, Look.Ribbon);
        S(path, new(0, 1.4f), Lower0, Concrete);
        S(path, Lower0, Lower1, seat, Look.Tier, 2, 6);
        S(path, Lower1, new(22, 11.5f), Concrete);
        S(path, new(22, 11.5f), new(22, 14.5f), 0xffffff, Look.Glass);
        S(path, new(22, 14.5f), new(21, 14.5f), Concrete);
        S(path, new(21, 14.5f), new(21, 15.8f), 0xffffff, Look.Ribbon);
        S(path, new(21, 15.8f), Upper0, Concrete);
        S(path, Upper0, Upper1, seat, Look.Tier, 1, 6);

        // Ends and corners: back wall and the lower roof.
        float panelO = RoofEdge + 5;
        var panelAt = new Vector2(panelO, RoofAt(RoofBack, RoofEdge, RoofH, panelO));
        foreach (var pts in ends) EndRoof(m, pts, panelAt);

        // The main stand rises on: second boxes, the top tier, the high roof.
        float mainPanelO = MainEdge + 6;
        var mainPanelAt = new Vector2(mainPanelO, RoofAt(MainBack, MainEdge, MainH, mainPanelO));
        S(main, Upper1, new(40, 28.5f), Concrete);
        S(main, new(40, 28.5f), new(40, 31.3f), 0xffffff, Look.Glass);
        S(main, new(40, 31.3f), new(39.2f, 31.3f), Concrete);
        S(main, new(39.2f, 31.3f), new(39.2f, 32.4f), 0xffffff, Look.Ribbon);
        S(main, new(39.2f, 32.4f), Top0, Concrete);
        S(main, Top0, Top1, seat, Look.Tier, 1, 6);
        S(main, Top1, new(58, 50), DarkConcrete);
        S(main, new(58, 50), MainBack, DarkConcrete);
        S(main, MainBack, mainPanelAt, RoofCol, Look.Roof);
        S(main, mainPanelAt, new(MainEdge, MainH), Panels);
        S(main, new(MainEdge, MainH - 2.4f), new(MainEdge, MainH), 0xffffff, Look.Fascia);
        S(main, new(MainEdge + 0.3f, MainH - 2.45f), new(MainEdge + 3.4f, MainH - 2.2f), 0xffffff, Look.RoofLight);

        // Open near ends of the corners: the section in concrete; the main stand's flanks
        // above the corner roofs, glazed.
        m.Hex(DarkConcrete);
        Bowl.Caps(m, new[] { path[0], path[^1] }, new Vector2[]
        {
            new(0, 0), new(0, 1.4f), new(0.4f, 1.4f), new(20, 11.5f), new(22, 11.5f), new(22, 14.5f), new(21, 14.5f),
            new(21, 15.8f), new(21.5f, 15.8f), new(38, 28.5f), new(38, 32.5f), new(40, 33.2f), new(42, 33.2f), new(42, 0),
        });
        m.Hex(0x2c333d, Look.Curtain);
        Bowl.Caps(m, new[] { main[0], main[^1] }, new Vector2[]
        {
            new(38, 0), new(60, 0), new(60, MainBack.Y), new(MainEdge, MainH),
            new(MainEdge, RoofAt(RoofBack, RoofEdge, RoofH, MainEdge) + 0.2f), new(38, 33.2f),
        });

        // Steel girders riding on the roofs, one batch with everything else.
        m.Hex(Steel);
        foreach (var pts in ends) Girders(m, pts, RoofBack, RoofEdge, RoofH, 2.6f);
        Girders(m, main, MainBack, MainEdge, MainH, 3.4f);

        // Floodlight lamps along the roof fronts.
        m.Hex(0xffffff, Look.Lamp);
        foreach (var pts in ends) LampRow(m, pts, RoofEdge, RoofH);
        LampRow(m, main, MainEdge, MainH);

        // TV gantry under the main roof.
        var mid = main[main.Count / 2];
        var gantryPos = mid.At(MainEdge + 9, RoofAt(MainBack, MainEdge, MainH, MainEdge + 9) - 2.6f);
        m.Hex(0x23272e);
        m.Box(new Transform3D(Basis.Identity, gantryPos), new Vector3(34, 1.6f, 2.2f));
        for (int k = -3; k <= 3; k++) m.Box(new Transform3D(Basis.Identity, gantryPos + new Vector3(k * 4.6f, 1.1f, 0.6f)), new Vector3(0.7f, 0.6f, 1.1f));

        // Big screens hung under the corner roofs.
        foreach (var pts in ends)
        {
            var p = pts.Where(q => q.NZ < 0).OrderBy(q => Mathf.Abs(Mathf.Abs(q.NX) - Mathf.Abs(q.NZ))).First();
            var c = p.At(RoofEdge + 2.5f, RoofH - 6.2f);
            var basis = new Basis(Vector3.Up, p.FacePitch + Mathf.Pi) * new Basis(Vector3.Right, 0.2f);
            m.Hex(0x23272e);
            m.Box(new Transform3D(basis, c + new Vector3(p.NX, 0, p.NZ) * 0.3f), new Vector3(11.2f, 6.5f, 0.5f));
            m.Hex(0xffffff, Look.Screen);
            var x = basis.X * 5.25f; var y = basis.Y * 2.95f; var f = basis.Z * 0.02f;
            m.QuadUV(c - x - y + f, c + x - y + f, c + x + y + f, c - x + y + f, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }

        Pitchside.Tunnel(m, -BowlZ, DarkConcrete);

        // The paddock on the near side, under the camera: a low open terrace and its wall.
        float cxN = BowlX - BowlR - 8;
        var near = Bowl.Straight(cxN, BowlZ, -cxN, BowlZ, 0, 1);
        S(near, new(0, 0), new(0, 1.2f), 0xffffff, Look.Ribbon);
        S(near, new(0, 1.2f), new(0.4f, 1.2f), Concrete);
        S(near, new(0.4f, 1.2f), new(11, 5.6f), seat, Look.Tier, 1, 3);
        S(near, new(11, 5.6f), new(11, 7.4f), Concrete);
        m.Hex(Concrete);
        Bowl.Caps(m, new[] { near[0], near[^1] }, new Vector2[] { new(0, 0), new(0, 1.2f), new(0.4f, 1.2f), new(11, 5.6f), new(11, 7.4f), new(12, 7.4f), new(12, 0) });

        // The near stand behind the camera: never drawn, but solid for the evening sun.
        var np = Bowl.NearPath(BowlX, BowlZ, BowlR);
        foreach (var (a, b) in new[] { (new Vector2(0, 0), Lower0), (Lower0, Lower1), (Lower1, Upper1), (Upper1, RoofBack), (RoofBack, new Vector2(RoofEdge, RoofH)) })
            Bowl.Strip(ShadowOnly, np, a, b);

        Pitchside.AdBoards(m);
        Pitchside.CornerFlags(m);
        Pitchside.Dugouts(m, Pitchside.HomeKit, Pitchside.AwayKit);
        RailBanners(m, path);

        // ---- the crowd
        float lowerSlope = (Lower1 - Lower0).Length();
        float cz = BowlZ - BowlR;
        Vector2? EndTifo(Vector3 p, float sv, int zone)
        {
            // Card displays over the straight of each end (the corners stay as they are).
            if (zone == 0 || Mathf.Abs(p.Z) > cz - 1 || sv < 0.6f || sv > lowerSlope - 0.4f) return null;
            float u = zone == 1 ? (cz - p.Z) / (2 * cz) : (p.Z + cz) / (2 * cz);
            float v = (sv - 0.6f) / (lowerSlope - 1f);
            return new Vector2(512 + u * 512, (zone == 1 ? 288 : 448) + (1 - v) * 160) / 1024f;
        }
        Crowd.Tier(path, Lower0, Lower1, new TierFans { Shade = new(9, 17), Vom = new(7.2f, 10.2f), Tifo = EndTifo });
        Crowd.Tier(path, Upper0, Upper1, new TierFans { Shade = new(-2, 10) });
        Crowd.Tier(main, Top0, Top1, new TierFans { Shade = new(-2, 6), Fill = 0.9f });
        Crowd.Tier(near, new(0.4f, 1.2f), new(11, 5.6f), new TierFans { Fill = 0.85f });

        // Floodlight banks along the roof fronts (and the near stand's, unseen).
        float r = (BowlR + RoofEdge + 1) * Mathf.Sqrt2 / 2;
        float ex = BowlX + RoofEdge + 1, mz = -(BowlZ + MainEdge + 1);
        float ccx = BowlX - BowlR, ccz = BowlZ - BowlR;
        Lamps = new[]
        {
            new Vector3(-ccx - r, RoofH - 2, -ccz - r), new Vector3(ccx + r, RoofH - 2, -ccz - r),
            new Vector3(-ex, RoofH - 2, -14), new Vector3(-ex, RoofH - 2, 18), new Vector3(ex, RoofH - 2, -14), new Vector3(ex, RoofH - 2, 18),
            new Vector3(-30, MainH - 2, mz), new Vector3(0, MainH - 2, mz), new Vector3(30, MainH - 2, mz),
            new Vector3(-ccx - r, RoofH - 2, ccz + r), new Vector3(ccx + r, RoofH - 2, ccz + r), new Vector3(0, RoofH - 2, BowlZ + RoofEdge + 1),
        };
    }

    /// <summary>Only the banks the camera can see get a glow; the near stand's light the grass only.</summary>
    protected override Vector3[] GlowSpots => Lamps.Take(9).ToArray();

    static uint Darken(uint hex, float k) =>
        ((uint)(((hex >> 16) & 255) * k) << 16) | ((uint)(((hex >> 8) & 255) * k) << 8) | (uint)((hex & 255) * k);

    void EndRoof(MeshData m, List<PathPt> pts, Vector2 panelAt)
    {
        void S(Vector2 a, Vector2 b, uint col, Look look = Look.Plain) { m.Hex(col, look); Bowl.Strip(m, pts, a, b); }
        S(Upper1, new(38, 32.5f), DarkConcrete);
        S(new(38, 32.5f), RoofBack, DarkConcrete);
        S(RoofBack, panelAt, RoofCol, Look.Roof);
        S(panelAt, new(RoofEdge, RoofH), Panels);
        S(new(RoofEdge, RoofH - 2.2f), new(RoofEdge, RoofH), 0xffffff, Look.Fascia);
        S(new(RoofEdge + 0.3f, RoofH - 2.25f), new(RoofEdge + 3, RoofH - 2.05f), 0xffffff, Look.RoofLight);
    }

    /// <summary>Transverse girders above a roof with posts down to the sheet, chords tying them.</summary>
    static void Girders(MeshData m, List<PathPt> pts, Vector2 back, float edge, float h, float lift)
    {
        float[] offs = { back.X - 1, (back.X + edge) * 0.5f, edge + 2 };
        var tops = new List<Vector3[]>();
        for (int i = 0; i < pts.Count; i++)
        {
            if (i % 3 != 0 && i != pts.Count - 1) continue;
            var p = pts[i];
            var row = offs.Select(o => p.At(o, RoofAt(back, edge, h, o) + lift)).ToArray();
            m.Beam(row[0], row[2], 0.45f, 0.9f);
            for (int k = 0; k < 3; k++) m.Beam(row[k], p.At(offs[k], RoofAt(back, edge, h, offs[k])), 0.3f, 0.3f);
            tops.Add(row);
        }
        for (int i = 1; i < tops.Count; i++)
            for (int k = 0; k < 3; k++) m.Beam(tops[i - 1][k], tops[i][k], 0.35f, 0.6f);
    }

    /// <summary>Lamp panels on every other path point, facing the pitch, tilted down.</summary>
    static void LampRow(MeshData m, List<PathPt> pts, float edge, float h)
    {
        for (int i = 0; i < pts.Count; i += 2)
        {
            var p = pts[i];
            var c = p.At(edge + 1.2f, h - 2.7f);
            var basis = new Basis(Vector3.Up, p.FacePitch + Mathf.Pi) * new Basis(Vector3.Right, 0.75f);
            var x = basis.X * 1.7f; var y = basis.Y * 0.55f;
            m.QuadUV(c - x - y, c + x - y, c + x + y, c - x + y, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }
    }

    /// <summary>Supporters' banners over the railings at the front of the lower tier, and the
    /// ultras' big drop banner over the boxes behind the home goal.</summary>
    void RailBanners(MeshData m, List<PathPt> path)
    {
        var straights = new[] { 0, 1, 2 }.Select(z => path.Where(p => p.Zone == z && (z == 0 ? p.NZ < -0.999f : Mathf.Abs(p.NX) > 0.999f)).ToList()).ToArray();
        void Hang(int row, PathPt p, float o, float y, float w, float h)
        {
            m.Hex(0xffffff, Look.Cloth, row);
            var c = p.At(o, y);
            var right = new Vector3(-p.NZ, 0, p.NX);
            int seg = Mathf.Max(2, (int)(w / 1.5f));
            for (int s = 0; s < seg; s++)
            {
                float u0 = s / (float)seg, u1 = (s + 1) / (float)seg;
                var a = c + right * (w * (u0 - 0.5f));
                var b = c + right * (w * (u1 - 0.5f));
                var dy = new Vector3(0, h / 2, 0);
                m.QuadUV(a - dy, b - dy, b + dy, a + dy, new(u0, 0), new(u1, 0), new(u1, 1), new(u0, 1));
            }
        }
        void Rail(int row, int zone, float f, float w)
        {
            var pts = straights[zone];
            Hang(row, pts[Mathf.Min(pts.Count - 1, (int)(f * pts.Count))], -0.08f, 0.72f, w, 1.35f);
        }
        Rail(0, 1, 0.22f, 15); Rail(1, 1, 0.5f, 11); Rail(2, 1, 0.8f, 14);
        Rail(3, 0, 0.12f, 13); Rail(4, 0, 0.36f, 11); Rail(5, 0, 0.6f, 9); Rail(6, 0, 0.86f, 13);
        Rail(7, 2, 0.3f, 15); Rail(8, 2, 0.72f, 10);
        Hang(9, straights[1][straights[1].Count / 2], 20.9f, 13.7f, 34, 4.2f);
    }
}
