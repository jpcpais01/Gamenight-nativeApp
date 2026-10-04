using System;
using Godot;

namespace GameNight.Menus;

/// <summary>
/// The launch screen (the PWA's boot screen in index.html), in whole art pixels: it starts as
/// the app icon dead centre on the night colour, then the icon hops up, the wordmark and a
/// segmented loading bar drop in, and the ground switches on around it: floodlights, a stand of
/// crowd lights, the pitch. Motion is stepped, like a cartridge. Then it steps away.
/// </summary>
public sealed partial class Splash : Control
{
    const double Hold = 2.1, Fade = 0.4;
    double _t;
    bool _leaving;
    double _leaveAt;
    Texture2D _icon;
    public event Action Gone;

    static readonly Vector2[] Stars =
    {
        new(0.06f, 0.09f), new(0.19f, 0.04f), new(0.31f, 0.14f), new(0.46f, 0.06f), new(0.58f, 0.17f), new(0.72f, 0.05f), new(0.84f, 0.13f),
        new(0.93f, 0.07f), new(0.12f, 0.24f), new(0.88f, 0.26f), new(0.03f, 0.18f), new(0.25f, 0.09f), new(0.39f, 0.03f), new(0.52f, 0.12f),
        new(0.65f, 0.09f), new(0.78f, 0.2f), new(0.97f, 0.15f), new(0.35f, 0.25f), new(0.62f, 0.26f),
    };

    public Splash()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        _icon = ResourceLoader.Exists("res://icon.svg") ? GD.Load<Texture2D>("res://icon.svg") : null;
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

    static float Step(double t, double start, double dur, int steps) => Mathf.Floor(Mathf.Clamp((float)((t - start) / dur), 0, 1) * steps) / steps;

