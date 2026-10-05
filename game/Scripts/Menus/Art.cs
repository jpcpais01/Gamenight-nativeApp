using System;
using Godot;
using GameNight.Club;
using GameNight.Sim;
using Pos = GameNight.Club.Position;

namespace GameNight.Menus;

/// <summary>The menus' pictures: player portraits, cards, crests and kits, drawn as flat shapes.</summary>
public static class Art
{
    static Vector2[] Pts(Rect2 r, params float[] xy)
    {
        var p = new Vector2[xy.Length / 2];
        for (int i = 0; i < p.Length; i++) p[i] = r.Position + new Vector2(xy[i * 2] * r.Size.X / 100f, xy[i * 2 + 1] * r.Size.Y / 100f);
        return p;
    }

    static void Poly(CanvasItem ci, Rect2 r, Color c, params float[] xy) => ci.DrawColoredPolygon(Pts(r, xy), c);

    static void Oval(CanvasItem ci, Rect2 r, float cx, float cy, float rx, float ry, Color c)
    {
        var k = r.Size / 100f;
        ci.DrawColoredPolygon(Px.Ellipse(r.Position + new Vector2(cx * k.X, cy * k.Y), rx * k.X, ry * k.Y), c);
    }

    // ---------------------------------------------------------------- portrait

    /// <summary>Little portrait in a 100 x 100 box: kit, skin, hair. Same palette as the 3D player.</summary>
    public static void Avatar(CanvasItem ci, Rect2 r, int skin, int hair, int hairStyle, int shirt, int trim)
    {
        int sk = TeamData.SkinTones[Math.Clamp(skin, 0, TeamData.SkinTones.Length - 1)];
        int hc = TeamData.HairColors[Math.Clamp(hair, 0, TeamData.HairColors.Length - 1)];
        var s = Px.Hex(sk);
        var sd = Px.Shade(sk, 0.82f);
        var h = Px.Hex(hc);
        Poly(ci, r, Px.Hex(shirt), 10, 100, 12, 80, 20, 72, 34, 68, 50, 76, 66, 68, 80, 72, 88, 80, 90, 100);
        Poly(ci, r, Px.Hex(trim), 34, 68, 50, 76, 66, 68, 62, 66, 50, 72, 38, 66);
        Poly(ci, r, new Color(0, 0, 0, 0.12f), 10, 100, 12, 80, 24, 72, 26, 100);
        ci.DrawRect(new Rect2(r.Position + new Vector2(42, 54) * r.Size / 100f, new Vector2(16, 17) * r.Size / 100f), sd);
        Oval(ci, r, 31, 44, 3.5f, 5, sd);
        Oval(ci, r, 69, 44, 3.5f, 5, sd);
        Oval(ci, r, 50, 42, 19, 22, s);
        switch (hairStyle % 4)
        {
            case 0: // short crop
                Poly(ci, r, h, 30, 40, 30, 28, 36, 20, 50, 17, 64, 20, 70, 28, 70, 40, 66, 31, 50, 29, 34, 31);
                break;
            case 1: // buzz
                Poly(ci, r, new Color(h, 0.85f), 32, 38, 33, 27, 39, 22, 50, 20, 61, 22, 67, 27, 68, 38, 64, 29, 50, 27, 36, 29);
                break;
            case 2: // curls
                foreach (var (cx, cy, rr) in new[] { (36f, 28f, 9f), (50f, 21f, 11f), (64f, 28f, 9f), (30f, 38f, 6f), (70f, 38f, 6f), (43f, 23f, 9f), (57f, 23f, 9f) })
                    Oval(ci, r, cx, cy, rr, rr, h);
                break;
            default: // swept quiff
                Poly(ci, r, h, 28, 46, 26, 30, 34, 18, 52, 14, 66, 17, 73, 28, 72, 42, 70, 30, 60, 26, 55, 33, 38, 31, 31, 36);
                Poly(ci, r, h, 28, 44, 27, 56, 31, 60, 33, 46);
                Poly(ci, r, h, 72, 42, 73, 56, 69, 60, 67, 46);
                break;
        }
        var brow = Px.Shade(hc, 0.9f);
        var k = r.Size / 100f;
        ci.DrawRect(new Rect2(r.Position + new Vector2(40, 38) * k, new Vector2(7, 2.2f) * k), brow);
        ci.DrawRect(new Rect2(r.Position + new Vector2(53, 38) * k, new Vector2(7, 2.2f) * k), brow);
        var eye = Px.Hex(0x1b1410);
        ci.DrawRect(new Rect2(r.Position + new Vector2(41.6f, 42.2f) * k, new Vector2(3.8f, 3.8f) * k), eye);
        ci.DrawRect(new Rect2(r.Position + new Vector2(54.6f, 42.2f) * k, new Vector2(3.8f, 3.8f) * k), eye);
        ci.DrawRect(new Rect2(r.Position + new Vector2(45, 54) * k, new Vector2(10, 1.8f) * k), sd);
    }

    public static void Avatar(CanvasItem ci, Rect2 r, Card c, Kit kit) =>
        Avatar(ci, r, c.Skin, c.Hair, c.HairStyle, c.Position == Pos.GK ? kit.GkShirt : kit.Shirt, c.Position == Pos.GK ? kit.GkShorts : kit.Shirt2);

    // ---------------------------------------------------------------- cards

    public static Color RarityColor(Rarity r) => Px.Hex(Cards.RarityColor[(int)r]);

    /// <summary>Ink that reads on the card face.</summary>
    static Color CardInk(Rarity r) => r switch
    {
        Rarity.Common => Px.Hex(0x2e1806),
        Rarity.Rare => Px.Hex(0x1b2230),
        Rarity.Epic => Px.Hex(0x3a2604),
        Rarity.Legendary => Px.Hex(0xf6ecff),
        _ => Px.Hex(0x2a2410),
    };

