namespace GameNight.Sim;

/// <summary>
/// A Float32Array as JS sees it: stores round to single precision, reads come back as doubles
/// (so arithmetic on two entries is done in double, as in the PWA, not in float as C# would).
/// </summary>
public sealed class F32
{
    readonly float[] a;

    public F32(int n)
    {
        a = new float[n];
    }

    public double this[int i]
    {
        get => a[i];
        set => a[i] = (float)value;
    }

    public int Length => a.Length;
}
