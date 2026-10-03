using System;

namespace GameNight.Sim;

public enum DrillKind { FreeKicks, Penalties, OneOnOne, TwoVTwo, Keeper }

public sealed class DrillInfo
{
    public DrillKind Id;
    public string Name = "", About = "";
}

public sealed class Verdict
{
    public string Title = "", Sub = "";
    /// <summary>Good for you, bad, or neither (a foul, time up): neither isn't counted.</summary>
    public bool? Good;

    public Verdict(string title, string sub, bool? good)
    {
        Title = title;
        Sub = sub;
        Good = good;
    }
}

/// <summary>
/// Training drills, run on a real match: the drill picks who takes part (Match.Field), lays out
/// each attempt, and judges how it ended. Everything in between is the ordinary simulation: the
/// same kicks, keepers, walls and tackles as a match. Call Step after every Match.Step.
///
/// The human is always team 0, attacking +x (the clock never runs, so ends never change).
/// </summary>
public sealed class Drill
{
    public static readonly DrillInfo[] All =
    {
        new DrillInfo { Id = DrillKind.FreeKicks, Name = "Free kicks", About = "A new spot every time, a wall and a keeper" },
        new DrillInfo { Id = DrillKind.Penalties, Name = "Penalties", About = "Twelve yards, you against the keeper" },
        new DrillInfo { Id = DrillKind.OneOnOne, Name = "One on one", About = "Through on goal: round him or slot it past" },
        new DrillInfo { Id = DrillKind.TwoVTwo, Name = "2 v 2", About = "Two on two with keepers: attack, then defend" },
        new DrillInfo { Id = DrillKind.Keeper, Name = "Goalkeeper", About = "You in goal: stick to move, any button to dive" },
    };

    public readonly Match M;
    public readonly DrillKind Kind;
    /// <summary>Rounds that went your way and rounds that didn't (goals for and against in 2 v 2,
    /// saves and goals let in for the keeper).</summary>
    public int Made, Against;
    /// <summary>Good rounds in a row, and the best run (kept by the caller between sessions).</summary>
    public int Streak, Best;
    /// <summary>The last attempt's outcome; Verdicts counts them, so the HUD sees a new one arrive.</summary>
    public Verdict? Verdict;
    public int Verdicts;
    /// <summary>Which side attacks this round.</summary>
    public int Att;
    int round;
    /// <summary>Seconds into this attempt, and since its verdict (-1 while it's live).</summary>
    double t;
    double doneT = -1;
    double hold = 1.5;
    bool keeperTouched, blocked, woodwork;
    double defOwnT;
    readonly int[] scores = new int[2];
    /// <summary>The club's dead-ball specialist (shirt slot), who takes the free kicks and penalties.</summary>
    readonly int specialist;

    public Drill(Match m, DrillKind kind, int best = 0)
    {
        M = m;
        Kind = kind;
        Best = best;
        m.Training = true;
        m.KeeperHuman = kind == DrillKind.Keeper;
        double bs = -1;
        specialist = 9;
        foreach (var p in m.All)
        {
            if (p.Team != 0 || p.Role == Role.GK) continue;
            double s = p.Attrs.Shooting * 0.7 + p.Attrs.Passing * 0.3;
            if (s > bs)
            {
                bs = s;
                specialist = p.Index;
            }
        }
        Next();
    }

    /// <summary>Is this attempt over (its verdict on screen)?</summary>
    public bool Done => doneT >= 0;

    /// <summary>The scoreboard line.</summary>
    public string Line =>
        Kind == DrillKind.TwoVTwo ? $"You {Made} – {Against} Them"
        : Kind == DrillKind.Keeper ? $"Saves {Made} · Let in {Against}"
        : $"{Made} / {Made + Against}";

    /// <summary>What this round asks of you (2 v 2 swaps ends of the task every round).</summary>
    public string Task => Kind == DrillKind.TwoVTwo ? (Att == 0 ? "Attack" : "Defend") : "";

