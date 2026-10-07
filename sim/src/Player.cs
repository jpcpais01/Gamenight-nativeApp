using System;

namespace GameNight.Sim;

public enum Role { GK, DEF, MID, FWD }

public enum ActionKind { None, Kick, Tackle, Slide, Dive, Stumble, Fall, Header, Throw, Catch, Celebrate, Stretch, Punch, Trick, Parry }

public enum KickType { Pass, Lob, Through, Shot, Clear, Cross }

public sealed class Attributes
{
    /// <summary>0..1: top speed.</summary>
    public double Pace;
    /// <summary>First steps.</summary>
    public double Accel;
    /// <summary>Turning / cutting grip.</summary>
    public double Agility;
    /// <summary>Sprint endurance.</summary>
    public double Stamina;
    /// <summary>Duels, shielding.</summary>
    public double Strength;
    /// <summary>Aerial reach.</summary>
    public double Jumping;
    /// <summary>Kick power.</summary>
    public double Power;
    public double Control;
    public double Passing;
    public double Shooting;
    public double Defending;
    public double Keeping;
    /// <summary>Body: metres.</summary>
    public double Height;
    /// <summary>Body: kilograms.</summary>
    public double Weight;
    /// <summary>Skill moves, 1..5 stars (0 = not set: the match works it out from his touch).</summary>
    public int Skill;

    public Attributes Clone() => (Attributes)MemberwiseClone();
}

/// <summary>How a player looks (the renderer's business; Height also scales the keeper's reach).</summary>
public sealed class Look
{
    public int Skin;
    public int Hair;
    public int HairStyle;
    public double Height;
    public double Build;
}

/// <summary>A queued strike. Optional fields are null when the PWA leaves them undefined.</summary>
public sealed class KickPlan
{
    public KickType Type;
    /// <summary>For human kicks, aim direction in world XZ (unit).</summary>
    public double DirX, DirZ;
    /// <summary>False when the stick was idle: the game picks the best option instead of a direction. Null: the computer's own kick.</summary>
    public bool? Aimed;
    /// <summary>0..1: shot power, or pass weight.</summary>
    public double Power;
    /// <summary>Lofted (chipped / clipped) instead of along the ground.</summary>
    public bool? Lofted;
    /// <summary>Dead-ball shots aimed on the goal mouth: across (world z) and height (m).</summary>
    public double? AimZ, AimY;
    /// <summary>Corner delivery aimed at a landing spot on the pitch; floated (high) or whipped.</summary>
    public double? LandX, LandZ;
    public bool? Float;
    /// <summary>Receiver, -1 for none.</summary>
    public int TargetId = -1;
    /// <summary>Sim time.</summary>
    public double Expires;

    public KickPlan Clone() => (KickPlan)MemberwiseClone();
}

public sealed class Player
{
    /// <summary>How hard the grass brakes a slide (m/s²): from a sprint, about 4 m on the floor.</summary>
    public const double SlideDecel = 8.5;
    /// <summary>How quickly a runner closes the gap to the line (1/s) and the pace (1/s) he wants.</summary>
    const double SteerK = 14, PaceK = 10;

    // Smoothing factors for the last dt (always DT in a match).
    static double expDt = -1, exp8, exp10;

    public readonly V3 Pos = new V3();
    public readonly V3 Vel = new V3();
    public readonly V3 PrevPos = new V3();
    /// <summary>Yaw; 0 = +x.</summary>
    public double Facing;
    public double PrevFacing;

    // ---- intent, written by AI / human each tick
    public double MoveX, MoveZ, WantSpeed;
    /// <summary>Where the carrier wants his next touch to go (stick / AI intent), separate from the run line.</summary>
    public double TouchX = 1, TouchZ;
    /// <summary>If set, the body turns toward this point instead of the run direction (jockey, receive).</summary>
    public V3? LookAt;
    /// <summary>Keep the body square to LookAt even on the move (jockeying, keepers, the wall).
    /// Otherwise LookAt is where he's watching: the body follows his run and only opens toward
    /// it as he slows.</summary>
    public bool SquareUp;
    /// <summary>Going for a loose ball that's right there: the last couple of strides are explosive.</summary>
    public bool Burst;
    public readonly V3 LookTarget = new V3();

