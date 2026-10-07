using System;

namespace GameNight.Sim;

/// <summary>The special moves a player on the ball can pull off (slide SPRINT on the ball; each way is a slot the club fills).</summary>
public enum SkillMove : byte
{
    None, BodyFeint, StepOver, DragBack, CruyffTurn, FakeShot, Roulette, Croqueta, HeelToHeel, Elastico, Rainbow,
    // Signature skills (double tap SPRINT): one per 4-star or 5-star man, his own.
    RonaldoChop, McGeadySpin, HocusPocus, StepOverStorm, Sombrero, Panna,
}

/// <summary>
/// What a skill move does, written once for the sim and the renderer. Every move is a short
/// script in the carrier's own frame at the moment he starts it: forward (f) along his run, and
/// sideways (l) toward the side he's going to beat his man on (the exit side, e = +1 left or -1
/// right, so the same script mirrors). The script moves his body, carries the ball on his foot
/// until the release, when the ball goes back to the physics with a real touch, and at the feint
/// moment it sells the dummy to the men in front of him.
/// </summary>
public static class Skills
{
    public static readonly SkillMove[] All =
    {
        SkillMove.BodyFeint, SkillMove.StepOver, SkillMove.DragBack, SkillMove.CruyffTurn, SkillMove.FakeShot,
        SkillMove.Roulette, SkillMove.Croqueta, SkillMove.HeelToHeel,
        SkillMove.Elastico, SkillMove.Rainbow,
    };

    /// <summary>The signature skills: three for 4-star men, three for 5-star men.</summary>
    public static readonly SkillMove[] Signatures4 = { SkillMove.RonaldoChop, SkillMove.McGeadySpin, SkillMove.HocusPocus };
    public static readonly SkillMove[] Signatures5 = { SkillMove.StepOverStorm, SkillMove.Sombrero, SkillMove.Panna };

    /// <summary>A man's own signature skill from his skill stars and a seed that's his alone
    /// (none below 4 stars).</summary>
    public static SkillMove SignatureFor(int stars, uint seed) =>
        stars >= 5 ? Signatures5[seed % 3] : stars == 4 ? Signatures4[seed % 3] : SkillMove.None;

    /// <summary>1, 2 or 3 stars: how hard the move is (4 and 5 for the signatures).</summary>
    public static int Tier(SkillMove m) => m switch
    {
        SkillMove.RonaldoChop or SkillMove.McGeadySpin or SkillMove.HocusPocus => 4,
        SkillMove.StepOverStorm or SkillMove.Sombrero or SkillMove.Panna => 5,
        SkillMove.Roulette or SkillMove.Croqueta or SkillMove.HeelToHeel => 2,
        SkillMove.Elastico or SkillMove.Rainbow => 3,
        SkillMove.None => 0,
        _ => 1,
    };

    public static string Name(SkillMove m) => m switch
    {
        SkillMove.BodyFeint => "Body Feint",
        SkillMove.StepOver => "Step Over",
        SkillMove.DragBack => "Drag Back",
        SkillMove.CruyffTurn => "Cruyff Turn",
        SkillMove.FakeShot => "Fake Shot",
        SkillMove.Roulette => "Roulette",
        SkillMove.Croqueta => "La Croqueta",
        SkillMove.HeelToHeel => "Heel to Heel",
        SkillMove.Elastico => "Elastico",
        SkillMove.Rainbow => "Rainbow Flick",
        SkillMove.RonaldoChop => "Ronaldo Chop",
        SkillMove.McGeadySpin => "McGeady Spin",
        SkillMove.HocusPocus => "Hocus Pocus",
        SkillMove.StepOverStorm => "Step Over Storm",
        SkillMove.Sombrero => "Sombrero Flick",
        SkillMove.Panna => "Panna",
        _ => "",
    };

    /// <summary>What it does, for the skill slots page.</summary>
    public static string How(SkillMove m) => m switch
    {
        SkillMove.BodyFeint => "Dips a shoulder one way, goes the other",
        SkillMove.StepOver => "Steps round the ball, then pushes it past",
        SkillMove.DragBack => "Stops the ball under the sole, pulls it back",
        SkillMove.CruyffTurn => "Fakes a pass, drags it behind his leg",
        SkillMove.FakeShot => "Shapes to shoot, chops it away",
        SkillMove.Roulette => "Spins over the ball with both soles",
        SkillMove.Croqueta => "Shifts it foot to foot past his man",
        SkillMove.HeelToHeel => "Back-heels it through his own legs",
        SkillMove.Elastico => "Outside of the foot one way, snaps it back",
        SkillMove.Rainbow => "Flicks it up over his head and the man",
        SkillMove.RonaldoChop => "Chops it behind his standing leg, cuts away",
        SkillMove.McGeadySpin => "Sole-drags it sideways, spins right round",
        SkillMove.HocusPocus => "Flicks it behind his leg and across",
        SkillMove.StepOverStorm => "Three quick step overs, then explodes",
        SkillMove.Sombrero => "Scoops it over his man's head, runs round",
        SkillMove.Panna => "Through the man's legs, round him, gone",
        _ => "",
    };

