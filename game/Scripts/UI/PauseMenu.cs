using System;
using GameNight.Sim;
using Godot;

namespace GameNight.UI;

/// <summary>
/// The pause button (top right) and the pause menu: the match dims behind one card, what to do
/// (resume, restart, leave) on the left and the settings on the right in sections (picture,
/// match, performance), each a tile showing its value that moves on to the next with a tap.
/// </summary>
public sealed partial class PauseMenu : Control
{
    public event Action Opened, Resumed, Restart, SettingsChanged;
    /// <summary>Leave the match for the menus (set by Main when the menus started it).</summary>
    public Action Leave;
    /// <summary>The weather: its name, and the next one (set by Main; switches live).</summary>
    public Func<string> WeatherName, CycleWeather;
    /// <summary>Saves the frame-time report (FPS DETAIL on); returns where it went, for the button.</summary>
    public Func<string> SaveReport;
    /// <summary>The PWA's FOUL button beside pause: a free kick for us where the ball is (set by
    /// Main for real matches), shown while FoulShown (not during the walk-out or a replay).</summary>
    public Action Foul;
    public bool FoulShown;
    /// <summary>Holding FOUL down for a second instead: a pitch invader (to see one on demand).</summary>
    public Action Invader;
    ulong _foulDown;

    static readonly string[] CameraNames = { "Close", "Normal", "Far" };

    readonly Button _pause;
    readonly Control _menu;
    readonly HBoxContainer _card;
    readonly Label _title;
    readonly Button _restart;
    readonly Button _foul;
    readonly PanelContainer _main, _subsCard;
    readonly Button _sim;
    Button _simFull;
    readonly HBoxContainer _simWays;
    bool _autopilot;
    /// <summary>Simulate the rest of the match: true straight to full time, false the computer plays it out on screen.</summary>
    public Action<bool> Simulate;
    /// <summary>The computer has been playing for you: hand it back.</summary>
    public Action TakeBack;
    readonly VolumeBar[] _volumes;
    readonly SubsBoard _subs;
    readonly Button _subsButton;
    readonly Button _camera, _graphics, _pixels, _weather, _fps, _limit, _smooth, _leave, _report;

    public bool IsOpen => _menu.Visible;

    public PauseMenu()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = MakeTheme();

        _pause = new Button { CustomMinimumSize = new Vector2(38, 34), FocusMode = FocusModeEnum.None };
        _pause.AddThemeStyleboxOverride("normal", Flat(Style.Panel, 4));
        _pause.AddThemeStyleboxOverride("hover", Flat(Style.Panel, 4));
        _pause.AddThemeStyleboxOverride("pressed", Flat(new Color(Style.Ink, 0.3f), 4));
        _pause.Draw += () =>
        {
            var c = _pause.Size / 2;
            _pause.DrawRect(new Rect2(c.X - 6.5f, c.Y - 7, 4, 14), Style.Ink);
            _pause.DrawRect(new Rect2(c.X + 2.5f, c.Y - 7, 4, 14), Style.Ink);
        };
        _pause.Pressed += Open;
        _foul = new Button { Text = "FOUL", CustomMinimumSize = new Vector2(0, 34), FocusMode = FocusModeEnum.None, Visible = false };
        _foul.AddThemeFontOverride("font", Style.Font(true, 12 * 0.12f));
        _foul.AddThemeFontSizeOverride("font_size", 12);
        var red = new Color(200 / 255f, 57 / 255f, 59 / 255f, 0.55f);
        foreach (var st in new[] { "normal", "hover", "focus" }) _foul.AddThemeStyleboxOverride(st, Flat(red, 4, null, 0, 10));
        _foul.AddThemeStyleboxOverride("pressed", Flat(new Color(red, 0.85f), 4, null, 0, 10));
        foreach (var st in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) _foul.AddThemeColorOverride(st, Style.Ink);
        _foul.ButtonDown += () => _foulDown = Time.GetTicksMsec();
        _foul.Pressed += () =>
        {
            if (Invader != null && Time.GetTicksMsec() - _foulDown > 900) Invader();
            else Foul?.Invoke();
        };
        AddChild(_foul);
        AddChild(_pause);

