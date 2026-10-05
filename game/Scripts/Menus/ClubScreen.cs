using System;
using Godot;
using GameNight.Club;
using GameNight.Sim;

namespace GameNight.Menus;

/// <summary>
/// The club studio (the PWA's clubScreen.ts and coachEditor.ts). A rail of tabs down the left,
/// each one a live picture of what it edits (add a tab by adding to <see cref="Tabs"/>); a lit
/// showroom stage with the thing itself on a plinth; the editor on the right under a heading.
/// Tabs: the kit (name, code, colours, design, record), the crest maker, the fans (tifo
/// pictures and the stand banner) and the manager on the touchline.
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

    static readonly string[] Tabs = { "KIT", "CREST", "FANS", "MANAGER" };
    /// <summary>The heading over each tab's editor.</summary>
    static readonly string[] About = { "Name, colours and the shirt", "Build your badge piece by piece", "Tifo pictures and the stand banner", "You on the touchline" };
    static readonly Color Panel = new(16 / 255f, 14 / 255f, 44 / 255f, 0.6f);

    readonly Menus _ui;
    readonly LineEdit _name, _code, _letters, _year, _banner, _coach;
    int _tab, _slot, _part, _cslot, _page; // tab; kit colour slot; crest part; crest colour slot
    bool _resetArmed;
    /// <summary>The open tab: 0 kit, 1 crest, 2 fans, 3 manager.</summary>
    public int Tab { get => _tab; set => _tab = value; }

    /// <summary>Debug: open the crest maker at a part and page.</summary>
    public void CrestPage(int part, int page)
    {
        _tab = 1;
        _part = part;
        _page = page;
    }
    ClubState Club => _ui.Club;

    // The stage's light this frame: a glow behind the subject and the shine over it.
    Vector2 _glowAt;
    float _glowR;
    Color _glowC;
    Rect2 _shine;

    public ClubScreen(Menus ui)
    {
        _ui = ui;
        AddChild(new Fx.Light(DrawLight));
        _name = Field(24);
        _code = Field(3);
        _letters = Field(4);
        _year = Field(4);
        _banner = Field(28, 20);
        _coach = Field(20);
        VisibilityChanged += () =>
        {
            if (!Visible) return;
            Refill();
            _resetArmed = false;
        };
    }

    LineEdit Field(int max, int size = 26)
    {
        var e = new LineEdit { MaxLength = max, CaretBlink = true, ContextMenuEnabled = false, Visible = false };
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
        e.AddThemeFontSizeOverride("font_size", size);
        e.AddThemeColorOverride("font_color", Px.Ink);
        e.AddThemeColorOverride("caret_color", Px.Gold);
        e.TextSubmitted += _ => Commit();
        e.FocusExited += Commit;
        AddChild(e);
        return e;
    }

    void Refill()
    {
        var s = Club.S;
        _name.Text = s.Name;
        _code.Text = Club.ShortName;
        _letters.Text = s.Crest.Text;
        _year.Text = s.Crest.Year;
        _banner.Text = s.Banner.Text;
        _coach.Text = s.Coach.Name;
    }

    /// <summary>Keep whatever was typed into any field.</summary>
    void Commit()
    {
        var s = Club.S;
        if (_name.Text.Trim() != s.Name) Club.Rename(_name.Text);
        string code = _code.Text.Trim();
        if (code.Length == 0 || code.ToUpperInvariant() != Club.ShortName) Club.SetShort(code);
        string letters = _letters.Text.Trim().ToUpperInvariant();
        string year = new string(Array.FindAll(_year.Text.ToCharArray(), char.IsAsciiDigit));
        if (letters != s.Crest.Text || year != s.Crest.Year) EditCrest(c =>
        {
            c.Text = letters;
            c.Year = year;
        });
        string words = _banner.Text.Trim().ToUpperInvariant();
        if (words != s.Banner.Text) Club.SetBanner(new Banner { Text = words, Color = s.Banner.Color });
        string coach = _coach.Text.Trim();
        if (coach.Length == 0) coach = "The Gaffer";
        if (coach != s.Coach.Name) EditCoach(c => c.Name = coach);
        Refill();
    }

    void EditCrest(Action<Crest> f)
    {
        var c = Club.S.Crest.Clone();
        f(c);
        Club.SetCrest(c);
    }

    void EditCoach(Action<Coach> f)
    {
        var c = Club.S.Coach.Clone();
        f(c);
        Club.SetCoach(c);
    }

    void Place(LineEdit e, bool show, Rect2 r = default)
    {
        if (e.Visible != show) e.Visible = show;
        if (!show) return;
        e.Position = r.Position;
        e.Size = r.Size;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (IsVisibleInTree()) GetChild<Control>(0).QueueRedraw();
    }

    void DrawLight(CanvasItem ci)
    {
        if (_glowR <= 0) return;
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)T * 1.3f);
        Fx.Glow(ci, _glowAt, _glowR, _glowC, 0.1f + pulse * 0.04f);
        if (_shine.Size.X > 0) Fx.Shine(ci, _shine, (float)(T % 4.5) / 2.4f, new Color(1, 1, 1, 0.12f), 0.28f);
    }

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        NightBackdrop(new[] { Px.Hex(0x0a0820), Px.Hex(0x100d30), Px.Hex(0x161242), Px.Hex(0x1b1652) });
        _glowR = 0;
        _shine = default;
        Rail(H);

        float sx = 88, sw = Mathf.Round(Mathf.Clamp(W * 0.36f, 280, 380));
        var stage = new Rect2(sx, 14, sw, H - 28);
        float px = stage.End.X + 22, pw = W - 16 - px;
        Stage(stage);
        // Heading.
        string title = Tabs[Math.Min(_tab, Tabs.Length - 1)];
        Px.Text(this, Px.Big, new Vector2(px, 44), title, 36, Px.Ink, new Color(0, 0, 0, 0.55f), 3);
        float tw = Px.Width(Px.Big, title, 36);
        if (_tab < About.Length) Px.Text(this, Px.Small, new Vector2(px + tw + 14, 40), About[_tab].ToUpperInvariant(), 8, Px.InkDim);
        DrawRect(new Rect2(px, 52, pw, 2), Px.Line);
        DrawRect(new Rect2(px, 52, tw, 2), Px.Gold);

        Place(_name, _tab == 0);
        Place(_code, _tab == 0);
        Place(_letters, _tab == 1 && _part == 4);
        Place(_year, _tab == 1 && _part == 4);
        Place(_banner, _tab == 2);
        Place(_coach, _tab == 3);
        switch (_tab)
        {
            case 0: KitTab(stage, px, pw, H); break;
            case 1: CrestTab(stage, px, pw); break;
            case 2: FansTab(stage, px, pw); break;
            default: ManagerTab(stage, px, pw); break;
        }
        Px.Scanlines(this, new Rect2(0, 0, W, H));
    }

    /// <summary>The rail: back, then a tab for each part of the club, drawn as a live picture of it.</summary>
    void Rail(float H)
    {
        const float rw = 74;
        DrawRect(new Rect2(0, 0, rw, H), new Color(5 / 255f, 4 / 255f, 18 / 255f, 0.9f));
        DrawRect(new Rect2(rw, 0, 2, H), Px.Line2);
        DrawRect(new Rect2(0, 0, 3, H), Px.Hex(Club.S.Kit.Main));
        BackButton(new Vector2(17, 12), () =>
        {
            Commit();
            _ui.Go(_ui.Home);
        });
        float top = 62, th = Mathf.Min(66, (H - top - 8) / Tabs.Length);
        for (int i = 0; i < Tabs.Length; i++)
        {
            int idx = i;
            string key = "tab" + i;
            bool on = _tab == i, held = Held(key);
            var r = new Rect2(9, top + i * th, rw - 16, th - 6);
            var rr = held ? r.Translated(new Vector2(2, 2)) : r;
            Px.Frame(this, rr, on ? new Color(Px.Gold, 0.16f) : new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.85f), on ? Px.Gold : Px.Line2, held || on ? null : Px.ShadowSoft, 2, 3);
            if (on) DrawRect(new Rect2(3, rr.Position.Y + 4, 3, rr.Size.Y - 8), Px.Gold);
            TabIcon(i, new Rect2(rr.Position.X + 6, rr.Position.Y + 5, rr.Size.X - 12, rr.Size.Y - 20));
            Px.TextC(this, Px.Small, rr.GetCenter().X, rr.End.Y - 5, Tabs[i], 7, on ? Px.Gold : Px.Ink);
            Tap(key, r, () =>
            {
                Commit();
                GetViewport().GuiReleaseFocus();
                _tab = idx;
                _resetArmed = false;
            });
        }
    }

    /// <summary>A tab's picture: the shirt, the crest, a flag, the manager.</summary>
    void TabIcon(int tab, Rect2 r)
    {
        var s = Club.S;
        var c = r.GetCenter();
        float h = r.Size.Y;
        switch (tab)
        {
            case 0: Art.Jersey(this, new Rect2(c.X - h * 0.5f, r.Position.Y, h, h * 1.3f * 0.8f), s.Kit); break;
            case 1: CrestArt.Draw(this, new Rect2(c.X - h * 0.42f, r.Position.Y, h * 0.84f, h), s.Crest); break;
            case 2:
            {
                // A flag on a pole, in the club's colours.
                var pole = new Rect2(c.X - h * 0.45f, r.Position.Y, 2, h);
                DrawRect(pole, Px.InkDim);
                var f = new Rect2(pole.End.X, r.Position.Y + 1, h * 0.9f, h * 0.6f);
                float wave = Mathf.Sin((float)T * 3) * 2;
                DrawColoredPolygon(new[] { f.Position, new Vector2(f.End.X, f.Position.Y + wave), new Vector2(f.End.X, f.End.Y + wave), new Vector2(f.Position.X, f.End.Y) }, Px.Hex(s.Kit.Main));
                DrawColoredPolygon(new[] { new Vector2(f.Position.X, f.Position.Y + f.Size.Y * 0.38f), new Vector2(f.End.X, f.Position.Y + f.Size.Y * 0.38f + wave), new Vector2(f.End.X, f.Position.Y + f.Size.Y * 0.62f + wave), new Vector2(f.Position.X, f.Position.Y + f.Size.Y * 0.62f) }, Px.Hex(s.Kit.Secondary));
                break;
            }
            case 3: Art.Coach(this, new Rect2(c.X - h * 24 / 84f, r.Position.Y, h * 48 / 84f, h), s.Coach, s.Kit.Main); break;
            default: Px.TextC(this, Px.Big, c.X, c.Y + 8, Tabs[tab].Length > 0 ? Tabs[tab][..1] : "?", 26, Px.Ink); break;
        }
    }

    /// <summary>The showroom: a dark room, a spotlight from above, a floor running away from you.</summary>
    void Stage(Rect2 st)
    {
        var tint = Px.Hex(Club.S.Kit.Main).Lerp(Colors.White, 0.35f);
        Px.Frame(this, st, Colors.Transparent, Px.Line2, Px.Shadow);
        var inner = st.Grow(-3);
        Px.Bands(this, inner, new[] { Px.Hex(0x07061a), Px.Hex(0x0c0a26), Px.Hex(0x120f36), Px.Hex(0x0a0820) }, new[] { 0, 0.3f, 0.62f, 0.8f });
        float cx = inner.GetCenter().X, fy = inner.Position.Y + inner.Size.Y * 0.78f;
        DrawRect(new Rect2(inner.Position.X, fy, inner.Size.X, inner.End.Y - fy), new Color(0, 0, 0, 0.35f));
        for (int i = -6; i <= 6; i++)
            DrawLine(new Vector2(cx + i * inner.Size.X * 0.05f, fy), new Vector2(cx + i * inner.Size.X * 0.22f, inner.End.Y), new Color(tint, 0.1f * (1 - Mathf.Abs(i) / 7f)), 1);
        for (int i = 1; i < 4; i++)
        {
            float k = i / 4f;
            DrawRect(new Rect2(inner.Position.X, fy + (inner.End.Y - fy) * k * k, inner.Size.X, 1), new Color(tint, 0.08f));
        }
        Fx.Spot(this, new Vector2(cx, inner.Position.Y), new Vector2(cx, fy), inner.Size.X * 0.16f, inner.Size.X * 0.82f, tint, 0.07f);
        DrawRect(new Rect2(cx - 14, inner.Position.Y, 28, 5), Px.Hex(0x2a2550));
        DrawRect(new Rect2(cx - 10, inner.Position.Y + 5, 20, 2), Px.Hex(0xfff3c0));
    }

    /// <summary>A plaque along the bottom of the stage.</summary>
    void Plaque(Rect2 st, string text, string tag = null)
    {
        var p = new Rect2(st.Position.X + 14, st.End.Y - 48, st.Size.X - 28, 36);
        Px.Frame(this, p, new Color(0, 0, 0, 0.6f), new Color(Px.Hex(Club.S.Kit.Secondary).Lerp(Colors.White, 0.3f), 0.7f), null, 2, 0);
        float tagW = tag == null ? 0 : Px.Width(Px.Big, tag, 18) + 16;
        Px.TextC(this, Px.Big, p.GetCenter().X - tagW / 2, p.GetCenter().Y + 8, Px.Fit(Px.Big, text, 24, p.Size.X - tagW - 20), 24, Px.Ink);
        if (tag != null)
        {
            var t = new Rect2(p.End.X - tagW - 5, p.Position.Y + 5, tagW, p.Size.Y - 10);
            DrawRect(t, Px.Hex(Club.S.Kit.Main));
            Px.TextC(this, Px.Big, t.GetCenter().X, t.GetCenter().Y + 6, tag, 18, Px.Hex(Club.S.Kit.Main).Luminance > 0.6f ? Px.Hex(0x14121c) : Px.Ink);
        }
    }

    /// <summary>A grid of colour swatches; returns the height used.</summary>
    float Swatches(float x, float y, float w, int current, Action<int> pick, int perRow = 12, float max = 34)
    {
        float s = Mathf.Min(max, (w - (perRow - 1) * 6) / perRow);
        for (int i = 0; i < Palette.Length; i++)
        {
            int c = Palette[i];
            var r = new Rect2(x + i % perRow * (s + 6), y + i / perRow * (s + 6), s, s);
            bool on = current == c;
            if (on) Px.Frame(this, r.Grow(4), Colors.Transparent, Px.Gold, null, 2, 0);
            DrawRect(r, Colors.Black);
            DrawRect(r.Grow(-2), Px.Hex(c));
            DrawRect(new Rect2(r.Position + new Vector2(2, 2), new Vector2(s - 4, 3)), new Color(1, 1, 1, 0.22f));
            DrawRect(new Rect2(r.Position.X + 2, r.End.Y - 5, s - 4, 3), new Color(0, 0, 0, 0.22f));
            if (on) DrawRect(new Rect2(r.GetCenter() - new Vector2(3, 3), new Vector2(6, 6)), Px.Hex(c).Luminance > 0.6f ? Px.Dark : Px.Ink);
            Tap("sw" + i, r, () => pick(c));
        }
        return (Palette.Length + perRow - 1) / perRow * (s + 6);
    }

    /// <summary>One colour slot as a well: a big swatch of the colour and its name.</summary>
    void Well(string key, Rect2 r, string label, int col, bool on, Action tap)
    {
        bool held = Held(key);
        var rr = held ? r.Translated(new Vector2(2, 2)) : r;
        Px.Frame(this, rr, on ? new Color(Px.Gold, 0.14f) : new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.85f), on ? Px.Gold : Px.Line2, held ? null : Px.ShadowSoft, 2, 3);
        var sw = new Rect2(rr.Position + new Vector2(5, 5), new Vector2(rr.Size.Y - 10, rr.Size.Y - 10));
        DrawRect(sw, Colors.Black);
        DrawRect(sw.Grow(-2), Px.Hex(col));
        Px.Text(this, Px.Big, new Vector2(sw.End.X + 8, rr.GetCenter().Y + 7), label, 20, on ? Px.Gold : Px.Ink);
        Tap(key, r, tap);
    }

    /// <summary>A chip with a colour dot: one colour slot.</summary>
    float Slot(string key, float x, float y, string label, int col, bool on, Action tap)
    {
        float w = Chip(key, new Vector2(x, y), "   " + label, on, tap);
        var r = new Rect2(x + 8, y + 7, 12, 12);
        DrawRect(r.Grow(1), Colors.Black);
        DrawRect(r, Px.Hex(col));
        return w;
    }

    /// <summary>A section heading in the panel.</summary>
    void Head(float x, float y, string s) => Px.Text(this, Px.Small, new Vector2(x, y), s, 8, Px.Cyan);

    // ---------------------------------------------------------------- kit

    void KitTab(Rect2 stage, float px, float pw, float H)
    {
        var k = Club.S.Kit;
        // Stage: the kit on a plinth, the crest on the wall.
        float cx = stage.GetCenter().X;
        float pedY = stage.End.Y - 86, pedW = stage.Size.X * 0.56f;
        Fx.Pedestal(this, new Vector2(cx, pedY), pedW, Px.Hex(k.Main).Lerp(Colors.White, 0.3f), 0.5f + 0.5f * Mathf.Sin((float)T * 1.3f));
        float jh = Mathf.Min(pedY - stage.Position.Y - 10, stage.Size.X * 0.82f * 1.3f), jw = jh / 1.3f;
        var jr = new Rect2(cx - jw / 2, pedY + 4 - jh * 122 / 130f, jw, jh);
        Art.Jersey(this, jr, k, Club.S.Crest);
        _glowAt = jr.GetCenter();
        _glowR = jh * 0.5f;
        _glowC = Px.Hex(k.Main).Lerp(Colors.White, 0.3f);
        _shine = new Rect2(jr.Position + new Vector2(jw * 0.12f, jh * 0.05f), new Vector2(jw * 0.76f, jh * 0.62f));
        CrestArt.Draw(this, new Rect2(stage.End.X - 62, stage.Position.Y + 16, 44, 55), Club.S.Crest);
        Plaque(stage, Club.S.Name, Club.ShortName);

        // Name and code.
        float y = 64;
        Head(px, y + 8, "CLUB NAME");
        Head(px + pw - 86, y + 8, "CODE");
        Place(_name, true, new Rect2(px, y + 14, pw - 98, 42));
        Place(_code, true, new Rect2(px + pw - 86, y + 14, 86, 42));

        // Colours: three wells, the palette, and two shortcuts.
        y = 146;
        Head(px, y, "COLOURS");
        float bx = px + pw;
        bx -= Chip("ksurprise", new Vector2(bx - Px.Width(Px.Big, "SURPRISE ME", 16) - 18, y - 16), "SURPRISE ME", false, SurpriseKit, 16, 24) + 6;
        Chip("kswap", new Vector2(bx - Px.Width(Px.Big, "SWAP", 16) - 18, y - 16), "SWAP", false, () =>
            Club.SetKit(new ClubKit { Pattern = k.Pattern, Main = k.Secondary, Secondary = k.Main, Shorts = k.Shorts }), 16, 24);
        y += 12;
        string[] names = { "MAIN", "SECOND", "SHORTS" };
        int[] cols = { k.Main, k.Secondary, k.Shorts };
        float ww = (pw - 16) / 3;
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            Well("slot" + i, new Rect2(px + i * (ww + 8), y, ww, 36), names[i], cols[i], _slot == i, () => _slot = idx);
        }
        y += 44;
        y += Swatches(px, y, pw, cols[_slot], c =>
        {
            var nk = new ClubKit { Pattern = k.Pattern, Main = k.Main, Secondary = k.Secondary, Shorts = k.Shorts };
            if (_slot == 0) nk.Main = c;
            else if (_slot == 1) nk.Secondary = c;
            else nk.Shorts = c;
            Club.SetKit(nk);
        }, 12, 24) + 10;

        // Design: every pattern as a little shirt in your colours.
        Head(px, y, "DESIGN");
        y += 6;
        int n = TeamData.KitPatterns.Length;
        float tw = (pw - (n - 1) * 5) / n;
        float th = Mathf.Min(tw * 1.25f + 12, H - 56 - y);
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            var r = new Rect2(px + i * (tw + 5), y, tw, th);
            bool on = k.Pattern == i;
            Px.Frame(this, r, on ? new Color(Px.Gold, 0.18f) : new Color(8 / 255f, 6 / 255f, 26 / 255f, 0.6f), on ? Px.Gold : Px.Line, null, 2, 0);
            float sh = Mathf.Min(th - 14, (tw - 6) * 1.3f);
            Art.Jersey(this, new Rect2(r.GetCenter().X - sh / 2.6f, r.Position.Y + 3, sh / 1.3f, sh), new ClubKit { Pattern = i, Main = k.Main, Secondary = k.Secondary, Shorts = k.Shorts });
            Px.TextC(this, Px.Small, r.GetCenter().X, r.End.Y - 3, Px.Fit(Px.Small, TeamData.KitPatterns[i].ToUpperInvariant(), 6, tw - 2), 6, on ? Px.Gold : Px.InkDim);
            Tap("pat" + i, r, () => Club.SetKit(new ClubKit { Pattern = idx, Main = k.Main, Secondary = k.Secondary, Shorts = k.Shorts }));
        }

        // The record, and starting over.
        var rec = Club.S.Record;
        float W = Size.X;
        Px.Text(this, Px.Small, new Vector2(px, H - 24), $"PLAYED {rec.Played} · GOALS {rec.Gf}-{rec.Ga} · PACKS {Club.S.PacksOpened}", 8, Px.InkDim);
        float rw = _resetArmed ? 290 : 140;
        GhostButton("reset", new Rect2(W - 16 - rw, H - 46, rw, 32), _resetArmed ? "TAP AGAIN: LOSE EVERY PLAYER" : "NEW CLUB…", 16, () =>
        {
            if (!_resetArmed)
            {
                _resetArmed = true;
                return;
            }
            Club.Reset();
            _resetArmed = false;
            Refill();
            _ui.Toast("New club founded");
        }, _resetArmed ? Px.Loss : new Color(1, 90 / 255f, 90 / 255f, 0.5f));
    }

    /// <summary>A fresh kit: two colours that stand apart, shorts to go with them, any design.</summary>
    void SurpriseKit()
    {
        var rng = new Random();
        int main = Palette[rng.Next(Palette.Length)], sec;
        do sec = Palette[rng.Next(Palette.Length)];
        while (Mathf.Abs(Px.Hex(main).Luminance - Px.Hex(sec).Luminance) < 0.22f);
        int shorts = rng.Next(4) switch { 0 => 0xf3ede0, 1 => 0x14123a, 2 => main, _ => sec };
        Club.SetKit(new ClubKit { Pattern = rng.Next(TeamData.KitPatterns.Length), Main = main, Secondary = sec, Shorts = shorts });
    }

    // ---------------------------------------------------------------- crest

    void CrestTab(Rect2 stage, float px, float pw)
    {
        var c = Club.S.Crest;
        var k = Club.S.Kit;
        // Stage: the crest on a plinth, under the spotlight.
        float cx = stage.GetCenter().X;
        float pedY = stage.End.Y - 92;
        Fx.Pedestal(this, new Vector2(cx, pedY), stage.Size.X * 0.5f, Px.Hex(c.Primary).Lerp(Colors.White, 0.3f), 0.5f + 0.5f * Mathf.Sin((float)T * 1.3f));
        float ch = Mathf.Min(pedY - stage.Position.Y - 24, (stage.Size.X - 60) * 1.24f), cw = ch / 1.24f;
        var cr = new Rect2(cx - cw / 2, pedY - ch - 2 + Mathf.Sin((float)T * 1.6f) * 2, cw, ch);
        CrestArt.Draw(this, cr, c);
        _glowAt = cr.GetCenter();
        _glowR = ch * 0.6f;
        _glowC = Px.Hex(c.Primary).Lerp(Colors.White, 0.35f);
        _shine = cr.Grow(-cw * 0.08f);
        float bw = (stage.Size.X - 42) / 2, by = stage.End.Y - 58;
        GhostButton("kitcols", new Rect2(stage.Position.X + 14, by, bw, 42), "KIT COLOURS", 20, () => EditCrest(x =>
        {
            x.Primary = k.Main;
            x.Secondary = k.Secondary;
        }));
        GoldButton("surprise", new Rect2(stage.Position.X + 28 + bw, by, bw, 42), "SURPRISE ME", 22, () =>
        {
            Club.SetCrest(CrestArt.Random(c, k.Main, k.Secondary));
            Refill();
        });

        string[] parts = { "SHAPE", "FIELD", "PATTERN", "EMBLEM", "LETTERS", "TRIM" };
        float x = px;
        for (int i = 0; i < parts.Length; i++)
        {
            int idx = i;
            x += Chip("part" + i, new Vector2(x, 62), parts[i], _part == i, () =>
            {
                Commit();
                _part = idx;
                _page = 0;
            }, 17) + 6;
        }

        float y = 100;
        float pagerX = px + pw;
        if (_part < 4)
        {
            // A page of thumbnails: three rows at a time.
            string[] names = _part switch { 0 => Crest.Shapes, 1 => Crest.Divisions, 2 => Crest.Patterns, _ => Crest.Emblems };
            int cur = CrestPart(c, _part);
            int per = Mathf.Max(1, (int)((pw + 6) / 46));
            int pageSize = per * 3;
            int pages = (names.Length + pageSize - 1) / pageSize;
            _page = Math.Clamp(_page, 0, pages - 1);
            float tw = (pw - (per - 1) * 6) / per;
            float th = tw * 1.24f * 0.78f + 13;
            for (int j = 0; j < pageSize; j++)
            {
                int v = _page * pageSize + j;
                if (v >= names.Length) break;
                var r = new Rect2(px + j % per * (tw + 6), y + j / per * (th + 6), tw, th);
                bool on = v == cur;
                Px.Frame(this, r, on ? new Color(Px.Gold, 0.18f) : new Color(8 / 255f, 6 / 255f, 26 / 255f, 0.6f), on ? Px.Gold : Px.Line, null, 2, 0);
                var look = c.Clone();
                SetCrestPart(look, _part, v);
                CrestArt.Draw(this, new Rect2(r.Position.X + 4, r.Position.Y + 3, tw - 8, th - 17), look);
                Px.TextC(this, Px.Small, r.GetCenter().X, r.End.Y - 4, Px.Fit(Px.Small, names[v].ToUpperInvariant(), 7, tw - 4), 7, on ? Px.Gold : Px.InkDim);
                int part = _part;
                Tap("opt" + j, r, () => EditCrest(z => SetCrestPart(z, part, v)));
            }
            y += 3 * (th + 6) + 6;
            if (pages > 1)
            {
                // Pager, right of the colour slots.
                float nx = Chip("pg+", new Vector2(px + pw - 34, y), ">", false, () => _page = (_page + 1) % pages, 18, 26);
                Px.TextC(this, Px.Big, px + pw - 34 - 30, y + 20, $"{_page + 1}/{pages}", 18, Px.InkDim);
                Chip("pg-", new Vector2(px + pw - 34 - 30 - 46, y), "<", false, () => _page = (_page + pages - 1) % pages, 18, 26);
                pagerX = px + pw - 34 - 30 - 52;
            }
        }
        else if (_part == 4)
        {
            Head(px, y + 6, "LETTERS");
            Head(px + 120, y + 6, "FOUNDED");
            Place(_letters, true, new Rect2(px, y + 12, 108, 40));
            Place(_year, true, new Rect2(px + 120, y + 12, 98, 40));
            Head(px + 232, y + 6, "FACE");
            ChipRow("font", Crest.Fonts, c.Font, v => EditCrest(z => z.Font = v), px + 232, y + 18, pw - 232);
            y += 64;
            Head(px, y, "STYLE");
            y = ChipRow("ts", Crest.TextStyles, c.TextStyle, v => EditCrest(z => z.TextStyle = v), px, y + 6, pw) + 10;
        }
        else
        {
            Head(px, y + 6, "BORDER");
            y = ChipRow("bd", Crest.Borders, c.Border, v => EditCrest(z => z.Border = v), px, y + 12, pw) + 6;
            Head(px, y + 6, "CHAMPION STARS");
            Chip("star-", new Vector2(px + 130, y - 6), "-", false, () => EditCrest(z => z.Stars = Math.Max(0, z.Stars - 1)), 22, 30);
            Px.TextC(this, Px.Big, px + 184, y + 18, c.Stars.ToString(), 26, Px.Ink);
            Chip("star+", new Vector2(px + 204, y - 6), "+", false, () => EditCrest(z => z.Stars = Math.Min(5, z.Stars + 1)), 22, 30);
            y += 40;
        }
        y = Mathf.Max(y, 244);
        y = Mathf.Min(y, Size.Y - 120);

        // Colours: field, second, detail.
        string[] slots = { "Field", "Second", "Detail" };
        int[] cols = { c.Primary, c.Secondary, c.Accent };
        float sx = px;
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            sx += Slot("cslot" + i, sx, y, slots[i], cols[i], _cslot == i, () => _cslot = idx) + 8;
        }
        y += 36;
        Swatches(px, y, pw, cols[_cslot], col => EditCrest(z =>
        {
            if (_cslot == 0) z.Primary = col;
            else if (_cslot == 1) z.Secondary = col;
            else z.Accent = col;
        }), 12, Mathf.Min(30, (Size.Y - 14 - y) / 2 - 6));
    }

    static int CrestPart(Crest c, int part) => part switch { 0 => c.Shape, 1 => c.Division, 2 => c.Pattern, _ => c.Emblem };

    static void SetCrestPart(Crest c, int part, int v)
    {
        switch (part)
        {
            case 0: c.Shape = v; break;
            case 1: c.Division = v; break;
            case 2: c.Pattern = v; break;
            default: c.Emblem = v; break;
        }
    }

    /// <summary>Option chips that wrap onto new lines; returns the y under the last line.</summary>
    float ChipRow(string key, string[] names, int cur, Action<int> pick, float x0, float y, float w)
    {
        float x = x0;
        for (int i = 0; i < names.Length; i++)
        {
            int v = i;
            float cw = Px.Width(Px.Big, names[i], 18) + 18;
            if (x + cw > x0 + w && x > x0)
            {
                x = x0;
                y += 32;
            }
            x += Chip(key + i, new Vector2(x, y), names[i], cur == i, () => pick(v)) + 6;
        }
        return y + 32;
    }

    // ---------------------------------------------------------------- fans

    void FansTab(Rect2 stage, float px, float pw)
    {
        var s = Club.S;
        // Stage: the giant tifo as it hangs (the picture, or the club's own design).
        var g = Tifos.Of(TifoKind.Giant);
        float gh = stage.Size.Y - 40, gw = gh * g.W / g.H;
        var gr = new Rect2(stage.GetCenter().X - gw / 2, stage.Position.Y + 14, gw, gh);
        DrawRect(gr.Grow(3), Colors.Black);
        var tex = Tifos.Texture(TifoKind.Giant);
        if (tex != null) DrawTextureRect(tex, gr, false);
        else MadeTifo(gr);
        Px.TextC(this, Px.Small, gr.GetCenter().X, stage.End.Y - 9, "GIANT TIFO · BIG STADIUM", 7, Px.InkDim);

        // Panel: a picture for each tifo.
        float y = 62;
        foreach (var t in Tifos.All)
        {
            var kind = t.Kind;
            float ph = 44, pwid = ph * t.W / t.H;
            if (pwid > 96)
            {
                pwid = 96;
                ph = pwid * t.H / t.W;
            }
            var pr = new Rect2(px, y + (44 - ph) / 2, pwid, ph);
            DrawRect(pr.Grow(2), Px.Line2);
            var tt = Tifos.Texture(kind);
            if (tt != null) DrawTextureRect(tt, pr, false);
            else
            {
                DrawRect(pr, new Color(8 / 255f, 6 / 255f, 26 / 255f, 0.9f));
                Px.TextC(this, Px.Small, pr.GetCenter().X, pr.GetCenter().Y + 3, kind == TifoKind.Fan ? "NONE" : "CLUB", 7, Px.InkDim);
            }
            float ix = px + 108;
            Px.Text(this, Px.Big, new Vector2(ix, y + 18), t.Name.ToUpperInvariant(), 22, Px.Ink);
            Px.Text(this, Px.Big, new Vector2(ix, y + 36), Px.Fit(Px.Big, t.About, 16, pw - 108 - 146), 16, Px.InkDim);
            float bx = px + pw - 140;
            Chip("pick" + t.Id, new Vector2(bx, y + 8), "PICTURE…", false, () => Tifos.Pick(kind, ok =>
            {
                if (!ok) _ui.Toast("Couldn't read that picture");
            }));
            if (tt != null) Chip("clr" + t.Id, new Vector2(bx + 104, y + 8), "X", false, () => Tifos.Clear(kind));
            y += 54;
        }

        // The drop banner over the home end.
        y += 6;
        Head(px, y, "STAND BANNER");
        var (text, bg, fg) = Club.BannerColors();
        var br = new Rect2(px, y + 8, pw, 38);
        Px.Frame(this, br, Px.Hex(bg), Px.Hex(fg), Px.ShadowSoft, 3, 4);
        CrestArt.Draw(this, new Rect2(br.Position.X + 8, br.Position.Y + 4, 30, 30), s.Crest);
        CrestArt.Draw(this, new Rect2(br.End.X - 38, br.Position.Y + 4, 30, 30), s.Crest);
        string words = text.Length > 0 ? text : "ONE CLUB · ONE NIGHT";
        Px.TextC(this, Px.Big, br.GetCenter().X, br.GetCenter().Y + 8, Px.Fit(Px.Big, words, 24, pw - 96), 24, Px.Hex(fg));
        y += 56;
        Place(_banner, true, new Rect2(px, y, pw - 250, 38));
        string[] colors = { "main", "secondary", "dark" };
        string[] labels = { "MAIN", "SECOND", "DARK" };
        float cx = px + pw - 242;
        for (int i = 0; i < 3; i++)
        {
            string col = colors[i];
            cx += Chip("bcol" + i, new Vector2(cx, y + 6), labels[i], s.Banner.Color == col, () =>
                Club.SetBanner(new Banner { Text = Club.S.Banner.Text, Color = col })) + 6;
        }
    }

    /// <summary>The giant tifo without a picture: the club's colours, crest, name and banner words.</summary>
    void MadeTifo(Rect2 r)
    {
        var s = Club.S;
        DrawRect(r, Px.Hex(s.Kit.Main));
        var sec = Px.Hex(s.Kit.Secondary);
        for (int i = 0; i < 6; i++) DrawRect(new Rect2(r.Position.X + r.Size.X * (i * 2 + 1) / 12f, r.Position.Y, r.Size.X / 12f, r.Size.Y), new Color(sec, 0.16f));
        var ink = Px.Hex(s.Kit.Main).Luminance > 0.6f ? Px.Hex(0x14121c) : Px.Ink;
        Px.TextC(this, Px.Big, r.GetCenter().X, r.Position.Y + r.Size.Y * 0.16f, Px.Fit(Px.Big, s.Name.ToUpperInvariant(), 26, r.Size.X - 16), 26, ink);
        float cw = r.Size.X * 0.56f;
        CrestArt.Draw(this, new Rect2(r.GetCenter().X - cw / 2, r.Position.Y + r.Size.Y * 0.22f, cw, cw * 1.24f), s.Crest);
        string words = s.Banner.Text.Length > 0 ? s.Banner.Text : "ONE CLUB · ONE NIGHT";
        foreach (var (l, i) in WithIndex(Px.Wrap(Px.Big, words, 20, r.Size.X - 16)))
            Px.TextC(this, Px.Big, r.GetCenter().X, r.Position.Y + r.Size.Y * 0.86f + i * 20, l, 20, ink);
    }

    static System.Collections.Generic.IEnumerable<(string, int)> WithIndex(System.Collections.Generic.List<string> l)
    {
        for (int i = 0; i < l.Count; i++) yield return (l[i], i);
    }

    // ---------------------------------------------------------------- manager

    void ManagerTab(Rect2 stage, float px, float pw)
    {
        var c = Club.S.Coach;
        var k = Club.S.Kit;
        // Stage: you on the touchline, on a strip of grass.
        var grass = new Rect2(stage.Position.X + 3, stage.End.Y - 70, stage.Size.X - 6, 67);
        DrawRect(grass, Px.Hex(0x1f6b2e));
        for (int i = 0; i < 6; i++) DrawRect(new Rect2(grass.Position.X + i * grass.Size.X / 6, grass.Position.Y, grass.Size.X / 12, grass.Size.Y), Px.Hex(0x23773a));
        DrawRect(new Rect2(grass.Position.X, grass.Position.Y + 8, grass.Size.X, 3), new Color(1, 1, 1, 0.7f));
        float fh = stage.Size.Y - 60;
        Art.Coach(this, new Rect2(stage.GetCenter().X - fh * 24 / 84, stage.Position.Y + 14, fh * 48 / 84, fh), c, k.Main);
        Place(_coach, true, new Rect2(px + 70, 62, pw - 70, 40));
        Px.Text(this, Px.Big, new Vector2(px, 90), "NAME", 22, Px.Ink);

        float y = 116;
        // Skin, then hair (or bald), then the cut.
        Head(px, y + 16, "SKIN");
        Dots("skin", px + 70, y, Coach.Skins, c.Skin, v => EditCoach(z => z.Skin = v));
        y += 34;
        Head(px, y + 16, "HAIR");
        float hx = Dots("hair", px + 70, y, Coach.Hairs, c.Hair, v => EditCoach(z => z.Hair = v)) + 10;
        foreach (var (style, name) in Coach.HairStyles)
        {
            int v = style;
            hx += Chip("hs" + v, new Vector2(hx, y), name, c.HairStyle == v, () => EditCoach(z => z.HairStyle = v)) + 6;
        }
        y += 38;
        Head(px, y + 16, "HEIGHT");
        int cm = (int)Math.Round(c.Height * 100);
        Chip("h-", new Vector2(px + 70, y), "-", false, () => EditCoach(z => z.Height = Math.Max(1.65, Math.Round(z.Height * 100 - 2) / 100)));
        Px.TextC(this, Px.Big, px + 128, y + 21, cm + " CM", 22, Px.Ink);
        float bx = Chip("h+", new Vector2(px + 162, y), "+", false, () => EditCoach(z => z.Height = Math.Min(2.0, Math.Round(z.Height * 100 + 2) / 100))) + px + 162 + 24;
        Head(bx, y + 16, "BUILD");
        bx += 50;
        for (int i = 0; i < Coach.Builds.Length; i++)
        {
            int v = i;
            bx += Chip("b" + i, new Vector2(bx, y), Coach.Builds[i], c.Build == i, () => EditCoach(z => z.Build = v)) + 6;
        }
        y += 38;
        Head(px, y + 16, "OUTFIT");
        float ox = px + 70;
        for (int i = 0; i < Coach.StyleNames.Length; i++)
        {
            var v = (CoachStyle)i;
            ox += Chip("st" + i, new Vector2(ox, y), Coach.StyleNames[i], c.Style == v, () => EditCoach(z => z.Style = v)) + 6;
        }
        y += 38;
        Head(px, y + 16, "TEMPER");
        float mx = px + 70;
        for (int i = 0; i < Coach.Tempers.Length; i++)
        {
            var v = (CoachTemper)i;
            mx += Chip("tm" + i, new Vector2(mx, y), Coach.Tempers[i].name, c.Temper == v, () => EditCoach(z => z.Temper = v)) + 6;
        }
        y += 44;
        Px.Text(this, Px.Big, new Vector2(px + 70, y), Coach.Tempers[(int)c.Temper].about, 20, Px.InkDim);
    }

    /// <summary>Colour dots for the manager (-1 = none, drawn crossed out). Returns the right edge.</summary>
    float Dots(string key, float x, float y, int[] cols, int cur, Action<int> pick)
    {
        const float s = 26;
        for (int i = 0; i < cols.Length; i++)
        {
            int col = cols[i];
            var r = new Rect2(x + i * (s + 6), y, s, s);
            if (col == cur) Px.Frame(this, r.Grow(4), Colors.Transparent, Px.Gold, null, 3, 0);
            DrawRect(r, Colors.Black);
            if (col < 0)
            {
                DrawRect(r.Grow(-2), Px.Night);
                DrawLine(r.Position + new Vector2(5, s - 5), r.Position + new Vector2(s - 5, 5), Px.InkDim, 2);
            }
            else DrawRect(r.Grow(-2), Px.Hex(col));
            Tap(key + i, r, () => pick(col));
        }
        return x + cols.Length * (s + 6);
    }
}
