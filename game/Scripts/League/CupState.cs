using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GameNight.Club;

namespace GameNight.League;

public sealed class CupSave
{
    public int V = 1;
    public string CupId = "";
    public int Seed;
    /// <summary>Your league's level and prize scale when the draw was made (a national cup follows them).</summary>
    public double Level, PrizeScale = 1;
    public List<LClub> Clubs = new();
    /// <summary>Every tie so far, round by round; a round's ties are drawn when the one before is done.</summary>
    public List<Fixture> Ties = new();
    /// <summary>The next round to play; Rounds = the cup is over.</summary>
    public int Round;
    /// <summary>Knocked out in this round (-1: still in, or won it).</summary>
    public int Out = -1;
    public bool PrizePaid;
}

/// <summary>
/// A cup run: a straight knockout of 16 (or 8) clubs, one match a round, level ties settled on
/// penalties. Your ties are played (or watched, or settled on the numbers); the rest of each round
/// is settled by the same goals model as the league. Knocked out, the cup plays on without you.
/// One run at a time, saved beside the league; each cup can be entered once a season.
/// </summary>
public sealed class CupState : ClubPool
{
    const string SaveFile = "cup.json";
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    /// <summary>Share of the winners' prize for going out N rounds short of the trophy.</summary>
    static readonly double[] PrizeShare = { 1, 0.45, 0.22, 0.1, 0.04 };

    public CupSave S;
    readonly string _path;
    readonly LeagueState _league;

    public CupState(string dir, ClubState club, LeagueState league) : base(club)
    {
        _league = league;
        _path = Path.Combine(dir, SaveFile);
        S = Load();
    }

    protected override List<LClub> Pool => S.Clubs;

    public bool Active => S != null;
    public CupDef Def => Ladder.CupById(S?.CupId ?? "");
    public int Rounds => Def?.Rounds ?? 4;
    public bool Over => S != null && S.Round >= Rounds;
    public bool Won => Over && S.Out < 0;
    public bool Running(CupDef d) => S != null && S.CupId == d.Id;

