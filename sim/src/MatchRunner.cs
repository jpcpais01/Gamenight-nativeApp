using System;
using System.Diagnostics;
using System.Threading;

namespace GameNight.Sim;

/// <summary>
/// Runs a match on its own thread at the fixed 120 Hz, against the real clock, like the PWA's
/// frame loop (at most 12 steps at a time; beyond that it lets the time go rather than spiral).
/// The game thread hands it input with Submit and reads the last two steps with Read, then
/// draws between them (alpha). Nothing else on the Match may be touched while it runs; for
/// one-off changes (switching player, pausing) use Invoke, which runs on the sim thread
/// between steps.
/// </summary>
public sealed class MatchRunner : IDisposable
{
    const int MaxSteps = 12;

    public readonly Match Match;
    /// <summary>Optional: called after every Match.Step on the sim thread (a training Drill.Step).</summary>
    public Action? AfterStep;

    readonly object gate = new object();
    readonly InputState staged = new InputState();
    readonly InputState live = new InputState();
    readonly MatchSnapshot prev = new MatchSnapshot(), cur = new MatchSnapshot();
    MatchSnapshot work = new MatchSnapshot(), last = new MatchSnapshot();
    Action<Match>? pending;
    Thread? thread;
    volatile bool running;
    volatile bool paused;
    long steps;
    double stepAt;

    public MatchRunner(Match match)
    {
        Match = match;
        match.Write(last);
        prev.CopyFrom(last);
        cur.CopyFrom(last);
    }

    /// <summary>Stops the match clock (pause menu); Resume picks up without a jump.</summary>
    public bool Paused
    {
        get => paused;
        set => paused = value;
    }

    public void Start()
    {
        if (running) return;
        running = true;
        thread = new Thread(Loop) { IsBackground = true, Name = "GameNight sim", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    public void Stop()
    {
        running = false;
        thread?.Join();
        thread = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// The controls as they are now: the stick, sprint and held buttons are copied; button
    /// events and a tackle swipe are moved over (taken out of `src`) and reach the next step.
    /// </summary>
    public void Submit(InputState src)
    {
        lock (gate)
        {
            staged.MoveX = src.MoveX;
            staged.MoveY = src.MoveY;
            staged.Sprint = src.Sprint;
            for (int i = 0; i < 3; i++)
            {
                staged.Held[i] = src.Held[i];
                staged.HoldTime[i] = src.HoldTime[i];
                staged.Swipe[i] = src.Swipe[i];
            }
            staged.Events.AddRange(src.Events);
            src.Events.Clear();
            if (src.TackleSwipe != TackleSwipe.None) staged.TackleSwipe = src.TackleSwipe;
            src.TackleSwipe = TackleSwipe.None;
        }
    }

    /// <summary>Runs `action` on the sim thread before the next step.</summary>
    public void Invoke(Action<Match> action)
    {
        lock (gate) pending += action;
    }

    /// <summary>
    /// The last two steps (for interpolation), their events folded into `current`, and how far
    /// the real clock has gone past `previous`, in steps (0..1, for the blend). Returns the
    /// number of steps run so far.
    /// </summary>
    public long Read(MatchSnapshot previous, MatchSnapshot current, out float alpha)
    {
        lock (gate)
        {
            previous.CopyFrom(prev);
            current.CopyFrom(cur);
            cur.ClearEvents();
            double since = Stopwatch.GetTimestamp() - stepAt;
            alpha = paused ? 1 : (float)Math.Clamp(since / Stopwatch.Frequency / Tick.DT, 0, 1);
            return steps;
        }
    }

    void Loop()
    {
        long t0 = Stopwatch.GetTimestamp();
        double simT = 0;
        double pausedFor = 0;
        long pausedAt = 0;
        while (running)
        {
            long now = Stopwatch.GetTimestamp();
            if (paused)
            {
                if (pausedAt == 0) pausedAt = now;
                Thread.Sleep(4);
                continue;
            }
            if (pausedAt != 0)
            {
                pausedFor += (double)(now - pausedAt) / Stopwatch.Frequency;
                pausedAt = 0;
            }
            double real = (double)(now - t0) / Stopwatch.Frequency - pausedFor;
            int n = 0;
            while (simT + Tick.DT <= real && n < MaxSteps)
            {
                StepOnce();
                simT += Tick.DT;
                n++;
            }
            if (n == MaxSteps && simT + Tick.DT <= real) simT = real; // fell behind: let it go
            // Sleep to just before the next step is due.
            double wait = simT + Tick.DT - ((double)(Stopwatch.GetTimestamp() - t0) / Stopwatch.Frequency - pausedFor);
            if (wait > 0.002) Thread.Sleep((int)((wait - 0.001) * 1000));
            else if (wait > 0) Thread.Yield();
        }
    }

    void StepOnce()
    {
        Action<Match>? act;
        lock (gate)
        {
            act = pending;
            pending = null;
            live.MoveX = staged.MoveX;
            live.MoveY = staged.MoveY;
            live.Sprint = staged.Sprint;
            for (int i = 0; i < 3; i++)
            {
                live.Held[i] = staged.Held[i];
                live.HoldTime[i] = staged.HoldTime[i];
                live.Swipe[i] = staged.Swipe[i];
            }
            live.Events.AddRange(staged.Events);
            staged.Events.Clear();
            if (staged.TackleSwipe != TackleSwipe.None) live.TackleSwipe = staged.TackleSwipe;
            staged.TackleSwipe = TackleSwipe.None;
        }
        act?.Invoke(Match);
        Match.Step(live);
        AfterStep?.Invoke();
        live.Events.Clear();
        live.TackleSwipe = TackleSwipe.None;
        var e = Match.TakeEvents();
        Match.Write(work);
        lock (gate)
        {
            prev.CopyFrom(last);
            // Events the game hasn't read yet carry over into the newest frame.
            work.ClearEvents();
            work.AddEvents(e);
            FoldUnread(work, cur);
            cur.CopyFrom(work);
            (last, work) = (work, last);
            steps++;
            stepAt = Stopwatch.GetTimestamp();
        }
    }

    static void FoldUnread(MatchSnapshot into, MatchSnapshot unread)
    {
        into.KickCount += unread.KickCount;
        into.KickMax = Math.Max(into.KickMax, unread.KickMax);
        into.Whistle = Math.Max(into.Whistle, unread.Whistle);
        if (into.Goal < 0) into.Goal = unread.Goal;
        into.Foul = Math.Max(into.Foul, unread.Foul);
        into.Card = Math.Max(into.Card, unread.Card);
        into.Offside = Math.Max(into.Offside, unread.Offside);
        into.Post = Math.Max(into.Post, unread.Post);
        into.Net = Math.Max(into.Net, unread.Net);
        into.Bounce = Math.Max(into.Bounce, unread.Bounce);
        into.Save = Math.Max(into.Save, unread.Save);
        into.Tackle = Math.Max(into.Tackle, unread.Tackle);
    }
}
