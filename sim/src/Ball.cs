using System;

namespace GameNight.Sim;

public sealed class BallEvents
{
    /// <summary>Impact speed of the latest ground bounce.</summary>
    public double Bounce;
    /// <summary>Impact speed of the latest woodwork hit.</summary>
    public double Post;
    /// <summary>Impact speed into the net, and where.</summary>
    public double Net, NetX, NetY, NetZ;
}

public sealed class Ball
{
    const double R = BallK.Radius;
    const double Mass = BallK.Mass;
    const double I = BallK.InertiaFactor * Mass * R * R;
    // Effective mass for changing the contact-point velocity with a tangential impulse on a
    // sphere: 1/m + r^2/I.
    const double ContactK = 1 / Mass + (R * R) / I;

    public readonly V3 Pos = new V3(0, R, 0);
    public readonly V3 Vel = new V3();
    /// <summary>Angular velocity, rad/s.</summary>
    public readonly V3 Spin = new V3();
    public readonly V3 PrevPos = new V3(0, R, 0);
    public bool OnGround = true;
    /// <summary>True while the ball is inside a goal frame (behind the line).</summary>
    public bool InGoal;
    public readonly BallEvents Events = new BallEvents();

    readonly V3 tmp = new V3();
    // Spin decay factors in the air and on the grass, for the last dt (it's only ever DT or 2 DT).
    double eDt = -1, eSpin, eGrass;

    public void Reset(double x, double z)
    {
        Pos.Set(x, R, z);
        PrevPos.Copy(Pos);
        Vel.Set(0, 0, 0);
        Spin.Set(0, 0, 0);
        OnGround = true;
        InGoal = false;
    }

    /// <summary>Strike the ball. Velocity in m/s, spin in rad/s.</summary>
    public void Kick(double vx, double vy, double vz, double sx, double sy, double sz)
    {
        Vel.Set(vx, vy, vz);
        Spin.Set(sx, sy, sz);
        if (vy > 0.05) OnGround = false;
    }

