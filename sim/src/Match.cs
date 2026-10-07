using System;
using System.Collections.Generic;

namespace GameNight.Sim;

public enum Phase { Kickoff, Play, Out, SetPiece, Goal, Halftime, Fulltime }

/// <summary>Goal celebrations, one per button: Pass, Through, Shoot, Sprint.</summary>
public enum CelebrationKind { Slide, Plane, Siu, Flip }

public enum SetPieceKind { Kickoff, Throw, Corner, GoalKick, FreeKick, Penalty }

public sealed class Celebration
{
    public CelebrationKind Kind;
    /// <summary>PhaseT when the move starts.</summary>
    public double At;
    /// <summary>Toward the camera (the centre spot), fixed when it was picked.</summary>
    public double Dx, Dz;
    /// <summary>The side the aeroplane banks to (+1 = left).</summary>
    public double Turn;
}

public sealed class Wall
{
    public readonly List<Player> Players = new List<Player>();
    public readonly List<XZ> Slots = new List<XZ>();
}

public sealed class SetPiece
{
    public SetPieceKind Kind;
    public int Team;
    public double X, Z;
    public Player Taker;
    public double T;
    /// <summary>Free kick in shooting range: the defending wall (players and their spots).</summary>
    public Wall? Wall;
    /// <summary>Free kick close enough to shoot at goal.</summary>
    public bool Direct;
    /// <summary>Human dead-ball shot: the aim point on the goal mouth (world z, height).</summary>
    public double? AimZ, AimY;
    /// <summary>Human corner or goal kick: where the delivery is aimed to land (the gold ring on the grass).</summary>
    public XZ? Target;

    public SetPiece(SetPieceKind kind, int team, double x, double z, Player taker)
    {
        Kind = kind;
        Team = team;
        X = x;
        Z = z;
        Taker = taker;
    }
}

/// <summary>The last foul given (for the HUD, the referee and the commentary of the moment).</summary>
public sealed class Foul
{
    public Player Offender = null!, Victim = null!;
    public double X, Z;
    public bool Yellow, Penalty;
    /// <summary>Sent off: a straight red, or a second yellow (SecondYellow).</summary>
    public bool Red, SecondYellow;
    public double Time;
}

/// <summary>The last offside given: the player caught, and the free kick it gave the defenders.</summary>
public sealed class Offside
{
    public Player Player = null!;
    /// <summary>The team awarded the indirect free kick.</summary>
    public int Team;
    public double X, Z, Time;
}

public sealed class TeamState
{
    public TeamInfo Info;
    /// <summary>+1 attacks toward +x, -1 toward -x.</summary>
    public double Dir;
    public int Score;
    public List<Player> Players = new List<Player>();
    /// <summary>Shirt index of the captain.</summary>
    public int Captain;

    public TeamState(TeamInfo info, double dir, int captain)
    {
        Info = info;
        Dir = dir;
        Captain = captain;
    }
}

/// <summary>What happened this step (for sound, camera and HUD). Taken with Match.TakeEvents.</summary>
public sealed class MatchEvents
{
    /// <summary>Strengths 0..1.</summary>
    public readonly List<double> Kicks = new List<double>();
    /// <summary>0 none, 1 short, 2 long, 3 final.</summary>
    public int Whistle;
    /// <summary>Team index or -1.</summary>
    public int Goal = -1;
    public double Post, Net, NetX, NetY, NetZ, Bounce, Save, Tackle;
    /// <summary>A foul was given (1) or the referee played advantage (2).</summary>
    public int Foul;
    /// <summary>A card was shown: 1 yellow, 2 red.</summary>
    public int Card;
    /// <summary>The linesman's flag went up for offside.</summary>
    public int Offside;
    /// <summary>A substitution was made (see Match.Subs).</summary>
    public int Sub;
    /// <summary>A skill move sold its dummy: the move's stars (one more when he left a man on the floor).</summary>
    public double Skill;

    public void Clear()
    {
        Kicks.Clear();
        Whistle = 0;
        Goal = -1;
        Post = Net = NetX = NetY = NetZ = Bounce = Save = Tackle = 0;
        Foul = Card = Offside = Sub = 0;
        Skill = 0;
    }
}

public sealed class PendingRestart
{
    public SetPieceKind Kind;
    public int Team;
    public double X, Z;
}

public sealed class Advantage
{
    public int Team;
    public double X, Z;
    public bool Penalty;
    public double Until;
}

public sealed class RestartZone
{
    public double X, Z, R;
    public bool Half;
}

public sealed class SteerState
{
    public bool On;
    public double X, Z;
}

