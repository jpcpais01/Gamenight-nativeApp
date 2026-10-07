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

    // The bottom drawer's tab (stands, colour, around) and its scrolling strip of stand cards.
    int _tab, _shownSet = -1, _shownArea = -1, _stripTab = -1;
    readonly Strip _strip;

    public StadiumScreen(Menus ui)
    {
        _ui = ui;
        _strip = new Strip { ClipContents = true, Draw = PaintStrip, Visible = false };
        AddChild(_strip);
    }

    /// <summary>A clipped strip that swipes sideways, drawn by its owner.</summary>
    public sealed partial class Strip : PxCanvas
    {
        public Action<Strip> Draw;
        public Rect2 Area;
        public Strip() { ScrollAxis = 2; }
        public float Offset => Scroll;
        public float Max => ScrollMax;
        public float Total { set => Content = value; }
        protected override void Paint() => Draw?.Invoke(this);
        public void Hit(string k, Rect2 r, Action a) => Tap(k, r, a);
        public bool IsHeld(string k) => Held(k);
        public void Page(int dir) => Glide(dir * Size.X * 0.8f);
        /// <summary>Brings [a, b] into view.</summary>
        public void Show(float a, float b)
        {
            if (a < Scroll) Scroll = a - 8;
            else if (b > Scroll + Size.X) Scroll = b - Size.X + 8;
        }
    }

    StadiumPlan Plan => _ui.Club.S.Stadium;

    // Fingers on the stadium: one drag turns round it, two slide across it and pinch to zoom.
    readonly Dictionary<int, Vector2> _touch = new();
    bool _orbiting, _two, _gesture;
    // A plain tap on the stadium hides the menus (the stadium alone, centred); another brings them back.
    bool _bare;
    double _bareAt;
    // The view with the menus hidden (the menus always come back to the drone's).
    int _view;
    static readonly string[] Views = { "DRONE", "PITCH", "AERIAL" };
    public bool Bare => _bare;

    public override void _Notification(int what)
    {
        base._Notification(what);
        // Always open with the menus showing.
        if (what == NotificationVisibilityChanged && IsVisibleInTree() && _bare)
        {
            _bare = false;
            _ui.App.StadiumBare(false);
            _ui.App.StadiumView(0);
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
        _ui.App.StadiumView(_bare ? _view : 0);
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
        _strip.Position = _strip.Area.Position;
        _strip.Size = _strip.Area.Size;
        _strip.Visible = !_bare && _tab != 1 && _strip.Area.Size.X > 0;
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

    /// <summary>A random stadium that still looks designed: mirrored end to end (both ends, and
    /// the corners on each side, alike), one set or two, in one colour or two.</summary>
    void Surprise()
    {
        var rng = new Random();
        int n = Kit.Sets.Length;
        int a = rng.Next(n), b = (a + 1 + rng.Next(n - 1)) % n;
        // Which places take the second set: none, the ends and all corners, just the ends, or
        // just the main stand as the showpiece.
        var second = rng.Next(4) switch
        {
            0 => Array.Empty<Slot>(),
            1 => new[] { Slot.Home, Slot.Away, Slot.HomeFar, Slot.AwayFar, Slot.HomeNear, Slot.AwayNear },
            2 => new[] { Slot.Home, Slot.Away },
            _ => new[] { Slot.Main },
        };
        foreach (Slot s in Enum.GetValues<Slot>()) Plan.Set(s, Array.IndexOf(second, s) >= 0 ? b : a);

        // Colours: the club's, or one other, on everything; or, with two sets, the club's and
        // a plain white, black or grey on the other.
        uint Any() => Kit.Paints[2 + rng.Next(Kit.Paints.Length - 2)];
        uint first = rng.NextDouble() < 0.6 ? Kit.ClubPaint : Any();
        uint other = second.Length == 0 || rng.NextDouble() < 0.5 ? first
            : first == Kit.ClubPaint ? new[] { 0xeceae4u, 0x2a2c33u, 0x8c8f95u }[rng.Next(3)] : Kit.ClubPaint;
        Plan.SetPaint(a, first);
        if (second.Length > 0) Plan.SetPaint(b, other);
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
            // SURPRISE ME stays, to keep rolling stadiums with the view clear (a tap on it
            // isn't the tap that brings the menu back).
            float bw = Px.Width(Px.Big, "SURPRISE ME", 18) + 18;
            Chip("surprise", new Vector2(W - 16 - bw, 16), "SURPRISE ME", false, Surprise);
            // The camera: the drone, down on the pitch, or high over a corner.
            float vx = 16;
            for (int i = 0; i < Views.Length; i++)
            {
                int v = i;
                vx += Chip("view" + i, new Vector2(vx, 16), Views[i], _view == i, () => { _view = v; _ui.App.StadiumView(v); }) + 8;
            }
            if (_building || _changedAt >= 0)
                Px.TextR(this, Px.Big, W - 16, 74, (T % 0.6) < 0.3 ? "BUILDING..." : "BUILDING", 22, Px.Gold, new Color(0, 0, 0, 0.6f), 2);
            return;
        }
        var (L, R) = Margins();
        var drawer = Drawer();
        float DH = drawer.Size.Y;

        // Top bar: back, the title; play here and surprise me on the right.
        DrawRect(new Rect2(0, 0, W, 66), new Color(14 / 255f, 10 / 255f, 40 / 255f, 0.35f));
        BackButton(new Vector2(L, 12), () => _ui.Go(_ui.Home));
        Title(new Vector2(L + 50, 44), "STADIUM");
        Px.Text(this, Px.Small, new Vector2(L + 52, 60), "TAP A PLACE, THEN PICK ITS STAND", 8, Px.Cyan);
        GoldButton("play", new Rect2(R - 190, 12, 190, 42), "PLAY HERE  >", 26, () => _ui.App.PlayAt("custom"));
        float sw = Px.Width(Px.Big, "SURPRISE ME", 18) + 18;
        Chip("surprise", new Vector2(R - 190 - 12 - sw, 20), "SURPRISE ME", false, Surprise);
        if (_building || _changedAt >= 0)
            Px.TextR(this, Px.Big, R, 86, (T % 0.6) < 0.3 ? "BUILDING..." : "BUILDING", 22, Px.Gold, new Color(0, 0, 0, 0.6f), 2);

        // Left: the ground from above, and the place picked.
        var panel = new Rect2(L, 72, PanelW, H - 72 - 16);
        Px.Frame(this, panel, Px.Glass, Px.Line2, Px.ShadowSoft);
        float mh = Mathf.Min(196, panel.Size.Y * 0.5f);
        PlanMap(new Rect2(panel.Position + new Vector2(12, 12), new Vector2(panel.Size.X - 24, mh)));
        var set = Kit.Sets[Plan.Get(Selected)];
        float px = panel.Position.X + 16, tw = panel.Size.X - 32;
        float ly = panel.Position.Y + 12 + mh + 24;
        Px.Text(this, Px.Small, new Vector2(px, ly), Kit.SlotNames[(int)Selected].ToUpperInvariant(), 8, Px.InkDim);
        ly += 28;
        Px.Text(this, Px.Big, new Vector2(px, ly), Px.Fit(Px.Big, set.Name, 30, tw), 30, Px.Hex((int)Shown(Plan.Get(Selected))).Lightened(0.15f), new Color(0, 0, 0, 0.5f), 2);
        ly += 18;
        foreach (var l in Px.Wrap(Px.Small, set.About.ToUpperInvariant(), 8, tw))
        {
            if (ly > panel.End.Y - 40) break;
            Px.Text(this, Px.Small, new Vector2(px, ly), l, 8, Px.Ink);
            ly += 13;
        }
        string tifo = set.CarriesTifo ? "HANGS YOUR GIANT TIFO AS THE MAIN STAND"
            : Selected == Slot.Main ? "NO GIANT TIFO ON THIS ONE (LOOK FOR THE FLAG ON A CARD)" : null;
        if (tifo != null)
        {
            ly += 6;
            if (set.CarriesTifo) TifoBadge(new Vector2(px, ly - 9), false);
            foreach (var l in Px.Wrap(Px.Small, tifo, 8, tw - (set.CarriesTifo ? 14 : 0)))
            {
                if (ly > panel.End.Y - 6) break;
                Px.Text(this, Px.Small, new Vector2(px + (set.CarriesTifo ? 14 : 0), ly), l, 8, set.CarriesTifo ? Px.Gold : Px.InkDim);
                ly += 13;
            }
        }

        // How to move round it, just above the drawer.
        const string hint = "DRAG TO TURN  ·  TWO FINGERS TO MOVE AND ZOOM  ·  TAP TO HIDE THE MENU";
        float hw = Px.Width(Px.Small, hint, 8) + 16;
        DrawRect(new Rect2(R - hw, drawer.Position.Y - 22, hw, 16), new Color(14 / 255f, 10 / 255f, 40 / 255f, 0.7f));
        Px.TextR(this, Px.Small, R - 8, drawer.Position.Y - 10, hint, 8, Px.Ink);

        // The drawer: tabs along its top, the stands, the colours or what's around below.
        Px.Frame(this, drawer, Px.Glass, Px.Line2, Px.ShadowSoft);
        Tap("drawer", drawer, null);
        float tx = drawer.Position.X + 12, ty = drawer.Position.Y + 10;
        string[] tabs = { $"STANDS  {Kit.Sets.Length}", "COLOUR", "AROUND" };
        for (int i = 0; i < tabs.Length; i++)
        {
            int t = i;
            tx += Chip("tab" + i, new Vector2(tx, ty), tabs[i], _tab == i, () => _tab = t, 18, 28) + 8;
        }
        var body = new Rect2(drawer.Position.X + 12, drawer.Position.Y + 48, drawer.Size.X - 24, DH - 58);
        switch (_tab)
        {
            case 0:
            {
                float aw = Px.Width(Px.Big, "SAME ALL ROUND", 18) + 18;
                Chip("same", new Vector2(drawer.End.X - 12 - aw, ty), "SAME ALL ROUND", false, () => All(Plan.Get(Selected)), 18, 28);
                // The cards scroll in their own strip; arrows either side page through them.
                StripIn(body);
                break;
            }
            case 1:
            {
                int si = Plan.Get(Selected);
                Px.TextR(this, Px.Small, drawer.End.X - 14, ty + 18, $"EVERY {Kit.Sets[si].Name.ToUpperInvariant()} STAND IN THE GROUND", 8, Px.InkDim);
                Colours(body, si);
                break;
            }
            default:
            {
                int area = Surroundings.Clamp(Plan.Area);
                var about = Px.Fit(Px.Small, Surroundings.About[area], 8, drawer.End.X - 14 - tx - 10);
                Px.TextR(this, Px.Small, drawer.End.X - 14, ty + 18, about, 8, Px.InkDim);
                StripIn(body);
                break;
            }
        }
    }

    /// <summary>Side margins: the menus keep to the middle of a very wide screen.</summary>
    (float l, float r) Margins()
    {
        float L = Mathf.Max(16, (Size.X - 1640) / 2);
        return (L, Size.X - L);
    }

    const float PanelW = 264;

    /// <summary>The drawer along the bottom, right of the plan panel.</summary>
    Rect2 Drawer()
    {
        var (L, R) = Margins();
        const float DH = 150;
        return new Rect2(L + PanelW + 16, Size.Y - 16 - DH, R - (L + PanelW + 16), DH);
    }

    /// <summary>The scrolling card strip in the drawer's body, an arrow key either side.</summary>
    void StripIn(Rect2 body)
    {
        float aw = 26;
        _strip.Area = new Rect2(body.Position.X + aw + 6, body.Position.Y, body.Size.X - 2 * (aw + 6), body.Size.Y);
        Arrow("stripL", new Rect2(body.Position.X, body.Position.Y, aw, body.Size.Y), -1);
        Arrow("stripR", new Rect2(body.End.X - aw, body.Position.Y, aw, body.Size.Y), 1);
    }

    /// <summary>A tall arrow key either side of the cards: pages them along.</summary>
    void Arrow(string key, Rect2 r, int dir)
    {
        bool can = dir < 0 ? _strip.Offset > 1 : _strip.Offset < _strip.Max - 1;
        bool held = Held(key) && can;
        var rr = held ? new Rect2(r.Position + new Vector2(1, 1), r.Size) : r;
        Px.Frame(this, rr, can ? Px.Glass2 : new Color(0, 0, 0, 0.2f), Px.Line2, null, 2, 0);
        var c = rr.GetCenter();
        var ink = can ? Px.Ink : Px.InkDim;
        DrawColoredPolygon(new[] { c + new Vector2(-5 * dir, -9), c + new Vector2(6 * dir, 0), c + new Vector2(-5 * dir, 9) }, ink);
        Tap(key, r, () => _strip.Page(dir));
    }

    /// <summary>The stand cards, drawn into the strip (it clips and scrolls them).</summary>
    void PaintStrip(Strip st)
    {
        // A new tab brings its chosen card into view again.
        if (_stripTab != _tab) { _stripTab = _tab; _shownSet = _shownArea = -1; }
        if (_tab == 2) { Areas(st); return; }
        const float cw = 106, gap = 8;
        float h = st.Size.Y;
        st.Total = Kit.Sets.Length * (cw + gap) - gap;
        // Whenever the place (or its stand) changes, bring its card into view.
        int now = Plan.Get(Selected);
        if (now != _shownSet && st.Size.X > 0)
        {
            _shownSet = now;
            st.Show(now * (cw + gap), now * (cw + gap) + cw);
        }
        for (int i = 0; i < Kit.Sets.Length; i++)
        {
            int idx = i;
            var s = Kit.Sets[i];
            var r = new Rect2(i * (cw + gap) - st.Offset, 0, cw, h - 2);
            if (r.End.X < -4 || r.Position.X > st.Size.X + 4) continue;
            bool on = Plan.Get(Selected) == i;
            bool held = st.IsHeld("set" + i);
            var rr = held ? new Rect2(r.Position + new Vector2(2, 2), r.Size) : r;
            Px.Frame(st, rr, on ? Px.Gold : Px.Glass2, on ? Px.Hex(0xb37400) : Px.Line2, null, 3, 0);
            Swatch(st, new Rect2(rr.Position + new Vector2(6, 6), new Vector2(cw - 12, h - 36)), i);
            Px.TextC(st, Px.Big, rr.GetCenter().X, rr.End.Y - 9, Px.Fit(Px.Big, s.Name.ToUpperInvariant(), 17, cw - 10), 17, on ? Px.Dark : Px.Ink);
            if (s.CarriesTifo) TifoBadge(st, new Vector2(rr.End.X - 15, rr.Position.Y + 8), false);
            st.Hit("set" + i, r, () => Choose(idx));
        }
    }

    /// <summary>What's round the ground: a card per area, a little view of each.</summary>
    void Areas(Strip st)
    {
        int n = Surroundings.Names.Length, now = Surroundings.Clamp(Plan.Area);
        const float cw = 132, gap = 8;
        float h = st.Size.Y;
        st.Total = n * (cw + gap) - gap;
        if (now != _shownArea && st.Size.X > 0)
        {
            _shownArea = now;
            st.Show(now * (cw + gap), now * (cw + gap) + cw);
        }
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            var r = new Rect2(i * (cw + gap) - st.Offset, 0, cw, h - 2);
            if (r.End.X < -4 || r.Position.X > st.Size.X + 4) continue;
            bool on = now == i, held = st.IsHeld("area" + i);
            var rr = held ? new Rect2(r.Position + new Vector2(2, 2), r.Size) : r;
            Px.Frame(st, rr, on ? Px.Gold : Px.Glass2, on ? Px.Hex(0xb37400) : Px.Line2, null, 3, 0);
            AreaView(st, new Rect2(rr.Position + new Vector2(6, 6), new Vector2(cw - 12, rr.Size.Y - 36)), i);
            Px.TextC(st, Px.Big, rr.GetCenter().X, rr.End.Y - 9, Px.Fit(Px.Big, Surroundings.Names[i].ToUpperInvariant(), 17, cw - 10), 17, on ? Px.Dark : Px.Ink);
            st.Hit("area" + i, r, () => SetArea(idx));
        }
    }

    /// <summary>A tiny view of an area: its sky, its land and what stands out in it.</summary>
    static void AreaView(CanvasItem ci, Rect2 r, int area)
    {
        float x = r.Position.X, y = r.End.Y, w = r.Size.X, h = r.Size.Y;
        Vector2 P(float fx, float fy) => new(Mathf.Round(x + fx * w), Mathf.Round(y - fy * h));
        ci.DrawRect(r, Px.Hex(0x9cc7e8));
        var land = Px.Hex(area switch { 1 => 0x6b6e6a, 3 => 0x5a6250, 4 => 0x6f9440, 5 => 0x5f8a3e, 6 => 0xb5a062, 7 => 0x4c7636, 8 => 0x8a8a84, 9 => 0x3c3d40, 10 => 0xb8ad94, 11 => 0xc9b48a, 12 => 0x9a9488, 13 => 0xd6c29a, 14 => 0x2e362a, 15 => 0x8c8c8a, 16 => 0xe8eef4, _ => 0x557a3c });
        switch (area)
        {
            case 1: // towers
                for (int i = 0; i < 7; i++)
                {
                    float hh = 0.35f + 0.5f * Mathf.Abs(Mathf.Sin(i * 2.1f + 0.7f));
                    ci.DrawRect(new Rect2(P(0.04f + i * 0.135f, hh), new Vector2(w * 0.11f, hh * h)), Px.Hex(i % 2 == 0 ? 0x5f7f8a : 0x404852));
                }
                ci.DrawRect(new Rect2(P(0, 0.12f), new Vector2(w, h * 0.12f)), land);
                break;
            case 2: // red roofs up to a castle on its hill, the river
                ci.DrawColoredPolygon(new[] { P(0, 0.2f), P(0.45f, 0.62f), P(0.75f, 0.55f), P(1, 0.3f), P(1, 0), P(0, 0) }, Px.Hex(0x6d8442));
                ci.DrawRect(new Rect2(P(0.4f, 0.8f), new Vector2(w * 0.12f, h * 0.2f)), Px.Hex(0xa39886));
                for (int i = 0; i < 6; i++) ci.DrawColoredPolygon(new[] { P(0.05f + i * 0.15f, 0.22f), P(0.12f + i * 0.15f, 0.32f), P(0.19f + i * 0.15f, 0.22f) }, Px.Hex(0xb5522f));
                ci.DrawRect(new Rect2(P(0, 0.12f), new Vector2(w, h * 0.08f)), Px.Hex(0x2f6a86));
                break;
            case 3: // the quay, a crane, cooling towers over the water
                ci.DrawRect(new Rect2(P(0, 0.35f), new Vector2(w, h * 0.35f)), Px.Hex(0x2f5a6e));
                ci.DrawColoredPolygon(new[] { P(0.62f, 0.35f), P(0.66f, 0.62f), P(0.72f, 0.62f), P(0.76f, 0.35f) }, Px.Hex(0xc9c3b8));
                ci.DrawColoredPolygon(new[] { P(0.8f, 0.35f), P(0.84f, 0.58f), P(0.9f, 0.58f), P(0.94f, 0.35f) }, Px.Hex(0xc9c3b8));
                ci.DrawRect(new Rect2(P(0.2f, 0.9f), new Vector2(3, h * 0.55f)), Px.Hex(0xb8322a));
                ci.DrawRect(new Rect2(P(0.08f, 0.9f), new Vector2(w * 0.36f, 3)), Px.Hex(0xb8322a));
                ci.DrawRect(new Rect2(P(0, 0.12f), new Vector2(w, h * 0.12f)), Px.Hex(0x8a4a35));
                break;
            case 4: // fields, a turbine
                for (int i = 0; i < 4; i++) ci.DrawRect(new Rect2(P(0, 0.12f + i * 0.08f + 0.08f), new Vector2(w, h * 0.08f + 1)), Px.Hex(new[] { 0xc8b45e, 0x6f9440, 0xd9c63a, 0x5f8c3c }[i]));
                ci.DrawRect(new Rect2(P(0.7f, 0.85f), new Vector2(2, h * 0.5f)), Px.Hex(0xf2f2f0));
                ci.DrawLine(P(0.705f, 0.85f), P(0.62f, 0.95f), Px.Hex(0xf2f2f0), 2);
                ci.DrawLine(P(0.705f, 0.85f), P(0.8f, 0.9f), Px.Hex(0xf2f2f0), 2);
                ci.DrawLine(P(0.705f, 0.85f), P(0.69f, 0.7f), Px.Hex(0xf2f2f0), 2);
                break;
            case 5: // snowy peaks over a lake
                ci.DrawColoredPolygon(new[] { P(0, 0.3f), P(0.3f, 0.9f), P(0.55f, 0.45f), P(0.75f, 0.8f), P(1, 0.35f), P(1, 0.2f), P(0, 0.2f) }, Px.Hex(0x4a5a48));
                ci.DrawColoredPolygon(new[] { P(0.22f, 0.74f), P(0.3f, 0.9f), P(0.38f, 0.74f) }, Px.Hex(0xeef1f4));
                ci.DrawColoredPolygon(new[] { P(0.69f, 0.68f), P(0.75f, 0.8f), P(0.81f, 0.68f) }, Px.Hex(0xeef1f4));
                ci.DrawRect(new Rect2(P(0, 0.2f), new Vector2(w, h * 0.12f)), Px.Hex(0x2f6a86));
                break;
            case 6: // a white town and its castle on the hill, gold fields, a windmill
                ci.DrawColoredPolygon(new[] { P(0, 0.25f), P(0.25f, 0.55f), P(0.5f, 0.7f), P(0.8f, 0.5f), P(1, 0.3f), P(1, 0), P(0, 0) }, Px.Hex(0xb59a5a));
                for (int i = 0; i < 9; i++)
                {
                    float fx = 0.18f + i * 0.07f, fy = 0.42f + 0.22f * (1 - Mathf.Abs(fx - 0.5f) * 3f);
                    ci.DrawRect(new Rect2(P(fx, fy), new Vector2(w * 0.06f, h * 0.1f)), Px.Hex(0xf4f1ea));
                    ci.DrawRect(new Rect2(P(fx, fy), new Vector2(w * 0.06f, 2)), Px.Hex(0xb5522f));
                }
                ci.DrawRect(new Rect2(P(0.44f, 0.86f), new Vector2(w * 0.14f, h * 0.18f)), Px.Hex(0xa8946e));
                for (int i = 0; i < 3; i++) ci.DrawRect(new Rect2(P(0.44f + i * 0.055f, 0.9f), new Vector2(w * 0.03f, h * 0.04f)), Px.Hex(0xa8946e));
                ci.DrawRect(new Rect2(P(0, 0.3f), new Vector2(w, h * 0.12f)), Px.Hex(0xd6b85a));
                ci.DrawRect(new Rect2(P(0.84f, 0.5f), new Vector2(w * 0.06f, h * 0.2f)), Px.Hex(0xf4f1ea));
                ci.DrawColoredPolygon(new[] { P(0.83f, 0.5f), P(0.87f, 0.58f), P(0.91f, 0.5f) }, Px.Hex(0x8a4a35));
                break;
            case 7: // steep walls down to the water, a waterfall, red houses on the shore
                ci.DrawColoredPolygon(new[] { P(0, 0.95f), P(0.3f, 0.75f), P(0.36f, 0.3f), P(0, 0.3f) }, Px.Hex(0x4f5a52));
                ci.DrawColoredPolygon(new[] { P(1, 0.9f), P(0.68f, 0.72f), P(0.62f, 0.3f), P(1, 0.3f) }, Px.Hex(0x45524a));
                ci.DrawColoredPolygon(new[] { P(0.36f, 0.48f), P(0.5f, 0.6f), P(0.62f, 0.48f) }, Px.Hex(0x6b7a72));
                ci.DrawRect(new Rect2(P(0.2f, 0.8f), new Vector2(2, h * 0.48f)), Px.Hex(0xe8f2f6));
                ci.DrawRect(new Rect2(P(0, 0.34f), new Vector2(w, h * 0.12f)), Px.Hex(0x2a5868));
                for (int i = 0; i < 5; i++) ci.DrawRect(new Rect2(P(0.08f + i * 0.18f, 0.3f), new Vector2(w * 0.07f, h * 0.08f)), Px.Hex(i % 2 == 0 ? 0xa83a2a : 0xe0c060));
                break;
            case 8: // a granite dome over the bay, a colourful hill, the beach
                ci.DrawColoredPolygon(new[] { P(0.6f, 0.4f), P(0.64f, 0.8f), P(0.72f, 0.92f), P(0.8f, 0.8f), P(0.84f, 0.4f) }, Px.Hex(0x7a7468));
                ci.DrawRect(new Rect2(P(0, 0.4f), new Vector2(w, h * 0.12f)), Px.Hex(0x2f7a8e));
                ci.DrawColoredPolygon(new[] { P(0, 0.3f), P(0.05f, 0.62f), P(0.3f, 0.7f), P(0.5f, 0.3f) }, Px.Hex(0x3f7a3a));
                for (int i = 0; i < 10; i++)
                {
                    float fx = 0.05f + (i % 5) * 0.075f, fy = 0.36f + (i / 5) * 0.12f + (i % 5) * 0.02f;
                    ci.DrawRect(new Rect2(P(fx, fy), new Vector2(w * 0.05f, h * 0.06f)), Px.Hex(new[] { 0xe8c84a, 0xd85a4a, 0x5aa8d8, 0xf0f0ea, 0xe88a3a }[i % 5]));
                }
                ci.DrawRect(new Rect2(P(0, 0.28f), new Vector2(w, h * 0.1f)), Px.Hex(0xe6d6a8));
                break;
            case 9: // terraced rows, the viaduct, the mill chimney, the moors
                ci.DrawColoredPolygon(new[] { P(0, 0.4f), P(0.4f, 0.62f), P(0.8f, 0.5f), P(1, 0.6f), P(1, 0.2f), P(0, 0.2f) }, Px.Hex(0x6a5a6a));
                for (int i = 0; i < 9; i++) ci.DrawRect(new Rect2(P(0.04f + i * 0.11f, 0.5f), new Vector2(w * 0.03f, h * 0.24f)), Px.Hex(0x7c3626));
                ci.DrawRect(new Rect2(P(0, 0.52f), new Vector2(w, h * 0.05f)), Px.Hex(0x7c3626));
                ci.DrawRect(new Rect2(P(0.78f, 0.9f), new Vector2(w * 0.04f, h * 0.7f)), Px.Hex(0x6f3324));
                for (int i = 0; i < 3; i++) ci.DrawRect(new Rect2(P(0, 0.2f + i * 0.08f), new Vector2(w, h * 0.04f)), Px.Hex(i % 2 == 0 ? 0x8a3d2a : 0x4a4f58));
                break;
            case 10: // stone blocks, the iron tower, the basilica
                ci.DrawColoredPolygon(new[] { P(0.62f, 0.2f), P(0.72f, 0.95f), P(0.82f, 0.2f), P(0.78f, 0.2f), P(0.72f, 0.5f), P(0.66f, 0.2f) }, Px.Hex(0x6b5a48));
                ci.DrawRect(new Rect2(P(0.2f, 0.56f), new Vector2(w * 0.14f, h * 0.18f)), Px.Hex(0xf2efe6));
                ci.DrawCircle(P(0.27f, 0.58f), h * 0.08f, Px.Hex(0xf2efe6));
                for (int i = 0; i < 6; i++) ci.DrawRect(new Rect2(P(0.02f + i * 0.17f, 0.38f), new Vector2(w * 0.14f, h * 0.18f)), Px.Hex(0xe6dcc4));
                for (int i = 0; i < 6; i++) ci.DrawRect(new Rect2(P(0.02f + i * 0.17f, 0.42f), new Vector2(w * 0.14f, h * 0.05f)), Px.Hex(0x6b7178));
                break;
            case 11: // white town on red cliffs over the deep blue
                ci.DrawRect(new Rect2(P(0, 0.42f), new Vector2(w, h * 0.3f)), Px.Hex(0x1f5a8e));
                ci.DrawColoredPolygon(new[] { P(0, 0.75f), P(0.3f, 0.7f), P(0.36f, 0.42f), P(0, 0.42f) }, Px.Hex(0x8a4a36));
                ci.DrawColoredPolygon(new[] { P(1, 0.8f), P(0.66f, 0.72f), P(0.6f, 0.42f), P(1, 0.42f) }, Px.Hex(0x4a403a));
                for (int i = 0; i < 6; i++) ci.DrawRect(new Rect2(P(0.68f + i * 0.05f, 0.82f + (i % 2) * 0.03f), new Vector2(w * 0.04f, h * 0.06f)), Px.Hex(0xf6f4ef));
                ci.DrawCircle(P(0.82f, 0.88f), h * 0.04f, Px.Hex(0x1f5fb8));
                break;
            case 12: // the snowy cone, the pagoda, cherry blossom
                ci.DrawColoredPolygon(new[] { P(0.15f, 0.3f), P(0.45f, 0.95f), P(0.75f, 0.3f) }, Px.Hex(0x6a6a70));
                ci.DrawColoredPolygon(new[] { P(0.38f, 0.8f), P(0.45f, 0.95f), P(0.52f, 0.8f) }, Px.Hex(0xf2f4f6));
                for (int i = 0; i < 4; i++) ci.DrawRect(new Rect2(P(0.78f - i * 0.01f, 0.3f + i * 0.13f), new Vector2(w * (0.12f - i * 0.02f), 3)), Px.Hex(0x2a2c30));
                ci.DrawRect(new Rect2(P(0.82f, 0.82f), new Vector2(w * 0.03f, h * 0.55f)), Px.Hex(0xc9442e));
                for (int i = 0; i < 4; i++) ci.DrawCircle(P(0.08f + i * 0.14f, 0.28f), h * 0.07f, Px.Hex(0xf2b8c8));
                break;
            case 13: // the golden dome, minarets, glass towers, dunes
                ci.DrawRect(new Rect2(P(0, 0.3f), new Vector2(w, h * 0.12f)), Px.Hex(0xe0b878));
                ci.DrawCircle(P(0.3f, 0.42f), h * 0.16f, Px.Hex(0xd9a83a));
                ci.DrawRect(new Rect2(P(0.16f, 0.42f), new Vector2(w * 0.28f, h * 0.12f)), Px.Hex(0xf0e6d0));
                foreach (float x2 in new[] { 0.1f, 0.5f }) ci.DrawRect(new Rect2(P(x2, 0.78f), new Vector2(w * 0.03f, h * 0.48f)), Px.Hex(0xf0e6d0));
                for (int i = 0; i < 4; i++) ci.DrawRect(new Rect2(P(0.64f + i * 0.09f, 0.5f + (i % 2) * 0.35f), new Vector2(w * 0.06f, h * (0.2f + (i % 2) * 0.35f))), Px.Hex(0x8aa6b8));
                break;
            case 14: // the full moon over the mansion on its hill
                ci.DrawRect(r, Px.Hex(0x2a1640));
                ci.DrawCircle(P(0.72f, 0.72f), h * 0.2f, Px.Hex(0xfaf0cc));
                ci.DrawColoredPolygon(new[] { P(0, 0.2f), P(0.35f, 0.45f), P(0.7f, 0.25f), P(1, 0.2f), P(1, 0), P(0, 0) }, Px.Hex(0x15121a));
                ci.DrawRect(new Rect2(P(0.26f, 0.62f), new Vector2(w * 0.18f, h * 0.2f)), Px.Hex(0x15121a));
                ci.DrawColoredPolygon(new[] { P(0.24f, 0.62f), P(0.29f, 0.78f), P(0.34f, 0.62f) }, Px.Hex(0x15121a));
                ci.DrawRect(new Rect2(P(0.3f, 0.54f), new Vector2(3, 3)), Px.Hex(0xffc04a));
                ci.DrawCircle(P(0.12f, 0.16f), h * 0.05f, Px.Hex(0xe0681a));
                break;
            case 15: // the Earth in a black sky, domes, the rocket
                ci.DrawRect(r, Px.Hex(0x05060c));
                ci.DrawCircle(P(0.28f, 0.75f), h * 0.14f, Px.Hex(0x2f6ad0));
                ci.DrawCircle(P(0.25f, 0.78f), h * 0.05f, Px.Hex(0x4f9a4a));
                ci.DrawRect(new Rect2(P(0, 0.3f), new Vector2(w, h * 0.3f)), Px.Hex(0x8c8c8a));
                ci.DrawCircle(P(0.45f, 0.3f), h * 0.1f, Px.Hex(0xeef0f2));
                ci.DrawRect(new Rect2(P(0.78f, 0.8f), new Vector2(w * 0.05f, h * 0.5f)), Px.Hex(0xf4f4f2));
                ci.DrawColoredPolygon(new[] { P(0.78f, 0.8f), P(0.805f, 0.92f), P(0.83f, 0.8f) }, Px.Hex(0xf4f4f2));
                break;
            case 16: // the great tree, snowy hills, warm windows
                ci.DrawColoredPolygon(new[] { P(0, 0.4f), P(0.3f, 0.6f), P(0.7f, 0.45f), P(1, 0.55f), P(1, 0.2f), P(0, 0.2f) }, Px.Hex(0xeef2f6));
                ci.DrawColoredPolygon(new[] { P(0.38f, 0.22f), P(0.5f, 0.88f), P(0.62f, 0.22f) }, Px.Hex(0x1f5a32));
                ci.DrawCircle(P(0.5f, 0.9f), h * 0.04f, Px.Hex(0xffd23a));
                for (int i = 0; i < 5; i++) ci.DrawRect(new Rect2(P(0.44f + (i % 3) * 0.05f, 0.35f + i * 0.1f), new Vector2(3, 3)), Px.Hex(i % 2 == 0 ? 0xff3a2a : 0xffd23a));
                foreach (float x2 in new[] { 0.08f, 0.75f }) { ci.DrawRect(new Rect2(P(x2, 0.36f), new Vector2(w * 0.14f, h * 0.14f)), Px.Hex(0x7a4a2e)); ci.DrawRect(new Rect2(P(x2 + 0.04f, 0.3f), new Vector2(3, 3)), Px.Hex(0xffd27a)); }
                break;
            default: // a skyline across the bay, a beach
                for (int i = 0; i < 6; i++) ci.DrawRect(new Rect2(P(0.45f + i * 0.08f, 0.45f + (i % 3) * 0.1f), new Vector2(w * 0.06f, h * (0.12f + (i % 3) * 0.1f))), Px.Hex(0x8a96a3));
                ci.DrawRect(new Rect2(P(0, 0.33f), new Vector2(w, h * 0.13f)), Px.Hex(0x2f6a86));
                ci.DrawRect(new Rect2(P(0, 0.2f), new Vector2(w, h * 0.08f)), Px.Hex(0xdcc9a0));
                break;
        }
        ci.DrawRect(new Rect2(P(0, 0.12f), new Vector2(w, h * 0.12f)), land);
    }

    void SetArea(int area)
    {
        if (Surroundings.Clamp(Plan.Area) == area) return;
        _ui.Club.SetStadiumArea(area);
        _changedAt = T;
    }

    static IEnumerable<string> TifoSets()
    {
        foreach (var s in Kit.Sets) if (s.CarriesTifo) yield return s.Name;
    }

    /// <summary>A little hanging banner: this set carries the giant tifo.</summary>
    void TifoBadge(Vector2 p, bool on) => TifoBadge(this, p, on);

    static void TifoBadge(CanvasItem ci, Vector2 p, bool on)
    {
        var c = on ? Px.Dark : Px.Gold;
        ci.DrawRect(new Rect2(p, new Vector2(8, 2)), c);
        ci.DrawColoredPolygon(new[] { p + new Vector2(1, 2), p + new Vector2(7, 2), p + new Vector2(7, 11), p + new Vector2(4, 9), p + new Vector2(1, 11) }, c);
    }

    /// <summary>The set's main colour: its own, the club's, or one of the paints; returns the
    /// row's bottom.</summary>
    void Colours(Rect2 r, int set)
    {
        int n = Kit.Paints.Length;
        float gap = 6, w = Mathf.Min(64, (r.Size.X - gap * (n - 1)) / n), h = r.Size.Y - 4;
        uint now = Plan.PaintOf(set);
        for (int i = 0; i < n; i++)
        {
            uint p = Kit.Paints[i];
            var b = new Rect2(r.Position.X + i * (w + gap), r.Position.Y + 2, w, h);
            bool held = Held("paint" + i);
            var bb = held ? new Rect2(b.Position + new Vector2(1, 1), b.Size) : b;
            uint shown = p == 0 ? Kit.Sets[set].Mains[0] : p == Kit.ClubPaint ? (uint)_ui.Club.S.Kit.Main : p;
            DrawRect(bb, Px.Hex((int)shown));
            if (p == 0 || p == Kit.ClubPaint)
            {
                var lum = Px.Hex((int)shown).Luminance;
                Px.TextC(this, Px.Small, bb.GetCenter().X, bb.GetCenter().Y + 4, p == 0 ? "OWN" : "CLUB", 8, lum > 0.5f ? Px.Dark : Px.Ink);
            }
            if (p == now) Px.Ring(this, bb.Grow(3), (T % 0.8) < 0.4 ? Px.Ink : Px.Gold, 3);
            else Px.Ring(this, bb, new Color(0, 0, 0, 0.5f), 1);
            uint pick = p;
            Tap("paint" + i, b.Grow(gap / 2), () => Recolour(set, pick));
        }
    }

    /// <summary>A little elevation of a set: its silhouette in its colour.</summary>
    void Swatch(CanvasItem ci, Rect2 r, int set)
    {
        var c = Px.Hex((int)Kit.Sets[set].Swatch);
        var dark = c.Darkened(0.45f);
        ci.DrawRect(r, new Color(0.05f, 0.04f, 0.14f, 0.85f));
        float x = r.Position.X, y = r.End.Y, w = r.Size.X, h = r.Size.Y;
        Vector2 P(float fx, float fy) => new(Mathf.Round(x + fx * w), Mathf.Round(y - fy * h));
        switch (set)
        {
            case 0: // a bowl under a sweeping roof
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.2f), P(0.55f, 0.62f), P(1, 0.62f), P(1, 0) }, c);
                ci.DrawColoredPolygon(new[] { P(0.1f, 0.86f), P(1, 0.72f), P(1, 0.8f), P(0.1f, 0.92f) }, dark);
                break;
            case 1: // a low terrace under a pitched roof
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.9f, 0.4f), P(0.9f, 0) }, c);
                ci.DrawColoredPolygon(new[] { P(0.05f, 0.55f), P(0.95f, 0.66f), P(0.95f, 0.6f), P(0.05f, 0.5f) }, dark);
                for (int i = 1; i < 4; i++) ci.DrawRect(new Rect2(P(0.08f + i * 0.2f, 0.52f + i * 0.02f), new Vector2(2, h * 0.3f)), dark);
                break;
            case 2: // two open tiers and a ramp tower
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.15f), P(0.4f, 0.4f), P(0.4f, 0.5f), P(0.78f, 0.78f), P(0.82f, 0.78f), P(0.82f, 0) }, c);
                ci.DrawRect(new Rect2(P(0.88f, 0.95f), new Vector2(Mathf.Max(3, w * 0.06f), h * 0.95f)), dark);
                break;
            case 3: // one steep wall and its pylon
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.1f), P(0.68f, 0.82f), P(0.72f, 0.82f), P(0.72f, 0) }, c);
                ci.DrawColoredPolygon(new[] { P(0.12f, 0.88f), P(0.78f, 0.88f), P(0.78f, 0.82f), P(0.12f, 0.82f) }, dark);
                ci.DrawRect(new Rect2(P(0.8f, 1), new Vector2(Mathf.Max(3, w * 0.05f), h)), c.Lightened(0.2f));
                break;
            case 5: // containers stacked, a crane
                for (int i = 0; i < 6; i++) ci.DrawRect(new Rect2(P(0.05f + i % 3 * 0.22f, 0.25f + i / 3 * 0.25f), new Vector2(w * 0.2f, h * 0.22f)), Px.Hex(new[] { 0xc0392b, 0x2f7fb8, 0xe0a030, 0x2e8b57, 0xd35400, 0xbdc3c7 }[i]));
                ci.DrawRect(new Rect2(P(0.8f, 0.95f), new Vector2(3, h * 0.95f)), dark);
                ci.DrawRect(new Rect2(P(0.45f, 0.95f), new Vector2(w * 0.5f, 3)), dark);
                break;
            case 6: // a tier under a swept roof, red pillars
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.8f, 0.45f), P(0.8f, 0) }, Px.Hex(0x6e2a22));
                ci.DrawColoredPolygon(new[] { P(0, 0.62f), P(0.15f, 0.55f), P(0.9f, 0.8f), P(1, 0.72f), P(0.9f, 0.86f), P(0.1f, 0.66f) }, Px.Hex(0x34433f).Lightened(0.2f));
                for (int i = 0; i < 3; i++) ci.DrawRect(new Rect2(P(0.1f + i * 0.25f, 0.6f), new Vector2(2, h * 0.5f)), c);
                break;
            case 7: // stepped cream stand and its clock tower
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.15f), P(0.55f, 0.55f), P(0.62f, 0.55f), P(0.62f, 0.68f), P(0.7f, 0.68f), P(0.7f, 0) }, c);
                ci.DrawRect(new Rect2(P(0.75f, 1), new Vector2(w * 0.16f, h)), c);
                ci.DrawRect(new Rect2(P(0.78f, 0.82f), new Vector2(w * 0.1f, w * 0.1f)), Px.Hex(0xd4a63a));
                break;
            case 8: // black rock, a lava seam, a spire
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.1f), P(0.6f, 0.75f), P(0.7f, 0.9f), P(0.8f, 0.7f), P(1, 0) }, Px.Hex(0x3a3533).Lightened(0.15f));
                ci.DrawRect(new Rect2(P(0.1f, 0.3f), new Vector2(w * 0.5f, 2)), c);
                ci.DrawColoredPolygon(new[] { P(0.85f, 0), P(0.9f, 1), P(0.95f, 0) }, Px.Hex(0x4d4642).Lightened(0.2f));
                ci.DrawRect(new Rect2(P(0.88f, 1.0f), new Vector2(4, 3)), Px.Hex(0xffb347));
                break;
            case 9: // white tiers under a floating halo
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.65f, 0.55f), P(0.7f, 0.55f), P(0.7f, 0) }, Px.Hex(0xe6eaee));
                ci.DrawRect(new Rect2(P(0.05f, 0.85f), new Vector2(w * 0.85f, 3)), c);
                ci.DrawRect(new Rect2(P(0.78f, 0.85f), new Vector2(2, h * 0.85f)), Px.Hex(0xe6eaee));
                break;
            case 10: // a tier under peaked white fabric on masts
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.75f, 0.5f), P(0.75f, 0) }, Px.Hex(0x8d949b));
                ci.DrawColoredPolygon(new[] { P(0, 0.7f), P(0.3f, 0.95f), P(0.55f, 0.72f), P(0.8f, 0.95f), P(0.95f, 0.78f), P(0.95f, 0.72f), P(0.8f, 0.86f), P(0.55f, 0.64f), P(0.3f, 0.86f), P(0, 0.62f) }, c);
                ci.DrawRect(new Rect2(P(0.3f, 1), new Vector2(2, h)), dark);
                ci.DrawRect(new Rect2(P(0.8f, 1), new Vector2(2, h)), dark);
                break;
            case 11: // raw concrete, raking frames and a heavy slab
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.7f, 0.6f), P(0.7f, 0) }, c);
                ci.DrawRect(new Rect2(P(0, 0.86f), new Vector2(w * 0.85f, h * 0.1f)), dark);
                ci.DrawColoredPolygon(new[] { P(0.6f, 0.86f), P(0.66f, 0.86f), P(0.9f, 0.3f), P(0.9f, 0) , P(0.84f, 0), P(0.84f, 0.3f) }, dark);
                break;
            case 12: // three steep stacked tiers in blue and gold
                for (int i = 0; i < 3; i++)
                    ci.DrawColoredPolygon(new[] { P(0.12f + i * 0.25f, 0.06f + i * 0.3f), P(0.12f + i * 0.25f, 0.12f + i * 0.3f), P(0.4f + i * 0.25f, 0.34f + i * 0.3f), P(0.4f + i * 0.25f, 0.28f + i * 0.3f) }, i % 2 == 0 ? c : Px.Hex(0xf2c230));
                ci.DrawRect(new Rect2(P(0.92f, 0.98f), new Vector2(3, h * 0.98f)), dark);
                break;
            case 13: // a timber tier under an arched green roof
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.8f, 0.42f), P(0.8f, 0) }, c);
                ci.DrawColoredPolygon(new[] { P(0, 0.6f), P(0.3f, 0.8f), P(0.65f, 0.86f), P(0.95f, 0.74f), P(0.95f, 0.66f), P(0.65f, 0.78f), P(0.3f, 0.72f), P(0, 0.54f) }, Px.Hex(0x6f8a3c));
                ci.DrawColoredPolygon(new[] { P(0.48f, 0), P(0.52f, 0), P(0.52f, 0.5f), P(0.6f, 0.78f), P(0.56f, 0.78f), P(0.5f, 0.58f), P(0.44f, 0.76f), P(0.4f, 0.76f), P(0.48f, 0.5f) }, dark);
                break;
            case 14: // a glowing cushioned bowl
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.55f), P(0.3f, 0.8f), P(0.62f, 0.86f), P(0.88f, 0.7f), P(1, 0.4f), P(1, 0) }, Px.Hex(0xe8eef2));
                for (int i = 0; i < 4; i++) ci.DrawRect(new Rect2(P(0.1f + i * 0.22f, 0.5f), new Vector2(w * 0.12f, 2)), c);
                ci.DrawRect(new Rect2(P(0.05f, 0.25f), new Vector2(w * 0.9f, 2)), c);
                break;
            case 15: // a red bowl in a woven steel lattice
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.62f, 0.62f), P(0.68f, 0.7f), P(0.68f, 0) }, Px.Hex(0x9e2b25));
                for (int i = 0; i < 5; i++)
                {
                    ci.DrawLine(P(0.06f + i * 0.2f, 0), P(0.3f + i * 0.2f, 0.82f), c, 2);
                    ci.DrawLine(P(0.3f + i * 0.2f, 0), P(0.06f + i * 0.2f, 0.82f), c, 2);
                }
                ci.DrawLine(P(0, 0.82f), P(1, 0.82f), c, 2);
                break;
            case 16: // two tiers under a great white arch
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.1f), P(0.4f, 0.3f), P(0.4f, 0.36f), P(0.72f, 0.56f), P(0.76f, 0.56f), P(0.76f, 0) }, Px.Hex(0x3c4450).Lightened(0.25f));
                ci.DrawColoredPolygon(new[] { P(0.05f, 0.6f), P(0.8f, 0.6f), P(0.8f, 0.55f), P(0.05f, 0.56f) }, Px.Hex(0x2e333a).Lightened(0.3f));
                for (int i = 0; i < 12; i++)
                {
                    float a0 = i / 12f * Mathf.Pi, a1 = (i + 1) / 12f * Mathf.Pi;
                    ci.DrawLine(P(0.5f - 0.48f * Mathf.Cos(a0), 0.98f * Mathf.Sin(a0)), P(0.5f - 0.48f * Mathf.Cos(a1), 0.98f * Mathf.Sin(a1)), c, 3);
                }
                break;
            case 17: // a black box striped with neon
            {
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.7f, 0.66f), P(0.76f, 0.66f), P(0.76f, 0.86f), P(0.06f, 0.86f), P(0.06f, 0.8f), P(0.84f, 0.8f), P(0.84f, 0) }, Px.Hex(0x18191d).Lightened(0.12f));
                var neon = Px.Hex(_ui.Club.S.Kit.Main).Lightened(0.2f);
                for (int i = 0; i < 4; i++) ci.DrawRect(new Rect2(P(0.76f, 0.16f + i * 0.17f), new Vector2(w * 0.08f, 2)), neon);
                ci.DrawRect(new Rect2(P(0.06f, 0.84f), new Vector2(w * 0.78f, 2)), neon);
                break;
            }
            case 18: // mud brick, pinnacles, beams poking out
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.62f, 0.5f), P(0.62f, 0.62f), P(0.74f, 0.62f), P(0.74f, 0) }, c);
                for (int i = 0; i < 3; i++)
                {
                    float px = 0.64f + i * 0.045f;
                    ci.DrawColoredPolygon(new[] { P(px - 0.018f, 0.62f), P(px, 0.76f), P(px + 0.018f, 0.62f) }, c);
                    ci.DrawRect(new Rect2(P(0.74f, 0.18f + i * 0.15f), new Vector2(w * 0.07f, 2)), Px.Hex(0x4f3420));
                }
                ci.DrawColoredPolygon(new[] { P(0.84f, 0), P(0.86f, 0.9f), P(0.9f, 1), P(0.94f, 0.9f), P(0.96f, 0) }, c.Darkened(0.15f));
                break;
            case 19: // grass banks and a tree
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.1f), P(0.55f, 0.3f), P(0.62f, 0.34f), P(1, 0.1f), P(1, 0) }, c);
                ci.DrawRect(new Rect2(P(0, 0.14f), new Vector2(w * 0.3f, 2)), Px.Hex(0xebe7dc));
                ci.DrawRect(new Rect2(P(0.76f, 0.5f), new Vector2(3, h * 0.3f)), Px.Hex(0x5a4030));
                ci.DrawCircle(P(0.77f, 0.62f), h * 0.2f, Px.Hex(0x3f6e30));
                ci.DrawRect(new Rect2(P(0.4f, 0.9f), new Vector2(2, h * 0.65f)), Px.Hex(0x8a8f96));
                ci.DrawRect(new Rect2(P(0.37f, 0.92f), new Vector2(8, 4)), Px.Hex(0xfff2c8));
                break;
            case 20: // three tiers stacked high under a roof, a skin of stars
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.08f), P(0.3f, 0.3f), P(0.3f, 0.36f), P(0.52f, 0.56f), P(0.52f, 0.62f), P(0.74f, 0.86f), P(0.82f, 0.9f), P(0.82f, 0) }, c);
                ci.DrawRect(new Rect2(P(0.28f, 0.36f), new Vector2(w * 0.04f, 2)), Px.Ink);
                ci.DrawRect(new Rect2(P(0.5f, 0.62f), new Vector2(w * 0.04f, 2)), Px.Ink);
                ci.DrawColoredPolygon(new[] { P(0.4f, 0.97f), P(0.4f, 0.93f), P(0.9f, 0.9f), P(0.9f, 0.95f) }, c.Darkened(0.4f));
                for (int i = 0; i < 9; i++) ci.DrawRect(new Rect2(P(0.84f + (i % 3) * 0.045f, 0.12f + i * 0.08f), new Vector2(2, 2)), Px.Ink);
                break;
            case 21: // a purple stand, a pumpkin, a spire and a bat
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.1f), P(0.62f, 0.5f), P(0.62f, 0.6f), P(0.72f, 0.6f), P(0.72f, 0) }, c);
                ci.DrawRect(new Rect2(P(0.2f, 0.26f), new Vector2(w * 0.08f, 2)), Px.Hex(0xff7a1a));
                ci.DrawCircle(P(0.3f, 0.3f), h * 0.07f, Px.Hex(0xe8701a));
                ci.DrawRect(new Rect2(P(0.78f, 0.7f), new Vector2(w * 0.12f, h * 0.7f)), c.Darkened(0.2f));
                ci.DrawColoredPolygon(new[] { P(0.76f, 0.7f), P(0.84f, 1), P(0.92f, 0.7f) }, Px.Hex(0x16121c));
                ci.DrawRect(new Rect2(P(0.82f, 0.5f), new Vector2(3, 3)), Px.Hex(0xffc23a));
                ci.DrawColoredPolygon(new[] { P(0.3f, 0.85f), P(0.38f, 0.8f), P(0.46f, 0.85f), P(0.38f, 0.78f) }, Px.Hex(0x16121c));
                break;
            default: // walls, battlements and a keep
                ci.DrawColoredPolygon(new[] { P(0, 0), P(0, 0.12f), P(0.62f, 0.55f), P(0.62f, 0.6f), P(0.7f, 0.6f), P(0.7f, 0) }, c);
                for (int i = 0; i < 4; i++) ci.DrawRect(new Rect2(P(0.6f + (i % 2) * 0.06f, 0.68f), new Vector2(3, 3)), c);
                ci.DrawRect(new Rect2(P(0.78f, 0.78f), new Vector2(w * 0.14f, h * 0.78f)), c);
                ci.DrawColoredPolygon(new[] { P(0.76f, 0.78f), P(0.94f, 0.78f), P(0.85f, 1) }, Px.Hex(_ui.Club.S.Kit.Main));
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
        for (int i = 0; i < 4; i++) Px.TextC(this, Px.Small, M(-bx - D / 2, 0).X, M(-bx - D / 2, 0).Y + 4 + (i - 1.5f) * 10, "HOME"[i].ToString(), 8, Px.Dark);
        for (int i = 0; i < 4; i++) Px.TextC(this, Px.Small, M(bx + D / 2, 0).X, M(bx + D / 2, 0).Y + 4 + (i - 1.5f) * 10, "AWAY"[i].ToString(), 8, Px.Dark);
    }
}
