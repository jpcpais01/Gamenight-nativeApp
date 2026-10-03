using System.Collections.Generic;

namespace GameNight.Bridge;

/// <summary>Contextual buttons, as in the PWA: the same button means different things in attack and defence.</summary>
public enum Btn
{
    /// <summary>Attack: Pass. Defence: Tackle.</summary>
    A = 0,
    /// <summary>Attack: Through ball. Defence: Switch.</summary>
    B = 1,
    /// <summary>Attack: Shoot. Defence: Press.</summary>
    C = 2,
}

public enum TackleSwipe { None, Tackle, Slide }

public readonly record struct ButtonEvent(Btn Btn, bool Down, float Hold, bool SwipeUp);

/// <summary>
/// What the touch controls hand the match each tick: a mirror of the PWA's InputState
/// (src/sim/input.ts). The stick is in screen space, -1..1, y up; the sim maps it to the pitch.
/// </summary>
public sealed class InputState
{
    public float MoveX, MoveY;
    public bool Sprint;
    public readonly bool[] Held = new bool[3];
    public readonly float[] HoldTime = new float[3];
    /// <summary>Live: the finger is slid up on a held button (lofted pass / through ball).</summary>
    public readonly bool[] Swipe = new bool[3];
    /// <summary>Presses and releases since the match last consumed them.</summary>
    public readonly List<ButtonEvent> Events = new();
    /// <summary>Defence: finger slid on the held Sprint button. Consumed by the match.</summary>
    public TackleSwipe TackleSwipe;
}
