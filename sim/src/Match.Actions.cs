using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameNight.Sim;

/// <summary>The tackling leg as a capsule on the ground: hip (A) to boot (B).</summary>
public struct Leg
{
    public double Ax, Az, Bx, Bz;
}

public sealed partial class Match
{
    static string F1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
    static string F0(double v) => v.ToString("F0", CultureInfo.InvariantCulture);

    /// <summary>Ground distance from (x, z) to a segment.</summary>
    static double SegDist(double x, double z, in Leg s)
    {
        double vx = s.Bx - s.Ax;
        double vz = s.Bz - s.Az;
        double l2 = vx * vx + vz * vz;
        double t = l2 > 1e-9 ? M.Clamp(((x - s.Ax) * vx + (z - s.Az) * vz) / l2, 0, 1) : 0;
        return JsMath.Hypot(x - (s.Ax + vx * t), z - (s.Az + vz * t));
    }

    static bool Down(Player p) => p.Action == ActionKind.Stumble || p.Action == ActionKind.Fall;

    // ------------------------------------------------------------------ physics between players

    void CollidePlayers()
    {
        var ps = Players;
        const double minD = PlayerK.Radius * 2;
        for (int i = 0; i < ps.Count; i++)
        {
            var a = ps[i];
            var ap = a.Pos;
            for (int j = i + 1; j < ps.Count; j++)
            {
                var b = ps[j];
                double dx = b.Pos.X - ap.X;
                if (dx >= minD || dx <= -minD) continue;
                double dz = b.Pos.Z - ap.Z;
                double d2 = dx * dx + dz * dz;
                if (d2 >= minD * minD || d2 < 1e-8) continue;
                double d = Math.Sqrt(d2);
                double nx = dx / d;
                double nz = dz / d;
                double overlap = minD - d;
                // Stronger players move less.
                double wa = 1 - a.DuelStrength * 0.5;
                double wb = 1 - b.DuelStrength * 0.5;
                double sa = wa / (wa + wb);
                a.Pos.X -= nx * overlap * sa;
                a.Pos.Z -= nz * overlap * sa;
                b.Pos.X += nx * overlap * (1 - sa);
                b.Pos.Z += nz * overlap * (1 - sa);
                // Remove closing velocity.
                double rv = (b.Vel.X - a.Vel.X) * nx + (b.Vel.Z - a.Vel.Z) * nz;
                if (rv < 0)
                {
                    a.Vel.X += nx * rv * sa;
                    a.Vel.Z += nz * rv * sa;
                    b.Vel.X -= nx * rv * (1 - sa);
                    b.Vel.Z -= nz * rv * (1 - sa);
                    // A real shoulder-to-shoulder impact can knock the weaker / slower-braced player
                    // off balance (and off the ball).
                    if (rv < -3.2) Bump(a, b, -rv);
                }
            }
        }
    }

    void Bump(Player a, Player b, double impact)
    {
        if (a.Team == b.Team || a.BalanceCD > 0 || b.BalanceCD > 0) return;
        // Who gives way: strength, body weight, momentum into the contact and a little luck.
        double sa = a.DuelStrength + (a.Speed * a.Attrs.Weight) / 1500 + Rng.Next() * 0.35;
        double sb = b.DuelStrength + (b.Speed * b.Attrs.Weight) / 1500 + Rng.Next() * 0.35;
        var loser = sa < sb ? a : b;
        a.BalanceCD = b.BalanceCD = 1.2;
        if (Rng.Next() > M.Clamp((impact - 3.2) / 3, 0.15, 0.75)) return;
        if (loser.Action == ActionKind.None) loser.StartAction(ActionKind.Stumble, 0.4 + impact * 0.04, 0, 0);
        if (Owner == loser)
        {
            Owner = null;
            var b2 = Ball;
            b2.Vel.X += (Rng.Next() - 0.5) * 3;
            b2.Vel.Z += (Rng.Next() - 0.5) * 3;
        }
        Events.Tackle = Math.Max(Events.Tackle, 0.5);
    }

