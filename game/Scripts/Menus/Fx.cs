using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Menus;

/// <summary>
/// The light show for the store and pack openings: particles, shockwave rings, light beams,
/// spotlights, the vault floor, pedestals and the foil shine that sweeps across packs and cards.
/// Everything is flat 2D in the menus' pixel skin; the bright parts are meant to be drawn into
/// an additive layer (see <see cref="Light"/>), so overlapping light builds up like real glare.
/// </summary>
public static partial class Fx
{
    /// <summary>A full-screen child layer that adds its colours onto what's under it.</summary>
    public sealed partial class Light : Control
    {
        public Light(Action<CanvasItem> draw)
        {
            MouseFilter = MouseFilterEnum.Ignore;
            SetAnchorsPreset(LayoutPreset.FullRect);
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            Draw += () => draw(this);
        }
    }

    public enum Kind { Spark, Shard, Star, Mote, Streak }

    public struct Bit
    {
        public Vector2 P, V;
        public Color C;
        public float Life, Max, Size, Gravity, Drag, Rot, Spin;
        public Kind Kind;
    }

    public struct Ring
    {
        public Vector2 P;
        public Color C;
        public float R, Speed, Life, Max, Width;
    }

    /// <summary>Particles and rings with their own little physics.</summary>
    public sealed class World
    {
        public readonly List<Bit> Bits = new();
        public readonly List<Ring> Rings = new();
        public readonly Random Rng = new();

        float F() => (float)Rng.NextDouble();

        public void Step(float dt)
        {
            for (int i = Bits.Count - 1; i >= 0; i--)
            {
                var b = Bits[i];
                b.Life -= dt;
                if (b.Life <= 0)
                {
                    Bits.RemoveAt(i);
                    continue;
                }
                b.V.Y += b.Gravity * dt;
                b.V *= Mathf.Pow(b.Drag, dt);
                b.P += b.V * dt;
                b.Rot += b.Spin * dt;
                Bits[i] = b;
            }
            for (int i = Rings.Count - 1; i >= 0; i--)
            {
                var r = Rings[i];
                r.Life -= dt;
                if (r.Life <= 0)
                {
                    Rings.RemoveAt(i);
                    continue;
                }
                r.R += r.Speed * dt;
                r.Speed *= Mathf.Pow(0.25f, dt);
                Rings[i] = r;
            }
        }

        public void Add(Bit b) => Bits.Add(b);

        /// <summary>A shockwave ring from p.</summary>
        public void Shock(Vector2 p, Color c, float speed, float life = 0.6f, float width = 4, float r0 = 10) =>
            Rings.Add(new Ring { P = p, C = c, R = r0, Speed = speed, Life = life, Max = life, Width = width });

        /// <summary>Sparks out in every direction.</summary>
        public void Burst(Vector2 p, Color[] cols, int n, float speed, float gravity = 140, Kind kind = Kind.Spark, float life = 0.9f)
        {
            for (int i = 0; i < n; i++)
            {
                float a = F() * Mathf.Tau;
                float s = speed * (0.25f + F() * 0.9f);
                float l = life * (0.6f + F() * 0.7f);
                Bits.Add(new Bit
                {
                    P = p, V = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s, C = cols[i % cols.Length], Life = l, Max = l,
                    Size = kind == Kind.Shard ? 5 + F() * 6 : 2 + F() * 4, Gravity = gravity, Drag = 0.35f,
                    Rot = F() * Mathf.Tau, Spin = (F() - 0.5f) * 18, Kind = kind,
                });
            }
        }

        /// <summary>Light pulled in from all around towards p (the charge before a big reveal).</summary>
        public void Implode(Vector2 p, Color c, int n, float radius)
        {
            for (int i = 0; i < n; i++)
            {
                float a = F() * Mathf.Tau;
                float r = radius * (0.7f + F() * 0.6f);
                float l = 0.35f + F() * 0.25f;
                var from = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                Bits.Add(new Bit { P = from, V = (p - from) / l, C = c, Life = l, Max = l, Size = 2 + F() * 2, Drag = 1, Kind = Kind.Streak });
            }
        }

