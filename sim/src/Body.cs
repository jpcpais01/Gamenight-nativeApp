namespace GameNight.Sim;

/// <summary>
/// Body types. Every player has one of five, read from his real height, weight and strength
/// (so a 1.95 m 90 kg striker looks like one), and a body shape from it: limb lengths and
/// thicknesses, shoulders, chest, neck — with a little personal variation so no two players
/// are cut from the same mould. Only the look uses the shape; the simulation keeps using the
/// real height and weight.
/// </summary>
public enum BodyType { Lean = 0, Athletic = 1, Muscular = 2, Stocky = 3, Lanky = 4 }

/// <summary>Proportions relative to the base (athletic) body.</summary>
public sealed class BodyShape
{
    public BodyType Type;
    /// <summary>Leg length (thigh + shin).</summary>
    public double Leg;
    public double ArmLen;
    /// <summary>Torso width (and the pelvis), depth (chest), length.</summary>
    public double TorsoW, TorsoD, TorsoL;
    /// <summary>Shoulder spread.</summary>
    public double Shoulder;
    /// <summary>Girths: upper and lower arm, thigh, calf, neck.</summary>
    public double Arm, Thigh, Calf, Neck;
    public double NeckLen;
}

public static class Body
{
    public static readonly string[] Names = { "Lean", "Athletic", "Muscular", "Stocky", "Tall & lanky" };

    /// <summary>Weight for height of a typical footballer (BMI), the middle the types are read from.</summary>
    const double BmiMid = 23.4;

    public static BodyType TypeOf(double heightM, double weightKg, double strength)
    {
        double heavy = weightKg / (heightM * heightM) - BmiMid;
        if (heightM >= 1.87 && heavy < 0.6) return BodyType.Lanky;
        if (heightM < 1.78 && heavy > 0.5) return BodyType.Stocky;
        if (heavy > 0.9 || (heavy > 0.3 && strength > 0.72)) return BodyType.Muscular;
        if (heavy < -0.75) return BodyType.Lean;
        return BodyType.Athletic;
    }

    // leg, armLen, torsoW, torsoD, torsoL, shoulder, arm, thigh, calf, neck, neckLen
    static readonly double[][] Shapes =
    {
        new[] { 1.035, 1.02, 0.88, 0.88, 1.0, 0.93, 0.84, 0.85, 0.88, 0.9, 1.05 },
        new[] { 1.0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
        new[] { 1.0, 1, 1.12, 1.14, 1.0, 1.1, 1.24, 1.16, 1.1, 1.22, 0.94 },
        new[] { 0.92, 0.96, 1.13, 1.16, 0.97, 1.05, 1.14, 1.2, 1.16, 1.2, 0.85 },
        new[] { 1.07, 1.06, 0.92, 0.9, 1.02, 0.97, 0.9, 0.9, 0.9, 0.92, 1.12 },
    };

    /// <summary>Stable 0..1 per player and channel.</summary>
    static double Vary(double seed, double k)
    {
        double s = JsMath.Sin(seed * 91.7 + k * 47.3) * 43758.5453;
        return s - System.Math.Floor(s);
    }

    public static BodyShape Shape(double heightM, double weightKg, double strength, double seed)
    {
        var type = TypeOf(heightM, weightKg, strength);
        var b = Shapes[(int)type];
        double bmi = weightKg / (heightM * heightM);
        double mass = System.Math.Max(-0.05, System.Math.Min(0.05, (bmi - BmiMid) * 0.012));
        double J(int k, double amt) => 1 + (Vary(seed, k) - 0.5) * 2 * amt;
        return new BodyShape
        {
            Type = type,
            Leg = b[0] * J(1, 0.02),
            ArmLen = b[1] * J(2, 0.02),
            TorsoW = b[2] * J(3, 0.03) * (1 + mass),
            TorsoD = b[3] * J(4, 0.03) * (1 + mass),
            TorsoL = b[4] * J(5, 0.02),
            Shoulder = b[5] * J(6, 0.025),
            Arm = b[6] * J(7, 0.04),
            Thigh = b[7] * J(8, 0.04) * (1 + mass * 0.8),
            Calf = b[8] * J(9, 0.04),
            Neck = b[9] * J(10, 0.04),
            NeckLen = b[10] * J(11, 0.04),
        };
    }
}
