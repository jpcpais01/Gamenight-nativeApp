using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds;

/// <summary>
/// The club's training ground (the PWA's trainingGround.ts): modern, simple and clean. Open
/// fields under the sky, the training centre along the far side (white cladding, ribbon
/// glazing, a flat roof slab, the club's name over the doors), tall ball-stop nets behind the
/// goals, a low mesh fence round the grass, slim floodlight masts, two practice pitches beyond
/// the ends, cones, mannequins and mini goals, rows of trees. No crowd.
/// </summary>
public sealed class TrainingGround : Ground
{
    const float HL = 52.5f, HW = 34f;
    const uint White = 0xe8eae6, Trim = 0x2b3036, NetPole = 0x23332a, NetCol = 0x18241d;
    static readonly Rect2 SignPx = new(0, 640, 1024, 64);

    public TrainingGround()
    {
        Land = new Vector3(0x55, 0x7a, 0x3c) / 255f;
        FloodScale = 0.75f;
    }

    protected override void Build()
    {
        var m = Static;
        void Box(float w, float h, float d, uint col, float x, float y, float z)
        {
            m.Hex(col);
            m.Box(new Vector3(x - w / 2, y - h / 2, z - d / 2), new Vector3(x + w / 2, y + h / 2, z + d / 2));
        }
        // An upright panel facing +z, uv in metres.
        void Panel(float w, float h, uint col, Look look, float x, float y, float z)
        {
            m.Hex(col, look);
            m.QuadUV(new(x - w / 2, y - h / 2, z), new(x + w / 2, y - h / 2, z), new(x + w / 2, y + h / 2, z), new(x - w / 2, y + h / 2, z),
                new(0, 0), new(w, 0), new(w, h), new(0, h));
        }
        // A see-through mesh between two posts' feet, h tall.
        void Net(Vector3 a, Vector3 b, float h)
        {
            m.Hex(NetCol, Look.Stipple);
            m.Quad(a, b, b + Vector3.Up * h, a + Vector3.Up * h, (b - a).Length(), h);
        }

        // ---- the training centre: two storeys along the far side, its doors facing halfway
        float front = -(HW + 13);
        const float depth = 14;
        Box(64, 8.4f, depth, White, 0, 4.2f, front - depth / 2);
        Panel(60, 3, 0x1a232c, Look.Curtain, 0, 1.75f, front + 0.15f);
        Panel(60, 2, 0x1a232c, Look.Curtain, 0, 6.05f, front + 0.15f);
        Box(67, 0.5f, depth + 2.6f, Trim, 0, 8.65f, front - depth / 2 + 0.6f);
        Box(67, 0.12f, 0.2f, HomeColor, 0, 8.3f, front + 1.95f);
        Box(10, 0.25f, 4.2f, Trim, 0, 3.45f, front + 2.1f);
        foreach (float x in new[] { -4.6f, 4.6f }) Box(0.18f, 3.4f, 0.18f, Trim, x, 1.7f, front + 4);
        Box(1.4f, 11, 1.4f, HomeColor, 7.4f, 5.5f, front + 0.6f);
        // The gym wing: lower, glazed full height.
        Box(22, 5.2f, 12, White, 43, 2.6f, front - 6.5f);
        Panel(20, 4, 0x1a232c, Look.Curtain, 43, 2.3f, front - 0.35f);
        Box(23.5f, 0.4f, 13.6f, Trim, 43, 5.4f, front - 6.2f);
        Sign(m, new Vector3(-0.6f, 4.15f, front + 0.2f), Vector3.Back, 22.4f, 1.4f, SignPx);

        // ---- ball-stop nets behind both goals
        const float netX = HL + 7, netH = 8;
        foreach (int s in new[] { -1, 1 })
        {
            m.Hex(NetPole);
            for (float z = -24; z <= 24; z += 6) m.Column(new Vector3(s * netX, 0, z), 0.09f, 0.07f, netH, 6);
            Box(0.1f, 0.1f, 48, NetPole, s * netX, netH, 0);
            Net(new Vector3(s * netX, 0, -24), new Vector3(s * netX, 0, 24), netH);
        }

        // ---- a low mesh fence round the grass (a gap at the far side for the path in)
        const float fx = HL + 13, fz = HW + 11, fenceH = 1.2f;
        foreach (var (x0, z0, x1, z1) in new[] { (-fx, fz, fx, fz), (-fx, -fz, -fx, fz), (fx, -fz, fx, fz), (-fx, -fz, -5f, -fz), (5f, -fz, fx, -fz) })
        {
            var a = new Vector3(x0, 0, z0); var b = new Vector3(x1, 0, z1);
            m.Hex(NetPole);
            m.Beam(a + Vector3.Up * fenceH, b + Vector3.Up * fenceH, 0.06f, 0.06f);
            int n = Mathf.RoundToInt((b - a).Length() / 3);
            for (int i = 0; i <= n; i++) m.Beam(a.Lerp(b, i / (float)n), a.Lerp(b, i / (float)n) + Vector3.Up * fenceH, 0.07f, 0.07f);
            Net(a, b, fenceH);
        }

        // ---- practice pitches beyond both ends, mini goals at each end
        foreach (int s in new[] { -1, 1 })
        {
            float cx = s * (fx + 30);
            PracticePitch(m, cx);
            foreach (int e in new[] { -1, 1 }) MiniGoal(m, cx, e * 29.6f, e > 0 ? Mathf.Pi : 0, 5, 2);
        }

        // ---- on the touchline: cones, a mannequin wall, mini goals
        m.Hex(0xff7a1f);
        for (int i = 0; i < 24; i++)
        {
            int row = i < 12 ? 0 : 1, k = i % 12;
            m.Column(new Vector3((row == 1 ? 20 : -42) + k * 1.8f, 0, -(HW + 2.2f) - k % 2 * 1.2f), 0.13f, 0, 0.32f, 6);
        }
        for (int i = 0; i < 4; i++)
        {
            float x = -28 + i * 0.62f, z = -(HW + 4.2f);
            Box(0.42f, 1.55f, 0.16f, 0xffd23a, x, 1.0f, z);
            m.Blob(new Vector3(x, 1.92f, z), new Vector3(0.13f, 0.13f, 0.13f), 6, 3);
            Box(0.05f, 0.25f, 0.05f, Trim, x, 0.12f, z);
        }
        foreach (float x in new[] { -46f, 46f }) MiniGoal(m, x, -(HW + 4.6f), 0, 3, 1);

        // ---- floodlight masts in the corners: slim tapering poles, a bank of lamps on top
        const float Head = 24;
        var lamps = new List<Vector3>();
        foreach (var (px, pz) in new[] { (-65.3f, -45.8f), (65.3f, -45.8f), (-65.3f, 45.8f), (65.3f, 45.8f) })
        {
            m.Hex(0xd9dcdf);
            m.Column(new Vector3(px, 0, pz), 0.45f, 0.22f, Head, 8);
            var b = new Basis(Vector3.Up, Mathf.Atan2(-px, -pz)) * new Basis(Vector3.Right, 0.45f);
            var c0 = new Vector3(px, Head + 1.2f, pz);
            m.Hex(Trim);
            m.Box(new Transform3D(b, c0), new Vector3(5.6f, 2.2f, 0.3f));
            m.Hex(0xffffff, Look.Lamp);
            var p = c0 + b.Z * 0.2f;
            var xv = b.X * 2.6f; var yv = b.Y * 0.9f;
            m.QuadUV(p - xv - yv, p + xv - yv, p + xv + yv, p - xv + yv, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
            lamps.Add(c0 + b.Z * 1.2f);
        }
        Lamps = lamps.ToArray();

        Pitchside.CornerFlags(m);
        Pitchside.Dugouts(m, Pitchside.HomeKit, Pitchside.AwayKit);
        Trees(m);
    }

    /// <summary>A 40 x 60 m practice pitch centred on (cx, 0): mown stripes and white lines.</summary>
    static void PracticePitch(MeshData m, float cx)
    {
        const float y = 0.03f, ly = 0.05f, lw = 0.07f;
        void Flat(float x0, float z0, float x1, float z1, float h) =>
            m.Quad(new(x0, h, z1), new(x1, h, z1), new(x1, h, z0), new(x0, h, z0), x1 - x0, z1 - z0);
        for (int i = 0; i < 10; i++)
        {
            m.Hex(i % 2 == 1 ? 0x4f7f3au : 0x578a40u);
            Flat(cx - 20, -30 + i * 6, cx + 20, -30 + (i + 1) * 6, y);
        }
        m.Hex(0xeef2ea);
        void Line(float x0, float z0, float x1, float z1)
        {
            var a = new Vector3(x0, ly, z0); var b = new Vector3(x1, ly, z1);
            var side = (b - a).Normalized().Cross(Vector3.Up) * lw;
            m.Quad(a - side, b - side, b + side, a + side, 1, 1);
        }
        const float hx = 18.5f, hz = 28.5f;
        Line(cx - hx, -hz, cx + hx, -hz); Line(cx - hx, hz, cx + hx, hz);
        Line(cx - hx, -hz, cx - hx, hz); Line(cx + hx, -hz, cx + hx, hz);
        Line(cx - hx, 0, cx + hx, 0);
        foreach (int e in new[] { -1, 1 })
        {
            float z0 = e * hz, z1 = e * (hz - 6);
            Line(cx - 8, z0, cx - 8, z1); Line(cx + 8, z0, cx + 8, z1); Line(cx - 8, z1, cx + 8, z1);
        }
        for (int i = 0; i < 24; i++)
        {
            float a0 = i / 24f * Mathf.Tau, a1 = (i + 1) / 24f * Mathf.Tau;
            Line(cx + Mathf.Cos(a0) * 6, Mathf.Sin(a0) * 6, cx + Mathf.Cos(a1) * 6, Mathf.Sin(a1) * 6);
        }
    }

    /// <summary>A small goal facing +z at rotY 0: white frame, the base behind, a net.</summary>
    static void MiniGoal(MeshData m, float x, float z, float rotY, float w, float h)
    {
        var t = new Transform3D(new Basis(Vector3.Up, rotY), new Vector3(x, 0, z));
        float d = h * 0.8f;
        m.Hex(0xf4f4f0);
        void Bar(float bw, float bh, float bd, float px, float py, float pz) =>
            m.Box(t * new Transform3D(Basis.Identity, new Vector3(px, py, pz)), new Vector3(bw, bh, bd));
        Bar(0.08f, h, 0.08f, -w / 2, h / 2, 0);
        Bar(0.08f, h, 0.08f, w / 2, h / 2, 0);
        Bar(w + 0.08f, 0.08f, 0.08f, 0, h, 0);
        Bar(w + 0.08f, 0.06f, 0.06f, 0, 0.03f, -d);
        Bar(0.06f, 0.06f, d, -w / 2, 0.03f, -d / 2);
        Bar(0.06f, 0.06f, d, w / 2, 0.03f, -d / 2);
        m.Hex(NetCol, Look.Stipple);
        m.Quad(t * new Vector3(-w / 2, h, 0), t * new Vector3(w / 2, h, 0), t * new Vector3(w / 2, 0, -d), t * new Vector3(-w / 2, 0, -d), w, h);
    }

    /// <summary>Round trees in rows beyond the fences.</summary>
    static void Trees(MeshData m)
    {
        long seed = 7;
        float Rnd() => (seed = seed * 16807 % 2147483647) / 2147483647f;
        var spots = new List<(float x, float z, float s)>();
        for (float x = -150; x <= 150; x += 8)
        {
            spots.Add((x + Rnd() * 3, 66 + Rnd() * 6, 0.8f + Rnd() * 0.5f));
            spots.Add((x + Rnd() * 3, -84 - Rnd() * 8, 0.9f + Rnd() * 0.6f));
        }
        for (float z = -60; z <= 60; z += 8)
            foreach (int s in new[] { -1, 1 }) spots.Add((s * (140 + Rnd() * 6), z + Rnd() * 3, 0.8f + Rnd() * 0.5f));
        uint[] greens = { 0x2f5a2a, 0x3b6b30, 0x2a4f2c, 0x45763a };
        for (int i = 0; i < spots.Count; i++)
        {
            var (x, z, s) = spots[i];
            m.Hex(0x4a3a2c);
            m.Column(new Vector3(x, 0, z), 0.35f * s, 0.25f * s, 3 * s, 5);
            m.Hex(greens[i % greens.Length]);
            m.Blob(new Vector3(x, 3 + 3.6f * s, z), new Vector3(3.2f, 3.7f, 3.2f) * s, 7, 4);
        }
    }

    protected override void Paint(Signage s)
    {
        // The sign over the doors: the crest, a club-coloured bar, "<CLUB>  TRAINING CENTRE".
        float y0 = SignPx.Position.Y;
        s.Rect(SignPx, 0xe8eae6);
        s.Crest(new Vector2(360, y0 + 32), 58, HomeColor);
        s.Rect(new Rect2(392, y0 + 14, 5, 36), HomeColor);
        s.Text(new Rect2(404, y0, 600, 64), $"{ClubName.ToUpperInvariant()}  TRAINING CENTRE", 0x23282e, 40);
    }
}
