using System;

namespace GameNight.Sim;

/// <summary>
/// Goalkeepers (native 0.40 rework). A keeper reads the game in this order every think:
/// - the ball in his hands: secure it, look up, then roll it, throw it, walk it to the edge
///   of the box or punt it, whatever the picture says (HoldBall);
/// - a shot: get the body behind it when his feet can get him there, dive only when they
///   can't, and late, so the stretch arrives with the ball (Save);
/// - a runner through on goal: rush out to narrow the angle, set as he shoots, smother a
///   heavy touch at his feet (Rush);
/// - a cross or a high ball into his area: come for it and take it at its highest, or punch
///   it clear in a crowd (Claim);
/// - a loose ball he gets to first: gather it in the box, clear it with his feet outside (Sweep);
/// - otherwise angle play: on the bisector of the posts as the ball sees them, off his line
///   by how far away it is, behind his back line as a sweeper (Guard).
/// The keeper's playstyles land here through the stats: Keeping and Agility are reactions and
/// handling (Cat), Accel and Pace how far and fast he comes (Rush Out), Jumping the dive's
/// spring and how high he takes a cross (Far Reach).
/// </summary>
public sealed partial class AI
{
    readonly double[] holdStart = { -10, -10 };
    readonly bool[] holding = new bool[2];
    readonly double[] carryX = new double[2], carryZ = new double[2];
    /// <summary>Going up for a high ball he means to take: his hands reach a jump higher.</summary>
    public readonly bool[] Claiming = new bool[2];
    readonly double[] claimFor = { -10, -10 };
    readonly bool[] claimGo = new bool[2];
    /// <summary>For tuning: per kind (see KeeperMoveNames), how many times a keeper went for it.</summary>
    public readonly int[] KeeperMoves = new int[10];
    public static readonly string[] KeeperMoveNames = { "feet-save", "dive", "smother", "rush", "claim", "sweep-out", "punch", "catch", "dive-catch", "parry" };
    readonly double[,] moveStamp = new double[10, 2];

    /// <summary>Counts a move once per ball (per kick).</summary>
    void Note(int kind, Player k)
    {
        if (moveStamp[kind, k.Team] == m.LastKickTime + 1) return;
        moveStamp[kind, k.Team] = m.LastKickTime + 1;
        KeeperMoves[kind]++;
    }

    double OwnSide(Player k) => -m.Teams[k.Team].Dir;

    /// <summary>How far (m) a ball's line can be from a keeper standing at `depth` m off his line and he still covers it.</summary>
    static double Sweepness(Player k) => M.Clamp(k.Attrs.Accel * 0.55 + k.Attrs.Pace * 0.45, 0, 1);

    void KeeperThink(Player k)
    {
        k.SquareUp = true;
        k.Burst = false;
        // Coming for a high ball: committed until someone touches it.
        Claiming[k.Team] = claimGo[k.Team] && claimFor[k.Team] == m.LastKickTime && m.Owner == null && m.HeldBy == null;

        if (m.HeldBy == k)
        {
            HoldBall(k);
            return;
        }
        holding[k.Team] = false;
        if (k.Action == ActionKind.Dive) return;

        // A team-mate's pass to him: meet it and play it with his feet, like an outfielder.
        if (m.PassTarget == k && m.LastKicker != null && m.LastKicker.Team == k.Team && m.Owner == null)
        {
            var ip = Intercept[k.Id];
            MoveTo(k, ip.X, ip.Z, m.BallDist(k) > 6, true);
            return;
        }
        if (k.IsBusy) return;
        if (Save(k)) return;
        if (Rush(k)) return;
        if (Claim(k)) return;
        if (Sweep(k)) return;
        Guard(k);
    }

    // ------------------------------------------------------------------ the ball in his hands

