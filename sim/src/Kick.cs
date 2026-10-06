using System;

namespace GameNight.Sim;

public sealed class KickResult
{
    public V3 Vel;
    public V3 Spin;
    /// <summary>Seconds until the ball reaches the target.</summary>
    public double Time;

    public KickResult(V3 vel, V3 spin, double time)
    {
        Vel = vel;
        Spin = spin;
        Time = time;
    }
}

/// <summary>A ground pass from the roll table: strike pace, time to get there, pace on arrival.</summary>
public struct RollPass
{
    public double V0, T, Arrive;
}

/// <summary>
/// Kick solver. Every pass and shot is solved against the real ball integrator (drag crisis,
/// Magnus, skid-to-roll), so what the AI/assist "intends" is exactly what the physics
/// produces. Errors are added afterwards to model the striker's skill.
/// </summary>
public static class Kick
{
    const double DT = Tick.DT;

    // A scratch ball per thread (the PWA has one; the native sim may run off the main thread
    // while the HUD previews a corner).
    [ThreadStatic] static Ball? scratchBall;
    static Ball Scratch => scratchBall ??= new Ball();

    static V3 MakeSpin(double fx, double fz, double top, double side, V3 output)
    {
        // right = forward x up = (-fz, 0, fx); topspin rotates about -right; curl-right is -y.
        return output.Set(fz * top, -side, -fx * top);
    }

    static Ball LoadScratch(V3 from, double vx, double vy, double vz, V3 spin)
    {
        var b = Scratch;
        b.Pos.Copy(from);
        b.PrevPos.Copy(from);
        b.Vel.Set(vx, vy, vz);
        b.Spin.Copy(spin);
        b.OnGround = vy <= 0.05 && from.Y <= 0.12;
        b.InGoal = false;
        b.Events.Bounce = 0;
        return b;
    }

