using System;
using System.Collections.Generic;
using System.Linq;

namespace GameNight.League;

public enum Photo { Celebrate, Draw, Despair, Trophy }

/// <summary>One edition of the league's paper: the morning after a matchday.</summary>
public sealed class Paper
{
    public int Round;
    public string Masthead = "", Dateline = "", Kicker = "", Headline = "", Standfirst = "", Caption = "", Hero = "";
    public List<string> Body = new();
    public Photo Photo;
    public Fixture Yours;
    public List<Fixture> Others = new();
    public List<Row> Table = new();
    public int Before, After;
}

/// <summary>
/// Writes the paper from what happened: a headline that knows the story (a late winner, a
/// hat-trick, going top, a hiding, a giant-killing), the goals in order with the running score,
/// what it means for the table, a word from each manager, and the rest of the matchday.
/// </summary>
public static class Gazette
{
    /// <summary>The words before the town in every club name style ("Real", "Sporting de", "SV"...).</summary>
    static readonly HashSet<string> Prefixes = new(new[] { "Real", "Sporting", "Atlético", "Dynamo", "Inter", "Olympique", "Racing", "Union", "Lokomotiv", "Académica", "Deportivo", "Vitória", "Stella", "Fortuna", "AC", "FC", "The",
            "Estádio", "Estadio", "Stadio", "Stade", "Stadion", "Campo", "Parc", "Sportpark", "Gradski", "Municipal", "Comunale", "Miejski", "Idrottsparken", "Nuevo", "Old", "do", "de", "la", "an", "der" }
        .Concat(Ladder.Countries.SelectMany(c => c.Clubs).Where(p => !p.StartsWith("{T}")).SelectMany(p => p[..p.IndexOf("{T}")].Split(' ', StringSplitOptions.RemoveEmptyEntries))),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>What a headline calls a club: its town, not its suffix ("Ashford", not "United").</summary>
    public static string Word(string name)
    {
        var w = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length == 0) return name;
        foreach (var x in w.Take(w.Length - 1))
            if (!Prefixes.Contains(x)) return x;
        return w[^1];
    }

    static string Last(string name) => name.Contains(' ') ? name[(name.LastIndexOf(' ') + 1)..] : name;

    static string Min(int m) => m > 90 ? $"90+{m - 90}" : m.ToString();

    static string Num(int n) => n switch { 2 => "two", 3 => "three", 4 => "four", 5 => "five", 6 => "six", _ => n.ToString() };

