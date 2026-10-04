using System;
using System.Collections.Generic;
using Godot;
using GameNight.Sim;

namespace GameNight.UI;

/// <summary>
/// The PWA's FIFA-Mobile style controls (src/ui/controls.ts), drawn by the engine: a floating
/// joystick on the left, Pass / Through / Kick (Tackle / Switch / Press in defence) around a
/// big Sprint button on the right. Sliding up on a held Pass or Through lofts it; in defence,
/// sliding down on Sprint tackles and sliding left slides in. Layout is in the same units as
/// the PWA's CSS pixels.
/// </summary>
public sealed partial class TouchControls : Control
{
    /// <summary>What the buttons say (the PWA's modes): open play, lining up your corner or goal
    /// kick (what each button does with the ball on the ring), in goal at training, and after
    /// your goal, when they pick the celebration (in the match's Celebrations order).</summary>
    public enum Mode { Attack, Defend, Corner, GoalKick, Keeper, Celebrate }

    static readonly string[][] Labels =
    {
        new[] { "PASS", "THROUGH", "KICK", "SPRINT" },
        new[] { "TACKLE", "SWITCH", "PRESS", "SPRINT" },
        new[] { "WHIP", "SHORT", "FLOAT", "SPRINT" },
        new[] { "DRIVE", "SHORT", "FLOAT", "SPRINT" },
        new[] { "DIVE", "DIVE", "DIVE", "QUICK\nSTEP" },
        new[] { "KNEE\nSLIDE", "AERO\nPLANE", "SIUU", "BACK\nFLIP" },
    };

    public readonly InputState Input = new();
    public Mode Current { get; private set; } = Mode.Attack;
    /// <summary>The celebration picked (0-3), -1 for none yet.</summary>
    public int Picked { get; private set; } = -1;
    double _modeAt;

    const float JoyR = 56, JoyBaseR = 64, KnobR = 28;
    const float Dead = 0.12f;
    const float ShotFull = 0.85f;

    // Buttons 0-2 are A, B, C; 3 is Sprint. Centre (from the bottom-right corner) and radius.
    static readonly Vector2[] BtnOffset = { new(148 + 35, 26 + 35), new(124 + 35, 114 + 35), new(40 + 37, 144 + 37), new(24 + 54, 24 + 54) };
    static readonly float[] BtnRadius = { 35, 35, 37, 54 };

    static readonly Color Ink = new(0.957f, 0.937f, 0.89f);
    static readonly Color Base = new(20 / 255f, 26 / 255f, 22 / 255f, 0.55f);
    static readonly Color Rim = new(0.957f, 0.937f, 0.89f, 0.4f);
    static readonly Color Red = new(200 / 255f, 57 / 255f, 59 / 255f, 0.6f);
    static readonly Color Blue = new(35 / 255f, 52 / 255f, 94 / 255f, 0.6f);
    static readonly Color Accent = new(1f, 0.82f, 0.35f);

    enum Role { Joy, A, B, C, Sprint }

    readonly Dictionary<int, Role> _pointers = new();
    readonly ulong[] _downAt = new ulong[3];
    readonly float[] _startY = new float[3];
    Vector2 _joyCentre, _knob;
    int _joyId = -1;
    Vector2 _sprintStart;
    int _sprintSwipe;
    bool _sprintDown;
    bool _keySprint;
    readonly HashSet<Key> _keys = new();
    Font _font;

    public TouchControls()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Ready()
    {
        _font = Style.Font(true, 1);
        Resized += ResetJoy;
        ResetJoy();
    }

    public void SetMode(Mode m, int picked = -1)
    {
        if (m == Current && picked == Picked) return;
        if (m != Current || picked != Picked) _modeAt = Time.GetTicksMsec() / 1000.0;
        Current = m;
        Picked = picked;
        QueueRedraw();
    }

    Vector2 BtnCentre(int i) => Size - BtnOffset[i];

    /// <summary>Let go of everything (the pause menu opened): like every finger lifting at once.</summary>
    public void ReleaseAll()
    {
        foreach (var id in new List<int>(_pointers.Keys)) TouchUp(id);
        for (int i = 0; i < 3; i++) Release(i);
        _keys.Clear();
        _keySprint = false;
        Input.MoveX = Input.MoveY = 0;
        Input.Sprint = false;
    }

