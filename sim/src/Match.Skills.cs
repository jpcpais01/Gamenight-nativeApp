using System;

namespace GameNight.Sim;

/// <summary>
/// Skill moves (slide SPRINT on the ball, one move per slot; the computer's dribblers use them too). A move is
/// an action (ActionKind.Trick) playing one of the Skills scripts: through it his run is the
/// script's, the ball rides his foot along the script's path (nobody can just walk in and take
/// it, though a tackle still can), at the feint the men in front may buy the dummy, and at the
/// release the ball goes back to the physics with a real touch toward where he's going.
/// </summary>
public sealed partial class Match
{
    /// <summary>Starts a move if he can do one now: on the ball at his feet, in open play. `sx, sz`
    /// is where he wants to go (the stick, world), `idle` when the stick's left alone. `move` is
    /// the one asked for (a human's slot); None lets the stick and his stars pick.</summary>
    public bool TryTrick(Player p, double sx, double sz, bool idle, SkillMove move = SkillMove.None)
    {
        if (Phase != Phase.Play || Owner != p || HeldBy != null || SetPiece != null || p.IsBusy || p.Role == Role.GK) return false;
        if (Time < p.TrickReady || !Ball.OnGround || Ball.Pos.Y > 0.3 || BallDist(p) > 1.15) return false;
        double fx = JsMath.Cos(p.Facing), fz = JsMath.Sin(p.Facing);
        double v0 = Math.Min(7, p.Speed);
        double fwd = idle ? 0 : sx * fx + sz * fz;
        double lat = idle ? 0 : sx * fz - sz * fx;
        // The exit side: where the stick leans; straight on (or no stick), away from the nearest man.
        int e;
        if (!idle && Math.Abs(lat) > 0.15) e = lat > 0 ? 1 : -1;
        else
        {
            var o = NearestOpponent(p);
            double ol = o != null ? (o.Pos.X - p.Pos.X) * fz - (o.Pos.Z - p.Pos.Z) * fx : 0;
            e = o == null || Math.Abs(ol) < 0.05 ? (Rng.Next() < 0.5 ? 1 : -1) : ol > 0 ? -1 : 1;
        }
        if (move == SkillMove.None) move = Skills.Pick(Math.Max(1, p.Attrs.Skill), idle, fwd, v0, Rng.Next());
        var tm = Skills.TimingOf(move);

        p.StartAction(ActionKind.Trick, tm.Dur, fx, fz);
        p.Trick = move;
        p.TrickSide = e;
        p.TrickV0 = v0;
        p.TrickReleased = false;
        p.TrickFeinted = false;
        p.Plan = null;
        p.Sprinting = false;
        // Out of it toward the script's exit, bent toward the stick on the moves that go forward.
        double ex = fx * tm.ExitF + fz * tm.ExitL * e;
        double ez = fz * tm.ExitF - fx * tm.ExitL * e;
        bool turn = move is SkillMove.DragBack or SkillMove.CruyffTurn or SkillMove.Roulette;
        if (!idle && !turn && fwd > -0.2)
        {
            ex = ex * 0.6 + sx * 0.4;
            ez = ez * 0.6 + sz * 0.4;
        }
        double en = JsMath.Or1(JsMath.Hypot(ex, ez));
        p.TrickExitX = ex / en;
        p.TrickExitZ = ez / en;
        // Where the ball is now in the move's frame: the script eases it from there.
        double bx = Ball.Pos.X - p.Pos.X, bz = Ball.Pos.Z - p.Pos.Z;
        p.TrickBallF = bx * fx + bz * fz;
        p.TrickBallL = (bx * fz - bz * fx) * e;
        p.TrickReady = Time + (Piloted(p) && !AutoPlay ? 0.9 : 6);
        return true;
    }

    Player? NearestOpponent(Player p)
    {
        Player? best = null;
        double bd = 1e9;
        foreach (var o in Teams[1 - p.Team].Players)
        {
            double d = M.Dist2D(o.Pos.X, o.Pos.Z, p.Pos.X, p.Pos.Z);
            if (d < bd)
            {
                bd = d;
                best = o;
            }
        }
        return best;
    }

