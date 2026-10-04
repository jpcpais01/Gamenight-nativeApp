using System;

namespace GameNight.Audio;

/// <summary>
/// The terraces, synthesised, on top of the recorded crowd bed (from the PWA's chantAudio.ts,
/// and well past it): the ends singing the director's songs (a choir on the tune, the capo's
/// calls answered by the whole end), the ultras' drums and claps keeping time, the Viking
/// "HUH!", and every reaction (oohs, groans, sighs, applause, boos and whistling, cheers, the
/// PA and the scorer's name roared back). The home end sings in more voices than the away
/// end and in its own key. Beats are scheduled a little ahead from the director's song. Runs
/// on the audio thread.
/// </summary>
public sealed class Chants
{
    readonly Mixer _mx;
    int _scheduledId = -1;
    double _scheduledTo;
    /// <summary>Each end's key (semitones from the root): the away end pitches its own songs.</summary>
    readonly float[] _key;

    public Chants(Mixer mx)
    {
        _mx = mx;
        _key = new[] { 0, 1.5f + mx.Rand() * 1.5f };
    }

    static Bus EndBus(int end) => end == 0 ? Bus.End0 : Bus.End1;

    /// <summary>How many voices an end sings with (the ground's size, the home end's numbers).</summary>
    int Voices(int end, bool call = false) => call ? 3 : Math.Max(3, (int)MathF.Round((end == 0 ? 9 : 5) * (0.45f + 0.55f * _mx.Venue.Size)));