    void ResetJoy()
    {
        _joyCentre = new Vector2(MathF.Max(110, Size.X * 0.12f), Size.Y - MathF.Max(100, Size.Y * 0.26f));
        _knob = Vector2.Zero;
        QueueRedraw();
    }

    /// <summary>Per frame: hold timers, sprint, keyboard stick.</summary>
    public void Tick(float dt)
    {
        for (int i = 0; i < 3; i++)
            if (Input.Held[i]) Input.HoldTime[i] += dt;
        Input.Sprint = _sprintDown || _keySprint;
        if (_joyId < 0)
        {
            float x = 0, y = 0;
            if (_keys.Contains(Key.Left) || _keys.Contains(Key.A)) x -= 1;
            if (_keys.Contains(Key.Right) || _keys.Contains(Key.D)) x += 1;
            if (_keys.Contains(Key.Up) || _keys.Contains(Key.W)) y += 1;
            if (_keys.Contains(Key.Down) || _keys.Contains(Key.S)) y -= 1;
            float m = MathF.Sqrt(x * x + y * y);
            Input.MoveX = m > 0 ? x / m : 0;
            Input.MoveY = m > 0 ? y / m : 0;
        }
        // The shot power ring needs a redraw only while it fills.
        if (Current != Mode.Defend && Input.Held[2]) QueueRedraw();
        // The celebration buttons breathe.
        if (Current == Mode.Celebrate) QueueRedraw();
    }

    public override void _Input(InputEvent e)
    {
        switch (e)
        {
            case InputEventScreenTouch t:
            {
                var p = ((InputEventScreenTouch)MakeInputLocal(t)).Position;
                if (t.Pressed) TouchDown(t.Index, p);
                else TouchUp(t.Index);
                break;
            }
            case InputEventScreenDrag d:
                TouchMove(d.Index, ((InputEventScreenDrag)MakeInputLocal(d)).Position);
                break;
            case InputEventKey k when !k.Echo:
                Keyboard(k);
                break;
        }
    }

