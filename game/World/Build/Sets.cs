using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds.Build;

/// <summary>
/// Arena: the modern bowl. A band of glass boxes over the concourse, a raked upper tier, a
/// cantilever roof with steel girders riding on top and translucent panels at the front, LED
/// ribbons on the tier fronts, a glazed facade lit up at night. Its corners hang big screens.
/// </summary>
public sealed class Arena : StandSet
{
    const uint White = 0xffffff, RoofCol = 0x30353d, Panels = 0xaab6c0, Facade = 0x2c333d;
    public override string Name => "Arena";
    public override string About => "Modern bowl: glass boxes, a sweeping roof, LED ribbons";
    public override uint Swatch => 0x9fc6e8;
    public override uint[] Mains => new uint[] { RoofCol, Facade, Panels };
    public override float Natural(Kind k) => k == Kind.Side ? 40 : 34;
    public override Vector2 Range => new(24, 46);
    public override (uint col, Look look) Front(BuiltGround g) => (White, Look.Ribbon);
    public override TierFans LowerFans() => new() { Vom = new(7.2f, 10.2f), Shade = new(9, 17) };
    public override (uint col, Look look) Cap => (Facade, Look.Curtain);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float uh = T - 6, ue = 21.5f + (uh - 15.8f) / 0.77f;
        x.Top = T; x.Ue = ue; x.Back = ue + 2; x.BackH = T - 1.2f; x.Edge = 8;
        float panel = 13;
        x.To(new(22, 11.5f), Kit.Concrete)
            .To(new(22, 14.5f), White, Look.Glass)
            .To(new(21, 14.5f), Kit.Concrete)
            .To(new(21, 15.8f), White, Look.Ribbon)
            .To(new(21.5f, 15.8f), Kit.Concrete)
            .To(new(ue, uh), g.Seat, Look.Tier, 1, 6)
            .To(new(ue, T - 1.9f), Kit.DarkConcrete)
            .To(new(x.Back, x.BackH), Kit.DarkConcrete)
            .To(new(x.Back, 0), Facade, Look.Curtain, flip: true);
        x.Sheet(new(x.Back, x.BackH), new(panel, x.RoofAt(panel)), RoofCol, Look.Roof)
            .Sheet(new(panel, x.RoofAt(panel)), new(x.Edge, T), Panels)
            .Sheet(new(x.Edge, T - 2.2f), new(x.Edge, T), White, Look.Fascia)
            .Sheet(new(x.Edge + 0.3f, T - 2.25f), new(x.Edge + 3.4f, T - 2.05f), White, Look.RoofLight);
        x.Fans(new(21.5f, 15.8f), new(ue, uh), new TierFans { Shade = new(-2, 10) });
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        // Girders over the roof: a truss across every third point, chords tying them.
        m.Hex(Kit.Steel);
        var tops = new List<Vector3[]>();
        for (int i = 0; i < p.Path.Count; i++)
        {
            if (i % 3 != 0 && i != p.Path.Count - 1) continue;
            var s = p.Sec[i];
            var pt = p.Path[i];
            float[] offs = { s.Back - 1, (s.Back + s.Edge) * 0.5f, s.Edge + 2 };
            var row = Array.ConvertAll(offs, o => pt.At(o, s.RoofAt(o) + 2.6f));
            m.Beam(row[0], row[2], 0.45f, 0.9f);
            for (int k = 0; k < 3; k++) m.Beam(row[k], pt.At(offs[k], s.RoofAt(offs[k])), 0.3f, 0.3f);
            tops.Add(row);
        }
        for (int i = 1; i < tops.Count; i++)
            for (int k = 0; k < 3; k++) m.Beam(tops[i - 1][k], tops[i][k], 0.35f, 0.6f);

