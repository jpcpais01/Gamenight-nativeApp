using System;

namespace GameNight.Bridge;

/// <summary>
/// A small stand-in match so the game is playable before the ported engine lands: 4-4-2
/// shape that shifts with the ball, dribbling, passes, through balls, shots, tackles,
/// throw-ins, corners, goal kicks and goals. Deliberately simple; the real rules live in
/// /sim and replace this behind <see cref="IMatchSource"/>.
/// </summary>
public sealed class StubMatch : IMatchSource
{
    const float HL = 52.5f, HW = 34f, GoalHalf = 3.66f, GoalH = 2.44f, BallR = 0.11f;
    const float G = 9.81f;
    const int N = 22;
    const float HalfSeconds = 150f;

    public float Dt => 1f / 120f;

    // Shape in the frame of a team attacking +X (index 0 is the keeper).
    static readonly float[] SlotX = { -49, -34, -36, -36, -34, -16, -18, -18, -16, -5, -5 };
    static readonly float[] SlotZ = { 0, -22, -8, 8, 22, -22, -7, 7, 22, -7, 7 };

    readonly Random _rng;
    readonly float[] _x = new float[N], _z = new float[N], _vx = new float[N], _vz = new float[N];
    readonly float[] _face = new float[N], _height = new float[N], _cool = new float[N];
    readonly byte[] _skin = new byte[N];
    readonly int[] _dir = { 1, -1 };
    readonly int[] _score = new int[2];

    float _bx, _by = BallR, _bz, _bvx, _bvy, _bvz;
    int _owner = -1, _lastTeam = -1, _receiver = -1, _controlled = 9;
    float _decide, _phaseT, _clock;
    MatchPhase _phase = MatchPhase.Kickoff;
    int _kickoffTeam;
    // Pending restart after the ball goes out.
    float _restartX, _restartZ;
    int _restartTeam;
    bool _restartKeeper;
    bool _secondHalf;

    public StubMatch(int seed)
    {
        _rng = new Random(seed);
        for (int i = 0; i < N; i++)
        {
            _height[i] = 1.72f + (float)_rng.NextDouble() * 0.2f;
            _skin[i] = (byte)_rng.Next(4);
        }
        BeginKickoff(0);
    }

    public bool HumanAttacking => _lastTeam == 0;

    static int TeamOf(int i) => i < 11 ? 0 : 1;
    static bool IsKeeper(int i) => i == 0 || i == 11;
    float R() => (float)_rng.NextDouble();

    // ------------------------------------------------------------------ step

    public void Step(InputState input)
    {
        float dt = Dt;
        _phaseT += dt;
        for (int i = 0; i < N; i++) _cool[i] = MathF.Max(0, _cool[i] - dt);

        switch (_phase)
        {
            case MatchPhase.Kickoff:
                input.Events.Clear();
                if (_phaseT > 1.2f) StartPlay(_kickoffTeam == 0 ? 9 : 20);
                break;
            case MatchPhase.Play:
                _clock += dt;
                HumanInput(input, dt);
                Think(dt);
                break;
            case MatchPhase.Out:
                input.Events.Clear();
                if (_phaseT > 1.0f) Restart();
                break;
            case MatchPhase.Goal:
                input.Events.Clear();
                if (_phaseT > 3.5f) BeginKickoff(_kickoffTeam);
                break;
            case MatchPhase.HalfTime:
                input.Events.Clear();
                if (_phaseT > 3f) BeginKickoff(1);
                break;
            case MatchPhase.FullTime:
                input.Events.Clear();
                if (_phaseT > 6f)
                {
                    _score[0] = _score[1] = 0;
                    _clock = 0;
                    _secondHalf = false;
                    BeginKickoff(0);
                }
                break;
        }

        MovePlayers(input, dt);
        MoveBall(dt);

        if (_phase == MatchPhase.Play)
        {
            Pickups();
            CheckBall();
            if (!_secondHalf && _clock >= HalfSeconds) { _secondHalf = true; SetPhase(MatchPhase.HalfTime); _owner = -1; }
            else if (_clock >= HalfSeconds * 2) { SetPhase(MatchPhase.FullTime); _owner = -1; }
        }
    }

