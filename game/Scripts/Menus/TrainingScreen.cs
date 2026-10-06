using System;
using Godot;
using GameNight.Sim;

namespace GameNight.Menus;

/// <summary>
/// The training ground: the five drills as a centred row of cards over the training pitch at
/// dusk. Each card has a little live pixel scene of the drill, what it is, your best streak and
/// the medal it has earned (bronze, silver, gold), and a TRAIN button.
/// </summary>
public sealed partial class TrainingScreen : PxCanvas
{
    readonly Menus _ui;

    public TrainingScreen(Menus ui) => _ui = ui;

    /// <summary>Streaks for bronze, silver and gold, per drill.</summary>
    static int[] Medals(DrillKind k) => k switch
    {
        DrillKind.Penalties => new[] { 5, 10, 20 },
        DrillKind.TwoVTwo => new[] { 2, 4, 7 },
        _ => new[] { 3, 6, 10 },
    };

    static readonly Color[] MedalColor = { Px.Hex(0xd08a4a), Px.Hex(0xd9e1ef), Px.Hex(0xffd447) };
    static readonly string[] MedalName = { "BRONZE", "SILVER", "GOLD" };

    static int MedalOf(DrillKind k, int best)
    {
        var m = Medals(k);
        int got = -1;
        for (int i = 0; i < 3; i++) if (best >= m[i]) got = i;
        return got;
    }

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        Ground(W, H);
        BackButton(new Vector2(14, 12), () => _ui.Go(_ui.Home));
        // Heading, in the club studio's style.
        const string title = "TRAINING GROUND";
        Px.Text(this, Px.Big, new Vector2(66, 44), title, 36, Px.Ink, new Color(0, 0, 0, 0.55f), 3);
        float tw = Px.Width(Px.Big, title, 36);
        Px.Text(this, Px.Small, new Vector2(66 + tw + 14, 40), "PICK A DRILL · BEAT YOUR BEST STREAK", 8, Px.InkDim);
        DrawRect(new Rect2(66, 52, W - 66 - 16, 2), Px.Line);
        DrawRect(new Rect2(66, 52, tw, 2), Px.Gold);
        // Medals won so far.
        var won = new int[3];
        foreach (var d in Drill.All)
        {
            int m = MedalOf(d.Id, _ui.Club.DrillBest(d.Id));
            for (int i = 0; i <= m; i++) won[i]++;
        }
        float mx = W - 16;
        for (int i = 2; i >= 0; i--)
        {
            string s = $"{won[i]}/{Drill.All.Length}";
            float w = Px.Width(Px.Big, s, 18);
            Px.TextR(this, Px.Big, mx, 40, s, 18, won[i] > 0 ? Px.Ink : Px.InkDim);
            Medal(new Vector2(mx - w - 12, 33), 7, i, won[i] > 0);
            mx -= w + 34;
        }

