using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Godot;

namespace GameNight.Audio;

/// <summary>
/// The game's sound, all synthesised: strikes, whistle, woodwork, net, weather, menus, and the
/// crowd's soundtrack (<see cref="CrowdScore"/>), whose voices are baked once in the background
/// at start-up (<see cref="CrowdBank"/>) and then only mixed.
///
/// The mix runs on its own thread a block at a time into a stream generator, so a slow frame
/// never stutters it. Calls from the game just queue the sound; it starts on the next block.
/// </summary>
public sealed partial class GameAudio : Node
{
    public static GameAudio Instance { get; private set; }

    AudioStreamPlayer _player;
    AudioStreamGeneratorPlayback _pb;
    Mixer _mx;
    CrowdScore _score;
    Thread _thread;
    volatile bool _run;
    readonly ConcurrentQueue<Action> _q = new();
    readonly Godot.Vector2[] _block = new Godot.Vector2[Mixer.Block];

    Voice _rain;
    bool _raining;
    float _crowd = -1, _placeX = 1e9f, _lift;
    bool _muted, _suspended;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        float sr = AudioServer.GetMixRate();
        _player = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = sr, BufferLength = 0.08f },
            Bus = "Master",
        };
        AddChild(_player);
        _player.Play();
        _pb = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        _mx = new Mixer(sr);
        _score = new CrowdScore(_mx);
        _mx.Score = _score;
        // The crowd's voices take a moment to bake: off the main thread, then the crowd fades in.
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                // Baked once; after that, loaded from where the last bake was saved.
                string path = ProjectSettings.GlobalizePath($"user://crowd-v{CrowdBank.Version}.bin");
                CrowdBank bank = null;
                if (System.IO.File.Exists(path))
                    using (var f = System.IO.File.OpenRead(path)) bank = CrowdBank.Read(f);
                if (bank == null)
                {
                    bank = CrowdBank.Bake();
                    GD.Print($"Crowd baked in {bank.BakeMs:0} ms");
                    try
                    {
                        using var f = System.IO.File.Create(path);
                        bank.Write(f);
                    }
                    catch (Exception e)
                    {
                        GD.PrintErr("Crowd bank not saved: ", e.Message);
                    }
                }
                Do(() => _score.Use(bank));
            }
            catch (Exception e)
            {
                GD.PrintErr("Crowd bake: ", e);
            }
        });

        _run = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Audio", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public override void _ExitTree()
    {
        _run = false;
        _thread?.Join(200);
        if (Instance == this) Instance = null;
    }

    void Loop()
    {
        while (_run)
        {
            while (_q.TryDequeue(out var a)) a();
            if (_pb.GetFramesAvailable() < Mixer.Block)
            {
                Thread.Sleep(2);
                continue;
            }
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                _mx.Render(_block);
            }
            catch (Exception e)
            {
                GD.PrintErr("Audio: ", e);
                Array.Clear(_block);
            }
            _pb.PushBuffer(_block);
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            BlockMs += (ms - BlockMs) * 0.05;
        }
    }

    /// <summary>What one block of sound costs on the audio thread (ms, smoothed), and how long that
    /// block plays: for the frame-time breakdown.</summary>
    public double BlockMs;
    public static double BlockPlayMs => Mixer.Block * 1000.0 / 44100;

    void Do(Action a) => _q.Enqueue(a);

    // ------------------------------------------------------------------ settings

    /// <summary>The ground the match is at: its crowd and how it sounds.</summary>
    public void SetVenue(Venue v) => Do(() => _mx.SetVenue(v));

    /// <summary>Where the camera is along the pitch (x, metres), and how big the moment is (0..1):
    /// the ends' levels, sides and brightness follow.</summary>
    public void Place(float x, float lift)
    {
        if (MathF.Abs(x - _placeX) < 0.75f && MathF.Abs(lift - _lift) < 0.05f) return;
        _placeX = x;
        _lift = lift;
        Do(() => _mx.Place(x, lift));
    }

    /// <summary>A ground with or without a crowd.</summary>
    public void SetCrowd(bool on) => Do(() => _mx.SetCrowd(on));

    /// <summary>Rain on (or off) the stadium: a hiss of drops on the roofs and the turf.</summary>
    public void SetRain(bool on)
    {
        if (on == _raining) return;
        _raining = on;
        Do(() =>
        {
            double t = _mx.Now;
            _mx.Weather(on);
            if (on)
            {
                _rain ??= _mx.NoiseVoice(t, double.MaxValue, 0.83f, new Param(0), Bus.Master,
                    new Biquad(FilterType.Highpass, 1600), new Biquad(FilterType.Peaking, 4200, 1, 5));
                _rain.Series = true;
                _rain.Stop = double.MaxValue;
                _rain.Gain.Target(0.11f, t, 0.6f);
            }
            else if (_rain != null)
            {
                _rain.Gain.Target(0, t, 0.6f);
                _rain.Stop = t + 4;
                _rain = null;
            }
        });
    }

    /// <summary>Thunder after a lightning flash, `delay` seconds later (how far off the strike
    /// was): a crack when it's close, then a long low roll that swells twice and dies away.</summary>
    public void Thunder(float delay) => Do(() =>
    {
        double t = _mx.Now + delay;
        float near = Math.Clamp(1 - delay / 4, 0, 1);
        if (near > 0.3f) _mx.Burst(t, 0.35, FilterType.Bandpass, 900, 0.7f, 0.22f * near, 0.8f);
        var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.5f, t + 0.12).Exp(0.22f, t + 0.9)
            .Exp(0.42f, t + 1.4).Exp(0.12f, t + 2.6).Exp(0.25f, t + 3.1).Exp(0.0001f, t + 6.5);
        _mx.NoiseVoice(t, t + 6.6, 0.35f, g, Bus.Master,
            new Biquad(FilterType.Lowpass, 120 + 160 * near, 0.9f), new Biquad(FilterType.Peaking, 55, 1, 6));
        // A big one right overhead: the crowd jumps, whistles, then cheers itself.
        if (near > 0.55f) _score.Thunder(t + 0.25, near);
    });

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            Do(() => _mx.MasterGain.Target(value ? 0 : 0.9f, _mx.Now, 0.05f));
            Suspended = _suspended;
        }
    }

    /// <summary>Stopped (the pause menu, the app in the background): silence that costs nothing.</summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            _suspended = value;
            if (_player != null) _player.StreamPaused = value;
        }
    }

    // ------------------------------------------------------------------ the match

    /// <summary>The crowd's level: 1 at a match.</summary>
    public void SetCrowdLevel(float k)
    {
        if (MathF.Abs(k - _crowd) < 0.005f) return;
        _crowd = k;
        Do(() => _score.Level = k);
    }

    /// <summary>Menus: the crowd sinks to a distant murmur (or silence inside a pack opening).</summary>
    public void SetAmbience(float level) => SetCrowdLevel(0.35f * level);

    /// <summary>Follow the terraces' director (call every frame it runs).</summary>
    public void Terraces(Terraces dir)
    {
        var cue = dir.TakeCue();
        Do(() => _score.Cue(cue));
    }

    public void Kick(float strength) => Do(() =>
    {
        double t = _mx.Now;
        var f = new Param(0).Set(170 + strength * 40, t).Exp(48, t + 0.09);
        float v = 0.15f + strength * 0.65f;
        var g = new Param(v).Set(v, t).Exp(0.0001f, t + 0.13 + strength * 0.05);
        _mx.Osc(Wave.Sine, t, 0, t + 0.25, g, Bus.Master, f);
        _mx.Burst(t, 0.035 + strength * 0.03, FilterType.Highpass, 1600, 0.7f, 0.1f + strength * 0.35f, 1.6f);
    });

    public void Bounce(float speed)
    {
        if (speed < 1.2f) return;
        float s = MathF.Min(1, speed / 12);
        Do(() =>
        {
            double t = _mx.Now;
            var f = new Param(110).Set(110, t).Exp(45, t + 0.07);
            var g = new Param(0).Set(0.25f * s, t).Exp(0.0001f, t + 0.1);
            _mx.Osc(Wave.Sine, t, 0, t + 0.15, g, Bus.Master, f);
        });
    }

    /// <summary>The referee: 1 a blast, 2 two (half time), 3 three (full time).</summary>
    public void Whistle(int kind) => Do(() =>
    {
        double[] blasts = kind == 3 ? new[] { 0.35, 0.35, 1.1 } : kind == 2 ? new[] { 0.3, 0.9 } : new[] { 0.32 };
        double t = _mx.Now + 0.02;
        foreach (double dur in blasts)
        {
            var g = new Param(0.0001f).Set(0.0001f, t).Exp(0.12f, t + 0.02).Set(0.12f, t + dur - 0.05).Exp(0.0001f, t + dur);
            // Two triangles a little apart (the pea's beating), through a 28 Hz flutter.
            double p1 = 0, p2 = 0, start = t;
            float sr = _mx.Sr;
            Func<double, float> src = tt =>
            {
                float a = Tri(p1) + Tri(p2);
                p1 = (p1 + 2750 / sr) % 1;
                p2 = (p2 + 2930 / sr) % 1;
                return a * (0.6f + 0.4f * MathF.Sin((float)(2 * Math.PI * 28 * (tt - start))));
            };
            _mx.Add(new Voice { Kind = Voice.Src.Custom, Custom = src, F1 = new Biquad(FilterType.Lowpass, 4200), Gain = g, Out = Bus.Master, Start = t, Stop = t + dur + 0.05 });
            t += dur + 0.12;
        }
    });

    static float Tri(double p) => (float)(p < 0.25 ? 4 * p : p < 0.75 ? 2 - 4 * p : 4 * p - 4);

    /// <summary>The woodwork: a ringing clang, and the crowd's gasp.</summary>
    public void Post(float speed) => Do(() =>
    {
        double t = _mx.Now;
        float v = MathF.Min(1, speed / 20) * 0.35f;
        foreach (var (f, d) in new[] { (523f, 0.9), (1347f, 0.6), (2211f, 0.4), (3010f, 0.25) })
            _mx.Osc(Wave.Sine, t, f, t + d + 0.05, new Param(v).Set(v, t).Exp(0.0001f, t + d));
    });

    public void Net(float speed) => Do(() => _mx.Burst(_mx.Now, 0.4, FilterType.Bandpass, 1300, 0.8f, MathF.Min(0.5f, speed / 30), 1.2f));

    // ------------------------------------------------------------------ menus & packs

    public void UiTap() => Do(() => _mx.Tone(_mx.Now, 1250, 0.06, Wave.Sine, 0.12f, 900));

    public void Coins() => Do(() =>
    {
        double t = _mx.Now;
        _mx.Tone(t, 1568, 0.18, Wave.Triangle, 0.16f);
        _mx.Tone(t + 0.08, 2093, 0.3, Wave.Triangle, 0.16f);
    });

    /// <summary>Pack charging up: each tap a little higher and louder.</summary>
    public void PackShake(int level) => Do(() =>
    {
        double t = _mx.Now;
        _mx.Burst(t, 0.25 + level * 0.1, FilterType.Bandpass, 500 + level * 500, 1.2f, 0.25f + level * 0.12f, 1.1f);
        _mx.Tone(t, 180 + level * 120, 0.5, Wave.Sawtooth, 0.05f + level * 0.02f, 360 + level * 260, 0.08);
        _mx.Tone(t, 520 + level * 200, 0.35, Wave.Sine, 0.08f, 900 + level * 300);
    });

    public void PackBurst(int tier) => Do(() =>
    {
        double t = _mx.Now;
        _mx.Tone(t, 110, 1.2, Wave.Sine, 0.55f, 38);
        _mx.Burst(t, 1.1, FilterType.Lowpass, 2400, 0.5f, 0.6f, 0.8f);
        _mx.Burst(t + 0.02, 1.6 + tier * 0.3, FilterType.Highpass, 5000, 0.4f, 0.18f, 1.3f);
        float[] notes = { 523, 659, 784, 1047, 1319, 1568 };
        for (int i = 0; i < 3 + tier; i++) _mx.Tone(t + 0.05 + i * 0.04, notes[i % notes.Length] * (i >= 6 ? 2 : 1), 1.4, Wave.Triangle, 0.07f, 0, 0.01);
    });

    /// <summary>A card flying in.</summary>
    public void Whoosh() => Do(() => _mx.Burst(_mx.Now, 0.35, FilterType.Bandpass, 1800, 0.9f, 0.18f, 1.6f));

    /// <summary>Walkout beat: nation / position stingers.</summary>
    public void Stinger(int step) => Do(() =>
    {
        double t = _mx.Now;
        _mx.Tone(t, 65, 0.7, Wave.Sine, 0.5f, 45);
        _mx.Burst(t, 0.5, FilterType.Lowpass, 900, 0.7f, 0.35f, 0.7f);
        _mx.Tone(t, new float[] { 392, 494, 587 }[step % 3], 0.9, Wave.Triangle, 0.1f);
    });

    /// <summary>The card turns face up. Better cards get a bigger chord.</summary>
    public void Reveal(int tier)
    {
        Do(() =>
        {
            double t = _mx.Now;
            float[][] chords =
            {
                new float[] { 523, 659 },
                new float[] { 523, 659, 784 },
                new float[] { 523, 659, 784, 1047 },
                new float[] { 440, 554, 659, 880, 1109, 1319 },
                new float[] { 392, 494, 587, 784, 988, 1175, 1568 },
            };
            var ch = chords[Math.Min(4, tier)];
            for (int i = 0; i < ch.Length; i++)
                _mx.Tone(t + i * (tier >= 3 ? 0.07 : 0.04), ch[i], 0.8 + tier * 0.5, tier >= 3 ? Wave.Sawtooth : Wave.Triangle, tier >= 3 ? 0.04f : 0.09f, 0, 0.01);
            if (tier >= 2) _mx.Burst(t, 0.9 + tier * 0.3, FilterType.Highpass, 6000, 0.5f, 0.12f + tier * 0.04f, 1.2f);
            if (tier >= 3) _mx.Tone(t, 98, 2, Wave.Sine, 0.4f, 49);
        });
        // The best cards: a stadium erupting.
        if (tier >= 3) Do(() =>
        {
            var b = _score.Take(Shot.Erupt);
            if (b == null) return;
            var rec = new Recording { L = b, R = b, Rate = (int)CrowdBank.Rate };
            _mx.Add(new Voice { Kind = Voice.Src.Sample, Rec = rec, Gain = new Param(1.2f), Out = Bus.Master, Start = _mx.Now });
        });
    }
}
