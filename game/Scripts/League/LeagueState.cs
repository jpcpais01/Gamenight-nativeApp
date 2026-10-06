using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GameNight.Club;
using GameNight.Sim;

namespace GameNight.League;

/// <summary>A club in the league. Index 0 is always yours: its name, kit and crest come live
/// from your club, so only the league's own facts about it are kept here.</summary>
public sealed class LClub
{
    public string Name = "", Short = "", Town = "", Nickname = "", Ground = "", Manager = "";
    public int Founded = 1900;
    public Crest Crest = new();
    public int Main, Secondary, Shorts, Pattern;
    public int AwayMain, AwaySecondary, AwayShorts, AwayPattern;
    public string Formation = "433";
    /// <summary>First eleven in formation slot order, then the bench.</summary>
    public List<Card> Squad = new();
    /// <summary>0 balanced, 1 attacking, 2 defensive, 3 counter-attacking.</summary>
    public int Style;
    /// <summary>The ground id their home matches were played at before every club had its own (old saves).</summary>
    public string Venue = "big";
    /// <summary>Their own stadium (see Stadia), drawn up the first time it's needed.</summary>
    public GameNight.Grounds.Build.StadiumPlan Plan;
    public int Capacity = 30000;
    /// <summary>Came up from below this season.</summary>
    public bool Promoted;
}

public sealed class GoalNote
{
    public int Club;
    public string Player = "";
    public int Minute;
}

public sealed class Fixture
{
    public int Round, Home, Away;
    public int Hg = -1, Ag = -1;
    public int Crowd;
    /// <summary>Left before the end: booked 0-3.</summary>
    public bool Forfeit;
    public List<GoalNote> Goals = new();
    public bool Played => Hg >= 0;
    public bool Has(int club) => Home == club || Away == club;
    public int Other(int club) => Home == club ? Away : Home;
    public int GoalsFor(int club) => Home == club ? Hg : Ag;
    public int GoalsAgainst(int club) => Home == club ? Ag : Hg;
    public char ResultFor(int club) => GoalsFor(club) > GoalsAgainst(club) ? 'W' : GoalsFor(club) == GoalsAgainst(club) ? 'D' : 'L';
}

public sealed class SeasonRow
{
    public int Season;
    public string Champion = "";
    public Crest ChampionCrest;
    public int UserPos, UserPts;
    public string TopScorer = "";
    public int TopGoals;
}

public sealed class LeagueSave
{
    public int V = 1;
    public int Season = 1;
    public string Name = "", Paper = "";
    /// <summary>Which league on the map (Ladder.Leagues); empty in saves from before the map.</summary>
    public string LeagueId = "";
    public int Seed;
    public List<LClub> Clubs = new();
    public List<Fixture> Fixtures = new();
    /// <summary>The next matchday to play (0..29); 30 = the season is over.</summary>
    public int Round;
    public bool PrizePaid;
    public int Titles;
    public List<SeasonRow> History = new();
}

public sealed class Row
{
    public int Club, P, W, D, L, Gf, Ga, Pts;
    public string Form = "";
    public int Gd => Gf - Ga;
}

/// <summary>
/// The league: 16 clubs, home and away, 30 matchdays. Your fixture is played for real; the other
/// seven each matchday are settled by a goals model on the clubs' attack and defence (a Poisson
/// draw on each side, nudged for home advantage, style and the day's form), with scorers picked
/// by position and shooting. Saved as JSON next to the club.
/// </summary>
public sealed class LeagueState
{
    public const int Clubs = 16, Rounds = 30, PerRound = 8;
    public const int You = 0;
    const string SaveFile = "league.json", CareerFile = "career.json";
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    public static readonly int[] Prize = { 30000, 20000, 15000, 12000, 10000, 8000, 7000, 6000, 5000, 4500, 4000, 3500, 3000, 2500, 2000, 1500 };
    public static readonly string[] Styles = { "Balanced", "All-out attack", "Defensive", "Counter-attack" };

    public LeagueSave S;
    /// <summary>Your career on the map; null until you pick a home country.</summary>
    public CareerSave C;
    readonly string _path, _careerPath;
    readonly ClubState _club;

