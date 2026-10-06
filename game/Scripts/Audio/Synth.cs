using System;
using System.Collections.Generic;

namespace GameNight.Audio;

/// <summary>
/// The few pieces of WebAudio the PWA's sound is made of, so its recipes port line for line:
/// automatable parameters (set / linear / exponential ramps / set-target), oscillators, the
/// shared noise loop, recordings, biquad filters, a compressor and a stadium reverb. Times are
/// in seconds on the synth's own clock (frames rendered / mix rate).
/// </summary>
public sealed class Param
{
    enum K : byte { Set, Lin, Exp, Target }

    struct Ev
    {
        public K Kind;
        public double T;
        public float V, Tau;
    }

    readonly List<Ev> _ev = new();
    int _k;
    double _baseT;
    float _baseV;

    public Param(float value) => _baseV = value;

    public Param Set(float v, double t) => Add(K.Set, t, v);
    public Param Lin(float v, double t) => Add(K.Lin, t, v);
    public Param Exp(float v, double t) => Add(K.Exp, t, v);
    public Param Target(float v, double t, float tau) => Add(K.Target, t, v, Math.Max(1e-4f, tau));

    Param Add(K kind, double t, float v, float tau = 0)
    {
        var e = new Ev { Kind = kind, T = t, V = v, Tau = tau };
        int i = _ev.Count;
        while (i > _k && _ev[i - 1].T > t) i--;
        _ev.Insert(i, e);
        return this;
    }

    /// <summary>The value at t (t only moves forward).</summary>
    public float At(double t)
    {
        while (_k < _ev.Count && _ev[_k].T <= t)
        {
            var e = _ev[_k];
            _baseV = e.Kind == K.Target ? Eval(e.T) : e.V;
            _baseT = e.T;
            _k++;
        }
        if (_k > 24)
        {
            _ev.RemoveRange(0, _k - 1);
            _k = 1;
        }
        return Eval(t);
    }

    float Eval(double t)
    {
        if (_k < _ev.Count)
        {
            var n = _ev[_k];
            if (n.Kind == K.Lin || n.Kind == K.Exp)
            {
                double span = n.T - _baseT;
                double f = span <= 0 ? 1 : Math.Clamp((t - _baseT) / span, 0, 1);
                if (n.Kind == K.Lin) return (float)(_baseV + (n.V - _baseV) * f);
                if (_baseV * n.V <= 0) return _baseV;
                return (float)(_baseV * Math.Pow(n.V / _baseV, f));
            }
        }
        if (_k > 0)
        {
            var p = _ev[_k - 1];
            if (p.Kind == K.Target) return (float)(p.V + (_baseV - p.V) * Math.Exp(-(t - p.T) / p.Tau));
        }
        return _baseV;
    }
}

public enum Wave : byte { Sine, Triangle, Sawtooth }
public enum FilterType : byte { Lowpass, Highpass, Bandpass, Peaking }

/// <summary>A WebAudio BiquadFilterNode (its cookbook formulas; low/high-pass Q in dB, as WebAudio has it).</summary>
public sealed class Biquad
{
    public readonly FilterType Type;
    public readonly Param Freq;
    public float Q, GainDb;
    float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2, _lastF = -1;
    int _until;

    public Biquad(FilterType type, float freq, float q = 1, float gainDb = 0)
    {
        Type = type;
        Freq = new Param(freq);
        Q = q;
        GainDb = gainDb;
    }

    public float Run(float x, double t, float sr)
    {
        // Coefficients follow the frequency (re-solved every 32 samples while it moves).
        if (--_until <= 0)
        {
            _until = 32;
            float f = Freq.At(t);
            if (f != _lastF) Solve(f, sr);
        }
        float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y;
    }

