using System;
using GameNight.Sim;

namespace GameNight.Audio;

/// <summary>
/// What a match sounds like (the PWA's handleEvents / matchCalls, the sound half): each
/// frame's strikes, bounces, whistles, woodwork and net, the recorded goal roar, the crowd's
/// level following the danger and the goal mouth, and the terraces' director.
/// </summary>
public sealed class MatchSound
{
    public readonly Terraces Terraces = new();
    /// <summary>The crowd at full cry from the penalty whistle until the kick has played out.</summary>
    double _penaltyUntil = -1;

    /// <summary>
    /// One frame. `playing`: a match the player is in (the demo behind the menus only keeps
    /// the terraces going, at the menus' level). `crowded`: a ground with a crowd. `dt` is 0
    /// while paused.
    /// </summary>
    public void Frame(Match match, MatchSnapshot s, bool playing, bool crowded, bool drill, float dt)
    {
        var audio = GameAudio.Instance;
        if (audio == null) return;
        if (crowded)
        {
            Terraces.Update(dt, s);
            Terraces.OnEvents(s, s.Foul == 1 || s.Card > 0 ? match.LastFoul?.Offender.Team ?? -1 : -1, s.Offside > 0 ? match.LastOffside?.Team ?? -1 : -1);
        }
        if (playing)
        {
            audio.SetCrowdLevel(1);
            if (s.KickCount > 0) audio.Kick(s.KickMax);
            if (s.Bounce > 1.5f) audio.Bounce(s.Bounce);
            if (s.Whistle > 0) audio.Whistle(s.Whistle);
            if (s.Post > 0) audio.Post(s.Post);
            if (s.Net > 0) audio.Net(s.Net);
            // Full until the players walk back, then fading (theirs: only the away end, briefly).
            if (s.Goal >= 0) audio.Goal(drill ? 1.5f : (float)GoalSeq.Back, drill ? 0 : s.Goal);
            bool pen = !drill && PenaltyNoise(s);
            // After their goal the home crowd is stunned: the bed sinks, then comes back.
            float hush = 1 - 0.6f * Terraces.Hush;
            audio.SetExcitement((pen ? 1 : s.Excitement) * hush, pen ? 1 : GoalMouth(s) * hush);
        }
        if (crowded) audio.Terraces(Terraces);
    }

    bool PenaltyNoise(MatchSnapshot s)
    {
        if (s.PenaltyPending)
        {
            _penaltyUntil = s.Time + 2.5;
            return true;
        }
        return s.Phase == Phase.Play && s.Time < _penaltyUntil;
    }

    /// <summary>0..1: the ball in the last 12 m before a goal line, in front of the goal, rising
    /// exponentially to the line (the crowd surges with it).</summary>
    static float GoalMouth(MatchSnapshot s)
    {
        if (s.Phase != Phase.Play) return 0;
        float k = 1 - Math.Min(1, Math.Max(0, (float)Pitch.HalfL - MathF.Abs(s.BallX)) / 12);
        if (k <= 0) return 0;
        float z = MathF.Abs(s.BallZ), u = Math.Clamp((z - 16) / 14, 0, 1);
        float front = 1 - u * u * (3 - 2 * u);
        return (MathF.Exp(4 * k) - 1) / (MathF.Exp(4) - 1) * front;
    }
}
