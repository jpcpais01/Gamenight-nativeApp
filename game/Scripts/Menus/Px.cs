using System;
using Godot;

namespace GameNight.Menus;

/// <summary>
/// The menus' 90s game-night skin (the PWA's retro.css), drawn by the engine: a pixel display
/// face for titles and numbers (Jersey 10), a small pixel face for labels (Silkscreen), square
/// boxes framed by a 3 px ring with the corners stepped out and a hard drop shadow, banded
/// gradients, scanlines.
/// </summary>
public static class Px
{
    public static Font Big, Small;

    public static readonly Color Night = Hex(0x14123a), Night2 = Hex(0x1d1a4e), Night3 = Hex(0x2b2670);
    public static readonly Color Cyan = Hex(0x5ef2ff), Gold = Hex(0xffd447), Gold2 = Hex(0xffb020), Neon = Hex(0xff4fa3);
    public static readonly Color Ink = Hex(0xf4efe3), InkDim = new(0.957f, 0.937f, 0.89f, 0.62f), Dark = Hex(0x1a1406);
    public static readonly Color Line = new(190 / 255f, 200 / 255f, 1f, 0.16f), Line2 = new(190 / 255f, 200 / 255f, 1f, 0.3f);
    public static readonly Color Glass = new(16 / 255f, 14 / 255f, 44 / 255f, 0.86f), Glass2 = new(28 / 255f, 24 / 255f, 70 / 255f, 0.92f);
    public static readonly Color Shadow = new(0, 0, 0, 0.5f), ShadowSoft = new(0, 0, 0, 0.35f);
    public static readonly Color Win = Hex(0x6ff0a8), Loss = Hex(0xff8a7a);

    public static readonly Color[] GoldBands = { Hex(0xfff3a0), Hex(0xffe066), Hex(0xffbf1f), Hex(0xe69a10) };
    public static readonly float[] GoldStops = { 0, 0.18f, 0.55f, 0.82f };

    public static Rect2 Translated(this Rect2 r, Vector2 d) => new(r.Position + d, r.Size);

    public static Color Hex(int c, float a = 1) => new(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f, a);

    public static Color Shade(int c, float k) => Hex(c).Darkened(1 - k);

    public static void LoadFonts()
    {
        if (Big != null) return;
        Big = Load("res://Fonts/jersey-10.woff2");
        Small = Load("res://Fonts/silkscreen.woff2");
    }

    static Font Load(string path)
    {
        var f = ResourceLoader.Exists(path) ? GD.Load<FontFile>(path) : null;
        if (f == null) return ThemeDB.FallbackFont;
        // Hard pixels: no smoothing, no hinting, whole-pixel placement.
        f.Antialiasing = TextServer.FontAntialiasing.None;
        f.Hinting = TextServer.Hinting.None;
        f.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
        f.GenerateMipmaps = false;
        f.Fallbacks = new Godot.Collections.Array<Font> { ThemeDB.FallbackFont };
        return f;
    }

    // ---------------------------------------------------------------- boxes

    /// <summary>
    /// A framed box: `r` is the outside of the 3 px ring (corners left out, so they step), the
    /// fill sits inside it, and the shadow is a hard block offset down-right.
    /// </summary>
    public static void Frame(CanvasItem ci, Rect2 r, Color fill, Color ring, Color? shadow = null, int t = 3, int sh = 6)
    {
        if (shadow is Color s && s.A > 0) ci.DrawRect(new Rect2(r.Position + new Vector2(sh - 2 + t, sh - 2 + t), r.Size + new Vector2(4 - 2 * t, 4 - 2 * t)), s);
        Ring(ci, r, ring, t);
        if (fill.A > 0) ci.DrawRect(r.Grow(-t), fill);
    }

    public static void Ring(CanvasItem ci, Rect2 r, Color ring, int t = 3)
    {
        if (ring.A <= 0) return;
        float x = r.Position.X, y = r.Position.Y, w = r.Size.X, h = r.Size.Y;
        ci.DrawRect(new Rect2(x + t, y, w - 2 * t, t), ring);
        ci.DrawRect(new Rect2(x + t, y + h - t, w - 2 * t, t), ring);
        ci.DrawRect(new Rect2(x, y + t, t, h - 2 * t), ring);
        ci.DrawRect(new Rect2(x + w - t, y + t, t, h - 2 * t), ring);
    }

    /// <summary>Hard vertical bands: band i starts at stops[i] (0..1) of the height.</summary>
    public static void Bands(CanvasItem ci, Rect2 r, Color[] cols, float[] stops)
    {
        for (int i = 0; i < cols.Length; i++)
        {
            float a = Mathf.Round(r.Size.Y * stops[i]);
            float b = i + 1 < stops.Length ? Mathf.Round(r.Size.Y * stops[i + 1]) : r.Size.Y;
            if (b > a) ci.DrawRect(new Rect2(r.Position.X, r.Position.Y + a, r.Size.X, b - a), cols[i]);
        }
    }