    /// <summary>After every match step.</summary>
    public void Step()
    {
        var m = M;
        t += Tick.DT;
        if (doneT >= 0)
        {
            doneT += Tick.DT;
            if (doneT > hold) Next();
            return;
        }
        var lt = m.LastTouch;
        if (lt != null && lt.Team != Att)
        {
            if (lt.Role == Role.GK) keeperTouched = true;
            else blocked = true;
        }
        if (m.Events.Post > 0) woodwork = true;
        var v = Judge();
        if (v != null) Finish(v);
    }

    // ------------------------------------------------------------------ judging

    Verdict? Judge()
    {
        var m = M;
        int att = Att;
        int def = 1 - att;
        bool mine = att == m.HumanTeam;
        var b = m.Ball;
        bool deadBall = Kind == DrillKind.FreeKicks || Kind == DrillKind.Penalties;
        // Goal (an own goal counts for whoever it went in for).
        if (m.Phase == Phase.Goal)
        {
            bool forAtt = m.Teams[att].Score > scores[att];
            if (forAtt == mine) return new Verdict("GOAL", woodwork ? "In off the woodwork" : keeperTouched ? "Through his hands" : GoalLine(), true);
            return new Verdict(mine ? "OWN GOAL" : "GOAL", mine ? "Into your own net" : "They scored", false);
        }
        Verdict Saved(string sub) => new Verdict("SAVED", sub, !mine);
        if (m.HeldBy != null && m.HeldBy.Team == def) return Saved(mine ? "The keeper holds it" : "Held");
        if (m.Phase == Phase.Out)
        {
            var r = m.RestartPending;
            if (r == SetPieceKind.FreeKick || r == SetPieceKind.Penalty) return new Verdict("FOUL", "Run it again", null);
            if (keeperTouched) return Saved(mine ? "Tipped away" : "Turned behind");
            if (blocked) return new Verdict("BLOCKED", deadBall ? "Into the wall" : "Charged down", !mine);
            return new Verdict(woodwork ? "POST" : Math.Abs(b.Pos.Z) < Pitch.GoalHalfWidth ? "OVER" : "WIDE", woodwork ? "Off the woodwork" : "Off target", Kept(mine));
        }
        // The defending side has it under control (the keeper at his feet counts as a save).
        var own = m.Owner != null && m.Owner.Team == def ? m.Owner : null;
        defOwnT = own != null ? defOwnT + Tick.DT : 0;
        if (defOwnT > 0.6)
        {
            if (own!.Role == Role.GK) return Saved(mine ? "Smothered" : "Gathered");
            if (deadBall) return new Verdict(blocked ? "BLOCKED" : "CLEARED", blocked ? "Into the wall" : "Cleared", false);
            return new Verdict(mine ? "LOST IT" : "WON IT", mine ? "They took it off you" : "Ball won back", !mine);
        }
        // Cleared well away from goal.
        double gx = Pitch.HalfL * m.Teams[att].Dir;
        if (Math.Abs(b.Pos.X - gx) > 48) return new Verdict("CLEARED", mine ? "Booted away" : "Danger over", !mine);
        if (deadBall)
        {
            // The kick's been taken and the ball has died (or come back off the keeper or the wall).
            if (m.Phase == Phase.Play && m.Time - m.LastKickTime > 1.1 && JsMath.Hypot(b.Vel.X, b.Vel.Z) < 3)
            {
                if (keeperTouched) return Saved("Parried");
                if (blocked) return new Verdict("BLOCKED", "Into the wall", false);
                return new Verdict("SHORT", "It never got there", false);
            }
            return null;
        }
        double limit = Kind == DrillKind.TwoVTwo ? 25 : Kind == DrillKind.Keeper ? 10 : 14;
        if (t > limit) return new Verdict("TIME", mine ? "Too slow" : "Kept them out", Kept(mine));
        return null;
    }

