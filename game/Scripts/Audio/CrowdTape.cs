using System;
using System.Collections.Generic;

namespace GameNight.Audio;

/// <summary>
/// The crowd, as the PWA does it: the real recordings, not synthesised voices. The bed is the
/// recorded crowd looping as five copies a fifth of a loop apart, each at its own slightly
/// different speed and wandering in level, so no loop point is ever heard; it breathes with the
/// match's excitement and surges exponentially in the last metres before a goal line. A goal
/// plays the recorded roar, held through the celebration by staggered, drifting layers of it.
/// On top, only what doesn't need a voice: the ultras' drums and claps in time with the
/// director's songs, applause, whistling, breathy oohs, groans and boos, and the bed swelling
/// when an end cheers. Runs on the audio thread.
/// </summary>
public sealed class CrowdTape
{
    readonly Mixer _mx;
    readonly Recording _bed, _roar;
    readonly List<(Param g, double next)> _drift = new();
    int _scheduledId = -1;
    double _scheduledTo;

    /// <summary>1 at a match; the menus sink it.</summary>
    float _level = 1;
    float _excite = 0.2f, _mouth;
    float _bedSent = -1;
    /// <summary>Moments the whole bed rises for (an end on its feet): start, peak level, rise, hold, fall.</summary>
    readonly List<(double at, float peak, float rise, float hold, float fall)> _swells = new();

    /// <summary>The two formants of each vowel, a big male crowd (A, E, I, O, U).</summary>
    static readonly (float f1, float f2)[] Formants = { (730, 1090), (530, 1840), (390, 1990), (570, 840), (320, 800) };

    public CrowdTape(Mixer mx, Recording bed, Recording roar)
    {
        _mx = mx;
        _bed = bed;
        _roar = roar;
        if (_bed == null) return;
        double len = _bed.Duration;
        for (int i = 0; i < 5; i++)
        {
            var g = new Param(0.5f);
            _mx.Add(new Voice
            {
                Kind = Voice.Src.Sample, Rec = _bed, Loop = true, LoopStart = 0, LoopEnd = len,
                Rate = 0.96f + 0.02f * i + _mx.Rand() * 0.01f,
                Pos = i / 5.0 * len * _bed.Rate,
                Gain = g, Out = Bus.Bed, Stop = double.MaxValue,
            });
            _drift.Add((g, 0));
        }
    }

    static Bus EndBus(int end) => end == 1 ? Bus.End1 : Bus.End0;

    // ------------------------------------------------------------------ the bed

    /// <summary>The crowd's overall level: 1 at a match, lower behind the menus.</summary>
    public float Level
    {
        get => _level;
        set
        {
            _level = value;
            _mx.EndsGain.Target(value, _mx.Now, 0.5f);
        }
    }

    /// <summary>`e`: the match's excitement (0..1); `mouth` (0..1): the ball in the last metres
    /// before a goal line, in front of the goal.</summary>
    public void Excite(float e, float mouth)
    {
        _excite = e;
        _mouth = mouth;
    }

    /// <summary>Every block: the copies wander, the bed follows the match.</summary>
    public void Tick()
    {
        double now = _mx.Now;
        for (int i = 0; i < _drift.Count; i++)
        {
            var (g, next) = _drift[i];
            if (now < next) continue;
            g.Target(0.25f + _mx.Rand() * 0.55f, now, 0.8f + _mx.Rand());
            _drift[i] = (g, now + 1.5 + _mx.Rand() * 3);
        }
        float swell = 0;
        for (int i = _swells.Count - 1; i >= 0; i--)
        {
            var (at, peak, rise, hold, fall) = _swells[i];
            double u = now - at;
            if (u < 0) continue;
            if (u > rise + hold + fall)
            {
                _swells.RemoveAt(i);
                continue;
            }
            float k = u < rise ? (float)(u / rise) : u < rise + hold ? 1 : 1 - (float)((u - rise - hold) / fall);
            swell = MathF.Max(swell, peak * k * k * (3 - 2 * k));
        }
        float want = _level * (0.2f + _excite * 0.6f) * (1 + 6 * _mouth) * (1 + swell);
        if (MathF.Abs(want - _bedSent) > 0.004f)
        {
            _mx.BedGain.Target(want, now, _mouth > 0 ? 0.15f : swell > 0 ? 0.2f : 0.4f);
            _bedSent = want;
        }
    }

