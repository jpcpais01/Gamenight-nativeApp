using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GameNight.Club;
using GameNight.Menus;
using GameNight.Sim;

namespace GameNight.League;

/// <summary>
/// The league hub: the next match and where you stand, the full table, every matchday's
/// fixtures and results, the golden boot and records, and the other fifteen clubs. It also runs
/// a matchday: the preview, your match (or the simulation), then the scores coming in from
/// around the grounds, the morning paper and, at the end, the season finale.
/// </summary>
public sealed partial class LeagueScreen : PxCanvas
{
    readonly Menus.Menus _ui;
    LeagueState L => _ui.Season;
    ClubState Club => _ui.Club;
    const int You = LeagueState.You;

    int _tab;
    int _round = -1;
    static readonly string[] Tabs = { "OVERVIEW", "TABLE", "FIXTURES", "STATS", "CLUBS" };

    public LeagueScreen(Menus.Menus ui)
    {
        _ui = ui;
    }

    public void Opened() => _round = -1;

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        NightBackdrop(new[] { Px.Hex(0x0a1426), Px.Hex(0x0c1a30), Px.Hex(0x0f2138), Px.Hex(0x122842) });
        // The pitch markings, faint, behind everything.
        var line = new Color(1, 1, 1, 0.035f);
        DrawArc(new Vector2(W / 2, H * 0.62f), H * 0.32f, 0, Mathf.Tau, 48, line, 3);
        DrawRect(new Rect2(W / 2 - 1.5f, 0, 3, H), line);
        Px.Scanlines(this, new Rect2(0, 0, W, H));

        BackButton(new Vector2(14, 12), () => _ui.Go(_ui.Map));
        Title(new Vector2(66, 44), "LEAGUE");
        Coins(W - 16, 14, Club.S.Coins);
        if (!L.Active)
        {
            Intro(W, H);
            return;
        }
        float sx = 66 + Px.Width(Px.Big, "LEAGUE", 38) + 18;
        Px.Text(this, Px.Small, new Vector2(sx, 26), L.S.Name.ToUpperInvariant(), 8, Px.Cyan);
        string md = L.SeasonOver ? $"SEASON {L.S.Season} · COMPLETE" : $"SEASON {L.S.Season} · MATCHDAY {L.S.Round + 1} OF {LeagueState.Rounds}";
        Px.Text(this, Px.Big, new Vector2(sx, 45), md, 20, Px.Ink, new Color(0, 0, 0, 0.5f));

