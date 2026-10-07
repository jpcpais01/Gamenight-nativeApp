using System;

namespace GameNight.Sim;

public sealed partial class Match
{

    // ------------------------------------------------------------------ main step

    /// <summary>One step. `input` is HumanTeam's human; in a 1v1 (Versus) `input2` is the other side's.</summary>
    public void Step(InputState input, InputState? input2 = null)
    {
        Time += DT;
        PhaseT += DT;
        foreach (var s in Seats) s.SwitchT += DT;
        var ball = Ball;
        InvaderStep();

        // (The clock stops while a pitch invader is on.)
        bool running = (Phase == Phase.Play || Phase == Phase.Out || Phase == Phase.SetPiece || Phase == Phase.Kickoff) && InvaderT < 0;
        if (running && !Training) Clock += DT;

        // Half / full time: once the added time is up, the referee lets an attack in the final third
        // play out. He blows when it's over: the ball back out of the third, won by the defenders,
        // in the keeper's hands or dead, and at the latest a few minutes on. Never while the ball
        // is in either penalty area, whoever has it.
        double over = Clock - (MatchK.HalfSeconds * (45 + AddedTime)) / 45;
        bool inBox = Math.Abs(ball.Pos.X) > Pitch.HalfL - Pitch.BoxDepth && Math.Abs(ball.Pos.Z) < Pitch.BoxHalfWidth;
        bool attack = inBox || (Math.Abs(ball.Pos.X) > Pitch.HalfL / 3 && Teams[PossTeam].Dir == JsMath.Sign(ball.Pos.X) && HeldBy == null);
        // A penalty, once given, is always taken.
        if (running && over >= 0 && !PenaltyPending && ((Phase == Phase.Play && !attack) || Phase == Phase.Out || over >= (MatchK.HalfSeconds * 3) / 45))
        {
            pendingRestart = null;
            SetPiece = null;
            if (Half == 1)
            {
                Phase = Phase.Halftime;
                PhaseT = 0;
                Events.Whistle = 2;
            }
            else
            {
                Phase = Phase.Fulltime;
                PhaseT = 0;
                Events.Whistle = 3;
            }
        }
        if (Phase == Phase.Halftime && PhaseT > 3)
        {
            Half = 2;
            Clock = 0;
            foreach (var t in Teams) t.Dir = -t.Dir;
            foreach (var p in Players) p.Stamina = Math.Min(1, p.Stamina + 0.4);
            StartKickoff(1);
        }
        if (Phase == Phase.Out && PhaseT > 1.5 && pendingRestart != null && !Training)
        {
            var r = pendingRestart;
            pendingRestart = null;
            StartSetPiece(r.Kind, r.Team, r.X, r.Z);
        }
        if (Phase == Phase.Goal)
        {
            int kickTeam = Scorer != null ? 1 - Scorer.Team : 0;
            // Picked mid-run (perhaps a steered one): aim it from where he actually pulls up.
            var cel = Celebration;
            if (cel != null && PhaseT >= cel.At && PhaseT - DT < cel.At) AimCelebration();
            // The cut (camera's on the crowd): ball back on the spot, players most of the way home.
            if (PhaseT >= GoalSeq.Cut && PhaseT - DT < GoalSeq.Cut)
            {
                Ball.Reset(0, 0);
                Ball.Pos.Y = BallK.Radius;
                foreach (var p in Players)
                {
                    KickoffSpot(p, kickTeam, tmpV);
                    p.Pos.Set(tmpV.X + (p.Pos.X - tmpV.X) * 0.3, 0, tmpV.Z + (p.Pos.Z - tmpV.Z) * 0.3);
                    p.PrevPos.Copy(p.Pos);
                    p.Vel.Scale(0.3);
                    p.Action = ActionKind.None;
                }
            }
            if (PhaseT > GoalSeq.End) StartKickoff(kickTeam);
        }

        SendOffStep();
        SubStep();

        // Set piece timer.
        if (SetPiece != null) SetPiece.T += DT;

        // Intents.
        Steer.On = false;
        ApplyHumanInput(Seats[HumanTeam], input);
        if (Versus) ApplyHumanInput(Seats[1 - HumanTeam], input2 ?? Idle);
        else input2?.Events.Clear();
        AI.Update();
        TryStretches();

        // Locomotion.
        foreach (var p in Players) p.Move(DT);
        CollidePlayers();
        ConfineToPitch();
        KeepRestartDistance();
        KeepHeldInBox();

        // Action resolution (kicks, tackles).
        foreach (var p in Players) ResolveActions(p);

        // Ball.
        if (HeldBy != null)
        {
            var h = HeldBy;
            double hx = JsMath.Cos(h.Facing);
            double hz = JsMath.Sin(h.Facing);
            bool throwIn = SetPiece?.Kind == SetPieceKind.Throw;
            ball.PrevPos.Copy(ball.Pos);
            HeldBallSpot(h, throwIn, out double fwd, out double hy);
            ball.Pos.Set(h.Pos.X + hx * fwd, hy, h.Pos.Z + hz * fwd);
            ball.Vel.Copy(h.Vel);
            ball.Spin.Set(0, 0, 0);
            ball.OnGround = false;
        }
        else if (Phase == Phase.SetPiece || Phase == Phase.Kickoff)
        {
            ball.PrevPos.Copy(ball.Pos);
        }
        else
        {
            ball.Step(DT);
            ConsumeBallEvents();
            CloseControl();
            if (Phase == Phase.Play || Phase == Phase.Goal || Phase == Phase.Fulltime || Phase == Phase.Halftime) BallTouches();
        }

        if (Phase == Phase.Play) CheckOutOfPlay();
        if (Advantage != null) WatchAdvantage();

        // A pass that has died short is just a loose ball: whoever's nearest goes for it.
        if (PassTarget != null && Owner == null && Ball.OnGround && JsMath.Hypot(Ball.Vel.X, Ball.Vel.Z) < 1.8 && BallDist(PassTarget) > 2.5) PassTarget = null;

        // Ownership persistence.
        if (Owner != null && BallDist(Owner) > 3) Owner = null;
        if (Owner != null) PossTeam = Owner.Team;
        if (HeldBy != null) PossTeam = HeldBy.Team;

        UpdateExcitement();
    }

