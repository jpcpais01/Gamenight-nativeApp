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
    static Color CardInk(Rarity r) => r == Rarity.Legendary ? Px.Hex(0x1e0a33) : Px.Hex(0x2b1708);

    /// <summary>A full player card (FUT style); `r` should be about 10 x 14.</summary>
    public static void Card(CanvasItem ci, Rect2 r, Card c, Kit kit, float glow = 0)
    {
        float u = r.Size.X / 10f;
        var col = RarityColor(c.Rarity);
        if (glow > 0)
            for (int i = 3; i >= 1; i--) ci.DrawRect(r.Grow(u * 0.5f * i * glow), new Color(col, 0.12f * glow));
        Px.Frame(ci, r, Colors.Transparent, col.Darkened(0.55f), new Color(0, 0, 0, 0.5f), Math.Max(2, (int)(u * 0.3f)), Math.Max(3, (int)(u * 0.5f)));
        var inner = r.Grow(-Math.Max(2, (int)(u * 0.3f)));
        Px.Bands(ci, inner, new[] { col.Lightened(0.45f), col.Lightened(0.15f), col, col.Darkened(0.18f), col.Darkened(0.32f) }, new[] { 0, 0.12f, 0.3f, 0.62f, 0.85f });
        // Icons get a little sparkle grid.
        if (c.Rarity == Rarity.Icon)
            for (int i = 0; i < 6; i++) ci.DrawRect(new Rect2(inner.Position + new Vector2((i * 37 % 9 + 0.5f) * u, (i * 23 % 13 + 0.5f) * u), new Vector2(u * 0.3f, u * 0.3f)), new Color(1, 1, 1, 0.5f));
        var ink = CardInk(c.Rarity);
        var p = inner.Position;
        int ovrSize = (int)(u * 3.4f);
        Px.TextC(ci, Px.Big, p.X + u * 1.7f, p.Y + u * 3.0f, c.Overall.ToString(), ovrSize, ink);
        Px.TextC(ci, Px.Big, p.X + u * 1.7f, p.Y + u * 4.4f, c.Position.ToString(), (int)(u * 1.5f), ink);
        Px.Flag(ci, new Rect2(p + new Vector2(u * 0.9f, u * 5.0f), new Vector2(u * 1.6f, u * 1.1f)), Cards.Nations[c.Nation]);
        Avatar(ci, new Rect2(p + new Vector2(u * 3.2f, u * 0.7f), new Vector2(u * 6.2f, u * 6.2f)), c, kit);
        // Playstyle badges down the right edge.
        var ps = Playstyles.Of(c);
        for (int i = 0; i < ps.Count; i++)
            Playstyle(ci, p + new Vector2(inner.Size.X - u * 1.05f, u * 1.25f + i * u * 1.95f), u * 0.88f, ps[i], u >= 7);
        ci.DrawRect(new Rect2(p.X + u * 0.6f, p.Y + u * 6.9f, inner.Size.X - u * 1.2f, Mathf.Max(1, u * 0.12f)), new Color(ink, 0.35f));
        Px.TextC(ci, Px.Big, inner.GetCenter().X, p.Y + u * 8.5f, Px.Fit(Px.Big, c.LastName.ToUpperInvariant(), (int)(u * 1.8f), inner.Size.X - u), (int)(u * 1.8f), ink);
        var fs = Cards.FaceStats(c);
        for (int i = 0; i < 6; i++)
        {
            float x = p.X + u * (0.5f + (i % 3) * 3.1f);
            float y = p.Y + u * (10.2f + (i / 3) * 1.7f);
            string v = fs[i].Item2.ToString();
            int ns = (int)(u * 1.4f);
            Px.Text(ci, Px.Big, new Vector2(x, y), v, ns, ink);
            Px.Text(ci, Px.Big, new Vector2(x + Px.Width(Px.Big, v, ns) + u * 0.2f, y), fs[i].Item1, (int)(u * 0.95f), new Color(ink, 0.75f));
        }
        if (u >= 6) Px.TextC(ci, Px.Small, inner.GetCenter().X, inner.End.Y - u * 0.45f, Cards.Label(c.Rarity).ToUpperInvariant(), Math.Max(8, (int)(u * 0.75f)), new Color(ink, 0.7f));
    }

    /// <summary>Face-down card back.</summary>
    /// <summary>A playstyle badge: a hexagon in its colour, with its two letters when there's room.</summary>
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
        ci.DrawColoredPolygon(Hex(r * 0.78f), Px.Hex(0x14121c));
        if (letters) Px.TextC(ci, Px.Big, c.X, c.Y + r * 0.42f, p.Code, Math.Max(8, (int)(r * 1.15f)), col);
        else ci.DrawColoredPolygon(Hex(r * 0.36f), col);
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
        Px.TextC(ci, Px.Big, badge.GetCenter().X, badge.GetCenter().Y + u * 1.3f, "GN", (int)(u * 3.4f), c3);
        int es = (int)(u * 1.9f);
        while (es > 6 && Px.Width(Px.Big, p.Emblem, es) > inner.Size.X - u * 0.8f) es--;
        Px.TextC(ci, Px.Big, inner.GetCenter().X, inner.Position.Y + inner.Size.Y * 0.8f, p.Emblem, es, c2.Darkened(0.3f));
        if (u >= 8) Px.TextC(ci, Px.Small, inner.GetCenter().X, inner.Position.Y + inner.Size.Y * 0.9f, $"{p.Cards} PLAYERS", Math.Max(8, (int)(u * 0.7f)), c2.Darkened(0.2f));
        if (charge > 0) ci.DrawRect(inner, new Color(1, 1, 1, 0.12f * charge));
    }
}
