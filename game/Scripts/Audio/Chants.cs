using System;

namespace GameNight.Audio;

/// <summary>
/// The terraces, synthesised, on top of the recorded crowd bed (the PWA's chantAudio.ts): the
/// ultras' bass drum and crowd claps keeping the director's songs' rhythm, the Viking "HUH!",
/// boos, and the "ooh" of a near miss. Voices are breath only (noise through the vowel's two
/// formants). Each end is panned to its side of the ground, through the bowl's reverb (the
/// mixer's End buses). Beats are scheduled a little ahead from the director's song. Runs on
/// the audio thread.
/// </summary>
public sealed class Chants
{
    /// <summary>First and second formants (Hz) of each vowel, a big male crowd.</summary>
    static readonly (float f1, float f2)[] Formants = { (730, 1090), (530, 1840), (390, 1990), (570, 840), (320, 800) };

    readonly Mixer _mx;
    int _scheduledId = -1;
    double _scheduledTo;

    public Chants(Mixer mx) => _mx = mx;

    static Bus EndBus(int end) => end == 0 ? Bus.End0 : Bus.End1;

    /// <summary>Schedule what the director's song needs over the next moment.</summary>
    public void Update(TerraceCue dir)
    {
        double now = _mx.Now;
        var s = dir.Singing;
        // Director time to audio time (a small fixed latency so nothing lands in the past).
        double toCtx = now + 0.06 - dir.T;
        double horizon = dir.T + 0.35;
        foreach (int end in dir.Boos) Boo(end, now + 0.15);
        foreach (float lv in dir.Oohs) Ooh(lv, now + 0.05);
        foreach (int end in dir.Erupts) Erupt(end, now + 0.02);
        foreach (int end in dir.Groans) Groan(end, now + 0.05);
        if (s == null) return;
        if (s.Id != _scheduledId)
        {
            _scheduledId = s.Id;
            _scheduledTo = s.Start - 1e-3;
        }
        // (After a stall, don't play the backlog all at once.)
        double from = Math.Max(_scheduledTo, dir.T - 0.05);
        if (horizon <= from) return;
        _scheduledTo = horizon;
        var bus = EndBus(s.End);
        float lv0 = dir.Level;
        // Fade in as the end joins in, out as it peters out.
        float Swell(double t) => (float)Math.Min(1, Math.Min((t - s.Start) / 2.5 + 0.35, (s.Until - t) / 2 + 0.2));

        if (s.Chant == null)
        {
            for (int i = 0; i < s.Booms.Length; i++)
            {
                double b = s.Booms[i];
                if (b > from && b <= horizon)
                {
                    double at = b + toCtx;
                    Drum(bus, at, 0.6f);
                    Huh(bus, at + 0.05, 0.75f + 0.25f * i / s.Booms.Length);
                }
            }
            // The roar at the end of it.
            double roar = s.Booms[^1] + 0.5;
            if (roar > from && roar <= horizon) Roar(bus, roar + toCtx);
            return;
        }

        var c = s.Chant;
        double beat = 60.0 / c.Bpm;
        double loop = c.Beats * beat;
        int k0 = Math.Max(0, (int)Math.Floor((from - s.Start) / loop));
        int k1 = (int)Math.Floor((horizon - s.Start) / loop);
        for (int k = k0; k <= k1; k++)
        {
            double top = s.Start + k * loop;
            foreach (float b in c.Claps)
            {
                double t = top + b * beat;
                if (t > from && t <= horizon && t < s.Until) Clap(bus, t + toCtx, lv0 * Swell(t));
            }
            foreach (float b in c.Drum)
            {
                double t = top + b * beat;
                if (t > from && t <= horizon && t < s.Until) Drum(bus, t + toCtx, b == 0 ? 1 : 0.75f);
            }
        }
    }

    /// <summary>Breath of many mouths through two formant filters (levels w1, w2), shaped by g.</summary>
    void Breath(Bus bus, double t, double dur, float gain, Param g, Biquad b1, Biquad b2, float w2 = 1)
    {
        var v = _mx.NoiseVoice(t, t + dur, 1, g, bus, b1, b2);
        v.Pre = gain;
        v.W2 = w2;
    }

    /// <summary>A section of the crowd shouting a vowel.</summary>
    void Shout(Bus bus, double t, double dur, Vowel v, float level)
    {
        var (f1, f2) = Formants[(int)v];
        float peak = MathF.Max(0.0005f, 0.26f * level);
        double end = t + Math.Max(0.12, dur * 0.95);
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(peak, t + 0.07).Target(peak * 0.75f, t + 0.1, (float)(dur * 0.6)).Target(0.0001f, end - 0.05, 0.06f);
        Breath(bus, t, end + 0.3 - t, 2.4f, g, new Biquad(FilterType.Bandpass, f1, 2.6f), new Biquad(FilterType.Bandpass, f2, 3.2f), 0.55f);
    }