    // ---- state
    public ActionKind Action = ActionKind.None;
    public double ActionT, ActionDur;
    public double ActionDirX = 1, ActionDirZ;
    public bool ActionDone;
    /// <summary>1 = right, -1 = left.</summary>
    public int KickLeg = 1;
    public KickPlan? Plan;
    /// <summary>The strike in progress (for body mechanics): type, power, target angle relative to the body.</summary>
    public KickType KickType = KickType.Pass;
    public double KickPower;
    public bool KickLofted;
    public double KickRel;
    /// <summary>Seconds from the start of the strike to the ball leaving the foot (wind-up + swing).</summary>
    public double KickContact = 0.15;
    /// <summary>The strike is with the weaker foot.</summary>
    public bool KickWeak;
    /// <summary>Height of the ball at the moment of the strike (first-time volleys and half-volleys).</summary>
    public double KickHeight;
    /// <summary>Reaching for the ball: 0 = it's at his foot, 1 = a full stretch.</summary>
    public double KickStretch;
    /// <summary>Where the ball will be at contact, in his frame (m): forward, and to his left.</summary>
    public double KickBallF, KickBallL;
    /// <summary>Running velocity when the strike started, and the lunge toward the ball (m/s).</summary>
    public double KickVX, KickVZ, LungeX, LungeZ;
    /// <summary>Preferred foot: 1 = right, -1 = left.</summary>
    public int Foot = 1;
    /// <summary>Throw-in (two hands) rather than a keeper's one-arm throw.</summary>
    public bool ThrowIn;
    /// <summary>Where the keeper's hands met the ball, for the catch animation: its height, and how
    /// far in front of him and to his left (m).</summary>
    public double CatchY = 1, CatchF = 0.4, CatchL;
    /// <summary>A keeper's dive: seconds in the air before he comes down on his side.</summary>
    public double DiveFly = 0.6;
    /// <summary>Keeper on his feet for a save: seconds until the ball reaches his hands (-1: none),
    /// and where (his frame: forward, to his left, height), so the hands go to meet it.</summary>
    public double ReachIn = -1, ReachF, ReachL, ReachY;
    /// <summary>Smoothed forward acceleration (m/s²), used for body inertia.</summary>
    public double AccelFwd;
    /// <summary>Seconds before this player can be knocked off balance again.</summary>
    public double BalanceCD;
    public double TouchCooldown;
    public double Stamina = 1;
    public bool Sprinting;
    /// <summary>This step only: his acceleration scaled (the human going for the ball without PRESS). Reset by Move.</summary>
    public double AccelScale = 1;
    /// <summary>Seconds since this player last touched the ball.</summary>
    public double SinceTouch = 99;
    /// <summary>Height (m) of the ball when he last cushioned it (0 for a ground touch, a strike or a header).</summary>
    public double TouchH;
    /// <summary>Close control's pull on the ball (m/s, x/z) and when it was last applied (match time).</summary>
    public double PullX, PullZ, PullT = -1;

    // ---- animation (read by the renderer)
    public double StridePhase, LeanFwd, LeanSide, PrevSpeed;

    // ---- skill moves (Match.Skills): the move, its exit side (+1 left), the frame it's done in,
    // the speed he came in at, where he goes after it and where the ball sat when he started.
    public SkillMove Trick;
    public int TrickSide = 1;
    public double TrickV0, TrickExitX = 1, TrickExitZ, TrickBallF, TrickBallL;
    public bool TrickReleased, TrickFeinted;
    /// <summary>Match time he can start another move.</summary>
    public double TrickReady;
    /// <summary>Sold a dummy: seconds he's still wrong-footed, how, and which way he's leaning.</summary>
    public double FooledT;
    public Skills.Fool FoolKind;
    public double FoolX, FoolZ;

    /// <summary>Speed a slide tackle starts with, and when the grass has stopped it (set as he goes down).</summary>
    public double SlideV0 = 7.5, SlideStop = 0.8;
    /// <summary>Where a tackling leg reaches (unit, on the ground), fixed when he commits.</summary>
    public double LegX = 1, LegZ;

    /// <summary>Shirt name / number (club line-ups).</summary>
    public string Name = "";
    public int Number;
    /// <summary>The setup's Source for whoever is in this slot (the game's player card).</summary>
    public object? Source;

