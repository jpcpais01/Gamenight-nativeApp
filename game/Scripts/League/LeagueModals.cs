using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GameNight.Club;
using GameNight.Menus;
using GameNight.Sim;

namespace GameNight.League;

/// <summary>A modal that takes the whole screen (its own backdrop, no box).</summary>
public abstract partial class Sheet : Modal
{
    protected readonly Fx.World Sparks = new();

    protected Sheet(Menus.Menus ui) : base(ui)
    {
        AddChild(new Fx.Light(ci => Sparks.Draw(ci)));
    }

    protected LeagueState L => Ui.Season;
    protected const int You = LeagueState.You;

    protected override Vector2 BoxSize => Size;
    protected override void PaintBox(Rect2 b) { }

    protected override void Paint()
    {
        Box = new Rect2(Vector2.Zero, Size);
        Draw();
    }

    protected abstract void Draw();

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!IsVisibleInTree()) return;
        Sparks.Step((float)delta);
        GetChild<Control>(0).QueueRedraw();
    }

    protected static readonly Color[] Confetti = { Px.Gold, Px.Cyan, Px.Neon, Px.Hex(0xffffff), Px.Hex(0x6ff0a8) };
}

// ==================================================================== the draw

/// <summary>A new league: the sixteen clubs drawn one by one, each badge landing with a flash.</summary>
public sealed partial class LeagueDraw : Sheet
{
    const float Start = 0.7f, Each = 0.26f;
    readonly bool[] _landed = new bool[LeagueState.Clubs];
    readonly Rect2[] _cells = new Rect2[LeagueState.Clubs];

    public LeagueDraw(Menus.Menus ui) : base(ui)
    {
        Closable = false;
    }

    float End => Start + LeagueState.Clubs * Each + 0.3f;

    protected override void Background()
    {
        if (T < End) T = End;
    }

    protected override void Draw()
    {
        float W = Size.X, H = Size.Y;
        Px.Bands(this, new Rect2(Vector2.Zero, Size), new[] { Px.Hex(0x07061a), Px.Hex(0x0b0a24), Px.Hex(0x100e30), Px.Hex(0x15123c) }, new[] { 0, 0.3f, 0.6f, 0.85f });
        for (int i = 0; i < 3; i++)
        {
            float x = W * (0.2f + i * 0.3f);
            Fx.Spot(this, new Vector2(x, -4), new Vector2(x + Mathf.Sin((float)T * 0.7f + i * 2) * 60, H * 0.95f), 16, 200, Px.Hex(0xc9d8ff), 0.05f);
        }
        Px.Scanlines(this, new Rect2(Vector2.Zero, Size));
        Px.TextC(this, Px.Small, W / 2, 26, L.S.Name.ToUpperInvariant() + $" · SEASON {L.S.Season}", 9, Px.Cyan);
        Px.TextC(this, Px.Big, W / 2, 66, "THE DRAW", 48, Px.Gold, new Color(0, 0, 0, 0.6f), 3);

        const int cols = 8;
        float gap = 8, top = 84;
        float cw = (W - 28 - gap * (cols - 1)) / cols, ch = (H - top - 70 - gap) / 2;
        var audio = Audio.GameAudio.Instance;
        for (int i = 0; i < LeagueState.Clubs; i++)
        {
            var r = new Rect2(14 + (i % cols) * (cw + gap), top + (i / cols) * (ch + gap), cw, ch);
            _cells[i] = r;
            float at = Start + i * Each;
            float k = Mathf.Clamp(((float)T - at) / 0.16f, 0, 1);
            Px.Frame(this, r, new Color(1, 1, 1, 0.03f), new Color(1, 1, 1, 0.08f), null, 2, 3);
            if (k <= 0) continue;
            if (!_landed[i])
            {
                _landed[i] = true;
                var c = r.GetCenter();
                Sparks.Burst(c, new[] { Px.Hex(L.Color(i)), Px.Gold, Px.Ink }, i == You ? 40 : 16, i == You ? 240 : 150, 120, Fx.Kind.Spark, 0.7f);
                Sparks.Shock(c, i == You ? Px.Gold : Px.Hex(L.Color(i)).Lightened(0.3f), 260, 0.5f, 3);
                audio?.Whoosh();
                if (i == You) audio?.Stinger(0);
            }
            float s = Mathf.Floor(k * 4) / 4;
            bool me = i == You;
            Px.Frame(this, r, Px.Glass, me ? Px.Gold : Px.Line2, Px.ShadowSoft, 3, 4);
            DrawRect(new Rect2(r.Position + new Vector2(3, 3), new Vector2(r.Size.X - 6, 4)), Px.Hex(L.Color(i)));
            float full = Mathf.Min(Mathf.Min(44, cw - 18), (ch - 60) / 1.24f);
            float bw = full * (0.6f + 0.4f * s);
            LeagueArt.Badge(this, new Rect2(r.GetCenter().X - bw / 2, r.Position.Y + 12 + (1 - s) * 10, bw, bw * 1.24f), L.Crest(i));
            float ny = r.Position.Y + 14 + full * 1.24f;
            // The name types itself in.
            string name = L.Name(i);
            int shown = Math.Clamp((int)(((float)T - at) * 40), 0, name.Length);
            var lines = Px.Wrap(Px.Big, name[..shown], 18, cw - 8);
            foreach (var l in lines.Take(2))
            {
                ny += 16;
                Px.TextC(this, Px.Big, r.GetCenter().X, ny, Px.Fit(Px.Big, l, 18, cw - 6), 18, me ? Px.Gold : Px.Ink);
            }
            if (lines.Count > 1 && ny > r.End.Y - 22) { }
            else if (me) Px.TextC(this, Px.Small, r.GetCenter().X, r.End.Y - 10, "YOU", 8, Px.Gold);
            else Px.TextC(this, Px.Small, r.GetCenter().X, r.End.Y - 10, i < L.S.Clubs.Count && L.S.Clubs[i].Nickname.Length > 0 ? Px.Fit(Px.Small, L.S.Clubs[i].Nickname.ToUpperInvariant(), 8, cw - 8) : "", 8, Px.InkDim);
        }

        if (T >= End)
        {
            var f = L.YourFixture(0);
            string first = $"MATCHDAY 1: {L.Name(f.Other(You)).ToUpperInvariant()} {(f.Home == You ? "AT HOME" : "AWAY")}";
            Px.TextC(this, Px.Small, W / 2 - 120, H - 30, $"30 MATCHDAYS · {first}", 8, Px.Ink);
            GoldButton("go", new Rect2(W - 14 - 240, H - 56, 240, 44), "TO THE LEAGUE  >", 26, () => Ui.Close(this));
        }
        else Px.TextC(this, Px.Small, W / 2, H - 26, "TAP TO SKIP", 8, Px.InkDim);
    }
}

