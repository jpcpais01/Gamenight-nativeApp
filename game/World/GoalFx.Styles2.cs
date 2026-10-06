using System;
using Godot;

namespace GameNight.Grounds;

/// <summary>Goal explosions 10-13: Tornado, Disco, Rainbow, Haunted.</summary>
public sealed partial class GoalFx
{
    // ------------------------------------------------------------------ 10 tornado

    /// <summary>The twister's foot (it wanders), and its axis leaning at height h (0..1).</summary>
    Vector3 TwisterAt(float h) =>
        _g + new Vector3(-_side * 1.2f + MathF.Sin(_t * 1.3f) * 1.1f, 0, MathF.Cos(_t * 0.9f) * 1.4f)
           + new Vector3(MathF.Sin(_t * 1.7f) * 1.6f, 0, MathF.Cos(_t * 1.1f) * 1.6f) * h;

    void Tornado()
    {
        var up = Vector3.Up;
        var dust = Lin(0x9a8a70);
        if (At(0))
        {
            Flash(_c, 5, Lin(0xd8c8a0), White, 0.25f);
            Shock(_g + up * 0.05f, 10, 0.8f, Lin(0xe0d0b0), dust, White, 0.14f);
            Smoke(_g + up * 0.3f, 18, 5, dust, 1.4f, 2.2f, 0.5f);
            // The storm turning overhead.
            for (int i = 0; i < 16; i++)
            {
                float a = R() * Mathf.Tau, r = R(0, 6);
                ref var p = ref Add(K.Puff, _g + new Vector3(MathF.Cos(a) * r - _side * 1.2f, R(12.5f, 14.5f), MathF.Sin(a) * r), Vector3.Zero, 3.8f);
                p.C0 = p.C1 = Lin(0x3a3a46) * R(0.85f, 1.1f);
                p.Smoke = true;
                p.S0 = R(4, 5.5f);
                p.S1 = p.S0 * 1.3f;
                p.Fade = 0.75f;
                p.Tag = 21;
            }
            Shake = 0.5f;
        }
        if (_t < 2.5f)
        {
            // Turf, dirt, paper, dust and wind sucked into the funnel at its foot.
            int chips = Rate(75), puffs = Rate(26), wind = Rate(45);
            for (int n = chips + puffs + wind; n > 0; n--)
            {
                var foot = TwisterAt(0) + new Vector3(R(-0.5f, 0.5f), 0.1f, R(-0.5f, 0.5f));
                var kind = n > puffs + wind ? K.Chip : n > wind ? K.Puff : K.Streak;
                ref var p = ref Add(kind, foot, Vector3.Zero, 6);
                if (p.Kind == K.Puff)
                {
                    p.C0 = p.C1 = dust * R(0.8f, 1.1f);
                    p.Smoke = true;
                    p.S0 = 0.9f;
                    p.S1 = 1.8f;
                }
                else if (p.Kind == K.Streak)
                {
                    p.C0 = p.C1 = Lin(0xe8e0d0);
                    p.Hot = White;
                    p.S0 = p.S1 = 0.12f;
                    p.Streak = 0.05f;
                }
                else
                {
                    p.Shape = R() < 0.5f ? Shape.Shard : Shape.Square;
                    p.S0 = p.S1 = R(0.2f, 0.36f);
                    p.C0 = p.C1 = R() switch { < 0.45f => Lin(0x3f8f3a), < 0.8f => Lin(0x5a4130), _ => Lin(0xeeeae0) };
                    p.Hot = Lin(0x8fd06a);
                    p.Lit = true;
                    p.SpinV = R(10, 20);
                }
                p.Move = Move.Seek;
                p.Drag = 10;
                p.Grav = 140;
                p.Fade = 0.9f;
                p.B = new Vector3(R() * Mathf.Tau, 0, R(7, 10));
                p.Tag = 20;
            }
        }
        bool end = At(2.6f);
        for (int i = 0; i < _n; i++)
        {
            ref var p = ref _p[i];
            if (p.Tag == 21)
            {
                var d = p.Pos - _g;
                p.Vel = new Vector3(-d.Z, 0, d.X).Normalized() * 3;
                continue;
            }
            if (p.Tag != 20) continue;
            // Up the funnel in a widening spiral...
            float h = p.B.Y += Dt * 0.55f;
            p.B.X += Dt * p.B.Z * (1.4f - 0.6f * h);
            float r = 0.5f + 3.2f * MathF.Pow(h, 1.4f), a = p.B.X;
            var axis = TwisterAt(h);
            p.A = axis + new Vector3(MathF.Cos(a) * r, h * 11, MathF.Sin(a) * r);
            if (h < 1 && !end) continue;
            // ...and flung out of the top.
            var outward = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            var tangent = new Vector3(-outward.Z, 0, outward.X);
            p.Tag = 0;
            p.Move = Move.Fly;
            p.Vel = tangent * p.B.Z * r * 0.6f + outward * R(3, 6) + up * R(1, 3);
            p.Grav = p.Kind == K.Chip ? 9.8f : 0;
            p.Drag = p.Kind == K.Chip ? 0.3f : 1.5f;
            p.Bounce = p.Kind == K.Chip;
            p.Life = p.Max = p.Kind == K.Streak ? 0.4f : R(1.2f, 2);
            p.Fade = 0.6f;
        }
        if (end) Sound?.Invoke(10, 1);
    }

