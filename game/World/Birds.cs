using System;
using Godot;
using GameNight.Sim;

namespace GameNight.Grounds;

/// <summary>
/// The ground's birds, by day: a flock of pigeons on the grass, strutting and pecking somewhere
/// quiet, that bursts up when play comes their way (or the crowd erupts), wheels round the bowl
/// and drops back down on the far side of the action; and a few gulls riding the air high over
/// the roofs. They leave when it gets dark or rains. Purely visual (its own Random); one
/// MultiMesh draw (World/Shaders/birds.gdshader flaps the wings).
/// </summary>
public sealed class Birds
{
    const int Pigeons = 14, Gulls = 6, N = Pigeons + Gulls, Stride = 20;
    const float HL = 52.5f, HW = 34f;
    enum Flock { Ground, Flying, Landing, Gone }

    readonly MultiMesh _mm;
    readonly float[] _buf = new float[N * Stride];
    readonly Random _rng = new();
    readonly Vector3[] _pos = new Vector3[N], _vel = new Vector3[N], _off = new Vector3[N];
    readonly Vector3[] _hop = new Vector3[N];
    readonly float[] _yaw = new float[N], _flap = new float[N], _peck = new float[N], _next = new float[N], _bank = new float[N];
    readonly bool[] _down = new bool[N];
    readonly float[] _gullR = new float[Gulls], _gullH = new float[Gulls], _gullW = new float[Gulls], _gullPh = new float[Gulls];
    readonly Vector2[] _gullC = new Vector2[Gulls];
    Flock _flock = Flock.Ground;
    Vector3 _spot;
    float _t, _airUntil, _orbitPh, _orbitR, _orbitH, _orbitW, _gullsOut = 1;

    float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

