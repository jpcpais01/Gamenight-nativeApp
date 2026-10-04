using System;
using Godot;
using GameNight.Render;
using GameNight.Sim;

namespace GameNight.UI;

/// <summary>A layer of the HUD that draws itself with a callback (redrawn only when asked).</summary>
public sealed partial class Painter : Control
{
    readonly Action<Painter> _paint;

    public Painter(Action<Painter> paint)
    {
        _paint = paint;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw() => _paint(this);
}

/// <summary>
/// The match HUD, ported from the PWA (hud.ts, minimap.ts, the charge / stamina / aim overlays
/// in main.ts and their CSS), drawn by the engine in the same frame as the match:
/// - the scoreboard, top left, with the fourth official's added-time board;
/// - captions for the big moments and the referee's calls;
/// - the score card that rolls the scorer's number over once the celebration is done;
/// - the active player's card (number, name, stamina) bottom left, in his line's colour;
/// - the minimap, bottom centre;
/// - the pass / shot charge bar and stamina over the active player, and the dead-ball reticle.
/// Each piece is its own canvas item, redrawn only when what it shows changes.
/// </summary>
public sealed partial class Hud : Control
{
    /// <summary>For projecting the world onto the screen.</summary>
    public PixelView View;
    public bool ShowFps;
    public bool Paused;

    MatchInfo _info;
    Match _match;
    readonly Painter _board, _caption, _player, _overlay, _fps;
    readonly Painter _card, _cellHome, _cellAway;
    readonly Minimap _map;
    float _safeL, _safeR, _safeT, _safeB;
    Vector2I _safeFor;
    double _now;

    // Scoreboard.
    int _hs = -1, _as = -1;
    string _clock = "", _added = "";
    /// <summary>Score shown until the goal's score card reveals the new one.</summary>
    int[] _held;
    float _revealAt = -1;
    int _revealTeam;
    string _revealLine = "";

    // Caption.
    string _capTitle = "", _capSub = "";
    int _capKind; // 0 big moment, 1 small (a call), 2 yellow (a booking)
    double _capAt = -99, _capDur;

    // Score card.
    double _cardAt = -99;
    int _cardTeam;
    readonly int[] _cardOld = new int[2], _cardNew = new int[2];
    string _cardLine = "";

    // Player card.
    int _pcFor = -2, _pcStamina = -1;
    bool _pcShow;
    string _pcName = "";
    Role _pcRole;

    // Overlays over the pitch.
    Vector2? _head, _aim;
    bool _aimOff;
    float _stamina = -1;
    int _chargeBtn = -1;
    float _chargeP, _chargeA;
    bool _chargeDead;

    // Frame rate.
    string _fpsText = "";
    int _frames;
    double _fpsT;

    Phase _lastPhase = Phase.Kickoff;

    static readonly GradientTexture1D RampPass = Style.Ramp((0, 0xe5483b), (0.35f, 0xf7a23b), (0.6f, 0xf2d43a), (1, 0x3ddc6a));
    static readonly GradientTexture1D RampShot = Style.Ramp((0, 0xe5483b), (0.3f, 0xf7a23b), (0.52f, 0xf2d43a), (0.8f, 0x3ddc6a), (0.87f, 0x3ddc6a), (1, 0xe5483b));
    static readonly GradientTexture1D RampDead = Style.Ramp((0, 0xe5483b), (0.38f, 0xf7a23b), (0.74f, 0x3ddc6a), (0.82f, 0x3ddc6a), (0.88f, 0xffd447), (1, 0xe5483b));

    public Hud()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Painter Layer(Action<Painter> paint)
        {
            var p = new Painter(paint);
            p.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(p);
            return p;
        }
        _overlay = Layer(DrawOverlay);
        _map = new Minimap();
        AddChild(_map);
        _board = Layer(DrawBoard);
        _player = Layer(DrawPlayerCard);
        _caption = Layer(DrawCaption);
        _card = new Painter(DrawCard) { Visible = false };
        AddChild(_card);
        // The score card's numbers roll inside their cells.
        _cellHome = new Painter(p => DrawCell(p, 0)) { ClipContents = true };
        _cellAway = new Painter(p => DrawCell(p, 1)) { ClipContents = true };
        _card.AddChild(_cellHome);
        _card.AddChild(_cellAway);
        _fps = Layer(DrawFps);
    }

