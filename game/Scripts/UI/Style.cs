using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.UI;

/// <summary>
/// The PWA's look (style.css): its colours, Barlow Condensed, and the few drawing helpers the
/// HUD and menus share. Sizes are in the same units as the PWA's CSS pixels (the window is
/// stretched from an 860x400 base, about what a phone's browser reports in landscape).
/// </summary>
public static class Style
{
    public static readonly Color Ink = Hex(0xf4efe3);
    public static readonly Color InkDim = new(Ink, 0.72f);
    public static readonly Color Panel = new(20 / 255f, 26 / 255f, 22 / 255f, 0.78f);
    public static readonly Color PanelSolid = Hex(0x18201b);
    public static readonly Color Accent = Hex(0xffd447);
    public static readonly Color Red = Hex(0xc8393b);

    public static Color Hex(int rgb, float a = 1) =>
        new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);

    static FontFile _bold, _semi;
    static readonly Dictionary<(bool, int), FontVariation> _fonts = new();

    /// <summary>Barlow Condensed, ExtraBold (800) or SemiBold (600), with letter spacing in pixels.</summary>
    public static Font Font(bool bold = true, float spacing = 0)
    {
        _bold ??= GD.Load<FontFile>("res://Fonts/BarlowCondensed-ExtraBold.ttf");
        _semi ??= GD.Load<FontFile>("res://Fonts/BarlowCondensed-SemiBold.ttf");
        int key = (int)MathF.Round(spacing * 4);
        if (_fonts.TryGetValue((bold, key), out var f)) return f;
        f = new FontVariation { BaseFont = bold ? _bold : _semi, SpacingGlyph = (int)MathF.Round(spacing) };
        _fonts[(bold, key)] = f;
        return f;
    }

    /// <summary>Text width at a size.</summary>
    public static float Width(Font f, string s, int size) => f.GetStringSize(s, HorizontalAlignment.Left, -1, size).X;

    /// <summary>Text drawn vertically centred in a box, left-aligned at x (or centred in the box's width).</summary>
    public static void Text(CanvasItem c, Font f, string s, Rect2 box, int size, Color col, bool centre = true)
    {
        float asc = f.GetAscent(size), desc = f.GetDescent(size);
        float y = box.Position.Y + (box.Size.Y + asc - desc) / 2;
        if (centre)
            c.DrawString(f, new Vector2(box.Position.X, y), s, HorizontalAlignment.Center, box.Size.X, size, col);
        else
            c.DrawString(f, new Vector2(box.Position.X, y), s, HorizontalAlignment.Left, -1, size, col);
    }

    static readonly Dictionary<(Color, int, int, int, int, int), StyleBoxFlat> _boxes = new();

    /// <summary>A filled rounded rectangle (radii: top-left, top-right, bottom-right, bottom-left), with an optional soft shadow.</summary>
    public static void Corners(CanvasItem c, Rect2 r, Color col, int tl, int tr, int br, int bl, int shadow = 0)
    {
        var key = (col, tl, tr, br, bl, shadow);
        if (!_boxes.TryGetValue(key, out var sb))
        {
            sb = new StyleBoxFlat
            {
                BgColor = col,
                CornerRadiusTopLeft = tl,
                CornerRadiusTopRight = tr,
                CornerRadiusBottomRight = br,
                CornerRadiusBottomLeft = bl,
                AntiAliasing = true,
                CornerDetail = 6,
            };
            if (shadow > 0)
            {
                sb.ShadowColor = new Color(0, 0, 0, 0.3f);
                sb.ShadowSize = shadow;
                sb.ShadowOffset = new Vector2(0, shadow * 0.3f);
            }
            _boxes[key] = sb;
        }
        c.DrawStyleBox(sb, r);
    }

    public static void Box(CanvasItem c, Rect2 r, Color col, int radius = 0, int shadow = 0) => Corners(c, r, col, radius, radius, radius, radius, shadow);

    /// <summary>A left-to-right colour ramp (the PWA's gauge gradients), as a texture.</summary>
    public static GradientTexture1D Ramp(params (float at, int rgb)[] stops)
    {
        var g = new Gradient();
        g.Offsets = Array.ConvertAll(stops, s => s.at);
        g.Colors = Array.ConvertAll(stops, s => Hex(s.rgb));
        return new GradientTexture1D { Gradient = g, Width = 128 };
    }

    public static float Smooth(float x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    public static float EaseOut(float x)
    {
        x = Math.Clamp(x, 0, 1);
        return 1 - (1 - x) * (1 - x) * (1 - x);
    }
}
