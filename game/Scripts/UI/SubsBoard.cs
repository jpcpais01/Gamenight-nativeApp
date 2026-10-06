using System;
using System.Collections.Generic;
using GameNight.Sim;
using Godot;

namespace GameNight.UI;

/// <summary>
/// The pause menu's substitutions card: your eleven on the left (legs left, bookings, who's
/// coming on for whom), the bench on the right. Tap a man on the pitch and one on the bench
/// (either order) to make the change; it happens at the next stoppage. Tap a waiting change to
/// call it off. Reads the match only while it's paused.
/// </summary>
public sealed partial class SubsBoard : Control
{
    public event Action Done;
    public Match Match;
    public int Team;

    const float W = 600, RowH = 21, Top = 66, ColW = 288, Gap = 24;
    Player _off;
    SetupPlayer _on;
    readonly Button _done;

    public SubsBoard()
    {
        CustomMinimumSize = new Vector2(W, Top + 11 * RowH + 40);
        _done = new Button { Text = "DONE", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(96, 30) };
        _done.AddThemeFontOverride("font", Style.Font(true, 1.6f));
        _done.AddThemeFontSizeOverride("font_size", 14);
        var box = new StyleBoxFlat { BgColor = Style.Ink, AntiAliasing = true };
        box.SetCornerRadiusAll(4);
        foreach (var st in new[] { "normal", "hover", "focus", "pressed" }) _done.AddThemeStyleboxOverride(st, box);
        foreach (var st in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) _done.AddThemeColorOverride(st, Style.PanelSolid);
        _done.Position = new Vector2(W - 96, 0);
        _done.Pressed += () => Done?.Invoke();
        AddChild(_done);
    }

    /// <summary>Fresh each time it's opened: nothing picked.</summary>
    public void Reset()
    {
        _off = null;
        _on = null;
        QueueRedraw();
    }

    List<Player> Eleven()
    {
        var l = new List<Player>();
        foreach (var p in Match.All) if (p.Team == Team) l.Add(p);
        l.Sort((a, b) => a.Index.CompareTo(b.Index));
        return l;
    }

    static string RoleName(Role r) => r switch { Role.GK => "GK", Role.DEF => "DEF", Role.MID => "MID", _ => "FWD" };

    static string Shirt(string name, int number, int index) =>
        (number > 0 ? number + "  " : "") + (string.IsNullOrWhiteSpace(name) ? "#" + (index + 1) : name.ToUpperInvariant());

