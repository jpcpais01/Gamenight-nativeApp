using System;
using System.Collections.Generic;

namespace GameNight.Sim;

/// <summary>Where and when a player gets to the live ball (refreshed every 0.1 s).</summary>
public sealed class Intercept
{
    /// <summary>-1 = can't reach in the horizon.</summary>
    public double T = -1;
    public double X, Z;
    /// <summary>Time in hand there by his real running (RunTime): the ball's arrival minus his.</summary>
    public double Slack = -9;
    /// <summary>When the ball gets to (X, Z), seconds after At (the moment this was worked out).</summary>
    public double M;
    public double At = -9;
    /// <summary>The ball's velocity there (m/s): he arrives running with it, not stopping for it.</summary>
    public double VX, VZ;
}

/// <summary>
/// Individuality. Every player reads the same field but weighs it in his own way:
/// discipline (keeping his place), creativity (roaming, forward-minded), work (getting going,
/// recovery sprints), react (reading of the game).
/// </summary>
public sealed class Traits
{
    public double Discipline, Creativity, Work, React;
}

/// <summary>A planned through ball: who it's for, where and when he meets it, and the strike.</summary>
public sealed class ThroughPlan
{
    public Player Receiver = null!;
    public double X, Z;
    /// <summary>Ball speed when he meets it.</summary>
    public double Arrive;
    public double Time;
    /// <summary>Ground ball: strike pace and direction.</summary>
    public double V0, Dx, Dz;
    /// <summary>Lofted: where it comes down.</summary>
    public double LandX, LandZ;
    public double Score;
}

/// <summary>Team brains. Coordinates in comments are "team frame": +x is the goal the team attacks.</summary>
public sealed partial class AI
{
    const int Samples = 36;
    const double SampleDT = 0.1;
    /// <summary>Reading the live ball: reaction (s), and the margin a meeting needs over the other side.</summary>
    const double LiveReact = 0.1;
    const double LiveMargin = 0.25;
    /// <summary>Planning a pass: the opponents' reaction to the strike, the runner's, and the margin.</summary>
    const double PlanReactOpp = 0.15;
    const double PlanReactRun = 0.1;
    const double PlanMargin = 0.35;
    /// <summary>Worth of a meeting point: per metre forward, per second waited, and per second short of MeetClose ahead of the other side.</summary>
    const double MeetProgress = 1;
    const double MeetWait = 4;
    const double MeetClose = 0.8;
    const double MeetContest = 6;
    /// <summary>How far (cosine) a human's aimed pass or through ball may stray from the stick: about 40°.</summary>
    public const double AimCone = 0.77;
    /// <summary>How a planned through ball's score sits against an ordinary pass's.</summary>
    const double ThroughBias = -0.3;
    const double DT = Tick.DT;

    static readonly double[,] BoxSpots = { { -6, -2 }, { -8, 3 }, { -11, -4 }, { -5, 4.5 }, { -12, 1 }, { -14, -6 } };
    static readonly double[] Leads = { 4, 7, 11, 15, 19 };
    static readonly int[] BoxOrder = { 2, 3, 9, 6, 7, 10, 8, 5, 1, 4 };

    sealed class Curve
    {
        public double Key;
        public readonly List<double> T = new List<double>(), S = new List<double>(), V = new List<double>();
    }

    sealed class Run
    {
        public double X, Z, Until = -1;
    }

    /// <summary>Stable pseudo-random in [0, 1) per player and channel (no Rng draws: keeps sims reproducible).</summary>
    static double Hash01(double id, double k)
    {
        double s = JsMath.Sin(id * 127.1 + k * 311.7) * 43758.5453;
        return s - Math.Floor(s);
    }

    static Traits TraitsFor(Player p)
    {
        var a = p.Attrs;
        double R(int k) => Hash01(p.Id, k) - 0.5;
        int role = p.Role == Role.DEF ? 0 : p.Role == Role.MID ? 1 : 2;
        double[] disc = { 0.75, 0.55, 0.35 };
        double[] crea = { 0.25, 0.55, 0.75 };
        return new Traits
        {
            Discipline = M.Clamp(disc[role] + a.Defending * 0.15 + R(1) * 0.4, 0.1, 1),
            Creativity = M.Clamp(crea[role] + a.Passing * 0.15 + R(2) * 0.4, 0.05, 1),
            Work = M.Clamp(0.4 + a.Pace * 0.15 + a.Stamina * 0.15 + R(3) * 0.5, 0.15, 1),
            React = M.Clamp(0.4 + (a.Defending + a.Passing) * 0.15 + R(4) * 0.4, 0.15, 1),
        };
    }

    readonly Match m;
    public readonly Intercept[] Intercept = new Intercept[22];
    public readonly Player?[] Chaser = new Player?[2];
    /// <summary>The live ball's predicted path, every SampleDT (Float32, as the PWA keeps it).</summary>
    public readonly F32 SX = new F32(Samples), SY = new F32(Samples), SZ = new F32(Samples);
    int sampleCount;
    double nextIntercept;
    /// <summary>When the live intercepts were last worked out.</summary>
    public double InterceptAt;
    /// <summary>Scratch path for planning passes.</summary>
    readonly F32 pathX = new F32(Samples), pathY = new F32(Samples), pathZ = new F32(Samples), pathV = new F32(Samples);
    readonly double[] nextDecision = new double[22];
    readonly double[] tackleReady = new double[22];
    readonly List<double> spots = new List<double>();
    /// <summary>Next time a carrier looks for a through ball.</summary>
    readonly double[] throughLook = new double[22];
    readonly Run[] run = new Run[22];
    readonly Curve?[] curves = new Curve?[22];
    readonly double[] dribX = new double[22];
    readonly double[] dribZ = new double[22];
    readonly bool[] dribSprint = new bool[22];
    /// <summary>Offside line per attacking team, in that team's frame.</summary>
    readonly double[] offside = { Pitch.HalfL, Pitch.HalfL };
    readonly double[] keeperDiveT = { -10, -10 };
    public readonly double[] DiveHeight = new double[22];
    /// <summary>Planned dive pose per player (keepers), shared with the renderer.</summary>
    public readonly double[] DiveRoll = new double[22];
    public readonly double[] DiveLift = new double[22];
    Player? lastOwner;
    double possStart;
    double patience = 0.6;
    readonly V3 tmp = new V3();
    readonly V3 tmp2 = new V3();
    readonly DivePose pose = new DivePose();
    /// <summary>Per-player character (fixed for the match): see TraitsFor.</summary>
    public readonly Traits[] Traits = new Traits[22];
    /// <summary>Off-ball: smoothed goal each player is drifting toward (world).</summary>
    readonly double[] goalX = new double[22];
    readonly double[] goalZ = new double[22];
    /// <summary>Off-ball: chosen pocket of space, as an offset from the shape slot (team frame).</summary>
    readonly double[] seekDX = new double[22];
    readonly double[] seekDZ = new double[22];
    readonly double[] seekAt = new double[22];
    /// <summary>Zonal marking: who each defender has picked up (refreshed per team).</summary>
    readonly Player?[] mark = new Player?[22];
    readonly double[] markAt = { 0, 0 };
    readonly double[] offBallT = new double[22];
    /// <summary>NaN until worked out (the PWA's undefined).</summary>
    readonly double[] offK = new double[22];
    readonly double[] spAt = { -1, -1 };
    readonly Player?[] spBest = new Player?[2];
    readonly List<(double x, double z)> dirs = new List<(double, double)>();
    readonly HashSet<double> tried = new HashSet<double>();
    readonly List<Player> near = new List<Player>();
    readonly List<Player> runners = new List<Player>();

    /// <summary>A substitute has taken p's slot: his own character.</summary>
    public void Refresh(Player p) => Traits[p.Id] = TraitsFor(p);

    public AI(Match match)
    {
        m = match;
        for (int i = 0; i < 22; i++)
        {
            var p = m.Players[i];
            Traits[i] = TraitsFor(p);
            goalX[i] = p.Pos.X;
            goalZ[i] = p.Pos.Z;
            offBallT[i] = -1;
            offK[i] = double.NaN;
            Intercept[i] = new Intercept();
            run[i] = new Run();
            dribX[i] = 1;
            DiveHeight[i] = 0.5;
            DiveRoll[i] = 1.3;
        }
    }

    public void SetRun(Player p, double x, double z, double seconds = 3)
    {
        var r = run[p.Id];
        r.X = M.Clamp(x, -Pitch.HalfL + 1, Pitch.HalfL - 1);
        r.Z = M.Clamp(z, -Pitch.HalfW + 1, Pitch.HalfW - 1);
        r.Until = m.Time + seconds;
    }

