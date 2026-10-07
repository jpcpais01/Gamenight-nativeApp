using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Menus;

/// <summary>
/// A menu surface drawn in immediate mode: Paint() draws everything and registers tap areas as
/// it goes, so what you see is exactly what you can press. A press highlights its area; it fires
/// on release over the same area. Optionally the content scrolls (drag, fling) on one axis.
/// Redraws every frame while visible, which is cheap for a handful of boxes.
/// </summary>
public abstract partial class PxCanvas : Control
{
    readonly struct Hit
    {
        public readonly string Key;
        public readonly Rect2 R;
        public readonly Action Tap;
        public Hit(string key, Rect2 r, Action tap)
        {
            Key = key;
            R = r;
            Tap = tap;
        }
    }

    readonly List<Hit> _hits = new();
    string _press;
    Vector2 _pressAt, _last;
    bool _down, _dragging, _inside;
    double _lastT;

    /// <summary>Scroll axis: 0 none, 1 vertical, 2 horizontal.</summary>
    protected int ScrollAxis;
    /// <summary>Current scroll offset (pixels) and the content's length on the scroll axis.</summary>
    protected float Scroll, Content;
    float _vel;

    /// <summary>Seconds since this surface was shown (for stepped animations).</summary>
    protected double T;

    protected PxCanvas()
    {
        MouseFilter = MouseFilterEnum.Stop;
    }

    protected abstract void Paint();

    public override void _Draw()
    {
        _hits.Clear();
        foreach (var l in _layers.Values) l.Used = false;
        Paint();
        foreach (var l in _layers.Values)
            if (!l.Used && l.Visible) l.Visible = false;
    }

    /// <summary>A piece of the picture on its own layer above this surface, drawn once and only
    /// redrawn when `stamp` changes (a heavy, mostly still thing such as a player card: moving it
    /// is free; `top` keeps it above the others). `draw` gets the layer and the rect in its own coordinates.</summary>
    protected void Layer(string key, Rect2 r, object stamp, Action<CanvasItem, Rect2> draw, bool top = false)
    {
        if (!_layers.TryGetValue(key, out var l))
        {
            _layers[key] = l = new LayerItem();
            CallDeferred(Node.MethodName.AddChild, l);
        }
        l.Used = true;
        if (!l.Visible) l.Visible = true;
        var at = r.Position.Round();
        if (l.Position != at) l.Position = at;
        if (top && l.GetParent() == this && l.GetIndex() != GetChildCount() - 1) l.CallDeferred(CanvasItem.MethodName.MoveToFront);
        var size = r.Size;
        if (l.Size != size || !Equals(l.Stamp, stamp))
        {
            l.Stamp = stamp;
            l.Size = size;
            l.Draw = draw;
            l.QueueRedraw();
        }
    }

    readonly Dictionary<string, LayerItem> _layers = new();

    sealed partial class LayerItem : Node2D
    {
        public bool Used;
        public object Stamp;
        public Vector2 Size;
        public Action<CanvasItem, Rect2> Draw;

        public override void _Draw() => Draw?.Invoke(this, new Rect2(Vector2.Zero, Size));
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        T += delta;
        if (ScrollAxis != 0 && !_down && _vel != 0)
        {
            Scroll += _vel * (float)delta;
            _vel *= Mathf.Pow(0.04f, (float)delta);
            if (Mathf.Abs(_vel) < 8) _vel = 0;
        }
        ClampScroll();
        QueueRedraw();
    }

    protected float ScrollMax => Mathf.Max(0, Content - (ScrollAxis == 2 ? Size.X : Size.Y));

    void ClampScroll()
    {
        float max = ScrollMax;
        if (Scroll < 0 || Scroll > max)
        {
            Scroll = Mathf.Clamp(Scroll, 0, max);
            _vel = 0;
        }
    }

    /// <summary>Glide the scroll about `px` (it eases to a stop).</summary>
    protected void Glide(float px) => _vel = px * 3.22f;