    public void Step(double dt)
    {
        PrevPos.Copy(Pos);
        var v = Vel;
        var w = Spin;
        // On the grass means on it: a real lift takes it off; a sliver of upward speed is nothing.
        if (OnGround && v.Y != 0)
        {
            if (v.Y > 0.05) OnGround = false;
            else v.Y = 0;
        }
        double speed = v.Len();

        // --- Aerodynamics
        if (speed > 0.01)
        {
            // Drag crisis: Cd drops sharply once the boundary layer turns turbulent.
            double t = M.Smoothstep(BallK.CrisisSpeed - BallK.CrisisWidth, BallK.CrisisSpeed + BallK.CrisisWidth, speed);
            double cd = BallK.CdLow + (BallK.CdHigh - BallK.CdLow) * t;
            double kDrag = (0.5 * Physics.AirDensity * cd * BallK.Area * speed) / Mass;
            v.X -= v.X * kDrag * dt;
            v.Y -= v.Y * kDrag * dt;
            v.Z -= v.Z * kDrag * dt;

            // Magnus: F = 1/2 rho A Cl v^2 (w x v)/(|w||v|)
            double wl = w.Len();
            if (wl > 0.5)
            {
                double sRatio = (R * wl) / speed;
                double cl = M.Clamp(1.25 * sRatio, 0, 0.33);
                double k = (0.5 * Physics.AirDensity * BallK.Area * cl * speed) / (Mass * wl);
                double cx = w.Y * v.Z - w.Z * v.Y;
                double cy = w.Z * v.X - w.X * v.Z;
                double cz = w.X * v.Y - w.Y * v.X;
                v.X += cx * k * dt;
                // Magnus lift on a rolling ball would make it hop; only the vertical part in the air.
                if (!OnGround) v.Y += cy * k * dt;
                v.Z += cz * k * dt;
            }
        }

        if (!OnGround)
        {
            v.Y -= Physics.Gravity * dt;
            DecayFor(dt);
            w.Scale(eSpin);
        }

        // Near the goal frame, move in small sub-steps so a fast ball can't tunnel through a
        // 12 cm post or the net between two frames.
        bool nearGoal = Math.Abs(Pos.X) > Pitch.HalfL - 2 && Math.Abs(Pos.X) < Pitch.HalfL + 3 && Math.Abs(Pos.Z) < Pitch.GoalHalfWidth + 2;
        if (nearGoal)
        {
            double n = Math.Min(8, Math.Max(1, Math.Ceiling((speed * dt) / 0.04)));
            for (int i = 0; i < n; i++)
            {
                Pos.AddScaled(v, dt / n);
                CollideGoals();
            }
        }
        else
        {
            Pos.AddScaled(v, dt);
        }

        // --- Ground
        if (Pos.Y <= R)
        {
            Pos.Y = R;
            double vy = v.Y;
            if (vy < -0.45)
            {
                // Bounce. Restitution softens for gentle impacts (grass absorbs them).
                double impact = -vy;
                double e = BallK.Restitution * M.Smoothstep(0.45, 2.5, impact) * (1 - 0.12 * M.Smoothstep(8, 20, impact));
                v.Y = impact * e;
                Events.Bounce = impact;
                OnGround = v.Y < 0.45;
                if (OnGround) v.Y = 0;
                // Tangential friction impulse limited by Coulomb friction.
                double jn = Mass * (1 + e) * impact;
                ApplyContactFriction(BallK.GroundFriction * jn);
            }
            else
            {
                v.Y = 0;
                OnGround = true;
            }
        }

        if (OnGround && Pos.Y <= R + 1e-4)
        {
            // Sliding / rolling on grass.
            double cvx = v.X + R * w.Z;
            double cvz = v.Z - R * w.X;
            double slip = Math.Sqrt(cvx * cvx + cvz * cvz);
            if (slip > 0.08)
            {
                ApplyContactFriction(BallK.GroundFriction * 0.75 * Mass * Physics.Gravity * dt);
            }
            else
            {
                // Pure rolling: lock spin to velocity and apply rolling resistance.
                double hs = Math.Sqrt(v.X * v.X + v.Z * v.Z);
                if (hs > 0)
                {
                    double dec = BallK.RollDecel * dt;
                    double ns = Math.Max(0, hs - dec);
                    double f = ns / hs;
                    v.X *= f;
                    v.Z *= f;
                }
                w.Z = -v.X / R;
                w.X = v.Z / R;
            }
            // Grass kills vertical-axis spin quickly.
            DecayFor(dt);
            w.Y *= eGrass;
            if (Math.Abs(v.X) + Math.Abs(v.Z) < 0.02)
            {
                v.X = 0;
                v.Z = 0;
            }
        }

        CollideGoals();
        CollideSurrounds();
    }

    void DecayFor(double dt)
    {
        if (dt == eDt) return;
        eDt = dt;
        eSpin = JsMath.Exp(-dt / BallK.SpinDecay);
        eGrass = JsMath.Exp(-dt / 0.6);
    }

    /// <summary>One wall (along x or z at ±limit, `height` tall): bounce the ball back off it with restitution e.</summary>
    void Hit(bool axisX, double limit, double height, double e)
    {
        var p = Pos;
        var v = Vel;
        double c = axisX ? p.X : p.Z;
        double vc = axisX ? v.X : v.Z;
        if (Math.Abs(c) > limit - R && p.Y < height + R && JsMath.Sign(vc) == JsMath.Sign(c))
        {
            double back = JsMath.Sign(c) * (limit - R);
            if (axisX)
            {
                p.X = back;
                v.X = -v.X * e;
                v.Z *= 0.75;
            }
            else
            {
                p.Z = back;
                v.Z = -v.Z * e;
                v.X *= 0.75;
            }
            v.Y *= 0.7;
            Spin.Scale(0.4);
            Events.Bounce = Math.Max(Events.Bounce, Math.Abs(vc) * 0.5);
        }
    }

    /// <summary>Ad boards round the pitch and the front walls of the stands, so a ball that goes
    /// out thuds into them and drops instead of flying away.</summary>
    void CollideSurrounds()
    {
        var p = Pos;
        // Boards along the touchlines and beside the goals (open behind the goal mouth).
        Hit(false, Pitch.HalfW + 3.8, 0.9, 0.35);
        if (Math.Abs(p.Z) > Pitch.GoalHalfWidth + 3.5) Hit(true, Pitch.HalfL + 4.5, 0.9, 0.35);
        // Stand walls behind them.
        Hit(false, Pitch.HalfW + 7.5, 1.6, 0.3);
        Hit(true, Pitch.HalfL + 8.5, 1.4, 0.3);
    }

