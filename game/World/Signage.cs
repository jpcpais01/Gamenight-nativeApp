using System;
using Godot;

namespace GameNight.Grounds;

/// <summary>A painted banner: words, colours and border style (0 bars, 1 diagonal ends, 2 frame).</summary>
public record struct BannerArt(string Text, uint Bg, uint Fg, int Style);

/// <summary>
/// All of a ground's signage in one 1024 x 1024 atlas (the gn_atlas global), drawn once by the
/// engine's own 2D renderer in an off-screen viewport, then kept as a mipmapped texture:
///   boards     x 0..512,    y 0..512    8 rows of 64 (pitchside LED designs)
///   screen     x 512..1024, y 0..288    the big screens
///   home tifo  x 512..1024, y 288..448  the ultras' card display
///   away tifo  x 512..1024, y 448..608  the travelling fans' display
///   fascia     x 0..512,    y 512..576  the club's name along the roof fronts
///   ribbon     x 0..1024,   y 608..640  scrolling LED ribbon messages
///   banners    y 768..1008, two columns of 5 rows of 48 (railing banners)
///   ground's own  y 640..768 (gables, clocks, signs: see Ground.Paint)
/// </summary>
public sealed partial class Signage : Node
{
    public static readonly (string text, uint bg, uint fg)[] Boards =
    {
        ("GAMENIGHT", 0x1f3b5c, 0xf2ede1), ("ROSSONERI", 0xc8393b, 0xffffff), ("ATLANTIC", 0xf1ebdc, 0x23345e),
        ("KESTREL", 0x2f6b4f, 0xf2ede1), ("NORTHWIND", 0xd9a93f, 0x1d1d1d), ("SEASON 01", 0x26282c, 0xffd447),
        ("FIELD & CO", 0xe9e1cc, 0xc4472f), ("HALCYON", 0x23345e, 0x9fd0ff),
    };

    readonly SubViewport _vp;
    readonly Control _root;
    readonly Font _font;
    readonly ClubArt _art;
    /// <summary>Where the finished picture goes (default: the gn_atlas global).</summary>
    readonly Action<ImageTexture> _done;
    int _frames;

    /// <summary>A one-off picture of the given size, drawn by `paint` with the same helpers,
    /// handed to `done` as a mipmapped texture once it has rendered.</summary>
    public Signage(Vector2I size, Action<Signage> paint, Action<ImageTexture> done, ClubArt art = null)
    {
        _art = art ?? new ClubArt();
        _done = done;
        (_vp, _root, _font) = Canvas(size);
        paint(this);
    }