    /// <summary>The slots a club starts with: up, left, right, down.</summary>
    public static readonly SkillMove[] DefaultSlots = { SkillMove.StepOver, SkillMove.BodyFeint, SkillMove.Roulette, SkillMove.DragBack };

    /// <summary>The highest tier of move a player with this many skill stars knows: 1-2 stars the
    /// one-star moves, 3-4 the two-star ones as well, 5 all ten.</summary>
    public static int TopTier(int stars) => stars >= 5 ? 3 : stars >= 3 ? 2 : 1;

    public static bool Knows(int stars, SkillMove m) => Tier(m) <= TopTier(stars);

    /// <summary>Skill stars for a player without a card (quick matches): from his touch and agility.</summary>
    public static int StarsFrom(Attributes a, Role role)
    {
        if (role == Role.GK) return 1;
        double score = a.Control * 0.7 + a.Agility * 0.3;
        return Math.Clamp(1 + (int)Math.Floor((score - 0.56) / 0.075), 1, 5);
    }

    // ------------------------------------------------------------------ the scripts

    /// <summary>How the men in front are sold the dummy: they lean the wrong way (Side), stop to
    /// block a shot or pass that never comes (Freeze), or carry on past him as he turns (Overrun).</summary>
    public enum Fool { Side, Freeze, Overrun }

    public struct Timing
    {
        /// <summary>The script runs his body to the end of the move, not just to the release (the
        /// panna's run round his man).</summary>
        public bool Whole;
        /// <summary>Whole move (s); the ball goes back to the physics at Release; the dummy is sold at Feint.</summary>
        public double Dur, Release, Feint;
        public Fool Fool;
        /// <summary>Where he goes after it, in the move's frame (f, l·e), and how fast (m/s, from his entry speed).</summary>
        public double ExitF, ExitL;
    }

    public static Timing TimingOf(SkillMove m) => m switch
    {
        SkillMove.BodyFeint => new Timing { Dur = 0.6, Release = 0.36, Feint = 0.18, Fool = Fool.Side, ExitF = 0.8, ExitL = 0.6 },
        SkillMove.StepOver => new Timing { Dur = 0.66, Release = 0.42, Feint = 0.26, Fool = Fool.Side, ExitF = 0.75, ExitL = 0.66 },
        SkillMove.DragBack => new Timing { Dur = 0.75, Release = 0.55, Feint = 0.24, Fool = Fool.Overrun, ExitF = -0.9, ExitL = 0.44 },
        SkillMove.CruyffTurn => new Timing { Dur = 0.72, Release = 0.52, Feint = 0.22, Fool = Fool.Freeze, ExitF = -0.85, ExitL = 0.53 },
        SkillMove.FakeShot => new Timing { Dur = 0.66, Release = 0.46, Feint = 0.24, Fool = Fool.Freeze, ExitF = 0.35, ExitL = 0.94 },
        SkillMove.Roulette => new Timing { Dur = 0.92, Release = 0.72, Feint = 0.34, Fool = Fool.Overrun, ExitF = 0.6, ExitL = 0.8 },
        SkillMove.Croqueta => new Timing { Dur = 0.46, Release = 0.26, Feint = 0.09, Fool = Fool.Side, ExitF = 0.85, ExitL = 0.53 },
        SkillMove.HeelToHeel => new Timing { Dur = 0.52, Release = 0.27, Feint = 0.15, Fool = Fool.Freeze, ExitF = 0.94, ExitL = 0.34 },
        SkillMove.Elastico => new Timing { Dur = 0.62, Release = 0.36, Feint = 0.2, Fool = Fool.Side, ExitF = 0.6, ExitL = 0.8 },
        SkillMove.Rainbow => new Timing { Dur = 0.55, Release = 0.31, Feint = 0.24, Fool = Fool.Freeze, ExitF = 1, ExitL = 0 },
        SkillMove.RonaldoChop => new Timing { Dur = 0.56, Release = 0.3, Feint = 0.2, Fool = Fool.Overrun, ExitF = 0.2, ExitL = 0.98 },
        SkillMove.McGeadySpin => new Timing { Dur = 0.9, Release = 0.66, Feint = 0.32, Fool = Fool.Overrun, ExitF = 0.75, ExitL = 0.66 },
        SkillMove.HocusPocus => new Timing { Dur = 0.52, Release = 0.25, Feint = 0.14, Fool = Fool.Side, ExitF = 0.72, ExitL = 0.7 },
        SkillMove.StepOverStorm => new Timing { Dur = 1.06, Release = 0.84, Feint = 0.64, Fool = Fool.Side, ExitF = 0.72, ExitL = 0.7 },
        SkillMove.Sombrero => new Timing { Dur = 0.5, Release = 0.3, Feint = 0.22, Fool = Fool.Freeze, ExitF = 1, ExitL = 0 },
        SkillMove.Panna => new Timing { Dur = 0.8, Release = 0.2, Feint = 0.12, Fool = Fool.Freeze, ExitF = 1, ExitL = 0, Whole = true },
        _ => new Timing { Dur = 0.5, Release = 0.3, Feint = 0.2, ExitF = 1 },
    };

