using System;
using System.Collections.Generic;
using Godot;
using GameNight.Sim;

namespace GameNight.Render;

/// <summary>
/// Before kick-off (after the PWA's src/ui/cutscene.ts, grown up): the teams wait in the tunnel
/// and walk out into the noise, the camera tracking alongside; wide shots of the ground (the
/// last one on the home fans' giant tifo); the announcer reads both line-ups along the row,
/// the home end roaring every name and whistling the visitors'; the captains at the centre
/// spot; then everyone jogs out to his kick-off mark while the camera rises and settles into
/// the match camera itself, so play starts without a cut. A tap cuts to the next shot.
/// Purely presentational: the match waits, and the walk-out is drawn from a copy of its
/// snapshot, so nothing needs handing back.
/// </summary>
public sealed class Cutscene
{
    enum Kind { Tunnel, Track, Wide, Line, Captains, Ready }

    sealed record Shot(Kind Kind, float Dur, Vector3 P0, Vector3 L0, Vector3 P1, Vector3 L1, float Fov, bool Hang = false, int Team = 0);

    /// <summary>What the crowd should do as the story goes (Main turns these into the terraces' cues).</summary>
    public enum Beat { Emerge, HomeLine, HomeName, AwayLine, AwayName, Captains, Ready }

    /// <summary>The front of the far stand, where the tunnel comes out.</summary>
    const float MouthZ = -((float)Pitch.HalfW + 7.5f);
    const float WalkSpeed = 1.45f, JogSpeed = 3.6f;
    /// <summary>How long the teams stand in the tunnel before the walk.</summary>
    const float Wait = 1.3f;
    /// <summary>The line-up: both teams in a row behind the centre spot, facing the camera.</summary>
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
    /// <summary>Each man's mark in the line-up, and his kick-off mark.</summary>
    readonly Dictionary<int, Spot> _line = new(), _kick = new();
    /// <summary>Where each is walking to now (the line, the toss, or his kick-off mark).</summary>
    readonly Dictionary<int, Spot> _to = new();
    readonly int[][] _order = new int[2][];
    readonly string[] _team = new string[2];
    readonly Dictionary<int, string> _who = new();
    string[] _caption = { "", "", "", "" };
    float _speed = WalkSpeed;
    bool _tunnel;
    int _named = -1;
    MatchSnapshot _live;
    Vector3 _trackPos, _trackLook;

    // Each walker: where he is, how fast, which way he faces, his stride.
    readonly float[] _x = new float[MatchSnapshot.N], _z = new float[MatchSnapshot.N];
    readonly float[] _vx = new float[MatchSnapshot.N], _vz = new float[MatchSnapshot.N];
    readonly float[] _face = new float[MatchSnapshot.N], _stride = new float[MatchSnapshot.N];
    readonly bool[] _square = new bool[MatchSnapshot.N];

    // Each man his own walk: pace, the gap he keeps to the man ahead, where in the file he
    // walks and how he drifts in it, a step that isn't anyone else's, and where he's looking.
    const int N = MatchSnapshot.N;
    readonly float[] _pace = new float[N], _gap = new float[N], _lane = new float[N];
    readonly float[] _swayA = new float[N], _swayW = new float[N], _phase = new float[N];
    readonly float[] _fileX = new float[N], _faceJit = new float[N], _fidgetT = new float[N];
    readonly int[] _ahead = new int[N];
    float _clock;
    Random _rng = new(1);
    /// <summary>Where each head looks (x, y, z; x NaN: at the ball), for PlayersView.</summary>
    public readonly float[] Gaze = new float[N * 3];
    readonly float[] _gazeT = new float[N];

    /// <summary>What's drawn: the match's snapshot with everyone where the walk-out has them.</summary>
    public readonly MatchSnapshot Frame = new();
    public bool Active => _i >= 0;
    /// <summary>The giant tifo is down (or coming down): for the stadium.</summary>
    public float Hang;
    /// <summary>A new shot's caption (title, subtitle; empty title for none).</summary>
    public Action<string, string> OnCaption;
    /// <summary>The same caption, a new subtitle (the next name along the line-up).</summary>
    public Action<string> OnSubtitle;
    /// <summary>A moment for the crowd.</summary>
    public Action<Beat> OnBeat;
    /// <summary>Every body jumped (a cut): settle feet afresh.</summary>
    public Action OnJump;

    /// <summary>An away day: team 1 are the hosts (their name first, their line-up the home one).</summary>
    public bool Away;

