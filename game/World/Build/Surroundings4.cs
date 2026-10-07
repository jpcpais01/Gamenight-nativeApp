using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds.Build;

/// <summary>
/// Five more real places: an English mill town of terraced streets, Paris along the Seine, a
/// Greek island on the rim of its caldera, Kyoto under the temple hills, and a desert city of
/// sandstone and domes. Each landmark sits behind the main stand (-z), where the match camera looks.
/// </summary>
static partial class Surroundings
{
    static readonly Vector2 Z2 = Vector2.Zero;

    /// <summary>A box with no floor (5 faces), the workhorse of every town.</summary>
    static void Block(MeshData m, Vector3 foot, float yaw, Vector3 size) =>
        m.Box(new Transform3D(new Basis(Vector3.Up, yaw), foot + new Vector3(0, size.Y / 2, 0)), size, 1 | 2 | 4 | 16 | 32);

    // ---------------------------------------------------------------- the mill town

    const float ViaductZ = -300;

    static float MillH(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        return Kit.Smooth((r - 520) / 520) * (40 + 150 * Noise(x * 0.003f + 5, z * 0.003f + 9)) + Kit.Smooth((-z - 420) / 400) * 30;
    }

    /// <summary>A northern English town: red-brick terraces in long rows right up to the ground,
    /// slate roofs and chimney stacks, a railway viaduct striding across behind the main stand
    /// with a train on it, the mill and its tall chimney, the church spire, and the moors above.</summary>
    static void MillTown(MeshData m, Random rng, uint home)
    {
        Concourse(m, 0x8f8a84, Apron - 14);
        Collar(m, 0x3c3d40, Apron - 16);
        uint[] bricks = { 0x8a3d2a, 0x7c3626, 0x95492f, 0x6f3324 };
        Terrain(m, 40, 1680, MillH, c =>
        {
            if (Inside(c, Apron - 14)) return 0;
            if (c.Avg < 6) return 0x3c3d40;
            float nz = Noise(c.X0 * 0.02f + 1, c.Z0 * 0.02f + 3);
            return c.Avg > 90 ? (nz > 0.55f ? 0x6a5a6au : 0x7a6e4au) : nz > 0.5f ? 0x5d7038u : 0x687a42u;
        }, 0.04f, c =>
        {
            // Dry-stone walls cut the fields up the hillsides.
            if (c.Avg < 10 || c.Avg > 120 || c.Mid.Length() > 1000 || Hash(c.X0, c.Z0) > 0.45f) return;
            m.Hex(0x7d7a70);
            var a = c.At(0, 0.5f); var b = c.At(1, 0.5f);
            Strip(m, a + new Vector3(0, 0.6f, 0), b + new Vector3(0, 0.6f, 0), 0.8f);
        });

        // The terraces: two rows back to back with an alley between, a street each side, in
        // runs of eight houses with a gap for the cross streets.
        const float Pitch = 30, Run = 40, Cross = 130;
        for (float z = -560; z <= 560; z += Pitch)
            for (float x = -560; x <= 560; x += Cross)
                for (int k = 0; k < 3; k++)
                {
                    float cx = x + 5 + Run / 2 + k * Run;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float cz = z + side * 6.5f;
                        float off = Mathf.Min(Mathf.Min(Off(cx - Run / 2, cz), Off(cx + Run / 2, cz)), Off(cx, cz));
                        if (off < Apron - 4 || cx * cx + cz * cz > 520 * 520) continue;
                        if (Mathf.Abs(cz - ViaductZ) < 16 || new Vector2(cx - 230, cz + 400).Length() < 70 || new Vector2(cx + 150, cz + 350).Length() < 40) continue;
                        float y = MillH(cx, cz) - 0.5f;
                        float h = 6.6f + (Hash(cx, cz) < 0.2f ? 2.4f : 0);
                        m.Hex(bricks[(int)(Hash(cz, cx) * bricks.Length)], Look.House);
                        Gable(m, new Vector3(cx, y, cz), 0, new Vector3(Run - 1, h, 8.5f), 2.8f, 0x4a4f58, 0.3f);
                        // Chimney stacks along the ridge.
                        m.Hex(0x6f3324);
                        for (int s = 0; s < 2; s++)
                            Block(m, new Vector3(cx - Run / 2 + 10 + s * 20, y + h + 1.4f, cz), 0, new Vector3(1.4f, 2.8f, 2.4f));
                    }
                }