// ==================================================================== before kick-off

/// <summary>The match preview: both sides' form, place and star man, where it's played, and the kick-off.</summary>
public sealed partial class MatchPreview : Modal
{
    readonly Fixture _f;
    LeagueState L => Ui.Season;
    const int You = LeagueState.You;

    public MatchPreview(Menus.Menus ui, Fixture f) : base(ui)
    {
        _f = f;
    }

    protected override Vector2 BoxSize => new(700, 376);

    protected override void PaintBox(Rect2 b)
    {
        bool home = _f.Home == You;
        var date = L.Date(_f.Round).ToString("dddd d MMMM", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant();
        Kicker(b.Position + new Vector2(20, 26), $"MATCHDAY {_f.Round + 1} OF {LeagueState.Rounds} · {date}");
        Heading(b.Position + new Vector2(20, 58), home ? "Home match" : "Away day");
        var table = L.Table();
        float colW = (b.Size.X - 40 - 120) / 2;
        Column(new Rect2(b.Position.X + 20, b.Position.Y + 72, colW, 200), _f.Home, table);
        Column(new Rect2(b.End.X - 20 - colW, b.Position.Y + 72, colW, 200), _f.Away, table);

        float cx = b.GetCenter().X;
        var vs = new Rect2(cx - 26, b.Position.Y + 100, 52, 32);
        Px.Frame(this, vs, Px.Hex(0x2a0414), Px.Gold, new Color(0, 0, 0, 0.4f), 3, 4);
        Px.TextC(this, Px.Big, cx, vs.GetCenter().Y + 8, "VS", 26, Px.Gold);
        var (eh, ea) = L.Expected(_f.Home, _f.Away);
        // A bookmaker's view: who the numbers favour.
        string fav = Math.Abs(eh - ea) < 0.25 ? "TOO CLOSE TO CALL" : (eh > ea ? L.Short(_f.Home) : L.Short(_f.Away)) + " FAVOURED";
        Px.TextC(this, Px.Small, cx, vs.End.Y + 22, fav, 8, Px.InkDim);
        var first = L.S.Fixtures.FirstOrDefault(x => x.Played && x.Has(_f.Other(You)) && x.Has(You) && x.Round < _f.Round);
        if (first != null)
        {
            Px.TextC(this, Px.Small, cx, vs.End.Y + 44, "FIRST LEG", 8, Px.Cyan);
            Px.TextC(this, Px.Big, cx, vs.End.Y + 64, $"{L.Short(first.Home)} {first.Hg}-{first.Ag} {L.Short(first.Away)}", 18, Px.Ink);
        }

        // Where.
        float vy = b.End.Y - 78;
        if (home)
        {
            var grounds = Menus.Grounds.All.Where(g => g.Id != "training").ToList();
            int gi = Math.Max(0, grounds.FindIndex(g => g.Id == Ui.Club.S.Ground));
            string label = $"GROUND: {grounds[gi].Name.ToUpperInvariant()}  >";
            float w = Px.Width(Px.Big, label, 18) + 24;
            GhostButton("ground", new Rect2(b.Position.X + 20, vy - 20, w, 30), label, 18, () => Ui.Club.SetGround(grounds[(gi + 1) % grounds.Count].Id), Px.Cyan);
        }
        else
        {
            var c = L.S.Clubs[_f.Home];
            Px.Text(this, Px.Small, new Vector2(b.Position.X + 20, vy), $"AT {c.Ground.ToUpperInvariant()} · {c.Capacity:#,0} SEATS · IN THEIR COLOURS", 8, Px.Cyan);
        }
        GoldButton("kick", new Rect2(b.End.X - 20 - 230, b.End.Y - 58, 230, 44), "KICK OFF  >", 30, () =>
        {
            Ui.Close(this);
            Ui.League.Kickoff(_f);
        });
        GhostButton("back", new Rect2(b.End.X - 20 - 230 - 14 - 120, b.End.Y - 58, 120, 44), "NOT YET", 20, Dismiss);
    }

    void Column(Rect2 r, int club, List<Row> table)
    {
        float cx = r.GetCenter().X;
        LeagueArt.Badge(this, new Rect2(cx - 30, r.Position.Y, 60, 74), L.Crest(club));
        Px.TextC(this, Px.Big, cx, r.Position.Y + 96, Px.Fit(Px.Big, L.Name(club), 24, r.Size.X), 24, club == You ? Px.Gold : Px.Ink, new Color(0, 0, 0, 0.5f));
        string place = L.S.Round > 0 ? LeagueState.Ordinal(L.Place(club, table)) + " · " : "";
        Px.TextC(this, Px.Small, cx, r.Position.Y + 112, $"{place}{L.Rating(club)} OVR" + (club != You ? " · " + LeagueState.Styles[L.S.Clubs[club].Style].ToUpperInvariant() : ""), 8, Px.Hex(0xffe0a8));
        string form = L.Form(club);
        LeagueArt.Form(this, new Vector2(cx - 39, r.Position.Y + 120), form, 14);
        // The man to watch.
        var star = L.Star(club);
        if (star == null) return;
        var kit = L.Info(club).Kit;
        var av = new Rect2(cx - 70, r.Position.Y + 146, 36, 36);
        Px.Frame(this, av, new Color(0.08f, 0.07f, 0.2f, 0.9f), Art.RarityColor(star.Rarity), null, 2, 3);
        Art.Avatar(this, av.Grow(-2), star, kit);
        Px.Text(this, Px.Small, new Vector2(av.End.X + 8, av.Position.Y + 12), "STAR MAN", 8, Px.InkDim);
        Px.Text(this, Px.Big, new Vector2(av.End.X + 8, av.Position.Y + 32), Px.Fit(Px.Big, $"{star.LastName} {star.Overall}", 20, r.End.X - av.End.X - 8), 20, Px.Ink);
    }
}

// ==================================================================== around the grounds

/// <summary>
/// The rest of the matchday comes in: the clock runs, scores tick over with a flash and the
/// scorer's name, and the table on the right re-sorts itself live, rows sliding to their new
/// places. Your own result is already in, with the coins counting up.
/// </summary>
public sealed partial class AroundGrounds : Sheet
{
    readonly int _round, _coins;
    readonly Action _then;
    readonly List<Fixture> _others;
    readonly Fixture _yours;
    readonly Dictionary<int, float> _rowY = new();
    readonly Dictionary<int, double> _moved = new();
    readonly Dictionary<int, int> _lastPos = new();
    readonly int[] _shown;
    readonly double[] _flash;
    readonly string[] _flashText;
    List<Row> _live;
    int _liveMinute = -1;
    bool _rang;
    const float Intro = 0.8f, Run = 7.5f;

