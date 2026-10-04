using System;
using Godot;

namespace GameNight.UI;

/// <summary>
/// The pause button (top right) and the pause menu, as the PWA has them: the match dims, a
/// small card in the middle with Resume and Restart full width and the settings two by two.
/// </summary>
public sealed partial class PauseMenu : Control
{
    public event Action Opened, Resumed, Restart, SettingsChanged;
    /// <summary>Leave the match for the menus (set by Main when the menus started it).</summary>
    public Action Leave;
    /// <summary>The weather: its name, and the next one (set by Main; switches live).</summary>
    public Func<string> WeatherName, CycleWeather;

    static readonly string[] CameraNames = { "Close", "Normal", "Far" };

    readonly Button _pause;
    readonly Control _menu;
    readonly VBoxContainer _card;
    readonly Button _camera, _graphics, _pixels, _weather, _fps, _sound, _leave;

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
        AddChild(_pause);

        // The dimmed match behind the card; taps on it do nothing.
        _menu = new ColorRect { Color = new Color(16 / 255f, 22 / 255f, 18 / 255f, 0.6f), Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _menu.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_menu);
        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        _menu.AddChild(centre);
        var card = _card = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        card.AddThemeConstantOverride("separation", 5);
        centre.AddChild(card);

        var title = new Label { Text = "PAUSED", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontOverride("font", Style.Font(true, 20 * 0.04f));
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", Style.Ink);
        card.AddChild(title);

        var resume = Solid("RESUME");
        resume.Pressed += Close;
        card.AddChild(resume);
        var restart = Ghost("RESTART MATCH");
        restart.Pressed += () => { Close(); Restart?.Invoke(); };
        card.AddChild(restart);
        _leave = Ghost("LEAVE MATCH");
        _leave.Pressed += () => { _menu.Visible = false; Leave?.Invoke(); };
        card.AddChild(_leave);

        _camera = Ghost("");
        _camera.Pressed += () => { MatchSettings.Camera = (MatchSettings.Camera + 1) % 3; Changed(); };
        _graphics = Ghost("");
        _graphics.Pressed += () => { MatchSettings.Fast = !MatchSettings.Fast; Changed(); };
        card.AddChild(Row(_camera, _graphics));

        _pixels = Ghost("");
        _pixels.Pressed += NextPixels;
        _weather = Ghost("");
        _weather.Pressed += () => { CycleWeather?.Invoke(); Labels(); };
        card.AddChild(Row(_pixels, _weather));
        _fps = Ghost("");
        // Off, on, then on with the breakdown of where the time goes.
        _fps.Pressed += () =>
        {
            if (!MatchSettings.ShowFps) MatchSettings.ShowFps = true;
            else if (!MatchSettings.Profile) MatchSettings.Profile = true;
            else MatchSettings.ShowFps = MatchSettings.Profile = false;
            Changed();
        };
        _sound = Ghost("");
        _sound.Pressed += () => { MatchSettings.Sound = !MatchSettings.Sound; Changed(); };
        card.AddChild(Row(_fps, _sound));
        Labels();
    }

    public override void _Process(double delta)
    {
        // Top right, clear of the notch.
        var hud = GetParent()?.GetNodeOrNull<Hud>("Hud");
        float r = hud?.SafeRight ?? 0, t = hud?.SafeTop ?? 0;
        _pause.Position = new Vector2(Size.X - 14 - r - _pause.Size.X, 10 + t);
        _card.CustomMinimumSize = new Vector2(Math.Min(340, Size.X - 32), 0);
    }

    public void Open()
    {
        if (_menu.Visible) return;
        _menu.Visible = true;
        _pause.Visible = false;
        _leave.Visible = Leave != null;
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
            if (_menu.Visible) Close();
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
        _camera.Text = "CAMERA: " + CameraNames[Math.Clamp(MatchSettings.Camera, 0, 2)].ToUpperInvariant();
        _graphics.Text = "GRAPHICS: " + (MatchSettings.Fast ? "FAST" : "FULL");
        int h = MatchSettings.Pixels > 0 ? MatchSettings.Pixels : CurrentHeight();
        int scale = Math.Max(1, (int)MathF.Round(Render.PixelView.ScreenPixels().Y / (float)h));
        _pixels.Text = $"PIXELS: {h} · {scale}X";
        _fps.Text = "FPS COUNTER: " + (!MatchSettings.ShowFps ? "OFF" : MatchSettings.Profile ? "DETAIL" : "ON");
        _sound.Text = "SOUND: " + (MatchSettings.Sound ? "ON" : "OFF");
        _weather.Visible = WeatherName != null;
        if (WeatherName != null) _weather.Text = "MATCH: " + WeatherName().ToUpperInvariant();
    }

    // ------------------------------------------------------------------ look

    static HBoxContainer Row(params Control[] items)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 5);
        foreach (var c in items)
        {
            c.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(c);
        }
        return row;
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