/// <summary>
/// One human's side of the match: the player he controls and what his buttons have going on.
/// There is one per team; only a human side's (see Match.HumanSide) is used.
/// </summary>
public sealed class Seat
{
    public Player Controlled = null!;
    /// <summary>PRESS / SPRINT held while the ball isn't his side's.</summary>
    public bool PressHeld;
    /// <summary>Seconds the stick has been idle (read by the AI for auto-switching).</summary>
    public double NoInputT;
    /// <summary>Seconds since the controlled player changed (UI flash).</summary>
    public double SwitchT;
    internal bool SprintWas;
    internal double LastTackleTap = -10;
    /// <summary>His skill moves, by the way he slides SPRINT on the ball: up, left, right, down.
    /// None leaves that slide doing nothing; so does a move the man on the ball can't do.</summary>
    public readonly SkillMove[] Slots = (SkillMove[])Skills.DefaultSlots.Clone();
    /// <summary>Last SPRINT press on the ball (a second one quickly after is his signature skill).</summary>
    internal double LastSprintTap = -10;
    /// <summary>Sprint-swipe tackle: committed, waiting for the moment to strike.</summary>
    internal bool LungeOn, LungeSlide;
    internal double LungeUntil;
    /// <summary>Pressing: how long he's been tight on the carrier (s), and when he can next go in.</summary>
    internal double PressTight, PokeReady;
}

/// <summary>
/// One match (or a training drill on a pitch): the whole simulation. Create it from a seed (and
/// optionally two line-ups), then call Step once per Tick.DT with the human's input. Same seed,
/// same line-ups, same inputs: the same match, step for step, as the PWA.
/// </summary>
public sealed partial class Match
{
    /// <summary>Level is onside: the linesman gives the attacker this much benefit of the doubt (m).</summary>
    const double OffsideMargin = 0.3;
    /// <summary>How much further a player reaches stretching a leg out for a loose ball (m).</summary>
    const double StretchReachMax = 0.4;
    const double StretchDur = 0.36;
    /// <summary>A dropping ball is let down onto the chest from above ChestTop; above ChestMax it can only be headed.</summary>
    const double ChestTop = 1.55;
    const double ChestMax = 1.95;
    /// <summary>Look-ahead samples for deciding a stretch (every 0.05 s).</summary>
    const int StretchN = 7;
    /// <summary>Tackling leg (boot and shin) radius, and the radius of a standing player's legs.</summary>
    const double TackleLegR = 0.12;
    /// <summary>A slide's leading leg against a man's legs; against the ball it sweeps wider: both
    /// legs, studs and shins, are on the grass together.</summary>
    const double SlideLegR = 0.15;
    const double SlideSweepR = 0.24;
    const double VictimLegR = 0.2;
    /// <summary>How long a human Pass/Shoot/Through stays queued waiting for the ball (s).</summary>
    const double HumanBuffer = 2.5;
    /// <summary>Extra reach given to the human's strikes so a queued command doesn't miss by inches.</summary>
    const double HumanStrikeReach = 1.15;
    const double HumanContactReach = 1.6;
    const double DT = Tick.DT;

    public static readonly CelebrationKind[] Celebrations = { CelebrationKind.Slide, CelebrationKind.Plane, CelebrationKind.Siu, CelebrationKind.Flip };

    readonly V3 tmpV = new V3();
    readonly V3 tmpK = new V3();
    readonly F32 stretchX = new F32(StretchN), stretchY = new F32(StretchN), stretchZ = new F32(StretchN);

    public readonly Ball Ball = new Ball();
    /// <summary>The players taking part (everyone, except in a small-sided training drill).</summary>
    public readonly List<Player> Players = new List<Player>();
    /// <summary>Every player by id, taking part or not.</summary>
    public readonly List<Player> All = new List<Player>();
    /// <summary>Bumped whenever the players taking part change (see Field).</summary>
    public int Roster;
    /// <summary>Training: no clock, no offside, and restarts are the drill's business.</summary>
    public bool Training;
    /// <summary>Training: the human plays in goal (the stick positions him, a button dives).</summary>
    public bool KeeperHuman;
    public readonly List<TeamState> Teams = new List<TeamState>();
    public readonly Rng Rng;
    public readonly AI AI;

    public double Time;
    /// <summary>Seconds into the current half (real).</summary>
    public double Clock;
    public int Half = 1;
    /// <summary>Added time per half, in match minutes (1–5), drawn at kick-off.</summary>
    public readonly double[] Added = new double[2];
    public Phase Phase = Phase.Kickoff;
    public double PhaseT;

    public Player? Owner;
    public Player? HeldBy;
    public Player? LastTouch;
    public Player? LastKicker;
    public double LastKickTime = -10;
    /// <summary>LastKicker played it with his foot (or threw it in): the back-pass rule applies.
    /// Headers, deflections and the keeper's own saves don't count.</summary>
    public bool LastKickFoot;
    /// <summary>When the ball went into the keeper's hands (the six-second rule).</summary>
    public double HeldSince;
    public Player? PassTarget;
    /// <summary>The pass to PassTarget is into space ahead of him (a through ball), not to him.</summary>
    public bool PassIntoSpace;
    /// <summary>Team that last had controlled possession (for team shape).</summary>
    public int PossTeam;
    public SetPiece? SetPiece;
    /// <summary>Restart waiting while the ball runs on after going out of play.</summary>
    PendingRestart? pendingRestart;
    public int KickoffTeam;
    public Player? Scorer;
    /// <summary>The scorer's celebration, once picked (by the buttons, or by the AI for its goals).</summary>
    public Celebration? Celebration;
    /// <summary>Your scorer's run, while the stick steers it (the first GoalSeq.Steer s of your goals).</summary>
    public readonly SteerState Steer = new SteerState();
    /// <summary>Yellow cards per player id.</summary>
    public readonly int[] Cards = new int[22];
    public Foul? LastFoul;
    /// <summary>Advantage being played after a foul: brought back if the fouled team loses the ball.</summary>
    public Advantage? Advantage;
    public Offside? LastOffside;
    // Offside is judged the moment a team plays the ball: these teammates were beyond the line
    // then, and touching it before an opponent plays it is the offence.
    int offsideSnapTeam = -1;
    List<Player>? offsideFlagged;
    /// <summary>Who struck the last shot (headers included); see ShotTeam.</summary>
    public Player? ShotBy;
    /// <summary>Wall players block (don't play) the ball until this time.</summary>
    double wallUntil = -1;
    readonly RestartZone zone = new RestartZone();

