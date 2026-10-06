using System;
using Godot;

namespace GameNight.Grounds;

/// <summary>The ten goal explosions, each a short script of moments on GoalFx's building blocks.</summary>
public sealed partial class GoalFx
{
    public const int Count = 14;

    public static readonly string[] Names =
    {
        "SUPERNOVA", "FIREWORKS", "THUNDERSTRIKE", "VOLCANO", "BLACK HOLE",
        "FROSTBITE", "PARTY CANNON", "ARCADE", "PHOENIX", "METEOR",
        "TORNADO", "DISCO", "RAINBOW", "HAUNTED",
    };

    public static readonly string[] Blurbs =
    {
        "White-hot blast and a spark storm",
        "Rockets bursting in your colours",
        "Lightning strikes the goal",
        "The goalmouth erupts in lava",
        "Pulled into the dark, then boom",
        "Ice spikes burst, then shatter",
        "Confetti cannons and balloons",
        "Pixels, coins and a giant GOAL!",
        "A firebird rises from the net",
        "A burning rock falls from the sky",
        "A twister rips up the turf",
        "A mirror ball lights the pitch",
        "A rainbow, and a pot of gold",
        "Ghosts, bats and green fire",
    };

    /// <summary>A colour to show each one by in the menus (sRGB hex).</summary>
    public static readonly int[] Swatches =
    {
        0xffb347, 0xff4fa3, 0x6fcaff, 0xff5a10, 0x9a4dff,
        0x9fe3ff, 0xffd447, 0x3ddc84, 0xff7a1a, 0xc08a5a,
        0xa89a80, 0xff5fd0, 0x5fd0ff, 0x7dff6a,
    };

    static readonly Color White = new(1, 1, 1), HotWhite = new(1.1f, 1.05f, 0.92f);
    static readonly Color Gold = Lin(0xffd447), Orange = Lin(0xff8a1f), Red = Lin(0xd8261a), Deep = Lin(0x4a0804);
    static readonly Color FireCore = new(1.35f, 1.1f, 0.6f);

    float Dt => _t - _t0;

    /// <summary>How many to spawn this frame at `rate` a second (the remainder by chance).</summary>
    int Rate(float rate)
    {
        float n = rate * Dt;
        int k = (int)n;
        return k + (R() < n - k ? 1 : 0);
    }

    void Script()
    {
        switch (_style)
        {
            case 0: Supernova(); break;
            case 1: Fireworks(); break;
            case 2: Thunderstrike(); break;
            case 3: Volcano(); break;
            case 4: BlackHole(); break;
            case 5: Frostbite(); break;
            case 6: Party(); break;
            case 7: Arcade(); break;
            case 8: Phoenix(); break;
            case 9: Meteor(); break;
            case 10: Tornado(); break;
            case 11: Disco(); break;
            case 12: Rainbow(); break;
            default: Haunted(); break;
        }
    }

    /// <summary>A piece flying into the ground (Splat set).</summary>
    void Splatted(in P p)
    {
        if (p.Tag == 1)
        {
            // A lava bomb lands: a glowing splash that cools to a black crust.
            ref var pool = ref Pool(p.Pos, 0.2f, R(0.7f, 1.2f), R(2.2f, 3), Lin(0xff6a10), new Color(1.3f, 0.9f, 0.4f), 0.95f);
            pool.C1 = Lin(0x2a0d06);
            pool.Grow = 0.15f;
            Sparks(p.Pos with { Y = 0.1f }, 6, 2, 5, Gold, Red, White, 0.5f, 0.7f, 9, 1, 0.14f);
            Smoke(p.Pos, 1, 0.5f, Lin(0x3a3330), 0.8f, 1.6f, 1.2f);
        }
    }

    /// <summary>A piece whose life ran out (Pop set).</summary>
    void Popped(in P p)
    {
        if (p.Tag == 4)
        {
            // A balloon pops: a puff of confetti in its colour.
            for (int i = 0; i < 16; i++)
            {
                ref var c = ref Chip(Shape.Square, p.Pos, Dir() * R(2, 5), 6, R(0.12f, 0.18f), i % 3 == 0 ? Gold : p.C0, White);
                c.Move = Move.Flutter;
                c.Grav = 1.1f;
                c.Lit = true;
                c.SpinV = R(6, 14);
            }
            Shock(p.Pos, 0.8f, 0.14f, White, p.C0, White, 0.25f, false);
            Sound?.Invoke(6, 2);
        }
    }

    // ------------------------------------------------------------------ 0 supernova

    void Supernova()
    {
        var up = Vector3.Up;
        if (At(0))
        {
            Flash(_c, 10, Gold, HotWhite, 0.5f);
            Flash(_c, 4, Lin(0xffb347), White, 0.22f);
            Shock(_g + up * 0.05f, 16, 0.9f, White, Orange, Gold, 0.12f);
            Shock(_c, 5, 0.45f, HotWhite, Orange, White, 0.14f, false);
            Pool(_g, 2, 9, 1.3f, Orange, Gold, 0.85f);
            Sparks(_c, 180, 12, 28, Gold, Red, White, 1.1f, 0.35f, 9, 1.3f);
            Fireballs(_c, 36, 8, Orange, Deep, FireCore, 1.7f, 0.9f);
            Lights(0.6f);
            Shake = 0.8f;
        }
        if (At(0.12f))
        {
            Shock(_c, 7.5f, 0.6f, Gold, Red, White, 0.08f, false);
            // Embers: slow, long-lived, drifting down.
            Sparks(_c, 70, 4, 11, Gold, Lin(0xff5a1a), White, 2.4f, 0.5f, 2.5f, 0.9f, 0.15f);
        }
        if (At(0.2f)) Smoke(_c + up * 0.8f, 24, 4.5f, Lin(0x3a3632), 1.8f, 3.2f, 2.2f);
        if (At(0.35f))
            for (int i = 0; i < 40; i++)
            {
                ref var p = ref Chip(Shape.Star, _c + Dir() * R(1, 5) + up * R(1, 4), Dir() * 0.6f + up * R(0.4f, 1.2f), R(1.2f, 2.4f), R(0.14f, 0.24f), Gold, White);
                p.Grav = -0.2f;
                p.Flick = 0.4f;
                p.SpinV = 0;
            }
    }

