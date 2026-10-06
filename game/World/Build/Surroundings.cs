using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds.Build;

/// <summary>
/// What's round the club's own ground: the edge of a city by the sea. A plaza and a ring road
/// round the stands, two tree-lined boulevards, car parks and a fan zone; a small modern
/// district of blocks, streets and parks on the landward side; a seafront drive, a promenade
/// of palms and a beach, rocks and a lighthouse on a point, a marina and boats out on the bay.
/// Across the water the big city's skyline (its windows lit at night), and green hills rolling
/// away inland, mountains behind the far shore.
///
/// Everything is static geometry in the stadium's one draw (the uber shader's Tower, Skyline,
/// Water and Road looks do the windows, glitter and markings), so it costs no extra draws.
///
/// The coast runs straight across the north-east, `Shore` metres out: d is the distance out
/// toward the sea (u), l the distance along the coast (v). The match camera looks over the
/// main stand toward the bay; the builder sees it all round.
/// </summary>
static partial class Surroundings
{
    const float Shore = 334, FarShore = 660, BayEnd = 480;
    /// <summary>Turns a box's x along the coast.</summary>
    static readonly Basis Coast = new(Vector3.Up, -0.6435f);

    static float D(float x, float z) => x * 0.6f - z * 0.8f;
    static float L(float x, float z) => x * 0.8f + z * 0.6f;
    static Vector3 At(float d, float l, float y = 0) => new(0.6f * d + 0.8f * l, y, -0.8f * d + 0.6f * l);
    static bool FarSide(float d, float l) => d > FarShore && l < BayEnd;
    static bool Sea(float x, float z)
    {
        float d = D(x, z), l = L(x, z);
        return d > Shore - 4 && !FarSide(d, l);
    }

    /// <summary>How far a point is outside the stands' front edge.</summary>
    static float Off(float x, float z) =>
        new Vector2(Mathf.Max(Mathf.Abs(x) - Kit.CX, 0), Mathf.Max(Mathf.Abs(z) - Kit.CZ, 0)).Length() - Kit.R;

    // The approaches.
    /// <summary>How far round the stands every area keeps clear (beyond the biggest stands'
    /// backs, stairs and towers), the paved concourse every ground has, and the seaside park's path.</summary>
    const float RoadOut = 92, Apron = 64, ParkPath = 90, Boulevard = 9;
    static bool WestBoulevard(float x, float z, float pad = 0) => x < 0 && Mathf.Abs(z) < Boulevard + 7 + pad;
    static bool SouthBoulevard(float x, float z, float pad = 0) => z > 0 && Mathf.Abs(x) < Boulevard + 7 + pad;
    static readonly Rect2[] CarParks = { new(-196, 150, 170, 48), new(26, 150, 170, 48) };
    static readonly Rect2 FanZone = new(-262, -74, 84, 52), Forecourt = new(-272, -84, 272 - (Kit.BX + Apron - 6), 168);

    static readonly uint[] Leaves = { 0x3f6b33, 0x4c7a38, 0x37602f, 0x58843c, 0x2f5a31 };
    static readonly uint[] Cars = { 0xe8e8e4, 0x1e2024, 0x9aa0a6, 0x5d6268, 0xb8322a, 0x2f5fb8, 0x2a4a3a, 0xd8c49a, 0x7a1f3a, 0xe0b030 };

    /// <summary>The areas a club can build its stadium in, in the order the builder lists them.</summary>
    public static readonly string[] Names = { "Seaside", "Downtown", "Old Town", "Docklands", "Countryside", "Alpine", "Alentejo", "Fjord", "Tropical" };

    public static readonly string[] About =
    {
        "THE EDGE OF A CITY BY THE SEA: PARKS, A BEACH AND THE SKYLINE ACROSS THE BAY",
        "RIGHT IN THE MIDDLE OF THE CITY: AVENUES, A PARK AND THE TOWERS OF DOWNTOWN",
        "AN OLD TOWN OF RED ROOFS BY A RIVER: BRIDGES, A CATHEDRAL AND A CASTLE ON THE HILL",
        "BRICK TERRACES AND THE DOCKS: CRANES, A SHIP, A BRIDGE AND THE POWER STATION",
        "FIELDS AND FARMS ROUND A VILLAGE, WIND TURBINES ON THE RIDGE",
        "A MOUNTAIN VALLEY: A LAKE, PINE FORESTS, CHALETS AND SNOW ON THE PEAKS",
        "A WHITE PORTUGUESE HILL TOWN: A CASTLE, CORK OAKS, OLIVES, VINES AND WINDMILLS",
        "A FJORD: SHEER ROCK WALLS, WATERFALLS, A WOODEN VILLAGE AND THE FERRY",
        "A TROPICAL CITY: COLOURFUL HOUSES UP THE HILLS, GRANITE PEAKS AND THE BEACH",
    };

    /// <summary>The colour of the land that runs on under everything (and round the pitch).</summary>
    public static Vector3 LandOf(int area) => area switch
    {
        1 => new Vector3(0x58, 0x5c, 0x55) / 255f,
        3 => new Vector3(0x4c, 0x55, 0x45) / 255f,
        4 => new Vector3(0x5a, 0x7f, 0x3c) / 255f,
        5 => new Vector3(0x5a, 0x80, 0x3e) / 255f,
        6 => new Vector3(0xb5, 0xa0, 0x62) / 255f,
        7 => new Vector3(0x4c, 0x76, 0x36) / 255f,
        8 => new Vector3(0x8a, 0x8a, 0x84) / 255f,
        _ => new Vector3(0x46, 0x5f, 0x35) / 255f,
    };