        // Lamps along the roof front, and the banks that light the pitch.
        m.Hex(0xffffff, Look.Lamp);
        for (int i = 0; i < p.Path.Count; i += 2)
        {
            var pt = p.Path[i];
            var s = p.Sec[i];
            var c = pt.At(s.Edge + 1.2f, s.Top - 2.7f);
            var basis = new Basis(Vector3.Up, pt.FacePitch + Mathf.Pi) * new Basis(Vector3.Right, 0.75f);
            var bx = basis.X * 1.7f; var by = basis.Y * 0.55f;
            m.QuadUV(c - bx - by, c + bx - by, c + bx + by, c - bx + by, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }
        p.Spread(Mathf.Max(1, Mathf.RoundToInt(p.Length / 28)), s => new(s.Edge + 1, s.Top - 2), (pos, n, s) => g.AddLamp(pos, p));

        var mid = p.Path[p.Mid];
        var ms = p.Sec[p.Mid];
        if (p.Kind == Kind.Corner && !p.Partial && !p.Hidden)
        {
            // A big screen under the corner roof.
            var c = mid.At(ms.Edge + 2.5f, ms.Top - 6.2f);
            var basis = new Basis(Vector3.Up, mid.FacePitch + Mathf.Pi) * new Basis(Vector3.Right, 0.2f);
            m.Hex(0x23272e);
            m.Box(new Transform3D(basis, c + new Vector3(mid.NX, 0, mid.NZ) * 0.3f), new Vector3(11.2f, 6.5f, 0.5f));
            m.Hex(0xffffff, Look.Screen);
            var bx = basis.X * 5.25f; var by = basis.Y * 2.95f; var f = basis.Z * 0.02f;
            m.QuadUV(c - bx - by + f, c + bx - by + f, c + bx + by + f, c - bx + by + f, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
            g.Screens = true;
        }
        if (p.Slot == Slot.Main)
        {
            // The TV gantry slung under the roof.
            var gp = mid.At(ms.Edge + 9, ms.RoofAt(ms.Edge + 9) - 2.6f);
            m.Hex(0x23272e);
            m.Box(new Transform3D(Basis.Identity, gp), new Vector3(34, 1.6f, 2.2f));
            for (int k = -3; k <= 3; k++) m.Box(new Transform3D(Basis.Identity, gp + new Vector3(k * 4.6f, 1.1f, 0.6f)), new Vector3(0.7f, 0.6f, 1.1f));
        }
    }
}

/// <summary>
/// Terrace: the old English ground. One deep standing terrace with crush barriers, under a
/// roof on steel pillars (a barrel roof at the ends, a pitched one along the sides), the back
/// clad in the club's colour, brick outside. Floodlight gantries ride the roofs; its corners
/// raise lattice pylons.
/// </summary>
public sealed class Terrace : StandSet
{
    const uint RoofCol = 0x3a4048, Brick = 0x8c4f3c, Steps = 0x76787d;
    public override string Name => "Terrace";
    public override string About => "Old English: a packed standing terrace, pillars, brick";
    public override uint Swatch => 0xc0683f;
    public override uint[] Mains => new uint[] { RoofCol, Brick };
    public override float Natural(Kind k) => k == Kind.Side ? 14 : k == Kind.End ? 13.5f : 12.5f;
    public override Vector2 Range => new(12, 17);
    public override (uint col, float par) Lower(BuiltGround g) => (Steps, 0);
    public override TierFans LowerFans() => new() { Aisles = false, Fill = 0.95f, Shade = new(-2, 6) };
    public override (uint col, Look look) Cap => (Brick, Look.Brick);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float rb = T + 2.6f;
        x.Top = T; x.Back = 22.2f; x.BackH = rb; x.Edge = 2; x.Ue = 20;
        x.To(new(21.5f, 11.5f), Kit.Concrete)
            .To(new(21.5f, rb), Kit.Darken(g.Home, 0.7f), Look.Roof)
            .To(new(22.2f, rb), Kit.Concrete)
            .To(new(22.2f, 0), Brick, Look.Brick, flip: true);
        // The roof: barrel-vaulted behind the goals, pitched along the sides.
        float bulge = kind == Kind.End ? 2.4f : kind == Kind.Corner ? 1.2f : 0;
        Vector2 Arc(float t) => new(22.2f + (2 - 22.2f) * t, rb + (T - rb) * t + bulge * Mathf.Sin(Mathf.Pi * t));
        for (int i = 0; i < 6; i++) x.Sheet(Arc(i / 6f), Arc((i + 1) / 6f), RoofCol, Look.Roof);
        x.Sheet(new(2, T - 2.2f), new(2, T), 0xffffff, Look.Fascia);
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        // Pillars along the front of the roof.
        m.Hex(Kit.Steel);
        p.Each(10, s => new(2, Kit.LowerAt(2)), (pos, n, s) => m.Beam(pos, new Vector3(pos.X, s.Top - 2.2f, pos.Z), 0.32f, 0.32f));
        // Crush barriers: waist-high rails in short staggered runs down the terrace.
        int row = 0;
        foreach (float o in new[] { 3.6f, 7.2f, 10.8f, 14.4f, 18f })
        {
            float h = Kit.LowerAt(o);
            p.Each(5, s => new(o, h), (pos, n, s) =>
            {
                var t = new Vector3(-n.Z, 0, n.X) * 1.75f;
                var up = new Vector3(0, 1.05f, 0);
                m.Beam(pos - t + up, pos + t + up, 0.09f, 0.09f);
                m.Beam(pos - t, pos - t + up, 0.08f, 0.08f);
                m.Beam(pos + t, pos + t + up, 0.08f, 0.08f);
            }, 1.5f + row % 2 * 2.5f);
            row++;
        }
        if (p.Kind == Kind.Corner)
        {
            // A lattice pylon out behind the corner, its lamps over the pitch.
            if (p.Partial) return;
            var mid = p.Path[p.Mid];
            var foot = mid.At(28, 0);
            const float Head = 40;
            Parts.Lattice(m, foot, Head, 1.6f, 0.7f, Kit.Steel);
            var face = new Vector3(-mid.NX, 0, -mid.NZ);
            m.Hex(Kit.Steel);
            m.Box(new Transform3D(Parts.Facing(face), foot + new Vector3(0, Head, 0)), new Vector3(5, 0.4f, 2));
            g.AddLamp(Parts.LampBank(m, foot + new Vector3(0, Head + 2.2f, 0) + face * 0.6f, face, 6, 3.6f, 0.55f, 3), p);
            return;
        }
        // Floodlight gantries on the roof's back edge.
        p.Spread(Mathf.Max(1, Mathf.RoundToInt(p.Length / 24)), s => new(21.6f, s.BackH), (pos, n, s) =>
        {
            var t = new Vector3(-n.Z, 0, n.X) * 1.8f;
            m.Hex(Kit.Steel);
            m.Beam(pos - t, pos - t + new Vector3(0, 4.2f, 0), 0.22f, 0.22f);
            m.Beam(pos + t, pos + t + new Vector3(0, 4.2f, 0), 0.22f, 0.22f);
            g.AddLamp(Parts.LampBank(m, pos + new Vector3(0, 4.6f, 0), -n, 4.2f, 1.6f, 0.6f), p);
        });
        if (p.Slot == Slot.Main && !p.Hidden)
        {
            // The gable over the middle of the roof, the club's disc on it.
            var mid = p.Path[p.Mid];
            var s = p.Sec[p.Mid];
            var face = new Vector3(-mid.NX, 0, -mid.NZ);
            var basis = Parts.Facing(face);
            m.Hex(0xe9e1cc);
            m.Prism(new Transform3D(basis, mid.At(3.4f, s.Top)), new Vector2[] { new(-8, 0), new(8, 0), new(0, 5) }, 2.6f);
            m.Hex(RoofCol);
            m.Prism(new Transform3D(basis, mid.At(7.5f, s.Top + 0.6f)), new Vector2[] { new(-8.4f, 0), new(8.4f, 0), new(0, 5.4f) }, 7);
            var c = mid.At(2.05f, s.Top + 1.9f);
            Parts.Disc(m, c, face, 1.5f, 0xe9e1cc);
            Parts.Disc(m, c + face * 0.02f, face, 1.25f, g.Home);
        }
    }
}

