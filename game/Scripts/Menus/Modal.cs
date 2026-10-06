using System;
using System.Collections.Generic;
using Godot;
using GameNight.Club;
using GameNight.Sim;

namespace GameNight.Menus;

/// <summary>A box over the dimmed screen. Tapping outside it, ✕ or Android back closes it.</summary>
public abstract partial class Modal : PxCanvas
{
    protected readonly Menus Ui;
    protected bool Closable = true;
    protected Rect2 Box;

    protected Modal(Menus ui)
    {
        Ui = ui;
    }

    protected abstract Vector2 BoxSize { get; }
    protected abstract void PaintBox(Rect2 box);

    public virtual void Dismiss()
    {
        if (Closable) Ui.Close(this);
    }

    protected override void Background()
    {
        if (!Box.HasPoint(GetLocalMousePosition())) Dismiss();
    }

    protected override void Paint()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.03f, 0.02f, 0.1f, 0.72f));
        var s = BoxSize;
        s = new Vector2(Mathf.Min(s.X, Size.X - 24), Mathf.Min(s.Y, Size.Y - 20));
        // Pops in, easing out.
        float k = Mathf.Min(1, (float)T / 0.18f);
        k = 1 - (1 - k) * (1 - k) * (1 - k);
        var size = s * (0.9f + 0.1f * k);
        Box = new Rect2((Size - size) / 2, size);
        Px.Frame(this, Box, Colors.Transparent, new Color(190 / 255f, 200 / 255f, 1f, 0.4f), new Color(0, 0, 0, 0.6f));
        Px.Bands(this, Box.Grow(-3), new[] { Px.Night2, Px.Hex(0x171442), Px.Hex(0x12103a) }, new[] { 0, 0.4f, 0.75f });
        Tap("box", Box, null);
        PaintBox(Box);
        if (Closable)
        {
            var x = new Rect2(Box.End.X - 40, Box.Position.Y + 8, 32, 30);
            bool held = Held("close");
            Px.Frame(this, held ? new Rect2(x.Position + Vector2.One * 2, x.Size) : x, Px.Glass2, Px.Line2, null);
            Px.TextC(this, Px.Big, x.GetCenter().X, x.GetCenter().Y + 8, "X", 22, Px.Ink);
            Tap("close", x, Dismiss);
        }
    }

    protected void Kicker(Vector2 p, string s) => Px.Text(this, Px.Small, p, s.ToUpperInvariant(), 9, Px.Cyan);
    protected void Heading(Vector2 p, string s) => Px.Text(this, Px.Big, p, s, 32, Px.Ink, new Color(0, 0, 0, 0.55f), 3);
}

/// <summary>A list of options in a modal (ground, drill).</summary>
public sealed partial class OptionsModal : Modal
{
    readonly string _kicker, _title, _hint;
    readonly List<(string name, string about, string extra)> _items;
    readonly Action<int> _pick;
    readonly Func<int, bool> _on;
    readonly string _confirm;
    readonly Action _confirmTap;

    public OptionsModal(Menus ui, string kicker, string title, List<(string, string, string)> items, Action<int> pick,
        Func<int, bool> on = null, string confirm = null, Action confirmTap = null, string hint = null) : base(ui)
    {
        _kicker = kicker;
        _title = title;
        _items = items;
        _pick = pick;
        _on = on;
        _confirm = confirm;
        _confirmTap = confirmTap;
        _hint = hint;
    }

    protected override Vector2 BoxSize => new(560, 92 + _items.Count * 50 + (_confirm != null ? 58 : 0) + (_hint != null ? 18 : 0));