    CupSave Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var s = JsonSerializer.Deserialize<CupSave>(File.ReadAllText(_path), Json);
            var d = s != null ? Ladder.CupById(s.CupId) : null;
            return d != null && s.V == 1 && s.Clubs?.Count == d.Size ? s : null;
        }
        catch
        {
            return null;
        }
    }

    public override void Save()
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

    public void Abandon()
    {
        S = null;
        try { File.Delete(_path); } catch { }
    }

    // ---------------------------------------------------------------- the career side

    public int Wins(CupDef d) => _league.C != null && _league.C.Cups.TryGetValue(d.Id, out var n) ? n : 0;
    public int TotalWins => _league.C?.Cups.Values.Sum() ?? 0;

    /// <summary>Furthest run, in rounds short of the trophy; -1 never entered.</summary>
    public int Best(CupDef d) => _league.C != null && _league.C.CupBest.TryGetValue(d.Id, out var n) ? n : -1;

    public bool Unlocked(CupDef d) => _league.C != null && _league.C.Trophies >= d.Need;

    /// <summary>Already had this season's run at it.</summary>
    public bool Spent(CupDef d) => _league.C != null && _league.C.Entered.TryGetValue(d.Id, out var s) && s == _league.C.Seasons && !Running(d);

    /// <summary>How strong its clubs are: a national cup draws from every level of the country, as
    /// strong as your league on average; a special tournament has its own level.</summary>
    public double LevelOf(CupDef d) => d.Special ? d.Level : _league.Def.Level - 1;

    public int PrizeOf(CupDef d) => d.Special ? d.Prize : (int)Math.Round(12000 * _league.Def.PrizeScale / 100) * 100;

    public int TopPrize => Prize;

    int Prize => (int)Math.Round((Def.Special ? Def.Prize : 12000 * S.PrizeScale) / 100) * 100;

    // ---------------------------------------------------------------- the draw

    public void Enter(CupDef d)
    {
        var seed = (int)(ClubState.Now & 0x7fffffff);
        var r = new Random(seed);
        S = new CupSave { CupId = d.Id, Seed = seed, Level = LevelOf(d), PrizeScale = _league.Def.PrizeScale };
        var towns = new HashSet<string>();
        var shorts = new HashSet<string> { _club.ShortName };
        S.Clubs.Add(new LClub { Name = "", Venue = "custom" });
        var countries = d.Special ? Ladder.Countries.Select(c => c.Code).ToArray() : new[] { d.Country };
        var from = new LeagueDef { Countries = countries };
        // A cup is a mix: a few big clubs, plenty of the middle, some brave minnows.
        for (int i = 1; i < d.Size; i++)
        {
            double off = (d.Size - 1 - i * 2) * (d.Size == 8 ? 1.6 : 0.8) + Gauss(r) * 1.5;
            S.Clubs.Add(NewClub(r, towns, shorts, Math.Clamp(S.Level + off, 46, 93), CountryFor(from, i - 1, r)));
        }
        var order = Enumerable.Range(0, d.Size).OrderBy(_ => r.Next()).ToList();
        for (int k = 0; k < d.Size / 2; k++) S.Ties.Add(new Fixture { Round = 0, Home = order[k * 2], Away = order[k * 2 + 1] });
        if (_league.C != null)
        {
            _league.C.Entered[d.Id] = _league.C.Seasons;
            _league.SaveCareer();
        }
        Save();
    }

    // ---------------------------------------------------------------- queries

    public IEnumerable<Fixture> RoundOf(int round) => S.Ties.Where(f => f.Round == round);

    public Fixture Next => Over || S.Out >= 0 ? null : RoundOf(S.Round).FirstOrDefault(f => f.Has(You));

    public static int Winner(Fixture f) => f.Hg > f.Ag ? f.Home : f.Hg < f.Ag ? f.Away : f.Hp > f.Ap ? f.Home : f.Away;

    /// <summary>"Final", "Semi-finals"... counted back from the end.</summary>
    public string RoundName(int round, bool upper = true)
    {
        int left = Rounds - round;
        string s = left switch { 1 => "Final", 2 => "Semi-finals", 3 => "Quarter-finals", _ => "Round of 16" };
        return upper ? s.ToUpperInvariant() : s;
    }

    public bool Final(Fixture f) => f.Round == Rounds - 1;

    // ---------------------------------------------------------------- playing a round

    /// <summary>Your tie is in (level ones go to penalties): settle the rest of the round, draw the
    /// next. Knocked out, the cup is played to the end without you.</summary>
    public void Complete(int hg, int ag, List<GoalNote> goals, bool forfeit = false)
    {
        var f = Next;
        if (f == null) return;
        var r = new Random(S.Seed ^ (S.Round * 7919 + 31));
        f.Hg = hg;
        f.Ag = ag;
        f.Forfeit = forfeit;
        f.Goals = (goals ?? new()).OrderBy(g => g.Minute).ToList();
        f.Crowd = Crowd(f, r);
        if (hg == ag) Shootout(f, r);
        if (Winner(f) != You) S.Out = S.Round;
        PlayOut(r);
        while (S.Out >= 0 && !Over) PlayOut(r);
        Save();
    }

    /// <summary>Your tie without playing it: the same model as the others.</summary>
    public void Simulate()
    {
        var f = Next;
        if (f == null) return;
        var tmp = new Fixture { Home = f.Home, Away = f.Away, Round = f.Round };
        Score(tmp, new Random(S.Seed ^ (S.Round * 4099 + 17)));
        Complete(tmp.Hg, tmp.Ag, tmp.Goals);
    }

    /// <summary>Settle the round's other ties and draw the next round.</summary>
    void PlayOut(Random r)
    {
        foreach (var o in RoundOf(S.Round).Where(x => !x.Played))
        {
            Score(o, r);
            o.Crowd = Crowd(o, r);
            if (o.Hg == o.Ag) Shootout(o, r);
        }
        var won = RoundOf(S.Round).Select(Winner).ToList();
        S.Round++;
        if (Over) return;
        for (int k = 0; k + 1 < won.Count; k += 2)
        {
            // The lower seed in the bracket hosts; the final goes to the bigger ground.
            bool swap = r.Next(2) == 0;
            S.Ties.Add(new Fixture { Round = S.Round, Home = swap ? won[k + 1] : won[k], Away = swap ? won[k] : won[k + 1] });
        }
    }

    /// <summary>Five kicks each, then sudden death: the takers' shooting against the keeper.</summary>
    void Shootout(Fixture f, Random r)
    {
        double P(int taker, int keeper) => Math.Clamp(0.75 + (Power(taker).att - Power(keeper).def) * 0.006, 0.6, 0.9);
        double ph = P(f.Home, f.Away), pa = P(f.Away, f.Home);
        int h = 0, a = 0;
        for (int k = 0; k < 5; k++)
        {
            if (r.NextDouble() < ph) h++;
            if (h > a + (5 - k) || a > h + (4 - k)) break;
            if (r.NextDouble() < pa) a++;
            if (a > h + (4 - k) || h > a + (4 - k)) break;
        }
        while (h == a)
        {
            if (r.NextDouble() < ph) h++;
            if (r.NextDouble() < pa) a++;
        }
        f.Hp = h;
        f.Ap = a;
    }

    int Crowd(Fixture f, Random r)
    {
        int cap = f.Home == You ? 42000 : S.Clubs[f.Home].Capacity;
        double big = 0.55 + 0.1 * f.Round + r.NextDouble() * 0.15;
        return (int)(cap * Math.Clamp(big, 0.45, 1));
    }

    /// <summary>What a win and a draw are worth on the way.</summary>
    public (int win, int draw) Bonus => ((int)Math.Round(Prize * 0.05 / 50) * 50, (int)Math.Round(Prize * 0.02 / 50) * 50);

    /// <summary>Rounds short of the trophy: 0 won it, 1 lost the final...</summary>
    public int Short => S.Out < 0 ? 0 : Rounds - S.Out;

    public int PrizeNow => (int)Math.Round(Prize * PrizeShare[Math.Clamp(Short, 0, PrizeShare.Length - 1)] / 50) * 50;

    /// <summary>The prize for how far you got (once, when the cup is over), and the honours.</summary>
    public int PayPrize()
    {
        if (!Over || S.PrizePaid) return 0;
        S.PrizePaid = true;
        var c = _league.C;
        if (c != null)
        {
            if (Won) c.Cups[S.CupId] = Wins(Def) + 1;
            int best = Best(Def);
            if (best < 0 || Short < best) c.CupBest[S.CupId] = Short;
            _league.SaveCareer();
        }
        Save();
        return PrizeNow;
    }
}
