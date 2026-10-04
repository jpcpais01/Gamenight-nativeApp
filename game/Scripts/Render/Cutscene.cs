using System;
using System.Collections.Generic;
using Godot;
using GameNight.Sim;

namespace GameNight.Render;

/// <summary>
/// Before kick-off (the PWA's src/ui/cutscene.ts): the teams walk out of the tunnel, wide
/// shots of the ground (the last one on the home fans' giant tifo), then the two captains at
/// the centre spot. A tap cuts to the next shot. Purely presentational: the match waits, and
/// the walk-out is drawn from a copy of its snapshot, so nothing needs handing back.
/// </summary>
public sealed class Cutscene
{
    enum Kind { Walk, Wide, Captains }

    sealed record Shot(Kind Kind, float Dur, Vector3 P0, Vector3 L0, Vector3 P1, Vector3 L1, float Fov, bool Hang = false);

    /// <summary>The front of the far stand, where the tunnel comes out.</summary>
    const float MouthZ = -((float)Pitch.HalfW + 7.5f);
    const float WalkSpeed = 1.45f;
    /// <summary>The line-up for the toss: both teams in a row behind the centre spot, facing the camera.</summary>
    const float LineZ = -5.5f;

    static Shot W(float dur, (float, float, float) p0, (float, float, float) l0, (float, float, float) p1, (float, float, float) l1, float fov, bool hang = false) =>
        new(Kind.Wide, dur, V(p0), V(l0), V(p1), V(l1), fov, hang);

    static Vector3 V((float x, float y, float z) p) => new(p.x, p.y, p.z);

    /// <summary>The wide establishing shots of each ground (the bare pitch has none).</summary>
    static Shot[] Wide(string ground) => ground switch
    {
        "comunale" => new[]
        {
            W(3, (-70, 34, 52), (10, 4, -12), (-46, 30, 60), (18, 6, -18), 40),
            // The Tribuna under its concrete canopy, the Torre and its flag above.
            W(3, (14, 3, 20), (0, 30, -70), (-8, 3, 16), (0, 36, -70), 46),
            // The Curva: two open tiers of ultras and their card display.
            W(5, (-28, 2.5f, 16), (-80, 14, 0), (-34, 3, -10), (-80, 15, 2), 44),
        },
        "old" => new[]
        {
            W(3, (-70, 30, 52), (10, 4, -12), (-46, 27, 60), (18, 6, -18), 40),
            // The old main stand and its gable.
            W(3, (12, 3, 22), (0, 9, -60), (-8, 3, 18), (0, 10, -60), 40),
            // The Shed, packed and bouncing.
            W(5, (-30, 2.5f, 14), (-80, 9, 0), (-36, 3, -10), (-80, 10, 2), 40),
        },
        "training" => new[]
        {
            W(3, (-72, 24, 54), (10, 3, -12), (-50, 20, 60), (18, 4, -18), 40),
            // The training centre, its name over the doors.
            W(4, (16, 2.6f, 16), (0, 5, -50), (-6, 2.8f, 12), (0, 5.5f, -50), 40),
        },
        "bare" => Array.Empty<Shot>(),
        _ => new[]
        {
            // Craning over the home end's corner, the whole bowl full.
            W(3, (-70, 34, 52), (10, 4, -12), (-46, 30, 60), (18, 6, -18), 40),
            // Low on the pitch, up at the home end and its card display.
            W(3, (-22, 3.5f, 20), (-80, 13, 2), (-27, 4.5f, 5), (-80, 14, -6), 40),
            // The main stand: the giant tifo drops from the roof and the camera follows it down.
            W(5, (-7, 2.2f, 27), (0, 40, -52), (6, 2.6f, 21), (0, 27, -52), 44, true),
        },
    };

    struct Spot
    {
        public float X, Z, LX, LZ;
    }

    readonly List<Shot> _shots = new();
    int _i = -1;
    float _t;
    readonly Dictionary<int, Spot> _spots = new();
    readonly int[][] _order = new int[2][];
    string[] _caption = { "", "", "", "" };

