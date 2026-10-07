using System;
using Godot;

namespace GameNight.Grounds.Build;

/// <summary>
/// Final: the great final-night bowl. Three steep tiers with two bands of boxes between, a roof
/// ring reaching far over the seats, and a midnight-blue skin scattered with stars outside and
/// along the roof's front, twinkling under the floodlights. Each end hangs a giant ring of stars
/// from its roof.
/// </summary>
public sealed class Final : StandSet
{
    const uint Navy = 0x14204a, Silver = 0xc9ced6, RoofCol = 0x262b36;
    public override string Name => "Final";
    public override string About => "A colossal three-tier bowl under a ring of stars, built for final nights";
    public override uint Swatch => 0x24357a;
    public override uint[] Mains => new uint[] { Navy, Silver };
    public override Vector2? TifoAt(Section x) => new(x.Edge + 0.6f, x.Top - 4.6f);
    public override float Natural(Kind k) => k == Kind.Side ? 52 : k == Kind.End ? 50 : 48;
    public override Vector2 Range => new(44, 58);
    public override TierFans LowerFans() => new() { Vom = new(7.2f, 10.2f), Shade = new(9, 17) };
    public override (uint col, Look look) Cap => (Navy, Look.Stars);
    public override Outside Outside => new(0x1a2238, Look.Plain, 0, Stairs.Tower, Silver);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        // The top tier steepens as the bowl rises, so its back row still sees the near touchline.
        float uh = T - 5, ue = 31 + (uh - 27.8f) / 0.86f;
        x.Top = T; x.Ue = ue; x.Back = ue + 2; x.BackH = T - 0.4f; x.Edge = 4;
        x.To(new(22, 11.5f), Kit.Concrete)
            .To(new(22, 14.5f), 0x2a3440, Look.Glass)
            .To(new(21, 14.5f), Kit.Concrete)
            .To(new(21, 15.8f), 0xffffff, Look.Ribbon)
            .To(new(21.5f, 15.8f), Kit.Concrete)
            .To(new(31.5f, 23), g.Seat, Look.Tier, 1, 4)
            .To(new(31.5f, 26.5f), 0x2a3440, Look.Glass)
            .To(new(30.5f, 26.5f), Kit.Concrete)
            .To(new(30.5f, 27.8f), 0xffffff, Look.Ribbon)
            .To(new(31, 27.8f), Kit.Concrete)
            .To(new(ue, uh), g.Seat, Look.Tier, 1, 5)
            .To(new(ue, T - 1.2f), Kit.DarkConcrete)
            .To(new(x.Back, x.BackH), Navy)
            .To(new(x.Back, 0), Navy, Look.Stars, flip: true);
        // The roof ring, deep over the seats; a starry fascia and a light strip under its front.
        x.Sheet(new(x.Back, x.BackH), new(x.Edge, T), RoofCol, Look.Roof)
            .Sheet(new(x.Edge, T - 4), new(x.Edge, T), Navy, Look.Stars)
            .Sheet(new(x.Edge + 0.3f, T - 4.05f), new(x.Edge + 3.4f, T - 3.85f), 0xffffff, Look.RoofLight);
        x.Fans(new(21.5f, 15.8f), new(31.5f, 23), new TierFans { Shade = new(6, 14) });
        x.Fans(new(31, 27.8f), new(ue, uh), new TierFans { Shade = new(-2, 8) });
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        Shared.RoofLamps(p, g, 1.3f, 4.4f);
        if (p.Kind != Kind.End || p.Hidden || p.Partial) return;

        // The ring of stars, hung on two cables from the middle of the end's roof.
        var m = p.Mesh(g);
        var pt = p.Path[p.Mid];
        var s = p.Sec[p.Mid];
        var inward = new Vector3(-pt.NX, 0, -pt.NZ).Normalized();
        var right = Vector3.Up.Cross(inward).Normalized();
        const float Rd = 7.5f;
        var c = pt.At(s.Edge + 1.2f, s.Top - 6 - Rd);
        Vector3 P(float u, float v) => c + right * u + Vector3.Up * v;

        m.Hex(Kit.Steel);
        foreach (float u in new[] { -Rd * 0.6f, Rd * 0.6f })
            m.Beam(P(u, Rd * 0.8f), pt.At(s.Edge + 1.2f, s.Top - 4) + right * u, 0.15f, 0.15f);