    /// <summary>A round that ends without a goal or a save: a failure when you attack, a success
    /// when you defend in 2 v 2, and nobody's doing in goal (it wasn't your save).</summary>
    bool? Kept(bool mine) => mine ? false : Kind == DrillKind.Keeper ? null : true;

    /// <summary>Where it went in, for the caption.</summary>
    string GoalLine()
    {
        var b = M.Ball.Pos;
        bool corner = Math.Abs(b.Z) > Pitch.GoalHalfWidth - 1;
        return b.Y > 1.7 ? (corner ? "Top corner" : "Under the bar") : corner ? "Bottom corner" : "Past the keeper";
    }

    void Finish(Verdict v)
    {
        Verdict = v;
        Verdicts++;
        doneT = 0;
        bool scored = v.Title == "GOAL" || v.Title == "OWN GOAL";
        hold = scored ? 2.4 : 1.6;
        if (v.Good == null) return;
        // 2 v 2 keeps the goals; the others count good rounds against bad ones.
        if (Kind == DrillKind.TwoVTwo)
        {
            if (scored && v.Good == true) Made++;
            else if (scored) Against++;
        }
        else if (v.Good == true) Made++;
        else Against++;
        Streak = v.Good == true ? Streak + 1 : 0;
        Best = Math.Max(Best, Streak);
    }

    // ------------------------------------------------------------------ laying out