        /// <summary>Confetti and glitter falling from above the screen.</summary>
        public void Rain(Vector2 size, Color[] cols, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float l = 1.6f + F() * 1.2f;
                Bits.Add(new Bit
                {
                    P = new Vector2(F() * size.X, -10 - F() * 120), V = new Vector2((F() - 0.5f) * 90, 70 + F() * 90),
                    C = cols[i % cols.Length], Life = l, Max = l, Size = 3 + F() * 4, Gravity = 60, Drag = 0.7f,
                    Rot = F() * Mathf.Tau, Spin = (F() - 0.5f) * 14, Kind = i % 3 == 0 ? Kind.Star : Kind.Shard,
                });
            }
        }

        /// <summary>Dust in the light, rising slowly; call every frame with a small rate.</summary>
        public void Motes(Vector2 size, Color c, float rate, float dt, float floorY)
        {
            float n = rate * dt;
            while (n > 0)
            {
                if (F() < n)
                {
                    float l = 3 + F() * 3;
                    Bits.Add(new Bit
                    {
                        P = new Vector2(F() * size.X, floorY - F() * size.Y * 0.4f), V = new Vector2((F() - 0.5f) * 8, -10 - F() * 16),
                        C = c, Life = l, Max = l, Size = F() < 0.2f ? 3 : 2, Drag = 1, Kind = Kind.Mote,
                    });
                }
                n -= 1;
            }
        }