    /// <summary>A new match (a restart): its teams, and a clean slate.</summary>
    public void SetMatch(Match m)
    {
        _match = m;
        _info = new MatchInfo(m);
        _map.Info = _info;
        _map.HumanTeam = m.HumanTeam;
        _hs = _as = -1;
        _clock = "";
        _held = null;
        _revealAt = -1;
        _capAt = _cardAt = -99;
        _card.Visible = false;
        _pcFor = -2;
        _lastPhase = Phase.Kickoff;
        QueueAll();
    }

    void QueueAll()
    {
        foreach (var c in GetChildren())
            if (c is CanvasItem ci) ci.QueueRedraw();
    }

    /// <summary>The screen's safe area (notch, rounded corners) in HUD units.</summary>
    void Insets()
    {
        var win = PixelView.ScreenPixels();
        if (win == _safeFor || win.Y == 0) return;
        _safeFor = win;
        var safe = DisplayServer.GetDisplaySafeArea();
        float k = Size.Y / win.Y;
        _safeL = Math.Max(0, safe.Position.X) * k;
        _safeT = Math.Max(0, safe.Position.Y) * k;
        _safeR = Math.Max(0, win.X - safe.End.X) * k;
        _safeB = Math.Max(0, win.Y - safe.End.Y) * k;
        if (_safeR > Size.X * 0.2f || _safeL > Size.X * 0.2f) _safeL = _safeR = 0;
        _map.SafeBottom = _safeB;
        QueueAll();
    }

    public float SafeRight => _safeR;
    public float SafeTop => _safeT;

    public void Tick(MatchSnapshot a, MatchSnapshot b, float alpha, InputState input, bool attack, double delta)
    {
        _now += delta;
        Insets();
        Events(b);
        Scoreboard(b);
        PlayerCard(b);
        _map.Visible = !Paused && b.Phase != Phase.Fulltime;
        if (_map.Visible) _map.Update(a, b, alpha);
        Overlays(a, b, alpha, input, attack, (float)delta);
        Animate();
        Fps(delta);
    }

    // ------------------------------------------------------------------ events

    void Events(MatchSnapshot b)
    {
        if (_info == null) return;
        if (b.Goal >= 0)
        {
            int team = b.Goal;
            string who = b.Scorer >= 0 ? _info.Surname[b.Scorer] + " · " : "";
            Caption("GOAL", who + _info.Name[team], 3.2, 0);
            // The new score is revealed as the camera comes back from the crowd.
            _held = new[] { b.Score[0] - (team == 0 ? 1 : 0), b.Score[1] - (team == 1 ? 1 : 0) };
            _revealAt = (float)GoalSeq.Back + 0.6f;
            _revealTeam = team;
            _revealLine = who + _info.Name[team] + " · " + b.ClockLabel;
        }
        // The referee's calls. (The Foul object is made before the step that reports it, and
        // the snapshot hand-over is locked, so reading it here sees it whole.)
        var f = _match?.LastFoul;
        if (b.Foul == 2) Caption("ADVANTAGE", "Play on", 1.8, 1);
        else if (b.Foul == 1 && f != null)
        {
            if (f.Penalty) Caption("PENALTY", _info.Name[f.Victim.Team], 3, 0);
            else if (f.Yellow) Caption("YELLOW CARD", $"{MatchInfo.Who(f.Offender)} · {_info.Name[f.Offender.Team]}", 2.6, 2);
            else Caption("FOUL", "Free kick · " + _info.Name[f.Victim.Team], 2, 1);
        }
        var off = _match?.LastOffside;
        if (b.Offside != 0 && off != null) Caption("OFFSIDE", "Free kick · " + _info.Name[off.Team], 2, 1);
        // Booked while advantage was played: show the card now.
        if (b.Card != 0 && b.Foul == 2 && f != null)
            Caption("YELLOW CARD", $"{MatchInfo.Who(f.Offender)} · {_info.Name[f.Offender.Team]} · advantage", 2.6, 2);

        if (b.Phase != _lastPhase)
        {
            if (b.Phase == Phase.Halftime) Caption("HALF TIME", $"{b.Score[0]} – {b.Score[1]}", 3, 0);
            if (b.Phase == Phase.Fulltime) Caption("FULL TIME", $"{b.Score[0]} – {b.Score[1]}", 30, 0);
            _lastPhase = b.Phase;
        }

        // Revealed on the goal sequence's own clock (so pausing doesn't spoil it).
        if (_revealAt >= 0 && (b.Phase != Phase.Goal || b.PhaseT >= _revealAt))
        {
            _cardOld[0] = _held?[0] ?? b.Score[0];
            _cardOld[1] = _held?[1] ?? b.Score[1];
            _cardNew[0] = b.Score[0];
            _cardNew[1] = b.Score[1];
            _cardTeam = _revealTeam;
            _cardLine = _revealLine;
            _cardAt = _now;
            _revealAt = -1;
            _held = null;
            LayoutCard();
        }
    }

