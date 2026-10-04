using System;
using System.Collections.Generic;

namespace GameNight.Audio;

/// <summary>A small, fast random source for baking (xorshift), seeded so every bake is the same.</summary>
public sealed class Rng
{
    uint _s;

    public Rng(uint seed) => _s = seed * 2654435761u | 1;

    public float F()
    {
        _s ^= _s << 13;
        _s ^= _s >> 17;
        _s ^= _s << 5;
        return (_s >> 8) * (1f / 16777216f);
    }

    public float R(float a, float b) => a + (b - a) * F();

    /// <summary>Roughly normal, mean 0, deviation 1.</summary>
    public float N() => (F() + F() + F() + F() - 2) * 1.732f;

    public int I(int n) => Math.Min(n - 1, (int)(F() * n));

    public T Pick<T>(params T[] a) => a[I(a.Length)];
}

/// <summary>A formant: Klatt's two-pole resonator (unity gain at DC, so a cascade of them keeps
/// the vowel's natural balance).</summary>
public struct Resonator
{
    float _a, _b, _c, _y1, _y2;

    public void Set(float f, float bw, float sr)
    {
        float r = MathF.Exp(-MathF.PI * bw / sr);
        _c = -r * r;
        _b = 2 * r * MathF.Cos(2 * MathF.PI * MathF.Min(f, sr * 0.45f) / sr);
        _a = 1 - _b - _c;
    }

    public float Run(float x)
    {
        float y = _a * x + _b * _y1 + _c * _y2;
        _y2 = _y1;
        _y1 = y;
        return y;
    }
}

/// <summary>
/// One stretch of a voice: a vowel held from T for Dur seconds, its pitch gliding from Hz to
/// HzEnd, at Amp, with its own attack and release. Unvoiced, it's a consonant: breath through
/// a hiss of high formants.
/// </summary>
public struct Seg
{
    public float T, Dur, Hz, HzEnd, Amp, Attack, Release;
    public Vowel V;
    public bool Hiss;

    public static Seg Note(float t, float dur, float hz, float hzEnd, Vowel v, float amp, float attack = 0.04f, float release = 0.08f) =>
        new() { T = t, Dur = dur, Hz = hz, HzEnd = hzEnd, V = v, Amp = amp, Attack = attack, Release = release };

    public static Seg Consonant(float t, float dur, float amp) =>
        new() { T = t, Dur = dur, Amp = amp, Attack = 0.008f, Release = 0.02f, Hiss = true };
}

/// <summary>How a voice sounds: man or woman (the formants), how bright and breathy, how
/// steady, how far away.</summary>
public struct Tone
{
    /// <summary>Formant scale: 1 a man, about 1.17 a woman.</summary>
    public float Size;
    /// <summary>Glottal brightness (Hz of the source's tilt): ~700 talking, 1600+ shouting.</summary>
    public float Bright;
    /// <summary>Breath mixed into the voicing, 0..1.</summary>
    public float Breath;
    /// <summary>Vibrato depth (ratio) and rate (Hz).</summary>
    public float Vib, VibHz;
    /// <summary>Pitch jitter (ratio, a slow random wander).</summary>
    public float Jitter;
    /// <summary>Distance: the lowpass (Hz) the voice reaches you through.</summary>
    public float Far;

    public static Tone Talk(Rng r, bool woman) => new()
    {
        Size = woman ? 1.17f : r.R(0.94f, 1.04f), Bright = r.R(600, 900), Breath = r.R(0.08f, 0.2f),
        Vib = 0, VibHz = 5, Jitter = 0.012f, Far = r.R(1800, 5000),
    };

    public static Tone Shout(Rng r, bool woman) => new()
    {
        Size = woman ? 1.17f : r.R(0.95f, 1.05f), Bright = r.R(1400, 2400), Breath = r.R(0.2f, 0.4f),
        Vib = r.R(0.004f, 0.012f), VibHz = r.R(4.5f, 6.5f), Jitter = 0.02f, Far = r.R(1600, 5500),
    };
}