    protected override void PaintBox(Rect2 b)
    {
        Kicker(b.Position + new Vector2(20, 26), _kicker);
        Heading(b.Position + new Vector2(20, 58), _title);
        float y = b.Position.Y + 74;
        float rowH = Mathf.Min(46, (b.Size.Y - 92 - (_confirm != null ? 58 : 0) - (_hint != null ? 18 : 0)) / _items.Count - 4);
        for (int i = 0; i < _items.Count; i++)
        {
            int idx = i;
            var r = new Rect2(b.Position.X + 20, y, b.Size.X - 40, rowH);
            bool on = _on?.Invoke(i) ?? false;
            bool held = Held("opt" + i);
            var rr = held ? new Rect2(r.Position + Vector2.One * 2, r.Size) : r;
            Px.Frame(this, rr, on ? Px.Gold : new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.85f), on ? Px.Hex(0xb37400) : Px.Line2, held ? null : Px.ShadowSoft, 3, 4);
            var ink = on ? Px.Dark : Px.Ink;
            Px.Text(this, Px.Big, rr.Position + new Vector2(12, rowH * 0.5f + 7), _items[i].name, 22, ink);
            float nx = rr.Position.X + 24 + Mathf.Max(150, Px.Width(Px.Big, _items[i].name, 22));
            Px.Text(this, Px.Small, new Vector2(nx, rr.Position.Y + rowH * 0.5f + 4), Px.Fit(Px.Small, _items[i].about, 8, rr.End.X - nx - 110), 8, on ? Px.Dark : Px.InkDim);
            if (_items[i].extra != null) Px.TextR(this, Px.Small, rr.End.X - 10, rr.Position.Y + rowH * 0.5f + 4, _items[i].extra, 8, on ? Px.Dark : Px.Cyan);
            Tap("opt" + i, r, () => _pick(idx));
            y += rowH + 6;
        }
        if (_hint != null) Px.TextC(this, Px.Small, b.GetCenter().X, b.End.Y - (_confirm != null ? 66 : 14), _hint, 8, Px.InkDim);
        if (_confirm != null) GoldButton("confirm", new Rect2(b.GetCenter().X - 110, b.End.Y - 56, 220, 42), _confirm, 26, _confirmTap);
    }
}

/// <summary>A yes / no question.</summary>
public sealed partial class ConfirmModal : Modal
{
    readonly string _title, _body, _yes, _no;
    readonly Action _onYes, _onNo;

    public ConfirmModal(Menus ui, string title, string body, string yes, Action onYes, string no = "Cancel", Action onNo = null) : base(ui)
    {
        _title = title;
        _body = body;
        _yes = yes;
        _onYes = onYes;
        _no = no;
        _onNo = onNo;
    }

    protected override Vector2 BoxSize => new(440, 200);

    public override void Dismiss()
    {
        base.Dismiss();
        _onNo?.Invoke();
    }

    protected override void PaintBox(Rect2 b)
    {
        Heading(b.Position + new Vector2(20, 50), _title);
        float y = b.Position.Y + 80;
        foreach (var l in Px.Wrap(Px.Small, _body, 9, b.Size.X - 40))
        {
            Px.Text(this, Px.Small, new Vector2(b.Position.X + 20, y), l, 9, Px.InkDim);
            y += 16;
        }
        float w = (b.Size.X - 60) / 2;
        GhostButton("no", new Rect2(b.Position.X + 20, b.End.Y - 60, w, 42), _no, 22, Dismiss);
        GoldButton("yes", new Rect2(b.Position.X + 40 + w, b.End.Y - 60, w, 42), _yes, 22, () =>
        {
            Ui.Close(this);
            _onYes?.Invoke();
        });
    }
}

/// <summary>What's new: the patch notes, newest first.</summary>
public sealed partial class NotesModal : Modal
{
    public NotesModal(Menus ui) : base(ui)
    {
        ScrollAxis = 1;
    }

    protected override Vector2 BoxSize => new(620, 600);

