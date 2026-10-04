using System;
using Godot;
using GameNight.Sim;

namespace GameNight.Render;

/// <summary>
/// Goal replays (the PWA's src/ui/replay.ts). A tape of what the renderer draws (the match
/// snapshots), 60 frames a second, keeping the last 8 seconds. At the cut after a goal (camera
/// on the crowd) the match stops and the tape plays the goal back: from a reverse angle on the
/// far touchline until 1.5 s after the ball crosses the line, then the whole move again over
/// the scorer's shoulder. The renderer draws it exactly as it draws the match. Tap to skip.
/// Purely presentational: the match just waits, and carries on from where it stopped.
/// </summary>
public sealed class Replay
{
    const int Hz = 60, Cap = Hz * 8;
    /// <summary>The replay: this long before the ball crosses the line, and this long after it.</summary>
    const double Before = 4, After = 1.5;
    /// <summary>A ball that moves further than this between frames was placed (a restart): the tape starts after.</summary>
    const float Jump = 2;

    readonly MatchSnapshot[] _tape = new MatchSnapshot[Cap];
    /// <summary>Frames recorded (the newest is n - 1); the first after a restart; the goal's.</summary>
    int _n, _first, _goal = -1, _played = -1;

    // Playback.
    double _u, _fromT, _toT;
    int _i, _fired;
    int _pass, _scorer = -1;
    float _heading, _fx, _fz;

    /// <summary>The two frames on show and the blend between them, phase as open play.</summary>
    public readonly MatchSnapshot A = new(), B = new();
    public float Alpha;
    public bool Active { get; private set; }
    /// <summary>Kicks, net and post along the tape, for the sound.</summary>
    public Action<MatchSnapshot> OnEvents;
    /// <summary>Every body jumped (the tape rewound): settle feet and secondary motion afresh.</summary>
    public Action OnRewind;

    MatchSnapshot Slot(int i) => _tape[i % Cap] ??= new MatchSnapshot();

    /// <summary>A new match: forget the tape.</summary>
    public void Reset()
    {
        Active = false;
        _n = _first = 0;
        _goal = _played = -1;
    }

    /// <summary>Each frame: tape the open play, and the second after a goal.</summary>
    public void Record(MatchSnapshot cur)
    {
        if (Active) return;
        if (cur.Phase != Phase.Goal) _goal = -1;
        else if (cur.PhaseT > After + 0.1) return;
        if (_n > 0)
        {
            var last = Slot(_n - 1);
            if (cur.Time <= last.Time) return;
            if (cur.Time - last.Time < 1.0 / Hz - 1e-4)
            {
                // Between frames: what happened goes on the newest one.
                last.KickCount += cur.KickCount;
                last.KickMax = MathF.Max(last.KickMax, cur.KickMax);
                last.Net = MathF.Max(last.Net, cur.Net);
                last.Post = MathF.Max(last.Post, cur.Post);
                return;
            }
        }
        var f = Slot(_n);
        f.CopyFrom(cur);
        if (_n > _first)
        {
            var p = Slot(_n - 1);
            float dx = f.BallX - p.BallX, dz = f.BallZ - p.BallZ;
            if (dx * dx + dz * dz > Jump * Jump) _first = _n;
        }
        if (cur.Phase == Phase.Goal && _goal < 0) _goal = _n;
        _n++;
        _first = Math.Max(_first, _n - Cap);
    }

    /// <summary>The cut after a goal: play it back (true), if there's enough of it on the tape.</summary>
    public bool Start(MatchSnapshot cur, double cutT)
    {
        if (cur.Phase != Phase.Goal || cur.PhaseT < cutT || _goal < 0 || _played == _goal) return false;
        _played = _goal;
        double goalT = Slot(_goal).Time;
        _fromT = Math.Max(Slot(_first).Time, goalT - Before);
        _toT = Math.Min(Slot(_n - 1).Time, goalT + After);
        if (_toT - _fromT < 1.5) return false;
        _scorer = cur.Scorer;
        Active = true;
        Play(0);
        return true;
    }

    /// <summary>Skip, or the end of the tape.</summary>
    public void Finish() => Active = false;