/// <summary>
/// Crowd voices, rendered offline into the bank (at a modest rate): a glottal source (a
/// band-limited saw softened to a voice's tilt, wandering in pitch and level like a real
/// throat, with breath in it) through three cascaded formants that glide from vowel to
/// vowel, then the distance it travels. A crowd is thousands of these: the bank renders
/// dozens per sound into loops and moments, and the live mix multiplies them.
/// </summary>
public static class CrowdSynth
{
    /// <summary>F1, F2, F3 of each vowel (a man's; Size scales them).</summary>
    static readonly (float a, float b, float c)[] Formants =
    {
        (730, 1090, 2440), (530, 1840, 2480), (300, 2250, 3000), (570, 840, 2410), (320, 870, 2240),
    };

    /// <summary>Where breath hisses for a consonant (s, sh, t: high and wide).</summary>
    static readonly (float a, float b, float c) Hiss = (1800, 3200, 4400);

    /// <summary>
    /// Render a voice's segments into `buf` at sample offset `at` (wrapping round the buffer
    /// when `wrap`, so a loop has no seam), scaled by `gain`.
    /// </summary>
    public static void Voice(float[] buf, float sr, int at, bool wrap, List<Seg> segs, Tone tone, Rng r, float gain)
    {
        if (segs.Count == 0) return;
        int len = buf.Length;
        var f1 = new Resonator();
        var f2 = new Resonator();
        var f3 = new Resonator();
        float c1 = 500, c2 = 1500, c3 = 2500;
        double phase = r.F();
        float tilt = 0, far = 0, lp = 0;
        float tiltK = 1 - MathF.Exp(-2 * MathF.PI * tone.Bright / sr);
        float farK = 1 - MathF.Exp(-2 * MathF.PI * tone.Far / sr);
        float wander = 0, shimmer = 0, vib = r.F() * 6.283f;
        float dv = 2 * MathF.PI * tone.VibHz / sr;
        float glide = 1 - MathF.Exp(-1 / (0.025f * sr));
        float hz = segs[0].Hz;
        bool first = true;
        for (int k = 0; k < segs.Count; k++)
        {
            var s = segs[k];
            int n0 = (int)(s.T * sr), n = (int)((s.Dur + s.Release) * sr);
            float att = MathF.Max(0.003f, s.Attack) * sr, rel = MathF.Max(0.005f, s.Release) * sr, hold = s.Dur * sr;
            var (ta, tb, tc) = s.Hiss ? Hiss : Formants[(int)s.V];
            float sz = s.Hiss ? 1 : tone.Size;
            ta *= sz;
            tb *= sz;
            tc *= sz;
            if (first)
            {
                c1 = ta;
                c2 = tb;
                c3 = tc;
                first = false;
            }
            for (int i = 0; i < n; i++)
            {
                int j = at + n0 + i;
                if (wrap) j = ((j % len) + len) % len;
                else if (j < 0) continue;
                else if (j >= len) break;
                float env = i < att ? i / att : i < hold ? 1 : MathF.Max(0, 1 - (i - hold) / rel);
                env *= s.Amp;
                // The formants glide to the new vowel; re-solved every 16 samples.
                if ((i & 15) == 0)
                {
                    c1 += (ta - c1) * MathF.Min(1, glide * 16);
                    c2 += (tb - c2) * MathF.Min(1, glide * 16);
                    c3 += (tc - c3) * MathF.Min(1, glide * 16);
                    float bwk = s.Hiss ? 4 : 1;
                    f1.Set(c1, 70 * bwk, sr);
                    f2.Set(c2, 95 * bwk, sr);
                    f3.Set(c3, 130 * bwk, sr);
                    wander += (r.N() * tone.Jitter * 3 - wander) * 0.05f;
                    shimmer += (r.N() * 0.08f - shimmer) * 0.05f;
                }
                float noise = r.F() * 2 - 1;
                float x;
                if (s.Hiss) x = noise * env * 0.9f;
                else
                {
                    float u = i / MathF.Max(1, hold);
                    float target = s.Hz + (s.HzEnd - s.Hz) * MathF.Min(1, u);
                    hz += (target - hz) * 0.01f;
                    vib += dv;
                    float f = hz * (1 + wander + tone.Vib * MathF.Sin(vib));
                    double dp = f / sr;
                    phase += dp;
                    phase -= Math.Floor(phase);
                    float saw = Mixer.Saw(phase, (float)dp);
                    tilt += (saw - tilt) * tiltK;
                    x = (tilt * (1 - tone.Breath) + noise * tone.Breath * 0.5f) * env * (1 + shimmer);
                }
                float y = f3.Run(f2.Run(f1.Run(x)));
                far += (y - far) * farK;
                // A touch of the lows the formants leave out (the chest).
                lp += (x - lp) * 0.02f;
                buf[j] += (far + lp * 0.3f) * gain;
            }
        }
    }