    void SetPhase(MatchPhase p)
    {
        _phase = p;
        _phaseT = 0;
    }

    void BeginKickoff(int team)
    {
        SetPhase(MatchPhase.Kickoff);
        _kickoffTeam = team;
        _owner = _receiver = -1;
        _bx = _bz = _bvx = _bvy = _bvz = 0;
        _by = BallR;
        for (int i = 0; i < N; i++)
        {
            int t = TeamOf(i), s = i % 11, d = _dir[t];
            float x = MathF.Min(SlotX[s] * 0.9f, -1.5f);
            _x[i] = x * d;
            _z[i] = SlotZ[s] * d;
            _vx[i] = _vz[i] = 0;
            _face[i] = d > 0 ? 0 : MathF.PI;
        }
        // The kicking side's two forwards stand over the ball.
        int k = team == 0 ? 9 : 20;
        _x[k] = -0.6f * _dir[team];
        _z[k] = 0;
        _x[k + 1] = -1.5f * _dir[team];
        _z[k + 1] = 3;
        _controlled = team == 0 ? 9 : 10;
        _lastTeam = team;
    }

    void StartPlay(int owner)
    {
        SetPhase(MatchPhase.Play);
        _owner = owner;
        _lastTeam = TeamOf(owner);
        _decide = 0.5f;
        if (TeamOf(owner) == 0) _controlled = owner;
    }

    // ------------------------------------------------------------------ the human

    void HumanInput(InputState input, float dt)
    {
        int c = _controlled;
        bool hasBall = _owner == c;
        bool theyHaveIt = _owner >= 0 && TeamOf(_owner) == 1;
        foreach (var e in input.Events)
        {
            if (hasBall && !e.Down)
            {
                if (e.Btn == Btn.A) Pass(c, input, false, e.SwipeUp);
                else if (e.Btn == Btn.B) Pass(c, input, true, e.SwipeUp);
                else Shoot(c, input, Math.Clamp(e.Hold / 0.85f, 0.15f, 1f));
                hasBall = false;
            }
            else if (!hasBall && e.Down)
            {
                if (e.Btn == Btn.A && theyHaveIt) TryTackle(c, 0.75f);
                else if (e.Btn == Btn.B && _owner != c) SwitchToNearest();
            }
        }
        input.Events.Clear();
        if (input.TackleSwipe != TackleSwipe.None)
        {
            if (theyHaveIt) TryTackle(c, input.TackleSwipe == TackleSwipe.Slide ? 0.6f : 0.75f);
            input.TackleSwipe = TackleSwipe.None;
        }
    }

    void SwitchToNearest()
    {
        int best = -1;
        float bd = float.MaxValue;
        for (int i = 1; i < 11; i++)
        {
            if (i == _controlled) continue;
            float d = Dist2(i, _bx, _bz);
            if (d < bd) { bd = d; best = i; }
        }
        if (best >= 0) _controlled = best;
    }

    // ------------------------------------------------------------------ AI

    void Think(float dt)
    {
        // The defending side's chaser goes in for the ball when he's close enough.
        if (_owner >= 0)
        {
            int ch = Chaser(1 - TeamOf(_owner));
            if (ch >= 0 && _cool[ch] <= 0 && Dist2(ch, _bx, _bz) < 1.2f && R() < 1.6f * dt) TryTackle(ch, 0.55f);
        }
        if (_owner >= 0 && _owner != _controlled)
        {
            _decide -= dt;
            if (_decide <= 0)
            {
                _decide = 0.35f + R() * 0.3f;
                int o = _owner, t = TeamOf(o), d = _dir[t];
                float toGoal = MathF.Abs(HL * d - _x[o]);
                bool pressed = NearestOpponentDist(o) < 2.6f;
                if (IsKeeper(o)) { if (_phaseT > 0 && R() < 0.6f) AiPass(o, true); }
                else if (toGoal < 25 && MathF.Abs(_z[o]) < 18 && R() < 0.45f) AiShoot(o);
                else if (pressed ? R() < 0.7f : R() < 0.14f) AiPass(o, R() < 0.2f);
            }
        }
    }

