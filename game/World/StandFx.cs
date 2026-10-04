using System;
using System.Collections.Generic;
using Godot;
using GameNight.Audio;

namespace GameNight.Grounds;

/// <summary>
/// What the stands throw into the air (the stadium half of the PWA's particles.ts): flares
/// that spit sparks and pour out smoke, smoke bombs billowing in the club's colour, ticker
/// tape and confetti that flutter out over the goalmouth and lie on the grass for a while.
/// The terraces director (Main.Sound.Terraces) decides what burns where; a goal brings the
/// confetti down from the main stand. Simulated here on the game thread, drawn as one
/// MultiMesh (World/Shaders/fx.gdshader).
/// </summary>
public sealed class StandFx
{
    const int Max = 2048, Stride = 20, MaxFlares = 8;
    enum Kind : byte { Spark, Smoke, Confetti, Landed, Glow }

    readonly float[] _px = new float[Max], _py = new float[Max], _pz = new float[Max];
    readonly float[] _vx = new float[Max], _vy = new float[Max], _vz = new float[Max];
    readonly float[] _life = new float[Max], _maxLife = new float[Max], _size = new float[Max], _seed = new float[Max];
    readonly Kind[] _kind = new Kind[Max];
    readonly Color[] _col = new Color[Max];
    int _n;
    readonly float[] _buf = new float[Max * Stride];
    readonly MultiMesh _mm;
    readonly Color _home, _away;
    readonly Vector4[] _flares = new Vector4[MaxFlares];
    readonly Random _rng = new(5);
    /// <summary>A gentle breeze carrying the smoke and confetti.</summary>
    static readonly Vector2 Wind = new(0.35f, 0.25f);
    const float HL = 52.5f, HW = 34f;

    float R() => (float)_rng.NextDouble();