    /// <summary>A framed box filled with bands.</summary>
    public static void BandFrame(CanvasItem ci, Rect2 r, Color[] cols, float[] stops, Color ring, Color? shadow = null)
    {
        Frame(ci, r, Colors.Transparent, ring, shadow);
        Bands(ci, r.Grow(-3), cols, stops);
    }

    /// <summary>Scanlines and a faint dither, over a whole screen.</summary>
    public static void Scanlines(CanvasItem ci, Rect2 r)
    {
        var c = new Color(0, 0, 0, 0.14f);
        for (float y = r.Position.Y + 2; y < r.End.Y; y += 3) ci.DrawRect(new Rect2(r.Position.X, y, r.Size.X, 1), c);
    }

    // ---------------------------------------------------------------- text

    public static float Width(Font f, string s, int size) => f.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;

    /// <summary>Text with its baseline-left at `p` (rounded to whole pixels).</summary>
    public static void Text(CanvasItem ci, Font f, Vector2 p, string s, int size, Color c, Color? shadow = null, float sh = 2)
    {
        p = p.Round();
        if (shadow is Color sc) ci.DrawString(f, p + new Vector2(sh, sh), s, HorizontalAlignment.Left, -1, size, sc);
        ci.DrawString(f, p, s, HorizontalAlignment.Left, -1, size, c);
    }

    /// <summary>Text centred horizontally on x.</summary>
    public static void TextC(CanvasItem ci, Font f, float x, float baseline, string s, int size, Color c, Color? shadow = null, float sh = 2) =>
        Text(ci, f, new Vector2(x - Width(f, s, size) / 2, baseline), s, size, c, shadow, sh);

    /// <summary>Text right-aligned to x.</summary>
    public static void TextR(CanvasItem ci, Font f, float x, float baseline, string s, int size, Color c, Color? shadow = null, float sh = 2) =>
        Text(ci, f, new Vector2(x - Width(f, s, size), baseline), s, size, c, shadow, sh);

    /// <summary>Text cut with an ellipsis to fit `max` pixels.</summary>
    public static string Fit(Font f, string s, int size, float max)
    {
        if (Width(f, s, size) <= max) return s;
        while (s.Length > 1 && Width(f, s + "…", size) > max) s = s[..^1];
        return s.TrimEnd() + "…";
    }

    /// <summary>Wraps text into lines no wider than `max`.</summary>
    public static System.Collections.Generic.List<string> Wrap(Font f, string s, int size, float max)
    {
        var lines = new System.Collections.Generic.List<string>();
        var line = "";
        foreach (var w in s.Split(' '))
        {
            var t = line.Length == 0 ? w : line + " " + w;
            if (Width(f, t, size) > max && line.Length > 0)
            {
                lines.Add(line);
                line = w;
            }
            else line = t;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }

    // ---------------------------------------------------------------- shapes

    public static Vector2[] Ellipse(Vector2 c, float rx, float ry, int n = 18)
    {
        var p = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.Tau / n;
            p[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        return p;
    }

    /// <summary>A coin: stepped gold square with a highlight.</summary>
    public static void Coin(CanvasItem ci, Vector2 p, float s)
    {
        ci.DrawRect(new Rect2(p - new Vector2(2, 2), new Vector2(s + 4, s + 4)), Hex(0x7a5208));
        Bands(ci, new Rect2(p, new Vector2(s, s)), new[] { Hex(0xffe066), Hex(0xffbf1f), Hex(0xc98a0c) }, new[] { 0, 0.45f, 0.75f });
        ci.DrawRect(new Rect2(p + new Vector2(s * 0.3f, s * 0.25f), new Vector2(s * 0.22f, s * 0.22f)), Hex(0xfff6c4));
    }

    /// <summary>A flag in three bands.</summary>
    public static void Flag(CanvasItem ci, Rect2 r, Club.Nation n)
    {
        ci.DrawRect(r.Grow(1), new Color(0, 0, 0, 0.5f));
        for (int i = 0; i < 3; i++)
        {
            var b = n.Vertical
                ? new Rect2(r.Position.X + r.Size.X * i / 3f, r.Position.Y, r.Size.X / 3f + 0.5f, r.Size.Y)
                : new Rect2(r.Position.X, r.Position.Y + r.Size.Y * i / 3f, r.Size.X, r.Size.Y / 3f + 0.5f);
            ci.DrawRect(b, Hex(n.Flag[i]));
        }
    }

    public static string Thousands(long n) => n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);

    public static string Clock(long ms)
    {
        long s = (ms + 999) / 1000;
        long h = s / 3600, m = s % 3600 / 60, ss = s % 60;
        return h > 0 ? $"{h}h {m:00}m" : $"{m}:{ss:00}";
    }
}