    // ------------------------------------------------------------------ 11 disco

    Vector3 MirrorBall()
    {
        float k = Mathf.Min(1, _t / 0.6f);
        float drop = 1 - (1 - k) * (1 - k) * (1 - k);
        return new Vector3(_side * (HL - 2), 16 - 9.5f * drop + MathF.Sin(_t * 3) * 0.15f * k, _c.Z * 0.5f);
    }

    void Disco()
    {
        var up = Vector3.Up;
        var ball = MirrorBall();
        if (At(0))
        {
            Flash(_c, 6, Hsv(0.85f), White, 0.3f);
            Shock(_g + up * 0.05f, 11, 0.8f, Hsv(0.55f), Hsv(0.9f), White, 0.12f);
            for (int i = 0; i < 6; i++) Sparks(_c, 12, 6, 14, Hsv(i / 6f), Hsv(i / 6f, 0.8f, 0.5f), White, 0.8f, 0.4f, 8, 1.5f, 0.18f);
            ref var m = ref Chip(Shape.Mirror, ball, Vector3.Zero, 3.4f, 2.6f, new Color(0.75f, 0.78f, 0.85f), White);
            m.Move = Move.Rest;
            m.SpinV = 1.6f;
            m.Fade = 0.95f;
            m.Tag = 22;
        }
        if (_t < 3.3f)
        {
            for (int i = 0; i < _n; i++)
                if (_p[i].Tag == 22) _p[i].Pos = ball;
            ref var cord = ref Beam(ball + up * 1.2f, ball + up * 30, 0.08f, 0.03f, Lin(0x9a9aa0), Lin(0xc8c8d0));
            cord.Fade = 1;
        }
        if (_t > 0.6f && _t < 3.3f)
        {
            // Eight coloured beams sweep round the goalmouth, each throwing a spot on the grass.
            for (int k = 0; k < 8; k++)
            {
                float a = k / 8f * Mathf.Tau + _t * 1.7f, rr = 6 + 2 * MathF.Sin(_t * 2.3f + k);
                var spot = _g + new Vector3(MathF.Cos(a) * rr - _side * 2, 0.05f, MathF.Sin(a) * rr);
                float hue = k / 8f + _t * 0.25f;
                ref var b = ref Beam(ball, spot, 0.4f, 0.035f, Hsv(hue, 0.6f, 1.2f), Hsv(hue, 0.25f, 1.4f));
                b.Fade = 1;
                b.Alpha = 0.85f;
                ref var pool = ref Pool(spot, 1.3f, 1.3f, 0.035f, Hsv(hue, 0.7f, 1), Hsv(hue, 0.3f, 1.3f), 0.8f);
                pool.Fade = 1;
            }
            for (int n = Rate(60); n > 0; n--)
            {
                ref var g = ref Chip(Shape.Star, ball + Dir() * R(1.4f, 2.2f), Vector3.Zero, 0.2f, 0.25f, White, White);
                g.Move = Move.Rest;
                g.Flick = 0.3f;
                g.SpinV = 0;
            }
            // Glints off the mirrors dancing over the pitch.
            for (int n = Rate(40); n > 0; n--)
            {
                ref var g = ref Chip(Shape.Star, _g + new Vector3(R(-10, 10) - _side * 3, R(0.1f, 3), R(-10, 10)), Vector3.Zero, 0.25f, 0.22f, Hsv(R()), White);
                g.Move = Move.Rest;
                g.SpinV = 0;
            }
        }
        if (At(0.6f))
        {
            Lights(0.3f);
            Sound?.Invoke(11, 1);
        }
        if (At(3.3f))
        {
            for (int i = 0; i < _n; i++)
                if (_p[i].Tag == 22) Kill(i--);
            Flash(ball, 8, Hsv(0.8f), White, 0.35f);
            for (int i = 0; i < 80; i++)
            {
                ref var s = ref Chip(Shape.Shard, ball, Dir() * R(4, 11), R(1.4f, 2.2f), R(0.22f, 0.36f), new Color(0.8f, 0.82f, 0.9f), White);
                s.Lit = true;
                s.Bounce = true;
                s.Flick = 0.3f;
            }
            for (int i = 0; i < 6; i++) Sparks(ball, 14, 6, 15, Hsv(i / 6f), Hsv(i / 6f, 0.8f, 0.5f), White, 0.9f, 0.2f, 6, 1.4f, 0.18f);
            Shake = 0.5f;
            Sound?.Invoke(11, 2);
        }
    }

