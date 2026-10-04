using System;
using System.Diagnostics;
using Godot;

namespace GameNight.UI;

/// <summary>
/// Where each frame's time goes (FPS COUNTER: DETAIL in the pause menu). The game thread's
/// systems are timed between laps the frame loop marks; the GPU and the render thread are
/// measured by the RenderingServer for the 3D world (the art viewport) and the screen pass
/// (upscale, post and HUD); the engine and audio threads report their own cost. Every timer is
/// skipped while it's off, and it redraws twice a second.
/// </summary>
public sealed partial class Profiler : Control
{
    /// <summary>The game thread's systems, in the order the frame loop laps them.</summary>
    public enum Sys { Camera, Players, Stadium, Sound, Hud }
    static readonly string[] SysNames = { "camera & replays", "players & refs", "stadium & crowd", "sound director", "hud & controls" };

    readonly double[] _sys = new double[5];
    long _lap;
    int _frames;
    double _t, _frameMs;
    Rid _art, _screen;
    bool _on;

    // What's on screen (averages over the last half second).
    readonly (string name, double ms, Color col)[] _bars = new (string, double, Color)[9];
    string _l1 = "", _l2 = "", _l3 = "", _l4 = "";
    double _scale = 8.33;

    static readonly Color Gpu = new(0.45f, 0.75f, 1f), Cpu = new(1f, 0.82f, 0.35f);

    public Func<double> SimStepMs, AudioBlockMs;

    public Profiler()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>The 3D world's viewport and the screen's, for the GPU split.</summary>
    public void Watch(Viewport art, Viewport screen)
    {
        _art = art.GetViewportRid();
        _screen = screen.GetViewportRid();
        if (_on) Measure(true);
    }

    public bool On
    {
        get => _on;
        set
        {
            if (value == _on) return;
            _on = value;
            Visible = value;
            Measure(value);
            Array.Clear(_sys);
            _frames = 0;
            _t = _frameMs = 0;
        }
    }

    void Measure(bool on)
    {
        if (_art.IsValid) RenderingServer.ViewportSetMeasureRenderTime(_art, on);
        if (_screen.IsValid) RenderingServer.ViewportSetMeasureRenderTime(_screen, on);
    }

    /// <summary>The top of the frame loop.</summary>
    public void Begin()
    {
        if (_on) _lap = Stopwatch.GetTimestamp();
    }

    /// <summary>The time since the last lap goes to this system.</summary>
    public void Lap(Sys s)
    {
        if (!_on) return;
        long now = Stopwatch.GetTimestamp();
        _sys[(int)s] += (now - _lap) * 1000.0 / Stopwatch.Frequency;
        _lap = now;
    }

    /// <summary>The end of the frame loop: count the frame, and refresh the read-out twice a second.</summary>
    public void End(double delta)
    {
        if (!_on) return;
        _frames++;
        _t += delta;
        if (_t < 0.5) return;
        double n = _frames;
        _frameMs = _t * 1000 / n;

        double gpuWorld = _art.IsValid ? RenderingServer.ViewportGetMeasuredRenderTimeGpu(_art) : 0;
        double gpuScreen = _screen.IsValid ? RenderingServer.ViewportGetMeasuredRenderTimeGpu(_screen) : 0;
        double cpuWorld = _art.IsValid ? RenderingServer.ViewportGetMeasuredRenderTimeCpu(_art) : 0;
        double cpuScreen = _screen.IsValid ? RenderingServer.ViewportGetMeasuredRenderTimeCpu(_screen) : 0;
        double setup = RenderingServer.GetFrameSetupTimeCpu();
        double game = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000;
        double sim = SimStepMs?.Invoke() ?? 0;
        double audio = AudioBlockMs?.Invoke() ?? 0;
        double ours = 0;
        foreach (var v in _sys) ours += v / n;

        _l1 = $"{1000 / _frameMs:0} fps · {_frameMs:0.00} ms a frame";
        _l2 = $"GPU {gpuWorld + gpuScreen:0.00} ms · CPU game {game:0.00} · render {cpuWorld + cpuScreen + setup:0.00}";
        _l3 = $"engine {sim:0.000} ms a step (120 a s, own thread) · sound {audio / Audio.GameAudio.BlockPlayMs * 100:0}% of its thread";
        _l4 = $"{Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0} draw calls · {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1000:0}k triangles · {Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame):0} objects";

        int b = 0;
        _bars[b++] = ("GPU: 3D world (pitch, stadium, crowd, players)", gpuWorld, Gpu);
        _bars[b++] = ("GPU: screen (upscale, post, HUD)", gpuScreen, Gpu);
        _bars[b++] = ("render thread: draw lists", cpuWorld + cpuScreen + setup, Cpu);
        for (int i = 0; i < _sys.Length; i++) _bars[b++] = ("game: " + SysNames[i], _sys[i] / n, Cpu);
        _bars[b++] = ("game: the rest (Godot itself, waiting on the GPU)", Math.Max(0, game - ours), Cpu);
        Array.Sort(_bars, (x, y) => y.ms.CompareTo(x.ms));
        // The bars are drawn against the frame's budget at the screen's refresh rate.
        float hz = DisplayServer.ScreenGetRefreshRate();
        _scale = 1000.0 / (hz > 0 ? hz : 120);

        Array.Clear(_sys);
        _frames = 0;
        _t = 0;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_l1.Length == 0) return;
        var f = Style.Font(false, 0.5f);
        const int size = 10, line = 13, barW = 70;
        float left = 14, top = 78;
        string[] lines = { _l1, _l2, _l3, _l4 };
        float w = 0;
        foreach (var s in lines) w = MathF.Max(w, Style.Width(f, s, size));
        foreach (var bar in _bars) w = MathF.Max(w, barW + 10 + Style.Width(f, bar.name + " 00.00", size));
        var box = new Rect2(left, top, w + 16, lines.Length * line + _bars.Length * line + 16);
        Style.Box(this, box, new Color(0, 0, 0, 0.62f), 4);
        float y = top + 8;
        foreach (var s in lines)
        {
            Style.Text(this, f, s, new Rect2(left + 8, y, w, line), size, Style.Ink, false);
            y += line;
        }
        y += 6;
        foreach (var (name, ms, col) in _bars)
        {
            float k = (float)Math.Clamp(ms / _scale, 0, 1);
            DrawRect(new Rect2(left + 8, y + 3, barW, line - 6), new Color(1, 1, 1, 0.1f));
            DrawRect(new Rect2(left + 8, y + 3, MathF.Max(1, barW * k), line - 6), col);
            Style.Text(this, f, $"{ms:0.00} {name}", new Rect2(left + 16 + barW, y, w - barW, line), size, Style.Ink, false);
            y += line;
        }
    }
}
