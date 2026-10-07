using Godot;
using GameNight.Club;
using GameNight.Sim;
using Pos = GameNight.Club.Position;

namespace GameNight.Menus;

/// <summary>
/// The skill moves page inside the squad page: the SPRINT button with its four slides (up, left,
/// right, down), each holding one of the ten moves or nothing. Tap a slot, then a move to put it
/// there (the same move again empties it). In a match, sliding SPRINT on the ball does the move
/// in that slot, if the man on the ball has the skill stars for it.
/// </summary>
public sealed partial class SquadScreen
{
    bool _skills;
    int _slot;

    static readonly string[] SlotNames = { "UP", "LEFT", "RIGHT", "DOWN" };
    static readonly Vector2[] SlotDirs = { Vector2.Up, Vector2.Left, Vector2.Right, Vector2.Down };

    public void OpenSkills()
    {
        Unpick();
        _skills = true;
        _slot = 0;
        _roster.Visible = false;
    }

    void CloseSkills()
    {
        _skills = false;
        _roster.Visible = true;
    }

    SkillMove[] Slots => Club.S.SkillSlots is { Length: 4 } s ? s : Skills.DefaultSlots;

    void PaintSkills()
    {
        float W = Size.X, H = Size.Y;
        NightBackdrop(new[] { Px.Hex(0x0a0820), Px.Hex(0x100d30), Px.Hex(0x161242), Px.Hex(0x1b1652) });
        float cw = Mathf.Min(W - 24, 1180), x0 = (W - cw) / 2;

        BackButton(new Vector2(x0, 6), CloseSkills);
        float hx = x0 + 50;
        Px.Text(this, Px.Big, new Vector2(hx, 34), "SKILL MOVES", 30, Px.Ink, new Color(0, 0, 0, 0.55f), 3);
        Px.Text(this, Px.Small, new Vector2(hx + Px.Width(Px.Big, "SKILL MOVES", 30) + 14, 30), "HOLD SPRINT ON THE BALL AND SLIDE IT", 8, Px.InkDim);

        float top = 48, left = Mathf.Round(Mathf.Clamp(cw * 0.4f, 320, 440));
        var pad = new Rect2(x0, top, left, H - 6 - top);
        Px.Frame(this, pad, new Color(12 / 255f, 10 / 255f, 36 / 255f, 0.92f), Px.Line2, Px.Shadow);
        Pad(pad);
        var list = new Rect2(x0 + left + 10, top, cw - left - 10, H - 6 - top);
        Px.Frame(this, list, new Color(12 / 255f, 10 / 255f, 36 / 255f, 0.92f), Px.Line2, Px.Shadow);
        Moves(list.Grow(-6));
        Px.Scanlines(this, new Rect2(0, 0, W, H));
    }

    /// <summary>The SPRINT button in the middle, a slot on each side of it.</summary>
    void Pad(Rect2 r)
    {
        var slots = Slots;
        float noteH = 56;
        var c = new Vector2(r.GetCenter().X, r.Position.Y + (r.Size.Y - noteH) / 2);
        float br = Mathf.Clamp((r.Size.Y - noteH) * 0.13f, 24, 40);
        float sw = Mathf.Min(122, (r.Size.X - 2 * br - 28) / 2), sh = Mathf.Clamp((r.Size.Y - noteH - 2 * br) / 2 - 14, 40, 60);

        DrawCircle(c + new Vector2(2, 3), br, Px.ShadowSoft);
        DrawCircle(c, br, Px.Glass2);
        DrawArc(c, br, 0, Mathf.Tau, 40, new Color(Px.Ink, 0.55f), 2);
        Px.TextC(this, Px.Big, c.X, c.Y + 6, "SPRINT", 16, Px.Ink);

        for (int i = 0; i < 4; i++)
        {
            var u = SlotDirs[i];
            // The arrow on the button's rim, lit for the slot being set.
            Arrow(c + u * (br - 6), u, 5, i == _slot ? Px.Gold : new Color(Px.Ink, 0.45f));
            var at = c + u * (u.Y != 0 ? br + 8 + sh / 2 : br + 8 + sw / 2);
            var box = new Rect2(at - new Vector2(sw, sh) / 2, new Vector2(sw, sh));
            Slot(box, i, slots[i]);
        }

        var note = Px.Wrap(Px.Small, "Everyone can do the 1-star moves. 2-star moves need 3 skill stars, 3-star moves need 5. A man who can't do a slot's move just keeps the ball. Double tap SPRINT: a 4 or 5-star man's own signature skill.", 8, r.Size.X - 20);
        float ny = r.End.Y - noteH + 8;
        foreach (var line in note)
        {
            Px.Text(this, Px.Small, new Vector2(r.Position.X + 10, ny), line, 8, Px.InkDim);
            ny += 11;
        }
    }