    public static int Clamp(int area) => Math.Clamp(area, 0, Names.Length - 1);

    public static void Build(MeshData m, uint home, int area)
    {
        var rng = new Random(1903 + area);
        switch (Clamp(area))
        {
            case 1: Downtown(m, rng, home); return;
            case 2: OldTown(m, rng, home); return;
            case 3: Docklands(m, rng, home); return;
            case 4: Countryside(m, rng, home); return;
            case 5: Alpine(m, rng, home); return;
            case 6: Alentejo(m, rng, home); return;
            case 7: FjordArea(m, rng, home); return;
            case 8: Tropical(m, rng, home); return;
        }
        Ground(m);
        Approaches(m, rng, home);
        District(m, rng);
        Seafront(m, rng);
        Bay(m, rng);
        FarCity(m, rng);
        Hills(m, rng);
    }

    // ---------------------------------------------------------------- the ground plan

    /// <summary>The rounded rectangle round the stands at offset o (closed, corners of 9 steps).</summary>
    static List<Vector3> Ring(float o, float y)
    {
        var pts = new List<Vector3>();
        (float x, float z, float a)[] corners = { (Kit.CX, Kit.CZ, 0), (-Kit.CX, Kit.CZ, 0.5f), (-Kit.CX, -Kit.CZ, 1), (Kit.CX, -Kit.CZ, 1.5f) };
        foreach (var (cx, cz, a0) in corners)
            for (int i = 0; i <= 8; i++)
            {
                float a = (a0 + i / 16f) * Mathf.Pi;
                pts.Add(new Vector3(cx + Mathf.Cos(a) * (Kit.R + o), y, cz + Mathf.Sin(a) * (Kit.R + o)));
            }
        pts.Add(pts[0]);
        return pts;
    }

    static void RingStrip(MeshData m, float o0, float o1, float y)
    {
        var a = Ring(o0, y);
        var b = Ring(o1, y);
        float u = 0;
        for (int i = 0; i + 1 < a.Count; i++)
        {
            float u1 = u + (b[i + 1] - b[i]).Length();
            m.QuadUV(a[i], a[i + 1], b[i + 1], b[i], new(u, 0), new(u1, 0), new(u1, o1 - o0), new(u, o1 - o0));
            u = u1;
        }
    }

    /// <summary>A flat rectangle (x0, z0)..(x1, z1) at y, uv in metres (u along x).</summary>
    static void Flat(MeshData m, float x0, float z0, float x1, float z1, float y) =>
        m.QuadUV(new(x0, y, z0), new(x1, y, z0), new(x1, y, z1), new(x0, y, z1), new(0, 0), new(x1 - x0, 0), new(x1 - x0, z1 - z0), new(0, z1 - z0));

    /// <summary>A flat band in coast coordinates (d0..d1 out, l0..l1 along), uv: along, across.</summary>
    static void Band(MeshData m, float d0, float d1, float l0, float l1, float y) =>
        m.QuadUV(At(d0, l0, y), At(d0, l1, y), At(d1, l1, y), At(d1, l0, y), new(0, 0), new(l1 - l0, 0), new(l1 - l0, d1 - d0), new(0, d1 - d0));

    /// <summary>The seaside ground stands in a park: lawns round it with a looping path under
    /// the trees, a paved forecourt on the town side where the west boulevard arrives (the fan
    /// zone on it), the south boulevard down past the car parks.</summary>
    static void Ground(MeshData m)
    {
        // Lawns out past the pitch's own ground plane (the district and parks sit on them).
        m.Hex(0x557a3c);
        RingStrip(m, Apron, RoadOut + 125, 0.012f);
        Concourse(m, 0xa8a39a, Apron);
        // The path looping round through the park.
        m.Hex(0xc2b79f);
        RingStrip(m, ParkPath - 2, ParkPath + 2, 0.03f);

        // The forecourt: from the concourse out to the fan zone, the boulevard running into it.
        float fx = Forecourt.End.X;
        m.Hex(0xb9b2a6);
        Flat(m, Forecourt.Position.X, Forecourt.Position.Y, fx, Forecourt.End.Y, 0.033f);
        m.Hex(0xa39d92);
        for (float x = Forecourt.Position.X + 6; x < fx; x += 12) Flat(m, x, Forecourt.Position.Y, x + 1, Forecourt.End.Y, 0.036f);

        // The boulevards: west into town, south past the car parks.
        float w0 = Forecourt.Position.X, s0 = Kit.BZ + Apron - 3;
        m.Hex(0x34363a, Look.Road, 2 * Boulevard);
        m.QuadUV(new(w0, 0.045f, -Boulevard), new(w0, 0.045f, Boulevard), new(-520, 0.045f, Boulevard), new(-520, 0.045f, -Boulevard),
            new(0, 0), new(0, 2 * Boulevard), new(520 + w0, 2 * Boulevard), new(520 + w0, 0));
        m.QuadUV(new(-Boulevard, 0.045f, s0), new(Boulevard, 0.045f, s0), new(Boulevard, 0.045f, 520), new(-Boulevard, 0.045f, 520),
            new(0, 0), new(0, 2 * Boulevard), new(520 - s0, 2 * Boulevard), new(520 - s0, 0));
        m.Hex(0xb3aea4);
        foreach (float k in new[] { -1f, 1 })
        {
            Flat(m, -520, Mathf.Min(k * Boulevard, k * (Boulevard + 7)), w0, Mathf.Max(k * Boulevard, k * (Boulevard + 7)), 0.03f);
            Flat(m, Mathf.Min(k * Boulevard, k * (Boulevard + 7)), s0, Mathf.Max(k * Boulevard, k * (Boulevard + 7)), 520, 0.03f);
        }

        // The car parks, a drive into each off the boulevard.
        m.Hex(0x3c3e42, Look.Road, -1);
        foreach (var r in CarParks) Flat(m, r.Position.X, r.Position.Y, r.End.X, r.End.Y, 0.04f);
        m.Hex(0x34363a);
        Flat(m, -26, 166, 26, 182, 0.042f);
        // The fan zone's square.
        m.Hex(0xc9bea8);
        Flat(m, FanZone.Position.X, FanZone.Position.Y, FanZone.End.X, FanZone.End.Y, 0.04f);
    }

