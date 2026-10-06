using System;
using System.IO;
using GameNight.Sim;

namespace GameNight.Net;

/// <summary>
/// The friend's controls on their way to the host: the stick, sprint and held buttons as they
/// are, plus the presses and the tackle swipe since the last message (the line is reliable and
/// in order, so each goes once). The clock rides along for the ping. Sent at most 60 times a
/// second, and only when something changed (or every 100 ms to show it's alive).
/// </summary>
public sealed class NetInput
{
    readonly InputState _pending = new();
    double _sentAt = -1, _lastX, _lastY;
    int _lastFlags = -1;

    /// <summary>Friend: this frame's controls; returns the message to send, or null for now.</summary>
    public byte[] Pack(InputState input, double now, uint clock)
    {
        _pending.Events.AddRange(input.Events);
        input.Events.Clear();
        if (input.TackleSwipe != TackleSwipe.None) _pending.TackleSwipe = input.TackleSwipe;
        input.TackleSwipe = TackleSwipe.None;
        int flags = Flags(input);
        bool changed = _pending.Events.Count > 0 || _pending.TackleSwipe != TackleSwipe.None || flags != _lastFlags
            || Math.Abs(input.MoveX - _lastX) > 0.02 || Math.Abs(input.MoveY - _lastY) > 0.02;
        if (now - _sentAt < 1 / 60.0 || (!changed && now - _sentAt < 0.1)) return null;
        _sentAt = now;
        _lastX = input.MoveX;
        _lastY = input.MoveY;
        _lastFlags = flags;
        var ms = new MemoryStream(64);
        var w = new BinaryWriter(ms);
        w.Write(Online.Input);
        w.Write(clock);
        w.Write((float)input.MoveX);
        w.Write((float)input.MoveY);
        w.Write((byte)flags);
        for (int i = 0; i < 3; i++) w.Write((float)input.HoldTime[i]);
        w.Write((byte)_pending.TackleSwipe);
        w.Write((byte)Math.Min(_pending.Events.Count, 255));
        for (int k = 0; k < Math.Min(_pending.Events.Count, 255); k++)
        {
            var e = _pending.Events[k];
            w.Write((byte)(e.Btn | (e.Kind == ButtonKind.Up ? 4 : 0) | (e.SwipeUp ? 8 : 0)));
            w.Write((float)e.Hold);
        }
        _pending.Events.Clear();
        _pending.TackleSwipe = TackleSwipe.None;
        w.Flush();
        return ms.ToArray();
    }

    static int Flags(InputState s)
    {
        int f = s.Sprint ? 1 : 0;
        for (int i = 0; i < 3; i++)
        {
            if (s.Held[i]) f |= 2 << i;
            if (s.Swipe[i]) f |= 16 << i;
        }
        return f;
    }

    /// <summary>Host: a message into `into` (stick and buttons replaced, presses added); returns
    /// the friend's clock for the ping's echo.</summary>
    public static uint Unpack(byte[] m, InputState into)
    {
        try
        {
            var r = new BinaryReader(new MemoryStream(m, 1, m.Length - 1));
            uint clock = r.ReadUInt32();
            into.MoveX = r.ReadSingle();
            into.MoveY = r.ReadSingle();
            int f = r.ReadByte();
            into.Sprint = (f & 1) != 0;
            for (int i = 0; i < 3; i++)
            {
                into.Held[i] = (f & (2 << i)) != 0;
                into.Swipe[i] = (f & (16 << i)) != 0;
            }
            for (int i = 0; i < 3; i++) into.HoldTime[i] = r.ReadSingle();
            var t = (TackleSwipe)r.ReadByte();
            if (t != TackleSwipe.None) into.TackleSwipe = t;
            int n = r.ReadByte();
            for (int k = 0; k < n; k++)
            {
                int b = r.ReadByte();
                double hold = r.ReadSingle();
                into.Events.Add((b & 4) != 0 ? ButtonEvent.Up(b & 3, hold, (b & 8) != 0) : ButtonEvent.Down(b & 3));
            }
            return clock;
        }
        catch (EndOfStreamException)
        {
            return 0;
        }
    }
}
