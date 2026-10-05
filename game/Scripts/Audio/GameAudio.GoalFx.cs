using System;

namespace GameNight.Audio;

/// <summary>The goal explosions' sounds (World/GoalFx.cs): cue 0 is the blast itself, the rest
/// are each style's own later moments.</summary>
public sealed partial class GameAudio
{
    public void GoalFx(int style, int cue) => Do(() =>
    {
        double t = _mx.Now;
        switch (style * 10 + cue)
        {
            case 0: // Supernova: a deep boom, a crackling tail, a shimmer.
                Boom(t, 1);
                Crackle(t + 0.15, 1.3, 34, 4200);
                _mx.Tone(t, 1760, 1.3, Wave.Triangle, 0.04f, 880, 0.02);
                break;

            case 10: // Fireworks: the mortar's pop.
                _mx.Burst(t, 0.14, FilterType.Bandpass, 1600, 1, 0.3f, 1.2f);
                _mx.Tone(t, 160, 0.2, Wave.Sine, 0.3f, 60);
                break;
            case 13: // A rocket whistling up.
                _mx.Tone(t, 900 + _mx.Rand() * 300, 0.95, Wave.Sine, 0.05f, 2600 + _mx.Rand() * 500, 0.08);
                _mx.Burst(t, 0.8, FilterType.Highpass, 3200, 0.6f, 0.05f, 1.4f);
                break;
            case 11: // A shell bursting up high.
                Boom(t + 0.05, 0.55f);
                break;
            case 12: // Crackle.
                Crackle(t + 0.45, 0.9, 46, 5200);
                break;

            case 20: // Thunderstrike: electricity humming over the frame.
                _mx.Tone(t, 118, 2.6, Wave.Sawtooth, 0.045f, 96, 0.03);
                _mx.Burst(t, 2.6, FilterType.Bandpass, 3400, 2.5f, 0.05f, 1.8f);
                break;
            case 21: // A strike: a crack and a short roll.
            {
                _mx.Burst(t, 0.22, FilterType.Highpass, 1800, 0.7f, 0.5f, 1.2f);
                _mx.Burst(t, 0.55, FilterType.Bandpass, 900, 0.7f, 0.4f, 0.8f);
                var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.4f, t + 0.06).Exp(0.18f, t + 0.6).Exp(0.0001f, t + 2.4);
                _mx.NoiseVoice(t, t + 2.5, 0.35f, g, Bus.Master, new Biquad(FilterType.Lowpass, 220, 0.9f), new Biquad(FilterType.Peaking, 60, 1, 6));
                break;
            }

            case 30: // Volcano: a boom, a long rumble, lava hissing, rocks spitting.
            {
                Boom(t, 1.1f);
                var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.45f, t + 0.3).Exp(0.3f, t + 1.6).Exp(0.0001f, t + 3.2);
                _mx.NoiseVoice(t, t + 3.3, 0.3f, g, Bus.Master, new Biquad(FilterType.Lowpass, 95, 0.8f), new Biquad(FilterType.Peaking, 45, 1, 6));
                _mx.Burst(t + 0.2, 2.6, FilterType.Highpass, 5200, 0.5f, 0.08f, 1.5f);
                Crackle(t + 0.1, 1.6, 30, 2400);
                break;
            }

            case 40: // Black hole: a falling, swelling suck of air and a sinking drone.
            {
                double e = t + 1.32;
                var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.12f, e - 0.05).Exp(0.0001f, e + 0.05);
                _mx.Osc(Wave.Sawtooth, t, 520, e + 0.1, g, Bus.Master, new Param(520).Set(520, t).Exp(42, e));
                var bp = new Biquad(FilterType.Bandpass, 3000, 1.4f);
                bp.Freq.Set(3000, t).Exp(180, e);
                var gn = new Param(0.0001f).Set(0.0001f, t).Exp(0.3f, e - 0.05).Exp(0.0001f, e + 0.04);
                _mx.NoiseVoice(t, e + 0.1, 1, gn, Bus.Master, bp);
                break;
            }
            case 41: // ...and the blast, with a ringing chord.
                Boom(t, 1.1f);
                foreach (float f in new[] { 1047f, 1319f, 1568f, 2093f })
                    _mx.Tone(t + 0.02, f, 1.8, Wave.Triangle, 0.035f, f * 0.98f, 0.01);
                break;

            case 50: // Frostbite: a dull thump, ice cracking, chimes and a cold wind.
                Boom(t, 0.55f);
                for (int i = 0; i < 4; i++) _mx.Burst(t + i * 0.05 + _mx.Rand() * 0.03, 0.08, FilterType.Highpass, 3500, 1, 0.3f, 1.4f);
                for (int i = 0; i < 9; i++) _mx.Tone(t + 0.05 + i * 0.09 + _mx.Rand() * 0.04, 2000 + _mx.Rand() * 2400, 0.7, Wave.Sine, 0.045f, 0, 0.003);
                _mx.Burst(t, 2.8, FilterType.Bandpass, 700, 0.5f, 0.09f, 0.6f);
                break;
            case 51: // The spikes shatter.
                _mx.Burst(t, 0.5, FilterType.Highpass, 4000, 0.6f, 0.32f, 1.3f);
                for (int i = 0; i < 14; i++) _mx.Tone(t + _mx.Rand() * 0.35, 2500 + _mx.Rand() * 2800, 0.16, Wave.Sine, 0.05f, 0, 0.002);
                break;

            case 60: // Party cannon: two thumps and a party horn.
                Thump(t);
                Thump(t + 0.04);
                Horn(t + 0.1);
                break;
            case 61:
                Thump(t);
                Thump(t + 0.05);
                break;
            case 62: // A balloon pops.
                _mx.Burst(t, 0.06, FilterType.Bandpass, 1300, 0.8f, 0.28f, 1.6f);
                _mx.Tone(t, 900, 0.05, Wave.Sine, 0.08f, 300);
                break;