    /// <summary>Advance and frame the shot (dt 0 while paused).</summary>
    public void Update(float dt, MatchCamera cam)
    {
        if (!Active) return;
        _u += dt;
        if (_u >= _toT)
        {
            // Once more from behind the scorer, then back to the match.
            if (_pass == 1 || _scorer < 0)
            {
                Finish();
                return;
            }
            Play(1);
            OnRewind?.Invoke();
        }
        Show();
        if (_pass == 0) Touchline(dt, cam);
        else Shoulder(dt, cam);
    }

    /// <summary>From the top of the tape.</summary>
    void Play(int pass)
    {
        _pass = pass;
        _u = _fromT;
        _i = _first;
        while (_i + 1 < _n && Slot(_i + 1).Time <= _u) _i++;
        _fired = _i;
        Show();
        _fx = B.BallX;
        _fz = B.BallZ;
        int s = _scorer;
        if (pass == 1 && s >= 0)
        {
            _heading = B.Speed[s] > 1.5f ? MathF.Atan2(B.VZ[s], B.VX[s]) : B.Facing[s];
            _fx = Mathf.Lerp(B.X[s] + MathF.Cos(_heading) * 7, B.BallX, 0.3f);
            _fz = Mathf.Lerp(B.Z[s] + MathF.Sin(_heading) * 7, B.BallZ, 0.3f);
        }
    }

    /// <summary>The frames either side of the tape time, and the sounds the tape just passed.</summary>
    void Show()
    {
        while (_i + 2 < _n && Slot(_i + 1).Time <= _u) _i++;
        var a = Slot(_i);
        var b = Slot(Math.Min(_i + 1, _n - 1));
        double span = b.Time - a.Time;
        Alpha = span > 0 ? (float)Math.Clamp((_u - a.Time) / span, 0, 1) : 0;
        A.CopyFrom(a);
        B.CopyFrom(b);
        // The tape is open play: drawn as such, not as the celebration around it.
        A.Phase = B.Phase = Phase.Play;
        A.Celebration = B.Celebration = null;
        for (; _fired < _i; _fired++)
        {
            var f = Slot(_fired + 1);
            if (f.KickMax > 0 || f.Net > 0 || f.Post > 0) OnEvents?.Invoke(f);
        }
    }

    /// <summary>The first pass: the reverse angle, high on the far touchline, following the ball.</summary>
    void Touchline(float dt, MatchCamera cam)
    {
        float k = 1 - MathF.Exp(-dt * 2.5f);
        _fx += (Mathf.Lerp(A.BallX, B.BallX, Alpha) - _fx) * k;
        _fz += (Mathf.Lerp(A.BallZ, B.BallZ, Alpha) - _fz) * k;
        float x = Math.Clamp(_fx, -(float)Pitch.HalfL + 10, (float)Pitch.HalfL - 10);
        cam.Cut(new Vector3(x, 8, -((float)Pitch.HalfW + 3)), new Vector3(x, 0.6f, _fz * 0.8f), 28);
    }

    /// <summary>The second pass: third person, over the scorer's shoulder, looking where he's going
    /// (and a little toward the ball). The heading follows his run, smoothed.</summary>
    void Shoulder(float dt, MatchCamera cam)
    {
        int s = _scorer;
        float x = Mathf.Lerp(A.X[s], B.X[s], Alpha), z = Mathf.Lerp(A.Z[s], B.Z[s], Alpha);
        float want = B.Speed[s] > 1.5f ? MathF.Atan2(B.VZ[s], B.VX[s]) : B.Facing[s];
        float d = want - _heading;
        d -= MathF.Round(d / (MathF.PI * 2)) * MathF.PI * 2;
        _heading += d * (1 - MathF.Exp(-dt * 2.2f));
        float cx = MathF.Cos(_heading), cz = MathF.Sin(_heading);
        float h = B.Height[s];
        float k = 1 - MathF.Exp(-dt * 4);
        _fx += (Mathf.Lerp(x + cx * 7, B.BallX, 0.3f) - _fx) * k;
        _fz += (Mathf.Lerp(z + cz * 7, B.BallZ, 0.3f) - _fz) * k;
        // Behind him and a little off his right shoulder.
        cam.Cut(new Vector3(x - cx * 3.6f + cz * 0.7f, 1.75f * h, z - cz * 3.6f - cx * 0.7f), new Vector3(_fx, 0.9f * h, _fz), 42);
    }
}