    public readonly int HumanTeam = 0;
    /// <summary>When true the AI also drives the "controlled" player (attract mode / tests).</summary>
    public bool AutoPlay;
    /// <summary>1v1: a human on each side, team 1's playing off Step's second input.</summary>
    public bool Versus;
    /// <summary>Each side's human (only the human sides' are used).</summary>
    public readonly Seat[] Seats = { new Seat(), new Seat() };
    /// <summary>The player HumanTeam's human controls.</summary>
    public Player Controlled => Seats[HumanTeam].Controlled;
    public double SwitchT => Seats[HumanTeam].SwitchT;
    /// <summary>A human plays this side (the one, or either in a 1v1).</summary>
    public bool HumanSide(int team) => team == HumanTeam || Versus;
    /// <summary>The human of p's side controls p.</summary>
    public bool Piloted(Player p) => HumanSide(p.Team) && Seats[p.Team].Controlled == p;

    public MatchEvents Events = new MatchEvents();
    MatchEvents spareEvents = new MatchEvents();
    /// <summary>Optional debug logger.</summary>
    public Action<string>? Log;
    /// <summary>0..1 crowd excitement, rises with danger near goals.</summary>
    public double Excitement;

    public Match(double seed = 20261002, MatchSetup? setup = null)
    {
        Rng = new Rng(seed);
        // Its own stream, so the added time doesn't shift every draw the match makes after it.
        var fourth = new Rng(M.ToInt32(seed) ^ 0x5eed4e);
        Added[0] = 1 + Math.Floor(fourth.Next() * 5);
        Added[1] = 1 + Math.Floor(fourth.Next() * 5);
        RollInvader(seed);
        for (int t = 0; t < 2; t++)
        {
            var ts = setup?.Teams[t];
            var team = new TeamState(ts != null ? ts.Info : TeamData.Default[t], t == 0 ? 1 : -1, ts?.Captain ?? 9);
            if (ts != null)
            {
                for (int i = 0; i < ts.Players.Count; i++)
                {
                    var sp = ts.Players[i];
                    var p = new Player(Players.Count, t, i, sp.Role, sp.X, sp.Z, sp.Attrs, sp.Look);
                    p.Name = sp.Name;
                    p.Number = sp.Number;
                    p.Source = sp.Source;
                    p.Foot = sp.Foot == -1 ? -1 : 1;
                    team.Players.Add(p);
                    Players.Add(p);
                }
                Teams.Add(team);
                continue;
            }
            for (int i = 0; i < TeamData.Formation433.Length; i++)
            {
                var slot = TeamData.Formation433[i];
                var attrs = TeamData.MakeAttributes(slot.Role, Rng);
                var look = new Look();
                look.Skin = TeamData.SkinTones[(int)Math.Floor(Rng.Next() * TeamData.SkinTones.Length)];
                look.Hair = TeamData.HairColors[(int)Math.Floor(Rng.Next() * TeamData.HairColors.Length)];
                look.HairStyle = (int)Math.Floor(Rng.Next() * 4);
                look.Height = Rng.Range(0.95, 1.06) * (slot.Role == Role.GK ? 1.04 : 1);
                look.Build = Rng.Range(0.92, 1.1);
                var p = new Player(Players.Count, t, i, slot.Role, slot.X, slot.Z, attrs, look);
                p.Attrs.Height = 1.8 * p.Look.Height;
                p.Attrs.Weight = 76 * p.Look.Build * p.Look.Height * p.Look.Height;
                team.Players.Add(p);
                Players.Add(p);
            }
            Teams.Add(team);
        }
        All.AddRange(Players);
        foreach (var p in Players)
            if (p.Attrs.Skill <= 0) p.Attrs.Skill = Skills.StarsFrom(p.Attrs, p.Role);
        MakeBenches(seed, setup);
        AI = new AI(this);
        Seats[0].Controlled = Teams[0].Players[9];
        Seats[1].Controlled = Teams[1].Players[9];
        StartKickoff(0);
    }

