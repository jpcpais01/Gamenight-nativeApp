using System.Collections.Generic;

namespace GameNight.Sim;

/// <summary>Buttons are contextual: the same physical button means different things in attack and defence.</summary>
public static class Btn
{
    /// <summary>Bottom button. Attack: Pass · Defence: Tackle (double tap: slide)</summary>
    public const int A = 0;
    /// <summary>Top button. Attack: Through ball · Defence: Switch</summary>
    public const int B = 1;
    /// <summary>Middle button. Attack: Shoot · Defence: hidden (pressing is the Sprint button)</summary>
    public const int C = 2;
}

public enum ButtonKind { Down, Up }

public enum TackleSwipe { None, Tackle, Slide }

public struct ButtonEvent
{
    public int Btn;
    public ButtonKind Kind;
    /// <summary>For Up: how long the button was held (s).</summary>
    public double Hold;
    /// <summary>For Up: the finger slid upward while holding (lofted pass).</summary>
    public bool SwipeUp;

    public static ButtonEvent Down(int btn) => new ButtonEvent { Btn = btn, Kind = ButtonKind.Down };

    public static ButtonEvent Up(int btn, double hold, bool swipeUp = false) =>
        new ButtonEvent { Btn = btn, Kind = ButtonKind.Up, Hold = hold, SwipeUp = swipeUp };
}

/// <summary>
/// What the touch controls feed the match each step (the PWA's InputState). The match consumes
/// `Events` and `TackleSwipe` on the step that reads them; the rest is live state.
/// </summary>
public sealed class InputState
{
    /// <summary>Joystick in screen space, -1..1, y up.</summary>
    public double MoveX, MoveY;
    public bool Sprint;
    public readonly bool[] Held = new bool[3];
    public readonly double[] HoldTime = new double[3];
    /// <summary>Live: finger currently slid up on a held button.</summary>
    public readonly bool[] Swipe = new bool[3];
    public readonly List<ButtonEvent> Events = new List<ButtonEvent>();
    /// <summary>Defence: finger slid on the held Sprint button (down = tackle, left = slide). Consumed by the match.</summary>
    public TackleSwipe TackleSwipe = TackleSwipe.None;
}
