using System;
using Godot;

namespace GameNight.Menus;

/// <summary>
/// The launch screen, in whole art pixels, in the home page's night palette. The ground is dark;
/// the floodlights clunk on one bank at a time and throw their beams onto the pitch; the ball (the
/// app icon's ball, drawn live as a real panelled sphere) drops out of the sky onto the centre spot
/// and bounces to rest, spinning; the GAMENIGHT wordmark slams in with a flash, the crowd's camera
/// flashes pop, and a scoreboard bar ticks through the pre-match jobs. Then it fades away.
/// </summary>
public sealed partial class Splash : Control
{
    const double Hold = 2.5, Fade = 0.4;
    double _t;
    bool _leaving, _frozen;
    double _leaveAt;
    public event Action Gone;

    static readonly Vector2[] Stars =
    {
        new(0.06f, 0.09f), new(0.19f, 0.04f), new(0.31f, 0.14f), new(0.46f, 0.06f), new(0.58f, 0.17f), new(0.72f, 0.05f), new(0.84f, 0.13f),
        new(0.93f, 0.07f), new(0.12f, 0.24f), new(0.88f, 0.26f), new(0.03f, 0.18f), new(0.25f, 0.09f), new(0.39f, 0.03f), new(0.52f, 0.12f),
        new(0.65f, 0.09f), new(0.78f, 0.2f), new(0.97f, 0.15f), new(0.35f, 0.25f), new(0.62f, 0.26f),
    };

    static readonly string[] Jobs = { "OPENING THE GATES", "SWITCHING ON THE LIGHTS", "WARMING UP", "KICK-OFF" };

    /// <summary>The 12 pentagon centres of a football (an icosahedron's vertices).</summary>
    static readonly Vector3[] Panels = MakePanels();

    static Vector3[] MakePanels()
    {
        float phi = (1 + Mathf.Sqrt(5)) / 2;
        var v = new System.Collections.Generic.List<Vector3>();
        foreach (float a in new[] { -1f, 1f })
            foreach (float b in new[] { -phi, phi })
                v.AddRange(new[] { new Vector3(0, a, b), new Vector3(a, b, 0), new Vector3(b, 0, a) });
        return v.ConvertAll(x => x.Normalized()).ToArray();
    }

