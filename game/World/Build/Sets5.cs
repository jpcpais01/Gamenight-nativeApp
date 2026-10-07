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
