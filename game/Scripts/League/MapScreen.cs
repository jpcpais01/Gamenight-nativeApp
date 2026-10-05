using System;
using System.Linq;
using Godot;
using GameNight.Club;
using GameNight.Menus;

namespace GameNight.League;

/// <summary>
/// The leagues of Europe: the relief map with a pin for every association, dotted roads up the
/// ladder from the local leagues to the Golden League, and a panel for the one you've picked:
/// what it is, how good its clubs are, what it pays, and whether you're in, can join, or still
/// need titles. The first time, it asks where your club is from.
/// </summary>
public sealed partial class MapScreen : PxCanvas
{
    readonly Menus.Menus _ui;
    LeagueState L => _ui.Season;
    LeagueDef _sel;
    string _pick;

    static readonly (float x, float y, float s)[] Clouds = { (0.1f, 0.22f, 1f), (0.42f, 0.12f, 0.8f), (0.7f, 0.5f, 1.1f), (0.25f, 0.62f, 0.7f), (0.55f, 0.82f, 0.9f) };

    public MapScreen(Menus.Menus ui)
    {
        _ui = ui;
    }

    public void Opened()
    {
        _sel = L.HasCareer ? L.Def : null;
        _pick = null;
    }

    bool Picking => !L.HasCareer;

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        var tex = EuropeMap.Texture(Size);
        DrawTextureRect(tex, new Rect2(0, 0, tex.GetWidth() * EuropeMap.Scale, tex.GetHeight() * EuropeMap.Scale), false);
        Glints(W, H);
        CloudLayer(W, H, true);
        CloudLayer(W, H, false);
        Roads();
        foreach (var d in Ladder.Leagues.OrderByDescending(d => d.Lat)) Pin(d);