    /// <summary>Speed (m/s) he comes out of the move at, from his speed going in.</summary>
    public static double ExitSpeed(SkillMove m, double v0) => m switch
    {
        SkillMove.DragBack => 3.2,
        SkillMove.CruyffTurn => 4.2,
        SkillMove.FakeShot => 4.4,
        SkillMove.Roulette => 4.8,
        SkillMove.HeelToHeel => Math.Max(v0, 5) + 1.5,
        SkillMove.Rainbow => Math.Max(v0, 5),
        SkillMove.Croqueta => Math.Max(v0, 4) + 0.6,
        SkillMove.RonaldoChop => Math.Max(v0, 4.5) * 0.85 + 0.5,
        SkillMove.McGeadySpin => 5,
        SkillMove.Sombrero => Math.Max(v0, 4) + 0.5,
        SkillMove.Panna => Math.Max(v0, 4.5) + 0.8,
        SkillMove.StepOverStorm => 6.2,
        _ => Math.Max(v0, 4.5) + 0.6,
    };

    static double S(double a, double b, double t) => M.Smoothstep(a, b, t);
    /// <summary>0 → 1 → 0 over [a, b].</summary>
    static double Bump(double a, double b, double t) => t <= a || t >= b ? 0 : Math.Sin((t - a) / (b - a) * Math.PI);