    public LeagueState(string dir, ClubState club)
    {
        _club = club;
        _path = Path.Combine(dir, SaveFile);
        _careerPath = Path.Combine(dir, CareerFile);
        S = Load();
        C = Ladder.Load(_careerPath);
    }

    public bool Active => S != null;

    // ---------------------------------------------------------------- the career

    public bool HasCareer => C != null;

    /// <summary>The league being played (or last played); your home country's local league otherwise.</summary>
    public LeagueDef Def => Ladder.ById(S?.LeagueId ?? "") ?? Ladder.EntryOf(C?.Country ?? "ENG");

    public Country Home => Ladder.CountryOf(C?.Country ?? "ENG");

    public int Trophies => C?.Trophies ?? 0;

    public bool Unlocked(LeagueDef d) => C != null && C.Trophies >= d.Need;

    public bool Playing(LeagueDef d) => S != null && S.LeagueId == d.Id;

    public int TitlesIn(LeagueDef d) => C != null && C.Titles.TryGetValue(d.Id, out var n) ? n : 0;

    public int BestIn(LeagueDef d) => C != null && C.Best.TryGetValue(d.Id, out var n) ? n : 0;

    /// <summary>Start a career in this country. A league already under way (from before the map)
    /// becomes the country's local league, and the titles won in it count as trophies.</summary>
    public void Begin(string country)
    {
        C = new CareerSave { Country = country };
        if (S != null && Ladder.ById(S.LeagueId) == null)
        {
            var entry = Ladder.EntryOf(country);
            S.LeagueId = entry.Id;
            S.Name = entry.Name;
            C.Trophies = S.Titles;
            if (S.Titles > 0) C.Titles[entry.Id] = S.Titles;
            C.Seasons = S.History.Count;
            Save();
        }
        SaveCareer();
    }

    void SaveCareer() => Ladder.Save(_careerPath, C);

    /// <summary>Leave the current league (its season is thrown away) and start in another.</summary>
    public void Join(LeagueDef d)
    {
        S = null;
        Start(d);
    }

    /// <summary>The prize for finishing here, scaled to the league's standing.</summary>
    public int PrizeFor(int pos) => (int)Math.Round(Prize[Math.Clamp(pos, 1, Clubs) - 1] * Def.PrizeScale / 100) * 100;

    /// <summary>What a win and a draw are worth in this league.</summary>
    public (int win, int draw) Bonus => ((int)Math.Round(500 * Def.PrizeScale / 50) * 50, (int)Math.Round(250 * Def.PrizeScale / 50) * 50);
    public bool SeasonOver => S != null && S.Round >= Rounds;