    // ---------------------------------------------------------------- round the stadium

    static void Approaches(MeshData m, Random rng, uint home)
    {
        // Trees in the park round the ground, thicker out by the path, not on the forecourt,
        // the boulevards or the car parks; lamps along the path.
        bool Busy(Vector3 p, float pad) =>
            Forecourt.Grow(pad).HasPoint(new(p.X, p.Z)) || WestBoulevard(p.X, p.Z, pad) || SouthBoulevard(p.X, p.Z, pad)
            || CarParks[0].Grow(pad).HasPoint(new(p.X, p.Z)) || CarParks[1].Grow(pad).HasPoint(new(p.X, p.Z));
        Edge(m, rng, home, Apron - 3, Leaves, p => Busy(p, 4), 70);
        foreach (float o in new[] { ParkPath - 6, ParkPath + 6 })
            Along(Ring(o, 0), 11, o, p =>
            {
                var q = p + new Vector3((float)rng.NextDouble() * 4 - 2, 0, (float)rng.NextDouble() * 4 - 2);
                if (!Busy(q, 5) && rng.NextDouble() < 0.75) Tree(m, q, 7 + (float)rng.NextDouble() * 5, Leaves[rng.Next(Leaves.Length)]);
            });
        Along(Ring(ParkPath + 2.6f, 0), 30, 4, p => { if (!Busy(p, 2)) Lamp(m, p); });
        // Clumps of trees out on the lawns.
        for (int i = 0; i < 60; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.Tau, o = ParkPath + 14 + (float)rng.NextDouble() * 70;
            var p = Ring(o, 0)[(int)(a / Mathf.Tau * 36)];
            if (Busy(p, 8) || Off(p.X, p.Z) > RoadOut + 24) continue;
            for (int k = 0; k < 3; k++)
                Tree(m, p + new Vector3((float)rng.NextDouble() * 12 - 6, 0, (float)rng.NextDouble() * 12 - 6), 8 + (float)rng.NextDouble() * 5, Leaves[rng.Next(Leaves.Length)]);
        }
        // Lamps and benches round the forecourt; a row of flagpoles where the boulevard meets it.
        var fc = Forecourt;
        for (float x = fc.Position.X + 8; x < -(Kit.BX + Apron); x += 24)
            foreach (float z in new[] { fc.Position.Y + 3, fc.End.Y - 3 }) Lamp(m, new Vector3(x, 0, z));
        for (int k = -3; k <= 3; k++)
            Parts.Flag(m, new Vector3(fc.Position.X + 30, 0, k * 6), 12, new Vector3(0, 0, 1), 2.6f, 1.5f, k % 2 == 0 ? home : 0xf3eee2);

        // The boulevards: a row of trees and lamps down each side.
        float w0 = fc.Position.X, s0 = Kit.BZ + Apron;
        foreach (float k in new[] { -1f, 1 })
        {
            for (float x = w0 - 8; x > -500; x -= 13)
                Tree(m, new Vector3(x, 0, k * (Boulevard + 3.5f)), 8 + (float)rng.NextDouble() * 2.5f, Leaves[rng.Next(Leaves.Length)]);
            for (float z = s0 + 8; z < 500; z += 13)
                Tree(m, new Vector3(k * (Boulevard + 3.5f), 0, z), 8 + (float)rng.NextDouble() * 2.5f, Leaves[rng.Next(Leaves.Length)]);
            for (float x = w0 - 20; x > -500; x -= 39) Lamp(m, new Vector3(x, 0, k * (Boulevard + 1)));
            for (float z = s0 + 20; z < 500; z += 39) Lamp(m, new Vector3(k * (Boulevard + 1), 0, z));
        }

        // Cars in about two bays in three.
        foreach (var r in CarParks)
            for (float z = r.Position.Y; z + 16 <= r.End.Y + 0.1f; z += 16)
                foreach (float row in new[] { 2.5f, 13.5f })
                    for (float x = r.Position.X + 1.3f; x < r.End.X - 1; x += 2.6f)
                    {
                        if (rng.NextDouble() > 0.55) continue;
                        m.Hex(Cars[rng.Next(Cars.Length)]);
                        m.Box(new Transform3D(Basis.Identity, new Vector3(x, 0.75f, z + row)), new Vector3(1.8f, 1.5f, 4.4f), 1 | 2 | 4 | 16 | 32);
                    }

        // The fan zone: stalls in the club's colour and white, a stage, the crowd milling.
        var fz = FanZone;
        for (int i = 0; i < 7; i++)
            foreach (float side in new[] { 0f, 1 })
            {
                var c = new Vector3(fz.Position.X + 8 + i * 11.5f, 0, side < 0.5f ? fz.Position.Y + 4 : fz.End.Y - 4);
                m.Hex(0xe9e4d8);
                m.Box(new Transform3D(Basis.Identity, c + new Vector3(0, 1.3f, 0)), new Vector3(5, 2.6f, 3.4f), 1 | 2 | 16 | 32);
                m.Hex(i % 2 == 0 ? home : 0xf3eee2);
                m.Box(new Transform3D(Basis.Identity, c + new Vector3(0, 2.9f, 0)), new Vector3(5.6f, 0.6f, 4.2f), 63);
            }
        m.Hex(0x2a2c33);
        m.Box(new Transform3D(Basis.Identity, new Vector3(fz.Position.X + 6, 1, fz.GetCenter().Y)), new Vector3(8, 2, 16), 63);
        m.Hex(0xffffff, Look.Lamp);
        m.Box(new Transform3D(Basis.Identity, new Vector3(fz.Position.X + 3, 7, fz.GetCenter().Y)), new Vector3(0.4f, 1, 14), 1);

        // Fans: thick in the fan zone and across the forecourt, thinning out up the boulevards.
        for (int i = 0; i < 90; i++)
            Fan(m, rng, home, new Vector3(fz.Position.X + 14 + (float)rng.NextDouble() * (fz.Size.X - 18), 0, fz.Position.Y + 9 + (float)rng.NextDouble() * (fz.Size.Y - 18)));
        for (int i = 0; i < 70; i++)
            Fan(m, rng, home, new Vector3(fc.Position.X + (float)rng.NextDouble() * (-(Kit.BX + Apron) - fc.Position.X), 0, fc.Position.Y + 4 + (float)rng.NextDouble() * (fc.Size.Y - 8)));
        for (int i = 0; i < 60; i++)
        {
            float t = (float)Math.Pow(rng.NextDouble(), 2) * 260;
            float a = (float)rng.NextDouble() * 10 - 5;
            Fan(m, rng, home, rng.Next(2) == 0 ? new Vector3(w0 - 4 - t, 0, (Boulevard + 4) * (rng.Next(2) * 2 - 1) + a * 0.3f) : new Vector3((Boulevard + 4) * (rng.Next(2) * 2 - 1) + a * 0.3f, 0, s0 + 4 + t));
        }
    }