    protected override void PaintBox(Rect2 b)
    {
        Heading(b.Position + new Vector2(20, 48), "Patch notes");
        // The notes scroll inside the box, under the heading.
        float top = b.Position.Y + 66, bottom = b.End.Y - 14, view = bottom - top;
        float y = top + 14 - Scroll, start = y;
        foreach (var (v, note) in PatchNotes.All)
        {
            if (y + 4 > top && y < bottom) Px.Text(this, Px.Big, new Vector2(b.Position.X + 20, y + 4), "v" + v, 20, Px.Cyan);
            foreach (var l in Px.Wrap(Px.Small, note, 9, b.Size.X - 130))
            {
                if (y - 10 > top && y < bottom) Px.Text(this, Px.Small, new Vector2(b.Position.X + 96, y), l, 9, Px.Ink);
                y += 16;
            }
            y += 12;
        }
        float height = y - start + 14;
        // ScrollMax is measured against the whole screen: give it the box's view instead.
        Content = height + Size.Y - view;
        DrawRect(new Rect2(b.Position.X + 12, top, b.Size.X - 24, 1), Px.Line);
        if (height > view)
        {
            float h = Mathf.Max(24, view * view / height);
            float t = (view - h) * Mathf.Clamp(Scroll / Mathf.Max(1, height - view), 0, 1);
            DrawRect(new Rect2(b.End.X - 10, top + t, 3, h), new Color(Px.Cyan, 0.6f));
        }
    }
}

/// <summary>Full time: the result, and the coins counting up.</summary>
public sealed partial class ResultModal : Modal
{
    readonly int _gf, _ga, _coins;
    readonly char _result;
    bool _rang;
    readonly TeamInfo _home, _away;

    public ResultModal(Menus ui, int gf, int ga, TeamInfo home, TeamInfo away, int coins, char result) : base(ui)
    {
        _gf = gf;
        _ga = ga;
        _home = home;
        _away = away;
        _coins = coins;
        _result = result;
    }

    protected override Vector2 BoxSize => new(520, 330);

    protected override void PaintBox(Rect2 b)
    {
        var c = b.GetCenter().X;
        Px.TextC(this, Px.Small, c, b.Position.Y + 30, "FULL TIME", 9, Px.Cyan);
        string label = _result == 'W' ? "VICTORY" : _result == 'D' ? "DRAW" : "DEFEAT";
        var col = _result == 'W' ? Px.Gold : _result == 'D' ? Px.Ink : Px.Loss;
        Px.TextC(this, Px.Big, c, b.Position.Y + 84, label, 60, col, new Color(0, 0, 0, 0.5f), 4);
        float y = b.Position.Y + 110;
        CrestArt.Draw(this, new Rect2(c - 170, y - 3, 54, 67), Ui.Club.S.Crest);
        Px.TextC(this, Px.Small, c - 143, y + 80, Px.Fit(Px.Small, _home.Name.ToUpperInvariant(), 8, 150), 8, Px.Ink);
        Art.Crest(this, new Rect2(c + 116, y, 54, 63), _away.Kit.Shirt, _away.Kit.Shirt2, _away.Short);
        Px.TextC(this, Px.Small, c + 143, y + 80, Px.Fit(Px.Small, _away.Name.ToUpperInvariant(), 8, 150), 8, Px.Ink);
        Px.TextC(this, Px.Big, c, y + 52, $"{_gf} - {_ga}", 56, Px.Ink, new Color(0, 0, 0, 0.5f), 3);
        // Coins count up after half a second, easing out.
        float k = Mathf.Clamp(((float)T - 0.5f) / 1.2f, 0, 1);
        if (k >= 1 && !_rang)
        {
            _rang = true;
            Audio.GameAudio.Instance?.Coins();
        }
        int shown = (int)Mathf.Round(_coins * (1 - Mathf.Pow(1 - k, 3)));
        string s = "+" + Px.Thousands(shown);
        float w = Px.Width(Px.Big, s, 30) + 30;
        Px.Coin(this, new Vector2(c - w / 2, y + 112), 20);
        Px.Text(this, Px.Big, new Vector2(c - w / 2 + 30, y + 132), s, 30, Px.Hex(0xffe066), Colors.Black);
        GoldButton("go", new Rect2(c - 100, b.End.Y - 58, 200, 44), "Continue", 26, Dismiss);
    }
}