        // The viaduct: brick arches on tall piers, a train crossing.
        m.Hex(0x7c3626, Look.Brick);
        for (float x = -820; x <= 820; x += 22)
        {
            float g = MillH(x, ViaductZ);
            float deck = 26 + Mathf.Max(0, 18 - g * 0.3f);
            Block(m, new Vector3(x, g - 1, ViaductZ), 0, new Vector3(4, deck - g + 1 + 1, 9));
            m.Box(new Transform3D(Basis.Identity, new Vector3(x + 11, deck - 1.2f, ViaductZ)), new Vector3(22, 2.6f, 9), 63);
        }
        float trainX = -160;
        uint[] carriage = { 0x1f3f7a, 0xd9dde2, 0x1f3f7a, 0xd9dde2, 0x1f3f7a };
        for (int i = 0; i < 5; i++)
        {
            float deck = 26 + Mathf.Max(0, 18 - MillH(trainX + i * 21, ViaductZ) * 0.3f);
            m.Hex(carriage[i]);
            Block(m, new Vector3(trainX + i * 21, deck + 0.1f, ViaductZ), 0, new Vector3(20, 3.8f, 3.2f));
            m.Hex(0xf2c230);
            Block(m, new Vector3(trainX + i * 21, deck + 1.6f, ViaductZ + 1.65f), 0, new Vector3(19, 0.8f, 0.1f));
        }

        // The mill: a long brick block, rows of windows, the chimney.
        {
            var p = new Vector3(230, MillH(230, -400) - 1, -400);
            m.Hex(0x8a4530, Look.Tower, 2);
            Block(m, p, 0.1f, new Vector3(110, 26, 30));
            m.Hex(0x4a4f58);
            Block(m, p + new Vector3(0, 26, 0), 0.1f, new Vector3(110, 1.2f, 30));
            m.Hex(0x6f3324, Look.Brick);
            m.Column(p + new Vector3(70, 0, -10), 4.5f, 2.6f, 72, 8);
            m.Hex(0x2b2a29);
            m.Column(p + new Vector3(70, 71, -10), 2.9f, 2.9f, 2.4f, 8);
        }

        // The church and its spire, a cricket field and a park.
        Church(m, new Vector3(-150, MillH(-150, -350) - 0.5f, -350), 0.2f, 0x8a8478, 0x4a4f58, 1.5f);
        m.Hex(0x6c8a3c);
        Flat(m, -480, 220, -360, 330, MillH(-420, 270) + 0.2f);

