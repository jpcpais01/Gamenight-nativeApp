using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GameNight.Club;
using GameNight.Sim;
using Pos = GameNight.Club.Position;

namespace GameNight.Menus;

/// <summary>
/// Squad (the PWA's squad.ts): formation, the tactics board with your eleven, and the whole
/// collection. Tap a man on the board to pick him, then tap another to swap them or a player
/// in the list to bring him in; tap him again for his card. Hold and drag him to move his spot.
/// </summary>
public sealed partial class SquadScreen : PxCanvas
{
    public enum Filter { All, GK, DEF, MID, FWD }

    readonly Menus _ui;
    readonly RosterList _roster;
    public int Selected = -1;
    public Filter Show = Filter.All;
    Rect2 _pitch;
    // Dragging a token to a new spot on the board.
    int _drag = -1;
    Vector2 _dragAt;
    bool _dragMoved;

    ClubState Club => _ui.Club;

    public SquadScreen(Menus ui)
    {
        _ui = ui;
        _roster = new RosterList(this, ui) { ClipContents = true };
        AddChild(_roster);
    }

    public void Opened()
    {
        Selected = -1;
        _roster.ResetScroll();
    }

    // Board coordinates: x (team frame, -1..0.5) runs left to right, z top to bottom.
    const float X0 = -1, X1 = 0.5f;
    Vector2 ToBoard(double x, double z) => _pitch.Position + new Vector2((0.07f + ((float)x - X0) / (X1 - X0) * 0.86f) * _pitch.Size.X, (0.06f + ((float)z + 1) / 2 * 0.88f) * _pitch.Size.Y);
    (double x, double z) FromBoard(Vector2 p) => (X0 + ((p.X - _pitch.Position.X) / _pitch.Size.X - 0.07f) / 0.86f * (X1 - X0), ((p.Y - _pitch.Position.Y) / _pitch.Size.Y - 0.06f) / 0.88f * 2 - 1);

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        NightBackdrop();
        // Top bar: back, title, formations, team rating.
        BackButton(new Vector2(14, 12), () => _ui.Go(_ui.Home));
        Title(new Vector2(66, 44), "SQUAD");
        float x = 66 + Px.Width(Px.Big, "SQUAD", 38) + 18;
        foreach (var f in Formations.All)
        {
            var id = f.Id;
            x += Chip("f" + id, new Vector2(x, 16), f.Name, Club.S.Lineup.Formation == id, () =>
            {
                Selected = -1;
                Club.SetFormation(id);
            }) + 8;
        }
        var ovr = new Rect2(W - 16 - 84, 12, 84, 36);
        Px.Frame(this, ovr, Px.Glass2, Px.Hex(0xb37400), Px.ShadowSoft);
        Px.Text(this, Px.Big, ovr.Position + new Vector2(10, 29), Club.TeamRating().ToString(), 30, Px.Hex(0xffe066), Colors.Black);
        Px.Text(this, Px.Small, ovr.Position + new Vector2(50, 25), "OVR", 8, Px.InkDim);

        // The board.
        float top = 62, bottom = H - 50;
        float bw = Mathf.Min(W * 0.56f, (bottom - top) * 1.55f);
        _pitch = new Rect2(14, top, bw, bottom - top);
        Board();

        // Tools under the board.
        float ty = H - 42;
        float tx = 14;
        GhostButton("auto", new Rect2(tx, ty, 150, 32), "AUTO-PICK BEST XI", 18, () =>
        {
            Selected = -1;
            Club.AutoPick();
            _ui.Toast($"Best XI picked · {Club.TeamRating()} OVR");
        });
        tx += 162;
        if (Club.HasCustom)
        {
            GhostButton("resetpos", new Rect2(tx, ty, 120, 32), "RESET SPOTS", 18, () => Club.ResetPositions());
            tx += 132;
        }
        var starters = Club.Starters();
        if (Selected >= 0 && starters[Selected] != null)
        {
            bool cap = Selected == Club.CaptainIndex();
            if (cap) Px.Text(this, Px.Big, new Vector2(tx + 6, ty + 24), "CAPTAIN", 18, Px.Gold);
            else GhostButton("captain", new Rect2(tx, ty, 120, 32), "MAKE CAPTAIN", 18, () =>
            {
                var p = starters[Selected];
                Selected = -1;
                Club.SetCaptain(p.Id);
                _ui.Toast($"{p.LastName} is your captain");
            });
            tx += 132;
        }
        string tip = Selected >= 0 ? $"PICK A PLAYER FOR {Club.Slot(Selected).Pos} >" : "TAP TO PICK · HOLD AND DRAG TO MOVE";
        if (tx + Px.Width(Px.Small, tip, 8) < _pitch.End.X) Px.Text(this, Px.Small, new Vector2(tx + 4, ty + 21), tip, 8, Selected >= 0 ? Px.Cyan : Px.InkDim);