    /// <summary>A goal: the roar is the recording (GameAudio.Goal); the scoring end claps after it.</summary>
    void Erupt(int end, double t)
    {
        var bus = EndBus(end);
        for (int i = 0; i < 9; i++) Clap(bus, t + 3.4 + i * 0.42, 1);
        // The other end: the air goes out of it.
        Groan(1 - end, t + 0.3);
    }

    /// <summary>A shot just wide: "aaaah-ohhh", falling.</summary>
    void Groan(int end, double t)
    {
        var bus = EndBus(end);
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.14f, t + 0.12).Target(0.0001f, t + 0.6, 0.4f);
        var f1 = new Biquad(FilterType.Bandpass, Formants[0].f1, 2.5f);
        f1.Freq.Set(Formants[0].f1, t).Lin(Formants[3].f1, t + 1.0);
        var f2 = new Biquad(FilterType.Bandpass, Formants[0].f2, 3);
        f2.Freq.Set(Formants[0].f2, t).Lin(Formants[3].f2, t + 1.0);
        Breath(bus, t, 2.2, 2.4f, g, f1, f2);
        _mx.Burst(t, 1.6, FilterType.Bandpass, 700, 0.6f, 0.12f, 1, bus);
    }

    /// <summary>A whole end clapping: many hands, scattered over a few tens of ms.</summary>
    void Clap(Bus bus, double t, float level)
    {
        for (int i = 0; i < 5; i++) _mx.Burst(t + _mx.Rand() * 0.035, 0.06, FilterType.Bandpass, 1300 + _mx.Rand() * 900, 1.1f, 0.16f * level, 1, bus);
    }

    /// <summary>The ultras' big bass drum.</summary>
    void Drum(Bus bus, double t, float accent)
    {
        var f = new Param(95).Set(95, t).Exp(48, t + 0.18);
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.3f * accent, t + 0.006).Exp(0.0001f, t + 0.45);
        _mx.Osc(Wave.Sine, t, 0, t + 0.5, g, bus, f);
        _mx.Burst(t, 0.08, FilterType.Lowpass, 900, 0.7f, 0.1f * accent, 1, bus);
    }

    /// <summary>Viking clap: one big "HUH!" from the end (and the clap that goes with it).</summary>
    void Huh(Bus bus, double t, float level)
    {
        Shout(bus, t, 0.16, Vowel.U, 0.45f * level);
        Clap(bus, t, 0.5f * level);
    }

    /// <summary>The release: a huge cheer.</summary>
    void Roar(Bus bus, double t)
    {
        _mx.Burst(t, 2.6, FilterType.Bandpass, 900, 0.5f, 0.22f, 1, bus);
        Shout(bus, t, 2.2, Vowel.A, 0.5f);
    }

    /// <summary>The referee's given a foul against them: a long, low boo, and the whistlers.</summary>
    void Boo(int end, double t)
    {
        var bus = EndBus(end);
        Shout(bus, t, 1.8, Vowel.U, 1.5f);
        Shout(bus, t + 0.1, 1.6, Vowel.O, 1.0f);
        for (int i = 0; i < 6; i++)
        {
            double at = t + _mx.Rand() * 0.4;
            float f = 2300 + _mx.Rand() * 1400;
            var fp = new Param(f).Set(f, at).Lin(f * (0.9f + _mx.Rand() * 0.25f), at + 0.6);
            var g = new Param(0.0001f).Set(0.0001f, at).Exp(0.012f, at + 0.05).Exp(0.0001f, at + 0.5 + _mx.Rand() * 0.6);
            _mx.Osc(Wave.Sine, at, 0, at + 1.3, g, bus, fp);
        }
    }

    /// <summary>The whole ground: "ooooooh", rising with the chance, sinking as it goes.</summary>
    void Ooh(float level, double t)
    {
        for (int e = 0; e < 2; e++)
        {
            var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.06f * level, t + 0.25).Target(0.0001f, t + 0.7, 0.35f);
            Breath(EndBus(e), t, 2.2, 2.4f, g, new Biquad(FilterType.Bandpass, Formants[4].f1, 3), new Biquad(FilterType.Bandpass, Formants[3].f2, 4));
        }
    }
}