    void HoldBall(Player k)
    {
        var team = m.Teams[k.Team];
        double dir = team.Dir;
        double own = -dir;
        k.SquareUp = false;
        k.LookAt = null;
        if (!holding[k.Team] || k.IsBusy)
        {
            // Just caught it (or still getting up off the floor): the clock starts once he's on his feet.
            if (!holding[k.Team])
            {
                // Walk it out toward the side with fewer of them, to the edge of the box.
                int left = 0, right = 0;
                foreach (var o in m.Teams[1 - k.Team].Players)
                {
                    if ((Pitch.HalfL - o.Pos.X * own) > 40) continue;
                    if (o.Pos.Z > 0) left++;
                    else right++;
                }
                double side = left == right ? JsMath.Or1(JsMath.Sign(k.Pos.Z)) : left < right ? 1 : -1;
                carryX[k.Team] = own * (Pitch.HalfL - Pitch.BoxDepth + 2.2);
                carryZ[k.Team] = M.Clamp(k.Pos.Z * 0.4 + side * 5, -12, 12);
            }
            holding[k.Team] = true;
            holdStart[k.Team] = m.Time;
            k.WantSpeed = 0;
            return;
        }
        double t = m.Time - holdStart[k.Team];
        bool human = k.Team == m.HumanTeam && !m.AutoPlay;
        // Body to the pitch, ball cradled, eyes up.
        k.Facing += M.AngleDiff(k.Facing, dir > 0 ? 0 : Math.PI) * 0.08;
        if (k.Plan != null)
        {
            k.WantSpeed = 0;
            return;
        }
        if (t < (human ? 5 : 0.6))
        {
            k.WantSpeed = 0;
            return;
        }
        if (m.Time >= nextDecision[k.Id])
        {
            nextDecision[k.Id] = m.Time + 0.35;
            // Their players caught upfield: a quick throw starts the counter.
            int caught = 0;
            foreach (var o in m.Teams[1 - k.Team].Players) if (o.Pos.X * own > -6) caught++;
            Player? roll = null, throwTo = null;
            double rs = 4.5, ts = 5;
            foreach (var q in team.Players)
            {
                if (q == k) continue;
                double d = M.Dist2D(k.Pos.X, k.Pos.Z, q.Pos.X, q.Pos.Z);
                if (d < 7 || d > 44) continue;
                double open = 99;
                foreach (var o in m.Teams[1 - k.Team].Players) open = Math.Min(open, M.Dist2D(o.Pos.X, o.Pos.Z, q.Pos.X, q.Pos.Z));
                double lane = LaneClearance(k.Pos.X, k.Pos.Z, q.Pos.X, q.Pos.Z, k.Team, false);
                if (lane < 0.4) continue;
                double up = (q.Pos.X - k.Pos.X) * dir;
                if (d <= 26 && open > 6)
                {
                    double s = open * 0.6 + Math.Min(lane, 6) * 0.4 - d * 0.08 + (q.Role == Role.DEF ? 0.8 : 0);
                    if (s > rs) { rs = s; roll = q; }
                }
                if (d >= 15 && open > 4.5)
                {
                    double s = open * 0.5 + Math.Min(lane, 6) * 0.3 + up * 0.06 + (caught >= 5 && q.Role != Role.DEF ? 2.5 : 0) - (d > 36 ? 2 : 0);
                    if (s > ts) { ts = s; throwTo = q; }
                }
            }
            double exp = m.Time + 2;
            if (throwTo != null && caught >= 5 && ts > 7)
            {
                k.Plan = HandPlan(k, throwTo, true, exp);
                return;
            }
            if (t > 1.1)
            {
                if (roll != null && (throwTo == null || rs + 1 > ts))
                {
                    k.Plan = HandPlan(k, roll, false, exp);
                    return;
                }
                if (throwTo != null)
                {
                    k.Plan = HandPlan(k, throwTo, true, exp);
                    return;
                }
            }
            bool atEdge = M.Dist2D(k.Pos.X, k.Pos.Z, carryX[k.Team], carryZ[k.Team]) < 0.8;
            if (t > 4.5 || (atEdge && t > 2.2))
            {
                // Long: to the most open of the front three, a punt or a lower drop-kick.
                Player? best = null;
                double bs = -1e9;
                foreach (int j in new[] { 9, 8, 10 })
                {
                    var f = m.ByJob(k.Team, j);
                    if (f == k) continue;
                    double open = 99;
                    foreach (var o in m.Teams[1 - k.Team].Players) open = Math.Min(open, M.Dist2D(o.Pos.X, o.Pos.Z, f.Pos.X, f.Pos.Z));
                    double s = open + (j == 9 ? 1 : 0) + m.Rng.Next() * 2;
                    if (s > bs) { bs = s; best = f; }
                }
                if (best == null || best == k) k.Plan = Plan(KickType.Lob, dir, 0, 0.8, -1, exp);
                else
                {
                    double dx = best.Pos.X - k.Pos.X, dz = best.Pos.Z - k.Pos.Z;
                    double d = Math.Max(0.1, JsMath.Hypot(dx, dz));
                    k.Plan = Plan(KickType.Lob, dx / d, dz / d, 0.8, best.Id, exp);
                }
                return;
            }
        }
        // Nothing on yet: walk it out to the edge of the box, upright, ball tucked in.
        double cx = carryX[k.Team] - k.Pos.X, cz = carryZ[k.Team] - k.Pos.Z;
        double cd = JsMath.Hypot(cx, cz);
        if (cd < 0.4) k.WantSpeed = 0;
        else
        {
            k.MoveX = cx / cd;
            k.MoveZ = cz / cd;
            k.WantSpeed = Math.Min(2.3, cd * 2);
        }
    }

    /// <summary>A ball out of the keeper's hands to `q`: rolled along the floor, or thrown overarm.</summary>
    KickPlan HandPlan(Player k, Player q, bool overarm, double exp)
    {
        double dx = q.Pos.X - k.Pos.X, dz = q.Pos.Z - k.Pos.Z;
        double d = Math.Max(0.1, JsMath.Hypot(dx, dz));
        var p = Plan(KickType.Pass, dx / d, dz / d, 0.5, q.Id, exp);
        p.Lofted = overarm;
        return p;
    }

    // ------------------------------------------------------------------ shots

