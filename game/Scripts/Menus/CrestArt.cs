using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using GameNight.Club;

namespace GameNight.Menus;

/// <summary>
/// The club crest drawn the PWA's way (src/meta/crest.ts): the same SVG shapes, divisions,
/// emblems and trim, rasterised by the engine into a small texture (one texel per menu unit,
/// shown with hard pixels like the rest of the skin). The lettering is drawn over it in the
/// menus' pixel face. Textures are cached per look and size.
/// </summary>
public static class CrestArt
{
    static readonly string[] ShapePaths =
    {
        "M12 22 H88 V62 Q88 96 50 114 Q12 96 12 62 Z",
        "M10 20 Q50 14 90 20 Q92 78 50 114 Q8 78 10 20 Z",
        "M50 18 C76 18 90 36 90 62 C90 92 72 112 50 114 C28 112 10 92 10 62 C10 36 24 18 50 18 Z",
        "M50 20 A46 46 0 1 1 49.9 20 Z",
        "M14 20 H86 Q90 20 90 26 V70 Q90 100 50 114 Q10 100 10 70 V26 Q10 20 14 20 Z",
        "M50 16 L92 64 L50 114 L8 64 Z",
        "M50 16 L91 38 V90 L50 114 L9 90 V38 Z",
        "M12 20 H88 V84 Q88 98 70 100 Q56 102 50 114 Q44 102 30 100 Q12 98 12 84 Z",
        "M10 20 H90 V96 L50 114 L10 96 Z",
    };

    static string Hex(int c) => "#" + (c & 0xffffff).ToString("x6");
    static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    static string Division(int d, string sec) => d switch
    {
        1 => $"<rect x=\"50\" y=\"0\" width=\"60\" height=\"130\" fill=\"{sec}\"/>",
        2 => $"<rect x=\"0\" y=\"66\" width=\"110\" height=\"70\" fill=\"{sec}\"/>",
        3 => $"<rect x=\"50\" y=\"0\" width=\"60\" height=\"66\" fill=\"{sec}\"/><rect x=\"0\" y=\"66\" width=\"50\" height=\"70\" fill=\"{sec}\"/>",
        4 => string.Concat(Array.ConvertAll(new[] { 18, 42, 66 }, x => $"<rect x=\"{x}\" y=\"0\" width=\"12\" height=\"130\" fill=\"{sec}\"/>")),
        5 => string.Concat(Array.ConvertAll(new[] { 34, 58, 82 }, y => $"<rect x=\"0\" y=\"{y}\" width=\"110\" height=\"12\" fill=\"{sec}\"/>")),
        6 => $"<path d=\"M-10 40 L30 0 L120 90 L80 130 Z\" fill=\"{sec}\"/>",
        7 => $"<path d=\"M0 84 L50 50 L100 84 V104 L50 70 L0 104 Z\" fill=\"{sec}\"/>",
        8 => $"<path d=\"M0 10 L14 10 L100 116 L86 126 Z M100 10 L86 10 L0 116 L14 126 Z\" fill=\"{sec}\"/>",
        9 => $"<rect x=\"42\" y=\"0\" width=\"16\" height=\"130\" fill=\"{sec}\"/><rect x=\"0\" y=\"54\" width=\"110\" height=\"16\" fill=\"{sec}\"/>",
        10 => $"<rect x=\"0\" y=\"0\" width=\"110\" height=\"44\" fill=\"{sec}\"/>",
        11 => string.Concat(Array.ConvertAll(new[] { 0, 90, 180, 270 }, a => $"<path d=\"M50 64 L50 -40 L150 -40 Z\" fill=\"{sec}\" transform=\"rotate({a} 50 64)\"/>")),
        _ => "",
    };

