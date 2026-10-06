using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds.Build;

/// <summary>
/// Three more places to build in: a whitewashed hill town in the Alentejo, the shore of a
/// Norwegian fjord, and a tropical city where the houses climb the hills behind the ground.
/// As everywhere, the landmark sits behind the main stand (-z), where the match camera looks.
/// </summary>
static partial class Surroundings
{
    // ---------------------------------------------------------------- the alentejo

    static readonly Vector2 Hilltop = new(70, -470);
    const float TownR = 230;

    static float AlenH(float x, float z)
    {
        float hill = 58 * Mathf.Exp(-(Sq((x - Hilltop.X) / 230) + Sq((z - Hilltop.Y) / 190)));
        float r = Mathf.Sqrt(x * x + z * z);
        float roll = Kit.Smooth((r - 260) / 380) * (6 + 46 * Noise(x * 0.004f + 7, z * 0.004f + 2));
        return hill + roll;
    }

    static float TownDist(float x, float z) => new Vector2(x - Hilltop.X, z - Hilltop.Y).Length();

    /// <summary>A whitewashed house: white walls, a band of ochre or blue round the foot, a low
    /// roof of red tiles, now and then a big white chimney.</summary>
    static void Casa(MeshData m, Vector3 p, float yaw, Vector3 size, uint band, bool chimney)
    {
        var b = new Basis(Vector3.Up, yaw);
        m.Hex(0xf3f1ea);
        Gable(m, p, yaw, size, 1.7f, 0xb5582f, 0.35f);
        m.Hex(band);
        m.Box(new Transform3D(b, p + new Vector3(0, 0.45f, 0)), new Vector3(size.X + 0.12f, 0.9f, size.Z + 0.12f), 1 | 2 | 16 | 32);
        if (!chimney) return;
        m.Hex(0xf3f1ea);
        var c = p + b * new Vector3(size.X * 0.3f, 0, 0) + new Vector3(0, size.Y + 1.6f, 0);
        m.Box(new Transform3D(b, c), new Vector3(1.6f, 3.2f, 1.2f), 1 | 2 | 4 | 16 | 32);
        m.Hex(0xb5582f);
        m.Box(new Transform3D(b, c + new Vector3(0, 1.8f, 0)), new Vector3(2, 0.4f, 1.6f), 63);
    }

    static void CorkOak(MeshData m, Vector3 p, float k)
    {
        // Stripped of its cork, the trunk shows red-brown; a wide flat dark crown.
        m.Hex(0x8a3f22);
        m.Beam(p - new Vector3(0, 0.5f, 0), p + new Vector3(0.5f, 3.6f * k, 0.3f), 0.9f * k, 0.9f * k);
        m.Hex(Hash(p.X, p.Z) < 0.5f ? 0x41592cu : 0x4a6232u);
        m.Blob(p + new Vector3(0.5f, 5.6f * k, 0.3f), new Vector3(5.8f, 2.6f, 5.4f) * k, 5, 2);
    }

    static void StonePine(MeshData m, Vector3 p, float h)
    {
        m.Hex(0x6b4a32);
        m.Beam(p, p + new Vector3(0, h * 0.8f, 0), 0.6f, 0.6f);
        m.Hex(0x35512e);
        m.Blob(p + new Vector3(0, h * 0.88f, 0), new Vector3(h * 0.38f, h * 0.14f, h * 0.38f), 6, 2);
    }

    static void Windmill(MeshData m, Vector3 p, float yaw)
    {
        m.Hex(0xf3f1ea);
        m.Column(p - new Vector3(0, 1, 0), 3.9f, 3.2f, 10, 8);
        m.Hex(0x6b4a32);
        m.Column(p + new Vector3(0, 9, 0), 3.5f, 0, 3.4f, 8);
        var face = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
        var side = new Vector3(face.Z, 0, -face.X);
        var hub = p + new Vector3(0, 9.6f, 0) + face * 3.6f;
        for (int i = 0; i < 4; i++)
        {
            float a = 0.4f + i * Mathf.Pi / 2;
            var dir = Vector3.Up * Mathf.Cos(a) + side * Mathf.Sin(a);
            m.Hex(0x5a4030);
            m.Beam(hub, hub + dir * 8, 0.25f, 0.25f);
            // A triangular cloth sail on each arm.
            m.Hex(0xeee8da);
            var turn = Vector3.Up * Mathf.Cos(a + 0.5f) + side * Mathf.Sin(a + 0.5f);
            m.Tri(hub + dir * 1.5f, hub + dir * 7.8f, hub + turn * 4.6f, Vector2.Zero, Vector2.Zero, Vector2.Zero);
            m.Tri(hub + dir * 7.8f, hub + dir * 1.5f, hub + turn * 4.6f, Vector2.Zero, Vector2.Zero, Vector2.Zero);
        }
    }

