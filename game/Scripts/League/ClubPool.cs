using System;
using System.Collections.Generic;
using System.Linq;
using GameNight.Club;
using GameNight.Sim;

namespace GameNight.League;

/// <summary>
/// A set of clubs to play against, yours always at index 0 (its name, kit and crest come live
/// from your club): their faces, squads and stadiums, the line-ups the engine plays, and the
/// goals model that settles the matches you don't play. The league and the cups both build on it.
/// </summary>
public abstract class ClubPool
{
    public const int You = 0;
    protected readonly ClubState _club;

    protected ClubPool(ClubState club)
    {
        _club = club;
    }

    /// <summary>The clubs, yours first.</summary>
    protected abstract List<LClub> Pool { get; }

    public abstract void Save();

    public string Name(int c) => c == You ? _club.S.Name : Pool[c].Name;
    public string Short(int c) => c == You ? _club.ShortName : Pool[c].Short;
    public Crest Crest(int c) => c == You ? _club.S.Crest : Pool[c].Crest;
    public int Color(int c) => c == You ? _club.S.Kit.Main : Pool[c].Main;
    public int Color2(int c) => c == You ? _club.S.Kit.Secondary : Pool[c].Secondary;
    public string Manager(int c) => c == You ? _club.S.Coach.Name : Pool[c].Manager;

    /// <summary>The home ground's name, for the paper and the previews.</summary>
    public string GroundName(int c)
    {
        if (c != You) return Pool[c].Ground;
        var words = _club.S.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (words.Length > 0 ? words[0] : "Home") + " Park";
    }

    public int Rating(int c) => c == You ? _club.TeamRating() : RatingOf(Pool[c]);

    protected static int RatingOf(LClub c)
    {
        var f = Formations.ById(c.Formation);
        double sum = 0;
        for (int i = 0; i < 11 && i < c.Squad.Count; i++) sum += Cards.RatingIn(c.Squad[i], f.Slots[i].Pos);
        return Cards.JsRound(sum / 11);
    }

    protected static bool Attacker(Position p) => p is Position.ST or Position.LW or Position.RW or Position.CAM or Position.LM or Position.RM;
    protected static bool Defender(Position p) => p is Position.GK or Position.CB or Position.LB or Position.RB or Position.CDM;

    /// <summary>Attack and defence from the eleven: forwards and creators for one, the back line,
    /// holder and keeper (who counts double) for the other; midfield feeds both.</summary>
    public (double att, double def) Power(int c)
    {
        Card[] xi;
        Position[] pos;
        if (c == You)
        {
            xi = _club.Starters();
            pos = Enumerable.Range(0, 11).Select(i => _club.Slot(i).Pos).ToArray();
        }
        else
        {
            var cl = Pool[c];
            var f = Formations.ById(cl.Formation);
            xi = cl.Squad.Take(11).ToArray();
            pos = f.Slots.Select(s => s.Pos).ToArray();
        }
        double a = 0, an = 0, d = 0, dn = 0;
        for (int i = 0; i < 11; i++)
        {
            double r = xi.Length > i && xi[i] != null ? Cards.RatingIn(xi[i], pos[i]) : 35;
            if (Attacker(pos[i])) { a += r; an++; }
            else if (Defender(pos[i])) { double w = pos[i] == Position.GK ? 2 : 1; d += r * w; dn += w; }
            else { a += r * 0.6; an += 0.6; d += r * 0.6; dn += 0.6; }
        }
        return (a / Math.Max(1, an), d / Math.Max(1, dn));
    }

    /// <summary>Clubs come from the league's countries in turn, so a regional league is a real mix.</summary>
    protected static Country CountryFor(LeagueDef def, int i, Random r) =>
        Ladder.CountryOf(def.Countries.Length == 1 ? def.Countries[0] : i < def.Countries.Length * 2 ? def.Countries[i % def.Countries.Length] : def.Countries[r.Next(def.Countries.Length)]);

    protected static int Pal(Random r) => Club.Crest.Palette[r.Next(Club.Crest.Palette.Length)];

    protected static double Lum(int c) => 0.299 * ((c >> 16) & 255) + 0.587 * ((c >> 8) & 255) + 0.114 * (c & 255);