    /// <summary>
    /// Someone talking for `len` seconds (phrases of syllables, pauses between): the murmur.
    /// `pace` speeds it up and `lift` raises the pitch (an excited crowd).
    /// </summary>
    public static List<Seg> Talking(Rng r, float len, float baseHz, float pace, float lift, float level)
    {
        var segs = new List<Seg>();
        float t = r.R(-1.5f, 1);
        while (t < len)
        {
            int syl = 3 + r.I(8);
            float hz0 = baseHz * lift * r.R(1.05f, 1.2f);
            for (int k = 0; k < syl && t < len; k++)
            {
                if (r.F() < 0.55f)
                {
                    float cd = r.R(0.025f, 0.06f);
                    segs.Add(Seg.Consonant(t, cd, level * 0.35f));
                    t += cd * 0.7f;
                }
                float d = r.R(0.08f, 0.2f) / pace;
                float decl = 1 - 0.25f * k / syl;
                float hz = hz0 * decl * (1 + r.N() * 0.06f);
                float stress = r.F() < 0.25f ? 1.3f : 1;
                segs.Add(Seg.Note(t, d, hz * stress, hz * r.R(0.92f, 1.04f), (Vowel)r.I(5), level * r.R(0.6f, 1) * stress, 0.015f, 0.04f));
                t += d + r.R(0.0f, 0.04f);
            }
            t += r.R(0.2f, 1.3f) / pace;
        }
        return segs;
    }

    /// <summary>Someone shouting on and off: long open vowels, pitch thrown up.</summary>
    public static List<Seg> Shouting(Rng r, float len, float baseHz, float lift, float level, float gapMax = 0.6f)
    {
        var segs = new List<Seg>();
        float t = r.R(-2, 0.5f);
        while (t < len)
        {
            float d = r.R(0.5f, 2.2f);
            float hz = baseHz * lift * r.R(0.85f, 1.2f);
            var v = r.Pick(Vowel.A, Vowel.A, Vowel.O, Vowel.E, Vowel.O);
            segs.Add(Seg.Note(t, d, hz * 0.88f, hz * r.R(0.95f, 1.08f), v, level * r.R(0.6f, 1), r.R(0.05f, 0.15f), r.R(0.15f, 0.4f)));
            t += d + r.R(0.05f, gapMax);
        }
        return segs;
    }

    /// <summary>A man or a woman's speaking pitch.</summary>
    public static float BaseHz(Rng r, out bool woman)
    {
        woman = r.F() < 0.22f;
        return woman ? r.R(175, 240) : r.R(90, 145);
    }

