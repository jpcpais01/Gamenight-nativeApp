using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds.Build;

/// <summary>
/// The other places a club can build its ground: in the middle of a city, in an old river town,
/// among the docks and terraces, out in the fields, and up a mountain valley. Each keeps the
/// plaza and the ring road round the stands and puts its landmark behind the main stand (the
/// -z side), where the match camera looks.
/// </summary>
static partial class Surroundings
{
    static float Sq(float v) => v * v;

    // ---------------------------------------------------------------- shared pieces

    /// <summary>The plaza and the ring road every area keeps, and a band of `verge` beyond it.</summary>
    static void Core(MeshData m, uint plaza, uint verge, float vergeOut)
    {
        if (vergeOut > 0)
        {
            m.Hex(verge);
            RingStrip(m, RoadOut + 4, RoadOut + vergeOut, 0.012f);
        }
        m.Hex(plaza);
        RingStrip(m, 2, RoadIn - 4, 0.02f);
        m.Hex(0xc3beb3);
        RingStrip(m, RoadIn - 4, RoadIn, 0.025f);
        m.Hex(0x34363a, Look.Road, RoadOut - RoadIn);
        RingStrip(m, RoadIn, RoadOut, 0.04f);
        m.Hex(0xb3aea4);
        RingStrip(m, RoadOut, RoadOut + 5, 0.03f);
    }

    /// <summary>Trees round the plaza, lamps along the ring road, fans milling about.</summary>
    static void Plaza(MeshData m, Random rng, uint home, uint[] leaves = null)
    {
        leaves ??= Leaves;
        var edge = Ring(RoadIn - 6, 0);
        Along(edge, 15, 0, p => Tree(m, p, 7 + (float)rng.NextDouble() * 3, leaves[rng.Next(leaves.Length)]));
        Along(Ring(RoadOut + 2.5f, 0), 32, 8, p => Lamp(m, p));
        for (int i = 0; i < 110; i++)
        {
            var p = edge[rng.Next(edge.Count)];
            Fan(m, rng, home, p + new Vector3((float)rng.NextDouble() * 12 - 6, 0, (float)rng.NextDouble() * 12 - 6));
        }
    }

    static void Fan(MeshData m, Random rng, uint home, Vector3 p)
    {
        m.Hex(rng.NextDouble() < 0.7 ? home : Cars[rng.Next(Cars.Length)]);
        m.Box(new Transform3D(Basis.Identity, p + new Vector3(0, 0.88f, 0)), new Vector3(0.55f, 1.75f, 0.45f), 1 | 2 | 4 | 16 | 32);
    }

    static void Car(MeshData m, Vector3 p, float yaw, Random rng)
    {
        m.Hex(Cars[rng.Next(Cars.Length)]);
        m.Box(new Transform3D(new Basis(Vector3.Up, yaw), p + new Vector3(0, 0.75f, 0)), new Vector3(1.8f, 1.5f, 4.4f), 1 | 2 | 4 | 16 | 32);
    }

    /// <summary>A flat strip w wide from a to b (uv: along, across), for roads and paths.</summary>
    static void Strip(MeshData m, Vector3 a, Vector3 b, float w, float u0 = 0)
    {
        var d = b - a;
        d.Y = 0;
        float len = d.Length();
        if (len < 0.01f) return;
        var n = new Vector3(-d.Z, 0, d.X) / len * (w / 2);
        m.QuadUV(a - n, a + n, b + n, b - n, new(u0, 0), new(u0, w), new(u0 + len, w), new(u0 + len, 0));
    }

    /// <summary>A building with a pitched roof: walls (in the colour the caller set) size.X long
    /// along yaw, size.Z deep, a ridge `rise` above the eaves along its length.</summary>
    static void Gable(MeshData m, Vector3 c, float yaw, Vector3 size, float rise, uint roof, float eave = 0.5f)
    {
        var b = new Basis(Vector3.Up, yaw);
        m.Box(new Transform3D(b, c + new Vector3(0, size.Y / 2, 0)), size, 1 | 2 | 16 | 32);
        m.Hex(roof);
        float hd = size.Z / 2 + eave;
        m.Prism(new Transform3D(b * new Basis(Vector3.Up, Mathf.Pi / 2), c + new Vector3(0, size.Y, 0)),
            new Vector2[] { new(-hd, 0), new(hd, 0), new(0, rise) }, size.X + 2 * eave);
    }

    /// <summary>A church: a nave, a tower at its west end and a spire.</summary>
    static void Church(MeshData m, Vector3 c, float yaw, uint stone, uint roof, float k = 1)
    {
        var b = new Basis(Vector3.Up, yaw);
        m.Hex(stone);
        Gable(m, c, yaw, new Vector3(26, 11, 11) * k, 6 * k, roof, 0.4f);
        var t = c + b * new Vector3(-15 * k, 0, 0);
        m.Hex(stone);
        m.Box(new Transform3D(b, t + new Vector3(0, 13 * k, 0)), new Vector3(7, 26, 7) * k, 1 | 2 | 4 | 16 | 32);
        m.Hex(roof);
        m.Column(t + new Vector3(0, 26 * k, 0), 4.6f * k, 0, 17 * k, 4, Mathf.Pi / 4 - yaw);
    }

    static void Pine(MeshData m, Vector3 p, float h, float phase, uint col)
    {
        m.Hex(col);
        m.Column(p + new Vector3(0, -0.5f, 0), h * 0.27f, 0, h, 5, phase);
    }

    /// <summary>A wind turbine, its rotor facing `yaw` (blades frozen at `spin`).</summary>
    static void Turbine(MeshData m, Vector3 p, float h, float yaw, float spin)
    {
        var face = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
        var side = new Vector3(face.Z, 0, -face.X);
        m.Hex(0xe9ebec);
        m.Column(p - new Vector3(0, 1, 0), 2.3f, 1.3f, h + 1, 6);
        var top = p + new Vector3(0, h, 0);
        m.Box(new Transform3D(new Basis(Vector3.Up, yaw), top + new Vector3(0, 1.4f, 0) - face * 1.5f), new Vector3(3, 3, 9), 63);
        var hub = top + new Vector3(0, 1.4f, 0) + face * 3.6f;
        for (int i = 0; i < 3; i++)
        {
            float a = spin + i * Mathf.Tau / 3;
            m.Beam(hub, hub + (Vector3.Up * Mathf.Cos(a) + side * Mathf.Sin(a)) * h * 0.46f, 1.5f, 0.5f);
        }
    }

    /// <summary>One square of a height field (corner heights; At(u, v) is a point on it).</summary>
    readonly record struct Cell(float X0, float Z0, float C, float H00, float H10, float H01, float H11)
    {
        public float Avg => (H00 + H10 + H01 + H11) / 4;
        public float Slope => (Mathf.Max(Mathf.Max(H00, H10), Mathf.Max(H01, H11)) - Mathf.Min(Mathf.Min(H00, H10), Mathf.Min(H01, H11))) / C;
        public Vector2 Mid => new(X0 + C / 2, Z0 + C / 2);
        public Vector3 At(float u, float v) => new(X0 + u * C, Mathf.Lerp(Mathf.Lerp(H00, H10, u), Mathf.Lerp(H01, H11, u), v), Z0 + v * C);
    }

