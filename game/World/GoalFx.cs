using System;
using Godot;

namespace GameNight.Grounds;

/// <summary>
/// Goal explosions, Rocket League style: the moment the ball crosses the line the net goes up
/// in the scoring club's chosen explosion (GoalFx.Styles.cs has the ten). Pieces are simulated
/// here on the game thread and drawn as one MultiMesh (World/Shaders/goalfx.gdshader), nothing
/// blended. Purely presentational: Main fires it from the goal event and again on the replay.
/// </summary>
public sealed partial class GoalFx
{
    const int Max = 2048, Stride = 20;
    const float HL = 52.5f, GoalHW = 3.66f, GoalH = 2.44f;

    enum K : byte { Streak, Puff, Glow, Ring, Chip, Beam, Orb }
    enum Move : byte { Fly, Flutter, Seek, Suck, Rest }
    enum Shape : byte { Square, Shard, Voxel, Coin, Star, Rock, Flake, Balloon, Spike }

    /// <summary>One piece. A and B are the ring's axes, the beam's reach, or a seeker's target
    /// (A) and its own data (B), depending on the kind.</summary>
    struct P
    {
        public Vector3 Pos, Vel, A, B;
        public float Life, Max, Delay, S0, S1, Grow, Grav, Drag, Seed, Spin, SpinV, Alpha, Fade, Thick, Flick, Streak;
        public Color C0, C1, Hot;
        public K Kind;
        public Shape Shape;
        public Move Move;
        public byte Tag;
        public bool Bounce, Lit, Smoke, Splat, Pop;
    }

    readonly P[] _p = new P[Max];
    P _spare;
    int _n;
    readonly float[] _buf = new float[Max * Stride];
    readonly MultiMesh _mm;
    readonly Random _rng = new(11);

    // The explosion going off.
    int _style = -1;
    float _t, _t0;
    /// <summary>Where it goes off (in the goal, at the ball), the goal's end (+1 / -1), the ground under it.</summary>
    Vector3 _c, _g;
    float _side;
    Color _team, _trim;
    Vector3 _eye = new(0, 20, -60);
    float _flash;
    bool _flashing;

    /// <summary>How hard to shake the camera this frame (taken by Main).</summary>
    public float Shake;
    /// <summary>A sound cue: (style, cue). Cue 0 is the blast itself; the styles add their own.</summary>
    public Action<int, int> Sound;
    /// <summary>Whether to light the whole stadium for the big moments (off in the menu preview).</summary>
    public bool Lightning = true;
    public bool Active => _style >= 0 || _n > 0;

    float R() => (float)_rng.NextDouble();
    float R(float a, float b) => a + (b - a) * R();
    Vector3 Dir()
    {
        float z = R() * 2 - 1, an = R() * Mathf.Tau, s = MathF.Sqrt(1 - z * z);
        return new Vector3(s * MathF.Cos(an), z, s * MathF.Sin(an));
    }