    /// <summary>
    /// The script at time t (s) for a man who came in at speed v0: the body's velocity (f, l·e),
    /// how far his body has turned (yaw, radians toward the exit side), and where the ball sits
    /// on his foot relative to him (f, l·e in the move's frame, and height). Only up to Release
    /// for the ball; the body's velocity after the release is the exit run.
    /// </summary>
    public static void Script(SkillMove m, double t, double v0, out double bodyF, out double bodyL, out double yaw,
        out double ballF, out double ballL, out double ballY)
    {
        double r = BallK.Radius;
        ballY = r;
        yaw = 0;
        switch (m)
        {
            case SkillMove.BodyFeint:
                // Drops the shoulder and steps toward the dummy side, then pushes off the other way.
                bodyF = v0 * 0.75;
                bodyL = -1.7 * Bump(0, 0.32, t) + 2.4 * S(0.28, 0.42, t);
                yaw = -0.28 * Bump(0, 0.32, t) + 0.3 * S(0.3, 0.45, t);
                ballF = 0.5;
                ballL = -0.1 * Bump(0, 0.32, t);
                break;
            case SkillMove.StepOver:
                // The far foot circles over the ball from inside to out, the body dipping with it;
                // the ball rolls on and the outside of the other foot takes it away.
                bodyF = v0 * 0.7 + 0.4;
                bodyL = -1.3 * Bump(0.04, 0.4, t) + 1.8 * S(0.36, 0.48, t);
                yaw = -0.32 * Bump(0.04, 0.4, t) + 0.25 * S(0.36, 0.5, t);
                ballF = 0.52;
                ballL = 0;
                break;
            case SkillMove.DragBack:
                // Foot up on the ball, he stops it dead, rolls it back under him and turns away.
                bodyF = v0 * (1 - S(0, 0.22, t)) - 1.4 * S(0.32, 0.5, t);
                bodyL = 0.4 * Bump(0.3, 0.6, t);
                yaw = Math.PI * S(0.24, 0.6, t);
                ballF = 0.34 - 0.78 * S(0.16, 0.46, t);
                ballL = 0.12 * Bump(0.16, 0.5, t);
                break;
            case SkillMove.CruyffTurn:
                // Shapes to pass or cross, then drags it back behind his standing leg with the
                // inside of the foot and spins away with it.
                bodyF = v0 * (1 - 0.85 * S(0, 0.3, t)) - 1.2 * S(0.36, 0.52, t);
                bodyL = 0.5 * Bump(0.3, 0.62, t);
                yaw = Math.PI * S(0.3, 0.6, t);
                ballF = 0.5 - 0.82 * S(0.24, 0.44, t);
                ballL = 0.24 * S(0.24, 0.4, t);
                break;
            case SkillMove.FakeShot:
                // Winds up as if to strike, the leg comes down over the ball instead and the inside of
                // the foot pulls it across his body.
                bodyF = v0 * (1 - 0.75 * S(0, 0.26, t));
                bodyL = 1.6 * S(0.32, 0.46, t);
                yaw = 0.7 * S(0.3, 0.5, t);
                ballF = 0.55 - 0.1 * S(0.28, 0.44, t);
                ballL = 0.46 * S(0.28, 0.44, t);
                break;
            case SkillMove.Roulette:
                // Sole on the ball, drag it back as he spins, the other sole takes it on round: a
                // full turn with his back to his man, ending past him on the exit side.
                bodyF = v0 * 0.35 * (1 - S(0, 0.2, t)) + 0.8 * S(0.6, 0.75, t);
                bodyL = 1.9 * S(0.05, 0.2, t);
                yaw = 2 * Math.PI * S(0.06, 0.74, t);
                ballF = 0.3 * Math.Cos(yaw) + 0.08;
                ballL = 0.3 * Math.Sin(yaw) + 0.12 * S(0.1, 0.5, t);
                break;
            case SkillMove.Croqueta:
                // Inside of one foot to the inside of the other, quick as a blink, and a side-step.
                bodyF = v0 * 0.8;
                bodyL = 3.2 * S(0, 0.1, t) * (1 - S(0.24, 0.42, t));
                yaw = 0.15 * Bump(0, 0.3, t);
                ballF = 0.42;
                ballL = -0.12 + 0.36 * S(0.03, 0.17, t);
                break;
            case SkillMove.HeelToHeel:
                // Front heel rolls it back, the other heel flicks it on past his man; then he goes.
                bodyF = v0 * 0.85 + 0.8 * S(0.2, 0.35, t);
                bodyL = 0.6 * Bump(0.05, 0.3, t);
                yaw = 0.12 * Bump(0.05, 0.3, t);
                ballF = 0.45 - 0.5 * S(0, 0.13, t) + 0.25 * S(0.16, 0.27, t);
                ballL = 0.14 * S(0.08, 0.2, t);
                break;
            case SkillMove.Elastico:
                // Outside of the foot pushes it the dummy way, the same foot snaps it back inside.
                bodyF = v0 * 0.65 + 0.3;
                bodyL = -1.1 * Bump(0, 0.24, t) + 3 * S(0.22, 0.36, t);
                yaw = -0.3 * Bump(0, 0.24, t) + 0.35 * S(0.24, 0.4, t);
                ballF = 0.5;
                ballL = -0.32 * S(0.03, 0.17, t) * (1 - S(0.19, 0.31, t)) + 0.3 * S(0.19, 0.31, t);
                break;
            case SkillMove.Rainbow:
                // Rolls it up the back of the standing leg with the other foot and flicks it over
                // his own head and his man's with the heel.
                bodyF = v0 * 0.85 + 0.3;
                bodyL = 0;
                ballF = 0.45 - 0.62 * S(0, 0.2, t);
                ballL = 0.06 * S(0, 0.2, t);
                ballY = r + 0.16 * S(0.14, 0.3, t);
                break;
            case SkillMove.RonaldoChop:
                // On the run, the far foot reaches behind the standing leg and chops it square
                // across him; he checks and cuts away with it.
                bodyF = v0 * (1 - 0.6 * S(0.05, 0.3, t));
                bodyL = 2.6 * S(0.24, 0.4, t);
                yaw = 1.2 * S(0.22, 0.42, t);
                ballF = 0.5 - 0.32 * S(0.12, 0.3, t);
                ballL = -0.06 * S(0, 0.12, t) + 0.36 * S(0.16, 0.3, t);
                break;
            case SkillMove.McGeadySpin:
                // The sole drags it across him while he spins right round the other way, and he
                // comes out of the turn already running.
                bodyF = v0 * 0.4 * (1 - S(0, 0.2, t)) + 0.9 * S(0.55, 0.7, t);
                bodyL = 2.2 * S(0.1, 0.6, t);
                yaw = -2 * Math.PI * S(0.08, 0.68, t);
                ballF = 0.38 - 0.24 * S(0.04, 0.24, t);
                ballL = 0.3 * S(0.06, 0.3, t);
                break;
            case SkillMove.HocusPocus:
                // A lean the dummy way, then the foot that's behind flicks it across behind his
                // standing leg and he's off the other way.
                bodyF = v0 * 0.8;
                bodyL = -0.9 * Bump(0, 0.2, t) + 2.3 * S(0.18, 0.32, t);
                yaw = -0.22 * Bump(0, 0.2, t) + 0.3 * S(0.2, 0.36, t);
                ballF = 0.42 - 0.24 * S(0, 0.12, t);
                ballL = -0.1 * S(0, 0.1, t) + 0.44 * S(0.1, 0.22, t);
                break;
            case SkillMove.StepOverStorm:
            {
                // Three step overs, one foot then the other, the body swaying with each; then
                // the outside of the foot and a burst.
                double sway = Math.Sin(Math.Clamp((t - 0.04) / 0.78, 0, 1) * 3 * Math.PI);
                bodyF = v0 * 0.55 + 0.3;
                bodyL = -0.9 * sway * (1 - S(0.7, 0.8, t)) + 2.2 * S(0.78, 0.9, t);
                yaw = -0.24 * sway * (1 - S(0.7, 0.8, t)) + 0.3 * S(0.78, 0.92, t);
                ballF = 0.52;
                ballL = 0;
                break;
            }
            case SkillMove.Sombrero:
                // He checks, gets the toe under it and scoops it up over the head of the man in
                // front; then runs round him to meet it.
                bodyF = v0 * (1 - 0.5 * S(0, 0.2, t));
                bodyL = 0;
                ballF = 0.5 - 0.1 * S(0, 0.15, t);
                ballL = 0;
                ballY = r + 0.1 * S(0.16, 0.3, t);
                break;
            case SkillMove.Panna:
                // Straight at his man, the ball slipped through his legs, and round him the other
                // side to pick it up.
                bodyF = Math.Max(v0, 3.5) * (1 - 0.25 * Bump(0.15, 0.6, t));
                bodyL = 2.1 * Bump(0.12, 0.72, t);
                yaw = 0.4 * Bump(0.12, 0.42, t) - 0.35 * Bump(0.42, 0.76, t);
                ballF = 0.5;
                ballL = 0;
                break;
            default:
                bodyF = v0;
                bodyL = 0;
                ballF = 0.5;
                ballL = 0;
                break;
        }
    }