    public AroundGrounds(Menus.Menus ui, int round, int coins, Action then) : base(ui)
    {
        _round = round;
        _coins = coins;
        _then = then;
        _yours = L.YourFixture(round);
        _others = L.RoundOf(round).Where(f => !f.Has(You)).ToList();
        _shown = new int[_others.Count];
        _flash = Enumerable.Repeat(-9.0, _others.Count).ToArray();
        _flashText = new string[_others.Count];
        Closable = false;
    }

    int Minute => (int)Mathf.Clamp(((float)T - Intro) / Run * 95, 0, 95);
    bool Over => T > Intro + Run + 0.2;

    protected override void Background()
    {
        if (!Over) T = Intro + Run + 0.3;
    }

    public override void Dismiss()
    {
        Ui.Close(this);
        _then?.Invoke();
    }

    protected override void Draw()
    {
        float W = Size.X, H = Size.Y;
        Px.Bands(this, new Rect2(Vector2.Zero, Size), new[] { Px.Hex(0x0a1426), Px.Hex(0x0c1a30), Px.Hex(0x0f2138) }, new[] { 0, 0.4f, 0.75f });
        Px.Scanlines(this, new Rect2(Vector2.Zero, Size));
        int min = Minute;
        Px.Text(this, Px.Small, new Vector2(16, 24), $"MATCHDAY {_round + 1} · LIVE", 9, Px.Cyan);
        Px.Text(this, Px.Big, new Vector2(16, 58), "AROUND THE GROUNDS", 38, Px.Ink, new Color(0, 0, 0, 0.55f), 3);
        // The clock.
        var clock = new Rect2(W * 0.56f - 96, 20, 96, 40);
        Px.Frame(this, clock, Px.Hex(0x05040f), Over ? Px.Gold : Px.Hex(0x3ddc84), Px.ShadowSoft, 3, 4);
        string cl = Over ? "FT" : min == 0 ? "KO" : min > 90 ? $"90+{min - 90}" : $"{min}'";
        bool blink = !Over && (T % 1) < 0.5;
        Px.TextC(this, Px.Big, clock.GetCenter().X, clock.GetCenter().Y + 11, cl, 32, Over ? Px.Gold : Px.Hex(0x6ff0a8));
        if (blink) DrawRect(new Rect2(clock.Position.X + 8, clock.Position.Y + 8, 4, 4), Px.Hex(0xff4040));

        float lw = Mathf.Round(W * 0.56f) - 16;
        Yours(new Rect2(16, 72, lw, 66));
        float y = 148, rh = Mathf.Floor((H - 148 - 64) / Math.Max(1, _others.Count));
        var audio = Audio.GameAudio.Instance;
        for (int i = 0; i < _others.Count; i++)
        {
            var f = _others[i];
            int count = f.Goals.Count(g => g.Minute <= min);
            if (count > _shown[i])
            {
                var g = f.Goals.Where(x => x.Minute <= min).Last();
                _shown[i] = count;
                _flash[i] = T;
                _flashText[i] = $"GOAL! {g.Player.ToUpperInvariant()} {(g.Minute > 90 ? "90+" + (g.Minute - 90) : g.Minute.ToString())}'";
                audio?.Stinger(i);
                Sparks.Burst(new Vector2(16 + lw / 2, y + i * rh + rh / 2), new[] { Px.Gold, Px.Ink }, 10, 120, 80);
            }
            Other(new Rect2(16, y + i * rh, lw, rh - 4), f, min, i);
        }
        LiveTable(new Rect2(W * 0.56f + 12, 20, W - (W * 0.56f + 12) - 16, H - 36), min);

        if (Over)
        {
            if (!_rang)
            {
                _rang = true;
                Audio.GameAudio.Instance?.Whistle(3);
            }
            GoldButton("paper", new Rect2(16 + lw - 260, H - 54, 260, 44), "READ THE PAPER  >", 26, Dismiss);
        }
        else GhostButton("skip", new Rect2(16 + lw - 130, H - 50, 130, 38), "SKIP  >>", 20, () => T = Intro + Run + 0.3);
    }