    public override void _Draw()
    {
        float W = Size.X, H = Size.Y;
        // One art pixel.
        float p = Mathf.Max(2, Mathf.Floor(Mathf.Min(W, H) / 100f));
        float alpha = _leaving ? 1 - Step(_t, _leaveAt, Fade, 4) : 1;
        var night = Px.Night;
        DrawRect(new Rect2(0, 0, W, H), new Color(night, alpha));
        if (alpha <= 0) return;
        var ink = Px.Hex(0x170f2c);
        var cream = Px.Hex(0xf7f3e8);
        Color A(Color c, float a = 1) => new(c, c.A * a * alpha);

        // The ground, switched on around the logo.
        float on = Step(_t, 0.35, 0.6, 4);
        if (on > 0)
        {
            bool twinkle = (int)(_t / 0.8) % 2 == 0;
            for (int i = 0; i < Stars.Length; i++)
            {
                var c = i % 3 == 0 ? Px.Hex(0xa99fc6) : Px.Hex(0xfffbe6);
                float a = (i % 2 == 0) == twinkle ? 1 : 0.25f;
                DrawRect(new Rect2(Stars[i].X * W, Stars[i].Y * H, p, p), A(c, on * a));
            }
            // A night sky warming in hard bands toward the lit ground.
            var glow = new Rect2(0, H * 0.46f, W, H * 0.34f);
            Px.Bands(this, glow, new[] { A(night, 0), A(Px.Hex(0x1a1240), on), A(Px.Hex(0x26175a), on), A(Px.Hex(0x3b1d68), on), A(Px.Hex(0x5c2470), on), A(Px.Hex(0x8a3170), on) },
                new[] { 0, 0.2f, 0.42f, 0.62f, 0.8f, 0.92f });
            // The stand: stepped silhouette with crowd lights.
            float sy = H * 0.77f, sh = H * 0.12f;
            DrawRect(new Rect2(0, sy + sh * 0.45f, W, sh * 0.55f), A(ink, on));
            DrawRect(new Rect2(W * 0.06f, sy + sh * 0.3f, W * 0.88f, sh * 0.7f), A(ink, on));
            DrawRect(new Rect2(W * 0.14f, sy + sh * 0.15f, W * 0.72f, sh * 0.85f), A(ink, on));
            float crowd = Step(_t, 1.25, 0.3, 2);
            if (crowd > 0)
            {
                bool flick = (int)(_t / 0.45) % 2 == 0;
                int n = 0;
                for (float cy = sy + sh * 0.4f; cy < sy + sh * 0.85f; cy += p * 3)
                    for (float cx = W * 0.14f + p; cx < W * 0.86f; cx += p * 4, n++)
                    {
                        if ((n * 7919) % 5 > 1) continue;
                        var c = n % 3 == 0 ? Px.Hex(0xfff0bf) : Px.Hex(0xa99fc6);
                        DrawRect(new Rect2(cx, cy, p, p), A(c, crowd * (flick == (n % 2 == 0) ? 1 : 0.55f)));
                    }
            }
            // The pitch.
            float py = H * 0.89f;
            for (float x = 0, i = 0; x < W; x += p * 10, i++) DrawRect(new Rect2(x, py, p * 10, H - py), A(i % 2 == 0 ? Px.Hex(0x2f7a3c) : Px.Hex(0x3d9447), on));
            DrawRect(new Rect2(0, py, W, p), A(cream, on));
            DrawRect(new Rect2(0, py + (H - py) * 0.7f, W, (H - py) * 0.3f), A(new Color(ink, 0.35f), on));
            // Floodlight pylons: a lattice mast and a lamp bank that flickers on.
            foreach (var (x0, delay) in new[] { (W * 0.06f, 0.7), (W * 0.94f - p * 14, 0.95) })
            {
                float top = H * 0.89f - H * 0.58f;
                for (float y = top + p * 6; y < H * 0.89f; y += p * 3)
                {
                    DrawRect(new Rect2(x0 + p * 6, y, p * 2, p * 2), A(ink, on));
                    DrawRect(new Rect2(x0 + p * 6, y + p * 2, p * 2, p), A(Px.Hex(0x24183f), on));
                }
                double lt = _t - delay;
                bool lit = lt > 0.225 || (lt > 0 && lt < 0.11);
                var lamp = new Rect2(x0, top, p * 14, p * 7);
                if (lit)
                {
                    foreach (var (g, a) in new[] { (9f, 0.06f), (5f, 0.13f), (2f, 0.3f) })
                        DrawRect(lamp.Grow(p * g), A(Px.Hex(0xfff0bf), a * on));
                    // The cone of light.
                    var c0 = lamp.GetCenter() + new Vector2(0, p * 3.5f);
                    float side = x0 < W / 2 ? 1 : -1;
                    DrawColoredPolygon(new[] { c0 - new Vector2(p * 4, 0), c0 + new Vector2(p * 4, 0), c0 + new Vector2(side * W * 0.18f + p * 30, H), c0 + new Vector2(side * W * 0.18f - p * 30, H) }, A(Px.Hex(0xfff0bf), 0.07f * on));
                }
                DrawRect(lamp, A(ink, on));
                for (float ly = lamp.Position.Y + p; ly < lamp.End.Y - p; ly += p)
                    for (float lx = lamp.Position.X + p; lx < lamp.End.X - p; lx += p)
                    {
                        bool chk = ((int)((lx - lamp.Position.X) / p) + (int)((ly - lamp.Position.Y) / p)) % 2 == 0;
                        var c = chk ? Px.Hex(0xfffbe6) : Px.Hex(0xfff0bf);
                        DrawRect(new Rect2(lx, ly, p, p), A(lit ? c : c.Darkened(0.7f), on));
                    }
            }
        }

        // The logo block: the icon starts dead centre and hops up.
        float hop = Step(_t, 0.3, 0.4, 4);
        float iconS = p * 32;
        var ic = new Vector2(W / 2, H / 2 - hop * p * 15.5f - p * 8);
        var iconR = new Rect2(ic - new Vector2(iconS, iconS) / 2, new Vector2(iconS, iconS));
        DrawRect(iconR.Grow(p), A(ink));
        if (_icon != null) DrawTextureRect(_icon, iconR, false, A(Colors.White));
        float word = Step(_t, 0.55, 0.3, 3);
        if (word > 0)
        {
            int size = (int)(p * 15);
            float y = iconR.End.Y + p * 4 + size * 0.75f - (1 - word) * p * 6;
            Px.TextC(this, Px.Big, W / 2, y, "GAMENIGHT", size, A(Px.Gold, word), A(ink, word), p);
            // The segmented loading bar fills in steps.
            float fill = Step(_t, 0.7, 1.2, 10);
            var bar = new Rect2(W / 2 - p * 30, y + p * 5, p * 60, p * 4);
            DrawRect(bar.Grow(p), A(ink, word));
            for (int i = 0; i < 10; i++)
            {
                var seg = new Rect2(bar.Position.X + i * p * 6 + p * 0.5f, bar.Position.Y, p * 5, p * 4);
                DrawRect(seg, A(i < fill * 10 ? Px.Gold : Px.Hex(0x2b2670), word));
            }
        }
    }
}
