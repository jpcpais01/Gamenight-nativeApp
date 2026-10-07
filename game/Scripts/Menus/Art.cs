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
    public static void Avatar(CanvasItem ci, Rect2 r, int skin, int hair, int hairStyle, int shirt, int trim, int beard = 0)
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
        // Facial hair, as on the pitch (Body.FacialHair): stubble, a beard, moustache and goatee.
        var fh = beard == 1 ? new Color(h, 0.3f) : h;
        if (beard == 1 || beard == 2)
            Poly(ci, r, fh, 31, 46, 34, 56, 42, 63, 50, 65, 58, 63, 66, 56, 69, 46, 66, 50, 58, 57, 50, 58, 42, 57, 34, 50);
        if (beard >= 2)
            Poly(ci, r, fh, 42, 52, 50, 50.5f, 58, 52, 57, 53.5f, 50, 52.5f, 43, 53.5f);
        if (beard == 3)
            Poly(ci, r, fh, 45, 58, 55, 58, 54, 63.5f, 50, 64.5f, 46, 63.5f);
        ci.DrawRect(new Rect2(r.Position + new Vector2(45, 54) * k, new Vector2(10, 1.8f) * k), sd);
    }

    public static void Avatar(CanvasItem ci, Rect2 r, Card c, Kit kit) =>
        Avatar(ci, r, c.Skin, c.Hair, c.HairStyle, c.Position == Pos.GK ? kit.GkShirt : kit.Shirt, c.Position == Pos.GK ? kit.GkShorts : kit.Shirt2,
            GameNight.Sim.Body.FacialHair(c.Name));

    // ---------------------------------------------------------------- cards

    public static Color RarityColor(Rarity r) => Px.Hex(Cards.RarityColor[(int)r]);

    /// <summary>A card's colour in lists and tokens: its event's trim, else its rarity's.</summary>
    public static Color ColorOf(Card c) => Events.Of(c) is EventDef e ? Px.Hex(e.Trim) : RarityColor(c.Rarity);

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

    /// <summary>Event cards' silhouettes: bat wings, icicles, a feather crown, a capsule.</summary>
    static readonly System.Collections.Generic.Dictionary<string, float[]> EventShapes = new()
    {
        ["halloween"] = new[] { 0, 0.1f, 0.1f, 0.02f, 0.17f, 0.075f, 0.26f, 0.01f, 0.36f, 0.06f, 0.5f, 0.025f, 0.64f, 0.06f, 0.74f, 0.01f, 0.83f, 0.075f, 0.9f, 0.02f, 1, 0.1f, 1, 0.86f, 0.7f, 0.93f, 0.62f, 0.985f, 0.5f, 0.95f, 0.38f, 0.985f, 0.3f, 0.93f, 0, 0.86f },
        ["frost"] = new[] { 0, 0.07f, 0.2f, 0.015f, 0.5f, 0, 0.8f, 0.015f, 1, 0.07f, 1, 0.85f, 0.88f, 0.885f, 0.83f, 0.95f, 0.77f, 0.9f, 0.64f, 0.94f, 0.5f, 1, 0.36f, 0.94f, 0.23f, 0.9f, 0.17f, 0.95f, 0.12f, 0.885f, 0, 0.85f },
        ["carnival"] = new[] { 0, 0.12f, 0.07f, 0.02f, 0.19f, 0.09f, 0.31f, 0, 0.41f, 0.075f, 0.5f, 0, 0.59f, 0.075f, 0.69f, 0, 0.81f, 0.09f, 0.93f, 0.02f, 1, 0.12f, 1, 0.86f, 0.5f, 1, 0, 0.86f },
        ["cosmic"] = new[] { 0.16f, 0.035f, 0.5f, 0, 0.84f, 0.035f, 1, 0.13f, 1, 0.86f, 0.84f, 0.955f, 0.5f, 1, 0.16f, 0.955f, 0, 0.86f, 0, 0.13f },
    };

    static Vector2[] Shape(Rect2 r, int tier) => Shape(r, Shapes[Math.Clamp(tier, 0, 4)]);

    static Vector2[] Shape(Rect2 r, float[] n)
    {
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
    public static void Card(CanvasItem ci, Rect2 r, Card c, Kit kit, float glow = 0, Crest crest = null) => CardFace(ci, r, c, kit, glow, crest, null);

    /// <summary>The mini card for the formation board and the bench: the real card's finish,
    /// portrait and name plate, without the stats; `r` about 10 x 12. With a slot, the rating
    /// and position are his in that slot.</summary>
    public static void MiniCard(CanvasItem ci, Rect2 r, Card c, Kit kit, Pos slot) => CardFace(ci, r, c, kit, 0, null, slot);

    static void CardFace(CanvasItem ci, Rect2 r, Card c, Kit kit, float glow, Crest crest, Pos? slot)
    {
        bool mini = slot != null;
        float u = r.Size.X / 10f;
        int tier = (int)c.Rarity;
        var ev = Events.Of(c);
        var outline = ev != null ? EventShapes[ev.Id] : Shapes[tier];
        var face = ev?.Face ?? Face[tier];
        var trim = Px.Hex(ev?.Trim ?? Trim[tier]);
        var acc = Px.Hex(ev?.Accent ?? Accent[tier]);
        var col = ev != null ? acc : RarityColor(c.Rarity);
        // Dark faces take light ink, a dark plate and accent lines.
        bool dark = ev != null ? Px.Hex(ev.Ink).Luminance > 0.5f : tier == 3;
        var outer = Shape(r, outline);
        if (glow > 0)
            for (int i = 3; i >= 1; i--) ci.DrawColoredPolygon(Shape(r.Grow(u * 0.45f * i * glow), outline), new Color(col, 0.12f * glow));
        ci.DrawColoredPolygon(Shape(r.Translated(new Vector2(u * 0.35f, u * 0.45f)), outline), new Color(0, 0, 0, 0.5f));
        ci.DrawColoredPolygon(outer, trim.Darkened(0.35f));
        float bw = Mathf.Max(2, u * 0.32f);
        var ir = r.Grow(-bw);
        var shape = Shape(ir, outline);
        ci.DrawColoredPolygon(shape, Px.Hex(face[2]));
        // The face: hard bands, top to bottom.
        float[] stops = { 0, 0.14f, 0.36f, 0.64f, 0.84f, 1.01f };
        for (int i = 0; i < 5; i++)
            Clip(ci, Quad(ir.Position.X - 1, ir.Position.Y + ir.Size.Y * stops[i], ir.End.X + 1, ir.Position.Y + ir.Size.Y * stops[i + 1]), shape, Px.Hex(face[i]));
        var p = ir.Position;
        Vector2 U(float x, float y) => p + new Vector2(x, y) * u;

        // The tier's texture (or the event's).
        if (ev != null) EventTexture(ci, ev.Id, shape, U, u, acc, trim);
        else switch (tier)
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
        var line = Shape(ir.Grow(-u * 0.35f), outline);
        var closed = new Vector2[line.Length + 1];
        Array.Copy(line, closed, line.Length);
        closed[^1] = line[0];
        ci.DrawPolyline(closed, new Color(tier == 3 && ev == null ? acc : trim, tier >= 2 || ev != null ? 0.85f : 0.45f), Mathf.Max(1, u * 0.12f));

        var ink = ev != null ? Px.Hex(ev.Ink) : CardInk(c.Rarity);
        // The portrait, on a soft halo.
        ci.DrawColoredPolygon(Px.Ellipse(U(6.5f, 5.1f), u * 2.9f, u * 2.9f, 22), new Color(acc, dark ? 0.14f : 0.22f));
        Avatar(ci, new Rect2(U(3.6f, 1.85f), new Vector2(u * 6.1f, u * 6.1f)), c, kit);
        if (mini)
        {
            MiniFace(ci, ir, c, slot.Value, U, u, ink, acc, trim, dark, ev);
            return;
        }
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
        ci.DrawRect(plate, dark ? new Color(0, 0, 0, 0.35f) : new Color(ink, 0.12f));
        var plateLine = new Color(dark ? acc : trim, 0.7f);
        ci.DrawRect(new Rect2(plate.Position, new Vector2(plate.Size.X, Mathf.Max(1, u * 0.1f))), plateLine);
        ci.DrawRect(new Rect2(plate.Position.X, plate.End.Y, plate.Size.X, Mathf.Max(1, u * 0.1f)), plateLine);
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
        if (u >= 6) Px.TextC(ci, Px.Small, ir.GetCenter().X, p.Y + u * 12.95f, ev?.Label ?? Cards.Label(c.Rarity).ToUpperInvariant(), Math.Max(7, (int)(u * 0.6f)), ev != null ? acc : new Color(ink, 0.7f));
    }

    /// <summary>The mini card's print: rating and position in his slot, playstyles, the name
    /// plate, and under it a row kept for the skill-move stars, then the tier or event.</summary>
    static void MiniFace(CanvasItem ci, Rect2 ir, Card c, Pos slot, Func<float, float, Vector2> U, float u, Color ink, Color acc, Color trim, bool dark, EventDef ev)
    {
        var p = ir.Position;
        var shadow = new Color(0, 0, 0, dark ? 0.5f : 0.25f);
        int rs = Math.Max(14, (int)(u * 3.3f));
        Px.TextC(ci, Px.Big, p.X + u * 2.0f, p.Y + u * 1.0f + rs * 0.82f, Cards.RatingIn(c, slot).ToString(), rs, ink, shadow, 1);
        int ps = Math.Max(7, (int)(u * 1.3f));
        float py = p.Y + u * 1.0f + rs * 0.82f + ps + 3;
        if (ps >= 10) Px.TextC(ci, Px.Big, p.X + u * 2.0f, py, slot.ToString(), ps, ink);
        else Px.TextC(ci, Px.Small, p.X + u * 2.0f, py, slot.ToString(), 7, ink);
        var styles = Playstyles.Of(c);
        for (int i = 0; i < styles.Count; i++)
            Playstyle(ci, U(8.75f, 2.4f + i * 2.1f), Mathf.Max(5, u * 0.95f), styles[i]);
        // The name plate.
        var plate = new Rect2(U(0.2f, 7.6f), new Vector2(ir.Size.X - u * 0.4f, Mathf.Max(11, u * 1.9f)));
        ci.DrawRect(plate, dark ? new Color(0, 0, 0, 0.45f) : new Color(ink, 0.14f));
        var plateLine = new Color(dark ? acc : trim, 0.75f);
        ci.DrawRect(new Rect2(plate.Position, new Vector2(plate.Size.X, 1)), plateLine);
        ci.DrawRect(new Rect2(plate.Position.X, plate.End.Y - 1, plate.Size.X, 1), plateLine);
        string name = c.LastName.ToUpperInvariant();
        int ns = (int)(plate.Size.Y * 0.8f);
        if (ns >= 12) Px.TextC(ci, Px.Big, plate.GetCenter().X, plate.GetCenter().Y + ns * 0.36f, Px.Fit(Px.Big, name, ns, plate.Size.X - 4), ns, ink);
        else Px.TextC(ci, Px.Small, plate.GetCenter().X, plate.GetCenter().Y + 4, Px.Fit(Px.Small, name, 7, plate.Size.X - 2), 7, ink);
        // Under the plate: his skill-move stars (gold, the rest dim); then the tier.
        float below = plate.End.Y + (ir.End.Y - plate.End.Y) * 0.72f;
        {
            int stars = SkillStars.Of(c);
            float sr = Mathf.Clamp(u * 0.42f, 2, 6), sg = sr * 0.5f;
            float sy = ir.End.Y - plate.End.Y >= 14 ? plate.End.Y + (below - 4 - plate.End.Y) * 0.55f : (plate.End.Y + ir.End.Y) / 2;
            float sx = ir.GetCenter().X - (5 * sr * 2 + 4 * sg) / 2 + sr;
            for (int i = 0; i < 5; i++)
                Star(ci, new Vector2(sx + i * (sr * 2 + sg), sy), sr, i < stars ? Px.Gold : new Color(ink, 0.18f));
        }
        string tier = ev?.Label ?? Cards.Label(c.Rarity).ToUpperInvariant();
        if (ir.End.Y - plate.End.Y >= 14)
            Px.TextC(ci, Px.Small, ir.GetCenter().X, below + 3, Px.Fit(Px.Small, tier, 6, ir.Size.X - 4), 6, ev != null ? acc : new Color(ink, 0.7f));
    }

    /// <summary>An event pack's badge picture: a jack-o'-lantern, a snowflake, a carnival mask, a
    /// ringed planet.</summary>
    static void PackMotif(CanvasItem ci, Rect2 b, string id, Color main, Color light, float u)
    {
        var c = b.GetCenter();
        float s = Mathf.Min(b.Size.X, b.Size.Y) * 0.36f;
        var dark = Px.Hex(0x14121c);
        switch (id)
        {
            case "halloween":
                ci.DrawRect(new Rect2(c + new Vector2(-s * 0.12f, -s * 1.05f), new Vector2(s * 0.24f, s * 0.35f)), Px.Hex(0x3a7a2a));
                ci.DrawColoredPolygon(Px.Ellipse(c + new Vector2(0, s * 0.1f), s * 1.05f, s * 0.82f, 20), Px.Hex(0xff7a1a));
                ci.DrawColoredPolygon(Px.Ellipse(c + new Vector2(0, s * 0.1f), s * 0.35f, s * 0.8f, 14), Px.Hex(0xe0600c));
                ci.DrawColoredPolygon(new[] { c + new Vector2(-s * 0.6f, -s * 0.05f), c + new Vector2(-s * 0.2f, -s * 0.05f), c + new Vector2(-s * 0.4f, -s * 0.38f) }, dark);
                ci.DrawColoredPolygon(new[] { c + new Vector2(s * 0.2f, -s * 0.05f), c + new Vector2(s * 0.6f, -s * 0.05f), c + new Vector2(s * 0.4f, -s * 0.38f) }, dark);
                ci.DrawColoredPolygon(new[] { c + new Vector2(-s * 0.6f, s * 0.3f), c + new Vector2(-s * 0.3f, s * 0.45f), c + new Vector2(0, s * 0.3f), c + new Vector2(s * 0.3f, s * 0.45f), c + new Vector2(s * 0.6f, s * 0.3f), c + new Vector2(s * 0.3f, s * 0.65f), c + new Vector2(-s * 0.3f, s * 0.65f) }, dark);
                break;
            case "frost":
                for (int k = 0; k < 3; k++)
                {
                    float a = k * Mathf.Pi / 3 + Mathf.Pi / 2;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    ci.DrawLine(c - d * s, c + d * s, light, Mathf.Max(2, u * 0.35f));
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        var tip = c + d * s * 0.62f * sign;
                        var side = new Vector2(-d.Y, d.X) * s * 0.25f;
                        ci.DrawLine(tip, tip + d * s * 0.25f * sign + side, light, Mathf.Max(1, u * 0.22f));
                        ci.DrawLine(tip, tip + d * s * 0.25f * sign - side, light, Mathf.Max(1, u * 0.22f));
                    }
                }
                break;
            case "carnival":
                for (int i = 0; i < 5; i++)
                {
                    float a = -Mathf.Pi / 2 + (i - 2) * 0.35f;
                    var tip = c + new Vector2(0, -s * 0.2f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s * 1.1f;
                    ci.DrawColoredPolygon(new[] { c + new Vector2(-s * 0.15f, -s * 0.2f), tip, c + new Vector2(s * 0.15f, -s * 0.2f) }, i % 2 == 0 ? Px.Hex(0x2ef2c8) : Px.Hex(0xffd447));
                }
                ci.DrawColoredPolygon(new[] { c + new Vector2(-s, -s * 0.15f), c + new Vector2(s, -s * 0.15f), c + new Vector2(s * 0.85f, s * 0.35f), c + new Vector2(s * 0.15f, s * 0.45f), c + new Vector2(0, s * 0.25f), c + new Vector2(-s * 0.15f, s * 0.45f), c + new Vector2(-s * 0.85f, s * 0.35f) }, light);
                ci.DrawColoredPolygon(Px.Ellipse(c + new Vector2(-s * 0.45f, s * 0.08f), s * 0.24f, s * 0.15f, 10), dark);
                ci.DrawColoredPolygon(Px.Ellipse(c + new Vector2(s * 0.45f, s * 0.08f), s * 0.24f, s * 0.15f, 10), dark);
                break;
            default:
                ci.DrawColoredPolygon(Px.Ellipse(c, s * 0.7f, s * 0.7f, 18), Px.Hex(0xffb84a));
                ci.DrawColoredPolygon(Px.Ellipse(c + new Vector2(-s * 0.2f, -s * 0.2f), s * 0.22f, s * 0.15f, 10), Px.Hex(0xffd88a));
                var ring = new[] { c + new Vector2(-s * 1.3f, s * 0.35f), c + new Vector2(s * 1.3f, -s * 0.35f) };
                var n = (ring[1] - ring[0]).Normalized().Orthogonal() * Mathf.Max(2, u * 0.3f);
                ci.DrawColoredPolygon(new[] { ring[0] - n, ring[1] - n, ring[1] + n, ring[0] + n }, light);
                foreach (var (x, y) in new[] { (-0.9f, -0.8f), (0.95f, 0.7f), (0.6f, -0.95f), (-1.0f, 0.75f) })
                    ci.DrawRect(new Rect2(c + new Vector2(x, y) * s, Vector2.One * Mathf.Max(1, u * 0.25f)), Colors.White);
                break;
        }
    }

    /// <summary>The event finishes: a moon, bats and a web; snowflakes and icicles; a feather fan
    /// and confetti; stars, a nebula and a ringed planet.</summary>
    static void EventTexture(CanvasItem ci, string id, Vector2[] shape, Func<float, float, Vector2> U, float u, Color acc, Color trim)
    {
        float lw = Mathf.Max(1, u * 0.1f);
        void Bar(Vector2 a, Vector2 b, float w, Color c)
        {
            var n = (b - a).Normalized().Orthogonal() * w / 2;
            Clip(ci, new[] { a - n, b - n, b + n, a + n }, shape, c);
        }
        switch (id)
        {
            case "halloween":
            {
                // A low orange moon behind the portrait, a web in the corner, bats.
                Clip(ci, Px.Ellipse(U(6.6f, 4.4f), u * 3.3f, u * 3.3f, 26), shape, new Color(1, 0.62f, 0.2f, 0.18f));
                Clip(ci, Px.Ellipse(U(6.6f, 4.4f), u * 2.5f, u * 2.5f, 24), shape, new Color(1, 0.85f, 0.55f, 0.16f));
                var o = U(0, 0);
                for (int i = 0; i < 5; i++)
                {
                    float a = i / 4f * Mathf.Pi / 2;
                    Bar(o, o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * u * 3.4f, lw, new Color(1, 1, 1, 0.22f));
                }
                for (int k = 1; k <= 3; k++)
                {
                    var pts = new Vector2[5];
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i / 4f * Mathf.Pi / 2;
                        pts[i] = o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * u * k * 0.95f;
                    }
                    for (int i = 0; i < 4; i++) Bar(pts[i], pts[i + 1], lw, new Color(1, 1, 1, 0.18f));
                }
                foreach (var (x, y, sz) in new[] { (8.6f, 1.2f, 0.8f), (2.6f, 2.2f, 0.6f), (7.2f, 0.7f, 0.5f) })
                {
                    var c = U(x, y);
                    float w = u * sz;
                    Clip(ci, new[] { c + new Vector2(-w * 1.6f, -w * 0.5f), c + new Vector2(-w * 0.8f, -w * 0.1f), c + new Vector2(-w * 0.3f, -w * 0.5f), c + new Vector2(0, -w * 0.2f), c + new Vector2(w * 0.3f, -w * 0.5f), c + new Vector2(w * 0.8f, -w * 0.1f), c + new Vector2(w * 1.6f, -w * 0.5f), c + new Vector2(w * 0.9f, w * 0.4f), c + new Vector2(0, w * 0.2f), c + new Vector2(-w * 0.9f, w * 0.4f) }, shape, new Color(acc, 0.75f));
                }
                // Embers rising from the bottom.
                for (int i = 0; i < 9; i++) ci.DrawRect(new Rect2(U(0.6f + i * 1.05f, 12.4f - (i * 7 % 5) * 0.5f), Vector2.One * Mathf.Max(1, u * 0.18f)), new Color(acc, 0.6f));
                break;
            }
            case "frost":
            {
                for (int i = 0; i < 3; i++)
                {
                    float x = 1.5f + i * 3.3f;
                    Clip(ci, new[] { U(x, 0), U(x + 1.1f, 0), U(x - 3, 14), U(x - 4.1f, 14) }, shape, new Color(1, 1, 1, 0.2f));
                }
                // Snowflakes.
                foreach (var (x, y, sz) in new[] { (8.4f, 1.6f, 0.7f), (1.6f, 8.9f, 0.45f), (8.9f, 7.6f, 0.5f), (3.2f, 1.1f, 0.4f), (5.2f, 13.1f, 0.35f) })
                {
                    var c = U(x, y);
                    for (int k = 0; k < 3; k++)
                    {
                        float a = k * Mathf.Pi / 3;
                        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * u * sz;
                        Bar(c - d, c + d, lw, new Color(trim, 0.55f));
                    }
                }
                // Icicles off the top edge.
                for (int i = 0; i < 8; i++)
                {
                    float x = 0.7f + i * 1.2f, len = 0.6f + (i * 5 % 3) * 0.35f;
                    Clip(ci, new[] { U(x - 0.3f, 0), U(x + 0.3f, 0), U(x, len) }, shape, new Color(1, 1, 1, 0.75f));
                }
                break;
            }
            case "carnival":
            {
                // A fan of feathers behind the portrait.
                var o = U(6.5f, 7.6f);
                var cols = new[] { acc, trim, Px.Hex(0x8a3cff), Px.Hex(0xff8a2e) };
                for (int i = 0; i < 9; i++)
                {
                    float a = Mathf.Pi + 0.25f + i * (Mathf.Pi - 0.5f) / 8;
                    var tip = o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * u * 7.2f;
                    var side = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * u * 0.55f;
                    Clip(ci, new[] { o, tip + side, tip - side }, shape, new Color(cols[i % 4], 0.32f));
                }
                // Confetti.
                for (int i = 0; i < 16; i++)
                {
                    var c = U(0.4f + (i * 37 % 92) / 10f, 0.5f + (i * 53 % 125) / 10f);
                    float w = u * 0.32f;
                    Clip(ci, Quad(c.X, c.Y, c.X + w, c.Y + w * (i % 2 == 0 ? 0.5f : 1.4f)), shape, new Color(cols[i % 4], 0.85f));
                }
                break;
            }
            case "cosmic":
            {
                Clip(ci, Px.Ellipse(U(3.0f, 4.0f), u * 3.4f, u * 2.2f, 22), shape, new Color(acc, 0.12f));
                Clip(ci, Px.Ellipse(U(7.4f, 9.6f), u * 3.8f, u * 2.0f, 22), shape, new Color(trim, 0.1f));
                for (int i = 0; i < 22; i++)
                {
                    var c = U(0.3f + (i * 41 % 94) / 10f, 0.4f + (i * 67 % 128) / 10f);
                    float w = Mathf.Max(1, u * (i % 5 == 0 ? 0.24f : 0.13f));
                    ci.DrawRect(new Rect2(c, new Vector2(w, w)), new Color(1, 1, 1, i % 3 == 0 ? 0.9f : 0.5f));
                }
                // A ringed planet in the corner and a shooting star.
                var pc = U(8.5f, 1.3f);
                Clip(ci, Px.Ellipse(pc, u * 0.85f, u * 0.85f, 16), shape, Px.Hex(0xffb84a));
                Bar(pc + new Vector2(-u * 1.5f, u * 0.35f), pc + new Vector2(u * 1.5f, -u * 0.35f), Mathf.Max(1, u * 0.18f), new Color(trim, 0.9f));
                Bar(U(0.6f, 1.2f), U(2.8f, 2.4f), Mathf.Max(1, u * 0.12f), new Color(1, 1, 1, 0.6f));
                break;
            }
        }
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

    /// <summary>A player on the tactics board or the bench: his mini card, how well he fits the slot, the armband.</summary>
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
        MiniCard(ci, r, c, kit, slot);
        double f = Cards.FitFactor(c.Position, slot);
        var fit = f == 1 ? Px.Win : f >= 0.85 ? Px.Gold : Px.Loss;
        // How well he fits the slot: a lamp top right (and his own position, if not this one).
        var lamp = new Rect2(r.End.X - 11, r.Position.Y + 6, 6, 6);
        ci.DrawRect(lamp.Grow(1), new Color(0, 0, 0, 0.6f));
        ci.DrawRect(lamp, fit);
        if (f < 1)
        {
            string own = c.Position.ToString();
            float w = Px.Width(Px.Small, own, 7) + 6;
            var tag = new Rect2(r.GetCenter().X - w / 2, r.End.Y + 2, w, 11);
            ci.DrawRect(tag, new Color(0.05f, 0.04f, 0.15f, 0.88f));
            Px.TextC(ci, Px.Small, tag.GetCenter().X, tag.End.Y - 2, own, 7, fit);
        }
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

    /// <summary>The manager, full length (r about 48 x 84): his face, hair and beard, and his
    /// outfit in his own coat and accent colours.</summary>
    public static void Coach(CanvasItem ci, Rect2 r, Coach c, int shirt, int second = -1)
    {
        var o = Outfit.ForCoach(c, shirt, second < 0 ? shirt : second);
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
        var shade = new Color(0, 0, 0, 0.22f);
        var light = new Color(1, 1, 1, 0.12f);
        var shadowC = origin + new Vector2(20, 79) * k;
        ci.DrawColoredPolygon(Px.Ellipse(shadowC, 12 * w * k, 2 * k), new Color(0, 0, 0, 0.35f));
        bool longCoat = c.Style == CoachStyle.Coat || c.Style == CoachStyle.Puffer;
        float coatLen = longCoat ? 58 : 48;

        // Legs and shoes.
        Box(13, 46, 6, 30, o.Trousers);
        Box(21, 46, 6, 30, o.Trousers);
        Box2(17.5f, 46, 1.5f, 30, shade);
        Box2(25.5f, 46, 1.5f, 30, shade);
        if (o.Stripe != o.Trousers)
        {
            Box(13, 46, 1, 30, o.Stripe);
            Box(26, 46, 1, 30, o.Stripe);
        }
        Box(12, 76, 7, 3, o.Shoes);
        Box(21, 76, 7, 3, o.Shoes);
        Box2(12, 76, 7, 1, light);
        Box2(21, 76, 7, 1, light);
        Box(12, 78, 7, 1, o.Sole);
        Box(21, 78, 7, 1, o.Sole);

        // Arms, cuffs, hands.
        Box(6, 23, 4.5f, 23, o.Coat);
        Box(29.5f, 23, 4.5f, 23, o.Coat);
        Box2(32.5f, 23, 1.5f, 23, shade);
        Box(6, 44, 4.5f, 2, o.Cuff);
        Box(29.5f, 44, 4.5f, 2, o.Cuff);
        Box(6.5f, 46, 3.5f, 4, c.Skin);
        Box(30, 46, 3.5f, 4, c.Skin);
        Box2(6.5f, 49, 3.5f, 1, shade);
        Box2(30, 49, 3.5f, 1, shade);

        // The body of the coat.
        Box(10, 22, 20, coatLen - 22, o.Coat);
        Box2(26, 22, 4, coatLen - 22, shade);
        Box2(10, 22, 20, 1, light);
        switch (c.Style)
        {
            case CoachStyle.Suit:
                // White shirt in the V, a tie, lapels and a pocket square.
                ci.DrawColoredPolygon(new[] { At(16, 22), At(24, 22), At(20, 33) }, Px.Hex(0xf1efe8));
                Box(19, 23, 2, 2, o.Trim == 0xf1efe8 ? 0x8f1f24 : o.Trim);
                ci.DrawColoredPolygon(new[] { At(19.2f, 25), At(20.8f, 25), At(21.2f, 32), At(20, 34), At(18.8f, 32) }, Px.Hex(o.Trim == 0xf1efe8 ? 0x8f1f24 : o.Trim));
                ci.DrawColoredPolygon(new[] { At(15, 22), At(17, 22), At(20, 34), At(18, 34) }, Px.Hex(o.Coat).Darkened(0.25f));
                ci.DrawColoredPolygon(new[] { At(23, 22), At(25, 22), At(22, 34), At(20, 34) }, Px.Hex(o.Coat).Darkened(0.35f));
                Box(24, 30, 3, 1, 0xf1efe8);
                Box(19.5f, 38, 1, 1, 0x0e0e10);
                Box(19.5f, 43, 1, 1, 0x0e0e10);
                break;
            case CoachStyle.Coat:
                // A scarf in his colour, wrapped and hanging down the front, and buttons.
                Box(14, 20, 12, 4, o.Trim);
                Box2(14, 22.5f, 12, 1.5f, shade);
                Box(17, 23, 4, 18, o.Trim);
                Box2(17, 39, 4, 2, new Color(1, 1, 1, 0.35f));
                Box(17, 40, 1, 2, o.Trim);
                Box(19, 40, 1, 2, o.Trim);
                foreach (var y in new[] { 44, 49, 54 }) Box(23, y, 1.5f, 1.5f, 0x1a120c);
                Box2(10, 38, 6, 1, shade);
                break;
            case CoachStyle.Track:
                // Collar, zip, chest badge and sleeve stripes.
                Box(15, 20, 10, 3, o.Coat);
                Box(15, 20, 10, 1, o.Trim);
                Box(19.5f, 22, 1, 26, 0xb9bdc4);
                Box(19, 26, 2, 2, 0xd8d6cf);
                Box(23, 27, 3, 3, o.Trim);
                Box(6, 24, 1, 20, o.Trim);
                Box(33, 24, 1, 20, o.Trim);
                Box(10, 46, 20, 2, o.Trim);
                break;
            default:
                // Puffer: a tall collar and quilted bands.
                Box(14, 18, 12, 5, o.Coat);
                Box2(14, 18, 12, 1, light);
                foreach (var y in new[] { 28, 34, 40, 46, 52 }) Box(10, y, 20, 1, o.Trim);
                foreach (var y in new[] { 30, 37 }) Box(6, y, 4.5f, 1, o.Trim);
                foreach (var y in new[] { 30, 37 }) Box(29.5f, y, 4.5f, 1, o.Trim);
                Box(19.5f, 23, 1, 35, 0x0e0e10);
                break;
        }

        // Neck, ears, head.
        if (c.Style != CoachStyle.Puffer) Box(17, 18, 6, 4, c.Skin);
        Box2(17, 18, 6, 2, shade);
        Box(13, 10, 1.5f, 4, c.Skin);
        Box(25.5f, 10, 1.5f, 4, c.Skin);
        Box2(13, 13, 1.5f, 1, shade);
        Box2(25.5f, 13, 1.5f, 1, shade);
        Box(14, 5, 12, 15, c.Skin);
        Box2(23.5f, 5, 2.5f, 15, new Color(0, 0, 0, 0.12f));
        Box2(14, 18.5f, 12, 1.5f, new Color(0, 0, 0, 0.1f));
        int brow = c.Hair < 0 ? 0x4a3324 : c.Hair;
        // Eyes with whites, and brows set by temper.
        Box(16, 11, 3, 2, 0xf3ede0);
        Box(21, 11, 3, 2, 0xf3ede0);
        Box(17, 11, 2, 2, 0x1a1410);
        Box(22, 11, 2, 2, 0x1a1410);
        switch (c.Temper)
        {
            case CoachTemper.Fiery:
                ci.DrawColoredPolygon(new[] { At(15.5f, 8.5f), At(19.5f, 9.8f), At(19.5f, 10.8f), At(15.5f, 9.5f) }, Px.Hex(brow));
                ci.DrawColoredPolygon(new[] { At(20.5f, 9.8f), At(24.5f, 8.5f), At(24.5f, 9.5f), At(20.5f, 10.8f) }, Px.Hex(brow));
                break;
            case CoachTemper.Showman:
                Box(15.5f, 8.5f, 4, 1, brow);
                Box(20.5f, 8, 4, 1, brow);
                break;
            default:
                Box(15.5f, 9, 4, 1, brow);
                Box(20.5f, 9, 4, 1, brow);
                break;
        }
        // Nose.
        Box2(19.5f, 12.5f, 1.5f, 2.5f, new Color(0, 0, 0, 0.15f));
        Box2(19, 14.5f, 2.5f, 0.8f, new Color(0, 0, 0, 0.22f));
        // Facial hair, then the mouth over it.
        var beard = Px.Hex(c.BeardColor);
        switch (c.Facial)
        {
            case 1:
                Box2(14, 14, 12, 6, new Color(beard, 0.35f));
                break;
            case 2:
                Box2(14, 13, 12, 7, beard);
                Box2(13, 10, 1.5f, 5, beard);
                Box2(25.5f, 10, 1.5f, 5, beard);
                Box2(15, 20, 10, 1.5f, beard);
                Box2(17, 15, 6, 2, Px.Hex(c.Skin).Darkened(0.05f));
                break;
            case 3:
                Box2(17, 15, 6, 1.2f, beard);
                Box2(17, 15, 1, 3, beard);
                Box2(22, 15, 1, 3, beard);
                Box2(18.5f, 18, 3, 2.5f, beard);
                break;
        }
        var lip = new Color(60 / 255f, 25 / 255f, 15 / 255f, 0.7f);
        switch (c.Temper)
        {
            case CoachTemper.Showman:
                Box2(18, 16, 4, 1.4f, Px.Hex(0x3a1410));
                Box2(18.5f, 16, 3, 0.6f, Px.Hex(0xf3ede0));
                break;
            case CoachTemper.Fiery:
                Box2(18, 16.5f, 4, 1, lip);
                Box2(17.5f, 17, 0.8f, 0.8f, lip);
                Box2(21.7f, 17, 0.8f, 0.8f, lip);
                break;
            default:
                Box2(18, 16.3f, 4, 1, lip);
                break;
        }

        // Hair.
        if (c.Hair < 0)
        {
            Box2(16, 5.5f, 5, 1.5f, new Color(1, 1, 1, 0.25f));
            return;
        }
        var hair = Px.Hex(c.Hair);
        switch (c.HairStyle)
        {
            case 1: // buzz cut: a thin cap with skin showing through
                Box2(14, 4.5f, 12, 3, new Color(hair, 0.85f));
                Box2(14, 6.5f, 1.5f, 3.5f, new Color(hair, 0.6f));
                Box2(24.5f, 6.5f, 1.5f, 3.5f, new Color(hair, 0.6f));
                break;
            case 2: // curly
                Box2(13, 3, 14, 5, hair);
                foreach (var x in new[] { 13f, 16f, 19f, 22f, 25f }) Box2(x, 2, 2, 1.5f, hair);
                Box2(12, 5, 2.5f, 7, hair);
                Box2(25.5f, 5, 2.5f, 7, hair);
                foreach (var x in new[] { 14.5f, 18.5f, 22.5f }) Box2(x, 4, 1, 1, hair.Darkened(0.3f));
                break;
            case 3: // quiff
                Box2(16, 0, 8, 4, hair);
                Box2(18, -1, 5, 1.5f, hair);
                Box2(16, 0.5f, 6, 1, hair.Lightened(0.2f));
                Box2(13, 4, 14, 4, hair);
                Box2(13, 6, 2, 5, hair);
                Box2(25, 6, 2, 5, hair);
                break;
            default: // short, side parted
                Box2(13, 3.5f, 14, 4.5f, hair);
                Box2(13, 6, 2, 4.5f, hair);
                Box2(25, 6, 2, 4.5f, hair);
                Box2(18, 3.5f, 1, 2, hair.Darkened(0.35f));
                Box2(19, 3.5f, 6, 1, hair.Lightened(0.15f));
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
        if (p.Event != null) PackMotif(ci, badge, p.Event, c1, c3, u);
        else
        {
            int ms = (int)(u * 3.4f);
            while (ms > 6 && Px.Width(Px.Big, p.Mark, ms) > badge.Size.X - u * 0.6f) ms--;
            Px.TextC(ci, Px.Big, badge.GetCenter().X, badge.GetCenter().Y + ms * 0.38f, p.Mark, ms, c3);
        }
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

    /// <summary>A five-pointed star (skill moves), with a hard pixel shadow.</summary>
    public static void Star(CanvasItem c, Vector2 centre, float r, Color col)
    {
        var pts = new Vector2[10];
        var sh = new Vector2[10];
        for (int j = 0; j < 10; j++)
        {
            float a = -Mathf.Pi / 2 + j * Mathf.Pi / 5;
            float rr = j % 2 == 0 ? r : r * 0.45f;
            pts[j] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
            sh[j] = pts[j] + new Vector2(1, 1);
        }
        if (col.A > 0.5f) c.DrawColoredPolygon(sh, new Color(0, 0, 0, 0.5f));
        c.DrawColoredPolygon(pts, col);
    }
}