    // ---------------------------------------------------------------- the district

    static void District(MeshData m, Random rng)
    {
        const float C = 34, Inset = 5;
        uint[] walls = { 0xe6e3dc, 0xb8bcc0, 0xcfc3ad, 0x4a4e55, 0xb5735a, 0x8fa38a, 0xd9d4c7, 0x6d7a86 };
        for (int i = -14; i <= 14; i++)
            for (int j = -14; j <= 14; j++)
            {
                float cx = i * C, cz = j * C;
                float r = new Vector2(cx, cz).Length();
                if (r > 470 || Off(cx, cz) < RoadOut + 22 || D(cx, cz) > 276) continue;
                if (WestBoulevard(cx, cz, C / 2) || SouthBoulevard(cx, cz, C / 2)) continue;
                var cell = new Rect2(cx - C / 2, cz - C / 2, C, C);
                bool blocked = cell.Intersects(Forecourt.Grow(4));
                foreach (var p in CarParks) blocked |= cell.Intersects(p.Grow(4));
                if (blocked) continue;
                float h0 = Hash(i, j);
                bool park = h0 < 0.24f || D(cx, cz) > 214;

                // The street round the block, the block itself.
                m.Hex(0x3a3c40);
                Flat(m, cell.Position.X, cell.Position.Y, cell.End.X, cell.End.Y, 0.025f);
                var blk = cell.Grow(-Inset);
                m.Hex(park ? 0x557a3cu : 0x9d9990u);
                Flat(m, blk.Position.X, blk.Position.Y, blk.End.X, blk.End.Y, 0.035f);
                if (park)
                {
                    // A park: a path across it and trees about.
                    m.Hex(0xc2b79f);
                    m.QuadUV(new(blk.Position.X, 0.04f, blk.Position.Y + 1), new(blk.Position.X + 1.4f, 0.04f, blk.Position.Y), new(blk.End.X, 0.04f, blk.End.Y - 1), new(blk.End.X - 1.4f, 0.04f, blk.End.Y), new(0, 0), new(1, 0), new(1, 1), new(0, 1));
                    int n = 3 + rng.Next(4);
                    for (int k = 0; k < n; k++)
                        Tree(m, new Vector3(blk.Position.X + 2 + (float)rng.NextDouble() * (blk.Size.X - 4), 0, blk.Position.Y + 2 + (float)rng.NextDouble() * (blk.Size.Y - 4)),
                            7 + (float)rng.NextDouble() * 5, Leaves[rng.Next(Leaves.Length)]);
                    continue;
                }

                // One building or two, taller further from the ground; now and then a tower.
                bool tower = Hash(j, i + 31) < 0.09f && r > 220;
                int count = !tower && Hash(i + 7, j) < 0.45f ? 2 : 1;
                for (int k = 0; k < count; k++)
                {
                    float w = count == 2 ? blk.Size.X - 2 : 14 + Hash(i, j + k * 5) * (blk.Size.X - 16);
                    float dep = count == 2 ? (blk.Size.Y - 4) / 2 : 14 + Hash(i + 3, j + k) * (blk.Size.Y - 16);
                    float h = tower ? 40 + Hash(i, j + 9) * 26 : Mathf.Lerp(10, 22, Mathf.Clamp((r - 150) / 300, 0, 1)) + Hash(i + 1, j + k * 3) * 13;
                    var c = new Vector3(blk.GetCenter().X, 0, count == 2 ? blk.Position.Y + 1 + dep / 2 + k * (dep + 2) : blk.GetCenter().Y);
                    Building(m, c, new Vector3(w, h, dep), walls[(int)(Hash(i + 11, j + k) * walls.Length)], (int)(Hash(i + 5, j - k) * 3), Hash(i - 2, j + k) < 0.3f);
                }
                // A street tree on the block's corner.
                Tree(m, new Vector3(blk.Position.X + 1.5f, 0, blk.Position.Y + 1.5f), 7, Leaves[rng.Next(Leaves.Length)]);
            }
    }