/// <summary>
/// Curva: the Italian concrete bowl. Two open tiers, an arcade of vomitories between them,
/// ochre arches all round the outside. Behind the goals it's the ultras' curva (striped card
/// displays); the main stand gets a thin coffered canopy on fins; the corners stand spiral
/// ramp towers with floodlight masts rising out of them.
/// </summary>
public sealed class Curva : StandSet
{
    const uint Travertine = 0xe4dac4, Ochre = 0xc98a52, Steel = 0x55595f;
    public override string Name => "Curva";
    public override string About => "Italian concrete: two open tiers, arches, spiral towers";
    public override uint Swatch => 0xe0a85e;
    public override uint[] Mains => new uint[] { Ochre, Travertine };
    public override float Natural(Kind k) => k == Kind.Side ? 26 : k == Kind.End ? 28 : 27;
    public override Vector2 Range => new(20, 32);
    public override (uint col, Look look) Cap => (Ochre, Look.Plain);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float ue = 21.5f + (T - 1.2f - 15.8f) / 0.72f;
        x.Top = T; x.Ue = ue; x.Back = ue + 1.2f; x.BackH = T; x.Edge = ue;
        x.To(new(22, 11.5f), Kit.Concrete)
            .To(new(22, 14.5f), Travertine, Look.Arcade, 3003)
            .To(new(21, 14.5f), Kit.Concrete)
            .To(new(21, 15.8f), g.WallCol, Look.Wall)
            .To(new(21.5f, 15.8f), Kit.Concrete)
            .To(new(ue, T - 1.2f), g.Seat, Look.Tier, 1, 6)
            .To(new(ue, T), Kit.Concrete)
            .To(new(ue + 1.2f, T), Kit.Concrete)
            .To(new(ue + 1.2f, 0), Ochre, Look.Arcade, 7006, flip: true);
        x.Fans(new(21.5f, 15.8f), new(ue, T - 1.2f), new TierFans { Fill = 0.86f });
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        var mid = p.Path[p.Mid];
        var ms = p.Sec[p.Mid];
        if (p.Kind == Kind.Corner)
        {
            if (p.Partial) return;
            // A spiral ramp tower out behind the corner, the mast rising out of it.
            var c = mid.At(ms.Back + 8, 0);
            float rim = ms.Top, head = Mathf.Max(rim + 20, 44);
            m.Hex(Ochre);
            m.Column(c, 3.2f, 3.2f, rim + 1, 10);
            m.Hex(Travertine);
            m.Column(c + new Vector3(0, rim + 1, 0), 3.8f, 3.8f, 0.7f, 10);
            Helix(m, c, 3.2f, 7, rim, 3.5f, Mathf.Atan2(-c.Z, -c.X));
            int i = 0;
            for (float y = rim + 1.7f; y < head - 2; y += 6, i++)
            {
                float w = 2.4f - i * 0.22f;
                m.Box(c + new Vector3(-w / 2, y, -w / 2), c + new Vector3(w / 2, Mathf.Min(y + 6, head - 2), w / 2));
            }
            var face = new Vector3(-c.X, 0, -c.Z).Normalized();
            g.AddLamp(Parts.LampBank(m, c + new Vector3(0, head + 1.5f, 0), face, 10, 5.4f, 0.45f), p);
            return;
        }
        // Slender masts behind the stand, a bank of lamps on each.
        p.Spread(2, s => new(s.Back + 2, 0), (pos, n, s) =>
        {
            float head = s.Top + 18;
            m.Hex(Steel);
            m.Column(pos, 0.9f, 0.45f, head, 6);
            g.AddLamp(Parts.LampBank(m, pos + new Vector3(0, head + 1.6f, 0) - n * 0.6f, -n, 6, 3.2f, 0.5f), p);
        });
        if (p.Slot == Slot.Main && !p.Hidden)
        {
            // The Tribuna's canopy: a thin coffered slab out over the upper tier, on fins.
            float back = ms.Back, edge = 9;
            float Under(float o) => ms.Top + 5 + (back - o) / (back - edge) * 1.6f;
            float Over(float o) => Under(o) + 0.5f + 0.5f * (o - edge) / (back - edge);
            var a = p.Path[0]; var b = p.Path[^1];
            var strip = new List<PathPt> { a, b };
            m.Hex(0xe6b98c, Look.Coffer);
            Bowl.Strip(m, strip, new(back, Under(back)), new(edge, Under(edge)));
            m.Hex(0xffffff, Look.Fascia);
            Bowl.Strip(m, strip, new(edge, Under(edge)), new(edge, Over(edge)));
            m.Hex(Travertine);
            Bowl.Strip(m, strip, new(edge, Over(edge)), new(back, Over(back)));
            Bowl.Strip(m, strip, new(back, Over(back)), new(back, ms.Top));
            Bowl.Caps(m, new[] { a, b }, new Vector2[] { new(back, Under(back)), new(back, Over(back)), new(edge, Over(edge)), new(edge, Under(edge)) });
            var fin = new Vector2[]
            {
                new(back, 0), new(back + 4.8f, 0), new(back + 0.8f, Over(back)), new(edge + 0.6f, Over(edge) - 0.1f),
                new(edge + 0.6f, Under(edge) - 0.3f), new(back - 2, Under(back - 2) - 2.1f), new(back, Under(back) - 2.1f),
            };
            float len = new Vector2(b.X - a.X, b.Z - a.Z).Length();
            int n = Mathf.RoundToInt(len / 6.4f);
            var basis = new Basis(new Vector3(a.NX, 0, a.NZ), Vector3.Up, new Vector3(-a.NZ, 0, a.NX));
            m.Hex(0xd8c9a8);
            for (int k = 0; k <= n; k++)
            {
                float f = 0.01f + 0.98f * k / n;
                m.Prism(new Transform3D(basis, new Vector3(a.X + (b.X - a.X) * f, 0, a.Z + (b.Z - a.Z) * f)), fin, 0.7f);
            }
        }
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
}