    // ------------------------------------------------------------------ 1 fireworks

    sealed class Rocket
    {
        public bool Live;
        public Vector3 Pos, Vel;
        public float Fuse;
        public Color Col;
        public int Type;
    }

    readonly Rocket[] _rockets = { new(), new(), new(), new(), new(), new(), new(), new(), new(), new() };
    static readonly float[] LaunchAt = { 0, 0.12f, 0.26f, 0.42f, 0.58f, 0.76f, 0.95f, 1.12f };
    static readonly int[] ShellType = { 0, 1, 2, 3, 0, 2, 1, 0 };

    void Fireworks()
    {
        if (At(0))
        {
            Flash(_c, 3, Gold, White, 0.15f);
            Sparks(_c, 30, 4, 9, Gold, Orange, White, 0.6f, 0.6f, 9, 1.5f, 0.16f);
            Smoke(_g + Vector3.Up * 0.6f, 8, 2, Lin(0x9a9590), 1.2f, 2.2f, 1);
        }
        for (int k = 0; k < LaunchAt.Length; k++)
        {
            if (!At(LaunchAt[k])) continue;
            Color[] cols = { _team, _trim, Gold, Lin(0xbfe6ff), _team, Lin(0xff5fb0), _trim, Gold };
            var from = _g + new Vector3(-_side * R(0, 1.5f), 0.4f, R(-3, 3));
            var vel = new Vector3(-_side * R(1, 4.5f), R(14, 17), R(-4.5f, 4.5f));
            foreach (var r in _rockets)
            {
                if (r.Live) continue;
                (r.Live, r.Pos, r.Vel, r.Fuse, r.Col, r.Type) = (true, from, vel, R(0.75f, 0.95f), cols[k], ShellType[k]);
                break;
            }
            Sound?.Invoke(1, 3);
        }
        if (At(2.0f))
        {
            // The finale: two golden willows that hang and droop.
            for (int w = -1; w <= 1; w += 2)
            {
                var at = _g + new Vector3(-_side * 4, R(10.5f, 12), w * 4.5f);
                Flash(at, 7, Gold, White, 0.3f);
                for (int i = 0; i < 110; i++)
                {
                    ref var p = ref Add(K.Streak, at, Dir() * R(5, 8), R(2.6f, 3.4f));
                    p.C0 = Gold;
                    p.C1 = Lin(0x7a3a08);
                    p.Hot = HotWhite;
                    p.Grav = 2.2f;
                    p.Drag = 0.9f;
                    p.S0 = p.S1 = 0.2f;
                    p.Streak = 0.09f;
                    p.Fade = 0.55f;
                }
            }
            Sound?.Invoke(1, 1);
            Sound?.Invoke(1, 2);
        }
    }

    /// <summary>The rockets in the air: a sparking trail, then the shell bursts at the fuse.</summary>
    void Rockets(float dt)
    {
        foreach (var r in _rockets)
        {
            if (!r.Live) continue;
            r.Vel.Y -= 6 * dt;
            r.Pos += r.Vel * dt;
            r.Fuse -= dt;
            for (int i = 0; i < 2; i++)
            {
                ref var s = ref Add(K.Streak, r.Pos, -r.Vel * 0.12f + Dir() * 1.2f, R(0.3f, 0.5f));
                s.C0 = Gold;
                s.C1 = Orange;
                s.Hot = White;
                s.Grav = 3;
                s.S0 = s.S1 = 0.16f;
            }
            ref var head = ref Add(K.Glow, r.Pos, Vector3.Zero, 0.05f);
            head.S0 = head.S1 = 0.9f;
            head.C0 = head.C1 = Gold;
            head.Hot = White;
            head.Move = Move.Rest;
            if (r.Fuse > 0) continue;
            r.Live = false;
            Shell(r);
        }
    }

    void Shell(Rocket r)
    {
        var c = r.Col;
        var dim = c * 0.45f;
        Flash(r.Pos, 7, c, White, 0.3f);
        Sound?.Invoke(1, 1);
        switch (r.Type)
        {
            case 1:
            {
                // A ring shell: a hoop of stars in a tilted plane, white at its heart.
                var n = Dir();
                var u = n.Cross(Vector3.Up).Normalized();
                if (u.LengthSquared() < 0.1f) u = Vector3.Right;
                var v = n.Cross(u).Normalized();
                for (int i = 0; i < 64; i++)
                {
                    float a = i / 64f * Mathf.Tau;
                    Star(r.Pos, (u * MathF.Cos(a) + v * MathF.Sin(a)) * R(9.5f, 10.5f), c, dim, R(1.3f, 1.7f));
                }
                for (int i = 0; i < 24; i++) Star(r.Pos, Dir() * R(3, 5), White, Gold, 1.2f);
                break;
            }
            case 2:
            {
                // Crackle: gold stars, then the sky fizzes with white glints.
                for (int i = 0; i < 70; i++) Star(r.Pos, Dir() * R(8, 10.5f), Gold, Lin(0x8a4a10), R(1.2f, 1.6f));
                for (int i = 0; i < 60; i++)
                {
                    ref var g = ref Chip(Shape.Star, r.Pos + Dir() * R(4, 9), Vector3.Zero, R(0.15f, 0.3f), R(0.2f, 0.32f), White, White);
                    g.Delay = R(0.45f, 1.05f);
                    g.Move = Move.Rest;
                    g.Flick = 0.45f;
                    g.SpinV = 0;
                }
                Sound?.Invoke(1, 2);
                break;
            }
            case 3:
            {
                // A palm: a few heavy comets with long tails.
                for (int i = 0; i < 16; i++)
                {
                    var d = Dir();
                    d.Y = MathF.Abs(d.Y) * 0.6f + 0.2f;
                    ref var p = ref Star(r.Pos, d.Normalized() * R(11, 14), c, Gold, R(1.5f, 1.9f));
                    p.S0 = p.S1 = 0.36f;
                    p.Streak = 0.13f;
                    p.Grav = 5;
                }
                for (int i = 0; i < 30; i++) Star(r.Pos, Dir() * R(4, 7), Gold, dim, 1.2f);
                break;
            }
            default:
                // A peony: a full sphere of stars in the colour.
                for (int i = 0; i < 95; i++) Star(r.Pos, Dir() * R(8.5f, 10.5f), c.Lerp(White, 0.15f), dim, R(1.4f, 1.9f));
                break;
        }
    }