        // A midnight disc with a silver rim.
        const int N = 28;
        m.Hex(Navy);
        for (int i = 0; i < N; i++)
        {
            float a0 = i * Mathf.Tau / N, a1 = (i + 1) * Mathf.Tau / N;
            m.Tri(c, P(Rd * MathF.Cos(a0), Rd * MathF.Sin(a0)), P(Rd * MathF.Cos(a1), Rd * MathF.Sin(a1)), Vector2.Zero, Vector2.Zero, Vector2.Zero);
        }
        var fwd = inward * 0.05f;
        m.Hex(Silver, Look.Unlit);
        for (int i = 0; i < N; i++)
        {
            float a0 = i * Mathf.Tau / N, a1 = (i + 1) * Mathf.Tau / N;
            Vector2 o0 = new(MathF.Cos(a0), MathF.Sin(a0)), o1 = new(MathF.Cos(a1), MathF.Sin(a1));
            float r0 = Rd - 0.7f;
            m.Quad(P(o0.X * Rd, o0.Y * Rd) + fwd, P(o1.X * Rd, o1.Y * Rd) + fwd, P(o1.X * r0, o1.Y * r0) + fwd, P(o0.X * r0, o0.Y * r0) + fwd);
        }

        // Eight stars round the ring, one in the middle: they shine on their own, day and night.
        m.Hex(0xffffff, Look.Unlit);
        void Star(float cu, float cv, float r, float turn)
        {
            var mid = P(cu, cv) + fwd * 2;
            for (int k = 0; k < 5; k++)
            {
                float a = turn + k * Mathf.Tau / 5, b = a + Mathf.Tau / 10, e = a - Mathf.Tau / 10;
                var tip = P(cu + r * MathF.Cos(a), cv + r * MathF.Sin(a)) + fwd * 2;
                var nb = P(cu + r * 0.42f * MathF.Cos(b), cv + r * 0.42f * MathF.Sin(b)) + fwd * 2;
                var ne = P(cu + r * 0.42f * MathF.Cos(e), cv + r * 0.42f * MathF.Sin(e)) + fwd * 2;
                m.Tri(mid, ne, tip, Vector2.Zero, Vector2.Zero, Vector2.Zero);
                m.Tri(mid, tip, nb, Vector2.Zero, Vector2.Zero, Vector2.Zero);
            }
        }
        for (int k = 0; k < 8; k++)
        {
            float a = Mathf.Pi / 2 + k * Mathf.Tau / 8;
            Star(4.3f * MathF.Cos(a), 4.3f * MathF.Sin(a), 1.3f, a);
        }
        Star(0, 0, 2.2f, Mathf.Pi / 2);
    }
}