        // The drills, centred.
        int n = Drill.All.Length;
        float gap = 12, top = 68, bottom = H - 30;
        float cw = Mathf.Min(176, (W - 32 - gap * (n - 1)) / n);
        float x0 = (W - (cw * n + gap * (n - 1))) / 2;
        for (int i = 0; i < n; i++)
            DrillCard(new Rect2(x0 + i * (cw + gap), top, cw, bottom - top), Drill.All[i], i);
        if (T % 1.4 < 1) Px.TextC(this, Px.Small, W / 2, H - 12, "PAUSE IN A DRILL TO RESTART OR LEAVE IT", 8, Px.InkDim);
        Px.Scanlines(this, new Rect2(0, 0, W, H));
    }

    /// <summary>The training pitch at dusk: sky bands, a tree line, a fence, mown grass.</summary>
    void Ground(float W, float H)
    {
        Px.Bands(this, new Rect2(0, 0, W, H * 0.62f), new[] { Px.Hex(0x0a0820), Px.Hex(0x15113a), Px.Hex(0x2a1850), Px.Hex(0x4a2160), Px.Hex(0x7a3466) }, new[] { 0, 0.3f, 0.6f, 0.82f, 0.94f });
        float hy = H * 0.62f;
        // Trees: a bumpy silhouette.
        for (float x = 0; x < W; x += 6)
        {
            float h = 14 + 8 * Mathf.Abs(Mathf.Sin(x * 0.037f)) + 6 * Mathf.Abs(Mathf.Sin(x * 0.11f + 1));
            DrawRect(new Rect2(x, hy - h, 6, h), Px.Hex(0x120c26));
        }
        DrawRect(new Rect2(0, hy, W, H - hy), Px.Hex(0x1f5a2c));
        for (int i = 0; i < 12; i++)
            DrawRect(new Rect2(i * W / 12f, hy + 10, W / 24f, H - hy - 10), Px.Hex(0x236433));
        // A low fence on the edge of the pitch.
        DrawRect(new Rect2(0, hy - 2, W, 2), Px.Hex(0x3a2f5c));
        for (float x = 4; x < W; x += 24) DrawRect(new Rect2(x, hy - 9, 2, 9), Px.Hex(0x3a2f5c));
        DrawRect(new Rect2(0, hy - 8, W, 1), Px.Hex(0x2e2550));
        DrawRect(new Rect2(0, 0, W, H), new Color(0.03f, 0.02f, 0.1f, 0.35f));
    }

    void DrillCard(Rect2 r, DrillInfo d, int index)
    {
        string key = "drill" + d.Id;
        bool held = Held(key);
        var rr = held ? r.Translated(new Vector2(3, 3)) : r;
        int best = _ui.Club.DrillBest(d.Id);
        int medal = MedalOf(d.Id, best);
        var ring = medal >= 0 ? MedalColor[medal] : Px.Line2;
        Px.Frame(this, rr, new Color(12 / 255f, 10 / 255f, 34 / 255f, 0.92f), new Color(ring, medal >= 0 ? 0.85f : 1), held ? null : Px.Shadow, 2, 6);
        var inner = rr.Grow(-4);

        // The scene.
        var art = new Rect2(inner.Position, new Vector2(inner.Size.X, Mathf.Round(inner.Size.Y * 0.5f)));
        Scene(art, d.Id, index);
        DrawRect(new Rect2(art.Position.X, art.End.Y, art.Size.X, 2), new Color(ring, 0.6f));
        // Number tag on the scene.
        var tag = new Rect2(art.Position + new Vector2(4, 4), new Vector2(18, 16));
        DrawRect(tag, new Color(0, 0, 0, 0.6f));
        Px.TextC(this, Px.Big, tag.GetCenter().X, tag.End.Y - 3, (index + 1).ToString(), 16, Px.Gold);

        float y = art.End.Y + 26;
        Px.Text(this, Px.Big, new Vector2(inner.Position.X + 6, y), Px.Fit(Px.Big, d.Name, 24, inner.Size.X - 12), 24, Px.Ink, new Color(0, 0, 0, 0.5f), 2);
        y += 14;
        foreach (var line in Px.Wrap(Px.Small, d.About.ToUpperInvariant(), 8, inner.Size.X - 12))
        {
            Px.Text(this, Px.Small, new Vector2(inner.Position.X + 6, y), line, 8, Px.InkDim);
            y += 11;
        }

        // Best streak and medal, above the button.
        var btn = new Rect2(inner.Position.X + 6, inner.End.Y - 34, inner.Size.X - 12, 30);
        float sy = btn.Position.Y - 34;
        DrawRect(new Rect2(inner.Position.X + 6, sy - 4, inner.Size.X - 12, 1), Px.Line);
        Medal(new Vector2(inner.Position.X + 18, sy + 12), 9, Math.Max(0, medal), medal >= 0);
        Px.Text(this, Px.Big, new Vector2(inner.Position.X + 34, sy + 12), best > 0 ? best.ToString() : "-", 22, best > 0 ? Px.Ink : Px.InkDim);
        Px.Text(this, Px.Small, new Vector2(inner.Position.X + 34, sy + 24), "BEST STREAK", 7, Px.InkDim);
        var m = Medals(d.Id);
        string next = medal >= 2 ? "TOP MEDAL" : $"{MedalName[medal + 1]} AT {m[medal + 1]}";
        Px.TextR(this, Px.Small, inner.End.X - 6, sy + 24, next, 7, medal >= 2 ? Px.Gold : MedalColor[medal + 1]);
        if (medal >= 0) Px.TextR(this, Px.Small, inner.End.X - 6, sy + 10, MedalName[medal], 8, MedalColor[medal]);
        // TRAIN.
        bool bh = held;
        var b = bh ? btn : btn;
        Px.BandFrame(this, b, Px.GoldBands, Px.GoldStops, Px.Hex(0x7a4a00));
        Px.TextC(this, Px.Big, b.GetCenter().X, b.GetCenter().Y + 7, "TRAIN", 20, Px.Dark);
        Tap(key, r, () => _ui.App.StartDrill(d.Id));
    }

    /// <summary>A round medal on a ribbon; dim when not yet won.</summary>
    void Medal(Vector2 c, float rad, int tier, bool won)
    {
        var col = won ? MedalColor[tier] : Px.Hex(0x3a3560);
        DrawColoredPolygon(new[] { c + new Vector2(-rad * 0.8f, -rad * 1.6f), c + new Vector2(-rad * 0.1f, -rad * 1.6f), c + new Vector2(rad * 0.25f, -rad * 0.5f), c + new Vector2(-rad * 0.45f, -rad * 0.5f) }, won ? Px.Neon : Px.Hex(0x2a2550));
        DrawColoredPolygon(new[] { c + new Vector2(rad * 0.8f, -rad * 1.6f), c + new Vector2(rad * 0.1f, -rad * 1.6f), c + new Vector2(-rad * 0.25f, -rad * 0.5f), c + new Vector2(rad * 0.45f, -rad * 0.5f) }, won ? Px.Cyan : Px.Hex(0x2a2550));
        DrawColoredPolygon(Px.Ellipse(c, rad + 1, rad + 1, 14), Px.Hex(0x14121c));
        DrawColoredPolygon(Px.Ellipse(c, rad, rad, 14), col);
        DrawColoredPolygon(Px.Ellipse(c, rad * 0.6f, rad * 0.6f, 12), col.Darkened(0.18f));
        if (won) DrawRect(new Rect2(c - new Vector2(rad * 0.5f, rad * 0.6f), new Vector2(2, 2)), new Color(1, 1, 1, 0.8f));
    }

    // ---------------------------------------------------------------- the little scenes

    /// <summary>A small footballer: head, shirt, shorts, legs. (x, feet) is where he stands.</summary>
    void Fig(float x, float feet, float s, Color shirt, Color shorts, float lean = 0, bool armsUp = false)
    {
        var skin = Px.Hex(0xc98e62);
        DrawRect(new Rect2(x - s * 0.9f + lean * s, feet - s * 3.2f, s * 1.8f, s * 1.6f), shirt);
        if (armsUp)
        {
            DrawRect(new Rect2(x - s * 1.7f + lean * s, feet - s * 4.4f, s * 0.7f, s * 1.6f), shirt);
            DrawRect(new Rect2(x + s * 1.0f + lean * s, feet - s * 4.4f, s * 0.7f, s * 1.6f), shirt);
        }
        DrawRect(new Rect2(x - s * 0.6f + lean * s * 1.4f, feet - s * 4.3f, s * 1.2f, s * 1.1f), skin);
        DrawRect(new Rect2(x - s * 0.8f + lean * s * 0.6f, feet - s * 1.7f, s * 1.6f, s * 0.7f), shorts);
        DrawRect(new Rect2(x - s * 0.7f, feet - s, s * 0.5f, s), skin);
        DrawRect(new Rect2(x + s * 0.2f, feet - s, s * 0.5f, s), skin);
    }

    void Ball(Vector2 c, float rad)
    {
        DrawColoredPolygon(Px.Ellipse(c, rad, rad, 10), Px.Ink);
        DrawRect(new Rect2(c - new Vector2(rad * 0.3f, rad * 0.3f), new Vector2(rad * 0.6f, rad * 0.6f)), Px.Hex(0x14121c));
    }

    /// <summary>A goal seen from the front: posts, bar and a netted back.</summary>
    Rect2 Goal(Rect2 a, float w, float h, float y)
    {
        var g = new Rect2(a.GetCenter().X - w / 2, y, w, h);
        DrawRect(g, new Color(0, 0, 0, 0.3f));
        for (float x = g.Position.X + 4; x < g.End.X; x += 5) DrawRect(new Rect2(x, g.Position.Y, 1, g.Size.Y), new Color(1, 1, 1, 0.16f));
        for (float yy = g.Position.Y + 4; yy < g.End.Y; yy += 5) DrawRect(new Rect2(g.Position.X, yy, g.Size.X, 1), new Color(1, 1, 1, 0.16f));
        DrawRect(new Rect2(g.Position.X - 2, g.Position.Y - 2, g.Size.X + 4, 3), Px.Ink);
        DrawRect(new Rect2(g.Position.X - 2, g.Position.Y, 3, g.Size.Y), Px.Ink);
        DrawRect(new Rect2(g.End.X - 1, g.Position.Y, 3, g.Size.Y), Px.Ink);
        return g;
    }

    void Scene(Rect2 a, DrillKind kind, int index)
    {
        // Pitch under a dusk sky, floodlit.
        float hy = a.Position.Y + a.Size.Y * 0.36f;
        Px.Bands(this, new Rect2(a.Position, new Vector2(a.Size.X, hy - a.Position.Y)), new[] { Px.Hex(0x1a1240), Px.Hex(0x3b1d68), Px.Hex(0x6a2a6a) }, new[] { 0, 0.5f, 0.85f });
        DrawRect(new Rect2(a.Position.X, hy, a.Size.X, a.End.Y - hy), Px.Hex(0x2f7a3c));
        for (int i = 0; i < 4; i++)
        {
            float y0 = hy + (a.End.Y - hy) * i / 4f;
            if (i % 2 == 1) DrawRect(new Rect2(a.Position.X, y0, a.Size.X, (a.End.Y - hy) / 4f), Px.Hex(0x37874a));
        }
        float t = (float)((T + index * 0.37) % 2.4 / 2.4);
        float s = Mathf.Max(2, Mathf.Round(a.Size.Y / 26f));
        var red = Px.Hex(_ui.Club.S.Kit.Main);
        var redShorts = Px.Hex(_ui.Club.S.Kit.Secondary);
        var blue = Px.Hex(0x3a5bd9);
        var keeper = Px.Hex(0x2fd06a);
        float cx = a.GetCenter().X;
        switch (kind)
        {
            case DrillKind.FreeKicks:
            {
                var g = Goal(a, a.Size.X * 0.5f, a.Size.Y * 0.28f, hy - a.Size.Y * 0.2f);
                Fig(cx + 2, g.End.Y - 1, s * 0.7f, keeper, Px.Hex(0x14121c));
                for (int i = 0; i < 4; i++) Fig(cx - 18 + i * 9, hy + a.Size.Y * 0.3f, s * 0.9f, blue, Px.Hex(0x14121c), 0, true);
                // The curler: over the wall, into the top corner.
                var from = new Vector2(cx - a.Size.X * 0.12f, a.End.Y - 8);
                var to = new Vector2(g.End.X - 6, g.Position.Y + 4);
                var ctrl = new Vector2(cx - a.Size.X * 0.42f, hy - 4);
                for (int i = 0; i < 10; i++)
                {
                    float k = i / 10f;
                    if (k > t) break;
                    DrawRect(new Rect2(Bez(from, ctrl, to, k), new Vector2(2, 2)), new Color(1, 1, 1, 0.5f));
                }
                Ball(Bez(from, ctrl, to, t), 3 + (1 - t) * 1.5f);
                break;
            }
            case DrillKind.Penalties:
            {
                var g = Goal(a, a.Size.X * 0.66f, a.Size.Y * 0.34f, hy - a.Size.Y * 0.22f);
                float dive = t > 0.55f ? Mathf.Min(1, (t - 0.55f) / 0.2f) : 0;
                Fig(cx - dive * g.Size.X * 0.28f, g.End.Y - 1 - dive * 6, s * 0.9f, keeper, Px.Hex(0x14121c), -dive * 2, true);
                var spot = new Vector2(cx, a.End.Y - 10);
                DrawRect(new Rect2(spot - new Vector2(2, -4), new Vector2(4, 2)), Px.Ink);
                var aim = new Vector2(g.End.X - 8, g.End.Y - 8);
                Ball(t < 0.5f ? spot : spot.Lerp(aim, Mathf.Min(1, (t - 0.5f) / 0.25f)), t < 0.5f ? 4 : 3);
                break;
            }
            case DrillKind.OneOnOne:
            {
                var g = Goal(a, a.Size.X * 0.5f, a.Size.Y * 0.26f, hy - a.Size.Y * 0.18f);
                float k = 0.5f + 0.5f * Mathf.Sin(t * Mathf.Tau);
                Fig(cx + 6, g.End.Y + 10 + k * 6, s * 0.9f, keeper, Px.Hex(0x14121c), 0, true);
                float px = cx - a.Size.X * 0.2f + k * 10, py = a.End.Y - 4;
                Fig(px, py, s, red, redShorts, 0.4f);
                for (int i = 1; i <= 3; i++) DrawRect(new Rect2(px - s * 2 - i * 5, py - s * 2.5f + i, 4, 1), new Color(1, 1, 1, 0.35f));
                Ball(new Vector2(px + s * 2, py - 3), 3.5f);
                break;
            }
            case DrillKind.TwoVTwo:
            {
                Goal(a, a.Size.X * 0.42f, a.Size.Y * 0.22f, hy - a.Size.Y * 0.14f);
                float k = 0.5f + 0.5f * Mathf.Sin(t * Mathf.Tau);
                var p1 = new Vector2(cx - a.Size.X * 0.24f, a.End.Y - 6);
                var p2 = new Vector2(cx + a.Size.X * 0.2f, hy + a.Size.Y * 0.36f);
                Fig(p1.X, p1.Y, s, red, redShorts, 0.3f);
                Fig(p2.X, p2.Y, s, red, redShorts, -0.3f);
                Fig(cx - a.Size.X * 0.06f, hy + a.Size.Y * 0.32f, s, blue, Px.Hex(0x14121c));
                Fig(cx + a.Size.X * 0.32f, hy + a.Size.Y * 0.18f, s, blue, Px.Hex(0x14121c));
                Ball(p1.Lerp(p2 + new Vector2(-6, -2), k) + new Vector2(6, -3), 3);
                break;
            }
            case DrillKind.Keeper:
            {
                var g = Goal(a, a.Size.X * 0.8f, a.Size.Y * 0.5f, hy - a.Size.Y * 0.26f);
                float dive = 0.5f + 0.5f * Mathf.Sin(t * Mathf.Tau);
                // You, flying across the goal, gloves first.
                float kx = Mathf.Lerp(cx - 4, g.End.X - 22, dive), ky = g.End.Y - 2 - dive * g.Size.Y * 0.45f;
                Fig(kx, ky, s, keeper, Px.Hex(0x14121c), 1.2f * dive, true);
                DrawRect(new Rect2(kx + s * (1.2f + dive), ky - s * 4.8f, s * 1.1f, s * 1.1f), Px.Hex(0xfff3c0));
                Ball(new Vector2(g.End.X - 10, g.Position.Y + g.Size.Y * 0.25f).Lerp(new Vector2(cx, a.End.Y - 4), 1 - dive), 3.5f);
                break;
            }
        }
        // Floodlight wash and a frame.
        Fx.Spot(this, new Vector2(cx, a.Position.Y), new Vector2(cx, a.End.Y), a.Size.X * 0.2f, a.Size.X * 0.9f, Px.Hex(0xfff0bf), 0.05f);
    }

    static Vector2 Bez(Vector2 a, Vector2 c, Vector2 b, float t) => a.Lerp(c, t).Lerp(c.Lerp(b, t), t);
}