    void Yours(Rect2 r)
    {
        var f = _yours;
        Px.Frame(this, r, new Color(0.2f, 0.16f, 0.05f, 0.85f), Px.Gold, Px.Shadow);
        float cx = r.Position.X + r.Size.X * 0.42f;
        var sb = new Rect2(cx - 40, r.Position.Y + 12, 80, 40);
        Px.Frame(this, sb, Px.Hex(0x0b0a1e), Px.Gold, null, 3, 4);
        Px.TextC(this, Px.Big, cx, sb.GetCenter().Y + 12, $"{f.Hg} - {f.Ag}", 34, Px.Ink);
        Px.TextC(this, Px.Small, cx, sb.Position.Y - 2, "FULL TIME", 8, Px.Gold);
        LeagueArt.Badge(this, new Rect2(sb.Position.X - 40, r.Position.Y + 12, 30, 37), L.Crest(f.Home));
        LeagueArt.Badge(this, new Rect2(sb.End.X + 10, r.Position.Y + 12, 30, 37), L.Crest(f.Away));
        Px.TextR(this, Px.Big, sb.Position.X - 48, r.Position.Y + 38, L.Short(f.Home), 22, f.Home == You ? Px.Gold : Px.Ink);
        Px.Text(this, Px.Big, new Vector2(sb.End.X + 48, r.Position.Y + 38), L.Short(f.Away), 22, f.Away == You ? Px.Gold : Px.Ink);
        // The coins.
        float k = Mathf.Clamp(((float)T - 0.3f) / 1.2f, 0, 1);
        int shown = (int)Mathf.Round(_coins * (1 - Mathf.Pow(1 - k, 3)));
        if (k >= 1 && _coins > 0 && !_coinsRang)
        {
            _coinsRang = true;
            Audio.GameAudio.Instance?.Coins();
        }
        string s = "+" + Px.Thousands(shown);
        Px.Coin(this, new Vector2(r.End.X - 30 - Px.Width(Px.Big, s, 26) - 10, r.Position.Y + 24), 16);
        Px.TextR(this, Px.Big, r.End.X - 14, r.Position.Y + 42, s, 26, Px.Hex(0xffe066), Colors.Black);
    }

    bool _coinsRang;

    void Other(Rect2 r, Fixture f, int min, int i)
    {
        int h = f.Goals.Count(g => g.Club == f.Home && g.Minute <= min);
        int a = f.Goals.Count(g => g.Club == f.Away && g.Minute <= min);
        double since = T - _flash[i];
        bool flash = since < 1.4;
        bool on = flash && (since % 0.3) < 0.18;
        Px.Frame(this, r, on ? new Color(1, 0.83f, 0.28f, 0.35f) : Px.Glass, flash ? Px.Gold : Px.Line, null, 2, 3);
        float cx = r.Position.X + r.Size.X * 0.42f, by = r.GetCenter().Y + 7;
        var sb = new Rect2(cx - 28, r.Position.Y + 3, 56, r.Size.Y - 6);
        Px.Frame(this, sb, Px.Hex(0x0b0a1e), Px.Line2, null, 2, 3);
        Px.TextC(this, Px.Big, cx, by + 1, $"{h} - {a}", 22, Over ? Px.Ink : Px.Hex(0x6ff0a8));
        float nh = Mathf.Min(16, r.Size.Y - 6);
        LeagueArt.Badge(this, new Rect2(sb.Position.X - 20, r.GetCenter().Y - nh * 0.62f, nh * 0.8f, nh * 1.24f * 0.8f), L.Crest(f.Home));
        LeagueArt.Badge(this, new Rect2(sb.End.X + 6, r.GetCenter().Y - nh * 0.62f, nh * 0.8f, nh * 1.24f * 0.8f), L.Crest(f.Away));
        float nw = sb.Position.X - 26 - r.Position.X - 8;
        Px.TextR(this, Px.Big, sb.Position.X - 26, by, Px.Fit(Px.Big, L.Name(f.Home), 18, nw), 18, Px.Ink);
        if (flash) Px.Text(this, Px.Small, new Vector2(sb.End.X + 28, by - 2), Px.Fit(Px.Small, _flashText[i], 8, r.End.X - sb.End.X - 34), 8, Px.Gold);
        else Px.Text(this, Px.Big, new Vector2(sb.End.X + 28, by), Px.Fit(Px.Big, L.Name(f.Away), 18, r.End.X - sb.End.X - 34), 18, Px.Ink);
    }

    void LiveTable(Rect2 r, int min)
    {
        if (min != _liveMinute)
        {
            _liveMinute = min;
            _live = L.LiveTable(_round, min, You);
        }
        Px.Frame(this, r, Px.Glass, Px.Line, Px.ShadowSoft);
        Px.Text(this, Px.Small, r.Position + new Vector2(10, 16), Over ? "THE TABLE" : "AS IT STANDS", 8, Over ? Px.Gold : Px.Hex(0x6ff0a8));
        Px.TextR(this, Px.Small, r.End.X - 10, r.Position.Y + 16, "PTS", 8, Px.InkDim);
        float top = r.Position.Y + 22, rh = (r.Size.Y - 26) / LeagueState.Clubs;
        float dt = (float)GetProcessDeltaTime();
        for (int p = 0; p < _live.Count; p++)
        {
            var row = _live[p];
            float target = top + p * rh;
            if (!_rowY.TryGetValue(row.Club, out float y)) y = target;
            y = Mathf.Lerp(y, target, 1 - Mathf.Exp(-10 * dt));
            if (Mathf.Abs(y - target) < 0.4f) y = target;
            _rowY[row.Club] = y;
            if (_lastPos.TryGetValue(row.Club, out int lp) && lp != p) _moved[row.Club] = T * (p < lp ? 1 : -1);
            _lastPos[row.Club] = p;
            bool me = row.Club == You;
            var rr = new Rect2(r.Position.X + 6, Mathf.Round(y), r.Size.X - 12, rh - 1);
            if (me) DrawRect(rr, new Color(1, 0.83f, 0.28f, 0.2f));
            if (_moved.TryGetValue(row.Club, out double at) && T - Math.Abs(at) < 1.2)
                DrawRect(rr, new Color(at > 0 ? Px.Win : Px.Loss, 0.22f));
            if (LeagueArt.Zone(p + 1) is Color z) DrawRect(new Rect2(rr.Position.X, rr.Position.Y + 1, 3, rr.Size.Y - 2), z);
            float base_ = rr.Position.Y + rh * 0.5f + 6;
            int fs = rh >= 18 ? 18 : 16;
            Px.TextR(this, Px.Big, rr.Position.X + 24, base_, (p + 1).ToString(), fs, Px.InkDim);
            LeagueArt.Badge(this, new Rect2(rr.Position.X + 30, rr.Position.Y + (rh - 15) / 2, 12, 15), L.Crest(row.Club));
            Px.Text(this, Px.Big, new Vector2(rr.Position.X + 48, base_), Px.Fit(Px.Big, L.Name(row.Club), fs, rr.Size.X - 90), fs, me ? Px.Gold : Px.Ink);
            Px.TextR(this, Px.Big, rr.End.X - 4, base_, row.Pts.ToString(), fs, me ? Px.Gold : Px.Ink);
        }
    }
}

// ==================================================================== the morning paper

/// <summary>
/// The paper the morning after: it spins in and lands with a thud. Masthead and date, the
/// headline, a pixel press photo with its caption, the report, the rest of the matchday and the
/// top of the table.
/// </summary>
public sealed partial class Newspaper : Sheet
{
    readonly Paper _p;
    readonly Action _then;
    bool _landed;
    const float Spin = 0.9f;
    static readonly Color Newsprint = Px.Hex(0xefe6cf), Newsprint2 = Px.Hex(0xe4d9bd), Ink = Px.Hex(0x1d1a16), Ink2 = Px.Hex(0x4a443a);