    void Next()
    {
        var m = M;
        var rng = m.Rng;
        round++;
        t = 0;
        doneT = -1;
        keeperTouched = blocked = woodwork = false;
        defOwnT = 0;
        m.ClearPlay();
        const double gx = Pitch.HalfL;
        switch (Kind)
        {
            case DrillKind.FreeKicks:
            {
                Att = 0;
                m.Field(new[] { new[] { specialist }, new[] { 0, 1, 2, 3, 4, 5 } });
                // Somewhere a shot is on: 17 to 29 m out, never wider than 31 m from goal.
                double depth = 17 + rng.Next() * 12;
                double zMax = Math.Min(20, Math.Sqrt(31 * 31 - depth * depth));
                double x = gx - depth;
                double z = (rng.Next() * 2 - 1) * zMax;
                // The defenders gather where the wall will stand; the keeper is on his line.
                double d = JsMath.Hypot(depth, z);
                double cx = x + (depth / d) * 9.5;
                double cz = z - (z / d) * 9.5;
                Put(Pick(1, 0), gx - 0.6, 0, x, z);
                for (int k = 0; k < 5; k++) Put(Pick(1, k + 1), cx + 0.5, cz + (k - 2) * 0.9, x, z);
                m.StartSetPiece(SetPieceKind.FreeKick, 0, x, z);
                break;
            }
            case DrillKind.Penalties:
            {
                Att = 0;
                m.Field(new[] { new[] { specialist }, new[] { 0 } });
                m.StartSetPiece(SetPieceKind.Penalty, 0, gx - Pitch.PenaltySpot, 0);
                break;
            }
            case DrillKind.OneOnOne:
            {
                Att = 0;
                m.Field(new[] { new[] { 9 }, new[] { 0 } });
                double x = gx - (28 + rng.Next() * 8);
                double z = (rng.Next() * 2 - 1) * 12;
                Put(Pick(1, 0), gx - 1.2, 0, x, z);
                Attack(Pick(0, 9), x, z, gx);
                break;
            }
            case DrillKind.TwoVTwo:
            {
                // Odd rounds you attack the far goal; even rounds they come at yours.
                Att = round % 2 == 1 ? 0 : 1;
                int a = Att;
                int d = 1 - a;
                m.Field(a == 0 ? new[] { new[] { 9, 10 }, new[] { 0, 2, 3 } } : new[] { new[] { 0, 2, 3 }, new[] { 9, 10 } });
                double goal = gx * m.Teams[a].Dir; // the goal being attacked
                double s = -JsMath.Sign(goal); // toward the halfway line
                double z = (rng.Next() * 2 - 1) * 10;
                double wide = z > 0 ? -1 : 1;
                Put(Pick(d, 0), goal + s * 1.2, 0, goal + s * 30, z);
                Put(Pick(d, 2), goal + s * 19, -5 + z * 0.3, goal + s * 30, z);
                Put(Pick(d, 3), goal + s * 19, 5 + z * 0.3, goal + s * 30, z);
                Put(Pick(a, 10), goal + s * 32, z + wide * 11, goal, 0);
                Attack(Pick(a, 9), goal + s * 34, z, goal);
                if (a != 0)
                {
                    // You take the defender nearer the ball.
                    var p2 = Pick(0, 2);
                    var p3 = Pick(0, 3);
                    m.SetControlled(Math.Abs(p2.Pos.Z - z) < Math.Abs(p3.Pos.Z - z) ? p2 : p3);
                }
                break;
            }
            case DrillKind.Keeper:
            {
                Att = 1;
                m.Field(new[] { new[] { 0 }, new[] { 9, 10 } });
                double goal = -gx;
                var k = Pick(0, 0);
                Put(k, goal + 0.8, 0, 0, 0);
                m.SetControlled(k);
                var shooter = Pick(1, 9);
                var mate = Pick(1, 10);
                if (rng.Next() < 0.6)
                {
                    // A strike from outside the box, a team-mate lurking for the rebound.
                    double depth = 17 + rng.Next() * 11;
                    double z = (rng.Next() * 2 - 1) * 14;
                    Put(mate, goal + 12, -JsMath.Sign(JsMath.Or1(z)) * 7, goal, 0);
                    Attack(shooter, goal + depth, z, goal);
                    double dirZ = rng.Next() < 0.5 ? -1 : 1;
                    double power = 0.7 + rng.Next() * 0.3;
                    shooter.Plan = new KickPlan { Type = KickType.Shot, DirX = -1, DirZ = dirZ, Power = power, TargetId = -1, Expires = m.Time + 3 };
                }
                else
                {
                    // A break: one or two of them running at you.
                    double z = (rng.Next() * 2 - 1) * 8;
                    bool pair = rng.Next() < 0.5;
                    Put(mate, pair ? goal + 31 : goal + 60, pair ? z + (z > 0 ? -10 : 10) : 30, goal, 0);
                    Attack(shooter, goal + 33, z, goal);
                }
                break;
            }
        }
        scores[0] = m.Teams[0].Score;
        scores[1] = m.Teams[1].Score;
    }

    /// <summary>The player in shirt slot `index` of `team` (taking part or not).</summary>
    Player Pick(int team, int index)
    {
        foreach (var p in M.All) if (p.Team == team && p.Index == index) return p;
        throw new InvalidOperationException($"No player {index} in team {team}");
    }

    static void Put(Player p, double x, double z, double lookX, double lookZ)
    {
        p.Pos.Set(x, 0, z);
        p.PrevPos.Copy(p.Pos);
        p.Vel.Set(0, 0, 0);
        p.Facing = JsMath.Atan2(lookZ - z, lookX - x);
        p.PrevFacing = p.Facing;
    }

    /// <summary>Open play: `p` on the ball at (x, z), facing the goal at `goal`.</summary>
    void Attack(Player p, double x, double z, double goal)
    {
        var m = M;
        Put(p, x, z, goal, 0);
        double d = JsMath.Or1(JsMath.Hypot(goal - x, z));
        m.Ball.Reset(x + ((goal - x) / d) * 0.7, z - (z / d) * 0.7);
        m.Ball.Pos.Y = BallK.Radius;
        m.Phase = Phase.Play;
        m.PhaseT = 0;
        m.PossTeam = p.Team;
        if (p.Team == m.HumanTeam) m.SetControlled(p);
    }
}