    ref P Star(Vector3 at, Vector3 vel, Color c0, Color c1, float life)
    {
        ref var p = ref Add(K.Streak, at, vel, life);
        p.C0 = c0;
        p.C1 = c1;
        p.Hot = White;
        p.Grav = 2.6f;
        p.Drag = 1.5f;
        p.S0 = p.S1 = 0.24f;
        p.Streak = 0.06f;
        p.Fade = 0.6f;
        return ref p;
    }

    // ------------------------------------------------------------------ 2 thunderstrike

    static readonly float[] StrikeAt = { 0, 0.18f, 0.42f, 0.85f, 1.5f };

    void Thunderstrike()
    {
        var blue = Lin(0x6fcaff);
        var ice = new Color(0.8f, 0.95f, 1.3f);
        if (At(0))
        {
            // The storm cloud the bolts come out of.
            for (int i = 0; i < 18; i++)
            {
                ref var p = ref Add(K.Puff, _c + new Vector3(R(-9, 9) - _side * 3, R(26, 31), R(-9, 9)), new Vector3(R(-0.5f, 0.5f), 0, R(-0.5f, 0.5f)), 3.6f);
                p.C0 = p.C1 = Lin(0x2a2f3a) * R(0.8f, 1.1f);
                p.Smoke = true;
                p.S0 = R(6, 8);
                p.S1 = p.S0 * 1.4f;
                p.Fade = 0.7f;
            }
            Pool(_g, 1, 6.5f, 1.6f, Lin(0x2a6cff), ice, 0.8f);
        }
        for (int k = 0; k < StrikeAt.Length; k++)
            if (At(StrikeAt[k])) Strike(k, blue, ice);
        // Electricity crawls over the frame.
        if (_t < 2.8f)
            for (int k = Rate(_t < 1.8f ? 46 : 18); k > 0; k--)
            {
                float s = R(0, 12.2f);
                var a = FramePoint(s);
                var b = FramePoint(Mathf.Clamp(s + R(-1.8f, 1.8f), 0, 12.2f));
                Bolt(a, b, 0.16f, 0.07f, ice, White, 2, 0.35f, 0);
                if (R() < 0.4f) Sparks(a, 3, 1, 3, ice, blue, White, 0.4f, 0, 9, 0.5f, 0.12f);
            }
        if (At(1.2f))
            for (int i = 0; i < 36; i++)
            {
                ref var p = ref Chip(Shape.Star, _c + Dir() * R(0.5f, 4), Vector3.Up * R(0.5f, 1.5f), R(1.2f, 2.2f), R(0.14f, 0.22f), ice, White);
                p.Grav = -0.4f;
                p.Flick = 0.5f;
                p.SpinV = 0;
            }
    }

    void Strike(int k, Color blue, Color ice)
    {
        var top = new Vector3(_c.X - _side * R(0, 7) + R(-4, 4), 30, _c.Z + R(-7, 7));
        var hit = k == 0 ? _c + Vector3.Up * 0.8f : R() < 0.5f ? FramePoint(R(0, 12.2f)) : _g + new Vector3(-_side * R(0, 4), 0, R(-5, 5));
        Bolt(top, hit, k == 0 ? 0.6f : 0.42f, 0.26f, blue, White, 5, 0.2f, 3);
        Flash(hit, k == 0 ? 9 : 6, blue, White, 0.35f);
        Shock(hit with { Y = 0.05f }, k == 0 ? 12 : 7, 0.6f, ice, Lin(0x2a5cff), White, 0.1f);
        Sparks(hit, k == 0 ? 90 : 45, 8, 22, ice, blue, White, 0.6f, 0.4f, 9, 2);
        Lights(k == 0 ? 1.2f : 0.8f);
        Shake = k == 0 ? 0.9f : 0.5f;
        Sound?.Invoke(2, 1);
    }

    /// <summary>A point on the goal frame, s metres along it from the foot of one post, over the bar, to the other.</summary>
    Vector3 FramePoint(float s)
    {
        float x = _side * HL;
        if (s < GoalH) return new Vector3(x, s, -GoalHW);
        if (s < GoalH + GoalHW * 2) return new Vector3(x, GoalH, -GoalHW + (s - GoalH));
        return new Vector3(x, Mathf.Max(0, GoalH - (s - GoalH - GoalHW * 2)), GoalHW);
    }

    // ------------------------------------------------------------------ 3 volcano

