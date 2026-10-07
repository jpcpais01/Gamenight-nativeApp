using System;
using GameNight.Club;
using GameNight.Menus;
using GameNight.Sim;
using Godot;

namespace GameNight.Net;

/// <summary>
/// Coach mode (online 1v1 where the computer plays and the two friends manage): a COACH key
/// bottom right and, opened, the manager's board over the match, which goes on underneath
/// (nobody pauses the other). Mentality, formation and substitutions: tap a man on the pitch
/// and one on the bench to make a change at the next stoppage, tap a waiting change to call it
/// off. Draws whatever `View` says (the host's engine is the truth) and hands each order to
/// `Ordered`.
/// </summary>
public sealed partial class CoachPanel : PxCanvas
{
    public CoachView View;
    public event Action<CoachOrder> Ordered;
    public Color Shirt = Px.Gold;

    static readonly string[] Moods = { "PARK THE BUS", "DEFENSIVE", "BALANCED", "ATTACKING", "ALL OUT" };

    bool _open;
    Rect2 _key, _card;
    int _off = -1, _on;
    int _mood;
    string _form;
    double _moodAt = -9, _formAt = -9;

    int Mood => T - _moodAt < 2 ? _mood : View?.Mentality ?? 0;
    string Form => T - _formAt < 2 ? _form : View?.Formation ?? "";

    public bool IsOpen => _open;

    public CoachPanel()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Closed, only the key takes taps: the rest of the screen stays the match's.</summary>
    public override bool _HasPoint(Vector2 point) => _open || _key.HasPoint(point);

    public void Toggle()
    {
        _open = !_open;
        _off = -1;
        _on = 0;
    }

    protected override void Background()
    {
        if (_open && !_card.HasPoint(GetLocalMousePosition())) Toggle();
    }

    void Order(CoachOrder o)
    {
        // Shown straight away; the host's board confirms it a moment later.
        if (o.Kind == CoachOrder.Mood) (_mood, _moodAt) = (o.Value, T);
        if (o.Kind == CoachOrder.Shape) (_form, _formAt) = (o.Formation, T);
        Ordered?.Invoke(o);
        _off = -1;
        _on = 0;
    }

    static string MoodName(int v) => Moods[Math.Clamp(v + 2, 0, 4)];