    /// <summary>
    /// Time for a player to get to (x, z), by the same running he really does (Player.Move):
    /// a reaction, the sideways part of his momentum turned onto the line, a brake if he's
    /// going the other way, then the sprint-start curve. `reach`: done once he's that close.
    /// </summary>
    public double RunTime(Player q, double x, double z, double react = 0.15, double reach = 0)
    {
        double dx = x - q.Pos.X;
        double dz = z - q.Pos.Z;
        double full = Math.Sqrt(dx * dx + dz * dz);
        double d = full - reach;
        if (d < 0.3) return react * 0.5;
        double ux = dx / full;
        double uz = dz / full;
        double v = q.Vel.X * ux + q.Vel.Z * uz;
        double t = react + (Math.Abs(q.Vel.X * uz - q.Vel.Z * ux) / PlayerK.Lateral) * 0.6;
        if (v < 0)
        {
            t += -v / PlayerK.Brake;
            v = 0;
        }
        // Along his sprint curve from a standstill: start where his speed already is.
        var c = SprintCurve(q);
        var cv = c.V;
        var cs = c.S;
        var ct = c.T;
        int n = cv.Count;
        // The last point of the curve at or below his speed (V only rises): binary search.
        int k0 = 0;
        int top = n - 1;
        while (k0 < top)
        {
            int mid = (k0 + top + 1) >> 1;
            if (cv[mid] <= v) k0 = mid;
            else top = mid - 1;
        }
        double s0 = cs[k0];
        double goal = s0 + d;
        int last = n - 1;
        if (goal >= cs[last]) return t + ct[last] - ct[k0] + (goal - cs[last]) / cv[last];
        int lo = k0;
        int hi = last;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (cs[mid] < goal) lo = mid;
            else hi = mid;
        }
        double f = (goal - cs[lo]) / Math.Max(1e-6, cs[hi] - cs[lo]);
        return t + ct[lo] + (ct[hi] - ct[lo]) * f - ct[k0];
    }

    /// <summary>A player's sprint from a standstill (Player.Move's acceleration), tabulated; cached.</summary>
    Curve SprintCurve(Player q)
    {
        double top = q.TopSpeed;
        double a = q.AccelRate;
        double key = JsMath.Round(top * 50) * 1000 + JsMath.Round(a * 50);
        var hit = curves[q.Id];
        if (hit != null && hit.Key == key) return hit;
        var c = new Curve { Key = key };
        c.T.Add(0);
        c.S.Add(0);
        c.V.Add(0);
        const double h = 0.05;
        double v = 0;
        double s = 0;
        for (int k = 1; v < top - 0.05 && k < 200; k++)
        {
            double v1 = Math.Min(top, v + (a * Math.Max(0.1, 1 - JsMath.Pow(v / (top + 0.4), 1.6)) + 0.6) * h);
            s += ((v + v1) / 2) * h;
            v = v1;
            c.T.Add(k * h);
            c.S.Add(s);
            c.V.Add(v);
        }
        curves[q.Id] = c;
        return c;
    }

    /// <summary>
    /// Where to go to get to the ball, one rule: the earliest point on the ball's path he can
    /// reach before it does (the intercept, predicted with the real ball physics). In the last
    /// stride, straight onto the ball.
    /// </summary>
    public V3 MeetPoint(Player p, V3 output)
    {
        var b = m.Ball;
        double d = m.BallDist(p);
        if (d < 3)
        {
            // Close: get in its way. A ball coming past is met by stepping across onto its line;
            // one at his feet or running away, by going to where it'll be in a moment.
            double sp = JsMath.Hypot(b.Vel.X, b.Vel.Z);
            if (sp > 2)
            {
                double ux = b.Vel.X / sp;
                double uz = b.Vel.Z / sp;
                double along = (p.Pos.X - b.Pos.X) * ux + (p.Pos.Z - b.Pos.Z) * uz;
                if (along > 0.3) return output.Set(b.Pos.X + ux * along, 0, b.Pos.Z + uz * along);
            }
            if (d < 1.5) return output.Set(b.Pos.X + b.Vel.X * 0.1, 0, b.Pos.Z + b.Vel.Z * 0.1);
        }
        var ip = Intercept[p.Id];
        return output.Set(ip.X, 0, ip.Z);
    }

    /// <summary>Going to meet the ball: flat out when it's tight; with time in hand, just the pace that gets him there as it arrives.</summary>
    void Pace(Player p)
    {
        if (m.BallDist(p) < 3) return;
        p.WantSpeed = Math.Min(p.WantSpeed, Math.Max(PlayerK.JogSpeed * 0.6, MeetPace(p)));
        p.Sprinting = p.WantSpeed > PlayerK.JogSpeed + 0.5;
    }

    /// <summary>The pace that has him at his meeting point as the ball gets there (top speed when it's tight).</summary>
    public double MeetPace(Player p)
    {
        var ip = Intercept[p.Id];
        if (ip.Slack < 0.35) return p.TopSpeed;
        double left = ip.M - (m.Time - ip.At);
        return Math.Min(p.TopSpeed, M.Dist2D(p.Pos.X, p.Pos.Z, ip.X, ip.Z) / Math.Max(0.2, left - 0.25) + 0.8);
    }

    /// <summary>
    /// A team-mate's kick is coming through and it's not for him: if its line passes close
    /// (and low enough to hit him) in the next moment, step off it, to the side he's on.
    /// </summary>
    bool Dodge(Player p)
    {
        var k = m.LastKicker;
        if (k == null || k == p || k.Team != p.Team || m.LastTouch != k || m.Owner != null || m.HeldBy != null || m.PassTarget == p) return false;
        if (m.Time - m.LastKickTime > 2) return false;
        var b = m.Ball;
        double sp = JsMath.Hypot(b.Vel.X, b.Vel.Z);
        if (sp < 8) return false;
        double rx = p.Pos.X - b.Pos.X;
        double rz = p.Pos.Z - b.Pos.Z;
        double t = (rx * b.Vel.X + rz * b.Vel.Z) / (sp * sp);
        if (t <= 0.05 || t > 1.2) return false;
        double lat = (rx * b.Vel.Z - rz * b.Vel.X) / sp;
        if (Math.Abs(lat) > 1.3) return false;
        int i = (int)Math.Min(sampleCount - 1, JsMath.Round(t / SampleDT));
        if (SY[i] > 2.0) return false; // it'll fly over him
        double side = JsMath.Or1(JsMath.Sign(lat));
        MoveTo(p, p.Pos.X + (b.Vel.Z / sp) * side * 2.2, p.Pos.Z - (b.Vel.X / sp) * side * 2.2, true, true);
        return true;
    }

    /// <summary>Active planned run (e.g. onto a through ball), if any.</summary>
    public bool RunTarget(Player p, out double x, out double z)
    {
        var r = run[p.Id];
        x = r.X;
        z = r.Z;
        return r.Until > m.Time;
    }

    /// <summary>Second-last defender line (team frame) the given attacking team must stay behind.</summary>
    public double OffsideLineFor(int team) => offside[team];

    /// <summary>
    /// Your through ball. The stick picks the runner, as for a pass to feet (within 50° of it, judged
    /// by where he's heading), and the hold how deep: a tap finds the nearer man, into his stride; a
    /// full hold the deeper one, into the space in behind. It's never a guess at a spot:
    /// along his run, every strike pace has its own point where the ball and he get there together
    /// (Meet), and of those it takes the one nearest the depth you held for, steering off a spot a
    /// defender reaches first or a line he cuts. A man the ball can't be timed to isn't chosen.
    /// </summary>
    public ThroughPlan? AimedThrough(Player p, double aimX, double aimZ, bool aimed, double power, bool lofted)
    {
        var team = m.Teams[p.Team];
        double dir = team.Dir;
        var b = m.Ball.Pos;
        double line = offside[p.Team];
        if (!aimed)
        {
            aimX = dir;
            aimZ = 0;
        }
        double hold = M.Clamp(power, 0, 1);
        double depth = 2 + 14 * hold;
        // The same rule as a pass to feet (HumanReceiver): the direction first, then the distance
        // the hold asks for, then how good his ball is. Lower cost wins.
        double cone = JsMath.Cos((aimed ? 50 : 70) * Math.PI / 180);
        double want = 8 + 32 * hold;
        ThroughPlan? best = null;
        double bestC = 1e9;
        foreach (var o in team.Players)
        {
            if (o == p || o.Role == Role.GK) continue;
            // Where he's heading: a man already on the move is judged by his run.
            double dx = o.Pos.X + o.Vel.X * 0.6 + dir * 3 - b.X;
            double dz = o.Pos.Z + o.Vel.Z * 0.6 - b.Z;
            double d = JsMath.Hypot(dx, dz);
            if (d < 4 || d > 52) continue;
            if ((dx * aimX + dz * aimZ) / d < cone) continue;
            var tp = Meet(p, o, aimed ? aimX : 0, aimed ? aimZ : 0, depth, lofted);
            if (tp == null) continue;
            double off = Math.Abs(JsMath.Atan2(dx * aimZ - dz * aimX, dx * aimX + dz * aimZ)) * 180 / Math.PI;
            double ahead = (o.Pos.X - b.X) * dir;
            bool offsideNow = o.Pos.X * dir > line + 0.3 && ahead > 0;
            double D = M.Dist2D(b.X, b.Z, tp.X, tp.Z);
            // Its own cost (the depth missed, a defender there first, a cut lane) counts a quarter;
            // a man already beyond the line, or one heading back, isn't the one for a ball in behind.
            double c = off / 12 + Math.Abs(D - want) / 12 + tp.Score / 4 + (offsideNow ? 3 : 0) - M.Clamp(ahead / 20, -0.5, 1) * 0.5;
            if (c < bestC)
            {
                bestC = c;
                best = tp;
            }
        }
        if (best != null) best.Score = -bestC;
        return best;
    }

    /// <summary>
    /// Where a through ball meets runner q, `depth` metres on along his run if it can. His run goes
    /// on the way he's making it (or toward goal), bent by the stick (aimX, aimZ), never back. For
    /// a ground ball each strike pace (4..19 m/s) rolls out a clock; where along his run that clock
    /// equals his own (he's there just as it is) is a meeting. A lob has one clock: its flight to
    /// a little short of him and the bounce on. Of all the meetings: nearest the depth, and not
    /// where a defender is first or a man nearby cuts the line. Score is that cost (lower is better).
    /// </summary>
    ThroughPlan? Meet(Player p, Player q, double aimX, double aimZ, double depth, bool lofted)
    {
        double dir = m.Teams[p.Team].Dir;
        var b = m.Ball.Pos;
        double rx, rz;
        if (q.Speed > 2.5 && q.Vel.X * dir > 0)
        {
            rx = q.Vel.X / q.Speed;
            rz = q.Vel.Z / q.Speed;
        }
        else
        {
            double gx = Pitch.HalfL * dir - q.Pos.X;
            double gz = -q.Pos.Z * 0.5;
            double gn = JsMath.Or1(JsMath.Hypot(gx, gz));
            rx = gx / gn;
            rz = gz / gn;
        }
        rx += aimX * 0.8;
        rz += aimZ * 0.8;
        if (rx * dir < 0) rx = 0;
        double rn = JsMath.Hypot(rx, rz);
        if (rn < 0.05)
        {
            rx = dir;
            rz = 0;
        }
        else
        {
            rx /= rn;
            rz /= rn;
        }
        // His clock along the run, every half metre.
        const int N = 71;
        const double Step = 0.5, From = 1, Early = 0.05;
        Span<double> sx = stackalloc double[N], sz = stackalloc double[N], sd = stackalloc double[N], st = stackalloc double[N];
        for (int i = 0; i < N; i++)
        {
            double L = From + i * Step;
            sx[i] = M.Clamp(q.Pos.X + rx * L, -Pitch.HalfL + 3, Pitch.HalfL - 3);
            sz[i] = M.Clamp(q.Pos.Z + rz * L, -Pitch.HalfW + 2, Pitch.HalfW - 2);
            sd[i] = Math.Max(0.5, M.Dist2D(b.X, b.Z, sx[i], sz[i]));
            st[i] = RunTime(q, sx[i], sz[i], PlanReactRun, PlayerK.Reach * 0.8) - Early;
        }
        ThroughPlan? best = null;
        int lo = lofted ? 0 : 4, hi = lofted ? 0 : 19;
        // With no meeting at all (he's too far on for any ball to catch), the nearest miss, marked down.
        int missV = -1;
        double missL = 0, missF = 1e9;
        for (int v0 = lo; v0 <= hi; v0++)
        {
            double prev = double.NaN;
            for (int i = 0; i < N; i++)
            {
                double tb = BallClock(v0, sd[i]);
                double f = tb < 0 ? double.NaN : tb - st[i];
                if (!double.IsNaN(f) && Math.Abs(f) < missF)
                {
                    missF = Math.Abs(f);
                    missV = v0;
                    missL = From + i * Step;
                }
                // A sign change: between these two points ball and runner cross. Close in on it.
                if (i > 0 && !double.IsNaN(f) && !double.IsNaN(prev) && (prev > 0) != (f > 0))
                    Consider(From + (i - 1 + prev / (prev - f)) * Step, v0, 0);
                prev = f;
            }
        }
        if (best == null && missV >= 0) Consider(missL, missV, 6 + missF * 10);
        return best;

        void Consider(double L, int v0, double extra)
        {
            double x = M.Clamp(q.Pos.X + rx * L, -Pitch.HalfL + 3, Pitch.HalfL - 3);
            double z = M.Clamp(q.Pos.Z + rz * L, -Pitch.HalfW + 2, Pitch.HalfW - 2);
            double D = Math.Max(0.5, M.Dist2D(b.X, b.Z, x, z));
            double tr = RunTime(q, x, z, PlanReactRun, PlayerK.Reach * 0.8);
            double kx = (x - b.X) / D;
            double kz = (z - b.Z) / D;
            double tOpp = 1e9;
            foreach (var o in m.Teams[1 - p.Team].Players) tOpp = Math.Min(tOpp, RunTime(o, x, z, PlanReactOpp, PlayerK.Reach * 0.8));
            double cost = extra + Math.Abs(L - depth) + (tOpp < tr + 0.1 ? 8 : 0) + (!lofted && Cuts(p, b, kx, kz, D, v0) ? 8 : 0);
            if (best != null && cost >= best.Score) return;
            double arrive = 0;
            if (!lofted) Kick.RollAt(v0, tr - Early, out _, out arrive);
            best = new ThroughPlan
            {
                Receiver = q, X = x, Z = z, Time = tr, Arrive = arrive, Dx = kx, Dz = kz, V0 = v0, Score = cost,
                // A lob drops a little short, to bounce on into his path.
                LandX = lofted ? b.X + kx * D * 0.88 : x,
                LandZ = lofted ? b.Z + kz * D * 0.88 : z,
            };
        }
    }

    /// <summary>
    /// Seconds for a through ball to cover `D` metres: rolled at strike pace v0 (-1 if it doesn't get
    /// there still moving), or for v0 = 0 lobbed (down at 0.88 D, the last stretch on the bounce).
    /// </summary>
    static double BallClock(int v0, double D)
    {
        if (v0 == 0) return Kick.ThroughLobTime(D * 0.88) * 1.23;
        double t = Kick.RollTimeAt(v0, D);
        if (t < 0) return -1;
        Kick.RollAt(v0, t, out _, out double v);
        return v < 1.5 ? -1 : t;
    }

    /// <summary>
    /// Whether a defender near its line gets a foot to a ground ball struck at `v0` toward a spot
    /// `D` metres off along (kx, kz): he can step across before it's past him (it travels at
    /// about three quarters of its strike pace). And a soft one is taken off the feet of a man
    /// right on the ball.
    /// </summary>
    bool Cuts(Player p, V3 b, double kx, double kz, double D, double v0)
    {
        foreach (var o in m.Teams[1 - p.Team].Players)
        {
            double ox = o.Pos.X - b.X;
            double oz = o.Pos.Z - b.Z;
            if (v0 < 10 && ox * ox + oz * oz < 4) return true;
            double along = ox * kx + oz * kz;
            if (along < 0.3 || along > D) continue;
            double perp = Math.Abs(ox * kz - oz * kx);
            if (perp < 1.3 + Math.Max(0, along / (0.75 * v0) - PlanReactOpp) * 3) return true;
        }
        return false;
    }

    /// <summary>
    /// The computer's through ball, planned as the strike it is: a direction and a pace for the ball. For each
    /// candidate runner, run lines and lead distances give directions, and the pace that has the
    /// ball there as he arrives (or a little firmer). On each such ball's exact path, the
    /// defenders' and the keeper's earliest arrivals are worked out by how they really run, and
    /// the runner's meeting point by the same rule he'll use to go and get it (Meeting). Only a
    /// ball he gets to safely first is a through ball; among those: progress, danger, the
    /// margin, a ball he can take in his stride, and offside.
    /// </summary>
    public ThroughPlan? PlanThrough(Player p, bool lofted, Player? only)
    {
        var team = m.Teams[p.Team];
        double dir = team.Dir;
        double gx = Pitch.HalfL * dir;
        var b = m.Ball.Pos;
        near.Clear();
        near.AddRange(m.Teams[1 - p.Team].Players);
        double line = offside[p.Team];
        var P = (x: pathX, y: pathY, z: pathZ, v: pathV);
        ThroughPlan? best = null;
        double bestS = -1e9;
        // The most advanced runners first: the best ball is usually theirs, and once it's known
        // most of the others can be passed over without asking the defenders.
        runners.Clear();
        foreach (var q in team.Players) if (q != p && q.Role != Role.GK && (only == null || q == only)) runners.Add(q);
        M.StableSort(runners, (a, c) => c.Pos.X * dir - a.Pos.X * dir);
        foreach (var q in runners)
        {
            if (q.Pos.X * dir < b.X * dir - 8) continue; // well behind the ball: not a through ball
            if (M.Dist2D(q.Pos.X, q.Pos.Z, b.X, b.Z) > 48) continue;
            bool offsideNow = q.Pos.X * dir > line + 0.3 && q.Pos.X * dir > b.X * dir;
            // Defenders nearest him first: if anyone beats him to it, it's usually one of them.
            M.StableSort(near, (a, c) => M.Dist2D(a.Pos.X, a.Pos.Z, q.Pos.X, q.Pos.Z) - M.Dist2D(c.Pos.X, c.Pos.Z, q.Pos.X, q.Pos.Z));
            // Candidate run lines.
            dirs.Clear();
            {
                double tx = gx - q.Pos.X;
                double tz = -q.Pos.Z * 0.6;
                double n0 = JsMath.Or1(JsMath.Hypot(tx, tz));
                dirs.Add((tx / n0, tz / n0));
            }
            dirs.Add((dir, 0));
            // Diagonal runs into the channels either side.
            dirs.Add((dir * JsMath.Cos(0.45), JsMath.Sin(0.45)));
            dirs.Add((dir * JsMath.Cos(0.45), -JsMath.Sin(0.45)));
            if (q.Speed > 2) dirs.Add((q.Vel.X / q.Speed, q.Vel.Z / q.Speed));
            tried.Clear();
            // Where he'll meet it: along each run line at each lead.
            spots.Clear();
            foreach (var (ux, uz) in dirs)
            {
                if (ux * dir < -0.2) continue; // through balls go forward
                foreach (double L in Leads)
                {
                    spots.Add(q.Pos.X + ux * L);
                    spots.Add(q.Pos.Z + uz * L);
                    spots.Add(L);
                }
            }
            for (int si = 0; si < spots.Count; si += 3)
            {
                double L = spots[si + 2];
                double x = M.Clamp(spots[si], -Pitch.HalfL + 3, Pitch.HalfL - 3);
                double z = M.Clamp(spots[si + 1], -Pitch.HalfW + 1.5, Pitch.HalfW - 1.5);
                // Not into the six-yard box: that's the keeper's ball.
                if ((gx - x) * dir < Pitch.SixDepth + 1 && Math.Abs(z) < Pitch.SixHalfWidth + 1) continue;
                double D = M.Dist2D(b.X, b.Z, x, z);
                if (D < 6) continue;
                double kx = (x - b.X) / D;
                double kz = (z - b.Z) / D;
                double tr = RunTime(q, x, z, PlanReactRun);
                double tFly = 0;
                int nv;
                double v0a, v0b = 0;
                if (lofted)
                {
                    tFly = 0.55 + D / 17;
                    v0a = 0;
                    nv = 1;
                }
                else
                {
                    // Paced to get there a touch before him (into a long run, a little firmer too: he meets it sooner).
                    if (!Kick.RollingPass(D, Math.Max(0.5, tr - 0.15), out var rp)) continue;
                    v0a = rp.V0;
                    if (L >= 11 && rp.V0 + 2 <= 19)
                    {
                        v0b = rp.V0 + 2;
                        nv = 2;
                    }
                    else nv = 1;
                }
                for (int vi = 0; vi < nv; vi++)
                {
                    double v0 = vi == 0 ? v0a : v0b;
                    double key = JsMath.Round(JsMath.Atan2(kz, kx) * 40) * 64 + (lofted ? JsMath.Round(D) : v0);
                    if (!tried.Add(key)) continue;
                    // The ball's path, every SampleDT.
                    int n = 0;
                    if (lofted)
                    {
                        // Flight to the spot (too high to play), the bounce, then running on.
                        double h = (9.81 * tFly * tFly) / 8;
                        double vh = D / tFly;
                        for (; n < Samples; n++)
                        {
                            double t = n * SampleDT;
                            double u = t / tFly;
                            double d = u < 1 ? D * u : D + vh * 0.55 * (t - tFly) - 0.75 * (t - tFly) * (t - tFly);
                            P.x[n] = b.X + kx * d;
                            P.z[n] = b.Z + kz * d;
                            P.y[n] = u < 1 ? 4 * h * u * (1 - u) : 0;
                            P.v[n] = u < 1 ? vh : Math.Max(0, vh * 0.55 - 1.5 * (t - tFly));
                            if (u >= 1 && P.v[n] <= 0) break;
                        }
                    }
                    else
                    {
                        int still = 0;
                        for (; n < Samples; n++)
                        {
                            Kick.RollAt(v0, n * SampleDT, out double rd, out double rv);
                            P.x[n] = b.X + kx * rd;
                            P.z[n] = b.Z + kz * rd;
                            P.y[n] = 0;
                            P.v[n] = rv;
                            // A dead ball waits a moment for whoever gets there.
                            if (rv < 0.3 && ++still > 6) break;
                        }
                    }
                    n = Math.Min(n + 1, Samples);
                    int first = Arrival(q, P.x, P.y, P.z, n, PlanReactRun);
                    if (first < 0) continue;
                    // Unopposed, where would he take it, and what's the most that could be worth? (Only
                    // a ball that could beat the best so far is worth asking the defenders about.)
                    int k0 = Meeting(q, P.x, P.y, P.z, n, first, 1e9, PlanReactRun, 0);
                    if (k0 < 0) continue;
                    double ub = ((P.x[k0] - b.X) * dir) / 20 * 0.9 + (1 - M.Clamp(M.Dist2D(P.x[k0], P.z[k0], gx, 0) / 38, 0, 1)) * 0.9 + 0.8 + q.Attrs.Pace * 0.25 + (q.Role == Role.FWD ? 0.2 : 0);
                    if (ub - (offsideNow ? 4 : 0) <= bestS) continue;
                    // The other side: the first of them to the ball (no need to look past where he'd take it).
                    int look = (int)Math.Min(n, k0 + Math.Ceiling(PlanMargin / SampleDT) + 2);
                    double tOpp = 1e9;
                    foreach (var o in near)
                    {
                        int i = Arrival(o, P.x, P.y, P.z, (int)Math.Min(look, tOpp < 1e8 ? JsMath.Round(tOpp / SampleDT) : look), PlanReactOpp);
                        if (i >= 0) tOpp = Math.Min(tOpp, i * SampleDT);
                        if (tOpp <= first * SampleDT) break;
                    }
                    if (tOpp <= first * SampleDT) continue;
                    int k = tOpp > 1e8 ? k0 : Meeting(q, P.x, P.y, P.z, n, first, tOpp, PlanReactRun, PlanMargin);
                    if (k < 0) continue;
                    double mx = P.x[k];
                    double mz = P.z[k];
                    double tm = k * SampleDT;
                    if (M.Dist2D(mx, mz, b.X, b.Z) < 6) continue; // that's a pass to feet
                    // Taking it in stride: the ball's pace against his run onto it.
                    double md = JsMath.Hypot(mx - q.Pos.X, mz - q.Pos.Z);
                    double runAlong = md > 0.5 ? Math.Max(0, ((mx - q.Pos.X) * kx + (mz - q.Pos.Z) * kz) / md) : 0;
                    double rel = lofted ? 0 : P.v[k] - q.TopSpeed * 0.85 * runAlong;
                    double progress = ((mx - b.X) * dir) / 20;
                    double threat = 1 - M.Clamp(M.Dist2D(mx, mz, gx, 0) / 38, 0, 1);
                    double sc =
                        progress * 0.9 +
                        threat * 0.9 +
                        M.Clamp((tOpp - tm - PlanMargin) / 0.6, 0, 1) * 0.8 -
                        Math.Max(0, rel - 1) * 0.3 +
                        q.Attrs.Pace * 0.25 +
                        (q.Role == Role.FWD ? 0.2 : 0);
                    if (offsideNow) sc -= 4;
                    if (sc > bestS)
                    {
                        bestS = sc;
                        best = new ThroughPlan { Receiver = q, X = mx, Z = mz, Arrive = P.v[k], Time = tm, V0 = v0, Dx = kx, Dz = kz, LandX = x, LandZ = z, Score = sc };
                    }
                }
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ perception

    void ComputeIntercepts()
    {
        var b = m.Ball;
        int n = 0;
        if (m.HeldBy != null || m.Phase != Phase.Play)
        {
            for (int i = 0; i < Samples; i++)
            {
                SX[i] = b.Pos.X;
                SY[i] = b.Pos.Y;
                SZ[i] = b.Pos.Z;
            }
            n = Samples;
        }
        else
        {
            int next = 0;
            var pb = Kick.LoadPrediction(b);
            const double maxT = Samples * SampleDT;
            double t = 0;
            while (t < maxT)
            {
                if (t + 1e-6 >= next * SampleDT && n < Samples)
                {
                    SX[n] = pb.Pos.X;
                    SY[n] = pb.Pos.Y;
                    SZ[n] = pb.Pos.Z;
                    n++;
                    next++;
                }
                if (n >= Samples) break;
                pb.Step(DT * 2);
                t += DT * 2;
            }
            while (n < Samples)
            {
                SX[n] = SX[n - 1];
                SY[n] = SY[n - 1];
                SZ[n] = SZ[n - 1];
                n++;
            }
        }
        sampleCount = n;
        InterceptAt = m.Time;

        // Who can be at the ball first, by the way each player really runs (RunTime against the
        // ball's own clock): his earliest arrival on its path.
        double first0 = 1e9, first1 = 1e9;
        foreach (var p in m.Players)
        {
            var ip = Intercept[p.Id];
            int i = Arrival(p, SX, SY, SZ, n, LiveReact);
            ip.T = i < 0 ? -1 : i * SampleDT;
            if (i >= 0)
            {
                if (p.Team == 0) first0 = Math.Min(first0, ip.T);
                else first1 = Math.Min(first1, ip.T);
            }
        }
        // Where each one takes it: the best point on the path he gets to safely ahead of the other
        // side (see Meeting). With none, he attacks it at his earliest; out of reach, he goes for
        // the point he gets closest to in time.
        foreach (var p in m.Players)
        {
            var ip = Intercept[p.Id];
            double prev = ip.At + ip.M - m.Time;
            int i = ip.T < 0 ? -1 : Meeting(p, SX, SY, SZ, n, (int)JsMath.Round(ip.T / SampleDT), p.Team == 0 ? first1 : first0, LiveReact, LiveMargin, prev);
            if (i < 0 && ip.T >= 0) i = (int)JsMath.Round(ip.T / SampleDT);
            if (i < 0)
            {
                double top = p.TopSpeed;
                double bestDef = 1e9;
                i = n - 1;
                for (int k = 0; k < n; k++)
                {
                    double deficit = M.Dist2D(p.Pos.X, p.Pos.Z, SX[k], SZ[k]) - PlayerK.Reach * 0.8 - Math.Max(0, k * SampleDT - 0.2) * top;
                    if (deficit < bestDef)
                    {
                        bestDef = deficit;
                        i = k;
                    }
                }
            }
            ip.X = SX[i];
            ip.Z = SZ[i];
            ip.M = i * SampleDT;
            ip.At = m.Time;
            int i0 = Math.Max(0, i - 1);
            int i1 = Math.Min(n - 1, i + 1);
            double h = i1 > i0 ? (i1 - i0) * SampleDT : 1;
            ip.VX = (SX[i1] - SX[i0]) / h;
            ip.VZ = (SZ[i1] - SZ[i0]) / h;
            ip.Slack = ip.M - RunTime(p, ip.X, ip.Z, 0.05, PlayerK.Reach * 0.75);
        }

        for (int t = 0; t < 2; t++)
        {
            Player? best = null;
            double bt = 1e9;
            foreach (var p in m.Teams[t].Players)
            {
                var ip = Intercept[p.Id];
                // Keepers: anything in the box, and as a sweeper a ball over the top he's clearly first to.
                if (p.Role == Role.GK && !InOwnBox(p, ip.X, ip.Z)
                    && !(ip.T >= 0 && ip.T + 0.45 < (t == 0 ? first1 : first0) && Pitch.HalfL - ip.X * -m.Teams[t].Dir < 32)) continue;
                if (m.OffsideFlagged(p) && m.PassTarget != p) continue;
                double score = ip.T >= 0 ? ip.T : 10 + M.Dist2D(p.Pos.X, p.Pos.Z, ip.X, ip.Z) / p.TopSpeed;
                if (score < bt)
                {
                    bt = score;
                    best = p;
                }
            }
            Chaser[t] = best;
            // Offside line for the *other* team: second-last defender of team t.
            double dir = m.Teams[t].Dir;
            double a = -1e9;
            double bb = -1e9;
            foreach (var p in m.Teams[t].Players)
            {
                double v = -p.Pos.X * dir; // depth toward own goal in attacker's frame
                if (v > a)
                {
                    bb = a;
                    a = v;
                }
                else if (v > bb) bb = v;
            }
            double ballInAttFrame = b.Pos.X * -dir;
            offside[1 - t] = Math.Max(Math.Max(bb, ballInAttFrame), 0);
        }
    }

    /// <summary>Highest ball (m) this player can play at (x, z): keepers in their box use their hands.</summary>
    double PlayHeight(Player p, double x, double z) => p.Role == Role.GK && InOwnBox(p, x, z) ? 2.5 : p.HeadReach;

    /// <summary>
    /// The first sample of a ball path (positions every SampleDT from now) at which `q` can be
    /// at the ball, by his real running (RunTime), or -1. A cheap bound skips the samples he
    /// can't possibly reach at top speed.
    /// </summary>
    int Arrival(Player q, F32 xs, F32 ys, F32 zs, int n, double react)
    {
        const double reach = PlayerK.Reach * 0.8;
        double top = q.TopSpeed + 0.5;
        double px = q.Pos.X;
        double pz = q.Pos.Z;
        bool gk = q.Role == Role.GK;
        double head = q.HeadReach;
        const double R = PlayerK.Reach + 0.15;
        const double RB = R + 0.01;
        for (int i = 0; i < n; i++)
        {
            double t = i * SampleDT;
            if (ys[i] > (gk ? PlayHeight(q, xs[i], zs[i]) : head)) continue;
            // A ball going past within reach of where he stands is his without a step: check the
            // whole stretch it travels up to this sample, not just the sample.
            // (Until he reacts, he carries on the way he's going.)
            if (i > 0)
            {
                double tc = Math.Min(t, react + 0.1);
                double cx = px + q.Vel.X * tc;
                double cz = pz + q.Vel.Z * tc;
                double ax = xs[i - 1];
                double az = zs[i - 1];
                double bx = xs[i];
                double bz = zs[i];
                // (Out of the stretch's bounding box widened by the reach: it can't come near him.)
                bool isNear = cx > Math.Min(ax, bx) - RB && cx < Math.Max(ax, bx) + RB && cz > Math.Min(az, bz) - RB && cz < Math.Max(az, bz) + RB;
                if (isNear)
                {
                    double vx = bx - ax;
                    double vz = bz - az;
                    double l2 = vx * vx + vz * vz;
                    double k = l2 > 1e-6 ? M.Clamp(((cx - ax) * vx + (cz - az) * vz) / l2, 0, 1) : 0;
                    double ex = cx - ax - vx * k;
                    double ez = cz - az - vz * k;
                    if (ex * ex + ez * ez < R * R && ys[i - 1] < 1) return i;
                }
            }
            double d = M.Dist2D(px, pz, xs[i], zs[i]) - reach;
            if (react + d / top > t + 0.05) continue;
            if (RunTime(q, xs[i], zs[i], react, reach) <= t) return i;
        }
        return -1;
    }

    /// <summary>
    /// Where on a ball path `q` should take it, given the other side's earliest arrival (`tOpp`):
    /// of the points he can be at in time, inside the pitch and safely before them, the one worth
    /// most: further forward is better, waiting costs, cutting it fine with them closing costs
    /// more. The passer plans with the same rule, so runner and ball meet where the pass was
    /// meant to be met. `prev` (seconds from now) keeps a meeting he's already going for.
    /// -1: no safe point.
    /// </summary>
    int Meeting(Player q, F32 xs, F32 ys, F32 zs, int n, int from, double tOpp, double react, double margin, double prev = -9)
    {
        double dir = m.Teams[q.Team].Dir;
        const double reach = PlayerK.Reach * 0.8;
        // A pass played to him: he comes to it, taking it at the first point he safely can at his
        // feet (not drifting off with it for a few metres more).
        bool mine = m.PassTarget == q && m.LastKicker?.Team == q.Team;
        bool toHim = mine && !m.PassIntoSpace;
        // Any pass meant for him (into space included: it was weighted for where they meet), and
        // any ball for the player you control, he takes at the first point he safely can, rather
        // than racing ahead to let it run on to him.
        bool yours = m.Piloted(q) && !m.AutoPlay;
        double progress = mine || yours ? 0 : MeetProgress;
        int best = -1;
        double bestV = -1e9;
        for (int i = Math.Max(0, from); i < n; i++)
        {
            double t = i * SampleDT;
            // (A pass to him is his to fight for: a man closing only costs, as below.)
            if (t > tOpp - margin && !toHim) break;
            if (Math.Abs(xs[i]) > Pitch.HalfL - 0.5 || Math.Abs(zs[i]) > Pitch.HalfW - 0.5) break;
            if (ys[i] > PlayHeight(q, xs[i], zs[i])) continue;
            // (and at his feet: a lifted pass he lets drop, rather than heading it on)
            if (toHim && ys[i] > 0.8) continue;
            if (i > from)
            {
                // Cheap bound first: RunTime is never under react + distance / top speed.
                double dd = M.Dist2D(q.Pos.X, q.Pos.Z, xs[i], zs[i]) - reach;
                if (dd >= 0.3 && react + dd / (q.TopSpeed + 0.5) > t + 0.05) continue;
                if (RunTime(q, xs[i], zs[i], react, reach) > t) continue;
            }
            double v = xs[i] * dir * progress - t * MeetWait - Math.Max(0, MeetClose - (tOpp - t)) * MeetContest;
            if (Math.Abs(t - prev) < 0.2) v += 0.6;
            if (v > bestV)
            {
                bestV = v;
                best = i;
            }
        }
        return best;
    }

    public bool InOwnBox(Player p, double x, double z)
    {
        double own = -m.Teams[p.Team].Dir;
        return x * own > Pitch.HalfL - Pitch.BoxDepth && Math.Abs(z) < Pitch.BoxHalfWidth;
    }

    // ------------------------------------------------------------------ main

    public void Update()
    {
        if (m.Time >= nextIntercept)
        {
            ComputeIntercepts();
            nextIntercept = m.Time + 0.1;
        }
        // A keeper without the ball shouldn't stay human-controlled.
        // (Unless he's playing it with his feet: a pass to him from a team-mate, or the ball at his feet.)
        for (int t = 0; t < 2; t++)
            if (m.HumanSide(t)) AutoSwitch(m.Seats[t], t);

        if (m.Owner != lastOwner)
        {
            lastOwner = m.Owner;
            possStart = m.Time;
            patience = 0.35 + m.Rng.Next() * 0.9;
        }

        TeamPlay();

        foreach (var p in m.Players)
        {
            bool isTaker = m.SetPiece != null && m.SetPiece.Taker == p;
            // Your keeper sitting on the ball: after six seconds he plays it himself.
            bool keeperSits = p.Role == Role.GK && m.HeldBy == p && m.SetPiece == null && p.Plan == null && m.Time - m.HeldSince > 6;
            if (m.Piloted(p) && !isTaker && !keeperSits && m.Phase != Phase.Goal && !m.AutoPlay) continue;
            Think(p);
        }
    }

    /// <summary>A human's player switches by himself: off a keeper who's done with the ball,
    /// and on defence or a loose ball to the team-mate who should take it.</summary>
    void AutoSwitch(Seat seat, int team)
    {
        var ctl = seat.Controlled;
        bool keeperFeet = m.Owner == ctl || (m.PassTarget == ctl && m.LastKicker?.Team == ctl.Team);
        if (ctl.Role == Role.GK && !m.KeeperHuman && m.HeldBy != ctl && !keeperFeet && m.Phase == Phase.Play && !(m.SetPiece != null && m.SetPiece.Taker == ctl))
        {
            var ch = Chaser[team];
            if (ch != null && ch.Role != Role.GK) m.SetControlled(ch);
        }
        // Auto-switch on defence / loose balls to the teammate who should take the ball.
        int att = m.AttackingTeam();
        var c = seat.Controlled;
        if (m.Phase == Phase.Play && att != team && m.ShotTeam() != team && seat.SwitchT > 0.45 && c.Action != ActionKind.Tackle && c.Action != ActionKind.Slide)
        {
            var ci = Intercept[c.Id];
            double ct = ci.T >= 0 ? ci.T : 9;
            double cd = m.BallDist(c);
            // Is the stick pushing toward the ball? (Then the player clearly means to chase.)
            double toward = 0;
            if (seat.NoInputT == 0 && cd > 0.5)
            {
                double bx = (m.Ball.Pos.X - c.Pos.X) / cd;
                double bz = (m.Ball.Pos.Z - c.Pos.Z) / cd;
                toward = c.TouchX * bx + c.TouchZ * bz;
            }
            bool idle = seat.NoInputT > 0.25;
            double margin = idle ? 0.25 : toward > 0.6 ? 0.9 : 0.4;
            Player? best = null;
            double bestScore = 0;
            foreach (var q in m.Teams[team].Players)
            {
                if (q == c || q.Role == Role.GK || q.Action == ActionKind.Stumble || q.Action == ActionKind.Fall) continue;
                var qi = Intercept[q.Id];
                double qt = qi.T >= 0 ? qi.T : 9;
                double qd = m.BallDist(q);
                // Clearly closer to the ball (half the distance and at least 4 m nearer)…
                bool muchCloser = qd < cd * 0.5 && cd - qd > 4;
                // …or gets there meaningfully sooner.
                bool sooner = ct - qt > margin && cd > 2.5;
                if (!muchCloser && !sooner) continue;
                double score = (ct - qt) + (cd - qd) * 0.15;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = q;
                }
            }
            if (best != null) m.SetControlled(best);
        }
    }

    void Think(Player p)
    {
        p.LookAt = null;
        p.SquareUp = false;
        p.Burst = false;
        p.Sprinting = false;
        switch (m.Phase)
        {
            case Phase.Goal:
                Celebrate(p);
                return;
            case Phase.Out:
                if (m.InvaderWalk(p)) return;
                // Play's stopped: ease off and watch the ball.
                p.WantSpeed = Math.Max(0, p.WantSpeed - DT * 5);
                p.LookTarget.Copy(m.Ball.Pos);
                p.LookAt = p.LookTarget;
                return;
            case Phase.Halftime:
            case Phase.Fulltime:
                p.WantSpeed = 0;
                return;
            case Phase.Kickoff:
            case Phase.SetPiece:
                SetPieceThink(p);
                return;
        }
        if (m.Owner == p)
        {
            CarrierThink(p);
            return;
        }
        if (p.Role == Role.GK)
        {
            KeeperThink(p);
            return;
        }

        int att = m.AttackingTeam();
        var r = run[p.Id];
        if (m.PassTarget == p)
        {
            // Run with the body where he's going (squaring up to the ball only in the last few
            // metres, in MoveTo): facing a ball played from behind would have him backpedalling.
            MeetPoint(p, tmp);
            MoveTo(p, tmp.X, tmp.Z, true, false);
            Pace(p);
            // As it arrives he opens his body to it, set to take it.
            p.SquareUp = m.BallDist(p) < 6;
            p.Burst = m.BallDist(p) < 2.5;
            p.Sprinting = m.BallDist(p) > 6;
            return;
        }
        // A team-mate's kick coming through: get out of its way.
        if (Dodge(p)) return;
        // A cross is on: attackers fill the box, defenders drop in to mark it.
        var crossCarrier = m.Owner != null && m.InCrossZone(m.Owner.Team, m.Owner.Pos.X, m.Owner.Pos.Z) ? m.Owner : null;
        bool pressing = crossCarrier != null && crossCarrier.Team != p.Team && Chaser[p.Team] == p;
        if (crossCarrier != null && crossCarrier != p && !pressing && r.Until <= m.Time)
        {
            if (BoxSpot(p, crossCarrier, crossCarrier.Team == p.Team, out double sx, out double sz))
            {
                MoveTo(p, sx, sz, M.Dist2D(p.Pos.X, p.Pos.Z, sx, sz) > 6, true);
                return;
            }
        }

        if (att == p.Team)
        {
            // Forwards (and sometimes midfielders) attack the space behind the last line.
            var carrier = m.Owner ?? m.HeldBy;
            if (r.Until <= m.Time && carrier != null && carrier != p && (p.Role == Role.FWD || p.Role == Role.MID))
            {
                double dir = m.Teams[p.Team].Dir;
                double cx = carrier.Pos.X * dir;
                double line = offside[p.Team];
                bool nearLine = p.Pos.X * dir > line - 9 && p.Pos.X * dir < line + 0.5;
                // The cue: the man on the ball has time and is looking up the pitch. That is when a
                // runner goes, from onside, so the pass can meet him in stride. Without it, rarely.
                bool cue = m.NearestOpponentDist(carrier) > 4.5 && JsMath.Cos(carrier.Facing) * dir > 0.3;
                double chance = (p.Role == Role.FWD ? (cue ? 0.03 : 0.002) : cue ? 0.006 : 0.0005) * (cx > -10 ? 1 : 0.3);
                if (nearLine && m.Rng.Next() < chance)
                {
                    // Bend it into the gap: away from the nearest defender, never off toward the flag.
                    double nz = 0;
                    double nd = double.PositiveInfinity;
                    foreach (var q in m.Teams[1 - p.Team].Players)
                    {
                        double d = M.Dist2D(p.Pos.X, p.Pos.Z, q.Pos.X, q.Pos.Z);
                        if (d < nd)
                        {
                            nd = d;
                            nz = q.Pos.Z;
                        }
                    }
                    double away = nd < 8 ? JsMath.Sign(JsMath.Or1(p.Pos.Z - nz)) * 4 : 0;
                    double tz = p.Pos.Z * 0.7 + away + (m.Rng.Next() - 0.5) * 6;
                    SetRun(p, (line + 7 + m.Rng.Next() * 6) * dir, tz);
                    r.Until = m.Time + 2.2;
                }
            }
            if (r.Until > m.Time)
            {
                MoveTo(p, r.X, r.Z, true, false);
                return;
            }
            OffBall(p, true);
            return;
        }
        // Our own shot in flight: nobody runs onto it; follow it in (rebounds) in shape.
        if (m.ShotTeam() == p.Team)
        {
            OffBall(p, true);
            return;
        }
        // Defending or loose ball. (The ball in their keeper's hands can't be challenged: drop off.)
        if (Chaser[p.Team] == p && (m.HeldBy == null || m.HeldBy.Team == p.Team))
        {
            if (m.Owner != null && m.Owner.Team != p.Team)
            {
                Press(p, m.Owner);
                return;
            }
            MeetPoint(p, tmp);
            MoveTo(p, tmp.X, tmp.Z, true, true);
            Pace(p);
            p.Burst = m.BallDist(p) < 2.5;
            return;
        }
        // Second defender: cover goal-side of the ball if close.
        if (m.Owner != null && m.Owner.Team != p.Team && IsSecondPresser(p))
        {
            // Springing the trap: he goes in too, from the other side.
            if (trapUntil[p.Team] > m.Time)
            {
                Press(p, m.Owner);
                return;
            }
            double gx = -m.Teams[p.Team].Dir * Pitch.HalfL;
            double bx = m.Ball.Pos.X;
            double bz = m.Ball.Pos.Z;
            double dx = gx - bx;
            double dz = -bz;
            double d = Math.Max(0.1, JsMath.Hypot(dx, dz));
            MoveTo(p, bx + (dx / d) * 6, bz + (dz / d) * 6, false, false);
            p.LookTarget.Copy(m.Ball.Pos);
            p.LookAt = p.LookTarget;
            return;
        }
        OffBall(p, false);
    }

    /// <summary>
    /// Off-ball movement as a small steering field. The shape slot is the anchor; on top of it
    /// each player adds what matters to him right now (attacking: drift into a pocket of space
    /// with a clear lane from the ball; defending: pick up the most dangerous attacker in his
    /// zone and stand goal-side; always: keep apart from teammates). Each player's goal eases
    /// toward the result at his own reading speed, so a turnover ripples through the team.
    /// </summary>
    void OffBall(Player p, bool attacking)
    {
        var tr = Traits[p.Id];
        double dir = m.Teams[p.Team].Dir;
        Slot(p, tmp);
        double ax = tmp.X;
        double az = tmp.Z;
        double tx = ax;
        double tz = az;

        if (attacking)
        {
            if (m.Time >= seekAt[p.Id]) SeekSpace(p, ax, az);
            tx += seekDX[p.Id] * dir;
            tz += seekDZ[p.Id] * dir;
        }
        else
        {
            if (m.Time >= markAt[p.Team]) AssignMarks(p.Team);
            var a = mark[p.Id];
            if (a != null)
            {
                // Goal-side of his man, shaded toward the ball; tighter the nearer our goal.
                double gx0 = -dir * Pitch.HalfL;
                double gdx = gx0 - a.Pos.X;
                double gdz = -a.Pos.Z * 0.7;
                double gd = Math.Max(0.1, JsMath.Hypot(gdx, gdz));
                double bdx = m.Ball.Pos.X - a.Pos.X;
                double bdz = m.Ball.Pos.Z - a.Pos.Z;
                double bd = Math.Max(0.1, JsMath.Hypot(bdx, bdz));
                double danger = 1 - M.Clamp((a.Pos.X * -dir + Pitch.HalfL) / Pitch.HalfL, 0, 1);
                double gap = (1.4 + (1 - danger) * 2.2) * (trapUntil[p.Team] > m.Time ? 0.55 : 1);
                double mx = a.Pos.X + (gdx / gd) * gap + (bdx / bd) * 0.9;
                double mz = a.Pos.Z + (gdz / gd) * gap + (bdz / bd) * 0.9;
                // Defenders step out of the line only so far; beyond that they pass him on.
                if (p.Role == Role.DEF) mx = dir * Math.Min(mx * dir, ax * dir + 5);
                double w = M.Clamp(0.45 + danger * 0.35 + (1 - tr.Discipline) * 0.15, 0, 0.92);
                tx += (mx - tx) * w;
                tz += (mz - tz) * w;
            }
        }

        // Separation: nobody crowds a teammate's space.
        double sx = 0;
        double sz = 0;
        foreach (var q in m.Teams[p.Team].Players)
        {
            if (q == p || q.Role == Role.GK) continue;
            double dx = tx - q.Pos.X;
            double dz = tz - q.Pos.Z;
            double d2 = dx * dx + dz * dz;
            if (d2 > 49 || d2 < 1e-4) continue;
            double dd = Math.Sqrt(d2);
            double f = ((7 - dd) / 7) * 2.6;
            sx += (dx / dd) * f;
            sz += (dz / dd) * f;
        }
        tx = M.Clamp(tx + sx, -Pitch.HalfL + 1.5, Pitch.HalfL - 1.5);
        tz = M.Clamp(tz + sz, -Pitch.HalfW + 1, Pitch.HalfW - 1);

        // Reading of the game: the goal follows the field at the player's own pace.
        if (m.Time - offBallT[p.Id] > 0.25)
        {
            goalX[p.Id] = tx;
            goalZ[p.Id] = tz;
        }
        offBallT[p.Id] = m.Time;
        // The smoothing rate depends only on his reactions: worked out once.
        double k = offK[p.Id];
        if (double.IsNaN(k)) k = offK[p.Id] = 1 - JsMath.Exp(-DT / (0.18 + (1 - tr.React) * 0.55));
        double gx = goalX[p.Id] += (tx - goalX[p.Id]) * k;
        double gz = goalZ[p.Id] += (tz - goalZ[p.Id]) * k;

        // Recovery: caught upfield after a turnover (or badly out of place), hard workers sprint back.
        double d = M.Dist2D(p.Pos.X, p.Pos.Z, gx, gz);
        bool behindPlay = !attacking && (p.Pos.X - m.Ball.Pos.X) * dir > 2;
        bool turnover = m.Time - possStart < 3;
        bool urgent = d > 6 + (1 - tr.Work) * 10 && (behindPlay || turnover);
        MoveTo(p, gx, gz, urgent, !attacking || d < 6);
        if (!urgent) p.WantSpeed *= 0.88 + tr.Work * 0.2;
    }

    /// <summary>
    /// Choose a pocket of space near the slot: open from opponents, a clear lane from the ball,
    /// some forward progress, not on top of a teammate, and not too far from where the shape
    /// wants him. Re-read every half second or so, staggered so the team never moves in lockstep.
    /// </summary>
    void SeekSpace(Player p, double ax, double az)
    {
        var tr = Traits[p.Id];
        double dir = m.Teams[p.Team].Dir;
        var ball = m.Ball.Pos;
        double line = offside[p.Team];
        double roam = 3 + tr.Creativity * 7;
        double best = -1e9;
        double bdx = 0;
        double bdz = 0;
        for (int i = 0; i <= 8; i++)
        {
            double ang = (i / 8.0) * Math.PI * 2 + p.Id;
            double r = i == 8 ? 0 : roam * (0.55 + 0.45 * Hash01(p.Id + i, M.ToInt32(m.Time)));
            // Candidate offsets in team frame, sticking near the previous choice.
            double ox = i == 8 ? seekDX[p.Id] : JsMath.Cos(ang) * r;
            double oz = i == 8 ? seekDZ[p.Id] : JsMath.Sin(ang) * r;
            double cx = ax + ox * dir;
            double cz = az + oz * dir;
            if (Math.Abs(cz) > Pitch.HalfW - 1.5 || Math.Abs(cx) > Pitch.HalfL - 3) continue;
            double open = 99;
            foreach (var q in m.Teams[1 - p.Team].Players) open = Math.Min(open, M.Dist2D(q.Pos.X, q.Pos.Z, cx, cz));
            double crowd = 0;
            foreach (var q in m.Teams[p.Team].Players)
            {
                if (q == p) continue;
                double d = M.Dist2D(q.Pos.X, q.Pos.Z, cx, cz);
                if (d < 9) crowd += (9 - d) / 9;
            }
            double lane = M.Clamp(LaneClearance(ball.X, ball.Z, cx, cz, p.Team, true), -2, 3);
            double fromBall = M.Dist2D(ball.X, ball.Z, cx, cz);
            double range = fromBall < 7 ? (7 - fromBall) * 0.3 : fromBall > 30 ? (fromBall - 30) * 0.1 : 0;
            double prog = cx * dir;
            double s = Math.Min(open, 9) * 0.35 + lane * 0.45 + ox * (0.04 + tr.Creativity * 0.1) - crowd * 0.6 - range;
            s -= JsMath.Hypot(ox, oz) * (0.04 + tr.Discipline * 0.12);
            if (prog > line - 0.8) s -= (prog - line + 0.8) * 1.5;
            if (i == 8) s += 0.4; // hysteresis
            if (s > best)
            {
                best = s;
                bdx = ox;
                bdz = oz;
            }
        }
        seekDX[p.Id] = bdx;
        seekDZ[p.Id] = bdz;
        seekAt[p.Id] = m.Time + 0.45 + Hash01(p.Id, m.Time * 3) * 0.5 + (1 - tr.React) * 0.3;
    }

    readonly HashSet<Player> taken = new HashSet<Player>();

    /// <summary>
    /// Zonal marking: each free defender picks up the most dangerous opponent inside his zone
    /// (around his slot), greedily by cost, each attacker taken once. The carrier is the
    /// chasers' job, so he's left out.
    /// </summary>
    void AssignMarks(int team)
    {
        double dir = m.Teams[team].Dir;
        var carrier = m.Owner ?? m.HeldBy;
        var mine = m.Teams[team].Players;
        var theirs = m.Teams[1 - team].Players;
        foreach (var p in mine) mark[p.Id] = null;
        taken.Clear();
        for (int round = 0; round < mine.Count; round++)
        {
            double best = 1e9;
            Player? bp = null;
            Player? bq = null;
            foreach (var p in mine)
            {
                if (p.Role == Role.GK || mark[p.Id] != null || p == Chaser[team]) continue;
                var a = Slot(p, tmp2);
                double zone = 9 + (1 - Traits[p.Id].Discipline) * 6 + (p.Role == Role.DEF ? 2 : 0);
                foreach (var q in theirs)
                {
                    if (q.Role == Role.GK || q == carrier || taken.Contains(q)) continue;
                    double d = M.Dist2D(a.X, a.Z, q.Pos.X, q.Pos.Z);
                    if (d > zone) continue;
                    // Danger: near our goal and central.
                    double toGoal = q.Pos.X * -dir + Pitch.HalfL;
                    double danger = M.Clamp(1 - toGoal / 60, 0, 1) * (1 - Math.Abs(q.Pos.Z) / (Pitch.HalfW * 1.6));
                    double cost = d - danger * 8;
                    if (cost < best)
                    {
                        best = cost;
                        bp = p;
                        bq = q;
                    }
                }
            }
            if (bp == null || bq == null) break;
            mark[bp.Id] = bq;
            taken.Add(bq);
        }
        markAt[team] = m.Time + 0.3;
    }

    /// <summary>Box positions while the ball is in a crossing area (false = keep normal shape).</summary>
    bool BoxSpot(Player p, Player carrier, bool attacking, out double x, out double z)
    {
        double attDir = m.Teams[carrier.Team].Dir;
        double gx = Pitch.HalfL * attDir; // the goal being attacked
        double nearSide = JsMath.Sign(JsMath.Or1(carrier.Pos.Z));
        double jitter = JsMath.Sin(p.Id * 7.3 + m.Time * 0.4) * 0.8;
        x = z = 0;
        if (attacking)
        {
            switch (p.Index)
            {
                case 9: x = gx - attDir * 8.5; z = nearSide * 1.5 + jitter; return true;
                case 8:
                case 10:
                    if (JsMath.Sign(p.BaseZ * attDir) == nearSide) { x = gx - attDir * 5.5; z = nearSide * 3 + jitter; }
                    else { x = gx - attDir * 7; z = -nearSide * 4 + jitter; }
                    return true;
                case 6:
                case 7: x = gx - attDir * 15; z = (p.Index == 6 ? -1 : 1) * 5 + jitter; return true;
                default: return false;
            }
        }
        // Defending the cross: centre-backs on the six-yard line, full-backs tuck in.
        switch (p.Index)
        {
            case 2: x = gx - attDir * 6; z = -2.2 + nearSide * 0.8; return true;
            case 3: x = gx - attDir * 6; z = 2.2 + nearSide * 0.8; return true;
            case 1:
            case 4:
                if (JsMath.Sign(p.BaseZ * -attDir) == nearSide) return false;
                x = gx - attDir * 8; z = -nearSide * 5; return true;
            case 5: x = gx - attDir * 13; z = nearSide * 1.5; return true;
            default: return false;
        }
    }

    bool IsSecondPresser(Player p)
    {
        if (spAt[p.Team] == m.Time) return spBest[p.Team] == p;
        var ch = Chaser[p.Team];
        Player? best = null;
        double bd = 1e9;
        foreach (var q in m.Teams[p.Team].Players)
        {
            if (q == ch || q.Role == Role.GK) continue;
            double d = m.BallDist(q);
            if (d < bd)
            {
                bd = d;
                best = q;
            }
        }
        spAt[p.Team] = m.Time;
        spBest[p.Team] = bd < 16 ? best : null;
        return best == p && bd < 16;
    }

    /// <summary>Steering with arrival: sprint when far / urgent, ease in near the target.</summary>
    public void MoveTo(Player p, double x, double z, bool urgent, bool faceBall)
    {
        double dx = x - p.Pos.X;
        double dz = z - p.Pos.Z;
        double d = JsMath.Hypot(dx, dz);
        if (d < 0.25)
        {
            p.MoveX = 0;
            p.MoveZ = 0;
            p.WantSpeed = 0;
        }
        else
        {
            p.MoveX = dx / d;
            p.MoveZ = dz / d;
            double cruise = urgent ? p.TopSpeed : d > 12 ? PlayerK.JogSpeed + 1.2 : PlayerK.JogSpeed * 0.85;
            p.WantSpeed = Math.Min(cruise, d * (urgent ? 3 : 1.3) + 0.3);
            p.Sprinting = p.WantSpeed > PlayerK.JogSpeed + 0.5;
        }
        if (faceBall || d < 4)
        {
            p.LookTarget.Copy(m.Ball.Pos);
            p.LookAt = p.LookTarget;
        }
    }

    /// <summary>Where a player should stand given the team shape and the ball.</summary>
    public V3 Slot(Player p, V3 output)
    {
        var team = m.Teams[p.Team];
        double dir = team.Dir;
        double bx = m.Ball.Pos.X * dir;
        double bz = m.Ball.Pos.Z * dir;
        bool attacking = m.PossTeam == p.Team;
        double x;
        double z;
        if (attacking)
        {
            x = p.BaseX * Pitch.HalfL * 0.62 + bx * 0.45 + 10;
            z = p.BaseZ * Pitch.HalfW * 0.88 + bz * 0.2;
            if (p.Role == Role.FWD) x = Math.Min(x + 4, offside[p.Team] - 0.8);
            else x = Math.Min(x, offside[p.Team] - 0.8);
        }
        else
        {
            x = p.BaseX * Pitch.HalfL * 0.5 + bx * 0.5 - 4;
            z = p.BaseZ * Pitch.HalfW * 0.62 + bz * 0.35;
            // Don't defend higher than the ball.
            if (p.Role == Role.DEF) x = Math.Min(x, bx - 4);
        }
        // The mood of the side (chasing it late, or seeing it out) and the squeeze.
        double shift = ShapeShift(p, attacking);
        if (shift != 0)
        {
            x += shift;
            if (attacking) x = Math.Min(x, offside[p.Team] - 0.8);
            else if (p.Role == Role.DEF) x = Math.Min(x, bx - 4 + Math.Max(0, shift) * 0.4);
        }
        x = M.Clamp(x, -Pitch.HalfL + 4, Pitch.HalfL - 6);
        z = M.Clamp(z, -Pitch.HalfW + 1.5, Pitch.HalfW - 1.5);
        // Small personal offset keeps lines from looking robotic.
        x += JsMath.Sin(p.Id * 12.9 + m.Time * 0.13) * 1.2;
        return output.Set(x * dir, 0, z * dir);
    }

    /// <summary>
    /// Where a presser goes on the carrier: goal-side of the ball, `keep` metres off it, and
    /// where it will be a moment from now. Except when the carrier's touch has put the ball
    /// nearer the presser than him: then it's there to be won and he goes straight onto it
    /// (returns true).
    /// </summary>
    public bool PressPoint(Player p, Player carrier, V3 output, double keep = 1.3)
    {
        var b = m.Ball;
        double bx = b.Pos.X + b.Vel.X * 0.25;
        double bz = b.Pos.Z + b.Vel.Z * 0.25;
        // Both of them a moment on too: a carrier running onto his own touch isn't exposed.
        double mine = M.Dist2D(p.Pos.X + p.Vel.X * 0.25, p.Pos.Z + p.Vel.Z * 0.25, bx, bz);
        double his = M.Dist2D(carrier.Pos.X + carrier.Vel.X * 0.25, carrier.Pos.Z + carrier.Vel.Z * 0.25, bx, bz);
        if (b.Pos.Y < 0.7 && mine < 2 && mine < his - 0.2)
        {
            output.Set(bx, 0, bz);
            return true;
        }
        ContainAt(p, bx, bz, output, keep);
        return false;
    }

    /// <summary>Goal-side point from which to contain the ball carrier.</summary>
    public V3 ContainTarget(Player p, V3 output, double keep = 1.3)
    {
        var b = m.Ball.Pos;
        ContainAt(p, b.X, b.Z, output, keep);
        return output;
    }

    void ContainAt(Player p, double bx, double bz, V3 output, double keep)
    {
        double gx = -m.Teams[p.Team].Dir * Pitch.HalfL;
        double dx = gx - bx;
        double dz = -bz * 0.5;
        double d = Math.Max(0.1, JsMath.Hypot(dx, dz));
        output.Set(bx + (dx / d) * keep, 0, bz + (dz / d) * keep);
    }

    void Press(Player p, Player carrier)
    {
        bool onBall = PressPoint(p, carrier, tmp);
        double d = m.BallDist(p);
        MoveTo(p, tmp.X, tmp.Z, d > 4 || onBall, true);
        // Closing in: square to him, ready to jockey; a loose touch is pounced on.
        p.SquareUp = d < 6 && !onBall;
        p.Burst = onBall;
        if (d < 5 && !onBall) p.WantSpeed = Math.Min(p.WantSpeed, carrier.Speed + 1.5 + d);
        // Tackle when close and the ball is exposed.
        if (d < 1.5 && !p.IsBusy && m.Time > tackleReady[p.Id])
        {
            tackleReady[p.Id] = m.Time + 0.9 + m.Rng.Next() * 0.8;
            // He goes in when a foot can get to it; with it tucked away, only now and then.
            bool open = m.BallOpen(p, carrier, d);
            if (m.Rng.Next() < (open ? 0.4 + p.Attrs.Defending * 0.3 : 0.1 + p.Attrs.Defending * 0.1))
            {
                double dx = m.Ball.Pos.X - p.Pos.X;
                double dz = m.Ball.Pos.Z - p.Pos.Z;
                double dd = Math.Max(0.01, JsMath.Hypot(dx, dz));
                m.StartTackle(p, dx / dd, dz / dd, false);
            }
        }
    }

    // ------------------------------------------------------------------ ball carrier

    static KickPlan Plan(KickType type, double dirX, double dirZ, double power, int targetId, double expires, bool? aimed = null) =>
        new KickPlan { Type = type, DirX = dirX, DirZ = dirZ, Power = power, TargetId = targetId, Expires = expires, Aimed = aimed };

    void CarrierThink(Player p)
    {
        var team = m.Teams[p.Team];
        double dir = team.Dir;
        double gx = Pitch.HalfL * dir;
        var b = m.Ball.Pos;
        if (m.Time >= nextDecision[p.Id] && p.SinceTouch > 0.05 && p.Plan == null)
        {
            nextDecision[p.Id] = m.Time + 0.22 + m.Rng.Next() * 0.15;
            double distGoal = M.Dist2D(b.X, b.Z, gx, 0);
            double pressure = m.NearestOpponentDist(p);
            double held = m.Time - possStart;
            // Keepers with the ball at their feet: move it on quickly.
            if (p.Role == Role.GK)
            {
                var fwd = m.ByJob(p.Team, pressure < 6 ? 9 : 2 + (int)Math.Floor(m.Rng.Next() * 2));
                double dx = fwd.Pos.X - b.X;
                double dz = fwd.Pos.Z - b.Z;
                double d = JsMath.Hypot(dx, dz);
                // (Alone in a training drill: just clear it upfield.)
                if (fwd == p || d < 0.5) p.Plan = Plan(KickType.Lob, dir, 0, 0.7, -1, m.Time + 1);
                else p.Plan = Plan(pressure < 6 ? KickType.Lob : KickType.Pass, dx / d, dz / d, 0, fwd.Id, m.Time + 1);
                Dribble(p, true);
                return;
            }
            Mood(p.Team, out double chase, out double protect);
            bool counter = Countering(p.Team);
            // Take a touch or two before deciding, unless someone is right on us (or it's a break: go).
            if (held < patience * (1 + protect) && pressure > 2.2 && distGoal > 20 && !counter)
            {
                ChooseDribble(p, pressure);
                Dribble(p, false);
                return;
            }
            double clear = LaneClearance(b.X, b.Z, gx, 0, p.Team, true);

            // Shoot?
            double shootP = 0;
            if (distGoal < 16) shootP = 0.8;
            else if (distGoal < 25 && clear > 0.4) shootP = 0.5;
            else if (distGoal < 30 && clear > 1.5 && p.Attrs.Shooting > 0.75) shootP = 0.15;
            // Chasing it late: have a go from further out.
            if (chase > 0 && distGoal < 32 && clear > 0.3) shootP = Math.Max(shootP, chase * (distGoal < 25 ? 0.45 : 0.2));
            double angle = Math.Abs(JsMath.Atan2(b.Z, Math.Abs(gx - b.X)));
            if (angle > 1.1) shootP *= 0.2;
            if (m.Rng.Next() < shootP)
            {
                double pw = M.Clamp(0.55 + distGoal / 40 + m.Rng.Gauss() * 0.12, 0.35, 1.0);
                // He picks a corner (AimZ carries the side) and shapes up toward it.
                double side = m.Rng.Next() < 0.5 ? -1 : 1;
                double sx = gx - b.X, sz = side * (Pitch.GoalHalfWidth - 0.55) - b.Z;
                double sn = Math.Max(0.1, JsMath.Hypot(sx, sz));
                p.Plan = Plan(KickType.Shot, sx / sn, sz / sn, pw, -1, m.Time + 1);
                p.Plan.AimZ = side;
                Dribble(p, true);
                return;
            }

            // Cross? Wide near the byline with someone attacking the box.
            if (m.InCrossZone(p.Team, b.X, b.Z) && Math.Abs(b.Z) > 11 && distGoal > 10)
            {
                int inBox = 0;
                foreach (var q in team.Players)
                {
                    if (q == p || q.Role == Role.GK) continue;
                    if ((gx - q.Pos.X) * dir < 18 && Math.Abs(q.Pos.Z) < 18) inBox++;
                }
                double crossP = inBox >= 2 ? 0.45 : inBox == 1 ? 0.25 : 0.04;
                if (m.Rng.Next() < crossP)
                {
                    double dx = gx - b.X;
                    double dz = -b.Z;
                    double d = Math.Max(0.1, JsMath.Hypot(dx, dz));
                    p.Plan = Plan(KickType.Cross, dx / d, dz / d, 0, -1, m.Time + 1.2, false);
                    Dribble(p, true);
                    return;
                }
            }

            // Pass?
            Player? best = null;
            double bestScore = -1e9;
            bool bestThrough = false;
            foreach (var q in team.Players)
            {
                if (q == p) continue;
                double s = PassScore(p, q, false);
                if (s > bestScore)
                {
                    bestScore = s;
                    best = q;
                }
            }
            // A ball into space for a runner, when there's one he'll get to first. (Looked for
            // twice a second or so: the runs don't change faster than that.)
            ThroughPlan? tp = null;
            if (m.Time >= throughLook[p.Id])
            {
                throughLook[p.Id] = m.Time + 0.45;
                tp = PlanThrough(p, false, null);
            }
            // On the break, and for the man the move was made for, the ball in behind comes first;
            // seeing a game out, it's kept safe.
            double tBias = ThroughBias + (counter ? 0.3 : 0) - protect * 0.3 + (tp != null && IsPlayRunner(tp.Receiver) ? 0.3 : 0);
            if (tp != null && tp.Score + tBias > bestScore)
            {
                bestScore = tp.Score + tBias;
                best = tp.Receiver;
                bestThrough = true;
            }
            double dribbleValue = DribbleValue(p);
            double needPass = pressure < 2.2 ? 0.45 : pressure < 4 ? 0.15 : 0;
            if (best != null && bestScore + needPass > dribbleValue + 0.55 + 0.3 * m.Rng.Next())
            {
                double dx = best.Pos.X - b.X;
                double dz = best.Pos.Z - b.Z;
                double d = JsMath.Hypot(dx, dz);
                bool lob = !bestThrough && d > 26 && LaneClearance(b.X, b.Z, best.Pos.X, best.Pos.Z, p.Team, false) < 1.5;
                p.Plan = Plan(bestThrough ? KickType.Through : lob ? KickType.Lob : KickType.Pass, dx / d, dz / d, 0, best.Id, m.Time + 1);
                p.LookTarget.Set(best.Pos.X, 0, best.Pos.Z);
                Dribble(p, true);
                return;
            }

            ChooseDribble(p, pressure);
        }
        Dribble(p, false);
    }

    void ChooseDribble(Player p, double pressure)
    {
        double dir = m.Teams[p.Team].Dir;
        double gx = Pitch.HalfL * dir;
        var b = m.Ball.Pos;
        // Dribble direction: toward goal, away from pressure, away from the touchline.
        double dx = (gx - b.X) / Math.Max(1, Math.Abs(gx - b.X));
        double dz = (-b.Z / Pitch.HalfW) * 0.5;
        foreach (var q in m.Teams[1 - p.Team].Players)
        {
            double ox = p.Pos.X - q.Pos.X;
            double oz = p.Pos.Z - q.Pos.Z;
            double od = JsMath.Hypot(ox, oz);
            if (od < 7 && od > 0.01)
            {
                double w = JsMath.Pow((7 - od) / 7, 2) * 1.6;
                dx += (ox / od) * w;
                dz += (oz / od) * w;
            }
        }
        if (Math.Abs(b.Z) > Pitch.HalfW - 4) dz -= JsMath.Sign(b.Z) * 0.8;
        // A team-mate overlapping outside him: cut in, and take his man with him.
        if (overlapCarrier[p.Team] == p && playUntil[p.Team] > m.Time) dz -= JsMath.Sign(b.Z) * 0.7;
        // Never dribble backwards into our own goal area.
        if (dx * dir < -0.3) dx = -0.3 * dir;
        double n = Math.Max(0.01, JsMath.Hypot(dx, dz));
        dribX[p.Id] = dx / n;
        dribZ[p.Id] = dz / n;
        // Drive into space; on the break, run at them.
        dribSprint[p.Id] = (pressure > 5 || (Countering(p.Team) && pressure > 2.5)) && DribbleValue(p) > (Countering(p.Team) ? 0.25 : 0.45);
    }

    void Dribble(Player p, bool settle)
    {
        var b = m.Ball.Pos;
        double mx = dribX[p.Id];
        double mz = dribZ[p.Id];
        if (settle && p.Plan != null)
        {
            // Shape up for the strike: slow down and turn toward the target.
            mx = p.Plan.DirX;
            mz = p.Plan.DirZ;
        }
        p.TouchX = mx;
        p.TouchZ = mz;
        // Stay with the ball: steer toward it when it's ahead of us.
        double tbx = b.X + m.Ball.Vel.X * 0.15 - p.Pos.X;
        double tbz = b.Z + m.Ball.Vel.Z * 0.15 - p.Pos.Z;
        double td = JsMath.Hypot(tbx, tbz);
        if (td > 0.45)
        {
            mx = mx * 0.3 + (tbx / td) * 0.7;
            mz = mz * 0.3 + (tbz / td) * 0.7;
            double n = JsMath.Hypot(mx, mz);
            mx /= n;
            mz /= n;
        }
        p.MoveX = mx;
        p.MoveZ = mz;
        bool sprint = dribSprint[p.Id] && !settle;
        p.Sprinting = sprint;
        p.WantSpeed = settle ? 3 : sprint ? p.TopSpeed : PlayerK.JogSpeed * 0.95;
    }

    double DribbleValue(Player p)
    {
        double dir = m.Teams[p.Team].Dir;
        double space = 12;
        foreach (var q in m.Teams[1 - p.Team].Players)
        {
            double dx = (q.Pos.X - p.Pos.X) * dir;
            double dz = q.Pos.Z - p.Pos.Z;
            if (dx < -1) continue;
            double d = JsMath.Hypot(dx, dz);
            if (Math.Abs(dz) < dx * 1.2 + 2) space = Math.Min(space, d);
        }
        return M.Clamp(space / 10, 0, 1) * 0.9;
    }

    /// <summary>Minimum clearance (m) of opponents from the lane, scaled by how much time they have.</summary>
    public double LaneClearance(double ax, double az, double bx, double bz, int team, bool ignoreKeeper)
    {
        double lx = bx - ax;
        double lz = bz - az;
        double len = Math.Max(0.1, JsMath.Hypot(lx, lz));
        double ux = lx / len;
        double uz = lz / len;
        double minC = 99;
        foreach (var q in m.Teams[1 - team].Players)
        {
            if (ignoreKeeper && q.Role == Role.GK) continue;
            double qx = q.Pos.X - ax;
            double qz = q.Pos.Z - az;
            double along = qx * ux + qz * uz;
            if (along < 0 || along > len + 1) continue;
            double perp = Math.Abs(qx * uz - qz * ux);
            double reach = 0.9 + (along / 13) * 4;
            minC = Math.Min(minC, perp - reach);
        }
        return minC;
    }

    double PassScore(Player p, Player q, bool through)
    {
        double dir = m.Teams[p.Team].Dir;
        double tx = q.Pos.X;
        double tz = q.Pos.Z;
        if (through)
        {
            tx += dir * 8;
            if (tx * dir > offside[p.Team] + 6) return -9;
            if (Math.Abs(tx) > Pitch.HalfL - 3) return -9;
        }
        var b = m.Ball.Pos;
        double d = M.Dist2D(b.X, b.Z, tx, tz);
        if (d < 5 || d > 45) return -9;
        if (q.Role == Role.GK && d > 20) return -9;
        if (!through && q.Pos.X * dir > offside[p.Team] + 0.3) return -9; // offside
        double lane = LaneClearance(b.X, b.Z, tx, tz, p.Team, false);
        if (lane < -0.3) return -9;
        if (!through && PassMargin(p, q) < 0.15) return -9;
        double open = 99;
        foreach (var o in m.Teams[1 - p.Team].Players) open = Math.Min(open, M.Dist2D(o.Pos.X, o.Pos.Z, tx, tz));
        double progress = ((tx - b.X) * dir) / 25;
        double goalDist = M.Dist2D(tx, tz, Pitch.HalfL * dir, 0);
        double threat = M.Clamp(1 - goalDist / 40, 0, 1);
        // Switching it: the ball side is crowded and he's free across on the far side.
        double sw = 0;
        if (Math.Abs(tz - b.Z) > 22 && open > 7)
        {
            int crowd = 0;
            foreach (var o in m.Teams[1 - p.Team].Players) if (M.Dist2D(o.Pos.X, o.Pos.Z, b.X, b.Z) < 15) crowd++;
            if (crowd >= 4) sw = 0.35 + (crowd - 4) * 0.1;
        }
        if (Countering(p.Team)) progress *= 1.5;
        return sw + progress * 0.8 + M.Clamp(lane / 3, 0, 1) * 0.7 + M.Clamp(open / 7, 0, 1) * 0.5 + threat * 0.6 - d / 70;
    }

    /// <summary>
    /// A ball played to a team-mate's feet, judged like a through ball: the path a ground pass of
    /// the usual weight takes, and how much sooner he's on it than the first of the other side
    /// (seconds; negative: they'd cut it out).
    /// </summary>
    public double PassMargin(Player p, Player q)
    {
        var b = m.Ball.Pos;
        double tx = q.Pos.X + q.Vel.X * 0.5;
        double tz = q.Pos.Z + q.Vel.Z * 0.5;
        double D = M.Dist2D(b.X, b.Z, tx, tz);
        if (D < 1) return 9;
        double kx = (tx - b.X) / D;
        double kz = (tz - b.Z) / D;
        double v0 = Kick.RollPaceFor(D, M.Clamp(5.5 + D * 0.14, 6, 11));
        int n = 0;
        for (; n < Samples; n++)
        {
            Kick.RollAt(v0, n * SampleDT, out double rd, out double rv);
            pathX[n] = b.X + kx * rd;
            pathZ[n] = b.Z + kz * rd;
            pathY[n] = 0;
            if (rd > D + 2 || rv < 0.3) break;
        }
        n = Math.Min(n + 1, Samples);
        int mine = Arrival(q, pathX, pathY, pathZ, n, PlanReactRun);
        if (mine < 0) return -9;
        double tOpp = 9;
        foreach (var o in m.Teams[1 - p.Team].Players)
        {
            int i = Arrival(o, pathX, pathY, pathZ, n, PlanReactOpp);
            if (i >= 0) tOpp = Math.Min(tOpp, i * SampleDT);
        }
        return tOpp - mine * SampleDT;
    }

    /// <summary>Best option overall (used when the stick is idle).</summary>
    public Player? BestReceiver(Player p, bool through)
    {
        Player? best = null;
        double bs = -1e9;
        foreach (var q in m.Teams[p.Team].Players)
        {
            if (q == p) continue;
            double s = PassScore(p, q, through);
            if (s > bs)
            {
                bs = s;
                best = q;
            }
        }
        if (bs < -5)
        {
            // Everything looks risky: nearest teammate.
            double bd = 1e9;
            foreach (var q in m.Teams[p.Team].Players)
            {
                if (q == p || q.Role == Role.GK) continue;
                double d = m.BallDist(q);
                if (d < bd)
                {
                    bd = d;
                    best = q;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Receiver for a human pass: the teammate best aligned with the stick. `cone` (cosine) is how
    /// far off the stick he may be. `power` (the charge, 0..1): past half, the harder it's charged
    /// the further on he looks.
    /// </summary>
    public Player? PickReceiver(Player p, double dirX, double dirZ, bool through, double cone = 0.35, double? power = null)
    {
        Player? best = null;
        double bestS = -1e9;
        foreach (var q in m.Teams[p.Team].Players)
        {
            if (q == p) continue;
            double dx = q.Pos.X - p.Pos.X;
            double dz = q.Pos.Z - p.Pos.Z;
            double d = JsMath.Hypot(dx, dz);
            if (d < 2 || d > 50) continue;
            double align = (dx * dirX + dz * dirZ) / d;
            if (align < cone) continue;
            double open = 99;
            foreach (var o in m.Teams[1 - p.Team].Players) open = Math.Min(open, M.Dist2D(o.Pos.X, o.Pos.Z, q.Pos.X, q.Pos.Z));
            double s = align * 3 + M.Clamp(open / 6, 0, 1) * 0.6 - d / 35;
            // Your pass, as in the big football games: the hold says how far. A tap looks for the
            // nearer man, a full charge for the one further on.
            if (power != null) s += (M.Clamp(power.Value, 0, 1) - 0.5) * 2 * Math.Min(d, 40) / 20;
            // Of the ones the stick points at, one he can actually get it to.
            if (!through && (align > 0.7 || power != null) && PassMargin(p, q) < 0.1) s -= 1.2;
            if (through && q.Role == Role.FWD) s += 0.3;
            if (q.Role == Role.GK) s -= 0.8;
            if (s > bestS)
            {
                bestS = s;
                best = q;
            }
        }
        return best;
    }

    /// <summary>
    /// Who your pass is for, mobile-style: the direction first, then the distance. Everyone is judged
    /// where he'll be a moment from now. He has to be within 50° of the stick (70° of the way you're
    /// facing with the stick idle); then each 12° off it, each 12 m from the distance the hold asks
    /// for (a tap ~6 m, a full hold ~40 m), a defender who can cut the lane (1.5, up to 7.5 the
    /// sooner he'd be there) and the keeper (2, unless it's a back pass) all count against him.
    /// The lowest wins.
    /// </summary>
    public Player? HumanReceiver(Player p, double dirX, double dirZ, bool aimed, double hold)
    {
        var b = m.Ball.Pos;
        double dir = m.Teams[p.Team].Dir;
        double cone = JsMath.Cos((aimed ? 50 : 70) * Math.PI / 180);
        double want = 6 + 34 * M.Clamp(hold, 0, 1);
        Player? best = null;
        double bestC = 1e9;
        foreach (var q in m.Teams[p.Team].Players)
        {
            if (q == p) continue;
            double dx = q.Pos.X + q.Vel.X * 0.4 - b.X;
            double dz = q.Pos.Z + q.Vel.Z * 0.4 - b.Z;
            double d = JsMath.Hypot(dx, dz);
            if (d < 2 || d > 60) continue;
            double align = (dx * dirX + dz * dirZ) / d;
            if (align < cone) continue;
            double off = Math.Abs(JsMath.Atan2(dx * dirZ - dz * dirX, dx * dirX + dz * dirZ)) * 180 / Math.PI;
            double c = off / 12 + Math.Abs(d - want) / 12;
            // A defender who'd get there first: the further first, the worse.
            double margin = PassMargin(p, q);
            if (margin < 0.1) c += 1.5 + M.Clamp(0.1 - margin, 0, 2) * 3;
            if (q.Role == Role.GK && dirX * dir > -0.7) c += 2;
            if (c < bestC)
            {
                bestC = c;
                best = q;
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ set pieces

    void SetPieceThink(Player p)
    {
        var sp = m.SetPiece;
        if (sp == null)
        {
            p.WantSpeed = 0;
            return;
        }
        var team = m.Teams[p.Team];
        double dir = team.Dir;
        if (sp.Taker == p)
        {
            // Stand behind the ball facing into play (a run-up for shots from a dead ball).
            var f = m.SetPieceFacing(sp);
            var spot = m.SetPieceSpot(sp);
            bool runUp = spot.runUp;
            if (runUp && p.Plan != null)
            {
                // The run-up: a couple of short, accelerating steps, then a long last stride that
                // plants the standing foot beside the ball (on the far side from the kicking foot,
                // a touch behind it) so the swing comes through the ball.
                double rightX = -f.z, rightZ = f.x;
                double lat = 0.3 * p.Foot;
                double px = sp.X - f.x * 0.32 - rightX * lat;
                double pz = sp.Z - f.z * 0.32 - rightZ * lat;
                double dd = M.Dist2D(p.Pos.X, p.Pos.Z, px, pz);
                MoveTo(p, px, pz, false, false);
                double full = sp.Kind == SetPieceKind.Penalty ? 4.6 : 5.4;
                // Building speed over the run, easing only in the last metre to set the plant foot.
                double gone = M.Dist2D(p.Pos.X, p.Pos.Z, spot.x, spot.z);
                p.WantSpeed = Math.Min(Math.Min(full, 1.8 + gone * 1.5), 2.6 + dd * 3);
                p.LookTarget.Set(sp.X, 0, sp.Z);
                p.LookAt = dd > 0.8 ? null : p.LookTarget;
                return;
            }
            // Run-ups are taken from a little to the side, like real takers.
            double sx = spot.x;
            double sz = spot.z;
            double d = M.Dist2D(p.Pos.X, p.Pos.Z, sx, sz);
            if (d > 0.3)
            {
                MoveTo(p, sx, sz, d > 3, false);
                sp.T = Math.Min(sp.T, 0.3); // the clock starts once the taker is there
                return;
            }
            p.MoveX = 0;
            p.MoveZ = 0;
            p.WantSpeed = 0;
            // Lined up: body square to the ball, eyes on it (a penalty taker looks at the keeper).
            p.Facing = runUp ? JsMath.Atan2(sp.Z - p.Pos.Z, sp.X - p.Pos.X) : JsMath.Atan2(f.z, f.x);
            p.LookTarget.Set(sp.X + f.x * 20, 0, sp.Z + f.z * 20);
            p.LookAt = runUp ? null : p.LookTarget;
            if (sp.Kind == SetPieceKind.Throw && m.HeldBy != p) m.CatchBall(p);
            bool human = m.HumanSide(p.Team) && !m.AutoPlay;
            // (In a real match, the other side's goal kick takes a moment longer: the camera drops
            // in behind the keeper to watch it.)
            double wait = human ? (runUp || sp.Kind == SetPieceKind.Corner || sp.Kind == SetPieceKind.GoalKick ? 20 : 7) : runUp ? 2.6 : sp.Kind == SetPieceKind.FreeKick ? 1.8 : m.AutoPlay ? 1.3 : 2.6;
            if (sp.T > wait && p.Plan == null) PlanSetPiece(p);
            return;
        }

        // Everyone else gets into position.
        double x;
        double z;
        if (sp.Kind == SetPieceKind.Kickoff)
        {
            p.WantSpeed = 0;
            p.LookTarget.Copy(m.Ball.Pos);
            p.LookAt = p.LookTarget;
            return;
        }
        // Penalty: everyone was placed outside the box; just stand and watch.
        if (sp.Kind == SetPieceKind.Penalty)
        {
            p.MoveX = 0;
            p.MoveZ = 0;
            p.WantSpeed = 0;
            p.LookTarget.Copy(m.Ball.Pos);
            p.LookAt = p.LookTarget;
            return;
        }
        // The wall holds its line, eyes on the ball.
        int wi = sp.Wall != null ? sp.Wall.Players.IndexOf(p) : -1;
        if (wi >= 0)
        {
            var ws = sp.Wall!.Slots[wi];
            MoveTo(p, ws.X, ws.Z, false, true);
            p.SquareUp = true;
            return;
        }
        double spGoal = Pitch.HalfL * m.Teams[sp.Team].Dir; // goal being attacked by the restart
        bool boxBall = sp.Kind == SetPieceKind.Corner || (sp.Kind == SetPieceKind.FreeKick && !sp.Direct && Math.Abs(sp.X - spGoal) < 40);
        if (boxBall && p.Role != Role.GK)
        {
            // Ball into the box: attackers take their runs, defenders pick them up.
            bool attackers = p.Team == sp.Team;
            double sideDir = -JsMath.Sign(spGoal); // into the pitch
            int idx = Array.IndexOf(BoxOrder, p.Index);
            if (attackers && idx >= 0 && idx < 5)
            {
                x = spGoal + sideDir * Math.Abs(BoxSpots[idx, 0]);
                z = BoxSpots[idx, 1];
            }
            else if (!attackers && idx >= 0 && idx < 7)
            {
                int si = idx % BoxSpots.GetLength(0);
                x = spGoal + sideDir * (Math.Abs(BoxSpots[si, 0]) - 0.8);
                z = BoxSpots[si, 1] * 0.9;
            }
            else
            {
                Slot(p, tmp);
                x = tmp.X;
                z = tmp.Z;
            }
        }
        else if (sp.Direct && p.Role != Role.GK)
        {
            // Shot on: a dummy runner beside the ball, two lurking for rebounds, defenders on the edge of the box.
            double sideDir = -JsMath.Sign(spGoal);
            if (p.Team == sp.Team && p.Index == 8)
            {
                x = sp.X - m.Teams[sp.Team].Dir * 1.2;
                z = sp.Z + (sp.Z > 0 ? -1.4 : 1.4);
            }
            else if (p.Team == sp.Team && (p.Index == 9 || p.Index == 10))
            {
                x = spGoal + sideDir * 13;
                z = (p.Index == 9 ? -1 : 1) * 5;
            }
            else if (p.Team != sp.Team && (p.Role == Role.DEF || p.Index == 5))
            {
                x = spGoal + sideDir * 11.5;
                z = ((p.Index % 4) - 1.5) * 4;
            }
            else
            {
                Slot(p, tmp);
                x = tmp.X;
                z = tmp.Z;
            }
        }
        else if (p.Role == Role.GK)
        {
            x = -dir * (Pitch.HalfL - 1);
            z = 0;
            if (sp.Kind == SetPieceKind.GoalKick && sp.Team == p.Team)
            {
                x = sp.X;
                z = sp.Z;
            }
            else if (sp.Direct && sp.Team != p.Team)
            {
                // Free kick: the wall has the near post; the keeper covers the far side.
                x = -dir * (Pitch.HalfL - 0.6);
                z = -JsMath.Or1(JsMath.Sign(sp.Z)) * Pitch.GoalHalfWidth * 0.3;
            }
        }
        else
        {
            Slot(p, tmp);
            x = tmp.X;
            z = tmp.Z;
        }
        // Opponents keep their distance (a step clear of the line the match holds them behind).
        var zn = m.GetRestartZone(p);
        double cx = zn != null ? zn.X : sp.X;
        double cz = zn != null ? zn.Z : sp.Z;
        double minD = zn != null ? zn.R + 0.15 : p.Team != sp.Team ? (sp.Kind == SetPieceKind.Throw ? 3 : 9.3) : 0;
        double ddx = x - cx;
        double ddz = z - cz;
        double dist = JsMath.Hypot(ddx, ddz);
        if (dist < minD)
        {
            double k = minD / Math.Max(0.1, dist);
            x = cx + ddx * k;
            z = cz + ddz * k;
        }
        MoveTo(p, x, z, false, true);
    }

    readonly List<Player> cands = new List<Player>();

    Player? PickRandom(List<Player> list)
    {
        int i = (int)Math.Floor(m.Rng.Next() * list.Count);
        return i < list.Count ? list[i] : null;
    }

    void PlanSetPiece(Player p)
    {
        var sp = m.SetPiece!;
        var team = m.Teams[p.Team];
        Player? target = null;
        var type = KickType.Pass;
        double goalX = Pitch.HalfL * team.Dir;
        if (sp.Kind == SetPieceKind.Penalty || (sp.Direct && m.Rng.Next() < 0.55 + p.Attrs.Shooting * 0.35))
        {
            // Pick a side and a height; now and then straight down the middle.
            double r = m.Rng.Next();
            double side = sp.Kind == SetPieceKind.Penalty && r < 0.08 ? 0 : m.Rng.Next() < 0.5 ? -1 : 1;
            double power = sp.Kind == SetPieceKind.Penalty ? 0.35 + m.Rng.Next() * 0.6 : 0.6 + m.Rng.Next() * 0.35;
            // DirZ only carries the side (|DirZ| > 0.3 picks a post).
            double ax = JsMath.Sign(goalX - p.Pos.X) * 0.45;
            double az = side * 0.9;
            double n = JsMath.Hypot(ax, az);
            p.Plan = Plan(KickType.Shot, ax / n, az / n, power, -1, m.Time + 3, true);
            return;
        }
        if (sp.Kind == SetPieceKind.Kickoff)
        {
            target = m.ByJob(p.Team, 7);
        }
        else if (sp.Kind == SetPieceKind.FreeKick)
        {
            // Into the box when it's close enough to deliver, otherwise keep the ball.
            if (Math.Abs(sp.X - goalX) < 40)
            {
                type = KickType.Cross;
                cands.Clear();
                foreach (var q in team.Players) if (q.Index == 2 || q.Index == 3 || q.Index == 9 || q.Index == 10) cands.Add(q);
                target = PickRandom(cands);
            }
            else
            {
                target = BestReceiver(p, false);
            }
        }
        else if (sp.Kind == SetPieceKind.Corner)
        {
            type = KickType.Cross;
            cands.Clear();
            foreach (var q in team.Players) if (q.Index == 2 || q.Index == 3 || q.Index == 9) cands.Add(q);
            target = PickRandom(cands);
        }
        else if (sp.Kind == SetPieceKind.GoalKick)
        {
            bool shortKick = m.Rng.Next() < 0.4;
            cands.Clear();
            foreach (var q in team.Players)
                if (shortKick ? q.Role == Role.DEF : q.Role == Role.MID || q.Role == Role.FWD) cands.Add(q);
            target = PickRandom(cands);
            type = shortKick ? KickType.Pass : KickType.Lob;
        }
        else
        {
            // Throw: nearest teammate with some space.
            double bd = 1e9;
            foreach (var q in team.Players)
            {
                if (q == p || q.Role == Role.GK) continue;
                double d = m.BallDist(q);
                if (d < bd && d > 3)
                {
                    bd = d;
                    target = q;
                }
            }
            type = KickType.Lob;
        }
        if (target == null) return;
        double dx = target.Pos.X - p.Pos.X;
        double dz = target.Pos.Z - p.Pos.Z;
        double dd = Math.Max(0.1, JsMath.Hypot(dx, dz));
        p.Plan = Plan(type, dx / dd, dz / dd, 0, target.Id, m.Time + 2);
    }

    // ------------------------------------------------------------------ celebrations

    void Celebrate(Player p)
    {
        var s = m.Scorer;
        if (s == null)
        {
            p.WantSpeed = 0;
            return;
        }
        // After the cut: jog back into the kick-off shape.
        if (m.PhaseT > GoalSeq.Cut)
        {
            m.KickoffSpot(p, 1 - s.Team, tmp);
            MoveTo(p, tmp.X, tmp.Z, false, true);
            p.WantSpeed = Math.Min(p.WantSpeed, PlayerK.JogSpeed * 0.8);
            return;
        }
        // The celebration is shot from in front of the scorer, between him and the centre spot:
        // he pulls up and turns to it, and his team-mates pile in from behind and the sides.
        bool front = m.PhaseT > GoalSeq.Front;
        double d = JsMath.Or1(JsMath.Hypot(s.Pos.X, s.Pos.Z));
        double fx = -s.Pos.X / d;
        double fz = -s.Pos.Z / d;
        var cel = m.Celebration;
        if (p == s && cel != null && m.PhaseT >= cel.At)
        {
            CelebrationMove(p, m.PhaseT - cel.At);
            return;
        }
        // Team-mates give a flip or a leap room before they pile in.
        double room = cel != null && (cel.Kind == CelebrationKind.Flip || cel.Kind == CelebrationKind.Siu) && m.PhaseT < cel.At + 2.2 ? 1.6 : 0;
        if (p == s && m.Steer.On)
        {
            // Your stick has him: flat out wherever it points, pulling up short of the lines.
            double x = m.Steer.X, z = m.Steer.Z;
            if (Math.Abs(p.Pos.X) > Pitch.HalfL - 1.5 && x * p.Pos.X > 0) x = 0;
            if (Math.Abs(p.Pos.Z) > Pitch.HalfW - 1.5 && z * p.Pos.Z > 0) z = 0;
            double k = JsMath.Hypot(x, z);
            bool any = k != 0 && !double.IsNaN(k);
            p.MoveX = any ? x / k : 0;
            p.MoveZ = any ? z / k : 0;
            p.WantSpeed = any ? p.TopSpeed : 0;
            p.Sprinting = true;
            p.LookAt = null;
            return;
        }
        if (p == s)
        {
            if (front)
            {
                p.MoveX = p.MoveZ = 0;
                p.WantSpeed = 0;
                p.Sprinting = false;
                p.LookTarget.Set(0, 0, 0);
                p.LookAt = p.LookTarget;
                return;
            }
            double cx = JsMath.Sign(JsMath.Or1(p.Pos.X)) * (Pitch.HalfL - 6);
            double cz = JsMath.Sign(JsMath.Or1(p.Pos.Z)) * (Pitch.HalfW - 2);
            MoveTo(p, cx, cz, m.PhaseT < 2.2, false);
            p.Sprinting = true;
        }
        else if (p.Team == s.Team && p.Role != Role.GK)
        {
            // Chase him down, then fan out behind him: alternate sides, staggered.
            double k = (p.Index % 2 != 0 ? 1 : -1) * (1.3 + (p.Index % 4) * 0.45);
            double ahead = front ? -1.1 - (p.Index % 3) * 0.5 - room : 1.5 + room;
            MoveTo(p, s.Pos.X + fx * ahead - fz * k, s.Pos.Z + fz * ahead + fx * k, front, false);
            if (front && JsMath.Hypot(p.Pos.X - s.Pos.X, p.Pos.Z - s.Pos.Z) < 3.5)
            {
                p.LookTarget.Copy(s.Pos);
                p.LookAt = p.LookTarget;
            }
        }
        else
        {
            p.WantSpeed = Math.Max(0, p.WantSpeed - DT * 4);
        }
    }

    /// <summary>
    /// The scorer's chosen celebration, `u` seconds in. The sim moves him; the renderer poses him
    /// from the same clock. All of them end facing the camera, which stands between him and the
    /// centre spot.
    /// </summary>
    void CelebrationMove(Player p, double u)
    {
        var cel = m.Celebration!;
        double toCam = JsMath.Atan2(cel.Dz, cel.Dx);
        void Face(double a)
        {
            p.LookTarget.Set(p.Pos.X + JsMath.Cos(a) * 30, 0, p.Pos.Z + JsMath.Sin(a) * 30);
            p.LookAt = p.LookTarget;
            p.SquareUp = true;
        }
        void RunAt(double a, double speed)
        {
            p.MoveX = JsMath.Cos(a);
            p.MoveZ = JsMath.Sin(a);
            p.WantSpeed = speed;
        }
        p.Sprinting = false;
        p.LookAt = null;
        p.SquareUp = false;
        switch (cel.Kind)
        {
            case CelebrationKind.Slide:
                // Charge at the camera, drop onto both knees and skid toward it.
                p.Sprinting = u < 0.75;
                RunAt(toCam, u < 0.75 ? 7.8 : Math.Max(0, 7.4 - 4.6 * (u - 0.75)));
                if (u > 0.5) Face(toCam);
                break;
            case CelebrationKind.Plane:
            {
                // Arms out, banking round a wide loop; it comes out of the turn gliding at the camera.
                const double w = 1.3;
                double left = 3.0 - Math.Min(u, 3.0);
                RunAt(toCam - cel.Turn * w * left, u < 3.0 ? 6 : Math.Max(0, 6 - 7 * (u - 3.0)));
                if (u > 3.0) Face(toCam);
                break;
            }
            case CelebrationKind.Siu:
                // Away from the camera a few strides, a leap with a half turn in the air, and the
                // landing, feet planted wide, facing it.
                if (u < 0.45) RunAt(toCam + Math.PI, 4.5);
                else
                {
                    RunAt(toCam, 0);
                    if (Math.Abs(M.AngleDiff(p.Facing, toCam)) > 0.3) p.Facing = p.PrevFacing = toCam;
                    Face(toCam);
                }
                break;
            case CelebrationKind.Flip:
                // Pull up, turn to the camera, a standing backflip.
                RunAt(toCam, 0);
                Face(toCam);
                break;
        }
    }
}