    // ------------------------------------------------------------------ 12 rainbow

    static readonly int[] Bands = { 0xff3b3b, 0xff8c1a, 0xffe03a, 0x4fdc4a, 0x3aa8ff, 0x4a5cff, 0xa64dff };
    const float BowR = 7, BowStep = 5, BowGrow = 0.9f;
    Vector3 _bowO, _bowRight;

    Vector3 BowPoint(int side, float deg, float r)
    {
        float a = Mathf.DegToRad(deg);
        return _bowO + _bowRight * (side * MathF.Cos(a) * r) + Vector3.Up * (MathF.Sin(a) * r);
    }

    void Rainbow()
    {
        var up = Vector3.Up;
        if (At(0))
        {
            _bowO = _g + new Vector3(-_side * 1.2f, 0, 0);
            _bowRight = Facing(_bowO).right;
            Flash(_c, 6, Lin(0xfff6c0), White, 0.3f);
            Shock(_g + up * 0.05f, 10, 0.8f, White, Hsv(0.6f, 0.4f), White, 0.12f);
            // Clouds puff up at both feet.
            for (int s = -1; s <= 1; s += 2)
            {
                var foot = BowPoint(s, 0, BowR - 1.4f) + up * 0.4f;
                for (int i = 0; i < 14; i++)
                {
                    ref var p = ref Add(K.Puff, foot + Dir() * 0.6f, Dir() * 2.5f + up * 0.6f, R(2.2f, 3));
                    p.C0 = p.C1 = new Color(1.05f, 1.05f, 1.1f);
                    p.Hot = White;
                    p.Drag = 2;
                    p.S0 = R(1.4f, 1.9f);
                    p.S1 = p.S0 * 1.7f;
                    p.Fade = 0.6f;
                }
                Sparks(foot, 16, 3, 7, White, Hsv(0.6f, 0.3f), White, 0.6f, 0.5f, 6, 1.5f, 0.15f);
            }
        }
        if (_t0 < BowGrow)
        {
            // The arc grows from both feet to meet at the top, band by band.
            float e0 = Ease(_t0), e1 = Ease(_t);
            for (int j = 0; j * BowStep < 90; j++)
            {
                float d0 = j * BowStep, d1 = d0 + BowStep;
                if (!(e0 * 90 < d1 && d1 <= e1 * 90 + 1e-3f)) continue;
                for (int s = -1; s <= 1; s += 2)
                    for (int k = 0; k < Bands.Length; k++)
                    {
                        float r = BowR - k * 0.45f;
                        var c = Lin((uint)Bands[k]);
                        ref var b = ref Beam(BowPoint(s, d0, r), BowPoint(s, d1, r), 0.52f, 3.6f - _t, c, c.Lerp(White, 0.35f));
                        b.Fade = 0.8f;
                    }
            }
            for (int s = -1; s <= 1; s += 2)
            {
                ref var g = ref Chip(Shape.Star, BowPoint(s, e1 * 90, BowR - 1.4f) + Dir() * 1.2f, Vector3.Zero, 0.3f, 0.3f, White, White);
                g.Move = Move.Rest;
                g.Flick = 0.3f;
                g.SpinV = 0;
            }
        }
        if (At(BowGrow))
        {
            var apex = BowPoint(1, 90, BowR - 1.4f);
            Flash(apex, 6, White, White, 0.3f);
            for (int k = 0; k < Bands.Length; k++)
            {
                var c = Lin((uint)Bands[k]);
                Sparks(apex, 13, 5, 12, c, c * 0.5f, White, 1.2f, 0.2f, 3, 1.3f, 0.18f);
            }
            Sound?.Invoke(12, 1);
        }
        if (At(1.0f))
        {
            // Gold pours out of the cloud at one end.
            var foot = BowPoint(1, 0, BowR - 1.4f) + up * 0.6f;
            for (int i = 0; i < 34; i++)
            {
                ref var c = ref Chip(Shape.Coin, foot, -_bowRight * R(1, 4) + new Vector3(-_side * R(0, 2), R(8, 12), R(-1, 1)), 2.8f, 0.5f, new Color(1, 0.72f, 0.1f), new Color(1.3f, 1.2f, 0.7f));
                c.Grav = 14;
                c.Bounce = true;
                c.SpinV = 14;
            }
            for (int i = 0; i < 30; i++)
            {
                ref var g = ref Chip(Shape.Star, foot, Dir() * R(2, 6) + up * 3, R(1.2f, 2), 0.22f, Gold, White);
                g.Grav = 3;
                g.Drag = 1.5f;
                g.Flick = 0.4f;
                g.SpinV = 0;
            }
            Sound?.Invoke(12, 2);
        }
    }