    /// <summary>
    /// Where a held ball is, ahead of the holder (m) and up: tucked in at the chest; on a throw
    /// it follows the arm (overarm: back behind the head and over; a roll: down to the grass);
    /// on a punt it's dropped onto the foot. The renderer puts it in his hands; this is where
    /// it leaves them.
    /// </summary>
    static void HeldBallSpot(Player h, bool throwIn, out double fwd, out double y)
    {
        double s = h.Look.Height;
        fwd = 0.3 * s;
        y = 1.08 * s;
        if (throwIn)
        {
            fwd = 0.32;
            y = 2.05;
            return;
        }
        double u = M.Clamp(h.ActionT / Math.Max(0.05, h.KickContact), 0, 1);
        if (h.Action == ActionKind.Throw && h.KickLofted)
        {
            double back = M.Smoothstep(0, 0.45, u), over = M.Smoothstep(0.55, 1, u);
            fwd = M.Lerp(M.Lerp(fwd, -0.3, back), 0.45, over) * s;
            y = M.Lerp(M.Lerp(y, 1.75, back), 2.05, over) * s;
        }
        else if (h.Action == ActionKind.Throw)
        {
            double down = M.Smoothstep(0, 0.6, u), through = M.Smoothstep(0.5, 1, u);
            fwd = M.Lerp(M.Lerp(fwd, -0.15, down), 0.75, through) * s;
            y = M.Lerp(M.Lerp(y, 0.55, down), 0.16, through);
        }
        else if (h.Action == ActionKind.Kick)
        {
            double drop = M.Smoothstep(0.55, 1, u);
            fwd = M.Lerp(0.42, 0.55, drop) * s;
            y = M.Lerp(0.98 * s, 0.42, drop * drop);
        }
    }

    void ConsumeBallEvents()
    {
        var be = Ball.Events;
        if (be.Bounce > 0) Events.Bounce = Math.Max(Events.Bounce, be.Bounce);
        if (be.Post > 0) Events.Post = Math.Max(Events.Post, be.Post);
        if (be.Net > 0)
        {
            Events.Net = Math.Max(Events.Net, be.Net);
            Events.NetX = be.NetX;
            Events.NetY = be.NetY;
            Events.NetZ = be.NetZ;
        }
        be.Bounce = 0;
        be.Post = 0;
        be.Net = 0;
    }

    void UpdateExcitement()
    {
        var b = Ball;
        int att = AttackingTeam();
        double target = 0.15;
        if (att >= 0 && Phase == Phase.Play)
        {
            double gx = GoalX(att);
            double d = M.Dist2D(b.Pos.X, b.Pos.Z, gx, 0);
            target = 0.15 + 0.75 * (1 - M.Smoothstep(10, 40, d));
        }
        if (Phase == Phase.Goal) target = 1;
        Excitement += (target - Excitement) * (1 - JsMath.Exp(-DT * 1.5));
    }

    // ------------------------------------------------------------------ celebrations

    /// <summary>After your goal, until a little after the camera comes round: the buttons pick the celebration.</summary>
    public bool CelebrationOpen =>
        Phase == Phase.Goal && !AutoPlay && Celebration == null && Scorer != null && HumanSide(Scorer.Team) && PhaseT < GoalSeq.Front + 1.4;