        // The roster on the right: its header here, the list in its own clipped child.
        float rx = _pitch.End.X + 18, rw = W - 14 - rx;
        RosterHead(new Vector2(rx, top), rw);
        _roster.Position = new Vector2(rx, top + 40);
        _roster.Size = new Vector2(rw, H - 10 - top - 40);
        Px.Scanlines(this, new Rect2(0, 0, W, H));
    }

    void Board()
    {
        var p = _pitch;
        Px.Frame(this, p, Colors.Transparent, Px.Line2, Px.Shadow);
        var inner = p.Grow(-3);
        // Mown stripes, then the lines.
        int n = 10;
        for (int i = 0; i < n; i++)
            DrawRect(new Rect2(inner.Position.X + inner.Size.X * i / n, inner.Position.Y, inner.Size.X / n + 1, inner.Size.Y), i % 2 == 0 ? Px.Hex(0x2f7a3c) : Px.Hex(0x37874a));
        var line = new Color(1, 1, 1, 0.55f);
        var g = inner.Grow(-10);
        Px.Ring(this, g, line, 2);
        // Our goal on the left; the halfway line where x = 0.
        float hx = ToBoard(0, 0).X;
        DrawRect(new Rect2(hx - 1, g.Position.Y, 2, g.Size.Y), line);
        var cc = new Vector2(hx, g.GetCenter().Y);
        DrawPolyline(Px.Ellipse(cc, g.Size.Y * 0.16f, g.Size.Y * 0.16f, 24).Append(cc + new Vector2(g.Size.Y * 0.16f, 0)).ToArray(), line, 2);
        var box = new Rect2(g.Position.X, g.GetCenter().Y - g.Size.Y * 0.3f, g.Size.X * 0.16f, g.Size.Y * 0.6f);
        Px.Ring(this, new Rect2(box.Position - new Vector2(2, 0), box.Size + new Vector2(2, 0)), line, 2);
        var six = new Rect2(g.Position.X, g.GetCenter().Y - g.Size.Y * 0.13f, g.Size.X * 0.06f, g.Size.Y * 0.26f);
        Px.Ring(this, new Rect2(six.Position - new Vector2(2, 0), six.Size + new Vector2(2, 0)), line, 2);
        Px.TextR(this, Px.Small, g.End.X - 6, g.Position.Y + 16, "ATTACK >", 8, new Color(1, 1, 1, 0.6f));
        Tap("pitch", p, () => Selected = -1);

        var kit = Club.Info().Kit;
        var starters = Club.Starters();
        int cap = Club.CaptainIndex();
        float th = Mathf.Clamp(p.Size.Y * 0.15f, 36, 56), tw = th * 0.82f;
        for (int i = 0; i < 11; i++)
        {
            var s = Club.Slot(i);
            var at = i == _drag && _dragMoved ? _dragAt : ToBoard(s.X, s.Z);
            var r = new Rect2(at - new Vector2(tw / 2, th / 2 + 6), new Vector2(tw, th));
            int idx = i;
            Art.Token(this, r, starters[i], s.Pos, kit, i == Selected, i == cap && starters[i] != null);
            Tap("t" + i, r, () => TapToken(idx));
        }
    }

    void TapToken(int i)
    {
        if (_dragMoved) return;
        var starters = Club.Starters();
        if (Selected == i)
        {
            Selected = -1;
            if (starters[i] != null) _ui.OpenPlayer(starters[i]);
        }
        else if (Selected >= 0)
        {
            Club.SwapSlots(Selected, i);
            Selected = -1;
        }
        else Selected = i;
    }

    /// <summary>A player picked in the list.</summary>
    public void PickCard(Card c)
    {
        if (Selected >= 0)
        {
            Club.Assign(Selected, c.Id);
            Selected = -1;
        }
        else _ui.OpenPlayer(c);
    }

    void RosterHead(Vector2 p, float w)
    {
        if (Selected >= 0)
        {
            Px.Text(this, Px.Big, p + new Vector2(0, 22), $"CHOOSE {Club.Slot(Selected).Pos}", 24, Px.Cyan);
            Chip("cancel", new Vector2(p.X + w - 80, p.Y), "Cancel", false, () => Selected = -1);
            return;
        }
        float x = p.X;
        foreach (Filter f in Enum.GetValues(typeof(Filter)))
        {
            var ff = f;
            string label = f == Filter.All ? $"All {Club.S.Cards.Count}" : f.ToString();
            x += Chip("flt" + f, new Vector2(x, p.Y), label, Show == f, () =>
            {
                Show = ff;
                _roster.ResetScroll();
            }) + 6;
        }
    }

    // ---------------------------------------------------------------- dragging tokens

    public override void _GuiInput(InputEvent e)
    {
        // A held token (long press) follows the finger and lands where it's let go.
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _drag = TokenAt(mb.Position);
                _dragAt = mb.Position;
                _dragMoved = false;
                _pressT = Time.GetTicksMsec();
            }
            else if (_drag >= 0)
            {
                int i = _drag;
                bool moved = _dragMoved;
                _drag = -1;
                if (moved)
                {
                    int over = TokenAt(mb.Position, i);
                    if (over >= 0) Club.SwapSlots(i, over);
                    else if (Club.Slot(i).Pos == Pos.GK) _ui.Toast("The keeper stays in goal");
                    else if (_pitch.HasPoint(mb.Position))
                    {
                        var (x, z) = FromBoard(mb.Position + new Vector2(0, 6));
                        Club.MoveSlot(i, x, z);
                    }
                    Selected = -1;
                    _dragMoved = false;
                    AcceptEvent();
                    return;
                }
            }
        }
        else if (e is InputEventMouseMotion mm && _drag >= 0)
        {
            if (!_dragMoved && (mm.Position - _dragAt).Length() > 8 && Time.GetTicksMsec() - _pressT > 120) _dragMoved = true;
            if (_dragMoved)
            {
                _dragAt = mm.Position;
                AcceptEvent();
                return;
            }
        }
        base._GuiInput(e);
    }

    ulong _pressT;

    int TokenAt(Vector2 p, int except = -1)
    {
        float th = Mathf.Clamp(_pitch.Size.Y * 0.15f, 36, 56), tw = th * 0.82f;
        for (int i = 10; i >= 0; i--)
        {
            if (i == except) continue;
            var s = Club.Slot(i);
            var r = new Rect2(ToBoard(s.X, s.Z) - new Vector2(tw / 2, th / 2 + 6), new Vector2(tw, th));
            if (r.HasPoint(p)) return i;
        }
        return -1;
    }
}