    /// <summary>Each tier's finish: face bands top to bottom, the trim, and the accent.</summary>
    static readonly int[][] Face =
    {
        new[] { 0xf0bb85, 0xd99559, 0xb8733c, 0x8f5427, 0x6e3d18 },
        new[] { 0xffffff, 0xe3e9f1, 0xc5cfdb, 0xa2adbd, 0x7f8a9c },
        new[] { 0xfff6b8, 0xffe066, 0xf2bd2c, 0xd09418, 0xa8700c },
        new[] { 0x5a2a94, 0x3d1a6e, 0x2a0f52, 0x1c0838, 0x12052a },
        new[] { 0xffffff, 0xfffbea, 0xf6eccb, 0xe8d8a2, 0xd9c27a },
    };
    static readonly int[] Trim = { 0x5a3112, 0x5b6676, 0x8a5a06, 0xc27bff, 0xc9a23a };
    static readonly int[] Accent = { 0xffd7a8, 0xffffff, 0xfff3a0, 0xe0b0ff, 0x7ff6ff };

    /// <summary>The card's silhouette (unit coordinates, 10 x 14 card): plainer for the common
    /// tiers, crowned for the great ones.</summary>
    static readonly float[][] Shapes =
    {
        new[] { 0.07f, 0, 0.93f, 0, 1, 0.045f, 1, 0.9f, 0.5f, 1, 0, 0.9f, 0, 0.045f },
        new[] { 0, 0.06f, 0.2f, 0.02f, 0.5f, 0, 0.8f, 0.02f, 1, 0.06f, 1, 0.88f, 0.5f, 1, 0, 0.88f },
        new[] { 0, 0.075f, 0.1f, 0.03f, 0.36f, 0.03f, 0.5f, 0, 0.64f, 0.03f, 0.9f, 0.03f, 1, 0.075f, 1, 0.87f, 0.5f, 1, 0, 0.87f },
        new[] { 0, 0.095f, 0.12f, 0.02f, 0.28f, 0.065f, 0.5f, 0, 0.72f, 0.065f, 0.88f, 0.02f, 1, 0.095f, 1, 0.86f, 0.62f, 0.965f, 0.5f, 1, 0.38f, 0.965f, 0, 0.86f },
        new[] { 0, 0.11f, 0.08f, 0.03f, 0.22f, 0.06f, 0.36f, 0, 0.5f, 0.045f, 0.64f, 0, 0.78f, 0.06f, 0.92f, 0.03f, 1, 0.11f, 1, 0.86f, 0.5f, 1, 0, 0.86f },
    };

    static Vector2[] Shape(Rect2 r, int tier)
    {
        var n = Shapes[Math.Clamp(tier, 0, 4)];
        var pts = new Vector2[n.Length / 2];
        for (int i = 0; i < pts.Length; i++) pts[i] = r.Position + new Vector2(n[i * 2], n[i * 2 + 1]) * r.Size;
        return pts;
    }