    /// <summary>A whitewashed hill town in the Alentejo: houses in rings up the hill to the
    /// castle, a white church with yellow trim, windmills; cork oaks, olive groves and vines on
    /// the golden hills, stone pines along the road. The municipal ground sits below the town
    /// with a dirt car park and the road winding up.</summary>
    static void Alentejo(MeshData m, Random rng, uint home)
    {
        const float Road = 7, NatRd = 140;
        var church = new Vector3(Hilltop.X - 70, 0, Hilltop.Y + 120);
        church.Y = AlenH(church.X, church.Z);
        Concourse(m, 0xcdbfa3, Apron - 6);
        Collar(m, 0xbfa868, Apron - 8);
        // The car park of beaten earth by the main road, cars left every which way.
        m.Hex(0xb89b6b);
        Flat(m, -150, Kit.BZ + Apron - 8, 150, NatRd - 6, 0.09f);
        for (int i = 0; i < 110; i++)
        {
            var p = new Vector3(-145 + (float)rng.NextDouble() * 290, 0, Kit.BZ + Apron - 4 + (float)rng.NextDouble() * (NatRd - Kit.BZ - Apron - 6));
            Car(m, p, (float)rng.NextDouble() * 0.6f - 0.3f + (rng.Next(2) == 0 ? 0 : Mathf.Pi / 2), rng);
        }

        uint[] gold = { 0xc8a865, 0xbfa05c, 0xcfb06e };
        Terrain(m, 40, 1680, AlenH, c =>
        {
            if (Inside(c, Apron - 6)) return 0;
            var p = c.Mid;
            if (TownDist(p.X, p.Y) < TownR) return 0xcbbd9e;
            if (Off(p.X, p.Y) < Apron + 30) return 0xbfa868;
            float nz = Noise(c.X0 * 0.012f + 3, c.Z0 * 0.012f + 1);
            return nz > 0.62f ? 0x8a8a5au : nz < 0.28f ? 0x7f8a45u : nz < 0.36f ? 0x9a6a44u : gold[(int)(Hash(c.X0, c.Z0) * 3)];
        }, 0.05f, c =>
        {
            var p = c.Mid;
            if (TownDist(p.X, p.Y) < TownR + 10 || Off(p.X, p.Y) < Apron + 30 || p.Length() > 950 || Mathf.Abs(p.Y - NatRd) < 26) return;
            float nz = Noise(c.X0 * 0.012f + 3, c.Z0 * 0.012f + 1);
            if (nz > 0.62f)
            {
                // An olive grove in rows.
                for (int u = 0; u < 2; u++)
                    for (int v = 0; v < 2; v++)
                    {
                        var t = c.At(0.25f + u * 0.5f, 0.25f + v * 0.5f);
                        m.Hex(0x6f7a4a);
                        m.Blob(t + new Vector3(0, 2.2f, 0), new Vector3(2.6f, 2.2f, 2.6f), 4, 2);
                    }
            }
            else if (nz < 0.28f)
            {
                // Vines: low green rows.
                m.Hex(0x4f6a2e);
                for (int k = 0; k < 4; k++)
                {
                    var a = c.At(0.05f, 0.12f + k * 0.25f);
                    var b = c.At(0.95f, 0.12f + k * 0.25f);
                    m.Box(new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(b.X - a.X, b.Z - a.Z)), (a + b) / 2 + new Vector3(0, 0.6f, 0)), new Vector3(1.1f, 1.3f, (b - a).Length()), 1 | 2 | 4);
                }
            }
            else if (nz >= 0.36f && rng.NextDouble() < 0.7)
                // The montado: cork oaks scattered on the dry grass.
                CorkOak(m, c.At((float)rng.NextDouble(), (float)rng.NextDouble()), 0.85f + 0.4f * (float)rng.NextDouble());
        });

        // The main road across the near side, stone pines along it.
        m.Hex(0x4d4b47, Look.Road, 9);
        Strip(m, new Vector3(-1400, 0.05f, NatRd), new Vector3(1400, 0.05f, NatRd), 9);
        for (float x = -900; x < 900; x += 26)
            foreach (float k in new[] { -1f, 1 })
                if (Hash(x, k) < 0.7f) StonePine(m, new Vector3(x + 9 * Hash(k, x), 0, NatRd + k * 11), 15 + 5 * Hash(x + 1, k));

        // The road up to the town, between low white walls.
        var way = new List<Vector2> { new(-40, -(Kit.BZ + Apron - 8)), new(-70, -180), new(-40, -250), new(-20, -290), church2(church) };
        static Vector2 church2(Vector3 c) => new(c.X + 30, c.Z + 28);
        for (int i = 0; i + 1 < way.Count; i++)
        {
            int n = Mathf.CeilToInt((way[i + 1] - way[i]).Length() / 14);
            for (int k = 0; k < n; k++)
            {
                var a = way[i].Lerp(way[i + 1], k / (float)n);
                var b = way[i].Lerp(way[i + 1], (k + 1) / (float)n);
                var pa = new Vector3(a.X, AlenH(a.X, a.Y) + 0.3f, a.Y);
                var pb = new Vector3(b.X, AlenH(b.X, b.Y) + 0.3f, b.Y);
                m.Hex(0x4d4b47);
                Strip(m, pa, pb, Road);
                var d = pb - pa;
                var side = new Vector3(-d.Z, 0, d.X).Normalized() * (Road / 2 + 0.6f);
                m.Hex(0xf3f1ea);
                foreach (float s in new[] { -1f, 1 })
                    m.Box(new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(d.X, d.Z)), (pa + pb) / 2 + side * s + new Vector3(0, 0.4f, 0)), new Vector3(0.6f, 1, d.Length()), 1 | 2 | 4);
            }
        }

        // The town: houses packed up the hill along its contours, a few lanes climbing to the
        // castle, the edge ragged where the town gives way to the fields.
        uint[] bands = { 0xe0b030, 0xe0b030, 0x2f6fb8, 0x2f6fb8, 0x8a8f95 };
        const float Lot = 12.5f;
        for (float x = Hilltop.X - TownR; x < Hilltop.X + TownR; x += Lot)
            for (float z = Hilltop.Y - TownR; z < Hilltop.Y + TownR; z += Lot)
            {
                var p2 = new Vector2(x + (Hash(x, z) - 0.5f) * 3, z + (Hash(z, x) - 0.5f) * 3);
                float d = TownDist(p2.X, p2.Y);
                if (d < 50 || d > TownR - 6 - 46 * Noise(x * 0.012f + 4, z * 0.012f)) continue;
                float ang = Mathf.Atan2(p2.Y - Hilltop.Y, p2.X - Hilltop.X);
                // Lanes: seven climbing to the top, and a ring lane every 40 m or so.
                float spoke = Mathf.Abs(Mathf.PosMod(ang / (Mathf.Tau / 7) + 0.3f, 1) - 0.5f) * Mathf.Tau / 7 * d;
                if (spoke < 4.5f || Mathf.Abs(Mathf.PosMod(d + 8 * Hash(ang * 3, 1), 42) - 21) < 3.5f) continue;
                if (p2.DistanceTo(new Vector2(church.X, church.Z)) < 30 || Hash(p2.X, p2.Y) < 0.05f) continue;
                float y = AlenH(p2.X, p2.Y);
                Casa(m, new Vector3(p2.X, y - 0.9f, p2.Y), -ang + Mathf.Pi / 2 + (Hash(x + 2, z) - 0.5f) * 0.2f,
                    new Vector3(9.5f + 2 * Hash(z, x + 1), 4.6f + 3 * Hash(p2.Y, p2.X) + (d < 100 ? 2 : 0), 10), bands[(int)(Hash(x + 3, z) * bands.Length)], Hash(p2.X + 1, p2.Y) < 0.3f);
            }

        // The church: a white front with yellow pilasters, a bell tower, a red roof; a cypress or two.
        {
            var c = church;
            float yaw = Mathf.Atan2(-(Hilltop.X - c.X), -(Hilltop.Y - c.Z));
            var b = new Basis(Vector3.Up, yaw);
            m.Hex(0xf6f4ee);
            Gable(m, c - new Vector3(0, 1, 0), yaw + Mathf.Pi / 2, new Vector3(28, 13, 13), 4.5f, 0xb5582f, 0.4f);
            var front = c + b * new Vector3(0, 0, 14);
            m.Hex(0xf6f4ee);
            m.Box(new Transform3D(b, front + new Vector3(0, 8, 0)), new Vector3(16, 18, 1.4f), 63);
            m.Hex(0xe0b030);
            foreach (float k in new[] { -7.6f, 7.6f, 0 })
                m.Box(new Transform3D(b, front + b * new Vector3(k, 0, 0.5f) + new Vector3(0, k == 0 ? 17.4f : 8, 0)), k == 0 ? new Vector3(16.4f, 0.8f, 1.6f) : new Vector3(1, 18, 1.6f), 63);
            var t = c + b * new Vector3(9, 0, 9);
            m.Hex(0xf6f4ee);
            m.Box(new Transform3D(b, t + new Vector3(0, 13, 0)), new Vector3(6, 28, 6), 1 | 2 | 4 | 16 | 32);
            m.Hex(0x2b2a29);
            m.Box(new Transform3D(b, t + new Vector3(0, 23.5f, 0)), new Vector3(6.1f, 3, 2.4f), 63);
            m.Box(new Transform3D(b, t + new Vector3(0, 23.5f, 0)), new Vector3(2.4f, 3, 6.1f), 63);
            m.Hex(0xe0b030);
            m.Column(t + new Vector3(0, 27, 0), 3.4f, 0, 5, 4, Mathf.Pi / 4 - yaw);
            m.Hex(0xd9d1bf);
            Flat(m, front.X - 18, front.Z - 4, front.X + 18, front.Z + 26, front.Y + 0.15f);
            foreach (float k in new[] { -14f, 14 }) Pine(m, front + b * new Vector3(k, 0, 6), 15, 0, 0x2f4a2c);
        }

        // The castle on the hilltop: a curtain wall with square towers, the keep, the club's flag.
        {
            float top = AlenH(Hilltop.X, Hilltop.Y);
            var c = new Vector3(Hilltop.X, top, Hilltop.Y);
            const uint Stone = 0xcdc2a6;
            const int N = 8;
            for (int i = 0; i < N; i++)
            {
                float a0 = Mathf.Tau * i / N, a1 = Mathf.Tau * (i + 1) / N;
                var p0 = c + new Vector3(Mathf.Cos(a0) * 40, -3, Mathf.Sin(a0) * 34);
                var p1 = c + new Vector3(Mathf.Cos(a1) * 40, -3, Mathf.Sin(a1) * 34);
                var along = p1 - p0;
                var b = new Basis(Vector3.Up, Mathf.Atan2(along.X, along.Z));
                m.Hex(Stone, Look.Brick);
                m.Box(new Transform3D(b, (p0 + p1) / 2 + new Vector3(0, 6, 0)), new Vector3(2.6f, 12, along.Length()), 63);
                for (int k = 0; k < 5; k++)
                    m.Box(new Transform3D(b, (p0 + p1) / 2 + new Vector3(0, 12.6f, 0) + along.Normalized() * (k - 2) * along.Length() / 5.5f), new Vector3(2.8f, 1.2f, 1.4f), 1 | 2 | 4 | 16 | 32);
                m.Box(new Transform3D(new Basis(Vector3.Up, a0), p0 + new Vector3(0, 8, 0)), new Vector3(7, 16, 7), 1 | 2 | 4 | 16 | 32);
            }
            m.Hex(Stone, Look.Brick);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(6, 12, -4)), new Vector3(14, 30, 14), 1 | 2 | 4 | 16 | 32);
            for (int k = 0; k < 12; k++)
            {
                float u = (k % 3 - 1) * 5;
                var e = (k / 3) switch { 0 => new Vector3(u, 0, -7), 1 => new Vector3(u, 0, 7), 2 => new Vector3(-7, 0, u), _ => new Vector3(7, 0, u) };
                m.Box(new Transform3D(Basis.Identity, c + new Vector3(6, 27.6f, -4) + e), new Vector3(2, 1.6f, 2), 1 | 2 | 4 | 16 | 32);
            }
            m.Hex(0x3a3d42);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(6, 32, -4)), new Vector3(0.3f, 9, 0.3f), 63);
            m.Hex(home);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(8.4f, 35, -4)), new Vector3(4.6f, 2.8f, 0.15f), 63);
        }

        // Windmills on the hills either side, farmsteads (montes) out in the country.
        foreach (var (x, z, yaw) in new[] { (-430f, -360f, 0.4f), (-330f, -420f, 0.2f), (520f, -330f, -0.5f) })
            Windmill(m, new Vector3(x, AlenH(x, z), z), yaw);
        foreach (var f in new[] { new Vector2(-620, -120), new Vector2(560, 60), new Vector2(-480, 360), new Vector2(380, 420), new Vector2(760, -520), new Vector2(-820, -620) })
        {
            var p = new Vector3(f.X, AlenH(f.X, f.Y), f.Y);
            Casa(m, p - new Vector3(0, 0.6f, 0), 0.3f, new Vector3(34, 5, 9), 0x2f6fb8, true);
            Casa(m, p + new Vector3(14, -0.6f, 14), 0.3f + Mathf.Pi / 2, new Vector3(16, 4.4f, 8), 0xe0b030, false);
            for (int k = 0; k < 3; k++) CorkOak(m, p + new Vector3(-20 + k * 9, 0, 22), 1.1f);
        }

        Edge(m, rng, home, Apron - 9, new uint[] { 0x4f6a35, 0x5a7238 }, p => p.Z > Kit.BZ, 90);
        FarHills(m, 1150, 90);
    }

    // ---------------------------------------------------------------- the fjord

    const float FjordW = 150;
    static float FjordZ(float x) => -370 + 46 * Mathf.Sin(x / 380 + 1.1f);

    static float FjH(float x, float z)
    {
        float dz = z - FjordZ(x);
        float n = Noise(x * 0.003f + 2, z * 0.003f + 8);
        // Sheer walls straight up out of the water on the far side; the delta the ground stands
        // on is flat, closed in by mountains behind and to either side.
        float far = Kit.Smooth((-dz - FjordW + 10) / 190) * (360 + 300 * n);
        float near = Mathf.Max(Kit.Smooth((Mathf.Abs(x) - 520) / 300), Kit.Smooth((z - 260) / 300)) * (300 + 260 * n) * Kit.Smooth((dz - FjordW + 10) / 40);
        return Mathf.Max(far, near);
    }

    static bool Fjord(float x, float z) => Mathf.Abs(z - FjordZ(x)) < FjordW;

    /// <summary>A fjord: dark water just past the main stand under sheer walls of rock with
    /// waterfalls down them and snow on top, a village of red, ochre and white wooden houses
    /// along the shore with its church and quay, boats and a ferry, pine forest up the slopes.</summary>
    static void FjordArea(MeshData m, Random rng, uint home)
    {
        float Shore(float x) => FjordZ(x) + FjordW;
        Concourse(m, 0x9a9c9c, Apron - 6);
        Collar(m, 0x4f7a38, Apron - 8);
        uint[] pines = { 0x24402a, 0x2b4a2e, 0x223a26 };
        Terrain(m, 40, 1700, FjH, c =>
        {
            if (Inside(c, Apron - 6)) return 0;
            var p = c.Mid;
            if (Fjord(p.X, p.Y) && c.Avg < 2) return 0;
            float snow = 430 + 90 * Noise(c.X0 * 0.01f, c.Z0 * 0.01f);
            float nz = Noise(c.X0 * 0.03f + 2, c.Z0 * 0.03f);
            if (c.Avg > snow) return c.Slope > 1.5f ? 0xb4b9bfu : 0xeef1f4u;
            if (c.Slope > 1.05f) return nz > 0.5f ? 0x5f5c58u : 0x6c6862u;
            if (c.Avg > 330) return nz > 0.5f ? 0x6d6e5au : 0x7a7a68u;
            if (c.Avg > 12) return nz > 0.45f ? 0x2b4a2eu : 0x33553au;
            return nz > 0.5f ? 0x4f7a38u : 0x5a823eu;
        }, -0.1f, c =>
        {
            var p = c.Mid;
            if (c.Avg < 12)
            {
                // Out on the delta: birches here and there, hay drying on racks, a red barn.
                if (Fjord(p.X, p.Y) || Off(p.X, p.Y) < Apron + 20 || p.Y < FjordZ(p.X) + FjordW + 90) return;
                float h = Hash(c.X0, c.Z0);
                if (h < 0.35f)
                    for (int k = 0; k < 3; k++) Tree(m, c.At((float)rng.NextDouble(), (float)rng.NextDouble()), 9 + 4 * (float)rng.NextDouble(), Leaves[rng.Next(Leaves.Length)]);
                else if (h < 0.42f)
                {
                    m.Hex(0x9e2b25);
                    Gable(m, c.At(0.5f, 0.5f) - new Vector3(0, 0.3f, 0), h * 9, new Vector3(18, 6, 10), 5, 0x2f3236, 0.4f);
                }
                else if (h < 0.55f)
                {
                    m.Hex(0xa8935a);
                    var a = c.At(0.2f, 0.3f);
                    m.Box(new Transform3D(Basis.Identity, a + new Vector3(10, 0.9f, 0)), new Vector3(20, 1.8f, 0.8f), 1 | 2 | 4 | 16 | 32);
                    m.Box(new Transform3D(Basis.Identity, a + new Vector3(10, 0.9f, 12)), new Vector3(20, 1.8f, 0.8f), 1 | 2 | 4 | 16 | 32);
                }
                return;
            }
            if (c.Avg > 330 || c.Slope > 1.05f || c.Mid.Length() > 1400) return;
            for (int k = 0; k < 3; k++)
                Pine(m, c.At((float)rng.NextDouble(), (float)rng.NextDouble()), 12 + (float)rng.NextDouble() * 6, (float)rng.NextDouble(), pines[rng.Next(pines.Length)]);
        });

        // The water, a pebble beach along the near shore.
        for (float x = -1700; x < 1700; x += 40)
        {
            float z0 = FjordZ(x), z1 = FjordZ(x + 40);
            m.Hex(0x183c4a, Look.Water);
            m.QuadUV(new(x, 0.09f, z0 - FjordW - 30), new(x + 40, 0.09f, z1 - FjordW - 30), new(x + 40, 0.09f, z1 + FjordW), new(x, 0.09f, z0 + FjordW), new(0, 0), new(1, 0), new(1, 1), new(0, 1));
            m.Hex(0x8f877a);
            m.QuadUV(new(x, 0.07f, z0 + FjordW), new(x + 40, 0.07f, z1 + FjordW), new(x + 40, 0.07f, z1 + FjordW + 8), new(x, 0.07f, z0 + FjordW + 8), new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }

        // The shore road, the village along it: steep-roofed wooden houses in red, ochre and white.
        for (float x = -1100; x < 1100; x += 30)
        {
            m.Hex(0x3a3c40, Look.Road, 7);
            Strip(m, new Vector3(x, 0.05f, Shore(x) + 16), new Vector3(x + 30, 0.05f, Shore(x + 30) + 16), 7);
        }
        uint[] woods = { 0x9e2b25, 0x9e2b25, 0xd29a3a, 0xe9e6dc, 0xe9e6dc, 0x6e7c5a, 0xc9b27a };
        for (float x = -760; x < 760; x += 13)
            for (int row = 0; row < 8; row++)
            {
                float z = Shore(x) + 28 + row * 17 + 4 * Hash(x, row);
                if (Off(x, z) < Apron + 12 || Hash(x + 3, row) < 0.2f + row * 0.09f || FjH(x, z) > 8) continue;
                float yaw = Mathf.Atan2(1, (Shore(x + 1) - Shore(x))) + (Hash(row, x) < 0.3f ? Mathf.Pi / 2 : 0);
                m.Hex(woods[(int)(Hash(x, row + 7) * woods.Length)], Look.House, 2);
                Gable(m, new Vector3(x, FjH(x, z) - 0.4f, z), yaw, new Vector3(9, 5.5f, 7), 4.4f, Hash(x, row + 2) < 0.3f ? 0x55703au : 0x2f3236u, 0.4f);
            }
        // Boathouses on the water's edge, a quay, boats and the ferry.
        for (float x = -700; x < 700; x += 37)
        {
            if (Hash(x, 5) < 0.4f) continue;
            m.Hex(0x9e2b25);
            Gable(m, new Vector3(x, 0, Shore(x) - 4), Mathf.Atan2(1, Shore(x + 1) - Shore(x)) + Mathf.Pi / 2, new Vector3(12, 4, 7), 3.4f, 0x2f3236, 0.3f);
        }
        {
            float qx = 140, qz = Shore(qx);
            m.Hex(0x77736b);
            m.Box(new Transform3D(Basis.Identity, new Vector3(qx, 0.6f, qz - 22)), new Vector3(16, 1.2f, 44), 63);
            var fc = new Vector3(qx + 28, 0, qz - 40);
            m.Hex(0xe9e6de);
            m.Box(new Transform3D(Basis.Identity, fc + new Vector3(0, 2.6f, 0)), new Vector3(16, 5, 70), 63);
            m.Hex(0x1f2c5c);
            m.Box(new Transform3D(Basis.Identity, fc + new Vector3(0, 0.9f, 0)), new Vector3(16.2f, 1.8f, 70.2f), 1 | 2 | 16 | 32);
            m.Hex(0xe9e6de);
            m.Box(new Transform3D(Basis.Identity, fc + new Vector3(0, 7.5f, 8)), new Vector3(12, 5, 22), 63);
            m.Hex(0xb8322a);
            m.Column(fc + new Vector3(0, 10, 12), 1.6f, 1.6f, 4, 8);
        }
        for (int i = 0; i < 9; i++)
        {
            float x = -600 + (float)rng.NextDouble() * 1200, z = FjordZ(x) + (float)(rng.NextDouble() - 0.5) * FjordW * 1.5f;
            Boat(m, new Vector3(x, 0, z), (float)rng.NextDouble() * Mathf.Tau, rng.NextDouble() < 0.5, 1, rng);
        }

        // The church: white boards, a steep dark roof, a tower and spire at its west end.
        {
            float x = -260;
            var c = new Vector3(x, 0, Shore(x) + 60);
            c.Y = FjH(c.X, c.Z) - 0.3f;
            m.Hex(0xf1eee6, Look.House, 2);
            Gable(m, c, 0, new Vector3(22, 8, 10), 6, 0x2f3236, 0.5f);
            m.Hex(0xf1eee6);
            m.Box(new Transform3D(Basis.Identity, c + new Vector3(-13, 9, 0)), new Vector3(6, 18, 6), 1 | 2 | 4 | 16 | 32);
            m.Hex(0x2f3236);
            m.Column(c + new Vector3(-13, 18, 0), 4.3f, 0, 15, 4, Mathf.Pi / 4);
        }

        // Waterfalls down the far walls: a bright ribbon from the rim to the water, spray at its foot.
        foreach (float x in new[] { -380f, 90, 420 })
        {
            float z = FjordZ(x) - FjordW + 6;
            var pts = new List<Vector3>();
            for (int k = 0; k < 40 && pts.Count < 30; k++, z -= 9)
            {
                float h = FjH(x, z);
                pts.Add(new Vector3(x, h + 1.2f, z));
                if (h > 300) break;
            }
            m.Hex(0xdde8ee);
            for (int k = 0; k + 1 < pts.Count; k++) Strip(m, pts[k], pts[k + 1], 7 - k * 0.12f);
            m.Hex(0xe6eef2);
            m.Blob(pts[0] + new Vector3(0, 1, 2), new Vector3(9, 4, 7), 6, 2, half: true);
        }

        Edge(m, rng, home, Apron - 9, new uint[] { 0x4c7a38, 0x58843c, 0x6a8a3a }, null, 80);
    }

    // ---------------------------------------------------------------- the tropical city

    static float SeaX(float z) => -430 + 46 * Mathf.Sin(z / 300 + 0.4f);

    static float Dome(float x, float z, float cx, float cz, float r, float h)
    {
        float d = (Sq(x - cx) + Sq(z - cz)) / (r * r);
        return d < 1 ? h * Mathf.Pow(1 - d, 0.42f) : 0;
    }

    static float Domes(float x, float z) => Mathf.Max(Mathf.Max(Dome(x, z, -650, -560, 120, 300), Dome(x, z, -330, -860, 170, 380)), Dome(x, z, -560, -230, 70, 110));

    static float TrH(float x, float z)
    {
        float n = Noise(x * 0.004f + 5, z * 0.004f + 1);
        float hills = Kit.Smooth((-z - 170) / 260) * (120 + 200 * n) * Kit.Smooth((x - SeaX(z) - 60) / 200)
            + Kit.Smooth((x - 480) / 280) * (90 + 160 * n);
        return Mathf.Max(hills, Domes(x, z));
    }

    static bool Morro(float x, float z, float h, float slope) =>
        z < -140 && Mathf.Abs(x) < 560 && h > 3 && h < 190 && slope < 1.7f && Domes(x, z) < 2 && x > SeaX(z) + 70;

    /// <summary>A tropical city between the hills and the sea: the ground in the flat part of
    /// town, the hills behind the main stand covered in small houses in every colour climbing
    /// up to the forest, granite domes rising out of the bay with a cable car to the biggest, a
    /// long beach with palms along its avenue.</summary>
    static void Tropical(MeshData m, Random rng, uint home)
    {
        Concourse(m, 0xc9c2b4, Apron - 4);
        Collar(m, 0x8a8a84, Apron - 6);
        uint[] paint = { 0xe74c3c, 0xf1c40f, 0x3498db, 0x2ecc71, 0xe67e22, 0xf3eee2, 0x9b59b6, 0xf39c9c, 0xd9c7a0, 0x48c9b0, 0xf5b7b1, 0xc0392b };
        Terrain(m, 40, 1680, TrH, c =>
        {
            if (Inside(c, Apron - 4)) return 0;
            var p = c.Mid;
            if (p.X < SeaX(p.Y) && c.Avg < 1) return 0;
            float nz = Noise(c.X0 * 0.03f + 2, c.Z0 * 0.03f);
            if (Domes(p.X, p.Y) > 3 && Domes(p.X, p.Y) >= TrH(p.X, p.Y) - 1)
                return c.Slope > 0.7f || c.Avg > 60 ? (nz > 0.5f ? 0x8c8780u : 0x7d786fu) : 0x3a7a3au;
            if (Morro(p.X, p.Y, c.Avg, c.Slope)) return 0x9a7a5a;
            if (c.Avg > 3) return nz > 0.5f ? 0x2f6a34u : 0x3a7a3au;
            return 0x8a8a84;
        }, 0.05f, c =>
        {
            var p = c.Mid;
            if (Morro(p.X, p.Y, c.Avg, c.Slope))
            {
                // The houses: little boxes in every colour, crowded up the hill, water tanks on the roofs.
                for (int k = 0; k < 16; k++)
                {
                    float u = (k % 4 + 0.15f + 0.7f * (float)rng.NextDouble()) / 4, v = (k / 4 + 0.15f + 0.7f * (float)rng.NextDouble()) / 4;
                    if (rng.NextDouble() < 0.32) continue;
                    var a = c.At(u, v);
                    float w = 6 + 3 * (float)rng.NextDouble(), d = 6 + 3 * (float)rng.NextDouble(), h = 3 + (float)rng.NextDouble() * 6;
                    float yaw = (float)rng.NextDouble() * 0.3f;
                    // Dug into the slope: the downhill side stands on a storey of its own.
                    float sink = 1.5f + c.Slope * 0.6f * Mathf.Max(w, d);
                    m.Hex(paint[rng.Next(paint.Length)]);
                    m.Box(new Transform3D(new Basis(Vector3.Up, yaw), a + new Vector3(0, (h - sink) / 2, 0)), new Vector3(w, h + sink, d), 1 | 2 | 4 | 16 | 32);
                    if (rng.NextDouble() < 0.22)
                    {
                        m.Hex(0x2f5fb8);
                        m.Box(new Transform3D(Basis.Identity, a + new Vector3(w * 0.2f, h + 0.6f, 0)), new Vector3(1.6f, 1.2f, 1.6f), 1 | 2 | 4 | 16 | 32);
                    }
                }
                return;
            }
            if (c.Avg > 3 && c.Avg < 260 && c.Slope < 1 && p.Length() < 900 && Domes(p.X, p.Y) < 2)
                {
                    var t = c.At((float)rng.NextDouble(), (float)rng.NextDouble());
                    m.Hex(rng.NextDouble() < 0.5 ? 0x2a5f2eu : 0x357036u);
                    m.Blob(t + new Vector3(0, 6, 0), new Vector3(8, 7, 8), 5, 2);
                }
        });

        // The bay: the sea out west, a long beach, the avenue and its palms.
        for (float z = -1700; z < 1700; z += 40)
        {
            float x0 = SeaX(z), x1 = SeaX(z + 40);
            m.Hex(0x1f6a86, Look.Water);
            m.QuadUV(new(-1700, 0.09f, z), new(-1700, 0.09f, z + 40), new(x1, 0.09f, z + 40), new(x0, 0.09f, z), new(0, 0), new(1, 0), new(1, 1), new(0, 1));
            m.Hex(0xe8d8a8);
            m.QuadUV(new(x0, 0.06f, z), new(x1, 0.06f, z + 40), new(x1 + 60, 0.06f, z + 40), new(x0 + 60, 0.06f, z), new(0, 0), new(1, 0), new(1, 1), new(0, 1));
            m.Hex(0xece6da);
            m.QuadUV(new(x0 + 60, 0.07f, z), new(x1 + 60, 0.07f, z + 40), new(x1 + 70, 0.07f, z + 40), new(x0 + 70, 0.07f, z), new(0, 0), new(1, 0), new(1, 1), new(0, 1));
            m.Hex(0x34363a, Look.Road, 14);
            m.QuadUV(new(x0 + 70, 0.065f, z), new(x1 + 70, 0.065f, z + 40), new(x1 + 84, 0.065f, z + 40), new(x0 + 84, 0.065f, z), new(0, 0), new(40, 0), new(40, 14), new(0, 14));
        }
        for (float z = -900; z < 900; z += 15)
        {
            if (TrH(SeaX(z) + 64, z) > 2) continue;
            Palm(m, new Vector3(SeaX(z) + 64, 0, z), 8 + 4 * Hash(z, 1), Hash(1, z) * Mathf.Tau);
            if (Hash(z, 3) < 0.2f)
            {
                m.Hex(0xd35400);
                m.Blob(new Vector3(SeaX(z) + 30, 2.4f, z + 5), new Vector3(1.6f, 0.4f, 1.6f), 6, 1);
                m.Hex(0xe9e4d8);
                m.Beam(new Vector3(SeaX(z) + 30, 0, z + 5), new Vector3(SeaX(z) + 30, 2.4f, z + 5), 0.1f, 0.1f);
            }
        }
        for (int i = 0; i < 10; i++)
            Boat(m, new Vector3(SeaX(0) - 120 - (float)rng.NextDouble() * 700, 0, -600 + (float)rng.NextDouble() * 1100), (float)rng.NextDouble() * Mathf.Tau, rng.NextDouble() < 0.6, 1, rng);

        // The flat part of town round the ground: blocks in pastels, palms along the streets.
        const float C = 40;
        uint[] pastel = { 0xf3eee2, 0xf5d6a0, 0xa8d8c8, 0xf2b8a0, 0xe0e0d8, 0xc8d8e8, 0xf0e0a0, 0xd9c7a0 };
        for (int i = -16; i <= 16; i++)
            for (int j = -16; j <= 16; j++)
            {
                float cx = i * C + 20, cz = j * C + 20;
                if (Off(cx, cz) < Apron + 34 || cx < SeaX(cz) + 100 || TrH(cx, cz) > 2.5f || new Vector2(cx, cz).Length() > 640) continue;
                m.Hex(0x3a3c40);
                Flat(m, cx - C / 2, cz - C / 2, cx + C / 2, cz + C / 2, 0.08f);
                if (Hash(i, j) < 0.12f)
                {
                    m.Hex(0x5f8a3e);
                    Flat(m, cx - 15, cz - 15, cx + 15, cz + 15, 0.09f);
                    for (int k = 0; k < 4; k++) Palm(m, new Vector3(cx - 9 + k % 2 * 18, 0, cz - 9 + k / 2 * 18), 9, k);
                    continue;
                }
                float r = new Vector2(cx, cz).Length();
                float h = 9 + Hash(i + 2, j) * 14 + Mathf.Clamp((r - 200) / 300, 0, 1) * 30 * Hash(j, i + 1);
                Building(m, new Vector3(cx, 0, cz), new Vector3(30, h, 30), pastel[(int)(Hash(i + 7, j) * pastel.Length)], (int)(Hash(i, j + 5) * 3), Hash(i - 3, j) < 0.25f);
                Palm(m, new Vector3(cx - 17, 0, cz - 17), 8, i + j);
            }

        // The cable car out to the big dome in the bay.
        {
            var a = new Vector3(-560, Domes(-560, -230) + 2, -230);
            var b = new Vector3(-650, Domes(-650, -560) + 2, -560);
            m.Hex(0xb9b4aa);
            m.Box(new Transform3D(Basis.Identity, a + new Vector3(0, 3, 0)), new Vector3(14, 7, 10), 63);
            m.Box(new Transform3D(Basis.Identity, b + new Vector3(0, 3, 0)), new Vector3(14, 7, 10), 63);
            m.Hex(0x3a3d42);
            foreach (float k in new[] { -2.5f, 2.5f }) m.Beam(a + new Vector3(k, 6, 0), b + new Vector3(k, 6, 0), 0.3f, 0.3f);
            foreach (float f in new[] { 0.3f, 0.7f })
            {
                var p = a.Lerp(b, f) + new Vector3(f < 0.5f ? 2.5f : -2.5f, 3, 0);
                m.Hex(0xe6e8ea, Look.Glass);
                m.Box(new Transform3D(Basis.Identity, p), new Vector3(4, 3, 4), 63);
            }
        }

        // Palms round the concourse, lamps between them, the crowd.
        Along(Ring(Apron - 2, 0), 14, 0, p => Palm(m, p, 8 + 3 * Hash(p.X, p.Z), Hash(p.Z, p.X) * Mathf.Tau));
        Edge(m, rng, home, Apron - 2, Leaves, p => true, 130);
    }
}
