using System.Runtime.CompilerServices;

namespace GameNight.Sim;

/// <summary>Small mutable vector (a reference type, as in the PWA: players and the ball share
/// and alias these, and the sim never allocates them inside the hot loop).</summary>
public sealed class V3
{
    public double X, Y, Z;

    public V3() { }

    public V3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public V3 Set(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
        return this;
    }

    public V3 Copy(V3 v)
    {
        X = v.X;
        Y = v.Y;
        Z = v.Z;
        return this;
    }

    public V3 Clone() => new V3(X, Y, Z);

    public V3 Add(V3 v)
    {
        X += v.X;
        Y += v.Y;
        Z += v.Z;
        return this;
    }

    public V3 AddScaled(V3 v, double s)
    {
        X += v.X * s;
        Y += v.Y * s;
        Z += v.Z * s;
        return this;
    }

    public V3 Sub(V3 v)
    {
        X -= v.X;
        Y -= v.Y;
        Z -= v.Z;
        return this;
    }

    public V3 Scale(double s)
    {
        X *= s;
        Y *= s;
        Z *= s;
        return this;
    }

    public double Len() => System.Math.Sqrt(X * X + Y * Y + Z * Z);

    public double LenXZ() => System.Math.Sqrt(X * X + Z * Z);

    public double Dot(V3 v) => X * v.X + Y * v.Y + Z * v.Z;

    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}

/// <summary>A point on the grass.</summary>
public sealed class XZ
{
    public double X, Z;

    public XZ(double x, double z)
    {
        X = x;
        Z = z;
    }
}

public static class M
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static double Smoothstep(double e0, double e1, double x)
    {
        double t = Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Shortest signed angle from a to b.</summary>
    public static double AngleDiff(double a, double b)
    {
        double d = b - a;
        while (d > System.Math.PI) d -= System.Math.PI * 2;
        while (d < -System.Math.PI) d += System.Math.PI * 2;
        return d;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Dist2D(double ax, double az, double bx, double bz)
    {
        double dx = bx - ax;
        double dz = bz - az;
        return System.Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>JS ToInt32 (as `x | 0`).</summary>
    public static int ToInt32(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) return 0;
        double t = System.Math.Truncate(x);
        double m = t % 4294967296.0;
        if (m < 0) m += 4294967296.0;
        return unchecked((int)(uint)m);
    }

    /// <summary>JS ToUint32 (as `x >>> 0`).</summary>
    public static uint ToUint32(double x) => unchecked((uint)ToInt32(x));

    /// <summary>JS Array.prototype.sort: stable (the comparator returns a number; &lt; 0 keeps a first).</summary>
    public static void StableSort<T>(System.Collections.Generic.List<T> list, System.Func<T, T, double> cmp)
    {
        // Binary insertion sort: stable, and the lists here are a dozen long at most.
        for (int i = 1; i < list.Count; i++)
        {
            T x = list[i];
            int lo = 0, hi = i;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (cmp(x, list[mid]) < 0) hi = mid;
                else lo = mid + 1;
            }
            for (int j = i; j > lo; j--) list[j] = list[j - 1];
            list[lo] = x;
        }
    }
}

/// <summary>Deterministic PRNG (mulberry32), exactly as the PWA's: same seed, same match.</summary>
public sealed class Rng
{
    // The PWA adds to a JS number without wrapping it; only its low 32 bits ever matter.
    double s;

    /// <summary>The raw state (for parity checks against the PWA).</summary>
    public double State => s;

    public Rng(double seed = 1234567)
    {
        s = M.ToUint32(seed);
    }

    public double Next()
    {
        s += 0x6d2b79f5;
        uint t = M.ToUint32(s);
        unchecked
        {
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + (t ^ (t >> 7)) * (t | 61);
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    public double Range(double a, double b) => a + (b - a) * Next();

    /// <summary>Approximately normal, mean 0, sd 1.</summary>
    public double Gauss() => (Next() + Next() + Next() + Next() - 2) * 1.7320508;
}