    /// <summary>Schedule what the director's song needs over the next moment.</summary>
    public void Update(TerraceCue dir)
    {
        double now = _mx.Now;
        var s = dir.Singing;
        // Director time to audio time (a small fixed latency so nothing lands in the past).
        double toCtx = now + 0.06 - dir.T;
        double horizon = dir.T + 0.35;
        foreach (var r in dir.Reactions) React(r, Math.Max(now + 0.02, r.At + toCtx));
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
                    Drum(bus, s.End, at, 0.6f);
                    Huh(bus, s.End, at + 0.05, 0.75f + 0.25f * i / s.Booms.Length);
                }
            }
            // The roar at the end of it.
            double roar = s.Booms[^1] + 0.5;
            if (roar > from && roar <= horizon) Roar(bus, s.End, roar + toCtx);
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
            foreach (var n in c.Notes)
            {
                double t = top + n.B * beat;
                if (t > from && t <= horizon && t < s.Until)
                {
                    float lv = lv0 * Swell(t) * (n.Call ? 0.5f : 1);
                    Crowd.Sing(_mx, bus, t + toCtx, n.D * beat, Crowd.Hz(n.P + _key[s.End]), n.V, lv, Voices(s.End, n.Call));
                }
            }
            foreach (float b in c.Claps)
            {
                double t = top + b * beat;
                if (t > from && t <= horizon && t < s.Until) Clap(bus, t + toCtx, lv0 * Swell(t));
            }
            foreach (float b in c.Drum)
            {
                double t = top + b * beat;
                if (t > from && t <= horizon && t < s.Until) Drum(bus, s.End, t + toCtx, b == 0 ? 1 : 0.75f);
            }
        }
    }

    /// <summary>A reaction from the director, at audio time t.</summary>
    void React(Reaction r, double t)
    {
        int end = Math.Max(0, r.End);
        float lv = r.Level;
        switch (r.Kind)
        {
            case Audio.React.Ooh: Ooh(lv, t); break;
            case Audio.React.Groan: Groan(end, t, lv); break;
            case Audio.React.Aww: Aww(end, t, lv); break;
            case Audio.React.Applause: Crowd.Applause(_mx, EndBus(end), t, r.Dur, lv * (end == 0 ? 1 : 0.8f)); break;
            case Audio.React.Boo: Boo(end, t, lv); break;
            case Audio.React.Jeer: Jeer(end, t, r.Dur, lv); break;
            case Audio.React.Cheer: Cheer(end, t, lv); break;
            case Audio.React.Erupt: Erupt(end, t); break;
            case Audio.React.Rally: Rally(end, t, lv); break;
            case Audio.React.NameCall: NameCall(end, t, lv); break;
            case Audio.React.Announce:
                Crowd.Chime(_mx, t, lv);
                Crowd.Announce(_mx, t + 1.4, 9 + (int)(_mx.Rand() * 6), lv);
                break;
            case Audio.React.Heckle:
            {
                // A small ground, near the pitch: you hear the people round you. A big bowl
                // swallows them; most are lost in the noise.
                var v = _mx.Venue;
                float near = 0.4f + 0.6f * v.Near, keep = 1.1f - 0.5f * v.Size;
                if (_mx.Rand() < keep) Crowd.Heckle(_mx, t, (end == 0 ? -0.3f : 0.3f) + (_mx.Rand() - 0.5f) * 1.2f, lv * near);
                break;
            }
            case Audio.React.Laugh:
                for (int e = 0; e < 2; e++) if (r.End < 0 || r.End == e) Laugh(e, t, lv);
                break;
            case Audio.React.Ole:
                for (int e = 0; e < 2; e++)
                    if (r.End < 0 || r.End == e)
                    {
                        Crowd.Shout(_mx, EndBus(e), t, 0.22, Vowel.O, 0.75f * lv, Voices(e) + 2);
                        Crowd.Shout(_mx, EndBus(e), t + 0.24, 0.55, Vowel.E, 0.9f * lv, Voices(e) + 2);
                    }
                break;
            case Audio.React.Whistler:
                Crowd.Whistler(_mx, Bus.Bowl, t, (end == 0 ? -0.4f : 0.4f) + (_mx.Rand() - 0.5f) * 1f, lv);
                break;
        }
    }

    /// <summary>A goal: the roar is the recording (GameAudio.Goal); the scoring end claps along
    /// after it and its drums go, the other end deflates.</summary>
    void Erupt(int end, double t)
    {
        var bus = EndBus(end);
        for (int i = 0; i < 9; i++)
        {
            Clap(bus, t + 3.4 + i * 0.42, 1);
            if (i % 2 == 0) Drum(bus, end, t + 3.4 + i * 0.42, 1);
        }
        Crowd.Applause(_mx, bus, t + 1.5, 5, 0.7f);
        for (int i = 0; i < 6; i++) Crowd.Whistler(_mx, bus, t + 0.4 + _mx.Rand() * 3, 0, 0.8f);
        Groan(1 - end, t + 0.3, 0.8f);
    }

    /// <summary>A shot just wide: "aaaah-ohhh", falling.</summary>
    void Groan(int end, double t, float level = 1)
    {
        var bus = EndBus(end);
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.14f * level, t + 0.12).Target(0.0001f, t + 0.6, 0.4f);
        var f1 = new Biquad(FilterType.Bandpass, Crowd.Formants[0].f1, 2.5f);
        f1.Freq.Set(Crowd.Formants[0].f1, t).Lin(Crowd.Formants[3].f1, t + 1.0);
        var f2 = new Biquad(FilterType.Bandpass, Crowd.Formants[0].f2, 3);
        f2.Freq.Set(Crowd.Formants[0].f2, t).Lin(Crowd.Formants[3].f2, t + 1.0);
        Breath(bus, t, 2.2, 2.4f, g, f1, f2);
        // Voiced too: the end's "ohh" sliding down.
        Crowd.Sing(_mx, bus, t + 0.05, 1.3, Crowd.Hz(3 + _key[end]), Vowel.O, 0.55f * level, Voices(end), 0, -0.2f);
        _mx.Burst(t, 1.6, FilterType.Bandpass, 700, 0.6f, 0.12f * level, 1, bus);
    }

    /// <summary>An attack that came to nothing: a short, deflating sigh.</summary>
    void Aww(int end, double t, float level)
    {
        Crowd.Sing(_mx, EndBus(end), t, 0.9, Crowd.Hz(5 + _key[end]), Vowel.A, 0.35f * level, Voices(end), 0, -0.22f);
        _mx.Burst(t, 1.0, FilterType.Bandpass, 600, 0.6f, 0.06f * level, 1, EndBus(end));
    }

    /// <summary>An end laughing: little groups of voices going "ha-ha-ha", each falling in pitch
    /// and dying away, scattered round the stand over the best part of a second.</summary>
    void Laugh(int end, double t, float level)
    {
        var bus = EndBus(end);
        var (f1, f2) = Crowd.Formants[(int)Vowel.A];
        int groups = end == 0 ? 6 : 4;
        for (int g = 0; g < groups; g++)
        {
            double at = t + _mx.Rand() * 0.9;
            int n = 4 + (int)(_mx.Rand() * 5);
            double step = 0.12 + _mx.Rand() * 0.06;
            float hz = Crowd.Root * (1.45f + _mx.Rand() * 0.75f);
            float pan = (_mx.Rand() - 0.5f) * 1.4f;
            var voiced = new Param(0.0001f).Set(0.0001f, at);
            var breath = new Param(0.0001f).Set(0.0001f, at);
            for (int i = 0; i < n; i++)
            {
                double s0 = at + i * step;
                float pk = 0.1f * level * MathF.Pow(0.85f, i);
                voiced.Exp(pk, s0 + 0.025).Exp(0.0001f, s0 + step * 0.7);
                breath.Exp(pk * 0.5f, s0 + 0.015).Exp(0.0001f, s0 + step * 0.8);
            }
            double stop = at + n * step + 0.2;
            _mx.Add(new Voice
            {
                Kind = Voice.Src.Custom, Custom = Crowd.Choir(_mx, at, hz, 3, n * step, -0.2f),
                F1 = new Biquad(FilterType.Bandpass, f1, 4), F2 = new Biquad(FilterType.Bandpass, f2, 5), W2 = 0.55f,
                Pre = 5.5f, Gain = voiced, Out = bus, Start = at, Stop = stop,
            }.At(pan));
            _mx.Add(new Voice
            {
                Kind = Voice.Src.Noise, Rate = 1, Pos = _mx.Rand() * 3 * _mx.Sr, F1 = new Biquad(FilterType.Bandpass, f1, 2.6f),
                F2 = new Biquad(FilterType.Bandpass, f2, 3.2f), W2 = 0.55f, Pre = 2.4f, Gain = breath, Out = bus, Start = at, Stop = stop,
            }.At(pan));
        }
    }

    /// <summary>A short "YEAH!": the end on its feet for a moment.</summary>
    void Cheer(int end, double t, float level)
    {
        var bus = EndBus(end);
        Crowd.Shout(_mx, bus, t, 0.5, Vowel.E, 0.8f * level, Voices(end) + 2);
        Crowd.Shout(_mx, bus, t + 0.12, 0.45, Vowel.A, 0.5f * level, Voices(end));
        if (level > 0.6f) for (int i = 0; i < 2; i++) Crowd.Whistler(_mx, bus, t + 0.2 + _mx.Rand() * 0.6, 0, level);
    }

    /// <summary>The end claps its team back into it, with a shout of "come on!" over the top.</summary>
    void Rally(int end, double t, float level)
    {
        var bus = EndBus(end);
        for (int i = 0; i < 10; i++)
        {
            double at = t + i * 0.55;
            Clap(bus, at, level * Math.Min(1, 0.4f + i * 0.12f));
            if (i % 2 == 0) Drum(bus, end, at, 0.8f);
        }
        Crowd.Shout(_mx, bus, t + 2.2, 0.35, Vowel.U, 0.6f * level, Voices(end));
        Crowd.Shout(_mx, bus, t + 2.55, 0.5, Vowel.O, 0.7f * level, Voices(end));
        Crowd.Applause(_mx, bus, t + 4, 3, 0.5f * level);
    }

    /// <summary>The PA calls the scorer's first name, the end roars his surname back: three times,
    /// louder each time, the drum on every roar.</summary>
    void NameCall(int end, double t, float level)
    {
        var bus = EndBus(end);
        var a = (Vowel)(int)(_mx.Rand() * 5);
        var b = _mx.Rand() < 0.5f ? Vowel.A : Vowel.O;
        Crowd.Chime(_mx, t, 0.8f);
        double at = t + 1.3;
        for (int i = 0; i < 3; i++)
        {
            double said = Crowd.Announce(_mx, at, i == 0 ? 7 : 2, level, true);
            double roar = said + 0.12;
            float lv = level * (0.8f + 0.15f * i);
            Crowd.Shout(_mx, bus, roar, 0.24, a, lv, Voices(end) + 3);
            Crowd.Shout(_mx, bus, roar + 0.26, 0.6, b, lv, Voices(end) + 3);
            Drum(bus, end, roar, 1);
            Drum(bus, end, roar + 0.26, 1);
            Clap(bus, roar + 0.26, lv);
            at = roar + 1.2;
        }
        Crowd.Applause(_mx, bus, at - 0.6, 4, 0.8f * level);
    }

    /// <summary>Breath of many mouths through two formant filters (levels w1, w2), shaped by g.</summary>
    void Breath(Bus bus, double t, double dur, float gain, Param g, Biquad b1, Biquad b2, float w2 = 1)
    {
        var v = _mx.NoiseVoice(t, t + dur, 1, g, bus, b1, b2);
        v.Pre = gain;
        v.W2 = w2;
    }

    /// <summary>A section of the crowd shouting a vowel (breath only: the Viking "HUH!", boos).</summary>
    void Bark(Bus bus, double t, double dur, Vowel v, float level)
    {
        var (f1, f2) = Crowd.Formants[(int)v];
        float peak = MathF.Max(0.0005f, 0.26f * level);
        double end = t + Math.Max(0.12, dur * 0.95);
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(peak, t + 0.07).Target(peak * 0.75f, t + 0.1, (float)(dur * 0.6)).Target(0.0001f, end - 0.05, 0.06f);
        Breath(bus, t, end + 0.3 - t, 2.4f, g, new Biquad(FilterType.Bandpass, f1, 2.6f), new Biquad(FilterType.Bandpass, f2, 3.2f), 0.55f);
    }

    /// <summary>A whole end clapping: many hands, scattered over a few tens of ms.</summary>
    void Clap(Bus bus, double t, float level)
    {
        for (int i = 0; i < 5; i++) _mx.Burst(t + _mx.Rand() * 0.035, 0.06, FilterType.Bandpass, 1300 + _mx.Rand() * 900, 1.1f, 0.16f * level, 1, bus);
    }

    /// <summary>The ultras' big bass drum (the away end's is smaller and higher).</summary>
    void Drum(Bus bus, int end, double t, float accent)
    {
        float f0 = end == 0 ? 95 : 120;
        var f = new Param(f0).Set(f0, t).Exp(f0 * 0.5f, t + 0.18);
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.3f * accent, t + 0.006).Exp(0.0001f, t + 0.45);
        _mx.Osc(Wave.Sine, t, 0, t + 0.5, g, bus, f);
        _mx.Burst(t, 0.08, FilterType.Lowpass, 900, 0.7f, 0.1f * accent, 1, bus);
    }

    /// <summary>Viking clap: one big "HUH!" from the end (and the clap that goes with it).</summary>
    void Huh(Bus bus, int end, double t, float level)
    {
        Bark(bus, t, 0.16, Vowel.U, 0.45f * level);
        Crowd.Sing(_mx, bus, t, 0.16, Crowd.Hz(2 + _key[end]), Vowel.U, 0.5f * level, Voices(end), 0, -0.1f);
        Clap(bus, t, 0.5f * level);
    }

    /// <summary>The release: a huge cheer.</summary>
    void Roar(Bus bus, int end, double t)
    {
        _mx.Burst(t, 2.6, FilterType.Bandpass, 900, 0.5f, 0.22f, 1, bus);
        Bark(bus, t, 2.2, Vowel.A, 0.5f);
        Crowd.Shout(_mx, bus, t, 1.6, Vowel.A, 0.7f, Voices(end) + 3);
    }

    /// <summary>The referee's given a foul against them: a long, low boo, and the whistlers.</summary>
    void Boo(int end, double t, float level)
    {
        var bus = EndBus(end);
        Bark(bus, t, 1.8, Vowel.U, 1.5f * level);
        Bark(bus, t + 0.1, 1.6, Vowel.O, 1.0f * level);
        Crowd.Sing(_mx, bus, t + 0.05, 1.7, Crowd.Hz(-5 + _key[end]), Vowel.U, 0.5f * level, Voices(end), 0, -0.06f);
        for (int i = 0; i < 6; i++) Crowd.Whistler(_mx, bus, t + _mx.Rand() * 0.5, 0, 0.45f * level);
    }

    /// <summary>Whistling and booing at the other lot, for `dur` seconds.</summary>
    void Jeer(int end, double t, double dur, float level)
    {
        var bus = EndBus(end);
        int n = (int)(dur * 7);
        for (int i = 0; i < n; i++) Crowd.Whistler(_mx, bus, t + _mx.Rand() * dur, 0, level * (0.5f + 0.5f * _mx.Rand()));
        for (double at = t + 0.2; at < t + dur - 0.5; at += 1.6) Bark(bus, at, 1.5, Vowel.U, 0.7f * level);
    }

    /// <summary>Thunder overhead: a startled "whoa", whistles, then a cheer as if they'd ordered it.</summary>
    public void Thunderstruck(double t, float near)
    {
        Ooh(0.8f * near, t);
        for (int i = 0; i < 5; i++) Crowd.Whistler(_mx, Bus.Bowl, t + 0.6 + _mx.Rand() * 1.5, (_mx.Rand() - 0.5f) * 1.6f, near);
        Cheer(0, t + 1.3, 0.55f * near);
    }

    /// <summary>The whole ground: "ooooooh", rising with the chance, sinking as it goes.</summary>
    void Ooh(float level, double t)
    {
        for (int e = 0; e < 2; e++)
        {
            var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.06f * level, t + 0.25).Target(0.0001f, t + 0.7, 0.35f);
            Breath(EndBus(e), t, 2.2, 2.4f, g, new Biquad(FilterType.Bandpass, Crowd.Formants[4].f1, 3), new Biquad(FilterType.Bandpass, Crowd.Formants[3].f2, 4));
            // The voiced "oooh", up and then away.
            Crowd.Sing(_mx, EndBus(e), t, 1.4, Crowd.Hz(5 + _key[e]), Vowel.U, 0.45f * level, Voices(e), 0, -0.12f);
        }
    }
}
