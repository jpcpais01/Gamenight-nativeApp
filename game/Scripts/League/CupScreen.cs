using System;
using System.Linq;
using Godot;
using GameNight.Club;
using GameNight.Menus;

namespace GameNight.League;

/// <summary>
/// A cup run: the bracket on the left (every tie, your path in gold, the rounds still to come as
/// empty boxes fed by the winners), your next tie on the right with PLAY and SIMULATE, or how it
/// ended: the cup held high or the round you went out in.
/// </summary>
public sealed partial class CupScreen : PxCanvas
{
    readonly Menus.Menus _ui;
    CupState C => _ui.CupRun;
    ClubState Club => _ui.Club;
    const int You = ClubPool.You;

    public CupScreen(Menus.Menus ui)
    {
        _ui = ui;
    }

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        if (!C.Active)
        {
            _ui.Go(_ui.Map);
            return;
        }
        var d = C.Def;
        var tint = Px.Hex(d.Color);
        NightBackdrop(new[] { Px.Hex(0x0a1a1a), Px.Hex(0x0c2220), Px.Hex(0x0f2a26), Px.Hex(0x12322c) });
        Px.Scanlines(this, new Rect2(0, 0, W, H));
        BackButton(new Vector2(14, 12), () => _ui.Go(_ui.Map));
        string kicker = d.Special ? "SPECIAL TOURNAMENT" : $"NATIONAL CUP · {Ladder.CountryOf(d.Country).Name.ToUpperInvariant()}";
        Px.Text(this, Px.Small, new Vector2(66, 24), kicker, 8, tint);
        float coinsW = Px.Width(Px.Big, Px.Thousands(Club.S.Coins), 26) + 60;
        int ts = 38;
        string title = d.Name.ToUpperInvariant();
        while (ts > 24 && Px.Width(Px.Big, title, ts) > W - 80 - coinsW) ts -= 2;
        Title(new Vector2(66, 52), title, ts);
        Coins(W - 16, 14, Club.S.Coins);

