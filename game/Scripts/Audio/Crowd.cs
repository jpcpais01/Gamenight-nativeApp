using System;

namespace GameNight.Audio;

/// <summary>
/// The voices a stadium is made of, synthesised: a section singing a note (a choir of detuned,
/// slightly late, scooping voices through the vowel's formants), one fan shouting, applause,
/// whistling, and the PA (its chime and an announcer echoing round the stands). Everything
/// here schedules voices on the mixer; it runs on the audio thread.
/// </summary>
public static class Crowd
{
    /// <summary>First and second formants (Hz) of each vowel, a big male crowd.</summary>
    public static readonly (float f1, float f2)[] Formants = { (730, 1090), (530, 1840), (390, 1990), (570, 840), (320, 800) };

    /// <summary>The note the terraces pitch their songs from (D3: a crowd of men singing).</summary>
    public const float Root = 146.8f;

    public static float Hz(float semitones) => Root * MathF.Pow(2, semitones / 12);

    /// <summary>
    /// `n` voices on one pitch: each a band-limited saw a few cents off the others, joining a
    /// moment late, scooping up into the note, with its own vibrato; a few an octave up. The
    /// pitch can glide (shouts fall away) by `glide` (a ratio) over `dur`.
    /// </summary>
    public static Func<double, float> Choir(Mixer mx, double t0, float hz, int n, double dur, float glide = 0, float scoop = 0.05f, float late = 0.06f)
    {
        n = Math.Max(1, n);
        var ph = new double[n];
        var ratio = new float[n];
        var delay = new float[n];
        var amp = new float[n];
        var rate = new float[n];
        var vph = new float[n];
        var f = new float[n];
        float sum2 = 0;
        for (int i = 0; i < n; i++)
        {
            ratio[i] = MathF.Pow(2, (mx.Rand() - 0.5f) * 44 / 1200f) * (n >= 5 && i % 4 == 3 ? 2 : 1);
            amp[i] = n >= 5 && i % 4 == 3 ? 0.45f : 1;
            delay[i] = n == 1 ? 0 : mx.Rand() * late;
            rate[i] = 4.5f + mx.Rand() * 1.6f;
            vph[i] = mx.Rand() * 6.283f;
            ph[i] = mx.Rand();
            sum2 += amp[i] * amp[i];
        }
        float norm = 1 / MathF.Sqrt(sum2);
        double inv = 1.0 / mx.Sr;
        int c = 0;
        return t =>
        {
            double e = t - t0;
            // Pitch moves slowly: re-solve it every 16 samples.
            if ((c++ & 15) == 0)
            {
                float gl = 1 + glide * (float)Math.Min(1, e / dur);
                for (int i = 0; i < n; i++)
                {
                    float u = (float)(e - delay[i]);
                    float sc = u < 0.1f ? 1 - scoop * (1 - Math.Max(u, 0) / 0.1f) : 1;
                    f[i] = hz * ratio[i] * sc * gl * (1 + 0.007f * MathF.Sin(vph[i] + rate[i] * 6.283f * (float)e));
                }
            }
            float s = 0;
            for (int i = 0; i < n; i++)
            {
                if (e < delay[i]) continue;
                double dp = f[i] * inv, p = ph[i] + dp;
                p -= Math.Floor(p);
                ph[i] = p;
                s += Mixer.Saw(p, (float)dp) * amp[i];
            }
            return s * norm;
        };
    }

    /// <summary>A section singing one note on a vowel (the consonant's breath at its start).</summary>
    public static void Sing(Mixer mx, Bus bus, double t, double dur, float hz, Vowel v, float level, int n, float pan = 0, float glide = 0)
    {
        var (f1, f2) = Formants[(int)v];
        float peak = MathF.Max(0.0005f, 0.11f * level);
        double end = t + Math.Max(0.15, dur * 0.92);
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(peak, t + 0.06).Target(peak * 0.8f, t + 0.08, (float)(dur * 0.7))
            .Target(0.0001f, end - 0.05, 0.07f);
        mx.Add(new Voice
        {
            Kind = Voice.Src.Custom, Custom = Choir(mx, t, hz, n, dur, glide),
            F1 = new Biquad(FilterType.Bandpass, f1, 4), F2 = new Biquad(FilterType.Bandpass, f2, 5), W2 = 0.55f,
            Pre = 5.5f, Gain = g, Out = bus, Start = t, Stop = end + 0.3,
        }.At(pan));
        // The mass of breath under it, and the consonant at its start.
        var b = new Param(0.0001f).Set(0.0001f, t).Exp(peak * 0.5f, t + 0.05).Target(0.0001f, end - 0.05, 0.08f);
        mx.Add(new Voice
        {
            Kind = Voice.Src.Noise, Rate = 1, Pos = mx.Rand() * 3 * mx.Sr, F1 = new Biquad(FilterType.Bandpass, f1, 2.6f),
            F2 = new Biquad(FilterType.Bandpass, f2, 3.2f), W2 = 0.55f, Pre = 2.4f, Gain = b, Out = bus, Start = t, Stop = end + 0.3,
        }.At(pan));
        mx.Burst(t, 0.035, FilterType.Highpass, 2600, 0.7f, 0.05f * level, 1, bus);
    }