            case 70: // Arcade: a chip-tune boom and coins.
                _mx.Tone(t, 220, 0.45, Wave.Sawtooth, 0.1f, 50);
                _mx.Burst(t, 0.5, FilterType.Lowpass, 1500, 0.6f, 0.3f, 0.5f);
                for (int i = 0; i < 6; i++)
                {
                    double c = t + 0.2 + i * 0.13 + _mx.Rand() * 0.05;
                    _mx.Tone(c, 988, 0.07, Wave.Triangle, 0.07f);
                    _mx.Tone(c + 0.06, 1319, 0.22, Wave.Triangle, 0.07f);
                }
                break;
            case 71: // The power-up jingle as GOAL! lines up.
            {
                float[] notes = { 523, 659, 784, 1047, 1319, 1568, 2093 };
                for (int i = 0; i < notes.Length; i++)
                {
                    _mx.Tone(t + i * 0.06, notes[i], 0.16, Wave.Triangle, 0.07f);
                    _mx.Tone(t + i * 0.06, notes[i] * 0.5f, 0.14, Wave.Sawtooth, 0.02f);
                }
                foreach (float f in new[] { 1047f, 1319f, 1568f })
                    _mx.Tone(t + 0.45, f, 0.9, Wave.Triangle, 0.05f);
                break;
            }
            case 72: // The letters burst.
                _mx.Tone(t, 1600, 0.35, Wave.Sawtooth, 0.05f, 180);
                _mx.Burst(t, 0.3, FilterType.Lowpass, 2000, 0.6f, 0.2f, 0.6f);
                break;

            case 80: // Phoenix: a rising rush of fire.
            {
                var bp = new Biquad(FilterType.Bandpass, 300, 1.1f);
                bp.Freq.Set(300, t).Exp(2600, t + 1.2);
                var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.26f, t + 1.15).Exp(0.0001f, t + 1.5);
                _mx.NoiseVoice(t, t + 1.6, 1, g, Bus.Master, bp);
                _mx.Burst(t, 1.6, FilterType.Lowpass, 600, 0.6f, 0.22f, 0.6f);
                break;
            }
            case 81: // Its cry.
                _mx.Tone(t, 1500, 0.9, Wave.Sawtooth, 0.04f, 2000, 0.08);
                _mx.Tone(t + 0.12, 2300, 0.8, Wave.Sine, 0.05f, 1500, 0.04);
                _mx.Burst(t, 1.2, FilterType.Lowpass, 700, 0.6f, 0.18f, 0.6f);
                break;
            case 82: // It bursts into embers.
                Boom(t, 0.75f);
                Crackle(t, 1.6, 28, 3800);
                break;

            case 90: // Meteor: a falling scream, then the impact and the rubble.
            {
                double hit = t + 0.7;
                _mx.Tone(t, 2600, 0.75, Wave.Sine, 0.07f, 380, 0.6);
                var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.22f, hit - 0.02).Exp(0.0001f, hit + 0.05);
                _mx.NoiseVoice(t, hit + 0.1, 0.8f, g, Bus.Master, new Biquad(FilterType.Bandpass, 900, 0.6f));
                Boom(hit, 1.45f);
                var r = new Param(0.0001f).Set(0.0001f, hit).Exp(0.5f, hit + 0.1).Exp(0.0001f, hit + 3.2);
                _mx.NoiseVoice(hit, hit + 3.3, 0.3f, r, Bus.Master, new Biquad(FilterType.Lowpass, 85, 0.8f), new Biquad(FilterType.Peaking, 40, 1, 6));
                Crackle(hit + 0.2, 1.6, 24, 1500);
                break;
            }
        }
    });

    /// <summary>A blast: a falling sub thump, a wash of low noise and a crack on top.</summary>
    void Boom(double t, float size)
    {
        _mx.Tone(t, 95, 1.1, Wave.Sine, 0.55f * size, 30, 0.004);
        _mx.Burst(t, 1.3 + size * 0.3, FilterType.Lowpass, 900, 0.6f, 0.5f * size, 0.7f);
        _mx.Burst(t, 0.28, FilterType.Bandpass, 2400, 0.8f, 0.2f * size, 1.4f);
    }

    /// <summary>A crackle: many tiny pops scattered over `dur` seconds.</summary>
    void Crackle(double t, double dur, int n, float freq)
    {
        for (int i = 0; i < n; i++)
        {
            float k = _mx.Rand();
            _mx.Burst(t + k * k * dur, 0.03, FilterType.Bandpass, freq * (0.7f + _mx.Rand() * 0.6f), 1.2f, 0.05f + _mx.Rand() * 0.08f, 1.6f);
        }
    }

    void Thump(double t)
    {
        _mx.Tone(t, 140, 0.25, Wave.Sine, 0.45f, 50);
        _mx.Burst(t, 0.25, FilterType.Lowpass, 1500, 0.7f, 0.35f, 1);
    }

    /// <summary>A party horn: a buzzy blare that bends up.</summary>
    void Horn(double t)
    {
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.07f, t + 0.04).Set(0.07f, t + 0.6).Exp(0.0001f, t + 0.75);
        var f = new Param(440).Set(440, t).Exp(470, t + 0.1).Exp(520, t + 0.6);
        _mx.Add(new Voice { Kind = Voice.Src.Osc, Wave = Wave.Sawtooth, Freq = f, Gain = g, F1 = new Biquad(FilterType.Lowpass, 2200), Out = Bus.Master, Start = t, Stop = t + 0.8 });
    }
}