    public Newspaper(Menus.Menus ui, int round, Action then) : base(ui)
    {
        _p = Gazette.Make(L, round);
        _then = then;
    }

    public override void Dismiss()
    {
        Ui.Close(this);
        _then?.Invoke();
    }

    protected override void Background()
    {
        if (T < Spin) T = Spin;
    }

    protected override void Draw()
    {
        float W = Size.X, H = Size.Y;
        // The desk: dark wood in bands, a lamp's pool of light.
        Px.Bands(this, new Rect2(Vector2.Zero, Size), new[] { Px.Hex(0x1a0f0a), Px.Hex(0x22140c), Px.Hex(0x2a190f), Px.Hex(0x22140c) }, new[] { 0, 0.3f, 0.55f, 0.85f });
        for (float y = 7; y < H; y += 23) DrawRect(new Rect2(0, y, W, 2), new Color(0, 0, 0, 0.12f));
        float pw = Mathf.Min(W - 200, 640), ph = H - 20;
        var paper = new Rect2(14 + Mathf.Max(0, (W - 190 - pw) / 2), 10, pw, ph);
        DrawColoredPolygon(Px.Ellipse(paper.GetCenter(), pw * 0.7f, ph * 0.7f, 24), new Color(1, 0.85f, 0.6f, 0.05f));

        float k = Mathf.Clamp((float)T / Spin, 0, 1);
        if (k >= 1 && !_landed)
        {
            _landed = true;
            Audio.GameAudio.Instance?.Stinger(1);
            Sparks.Burst(new Vector2(paper.Position.X, paper.End.Y), new[] { Px.Hex(0xd9cfb6) }, 10, 90, 160, Fx.Kind.Mote, 0.6f);
            Sparks.Burst(new Vector2(paper.End.X, paper.End.Y), new[] { Px.Hex(0xd9cfb6) }, 10, 90, 160, Fx.Kind.Mote, 0.6f);
        }
        // Spinning in, stepped like an old film effect, then a little settle.
        float ks = Mathf.Floor(k * 14) / 14;
        float rot = (1 - ks) * Mathf.Tau * 2.25f;
        float scale = 0.08f + 0.92f * ks * ks * (3 - 2 * ks);
        float settle = k >= 1 ? Mathf.Max(0, 1 - ((float)T - Spin) / 0.25f) : 0;
        var centre = paper.GetCenter() + new Vector2(0, -settle * 4);
        DrawSetTransform(centre, rot, new Vector2(scale, scale) * (1 + settle * 0.02f));
        var local = new Rect2(-paper.Size / 2, paper.Size);
        DrawRect(local.Translated(new Vector2(8, 8)), new Color(0, 0, 0, 0.45f));
        Page(local);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        if (k >= 1)
        {
            float bx = W - 14 - 160;
            if (_then != null) GoldButton("go", new Rect2(bx, H - 60, 160, 46), "CONTINUE  >", 24, Dismiss);
            else GhostButton("go", new Rect2(bx, H - 56, 160, 42), "CLOSE", 22, Dismiss);
            Px.Text(this, Px.Small, new Vector2(bx, 30), "THE MORNING", 8, Px.Hex(0xf0e2c0, 0.7f));
            Px.Text(this, Px.Small, new Vector2(bx, 44), "AFTER", 8, Px.Hex(0xf0e2c0, 0.7f));
            Px.Text(this, Px.Big, new Vector2(bx, 80), $"MD {_p.Round + 1}", 34, Px.Hex(0xf0e2c0));
            if (_p.Round > 0)
            {
                int d = _p.Before - _p.After;
                string mv = d > 0 ? $"UP {d}" : d < 0 ? $"DOWN {-d}" : "NO CHANGE";
                Px.Text(this, Px.Small, new Vector2(bx, 104), mv, 8, d > 0 ? Px.Win : d < 0 ? Px.Loss : Px.InkDim);
            }
            Px.Text(this, Px.Big, new Vector2(bx, 134), LeagueState.Ordinal(_p.After), 44, LeagueArt.Zone(_p.After) ?? Px.Ink, new Color(0, 0, 0, 0.5f), 3);
        }
    }