        float split = Mathf.Round(W * 0.6f);
        Bracket(new Rect2(14, 70, split - 28, H - 82));
        var card = new Rect2(split, 70, W - split - 14, H - 82);
        if (C.Next is { } f) NextCard(card, f);
        else EndCard(card);
    }

    // ---------------------------------------------------------------- the bracket

    void Bracket(Rect2 r)
    {
        int rounds = C.Rounds, size = C.Def.Size;
        float gap = 12, cw = (r.Size.X - gap * (rounds - 1)) / rounds;
        int n0 = size / 2;
        float bh = Mathf.Min(40, (r.Size.Y - 18 - (n0 - 1) * 4) / n0);
        float top = r.Position.Y + 16;
        float span = r.Size.Y - 16;
        for (int round = 0; round < rounds; round++)
        {
            float x = r.Position.X + round * (cw + gap);
            bool now = round == C.S.Round;
            Px.TextC(this, Px.Small, x + cw / 2, r.Position.Y + 8, C.RoundName(round), 8, now ? Px.Gold : Px.InkDim);
            int n = size >> (round + 1);
            var ties = C.RoundOf(round).ToList();
            for (int k = 0; k < n; k++)
            {
                float cy = top + (k + 0.5f) * span / n;
                var box = new Rect2(x, Mathf.Round(cy - bh / 2), cw, bh);
                // The line on to the next round.
                if (round < rounds - 1)
                {
                    float nx = x + cw + gap / 2, ny = top + (k / 2 + 0.5f) * span / (n / 2);
                    var lc = k < ties.Count && ties[k].Played && CupState.Winner(ties[k]) == You ? Px.Gold : Px.Line2;
                    DrawRect(new Rect2(x + cw, cy - 1, gap / 2, 2), lc);
                    DrawRect(new Rect2(nx - 1, Mathf.Min(cy, ny) - 1, 2, Mathf.Abs(ny - cy) + 2), lc);
                    DrawRect(new Rect2(nx, ny - 1, gap / 2, 2), lc);
                }
                if (k < ties.Count) TieBox(box, ties[k]);
                else
                {
                    // Still to be drawn: the winners feeding it, once they're known.
                    var prev = C.RoundOf(round - 1).ToList();
                    int a = Feeder(prev, k * 2), b = Feeder(prev, k * 2 + 1);
                    TieBox(box, null, a, b);
                }
            }
        }
    }

    static int Feeder(System.Collections.Generic.List<Fixture> prev, int i) => i < prev.Count && prev[i].Played ? CupState.Winner(prev[i]) : -1;

    void TieBox(Rect2 r, Fixture f, int a = -1, int b = -1)
    {
        bool mine = f != null ? f.Has(You) : a == You || b == You;
        bool live = f != null && !f.Played && f.Has(You);
        Px.Frame(this, r, mine ? new Color(0.2f, 0.16f, 0.05f, 0.85f) : new Color(0.03f, 0.06f, 0.08f, 0.8f), live ? Px.Gold : mine ? Px.Hex(0x9a7420) : Px.Line, null, 1, 2);
        float lh = (r.Size.Y - 4) / 2;
        Line(new Rect2(r.Position.X + 3, r.Position.Y + 2, r.Size.X - 6, lh), f?.Home ?? a, f, true);
        Line(new Rect2(r.Position.X + 3, r.Position.Y + 2 + lh, r.Size.X - 6, lh), f?.Away ?? b, f, false);
    }

    void Line(Rect2 r, int club, Fixture f, bool home)
    {
        float base_ = r.Position.Y + r.Size.Y * 0.5f + 5;
        if (club < 0)
        {
            Px.Text(this, Px.Small, new Vector2(r.Position.X + 4, base_ - 1), "?", 8, Px.InkDim);
            return;
        }
        bool lost = f != null && f.Played && CupState.Winner(f) != club;
        var ink = club == You ? Px.Gold : lost ? Px.InkDim : Px.Ink;
        float bs = Mathf.Min(13, r.Size.Y - 2);
        LeagueArt.Badge(this, new Rect2(r.Position.X + 1, r.Position.Y + (r.Size.Y - bs * 1.2f) / 2, bs, bs * 1.2f), C.Crest(club));
        int fs = r.Size.Y >= 15 ? 16 : 14;
        string score = f != null && f.Played ? f.GoalsFor(club).ToString() : "";
        string pens = f != null && f.Hp >= 0 ? $"({(home ? f.Hp : f.Ap)})" : "";
        float sw = Px.Width(Px.Big, score, fs) + (pens != "" ? Px.Width(Px.Small, pens, 8) + 4 : 0);
        Px.Text(this, Px.Big, new Vector2(r.Position.X + bs + 6, base_), Px.Fit(Px.Big, C.Name(club), fs, r.Size.X - bs - 12 - sw), fs, ink);
        if (score != "") Px.TextR(this, Px.Big, r.End.X - 2, base_, score, fs, ink);
        if (pens != "") Px.TextR(this, Px.Small, r.End.X - 4 - Px.Width(Px.Big, score, fs), base_ - 1, pens, 8, Px.InkDim);
    }

    // ---------------------------------------------------------------- your next tie

    void NextCard(Rect2 r, Fixture f)
    {
        bool final = C.Final(f);
        bool home = f.Home == You;
        Px.Frame(this, r, Colors.Transparent, Px.Hex(0x8ff0b0), Px.Shadow);
        var inner = r.Grow(-3);
        Px.Bands(this, inner, new[] { Px.Hex(0x23804a), Px.Hex(0x1d7041), Px.Hex(0x186137), Px.Hex(0x13502e), Px.Hex(0x0e3f25) }, new[] { 0, 0.2f, 0.45f, 0.7f, 0.88f });
        for (float x = inner.Position.X; x < inner.End.X; x += 44)
            DrawRect(new Rect2(x, inner.Position.Y, Mathf.Min(22, inner.End.X - x), inner.Size.Y), new Color(1, 1, 1, 0.035f));
        Px.Text(this, Px.Small, inner.Position + new Vector2(12, 20), $"NEXT · {C.RoundName(f.Round)}", 8, Px.Hex(0xffe0a8));
        string where = final ? "FINAL" : home ? "HOME" : "AWAY";
        var tag = new Rect2(inner.End.X - 70, inner.Position.Y + 7, 60, 20);
        Px.Frame(this, tag, final ? Px.Hex(0xff8fd0) : home ? Px.Gold : Px.Cyan, Px.Dark, null, 2, 3);
        Px.TextC(this, Px.Big, tag.GetCenter().X, tag.GetCenter().Y + 6, where, 18, Px.Dark);

        float colW = (inner.Size.X - 50) / 2, cy = inner.Position.Y + 34;
        Side(new Rect2(inner.Position.X + 6, cy, colW, 120), f.Home);
        Side(new Rect2(inner.End.X - 6 - colW, cy, colW, 120), f.Away);
        var vs = new Rect2(inner.GetCenter().X - 20, cy + 22, 40, 26);
        Px.Frame(this, vs, Px.Hex(0x0e2a18), Px.Hex(0xffe066), new Color(0, 0, 0, 0.4f), 3, 4);
        Px.TextC(this, Px.Big, vs.GetCenter().X, vs.GetCenter().Y + 7, "VS", 20, Px.Hex(0xffe066));

        Px.TextC(this, Px.Small, inner.GetCenter().X, cy + 136, $"THE WINNERS GET {Px.Thousands(C.TopPrize)} · EVERY ROUND PAYS", 8, Px.Hex(0xffe066));
        string venue = final ? "A NEUTRAL GROUND · LEVEL GOES TO PENALTIES" : $"AT {C.GroundName(f.Home).ToUpperInvariant()} · LEVEL GOES TO PENALTIES";
        Px.TextC(this, Px.Small, inner.GetCenter().X, inner.End.Y - 66, Px.Fit(Px.Small, venue, 8, inner.Size.X - 16), 8, Px.Hex(0xe8ffe8, 0.85f));
        float by = inner.End.Y - 54, bw = Mathf.Round(inner.Size.X * 0.56f);
        GoldButton("play", new Rect2(inner.Position.X + 10, by, bw, 44), "PLAY  >", 28, () => Kickoff(f));
        GhostButton("sim", new Rect2(inner.Position.X + 20 + bw, by, inner.Size.X - bw - 30, 44), "SIMULATE", 20,
            () => _ui.Open(new SimulateChoice(_ui, C.Name(f.Other(You)), () => Kickoff(f, true), Instant)));
    }

    void Side(Rect2 r, int club)
    {
        float cx = r.GetCenter().X;
        LeagueArt.Badge(this, new Rect2(cx - 26, r.Position.Y, 52, 64), C.Crest(club));
        Px.TextC(this, Px.Big, cx, r.Position.Y + 86, Px.Fit(Px.Big, C.Name(club), 20, r.Size.X), 20, club == You ? Px.Gold : Px.Ink, new Color(0, 0, 0, 0.5f));
        Px.TextC(this, Px.Small, cx, r.Position.Y + 102, $"{C.Rating(club)} OVR", 8, Px.Hex(0xffe0a8));
    }

    void EndCard(Rect2 r)
    {
        bool won = C.Won;
        Px.Frame(this, r, Px.Glass, won ? Px.Gold : Px.Line2, Px.Shadow);
        var inner = r.Grow(-3);
        float cx = inner.GetCenter().X;
        if (won)
        {
            float bob = Mathf.Floor((float)(T * 2 % 4)) switch { 1 => -2, 2 => -4, 3 => -2, _ => 0 };
            LeagueArt.Trophy(this, new Rect2(cx - 40, inner.Position.Y + 14 + bob, 80, 104), (float)(T * 0.4 % 1.6));
        }
        else CrestArt.Draw(this, new Rect2(cx - 36, inner.Position.Y + 16, 72, 90), Club.S.Crest);
        float y = inner.Position.Y + 150;
        Px.TextC(this, Px.Big, cx, y, won ? "CUP WINNERS" : "KNOCKED OUT", 38, won ? Px.Gold : Px.Ink, new Color(0, 0, 0, 0.5f), 3);
        y += 20;
        var champ = C.RoundOf(C.Rounds - 1).Select(CupState.Winner).FirstOrDefault();
        string line = won ? $"{C.Def.Name.ToUpperInvariant()} IS YOURS" : $"OUT IN THE {C.RoundName(C.S.Out)} · WON BY {C.Name(champ).ToUpperInvariant()}";
        Px.TextC(this, Px.Small, cx, y, Px.Fit(Px.Small, line, 8, inner.Size.X - 16), 8, Px.InkDim);
        y += 18;
        Px.TextC(this, Px.Small, cx, y, C.S.PrizePaid ? $"PRIZE MONEY PAID: {Px.Thousands(C.PrizeNow)}" : $"PRIZE MONEY: {Px.Thousands(C.PrizeNow)}", 8, Px.Hex(0xffe066));
        var b = new Rect2(inner.Position.X + 10, inner.End.Y - 54, inner.Size.X - 20, 44);
        if (!C.S.PrizePaid)
            GoldButton("collect", b, "COLLECT PRIZE  >", 24, () => Club.Earn(C.PayPrize()));
        else
            GoldButton("map", b, "BACK TO THE MAP  >", 24, () => _ui.Go(_ui.Map));
    }

    // ---------------------------------------------------------------- playing a tie

    void Instant()
    {
        int round = C.S.Round;
        var f = C.Next;
        C.Simulate();
        Reward(f, round);
    }

    public void Kickoff(Fixture f, bool watch = false)
    {
        bool final = C.Final(f);
        bool home = f.Home == You || final;
        string ground = final ? "big"
            : home ? (Menus.Grounds.All.Any(g => g.Id == Club.S.Ground) && Club.S.Ground != "training" ? Club.S.Ground : "big")
            : "custom";
        var req = new MatchRequest { Setup = C.Setup(f), Seed = (int)(ClubState.Now & 0xffff) + 1, Ground = ground, Watch = watch };
        if (!home)
        {
            req.HostCrest = C.Crest(f.Home);
            req.HostPlan = C.PlanOf(f.Home);
            req.HostGoalFx = Stadia.GoalFxOf(C.S.Clubs[f.Home]);
        }
        int round = f.Round;
        _ui.App.PlayFixture(req, o => Played(round, o, watch));
    }

    void Played(int round, MatchOutcome o, bool watch)
    {
        _ui.Go(this);
        var f = C.Next;
        if (f == null || f.Round != round) return;
        bool home = f.Home == You;
        if (!o.Finished && watch)
        {
            Instant();
            return;
        }
        if (!o.Finished)
        {
            C.Complete(home ? 0 : 3, home ? 3 : 0, null, true);
            Club.RecordForfeit();
            _ui.Open(new CupDay(_ui, f, 0));
            return;
        }
        int opp = f.Other(You);
        var goals = (o.Goals ?? new()).Select(g => new GoalNote { Club = g.Team == 0 ? You : opp, Player = g.Name is { Length: > 0 } n ? n : C.Scorer(f, g.Team, g.Index), Minute = Math.Max(1, g.Minute) }).ToList();
        C.Complete(home ? o.Home : o.Away, home ? o.Away : o.Home, goals);
        Reward(f, round);
    }

    void Reward(Fixture f, int round)
    {
        var (coins, res) = Club.RecordResult(f.GoalsFor(You), f.GoalsAgainst(You));
        int bonus = res == 'W' ? C.Bonus.win : res == 'D' ? C.Bonus.draw : 0;
        Club.Earn(bonus);
        _ui.Open(new CupDay(_ui, f, coins + bonus));
    }

    /// <summary>Debug: `--screen=cup[@id]` enters a cup (your home country's by default) and opens it.</summary>
    public static void Debug(Menus.Menus ui, string arg)
    {
        if (!arg.StartsWith("--screen=cup")) return;
        if (!ui.Season.HasCareer) ui.Season.Begin("ENG");
        var d = Ladder.CupById(arg.Contains('@') ? arg[(arg.IndexOf('@') + 1)..].Replace(":sim", "") : "") ?? Ladder.Cups.First(c => c.Country == ui.Season.C.Country);
        if (!ui.CupRun.Running(d)) ui.CupRun.Enter(d);
        // `--screen=cup@id:sim` plays every one of your ties on the numbers.
        if (arg.EndsWith(":sim"))
            while (ui.CupRun.Next != null) ui.CupRun.Simulate();
        ui.Go(ui.CupHub);
    }
}