    void Volcano()
    {
        var lava = Lin(0xff6a10);
        var lavaHot = new Color(1.4f, 1.0f, 0.45f);
        var crust = Lin(0x2a0d06);
        var up = Vector3.Up;
        if (At(0))
        {
            Flash(_c, 8, lava, FireCore, 0.4f);
            Shock(_g + up * 0.05f, 12, 0.8f, lavaHot, Lin(0xff3a0a), Gold, 0.16f);
            ref var glow = ref Pool(_g, 2, 6, 3.6f, Lin(0xff4a0a), lavaHot, 0.9f);
            glow.C1 = Lin(0x5a1004);
            // The ground splits: glowing cracks run out from the goalmouth.
            for (int i = 0; i < 8; i++)
            {
                float a = R() * Mathf.Tau;
                var p0 = _g + up * 0.06f;
                float len = R(4, 7.5f);
                for (int s = 0; s < 3; s++)
                {
                    a += R(-0.5f, 0.5f);
                    var p1 = p0 + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * len / 3;
                    ref var b = ref Beam(p0, p1, 0.24f - s * 0.05f, 3.2f, lava, lavaHot);
                    b.Delay = s * 0.08f;
                    b.C1 = Lin(0x3a0a04);
                    p0 = p1;
                }
            }
            Fireballs(_c, 20, 9, lava, crust, lavaHot, 1.8f, 1.0f, 1.2f);
            Lights(0.3f);
            Shake = 0.9f;
        }
        if (_t < 1.8f)
        {
            float k = _t < 1.2f ? 1 : (1.8f - _t) / 0.6f;
            for (int n = Rate(32 * k); n > 0; n--)
            {
                ref var p = ref Add(K.Puff, _g + new Vector3(R(-0.6f, 0.6f), 0.3f, R(-0.6f, 0.6f)), new Vector3(R(-1.5f, 1.5f), R(8, 14), R(-1.5f, 1.5f)), R(0.8f, 1.1f));
                p.C0 = lava;
                p.C1 = crust;
                p.Hot = lavaHot;
                p.S0 = R(0.9f, 1.3f);
                p.S1 = R(2.2f, 3);
                p.Drag = 1.2f;
                p.Grav = 4;
                p.Fade = 0.5f;
            }
            for (int n = Rate(140 * k); n > 0; n--)
            {
                ref var s = ref Add(K.Streak, _g + up * 0.4f, new Vector3(R(-4, 4), R(10, 20), R(-4, 4)), R(0.8f, 1.5f));
                s.C0 = Gold;
                s.C1 = Red;
                s.Hot = White;
                s.Grav = 9.8f;
                s.S0 = s.S1 = R(0.16f, 0.26f);
            }
            if (_t < 1.3f)
                for (int n = Rate(22); n > 0; n--)
                {
                    var vel = new Vector3(R(-1, 1) * 5 - _side * R(0, 4), R(11, 19), R(-1, 1) * 5);
                    ref var b = ref Chip(Shape.Rock, _g + up * 0.5f, vel, 3.4f, R(0.4f, 0.75f), lavaHot, Lin(0xffd060));
                    b.C1 = crust;
                    b.Splat = true;
                    b.Drag = 0.05f;
                    b.Fade = 1;
                    b.Tag = 1;
                }
        }
        // The bombs smoke and spit as they fly.
        for (int i = 0; i < _n; i++)
        {
            if (_p[i].Tag != 1 || _p[i].Delay > 0 || R() > 14 * Dt) continue;
            var at = _p[i].Pos;
            ref var s = ref Add(K.Streak, at, Dir() * 1.5f, R(0.25f, 0.45f));
            s.C0 = Gold;
            s.C1 = Red;
            s.Hot = White;
            s.Grav = 4;
            s.S0 = s.S1 = 0.15f;
        }
        if (_t < 2.6f)
            for (int n = Rate(13); n > 0; n--)
                Smoke(_g + up * 2.5f, 1, 1.5f, Lin(0x2b2522), 1.6f, 4.2f, R(3, 5));
    }

    // ------------------------------------------------------------------ 4 black hole

    void BlackHole()
    {
        var violet = Lin(0x9a4dff);
        var magenta = Lin(0xff4fd8);
        var cyan = Lin(0x5ff2ff);
        var voidC = Lin(0x07000f);
        var h = new Vector3(_side * (HL - 1), 2.6f, _c.Z);
        if (At(0))
        {
            Flash(_c, 4, violet, White, 0.25f);
            ref var orb = ref Add(K.Orb, h, Vector3.Zero, 1.35f);
            orb.S0 = 0.3f;
            orb.S1 = 3.6f;
            orb.C0 = orb.C1 = voidC;
            orb.Hot = magenta;
            orb.Fade = 1;
            orb.Move = Move.Rest;
            ref var lens = ref Shock(h, 2.1f, 1.35f, violet, cyan, White, 0.12f, false);
            lens.S0 = 0.2f;
            lens.Fade = 0.95f;
        }
        if (_t < 1.3f)
        {
            // Light and turf spiral in.
            for (int n = Rate(190); n > 0; n--)
            {
                var d = Dir();
                var at = h + d * R(8, 13);
                if (at.Y < 0.2f) at.Y = 0.2f + R(0, 2);
                var spin = Vector3.Up.Cross(at - h).Normalized();
                ref var p = ref Add(K.Streak, at, spin * R(4, 7), 3);
                p.Move = Move.Suck;
                p.A = h;
                p.Grav = 95;
                p.Drag = 0.6f;
                p.C0 = R() < 0.5f ? cyan : violet;
                p.C1 = magenta;
                p.Hot = White;
                p.S0 = p.S1 = 0.2f;
                p.Streak = 0.06f;
                p.Fade = 0.9f;
            }
            for (int n = Rate(34); n > 0; n--)
            {
                float a = R() * Mathf.Tau, r = R(3, 9);
                var at = _g + new Vector3(-_side * MathF.Abs(MathF.Cos(a)) * r, 0.05f, MathF.Sin(a) * r);
                ref var p = ref Chip(Shape.Shard, at, Vector3.Up * 2.5f, 3, R(0.2f, 0.32f), R() < 0.7f ? Lin(0x3f8f3a) : White, Lin(0x8fd06a));
                p.Move = Move.Suck;
                p.A = h;
                p.Grav = 60;
                p.Drag = 0.4f;
                p.Lit = true;
                p.SpinV = R(10, 20);
                p.Fade = 0.95f;
            }
        }
        if (At(1.3f))
        {
            // It collapses to a point...
            ref var orb = ref Add(K.Orb, h, Vector3.Zero, 0.15f);
            orb.S0 = 3.6f;
            orb.S1 = 0.1f;
            orb.C0 = orb.C1 = voidC;
            orb.Hot = White;
            orb.Fade = 1;
            orb.Move = Move.Rest;
        }
        if (At(1.45f))
        {
            // ...and lets everything go.
            Flash(h, 15, violet, White, 0.5f);
            Flash(h, 6, magenta, White, 0.25f);
            Shock(_g + Vector3.Up * 0.05f, 18, 1, White, violet, cyan, 0.1f);
            Shock(h, 7, 0.5f, cyan, violet, White, 0.1f, false);
            Shock(h, 10, 0.75f, magenta, violet, White, 0.06f, false);
            Sparks(h, 110, 14, 32, cyan, violet, White, 1.2f, 0.2f, 3, 1.6f);
            Sparks(h, 110, 14, 32, magenta, violet, White, 1.2f, 0.2f, 3, 1.6f);
            for (int i = 0; i < 70; i++)
            {
                float pick = R();
                ref var g = ref Chip(Shape.Shard, h, Dir() * R(3, 9), R(2.2f, 3.4f), 0.28f, pick < 0.4f ? magenta : pick < 0.8f ? cyan : White, White);
                g.Grav = 1.2f;
                g.Drag = 1.2f;
                g.Flick = 0.35f;
            }
            Lights(0.8f);
            Shake = 1;
            Sound?.Invoke(4, 1);
        }
    }