    void PickCelebration(CelebrationKind kind, double at)
    {
        Celebration = new Celebration { Kind = kind, At = at, Dx = 0, Dz = 0, Turn = 1 };
        AimCelebration();
    }

    /// <summary>The celebration plays toward the camera, which stands between the scorer and the centre spot.</summary>
    void AimCelebration()
    {
        var s = Scorer!;
        var c = Celebration!;
        double d = JsMath.Or1(JsMath.Hypot(s.Pos.X, s.Pos.Z));
        c.Dx = -s.Pos.X / d;
        c.Dz = -s.Pos.Z / d;
        c.Turn = s.Pos.Z * s.Pos.X >= 0 ? 1 : -1;
    }

    // ------------------------------------------------------------------ human control

    /// <summary>A 1v1 side with nobody's input this step.</summary>
    static readonly InputState Idle = new InputState();

    void ApplyHumanInput(Seat seat, InputState input)
    {
        var c = seat.Controlled;
        if (AutoPlay)
        {
            input.Events.Clear();
            return;
        }
        // After a goal the four buttons are celebrations (Sprint counts on the press).
        bool sprintDown = input.Sprint && !seat.SprintWas;
        seat.SprintWas = input.Sprint;
        if (Phase == Phase.Goal)
        {
            // The first moments of your goal: the stick steers the scorer's run.
            double mv = JsMath.Hypot(input.MoveX, input.MoveY);
            var st = Steer;
            bool mine = Scorer?.Team == c.Team;
            if (mine) st.On = mv > 0.12 && PhaseT < GoalSeq.Steer;
            if (mine && st.On)
            {
                st.X = input.MoveX / mv;
                st.Z = -input.MoveY / mv;
            }
            if (mine && CelebrationOpen && PhaseT > 0.25)
            {
                CelebrationKind? pick = sprintDown ? CelebrationKind.Flip : null;
                foreach (var ev in input.Events) if (ev.Kind == ButtonKind.Down) pick = Celebrations[ev.Btn];
                if (pick.HasValue) PickCelebration(pick.Value, Math.Max(PhaseT, GoalSeq.Front - 0.3));
            }
            input.Events.Clear();
            c.Sprinting = false;
            return;
        }
        bool attacking = HumanAttacking(c.Team);
        double m = JsMath.Hypot(input.MoveX, input.MoveY);
        if (m > 0.12) seat.NoInputT = 0;
        else seat.NoInputT += DT;

        // Training in goal: the stick moves him (idle: he takes up his own position), any button dives.
        if (KeeperHuman && c.Role == Role.GK && HeldBy != c && Phase == Phase.Play)
        {
            double side = Math.Abs(input.MoveY) > 0.3 ? -JsMath.Sign(input.MoveY) : 0;
            foreach (var ev in input.Events) if (ev.Kind == ButtonKind.Down) AI.HumanDive(c, side);
            input.Events.Clear();
            c.Sprinting = false;
            if (c.IsBusy) return;
            c.LookAt = null;
            if (m > 0.12)
            {
                c.MoveX = input.MoveX / m;
                c.MoveZ = -input.MoveY / m;
                c.WantSpeed = (input.Sprint ? 5.5 : 3.6) * Math.Min(1, m / 0.85);
                c.LookTarget.Copy(Ball.Pos);
                c.LookAt = c.LookTarget;
                c.SquareUp = true;
            }
            else AI.KeeperStance(c);
            return;
        }

        // Buttons.
        foreach (var ev in input.Events)
        {
            if (attacking)
            {
                if (ev.Kind != ButtonKind.Up) continue;
                // Play's stopped: nothing to strike (a queued pass would go off after the restart).
                if (Phase == Phase.Out || Phase == Phase.Halftime || Phase == Phase.Fulltime) continue;
                // Pressed before the last player switch: cancelled by it.
                if (ev.Hold > seat.SwitchT + 0.05) continue;
                double ax = m > 0.12 ? input.MoveX / m : JsMath.Cos(c.Facing);
                double az = m > 0.12 ? -input.MoveY / m : JsMath.Sin(c.Facing);
                KickPlan? plan = null;
                // Commands stay queued until the ball arrives (e.g. press Pass while it's coming).
                double exp = Time + HumanBuffer;
                // Hold = pass weight; slide the finger up while holding = lofted.
                double weight = M.Clamp(ev.Hold / 0.6, 0, 1);
                if (ev.Btn == Btn.A) plan = new KickPlan { Type = ev.SwipeUp ? KickType.Lob : KickType.Pass, DirX = ax, DirZ = az, Power = weight, TargetId = -1, Expires = exp };
                else if (ev.Btn == Btn.B) plan = new KickPlan { Type = KickType.Through, DirX = ax, DirZ = az, Power = weight, Lofted = ev.SwipeUp, TargetId = -1, Expires = exp };
                else if (ev.Btn == Btn.C) plan = new KickPlan { Type = KickType.Shot, DirX = ax, DirZ = az, Power = M.Clamp(ev.Hold / 0.85, 0.08, 1.15), TargetId = -1, Expires = exp };
                if (plan != null)
                {
                    plan.Aimed = m > 0.12;
                    // On a set piece the button means "deliver it", not shoot at goal.
                    // (Free kicks and penalties: Shoot is a shot.)
                    var spk = SetPiece?.Kind;
                    if (spk.HasValue && spk != SetPieceKind.FreeKick && spk != SetPieceKind.Penalty && plan.Type == KickType.Shot)
                        plan.Type = spk == SetPieceKind.Corner ? KickType.Cross : KickType.Lob;
                    if (HeldBy == c && plan.Type == KickType.Shot) plan.Type = KickType.Clear;
                    // Out of the keeper's hands: Pass rolls it, Pass slid up throws it overarm.
                    if (HeldBy == c && SetPiece == null && plan.Type == KickType.Pass) plan.Lofted = false;
                    else if (HeldBy == c && SetPiece == null && plan.Type == KickType.Lob && ev.Btn == Btn.A)
                    {
                        plan.Type = KickType.Pass;
                        plan.Lofted = true;
                    }
                    if (AimingDelivery)
                    {
                        // Corner / goal kick: Pass drives (whips) it onto the ring, Shoot floats it,
                        // Through plays it short.
                        var t = SetPiece!.Target!;
                        if (ev.Btn == Btn.B && AimingGoalKick)
                            plan = new KickPlan { Type = KickType.Pass, DirX = ax, DirZ = az, Power = 0.5, TargetId = ShortOption(c, t).Id, Expires = exp, Aimed = false, Lofted = false };
                        else if (ev.Btn == Btn.B)
                            plan = new KickPlan { Type = KickType.Pass, DirX = ax, DirZ = az, Power = 0.5, TargetId = -1, Expires = exp, Aimed = false };
                        else
                            plan = new KickPlan { Type = KickType.Cross, DirX = ax, DirZ = az, Power = plan.Power, TargetId = -1, Expires = Time + 6, Aimed = true, LandX = t.X, LandZ = t.Z, Float = ev.Btn == Btn.C };
                    }
                    if (plan.Type == KickType.Shot && AimingShot)
                    {
                        plan.AimZ = SetPiece!.AimZ;
                        plan.AimY = SetPiece.AimY;
                        plan.Aimed = true;
                        plan.Expires = Time + 6;
                    }
                    c.Plan = plan;
                }
            }
            else
            {
                if (ev.Btn == Btn.B && ev.Kind == ButtonKind.Down) ManualSwitch(seat);
                if (ev.Btn == Btn.A && ev.Kind == ButtonKind.Down && Phase == Phase.Play)
                {
                    bool dbl = Time - seat.LastTackleTap < 0.32;
                    seat.LastTackleTap = Time;
                    if (!seat.Controlled.IsBusy) LungeAt(seat.Controlled, dbl);
                }
            }
        }
        // One button: going hard. Whenever the ball isn't ours, that's pressing for it.
        seat.PressHeld = input.Sprint && Owner?.Team != c.Team;
        input.Events.Clear();
        // Sliding down on Sprint commits to a tackle; sliding left commits to a slide tackle.
        if (input.TackleSwipe != TackleSwipe.None)
        {
            if (!attacking)
            {
                if (seat.LungeOn && input.TackleSwipe == TackleSwipe.Slide) seat.LungeSlide = true;
                else
                {
                    seat.LungeOn = true;
                    seat.LungeSlide = input.TackleSwipe == TackleSwipe.Slide;
                    seat.LungeUntil = Time + 0.75;
                }
            }
            input.TackleSwipe = TackleSwipe.None;
        }
        UpdateLunge(seat, attacking);

        if (Phase == Phase.Halftime || Phase == Phase.Fulltime || Phase == Phase.Out)
        {
            c.Sprinting = false;
            if (InvaderWalk(c)) return;
            if (Phase == Phase.Out) c.WantSpeed = Math.Max(0, c.WantSpeed - DT * 6);
            return;
        }

        // Set piece taker stays on the ball. Lining up a shot: the stick moves the aim
        // (screen-relative in the third-person view: right is the taker's right, up is higher).
        if (SetPiece != null && SetPiece.Taker == c)
        {
            var sp = SetPiece;
            if (AimingCorner && m > 0.12)
            {
                // The ring moves with the stick as you see it: right is right, up is away.
                var t = sp.Target!;
                double dir = Teams[sp.Team].Dir;
                double gx = Pitch.HalfL * dir;
                t.X += input.MoveX * 9 * DT;
                t.Z -= input.MoveY * 9 * DT;
                double depth = M.Clamp((gx - t.X) * dir, 1.5, 32);
                t.X = gx - dir * depth;
                t.Z = M.Clamp(t.Z, -Pitch.HalfW + 2.5, Pitch.HalfW - 2.5);
            }
            else if (AimingGoalKick && m > 0.12)
            {
                // Anywhere upfield a keeper can reach: 15 to 62 m out, inside the touchlines. The
                // camera stands behind the keeper looking at the ring, so the stick is screen-relative.
                var t = sp.Target!;
                double dir = Teams[sp.Team].Dir;
                double fl = JsMath.Or1(JsMath.Hypot(t.X - sp.X, t.Z - sp.Z));
                double fx = (t.X - sp.X) / fl;
                double fz = (t.Z - sp.Z) / fl;
                t.X = M.Clamp(t.X + (input.MoveY * fx - input.MoveX * fz) * 16 * DT, -Pitch.HalfL + 3, Pitch.HalfL - 3);
                t.Z = M.Clamp(t.Z + (input.MoveY * fz + input.MoveX * fx) * 16 * DT, -Pitch.HalfW + 3, Pitch.HalfW - 3);
                if ((t.X - sp.X) * dir < 8) t.X = sp.X + dir * 8;
                double dx = t.X - sp.X;
                double dz = t.Z - sp.Z;
                double d = JsMath.Or1(JsMath.Hypot(dx, dz));
                double k = M.Clamp(d, 15, 62) / d;
                t.X = sp.X + dx * k;
                t.Z = sp.Z + dz * k;
            }
            if (AimingShot && m > 0.12)
            {
                double dir = Teams[sp.Team].Dir;
                double lim = Pitch.GoalHalfWidth + 0.9;
                sp.AimZ = M.Clamp((sp.AimZ ?? 0) + input.MoveX * 3.4 * DT * dir, -lim, lim);
                sp.AimY = M.Clamp((sp.AimY ?? 1) + input.MoveY * 1.5 * DT, 0.2, Pitch.GoalHeight + 0.5);
            }
            return;
        }

        // Movement.
        c.Sprinting = input.Sprint;
        c.LookAt = null;
        c.SquareUp = false;
        c.Burst = false;
        if (m > 0.12)
        {
            double mx = input.MoveX / m;
            double mz = -input.MoveY / m;
            c.MoveX = mx;
            c.MoveZ = mz;
            c.TouchX = mx;
            c.TouchZ = mz;
            double walk = Math.Min(1, m / 0.85);
            c.WantSpeed = input.Sprint ? c.TopSpeed : PlayerK.JogSpeed * (0.35 + 0.65 * walk);
            if (Owner == c && !input.Sprint) c.WantSpeed *= PlayerK.DribbleSpeedFactor;
            // Dribbling: the stick sets where the next touch goes; between touches the player
            // runs onto the ball so turns become real cuts instead of running off without it.
            if (Owner == c || (c.Plan != null && Owner == null && BallDist(c) < 3)) TrackBall(c, mx, mz);
        }
        else
        {
            c.MoveX = 0;
            c.MoveZ = 0;
            c.WantSpeed = 0;
            if (Owner == c || (c.Plan != null && Owner == null && BallDist(c) < 3))
            {
                TrackBall(c, JsMath.Cos(c.Facing), JsMath.Sin(c.Facing));
                if (BallDist(c) >= 0.55) c.WantSpeed = Math.Max(c.WantSpeed, Math.Min(PlayerK.JogSpeed, BallDist(c) * 3));
            }
        }

        // Without the ball, he goes for it by himself (see GoForBall).
        if (Owner != c) GoForBall(seat, c, input, m);

        // Caught inside the distance at the other side's dead ball: an idle stick walks him out.
        var zn = GetRestartZone(c);
        if (zn != null && m <= 0.12)
        {
            double dx = c.Pos.X - zn.X;
            double dz = c.Pos.Z - zn.Z;
            double d = JsMath.Hypot(dx, dz);
            if (d < zn.R - 0.1)
            {
                c.MoveX = d > 1e-3 ? dx / d : -Teams[c.Team].Dir;
                c.MoveZ = d > 1e-3 ? dz / d : 0;
                c.WantSpeed = PlayerK.JogSpeed;
                c.LookTarget.Copy(Ball.Pos);
                c.LookAt = c.LookTarget;
            }
        }
    }