    // Each walker: where he is, how fast, which way he faces, his stride.
    readonly float[] _x = new float[MatchSnapshot.N], _z = new float[MatchSnapshot.N];
    readonly float[] _vx = new float[MatchSnapshot.N], _vz = new float[MatchSnapshot.N];
    readonly float[] _face = new float[MatchSnapshot.N], _stride = new float[MatchSnapshot.N];
    readonly bool[] _square = new bool[MatchSnapshot.N];

    /// <summary>What's drawn: the match's snapshot with everyone where the walk-out has them.</summary>
    public readonly MatchSnapshot Frame = new();
    public bool Active => _i >= 0;
    /// <summary>The giant tifo is down (or coming down): for the stadium.</summary>
    public float Hang;
    /// <summary>A new shot's caption (title, subtitle; empty title for none).</summary>
    public Action<string, string> OnCaption;
    /// <summary>Every body jumped (a cut): settle feet afresh.</summary>
    public Action OnJump;

    public void Start(Match m, MatchSnapshot cur, string ground)
    {
        Frame.CopyFrom(cur);
        // Captain first, then the rest; the keeper brings up the rear.
        for (int t = 0; t < 2; t++)
        {
            var team = m.Teams[t];
            var cap = team.Players[Math.Clamp(team.Captain, 0, team.Players.Count - 1)];
            var list = new List<int> { cap.Id };
            foreach (var p in team.Players) if (p != cap && p.Role != Role.GK) list.Add(p.Id);
            foreach (var p in team.Players) if (p != cap && p.Role == Role.GK) list.Add(p.Id);
            _order[t] = list.ToArray();
        }
        // Where each ends up: the captains at the centre spot, the rest in line.
        _spots.Clear();
        for (int t = 0; t < 2; t++)
        {
            float s = t == 0 ? -1 : 1;
            for (int k = 0; k < _order[t].Length; k++)
            {
                float x = s * (1.55f + k * 1.05f);
                _spots[_order[t][k]] = k == 0
                    ? new Spot { X = s * 0.95f, Z = 0.9f, LX = 0, LZ = 0.9f }
                    : new Spot { X = x, Z = LineZ, LX = x, LZ = 30 };
            }
        }
        string Name(int t)
        {
            var team = m.Teams[t];
            return UI.MatchInfo.Who(team.Players[Math.Clamp(team.Captain, 0, team.Players.Count - 1)]);
        }
        _caption = new[] { m.Teams[0].Info.Name, "v " + m.Teams[1].Info.Name, "Captains", $"{Name(0)} · {Name(1)}" };

        _shots.Clear();
        _shots.Add(new Shot(Kind.Walk, 3, new(3.4f, 1.55f, -28.4f), new(0, 1.25f, -36.5f), new(2.8f, 1.5f, -29.6f), new(0, 1.2f, -35.4f), 34));
        _shots.AddRange(Wide(ground));
        _shots.Add(new Shot(Kind.Captains, 4, new(0.7f, 1.75f, 7.6f), new(0, 1.15f, 0), new(0.2f, 1.6f, 5.6f), new(0, 1.2f, 0), 34));
        Hang = 0;
        _i = -1;
        Next();
    }

    /// <summary>Cut to the next shot (a tap), or end.</summary>
    public void Next()
    {
        _i++;
        _t = 0;
        if (_i >= _shots.Count)
        {
            _i = -1;
            return;
        }
        var shot = _shots[_i];
        if (shot.Hang) Hang = 1;
        if (shot.Kind == Kind.Walk) LineUpInTunnel();
        if (shot.Kind == Kind.Captains) PlaceAtSpots();
        if (shot.Kind == Kind.Walk) OnCaption?.Invoke(_caption[0], _caption[1]);
        else if (shot.Kind == Kind.Captains) OnCaption?.Invoke(_caption[2], _caption[3]);
        else OnCaption?.Invoke("", "");
        OnJump?.Invoke();
        Write();
    }

    /// <summary>Abandon it (a new match, or skipped to the end).</summary>
    public void Cancel() => _i = -1;

    public void Update(float dt, MatchCamera cam)
    {
        if (!Active) return;
        var shot = _shots[_i];
        _t += dt;
        if (_t >= shot.Dur)
        {
            Next();
            if (!Active) return;
            Update(0, cam);
            return;
        }
        Walk(dt);
        // A slow, even drift through the shot, eased only at the very start.
        float u = Math.Clamp(_t / shot.Dur, 0, 1);
        float e = u < 0.15f ? u * u / 0.3f : u - 0.075f;
        float k = e / 0.925f;
        cam.Cut(shot.P0.Lerp(shot.P1, k), shot.L0.Lerp(shot.L1, k), shot.Fov);
    }