        float tx = 14;
        for (int i = 0; i < Tabs.Length; i++)
        {
            int k = i;
            tx += Chip("tab" + i, new Vector2(tx, 58), Tabs[i], _tab == i, () => _tab = k) + 8;
        }
        var c = new Rect2(14, 96, W - 28, H - 96 - 12);
        switch (_tab)
        {
            case 0: Overview(c); break;
            case 1: TableTab(c); break;
            case 2: FixturesTab(c); break;
            case 3: StatsTab(c); break;
            default: ClubsTab(c); break;
        }
    }

    // ---------------------------------------------------------------- no league yet

    void Intro(float W, float H)
    {
        var card = new Rect2(14, 64, W - 28, H - 78);
        Px.Frame(this, card, Px.Glass, Px.Line2, Px.Shadow);
        // The trophy under a spotlight.
        var tr = new Rect2(card.Position.X + 40, card.Position.Y + 34, 130, 169);
        Fx.Spot(this, new Vector2(tr.GetCenter().X, card.Position.Y + 3), new Vector2(tr.GetCenter().X, tr.End.Y), 20, 220, Px.Hex(0xfff2c0), 0.1f);
        float bob = Mathf.Floor((float)(T * 2 % 4)) switch { 1 => -2, 2 => -4, 3 => -2, _ => 0 };
        LeagueArt.Trophy(this, tr.Translated(new Vector2(0, bob)), (float)(T * 0.4 % 1.6));
        DrawColoredPolygon(Px.Ellipse(new Vector2(tr.GetCenter().X, tr.End.Y + 14), 80, 10, 20), new Color(0, 0, 0, 0.35f));

        float x = tr.End.X + 50, y = card.Position.Y + 60;
        Px.Text(this, Px.Small, new Vector2(x, y - 22), "A SEASON OF YOUR OWN", 9, Px.Cyan);
        Px.Text(this, Px.Big, new Vector2(x, y + 20), "START A LEAGUE", 48, Px.Gold, new Color(0, 0, 0, 0.55f), 3);
        y += 50;
        foreach (var s in new[]
        {
            "16 CLUBS · 30 MATCHDAYS · HOME AND AWAY",
            "15 RIVALS MADE FOR YOU: NAMES, CRESTS, KITS, SQUADS",
            "PLAY YOUR MATCHES · THE REST ARE SETTLED ON FORM",
            "SCORES FROM AROUND THE GROUNDS, THE PAPER EVERY MORNING",
            "PRIZE MONEY, THE TROPHY, PROMOTION AND RELEGATION",
        })
        {
            DrawRect(new Rect2(x, y - 7, 5, 5), Px.Gold);
            Px.Text(this, Px.Small, new Vector2(x + 14, y), s, 8, Px.Ink);
            y += 20;
        }
        GoldButton("start", new Rect2(x, card.End.Y - 70, 280, 50), "PICK A LEAGUE  >", 30, () => _ui.Go(_ui.Map));
    }

    // ---------------------------------------------------------------- overview

    void Overview(Rect2 c)
    {
        float lw = Mathf.Round(c.Size.X * 0.54f);
        var card = new Rect2(c.Position, new Vector2(lw, c.Size.Y));
        if (L.Next is { } next) NextCard(card, next);
        else DoneCard(card);
        Standing(new Rect2(c.Position.X + lw + 16, c.Position.Y, c.Size.X - lw - 16, c.Size.Y));
    }

    /// <summary>The pitch-green panel: bands and mown stripes.</summary>
    void Turf(Rect2 r, Color ring)
    {
        Px.Frame(this, r, Colors.Transparent, ring, Px.Shadow);
        var inner = r.Grow(-3);
        Px.Bands(this, inner, new[] { Px.Hex(0x23804a), Px.Hex(0x1d7041), Px.Hex(0x186137), Px.Hex(0x13502e), Px.Hex(0x0e3f25) }, new[] { 0, 0.2f, 0.45f, 0.7f, 0.88f });
        for (float x = inner.Position.X; x < inner.End.X; x += 44)
            DrawRect(new Rect2(x, inner.Position.Y, Mathf.Min(22, inner.End.X - x), inner.Size.Y), new Color(1, 1, 1, 0.035f));
        DrawArc(new Vector2(inner.GetCenter().X, inner.Position.Y + inner.Size.Y * 0.36f), inner.Size.Y * 0.22f, 0, Mathf.Tau, 40, new Color(1, 1, 1, 0.08f), 2);
    }

    void NextCard(Rect2 r, Fixture f)
    {
        Turf(r, Px.Hex(0x8ff0b0));
        var inner = r.Grow(-3);
        bool home = f.Home == You;
        int opp = f.Other(You);
        var date = L.Date(f.Round).ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant();
        Px.Text(this, Px.Small, inner.Position + new Vector2(14, 22), $"NEXT · MATCHDAY {f.Round + 1} · {date}", 8, Px.Hex(0xffe0a8));
        var tag = new Rect2(inner.End.X - 74, inner.Position.Y + 9, 62, 20);
        Px.Frame(this, tag, home ? Px.Gold : Px.Cyan, home ? Px.Hex(0xb37400) : Px.Hex(0x1a8a96), null, 2, 3);
        Px.TextC(this, Px.Big, tag.GetCenter().X, tag.GetCenter().Y + 6, home ? "HOME" : "AWAY", 18, Px.Dark);

        var table = L.Table();
        float colW = (inner.Size.X - 60) / 2;
        float cy = inner.Position.Y + 38;
        int left = f.Home, right = f.Away;
        Side(new Rect2(inner.Position.X + 10, cy, colW, 160), left, table);
        Side(new Rect2(inner.End.X - 10 - colW, cy, colW, 160), right, table);
        var vs = new Rect2(inner.GetCenter().X - 22, cy + 30, 44, 28);
        Px.Frame(this, vs, Px.Hex(0x0e2a18), Px.Hex(0xffe066), new Color(0, 0, 0, 0.4f), 3, 4);
        Px.TextC(this, Px.Big, vs.GetCenter().X, vs.GetCenter().Y + 7, "VS", 22, Px.Hex(0xffe066));

        var first = L.S.Fixtures.FirstOrDefault(x => x.Played && x.Has(opp) && x.Has(You) && x.Round < f.Round);
        string venue = $"AT {L.GroundName(f.Home).ToUpperInvariant()}" + (first != null ? $" · FIRST LEG {first.Hg}-{first.Ag}" : "");
        Px.TextC(this, Px.Small, inner.GetCenter().X, inner.End.Y - 70, Px.Fit(Px.Small, venue, 8, inner.Size.X - 20), 8, Px.Hex(0xe8ffe8, 0.85f));

        float by = inner.End.Y - 56;
        float bw = Mathf.Round(inner.Size.X * 0.58f);
        GoldButton("play", new Rect2(inner.Position.X + 12, by, bw, 44), "PLAY MATCHDAY  >", 28, () => _ui.Open(new MatchPreview(_ui, f)));
        GhostButton("sim", new Rect2(inner.Position.X + 24 + bw, by, inner.Size.X - bw - 36, 44), "SIMULATE", 22, () => AskSimulate(f));
    }

    /// <summary>A side of the next-match card: badge, name, place, rating, form.</summary>
    void Side(Rect2 r, int club, List<Row> table)
    {
        float cx = r.GetCenter().X;
        LeagueArt.Badge(this, new Rect2(cx - 32, r.Position.Y, 64, 79), L.Crest(club));
        string n = Px.Fit(Px.Big, L.Name(club), 22, r.Size.X);
        Px.TextC(this, Px.Big, cx, r.Position.Y + 102, n, 22, club == You ? Px.Gold : Px.Ink, new Color(0, 0, 0, 0.5f));
        int pos = L.Place(club, table);
        Px.TextC(this, Px.Small, cx, r.Position.Y + 118, $"{(L.S.Round > 0 ? LeagueState.Ordinal(pos) + " · " : "")}{L.Rating(club)} OVR", 8, Px.Hex(0xffe0a8));
        string form = L.Form(club);
        float fw = 5 * 12 - 2;
        LeagueArt.Form(this, new Vector2(cx - fw / 2, r.Position.Y + 126), form, 10, false);
    }

    void DoneCard(Rect2 r)
    {
        Px.Frame(this, r, Px.Glass, Px.Gold, Px.Shadow);
        var inner = r.Grow(-3);
        int pos = L.Place(You);
        if (pos == 1) LeagueArt.Trophy(this, new Rect2(inner.Position.X + 22, inner.Position.Y + 40, 100, 130), (float)(T * 0.4 % 1.6));
        else CrestArt.Draw(this, new Rect2(inner.Position.X + 26, inner.Position.Y + 44, 92, 114), Club.S.Crest);
        float x = inner.Position.X + 150;
        Px.Text(this, Px.Small, new Vector2(x, inner.Position.Y + 40), $"SEASON {L.S.Season} IS OVER", 9, Px.Cyan);
        Px.Text(this, Px.Big, new Vector2(x, inner.Position.Y + 96), pos == 1 ? "CHAMPIONS" : $"{LeagueState.Ordinal(pos)} PLACE", 50, pos == 1 ? Px.Gold : Px.Ink, new Color(0, 0, 0, 0.5f), 3);
        var t = L.Table();
        Px.Text(this, Px.Small, new Vector2(x, inner.Position.Y + 122), $"CHAMPIONS: {L.Name(t[0].Club).ToUpperInvariant()} · {t[0].Pts} PTS", 8, Px.InkDim);
        GoldButton("finale", new Rect2(inner.Position.X + 16, inner.End.Y - 58, inner.Size.X - 32, 44),
            L.S.PrizePaid ? "START NEXT SEASON  >" : "SEASON FINALE  >", 28, () => _ui.Open(new SeasonFinale(_ui)));
    }

    void Standing(Rect2 r)
    {
        var table = L.Table();
        int pos = L.Place(You, table);
        var me = table[pos - 1];
        int before = L.S.Round > 0 ? L.Place(You, L.Table(L.S.Round - 1)) : pos;
        var top = new Rect2(r.Position, new Vector2(r.Size.X, 84));
        Px.Frame(this, top, Px.Glass, Px.Line2, Px.ShadowSoft);
        string ps = L.S.Round == 0 ? "-" : LeagueState.Ordinal(pos);
        var zc = LeagueArt.Zone(pos) ?? Px.Ink;
        Px.Text(this, Px.Big, top.Position + new Vector2(16, 64), ps, 60, L.S.Round == 0 ? Px.InkDim : zc, new Color(0, 0, 0, 0.55f), 3);
        float x = top.Position.X + 30 + Px.Width(Px.Big, ps, 60);
        Px.Text(this, Px.Big, new Vector2(x, top.Position.Y + 34), $"{me.Pts} PTS", 26, Px.Ink);
        Px.Text(this, Px.Small, new Vector2(x, top.Position.Y + 52), $"W{me.W} D{me.D} L{me.L} · GD {(me.Gd > 0 ? "+" : "")}{me.Gd}", 8, Px.InkDim);
        int gap = table[0].Pts - me.Pts;
        string g = L.S.Round == 0 ? "THE SEASON STARTS HERE" : pos == 1 ? (table.Count > 1 ? $"{me.Pts - table[1].Pts} PTS CLEAR AT THE TOP" : "TOP") : $"{gap} PTS OFF THE TOP";
        Px.Text(this, Px.Small, new Vector2(x, top.Position.Y + 68), g, 8, Px.Cyan);
        if (before != pos)
        {
            bool up = pos < before;
            var col = up ? Px.Win : Px.Loss;
            var ac = new Vector2(top.End.X - 30, top.Position.Y + 30);
            for (int i = 0; i < 4; i++)
                DrawRect(new Rect2(ac.X - i * 3, ac.Y + (up ? i * 3 : -i * 3), 3 + i * 6, 3), col);
            Px.TextC(this, Px.Big, ac.X + 1.5f, ac.Y + (up ? 32 : 24), Math.Abs(before - pos).ToString(), 20, col);
        }

        // A slice of the table around you.
        var rows = new List<int>();
        if (pos <= 4) rows.AddRange(Enumerable.Range(1, 6));
        else
        {
            rows.Add(1);
            rows.Add(2);
            int from = Math.Min(pos - 2, LeagueState.Clubs - 3);
            for (int k = from; k < from + 4; k++) if (!rows.Contains(k)) rows.Add(k);
        }
        float y = top.End.Y + 12;
        var box = new Rect2(r.Position.X, y, r.Size.X, 18 + rows.Count * 19 + 8);
        Px.Frame(this, box, Px.Glass, Px.Line, Px.ShadowSoft);
        Px.Text(this, Px.Small, new Vector2(box.Position.X + 10, y + 16), "TABLE", 8, Px.Cyan);
        Px.TextR(this, Px.Small, box.End.X - 52, y + 16, "GD", 8, Px.InkDim);
        Px.TextR(this, Px.Small, box.End.X - 12, y + 16, "PTS", 8, Px.InkDim);
        y += 22;
        int prev = 0;
        foreach (int p in rows)
        {
            if (prev > 0 && p != prev + 1) DrawRect(new Rect2(box.Position.X + 12, y - 2, box.Size.X - 24, 1), Px.Line);
            prev = p;
            MiniRow(new Rect2(box.Position.X + 6, y, box.Size.X - 12, 18), table[p - 1], p);
            y += 19;
        }
        if (L.S.Round > 0)
        {
            int last = L.S.Round - 1;
            GhostButton("paper", new Rect2(r.Position.X, r.End.Y - 40, r.Size.X, 40), "READ THE PAPER", 22, () => _ui.Open(new Newspaper(_ui, last, null)), Px.Hex(0xf0e2c0));
        }
    }

    void MiniRow(Rect2 r, Row row, int pos)
    {
        bool me = row.Club == You;
        if (me) DrawRect(r, new Color(1, 0.83f, 0.28f, 0.16f));
        if (LeagueArt.Zone(pos) is Color z) DrawRect(new Rect2(r.Position.X, r.Position.Y + 2, 3, r.Size.Y - 4), z);
        Px.TextR(this, Px.Big, r.Position.X + 26, r.Position.Y + 15, pos.ToString(), 18, Px.InkDim);
        LeagueArt.Badge(this, new Rect2(r.Position.X + 32, r.Position.Y + 1, 13, 16), L.Crest(row.Club));
        Px.Text(this, Px.Big, new Vector2(r.Position.X + 52, r.Position.Y + 15), Px.Fit(Px.Big, L.Name(row.Club), 18, r.Size.X - 140), 18, me ? Px.Gold : Px.Ink);
        Px.TextR(this, Px.Big, r.End.X - 46, r.Position.Y + 15, (row.Gd > 0 ? "+" : "") + row.Gd, 18, Px.InkDim);
        Px.TextR(this, Px.Big, r.End.X - 6, r.Position.Y + 15, row.Pts.ToString(), 18, me ? Px.Gold : Px.Ink);
    }

    // ---------------------------------------------------------------- table

    void TableTab(Rect2 c)
    {
        var table = L.Table();
        float rh = Mathf.Floor((c.Size.Y - 22) / LeagueState.Clubs);
        Px.Frame(this, c, Px.Glass, Px.Line, Px.ShadowSoft);
        float x0 = c.Position.X + 8, x1 = c.End.X - 8;
        // Columns from the right: form, points, GD, GA, GF, L, D, W, P.
        float fx = x1 - 58;
        string[] heads = { "P", "W", "D", "L", "GF", "GA", "GD", "PTS" };
        float[] col = new float[8];
        float cx = fx - 14;
        for (int i = 7; i >= 0; i--)
        {
            col[i] = cx;
            cx -= i == 7 ? 46 : i >= 4 ? 38 : 30;
        }
        float hy = c.Position.Y + 16;
        Px.Text(this, Px.Small, new Vector2(x0 + 4, hy), "POS", 8, Px.InkDim);
        Px.Text(this, Px.Small, new Vector2(x0 + 58, hy), "CLUB", 8, Px.InkDim);
        for (int i = 0; i < 8; i++) Px.TextR(this, Px.Small, col[i], hy, heads[i], 8, i == 7 ? Px.Gold : Px.InkDim);
        Px.Text(this, Px.Small, new Vector2(fx, hy), "FORM", 8, Px.InkDim);
        float y = c.Position.Y + 22;
        for (int p = 1; p <= table.Count; p++)
        {
            var row = table[p - 1];
            bool me = row.Club == You;
            var rr = new Rect2(x0, y, x1 - x0, rh - 1);
            if (me) DrawRect(rr, new Color(1, 0.83f, 0.28f, 0.17f));
            else if (p % 2 == 0) DrawRect(rr, new Color(1, 1, 1, 0.03f));
            if (LeagueArt.Zone(p) is Color z) DrawRect(new Rect2(x0, y + 1, 3, rh - 3), z);
            float base_ = y + rh * 0.5f + 6;
            Px.TextR(this, Px.Big, x0 + 26, base_, p.ToString(), 18, Px.InkDim);
            LeagueArt.Badge(this, new Rect2(x0 + 34, y + (rh - 16) / 2, 13, 16), L.Crest(row.Club));
            Px.Text(this, Px.Big, new Vector2(x0 + 56, base_), Px.Fit(Px.Big, L.Name(row.Club), 18, col[0] - 30 - x0 - 56), 18, me ? Px.Gold : Px.Ink);
            if (row.Club != You && L.S.Clubs[row.Club].Promoted) Px.Text(this, Px.Small, new Vector2(x0 + 62 + Mathf.Min(Px.Width(Px.Big, L.Name(row.Club), 18), col[0] - 120 - x0), base_ - 2), "NEW", 8, Px.Win);
            int[] v = { row.P, row.W, row.D, row.L, row.Gf, row.Ga, row.Gd, row.Pts };
            for (int i = 0; i < 8; i++)
                Px.TextR(this, Px.Big, col[i], base_, i == 6 && v[i] > 0 ? "+" + v[i] : v[i].ToString(), 18, i == 7 ? (me ? Px.Gold : Px.Ink) : Px.InkDim);
            LeagueArt.Form(this, new Vector2(fx, y + (rh - 9) / 2), row.Form.Length > 5 ? row.Form[^5..] : row.Form, 9, false);
            int club = row.Club;
            Tap("row" + p, rr, () => _ui.Open(new ClubSheet(_ui, club)));
            y += rh;
        }
    }

    // ---------------------------------------------------------------- fixtures

    void FixturesTab(Rect2 c)
    {
        if (_round < 0) _round = Math.Min(L.S.Round, LeagueState.Rounds - 1);
        int round = _round;
        var date = L.Date(round).ToString("ddd d MMMM", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant();
        GhostButton("prev", new Rect2(c.Position.X, c.Position.Y, 44, 32), "<", 24, () => _round = Math.Max(0, _round - 1));
        GhostButton("next", new Rect2(c.End.X - 44, c.Position.Y, 44, 32), ">", 24, () => _round = Math.Min(LeagueState.Rounds - 1, _round + 1));
        Px.TextC(this, Px.Big, c.GetCenter().X, c.Position.Y + 24, $"MATCHDAY {round + 1}  ·  {date}", 24, round == L.S.Round ? Px.Gold : Px.Ink, new Color(0, 0, 0, 0.5f));
        var list = L.RoundOf(round).OrderByDescending(f => f.Has(You)).ToList();
        float top = c.Position.Y + 42, gap = 8;
        float bw = (c.Size.X - gap) / 2, bh = (c.End.Y - top - gap * 3) / 4;
        for (int i = 0; i < list.Count; i++)
        {
            var f = list[i];
            var r = new Rect2(c.Position.X + (i % 2) * (bw + gap), top + (i / 2) * (bh + gap), bw, bh);
            FixtureBox(r, f, "fx" + i);
        }
    }

    void FixtureBox(Rect2 r, Fixture f, string key)
    {
        bool mine = f.Has(You);
        bool held = mine && f.Played && Held(key);
        if (held) r = r.Translated(new Vector2(2, 2));
        Px.Frame(this, r, mine ? new Color(0.2f, 0.16f, 0.05f, 0.85f) : Px.Glass, mine ? Px.Gold : Px.Line, held ? null : Px.ShadowSoft, 3, 4);
        float cx = r.GetCenter().X, my = r.Position.Y + 22;
        var sb = new Rect2(cx - 30, r.Position.Y + 6, 60, 26);
        Px.Frame(this, sb, f.Played ? Px.Hex(0x0b0a1e) : new Color(0, 0, 0, 0.25f), Px.Line2, null, 2, 3);
        Px.TextC(this, Px.Big, cx, sb.GetCenter().Y + 8, f.Played ? $"{f.Hg} - {f.Ag}" : "v", 24, f.Played ? Px.Ink : Px.InkDim);
        float nw = r.Size.X / 2 - 60;
        LeagueArt.Badge(this, new Rect2(sb.Position.X - 22, my - 13, 14, 17), L.Crest(f.Home));
        Px.TextR(this, Px.Big, sb.Position.X - 28, my + 6, Px.Fit(Px.Big, L.Name(f.Home), 20, nw - 10), 20, f.Home == You ? Px.Gold : Px.Ink);
        LeagueArt.Badge(this, new Rect2(sb.End.X + 8, my - 13, 14, 17), L.Crest(f.Away));
        Px.Text(this, Px.Big, new Vector2(sb.End.X + 28, my + 6), Px.Fit(Px.Big, L.Name(f.Away), 20, nw - 10), 20, f.Away == You ? Px.Gold : Px.Ink);
        if (f.Played && r.Size.Y >= 44)
        {
            string Who(int club) => string.Join(", ", f.Goals.Where(g => g.Club == club).GroupBy(g => g.Player).Select(g => $"{Gazette.Word(g.Key.Contains(' ') ? g.Key[(g.Key.LastIndexOf(' ') + 1)..] : g.Key)} {string.Join(",", g.Select(x => x.Minute > 90 ? $"90+{x.Minute - 90}'" : x.Minute + "'"))}"));
            float ly = r.End.Y - 9;
            Px.TextR(this, Px.Small, cx - 36, ly, Px.Fit(Px.Small, Who(f.Home), 8, nw), 8, Px.InkDim);
            Px.Text(this, Px.Small, new Vector2(cx + 36, ly), Px.Fit(Px.Small, Who(f.Away), 8, nw), 8, Px.InkDim);
            if (f.Forfeit) Px.TextC(this, Px.Small, cx, ly, "FORFEIT", 8, Px.Loss);
            else if (mine) Px.TextC(this, Px.Small, cx, ly, "PAPER >", 8, Px.Gold);
        }
        if (mine && f.Played)
        {
            int round = f.Round;
            Tap(key, r, () => _ui.Open(new Newspaper(_ui, round, null)));
        }
    }

    // ---------------------------------------------------------------- stats

    void StatsTab(Rect2 c)
    {
        float lw = Mathf.Round(c.Size.X * 0.48f);
        var boot = new Rect2(c.Position, new Vector2(lw, c.Size.Y));
        Px.Frame(this, boot, Px.Glass, Px.Line, Px.ShadowSoft);
        Px.Text(this, Px.Small, boot.Position + new Vector2(12, 18), "GOLDEN BOOT", 8, Px.Gold);
        var scorers = L.Scorers().Take(10).ToList();
        float rh = Mathf.Min(26, (boot.Size.Y - 30) / 10);
        float y = boot.Position.Y + 26;
        if (scorers.Count == 0) Px.TextC(this, Px.Small, boot.GetCenter().X, boot.GetCenter().Y, "NO GOALS YET", 8, Px.InkDim);
        int rank = 0, last = -1;
        for (int i = 0; i < scorers.Count; i++)
        {
            var (club, name, goals) = scorers[i];
            if (goals != last) rank = i + 1;
            last = goals;
            bool me = club == You;
            var rr = new Rect2(boot.Position.X + 6, y, boot.Size.X - 12, rh - 2);
            if (me) DrawRect(rr, new Color(1, 0.83f, 0.28f, 0.14f));
            Px.TextR(this, Px.Big, rr.Position.X + 22, rr.Position.Y + rh * 0.5f + 6, rank.ToString(), 18, i == 0 ? Px.Gold : Px.InkDim);
            LeagueArt.Badge(this, new Rect2(rr.Position.X + 30, rr.Position.Y + (rh - 17) / 2, 14, 17), L.Crest(club));
            Px.Text(this, Px.Big, new Vector2(rr.Position.X + 52, rr.Position.Y + rh * 0.5f + 6), Px.Fit(Px.Big, name, 20, rr.Size.X - 130), 20, me ? Px.Gold : Px.Ink);
            Px.TextR(this, Px.Small, rr.End.X - 40, rr.Position.Y + rh * 0.5f + 4, L.Short(club), 8, Px.InkDim);
            Px.TextR(this, Px.Big, rr.End.X - 6, rr.Position.Y + rh * 0.5f + 7, goals.ToString(), 22, Px.Gold);
            y += rh;
        }

        var rec = new Rect2(c.Position.X + lw + 16, c.Position.Y, c.Size.X - lw - 16, c.Size.Y);
        Px.Frame(this, rec, Px.Glass, Px.Line, Px.ShadowSoft);
        Px.Text(this, Px.Small, rec.Position + new Vector2(12, 18), "RECORDS", 8, Px.Cyan);
        var table = L.Table();
        var played = L.S.Fixtures.Where(f => f.Played && !f.Forfeit).ToList();
        y = rec.Position.Y + 44;
        void Line(string label, string value, int club = -1)
        {
            Px.Text(this, Px.Small, new Vector2(rec.Position.X + 12, y - 4), label, 8, Px.InkDim);
            float vx = rec.Position.X + 150;
            if (club >= 0)
            {
                LeagueArt.Badge(this, new Rect2(vx, y - 16, 13, 16), L.Crest(club));
                vx += 18;
            }
            Px.Text(this, Px.Big, new Vector2(vx, y), Px.Fit(Px.Big, value, 18, rec.End.X - vx - 10), 18, club == You ? Px.Gold : Px.Ink);
            y += 24;
        }
        if (played.Count > 0)
        {
            var att = table.OrderByDescending(x => x.Gf).First();
            var def = table.OrderBy(x => x.Ga).ThenByDescending(x => x.P).First();
            Line("BEST ATTACK", $"{L.Name(att.Club)} · {att.Gf}", att.Club);
            Line("TIGHTEST DEFENCE", $"{L.Name(def.Club)} · {def.Ga}", def.Club);
            var big = played.OrderByDescending(f => Math.Abs(f.Hg - f.Ag)).ThenByDescending(f => f.Hg + f.Ag).First();
            int winner = big.Hg >= big.Ag ? big.Home : big.Away;
            Line("BIGGEST WIN", $"{L.Short(big.Home)} {big.Hg}-{big.Ag} {L.Short(big.Away)}", winner);
            var cs = Enumerable.Range(0, LeagueState.Clubs).Select(k => (k, n: played.Count(f => f.Has(k) && f.GoalsAgainst(k) == 0))).OrderByDescending(x => x.n).First();
            Line("CLEAN SHEETS", $"{L.Name(cs.k)} · {cs.n}", cs.k);
            var mine = L.Scorers().FirstOrDefault(s => s.club == You);
            Line("YOUR TOP SCORER", mine.name != null ? $"{mine.name} · {mine.goals}" : "-", You);
        }
        else
        {
            Px.Text(this, Px.Small, new Vector2(rec.Position.X + 12, y), "RECORDS FILL IN AS THE SEASON GOES", 8, Px.InkDim);
            y += 24;
        }
        // Honours.
        y += 8;
        Px.Text(this, Px.Small, new Vector2(rec.Position.X + 12, y), "HONOURS", 8, Px.Gold);
        Px.TextR(this, Px.Small, rec.End.X - 12, y, $"TITLES HERE: {L.S.Titles} · TROPHIES: {L.Trophies}", 8, L.S.Titles > 0 ? Px.Gold : Px.InkDim);
        y += 20;
        foreach (var h in Enumerable.Reverse(L.S.History).Take(3))
        {
            Px.Text(this, Px.Big, new Vector2(rec.Position.X + 12, y), $"S{h.Season}", 18, Px.InkDim);
            Px.Text(this, Px.Big, new Vector2(rec.Position.X + 46, y), Px.Fit(Px.Big, h.Champion, 18, rec.Size.X - 170), 18, Px.Ink);
            Px.TextR(this, Px.Small, rec.End.X - 12, y - 2, $"YOU {LeagueState.Ordinal(h.UserPos)}", 8, h.UserPos == 1 ? Px.Gold : Px.InkDim);
            y += 20;
        }
        if (L.S.History.Count == 0) Px.Text(this, Px.Small, new Vector2(rec.Position.X + 12, y), "THE FIRST CHAMPIONS ARE STILL TO BE CROWNED", 8, Px.InkDim);
        // Starting over.
        var nl = new Rect2(rec.End.X - 130, rec.End.Y - 34, 120, 26);
        GhostButton("newleague", nl, "NEW LEAGUE", 16, () => _ui.Open(new ConfirmModal(_ui, "New league?",
            "This league, its table and its history are thrown away and a brand new league is drawn. Your club and squad stay as they are.", "Start over", () =>
            {
                L.Abandon();
                L.Start();
                _tab = 0;
                _ui.Open(new LeagueDraw(_ui));
            })), Px.Line2, Px.InkDim);
    }

    // ---------------------------------------------------------------- clubs

    void ClubsTab(Rect2 c)
    {
        var table = L.Table();
        const int cols = 8;
        float gap = 8;
        float cw = (c.Size.X - gap * (cols - 1)) / cols, ch = (c.Size.Y - gap) / 2;
        for (int i = 0; i < LeagueState.Clubs; i++)
        {
            int club = i;
            var r = new Rect2(c.Position.X + (i % cols) * (cw + gap), c.Position.Y + (i / cols) * (ch + gap), cw, ch);
            bool held = Held("club" + i);
            var rr = held ? r.Translated(new Vector2(2, 2)) : r;
            bool me = i == You;
            Px.Frame(this, rr, Px.Glass, me ? Px.Gold : Px.Line, held ? null : Px.ShadowSoft, 3, 4);
            DrawRect(new Rect2(rr.Position + new Vector2(3, 3), new Vector2(rr.Size.X - 6, 4)), Px.Hex(L.Color(i)));
            float bw = Mathf.Min(54, cw - 20);
            LeagueArt.Badge(this, new Rect2(rr.GetCenter().X - bw / 2, rr.Position.Y + 14, bw, bw * 1.24f), L.Crest(i));
            float ny = rr.Position.Y + 22 + bw * 1.24f;
            var lines = Px.Wrap(Px.Big, L.Name(i), 18, cw - 8);
            foreach (var l in lines.Take(2))
            {
                ny += 16;
                Px.TextC(this, Px.Big, rr.GetCenter().X, ny, Px.Fit(Px.Big, l, 18, cw - 6), 18, me ? Px.Gold : Px.Ink);
            }
            Px.TextC(this, Px.Small, rr.GetCenter().X, rr.End.Y - 10, $"{L.Rating(i)} OVR{(L.S.Round > 0 ? " · " + LeagueState.Ordinal(L.Place(i, table)) : "")}", 8, Px.InkDim);
            Tap("club" + i, r, () => _ui.Open(new ClubSheet(_ui, club)));
        }
    }

    // ---------------------------------------------------------------- running a matchday

    void AskSimulate(Fixture f) => _ui.Open(new SimulateChoice(_ui, L.Name(f.Other(You)), () => Kickoff(f, true), () => Instant()));

    /// <summary>Your match settled on the numbers, like the rest of the matchday.</summary>
    void Instant()
    {
        int round = L.S.Round;
        L.Simulate();
        var done = L.YourFixture(round);
        Reward(done, round);
    }

    /// <summary>Play your fixture for real: you're always the side with the controls; at home
    /// it's your ground, away it's theirs, dressed in their colours and crest.</summary>
    public void Kickoff(Fixture f, bool watch = false)
    {
        bool home = f.Home == You;
        // Away: the hosts' own stadium, in their colours, their fans, their celebrations.
        string ground = home
            ? (Menus.Grounds.All.Any(g => g.Id == Club.S.Ground) && Club.S.Ground != "training" ? Club.S.Ground : "big")
            : "custom";
        int seed = (int)(ClubState.Now & 0xffff) + 1;
        var req = new MatchRequest { Setup = L.Setup(f), Seed = seed, Ground = ground, Watch = watch };
        if (!home)
        {
            req.HostCrest = L.Crest(f.Home);
            req.HostPlan = L.PlanOf(f.Home);
            req.HostGoalFx = Stadia.GoalFxOf(L.S.Clubs[f.Home]);
        }
        int round = f.Round;
        _ui.App.PlayFixture(req, o => Played(round, o, watch));
    }

    void Played(int round, MatchOutcome o, bool watch)
    {
        _ui.Go(this);
        _tab = 0;
        var f = L.YourFixture(round);
        if (f.Played || L.S.Round != round) return;
        bool home = f.Home == You;
        // Stopped watching before the end: the numbers settle it instead.
        if (!o.Finished && watch)
        {
            Instant();
            return;
        }
        if (!o.Finished)
        {
            L.Complete(home ? 0 : 3, home ? 3 : 0, null, true);
            Club.RecordForfeit();
            After(round, 0);
            return;
        }
        int opp = f.Other(You);
        var goals = (o.Goals ?? new()).Select(g => new GoalNote { Club = g.Team == 0 ? You : opp, Player = g.Name is { Length: > 0 } n ? n : L.Scorer(f, g.Team, g.Index), Minute = Math.Max(1, g.Minute) }).ToList();
        L.Complete(home ? o.Home : o.Away, home ? o.Away : o.Home, goals);
        Reward(f, round);
    }

    /// <summary>Coins for a league result: the usual match money plus a league bonus.</summary>
    void Reward(Fixture f, int round)
    {
        var (coins, res) = Club.RecordResult(f.GoalsFor(You), f.GoalsAgainst(You));
        int bonus = res == 'W' ? L.Bonus.win : res == 'D' ? L.Bonus.draw : 0;
        Club.Earn(bonus);
        After(round, coins + bonus);
    }

    void After(int round, int coins)
    {
        _ui.Open(new AroundGrounds(_ui, round, coins, () =>
        {
            _ui.Open(new Newspaper(_ui, round, () =>
            {
                if (L.SeasonOver) _ui.Open(new SeasonFinale(_ui));
            }));
        }));
    }

    // ---------------------------------------------------------------- debug

    /// <summary>Debug: `--screen=league[@rounds]` opens the hub (a league simulated that far),
    /// `--league-tab=N`, `--league=paper|around|preview|finale|draw|club` opens a moment.</summary>
    public static void Debug(Menus.Menus ui, string arg)
    {
        var lg = ui.Season;
        if (arg.StartsWith("--screen=league"))
        {
            int rounds = arg.Contains('@') ? int.Parse(arg[(arg.IndexOf('@') + 1)..]) : 0;
            if (rounds > 0)
            {
                lg.Abandon();
                lg.Start();
                for (int i = 0; i < rounds; i++) lg.Simulate();
            }
            ui.Go(ui.League);
        }
        if (arg.StartsWith("--league-tab=")) ui.League._tab = int.Parse(arg[13..]);
        if (arg.StartsWith("--screen=map"))
        {
            // `--screen=map@ita:4` starts a career in Italy with four trophies.
            if (arg.Contains('@'))
            {
                var parts = arg[(arg.IndexOf('@') + 1)..].Split(':');
                lg.Begin(parts[0].ToUpperInvariant());
                if (parts.Length > 1) lg.C.Trophies = int.Parse(parts[1]);
                if (!lg.Active) lg.Start(lg.Def);
            }
            ui.Go(ui.Map);
        }
        if (arg.StartsWith("--map-view="))
        {
            var v = arg[11..].Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            ui.Map.LookAt(v[0], v[1], v[2]);
        }
        if (arg == "--map=pick")
        {
            lg.C = null;
            ui.Go(ui.Map);
        }
        if (!lg.Active) return;
        int last = Math.Max(0, lg.S.Round - 1);
        switch (arg)
        {
            case "--league=paper": ui.Open(new Newspaper(ui, last, () => { })); break;
            case "--league=around": ui.Open(new AroundGrounds(ui, last, 2150, null)); break;
            case "--league=preview": if (lg.Next != null) ui.Open(new MatchPreview(ui, lg.Next)); break;
            case "--league=finale": ui.Open(new SeasonFinale(ui)); break;
            case "--league=draw": ui.Open(new LeagueDraw(ui)); break;
            case "--league=club": ui.Open(new ClubSheet(ui, 3)); break;
            case "--league=away": ui.League.Kickoff(lg.S.Fixtures.First(f => !f.Played && f.Away == You)); break;
            case "--league=watch": if (lg.Next != null) ui.League.Kickoff(lg.Next, true); break;
        }
    }
}