    public StandFx(Node3D root, uint home, uint away)
    {
        _home = MeshData.Srgb(home);
        _away = MeshData.Srgb(away);
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
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/fx.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-200, -5, -200), new Vector3(400, 90, 400)),
        });
    }

    void Spawn(Kind k, float x, float y, float z, float vx, float vy, float vz, float life, float size, Color c)
    {
        if (_n >= Max) return;
        int i = _n++;
        (_px[i], _py[i], _pz[i], _vx[i], _vy[i], _vz[i]) = (x, y, z, vx, vy, vz);
        (_life[i], _maxLife[i], _size[i], _seed[i], _kind[i], _col[i]) = (life, life, size, R(), k, c);
    }

    static Color Lin(uint hex) => MeshData.Srgb(hex);

    /// <summary>A smoke bomb's colour: the club's, a touch lighter (smoke is never as deep as the
    /// shirt) and varied a little puff to puff.</summary>
    Color Tint(Color c) => c.Lerp(Colors.White, 0.12f + R() * 0.1f) * (0.92f + R() * 0.12f);

    Color ConfettiColour(Color team)
    {
        float r = R();
        return r < 0.45f ? team : r < 0.78f ? Lin(0xf6f1e3) : r < 0.9f ? Lin(0xffd447) : Lin(0x9fd0ff);
    }

    /// <summary>Ticker tape and confetti thrown from an end (0 home, behind the left goal).</summary>
    public void ThrowConfetti(int end, float amount)
    {
        float s = end == 0 ? -1 : 1;
        var team = end == 0 ? _home : _away;
        for (int k = 0; k < amount; k++)
            Spawn(Kind.Confetti, s * (HL + 6 + R() * 14), 5 + R() * 8, (R() - 0.5f) * 46, -s * (1.5f + R() * 4), -0.5f, (R() - 0.5f) * 1.6f, 16, 0.09f + R() * 0.05f, ConfettiColour(team));
    }

    /// <summary>A goal: confetti and ticker tape from the main stand, over the scorers' half.</summary>
    public void Goal(int team, float cx)
    {
        var col = team == 0 ? _home : _away;
        for (int k = 0; k < 420; k++)
            Spawn(Kind.Confetti, cx + (R() - 0.5f) * 90, 6 + R() * 12, -(HW + 8 + R() * 14), (R() - 0.3f) * 1.5f, -0.4f - R() * 0.8f, 1.5f + R() * 2.5f, 4 + R() * 2.5f, 0.09f + R() * 0.05f, ConfettiColour(col));
    }

    /// <summary>Per frame: light what the terraces light, move everything, upload. Returns the
    /// burning flares (position, strength) for the crowd's light, and how many.</summary>
    public (Vector4[] flares, int count) Update(float dt, double time, Terraces t)
    {
        int nf = 0;
        if (t != null)
        {
            foreach (var (end, amount) in t.Confetti) ThrowConfetti(end, amount);
            t.Confetti.Clear();
            foreach (var f in t.Pyro)
            {
                float age = (float)(t.T - f.Born);
                if (age < 0) continue;
                float fade = Mathf.Min(1, Mathf.Min(age / 0.6f, (float)(f.Life - age) / 1.5f));
                if (fade <= 0) continue;
                if (f.Smoke)
                {
                    // A smoke bomb billows in the club's colour.
                    // A smoke bomb pours out a thick plume in the club's colour: puffs burst out
                    // low and wide, then rise and roll away on the breeze.
                    if (R() < 9f * dt * fade)
                        Spawn(Kind.Smoke, f.X + (R() - 0.5f) * 0.8f, f.Y - 0.3f, f.Z + (R() - 0.5f) * 0.8f, Wind.X * 0.6f + (R() - 0.5f) * 2.2f, 0.7f + R() * 0.9f, Wind.Y * 0.6f + (R() - 0.5f) * 2.2f, 8 + R() * 4, 1.8f + R() * 0.8f, Tint(f.End == 0 ? _home : _away));
                    continue;
                }
                if (dt > 0 && R() < 0.6f * fade)
                    Spawn(Kind.Spark, f.X + (R() - 0.5f) * 0.35f, f.Y + R() * 0.2f, f.Z + (R() - 0.5f) * 0.35f, (R() - 0.5f) * 1.6f, 0.6f + R() * 1.6f, (R() - 0.5f) * 1.6f, 0.18f + R() * 0.15f, 0.2f + R() * 0.25f, R() < 0.4f ? Lin(0xffd9a0) : Lin(0xff4a2a));
                // A flare's own smoke: a pinkish-grey column off the fire.
                if (R() < 4f * dt * fade)
                    Spawn(Kind.Smoke, f.X, f.Y + 0.4f, f.Z, Wind.X * 0.8f + (R() - 0.5f) * 0.6f, 0.8f + R() * 0.6f, Wind.Y * 0.8f + (R() - 0.5f) * 0.6f, 7 + R() * 3, 1.1f + R() * 0.5f, R() < 0.5f ? Lin(0xe8b8b0) : Lin(0xcdbdbb));
                if (nf < MaxFlares)
                {
                    float flick = 0.75f + 0.25f * Mathf.Sin((float)time * 31 + f.Seed * 40) * Mathf.Sin((float)time * 17.3f + f.Seed * 13);
                    _flares[nf++] = new Vector4(f.X, f.Y, f.Z, fade * flick);
                }
            }
        }

        // Move and age; the dead are swapped out from the end.
        for (int i = 0; i < _n; i++)
        {
            _life[i] -= dt;
            if (_life[i] <= 0) { Kill(i--); continue; }
            float vx = _vx[i], vy = _vy[i], vz = _vz[i];
            switch (_kind[i])
            {
                case Kind.Spark:
                    vy -= 4 * dt;
                    break;
                case Kind.Smoke:
                    vx += (Wind.X * 1.2f - vx) * dt * 0.4f;
                    vz += (Wind.Y * 1.2f - vz) * dt * 0.4f;
                    vy *= 1 - dt * 0.2f;
                    break;
                case Kind.Confetti:
                    vx += (Wind.X * 0.6f - vx) * dt * 0.8f + Mathf.Sin((float)time * 5 + i) * 1.2f * dt;
                    vz += (Wind.Y * 0.6f - vz) * dt * 0.3f;
                    vy += (-1.25f - vy) * dt * 2;
                    if (_py[i] < 0.05f)
                    {
                        // On the grass: it lies there half a minute or so.
                        if (Mathf.Abs(_px[i]) < HL + 4 && Mathf.Abs(_pz[i]) < HW + 4)
                        {
                            _kind[i] = Kind.Landed;
                            _py[i] = 0.03f;
                            _life[i] = _maxLife[i] = 30 + R() * 30;
                            _size[i] *= 0.8f;
                            _vx[i] = _vy[i] = _vz[i] = 0;
                        }
                        else Kill(i--);
                        continue;
                    }
                    break;
                case Kind.Landed:
                    continue;
            }
            (_vx[i], _vy[i], _vz[i]) = (vx, vy, vz);
            _px[i] += vx * dt; _py[i] += vy * dt; _pz[i] += vz * dt;
        }

        // Upload: the particles, then a glow card on each burning flare.
        int n = 0;
        for (int i = 0; i < _n; i++)
        {
            float u = 1 - _life[i] / _maxLife[i];
            float size = _size[i], alpha;
            switch (_kind[i])
            {
                case Kind.Spark: alpha = 0.95f * (1 - u); size *= 0.8f + R() * 0.4f; break;
                case Kind.Smoke:
                    // Thick from the start, thinning over its last half; it swells as it rises.
                    alpha = Mathf.SmoothStep(0, 0.06f, u) * (1 - Mathf.SmoothStep(0.45f, 1, u)) * 1.05f;
                    size *= 1 + u * 3.2f;
                    Write(n++, _px[i], _py[i], _pz[i], _col[i], _kind[i], size, alpha, _seed[i], u);
                    continue;
                case Kind.Confetti: alpha = Mathf.Min(1, (1 - u) * 3); break;
                default: alpha = Mathf.Min(0.95f, _life[i] / 6); break;
            }
            Write(n++, _px[i], _py[i], _pz[i], _col[i], _kind[i], size, alpha, _seed[i], (float)time * 9 + i);
        }
        for (int k = 0; k < nf && n < Max; k++)
            Write(n++, _flares[k].X, _flares[k].Y, _flares[k].Z, Lin(0xff5a3a), Kind.Glow, 1.2f + 1.4f * _flares[k].W, _flares[k].W, k * 0.37f, 0);
        if (n > 0 || _mm.VisibleInstanceCount > 0)
        {
            if (n > 0) _mm.Buffer = _buf;
            _mm.VisibleInstanceCount = n;
        }
        return (_flares, nf);
    }

    void Kill(int i)
    {
        int j = --_n;
        if (i == j) return;
        (_px[i], _py[i], _pz[i], _vx[i], _vy[i], _vz[i]) = (_px[j], _py[j], _pz[j], _vx[j], _vy[j], _vz[j]);
        (_life[i], _maxLife[i], _size[i], _seed[i], _kind[i], _col[i]) = (_life[j], _maxLife[j], _size[j], _seed[j], _kind[j], _col[j]);
    }

    void Write(int i, float x, float y, float z, Color c, Kind k, float size, float alpha, float seed, float spin)
    {
        int o = i * Stride;
        var b = _buf;
        b[o] = 1; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = x;
        b[o + 4] = 0; b[o + 5] = 1; b[o + 6] = 0; b[o + 7] = y;
        b[o + 8] = 0; b[o + 9] = 0; b[o + 10] = 1; b[o + 11] = z;
        b[o + 12] = c.R; b[o + 13] = c.G; b[o + 14] = c.B; b[o + 15] = (float)k;
        b[o + 16] = size; b[o + 17] = alpha; b[o + 18] = seed; b[o + 19] = spin;
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
