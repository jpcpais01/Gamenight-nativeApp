using System;
using Godot;
using GameNight.Audio;
using GameNight.Sim;

namespace GameNight.Grounds;

/// <summary>
/// What the crowd feels from moment to moment, read off the match for crowd.gdshader. The
/// same signals the terraces' sound works from (Terraces.Danger, the shots, saves and woodwork),
/// so the stands move with what you hear:
/// - gn_mood (rise home, rise away, team 0's attacking direction): a team's fans come up out
///   of their seats as it closes on goal, the end it attacks first.
/// - gn_gasp (team that went close, seconds since, kind 1 wide or woodwork / 2 saved): the
///   whole of that team's support up with the shot, then hands on heads; the others applaud
///   their keeper.
/// - gn_ballout (x, z, out): the ball boys near a ball that's gone out get up with a new one.
/// </summary>
public sealed class CrowdMood
{
    const float HL = 52.5f;
    float _rise0, _rise1;
    double _clock, _shotAt = -99, _gaspAt = -99;
    int _shotTeam = -1, _gaspTeam = -1;
    float _gaspKind;
    Phase _lastPhase;
    /// <summary>An away day: the match's side 0 (yours) is the stands' away support.</summary>
    public bool Flip;

    static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    public void Update(MatchSnapshot s, Terraces terraces, float dt)
    {
        _clock += dt;
        double t = _clock;

        // A shot that goes close: saved, off the woodwork, or out just wide of the post.
        if (s.ShotTeam >= 0) { _shotTeam = s.ShotTeam; _shotAt = t; }
        int shooter = _shotTeam >= 0 && t - _shotAt < 4 ? _shotTeam : s.LastTouchTeam;
        float kind = 0;
        if (s.Save > 0.5f) kind = 2;
        else if (s.Post > 0) kind = 1;
        else if (s.Phase == Phase.Out && _lastPhase == Phase.Play && _shotTeam >= 0 && t - _shotAt < 3
            && MathF.Abs(MathF.Abs(s.BallX) - HL) < 3 && MathF.Abs(s.BallZ) < 14) kind = 1;
        if (kind > 0 && shooter >= 0 && t - _gaspAt > 1.5)
        {
            _gaspTeam = shooter;
            _gaspAt = t;
            _gaspKind = kind;
            _shotTeam = -1;
        }
        if (s.Goal >= 0) _gaspTeam = -1;
        _lastPhase = s.Phase;

        // Up out of their seats as their team closes in; they stay up a moment after a chance.
        float d0 = terraces?.Danger[0] ?? 0, d1 = terraces?.Danger[1] ?? 0;
        float hold = _gaspTeam >= 0 && t - _gaspAt < 2.5 ? 1 : 0;
        float w0 = MathF.Max(Smooth(0.3f, 0.85f, d0), _gaspTeam == 0 ? hold : 0);
        float w1 = MathF.Max(Smooth(0.3f, 0.85f, d1), _gaspTeam == 1 ? hold : 0);
        _rise0 += (w0 - _rise0) * (1 - MathF.Exp(-dt * (w0 > _rise0 ? 4f : 0.8f)));
        _rise1 += (w1 - _rise1) * (1 - MathF.Exp(-dt * (w1 > _rise1 ? 4f : 0.8f)));

        bool outBall = s.Phase == Phase.Out || s.SetPiece is SetPieceKind.Throw or SetPieceKind.Corner or SetPieceKind.GoalKick;
        // What each end is doing in the sound (Terraces): clapping, jeering, hands on heads, fists up;
        // the home end hushed after an away goal; whether the song is the jumping one.
        if (terraces != null)
        {
            int h = Flip ? 1 : 0, a = 1 - h;
            RenderingServer.GlobalShaderParameterSet("gn_react_home", new Vector4(terraces.Clapping[h], terraces.Jeering[h], terraces.Heads[h], terraces.Fists[h]));
            RenderingServer.GlobalShaderParameterSet("gn_react_away", new Vector4(terraces.Clapping[a], terraces.Jeering[a], terraces.Heads[a], terraces.Fists[a]));
        }
        float bounce = terraces?.Singing?.Chant?.Name == "bounce" ? 1 : 0;
        float dir0 = s.Dir[0] == 0 ? 1 : s.Dir[0];
        if (Flip) RenderingServer.GlobalShaderParameterSet("gn_mood", new Vector4(_rise1, _rise0, -dir0, (terraces?.Hush ?? 0) + bounce * 2));
        else RenderingServer.GlobalShaderParameterSet("gn_mood", new Vector4(_rise0, _rise1, dir0, (terraces?.Hush ?? 0) + bounce * 2));
        RenderingServer.GlobalShaderParameterSet("gn_gasp", new Vector4(Flip && _gaspTeam >= 0 ? 1 - _gaspTeam : _gaspTeam, (float)Math.Min(99, t - _gaspAt), _gaspKind, 0));
        RenderingServer.GlobalShaderParameterSet("gn_ballout", new Vector4(s.BallX, s.BallZ, outBall ? 1 : 0, 0));
    }
}