    /// <summary>
    /// The ground a player must give at the other side's dead ball, until it's played: 9.15 m
    /// round a free kick (a wall stands right on it), 9.15 m from the corner arc, and at a
    /// kick-off his own half, outside the centre circle. Null when he can go where he likes.
    /// The returned zone is reused: read it before asking again.
    /// </summary>
    public RestartZone? GetRestartZone(Player p)
    {
        var sp = SetPiece;
        if (sp == null || p.Team == sp.Team) return null;
        var zn = zone;
        zn.Half = false;
        if (Phase == Phase.Kickoff)
        {
            zn.X = zn.Z = 0;
            zn.R = Pitch.CircleRadius;
            zn.Half = true;
        }
        else if (Phase == Phase.SetPiece && sp.Kind == SetPieceKind.FreeKick)
        {
            zn.X = sp.X;
            zn.Z = sp.Z;
            zn.R = Pitch.CircleRadius;
        }
        else if (Phase == Phase.SetPiece && sp.Kind == SetPieceKind.Corner)
        {
            zn.X = JsMath.Sign(sp.X) * Pitch.HalfL;
            zn.Z = JsMath.Sign(sp.Z) * Pitch.HalfW;
            zn.R = Pitch.CircleRadius + 1;
        }
        else return null;
        return zn;
    }

    /// <summary>
    /// An invisible line round the restart: nobody gets any closer than he already was, and
    /// nobody crosses into the zone. Whoever's caught inside it can only walk out.
    /// </summary>
    void KeepRestartDistance()
    {
        foreach (var p in Players)
        {
            var zn = GetRestartZone(p);
            if (zn == null) continue;
            double dx = p.Pos.X - zn.X;
            double dz = p.Pos.Z - zn.Z;
            double d = JsMath.Hypot(dx, dz);
            double lim = Math.Min(zn.R, JsMath.Hypot(p.PrevPos.X - zn.X, p.PrevPos.Z - zn.Z));
            if (d < lim && d > 1e-3)
            {
                double nx = dx / d;
                double nz = dz / d;
                p.Pos.X = zn.X + nx * lim;
                p.Pos.Z = zn.Z + nz * lim;
                double vn = p.Vel.X * nx + p.Vel.Z * nz;
                if (vn < 0)
                {
                    p.Vel.X -= nx * vn;
                    p.Vel.Z -= nz * vn;
                }
            }
            if (zn.Half)
            {
                double dir = Teams[p.Team].Dir;
                double max = Math.Max(p.PrevPos.X * dir, 0);
                if (p.Pos.X * dir > max)
                {
                    p.Pos.X = max * dir;
                    if (p.Vel.X * dir > 0) p.Vel.X = 0;
                }
            }
        }
    }

    /// <summary>
    /// A keeper with the ball in his hands can carry it anywhere in his area but not out of it
    /// (a ratchet, like the restart distance: caught at the edge, he just can't go further).
    /// </summary>
    void KeepHeldInBox()
    {
        var k = HeldBy;
        if (k == null || k.Role != Role.GK || SetPiece != null) return;
        double own = -Teams[k.Team].Dir;
        double edge = Math.Max(Pitch.BoxDepth - 0.6, Pitch.HalfL - k.PrevPos.X * own);
        if (Pitch.HalfL - k.Pos.X * own > edge)
        {
            k.Pos.X = own * (Pitch.HalfL - edge);
            if (k.Vel.X * own < 0) k.Vel.X = 0;
        }
        double side = Math.Max(Pitch.BoxHalfWidth - 0.6, Math.Abs(k.PrevPos.Z));
        if (Math.Abs(k.Pos.Z) > side)
        {
            double s = JsMath.Sign(k.Pos.Z);
            k.Pos.Z = s * side;
            if (k.Vel.Z * s > 0) k.Vel.Z = 0;
        }
    }

    void ConfineToPitch()
    {
        const double lx = Pitch.HalfL + 4;
        const double lz = Pitch.HalfW + 3;
        foreach (var p in Players)
        {
            if (p.Pos.X > lx) p.Pos.X = lx;
            if (p.Pos.X < -lx) p.Pos.X = -lx;
            if (p.Pos.Z > lz) p.Pos.Z = lz;
            if (p.Pos.Z < -lz) p.Pos.Z = -lz;
        }
    }

    // ------------------------------------------------------------------ actions

