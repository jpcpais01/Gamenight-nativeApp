using System;
using System.Collections.Generic;

namespace GameNight.Audio;

/// <summary>
/// The mix, as the PWA wires it: voices into the master, the crowd bus (bed, terraces, gasps,
/// roars) and its two ends; the bed through its own limiter and level; the ends panned to
/// their side of the ground through a dark lowpass and the bowl's reverb; the master through
/// a gentle compressor. Rendered a block at a time on the audio thread.
/// </summary>
public sealed class Mixer
{
    public const int Block = 256;
    public readonly float Sr;
    readonly double _dt;
    long _frames;
    /// <summary>The synth clock: the time of the next sample to be rendered.</summary>
    public double Now => _frames * _dt;

    readonly List<Voice> _voices = new();
    readonly float[] _mL = new float[Block], _mR = new float[Block];
    readonly float[] _cL = new float[Block], _cR = new float[Block];
    readonly float[] _bL = new float[Block], _bR = new float[Block];
    readonly float[] _e0 = new float[Block], _e1 = new float[Block];

    public readonly float[] Noise;
    readonly Random _rng = new();

    public readonly Param MasterGain = new(0.9f);
    /// <summary>The crowd bed's overall level.</summary>
    public readonly Param BedGain = new(0.3f);
    /// <summary>Everything the crowd makes (off at a ground without one).</summary>
    public readonly Param CrowdGain = new(1);
    /// <summary>The terraces' overall level (menus sink it).</summary>
    public readonly Param EndsGain = new(1);
    public bool CrowdOn = true;
    double _crowdOffAt = double.MaxValue;

    readonly Compressor _master, _limiter;
    readonly Biquad _toneL, _toneR;
    readonly Reverb _verb;
    static readonly float[] EndGain = { 1, 0.62f };
    readonly float[] _panL = new float[2], _panR = new float[2];

    public Mixer(float sr)
    {
        Sr = sr;
        _dt = 1.0 / sr;
        _master = new Compressor(sr, -14, 30, 4, 0.003f, 0.25f);
        _limiter = new Compressor(sr, -8, 4, 20, 0.005f, 0.2f);
        _toneL = new Biquad(FilterType.Lowpass, 3800);
        _toneR = new Biquad(FilterType.Lowpass, 3800);
        _verb = new Reverb(sr);
        float[] pan = { -0.55f, 0.55f };
        for (int e = 0; e < 2; e++)
        {
            float x = (pan[e] + 1) / 2;
            _panL[e] = MathF.Cos(x * MathF.PI / 2) * EndGain[e];
            _panR[e] = MathF.Sin(x * MathF.PI / 2) * EndGain[e];
        }

        // Pink-ish noise, shared by everything.
        int len = (int)sr * 4;
        Noise = new float[len];
        float b0 = 0, b1 = 0, b2 = 0;
        for (int i = 0; i < len; i++)
        {
            float w = Rand() * 2 - 1;
            b0 = 0.99765f * b0 + w * 0.099046f;
            b1 = 0.963f * b1 + w * 0.2965164f;
            b2 = 0.57f * b2 + w * 1.0526913f;
            Noise[i] = (b0 + b1 + b2 + w * 0.1848f) * 0.18f;
        }
    }

    public float Rand() => (float)_rng.NextDouble();

    public void Add(Voice v) => _voices.Add(v);

    public void SetCrowd(bool on)
    {
        CrowdOn = on;
        _crowdOffAt = on ? double.MaxValue : Now + 1.5;
        CrowdGain.Target(on ? 1 : 0, Now, 0.2f);
    }

    // ------------------------------------------------------------------ recipes' building blocks

    /// <summary>An oscillator with an envelope (the PWA's tone()).</summary>
    public Voice Osc(Wave w, double t, float freq, double stop, Param gain, Bus bus = Bus.Master, Param freqP = null)
    {
        var v = new Voice { Kind = Voice.Src.Osc, Wave = w, Freq = freqP ?? new Param(freq), Gain = gain, Out = bus, Start = t, Stop = stop };
        Add(v);
        return v;
    }