    // ------------------------------------------------------------------ 5 frostbite

    void Frostbite()
    {
        var ice = Lin(0x9fe3ff);
        var deep = Lin(0x4aa3e8);
        var snow = new Color(1.2f, 1.25f, 1.35f);
        if (At(0))
        {
            Flash(_c, 8, ice, White, 0.35f);
            Shock(_g + Vector3.Up * 0.05f, 11, 0.7f, White, ice, White, 0.1f);
            ref var frost = ref Pool(_g, 1, 7.5f, 4.2f, Lin(0xc4ecff), White, 0.95f);
            frost.Grow = 0.35f;
            frost.Fade = 0.75f;
            for (int i = 0; i < 110; i++)
            {
                var d = Dir();
                d.Y = MathF.Abs(d.Y) * 0.8f + 0.25f;
                ref var s = ref Chip(Shape.Shard, _c, d.Normalized() * R(7, 16), 2.6f, R(0.3f, 0.5f), R() < 0.6f ? ice : White, White);
                s.Lit = true;
                s.Bounce = true;
                s.SpinV = R(-14, 14);
            }
            // Spikes of ice burst up through the turf, in a wave out from the goal.
            for (int i = 0; i < 21; i++)
            {
                float a = R() * Mathf.Tau, r = i < 3 ? R(0, 0.8f) : R(1.4f, 5.8f);
                var at = _g + new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
                ref var s = ref Chip(Shape.Spike, at, Vector3.Zero, 3, 0, i < 3 ? deep : ice, White);
                s.S0 = 0;
                s.S1 = i < 3 ? R(4, 5) : R(1.6f, 3.2f);
                s.Grow = R(0.12f, 0.25f);
                s.Delay = r * 0.045f;
                s.Move = Move.Rest;
                s.Fade = 0.97f;
                s.SpinV = 0;
                s.Tag = 2;
            }
            Smoke(_g + Vector3.Up * 0.3f, 26, 6, Lin(0xdfefff), 1.4f, 2.8f, 0.4f);
            Lights(0.4f);
            Shake = 0.6f;
        }
        if (_t > 0.2f && _t < 3.6f)
            for (int n = Rate(70); n > 0; n--)
            {
                var at = _g + new Vector3(R(-9, 9) - _side * 3, R(7, 11), R(-9, 9));
                ref var f = ref Chip(Shape.Flake, at, Vector3.Zero, 5, R(0.18f, 0.3f), snow, White);
                f.Move = Move.Flutter;
                f.Grav = 1.4f;
                f.SpinV = R(-2, 2);
                f.Fade = 0.8f;
            }
        if (At(2.05f))
        {
            // The spikes shatter.
            for (int i = 0; i < _n; i++)
            {
                if (_p[i].Tag != 2) continue;
                var foot = _p[i].Pos;
                float tall = _p[i].S1;
                var col = _p[i].C0;
                Kill(i--);
                for (int k = 0; k < 9; k++)
                {
                    ref var s = ref Chip(Shape.Shard, foot + Vector3.Up * R(0, tall * 0.9f), Dir() * R(3, 8) + Vector3.Up * 2, R(1.2f, 2), R(0.25f, 0.45f), col, White);
                    s.Lit = true;
                    s.Bounce = true;
                    s.SpinV = R(-16, 16);
                }
            }
            Flash(_c, 5, ice, White, 0.2f);
            for (int i = 0; i < 40; i++)
            {
                ref var g = ref Chip(Shape.Star, _c + Dir() * R(0.5f, 5), Vector3.Up * 0.5f, R(0.6f, 1.4f), R(0.16f, 0.26f), White, White);
                g.Flick = 0.5f;
                g.SpinV = 0;
                g.Grav = 0;
            }
            Sound?.Invoke(5, 1);
        }
    }

    // ------------------------------------------------------------------ 6 party cannon

    Color Party(float pick) => pick switch
    {
        < 0.22f => _team,
        < 0.4f => _trim,
        < 0.58f => Gold,
        < 0.72f => White,
        < 0.86f => Lin(0xff5fb0),
        _ => Lin(0x47d7ff),
    };

    void Party()
    {
        if (At(0))
        {
            Cannon(-1, 230);
            Cannon(1, 230);
            for (int i = 0; i < 40; i++)
            {
                var d = new Vector3(R(-0.4f, 0.4f) - _side * 0.2f, 1, R(-0.4f, 0.4f)).Normalized();
                ref var g = ref Chip(Shape.Star, _c, d * R(5, 10), 2.5f, 0.22f, Gold, White);
                g.Grav = 3;
                g.Drag = 1.5f;
                g.Flick = 0.4f;
                g.SpinV = 0;
            }
            Shake = 0.4f;
        }
        if (At(0.15f))
            for (int i = 0; i < 12; i++)
            {
                var at = _g + new Vector3(-_side * R(0, 1), R(0.5f, 1.5f), R(-3, 3));
                ref var b = ref Chip(Shape.Balloon, at, new Vector3(-_side * R(0.3f, 1.2f), R(2.2f, 3.2f), R(-0.6f, 0.6f)), R(2.6f, 4.4f), R(0.95f, 1.25f), Party(R() * 0.58f + (i % 3 == 0 ? 0.6f : 0)), White);
                b.Grav = 0;
                b.Drag = 0;
                b.SpinV = 3;
                b.Lit = true;
                b.Fade = 0.98f;
                b.Pop = true;
                b.Tag = 4;
            }
        if (At(0.4f))
        {
            Cannon(-1, 150);
            Cannon(1, 150);
            Sound?.Invoke(6, 1);
        }
        for (int i = 0; i < _n; i++)
        {
            ref var p = ref _p[i];
            if (p.Tag == 4) p.Vel.X += MathF.Sin(_t * 2.2f + p.Seed * 9) * Dt * 1.2f;
            else if (p.Tag == 3 && p.Move != Move.Rest)
            {
                // A streamer's tail trails behind it, curling.
                var back = p.Vel.LengthSquared() > 0.01f ? -p.Vel.Normalized() : Vector3.Up;
                p.A = back * 1.2f + new Vector3(MathF.Sin(_t * 7 + p.Seed * 20), MathF.Cos(_t * 5 + p.Seed * 11) * 0.5f, MathF.Cos(_t * 6 + p.Seed * 13)) * 0.45f;
            }
        }
    }

