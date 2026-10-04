using System;
using System.Collections.Generic;
using Godot;
using GameNight.Club;
using GameNight.Menus;
using GameNight.Sim;

namespace GameNight.League;

/// <summary>The league's drawings: small badges, form guides, table zones, the trophy and the
/// newspaper's pixel photographs.</summary>
public static class LeagueArt
{
    static readonly Dictionary<string, ImageTexture> Minis = new();

    /// <summary>A club badge. Big ones carry their lettering (CrestArt); small ones are just the
    /// shield, from our own cache so a table of sixteen never churns the crest cache.</summary>
    public static void Badge(CanvasItem ci, Rect2 r, Crest c)
    {
        float w = Mathf.Min(r.Size.X, r.Size.Y / 1.24f);
        if (w >= 30)
        {
            CrestArt.Draw(ci, r, c);
            return;
        }
        int iw = Math.Max(8, Mathf.RoundToInt(w));
        string key = c.Key + "@" + iw;
        if (!Minis.TryGetValue(key, out var t))
        {
            if (Minis.Count > 160) Minis.Clear();
            var img = new Image();
            img.LoadSvgFromString(CrestArt.Svg(c, iw), 1f);
            Minis[key] = t = ImageTexture.CreateFromImage(img);
        }
        ci.DrawTextureRect(t, new Rect2(r.Position + new Vector2((r.Size.X - iw) / 2, 0), new Vector2(iw, iw * 1.24f)), false);
    }

    public static readonly Color Gold = Px.Hex(0xffd447), Euro = Px.Hex(0x5ef2ff), Drop = Px.Hex(0xff6b6b);

    /// <summary>The colour of a table position's zone: champions, the next three, the drop.</summary>
    public static Color? Zone(int pos) => pos == 1 ? Gold : pos <= 4 ? Euro : pos >= LeagueState.Clubs - 2 ? Drop : null;

    public static Color ResultColor(char c) => c == 'W' ? Px.Hex(0x3ddc84) : c == 'D' ? Px.Hex(0x9a98b0) : Px.Hex(0xe8504f);

    /// <summary>The last five results as little squares (oldest left); returns the width.</summary>
    public static float Form(CanvasItem ci, Vector2 p, string form, float s, bool letters = true)
    {
        for (int i = 0; i < 5; i++)
        {
            var r = new Rect2(p.X + i * (s + 2), p.Y, s, s);
            int k = i - (5 - form.Length);
            if (k < 0)
            {
                ci.DrawRect(r, new Color(1, 1, 1, 0.08f));
                continue;
            }
            ci.DrawRect(r, ResultColor(form[k]));
            if (letters && s >= 12) Px.TextC(ci, Px.Small, r.GetCenter().X + 0.5f, r.GetCenter().Y + 4, form[k].ToString(), 8, Px.Hex(0x0e0c1e));
        }
        return 5 * (s + 2) - 2;
    }

    /// <summary>A gold cup with handles on a two-step plinth, in a box about 100 x 130.</summary>
    public static void Trophy(CanvasItem ci, Rect2 r, float shine = 0)
    {
        var k = r.Size / new Vector2(100, 130);
        Vector2 P(float x, float y) => r.Position + new Vector2(x, y) * k;
        void Poly(Color c, params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = P(xy[i * 2], xy[i * 2 + 1]);
            ci.DrawColoredPolygon(pts, c);
        }
        var g0 = Px.Hex(0xfff3a0);
        var g1 = Px.Hex(0xffd447);
        var g2 = Px.Hex(0xe0a01c);
        var g3 = Px.Hex(0x9a6408);
        // Handles behind the bowl.
        Poly(g2, 14, 18, 26, 18, 26, 26, 20, 26, 20, 44, 32, 58, 28, 64, 12, 48, 12, 22);
        Poly(g2, 86, 18, 74, 18, 74, 26, 80, 26, 80, 44, 68, 58, 72, 64, 88, 48, 88, 22);
        // Bowl: bands from light to dark.
        Poly(g1, 22, 10, 78, 10, 76, 40, 66, 60, 56, 68, 44, 68, 34, 60, 24, 40);
        Poly(g0, 30, 14, 42, 14, 40, 44, 36, 56, 30, 42);
        Poly(g2, 66, 14, 76, 14, 74, 40, 64, 60, 58, 64, 64, 44);
        ci.DrawRect(new Rect2(P(20, 6), new Vector2(60, 6) * k), g0);
        // Stem and plinth.
        Poly(g2, 44, 68, 56, 68, 54, 86, 46, 86);
        ci.DrawRect(new Rect2(P(34, 86), new Vector2(32, 8) * k), g1);
        ci.DrawRect(new Rect2(P(26, 96), new Vector2(48, 16) * k), Px.Hex(0x3a2a1a));
        ci.DrawRect(new Rect2(P(20, 112), new Vector2(60, 14) * k), Px.Hex(0x261a10));
        ci.DrawRect(new Rect2(P(36, 101), new Vector2(28, 6) * k), g3);
        if (shine > 0) ci.DrawRect(new Rect2(P(30 + shine * 40, 12), new Vector2(5, 46) * k), new Color(1, 1, 1, 0.55f));
    }