    public void Start(Match m, MatchSnapshot cur, string ground)
    {
        _live = cur;
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
            _team[t] = team.Info.Name;
            foreach (var p in team.Players) _who[p.Id] = (p.Number > 0 ? p.Number + "  " : "") + UI.MatchInfo.Who(p) + (p == cap ? "  (C)" : "");
        }
        // Who walks behind whom, and each man's manner (seeded by the fixture, so a replayed
        // walk-out is the same one).
        int seed = 17;
        foreach (char c in _team[0] + "|" + _team[1]) seed = seed * 31 + c;
        _rng = new Random(seed);
        Array.Fill(_ahead, -1);
        Array.Fill(Gaze, float.NaN);
        for (int t = 0; t < 2; t++)
            for (int k = 0; k < _order[t].Length; k++)
            {
                int id = _order[t][k];
                if (k > 0) _ahead[id] = _order[t][k - 1];
                _pace[id] = WalkSpeed * Rand(0.88f, 1.12f);
                _gap[id] = Rand(1.05f, 1.6f);
                _lane[id] = Rand(-0.22f, 0.22f);
                _swayA[id] = Rand(0.04f, 0.16f);
                _swayW[id] = Rand(0.9f, 1.8f);
                _phase[id] = Rand(0, MathF.Tau);
                _stride[id] = Rand(0, MathF.Tau);
                _faceJit[id] = 0;
                _fidgetT[id] = Rand(1, 5);
                _gazeT[id] = Rand(0, 1.5f);
            }
        // The line-up row, and where kick-off has each of them.
        _line.Clear();
        _kick.Clear();
        for (int t = 0; t < 2; t++)
        {
            float s = t == 0 ? -1 : 1;
            for (int k = 0; k < _order[t].Length; k++)
            {
                int id = _order[t][k];
                float x = s * (1.55f + k * 1.05f);
                // Not a parade-ground row: a touch in front or behind, not all square to the camera.
                _line[id] = new Spot { X = x, Z = LineZ + Rand(-0.08f, 0.08f), LX = x + Rand(-4, 4), LZ = 30 };
                float f = cur.Facing[id];
                _kick[id] = new Spot { X = cur.X[id], Z = cur.Z[id], LX = cur.X[id] + MathF.Cos(f) * 10, LZ = cur.Z[id] + MathF.Sin(f) * 10 };
            }
        }
        string Name(int t)
        {
            var team = m.Teams[t];
            return UI.MatchInfo.Who(team.Players[Math.Clamp(team.Captain, 0, team.Players.Count - 1)]);
        }
        int hosts = Away ? 1 : 0;
        _caption = new[] { m.Teams[hosts].Info.Name, "v " + m.Teams[1 - hosts].Info.Name, "Captains", $"{Name(hosts)} · {Name(1 - hosts)}" };