    void Caption(string title, string sub, double seconds, int kind)
    {
        _capTitle = title;
        _capSub = sub;
        _capKind = kind;
        _capAt = _now;
        _capDur = seconds;
    }

    void Animate()
    {
        // Captions and the score card animate; everything else redraws on change.
        if (_now - _capAt < Math.Min(_capDur, 3.2) + 0.1) _caption.QueueRedraw();
        double ct = _now - _cardAt;
        _card.Visible = ct >= 0 && ct < 3.4;
        if (_card.Visible)
        {
            float t = (float)ct;
            // In with a lift (9% of 3.4 s), held, out with a little rise (the last 12%).
            float op, y, s;
            if (t < 0.306f) { float e = Style.EaseOut(t / 0.306f); op = e; y = 30 * (1 - e); s = 0.96f + 0.04f * e; }
            else if (t < 2.992f) { op = 1; y = 0; s = 1; }
            else { float e = Style.Smooth((t - 2.992f) / 0.408f); op = 1 - e; y = -12 * e; s = 1 - 0.02f * e; }
            _card.Modulate = new Color(1, 1, 1, op);
            _card.PivotOffset = _card.Size / 2;
            _card.Scale = new Vector2(s, s);
            _card.Position = (Size - _card.Size) / 2 + new Vector2(0, y);
            _cellHome.QueueRedraw();
            _cellAway.QueueRedraw();
        }
    }

    // ------------------------------------------------------------------ scoreboard

    void Scoreboard(MatchSnapshot b)
    {
        int hs = _held?[0] ?? b.Score[0], aws = _held?[1] ?? b.Score[1];
        if (hs == _hs && aws == _as && b.ClockLabel == _clock) return;
        _hs = hs;
        _as = aws;
        if (b.ClockLabel != _clock)
        {
            _clock = b.ClockLabel;
            // The fourth official's board, up once the half reaches 45' or 90'.
            _added = _clock.Contains('+') && _match != null ? "+" + (int)Math.Round(_match.AddedTime) : "";
        }
        _board.QueueRedraw();
    }

