using System;
using System.Collections.Generic;
using System.Text;
using GameNight.Sim;

namespace GameNight.Link;

/// <summary>
/// The phone-as-controller protocol: small UDP packets on the home network. The phone shouts
/// HELLO (broadcast) until a PC answers HERE; then it sends its whole controller state every
/// frame (INPUT) and the PC answers each one with STATUS (what the buttons say, and the phone's
/// own clock echoed back for the ping). Each packet carries everything, so a lost one costs
/// nothing: the next is a frame behind. Button presses and tackles are numbered and repeated
/// for half a second, so the PC plays each exactly once even if a packet goes missing.
/// </summary>
public static class Wire
{
    public const int Port = 47820;
    public const byte Hello = 1, Here = 2, Input = 3, Status = 4;
    const byte Version = 1;

    /// <summary>What the phone shows: the match controls, or a touchpad for the menus.</summary>
    public enum View : byte { Controls, Pad }

    public struct NumberedEvent
    {
        public ushort Id;
        public ButtonEvent E;
    }

    /// <summary>Everything the phone sends, every frame.</summary>
    public sealed class Pad
    {
        public uint Nonce, Time;
        public View View;
        public float MoveX, MoveY;
        public bool Sprint;
        public readonly bool[] Held = new bool[3], Swipe = new bool[3];
        public readonly float[] HoldTime = new float[3];
        public readonly List<NumberedEvent> Events = new();
        public ushort TackleId;
        public TackleSwipe Tackle;
        public ushort BackId, ClickId;
        /// <summary>The touchpad's travel so far (in widths of the PC's window) and its held click.</summary>
        public float CursorX, CursorY;
        public bool MouseDown;
    }

    public static bool Is(byte[] b, int n, byte type) => n >= 4 && b[0] == 'G' && b[1] == 'N' && b[2] == type && b[3] == Version;

    static int Head(byte[] b, byte type)
    {
        b[0] = (byte)'G';
        b[1] = (byte)'N';
        b[2] = type;
        b[3] = Version;
        return 4;
    }

    public static int WriteHello(byte[] b, uint nonce)
    {
        int o = Head(b, Hello);
        o = U32(b, o, nonce);
        return o;
    }

    public static int WriteHere(byte[] b, string name)
    {
        int o = Head(b, Here);
        return Str(b, o, name);
    }

    public static string ReadHere(byte[] b, int n) => Encoding.UTF8.GetString(b, 4, Math.Max(0, n - 4));

    public static int WriteStatus(byte[] b, uint echo, bool live, int mode, int picked, string name)
    {
        int o = Head(b, Status);
        o = U32(b, o, echo);
        b[o++] = (byte)(live ? 1 : 0);
        b[o++] = (byte)mode;
        b[o++] = (byte)(sbyte)picked;
        return Str(b, o, name);
    }

    public static bool ReadStatus(byte[] b, int n, out uint echo, out bool live, out int mode, out int picked, out string name)
    {
        echo = 0;
        live = false;
        mode = picked = 0;
        name = "";
        if (!Is(b, n, Status) || n < 11) return false;
        echo = RU32(b, 4);
        live = b[8] != 0;
        mode = b[9];
        picked = (sbyte)b[10];
        name = Encoding.UTF8.GetString(b, 11, n - 11);
        return true;
    }