    /// <summary>A modern block: walls in the Tower look (style 0 glass, 1 ribbon windows, 2
    /// punched windows), a flat roof (green on some) with a plant room on it.</summary>
    static void Building(MeshData m, Vector3 c, Vector3 size, uint wall, int style, bool green)
    {
        m.Hex(wall, Look.Tower, style);
        m.Box(new Transform3D(Basis.Identity, c + new Vector3(0, size.Y / 2, 0)), size, 1 | 2 | 16 | 32);
        m.Hex(green ? 0x5d7a45u : 0x77746eu);
        m.Box(new Transform3D(Basis.Identity, c + new Vector3(0, size.Y, 0)), new Vector3(size.X, 0.01f, size.Z), 4);
        // A plant room.
        m.Hex(0x8a8780);
        m.Box(new Transform3D(Basis.Identity, c + new Vector3(size.X * 0.15f, size.Y + 1.4f, -size.Z * 0.1f)), new Vector3(size.X * 0.3f, 2.8f, size.Z * 0.35f), 1 | 2 | 4 | 16 | 32);
    }

    // ---------------------------------------------------------------- the seafront

    static void Seafront(MeshData m, Random rng)
    {
        const float l0 = -900, l1 = 1100;
        // Parkland down to the drive, the drive, the promenade, the beach.
        m.Hex(0x4f7438);
        Band(m, 276, 286, l0, l1, 0.02f);
        m.Hex(0x34363a, Look.Road, 12);
        Band(m, 286, 298, l0, l1, 0.045f);
        m.Hex(0xcfc6b2);
        Band(m, 298, 318, l0, l1, 0.035f);
        m.Hex(0x8a8478);
        Band(m, 317, 318.5f, l0, l1, 0.4f);
        m.Hex(0xdcc9a0);
        Band(m, 318, Shore + 2, l0, l1, 0.03f);
        // Wet sand at the water's edge.
        m.Hex(0xa8946c);
        Band(m, Shore - 3, Shore + 2, l0, l1, 0.035f);

        for (float l = l0 + 6; l < l1; l += 14)
        {
            if (l > -330 && l < -190) continue; // the point
            Palm(m, At(308, l + (float)rng.NextDouble() * 4), 7 + (float)rng.NextDouble() * 3, (float)rng.NextDouble() * Mathf.Tau);
            if (((int)(l / 14) & 1) == 0) Lamp(m, At(299.5f, l + 7));
        }
        // Parkland trees behind the drive.
        for (float l = l0; l < l1; l += 9)
        {
            float d = 252 + (float)rng.NextDouble() * 30;
            var p = At(d, l + (float)rng.NextDouble() * 6);
            if (Off(p.X, p.Z) < RoadOut + 8 || d > 283 || rng.NextDouble() < 0.35) continue;
            Tree(m, p, 7 + (float)rng.NextDouble() * 5, Leaves[rng.Next(Leaves.Length)]);
        }

        // The point: rocks out into the sea and a lighthouse on them.
        m.Hex(0x5f5a52);
        for (int i = 0; i < 26; i++)
        {
            float d = Shore - 6 + (float)rng.NextDouble() * 34, l = -260 + (float)(rng.NextDouble() - 0.5) * (130 - (d - Shore) * 2.4f);
            var s = new Vector3(3 + (float)rng.NextDouble() * 5, 1.5f + (float)rng.NextDouble() * 3.5f, 3 + (float)rng.NextDouble() * 5);
            m.Blob(At(d, l), s, 5, 2, half: true);
        }
        var lh = At(Shore + 22, -260);
        m.Hex(0x6b655b);
        m.Blob(lh, new Vector3(9, 4.5f, 9), 6, 2, half: true);
        m.Hex(0xf1eee6);
        m.Column(lh + new Vector3(0, 3, 0), 2.6f, 1.8f, 20, 8);
        m.Hex(0xb8322a);
        m.Column(lh + new Vector3(0, 11, 0), 2.25f, 2.05f, 3.5f, 8);
        m.Hex(0xffffff, Look.RoofLight);
        m.Column(lh + new Vector3(0, 23, 0), 1.5f, 1.5f, 2.2f, 8);
        m.Hex(0xb8322a);
        m.Column(lh + new Vector3(0, 25.2f, 0), 2.1f, 0, 2.4f, 8);
        // Rocks scattered along the rest of the shore.
        m.Hex(0x635e55);
        for (int i = 0; i < 30; i++)
        {
            float l = l0 + (float)rng.NextDouble() * (l1 - l0);
            if (l > 60 && l < 300) continue; // the marina
            m.Blob(At(Shore - 1 + (float)rng.NextDouble() * 6, l), new Vector3(2 + (float)rng.NextDouble() * 3, 1 + (float)rng.NextDouble() * 1.5f, 2 + (float)rng.NextDouble() * 3), 5, 2, half: true);
        }

        // The marina: two breakwaters and the boats moored inside.
        m.Hex(0x9a968c);
        m.Box(new Transform3D(Coast, At(Shore + 38, 120, 1.2f)), new Vector3(5, 2.4f, 80), 63);
        m.Box(new Transform3D(Coast, At(Shore + 76, 168, 1.2f)), new Vector3(96, 2.4f, 5), 63);
        m.Hex(0x7a6a55);
        for (int i = 0; i < 4; i++) m.Box(new Transform3D(Coast, At(Shore + 14 + i * 14, 210, 0.6f)), new Vector3(30, 0.4f, 2), 63);
        for (int i = 0; i < 4; i++)
            for (int k = 0; k < 6; k++)
            {
                if (rng.NextDouble() < 0.25) continue;
                var p = At(Shore + 10 + i * 14 + (k % 2) * 8 - 4, 198 + k * 4.5f);
                Boat(m, p, -0.6435f + Mathf.Pi / 2, rng.NextDouble() < 0.5, 0.7f, rng);
            }
    }