    /// <summary>
    /// A ball coming at him: where it passes through his frontal plane (height, offset to his
    /// left, seconds from now). False when nothing is coming, or it's going well wide or over.
    /// </summary>
    bool Incoming(Player k, out double cy, out double lat, out double ct)
    {
        cy = lat = ct = 0;
        var b = m.Ball;
        double own = OwnSide(k);
        if (m.Owner != null || m.HeldBy != null) return false;
        if (b.Vel.X * own < 3 || JsMath.Hypot(b.Vel.X, b.Vel.Z) < 5) return false;
        double fx = JsMath.Cos(k.Facing), fz = JsMath.Sin(k.Facing);
        double ago = m.Time - InterceptAt;
        // Does it get to the goal mouth (or near it)? A slow ball that dies first still counts
        // when it's coming right at him.
        bool onGoal = false;
        double gl = own * Pitch.HalfL;
        for (int i = 1; i < sampleCount; i++)
        {
            if ((SX[i] - gl) * own >= 0 && (SX[i - 1] - gl) * own < 0)
            {
                double f = (gl - SX[i - 1]) / (SX[i] - SX[i - 1]);
                double gz = SZ[i - 1] + (SZ[i] - SZ[i - 1]) * f;
                double gy = SY[i - 1] + (SY[i] - SY[i - 1]) * f;
                onGoal = Math.Abs(gz) < Pitch.GoalHalfWidth + 0.9 && gy < Pitch.GoalHeight + 0.5;
                break;
            }
        }
        for (int i = 1; i < sampleCount; i++)
        {
            double a = (SX[i - 1] - k.Pos.X) * fx + (SZ[i - 1] - k.Pos.Z) * fz;
            double c = (SX[i] - k.Pos.X) * fx + (SZ[i] - k.Pos.Z) * fz;
            if (a > 0 && c <= 0)
            {
                double f = a / (a - c);
                double px = SX[i - 1] + (SX[i] - SX[i - 1]) * f;
                double pz = SZ[i - 1] + (SZ[i] - SZ[i - 1]) * f;
                cy = SY[i - 1] + (SY[i] - SY[i - 1]) * f;
                lat = -(px - k.Pos.X) * fz + (pz - k.Pos.Z) * fx;
                ct = (i - 1 + f) * SampleDT - ago;
                if (ct < 0) return false;
                if (!onGoal && (Math.Abs(lat) > 1.6 || cy > 2.4)) return false;
                return Math.Abs(lat) < 6 && cy < Pitch.GoalHeight + 0.9;
            }
        }
        return false;
    }

    /// <summary>The old goal-line form (world z) for the training drill.</summary>
    public bool ShotCrossing(Player k, out double cy, out double cz, out double ct)
    {
        bool has = Incoming(k, out cy, out double lat, out ct);
        cz = k.Pos.Z + lat * JsMath.Cos(k.Facing);
        return has;
    }

    /// <summary>
    /// How far to his side his hands reach a ball at height y without diving: the arms' sweep
    /// from the shoulders, and for anything below the waist going down to it (further for a ball
    /// that gives him time: down on one knee, the body behind it). `jump` lifts it all.
    /// </summary>
    public static double HandsReach(Player k, double y, double jump, double speed)
    {
        double h = k.Look.Height;
        double top = 2.2 * h + jump;
        if (y > top) return -1;
        double sy = 1.45 * h + jump * 0.8;
        double arm = 0.72 * h;
        double r = 0.2 + Math.Sqrt(Math.Max(0, arm * arm - (y - sy) * (y - sy)));
        if (y < 1.1) r = Math.Max(r, 0.5 + 0.2 * k.Attrs.Keeping + 0.45 * (1 - M.Smoothstep(8, 14, speed)));
        return r;
    }

    bool Save(Player k)
    {
        if (m.PassTarget == k) return false;
        if (!Incoming(k, out double cy, out double lat, out double ct)) return false;
        // Coming for a high ball: he takes it in the air (Claim), no diving under it.
        if (Claiming[k.Team] && cy > 1.5) return false;
        var lk = m.LastKicker;
        bool theirs = lk == null || lk.Team != k.Team;
        k.LookTarget.Copy(m.Ball.Pos);
        k.LookAt = k.LookTarget;
        // The moment it's struck he can't move yet: feet set, reading it.
        double react = 0.2 - 0.07 * k.Attrs.Keeping - 0.04 * k.Attrs.Agility;
        if (theirs && m.Time - m.LastKickTime < react)
        {
            k.WantSpeed = 0;
            return true;
        }
        double fx = JsMath.Cos(k.Facing), fz = JsMath.Sin(k.Facing);
        double lx = -fz, lz = fx;
        double a = Math.Abs(lat);
        double side = JsMath.Or1(JsMath.Sign(lat));
        double y = M.Clamp(cy, 0.12, 2.6);
        // On his feet if his feet can get him there: side-steps, the body behind the ball.
        double slack = a - HandsReach(k, y, 0, m.Ball.Vel.Len()) * 0.85;
        double shuffle = Math.Max(0, ct - 0.06);
        double steps = Math.Min(4.4 * shuffle, 0.5 * 10 * shuffle * shuffle);
        if (slack <= steps)
        {
            double off = a < 0.25 ? 0 : side * Math.Max(0, a - 0.1);
            // A slow ball: go and meet it rather than wait for it.
            double meet = m.Ball.Vel.Len() < 10 && ct > 0.35 ? Math.Min(2, ct * 2) : 0;
            MoveTo(k, k.Pos.X + lx * off + fx * meet, k.Pos.Z + lz * off + fz * meet, true, true);
            if (a < 0.25 && meet == 0) k.WantSpeed = 0;
            Note(0, k);
            return true;
        }
        double reachNow = KeeperPose.PlanDive(a, y, k.Look.Height).reach;
        // Shuffle across first; the dive goes in the last moment so the full stretch arrives
        // together with the ball.
        if (ct > 0.3 + 0.1 * (1 - k.Attrs.Keeping))
        {
            double step = side * (a - Math.Min(a, reachNow * 0.6));
            MoveTo(k, k.Pos.X + lx * step, k.Pos.Z + lz * step, true, true);
            return true;
        }
        if (m.Time > keeperDiveT[k.Team] + 0.8)
        {
            CommitDive(k, lx * side, lz * side, a, y, Math.Max(0.18, ct), 0);
            Note(1, k);
        }
        return true;
    }