/// <summary>The scrolling list of every player at the club.</summary>
public sealed partial class RosterList : PxCanvas
{
    readonly SquadScreen _squad;
    readonly Menus _ui;
    const float RowH = 50;

    public RosterList(SquadScreen squad, Menus ui)
    {
        _squad = squad;
        _ui = ui;
        ScrollAxis = 1;
    }

    protected override void Paint()
    {
        var club = _ui.Club;
        var starters = club.Starters();
        int sel = _squad.Selected;
        Pos? slotPos = sel >= 0 ? club.Slot(sel).Pos : null;
        IEnumerable<Card> list = club.S.Cards;
        if (slotPos is Pos sp)
        {
            // Choosing for a slot: best fits first, the man already there left out.
            var cur = starters[sel]?.Id;
            list = list.Where(c => c.Id != cur).OrderByDescending(c => Cards.RatingIn(c, sp));
        }
        else
        {
            if (_squad.Show != SquadScreen.Filter.All) list = list.Where(c => Cards.RoleOf(c.Position).ToString() == _squad.Show.ToString());
            list = list.OrderByDescending(c => club.IsStarter(c.Id)).ThenByDescending(c => c.Overall);
        }
        var cards = list.ToList();
        Content = cards.Count * (RowH + 6) + 6;
        float y = -Scroll + 3;
        for (int i = 0; i < cards.Count; i++, y += RowH + 6)
        {
            if (y + RowH < 0 || y > Size.Y) continue;
            var c = cards[i];
            var r = new Rect2(3, y, Size.X - 10, RowH);
            Row(r, c, slotPos, club.IsStarter(c.Id));
            Tap("c" + c.Id, r, () => _squad.PickCard(c));
        }
        if (cards.Count == 0) Px.TextC(this, Px.Small, Size.X / 2, 40, "NO PLAYERS HERE YET. OPEN PACKS IN THE STORE!", 8, Px.InkDim);
        // Scroll bar.
        if (ScrollMax > 0)
        {
            float h = Mathf.Max(24, Size.Y * Size.Y / Content);
            float t = (Size.Y - h) * Scroll / ScrollMax;
            DrawRect(new Rect2(Size.X - 4, t, 3, h), new Color(Px.Cyan, 0.5f));
        }
    }

