namespace GameNight.Sim;

// World units are metres and seconds. X runs along the pitch length, Z across it, Y is up.

public static class Tick
{
    public const int Hz = 120;
    public const double DT = 1.0 / Hz;
}

public static class Pitch
{
    public const double Length = 105;
    public const double Width = 68;
    public const double HalfL = 52.5;
    public const double HalfW = 34;
    public const double GoalHalfWidth = 7.32 / 2;
    public const double GoalHeight = 2.44;
    public const double GoalDepth = 2.0;
    /// <summary>Net roof runs flat this far back, then the back of the net slopes down to the ground.</summary>
    public const double GoalRoofDepth = 1.0;
    public const double PostRadius = 0.06;
    public const double BoxDepth = 16.5;
    public const double BoxHalfWidth = 20.16;
    public const double SixDepth = 5.5;
    public const double SixHalfWidth = 9.16;
    public const double PenaltySpot = 11;
    public const double CircleRadius = 9.15;
}

public static class Physics
{
    public const double Gravity = 9.81;
    public const double AirDensity = 1.2;
}

public static class BallK
{
    public const double Radius = 0.11;
    public const double Mass = 0.43;
    /// <summary>Thin shell: I = 2/3 m r^2</summary>
    public const double InertiaFactor = 2.0 / 3;
    public const double Area = System.Math.PI * 0.11 * 0.11;
    // Drag coefficient below / above the drag crisis
    public const double CdLow = 0.47;
    public const double CdHigh = 0.18;
    public const double CrisisSpeed = 14;
    public const double CrisisWidth = 3;
    /// <summary>Air spin decay time constant (s)</summary>
    public const double SpinDecay = 7;
    public const double Restitution = 0.62;
    public const double GroundFriction = 0.55;
    /// <summary>Rolling resistance on grass (m/s^2). Air drag is applied on top.</summary>
    public const double RollDecel = 0.75;
    public const double PostRestitution = 0.65;
}

public static class PlayerK
{
    public const double Radius = 0.32;
    public const double Height = 1.8;
    public const double Reach = 0.85;
    public const double ControlHeight = 1.05;
    public const double HeadMin = 1.35;
    public const double HeadMax = 2.25;
    public const double TopSpeed = 8.6;
    public const double JogSpeed = 5.6;
    public const double Accel = 6.5;
    public const double Brake = 9.2;
    public const double Lateral = 9.8;
    public const double DribbleSpeedFactor = 0.9;
}

public static class MatchK
{
    /// <summary>Real seconds per half.</summary>
    public const double HalfSeconds = 150;
}

/// <summary>
/// After a goal (seconds into the Goal phase): the camera follows the scorer's run, swings
/// round in front of him for his celebration (Front → Crowd), then turns to the crowd —
/// while it's away the players are brought most of the way back (Cut) — and returns to the
/// field (Back) as they jog into their kick-off spots. For the first Steer seconds of your
/// own goals the stick steers the scorer's run.
/// </summary>
public static class GoalSeq
{
    public const double Steer = 2, Front = 2.6, Crowd = 6.6, Cut = 7.5, Back = 8.6, End = 11.6;
}