        // The dimmed match behind the card; taps on it do nothing.
        _menu = new ColorRect { Color = new Color(8 / 255f, 12 / 255f, 10 / 255f, 0.7f), Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _menu.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_menu);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        _menu.AddChild(centre);

        // One card, two columns: what to do on the left, the settings by section on the right.
        var panel = _main = new PanelContainer();
        var bg = Flat(Style.PanelSolid, 10, new Color(Style.Ink, 0.1f), 1, 18, 16);
        bg.ShadowColor = new Color(0, 0, 0, 0.45f);
        bg.ShadowSize = 18;
        panel.AddThemeStyleboxOverride("panel", bg);
        centre.AddChild(panel);
        var cols = _card = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 18);
        panel.AddChild(cols);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(190, 0) };
        left.AddThemeConstantOverride("separation", 7);
        cols.AddChild(left);
        _title = new Label { Text = "PAUSED" };
        _title.AddThemeFontOverride("font", Style.Font(true, 30 * 0.04f));
        _title.AddThemeFontSizeOverride("font_size", 30);
        _title.AddThemeColorOverride("font_color", Style.Ink);
        left.AddChild(_title);
        var bar = new ColorRect { Color = Style.Accent, CustomMinimumSize = new Vector2(34, 3), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        left.AddChild(bar);
        left.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        var resume = Solid("RESUME");
        resume.Pressed += Close;
        left.AddChild(resume);
        _subsButton = Ghost("SUBSTITUTIONS");
        _subsButton.Visible = false;
        _subsButton.Pressed += () => ShowSubs(true);
        left.AddChild(_subsButton);
        // Simulate the rest: a tap opens the two ways (straight to full time, or watch the
        // computer play it); while it plays for you, the same button hands the match back.
        _sim = Ghost("SIMULATE REST");
        _sim.Visible = false;
        _sim.Pressed += () =>
        {
            if (_autopilot)
            {
                Close();
                TakeBack?.Invoke();
            }
            else _simWays.Visible = !_simWays.Visible;
        };
        left.AddChild(_sim);
        _simWays = new HBoxContainer { Visible = false };
        _simWays.AddThemeConstantOverride("separation", 6);
        foreach (var (label, fast) in new[] { ("FAST", true), ("FULL", false) })
        {
            var b = Solid(label);
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.AddThemeFontSizeOverride("font_size", 12);
            b.Pressed += () =>
            {
                _simWays.Visible = false;
                Close();
                Simulate?.Invoke(fast);
            };
            if (!fast) _simFull = b;
            _simWays.AddChild(b);
        }
        left.AddChild(_simWays);
        _restart = Ghost("RESTART MATCH");
        _restart.Pressed += () => { Close(); Restart?.Invoke(); };
        left.AddChild(_restart);
        _leave = Ghost("LEAVE MATCH");
        _leave.Pressed += () => { _menu.Visible = false; Leave?.Invoke(); };
        left.AddChild(_leave);
        left.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        _report = Ghost("SAVE PERFORMANCE REPORT");
        _report.Pressed += () => { if (SaveReport != null) _report.Text = SaveReport(); };
        left.AddChild(_report);

        cols.AddChild(new ColorRect { Color = new Color(Style.Ink, 0.08f), CustomMinimumSize = new Vector2(1, 0) });

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        right.AddThemeConstantOverride("separation", 4);
        cols.AddChild(right);

        _camera = Tile("CAMERA", () => { MatchSettings.Camera = (MatchSettings.Camera + 1) % 3; Changed(); });
        _pixels = Tile("PIXEL SIZE", NextPixels);
        _smooth = Tile("SMOOTH PIXELS", () => { MatchSettings.Smooth = !MatchSettings.Smooth; Changed(); });
        _graphics = Tile("SHADOWS", () => { MatchSettings.Fast = !MatchSettings.Fast; Changed(); });
        Section(right, "PICTURE", _camera, _pixels, _smooth, _graphics);

        VolumeBar Volume(string name, Func<int> get, Action<int> set)
        {
            var v = new VolumeBar(name, get, set);
            v.Changed += () => GameNight.Audio.GameAudio.Instance?.ApplyVolumes();
            v.Released += MatchSettings.Save;
            return v;
        }
        _volumes = new[]
        {
            Volume("MASTER", () => MatchSettings.VolMaster, x => MatchSettings.VolMaster = x),
            Volume("CROWD", () => MatchSettings.VolCrowd, x => MatchSettings.VolCrowd = x),
            Volume("MATCH", () => MatchSettings.VolFx, x => MatchSettings.VolFx = x),
            Volume("MENUS", () => MatchSettings.VolUi, x => MatchSettings.VolUi = x),
        };
        Section(right, "SOUND", _volumes);

        _weather = Tile("WEATHER", () => { CycleWeather?.Invoke(); Labels(); });

        _limit = Tile("FPS LIMIT", () =>
        {
            var caps = MatchSettings.FpsCaps;
            MatchSettings.FpsCap = caps[(Array.IndexOf(caps, MatchSettings.FpsCap) + 1) % caps.Length];
            MatchSettings.ApplyFpsCap();
            Changed();
        });
        // Off, on, then on with the breakdown of where the time goes.
        _fps = Tile("FPS COUNTER", () =>
        {
            if (!MatchSettings.ShowFps) MatchSettings.ShowFps = true;
            else if (!MatchSettings.Profile) MatchSettings.Profile = true;
            else MatchSettings.ShowFps = MatchSettings.Profile = false;
            Changed();
        });
        Section(right, "MATCH & PERFORMANCE", _weather, _limit, _fps);
        Labels();

        // The substitutions card, in the same place as the menu's.
        _subsCard = new PanelContainer { Visible = false };
        _subsCard.AddThemeStyleboxOverride("panel", bg);
        centre.AddChild(_subsCard);
        _subs = new SubsBoard();
        _subs.Done += () => ShowSubs(false);
        _subsCard.AddChild(_subs);
    }

    /// <summary>The match whose side 0 the player manages (substitutions); null hides them (training).</summary>
    public Match Match
    {
        set
        {
            _subs.Match = value;
            _subsButton.Visible = value != null;
        }
    }

    void ShowSubs(bool on)
    {
        _subs.Reset();
        _main.Visible = !on;
        _subsCard.Visible = on;
    }

    PanelContainer _ask;
    Label _askText;
    Action _askYes, _askNo;

    /// <summary>A question from the other player (online), over the match whether the menu is
    /// open or not: their ask, YES or NO. A newer ask replaces it; null clears it.</summary>
    public void Ask(string question, Action yes = null, Action no = null)
    {
        if (_ask == null)
        {
            _ask = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Stop };
            var bg = Flat(Style.PanelSolid, 8, new Color(Style.Accent, 0.6f), 1.5f, 14, 10);
            _ask.AddThemeStyleboxOverride("panel", bg);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            _askText = new Label { VerticalAlignment = VerticalAlignment.Center };
            _askText.AddThemeFontOverride("font", Style.Font(true, 1));
            _askText.AddThemeFontSizeOverride("font_size", 14);
            _askText.AddThemeColorOverride("font_color", Style.Ink);
            row.AddChild(_askText);
            var yesB = Solid("YES");
            yesB.Pressed += () => { var a = _askYes; Ask(null); a?.Invoke(); };
            var noB = Ghost("NO");
            noB.CustomMinimumSize = new Vector2(60, 0);
            noB.Pressed += () => { var a = _askNo; Ask(null); a?.Invoke(); };
            row.AddChild(yesB);
            row.AddChild(noB);
            _ask.AddChild(row);
            AddChild(_ask);
        }
        _askYes = yes;
        _askNo = no;
        _ask.Visible = question != null;
        if (question == null) return;
        _askText.Text = question;
        _ask.ResetSize();
        _ask.Position = new Vector2((Size.X - _ask.GetCombinedMinimumSize().X) / 2, 58);
    }

    /// <summary>Which ways to simulate the rest are on offer (none hides the button): FULL only
    /// where someone is playing (a watched or managed match is the computer's already).</summary>
    public void SimulateWays(bool any, bool full)
    {
        _sim.Visible = any;
        _simFull.Visible = full;
    }

    /// <summary>The computer is playing for you (FULL): the button takes it back.</summary>
    public bool Autopilot
    {
        set
        {
            _autopilot = value;
            _sim.Text = value ? "TAKE BACK CONTROL" : "SIMULATE REST";
            _simWays.Visible = false;
        }
    }

    /// <summary>Online: the match doesn't stop for the menu and can't be restarted.</summary>
    public bool Online
    {
        set
        {
            _title.Text = value ? "ONLINE" : "PAUSED";
            _restart.Visible = !value;
        }
    }

    /// <summary>Training: the left column's actions are for the drill.</summary>
    public bool Training
    {
        set
        {
            _title.Text = value ? "TRAINING" : "PAUSED";
            _restart.Text = value ? "RESTART DRILL" : "RESTART MATCH";
            _leave.Text = value ? "END TRAINING" : "LEAVE MATCH";
        }
    }

    public override void _Process(double delta)
    {
        // Top right, clear of the notch.
        var hud = GetParent()?.GetNodeOrNull<Hud>("Hud");
        float r = hud?.SafeRight ?? 0, t = hud?.SafeTop ?? 0;
        _pause.Position = new Vector2(Size.X - 14 - r - _pause.Size.X, 10 + t);
        _foul.Visible = Foul != null && FoulShown && !_menu.Visible;
        if (_foul.Visible) _foul.Position = new Vector2(Size.X - 60 - r - _foul.Size.X, 10 + t);
    }

    public void Open()
    {
        if (_menu.Visible) return;
        _menu.Visible = true;
        _pause.Visible = false;
        _leave.Visible = Leave != null;
        _simWays.Visible = false;
        ShowSubs(false);
        Labels();
        Opened?.Invoke();
    }

    public void Close()
    {
        if (!_menu.Visible) return;
        _menu.Visible = false;
        _pause.Visible = true;
        Resumed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // Back (Android) or Escape toggles the menu.
        if (e is InputEventKey { Pressed: true, Echo: false } k && (k.Keycode == Key.Escape || k.Keycode == Key.Back))
        {
            if (_subsCard.Visible) ShowSubs(false);
            else if (_menu.Visible) Close();
            else Open();
            GetViewport().SetInputAsHandled();
        }
    }

    void NextPixels()
    {
        // One step chunkier each tap (a whole device pixel more per art pixel), then back to the finest.
        var levels = Render.PixelView.Levels(120, 600);
        if (levels.Length == 0) return;
        int cur = MatchSettings.Pixels > 0 ? MatchSettings.Pixels : CurrentHeight();
        int next = levels[0];
        foreach (int h in levels)
            if (h < cur)
            {
                next = h;
                break;
            }
        MatchSettings.Pixels = next;
        Changed();
    }

    /// <summary>The art height in use now (set by Main).</summary>
    public Func<int> CurrentHeight = () => 270;

    void Changed()
    {
        MatchSettings.Save();
        Labels();
        SettingsChanged?.Invoke();
    }

    void Labels()
    {
        Value(_camera, CameraNames[Math.Clamp(MatchSettings.Camera, 0, 2)]);
        Value(_graphics, MatchSettings.Fast ? "Off" : "On");
        int h = MatchSettings.Pixels > 0 ? MatchSettings.Pixels : CurrentHeight();
        int scale = Math.Max(1, (int)MathF.Round(Render.PixelView.ScreenPixels().Y / (float)h));
        Value(_pixels, $"{h} · {scale}x");
        Value(_smooth, MatchSettings.Smooth ? "On" : "Off");
        Value(_fps, !MatchSettings.ShowFps ? "Off" : MatchSettings.Profile ? "Detail" : "On");
        Value(_limit, MatchSettings.FpsCap.ToString());
        foreach (var v in _volumes) v.QueueRedraw();
        _weather.Disabled = WeatherName == null;
        Value(_weather, WeatherName != null ? WeatherName() : "—");
        _report.Visible = MatchSettings.ShowFps && MatchSettings.Profile && SaveReport != null;
        _report.Text = "SAVE PERFORMANCE REPORT";
    }

    // ------------------------------------------------------------------ look

    /// <summary>A section: a small heading, then its settings two to a row.</summary>
    static void Section(VBoxContainer into, string name, params Control[] tiles)
    {
        if (into.GetChildCount() > 0) into.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        var head = new Label { Text = name };
        head.AddThemeFontOverride("font", Style.Font(true, 10 * 0.2f));
        head.AddThemeFontSizeOverride("font_size", 10);
        head.AddThemeColorOverride("font_color", new Color(Style.Accent, 0.85f));
        into.AddChild(head);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var t in tiles)
        {
            t.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(t);
        }
        into.AddChild(grid);
    }

    readonly System.Collections.Generic.Dictionary<Button, (string name, string value)> _tiles = new();

    /// <summary>A setting: its name small on the left, what it's set to on the right; a tap
    /// moves it on to the next choice.</summary>
    Button Tile(string name, Action next)
    {
        var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) };
        var fill = new Color(Style.Ink, 0.06f);
        foreach (var st in new[] { "normal", "hover", "focus" }) b.AddThemeStyleboxOverride(st, Flat(fill, 6));
        b.AddThemeStyleboxOverride("pressed", Flat(new Color(Style.Ink, 0.16f), 6));
        b.AddThemeStyleboxOverride("disabled", Flat(new Color(Style.Ink, 0.03f), 6));
        _tiles[b] = (name, "");
        b.Pressed += next;
        b.Draw += () =>
        {
            var (n, v) = _tiles[b];
            float k = b.Disabled ? 0.4f : 1;
            var r = new Rect2(Vector2.Zero, b.Size);
            Style.Text(b, Style.Font(false, 0.6f), n, new Rect2(12, 0, r.Size.X, r.Size.Y), 12, new Color(Style.InkDim, Style.InkDim.A * k), false);
            var vf = Style.Font(true, 0.5f);
            float w = Style.Width(vf, v, 15);
            Style.Text(b, vf, v, new Rect2(r.Size.X - 22 - w, 0, w, r.Size.Y), 15, new Color(Style.Ink, k), false);
            // The chevron: tap for the next.
            float cx = r.Size.X - 12, cy = r.Size.Y / 2;
            b.DrawPolyline(new[] { new Vector2(cx - 3, cy - 4), new Vector2(cx, cy), new Vector2(cx - 3, cy + 4) }, new Color(Style.Accent, 0.8f * k), 1.5f, true);
        };
        return b;
    }

    void Value(Button tile, string value)
    {
        _tiles[tile] = (_tiles[tile].name, value.ToUpperInvariant());
        tile.QueueRedraw();
    }

    static StyleBoxFlat Flat(Color bg, int radius, Color? border = null, float bw = 0, float padX = 0, float padY = 0)
    {
        var s = new StyleBoxFlat { BgColor = bg, AntiAliasing = true };
        s.SetCornerRadiusAll(radius);
        if (border is Color b)
        {
            s.BorderColor = b;
            s.SetBorderWidthAll((int)MathF.Ceiling(bw));
        }
        s.ContentMarginLeft = s.ContentMarginRight = padX;
        s.ContentMarginTop = s.ContentMarginBottom = padY;
        return s;
    }

    /// <summary>The main action: light, dark text.</summary>
    static Button Solid(string text)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None };
        b.AddThemeFontOverride("font", Style.Font(true, 14 * 0.12f));
        b.AddThemeFontSizeOverride("font_size", 14);
        foreach (var st in new[] { "normal", "hover", "focus" })
            b.AddThemeStyleboxOverride(st, Flat(Style.Ink, 4, null, 0, 12, 7));
        b.AddThemeStyleboxOverride("pressed", Flat(Style.Ink.Darkened(0.12f), 4, null, 0, 12, 7));
        foreach (var st in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            b.AddThemeColorOverride(st, Style.PanelSolid);
        return b;
    }

    /// <summary>A setting: outlined, small.</summary>
    static Button Ghost(string text)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, ClipText = true };
        b.AddThemeFontOverride("font", Style.Font(true, 11 * 0.08f));
        b.AddThemeFontSizeOverride("font_size", 11);
        var line = new Color(Style.Ink, 0.4f);
        foreach (var st in new[] { "normal", "hover", "focus" })
            b.AddThemeStyleboxOverride(st, Flat(new Color(0, 0, 0, 0), 4, line, 1.5f, 6, 8));
        b.AddThemeStyleboxOverride("pressed", Flat(new Color(Style.Ink, 0.15f), 4, line, 1.5f, 6, 8));
        foreach (var st in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            b.AddThemeColorOverride(st, Style.Ink);
        return b;
    }

    static Theme MakeTheme() => new() { DefaultFont = Style.Font(true), DefaultFontSize = 14 };
}