    void Page(Rect2 r)
    {
        DrawRect(r, Newsprint);
        // Age and folds.
        DrawRect(new Rect2(r.Position.X, r.Position.Y + r.Size.Y * 0.5f - 1, r.Size.X, 2), Newsprint2);
        DrawRect(new Rect2(r.End.X - 6, r.Position.Y, 6, r.Size.Y), Newsprint2);
        float x0 = r.Position.X + 14, x1 = r.End.X - 14, w = x1 - x0;
        float y = r.Position.Y + 10;
        // Masthead.
        DrawRect(new Rect2(x0, y, w, 2), Ink);
        Px.TextC(this, Px.Big, r.GetCenter().X, y + 34, _p.Masthead, 38, Ink);
        y += 42;
        DrawRect(new Rect2(x0, y, w, 1), Ink);
        Px.Text(this, Px.Small, new Vector2(x0, y + 11), _p.Dateline, 8, Ink2);
        Px.TextR(this, Px.Small, x1, y + 11, $"No. {_p.Round + 1 + (L.S.Season - 1) * 30} · ONE COIN", 8, Ink2);
        y += 15;
        DrawRect(new Rect2(x0, y, w, 2), Ink);
        y += 6;
        // Kicker band in the club colour.
        var kc = Px.Hex(L.Color(You));
        float kw = Px.Width(Px.Small, _p.Kicker, 8) + 14;
        DrawRect(new Rect2(x0, y, kw, 13), kc);
        Px.Text(this, Px.Small, new Vector2(x0 + 7, y + 10), _p.Kicker, 8, kc.Luminance > 0.6f ? Ink : Newsprint);
        y += 15;
        // The headline: as big as fits on two lines.
        int hs = 50;
        List<string> lines;
        do
        {
            lines = Px.Wrap(Px.Big, _p.Headline, hs, w);
            if (lines.Count <= 2 && lines.All(l => Px.Width(Px.Big, l, hs) <= w)) break;
            hs -= 2;
        } while (hs > 24);
        foreach (var l in lines.Take(2))
        {
            y += hs * 0.8f;
            Px.Text(this, Px.Big, new Vector2(x0, y), l, hs, Ink);
        }
        y += 8;
        DrawRect(new Rect2(x0, y, w, 1), Ink);
        y += 6;

        // Left: the score and the photo. Right: standfirst and report.
        float bottomH = 66;
        float colTop = y, colBot = r.End.Y - bottomH - 8;
        float lw = Mathf.Round(w * 0.42f);
        Scoreline(new Rect2(x0, colTop, lw, 34));
        var ph = new Rect2(x0, colTop + 40, lw, Mathf.Min(lw * 0.625f, colBot - colTop - 40 - 14));
        Picture(ph);
        Px.Text(this, Px.Small, new Vector2(x0, ph.End.Y + 11), Px.Fit(Px.Small, _p.Caption.ToUpperInvariant(), 8, lw), 8, Ink2);

        float rx = x0 + lw + 14, rw = x1 - rx;
        DrawRect(new Rect2(rx - 8, colTop, 1, colBot - colTop), Ink2);
        float ty = colTop + 2;
        foreach (var l in Px.Wrap(Px.Big, _p.Standfirst, 18, rw).Take(4))
        {
            ty += 15;
            Px.Text(this, Px.Big, new Vector2(rx, ty), l, 18, Ink);
        }
        ty += 6;
        bool cut = false;
        foreach (var para in _p.Body)
        {
            foreach (var l in Px.Wrap(Px.Small, para, 8, rw))
            {
                if (ty + 11 > colBot)
                {
                    cut = true;
                    break;
                }
                ty += 11;
                Px.Text(this, Px.Small, new Vector2(rx, ty), l, 8, Ink2);
            }
            if (cut) break;
            ty += 4;
        }

        // The rest of the matchday and the top of the table.
        float by = r.End.Y - bottomH - 4;
        DrawRect(new Rect2(x0, by, w, 2), Ink);
        Px.Text(this, Px.Small, new Vector2(x0, by + 12), "AROUND THE LEAGUE", 8, Ink);
        float cw = (w * 0.68f) / 2;
        for (int i = 0; i < _p.Others.Count; i++)
        {
            var f = _p.Others[i];
            float cx = x0 + (i % 2) * cw, cy = by + 24 + (i / 2) * 11;
            Px.Text(this, Px.Small, new Vector2(cx, cy), Px.Fit(Px.Small, $"{L.Short(f.Home)} {f.Hg}-{f.Ag} {L.Short(f.Away)}", 8, cw - 6), 8, Ink2);
        }
        float tx = x0 + w * 0.7f;
        DrawRect(new Rect2(tx - 8, by + 4, 1, bottomH - 4), Ink2);
        Px.Text(this, Px.Small, new Vector2(tx, by + 12), "TOP OF THE TABLE", 8, Ink);
        var rows = _p.Table.Take(3).ToList();
        if (_p.After > 3) rows.Add(_p.Table[_p.After - 1]);
        float yy = by + 24;
        foreach (var row in rows)
        {
            int pos = _p.Table.IndexOf(row) + 1;
            var col = row.Club == You ? kc.Darkened(0.25f) : Ink2;
            Px.Text(this, Px.Small, new Vector2(tx, yy), $"{pos}. {Px.Fit(Px.Small, L.Name(row.Club).ToUpperInvariant(), 8, x1 - tx - 40)}", 8, col);
            Px.TextR(this, Px.Small, x1, yy, row.Pts.ToString(), 8, col);
            yy += 11;
        }
    }

    void Scoreline(Rect2 r)
    {
        var f = _p.Yours;
        DrawRect(r, Ink);
        LeagueArt.Badge(this, new Rect2(r.Position.X + 4, r.Position.Y + 3, 22, 28), L.Crest(f.Home));
        LeagueArt.Badge(this, new Rect2(r.End.X - 26, r.Position.Y + 3, 22, 28), L.Crest(f.Away));
        string s = $"{L.Short(f.Home)} {f.Hg} - {f.Ag} {L.Short(f.Away)}";
        Px.TextC(this, Px.Big, r.GetCenter().X, r.GetCenter().Y + 9, s, 28, Newsprint);
    }

