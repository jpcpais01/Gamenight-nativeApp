using GameNight.Sim;

namespace GameNight.Audio;

/// <summary>
/// What a match sounds like: each frame's strikes, bounces, whistles, woodwork and net, and the
/// terraces' director, whose mood, songs and reactions the crowd's soundtrack follows.
/// </summary>
public sealed class MatchSound
{
    public readonly Terraces Terraces = new();

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
            Terraces.OnEvents(s, s.Foul == 1 ? match.LastFoul?.Offender.Team ?? -1 : -1, s.Offside > 0 ? match.LastOffside?.Team ?? -1 : -1);
        }
        if (playing)
        {
            audio.SetCrowdLevel(1);
            if (s.KickCount > 0) audio.Kick(s.KickMax);
            if (s.Bounce > 1.5f) audio.Bounce(s.Bounce);
            if (s.Whistle > 0) audio.Whistle(s.Whistle);
            if (s.Post > 0) audio.Post(s.Post);
            if (s.Net > 0) audio.Net(s.Net);
            // A penalty given: the whole ground draws breath.
            if (crowded && !drill && s.Foul == 1 && match.LastFoul?.Penalty == true) Terraces.Cue(React.Ooh, -1, 0, 1);
        }
        if (crowded) audio.Terraces(Terraces);
    }
}