    /// <summary>A confetti cannon at one post fires up and out over the goalmouth.</summary>
    void Cannon(int post, int n)
    {
        var mouth = new Vector3(_side * (HL - 0.3f), 0.6f, post * GoalHW);
        var dir = new Vector3(-_side * 0.4f, 1, post * 0.4f).Normalized();
        Flash(mouth, 2.5f, Gold, White, 0.15f);
        Smoke(mouth, 4, 1, Lin(0xdddddd), 0.8f, 1.2f, 1);
        for (int i = 0; i < n; i++)
        {
            ref var c = ref Chip(Shape.Square, mouth, (dir + Dir() * 0.28f).Normalized() * R(11, 18), 12, R(0.14f, 0.2f), Party(R()), White);
            c.Move = Move.Flutter;
            c.Grav = 1.1f;
            c.Lit = true;
            c.SpinV = R(6, 14);
            c.Fade = 0.85f;
        }
        for (int i = 0; i < 14; i++)
        {
            ref var s = ref Add(K.Beam, mouth, (dir + Dir() * 0.25f).Normalized() * R(10, 17), 9);
            s.C0 = s.C1 = Party(R());
            s.Hot = s.C0.Lerp(White, 0.35f);
            s.S0 = s.S1 = 0.14f;
            s.Move = Move.Flutter;
            s.Grav = 1.7f;
            s.Fade = 0.85f;
            s.Tag = 3;
        }
    }

    // ------------------------------------------------------------------ 7 arcade

    /// <summary>"GOAL!" in a 5 x 5 block font, top row first.</summary>
    static readonly string[] Letters =
    {
        ".###. .###. .###. #.... #",
        "#.... #...# #...# #.... #",
        "#.### #...# ##### #.... #",
        "#...# #...# #...# #.... .",
        ".###. .###. #...# ##### #",
    };

    void Arcade()
    {
        int cols = Letters[0].Length;
        var up = Vector3.Up;
        var centre = _c + up * 4.4f + new Vector3(-_side * 1.5f, 0, -_c.Z * 0.5f);
        if (At(0))
        {
            Flash(_c, 7, Hsv(0.8f), White, 0.3f);
            Shock(_g + up * 0.05f, 12, 0.8f, Hsv(0.55f), Hsv(0.85f), White, 0.15f, true, 2);
            Shock(_c, 6, 0.4f, Hsv(0.15f), Hsv(0.0f), White, 0.2f, false, 2);
            for (int i = 0; i < 150; i++)
            {
                var d = Dir();
                d.Y = MathF.Abs(d.Y) * 0.7f + 0.4f;
                float hue = MathF.Atan2(d.Z, d.X) / Mathf.Tau + R(-0.05f, 0.05f);
                ref var v = ref Chip(Shape.Voxel, _c, d.Normalized() * R(6, 15), R(1.8f, 2.8f), R(0.32f, 0.5f), Hsv(hue), Hsv(hue, 0.35f, 1.2f));
                v.Grav = 14;
                v.Bounce = true;
                v.SpinV = 0;
            }
            for (int i = 0; i < 16; i++)
            {
                ref var c = ref Chip(Shape.Coin, _c, new Vector3(-_side * R(1, 4) + R(-2, 2), R(9, 14), R(-3, 3)), 2.6f, 0.5f, new Color(1, 0.72f, 0.1f), new Color(1.3f, 1.2f, 0.7f));
                c.Grav = 16;
                c.Bounce = true;
                c.SpinV = 14;
            }
            // The letters fly up out of the blast and line up.
            for (int row = 0; row < Letters.Length; row++)
                for (int col = 0; col < cols; col++)
                {
                    if (Letters[row][col] != '#') continue;
                    ref var v = ref Chip(Shape.Voxel, _c + Dir() * 0.5f, Dir() * 6, 3.2f, 0.54f, White, White);
                    v.Move = Move.Seek;
                    v.A = centre;
                    v.B = new Vector3(col, row, 0);
                    v.Drag = 7;
                    v.Grav = 60;
                    v.Delay = 0.2f + col * 0.015f;
                    v.Fade = 0.92f;
                    v.SpinV = 0;
                    v.Tag = 5;
                }
            Lights(0.3f);
            Shake = 0.6f;
        }
        if (At(0.45f)) Sound?.Invoke(7, 1);
        if (_t < 2.2f)
        {
            var (right, upv) = Facing(centre);
            for (int i = 0; i < _n; i++)
            {
                ref var p = ref _p[i];
                if (p.Tag != 5) continue;
                float col = p.B.X, row = p.B.Y;
                p.A = centre + right * ((col - cols * 0.5f + 0.5f) * 0.56f) + upv * ((2 - row) * 0.56f + 0.25f * MathF.Sin(_t * 6 - col * 0.45f));
                float hue = col / cols * 0.9f - _t * 0.6f;
                p.C0 = p.C1 = Hsv(hue, 0.75f, 1.05f);
                p.Hot = Hsv(hue, 0.3f, 1.25f);
            }
        }
        if (At(2.2f))
        {
            for (int i = 0; i < _n; i++)
            {
                ref var p = ref _p[i];
                if (p.Tag != 5) continue;
                p.Tag = 0;
                p.Move = Move.Fly;
                p.Vel = Dir() * R(5, 11) + up * 3;
                p.Grav = 14;
                p.Bounce = true;
                p.Life = p.Max = R(1.3f, 1.9f);
                p.Fade = 0.6f;
            }
            Flash(centre, 6, Hsv(0.6f), White, 0.25f);
            Sound?.Invoke(7, 2);
        }
    }