        // Header: a dark band so the title reads over the sea.
        for (int i = 0; i < 6; i++) DrawRect(new Rect2(0, i * 11, W - EuropeMap.PanelW, 11), new Color(0.03f, 0.05f, 0.12f, 0.5f - i * 0.08f));
        BackButton(new Vector2(14, 12), () => _ui.Go(_ui.Home));
        Px.Text(this, Px.Small, new Vector2(66, 24), Picking ? "WHERE DOES YOUR CLUB COME FROM?" : "THE LEAGUES OF EUROPE", 8, Px.Cyan);
        Title(new Vector2(66, 52), Picking ? "PICK YOUR HOME" : "EUROPE");
        Legend(H);
        Panel(new Rect2(W - EuropeMap.PanelW, 0, EuropeMap.PanelW, H));
    }

    // ---------------------------------------------------------------- the living map

    /// <summary>Sun on the water: a few pixels twinkling, stepped.</summary>
    void Glints(float W, float H)
    {
        int step = (int)(T * 3);
        for (int k = 0; k < 40; k++)
        {
            var p = new Vector2(Hash(k, step / 4) * (W - EuropeMap.PanelW), Hash(k + 99, step / 4) * H).Snapped(new Vector2(2, 2));
            if (!EuropeMap.Sea(p)) continue;
            float a = ((step + k) % 4) switch { 0 => 0.25f, 1 => 0.6f, 2 => 0.35f, _ => 0 };
            DrawRect(new Rect2(p, new Vector2(k % 3 == 0 ? 4 : 2, 2)), new Color(0.85f, 0.95f, 1, a));
        }
    }

    static float Hash(int a, int b)
    {
        uint h = (uint)(a * 73856093 ^ b * 19349663);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f;
    }

    /// <summary>Little pixel clouds drifting east, their shadows on the ground below.</summary>
    void CloudLayer(float W, float H, bool shadow)
    {
        float span = W - EuropeMap.PanelW + 160;
        foreach (var (cx, cy, s) in Clouds)
        {
            float x = Mathf.PosMod(cx * span + (float)T * 5 * s, span) - 80;
            var o = new Vector2(Mathf.Round(x / 2) * 2, Mathf.Round(cy * H / 2) * 2);
            if (shadow) o += new Vector2(14, 22);
            var col = shadow ? new Color(0.02f, 0.05f, 0.12f, 0.16f) : new Color(1, 1, 1, 0.82f);
            float u = 8 * s;
            Blob(o, new Vector2(u * 6, u * 1.5f), col);
            Blob(o + new Vector2(u, -u), new Vector2(u * 3, u * 1.2f), col);
            Blob(o + new Vector2(u * 3, -u * 1.6f), new Vector2(u * 2.2f, u * 1.8f), col);
            if (!shadow) DrawRect(new Rect2(o + new Vector2(0, u * 1.5f - 2), new Vector2(u * 6, 2)).Abs(), new Color(0.75f, 0.82f, 0.9f, 0.7f));
        }
    }

    void Blob(Vector2 p, Vector2 s, Color c) => DrawRect(new Rect2(p.Round(), s.Round()), c);

    /// <summary>Dotted roads up the ladder: each league to the ones a step above it that share a country.</summary>
    void Roads()
    {
        foreach (var hi in Ladder.Leagues.Where(d => d.Tier > 1))
            foreach (var lo in Ladder.Leagues.Where(d => d.Tier == hi.Tier - 1 && d.Countries.Any(hi.Countries.Contains)))
            {
                bool open = L.Unlocked(hi);
                var col = Px.Hex(Ladder.TierColors[hi.Tier - 1], open ? 0.85f : 0.4f);
                Vector2 a = EuropeMap.Project(lo.Lon, lo.Lat) + new Vector2(0, -6), b = EuropeMap.Project(hi.Lon, hi.Lat) + new Vector2(0, -6);
                float len = a.DistanceTo(b);
                // Marching dots, uphill.
                float shift = (float)(T * 10 % 8);
                for (float t = shift; t < len; t += 8)
                {
                    var p = a.Lerp(b, t / len).Snapped(new Vector2(2, 2));
                    DrawRect(new Rect2(p - new Vector2(1, 1), new Vector2(3, 3)), new Color(0, 0, 0, 0.35f));
                    DrawRect(new Rect2(p - new Vector2(1, 1), new Vector2(2, 2)), col);
                }
            }
    }

    void Pin(LeagueDef d)
    {
        var g = EuropeMap.Project(d.Lon, d.Lat).Round();
        bool sel = _sel == d, mine = L.Playing(d), open = Picking ? d.Tier == 1 : L.Unlocked(d);
        bool held = Held("pin" + d.Id);
        var tc = Px.Hex(Ladder.TierColors[d.Tier - 1]);
        float bob = sel ? Mathf.Floor((float)(T * 3 % 4)) switch { 1 => -2, 2 => -4, 3 => -2, _ => 0 } : 0;
        // Shadow on the ground, the post, then the head.
        DrawColoredPolygon(Px.Ellipse(g, 7, 3, 10), new Color(0, 0, 0, 0.4f));
        float headY = g.Y - 16 - (d.Tier - 1) * 3 + bob + (held ? 2 : 0);
        DrawRect(new Rect2(g.X - 1, headY, 2, g.Y - headY), Px.Hex(0x1a1406));
        float r = 6 + d.Tier;
        if (sel || mine)
        {
            float pr = r + 4 + (float)(T * 8 % 8);
            DrawArc(new Vector2(g.X, headY), pr, 0, Mathf.Tau, 20, new Color(tc, 1 - (pr - r - 4) / 8), 2);
        }
        var head = new Rect2(g.X - r, headY - r, r * 2, r * 2);
        Px.Frame(this, head, open ? tc : Px.Hex(0x5a5866), open ? tc.Darkened(0.55f) : Px.Hex(0x2a2834), null, 2, 3);
        DrawRect(new Rect2(head.Position + new Vector2(3, 3), new Vector2(r * 0.6f, 2)), new Color(1, 1, 1, open ? 0.6f : 0.2f));
        if (!open) Lock(new Vector2(g.X, headY));
        else if (d.Tier == 4) Star(new Vector2(g.X, headY), Px.Hex(0x0e3a44));
        else Px.TextC(this, Px.Big, g.X, headY + 6, d.Tier.ToString(), 18, tc.Darkened(0.65f));
        // Your club's badge flies over the league you're in.
        if (mine) LeagueArt.Badge(this, new Rect2(g.X - 9, headY - r - 26, 18, 22), _ui.Club.S.Crest);
        if (sel || mine)
        {
            string name = d.Name.ToUpperInvariant();
            float w = Px.Width(Px.Small, name, 8) + 10;
            float lx = Mathf.Clamp(g.X - w / 2, 4, Size.X - EuropeMap.PanelW - 4 - w);
            var lr = new Rect2(lx, g.Y + 5, w, 13);
            Px.Frame(this, lr, new Color(0.04f, 0.05f, 0.12f, 0.85f), sel ? tc : Px.Line2, null, 1, 2);
            Px.TextC(this, Px.Small, lr.GetCenter().X, lr.End.Y - 3, name, 8, sel ? tc : Px.Ink);
        }
        Tap("pin" + d.Id, new Rect2(g.X - 16, headY - 16, 32, g.Y - headY + 22), () => Select(d));
    }

    void Select(LeagueDef d)
    {
        if (Picking)
        {
            if (d.Tier == 1)
            {
                _pick = d.Countries[0];
                _sel = d;
            }
            else _ui.Toast("Every club starts in a local league at home");
            return;
        }
        _sel = d;
    }

    /// <summary>A padlock: a wide body under an open arch.</summary>
    void Lock(Vector2 c, Color? ink = null)
    {
        var k = ink ?? Px.Hex(0xd6d4e2);
        DrawRect(new Rect2(c.X - 5, c.Y - 1, 10, 7), k);
        DrawRect(new Rect2(c.X - 4, c.Y - 5, 2, 4), k);
        DrawRect(new Rect2(c.X + 2, c.Y - 5, 2, 4), k);
        DrawRect(new Rect2(c.X - 3, c.Y - 7, 6, 2), k);
        DrawRect(new Rect2(c.X - 1, c.Y + 1, 2, 3), Px.Hex(0x2a2834));
    }

    void Star(Vector2 c, Color ink)
    {
        DrawRect(new Rect2(c.X - 1, c.Y - 5, 2, 10), ink);
        DrawRect(new Rect2(c.X - 5, c.Y - 1, 10, 2), ink);
        DrawRect(new Rect2(c.X - 3, c.Y - 3, 6, 6), ink);
    }

    /// <summary>A small pixel cup.</summary>
    void Cup(Vector2 p, float s, Color c)
    {
        DrawRect(new Rect2(p.X, p.Y, s * 6, s * 4), c);
        DrawRect(new Rect2(p.X + s, p.Y + s * 4, s * 4, s), c);
        DrawRect(new Rect2(p.X + s * 2.5f, p.Y + s * 5, s, s * 2), c);
        DrawRect(new Rect2(p.X + s * 1.5f, p.Y + s * 7, s * 3, s), c);
        DrawRect(new Rect2(p.X - s, p.Y + s, s, s * 2), c);
        DrawRect(new Rect2(p.X + s * 6, p.Y + s, s, s * 2), c);
        DrawRect(new Rect2(p.X + s, p.Y + s * 0.5f, s, s * 2), new Color(1, 1, 1, 0.55f));
    }

    float TrophyW => Px.Width(Px.Big, L.Trophies.ToString(), 20) + 34;

    /// <summary>Your trophies: league titles won anywhere on the map.</summary>
    void TrophyChip(Vector2 p)
    {
        string s = L.Trophies.ToString();
        var r = new Rect2(p, new Vector2(TrophyW, 24));
        Px.Frame(this, r, new Color(0.16f, 0.11f, 0.02f, 0.9f), Px.Hex(0xb37400), Px.ShadowSoft, 2, 3);
        Cup(r.Position + new Vector2(9, 5), 1.75f, Px.Gold);
        Px.Text(this, Px.Big, r.Position + new Vector2(26, 19), s, 20, Px.Hex(0xffe066));
    }

    /// <summary>The way up: four steps, lit as your trophies open them.</summary>
    void Track(Rect2 r)
    {
        Px.Text(this, Px.Small, r.Position + new Vector2(0, 8), "THE WAY UP", 8, Px.InkDim);
        float y = r.Position.Y + 26, step = (r.Size.X - 24) / 3;
        for (int t = 0; t < 4; t++)
        {
            float x = r.Position.X + 12 + t * step;
            int need = Ladder.Leagues.First(d => d.Tier == t + 1).Need;
            bool open = L.Trophies >= need;
            var tc = Px.Hex(Ladder.TierColors[t]);
            if (t < 3)
            {
                bool next = L.Trophies >= Ladder.Leagues.First(d => d.Tier == t + 2).Need;
                DrawRect(new Rect2(x, y - 1, step, 3), next ? tc : Px.Line2);
            }
            var node = new Rect2(x - 8, y - 8, 16, 16);
            Px.Frame(this, node, open ? tc : Px.Hex(0x2a2834), open ? tc.Darkened(0.5f) : Px.Line2, null, 2, 2);
            if (_sel != null && _sel.Tier == t + 1) DrawRect(new Rect2(x - 3, y - 3, 6, 6), open ? Px.Dark : Px.Ink);
            Px.TextC(this, Px.Small, x, y + 22, Ladder.TierNames[t], 8, open ? tc : Px.InkDim);
            Px.TextC(this, Px.Small, x, y + 34, need == 0 ? "START" : need == 1 ? "1 TITLE" : $"{need} TITLES", 8, Px.InkDim);
        }
    }

    void Legend(float H)
    {
        float x = 14, y = H - 30;
        float w = 0;
        var items = Ladder.TierNames.Select((n, i) => (n, i, need: Ladder.Leagues.First(d => d.Tier == i + 1).Need)).ToArray();
        foreach (var (n, _, need) in items) w += Px.Width(Px.Small, need > 0 ? $"{n} {need}" : n, 8) + (need > 0 ? 34 : 24);
        Px.Frame(this, new Rect2(x, y, w + 8, 20), new Color(0.04f, 0.05f, 0.12f, 0.82f), Px.Line, null, 1, 3);
        x += 8;
        foreach (var (n, i, need) in items)
        {
            DrawRect(new Rect2(x, y + 6, 8, 8), Px.Hex(Ladder.TierColors[i]));
            string s = need > 0 ? $"{n} {need}" : n;
            Px.Text(this, Px.Small, new Vector2(x + 12, y + 14), s, 8, Px.Ink);
            x += Px.Width(Px.Small, s, 8) + 16;
            if (need > 0)
            {
                Cup(new Vector2(x - 2, y + 5), 1.2f, Px.Gold);
                x += 10;
            }
            x += 8;
        }
    }

    // ---------------------------------------------------------------- the panel

    void Panel(Rect2 r)
    {
        DrawRect(r, new Color(0.04f, 0.04f, 0.13f, 0.92f));
        DrawRect(new Rect2(r.Position, new Vector2(3, r.Size.Y)), Px.Line2);
        Px.Scanlines(this, r);
        var c = r.Grow(-14);
        c = new Rect2(c.Position + new Vector2(3, 0), c.Size - new Vector2(3, 0));
        if (Picking) PickPanel(c);
        else if (_sel != null)
        {
            TrophyChip(new Vector2(c.End.X - TrophyW, c.Position.Y - 2));
            LeaguePanel(c, _sel);
        }
    }

    void PickPanel(Rect2 c)
    {
        float x = c.Position.X, y = c.Position.Y + 12;
        Px.Text(this, Px.Small, new Vector2(x, y), "YOUR JOURNEY STARTS AT HOME", 8, Px.Cyan);
        y += 8;
        foreach (var line in Px.Wrap(Px.Small, "EVERY CLUB STARTS IN ITS LOCAL LEAGUE. WIN TITLES TO UNLOCK THE REGIONAL LEAGUES, THEN THE PREMIERS, THEN THE GOLDEN LEAGUE.", 8, c.Size.X))
        {
            y += 13;
            Px.Text(this, Px.Small, new Vector2(x, y), line, 8, Px.InkDim);
        }
        y += 12;
        float cw = (c.Size.X - 8) / 2, ch = 34;
        for (int i = 0; i < Ladder.Countries.Length; i++)
        {
            var co = Ladder.Countries[i];
            var cell = new Rect2(x + i % 2 * (cw + 8), y + i / 2 * (ch + 6), cw, ch);
            bool on = _pick == co.Code, held = Held("co" + co.Code);
            var rr = held ? cell.Translated(new Vector2(2, 2)) : cell;
            Px.Frame(this, rr, on ? Px.Hex(0x3a2c08) : Px.Glass2, on ? Px.Gold : Px.Line2, held ? null : Px.ShadowSoft, 2, 3);
            Px.Flag(this, new Rect2(rr.Position + new Vector2(8, 10), new Vector2(21, 14)), co.Nation);
            string nm = co.Name.ToUpperInvariant();
            int fs = 20;
            while (fs > 14 && Px.Width(Px.Big, nm, fs) > cw - 42) fs -= 2;
            Px.Text(this, Px.Big, rr.Position + new Vector2(36, 18 + fs * 0.3f), nm, fs, on ? Px.Gold : Px.Ink);
            var code = co.Code;
            Tap("co" + co.Code, cell, () =>
            {
                _pick = code;
                _sel = Ladder.EntryOf(code);
            });
        }
        if (_pick != null)
        {
            var e = Ladder.EntryOf(_pick);
            Px.Text(this, Px.Small, new Vector2(x, c.End.Y - 62), Px.Fit(Px.Small, $"YOU START IN: {e.Name.ToUpperInvariant()}", 8, c.Size.X), 8, Px.Hex(0xd9915a));
        }
        GoldButton("begin", new Rect2(x, c.End.Y - 48, c.Size.X, 46), _pick == null ? "PICK A COUNTRY" : $"START IN {Ladder.CountryOf(_pick).Name.ToUpperInvariant()}  >", 24, () =>
        {
            if (_pick == null) return;
            bool had = L.Active;
            L.Begin(_pick);
            _sel = L.Def;
            if (had)
            {
                _ui.Toast($"Your league is now the {L.Def.Name}");
                _ui.Go(_ui.League);
            }
            else Join(L.Def);
        }, _pick != null);
    }

    void LeaguePanel(Rect2 c, LeagueDef d)
    {
        float x = c.Position.X, y = c.Position.Y;
        var tc = Px.Hex(Ladder.TierColors[d.Tier - 1]);
        // Tier tag and name.
        string tag = $"{d.TierName} · TIER {d.Tier}";
        float tw = Px.Width(Px.Small, tag, 8) + 14;
        Px.Frame(this, new Rect2(x, y + 2, tw, 16), tc, tc.Darkened(0.5f), null, 2, 2);
        Px.Text(this, Px.Small, new Vector2(x + 7, y + 14), tag, 8, Px.Dark);
        float fx = x + tw + 8;
        foreach (var code in d.Countries.Take(5))
        {
            Px.Flag(this, new Rect2(fx, y + 4, 15, 11), Ladder.CountryOf(code).Nation);
            fx += 20;
        }
        if (d.Countries.Length > 5) Px.Text(this, Px.Small, new Vector2(fx, y + 14), $"+{d.Countries.Length - 5}", 8, Px.InkDim);
        y += 22;
        var lines = Px.Wrap(Px.Big, d.Name.ToUpperInvariant(), 30, c.Size.X);
        foreach (var line in lines.Take(2))
        {
            y += 27;
            Px.Text(this, Px.Big, new Vector2(x, y), line, 30, tc, new Color(0, 0, 0, 0.6f), 2);
        }
        y += 6;
        foreach (var line in Px.Wrap(Px.Small, d.Blurb.ToUpperInvariant(), 8, c.Size.X).Take(3))
        {
            y += 12;
            Px.Text(this, Px.Small, new Vector2(x, y), line, 8, Px.InkDim);
        }
        y += 14;
        // The numbers.
        int you = _ui.Club.TeamRating();
        int lvl = (int)Math.Round(d.Level);
        Stat(x, ref y, c.Size.X, "CLUBS", $"~{lvl} OVR", you >= lvl + 3 ? Px.Win : you <= lvl - 4 ? Px.Loss : Px.Ink);
        Stat(x, ref y, c.Size.X, "YOUR TEAM", $"{you} OVR", Px.Ink);
        Stat(x, ref y, c.Size.X, "CHAMPIONS GET", Px.Thousands((long)Math.Round(LeagueState.Prize[0] * d.PrizeScale)), Px.Hex(0xffe066));
        int titles = L.TitlesIn(d), best = L.BestIn(d);
        Stat(x, ref y, c.Size.X, "YOUR RECORD", titles > 0 ? (titles == 1 ? "1 TITLE" : $"{titles} TITLES") : best > 0 ? $"BEST {LeagueState.Ordinal(best)}" : "NOT PLAYED", titles > 0 ? Px.Gold : Px.InkDim);

        var b = new Rect2(x, c.End.Y - 48, c.Size.X, 46);
        if (b.Position.Y - 38 - y > 70) Track(new Rect2(x, y + 10, c.Size.X, 60));
        if (L.Playing(d))
        {
            string where = L.SeasonOver ? "SEASON OVER" : $"MATCHDAY {L.S.Round + 1}" + (L.S.Round > 0 ? $" · {LeagueState.Ordinal(L.Place(LeagueState.You))}" : "");
            Px.Text(this, Px.Small, new Vector2(x, b.Position.Y - 10), $"YOU PLAY HERE · {where}", 8, Px.Win);
            GoldButton("go", b, "GO TO LEAGUE  >", 26, () => _ui.Go(_ui.League));
        }
        else if (L.Unlocked(d))
        {
            Px.Text(this, Px.Small, new Vector2(x, b.Position.Y - 10), d.Need > 0 ? "UNLOCKED · YOUR CLUB IS WELCOME" : "OPEN TO EVERY CLUB", 8, Px.Win);
            GoldButton("join", b, "JOIN THIS LEAGUE  >", 24, () => AskJoin(d));
        }
        else
        {
            int more = d.Need - L.Trophies;
            Px.Text(this, Px.Small, new Vector2(x, b.Position.Y - 26), $"NEEDS {d.Need} LEAGUE TITLES · YOU HAVE {L.Trophies}", 8, Px.Loss);
            // Progress: one cup per title needed.
            float px = x;
            for (int i = 0; i < d.Need; i++)
            {
                Cup(new Vector2(px + 2, b.Position.Y - 18), 1.2f, i < L.Trophies ? Px.Gold : Px.Hex(0x4a4858));
                px += 14;
            }
            Px.Frame(this, b, new Color(0.18f, 0.17f, 0.24f), Px.Line2, Px.ShadowSoft);
            Lock(new Vector2(b.Position.X + 22, b.GetCenter().Y + 1));
            Px.TextC(this, Px.Big, b.GetCenter().X + 8, b.GetCenter().Y + 8, more == 1 ? "WIN 1 MORE TITLE" : $"WIN {more} MORE TITLES", 22, Px.InkDim);
        }
    }

    void Stat(float x, ref float y, float w, string label, string value, Color c)
    {
        DrawRect(new Rect2(x, y + 2, w, 1), Px.Line);
        y += 17;
        Px.Text(this, Px.Small, new Vector2(x, y - 2), label, 8, Px.InkDim);
        Px.TextR(this, Px.Big, x + w, y, value, 20, c);
        y += 4;
    }

    void AskJoin(LeagueDef d)
    {
        if (L.Active && !L.SeasonOver && L.S.Round > 0)
            _ui.Open(new ConfirmModal(_ui, $"Move to the {d.Name}?",
                $"Your season in the {L.S.Name} (matchday {L.S.Round + 1}) is left unfinished. Your trophies and titles stay yours.", "Move", () => Join(d)));
        else Join(d);
    }

    void Join(LeagueDef d)
    {
        L.Join(d);
        _sel = d;
        _ui.Go(_ui.League);
        _ui.Open(new LeagueDraw(_ui));
    }
}