    /// <summary>Friction impulse at the contact point, capped by maxImpulse (N·s).</summary>
    void ApplyContactFriction(double maxImpulse)
    {
        var v = Vel;
        var w = Spin;
        double cvx = v.X + R * w.Z;
        double cvz = v.Z - R * w.X;
        double slip = Math.Sqrt(cvx * cvx + cvz * cvz);
        if (slip < 1e-6) return;
        double needed = slip / ContactK;
        double j = Math.Min(needed, maxImpulse);
        double jx = (-cvx / slip) * j;
        double jz = (-cvz / slip) * j;
        v.X += jx / Mass;
        v.Z += jz / Mass;
        // torque = r x J with r = (0,-R,0): (-R*jz, 0, R*jx)
        w.X += (-R * jz) / I;
        w.Z += (R * jx) / I;
    }

    static double NetTop(double uu)
    {
        const double H = Pitch.GoalHeight, D = Pitch.GoalDepth, RF = Pitch.GoalRoofDepth;
        return uu <= RF ? H : uu >= D ? 0 : H * (1 - (uu - RF) / (D - RF));
    }

    void CollideGoals()
    {
        var p = Pos;
        double ax = Math.Abs(p.X);
        if (ax < Pitch.HalfL - 1.5)
        {
            InGoal = false;
            return;
        }
        double side = p.X > 0 ? 1 : -1;
        double lineX = side * Pitch.HalfL;
        const double hw = Pitch.GoalHalfWidth;
        const double H = Pitch.GoalHeight;
        const double pr = Pitch.PostRadius;
        // Posts (vertical) and crossbar (horizontal), all sitting on the goal line.
        CollideSegment(lineX, 0, -hw, lineX, H, -hw, pr);
        CollideSegment(lineX, 0, hw, lineX, H, hw, pr);
        CollideSegment(lineX, H, -hw, lineX, H, hw, pr);

        // The net, in goal-local coordinates: u = depth behind the line, z across, y up.
        const double D = Pitch.GoalDepth;
        const double RF = Pitch.GoalRoofDepth;
        double u = ax - Pitch.HalfL;
        double prevU = Math.Abs(PrevPos.X) - Pitch.HalfL;
        double az = Math.Abs(p.Z);
        // Back slope plane through (RF, H) and (D, 0); outward normal in (u, y).
        double nl = JsMath.Hypot(H, D - RF);
        double nu = H / nl;
        double ny = (D - RF) / nl;
        double backDist = ((u - RF) * H + (p.Y - H) * (D - RF)) / nl; // >0 outside

        if (!InGoal)
        {
            if (u > 0 && prevU <= 0.05 && az < hw && p.Y < H)
            {
                InGoal = true; // came in through the mouth
            }
            else if (u > -R && u < D + R && az < hw + R && p.Y < H + R)
            {
                // Outside the frame touching the net: side netting, roof or back.
                double penSide = az > hw - R && Math.Abs(PrevPos.Z) >= hw ? hw + R - az : 1e9;
                double penRoof = u > 0 && u <= RF && PrevPos.Y >= H ? H + R - p.Y : 1e9;
                double penBack = u > RF && backDist > -R && backDist < R ? R - backDist : 1e9;
                double pen = Math.Min(Math.Min(penSide, penRoof), penBack);
                if (pen < 1e8 && pen > 0)
                {
                    if (pen == penSide)
                    {
                        p.Z = JsMath.Sign(p.Z) * (hw + R);
                        NetHit(Math.Abs(Vel.Z));
                        Vel.Z *= -0.15;
                        Vel.X *= 0.5;
                        Vel.Y *= 0.7;
                    }
                    else if (pen == penRoof)
                    {
                        p.Y = H + R;
                        NetHit(Math.Abs(Vel.Y));
                        if (Vel.Y < 0) Vel.Y *= -0.2;
                        Vel.X *= 0.6;
                        Vel.Z *= 0.6;
                        OnGround = false;
                    }
                    else
                    {
                        p.X += side * nu * pen;
                        p.Y += ny * pen;
                        double vn = Vel.X * side * nu + Vel.Y * ny;
                        if (vn < 0)
                        {
                            NetHit(-vn);
                            Vel.X -= side * nu * vn * 1.2;
                            Vel.Y -= ny * vn * 1.2;
                        }
                        Vel.X *= 0.6;
                        Vel.Z *= 0.6;
                    }
                }
            }
        }
        if (InGoal)
        {
            // Inside: the net catches the ball and soaks up its energy.
            if (u > RF && backDist > -R)
            {
                double pen = backDist + R;
                p.X -= side * nu * pen;
                p.Y -= ny * pen;
                double vn = Vel.X * side * nu + Vel.Y * ny;
                if (vn > 0)
                {
                    NetHit(vn);
                    Vel.X -= side * nu * vn * 1.12;
                    Vel.Y -= ny * vn * 1.12;
                }
                Vel.Z *= 0.6;
            }
            if (u <= RF && p.Y > H - R)
            {
                p.Y = H - R;
                if (Vel.Y > 0)
                {
                    NetHit(Vel.Y);
                    Vel.Y *= -0.1;
                }
                Vel.X *= 0.7;
            }
            if (Math.Abs(p.X) - Pitch.HalfL > D - R)
            {
                p.X = side * (Pitch.HalfL + D - R);
                if (Vel.X * side > 0)
                {
                    NetHit(Math.Abs(Vel.X));
                    Vel.X *= -0.12;
                }
            }
            if (az > hw - R)
            {
                p.Z = JsMath.Sign(p.Z) * (hw - R);
                if (Vel.Z * JsMath.Sign(p.Z) > 0)
                {
                    NetHit(Math.Abs(Vel.Z));
                    Vel.Z *= -0.15;
                }
                Vel.X *= 0.7;
            }
            // Ball can't escape back through the front once it's in.
            if (Math.Abs(p.X) < Pitch.HalfL + R)
            {
                p.X = side * (Pitch.HalfL + R);
                if (Vel.X * side < 0) Vel.X *= -0.2;
            }
            if (p.Y > NetTop(Math.Abs(p.X) - Pitch.HalfL) - R + 0.001 && Math.Abs(p.X) - Pitch.HalfL > RF)
            {
                // Numerical safety: never leave the volume through the slope.
                p.Y = Math.Max(R, NetTop(Math.Abs(p.X) - Pitch.HalfL) - R);
            }
        }
    }