    public GoalFx(Node3D root)
    {
        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = Card(),
            InstanceCount = Max,
            VisibleInstanceCount = 0,
        };
        root.AddChild(new MultiMeshInstance3D
        {
            Multimesh = _mm,
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/goalfx.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-200, -5, -200), new Vector3(400, 120, 400)),
        });
    }

    /// <summary>Set one off: this style, for the ball at (x, y, z) in the net, in the scorers'
    /// shirt colours (sRGB hex).</summary>
    public void Fire(int style, float x, float y, float z, int shirt, int trim)
    {
        Clear();
        _style = Math.Clamp(style, 0, Count - 1);
        _side = x >= 0 ? 1 : -1;
        _c = new Vector3(_side * (HL + 0.9f), Math.Clamp(y, 0.6f, 1.8f), Math.Clamp(z, -GoalHW + 0.6f, GoalHW - 0.6f));
        _g = new Vector3(_c.X, 0, _c.Z);
        _team = Vivid(MeshData.Srgb((uint)shirt));
        _trim = Vivid(MeshData.Srgb((uint)trim));
        if (_team.R + _team.G + _team.B > 2.4f && _trim.R + _trim.G + _trim.B > 2.4f) _trim = MeshData.Srgb(0xffd447);
        // Just before zero, so the moments at 0 go off on the first frame.
        _t = _t0 = -1e-4f;
        Sound?.Invoke(_style, 0);
    }

    /// <summary>Everything gone (a new match, the replay rewinding or ending).</summary>
    public void Clear()
    {
        _n = 0;
        _style = -1;
        foreach (var r in _rockets) r.Live = false;
        if (_mm.VisibleInstanceCount > 0) _mm.VisibleInstanceCount = 0;
        EndFlash();
    }

    /// <summary>A club colour bright enough to burn: dark shirts are lifted toward their own hue.</summary>
    static Color Vivid(Color c)
    {
        float m = Mathf.Max(c.R, Mathf.Max(c.G, c.B));
        if (m < 0.001f) return new Color(0.9f, 0.9f, 0.95f);
        var hue = new Color(c.R / m, c.G / m, c.B / m);
        float lum = 0.3f * c.R + 0.55f * c.G + 0.15f * c.B;
        return lum < 0.25f ? hue.Lerp(Colors.White, 0.15f) : c;
    }

    static Color Lin(uint hex) => MeshData.Srgb(hex);
    static Color Hsv(float h, float s = 0.8f, float v = 1) => Color.FromHsv(h - MathF.Floor(h), s, v).SrgbToLinear();

    /// <summary>The camera's right and up as seen from the explosion (for things that face it).</summary>
    (Vector3 right, Vector3 up) Facing(Vector3 at)
    {
        var f = _eye - at;
        f.Y = 0;
        if (f.LengthSquared() < 1e-4f) f = new Vector3(0, 0, -1);
        f = f.Normalized();
        return (new Vector3(f.Z, 0, -f.X), Vector3.Up);
    }

    /// <summary>The scripted moment this frame crosses.</summary>
    bool At(float t) => _t0 < t && t <= _t;

    ref P Add(K kind, Vector3 pos, Vector3 vel, float life)
    {
        if (_n >= Max)
        {
            _spare = default;
            return ref _spare;
        }
        ref var p = ref _p[_n++];
        p = default;
        p.Kind = kind;
        p.Pos = pos;
        p.Vel = vel;
        p.Life = p.Max = life;
        p.S0 = p.S1 = 0.3f;
        p.Alpha = 1;
        p.Fade = 0.6f;
        p.Seed = R();
        p.Spin = R() * Mathf.Tau;
        p.C0 = p.C1 = p.Hot = Colors.White;
        p.Thick = 0.15f;
        p.Streak = 0.045f;
        return ref p;
    }

    // ------------------------------------------------------------------ building blocks

    /// <summary>A flash: a hot core in a halo that swells and fades.</summary>
    void Flash(Vector3 at, float size, Color halo, Color core, float life = 0.4f)
    {
        ref var p = ref Add(K.Glow, at, Vector3.Zero, life);
        p.S0 = size * 0.35f;
        p.S1 = size;
        p.C0 = halo;
        p.C1 = halo;
        p.Hot = core;
        p.Fade = 0.25f;
    }

    /// <summary>A ring racing out flat over the grass (or facing the camera when flat is false).</summary>
    ref P Shock(Vector3 at, float radius, float life, Color c0, Color c1, Color hot, float thick = 0.14f, bool flat = true, int shape = 0)
    {
        ref var p = ref Add(K.Ring, at, Vector3.Zero, life);
        p.S0 = radius * 0.04f;
        p.S1 = radius;
        p.C0 = c0;
        p.C1 = c1;
        p.Hot = hot;
        p.Thick = thick;
        p.Fade = 0.35f;
        p.Seed = shape;
        if (flat)
        {
            p.A = Vector3.Right;
            p.B = Vector3.Back;
        }
        return ref p;
    }

    /// <summary>A soft disc on the grass: the blast's light, a frost patch, a scorch.</summary>
    ref P Pool(Vector3 at, float r0, float r1, float life, Color c, Color hot, float alpha = 1)
    {
        ref var p = ref Shock(at with { Y = 0.04f }, r1, life, c, c, hot, 1, true, 1);
        p.S0 = r0;
        p.Alpha = alpha;
        p.Fade = 0.5f;
        return ref p;
    }

    /// <summary>Sparks out in every direction (up-weighted by lift).</summary>
    void Sparks(Vector3 at, int n, float v0, float v1, Color c0, Color c1, Color hot, float life = 1, float lift = 0.3f, float grav = 8, float drag = 1.4f, float size = 0.22f)
    {
        for (int i = 0; i < n; i++)
        {
            var d = Dir();
            d.Y = d.Y * (1 - lift) + lift * MathF.Abs(d.Y) + lift * 0.5f;
            ref var p = ref Add(K.Streak, at + d * 0.3f, d.Normalized() * R(v0, v1), life * R(0.6f, 1.2f));
            p.C0 = c0;
            p.C1 = c1;
            p.Hot = hot;
            p.Grav = grav;
            p.Drag = drag;
            p.S0 = p.S1 = size * R(0.7f, 1.3f);
            p.Fade = 0.5f;
        }
    }

    /// <summary>Smoke: lit by the sky, rising and spreading.</summary>
    void Smoke(Vector3 at, int n, float spread, Color c, float size = 1.6f, float life = 3, float rise = 1.5f)
    {
        for (int i = 0; i < n; i++)
        {
            var d = Dir();
            d.Y = MathF.Abs(d.Y) * 0.4f;
            ref var p = ref Add(K.Puff, at + d * R(0, spread * 0.4f), d * spread * R(0.4f, 1) + Vector3.Up * rise * R(0.6f, 1.3f), life * R(0.75f, 1.2f));
            p.C0 = p.C1 = c * R(0.85f, 1.1f);
            p.Smoke = true;
            p.Drag = 1.2f;
            p.Grav = -0.4f;
            p.S0 = size * R(0.6f, 0.9f);
            p.S1 = size * R(2.2f, 3.2f);
            p.Fade = 0.35f;
            p.Alpha = 1;
        }
    }

    /// <summary>Little solid things flung out: shards, voxels, rocks, coins.</summary>
    ref P Chip(Shape shape, Vector3 at, Vector3 vel, float life, float size, Color c, Color hot)
    {
        ref var p = ref Add(K.Chip, at, vel, life);
        p.Shape = shape;
        p.S0 = p.S1 = size;
        p.C0 = p.C1 = c;
        p.Hot = hot;
        p.Grav = 9.8f;
        p.Drag = 0.3f;
        p.SpinV = R(-9, 9);
        p.Fade = 0.75f;
        return ref p;
    }

    /// <summary>A beam from a to b.</summary>
    ref P Beam(Vector3 a, Vector3 b, float width, float life, Color c, Color hot)
    {
        ref var p = ref Add(K.Beam, a, Vector3.Zero, life);
        p.A = b - a;
        p.S0 = p.S1 = width;
        p.C0 = p.C1 = c;
        p.Hot = hot;
        p.Fade = 0.5f;
        p.Move = Move.Rest;
        return ref p;
    }

    /// <summary>A jagged bolt from a to b (midpoint displacement), with forks.</summary>
    void Bolt(Vector3 a, Vector3 b, float width, float life, Color c, Color hot, int depth, float jag, int forks)
    {
        Span<Vector3> pts = stackalloc Vector3[65];
        int n = 1 << depth;
        pts[0] = a;
        pts[n] = b;
        for (int step = n; step > 1; step /= 2)
            for (int i = 0; i < n; i += step)
            {
                var p0 = pts[i];
                var p1 = pts[i + step];
                float len = (p1 - p0).Length();
                pts[i + step / 2] = (p0 + p1) * 0.5f + new Vector3(R(-1, 1), R(-0.4f, 0.4f), R(-1, 1)) * len * jag;
            }
        for (int i = 0; i < n; i++)
        {
            ref var s = ref Beam(pts[i], pts[i + 1], width, life, c, hot);
            s.Flick = 0.3f;
        }
        for (int f = 0; f < forks; f++)
        {
            int at = 1 + (int)(R() * (n - 2));
            var from = pts[at];
            var to = from + new Vector3(R(-1, 1) * 5, -R(3, 8), R(-1, 1) * 5);
            if (to.Y < 0) to.Y = 0;
            Bolt(from, to, width * 0.55f, life * 0.8f, c, hot, Math.Max(2, depth - 2), jag, 0);
        }
    }

    void Lights(float amount)
    {
        if (!Lightning) return;
        _flash = MathF.Max(_flash, amount);
    }

    void EndFlash()
    {
        _flash = 0;
        if (!_flashing) return;
        _flashing = false;
        RenderingServer.GlobalShaderParameterSet("gn_flash", 0f);
    }

    // ------------------------------------------------------------------ per frame

    /// <summary>Per frame: run the explosion's script, move everything, upload. `eye` is the
    /// camera (things that face it, like letters and wings, turn with it).</summary>
    public void Update(float dt, double time, Vector3 eye)
    {
        _eye = eye;
        if (dt > 0.1f) dt = 0.1f;
        if (_style >= 0)
        {
            _t0 = _t;
            _t += dt;
            Script();
            if (_t > 7) _style = -1;
        }
        Rockets(dt);
        if (!Active)
        {
            EndFlash();
            return;
        }

        for (int i = 0; i < _n; i++)
        {
            ref var p = ref _p[i];
            if (p.Delay > 0)
            {
                p.Delay -= dt;
                continue;
            }
            p.Life -= dt;
            if (p.Life <= 0)
            {
                if (p.Pop) Popped(p);
                Kill(i--);
                continue;
            }
            p.Spin += p.SpinV * dt;
            switch (p.Move)
            {
                case Move.Fly:
                    p.Vel.Y -= p.Grav * dt;
                    p.Vel *= MathF.Max(0, 1 - p.Drag * dt);
                    p.Pos += p.Vel * dt;
                    if (p.Pos.Y < 0.02f && p.Vel.Y < 0)
                    {
                        if (p.Splat)
                        {
                            Splatted(p);
                            Kill(i--);
                            continue;
                        }
                        if (p.Bounce)
                        {
                            p.Pos.Y = 0.02f;
                            p.Vel = new Vector3(p.Vel.X * 0.65f, -p.Vel.Y * 0.42f, p.Vel.Z * 0.65f);
                            p.SpinV *= 0.6f;
                            if (p.Vel.Y < 0.6f) { p.Move = Move.Rest; p.Pos.Y = p.S1 * 0.3f; }
                        }
                    }
                    break;
                case Move.Flutter:
                    p.Vel.X += (0.35f - p.Vel.X) * dt * 1.3f + MathF.Sin((float)time * 5 + p.Seed * 40) * 1.4f * dt;
                    p.Vel.Z += (0.25f - p.Vel.Z) * dt * 1.3f;
                    p.Vel.Y += (-p.Grav - p.Vel.Y) * dt * 1.8f;
                    p.Pos += p.Vel * dt;
                    if (p.Pos.Y < 0.04f)
                    {
                        // On the grass it lies there a while.
                        p.Pos.Y = 0.04f;
                        p.Move = Move.Rest;
                        p.SpinV = 0;
                        p.Life = MathF.Min(p.Life, R(3, 6));
                        p.Max = MathF.Max(p.Life, 0.01f) / 0.25f;
                    }
                    break;
                case Move.Seek:
                {
                    var d = p.A - p.Pos;
                    p.Vel = p.Vel * MathF.Exp(-p.Drag * dt) + d * p.Grav * dt;
                    p.Pos += p.Vel * dt;
                    break;
                }
                case Move.Suck:
                {
                    var d = p.A - p.Pos;
                    float l = d.Length();
                    if (l < 0.45f)
                    {
                        Kill(i--);
                        continue;
                    }
                    p.Vel += d / l * (p.Grav / MathF.Max(1.2f, l)) * dt;
                    p.Vel *= MathF.Max(0, 1 - p.Drag * dt);
                    p.Pos += p.Vel * dt;
                    break;
                }
            }
        }

        UpdateFlash(dt);
        Upload();
    }

    void UpdateFlash(float dt)
    {
        if (_flash <= 0.001f)
        {
            if (_flashing) EndFlash();
            return;
        }
        _flashing = true;
        RenderingServer.GlobalShaderParameterSet("gn_flash", _flash * 0.45f);
        _flash *= MathF.Exp(-dt * 9);
    }

    void Upload()
    {
        int n = 0;
        for (int i = 0; i < _n; i++)
        {
            ref var p = ref _p[i];
            if (p.Delay > 0) continue;
            float age = p.Max - p.Life, u = Mathf.Clamp(age / p.Max, 0, 1);
            float g = p.Grow > 0 ? Mathf.Min(1, age / p.Grow) : u;
            g = 1 - (1 - g) * (1 - g);
            float size = p.S0 + (p.S1 - p.S0) * g;
            float alpha = p.Alpha * (u < p.Fade ? 1 : 1 - (u - p.Fade) / (1 - p.Fade));
            if (p.Flick > 0 && R() < p.Flick) alpha *= 0.15f;
            var c = p.C0.Lerp(p.C1, u);
            Vector3 a = Vector3.Zero, b = Vector3.Zero;
            float w = 0;
            switch (p.Kind)
            {
                case K.Streak:
                    a = -p.Vel * p.Streak;
                    if (a.LengthSquared() < 0.0004f) a = new Vector3(0, 0.02f, 0);
                    break;
                case K.Puff:
                    b.X = p.Smoke ? 1 : 0;
                    w = u;
                    break;
                case K.Ring:
                    if (p.A == Vector3.Zero) size *= 2;
                    a = p.A * size;
                    b = p.B * size;
                    w = Mathf.Clamp(p.Thick, 0.02f, 1) + 2 * p.Seed;
                    break;
                case K.Chip:
                    b.X = (int)p.Shape + (p.Lit ? 16 : 0);
                    w = p.Spin;
                    break;
                case K.Beam:
                    a = p.A;
                    break;
                case K.Orb:
                    w = age;
                    break;
            }
            float seed = p.Kind == K.Ring ? (i * 0.137f) % 1 : p.Seed;
            int o = n++ * Stride;
            var buf = _buf;
            buf[o] = a.X; buf[o + 1] = p.Hot.R; buf[o + 2] = b.X; buf[o + 3] = p.Pos.X;
            buf[o + 4] = a.Y; buf[o + 5] = p.Hot.G; buf[o + 6] = b.Y; buf[o + 7] = p.Pos.Y;
            buf[o + 8] = a.Z; buf[o + 9] = p.Hot.B; buf[o + 10] = b.Z; buf[o + 11] = p.Pos.Z;
            buf[o + 12] = c.R; buf[o + 13] = c.G; buf[o + 14] = c.B; buf[o + 15] = (float)p.Kind;
            buf[o + 16] = size; buf[o + 17] = alpha; buf[o + 18] = seed; buf[o + 19] = w;
        }
        if (n > 0) _mm.Buffer = _buf;
        _mm.VisibleInstanceCount = n;
    }

    void Kill(int i)
    {
        int j = --_n;
        if (i != j) _p[i] = _p[j];
    }

    /// <summary>One card, corners at (±1, ±1).</summary>
    static ArrayMesh Card()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0) };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 2, 1, 1, 2, 3 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