    /// <summary>The whole bed lifts by `peak` (1 = twice as loud) for a moment.</summary>
    void Swell(double t, float peak, float rise = 0.25f, float hold = 0.6f, float fall = 1.6f) => _swells.Add((t, peak, rise, hold, fall));

    // ------------------------------------------------------------------ the goal

    /// <summary>
    /// The roar, held at full for `hold` seconds, then fading. The recording plays once from
    /// the top; to hold it longer, three looping copies (staggered, each a touch faster or
    /// slower, wandering in level like the bed) swell in under it, so no seam is heard. `side`
    /// 1: the away end's, smaller and from across the ground.
    /// </summary>
    public void Goal(float hold, Bus bus, int side = 0)
    {
        double t = _mx.Now;
        if (side == 1) hold = Math.Min(hold, 4);
        if (_roar == null)
        {
            var gg = new Param(0.0001f).Set(0.0001f, t).Exp(0.9f, t + 0.35).Target(0.0001f, t + 2.2, 1.1f);
            _mx.NoiseVoice(t, t + 7, 1, gg, bus, new Biquad(FilterType.Bandpass, 800, 0.4f));
            return;
        }
        const double skip = 0.1; // the recording opens with a beat of dead air
        double dur = _roar.Duration, take = dur - skip;
        var g = new Param(side == 1 ? 0.9f : 1.1f);
        var fe = new Param(1);
        _mx.Add(new Voice { Kind = Voice.Src.Sample, Rec = _roar, Pos = skip * _roar.Rate, Gain = fe, Gain2 = g, Out = bus, Start = t, Stop = t + dur });
        if (hold <= take) return;
        // The opening take gives way to the layers over its last 2 s.
        fe.Set(1, t + take - 2).Lin(0.0001f, t + take);
        double end = t + hold + 5;
        g.Target(0.0001f, t + hold, 1.2f);
        const double lo = 0.6; // past the attack
        for (int i = 0; i < 3; i++)
        {
            var env = new Param(0.0001f).Set(0.0001f, t).Lin(0.0001f, t + 1.5).Lin(0.6f, t + take - 0.5);
            for (double at = t + take; at < t + hold; at += 1.2 + _mx.Rand() * 1.5) env.Target(0.4f + _mx.Rand() * 0.35f, at, 0.6f);
            _mx.Add(new Voice
            {
                Kind = Voice.Src.Sample, Rec = _roar, Loop = true, LoopStart = lo, LoopEnd = dur,
                Rate = 0.97f + 0.03f * i, Pos = (lo + i / 3.0 * (dur - lo)) * _roar.Rate,
                Gain = env, Gain2 = g, Out = bus, Start = t, Stop = end,
            });
        }
    }

    /// <summary>The ground draws breath (the woodwork, a penalty given).</summary>
    public void Gasp() => _mx.Burst(_mx.Now, 1.4, FilterType.Bandpass, 700, 0.6f, 0.35f, 0.9f, Bus.Crowd);

    // ------------------------------------------------------------------ the director