    // ---------------------------------------------------------------- the bay

    static void Bay(MeshData m, Random rng)
    {
        // The sea, out to the horizon, under the far shore.
        m.Hex(0x15405a, Look.Water);
        Band(m, Shore - 4, 2400, -2400, 2400, 0);
        // Boats out on the water.
        for (int i = 0; i < 16; i++)
        {
            float d = 380 + (float)rng.NextDouble() * 240, l = -700 + (float)rng.NextDouble() * 1500;
            if (FarSide(d + 20, l)) continue;
            Boat(m, At(d, l), (float)rng.NextDouble() * Mathf.Tau, rng.NextDouble() < 0.7, 1, rng);
        }
        // A ferry crossing to the city.
        m.Hex(0xf1eee6);
        var fp = At(520, -60, 2);
        m.Box(new Transform3D(Coast, fp), new Vector3(46, 4, 11), 1 | 2 | 4 | 16 | 32);
        m.Box(new Transform3D(Coast, fp + new Vector3(0, 3.5f, 0)), new Vector3(30, 3, 9), 1 | 2 | 4 | 16 | 32);
        m.Hex(0x2f5fb8);
        m.Box(new Transform3D(Coast, fp + new Vector3(0, 0.4f, 0)), new Vector3(46.2f, 1, 11.2f), 1 | 2 | 16 | 32);
        m.Hex(0xb8322a);
        m.Box(new Transform3D(Coast, fp + new Vector3(4, 7, 0)), new Vector3(3, 3, 3), 1 | 2 | 4 | 16 | 32);
    }

    /// <summary>A boat on the water: a sailing boat (mast and sail) or a motor boat (a cabin).</summary>
    static void Boat(MeshData m, Vector3 p, float yaw, bool sail, float k, Random rng)
    {
        var b = new Basis(Vector3.Up, yaw);
        float len = (sail ? 9 : 8) * k * (0.8f + (float)rng.NextDouble() * 0.6f);
        m.Hex(rng.NextDouble() < 0.75 ? 0xf1eee6u : 0x1f2c5cu);
        m.Prism(new Transform3D(b * new Basis(Vector3.Up, Mathf.Pi / 2), p + new Vector3(0, 0, 0)),
            new Vector2[] { new(-len / 2, 0), new(len / 2 - len * 0.2f, 0), new(len / 2, len * 0.14f), new(-len / 2, len * 0.14f) }, len * 0.32f);
        if (sail)
        {
            var mast = p + b * new Vector3(0, 0, len * 0.05f);
            m.Hex(0xd8d8d4);
            m.Box(new Transform3D(b, mast + new Vector3(0, len * 0.6f, 0)), new Vector3(0.2f, len * 1.1f, 0.2f), 1 | 2 | 16 | 32);
            m.Hex(0xf6f3ea);
            m.Tri(mast + new Vector3(0, len * 0.22f, 0), mast + new Vector3(0, len * 1.12f, 0), mast + b * new Vector3(0, len * 0.22f, -len * 0.45f), new(0, 0), new(0, 1), new(1, 0));
        }
        else
        {
            m.Hex(0xe9e6de);
            m.Box(new Transform3D(b, p + b * new Vector3(0, len * 0.2f, len * 0.08f)), new Vector3(len * 0.24f, len * 0.13f, len * 0.36f), 1 | 2 | 4 | 16 | 32);
        }
    }