    /// <summary>
    /// The active player without the ball, one idea: he knows where it can be won and goes
    /// there himself. A loose ball or a pass in flight he meets, arriving as it does; an
    /// opponent on the ball he closes down and presses tight, goal-side, squeezing in and
    /// pouncing on a heavy touch. All of that is automatic and the same with or without
    /// PRESS / SPRINT, flat out included; without it he gets going 10% slower, and only on it
    /// does he put a foot in to take the ball. The stick is always yours: pointed roughly at
    /// his run it bends it, pointed away it takes over.
    /// </summary>
    void GoForBall(Seat seat, Player c, InputState input, double m)
    {
        bool press = input.Sprint;
        var own = Owner;
        if (Phase != Phase.Play || HeldBy != null || own?.Team == c.Team || ShotTeam() == c.Team)
        {
            seat.PressTight = 0;
            return;
        }
        double tx, tz, speed;
        bool face = false, burst = false, square = false;
        double d = BallDist(c);
        if (own != null)
        {
            // Goal-side of the ball, moving with it, under a metre off and squeezing in the
            // longer he stays tight.
            seat.PressTight = d < 2.2 ? seat.PressTight + DT : 0;
            double keep = M.Lerp(0.9, 0.4, M.Smoothstep(0.15, 0.6, seat.PressTight));
            bool onBall = AI.PressPoint(c, own, tmpV, keep);
            double pull = onBall ? 5 : 3;
            double vx = Ball.Vel.X * 0.9 + (tmpV.X - c.Pos.X) * pull;
            double vz = Ball.Vel.Z * 0.9 + (tmpV.Z - c.Pos.Z) * pull;
            double v = JsMath.Hypot(vx, vz);
            if (v > 0.2)
            {
                tx = vx / v;
                tz = vz / v;
            }
            else
            {
                tx = (Ball.Pos.X - c.Pos.X) / Math.Max(0.01, d);
                tz = (Ball.Pos.Z - c.Pos.Z) / Math.Max(0.01, d);
                v = 0;
            }
            // As quick as he needs to close the gap and stay with him.
            speed = Math.Min(v, c.TopSpeed);
            face = true;
            burst = onBall;
            // Square-on while he can keep up that way; a carrier running at him faster than he
            // can backpedal, he turns and runs with.
            square = !onBall && v < 4.5;
            // Going in (only on PRESS): the ball shows and a foot can get to it. Kept out long
            // enough, he goes through anyway, shield or not.
            if (press && !c.IsBusy && !seat.LungeOn && Time > seat.PokeReady && Ball.Pos.Y < 0.6 && d < 1.35 &&
                (BallOpen(c, own, d) || (seat.PressTight > 1.0 && d < 0.95)))
            {
                LungeAt(c, false);
                seat.PokeReady = Time + 0.8;
                seat.PressTight = 0;
            }
        }
        else
        {
            seat.PressTight = 0;
            // A pass to a team-mate is his, unless we'd clearly get there first.
            var pt = PassTarget;
            if (pt != null && pt.Team == c.Team && pt != c)
            {
                double mine = AI.Intercept[c.Id].T;
                double his = AI.Intercept[pt.Id].T;
                if (mine < 0 || (his >= 0 && mine > his - 0.3)) return;
            }
            AI.MeetPoint(c, tmpV);
            // He runs at the ball, leading it only half way to where he'd cut it off: the run
            // bends onto it as he closes, rather than heading off to where it's going (as quick
            // over 180 loose balls, 2.38 s against 2.42 s).
            if (d >= 3)
            {
                tmpV.X = Ball.Pos.X + (tmpV.X - Ball.Pos.X) * 0.5;
                tmpV.Z = Ball.Pos.Z + (tmpV.Z - Ball.Pos.Z) * 0.5;
            }
            double dx = tmpV.X - c.Pos.X;
            double dz = tmpV.Z - c.Pos.Z;
            double dd = JsMath.Hypot(dx, dz);
            if (dd < 0.25) return;
            tx = dx / dd;
            tz = dz / dd;
            // Flat out, but no faster than he can still pull up from, so he arrives on it
            // rather than past it (running on with it when it's rolling his way).
            var ip = AI.Intercept[c.Id];
            speed = c.TopSpeed;
            double along = Math.Max(0, d < 3 ? Ball.Vel.X * tx + Ball.Vel.Z * tz : ip.VX * tx + ip.VZ * tz);
            speed = Math.Min(speed, Math.Sqrt(along * along + 2 * PlayerK.Brake * dd) + 0.6);
            burst = d < 2.5;
        }

        // The stick only nudges it, and only pointed at the ball: full nudge straight at it,
        // fading to none 45° off; beyond that it's ignored.
        if (m > 0.12)
        {
            double sx = input.MoveX / m;
            double sz = -input.MoveY / m;
            double bx = Ball.Pos.X - c.Pos.X;
            double bz = Ball.Pos.Z - c.Pos.Z;
            double off = Math.Abs(JsMath.Atan2(sx * bz - sz * bx, sx * bx + sz * bz));
            double w = 0.06 * Math.Max(0, 1 - off / (Math.PI / 4));
            double nx = tx + sx * w;
            double nz = tz + sz * w;
            double n = JsMath.Hypot(nx, nz);
            if (n > 0.05)
            {
                tx = nx / n;
                tz = nz / n;
            }
        }
        c.MoveX = tx;
        c.MoveZ = tz;
        c.WantSpeed = Math.Min(c.TopSpeed, speed);
        // Running hard costs legs, button or not (as it does the computer's players).
        c.Sprinting = c.WantSpeed > PlayerK.JogSpeed + 0.5;
        c.Burst = burst;
        if (face && d < 6)
        {
            c.LookTarget.Copy(Ball.Pos);
            c.LookAt = c.LookTarget;
            c.SquareUp = square;
        }
    }