    static string Star(double x, double y, double r, string c, string st)
    {
        var d = new StringBuilder();
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5;
            double rr = i % 2 == 1 ? r * 0.42 : r;
            d.Append(i > 0 ? 'L' : 'M').Append(F(x + Math.Cos(a) * rr)).Append(' ').Append(F(y + Math.Sin(a) * rr)).Append(' ');
        }
        return $"<path d=\"{d}Z\" fill=\"{c}\" {st}/>";
    }

    /// <summary>Emblems, drawn around (0, 0) at roughly 40 units across.</summary>
    static string Emblem(int e, string c, string dark)
    {
        string st = $"stroke=\"{dark}\" stroke-width=\"1.6\" stroke-linejoin=\"round\"";
        switch (e)
        {
            case 1: return Star(0, 0, 20, c, st);
            case 2:
                return $"<circle r=\"18\" fill=\"#f8f6f0\" {st}/><path d=\"M0 -7 L6.7 -2.2 L4.1 5.7 L-4.1 5.7 L-6.7 -2.2 Z\" fill=\"{dark}\"/>" +
                       $"<path d=\"M0 -7 V-18 M6.7 -2.2 L16.5 -5.5 M4.1 5.7 L10.6 14.6 M-4.1 5.7 L-10.6 14.6 M-6.7 -2.2 L-16.5 -5.5\" stroke=\"{dark}\" stroke-width=\"1.6\"/>";
            case 3: return $"<path d=\"M-20 10 L-22 -12 L-10 -2 L0 -18 L10 -2 L22 -12 L20 10 Z\" fill=\"{c}\" {st}/><rect x=\"-20\" y=\"10\" width=\"40\" height=\"7\" rx=\"1.5\" fill=\"{c}\" {st}/><circle cy=\"-18\" r=\"2.6\" fill=\"{c}\" {st}/>";
            case 4: return $"<path d=\"M5 -22 L-13 3 H-1 L-6 22 L14 -5 H2 Z\" fill=\"{c}\" {st}/>";
            case 5: return $"<path d=\"M-18 18 V-8 H-12 V-16 H-6 V-8 H-2 V-16 H2 V-8 H6 V-16 H12 V-8 H18 V18 H5 V6 A5 5 0 0 0 -5 6 V18 Z\" fill=\"{c}\" {st}/>";
            case 6: return $"<g fill=\"none\" stroke=\"{c}\" stroke-width=\"5\" stroke-linecap=\"round\"><circle cy=\"-15\" r=\"4.5\"/><path d=\"M0 -10 V18 M-11 -3 H11 M-17 6 Q-15 20 0 19 Q15 20 17 6\"/></g>";
            case 7: return $"<path d=\"M0 22 C-15 22 -18 8 -12 -2 C-10 4 -6 6 -5 3 C-8 -8 -2 -16 4 -22 C3 -12 13 -8 14 4 C15 14 9 22 0 22 Z\" fill=\"{c}\" {st}/><path d=\"M0 19 C-6 19 -7 12 -3 6 C-2 10 1 10 1 8 C4 11 6 13 5 16 C4 18 2 19 0 19 Z\" fill=\"{dark}\" opacity=\".55\"/>";
            case 8: return $"<path d=\"M0 6 C-6 -10 -20 -14 -24 -6 C-20 -6 -18 -2 -18 0 C-22 0 -24 4 -22 8 C-18 6 -14 8 -14 10 C-10 8 -4 10 0 14 C4 10 10 8 14 10 C14 8 18 6 22 8 C24 4 22 0 18 0 C18 -2 20 -6 24 -6 C20 -14 6 -10 0 6 Z\" fill=\"{c}\" {st}/>";
            case 9: return $"<path d=\"M0 -18 C5 -18 8 -14 7 -9 L12 -10 L9 -5 C16 -8 24 -12 26 -4 C20 -4 16 0 14 4 C10 2 8 6 8 10 L4 20 L0 14 L-4 20 L-8 10 C-8 6 -10 2 -14 4 C-16 0 -20 -4 -26 -4 C-24 -12 -16 -8 -9 -5 C-10 -14 -5 -18 0 -18 Z\" fill=\"{c}\" {st}/><circle cx=\"2\" cy=\"-12\" r=\"1.4\" fill=\"{dark}\"/>";
            case 10: return $"<path d=\"M-4 -20 C10 -22 20 -12 18 2 C24 6 22 14 16 14 C14 20 6 22 0 20 C-6 22 -14 20 -16 14 C-22 14 -24 6 -18 2 C-20 -10 -14 -18 -4 -20 Z\" fill=\"{c}\" {st}/><path d=\"M-7 -4 L-3 -2 M7 -4 L3 -2 M-5 8 Q0 12 5 8 M0 2 V7\" stroke=\"{dark}\" stroke-width=\"1.8\" fill=\"none\" stroke-linecap=\"round\"/>";
            case 11: return $"<path d=\"M0 -22 C6 -16 14 -16 12 -8 C20 -8 20 2 14 4 C20 8 14 16 8 12 C6 18 -6 18 -8 12 C-14 16 -20 8 -14 4 C-20 2 -20 -8 -12 -8 C-14 -16 -6 -16 0 -22 Z\" fill=\"{c}\" {st}/><path d=\"M0 -10 V22\" stroke=\"{dark}\" stroke-width=\"2.2\"/>";
            case 12: return $"<path d=\"M-14 -20 L-6 -8 H6 L14 -20 L16 0 C16 12 8 20 0 22 C-8 20 -16 12 -16 0 Z\" fill=\"{c}\" {st}/><path d=\"M-8 2 L-3 4 M8 2 L3 4 M-4 14 L0 17 L4 14\" stroke=\"{dark}\" stroke-width=\"1.8\" fill=\"none\" stroke-linecap=\"round\"/>";
            case 13:
            {
                var sb = new StringBuilder($"<g fill=\"{c}\" {st}>");
                for (int i = 0; i < 12; i++) sb.Append($"<path d=\"M-3 -14 L0 -23 L3 -14 Z\" transform=\"rotate({i * 30})\"/>");
                return sb.Append("<circle r=\"12\"/></g>").ToString();
            }
            case 14: return Star(-14, 4, 9, c, st) + Star(14, 4, 9, c, st) + Star(0, -8, 11, c, st);
            default: return "";
        }
    }

    static bool Light(int c) => 0.299 * ((c >> 16) & 255) + 0.587 * ((c >> 8) & 255) + 0.114 * (c & 255) > 150;

    /// <summary>Light or dark ink for text on a colour.</summary>
    static int Ink(int c) => Light(c) ? 0x14121c : 0xffffff;

    static string Upper(Crest c)
    {
        var t = (c.Text ?? "").ToUpperInvariant();
        return t.Length > 4 ? t[..4] : t;
    }

    /// <summary>The crest as SVG (viewBox 0 0 100 124) without its lettering.</summary>
    public static string Svg(Crest c, int width)
    {
        string shape = ShapePaths[((c.Shape % ShapePaths.Length) + ShapePaths.Length) % ShapePaths.Length];
        string P = Hex(c.Primary), S = Hex(c.Secondary), A = Hex(c.Accent);
        string dark = !Light(c.Accent) ? A : "#14121c";
        string text = Upper(c);
        int ts = c.TextStyle;
        int ey = ts == 0 ? 52 : ts == 1 ? 58 : ts == 2 ? 72 : 64;
        double es = text.Length > 0 && ts == 0 ? 0.85 : 1.1;
        double borderW = new[] { 0, 2.5, 5, 2.5, 4 }[((c.Border % 5) + 5) % 5];
        string borderC = c.Border == 4 ? "#e8c35a" : A;
        int stars = Math.Clamp(c.Stars, 0, 5);
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 124\" width=\"{width}\" height=\"{Math.Round(width * 1.24)}\">");
        sb.Append($"<defs><clipPath id=\"c\"><path d=\"{shape}\"/></clipPath><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"0.4\" y2=\"1\"><stop offset=\"0\" stop-color=\"#fff\" stop-opacity=\".22\"/><stop offset=\".5\" stop-color=\"#fff\" stop-opacity=\"0\"/><stop offset=\"1\" stop-color=\"#000\" stop-opacity=\".18\"/></linearGradient></defs>");
        for (int i = 0; i < stars; i++) sb.Append(Star(50 + (i - (stars - 1) / 2.0) * 11, 8, 4.5, "#e8c35a", ""));
        sb.Append($"<g clip-path=\"url(#c)\"><rect width=\"100\" height=\"130\" fill=\"{P}\"/>{Division(c.Division, S)}<rect width=\"100\" height=\"130\" fill=\"url(#g)\"/>");
        if (text.Length > 0 && ts == 2) sb.Append($"<rect x=\"0\" y=\"22\" width=\"100\" height=\"26\" fill=\"{A}\"/>");
        sb.Append("</g>");
        if (c.Emblem > 0) sb.Append($"<g transform=\"translate(50 {ey}) scale({F(es)})\">{Emblem(c.Emblem, A, dark)}</g>");
        if (borderW > 0) sb.Append($"<path d=\"{shape}\" fill=\"none\" stroke=\"{borderC}\" stroke-width=\"{F(borderW)}\"/>");
        if (c.Border == 3) sb.Append($"<path d=\"{shape}\" fill=\"none\" stroke=\"{borderC}\" stroke-width=\"1.4\" transform=\"translate(50 64) scale(0.88) translate(-50 -64)\"/>");
        sb.Append($"<path d=\"{shape}\" fill=\"none\" stroke=\"#000\" stroke-opacity=\".35\" stroke-width=\"0.8\"/>");
        if (ts == 1 && (text.Length > 0 || (c.Year ?? "").Length > 0))
        {
            sb.Append($"<path d=\"M2 84 L14 80 L14 98 L2 101 L7 92 Z M98 84 L86 80 L86 98 L98 101 L93 92 Z\" fill=\"{S}\" stroke=\"{dark}\" stroke-width=\"1\"/>");
            sb.Append($"<path d=\"M12 78 Q50 70 88 78 V96 Q50 88 12 96 Z\" fill=\"{A}\" stroke=\"{dark}\" stroke-width=\"1.2\"/>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    static readonly Dictionary<string, ImageTexture> Cache = new();

    public static ImageTexture Texture(Crest c, int width)
    {
        width = Math.Clamp(width, 8, 1024);
        string key = c.Key + "@" + width;
        if (Cache.TryGetValue(key, out var t)) return t;
        if (Cache.Count > 64) Cache.Clear();
        var img = new Image();
        img.LoadSvgFromString(Svg(c, width), 1f);
        t = ImageTexture.CreateFromImage(img);
        Cache[key] = t;
        return t;
    }

    /// <summary>The crest with its lettering, in a box about 100 x 124.</summary>
    public static void Draw(CanvasItem ci, Rect2 r, Crest c)
    {
        float w = Mathf.Min(r.Size.X, r.Size.Y / 1.24f);
        var box = new Rect2(r.Position + new Vector2((r.Size.X - w) / 2, 0), new Vector2(w, w * 1.24f));
        ci.DrawTextureRect(Texture(c, Mathf.RoundToInt(w)), box, false);
        float k = w / 100f;
        Vector2 At(float x, float y) => box.Position + new Vector2(x * k, y * k);
        string text = Upper(c);
        string year = c.Year ?? "";
        int ts = c.TextStyle;
        var A = Px.Hex(c.Accent);
        var onA = Px.Hex(Ink(c.Accent));
        var dark = Light(c.Accent) ? Px.Hex(0x14121c) : A.Darkened(0.6f);
        if (text.Length > 0 && ts == 0)
            Px.TextC(ci, Px.Big, At(50, 0).X, At(0, 88 + 2).Y, text, Size(k * (text.Length > 3 ? 21 : 26)), A, dark, Mathf.Max(1, k));
        else if (text.Length > 0 && ts == 2)
            Px.TextC(ci, Px.Big, At(50, 0).X, At(0, 42 + 1).Y, text, Size(k * 24), onA);
        if (ts == 1 && (text.Length > 0 || year.Length > 0))
        {
            Px.TextC(ci, Px.Big, At(50, 0).X, At(0, (year.Length > 0 && text.Length > 0 ? 89 : 91) + 1).Y, text.Length > 0 ? text : year, Size(k * (text.Length > 0 ? 16 : 11)), onA);
            if (text.Length > 0 && year.Length > 0 && k >= 0.8f) Px.TextC(ci, Px.Small, At(50, 0).X, At(0, 109).Y, "EST. " + year, Size(k * 6.5f), A);
        }
        else if (ts != 1 && year.Length > 0 && k >= 0.8f)
            Px.TextC(ci, Px.Small, At(50, 0).X, At(0, ts == 2 ? 105 : 107).Y, year, Size(k * 6.5f), A);
    }

    static int Size(float s) => Math.Max(6, Mathf.RoundToInt(s));

    /// <summary>A surprise crest in the club's colours.</summary>
    public static Crest Random(Crest from, int main, int second)
    {
        var rng = new System.Random();
        var c = from.Clone();
        c.Shape = rng.Next(Crest.Shapes.Length);
        c.Division = rng.Next(Crest.Divisions.Length);
        c.Emblem = 1 + rng.Next(Crest.Emblems.Length - 1);
        c.TextStyle = rng.Next(3);
        c.Border = rng.Next(Crest.Borders.Length);
        c.Stars = rng.Next(4) == 0 ? 1 + rng.Next(3) : 0;
        c.Primary = main;
        c.Secondary = second;
        c.Accent = rng.Next(2) == 0 ? 0xf3ede0 : 0xffd447;
        return c;
    }
}