    void TouchDown(int id, Vector2 p)
    {
        // Buttons first (they sit on top), nearest within its radius plus a little slack.
        int hit = -1;
        float best = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            float d = p.DistanceTo(BtnCentre(i));
            if (d < BtnRadius[i] + 8 && d < best) { best = d; hit = i; }
        }
        if (hit >= 0 && hit < 3)
        {
            _pointers[id] = (Role)(hit + 1);
            _startY[hit] = p.Y;
            Press(hit);
        }
        else if (hit == 3)
        {
            _pointers[id] = Role.Sprint;
            _sprintDown = true;
            _sprintStart = p;
            _sprintSwipe = 0;
            QueueRedraw();
        }
        else if (_joyId < 0 && p.X < Size.X * 0.46f && p.Y > Size.Y * 0.22f)
        {
            _pointers[id] = Role.Joy;
            _joyId = id;
            _joyCentre = p;
            JoyMove(p);
        }
    }

    void TouchMove(int id, Vector2 p)
    {
        if (!_pointers.TryGetValue(id, out var role)) return;
        if (role == Role.Joy) JoyMove(p);
        else if (role == Role.Sprint && Current == Mode.Defend)
        {
            float down = p.Y - _sprintStart.Y, left = _sprintStart.X - p.X;
            int stage = left > 28 && left > down ? 2 : down > 28 ? 1 : 0;
            if (stage > _sprintSwipe)
            {
                _sprintSwipe = stage;
                Input.TackleSwipe = stage == 2 ? TackleSwipe.Slide : TackleSwipe.Tackle;
                QueueRedraw();
            }
        }
        else if (role is Role.A or Role.B)
        {
            int i = (int)role - 1;
            bool up = _startY[i] - p.Y > 26;
            if (up != Input.Swipe[i]) { Input.Swipe[i] = up; QueueRedraw(); }
        }
    }

    void TouchUp(int id)
    {
        if (!_pointers.Remove(id, out var role)) return;
        switch (role)
        {
            case Role.Joy:
                _joyId = -1;
                Input.MoveX = Input.MoveY = 0;
                ResetJoy();
                break;
            case Role.Sprint:
                _sprintDown = false;
                _sprintSwipe = 0;
                QueueRedraw();
                break;
            default:
                Release((int)role - 1);
                break;
        }
    }

    void JoyMove(Vector2 p)
    {
        var d = p - _joyCentre;
        float len = d.Length();
        if (len > JoyR)
        {
            // Drag the base along so the stick never runs out.
            _joyCentre += d / len * (len - JoyR);
            d = d / len * JoyR;
            len = JoyR;
        }
        _knob = d;
        float m = MathF.Min(1, len / JoyR);
        float mm = m < Dead ? 0 : (m - Dead) / (1 - Dead);
        float n = MathF.Max(1e-6f, len);
        Input.MoveX = d.X / n * mm;
        Input.MoveY = -d.Y / n * mm;
        QueueRedraw();
    }

    void Press(int i)
    {
        if (Input.Held[i]) return;
        Input.Held[i] = true;
        Input.HoldTime[i] = 0;
        _downAt[i] = Time.GetTicksMsec();
        Input.Events.Add(ButtonEvent.Down(i));
        QueueRedraw();
    }

    void Release(int i)
    {
        if (!Input.Held[i]) return;
        Input.Held[i] = false;
        float hold = (Time.GetTicksMsec() - _downAt[i]) / 1000f;
        Input.Events.Add(ButtonEvent.Up(i, hold, Input.Swipe[i]));
        Input.HoldTime[i] = 0;
        Input.Swipe[i] = false;
        QueueRedraw();
    }

    void Keyboard(InputEventKey k)
    {
        bool down = k.Pressed;
        switch (k.Keycode)
        {
            case Key.J: if (down) Press(0); else Release(0); return;
            case Key.K: if (down) Press(1); else Release(1); return;
            case Key.L:
            case Key.Space: if (down) Press(2); else Release(2); return;
            case Key.U:
            case Key.O:
            {
                int b = k.Keycode == Key.U ? 0 : 1;
                if (down) { Press(b); Input.Swipe[b] = true; }
                else Release(b);
                return;
            }
            case Key.N:
                if (down && Current == Mode.Defend) Input.TackleSwipe = k.ShiftPressed ? TackleSwipe.Slide : TackleSwipe.Tackle;
                return;
            case Key.Shift: _keySprint = down; return;
        }
        if (down) _keys.Add(k.Keycode);
        else _keys.Remove(k.Keycode);
    }

    // ------------------------------------------------------------------ drawing

    public override void _Draw()
    {
        // Joystick: base ring and knob (faint at rest).
        float a = _joyId >= 0 ? 1f : 0.55f;
        DrawCircle(_joyCentre, JoyBaseR, new Color(Base, 0.18f * a));
        DrawArc(_joyCentre, JoyBaseR, 0, MathF.Tau, 48, new Color(Ink, 0.28f * a), 2, true);
        DrawCircle(_joyCentre + _knob, KnobR, new Color(Ink, 0.85f * a));

        var labels = Labels[(int)Current];
        bool defend = Current == Mode.Defend, cel = Current == Mode.Celebrate;
        double now = Time.GetTicksMsec() / 1000.0;
        for (int i = 0; i < 4; i++)
        {
            bool down = i < 3 ? Input.Held[i] : _sprintDown;
            var c = BtnCentre(i);
            float r = BtnRadius[i] * (down ? 0.92f : 1f);
            int size = i == 3 ? 17 : i == 1 ? 12 : i == 2 ? 14 : 13;
            if (cel)
            {
                DrawCelebrate(i, c, r, now);
                continue;
            }
            Color fill = Base;
            if (i == 2 && !defend) fill = Red;
            else if (defend && (i == 0 || i == 3)) fill = Blue;
            if (down) fill = new Color(Ink, 0.32f);
            DrawCircle(c, r, fill);
            DrawArc(c, r, 0, MathF.Tau, 48, i == 2 && !defend ? new Color(1, 0.86f, 0.82f, 0.6f) : i == 3 ? new Color(Ink, 0.55f) : Rim, 2, true);
            if (i < 2 && Input.Swipe[i]) DrawArc(c, r - 4, -MathF.PI * 0.85f, -MathF.PI * 0.15f, 16, Accent, 3, true);

            DrawLabel(c + new Vector2(0, defend && i == 3 ? -6 : 0), labels[i], size, Ink);
            if (defend && i == 3) DrawLabel(c + new Vector2(0, 12), "▼ TACKLE · ◀ SLIDE", 9, Ink);
        }

        if (cel)
        {
            // CELEBRATE over the buttons, until one is picked.
            if (Picked < 0) DrawLabel(new Vector2(Size.X - 140, Size.Y - 240), "C E L E B R A T E", 13, new Color(1, 0.88f, 0.54f));
            return;
        }

        // Shot power ring around Kick while it's held.
        if (!defend && Input.Held[2])
        {
            float p = (float)Math.Min(1, Input.HoldTime[2] / ShotFull);
            var c = BtnCentre(2);
            DrawArc(c, BtnRadius[2] + 5, 0, MathF.Tau, 48, new Color(1, 1, 1, 0.12f), 5, true);
            DrawArc(c, BtnRadius[2] + 5, -MathF.PI / 2, -MathF.PI / 2 + MathF.Tau * p, 48, Accent, 5, true);
        }
    }

    static readonly Color Gold = new(0.86f, 0.6f, 0.16f, 0.62f), GoldDeep = new(0.47f, 0.31f, 0.04f, 0.6f);
    static readonly Color GoldRim = new(1, 0.89f, 0.55f, 0.95f), GoldInk = new(1, 0.97f, 0.86f);

    /// <summary>After your goal: gold buttons breathing in turn; once one is picked it pops and
    /// glows, and the others shrink away.</summary>
    void DrawCelebrate(int i, Vector2 c, float r, double now)
    {
        float since = (float)(now - _modeAt);
        int size = i == 3 ? 15 : 12;
        var label = Labels[(int)Mode.Celebrate][i];
        if (Picked >= 0 && Picked != i)
        {
            // Faded: shrinks to 85% and 18% over 0.3 s.
            float k = MathF.Min(1, since / 0.3f);
            float a = 1 - 0.82f * k;
            r *= 1 - 0.15f * k;
            DrawCircle(c, r, new Color(GoldDeep, GoldDeep.A * a));
            DrawArc(c, r, 0, MathF.Tau, 48, new Color(GoldRim, GoldRim.A * a), 2, true);
            DrawLabel(c, label, size, new Color(GoldInk, a));
            return;
        }
        if (Picked == i)
        {
            // Picked: pops from 0.7 to 1.12 with an overshoot, lit gold with a glow.
            float t = MathF.Min(1, since / 0.5f);
            float e = 1 - MathF.Pow(1 - t, 3) * (1 - 2.2f * t);
            r *= 0.7f + 0.42f * e;
            for (int g = 4; g >= 1; g--) DrawCircle(c, r + g * 6, new Color(1, 0.78f, 0.24f, 0.09f));
            DrawCircle(c, r + 4, new Color(1, 0.9f, 0.55f, 0.35f));
            DrawCircle(c, r, new Color(0.88f, 0.64f, 0.11f));
            DrawCircle(c - new Vector2(0, r * 0.25f), r * 0.6f, new Color(1, 0.95f, 0.72f, 0.55f));
            DrawLabel(c, label, size, new Color(0.23f, 0.13f, 0));
            return;
        }
        // Breathing: a soft ring swelling out every 1.1 s, each button a quarter beat behind the last.
        float ph = (float)((now + i * 0.25) / 1.1 % 1.0);
        float b = 0.5f - 0.5f * MathF.Cos(ph * MathF.Tau);
        DrawCircle(c, r + 6 * b, new Color(1, 0.82f, 0.31f, 0.18f * b));
        DrawCircle(c, r + 2 + 9 * b, new Color(1, 0.78f, 0.24f, 0.1f + 0.15f * b));
        DrawCircle(c, r, GoldDeep);
        DrawCircle(c - new Vector2(0, r * 0.3f), r * 0.62f, Gold);
        DrawArc(c, r, 0, MathF.Tau, 48, GoldRim, 2, true);
        DrawLabel(c, label, size, GoldInk);
    }

    /// <summary>Centred text; a newline stacks the lines.</summary>
    void DrawLabel(Vector2 centre, string text, int size, Color ink)
    {
        var lines = text.Split('\n');
        float lh = size * 1.05f;
        for (int l = 0; l < lines.Length; l++)
        {
            var w = _font.GetStringSize(lines[l], HorizontalAlignment.Left, -1, size).X;
            float y = (l - (lines.Length - 1) / 2f) * lh;
            DrawString(_font, centre + new Vector2(-w / 2, y + size * 0.36f), lines[l], HorizontalAlignment.Left, -1, size, ink);
        }
    }
}