/// <summary>
/// The Wall: one enormous steep standing tier running straight on up from the lower one, the
/// way Dortmund's south stand does, under a flat box roof hung from towering lattice pylons in
/// the club's colour. Tallest of all behind the goals.
/// </summary>
public sealed class TheWall : StandSet
{
    const uint RoofCol = 0x2f343c, RoofTop = 0x5a6068, Panels = 0xb4bec6, Clad = 0x4b5159;
    public override string Name => "The Wall";
    public override string About => "One giant, steep standing tier under pylons and a box roof";
    public override uint Swatch => 0xffd447;
    public override uint[] Mains => new uint[] { Clad, RoofCol, RoofTop, Panels };
    public override float Natural(Kind k) => k == Kind.End ? 42 : 36;
    public override Vector2 Range => new(26, 46);
    public override (uint col, float par) Lower(BuiltGround g) => (g.Seat, 1);
    public override TierFans LowerFans() => new() { Fill = 0.97f, Shade = new(9, 17) };

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float uh = T - 7, ue = 21 + (uh - 12.6f) / 0.84f, back = ue + 1.5f;
        x.Top = T; x.Ue = ue; x.Back = back; x.BackH = T - 2.2f; x.Edge = 6;
        x.To(new(21, 11.5f), Kit.Concrete)
            .To(new(21, 12.6f), 0xffffff, Look.Ribbon)
            .To(new(ue, uh), g.Seat, Look.Tier, 1, 8)
            .To(new(ue, T - 2.2f), Kit.DarkConcrete)
            .To(new(back, T), Kit.DarkConcrete)
            .To(new(back, 0), Clad, Look.Roof, flip: true);
        x.Sheet(new(back, T - 2.2f), new(15, T - 2.2f), RoofCol, Look.Roof)
            .Sheet(new(15, T - 2.2f), new(6, T - 2.2f), Panels)
            .Sheet(new(6, T - 2.2f), new(6, T), 0xffffff, Look.Fascia)
            .Sheet(new(6, T), new(back, T), RoofTop);
        x.Fans(new(21, 12.6f), new(ue, uh), new TierFans { Fill = 0.98f, Shade = new(-2, 14) });
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        // Lamps along the roof's front.
        m.Hex(0xffffff, Look.Lamp);
        for (int i = 0; i < p.Path.Count; i += 2)
        {
            var pt = p.Path[i];
            var s = p.Sec[i];
            var c = pt.At(7.2f, s.Top - 2.75f);
            var basis = new Basis(Vector3.Up, pt.FacePitch + Mathf.Pi) * new Basis(Vector3.Right, 0.75f);
            var bx = basis.X * 1.7f; var by = basis.Y * 0.5f;
            m.QuadUV(c - bx - by, c + bx - by, c + bx + by, c - bx + by, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }
        p.Spread(Mathf.Max(1, Mathf.RoundToInt(p.Length / 28)), s => new(7, s.Top - 3), (pos, n, s) => g.AddLamp(pos, p));
        // The pylons: lattice towers behind the stand, the roof slung from their tops.
        if (p.Partial) return;
        uint col = g.Home;
        int count = p.Kind == Kind.Corner ? 1 : p.Length > 70 ? 3 : 2;
        p.Spread(count, s => new(s.Back + 3.2f, 0), (foot, n, s) =>
        {
            float head = s.Top + 12;
            Parts.Lattice(m, foot, head, 1.5f, 1.1f, col, 0.34f, 5);
            var top = foot + new Vector3(0, head, 0);
            var front = foot - n * (s.Back + 3.2f - 9) + new Vector3(0, s.Top + 0.2f, 0);
            var back = foot + new Vector3(0, s.Top + 0.2f, 0) - n * 3.2f;
            m.Hex(col);
            m.Beam(top, front, 0.5f, 0.7f);
            m.Beam(top, back, 0.5f, 0.7f);
            m.Beam(top + new Vector3(0, 0.6f, 0), top + new Vector3(0, 3, 0), 0.25f, 0.25f);
        });
    }
}