    // ------------------------------------------------------------------ 8 phoenix

    void Phoenix()
    {
        var flame = Lin(0xff7a1a);
        var crimson = Lin(0xc81830);
        var up = Vector3.Up;
        var top = _g + up * 9 + new Vector3(-_side * 1.4f, 0, 0);
        if (At(0))
        {
            Flash(_c, 7, flame, FireCore, 0.35f);
            Shock(_g + up * 0.05f, 10, 0.7f, Gold, crimson, White, 0.12f);
            Fireballs(_c, 16, 6, flame, crimson, FireCore, 1.4f, 0.8f);
            Pool(_g, 1.5f, 6, 2.4f, Lin(0xff6a1a), FireCore, 0.7f);
        }
        if (_t < 1.2f)
        {
            // Two streams of fire spiral up out of the net.
            float h = _t / 1.2f, r = 2.6f * (1 - h) + 0.5f;
            for (int s = 0; s < 2; s++)
            {
                float a = _t * 13 + s * MathF.PI;
                var head = _g + new Vector3(MathF.Cos(a) * r - _side * 1.4f * h, 0.4f + h * 8.6f, MathF.Sin(a) * r);
                for (int k = 0; k < 2; k++)
                {
                    ref var p = ref Add(K.Puff, head + Dir() * 0.2f, Dir() * 0.8f + up * 0.5f, R(0.6f, 0.8f));
                    p.C0 = Gold;
                    p.C1 = crimson;
                    p.Hot = FireCore;
                    p.S0 = 1.2f;
                    p.S1 = 0.25f;
                    p.Drag = 2;
                    p.Fade = 0.5f;
                }
                for (int k = 0; k < 2; k++)
                {
                    ref var sp = ref Add(K.Streak, head, Dir() * 2 - up * 1.5f, R(0.4f, 0.7f));
                    sp.C0 = Gold;
                    sp.C1 = Red;
                    sp.Hot = White;
                    sp.Grav = 4;
                    sp.S0 = sp.S1 = 0.15f;
                }
            }
        }
        if (At(1.2f))
        {
            // The wings unfold from the body, feathers of fire.
            for (int w = -1; w <= 1; w += 2)
                for (int i = 0; i < 14; i++)
                    for (int row = 0; row < 3; row++)
                    {
                        float s = i / 13f;
                        ref var p = ref Add(K.Puff, top, Dir() * 2, 1.75f);
                        p.Move = Move.Seek;
                        p.A = top;
                        p.B = new Vector3(w, s, row);
                        p.Drag = 6;
                        p.Grav = 45;
                        p.S0 = p.S1 = 1.2f - 0.4f * s - row * 0.15f;
                        p.C0 = p.C1 = Gold.Lerp(flame, Mathf.Min(1, s * 1.6f + row * 0.2f)).Lerp(crimson, s * s);
                        p.Hot = s < 0.4f ? FireCore : Gold;
                        p.Delay = s * 0.12f;
                        p.Fade = 0.85f;
                        p.Tag = 6;
                    }
            for (int k = 0; k < 9; k++)
            {
                ref var p = ref Add(K.Puff, top, Vector3.Zero, 1.75f);
                p.Move = Move.Seek;
                p.A = top;
                p.B = new Vector3(k, 0, 0);
                p.Drag = 6;
                p.Grav = 40;
                p.S0 = p.S1 = 1.1f - k * 0.07f;
                p.C0 = p.C1 = flame.Lerp(crimson, k / 9f);
                p.Hot = Gold;
                p.Fade = 0.85f;
                p.Tag = 7;
            }
            ref var head = ref Add(K.Glow, top, Vector3.Zero, 1.75f);
            head.Move = Move.Seek;
            head.A = top;
            head.Drag = 8;
            head.Grav = 60;
            head.S0 = head.S1 = 3;
            head.C0 = head.C1 = Gold;
            head.Hot = FireCore;
            head.Fade = 0.9f;
            head.Tag = 8;
            Lights(0.25f);
            Sound?.Invoke(8, 1);
        }
        if (_t >= 1.2f && _t < 2.8f)
        {
            float u = _t - 1.2f;
            var body = top + up * (0.35f * MathF.Sin(u * 5));
            float flap = MathF.Sin(u * 6.5f);
            var (right, upv) = Facing(body);
            for (int i = 0; i < _n; i++)
            {
                ref var p = ref _p[i];
                switch (p.Tag)
                {
                    case 6:
                    {
                        float w = p.B.X, s = p.B.Y, row = p.B.Z;
                        float x = w * s * 7.2f;
                        float y = 1.1f * MathF.Sin(s * 2.6f) - 1.9f * s * s + flap * s * 2.4f - row * (0.55f + 0.35f * s);
                        p.A = body + right * x + upv * y;
                        if (R() < Dt * 1.6f)
                        {
                            ref var e = ref Add(K.Streak, p.Pos, Dir() * 1.2f - up, R(0.6f, 1.1f));
                            e.C0 = Gold;
                            e.C1 = Red;
                            e.Hot = White;
                            e.Grav = 3;
                            e.S0 = e.S1 = 0.15f;
                        }
                        break;
                    }
                    case 7:
                    {
                        float k = p.B.X;
                        p.A = body - upv * (0.9f + k * 0.5f) + right * (MathF.Sin(k * 0.9f + _t * 4) * 0.12f * k);
                        break;
                    }
                    case 8:
                        p.A = body + upv * 0.7f;
                        break;
                }
            }
        }
        if (At(2.8f))
        {
            var body = top;
            for (int i = 0; i < _n; i++)
            {
                ref var p = ref _p[i];
                if (p.Tag < 6 || p.Tag > 8) continue;
                body = p.Tag == 8 ? p.Pos : body;
                p.Tag = 0;
                p.Move = Move.Fly;
                p.Vel = Dir() * R(2, 6) + up * 1.5f;
                p.Grav = 2.5f;
                p.Drag = 1;
                p.S0 = p.S1;
                p.S1 = 0.1f;
                p.Life = p.Max = R(0.8f, 1.4f);
                p.Fade = 0.3f;
            }
            Flash(body, 9, flame, FireCore, 0.4f);
            Shock(body, 9, 0.6f, Gold, crimson, White, 0.07f, false);
            Sparks(body, 130, 2, 7, Gold, Lin(0xff4a10), White, 3, 0.2f, 2.4f, 1, 0.16f);
            Lights(0.3f);
            Sound?.Invoke(8, 2);
        }
    }

