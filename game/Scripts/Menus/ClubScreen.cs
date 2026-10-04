using System;
using Godot;
using GameNight.Club;
using GameNight.Sim;

namespace GameNight.Menus;

/// <summary>
/// The club studio (the PWA's clubScreen.ts, kit part): name and scoreboard code, the kit's
/// colours and design, the club's record, and starting over.
/// </summary>
public sealed partial class ClubScreen : PxCanvas
{
    /// <summary>Curated swatches: club colours that look good together.</summary>
    static readonly int[] Palette =
    {
        0xc8393b, 0x8f1f24, 0xe0522b, 0xf28c28, 0xffd447, 0xe8c35a, 0x3ddc84, 0x1f6b4a,
        0x0f3d2e, 0x2fb6a8, 0x7ff6ff, 0x4aa3ff, 0x2457d6, 0x23345e, 0x14123a, 0x6a3fd1,
        0xb05cff, 0xe0559b, 0xf3ede0, 0xffffff, 0xb9bdc4, 0x6b6f78, 0x2a2a2a, 0x0e0e10,
    };

    readonly Menus _ui;
    readonly LineEdit _name, _code;
    int _slot; // 0 main, 1 secondary, 2 shorts
    bool _resetArmed;
    ClubState Club => _ui.Club;

    public ClubScreen(Menus ui)
    {
        _ui = ui;
        _name = Field(24);
        _code = Field(3);
        _name.TextSubmitted += _ => Commit();
        _code.TextSubmitted += _ => Commit();
        _name.FocusExited += Commit;
        _code.FocusExited += Commit;
        VisibilityChanged += () =>
        {
            if (!Visible) return;
            _name.Text = Club.S.Name;
            _code.Text = Club.ShortName;
            _resetArmed = false;
        };
    }

    LineEdit Field(int max)
    {
        var e = new LineEdit { MaxLength = max, CaretBlink = true, ContextMenuEnabled = false };
        var box = new StyleBoxFlat
        {
            BgColor = new Color(8 / 255f, 6 / 255f, 26 / 255f, 0.9f),
            BorderColor = Px.Line2,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 2, ContentMarginBottom = 2,
        };
        box.SetBorderWidthAll(3);
        var focus = (StyleBoxFlat)box.Duplicate();
        focus.BorderColor = Px.Gold;
        e.AddThemeStyleboxOverride("normal", box);
        e.AddThemeStyleboxOverride("focus", focus);
        e.AddThemeFontOverride("font", Px.Big);
        e.AddThemeFontSizeOverride("font_size", 26);
        e.AddThemeColorOverride("font_color", Px.Ink);
        e.AddThemeColorOverride("caret_color", Px.Gold);
        AddChild(e);
        return e;
    }

    void Commit()
    {
        if (_name.Text.Trim() != Club.S.Name) Club.Rename(_name.Text);
        string code = _code.Text.Trim();
        if (code.Length == 0 || code.ToUpperInvariant() != Club.ShortName) Club.SetShort(code);
        _name.Text = Club.S.Name;
        _code.Text = Club.ShortName;
    }

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        NightBackdrop();
        BackButton(new Vector2(14, 12), () =>
        {
            Commit();
            _ui.Go(_ui.Home);
        });
        Title(new Vector2(66, 44), "CLUB");
        var k = Club.S.Kit;