    void Picture(Rect2 r)
    {
        var f = _p.Yours;
        int opp = f.Other(You);
        var star = Ui.Club.S.Cards.FirstOrDefault(c => c.Name == _p.Hero) ?? L.Star(You);
        var tex = LeagueArt.Photo(_p.Photo, L.Info(You).Kit, L.Info(opp).Kit, star?.Skin ?? 1, star?.Hair ?? 0, L.Color(f.Home), _p.Round * 977 + L.S.Season);
        DrawRect(r.Grow(2), Ink);
        DrawTextureRect(tex, r, false);
    }
}

// ==================================================================== the end of the season

/// <summary>The season's end: the trophy and confetti if it's yours, else where you finished
/// and who won it; the prize money; then on to next season.</summary>
public sealed partial class SeasonFinale : Sheet
{
    readonly int _pos, _prize;
    readonly bool _paidNow, _star;
    readonly List<Row> _table;
    double _nextRain;

    public SeasonFinale(Menus.Menus ui) : base(ui)
    {
        _table = L.Table();
        _pos = L.Place(You, _table);
        _prize = LeagueState.Prize[_pos - 1];
        int paid = L.PayPrize();
        _paidNow = paid > 0;
        if (_paidNow)
        {
            Ui.Club.Earn(paid);
            // Champions earn a star over the crest.
            if (_pos == 1 && Ui.Club.S.Crest.Stars < 5)
            {
                var c = Ui.Club.S.Crest.Clone();
                c.Stars++;
                Ui.Club.SetCrest(c);
                _star = true;
            }
        }
    }

    protected override void Draw()
    {
        float W = Size.X, H = Size.Y;
        bool champ = _pos == 1;
        Px.Bands(this, new Rect2(Vector2.Zero, Size), champ
            ? new[] { Px.Hex(0x1a1004), Px.Hex(0x2a1a06), Px.Hex(0x3a2408), Px.Hex(0x2a1a06) }
            : new[] { Px.Hex(0x0a1426), Px.Hex(0x0c1a30), Px.Hex(0x0f2138), Px.Hex(0x0c1a30) }, new[] { 0, 0.3f, 0.6f, 0.85f });
        Px.Scanlines(this, new Rect2(Vector2.Zero, Size));
        var tc = new Vector2(W * 0.27f, H * 0.5f);
        if (champ)
        {
            Fx.Beams(this, tc, Px.Hex(0xffe9a0), 12, H * 0.8f, 0.07f, (float)T * 0.2f, 0.06f);
            if (T > _nextRain)
            {
                _nextRain = T + 0.9;
                Sparks.Rain(Size, Confetti, 40);
            }
            if (T < 0.05) Audio.GameAudio.Instance?.PackBurst(3);
        }
        float bob = Mathf.Floor((float)(T * 2 % 4)) switch { 1 => -2, 2 => -4, 3 => -2, _ => 0 };
        float th = H * 0.6f;
        if (champ) LeagueArt.Trophy(this, new Rect2(tc.X - th * 0.385f, tc.Y - th * 0.5f + bob, th * 0.77f, th), (float)(T * 0.5 % 1.6));
        else
        {
            // Not this time: your badge under a spotlight, the place on a plate.
            Fx.Spot(this, new Vector2(tc.X, 0), new Vector2(tc.X, tc.Y + th * 0.42f), 18, th * 0.9f, Px.Hex(0xc9d8ff), 0.07f);
            float cw = th * 0.62f;
            CrestArt.Draw(this, new Rect2(tc.X - cw / 2, tc.Y - th * 0.46f, cw, cw * 1.24f), Ui.Club.S.Crest);
            var plate = new Rect2(tc.X - 70, tc.Y + th * 0.36f, 140, 34);
            Px.Frame(this, plate, Px.Hex(0x261a10), Px.Hex(0x9a6408), Px.ShadowSoft, 3, 4);
            Px.TextC(this, Px.Big, tc.X, plate.GetCenter().Y + 8, $"FINISHED {LeagueState.Ordinal(_pos)}", 22, Px.Hex(0xffd447));
        }

        float x = W * 0.5f;
        Px.Text(this, Px.Small, new Vector2(x, 54), $"{L.S.Name.ToUpperInvariant()} · SEASON {L.S.Season}", 9, Px.Cyan);
        Px.Text(this, Px.Big, new Vector2(x, 112), champ ? "CHAMPIONS!" : $"{LeagueState.Ordinal(_pos)} PLACE", 66, champ ? Px.Gold : Px.Ink, new Color(0, 0, 0, 0.6f), 4);
        var me = _table[_pos - 1];
        Px.Text(this, Px.Big, new Vector2(x, 144), $"{me.Pts} PTS · W{me.W} D{me.D} L{me.L} · GOALS {me.Gf}-{me.Ga}", 22, Px.Ink);
        float y = 170;
        if (!champ)
        {
            LeagueArt.Badge(this, new Rect2(x, y, 26, 32), L.Crest(_table[0].Club));
            Px.Text(this, Px.Big, new Vector2(x + 34, y + 22), $"{L.Name(_table[0].Club)} are champions", 22, Px.Gold);
            y += 40;
        }
        var top = L.Scorers().FirstOrDefault();
        if (top.name != null)
        {
            Px.Text(this, Px.Small, new Vector2(x, y + 10), $"GOLDEN BOOT: {top.name.ToUpperInvariant()} ({L.Short(top.club)}) · {top.goals} GOALS", 8, Px.InkDim);
            y += 24;
        }
        if (_star)
        {
            Px.Text(this, Px.Small, new Vector2(x, y + 10), "A NEW STAR OVER YOUR CREST", 8, Px.Gold);
            CrestArt.Draw(this, new Rect2(x + 200, y - 10, 34, 42), Ui.Club.S.Crest);
            y += 30;
        }
        // Prize money, counting up.
        float k = Mathf.Clamp(((float)T - 0.6f) / 1.4f, 0, 1);
        int shown = (int)Mathf.Round(_prize * (1 - Mathf.Pow(1 - k, 3)));
        Px.Text(this, Px.Small, new Vector2(x, H - 100), _paidNow ? "PRIZE MONEY" : "PRIZE MONEY (PAID)", 8, Px.InkDim);
        Px.Coin(this, new Vector2(x, H - 88), 22);
        Px.Text(this, Px.Big, new Vector2(x + 32, H - 66), "+" + Px.Thousands(_paidNow ? shown : _prize), 38, Px.Hex(0xffe066), Colors.Black);

        GoldButton("next", new Rect2(W - 16 - 260, H - 60, 260, 46), "NEXT SEASON  >", 28, () =>
        {
            var news = L.NextSeason();
            Ui.Close(this);
            Ui.Toast(news.Count >= 6 ? $"Up: {Gazette.Word(news[1].Replace(" promoted", ""))}, {Gazette.Word(news[3].Replace(" promoted", ""))}, {Gazette.Word(news[5].Replace(" promoted", ""))}" : $"Season {L.S.Season} begins");
            Ui.Open(new LeagueDraw(Ui));
        });
        GhostButton("later", new Rect2(W - 16 - 260 - 14 - 110, H - 56, 110, 42), "LATER", 20, () => Ui.Close(this));
    }
}

// ==================================================================== a club

/// <summary>A club's page: badge and story, manager and ground, how they play, their best
/// players, and how you've done against them this season.</summary>
public sealed partial class ClubSheet : Modal
{
    readonly int _c;
    LeagueState L => Ui.Season;
    const int You = LeagueState.You;