    /// <summary>
    /// Close control for the human's dribbler: between touches the ball is gently drawn toward a
    /// point just ahead of his feet, so it feels attached without being glued.
    /// </summary>
    void CloseControl()
    {
        var c = Owner;
        var b = Ball;
        if (AutoPlay || c == null || !Piloted(c) || HeldBy != null || c.IsBusy || !b.OnGround) return;
        double gap = BallDist(c);
        if (gap > 1.8) return;
        bool moving = c.WantSpeed > 0.3;
        double dx = moving ? c.TouchX : JsMath.Cos(c.Facing);
        double dz = moving ? c.TouchZ : JsMath.Sin(c.Facing);
        double lead = 0.45 + c.Speed * 0.07;
        double px = c.Pos.X + dx * lead;
        double pz = c.Pos.Z + dz * lead;
        double wantVx = c.Vel.X + (px - b.Pos.X) * 3.5;
        double wantVz = c.Vel.Z + (pz - b.Pos.Z) * 3.5;
        c.PullX = wantVx - b.Vel.X;
        c.PullZ = wantVz - b.Vel.Z;
        c.PullT = Time;
        double k = 1 - JsMath.Exp(-DT * 1.25);
        b.Vel.X += (wantVx - b.Vel.X) * k;
        b.Vel.Z += (wantVz - b.Vel.Z) * k;
        // Keep the spin consistent with rolling so the ball doesn't skid oddly.
        b.Spin.Z = -b.Vel.X / BallK.Radius;
        b.Spin.X = b.Vel.Z / BallK.Radius;
    }

