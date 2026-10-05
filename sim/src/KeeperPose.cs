namespace GameNight.Sim;

/// <summary>
/// Goalkeeper dive pose, shared by the physics (hand/body capsule) and the renderer (body
/// roll and lift), so what you see is exactly what can stop the ball.
///
/// The keeper rotates about his feet: Roll is the body's tilt from vertical toward the dive
/// side, Lift raises the feet off the ground. A point at distance L along the body axis sits
/// at height lift + L·cos(roll), and L·sin(roll) to the side.
/// </summary>
public sealed class DivePose
{
    public double Roll, Lift;
}

public static class KeeperPose
{
    /// <summary>Distance along the body axis from feet to outstretched hands, per metre of height scale.</summary>
    public const double DiveHands = 2.15;
    public const double DiveHips = 0.55;
    public const double DiveRadius = 0.34;

    /// <summary>Chooses the dive (roll, lift) whose body line passes through a ball at lateral offset a, height y.</summary>
    public static (double roll, double lift, double reach) PlanDive(double a, double y, double height)
    {
        double lift = M.Clamp(y - 1.9, 0, 0.5);
        double up = System.Math.Max(0.08, y - lift);
        double L = 1.85 * height; // aim the forearms, not the fingertips
        double reach = System.Math.Sqrt(System.Math.Max(0, L * L - up * up));
        double lateral = System.Math.Min(a, reach);
        double roll = M.Clamp(JsMath.Atan2(System.Math.Max(0.2, lateral), up), 0.35, 1.5);
        return (roll, lift, reach);
    }

    /// <summary>
    /// The dive's timeline, in seconds from take-off (`fly` = when he comes down): push off and
    /// stretch out (by the time the ball gets there: 0.12 to 0.28 s), fly, land on his side,
    /// lie a moment, then get back up.
    /// </summary>
    public static double ReachTime(double fly) => M.Clamp(fly - 0.15, 0.12, 0.28);

    public static void Timeline(double t, double fly, out double reachOut, out double land, out double getUp)
    {
        reachOut = M.Smoothstep(0.02, ReachTime(fly), t);
        land = M.Smoothstep(fly - 0.04, fly + 0.18, t);
        getUp = M.Smoothstep(fly + 0.5, fly + 0.85, t);
    }

    public static DivePose Pose(Player k, double targetRoll, double targetLift, DivePose output)
    {
        Timeline(k.ActionT, k.DiveFly, out double reachOut, out double land, out double getUp);
        output.Roll = M.Lerp(M.Lerp(targetRoll * reachOut, System.Math.Max(targetRoll, 1.5), land), 0, getUp);
        output.Lift = targetLift * reachOut * (1 - land);
        return output;
    }
}