    /// <summary>A whole section shouting a word: higher, rougher, falling away.</summary>
    public static void Shout(Mixer mx, Bus bus, double t, double dur, Vowel v, float level, int n, float pan = 0)
    {
        Sing(mx, bus, t, dur, Root * 1.45f, v, level, n, pan, -0.18f);
        mx.Burst(t, dur + 0.25, FilterType.Bandpass, 900, 0.5f, 0.09f * level, 1, bus);
    }

    /// <summary>One fan, close by, shouting a few syllables at the referee or his team.</summary>
    public static void Heckle(Mixer mx, double t, float pan, float level)
    {
        int syl = 1 + (int)(mx.Rand() * 3);
        float hz = 165 + mx.Rand() * 70;
        for (int i = 0; i < syl; i++)
        {
            bool last = i == syl - 1;
            double dur = last ? 0.3 + mx.Rand() * 0.35 : 0.13 + mx.Rand() * 0.1;
            var v = (Vowel)(int)(mx.Rand() * 5);
            var (f1, f2) = Formants[(int)v];
            float peak = 0.05f * level;
            var g = new Param(0.0001f).Set(0.0001f, t).Exp(peak, t + 0.025).Target(0.0001f, t + dur, 0.05f);
            mx.Add(new Voice
            {
                Kind = Voice.Src.Custom, Custom = Choir(mx, t, hz * (last ? 1.12f : 1), 1, dur, last ? -0.25f : -0.05f, 0.03f),
                F1 = new Biquad(FilterType.Bandpass, f1 * 1.15f, 5), F2 = new Biquad(FilterType.Bandpass, f2, 6), W2 = 0.6f,
                Pre = 6, Gain = g, Out = Bus.Bowl, Start = t, Stop = t + dur + 0.2,
            }.At(pan));
            if (mx.Rand() < 0.6f) mx.Add(new Voice
            {
                Kind = Voice.Src.Noise, Rate = 1, Pos = mx.Rand() * 3 * mx.Sr, F1 = new Biquad(FilterType.Highpass, 2400),
                Gain = new Param(0.0001f).Set(0.0001f, t).Exp(0.015f * level, t + 0.01).Exp(0.0001f, t + 0.06), Out = Bus.Bowl, Start = t, Stop = t + 0.08,
            }.At(pan));
            t += dur + 0.03;
        }
    }