    // ---------------------------------------------------------------- the city across the bay

    static void FarCity(MeshData m, Random rng)
    {
        m.Hex(0x515a4c);
        Band(m, FarShore, 2400, -2400, BayEnd, 0.12f);
        m.Hex(0x8d8a82);
        Band(m, FarShore, FarShore + 8, -2400, BayEnd, 0.16f);
        // The headland at the bay's end.
        m.Hex(0x5a6250);
        m.Blob(At(FarShore + 30, BayEnd - 10), new Vector3(60, 22, 60), 7, 3, half: true);

        uint[] cols = { 0x8a96a3, 0x5f7f8a, 0xc9c6bd, 0x404852, 0x9aa5ae, 0x6e7c88 };
        int tall = 0;
        for (int row = 0; row < 5; row++)
            for (float l = -1300 + row * 9; l < BayEnd - 50; l += 24 + (float)rng.NextDouble() * 10)
            {
                float d = FarShore + 24 + row * 36 + (float)rng.NextDouble() * 10;
                // Downtown rises across the water, a little left of the stadium's line of sight.
                float peak = 24 + 116 * Mathf.Exp(-Mathf.Pow((l + 140) / 240, 2)) + 34 * Mathf.Exp(-Mathf.Pow((l + 700) / 160, 2));
                float h = Mathf.Max(14, peak * (0.4f + (float)rng.NextDouble() * 0.75f) * (1 - row * 0.06f));
                float w = 16 + (float)rng.NextDouble() * 14, dep = 16 + (float)rng.NextDouble() * 12;
                var c = At(d, l);
                m.Hex(cols[rng.Next(cols.Length)], Look.Skyline, rng.NextDouble() < 0.6 ? 0 : rng.Next(1, 3));
                m.Box(new Transform3D(Coast, c + new Vector3(0, h / 2, 0)), new Vector3(w, h, dep), 1 | 2 | 4 | 16 | 32);
                if (h > 80)
                {
                    // A setback crown, and a spire with a warning light on the tallest.
                    float ch = 6 + (float)rng.NextDouble() * 10;
                    m.Box(new Transform3D(Coast, c + new Vector3(0, h + ch / 2, 0)), new Vector3(w * 0.7f, ch, dep * 0.7f), 1 | 2 | 4 | 16 | 32);
                    if (h > 120 && tall++ < 5)
                    {
                        m.Hex(0xb8bcc0);
                        m.Column(c + new Vector3(0, h + ch, 0), 0.9f, 0, 18 + (float)rng.NextDouble() * 14, 4);
                    }
                    m.Hex(0xff2a1a, Look.Unlit);
                    m.Box(new Transform3D(Coast, c + new Vector3(0, h + ch + 0.6f, 0)), new Vector3(1.2f, 1.2f, 1.2f), 1 | 2 | 4 | 16 | 32);
                }
            }
        // The city's tower: a needle with a lit pod.
        var nt = At(FarShore + 60, -230);
        m.Hex(0xc9c6bd);
        m.Column(nt, 6, 2.4f, 170, 6);
        m.Hex(0x404852, Look.Skyline, 0);
        m.Column(nt + new Vector3(0, 150, 0), 10, 10, 12, 8);
        m.Hex(0xb8bcc0);
        m.Column(nt + new Vector3(0, 170, 0), 1.6f, 0, 42, 4);
        m.Hex(0xff2a1a, Look.Unlit);
        m.Box(new Transform3D(Basis.Identity, nt + new Vector3(0, 212, 0)), new Vector3(1.6f, 1.6f, 1.6f), 63);
    }

    // ---------------------------------------------------------------- hills and mountains

    static float HillH(float x, float z)
    {
        float d = D(x, z), l = L(x, z);
        if (FarSide(d, l)) return Kit.Smooth((d - 880) / 220) * (60 + 170 * Noise(x * 0.004f, z * 0.004f));
        float r = Mathf.Sqrt(x * x + z * z);
        float inland = Kit.Smooth((Shore - 30 - d) / 140);
        return Kit.Smooth((r - 520) / 300) * inland * (16 + 80 * Noise(x * 0.005f + 3, z * 0.005f + 1));
    }

