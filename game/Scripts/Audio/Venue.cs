using System;

namespace GameNight.Audio;

/// <summary>
/// How a ground sounds. Size is the crowd (how many voices, how loud the bed); Roof how much of
/// it sits under cover (a longer, brighter tail that holds the noise in); Near how close the
/// fans are to the pitch (terraces on the touchline sound right on top of you, a bowl behind a
/// running track sounds farther off, with the far stand's echo coming back later).
/// </summary>
public readonly record struct Venue(float Size, float Roof, float Near)
{
    public static readonly Venue Default = new(1, 0.7f, 0.6f);

    /// <summary>The stand sets of the stadium builder, in Kit.Sets order: Arena, Terrace, Curva,
    /// The Wall, Citadel, Harbour, Pagoda, Deco, Crater, Orbital, Membrane, Brutalist, Barrio, Timber, Lumen, Nest, Arch, Neon, Adobe, Meadow, Final, Haunt.</summary>
    static readonly Venue[] Sets =
    {
        new(1, 0.7f, 0.6f), new(0.6f, 0.55f, 1), new(0.9f, 0.15f, 0.2f), new(1, 0.6f, 0.9f), new(1, 0.9f, 0.7f),
        new(0.7f, 0.4f, 0.7f), new(0.8f, 0.6f, 0.6f), new(0.8f, 0.6f, 0.7f), new(0.9f, 0.2f, 0.3f), new(1, 0.85f, 0.5f),
        new(0.8f, 0.8f, 0.6f), new(1, 0.8f, 0.6f), new(1, 0.05f, 0.9f), new(0.6f, 0.9f, 0.9f), new(1, 0.9f, 0.6f),
        new(1, 0.85f, 0.7f), new(1, 0.85f, 0.6f), new(0.9f, 0.8f, 0.8f), new(0.6f, 0.1f, 0.8f), new(0.3f, 0.05f, 0.5f),
        new(1, 0.95f, 0.55f), new(0.8f, 0.7f, 0.7f),
    };

    /// <summary>A ground by id; a built stadium by its eight stand sets (the ends count double:
    /// that's where the singing is).</summary>
    public static Venue For(string ground, int[] sets = null)
    {
        switch (ground)
        {
            case "comunale": return new(0.85f, 0.15f, 0.1f);
            case "old": return new(0.55f, 0.5f, 1);
            case "training":
            case "bare": return new(0, 0, 1);
            case "custom" when sets != null && sets.Length > 0:
            {
                float s = 0, r = 0, n = 0, w = 0;
                for (int i = 0; i < sets.Length; i++)
                {
                    var v = Sets[Math.Clamp(sets[i], 0, Sets.Length - 1)];
                    float k = i == 1 || i == 2 ? 2 : i >= 4 ? 0.5f : 1;
                    s += v.Size * k;
                    r += v.Roof * k;
                    n += v.Near * k;
                    w += k;
                }
                return new(s / w, r / w, n / w);
            }
            default: return Default;
        }
    }
}