    public ClubSheet(Menus.Menus ui, int club) : base(ui)
    {
        _c = club;
    }

    protected override Vector2 BoxSize => new(720, 370);

    protected override void PaintBox(Rect2 b)
    {
        bool me = _c == You;
        DrawRect(new Rect2(b.Position + new Vector2(3, 3), new Vector2(b.Size.X - 6, 6)), Px.Hex(L.Color(_c)));
        LeagueArt.Badge(this, new Rect2(b.Position.X + 20, b.Position.Y + 22, 84, 104), L.Crest(_c));
        float x = b.Position.X + 122;
        var cl = me ? null : L.S.Clubs[_c];
        Kicker(new Vector2(x, b.Position.Y + 34), me ? "YOUR CLUB" : $"{cl.Nickname} · EST. {cl.Founded}");
        Px.Text(this, Px.Big, new Vector2(x, b.Position.Y + 70), Px.Fit(Px.Big, L.Name(_c), 38, b.End.X - x - 60), 38, me ? Px.Gold : Px.Ink, new Color(0, 0, 0, 0.55f), 3);
        var table = L.Table();
        int pos = L.Place(_c, table);
        var row = table[pos - 1];
        float y = b.Position.Y + 94;
        void Fact(string k, string v)
        {
            Px.Text(this, Px.Small, new Vector2(x, y), k, 8, Px.InkDim);
            Px.Text(this, Px.Big, new Vector2(x + 86, y + 2), Px.Fit(Px.Big, v, 18, 230), 18, Px.Ink);
            y += 20;
        }
        Fact("MANAGER", L.Manager(_c));
        Fact("GROUND", me ? L.GroundName(_c) : $"{cl.Ground} · {cl.Capacity:#,0}");
        Fact("STYLE", me ? Ui.Club.Formation.Name : $"{LeagueState.Styles[cl.Style]} · {Formations.ById(cl.Formation).Name}");
        Fact("RATING", $"{L.Rating(_c)} OVR");
        Fact("SEASON", L.S.Round > 0 ? $"{LeagueState.Ordinal(pos)} · {row.Pts} pts · {row.W}-{row.D}-{row.L}" : "Yet to play");
        LeagueArt.Form(this, new Vector2(x, y), L.Form(_c), 14);
        if (!me && cl.Promoted) Px.Text(this, Px.Small, new Vector2(x + 90, y + 11), "NEWLY PROMOTED", 8, Px.Win);

        // Their best players.
        float px = b.Position.X + b.Size.X * 0.56f, py = b.Position.Y + 112;
        Px.Text(this, Px.Small, new Vector2(px, py - 8), "KEY PLAYERS", 8, Px.Cyan);
        var players = me
            ? Ui.Club.Starters().Where(p => p != null).OrderByDescending(p => p.Overall).Take(5).ToList()
            : cl.Squad.Take(11).OrderByDescending(p => p.Overall).Take(5).ToList();
        var kit = L.Info(_c).Kit;
        var goals = L.Scorers().Where(s => s.club == _c).ToDictionary(s => s.name, s => s.goals);
        foreach (var p in players)
        {
            var av = new Rect2(px, py, 28, 28);
            Px.Frame(this, av, new Color(0.08f, 0.07f, 0.2f, 0.9f), Art.RarityColor(p.Rarity), null, 2, 3);
            Art.Avatar(this, av.Grow(-2), p, kit);
            Px.Text(this, Px.Big, new Vector2(av.End.X + 8, py + 20), Px.Fit(Px.Big, p.Name, 18, 150), 18, Px.Ink);
            Px.TextR(this, Px.Small, b.End.X - 70, py + 18, p.Position.ToString() + (goals.TryGetValue(p.Name, out int g) ? $" · {g}G" : ""), 8, Px.InkDim);
            Px.TextR(this, Px.Big, b.End.X - 24, py + 21, p.Overall.ToString(), 22, Art.RarityColor(p.Rarity));
            py += 32;
        }

        // Against you this season.
        if (!me)
        {
            var meet = L.S.Fixtures.Where(f => f.Has(_c) && f.Has(You)).OrderBy(f => f.Round).ToList();
            float my = b.End.Y - 28;
            Px.Text(this, Px.Small, new Vector2(b.Position.X + 20, my), "V YOU", 8, Px.Cyan);
            float mx = b.Position.X + 74;
            foreach (var f in meet)
            {
                string s = f.Played ? $"MD{f.Round + 1} {(f.Home == You ? "H" : "A")} {f.GoalsFor(You)}-{f.GoalsAgainst(You)}" : $"MD{f.Round + 1} {(f.Home == You ? "HOME" : "AWAY")}";
                Px.Text(this, Px.Big, new Vector2(mx, my + 2), s, 18, f.Played ? LeagueArt.ResultColor(f.ResultFor(You)) : Px.InkDim);
                mx += Px.Width(Px.Big, s, 18) + 24;
            }
        }
    }
}