    void DrawBoard(Painter c)
    {
        if (_info == null) return;
        const float h = 30;
        const int size = 18;
        var bold = Style.Font(true, size * 0.04f);
        var semi = Style.Font(false, size * 0.04f);
        float x = 14 + _safeL, y = 10 + _safeT;
        string hName = _info.Short[0], aName = _info.Short[1];
        float hw = 10 + 5 + 7 + Style.Width(bold, hName, size) + 10;
        float aw = 10 + Style.Width(bold, aName, size) + 7 + 5 + 10;
        string hs = _hs.ToString(), aws = _as.ToString();
        float dash = Style.Width(bold, "–", size);
        float sw = 10 + Style.Width(bold, hs, size) + 6 + dash + 6 + Style.Width(bold, aws, size) + 10;
        float cw = Math.Max(46, 20 + Style.Width(semi, _clock, size));

        // A soft shadow under the whole board (the PWA's drop-shadow filter).
        Style.Corners(c, new Rect2(x, y, hw + sw + aw, h), new Color(0, 0, 0, 0.001f), 4, 0, 0, 4, 6);
        // Home: kit swatch, short name.
        Style.Corners(c, new Rect2(x, y, hw, h), Style.PanelSolid, 4, 0, 0, 4);
        Style.Box(c, new Rect2(x + 10, y + 6, 5, 18), _info.Shirt[0], 1);
        Style.Text(c, bold, hName, new Rect2(x + 22, y, 0, h), size, Style.Ink, false);
        x += hw;
        // Score: dark on light.
        Style.Box(c, new Rect2(x, y, sw, h), Style.Ink);
        float tx = x + 10;
        Style.Text(c, bold, hs, new Rect2(tx, y, 0, h), size, Style.PanelSolid, false);
        tx += Style.Width(bold, hs, size) + 6;
        Style.Text(c, bold, "–", new Rect2(tx, y, 0, h), size, new Color(Style.PanelSolid, 0.5f), false);
        tx += dash + 6;
        Style.Text(c, bold, aws, new Rect2(tx, y, 0, h), size, Style.PanelSolid, false);
        x += sw;
        // Away: short name, kit swatch.
        Style.Box(c, new Rect2(x, y, aw, h), Style.PanelSolid);
        Style.Text(c, bold, aName, new Rect2(x + 10, y, 0, h), size, Style.Ink, false);
        Style.Box(c, new Rect2(x + aw - 15, y + 6, 5, 18), _info.Shirt[1], 1);
        x += aw + 4;
        // Clock.
        Style.Box(c, new Rect2(x, y, cw, h), new Color(20 / 255f, 26 / 255f, 22 / 255f, 0.6f), 4);
        Style.Text(c, semi, _clock, new Rect2(x, y, cw, h), size, Style.Ink);
        x += cw + 3;
        if (_added.Length > 0)
        {
            float bw = 14 + Style.Width(bold, _added, size);
            Style.Box(c, new Rect2(x, y, bw, h), Style.Hex(0x1f8f3a), 4);
            Style.Text(c, bold, _added, new Rect2(x, y, bw, h), size, Colors.White);
        }
    }

    // ------------------------------------------------------------------ captions