    /// <summary>One clap: a crack of noise ringing in the cupped hands.</summary>
    public static void Clap(float[] buf, float sr, int at, bool wrap, Rng r, float gain, float far)
    {
        var res = new Resonator();
        res.Set(r.R(700, 2600), r.R(300, 900), sr);
        int n = (int)(r.R(0.008f, 0.02f) * sr);
        float k = MathF.Exp(-1 / (r.R(0.0025f, 0.006f) * sr));
        float farK = 1 - MathF.Exp(-2 * MathF.PI * far / sr), lp = 0;
        float e = 1;
        int len = buf.Length;
        for (int i = 0; i < n; i++)
        {
            int j = at + i;
            if (wrap) j = ((j % len) + len) % len;
            else if (j < 0) continue;
            else if (j >= len) break;
            float y = res.Run((r.F() * 2 - 1) * e) * 3;
            lp += (y - lp) * farK;
            buf[j] += lp * gain;
            e *= k;
        }
    }

    /// <summary>A two-finger whistle: up into the note, held, and away (or a swooping wolf whistle).</summary>
    public static void Whistle(float[] buf, float sr, int at, bool wrap, Rng r, float gain, int shape = -1)
    {
        if (shape < 0) shape = r.I(3);
        float f = r.R(2100, 3500), d = shape == 1 ? r.R(0.6f, 0.9f) : r.R(0.3f, 1.1f);
        int n = (int)(d * sr), len = buf.Length;
        double ph = 0;
        float breath = 0;
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)n;
            float pitch = shape switch
            {
                1 => u < 0.45f ? 0.75f + 0.5f * u / 0.45f : 1.25f - 0.6f * (u - 0.45f) / 0.55f, // wolf whistle
                2 => u < 0.5f ? 1 : 0.8f,                                                       // two-tone
                _ => u < 0.08f ? 0.8f + 0.2f * u / 0.08f : u > 0.85f ? 1 - 0.3f * (u - 0.85f) / 0.15f : 1,
            };
            ph += f * pitch * (1 + 0.004f * MathF.Sin(i * 0.004f)) / sr;
            float env = MathF.Min(1, MathF.Min(u / 0.04f, (1 - u) / 0.08f));
            breath += ((r.F() * 2 - 1) - breath) * 0.5f;
            int j = at + i;
            if (wrap) j = ((j % len) + len) % len;
            else if (j < 0) continue;
            else if (j >= len) break;
            buf[j] += (MathF.Sin((float)(ph * 2 * Math.PI)) + breath * 0.15f) * env * gain;
        }
    }

    /// <summary>The ultras' bass drum: a skin thumping down in pitch, and the beater's slap.</summary>
    public static void Drum(float[] buf, float sr, int at, bool wrap, float f0, float gain)
    {
        int n = (int)(0.5f * sr), len = buf.Length;
        double ph = 0;
        var r = new Rng((uint)(at * 7 + 1));
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / sr;
            float f = f0 * (0.5f + 0.5f * MathF.Exp(-t / 0.06f));
            ph += f / sr;
            float env = MathF.Exp(-t / 0.16f) * MathF.Min(1, t / 0.003f);
            lp += ((r.F() * 2 - 1) - lp) * 0.3f;
            float slap = lp * MathF.Exp(-t / 0.012f) * 0.4f;
            int j = at + i;
            if (wrap) j = ((j % len) + len) % len;
            else if (j < 0) continue;
            else if (j >= len) break;
            buf[j] += (MathF.Sin((float)(ph * 2 * Math.PI)) * env + slap) * gain;
        }
    }

    /// <summary>Scale a sound so its loud part sits at `rms`.</summary>
    public static void Normalise(float[] buf, float rms)
    {
        double s = 0;
        int n = 0;
        // The loud part: ignore near-silence (a one-shot's tail).
        float peak = 0;
        foreach (float v in buf) peak = MathF.Max(peak, MathF.Abs(v));
        foreach (float v in buf)
            if (MathF.Abs(v) > peak * 0.05f)
            {
                s += v * v;
                n++;
            }
        if (n == 0) return;
        float k = rms / (float)Math.Sqrt(s / n);
        for (int i = 0; i < buf.Length; i++) buf[i] *= k;
    }
}