/// <summary>
/// Haunt: a Halloween stand. Purple brick and black iron, a band of glowing orange round the
/// tier, jack-o'-lanterns grinning along the balcony, a crown of iron spikes on the roof, bats
/// wheeling over it, giant cobwebs (spider included) strung across the corners and a crooked
/// spire behind each end with its windows lit.
/// </summary>
public sealed class Haunt : StandSet
{
    const uint Purple = 0x4a2366, Black = 0x16121c, Orange = 0xff7a1a, Glow = 0xffc23a, Web = 0xd8d4e6;
    public override string Name => "Haunt";
    public override string About => "Pumpkins, cobwebs, bats and spires: a stand for Halloween night";
    public override uint Swatch => 0x5b2a86;
    public override uint[] Mains => new uint[] { Purple, Black };
    public override float Natural(Kind k) => k == Kind.End ? 30 : k == Kind.Side ? 28 : 26;
    public override Vector2 Range => new(22, 36);
    public override TierFans LowerFans() => new() { Vom = new(7.2f, 10.2f), Shade = new(9, 17) };
    public override (uint col, Look look) Cap => (Purple, Look.Brick);
    public override Outside Outside => new(Black, Look.Plain, 0, Stairs.Tower, Purple);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float uh = T - 4, ue = 21.5f + (uh - 15.8f) / 0.8f;
        x.Top = T; x.Ue = ue; x.Back = ue + 2; x.BackH = T + 1; x.Edge = 8;
        x.To(new(22, 11.5f), Black)
            .To(new(22, 14.5f), 0x3a1f4a, Look.Glass)
            .To(new(21, 14.5f), Black)
            .To(new(21, 15.8f), Orange, Look.Unlit)
            .To(new(21.5f, 15.8f), Black)
            .To(new(ue, uh), g.Seat, Look.Tier, 1, 5)
            .To(new(ue, T), Purple, Look.Brick)
            .To(new(x.Back, T + 1), Black)
            .To(new(x.Back, 0), Purple, Look.Brick, flip: true);
        x.Sheet(new(x.Back, T + 1), new(x.Edge, T), Black, Look.Roof)
            .Sheet(new(x.Edge, T - 1.6f), new(x.Edge, T), Black, Look.Fascia)
            .Sheet(new(x.Edge, T - 1.9f), new(x.Edge, T - 1.6f), Orange, Look.Unlit)
            .Sheet(new(x.Edge + 0.3f, T - 1.95f), new(x.Edge + 3, T - 1.8f), 0xffffff, Look.RoofLight);
        x.Fans(new(21.5f, 15.8f), new(ue, uh), new TierFans { Shade = new(-2, 9) });
    }

    static readonly Vector2 Z = Vector2.Zero;

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        Shared.RoofLamps(p, g, 1.2f, 2.2f);
        var up = Vector3.Up;

        // Jack-o'-lanterns along the balcony, grinning at the pitch.
        p.Each(8, s => new(19.9f, 14.6f), (pos, n, s) =>
        {
            var inw = -n;
            var r = up.Cross(inw).Normalized();
            m.Hex(0xe8701a);
            m.Blob(pos, new Vector3(1.6f, 1.25f, 1.6f), 8, 3);
            m.Hex(0x3f6a2a);
            m.Box(new Transform3D(Basis.Identity, pos + up * 1.35f), new Vector3(0.35f, 0.6f, 0.35f));
            var f = pos + inw * 1.62f;
            m.Hex(Glow, Look.Unlit);
            foreach (float side in new[] { -1f, 1f })
                m.Tri(f + r * (side * 0.62f - 0.26f) + up * 0.12f, f + r * (side * 0.62f + 0.26f) + up * 0.12f, f + r * side * 0.62f + up * 0.6f, Z, Z, Z);
            m.Quad(f - r * 0.75f - up * 0.5f, f + r * 0.75f - up * 0.5f, f + r * 0.58f - up * 0.12f, f - r * 0.58f - up * 0.12f);
        });

        // A crown of iron spikes along the roof's front.
        m.Hex(Black);
        p.Each(4, s => new(s.Edge + 0.8f, s.Top), (pos, n, s) =>
        {
            var r = up.Cross(n).Normalized();
            float h = 2.6f + 1.6f * Hash.At(pos, 3);
            var tip = pos + up * h;
            m.Tri(pos - r * 0.55f, pos + r * 0.55f, tip, Z, Z, Z);
            m.Tri(pos + r * 0.55f, pos - r * 0.55f, tip, Z, Z, Z);
            m.Tri(pos - n * 0.55f, pos + n * 0.55f, tip, Z, Z, Z);
            m.Tri(pos + n * 0.55f, pos - n * 0.55f, tip, Z, Z, Z);
        });

        if (p.Hidden) return;

        // Bats wheeling over the roof.
        p.Spread(Mathf.Max(2, Mathf.RoundToInt(p.Length / 22)), s => new(s.Edge + 4, s.Top + 9), (pos, n, s) =>
        {
            float h = Hash.At(pos, 7);
            var c = pos + up * (h * 7) - n * (h * 6);
            const float B = 1.7f;
            var r = new Vector3(MathF.Cos(h * 9), 0, MathF.Sin(h * 9));
            var fwd = up.Cross(r);
            m.Hex(Black);
            m.Blob(c, new Vector3(0.45f, 0.4f, 0.45f) * B, 6, 2);
            foreach (float side in new[] { -1f, 1f })
            {
                var w0 = c + r * side * 0.3f * B;
                var w1 = c + (r * side * 1.6f + up * 0.7f) * B;
                var w2 = c + (r * side * 2.6f + up * 0.2f) * B;
                var w3 = c + (r * side * 1.7f - up * 0.25f) * B;
                m.Tri(w0 + fwd * 0.4f * B, w1, w0 - fwd * 0.4f * B, Z, Z, Z);
                m.Tri(w0 - fwd * 0.4f * B, w1, w0 + fwd * 0.4f * B, Z, Z, Z);
                m.Tri(w1, w2, w3, Z, Z, Z);
                m.Tri(w1, w3, w2, Z, Z, Z);
                m.Tri(w0 + fwd * 0.4f * B, w1, w3, Z, Z, Z);
                m.Tri(w0 + fwd * 0.4f * B, w3, w1, Z, Z, Z);
            }
        });

        var pt = p.Path[p.Mid];
        var sm = p.Sec[p.Mid];
        var inward = new Vector3(-pt.NX, 0, -pt.NZ).Normalized();
        var right = up.Cross(inward).Normalized();

        if (p.Kind == Kind.Corner && !p.Partial)
        {
            // A giant cobweb across the corner, its spider waiting near the middle.
            const float Rw = 10f;
            var c = pt.At(sm.Edge + 3, sm.Top - Rw - 1.5f);
            Vector3 W(float a, float rr) => c + right * (MathF.Cos(a) * rr) + up * (MathF.Sin(a) * rr);
            m.Hex(Web, Look.Unlit);
            const int Sp = 9;
            for (int k = 0; k < Sp; k++)
                m.Beam(c, W(k * Mathf.Tau / Sp + 0.2f, Rw), 0.3f, 0.3f);
            for (int ring = 1; ring <= 4; ring++)
            {
                float rr = Rw * ring / 4.4f;
                for (int k = 0; k < Sp; k++)
                    m.Beam(W(k * Mathf.Tau / Sp + 0.2f, rr), W((k + 1) * Mathf.Tau / Sp + 0.2f, rr * (0.92f + 0.08f * (k % 2))), 0.26f, 0.26f);
            }
            var sp = W(1.1f, Rw * 0.3f) + inward * 0.4f;
            m.Hex(Black);
            m.Blob(sp, new Vector3(0.9f, 0.75f, 0.6f), 8, 3);
            m.Blob(sp - up * 1, new Vector3(0.55f, 0.5f, 0.45f), 6, 2);
            for (int k = 0; k < 4; k++)
                foreach (float side in new[] { -1f, 1f })
                {
                    var knee = sp + right * side * 1.5f + up * (0.9f - k * 0.55f) + inward * 0.5f;
                    m.Beam(sp + up * (0.3f - k * 0.25f), knee, 0.18f, 0.18f);
                    m.Beam(knee, knee + right * side * 0.9f - up * 0.9f, 0.15f, 0.15f);
                }
            m.Hex(0xff3a2a, Look.Unlit);
            m.Box(new Transform3D(Basis.Identity, sp - up * 1 + inward * 0.45f + right * 0.18f), new Vector3(0.18f, 0.18f, 0.18f));
            m.Box(new Transform3D(Basis.Identity, sp - up * 1 + inward * 0.45f - right * 0.18f), new Vector3(0.18f, 0.18f, 0.18f));
        }

        if (p.Kind == Kind.End && !p.Partial)
        {
            // A crooked spire behind the end, its windows lit orange.
            float H = sm.Top + 16;
            var b = pt.At(sm.Back + 5, 0);
            var basis = new Basis(Vector3.Up, MathF.Atan2(inward.X, inward.Z)) * new Basis(Vector3.Forward, 0.05f);
            var t = new Transform3D(basis, b + up * (H / 2));
            m.Hex(Purple, Look.Brick);
            m.Box(t, new Vector3(8, H, 8));
            var top = t * new Vector3(0, H / 2, 0);
            var tip = t * new Vector3(0, H / 2 + 12, 0);
            m.Hex(Black);
            Vector3 Cn(float sx, float sz) => t * new Vector3(sx * 5, H / 2, sz * 5);
            var cs = new[] { Cn(-1, -1), Cn(1, -1), Cn(1, 1), Cn(-1, 1) };
            for (int k = 0; k < 4; k++)
            {
                m.Tri(cs[k], cs[(k + 1) % 4], tip, Z, Z, Z);
                m.Tri(cs[(k + 1) % 4], cs[k], tip, Z, Z, Z);
            }
            m.Hex(Glow, Look.Unlit);
            for (int k = 0; k < 4; k++)
            {
                var w = t * new Vector3(0, -H / 2 + sm.Top + 3 + k * 3.2f, 0) + inward * 4.05f;
                if (k == 2) continue;
                m.Quad(w - right * 0.7f - up * 1.1f, w + right * 0.7f - up * 1.1f, w + right * 0.7f + up * 0.8f, w - right * 0.7f + up * 0.8f);
            }
            m.Hex(Orange, Look.Unlit);
            m.Blob(top + up * 12.6f, new Vector3(0.5f, 0.5f, 0.5f), 6, 2);
        }
    }
}
