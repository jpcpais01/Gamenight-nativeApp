using System;
using System.Collections.Generic;
using Godot;
using GameNight.Grounds.Build;

namespace GameNight.Menus;

/// <summary>
/// The stadium builder: a plan of the ground with its eight places (the main stand, both ends,
/// the near side, the four corners), and the stand sets to build each from (each in a colour of the club's choosing). The stadium
/// itself stands behind the screen, built as you pick, the camera circling round to the stand
/// you're choosing (or dragged round by hand). Picks are saved with the club; "Your stadium" is then a ground to play at.
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

    // Fingers on the stadium: one drag turns round it, two slide across it and pinch to zoom.
    readonly Dictionary<int, Vector2> _touch = new();
    bool _orbiting, _two, _gesture;
    // A plain tap on the stadium hides the menus (the stadium alone, centred); another brings them back.
    bool _bare;
    double _bareAt;
    public bool Bare => _bare;

    public override void _Notification(int what)
    {
        base._Notification(what);
        // Always open with the menus showing.
        if (what == NotificationVisibilityChanged && IsVisibleInTree() && _bare)
        {
            _bare = false;
            _ui.App.StadiumBare(false);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventScreenTouch t:
                if (t.Pressed)
                {
                    _touch[t.Index] = t.Position;
                    if (_touch.Count == 1)
                    {
                        _orbiting = !OnTapArea(t.Position);
                        _gesture = false;
                        _pressAt = t.Position;
                    }
                    if (_touch.Count >= 2) _two = _gesture = true;
                }
                else _touch.Remove(t.Index);
                if (_touch.Count == 0) _orbiting = _two = false;
                break;
            case InputEventScreenDrag d when _touch.ContainsKey(d.Index):
                if (_two && _touch.Count >= 2)
                {
                    var (c0, s0) = Fingers();
                    _touch[d.Index] = d.Position;
                    var (c1, s1) = Fingers();
                    var m = 300 / Size.X;
                    _ui.App.StadiumOrbit(0, 0, s1 > 1 && s0 > 1 ? s0 / s1 : 1, new Vector2(-(c1.X - c0.X), c1.Y - c0.Y) * m);
                }
                else
                {
                    _touch[d.Index] = d.Position;
                    if ((d.Position - _pressAt).Length() > 10) _gesture = true;
                    if (_orbiting && !_two) _ui.App.StadiumOrbit(-d.Relative.X / Size.X * 4, d.Relative.Y / Size.Y * 1.6f, 1, Vector2.Zero);
                }
                break;
            // A mouse: the wheel zooms, the right button slides.
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } w:
                _ui.App.StadiumOrbit(0, 0, w.ButtonIndex == MouseButton.WheelUp ? 0.9f : 1.1f, Vector2.Zero);
                break;
            case InputEventMouseMotion mm when (mm.ButtonMask & MouseButtonMask.Right) != 0:
                _ui.App.StadiumOrbit(0, 0, 1, new Vector2(-mm.Relative.X, mm.Relative.Y) * (300 / Size.X));
                break;
        }
        base._GuiInput(e);
    }

    Vector2 _pressAt;

    protected override void Background()
    {
        if (_gesture) return;
        _bare = !_bare;
        _bareAt = T;
        _ui.App.StadiumBare(_bare);
    }

    (Vector2 centre, float spread) Fingers()
    {
        Vector2 c = Vector2.Zero;
        foreach (var p in _touch.Values) c += p;
        c /= _touch.Count;
        float s = 0;
        foreach (var p in _touch.Values) s += (p - c).Length();
        return (c, s / _touch.Count);
    }

    /// <summary>The colour a set shows in: the club's pick, else its own.</summary>
    uint Shown(int set)
    {
        uint p = Plan.PaintOf(set);
        return p == 0 ? Kit.Sets[set].Swatch : p == Kit.ClubPaint ? (uint)_ui.Club.S.Kit.Main : p;
    }

    void Recolour(int set, uint col)
    {
        if (Plan.PaintOf(set) == col) return;
        _ui.Club.SetStadiumPaint(set, col);
        _changedAt = T;
    }

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

    void Around(int step)
    {
        int n = Surroundings.Names.Length;
        _ui.Club.SetStadiumArea((Surroundings.Clamp(Plan.Area) + step + n) % n);
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
        if (_bare)
        {
            // Just the stadium: a hint for a moment, then nothing.
            float a = Mathf.Clamp(2.5f - (float)(T - _bareAt), 0, 1);
            if (a > 0) Px.TextC(this, Px.Small, W / 2, H - 20, "TAP TO BRING BACK THE MENU", 8, new Color(1, 1, 1, a), new Color(0, 0, 0, 0.6f * a), 1);
            return;
        }
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
        float ly = Colours(new Rect2(panel.Position.X + 16, ty + 26, panel.Size.X - 32, 0), Plan.Get(Selected)) + 14;
        // Which sets can hang the club's giant tifo (as the main stand).
        string tifo = set.CarriesTifo ? "HANGS YOUR GIANT TIFO AS THE MAIN STAND"
            : Selected == Slot.Main ? "NO GIANT TIFO HERE: " + string.Join(", ", TifoSets()).ToUpperInvariant() + " HANG IT" : null;
        if (tifo != null)
        {
            foreach (var l in Px.Wrap(Px.Small, tifo, 8, panel.Size.X - 32))
            {
                Px.Text(this, Px.Small, new Vector2(panel.Position.X + 16, ly), l, 8, set.CarriesTifo ? Px.Gold : Px.InkDim);
                ly += 13;
            }
            ly += 4;
        }
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

        // Under them: what's round the ground, stepped through either way.
        int area = Surroundings.Clamp(Plan.Area);
        string an = "AROUND: " + Surroundings.Names[area].ToUpperInvariant();
        float aw = Px.Width(Px.Big, an, 18) + 18, arrow = Px.Width(Px.Big, ">", 18) + 18;
        float ax = W - 16 - arrow;
        Chip("area+", new Vector2(ax, 66), ">", false, () => Around(1));
        ax -= aw + 6;
        Chip("area", new Vector2(ax, 66), an, true, () => Around(1));
        ax -= arrow + 6;
        Chip("area-", new Vector2(ax, 66), "<", false, () => Around(-1));
        Px.TextR(this, Px.Small, W - 16, 108, Surroundings.About[area], 8, Px.Ink, new Color(0, 0, 0, 0.6f), 1);
        if (_building || _changedAt >= 0)
            Px.TextR(this, Px.Big, W - 16, 140, (T % 0.6) < 0.3 ? "BUILDING..." : "BUILDING", 22, Px.Gold, new Color(0, 0, 0, 0.6f), 2);

        const string hint = "DRAG TO TURN  ·  TWO FINGERS TO MOVE AND ZOOM";
        float hw = Px.Width(Px.Small, hint, 8) + 16;
        DrawRect(new Rect2(W - 16 - hw, H - 16 - SetRows * 50 - 22, hw, 16), new Color(14 / 255f, 10 / 255f, 40 / 255f, 0.7f));
        Px.TextR(this, Px.Small, W - 24, H - 16 - SetRows * 50 - 10, hint, 8, Px.Ink);

        // The sets along the bottom, five to a row.
        int rows = (Kit.Sets.Length + 4) / 5, per = (Kit.Sets.Length + rows - 1) / rows;
        float x0 = 340, x1 = W - 16, gap = 6, ch = 44;
        float cw = (x1 - x0 - gap * (per - 1)) / per;
        for (int i = 0; i < Kit.Sets.Length; i++)
        {
            int idx = i;
            var s = Kit.Sets[i];
            var r = new Rect2(x0 + i % per * (cw + gap), H - 16 - (rows - i / per) * (ch + gap) + gap, cw, ch);
            bool on = Plan.Get(Selected) == i;
            bool held = Held("set" + i);
            var rr = held ? new Rect2(r.Position + new Vector2(2, 2), r.Size) : r;
            Px.Frame(this, rr, on ? Px.Gold : Px.Glass2, on ? Px.Hex(0xb37400) : Px.Line2, held ? null : Px.ShadowSoft);
            float sw = Mathf.Clamp(cw - 82, 18, 46);
            Swatch(new Rect2(rr.Position + new Vector2(7, 7), new Vector2(sw, ch - 14)), i);
            Px.Text(this, Px.Big, new Vector2(rr.Position.X + sw + 12, rr.GetCenter().Y + 7), Px.Fit(Px.Big, s.Name, 19, cw - sw - 17), 19, on ? Px.Dark : Px.Ink);
            if (s.CarriesTifo) TifoBadge(new Vector2(rr.End.X - 12, rr.Position.Y + 5), on);
            Tap("set" + i, r, () => Choose(idx));
        }
    }

    static int SetRows => (Kit.Sets.Length + 4) / 5;

    static IEnumerable<string> TifoSets()
    {
        foreach (var s in Kit.Sets) if (s.CarriesTifo) yield return s.Name;
    }

    /// <summary>A little hanging banner: this set carries the giant tifo.</summary>
    void TifoBadge(Vector2 p, bool on)
    {
        var c = on ? Px.Dark : Px.Gold;
        DrawRect(new Rect2(p, new Vector2(8, 2)), c);
        DrawColoredPolygon(new[] { p + new Vector2(1, 2), p + new Vector2(7, 2), p + new Vector2(7, 11), p + new Vector2(4, 9), p + new Vector2(1, 11) }, c);
    }

    /// <summary>The set's main colour: its own, the club's, or one of the paints; returns the
    /// row's bottom.</summary>
    float Colours(Rect2 r, int set)
    {
        Px.Text(this, Px.Small, new Vector2(r.Position.X, r.Position.Y + 8), $"COLOUR OF EVERY {Kit.Sets[set].Name.ToUpperInvariant()} STAND", 8, Px.InkDim);
        int per = (Kit.Paints.Length + 1) / 2;
        float gap = 4, w = (r.Size.X - gap * (per - 1)) / per, h = 20, y0 = r.Position.Y + 14;
        uint now = Plan.PaintOf(set);
        for (int i = 0; i < Kit.Paints.Length; i++)
        {
            uint p = Kit.Paints[i];
            var b = new Rect2(r.Position.X + i % per * (w + gap), y0 + i / per * (h + gap), w, h);
            bool held = Held("paint" + i);
            var bb = held ? new Rect2(b.Position + new Vector2(1, 1), b.Size) : b;
            uint shown = p == 0 ? Kit.Sets[set].Mains[0] : p == Kit.ClubPaint ? (uint)_ui.Club.S.Kit.Main : p;
            DrawRect(bb, Px.Hex((int)shown));
            if (p == 0 || p == Kit.ClubPaint)
            {
                var lum = Px.Hex((int)shown).Luminance;
                Px.TextC(this, Px.Small, bb.GetCenter().X, bb.GetCenter().Y + 4, p == 0 ? "OWN" : "CLUB", 8, lum > 0.5f ? Px.Dark : Px.Ink);
            }
            if (p == now) Px.Ring(this, bb.Grow(2), (T % 0.8) < 0.4 ? Px.Ink : Px.Gold, 2);
            else Px.Ring(this, bb, new Color(0, 0, 0, 0.5f), 1);
            uint pick = p;
            Tap("paint" + i, b.Grow(gap / 2), () => Recolour(set, pick));
        }
        return y0 + 2 * (h + gap);
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
            case 5: // containers stacked, a crane
                for (int i = 0; i < 6; i++) DrawRect(new Rect2(P(0.05f + i % 3 * 0.22f, 0.25f + i / 3 * 0.25f), new Vector2(w * 0.2f, h * 0.22f)), Px.Hex(new[] { 0xc0392b, 0x2f7fb8, 0xe0a030, 0x2e8b57, 0xd35400, 0xbdc3c7 }[i]));
                DrawRect(new Rect2(P(0.8f, 0.95f), new Vector2(3, h * 0.95f)), dark);
                DrawRect(new Rect2(P(0.45f, 0.95f), new Vector2(w * 0.5f, 3)), dark);
                break;
            case 6: // a tier under a swept roof, red pillars
                DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.8f, 0.45f), P(0.8f, 0) }, Px.Hex(0x6e2a22));
                DrawColoredPolygon(new[] { P(0, 0.62f), P(0.15f, 0.55f), P(0.9f, 0.8f), P(1, 0.72f), P(0.9f, 0.86f), P(0.1f, 0.66f) }, Px.Hex(0x34433f).Lightened(0.2f));
                for (int i = 0; i < 3; i++) DrawRect(new Rect2(P(0.1f + i * 0.25f, 0.6f), new Vector2(2, h * 0.5f)), c);
                break;
            case 7: // stepped cream stand and its clock tower
                DrawColoredPolygon(new[] { P(0, 0), P(0, 0.15f), P(0.55f, 0.55f), P(0.62f, 0.55f), P(0.62f, 0.68f), P(0.7f, 0.68f), P(0.7f, 0) }, c);
                DrawRect(new Rect2(P(0.75f, 1), new Vector2(w * 0.16f, h)), c);
                DrawRect(new Rect2(P(0.78f, 0.82f), new Vector2(w * 0.1f, w * 0.1f)), Px.Hex(0xd4a63a));
                break;
            case 8: // black rock, a lava seam, a spire
                DrawColoredPolygon(new[] { P(0, 0), P(0, 0.1f), P(0.6f, 0.75f), P(0.7f, 0.9f), P(0.8f, 0.7f), P(1, 0) }, Px.Hex(0x3a3533).Lightened(0.15f));
                DrawRect(new Rect2(P(0.1f, 0.3f), new Vector2(w * 0.5f, 2)), c);
                DrawColoredPolygon(new[] { P(0.85f, 0), P(0.9f, 1), P(0.95f, 0) }, Px.Hex(0x4d4642).Lightened(0.2f));
                DrawRect(new Rect2(P(0.88f, 1.0f), new Vector2(4, 3)), Px.Hex(0xffb347));
                break;
            case 9: // white tiers under a floating halo
                DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.65f, 0.55f), P(0.7f, 0.55f), P(0.7f, 0) }, Px.Hex(0xe6eaee));
                DrawRect(new Rect2(P(0.05f, 0.85f), new Vector2(w * 0.85f, 3)), c);
                DrawRect(new Rect2(P(0.78f, 0.85f), new Vector2(2, h * 0.85f)), Px.Hex(0xe6eaee));
                break;
            case 10: // a tier under peaked white fabric on masts
                DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.75f, 0.5f), P(0.75f, 0) }, Px.Hex(0x8d949b));
                DrawColoredPolygon(new[] { P(0, 0.7f), P(0.3f, 0.95f), P(0.55f, 0.72f), P(0.8f, 0.95f), P(0.95f, 0.78f), P(0.95f, 0.72f), P(0.8f, 0.86f), P(0.55f, 0.64f), P(0.3f, 0.86f), P(0, 0.62f) }, c);
                DrawRect(new Rect2(P(0.3f, 1), new Vector2(2, h)), dark);
                DrawRect(new Rect2(P(0.8f, 1), new Vector2(2, h)), dark);
                break;
            case 11: // raw concrete, raking frames and a heavy slab
                DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.7f, 0.6f), P(0.7f, 0) }, c);
                DrawRect(new Rect2(P(0, 0.86f), new Vector2(w * 0.85f, h * 0.1f)), dark);
                DrawColoredPolygon(new[] { P(0.6f, 0.86f), P(0.66f, 0.86f), P(0.9f, 0.3f), P(0.9f, 0) , P(0.84f, 0), P(0.84f, 0.3f) }, dark);
                break;
            case 12: // three steep stacked tiers in blue and gold
                for (int i = 0; i < 3; i++)
                    DrawColoredPolygon(new[] { P(0.12f + i * 0.25f, 0.06f + i * 0.3f), P(0.12f + i * 0.25f, 0.12f + i * 0.3f), P(0.4f + i * 0.25f, 0.34f + i * 0.3f), P(0.4f + i * 0.25f, 0.28f + i * 0.3f) }, i % 2 == 0 ? c : Px.Hex(0xf2c230));
                DrawRect(new Rect2(P(0.92f, 0.98f), new Vector2(3, h * 0.98f)), dark);
                break;
            case 13: // a timber tier under an arched green roof
                DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.8f, 0.42f), P(0.8f, 0) }, c);
                DrawColoredPolygon(new[] { P(0, 0.6f), P(0.3f, 0.8f), P(0.65f, 0.86f), P(0.95f, 0.74f), P(0.95f, 0.66f), P(0.65f, 0.78f), P(0.3f, 0.72f), P(0, 0.54f) }, Px.Hex(0x6f8a3c));
                DrawColoredPolygon(new[] { P(0.48f, 0), P(0.52f, 0), P(0.52f, 0.5f), P(0.6f, 0.78f), P(0.56f, 0.78f), P(0.5f, 0.58f), P(0.44f, 0.76f), P(0.4f, 0.76f), P(0.48f, 0.5f) }, dark);
                break;
            case 14: // a glowing cushioned bowl
                DrawColoredPolygon(new[] { P(0, 0), P(0, 0.55f), P(0.3f, 0.8f), P(0.62f, 0.86f), P(0.88f, 0.7f), P(1, 0.4f), P(1, 0) }, Px.Hex(0xe8eef2));
                for (int i = 0; i < 4; i++) DrawRect(new Rect2(P(0.1f + i * 0.22f, 0.5f), new Vector2(w * 0.12f, 2)), c);
                DrawRect(new Rect2(P(0.05f, 0.25f), new Vector2(w * 0.9f, 2)), c);
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
            bool sel = s == Selected;
            var col = Px.Hex((int)Shown(Plan.Get(s)));
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