    /// <summary>This step's events, handed over; the match starts a fresh set.</summary>
    public MatchEvents TakeEvents()
    {
        var e = Events;
        Events = spareEvents;
        Events.Clear();
        spareEvents = e;
        return e;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The goal this team attacks.</summary>
    public double GoalX(int team) => Teams[team].Dir * Pitch.HalfL;

    /// <summary>Team that is currently in control or about to be (pass in flight).</summary>
    public int AttackingTeam()
    {
        if (HeldBy != null) return HeldBy.Team;
        if (Owner != null) return Owner.Team;
        if (SetPiece != null) return SetPiece.Team;
        if (PassTarget != null && Time - LastKickTime < 3) return PassTarget.Team;
        return -1;
    }

    /// <summary>
    /// Whether the human's buttons mean attack. Includes loose balls / passes in flight that our
    /// side will reach first, so pressing Pass on an incoming ball queues a pass.
    /// </summary>
    public bool HumanAttacking() => HumanAttacking(HumanTeam);

    /// <summary>The same for either side's human (a 1v1).</summary>
    public bool HumanAttacking(int team)
    {
        if (Phase == Phase.Kickoff) return true;
        int att = AttackingTeam();
        if (att == team) return true;
        if (att >= 0) return false;
        var c = Seats[team].Controlled;
        var mine = AI.Intercept[c.Id];
        double theirs = 99;
        foreach (var q in Teams[1 - team].Players)
        {
            var ip = AI.Intercept[q.Id];
            if (ip.T >= 0 && ip.T < theirs) theirs = ip.T;
        }
        double mt = mine.T >= 0 ? mine.T : 99;
        return mt <= theirs + 0.25 || BallDist(c) < 1.5;
    }

    public double NearestOpponentDist(Player p)
    {
        double best = 99;
        foreach (var q in Teams[1 - p.Team].Players)
        {
            double d = M.Dist2D(p.Pos.X, p.Pos.Z, q.Pos.X, q.Pos.Z);
            if (d < best) best = d;
        }
        return best;
    }

    public double BallDist(Player p) => M.Dist2D(p.Pos.X, p.Pos.Z, Ball.Pos.X, Ball.Pos.Z);

    /// <summary>The player in shirt slot `index` (0 GK … 9 ST), or the best stand-in when a training
    /// drill has left him out or he's been sent off: the outfielder nearest his place in the shape.</summary>
    public Player ByJob(int team, int index)
    {
        var ps = Teams[team].Players;
        if (index < ps.Count && ps[index].Index == index) return ps[index];
        foreach (var p in ps) if (p.Index == index) return p;
        Player? gone = null;
        foreach (var p in All) if (p.Team == team && p.Index == index) gone = p;
        Player best = ps[0];
        double bd = 1e9;
        foreach (var p in ps)
        {
            if (p.Role == Role.GK) continue;
            double d = gone == null ? 0 : M.Dist2D(p.BaseX, p.BaseZ, gone.BaseX, gone.BaseZ);
            if (d < bd)
            {
                bd = d;
                best = p;
            }
        }
        return best;
    }

    /// <summary>
    /// Training: only these shirt slots take part, per team; the rest stand down (not simulated,
    /// not drawn). Null brings everyone back.
    /// </summary>
    public void Field(int[][]? squads)
    {
        Players.Clear();
        for (int t = 0; t < 2; t++)
        {
            var team = Teams[t];
            var list = new List<Player>();
            foreach (var p in All)
                if (p.Team == t && (squads == null || Array.IndexOf(squads[t], p.Index) >= 0)) list.Add(p);
            team.Players = list;
            Players.AddRange(list);
        }
        Roster++;
    }

    /// <summary>Training: a clean slate between attempts (no restart pending, nobody on the ball, everyone fresh).</summary>
    public void ClearPlay()
    {
        pendingRestart = null;
        SetPiece = null;
        Owner = null;
        HeldBy = null;
        PassTarget = null;
        LastTouch = null;
        LastKicker = null;
        ShotBy = null;
        Advantage = null;
        offsideSnapTeam = -1;
        offsideFlagged = null;
        Scorer = null;
        Celebration = null;
        foreach (var s in Seats) s.LungeOn = false;
        wallUntil = -1;
        Ball.Reset(0, 0);
        foreach (var p in All)
        {
            p.Stamina = 1;
            p.Vel.Set(0, 0, 0);
            p.Action = ActionKind.None;
            p.Plan = null;
            p.LookAt = null;
            p.Sprinting = false;
        }
    }

    /// <summary>The restart waiting while the ball runs out (a foul's free kick or penalty included).</summary>
    public SetPieceKind? RestartPending => pendingRestart?.Kind;

    /// <summary>A penalty has been given and not yet taken (whistle, walk-up, run-up).</summary>
    public bool PenaltyPending => pendingRestart?.Kind == SetPieceKind.Penalty || SetPiece?.Kind == SetPieceKind.Penalty;

    // ------------------------------------------------------------------ restarts

    /// <summary>Where a player lines up for a kick-off taken by `kickTeam` (world).</summary>
    public V3 KickoffSpot(Player p, int kickTeam, V3 output)
    {
        double d = Teams[p.Team].Dir;
        double x = Math.Min(p.BaseX * Pitch.HalfL * 0.85, -1.5);
        double z = p.BaseZ * Pitch.HalfW * 0.8;
        if (p.Index == 9 && p.Team == kickTeam)
        {
            x = -0.3;
            z = 0.2;
        }
        else if (p.Index == 7 && p.Team == kickTeam)
        {
            x = -1.2;
            z = 6;
        }
        else if (JsMath.Hypot(x, z) < Pitch.CircleRadius + 0.5)
        {
            double s = (Pitch.CircleRadius + 0.8) / Math.Max(0.1, JsMath.Hypot(x, z));
            x *= s;
            z *= s;
        }
        return output.Set(x * d, 0, z * d);
    }

    void PlaceForKickoff(int kickTeam)
    {
        foreach (var team in Teams)
        {
            foreach (var p in team.Players)
            {
                KickoffSpot(p, kickTeam, p.Pos);
                p.PrevPos.Copy(p.Pos);
                p.Vel.Set(0, 0, 0);
                p.Facing = team.Dir > 0 ? 0 : Math.PI;
                p.PrevFacing = p.Facing;
                p.Action = ActionKind.None;
                p.Plan = null;
                p.LookAt = null;
            }
        }
    }

    public void StartKickoff(int team)
    {
        Phase = Phase.Kickoff;
        PhaseT = 0;
        KickoffTeam = team;
        PlaceForKickoff(team);
        Ball.Reset(0, 0);
        Owner = null;
        HeldBy = null;
        PassTarget = null;
        LastTouch = null;
        offsideSnapTeam = -1;
        offsideFlagged = null;
        PossTeam = team;
        var taker = ByJob(team, 9);
        SetPiece = new SetPiece(SetPieceKind.Kickoff, team, 0, 0, taker);
        for (int t = 0; t < 2; t++)
            if (HumanSide(t)) SetControlled(t == team ? taker : ByJob(t, 9));
        Events.Whistle = 1;
    }

    /// <summary>Ball out: let it run on into the boards / stands for a moment, then restart.</summary>
    void BallOut(SetPieceKind kind, int team, double x, double z)
    {
        pendingRestart = new PendingRestart { Kind = kind, Team = team, X = x, Z = z };
        Advantage = null;
        offsideSnapTeam = -1;
        offsideFlagged = null;
        Phase = Phase.Out;
        PhaseT = 0;
        Owner = null;
        PassTarget = null;
        Events.Whistle = 1;
        foreach (var p in Players) p.Plan = null;
    }

    public void StartSetPiece(SetPieceKind kind, int team, double x, double z)
    {
        Phase = Phase.SetPiece;
        PhaseT = 0;
        Owner = null;
        PassTarget = null;
        Ball.Vel.Set(0, 0, 0);
        Ball.Spin.Set(0, 0, 0);
        double dir = Teams[team].Dir;
        double goalX = Pitch.HalfL * dir;
        double toGoal = M.Dist2D(x, z, goalX, 0);
        // A free kick is "direct" when it's worth a shot: close enough and not too wide.
        bool direct = kind == SetPieceKind.FreeKick && toGoal < 32 && Math.Abs(z) < 24 && (goalX - x) * dir > 9;
        Player taker;
        if (kind == SetPieceKind.GoalKick)
        {
            taker = ByJob(team, 0);
        }
        else if (kind == SetPieceKind.Penalty || direct)
        {
            // The specialist steps up: the best striker of a dead ball among those close enough.
            taker = ByJob(team, 9);
            double best = -1e9;
            foreach (var p in Teams[team].Players)
            {
                if (p.Role == Role.GK) continue;
                double score = p.Attrs.Shooting * 0.7 + p.Attrs.Passing * 0.3 - M.Dist2D(p.Pos.X, p.Pos.Z, x, z) * 0.004;
                if (score > best)
                {
                    best = score;
                    taker = p;
                }
            }
        }
        else
        {
            // Nearest outfield player of the restarting team.
            taker = ByJob(team, 1);
            double best = 1e9;
            foreach (var p in Teams[team].Players)
            {
                if (p.Role == Role.GK) continue;
                double d = M.Dist2D(p.Pos.X, p.Pos.Z, x, z);
                if (d < best)
                {
                    best = d;
                    taker = p;
                }
            }
        }
        var sp = new SetPiece(kind, team, x, z, taker) { Direct = direct };
        SetPiece = sp;
        PossTeam = team;
        // Cut straight to the taker standing over the ball (like a broadcast replay cut).
        var f = SetPieceFacing(sp);
        var spot = SetPieceSpot(sp);
        taker.Pos.Set(spot.x, 0, spot.z);
        taker.PrevPos.Copy(taker.Pos);
        taker.Vel.Set(0, 0, 0);
        taker.Facing = spot.runUp ? JsMath.Atan2(z - spot.z, x - spot.x) : JsMath.Atan2(f.z, f.x);
        taker.PrevFacing = taker.Facing;
        taker.Action = ActionKind.None;
        taker.Plan = null;
        Ball.Reset(x, z);
        Ball.Pos.Y = BallK.Radius;
        HeldBy = null;
        if (kind == SetPieceKind.Corner && HumanSide(team))
        {
            // Start the ring around the penalty spot, a little toward the far post.
            sp.Target = new XZ(goalX - dir * 9, -JsMath.Or1(JsMath.Sign(z)) * 1.5);
        }
        else if (kind == SetPieceKind.GoalKick && HumanSide(team))
        {
            // Start the ring just short of halfway, out toward the touchline on the kick's side.
            sp.Target = new XZ(x + dir * 38, JsMath.Or1(JsMath.Sign(z)) * 12);
        }
        if (kind == SetPieceKind.Penalty || direct)
        {
            // Where the human starts aiming: free kicks over the wall to the far post, penalties the middle.
            double near = JsMath.Or1(JsMath.Sign(z));
            sp.AimZ = kind == SetPieceKind.Penalty ? 0 : -near * (Pitch.GoalHalfWidth - 0.7);
            sp.AimY = kind == SetPieceKind.Penalty ? 0.9 : 1.9;
        }
        if (kind == SetPieceKind.Penalty) SetupPenalty(sp);
        else if (direct) SetupWall(sp);
        if (HumanSide(team)) SetControlled(taker);
        if (HumanSide(1 - team) && sp.Wall != null && sp.Wall.Players.Contains(Seats[1 - team].Controlled))
        {
            // Defending a free kick: the wall is the AI's job; take the nearest free defender.
            var free = new List<Player>();
            foreach (var p in Teams[1 - team].Players)
                if (p.Role != Role.GK && !sp.Wall.Players.Contains(p)) free.Add(p);
            M.StableSort(free, (a, b) => M.Dist2D(a.Pos.X, a.Pos.Z, x, z) - M.Dist2D(b.Pos.X, b.Pos.Z, x, z));
            if (free.Count > 0) SetControlled(free[0]);
        }
        Events.Whistle = 1;
    }

    /// <summary>Direction the taker faces over the ball.</summary>
    public (double x, double z) SetPieceFacing(SetPiece sp)
    {
        double dir = Teams[sp.Team].Dir;
        double fx = dir;
        double fz = 0;
        if (sp.Kind == SetPieceKind.Throw)
        {
            fx = 0;
            fz = -JsMath.Sign(sp.Z);
        }
        else if (sp.Target != null)
        {
            // Lined up over the ball toward the aim ring.
            fx = sp.Target.X - sp.X;
            fz = sp.Target.Z - sp.Z;
        }
        else if (sp.Kind == SetPieceKind.Corner)
        {
            fx = -JsMath.Sign(sp.X) * 0.6;
            fz = -JsMath.Sign(sp.Z);
        }
        else if (sp.Kind == SetPieceKind.Penalty || sp.Direct)
        {
            fx = Pitch.HalfL * dir - sp.X;
            fz = -sp.Z;
        }
        double n = JsMath.Or1(JsMath.Hypot(fx, fz));
        return (fx / n, fz / n);
    }

    /// <summary>How far behind the ball the taker waits (a run-up for shots from a dead ball).</summary>
    public double SetPieceBack(SetPiece sp) =>
        sp.Kind == SetPieceKind.Throw ? 0.05 : sp.Kind == SetPieceKind.Penalty ? 3.4 : sp.Direct ? 4.4 : 0.45;

    /// <summary>
    /// Where the taker stands before a set piece. Shots from a dead ball get a real run-up: a few
    /// strides back and off to the side of his kicking foot.
    /// </summary>
    public (double x, double z, bool runUp) SetPieceSpot(SetPiece sp)
    {
        var f = SetPieceFacing(sp);
        double back = SetPieceBack(sp);
        bool runUp = sp.Kind == SetPieceKind.Penalty || sp.Direct;
        // Right of the facing direction is (-f.z, f.x); the taker stands on his kicking foot's far side.
        double lat = runUp ? (sp.Kind == SetPieceKind.Penalty ? 0.45 : 0.7) * back * sp.Taker.Foot : 0;
        return (sp.X - f.x * back + f.z * lat, sp.Z - f.z * back - f.x * lat, runUp);
    }

    /// <summary>The human is lining up a corner: the landing ring and the flight preview are up.</summary>
    public bool AimingCorner
    {
        get
        {
            var sp = SetPiece;
            return sp != null && !AutoPlay && Phase == Phase.SetPiece && sp.Kind == SetPieceKind.Corner && Piloted(sp.Taker) && sp.Taker.Plan == null && sp.Taker.Action == ActionKind.None && sp.Target != null;
        }
    }

    /// <summary>The human is lining up a goal kick: the same ring and flight preview, out upfield.</summary>
    public bool AimingGoalKick
    {
        get
        {
            var sp = SetPiece;
            return sp != null && !AutoPlay && Phase == Phase.SetPiece && sp.Kind == SetPieceKind.GoalKick && Piloted(sp.Taker) && sp.Taker.Plan == null && sp.Taker.Action == ActionKind.None && sp.Target != null;
        }
    }

    /// <summary>Either aimed delivery is being lined up (the ring is on the grass).</summary>
    public bool AimingDelivery => AimingCorner || AimingGoalKick;

    /// <summary>The aimed delivery for the current set piece: a corner, or a goal kick (no bend on those).</summary>
    public KickResult SolveDelivery(double lx, double lz, bool floated, V3? from = null)
    {
        from ??= Ball.Pos;
        if (SetPiece?.Kind != SetPieceKind.GoalKick) return SolveCorner(lx, lz, floated, from);
        // Driven: low and skimming, quick to the target. Floated: a high hanging punt.
        double d = M.Dist2D(from.X, from.Z, lx, lz);
        double angle = floated ? M.Clamp(34 + d * 0.12, 38, 46) : M.Clamp(13 + d * 0.22, 18, 28);
        return Kick.SolveLofted(from, lx, lz, angle, floated ? 26 : 16, 0);
    }

    /// <summary>The short goal kick: the nearest outfield team-mate, preferring the ring's side.</summary>
    public Player ShortOption(Player p, XZ t)
    {
        var best = ByJob(p.Team, 2);
        double bestS = 1e9;
        foreach (var q in Teams[p.Team].Players)
        {
            if (q == p || q.Role == Role.GK) continue;
            double s = M.Dist2D(q.Pos.X, q.Pos.Z, p.Pos.X, p.Pos.Z) + 0.25 * M.Dist2D(q.Pos.X, q.Pos.Z, t.X, t.Z);
            if (s < bestS)
            {
                bestS = s;
                best = q;
            }
        }
        return best;
    }

    /// <summary>
    /// The corner delivery that lands on (lx, lz): whipped (flatter, faster, curling in toward
    /// goal) or floated (high and hanging). Shared by the kick and the on-screen preview, so the
    /// arc you see is the ball you get (before the taker's error).
    /// </summary>
    public KickResult SolveCorner(double lx, double lz, bool floated, V3? from = null)
    {
        from ??= Ball.Pos;
        var team = SetPiece != null ? Teams[SetPiece.Team] : Teams[HumanTeam];
        double gx = Pitch.HalfL * team.Dir;
        double d = Math.Max(1, M.Dist2D(from.X, from.Z, lx, lz));
        double fx = (lx - from.X) / d;
        double fz = (lz - from.Z) / d;
        // Inswinging: bend toward the goal line (right of travel is (-fz, fx)).
        double toGoalX = gx - lx;
        double toGoalZ = -lz;
        double bend = JsMath.Or1(JsMath.Sign(-fz * toGoalX + fx * toGoalZ));
        double angle = floated ? M.Clamp(30 + d * 0.15, 32, 40) : M.Clamp(14 + d * 0.3, 17, 26);
        return Kick.SolveLofted(from, lx, lz, angle, floated ? 22 : 14, bend * (floated ? 7 : 15));
    }

    /// <summary>The human is lining up a dead-ball shot (third-person camera, aim reticle).</summary>
    public bool AimingShot
    {
        get
        {
            var sp = SetPiece;
            return sp != null && !AutoPlay && Phase == Phase.SetPiece && Piloted(sp.Taker) &&
                sp.Taker.Plan == null && sp.Taker.Action == ActionKind.None && (sp.Kind == SetPieceKind.Penalty || sp.Direct) && sp.AimZ.HasValue;
        }
    }

    SetPiece? viewFor;
    (Player taker, double x, double z, SetPieceKind kind)? viewHeld;

    /// <summary>
    /// A dead ball worth watching from behind the taker: your goal kicks, corners, free kicks and
    /// penalties while you aim, and the other side's once their taker has lined up. Held as it
    /// was lined up through the run-up (Running), until the ball is struck. Null when there's
    /// nothing to show.
    /// </summary>
    public (Player taker, double x, double z, SetPieceKind kind, bool running)? DeadBallView
    {
        get
        {
            var sp = SetPiece;
            if (sp == null || AutoPlay || Phase != Phase.SetPiece) return null;
            var t = sp.Taker;
            if (t.Plan != null || t.Action != ActionKind.None || t.Speed > 0.8)
                return viewFor == sp && viewHeld is { } h ? (h.taker, h.x, h.z, h.kind, true) : null;
            viewFor = sp;
            viewHeld = LinedUp(sp);
            return viewHeld is { } v ? (v.taker, v.x, v.z, v.kind, false) : null;
        }
    }

    (Player taker, double x, double z, SetPieceKind kind)? LinedUp(SetPiece sp)
    {
        {
            var t = sp.Taker;
            bool human = HumanSide(sp.Team);
            if (human && t != Seats[sp.Team].Controlled) return null;
            double dir = Teams[sp.Team].Dir;
            if (sp.Kind == SetPieceKind.GoalKick)
            {
                // Yours: eyes on the ring; theirs: upfield.
                if (human && AimingGoalKick) return (t, sp.Target!.X, sp.Target.Z, sp.Kind);
                return (t, sp.X + dir * 40, sp.Z * 0.3, sp.Kind);
            }
            if (sp.Kind == SetPieceKind.Corner)
            {
                // Yours: eyes on the ring; theirs: the penalty spot, once he's at the flag.
                if (human && AimingCorner) return (t, sp.Target!.X, sp.Target.Z, sp.Kind);
                if (human || JsMath.Hypot(t.Pos.X - sp.X, t.Pos.Z - sp.Z) > 3) return null;
                return (t, dir * (Pitch.HalfL - 11), 0, sp.Kind);
            }
            if (sp.Kind != SetPieceKind.Penalty && !sp.Direct) return null;
            if (human)
            {
                var aim = AimingShot ? AimPoint() : null;
                return aim.HasValue ? (t, aim.Value.x, aim.Value.z * 0.35, sp.Kind) : null;
            }
            // Theirs: lined up (at the ball, or standing at the top of his run-up), eyes on our goal.
            if (JsMath.Hypot(t.Pos.X - sp.X, t.Pos.Z - sp.Z) > 8) return null;
            return (t, Pitch.HalfL * dir, (sp.AimZ ?? 0) * 0.35, sp.Kind);
        }
    }

    /// <summary>Aim point of a dead-ball shot in world coordinates.</summary>
    public (double x, double y, double z)? AimPoint()
    {
        var sp = SetPiece;
        if (sp == null || !sp.AimZ.HasValue) return null;
        return (Pitch.HalfL * Teams[sp.Team].Dir, sp.AimY ?? 1, sp.AimZ.Value);
    }

    /// <summary>
    /// The wall: 9.15 m from the ball on the line to the goal, covering the near-post side (the
    /// keeper takes the far side). Bigger the closer and more central the kick.
    /// </summary>
    void SetupWall(SetPiece sp)
    {
        int def = 1 - sp.Team;
        double dir = Teams[sp.Team].Dir;
        double gx = Pitch.HalfL * dir;
        double d = M.Dist2D(sp.X, sp.Z, gx, 0);
        double central = 1 - Math.Min(1, Math.Abs(sp.Z) / 22);
        int n = (int)M.Clamp(JsMath.Round(1 + central * 3.2 - (d - 18) / 7), 1, 5);
        // Aim the wall at a point just inside the near post.
        double near = JsMath.Or1(JsMath.Sign(sp.Z));
        double ax = gx;
        double az = near * (Pitch.GoalHalfWidth * 0.45);
        double lx = ax - sp.X;
        double lz = az - sp.Z;
        double ld = JsMath.Hypot(lx, lz);
        double ux = lx / ld;
        double uz = lz / ld;
        double cx = sp.X + ux * 9.15;
        double cz = sp.Z + uz * 9.15;
        // Perpendicular, ordered from the near-post side outward.
        double px = -uz;
        double pz = ux;
        if (JsMath.Sign(pz) != near)
        {
            px = -px;
            pz = -pz;
        }
        var wall = new Wall();
        for (int i = 0; i < n; i++)
        {
            double o = (i - (n - 1) / 2.0) * 0.62 + near * 0.3;
            wall.Slots.Add(new XZ(cx + px * o, cz + pz * o));
        }
        var free = new List<Player>();
        foreach (var p in Teams[def].Players) if (p.Role != Role.GK) free.Add(p);
        M.StableSort(free, (a, b) => M.Dist2D(a.Pos.X, a.Pos.Z, cx, cz) - M.Dist2D(b.Pos.X, b.Pos.Z, cx, cz));
        for (int i = 0; i < Math.Min(n, free.Count); i++) wall.Players.Add(free[i]);
        // Cut: the wall lines up while the taker places the ball.
        for (int i = 0; i < wall.Players.Count; i++)
        {
            var p = wall.Players[i];
            p.Pos.Set(wall.Slots[i].X, 0, wall.Slots[i].Z);
            p.PrevPos.Copy(p.Pos);
            p.Vel.Set(0, 0, 0);
            p.Facing = JsMath.Atan2(sp.Z - p.Pos.Z, sp.X - p.Pos.X);
            p.PrevFacing = p.Facing;
            p.Action = ActionKind.None;
        }
        sp.Wall = wall;
    }

    /// <summary>Penalty: everyone else outside the box and the arc; the keeper on his line.</summary>
    void SetupPenalty(SetPiece sp)
    {
        double dir = Teams[sp.Team].Dir;
        double edge = (Pitch.HalfL - Pitch.BoxDepth - 1.5) * dir;
        int i = 0;
        foreach (var p in Players)
        {
            if (p == sp.Taker) continue;
            double x, z;
            if (p.Role == Role.GK)
            {
                x = p.Team == sp.Team ? -dir * (Pitch.HalfL - 12) : dir * (Pitch.HalfL - 0.15);
                z = 0;
            }
            else
            {
                // Along the edge of the box, attackers and defenders shoulder to shoulder, leaving the arc clear.
                int k = i++;
                double side = k % 2 == 0 ? 1 : -1;
                int slot = k / 2;
                z = side * (10 + slot * 1.6);
                x = edge - dir * (Math.Abs(z) > Pitch.BoxHalfWidth ? 0 : 0.5 + (slot % 2) * 1.2);
            }
            p.Pos.Set(x, 0, M.Clamp(z, -Pitch.HalfW + 1, Pitch.HalfW - 1));
            p.PrevPos.Copy(p.Pos);
            p.Vel.Set(0, 0, 0);
            p.Facing = JsMath.Atan2(sp.Z - p.Pos.Z, sp.X - p.Pos.X);
            p.PrevFacing = p.Facing;
            p.Action = ActionKind.None;
        }
    }

    /// <summary>The human of p's side takes p.</summary>
    public void SetControlled(Player p)
    {
        var s = Seats[p.Team];
        if (s.Controlled == p) return;
        if (s.Controlled != null)
        {
            var old = s.Controlled;
            old.Sprinting = false;
            // A switch (manual or automatic) cancels whatever the human had loaded: a queued pass
            // or shot is dropped (unless the strike is already under way).
            if (!AutoPlay && HumanSide(old.Team) && old.Action != ActionKind.Kick && old.Action != ActionKind.Throw) old.Plan = null;
        }
        s.Controlled = p;
        s.SwitchT = 0;
    }
}