    public override void _Draw()
    {
        if (Match == null) return;
        var bold = Style.Font(true, 1.2f);
        var semi = Style.Font(false, 0.5f);
        lock (Match.SubGate)
        {
            int left = Match.SubsLeft(Team);
            Style.Text(this, Style.Font(true, 1.2f), "SUBSTITUTIONS", new Rect2(0, 0, 0, 30), 28, Style.Ink, false);
            string count = left == 1 ? "1 CHANGE LEFT" : $"{left} CHANGES LEFT";
            Style.Text(this, bold, count, new Rect2(W - 110 - Style.Width(bold, count, 13), 0, 0, 30), 13, left > 0 ? Style.Accent : Style.Red, false);
            string hint = left > 0 || Match.SubQueue.Count > 0
                ? "Tap a player on the pitch and one on the bench. The change is made at the next stoppage."
                : "All five changes made.";
            Style.Text(this, semi, hint, new Rect2(0, 32, 0, 14), 11, Style.InkDim, false);

            Head(0, "ON THE PITCH");
            Head(ColW + Gap, "BENCH");
            var shirt = Style.Hex(Match.Teams[Team].Info.Kit.Shirt);

            var xi = Eleven();
            for (int i = 0; i < xi.Count; i++)
            {
                var p = xi[i];
                var r = new Rect2(0, Top + i * RowH, ColW, RowH - 2);
                bool gone = Match.Red[p.Id] || !Match.Teams[Team].Players.Contains(p);
                var coming = Match.QueuedFor(p);
                bool picked = p == _off;
                Style.Corners(this, r, picked ? new Color(Style.Accent, 0.28f) : new Color(Style.Ink, coming != null ? 0.1f : 0.05f), 4, 4, 4, 4);
                Style.Corners(this, new Rect2(r.Position, new Vector2(3, r.Size.Y)), shirt, 2, 0, 0, 2);
                float k = gone ? 0.35f : 1;
                Style.Text(this, semi, RoleName(p.Role), new Rect2(8, r.Position.Y, 0, r.Size.Y), 10, new Color(Style.InkDim, k), false);
                string name = Shirt(p.Name, p.Number, p.Index);
                if (coming != null)
                {
                    string on = "IN  " + Shirt(coming.Name, coming.Number, p.Index);
                    float ow = Style.Width(bold, on, 12);
                    Style.Text(this, bold, Fit(semi, name, 12, ColW - 54 - ow - 14), new Rect2(36, r.Position.Y, 0, r.Size.Y), 12, new Color(Style.Ink, 0.45f), false);
                    Style.Text(this, bold, on, new Rect2(ColW - 8 - ow, r.Position.Y, 0, r.Size.Y), 12, Style.Accent, false);
                    continue;
                }
                Style.Text(this, bold, Fit(bold, name, 12, ColW - 110), new Rect2(36, r.Position.Y, 0, r.Size.Y), 12, new Color(Style.Ink, k), false);
                if (gone)
                {
                    Style.Text(this, bold, "SENT OFF", new Rect2(ColW - 62, r.Position.Y, 0, r.Size.Y), 10, Style.Red, false);
                    continue;
                }
                // Legs left, and a booking.
                float bx = ColW - 52, by = r.Position.Y + r.Size.Y / 2 - 2;
                Style.Corners(this, new Rect2(bx, by, 44, 4), new Color(Style.Ink, 0.12f), 2, 2, 2, 2);
                float st = (float)Math.Clamp(p.Stamina, 0, 1);
                var stc = st > 0.6f ? new Color(0.45f, 0.85f, 0.5f) : st > 0.35f ? Style.Accent : Style.Red;
                Style.Corners(this, new Rect2(bx, by, Math.Max(3, 44 * st), 4), stc, 2, 2, 2, 2);
                if (Match.Cards[p.Id] > 0) Style.Corners(this, new Rect2(bx - 12, r.Position.Y + 4, 7, r.Size.Y - 8), Style.Hex(0xf2c21b), 1, 1, 1, 1);
                if (Match.SubbedOn[p.Id]) Style.Text(this, semi, "SUB", new Rect2(bx - 34, r.Position.Y, 0, r.Size.Y), 9, Style.InkDim, false);
            }

            var bench = Match.Bench[Team];
            float x0 = ColW + Gap;
            for (int i = 0; i < bench.Count; i++)
            {
                var b = bench[i];
                var r = new Rect2(x0, Top + i * RowH, ColW, RowH - 2);
                bool queued = Match.SubQueue.Exists(q => q.on == b);
                bool picked = b == _on;
                Style.Corners(this, r, picked ? new Color(Style.Accent, 0.28f) : new Color(Style.Ink, 0.05f), 4, 4, 4, 4);
                float k = queued ? 0.4f : 1;
                Style.Text(this, semi, b.Pos, new Rect2(x0 + 8, r.Position.Y, 0, r.Size.Y), 10, new Color(Style.InkDim, k), false);
                Style.Text(this, bold, Fit(bold, Shirt(b.Name, b.Number, 0), 12, ColW - 90), new Rect2(x0 + 40, r.Position.Y, 0, r.Size.Y), 12, new Color(Style.Ink, k), false);
                if (queued) Style.Text(this, semi, "WARMING UP", new Rect2(x0 + ColW - 64, r.Position.Y, 0, r.Size.Y), 10, Style.Accent, false);
            }
            if (bench.Count == 0)
                Style.Text(this, semi, "Nobody left on the bench.", new Rect2(x0, Top, 0, RowH), 11, Style.InkDim, false);
        }
    }

    void Head(float x, string s) =>
        Style.Text(this, Style.Font(true, 2f), s, new Rect2(x, Top - 16, 0, 12), 10, new Color(Style.Accent, 0.85f), false);

    static string Fit(Font f, string s, int size, float w)
    {
        if (Style.Width(f, s, size) <= w) return s;
        while (s.Length > 1 && Style.Width(f, s + "…", size) > w) s = s[..^1];
        return s + "…";
    }

    public override void _GuiInput(InputEvent e)
    {
        if (Match == null || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
        var at = mb.Position;
        int row = (int)MathF.Floor((at.Y - Top) / RowH);
        if (at.Y < Top || row < 0 || row > 10) return;
        lock (Match.SubGate)
        {
            if (at.X < ColW)
            {
                var xi = Eleven();
                if (row >= xi.Count) return;
                var p = xi[row];
                if (Match.Red[p.Id] || !Match.Teams[Team].Players.Contains(p)) return;
                if (Match.QueuedFor(p) != null)
                {
                    Match.CancelSub(p);
                    _off = null;
                }
                else _off = _off == p ? null : p;
            }
            else if (at.X > ColW + Gap)
            {
                var bench = Match.Bench[Team];
                if (row >= bench.Count) return;
                var b = bench[row];
                if (Match.SubQueue.Exists(q => q.on == b)) return;
                _on = _on == b ? null : b;
            }
            if (_off != null && _on != null)
            {
                Match.Substitute(_off, _on);
                _off = null;
                _on = null;
            }
        }
        AcceptEvent();
        QueueRedraw();
    }
}