    // ---------------------------------------------------------------- the paper's photographs

    static readonly Dictionary<string, ImageTexture> Photos = new();

    /// <summary>
    /// A press photo in pixels (80 x 50): the stand full of fans in the hosts' colours, the
    /// boards, the grass, and our man in the club's kit: arms up, hands on head, a handshake
    /// or the trophy aloft. Printed like newsprint: the background toned down to warm grey, the
    /// kit left in colour.
    /// </summary>
    public static ImageTexture Photo(Photo kind, Kit kit, Kit other, int skin, int hair, int host, int seed)
    {
        string key = $"{kind}|{kit.Shirt}|{kit.Shirt2}|{kit.Shorts}|{other.Shirt}|{skin}|{hair}|{host}|{seed}";
        if (Photos.TryGetValue(key, out var tex)) return tex;
        if (Photos.Count > 12) Photos.Clear();
        const int W = 80, H = 50;
        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        var spot = new bool[W, H];
        var rng = new Random(seed);
        Color C(int hex) => Px.Hex(hex);
        void Set(int x, int y, Color c, bool colour = false)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            img.SetPixel(x, y, c);
            spot[x, y] = colour;
        }
        void Box(int x, int y, int w, int h, Color c, bool colour = false)
        {
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++) Set(i, j, c, colour);
        }

        // The stand: tiers of heads, a third of them in the hosts' colour.
        var hostC = C(host);
        for (int y = 0; y < 27; y++)
            for (int x = 0; x < W; x++)
            {
                bool tierLine = y % 5 == 4;
                var c = tierLine ? C(0x2a2630) : C(0x3a3542);
                if (!tierLine && (x + y * 3) % 2 == 0)
                {
                    int roll = rng.Next(10);
                    c = roll < 3 ? hostC : roll < 5 ? C(0xd9c7a8) : roll < 7 ? C(0x6b6270) : roll < 8 ? C(0xf3eee2) : C(0x8a5a3a);
                }
                Set(x, y, c);
            }
        // Boards, then the grass in mown stripes.
        for (int x = 0; x < W; x++)
        {
            Set(x, 27, C(0x14121c));
            Set(x, 28, (x / 10) % 2 == 0 ? hostC : C(0xf3eee2));
            Set(x, 29, (x / 10) % 2 == 0 ? hostC.Darkened(0.3f) : C(0xc9c2b0));
        }
        for (int y = 30; y < H; y++)
            for (int x = 0; x < W; x++) Set(x, y, ((x + (y - 30) / 3) / 7) % 2 == 0 ? C(0x3f8a3a) : C(0x357a32));

        var shirt = C(kit.Shirt);
        var trim = C(kit.Shirt2);
        var shorts = C(kit.Shorts);
        var sk = C(TeamData.SkinTones[Math.Clamp(skin, 0, TeamData.SkinTones.Length - 1)]);
        var hc = C(TeamData.HairColors[Math.Clamp(hair, 0, TeamData.HairColors.Length - 1)]);
        var boot = C(0x14121c);

        // One player, front on, feet at row 47, centred on cx.
        void Body(int cx, Color sh, Color tr, Color so, bool bowed)
        {
            int top = bowed ? 18 : 16;
            Box(cx - 5, 41, 3, 6, sk, true);
            Box(cx + 2, 41, 3, 6, sk, true);
            Box(cx - 5, 43, 3, 3, sh, true); // socks
            Box(cx + 2, 43, 3, 3, sh, true);
            Box(cx - 6, 47, 4, 2, boot);
            Box(cx + 2, 47, 4, 2, boot);
            Box(cx - 6, 35, 12, 6, so, true);
            Box(cx - 7, top + 8, 14, 12 - (bowed ? 2 : 0), sh, true);
            Box(cx - 7, top + 8, 14, 1, tr, true);
            Box(cx - 1, top + 9, 2, 2, tr, true);
            Box(cx - 3, top, 6, 7, sk, true);
            Box(cx - 3, top, 6, 2, hc, true);
            Box(cx - 1, top + 7, 2, 1, sk.Darkened(0.2f), true);
        }

        switch (kind)
        {
            case League.Photo.Celebrate:
            case League.Photo.Trophy:
            {
                Body(40, shirt, trim, shorts, false);
                // Arms up in a V.
                for (int i = 0; i < 6; i++)
                {
                    Box(31 - i, 23 - i * 2, 3, 3, i < 3 ? shirt : sk, true);
                    Box(46 + i, 23 - i * 2, 3, 3, i < 3 ? shirt : sk, true);
                }
                Box(38, 21, 4, 1, C(0x5a1e1e), true); // shouting
                if (kind == League.Photo.Trophy)
                {
                    // The cup held high between the hands.
                    var g = C(0xffd447);
                    Box(29, 4, 22, 2, C(0xfff3a0), true);
                    Box(31, 6, 18, 5, g, true);
                    Box(34, 11, 12, 2, g.Darkened(0.2f), true);
                    Box(38, 13, 4, 2, C(0xe0a01c), true);
                    Box(27, 5, 2, 4, g, true);
                    Box(51, 5, 2, 4, g, true);
                }
                // Confetti and flashbulbs.
                for (int i = 0; i < (kind == League.Photo.Trophy ? 46 : 14); i++)
                {
                    int x = rng.Next(W), y = rng.Next(28);
                    Set(x, y, i % 3 == 0 ? C(0xffffff) : i % 3 == 1 ? shirt.Lightened(0.3f) : C(0xffd447), true);
                }
                break;
            }
            case League.Photo.Despair:
            {
                Body(40, shirt, trim, shorts, true);
                // Hands on head, elbows out.
                Box(30, 24, 4, 3, shirt, true);
                Box(46, 24, 4, 3, shirt, true);
                Box(31, 20, 3, 4, sk, true);
                Box(46, 20, 3, 4, sk, true);
                Box(34, 17, 3, 3, sk, true);
                Box(43, 17, 3, 3, sk, true);
                break;
            }
            default:
            {
                // The handshake: ours on the left, theirs on the right.
                var os = C(other.Shirt);
                Body(26, shirt, trim, shorts, false);
                Body(54, os, C(other.Shirt2), C(other.Shorts), false);
                for (int i = 0; i < 4; i++)
                {
                    Box(33 + i * 2, 27 + (i > 1 ? 1 : 0), 2, 3, i < 2 ? shirt : sk, true);
                    Box(45 - i * 2, 27 + (i > 1 ? 1 : 0), 2, 3, i < 2 ? os : sk, true);
                }
                Box(25, 32, 2, 3, sk, true);
                Box(53, 32, 2, 3, sk, true);
                break;
            }
        }

        // Newsprint: everything but the kit and the players toned to warm grey, with a fine screen.
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var c = img.GetPixel(x, y);
                float l = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
                var grey = new Color(l * 1.02f + 0.04f, l * 0.97f + 0.03f, l * 0.86f + 0.02f);
                c = spot[x, y] ? c.Lerp(grey, 0.15f) : c.Lerp(grey, 0.72f);
                if ((x + y) % 2 == 0) c = c.Darkened(0.06f);
                img.SetPixel(x, y, c);
            }
        tex = ImageTexture.CreateFromImage(img);
        Photos[key] = tex;
        return tex;
    }
}