    static void Hills(MeshData m, Random rng)
    {
        const float C = 60, E = 1680;
        int n = (int)(2 * E / C);
        var h = new float[n + 1, n + 1];
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++) h[i, j] = HillH(-E + i * C, -E + j * C);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                float h00 = h[i, j], h10 = h[i + 1, j], h01 = h[i, j + 1], h11 = h[i + 1, j + 1];
                float top = Mathf.Max(Mathf.Max(h00, h10), Mathf.Max(h01, h11));
                if (top < 0.6f) continue;
                float x0 = -E + i * C, z0 = -E + j * C;
                if (new Vector2(x0 + C / 2, z0 + C / 2).Length() > E) continue;
                float avg = (h00 + h10 + h01 + h11) / 4;
                bool far = FarSide(D(x0 + C / 2, z0 + C / 2), L(x0 + C / 2, z0 + C / 2));
                float nz = Noise(x0 * 0.02f, z0 * 0.02f);
                uint col = far ? (avg > 150 ? 0x6e6a62u : avg > 90 ? 0x56604au : 0x4a5a40u)
                    : avg > 70 ? 0x5f7444u : nz > 0.55f ? 0x4c6a36u : nz > 0.3f ? 0x587a3eu : 0x45633au;
                m.Hex(col);
                Vector3 a = new(x0, h00 - 0.2f, z0), b = new(x0 + C, h10 - 0.2f, z0), c = new(x0 + C, h11 - 0.2f, z0 + C), d = new(x0, h01 - 0.2f, z0 + C);
                m.Tri(a, d, c, Vector2.Zero, Vector2.Zero, Vector2.Zero);
                m.Tri(a, c, b, Vector2.Zero, Vector2.Zero, Vector2.Zero);

                // Woods on the inland slopes.
                if (far || avg < 4 || avg > 75 || Noise(x0 * 0.012f + 9, z0 * 0.012f) < 0.45f || new Vector2(x0, z0).Length() > 1150) continue;
                for (int k = 0; k < 3; k++)
                {
                    float u = (float)rng.NextDouble(), v = (float)rng.NextDouble();
                    float y = Mathf.Lerp(Mathf.Lerp(h00, h10, u), Mathf.Lerp(h01, h11, u), v) - 1;
                    m.Hex(Leaves[rng.Next(Leaves.Length)]);
                    m.Column(new Vector3(x0 + u * C, y, z0 + v * C), 4 + (float)rng.NextDouble() * 2, 0, 12 + (float)rng.NextDouble() * 6, 4, (float)rng.NextDouble());
                }
            }
    }

    // ---------------------------------------------------------------- pieces

    static void Tree(MeshData m, Vector3 p, float h, uint leaf)
    {
        m.Hex(0x5a4632);
        m.Box(new Transform3D(Basis.Identity, p + new Vector3(0, h * 0.22f, 0)), new Vector3(0.45f, h * 0.44f, 0.45f), 1 | 2 | 16 | 32);
        m.Hex(leaf);
        m.Blob(p + new Vector3(0, h * 0.62f, 0), new Vector3(h * 0.32f, h * 0.4f, h * 0.32f), 4, 2);
    }

    static void Palm(MeshData m, Vector3 p, float h, float yaw)
    {
        var lean = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw)) * h * 0.18f;
        var top = p + new Vector3(0, h, 0) + lean;
        m.Hex(0x7a6448);
        m.Beam(p, top, 0.4f, 0.4f);
        m.Hex(0x4f7a3a);
        for (int i = 0; i < 6; i++)
        {
            float a = yaw + i * Mathf.Tau / 6;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var side = new Vector3(-dir.Z, 0, dir.X) * 0.7f;
            var tip = top + dir * 3.6f + new Vector3(0, -1.6f, 0);
            var mid = top + dir * 1.8f + new Vector3(0, 0.3f, 0);
            m.QuadUV(top, mid + side, tip, mid - side, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }
    }

    static void Lamp(MeshData m, Vector3 p)
    {
        m.Hex(0x3a3d42);
        m.Box(new Transform3D(Basis.Identity, p + new Vector3(0, 4, 0)), new Vector3(0.22f, 8, 0.22f), 1 | 2 | 16 | 32);
        m.Hex(0xffffff, Look.Lamp);
        m.Box(new Transform3D(Basis.Identity, p + new Vector3(0, 8, 0)), new Vector3(0.9f, 0.3f, 0.9f), 8 | 4);
    }

    /// <summary>Calls `at` every `step` metres along a polyline (from `start`).</summary>
    static void Along(List<Vector3> pts, float step, float start, Action<Vector3> at)
    {
        float next = start, u = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            float len = (pts[i + 1] - pts[i]).Length();
            while (next <= u + len)
            {
                at(pts[i].Lerp(pts[i + 1], len > 0 ? (next - u) / len : 0));
                next += step;
            }
            u += len;
        }
    }

    static float Hash(float a, float b) => Mathf.PosMod(Mathf.Sin(a * 127.1f + b * 311.7f) * 43758.547f, 1f);

    /// <summary>Smooth value noise, two octaves (0..1).</summary>
    static float Noise(float x, float z) => Value(x, z) * 0.65f + Value(x * 2.3f + 5.2f, z * 2.3f + 1.3f) * 0.35f;

    static float Value(float x, float z)
    {
        float ix = Mathf.Floor(x), iz = Mathf.Floor(z), fx = x - ix, fz = z - iz;
        fx = fx * fx * (3 - 2 * fx);
        fz = fz * fz * (3 - 2 * fz);
        return Mathf.Lerp(Mathf.Lerp(Hash(ix, iz), Hash(ix + 1, iz), fx), Mathf.Lerp(Hash(ix, iz + 1), Hash(ix + 1, iz + 1), fx), fz);
    }
}