    // ------------------------------------------------------------------ 9 meteor

    Vector3 _meteorFrom;
    const float MeteorFall = 0.7f;

    void Meteor()
    {
        var rock = Lin(0x4a3a30);
        var dust = Lin(0x8a7660);
        var up = Vector3.Up;
        var hit = _g + up * 0.4f;
        // It comes in high over the far touchline, crossing the sky into the net.
        if (At(0)) _meteorFrom = _g + new Vector3(-_side * 6, 32, 16);
        if (_t < MeteorFall)
        {
            float h = _t / MeteorFall;
            var at = _meteorFrom.Lerp(hit, MathF.Pow(h, 1.6f));
            var back = (_meteorFrom - hit).Normalized();
            for (int k = 0; k < 3; k++)
            {
                ref var p = ref Add(K.Puff, at + Dir() * 0.4f, Dir() * 1.5f, R(0.4f, 0.55f));
                p.C0 = Gold;
                p.C1 = Lin(0xb8201a);
                p.Hot = FireCore;
                p.S0 = 2.3f;
                p.S1 = 0.6f;
                p.Fade = 0.5f;
            }
            for (int k = 0; k < 2; k++)
            {
                ref var p = ref Add(K.Puff, at + back * 1.2f, up * 0.5f + Dir() * 0.4f, R(2.2f, 3));
                p.C0 = p.C1 = Lin(0x3a3634);
                p.Smoke = true;
                p.S0 = 1.2f;
                p.S1 = 3.6f;
                p.Fade = 0.35f;
            }
            for (int k = 0; k < 4; k++)
            {
                ref var s = ref Add(K.Streak, at, back * R(4, 10) + Dir() * 3, R(0.3f, 0.5f));
                s.C0 = Gold;
                s.C1 = Red;
                s.Hot = White;
                s.S0 = s.S1 = 0.2f;
            }
            ref var tail = ref Beam(at, at + back * 6, 1.1f, 0.03f, Orange, HotWhite);
            tail.Fade = 1;
            ref var glow = ref Add(K.Glow, at, Vector3.Zero, 0.03f);
            glow.S0 = glow.S1 = 2.8f;
            glow.Alpha = 0.6f;
            glow.C0 = glow.C1 = Orange;
            glow.Hot = HotWhite;
            glow.Fade = 1;
            glow.Move = Move.Rest;
            ref var core = ref Add(K.Orb, at, Vector3.Zero, 0.03f);
            core.S0 = core.S1 = 1.5f;
            core.C0 = core.C1 = rock;
            core.Hot = Lin(0xff6a10);
            core.Fade = 1;
            core.Move = Move.Rest;
        }
        if (At(MeteorFall))
        {
            Flash(hit, 17, Orange, White, 0.55f);
            Flash(hit, 6, White, White, 0.2f);
            Shock(_g + up * 0.05f, 21, 1.1f, Lin(0xffcf8a), dust, White, 0.1f);
            Shock(hit, 10, 0.5f, Gold, Orange, White, 0.08f, false);
            for (int i = 0; i < 70; i++)
            {
                var d = Dir();
                d.Y = MathF.Abs(d.Y) * 0.6f + 0.35f;
                ref var r = ref Chip(Shape.Rock, hit, d.Normalized() * R(6, 17), R(2.4f, 3.4f), R(0.3f, 0.75f), Lin(0xff7a20) * 1.3f, Gold);
                r.C1 = rock;
                r.Grav = 11;
                r.Bounce = true;
                r.Fade = 0.85f;
            }
            for (int i = 0; i < 50; i++)
            {
                ref var c = ref Chip(Shape.Square, hit, up * R(4, 10) + Dir() * 4, R(1.6f, 2.4f), R(0.14f, 0.24f), R() < 0.5f ? Lin(0x5a4130) : Lin(0x3f7a33), White);
                c.Lit = true;
                c.Bounce = true;
            }
            Smoke(hit, 36, 9, dust, 2, 3.4f, 0.6f);
            Fireballs(hit, 28, 10, Orange, Deep, FireCore, 2, 1);
            Sparks(hit, 140, 10, 26, Gold, Red, White, 1, 0.45f, 9, 1.4f);
            ref var scorch = ref Pool(_g, 3.3f, 3.5f, 5, Lin(0x1a120c), Lin(0x0c0806));
            scorch.Fade = 0.8f;
            ref var rim = ref Shock(_g + up * 0.05f, 3.7f, 3.2f, new Color(1.3f, 0.6f, 0.15f), Lin(0x3a0a04), Gold, 0.2f);
            rim.S0 = 3.4f;
            rim.Fade = 0.3f;
            Lights(1.1f);
            Shake = 1;
        }
    }

    /// <summary>Fireballs rolling out of a blast.</summary>
    void Fireballs(Vector3 at, int n, float v, Color c0, Color c1, Color hot, float size = 1.4f, float life = 0.9f, float lift = 0.4f)
    {
        for (int i = 0; i < n; i++)
        {
            var d = Dir();
            d.Y = MathF.Abs(d.Y) * 0.7f + lift;
            ref var p = ref Add(K.Puff, at + d * 0.4f, d.Normalized() * v * R(0.5f, 1.1f), life * R(0.7f, 1.2f));
            p.C0 = c0;
            p.C1 = c1;
            p.Hot = hot;
            p.Drag = 3;
            p.Grav = -1.5f;
            p.S0 = size * R(0.5f, 0.8f);
            p.S1 = size * R(1.6f, 2.3f);
            p.Fade = 0.45f;
        }
    }
}
