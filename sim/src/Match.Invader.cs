using System;

namespace GameNight.Sim;

/// <summary>
/// A pitch invader (native only). Each game minute has a 0.1% chance of a fan getting over the
/// boards, about one match in eleven; the minute is rolled from the seed, so a match replays the
/// same. The referee stops play the way he does for a foul (Phase.Out, the clock held), the
/// players walk to the drop ball and watch the chase, and the view (Render/PitchInvader) runs the
/// fan and the stewards. When they have him off the pitch the view calls EndInvader, and play
/// restarts with a drop ball for the side that had it. Without a view (or if it never says) the
/// match drops the ball itself after a minute.
/// </summary>
public sealed partial class Match
{
    public const double InvaderChance = 0.001;
    const double InvaderMaxHold = 60;

    /// <summary>When he comes on: the half (0 = not this match) and its clock, in seconds.</summary>
    public int InvaderHalf;
    public double InvaderClock;
    /// <summary>Seconds since the whistle for him; -1 while there's no invader on.</summary>
    public double InvaderT = -1;
    /// <summary>Picks his entrance and his runs (the same match, the same invader).</summary>
    public int InvaderSeed;
    /// <summary>Where the players look while he's on (the view keeps it on the chase).</summary>
    public double InvaderLookX, InvaderLookZ;

    int invaderTeam = -1;
    Player? invaderTaker;
    bool invaderGone;

    void RollInvader(double seed)
    {
        // Its own stream, like the added time: nothing else the match draws moves.
        var r = new Rng(M.ToInt32(seed) ^ 0x1a7ade);
        InvaderSeed = (int)(r.Next() * 1e9);
        for (int minute = 0; minute < 90; minute++)
        {
            if (r.Next() >= InvaderChance) continue;
            InvaderHalf = minute < 45 ? 1 : 2;
            InvaderClock = ((minute % 45) + r.Next()) * MatchK.HalfSeconds / 45;
            return;
        }
    }

    /// <summary>Debug: a fan comes on `after` seconds of play from now.</summary>
    public void DebugInvader(double after = 3)
    {
        if (Training || InvaderT >= 0) return;
        InvaderHalf = Half;
        InvaderClock = Clock + after;
    }

    /// <summary>The view has him off the pitch: drop the ball.</summary>
    public void EndInvader()
    {
        if (InvaderT >= 0) invaderGone = true;
    }

    public bool InvaderOn => InvaderT >= 0;

    /// <summary>Early in each step: the whistle for him, the hold, and the drop ball after.</summary>
    void InvaderStep()
    {
        if (InvaderT >= 0)
        {
            InvaderT += DT;
            if (invaderGone || InvaderT > InvaderMaxHold || Phase != Phase.Out) DropBall();
            return;
        }
        // In open play, not in the keeper's hands, and never with an attack in the box.
        if (InvaderHalf != Half || Clock < InvaderClock || Phase != Phase.Play || Training || HeldBy != null) return;
        if (Math.Abs(Ball.Pos.X) > Pitch.HalfL - Pitch.BoxDepth && Math.Abs(Ball.Pos.Z) < Pitch.BoxHalfWidth) return;
        InvaderHalf = 0;
        InvaderT = 0;
        invaderGone = false;
        invaderTeam = Owner?.Team ?? (LastTouch?.Team ?? PossTeam);
        pendingRestart = null;
        Advantage = null;
        offsideSnapTeam = -1;
        offsideFlagged = null;
        Phase = Phase.Out;
        PhaseT = 0;
        Owner = null;
        PassTarget = null;
        Ball.Vel.Scale(0.3);
        Events.Whistle = 1;
        InvaderLookX = Ball.Pos.X;
        InvaderLookZ = Ball.Pos.Z;
        foreach (var p in Players) p.Plan = null;
        // The nearest outfield player of the side that had it will take the drop.
        invaderTaker = null;
        double best = 1e9;
        foreach (var p in Teams[invaderTeam].Players)
        {
            if (p.Role == Role.GK) continue;
            double d = M.Dist2D(p.Pos.X, p.Pos.Z, Ball.Pos.X, Ball.Pos.Z);
            if (d < best)
            {
                best = d;
                invaderTaker = p;
            }
        }
    }

    (double x, double z) DropSpot() =>
        (M.Clamp(Ball.Pos.X, -Pitch.HalfL + 1.5, Pitch.HalfL - 1.5), M.Clamp(Ball.Pos.Z, -Pitch.HalfW + 1.5, Pitch.HalfW - 1.5));

    /// <summary>While he's on (AI and the human's player in Phase.Out): the taker walks to the
    /// ball, anyone else within 4 m of it backs off, and they all watch the chase.</summary>
    internal bool InvaderWalk(Player p)
    {
        if (InvaderT < 0) return false;
        var (x, z) = DropSpot();
        double dir = Teams[p.Team].Dir;
        double tx = p.Pos.X, tz = p.Pos.Z;
        if (p == invaderTaker)
        {
            tx = x - dir * 0.9;
            tz = z;
        }
        else
        {
            double dx = p.Pos.X - x, dz = p.Pos.Z - z, d = JsMath.Hypot(dx, dz);
            if (d < 4.5)
            {
                if (d < 0.1)
                {
                    dx = -dir;
                    dz = 0;
                    d = 1;
                }
                tx = x + dx / d * 5;
                tz = z + dz / d * 5;
            }
        }
        if (InvaderT < 1.5) p.WantSpeed = Math.Max(0, p.WantSpeed - DT * 5);
        else
        {
            AI.MoveTo(p, tx, tz, false, false);
            p.WantSpeed = Math.Min(p.WantSpeed, PlayerK.JogSpeed * 0.45);
            p.Sprinting = false;
        }
        p.LookTarget.Set(InvaderLookX, 0, InvaderLookZ);
        p.LookAt = p.LookTarget;
        return true;
    }

    /// <summary>Play on: the referee drops the ball for the side that had it.</summary>
    void DropBall()
    {
        InvaderT = -1;
        invaderGone = false;
        if (Phase != Phase.Out) return; // the half ended under him: nothing to restart
        var (x, z) = DropSpot();
        Ball.Reset(x, z);
        Ball.Pos.Y = 1.1;
        Ball.PrevPos.Copy(Ball.Pos);
        Ball.OnGround = false;
        Phase = Phase.Play;
        PhaseT = 0;
        Owner = null;
        PassTarget = null;
        LastTouch = null;
        PossTeam = invaderTeam;
        Events.Whistle = 1;
        var t = invaderTaker;
        invaderTaker = null;
        if (t == null) return;
        // Still on his way (a short stoppage): he's there.
        double dir = Teams[t.Team].Dir;
        if (M.Dist2D(t.Pos.X, t.Pos.Z, x - dir * 0.9, z) > 1.5)
        {
            t.Pos.Set(x - dir * 0.9, 0, z);
            t.PrevPos.Copy(t.Pos);
            t.Vel.Set(0, 0, 0);
        }
        t.Facing = dir > 0 ? 0 : Math.PI;
        if (t.Team == HumanTeam) SetControlled(t);
    }
}