/// <summary>
/// Citadel: a fortress. Stone curtain walls with battlements and the club's pennants, arched
/// windows glowing at night, turrets with lamps along the walls and great round keeps with
/// conical roofs in the corners.
/// </summary>
public sealed class Citadel : StandSet
{
    const uint Stone = 0x9a958a, StoneTop = 0xb5b0a3, Base = 0x7f7a70;
    public override string Name => "Citadel";
    public override string About => "A fortress: stone walls, battlements, banners, round keeps";
    public override uint Swatch => 0x9a958a;
    public override uint[] Mains => new uint[] { Stone, StoneTop, Base };
    public override float Natural(Kind k) => k == Kind.End ? 27 : k == Kind.Side ? 24 : 26;
    public override Vector2 Range => new(22, 34);
    public override (uint col, Look look) Front(BuiltGround g) => (Stone, Look.Brick);
    public override (uint col, Look look) Cap => (Stone, Look.Brick);

    public override void Upper(Section x, float T, Kind kind, BuiltGround g)
    {
        float uh = T - 4, ue = 21.7f + (uh - 16) / 0.8f, wb = ue + 3;
        x.Top = T; x.Ue = ue; x.Back = wb + 1; x.BackH = T + 1.4f; x.Edge = wb;
        x.To(new(22, 11.5f), StoneTop)
            .To(new(22, 14.8f), Stone, Look.Arcade, 3304)
            .To(new(21.2f, 14.8f), StoneTop)
            .To(new(21.2f, 16), Stone, Look.Brick)
            .To(new(21.7f, 16), StoneTop)
            .To(new(ue, uh), g.Seat, Look.Tier, 1, 6)
            .To(new(ue, T), Stone, Look.Brick)
            .To(new(wb, T), StoneTop)
            .To(new(wb, T + 1.4f), Stone, Look.Brick)
            .To(new(wb + 1, T + 1.4f), StoneTop)
            .To(new(wb + 1, 6), Stone, Look.Arcade, 5005, flip: true)
            .To(new(wb + 3, 0), Base, Look.Brick, flip: true);
        x.Fans(new(21.7f, 16), new(ue, uh), new TierFans { Fill = 0.9f });
    }