        _shots.Clear();
        // In the tunnel's mouth, looking back in: they wait, then come out at the camera.
        _shots.Add(new Shot(Kind.Tunnel, 3.6f, new(3.2f, 1.5f, -29.2f), new(0, 1.25f, -37), new(2.6f, 1.45f, -30), new(0, 1.2f, -35.6f), 34));
        // Alongside the captains as they walk out on to the grass (placed as it goes).
        _shots.Add(new Shot(Kind.Track, 4, default, default, default, default, 38));
        _shots.AddRange(Wide(ground));
        // Along each row, captain to keeper, the announcer reading the names.
        _shots.Add(new Shot(Kind.Line, 5.5f, new(-1.1f, 1.55f, LineZ + 3.7f), new(-1.1f, 1.15f, LineZ), new(-12.6f, 1.5f, LineZ + 3.4f), new(-12.6f, 1.15f, LineZ), 36, Team: 0));
        _shots.Add(new Shot(Kind.Line, 5.5f, new(1.1f, 1.55f, LineZ + 3.7f), new(1.1f, 1.15f, LineZ), new(12.6f, 1.5f, LineZ + 3.4f), new(12.6f, 1.15f, LineZ), 36, Team: 1));
        _shots.Add(new Shot(Kind.Captains, 4, new(0.7f, 1.75f, 7.6f), new(0, 1.15f, 0), new(0.2f, 1.6f, 5.6f), new(0, 1.2f, 0), 34));
        // Out to their marks, the camera craning up from the halfway line into the match camera.
        _shots.Add(new Shot(Kind.Ready, 5, new(-6, 3.5f, 16), new(0, 1.5f, -4), new(-14, 16, 34), new(0, 0, -2), 40));
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
        _named = -1;
        switch (shot.Kind)
        {
            case Kind.Tunnel:
                LineUpInTunnel();
                OnCaption?.Invoke(_caption[0], _caption[1]);
                break;
            case Kind.Track:
                // Tapped through the tunnel: they're on their way already.
                if (_tunnel) Release();
                _trackPos = TrackPos();
                _trackLook = TrackLook();
                OnCaption?.Invoke("", "");
                break;
            case Kind.Line:
                Place(_line);
                OnCaption?.Invoke(_team[shot.Team], "");
                OnBeat?.Invoke(shot.Team == (Away ? 1 : 0) ? Beat.HomeLine : Beat.AwayLine);
                break;
            case Kind.Captains:
                Place(_line);
                // The two captains step forward to the centre spot, the rest stay in the row.
                for (int t = 0; t < 2; t++)
                {
                    int id = _order[t][0];
                    float s = t == 0 ? -1 : 1;
                    _x[id] = s * 0.95f;
                    _z[id] = 0.9f;
                    _to[id] = new Spot { X = _x[id], Z = 0.9f, LX = 0, LZ = 0.9f };
                    _face[id] = t == 0 ? 0 : MathF.PI;
                }
                OnCaption?.Invoke(_caption[2], _caption[3]);
                OnBeat?.Invoke(Beat.Captains);
                break;
            case Kind.Ready:
                ToKickOff();
                Array.Fill(Gaze, float.NaN);
                OnCaption?.Invoke("", "");
                OnBeat?.Invoke(Beat.Ready);
                break;
            default:
                OnCaption?.Invoke("", "");
                break;
        }
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
        if (_tunnel && _t >= Wait) Release();
        Walk(dt);
        float u = Math.Clamp(_t / shot.Dur, 0, 1);
        switch (shot.Kind)
        {
            case Kind.Track:
            {
                // A steady dolly beside the captains; it lags them a touch, like a hand-held rig.
                float k = 1 - MathF.Exp(-MathF.Min(dt, 0.1f) * 2.5f);
                _trackPos = _trackPos.Lerp(TrackPos(), k);
                _trackLook = _trackLook.Lerp(TrackLook(), k);
                cam.Cut(_trackPos, _trackLook, shot.Fov);
                return;
            }
            case Kind.Line:
            {
                Pan(cam, shot, u);
                // Whoever the camera is on, the announcer names.
                float cx = shot.P0.Lerp(shot.P1, Ease(u)).X;
                int best = -1;
                float bd = 1e9f;
                foreach (int id in _order[shot.Team])
                {
                    float d = MathF.Abs(_line[id].X - cx);
                    if (d < bd) (bd, best) = (d, id);
                }
                if (best != _named && best >= 0)
                {
                    _named = best;
                    OnSubtitle?.Invoke(_who.GetValueOrDefault(best, ""));
                    OnBeat?.Invoke(shot.Team == (Away ? 1 : 0) ? Beat.HomeName : Beat.AwayName);
                }
                return;
            }
            case Kind.Ready:
            {
                // The match camera, already following the kick-off: the shot rises into it.
                cam.Update(_live, _live, 1, dt);
                var game = cam.Camera;
                var gp = game.GlobalPosition;
                var gd = -game.GlobalBasis.Z;
                float gf = game.Fov;
                float w = Smooth(Math.Clamp((u - 0.2f) / 0.7f, 0, 1));
                if (w >= 1) return;
                float e = Ease(u);
                var sp = shot.P0.Lerp(shot.P1, e);
                var sd = (shot.L0.Lerp(shot.L1, e) - sp).Normalized();
                var pos = sp.Lerp(gp, w);
                var dir = sd.Slerp(gd.Normalized(), w);
                cam.Cut(pos, pos + dir * 30, Mathf.Lerp(shot.Fov, gf, w));
                return;
            }
            default:
                Pan(cam, shot, u);
                return;
        }
    }

    /// <summary>A slow, even drift through the shot, eased only at the very start.</summary>
    static float Ease(float u) => (u < 0.15f ? u * u / 0.3f : u - 0.075f) / 0.925f;

    static float Smooth(float x) => x * x * (3 - 2 * x);

    static void Pan(MatchCamera cam, Shot shot, float u)
    {
        float k = Ease(u);
        cam.Cut(shot.P0.Lerp(shot.P1, k), shot.L0.Lerp(shot.L1, k), shot.Fov);
    }

    (float x, float z) Captains()
    {
        int a = _order[0][0], b = _order[1][0];
        return ((_x[a] + _x[b]) / 2, (_z[a] + _z[b]) / 2);
    }

    Vector3 TrackPos()
    {
        var (x, z) = Captains();
        return new Vector3(x + 4.6f, 1.55f, z + 4.4f);
    }

    Vector3 TrackLook()
    {
        var (x, z) = Captains();
        return new Vector3(x - 0.4f, 1.2f, z - 0.6f);
    }

    /// <summary>Two files in the tunnel mouth, captains at the front, waiting.</summary>
    void LineUpInTunnel()
    {
        for (int t = 0; t < 2; t++)
            for (int k = 0; k < _order[t].Length; k++)
            {
                int id = _order[t][k];
                _fileX[id] = (t == 0 ? -0.85f : 0.85f) + _lane[id] * 0.5f;
                _x[id] = _fileX[id];
                _z[id] = MouthZ + 3.4f - k * 1.25f + Rand(-0.12f, 0.12f);
                _vx[id] = _vz[id] = 0;
                _face[id] = MathF.PI / 2;
                _square[id] = true;
                // Waiting: eyes front (a captain glances across at the other).
                _to[id] = new Spot { X = _x[id], Z = _z[id], LX = k == 0 ? -_x[id] * 3 : _x[id], LZ = _z[id] + 10 };
            }
        _speed = WalkSpeed;
        _tunnel = true;
    }

    /// <summary>The referee's nod: off they go, out and over to the line-up.</summary>
    void Release()
    {
        _tunnel = false;
        foreach (var (id, s) in _line) _to[id] = s;
        OnBeat?.Invoke(Beat.Emerge);
    }

    /// <summary>Everyone on his mark (a cut hides the jump).</summary>
    void Place(Dictionary<int, Spot> marks)
    {
        _tunnel = false;
        foreach (var (id, s) in marks)
        {
            _x[id] = s.X;
            _z[id] = s.Z;
            _vx[id] = _vz[id] = 0;
            _face[id] = MathF.Atan2(s.LZ - s.Z, s.LX - s.X);
            _square[id] = true;
            _to[id] = s;
        }
    }

    /// <summary>Broken up from the row and jogging out to kick-off: each starts close enough
    /// to reach his mark as the shot settles (the cut hides who skipped ahead).</summary>
    void ToKickOff()
    {
        _tunnel = false;
        _speed = JogSpeed;
        foreach (var (id, k) in _kick)
        {
            var l = _line[id];
            float dx = k.X - l.X, dz = k.Z - l.Z;
            float d = MathF.Sqrt(dx * dx + dz * dz);
            float reach = JogSpeed * 3.2f;
            float back = MathF.Min(d, reach) / MathF.Max(d, 1e-3f);
            _x[id] = k.X - dx * back;
            _z[id] = k.Z - dz * back;
            _vx[id] = dx / MathF.Max(d, 1e-3f) * JogSpeed * MathF.Min(1, d);
            _vz[id] = dz / MathF.Max(d, 1e-3f) * JogSpeed * MathF.Min(1, d);
            _pace[id] = JogSpeed * Rand(0.9f, 1.1f);
            _face[id] = MathF.Atan2(dz, dx);
            _square[id] = false;
            _to[id] = k;
        }
    }

    float Rand(float a, float b) => a + (b - a) * (float)_rng.NextDouble();

    /// <summary>Out of the tunnel in two files, then each to his mark: each at his own pace,
    /// keeping his own gap to the man ahead, drifting a little in the file, glancing about.</summary>
    void Walk(float dt)
    {
        if (dt <= 0) return;
        dt = MathF.Min(dt, 1 / 30f);
        _clock += dt;
        bool walking = _speed == WalkSpeed;
        foreach (var (id, s) in _to)
        {
            // Straight out of the tunnel until clear of the dugouts, then across to his mark.
            bool outOf = !_tunnel && _z[id] < MouthZ + 12 && walking;
            float sway = walking ? _lane[id] + _swayA[id] * MathF.Sin(_clock * _swayW[id] + _phase[id]) : 0;
            float tx = outOf ? _fileX[id] + sway : s.X, tz = outOf ? _z[id] + 4 : s.Z;
            float dx = tx - _x[id], dz = tz - _z[id];
            float d = MathF.Sqrt(dx * dx + dz * dz);
            float wvx = 0, wvz = 0;
            if (d < 0.3f) _square[id] = true;
            else
            {
                float pace = walking && !_tunnel ? _pace[id] * (1 + 0.05f * MathF.Sin(_clock * 0.9f * _swayW[id] + 2 * _phase[id])) : _pace[id];
                float want = MathF.Min(pace, d * 1.2f + 0.3f);
                // Never up the heels of the man in front (while he walks): ease off inside your gap,
                // close it up outside.
                int a = _ahead[id];
                if (walking && a >= 0 && !_square[a])
                {
                    float ax = _x[a] - _x[id], az = _z[a] - _z[id];
                    float ad = MathF.Sqrt(ax * ax + az * az);
                    if (ad < 3 && ax * dx + az * dz > 0)
                        want *= Math.Clamp((ad - 0.7f * _gap[id]) / (0.45f * _gap[id]), 0, 1.12f);
                }
                // Across to the mark, the drift fades as he gets there.
                float side = outOf ? 0 : sway * Math.Clamp(d / 4, 0, 1) * 0.6f;
                wvx = dx / d * want - dz / d * side;
                wvz = dz / d * want + dx / d * side;
                _square[id] = false;
            }
            float ka = 1 - MathF.Exp(-dt * 6);
            _vx[id] += (wvx - _vx[id]) * ka;
            _vz[id] += (wvz - _vz[id]) * ka;
            _x[id] += _vx[id] * dt;
            _z[id] += _vz[id] * dt;
            float sp = MathF.Sqrt(_vx[id] * _vx[id] + _vz[id] * _vz[id]);
            // Stood still, now and then he shifts his weight and squares up again.
            if (_square[id] && walking && (_fidgetT[id] -= dt) <= 0)
            {
                _faceJit[id] = _rng.NextDouble() < 0.6 ? Rand(-0.35f, 0.35f) : 0;
                _fidgetT[id] = Rand(2.5f, 6);
            }
            // The body goes where he walks; on his mark, it turns to what he faces.
            float wantF = _square[id] || sp < 0.6f ? MathF.Atan2(s.LZ - _z[id], s.LX - _x[id]) + _faceJit[id] : MathF.Atan2(_vz[id], _vx[id]);
            float df = wantF - _face[id];
            df -= MathF.Round(df / (MathF.PI * 2)) * MathF.PI * 2;
            float turn = Math.Clamp(df, -(_square[id] ? 3 : 12) * dt, (_square[id] ? 3 : 12) * dt);
            _face[id] += turn;
            // Turning on the spot still takes steps; one step is half a stride cycle.
            if (sp < 2.5f) _stride[id] += MathF.Abs(turn) * 1.6f * (1 - sp / 2.5f);
            _stride[id] += sp / (float)Player.StepLength(sp, 1) * MathF.PI * dt;
            if (walking && (_gazeT[id] -= dt) <= 0) Glance(id, s, sp);
        }
        Write();
    }

    /// <summary>Somewhere new to look: up the tunnel, at the stands, at a team-mate, at his boots.</summary>
    void Glance(int id, Spot s, float sp)
    {
        float r = (float)_rng.NextDouble();
        float x = _x[id], z = _z[id];
        float gx, gy, gz;
        int a = _ahead[id];
        if (_tunnel)
        {
            _gazeT[id] = Rand(1.2f, 3);
            if (r < 0.55f) (gx, gy, gz) = (x, 1.7f, z + 20);
            else if (r < 0.8f) (gx, gy, gz) = (-x * 2, 1.5f, z + Rand(-1.5f, 1.5f));
            else (gx, gy, gz) = (x + Rand(-0.3f, 0.3f), 0, z + 1.6f);
        }
        else if (sp > 0.5f)
        {
            _gazeT[id] = Rand(0.8f, 2.4f);
            float hw = (float)Pitch.HalfW, hl = (float)Pitch.HalfL;
            if (r < 0.3f) (gx, gy, gz) = (s.X, 1.5f, s.Z);
            else if (r < 0.55f) (gx, gy, gz) = (Rand(-hl, hl), Rand(7, 16), hw + 14);
            else if (r < 0.8f) (gx, gy, gz) = (MathF.Sign(Rand(-1, 1)) * (hl + 12), Rand(6, 14), Rand(-hw, hw));
            else if (a >= 0) (gx, gy, gz) = (_x[a], 1.6f, _z[a]);
            else (gx, gy, gz) = (x, 0.5f, z + 4);
        }
        else
        {
            _gazeT[id] = Rand(1.5f, 4);
            if (r < 0.55f) (gx, gy, gz) = (x + Rand(-3, 3), 1.6f, 30);
            else if (r < 0.85f) (gx, gy, gz) = (Rand(-40, 40), Rand(9, 18), (float)Pitch.HalfW + 14);
            else (gx, gy, gz) = (x + Rand(-6, 6), 1.5f, z);
        }
        Gaze[id * 3] = gx;
        Gaze[id * 3 + 1] = gy;
        Gaze[id * 3 + 2] = gz;
    }

    void Write()
    {
        var f = Frame;
        foreach (var (id, s) in _to)
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