    public void ResetScroll()
    {
        Scroll = 0;
        _vel = 0;
    }

    /// <summary>Registers a tap area (local coordinates). Later areas win over earlier ones.</summary>
    protected void Tap(string key, Rect2 r, Action tap) => _hits.Add(new Hit(key, r, tap));

    /// <summary>Is this area being held down right now?</summary>
    protected bool Held(string key) => _down && !_dragging && _inside && _press == key;

    /// <summary>The finger turned out to be dragging something else (a map): the press won't fire.</summary>
    protected void CancelPress()
    {
        if (_down) _dragging = true;
    }

    /// <summary>Is there a tap area here?</summary>
    protected bool OnTapArea(Vector2 p) => HitAt(p) != null;

    Hit? HitAt(Vector2 p)
    {
        for (int i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].R.HasPoint(p)) return _hits[i];
        return null;
    }

    /// <summary>A tap that wasn't on any tap area.</summary>
    protected virtual void Background() { }

    public override void _GuiInput(InputEvent e)
    {
        // The mouse wheel (and touchpad scroll) on a computer.
        if (ScrollAxis != 0 && e is InputEventMouseButton { Pressed: true } wb && wb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight)
        {
            float step = 48 * (wb.Factor > 0 ? wb.Factor : 1);
            Scroll += wb.ButtonIndex is MouseButton.WheelDown or MouseButton.WheelRight ? step : -step;
            _vel = 0;
            AcceptEvent();
            return;
        }
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _down = true;
                _dragging = false;
                _pressAt = _last = mb.Position;
                _lastT = Time.GetTicksMsec() / 1000.0;
                _vel = 0;
                _press = HitAt(mb.Position)?.Key;
                _inside = true;
            }
            else if (_down)
            {
                _down = false;
                if (!_dragging)
                {
                    var h = HitAt(mb.Position);
                    if (h is Hit hit && hit.Key == _press)
                    {
                        Audio.GameAudio.Instance?.UiTap();
                        hit.Tap?.Invoke();
                    }
                    else if (_press == null) Background();
                }
                _press = null;
            }
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion mm && _down)
        {
            if (ScrollAxis != 0)
            {
                float d = ScrollAxis == 2 ? mm.Position.X - _last.X : mm.Position.Y - _last.Y;
                float from = ScrollAxis == 2 ? mm.Position.X - _pressAt.X : mm.Position.Y - _pressAt.Y;
                if (!_dragging && Mathf.Abs(from) > 10 && ScrollMax > 0) _dragging = true;
                if (_dragging)
                {
                    Scroll -= d;
                    double now = Time.GetTicksMsec() / 1000.0;
                    double dt = Math.Max(1e-3, now - _lastT);
                    _vel = Mathf.Lerp(_vel, (float)(-d / dt), 0.5f);
                    _lastT = now;
                }
            }
            _last = mm.Position;
            _inside = HitAt(_last)?.Key == _press;
            AcceptEvent();
        }
    }

    // ---------------------------------------------------------------- shared widgets

    /// <summary>A gold key: the main action. Pressed, it sinks into its shadow.</summary>
    protected void GoldButton(string key, Rect2 r, string label, int size, Action tap, bool enabled = true)
    {
        bool held = Held(key) && enabled;
        var rr = held ? new Rect2(r.Position + new Vector2(3, 3), r.Size) : r;
        if (enabled)
        {
            Px.Frame(this, rr, Colors.Transparent, Px.Hex(0x6b3f00), held ? null : new Color(0, 0, 0, 0.45f));
            Px.Bands(this, rr.Grow(-3), Px.GoldBands, Px.GoldStops);
        }
        else Px.Frame(this, rr, new Color(0.3f, 0.28f, 0.36f), Px.Line2, Px.ShadowSoft);
        Px.TextC(this, Px.Big, rr.GetCenter().X, rr.GetCenter().Y + size * 0.34f, label, size, enabled ? Px.Dark : Px.InkDim);
        Tap(key, r, tap);
    }

    /// <summary>A plain dark button.</summary>
    protected void GhostButton(string key, Rect2 r, string label, int size, Action tap, Color? ring = null, Color? ink = null)
    {
        bool held = Held(key);
        var rr = held ? new Rect2(r.Position + new Vector2(3, 3), r.Size) : r;
        Px.Frame(this, rr, Px.Glass2, ring ?? Px.Line2, held ? null : Px.ShadowSoft);
        Px.TextC(this, Px.Big, rr.GetCenter().X, rr.GetCenter().Y + size * 0.34f, label, size, ink ?? Px.Ink);
        Tap(key, r, tap);
    }

    /// <summary>A chip: small toggle (gold when on).</summary>
    protected float Chip(string key, Vector2 p, string label, bool on, Action tap, int size = 18, float h = 26)
    {
        float w = Px.Width(Px.Big, label, size) + 18;
        var r = new Rect2(p, new Vector2(w, h));
        bool held = Held(key);
        var rr = held ? new Rect2(r.Position + new Vector2(2, 2), r.Size) : r;
        Px.Frame(this, rr, on ? Px.Gold : new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.85f), on ? Px.Hex(0xb37400) : Px.Line2, held ? null : Px.ShadowSoft, 3, 4);
        Px.TextC(this, Px.Big, rr.GetCenter().X, rr.GetCenter().Y + size * 0.34f, label, size, on ? Px.Dark : Px.Ink);
        Tap(key, r, tap);
        return w;
    }

    /// <summary>The square back key, top left of a screen.</summary>
    protected void BackButton(Vector2 p, Action tap)
    {
        var r = new Rect2(p, new Vector2(40, 36));
        bool held = Held("back");
        var rr = held ? new Rect2(r.Position + new Vector2(2, 2), r.Size) : r;
        Px.Frame(this, rr, Px.Glass2, Px.Line2, held ? null : Px.ShadowSoft);
        // A pixel chevron.
        var c = rr.GetCenter();
        for (int i = 0; i < 4; i++)
        {
            DrawRect(new Rect2(c.X - 4 + i * 3, c.Y - 3 - i * 3, 3, 3), Px.Ink);
            DrawRect(new Rect2(c.X - 4 + i * 3, c.Y + i * 3, 3, 3), Px.Ink);
        }
        Tap("back", r, tap);
    }

    /// <summary>Coins: a gold slab with the coin and the count.</summary>
    protected void Coins(float right, float y, long coins)
    {
        string s = Px.Thousands(coins);
        float w = Px.Width(Px.Big, s, 26) + 44;
        var r = new Rect2(right - w, y, w, 34);
        Px.Frame(this, r, Colors.Transparent, Px.Hex(0xb37400), Px.Shadow);
        Px.Bands(this, r.Grow(-3), new[] { Px.Hex(0x3a2a06), Px.Hex(0x2a1e04) }, new[] { 0, 0.5f });
        Px.Coin(this, r.Position + new Vector2(10, 9), 16);
        Px.Text(this, Px.Big, r.Position + new Vector2(34, 26), s, 26, Px.Hex(0xffe066), Colors.Black);
    }

    /// <summary>The screen title with its hard shadow.</summary>
    protected void Title(Vector2 baseline, string s, int size = 38) => Px.Text(this, Px.Big, baseline, s, size, Px.Ink, new Color(0, 0, 0, 0.55f), 3);

    /// <summary>The night backdrop of the sub-screens: hard bands, then scanlines.</summary>
    protected void NightBackdrop(Color[] bands = null)
    {
        var r = new Rect2(Vector2.Zero, Size);
        Px.Bands(this, r, bands ?? new[] { Px.Hex(0x0d0b2a), Px.Hex(0x12103a), Px.Hex(0x181452), Px.Hex(0x1f1a62) }, new[] { 0, 0.2f, 0.45f, 0.7f });
    }
}