    public override void Dress(Piece p, BuiltGround g)
    {
        var m = p.Mesh(g);
        // Battlements along the parapet.
        m.Hex(Stone, Look.Brick);
        p.Each(2.4f, s => new(s.Edge + 0.5f, s.Top + 2.05f), (pos, n, s) =>
            m.Box(new Transform3D(Parts.Facing(n), pos), new Vector3(1.2f, 1.3f, 1f)));
        // The club's pennants along the wall walk.
        int k = 0;
        p.Each(13, s => new(s.Edge - 0.6f, s.Top), (pos, n, s) =>
            Parts.Flag(m, pos, 6.5f, new Vector3(-n.Z, 0, n.X), 2.6f, 1.6f, k++ % 2 == 0 ? g.Home : 0xf3eee2), 4);
        var mid = p.Path[p.Mid];
        var ms = p.Sec[p.Mid];
        if (p.Kind == Kind.Corner)
        {
            if (p.Partial) return;
            // The keep: a round tower out on the corner, a conical roof in the club's colour.
            var c = mid.At(ms.Back + 4.5f, 0);
            float h = ms.Top + 10, r = 6.5f;
            m.Hex(Stone, Look.Arcade, 4004);
            m.Column(c, r + 0.6f, r, h, 14);
            Parts.MerlonRing(m, c, r + 0.2f, h, 14, Stone);
            m.Hex(g.Home);
            m.Column(c + new Vector3(0, h + 0.2f, 0), r - 0.4f, 0, 9, 14);
            var face = new Vector3(-mid.NX, 0, -mid.NZ);
            Parts.Flag(m, c + new Vector3(0, h + 8.6f, 0), 4.5f, new Vector3(-face.Z, 0, face.X), 3.4f, 2.1f, g.Home);
            g.AddLamp(Parts.LampBank(m, c + face * (r + 0.4f) + new Vector3(0, h - 3, 0), face, 7, 2.8f, 0.5f), p);
            return;
        }
        // Turrets along the wall, a lamp bank on each.
        p.Spread(p.Length > 70 ? 3 : 2, s => new(s.Edge + 0.5f, 0), (pos, n, s) =>
        {
            float h = s.Top + 5;
            var basis = Parts.Facing(n);
            m.Hex(Stone, Look.Brick);
            m.Box(new Transform3D(basis, pos + new Vector3(0, h / 2, 0)), new Vector3(4.6f, h, 4.6f));
            foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                m.Box(new Transform3D(basis, pos + basis * new Vector3(sx * 1.8f, h + 0.65f, sz * 1.8f)), new Vector3(1, 1.3f, 1));
            m.Hex(g.Home);
            m.Column(pos + new Vector3(0, h + 1.3f, 0), 2.6f, 0, 4.2f, 4, Mathf.Atan2(n.Z, n.X) + Mathf.Pi / 4);
            g.AddLamp(Parts.LampBank(m, pos - n * 2.5f + new Vector3(0, h - 1.4f, 0), -n, 4, 1.8f, 0.55f), p);
        });
    }
}