    LeagueSave Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var s = JsonSerializer.Deserialize<LeagueSave>(File.ReadAllText(_path), Json);
            return s != null && s.V == 1 && s.Clubs?.Count == Clubs && s.Fixtures?.Count == Rounds * PerRound ? s : null;
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        if (S == null) return;
        try
        {
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(S, Json));
            File.Move(tmp, _path, true);
        }
        catch
        {
            /* keep playing in memory */
        }
    }

    // ---------------------------------------------------------------- your club, live

    public string Name(int c) => c == You ? _club.S.Name : S.Clubs[c].Name;
    public string Short(int c) => c == You ? _club.ShortName : S.Clubs[c].Short;
    public Crest Crest(int c) => c == You ? _club.S.Crest : S.Clubs[c].Crest;
    public int Color(int c) => c == You ? _club.S.Kit.Main : S.Clubs[c].Main;
    public int Color2(int c) => c == You ? _club.S.Kit.Secondary : S.Clubs[c].Secondary;
    public string Manager(int c) => c == You ? _club.S.Coach.Name : S.Clubs[c].Manager;

    /// <summary>The home ground's name, for the paper and the previews.</summary>
    public string GroundName(int c)
    {
        if (c != You) return S.Clubs[c].Ground;
        var words = _club.S.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (words.Length > 0 ? words[0] : "Home") + " Park";
    }

    public int Rating(int c) => c == You ? _club.TeamRating() : RatingOf(S.Clubs[c]);

    static int RatingOf(LClub c)
    {
        var f = Formations.ById(c.Formation);
        double sum = 0;
        for (int i = 0; i < 11 && i < c.Squad.Count; i++) sum += Cards.RatingIn(c.Squad[i], f.Slots[i].Pos);
        return Cards.JsRound(sum / 11);
    }

    static bool Attacker(Position p) => p is Position.ST or Position.LW or Position.RW or Position.CAM or Position.LM or Position.RM;
    static bool Defender(Position p) => p is Position.GK or Position.CB or Position.LB or Position.RB or Position.CDM;

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
            var cl = S.Clubs[c];
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

    // ---------------------------------------------------------------- a new league

    /// <summary>A new league (this one again if none is given): fifteen clubs from its countries
    /// at its level, a few giants, a crowded middle and some minnows.</summary>
    public void Start(LeagueDef def = null)
    {
        def ??= Def;
        var seed = (int)(ClubState.Now & 0x7fffffff);
        var r = new Random(seed);
        S = new LeagueSave
        {
            Seed = seed,
            LeagueId = def.Id,
            Name = def.Name,
            Paper = Names.Papers[r.Next(Names.Papers.Length)],
        };
        var towns = new HashSet<string>();
        var shorts = new HashSet<string> { _club.ShortName };
        S.Clubs.Add(new LClub { Name = "", Venue = "custom", Founded = int.TryParse(_club.S.Crest.Year, out var y) ? y : 1899 });
        double[] spread = { 9, 7.5, 6, 5, 4, 3, 2, 1, 0, -1, -2, -3, -4.5, -6, -7.5 };
        int i = 0;
        foreach (double off in spread.OrderBy(_ => r.Next()))
            S.Clubs.Add(NewClub(r, towns, shorts, Math.Clamp(def.Level + off + r.NextDouble() * 2 - 1, 46, 93), CountryFor(def, i++, r)));
        S.Fixtures = Schedule(r);
        Save();
    }

    /// <summary>Clubs come from the league's countries in turn, so a regional league is a real mix.</summary>
    static Country CountryFor(LeagueDef def, int i, Random r) =>
        Ladder.CountryOf(def.Countries.Length == 1 ? def.Countries[0] : i < def.Countries.Length * 2 ? def.Countries[i % def.Countries.Length] : def.Countries[r.Next(def.Countries.Length)]);

    static int Pal(Random r) => Club.Crest.Palette[r.Next(Club.Crest.Palette.Length)];

    static double Lum(int c) => 0.299 * ((c >> 16) & 255) + 0.587 * ((c >> 8) & 255) + 0.114 * (c & 255);

    static double ColorDist(int a, int b) =>
        Math.Sqrt(Math.Pow(((a >> 16) & 255) - ((b >> 16) & 255), 2) + Math.Pow(((a >> 8) & 255) - ((b >> 8) & 255), 2) + Math.Pow((a & 255) - (b & 255), 2));

    LClub NewClub(Random r, HashSet<string> towns, HashSet<string> shorts, double level, Country co)
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

    static Rarity RarityFor(int o) => o >= 88 ? Rarity.Icon : o >= 82 ? Rarity.Legendary : o >= 74 ? Rarity.Epic : o >= 64 ? Rarity.Rare : Rarity.Common;

    static readonly Position[] BenchPos = { Position.GK, Position.CB, Position.RB, Position.CM, Position.CAM, Position.LW, Position.ST };

    /// <summary>A full squad at this level: the eleven in their slots, then seven on the bench.</summary>
    static void Recruit(LClub c, Random r, double level)
    {
        var rng = new Rng(r.Next());
        var f = Formations.ById(c.Formation);
        c.Squad.Clear();
        for (int i = 0; i < 11; i++)
            c.Squad.Add(Sign(rng, f.Slots[i].Pos, level + rng.Gauss() * 3));
        foreach (var p in BenchPos) c.Squad.Add(Sign(rng, p, level - 4 + rng.Gauss() * 3));
    }

    static Card Sign(Rng rng, Position p, double ovr)
    {
        int o = (int)Math.Clamp(Math.Round(ovr), 45, 95);
        return Cards.Generate(rng, RarityFor(o), p, o);
    }

    /// <summary>Double round robin by the circle method: each club meets every other once in the
    /// first half and again with home and away swapped, alternating home and away as it goes.</summary>
    static List<Fixture> Schedule(Random r)
    {
        var order = Enumerable.Range(0, Clubs).OrderBy(_ => r.Next()).ToList();
        var list = new List<Fixture>();
        var t = new List<int>(order);
        for (int round = 0; round < Clubs - 1; round++)
        {
            for (int i = 0; i < PerRound; i++)
            {
                int a = t[i], b = t[Clubs - 1 - i];
                bool aHome = i == 0 ? round % 2 == 0 : (i + round) % 2 == 0;
                list.Add(new Fixture { Round = round, Home = aHome ? a : b, Away = aHome ? b : a });
            }
            // Rotate everyone but the first.
            var last = t[^1];
            t.RemoveAt(Clubs - 1);
            t.Insert(1, last);
        }
        foreach (var f in list.ToList())
            list.Add(new Fixture { Round = f.Round + Clubs - 1, Home = f.Away, Away = f.Home });
        return list;
    }

    // ---------------------------------------------------------------- queries

    public IEnumerable<Fixture> RoundOf(int round) => S.Fixtures.Where(f => f.Round == round);

    public Fixture YourFixture(int round) => S.Fixtures.First(f => f.Round == round && f.Has(You));

    public Fixture Next => SeasonOver ? null : YourFixture(S.Round);

    /// <summary>The table after `upTo` matchdays (all played so far by default).</summary>
    public List<Row> Table(int upTo = int.MaxValue)
    {
        var rows = Enumerable.Range(0, Clubs).Select(i => new Row { Club = i }).ToArray();
        foreach (var f in S.Fixtures.Where(f => f.Played && f.Round < upTo).OrderBy(f => f.Round))
        {
            Book(rows[f.Home], f.Hg, f.Ag);
            Book(rows[f.Away], f.Ag, f.Hg);
        }
        return Sort(rows);
    }

    /// <summary>Live: the table with this matchday's goals counted up to `minute` (the `full`
    /// club's match, already played, counts in full).</summary>
    public List<Row> LiveTable(int round, int minute, int full = -1)
    {
        var rows = Table(round).Select(x => new Row { Club = x.Club, P = x.P, W = x.W, D = x.D, L = x.L, Gf = x.Gf, Ga = x.Ga, Pts = x.Pts, Form = x.Form }).ToArray();
        var byClub = rows.ToDictionary(x => x.Club);
        foreach (var f in RoundOf(round).Where(f => f.Played))
        {
            bool all = f.Has(full) || f.Forfeit && minute >= 90;
            int h = all ? f.Hg : f.Forfeit ? 0 : f.Goals.Count(g => g.Club == f.Home && g.Minute <= minute);
            int a = all ? f.Ag : f.Forfeit ? 0 : f.Goals.Count(g => g.Club == f.Away && g.Minute <= minute);
            Book(byClub[f.Home], h, a);
            Book(byClub[f.Away], a, h);
        }
        return Sort(rows);
    }

    static void Book(Row r, int gf, int ga)
    {
        r.P++;
        r.Gf += gf;
        r.Ga += ga;
        if (gf > ga) { r.W++; r.Pts += 3; r.Form += "W"; }
        else if (gf == ga) { r.D++; r.Pts += 1; r.Form += "D"; }
        else { r.L++; r.Form += "L"; }
    }

    List<Row> Sort(IEnumerable<Row> rows) =>
        rows.OrderByDescending(x => x.Pts).ThenByDescending(x => x.Gd).ThenByDescending(x => x.Gf).ThenBy(x => Name(x.Club), StringComparer.Ordinal).ToList();

    public int Place(int club, List<Row> table = null) => (table ?? Table()).FindIndex(x => x.Club == club) + 1;

    /// <summary>The last five results, oldest first.</summary>
    public string Form(int club, int upTo = int.MaxValue)
    {
        var s = string.Concat(S.Fixtures.Where(f => f.Played && f.Round < upTo && f.Has(club)).OrderBy(f => f.Round).Select(f => f.ResultFor(club)));
        return s.Length > 5 ? s[^5..] : s;
    }

    /// <summary>Goals per player this season (club, name), most first.</summary>
    public List<(int club, string name, int goals)> Scorers() =>
        S.Fixtures.Where(f => f.Played).SelectMany(f => f.Goals)
            .GroupBy(g => (g.Club, g.Player)).Select(g => (g.Key.Club, g.Key.Player, g.Count()))
            .OrderByDescending(x => x.Item3).ThenBy(x => x.Player, StringComparer.Ordinal).ToList();

    public static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "TH" : (n % 10) switch { 1 => "ST", 2 => "ND", 3 => "RD", _ => "TH" });

    /// <summary>The date of a matchday: Saturdays from mid-August.</summary>
    public DateTime Date(int round)
    {
        var d = new DateTime(2025 + S.Season, 8, 8);
        while (d.DayOfWeek != DayOfWeek.Saturday) d = d.AddDays(1);
        // A winter break after the first half.
        return d.AddDays(7 * round + (round >= 15 ? 14 : 0));
    }

    // ---------------------------------------------------------------- kits and line-ups

    public TeamInfo Info(int c, bool change = false)
    {
        if (c == You) return _club.Info();
        var cl = S.Clubs[c];
        var gk = TeamData.Default[1].Kit;
        return new TeamInfo
        {
            Name = cl.Name, Short = cl.Short,
            Kit = change
                ? new Kit { Shirt = cl.AwayMain, Shirt2 = cl.AwaySecondary, Shorts = cl.AwayShorts, Socks = cl.AwayMain, GkShirt = gk.GkShirt, GkShorts = gk.GkShorts, Pattern = cl.AwayPattern }
                : new Kit { Shirt = cl.Main, Shirt2 = cl.Secondary, Shorts = cl.Shorts, Socks = cl.Main, GkShirt = gk.GkShirt, GkShorts = gk.GkShorts, Pattern = cl.Pattern },
        };
    }

    static bool Clash(Kit a, Kit b) => ColorDist(a.Shirt, b.Shirt) < 120 || ColorDist(a.Shorts, b.Shorts) < 50 && ColorDist(a.Shirt, b.Shirt) < 170;

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
    static Kit Fallback(int against, Kit k)
    {
        bool white = ColorDist(against, 0xf1ebdc) > ColorDist(against, 0x23262e);
        int body = white ? 0xf1ebdc : 0x23262e;
        return new Kit { Shirt = body, Shirt2 = k.Shirt, Shorts = body, Socks = body, GkShirt = k.GkShirt, GkShorts = k.GkShorts, Pattern = 0 };
    }

    TeamSetup TeamOf(int c)
    {
        var cl = S.Clubs[c];
        var f = Formations.ById(cl.Formation);
        var t = new TeamSetup { Info = Info(c) };
        int top = -1;
        for (int i = 0; i < 11; i++)
        {
            var slot = f.Slots[i];
            var card = cl.Squad[i];
            var sp = Cards.ToSim(card, slot.Pos);
            t.Players.Add(new SetupPlayer { Name = sp.Name, Number = sp.Number, Attrs = sp.Attrs, Look = sp.Look, Foot = sp.Foot, Role = Cards.RoleOf(slot.Pos), X = slot.X, Z = slot.Z });
            if (card.Overall > top)
            {
                top = card.Overall;
                t.Captain = i;
            }
        }
        return t;
    }

    /// <summary>The name of the man in shirt slot `index` of side `team` in your match.</summary>
    public string Scorer(Fixture f, int team, int index)
    {
        if (team == 0) return _club.Card(_club.S.Lineup.Slots[Math.Clamp(index, 0, 10)])?.Name ?? "";
        var sq = S.Clubs[f.Other(You)].Squad;
        return index >= 0 && index < sq.Count ? sq[index].Name : "";
    }

    /// <summary>A club's own stadium, the same on every visit.</summary>
    public GameNight.Grounds.Build.StadiumPlan PlanOf(int c)
    {
        if (c == You) return _club.S.Stadium;
        var cl = S.Clubs[c];
        if (cl.Plan?.Sets == null)
        {
            cl.Plan = Stadia.Make(cl, RatingOf(cl));
            Save();
        }
        return cl.Plan;
    }

    /// <summary>Their best player: the one to watch.</summary>
    public Card Star(int c) => c == You ? _club.Starters().Where(x => x != null).OrderByDescending(x => x.Overall).FirstOrDefault() : S.Clubs[c].Squad.Take(11).OrderByDescending(x => x.Overall).First();

    // ---------------------------------------------------------------- playing a matchday

    /// <summary>Your result is in: book it, play the rest of the matchday, move on.</summary>
    public void Complete(int hg, int ag, List<GoalNote> goals, bool forfeit = false)
    {
        var f = Next;
        if (f == null) return;
        f.Hg = hg;
        f.Ag = ag;
        f.Forfeit = forfeit;
        f.Goals = (goals ?? new()).OrderBy(g => g.Minute).ToList();
        f.Crowd = Crowd(f, new Random(S.Seed + S.Round * 31));
        var r = new Random(S.Seed ^ (S.Round * 7919 + S.Season * 104729));
        foreach (var o in RoundOf(S.Round).Where(x => !x.Has(You))) Settle(o, r);
        S.Round++;
        Save();
    }

    /// <summary>Your match without playing it: the same model as everyone else's.</summary>
    public void Simulate()
    {
        var f = Next;
        if (f == null) return;
        var r = new Random(S.Seed ^ (S.Round * 4099 + 17));
        var tmp = new Fixture { Home = f.Home, Away = f.Away, Round = f.Round };
        Settle(tmp, r);
        Complete(tmp.Hg, tmp.Ag, tmp.Goals);
    }

    static double Poisson(Random r, double lambda)
    {
        double l = Math.Exp(-lambda), p = 1;
        int k = 0;
        do { k++; p *= r.NextDouble(); } while (p > l && k < 12);
        return k - 1;
    }

    static double Gauss(Random r) => (r.NextDouble() + r.NextDouble() + r.NextDouble() + r.NextDouble() - 2) * 1.7320508;

    int StyleOf(int c) => c == You ? 0 : S.Clubs[c].Style;

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

    void Settle(Fixture f, Random r)
    {
        var (lh, la) = Expected(f.Home, f.Away);
        f.Hg = (int)Poisson(r, lh * Math.Exp(Gauss(r) * 0.15));
        f.Ag = (int)Poisson(r, la * Math.Exp(Gauss(r) * 0.15));
        f.Goals = new();
        for (int i = 0; i < f.Hg; i++) f.Goals.Add(new GoalNote { Club = f.Home, Player = PickScorer(f.Home, r), Minute = Minute(r) });
        for (int i = 0; i < f.Ag; i++) f.Goals.Add(new GoalNote { Club = f.Away, Player = PickScorer(f.Away, r), Minute = Minute(r) });
        f.Goals = f.Goals.OrderBy(g => g.Minute).ToList();
        f.Crowd = Crowd(f, r);
    }

    /// <summary>Goals come a little more often late on, and now and then in stoppage time.</summary>
    static int Minute(Random r)
    {
        double u = Math.Pow(r.NextDouble(), 0.85);
        int m = 1 + (int)(u * 90);
        if (m >= 90 && r.Next(2) == 0) m = 90 + 1 + r.Next(5);
        return Math.Min(m, 95);
    }

    static double ScoreWeight(Position p) => p switch
    {
        Position.ST => 5, Position.LW or Position.RW => 3.4, Position.CAM => 3, Position.LM or Position.RM => 2.2,
        Position.CM => 1.5, Position.CDM => 0.7, Position.CB => 0.7, Position.LB or Position.RB => 0.5, _ => 0,
    };

    string PickScorer(int c, Random r)
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
            xi = S.Clubs[c].Squad.Take(11).ToArray();
            pos = Formations.ById(S.Clubs[c].Formation).Slots.Select(s => s.Pos).ToArray();
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

    /// <summary>The gate: fuller for a big match between high-flyers, thinner late in a poor season.</summary>
    int Crowd(Fixture f, Random r)
    {
        int cap = f.Home == You ? 42000 : S.Clubs[f.Home].Capacity;
        var t = Table(f.Round);
        double top = 1 - (Place(f.Home, t) + Place(f.Away, t)) / 34.0;
        double fill = Math.Clamp(0.62 + top * 0.3 + r.NextDouble() * 0.14, 0.45, 1);
        return (int)(cap * fill);
    }

    // ---------------------------------------------------------------- the end of a season

    /// <summary>Prize money for where you finished (once).</summary>
    public int PayPrize()
    {
        if (!SeasonOver || S.PrizePaid) return 0;
        int pos = Place(You);
        S.PrizePaid = true;
        if (pos == 1) S.Titles++;
        if (C != null)
        {
            var id = Def.Id;
            if (pos == 1)
            {
                C.Trophies++;
                C.Titles[id] = TitlesIn(Def) + 1;
            }
            if (BestIn(Def) == 0 || pos < BestIn(Def)) C.Best[id] = pos;
            C.Seasons++;
            SaveCareer();
        }
        Save();
        return PrizeFor(pos);
    }

    /// <summary>Into the next season: the bottom three (yours excepted) go down and three new
    /// clubs come up; everyone else signs a few players, held near the league's level.</summary>
    public List<string> NextSeason()
    {
        var news = new List<string>();
        var table = Table();
        var top = Scorers().FirstOrDefault();
        S.History.Add(new SeasonRow
        {
            Season = S.Season, Champion = Name(table[0].Club), ChampionCrest = Crest(table[0].Club).Clone(),
            UserPos = Place(You, table), UserPts = table.First(x => x.Club == You).Pts,
            TopScorer = top.name ?? "", TopGoals = top.goals,
        });
        var r = new Random(S.Seed + S.Season * 7777);
        var def = Def;
        double level = def.Level;
        var down = table.Where(x => x.Club != You).TakeLast(3).Select(x => x.Club).ToList();
        var towns = new HashSet<string>(S.Clubs.Skip(1).Select(c => c.Town));
        var shorts = new HashSet<string>(S.Clubs.Skip(1).Select(c => c.Short)) { _club.ShortName };
        foreach (var c in S.Clubs) c.Promoted = false;
        foreach (int d in down)
        {
            news.Add($"{S.Clubs[d].Name} relegated");
            towns.Remove(S.Clubs[d].Town);
            var n = NewClub(r, towns, shorts, Math.Clamp(level - 2 + Gauss(r) * 2.5, 46, 93), CountryFor(def, 99, r));
            n.Promoted = true;
            S.Clubs[d] = n;
            news.Add($"{n.Name} promoted");
        }
        for (int i = 1; i < Clubs; i++)
        {
            var c = S.Clubs[i];
            if (c.Promoted) continue;
            int rating = RatingOf(c);
            // Pulled a little toward the league's level, plus some luck, so the pecking order shifts.
            double target = rating + (level - rating) * 0.2 + Gauss(r) * 2.2;
            var rng = new Rng(r.Next());
            var f = Formations.ById(c.Formation);
            for (int k = 0; k < 3; k++)
            {
                int slot = r.Next(11);
                c.Squad[slot] = Sign(rng, f.Slots[slot].Pos, target + rng.Gauss() * 2);
            }
            // Everyone else matures or fades toward the target too.
            for (int k = 0; k < 11; k++)
                if (Math.Abs(c.Squad[k].Overall - target) > 6) c.Squad[k] = Sign(rng, f.Slots[k].Pos, target + rng.Gauss() * 2.5);
        }
        S.Season++;
        S.Round = 0;
        S.PrizePaid = false;
        S.Fixtures = Schedule(r);
        Save();
        return news;
    }

    public void Abandon()
    {
        S = null;
        try { File.Delete(_path); } catch { }
    }
}