    void Solve(float f, float sr)
    {
        _lastF = f;
        double w0 = 2 * Math.PI * Math.Clamp(f, 10, sr * 0.49) / sr, cs = Math.Cos(w0), sn = Math.Sin(w0);
        double b0, b1, b2, a0, a1, a2;
        switch (Type)
        {
            case FilterType.Lowpass:
            case FilterType.Highpass:
            {
                double alpha = sn / (2 * Math.Pow(10, Q / 20));
                double s = Type == FilterType.Lowpass ? 1 - cs : 1 + cs;
                b0 = s / 2;
                b1 = Type == FilterType.Lowpass ? s : -s;
                b2 = s / 2;
                a0 = 1 + alpha;
                a1 = -2 * cs;
                a2 = 1 - alpha;
                break;
            }
            case FilterType.Bandpass:
            {
                double alpha = sn / (2 * Math.Max(1e-4, Q));
                b0 = alpha;
                b1 = 0;
                b2 = -alpha;
                a0 = 1 + alpha;
                a1 = -2 * cs;
                a2 = 1 - alpha;
                break;
            }
            default:
            {
                double A = Math.Pow(10, GainDb / 40), alpha = sn / (2 * Math.Max(1e-4, Q));
                b0 = 1 + alpha * A;
                b1 = -2 * cs;
                b2 = 1 - alpha * A;
                a0 = 1 + alpha / A;
                a1 = -2 * cs;
                a2 = 1 - alpha / A;
                break;
            }
        }
        _b0 = (float)(b0 / a0);
        _b1 = (float)(b1 / a0);
        _b2 = (float)(b2 / a0);
        _a1 = (float)(a1 / a0);
        _a2 = (float)(a2 / a0);
    }
}

/// <summary>Where a voice plays: a mono or stereo bus of the mix. Bowl is the stands at large
/// (stereo, placed by the voice's own pan) through the ground's reverb.</summary>
public enum Bus : byte { Master, Crowd, Bed, End0, End1, Bowl, Ui }

/// <summary>
/// One sounding thing: a source (oscillator, noise, recording, or a custom generator), through
/// up to two filters (in series, or in parallel with their own levels), an envelope and an
/// optional second gain, into a bus. It plays from Start until Stop.
/// </summary>
public sealed class Voice
{
    // Source
    public enum Src : byte { Osc, Noise, Sample, Custom }
    public Src Kind;
    public Wave Wave;
    public Param Freq;
    public double Phase;
    public float Rate = 1;
    public double Pos;
    public Recording Rec;
    public bool Loop;
    public double LoopStart, LoopEnd;
    public Func<double, float> Custom;

    // Path
    public Biquad F1, F2;
    public float W1 = 1, W2 = 1;
    public bool Series;
    public float Pre = 1;
    public Param Gain, Gain2;
    /// <summary>Left and right levels on a stereo bus (1, 1 is the centre).</summary>
    public float PanL = 1, PanR = 1;
    public Bus Out;
    public double Start, Stop = double.MaxValue;
    public bool Done;

    /// <summary>Place it from -1 (left) to 1 (right), equal power.</summary>
    public Voice At(float pan)
    {
        float x = (Math.Clamp(pan, -1, 1) + 1) * MathF.PI / 4;
        PanL = MathF.Cos(x) * 1.4142f;
        PanR = MathF.Sin(x) * 1.4142f;
        return this;
    }
}

/// <summary>A decoded recording (stereo, at its own rate).</summary>
public sealed class Recording
{
    public float[] L, R;
    public int Rate;
    public double Duration => L.Length / (double)Rate;

    /// <summary>Raw 16-bit little-endian stereo PCM.</summary>
    public static Recording FromPcm(byte[] data, int rate)
    {
        int n = data.Length / 4;
        var r = new Recording { L = new float[n], R = new float[n], Rate = rate };
        for (int i = 0; i < n; i++)
        {
            r.L[i] = (short)(data[4 * i] | data[4 * i + 1] << 8) / 32768f;
            r.R[i] = (short)(data[4 * i + 2] | data[4 * i + 3] << 8) / 32768f;
        }
        return r;
    }
}

/// <summary>A WebAudio-style DynamicsCompressor: soft knee, attack/release, and its automatic make-up gain.</summary>
public sealed class Compressor
{
    readonly float _thr, _knee, _ratio, _att, _rel, _makeup;
    float _gainDb;