    static float Area(Vector2[] p)
    {
        float a = 0;
        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++) a += p[j].X * p[i].Y - p[i].X * p[j].Y;
        return Mathf.Abs(a) / 2;
    }

    /// <summary>Fill the part of poly that lies inside the card's shape.</summary>
    static void Clip(CanvasItem ci, Vector2[] poly, Vector2[] shape, Color c)
    {
        foreach (var part in Geometry2D.IntersectPolygons(poly, shape))
            if (part.Length >= 3 && Area(part) > 0.5f) ci.DrawColoredPolygon(part, c);
    }

    static Vector2[] Quad(float x0, float y0, float x1, float y1) => new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };

    /// <summary>A full player card in the FUT manner; `r` should be about 10 x 14. Rating and
    /// position top left over the nation and club badges, the portrait, the name plate, six
    /// stats, playstyle icons down the right, and a finish (silhouette, colours, texture) per tier.</summary>
    public static void Card(CanvasItem ci, Rect2 r, Card c, Kit kit, float glow = 0, Crest crest = null)
    {
        float u = r.Size.X / 10f;
        int tier = (int)c.Rarity;
        var face = Face[tier];
        var trim = Px.Hex(Trim[tier]);
        var acc = Px.Hex(Accent[tier]);
        var col = RarityColor(c.Rarity);
        var outer = Shape(r, tier);
        if (glow > 0)
            for (int i = 3; i >= 1; i--) ci.DrawColoredPolygon(Shape(r.Grow(u * 0.45f * i * glow), tier), new Color(col, 0.12f * glow));
        ci.DrawColoredPolygon(Shape(r.Translated(new Vector2(u * 0.35f, u * 0.45f)), tier), new Color(0, 0, 0, 0.5f));
        ci.DrawColoredPolygon(outer, trim.Darkened(0.35f));
        float bw = Mathf.Max(2, u * 0.32f);
        var ir = r.Grow(-bw);
        var shape = Shape(ir, tier);
        ci.DrawColoredPolygon(shape, Px.Hex(face[2]));
        // The face: hard bands, top to bottom.
        float[] stops = { 0, 0.14f, 0.36f, 0.64f, 0.84f, 1.01f };
        for (int i = 0; i < 5; i++)
            Clip(ci, Quad(ir.Position.X - 1, ir.Position.Y + ir.Size.Y * stops[i], ir.End.X + 1, ir.Position.Y + ir.Size.Y * stops[i + 1]), shape, Px.Hex(face[i]));
        var p = ir.Position;
        Vector2 U(float x, float y) => p + new Vector2(x, y) * u;

        // The tier's texture.
        switch (tier)
        {
            case 0:
                for (float y = 1; y < 14; y += 0.55f) Clip(ci, Quad(p.X, p.Y + y * u, ir.End.X, p.Y + y * u + Mathf.Max(1, u * 0.08f)), shape, new Color(0, 0, 0, 0.07f));
                break;
            case 1:
                for (int i = 0; i < 4; i++)
                {
                    float x = 1 + i * 3.1f;
                    Clip(ci, new[] { U(x, 0), U(x + 0.9f, 0), U(x - 3.2f, 14), U(x - 4.1f, 14) }, shape, new Color(1, 1, 1, 0.16f));
                }
                break;
            case 2:
                for (float d = -14; d < 10; d += 1.2f)
                {
                    Clip(ci, new[] { U(d, 0), U(d + 0.12f, 0), U(d + 14.12f, 14), U(d + 14, 14) }, shape, new Color(1, 1, 1, 0.1f));
                    Clip(ci, new[] { U(d + 14, 0), U(d + 14.12f, 0), U(d + 0.12f, 14), U(d, 14) }, shape, new Color(0.5f, 0.3f, 0, 0.08f));
                }
                break;
            case 3:
                // Neon geometry over the dark.
                foreach (var (x0, y0, x1, y1) in new[] { (0f, 9.5f, 6f, 3f), (10f, 9.5f, 4f, 3f), (0f, 12f, 10f, 5f), (10f, 12f, 0f, 5f) })
                {
                    var a = U(x0, y0);
                    var b = U(x1, y1);
                    var n = (b - a).Normalized().Orthogonal() * Mathf.Max(1, u * 0.09f);
                    Clip(ci, new[] { a - n, b - n, b + n, a + n }, shape, new Color(acc, 0.35f));
                }
                Clip(ci, Px.Ellipse(U(6.5f, 4.5f), u * 4, u * 4, 24), shape, new Color(acc, 0.1f));
                break;
            default:
                // Rays from behind the portrait.
                for (int i = 0; i < 14; i++)
                {
                    float a0 = i * Mathf.Tau / 14, a1 = a0 + Mathf.Tau / 28;
                    var o = U(6.4f, 4.6f);
                    Clip(ci, new[] { o, o + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * u * 16, o + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * u * 16 }, shape, new Color(1, 0.92f, 0.6f, 0.22f));
                }
                for (int i = 0; i < 7; i++)
                    ci.DrawRect(new Rect2(U((i * 37 % 9) + 0.5f, (i * 23 % 11) + 1.2f), new Vector2(u * 0.25f, u * 0.25f)), new Color(acc, 0.7f));
                break;
        }
        // The inner trim line, following the silhouette.
        var line = Shape(ir.Grow(-u * 0.35f), tier);
        var closed = new Vector2[line.Length + 1];
        Array.Copy(line, closed, line.Length);
        closed[^1] = line[0];
        ci.DrawPolyline(closed, new Color(tier == 3 ? acc : trim, tier >= 2 ? 0.85f : 0.45f), Mathf.Max(1, u * 0.12f));

        var ink = CardInk(c.Rarity);
        // The portrait, on a soft halo.
        ci.DrawColoredPolygon(Px.Ellipse(U(6.5f, 5.1f), u * 2.9f, u * 2.9f, 22), new Color(acc, tier == 3 ? 0.14f : 0.22f));
        Avatar(ci, new Rect2(U(3.6f, 1.85f), new Vector2(u * 6.1f, u * 6.1f)), c, kit);
        // Rating, position, nation and club down the left.
        Px.TextC(ci, Px.Big, p.X + u * 2.0f, p.Y + u * 3.7f, c.Overall.ToString(), (int)(u * 3.2f), ink);
        Px.TextC(ci, Px.Big, p.X + u * 2.0f, p.Y + u * 5.0f, c.Position.ToString(), (int)(u * 1.4f), ink);
        ci.DrawRect(new Rect2(U(1.3f, 5.4f), new Vector2(u * 1.4f, Mathf.Max(1, u * 0.1f))), new Color(ink, 0.45f));
        var fr = new Rect2(U(1.25f, 5.75f), new Vector2(u * 1.5f, u * 1.0f));
        ci.DrawRect(fr.Grow(Mathf.Max(1, u * 0.08f)), new Color(0, 0, 0, 0.35f));
        Px.Flag(ci, fr, Cards.Nations[c.Nation]);
        if (crest != null) CrestArt.Draw(ci, new Rect2(U(1.35f, 7.0f), new Vector2(u * 1.3f, u * 1.6f)), crest);
        // Playstyles down the right edge.
        var ps = Playstyles.Of(c);
        for (int i = 0; i < ps.Count; i++)
            Playstyle(ci, U(8.75f, 2.6f + i * 1.95f), Mathf.Max(5, u * 0.9f), ps[i]);
        // The name plate.
        var plate = new Rect2(U(0.3f, 7.95f), new Vector2(ir.Size.X - u * 0.6f, u * 1.4f));
        ci.DrawRect(plate, tier == 3 ? new Color(0, 0, 0, 0.35f) : new Color(ink, 0.12f));
        ci.DrawRect(new Rect2(plate.Position, new Vector2(plate.Size.X, Mathf.Max(1, u * 0.1f))), new Color(tier == 3 ? acc : trim, 0.7f));
        ci.DrawRect(new Rect2(plate.Position.X, plate.End.Y, plate.Size.X, Mathf.Max(1, u * 0.1f)), new Color(tier == 3 ? acc : trim, 0.7f));
        int ns = (int)(u * 1.5f);
        Px.TextC(ci, Px.Big, plate.GetCenter().X, plate.GetCenter().Y + ns * 0.36f, Px.Fit(Px.Big, c.LastName.ToUpperInvariant(), ns, plate.Size.X - u * 0.6f), ns, ink);
        // Six stats, two columns of three, split by a rule.
        var fs = Cards.FaceStats(c);
        int vs = (int)(u * 1.2f), ls = (int)(u * 0.9f);
        for (int i = 0; i < 6; i++)
        {
            float x = p.X + u * (i < 3 ? 1.35f : 5.55f);
            float y = p.Y + u * (10.35f + (i % 3) * 0.98f);
            string v = fs[i].Item2.ToString();
            Px.Text(ci, Px.Big, new Vector2(x, y), v, vs, ink);
            Px.Text(ci, Px.Big, new Vector2(x + u * 1.75f, y), fs[i].Item1, ls, new Color(ink, 0.75f));
        }
        ci.DrawRect(new Rect2(U(4.95f, 9.6f), new Vector2(Mathf.Max(1, u * 0.1f), u * 2.75f)), new Color(ink, 0.3f));
        if (u >= 6) Px.TextC(ci, Px.Small, ir.GetCenter().X, p.Y + u * 12.95f, Cards.Label(c.Rarity).ToUpperInvariant(), Math.Max(7, (int)(u * 0.6f)), new Color(ink, 0.7f));
    }

    /// <summary>Face-down card back.</summary>
    /// <summary>A playstyle badge: a hexagon in its colour with the style's own pictogram inside.</summary>
    public static void Playstyle(CanvasItem ci, Vector2 c, float r, Playstyle p, bool letters = true)
    {
        Vector2[] Hex(float rr)
        {
            var pts = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Pi / 6 + i * Mathf.Pi / 3;
                pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
            }
            return pts;
        }
        var col = Px.Hex(p.Color);
        ci.DrawColoredPolygon(Hex(r + Mathf.Max(1, r * 0.14f)), new Color(0, 0, 0, 0.75f));
        ci.DrawColoredPolygon(Hex(r), col);
        ci.DrawColoredPolygon(Hex(r * 0.8f), Px.Hex(0x14121c));
        if (r >= 4.5f) Glyph(ci, c, r * 0.58f, p.Id, col.Lightened(0.15f));
        else ci.DrawColoredPolygon(Hex(r * 0.36f), col);
    }

    /// <summary>The pictogram of a playstyle, drawn in a box of half-size s around c.</summary>
    public static void Glyph(CanvasItem ci, Vector2 c, float s, string id, Color col)
    {
        Vector2 P(float x, float y) => c + new Vector2(x, y) * s;
        void Poly(params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = P(xy[i * 2], xy[i * 2 + 1]);
            ci.DrawColoredPolygon(pts, col);
        }
        void Box(float x, float y, float w, float h) => ci.DrawRect(new Rect2(P(x, y), new Vector2(w, h) * s), col);
        float lw = Mathf.Max(1, s * 0.28f);
        void Line(float x0, float y0, float x1, float y1) => ci.DrawLine(P(x0, y0), P(x1, y1), col, lw);
        void Arc(float x, float y, float rr, float a0, float a1) => ci.DrawArc(P(x, y), rr * s, a0, a1, 12, col, lw);
        void Disc(float x, float y, float rr) => ci.DrawColoredPolygon(Px.Ellipse(P(x, y), rr * s, rr * s, 12), col);
        var dark = Px.Hex(0x14121c);
        switch (id)
        {
            case "tank": Poly(-0.8f, -0.85f, 0.8f, -0.85f, 0.8f, 0.05f, 0, 0.95f, -0.8f, 0.05f); break; // shield
            case "rapid": Poly(0.25f, -1, -0.65f, 0.15f, -0.05f, 0.15f, -0.3f, 1, 0.65f, -0.2f, 0.05f, -0.2f); break; // bolt
            case "quickstep": // two chevrons
                Poly(-0.95f, -0.8f, -0.45f, -0.8f, 0.05f, 0, -0.45f, 0.8f, -0.95f, 0.8f, -0.45f, 0);
                Poly(-0.05f, -0.8f, 0.45f, -0.8f, 0.95f, 0, 0.45f, 0.8f, -0.05f, 0.8f, 0.45f, 0);
                break;
            case "engine": // a heart
                Disc(-0.42f, -0.28f, 0.48f);
                Disc(0.42f, -0.28f, 0.48f);
                Poly(-0.88f, -0.1f, 0.88f, -0.1f, 0, 0.95f);
                break;
            case "finesse": // curling arrow
                Arc(0.15f, 0.55f, 0.95f, Mathf.Pi * 1.05f, Mathf.Pi * 1.62f);
                Poly(0.3f, -0.75f, 0.95f, -0.42f, 0.3f, -0.05f);
                break;
            case "powershot": // a ball with speed lines
                Disc(0.35f, 0, 0.6f);
                ci.DrawColoredPolygon(Px.Ellipse(P(0.35f, 0), 0.24f * s, 0.24f * s, 5), dark);
                Box(-1, -0.45f, 0.6f, 0.2f);
                Box(-1, -0.1f, 0.8f, 0.2f);
                Box(-1, 0.25f, 0.6f, 0.2f);
                break;
            case "maestro": // an eye
                ci.DrawColoredPolygon(Px.Ellipse(c, s, s * 0.55f, 14), col);
                Disc(0, 0, 0.42f);
                ci.DrawColoredPolygon(Px.Ellipse(c, 0.42f * s, 0.42f * s, 10), dark);
                Disc(0, 0, 0.2f);
                break;
            case "technician": // the ball at the boot
                Disc(0, -0.1f, 0.62f);
                ci.DrawColoredPolygon(Px.Ellipse(P(0, -0.1f), 0.25f * s, 0.25f * s, 5), dark);
                Box(-0.95f, 0.65f, 1.9f, 0.28f);
                break;
            case "aerial": // up over a bar
                Poly(0, -1, 0.8f, -0.15f, 0.3f, -0.15f, 0.3f, 0.35f, -0.3f, 0.35f, -0.3f, -0.15f, -0.8f, -0.15f);
                Box(-0.9f, 0.6f, 1.8f, 0.3f);
                break;
            case "intercept": // an arrow cut by a wall
                Box(-1, -0.12f, 1.2f, 0.24f);
                Poly(0.2f, -0.45f, 0.55f, 0, 0.2f, 0.45f);
                Box(0.62f, -0.95f, 0.3f, 1.9f);
                break;
            case "bruiser": // a fist
                Box(-0.75f, -0.6f, 1.4f, 1.1f);
                for (int i = 0; i < 3; i++) Box(-0.75f + i * 0.47f, -0.62f, 0.06f, 0.5f);
                Box(-0.45f, 0.5f, 0.8f, 0.45f);
                break;
            case "anticipate": // a clock
                Arc(0, 0, 0.82f, 0, Mathf.Tau);
                Line(0, 0, 0, -0.55f);
                Line(0, 0, 0.4f, 0.15f);
                break;
            case "trickster": // a zigzag run
                ci.DrawPolyline(new[] { P(-0.95f, 0.7f), P(-0.45f, -0.6f), P(0.05f, 0.6f), P(0.55f, -0.6f) }, col, lw);
                Poly(0.35f, -0.85f, 0.95f, -0.95f, 0.8f, -0.35f);
                break;
            case "poacher": // crosshair
                Arc(0, 0, 0.72f, 0, Mathf.Tau);
                Disc(0, 0, 0.22f);
                Line(0, -1, 0, -0.45f);
                Line(0, 0.45f, 0, 1);
                Line(-1, 0, -0.45f, 0);
                Line(0.45f, 0, 1, 0);
                break;
            case "longball": // a dotted lob
                for (int i = 0; i < 4; i++)
                {
                    float x = -0.9f + i * 0.45f;
                    Disc(x, 0.6f - Mathf.Sin((i + 0.5f) / 4.5f * Mathf.Pi) * 1.3f, 0.16f);
                }
                Disc(0.75f, 0.55f, 0.28f);
                break;
            case "relentless": // a flame
                Poly(0, -1, 0.65f, -0.1f, 0.6f, 0.55f, 0.2f, 0.95f, -0.2f, 0.95f, -0.6f, 0.55f, -0.65f, -0.05f, -0.3f, 0.1f);
                Poly(0, 0.05f, 0.25f, 0.5f, 0, 0.8f, -0.25f, 0.5f);
                break;
            case "whirlwind": // a twister
                Box(-0.95f, -0.85f, 1.9f, 0.28f);
                Box(-0.6f, -0.4f, 1.3f, 0.28f);
                Box(-0.35f, 0.05f, 0.8f, 0.28f);
                Box(-0.1f, 0.5f, 0.4f, 0.28f);
                break;
            case "cat": // a cat's head
                Poly(-0.85f, -0.95f, -0.25f, -0.45f, 0.25f, -0.45f, 0.85f, -0.95f, 0.85f, 0.35f, 0.4f, 0.85f, -0.4f, 0.85f, -0.85f, 0.35f);
                Box(-0.45f, -0.05f, 0.25f, 0.25f);
                Box(0.2f, -0.05f, 0.25f, 0.25f);
                ci.DrawRect(new Rect2(P(-0.45f, -0.05f), new Vector2(0.25f, 0.25f) * s), dark);
                ci.DrawRect(new Rect2(P(0.2f, -0.05f), new Vector2(0.25f, 0.25f) * s), dark);
                break;
            case "rushout": // out off the line
                Box(-0.95f, 0.65f, 1.9f, 0.28f);
                Poly(0, -1, 0.75f, -0.2f, 0.28f, -0.2f, 0.28f, 0.45f, -0.28f, 0.45f, -0.28f, -0.2f, -0.75f, -0.2f);
                break;
            case "farreach": // a hand at full stretch
                Box(-0.55f, -0.15f, 1.1f, 1.05f);
                for (int i = 0; i < 4; i++) Box(-0.55f + i * 0.3f, -0.95f + Math.Abs(i - 1.5f) * 0.15f, 0.22f, 0.85f);
                Poly(0.55f, 0.2f, 0.95f, -0.25f, 1, 0.05f, 0.55f, 0.6f);
                break;
            default: Disc(0, 0, 0.5f); break;
        }
    }

    public static void CardBack(CanvasItem ci, Rect2 r, Rarity rarity)
    {
        float u = r.Size.X / 10f;
        var col = RarityColor(rarity);
        Px.Frame(ci, r, Px.Night2, col.Darkened(0.3f), new Color(0, 0, 0, 0.5f), Math.Max(2, (int)(u * 0.3f)), Math.Max(3, (int)(u * 0.5f)));
        var inner = r.Grow(-u * 0.8f);
        Px.Bands(ci, inner, new[] { Px.Night3, Px.Night2, Px.Night, Px.Hex(0x0d0b2a) }, new[] { 0, 0.25f, 0.6f, 0.85f });
        // A lattice of diamonds in the rarity's colour, brightest in the middle.
        var c = inner.GetCenter();
        float step = u * 1.6f;
        for (float y = inner.Position.Y + step / 2; y < inner.End.Y; y += step)
            for (float x = inner.Position.X + step / 2; x < inner.End.X; x += step)
            {
                float d = Mathf.Clamp(1 - (new Vector2(x, y) - c).Length() / (inner.Size.Y * 0.6f), 0, 1);
                float s = u * (0.2f + d * 0.35f);
                ci.DrawColoredPolygon(new[] { new Vector2(x, y - s), new Vector2(x + s, y), new Vector2(x, y + s), new Vector2(x - s, y) }, new Color(col, 0.1f + d * 0.3f));
            }
        Px.Ring(ci, inner, new Color(col, 0.7f), Math.Max(1, (int)(u * 0.2f)));
        // The gem: a big diamond with the GN mark.
        float g = u * 3.2f;
        ci.DrawColoredPolygon(new[] { c + new Vector2(0, -g * 1.25f), c + new Vector2(g, 0), c + new Vector2(0, g * 1.25f), c + new Vector2(-g, 0) }, col.Darkened(0.45f));
        g -= u * 0.4f;
        ci.DrawColoredPolygon(new[] { c + new Vector2(0, -g * 1.25f), c + new Vector2(g, 0), c + new Vector2(0, g * 1.25f), c + new Vector2(-g, 0) }, col.Darkened(0.15f));
        ci.DrawColoredPolygon(new[] { c + new Vector2(0, -g * 1.25f), c + new Vector2(g, 0), c, c + new Vector2(-g, 0) }, new Color(1, 1, 1, 0.14f));
        Px.TextC(ci, Px.Big, c.X, c.Y + u * 0.75f, "GN", (int)(u * 2.2f), col.Luminance > 0.6f ? Px.Dark : Px.Ink, new Color(0, 0, 0, 0.35f), Mathf.Max(1, u * 0.2f));
    }

    /// <summary>Mini token for the tactics board (about 46 x 56 plus the name below).</summary>
    public static void Token(CanvasItem ci, Rect2 r, Card c, Pos slot, Kit kit, bool selected, bool captain)
    {
        if (selected) Px.Frame(ci, r.Grow(5), Colors.Transparent, Px.Cyan, null, 3, 0);
        if (c == null)
        {
            Px.Frame(ci, r, new Color(1, 1, 1, 0.08f), new Color(1, 1, 1, 0.35f), null, 2, 0);
            Px.TextC(ci, Px.Big, r.GetCenter().X, r.GetCenter().Y + 8, "+", 26, Px.Ink);
            Px.TextC(ci, Px.Small, r.GetCenter().X, r.End.Y + 11, slot.ToString(), 8, Px.Ink, new Color(0, 0, 0, 0.7f), 1);
            return;
        }
        var col = RarityColor(c.Rarity);
        Px.Frame(ci, r, Colors.Transparent, col.Darkened(0.55f), new Color(0, 0, 0, 0.45f), 2, 3);
        Px.Bands(ci, r.Grow(-2), new[] { col.Lightened(0.3f), col, col.Darkened(0.25f) }, new[] { 0, 0.25f, 0.7f });
        Avatar(ci, new Rect2(r.Position + new Vector2(r.Size.X * 0.22f, r.Size.Y * 0.18f), new Vector2(r.Size.X * 0.76f, r.Size.X * 0.76f)), c, kit);
        double f = Cards.FitFactor(c.Position, slot);
        Px.Text(ci, Px.Big, r.Position + new Vector2(4, 17), Cards.RatingIn(c, slot).ToString(), 18, CardInk(c.Rarity));
        var fit = f == 1 ? Px.Win : f >= 0.85 ? Px.Gold : Px.Loss;
        ci.DrawRect(new Rect2(r.End.X - 9, r.Position.Y + 4, 5, 5), fit);
        string name = $"{slot} {c.LastName}";
        name = Px.Fit(Px.Small, name, 8, r.Size.X + 34);
        float w = Px.Width(Px.Small, name, 8) + 6;
        ci.DrawRect(new Rect2(r.GetCenter().X - w / 2, r.End.Y + 2, w, 12), new Color(0.05f, 0.04f, 0.15f, 0.85f));
        Px.TextC(ci, Px.Small, r.GetCenter().X, r.End.Y + 11, name, 8, Px.Ink);
        // Playstyles: little badges up the left edge.
        var ps = Playstyles.Of(c);
        for (int i = 0; i < ps.Count; i++)
            Playstyle(ci, new Vector2(r.Position.X + 2, r.End.Y - 7 - i * 15), 7, ps[i]);
        if (captain)
        {
            var cr = new Rect2(r.End.X - 10, r.End.Y - 14, 13, 13);
            ci.DrawRect(cr, Px.Gold);
            Px.TextC(ci, Px.Big, cr.GetCenter().X, cr.End.Y - 2, "C", 14, Px.Dark);
        }
    }

    // ---------------------------------------------------------------- crest and kit

    static readonly float[] Shield = { 50, 3, 94, 16, 93, 42, 86, 66, 72, 88, 50, 113, 28, 88, 14, 66, 7, 42, 6, 16 };
    static readonly float[] ShieldIn = { 50, 10, 87, 21, 86, 44, 79, 64, 67, 83, 50, 104, 33, 83, 21, 64, 14, 44, 13, 21 };

    /// <summary>Club crest in kit colours (r about 100 x 116).</summary>
    public static void Crest(CanvasItem ci, Rect2 r, int main, int second, string code)
    {
        var rr = new Rect2(r.Position, new Vector2(r.Size.X, r.Size.Y * 100f / 116f));
        Poly(ci, rr, Px.Hex(second), Shield);
        Poly(ci, rr, Px.Hex(main), ShieldIn);
        Poly(ci, rr, new Color(0, 0, 0, 0.14f), 50, 10, 50, 104, 33, 83, 21, 64, 14, 44, 13, 21);
        ci.DrawRect(new Rect2(rr.Position + new Vector2(24, 32) * rr.Size / 100f, new Vector2(52, 5) * rr.Size / 100f), Px.Hex(second));
        int size = Math.Max(8, (int)(rr.Size.X * 0.3f));
        var ink = Px.Hex(main).Luminance > 0.7f ? Px.Hex(0x14121c) : Colors.White;
        Px.TextC(ci, Px.Big, rr.Position.X + rr.Size.X / 2, rr.Position.Y + rr.Size.Y * 0.74f, code, size, ink);
    }

    /// <summary>Front view of shirt and shorts in a 100 x 130 box, with the kit's design.</summary>
    public static void Jersey(CanvasItem ci, Rect2 r, ClubKit k, Crest crest = null)
    {
        var box = new Rect2(r.Position, new Vector2(r.Size.X, r.Size.Y * 100f / 130f));
        var M = Px.Hex(k.Main);
        var S = Px.Hex(k.Secondary);
        // Shorts.
        Poly(ci, box, Px.Hex(k.Shorts), 32, 92, 68, 92, 71, 120, 53, 122, 50, 108, 47, 122, 29, 120);
        // Sleeves, then the body.
        Poly(ci, box, M, 30, 14, 12, 30, 22, 44, 30, 38);
        Poly(ci, box, M, 70, 14, 88, 30, 78, 44, 70, 38);
        Poly(ci, box, S, 12, 30, 22, 44, 25, 41, 15, 27);
        Poly(ci, box, S, 88, 30, 78, 44, 75, 41, 85, 27);
        Poly(ci, box, M, 30, 14, 44, 8, 50, 13, 56, 8, 70, 14, 70, 92, 50, 95, 30, 92);
        var k1 = box.Size / 100f;
        Rect2 R(float x, float y, float w, float h) => new(box.Position + new Vector2(x, y) * k1, new Vector2(w, h) * k1);
        switch (k.Pattern)
        {
            case 1: for (int i = 0; i < 4; i++) ci.DrawRect(R(32 + i * 10, 12, 5, 81), S); break;
            case 2: for (int i = 0; i < 5; i++) ci.DrawRect(R(30, 16 + i * 17, 40, 8.5f), S); break;
            case 3: for (int i = 0; i < 8; i++) ci.DrawRect(R(32 + i * 5, 12, 1, 81), S); break;
            case 4: ci.DrawRect(R(50, 12, 20, 81), S); break;
            case 5: Poly(ci, box, S, 30, 18, 38, 12, 70, 80, 70, 92, 64, 93); break;
            case 6: Poly(ci, box, S, 30, 30, 50, 46, 70, 30, 70, 38, 50, 54, 30, 38); break;
            case 7: ci.DrawRect(R(50, 12, 20, 38), S); ci.DrawRect(R(30, 50, 20, 42), S); break;
            case 8: ci.DrawRect(R(44, 10, 12, 84), S); break;
            case 9: for (int i = 0; i < 6; i++) ci.DrawRect(R(30, 92 - (i + 1) * 9, 40, 9), new Color(S, 1 - i * 0.18f)); break;
        }
        Poly(ci, box, S, 44, 8, 50, 16, 56, 8, 58, 9, 50, 20, 42, 9);
        Poly(ci, box, new Color(0, 0, 0, 0.18f), 30, 14, 36, 12, 36, 93, 30, 92);
        // The crest over the heart.
        if (crest != null) CrestArt.Draw(ci, R(55, 22, 10, 12.4f), crest);
    }

    // ---------------------------------------------------------------- manager

    /// <summary>
    /// The manager full length (the PWA's coachSVG): hair, face, coat with its trim, trousers and
    /// shoes, in a 48 x 84 box. Taller and heavier managers fill more of it.
    /// </summary>
    public static void Coach(CanvasItem ci, Rect2 r, Coach c, int shirt)
    {
        var o = Outfit.For(c.Style, shirt);
        float w = c.Build switch { 0 => 0.88f, 2 => 1.16f, _ => 1f };
        float h = (float)(c.Height / 1.8);
        float k = Mathf.Min(r.Size.X / 48f, r.Size.Y / 84f);
        var origin = r.GetCenter() - new Vector2(24, 42) * k + new Vector2(4, 2) * k;
        Vector2 At(float x, float y) => origin + new Vector2((20 + (x - 20) * w) * k, (80 + (y - 80) * h) * k);
        void Box(float x, float y, float bw, float bh, int col) => Box2(x, y, bw, bh, Px.Hex(col));
        void Box2(float x, float y, float bw, float bh, Color col)
        {
            var a = At(x, y);
            var b = At(x + bw, y + bh);
            ci.DrawRect(new Rect2(a, b - a), col);
        }
        var shadowC = origin + new Vector2(20, 79) * k;
        ci.DrawColoredPolygon(Px.Ellipse(shadowC, 12 * w * k, 2 * k), new Color(0, 0, 0, 0.35f));
        bool longCoat = c.Style == CoachStyle.Coat || c.Style == CoachStyle.Puffer;
        float coatLen = longCoat ? 58 : 48;
        Box(13, 46, 6, 30, o.Trousers);
        Box(21, 46, 6, 30, o.Trousers);
        if (o.Stripe != o.Trousers)
        {
            Box(13, 46, 1, 30, o.Stripe);
            Box(26, 46, 1, 30, o.Stripe);
        }
        Box(12, 76, 7, 3, o.Shoes);
        Box(21, 76, 7, 3, o.Shoes);
        Box(12, 78, 7, 1, o.Sole);
        Box(21, 78, 7, 1, o.Sole);
        Box(6, 23, 4.5f, 23, o.Coat);
        Box(29.5f, 23, 4.5f, 23, o.Coat);
        Box(6, 44, 4.5f, 2, o.Cuff);
        Box(29.5f, 44, 4.5f, 2, o.Cuff);
        Box(6.5f, 46, 3.5f, 4, c.Skin);
        Box(30, 46, 3.5f, 4, c.Skin);
        Box(10, 22, 20, coatLen - 22, o.Coat);
        if (o.Pattern == 8) Box(18, 23, 4, c.Style == CoachStyle.Coat ? 30 : 22, o.Trim);
        else if (o.Pattern == 2) foreach (var y in new[] { 29, 35, 41, 47, 53 }) Box(10, y, 20, 1, o.Trim);
        else
        {
            Box(10, 23, 1.5f, coatLen - 23, o.Trim);
            Box(28.5f, 23, 1.5f, coatLen - 23, o.Trim);
        }
        Box(17, 18, 6, 5, c.Skin);
        Box(14, 5, 12, 15, c.Skin);
        Box(16.5f, 11, 2, 2, 0x1a1410);
        Box(21.5f, 11, 2, 2, 0x1a1410);
        Box2(18, 16, 4, 1, new Color(60 / 255f, 25 / 255f, 15 / 255f, 0.6f));
        if (c.Hair < 0) return;
        switch (c.HairStyle)
        {
            case 2:
                Box(13, 3, 14, 6, c.Hair);
                Box(12, 5, 2, 7, c.Hair);
                Box(26, 5, 2, 7, c.Hair);
                break;
            case 3:
                Box(17, 0, 6, 4, c.Hair);
                Box(13, 4, 14, 4, c.Hair);
                Box(13, 6, 2, 5, c.Hair);
                Box(25, 6, 2, 5, c.Hair);
                break;
            default:
                Box(13, 4, 14, 4, c.Hair);
                Box(13, 6, 2, 4, c.Hair);
                Box(25, 6, 2, 4, c.Hair);
                break;
        }
    }

    // ---------------------------------------------------------------- packs

    /// <summary>A foil pack (r about 10 x 14).</summary>
    public static void Pack(CanvasItem ci, Rect2 r, PackDef p, float charge = 0)
    {
        var c1 = Px.Hex(p.Colors[0]);
        var c2 = Px.Hex(p.Colors[1]);
        var c3 = Px.Hex(p.Colors[2]);
        float u = r.Size.X / 10f;
        Px.Frame(ci, r, Colors.Transparent, c2.Darkened(0.4f), new Color(0, 0, 0, 0.5f), Math.Max(2, (int)(u * 0.3f)), Math.Max(3, (int)(u * 0.5f)));
        var inner = r.Grow(-Math.Max(2, (int)(u * 0.3f)));
        Px.Bands(ci, inner, new[] { c3, c1.Lightened(0.15f), c1, c1.Darkened(0.2f), c2 }, new[] { 0, 0.1f, 0.3f, 0.6f, 0.85f });
        // Foil: diagonal bright bands.
        for (int i = 0; i < 3; i++)
        {
            float x0 = inner.Position.X + inner.Size.X * (0.1f + i * 0.34f);
            ci.DrawColoredPolygon(new[]
            {
                new Vector2(x0, inner.Position.Y), new Vector2(x0 + u * 0.8f, inner.Position.Y),
                new Vector2(x0 - u * 2.2f, inner.End.Y), new Vector2(x0 - u * 3f, inner.End.Y),
            }, new Color(1, 1, 1, 0.12f));
        }
        // Crimped ends.
        for (float x = inner.Position.X; x < inner.End.X; x += u * 0.8f)
        {
            ci.DrawRect(new Rect2(x, inner.Position.Y + u * 0.3f, u * 0.4f, u * 0.4f), new Color(0, 0, 0, 0.2f));
            ci.DrawRect(new Rect2(x, inner.End.Y - u * 0.7f, u * 0.4f, u * 0.4f), new Color(0, 0, 0, 0.2f));
        }
        var badge = new Rect2(inner.GetCenter() - new Vector2(u * 2.6f, u * 3.2f), new Vector2(u * 5.2f, u * 4.4f));
        ci.DrawRect(badge, new Color(c2, 0.85f));
        Px.Ring(ci, badge, c3, Math.Max(1, (int)(u * 0.25f)));
        int ms = (int)(u * 3.4f);
        while (ms > 6 && Px.Width(Px.Big, p.Mark, ms) > badge.Size.X - u * 0.6f) ms--;
        Px.TextC(ci, Px.Big, badge.GetCenter().X, badge.GetCenter().Y + ms * 0.38f, p.Mark, ms, c3);
        if (p.Id == "ultimate")
        {
            // Gold filigree: a second ring and corner studs.
            Px.Ring(ci, inner.Grow(-u * 0.5f), new Color(c3, 0.8f), Math.Max(1, (int)(u * 0.18f)));
            foreach (var cp in new[] { inner.Position, new Vector2(inner.End.X, inner.Position.Y), inner.End, new Vector2(inner.Position.X, inner.End.Y) })
                ci.DrawRect(new Rect2(cp - Vector2.One * u * 0.9f + (inner.GetCenter() - cp).Normalized() * u * 1.2f, Vector2.One * u * 0.6f), c3);
        }
        if (p.Special)
        {
            // A sash across the top corner.
            var a = inner.Position + new Vector2(inner.Size.X * 0.42f, 0);
            var b = inner.Position + new Vector2(inner.Size.X, inner.Size.X * 0.58f);
            var n = (b - a).Normalized().Orthogonal() * u * 0.75f;
            var sash = new[] { a - n, b - n, b + n, a + n };
            var box = new[] { inner.Position, new Vector2(inner.End.X, inner.Position.Y), inner.End, new Vector2(inner.Position.X, inner.End.Y) };
            foreach (var part in Geometry2D.IntersectPolygons(sash, box))
                if (part.Length >= 3 && Area(part) > 0.5f) ci.DrawColoredPolygon(part, Px.Neon);
        }
        int es = (int)(u * 1.9f);
        while (es > 6 && Px.Width(Px.Big, p.Emblem, es) > inner.Size.X - u * 0.8f) es--;
        Px.TextC(ci, Px.Big, inner.GetCenter().X, inner.Position.Y + inner.Size.Y * 0.8f, p.Emblem, es, c2.Darkened(0.3f));
        if (u >= 8) Px.TextC(ci, Px.Small, inner.GetCenter().X, inner.Position.Y + inner.Size.Y * 0.9f, $"{p.Cards} PLAYERS", Math.Max(8, (int)(u * 0.7f)), c2.Darkened(0.2f));
        if (charge > 0) ci.DrawRect(inner, new Color(1, 1, 1, 0.12f * charge));
    }
}
