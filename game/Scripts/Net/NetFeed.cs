using System;
using System.Collections.Generic;
using GameNight.Sim;

namespace GameNight.Net;

/// <summary>
/// The friend's view of an online match: the host's frames as they arrive, played back a
/// little behind the newest (so a late one doesn't make the picture stop), and blended between
/// the two either side of the moment shown, as the local engine's steps are. Each frame's
/// events (kicks, whistles, the goal) are handed over once, when the playback passes it.
/// </summary>
public sealed class NetFeed
{
    /// <summary>How far behind the newest frame it plays (s): about two frames' gap at 30 a second.</summary>
    const double Delay = 0.075;

    readonly FrameCodec _codec = new();
    readonly List<MatchSnapshot> _frames = new();
    readonly Stack<MatchSnapshot> _spare = new();
    readonly MatchSnapshot _events = new();
    double _play = -1, _lastClock;
    /// <summary>The host's replay or walk-out is on (from the newest frame's flags).</summary>
    public bool HostDirected { get; private set; }
    /// <summary>Frames in hand (for the connection's health on screen).</summary>
    public int Count => _frames.Count;
    public double LastFrameAt { get; private set; } = -1;

    public NetFeed()
    {
        _events.ClearEvents();
    }

    /// <summary>A frame message has come in (`now` is the local clock, seconds).</summary>
    public void Add(byte[] msg, double now)
    {
        var s = _spare.Count > 0 ? _spare.Pop() : new MatchSnapshot();
        if (!_codec.Decode(msg, s))
        {
            _spare.Push(s);
            return;
        }
        HostDirected = (msg[1] & 1) != 0;
        LastFrameAt = now;
        // (Out of order can't happen on this line, but a restart of the host's clock can.)
        if (_frames.Count > 0 && s.Time < _frames[^1].Time) Drop(_frames.Count);
        _frames.Add(s);
    }

    /// <summary>The two frames to draw between and how far along, as MatchRunner.Read gives
    /// them. False until the first frame is in.</summary>
    public bool Read(MatchSnapshot previous, MatchSnapshot current, out float alpha, double now)
    {
        alpha = 1;
        if (_frames.Count == 0) return false;
        double dt = _lastClock > 0 ? Math.Clamp(now - _lastClock, 0, 0.1) : 0;
        _lastClock = now;
        double target = _frames[^1].Time - Delay;
        // Run with the real clock, eased toward the target; a big gap (a stall) jumps.
        if (_play < 0 || Math.Abs(target - _play) > 0.4) _play = target;
        else _play += dt + (target - _play) * Math.Min(1, dt * 2.5);
        _play = Math.Min(_play, _frames[^1].Time);
        // Passed frames: their events are due; keep one frame at or before the moment.
        int i = 0;
        while (i + 1 < _frames.Count && _frames[i + 1].Time <= _play) i++;
        if (i > 0) Drop(i);
        var a = _frames[0];
        var b = _frames.Count > 1 ? _frames[1] : a;
        previous.CopyFrom(a);
        current.CopyFrom(b);
        current.ClearEvents();
        Fold(current, _events);
        _events.ClearEvents();
        if (b != a)
        {
            double span = b.Time - a.Time;
            alpha = span > 1e-6 ? (float)Math.Clamp((_play - a.Time) / span, 0, 1) : 1;
        }
        return true;
    }

    /// <summary>Drops the oldest `n` frames, keeping their events for the next Read.</summary>
    void Drop(int n)
    {
        for (int k = 0; k < n; k++)
        {
            Fold(_events, _frames[k]);
            _spare.Push(_frames[k]);
        }
        _frames.RemoveRange(0, n);
        if (_frames.Count > 0)
        {
            // The frame now at the front carries events that happened up to it: due as well.
            Fold(_events, _frames[0]);
            _frames[0].ClearEvents();
        }
    }

    public static void Fold(MatchSnapshot into, MatchSnapshot from)
    {
        into.KickCount += from.KickCount;
        into.KickMax = Math.Max(into.KickMax, from.KickMax);
        into.Whistle = Math.Max(into.Whistle, from.Whistle);
        if (into.Goal < 0) into.Goal = from.Goal;
        into.Foul = Math.Max(into.Foul, from.Foul);
        into.Card = Math.Max(into.Card, from.Card);
        into.Offside = Math.Max(into.Offside, from.Offside);
        into.Sub = Math.Max(into.Sub, from.Sub);
        into.Post = Math.Max(into.Post, from.Post);
        into.Net = Math.Max(into.Net, from.Net);
        into.Bounce = Math.Max(into.Bounce, from.Bounce);
        into.Save = Math.Max(into.Save, from.Save);
        into.Tackle = Math.Max(into.Tackle, from.Tackle);
    }
}