    /// <summary>
    /// Throw the body sideways along (ux, uz) (his left or right): at a ball `a` m that way and
    /// `dh` high, getting there in `tt` s. `lunge` carries him forward too (smothering a ball at
    /// a striker's feet).
    /// </summary>
    void CommitDive(Player k, double ux, double uz, double a, double dh, double tt, double lunge)
    {
        keeperDiveT[k.Team] = m.Time;
        double h = k.Look.Height;
        double reachNow = KeeperPose.PlanDive(a, dh, h).reach;
        double spring = 6 + k.Attrs.Keeping * 1.6 + k.Attrs.Jumping * 1.4;
        double push = M.Clamp((a - reachNow) / tt, 0, spring);
        var plan = KeeperPose.PlanDive(Math.Max(0, a - push * tt), dh, h);
        double fx = JsMath.Cos(k.Facing), fz = JsMath.Sin(k.Facing);
        k.DiveFly = Math.Min(0.75, tt + 0.15);
        // Down, a moment on the floor, and back up.
        k.StartAction(ActionKind.Dive, k.DiveFly + 0.95, ux, uz);
        double fwd = 0.6 + lunge;
        k.Vel.Set(fx * fwd + ux * push, 0, fz * fwd + uz * push);
        k.Plan = null;
        DiveHeight[k.Id] = dh;
        DiveRoll[k.Id] = plan.roll;
        DiveLift[k.Id] = plan.lift;
    }

    /// <summary>A dive at a point `dz` along the goal line (world z), `dh` high: penalties and the drill.</summary>
    void DiveAlongLine(Player k, double dz, double dh, double tt)
    {
        double fx = JsMath.Cos(k.Facing), fz = JsMath.Sin(k.Facing);
        double lx = -fz, lz = fx;
        double s = JsMath.Or1(JsMath.Sign(dz * lz + 1e-9 * lx));
        CommitDive(k, lx * s, lz * s, Math.Abs(dz), dh, tt, 0);
    }

    /// <summary>
    /// The human in goal presses Dive. With a shot coming he throws himself at it (or, with the
    /// stick pushed the other way, at full stretch to that side); with nothing coming, a stick
    /// push still sends him that way. `side`: the stick along the goal line in world z (-1, 0, 1).
    /// </summary>
    public void HumanDive(Player k, double side)
    {
        if (k.IsBusy || m.HeldBy != null) return;
        bool has = ShotCrossing(k, out double cy, out double cz, out double ct);
        bool at = has && (side == 0 || JsMath.Sign(cz - k.Pos.Z) == side);
        if (at)
        {
            DiveAlongLine(k, cz - k.Pos.Z, M.Clamp(cy, 0.15, 2.4), Math.Max(0.15, ct));
            return;
        }
        if (side == 0) return;
        DiveAlongLine(k, side * 2.8, has ? M.Clamp(cy, 0.15, 2.4) : 0.7, has ? Math.Max(0.15, ct) : 0.35);
    }

    /// <summary>
    /// Penalty: there's no time to react, so the keeper picks a side as the kick is struck. A
    /// good keeper reads the taker more often; sometimes he stays big in the middle.
    /// </summary>
    public void PenaltyGuess(Player k, double tz, double ty)
    {
        double r = m.Rng.Next();
        double read = 0.34 + k.Attrs.Keeping * 0.22;
        if (r < 0.14) return; // stays: reacts to whatever comes at him
        double side = r < 0.14 + read && Math.Abs(tz) > 0.5 ? JsMath.Sign(tz) : m.Rng.Next() < 0.5 ? -1 : 1;
        double dz = side * (1.6 + m.Rng.Next() * 1.6) - k.Pos.Z;
        double dh = M.Clamp(r < 0.14 + read ? ty : 0.4 + m.Rng.Next() * 1.4, 0.2, 2.2);
        DiveAlongLine(k, dz, dh, 0.42);
    }

    // ------------------------------------------------------------------ one on one

