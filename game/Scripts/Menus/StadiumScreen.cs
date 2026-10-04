using System;
using System.Collections.Generic;
using Godot;
using GameNight.Grounds.Build;

namespace GameNight.Menus;

/// <summary>
/// The stadium builder: a plan of the ground with its eight places (the main stand, both ends,
/// the near side, the four corners), and the five stand sets to build each from. The stadium
/// itself stands behind the screen, built as you pick, the camera circling round to the stand
/// you're choosing. Picks are saved with the club; "Your stadium" is then a ground to play at.
/// </summary>
public sealed partial class StadiumScreen : PxCanvas
{
    readonly Menus _ui;
    public Slot Selected = Slot.Main;
    double _changedAt = -1;
    bool _building;

    public StadiumScreen(Menus ui)
    {
        _ui = ui;
    }

    StadiumPlan Plan => _ui.Club.S.Stadium;

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!IsVisibleInTree()) return;
        // Rebuild once the picks settle (building takes a moment): first a frame saying so.
        if (_building)
        {
            _building = false;
            _ui.App.StadiumChanged();
        }
        else if (_changedAt >= 0 && T - _changedAt > 0.35)
        {
            _changedAt = -1;
            _building = true;
        }
    }

    void Pick(Slot s)
    {
        Selected = s;
        _ui.App.StadiumFocus(s);
    }

    void Choose(int set)
    {
        if (Plan.Get(Selected) == set) return;
        _ui.Club.SetStadium(Selected, set);
        _changedAt = T;
    }

    void All(int set)
    {
        foreach (Slot s in Enum.GetValues<Slot>()) _ui.Club.S.Stadium.Set(s, set);
        _ui.Club.SetStadium(Selected, set);
        _changedAt = T;
    }

    void Surprise()
    {
        var rng = new Random();
        foreach (Slot s in Enum.GetValues<Slot>()) Plan.Set(s, rng.Next(Kit.Sets.Length));
        _ui.Club.SetStadium(Selected, Plan.Get(Selected));
        _changedAt = T;
    }

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        // The stadium shows through; shade the panels' side so they read.
        DrawRect(new Rect2(0, 0, 340, H), new Color(14 / 255f, 10 / 255f, 40 / 255f, 0.45f));
        DrawRect(new Rect2(0, H - 110, W, 110), new Color(14 / 255f, 10 / 255f, 40 / 255f, 0.35f));

        BackButton(new Vector2(16, 12), () => _ui.Go(_ui.Home));
        Title(new Vector2(66, 44), "STADIUM");
        Px.Text(this, Px.Small, new Vector2(68, 60), "PICK A STAND FOR EACH PLACE", 8, Px.Cyan);

        var panel = new Rect2(16, 72, 308, H - 72 - 16);
        Px.Frame(this, panel, Px.Glass, Px.Line2, Px.ShadowSoft);
        PlanMap(new Rect2(panel.Position + new Vector2(14, 12), new Vector2(panel.Size.X - 28, Mathf.Min(176, panel.Size.Y * 0.56f))));
        var set = Kit.Sets[Plan.Get(Selected)];
        float ty = panel.Position.Y + 12 + Mathf.Min(176, panel.Size.Y * 0.56f) + 30;
        Px.Text(this, Px.Small, new Vector2(panel.Position.X + 16, ty - 12), Kit.SlotNames[(int)Selected].ToUpperInvariant(), 8, Px.InkDim);
        Px.Text(this, Px.Big, new Vector2(panel.Position.X + 16, ty + 14), set.Name, 28, Px.Hex((int)set.Swatch), new Color(0, 0, 0, 0.5f), 2);
        float ly = ty + 32;
        foreach (var l in Px.Wrap(Px.Small, set.About, 8, panel.Size.X - 32))
        {
            if (ly > panel.End.Y - 6) break;
            Px.Text(this, Px.Small, new Vector2(panel.Position.X + 16, ly), l, 8, Px.Ink);
            ly += 13;
        }

        // Top right: play here, and the quick picks.
        GoldButton("play", new Rect2(W - 16 - 190, 14, 190, 42), "PLAY HERE  >", 26, () => _ui.App.PlayAt("custom"));
        float cx = W - 16 - 190 - 14;
        float w2 = Px.Width(Px.Big, "SURPRISE ME", 18) + 18;
        Chip("surprise", new Vector2(cx - w2, 22), "SURPRISE ME", false, Surprise);
        cx -= w2 + 10;
        float w1 = Px.Width(Px.Big, "SAME ALL ROUND", 18) + 18;
        Chip("same", new Vector2(cx - w1, 22), "SAME ALL ROUND", false, () => All(Plan.Get(Selected)));
        if (_building || _changedAt >= 0)
            Px.TextR(this, Px.Big, W - 20, 82, (T % 0.6) < 0.3 ? "BUILDING..." : "BUILDING", 22, Px.Gold, new Color(0, 0, 0, 0.6f), 2);

        // The five sets along the bottom.
        float x0 = 340, x1 = W - 16, y0 = H - 96, gap = 10;
        float cw = (x1 - x0 - gap * (Kit.Sets.Length - 1)) / Kit.Sets.Length;
        for (int i = 0; i < Kit.Sets.Length; i++)
        {
            int idx = i;
            var s = Kit.Sets[i];
            var r = new Rect2(x0 + i * (cw + gap), y0, cw, 80);
            bool on = Plan.Get(Selected) == i;
            bool held = Held("set" + i);
            var rr = held ? new Rect2(r.Position + new Vector2(2, 2), r.Size) : r;
            Px.Frame(this, rr, on ? Px.Gold : Px.Glass2, on ? Px.Hex(0xb37400) : Px.Line2, held ? null : Px.ShadowSoft);
            Swatch(new Rect2(rr.Position + new Vector2(10, 10), new Vector2(rr.Size.X - 20, 30)), i);
            Px.TextC(this, Px.Big, rr.GetCenter().X, rr.End.Y - 14, s.Name, 22, on ? Px.Dark : Px.Ink);
            Tap("set" + i, r, () => Choose(idx));
        }
    }

    /// <summary>A little elevation of a set: its silhouette in its colour.</summary>
    void Swatch(Rect2 r, int set)
    {
        var c = Px.Hex((int)Kit.Sets[set].Swatch);
        var dark = c.Darkened(0.45f);
        DrawRect(r, new Color(0.05f, 0.04f, 0.14f, 0.85f));
        float x = r.Position.X, y = r.End.Y, w = r.Size.X, h = r.Size.Y;
        Vector2 P(float fx, float fy) => new(Mathf.Round(x + fx * w), Mathf.Round(y - fy * h));
        switch (set)
        {
            case 0: // a bowl under a sweeping roof
                DrawColoredPolygon(new[] { P(0, 0), P(0, 0.2f), P(0.55f, 0.62f), P(1, 0.62f), P(1, 0) }, c);
                DrawColoredPolygon(new[] { P(0.1f, 0.86f), P(1, 0.72f), P(1, 0.8f), P(0.1f, 0.92f) }, dark);
                break;
            case 1: // a low terrace under a pitched roof
                DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.9f, 0.4f), P(0.9f, 0) }, c);
                DrawColoredPolygon(new[] { P(0.05f, 0.55f), P(0.95f, 0.66f), P(0.95f, 0.6f), P(0.05f, 0.5f) }, dark);
                for (int i = 1; i < 4; i++) DrawRect(new Rect2(P(0.08f + i * 0.2f, 0.52f + i * 0.02f), new Vector2(2, h * 0.3f)), dark);
                break;
            case 2: // two open tiers and a ramp tower
                DrawColoredPolygon(new[] { P(0, 0), P(0, 0.15f), P(0.4f, 0.4f), P(0.4f, 0.5f), P(0.78f, 0.78f), P(0.82f, 0.78f), P(0.82f, 0) }, c);
                DrawRect(new Rect2(P(0.88f, 0.95f), new Vector2(Mathf.Max(3, w * 0.06f), h * 0.95f)), dark);
                break;
            case 3: // one steep wall and its pylon
                DrawColoredPolygon(new[] { P(0, 0), P(0, 0.1f), P(0.68f, 0.82f), P(0.72f, 0.82f), P(0.72f, 0) }, c);
                DrawColoredPolygon(new[] { P(0.12f, 0.88f), P(0.78f, 0.88f), P(0.78f, 0.82f), P(0.12f, 0.82f) }, dark);
                DrawRect(new Rect2(P(0.8f, 1), new Vector2(Mathf.Max(3, w * 0.05f), h)), c.Lightened(0.2f));
                break;
            default: // walls, battlements and a keep
                DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.62f, 0.55f), P(0.62f, 0.6f), P(0.7f, 0.6f), P(0.7f, 0) }, c);
                for (int i = 0; i < 4; i++) DrawRect(new Rect2(P(0.6f + (i % 2) * 0.06f, 0.68f), new Vector2(3, 3)), c);
                DrawRect(new Rect2(P(0.78f, 0.78f), new Vector2(w * 0.14f, h * 0.78f)), c);
                DrawColoredPolygon(new[] { P(0.76f, 0.78f), P(0.94f, 0.78f), P(0.85f, 1) }, Px.Hex(_ui.Club.S.Kit.Main));
                break;
        }
    }

    /// <summary>The ground from above: the pitch and the eight places round it, each in its
    /// set's colour; tap one to choose its stand.</summary>
    void PlanMap(Rect2 r)
    {
        // World x -84..84, z -66..66 into the box (the far side at the top).
        float k = Mathf.Min(r.Size.X / 168f, r.Size.Y / 132f);
        var c = r.GetCenter();
        Vector2 M(float x, float z) => (c + new Vector2(x, z) * k).Round();
        const float D = 18; // depth drawn
        var pitch = new Rect2(M(-52.5f, -34), new Vector2(105, 68) * k);
        DrawRect(pitch.Grow(3 * k), Px.Hex(0x1f6e34));
        DrawRect(pitch, Px.Hex(0x2f8f45));
        DrawRect(new Rect2(M(-0.4f, -34), new Vector2(Mathf.Max(1, 0.8f * k), 68 * k)), new Color(1, 1, 1, 0.6f));
        foreach (int sx in new[] { -1, 1 })
            Px.Ring(this, new Rect2(M(sx < 0 ? -52.5f : 52.5f - 16.5f, -20.2f), new Vector2(16.5f, 40.4f) * k), new Color(1, 1, 1, 0.5f), 1);

        float bx = Kit.BX, bz = Kit.BZ, cx = Kit.CX, cz = Kit.CZ;
        void Piece(Slot s, Vector2[] poly, Rect2 hit)
        {
            var set = Kit.Sets[Plan.Get(s)];
            bool sel = s == Selected;
            var col = Px.Hex((int)set.Swatch);
            if (!sel) col = col.Darkened(0.25f);
            DrawColoredPolygon(poly, col);
            if (sel)
            {
                var ring = (T % 0.8) < 0.4 ? Px.Ink : Px.Gold;
                var loop = new Vector2[poly.Length + 1];
                poly.CopyTo(loop, 0);
                loop[^1] = poly[0];
                DrawPolyline(loop, ring, 3);
            }
            Tap("slot" + (int)s, hit, () => Pick(s));
        }
        Vector2[] Box(float x0, float z0, float x1, float z1) => new[] { M(x0, z0), M(x1, z0), M(x1, z1), M(x0, z1) };
        Rect2 Hit(float x0, float z0, float x1, float z1) { var a = M(x0, z0); var b = M(x1, z1); return new Rect2(a, b - a).Abs().Grow(4); }
        Vector2[] Quarter(float ox, float oz, float a0)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i <= 6; i++) { float a = a0 + i / 6f * Mathf.Pi / 2; pts.Add(M(ox + Mathf.Cos(a) * Kit.R, oz + Mathf.Sin(a) * Kit.R)); }
            for (int i = 6; i >= 0; i--) { float a = a0 + i / 6f * Mathf.Pi / 2; pts.Add(M(ox + Mathf.Cos(a) * (Kit.R + D), oz + Mathf.Sin(a) * (Kit.R + D))); }
            return pts.ToArray();
        }
        Piece(Slot.Main, Box(-cx, -bz - D, cx, -bz), Hit(-cx, -bz - D, cx, -bz));
        Piece(Slot.Near, Box(-cx, bz, cx, bz + D), Hit(-cx, bz, cx, bz + D));
        Piece(Slot.Home, Box(-bx - D, -cz, -bx, cz), Hit(-bx - D, -cz, -bx, cz));
        Piece(Slot.Away, Box(bx, -cz, bx + D, cz), Hit(bx, -cz, bx + D, cz));
        float o = Kit.R + D;
        Piece(Slot.HomeFar, Quarter(-cx, -cz, Mathf.Pi), Hit(-cx - o, -cz - o, -cx, -cz));
        Piece(Slot.AwayFar, Quarter(cx, -cz, 1.5f * Mathf.Pi), Hit(cx, -cz - o, cx + o, -cz));
        Piece(Slot.AwayNear, Quarter(cx, cz, 0), Hit(cx, cz, cx + o, cz + o));
        Piece(Slot.HomeNear, Quarter(-cx, cz, 0.5f * Mathf.Pi), Hit(-cx - o, cz, -cx, cz + o));
        Px.TextC(this, Px.Small, M(0, -bz - D / 2).X, M(0, -bz - D / 2).Y + 4, "MAIN", 8, Px.Dark);
        Px.TextC(this, Px.Small, M(0, bz + D / 2).X, M(0, bz + D / 2).Y + 4, "NEAR SIDE", 8, Px.Dark);
        Px.TextC(this, Px.Small, M(-bx - D / 2, 0).X, M(-bx - D / 2, 0).Y + 4, "HOME", 8, Px.Dark);
        Px.TextC(this, Px.Small, M(bx + D / 2, 0).X, M(bx + D / 2, 0).Y + 4, "AWAY", 8, Px.Dark);
    }
}