    (SubViewport, Control, Font) Canvas(Vector2I size)
    {
        var vp = new SubViewport
        {
            Size = size,
            Disable3D = true,
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            RenderTargetClearMode = SubViewport.ClearMode.Once,
        };
        AddChild(vp);
        var root = new Control { Size = size };
        vp.AddChild(root);
        // The PWA's lettering (Barlow Condensed ExtraBold).
        var font = ResourceLoader.Exists("res://Fonts/BarlowCondensed-ExtraBold.ttf")
            ? GD.Load<Font>("res://Fonts/BarlowCondensed-ExtraBold.ttf")
            : new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 1.1f };
        return (vp, root, font);
    }

    public Signage(string clubName, uint home, uint away, BannerArt[] banners, (string text, uint bg, uint fg)[] boards = null, Action<Signage> paint = null, ClubArt art = null)
    {
        _art = art ?? new ClubArt();
        _done = tex => RenderingServer.GlobalShaderParameterSet("gn_atlas", tex);
        (_vp, _root, _font) = Canvas(new Vector2I(1024, 1024));

        boards ??= Boards;
        for (int i = 0; i < 8; i++)
        {
            var (t, bg, fg) = boards[i % boards.Length];
            Rect(new Rect2(0, i * 64, 512, 64), bg);
            Rect(new Rect2(0, i * 64, 512, 22), 0xffffff, 0.06f);
            Text(new Rect2(0, i * 64 + 4, 512, 60), t, fg, 46);
        }
        Screen(clubName, home);
        HomeTifo(clubName, home);
        AwayTifo(away);
        // Fascia: the club's name, letter-spaced, on a dark club colour.
        var dark = Col(home).Darkened(0.45f);
        Rect(new Rect2(0, 512, 512, 64), dark);
        Rect(new Rect2(0, 512, 512, 5), 0x000000, 0.25f);
        Rect(new Rect2(0, 571, 512, 5), 0x000000, 0.25f);
        Text(new Rect2(0, 514, 512, 62), Spaced(clubName), 0xf4f0e6, 40);
        // Ribbon: four messages along 1024 px.
        var segs = new (string, uint, uint)[] { ("GAMENIGHT", 0x0d1030, 0xffd447), ("SEASON 01", home, 0xffffff), ("BIG NIGHT", 0x0d1030, 0x9fd0ff), ("MATCHDAY", 0xf2ede1, 0x14123a) };
        for (int i = 0; i < 4; i++)
        {
            Rect(new Rect2(i * 256, 608, 256, 32), segs[i].Item2);
            Text(new Rect2(i * 256, 608, 256, 32), $"★  {segs[i].Item1}  ★", segs[i].Item3, 26);
        }
        for (int i = 0; i < banners.Length && i < 10; i++)
            Banner(new Rect2(i / 5 * 512, 768 + i % 5 * 48, 512, 48), banners[i]);
        paint?.Invoke(this);
    }

    static string Spaced(string s) => string.Join(' ', s.ToUpperInvariant().ToCharArray());

    public static Color Col(uint hex, float a = 1) => new(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, a);

    public void Rect(Rect2 r, uint hex, float a = 1) => Rect(r, Col(hex, a));

    public void Rect(Rect2 r, Color c) => _root.AddChild(new ColorRect { Position = r.Position, Size = r.Size, Color = c });

    public void Line(uint hex, float width, params Vector2[] pts) =>
        _root.AddChild(new Line2D { Points = pts, Width = width, DefaultColor = Col(hex), JointMode = Line2D.LineJointMode.Sharp });

    public void Picture(Texture2D tex, Rect2 r) =>
        _root.AddChild(new TextureRect { Texture = tex, Position = r.Position, Size = r.Size, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale });

    public void Poly(Color c, params Vector2[] pts) => _root.AddChild(new Polygon2D { Polygon = pts, Color = c });

    public void Poly(uint hex, params Vector2[] pts) => _root.AddChild(new Polygon2D { Polygon = pts, Color = Col(hex) });

    public void Text(Rect2 r, string text, uint fg, int size, uint? outline = null, int outlineSize = 0)
    {
        // Shrink to fit the width.
        float wantW = r.Size.X * 0.92f;
        float w = _font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        if (w > wantW) size = Math.Max(8, (int)(size * wantW / w));
        var settings = new LabelSettings { Font = _font, FontSize = size, FontColor = Col(fg) };
        if (outline is uint o) { settings.OutlineColor = Col(o); settings.OutlineSize = outlineSize; }
        _root.AddChild(new Label
        {
            Text = text,
            Position = r.Position,
            Size = r.Size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LabelSettings = settings,
            ClipText = true,
        });
    }

    public void Star(Vector2 c, float r0, float r1, uint hex)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float r = i % 2 == 1 ? r0 : r1;
            float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5;
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        Poly(hex, pts);
    }

    /// <summary>The club crest stand-in: a shield in the club colour with a gold star, `h` px tall.</summary>
    public void Crest(Vector2 c, float h, uint home)
    {
        if (_art.Crest != null)
        {
            _root.AddChild(new TextureRect
            {
                Texture = _art.Crest, Position = c - new Vector2(h, h) / 2, Size = new Vector2(h, h),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
            return;
        }
        float k = h / 150f;
        Vector2 P(float x, float y) => c + new Vector2(x, y) * k;
        Poly(0xf4efe2, P(-60, -75), P(60, -75), P(60, 15), P(0, 75), P(-60, 15));
        Poly(home, P(-52, -67), P(52, -67), P(52, 11), P(0, 63), P(-52, 11));
        Star(P(0, -9), 18 * k, 42 * k, 0xffd447);
    }

    void Screen(string name, uint home)
    {
        var r = new Rect2(512, 0, 512, 288);
        Rect(r, 0x0b1024);
        Rect(new Rect2(512, 144, 512, 100), 0x1a1640);
        Rect(new Rect2(512, 244, 512, 44), home);
        Crest(new Vector2(768, 105), 150, home);
        Text(new Rect2(512, 244, 512, 44), name.ToUpperInvariant(), 0xffffff, 34);
        // LED pixel grid.
        for (int x = 0; x < 512; x += 4) Rect(new Rect2(512 + x, 0, 1, 288), 0x000000, 0.22f);
        for (int y = 0; y < 288; y += 4) Rect(new Rect2(512, y, 512, 1), 0x000000, 0.22f);
    }

    void HomeTifo(string name, uint home)
    {
        const float x0 = 512, y0 = 288;
        Rect(new Rect2(x0, y0, 512, 160), home);
        for (float x = -40; x < 560; x += 40)
        {
            Poly(0xf4efe2, new(x0 + Mathf.Max(0, x), y0), new(x0 + Mathf.Clamp(x + 20, 0, 512), y0 + 18), new(x0 + Mathf.Min(512, x + 40), y0));
            Poly(0xf4efe2, new(x0 + Mathf.Max(0, x), y0 + 160), new(x0 + Mathf.Clamp(x + 20, 0, 512), y0 + 142), new(x0 + Mathf.Min(512, x + 40), y0 + 160));
        }
        Rect(new Rect2(x0, y0 + 26, 512, 8), 0x14123a);
        Rect(new Rect2(x0, y0 + 126, 512, 8), 0x14123a);
        Text(new Rect2(x0 + 40, y0 + 34, 432, 92), name.ToUpperInvariant(), 0xffd447, 92, 0x14123a, 10);
        Star(new Vector2(x0 + 30, y0 + 80), 8, 20, 0xffd447);
        Star(new Vector2(x0 + 482, y0 + 80), 8, 20, 0xffd447);
        // The supporters' own picture, when they've made one.
        if (_art.EndTifo != null)
            _root.AddChild(new TextureRect { Texture = _art.EndTifo, Position = new Vector2(x0, y0), Size = new Vector2(512, 160), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale });
    }

    void AwayTifo(uint away)
    {
        const float x0 = 512, y0 = 448;
        Rect(new Rect2(x0, y0, 512, 160), away);
        for (float x = -160; x < 640; x += 64)
        {
            var a = new Vector2(x, 160); var b = new Vector2(x + 32, 160); var c = new Vector2(x + 152, 0); var d = new Vector2(x + 120, 0);
            Vector2 Clip(Vector2 p) => new(x0 + Mathf.Clamp(p.X, 0, 512), y0 + p.Y);
            Poly(0x14123a, Clip(a), Clip(b), Clip(c), Clip(d));
        }
        Poly(0x14123a, Circle(new Vector2(x0 + 256, y0 + 80), 66));
        Star(new Vector2(x0 + 256, y0 + 80), 22, 54, away);
    }

    public static Vector2[] Circle(Vector2 c, float r)
    {
        var pts = new Vector2[24];
        for (int i = 0; i < 24; i++) pts[i] = c + new Vector2(Mathf.Cos(i * Mathf.Tau / 24), Mathf.Sin(i * Mathf.Tau / 24)) * r;
        return pts;
    }

    void Banner(Rect2 r, BannerArt b)
    {
        Rect(r, b.Bg);
        float H = r.Size.Y, X = r.Position.X, Y = r.Position.Y;
        if (b.Style == 0)
        {
            Rect(new Rect2(X, Y + H * 0.07f, 512, H * 0.07f), b.Fg);
            Rect(new Rect2(X, Y + H * 0.86f, 512, H * 0.07f), b.Fg);
        }
        else if (b.Style == 1)
        {
            for (float x = 0; x < 70; x += 18)
            {
                Poly(b.Fg, new(X + x + H * 0.4f, Y), new(X + x + 9 + H * 0.4f, Y), new(X + x + 9, Y + H), new(X + x, Y + H));
                Poly(b.Fg, new(X + 512 - x - H * 0.4f, Y), new(X + 503 - x - H * 0.4f, Y), new(X + 503 - x, Y + H), new(X + 512 - x, Y + H));
            }
        }
        else
        {
            float t = Mathf.Max(2, H * 0.07f);
            Rect(new Rect2(X + 4, Y + 4, 504, t), b.Fg);
            Rect(new Rect2(X + 4, Y + H - 4 - t, 504, t), b.Fg);
            Rect(new Rect2(X + 4, Y + 4, t, H - 8), b.Fg);
            Rect(new Rect2(X + 508 - t, Y + 4, t, H - 8), b.Fg);
        }
        Text(new Rect2(X + (b.Style == 1 ? 80 : 20), Y, 512 - (b.Style == 1 ? 160 : 40), H), b.Text, b.Fg, (int)(H * 0.7f), 0x0a0a14, 3);
        Rect(new Rect2(X, Y, 512, 3), 0x000000, 0.22f);
    }

    public override void _Process(double delta)
    {
        // Wait for the viewport to have drawn, then keep a mipmapped copy and drop the viewport.
        if (++_frames < 3) return;
        var img = _vp.GetTexture().GetImage();
        img.Convert(Image.Format.Rgba8);
        img.GenerateMipmaps();
        _done(ImageTexture.CreateFromImage(img));
        SetProcess(false);
        _vp.QueueFree();
    }
}