    public Compressor(float sr, float threshold, float knee, float ratio, float attack, float release)
    {
        _thr = threshold;
        _knee = knee;
        _ratio = ratio;
        _att = 1 - MathF.Exp(-1 / (attack * sr));
        _rel = 1 - MathF.Exp(-1 / (release * sr));
        // Chrome lifts the output by the full-scale reduction to the power 0.6.
        _makeup = MathF.Pow(10, -Curve(0) / 20 * 0.6f);
    }

    /// <summary>Gain reduction (dB, ≤ 0) for an input level in dB.</summary>
    float Curve(float x)
    {
        float lo = _thr - _knee / 2, hi = _thr + _knee / 2;
        if (x <= lo) return 0;
        if (x >= hi) return _thr + (x - _thr) / _ratio - x;
        float d = x - lo;
        return (1 / _ratio - 1) * d * d / (2 * _knee);
    }

    public float Gain(float peak)
    {
        float want = peak > 1e-6f ? Curve(20 * MathF.Log10(peak)) : 0;
        _gainDb += (want - _gainDb) * (want < _gainDb ? _att : _rel);
        return MathF.Pow(10, _gainDb / 20) * _makeup;
    }
}

/// <summary>
/// The bowl's reverb: a long, dark Freeverb tail with a slap-back off the far stand (the PWA
/// convolves a decaying noise impulse; this is its cheap twin). Each ground tunes it
/// (<see cref="Configure"/>): a roof holds a longer, brighter tail; an open bowl with a track
/// round the pitch throws back a later, clearer echo.
/// </summary>
public sealed class Reverb
{
    static readonly int[] Combs = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
    static readonly int[] Alls = { 556, 441, 341, 225 };
    const int Spread = 23;

    readonly float[][] _cb = new float[16][], _ab = new float[8][];
    readonly int[] _ci = new int[16], _ai = new int[8];
    readonly float[] _cs = new float[16];
    readonly float[] _slap;
    int _si, _slapLen;
    float _feedback = 0.86f, _damp = 0.55f, _slapGain = 0.8f;

    public Reverb(float sr)
    {
        float k = sr / 44100f;
        for (int c = 0; c < 2; c++)
        {
            for (int i = 0; i < 8; i++) _cb[c * 8 + i] = new float[(int)((Combs[i] + c * Spread) * k)];
            for (int i = 0; i < 4; i++) _ab[c * 4 + i] = new float[(int)((Alls[i] + c * Spread) * k)];
        }
        _slap = new float[(int)(0.45f * sr)];
        _slapLen = (int)(0.2f * sr);
        _sr = sr;
    }

    readonly float _sr;

    /// <summary>The tail's feedback (length) and damping (darkness), and the far stand's echo.</summary>
    public void Configure(float feedback, float damp, float slapSeconds, float slapGain)
    {
        _feedback = Math.Clamp(feedback, 0.5f, 0.92f);
        _damp = Math.Clamp(damp, 0.1f, 0.8f);
        _slapLen = Math.Clamp((int)(slapSeconds * _sr), 1, _slap.Length);
        _slapGain = slapGain;
        if (_si >= _slapLen) _si = 0;
    }

    public void Run(float inL, float inR, out float l, out float r)
    {
        float x = (inL + inR) * 0.015f;
        float slap = _slap[_si];
        _slap[_si] = x;
        if (++_si >= _slapLen) _si = 0;
        x += slap * _slapGain;
        l = Chan(0, x);
        r = Chan(1, x);
    }

    float Chan(int c, float x)
    {
        float o = 0;
        for (int i = 0; i < 8; i++)
        {
            int j = c * 8 + i;
            var b = _cb[j];
            float y = b[_ci[j]];
            _cs[j] = y * (1 - _damp) + _cs[j] * _damp;
            b[_ci[j]] = x + _cs[j] * _feedback;
            if (++_ci[j] >= b.Length) _ci[j] = 0;
            o += y;
        }
        for (int i = 0; i < 4; i++)
        {
            int j = c * 4 + i;
            var b = _ab[j];
            float bo = b[_ai[j]];
            b[_ai[j]] = o + bo * 0.5f;
            if (++_ai[j] >= b.Length) _ai[j] = 0;
            o = bo - o;
        }
        return o * 3;
    }
}