    /// <summary>Two files in the tunnel mouth, captains at the front.</summary>
    void LineUpInTunnel()
    {
        for (int t = 0; t < 2; t++)
            for (int k = 0; k < _order[t].Length; k++)
            {
                int id = _order[t][k];
                _x[id] = t == 0 ? -0.85f : 0.85f;
                _z[id] = MouthZ + 3.4f - k * 1.25f;
                _vx[id] = 0;
                _vz[id] = WalkSpeed;
                _face[id] = MathF.PI / 2;
                _square[id] = false;
            }
    }

    /// <summary>Everyone on his mark for the toss (a cut hides the jump).</summary>
    void PlaceAtSpots()
    {
        foreach (var (id, s) in _spots)
        {
            _x[id] = s.X;
            _z[id] = s.Z;
            _vx[id] = _vz[id] = 0;
            _face[id] = MathF.Atan2(s.LZ - s.Z, s.LX - s.X);
            _square[id] = true;
        }
    }

    /// <summary>Out of the tunnel in two files, then each to his mark, at a walk.</summary>
    void Walk(float dt)
    {
        if (dt <= 0) return;
        dt = MathF.Min(dt, 1 / 30f);
        foreach (var (id, s) in _spots)
        {
            // Straight out of the tunnel until clear of the dugouts, then across to his mark.
            bool outOf = _z[id] < MouthZ + 12;
            float tx = outOf ? _x[id] : s.X, tz = outOf ? _z[id] + 4 : s.Z;
            float dx = tx - _x[id], dz = tz - _z[id];
            float d = MathF.Sqrt(dx * dx + dz * dz);
            float wvx = 0, wvz = 0;
            if (d < 0.3f) _square[id] = true;
            else
            {
                float want = MathF.Min(WalkSpeed, d * 1.2f + 0.3f);
                wvx = dx / d * want;
                wvz = dz / d * want;
                _square[id] = false;
            }
            float ka = 1 - MathF.Exp(-dt * 6);
            _vx[id] += (wvx - _vx[id]) * ka;
            _vz[id] += (wvz - _vz[id]) * ka;
            _x[id] += _vx[id] * dt;
            _z[id] += _vz[id] * dt;
            float sp = MathF.Sqrt(_vx[id] * _vx[id] + _vz[id] * _vz[id]);
            // The body goes where he walks; on his mark, it turns to what he faces.
            float wantF = _square[id] || sp < 0.6f ? MathF.Atan2(s.LZ - _z[id], s.LX - _x[id]) : MathF.Atan2(_vz[id], _vx[id]);
            float df = wantF - _face[id];
            df -= MathF.Round(df / (MathF.PI * 2)) * MathF.PI * 2;
            float turn = Math.Clamp(df, -12 * dt, 12 * dt);
            _face[id] += turn;
            // Turning on the spot still takes steps; one step is half a stride cycle.
            if (sp < 2.5f) _stride[id] += MathF.Abs(turn) * 1.6f * (1 - sp / 2.5f);
            _stride[id] += sp / (0.7f + 0.12f * sp) * MathF.PI * dt;
        }
        Write();
    }

    void Write()
    {
        var f = Frame;
        foreach (var (id, s) in _spots)
        {
            float sp = MathF.Sqrt(_vx[id] * _vx[id] + _vz[id] * _vz[id]);
            f.X[id] = _x[id];
            f.Y[id] = 0;
            f.Z[id] = _z[id];
            f.VX[id] = _vx[id];
            f.VZ[id] = _vz[id];
            f.Speed[id] = sp;
            f.Facing[id] = _face[id];
            f.StridePhase[id] = _stride[id];
            f.AccelFwd[id] = 0;
            f.HasLook[id] = _square[id];
            f.LookX[id] = s.LX;
            f.LookZ[id] = s.LZ;
            f.Action[id] = ActionKind.None;
            f.Sprinting[id] = false;
            f.LeanFwd[id] = f.LeanSide[id] = 0;
        }
        f.Controlled = f.Owner = f.HeldBy = -1;
    }
}