    protected static double ColorDist(int a, int b) =>
        Math.Sqrt(Math.Pow(((a >> 16) & 255) - ((b >> 16) & 255), 2) + Math.Pow(((a >> 8) & 255) - ((b >> 8) & 255), 2) + Math.Pow((a & 255) - (b & 255), 2));

    protected LClub NewClub(Random r, HashSet<string> towns, HashSet<string> shorts, double level, Country co)
    {
        var (name, town) = Names.Club(r, towns, co);
        bool latin = co.Latin;
        var c = new LClub { Name = name, Town = town, Founded = 1870 + r.Next(60), Manager = Names.Person(r, co) };
        c.Short = Names.Short(town, shorts);
        c.Ground = Names.Ground(r, town, co);
        // Colours first, then a crest in them, so shirt and badge belong together.
        c.Main = Pal(r);
        int tries = 0;
        do c.Secondary = Pal(r);
        while (tries++ < 40 && (Math.Abs(Lum(c.Secondary) - Lum(c.Main)) < 80 || c.Secondary == c.Main));
        int[] patterns = { 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        c.Pattern = patterns[r.Next(patterns.Length)];
        c.Shorts = r.Next(3) switch { 0 => 0xf3ede0, 1 => 0x14121c, _ => c.Secondary };
        bool dark = Lum(c.Main) < 110;
        (c.AwayMain, c.AwaySecondary, c.AwayShorts) = dark ? (0xf1ebdc, c.Main, 0xf1ebdc) : r.Next(2) == 0 ? (0x23262e, c.Main, 0x23262e) : (c.Secondary, c.Main, c.Secondary);
        c.AwayPattern = r.Next(3) == 0 ? c.Pattern : 0;
        c.Crest = GameNight.Menus.CrestArt.Random(new Crest(), c.Main, c.Secondary);
        if (r.Next(3) > 0)
        {
            c.Crest.Primary = c.Main;
            c.Crest.Secondary = c.Secondary;
        }
        c.Crest.Text = c.Short;
        c.Crest.Year = c.Founded.ToString();
        c.Crest.Stars = level > 80 ? r.Next(4) : r.Next(5) == 0 ? 1 : 0;
        c.Nickname = Names.Nickname(r, c.Crest.Emblem);
        c.Style = r.Next(4);
        c.Formation = Formations.All[r.Next(Formations.All.Length)].Id;
        c.Venue = latin ? (r.Next(3) > 0 ? "comunale" : "big") : level > 70 || r.Next(2) == 0 ? "big" : "old";
        c.Capacity = (int)Math.Round(Math.Clamp(9000 + (level - 45) * 1600 + r.Next(-6000, 9000), 6000, 82000) / 100) * 100;
        Recruit(c, r, level);
        return c;
    }

    protected static Rarity RarityFor(int o) => o >= 88 ? Rarity.Icon : o >= 82 ? Rarity.Legendary : o >= 74 ? Rarity.Epic : o >= 64 ? Rarity.Rare : Rarity.Common;

    protected static readonly Position[] BenchPos = { Position.GK, Position.CB, Position.RB, Position.CM, Position.CAM, Position.LW, Position.ST };

    /// <summary>A full squad at this level: the eleven in their slots, then seven on the bench.</summary>
    protected static void Recruit(LClub c, Random r, double level)
    {
        var rng = new Rng(r.Next());
        var f = Formations.ById(c.Formation);
        c.Squad.Clear();
        for (int i = 0; i < 11; i++)
            c.Squad.Add(Sign(rng, f.Slots[i].Pos, level + rng.Gauss() * 3));
        foreach (var p in BenchPos) c.Squad.Add(Sign(rng, p, level - 4 + rng.Gauss() * 3));
    }

    protected static Card Sign(Rng rng, Position p, double ovr)
    {
        int o = (int)Math.Clamp(Math.Round(ovr), 45, 95);
        return Cards.Generate(rng, RarityFor(o), p, o);
    }

    // ---------------------------------------------------------------- kits and line-ups

    public TeamInfo Info(int c, bool change = false)
    {
        if (c == You) return _club.Info();
        var cl = Pool[c];
        var gk = TeamData.Default[1].Kit;
        return new TeamInfo
        {
            Name = cl.Name, Short = cl.Short,
            Kit = change
                ? new Kit { Shirt = cl.AwayMain, Shirt2 = cl.AwaySecondary, Shorts = cl.AwayShorts, Socks = cl.AwayMain, GkShirt = gk.GkShirt, GkShorts = gk.GkShorts, Pattern = cl.AwayPattern }
                : new Kit { Shirt = cl.Main, Shirt2 = cl.Secondary, Shorts = cl.Shorts, Socks = cl.Main, GkShirt = gk.GkShirt, GkShorts = gk.GkShorts, Pattern = cl.Pattern },
        };
    }

    protected static bool Clash(Kit a, Kit b) => ColorDist(a.Shirt, b.Shirt) < 120 || ColorDist(a.Shorts, b.Shorts) < 50 && ColorDist(a.Shirt, b.Shirt) < 170;

    /// <summary>Your fixture as the engine plays it: you are always side 0 (the controls are
    /// yours); the hosts wear their home kit and the visitors change if the colours clash.</summary>
    public MatchSetup Setup(Fixture f)
    {
        int opp = f.Other(You);
        bool home = f.Home == You;
        var mine = _club.TeamSetup();
        var them = TeamOf(opp);
        if (home)
        {
            if (Clash(mine.Info.Kit, them.Info.Kit)) them.Info = Info(opp, true);
            if (Clash(mine.Info.Kit, them.Info.Kit)) them.Info.Kit = Fallback(mine.Info.Kit.Shirt, them.Info.Kit);
        }
        else if (Clash(mine.Info.Kit, them.Info.Kit))
            mine.Info.Kit = Fallback(them.Info.Kit.Shirt, mine.Info.Kit);
        return new MatchSetup { Teams = new[] { mine, them } };
    }

    /// <summary>A change strip, white or dark (whichever stands apart from `against`), trimmed in the club colour.</summary>
    protected static Kit Fallback(int against, Kit k)
    {
        bool white = ColorDist(against, 0xf1ebdc) > ColorDist(against, 0x23262e);
        int body = white ? 0xf1ebdc : 0x23262e;
        return new Kit { Shirt = body, Shirt2 = k.Shirt, Shorts = body, Socks = body, GkShirt = k.GkShirt, GkShorts = k.GkShorts, Pattern = 0 };
    }

    protected TeamSetup TeamOf(int c)
    {
        var cl = Pool[c];
        var f = Formations.ById(cl.Formation);
        var t = new TeamSetup { Info = Info(c) };
        int top = -1;
        for (int i = 0; i < 11; i++)
        {
            var slot = f.Slots[i];
            var card = cl.Squad[i];
            var sp = Cards.ToSim(card, slot.Pos);
            t.Players.Add(new SetupPlayer { Name = sp.Name, Number = sp.Number, Attrs = sp.Attrs, Look = sp.Look, Foot = sp.Foot, Role = Cards.RoleOf(slot.Pos), X = slot.X, Z = slot.Z, Source = card });
            if (card.Overall > top)
            {
                top = card.Overall;
                t.Captain = i;
            }
        }
        t.Bench = cl.Squad.Skip(11).Take(7).Select(Cards.Sub).ToList();
        return t;
    }

    /// <summary>The name of the man in shirt slot `index` of side `team` in your match.</summary>
    public string Scorer(Fixture f, int team, int index)
    {
        if (team == 0) return _club.Card(_club.S.Lineup.Slots[Math.Clamp(index, 0, 10)])?.Name ?? "";
        var sq = Pool[f.Other(You)].Squad;
        return index >= 0 && index < sq.Count ? sq[index].Name : "";
    }

    /// <summary>A club's own stadium, the same on every visit.</summary>
    public GameNight.Grounds.Build.StadiumPlan PlanOf(int c)
    {
        if (c == You) return _club.S.Stadium;
        var cl = Pool[c];
        if (cl.Plan?.Sets == null)
        {
            cl.Plan = Stadia.Make(cl, RatingOf(cl));
            Save();
        }
        return cl.Plan;
    }

    /// <summary>Their best player: the one to watch.</summary>
    public Card Star(int c) => c == You ? _club.Starters().Where(x => x != null).OrderByDescending(x => x.Overall).FirstOrDefault() : Pool[c].Squad.Take(11).OrderByDescending(x => x.Overall).First();

    protected static double Poisson(Random r, double lambda)
    {
        double l = Math.Exp(-lambda), p = 1;
        int k = 0;
        do { k++; p *= r.NextDouble(); } while (p > l && k < 12);
        return k - 1;
    }

    protected static double Gauss(Random r) => (r.NextDouble() + r.NextDouble() + r.NextDouble() + r.NextDouble() - 2) * 1.7320508;

    protected int StyleOf(int c) => c == You ? 0 : Pool[c].Style;

    /// <summary>Expected goals for each side, before the day's luck.</summary>
    public (double home, double away) Expected(int home, int away)
    {
        var (ah, dh) = Power(home);
        var (aa, da) = Power(away);
        double lh = 1.25 * Math.Exp(0.045 * (ah - da) + 0.14);
        double la = 1.05 * Math.Exp(0.045 * (aa - dh));
        void Style(int s, ref double own, ref double conceded, bool underdog)
        {
            if (s == 1) { own *= 1.14; conceded *= 1.1; }
            else if (s == 2) { own *= 0.86; conceded *= 0.85; }
            else if (s == 3 && underdog) { own *= 1.1; conceded *= 0.95; }
        }
        Style(StyleOf(home), ref lh, ref la, ah + dh < aa + da);
        Style(StyleOf(away), ref la, ref lh, aa + da < ah + dh);
        return (Math.Clamp(lh, 0.15, 4.5), Math.Clamp(la, 0.15, 4.5));
    }

    /// <summary>Goals come a little more often late on, and now and then in stoppage time.</summary>
    protected static int Minute(Random r)
    {
        double u = Math.Pow(r.NextDouble(), 0.85);
        int m = 1 + (int)(u * 90);
        if (m >= 90 && r.Next(2) == 0) m = 90 + 1 + r.Next(5);
        return Math.Min(m, 95);
    }

    protected static double ScoreWeight(Position p) => p switch
    {
        Position.ST => 5, Position.LW or Position.RW => 3.4, Position.CAM => 3, Position.LM or Position.RM => 2.2,
        Position.CM => 1.5, Position.CDM => 0.7, Position.CB => 0.7, Position.LB or Position.RB => 0.5, _ => 0,
    };

    protected string PickScorer(int c, Random r)
    {
        Card[] xi;
        Position[] pos;
        if (c == You)
        {
            xi = _club.Starters();
            pos = Enumerable.Range(0, 11).Select(i => _club.Slot(i).Pos).ToArray();
        }
        else
        {
            xi = Pool[c].Squad.Take(11).ToArray();
            pos = Formations.ById(Pool[c].Formation).Slots.Select(s => s.Pos).ToArray();
        }
        var w = new double[11];
        double sum = 0;
        for (int i = 0; i < 11; i++)
        {
            if (xi[i] == null) continue;
            w[i] = ScoreWeight(pos[i]) * Math.Pow(xi[i].Stats.Shooting / 70.0, 2);
            sum += w[i];
        }
        double pick = r.NextDouble() * sum;
        for (int i = 0; i < 11; i++)
        {
            pick -= w[i];
            if (pick <= 0 && xi[i] != null) return xi[i].Name;
        }
        return xi.LastOrDefault(x => x != null)?.Name ?? "";
    }

    /// <summary>A fixture settled on the model: goals for each side, their scorers and minutes.</summary>
    protected void Score(Fixture f, Random r)
    {
        var (lh, la) = Expected(f.Home, f.Away);
        f.Hg = (int)Poisson(r, lh * Math.Exp(Gauss(r) * 0.15));
        f.Ag = (int)Poisson(r, la * Math.Exp(Gauss(r) * 0.15));
        f.Goals = new();
        for (int i = 0; i < f.Hg; i++) f.Goals.Add(new GoalNote { Club = f.Home, Player = PickScorer(f.Home, r), Minute = Minute(r) });
        for (int i = 0; i < f.Ag; i++) f.Goals.Add(new GoalNote { Club = f.Away, Player = PickScorer(f.Away, r), Minute = Minute(r) });
        f.Goals = f.Goals.OrderBy(g => g.Minute).ToList();
    }
}