    void DrawCaption(Painter c)
    {
        float t = (float)(_now - _capAt);
        float life = (float)Math.Min(_capDur, 3.2);
        if (t < 0 || t > life || _capTitle.Length == 0) return;
        // In: from a little big, quickly. Out: a short fade at the end of its time.
        float inE = Style.EaseOut(t / 0.32f);
        float outE = Style.Smooth((t - (life - 0.35f)) / 0.35f);
        float op = inE * (1 - outE);
        float scale = 1.12f - 0.12f * inE - 0.02f * outE;

        float W = Size.X, H = Size.Y;
        bool small = _capKind != 0;
        int ts = (int)Math.Clamp(W * (small ? 0.065f : 0.11f), small ? 34 : 54, small ? 64 : 110);
        int ss = (int)Math.Clamp(W * 0.024f, 15, 22);
        var tf = Style.Font(true, ts * 0.06f);
        var sf = Style.Font(false, ss * 0.2f);
        float top = H * (small ? 0.22f : 0.30f) + _safeT;
        float lineH = ts * 0.9f;
        var centre = new Vector2(W / 2, top + lineH / 2);
        c.DrawSetTransform(centre, 0, new Vector2(scale, scale));

        float tw = Style.Width(tf, _capTitle, ts);
        float icon = _capKind == 2 ? ts * 0.64f : 0;
        float x0 = -(tw + icon) / 2;
        float baseY = (lineH + tf.GetAscent(ts) - tf.GetDescent(ts)) / 2 - lineH / 2;
        if (_capKind == 2)
        {
            // The yellow card, tilted, before the title.
            float cw = ts * 0.42f, ch = ts * 0.6f;
            var cc = new Vector2(x0 + cw / 2, baseY - ch / 2 + ts * 0.04f);
            c.DrawSetTransformMatrix(new Transform2D(0, new Vector2(scale, scale), 0, centre) * new Transform2D(Mathf.DegToRad(-8), cc));
            Style.Box(c, new Rect2(-cw / 2, -ch / 2 + 4, cw, ch), new Color(0, 0, 0, 0.3f * op), 2);
            Style.Box(c, new Rect2(-cw / 2, -ch / 2, cw, ch), new Color(Style.Accent, op), 2);
            c.DrawSetTransform(centre, 0, new Vector2(scale, scale));
        }
        float tx = x0 + icon;
        c.DrawString(tf, new Vector2(tx, baseY + 3), _capTitle, HorizontalAlignment.Left, -1, ts, new Color(0, 0, 0, 0.25f * op));
        c.DrawString(tf, new Vector2(tx, baseY + 8), _capTitle, HorizontalAlignment.Left, -1, ts, new Color(0, 0, 0, 0.1f * op));
        c.DrawString(tf, new Vector2(tx, baseY), _capTitle, HorizontalAlignment.Left, -1, ts, new Color(Style.Ink, op));
        if (_capSub.Length > 0)
        {
            string sub = _capSub.ToUpperInvariant();
            float sy = lineH / 2 + 6 + sf.GetAscent(ss);
            float sw = Style.Width(sf, sub, ss);
            c.DrawString(sf, new Vector2(-sw / 2, sy + 1), sub, HorizontalAlignment.Left, -1, ss, new Color(0, 0, 0, 0.3f * op));
            c.DrawString(sf, new Vector2(-sw / 2, sy), sub, HorizontalAlignment.Left, -1, ss, new Color(Style.InkDim, Style.InkDim.A * op));
        }
        c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    // ------------------------------------------------------------------ score card

    // Its layout, worked out when it's shown.
    float _rowH, _numW, _cardW, _lineW, _lineH;
    int _teamFont, _numFont, _teamPad;
    float _homeW, _awayW, _dashW;

    void LayoutCard()
    {
        bool compact = Size.Y <= 420;
        _rowH = compact ? 50 : 64;
        _teamFont = compact ? 20 : 26;
        _teamPad = compact ? 14 : 18;
        _numW = compact ? 42 : 52;
        _numFont = compact ? 36 : 46;
        var tf = Style.Font(true, _teamFont * 0.1f);
        _homeW = 8 + 4 + 12 + Style.Width(tf, _info.Short[0], _teamFont) + _teamPad;
        _awayW = _teamPad + Style.Width(tf, _info.Short[1], _teamFont) + 12 + 4 + 8;
        _dashW = Style.Width(Style.Font(true), "–", 30) + 4;
        _cardW = _homeW + _numW + _dashW + _numW + _awayW;
        var lf = Style.Font(false, 14 * 0.16f);
        _lineW = Style.Width(lf, _cardLine.ToUpperInvariant(), 14) + 24;
        _lineH = 14 + 10;
        _card.Size = new Vector2(Math.Max(_cardW, _lineW), _rowH + 8 + _lineH);
        float x0 = (_card.Size.X - _cardW) / 2;
        _cellHome.Position = new Vector2(x0 + _homeW, 0);
        _cellAway.Position = new Vector2(x0 + _homeW + _numW + _dashW, 0);
        _cellHome.Size = _cellAway.Size = new Vector2(_numW, _rowH);
        _card.QueueRedraw();
    }

    void DrawCard(Painter c)
    {
        float x0 = (c.Size.X - _cardW) / 2;
        var row = new Rect2(x0, 0, _cardW, _rowH);
        Style.Box(c, row, new Color(14 / 255f, 18 / 255f, 16 / 255f, 0.86f), 6, 14);
        var tf = Style.Font(true, _teamFont * 0.1f);
        // Home: the kit stripe at the outer edge, then the short name.
        Style.Corners(c, new Rect2(x0, 0, 8, _rowH), _info.Shirt[0], 6, 0, 0, 6);
        Style.Text(c, tf, _info.Short[0], new Rect2(x0 + 24, 0, 0, _rowH), _teamFont, Style.Ink, false);
        float dx = x0 + _homeW + _numW;
        Style.Text(c, Style.Font(true), "–", new Rect2(dx, 0, _dashW, _rowH), 30, new Color(Style.Ink, 0.45f));
        float ax = dx + _dashW + _numW;
        Style.Text(c, tf, _info.Short[1], new Rect2(ax + _teamPad, 0, 0, _rowH), _teamFont, Style.Ink, false);
        Style.Corners(c, new Rect2(ax + _awayW - 8, 0, 8, _rowH), _info.Shirt[1], 0, 6, 6, 0);
        // The line underneath: scorer, team, minute.
        var lf = Style.Font(false, 14 * 0.16f);
        var lr = new Rect2((c.Size.X - _lineW) / 2, _rowH + 8, _lineW, _lineH);
        Style.Box(c, lr, Style.Panel, 3);
        Style.Text(c, lf, _cardLine.ToUpperInvariant(), lr, 14, Style.InkDim);
    }

    void DrawCell(Painter c, int side)
    {
        float t = (float)(_now - _cardAt);
        var r = new Rect2(Vector2.Zero, c.Size);
        var nf = Style.Font(true);
        bool roll = side == _cardTeam;
        // The scorer's side lights up and rolls over to the new number.
        Color bg = Style.Ink, fg = Style.PanelSolid;
        if (roll)
        {
            if (t >= 1.0f) bg = Style.Accent.Lerp(Style.Ink, Style.EaseOut((t - 1.0f) / 1.2f));
            if (t >= 1.05f) fg = Style.Accent.Lerp(Style.PanelSolid, Style.EaseOut((t - 1.05f) / 0.9f));
        }
        c.DrawRect(r, bg);
        if (!roll)
        {
            Style.Text(c, nf, _cardNew[side].ToString(), r, _numFont, fg);
            return;
        }
        float e = Style.Smooth((t - 0.55f) / 0.55f);
        Style.Text(c, nf, _cardOld[side].ToString(), new Rect2(0, -_rowH * e, _numW, _rowH), _numFont, fg);
        Style.Text(c, nf, _cardNew[side].ToString(), new Rect2(0, _rowH * (1 - e), _numW, _rowH), _numFont, fg);
    }

    // ------------------------------------------------------------------ player card

    void PlayerCard(MatchSnapshot b)
    {
        bool show = b.Phase != Phase.Fulltime && b.Controlled >= 0;
        int c = b.Controlled;
        int st = c >= 0 ? (int)MathF.Round(b.Stamina[c] * 100) : -1;
        if (show == _pcShow && c == _pcFor && st == _pcStamina) return;
        _pcShow = show;
        _pcStamina = st;
        if (c != _pcFor && c >= 0 && _info != null)
        {
            _pcRole = b.Role[c];
            int num = b.Number[c] > 0 ? b.Number[c] : b.Index[c] + 1;
            string name = _info.Surname[c] ?? "";
            if (name.StartsWith('#')) name = "Player";
            _pcName = $"{num}  {name}".ToUpperInvariant();
        }
        _pcFor = c;
        _player.QueueRedraw();
    }

    void DrawPlayerCard(Painter c)
    {
        if (!_pcShow) return;
        const int size = 15;
        var f = Style.Font(true, size * 0.06f);
        float w = Math.Max(118, Style.Width(f, _pcName, size) + 18);
        float h = 5 + size * 1.1f + 5 + 3 + 6;
        var r = new Rect2(14 + _safeL, Size.Y - 12 - _safeB - h, w, h);
        Color bg = _pcRole switch { Role.FWD => Style.Hex(0xd6453a), Role.MID => Style.Hex(0xe8bd25), _ => Style.Hex(0x2f9e4f) };
        Color fg = _pcRole == Role.MID ? Style.Hex(0x1b1a12) : Colors.White;
        Style.Box(c, r, bg, 4, 10);
        Style.Corners(c, new Rect2(r.Position.X, r.End.Y - 2, r.Size.X, 2), new Color(0, 0, 0, 0.18f), 0, 0, 4, 4);
        Style.Text(c, f, _pcName, new Rect2(r.Position.X + 9, r.Position.Y + 5, 0, size * 1.1f), size, fg, false);
        Bar(c, new Rect2(r.Position.X + 9, r.End.Y - 6 - 3, w - 18, 3), Math.Max(0, _pcStamina) / 100f, null, Style.Ink);
    }

    /// <summary>A gauge: dark track, black hairline, filled to p (in a colour or along a ramp).</summary>
    static void Bar(CanvasItem c, Rect2 r, float p, Texture2D ramp, Color fill, float alpha = 1)
    {
        c.DrawRect(r.Grow(1), new Color(0, 0, 0, alpha));
        c.DrawRect(r, new Color(15 / 255f, 20 / 255f, 17 / 255f, 0.55f * alpha));
        p = Math.Clamp(p, 0, 1);
        if (p <= 0) return;
        var fr = new Rect2(r.Position, new Vector2(r.Size.X * p, r.Size.Y));
        if (ramp != null) c.DrawTextureRectRegion(ramp, fr, new Rect2(0, 0, ramp.GetWidth() * p, 1), new Color(1, 1, 1, alpha));
        else c.DrawRect(fr, new Color(fill, alpha));
    }

    // ------------------------------------------------------------------ over the pitch

    void Overlays(MatchSnapshot a, MatchSnapshot b, float alpha, InputState input, bool attack, float dt)
    {
        bool dirty = _head != null || _aim != null || _chargeA > 0;
        _head = null;
        _aim = null;
        if (View == null || Paused) { if (dirty) _overlay.QueueRedraw(); _chargeA = 0; return; }

        // Dead-ball shot: the target on the goal mouth (red when it would miss).
        if (b.AimingShot && b.HasAimPoint)
        {
            _aim = View.WorldToUnits(new Vector3(b.AimX, b.AimY, b.AimZ));
            _aimOff = MathF.Abs(b.AimZ) > Pitch.GoalHalfWidth - 0.1 || b.AimY > Pitch.GoalHeight - 0.1;
        }

        int c = b.Controlled;
        bool ballOut = b.Phase == Phase.Fulltime;
        if (c >= 0 && !ballOut)
        {
            var head = new Vector3(Mathf.Lerp(a.X[c], b.X[c], alpha), 2.45f * b.Height[c], Mathf.Lerp(a.Z[c], b.Z[c], alpha));
            _head = View.WorldToUnits(head);
            _stamina = b.DeadBallTaker < 0 ? b.Stamina[c] : -1;
        }

        // Pass / through / shot weight while a button is held.
        int btn = -1;
        if (attack && input != null)
        {
            if (input.Held[2]) btn = 2;
            else if (input.Held[0]) btn = 0;
            else if (input.Held[1]) btn = 1;
        }
        if (btn >= 0 && _match != null && input.HoldTime[btn] > _match.SwitchT + 0.05) btn = -1;
        if (btn >= 0)
        {
            double hold = input.HoldTime[btn];
            bool shot = btn == 2;
            _chargeP = shot ? (float)Math.Min(1.15, hold / 0.85) / 1.15f : (float)Math.Min(1, hold / 0.6);
            _chargeBtn = btn;
            _chargeDead = shot && _aim != null;
        }
        _chargeA = Math.Clamp(_chargeA + (btn >= 0 ? 1 : -1) * dt / 0.12f, 0, 1);
        if (dirty || _head != null || _aim != null || _chargeA > 0) _overlay.QueueRedraw();
    }

    void DrawOverlay(Painter c)
    {
        if (_aim is Vector2 am) DrawAim(c, am);
        if (_head is Vector2 h)
        {
            // Stamina: very small and thin, just over his head.
            if (_stamina >= 0) Bar(c, new Rect2(h.X - 14, h.Y - 3, 28, 2), _stamina, null, Style.Ink);
        }
        if (_chargeA > 0)
        {
            Vector2? at = _chargeDead && _aim is Vector2 ap ? ap - new Vector2(0, 32) : _head;
            if (at is Vector2 p)
            {
                bool shot = _chargeBtn == 2;
                float w = _chargeDead ? 70 : 44, hh = _chargeDead ? 6 : 4;
                var r = new Rect2(p.X - w / 2, p.Y - 11, w, hh);
                Bar(c, r, _chargeP, _chargeDead ? RampDead : shot ? RampShot : RampPass, Colors.White, _chargeA);
                if (shot)
                {
                    // The tick: full power (the dead-ball gauge's sweet spot).
                    float tx = r.Position.X + w * ((_chargeDead ? 0.92f : 1f) / 1.15f);
                    if (_chargeDead)
                    {
                        c.DrawRect(new Rect2(tx - 2, r.Position.Y - 1, 4, hh + 2), new Color(0, 0, 0, _chargeA));
                        c.DrawRect(new Rect2(tx - 1, r.Position.Y, 2, hh), new Color(Style.Ink, _chargeA));
                    }
                    else c.DrawRect(new Rect2(tx, r.Position.Y, 1, hh), new Color(0, 0, 0, _chargeA));
                }
            }
        }
    }

    void DrawAim(Painter c, Vector2 p)
    {
        var col = _aimOff ? Style.Hex(0xff5a4d) : Style.Accent;
        // Outer ring with a glow, an inner ring that pulses, a white dot.
        c.DrawArc(p, 15.75f, 0, MathF.Tau, 40, new Color(col, 0.25f), 7, true);
        c.DrawArc(p, 15.75f, 0, MathF.Tau, 40, col, 2.5f, true);
        float k = 0.5f - 0.5f * MathF.Cos((float)_now * MathF.Tau / 1.1f);
        float r = 7 * (1 - 0.3f * k);
        c.DrawArc(p, r, 0, MathF.Tau, 24, new Color(col, 1 - 0.4f * k), 2, true);
        c.DrawCircle(p, 2.5f, Colors.White);
    }

    // ------------------------------------------------------------------ frame rate

    void Fps(double delta)
    {
        if (!ShowFps)
        {
            if (_fpsText.Length > 0) { _fpsText = ""; _fps.QueueRedraw(); }
            _frames = 0;
            _fpsT = 0;
            return;
        }
        _frames++;
        _fpsT += delta;
        if (_fpsT < 0.5) return;
        float hz = DisplayServer.ScreenGetRefreshRate();
        _fpsText = $"{_frames / _fpsT:0} fps · {_fpsT * 1000 / _frames:0.0} ms" + (hz > 0 ? $" · screen {hz:0} Hz" : "");
        _frames = 0;
        _fpsT = 0;
        _fps.QueueRedraw();
    }

    void DrawFps(Painter c)
    {
        if (_fpsText.Length == 0) return;
        var f = Style.Font(false, 0.5f);
        const int size = 13;
        var r = new Rect2(14 + _safeL, 52 + _safeT, Style.Width(f, _fpsText, size) + 12, size + 8);
        Style.Box(c, r, new Color(0, 0, 0, 0.55f), 3);
        Style.Text(c, f, _fpsText, r, size, Style.Ink);
    }
}