        // Stage: the kit big, the crest beside it, name and code below.
        float sw = Mathf.Round(W * 0.42f);
        var stage = new Rect2(14, 62, sw, H - 62 - 14);
        Px.Frame(this, stage, new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.6f), Px.Line, Px.Shadow);
        float jh = stage.Size.Y - 96, jw = jh / 1.3f;
        var glow = new Vector2(stage.Position.X + stage.Size.X * 0.42f, stage.Position.Y + 12 + jh / 2);
        foreach (var (rr, a) in new[] { (0.62f, 0.08f), (0.45f, 0.12f) })
            DrawColoredPolygon(Px.Ellipse(glow, jh * rr, jh * rr, 24), new Color(Px.Hex(k.Main), a));
        Art.Jersey(this, new Rect2(glow.X - jw / 2, stage.Position.Y + 14, jw, jh), k);
        Art.Crest(this, new Rect2(stage.End.X - 78, stage.Position.Y + 16, 60, 70), k.Main, k.Secondary, Club.ShortName);
        float fy = stage.End.Y - 64;
        Px.Text(this, Px.Small, new Vector2(stage.Position.X + 14, fy - 6), "CLUB NAME", 8, Px.InkDim);
        Px.Text(this, Px.Small, new Vector2(stage.End.X - 92, fy - 6), "CODE", 8, Px.InkDim);
        _name.Position = new Vector2(stage.Position.X + 14, fy);
        _name.Size = new Vector2(stage.Size.X - 130, 44);
        _code.Position = new Vector2(stage.End.X - 92, fy);
        _code.Size = new Vector2(78, 44);

        // Panel: colours and design.
        float px = stage.End.X + 22, pw = W - 14 - px;
        float y = 76;
        Px.Text(this, Px.Big, new Vector2(px, y + 4), "COLOURS", 24, Px.Ink);
        float cx = px + 110;
        string[] names = { "Main", "Secondary", "Shorts" };
        int[] cols = { k.Main, k.Secondary, k.Shorts };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            float w = Chip("slot" + i, new Vector2(cx, y - 16), "   " + names[i], _slot == i, () => _slot = idx);
            var sw2 = new Rect2(cx + 8, y - 8, 12, 12);
            DrawRect(sw2.Grow(1), Colors.Black);
            DrawRect(sw2, Px.Hex(cols[i]));
            cx += w + 8;
        }
        y += 22;
        int perRow = 12;
        float s = Mathf.Min(34, (pw - (perRow - 1) * 6) / perRow);
        for (int i = 0; i < Palette.Length; i++)
        {
            int c = Palette[i];
            var r = new Rect2(px + i % perRow * (s + 6), y + i / perRow * (s + 6), s, s);
            bool on = cols[_slot] == c;
            if (on) Px.Frame(this, r.Grow(4), Colors.Transparent, Px.Gold, null, 3, 0);
            DrawRect(r, Colors.Black);
            DrawRect(r.Grow(-2), Px.Hex(c));
            Tap("sw" + i, r, () =>
            {
                var nk = new ClubKit { Pattern = k.Pattern, Main = k.Main, Secondary = k.Secondary, Shorts = k.Shorts };
                if (_slot == 0) nk.Main = c;
                else if (_slot == 1) nk.Secondary = c;
                else nk.Shorts = c;
                Club.SetKit(nk);
            });
        }
        y += 2 * (s + 6) + 30;
        Px.Text(this, Px.Big, new Vector2(px, y), "DESIGN", 24, Px.Ink);
        y += 10;
        float dx = px;
        for (int i = 0; i < TeamData.KitPatterns.Length; i++)
        {
            int idx = i;
            string label = TeamData.KitPatterns[i];
            float w = Px.Width(Px.Big, label, 18) + 18;
            if (dx + w > px + pw)
            {
                dx = px;
                y += 34;
            }
            Chip("pat" + i, new Vector2(dx, y), label, k.Pattern == i, () =>
                Club.SetKit(new ClubKit { Pattern = idx, Main = k.Main, Secondary = k.Secondary, Shorts = k.Shorts }));
            dx += w + 8;
        }

        // The record, and starting over.
        var rec = Club.S.Record;
        Px.Text(this, Px.Small, new Vector2(px, H - 26), $"PLAYED {rec.Played} · GOALS {rec.Gf}-{rec.Ga} · PACKS OPENED {Club.S.PacksOpened}", 8, Px.InkDim);
        float rw = _resetArmed ? 300 : 160;
        GhostButton("reset", new Rect2(W - 14 - rw, H - 50, rw, 36), _resetArmed ? "TAP AGAIN: LOSE EVERY PLAYER" : "NEW CLUB…", 18, () =>
        {
            if (!_resetArmed)
            {
                _resetArmed = true;
                return;
            }
            Club.Reset();
            _resetArmed = false;
            _name.Text = Club.S.Name;
            _code.Text = Club.ShortName;
            _ui.Toast("New club founded");
        }, _resetArmed ? Px.Loss : new Color(1, 90 / 255f, 90 / 255f, 0.5f));
        Px.Scanlines(this, new Rect2(0, 0, W, H));
    }
}