    public Splash()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        // Debug: `--splash=1.4` holds the launch screen at that moment (for screenshots).
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--splash=")) { _t = double.Parse(arg[9..], System.Globalization.CultureInfo.InvariantCulture); _frozen = true; }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } && _t > 0.6) Leave();
        AcceptEvent();
    }

    void Leave()
    {
        if (_leaving) return;
        _leaving = true;
        _leaveAt = _t;
    }

    public override void _Process(double delta)
    {
        if (_frozen) { QueueRedraw(); return; }
        _t += delta;
        if (!_leaving && _t > Hold) Leave();
        if (_leaving && _t - _leaveAt > Fade)
        {
            Gone?.Invoke();
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    /// <summary>0..1 over [start, start + dur], easing out.</summary>
    static float Ease(double t, double start, double dur)
    {
        float k = Mathf.Clamp((float)((t - start) / dur), 0, 1);
        return 1 - (1 - k) * (1 - k);
    }

    /// <summary>The ball's height above the spot (0..1 of the drop) and how far it has spun.</summary>
    static (float lift, float spin, float squash) Bounce(double t)
    {
        // Drop, then two shrinking hops, each a parabola.
        double[] at = { 0.25, 0.7, 1.08, 1.32, 1.46 };
        float[] peak = { 1, 0.34f, 0.12f, 0.04f };
        float spin = (float)Math.Max(0, Math.Min(t, 1.6) - 0.25) * 5.2f;
        if (t < at[0]) return (1, 0, 0);
        if (t < at[1])
        {
            float k = (float)((t - at[0]) / (at[1] - at[0]));
            return (1 - k * k, spin, 0);
        }
        for (int i = 1; i < at.Length - 1; i++)
            if (t < at[i + 1])
            {
                float k = (float)((t - at[i]) / (at[i + 1] - at[i]));
                float sq = k < 0.12f ? (1 - k / 0.12f) * peak[i - 1] : 0;
                return (peak[i] * 4 * k * (1 - k), spin, sq);
            }
        return (0, spin, 0);
    }

    public override void _Draw()
    {
        float W = Size.X, H = Size.Y;
        // One art pixel.
        float p = Mathf.Max(2, Mathf.Floor(Mathf.Min(W, H) / 100f));
        float alpha = _leaving ? 1 - Ease(_t, _leaveAt, Fade) : 1;
        var night = Px.Night;
        var ink = Px.Hex(0x0c0a24);
        var cream = Px.Hex(0xf7f3e8);
        var lampC = Px.Hex(0xfff0bf);
        Color A(Color c, float a = 1) => new(c, c.A * a * alpha);
        float Snap(float v) => Mathf.Floor(v / p) * p;

        DrawRect(new Rect2(0, 0, W, H), A(Px.Hex(0x0b0a20)));
        if (alpha <= 0) return;

        // Sky: stars and hard bands warming toward the lit ground.
        bool twinkle = (int)(_t / 0.8) % 2 == 0;
        for (int i = 0; i < Stars.Length; i++)
        {
            var c = i % 3 == 0 ? Px.Hex(0xa99fc6) : Px.Hex(0xfffbe6);
            float a = (i % 2 == 0) == twinkle ? 1 : 0.3f;
            DrawRect(new Rect2(Snap(Stars[i].X * W), Snap(Stars[i].Y * H), p, p), A(c, a));
        }
        float on = Ease(_t, 0.15, 0.5);
        float horizon = Snap(H * 0.5f), pitchY = Snap(H * 0.66f);
        Px.Bands(this, new Rect2(0, H * 0.18f, W, horizon - H * 0.18f + p),
            new[] { A(night, 0), A(Px.Hex(0x1a1240), 0.6f + 0.4f * on), A(Px.Hex(0x26175a), on), A(Px.Hex(0x3b1d68), on), A(Px.Hex(0x5c2470), on), A(Px.Hex(0x8a3170), on) },
            new[] { 0, 0.22f, 0.44f, 0.64f, 0.82f, 0.93f });

        // The bowl: a back stand across and two wings rising to the sides.
        float standTop = Snap(H * 0.44f);
        DrawRect(new Rect2(0, horizon - p * 2, W, pitchY - horizon + p * 2), A(ink));
        for (int s = 0; s < 2; s++)
        {
            float dir = s == 0 ? 1 : -1, x0 = s == 0 ? 0 : W;
            DrawColoredPolygon(new[] { new Vector2(x0, standTop - p * 8), new Vector2(x0 + dir * W * 0.3f, horizon - p * 2), new Vector2(x0 + dir * W * 0.3f, pitchY), new Vector2(x0, pitchY) }, A(ink));
        }
        DrawRect(new Rect2(W * 0.25f, standTop, W * 0.5f, horizon - standTop), A(ink));
        // Roof lip.
        DrawRect(new Rect2(W * 0.25f - p * 2, standTop - p, W * 0.5f + p * 4, p), A(Px.Hex(0x2b2670)));
        // Crowd lights wake with the floodlights, then camera flashes pop.
        float crowd = Ease(_t, 0.6, 0.4);
        if (crowd > 0)
        {
            bool flick = (int)(_t / 0.45) % 2 == 0;
            int n = 0;
            for (float cy = standTop + p * 2; cy < pitchY - p * 2; cy += p * 3)
                for (float cx = p; cx < W - p; cx += p * 3, n++)
                {
                    // Only inside the stand silhouette.
                    float wing = cx < W * 0.3f ? cx / (W * 0.3f) : cx > W * 0.7f ? (W - cx) / (W * 0.3f) : 2;
                    float roof = wing < 1 ? Mathf.Lerp(standTop - p * 8, horizon - p * 2, wing) : cx > W * 0.25f && cx < W * 0.75f ? standTop : horizon - p * 2;
                    if (cy < roof + p * 2) continue;
                    uint h = (uint)(n * 2654435761u) >> 27;
                    if (h % 4 > 0) continue;
                    var c = (h >> 2) switch { 0 => Px.Gold, 1 => Px.Neon, 2 => Px.Cyan, 3 => lampC, 4 => Px.Hex(0xa99fc6), _ => Px.Hex(0x6a5fa0) };
                    DrawRect(new Rect2(Snap(cx), Snap(cy), p, p), A(c, crowd * (flick == (n % 2 == 0) ? 1 : 0.5f)));
                    if (_t > 1.1 && (n * 131 + (int)(_t * 9)) % 97 == 0)
                    {
                        DrawRect(new Rect2(Snap(cx) - p, Snap(cy), p * 3, p), A(Colors.White, 1));
                        DrawRect(new Rect2(Snap(cx), Snap(cy) - p, p, p * 3), A(Colors.White, 1));
                    }
                }
        }

        // The pitch: mown stripes, touchline, halfway line and the centre circle.
        for (float x = 0, i = 0; x < W; x += p * 12, i++)
            DrawRect(new Rect2(x, pitchY, p * 12, H - pitchY), A(i % 2 == 0 ? Px.Hex(0x2f7a3c) : Px.Hex(0x3d9447), 0.35f + 0.65f * on));
        DrawRect(new Rect2(0, pitchY, W, p), A(cream, 0.4f + 0.6f * on));
        float cx0 = Snap(W / 2), spotY = Snap(H * 0.83f);
        DrawRect(new Rect2(cx0, pitchY, p, H - pitchY), A(cream, 0.4f + 0.6f * on));
        float rx = W * 0.16f, ry = (H - pitchY) * 0.42f;
        for (int k = 0; k < 96; k++)
        {
            float a = k / 96f * Mathf.Tau;
            DrawRect(new Rect2(Snap(cx0 + Mathf.Cos(a) * rx), Snap(spotY + Mathf.Sin(a) * ry), p, p), A(cream, 0.4f + 0.6f * on));
        }

        // Floodlights: lattice masts on the stand wings, lamp banks that clunk on, beams onto the grass.
        foreach (var (fx, delay) in new[] { (W * 0.13f, 0.15), (W * 0.87f, 0.32) })
        {
            float lx = Snap(fx), top = Snap(H * 0.1f);
            double lt = _t - delay;
            bool lit = lt > 0.2 || (lt > 0 && lt < 0.08);
            var lamp = new Rect2(lx - p * 8, top, p * 16, p * 7);
            if (lit)
            {
                var c0 = lamp.GetCenter() + new Vector2(0, p * 3.5f);
                float side = fx < W / 2 ? 1 : -1;
                foreach (var (spread, a) in new[] { (36f, 0.05f), (20f, 0.07f) })
                    DrawColoredPolygon(new[] { c0 - new Vector2(p * 6, 0), c0 + new Vector2(p * 6, 0), new Vector2(W / 2 - side * W * 0.04f + p * spread, H), new Vector2(W / 2 - side * W * 0.04f - p * spread, H) }, A(lampC, a));
                foreach (var (g, a) in new[] { (14f, 0.05f), (9f, 0.08f), (5f, 0.16f) })
                    DrawColoredPolygon(Px.Ellipse(lamp.GetCenter(), lamp.Size.X / 2 + p * g, lamp.Size.Y / 2 + p * g, 24), A(lampC, a));
            }
            for (float y = lamp.End.Y; y < pitchY - p * 6; y += p * 3)
            {
                DrawRect(new Rect2(lx - p, y, p * 2, p * 3), A(ink));
                DrawRect(new Rect2(lx - p * 2, y + p * 2, p * 4, p), A(Px.Hex(0x2b2670)));
            }
            DrawRect(lamp.Grow(p), A(ink));
            for (float ly = lamp.Position.Y; ly < lamp.End.Y; ly += p)
                for (float lx2 = lamp.Position.X; lx2 < lamp.End.X; lx2 += p)
                {
                    int ix = (int)Mathf.Round((lx2 - lamp.Position.X) / p), iy = (int)Mathf.Round((ly - lamp.Position.Y) / p);
                    if (iy == 3) continue;
                    var c = (ix + iy) % 2 == 0 ? Px.Hex(0xfffbe6) : Px.Hex(0xffe680);
                    DrawRect(new Rect2(lx2, ly, p, p), A(lit ? c : c.Darkened(0.75f)));
                }
        }

        // The ball drops onto the centre spot and bounces to rest.
        var (lift, spin, squash) = Bounce(_t);
        float r = 11;
        float rest = spotY - r * p;
        float by = Snap(rest - lift * (rest + r * p * 2)), bx = cx0;
        // Its shadow grows as it falls.
        float near = 1 - lift;
        if (_t > 0.25)
        {
            var sh = Px.Ellipse(new Vector2(bx + p, spotY + p), r * p * (0.5f + 0.6f * near), p * (1.5f + 1.5f * near), 20);
            DrawColoredPolygon(sh, A(new Color(0, 0, 0, 0.25f + 0.3f * near)));
        }
        DrawBall(bx, by, r, p, spin, squash, A);
        // A dust puff at each landing.
        foreach (double land in new[] { 0.7, 1.08, 1.32 })
        {
            float k = (float)((_t - land) / 0.3);
            if (k <= 0 || k >= 1) continue;
            for (int i = 0; i < 6; i++)
            {
                float d = (i < 3 ? -1 : 1) * (r * p * 0.6f + k * p * (8 + 4 * (i % 3)));
                DrawRect(new Rect2(Snap(bx + d), Snap(spotY - p * (1 + (i % 3)) * k * 2), p, p), A(cream, (1 - k) * 0.7f));
            }
        }

        // Wordmark: slams in on the first landing, in the home page's gold with its magenta shadow.
        float slam = Ease(_t, 0.62, 0.22);
        if (slam > 0)
        {
            int size = (int)(p * 21);
            float scale = 1 + (1 - slam) * 0.6f;
            var at = new Vector2(W / 2, Snap(H * 0.29f));
            DrawSetTransform(at, 0, new Vector2(scale, scale));
            float flash = 1 - Ease(_t, 0.84, 0.3);
            float w = Px.Width(Px.Big, "GAMENIGHT", size);
            Px.Text(this, Px.Big, new Vector2(-w / 2 + p, p), "GAMENIGHT", size, A(Px.Hex(0x7a1a5c), slam));
            Px.Text(this, Px.Big, new Vector2(-w / 2 - p * 0.5f, 0), "GAMENIGHT", size, A(Px.Hex(0x0c0a24), slam));
            Px.Text(this, Px.Big, new Vector2(-w / 2, -p * 0.5f), "GAMENIGHT", size, A(Px.Gold.Lerp(Colors.White, flash * 0.8f), slam));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            float tag = Ease(_t, 0.9, 0.3);
            Px.TextC(this, Px.Small, W / 2, at.Y + p * 7, "FOOTBALL  ·  11 v 11", (int)(p * 2.5f), A(Px.InkDim, tag), A(ink, tag), p * 0.5f);

            // The scoreboard bar: segments tick across while the pre-match jobs roll by.
            float fill = Ease(_t, 0.7, Hold - 0.8);
            var bar = new Rect2(Snap(W / 2 - p * 36), at.Y + p * 11, p * 72, p * 3);
            DrawRect(bar.Grow(p), A(ink, tag));
            DrawRect(bar.Grow(p * 2), A(new Color(Px.Gold, 0.18f), tag));
            int segs = 18;
            for (int i = 0; i < segs; i++)
            {
                var seg = new Rect2(bar.Position.X + i * p * 4, bar.Position.Y, p * 3, p * 3);
                bool full = i < fill * segs;
                var c = full ? (i == (int)(fill * segs) - 1 ? Colors.White : i % 2 == 0 ? Px.Gold : Px.Gold2) : Px.Hex(0x2b2670);
                DrawRect(seg, A(c, tag));
            }
            string job = Jobs[Math.Min(Jobs.Length - 1, (int)(fill * Jobs.Length))];
            Px.TextC(this, Px.Small, W / 2, bar.End.Y + p * 5, job, (int)(p * 2), A(Px.Cyan, tag), A(ink, tag), p * 0.5f);
        }
    }

    /// <summary>A panelled football of radius r art pixels, lit from the floodlights, spun about a tilted axis.</summary>
    void DrawBall(float bx, float by, float r, float p, float spin, float squash, Func<Color, float, Color> A)
    {
        var basis = new Basis(new Vector3(0.3f, 1, 0.2f).Normalized(), spin) * new Basis(new Vector3(1, 0, 0), 0.35f);
        var panels = new Vector3[Panels.Length];
        for (int i = 0; i < Panels.Length; i++) panels[i] = basis * Panels[i];
        var light = new Vector3(-0.5f, -0.6f, 0.62f).Normalized();
        var black = Px.Hex(0x14121c);
        float sx = 1 + squash * 0.18f, sy = 1 - squash * 0.18f;
        int n = (int)Mathf.Ceil(r * sx) + 1;
        // Halo first, so the ball sits in the floodlight glow.
        for (int g = 3; g >= 1; g--)
            DrawColoredPolygon(Px.Ellipse(new Vector2(bx, by), (r + g * 1.2f) * p * sx, (r + g * 1.2f) * p * sy, 24), A(Px.Hex(0xfff0bf), 0.06f));
        for (int iy = -n; iy < n; iy++)
            for (int ix = -n; ix < n; ix++)
            {
                float dx = (ix + 0.5f) / (r * sx), dy = (iy + 0.5f) / (r * sy);
                float r2 = dx * dx + dy * dy;
                if (r2 > 1) continue;
                var nrm = new Vector3(dx, dy, Mathf.Sqrt(1 - r2));
                float best = -1;
                foreach (var v in panels) best = Mathf.Max(best, nrm.Dot(v));
                float lam = Mathf.Max(0, nrm.Dot(light));
                Color c;
                if (r2 > 0.86f) c = black;
                else if (best > 0.952f) c = lam > 0.5f ? Px.Hex(0x2a2638) : black;
                else c = Px.Hex(lam > 0.82f ? 0xffffff : lam > 0.55f ? 0xf2eee4 : lam > 0.25f ? 0xc9c3d6 : 0x8f87a8);
                DrawRect(new Rect2(bx + ix * p, by + iy * p, p, p), A(c, 1));
            }
        // Glint.
        DrawRect(new Rect2(bx - r * 0.45f * p, by - r * 0.55f * p, p * 2, p), A(Colors.White, 1));
        DrawRect(new Rect2(bx - r * 0.45f * p, by - r * 0.55f * p + p, p, p), A(Colors.White, 1));
    }
}
