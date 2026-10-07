using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GameNight.Club;
using GameNight.Sim;
using Pos = GameNight.Club.Position;

namespace GameNight.Menus;

/// <summary>
/// Squad: the starting eleven on a big floodlit pitch is the page; the bench of seven sits in the
/// dugout under it and the rest of the squad runs down the side. Tap a man (on the pitch or the
/// bench) to pick him, then tap another to swap them, or a player in the list to bring him in; tap
/// him again for his card. Hold and drag: on the pitch to move his spot, onto another man to swap.
/// </summary>
public sealed partial class SquadScreen : PxCanvas
{
    public enum Filter { All, GK, DEF, MID, FWD }

    readonly Menus _ui;
    readonly RosterList _roster;
    /// <summary>The picked man: a slot in the XI, or a bench seat.</summary>
    public int Selected = -1, SelSeat = -1;
    public Filter Show = Filter.All;
    Rect2 _pitch;
    readonly Rect2[] _seats = new Rect2[7];
    // Dragging a man: slots 0..10, bench seats 100..106.
    int _drag = -1;
    Vector2 _dragAt;
    bool _dragMoved;
    ulong _pressT;

    ClubState Club => _ui.Club;

    public SquadScreen(Menus ui)
    {
        _ui = ui;
        _roster = new RosterList(this, ui) { ClipContents = true };
        AddChild(_roster);
    }

    public void Opened()
    {
        Unpick();
        _roster.ResetScroll();
    }

    void Unpick()
    {
        Selected = -1;
        SelSeat = -1;
    }

    // Board coordinates: x (team frame, -1..0.5) runs left to right, z top to bottom.
    const float X0 = -1, X1 = 0.5f;
    Vector2 ToBoard(double x, double z) => _pitch.Position + new Vector2((0.07f + ((float)x - X0) / (X1 - X0) * 0.86f) * _pitch.Size.X, (0.05f + ((float)z + 1) / 2 * 0.88f) * _pitch.Size.Y);
    (double x, double z) FromBoard(Vector2 p) => (X0 + ((p.X - _pitch.Position.X) / _pitch.Size.X - 0.07f) / 0.86f * (X1 - X0), ((p.Y - _pitch.Position.Y) / _pitch.Size.Y - 0.05f) / 0.88f * 2 - 1);

    float TokenH => Mathf.Clamp(_pitch.Size.Y * 0.155f, 36, 64);

    Rect2 TokenRect(Vector2 at)
    {
        float th = TokenH, tw = th * 0.82f;
        return new Rect2(at - new Vector2(tw / 2, th / 2 + 6), new Vector2(tw, th));
    }

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        NightBackdrop(new[] { Px.Hex(0x0a0820), Px.Hex(0x100d30), Px.Hex(0x161242), Px.Hex(0x1b1652) });
        float cw = Mathf.Min(W - 24, 1180), x0 = (W - cw) / 2;
        float side = Mathf.Round(Mathf.Clamp(cw * 0.27f, 220, 300));
        float mainW = cw - side - 10;

        // Header: back, title, the shapes, the team's rating.
        BackButton(new Vector2(x0, 6), () => _ui.Go(_ui.Home));
        float hx = x0 + 50;
        Px.Text(this, Px.Big, new Vector2(hx, 34), "SQUAD", 30, Px.Ink, new Color(0, 0, 0, 0.55f), 3);
        float fx = hx + Px.Width(Px.Big, "SQUAD", 30) + 14;
        foreach (var f in Formations.All)
        {
            var id = f.Id;
            fx += Chip("f" + id, new Vector2(fx, 12), f.Name, Club.S.Lineup.Formation == id, () =>
            {
                Unpick();
                Club.SetFormation(id);
            }, 16, 24) + 5;
        }

        // The pitch: the page.
        float top = 44, benchH = Mathf.Clamp(H * 0.17f, 62, 92);
        var panel = new Rect2(x0, top, mainW, H - 6 - benchH - 6 - top);
        _pitch = panel.Grow(-4);
        Board(panel);
        // The dugout under it.
        Dugout(new Rect2(x0, panel.End.Y + 6, mainW, benchH));