    /// <summary>The noise loop from a random place, through a filter, with an envelope.</summary>
    public Voice NoiseVoice(double t, double stop, float rate, Param gain, Bus bus, Biquad f1 = null, Biquad f2 = null)
    {
        var v = new Voice { Kind = Voice.Src.Noise, Rate = rate, Pos = Rand() * 3 * Sr, F1 = f1, F2 = f2, Gain = gain, Out = bus, Start = t, Stop = stop };
        Add(v);
        return v;
    }

    /// <summary>The PWA's noiseBurst: a quick filtered hiss, up in 5 ms and away by dur.</summary>
    public void Burst(double t, double dur, FilterType type, float freq, float q, float gain, float rate = 1, Bus bus = Bus.Master)
    {
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(Math.Max(0.0002f, gain), t + 0.005).Exp(0.0001f, t + dur);
        NoiseVoice(t, t + dur + 0.05, rate, g, bus, new Biquad(type, freq, q));
    }

    /// <summary>The PWA's tone(): one oscillator, a short attack, an exponential fall (and an optional glide).</summary>
    public void Tone(double t, float freq, double dur, Wave w, float gain, float freqEnd = 0, double attack = 0.005, Bus bus = Bus.Master)
    {
        var f = new Param(freq).Set(freq, t);
        if (freqEnd > 0 && freqEnd != freq) f.Exp(freqEnd, t + dur);
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(gain, t + attack).Exp(0.0001f, t + dur);
        Osc(w, t, freq, t + dur + 0.05, g, bus, f);
    }

    // ------------------------------------------------------------------ render

    public void Render(Godot.Vector2[] outBuf)
    {
        Array.Clear(_mL); Array.Clear(_mR);
        Array.Clear(_cL); Array.Clear(_cR);
        Array.Clear(_bL); Array.Clear(_bR);
        Array.Clear(_e0); Array.Clear(_e1);
        double t0 = Now;
        bool crowd = CrowdOn || t0 < _crowdOffAt;

        for (int k = _voices.Count - 1; k >= 0; k--)
        {
            var v = _voices[k];
            if (v.Out != Bus.Master && !crowd)
            {
                if (t0 >= v.Stop) v.Done = true;
            }
            else RenderVoice(v, t0);
            if (v.Done)
            {
                _voices[k] = _voices[^1];
                _voices.RemoveAt(_voices.Count - 1);
            }
        }

        for (int i = 0; i < Block; i++)
        {
            double t = t0 + i * _dt;
            float l = _mL[i], r = _mR[i];
            if (crowd)
            {
                // The bed, levelled and limited.
                float bg = BedGain.At(t);
                float bl = _bL[i] * bg, br = _bR[i] * bg;
                float lim = _limiter.Gain(MathF.Max(MathF.Abs(bl), MathF.Abs(br)));
                float cl = _cL[i] + bl * lim, cr = _cR[i] + br * lim;
                // The two ends, panned, through the dark lowpass, dry and into the bowl.
                float eg = EndsGain.At(t);
                float el = (_e0[i] * _panL[0] + _e1[i] * _panL[1]) * eg;
                float er = (_e0[i] * _panR[0] + _e1[i] * _panR[1]) * eg;
                el = _toneL.Run(el, t, Sr);
                er = _toneR.Run(er, t, Sr);
                _verb.Run(el, er, out float wl, out float wr);
                cl += 0.55f * (el + wl);
                cr += 0.55f * (er + wr);
                float cg = CrowdGain.At(t);
                l += cl * cg;
                r += cr * cg;
            }
            float mg = MasterGain.At(t);
            l *= mg;
            r *= mg;
            float comp = _master.Gain(MathF.Max(MathF.Abs(l), MathF.Abs(r)));
            outBuf[i] = new Godot.Vector2(Math.Clamp(l * comp, -1, 1), Math.Clamp(r * comp, -1, 1));
        }
        _frames += Block;
    }