    /// <summary>Play what the director's ends are doing over the next moment.</summary>
    public void Cue(TerraceCue dir)
    {
        double now = _mx.Now;
        // Director time to audio time (a small fixed latency so nothing lands in the past).
        double toCtx = now + 0.06 - dir.T;
        double horizon = dir.T + 0.35;
        foreach (var r in dir.Reactions) React(r, Math.Max(now + 0.02, r.At + toCtx));
        var s = dir.Singing;
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
        // In as the end joins in, out as it peters out.
        float In(double t) => (float)Math.Clamp(Math.Min((t - s.Start) / 2.5 + 0.35, (s.Until - t) / 2 + 0.2), 0, 1);

        if (s.Chant == null)
        {
            // The Viking clap: a drum and a "HUH!" on every boom, faster and faster, then the roar.
            for (int i = 0; i < s.Booms.Length; i++)
            {
                double b = s.Booms[i];
                if (b > from && b <= horizon)
                {
                    double at = b + toCtx;
                    Drum(bus, s.End, at, 0.6f);
                    Bark(bus, at + 0.05, 0.16, Vowel.U, 0.45f * (0.75f + 0.25f * i / s.Booms.Length));
                    Clap(bus, at + 0.05, 0.5f);
                }
            }
            double roar = s.Booms[^1] + 0.5;
            if (roar > from && roar <= horizon)
            {
                _mx.Burst(roar + toCtx, 2.6, FilterType.Bandpass, 900, 0.5f, 0.22f, 1, bus);
                Swell(roar + toCtx, 1.2f, 0.2f, 0.8f, 2);
            }
            return;
        }

        // A song: its claps and the drum, in time (the singing itself is the bed rising under it).
        var c = s.Chant;
        double beat = 60.0 / c.Bpm, loop = c.Beats * beat;
        int k0 = Math.Max(0, (int)Math.Floor((from - s.Start) / loop));
        int k1 = (int)Math.Floor((horizon - s.Start) / loop);
        for (int k = k0; k <= k1; k++)
        {
            double top = s.Start + k * loop;
            foreach (float b in c.Claps)
            {
                double t = top + b * beat;
                if (t > from && t <= horizon && t < s.Until) Clap(bus, t + toCtx, lv0 * In(t));
            }
            foreach (float b in c.Drum)
            {
                double t = top + b * beat;
                if (t > from && t <= horizon && t < s.Until) Drum(bus, s.End, t + toCtx, b == 0 ? 1 : 0.75f);
            }
        }
        if (s.Start > from && s.Start <= horizon) Swell(s.Start + toCtx, 0.25f * lv0 * (s.End == 0 ? 1 : 0.6f), 2.5f, (float)Math.Max(0, s.Until - s.Start - 4.5), 2);
    }