    public readonly int Id;
    public readonly int Team;
    /// <summary>Shirt slot: 0 GK, 1/4 FB, 2/3 CB, 5 DM, 6/7 CM, 8/10 wide forwards, 9 ST.</summary>
    public readonly int Index;
    public readonly Role Role;
    /// <summary>Formation slot in team frame (attacking +x): x,z in -1..1.</summary>
    public double BaseX, BaseZ;
    public Attributes Attrs;
    /// <summary>Changes only when a substitute takes the slot.</summary>
    public Look Look;

    double accelFor = double.NaN, weightFor = double.NaN, accelMemo;
    double agileFor = double.NaN, agileMemo = 1;

    public Player(int id, int team, int index, Role role, double baseX, double baseZ, Attributes attrs, Look look)
    {
        Id = id;
        Team = team;
        Index = index;
        Role = role;
        BaseX = baseX;
        BaseZ = baseZ;
        Attrs = attrs;
        Look = look;
    }

    public double Speed => Math.Sqrt(Vel.X * Vel.X + Vel.Z * Vel.Z);

    public double TopSpeed
    {
        get
        {
            // Heavier bodies carry a little less top speed.
            double mass = M.Clamp(1 - (Attrs.Weight - 78) * 0.0015, 0.96, 1.03);
            return PlayerK.TopSpeed * (0.86 + 0.14 * Attrs.Pace) * (0.75 + 0.25 * Stamina) * mass;
        }
    }

    /// <summary>Acceleration (m/s²): the accel stat, scaled by body mass; tired legs lose their burst.</summary>
    public double AccelRate => AccelBase * (0.7 + 0.3 * Stamina);

    double AccelBase
    {
        get
        {
            double accel = Attrs.Accel, weight = Attrs.Weight;
            if (accel != accelFor || weight != weightFor)
            {
                accelFor = accel;
                weightFor = weight;
                accelMemo = PlayerK.Accel * (0.8 + 0.35 * accel) * M.Clamp(JsMath.Pow(78 / weight, 0.3), 0.92, 1.08);
            }
            return accelMemo;
        }
    }

    /// <summary>Effective strength in duels: the stat plus body weight.</summary>
    public double DuelStrength => Attrs.Strength * 0.75 + M.Clamp((Attrs.Weight - 60) / 40, 0, 1) * 0.25;

    /// <summary>Aerial ability 0..1: jumping and height.</summary>
    public double Aerial => Attrs.Jumping * 0.6 + M.Clamp((Attrs.Height - 1.65) / 0.35, 0, 1) * 0.4;

    /// <summary>Highest ball (m) this player can head: taller players and better jumpers reach higher.</summary>
    public double HeadReach => PlayerK.HeadMax + (Attrs.Height - 1.8) * 0.9 + (Attrs.Jumping - 0.5) * 0.5;

    public bool IsBusy => Action != ActionKind.None;

    /// <summary>
    /// Length of one step (m) at a speed, for a body `scale` × 1.8 m tall: measured human gait,
    /// from ~0.75 m at a walk (about two steps a second) through ~1.2 m at a jog to ~1.95 m at a
    /// sprint (4.4 a second). Taller players stride longer and turn their legs over slower.
    /// </summary>
    public static double StepLength(double speed, double scale)
    {
        double v = Math.Min(speed, 10);
        if (!(scale > 0.5)) scale = 1;
        return (0.45 + 0.23 * v - 0.0065 * v * v) * scale;
    }

    /// <summary>A leg stretched out for the ball, 0..1: shoots out (~0.14 s), holds, draws back.</summary>
    public static double StretchExt(double t, double dur) => M.Smoothstep(0.03, 0.14, t) * (1 - M.Smoothstep(dur * 0.66, dur, t));

    public void StartAction(ActionKind kind, double dur, double dirX, double dirZ)
    {
        Action = kind;
        ActionT = 0;
        ActionDur = dur;
        ActionDirX = dirX;
        ActionDirZ = dirZ;
        ActionDone = false;
    }