    public Birds(Node3D root)
    {
        _mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = BirdMesh() };
        _mm.InstanceCount = N;
        root.AddChild(new MultiMeshInstance3D
        {
            Multimesh = _mm,
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/birds.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-150, -60, -150), new Vector3(300, 200, 300)),
        });
        _spot = new Vector3(R(18, 40) * (_rng.Next(2) * 2 - 1), 0, R(16, 28) * (_rng.Next(2) * 2 - 1));
        for (int i = 0; i < N; i++)
        {
            int o = i * Stride;
            bool gull = i >= Pigeons;
            // Pigeons: blue-grey, the odd dark one or white one; gulls white.
            var c = gull ? new Color(0.93f, 0.93f, 0.9f)
                : _rng.Next(8) == 0 ? new Color(0.62f, 0.62f, 0.6f)
                : _rng.Next(4) == 0 ? new Color(0.12f, 0.12f, 0.13f)
                : new Color(0.22f, 0.23f, 0.27f) * R(0.85f, 1.1f);
            _buf[o + 12] = c.R; _buf[o + 13] = c.G; _buf[o + 14] = c.B; _buf[o + 15] = 1;
            _flap[i] = R(0, 10);
            if (gull)
            {
                int g = i - Pigeons;
                _gullC[g] = new Vector2(R(-40, 40), R(-30, 30));
                _gullR[g] = R(18, 42);
                _gullH[g] = R(30, 46);
                _gullW[g] = R(0.12f, 0.22f) * (_rng.Next(2) * 2 - 1);
                _gullPh[g] = R(0, MathF.Tau);
                continue;
            }
            float a = R(0, MathF.Tau), r = MathF.Sqrt(R(0, 1)) * 2.6f;
            _off[i] = new Vector3(MathF.Cos(a) * r, R(-1.5f, 1.5f), MathF.Sin(a) * r);
            _pos[i] = _spot + new Vector3(_off[i].X, 0, _off[i].Z);
            _hop[i] = _pos[i];
            _yaw[i] = R(0, MathF.Tau);
            _down[i] = true;
            _next[i] = R(0, 3);
        }
    }

    /// <summary>Per frame. Day: 0 at night or in the rain (the birds leave), 1 in daylight.</summary>
    public void Update(MatchSnapshot s, float dt, float day)
    {
        if (dt <= 0) return;
        dt = MathF.Min(dt, 1 / 20f);
        _t += dt;
        bool stay = day > 0.5f;
        if (!stay && _t < 1 && _flock != Flock.Gone)
        {
            // A night or wet match: no birds at all.
            _flock = Flock.Gone;
            for (int i = 0; i < Pigeons; i++)
            {
                _down[i] = false;
                _pos[i] = new Vector3(_pos[i].X * 3, 80, _pos[i].Z * 3);
            }
            _gullsOut = 0;
        }
        Pigeon(s, dt, stay);
        _gullsOut += ((stay ? 1 : 0) - _gullsOut) * (1 - MathF.Exp(-dt * 0.3f));
        for (int g = 0; g < Gulls; g++) Gull(g, Pigeons + g, dt);
        _mm.Buffer = _buf;
    }

    // ------------------------------------------------------------------ the pigeons

    /// <summary>How close the game is to the flock: the nearest player, the ball (or a ball
    /// coming over high), and a roar from the stands.</summary>
    bool Threat(MatchSnapshot s)
    {
        float near = 99;
        for (int i = 0; i < MatchSnapshot.N; i++)
            if (s.Active[i]) near = MathF.Min(near, Dist(s.X[i], s.Z[i]));
        float ball = Dist(s.BallX, s.BallZ);
        return near < 9 || ball < 12 || ball < 20 && s.BallY > 2 || s.Excitement > 0.92f || s.Phase == Phase.Goal;
    }

    float Dist(float x, float z) => MathF.Sqrt((x - _spot.X) * (x - _spot.X) + (z - _spot.Z) * (z - _spot.Z));

    /// <summary>Somewhere quiet to come down: on the pitch, away from the ball and the players.</summary>
    Vector3 Quiet(MatchSnapshot s)
    {
        var best = _spot;
        float bestScore = -1;
        for (int k = 0; k < 14; k++)
        {
            var p = new Vector3(R(-46, 46), 0, R(-30, 30));
            float score = MathF.Sqrt((p.X - s.BallX) * (p.X - s.BallX) + (p.Z - s.BallZ) * (p.Z - s.BallZ));
            for (int i = 0; i < MatchSnapshot.N; i++)
                if (s.Active[i]) score = MathF.Min(score, 1.6f * MathF.Sqrt((p.X - s.X[i]) * (p.X - s.X[i]) + (p.Z - s.Z[i]) * (p.Z - s.Z[i])));
            if (score > bestScore) { bestScore = score; best = p; }
        }
        return best;
    }

    void Pigeon(MatchSnapshot s, float dt, bool stay)
    {
        switch (_flock)
        {
            case Flock.Ground:
                if (!stay || Threat(s)) TakeOff(stay);
                break;
            case Flock.Flying:
                if (stay && _t >= _airUntil)
                {
                    _spot = Quiet(s);
                    if (!Threat(s)) _flock = Flock.Landing;
                    else _airUntil = _t + R(4, 8);
                }
                break;
            case Flock.Landing:
                if (!stay || Threat(s)) TakeOff(stay);
                else
                {
                    bool all = true;
                    for (int i = 0; i < Pigeons; i++) all &= _down[i];
                    if (all) _flock = Flock.Ground;
                }
                break;
            case Flock.Gone:
                if (stay)
                {
                    _flock = Flock.Flying;
                    _airUntil = _t + R(10, 20);
                }
                break;
        }
        // The wheel round the bowl, over the stands' fronts.
        _orbitPh += _orbitW * dt;
        var orbit = new Vector3(MathF.Cos(_orbitPh) * _orbitR, _orbitH, MathF.Sin(_orbitPh) * _orbitR * 0.72f);
        if (_flock == Flock.Gone || _flock == Flock.Flying && !stay) orbit = new Vector3(orbit.X * 2.2f, 70, orbit.Z * 2.2f);

        for (int i = 0; i < Pigeons; i++)
        {
            if (_down[i])
            {
                Strut(i, dt);
                continue;
            }
            var target = _flock == Flock.Landing ? _spot + new Vector3(_off[i].X, 0, _off[i].Z) : orbit + _off[i] * 1.6f;
            var d = target - _pos[i];
            bool landing = _flock == Flock.Landing;
            float dl = d.Length();
            // Chasing the wheel loosely, each with its own wobble; settling in to land.
            var wobble = new Vector3(MathF.Sin(_t * 1.7f + i), MathF.Sin(_t * 2.3f + i * 1.3f) * 0.5f, MathF.Cos(_t * 1.9f + i)) * 1.5f * Math.Clamp(dl / 8, 0, 1);
            var acc = d * (landing ? 1.8f : 1.2f) - _vel[i] * (landing ? 2.6f : 1.3f) + wobble;
            _vel[i] += acc * dt;
            float sp = _vel[i].Length(), max = _flock == Flock.Landing ? 7 : 11;
            if (sp > max) _vel[i] *= max / sp;
            _pos[i] += _vel[i] * dt;
            if (_pos[i].Y < 0) _pos[i].Y = 0;
            var flat = new Vector2(_vel[i].X, _vel[i].Z);
            if (flat.LengthSquared() > 0.04f)
            {
                float yaw = MathF.Atan2(_vel[i].X, _vel[i].Z);
                float turn = Wrap(yaw - _yaw[i]);
                _yaw[i] += turn * (1 - MathF.Exp(-dt * 6));
                _bank[i] += (Math.Clamp(-turn * 2.5f, -0.8f, 0.8f) - _bank[i]) * (1 - MathF.Exp(-dt * 4));
            }
            // Flapping hard climbing or braking to land, gliding round the turns.
            bool beat = _vel[i].Y > -0.6f || landing && dl < 6;
            _flap[i] += dt * (beat ? 15 : 3);
            float wing = beat ? MathF.Sin(_flap[i]) * 0.95f : 0.08f + MathF.Sin(_flap[i]) * 0.05f;
            // Touchdown.
            if (landing && dl < 0.6f && _pos[i].Y < 0.5f)
            {
                _down[i] = true;
                _pos[i].Y = 0;
                _vel[i] = Vector3.Zero;
                _bank[i] = 0;
                _hop[i] = _pos[i];
                _next[i] = _t + R(0.5f, 2);
            }
            Put(i, _pos[i], _yaw[i], _bank[i], 1.25f, wing, 1, 0);
        }
    }

    void TakeOff(bool stay)
    {
        _flock = stay ? Flock.Flying : Flock.Gone;
        _airUntil = _t + R(16, 34);
        _orbitR = R(42, 58);
        _orbitH = R(14, 22);
        _orbitW = R(0.18f, 0.26f) * (_rng.Next(2) * 2 - 1);
        _orbitPh = MathF.Atan2(_spot.Z / 0.72f, _spot.X);
        for (int i = 0; i < Pigeons; i++)
        {
            if (!_down[i]) continue;
            // Up in a clatter, each a beat after the next, scattering away from the trouble.
            _down[i] = false;
            var away = new Vector3(_pos[i].X - _spot.X, 0, _pos[i].Z - _spot.Z);
            away = away.LengthSquared() > 0.01f ? away.Normalized() : new Vector3(1, 0, 0);
            _vel[i] = away * R(2, 4) + new Vector3(0, R(4, 6.5f), 0);
            _flap[i] = R(0, 3);
        }
    }

    /// <summary>On the grass: a few quick steps to a new spot, a peck or two, a look round.</summary>
    void Strut(int i, float dt)
    {
        var d = _hop[i] - _pos[i];
        float len = d.Length();
        float bob = 0;
        if (len > 0.02f)
        {
            float step = MathF.Min(len, 0.55f * dt);
            _pos[i] += d / len * step;
            _yaw[i] += Wrap(MathF.Atan2(d.X, d.Z) - _yaw[i]) * (1 - MathF.Exp(-dt * 10));
            // The head goes back and forth with each step.
            bob = MathF.Max(0, MathF.Sin(_t * 18 + i)) * 0.35f;
        }
        else if (_t >= _next[i])
        {
            if (_rng.Next(3) == 0)
            {
                // Pick a new spot nearby, not too far from the others.
                var home = _spot + new Vector3(_off[i].X, 0, _off[i].Z);
                float a = R(0, MathF.Tau), r = R(0.2f, 0.9f);
                _hop[i] = (_pos[i] + home) * 0.5f + new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
            }
            else _peck[i] = _t;
            _next[i] = _t + R(0.6f, 2.5f);
        }
        float pk = _t - _peck[i];
        float peck = pk < 0.5f ? MathF.Abs(MathF.Sin(pk * MathF.Tau * 2)) : bob;
        Put(i, _pos[i], _yaw[i], 0, 1.25f, -0.25f, 0, peck);
    }

    // ------------------------------------------------------------------ the gulls

    void Gull(int g, int i, float dt)
    {
        float out_ = 1 - _gullsOut;
        float ph = _gullPh[g] + _t * _gullW[g];
        float r = _gullR[g] * (1 + out_ * 3);
        var c = _gullC[g];
        var p = new Vector3(c.X + MathF.Cos(ph) * r, _gullH[g] + MathF.Sin(_t * 0.3f + g) * 2 + out_ * 60, c.Y + MathF.Sin(ph) * r);
        float yaw = MathF.Atan2(-MathF.Sin(ph) * _gullW[g], MathF.Cos(ph) * _gullW[g]);
        // Mostly riding the air, a few slow beats now and then.
        _flap[i] += dt * 7;
        float beats = MathF.Sin(_t * 0.4f + g * 1.7f) > 0.75f ? 1 : 0;
        float wing = beats > 0 ? MathF.Sin(_flap[i]) * 0.6f : 0.12f + MathF.Sin(_t * 0.9f + g) * 0.05f;
        float bank = -MathF.Sign(_gullW[g]) * 0.35f;
        if (_gullsOut < 0.02f) p = new Vector3(0, -50, 0);
        Put(i, p, yaw, bank, 2.6f, wing, 1, 0);
    }

    // ------------------------------------------------------------------ out

    static float Wrap(float a) => MathF.Atan2(MathF.Sin(a), MathF.Cos(a));

    void Put(int i, Vector3 p, float yaw, float bank, float scale, float wing, float spread, float peck)
    {
        var basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Back, bank);
        basis = basis.Scaled(Vector3.One * scale);
        int o = i * Stride;
        Vector3 x = basis.Column0, y = basis.Column1, z = basis.Column2;
        _buf[o] = x.X; _buf[o + 1] = y.X; _buf[o + 2] = z.X; _buf[o + 3] = p.X;
        _buf[o + 4] = x.Y; _buf[o + 5] = y.Y; _buf[o + 6] = z.Y; _buf[o + 7] = p.Y;
        _buf[o + 8] = x.Z; _buf[o + 9] = y.Z; _buf[o + 10] = z.Z; _buf[o + 11] = p.Z;
        _buf[o + 16] = wing; _buf[o + 17] = spread; _buf[o + 18] = peck; _buf[o + 19] = 0;
    }

    /// <summary>A pigeon, looking down +z: a body, a head (UV.y = 1), a fanned tail and two wings
    /// (UV.x = 0 left, 1 right, 0.5 the rest; birds.gdshader beats and folds them), the wing
    /// tips darker.</summary>
    static ArrayMesh BirdMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void V(Vector3 p, float side, float head, float shade)
        {
            st.SetColor(new Color(shade, shade, shade));
            st.SetUV(new Vector2(side, head));
            st.SetNormal(Vector3.Up);
            st.AddVertex(p);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float side, float head, float shade)
        {
            V(a, side, head, shade); V(b, side, head, shade); V(c, side, head, shade);
            V(a, side, head, shade); V(c, side, head, shade); V(d, side, head, shade);
        }
        void Box(Vector3 a, Vector3 b, float head, float shade)
        {
            Vector3 P(int i) => new((i & 1) == 0 ? a.X : b.X, (i & 2) == 0 ? a.Y : b.Y, (i & 4) == 0 ? a.Z : b.Z);
            int[][] faces = { new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 } };
            foreach (var f in faces) Quad(P(f[0]), P(f[1]), P(f[2]), P(f[3]), 0.5f, head, shade * (f[0] == 2 ? 1.1f : 1));
        }
        Box(new Vector3(-0.05f, 0.06f, -0.12f), new Vector3(0.05f, 0.17f, 0.1f), 0, 1);
        Box(new Vector3(-0.035f, 0.15f, 0.08f), new Vector3(0.035f, 0.23f, 0.15f), 1, 0.8f);
        Quad(new Vector3(-0.03f, 0.12f, -0.12f), new Vector3(0.03f, 0.12f, -0.12f), new Vector3(0.05f, 0.1f, -0.22f), new Vector3(-0.05f, 0.1f, -0.22f), 0.5f, 0, 0.6f);
        foreach (int s in new[] { -1, 1 })
        {
            float side = s < 0 ? 0 : 1;
            Quad(new Vector3(s * 0.04f, 0.155f, 0.07f), new Vector3(s * 0.2f, 0.155f, 0.03f), new Vector3(s * 0.2f, 0.155f, -0.09f), new Vector3(s * 0.04f, 0.155f, -0.07f), side, 0, 0.9f);
            Quad(new Vector3(s * 0.2f, 0.155f, 0.03f), new Vector3(s * 0.32f, 0.155f, -0.02f), new Vector3(s * 0.32f, 0.155f, -0.1f), new Vector3(s * 0.2f, 0.155f, -0.09f), side, 0, 0.4f);
        }
        return st.Commit();
    }
}