    bool Rush(Player k)
    {
        var c = m.Owner;
        if (c == null || c.Team == k.Team) return false;
        double own = OwnSide(k);
        double gx = own * Pitch.HalfL;
        double dGoal = M.Dist2D(c.Pos.X, c.Pos.Z, gx, 0);
        if (dGoal > 24 || (!InOwnBox(k, c.Pos.X, c.Pos.Z) && dGoal > 20)) return false;
        // From a tight angle he holds his near post instead.
        if (Math.Abs(c.Pos.Z) > 4 + (Pitch.HalfL - c.Pos.X * own) * 0.9) return false;
        // Through on goal: none of his defenders goal-side in the way.
        double ux = (gx - c.Pos.X) / dGoal, uz = -c.Pos.Z / dGoal;
        foreach (var q in m.Teams[k.Team].Players)
        {
            if (q == k) continue;
            double qx = q.Pos.X - c.Pos.X, qz = q.Pos.Z - c.Pos.Z;
            double along = qx * ux + qz * uz;
            if (along < -0.5 || along > dGoal) continue;
            if (Math.Abs(qx * uz - qz * ux) < 1.6 + along * 0.22) return false;
        }
        var b = m.Ball;
        double kb = m.BallDist(k);
        double loose = m.BallDist(c);
        k.LookTarget.Copy(b.Pos);
        k.LookAt = k.LookTarget;
        // At his feet: a heavy touch, or he's on top of it: down and smother it.
        if (b.Pos.Y < 0.45 && kb < 2.6 && (loose > 0.8 || kb < 1.4) && m.Time > keeperDiveT[k.Team] + 0.8)
        {
            double fx = JsMath.Cos(k.Facing), fz = JsMath.Sin(k.Facing);
            double dx = b.Pos.X - k.Pos.X, dz = b.Pos.Z - k.Pos.Z;
            double lat = -dx * fz + dz * fx;
            double depth = dx * fx + dz * fz;
            double s = JsMath.Or1(JsMath.Sign(lat));
            CommitDive(k, -fz * s, fx * s, Math.Abs(lat) + 0.3, 0.2, 0.3, M.Clamp(depth - 0.5, 0, 2.5) / 0.8);
            Note(2, k);
            return true;
        }
        // Out to narrow the angle, at his pace; set (still, big) once the shot is coming.
        double sweep = Sweepness(k);
        double depthOut = Math.Min(dGoal - 2.2, M.Clamp(dGoal * 0.5, 2.5, 10 + 4 * sweep));
        double tx = gx + (c.Pos.X - gx) / dGoal * depthOut;
        double tz = c.Pos.Z / dGoal * depthOut;
        bool shooting = c.Plan != null && c.Plan.Type == KickType.Shot || M.Dist2D(c.Pos.X, c.Pos.Z, k.Pos.X, k.Pos.Z) < 5.5;
        MoveTo(k, tx, tz, true, true);
        Note(3, k);
        if (shooting) k.WantSpeed = Math.Min(k.WantSpeed, 1.2);
        else
        {
            k.Sprinting = true;
            k.Burst = M.Dist2D(k.Pos.X, k.Pos.Z, tx, tz) > 3;
        }
        return true;
    }

    // ------------------------------------------------------------------ crosses