    public static Paper Make(LeagueState lg, int round)
    {
        var r = new Random(lg.S.Seed + round * 131 + lg.S.Season * 7);
        string P(params string[] a) => a[r.Next(a.Length)];
        const int you = LeagueState.You;
        var f = lg.YourFixture(round);
        int opp = f.Other(you);
        bool home = f.Home == you;
        int gf = f.GoalsFor(you), ga = f.GoalsAgainst(you);
        var before = lg.Table(round);
        var after = lg.Table(round + 1);
        int pb = lg.Place(you, before), pa = lg.Place(you, after);
        int ob = lg.Place(opp, before);
        string us = lg.Name(you), them = lg.Name(opp);
        string US = Word(us).ToUpperInvariant(), THEM = Word(them).ToUpperInvariant();
        string ground = lg.GroundName(f.Home);
        var mine = f.Goals.Where(g => g.Club == you).ToList();
        var theirs = f.Goals.Where(g => g.Club == opp).ToList();
        var byPlayer = mine.GroupBy(g => g.Player).OrderByDescending(g => g.Count()).ToList();
        var lastGoal = f.Goals.LastOrDefault();
        bool lateWinner = gf == ga + 1 && lastGoal != null && lastGoal.Club == you && lastGoal.Minute >= 85;
        bool lateLoser = ga == gf + 1 && lastGoal != null && lastGoal.Club == opp && lastGoal.Minute >= 85;
        char res = f.ResultFor(you);

        // Has anyone clinched the title this matchday?
        int left = LeagueState.Rounds - (round + 1);
        bool Clinched(List<Row> t, int l) => t.Count > 1 && t[0].Pts - t[1].Pts > 3 * l;
        bool clinchedNow = Clinched(after, left) && !Clinched(before, left + 1);
        bool champions = clinchedNow && after[0].Club == you;

        var p = new Paper
        {
            Round = round, Masthead = lg.S.Paper, Yours = f, Before = pb, After = pa, Table = after,
            Others = lg.RoundOf(round).Where(x => !x.Has(you)).ToList(),
            Dateline = lg.Date(round).AddDays(1).ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant(),
            Kicker = $"{lg.S.Name.ToUpperInvariant()} · MATCHDAY {round + 1}",
        };

        // ---- the headline
        if (champions) p.Headline = P("CHAMPIONS!", $"{US} ARE CHAMPIONS!", "THE TITLE IS OURS!");
        else if (f.Forfeit) p.Headline = P("WALK-OFF SHAME", $"{US} WALK OFF", "FARCE AS TEAM LEAVES PITCH");
        else if (byPlayer.Count > 0 && byPlayer[0].Count() >= 3) p.Headline = P($"{Last(byPlayer[0].Key).ToUpperInvariant()} HAT-TRICK HERO", $"HAT-TRICK FOR {Last(byPlayer[0].Key).ToUpperInvariant()}", $"{Last(byPlayer[0].Key).ToUpperInvariant()} TAKES THE BALL HOME");
        else if (res == 'W' && pa == 1 && pb != 1 && round >= 2) p.Headline = P("TOP OF THE LEAGUE!", $"{US} GO TOP", $"{US} HIT THE SUMMIT");
        else if (res == 'W' && gf - ga >= 3) p.Headline = P($"{US} RUN RIOT", $"{Num(gf).ToUpperInvariant()}-STAR {US}", $"{THEM} HUMBLED", $"{US} HIT {THEM} FOR {Num(gf).ToUpperInvariant()}");
        else if (lateWinner) p.Headline = P("LATE, LATE SHOW!", $"{Last(lastGoal.Player).ToUpperInvariant()} WINS IT AT THE DEATH", "LAST-GASP GLORY");
        else if (res == 'W' && ob <= pb - 4 && round > 0) p.Headline = P("GIANT KILLERS!", $"{US} STUN {THEM}", $"{THEM} TOPPLED");
        else if (res == 'W' && ga == 0) p.Headline = P($"{US} SHUT OUT {THEM}", $"CLEAN SHEET, SWEET WIN", $"{US} BLANK {THEM}");
        else if (res == 'W') p.Headline = gf - ga == 1 ? P($"{US} EDGE {THEM}", $"{US} HOLD ON", "NARROW BUT NEEDED") : P($"{US} SEE OFF {THEM}", $"{US} BEAT {THEM}", $"JOY FOR {US}");
        else if (res == 'D' && gf == 0) p.Headline = P("STALEMATE", "NOTHING TO SEPARATE THEM", $"GOALLESS AT {Word(ground).ToUpperInvariant()}");
        else if (res == 'D' && gf >= 3) p.Headline = P($"{Num(gf + ga).ToUpperInvariant()}-GOAL THRILLER", "WHAT A GAME!", "ALL SQUARE IN A CLASSIC");
        else if (res == 'D') p.Headline = P("HONOURS EVEN", $"{US} AND {THEM} SHARE THE SPOILS", "A POINT APIECE");
        else if (pa == LeagueState.Clubs && pb != LeagueState.Clubs) p.Headline = P($"{US} HIT ROCK BOTTOM", "BOTTOM OF THE PILE");
        else if (ga - gf >= 3) p.Headline = P($"NIGHTMARE FOR {US}", $"{THEM} RUN RIOT", $"{US} HAMMERED");
        else if (lateLoser) p.Headline = P("HEARTBREAK!", "SUCKER PUNCH", $"LATE AGONY FOR {US}");
        else if (ob >= pb + 5 && round > 0) p.Headline = P($"SHOCK DEFEAT FOR {US}", $"{THEM} STUN {US}", "UPSET!");
        else p.Headline = P($"{THEM} EDGE {US}", $"{US} FALL AT {Word(ground).ToUpperInvariant()}", $"NO JOY FOR {US}");

        // ---- the standfirst
        string Scorers(List<IGrouping<string, GoalNote>> g)
        {
            var parts = g.Select(x => x.Count() >= 3 ? $"a {Last(x.Key)} hat-trick" : x.Count() == 2 ? $"two from {Last(x.Key)}" : Last(x.Key)).ToList();
            return parts.Count <= 1 ? string.Concat(parts) : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
        }
        string at = $"at {ground}" + (f.Crowd > 0 ? $" in front of {f.Crowd:#,0}" : "");
        if (f.Forfeit) p.Standfirst = $"{us} left the field at {ground} and the points were handed to {them}, the match booked as a 0-3 defeat.";
        else if (res == 'W') p.Standfirst = (byPlayer.Count > 0 ? $"{Cap(Scorers(byPlayer))} {(mine.Count > 1 && byPlayer.Count > 1 ? "were" : "was")} enough as " : "") + $"{us} beat {them} {gf}-{ga} {at}.";
        else if (res == 'D') p.Standfirst = gf == 0 ? $"{us} and {them} could not be separated in a goalless draw {at}." : $"{Cap(Scorers(byPlayer))} {(byPlayer.Count > 1 ? "were" : "was")} not enough to win it: {us} drew {gf}-{ga} with {them} {at}.";
        else p.Standfirst = $"{us} went down {ga}-{gf} to {them} {at}" + (byPlayer.Count > 0 ? $", despite {Scorers(byPlayer)}." : ".");

        // ---- the goals, in order
        if (!f.Forfeit && f.Goals.Count > 0)
        {
            var lines = new List<string>();
            int h = 0, a = 0;
            foreach (var g in f.Goals.Take(6))
            {
                bool ours = g.Club == you;
                if (ours) h++; else a++;
                string who = Last(g.Player);
                string side = ours ? us : them;
                string score = home ? $"{h}-{a}" : $"{a}-{h}";
                string line = (h + a) switch
                {
                    1 => $"{who} opened the scoring on {Min(g.Minute)} minutes",
                    _ when h == a => $"{who} levelled for {Word(side)} on {Min(g.Minute)}",
                    _ when ours && h == a + 1 && g.Minute >= 80 => $"{who} struck what proved the winner on {Min(g.Minute)}",
                    _ => P($"{who} made it {score} on {Min(g.Minute)}", $"{who} added another on {Min(g.Minute)} ({score})", $"{Min(g.Minute)} minutes: {who}, {score}"),
                };
                lines.Add(line);
            }
            p.Body.Add(Cap(string.Join(". ", lines)) + (f.Goals.Count > 6 ? ", and still the goals kept coming." : "."));
        }
        else if (!f.Forfeit) p.Body.Add(P("Chances were few and the keepers rarely tested.", "Both defences stood firm on a night for the purists.", "A tight, tense affair that never quite caught fire."));

        // ---- what it means
        var me = after.First(x => x.Club == you);
        int gap = after[0].Pts - me.Pts;
        string where = pa == 1
            ? (after.Count > 1 ? $"{after[0].Pts - after[1].Pts} point{(after[0].Pts - after[1].Pts == 1 ? "" : "s")} clear at the top" : "top")
            : $"{gap} point{(gap == 1 ? "" : "s")} off the top";
        string move = round == 0 || pa == pb ? "" : pa < pb ? $", up {Num(pb - pa)} place{(pb - pa == 1 ? "" : "s")}" : $", down {Num(pa - pb)} place{(pa - pb == 1 ? "" : "s")}";
        string table = $"The result leaves {us} {LeagueState.Ordinal(pa).ToLowerInvariant()} with {me.Pts} point{(me.Pts == 1 ? "" : "s")}{move}, {where}.";
        if (round + 1 < LeagueState.Rounds)
        {
            var next = lg.YourFixture(round + 1);
            table += $" Next up: {lg.Name(next.Other(you))}, {(next.Home == you ? "at home" : "away")}.";
        }
        p.Body.Add(table);

        // ---- a word from the dugouts
        string coach = lg.Manager(you);
        string quote = res switch
        {
            'W' => P("The lads were magnificent. That's what this club is about.", "We stuck to the plan and the goals came. I'm proud of them.", "Three points, a clean conscience. On to the next one.", "The fans were our twelfth man tonight."),
            'D' => P("A point is a point. We'll take it and move on.", "We should have won it, but there's character in this group.", "Frustrating. We had the chances to finish them."),
            _ => P("Not good enough. I take responsibility.", "We'll look at the tape and put it right.", "Football can be cruel. We go again.", "I won't hide behind excuses: we were second best."),
        };
        string reply = res switch
        {
            'W' => P("They punished our mistakes.", "We gave them too much space.", "Credit to them. We'll be back."),
            'D' => P("A fair result, I think.", "We came for three and leave with one."),
            _ => P("A huge win for us. The players deserve it.", "We knew where we could hurt them.", "That's the best we've played all season."),
        };
        if (!f.Forfeit) p.Body.Add($"\"{quote}\" said {coach}. {lg.Manager(opp)} of {Word(them)}: \"{reply}\"");

        // ---- the picture
        var hero = byPlayer.Count > 0 ? byPlayer[0].Key : lg.Star(you)?.Name ?? us;
        p.Hero = hero;
        p.Photo = champions ? Photo.Trophy : res == 'W' ? Photo.Celebrate : res == 'D' ? Photo.Draw : Photo.Despair;
        p.Caption = p.Photo switch
        {
            Photo.Trophy => $"{Last(hero)} and {us} celebrate the title",
            Photo.Celebrate => byPlayer.Count > 0 ? $"{Last(hero)} wheels away after scoring" : $"Joy for {us} at the final whistle",
            Photo.Draw => $"Honours even at {ground}",
            _ => $"Dejection for {us}",
        };
        return p;
    }

    static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