/// <summary>After your tie: the score (and the shoot-out), the coins, the rest of the round and
/// where it leaves you: through, out, or holding the cup with the prize money counting up.</summary>
public sealed partial class CupDay : Sheet
{
    readonly Fixture _f;
    readonly int _coins, _prize;
    readonly bool _paidNow;
    double _nextRain;
    CupState C => Ui.CupRun;

    public CupDay(Menus.Menus ui, Fixture f, int coins) : base(ui)
    {
        _f = f;
        _coins = coins;
        int paid = C.PayPrize();
        _paidNow = paid > 0;
        _prize = C.PrizeNow;
        if (_paidNow) Ui.Club.Earn(paid);
    }

    protected override void Draw()
    {
        float W = Size.X, H = Size.Y;
        bool through = CupState.Winner(_f) == You, cup = through && C.Won;
        Px.Bands(this, new Rect2(Vector2.Zero, Size), cup
            ? new[] { Px.Hex(0x1a1004), Px.Hex(0x2a1a06), Px.Hex(0x3a2408), Px.Hex(0x2a1a06) }
            : new[] { Px.Hex(0x0a1a1a), Px.Hex(0x0c2220), Px.Hex(0x0f2a26), Px.Hex(0x0c2220) }, new[] { 0, 0.3f, 0.6f, 0.85f });
        Px.Scanlines(this, new Rect2(Vector2.Zero, Size));
        var tc = new Vector2(W * 0.2f, H * 0.48f);
        float th = H * 0.5f;
        if (cup)
        {
            Fx.Beams(this, tc, Px.Hex(0xffe9a0), 12, H * 0.8f, 0.07f, (float)T * 0.2f, 0.06f);
            if (T > _nextRain)
            {
                _nextRain = T + 0.9;
                Sparks.Rain(Size, Confetti, 40);
            }
            if (T < 0.05) Audio.GameAudio.Instance?.PackBurst(3);
            float bob = Mathf.Floor((float)(T * 2 % 4)) switch { 1 => -2, 2 => -4, 3 => -2, _ => 0 };
            LeagueArt.Trophy(this, new Rect2(tc.X - th * 0.385f, tc.Y - th * 0.5f + bob, th * 0.77f, th), (float)(T * 0.5 % 1.6));
        }
        else
        {
            Fx.Spot(this, new Vector2(tc.X, 0), new Vector2(tc.X, tc.Y + th * 0.42f), 18, th * 0.9f, Px.Hex(0xc9d8ff), 0.07f);
            float cw = th * 0.62f;
            CrestArt.Draw(this, new Rect2(tc.X - cw / 2, tc.Y - th * 0.46f, cw, cw * 1.24f), Ui.Club.S.Crest);
        }

        float x = W * 0.38f, right = W - 20;
        Px.Text(this, Px.Small, new Vector2(x, 40), $"{C.Def.Name.ToUpperInvariant()} · {C.RoundName(_f.Round)}", 9, Px.Cyan);
        string head = cup ? "CUP WINNERS!" : through ? "THROUGH!" : "KNOCKED OUT";
        Px.Text(this, Px.Big, new Vector2(x, 92), head, 56, cup || through ? Px.Gold : Px.Ink, new Color(0, 0, 0, 0.6f), 4);
        // The score.
        float y = 112;
        LeagueArt.Badge(this, new Rect2(x, y, 22, 27), C.Crest(_f.Home));
        string score = $"{C.Name(_f.Home)}  {_f.Hg}-{_f.Ag}  {C.Name(_f.Away)}";
        Px.Text(this, Px.Big, new Vector2(x + 30, y + 21), Px.Fit(Px.Big, score, 22, right - x - 64), 22, Px.Ink);
        LeagueArt.Badge(this, new Rect2(Mathf.Min(right - 22, x + 36 + Px.Width(Px.Big, Px.Fit(Px.Big, score, 22, right - x - 64), 22)), y, 22, 27), C.Crest(_f.Away));
        y += 40;
        if (_f.Hp >= 0)
        {
            Px.Text(this, Px.Small, new Vector2(x, y), $"LEVEL AFTER 90 MINUTES · {(through ? "WON" : "LOST")} {Math.Max(_f.Hp, _f.Ap)}-{Math.Min(_f.Hp, _f.Ap)} ON PENALTIES", 8, through ? Px.Win : Px.Loss);
            y += 16;
        }
        else if (_f.Forfeit)
        {
            Px.Text(this, Px.Small, new Vector2(x, y), "LEFT BEFORE THE END · BOOKED AS A 3-0 DEFEAT", 8, Px.Loss);
            y += 16;
        }
        if (_coins > 0)
        {
            Px.Coin(this, new Vector2(x, y - 2), 14);
            Px.Text(this, Px.Big, new Vector2(x + 20, y + 11), "+" + Px.Thousands(_coins), 20, Px.Hex(0xffe066), Colors.Black);
            y += 22;
        }
        // The rest of the round.
        var rest = C.RoundOf(_f.Round).Where(t => t != _f).ToList();
        if (rest.Count > 0)
        {
            y += 8;
            Px.Text(this, Px.Small, new Vector2(x, y), "THE REST OF THE ROUND", 8, Px.InkDim);
            y += 6;
            float colW = (right - x - 12) / 2;
            for (int i = 0; i < rest.Count; i++)
            {
                var t = rest[i];
                float lx = x + (i % 2) * (colW + 12), ly = y + 16 + (i / 2) * 18;
                string s = $"{C.Short(t.Home)} {t.Hg}-{t.Ag} {C.Short(t.Away)}" + (t.Hp >= 0 ? $" ({t.Hp}-{t.Ap} P)" : "");
                Px.Text(this, Px.Big, new Vector2(lx, ly), Px.Fit(Px.Big, s, 18, colW), 18, Px.Ink);
            }
            y += 16 + (rest.Count + 1) / 2 * 18;
        }
        // Where it leaves you.
        string next = cup ? "" : through ? $"NEXT: THE {C.RoundName(C.S.Round)}" : "";
        if (next != "") Px.Text(this, Px.Small, new Vector2(x, H - 74), next, 9, Px.Gold);
        if (C.Over)
        {
            float k = Mathf.Clamp(((float)T - 0.6f) / 1.4f, 0, 1);
            int shown = (int)Mathf.Round(_prize * (1 - Mathf.Pow(1 - k, 3)));
            Px.Text(this, Px.Small, new Vector2(x, H - 92), _paidNow ? "PRIZE MONEY" : "PRIZE MONEY (PAID)", 8, Px.InkDim);
            Px.Coin(this, new Vector2(x, H - 82), 20);
            Px.Text(this, Px.Big, new Vector2(x + 28, H - 62), "+" + Px.Thousands(_paidNow ? shown : _prize), 34, Px.Hex(0xffe066), Colors.Black);
        }
        GoldButton("go", new Rect2(W - 16 - 220, H - 60, 220, 46), "CONTINUE  >", 26, () => Ui.Close(this));
    }
}