    /// <summary>A reaction from the director, at audio time t.</summary>
    void React(Reaction r, double t)
    {
        int end = Math.Max(0, r.End);
        float lv = r.Level;
        var bus = EndBus(end);
        switch (r.Kind)
        {
            case Audio.React.Ooh:
                for (int e = 0; e < 2; e++)
                {
                    var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.09f * lv, t + 0.25).Target(0.0001f, t + 0.7, 0.35f);
                    Breath(EndBus(e), t, 2.2, 2.4f, g, new Biquad(FilterType.Bandpass, Formants[4].f1, 3), new Biquad(FilterType.Bandpass, Formants[3].f2, 4));
                }
                Swell(t, 0.6f * lv, 0.25f, 0.4f, 1.5f);
                break;
            case Audio.React.Groan: Groan(end, t, lv); break;
            case Audio.React.Aww: _mx.Burst(t, 1.0, FilterType.Bandpass, 600, 0.6f, 0.08f * lv, 1, bus); break;
            case Audio.React.Applause: Applause(bus, t, r.Dur, lv * (end == 0 ? 1 : 0.8f)); break;
            case Audio.React.Boo: Boo(end, t, lv); break;
            case Audio.React.Jeer:
            {
                int n = (int)(r.Dur * 7);
                for (int i = 0; i < n; i++) Whistler(bus, t + _mx.Rand() * r.Dur, 0, lv * (0.5f + 0.5f * _mx.Rand()));
                for (double at = t + 0.2; at < t + r.Dur - 0.5; at += 1.6) Bark(bus, at, 1.5, Vowel.U, 0.7f * lv);
                break;
            }
            case Audio.React.Cheer:
                Swell(t, 0.7f * lv, 0.15f, 0.4f, 1.2f);
                if (lv > 0.6f) for (int i = 0; i < 2; i++) Whistler(bus, t + 0.2 + _mx.Rand() * 0.6, 0, lv);
                break;
            case Audio.React.Erupt:
                // The roar is the recording (Goal); after it the scoring end claps along and
                // its drums go, the other end deflates.
                for (int i = 0; i < 9; i++)
                {
                    Clap(bus, t + 3.4 + i * 0.42, 1);
                    if (i % 2 == 0) Drum(bus, end, t + 3.4 + i * 0.42, 1);
                }
                Applause(bus, t + 1.5, 5, 0.7f);
                for (int i = 0; i < 6; i++) Whistler(bus, t + 0.4 + _mx.Rand() * 3, 0, 0.8f);
                Groan(1 - end, t + 0.3, 0.8f);
                break;
            case Audio.React.Rally:
                for (int i = 0; i < 10; i++)
                {
                    double at = t + i * 0.55;
                    Clap(bus, at, lv * Math.Min(1, 0.4f + i * 0.12f));
                    if (i % 2 == 0) Drum(bus, end, at, 0.8f);
                }
                Swell(t + 2.2, 0.5f * lv, 0.3f, 0.8f, 1.5f);
                Applause(bus, t + 4, 3, 0.5f * lv);
                break;
            case Audio.React.NameCall:
            {
                // The PA's chime, then three times the end roars the scorer's name, the drum on it.
                Chime(t, 0.8f);
                for (int i = 0; i < 3; i++)
                {
                    double roar = t + 2.2 + i * 1.9;
                    Swell(roar, (0.6f + 0.2f * i) * lv, 0.08f, 0.5f, 0.8f);
                    Drum(bus, end, roar, 1);
                    Drum(bus, end, roar + 0.26, 1);
                    Clap(bus, roar + 0.26, lv);
                }
                Applause(bus, t + 7.3, 4, 0.8f * lv);
                break;
            }
            case Audio.React.Announce: Chime(t, lv); break;
            case Audio.React.Ole:
                for (int e = 0; e < 2; e++)
                    if (r.End < 0 || r.End == e)
                    {
                        Bark(EndBus(e), t, 0.22, Vowel.O, 0.6f * lv);
                        Bark(EndBus(e), t + 0.24, 0.55, Vowel.E, 0.75f * lv);
                    }
                Swell(t, 0.4f * lv, 0.1f, 0.5f, 0.8f);
                break;
            case Audio.React.Laugh: Swell(t, 0.3f * lv, 0.3f, 1, 1.5f); break;
            case Audio.React.Whistler: Whistler(Bus.Bowl, t, (end == 0 ? -0.4f : 0.4f) + (_mx.Rand() - 0.5f), lv); break;
            // One fan shouting needs a voice: the recording has plenty of those.
            case Audio.React.Heckle: break;
        }
    }

    /// <summary>Thunder overhead: a startled "whoa", whistles, then a cheer as if they'd ordered it.</summary>
    public void Thunderstruck(double t, float near)
    {
        React(new Reaction { Kind = Audio.React.Ooh, End = -1, Level = 0.8f * near }, t);
        for (int i = 0; i < 5; i++) Whistler(Bus.Bowl, t + 0.6 + _mx.Rand() * 1.5, (_mx.Rand() - 0.5f) * 1.6f, near);
        Swell(t + 1.3, 0.5f * near, 0.15f, 0.5f, 1.4f);
    }

    // ------------------------------------------------------------------ the pieces

    /// <summary>A shot just wide: "aaaah-ohhh", the breath of the end falling.</summary>
    void Groan(int end, double t, float level)
    {
        var bus = EndBus(end);
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.16f * level, t + 0.12).Target(0.0001f, t + 0.6, 0.4f);
        var f1 = new Biquad(FilterType.Bandpass, Formants[0].f1, 2.5f);
        f1.Freq.Set(Formants[0].f1, t).Lin(Formants[3].f1, t + 1.0);
        var f2 = new Biquad(FilterType.Bandpass, Formants[0].f2, 3);
        f2.Freq.Set(Formants[0].f2, t).Lin(Formants[3].f2, t + 1.0);
        Breath(bus, t, 2.2, 2.4f, g, f1, f2);
        _mx.Burst(t, 1.6, FilterType.Bandpass, 700, 0.6f, 0.12f * level, 1, bus);
    }

    /// <summary>The referee's given a foul against them: a long, low boo, and the whistlers.</summary>
    void Boo(int end, double t, float level)
    {
        var bus = EndBus(end);
        Bark(bus, t, 1.8, Vowel.U, 1.5f * level);
        Bark(bus, t + 0.1, 1.6, Vowel.O, 1.0f * level);
        for (int i = 0; i < 6; i++) Whistler(bus, t + _mx.Rand() * 0.5, 0, 0.45f * level);
    }

    /// <summary>Breath of many mouths through two formant filters, shaped by g.</summary>
    void Breath(Bus bus, double t, double dur, float gain, Param g, Biquad b1, Biquad b2, float w2 = 1)
    {
        var v = _mx.NoiseVoice(t, t + dur, 1, g, bus, b1, b2);
        v.Pre = gain;
        v.W2 = w2;
    }

    /// <summary>A section of the crowd shouting a vowel, breath only (the "HUH!", boos, olés).</summary>
    void Bark(Bus bus, double t, double dur, Vowel v, float level)
    {
        var (f1, f2) = Formants[(int)v];
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

    /// <summary>A two-finger whistle: up, held, and down.</summary>
    void Whistler(Bus bus, double t, float pan, float level)
    {
        float f = 2300 + _mx.Rand() * 1300;
        double d = 0.35 + _mx.Rand() * 0.7;
        var fp = new Param(f * 0.8f).Set(f * 0.8f, t).Exp(f, t + 0.07).Lin(f * (0.94f + _mx.Rand() * 0.1f), t + d - 0.08).Exp(f * 0.75f, t + d);
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.02f * level, t + 0.05).Target(0.0001f, t + d - 0.06, 0.04f);
        _mx.Add(new Voice { Kind = Voice.Src.Osc, Wave = Wave.Sine, Freq = fp, Gain = g, Out = bus, Start = t, Stop = t + d + 0.1 }.At(pan));
    }

    /// <summary>Applause: hands clapping out of step, rising, holding and dying away over `dur`.</summary>
    void Applause(Bus bus, double t, double dur, float level, float density = 900)
    {
        const int M = 40;
        var env = new float[M];
        var amp = new float[M];
        var pos = new int[M];
        var dec = new float[M];
        var noise = _mx.Noise;
        int nn = noise.Length, slot = 0;
        uint rng = (uint)(_mx.Rand() * uint.MaxValue) | 1;
        float sr = _mx.Sr, p = density / sr;
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
        _mx.Add(new Voice
        {
            Kind = Voice.Src.Custom, Custom = src, F1 = new Biquad(FilterType.Bandpass, 1700, 0.6f), F2 = new Biquad(FilterType.Peaking, 3200, 1, 4),
            Series = true, Gain = g, Out = bus, Start = t, Stop = end + 1.5,
        });
    }

    /// <summary>The PA's three-note chime, from the near speakers and then across the ground.</summary>
    void Chime(double t, float level)
    {
        float[] notes = { 784, 659, 523 };
        foreach (var (delay, gain, pan) in new[] { (0.0, 1f, 0f), (0.21, 0.5f, -0.6f), (0.47, 0.3f, 0.6f) })
            for (int i = 0; i < 3; i++)
            {
                double at = t + delay + i * 0.42;
                float v = 0.05f * level * gain;
                _mx.Add(new Voice { Kind = Voice.Src.Osc, Wave = Wave.Sine, Freq = new Param(notes[i]), Gain = new Param(0.0001f).Set(0.0001f, at).Exp(v, at + 0.01).Exp(0.0001f, at + 1.6), Out = Bus.Bowl, Start = at, Stop = at + 1.7 }.At(pan));
            }
    }
}
