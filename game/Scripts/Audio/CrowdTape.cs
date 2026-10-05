using System;
using System.Collections.Generic;

namespace GameNight.Audio;

/// <summary>
/// The crowd, as the PWA does it: the real recordings, not synthesised voices. The bed is the
/// recorded crowd looping as five copies a fifth of a loop apart, each at its own slightly
/// different speed and wandering in level, so no loop point is ever heard; it breathes with the
/// match's excitement and surges exponentially in the last metres before a goal line. A goal
/// plays the recorded roar, held through the celebration by staggered, drifting layers of it.
/// Nothing else: the director's moments only lift the bed for a while. Runs on the audio thread.
/// </summary>
public sealed class CrowdTape
{
    readonly Mixer _mx;
    readonly Recording _bed, _roar;
    readonly List<(Param g, double next)> _drift = new();
    int _scheduledId = -1;

    /// <summary>1 at a match; silent until one starts (the loading screen and the menus have no crowd).</summary>
    float _level;
    float _excite = 0.2f, _mouth;
    float _bedSent = -1;
    /// <summary>Moments the whole bed rises for (an end on its feet): start, peak level, rise, hold, fall.</summary>
    readonly List<(double at, float peak, float rise, float hold, float fall)> _swells = new();

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

    // ------------------------------------------------------------------ the director

    /// <summary>Follow the director: nothing is added to the recordings; an end cheering, a
    /// chance, a song or the scorer's name only lifts the bed for a moment.</summary>
    public void Cue(TerraceCue dir)
    {
        double toCtx = _mx.Now + 0.06 - dir.T;
        foreach (var r in dir.Reactions)
        {
            double t = Math.Max(_mx.Now + 0.02, r.At + toCtx);
            float lv = r.Level;
            switch (r.Kind)
            {
                case React.Ooh: Swell(t, 0.6f * lv, 0.25f, 0.4f, 1.5f); break;
                case React.Cheer: Swell(t, 0.7f * lv, 0.15f, 0.4f, 1.2f); break;
                case React.Rally: Swell(t + 2.2, 0.5f * lv, 0.3f, 0.8f, 1.5f); break;
                case React.Ole: Swell(t, 0.4f * lv, 0.1f, 0.5f, 0.8f); break;
                case React.NameCall:
                    for (int i = 0; i < 3; i++) Swell(t + 2.2 + i * 1.9, (0.6f + 0.2f * i) * lv, 0.08f, 0.5f, 0.8f);
                    break;
            }
        }
        var s = dir.Singing;
        if (s == null || s.Id == _scheduledId) return;
        _scheduledId = s.Id;
        Swell(Math.Max(_mx.Now, s.Start + toCtx), 0.25f * dir.Level * (s.End == 0 ? 1 : 0.6f), 2.5f, (float)Math.Max(0, s.Until - s.Start - 4.5), 2);
    }
}