    /// <summary>Physical movement with momentum: separate limits for speeding up, braking and turning.</summary>
    public void Move(double dt)
    {
        double accelScale = AccelScale;
        AccelScale = 1;
        PrevPos.Copy(Pos);
        PrevFacing = Facing;
        TouchCooldown = Math.Max(0, TouchCooldown - dt);
        SinceTouch += dt;

        double tx = MoveX * WantSpeed;
        double tz = MoveZ * WantSpeed;

        bool trick = false;
        // Actions override locomotion.
        if (Action != ActionKind.None)
        {
            ActionT += dt;
            var a = Action;
            if (a == ActionKind.Kick && ActionT >= KickContact)
            {
                // Follow-through: the body is carried on through the ball, then the player
                // gathers himself and runs on the way he wants to go.
                double k = M.Clamp((ActionT - KickContact) / Math.Max(0.01, ActionDur - KickContact), 0, 1);
                double blend = k * k;
                tx = Vel.X * 0.97 * (1 - blend) + MoveX * WantSpeed * blend;
                tz = Vel.Z * 0.97 * (1 - blend) + MoveZ * WantSpeed * blend;
            }
            else if (a == ActionKind.Kick)
            {
                // Strike on the run: the plant foot brakes the body; reaching for a ball that's
                // beyond the foot, he lunges toward it through the wind-up.
                tx = KickVX * 0.8 + LungeX;
                tz = KickVZ * 0.8 + LungeZ;
            }
            else if (a == ActionKind.Header || a == ActionKind.Throw || a == ActionKind.Punch)
            {
                double keep = a == ActionKind.Throw ? 0.4 : 0.8;
                tx = Vel.X * keep;
                tz = Vel.Z * keep;
            }
            else if (a == ActionKind.Tackle)
            {
                double p = ActionT / ActionDur;
                double lunge = p < 0.45 ? 4.5 : 0.5;
                tx = ActionDirX * lunge;
                tz = ActionDirZ * lunge;
            }
            else if (a == ActionKind.Slide)
            {
                // Committed: he goes where his momentum takes him (set as he went down, in
                // Match.StartTackle) and the grass brakes him to a stop.
                double sp0 = JsMath.Hypot(Vel.X, Vel.Z);
                double k = sp0 > 1e-3 ? Math.Max(0, sp0 - SlideDecel * dt) / sp0 : 0;
                Vel.X *= k;
                Vel.Z *= k;
                tx = Vel.X;
                tz = Vel.Z;
            }
            else if (a == ActionKind.Dive)
            {
                // Flying until he lands (planned with the dive), then the grass stops him.
                if (ActionT > DiveFly) Vel.Scale(Math.Max(0, 1 - dt * 9));
                tx = Vel.X;
                tz = Vel.Z;
            }
            else if (a == ActionKind.Catch || a == ActionKind.Parry)
            {
                tx = Vel.X * 0.4;
                tz = Vel.Z * 0.4;
            }
            else if (a == ActionKind.Fall)
            {
                // Knocked down: carried on by the hit, the grass brings him to a stop.
                double sp0 = JsMath.Hypot(Vel.X, Vel.Z);
                double k = sp0 > 1e-3 ? Math.Max(0, sp0 - 7 * dt) / sp0 : 0;
                Vel.X *= k;
                Vel.Z *= k;
                tx = Vel.X;
                tz = Vel.Z;
            }
            else if (a == ActionKind.Stretch)
            {
                // Reaching a leg out for it: the body carries on and leans in behind the leg.
                double lunge = ActionT < ActionDur * 0.45 ? 1.8 : 0;
                tx = Vel.X * 0.92 + ActionDirX * lunge;
                tz = Vel.Z * 0.92 + ActionDirZ * lunge;
            }
            else if (a == ActionKind.Stumble)
            {
                tx = Vel.X * 0.3;
                tz = Vel.Z * 0.3;
            }
            else if (a == ActionKind.Trick)
            {
                // A skill move: the feet are quick and sure (Match.Skills scripts the run).
                trick = true;
            }
            else if (a == ActionKind.Celebrate)
            {
                tx = MoveX * WantSpeed;
                tz = MoveZ * WantSpeed;
            }
            if (ActionT >= ActionDur) Action = ActionKind.None;
        }

        double vx = Vel.X;
        double vz = Vel.Z;
        double sp = Math.Sqrt(vx * vx + vz * vz);
        double top = TopSpeed;

        // Body orientation constrains speed: backpedalling and side-stepping are slower.
        bool passive = Action == ActionKind.Slide || Action == ActionKind.Dive || Action == ActionKind.Fall;
        if (!passive)
        {
            double tsp = Math.Sqrt(tx * tx + tz * tz);
            if (tsp > 0.1 && LookAt != null && SquareUp)
            {
                double off = Math.Abs(M.AngleDiff(Facing, JsMath.Atan2(tz, tx)));
                double cap = off < 1.2 ? top : off < 2.2 ? 5.6 : 4.0;
                if (tsp > cap)
                {
                    tx *= cap / tsp;
                    tz *= cap / tsp;
                }
            }
            double accel = AccelRate * accelScale * (Burst ? 1.7 : 1);
            if (trick)
            {
                // Short, sharp steps under him: the script's line is held to a firm limit.
                double dvx = tx - vx;
                double dvz = tz - vz;
                double m = Math.Sqrt(dvx * dvx + dvz * dvz);
                double lim = (16 + 6 * Attrs.Agility) * dt;
                if (m > lim)
                {
                    dvx *= lim / m;
                    dvz *= lim / m;
                }
                Vel.X += dvx;
                Vel.Z += dvz;
            }
            else if (sp < 0.6)
            {
                // Near a standstill the first step can go any way.
                double dvx = tx - vx;
                double dvz = tz - vz;
                double m = Math.Sqrt(dvx * dvx + dvz * dvz);
                double lim = (accel + 2) * dt;
                if (m > lim)
                {
                    dvx *= lim / m;
                    dvz *= lim / m;
                }
                Vel.X += dvx;
                Vel.Z += dvz;
            }
            else
            {
                // On the move, a body changes two things through its feet: how fast it goes
                // (drive or brake along the run) and where (the run line bends, at most as hard
                // as the planted foot grips). Both come out of the same grip, so a player can't
                // brake flat out and cut flat out at once.
                double fx = vx / sp;
                double fz = vz / sp;
                double wsp = Math.Sqrt(tx * tx + tz * tz);
                double err = 0, spWant = 0;
                if (wsp > 0.05)
                {
                    double ux = tx / wsp, uz = tz / wsp;
                    double dot = fx * ux + fz * uz;
                    err = JsMath.Atan2(fx * uz - fz * ux, dot);
                    // A sharp change of line is made by slowing into it.
                    spWant = wsp * Math.Max(0, dot);
                }
                // Direction changes go through the planted foot, so grip pulses with the stride;
                // agile, compact players cut sharper than tall, heavy ones, and nobody cuts at a
                // sprint the way he does at a jog.
                double plant = JsMath.Cos(StridePhase);
                if (Attrs.Height != agileFor)
                {
                    agileFor = Attrs.Height;
                    agileMemo = M.Clamp(JsMath.Pow(1.8 / agileFor, 0.6), 0.92, 1.08);
                }
                double latMax = PlayerK.Lateral * (0.8 + 0.3 * Attrs.Agility) * agileMemo * (0.78 + 0.44 * plant * plant)
                    * (1 - 0.035 * Math.Max(0, sp - 4));
                double dSp = spWant - sp;
                // Explosive first steps, fading near top speed (sprint-start curve).
                double aMax = dSp > 0 ? accel * Math.Max(0.1, 1 - JsMath.Pow(sp / (top + 0.4), 1.6)) + 0.6 : PlayerK.Brake;
                // Share of each the step asks for (1 = flat out), then the grip ellipse: the turn
                // has first call on it, unless he's turning back on himself, when the brake does.
                // People steer and change pace smoothly: a small correction is eased in (about a
                // tenth of a second), only a big one is taken flat out.
                // (A keeper's set: he takes every correction at once.)
                double kS = Role == Role.GK ? 1 / dt : SteerK, kP = Role == Role.GK ? 1 / dt : PaceK;
                double nl = Math.Min(1, Math.Abs(err) * sp * Math.Min(1 / dt, kS) / latMax);
                double na = Math.Min(1, Math.Abs(dSp) * Math.Min(1 / dt, kP) / aMax);
                const double Share = 0.75;
                if (Math.Abs(err) < Math.PI / 2) na = Math.Min(na, Math.Sqrt(1 - Share * nl * nl));
                else nl = Math.Min(nl, Math.Sqrt(1 - Share * na * na));
                double rot = JsMath.Sign(err) * nl * latMax * dt / sp;
                double nsp0 = Math.Max(0, sp + JsMath.Sign(dSp) * na * aMax * dt);
                double c = JsMath.Cos(rot), sn = JsMath.Sin(rot);
                Vel.X = (fx * c - fz * sn) * nsp0;
                Vel.Z = (fx * sn + fz * c) * nsp0;
            }
        }

        // Lean (for animation): forward with acceleration, sideways into turns.
        double nsp = Speed;
        double accelFwd = (nsp - PrevSpeed) / dt;
        PrevSpeed = nsp;
        double latAcc = 0;
        if (nsp > 0.5)
            latAcc = ((Vel.Z - vz) * (vx / Math.Max(sp, 0.01)) - (Vel.X - vx) * (vz / Math.Max(sp, 0.01))) / dt;
        if (dt != expDt)
        {
            expDt = dt;
            exp8 = 1 - JsMath.Exp(-dt * 8);
            exp10 = 1 - JsMath.Exp(-dt * 10);
        }
        double ke = exp8;
        AccelFwd += (M.Clamp(accelFwd, -12, 12) - AccelFwd) * exp10;
        BalanceCD = Math.Max(0, BalanceCD - dt);
        // A runner leans where the push of his feet points, so his weight stays over them:
        // tan(lean) = acceleration / g. Into the drive of a sprint start, back against a brake,
        // in toward the middle of a curve; a little forward at speed as well.
        LeanFwd += (M.Clamp(JsMath.Atan(M.Clamp(accelFwd, -12, 12) / Physics.Gravity) * 0.7 + nsp * 0.012, -0.35, 0.5) - LeanFwd) * ke;
        LeanSide += (M.Clamp(-JsMath.Atan(latAcc / Physics.Gravity) * 0.75, -0.5, 0.5) - LeanSide) * ke;

        Pos.X += Vel.X * dt;
        Pos.Z += Vel.Z * dt;

        // Facing. Committed to a tackle, the body turns (quickly) to the line it went in on.
        if (Action == ActionKind.Tackle || Action == ActionKind.Slide)
        {
            double step = 14 * dt;
            Facing += M.Clamp(M.AngleDiff(Facing, JsMath.Atan2(ActionDirZ, ActionDirX)), -step, step);
        }
        else if (!passive)
        {
            double want = Facing;
            bool moving = nsp > 0.6;
            double run = moving ? JsMath.Atan2(Vel.Z, Vel.X) : 0;
            if (LookAt != null)
            {
                double look = JsMath.Atan2(LookAt.Z - Pos.Z, LookAt.X - Pos.X);
                if (SquareUp || !moving) want = look;
                else
                {
                    // The body goes where he's running; slowing down, it opens up toward what he's
                    // watching (all the way when he's barely moving, ~30° at a sprint).
                    double open = 0.5 + 2.6 * (1 - M.Smoothstep(1.5, 4.5, nsp));
                    want = run + M.Clamp(M.AngleDiff(run, look), -open, open);
                }
            }
            else if (moving)
            {
                want = run;
            }
            double turnRate = (12.5 - nsp * 0.65) * (0.85 + 0.3 * Attrs.Agility);
            double d = M.AngleDiff(Facing, want);
            double step = turnRate * dt;
            double turn = M.Clamp(d, -step, step);
            Facing += turn;
            // Turning on the spot still takes steps: the feet shuffle round with the body.
            if (nsp < 2.5) StridePhase += Math.Abs(turn) * 1.6 * (1 - nsp / 2.5);
        }

        // Gait: one step = half a stride cycle.
        StridePhase += (nsp / StepLength(nsp, Look.Height)) * Math.PI * dt;

        // Stamina: sprinting drains, everything else recovers.
        double st = Attrs.Stamina;
        if (Sprinting && nsp > PlayerK.JogSpeed) Stamina = Math.Max(0, Stamina - dt * 0.035 * (1.45 - 0.9 * st));
        else Stamina = Math.Min(1, Stamina + dt * (nsp < 3 ? 0.03 : 0.012) * (0.7 + 0.6 * st));
    }
}