    static float Ease(float t) => t <= 0 ? 0 : t >= BowGrow ? 1 : 1 - MathF.Pow(1 - t / BowGrow, 2);

    // ------------------------------------------------------------------ 13 haunted

    void Haunted()
    {
        var up = Vector3.Up;
        var green = Lin(0x7dff6a);
        var toxic = new Color(0.9f, 1.3f, 0.6f);
        var purple = Lin(0x6a2aa0);
        if (At(0))
        {
            Flash(_c, 7, green, toxic, 0.35f);
            Shock(_g + up * 0.05f, 11, 0.9f, green, purple, toxic, 0.12f);
            Fireballs(_c, 24, 6, green, purple, toxic, 1.6f, 1.1f, 0.8f);
            Pool(_g, 1.5f, 7, 3.2f, Lin(0x2a6a20), green, 0.7f);
            Smoke(_g + up * 0.2f, 18, 7, Lin(0x5a3a78), 1.2f, 3.2f, 0.2f);
            // Ghosts drift up out of the net.
            for (int i = 0; i < 7; i++)
            {
                var at = _c + new Vector3(R(-1, 1), R(0, 0.6f), R(-2.5f, 2.5f));
                ref var g = ref Chip(Shape.Ghost, at, new Vector3(-_side * R(0.3f, 1.2f), R(1.4f, 2.4f), R(-0.5f, 0.5f)), R(2.8f, 3.6f), R(1.3f, 1.9f), new Color(0.92f, 1.05f, 0.95f), new Color(0.05f, 0.08f, 0.06f));
                g.Grav = 0;
                g.Drag = 0;
                g.Delay = R(0, 0.6f);
                g.SpinV = 1;
                g.Fade = 0.7f;
                g.Tag = 23;
            }
            // A cloud of bats bursts out.
            for (int i = 0; i < 34; i++)
            {
                var d = Dir();
                d.Y = MathF.Abs(d.Y) * 0.6f + 0.35f;
                ref var b = ref Chip(Shape.Bat, _c, d.Normalized() * R(5, 9), R(2.4f, 3.4f), R(0.8f, 1.2f), Lin(0x5a3a7a), Lin(0xff3a2a));
                b.Grav = 0;
                b.Drag = 0.3f;
                b.SpinV = R(16, 22);
                b.Delay = R(0, 0.3f);
                b.Fade = 0.8f;
                b.Tag = 24;
            }
            // Will-o'-the-wisps.
            for (int i = 0; i < 16; i++)
            {
                ref var w = ref Add(K.Glow, _c + Dir() * R(1, 4), Dir() * 0.6f + up * 0.5f, R(2.5f, 3.5f));
                w.S0 = w.S1 = 0.9f;
                w.C0 = w.C1 = green;
                w.Hot = toxic;
                w.Flick = 0.2f;
                w.Fade = 0.7f;
            }
            Lights(0.35f);
            Shake = 0.5f;
        }
        if (_t < 1.6f)
            for (int n = Rate(18); n > 0; n--)
            {
                // Green flames lick up round the goalmouth.
                float a = R() * Mathf.Tau, r = R(2, 4);
                ref var p = ref Add(K.Puff, _g + new Vector3(MathF.Cos(a) * r, 0.2f, MathF.Sin(a) * r), up * R(2, 4), 0.7f);
                p.C0 = green;
                p.C1 = purple;
                p.Hot = toxic;
                p.S0 = 0.9f;
                p.S1 = 0.2f;
                p.Fade = 0.5f;
            }
        for (int i = 0; i < _n; i++)
        {
            ref var p = ref _p[i];
            if (p.Delay > 0) continue;
            if (p.Tag == 23)
            {
                p.Vel.X += MathF.Sin(_t * 2.5f + p.Seed * 20) * Dt * 2;
                p.Vel.Z += MathF.Cos(_t * 2.1f + p.Seed * 13) * Dt * 2;
            }
            else if (p.Tag == 24) p.Vel += Dir() * Dt * 14;
        }
        if (At(1.6f)) Sound?.Invoke(13, 1);
    }
}
