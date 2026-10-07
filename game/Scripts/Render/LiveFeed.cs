using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Render;

/// <summary>
/// After a goal's replay, before the new score goes up: the broadcast cuts round the ground
/// for a few seconds of "live feed" while the scorers' end goes wild. Three quick shots from a
/// random mix: a drone sweeping high round the stadium, a drone swooping in low over the pitch
/// at the celebrating end, a fan's-eye camera down by the corner flag looking up into the stand,
/// and the spidercam gliding over the halfway line. Every camera stays above or inside the
/// pitch, clear of any ground's stands.
/// </summary>
public sealed class LiveFeed
{
    public bool Active { get; private set; }
    /// <summary>Each new shot's name, for the LIVE tag.</summary>
    public Action<string> OnShot;

    const float HL = 52.5f, HW = 34f;
    const float ShotLen = 2.6f;

    enum Kind { Orbit, Swoop, FanCam, Spider }
    static readonly string[] Names = { "DRONE CAM", "DRONE CAM", "FAN CAM", "SPIDERCAM" };

    readonly List<(Kind kind, float side, float lean)> _shots = new();
    readonly Random _rng = new();
    int _i;
    float _t, _end;

    /// <summary>Roll the shots: `end` is the celebrating end, -1 behind the left goal, +1 the right.</summary>
    public void Start(int end)
    {
        _end = end;
        _shots.Clear();
        // Open high, get in among the fans, finish with the other drone or the spidercam.
        var opener = _rng.Next(2) == 0 ? Kind.Orbit : Kind.Swoop;
        var closer = opener == Kind.Orbit ? (_rng.Next(2) == 0 ? Kind.Swoop : Kind.Spider) : (_rng.Next(2) == 0 ? Kind.Orbit : Kind.Spider);
        foreach (var k in new[] { opener, Kind.FanCam, closer })
            _shots.Add((k, _rng.Next(2) == 0 ? -1 : 1, (float)_rng.NextDouble() * 2 - 1));
        _i = 0;
        _t = 0;
        Active = true;
        OnShot?.Invoke(Names[(int)_shots[0].kind]);
    }

    public void Finish() => Active = false;

    public void Update(float dt, MatchCamera cam)
    {
        if (!Active) return;
        _t += dt;
        if (_t >= ShotLen)
        {
            _t -= ShotLen;
            if (++_i >= _shots.Count)
            {
                Active = false;
                return;
            }
            OnShot?.Invoke(Names[(int)_shots[_i].kind]);
        }
        var (kind, side, lean) = _shots[_i];
        float u = _t / ShotLen, s = _end;
        // A touch of hand-held drift on the low camera, a smooth glide on the rest.
        float e = u * u * (3 - 2 * u);
        Vector3 pos, look;
        float fov;
        switch (kind)
        {
            case Kind.Orbit:
            {
                // High over the far side, sweeping round toward the celebrating end.
                float a = Mathf.Pi * (0.5f + 0.32f * lean) - s * (0.25f + 0.35f * e);
                float r = 82 - 8 * e;
                pos = new Vector3(MathF.Cos(a) * r, 52 - 6 * e, -MathF.Abs(MathF.Sin(a)) * r * 0.75f);
                look = new Vector3(s * 14 * e, 0, 0);
                fov = 44;
                break;
            }
            case Kind.Swoop:
            {
                // From over the box toward the end, dropping as it goes: the stand fills the frame.
                float z = lean * 12;
                pos = new Vector3(s * (HL - 34 + 24 * e), 30 - 15 * e, z * (1 - 0.5f * e));
                look = new Vector3(s * (HL + 24), 9 - 2 * e, z * 0.3f);
                fov = 46;
                break;
            }
            case Kind.FanCam:
            {
                // Down by the corner flag at head height, panning along the end as it bounces.
                float shake = MathF.Sin(_t * 9.1f) * 0.05f + MathF.Sin(_t * 13.7f) * 0.03f;
                pos = new Vector3(s * (HL - 3.5f), 1.7f + shake, side * (HW - 2.5f));
                look = new Vector3(s * (HL + 20), 7 + shake * 4, side * (HW - 2.5f) * (0.6f - 1.4f * e));
                fov = 40;
                break;
            }
            default:
            {
                // The spidercam: gliding over halfway, then tipping toward the scorers' end.
                pos = new Vector3(-s * 8 + s * 16 * e, 22 + 2 * lean, side * (8 + 6 * e));
                look = new Vector3(s * (HL + 6) * (0.4f + 0.6f * e), 2, 0);
                fov = 48;
                break;
            }
        }
        cam.Cut(pos, look, fov);
    }
}