    bool Claim(Player k)
    {
        if (m.Owner != null || m.Ball.Pos.Y < 1.0 && m.Ball.Vel.Y <= 0) return false;
        var lk = m.LastKicker;
        if (lk != null && lk.Team == k.Team && m.PassTarget != null) return false;
        double own = OwnSide(k);
        double ago = m.Time - InterceptAt;
        double jump = 0.25 + 0.4 * k.Attrs.Jumping;
        double top = 2.2 * k.Look.Height + jump;
        // Where it comes down through his reach, in his area.
        for (int i = 2; i < sampleCount; i++)
        {
            if (SY[i] > top || SY[i] >= SY[i - 1]) continue;
            if (SY[i] < 1.3) return false; // under his reach: a low ball (Sweep, or Save)
            double px = SX[i], pz = SZ[i];
            double depth = Pitch.HalfL - px * own;
            if (depth < 0.3 || depth > 7.5 || Math.Abs(pz) > Pitch.SixHalfWidth) return false;
            double ti = i * SampleDT - ago;
            double run = RunTime(k, px, pz, 0.15, 0.55);
            if (run > ti + 0.05) return false;
            // Make the call once per ball: a good keeper comes more often, and more often still
            // when there's nobody near it.
            if (claimFor[k.Team] != m.LastKickTime)
            {
                claimFor[k.Team] = m.LastKickTime;
                double near = 99;
                foreach (var o in m.Teams[1 - k.Team].Players) near = Math.Min(near, M.Dist2D(o.Pos.X, o.Pos.Z, px, pz));
                double go = 0.2 + 0.3 * k.Attrs.Keeping + (near > 3 ? 0.3 : 0) - (depth > 6 ? 0.15 : 0);
                claimGo[k.Team] = m.Rng.Next() < go;
            }
            if (!claimGo[k.Team]) return false;
            Claiming[k.Team] = true;
            Note(4, k);
            MoveTo(k, px, pz, true, true);
            k.Burst = true;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ loose balls

    bool Sweep(Player k)
    {
        if (m.Owner != null || Chaser[k.Team] != k) return false;
        var ip = Intercept[k.Id];
        k.LookTarget.Copy(m.Ball.Pos);
        k.LookAt = k.LookTarget;
        if (InOwnBox(k, ip.X, ip.Z))
        {
            // His ball: gather it (the hands take it as it arrives).
            MoveTo(k, ip.X, ip.Z, true, true);
            return true;
        }
        // Out of the box, with his feet: get there and clear it.
        Note(5, k);
        if (k.Plan == null)
        {
            double dir = m.Teams[k.Team].Dir;
            double z = ip.Z;
            double s = JsMath.Or1(JsMath.Sign(z));
            double ax = dir, az = s * 0.45;
            double d = JsMath.Hypot(ax, az);
            k.Plan = Plan(KickType.Clear, ax / d, az / d, 0.85, -1, m.Time + 1.5);
        }
        MoveTo(k, ip.X, ip.Z, true, true);
        k.Burst = true;
        return true;
    }

    // ------------------------------------------------------------------ angle play

    /// <summary>Angle play, no saves (also the human's keeper in the drill when the stick is idle).</summary>
    public void KeeperStance(Player k) => Guard(k);

    void Guard(Player k)
    {
        k.SquareUp = true;
        var team = m.Teams[k.Team];
        double own = -team.Dir;
        double gx = own * Pitch.HalfL;
        var b = m.Owner != null ? m.Owner.Pos : m.Ball.Pos;
        double bD = Math.Max(0.5, Pitch.HalfL - b.X * own); // ball's distance out from the goal line
        double bz = b.Z;
        double G = Pitch.GoalHalfWidth;
        // The bisector of the angle the posts make from the ball meets the goal line here.
        double d1 = JsMath.Hypot(bD, bz + G), d2 = JsMath.Hypot(bD, bz - G);
        double zc = G * (d1 - d2) / (d1 + d2);
        double d = JsMath.Hypot(bD, bz);
        double depth;
        if (d < 30) depth = M.Clamp(0.6 + d * 0.11, 0.6, 3.6);
        else
        {
            // Ball far away: up behind his back line, ready for the ball over the top.
            double line = 99;
            foreach (var q in team.Players) if (q != k) line = Math.Min(line, Pitch.HalfL - q.Pos.X * own);
            double sweepMax = 8 + 6 * Sweepness(k);
            depth = M.Lerp(3.6, Math.Min(sweepMax, Math.Max(3.6, line - 6)), M.Smoothstep(30, 60, d));
        }
        // Wide and deep (a cross coming): off the near post toward the middle, a step off his line.
        double wide = M.Smoothstep(10, 16, Math.Abs(bz)) * (1 - M.Smoothstep(16, 24, bD));
        zc *= 1 - 0.45 * wide;
        depth = M.Lerp(depth, 1.7, wide);
        double vx = b.X - gx, vz = b.Z - zc;
        double vl = Math.Max(0.1, JsMath.Hypot(vx, vz));
        double tx = gx + vx / vl * depth;
        double tz = zc + vz / vl * depth;
        if (depth < 6) tz = M.Clamp(tz, -G - 0.4, G + 0.4);
        if ((tx - gx) * own > -0.4) tx = gx - own * 0.4;
        double dd = M.Dist2D(k.Pos.X, k.Pos.Z, tx, tz);
        var carrier = m.Owner;
        bool danger = carrier != null && carrier.Team != k.Team && d < 28;
        if (dd > 7)
        {
            // Well out of position (a ball over his head): turn and run.
            k.SquareUp = false;
            MoveTo(k, tx, tz, true, false);
            k.LookTarget.Copy(m.Ball.Pos);
            k.LookAt = k.LookTarget;
            return;
        }
        MoveTo(k, tx, tz, dd > 2.5, true);
        // Side-steps, not strides; feet set when someone's lining one up.
        k.WantSpeed = Math.Min(k.WantSpeed, dd > 2.5 ? 5.5 : 4.2);
        if (danger && dd < 0.45) k.WantSpeed = 0;
    }

    // ------------------------------------------------------------------ contact

    /// <summary>
    /// Is the ball (centre at x, y, z) touching the keeper, and where? `edge` 0..1 is how close to
    /// the limit of his reach it is. Diving: a capsule along the body from the hips to the
    /// outstretched hands, laid out sideways along the dive. Set: a real body (torso, head, two
    /// legs with the gap between them) and two hands that reach as far as HandsReach says, in
    /// front of him, plus a jump when he's coming for a high ball.
    /// </summary>
    bool KeeperHit(Player k, bool diving, double x, double y, double z, out bool hands, out double edge)
    {
        hands = false;
        edge = 0;
        double h = k.Look.Height;
        const double R = 0.11; // ball
        if (diving)
        {
            var ps = KeeperPose.Pose(k, DiveRoll[k.Id], DiveLift[k.Id], pose);
            double ux = k.ActionDirX, uz = k.ActionDirZ;
            double sr = JsMath.Sin(ps.Roll);
            double cr = JsMath.Cos(ps.Roll);
            double l0 = KeeperPose.DiveHips * h;
            double l1 = KeeperPose.DiveHands * h;
            double ax = k.Pos.X + ux * l0 * sr, az = k.Pos.Z + uz * l0 * sr, ay = ps.Lift + l0 * cr;
            double lx = ux * (l1 - l0) * sr, lz = uz * (l1 - l0) * sr, ly = (l1 - l0) * cr;
            double len2 = lx * lx + ly * ly + lz * lz;
            double t = M.Clamp(((x - ax) * lx + (y - ay) * ly + (z - az) * lz) / len2, 0, 1);
            double d = JsMath.Hypot(x - (ax + lx * t), y - (ay + ly * t), z - (az + lz * t));
            if (d < KeeperPose.DiveRadius + R)
            {
                hands = t > 0.55;
                edge = t;
                return true;
            }
            return false;
        }
        // Keeper frame: depth toward the pitch, lateral to his left.
        double dx = x - k.Pos.X;
        double dz = z - k.Pos.Z;
        double fx = JsMath.Cos(k.Facing);
        double fz = JsMath.Sin(k.Facing);
        double depth = dx * fx + dz * fz;
        double lat = -dx * fz + dz * fx;
        bool Capsule(double la, double ya, double lb, double yb, double r)
        {
            double vl = lb - la;
            double vy = yb - ya;
            double t = M.Clamp(((lat - la) * vl + (y - ya) * vy) / (vl * vl + vy * vy), 0, 1);
            return JsMath.Hypot(depth, lat - (la + vl * t), y - (ya + vy * t)) < r + R;
        }
        double jump = Claiming[k.Team] ? 0.25 + 0.4 * k.Attrs.Jumping : 0;
        // Hands first: in front of the body line, as far to the side as his arms (or a crouch) go.
        if (depth > -0.25 && depth < 0.75)
        {
            double reach = HandsReach(k, y, jump, m.Ball.Vel.Len());
            if (reach > 0 && Math.Abs(lat) < reach + R)
            {
                hands = true;
                edge = Math.Abs(lat) / (reach + R);
                return true;
            }
        }
        return Capsule(0, 0.98 * h, 0, 1.52 * h, 0.19) || // torso
            JsMath.Hypot(depth, lat, y - 1.72 * h) < 0.12 + R || // head
            Capsule(0.2, 0.06, 0.1, 0.9 * h, 0.085) || // left leg
            Capsule(-0.2, 0.06, -0.1, 0.9 * h, 0.085); // right leg
    }

    /// <summary>May the keeper use his hands on the ball now (his box, no back-pass, nobody's
    /// at his feet with it)? Then he leaves it for his hands rather than taking a touch.</summary>
    public bool CanHandle(Player k)
    {
        var b = m.Ball;
        if (k.Role != Role.GK || m.HeldBy != null) return false;
        if (k.Action == ActionKind.Stumble || k.Action == ActionKind.Fall || k.Action == ActionKind.Kick || k.Action == ActionKind.Throw) return false;
        if (!InOwnBox(k, b.Pos.X, b.Pos.Z)) return false;
        if (m.Owner != null && m.Owner.Team == k.Team && m.Owner != k) return false;
        // Ball at his feet: he's playing it as an outfielder (no picking it up mid-dribble).
        if (m.Owner == k) return false;
        // Back-pass rule: no hands from a teammate's deliberate kick.
        if (m.LastKicker != null && m.LastKicker.Team == k.Team && m.LastKicker != k && m.LastTouch == m.LastKicker) return false;
        // Only a ball in front of the goal line can be handled.
        if (b.Pos.X * -m.Teams[k.Team].Dir > Pitch.HalfL) return false;
        // A ball in an opponent's control at his feet is for a smother, not for the hands.
        if (m.Owner != null && m.Owner.Team != k.Team && k.Action != ActionKind.Dive && m.BallDist(m.Owner) < 0.9) return false;
        return true;
    }

    /// <summary>Hand contact for keepers. Returns true if the keeper dealt with the ball this step.</summary>
    public bool KeeperContact(Player k)
    {
        var b = m.Ball;
        if (k.TouchCooldown > 0 || !CanHandle(k)) return false;
        if (m.LastTouch == k && m.Time - m.LastKickTime < 0.6) return false;

        // Check along the ball's path through this step, not just where it ended up: a hard
        // shot moves ~25 cm per step and would otherwise slip through the edge of a hand.
        bool diving = k.Action == ActionKind.Dive;
        bool hit = false;
        bool hands = false;
        double edge = 0;
        var p0 = b.PrevPos;
        for (int i = 1; i <= 4 && !hit; i++)
        {
            double f = i / 4.0;
            if (KeeperHit(k, diving, p0.X + (b.Pos.X - p0.X) * f, p0.Y + (b.Pos.Y - p0.Y) * f, p0.Z + (b.Pos.Z - p0.Z) * f, out hands, out edge))
            {
                hit = true;
                if (i < 4) b.Pos.Set(p0.X + (b.Pos.X - p0.X) * f, p0.Y + (b.Pos.Y - p0.Y) * f, p0.Z + (b.Pos.Z - p0.Z) * f);
            }
        }
        if (!hit) return false;
        bool body = !hands;
        if (m.Owner != null)
        {
            // Smothered at the striker's feet.
            m.Owner = null;
        }

        double speed = b.Vel.Len();
        double kp = k.Attrs.Keeping;
        k.TouchCooldown = 0.4;
        double y = b.Pos.Y;
        // Comfortable: a ball he's set for, into the hands or at the body, is simply taken.
        if (!diving && (hands ? speed < 7 + 4 * kp && edge < 0.8 : speed < 6))
        {
            if (Claiming[k.Team] && Crowded(k, 1.6) && m.Rng.Next() > 0.2 + 0.4 * kp)
            {
                Punch(k);
                return true;
            }
            m.CatchBall(k);
            KeeperMoves[7]++;
            m.Events.Save = 0.3;
            return true;
        }
        double saveP = 0.41 + kp * 0.4 - M.Clamp((speed - 16) / 16, 0, 1) * 0.3 - (edge > 0.85 ? 0.25 : 0) + (body ? 0.2 : 0);
        var team = m.Teams[k.Team];
        double outDir = team.Dir; // away from his goal
        if (m.Rng.Next() < saveP)
        {
            bool high = y > Pitch.GoalHeight - 0.5;
            double catchP = diving ? (high ? 0.08 : 0.32 + 0.25 * kp) * (1 - M.Smoothstep(12, 24, speed)) : (1 - M.Smoothstep(14, 26, speed)) * (0.55 + 0.4 * kp);
            if (edge > 0.85) catchP *= 0.3;
            if (hands && m.Rng.Next() < catchP)
            {
                m.CatchBall(k);
                KeeperMoves[diving ? 8 : 7]++;
            }
            else if (Claiming[k.Team] && hands && y > 1.8)
            {
                Punch(k);
                m.Events.Save = M.Clamp(speed / 30, 0.3, 1);
                return true;
            }
            else if (high && hands)
            {
                // Fingertips over the bar: up and over, out for a corner.
                double s = diving ? 1 : 0;
                b.Vel.Set(-outDir * (2.5 + m.Rng.Next() * 1.5), 5 + m.Rng.Next() * 2, b.Vel.Z * 0.3 + (diving ? k.ActionDirZ * 1.5 * s : 0));
            }
            else if (diving)
            {
                // Pushed wide: the ball carries on the way he dived, away from goal, off the post.
                double ux = k.ActionDirX, uz = k.ActionDirZ;
                double side = 2 + m.Rng.Next() * 4;
                b.Vel.Set(outDir * speed * (0.12 + m.Rng.Next() * 0.2) + ux * side, 0.8 + m.Rng.Next() * 2.5, uz * side + b.Vel.Z * 0.25);
            }
            else
            {
                // Beaten away in front of him.
                b.Vel.Set(outDir * speed * (0.25 + m.Rng.Next() * 0.2), 1 + m.Rng.Next() * 2.5, b.Vel.Z * 0.3 + (m.Rng.Next() - 0.5) * 5);
            }
            if (m.HeldBy != k)
            {
                Touched(k);
                KeeperMoves[9]++;
            }
            m.Events.Save = M.Clamp(speed / 30, 0.3, 1);
            return true;
        }
        if (body)
        {
            // Not held, but it still hits him: a real rebound off the body.
            double vx = b.Vel.X;
            b.Vel.X = -vx * 0.35;
            b.Vel.Z *= 0.6;
            b.Vel.Y = Math.Abs(b.Vel.Y) * 0.3 + 0.5;
            b.OnGround = false;
            b.Pos.X = k.Pos.X + JsMath.Sign(vx != 0 && !double.IsNaN(vx) ? vx : outDir) * -0.48;
            m.LastTouch = k;
            m.Events.Save = 0.3;
            return true;
        }
        // Fingertips: a slight touch that doesn't stop it (the body can still be hit after).
        k.TouchCooldown = 0.08;
        b.Vel.Z += (m.Rng.Next() - 0.5) * 1.5;
        b.Vel.Y += m.Rng.Next() * 0.6;
        b.Vel.Scale(0.95);
        m.LastTouch = k;
        return true;
    }

    bool Crowded(Player k, double r)
    {
        var b = m.Ball.Pos;
        foreach (var o in m.Teams[1 - k.Team].Players) if (M.Dist2D(o.Pos.X, o.Pos.Z, b.X, b.Z) < r) return true;
        return false;
    }

    /// <summary>Fists through it: high and away, out toward the side he's facing it from.</summary>
    void Punch(Player k)
    {
        var b = m.Ball;
        double outDir = m.Teams[k.Team].Dir;
        double s = JsMath.Or1(JsMath.Sign(b.Pos.Z));
        b.Vel.Set(outDir * (11 + m.Rng.Next() * 6), 4.5 + m.Rng.Next() * 2.5, s * (2 + m.Rng.Next() * 4));
        if (!k.IsBusy) k.StartAction(ActionKind.Punch, 0.5, outDir, 0);
        KeeperMoves[6]++;
        k.CatchY = b.Pos.Y;
        Touched(k);
        m.Events.Save = 0.5;
    }

    void Touched(Player k)
    {
        var b = m.Ball;
        b.Spin.Set(0, 0, 0);
        b.OnGround = false;
        m.Owner = null;
        m.LastTouch = k;
        m.LastKicker = k;
        m.LastKickTime = m.Time;
        m.PassTarget = null;
    }
}
