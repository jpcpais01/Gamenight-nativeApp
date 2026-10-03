using System;
using System.Runtime.CompilerServices;

namespace GameNight.Sim;

/// <summary>
/// The JavaScript engine's maths, bit for bit. The PWA's sim runs on V8, whose Math.sin,
/// cos, atan2, exp and pow are fdlibm ports, and whose Math.hypot normalises and sums with
/// Kahan compensation. .NET calls the platform libm instead (glibc on Linux, bionic on
/// Android), which rounds differently in the last bit often enough that a seeded match
/// drifts apart within seconds. Using these everywhere keeps the port step-for-step equal
/// to the PWA, and makes the native sim give the same result on every device.
/// Math.Sqrt, Floor, Ceiling, Abs, Min, Max are exact in both and are used directly.
/// </summary>
public static class JsMath
{
    public const double PI = Math.PI;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Hi(double x) => (int)(BitConverter.DoubleToInt64Bits(x) >> 32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint Lo(double x) => (uint)BitConverter.DoubleToInt64Bits(x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double Words(int hi, uint lo) => BitConverter.Int64BitsToDouble(((long)hi << 32) | lo);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double WithLo(double x, uint lo) => Words(Hi(x), lo);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double WithHi(double x, int hi) => Words(hi, Lo(x));

    // ------------------------------------------------------------------ small JS semantics

    /// <summary>Math.sign: -1, 0 (keeping -0) or 1; NaN stays NaN.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Sign(double x) => x > 0 ? 1 : x < 0 ? -1 : x;

    /// <summary>`x || 1` for a number: 0, -0 and NaN become 1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Or1(double x) => x == 0 || double.IsNaN(x) ? 1 : x;

    /// <summary>Math.round: halves round up (toward +infinity).</summary>
    public static double Round(double x)
    {
        double c = Math.Ceiling(x);
        return c - 0.5 > x ? c - 1 : c;
    }

    /// <summary>Math.min over two numbers (NaN wins, as in JS).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Min(double a, double b) => Math.Min(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Max(double a, double b) => Math.Max(a, b);

    /// <summary>Math.hypot of two numbers, as V8 computes it.</summary>
    public static double Hypot(double a, double b)
    {
        if (double.IsInfinity(a) || double.IsInfinity(b)) return double.PositiveInfinity;
        if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
        a = Math.Abs(a);
        b = Math.Abs(b);
        double max = a > b ? a : b;
        if (max == 0) return 0;
        double sum = 0, comp = 0;
        double n = a / max;
        double summand = n * n - comp;
        double pre = sum + summand;
        comp = (pre - sum) - summand;
        sum = pre;
        n = b / max;
        summand = n * n - comp;
        pre = sum + summand;
        sum = pre;
        return Math.Sqrt(sum) * max;
    }

    /// <summary>Math.hypot of three numbers, as V8 computes it.</summary>
    public static double Hypot(double a, double b, double c)
    {
        if (double.IsInfinity(a) || double.IsInfinity(b) || double.IsInfinity(c)) return double.PositiveInfinity;
        if (double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c)) return double.NaN;
        a = Math.Abs(a);
        b = Math.Abs(b);
        c = Math.Abs(c);
        double max = a;
        if (b > max) max = b;
        if (c > max) max = c;
        if (max == 0) return 0;
        double sum = 0, comp = 0;
        double n = a / max, summand = n * n - comp, pre = sum + summand;
        comp = (pre - sum) - summand;
        sum = pre;
        n = b / max;
        summand = n * n - comp;
        pre = sum + summand;
        comp = (pre - sum) - summand;
        sum = pre;
        n = c / max;
        summand = n * n - comp;
        pre = sum + summand;
        sum = pre;
        return Math.Sqrt(sum) * max;
    }

    // ------------------------------------------------------------------ sin / cos (fdlibm)

    const double S1 = -1.66666666666666324348e-01, S2 = 8.33333333332248946124e-03, S3 = -1.98412698298579493134e-04,
        S4 = 2.75573137070700676789e-06, S5 = -2.50507602534068634195e-08, S6 = 1.58969099521155010221e-10;

    static double KSin(double x, double y, int iy)
    {
        int ix = Hi(x) & 0x7fffffff;
        if (ix < 0x3e400000 && (int)x == 0) return x;
        double z = x * x;
        double v = z * x;
        double r = S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)));
        if (iy == 0) return x + v * (S1 + z * r);
        return x - ((z * (0.5 * y - v * r) - y) - v * S1);
    }

    const double C1 = 4.16666666666666019037e-02, C2 = -1.38888888888741095749e-03, C3 = 2.48015872894767294178e-05,
        C4 = -2.75573143513906633035e-07, C5 = 2.08757232129817482790e-09, C6 = -1.13596475577881948265e-11;

    static double KCos(double x, double y)
    {
        int ix = Hi(x) & 0x7fffffff;
        if (ix < 0x3e400000 && (int)x == 0) return 1.0;
        double z = x * x;
        double r = z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
        if (ix < 0x3fd33333) return 1.0 - (0.5 * z - (z * r - x * y));
        double qx = ix > 0x3fe90000 ? 0.28125 : Words(ix - 0x00200000, 0);
        double iz = 0.5 * z - qx;
        double a = 1.0 - qx;
        return a - (iz - (z * r - x * y));
    }

    static readonly int[] Npio2Hw =
    {
        0x3FF921FB, 0x400921FB, 0x4012D97C, 0x401921FB, 0x401F6A7A, 0x4022D97C, 0x4025FDBB, 0x402921FB,
        0x402C463A, 0x402F6A7A, 0x4031475C, 0x4032D97C, 0x40346B9C, 0x4035FDBB, 0x40378FDB, 0x403921FB,
        0x403AB41B, 0x403C463A, 0x403DD85A, 0x403F6A7A, 0x40407E4C, 0x4041475C, 0x4042106C, 0x4042D97C,
        0x4043A28C, 0x40446B9C, 0x404534AC, 0x4045FDBB, 0x4046C6CB, 0x40478FDB, 0x404858EB, 0x404921FB,
    };

    const double Invpio2 = 6.36619772367581382433e-01, Pio2_1 = 1.57079632673412561417e+00, Pio2_1t = 6.07710050650619224932e-11,
        Pio2_2 = 6.07710050630396597660e-11, Pio2_2t = 2.02226624879595063154e-21, Pio2_3 = 2.02226624871116645580e-21,
        Pio2_3t = 8.47842766036889956997e-32;

    /// <summary>x reduced by multiples of pi/2: returns n, and x - n pi/2 as y0 + y1. Args beyond
    /// 2^19 pi/2 (never reached by a match) fall back to the platform.</summary>
    static int RemPio2(double x, out double y0, out double y1)
    {
        int hx = Hi(x);
        int ix = hx & 0x7fffffff;
        double z;
        if (ix < 0x4002D97C)
        {
            if (hx > 0)
            {
                z = x - Pio2_1;
                if (ix != 0x3FF921FB)
                {
                    y0 = z - Pio2_1t;
                    y1 = (z - y0) - Pio2_1t;
                }
                else
                {
                    z -= Pio2_2;
                    y0 = z - Pio2_2t;
                    y1 = (z - y0) - Pio2_2t;
                }
                return 1;
            }
            z = x + Pio2_1;
            if (ix != 0x3FF921FB)
            {
                y0 = z + Pio2_1t;
                y1 = (z - y0) + Pio2_1t;
            }
            else
            {
                z += Pio2_2;
                y0 = z + Pio2_2t;
                y1 = (z - y0) + Pio2_2t;
            }
            return -1;
        }
        if (ix <= 0x413921FB)
        {
            double t = Math.Abs(x);
            int n = (int)(t * Invpio2 + 0.5);
            double fn = n;
            double r = t - fn * Pio2_1;
            double w = fn * Pio2_1t;
            if (n < 32 && ix != Npio2Hw[n - 1])
            {
                y0 = r - w;
            }
            else
            {
                int j = ix >> 20;
                y0 = r - w;
                int i = j - ((Hi(y0) >> 20) & 0x7ff);
                if (i > 16)
                {
                    t = r;
                    w = fn * Pio2_2;
                    r = t - w;
                    w = fn * Pio2_2t - ((t - r) - w);
                    y0 = r - w;
                    i = j - ((Hi(y0) >> 20) & 0x7ff);
                    if (i > 49)
                    {
                        t = r;
                        w = fn * Pio2_3;
                        r = t - w;
                        w = fn * Pio2_3t - ((t - r) - w);
                        y0 = r - w;
                    }
                }
            }
            y1 = (r - y0) - w;
            if (hx < 0)
            {
                y0 = -y0;
                y1 = -y1;
                return -n;
            }
            return n;
        }
        // Huge argument: platform reduction (a match never gets here).
        double q = Math.Round(x / (Math.PI / 2));
        y0 = Math.IEEERemainder(x, Math.PI / 2);
        y1 = 0;
        return (int)(long)q;
    }

    public static double Sin(double x)
    {
        int ix = Hi(x) & 0x7fffffff;
        if (ix <= 0x3fe921fb) return KSin(x, 0, 0);
        if (ix >= 0x7ff00000) return x - x;
        int n = RemPio2(x, out double y0, out double y1);
        switch (n & 3)
        {
            case 0: return KSin(y0, y1, 1);
            case 1: return KCos(y0, y1);
            case 2: return -KSin(y0, y1, 1);
            default: return -KCos(y0, y1);
        }
    }

    public static double Cos(double x)
    {
        int ix = Hi(x) & 0x7fffffff;
        if (ix <= 0x3fe921fb) return KCos(x, 0);
        if (ix >= 0x7ff00000) return x - x;
        int n = RemPio2(x, out double y0, out double y1);
        switch (n & 3)
        {
            case 0: return KCos(y0, y1);
            case 1: return -KSin(y0, y1, 1);
            case 2: return -KCos(y0, y1);
            default: return KSin(y0, y1, 1);
        }
    }

    // ------------------------------------------------------------------ atan / atan2 (fdlibm)

    static readonly double[] AtanHi = { 4.63647609000806093515e-01, 7.85398163397448278999e-01, 9.82793723247329054082e-01, 1.57079632679489655800e+00 };
    static readonly double[] AtanLo = { 2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17 };
    const double AT0 = 3.33333333333329318027e-01, AT1 = -1.99999999998764832476e-01, AT2 = 1.42857142725034663711e-01,
        AT3 = -1.11111104054623557880e-01, AT4 = 9.09088713343650656196e-02, AT5 = -7.69187620504482999495e-02,
        AT6 = 6.66107313738753120669e-02, AT7 = -5.83357013379057348645e-02, AT8 = 4.97687799461593236017e-02,
        AT9 = -3.65315727442169155270e-02, AT10 = 1.62858201153657823623e-02;

    public static double Atan(double x)
    {
        int hx = Hi(x);
        int ix = hx & 0x7fffffff;
        int id;
        if (ix >= 0x44100000)
        {
            if (ix > 0x7ff00000 || (ix == 0x7ff00000 && Lo(x) != 0)) return x + x;
            return hx > 0 ? AtanHi[3] + AtanLo[3] : -AtanHi[3] - AtanLo[3];
        }
        if (ix < 0x3fdc0000)
        {
            if (ix < 0x3e400000) return x;
            id = -1;
        }
        else
        {
            x = Math.Abs(x);
            if (ix < 0x3ff30000)
            {
                if (ix < 0x3fe60000)
                {
                    id = 0;
                    x = (2.0 * x - 1.0) / (2.0 + x);
                }
                else
                {
                    id = 1;
                    x = (x - 1.0) / (x + 1.0);
                }
            }
            else if (ix < 0x40038000)
            {
                id = 2;
                x = (x - 1.5) / (1.0 + 1.5 * x);
            }
            else
            {
                id = 3;
                x = -1.0 / x;
            }
        }
        double z = x * x;
        double w = z * z;
        double s1 = z * (AT0 + w * (AT2 + w * (AT4 + w * (AT6 + w * (AT8 + w * AT10)))));
        double s2 = w * (AT1 + w * (AT3 + w * (AT5 + w * (AT7 + w * AT9))));
        if (id < 0) return x - x * (s1 + s2);
        z = AtanHi[id] - ((x * (s1 + s2) - AtanLo[id]) - x);
        return hx < 0 ? -z : z;
    }

    const double PiO4 = 7.8539816339744827900E-01, PiO2 = 1.5707963267948965580E+00, Pi = 3.1415926535897931160E+00,
        PiLo = 1.2246467991473531772E-16;

    public static double Atan2(double y, double x)
    {
        if (double.IsNaN(x) || double.IsNaN(y)) return x + y;
        long bx = BitConverter.DoubleToInt64Bits(x), by = BitConverter.DoubleToInt64Bits(y);
        int hx = (int)(bx >> 32), hy = (int)(by >> 32);
        uint lx = (uint)bx, ly = (uint)by;
        int ix = hx & 0x7fffffff, iy = hy & 0x7fffffff;
        if (((hx - 0x3ff00000) | (int)lx) == 0) return Atan(y);
        int m = ((hy >> 31) & 1) | ((hx >> 30) & 2);
        if ((iy | (int)ly) == 0)
        {
            switch (m)
            {
                case 0:
                case 1: return y;
                case 2: return Pi;
                default: return -Pi;
            }
        }
        if ((ix | (int)lx) == 0) return hy < 0 ? -PiO2 : PiO2;
        if (ix == 0x7ff00000)
        {
            if (iy == 0x7ff00000)
            {
                switch (m)
                {
                    case 0: return PiO4;
                    case 1: return -PiO4;
                    case 2: return 3.0 * PiO4;
                    default: return -3.0 * PiO4;
                }
            }
            switch (m)
            {
                case 0: return 0.0;
                case 1: return -0.0;
                case 2: return Pi;
                default: return -Pi;
            }
        }
        if (iy == 0x7ff00000) return hy < 0 ? -PiO2 : PiO2;
        int k = (iy - ix) >> 20;
        double z;
        if (k > 60)
        {
            z = PiO2 + 0.5 * PiLo;
            m &= 1;
        }
        else if (hx < 0 && k < -60) z = 0.0;
        else z = Atan(Math.Abs(y / x));
        switch (m)
        {
            case 0: return z;
            case 1: return -z;
            case 2: return Pi - (z - PiLo);
            default: return (z - PiLo) - Pi;
        }
    }

    // ------------------------------------------------------------------ exp (fdlibm)

    const double Ln2Hi = 6.93147180369123816490e-01, Ln2Lo = 1.90821492927058770002e-10, Invln2 = 1.44269504088896338700e+00,
        P1 = 1.66666666666666019037e-01, P2 = -2.77777777770155933842e-03, P3 = 6.61375632143793436117e-05,
        P4 = -1.65339022054652515390e-06, P5 = 4.13813679705723846039e-08, E = 2.718281828459045,
        Two1023 = 8.988465674311579539e307, Twom1000 = 9.33263618503218878990e-302;

    public static double Exp(double x)
    {
        int hx = Hi(x);
        int xsb = (hx >> 31) & 1;
        hx &= 0x7fffffff;
        double hi = 0, lo = 0;
        int k = 0;
        if (hx >= 0x40862E42)
        {
            if (hx >= 0x7ff00000)
            {
                if (((hx & 0xfffff) | (int)Lo(x)) != 0) return x + x;
                return xsb == 0 ? x : 0.0;
            }
            if (x > 7.09782712893383973096e+02) return double.PositiveInfinity;
            if (x < -7.45133219101941108420e+02) return 0;
        }
        if (hx > 0x3fd62e42)
        {
            if (hx < 0x3FF0A2B2)
            {
                if (x == 1.0) return E;
                hi = x - (xsb == 0 ? Ln2Hi : -Ln2Hi);
                lo = xsb == 0 ? Ln2Lo : -Ln2Lo;
                k = 1 - xsb - xsb;
            }
            else
            {
                k = (int)(Invln2 * x + (xsb == 0 ? 0.5 : -0.5));
                double tk = k;
                hi = x - tk * Ln2Hi;
                lo = tk * Ln2Lo;
            }
            x = hi - lo;
        }
        else if (hx < 0x3e300000)
        {
            return 1.0 + x;
        }
        else k = 0;
        double t = x * x;
        double twopk = k >= -1021 ? Words(0x3ff00000 + (k << 20), 0) : Words(0x3ff00000 + ((k + 1000) << 20), 0);
        double c = x - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
        if (k == 0) return 1.0 - ((x * c) / (c - 2.0) - x);
        double y = 1.0 - ((lo - (x * c) / (2.0 - c)) - hi);
        if (k >= -1021)
        {
            if (k == 1024) return y * 2.0 * Two1023;
            return y * twopk;
        }
        return y * twopk * Twom1000;
    }

    // ------------------------------------------------------------------ pow (fdlibm)

    const double Two53 = 9007199254740992.0, Huge = 1.0e300, Tiny = 1.0e-300,
        L1 = 5.99999999999994648725e-01, L2 = 4.28571428578550184252e-01, L3 = 3.33333329818377432918e-01,
        L4 = 2.72728123808534006489e-01, L5 = 2.30660745775561754067e-01, L6 = 2.06975017800338417784e-01,
        Lg2 = 6.93147180559945286227e-01, Lg2H = 6.93147182464599609375e-01, Lg2L = -1.90465429995776804525e-09,
        Ovt = 8.0085662595372944372e-17, Cp = 9.61796693925975554329e-01, CpH = 9.61796700954437255859e-01,
        CpL = -7.02846165095275826516e-09, Ivln2 = 1.44269504088896338700e+00, Ivln2H = 1.44269502162933349609e+00,
        Ivln2L = 1.92596299112661746887e-08;

    static readonly double[] Bp = { 1.0, 1.5 };
    static readonly double[] DpH = { 0.0, 5.84962487220764160156e-01 };
    static readonly double[] DpL = { 0.0, 1.35003920212974897128e-08 };

    public static double Pow(double x, double y)
    {
        int hx = Hi(x), hy = Hi(y);
        uint lx = Lo(x), ly = Lo(y);
        int ix = hx & 0x7fffffff, iy = hy & 0x7fffffff;
        if ((iy | (int)ly) == 0) return 1.0;
        if (ix > 0x7ff00000 || (ix == 0x7ff00000 && lx != 0) || iy > 0x7ff00000 || (iy == 0x7ff00000 && ly != 0))
            return x + y;
        int yisint = 0, k, j;
        if (hx < 0)
        {
            if (iy >= 0x43400000) yisint = 2;
            else if (iy >= 0x3ff00000)
            {
                k = (iy >> 20) - 0x3ff;
                if (k > 20)
                {
                    uint jj = ly >> (52 - k);
                    if ((jj << (52 - k)) == ly) yisint = 2 - (int)(jj & 1);
                }
                else if (ly == 0)
                {
                    j = iy >> (20 - k);
                    if ((j << (20 - k)) == iy) yisint = 2 - (j & 1);
                }
            }
        }
        if (ly == 0)
        {
            if (iy == 0x7ff00000)
            {
                if (((ix - 0x3ff00000) | (int)lx) == 0) return y - y;
                if (ix >= 0x3ff00000) return hy >= 0 ? y : 0.0;
                return hy < 0 ? -y : 0.0;
            }
            if (iy == 0x3ff00000) return hy < 0 ? 1.0 / x : x;
            if (hy == 0x40000000) return x * x;
            if (hy == 0x3fe00000 && hx >= 0) return Math.Sqrt(x);
        }
        double ax = Math.Abs(x);
        double z;
        if (lx == 0 && (ix == 0x7ff00000 || ix == 0 || ix == 0x3ff00000))
        {
            z = ax;
            if (hy < 0) z = 1.0 / z;
            if (hx < 0)
            {
                if (((ix - 0x3ff00000) | yisint) == 0) z = double.NaN;
                else if (yisint == 1) z = -z;
            }
            return z;
        }
        int n = (hx >> 31) + 1;
        if ((n | yisint) == 0) return double.NaN;
        double s = 1.0;
        if ((n | (yisint - 1)) == 0) s = -1.0;

        double t1, t2, t, u, v, w;
        if (iy > 0x41e00000)
        {
            if (iy > 0x43f00000)
            {
                if (ix <= 0x3fefffff) return hy < 0 ? double.PositiveInfinity : 0.0;
                if (ix >= 0x3ff00000) return hy > 0 ? double.PositiveInfinity : 0.0;
            }
            if (ix < 0x3fefffff) return hy < 0 ? s * Huge * Huge : s * Tiny * Tiny;
            if (ix > 0x3ff00000) return hy > 0 ? s * Huge * Huge : s * Tiny * Tiny;
            t = ax - 1.0;
            w = (t * t) * (0.5 - t * (0.3333333333333333333333 - t * 0.25));
            u = Ivln2H * t;
            v = t * Ivln2L - w * Ivln2;
            t1 = WithLo(u + v, 0);
            t2 = v - (t1 - u);
        }
        else
        {
            n = 0;
            if (ix < 0x00100000)
            {
                ax *= Two53;
                n -= 53;
                ix = Hi(ax);
            }
            n += (ix >> 20) - 0x3ff;
            j = ix & 0x000fffff;
            ix = j | 0x3ff00000;
            if (j <= 0x3988E) k = 0;
            else if (j < 0xBB67A) k = 1;
            else
            {
                k = 0;
                n += 1;
                ix -= 0x00100000;
            }
            ax = WithHi(ax, ix);
            u = ax - Bp[k];
            v = 1.0 / (ax + Bp[k]);
            double ss = u * v;
            double sH = WithLo(ss, 0);
            double tH = Words(((ix >> 1) | 0x20000000) + 0x00080000 + (k << 18), 0);
            double tL = ax - (tH - Bp[k]);
            double sL = v * ((u - sH * tH) - sH * tL);
            double s2 = ss * ss;
            double r = s2 * s2 * (L1 + s2 * (L2 + s2 * (L3 + s2 * (L4 + s2 * (L5 + s2 * L6)))));
            r += sL * (sH + ss);
            s2 = sH * sH;
            tH = WithLo(3.0 + s2 + r, 0);
            tL = r - ((tH - 3.0) - s2);
            u = sH * tH;
            v = sL * tH + tL * ss;
            double pH0 = WithLo(u + v, 0);
            double pL0 = v - (pH0 - u);
            double zH = CpH * pH0;
            double zL = CpL * pH0 + pL0 * Cp + DpL[k];
            t = n;
            t1 = WithLo(((zH + zL) + DpH[k]) + t, 0);
            t2 = zL - (((t1 - t) - DpH[k]) - zH);
        }

        double y1 = WithLo(y, 0);
        double pL = (y - y1) * t1 + y * t2;
        double pH = y1 * t1;
        z = pL + pH;
        j = Hi(z);
        int i = (int)Lo(z);
        if (j >= 0x40900000)
        {
            if (((j - 0x40900000) | i) != 0) return s * Huge * Huge;
            if (pL + Ovt > z - pH) return s * Huge * Huge;
        }
        else if ((j & 0x7fffffff) >= 0x4090cc00)
        {
            if (((j - unchecked((int)0xc090cc00)) | i) != 0) return s * Tiny * Tiny;
            if (pL <= z - pH) return s * Tiny * Tiny;
        }
        i = j & 0x7fffffff;
        k = (i >> 20) - 0x3ff;
        n = 0;
        if (i > 0x3fe00000)
        {
            n = j + (0x00100000 >> (k + 1));
            k = ((n & 0x7fffffff) >> 20) - 0x3ff;
            t = Words(n & ~(0x000fffff >> k), 0);
            n = ((n & 0x000fffff) | 0x00100000) >> (20 - k);
            if (j < 0) n = -n;
            pH -= t;
        }
        t = WithLo(pL + pH, 0);
        u = t * Lg2H;
        v = (pL - (t - pH)) * Lg2 + t * Lg2L;
        z = u + v;
        w = v - (z - u);
        t = z * z;
        t1 = z - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
        // (V8's port divides by the whole of (t1 - 2) - (w + z w), unlike fdlibm: kept.)
        double rr = (z * t1) / ((t1 - 2.0) - (w + z * w));
        z = 1.0 - (rr - z);
        j = Hi(z);
        j += n << 20;
        if ((j >> 20) <= 0) z = Math.ScaleB(z, n);
        else z = WithHi(z, Hi(z) + (n << 20));
        return s * z;
    }
}