    protected override void Paint()
    {
        var size = Size;
        var v = View;
        _key = new Rect2(size.X - 16 - 150, size.Y - 16 - 44, 150, 44);
        if (!_open)
        {
            string tag = v == null ? "COACH MODE" : $"{Formations.ById(Form).Name} · {MoodName(Mood)}";
            float tw = Px.Width(Px.Big, tag, 18) + 24;
            var tr = new Rect2(_key.Position.X - 12 - tw, size.Y - 16 - 36, tw, 28);
            Px.Frame(this, tr, new Color(0.06f, 0.05f, 0.16f, 0.82f), Px.Cyan, null, 2, 3);
            Px.Text(this, Px.Big, tr.Position + new Vector2(12, 20), tag, 18, Px.Ink);
            GoldButton("coach", _key, "COACH", 24, Toggle);
            return;
        }

        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.03f, 0.02f, 0.1f, 0.45f));
        var cs = new Vector2(Mathf.Min(680, size.X - 24), Mathf.Min(372, size.Y - 16));
        _card = new Rect2((size - cs) / 2, cs);
        Px.Frame(this, _card, Px.Glass, Px.Gold, Px.Shadow, 3, 6);
        Tap("card", _card, null);
        float x = _card.Position.X + 18, y = _card.Position.Y + 30, right = _card.End.X - 18;
        Px.Text(this, Px.Big, new Vector2(x, y), "COACH", 30, Px.Gold);
        GhostButton("close", new Rect2(right - 84, _card.Position.Y + 10, 84, 30), "DONE", 18, Toggle);
        if (v == null)
        {
            Px.Text(this, Px.Small, new Vector2(x, y + 30), "WAITING FOR THE MATCH...", 9, Px.InkDim);
            return;
        }
        string left = v.Left == 1 ? "1 CHANGE LEFT" : $"{v.Left} CHANGES LEFT";
        Px.TextR(this, Px.Big, right - 96, y - 2, left, 18, v.Left > 0 ? Px.Cyan : Px.Loss);

        // Mentality.
        y += 14;
        Px.Text(this, Px.Small, new Vector2(x, y + 15), "MENTALITY", 8, Px.InkDim);
        float cx = x + 86;
        for (int m = -2; m <= 2; m++)
        {
            int mood = m;
            cx += Chip("mood" + m, new Vector2(cx, y), Moods[m + 2], Mood == m, () => Order(new CoachOrder { Kind = CoachOrder.Mood, Value = mood }), 14, 22) + 6;
        }
        // Formation.
        y += 28;
        Px.Text(this, Px.Small, new Vector2(x, y + 15), "FORMATION", 8, Px.InkDim);
        cx = x + 86;
        foreach (var f in Formations.All)
        {
            var form = f;
            cx += Chip("form" + f.Id, new Vector2(cx, y), f.Name, Form == f.Id, () => Order(Shape(form)), 14, 22) + 6;
        }

        // Substitutions: the eleven, the bench.
        y += 34;
        float colW = (right - x - 16) / 2, rowH = Mathf.Min(19, (_card.End.Y - 14 - y - 14) / 11);
        Px.Text(this, Px.Small, new Vector2(x, y + 8), "ON THE PITCH", 8, Px.Gold);
        Px.Text(this, Px.Small, new Vector2(x + colW + 16, y + 8), "BENCH", 8, Px.Gold);
        Px.TextR(this, Px.Small, right, y + 8, "TAP ONE OF EACH · MADE AT THE NEXT STOPPAGE", 7, Px.InkDim);
        y += 14;
        for (int i = 0; i < v.Pitch.Count; i++)
        {
            var p = v.Pitch[i];
            var r = new Rect2(x, y + i * rowH, colW, rowH - 2);
            bool picked = p.Id == _off;
            DrawRect(r, picked ? new Color(Px.Gold, 0.3f) : new Color(1, 1, 1, p.Coming > 0 ? 0.1f : 0.05f));
            DrawRect(new Rect2(r.Position, new Vector2(3, r.Size.Y)), Shirt);
            float k = p.Off ? 0.35f : 1;
            float by = r.Position.Y + r.Size.Y / 2;
            Px.Text(this, Px.Small, new Vector2(x + 8, by + 3), p.Role, 7, new Color(Px.InkDim, k));
            string name = Shirt2(p.Name, p.Number, p.Index);
            if (p.Coming > 0)
            {
                string on = "IN " + Shirt2(p.ComingName, p.Coming, p.Index);
                float ow = Px.Width(Px.Big, on, 15);
                Px.Text(this, Px.Big, new Vector2(x + 38, by + 5), Px.Fit(Px.Big, name, 15, colW - 50 - ow - 12), 15, new Color(Px.Ink, 0.45f));
                Px.TextR(this, Px.Big, r.End.X - 6, by + 5, on, 15, Px.Cyan);
            }
            else
            {
                Px.Text(this, Px.Big, new Vector2(x + 38, by + 5), Px.Fit(Px.Big, name, 15, colW - 110), 15, new Color(Px.Ink, k));
                if (p.Off) Px.TextR(this, Px.Small, r.End.X - 6, by + 3, "SENT OFF", 7, Px.Loss);
                else
                {
                    // Legs left, a booking, came off the bench.
                    float bx = r.End.X - 50;
                    DrawRect(new Rect2(bx, by - 2, 44, 4), new Color(1, 1, 1, 0.12f));
                    var stc = p.Stamina > 0.6f ? Px.Win : p.Stamina > 0.35f ? Px.Gold : Px.Loss;
                    DrawRect(new Rect2(bx, by - 2, Mathf.Max(3, 44 * p.Stamina), 4), stc);
                    if (p.Card > 0) DrawRect(new Rect2(bx - 11, r.Position.Y + 3, 6, r.Size.Y - 6), Px.Hex(0xf2c21b));
                    if (p.SubbedOn) Px.TextR(this, Px.Small, bx - 14, by + 3, "SUB", 7, Px.InkDim);
                }
            }
            if (p.Off) continue;
            var man = p;
            Tap("off" + p.Id, r, () =>
            {
                if (man.Coming > 0) Order(new CoachOrder { Kind = CoachOrder.Cancel, Off = man.Id });
                else PickOff(man.Id);
            });
        }
        float bx0 = x + colW + 16;
        for (int i = 0; i < v.Bench.Count; i++)
        {
            var b = v.Bench[i];
            var r = new Rect2(bx0, y + i * rowH, colW, rowH - 2);
            bool picked = b.Number == _on;
            DrawRect(r, picked ? new Color(Px.Gold, 0.3f) : new Color(1, 1, 1, 0.05f));
            float k = b.Warming ? 0.4f : 1;
            float by = r.Position.Y + r.Size.Y / 2;
            Px.Text(this, Px.Small, new Vector2(bx0 + 8, by + 3), b.Pos, 7, new Color(Px.InkDim, k));
            Px.Text(this, Px.Big, new Vector2(bx0 + 38, by + 5), Px.Fit(Px.Big, Shirt2(b.Name, b.Number, 0), 15, colW - 120), 15, new Color(Px.Ink, k));
            if (b.Warming) Px.TextR(this, Px.Small, r.End.X - 6, by + 3, "WARMING UP", 7, Px.Cyan);
            else
            {
                var sub = b;
                Tap("on" + b.Number, r, () => PickOn(sub.Number));
            }
        }
        if (v.Bench.Count == 0) Px.Text(this, Px.Small, new Vector2(bx0, y + 12), "NOBODY LEFT ON THE BENCH.", 8, Px.InkDim);
    }

    // A second tap on the same man lets go of him; one of each makes the change.
    void PickOff(int id)
    {
        _off = _off == id ? -1 : id;
        Pair();
    }

    void PickOn(int number)
    {
        _on = _on == number ? 0 : number;
        Pair();
    }

    void Pair()
    {
        if (_off >= 0 && _on > 0 && View?.Left > 0) Order(new CoachOrder { Kind = CoachOrder.Sub, Off = _off, On = _on });
    }

    static string Shirt2(string name, int number, int index) =>
        (number > 0 ? number + "  " : "") + (string.IsNullOrWhiteSpace(name) ? "#" + (index + 1) : name.ToUpperInvariant());

    /// <summary>A formation as an order: its eleven slots in shirt order.</summary>
    public static CoachOrder Shape(Formation f)
    {
        var slots = new double[22];
        for (int i = 0; i < 11 && i < f.Slots.Length; i++)
        {
            slots[i] = f.Slots[i].X;
            slots[11 + i] = f.Slots[i].Z;
        }
        return new CoachOrder { Kind = CoachOrder.Shape, Formation = f.Id, Slots = slots };
    }
}