    /// <summary>Speed the ball has when it has travelled `dist` metres along the ground (or -1 if it stops short).</summary>
    static double GroundArrival(V3 from, double fx, double fz, double v0, double rollFrac, double dist, out double outT, double floor = -1)
    {
        var spin = MakeSpin(fx, fz, (v0 / 0.11) * rollFrac, 0, new V3());
        var b = LoadScratch(from, fx * v0, 0, fz * v0, spin);
        double t = 0;
        double sx = from.X;
        double sz = from.Z;
        while (t < 8)
        {
            b.Step(DT);
            t += DT;
            double dx = b.Pos.X - sx;
            double dz = b.Pos.Z - sz;
            if (dx * fx + dz * fz >= dist)
            {
                outT = t;
                return Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z);
            }
            if (b.Vel.X == 0 && b.Vel.Z == 0) break;
            // Already slower than `floor`: it can only slow further, so it won't arrive at that pace.
            if (floor > 0 && b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z < floor * floor * 0.98) break;
        }
        outT = t;
        return -1;
    }

    /// <summary>
    /// How far past `dist` (along fx, fz) a ground pass struck at v0 is when it slows below
    /// `floor` m/s: negative if it gets there too slow. Once it is through `dist` still faster,
    /// the rest of the way is estimated instead of rolled out.
    /// </summary>
    static double GroundReach(V3 from, double fx, double fz, double v0, double rollFrac, double dist, double floor)
    {
        var spin = MakeSpin(fx, fz, (v0 / 0.11) * rollFrac, 0, new V3());
        var b = LoadScratch(from, fx * v0, 0, fz * v0, spin);
        double f2 = floor * floor;
        for (double t = 0; t < 8; t += DT)
        {
            b.Step(DT);
            double v2 = b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z;
            double along = (b.Pos.X - from.X) * fx + (b.Pos.Z - from.Z) * fz;
            if (v2 < f2) return along - dist;
            if (along >= dist) return along - dist + (v2 - f2) / (2 * (BallK.RollDecel + 0.015 * v2));
        }
        return (b.Pos.X - from.X) * fx + (b.Pos.Z - from.Z) * fz - dist;
    }

    /// <summary>
    /// For an increasing f: the smallest x in [min, max] with f(x) >= 0, to within tolX (or as
    /// soon as 0 &lt;= f(x) &lt; tolF). Starts from a guess (a, then b's distance from it), steps
    /// along the secant until the answer is bracketed, then closes in by regula falsi (Illinois).
    /// </summary>
    static double SmallestUp(Func<double, double> f, double min, double max, double a, double b, double tolX, double tolF)
    {
        double w0 = Math.Max(tolX, Math.Abs(b - a));
        double x0 = Math.Min(max, Math.Max(min, a));
        double f0 = f(x0);
        double x1, f1;
        if (f0 < 0)
        {
            if (x0 >= max) return max;
            x1 = Math.Min(max, x0 + w0);
            f1 = f(x1);
        }
        else
        {
            if (f0 < tolF || x0 <= min) return x0;
            x1 = x0;
            f1 = f0;
            x0 = Math.Max(min, x1 - w0);
            f0 = f(x0);
        }
        // Bracket it: from the end nearer the answer, along the secant (overshooting a little).
        while ((f0 >= 0) == (f1 >= 0))
        {
            bool up = f1 < 0;
            if (up ? x1 >= max : x0 <= min) return up ? max : min;
            if (!up && f0 < tolF) return x0;
            double w = x1 - x0;
            double sec = f1 > f0 ? ((up ? -f1 : -f0) * w) / (f1 - f0) : 0;
            if (up)
            {
                x0 = x1;
                f0 = f1;
                x1 = Math.Min(max, x1 + Math.Min(4 * w, Math.Max(w, sec * 1.15)));
                f1 = f(x1);
            }
            else
            {
                x1 = x0;
                f1 = f0;
                x0 = Math.Max(min, x0 - Math.Min(4 * w, Math.Max(w, -sec * 1.15)));
                f0 = f(x0);
            }
        }
        // f(lo) < 0 <= f(hi).
        double lo = x0, hi = x1, wlo = f0, whi = f1, fhi = f1;
        int side = 0, same = 0;
        for (int i = 0; i < 40 && fhi >= tolF && hi - lo > tolX; i++)
        {
            double r = wlo / (wlo - whi);
            double x = same >= 2 || !(r > 0 && r < 1) ? (lo + hi) * 0.5 : Math.Min(hi - tolX * 0.5, Math.Max(lo + tolX * 0.5, lo + (hi - lo) * r));
            double fx = f(x);
            int s = fx >= 0 ? 1 : -1;
            same = s == side ? same + 1 : 0;
            if (s > 0)
            {
                hi = x;
                fhi = whi = fx;
                if (side == 1) wlo *= 0.5;
            }
            else
            {
                lo = x;
                wlo = fx;
                if (side == -1) whi *= 0.5;
            }
            side = s;
        }
        return hi;
    }

    // ------------------------------------------------------------------ ground passes, tabulated
    //
    // Struck from the grass, a side-foot pass runs in a straight line with no lift and no swerve,
    // the same in every direction: how far it has gone and how fast it's going, step by step,
    // depends only on the strike speed. So each strike speed's whole run (every DT, from the real
    // integrator) is a table row, rows every PassDV m/s, and a pass in between is read off its two
    // neighbours.

    const double PassMin = 1;
    const double PassDV = 0.25;
    static readonly int PassRows = (int)JsMath.Round((31 - PassMin) / PassDV) + 1;
    static readonly int PassSteps = (int)JsMath.Round(8 / DT);
    /// <summary>Per row: distance and speed after each step, interleaved (it stays put once stopped).</summary>
    static float[][]? passTable;
    static readonly object tableLock = new object();

    /// <summary>Builds the ground-pass table (a few hundred thousand ball steps: do it while loading).</summary>
    public static void PrepareGroundPasses()
    {
        if (passTable != null) return;
        lock (tableLock)
        {
            if (passTable != null) return;
            var table = new float[PassRows][];
            var from = new V3(0, BallK.Radius, 0);
            for (int r = 0; r < PassRows; r++)
            {
                double v0 = PassMin + r * PassDV;
                var b = LoadScratch(from, v0, 0, 0, MakeSpin(1, 0, (v0 / 0.11) * 0.55, 0, new V3()));
                var row = new float[PassSteps * 2];
                for (int k = 0; k < PassSteps; k++)
                {
                    b.Step(DT);
                    row[k * 2] = (float)b.Pos.X;
                    row[k * 2 + 1] = (float)Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z);
                    if (row[k * 2 + 1] == 0)
                    {
                        // Stopped: there it stays.
                        for (int j = k + 1; j < PassSteps; j++) row[j * 2] = (float)b.Pos.X;
                        break;
                    }
                }
                table[r] = row;
            }
            passTable = table;
        }
    }

    /// <summary>One row's answer for a pass of `dist` that should still be doing `floor` m/s there.</summary>
    static void PassRowAt(float[] row, double dist, double floor, out double reach, out double t)
    {
        // First step at or past `dist` (distance only grows): its time.
        int lo = 0, hi = PassSteps;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (row[mid * 2] < dist) lo = mid + 1;
            else hi = mid;
        }
        t = (Math.Min(lo, PassSteps - 1) + 1) * DT;
        // How far past `dist` it is once it slows below `floor` (speed only falls).
        lo = 0;
        hi = PassSteps;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (row[mid * 2 + 1] >= floor) lo = mid + 1;
            else hi = mid;
        }
        reach = row[Math.Min(lo, PassSteps - 1) * 2] - dist;
    }

    /// <summary>The table read at strike speed v (between rows: linear).</summary>
    static void PassAt(double v, double dist, double floor, out double reach, out double t)
    {
        double x = (Math.Min(Math.Max(v, PassMin), 31) - PassMin) / PassDV;
        int i = (int)Math.Min(PassRows - 2, Math.Floor(x));
        double f = x - i;
        PassRowAt(passTable![i], dist, floor, out double ra, out double ta);
        PassRowAt(passTable[i + 1], dist, floor, out double rb, out double tb);
        reach = ra + (rb - ra) * f;
        t = ta + (tb - ta) * f;
    }

    /// <summary>Ground pass that arrives at the target with roughly `arriveSpeed` m/s.</summary>
    public static KickResult SolveGroundPass(V3 from, double tx, double tz, double arriveSpeed, double maxSpeed = 30)
    {
        double dx = tx - from.X;
        double dz = tz - from.Z;
        double dist = Math.Max(0.5, Math.Sqrt(dx * dx + dz * dz));
        dx /= dist;
        dz /= dist;
        const double rollFrac = 0.55; // side-foot pass: ball starts partly rolling (as in the roll table)
        // The table brackets it to a whole m/s: the softest row still going at that pace.
        double row = RollPaceFor(dist, arriveSpeed);
        double lo = row >= 20 ? 20 : row - 0.5;
        double hi = row >= 20 ? 22 : row;
        double best, bestT;
        // Off the grass (a first-time pass of a bouncing ball), or near a goal frame it could hit:
        // the real flights.
        const double near = Pitch.HalfL - 2.5;
        if (from.Y > BallK.Radius + 1e-4 || Math.Abs(from.X) > near || Math.Abs(tx) > near || maxSpeed > 31)
        {
            double fdx = dx, fdz = dz;
            best = SmallestUp(v => GroundReach(from, fdx, fdz, v, rollFrac, dist, arriveSpeed), 1, maxSpeed, lo, hi, 0.002, 0.05);
            GroundArrival(from, dx, dz, best, rollFrac, dist, out bestT);
        }
        else
        {
            PrepareGroundPasses();
            best = SmallestUp(v =>
            {
                PassAt(v, dist, arriveSpeed, out double reach, out _);
                return reach;
            }, 1, maxSpeed, lo, hi, 0.002, 0.05);
            PassAt(best, dist, arriveSpeed, out _, out bestT);
        }
        var spin = MakeSpin(dx, dz, (best / 0.11) * rollFrac, 0, new V3());
        return new KickResult(new V3(dx * best, 0, dz * best), spin, bestT);
    }

    /// <summary>Simulates a lofted ball and returns horizontal distance at first landing (y back at ground).</summary>
    static double LoftLanding(V3 from, double fx, double fz, double speed, double angle, double backspin, double side, out double outT, out double outX, out double outZ)
    {
        double c = JsMath.Cos(angle);
        var spin = MakeSpin(fx, fz, -backspin, side, new V3());
        var b = LoadScratch(from, fx * speed * c, speed * JsMath.Sin(angle), fz * speed * c, spin);
        b.OnGround = false;
        double t = 0;
        while (t < 6)
        {
            // Coming down onto the grass: the step that bounces it has already flipped vel.y.
            bool falling = b.Vel.Y <= 0;
            b.Step(DT);
            t += DT;
            if (falling && b.Pos.Y <= 0.115) break;
        }
        outT = t;
        outX = b.Pos.X;
        outZ = b.Pos.Z;
        double dx = b.Pos.X - from.X;
        double dz = b.Pos.Z - from.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Lofted ball (cross, long ball, lob) landing at the target.</summary>
    public static KickResult SolveLofted(V3 from, double tx, double tz, double angleDeg, double backspin = 30, double curl = 0)
    {
        double ax = tx;
        double az = tz;
        double angle = (angleDeg * Math.PI) / 180;
        double speed = 15;
        double fx = 1;
        double fz = 0;
        double lt, lx, lz;
        // Outer loop corrects direction for curl, inner search finds the speed.
        for (int pass = 0; pass < 3; pass++)
        {
            double dx = ax - from.X;
            double dz = az - from.Z;
            double d = Math.Max(0.5, Math.Sqrt(dx * dx + dz * dz));
            fx = dx / d;
            fz = dz / d;
            double wx = tx - from.X, wz = tz - from.Z;
            double want = Math.Sqrt(wx * wx + wz * wz);
            // First guess: the speed that carries it there in a vacuum; after a curl correction, the last speed.
            double g = pass == 0 ? Math.Sqrt((9.81 * Math.Max(1, want)) / Math.Max(0.2, JsMath.Sin(2 * angle))) : speed;
            double gfx = fx, gfz = fz;
            speed = SmallestUp(v => LoftLanding(from, gfx, gfz, v, angle, backspin, curl, out _, out _, out _) - want, 2, 38,
                g * (pass == 0 ? 1.25 : 1), g * (pass == 0 ? 1.3 : 1.01), 0.004, 0.15);
            if (curl == 0) break;
            LoftLanding(from, fx, fz, speed, angle, backspin, curl, out lt, out lx, out lz);
            ax += tx - lx;
            az += tz - lz;
        }
        LoftLanding(from, fx, fz, speed, angle, backspin, curl, out lt, out _, out _);
        double c = JsMath.Cos(angle);
        return new KickResult(
            new V3(fx * speed * c, speed * JsMath.Sin(angle), fz * speed * c),
            MakeSpin(fx, fz, -backspin, curl, new V3()),
            lt);
    }

    /// <summary>
    /// Struck shot: finds the launch direction that sends the ball through (tx, ty, tz) at the
    /// given speed with the given top/side spin. Iterates on the real flight.
    /// </summary>
    public static KickResult SolveShot(V3 from, double tx, double ty, double tz, double speed, double topspin, double curl)
    {
        double ax = tx;
        double az = tz;
        // Initial guess: compensate for gravity drop over the estimated flight time.
        double estT = JsMath.Hypot(tx - from.X, tz - from.Z) / (speed * 0.85);
        double ay = ty + 0.5 * 9.81 * estT * estT;
        var vel = new V3();
        var spin = new V3();
        double time = 0;
        for (int it = 0; it < 8; it++)
        {
            double dx = ax - from.X;
            double dy = ay - from.Y;
            double dz = az - from.Z;
            double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            vel = new V3((dx / d) * speed, (dy / d) * speed, (dz / d) * speed);
            double h = Math.Sqrt(dx * dx + dz * dz);
            MakeSpin(dx / h, dz / h, topspin, curl, spin);
            var b = LoadScratch(from, vel.X, vel.Y, vel.Z, spin);
            b.OnGround = false;
            // Fly until we pass the target's plane (perpendicular to the horizontal direction).
            double fx = (tx - from.X) / Math.Max(0.01, JsMath.Hypot(tx - from.X, tz - from.Z));
            double fz = (tz - from.Z) / Math.Max(0.01, JsMath.Hypot(tx - from.X, tz - from.Z));
            double targetAlong = (tx - from.X) * fx + (tz - from.Z) * fz;
            double t = 0;
            double px = from.X, py = from.Y, pz = from.Z;
            while (t < 4)
            {
                px = b.Pos.X;
                py = b.Pos.Y;
                pz = b.Pos.Z;
                b.Step(DT);
                t += DT;
                double along = (b.Pos.X - from.X) * fx + (b.Pos.Z - from.Z) * fz;
                if (along >= targetAlong)
                {
                    double prevAlong = (px - from.X) * fx + (pz - from.Z) * fz;
                    double k = (targetAlong - prevAlong) / Math.Max(1e-6, along - prevAlong);
                    px += (b.Pos.X - px) * k;
                    py += (b.Pos.Y - py) * k;
                    pz += (b.Pos.Z - pz) * k;
                    break;
                }
            }
            time = t;
            ax += tx - px;
            ay += ty - py;
            az += tz - pz;
        }
        return new KickResult(vel, spin.Clone(), time);
    }

    /// <summary>Height of a struck ball when it has travelled `dist` metres horizontally (-1 if it lands first).</summary>
    public static double HeightAlong(V3 from, V3 vel, V3 spin, double dist)
    {
        var b = LoadScratch(from, vel.X, vel.Y, vel.Z, spin);
        b.OnGround = false;
        double h = JsMath.Or1(JsMath.Hypot(vel.X, vel.Z));
        double fx = vel.X / h;
        double fz = vel.Z / h;
        for (double t = 0; t < 3; t += DT)
        {
            b.Step(DT);
            if ((b.Pos.X - from.X) * fx + (b.Pos.Z - from.Z) * fz >= dist) return b.Pos.Y;
            if (b.Vel.Y < 0 && b.Pos.Y <= 0.12) return -1;
        }
        return -1;
    }

    /// <summary>
    /// Dead-ball shot: the lowest target height (from `minY`, the aimed height, up) that still
    /// clears the wall at `wallDist`. Aimed high, it simply goes where it was aimed.
    /// </summary>
    public static KickResult SolveFreeKick(V3 from, double tx, double tz, double wallDist, double wallTop, double speed, double curl, double topspin, double minY = 1.15)
    {
        double y0 = Math.Min(2.9, Math.Max(0.3, minY));
        var r = SolveShot(from, tx, Math.Max(2.2, y0), tz, speed, topspin, curl);
        for (double ty = y0; ty <= 2.26; ty += 0.1)
        {
            var c = SolveShot(from, tx, ty, tz, speed, topspin, curl);
            if (HeightAlong(from, c.Vel, c.Spin, wallDist) >= wallTop)
            {
                r = c;
                break;
            }
        }
        return r;
    }

    /// <summary>
    /// The ball's real flight from `src`, for prediction: loads this thread's scratch ball. Step
    /// it with `Step(DT * 2)` (the PWA's prediction step) and read where it goes.
    /// </summary>
    public static Ball LoadPrediction(Ball src)
    {
        var b = Scratch;
        b.Pos.Copy(src.Pos);
        b.PrevPos.Copy(src.Pos);
        b.Vel.Copy(src.Vel);
        b.Spin.Copy(src.Spin);
        b.OnGround = src.OnGround;
        b.InGoal = src.InGoal;
        return b;
    }

    /// <summary>Runs `cb` along the predicted flight every 2 DT until it returns true (its time) or maxT (-1).</summary>
    public static double PredictBallAt(Ball src, double maxT, Func<Ball, double, bool> cb)
    {
        var b = LoadPrediction(src);
        double t = 0;
        while (t < maxT)
        {
            if (cb(b, t)) return t;
            b.Step(DT * 2);
            t += DT * 2;
        }
        return -1;
    }

    // ------------------------------------------------------------------ rolling-pass timing table
    //
    // Built once from the real integrator: for strike speeds 4..20 m/s, how far the ball has
    // rolled and how fast it is going at each moment. Lets the AI plan through balls with
    // exactly the timing the physics will produce.

    const double TableDT = 1.0 / 30;
    const double TableT = 6;

    sealed class RollRow
    {
        public double V0;
        public float[] D = Array.Empty<float>();
        public float[] V = Array.Empty<float>();
    }

    static RollRow[]? rollTable;

    static RollRow[] RollTable
    {
        get
        {
            if (rollTable != null) return rollTable;
            lock (tableLock)
            {
                return rollTable ??= BuildRollTable();
            }
        }
    }

    static RollRow[] BuildRollTable()
    {
        var output = new System.Collections.Generic.List<RollRow>();
        int n = (int)JsMath.Round(TableT / TableDT);
        int steps = (int)JsMath.Round(TableDT / DT);
        for (double v0 = 4; v0 <= 20; v0++)
        {
            var spin = MakeSpin(1, 0, (v0 / 0.11) * 0.55, 0, new V3());
            var b = LoadScratch(new V3(0, 0.11, 0), v0, 0, 0, spin);
            var d = new float[n];
            var v = new float[n];
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < steps; k++) b.Step(DT);
                d[i] = (float)b.Pos.X;
                v[i] = (float)JsMath.Hypot(b.Vel.X, b.Vel.Z);
            }
            output.Add(new RollRow { V0 = v0, D = d, V = v });
        }
        return output.ToArray();
    }

    /// <summary>First index where the distance rolled reaches x (a row only ever grows: binary search).</summary>
    static int FirstAtLeast(float[] d, double x)
    {
        int lo = 0, hi = d.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (d[mid] < x) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>
    /// Ground pass that covers `dist` metres in about `wantT` seconds (strike speed 4..maxV).
    /// False if it can't get there (too far to roll) at any allowed strike speed.
    /// </summary>
    public static bool RollingPass(double dist, double wantT, out RollPass best, double maxV = 19)
    {
        best = default;
        bool found = false;
        double bestErr = 1e9;
        foreach (var row in RollTable)
        {
            if (row.V0 > maxV) break;
            int i = FirstAtLeast(row.D, dist);
            if (i >= row.D.Length || row.V[i] < 0.8) continue;
            double t = (i + 1) * TableDT;
            double err = Math.Abs(t - wantT);
            if (err < bestErr)
            {
                bestErr = err;
                best = new RollPass { V0 = row.V0, T = t, Arrive = row.V[i] };
                found = true;
            }
        }
        return found;
    }

    static RollRow RowFor(double v0)
    {
        var table = RollTable;
        return table[(int)Math.Max(0, Math.Min(table.Length - 1, JsMath.Round(v0) - 4))];
    }

    /// <summary>Time for a ground pass struck at v0 (from the table) to roll `dist` metres, or -1.</summary>
    public static double RollTimeAt(double v0, double dist)
    {
        var row = RowFor(v0);
        int i = FirstAtLeast(row.D, dist);
        return i < row.D.Length ? (i + 1) * TableDT : -1;
    }

    /// <summary>A ground pass struck at `v0` m/s along the unit direction (dx, dz): side-foot, partly rolling.</summary>
    public static KickResult GroundKick(double dx, double dz, double v0) =>
        new KickResult(new V3(dx * v0, 0, dz * v0), MakeSpin(dx, dz, (v0 / 0.11) * 0.55, 0, new V3()), 0);

    /// <summary>
    /// Where a ground pass struck at `v0` (whole m/s, 4..20: a table row) is `t` seconds later:
    /// metres rolled and its speed then (it stays put once it has stopped).
    /// </summary>
    public static void RollAt(double v0, double t, out double d, out double v)
    {
        var row = RowFor(v0);
        double f = t / TableDT - 1;
        if (f <= 0)
        {
            double k0 = Math.Max(0, t / TableDT);
            d = row.D[0] * k0;
            v = row.V0 + (row.V[0] - row.V0) * k0;
            return;
        }
        int i = (int)Math.Min(row.D.Length - 2, Math.Floor(f));
        double k = Math.Min(1, f - i);
        // (Float32 table entries, worked in doubles as JS does.)
        double d0 = row.D[i], d1 = row.D[i + 1], v0r = row.V[i], v1r = row.V[i + 1];
        d = d0 + (d1 - d0) * k;
        v = v0r + (v1r - v0r) * k;
    }

    /// <summary>A lofted through ball coming down at (tx, tz): steeper the further it goes.</summary>
    public static KickResult ThroughLob(V3 from, double tx, double tz) =>
        SolveLofted(from, tx, tz, M.Clamp(22 + M.Dist2D(from.X, from.Z, tx, tz) * 0.3, 26, 40), 45, 0);

    static double[]? lobTimes;

    /// <summary>Flight time of a ThroughLob that comes down `dist` metres away (table every 2 m, 0..80).</summary>
    public static double ThroughLobTime(double dist)
    {
        var t = lobTimes;
        if (t == null)
        {
            lock (tableLock)
            {
                if (lobTimes == null)
                {
                    var a = new double[41];
                    for (int i = 0; i < a.Length; i++) a[i] = i == 0 ? 0 : ThroughLob(new V3(0, 0.11, 0), i * 2, 0).Time;
                    lobTimes = a;
                }
                t = lobTimes;
            }
        }
        double f = M.Clamp(dist / 2, 0, t.Length - 1.001);
        int k = (int)f;
        return t[k] + (t[k + 1] - t[k]) * (f - k);
    }

    /// <summary>The softest ground pass (table row, m/s) still going at `arrive` m/s when it has rolled `dist` metres (20 if none).</summary>
    public static double RollPaceFor(double dist, double arrive)
    {
        foreach (var row in RollTable)
        {
            int i = FirstAtLeast(row.D, dist);
            if (i < row.D.Length && row.V[i] >= arrive) return row.V0;
        }
        return 20;
    }
}