    float NearestOpponentDist(int i)
    {
        float best = float.MaxValue;
        int from = TeamOf(i) == 0 ? 11 : 0;
        for (int j = from; j < from + 11; j++) best = MathF.Min(best, Dist2(j, _x[i], _z[i]));
        return MathF.Sqrt(best);
    }

    void AiPass(int o, bool longBall)
    {
        int t = TeamOf(o), d = _dir[t];
        int best = -1;
        float bs = float.MinValue;
        for (int j = t * 11 + 1; j < t * 11 + 11; j++)
        {
            if (j == o) continue;
            float dx = _x[j] - _x[o], dz = _z[j] - _z[o];
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist < 6 || dist > (longBall ? 50 : 30)) continue;
            float s = dx * d * 0.08f - dist * 0.02f + NearestOpponentDist(j) * 0.25f + R() * 0.8f;
            if (s > bs) { bs = s; best = j; }
        }
        if (best < 0) return;
        KickTo(o, _x[best] + _vx[best] * 0.6f, _z[best] + _vz[best] * 0.6f, longBall && R() < 0.5f);
        _receiver = best;
    }

    void AiShoot(int o)
    {
        int d = _dir[TeamOf(o)];
        float aim = (R() * 2 - 1) * 3.4f;
        ShootAt(o, HL * d, aim, 0.5f + R() * 0.5f, 0.06f);
    }

    void TryTackle(int i, float chance)
    {
        if (_owner < 0 || Dist2(i, _x[_owner], _z[_owner]) > 1.7f * 1.7f) return;
        if (R() < chance)
        {
            int o = _owner;
            _owner = -1;
            _cool[o] = 0.6f;
            float a = R() * MathF.Tau;
            _bvx = MathF.Cos(a) * 3 + (_x[i] - _x[o]) * -1.5f;
            _bvz = MathF.Sin(a) * 3 + (_z[i] - _z[o]) * -1.5f;
            _bvy = 0.5f;
        }
        else _cool[i] = 0.5f;
    }

    // ------------------------------------------------------------------ kicks

    void Pass(int o, InputState input, bool through, bool lofted)
    {
        int t = TeamOf(o), d = _dir[t];
        // Preferred direction: the stick (screen up = away from the camera = -Z), else facing.
        float px = input.MoveX, pz = -input.MoveY;
        float pm = MathF.Sqrt(px * px + pz * pz);
        if (pm < 0.3f) { px = MathF.Cos(_face[o]); pz = MathF.Sin(_face[o]); }
        else { px /= pm; pz /= pm; }
        int best = -1;
        float bs = float.MinValue;
        for (int j = t * 11; j < t * 11 + 11; j++)
        {
            if (j == o) continue;
            float dx = _x[j] - _x[o], dz = _z[j] - _z[o];
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist < 3) continue;
            float cos = (dx * px + dz * pz) / dist;
            float s = cos * 2 - dist / 40f;
            if (s > bs) { bs = s; best = j; }
        }
        if (best < 0) return;
        float tx = _x[best], tz = _z[best];
        float lead = through ? 7f + MathF.Sqrt(Dist2(best, _x[o], _z[o])) * 0.1f : 0.6f;
        if (through) { tx += d * lead; tz += pz * 2; }
        else { tx += _vx[best] * lead; tz += _vz[best] * lead; }
        tx = Math.Clamp(tx, -HL + 1, HL - 1);
        tz = Math.Clamp(tz, -HW + 1, HW - 1);
        KickTo(o, tx, tz, lofted);
        _receiver = best;
    }

    void Shoot(int o, InputState input, float power)
    {
        int d = _dir[TeamOf(o)];
        // Stick up/down aims at the far/near post; centred aims a little inside a random post.
        float aim = MathF.Abs(input.MoveY) > 0.25f ? -MathF.Sign(input.MoveY) * 2.9f : (R() < 0.5f ? -2.4f : 2.4f);
        ShootAt(o, HL * d, aim, power, 0.035f);
    }

    void ShootAt(int o, float gx, float gz, float power, float error)
    {
        float dx = gx - _bx, dz = gz - _bz;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        float speed = 15 + 15 * power;
        float a = MathF.Atan2(dz, dx) + (R() * 2 - 1) * error * (1 + dist / 25);
        Release(o, MathF.Cos(a) * speed, 1.2f + 5.5f * power * power + dist * 0.03f, MathF.Sin(a) * speed);
    }

    void KickTo(int o, float tx, float tz, bool lofted)
    {
        float dx = tx - _bx, dz = tz - _bz;
        float dist = MathF.Max(1, MathF.Sqrt(dx * dx + dz * dz));
        float speed, vy;
        if (lofted)
        {
            // Flight time from the launch speed up; the ball lands near the target.
            vy = Math.Clamp(dist * 0.3f, 4, 13);
            float tFlight = 2 * vy / G;
            speed = dist * 0.92f / tFlight;
        }
        else
        {
            // Rolling: arrive at about 4 m/s against grass and drag.
            speed = MathF.Sqrt(16 + 2 * 1.6f * dist) + dist * 0.06f;
            vy = 0.4f;
        }
        Release(o, dx / dist * speed, vy, dz / dist * speed);
    }

    void Release(int o, float vx, float vy, float vz)
    {
        _owner = -1;
        _cool[o] = 0.35f;
        _bvx = vx;
        _bvy = vy;
        _bvz = vz;
        _face[o] = MathF.Atan2(vz, vx);
    }

    // ------------------------------------------------------------------ movement

    void MovePlayers(InputState input, float dt)
    {
        int chaser0 = Chaser(0), chaser1 = Chaser(1);
        for (int i = 0; i < N; i++)
        {
            int t = TeamOf(i), d = _dir[t];
            float tx, tz, speed;
            bool human = i == _controlled && t == 0 && _phase == MatchPhase.Play;
            if (_phase is MatchPhase.Kickoff or MatchPhase.HalfTime or MatchPhase.FullTime)
            {
                Steer(i, _x[i], _z[i], 0, dt);
                continue;
            }
            if (human && !(input.Held[2] && _owner >= 0 && TeamOf(_owner) == 1))
            {
                float mx = input.MoveX, mz = -input.MoveY;
                float top = (input.Sprint ? 8.6f : 6.4f) * (_owner == i ? 0.9f : 1f);
                SteerVel(i, mx * top, mz * top, dt);
                continue;
            }
            if (_phase == MatchPhase.Goal)
            {
                Steer(i, _x[i] * 0.98f, _z[i] * 0.98f, 1.5f, dt);
                continue;
            }
            if (i == _owner)
            {
                // Carry it toward goal, drifting away from the nearest marker.
                tx = HL * d;
                tz = _z[i] * 0.6f;
                speed = IsKeeper(i) ? 0 : 6.2f;
            }
            else if (i == _receiver && _owner < 0)
            {
                tx = _bx + _bvx * 0.35f;
                tz = _bz + _bvz * 0.35f;
                speed = 8f;
            }
            else if (i == chaser0 || i == chaser1 || (human && input.Held[2]))
            {
                float lead = _owner >= 0 ? 0.25f : 0.3f;
                tx = _bx + _bvx * lead;
                tz = _bz + _bvz * lead;
                speed = human ? 8.6f : (_owner >= 0 ? 7.6f : 8.2f);
            }
            else if (IsKeeper(i))
            {
                float gx = -HL * d;
                tx = gx + d * Math.Clamp((_bx * d + HL) * 0.04f, 0.5f, 3.5f);
                tz = Math.Clamp(_bz * 0.12f, -2.6f, 2.6f);
                speed = 5;
            }
            else
            {
                int s = i % 11;
                bool attacking = _lastTeam == t;
                float bxT = _bx * d;
                float x = SlotX[s] * 0.75f + bxT * 0.55f + (attacking ? 9 : -3);
                x = Math.Clamp(x, -HL + 4, HL - 8);
                tx = x * d;
                tz = SlotZ[s] * d * 0.95f + _bz * 0.28f;
                speed = 5.6f;
            }
            Steer(i, tx, tz, speed, dt);
        }
    }

    /// <summary>The nearest outfielder to the ball who should go for it (not the human).</summary>
    int Chaser(int team)
    {
        if (_owner >= 0 && TeamOf(_owner) == team) return -1;
        if (_receiver >= 0 && TeamOf(_receiver) == team && _owner < 0) return -1;
        int best = -1;
        float bd = float.MaxValue;
        for (int i = team * 11; i < team * 11 + 11; i++)
        {
            if (i == _controlled && team == 0) continue;
            if (IsKeeper(i) && !InBox(i)) continue;
            float dd = Dist2(i, _bx, _bz);
            if (dd < bd) { bd = dd; best = i; }
        }
        return best;
    }

    bool InBox(int k) => MathF.Abs(_bx - (-HL * _dir[TeamOf(k)])) < 16.5f && MathF.Abs(_bz) < 20f;

    void Steer(int i, float tx, float tz, float speed, float dt)
    {
        float dx = tx - _x[i], dz = tz - _z[i];
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        float want = MathF.Min(speed, dist * 1.8f);
        if (dist > 1e-3f) SteerVel(i, dx / dist * want, dz / dist * want, dt);
        else SteerVel(i, 0, 0, dt);
    }

    void SteerVel(int i, float wvx, float wvz, float dt)
    {
        float ax = wvx - _vx[i], az = wvz - _vz[i];
        float am = MathF.Sqrt(ax * ax + az * az);
        float maxA = 9f * dt;
        if (am > maxA) { ax *= maxA / am; az *= maxA / am; }
        _vx[i] += ax;
        _vz[i] += az;
        _x[i] = Math.Clamp(_x[i] + _vx[i] * dt, -HL - 4, HL + 4);
        _z[i] = Math.Clamp(_z[i] + _vz[i] * dt, -HW - 3, HW + 3);
        float sp = MathF.Sqrt(_vx[i] * _vx[i] + _vz[i] * _vz[i]);
        float want = sp > 0.4f ? MathF.Atan2(_vz[i], _vx[i]) : MathF.Atan2(_bz - _z[i], _bx - _x[i]);
        float diff = MathF.IEEERemainder(want - _face[i], MathF.Tau);
        _face[i] += diff * MathF.Min(1, dt * 10);
    }

    // ------------------------------------------------------------------ ball

    void MoveBall(float dt)
    {
        if (_owner >= 0)
        {
            int o = _owner;
            float fx = MathF.Cos(_face[o]), fz = MathF.Sin(_face[o]);
            // A dribble: the ball runs a touch ahead of the feet.
            float tx = _x[o] + fx * 0.62f, tz = _z[o] + fz * 0.62f;
            _bvx = (tx - _bx) / dt * 0.25f + _vx[o] * 0.75f;
            _bvz = (tz - _bz) / dt * 0.25f + _vz[o] * 0.75f;
            _bvy = 0;
            _bx += (tx - _bx) * 0.25f + _vx[o] * dt * 0.75f;
            _bz += (tz - _bz) * 0.25f + _vz[o] * dt * 0.75f;
            _by = BallR;
            return;
        }
        float sp = MathF.Sqrt(_bvx * _bvx + _bvy * _bvy + _bvz * _bvz);
        // Air drag (with the drag crisis above ~14 m/s), as in the PWA's ball constants.
        float cd = sp > 14 ? 0.18f : 0.47f;
        float k = 0.5f * 1.2f * cd * (MathF.PI * BallR * BallR) / 0.43f * sp;
        _bvx -= _bvx * k * dt;
        _bvy -= _bvy * k * dt + G * dt;
        _bvz -= _bvz * k * dt;
        float px = _bx;
        _bx += _bvx * dt;
        _by += _bvy * dt;
        _bz += _bvz * dt;
        if (_by < BallR)
        {
            _by = BallR;
            if (_bvy < -1.2f) { _bvy = -_bvy * 0.62f; _bvx *= 0.86f; _bvz *= 0.86f; }
            else _bvy = 0;
        }
        if (_by <= BallR + 1e-3f && _bvy == 0)
        {
            float h = MathF.Sqrt(_bvx * _bvx + _bvz * _bvz);
            float nh = MathF.Max(0, h - 0.75f * dt);
            if (h > 0) { _bvx *= nh / h; _bvz *= nh / h; }
        }
        // Posts and crossbar: a crude bounce off the frame as it crosses the goal line.
        if (MathF.Abs(px) < HL && MathF.Abs(_bx) >= HL)
        {
            float az = MathF.Abs(_bz);
            bool post = MathF.Abs(az - GoalHalf) < 0.17f && _by < GoalH;
            bool bar = az < GoalHalf && MathF.Abs(_by - GoalH) < 0.17f;
            if (post || bar)
            {
                _bx = MathF.Sign(_bx) * (HL - 0.01f);
                _bvx = -_bvx * 0.65f;
                if (bar) _bvy = -MathF.Abs(_bvy) * 0.5f;
            }
        }
        // In the net: the back of the net stops it.
        if (MathF.Abs(_bx) > HL + 1.9f && MathF.Abs(_bz) < GoalHalf + 0.5f)
        {
            _bx = MathF.Sign(_bx) * (HL + 1.9f);
            _bvx *= -0.1f;
            _bvz *= 0.3f;
        }
    }

    void Pickups()
    {
        if (_owner >= 0) return;
        float sp = MathF.Sqrt(_bvx * _bvx + _bvz * _bvz);
        int best = -1;
        float bd = float.MaxValue;
        for (int i = 0; i < N; i++)
        {
            if (_cool[i] > 0) continue;
            bool keeper = IsKeeper(i) && InBox(i);
            float reach = keeper ? 1.6f : 0.8f;
            float maxH = keeper ? 2.4f : 1.0f;
            if (_by > maxH) continue;
            float d = Dist2(i, _bx, _bz);
            if (d < reach * reach && d < bd) { bd = d; best = i; }
        }
        if (best < 0) return;
        // A hard shot past an outfielder is often just a deflection.
        if (sp > 17 && !IsKeeper(best) && R() < 0.7f)
        {
            _bvx *= -0.25f; _bvz *= -0.25f; _bvy = 1.5f;
            _cool[best] = 0.3f;
            return;
        }
        // A keeper parries some of the hard ones.
        if (sp > 20 && IsKeeper(best) && R() < 0.3f)
        {
            _bvx *= -0.3f; _bvz = (R() - 0.5f) * 8; _bvy = 2f;
            _cool[best] = 0.5f;
            _lastTeam = TeamOf(best);
            return;
        }
        _owner = best;
        _receiver = -1;
        _lastTeam = TeamOf(best);
        _decide = IsKeeper(best) ? 1.2f : 0.25f + R() * 0.3f;
        if (TeamOf(best) == 0) _controlled = best;
        else if (Dist2(_controlled, _bx, _bz) > 12 * 12) SwitchToNearest();
    }

    void CheckBall()
    {
        float ax = MathF.Abs(_bx), az = MathF.Abs(_bz);
        if (ax > HL + BallR)
        {
            int side = _bx > 0 ? 1 : -1;
            // The team defending this end: the one attacking the other way.
            int defending = _dir[0] == side ? 1 : 0;
            if (az < GoalHalf && _by < GoalH)
            {
                int scorer = 1 - defending;
                _score[scorer]++;
                SetPhase(MatchPhase.Goal);
                _owner = -1;
                _kickoffTeam = defending;
                return;
            }
            if (_lastTeam == defending)
            {
                _restartTeam = 1 - defending;
                _restartX = side * (HL - 0.4f);
                _restartZ = MathF.Sign(_bz) * (HW - 0.4f);
                _restartKeeper = false;
            }
            else
            {
                _restartTeam = defending;
                _restartX = side * (HL - 5.5f);
                _restartZ = 0;
                _restartKeeper = true;
            }
            GoOut();
        }
        else if (az > HW + BallR)
        {
            _restartTeam = 1 - Math.Max(0, _lastTeam);
            _restartX = Math.Clamp(_bx, -HL + 1, HL - 1);
            _restartZ = MathF.Sign(_bz) * (HW + 0.2f);
            _restartKeeper = false;
            GoOut();
        }
    }

    void GoOut()
    {
        SetPhase(MatchPhase.Out);
        _owner = _receiver = -1;
    }

    void Restart()
    {
        int t = _restartTeam;
        int taker = t * 11;
        if (!_restartKeeper)
        {
            float bd = float.MaxValue;
            for (int i = t * 11 + 1; i < t * 11 + 11; i++)
            {
                float d = Dist2(i, _restartX, _restartZ);
                if (d < bd) { bd = d; taker = i; }
            }
        }
        _bx = _restartX;
        _bz = _restartZ;
        _by = BallR;
        _bvx = _bvy = _bvz = 0;
        // The taker steps up behind the ball, facing infield.
        float fx = -_bx, fz = -_bz;
        float fm = MathF.Max(1e-3f, MathF.Sqrt(fx * fx + fz * fz));
        _x[taker] = _bx - fx / fm * 0.6f;
        _z[taker] = _bz - fz / fm * 0.6f;
        _vx[taker] = _vz[taker] = 0;
        _face[taker] = MathF.Atan2(fz, fx);
        StartPlay(taker);
        _decide = 0.8f;
    }

    float Dist2(int i, float x, float z)
    {
        float dx = _x[i] - x, dz = _z[i] - z;
        return dx * dx + dz * dz;
    }

    // ------------------------------------------------------------------ frame

    public void Write(MatchFrame f)
    {
        f.Count = N;
        for (int i = 0; i < N; i++)
        {
            f.X[i] = _x[i];
            f.Y[i] = 0;
            f.Z[i] = _z[i];
            f.Facing[i] = _face[i];
            f.Speed[i] = MathF.Sqrt(_vx[i] * _vx[i] + _vz[i] * _vz[i]);
            f.Team[i] = (byte)TeamOf(i);
            f.Keeper[i] = IsKeeper(i);
            f.Height[i] = _height[i];
            f.Skin[i] = _skin[i];
        }
        f.BallX = _bx; f.BallY = _by; f.BallZ = _bz;
        f.BallVX = _bvx; f.BallVY = _bvy; f.BallVZ = _bvz;
        f.Controlled = _controlled;
        f.Attacking = _lastTeam;
        f.Dir[0] = _dir[0]; f.Dir[1] = _dir[1];
        f.Score[0] = _score[0]; f.Score[1] = _score[1];
        f.Phase = _phase;
        f.Minute = MathF.Min(90, _clock / (HalfSeconds * 2) * 90);
    }
}