    void RenderVoice(Voice v, double t0)
    {
        int first = (int)Math.Ceiling((v.Start - t0) * Sr);
        if (first >= Block) return;
        first = Math.Max(0, first);
        float[] L, R;
        switch (v.Out)
        {
            case Bus.Master: L = _mL; R = _mR; break;
            case Bus.Crowd: L = _cL; R = _cR; break;
            case Bus.Bed: L = _bL; R = _bR; break;
            case Bus.End0: L = R = _e0; break;
            default: L = R = _e1; break;
        }
        bool mono = L == R;
        int nNoise = Noise.Length;
        for (int i = first; i < Block; i++)
        {
            double t = t0 + i * _dt;
            if (t >= v.Stop)
            {
                v.Done = true;
                return;
            }
            float s = 0, sr = 0;
            bool stereo = false;
            switch (v.Kind)
            {
                case Voice.Src.Osc:
                {
                    float f = v.Freq.At(t);
                    double p = v.Phase;
                    s = v.Wave switch
                    {
                        Wave.Sine => MathF.Sin((float)(p * 2 * Math.PI)),
                        Wave.Triangle => (float)(p < 0.25 ? 4 * p : p < 0.75 ? 2 - 4 * p : 4 * p - 4),
                        _ => Saw(p, f / Sr),
                    };
                    p += f / Sr;
                    v.Phase = p - Math.Floor(p);
                    break;
                }
                case Voice.Src.Noise:
                {
                    int j = (int)v.Pos;
                    float fr = (float)(v.Pos - j);
                    s = Noise[j % nNoise] * (1 - fr) + Noise[(j + 1) % nNoise] * fr;
                    v.Pos += v.Rate;
                    if (v.Pos >= nNoise) v.Pos -= nNoise;
                    break;
                }
                case Voice.Src.Sample:
                {
                    var rec = v.Rec;
                    int j = (int)v.Pos, j1 = j + 1;
                    if (j1 >= rec.L.Length)
                    {
                        if (!v.Loop || j >= rec.L.Length)
                        {
                            v.Done = true;
                            return;
                        }
                        // Looping: the last sample blends into the loop's first.
                        j1 = (int)(v.LoopStart * rec.Rate);
                    }
                    float fr = (float)(v.Pos - j);
                    s = rec.L[j] * (1 - fr) + rec.L[j1] * fr;
                    sr = rec.R[j] * (1 - fr) + rec.R[j1] * fr;
                    stereo = true;
                    v.Pos += v.Rate * rec.Rate / Sr;
                    if (v.Loop && v.Pos >= v.LoopEnd * rec.Rate) v.Pos -= (v.LoopEnd - v.LoopStart) * rec.Rate;
                    break;
                }
                default:
                    s = v.Custom(t);
                    break;
            }
            s *= v.Pre;
            if (v.F1 != null)
            {
                if (v.Series)
                {
                    s = v.F1.Run(s, t, Sr);
                    if (v.F2 != null) s = v.F2.Run(s, t, Sr);
                }
                else
                {
                    float a = v.F1.Run(s, t, Sr) * v.W1;
                    if (v.F2 != null) a += v.F2.Run(s, t, Sr) * v.W2;
                    s = a;
                }
            }
            float g = v.Gain.At(t);
            if (v.Gain2 != null) g *= v.Gain2.At(t);
            if (mono)
            {
                L[i] += (stereo ? (s + sr) * 0.5f : s) * g;
            }
            else
            {
                L[i] += s * g;
                R[i] += (stereo ? sr : s) * g;
            }
        }
    }

    /// <summary>A sawtooth with its step smoothed (polyBLEP), so the high ones don't alias.</summary>
    static float Saw(double p, float dp)
    {
        double q = p + 0.5;
        q -= Math.Floor(q);
        float s = (float)(2 * q - 1);
        if (dp <= 0) return s;
        if (q < dp)
        {
            double x = q / dp;
            s -= (float)(x + x - x * x - 1);
        }
        else if (q > 1 - dp)
        {
            double x = (q - 1) / dp;
            s -= (float)(x * x + x + x + 1);
        }
        return s;
    }
}