        // The rest of the squad down the side.
        float rx = x0 + mainW + 10;
        var room = new Rect2(rx, 6, side, H - 12);
        Px.Frame(this, room, new Color(12 / 255f, 10 / 255f, 36 / 255f, 0.92f), Px.Line2, Px.Shadow);
        var badge = TeamBadge(new Rect2(rx + 7, 12, 82, 36));
        Lines(new Rect2(badge.End.X + 5, 12, room.End.X - 7 - badge.End.X - 5, 36), Club.Starters());
        RosterHead(new Vector2(rx + 7, 56), side - 14);
        float listTop = 106, foot = 44 + (Selected >= 0 && Club.Starters()[Selected] != null || Club.HasCustom ? 34 : 0);
        DrawRect(new Rect2(rx + 3, listTop - 2, side - 6, 1), Px.Line);
        _roster.Position = new Vector2(rx + 3, listTop);
        _roster.Size = new Vector2(side - 6, room.End.Y - listTop - foot);
        Tools(new Rect2(rx + 7, room.End.Y - foot + 4, side - 14, foot - 10));
        Px.Scanlines(this, new Rect2(0, 0, W, H));
    }

    Rect2 TeamBadge(Rect2 badge)
    {
        var main = Px.Hex(Club.S.Kit.Main);
        Px.Frame(this, badge, main.Darkened(0.55f), Px.Hex(Club.S.Kit.Secondary).Lerp(Colors.White, 0.3f), Px.ShadowSoft, 2);
        DrawRect(new Rect2(badge.Position + new Vector2(2, 2), new Vector2(badge.Size.X - 4, 3)), main);
        Px.Text(this, Px.Big, badge.Position + new Vector2(9, 30), Club.TeamRating().ToString(), 28, Px.Hex(0xffe066), Colors.Black);
        Px.Text(this, Px.Small, badge.Position + new Vector2(48, 18), "TEAM", 7, Px.InkDim);
        Px.Text(this, Px.Small, badge.Position + new Vector2(48, 28), "OVR", 8, Px.Ink);
        return badge;
    }

    /// <summary>Line strengths along the top of the pitch.</summary>
    void Lines(Rect2 g, Card[] starters)
    {
        var sums = new int[4];
        var counts = new int[4];
        for (int i = 0; i < 11; i++)
        {
            if (starters[i] == null) continue;
            var pos = Club.Slot(i).Pos;
            int role = (int)Cards.RoleOf(pos);
            sums[role] += Cards.RatingIn(starters[i], pos);
            counts[role]++;
        }
        string[] names = { "GK", "DEF", "MID", "ATT" };
        float bw = (g.Size.X - 9) / 4;
        for (int role = 0; role < 4; role++)
        {
            int v = counts[role] > 0 ? sums[role] / counts[role] : 0;
            var r = new Rect2(g.Position.X + role * (bw + 3), g.Position.Y, bw, g.Size.Y);
            Px.Frame(this, r, new Color(8 / 255f, 7 / 255f, 26 / 255f, 0.9f), Px.Line, null, 2);
            Px.TextC(this, Px.Big, r.GetCenter().X, r.Position.Y + 20, v > 0 ? v.ToString() : "-", 18, v >= 80 ? Px.Win : v >= 65 ? Px.Ink : Px.Loss);
            Px.TextC(this, Px.Small, r.GetCenter().X, r.End.Y - 4, names[role], 7, Px.InkDim);
        }
    }

    void Board(Rect2 panel)
    {
        Px.Frame(this, panel, Px.Hex(0x0b1a10), Px.Line2, Px.Shadow);
        var inner = _pitch;
        // Mown stripes under the floodlights, then the lines.
        int n = 14;
        for (int i = 0; i < n; i++)
            DrawRect(new Rect2(inner.Position.X + inner.Size.X * i / n, inner.Position.Y, inner.Size.X / n + 1, inner.Size.Y), i % 2 == 0 ? Px.Hex(0x2a7238) : Px.Hex(0x317e42));
        foreach (float sx in new[] { 0.25f, 0.75f })
            Fx.Spot(this, new Vector2(inner.Position.X + inner.Size.X * sx, inner.Position.Y - 30), new Vector2(inner.Position.X + inner.Size.X * sx, inner.End.Y - inner.Size.Y * 0.25f), inner.Size.X * 0.06f, inner.Size.X * 0.34f, Px.Hex(0xfff0bf), 0.05f);
        for (int i = 0; i < 4; i++)
        {
            float k = 5 + i * 6;
            var c = new Color(0.02f, 0.02f, 0.08f, 0.08f);
            DrawRect(new Rect2(inner.Position.X, inner.Position.Y, inner.Size.X, k), c);
            DrawRect(new Rect2(inner.Position.X, inner.End.Y - k, inner.Size.X, k), c);
            DrawRect(new Rect2(inner.Position.X, inner.Position.Y, k, inner.Size.Y), c);
            DrawRect(new Rect2(inner.End.X - k, inner.Position.Y, k, inner.Size.Y), c);
        }
        var line = new Color(1, 1, 1, 0.5f);
        var g = inner.Grow(-10);
        Px.Ring(this, g, line, 2);
        float hx = ToBoard(0, 0).X;
        DrawRect(new Rect2(hx - 1, g.Position.Y, 2, g.Size.Y), line);
        var cc = new Vector2(hx, g.GetCenter().Y);
        float cr = g.Size.Y * 0.17f;
        DrawPolyline(Px.Ellipse(cc, cr, cr, 28).Append(cc + new Vector2(cr, 0)).ToArray(), line, 2);
        DrawRect(new Rect2(cc - new Vector2(2, 2), new Vector2(4, 4)), line);
        var box = new Rect2(g.Position.X, g.GetCenter().Y - g.Size.Y * 0.3f, g.Size.X * 0.13f, g.Size.Y * 0.6f);
        Px.Ring(this, new Rect2(box.Position - new Vector2(2, 0), box.Size + new Vector2(2, 0)), line, 2);
        var six = new Rect2(g.Position.X, g.GetCenter().Y - g.Size.Y * 0.13f, g.Size.X * 0.05f, g.Size.Y * 0.26f);
        Px.Ring(this, new Rect2(six.Position - new Vector2(2, 0), six.Size + new Vector2(2, 0)), line, 2);
        var net = new Rect2(g.Position.X - 8, g.GetCenter().Y - g.Size.Y * 0.07f, 8, g.Size.Y * 0.14f);
        DrawRect(net, new Color(1, 1, 1, 0.12f));
        for (float y = net.Position.Y + 3; y < net.End.Y; y += 3) DrawRect(new Rect2(net.Position.X, y, net.Size.X, 1), new Color(1, 1, 1, 0.2f));
        // Attack chevrons, top right.
        float ax = g.End.X - 30, ay = g.Position.Y + 14;
        Px.TextR(this, Px.Small, ax - 4, ay + 3, "ATTACK", 7, new Color(1, 1, 1, 0.55f));
        for (int i = 0; i < 3; i++)
        {
            float a = (int)(T * 3) % 3 == i ? 0.75f : 0.25f;
            var c0 = new Vector2(ax + i * 7, ay);
            DrawColoredPolygon(new[] { c0 + new Vector2(0, -4), c0 + new Vector2(4, 0), c0 + new Vector2(0, 4), c0 + new Vector2(-2, 4), c0 + new Vector2(2, 0), c0 + new Vector2(-2, -4) }, new Color(1, 1, 1, a));
        }
        var starters = Club.Starters();
        string tip = Selected >= 0 || SelSeat >= 0 ? "NOW TAP WHO COMES IN · OR TAP HIM AGAIN FOR HIS CARD" : "TAP TO PICK · HOLD AND DRAG TO MOVE OR SWAP";
        Px.TextC(this, Px.Small, g.GetCenter().X, g.End.Y - 5, tip, 7, Selected >= 0 || SelSeat >= 0 ? Px.Cyan : new Color(1, 1, 1, 0.5f));
        Tap("pitch", _pitch, Unpick);

        var kit = Club.Info().Kit;
        int cap = Club.CaptainIndex();
        for (int i = 0; i < 11; i++)
        {
            var s = Club.Slot(i);
            var at = i == _drag && _dragMoved ? _dragAt : ToBoard(s.X, s.Z);
            var r = TokenRect(at);
            int idx = i;
            DrawColoredPolygon(Px.Ellipse(new Vector2(r.GetCenter().X + 2, r.End.Y + 1), r.Size.X * 0.55f, 3, 12), new Color(0, 0, 0, 0.3f));
            Art.Token(this, r, starters[i], s.Pos, kit, i == Selected, i == cap && starters[i] != null);
            Tap("t" + i, r, () => TapToken(idx));
        }
    }

    /// <summary>The bench: seven seats in a dugout under the pitch.</summary>
    void Dugout(Rect2 d)
    {
        Px.Frame(this, d, new Color(10 / 255f, 9 / 255f, 30 / 255f, 0.95f), Px.Line2, Px.Shadow);
        // The perspex roof and the seat back.
        DrawRect(new Rect2(d.Position.X + 3, d.Position.Y + 3, d.Size.X - 6, 5), new Color(Px.Cyan, 0.18f));
        DrawRect(new Rect2(d.Position.X + 3, d.Position.Y + 8, d.Size.X - 6, 1), new Color(Px.Cyan, 0.35f));
        DrawRect(new Rect2(d.Position.X + 3, d.End.Y - 14, d.Size.X - 6, 11), Px.Hex(Club.S.Kit.Main).Darkened(0.45f));
        var label = new Rect2(d.Position.X + 6, d.Position.Y + 12, 54, d.Size.Y - 18);
        Px.TextC(this, Px.Big, label.GetCenter().X, label.GetCenter().Y + 2, "BENCH", 18, Px.Ink);
        Px.TextC(this, Px.Small, label.GetCenter().X, label.GetCenter().Y + 14, "7 SUBS", 7, Px.InkDim);
        var bench = Club.BenchSeven();
        var kit = Club.Info().Kit;
        float th = Mathf.Min(d.Size.Y - 28, 64), tw = th * 0.82f;
        float x = label.End.X + 6, gap = Mathf.Min(52, (d.End.X - 8 - x - tw * 7) / 6);
        float total = tw * 7 + gap * 6;
        x += Mathf.Max(0, (d.End.X - 8 - x - total) / 2);
        for (int i = 0; i < 7; i++)
        {
            var at = 100 + i == _drag && _dragMoved ? _dragAt : new Vector2(x + i * (tw + gap) + tw / 2, d.Position.Y + 12 + th / 2 + 6);
            var r = new Rect2(at - new Vector2(tw / 2, th / 2 + 6), new Vector2(tw, th));
            _seats[i] = 100 + i == _drag && _dragMoved ? new Rect2(new Vector2(x + i * (tw + gap), d.Position.Y + 12), new Vector2(tw, th)) : r;
            int seat = i;
            var c = bench[i];
            if (c == null)
            {
                Px.Frame(this, r, new Color(1, 1, 1, 0.04f), new Color(1, 1, 1, 0.18f), null, 2, 0);
                Px.TextC(this, Px.Small, r.GetCenter().X, r.GetCenter().Y + 3, "EMPTY", 7, Px.InkDim);
            }
            else Art.Token(this, r, c, c.Position, kit, SelSeat == i, false);
            Tap("seat" + i, _seats[i], () => TapSeat(seat));
        }
    }

    void Tools(Rect2 r)
    {
        float y = r.Position.Y;
        var starters = Club.Starters();
        if (Selected >= 0 && starters[Selected] != null)
        {
            bool cap = Selected == Club.CaptainIndex();
            if (cap) Px.TextC(this, Px.Big, r.GetCenter().X, y + 20, "HE'S YOUR CAPTAIN", 16, Px.Gold);
            else GhostButton("captain", new Rect2(r.Position.X, y, r.Size.X, 28), "MAKE CAPTAIN", 16, () =>
            {
                var p = starters[Selected];
                Unpick();
                Club.SetCaptain(p.Id);
                _ui.Toast($"{p.LastName} is your captain");
            });
            y += 34;
        }
        else if (Club.HasCustom)
        {
            GhostButton("resetpos", new Rect2(r.Position.X, y, r.Size.X, 28), "RESET SPOTS", 16, () => Club.ResetPositions());
            y += 34;
        }
        GoldButton("auto", new Rect2(r.Position.X, y, r.Size.X, 30), "AUTO-PICK BEST XI", 18, () =>
        {
            Unpick();
            Club.AutoPick();
            _ui.Toast($"Best XI picked · {Club.TeamRating()} OVR");
        });
    }

    void TapToken(int i)
    {
        if (_dragMoved) return;
        var starters = Club.Starters();
        if (Selected == i)
        {
            Unpick();
            if (starters[i] != null) _ui.OpenPlayer(starters[i]);
        }
        else if (Selected >= 0)
        {
            Club.SwapSlots(Selected, i);
            Unpick();
        }
        else if (SelSeat >= 0)
        {
            Club.SwapSlotBench(i, SelSeat);
            Unpick();
        }
        else Selected = i;
    }

    void TapSeat(int seat)
    {
        if (_dragMoved) return;
        var bench = Club.BenchSeven();
        if (SelSeat == seat)
        {
            Unpick();
            if (bench[seat] != null) _ui.OpenPlayer(bench[seat]);
        }
        else if (Selected >= 0)
        {
            Club.SwapSlotBench(Selected, seat);
            Unpick();
        }
        else if (SelSeat >= 0)
        {
            Club.SwapSeats(SelSeat, seat);
            Unpick();
        }
        else SelSeat = seat;
    }

    /// <summary>A player picked in the list.</summary>
    public void PickCard(Card c)
    {
        if (Selected >= 0)
        {
            int seat = Club.BenchSeat(c.Id);
            if (seat >= 0) Club.SwapSlotBench(Selected, seat);
            else Club.Assign(Selected, c.Id);
            Unpick();
        }
        else if (SelSeat >= 0)
        {
            Club.SetBench(SelSeat, c.Id);
            Unpick();
        }
        else _ui.OpenPlayer(c);
    }

    void RosterHead(Vector2 p, float w)
    {
        if (Selected >= 0 || SelSeat >= 0)
        {
            string what = Selected >= 0 ? $"IN AT {Club.Slot(Selected).Pos}" : "ONTO THE BENCH";
            Px.Text(this, Px.Big, p + new Vector2(0, 18), "WHO COMES", 20, Px.Cyan);
            Px.Text(this, Px.Small, p + new Vector2(0, 36), what, 8, Px.Cyan);
            Chip("cancel", new Vector2(p.X + w - 66, p.Y), "Cancel", false, Unpick, 14, 22);
            return;
        }
        Px.Text(this, Px.Big, p + new Vector2(0, 16), "THE SQUAD", 18, Px.Ink);
        Px.TextR(this, Px.Small, p.X + w, p.Y + 12, $"{Club.S.Cards.Count} PLAYERS", 7, Px.InkDim);
        float x = p.X;
        foreach (Filter f in Enum.GetValues(typeof(Filter)))
        {
            var ff = f;
            x += Chip("flt" + f, new Vector2(x, p.Y + 22), f == Filter.All ? "ALL" : f.ToString(), Show == f, () =>
            {
                Show = ff;
                _roster.ResetScroll();
            }, 14, 22) + 4;
        }
    }

    // ---------------------------------------------------------------- dragging

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _drag = ManAt(mb.Position);
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
                    Drop(i, mb.Position);
                    Unpick();
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

    /// <summary>A dragged man let go: onto another man swaps them; on open grass moves his spot.</summary>
    void Drop(int from, Vector2 p)
    {
        int over = ManAt(p, from);
        bool fromSeat = from >= 100;
        if (over >= 0)
        {
            bool toSeat = over >= 100;
            if (!fromSeat && !toSeat) Club.SwapSlots(from, over);
            else if (fromSeat && toSeat) Club.SwapSeats(from - 100, over - 100);
            else if (fromSeat) Club.SwapSlotBench(over, from - 100);
            else Club.SwapSlotBench(from, over - 100);
        }
        else if (!fromSeat && _pitch.HasPoint(p))
        {
            if (Club.Slot(from).Pos == Pos.GK) _ui.Toast("The keeper stays in goal");
            else
            {
                var (x, z) = FromBoard(p + new Vector2(0, 6));
                Club.MoveSlot(from, x, z);
            }
        }
    }

    int ManAt(Vector2 p, int except = -1)
    {
        for (int i = 6; i >= 0; i--)
            if (100 + i != except && _seats[i].HasPoint(p)) return 100 + i;
        for (int i = 10; i >= 0; i--)
        {
            if (i == except) continue;
            var s = Club.Slot(i);
            if (TokenRect(ToBoard(s.X, s.Z)).HasPoint(p)) return i;
        }
        return -1;
    }
}