    /// <summary>Before locomotion: the movers run their scripts, the fooled lean the wrong way.</summary>
    void TricksIntent()
    {
        foreach (var p in Players)
        {
            if (p.FooledT > 0) Fooled(p);
            if (p.Action != ActionKind.Trick) continue;
            // Lost it (a tackle, out of play): the move's over.
            if (Phase != Phase.Play || (!p.TrickReleased && Owner != p))
            {
                p.Action = ActionKind.None;
                continue;
            }
            var tm = Skills.TimingOf(p.Trick);
            double t = p.ActionT + DT;
            double fx = p.ActionDirX, fz = p.ActionDirZ, e = p.TrickSide;
            double vx, vz;
            if (!p.TrickReleased && t < tm.Release + 0.02)
            {
                Skills.Script(p.Trick, t, p.TrickV0, out double bf, out double bl, out _, out _, out _, out _);
                vx = fx * bf + fz * bl * e;
                vz = fz * bf - fx * bl * e;
            }
            else
            {
                double v = Skills.ExitSpeed(p.Trick, p.TrickV0);
                vx = p.TrickExitX * v;
                vz = p.TrickExitZ * v;
            }
            double sp = JsMath.Hypot(vx, vz);
            p.MoveX = sp > 1e-3 ? vx / sp : 0;
            p.MoveZ = sp > 1e-3 ? vz / sp : 0;
            p.WantSpeed = sp;
            p.LookAt = null;
            p.SquareUp = false;
            p.Burst = false;
            p.Sprinting = sp > PlayerK.JogSpeed + 0.5;
        }
    }

    /// <summary>After locomotion: the body turns as the script says.</summary>
    void TricksFacing()
    {
        foreach (var p in Players)
        {
            if (p.Action != ActionKind.Trick) continue;
            Skills.Script(p.Trick, p.ActionT, p.TrickV0, out _, out _, out double yaw, out _, out _, out _);
            double fx = p.ActionDirX, fz = p.ActionDirZ, e = p.TrickSide;
            double c = JsMath.Cos(yaw), s = JsMath.Sin(yaw);
            p.Facing = JsMath.Atan2(fz * c - fx * s * e, fx * c + fz * s * e);
        }
    }

    /// <summary>After the ball's step: the ball rides the mover's foot until the release; the dummy.</summary>
    void TricksBall()
    {
        var p = Owner;
        if (p == null || p.Action != ActionKind.Trick || p.TrickReleased) return;
        var tm = Skills.TimingOf(p.Trick);
        double t = p.ActionT;
        if (!p.TrickFeinted && t >= tm.Feint) SellDummy(p, tm);
        if (BallDist(p) > 1.6 || HeldBy != null)
        {
            p.TrickReleased = true;
            return;
        }
        if (t >= tm.Release)
        {
            ReleaseTrick(p, tm);
            return;
        }
        Skills.Script(p.Trick, t, p.TrickV0, out _, out _, out _, out double bf, out double bl, out double by);
        double k = M.Smoothstep(0, 0.1, t);
        bf = p.TrickBallF + (bf - p.TrickBallF) * k;
        bl = p.TrickBallL + (bl - p.TrickBallL) * k;
        double fx = p.ActionDirX, fz = p.ActionDirZ, e = p.TrickSide;
        var b = Ball;
        double nx = p.Pos.X + fx * bf + fz * bl * e;
        double nz = p.Pos.Z + fz * bf - fx * bl * e;
        b.Vel.Set((nx - b.PrevPos.X) / DT, (by - b.PrevPos.Y) / DT, (nz - b.PrevPos.Z) / DT);
        b.Pos.Set(nx, by, nz);
        b.OnGround = by <= BallK.Radius + 0.005;
        if (b.OnGround) b.Vel.Y = 0;
        b.Spin.Set(b.Vel.Z / BallK.Radius, 0, -b.Vel.X / BallK.Radius);
        LastTouch = p;
    }

    void ReleaseTrick(Player p, Skills.Timing tm)
    {
        p.TrickReleased = true;
        var b = Ball;
        double ex = p.TrickExitX, ez = p.TrickExitZ;
        double v = Skills.ExitSpeed(p.Trick, p.TrickV0);
        double ctrl = p.Attrs.Control;
        if (p.Trick == SkillMove.Rainbow)
        {
            // Up and over: high enough to clear a man, far enough that he runs onto it.
            double vy = 6.4 + 0.5 * ctrl;
            double vh = v * 0.92 + 0.4;
            double a = JsMath.Atan2(ez, ex) + Rng.Gauss() * (0.05 + (1 - ctrl) * 0.08);
            b.Kick(JsMath.Cos(a) * vh, vy, JsMath.Sin(a) * vh, 0, 0, 0);
            b.Spin.Set(0, 0, 0);
            Events.Kicks.Add(0.25);
            Owner = null;
            PassTarget = p;
            LastKicker = p;
            LastKickTime = Time;
            LastKickFoot = true;
            p.TouchCooldown = 0.45;
        }
        else
        {
            // A touch he'll meet again in stride (as DribbleTouch weighs it).
            const double T = 0.45;
            double vEst = v + 1;
            double decel = BallK.RollDecel + 0.025 * vEst * vEst;
            double s = v + (decel * T) / 2 + 0.35;
            double sd = 0.025 + (1 - ctrl) * 0.07;
            double a = JsMath.Atan2(ez, ex) + Rng.Gauss() * sd;
            s *= 1 + Rng.Gauss() * sd * 0.5;
            b.Kick(JsMath.Cos(a) * s, 0, JsMath.Sin(a) * s, 0, 0, 0);
            b.Spin.Set(JsMath.Sin(a) * s / BallK.Radius, 0, -JsMath.Cos(a) * s / BallK.Radius);
            p.TouchCooldown = Math.Max(0.18, tm.Dur - tm.Release);
        }
        p.SinceTouch = 0;
        p.TouchH = 0;
        LastTouch = p;
        JudgeOffside(p);
        if (!Piloted(p) || AutoPlay) AI.SetDribble(p, ex, ez);
    }