    /// <summary>
    /// How long a strike takes: wind-up + swing to contact, then the follow-through. A pass is
    /// a short, compact swing; a driven shot a full back-lift with the knee whipping through;
    /// a dead-ball shot the fullest of all. Throws keep their old quick timing.
    /// </summary>
    public (double contact, double follow) KickTiming(KickPlan plan)
    {
        bool dead = SetPiece != null && (SetPiece.Kind == SetPieceKind.Penalty || SetPiece.Direct) && plan.Type == KickType.Shot;
        if (HeldBy != null && HeldBy.Plan == plan)
        {
            // Throw-ins keep their quick timing. The keeper: a punt drops the ball onto the foot;
            // a roll bends down and bowls it; an overarm throw winds back behind the head.
            if (SetPiece?.Kind == SetPieceKind.Throw) return (0.15, 0.12);
            if (plan.Type == KickType.Lob || plan.Type == KickType.Clear) return (0.4, 0.42);
            return plan.Lofted == false ? (0.38, 0.42) : (0.34, 0.4);
        }
        switch (plan.Type)
        {
            case KickType.Shot:
                return dead ? (0.24, 0.42) : (0.18 + 0.03 * Math.Min(1, plan.Power), 0.34);
            case KickType.Lob:
            case KickType.Cross:
            case KickType.Clear:
                return (0.16, 0.3);
            case KickType.Through:
                return plan.Lofted == true ? (0.16, 0.28) : (0.13, 0.2);
            default:
                return (0.12, 0.2);
        }
    }

    /// <summary>Where the ball will be `dt` seconds from now: its real flight from its current state.</summary>
    V3 BallAt(double dt, V3 output)
    {
        output.Copy(Ball.Pos);
        var b = Kick.LoadPrediction(Ball);
        double maxT = dt + 1e-6;
        double t = 0;
        while (t < maxT)
        {
            output.Copy(b.Pos);
            if (t >= dt) break;
            b.Step(DT * 2);
            t += DT * 2;
        }
        return output;
    }

    /// <summary>
    /// Can he strike the ball `contactIn` seconds from now? Judged on where the ball will
    /// really be then (its predicted flight: dropping, bouncing), so a pass is met in stride
    /// and a dropping ball can be volleyed (shots: up to waist height and a bit).
    /// </summary>
    bool Kickable(Player p, double contactIn = 0.12, double reach = PlayerK.Reach, double maxH = 1.0)
    {
        if (HeldBy == p) return true;
        if (HeldBy != null) return false;
        var at = BallAt(contactIn, tmpK);
        if (at.Y > maxH) return false;
        double d = JsMath.Hypot(at.X - (p.Pos.X + p.Vel.X * 0.8 * contactIn), at.Z - (p.Pos.Z + p.Vel.Z * 0.8 * contactIn));
        if (d > reach) return false;
        if (SetPiece != null && SetPiece.Taker != p) return false;
        return true;
    }

