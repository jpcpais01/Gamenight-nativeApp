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

    /// <summary>Facial hair, fixed per person (keyed on the name so it's the same on his card
    /// and on the pitch, every match): 0 clean-shaven, 1 stubble, 2 a beard, 3 moustache and goatee.</summary>
    public static int FacialHair(string name, int fallback = 0)
    {
        uint h = 2166136261;
        if (string.IsNullOrEmpty(name)) h ^= (uint)fallback * 2654435761u;
        else foreach (char ch in name) h = (h ^ ch) * 16777619;
        h ^= h >> 13;
        double r = (h % 1000) / 1000.0;
        return r < 0.5 ? 0 : r < 0.74 ? 1 : r < 0.9 ? 2 : 3;
    }

    /// <summary>A person's head, fixed by his name like his facial hair: overall size and
    /// proportions (multipliers near 1), and the face's build (each -1..1): jaw width, chin
    /// length, cheekbones, nose, brow.</summary>
    public readonly record struct Head(double Size, double Width, double Height, double Depth,
        double Jaw, double Chin, double Cheek, double Nose, double Brow);

    // Base shapes: width, height, depth, jaw, chin, cheek.
    static readonly double[][] HeadTypes =
    {
        new[] { 1.0, 1.0, 1.0, 0.0, 0.0, 0.0 },     // oval
        new[] { 1.05, 0.96, 1.0, 0.2, -0.5, 0.4 },  // round
        new[] { 1.03, 0.99, 1.0, 0.9, 0.0, 0.0 },   // square
        new[] { 0.95, 1.06, 1.01, -0.2, 0.7, -0.2 },// long
        new[] { 1.0, 1.0, 1.0, -0.8, 0.4, 0.7 },    // heart (wide cheekbones, narrow chin)
        new[] { 0.98, 1.0, 1.02, 1.0, -0.2, -0.5 }, // pear (wide jaw)
    };

    public static Head HeadOf(string name, int fallback = 0)
    {
        uint h = 2166136261;
        if (string.IsNullOrEmpty(name)) h ^= (uint)fallback * 2246822519u;
        else foreach (char ch in name) h = (h ^ ch) * 16777619;
        double R()
        {
            h ^= h << 13;
            h ^= h >> 17;
            h ^= h << 5;
            return (h % 10007) / 10007.0;
        }
        double J(double amt) => (R() - 0.5) * 2 * amt;
        var t = HeadTypes[(int)(R() * HeadTypes.Length) % HeadTypes.Length];
        return new Head(
            Size: 1 + J(0.055),
            Width: t[0] + J(0.03),
            Height: t[1] + J(0.03),
            Depth: t[2] + J(0.03),
            Jaw: System.Math.Clamp(t[3] + J(0.35), -1, 1),
            Chin: System.Math.Clamp(t[4] + J(0.4), -1, 1),
            Cheek: System.Math.Clamp(t[5] + J(0.35), -1, 1),
            Nose: J(1),
            Brow: J(1));
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