    public static int WritePad(byte[] b, Pad p)
    {
        int o = Head(b, Input);
        o = U32(b, o, p.Nonce);
        o = U32(b, o, p.Time);
        b[o++] = (byte)p.View;
        o = I16(b, o, p.MoveX);
        o = I16(b, o, p.MoveY);
        int flags = p.Sprint ? 1 : 0;
        for (int i = 0; i < 3; i++)
        {
            if (p.Held[i]) flags |= 2 << i;
            if (p.Swipe[i]) flags |= 16 << i;
        }
        if (p.MouseDown) flags |= 128;
        b[o++] = (byte)flags;
        for (int i = 0; i < 3; i++) o = U16(b, o, Ms(p.HoldTime[i]));
        int count = Math.Min(p.Events.Count, 12);
        b[o++] = (byte)count;
        for (int k = p.Events.Count - count; k < p.Events.Count; k++)
        {
            var e = p.Events[k];
            o = U16(b, o, e.Id);
            b[o++] = (byte)(e.E.Btn | (e.E.Kind == ButtonKind.Up ? 4 : 0) | (e.E.SwipeUp ? 8 : 0));
            o = U16(b, o, Ms(e.E.Hold));
        }
        o = U16(b, o, p.TackleId);
        b[o++] = (byte)p.Tackle;
        o = U16(b, o, p.BackId);
        o = U16(b, o, p.ClickId);
        o = F32(b, o, p.CursorX);
        o = F32(b, o, p.CursorY);
        return o;
    }

    public static bool ReadPad(byte[] b, int n, Pad p)
    {
        if (!Is(b, n, Input) || n < 30) return false;
        try
        {
            int o = 4;
            p.Nonce = RU32(b, o); o += 4;
            p.Time = RU32(b, o); o += 4;
            p.View = (View)b[o++];
            p.MoveX = RI16(b, o); o += 2;
            p.MoveY = RI16(b, o); o += 2;
            int flags = b[o++];
            p.Sprint = (flags & 1) != 0;
            p.MouseDown = (flags & 128) != 0;
            for (int i = 0; i < 3; i++)
            {
                p.Held[i] = (flags & (2 << i)) != 0;
                p.Swipe[i] = (flags & (16 << i)) != 0;
            }
            for (int i = 0; i < 3; i++) { p.HoldTime[i] = RU16(b, o) / 1000f; o += 2; }
            int count = b[o++];
            p.Events.Clear();
            for (int k = 0; k < count; k++)
            {
                ushort id = RU16(b, o); o += 2;
                int f = b[o++];
                double hold = RU16(b, o) / 1000.0; o += 2;
                var e = (f & 4) != 0 ? ButtonEvent.Up(f & 3, hold, (f & 8) != 0) : ButtonEvent.Down(f & 3);
                p.Events.Add(new NumberedEvent { Id = id, E = e });
            }
            p.TackleId = RU16(b, o); o += 2;
            p.Tackle = (TackleSwipe)b[o++];
            p.BackId = RU16(b, o); o += 2;
            p.ClickId = RU16(b, o); o += 2;
            p.CursorX = BitConverter.ToSingle(b, o); o += 4;
            p.CursorY = BitConverter.ToSingle(b, o);
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (IndexOutOfRangeException) { return false; }
    }

    /// <summary>Is numbered thing `id` newer than `last` (the numbers wrap round)?</summary>
    public static bool Newer(ushort id, ushort last) => (short)(id - last) > 0;

    static ushort Ms(double s) => (ushort)Math.Clamp(Math.Round(s * 1000), 0, 65535);
    static int U16(byte[] b, int o, ushort v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); return o + 2; }
    static int U32(byte[] b, int o, uint v) { for (int i = 0; i < 4; i++) b[o + i] = (byte)(v >> (8 * i)); return o + 4; }
    static int I16(byte[] b, int o, float v) => U16(b, o, (ushort)(short)Math.Clamp(Math.Round(v * 32767), -32767, 32767));
    static int F32(byte[] b, int o, float v) { BitConverter.TryWriteBytes(new Span<byte>(b, o, 4), v); return o + 4; }
    static int Str(byte[] b, int o, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s.Length > 40 ? s[..40] : s);
        Array.Copy(bytes, 0, b, o, bytes.Length);
        return o + bytes.Length;
    }
    static ushort RU16(byte[] b, int o) => (ushort)(b[o] | b[o + 1] << 8);
    static uint RU32(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
    static float RI16(byte[] b, int o) => (short)RU16(b, o) / 32767f;
}