    void NetHit(double speed)
    {
        if (speed < 0.5) return;
        Events.Net = Math.Max(Events.Net, speed);
        Events.NetX = Pos.X;
        Events.NetY = Pos.Y;
        Events.NetZ = Pos.Z;
    }

    /// <summary>Sphere vs capsule (goal frame).</summary>
    void CollideSegment(double ax, double ay, double az, double bx, double by, double bz, double radius)
    {
        var p = Pos;
        double abx = bx - ax;
        double aby = by - ay;
        double abz = bz - az;
        double t = M.Clamp(((p.X - ax) * abx + (p.Y - ay) * aby + (p.Z - az) * abz) / (abx * abx + aby * aby + abz * abz), 0, 1);
        double cx = ax + abx * t;
        double cy = ay + aby * t;
        double cz = az + abz * t;
        var n = tmp.Set(p.X - cx, p.Y - cy, p.Z - cz);
        double d = n.Len();
        double minD = R + radius;
        if (d >= minD || d < 1e-6) return;
        n.Scale(1 / d);
        p.Set(cx + n.X * minD, cy + n.Y * minD, cz + n.Z * minD);
        double vn = Vel.Dot(n);
        if (vn < 0)
        {
            Vel.AddScaled(n, -(1 + BallK.PostRestitution) * vn);
            // Woodwork scrubs a bit of spin and speed.
            Vel.Scale(0.92);
            Spin.Scale(0.6);
            Events.Post = Math.Max(Events.Post, -vn);
            if (Vel.Y > 0.5) OnGround = false;
        }
    }
}