    void Slot(Rect2 box, int i, SkillMove m)
    {
        string key = "slot" + i;
        bool on = i == _slot, held = Held(key);
        var rr = held ? box.Translated(new Vector2(2, 2)) : box;
        Px.Frame(this, rr, on ? new Color(Px.Gold, 0.16f) : Px.Glass2, on ? Px.Gold : Px.Line2, held ? null : Px.ShadowSoft, on ? 3 : 2, 4);
        Px.Text(this, Px.Small, rr.Position + new Vector2(7, 12), SlotNames[i], 7, on ? Px.Gold : Px.InkDim);
        if (m == SkillMove.None)
            Px.TextC(this, Px.Big, rr.GetCenter().X, rr.GetCenter().Y + 9, "EMPTY", 14, new Color(Px.Ink, 0.35f));
        else
        {
            Tier(rr.Position + new Vector2(rr.Size.X - 9, 8), Skills.Tier(m), 4, true);
            Px.TextC(this, Px.Big, rr.GetCenter().X, rr.GetCenter().Y + 10, Px.Fit(Px.Big, Skills.Name(m).ToUpperInvariant(), 15, rr.Size.X - 10), 15, Px.Ink);
        }
        Tap(key, box, () => _slot = i);
    }

    /// <summary>The ten moves: tier, name, what it does, how many of the XI can do it, its slot.</summary>
    void Moves(Rect2 r)
    {
        var slots = Slots;
        var starters = Club.Starters();
        int outfield = 0;
        var stars = new int[11];
        for (int i = 0; i < 11; i++)
        {
            if (starters[i] == null || Club.Slot(i).Pos == Pos.GK) continue;
            outfield++;
            stars[i] = SkillStars.Of(starters[i]);
        }
        float rh = Mathf.Floor(r.Size.Y / Skills.All.Length);
        for (int k = 0; k < Skills.All.Length; k++)
        {
            var m = Skills.All[k];
            var row = new Rect2(r.Position.X, r.Position.Y + k * rh, r.Size.X, rh - 3);
            int slot = System.Array.IndexOf(slots, m);
            bool here = slot == _slot, held = Held("mv" + k);
            var rr = held ? row.Translated(new Vector2(2, 1)) : row;
            Px.Frame(this, rr, here ? new Color(Px.Gold, 0.14f) : new Color(1, 1, 1, slot >= 0 ? 0.07f : 0.03f), here ? Px.Gold : slot >= 0 ? Px.Line2 : Px.Line, null, 2, 0);

            float y = rr.GetCenter().Y;
            Tier(new Vector2(rr.Position.X + 10, y), Skills.Tier(m), 5, false);
            float nx = rr.Position.X + 50;
            Px.Text(this, Px.Big, new Vector2(nx, y + 6), Skills.Name(m).ToUpperInvariant(), 16, Px.Ink);
            float dx = nx + Mathf.Max(118, Px.Width(Px.Big, Skills.Name(m).ToUpperInvariant(), 16) + 10);

            int can = 0;
            for (int i = 0; i < 11; i++)
                if (stars[i] > 0 && Skills.Knows(stars[i], m)) can++;
            string who = $"{can}/{outfield} IN XI";
            float ww = Px.Width(Px.Small, who, 8);
            float right = rr.End.X - 34;
            Px.Text(this, Px.Small, new Vector2(right - ww, y + 3), who, 8, can == 0 ? Px.Loss : can == outfield ? Px.Win : Px.Ink);
            Px.Text(this, Px.Small, new Vector2(dx, y + 3), Px.Fit(Px.Small, Skills.How(m).ToUpperInvariant(), 8, right - ww - 10 - dx), 8, Px.InkDim);
            // Its slot, as the arrow it's on.
            if (slot >= 0) Arrow(new Vector2(rr.End.X - 16, y), SlotDirs[slot], 6, here ? Px.Gold : Px.Ink);

            Tap("mv" + k, row, () => Club.SetSkillSlot(_slot, Slots[_slot] == m ? SkillMove.None : m));
        }
    }

    /// <summary>A move's tier as gold stars: in a row from `p` (or right-aligned to it).</summary>
    void Tier(Vector2 p, int tier, float r, bool fromRight)
    {
        for (int s = 0; s < tier; s++)
        {
            float x = fromRight ? p.X - s * (r * 2 + 2) : p.X + r + s * (r * 2 + 2);
            Art.Star(this, new Vector2(x, p.Y), r, Px.Gold);
        }
    }

    /// <summary>A small filled arrowhead pointing along `u`, its tip at `tip`.</summary>
    void Arrow(Vector2 tip, Vector2 u, float s, Color col)
    {
        var n = new Vector2(-u.Y, u.X);
        DrawColoredPolygon(new[] { tip, tip - u * s * 1.2f + n * s, tip - u * s * 1.2f - n * s }, col);
    }
}