/// <summary>The rest of the squad, down the side: everyone not in the XI, the bench marked.</summary>
public sealed partial class RosterList : PxCanvas
{
    readonly SquadScreen _squad;
    readonly Menus _ui;
    const float RowH = 44;

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
        club.BenchSeven();
        int sel = _squad.Selected;
        Pos? slotPos = sel >= 0 ? club.Slot(sel).Pos : null;
        IEnumerable<Card> list = club.S.Cards;
        if (slotPos is Pos sp)
        {
            // Choosing for a slot: best fits first, the man already there left out.
            var cur = starters[sel]?.Id;
            list = list.Where(c => c.Id != cur).OrderByDescending(c => Cards.RatingIn(c, sp));
        }
        else if (_squad.SelSeat >= 0)
        {
            var cur = club.S.Lineup.Bench[_squad.SelSeat];
            list = list.Where(c => c.Id != cur && !club.IsStarter(c.Id)).OrderByDescending(c => c.Overall);
        }
        else
        {
            list = list.Where(c => !club.IsStarter(c.Id));
            if (_squad.Show != SquadScreen.Filter.All) list = list.Where(c => Cards.RoleOf(c.Position).ToString() == _squad.Show.ToString());
            list = list.OrderBy(c => club.BenchSeat(c.Id) >= 0).ThenByDescending(c => c.Overall);
        }
        var cards = list.ToList();
        Content = cards.Count * (RowH + 4) + 6;
        float y = -Scroll + 3;
        for (int i = 0; i < cards.Count; i++, y += RowH + 4)
        {
            if (y + RowH < 0 || y > Size.Y) continue;
            var c = cards[i];
            var r = new Rect2(4, y, Size.X - 12, RowH);
            Row(r, c, slotPos, club.IsStarter(c.Id), club.BenchSeat(c.Id) >= 0);
            Tap("c" + c.Id, r, () => _squad.PickCard(c));
        }
        if (cards.Count == 0) Px.TextC(this, Px.Small, Size.X / 2, 40, "EVERYONE'S IN THE XI. OPEN PACKS!", 7, Px.InkDim);
        if (ScrollMax > 0)
        {
            float h = Mathf.Max(24, Size.Y * Size.Y / Content);
            float t = (Size.Y - h) * Scroll / ScrollMax;
            DrawRect(new Rect2(Size.X - 5, t, 3, h), new Color(Px.Cyan, 0.5f));
        }
    }

    void Row(Rect2 r, Card c, Pos? slotPos, bool starter, bool bench)
    {
        bool held = Held("c" + c.Id);
        var rr = held ? new Rect2(r.Position + new Vector2(2, 2), r.Size) : r;
        var col = Art.ColorOf(c);
        Px.Frame(this, rr, bench || starter ? new Color(28 / 255f, 24 / 255f, 80 / 255f, 0.9f) : new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.82f), starter ? new Color(Px.Cyan, 0.45f) : bench ? new Color(Px.Gold, 0.35f) : Px.Line, null, 2);
        var ob = new Rect2(rr.Position + new Vector2(2, 2), new Vector2(38, rr.Size.Y - 4));
        Px.Bands(this, ob, new[] { col.Lightened(0.3f), col, col.Darkened(0.25f) }, new[] { 0, 0.3f, 0.75f });
        int rating = slotPos is Pos sp ? Cards.RatingIn(c, sp) : c.Overall;
        var ink = Px.Hex(0x2b1708);
        Px.TextC(this, Px.Big, ob.GetCenter().X, ob.Position.Y + 23, rating.ToString(), 24, ink);
        Px.TextC(this, Px.Small, ob.GetCenter().X, ob.End.Y - 4, c.Position.ToString(), 7, ink);
        var face = new Rect2(ob.End.X + 3, rr.Position.Y + 3, rr.Size.Y - 6, rr.Size.Y - 6);
        DrawRect(face, new Color(0, 0, 0, 0.35f));
        Art.Avatar(this, face.Grow(-1), c, _ui.Club.Info().Kit);
        DrawRect(new Rect2(face.Position.X, face.End.Y - 2, face.Size.X, 2), col);
        float x = face.End.X + 6, right = rr.End.X - 6;
        Px.Text(this, Px.Big, new Vector2(x, rr.Position.Y + 19), Px.Fit(Px.Big, c.LastName, 18, right - x), 18, Px.Ink);
        Px.Flag(this, new Rect2(x, rr.End.Y - 14, 12, 8), Cards.Nations[c.Nation]);
        float nx = x + 18;
        string tag = starter ? "XI" : bench ? "SUB" : null;
        if (tag != null)
        {
            Px.Text(this, Px.Small, new Vector2(nx, rr.End.Y - 6), tag, 7, starter ? Px.Cyan : Px.Gold);
            nx += Px.Width(Px.Small, tag, 7) + 8;
        }
        if (slotPos is Pos p2 && Cards.FitFactor(c.Position, p2) is double fit && fit < 1)
            Px.Text(this, Px.Small, new Vector2(nx, rr.End.Y - 6), fit >= 0.85 ? "CLOSE FIT" : "OUT OF POSITION", 7, fit >= 0.85 ? Px.Gold : Px.Loss);
        else
            foreach (var ps in Playstyles.Of(c))
            {
                if (nx + 14 > right) break;
                Art.Playstyle(this, new Vector2(nx + 6, rr.End.Y - 9), 6, ps);
                nx += 15;
            }
    }
}
