using System;

namespace GameNight.Sim;

/// <summary>A cross as it would go: who it's for, where it comes down, its loft and curl.</summary>
public struct CrossPlan
{
    public Player? Receiver;
    public double X, Z, Angle, Curl;
}

/// <summary>The human's cross preview: where it lands and how it's struck (before the taker's error).</summary>
public sealed class CrossAimResult
{
    public double X, Z, Time;
    public V3 Vel = null!, Spin = null!;
}

public sealed partial class Match
{
    // ------------------------------------------------------------------ the strike

    /// <summary>Executes the strike: solve the ideal ball, then add the striker's error.</summary>
    public void PerformKick(Player p, KickPlan plan)
    {
        if (OffsideTouch(p)) return;
        var b = Ball;
        var team = Teams[p.Team];
        bool fromHands = HeldBy == p;
        SetPieceKind? restart = SetPiece?.Kind;
        if (fromHands)
        {
            HeldBy = null;
            b.OnGround = false;
        }
        V3 vel;
        V3 spin;
        Player? receiver = null;
        double @base = 0.05;
        double skill = p.Attrs.Passing;
        double strength = 0.4;
        // First-time technique (set by the shot): error multiplier and upward bias.
        double techErr = 1;
        double techLift = 0;

        double opp = Pitch.HalfL * team.Dir;
        SetPieceKind? setPieceKind = SetPiece?.Kind;

        // A through ball is planned now, at the strike. The computer's player looks again if the
        // run he picked has gone; with no runner left, he plays it to feet instead.
        ThroughPlan? through = null;
        if (plan.Type == KickType.Through)
        {
            ThroughPlan? Ask(Player? only) =>
                AI.PlanThrough(p, plan.DirX, plan.DirZ, plan.Aimed == true, plan.Aimed == null ? 0.5 : plan.Power, plan.Lofted == true, only);
            through = Ask(plan.TargetId >= 0 ? All[plan.TargetId] : null);
            if (through == null && plan.Aimed == null)
            {
                through = Ask(null);
                if (through == null)
                {
                    plan = plan.Clone();
                    plan.Type = KickType.Pass;
                    plan.TargetId = -1;
                    plan.Aimed = false;
                }
            }
        }

        if (plan.Type == KickType.Shot && setPieceKind == SetPieceKind.Penalty)
        {
            // Penalty: the stick picks the side (centre if it's idle), the hold picks the height and pace.
            skill = p.Attrs.Shooting;
            @base = 0.035;
            double pw = plan.Power;
            double side = Math.Abs(plan.DirZ) > 0.3 ? JsMath.Sign(plan.DirZ) : 0;
            // Aimed with the reticle: exactly there (plus the error model); otherwise stick side + hold.
            double tz = plan.AimZ ?? side * (Pitch.GoalHalfWidth - 0.45 - (1 - Math.Min(1, pw)) * 0.35);
            double ty = (plan.AimY ?? 0.25 + Math.Min(pw, 1) * 1.7) + Math.Max(0, pw - 1) * 7;
            var r = Kick.SolveShot(b.Pos, opp, ty, tz, 17 + Math.Min(pw, 1.1) * 10, 4, 0);
            vel = r.Vel;
            spin = r.Spin;
            strength = 0.6 + pw * 0.4;
            AI.PenaltyGuess(ByJob(1 - p.Team, 0), tz, ty);
        }
        else if (plan.Type == KickType.Shot && setPieceKind == SetPieceKind.FreeKick && SetPiece!.Direct)
        {
            // Direct free kick: over (or round) the wall, dipping under the bar, curling away from the keeper.
            skill = p.Attrs.Shooting * 0.6 + p.Attrs.Passing * 0.4;
            @base = 0.045;
            double pw = plan.Power;
            double near = JsMath.Or1(JsMath.Sign(b.Pos.Z));
            double side;
            if (plan.AimZ != null)
            {
                double s = JsMath.Sign(plan.AimZ.Value - b.Pos.Z * 0.15);
                side = s == 0 || double.IsNaN(s) ? -near : s;
            }
            else side = Math.Abs(plan.DirZ) > 0.3 ? JsMath.Sign(plan.DirZ) : -near; // default: over the wall, far post
            double tz = plan.AimZ ?? side * (Pitch.GoalHalfWidth - 0.55);
            // Curl: whipped away from the keeper toward the aimed side.
            double curl = -side * team.Dir * (18 + 10 * (1 - Math.Min(1, pw)));
            double speed = (19 + Math.Min(pw, 1.1) * 8) * (0.9 + 0.2 * p.Attrs.Power);
            var r = Kick.SolveFreeKick(b.Pos, opp, tz, 9.15, 2.4, speed, curl, 9 + pw * 4, plan.AimY ?? 1.15);
            vel = r.Vel;
            // Leathered it: the extra power sends it over.
            if (pw > 1) vel.Y += (pw - 1) * 9;
            spin = r.Spin;
            strength = 0.6 + pw * 0.4;
        }
        else if (plan.Type == KickType.Shot)
        {
            skill = p.Attrs.Shooting;
            @base = 0.055;
            double pw = plan.Power;
            // First time (the ball arriving, not at his feet): how it comes decides the strike.
            // On the bounce he gets over it; in the air it's a volley - hit harder, wilder, and it
            // likes to fly; across the line of an airborne ball it's hardest.
            double inc = JsMath.Hypot(b.Vel.X, b.Vel.Z);
            bool firstTime = !fromHands && Owner != p && inc > 3;
            double volley = firstTime ? M.Smoothstep(0.45, 0.9, b.Pos.Y) : 0;
            double half = firstTime ? M.Smoothstep(0.18, 0.4, b.Pos.Y) * (1 - volley) : 0;
            // Aim: stick sideways picks a post, otherwise the far post.
            double sideSign;
            if (Math.Abs(plan.DirZ) > 0.35) sideSign = JsMath.Sign(plan.DirZ);
            else sideSign = b.Pos.Z > 0.5 ? -1 : b.Pos.Z < -0.5 ? 1 : Rng.Next() < 0.5 ? -1 : 1;
            double tz = sideSign * (Pitch.GoalHalfWidth - 0.55 - (1 - Math.Min(1, pw)) * 0.4);
            bool finesse = pw < 0.55;
            double ty = 0.35 + Math.Min(pw, 1) * 1.45 + Math.Max(0, pw - 1) * 6;
            // Shot power stat: the same swing sends the ball harder.
            double speed = (15 + Math.Min(pw, 1.1) * 16) * (0.88 + 0.24 * p.Attrs.Power);
            if (firstTime)
            {
                double gx = opp - b.Pos.X;
                double gz = tz - b.Pos.Z;
                double gd = Math.Max(0.1, JsMath.Hypot(gx, gz));
                double back = -(b.Vel.X * gx + b.Vel.Z * gz) / (gd * inc); // 1 = struck straight back
                speed *= 1 + M.Clamp(inc * back * 0.01, 0, 0.12) + volley * 0.06;
                double across = Math.Sqrt(Math.Max(0, 1 - back * back));
                techErr = 1 + half * 0.25 + volley * (0.6 + 0.35 * across);
                techLift = volley * 0.05;
            }
            // Finesse shots curl back toward goal; driven shots get topspin.
            double curlDir = -JsMath.Sign(tz) * team.Dir;
            double curl = finesse ? curlDir * 28 * (1 - pw) : 0;
            double top = (finesse ? 4 : 6 + pw * 8) + half * 5 - volley * 4;
            var r = Kick.SolveShot(b.Pos, opp, ty, tz, speed, top, curl);
            vel = r.Vel;
            spin = r.Spin;
            strength = 0.5 + pw * 0.5;
        }
        else if (plan.Type == KickType.Cross && plan.LandX != null && plan.LandZ != null)
        {
            // Aimed corner or goal kick: onto the ring. The nearest team-mate attacks the landing
            // spot, timed to arrive with the ball; on a corner others take the near post, the far
            // post and the edge of the box.
            double lx = plan.LandX.Value;
            double lz = plan.LandZ.Value;
            var r = SolveDelivery(lx, lz, plan.Float == true, b.Pos);
            vel = r.Vel;
            spin = r.Spin;
            @base = plan.Float == true ? 0.035 : 0.045;
            strength = plan.Float == true ? 0.6 : 0.75;
            Player? best = null;
            double bestD = 1e9;
            foreach (var q in team.Players)
            {
                if (q == p || q.Role == Role.GK) continue;
                double d = M.Dist2D(q.Pos.X, q.Pos.Z, lx, lz);
                if (d < bestD)
                {
                    bestD = d;
                    best = q;
                }
            }
            receiver = best;
            if (best != null) AI.SetRun(best, lx, lz, r.Time + 0.6);
            bool gk = setPieceKind == SetPieceKind.GoalKick;
            if (gk)
            {
                @base = plan.Float == true ? 0.03 : 0.035;
                strength = plan.Float == true ? 0.85 : 0.95;
            }
            double gx = Pitch.HalfL * team.Dir;
            double near = JsMath.Sign(JsMath.Or1(b.Pos.Z));
            Span<double> sx = stackalloc double[] { gx - team.Dir * 5.5, gx - team.Dir * 7, gx - team.Dir * 14 };
            Span<double> sz = stackalloc double[] { near * 2.5, -near * 3.5, 0 };
            int k = gk ? 3 : 0; // (box runs are for corners)
            foreach (var q in team.Players)
            {
                if (q == p || q == best || q.Role == Role.GK || q.Role == Role.DEF || k >= 3) continue;
                if (M.Dist2D(q.Pos.X, q.Pos.Z, gx, 0) > 34) continue;
                // Skip a spot the ball is already landing on.
                if (M.Dist2D(sx[k], sz[k], lx, lz) < 3) k++;
                if (k >= 3) break;
                AI.SetRun(q, sx[k], sz[k], r.Time + 0.6);
                k++;
            }
        }
        else if ((plan.Type == KickType.Lob || plan.Type == KickType.Cross) && !fromHands && InCrossZone(p.Team, b.Pos.X, b.Pos.Z))
        {
            // Cross: a lofted ball from the wide areas near the byline is whipped into the box.
            var c = PlanCross(p, plan);
            receiver = c.Receiver;
            var r = Kick.SolveLofted(b.Pos, c.X, c.Z, c.Angle, 14, c.Curl);
            vel = r.Vel;
            spin = r.Spin;
            @base = 0.04;
            strength = 0.7;
        }
        else if (plan.Type == KickType.Clear)
        {
            double len = 38 * (0.8 + 0.4 * p.Attrs.Power);
            double tx = b.Pos.X + plan.DirX * len;
            double tz = M.Clamp(b.Pos.Z + plan.DirZ * len, -Pitch.HalfW + 3, Pitch.HalfW - 3);
            var r = Kick.SolveLofted(b.Pos, tx, tz, 34, 20, 0);
            vel = r.Vel;
            spin = r.Spin;
            @base = 0.09;
            strength = 0.9;
        }
        else if (plan.Type == KickType.Through)
        {
            // Planned through ball: into space so the runner and the ball arrive together.
            bool lofted = plan.Lofted == true;
            var tp = through;
            KickResult r;
            if (tp != null)
            {
                receiver = tp.Receiver;
                r = lofted
                    ? Kick.SolveLofted(b.Pos, tp.LandX, tp.LandZ, M.Clamp(22 + M.Dist2D(b.Pos.X, b.Pos.Z, tp.LandX, tp.LandZ) * 0.3, 26, 40), 45, 0)
                    : Kick.GroundKick(tp.Dx, tp.Dz, tp.V0);
                AI.SetRun(receiver, tp.X, tp.Z, tp.Time + 1.2);
            }
            else
            {
                // No runner: weighted into space along the stick.
                double len = 12 + 18 * plan.Power;
                double tx = M.Clamp(b.Pos.X + plan.DirX * len, -Pitch.HalfL + 2, Pitch.HalfL - 2);
                double tz = M.Clamp(b.Pos.Z + plan.DirZ * len, -Pitch.HalfW + 2, Pitch.HalfW - 2);
                r = lofted ? Kick.SolveLofted(b.Pos, tx, tz, 30, 40, 0) : Kick.SolveGroundPass(b.Pos, tx, tz, 2.5 + 1.5 * plan.Power);
            }
            vel = r.Vel;
            spin = r.Spin;
            @base = lofted ? 0.04 : 0.028;
            strength = lofted ? 0.6 : 0.4;
        }
        else
        {
            receiver =
                plan.TargetId >= 0
                    ? All[plan.TargetId]
                    : plan.Aimed == false
                        ? AI.BestReceiver(p, false)
                        : AI.PickReceiver(p, plan.DirX, plan.DirZ, false, plan.Aimed == true ? AI.AimCone : 0.35, plan.Aimed == true ? plan.Power : null);
            bool lob = plan.Type == KickType.Lob;
            // Nobody to feet where the stick points: into the path of a team-mate who gets there
            // first along it (the through-ball planner, held to the stick).
            var space = receiver == null && plan.Aimed == true && (plan.Type == KickType.Pass || lob) ? AI.PlanThrough(p, plan.DirX, plan.DirZ, true, plan.Power, lob, null) : null;
            if (space != null)
            {
                receiver = space.Receiver;
                var r = lob
                    ? Kick.SolveLofted(b.Pos, space.LandX, space.LandZ, M.Clamp(22 + M.Dist2D(b.Pos.X, b.Pos.Z, space.LandX, space.LandZ) * 0.3, 26, 40), 45, 0)
                    : Kick.GroundKick(space.Dx, space.Dz, space.V0);
                AI.SetRun(receiver, space.X, space.Z, space.Time + 1.2);
                vel = r.Vel;
                spin = r.Spin;
                @base = lob ? 0.04 : 0.028;
                strength = lob ? 0.6 : 0.4;
            }
            else if (receiver == null)
            {
                // Nobody at all: into space along the stick, as hard as it was charged.
                double len = 12 + 23 * (plan.Aimed == null ? 0.5 : plan.Power);
                double tx = M.Clamp(b.Pos.X + plan.DirX * len, -Pitch.HalfL, Pitch.HalfL);
                double tz = M.Clamp(b.Pos.Z + plan.DirZ * len, -Pitch.HalfW, Pitch.HalfW);
                var r = lob ? Kick.SolveLofted(b.Pos, tx, tz, 30, 40, 0) : Kick.SolveGroundPass(b.Pos, tx, tz, 3 + 2 * plan.Power);
                vel = r.Vel;
                spin = r.Spin;
            }
            else
            {
                // Lead the receiver: iterate target with predicted travel time.
                double tx = receiver.Pos.X;
                double tz = receiver.Pos.Z;
                bool lofted = plan.Type == KickType.Lob || plan.Type == KickType.Cross || fromHands || (setPieceKind == SetPieceKind.GoalKick && plan.Lofted != false);
                // Pass weight: AI plays a normal weight; a human tap is soft, a full hold is firm.
                double weightK = 0.8 + 0.4 * (plan.Aimed == null ? 0.5 : plan.Power);
                if (plan.Type == KickType.Cross || setPieceKind == SetPieceKind.Corner)
                {
                    // Into the box toward the receiver, a bit in front of goal.
                    tx = M.Clamp(tx, opp - team.Dir * 14, opp - team.Dir * 5);
                    tz = M.Clamp(tz, -8, 8);
                }
                var r = lofted ? Kick.SolveLofted(b.Pos, tx, tz, 30, 25, 0) : Kick.SolveGroundPass(b.Pos, tx, tz, 7);
                for (int i = 0; i < 2; i++)
                {
                    double lt = Math.Min(r.Time, 2.5) * 0.85;
                    double ax = receiver.Pos.X + receiver.Vel.X * lt;
                    double az = receiver.Pos.Z + receiver.Vel.Z * lt;
                    double dd = M.Dist2D(b.Pos.X, b.Pos.Z, ax, az);
                    if (lofted)
                    {
                        double angle = setPieceKind == SetPieceKind.GoalKick ? 34 : fromHands && SetPiece?.Kind == SetPieceKind.Throw ? 18 : M.Clamp(16 + dd * 0.45, 20, 38);
                        r = Kick.SolveLofted(b.Pos, ax, az, angle, 25, 0);
                    }
                    else
                    {
                        r = Kick.SolveGroundPass(b.Pos, ax, az, M.Clamp(5.5 + dd * 0.14, 6, 11) * weightK);
                    }
                }
                vel = r.Vel;
                spin = r.Spin;
                @base = lofted ? 0.045 : 0.03;
                strength = lofted ? 0.65 : 0.35;
            }
        }

        // ---- Error model: skill, body shape, running speed, pressure, first time.
        double kickYaw = JsMath.Atan2(vel.Z, vel.X);
        double bodyPen = M.Smoothstep(0.8, 2.6, Math.Abs(M.AngleDiff(p.Facing, kickYaw)));
        double runPen = p.Speed / 9;
        double press = Math.Max(0, 1.8 - NearestOpponentDist(p)) / 1.8;
        double relBall = JsMath.Hypot(b.Vel.X - p.Vel.X, b.Vel.Z - p.Vel.Z);
        double ballPen = M.Clamp(relBall / 12, 0, 1);
        double weak = p.KickWeak ? 1.35 : 1; // the weaker foot is less precise
        double sd = @base * (1.3 - skill) * weak * techErr * (1 + bodyPen * 2.2 + runPen * 0.7 + press * 0.9 + ballPen * 0.9);
        double yawErr = Rng.Gauss() * sd;
        double pitchErr = Rng.Gauss() * sd * (plan.Type == KickType.Shot ? 0.6 : 0.35) + techLift * (1.2 - skill);
        double speedErr = 1 + Rng.Gauss() * sd * 0.8;
        double hs = JsMath.Hypot(vel.X, vel.Z);
        double yaw = kickYaw + yawErr;
        double pitch = JsMath.Atan2(vel.Y, hs) + pitchErr;
        double sp = vel.Len() * speedErr;
        double vx = JsMath.Cos(yaw) * JsMath.Cos(pitch) * sp;
        double vz = JsMath.Sin(yaw) * JsMath.Cos(pitch) * sp;
        double vy = vel.Y == 0 && pitchErr < 0 ? 0 : JsMath.Sin(pitch) * sp;
        b.Kick(vx, Math.Max(0, vy), vz, spin.X, spin.Y, spin.Z);

        Events.Kicks.Add(strength);
        Log?.Invoke($"{F1(Time)} T{p.Team} #{p.Index} {plan.Type.ToString().ToLowerInvariant()}{(receiver != null ? " -> #" + receiver.Index : "")} from {F0(b.Pos.X)},{F0(b.Pos.Z)} v={F1(b.Vel.Len())}");
        Owner = null;
        LastTouch = p;
        LastKicker = p;
        LastKickTime = Time;
        PassTarget = receiver;
        ShotBy = plan.Type == KickType.Shot ? p : null;
        p.TouchCooldown = 0.35;
        p.SinceTouch = 0;
        p.TouchH = 0;
        if (SetPiece != null)
        {
            // The wall jumps as the ball's struck (most of them) and holds its shape for a moment.
            if (SetPiece.Wall != null)
            {
                foreach (var w in SetPiece.Wall.Players)
                {
                    if (Rng.Next() < 0.8) w.StartAction(ActionKind.Header, 0.62, 0, 0);
                    w.TouchCooldown = 0;
                }
                wallUntil = Time + 0.7;
            }
            SetPiece = null;
            Phase = Phase.Play;
        }
        JudgeOffside(p, true, restart);
        if (receiver != null && receiver.Team == HumanTeam) SetControlled(receiver);
    }

    // ------------------------------------------------------------------ ball contact

    bool WantsBall(Player p)
    {
        if (p == Owner) return true;
        // Our own shot on its way to goal: let it through (the body can still block it).
        if (p.Team == ShotTeam()) return false;
        // The wall blocks with its body; it doesn't try to play the ball.
        if (Time < wallUntil && p.Action == ActionKind.Header) return false;
        if (p.Plan != null) return true;
        if (Owner != null && Owner.Team == p.Team) return false;
        if (PassTarget == p) return true;
        if (p == Controlled) return true;
        // Caught beyond the line: leave it for someone onside.
        if (OffsideFlagged(p)) return false;
        if (AI.Chaser[p.Team] == p) return true;
        if (p.Role == Role.GK) return true;
        // Opponents of the pass target will happily intercept.
        if (PassTarget != null && PassTarget.Team != p.Team) return true;
        return Owner == null && BallDist(p) < 1.2;
    }

    void BallTouches()
    {
        var b = Ball;
        double h = b.Pos.Y;
        if (HeldBy != null) return;

        // Keepers first: saves and catches.
        foreach (var t in Teams)
        {
            if (t.Players.Count == 0) continue;
            var k = t.Players[0];
            if (k.Role == Role.GK && AI.KeeperContact(k)) return;
        }

        // Closest eligible player gets the touch; a high ball both sides can reach is a duel.
        Player? best = null;
        double bestD = 1e9;
        Player? rival = null;
        double rivalD = 1e9;
        foreach (var p in Players)
        {
            if (p.TouchCooldown > 0) continue;
            var ac = p.Action;
            if (ac == ActionKind.Stumble || ac == ActionKind.Fall || ac == ActionKind.Slide || ac == ActionKind.Dive || ac == ActionKind.Kick || ac == ActionKind.Throw) continue;
            double d = BallDist(p);
            double headMax = p.HeadReach;
            bool headZone = h > PlayerK.ControlHeight && h < headMax;
            double attack = headZone ? AttackingBall(p, d) : 0;
            double reach = headZone ? 0.6 + 0.25 * attack : PlayerK.Reach + (h < 0.5 ? StretchReach(p) : 0);
            if (d > reach || h > headMax) continue;
            if (!WantsBall(p))
            {
                // Body deflection for anyone in the way. Jumping (a wall, a block) reaches higher.
                double top = p.Action == ActionKind.Header ? 2.3 : 1.85;
                if (d < PlayerK.Radius + BallK.Radius && h < top && p != LastKicker)
                {
                    if (OffsideTouch(p)) return;
                    Deflect(p);
                }
                continue;
            }
            // Close control by the owner: opponents must tackle, not just touch.
            if (Owner != null && Owner != p && Owner.Team != p.Team && BallDist(Owner) < PlayerK.Reach) continue;
            if (p.Plan != null && h < 1.0) continue; // the plan will strike it
            // In the air the better jumper / taller player, the one attacking the ball, and the
            // stronger body in the challenge win it.
            double score = headZone ? d - (p.Aerial - 0.5) * 0.35 - attack * 0.2 - (p.DuelStrength - 0.5) * 0.15 : d;
            if (score < bestD)
            {
                if (best != null && best.Team != p.Team)
                {
                    rival = best;
                    rivalD = bestD;
                }
                bestD = score;
                best = p;
            }
            else if (best != null && p.Team != best.Team && score < rivalD)
            {
                rival = p;
                rivalD = score;
            }
        }
        if (best == null) return;
        var pl = best;
        bool challenged = false;
        if (rival != null && h > PlayerK.ControlHeight)
        {
            // An aerial duel: both go up for it. The other still jumps into him, and whoever wins
            // it plays it under that challenge.
            if (rivalD + Rng.Gauss() * 0.12 < bestD) (pl, rival) = (rival, pl);
            double dx = Ball.Pos.X - rival.Pos.X;
            double dz = Ball.Pos.Z - rival.Pos.Z;
            double dd = Math.Max(0.01, JsMath.Hypot(dx, dz));
            rival.StartAction(ActionKind.Header, 0.4, dx / dd, dz / dd);
            rival.TouchCooldown = 0.45;
            challenged = true;
        }
        if (OffsideTouch(pl)) return;
        if (h > PlayerK.ControlHeight)
        {
            // Head it only when he means to (or must); otherwise he takes it down. A ball dropping
            // onto him from above chest height he lets come down onto the chest.
            if (challenged || ShouldHead(pl)) Header(pl, challenged);
            else if (h > ChestTop && Ball.Vel.Y < -0.5 && DropsOnto(pl)) return;
            else if (h > ChestMax) Header(pl, false);
            else ControlTouch(pl);
            return;
        }
        if (pl == Owner) DribbleTouch(pl);
        else ControlTouch(pl);
    }

    /// <summary>
    /// How hard a player is going at a ball in the air, 0..1: running onto it (a jump with a
    /// run-up reaches further and lands with more force), or pressing for it.
    /// </summary>
    double AttackingBall(Player p, double d)
    {
        var b = Ball.Pos;
        double closing = d > 0.01 ? (p.Vel.X * (b.X - p.Pos.X) + p.Vel.Z * (b.Z - p.Pos.Z)) / d : 0;
        double pressing = p == Controlled && PressHeld ? 0.5 : 0;
        return M.Clamp(closing / 4 + pressing, 0, 1);
    }

    void Deflect(Player p)
    {
        var b = Ball;
        double dx = b.Pos.X - p.Pos.X;
        double dz = b.Pos.Z - p.Pos.Z;
        double d = Math.Max(0.01, JsMath.Hypot(dx, dz));
        double nx = dx / d;
        double nz = dz / d;
        double rv = (b.Vel.X - p.Vel.X) * nx + (b.Vel.Z - p.Vel.Z) * nz;
        if (rv >= 0) return;
        b.Vel.X -= 1.45 * rv * nx;
        b.Vel.Z -= 1.45 * rv * nz;
        b.Vel.X *= 0.55;
        b.Vel.Z *= 0.55;
        b.Vel.Y = Math.Abs(b.Vel.Y) * 0.4 + Math.Abs(rv) * 0.1;
        if (b.Vel.Y > 0.5) b.OnGround = false;
        b.Spin.Scale(0.3);
        b.Pos.X = p.Pos.X + nx * (PlayerK.Radius + BallK.Radius + 0.01);
        b.Pos.Z = p.Pos.Z + nz * (PlayerK.Radius + BallK.Radius + 0.01);
        LastTouch = p;
        LastKicker = p;
        JudgeOffside(p, false);
        PassTarget = null;
        if (Owner != null && Owner != p) Owner = null;
        Events.Kicks.Add(M.Clamp(-rv / 25, 0.1, 0.6));
    }

    /// <summary>Direction the player wants to take the ball (from stick or AI).</summary>
    bool DribbleDir(Player p, V3 output)
    {
        if (p.WantSpeed > 0.3 && (p.MoveX != 0 || p.MoveZ != 0))
        {
            output.Set(p.TouchX, 0, p.TouchZ);
            return true;
        }
        output.Set(JsMath.Cos(p.Facing), 0, JsMath.Sin(p.Facing));
        return false;
    }

    void DribbleTouch(Player p)
    {
        var b = Ball;
        bool moving = DribbleDir(p, tmpV);
        double dx = tmpV.X;
        double dz = tmpV.Z;
        double ps = p.Speed;
        // Only touch when the ball is not already running away ahead of us.
        double relAlong = (b.Vel.X - p.Vel.X) * dx + (b.Vel.Z - p.Vel.Z) * dz;
        double toBallX = b.Pos.X - p.Pos.X;
        double toBallZ = b.Pos.Z - p.Pos.Z;
        double ahead = toBallX * dx + toBallZ * dz;
        if (moving && relAlong > 0.6 && ahead > 0.15) return;

        double ctrl = p.Attrs.Control;
        if (moving)
        {
            bool sprint = p.Sprinting && ps > PlayerK.JogSpeed;
            // Push the ball so the player meets it again on a later stride: the ball must cover
            // what the player covers in T seconds while grass and air slow it down.
            double target = Math.Max(ps, Math.Min(p.WantSpeed, ps + 2.5) * 0.85);
            // The human's player keeps it closer: shorter touches, more of them.
            bool human = p == Controlled && !AutoPlay;
            double T = human ? (sprint ? 0.75 : target > 4 ? 0.5 : 0.4) : sprint ? 1.15 : target > 4 ? 0.8 : 0.6;
            double vEst = target + 1;
            double decel = BallK.RollDecel + 0.025 * vEst * vEst;
            double touchSpeed = target + (decel * T) / 2 + 0.35;
            // Changing direction at speed makes touches less precise.
            double ballYaw = ps > 0.5 ? JsMath.Atan2(p.Vel.Z, p.Vel.X) : JsMath.Atan2(dz, dx);
            double turn = Math.Abs(M.AngleDiff(ballYaw, JsMath.Atan2(dz, dx)));
            double sd = (0.035 + (1 - ctrl) * 0.09) * (1 + turn * (ps / 6) * 1.5) * (sprint ? 1.4 : 1) * (human ? 0.5 : 1);
            double a = JsMath.Atan2(dz, dx) + Rng.Gauss() * sd;
            double s = touchSpeed * (1 + Rng.Gauss() * sd * 0.6);
            b.Kick(JsMath.Cos(a) * s, 0, JsMath.Sin(a) * s, 0, 0, 0);
            b.Spin.Set(JsMath.Sin(a) * s / BallK.Radius, 0, (-JsMath.Cos(a) * s) / BallK.Radius);
            p.TouchCooldown = sprint ? 0.32 : 0.2;
        }
        else
        {
            // Settle the ball under the body.
            b.Kick(p.Vel.X * 0.75, 0, p.Vel.Z * 0.75, 0, 0, 0);
            p.TouchCooldown = 0.25;
        }
        p.SinceTouch = 0;
        p.TouchH = 0;
        LastTouch = p;
        JudgeOffside(p);
        Events.Kicks.Add(0.08);
    }

    /// <summary>Extra reach of a leg stretched out for the ball, following the stretch's extension.</summary>
    public double StretchReach(Player p)
    {
        if (p.Action != ActionKind.Stretch) return 0;
        return StretchReachMax * Player.StretchExt(p.ActionT, p.ActionDur);
    }

    /// <summary>
    /// The last-ditch reach: a player going for a loose ball that's about to pass just beyond
    /// his feet (it never comes within reach but does come within a leg's length) sticks a leg
    /// out for it, timed so the leg is out when the ball is closest.
    /// </summary>
    void TryStretches()
    {
        if (Phase != Phase.Play || HeldBy != null || Owner != null || Ball.Pos.Y > 0.6) return;
        bool sampled = false;
        foreach (var p in Players)
        {
            if (p.Action != ActionKind.None || p.TouchCooldown > 0 || p.Plan != null || p.Role == Role.GK) continue;
            double d = BallDist(p);
            if (d < PlayerK.Reach || d > 2.4) continue;
            if (p != Controlled && PassTarget != p && AI.Chaser[p.Team] != p) continue;
            if (!WantsBall(p)) continue;
            if (!sampled)
            {
                sampled = true;
                int k = 0;
                var pb = Kick.LoadPrediction(Ball);
                double maxT = StretchN * 0.05 + 1e-6;
                double t = 0;
                while (t < maxT)
                {
                    if (t + 1e-6 >= (k + 1) * 0.05 && k < StretchN)
                    {
                        stretchX[k] = pb.Pos.X;
                        stretchY[k] = pb.Pos.Y;
                        stretchZ[k] = pb.Pos.Z;
                        k++;
                    }
                    if (k >= StretchN) break;
                    pb.Step(DT * 2);
                    t += DT * 2;
                }
                for (; k < StretchN; k++)
                {
                    stretchX[k] = Ball.Pos.X;
                    stretchY[k] = Ball.Pos.Y;
                    stretchZ[k] = Ball.Pos.Z;
                }
            }
            double best = 9;
            int bi = -1;
            for (int k = 0; k < StretchN; k++)
            {
                if (stretchY[k] > 0.5) continue;
                double tk = (k + 1) * 0.05;
                double dk = JsMath.Hypot(stretchX[k] - (p.Pos.X + p.Vel.X * tk), stretchZ[k] - (p.Pos.Z + p.Vel.Z * tk));
                if (dk < best)
                {
                    best = dk;
                    bi = k;
                }
            }
            // It'll come to him anyway, or it's beyond any leg; or the moment isn't here yet.
            if (bi < 0 || best <= PlayerK.Reach * 0.95 || best > PlayerK.Reach + StretchReachMax * 0.9 || bi > 3) continue;
            double ts = (bi + 1) * 0.05;
            double rx = stretchX[bi] - (p.Pos.X + p.Vel.X * ts);
            double rz = stretchZ[bi] - (p.Pos.Z + p.Vel.Z * ts);
            double r = Math.Max(0.01, JsMath.Hypot(rx, rz));
            double cf = JsMath.Cos(p.Facing);
            double sf = JsMath.Sin(p.Facing);
            p.KickBallF = rx * cf + rz * sf;
            p.KickBallL = -rx * sf + rz * cf;
            // The leg on the ball's side (the good foot if it's straight ahead).
            p.KickLeg = Math.Abs(p.KickBallL) < 0.25 ? p.Foot : p.KickBallL > 0 ? 1 : -1;
            p.StartAction(ActionKind.Stretch, StretchDur, rx / r, rz / r);
        }
    }

    void ControlTouch(Player p)
    {
        var b = Ball;
        double h = b.Pos.Y;
        double relX = b.Vel.X - p.Vel.X;
        // Chest and thigh give way under a dropping ball: they soak up half its fall.
        double relY = b.Vel.Y * (h > 0.5 ? 0.5 : 1);
        double relZ = b.Vel.Z - p.Vel.Z;
        double rel = Math.Sqrt(relX * relX + relY * relY + relZ * relZ);
        double q = p.Attrs.Control;
        double heightPen = h > 0.55 ? 0.6 : 0;
        // Taken on an outstretched leg: a toe-poke, not a cushioned touch.
        double stretched = M.Clamp((BallDist(p) - PlayerK.Reach) / StretchReachMax, 0, 1);
        double err = rel * (0.045 + (1 - q) * 0.08 + heightPen * 0.05) * Math.Abs(1 + Rng.Gauss() * 0.5) * (1 + 1.3 * stretched);
        DribbleDir(p, tmpV);
        bool moving = p.WantSpeed > 0.3;
        double push = (moving ? 1.0 + p.Speed * 0.15 : 0.3) * (1 - 0.6 * stretched);
        double ea = Rng.Next() * Math.PI * 2;
        b.Kick(p.Vel.X * 0.95 + tmpV.X * push + JsMath.Cos(ea) * err, 0, p.Vel.Z * 0.95 + tmpV.Z * push + JsMath.Sin(ea) * err, 0, 0, 0);
        if (h > 0.3)
        {
            b.Vel.Y = -0.3;
            b.OnGround = false;
        }
        p.TouchCooldown = 0.18;
        p.SinceTouch = 0;
        p.TouchH = h;
        // (The leg on the ball's side takes it, if it's a thigh.)
        p.KickLeg = -JsMath.Sin(p.Facing) * (b.Pos.X - p.Pos.X) + JsMath.Cos(p.Facing) * (b.Pos.Z - p.Pos.Z) >= 0 ? 1 : -1;
        Owner = p;
        LastTouch = p;
        JudgeOffside(p);
        PassTarget = null;
        PossTeam = p.Team;
        Events.Kicks.Add(M.Clamp(rel / 30, 0.05, 0.4));
        if (p.Team == HumanTeam) SetControlled(p);
    }

    /// <summary>
    /// Automatic headers are for real heading situations: a queued Pass/Shoot, a chance in
    /// front of goal, a clearance under pressure, or a contested ball. A free high ball is
    /// chested down instead, which stops endless heading rallies.
    /// </summary>
    bool ShouldHead(Player p)
    {
        var b = Ball;
        if (p.Plan != null) return true;
        var team = Teams[p.Team];
        double distGoal = M.Dist2D(b.Pos.X, b.Pos.Z, Pitch.HalfL * team.Dir, 0);
        if (distGoal < 18) return true;
        double press = NearestOpponentDist(p);
        bool ownThird = b.Pos.X * team.Dir < -Pitch.HalfL / 3;
        if (ownThird && press < 4) return true;
        return press < 1.8;
    }

    /// <summary>Whether a ball coming down will still be on him by the time it's at chest height.</summary>
    bool DropsOnto(Player p)
    {
        var b = Ball;
        double t = (b.Pos.Y - 1.35) / -b.Vel.Y;
        double dx = b.Pos.X + b.Vel.X * t - (p.Pos.X + p.Vel.X * t);
        double dz = b.Pos.Z + b.Vel.Z * t - (p.Pos.Z + p.Vel.Z * t);
        return JsMath.Hypot(dx, dz) < 0.75;
    }

    void Header(Player p, bool challenged = false)
    {
        var b = Ball;
        var team = Teams[p.Team];
        double gx = Pitch.HalfL * team.Dir;
        double distGoal = M.Dist2D(b.Pos.X, b.Pos.Z, gx, 0);
        double dirX;
        double dirZ;
        double speed;
        double up;
        bool wantShot = (p.Plan?.Type == KickType.Shot || distGoal < (p == Controlled ? 13 : 16)) && distGoal < 20;
        if (wantShot)
        {
            double tz = (Rng.Next() < 0.5 ? -1 : 1) * (Pitch.GoalHalfWidth - 0.8);
            dirX = gx - b.Pos.X;
            dirZ = tz - b.Pos.Z;
            speed = 10 + p.Attrs.Shooting * 5 + p.Attrs.Power * 3;
            up = -0.08;
        }
        else
        {
            var recv = AI.PickReceiver(p, p.Plan != null ? p.Plan.DirX : team.Dir, p.Plan != null ? p.Plan.DirZ : 0, false);
            if (recv != null && M.Dist2D(recv.Pos.X, recv.Pos.Z, b.Pos.X, b.Pos.Z) < 22)
            {
                dirX = recv.Pos.X - b.Pos.X;
                dirZ = recv.Pos.Z - b.Pos.Z;
            }
            else
            {
                dirX = team.Dir;
                dirZ = -b.Pos.Z * 0.02;
            }
            speed = 9 + Math.Min(1, JsMath.Hypot(dirX, dirZ) / 25) * 5;
            // Nod it down to a teammate's feet; only clearances go high.
            bool clearing = b.Pos.X * team.Dir < -Pitch.HalfL / 3 && NearestOpponentDist(p) < 4;
            up = clearing ? 0.35 : 0.05;
        }
        double d = Math.Max(0.01, JsMath.Hypot(dirX, dirZ));
        // Won under a challenge, it comes off the head less cleanly and with less on it.
        double sd = (0.08 + (1 - (p.Attrs.Control * 0.4 + p.Aerial * 0.6)) * 0.12) * (challenged ? 2.2 : 1);
        if (challenged) speed *= 0.85;
        double a = JsMath.Atan2(dirZ / d, dirX / d) + Rng.Gauss() * sd;
        b.Kick(JsMath.Cos(a) * speed, speed * up + Rng.Gauss() * (challenged ? 1.2 : 0.6), JsMath.Sin(a) * speed, 0, 0, 0);
        b.OnGround = false;
        p.StartAction(ActionKind.Header, 0.4, JsMath.Cos(a), JsMath.Sin(a));
        p.Plan = null;
        p.TouchCooldown = 0.4;
        p.SinceTouch = 0;
        p.TouchH = 0;
        Owner = null;
        LastTouch = p;
        LastKicker = p;
        LastKickTime = Time;
        JudgeOffside(p);
        PassTarget = null;
        ShotBy = wantShot ? p : null;
        Events.Kicks.Add(0.35);
    }

    // ------------------------------------------------------------------ crosses

    /// <summary>Within 40 m of one of the corner flags this team attacks (and in their half).</summary>
    public bool InCrossZone(int team, double x, double z)
    {
        double dir = Teams[team].Dir;
        if (x * dir < 12) return false;
        double gx = Pitch.HalfL * dir;
        return Math.Min(M.Dist2D(x, z, gx, Pitch.HalfW), M.Dist2D(x, z, gx, -Pitch.HalfW)) < 40;
    }

    /// <summary>
    /// Picks who the cross is for and where to put it: aimed so the ball arrives around head
    /// height as the receiver attacks it, driven for short crosses, floated for long ones, and
    /// curling away from the keeper. The stick (if pushed) steers which runner it's for.
    /// </summary>
    public CrossPlan PlanCross(Player p, KickPlan plan)
    {
        var c = PickCross(p, plan);
        var best = c.Receiver;
        if (best != null)
        {
            // Others attack the near post, far post and the edge of the area.
            var team = Teams[p.Team];
            double dir = team.Dir;
            double gx = Pitch.HalfL * dir;
            double near = JsMath.Sign(JsMath.Or1(Ball.Pos.Z));
            Span<double> sx = stackalloc double[] { gx - dir * 5.5, gx - dir * 7, gx - dir * 13 };
            Span<double> sz = stackalloc double[] { near * 2.5, -near * 3.5, 0 };
            int k = 0;
            foreach (var q in team.Players)
            {
                if (q == p || q == best || q.Role == Role.GK || q.Role == Role.DEF || k >= 3) continue;
                if (M.Dist2D(q.Pos.X, q.Pos.Z, gx, 0) > 34) continue;
                AI.SetRun(q, sx[k], sz[k]);
                k++;
            }
        }
        return c;
    }

    /// <summary>
    /// The human's cross as it would go now (Pass held and slid up, on the ball in the crossing
    /// zone): where it comes down and how it's struck, before the taker's error. Null otherwise.
    /// </summary>
    public CrossAimResult? CrossAim(double moveX, double moveY)
    {
        var p = Controlled;
        if (AutoPlay || Phase != Phase.Play || Owner != p || HeldBy == p) return null;
        if (!InCrossZone(p.Team, Ball.Pos.X, Ball.Pos.Z)) return null;
        // The stick as the button handler reads it.
        double m = JsMath.Hypot(moveX, moveY);
        bool aimed = m > 0.12;
        var plan = new KickPlan
        {
            Type = KickType.Lob,
            DirX = aimed ? moveX / m : JsMath.Cos(p.Facing),
            DirZ = aimed ? -moveY / m : JsMath.Sin(p.Facing),
            Power = 0.5,
            TargetId = -1,
            Expires = 0,
            Aimed = aimed,
        };
        var c = PickCross(p, plan);
        var r = Kick.SolveLofted(Ball.Pos, c.X, c.Z, c.Angle, 14, c.Curl);
        return new CrossAimResult { X = c.X, Z = c.Z, Vel = r.Vel, Spin = r.Spin, Time = r.Time };
    }

    /// <summary>The cross itself (no side effects): who it's for, where it lands, its loft and curl.</summary>
    CrossPlan PickCross(Player p, KickPlan plan)
    {
        var b = Ball;
        var team = Teams[p.Team];
        double dir = team.Dir;
        double gx = Pitch.HalfL * dir;
        Player? best = null;
        double bestS = -1e9;
        foreach (var q in team.Players)
        {
            if (q == p || q.Role == Role.GK) continue;
            // Where he'll be when the ball gets there.
            double qx = q.Pos.X + q.Vel.X * 0.9;
            double qz = q.Pos.Z + q.Vel.Z * 0.9;
            double depth0 = (gx - qx) * dir; // metres from the goal line
            if (depth0 > 24 || depth0 < 1 || Math.Abs(qz) > 22) continue;
            // The computer doesn't pick out a man standing offside (the human might).
            if (plan.Aimed != true && q.Pos.X * dir > AI.OffsideLineFor(p.Team) + OffsideMargin) continue;
            double open = 99;
            foreach (var o in Teams[1 - p.Team].Players) open = Math.Min(open, M.Dist2D(o.Pos.X, o.Pos.Z, qx, qz));
            double toGoal = M.Dist2D(qx, qz, gx, 0);
            double sc = -toGoal * 0.12 + M.Clamp(open / 3, 0, 1.2) + q.Attrs.Strength * 0.3 + (q.Role == Role.FWD ? 0.4 : 0);
            if (plan.Aimed == true)
            {
                double dx = qx - b.Pos.X;
                double dz = qz - b.Pos.Z;
                double dd = Math.Max(0.1, JsMath.Hypot(dx, dz));
                sc += ((dx * plan.DirX + dz * plan.DirZ) / dd) * 1.5;
            }
            if (sc > bestS)
            {
                bestS = sc;
                best = q;
            }
        }
        // Aim point: the receiver's run, kept to the dangerous zone (between the six-yard line
        // and the penalty spot, inside the posts' width plus a bit). No one there: the spot.
        double tx;
        double tz;
        if (best != null)
        {
            double flight = 1.0 + M.Dist2D(b.Pos.X, b.Pos.Z, best.Pos.X, best.Pos.Z) / 30;
            tx = best.Pos.X + best.Vel.X * flight * 0.8;
            tz = best.Pos.Z + best.Vel.Z * flight * 0.8;
        }
        else
        {
            tx = gx - dir * 10;
            tz = -JsMath.Sign(JsMath.Or1(b.Pos.Z)) * 2;
        }
        double depth = M.Clamp((gx - tx) * dir, 4.5, 15);
        tx = gx - dir * depth;
        tz = M.Clamp(tz, -10, 10);
        // Land a couple of metres beyond him so it reaches him at head height.
        double fx = tx - b.Pos.X;
        double fz = tz - b.Pos.Z;
        double d = Math.Max(1, JsMath.Hypot(fx, fz));
        fx /= d;
        fz /= d;
        double beyond = d > 22 ? 2.6 : 1.8;
        double lx = tx + fx * beyond;
        double lz = tz + fz * beyond;
        double angle = M.Clamp(12 + d * 0.45, 17, 31);
        // Curl away from the goal (and the keeper): right of travel is (-fz, fx).
        double curlSign = JsMath.Or1(JsMath.Sign(-fz * -dir));
        double curl = curlSign * (10 + Math.Min(10, d * 0.3));
        return new CrossPlan { Receiver = best, X = lx, Z = lz, Angle = angle, Curl = curl };
    }

    /// <summary>Keeper secures the ball in his hands.</summary>
    public void CatchBall(Player k)
    {
        if (k.Role == Role.GK && k.Action == ActionKind.None && Phase == Phase.Play)
        {
            k.CatchY = Ball.Pos.Y;
            k.StartAction(ActionKind.Catch, 0.45, 0, 0);
        }
        HeldBy = k;
        Owner = null;
        PassTarget = null;
        LastTouch = k;
        JudgeOffside(k);
        PossTeam = k.Team;
        Ball.Vel.Set(0, 0, 0);
        Ball.Spin.Set(0, 0, 0);
        if (k.Team == HumanTeam) SetControlled(k);
    }

    // ------------------------------------------------------------------ rules

    void CheckOutOfPlay()
    {
        var b = Ball;
        var p = b.Pos;
        // Goal.
        if (b.InGoal && Math.Abs(p.X) > Pitch.HalfL + BallK.Radius)
        {
            int side = p.X > 0 ? 1 : -1;
            int scoringTeam = Teams[0].Dir == side ? 0 : 1;
            Teams[scoringTeam].Score++;
            Scorer = LastTouch != null && LastTouch.Team == scoringTeam ? LastTouch : ByJob(scoringTeam, 9);
            Phase = Phase.Goal;
            PhaseT = 0;
            Owner = null;
            PassTarget = null;
            Events.Goal = scoringTeam;
            Events.Whistle = 1;
            Celebration = null;
            // The computer's scorers pick their own (most of the time; the rest do the classic).
            if (scoringTeam != HumanTeam || AutoPlay)
            {
                int h = (Teams[0].Score * 7 + Teams[1].Score * 13 + Scorer.Id * 5) % 6;
                if (h < 4) PickCelebration(Celebrations[h], GoalSeq.Front - 0.3);
            }
            return;
        }
        if (Math.Abs(p.Z) > Pitch.HalfW + BallK.Radius)
        {
            int team = LastTouch != null ? 1 - LastTouch.Team : 0;
            BallOut(SetPieceKind.Throw, team, M.Clamp(p.X, -Pitch.HalfL + 1, Pitch.HalfL - 1), JsMath.Sign(p.Z) * (Pitch.HalfW + 0.3));
            return;
        }
        if (Math.Abs(p.X) > Pitch.HalfL + BallK.Radius && !b.InGoal)
        {
            int side = p.X > 0 ? 1 : -1;
            int defending = Teams[0].Dir == side ? 1 : 0;
            int attacking = 1 - defending;
            if (LastTouch != null && LastTouch.Team == defending)
                BallOut(SetPieceKind.Corner, attacking, side * (Pitch.HalfL - 0.4), JsMath.Sign(JsMath.Or1(p.Z)) * (Pitch.HalfW - 0.4));
            else
                BallOut(SetPieceKind.GoalKick, defending, side * (Pitch.HalfL - 5.5), JsMath.Sign(JsMath.Or1(p.Z)) * 5);
        }
    }

    // ------------------------------------------------------------------ clock

    public int DisplayMinute
    {
        get
        {
            double m = (Clock / MatchK.HalfSeconds) * 45;
            return (int)Math.Floor(m) + (Half == 2 ? 45 : 0);
        }
    }

    /// <summary>This half's added minutes.</summary>
    public double AddedTime => Added[Half - 1];

    /// <summary>The clock as TV shows it: 23', or 45+2' once the half runs into added time.</summary>
    public string ClockLabel
    {
        get
        {
            int end = Half == 1 ? 45 : 90;
            int min = DisplayMinute;
            return min < end ? $"{min}'" : $"{end}+{(int)Math.Min(min - end, AddedTime - 1) + 1}'";
        }
    }
}
