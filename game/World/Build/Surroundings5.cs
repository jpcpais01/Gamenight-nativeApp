using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds.Build;

/// <summary>
/// Three special places: a haunted hollow on Halloween night, a base on the Moon, and a snowy
/// Christmas village. Each landmark sits behind the main stand (-z), where the match camera looks.
/// </summary>
static partial class Surroundings
{
    /// <summary>A flat disc facing +z (towards the ground), for the moon and the Earth in the sky.</summary>
    static void Disc(MeshData m, Vector3 c, float r, int n = 24)
    {
        for (int i = 0; i < n; i++)
        {
            float a0 = i * Mathf.Tau / n, a1 = (i + 1) * Mathf.Tau / n;
            m.Tri(c, c + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0) * r, c + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0) * r, Z2, Z2, Z2);
        }
    }

    /// <summary>A quad facing +z, w by h, centred on c (a lit window, a face).</summary>
    static void Pane(MeshData m, Vector3 c, float w, float h) =>
        m.Quad(c + new Vector3(-w / 2, -h / 2, 0), c + new Vector3(w / 2, -h / 2, 0), c + new Vector3(w / 2, h / 2, 0), c + new Vector3(-w / 2, h / 2, 0));

    // ---------------------------------------------------------------- the haunted hollow

    static readonly Vector2 Mansion = new(40, -520);

    static float HauntH(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float hill = 80 * Mathf.Exp(-(Sq((x - Mansion.X) / 230) + Sq((z - Mansion.Y) / 190)));
        return Mathf.Max(hill, Kit.Smooth((r - 300) / 400) * (20 + 70 * Noise(x * 0.004f + 1, z * 0.004f + 7)));
    }

    static void DeadTree(MeshData m, Vector3 p, float h, float turn)
    {
        m.Hex(0x1e1a1c);
        var top = p + new Vector3(0.6f, h, 0.3f);
        m.Beam(p - new Vector3(0, 0.5f, 0), top, 0.9f, 0.9f);
        for (int i = 0; i < 4; i++)
        {
            float a = turn + i * 1.7f;
            var from = p + new Vector3(0, h * (0.45f + 0.14f * i), 0);
            var to = from + new Vector3(Mathf.Cos(a) * h * 0.4f, h * 0.28f, Mathf.Sin(a) * h * 0.4f);
            m.Beam(from, to, 0.45f, 0.45f);
            m.Beam(to, to + new Vector3(Mathf.Cos(a + 0.9f) * h * 0.18f, h * 0.16f, Mathf.Sin(a + 0.9f) * h * 0.18f), 0.25f, 0.25f);
        }
    }

    static void Pumpkin(MeshData m, Vector3 p, float r, bool lit)
    {
        m.Hex(0xe0681a);
        m.Blob(p + new Vector3(0, r * 0.7f, 0), new Vector3(r, r * 0.75f, r), 6, 2);
        if (!lit) return;
        m.Hex(0xffc23a, Look.Unlit);
        var f = p + new Vector3(0, r * 0.75f, r * 0.98f);
        Pane(m, f + new Vector3(-r * 0.38f, r * 0.2f, 0), r * 0.3f, r * 0.26f);
        Pane(m, f + new Vector3(r * 0.38f, r * 0.2f, 0), r * 0.3f, r * 0.26f);
        Pane(m, f + new Vector3(0, -r * 0.25f, 0), r * 0.9f, r * 0.2f);
    }

    /// <summary>A haunted hollow on Halloween night: the old mansion on its hill behind the main
    /// stand with its towers and lit windows, a graveyard on the slope below, pumpkin fields with
    /// jack-o'-lanterns and scarecrows, dead trees, lanterns along the paths, bats, a witch across
    /// a huge full moon.</summary>
    static void Haunted(MeshData m, Random rng, uint home)
    {
        Concourse(m, 0x4a4650, Apron - 6);
        Collar(m, 0x2e3a2a, Apron - 8);
        Terrain(m, 40, 1680, HauntH, c =>
        {
            if (Inside(c, Apron - 6)) return 0;
            float nz = Noise(c.X0 * 0.012f + 4, c.Z0 * 0.012f);
            if (nz > 0.6f && c.Mid.Length() < 700 && c.Slope < 0.4f && Off(c.Mid.X, c.Mid.Y) > Apron + 20) return 0x4a3a2a;
            return nz < 0.3f ? 0x3d3546u : Hash(c.X0, c.Z0) < 0.5f ? 0x2e3a2au : 0x35402eu;
        }, 0.04f, c =>
        {
            var p = c.Mid;
            float nz = Noise(c.X0 * 0.012f + 4, c.Z0 * 0.012f);
            if (Off(p.X, p.Y) < Apron + 20 || p.Length() > 1000) return;
            if (nz > 0.6f && p.Length() < 700 && c.Slope < 0.4f)
            {
                // The pumpkin field: rows of pumpkins, some carved and lit, a scarecrow.
                for (int k = 0; k < 4; k++)
                {
                    var a = c.At(0.1f + 0.8f * (float)rng.NextDouble(), 0.1f + 0.8f * (float)rng.NextDouble());
                    Pumpkin(m, a, 1.2f + 0.8f * (float)rng.NextDouble(), rng.NextDouble() < 0.35);
                }
                if (Hash(c.X0, c.Z0) < 0.25f)
                {
                    var s = c.At(0.5f, 0.5f);
                    m.Hex(0x5a4030);
                    m.Beam(s, s + new Vector3(0, 5, 0), 0.3f, 0.3f);
                    m.Beam(s + new Vector3(-2, 3.6f, 0), s + new Vector3(2, 3.6f, 0), 0.25f, 0.25f);
                    m.Hex(0x6a4a7a);
                    Block(m, s + new Vector3(0, 2.4f, 0), 0, new Vector3(1.4f, 1.8f, 0.6f));
                    m.Hex(0xc8a050);
                    m.Blob(s + new Vector3(0, 4.7f, 0), new Vector3(0.5f, 0.5f, 0.5f), 5, 2);
                    m.Hex(0x1e1a1c);
                    m.Column(s + new Vector3(0, 5.1f, 0), 0.8f, 0, 1.4f, 5);
                }
                return;
            }
            if (Hash(c.Z0, c.X0) < 0.16f) DeadTree(m, c.At((float)rng.NextDouble(), (float)rng.NextDouble()), 9 + 6 * (float)rng.NextDouble(), (float)rng.NextDouble() * 6);
        });

        // The graveyard on the slope below the mansion: headstones, crosses, a crypt, an iron fence.
        var g0 = new Vector2(-150, -400);
        for (int i = 0; i < 160; i++)
        {
            float x = g0.X + (i % 20) * 9 + 2 * Hash(i, 1), z = g0.Y - (i / 20) * 8;
            var p = new Vector3(x, HauntH(x, z) - 0.3f, z);
            float lean = (Hash(i, 2) - 0.5f) * 0.3f;
            m.Hex(Hash(i, 3) < 0.5f ? 0x8a8690u : 0x6f6b74u);
            if (Hash(i, 4) < 0.25f)
            {
                m.Box(new Transform3D(new Basis(Vector3.Forward, lean), p + new Vector3(0, 1.6f, 0)), new Vector3(0.5f, 3.2f, 0.5f), 63);
                m.Box(new Transform3D(new Basis(Vector3.Forward, lean), p + new Vector3(0, 2.3f, 0)), new Vector3(1.8f, 0.5f, 0.5f), 63);
            }
            else
                m.Box(new Transform3D(new Basis(Vector3.Forward, lean), p + new Vector3(0, 1, 0)), new Vector3(1.6f, 2.2f, 0.5f), 63);
        }
        {
            var c = new Vector3(g0.X + 90, 0, g0.Y - 70);
            c.Y = HauntH(c.X, c.Z) - 0.5f;
            m.Hex(0x7a7680);
            Block(m, c, 0, new Vector3(12, 8, 10));
            m.Prism(new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 2), c + new Vector3(0, 8, 0)), new Vector2[] { new(-6, 0), new(6, 0), new(0, 4) }, 13);
            m.Hex(0x1e1a1c);
            Pane(m, c + new Vector3(0, 3, 5.05f), 3, 5);
        }
        m.Hex(0x1e1a1c);
        for (int k = 0; k <= 20; k++)
        {
            float x = g0.X - 6 + k * 9.5f, z = g0.Y + 6;
            m.Beam(new Vector3(x, HauntH(x, z) - 0.2f, z), new Vector3(x, HauntH(x, z) + 2.6f, z), 0.18f, 0.18f);
        }

        // The mansion: a tall dark house, steep roofs, three towers with spires, windows lit.
        {
            float top = HauntH(Mansion.X, Mansion.Y);
            var c = new Vector3(Mansion.X, top - 1, Mansion.Y);
            const float K = 1.6f;
            Vector3 At(float x, float y, float z) => c + new Vector3(x, y, z) * K;
            const uint Wall = 0x3a3442, Roof = 0x15121a;
            m.Hex(Wall);
            Gable(m, c, 0, new Vector3(40, 20, 22) * K, 14 * K, Roof, 0.8f);
            m.Hex(Wall);
            Gable(m, At(-14, 0, 8), Mathf.Pi / 2, new Vector3(18, 26, 14) * K, 12 * K, Roof, 0.6f);
            foreach (var (tx, tz, th) in new[] { (22f, 6f, 40f), (-26f, -6f, 32f), (6f, -10f, 50f) })
            {
                m.Hex(Wall);
                m.Column(At(tx, 0, tz), 5 * K, 5 * K, th * K, 8);
                m.Hex(Roof);
                m.Column(At(tx, th, tz), 6.4f * K, 0, 16 * K, 8);
                m.Hex(0xffb83a, Look.Unlit);
                Pane(m, At(tx, th - 6, tz + 5.05f), 1.6f * K, 3 * K);
            }
            m.Hex(0x2a2430);
            Block(m, At(10, 30, 2), 0.2f, new Vector3(2.4f, 10, 2.4f) * K);
            // Windows: most lit warm, some dark, one green.
            for (int r = 0; r < 3; r++)
                for (int k = 0; k < 7; k++)
                {
                    float h = Hash(r, k);
                    m.Hex(h < 0.15f ? 0x1e1a1cu : h < 0.22f ? 0x6aff6au : 0xffc04au, Look.Unlit);
                    Pane(m, At(-15 + k * 5, 4 + r * 6, 11.05f), 1.8f * K, 3 * K);
                }
            for (int r = 0; r < 4; r++)
            {
                m.Hex(0xffc04a, Look.Unlit);
                Pane(m, At(-14, 4 + r * 5.6f, 17.05f), 1.8f * K, 3 * K);
            }
            // The path up, lanterns along it.
            for (int k = 0; k < 14; k++)
            {
                float z = Mansion.Y + 40 + k * 14, x = Mansion.X - 10 + 12 * Mathf.Sin(k * 0.7f);
                var p = new Vector3(x, HauntH(x, z), z);
                m.Hex(0x1e1a1c);
                m.Beam(p, p + new Vector3(0, 3.4f, 0), 0.2f, 0.2f);
                m.Hex(0xff8a2a, Look.Unlit);
                Block(m, p + new Vector3(0, 3.4f, 0), 0, new Vector3(0.8f, 1, 0.8f));
            }
        }

        // The full moon, huge and low, a witch flying across it, bats.
        {
            var moon = new Vector3(-280, 300, -980);
            m.Hex(0xfaf0cc, Look.Unlit);
            Disc(m, moon, 120, 32);
            m.Hex(0xe0d4a8, Look.Unlit);
            foreach (var (x, y, r) in new[] { (-40f, 30f, 22f), (30f, -20f, 16f), (50f, 45f, 12f), (-20f, -55f, 14f) })
                Disc(m, moon + new Vector3(x, y, 0.5f), r, 12);
            m.Hex(0x0d0a10, Look.Unlit);
            var w = moon + new Vector3(30, -10, 8);
            m.Beam(w + new Vector3(-26, -6, 0), w + new Vector3(18, 4, 0), 1.6f, 1.6f);
            m.Tri(w + new Vector3(-24, -8, 0), w + new Vector3(-32, -14, 0), w + new Vector3(-34, -2, 0), Z2, Z2, Z2);
            m.Tri(w + new Vector3(-6, -2, 0), w + new Vector3(6, 0, 0), w + new Vector3(0, 14, 0), Z2, Z2, Z2);
            m.Tri(w + new Vector3(-3, 12, 0), w + new Vector3(3, 12, 0), w + new Vector3(6, 26, 0), Z2, Z2, Z2);
            m.Tri(w + new Vector3(-6, 12, 0), w + new Vector3(8, 12, 0), w + new Vector3(1, 14, 0), Z2, Z2, Z2);
            for (int i = 0; i < 9; i++)
            {
                var b = moon + new Vector3(-180 + i * 45 + 20 * Hash(i, 5), -60 + 120 * Hash(i, 6), 30);
                float s = 5 + 4 * Hash(i, 7);
                m.Tri(b, b + new Vector3(-s * 1.4f, s * 0.6f, 0), b + new Vector3(-s * 0.6f, -s * 0.2f, 0), Z2, Z2, Z2);
                m.Tri(b, b + new Vector3(s * 0.6f, -s * 0.2f, 0), b + new Vector3(s * 1.4f, s * 0.6f, 0), Z2, Z2, Z2);
            }
        }

        Edge(m, rng, home, Apron - 7, new uint[] { 0x2e3a2a }, p => true, 130);
        foreach (var p in Ring(Apron + 4, 0))
            if (Hash(p.X, p.Z) < 0.22f) Pumpkin(m, p, 1.4f, true);
    }

    // ---------------------------------------------------------------- the moon base

    static readonly Vector4[] Craters =
    {
        new(-420, -560, 160, 30), new(380, -720, 220, 40), new(-120, -1100, 300, 50), new(560, 260, 140, 22),
        new(-620, 180, 180, 26), new(120, 640, 120, 18), new(-900, -500, 260, 40), new(900, -300, 200, 30),
        new(260, -380, 60, 10), new(-260, 420, 70, 12),
    };

    static readonly Vector2 Mesa = new(0, -560);

    static float MoonH(float x, float z)
    {
        float h = 6 * Noise(x * 0.01f + 3, z * 0.01f + 1);
        foreach (var c in Craters)
        {
            float d = new Vector2(x - c.X, z - c.Y).Length() / c.Z;
            if (d < 1) h += c.W * (d * d - 1) + c.W * 0.4f;
            h += c.W * 0.4f * Mathf.Exp(-Sq((d - 1) / 0.25f)) * (d < 1 ? 0 : 1);
        }
        float r = Mathf.Sqrt(x * x + z * z);
        h += Kit.Smooth((r - 800) / 600) * 260 * Noise(x * 0.003f + 9, z * 0.003f + 4);
        h *= Kit.Smooth((r - 200) / 160);
        // The mesa the base stands on, behind the main stand.
        float mesa = new Vector2((x - Mesa.X) / 1.4f, z - Mesa.Y).Length();
        return Mathf.Max(h, 60 * Kit.Smooth((260 - mesa) / 70));
    }

    /// <summary>A base on the Moon: grey dust and craters to the mountains, white habitat domes
    /// joined by tubes, a rocket on its launch tower, solar arrays, a dish, rovers, the club's flag,
    /// and the Earth hanging in a black sky.</summary>
    static void MoonBase(MeshData m, Random rng, uint home)
    {
        Concourse(m, 0xb0b4b8, Apron - 6);
        Collar(m, 0x8c8c8a, Apron - 8);
        Terrain(m, 40, 1680, MoonH, c =>
        {
            if (Inside(c, Apron - 6)) return 0;
            float nz = Noise(c.X0 * 0.02f + 5, c.Z0 * 0.02f + 2);
            if (c.Slope > 0.6f) return nz > 0.5f ? 0x6a6a68u : 0x5e5e5cu;
            return nz > 0.6f ? 0x9a9a96u : nz > 0.3f ? 0x8c8c8au : 0x7c7c7au;
        }, 0.04f, c =>
        {
            // Boulders scattered on the dust.
            if (Off(c.Mid.X, c.Mid.Y) < Apron + 20 || Hash(c.X0, c.Z0) > 0.25f || c.Mid.Length() > 1100) return;
            m.Hex(0x6f6f6c);
            m.Blob(c.At((float)rng.NextDouble(), (float)rng.NextDouble()), new Vector3(2.6f, 1.8f, 2.2f) * (0.6f + (float)rng.NextDouble()), 5, 2);
        });

        // The Earth: blue seas, green and brown land, white cloud, hanging over the base.
        {
            var e = new Vector3(240, 300, -1050);
            m.Hex(0x2f6ad0, Look.Unlit);
            Disc(m, e, 95, 32);
            m.Hex(0x4f9a4a, Look.Unlit);
            foreach (var (x, y, r) in new[] { (-30f, 20f, 26f), (-12f, 38f, 16f), (36f, -10f, 22f), (20f, -40f, 14f) })
                Disc(m, e + new Vector3(x, y, 0.5f), r, 10);
            m.Hex(0xf4f6f8, Look.Unlit);
            foreach (var (x, y, r) in new[] { (0f, 80f, 20f), (-50f, -30f, 12f), (55f, 30f, 10f), (-10f, -78f, 16f) })
                Disc(m, e + new Vector3(x, y, 1), r, 10);
        }

        // The habitat: white domes, window bands, tubes between them.
        var domes = new[] { new Vector4(-190, -470, 34, 0), new Vector4(-90, -500, 26, 0), new Vector4(130, -480, 30, 0), new Vector4(-260, -560, 24, 0), new Vector4(230, -560, 24, 0), new Vector4(40, -580, 46, 0) };
        foreach (var d in domes)
        {
            var c = new Vector3(d.X, MoonH(d.X, d.Y) - 1, d.Y);
            m.Hex(0xeef0f2);
            m.Blob(c, new Vector3(d.Z, d.Z * 0.8f, d.Z), 14, 4, true);
            m.Hex(0x9fd8ff, Look.Unlit);
            m.Column(c + new Vector3(0, d.Z * 0.28f, 0), d.Z * 0.96f, d.Z * 0.93f, 1.6f, 14);
            m.Hex(0xc9ccd0);
            Block(m, c + new Vector3(0, 0, d.Z * 0.95f), 0, new Vector3(6, 5, 6));
        }
        m.Hex(0xd8dbe0);
        for (int i = 0; i + 1 < domes.Length; i++)
        {
            var a = new Vector3(domes[i].X, 3, domes[i].Y);
            var b = new Vector3(domes[i + 1].X, 3, domes[i + 1].Y);
            a.Y = MoonH(a.X, a.Z) + 3; b.Y = MoonH(b.X, b.Z) + 3;
            m.Beam(a, b, 4, 4);
        }

        // The rocket on its pad, the launch tower beside it.
        {
            var p = new Vector3(260, 0, -650);
            p.Y = MoonH(p.X, p.Z);
            m.Hex(0x9a9ca0);
            m.Column(p - new Vector3(0, 1, 0), 26, 26, 3, 16);
            m.Hex(0xf4f4f2);
            m.Column(p + new Vector3(0, 6, 0), 6, 6, 70, 12);
            m.Column(p + new Vector3(0, 76, 0), 6, 0, 16, 12);
            m.Hex(0x1e2026);
            m.Column(p + new Vector3(0, 30, 0), 6.1f, 6.1f, 3, 12);
            m.Column(p + new Vector3(0, 62, 0), 6.1f, 6.1f, 3, 12);
            m.Hex(home);
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.Pi / 2;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                m.Tri(p + new Vector3(0, 22, 0) + dir * 5.8f, p + new Vector3(0, 4, 0) + dir * 12, p + new Vector3(0, 6, 0) + dir * 5.8f, Z2, Z2, Z2);
                m.Tri(p + new Vector3(0, 6, 0) + dir * 5.8f, p + new Vector3(0, 4, 0) + dir * 12, p + new Vector3(0, 22, 0) + dir * 5.8f, Z2, Z2, Z2);
            }
            m.Hex(0xb8322a);
            var t = p + new Vector3(-22, 0, 0);
            for (int k = 0; k < 4; k++)
            {
                var o = new Vector3(k % 2 == 0 ? -3 : 3, 0, k < 2 ? -3 : 3);
                m.Beam(t + o, t + o + new Vector3(0, 96, 0), 0.8f, 0.8f);
            }
            for (int y = 8; y < 96; y += 8)
            {
                m.Beam(t + new Vector3(-3, y, -3), t + new Vector3(3, y + 8, -3), 0.5f, 0.5f);
                m.Beam(t + new Vector3(-3, y, 3), t + new Vector3(3, y + 8, 3), 0.5f, 0.5f);
            }
            m.Beam(t + new Vector3(0, 80, 0), p + new Vector3(-6, 80, 0), 1.2f, 1.2f);
            m.Hex(0xff3a2a, Look.Unlit);
            Block(m, t + new Vector3(0, 96, 0), 0, new Vector3(1.2f, 1.2f, 1.2f));
        }

        // Solar arrays in rows, the dish, rovers, the club's flag planted in the dust.
        for (int r = 0; r < 4; r++)
            for (int k = 0; k < 10; k++)
            {
                var p = new Vector3(-330 + k * 16, 0, -620 - r * 18);
                p.Y = MoonH(p.X, p.Z);
                m.Hex(0x8a8c90);
                m.Beam(p, p + new Vector3(0, 3, 0), 0.4f, 0.4f);
                m.Hex(0x1f2f6a);
                m.Quad(p + new Vector3(-6.5f, 2, 3), p + new Vector3(6.5f, 2, 3), p + new Vector3(6.5f, 6, -3), p + new Vector3(-6.5f, 6, -3));
            }
        {
            var p = new Vector3(-290, 0, -440);
            p.Y = MoonH(p.X, p.Z);
            m.Hex(0xd8dbe0);
            m.Column(p, 2, 1.4f, 14, 8);
            m.Column(p + new Vector3(0, 14, 0), 1.5f, 18, 7, 16);
            m.Beam(p + new Vector3(0, 21, 0), p + new Vector3(0, 30, 0), 0.5f, 0.5f);
        }
        for (int i = 0; i < 4; i++)
        {
            var p = new Vector3(-200 + i * 120, 0, -200 - 40 * Hash(i, 2));
            p.Y = MoonH(p.X, p.Z);
            m.Hex(0xe8e8e4);
            Block(m, p + new Vector3(0, 1.4f, 0), i * 0.7f, new Vector3(4, 2, 6));
            m.Hex(0x2a2c30);
            foreach (var (dx, dz) in new[] { (-2.3f, -2f), (2.3f, -2f), (-2.3f, 2f), (2.3f, 2f) })
                m.Box(new Transform3D(new Basis(Vector3.Up, i * 0.7f), p + new Basis(Vector3.Up, i * 0.7f) * new Vector3(dx, 0.8f, dz)), new Vector3(0.8f, 1.6f, 1.6f), 63);
        }
        {
            var p = new Vector3(-90, 0, -200);
            p.Y = MoonH(p.X, p.Z);
            m.Hex(0xd8dbe0);
            m.Beam(p, p + new Vector3(0, 14, 0), 0.3f, 0.3f);
            m.Hex(home);
            Block(m, p + new Vector3(3.6f, 10, 0), 0, new Vector3(7, 4, 0.1f));
        }

        Edge(m, rng, home, Apron - 7, new uint[] { 0x8c8c8a }, p => true, 110);
    }

    // ---------------------------------------------------------------- christmas

    static float SnowH(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        return Kit.Smooth((r - 380) / 450) * (30 + 150 * Noise(x * 0.004f + 6, z * 0.004f + 2)) + Kit.Smooth((-z - 500) / 500) * 60;
    }

    static void SnowPine(MeshData m, Vector3 p, float h, float phase)
    {
        m.Hex(0x234a32);
        m.Column(p - new Vector3(0, 0.5f, 0), h * 0.3f, 0, h, 5, phase);
        m.Hex(0xf4f7fa);
        m.Column(p + new Vector3(0, h * 0.55f, 0), h * 0.15f, 0, h * 0.45f, 5, phase);
    }

    /// <summary>A snowy Christmas village: the great tree on the square behind the main stand
    /// with its baubles and star, the market's wooden stalls strung with lights, chalets with warm
    /// windows and snow on their roofs, the church, a frozen lake with skaters, snowmen, and snowy
    /// pine forest up the hills.</summary>
    static void Christmas(MeshData m, Random rng, uint home)
    {
        Concourse(m, 0xd8dde4, Apron - 6);
        Collar(m, 0xeef2f6, Apron - 8);
        var lake = new Vector2(-380, -260);
        Terrain(m, 40, 1680, SnowH, c =>
        {
            if (Inside(c, Apron - 6)) return 0;
            if (new Vector2(c.Mid.X - lake.X, c.Mid.Y - lake.Y).Length() < 90) return 0;
            if (c.Slope > 0.9f) return 0x8a8e96;
            return Hash(c.X0, c.Z0) < 0.5f ? 0xeef2f6u : 0xe2e8f0u;
        }, 0.04f, c =>
        {
            var p = c.Mid;
            if (c.Avg < 8 || p.Length() > 1100 || c.Slope > 0.9f) return;
            SnowPine(m, c.At((float)rng.NextDouble(), (float)rng.NextDouble()), 15 + 9 * (float)rng.NextDouble(), (float)rng.NextDouble());
        });

        // The frozen lake, skaters on it.
        m.Hex(0xbfe0ee);
        for (int i = 0; i < 20; i++)
        {
            float a0 = i * Mathf.Tau / 20, a1 = (i + 1) * Mathf.Tau / 20;
            var c = new Vector3(lake.X, 0.1f, lake.Y);
            m.Tri(c, c + new Vector3(Mathf.Cos(a1) * 100, 0, Mathf.Sin(a1) * 100), c + new Vector3(Mathf.Cos(a0) * 100, 0, Mathf.Sin(a0) * 100), Z2, Z2, Z2);
        }
        uint[] coats = { 0xc0392b, 0x2e6fb8, 0x27ae60, 0xf1c40f, 0x8e44ad, 0xffffff };
        for (int i = 0; i < 30; i++)
        {
            float a = i * 2.4f, r = 20 + 60 * Hash(i, 1);
            m.Hex(coats[i % coats.Length]);
            Block(m, new Vector3(lake.X + Mathf.Cos(a) * r, 0, lake.Y + Mathf.Sin(a) * r), a, new Vector3(0.6f, 1.8f, 0.5f));
        }

        // The square behind the main stand: the great tree, the market round it.
        var sq = new Vector3(-170, 0, -360);
        m.Hex(0xd8dde4);
        Flat(m, sq.X - 90, sq.Z - 70, sq.X + 90, sq.Z + 70, 0.08f);
        {
            m.Hex(0x5a3a26);
            m.Column(sq, 2.5f, 2, 8, 8);
            m.Hex(0x1f5a32);
            for (int t = 0; t < 4; t++)
                m.Column(sq + new Vector3(0, 6 + t * 19, 0), 28 - t * 6, 2, 30 - t * 2, 10, t * 0.3f);
            uint[] baubles = { 0xff3a2a, 0xffd23a, 0x3a8aff, 0xffffff, 0xff6ad5 };
            for (int i = 0; i < 90; i++)
            {
                float u = i / 90f, y = 8 + u * 80, r = (1 - u) * 25 + 1.5f, a = i * 2.39996f;
                m.Hex(baubles[i % baubles.Length], Look.Unlit);
                Block(m, sq + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), a, new Vector3(1.8f, 1.8f, 1.8f));
            }
            m.Hex(0xffd23a, Look.Unlit);
            var st = sq + new Vector3(0, 96, 0);
            for (int k = 0; k < 5; k++)
            {
                float a = Mathf.Pi / 2 + k * Mathf.Tau / 5;
                var tip = st + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 7;
                var l = st + new Vector3(Mathf.Cos(a - 0.63f), Mathf.Sin(a - 0.63f), 0) * 2;
                var r = st + new Vector3(Mathf.Cos(a + 0.63f), Mathf.Sin(a + 0.63f), 0) * 2;
                m.Tri(st, l, tip, Z2, Z2, Z2); m.Tri(st, tip, r, Z2, Z2, Z2);
                m.Tri(st, tip, l, Z2, Z2, Z2); m.Tri(st, r, tip, Z2, Z2, Z2);
            }
        }
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.Tau / 16;
            var p = sq + new Vector3(Mathf.Cos(a) * 62, 0, Mathf.Sin(a) * 50);
            m.Hex(0x7a4a2e);
            Gable(m, p, -a + Mathf.Pi / 2, new Vector3(7, 3.4f, 4.5f), 2.2f, i % 2 == 0 ? 0xc0392bu : 0xf4f7fau, 0.6f);
            m.Hex(i % 3 == 0 ? 0xff5a3au : 0xffd27au, Look.Unlit);
            Block(m, p + new Vector3(0, 3.4f, 0), -a + Mathf.Pi / 2, new Vector3(7.6f, 0.3f, 5.2f));
        }

        // The chalets: wooden walls, snow on the roofs, warm windows; up the streets round the square.
        uint[] wood = { 0x7a4a2e, 0x8a5a36, 0xa8322a, 0xe8dcc0, 0x6a3f28 };
        for (float x = -560; x <= 560; x += 24)
            for (float z = -620; z <= 560; z += 22)
            {
                if (Off(x, z) < Apron + 8 || x * x + z * z > 560 * 560 || Hash(x, z) < 0.4f) continue;
                if (Mathf.Abs(x - sq.X) < 110 && Mathf.Abs(z - sq.Z) < 90) continue;
                if (new Vector2(x - lake.X, z - lake.Y).Length() < 110 || Mathf.Abs(x) < 12) continue;
                var p = new Vector3(x + 4 * (Hash(z, x) - 0.5f), SnowH(x, z) - 0.4f, z);
                float h = 6 + 3 * Hash(x + 1, z);
                m.Hex(wood[(int)(Hash(z + 1, x) * wood.Length)]);
                Gable(m, p, 0, new Vector3(13, h, 10), 4.5f, 0xf4f7fa, 1);
                m.Hex(0xffd27a, Look.Unlit);
                Pane(m, p + new Vector3(-3, h * 0.55f, 5.05f), 1.8f, 1.6f);
                Pane(m, p + new Vector3(3, h * 0.55f, 5.05f), 1.8f, 1.6f);
            }
        Church(m, new Vector3(220, SnowH(220, -480) - 0.5f, -480), 0.1f, 0xe8e2d4, 0xf4f7fa, 1.6f);

        // Snowmen by the turnstiles.
        foreach (var p in Ring(Apron + 3, 0))
        {
            if (Hash(p.X, p.Z) > 0.12f) continue;
            m.Hex(0xfafcff);
            m.Blob(p + new Vector3(0, 1.1f, 0), new Vector3(1.2f, 1.1f, 1.2f), 6, 2);
            m.Blob(p + new Vector3(0, 2.7f, 0), new Vector3(0.85f, 0.8f, 0.85f), 6, 2);
            m.Blob(p + new Vector3(0, 3.8f, 0), new Vector3(0.6f, 0.55f, 0.6f), 6, 2);
            m.Hex(0xff7a1a);
            m.Beam(p + new Vector3(0, 3.8f, 0.5f), p + new Vector3(0, 3.8f, 1.3f), 0.15f, 0.15f);
            m.Hex(home);
            Block(m, p + new Vector3(0, 3.2f, 0), 0, new Vector3(1.4f, 0.35f, 1.4f));
        }
        Edge(m, rng, home, Apron - 7, new uint[] { 0x234a32 }, p => Hash(p.X, p.Z) < 0.6f, 150);
    }
}