    /// <summary>A two-finger whistle: up, held, and down (or a wolf-whistle's swoop).</summary>
    public static void Whistler(Mixer mx, Bus bus, double t, float pan, float level)
    {
        float f = 2300 + mx.Rand() * 1300;
        double d = 0.35 + mx.Rand() * 0.7;
        var fp = new Param(f * 0.8f).Set(f * 0.8f, t).Exp(f, t + 0.07).Lin(f * (0.94f + mx.Rand() * 0.1f), t + d - 0.08).Exp(f * 0.75f, t + d);
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.02f * level, t + 0.05).Target(0.0001f, t + d - 0.06, 0.04f);
        mx.Add(new Voice { Kind = Voice.Src.Osc, Wave = Wave.Sine, Freq = fp, Gain = g, Out = bus, Start = t, Stop = t + d + 0.1 }.At(pan));
    }

    /// <summary>
    /// Applause: hands clapping out of step, `density` claps a second at full, each a click of
    /// noise with its own level and ring; rises, holds, dies away over `dur`.
    /// </summary>
    public static void Applause(Mixer mx, Bus bus, double t, double dur, float level, float density = 900, float pan = 0)
    {
        const int M = 40;
        var env = new float[M];
        var amp = new float[M];
        var pos = new int[M];
        var dec = new float[M];
        var noise = mx.Noise;
        int nn = noise.Length, slot = 0;
        uint rng = (uint)(mx.Rand() * uint.MaxValue) | 1;
        float sr = mx.Sr, p = density / sr;
        float Next()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 16777216f;
        }
        Func<double, float> src = _ =>
        {
            if (Next() < p)
            {
                env[slot] = 1;
                amp[slot] = 0.35f + Next() * 0.65f;
                pos[slot] = (int)(Next() * (nn - 1));
                dec[slot] = MathF.Exp(-1 / (sr * (0.004f + 0.009f * Next())));
                slot = (slot + 1) % M;
            }
            float s = 0;
            for (int i = 0; i < M; i++)
            {
                float e = env[i];
                if (e < 0.002f) continue;
                int j = pos[i];
                s += (noise[j] - noise[j + 1]) * e * amp[i];
                pos[i] = j + 2 < nn ? j + 1 : 0;
                env[i] = e * dec[i];
            }
            return s;
        };
        float peak = 0.65f * level;
        double end = t + dur;
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(peak, t + 0.35).Target(peak * 0.75f, t + 0.6, (float)(dur * 0.4)).Target(0.0001f, end - 0.6, 0.45f);
        mx.Add(new Voice
        {
            Kind = Voice.Src.Custom, Custom = src, F1 = new Biquad(FilterType.Bandpass, 1700, 0.6f), F2 = new Biquad(FilterType.Peaking, 3200, 1, 4),
            Series = true, Gain = g, Out = bus, Start = t, Stop = end + 1.5,
        }.At(pan));
    }

    // ------------------------------------------------------------------ the PA

    /// <summary>The PA's speakers round the ground: the same sound from the near ones, then
    /// later and fainter from across the pitch, either side.</summary>
    static readonly (double delay, float gain, float pan)[] Speakers = { (0, 1, 0), (0.21, 0.5f, -0.6f), (0.47, 0.3f, 0.6f) };

    /// <summary>The PA's three-note chime before an announcement.</summary>
    public static void Chime(Mixer mx, double t, float level = 1)
    {
        float[] notes = { 784, 659, 523 };
        foreach (var (delay, gain, pan) in Speakers)
            for (int i = 0; i < 3; i++)
            {
                double at = t + delay + i * 0.42;
                float v = 0.05f * level * gain;
                mx.Add(new Voice { Kind = Voice.Src.Osc, Wave = Wave.Sine, Freq = new Param(notes[i]), Gain = new Param(0.0001f).Set(0.0001f, at).Exp(v, at + 0.01).Exp(0.0001f, at + 1.6), Out = Bus.Bowl, Start = at, Stop = at + 1.7 }.At(pan));
                mx.Add(new Voice { Kind = Voice.Src.Osc, Wave = Wave.Triangle, Freq = new Param(notes[i] * 2), Gain = new Param(0.0001f).Set(0.0001f, at).Exp(v * 0.3f, at + 0.005).Exp(0.0001f, at + 0.5), Out = Bus.Bowl, Start = at, Stop = at + 0.6 }.At(pan));
            }
    }

    /// <summary>
    /// The announcer: `syllables` of speech (a voice through moving formants, a hiss of
    /// consonants) through the PA's horns, echoing from speaker to speaker. With `name`, the
    /// last two are a name, the last one drawn out and rising, as he leaves it for the crowd.
    /// Returns when he stops speaking.
    /// </summary>
    public static double Announce(Mixer mx, double t, int syllables, float level, bool name = false)
    {
        var syl = new (double at, double dur, Vowel v, bool hiss)[syllables];
        double at = t;
        for (int i = 0; i < syllables; i++)
        {
            bool drawn = name && i == syllables - 1;
            double dur = drawn ? 0.75 : name && i == syllables - 2 ? 0.26 : 0.11 + mx.Rand() * 0.1;
            if (name && i == syllables - 2) at += 0.28; // the beat before the name
            syl[i] = (at, dur, (Vowel)(int)(mx.Rand() * 5), mx.Rand() < 0.55f);
            at += dur + (mx.Rand() < 0.2f ? 0.12 : 0.025);
        }
        double end = at;
        foreach (var (delay, gain, pan) in Speakers)
        {
            float peak = 0.06f * level * gain;
            var g = new Param(0.0001f);
            var f0 = new Param(118);
            var b1 = new Biquad(FilterType.Bandpass, 600, 5);
            var b2 = new Biquad(FilterType.Bandpass, 1500, 6);
            f0.Set(124, t + delay);
            float pa = 600, pb = 1500;
            for (int i = 0; i < syllables; i++)
            {
                var (s, d, v, _) = syl[i];
                s += delay;
                bool drawn = name && i == syllables - 1;
                var (fa, fb) = Formants[(int)v];
                g.Set(0.0001f, s).Exp(peak, s + 0.025).Exp(peak * 0.55f, s + d - 0.02).Exp(0.0001f, s + d);
                b1.Freq.Set(pa, s).Lin(fa, s + 0.04);
                b2.Freq.Set(pb, s).Lin(fb, s + 0.04);
                pa = fa;
                pb = fb;
                // Down through a phrase; the name lifted and held.
                float pitch = drawn ? 150 : 128 - 20f * i / syllables + (mx.Rand() - 0.5f) * 10;
                f0.Lin(pitch, s + 0.02);
                if (drawn) f0.Lin(170, s + d * 0.6).Lin(135, s + d);
            }
            double t0 = t + delay;
            double phase = 0, inv = 1.0 / mx.Sr;
            Func<double, float> src = tt =>
            {
                float hz = f0.At(tt);
                phase += hz * inv;
                phase -= Math.Floor(phase);
                return Mixer.Saw(phase, (float)(hz * inv));
            };
            mx.Add(new Voice { Kind = Voice.Src.Custom, Custom = src, F1 = b1, F2 = b2, W2 = 0.7f, Pre = 7, Gain = g, Out = Bus.Bowl, Start = t0, Stop = end + delay + 0.2 }.At(pan));
            foreach (var (s, _, _, hiss) in syl)
                if (hiss) mx.Burst(s + delay, 0.05, FilterType.Bandpass, 3400, 1.5f, 0.02f * level * gain, 1, Bus.Bowl);
        }
        return end;
    }
}