    /// <summary>
    /// The feint: each man in front of him close enough to see it may buy it, more likely against
    /// a harder move, a more skilful man, better touch against worse defending, and the closer he
    /// is. Bought, he's wrong-footed for a moment (no tackle in that time); against the best moves
    /// he can be left on the floor.
    /// </summary>
    void SellDummy(Player p, Skills.Timing tm)
    {
        p.TrickFeinted = true;
        int tier = Skills.Tier(p.Trick);
        int stars = Math.Max(1, p.Attrs.Skill);
        double fx = p.ActionDirX, fz = p.ActionDirZ, e = p.TrickSide;
        double best = 0;
        foreach (var q in Teams[1 - p.Team].Players)
        {
            bool keeper = q.Role == Role.GK;
            if (keeper && p.Trick != SkillMove.FakeShot) continue;
            var ac = q.Action;
            if (ac != ActionKind.None && ac != ActionKind.Tackle) continue;
            double dx = q.Pos.X - p.Pos.X, dz = q.Pos.Z - p.Pos.Z;
            double d = JsMath.Hypot(dx, dz);
            if (d > (keeper ? 14 : 4.5) || d < 0.01) continue;
            double front = (dx * fx + dz * fz) / d;
            if (front < -0.25 && d > 1.8) continue;
            double chance = 0.4 + 0.1 * tier + 0.06 * (stars - 3) + 0.55 * (p.Attrs.Control - q.Attrs.Defending) - 0.05 * Math.Max(0, d - 2.5);
            if (keeper) chance = 0.35 + 0.06 * (stars - 3) - 0.3 * (q.Attrs.Keeping - 0.7);
            if (Rng.Next() >= M.Clamp(chance, 0.08, 0.9)) continue;
            q.FooledT = 0.3 + 0.14 * tier + (keeper ? 0.15 : 0);
            q.FoolKind = tm.Fool;
            // The dummy side: the way the move first went.
            q.FoolX = -fz * e;
            q.FoolZ = fx * e;
            best = Math.Max(best, tier);
            // On his heels and turned the wrong way: now and then he goes down.
            if (!keeper && ac == ActionKind.None && d < 3 && Rng.Next() < 0.08 + 0.17 * (tier - 1))
            {
                q.StartAction(ActionKind.Stumble, 0.7, q.FoolX, q.FoolZ);
                best = Math.Max(best, tier + 1);
            }
        }
        if (best > 0) Events.Skill = Math.Max(Events.Skill, best);
    }

    /// <summary>Wrong-footed: whatever he meant to do, for a moment he can't.</summary>
    void Fooled(Player q)
    {
        q.FooledT -= DT;
        if (q.IsBusy) return;
        switch (q.FoolKind)
        {
            case Skills.Fool.Side:
            {
                double mx = q.MoveX * 0.25 + q.FoolX * 0.75;
                double mz = q.MoveZ * 0.25 + q.FoolZ * 0.75;
                double n = JsMath.Or1(JsMath.Hypot(mx, mz));
                q.MoveX = mx / n;
                q.MoveZ = mz / n;
                q.WantSpeed = M.Clamp(q.WantSpeed, 2, 4);
                q.AccelScale = 0.55;
                break;
            }
            case Skills.Fool.Freeze:
                q.WantSpeed = 0;
                break;
            case Skills.Fool.Overrun:
            {
                double sp = q.Speed;
                if (sp > 0.5)
                {
                    q.MoveX = q.Vel.X / sp;
                    q.MoveZ = q.Vel.Z / sp;
                    q.WantSpeed = sp;
                }
                q.AccelScale = 0.5;
                break;
            }
        }
    }
}
