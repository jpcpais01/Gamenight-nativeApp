using Godot;

namespace GameNight.Grounds;

/// <summary>
/// The big screens, live: the crest and club name over the score and the match clock, and
/// a flashing GOAL! in the scorers' colour after a goal. Drawn by the engine's 2D renderer
/// into a small viewport that only re-renders when something on it changes (once a match
/// minute at most), read through the gn_screen global by the stadium's screen look.
/// </summary>
public sealed partial class ScreenView : Node
{
    const int W = 512, H = 288;
    readonly SubViewport _vp;
    readonly Label _score, _clock, _goal;
    readonly ColorRect _goalBg;
    readonly uint _home, _away;
    string _last = "";

    public ScreenView(string clubName, string homeShort, string awayShort, uint home, uint away, ClubArt art)
    {
        _home = home;
        _away = away;
        _vp = new SubViewport
        {
            Size = new Vector2I(W, H),
            Disable3D = true,
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        AddChild(_vp);
        var font = ResourceLoader.Exists("res://Fonts/BarlowCondensed-ExtraBold.ttf")
            ? GD.Load<Font>("res://Fonts/BarlowCondensed-ExtraBold.ttf")
            : ThemeDB.FallbackFont;
        var root = new Control { Size = new Vector2(W, H) };
        _vp.AddChild(root);
        void Rect(float x, float y, float w, float h, Color c) => root.AddChild(new ColorRect { Position = new(x, y), Size = new(w, h), Color = c });
        Label Text(float x, float y, float w, float h, string t, Color c, int size, Control parent = null)
        {
            var l = new Label
            {
                Text = t, Position = new(x, y), Size = new(w, h),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                LabelSettings = new LabelSettings { Font = font, FontSize = size, FontColor = c, OutlineSize = 6, OutlineColor = new Color(0.02f, 0.02f, 0.06f) },
            };
            (parent ?? root).AddChild(l);
            return l;
        }
        Rect(0, 0, W, H, new Color(0.04f, 0.06f, 0.14f));
        Rect(0, 150, W, 138, new Color(0.1f, 0.09f, 0.25f));
        // Crest and name along the top.
        if (art?.Crest != null)
            root.AddChild(new TextureRect { Texture = art.Crest, Position = new(28, 14), Size = new(116, 116), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        else
        {
            root.AddChild(new Polygon2D { Polygon = new Vector2[] { new(46, 22), new(126, 22), new(126, 82), new(86, 122), new(46, 82) }, Color = Signage.Col(0xf4efe2) });
            root.AddChild(new Polygon2D { Polygon = new Vector2[] { new(52, 28), new(120, 28), new(120, 79), new(86, 113), new(52, 79) }, Color = Signage.Col(home) });
        }
        Text(150, 18, 340, 70, clubName.ToUpperInvariant(), new Color(1, 1, 1), 54);
        Text(150, 82, 340, 44, "MATCHDAY · SEASON 01", new Color(1, 0.83f, 0.28f), 30);
        // The score: the two clubs' colours either side.
        Rect(24, 170, 112, 96, Signage.Col(home));
        Rect(W - 136, 170, 112, 96, Signage.Col(away));
        Text(24, 170, 112, 96, homeShort, new Color(1, 1, 1), 50);
        Text(W - 136, 170, 112, 96, awayShort, new Color(1, 1, 1), 50);
        _score = Text(140, 160, W - 280, 90, "0 - 0", new Color(1, 1, 1), 84);
        _clock = Text(140, 242, W - 280, 40, "0'", new Color(0.62f, 0.82f, 1), 30);
        // GOAL!: over everything, shown for a while after a goal.
        _goalBg = new ColorRect { Size = new(W, H), Visible = false };
        root.AddChild(_goalBg);
        _goal = Text(0, 0, W, H, "GOAL!", new Color(1, 1, 1), 150, _goalBg);
        // The LED pixel grid on top.
        for (int x = 0; x < W; x += 4) Rect(x, 0, 1, H, new Color(0, 0, 0, 0.22f));
        for (int y = 0; y < H; y += 4) Rect(0, y, W, 1, new Color(0, 0, 0, 0.22f));
    }

    public override void _Ready() =>
        RenderingServer.GlobalShaderParameterSet("gn_screen", _vp.GetTexture());

    /// <summary>What's on the screen now; re-renders only on a change. `goalTeam` &lt; 0: no goal
    /// showing; `flash` alternates the GOAL! colours.</summary>
    public void Show(int home, int away, int minute, int goalTeam, bool flash)
    {
        string key = $"{home}-{away}-{minute}-{goalTeam}-{flash}";
        if (key == _last) return;
        _last = key;
        _score.Text = $"{home} - {away}";
        _clock.Text = $"{minute}'";
        _goalBg.Visible = goalTeam >= 0;
        if (goalTeam >= 0)
        {
            var c = Signage.Col(goalTeam == 0 ? _home : _away);
            _goalBg.Color = flash ? c : new Color(0.04f, 0.06f, 0.14f);
            _goal.LabelSettings.FontColor = flash ? new Color(1, 1, 1) : c.Lightened(0.2f);
        }
        _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }
}
