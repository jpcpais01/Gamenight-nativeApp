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
///
/// It also catches spikes: every frame's time and each part's share go into a short history,
/// and a frame well over both the screen's budget and the recent median is a spike. A few
/// frames later (the GPU's timings arrive late) it is blamed on whatever jumped most against
/// its own baseline: a garbage-collection pause, a shader compiled on first use, one of the
/// parts above, or nothing in the game at all (Android, a missed vsync, the phone heating up),
/// noted with what was happening in the match. A strip graph shows the last two seconds.
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

    public Func<double> SimStepMs, AudioBlockMs, SimStepPeak;
    /// <summary>What's happening in the match, in a few words (for the spike list).</summary>
    public Func<string> Context;

    // ---------------------------------------------------------------- spikes

    /// <summary>Per-frame channels: the five systems, the rest of the game thread, the render
    /// thread, the GPU's world and screen passes, the engine's slowest step.</summary>
    const int Ch = 10, Rest = 5, Render = 6, GpuWorld = 7, GpuScreen = 8, Step = 9, Hist = 240, Late = 3;
    static readonly string[] ChNames =
    {
        "camera & replays", "players & refs", "stadium & crowd", "sound director", "hud & controls",
        "the rest of the game thread", "render thread", "GPU: 3D world", "GPU: screen pass", "engine step (lock)",
    };

    struct Rec
    {
        public float Frame, Gc;
        public int Gen, Compiles;
        public float[] V;
        public string Context;
        public double At;
    }

    readonly Rec[] _hist = new Rec[Hist];
    readonly double[] _frameSys = new double[5], _base = new double[Ch];
    readonly float[] _sorted = new float[Hist];
    long _f;
    double _median = 8.33, _budget = 8.33, _now, _gcPause, _compiles;
    readonly int[] _gcCount = new int[3];
    readonly (double at, float ms, string what, string ctx)[] _spikes = new (double, float, string, string)[4];
    int _spikeN;
    double _spikeTotal;

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
            if (value) ResetSpikes();
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
        double ms = (now - _lap) * 1000.0 / Stopwatch.Frequency;
        _sys[(int)s] += ms;
        _frameSys[(int)s] += ms;
        _lap = now;
    }

    /// <summary>The end of the frame loop: count the frame, and refresh the read-out twice a second.</summary>
    public void End(double delta)
    {
        if (!_on) return;
        Spikes(delta);
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

    void ResetSpikes()
    {
        for (int i = 0; i < Hist; i++) _hist[i] = new Rec { V = new float[Ch], Context = "" };
        _f = 0;
        _spikeN = 0;
        _spikeTotal = 0;
        Array.Clear(_spikes);
        Array.Clear(_base);
        _gcPause = GC.GetTotalPauseDuration().TotalMilliseconds;
        for (int g = 0; g < 3; g++) _gcCount[g] = GC.CollectionCount(g);
        _compiles = Compiles();
        float hz = DisplayServer.ScreenGetRefreshRate();
        _budget = 1000.0 / (hz > 0 ? hz : 120);
        _median = _budget;
    }

    static double Compiles() =>
        Performance.GetMonitor(Performance.Monitor.PipelineCompilationsCanvas) + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsMesh)
        + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSurface) + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw);

    /// <summary>This frame into the history; the frame from a few frames ago judged (its GPU
    /// timings have arrived by now).</summary>
    void Spikes(double delta)
    {
        _now += delta;
        ref var r = ref _hist[_f % Hist];
        r.Frame = 0;
        r.At = _now;
        r.Context = Context?.Invoke() ?? "";
        for (int i = 0; i < 5; i++) r.V[i] = (float)_frameSys[i];
        Array.Clear(_frameSys);
        // The frame's length (delta) and the process time read now are the previous frame's:
        // they go on that frame.
        double process = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000;
        if (_f > 0)
        {
            ref var p = ref _hist[(_f - 1) % Hist];
            p.Frame = (float)(delta * 1000);
            float ours = 0;
            for (int i = 0; i < 5; i++) ours += p.V[i];
            p.V[Rest] = MathF.Max(0, (float)process - ours);
        }
        r.V[Render] = (float)((_art.IsValid ? RenderingServer.ViewportGetMeasuredRenderTimeCpu(_art) : 0)
            + (_screen.IsValid ? RenderingServer.ViewportGetMeasuredRenderTimeCpu(_screen) : 0) + RenderingServer.GetFrameSetupTimeCpu());
        r.V[GpuWorld] = (float)(_art.IsValid ? RenderingServer.ViewportGetMeasuredRenderTimeGpu(_art) : 0);
        r.V[GpuScreen] = (float)(_screen.IsValid ? RenderingServer.ViewportGetMeasuredRenderTimeGpu(_screen) : 0);
        r.V[Step] = (float)(SimStepPeak?.Invoke() ?? 0);
        // Collections and compiles since the last frame.
        double pause = GC.GetTotalPauseDuration().TotalMilliseconds;
        r.Gc = (float)(pause - _gcPause);
        _gcPause = pause;
        r.Gen = -1;
        for (int g = 2; g >= 0; g--)
        {
            int c = GC.CollectionCount(g);
            if (c != _gcCount[g] && r.Gen < 0) r.Gen = g;
            _gcCount[g] = c;
        }
        double comp = Compiles();
        r.Compiles = (int)Math.Max(0, comp - _compiles);
        _compiles = comp;

        // The median frame over the history, refreshed now and then.
        if (_f % 15 == 14)
        {
            int m = (int)Math.Min(_f, Hist - 1);
            for (int i = 0; i < m; i++) _sorted[i] = _hist[(_f - 1 - i) % Hist].Frame;
            Array.Sort(_sorted, 0, m);
            _median = _sorted[m / 2];
        }
        if (_f >= Late + 1) Judge(_f - Late);
        _f++;
    }

    bool IsSpike(float frame) => frame > Math.Max(_budget, _median) * 1.5 && frame - _median > 2;

    void Judge(long e)
    {
        ref var r = ref _hist[e % Hist];
        if (!IsSpike(r.Frame))
        {
            // A calm frame teaches each part its usual cost.
            float k = e < 60 ? 0.2f : 0.03f;
            for (int c = 0; c < Ch; c++) _base[c] += (r.V[c] - _base[c]) * k;
            return;
        }
        float excess = r.Frame - (float)_median;
        // The GPU and render thread report a frame or two late: take their worst since.
        float best = 0;
        int who = -1;
        for (int c = 0; c < Ch; c++)
        {
            float v = r.V[c];
            if (c == Render || c == GpuWorld || c == GpuScreen)
                for (long j = e + 1; j <= _f; j++) v = MathF.Max(v, _hist[j % Hist].V[c]);
            float d = v - (float)_base[c];
            if (d > best) { best = d; who = c; }
        }
        int compiles = 0;
        for (long j = e; j <= _f; j++) compiles += _hist[j % Hist].Compiles;
        string what;
        if (r.Gc > 0.3f * excess && r.Gc > 0.5f) what = $"garbage collection pause (gen {Math.Max(0, r.Gen)})";
        else if (compiles > 0) what = $"shader compiled on first use (×{compiles})" + (who >= 0 ? $", {ChNames[who]}" : "");
        else if (who >= 0 && best > 0.3f * excess) what = ChNames[who] + $" (+{best:0.0})";
        else what = "outside the game: Android, a missed vsync or heat";
        _spikes[_spikeN % _spikes.Length] = (r.At, excess, what, r.Context);
        _spikeN++;
        _spikeTotal += excess;
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
        int shown = Math.Min(_spikeN, _spikes.Length);
        const int graphH = 34;
        var spikeLines = new string[shown];
        for (int i = 0; i < shown; i++)
        {
            var (at, ms, what, ctx) = _spikes[(_spikeN - 1 - i) % _spikes.Length];
            spikeLines[i] = $"+{ms:0.0} ms  {what} · {ctx} · {_now - at:0}s ago";
            w = MathF.Max(w, Style.Width(f, spikeLines[i], size));
        }
        string head = _spikeN == 0 ? "no spikes yet" : $"spikes: {_spikeN} since on, {_spikeTotal / _spikeN:0.0} ms over on average · newest first";
        w = MathF.Max(w, Style.Width(f, head, size));
        var box = new Rect2(left, top, w + 16, (lines.Length + _bars.Length + 1 + shown) * line + graphH + 28);
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

        // The last two seconds of frames: the budget line, spikes in red.
        y += 6;
        float gx = left + 8, gw = w;
        int n = (int)Math.Min(_f - 1, Hist - 1);
        float colW = gw / Hist, top2 = y, max = (float)(_budget * 3);
        DrawRect(new Rect2(gx, top2, gw, graphH), new Color(1, 1, 1, 0.06f));
        for (int i = 0; i < n; i++)
        {
            ref var r = ref _hist[(_f - 1 - n + i) % Hist];
            float h = MathF.Min(1, r.Frame / max) * graphH;
            var col = IsSpike(r.Frame) ? new Color(1, 0.35f, 0.3f) : r.Frame > _budget * 1.05f ? Cpu : new Color(0.55f, 0.85f, 0.5f);
            DrawRect(new Rect2(gx + (Hist - n + i) * colW, top2 + graphH - h, MathF.Max(1, colW), h), col);
        }
        float by = top2 + graphH - (float)(_budget / max) * graphH;
        DrawLine(new Vector2(gx, by), new Vector2(gx + gw, by), new Color(1, 1, 1, 0.5f), 1);
        y += graphH + 4;
        Style.Text(this, f, head, new Rect2(left + 8, y, w, line), size, Style.Accent, false);
        y += line;
        foreach (var sl in spikeLines)
        {
            Style.Text(this, f, sl, new Rect2(left + 8, y, w, line), size, Style.Ink, false);
            y += line;
        }
    }
}