    void Row(Rect2 r, Card c, Pos? slotPos, bool starter)
    {
        bool held = Held("c" + c.Id);
        var rr = held ? new Rect2(r.Position + new Vector2(2, 2), r.Size) : r;
        var col = Art.RarityColor(c.Rarity);
        Px.Frame(this, rr, starter ? new Color(28 / 255f, 24 / 255f, 80 / 255f, 0.9f) : new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.82f), starter ? new Color(Px.Cyan, 0.45f) : Px.Line, null, 2);
        // Rating block in the rarity colour.
        var ob = new Rect2(rr.Position + new Vector2(2, 2), new Vector2(46, rr.Size.Y - 4));
        Px.Bands(this, ob, new[] { col.Lightened(0.3f), col, col.Darkened(0.25f) }, new[] { 0, 0.3f, 0.75f });
        int rating = slotPos is Pos sp ? Cards.RatingIn(c, sp) : c.Overall;
        var ink = Px.Hex(0x2b1708);
        Px.TextC(this, Px.Big, ob.GetCenter().X, ob.Position.Y + 26, rating.ToString(), 28, ink);
        Px.TextC(this, Px.Small, ob.GetCenter().X, ob.End.Y - 5, c.Position.ToString(), 8, ink);
        float x = ob.End.X + 10;
        Px.Flag(this, new Rect2(x, rr.Position.Y + 9, 14, 10), Cards.Nations[c.Nation]);
        string name = c.Name;
        Px.Text(this, Px.Big, new Vector2(x + 20, rr.Position.Y + 21), Px.Fit(Px.Big, name, 22, rr.Size.X - 170), 22, Px.Ink);
        float nx = x + 26 + Mathf.Min(Px.Width(Px.Big, name, 22), rr.Size.X - 170);
        if (starter) Px.Text(this, Px.Small, new Vector2(nx, rr.Position.Y + 19), "XI", 8, Px.Cyan);
        if (slotPos is Pos p2)
        {
            double fit = Cards.FitFactor(c.Position, p2);
            if (fit < 1) Px.TextR(this, Px.Small, rr.End.X - 8, rr.Position.Y + 19, fit >= 0.85 ? "CLOSE FIT" : "OUT OF POSITION", 8, fit >= 0.85 ? Px.Gold : Px.Loss);
        }
        var fs = Cards.FaceStats(c);
        float sx = x;
        foreach (var (k, v) in fs)
        {
            var sc = v >= 80 ? Px.Win : v < 55 ? Px.Loss : Px.InkDim;
            Px.Text(this, Px.Small, new Vector2(sx, rr.End.Y - 9), k, 8, Px.InkDim);
            Px.Text(this, Px.Big, new Vector2(sx + 25, rr.End.Y - 7), v.ToString(), 17, sc);
            sx += 52;
            if (sx > rr.End.X - 52) break;
        }
    }
}
