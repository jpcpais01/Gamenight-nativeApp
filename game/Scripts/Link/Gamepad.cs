using System;
using Godot;
using GameNight.Sim;
using GameNight.UI;

namespace GameNight.Link;

/// <summary>
/// A standard gamepad (Xbox layout; PlayStation and others map onto it), FIFA-style:
/// left stick or d-pad moves, A passes, X lofts it, Y plays it through (LB+Y lofted), B shoots,
/// RT or RB sprints / presses. In defence A tackles, Y or LB switches, B is the tackle swipe and
/// X slides in. Start pauses. Read once a frame into its own <see cref="InputState"/>.
/// </summary>
public sealed class Gamepad
{
    public readonly InputState Input = new();
    /// <summary>When it was last touched (seconds), so the on-screen buttons can step aside.</summary>
    public double LastUsed = -100;
    /// <summary>Start was pressed this frame.</summary>
    public bool Back;

    readonly ulong[] _downAt = new ulong[3];
    bool _tackleWas, _slideWas, _startWas;

    public void Poll(float dt, TouchControls.Mode mode, double now)
    {
        Back = false;
        var pads = Godot.Input.GetConnectedJoypads();
        if (pads.Count == 0)
        {
            Clear();
            return;
        }
        int dev = pads[0];
        bool B(JoyButton b) => Godot.Input.IsJoyButtonPressed(dev, b);
        float Ax(JoyAxis a) => Godot.Input.GetJoyAxis(dev, a);

        // The stick, with a round dead zone; the d-pad as a fallback.
        float x = Ax(JoyAxis.LeftX), y = -Ax(JoyAxis.LeftY);
        float m = MathF.Sqrt(x * x + y * y);
        if (m < 0.2f) x = y = 0;
        else
        {
            float k = MathF.Min(1, (m - 0.2f) / 0.75f) / m;
            x *= k;
            y *= k;
        }
        if (x == 0 && y == 0)
        {
            x = (B(JoyButton.DpadRight) ? 1 : 0) - (B(JoyButton.DpadLeft) ? 1 : 0);
            y = (B(JoyButton.DpadUp) ? 1 : 0) - (B(JoyButton.DpadDown) ? 1 : 0);
            float d = MathF.Sqrt(x * x + y * y);
            if (d > 0) { x /= d; y /= d; }
        }
        Input.MoveX = x;
        Input.MoveY = y;
        Input.Sprint = Ax(JoyAxis.TriggerRight) > 0.3f || B(JoyButton.RightShoulder);

        bool defend = mode == TouchControls.Mode.Defend;
        bool a = B(JoyButton.A), bx = B(JoyButton.X), yb = B(JoyButton.Y), bb = B(JoyButton.B), lb = B(JoyButton.LeftShoulder);
        if (defend)
        {
            Set(0, a, false);
            Set(1, yb || lb, false);
            Set(2, false, false);
            if (bb && !_tackleWas) Input.TackleSwipe = TackleSwipe.Tackle;
            if (bx && !_slideWas) Input.TackleSwipe = TackleSwipe.Slide;
        }
        else
        {
            Set(0, a || bx, bx);
            Set(1, yb, lb);
            Set(2, bb, false);
        }
        _tackleWas = bb;
        _slideWas = bx;
        bool start = B(JoyButton.Start);
        Back = start && !_startWas;
        _startWas = start;

        for (int i = 0; i < 3; i++)
            if (Input.Held[i]) Input.HoldTime[i] += dt;
        if (x != 0 || y != 0 || Input.Sprint || a || bx || yb || bb || lb || start) LastUsed = now;
    }

    /// <summary>Button i held or not; `lofted` is the touch controls' swipe up.</summary>
    void Set(int i, bool down, bool lofted)
    {
        if (down) Input.Swipe[i] |= lofted;
        if (down == Input.Held[i]) return;
        Input.Held[i] = down;
        if (down)
        {
            Input.HoldTime[i] = 0;
            _downAt[i] = Time.GetTicksMsec();
            Input.Events.Add(ButtonEvent.Down(i));
        }
        else
        {
            Input.Events.Add(ButtonEvent.Up(i, (Time.GetTicksMsec() - _downAt[i]) / 1000.0, Input.Swipe[i]));
            Input.HoldTime[i] = 0;
            Input.Swipe[i] = false;
        }
    }

    void Clear()
    {
        for (int i = 0; i < 3; i++) Set(i, false, false);
        Input.MoveX = Input.MoveY = 0;
        Input.Sprint = false;
    }
}