    /// <summary>Steer a ball-carrier onto the ball when it isn't at his feet.</summary>
    void TrackBall(Player c, double mx, double mz)
    {
        var b = Ball;
        c.TouchX = mx;
        c.TouchZ = mz;
        double gap = BallDist(c);
        if (gap < 0.55) return;
        double look = M.Clamp(gap / Math.Max(1, c.Speed + 1), 0.05, 0.4);
        double bx = b.Pos.X + b.Vel.X * look - c.Pos.X;
        double bz = b.Pos.Z + b.Vel.Z * look - c.Pos.Z;
        double bd = JsMath.Hypot(bx, bz);
        if (bd < 0.01) return;
        // Mostly toward the ball, a little toward the stick so the body is set for the next touch.
        double w = gap > 1.2 ? 0.85 : 0.65;
        double nx = mx * (1 - w) + (bx / bd) * w;
        double nz = mz * (1 - w) + (bz / bd) * w;
        double n = JsMath.Or1(JsMath.Hypot(nx, nz));
        c.MoveX = nx / n;
        c.MoveZ = nz / n;
        // If the ball is running away, chase it at least at its pace.
        double bs = JsMath.Hypot(b.Vel.X, b.Vel.Z);
        if (gap > 1.0) c.WantSpeed = Math.Max(c.WantSpeed, Math.Min(c.TopSpeed, bs + 1.2));
    }