    /// <summary>
    /// The move he does: by where the stick points against the way he faces (forward, to a side,
    /// back, or left alone), the best move he knows for it, and the situation (a turn at a walk is
    /// a drag back, on the run a Cruyff; the rainbow needs a run up). Mostly his best one, now and
    /// then a simpler one, so it doesn't become a single trick.
    /// </summary>
    public static SkillMove Pick(int stars, bool idle, double fwd, double v0, double roll)
    {
        int top = TopTier(stars);
        SkillMove[] options;
        if (idle) options = new[] { SkillMove.FakeShot };
        else if (fwd > 0.77) options = new[] { SkillMove.Rainbow, SkillMove.HeelToHeel, SkillMove.StepOver };
        else if (fwd < -0.64) options = new[] { SkillMove.Roulette, v0 >= 3.2 ? SkillMove.CruyffTurn : SkillMove.DragBack };
        else options = new[] { SkillMove.Elastico, SkillMove.Croqueta, roll < 0.5 ? SkillMove.BodyFeint : SkillMove.StepOver };
        double keep = roll;
        for (int i = 0; i < options.Length; i++)
        {
            var o = options[i];
            if (Tier(o) > top) continue;
            if (o == SkillMove.Rainbow && v0 < 3) continue;
            if (i == options.Length - 1) return o;
            // Two in three times his best; otherwise one down.
            if ((keep * 7.31) % 1 < 0.67) return o;
            keep = (keep * 3.17 + 0.29) % 1;
        }
        return options[^1];
    }
}