        Edge(m, rng, home, Apron - 15, Leaves, p => Hash(p.X, p.Z) < 0.5f, 130);
    }

    // ---------------------------------------------------------------- paris

    static float SeineZ(float x) => -400 + 60 * Mathf.Sin(x / 300 + 0.5f);
    static readonly Vector2 Butte = new(-90, -760);

    static float ParisH(float x, float z) => 105 * Mathf.Exp(-(Sq((x - Butte.X) / 220) + Sq((z - Butte.Y) / 180)));

    /// <summary>On an avenue: is (x, z) on one of the eight avenues out of the square?</summary>
    static bool OnAvenue(float x, float z, float w)
    {
        float a = Mathf.Atan2(z, x), r = Mathf.Sqrt(x * x + z * z);
        float da = Mathf.Abs(Mathf.PosMod(a + Mathf.Pi / 8, Mathf.Pi / 4) - Mathf.Pi / 8);
        return da * r < w;
    }

    /// <summary>Paris: the ground on its own place, eight tree-lined avenues out of it between
    /// cream stone blocks with grey mansard roofs, the Seine with its stone bridges and quays
    /// behind the main stand, and the white basilica on its hill above the roofs.</summary>
    static void Paris(MeshData m, Random rng, uint home)
    {
        const float Sq0 = 150;
        Concourse(m, 0xc9bfa8, Apron - 4);
        Collar(m, 0xb8ad94, Apron - 6);
        // The place: pale gravel and stone round the ground, out to where the blocks begin.
        m.Hex(0xbcb39c);
        RingStrip(m, Apron - 6, Sq0 - Kit.BZ + 10, 0.03f);
        Terrain(m, 40, 1680, ParisH, c =>
        {
            if (Inside(c, Sq0 - Kit.BZ + 8)) return 0;
            if (Mathf.Abs(c.Mid.Y - SeineZ(c.Mid.X)) < 52) return 0x9a958au;
            return c.Avg > 40 ? 0x5f7a3cu : 0x8a857au;
        }, 0.02f);
        // The river between stone quays, three bridges across.
        m.Hex(0x3b6170, Look.Water);
        for (float x = -1680; x < 1680; x += 40)
        {
            float z0 = SeineZ(x), z1 = SeineZ(x + 40);
            m.QuadUV(new(x, 0.1f, z0 - 40), new(x, 0.1f, z0 + 40), new(x + 40, 0.1f, z1 + 40), new(x + 40, 0.1f, z1 - 40), new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }
        foreach (float bx in new[] { -380f, 0, 360 })
        {
            float z = SeineZ(bx);
            m.Hex(0xcfc4ab);
            for (int k = 0; k < 4; k++)
            {
                float az = z - 45 + k * 30;
                m.Box(new Transform3D(Basis.Identity, new Vector3(bx, 2, az)), new Vector3(20, 4, 3), 63);
            }
            m.Box(new Transform3D(Basis.Identity, new Vector3(bx, 4.6f, z)), new Vector3(20, 1.4f, 104), 63);
        }

        // The blocks: a 64 m grid of stone buildings, six storeys, a mansard on top, cut by the
        // river, the avenues (lined with plane trees) and the basilica's hill.
        uint[] stone = { 0xe6dcc4, 0xddd1b6, 0xe9e1cd, 0xd6c9ad };
        for (float x = -640; x <= 640; x += 64)
            for (float z = -760; z <= 640; z += 64)
            {
                var c = new Vector2(x, z);
                if (c.Length() < Sq0 + 60 || c.Length() > 680 && z > -500) continue;
                if (Mathf.Abs(z - SeineZ(x)) < 80 || ParisH(x, z) > 16) continue;
                for (int q = 0; q < 4; q++)
                {
                    float qx = x + (q % 2 == 0 ? -14 : 14), qz = z + (q < 2 ? -14 : 14);
                    if (OnAvenue(qx, qz, 22) || Mathf.Abs(qz - SeineZ(qx)) < 72) continue;
                    float h = 21 + 5 * Hash(qx, qz);
                    var foot = new Vector3(qx, ParisH(qx, qz) - 0.5f, qz);
                    m.Hex(stone[(int)(Hash(qz, qx) * stone.Length)], Look.Tower, 2);
                    Block(m, foot, 0, new Vector3(25, h, 25));
                    m.Hex(0x6b7178);
                    m.Prism(new Transform3D(Basis.Identity, foot + new Vector3(0, h, 0)), new Vector2[] { new(-12.5f, 0), new(12.5f, 0), new(10, 5), new(-10, 5) }, 25);
                    m.Hex(0xb55a3a);
                    Block(m, foot + new Vector3(-6, h + 4.5f, -9), 0, new Vector3(4, 2, 1));
                }
            }
        // Plane trees along the avenues.
        for (int a = 0; a < 8; a++)
        {
            float ang = a * Mathf.Pi / 4;
            var dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
            var side = new Vector3(-dir.Z, 0, dir.X);
            for (float r = Sq0 + 30; r < 640; r += 16)
            {
                var p = dir * r;
                if (Mathf.Abs(p.Z - SeineZ(p.X)) < 60) continue;
                foreach (float s in new[] { -1f, 1f }) Tree(m, p + side * s * 13, 11, 0x5a7a34);
            }
        }

        // The basilica on the butte: white stone, a great dome and four small ones, a bell tower.
        {
            var c = new Vector3(Butte.X, ParisH(Butte.X, Butte.Y) - 1, Butte.Y + 40);
            const uint White = 0xf2efe6;
            m.Hex(White);
            Block(m, c, 0, new Vector3(56, 26, 46));
            m.Column(c + new Vector3(0, 26, 0), 13, 13, 16, 12);
            m.Blob(c + new Vector3(0, 42, 0), new Vector3(13, 18, 13), 12, 4, true);
            m.Column(c + new Vector3(0, 59, 0), 2.2f, 2.2f, 6, 8);
            m.Column(c + new Vector3(0, 65, 0), 2.6f, 0, 5, 8);
            foreach (var (dx, dz) in new[] { (-20f, 16f), (20f, 16f), (-20f, -14f), (20f, -14f) })
            {
                m.Column(c + new Vector3(dx, 26, dz), 5, 5, 6, 8);
                m.Blob(c + new Vector3(dx, 32, dz), new Vector3(5, 7, 5), 8, 3, true);
            }
            Block(m, c + new Vector3(0, 0, -34), 0, new Vector3(12, 52, 12));
            m.Blob(c + new Vector3(0, 52, -34), new Vector3(6, 9, 6), 8, 3, true);
            m.Hex(0xd9d1bf);
            Flat(m, c.X - 40, c.Z + 22, c.X + 40, c.Z + 70, ParisH(c.X, c.Z + 46) + 0.4f);
        }

        // The iron tower across the river: four arched legs into one tapering shaft.
        {
            var b = new Vector3(470, 0, -560);
            m.Hex(0x6b5a48);
            for (int l = 0; l < 4; l++)
            {
                var dir = new Vector3(l % 2 == 0 ? -1 : 1, 0, l < 2 ? -1 : 1);
                Vector3 Leg(float t) => b + dir * Mathf.Lerp(62, 9, Mathf.Sqrt(t)) + new Vector3(0, 125 * t, 0);
                for (int k = 0; k < 4; k++) m.Beam(Leg(k / 4f), Leg((k + 1) / 4f), 7 - k, 7 - k);
            }
            m.Box(new Transform3D(Basis.Identity, b + new Vector3(0, 58, 0)), new Vector3(70, 4, 70), 63);
            m.Box(new Transform3D(Basis.Identity, b + new Vector3(0, 116, 0)), new Vector3(40, 3, 40), 63);
            m.Column(b + new Vector3(0, 116, 0), 13, 3, 165, 4, Mathf.Pi / 4);
            m.Box(new Transform3D(Basis.Identity, b + new Vector3(0, 278, 0)), new Vector3(8, 6, 8), 63);
            m.Column(b + new Vector3(0, 281, 0), 0.6f, 0.2f, 18, 4);
        }

        Edge(m, rng, home, Apron - 5, new uint[] { 0x5a7a34, 0x4f7030 }, null, 150);
        FarHills(m, 1200, 70);
    }

    // ---------------------------------------------------------------- the island

    static readonly Vector2 Caldera = new(0, -700);
    const float CalR = 480, SeaY = -140;

    static float CalD(float x, float z) => new Vector2(x - Caldera.X, z - Caldera.Y).Length();

    static float IslandH(float x, float z)
    {
        float d = CalD(x, z);
        // The plateau rises gently from the ground to the far rim; sheer cliffs drop into the
        // caldera; the ring of land runs down to the open sea outside.
        float rim = Kit.Smooth((-z - 150) / 800) * (120 + 110 * Noise(x * 0.004f + 2, z * 0.004f + 5)) + 6 * Noise(x * 0.02f, z * 0.02f);
        float h = Mathf.Lerp(SeaY - 6, rim, Kit.Smooth((d - CalR + 4) / 46));
        return Mathf.Lerp(h, SeaY - 6, Kit.Smooth((d - CalR - 520) / 180));
    }

    /// <summary>A Greek island: the ground up on the dry plateau, the white town strung along
    /// the cliffs of the caldera behind the main stand, blue domes, windmills, the deep blue sea
    /// below and all round, a volcano islet out in the middle.</summary>
    static void Island(MeshData m, Random rng, uint home)
    {
        Concourse(m, 0xe0d8c8, Apron - 6);
        Collar(m, 0xc9b48a, Apron - 8);
        // The sea, all round and down inside the caldera.
        m.Hex(0x1f5a8e, Look.Water);
        m.QuadUV(new(-1700, SeaY, -1700), new(-1700, SeaY, 1700), new(1700, SeaY, 1700), new(1700, SeaY, -1700), new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        uint[] layers = { 0x8a4a36, 0x3a3330, 0xcbb894, 0x9a5a40, 0x4a403a };
        Terrain(m, 40, 1680, IslandH, c =>
        {
            if (Inside(c, Apron - 6)) return 0;
            if (c.Avg < SeaY - 3) return 0;
            float nz = Noise(c.X0 * 0.02f + 3, c.Z0 * 0.02f + 1);
            // The caldera's cliffs: bands of red, black and cream rock.
            if (c.Slope > 0.9f) return layers[(int)Mathf.PosMod(c.Avg / 22 + nz, layers.Length)];
            if (c.Avg < SeaY + 4) return 0x5a5048;
            float dd = CalD(c.Mid.X, c.Mid.Y);
            if (dd < CalR + 110 && c.Avg > SeaY + 60) return nz > 0.4f ? 0xf0ece2u : 0xe6dccau;
            return nz > 0.62f ? 0x8a9058u : nz < 0.3f ? 0xb89a6au : 0xc9b48au;
        }, 0.05f, c =>
        {
            var p = c.Mid;
            float d = CalD(p.X, p.Y);
            if (c.Avg < SeaY + 4 || Off(p.X, p.Y) < Apron + 10) return;
            // The town: along the rim and tumbling down the top of the cliff.
            if (d > CalR - 30 && d < CalR + 130 && c.Slope < 3f && c.Avg > SeaY + 60)
            {
                int n = d < CalR + 60 ? 7 : 3;
                for (int k = 0; k < n; k++)
                {
                    var a = c.At((float)rng.NextDouble(), (float)rng.NextDouble());
                    if (CalD(a.X, a.Z) < CalR - 8) continue;
                    float big = Mathf.Clamp((-a.Z - 300) / 500, 0, 1) * 0.8f + 1;
                    float w = (6 + 3 * (float)rng.NextDouble()) * big, h = (4 + 3 * (float)rng.NextDouble()) * big;
                    m.Hex(rng.NextDouble() < 0.88 ? 0xf6f4efu : 0xe8c87au);
                    Block(m, a - new Vector3(0, 3, 0), (float)rng.NextDouble() * 0.4f, new Vector3(w, h + 3, w * 0.9f));
                    if (rng.NextDouble() < 0.12)
                    {
                        m.Hex(0x1f5fb8);
                        m.Blob(a + new Vector3(0, h, 0), new Vector3(2.8f, 3.2f, 2.8f), 8, 2, true);
                    }
                }
                return;
            }
            // Out in the country: a white farmhouse now and then, olives and dry-stone terraces.
            if (Hash(c.X0, c.Z0) < 0.1f)
            {
                var a = c.At(0.5f, 0.5f);
                m.Hex(0xf6f4ef);
                Block(m, a - new Vector3(0, 2, 0), 0.3f, new Vector3(10, 7, 8));
            }
            else if (Hash(c.Z0, c.X0) < 0.35f && c.Mid.Length() < 900)
            {
                m.Hex(0x6f7a4a);
                m.Blob(c.At(0.3f, 0.6f) + new Vector3(0, 2, 0), new Vector3(2.6f, 2, 2.6f), 4, 2);
            }
        });
        // Churches with blue domes and bell walls along the rim; windmills on the high points.
        for (int i = 0; i < 6; i++)
        {
            float a = -Mathf.Pi / 2 - 1.1f + i * 0.44f;
            var p2 = Caldera + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (CalR + 26);
            var p = new Vector3(p2.X, IslandH(p2.X, p2.Y) - 1, p2.Y);
            if (p.Y < SeaY + 50) continue;
            const float K = 2.2f;
            m.Hex(0xfbfaf6);
            Block(m, p, a, new Vector3(12, 9, 9) * K);
            m.Column(p + new Vector3(0, 9 * K, 0), 4 * K, 4 * K, 2 * K, 10);
            m.Hex(0x1f5fb8);
            m.Blob(p + new Vector3(0, 11 * K, 0), new Vector3(4, 4.6f, 4) * K, 10, 3, true);
            m.Hex(0xfbfaf6);
            Block(m, p + new Vector3(7 * K, 0, 0), a, new Vector3(1, 15, 6) * K);
        }
        foreach (float a in new[] { -2.3f, -2.1f, -0.9f, -0.75f })
        {
            var p2 = Caldera + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (CalR + 90);
            Windmill(m, new Vector3(p2.X, IslandH(p2.X, p2.Y), p2.Y), a + Mathf.Pi);
        }
        // The volcano islet in the caldera: dark lava.
        m.Hex(0x3a3330);
        m.Column(new Vector3(Caldera.X + 80, SeaY - 2, Caldera.Y - 40), 170, 50, 52, 10);
        // Boats and a cruise ship out on the caldera.
        for (int i = 0; i < 7; i++)
            Boat(m, new Vector3(-320 + i * 100, SeaY, Caldera.Y + 260 - 120 * (i % 3)), i * 1.1f, i % 2 == 0, 1.6f, rng);
        m.Hex(0xf4f4f0);
        Block(m, new Vector3(-200, SeaY - 2, Caldera.Y - 120), 0.3f, new Vector3(150, 16, 24));
        Block(m, new Vector3(-200, SeaY + 14, Caldera.Y - 120), 0.3f, new Vector3(110, 10, 20));
        m.Hex(0x1f3f7a);
        Block(m, new Vector3(-200, SeaY - 2, Caldera.Y - 120), 0.3f, new Vector3(151, 4, 25));

        Edge(m, rng, home, Apron - 7, new uint[] { 0x6f7a4a, 0x7a8452 }, p => Hash(p.X, p.Z) < 0.4f, 100);
    }

    // ---------------------------------------------------------------- kyoto

    static float KyotoH(float x, float z)
    {
        float hills = Kit.Smooth((-z - 360) / 300) * (90 + 120 * Noise(x * 0.004f + 2, z * 0.004f + 6));
        float sides = Kit.Smooth((Mathf.Abs(x) - 620) / 400) * 120 * Noise(x * 0.004f, z * 0.004f + 3);
        // The great snow-capped cone far off behind it all.
        float d = new Vector2(x + 260, z + 1450).Length();
        float fuji = Mathf.Max(0, 640 - d) * 1.05f;
        return Mathf.Max(Mathf.Max(hills, sides), fuji);
    }

    static void Torii(MeshData m, Vector3 p, float yaw, float k)
    {
        var b = new Basis(Vector3.Up, yaw);
        m.Hex(0xd1372a);
        foreach (float s in new[] { -1f, 1f })
            m.Box(new Transform3D(b, p + b * new Vector3(s * 2.6f * k, 3 * k, 0)), new Vector3(0.6f, 6, 0.6f) * k, 1 | 2 | 16 | 32);
        m.Box(new Transform3D(b, p + new Vector3(0, 5 * k, 0)), new Vector3(6.6f, 0.5f, 0.5f) * k, 63);
        m.Hex(0x1b1a1c);
        m.Box(new Transform3D(b, p + new Vector3(0, 6.2f * k, 0)), new Vector3(8.2f, 0.7f, 0.9f) * k, 63);
    }

    /// <summary>A roof that sweeps out wide: a flat pyramid of four sides over a w x d plan.</summary>
    static void Hip(MeshData m, Vector3 c, float w, float d, float rise)
    {
        var t = c + new Vector3(0, rise, 0);
        Vector3[] q = { c + new Vector3(-w / 2, 0, -d / 2), c + new Vector3(w / 2, 0, -d / 2), c + new Vector3(w / 2, 0, d / 2), c + new Vector3(-w / 2, 0, d / 2) };
        for (int i = 0; i < 4; i++) m.Tri(q[(i + 1) % 4], q[i], t, Z2, Z2, Z2);
    }

    /// <summary>Kyoto: low wooden town houses with dark tiled roofs in tight streets round the
    /// ground, cherry trees in blossom along the canal, a five-storey pagoda and a temple on the
    /// wooded hill behind the main stand with a tunnel of red torii gates climbing to it, and the
    /// great snow-capped cone far beyond.</summary>
    static void Kyoto(MeshData m, Random rng, uint home)
    {
        const float Canal = 175;
        Concourse(m, 0xb5aea0, Apron - 8);
        Collar(m, 0x9a9488, Apron - 10);
        Terrain(m, 40, 1680, KyotoH, c =>
        {
            if (Inside(c, Apron - 8)) return 0;
            float h = c.Avg;
            float nz = Noise(c.X0 * 0.02f + 2, c.Z0 * 0.02f);
            if (h > 520 + 40 * nz) return 0xf2f4f6;
            if (h > 250 && new Vector2(c.Mid.X + 260, c.Mid.Y + 1450).Length() < 650) return nz > 0.5f ? 0x5a5a62u : 0x6a6a70u;
            if (h > 6) return nz > 0.72f ? 0x8a4a32u : nz > 0.35f ? 0x2f5a30u : 0x3a6a36u;
            return 0x9a9488;
        }, 0.04f, c =>
        {
            if (c.Avg < 6 || c.Avg > 300 || c.Mid.Length() > 1000) return;
            for (int k = 0; k < 2; k++)
            {
                var t = c.At((float)rng.NextDouble(), (float)rng.NextDouble());
                float nz = Noise(c.X0 * 0.02f + 2, c.Z0 * 0.02f);
                m.Hex(nz > 0.72f ? 0xa8482eu : rng.NextDouble() < 0.5 ? 0x2a522cu : 0x356236u);
                m.Blob(t + new Vector3(0, 5, 0), new Vector3(6, 6, 6), 5, 2);
            }
        });

        // The canal in front of the main stand's side, stone edged, cherry trees all along it.
        m.Hex(0x3c5f62, Look.Water);
        Flat(m, -900, -Canal - 7, 900, -Canal + 7, 0.08f);
        for (float x = -700; x < 700; x += 11)
            foreach (float s in new[] { -1f, 1f })
            {
                var p = new Vector3(x + 4 * Hash(x, s), 0, -Canal + s * 12);
                m.Hex(0x4a3628);
                m.Beam(p, p + new Vector3(0.6f, 3.4f, 0), 0.5f, 0.5f);
                m.Hex(Hash(s, x) < 0.5f ? 0xf2b8c8u : 0xf7d3de);
                m.Blob(p + new Vector3(0.6f, 5.4f, 0), new Vector3(4.6f, 2.8f, 4.6f), 5, 2);
            }

        // The town: two-storey wooden houses, white plaster or dark timber, dark tile roofs, in
        // narrow streets; shops with red lanterns.
        uint[] walls = { 0x5a3f2c, 0xe8e2d4, 0x4a3426, 0xd9d0bc };
        for (float x = -520; x <= 520; x += 18)
            for (float z = -330; z <= 520; z += 16)
            {
                if (Mathf.Abs(x % 96) < 9 || Mathf.Abs(z % 70) < 5) continue;
                if (Off(x, z) < Apron + 4 || Mathf.Abs(z + Canal) < 22 || x * x + z * z > 520 * 520 || KyotoH(x, z) > 8) continue;
                if (Hash(x, z) < 0.08f) continue;
                var p = new Vector3(x, KyotoH(x, z) - 0.3f, z);
                float h = 5.5f + 2.5f * Hash(z, x);
                m.Hex(walls[(int)(Hash(x + 1, z) * walls.Length)]);
                Block(m, p, 0, new Vector3(13, h, 11));
                m.Hex(0x34363c);
                Hip(m, p + new Vector3(0, h, 0), 15, 13, 3);
                if (Hash(z + 2, x) < 0.15f)
                {
                    m.Hex(0xe8402a, Look.Unlit);
                    Block(m, p + new Vector3(0, h - 2.2f, 5.8f), 0, new Vector3(0.8f, 1.1f, 0.8f));
                }
            }

        // Up the hill: the torii tunnel climbing to the temple, the five-storey pagoda.
        var gate = new Vector3(-160, 0, -420);
        var temple = new Vector3(-210, 0, -660);
        temple.Y = KyotoH(temple.X, temple.Z);
        for (int i = 0; i < 46; i++)
        {
            var p = gate.Lerp(temple, i / 46f);
            p.Y = KyotoH(p.X, p.Z);
            Torii(m, p, Mathf.Atan2(temple.X - gate.X, temple.Z - gate.Z), 1.6f);
        }
        {
            // The temple hall: a broad hip roof over red pillars.
            m.Hex(0xd1372a);
            Block(m, temple - new Vector3(0, 1, 0), 0, new Vector3(54, 16, 36));
            m.Hex(0x2a2c30);
            Hip(m, temple + new Vector3(0, 15, 0), 72, 50, 15);
        }
        {
            var p = new Vector3(110, 0, -600);
            p.Y = KyotoH(p.X, p.Z) - 1;
            const float K = 1.8f;
            for (int s = 0; s < 5; s++)
            {
                float w = (12 - s * 1.4f) * K, y = p.Y + s * 8 * K;
                m.Hex(0xc9442e);
                Block(m, new Vector3(p.X, y, p.Z), 0, new Vector3(w, 6 * K, w));
                m.Hex(0x2a2c30);
                Hip(m, new Vector3(p.X, y + 6 * K, p.Z), w + 7 * K, w + 7 * K, 2.2f * K);
            }
            m.Hex(0xb08a3a);
            m.Column(new Vector3(p.X, p.Y + 41 * K, p.Z), 0.5f * K, 0.2f * K, 12 * K, 6);
        }

        Edge(m, rng, home, Apron - 9, new uint[] { 0xf2b8c8, 0x3a6a36, 0xf7d3de }, null, 130);
    }

    // ---------------------------------------------------------------- the desert city

    static float DesertH(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float dunes = Mathf.Abs(Mathf.Sin(x * 0.012f + 2 * Noise(x * 0.003f, z * 0.003f) * 3)) * 26 + 30 * Noise(x * 0.004f + 4, z * 0.004f);
        return Kit.Smooth((r - 640) / 400) * dunes;
    }

    /// <summary>A desert city: sandstone houses with flat roofs and wind towers, a great mosque
    /// with a golden dome and four minarets behind the main stand, date palms on the boulevards,
    /// glass towers rising out of the haze, and the dunes all round with a camel train.</summary>
    static void Desert(MeshData m, Random rng, uint home)
    {
        Concourse(m, 0xe2d4b4, Apron - 6);
        Collar(m, 0xd6c29a, Apron - 8);
        Terrain(m, 40, 1680, DesertH, c =>
        {
            if (Inside(c, Apron - 6)) return 0;
            float nz = Noise(c.X0 * 0.01f, c.Z0 * 0.01f);
            if (c.Avg < 1) return 0xcdb68a;
            return nz > 0.5f ? 0xe0b878u : 0xd8ac6au;
        }, 0.04f);

        // Boulevards out of the square on all four sides, palms down the middle.
        m.Hex(0x4d4b47, Look.Road, 12);
        Strip(m, new Vector3(-700, 0.06f, 0), new Vector3(-Kit.BX - Apron + 6, 0.06f, 0), 24);
        Strip(m, new Vector3(Kit.BX + Apron - 6, 0.06f, 0), new Vector3(700, 0.06f, 0), 24);
        Strip(m, new Vector3(0, 0.06f, Kit.BZ + Apron - 6), new Vector3(0, 0.06f, 700), 24);
        for (float r = Kit.BX + Apron + 4; r < 640; r += 14)
        {
            Palm(m, new Vector3(-r, 0, 0), 10 + 3 * Hash(r, 1), r);
            Palm(m, new Vector3(r, 0, 0), 10 + 3 * Hash(r, 2), r * 2);
            if (r + Kit.BZ - Kit.BX < 640) Palm(m, new Vector3(0, 0, r + Kit.BZ - Kit.BX), 10 + 3 * Hash(r, 3), r * 3);
        }

        // The old city: sandstone houses packed round courtyards, wind towers on the roofs.
        uint[] sand = { 0xd8c29a, 0xcdb48a, 0xe2d2b0, 0xc9a878 };
        var mosque = new Vector3(-210, 0, -560);
        for (float x = -600; x <= 600; x += 18)
            for (float z = -600; z <= 600; z += 18)
            {
                if (Off(x, z) < Apron + 6 || x * x + z * z > 620 * 620 || Mathf.Abs(z) < 24 && Mathf.Abs(x) > 60 || Mathf.Abs(x) < 24 && z > 60) continue;
                if (new Vector2(x - mosque.X, z - mosque.Z).Length() < 170 || Hash(x, z) < 0.1f) continue;
                var p = new Vector3(x + 3 * (Hash(z, x) - 0.5f), -0.5f, z);
                float h = 6 + 8 * Hash(x + 1, z);
                m.Hex(sand[(int)(Hash(z + 1, x) * sand.Length)]);
                Block(m, p, 0, new Vector3(15, h, 15));
                if (Hash(x, z + 3) < 0.25f)
                    Block(m, p + new Vector3(4, h, 4), 0, new Vector3(3, 5, 3));
            }

        // The great mosque: a courtyard, the prayer hall, the golden dome, four minarets.
        {
            var c = mosque;
            const uint Stone = 0xf0e6d0;
            const float K = 2.1f;
            Vector3 At(float dx, float dy, float dz) => c + new Vector3(dx, dy, dz) * K;
            m.Hex(Stone);
            Block(m, At(0, -0.3f, 40), 0, new Vector3(150, 9, 70) * K);
            Block(m, At(0, -0.3f, -30), 0, new Vector3(110, 20, 60) * K);
            m.Column(At(0, 19.5f, -30), 21 * K, 21 * K, 6 * K, 16);
            m.Hex(0xd9a83a);
            m.Blob(At(0, 25.5f, -30), new Vector3(21, 26, 21) * K, 16, 5, true);
            m.Column(At(0, 51, -30), 1 * K, 0.2f * K, 7 * K, 6);
            m.Hex(0x2f8f8a);
            foreach (var (dx, dz) in new[] { (-38f, -30f), (38f, -30f) })
                m.Blob(At(dx, 19.5f, dz), new Vector3(9, 10, 9) * K, 10, 3, true);
            foreach (var (dx, dz) in new[] { (-70f, 75f), (70f, 75f), (-70f, -60f), (70f, -60f) })
            {
                var t = At(dx, -0.3f, dz);
                m.Hex(Stone);
                m.Column(t, 3.4f * K, 2.6f * K, 62 * K, 8);
                m.Box(new Transform3D(Basis.Identity, t + new Vector3(0, 44 * K, 0)), new Vector3(8, 1.2f, 8) * K, 63);
                m.Hex(0xd9a83a);
                m.Column(t + new Vector3(0, 61.5f * K, 0), 2.8f * K, 0, 9 * K, 8);
            }
        }

        // The new city rising out of the haze on the far side: glass towers.
        for (int i = 0; i < 14; i++)
        {
            float x = 300 + i * 46 + 20 * Hash(i, 1), z = -720 - 160 * Hash(i, 2);
            float h = 90 + 220 * Hash(i, 3) + (i == 6 ? 220 : 0);
            Building(m, new Vector3(x, -0.5f, z), new Vector3(26 + 10 * Hash(i, 4), h, 26), 0x8aa6b8, 1, false);
        }
        // A camel train along a dune.
        for (int i = 0; i < 7; i++)
        {
            var p = new Vector3(-700 + i * 9, 0, -620 - i * 3);
            p.Y = DesertH(p.X, p.Z);
            m.Hex(0xa9825a);
            Block(m, p + new Vector3(0, 1.8f, 0), 0.3f, new Vector3(1.2f, 1.4f, 3.2f));
            Block(m, p + new Vector3(0, 3.2f, -0.2f), 0.3f, new Vector3(1, 0.8f, 1.2f));
            Block(m, p + new Vector3(0.6f, 2.4f, 1.6f), 0.3f, new Vector3(0.5f, 2.2f, 0.5f));
            foreach (float s in new[] { -0.4f, 0.4f })
                Block(m, p + new Vector3(s, 0, 0), 0.3f, new Vector3(0.3f, 1.8f, 2.4f));
        }

        Edge(m, rng, home, Apron - 7, new uint[] { 0x4f7a3a }, p => Hash(p.X, p.Z) < 0.6f, 130);
    }
}