    void ManualSwitch(Seat seat)
    {
        var team = Teams[seat.Controlled.Team].Players;
        Player? best = null;
        double bestScore = 1e9;
        foreach (var p in team)
        {
            if (p == seat.Controlled || p.Role == Role.GK) continue;
            var ip = AI.Intercept[p.Id];
            double score = ip.T >= 0 ? ip.T : 5 + BallDist(p) / 8;
            if (score < bestScore)
            {
                bestScore = score;
                best = p;
            }
        }
        if (best != null) SetControlled(best);
    }

    /// <summary>
    /// The sprint-swipe tackle is a commitment, not a button-mash: the defender keeps pressing and
    /// strikes at the right moment — the ball within reach and not tucked away behind the
    /// carrier's body (or he's simply right on it).
    /// </summary>
    void UpdateLunge(Seat seat, bool attacking)
    {
        if (!seat.LungeOn) return;
        var c = seat.Controlled;
        if (attacking || Phase != Phase.Play)
        {
            seat.LungeOn = false;
            return;
        }
        if (c.IsBusy) return;
        double d = BallDist(c);
        var b = Ball.Pos;
        if (Time > seat.LungeUntil)
        {
            if (d < (seat.LungeSlide ? 3.2 : 2.3) && b.Y < 0.7) LungeAt(c, seat.LungeSlide);
            seat.LungeOn = false;
            return;
        }
        double reach = seat.LungeSlide ? 2.8 : 1.5;
        if (d > reach || b.Y > 0.7) return;
        var carrier = Owner;
        bool open = carrier == null || carrier.Team == c.Team || BallOpen(c, carrier, d);
        if (open || d < reach * 0.6)
        {
            LungeAt(c, seat.LungeSlide);
            seat.LungeOn = false;
        }
    }