    void ResolveActions(Player p)
    {
        bool human = p == Controlled && !AutoPlay;
        // Expire stale plans, and drop them if the other side has won the ball.
        if (p.Plan != null && Time > p.Plan.Expires) p.Plan = null;
        if (p.Plan != null && ((Owner != null && Owner.Team != p.Team) || (HeldBy != null && HeldBy.Team != p.Team))) p.Plan = null;

        // Start a kick when the ball arrives in range: the wind-up and swing take `contact`,
        // the follow-through the rest.
        if (p.Plan != null)
        {
            var timing = KickTiming(p.Plan);
            double reach = human ? HumanStrikeReach : PlayerK.Reach;
            double maxH = p.Plan.Type == KickType.Shot && SetPiece == null ? 1.25 : 1.0;
            if (!p.IsBusy && (p.TouchCooldown <= 0 || p.SinceTouch > 0.12) && Kickable(p, timing.contact, reach, maxH))
            {
                if (SetPiece != null && (SetPiece.Taker != p || SetPiece.T < 0.7)) return;
                var plan = p.Plan;
                double dur = timing.contact + timing.follow;
                // From the hands: a throw (or a throw-in), except keepers punt long balls.
                bool fromHands = HeldBy == p;
                bool punt = fromHands && SetPiece == null && (plan.Type == KickType.Lob || plan.Type == KickType.Clear);
                var kind = fromHands && !punt ? ActionKind.Throw : ActionKind.Kick;
                p.ThrowIn = SetPiece?.Kind == SetPieceKind.Throw;
                // Strike with the preferred foot, unless the ball is well over on the other side
                // (then it's the weaker one). Dead balls: always the good foot.
                double side = -JsMath.Sin(p.Facing) * (Ball.Pos.X - p.Pos.X) + JsMath.Cos(p.Facing) * (Ball.Pos.Z - p.Pos.Z);
                int ballSide = side >= 0 ? 1 : -1;
                p.KickLeg = SetPiece != null || Math.Abs(side) < 0.32 || ballSide == p.Foot ? p.Foot : ballSide;
                p.KickWeak = p.KickLeg != p.Foot;
                p.StartAction(kind, dur, plan.DirX, plan.DirZ);
                p.KickContact = kind == ActionKind.Throw && p.ThrowIn ? dur * 0.55 : timing.contact;
                p.KickType = plan.Type;
                p.KickPower = plan.Power;
                p.KickLofted = plan.Type == KickType.Lob || plan.Type == KickType.Cross || plan.Type == KickType.Clear || plan.Lofted == true;
                p.KickRel = M.AngleDiff(p.Facing, JsMath.Atan2(plan.DirZ, plan.DirX));
                p.KickHeight = HeldBy == p ? 0 : BallAt(timing.contact, tmpK).Y;
                // How far he has to reach: the ball at contact against where his hips will be. Beyond
                // a comfortable foot reach (~0.5 m) he lunges for it through the wind-up.
                p.KickVX = p.Vel.X;
                p.KickVZ = p.Vel.Z;
                p.LungeX = p.LungeZ = 0;
                p.KickStretch = p.KickBallF = p.KickBallL = 0;
                if (kind == ActionKind.Kick && HeldBy != p)
                {
                    double hx = p.Pos.X + p.Vel.X * 0.8 * timing.contact;
                    double hz = p.Pos.Z + p.Vel.Z * 0.8 * timing.contact;
                    double rx = tmpK.X - hx;
                    double rz = tmpK.Z - hz;
                    double r = JsMath.Hypot(rx, rz);
                    double cf = JsMath.Cos(p.Facing);
                    double sf = JsMath.Sin(p.Facing);
                    p.KickBallF = rx * cf + rz * sf;
                    p.KickBallL = -rx * sf + rz * cf;
                    p.KickStretch = M.Clamp((r - 0.5) / 0.7, 0, 1);
                    if (r > 0.5)
                    {
                        double l = Math.Min(3.5, (r - 0.5) / Math.Max(0.1, timing.contact));
                        p.LungeX = (rx / r) * l;
                        p.LungeZ = (rz / r) * l;
                    }
                }
            }
        }

        if ((p.Action == ActionKind.Kick || p.Action == ActionKind.Throw) && !p.ActionDone && p.ActionT >= p.KickContact)
        {
            p.ActionDone = true;
            var plan = p.Plan;
            p.Plan = null;
            double contact = human ? HumanContactReach : PlayerK.Reach + 0.35;
            if (plan != null && (HeldBy == p || (BallDist(p) < contact && Ball.Pos.Y < 1.45))) PerformKick(p, plan);
            else if (plan != null && Time < plan.Expires) p.Plan = plan; // missed it: stay queued and try again
        }

        if ((p.Action == ActionKind.Tackle || p.Action == ActionKind.Slide) && !p.ActionDone && Phase == Phase.Play)
        {
            bool slide = p.Action == ActionKind.Slide;
            if (TackleLeg(p, out var leg))
            {
                var b = Ball;
                // The opponent it can catch: the carrier, or someone who's just got rid of it.
                Player? victim = Owner != null && Owner.Team != p.Team ? Owner
                    : LastKicker != null && LastKicker.Team != p.Team && Time - LastKickTime < 0.6 ? LastKicker : null;
                // Contact means real contact: the leg capsule against his legs (not a radius round him).
                bool bodyHit = victim != null && !Down(victim) && SegDist(victim.Pos.X, victim.Pos.Z, leg) < (slide ? SlideLegR : TackleLegR) + VictimLegR;
                bool ballHit = HeldBy == null && b.Pos.Y < (slide ? 0.5 : 0.6) && SegDist(b.Pos.X, b.Pos.Z, leg) < (slide ? SlideSweepR : TackleLegR) + BallK.Radius + 0.04;
                if (ballHit || bodyHit)
                {
                    p.ActionDone = true;
                    // The leg meets his legs: what that does to him is physics (see LegImpact).
                    int knock = bodyHit ? LegImpact(p, victim!, slide) : 0;
                    if (ballHit) ResolveTackle(p, slide, bodyHit, knock, leg);
                    else
                    {
                        // Missed the ball but caught the man.
                        bool late = victim != Owner;
                        if (Phase == Phase.Play && Rng.Next() < FoulChance(p, victim!, slide, false) + (late ? 0.2 : 0) + knock * 0.12) CommitFoul(p, victim!, slide, late);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The tackling leg as a capsule on the ground (hip to boot), extending and withdrawing
    /// on the same timeline as the animation: a standing tackle reaches ~0.85 m in front of
    /// the body (plus the lunge), a slide's straight leg ~1.05 m. False while the leg isn't out.
    /// </summary>
    bool TackleLeg(Player p, out Leg leg)
    {
        bool slide = p.Action == ActionKind.Slide;
        double t = p.ActionT;
        // A slide's leg is out from just after he drops until the slide dies; a block tackle's
        // swings out and back over the action.
        double ext = slide
            ? M.Smoothstep(0.04, 0.14, t) * (1 - M.Smoothstep(p.SlideStop - 0.05, p.SlideStop + 0.15, t))
            : M.Smoothstep(0.12, 0.42, t / p.ActionDur) * (1 - M.Smoothstep(0.62, 0.9, t / p.ActionDur));
        leg = default;
        if (ext < 0.35) return false;
        double lx = p.LegX;
        double lz = p.LegZ;
        // (A slide's capsule runs back up the body on the grass: a ball into his hip is stopped too.)
        double from = slide ? -0.6 : 0.15;
        double to = slide ? 0.2 + 0.85 * ext : 0.25 + 0.6 * ext;
        leg = new Leg { Ax = p.Pos.X + lx * from, Az = p.Pos.Z + lz * from, Bx = p.Pos.X + lx * to, Bz = p.Pos.Z + lz * to };
        return true;
    }

    /// <summary>
    /// The tackling leg meets the victim's legs. An inelastic hit: the closing speed of the
    /// leg into him, shared by the two bodies' masses, is the shove his feet get. Taken at the
    /// ankles (a slide) that shove has the most leverage to tip him; a block tackle meets him
    /// higher and less squarely. He resists with strength and footing. Past his balance he goes
    /// down; half of it is a stumble; less, he rides it. Returns 0 (nothing), 1 (stumble) or 2 (down).
    /// </summary>
    int LegImpact(Player p, Player victim, bool slide)
    {
        double dx = p.ActionDirX;
        double dz = p.ActionDirZ;
        double closing = Math.Max(0, (p.Vel.X - victim.Vel.X) * dx + (p.Vel.Z - victim.Vel.Z) * dz);
        double mp = p.Attrs.Weight;
        double mv = victim.Attrs.Weight;
        double shove = (closing * mp) / (mp + mv); // m/s given to his feet
        double leverage = slide ? 1.0 : 0.55;
        double footing = 1 - M.Clamp(victim.Speed / 7, 0, 1); // 1 = planted, 0 = sprinting on one foot
        double balance = 1.1 + victim.DuelStrength * 1.6 + footing * 0.9;
        double e = shove * leverage;
        if (e > balance)
        {
            double hard = M.Clamp(e - balance, 0, 3);
            victim.StartAction(ActionKind.Fall, 1.15 + hard * 0.3, -dx, -dz);
            victim.Vel.X = victim.Vel.X * 0.6 + dx * shove * 0.5;
            victim.Vel.Z = victim.Vel.Z * 0.6 + dz * shove * 0.5;
            victim.TouchCooldown = victim.ActionDur;
            victim.Plan = null;
            if (Owner == victim) Owner = null;
            return 2;
        }
        if (e > balance * 0.5)
        {
            victim.StartAction(ActionKind.Stumble, 0.35 + (e / balance) * 0.3, 0, 0);
            victim.Vel.X += dx * shove * 0.3;
            victim.Vel.Z += dz * shove * 0.3;
            return 1;
        }
        return 0;
    }

    /// <summary>How the challenge comes in, relative to the victim's run: 1 = straight from behind.</summary>
    double FromBehind(Player p, Player victim)
    {
        double vf = victim.Speed > 1 ? JsMath.Atan2(victim.Vel.Z, victim.Vel.X) : victim.Facing;
        return Math.Max(0, JsMath.Cos(JsMath.Atan2(p.ActionDirZ, p.ActionDirX) - vf));
    }

    /// <summary>
    /// Chance that a challenge is a foul. Losing the duel and still going through means
    /// contact; from behind, at speed and on the floor it's much likelier; good defenders time
    /// it better. Winning the ball cleanly is fine, unless it's a slide through the back of him.
    /// </summary>
    double FoulChance(Player p, Player victim, bool slide, bool wonBall)
    {
        double behind = FromBehind(p, victim);
        if (wonBall) return slide && behind > 0.6 ? 0.22 * behind : 0;
        double f = slide ? 0.5 : 0.26;
        f += behind * (slide ? 0.4 : 0.28);
        f += M.Clamp((p.Speed - 5) / 4, 0, 1) * 0.14;
        f -= p.Attrs.Defending * 0.16;
        return M.Clamp(f, 0.02, 0.92);
    }

    /// <summary>
    /// The team whose shot is in flight, or -1. A shot belongs to the goal: until somebody
    /// else touches it (a block, a deflection, the keeper), the shooter's teammates neither
    /// play it nor run onto it; it can still hit them on the way through.
    /// </summary>
    public int ShotTeam()
    {
        var s = ShotBy;
        if (s == null || LastTouch != s || Owner != null || HeldBy != null || Phase != Phase.Play) return -1;
        if (Time - LastKickTime > 2.5 || JsMath.Hypot(Ball.Vel.X, Ball.Vel.Z) < 5) return -1;
        return s.Team;
    }

    /// <summary>Penalty area of the goal `team` defends.</summary>
    public bool InPenaltyArea(int team, double x, double z)
    {
        double gx = -Teams[team].Dir * Pitch.HalfL;
        return Math.Abs(x - gx) < Pitch.BoxDepth && Math.Abs(z) < Pitch.BoxHalfWidth;
    }

    /// <summary>
    /// The referee's call. The victim goes down; a reckless one (from behind, on the floor, at
    /// speed, late) is a yellow card; in the box it's a penalty. If the fouled side still has a
    /// promising attack he waves play on, and comes back for the free kick if it breaks down.
    /// </summary>
    void CommitFoul(Player off, Player victim, bool slide, bool late)
    {
        double x = M.Clamp(victim.Pos.X, -Pitch.HalfL + 0.5, Pitch.HalfL - 0.5);
        double z = M.Clamp(victim.Pos.Z, -Pitch.HalfW + 0.5, Pitch.HalfW - 0.5);
        if (victim.Action != ActionKind.Fall) victim.StartAction(ActionKind.Stumble, 1.1, 0, 0);
        victim.TouchCooldown = Math.Max(victim.TouchCooldown, 1.1);
        victim.Plan = null;
        double severity = (slide ? 0.35 : 0.1) + FromBehind(off, victim) * 0.4 + M.Clamp((off.Speed - 5) / 4, 0, 1) * 0.25 + (late ? 0.2 : 0);
        bool yellow = Rng.Next() < M.Clamp((severity - 0.5) * 1.5, 0, 0.85);
        if (yellow)
        {
            Cards[off.Id]++;
            Events.Card = 1;
        }
        bool penalty = InPenaltyArea(off.Team, x, z);
        LastFoul = new Foul { Offender = off, Victim = victim, X = x, Z = z, Yellow = yellow, Penalty = penalty, Time = Time };
        Log?.Invoke($"{F1(Time)} FOUL by T{off.Team} #{off.Index} on #{victim.Index}{(yellow ? " (yellow)" : "")}{(penalty ? " PENALTY" : "")}");
        if (!penalty && AdvantageOn(victim.Team, victim))
        {
            Advantage = new Advantage { Team = victim.Team, X = x, Z = z, Penalty = penalty, Until = Time + 3 };
            Events.Foul = 2;
            return;
        }
        Events.Foul = 1;
        WhistleFoul(victim.Team, x, z, penalty);
    }

    /// <summary>Would stopping play hurt the fouled team? (They're attacking and will get to the ball first.)</summary>
    bool AdvantageOn(int team, Player victim)
    {
        double dir = Teams[team].Dir;
        if (Ball.Pos.X * dir < -12) return false;
        double mine = 9;
        double theirs = 9;
        foreach (var p in Players)
        {
            if (p == victim || p.Action == ActionKind.Stumble || p.Action == ActionKind.Fall || p.Action == ActionKind.Slide) continue;
            var it = AI.Intercept[p.Id];
            double t = it.T >= 0 ? it.T : 9;
            if (p.Team == team) mine = Math.Min(mine, t);
            else theirs = Math.Min(theirs, t);
        }
        return mine < theirs - 0.35;
    }

    /// <summary>Advantage: if the fouled team loses the ball soon after, go back for the free kick.</summary>
    void WatchAdvantage()
    {
        var a = Advantage!;
        if (Phase != Phase.Play || Time > a.Until)
        {
            Advantage = null;
            return;
        }
        var o = Owner ?? HeldBy;
        if (o != null && o.Team != a.Team)
        {
            Events.Foul = 1;
            WhistleFoul(a.Team, a.X, a.Z, a.Penalty);
        }
    }

    void WhistleFoul(int team, double x, double z, bool penalty)
    {
        if (penalty) BallOut(SetPieceKind.Penalty, team, Teams[team].Dir * (Pitch.HalfL - Pitch.PenaltySpot), 0);
        else BallOut(SetPieceKind.FreeKick, team, x, z);
        // The referee stops it: the ball's taken out of play rather than left to roll on.
        Ball.Vel.Scale(0.3);
    }

    /// <summary>Was this player beyond the line when his team last played the ball (and not yet onside again)?</summary>
    public bool OffsideFlagged(Player p) => offsideSnapTeam == p.Team && offsideFlagged != null && offsideFlagged.Contains(p);

    /// <summary>About to play the ball: if he was caught offside, the flag goes up instead.</summary>
    bool OffsideTouch(Player p)
    {
        if (Phase != Phase.Play || !OffsideFlagged(p)) return false;
        int def = 1 - p.Team;
        double x = M.Clamp(p.Pos.X, -Pitch.HalfL + 0.5, Pitch.HalfL - 0.5);
        double z = M.Clamp(p.Pos.Z, -Pitch.HalfW + 0.5, Pitch.HalfW - 0.5);
        LastOffside = new Offside { Player = p, Team = def, X = x, Z = z, Time = Time };
        Log?.Invoke($"{F1(Time)} OFFSIDE T{p.Team} #{p.Index} at {F0(x)},{F0(z)}");
        Events.Offside = 1;
        // Indirect free kick to the defenders where he became involved.
        BallOut(SetPieceKind.FreeKick, def, x, z);
        Ball.Vel.Scale(0.3);
        return true;
    }

    /// <summary>
    /// `p` has played the ball: the moment that judges his teammates. An opponent's deflection or
    /// save leaves the earlier judgement standing; nobody is offside straight from a throw-in,
    /// corner or goal kick.
    /// </summary>
    void JudgeOffside(Player p, bool deliberate = true, SetPieceKind? restart = null)
    {
        if (offsideSnapTeam >= 0 && offsideSnapTeam != p.Team && !deliberate) return;
        if (Phase != Phase.Play || Training || restart == SetPieceKind.Throw || restart == SetPieceKind.Corner || restart == SetPieceKind.GoalKick)
        {
            offsideSnapTeam = -1;
            offsideFlagged = null;
            return;
        }
        double dir = Teams[p.Team].Dir;
        // Second-last opponent (the keeper usually being the last), the ball and halfway.
        double a = -1e9;
        double bb = -1e9;
        foreach (var q in Teams[1 - p.Team].Players)
        {
            double v = q.Pos.X * dir;
            if (v > a)
            {
                bb = a;
                a = v;
            }
            else if (v > bb) bb = v;
        }
        double line = Math.Max(Math.Max(bb, Ball.Pos.X * dir), 0) + OffsideMargin;
        var flagged = offsideSnapTeam == p.Team && offsideFlagged != null ? offsideFlagged : new List<Player>();
        flagged.Clear();
        foreach (var q in Teams[p.Team].Players) if (q != p && q.Pos.X * dir > line) flagged.Add(q);
        offsideSnapTeam = p.Team;
        offsideFlagged = flagged;
    }

    /// <summary>Debug: a foul for the human team where the ball is right now.</summary>
    public void DebugFoul()
    {
        if (Phase != Phase.Play) return;
        var b = Ball.Pos;
        Player Near(int team)
        {
            var best = ByJob(team, 1);
            double bd = 1e9;
            foreach (var p in Teams[team].Players)
            {
                if (p.Role == Role.GK) continue;
                double d = M.Dist2D(p.Pos.X, p.Pos.Z, b.X, b.Z);
                if (d < bd)
                {
                    bd = d;
                    best = p;
                }
            }
            return best;
        }
        var victim = Near(HumanTeam);
        var off = Near(1 - HumanTeam);
        double x = M.Clamp(b.X, -Pitch.HalfL + 0.5, Pitch.HalfL - 0.5);
        double z = M.Clamp(b.Z, -Pitch.HalfW + 0.5, Pitch.HalfW - 0.5);
        bool penalty = InPenaltyArea(off.Team, x, z);
        victim.StartAction(ActionKind.Stumble, 1.1, 0, 0);
        LastFoul = new Foul { Offender = off, Victim = victim, X = x, Z = z, Yellow = false, Penalty = penalty, Time = Time };
        Events.Foul = 1;
        WhistleFoul(victim.Team, x, z, penalty);
    }

    void ResolveTackle(Player p, bool slide, bool bodyHit, int knock, in Leg leg)
    {
        var b = Ball;
        var carrier = Owner != null && Owner.Team != p.Team ? Owner : null;
        double win = 1;
        if (carrier != null)
        {
            // Shielding: is the carrier's body between the tackler and the ball?
            double toBallX = b.Pos.X - p.Pos.X;
            double toBallZ = b.Pos.Z - p.Pos.Z;
            double toCarrX = carrier.Pos.X - p.Pos.X;
            double toCarrZ = carrier.Pos.Z - p.Pos.Z;
            double dB = JsMath.Hypot(toBallX, toBallZ);
            double dC = JsMath.Hypot(toCarrX, toCarrZ);
            double shield = dC < dB && (toBallX * toCarrX + toBallZ * toCarrZ) / Math.Max(0.01, dB * dC) > 0.8 ? 0.3 : 0;
            double close = BallDist(carrier) < 0.55 ? 0.12 : 0;
            win = 0.3 + p.Attrs.Defending * 0.35 + p.DuelStrength * 0.06 - carrier.Attrs.Control * 0.18 - carrier.DuelStrength * 0.1 - shield - close + (slide ? 0.12 : 0);
        }
        Events.Tackle = 1;
        bool won = Rng.Next() < win;
        if (carrier != null && bodyHit && Phase == Phase.Play && Rng.Next() < FoulChance(p, carrier, slide, won) + knock * 0.12)
        {
            // Through the man (the leg caught him too): the ball doesn't matter, it's a foul.
            CommitFoul(p, carrier, slide, false);
            if (Advantage == null)
            {
                p.Action = slide ? ActionKind.Slide : ActionKind.Stumble;
                p.ActionT = slide ? p.ActionT : 0;
                p.ActionDur = slide ? p.ActionDur : 0.4;
                return;
            }
        }
        if (won)
        {
            if (carrier == null && OffsideTouch(p)) return;
            // The ball comes off the leg: the boot's speed into it along the line of contact (the
            // leg swinging through on a block, mostly the body's run on a slide). Met square and
            // soft, it dies at his feet and he has it; met hard, it flies off where it was hit.
            double vx0 = leg.Bx - leg.Ax;
            double vz0 = leg.Bz - leg.Az;
            double l2 = vx0 * vx0 + vz0 * vz0;
            double k = l2 > 1e-9 ? M.Clamp(((b.Pos.X - leg.Ax) * vx0 + (b.Pos.Z - leg.Az) * vz0) / l2, 0, 1) : 1;
            double nx = b.Pos.X - (leg.Ax + vx0 * k);
            double nz = b.Pos.Z - (leg.Az + vz0 * k);
            double nd = JsMath.Hypot(nx, nz);
            if (nd > 1e-3)
            {
                nx /= nd;
                nz /= nd;
            }
            else
            {
                nx = p.LegX;
                nz = p.LegZ;
            }
            double sweep = slide ? 0.8 : 2.5;
            double lvx = p.Vel.X + p.LegX * sweep;
            double lvz = p.Vel.Z + p.LegZ * sweep;
            double rel = (lvx - b.Vel.X) * nx + (lvz - b.Vel.Z) * nz;
            double vx = rel > 0.3 ? b.Vel.X + nx * rel * 1.55 : lvx * 0.8;
            double vz = rel > 0.3 ? b.Vel.Z + nz * rel * 1.55 : lvz * 0.8;
            double wob = Rng.Gauss() * 0.15;
            double cw = JsMath.Cos(wob);
            double sw = JsMath.Sin(wob);
            (vx, vz) = (vx * cw - vz * sw, vx * sw + vz * cw);
            bool keep = !slide && JsMath.Hypot(vx - p.Vel.X, vz - p.Vel.Z) < 2.5;
            b.Kick(vx, 0, vz, 0, 0, 0);
            if (carrier != null && carrier.Action == ActionKind.None)
            {
                carrier.StartAction(ActionKind.Stumble, 0.45, 0, 0);
                carrier.TouchCooldown = 0.6;
            }
            Owner = keep ? p : null;
            LastTouch = p;
            JudgeOffside(p);
            PassTarget = null;
            p.TouchCooldown = keep ? 0 : 0.2;
            if (keep && p.Team == HumanTeam) SetControlled(p);
        }
        else
        {
            // Missed: committed and off balance.
            p.Action = slide ? ActionKind.Slide : ActionKind.Stumble;
            p.ActionT = slide ? p.ActionT : 0;
            p.ActionDur = slide ? p.ActionDur + 0.3 : 0.4;
        }
    }
}