    /// <summary>A height field of C-metre squares out to E, each in the colour `col` gives it (0:
    /// leave it out), raised `lift`; `each` dresses a square once it's laid.</summary>
    static void Terrain(MeshData m, float C, float E, Func<float, float, float> H, Func<Cell, uint> col, float lift, Action<Cell> each = null)
    {
        int n = (int)(2 * E / C);
        var h = new float[n + 1, n + 1];
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++) h[i, j] = H(-E + i * C, -E + j * C);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                var c = new Cell(-E + i * C, -E + j * C, C, h[i, j], h[i + 1, j], h[i, j + 1], h[i + 1, j + 1]);
                if (c.Mid.Length() > E) continue;
                uint k = col(c);
                if (k == 0) continue;
                // A touch lighter or darker square to square, so slopes don't read as one flat sheet.
                m.Hex(Kit.Darken(k, 0.93f + 0.12f * Hash(c.X0 * 0.37f + 1.3f, c.Z0 * 0.53f)));
                Vector3 a = new(c.X0, c.H00 + lift, c.Z0), b = new(c.X0 + C, c.H10 + lift, c.Z0), cc = new(c.X0 + C, c.H11 + lift, c.Z0 + C), d = new(c.X0, c.H01 + lift, c.Z0 + C);
                m.Tri(a, d, cc, Vector2.Zero, Vector2.Zero, Vector2.Zero);
                m.Tri(a, cc, b, Vector2.Zero, Vector2.Zero, Vector2.Zero);
                each?.Invoke(c);
            }
    }

    /// <summary>Far hills all round, out past `r0` (for the areas that have no horizon of their own).</summary>
    static void FarHills(MeshData m, float r0, float height)
    {
        Terrain(m, 80, 1680, (x, z) => Kit.Smooth((Mathf.Sqrt(x * x + z * z) - r0) / 420) * (height * 0.25f + height * Noise(x * 0.004f + 3, z * 0.004f + 1)),
            c => c.Avg < 0.6f ? 0u : c.Avg > height * 0.7f ? 0x5a6a48u : Noise(c.X0 * 0.02f, c.Z0 * 0.02f) > 0.5f ? 0x4c6a36u : 0x587a3eu, -0.2f);
    }

    // ---------------------------------------------------------------- downtown

    /// <summary>The ground in the middle of a city: a grid of blocks rising to the towers of
    /// downtown behind the main stand, four avenues out from the ring road, a park, an elevated
    /// highway on the west side.</summary>
    static void Downtown(MeshData m, Random rng, uint home)
    {
        const float C = 44, Inset = 6, Av = 12, Hw = -330;
        Core(m, 0xa8a39a, 0x8d8a85, 125);
        var park = new Rect2(170, 150, 300, 230);
        uint[] walls = { 0x5f7f8a, 0x8a96a3, 0x404852, 0x9aa5ae, 0x6e7c88, 0xc9c6bd, 0xd9d4c7, 0xb5735a, 0x4a4e55, 0xcfc3ad };
        int spires = 0;
        for (int i = -20; i <= 20; i++)
            for (int j = -20; j <= 20; j++)
            {
                float cx = i * C, cz = j * C, r = new Vector2(cx, cz).Length();
                if (r > 860 || i == 0 || j == 0 || Off(cx, cz) < RoadOut + 24) continue;
                var cell = new Rect2(cx - C / 2, cz - C / 2, C, C);
                m.Hex(0x3a3c40);
                Flat(m, cell.Position.X, cell.Position.Y, cell.End.X, cell.End.Y, 0.025f);
                if (Mathf.Abs(cx - Hw) < 30 || park.Intersects(cell.Grow(-2))) continue;
                var blk = cell.Grow(-Inset);
                m.Hex(0x9d9990);
                Flat(m, blk.Position.X, blk.Position.Y, blk.End.X, blk.End.Y, 0.035f);

                float cbd = Mathf.Exp(-(Sq((cx + 60) / 300) + Sq((cz + 470) / 250)));
                float h = 14 + Hash(i, j) * 20 + Mathf.Clamp((r - 220) / 600, 0, 1) * 30 + cbd * (110 + 120 * Hash(j, i + 4));
                if (Off(cx, cz) < RoadOut + 70) h = Mathf.Min(h, 30);
                var look = r > 500 ? Look.Skyline : Look.Tower;
                int style = (int)(Hash(i + 5, j) * 3);
                uint wall = walls[(int)(Hash(i + 11, j) * walls.Length)];
                var c = new Vector3(blk.GetCenter().X, 0, blk.GetCenter().Y);
                if (h > 64)
                {
                    // A podium, the tower set back on it, a crown; a spire and a warning light on the tallest.
                    m.Hex(0xb8b2a6, look, 1);
                    m.Box(new Transform3D(Basis.Identity, c + new Vector3(0, 5, 0)), new Vector3(blk.Size.X, 10, blk.Size.Y), 1 | 2 | 4 | 16 | 32);
                    float w = blk.Size.X * (0.6f + 0.25f * Hash(i, j + 3)), d = blk.Size.Y * (0.6f + 0.25f * Hash(i + 2, j));
                    m.Hex(wall, look, style);
                    m.Box(new Transform3D(Basis.Identity, c + new Vector3(0, h / 2, 0)), new Vector3(w, h, d), 1 | 2 | 4 | 16 | 32);
                    float ch = 5 + Hash(i + 1, j + 1) * 9;
                    m.Box(new Transform3D(Basis.Identity, c + new Vector3(0, h + ch / 2, 0)), new Vector3(w * 0.72f, ch, d * 0.72f), 1 | 2 | 4 | 16 | 32);
                    if (h > 150 && spires++ < 6)
                    {
                        m.Hex(0xb8bcc0);
                        m.Column(c + new Vector3(0, h + ch, 0), 0.8f, 0, 16 + Hash(j, i) * 16, 4);
                    }
                    m.Hex(0xff2a1a, Look.Unlit);
                    m.Box(new Transform3D(Basis.Identity, c + new Vector3(0, h + ch + 0.6f, 0)), new Vector3(1.2f, 1.2f, 1.2f), 63);
                    continue;
                }
                Building(m, c, new Vector3(blk.Size.X - 2, h, blk.Size.Y - 2), wall, style, Hash(i - 2, j) < 0.2f);
                if (Hash(i + 9, j) < 0.35f)
                {
                    // A water tank on legs.
                    var t = c + new Vector3(-blk.Size.X * 0.25f, h, blk.Size.Y * 0.2f);
                    m.Hex(0x6b5640);
                    m.Column(t + new Vector3(0, 1.2f, 0), 1.8f, 1.8f, 3.4f, 6);
                    m.Column(t + new Vector3(0, 4.6f, 0), 2, 0, 1.4f, 6);
                }
                if (Hash(i, j + 13) < 0.12f && r < 460)
                {
                    // A lit billboard on the roof, turned to the ground.
                    var b = new Basis(Vector3.Up, Mathf.Atan2(-c.X, -c.Z));
                    m.Hex(0x2a2c33);
                    m.Box(new Transform3D(b, c + new Vector3(0, h + 2, 0)), new Vector3(0.4f, 4, 0.4f), 63);
                    m.Hex(home, Look.Unlit);
                    m.Box(new Transform3D(b, c + new Vector3(0, h + 6, 0)), new Vector3(13, 5, 0.3f), 63);
                    m.Hex(0xf3eee2, Look.Unlit);
                    m.Box(new Transform3D(b, c + new Vector3(0, h + 5.2f, 0)), new Vector3(13.1f, 1, 0.35f), 63);
                }
            }

        // The avenues: out from the ring road each way, trees and lamps down both sides, traffic.
        foreach (var (ax, az) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
        {
            var dir = new Vector3(ax, 0, az);
            var side = new Vector3(-az, 0, ax);
            float t0 = (ax != 0 ? Kit.BX : Kit.BZ) + RoadOut - 2;
            m.Hex(0x34363a, Look.Road, 2 * Av);
            Strip(m, dir * t0 + new Vector3(0, 0.045f, 0), dir * 880 + new Vector3(0, 0.045f, 0), 2 * Av);
            m.Hex(0xb3aea4);
            foreach (float k in new[] { -1f, 1 })
                Strip(m, dir * t0 + side * k * (Av + 3) + new Vector3(0, 0.03f, 0), dir * 880 + side * k * (Av + 3) + new Vector3(0, 0.03f, 0), 6);
            for (float t = t0 + 8; t < 860; t += 14)
                foreach (float k in new[] { -1f, 1 })
                {
                    if (Mathf.Abs((dir * t).X - Hw) < 14) continue;
                    Tree(m, dir * t + side * k * (Av + 3.5f), 8 + (float)rng.NextDouble() * 2, Leaves[rng.Next(Leaves.Length)]);
                    if (((int)(t / 14) & 1) == 0) Lamp(m, dir * (t + 7) + side * k * (Av + 0.8f));
                }
            for (float t = t0 + 10; t < 860; t += 8)
                foreach (float lane in new[] { -0.75f, -0.25f, 0.25f, 0.75f })
                    if (rng.NextDouble() < 0.28)
                        Car(m, dir * (t + (float)rng.NextDouble() * 4) + side * lane * Av, Mathf.Atan2(ax, az), rng);
        }

        // The park: lawns, crossing paths, a pond, trees; a few people about.
        m.Hex(0x557a3c);
        Flat(m, park.Position.X, park.Position.Y, park.End.X, park.End.Y, 0.04f);
        m.Hex(0xc2b79f);
        Strip(m, new Vector3(park.Position.X, 0.045f, park.Position.Y), new Vector3(park.End.X, 0.045f, park.End.Y), 4);
        Strip(m, new Vector3(park.Position.X, 0.045f, park.End.Y), new Vector3(park.End.X, 0.045f, park.Position.Y), 4);
        var pond = new Vector3(park.GetCenter().X + 60, 0.05f, park.GetCenter().Y - 30);
        m.Hex(0x15405a, Look.Water);
        for (int i = 0; i < 20; i++)
        {
            float a0 = Mathf.Tau * i / 20, a1 = Mathf.Tau * (i + 1) / 20;
            m.Tri(pond, pond + new Vector3(Mathf.Cos(a0) * 44, 0, Mathf.Sin(a0) * 26), pond + new Vector3(Mathf.Cos(a1) * 44, 0, Mathf.Sin(a1) * 26), Vector2.Zero, Vector2.Zero, Vector2.Zero);
        }
        for (int i = 0; i < 90; i++)
        {
            var p = new Vector3(park.Position.X + 4 + (float)rng.NextDouble() * (park.Size.X - 8), 0, park.Position.Y + 4 + (float)rng.NextDouble() * (park.Size.Y - 8));
            if (Sq((p.X - pond.X) / 50) + Sq((p.Z - pond.Z) / 32) < 1) continue;
            Tree(m, p, 7 + (float)rng.NextDouble() * 6, Leaves[rng.Next(Leaves.Length)]);
        }

        // The elevated highway: a deck on piers, traffic both ways.
        const float Deck = 11, Hw2 = 11;
        m.Hex(0x9a968c);
        for (float z = -860; z <= 860; z += 32) m.Box(new Transform3D(Basis.Identity, new Vector3(Hw, Deck / 2, z)), new Vector3(3, Deck, 8), 1 | 2 | 16 | 32);
        m.Box(new Transform3D(Basis.Identity, new Vector3(Hw, Deck + 0.8f, 0)), new Vector3(2 * Hw2, 1.6f, 1740), 63);
        m.Hex(0x34363a, Look.Road, 2 * Hw2 - 2);
        Strip(m, new Vector3(Hw, Deck + 1.62f, -870), new Vector3(Hw, Deck + 1.62f, 870), 2 * Hw2 - 2);
        m.Hex(0xb3aea4);
        foreach (float k in new[] { -1f, 1 }) m.Box(new Transform3D(Basis.Identity, new Vector3(Hw + k * (Hw2 - 0.4f), Deck + 2.2f, 0)), new Vector3(0.6f, 1.2f, 1740), 1 | 2 | 4);
        for (float z = -850; z < 850; z += 7)
            foreach (float lane in new[] { -7f, -2.5f, 2.5f, 7 })
                if (rng.NextDouble() < 0.3) Car(m, new Vector3(Hw + lane, Deck + 1.6f, z + (float)rng.NextDouble() * 3), 0, rng);

        Plaza(m, rng, home);
        FarHills(m, 1050, 110);
    }

    // ---------------------------------------------------------------- the old town

    const float RiverW = 24;
    static float RiverZ(float x) => -236 + 26 * Mathf.Sin(x / 260 + 0.6f);
    static readonly Vector3 Castle = new(-60, 0, -560), Cathedral = new(170, 0, -320);
    static readonly float[] Bridges = { -330, -40, 260 };

    static float OldH(float x, float z)
    {
        float dz = Mathf.Abs(z - RiverZ(x));
        if (dz < RiverW + 14) return 0;
        float hill = 64 * Mathf.Exp(-(Sq((x - Castle.X) / 240) + Sq((z - Castle.Z) / 210)));
        float roll = Kit.Smooth((Mathf.Sqrt(x * x + z * z) - 640) / 320) * (18 + 80 * Noise(x * 0.004f + 2, z * 0.004f + 7));
        return (hill + roll) * Kit.Smooth((dz - RiverW - 14) / 60);
    }

    static bool InTown(float x, float z) =>
        x * x + z * z < 500 * 500 && Off(x, z) > RoadOut + 10 && Mathf.Abs(z - RiverZ(x)) > RiverW + 12;

    /// <summary>An old river town: streets of tall narrow houses under red roofs, a river with
    /// stone bridges just past the main stand, the cathedral on the far bank and a castle on the
    /// hill above the town; olive groves and cypresses on the hills round about.</summary>
    static void OldTown(MeshData m, Random rng, uint home)
    {
        const uint Stone = 0x8b8175;
        Core(m, 0x9b8f80, Stone, 64);
        uint[] olives = { 0x5d6b3c, 0x67733f, 0x55633a };
        Terrain(m, 40, 1680, OldH, c =>
        {
            if (Off(c.X0, c.Z0) < RoadOut + 6 || Off(c.X0 + 40, c.Z0) < RoadOut + 6 || Off(c.X0, c.Z0 + 40) < RoadOut + 6 || Off(c.X0 + 40, c.Z0 + 40) < RoadOut + 6) return 0;
            var p = c.Mid;
            if (InTown(p.X, p.Y) || Mathf.Abs(p.Y - RiverZ(p.X)) < RiverW + 20) return Stone;
            if (new Vector2(p.X - Castle.X, p.Y - Castle.Z).Length() < 70) return 0x7d7466;
            float nz = Noise(c.X0 * 0.02f, c.Z0 * 0.02f);
            return c.Avg > 70 ? 0x6b7348u : nz > 0.55f ? 0x6d8442u : nz > 0.3f ? 0x5f7a3au : 0x75844au;
        }, 0.05f, c =>
        {
            var p = c.Mid;
            if (InTown(p.X, p.Y) || c.Avg < 3 || new Vector2(p.X - Castle.X, p.Y - Castle.Z).Length() < 80 || p.Length() > 950) return;
            // Olive groves, and a cypress now and then.
            if (Noise(c.X0 * 0.01f + 4, c.Z0 * 0.01f) > 0.5f)
                for (int k = 0; k < 3; k++)
                {
                    var t = c.At((float)rng.NextDouble(), (float)rng.NextDouble());
                    m.Hex(olives[rng.Next(olives.Length)]);
                    m.Blob(t + new Vector3(0, 2.4f, 0), new Vector3(2.8f, 2.4f, 2.8f), 4, 2);
                }
            else if (rng.NextDouble() < 0.5)
                Pine(m, c.At((float)rng.NextDouble(), (float)rng.NextDouble()), 13, 0, 0x2f4a2c);
        });

        // The river, its embankments and promenades.
        for (float x = -1680; x < 1680; x += 30)
        {
            float z0 = RiverZ(x), z1 = RiverZ(x + 30);
            m.Hex(0x1f4a5e, Look.Water);
            m.QuadUV(new(x, 0.09f, z0 - RiverW), new(x + 30, 0.09f, z1 - RiverW), new(x + 30, 0.09f, z1 + RiverW), new(x, 0.09f, z0 + RiverW), new(0, 0), new(1, 0), new(1, 1), new(0, 1));
            m.Hex(0xbdb3a0);
            foreach (float k in new[] { -1f, 1 })
                m.Beam(new Vector3(x, 0.5f, z0 + k * (RiverW + 0.4f)), new Vector3(x + 30, 0.5f, z1 + k * (RiverW + 0.4f)), 0.7f, 1);
            if (Mathf.Abs(x) < 700)
                foreach (float k in new[] { -1f, 1 })
                {
                    bool atBridge = false;
                    foreach (float bx in Bridges) atBridge |= Mathf.Abs(x - bx) < 20;
                    if (atBridge) continue;
                    Tree(m, new Vector3(x + 15, 0, RiverZ(x + 15) + k * (RiverW + 7)), 8 + (float)rng.NextDouble() * 2, Leaves[rng.Next(Leaves.Length)]);
                    Lamp(m, new Vector3(x, 0, z0 + k * (RiverW + 2)));
                }
        }
        // The bridges: three low stone arches each, ramps down to the quays.
        foreach (float bx in Bridges)
        {
            float rz = RiverZ(bx);
            var prof = new List<Vector2> { new(-40, 0.3f), new(-27, 3.4f), new(27, 3.4f), new(40, 0.3f), new(40, 0), new(RiverW, 0) };
            foreach (var (a, b) in new[] { (RiverW, 9f), (5f, -5f), (-9f, -RiverW) })
            {
                for (int i = 0; i <= 8; i++)
                {
                    float t = i / 8f;
                    prof.Add(new Vector2(Mathf.Lerp(a, b, t), 2.3f * Mathf.Sin(t * Mathf.Pi) + (i == 0 || i == 8 ? 0 : 0.3f)));
                }
                prof.Add(new Vector2(b, 0));
            }
            prof.Add(new Vector2(-40, 0));
            m.Hex(0xc4b9a4);
            m.Prism(new Transform3D(new Basis(Vector3.Up, -Mathf.Pi / 2), new Vector3(bx, 0, rz)), prof.ToArray(), 11);
            m.Hex(0xb0a690);
            foreach (float k in new[] { -1f, 1 }) m.Box(new Transform3D(Basis.Identity, new Vector3(bx + k * 5.2f, 3.9f, rz)), new Vector3(0.6f, 1, 54), 63);
            Lamp(m, new Vector3(bx + 5, 3.4f, rz - 12));
            Lamp(m, new Vector3(bx - 5, 3.4f, rz + 12));
        }

        // The town: blocks of houses round courtyards, a row along each side under its own roof.
        uint[] roofs = { 0xb5522f, 0xa84a2c, 0xc0653a, 0x9a4228, 0xb85a36 };
        const float B = 36, S = 30, D = 9;
        int churches = 0;
        for (int i = -16; i <= 16; i++)
            for (int j = -16; j <= 16; j++)
            {
                float cx = i * B + 8, cz = j * B;
                if (!InTown(cx, cz) || Off(cx, cz) < RoadOut + 22) continue;
                if (new Vector2(cx - Cathedral.X, cz - Cathedral.Z).Length() < 64 || new Vector2(cx - Castle.X, cz - Castle.Z).Length() < 80) continue;
                bool near = false;
                foreach (float bx in Bridges) near |= Mathf.Abs(cx - bx) < 26 && Mathf.Abs(cz - RiverZ(bx)) < 80;
                if (near) continue;
                float y = OldH(cx, cz);
                var c = new Vector3(cx, y - 3, cz);
                if (Hash(i + 3, j - 7) < 0.03f && churches++ < 5)
                {
                    Church(m, c, Hash(i, j) < 0.5f ? 0 : Mathf.Pi / 2, 0xd8cfbe, 0x9a4228);
                    continue;
                }
                if (Hash(i - 5, j + 2) < 0.12f)
                {
                    // A square with a fountain.
                    m.Hex(0xc9bea8);
                    m.Column(c + new Vector3(0, 3, 0), 3, 3, 0.8f, 8);
                    m.Hex(0x1f4a5e, Look.Water);
                    m.Column(c + new Vector3(0, 3.2f, 0), 2.6f, 2.6f, 0.65f, 8);
                    for (int k = 0; k < 4; k++) Tree(m, new Vector3(cx + (k % 2 * 2 - 1) * 10, y, cz + (k / 2 * 2 - 1) * 10), 7, Leaves[rng.Next(Leaves.Length)]);
                    continue;
                }
                for (int side = 0; side < 4; side++)
                {
                    float h = 9 + Hash(i * 3 + side, j) * 8 + 3;
                    bool alongX = side < 2;
                    float off = S / 2 - D / 2;
                    var p = c + (alongX ? new Vector3(0, 0, side == 0 ? -off : off) : new Vector3(side == 2 ? -off : off, 0, 0));
                    m.Hex(0xffffff, Look.House, 1);
                    Gable(m, p, alongX ? 0 : Mathf.Pi / 2, new Vector3(alongX ? S : S - 2 * D, h, D), 3.2f, roofs[(int)(Hash(i + side, j * 2) * roofs.Length)], 0.4f);
                }
            }

        // The cathedral on the far bank: nave, two west towers with spires, a dome on a drum.
        {
            var c = Cathedral + new Vector3(0, OldH(Cathedral.X, Cathedral.Z) - 2, 0);
            m.Hex(0xddd3c0);
            Gable(m, c, 0, new Vector3(64, 26, 24), 10, 0x5a5f66, 0.6f);
            m.Hex(0xddd3c0);
            Gable(m, c + new Vector3(8, 0, 0), Mathf.Pi / 2, new Vector3(52, 24, 18), 9, 0x5a5f66, 0.6f);
            foreach (float k in new[] { -1f, 1 })
            {
                var t = c + new Vector3(-36, 0, k * 9);
                m.Hex(0xddd3c0);
                m.Box(new Transform3D(Basis.Identity, t + new Vector3(0, 24, 0)), new Vector3(11, 48, 11), 1 | 2 | 4 | 16 | 32);
                m.Hex(0x5a5f66);
                m.Column(t + new Vector3(0, 48, 0), 6.5f, 0, 24, 8);
            }
            m.Hex(0xddd3c0);
            m.Column(c + new Vector3(8, 30, 0), 10, 10, 12, 12);
            m.Hex(0x6f9a87);
            m.Blob(c + new Vector3(8, 42, 0), new Vector3(10.5f, 13, 10.5f), 12, 3, half: true);
            m.Hex(0xddd3c0);
            m.Column(c + new Vector3(8, 54, 0), 2.4f, 2.4f, 5, 8);
            m.Hex(0x6f9a87);
            m.Column(c + new Vector3(8, 59, 0), 2.8f, 0, 6, 8);
            // Its square, down to the river.
            m.Hex(0xc9bea8);
            Flat(m, c.X - 70, c.Z - 30, c.X - 40, c.Z + 30, c.Y + 2.06f);
        }

        // The castle on the hill: a ring of walls with round towers, the keep, the club's flag.
        {
            float top = OldH(Castle.X, Castle.Z);
            var c = Castle + new Vector3(0, top, 0);
            const int N = 9;
            for (int i = 0; i < N; i++)
            {
                float a0 = Mathf.Tau * i / N, a1 = Mathf.Tau * (i + 1) / N;
                var p0 = c + new Vector3(Mathf.Cos(a0) * 48, 0, Mathf.Sin(a0) * 40);
                var p1 = c + new Vector3(Mathf.Cos(a1) * 48, 0, Mathf.Sin(a1) * 40);
                p0.Y = OldH(p0.X, p0.Z) - 4;
                p1.Y = OldH(p1.X, p1.Z) - 4;
                float y0 = Mathf.Max(p0.Y, p1.Y) + 14;
                m.Hex(0xa39886);
                var mid = (p0 + p1) / 2;
                var along = p1 - p0;
                along.Y = 0;
                var b = new Basis(Vector3.Up, Mathf.Atan2(along.X, along.Z));
                float lo = Mathf.Min(p0.Y, p1.Y);
                m.Box(new Transform3D(b, new Vector3(mid.X, (lo + y0) / 2, mid.Z)), new Vector3(3, y0 - lo, along.Length()), 63);
                for (int k = 0; k < 5; k++)
                    m.Box(new Transform3D(b, new Vector3(mid.X, y0 + 0.8f, mid.Z) + along.Normalized() * (k - 2) * along.Length() / 5.5f), new Vector3(3.2f, 1.6f, 1.6f), 63);
                m.Column(new Vector3(p0.X, p0.Y, p0.Z), 5, 5, y0 - p0.Y + 4, 8);
                m.Hex(0x7a4a36);
                m.Column(new Vector3(p0.X, y0 + 4, p0.Z), 5.6f, 0, 7, 8);
            }
            m.Hex(0x9a8f7c);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(0, 16, 0)), new Vector3(20, 36, 20), 63);
            for (int k = 0; k < 12; k++)
            {
                float u = (k % 3 - 1) * 7.5f;
                var e = (k / 3) switch { 0 => new Vector3(u, 0, -10), 1 => new Vector3(u, 0, 10), 2 => new Vector3(-10, 0, u), _ => new Vector3(10, 0, u) };
                m.Box(new Transform3D(Basis.Identity, c + e + new Vector3(0, 34.9f, 0)), new Vector3(2.4f, 1.8f, 2.4f), 63);
            }
            m.Hex(0x3a3d42);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(0, 42, 0)), new Vector3(0.3f, 12, 0.3f), 63);
            m.Hex(home);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(2.6f, 46.5f, 0)), new Vector3(5, 3, 0.15f), 63);
        }

        Plaza(m, rng, home);
    }

    // ---------------------------------------------------------------- the docklands

    const float QuayZ = -230, FarBank = -700, Rail = 330;

    /// <summary>Among the docks: rows of brick terraces right up to the ground, old mills with
    /// their chimneys, a gasometer, the railway; the quay just past the main stand with its
    /// cranes, containers and a ship, a suspension bridge upriver and the power station's
    /// cooling towers on the far bank.</summary>
    static void Docklands(MeshData m, Random rng, uint home)
    {
        Core(m, 0x8f8b84, 0x55534f, 470);
        // The river and its quays.
        m.Hex(0x1d4254, Look.Water);
        Flat(m, -1700, FarBank, 1700, QuayZ, 0.06f);
        m.Hex(0x9a968c);
        Flat(m, -1700, QuayZ, 1700, QuayZ + 34, 0.05f);
        m.Hex(0x5a5e52);
        Flat(m, -1700, -1700, 1700, FarBank, 0.05f);
        m.Hex(0x77736b);
        m.Box(new Transform3D(Basis.Identity, new Vector3(0, 0.5f, QuayZ)), new Vector3(3400, 1, 1.2f), 1 | 2 | 4 | 32);
        m.Box(new Transform3D(Basis.Identity, new Vector3(0, 0.5f, FarBank)), new Vector3(3400, 1, 1.2f), 1 | 2 | 4 | 16);

        // Containers stacked on the quay, gantry cranes along its edge, a ship alongside.
        uint[] boxes = { 0xc0392b, 0x2f7fb8, 0xe0a030, 0x2e8b57, 0xd35400, 0xbdc3c7, 0x7a1f3a, 0x1f5c8a };
        for (float x = 110; x < 560; x += 13)
            for (int row = 0; row < 5; row++)
            {
                int n = 1 + rng.Next(4);
                m.Hex(boxes[rng.Next(boxes.Length)]);
                m.Box(new Transform3D(Basis.Identity, new Vector3(x, n * 1.3f, QuayZ + 12 + row * 2.9f)), new Vector3(12.2f, n * 2.6f, 2.5f), 1 | 2 | 4 | 16 | 32);
            }
        foreach (float x in new[] { 170f, 280f, 390f, 500f })
        {
            m.Hex(x == 280 ? 0xe9e6deu : 0xb8322au);
            foreach (float dx in new[] { -8f, 8 })
            {
                m.Beam(new Vector3(x + dx, 0, QuayZ + 4), new Vector3(x + dx, 40, QuayZ + 4), 1.6f, 1.6f);
                m.Beam(new Vector3(x + dx, 0, QuayZ + 28), new Vector3(x + dx, 40, QuayZ + 28), 1.6f, 1.6f);
                m.Beam(new Vector3(x + dx, 40, QuayZ - 46), new Vector3(x + dx, 40, QuayZ + 44), 1.4f, 2.4f);
                m.Beam(new Vector3(x + dx, 58, QuayZ + 10), new Vector3(x + dx, 41, QuayZ - 40), 0.4f, 0.4f);
            }
            m.Beam(new Vector3(x - 9, 40, QuayZ + 4), new Vector3(x + 9, 40, QuayZ + 4), 1.4f, 1.4f);
            m.Beam(new Vector3(x - 9, 40, QuayZ + 28), new Vector3(x + 9, 40, QuayZ + 28), 1.4f, 1.4f);
            m.Beam(new Vector3(x - 9, 20, QuayZ + 28), new Vector3(x + 9, 20, QuayZ + 28), 1, 1);
            m.Box(new Transform3D(Basis.Identity, new Vector3(x, 58, QuayZ + 10)), new Vector3(16, 2, 2), 63);
            m.Hex(0xe9e6de);
            m.Box(new Transform3D(Basis.Identity, new Vector3(x, 44, QuayZ + 34)), new Vector3(14, 6, 10), 63);
        }
        {
            const float L = 190, W = 30;
            var c = new Vector3(330, 0, QuayZ - 22);
            var b = new Basis(Vector3.Up, -Mathf.Pi / 2);
            m.Hex(0x8e2f25);
            m.Prism(new Transform3D(b, c), new Vector2[] { new(-L / 2, -1), new(L / 2 - 14, -1), new(L / 2, 6), new(-L / 2, 6) }, W);
            m.Hex(0x1f2c5c);
            m.Prism(new Transform3D(b, c), new Vector2[] { new(-L / 2, 5), new(L / 2, 5), new(L / 2 + 3, 13), new(-L / 2, 13) }, W);
            for (float x = -60; x < 86; x += 13)
                for (int row = -4; row <= 4; row++)
                {
                    int n = 1 + rng.Next(4);
                    m.Hex(boxes[rng.Next(boxes.Length)]);
                    m.Box(new Transform3D(Basis.Identity, c + new Vector3(x, 13 + n * 1.3f, row * 2.9f)), new Vector3(12.2f, n * 2.6f, 2.5f), 1 | 2 | 4 | 16 | 32);
                }
            m.Hex(0xf1eee6);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(-80, 26, 0)), new Vector3(14, 26, W - 4), 63);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(-76, 39, 0)), new Vector3(6, 1, W + 4), 63);
            m.Hex(0x2a2c33);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(-88, 34, 0)), new Vector3(6, 14, 6), 63);
        }

        // Old warehouses along the quay.
        for (int i = 0; i < 6; i++)
        {
            var c = new Vector3(-580 + i * 94, 0, QuayZ + 52);
            if (Off(c.X, c.Z) < RoadOut + 30) continue;
            m.Hex(0x8a4a35, Look.Tower, 2);
            Gable(m, c, 0, new Vector3(80, 18 + (i % 3) * 3, 26), 6, 0x4a4d52, 0.4f);
        }

        // The suspension bridge upriver.
        {
            const float X = -760, Top = 104, DeckY = 24;
            float z0 = QuayZ - 70, z1 = FarBank + 70;
            m.Hex(0xb24a2a);
            foreach (float z in new[] { z0, z1 })
            {
                foreach (float k in new[] { -1f, 1 }) m.Box(new Transform3D(Basis.Identity, new Vector3(X + k * 14, Top / 2, z)), new Vector3(4, Top, 5), 63);
                foreach (float y in new[] { DeckY - 2, 60f, Top - 4 }) m.Box(new Transform3D(Basis.Identity, new Vector3(X, y, z)), new Vector3(30, 3, 4), 63);
            }
            float zA = QuayZ + 80, zB = FarBank - 80;
            m.Hex(0x7d7a74);
            m.Box(new Transform3D(Basis.Identity, new Vector3(X, DeckY, (zA + zB) / 2)), new Vector3(26, 3, zA - zB), 63);
            for (float z = zA - 20; z > zB; z -= 40)
                m.Box(new Transform3D(Basis.Identity, new Vector3(X, DeckY / 2, z)), new Vector3(6, DeckY, 6), 1 | 2 | 16 | 32);
            // The main cables: a sag between the towers, straight down to the deck's ends beyond.
            float CableY(float z) => z > z0 ? Mathf.Lerp(Top, DeckY + 2, (z - z0) / (zA - z0))
                : z < z1 ? Mathf.Lerp(Top, DeckY + 2, (z1 - z) / (z1 - zB))
                : DeckY + 4 + (Top - DeckY - 4) * Sq(2 * (z - z0) / (z1 - z0) - 1);
            var zs = new List<float> { zA };
            for (int i = 1; i <= 4; i++) zs.Add(Mathf.Lerp(zA, z0, i / 4f));
            for (int i = 1; i <= 16; i++) zs.Add(Mathf.Lerp(z0, z1, i / 16f));
            for (int i = 1; i <= 4; i++) zs.Add(Mathf.Lerp(z1, zB, i / 4f));
            m.Hex(0xb24a2a);
            foreach (float k in new[] { -1f, 1 })
                for (int i = 0; i + 1 < zs.Count; i++)
                {
                    var p = new Vector3(X + k * 14, CableY(zs[i]), zs[i]);
                    m.Beam(p, new Vector3(X + k * 14, CableY(zs[i + 1]), zs[i + 1]), 0.9f, 0.9f);
                    if (zs[i] < z0 && zs[i] > z1) m.Beam(p, new Vector3(X + k * 14, DeckY, zs[i]), 0.25f, 0.25f);
                }
        }

        // The power station on the far bank: cooling towers, the turbine hall, a tall chimney.
        {
            var c = new Vector3(-80, 0, -860);
            m.Hex(0x7a4434, Look.Tower, 2);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(0, 19, 0)), new Vector3(150, 38, 50), 1 | 2 | 4 | 16 | 32);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(30, 27, -45)), new Vector3(70, 54, 40), 1 | 2 | 4 | 16 | 32);
            m.Hex(0xb9b4aa);
            var ch = c + new Vector3(110, 0, -30);
            m.Column(ch, 7.5f, 4.5f, 190, 10);
            m.Hex(0xb8322a);
            m.Column(ch + new Vector3(0, 168, 0), 4.85f, 4.65f, 8, 10);
            m.Column(ch + new Vector3(0, 182, 0), 4.65f, 4.55f, 8, 10);
            m.Hex(0xff2a1a, Look.Unlit);
            m.Box(new Transform3D(Basis.Identity, ch + new Vector3(0, 191, 0)), new Vector3(1.4f, 1.4f, 1.4f), 63);
            m.Hex(0xc9c3b8);
            foreach (var t in new[] { new Vector3(-250, 0, -820), new Vector3(-350, 0, -880), new Vector3(-230, 0, -950) })
            {
                m.Column(t, 36, 23, 62, 16);
                m.Column(t + new Vector3(0, 62, 0), 23, 27, 30, 16);
            }
            // The far bank's sheds.
            for (int i = 0; i < 14; i++)
            {
                var p = new Vector3(-1100 + i * 160 + (float)rng.NextDouble() * 50, 0, FarBank - 50 - (float)rng.NextDouble() * 160);
                if (Mathf.Abs(p.X + 80) < 200) continue;
                m.Hex(rng.NextDouble() < 0.5 ? 0x8a8f94u : 0x6e7c88u);
                Gable(m, p, 0, new Vector3(60 + (float)rng.NextDouble() * 40, 14, 34), 4, 0x55595e, 0.3f);
            }
        }

        // The railway along the back, a train on it.
        m.Hex(0x6b6259);
        Flat(m, -1700, Rail - 5, 1700, Rail + 5, 0.045f);
        m.Hex(0x2b2a29);
        foreach (float k in new[] { -2.2f, -0.8f, 0.8f, 2.2f }) Flat(m, -1700, Rail + k - 0.1f, 1700, Rail + k + 0.1f, 0.05f);
        for (int i = 0; i < 9; i++)
        {
            m.Hex(i == 0 ? 0x2f5fb8u : 0xd8d4c8u);
            m.Box(new Transform3D(Basis.Identity, new Vector3(-180 + i * 21, 2.6f, Rail - 1.5f)), new Vector3(20, 3.6f, 3), 63);
            m.Hex(0x2f5fb8);
            m.Box(new Transform3D(Basis.Identity, new Vector3(-180 + i * 21, 2.1f, Rail - 1.5f)), new Vector3(20.1f, 0.6f, 3.1f), 1 | 2 | 16 | 32);
        }

        // Mills: sawtooth roofs and their chimneys; the gasometer.
        var mills = new[] { new Vector2(-470, -120), new Vector2(-470, 60), new Vector2(-560, 220), new Vector2(480, -100) };
        foreach (var mp in mills)
        {
            var c = new Vector3(mp.X, 0, mp.Y);
            m.Hex(0x8a4a35, Look.Tower, 2);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(0, 5, 0)), new Vector3(80, 10, 60), 1 | 2 | 16 | 32);
            for (int k = 0; k < 6; k++)
            {
                m.Hex(0x55595e);
                m.Prism(new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 2), c + new Vector3(0, 10, -30 + 5 + k * 10)), new Vector2[] { new(-5, 0), new(5, 0), new(5, 5) }, 80);
            }
            m.Hex(0x7a4030);
            m.Column(c + new Vector3(46, 0, 20), 3, 2, 52, 8);
            m.Hex(0x2b2a29);
            m.Column(c + new Vector3(46, 50, 20), 2.1f, 2.1f, 2.5f, 8);
        }
        {
            var g = new Vector3(400, 0, 170);
            m.Hex(0x6b7c72);
            m.Column(g, 24, 24, 24, 16);
            m.Hex(0x6e3a30);
            for (int k = 0; k < 12; k++)
            {
                float a = Mathf.Tau * k / 12;
                var p = g + new Vector3(Mathf.Cos(a) * 26, 0, Mathf.Sin(a) * 26);
                m.Beam(p, p + new Vector3(0, 40, 0), 1, 1);
            }
            foreach (float y in new[] { 13f, 26, 39 })
                for (int k = 0; k < 12; k++)
                {
                    float a0 = Mathf.Tau * k / 12, a1 = Mathf.Tau * (k + 1) / 12;
                    m.Beam(g + new Vector3(Mathf.Cos(a0) * 26, y, Mathf.Sin(a0) * 26), g + new Vector3(Mathf.Cos(a1) * 26, y, Mathf.Sin(a1) * 26), 0.8f, 0.8f);
                }
        }

        // Terraces: long rows of brick houses back to back, slate roofs, cars along the kerb.
        bool Clear(Vector2 p, float pad)
        {
            foreach (var mp in mills) if (Mathf.Abs(p.X - mp.X) < 44 + pad && Mathf.Abs(p.Y - mp.Y) < 34 + pad) return false;
            return new Vector2(p.X - 400, p.Y - 170).Length() > 34 + pad && Mathf.Abs(p.Y - Rail) > 12 + pad;
        }
        for (float z = QuayZ + 88; z < 600; z += 17)
            for (float x0 = -620; x0 < 620; x0 += 70)
            {
                var a = new Vector2(x0, z);
                var b = new Vector2(x0 + 60, z);
                var mid = (a + b) / 2;
                if (mid.Length() > 600 || Off(a.X, a.Y) < RoadOut + 14 || Off(b.X, b.Y) < RoadOut + 14 || Off(mid.X, mid.Y) < RoadOut + 14) continue;
                if (!Clear(a, 0) || !Clear(b, 0) || !Clear(mid, 0)) continue;
                m.Hex(0xffffff, Look.House);
                Gable(m, new Vector3(mid.X, 0, mid.Y), 0, new Vector3(60, 7.5f, 8), 2.6f, 0x3d4048, 0.3f);
                m.Hex(0x7a4030);
                for (int k = 0; k < 3; k++) m.Box(new Transform3D(Basis.Identity, new Vector3(x0 + 10 + k * 20, 10.2f, z)), new Vector3(1.2f, 1.6f, 0.8f), 63);
                if (((int)(z / 17) & 1) == 0)
                    for (float x = x0 + 3; x < x0 + 58; x += 5.5f)
                        if (rng.NextDouble() < 0.4) Car(m, new Vector3(x, 0, z + 6.5f), Mathf.Pi / 2, rng);
            }
        Church(m, new Vector3(-250, 0, 250), 0, 0x9a8f7c, 0x4a4d52, 1.2f);

        Plaza(m, rng, home);
        Terrain(m, 80, 1680, (x, z) => z < FarBank - 120 ? Kit.Smooth((-z - 1000) / 320) * (40 + 100 * Noise(x * 0.004f + 3, z * 0.004f + 1)) : 0,
            c => c.Avg < 0.6f ? 0u : Noise(c.X0 * 0.02f, c.Z0 * 0.02f) > 0.5f ? 0x4f5f40u : 0x5a6a48u, -0.2f);
    }

    // ---------------------------------------------------------------- the countryside

    static float CountryH(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float roll = Kit.Smooth((r - 300) / 450) * (5 + 34 * Noise(x * 0.0045f + 1, z * 0.0045f + 4));
        float ridge = Kit.Smooth((-z - 560) / 260) * (40 + 50 * Noise(x * 0.003f + 5, 0.7f));
        return roll + ridge;
    }

    static readonly Vector2 Village = new(0, -420);
    static bool InVillage(float x, float z) => Sq((x - Village.X) / 130) + Sq((z - Village.Y) / 90) < 1;

    /// <summary>Out in the country: a patchwork of fields and hedgerows, woods, farms with their
    /// barns and silos, cattle and sheep, a village round its church just past the main stand,
    /// and a line of wind turbines on the ridge beyond.</summary>
    static void Countryside(MeshData m, Random rng, uint home)
    {
        const float F = 120, G = 80;
        uint[] crops = { 0xc8b45e, 0xb9b067, 0x6f9440, 0x5f8c3c, 0x5f8c3c, 0x4c7232, 0x7b5b3e, 0xd9c63a, 0x86a24a, 0x5f8c3c };
        uint Crop(float x, float z) => crops[(int)(Hash(Mathf.Floor(x / F) + 0.5f, Mathf.Floor(z / G) + 0.25f) * crops.Length)];
        bool Wood(float x, float z) => Hash(Mathf.Floor(x / F) + 7.5f, Mathf.Floor(z / G) + 3.25f) < 0.1f;
        Core(m, 0xa49a84, 0x5d8a3c, 60);
        Terrain(m, 40, 1680, CountryH, c =>
        {
            if (Off(c.X0, c.Z0) < RoadOut + 6 || Off(c.X0 + 40, c.Z0) < RoadOut + 6 || Off(c.X0, c.Z0 + 40) < RoadOut + 6 || Off(c.X0 + 40, c.Z0 + 40) < RoadOut + 6) return 0;
            var p = c.Mid;
            if (InVillage(p.X, p.Y) || Off(p.X, p.Y) < RoadOut + 60) return 0x5d8a3c;
            if (c.Avg > 60 && c.Slope > 0.5f) return 0x6a7a4a;
            return Wood(p.X, p.Y) ? 0x3f6332u : Crop(p.X, p.Y);
        }, 0.05f, c =>
        {
            var p = c.Mid;
            if (InVillage(p.X, p.Y) || Off(p.X, p.Y) < RoadOut + 60 || p.Length() > 1000) return;
            uint crop = Crop(p.X, p.Y);
            if (Wood(p.X, p.Y))
                for (int k = 0; k < 3; k++) Tree(m, c.At((float)rng.NextDouble(), (float)rng.NextDouble()) - new Vector3(0, 0.5f, 0), 11 + (float)rng.NextDouble() * 5, Leaves[rng.Next(Leaves.Length)]);
            else if ((crop == 0xc8b45e || crop == 0xb9b067) && rng.NextDouble() < 0.3)
                for (int k = 0; k < 5; k++)
                {
                    // Round bales.
                    var b = c.At(0.15f + 0.7f * (float)rng.NextDouble(), 0.15f + 0.7f * (float)rng.NextDouble()) + new Vector3(0, 0.8f, 0);
                    m.Hex(0xd9c27a);
                    m.Cylinder(b - new Vector3(0.7f, 0, 0), b + new Vector3(0.7f, 0, 0), 0.85f, 6);
                }
            else if (crop == 0x5f8c3c && p.Length() < 900 && rng.NextDouble() < 0.5)
            {
                bool cows = rng.NextDouble() < 0.5;
                for (int k = 0; k < 6; k++)
                {
                    var a = c.At((float)rng.NextDouble(), (float)rng.NextDouble());
                    float yaw = (float)rng.NextDouble() * Mathf.Tau;
                    m.Hex(cows ? (rng.NextDouble() < 0.5 ? 0x2b2a29u : 0x8a5a34u) : 0xe9e6dcu);
                    m.Box(new Transform3D(new Basis(Vector3.Up, yaw), a + new Vector3(0, cows ? 1 : 0.6f, 0)), cows ? new Vector3(0.9f, 1.1f, 2.2f) : new Vector3(0.7f, 0.7f, 1.2f), 1 | 2 | 4 | 16 | 32);
                }
            }
        });

        // Hedgerows along the field edges, a tree in them now and then.
        void Hedge(Vector2 a, Vector2 b)
        {
            var mid = (a + b) / 2;
            if (mid.Length() > 820 || Off(mid.X, mid.Y) < RoadOut + 64 || InVillage(mid.X, mid.Y) || rng.NextDouble() < 0.15) return;
            var pa = new Vector3(a.X, CountryH(a.X, a.Y) + 0.9f, a.Y);
            var pb = new Vector3(b.X, CountryH(b.X, b.Y) + 0.9f, b.Y);
            m.Hex(0x3d5e30);
            m.Box(new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(pb.X - pa.X, pb.Z - pa.Z)), (pa + pb) / 2), new Vector3(1.6f, 2, (pb - pa).Length()), 1 | 2 | 4);
            if (rng.NextDouble() < 0.2) Tree(m, new Vector3(mid.X, CountryH(mid.X, mid.Y) - 0.3f, mid.Y), 9 + (float)rng.NextDouble() * 4, Leaves[rng.Next(Leaves.Length)]);
        }
        for (float x = -960; x <= 960; x += F)
            for (float z = -960; z < 960; z += 40) Hedge(new Vector2(x, z), new Vector2(x, z + 40));
        for (float z = -960; z <= 960; z += G)
            for (float x = -960; x < 960; x += 40) Hedge(new Vector2(x, z), new Vector2(x + 40, z));

        // Lanes: to the village and on past it, and west and east across the fields.
        void Lane(Vector2 a, Vector2 b, bool cars)
        {
            int n = Mathf.CeilToInt((b - a).Length() / 20);
            for (int i = 0; i < n; i++)
            {
                var p0 = a.Lerp(b, i / (float)n);
                var p1 = a.Lerp(b, (i + 1) / (float)n);
                m.Hex(0x55534f);
                Strip(m, new Vector3(p0.X, CountryH(p0.X, p0.Y) + 0.3f, p0.Y), new Vector3(p1.X, CountryH(p1.X, p1.Y) + 0.3f, p1.Y), 6);
                if (cars && rng.NextDouble() < 0.12)
                {
                    var d = p1 - p0;
                    Car(m, new Vector3(p0.X, CountryH(p0.X, p0.Y) + 0.3f, p0.Y), Mathf.Atan2(d.X, d.Y), rng);
                }
            }
        }
        Lane(new Vector2(0, -(Kit.BZ + RoadOut - 2)), new Vector2(0, -1500), true);
        Lane(new Vector2(-(Kit.BX + RoadOut - 2), 10), new Vector2(-1500, 10), true);
        Lane(new Vector2(Kit.BX + RoadOut - 2, 10), new Vector2(1500, 10), true);
        Lane(new Vector2(-130, -420), new Vector2(130, -420), false);

        // Cars parked on the grass.
        foreach (var r in CarParks)
            for (float z = r.Position.Y; z + 16 <= r.End.Y + 0.1f; z += 16)
                foreach (float row in new[] { 2.5f, 13.5f })
                    for (float x = r.Position.X + 1.3f; x < r.End.X - 1; x += 2.6f)
                        if (rng.NextDouble() < 0.5) Car(m, new Vector3(x, 0, z + row), 0, rng);

        // The village: cottages along the lanes, the church, the green.
        for (float z = -330; z > -510; z -= 14)
            foreach (float k in new[] { -1f, 1 })
            {
                if (Mathf.Abs(z + 420) < 10 || rng.NextDouble() < 0.15) continue;
                var p = new Vector3(k * 13, CountryH(k * 13, z) - 1, z);
                m.Hex(0xffffff, Look.House);
                Gable(m, p, Mathf.Pi / 2, new Vector3(10, 7, 7), 3.6f, rng.NextDouble() < 0.4 ? 0x8a6a42u : 0x5a5f66u, 0.5f);
            }
        for (float x = -120; x < 125; x += 14)
            foreach (float k in new[] { -1f, 1 })
            {
                if (Mathf.Abs(x) < 24 || (k < 0 && x > 20 && x < 80) || rng.NextDouble() < 0.2) continue;
                var p = new Vector3(x, CountryH(x, -420 + k * 13) - 1, -420 + k * 13);
                m.Hex(0xffffff, Look.House);
                Gable(m, p, 0, new Vector3(10, 7, 7), 3.6f, rng.NextDouble() < 0.4 ? 0x8a6a42u : 0x5a5f66u, 0.5f);
            }
        Church(m, new Vector3(50, CountryH(50, -446) - 1, -446), 0, 0xb9b2a2, 0x5a5f66, 1.1f);
        for (int k = 0; k < 6; k++) Tree(m, new Vector3(25 + k * 12, CountryH(25 + k * 12, -470), -470), 9, Leaves[rng.Next(Leaves.Length)]);

        // Farms: the house, a big barn, silos, a tree or two.
        var farms = new[] { new Vector2(420, 260), new Vector2(-460, 330), new Vector2(520, -260), new Vector2(-560, -140), new Vector2(260, 560), new Vector2(-300, 640), new Vector2(720, 80), new Vector2(-760, -380) };
        foreach (var f in farms)
        {
            float y = CountryH(f.X, f.Y);
            var c = new Vector3(f.X, y - 1, f.Y);
            m.Hex(0x9c927d);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(0, 1.1f, 0)), new Vector3(46, 0.2f, 36), 4);
            m.Hex(0xe8e2d4);
            Gable(m, c + new Vector3(-12, 0, -8), 0, new Vector3(12, 8, 8), 4, 0x8e4a32);
            m.Hex(0x8e2f25);
            Gable(m, c + new Vector3(8, 0, 4), Mathf.Pi / 2, new Vector3(24, 10, 14), 6, 0x6b6f72);
            m.Hex(0xb8bcc0);
            foreach (float k in new[] { 0f, 7 })
            {
                m.Column(c + new Vector3(22, 0, -10 + k), 3, 3, 16, 8);
                m.Blob(c + new Vector3(22, 16, -10 + k), new Vector3(3, 2.4f, 3), 8, 2, half: true);
            }
            for (int k = 0; k < 3; k++) Tree(m, c + new Vector3(-24 + k * 6, 1, 14), 10, Leaves[rng.Next(Leaves.Length)]);
        }

        // Wind turbines along the ridge, facing the ground.
        for (int i = 0; i < 8; i++)
        {
            float x = -560 + i * 160 + (float)(rng.NextDouble() - 0.5) * 50, z = -880 - (float)rng.NextDouble() * 70;
            Turbine(m, new Vector3(x, CountryH(x, z), z), 78, 0.15f * (i - 3.5f) * 0.3f, (float)rng.NextDouble() * Mathf.Tau);
        }

        Plaza(m, rng, home);
    }

    // ---------------------------------------------------------------- the alpine valley

    static readonly Vector2 Lake = new(0, -420);
    static float LakeK(float x, float z) => Sq((x - Lake.X) / 270) + Sq((z - Lake.Y) / 140);

    static float AlpH(float x, float z)
    {
        float re = new Vector2(x / 1.7f, z).Length();
        float n = Noise(x * 0.003f + 11, z * 0.003f + 3);
        float ridge = 1 - Mathf.Abs(2 * Noise(x * 0.0022f + 4, z * 0.0022f + 9) - 1);
        // Forested slopes close round the valley, the high snowy peaks further back.
        float h = Kit.Smooth((re - 360) / 420) * (90 + 170 * n)
            + Kit.Smooth((re - 760) / 520) * (260 + 360 * n + 300 * ridge * ridge);
        return h * Kit.Smooth((LakeK(x, z) - 1) / 1.5f);
    }

    /// <summary>A mountain valley: chalets round the ground, a lake just past the main stand,
    /// pine forests up the slopes to bare rock and snow on the peaks; a church with an onion
    /// dome and a cable car up the mountain.</summary>
    static void Alpine(MeshData m, Random rng, uint home)
    {
        Core(m, 0xa3a09a, 0x5f8a3e, 40);
        uint[] pines = { 0x2a4529, 0x31502f, 0x283f2a };
        Terrain(m, 40, 1700, AlpH, c =>
        {
            if (c.Avg < 1.5f) return 0;
            float snow = 360 + 80 * Noise(c.X0 * 0.01f, c.Z0 * 0.01f);
            float nz = Noise(c.X0 * 0.03f + 2, c.Z0 * 0.03f);
            if (c.Avg > snow) return c.Slope > 1.4f ? 0xb9bec4u : 0xeef1f4u;
            if (c.Slope > 1.15f) return nz > 0.5f ? 0x7a766fu : 0x8a857cu;
            if (c.Avg > snow - 70) return c.Slope < 0.8f ? 0xd5dadeu : 0x8a857cu;
            if (c.Avg > 230) return nz > 0.5f ? 0x7d7a62u : 0x6f7a55u;
            if (c.Avg > 26) return nz > 0.45f ? 0x2f4f2eu : 0x36583au;
            return nz > 0.5f ? 0x5f8a3eu : 0x6a9445u;
        }, -0.1f, c =>
        {
            if (c.Avg < 26 || c.Avg > 230 || c.Slope > 1.15f || c.Mid.Length() > 1350) return;
            for (int k = 0; k < 3; k++)
                Pine(m, c.At((float)rng.NextDouble(), (float)rng.NextDouble()), 11 + (float)rng.NextDouble() * 6, (float)rng.NextDouble(), pines[rng.Next(pines.Length)]);
        });

        // The lake, a pebble shore, boats and a jetty.
        var lc = new Vector3(Lake.X, 0.09f, Lake.Y);
        m.Hex(0xb3aa98);
        for (int i = 0; i < 40; i++)
        {
            float a0 = Mathf.Tau * i / 40, a1 = Mathf.Tau * (i + 1) / 40;
            Vector3 E(float a, float k) => lc + new Vector3(Mathf.Cos(a) * 270 * k, -0.02f, Mathf.Sin(a) * 140 * k);
            m.QuadUV(E(a0, 1), E(a1, 1), E(a1, 1.07f), E(a0, 1.07f), new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }
        m.Hex(0x1b4d5c, Look.Water);
        for (int i = 0; i < 40; i++)
        {
            float a0 = Mathf.Tau * i / 40, a1 = Mathf.Tau * (i + 1) / 40;
            m.Tri(lc, lc + new Vector3(Mathf.Cos(a0) * 271, 0, Mathf.Sin(a0) * 141), lc + new Vector3(Mathf.Cos(a1) * 271, 0, Mathf.Sin(a1) * 141), Vector2.Zero, Vector2.Zero, Vector2.Zero);
        }
        for (int i = 0; i < 7; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.Tau, k = 0.3f + 0.55f * (float)rng.NextDouble();
            Boat(m, lc + new Vector3(Mathf.Cos(a) * 270 * k, -0.09f, Mathf.Sin(a) * 140 * k), (float)rng.NextDouble() * Mathf.Tau, rng.NextDouble() < 0.6, 0.8f, rng);
        }
        m.Hex(0x7a6a55);
        m.Box(new Transform3D(Basis.Identity, new Vector3(90, 0.6f, -296)), new Vector3(3, 0.4f, 40), 63);

        // Chalets round the valley floor; two big hotels; the church.
        for (int i = -18; i <= 18; i++)
            for (int j = -12; j <= 12; j++)
            {
                float x = i * 30 + 7 * Hash(i, j), z = j * 30 + 7 * Hash(j, i);
                if (Off(x, z) < RoadOut + 16 || LakeK(x, z) < 1.25f || AlpH(x, z) > 18 || Hash(i + 4, j - 3) > 0.26f || Mathf.Abs(z) < 12) continue;
                var p = new Vector3(x, AlpH(x, z) - 0.5f, z);
                float yaw = (Hash(i - 1, j + 1) < 0.5f ? 0 : Mathf.Pi / 2) + 0.1f * (Hash(i, j + 5) - 0.5f);
                bool big = Hash(i + 2, j + 9) < 0.06f;
                var size = big ? new Vector3(30, 4, 14) : new Vector3(11, 3.2f, 9);
                m.Hex(0xe9e3d6);
                m.Box(new Transform3D(new Basis(Vector3.Up, yaw), p + new Vector3(0, size.Y / 2, 0)), size, 1 | 2 | 16 | 32);
                m.Hex(0x8a5a34);
                Gable(m, p + new Vector3(0, size.Y, 0), yaw, new Vector3(size.X, big ? 9 : 3.6f, size.Z), big ? 5 : 3.4f, 0x4a3a30, 1.2f);
                m.Hex(0x6e4a2c);
                m.Box(new Transform3D(new Basis(Vector3.Up, yaw), p + new Vector3(0, size.Y + 0.6f, 0) + new Basis(Vector3.Up, yaw) * new Vector3(0, 0, size.Z / 2 + 0.8f)), new Vector3(size.X * 0.8f, 1.1f, 1.4f), 63);
            }
        {
            var c = new Vector3(230, 0, 150);
            m.Hex(0xf1eee6);
            Gable(m, c, 0, new Vector3(22, 10, 11), 6, 0x4a3a30, 0.6f);
            var t = c + new Vector3(-14, 0, 0);
            m.Box(new Transform3D(Basis.Identity, t + new Vector3(0, 13, 0)), new Vector3(7, 26, 7), 63);
            m.Hex(0x5f8a78);
            m.Blob(t + new Vector3(0, 28.4f, 0), new Vector3(4.6f, 4.4f, 4.6f), 8, 3);
            m.Column(t + new Vector3(0, 31.5f, 0), 1.2f, 0, 7, 6);
        }

        // The road along the valley, the railway on the far side.
        m.Hex(0x34363a, Look.Road, 12);
        Strip(m, new Vector3(-(Kit.BX + RoadOut - 2), 0.045f, 0), new Vector3(-1000, 0.045f, 0), 12);
        Strip(m, new Vector3(Kit.BX + RoadOut - 2, 0.045f, 0), new Vector3(1000, 0.045f, 0), 12);
        m.Hex(0x6b6259);
        Flat(m, -1000, 236, 1000, 244, 0.04f);
        for (int i = 0; i < 7; i++)
        {
            m.Hex(i == 0 ? 0xb8322au : 0xc0392bu);
            m.Box(new Transform3D(Basis.Identity, new Vector3(-120 + i * 19, 2.4f, 240)), new Vector3(18, 3.4f, 3), 63);
            m.Hex(0xe9e6de);
            m.Box(new Transform3D(Basis.Identity, new Vector3(-120 + i * 19, 3.2f, 240)), new Vector3(18.1f, 0.8f, 3.1f), 1 | 2 | 16 | 32);
        }

        // The cable car: a valley station, pylons up the mountain, two cabins on the line.
        {
            var a = new Vector3(420, 0, -140);
            var b = new Vector3(760, 0, -820);
            b.Y = AlpH(b.X, b.Z);
            m.Hex(0x8a8f94);
            m.Box(new Transform3D(Basis.Identity, a + new Vector3(0, 5, 0)), new Vector3(16, 10, 12), 63);
            m.Box(new Transform3D(Basis.Identity, b + new Vector3(0, 5, 0)), new Vector3(16, 12, 12), 63);
            var tops = new List<Vector3> { a + new Vector3(0, 9, 0) };
            for (int i = 1; i < 6; i++)
            {
                var p = a.Lerp(b, i / 6f);
                p.Y = AlpH(p.X, p.Z);
                m.Hex(0x8a8f94);
                m.Beam(p - new Vector3(0, 2, 0), p + new Vector3(0, 24, 0), 1.6f, 1.6f);
                m.Box(new Transform3D(Basis.Identity, p + new Vector3(0, 24, 0)), new Vector3(8, 1, 1), 63);
                tops.Add(p + new Vector3(0, 24, 0));
            }
            tops.Add(b + new Vector3(0, 11, 0));
            m.Hex(0x3a3d42);
            for (int i = 0; i + 1 < tops.Count; i++)
                foreach (float k in new[] { -3.5f, 3.5f }) m.Beam(tops[i] + new Vector3(k, 0, 0), tops[i + 1] + new Vector3(k, 0, 0), 0.25f, 0.25f);
            foreach (var (seg, f, k) in new[] { (1, 0.4f, 3.5f), (4, 0.6f, -3.5f) })
            {
                var p = tops[seg].Lerp(tops[seg + 1], f) + new Vector3(k, -4, 0);
                m.Hex(0xb8322a);
                m.Box(new Transform3D(Basis.Identity, p), new Vector3(3.4f, 3, 3.4f), 63);
                m.Hex(0x3a3d42);
                m.Beam(p, p + new Vector3(0, 4, 0), 0.25f, 0.25f);
            }
        }

        Plaza(m, rng, home, pines);
    }
}