    /// <summary>
    /// Whether a challenger at ball distance `d` can get a foot to the carrier's ball: it isn't
    /// tucked away behind the carrier's body (or it's run out from under him).
    /// </summary>
    public bool BallOpen(Player c, Player carrier, double d)
    {
        var b = Ball.Pos;
        double cx = carrier.Pos.X - c.Pos.X;
        double cz = carrier.Pos.Z - c.Pos.Z;
        double cd = JsMath.Hypot(cx, cz);
        bool behind = cd < d && (cx * (b.X - c.Pos.X) + cz * (b.Z - c.Pos.Z)) / (cd * d + 1e-6) > 0.85;
        return !behind || BallDist(carrier) > 0.5;
    }

    /// <summary>
    /// Strike toward where the ball will be as the boot gets there. A slide covers
    /// v0·t − ½·decel·t² on the grass with the boot about a metre past the hip, so the meeting
    /// time comes from the distance (a ball a stride away is met almost at once, one three
    /// metres off a third of a second later); a standing tackle just leads it a touch.
    /// </summary>
    void LungeAt(Player c, bool slide)
    {
        double lead = 0.15;
        if (slide)
        {
            double v0 = M.Clamp(c.Speed + 1.2, 4.5, 8.5);
            double stop = v0 / Player.SlideDecel;
            for (lead = 0; lead < 0.6; lead += 0.02)
            {
                double ts = Math.Min(lead, stop);
                double reach = v0 * ts - 0.5 * Player.SlideDecel * ts * ts + 0.95;
                if (reach >= M.Dist2D(c.Pos.X, c.Pos.Z, Ball.Pos.X + Ball.Vel.X * lead, Ball.Pos.Z + Ball.Vel.Z * lead)) break;
            }
        }
        double tx = Ball.Pos.X + Ball.Vel.X * lead - c.Pos.X;
        double tz = Ball.Pos.Z + Ball.Vel.Z * lead - c.Pos.Z;
        double d = Math.Max(0.01, JsMath.Hypot(tx, tz));
        StartTackle(c, tx / d, tz / d, slide);
    }

    /// <summary>
    /// Committing to a tackle toward (dx, dz). Going in is a plant step, and a plant step only
    /// bends a run so far: hardly at all at a sprint, a lot from a jog. The body goes on that
    /// line; the leg reaches the rest of the way to the ball (up to ~35° off it).
    /// </summary>
    public void StartTackle(Player p, double dx, double dz, bool slide)
    {
        double want = JsMath.Atan2(dz, dx);
        double sp = p.Speed;
        double body = want;
        if (sp > 1.2)
        {
            double run = JsMath.Atan2(p.Vel.Z, p.Vel.X);
            double fast = M.Clamp((sp - 2) / 5, 0, 1);
            double turn = slide ? 1.3 - 0.75 * fast : 2.2 - 1.2 * fast;
            body = run + M.Clamp(M.AngleDiff(run, want), -turn, turn);
        }
        double leg = body + M.Clamp(M.AngleDiff(body, want), -0.6, 0.6);
        double bx = JsMath.Cos(body);
        double bz = JsMath.Sin(body);
        p.LegX = JsMath.Cos(leg);
        p.LegZ = JsMath.Sin(leg);
        // Tackle with the leg on the ball's side (same convention as strikes).
        double side = -bz * (Ball.Pos.X - p.Pos.X) + bx * (Ball.Pos.Z - p.Pos.Z);
        p.KickLeg = side >= 0 ? 1 : -1;
        if (slide)
        {
            // Down onto the grass with the pace he carries along that line (and the push of the
            // plant): what's across it is lost in the step. Then only the grass slows him.
            double v0 = M.Clamp(p.Vel.X * bx + p.Vel.Z * bz + 1.2, 4.5, 8.5);
            p.Vel.X = bx * v0;
            p.Vel.Z = bz * v0;
            p.SlideV0 = v0;
            p.SlideStop = v0 / Player.SlideDecel;
            p.StartAction(ActionKind.Slide, p.SlideStop + 0.55, bx, bz);
        }
        else p.StartAction(ActionKind.Tackle, 0.42, bx, bz);
    }
}
