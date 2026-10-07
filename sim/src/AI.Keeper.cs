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
    public readonly int[] KeeperMoves = new int[14];
    public static readonly string[] KeeperMoveNames = { "feet-save", "dive", "smother", "rush", "claim", "sweep-out", "punch", "catch", "dive-catch", "parry-wide", "tip", "block", "spill", "brushed" };
    readonly double[,] moveStamp = new double[14, 2];

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
        k.ReachIn = -1;
        // Coming for a high ball: committed until someone touches it.
        Claiming[k.Team] = claimGo[k.Team] && claimFor[k.Team] == m.LastKickTime && m.Owner == null && m.HeldBy == null;

        if (m.HeldBy == k)
        {
            HoldBall(k);
            return;
        }
        holding[k.Team] = false;
        if (k.Action == ActionKind.Dive) return;
        if (!k.IsBusy) leap[k.Team] = false;

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
        // (Yours only comes here once you've held it six seconds: he plays it straight away.)
        bool human = m.Piloted(k) && !m.AutoPlay;
        // Body to the pitch, ball cradled, eyes up.
        k.Facing += M.AngleDiff(k.Facing, dir > 0 ? 0 : Math.PI) * 0.08;
        if (k.Plan != null)
        {
            k.WantSpeed = 0;
            return;
        }
        if (t < (human ? 0.2 : 0.6))
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
            if (t > (human ? 0.3 : 1.1))
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
            if (t > (human ? 1.5 : 4.5) || (atEdge && t > 2.2))
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

    // ------------------------------------------------------------------ reading the ball

    /// <summary>The ball's flight as each keeper reads it: his own fresh prediction, every 2 DT.</summary>
    const int PathN = 150;
    readonly double[][] kpX = { new double[PathN], new double[PathN] };
    readonly double[][] kpY = { new double[PathN], new double[PathN] };
    readonly double[][] kpZ = { new double[PathN], new double[PathN] };
    readonly int[] kpN = new int[2];
    readonly double[] kpAt = { -10, -10 }, kpKick = { -10, -10 }, kpTouchAt = { -10, -10 };
    readonly Player?[] kpTouch = new Player?[2];
    /// <summary>Bodies between the strike and him: he picks the ball up a beat later.</summary>
    readonly bool[] kpScreened = new bool[2];
    /// <summary>Wrong-footed by the strike: the read comes a beat late when it goes away from him.</summary>
    readonly bool[] kpWrong = new bool[2];
    /// <summary>Going up for a ball over his head (a lob, a dipping shot): his hands reach a jump higher.</summary>
    readonly bool[] leap = new bool[2];

    /// <summary>
    /// What a keeper makes of a ball coming his way: where it crosses his line, and where on its
    /// way he can meet it: with his feet and hands (step across, the body behind it), or only at
    /// the end of a dive.
    /// </summary>
    struct ShotRead
    {
        /// <summary>Crossing the goal mouth (or close enough that he has to treat it as if).</summary>
        public bool On;
        public double GT, GY, GZ;
        /// <summary>The meeting point (world), when it gets there (s from now), and in his frame.</summary>
        public double T, X, Y, Z, Lat, Depth, V;
        /// <summary>0: let it go; 1: on his feet; 2: a dive; 3: at full stretch, it's probably beyond him.</summary>
        public int How;
        /// <summary>On his feet: where to stand (the ball's line through his body, or as near as he gets).</summary>
        public double SX, SZ;
        public bool Leap;
    }

    void ReadPath(Player k)
    {
        int t = k.Team;
        if (m.LastTouch != kpTouch[t])
        {
            kpTouch[t] = m.LastTouch;
            kpTouchAt[t] = m.Time;
        }
        if (m.Time - kpAt[t] < 0.05 && kpKick[t] == m.LastKickTime && kpTouchAt[t] < kpAt[t]) return;
        bool fresh = kpKick[t] != m.LastKickTime || kpTouchAt[t] >= kpAt[t];
        kpAt[t] = m.Time;
        kpKick[t] = m.LastKickTime;
        var b = m.Ball;
        var pb = Kick.LoadPrediction(b);
        double[] X = kpX[t], Y = kpY[t], Z = kpZ[t];
        int n = 0;
        while (n < PathN)
        {
            X[n] = pb.Pos.X;
            Y[n] = pb.Pos.Y;
            Z[n] = pb.Pos.Z;
            n++;
            if (Math.Abs(pb.Pos.X) > Pitch.HalfL + 0.3) break;
            pb.Step(DT * 2);
        }
        kpN[t] = n;
        if (fresh)
        {
            // He's half-guessed where it's going as it's struck; now and then the striker's
            // fooled him and his weight's on the wrong foot.
            kpWrong[t] = m.LastKicker != null && m.LastKicker.Team != t && m.Rng.Next() > 0.45 + 0.25 * k.Attrs.Keeping;
            // Unsighted: someone in the line between the ball and his eyes.
            kpScreened[t] = false;
            double lx = k.Pos.X - b.Pos.X, lz = k.Pos.Z - b.Pos.Z;
            double l2 = lx * lx + lz * lz;
            if (l2 > 4)
                foreach (var q in m.Players)
                {
                    if (q == k || q == m.LastTouch) continue;
                    double u = ((q.Pos.X - b.Pos.X) * lx + (q.Pos.Z - b.Pos.Z) * lz) / l2;
                    if (u < 0.1 || u > 0.85) continue;
                    if (M.Dist2D(q.Pos.X, q.Pos.Z, b.Pos.X + lx * u, b.Pos.Z + lz * u) < 0.45) kpScreened[t] = true;
                }
        }
    }

    /// <summary>How long before he can move for the ball just struck or deflected: reading it.</summary>
    double ReactLeft(Player k)
    {
        double react = 0.33 - 0.08 * k.Attrs.Keeping - 0.05 * k.Attrs.Agility + (kpScreened[k.Team] ? 0.09 : 0) + (kpWrong[k.Team] ? 0.15 : 0);
        double since = Math.Max(m.LastKickTime, kpTouchAt[k.Team]);
        return Math.Max(0, since + react - m.Time);
    }

    /// <summary>
    /// How far he gets (m) along (ux, uz) in `tau` s on his feet: quick side-steps from where his
    /// momentum has him, backpedalling slower.
    /// </summary>
    static double MoveCap(Player k, double ux, double uz, double tau)
    {
        if (tau <= 0) return 0;
        double v0 = k.Vel.X * ux + k.Vel.Z * uz;
        double back = -(JsMath.Cos(k.Facing) * ux + JsMath.Sin(k.Facing) * uz);
        double vmax = (back > 0.6 ? 3.8 : 5.0) + 0.6 * k.Attrs.Agility;
        double a = k.AccelRate + 1.5;
        if (v0 < 0)
        {
            // Going the wrong way first: stop, then go.
            double stop = -v0 / PlayerK.Brake;
            if (tau <= stop) return v0 * tau + 0.5 * PlayerK.Brake * tau * tau;
            return v0 * stop * 0.5 + MoveCap0(a, vmax, 0, tau - stop);
        }
        return MoveCap0(a, vmax, Math.Min(v0, vmax), tau);
    }

    static double MoveCap0(double a, double vmax, double v0, double tau)
    {
        double tA = (vmax - v0) / a;
        if (tau <= tA) return v0 * tau + 0.5 * a * tau * tau;
        return v0 * tA + 0.5 * a * tA * tA + vmax * (tau - tA);
    }

    /// <summary>A dive's spring: how fast the push off the near foot carries him sideways (m/s).</summary>
    static double Spring(Player k) => 2.8 + 1.2 * k.Attrs.Keeping + 1.0 * k.Attrs.Jumping;

    /// <summary>How far to his side, laid out, his hands get to a ball at height y (no push yet). -1: over him.</summary>
    static double DiveReach(Player k, double y)
    {
        double h = k.Look.Height;
        if (y > 2.15 * h + 0.5) return -1;
        return KeeperPose.PlanDive(9, y, h).reach + 0.12;
    }

    /// <summary>
    /// Reads the ball on its way into his goal. False when nothing's coming (it's going wide or
    /// over, it dies before it gets there, it's a team-mate's pass to his feet).
    /// </summary>
    bool ReadShot(Player k, out ShotRead r)
    {
        r = default;
        var b = m.Ball;
        if (m.Owner != null || m.HeldBy != null) return false;
        double own = OwnSide(k);
        if (b.Pos.X * own < 12 || b.Vel.Len() < 1.5) return false;
        // A team-mate's deliberate kick back to him is for his feet.
        if (m.LastKickFoot && m.LastKicker != null && m.LastKicker.Team == k.Team && m.LastKicker != k && m.LastTouch == m.LastKicker) return false;
        ReadPath(k);
        int t = k.Team;
        int n = kpN[t];
        double[] X = kpX[t], Y = kpY[t], Z = kpZ[t];
        double t0 = kpAt[t] - m.Time;
        double gl = own * Pitch.HalfL;
        int jEnd = -1;
        for (int j = 1; j < n; j++)
        {
            if ((X[j] - gl) * own >= 0 && (X[j - 1] - gl) * own < 0)
            {
                double f = (gl - X[j - 1]) / (X[j] - X[j - 1]);
                r.GZ = Z[j - 1] + (Z[j] - Z[j - 1]) * f;
                r.GY = Y[j - 1] + (Y[j] - Y[j - 1]) * f;
                r.GT = t0 + (j - 1 + f) * DT * 2;
                jEnd = j;
                break;
            }
        }
        if (jEnd < 0 || r.GT <= 0) return false;
        double G = Pitch.GoalHalfWidth;
        r.On = Math.Abs(r.GZ) < G + 0.2 && r.GY < Pitch.GoalHeight + 0.12;
        if (Math.Abs(r.GZ) > G + 1.2 || r.GY > Pitch.GoalHeight + 0.8) return false;

        double react = ReactLeft(k);
        double h = k.Look.Height;
        double jump = 0.25 + 0.4 * k.Attrs.Jumping;
        double fx = JsMath.Cos(k.Facing), fz = JsMath.Sin(k.Facing);
        double spring = Spring(k);
        int feetJ = -1, fullJ = -1, diveJ = -1;
        double diveBest = -1e9;
        bool feetLeap = false;
        for (int j = 0; j <= jEnd; j++)
        {
            double tj = t0 + j * DT * 2;
            double px = X[j], py = Y[j], pz = Z[j];
            if (j == jEnd)
            {
                // The goal line itself: the last place to stop it.
                px = gl - own * 0.15;
                py = r.GY;
                pz = r.GZ;
                tj = r.GT;
            }
            if (tj < 0.02) continue;
            if (Pitch.HalfL - px * own > Pitch.BoxDepth) continue;
            double vj = j + 1 < n ? JsMath.Hypot(X[j + 1] - X[j], Y[j + 1] - Y[j], Z[j + 1] - Z[j]) / (DT * 2) : b.Vel.Len();
            double dx = px - k.Pos.X, dz = pz - k.Pos.Z;
            double d = JsMath.Hypot(dx, dz);
            double ux = d > 1e-6 ? dx / d : fx, uz = d > 1e-6 ? dz / d : fz;
            double move = tj - react - 0.03;
            double cap = MoveCap(k, ux, uz, move);
            bool canLeap = move > 0.3;
            double reach = HandsReach(k, py, 0, vj);
            bool leapNeed = false;
            if (reach < 0 && canLeap)
            {
                reach = HandsReach(k, py, jump, vj);
                leapNeed = reach > 0;
            }
            if (reach > 0)
            {
                // (Already in reach of his hands, he doesn't throw himself about: they go to it.)
                if (feetJ < 0 && (d - reach * 0.9 <= cap || d < reach && move < 0.2))
                {
                    feetJ = j;
                    feetLeap = leapNeed;
                }
                if (fullJ < 0 && py < 1.9 * h && d <= cap * 0.85 + 0.2) fullJ = j;
            }
            double dr = DiveReach(k, py);
            // A dive takes a moment to get off the ground.
            if (dr > 0 && move > 0.12)
            {
                double fly = Math.Min(move, 0.5);
                double margin = dr + spring * Math.Max(0, fly - 0.12) + MoveCap(k, ux, uz, move - fly) * 0.8 - d;
                if (margin > diveBest)
                {
                    diveBest = margin;
                    diveJ = j;
                }
            }
        }

        void At(ref ShotRead s, int j)
        {
            if (j == jEnd)
            {
                s.X = gl - own * 0.15;
                s.Y = s.GY;
                s.Z = s.GZ;
                s.T = s.GT;
            }
            else
            {
                s.X = X[j];
                s.Y = Y[j];
                s.Z = Z[j];
                s.T = t0 + j * DT * 2;
            }
            double ax = s.X - k.Pos.X, az = s.Z - k.Pos.Z;
            s.Depth = ax * fx + az * fz;
            s.Lat = -ax * fz + az * fx;
            int j1 = Math.Min(n - 1, j + 1), j0 = j1 - 1;
            double vx = X[j1] - X[j0], vz = Z[j1] - Z[j0];
            s.V = j0 >= 0 ? JsMath.Hypot(vx, Y[j1] - Y[j0], vz) / (DT * 2) : b.Vel.Len();
            // Where to stand: on its line where it passes nearest him (before it reaches the goal
            // line), his body behind it, and never back in his own net.
            double best = 1e9;
            for (int i = 1; i <= jEnd; i++)
            {
                double x0 = X[i - 1], z0 = Z[i - 1];
                double x1 = i == jEnd ? gl - own * 0.4 : X[i], z1 = i == jEnd ? s.GZ : Z[i];
                double sx = x1 - x0, sz = z1 - z0;
                double l2 = sx * sx + sz * sz;
                double u = l2 > 1e-9 ? M.Clamp(((k.Pos.X - x0) * sx + (k.Pos.Z - z0) * sz) / l2, 0, 1) : 0;
                double qx = x0 + sx * u, qz = z0 + sz * u;
                double dd = M.Dist2D(qx, qz, k.Pos.X, k.Pos.Z);
                if (dd < best)
                {
                    best = dd;
                    s.SX = qx;
                    s.SZ = qz;
                }
            }
            if (best > 1e8)
            {
                s.SX = s.X;
                s.SZ = s.Z;
            }
            if (s.SX * own > Pitch.HalfL - 0.4) s.SX = own * (Pitch.HalfL - 0.4);
        }

        // A ball he can get his body behind in good time: he goes and meets it (a slow one,
        // he walks onto it). A quicker one: across onto its line, or as far as he gets and the
        // hands take the rest. Only when his feet can't get him there, the dive.
        double sp = b.Vel.Len();
        if (fullJ >= 0 && sp < 13)
        {
            At(ref r, fullJ);
            r.How = 1;
        }
        else if (feetJ >= 0)
        {
            At(ref r, feetJ);
            r.How = 1;
            r.Leap = feetLeap;
        }
        else if (diveJ >= 0 && diveBest > -0.1)
        {
            At(ref r, diveJ);
            r.How = 2;
        }
        else if (r.On && diveJ >= 0)
        {
            At(ref r, diveJ);
            r.How = 3;
        }
        else
        {
            At(ref r, jEnd);
            r.How = 0;
        }
        return true;
    }

    /// <summary>The old goal-line form (world z) for the human's dive in the drill.</summary>
    public bool ShotCrossing(Player k, out double cy, out double cz, out double ct)
    {
        bool has = ReadShot(k, out var r) && r.How != 0;
        cy = r.Y;
        cz = r.Z;
        ct = r.T;
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
        if (!ReadShot(k, out var r)) return false;
        // Coming for a high ball: he takes it in the air (Claim), no diving under it.
        if (Claiming[k.Team] && r.Y > 1.5) return false;
        k.LookTarget.Copy(m.Ball.Pos);
        k.LookAt = k.LookTarget;
        k.SquareUp = true;
        double react = ReactLeft(k);
        if (react > 0)
        {
            // The moment it's struck: feet set, reading it (his momentum carries on).
            k.WantSpeed = 0;
            return true;
        }
        double fx = JsMath.Cos(k.Facing), fz = JsMath.Sin(k.Facing);
        double lx = -fz, lz = fx;
        double side = JsMath.Or1(JsMath.Sign(r.Lat));
        double a = Math.Abs(r.Lat);
        switch (r.How)
        {
            case 0:
                if (r.On)
                {
                    // Over his head and dropping in (a lob, a dipping one): back to his line and up.
                    leap[k.Team] = true;
                    MoveTo(k, OwnSide(k) * (Pitch.HalfL - 0.3), M.Clamp(r.GZ, -Pitch.GoalHalfWidth + 0.3, Pitch.GoalHalfWidth - 0.3), true, true);
                    k.Burst = true;
                    return true;
                }
                // Wide or over: he watches it go, a step across to be sure.
                if (Math.Abs(r.GZ) < Pitch.GoalHalfWidth + 0.6)
                {
                    double tz = M.Clamp(r.GZ, -Pitch.GoalHalfWidth, Pitch.GoalHalfWidth);
                    double tx = OwnSide(k) * (Pitch.HalfL - 0.6);
                    if (M.Dist2D(k.Pos.X, k.Pos.Z, tx, tz) < 3) MoveTo(k, k.Pos.X + (tx - k.Pos.X) * 0.3, k.Pos.Z + (tz - k.Pos.Z) * 0.3, false, true);
                    else k.WantSpeed = 0;
                }
                else k.WantSpeed = 0;
                return true;
            case 1:
            {
                // On his feet: across onto its line (or meeting it), set as it arrives.
                double dx = r.SX - k.Pos.X, dz = r.SZ - k.Pos.Z;
                double d = JsMath.Hypot(dx, dz);
                leap[k.Team] = r.Leap;
                // Where his hands will meet it (for the pose): from where he'll be standing.
                k.ReachIn = r.T;
                double mx = r.X - r.SX, mz = r.Z - r.SZ;
                k.ReachF = mx * fx + mz * fz;
                k.ReachL = -mx * fz + mz * fx;
                k.ReachY = r.Y;
                if (d < 0.12) k.WantSpeed = 0;
                else
                {
                    k.MoveX = dx / d;
                    k.MoveZ = dz / d;
                    // Quick, short steps: there in time, and no further.
                    double need = d / Math.Max(0.08, r.T - 0.05);
                    k.WantSpeed = Math.Min(6, Math.Max(need * 1.25, Math.Min(d * 5, 3.5)) + 0.2);
                    k.Burst = need > 3;
                }
                Note(0, k);
                return true;
            }
            default:
            {
                // A dive: shuffle across first, then go in the last moment so the full stretch
                // arrives together with the ball.
                double reachNow = DiveReach(k, r.Y);
                double s = Math.Max(0, a - reachNow);
                double fly = M.Clamp(s / Spring(k) + 0.08, 0.16, 0.5);
                if (r.T > fly + 0.06)
                {
                    double step = Math.Max(0, Math.Min(a - 0.5, a - reachNow * 0.7));
                    if (step < 0.1) k.WantSpeed = 0;
                    else
                    {
                        k.MoveX = lx * side;
                        k.MoveZ = lz * side;
                        k.WantSpeed = 5.5;
                    }
                    return true;
                }
                if (m.Time > keeperDiveT[k.Team] + 0.8)
                {
                    CommitDive(k, lx * side, lz * side, a, M.Clamp(r.Y, 0.12, 2.6), Math.Max(0.14, r.T), r.Depth / Math.Max(0.2, r.T));
                    Note(1, k);
                }
                return true;
            }
        }
    }

    /// <summary>
    /// Throw the body sideways along (ux, uz) (his left or right): at a ball `a` m that way and
    /// `dh` high, getting there in `tt` s. `fwd` (m/s) carries him forward too: attacking a
    /// ball in front of him, smothering one at a striker's feet.
    /// </summary>
    void CommitDive(Player k, double ux, double uz, double a, double dh, double tt, double fwd)
    {
        keeperDiveT[k.Team] = m.Time;
        double h = k.Look.Height;
        double reachNow = KeeperPose.PlanDive(a, dh, h).reach;
        double push = M.Clamp((a - reachNow) / tt, 0, Spring(k));
        var plan = KeeperPose.PlanDive(Math.Max(0, a - push * tt), dh, h);
        double fx = JsMath.Cos(k.Facing), fz = JsMath.Sin(k.Facing);
        k.DiveFly = Math.Min(0.75, tt + 0.15);
        // Down, a moment on the floor, and back up.
        k.StartAction(ActionKind.Dive, k.DiveFly + 0.95, ux, uz);
        fwd = M.Clamp(fwd, -1, 3.5);
        k.Vel.Set(fx * fwd + ux * push, 0, fz * fwd + uz * push);
        k.Plan = null;
        leap[k.Team] = false;
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
        CommitDive(k, lx * s, lz * s, Math.Abs(dz), dh, tt, 0.5);
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
            CommitDive(k, -fz * s, fx * s, Math.Abs(lat) + 0.3, 0.2, 0.3, 0.6 + M.Clamp(depth - 0.5, 0, 2.5) / 0.8);
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
        // Wide and deep, by the byline (a cross coming): off the near post toward the middle, a
        // step off his line. Further out at an angle it's a shooting position: the bisector holds.
        double wide = M.Smoothstep(10, 16, Math.Abs(bz)) * (1 - M.Smoothstep(5, 11, bD));
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
        if (danger && (dd < 0.45 || dd < 1.2 && carrier!.Plan != null && carrier.Plan.Type == KickType.Shot)) k.WantSpeed = 0;
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
        double jump = Claiming[k.Team] || leap[k.Team] ? 0.25 + 0.4 * k.Attrs.Jumping : 0;
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
        // Back-pass rule: no hands from a teammate's deliberate kick or throw-in (a header or a
        // deflection off him is fine).
        if (m.LastKickFoot && m.LastKicker != null && m.LastKicker.Team == k.Team && m.LastKicker != k && m.LastTouch == m.LastKicker) return false;
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
        double kp = k.Attrs.Keeping, ag = k.Attrs.Agility;
        k.TouchCooldown = 0.4;
        double y = b.Pos.Y;
        bool set = !diving && k.Vel.Len() < 2.2;
        // What makes it hard to hold: its pace, how far out on his hands he meets it, being in the
        // air or on the move, an awkward height (over his head, skidding at his feet).
        double pace = M.Clamp((speed - 9) / 21, 0, 1);
        double awkward = (y > 1.95 ? 0.12 : 0) + (y < 0.3 && speed > 12 ? 0.1 : 0);
        m.Events.Save = M.Clamp(speed / 30, 0.3, 1);
        if (body)
        {
            // It hits him, so it doesn't go through: a soft one he gathers into his body, anything
            // with pace comes back off him.
            if (!diving && speed < 11 + 4 * kp && m.Rng.Next() < 0.75 + 0.2 * kp)
            {
                m.CatchBall(k);
                KeeperMoves[7]++;
                return true;
            }
            Block(k, diving);
            return true;
        }
        if (edge > 0.88 - (diving ? 0.3 : 0.1) * pace)
        {
            // Fingertips: the end of his reach (and a hard one bends back the wrist further in).
            // A strong hand turns it away; a weak one only brushes it on its way.
            if (m.Rng.Next() < 0.5 + 0.3 * kp + 0.1 * ag - pace * 0.3) Tip(k, y, diving);
            else
            {
                k.TouchCooldown = 0.08;
                double outZ = JsMath.Or1(JsMath.Sign(b.Pos.Z));
                b.Vel.Z += outZ * (0.6 + m.Rng.Next() * 1.4);
                b.Vel.Y += m.Rng.Next() * 0.7;
                b.Vel.Scale(0.9);
                m.LastTouch = k;
                KeeperMoves[13]++;
            }
            return true;
        }
        // A cross he's come for with bodies round him: fists through it.
        if (Claiming[k.Team] && !diving && Crowded(k, 1.6) && m.Rng.Next() > 0.3 + 0.45 * kp)
        {
            Punch(k);
            return true;
        }
        double catchP = (diving ? 0.62 : 0.97) - pace * (diving ? 0.55 : 0.7) - edge * edge * 0.3 - awkward + kp * 0.22 + (set ? 0.04 : -0.04) + (Claiming[k.Team] ? 0.1 : 0);
        if (m.Rng.Next() < M.Clamp(catchP, 0.03, 0.98))
        {
            m.CatchBall(k);
            KeeperMoves[diving ? 8 : 7]++;
            if (speed < 9) m.Events.Save = 0;
            return true;
        }
        if (Claiming[k.Team] && y > 1.8)
        {
            Punch(k);
            return true;
        }
        Parry(k, speed, y, diving, edge);
        return true;
    }

    /// <summary>
    /// Not held: pushed away. A good keeper puts it somewhere safe (round the post, over the bar,
    /// wide and out of play); otherwise it drops loose in front of him, a rebound for whoever's quickest.
    /// </summary>
    void Parry(Player k, double speed, double y, bool diving, double edge)
    {
        var b = m.Ball;
        double outDir = m.Teams[k.Team].Dir;
        double kp = k.Attrs.Keeping;
        double safe = 0.15 + 0.45 * kp - M.Clamp((speed - 15) / 15, 0, 1) * 0.15 - edge * 0.1;
        bool wide = m.Rng.Next() < safe;
        if (y > Pitch.GoalHeight - 0.45 && (wide || b.Vel.Y > -1))
        {
            Tip(k, y, diving);
            return;
        }
        double r1 = m.Rng.Next(), r2 = m.Rng.Next();
        if (diving)
        {
            // On along the way he dived: round the post, or (spilled) down in front of him.
            double ux = k.ActionDirX, uz = k.ActionDirZ;
            if (wide) b.Vel.Set(outDir * speed * (0.08 + r1 * 0.12) + ux * (3 + r2 * 3), 0.8 + r1 * 2.2, uz * (3 + r2 * 3));
            else b.Vel.Set(outDir * speed * (0.15 + r1 * 0.15) + ux * (0.5 + r2 * 1.5), 0.4 + r1 * 1.4, uz * (0.5 + r2 * 1.5));
        }
        else
        {
            // Beaten away: up and out to the side of the hand that met it, or dropped in front.
            double fx = JsMath.Cos(k.Facing), fz = JsMath.Sin(k.Facing);
            double lat = -(b.Pos.X - k.Pos.X) * fz + (b.Pos.Z - k.Pos.Z) * fx;
            double s = JsMath.Or1(JsMath.Sign(lat));
            double lx = -fz * s, lz = fx * s;
            if (wide) b.Vel.Set(outDir * speed * (0.2 + r1 * 0.15) + lx * (2.5 + r2 * 3), 1.5 + r1 * 2, lz * (2.5 + r2 * 3));
            else b.Vel.Set(outDir * speed * (0.1 + r1 * 0.12) + lx * (r2 - 0.3) * 2, 0.3 + r1 * 1.2, lz * (r2 - 0.3) * 2);
            Reacts(k, ActionKind.Parry);
        }
        KeeperMoves[wide ? 9 : 12]++;
        Touched(k);
    }

    /// <summary>Fingertips: over the bar for a high one, round the post for the rest.</summary>
    void Tip(Player k, double y, bool diving)
    {
        var b = m.Ball;
        double outDir = m.Teams[k.Team].Dir;
        if (y > Pitch.GoalHeight - 0.6)
            b.Vel.Set(b.Vel.X * (0.15 + m.Rng.Next() * 0.15), 4.5 + m.Rng.Next() * 2.5, b.Vel.Z * 0.3 + (diving ? k.ActionDirZ * 1.5 : 0));
        else
        {
            double outZ = diving ? JsMath.Or1(JsMath.Sign(k.ActionDirZ)) : JsMath.Or1(JsMath.Sign(b.Pos.Z));
            b.Vel.Set(b.Vel.X * (0.35 + m.Rng.Next() * 0.25), b.Vel.Y * 0.5 + m.Rng.Next() * 1.5, b.Vel.Z * 0.3 + outZ * (4 + m.Rng.Next() * 3));
        }
        if (!diving) Reacts(k, y > 1.8 ? ActionKind.Punch : ActionKind.Parry);
        KeeperMoves[10]++;
        Touched(k);
    }

    /// <summary>Off his body: back the way it came, most of its pace gone.</summary>
    void Block(Player k, bool diving)
    {
        var b = m.Ball;
        double outDir = m.Teams[k.Team].Dir;
        double e = 0.22 + m.Rng.Next() * 0.2;
        double vx = b.Vel.X;
        b.Vel.X = -vx * e;
        if (b.Vel.X * outDir < 1) b.Vel.X = outDir * (1 + m.Rng.Next() * 2);
        b.Vel.Z = b.Vel.Z * 0.35 + (m.Rng.Next() - 0.5) * 3;
        b.Vel.Y = Math.Abs(b.Vel.Y) * 0.3 + 0.4 + m.Rng.Next();
        b.Pos.X = k.Pos.X + outDir * 0.45;
        if (!diving) Reacts(k, ActionKind.Parry);
        KeeperMoves[11]++;
        Touched(k);
    }

    /// <summary>The body's answer to the ball (for the pose): where it met him, then the action.</summary>
    void Reacts(Player k, ActionKind a)
    {
        if (k.IsBusy) return;
        var b = m.Ball;
        double dx = b.Pos.X - k.Pos.X, dz = b.Pos.Z - k.Pos.Z;
        double fx = JsMath.Cos(k.Facing), fz = JsMath.Sin(k.Facing);
        k.CatchY = b.Pos.Y;
        k.CatchF = dx * fx + dz * fz;
        k.CatchL = -dx * fz + dz * fx;
        k.StartAction(a, a == ActionKind.Punch ? 0.5 : 0.36, m.Teams[k.Team].Dir, 0);
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
        m.LastKickFoot = false;
        m.PassTarget = null;
    }
}
