namespace GameNight.Bridge;

/// <summary>
/// The match as the game sees it: step it, read a frame. The stub implements this until the
/// ported engine (/sim) lands; then a thin adapter over the real match does.
/// </summary>
public interface IMatchSource
{
    /// <summary>Fixed step length, seconds (1/120, as in the PWA).</summary>
    float Dt { get; }

    /// <summary>Advance one fixed step with the current input (consuming its events).</summary>
    void Step(InputState input);

    /// <summary>Write the current state into a frame.</summary>
    void Write(MatchFrame frame);

    /// <summary>True when the human's side is in possession (the buttons switch to attack labels).</summary>
    bool HumanAttacking { get; }
}