        public void Draw(CanvasItem ci)
        {
            foreach (var r in Rings)
            {
                float k = r.Life / r.Max;
                ci.DrawArc(r.P, r.R, 0, Mathf.Tau, 56, new Color(r.C, k), Mathf.Max(1, r.Width * k));
            }
            foreach (var b in Bits)
            {
                float k = b.Life / b.Max;
                switch (b.Kind)
                {
                    case Kind.Mote:
                        // Fade in and out.
                        float a = Mathf.Min(1, Mathf.Min(k * 3, (1 - k) * 4)) * 0.55f;
                        ci.DrawRect(new Rect2(b.P, new Vector2(b.Size, b.Size)), new Color(b.C, a));
                        break;
                    case Kind.Streak:
                        var tail = b.P - b.V.Normalized() * Mathf.Min(26, b.V.Length() * 0.05f);
                        ci.DrawLine(tail, b.P, new Color(b.C, 0.4f + 0.6f * (1 - k)), b.Size);
                        break;
                    case Kind.Shard:
                    {
                        // A spinning strip of foil: its width flickers as it turns.
                        float w = b.Size * Mathf.Abs(Mathf.Cos(b.Rot)) + 1, h = b.Size * 0.55f;
                        var c = new Color(b.C.Lightened(Mathf.Abs(Mathf.Sin(b.Rot)) * 0.4f), Mathf.Min(1, k * 2));
                        ci.DrawRect(new Rect2(b.P - new Vector2(w, h) / 2, new Vector2(w, h)), c);
                        break;
                    }
                    case Kind.Star:
                        Twinkle(ci, b.P, b.Size * (0.6f + 0.4f * Mathf.Abs(Mathf.Sin(b.Rot))), new Color(b.C, Mathf.Min(1, k * 2)));
                        break;
                    default:
                        ci.DrawRect(new Rect2(b.P, new Vector2(b.Size, b.Size)), new Color(b.C, Mathf.Clamp(k * 1.6f, 0, 1)));
                        break;
                }
            }
        }
    }

    // ---------------------------------------------------------------- shapes of light

    /// <summary>A four-point glint.</summary>
    public static void Twinkle(CanvasItem ci, Vector2 p, float s, Color c)
    {
        float t = Mathf.Max(1, Mathf.Round(s * 0.3f));
        ci.DrawRect(new Rect2((p - new Vector2(s, t / 2)).Round(), new Vector2(s * 2, t).Round()), c);
        ci.DrawRect(new Rect2((p - new Vector2(t / 2, s)).Round(), new Vector2(t, s * 2).Round()), c);
        ci.DrawRect(new Rect2((p - new Vector2(t, t)).Round(), new Vector2(t * 2, t * 2)), c);
    }

    /// <summary>Light beams fanning out from c.</summary>
    public static void Beams(CanvasItem ci, Vector2 c, Color col, int n, float len, float half, float rot, float alpha, float inner = 0)
    {
        for (int i = 0; i < n; i++)
        {
            float a = rot + i * Mathf.Tau / n;
            var d0 = new Vector2(Mathf.Cos(a - half), Mathf.Sin(a - half));
            var d1 = new Vector2(Mathf.Cos(a + half), Mathf.Sin(a + half));
            var dm = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            ci.DrawColoredPolygon(new[] { c + dm * inner, c + d0 * len, c + d1 * len }, new Color(col, alpha));
        }
    }

    /// <summary>Soft round glow: stacked discs.</summary>
    public static void Glow(CanvasItem ci, Vector2 c, float r, Color col, float alpha, int layers = 4)
    {
        for (int i = 0; i < layers; i++)
        {
            float k = 1 - i / (float)layers;
            ci.DrawColoredPolygon(Px.Ellipse(c, r * k, r * k, 28), new Color(col, alpha / layers * 1.6f));
        }
    }

    /// <summary>A cone of light from a lamp at top down to a pool at the bottom.</summary>
    public static void Spot(CanvasItem ci, Vector2 lamp, Vector2 pool, float topW, float poolW, Color c, float alpha)
    {
        for (int i = 0; i < 3; i++)
        {
            float k = 1 - i * 0.3f;
            ci.DrawColoredPolygon(new[]
            {
                lamp + new Vector2(-topW * k / 2, 0), lamp + new Vector2(topW * k / 2, 0),
                pool + new Vector2(poolW * k / 2, 0), pool + new Vector2(-poolW * k / 2, 0),
            }, new Color(c, alpha * 0.5f));
        }
        ci.DrawColoredPolygon(Px.Ellipse(pool, poolW * 0.55f, poolW * 0.12f, 24), new Color(c, alpha * 1.2f));
    }

    /// <summary>
    /// A bright diagonal band crossing r (0 = off the left, 1 = off the right), clipped to it:
    /// the shine sliding over foil.
    /// </summary>
    public static void Shine(CanvasItem ci, Rect2 r, float k, Color c, float width = 0.22f)
    {
        float span = r.Size.X + r.Size.Y * 0.6f;
        float x = r.Position.X - r.Size.Y * 0.6f + k * (span + width * r.Size.X) - width * r.Size.X;
        float w = width * r.Size.X;
        Band(ci, r, x, w, c);
        Band(ci, r, x + w * 1.4f, w * 0.3f, new Color(c, c.A * 0.6f));
    }

    static void Band(CanvasItem ci, Rect2 r, float x, float w, Color c)
    {
        float lean = r.Size.Y * 0.6f;
        var band = new[]
        {
            new Vector2(x + lean, r.Position.Y), new Vector2(x + lean + w, r.Position.Y),
            new Vector2(x + w, r.End.Y), new Vector2(x, r.End.Y),
        };
        var box = new[] { r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y) };
        foreach (var poly in Geometry2D.IntersectPolygons(band, box))
            if (poly.Length >= 3) ci.DrawColoredPolygon(poly, c);
    }

    /// <summary>A holographic rainbow sheen drifting over r.</summary>
    public static void Holo(CanvasItem ci, Rect2 r, float t, float alpha)
    {
        int n = 7;
        float w = r.Size.X / 3.2f;
        for (int i = 0; i < n; i++)
        {
            float h = (i / (float)n + t * 0.15f) % 1f;
            float x = r.Position.X - r.Size.Y * 0.6f + ((i * w * 0.9f + t * 40) % (r.Size.X + r.Size.Y * 0.6f + w)) - w * 0.5f;
            Band(ci, r, x, w * 0.55f, Color.FromHsv(h, 0.65f, 1, alpha));
        }
    }

    // ---------------------------------------------------------------- the room

    /// <summary>
    /// The vault: deep night, a glowing perspective floor from the horizon down, and a vignette.
    /// `tint` colours the floor lines; `dim` (0..1) drops the room into darkness.
    /// </summary>
    public static void Vault(CanvasItem ci, Vector2 size, Color tint, float t, float horizon, float dim = 0)
    {
        float W = size.X, H = size.Y;
        Px.Bands(ci, new Rect2(0, 0, W, H), new[] { Px.Hex(0x05040f), Px.Hex(0x090720), Px.Hex(0x0e0a2c), Px.Hex(0x130d36), Px.Hex(0x0a0820) },
            new[] { 0, 0.22f, 0.42f, 0.6f, 0.78f });
        // Far stars, twinkling on a slow step.
        for (int i = 0; i < 46; i++)
        {
            float x = (i * 0.6180339f % 1f) * W, y = (i * 0.4142135f % 1f) * horizon * 0.92f;
            float tw = ((int)(t * 3 + i * 7) % 9) < 2 ? 0.9f : 0.35f;
            ci.DrawRect(new Rect2(Mathf.Round(x), Mathf.Round(y), i % 7 == 0 ? 2 : 1, i % 7 == 0 ? 2 : 1), new Color(1, 1, 1, tw * (1 - dim)));
        }
        // The horizon glow.
        ci.DrawRect(new Rect2(0, horizon - 10, W, 10), new Color(tint, 0.06f * (1 - dim)));
        ci.DrawRect(new Rect2(0, horizon - 3, W, 3), new Color(tint, 0.14f * (1 - dim)));
        // Floor: lines running to the vanishing point, and rungs bunching towards the horizon.
        ci.DrawRect(new Rect2(0, horizon, W, H - horizon), new Color(0.02f, 0.015f, 0.06f, 0.9f));
        var vp = new Vector2(W / 2, horizon);
        float lineA = 0.22f * (1 - dim * 0.8f);
        for (int i = -12; i <= 12; i++)
        {
            var end = new Vector2(W / 2 + i * W * 0.16f, H);
            ci.DrawLine(vp, end, new Color(tint, lineA * (1 - Mathf.Abs(i) / 14f)), 1);
        }
        float scroll = t * 0.25f % 1f;
        for (int i = 0; i < 9; i++)
        {
            float k = (i + scroll) / 9f;
            float y = horizon + (H - horizon) * k * k;
            ci.DrawRect(new Rect2(0, y, W, 1), new Color(tint, lineA * k * 1.2f));
        }
        // Vignette.
        for (int i = 0; i < 4; i++)
        {
            float e = 18 + i * 22;
            var c = new Color(0, 0, 0, 0.16f);
            ci.DrawRect(new Rect2(0, 0, e, H), c);
            ci.DrawRect(new Rect2(W - e, 0, e, H), c);
            ci.DrawRect(new Rect2(0, H - e * 0.6f, W, e * 0.6f), c);
        }
        if (dim > 0) ci.DrawRect(new Rect2(0, 0, W, H), new Color(0, 0, 0, dim * 0.75f));
    }

    /// <summary>A round plinth with a lit top and a glowing rim.</summary>
    public static void Pedestal(CanvasItem ci, Vector2 top, float w, Color rim, float glow)
    {
        float h = w * 0.34f, ry = w * 0.13f;
        var body = new Rect2(top.X - w / 2, top.Y, w, h);
        Px.Bands(ci, body, new[] { Px.Hex(0x2a2550), Px.Hex(0x1d1940), Px.Hex(0x141130), Px.Hex(0x0c0a20) }, new[] { 0, 0.3f, 0.6f, 0.85f });
        ci.DrawColoredPolygon(Px.Ellipse(top + new Vector2(0, h), w / 2, ry, 28), Px.Hex(0x0c0a20));
        ci.DrawRect(new Rect2(body.Position.X, body.Position.Y + h * 0.18f, w, 2), new Color(rim, 0.5f + glow * 0.4f));
        ci.DrawColoredPolygon(Px.Ellipse(top, w / 2 + 2, ry + 2, 28), new Color(rim, 0.6f + glow * 0.4f));
        ci.DrawColoredPolygon(Px.Ellipse(top, w / 2, ry, 28), Px.Hex(0x3a3470));
        ci.DrawColoredPolygon(Px.Ellipse(top, w / 2 * 0.72f, ry * 0.72f, 28), new Color(rim, 0.18f + glow * 0.3f));
    }
}
