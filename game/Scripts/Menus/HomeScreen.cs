using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GameNight.Club;
using GameNight.Sim;

namespace GameNight.Menus;

/// <summary>
/// The home screen (the PWA's home.ts): your club and record across the top, the big Kick Off
/// tile with today's opponent, Squad and Store tiles, patch notes and the version at the
/// bottom. The demo match plays behind it.
/// </summary>
public sealed partial class HomeScreen : PxCanvas
{
    readonly Menus _ui;
    ClubState Club => _ui.Club;

    public HomeScreen(Menus ui)
    {
        _ui = ui;
    }

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        // The match plays behind: darken it toward the left where the tiles are.
        DrawRect(new Rect2(0, 0, W, H), new Color(14 / 255f, 10 / 255f, 40 / 255f, 0.55f));
        DrawRect(new Rect2(0, 0, W * 0.6f, H), new Color(14 / 255f, 10 / 255f, 40 / 255f, 0.3f));
        DrawRect(new Rect2(0, H * 0.6f, W, H * 0.4f), new Color(40 / 255f, 10 / 255f, 60 / 255f, 0.25f));
        Px.Scanlines(this, new Rect2(0, 0, W, H));

        TopBar(W);
        float top = 72, bottom = H - 36;
        float heroW = Mathf.Round((W - 32 - 20) * 0.58f);
        Hero(new Rect2(16, top, heroW, bottom - top));
        float rx = 16 + heroW + 20, rw = W - 16 - rx;
        float th = (bottom - top - 20) / 2;
        SquadTile(new Rect2(rx, top, rw, th));
        StoreTile(new Rect2(rx, top + th + 20, rw, th));
        Footer(W, H);
    }

    void TopBar(float W)
    {
        var info = Club.Info();
        // Club button: crest, "your club", name.
        float nameW = Px.Width(Px.Big, info.Name, 30);
        var club = new Rect2(10, 8, 58 + Mathf.Max(nameW, 100) + 10, 52);
        bool held = Held("club");
        var o = held ? new Vector2(2, 2) : Vector2.Zero;
        Art.Crest(this, new Rect2(new Vector2(16, 12) + o, new Vector2(36, 42)), Club.S.Kit.Main, Club.S.Kit.Secondary, info.Short);
        Px.Text(this, Px.Small, new Vector2(60, 24) + o, "YOUR CLUB · EDIT", 8, Px.Cyan);
        Px.Text(this, Px.Big, new Vector2(60, 50) + o, info.Name, 30, Px.Ink, new Color(0, 0, 0, 0.5f), 3);
        Tap("club", club, () => _ui.Go(_ui.ClubStudio));

        // Record: won, drawn, lost.
        var r = Club.S.Record;
        float x = club.End.X + 14;
        foreach (var (n, l, c) in new[] { (r.Won, "W", Px.Win), (r.Drawn, "D", Px.Ink), (r.Lost, "L", Px.Loss) })
        {
            string ns = n.ToString();
            float w = Px.Width(Px.Big, ns, 22) + 26;
            Px.Frame(this, new Rect2(x, 20, w, 30), new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.8f), new Color(190 / 255f, 200 / 255f, 1, 0.18f), Px.ShadowSoft, 3, 4);
            Px.Text(this, Px.Big, new Vector2(x + 8, 42), ns, 22, c);
            Px.Text(this, Px.Small, new Vector2(x + w - 15, 40), l, 8, Px.InkDim);
            x += w + 10;
        }
        Coins(W - 16, 18, Club.S.Coins);
    }

    void Hero(Rect2 r)
    {
        Px.Frame(this, r, Colors.Transparent, Px.Hex(0xff8a7a), new Color(40 / 255f, 0, 20 / 255f, 0.6f));
        var inner = r.Grow(-3);
        Px.Bands(this, inner, new[] { Px.Hex(0xef4b4e), Px.Hex(0xd23345), Px.Hex(0xa81f3c), Px.Hex(0x6e1234), Px.Hex(0x3e0a28) }, new[] { 0, 0.22f, 0.46f, 0.7f, 0.88f });
        for (float ly = inner.Position.Y + 6; ly < inner.End.Y; ly += 9) DrawRect(new Rect2(inner.Position.X, ly, inner.Size.X, 3), new Color(0, 0, 0, 0.07f));
        // The sun: stepped rings, drifting in steps.
        float step = Mathf.Floor((float)(T % 12) / 2) / 6;
        float sr = inner.Size.Y * 0.3f;
        var sun = new Vector2(inner.End.X - sr - 6 - inner.Size.X * 0.05f * step, inner.Position.Y + sr + 6 + inner.Size.Y * 0.05f * step);
        foreach (var (k, a) in new[] { (1f, 0.14f), (0.7f, 0.3f), (0.4f, 0.55f) })
            DrawColoredPolygon(Px.Ellipse(sun, sr * k, sr * k, 24), new Color(1, 224 / 255f - (1 - k) * 0.2f, 102 / 255f, a));

        float x = inner.Position.X + 18, y = inner.Position.Y;
        float h = inner.Size.Y;
        Px.Text(this, Px.Small, new Vector2(x, y + 24), "★ FRIENDLY · KICK OFF", 9, Px.Hex(0xffe0a8));
        int ts = (int)Mathf.Clamp(h * 0.26f, 48, 96);
        float ty = y + 26 + ts * 0.78f;
        Px.Text(this, Px.Big, new Vector2(x + 6, ty + 6), "KICK OFF", ts, Px.Hex(0x2a0414));
        Px.Text(this, Px.Big, new Vector2(x + 3, ty + 3), "KICK OFF", ts, Px.Hex(0x7a1024));
        Px.Text(this, Px.Big, new Vector2(x, ty), "KICK OFF", ts, Px.Hex(0xffe066));

        // The match-up.
        var info = Club.Info();
        var opp = Club.OpponentInfo();
        float my = ty + 14;
        float colW = (inner.Size.X - 36 - 60) / 2;
        Team(new Vector2(x, my), info, Club.TeamRating(), colW, Club.S.Kit.Main, Club.S.Kit.Secondary);
        var vs = new Rect2(x + colW + 8, my + 10, 44, 28);
        Px.Frame(this, vs, Px.Hex(0x2a0414), Px.Hex(0xffe066), new Color(0, 0, 0, 0.4f), 3, 4);
        Px.TextC(this, Px.Big, vs.GetCenter().X, vs.GetCenter().Y + 7, "VS", 22, Px.Hex(0xffe066));
        Team(new Vector2(x + colW + 60, my), opp, Club.OpponentLevel(_ui.NextSeed), colW, opp.Kit.Shirt, opp.Kit.Shirt2);

        // Actions.
        float by = inner.End.Y - 82;
        float bw = Mathf.Min(260, inner.Size.X * 0.48f);
        GoldButton("play", new Rect2(x, by, bw, 48), "PLAY MATCH  >", 32, () => _ui.App.PickGround());
        float tw = Mathf.Min(150, (inner.Size.X - bw - 54));
        GhostButton("train", new Rect2(x + bw + 16, by, tw, 48), "TRAINING", 24, () => _ui.App.PickDrill());
        Px.Text(this, Px.Big, new Vector2(x, inner.End.Y - 14), "Win +1,500 · Draw +800 · +150 per goal", 17, Px.Hex(0xffe6d2, 0.85f));
    }

    void Team(Vector2 p, TeamInfo t, int ovr, float w, int main, int second)
    {
        Art.Crest(this, new Rect2(p, new Vector2(38, 44)), main, second, t.Short);
        Px.Text(this, Px.Big, p + new Vector2(46, 22), Px.Fit(Px.Big, t.Name, 22, w - 50), 22, Px.Ink, new Color(0, 0, 0, 0.45f));
        Px.Text(this, Px.Small, p + new Vector2(46, 38), $"{ovr} OVR", 8, Px.Hex(0xffe0a8));
    }

    void TileFrame(string key, Rect2 r, string title, Action tap, Color? fill = null)
    {
        bool held = Held(key);
        var rr = held ? new Rect2(r.Position + new Vector2(3, 3), r.Size) : r;
        Px.Frame(this, rr, fill ?? Px.Glass, Px.Line2, held ? null : Px.Shadow);
        Px.Text(this, Px.Big, rr.Position + new Vector2(16, 38), title, 34, Px.Ink, new Color(0, 0, 0, 0.55f), 3);
        // A blinking cyan chevron.
        float nudge = (T % 1) < 0.5 ? 0 : 3;
        var c = new Vector2(rr.End.X - 26 + nudge, rr.Position.Y + 26);
        for (int i = 0; i < 4; i++)
        {
            DrawRect(new Rect2(c.X + i * 3, c.Y - 9 + i * 3, 3, 3), Px.Cyan);
            DrawRect(new Rect2(c.X + i * 3, c.Y + 9 - i * 3, 3, 3), Px.Cyan);
        }
        Tap(key, r, tap);
    }

    void SquadTile(Rect2 r)
    {
        TileFrame("squad", r, "SQUAD", () => _ui.Go(_ui.Squad));
        var p = r.Position + (Held("squad") ? new Vector2(3, 3) : Vector2.Zero);
        int ovr = Club.TeamRating();
        int os = (int)Mathf.Clamp(r.Size.Y * 0.42f, 40, 72);
        float oy = p.Y + 44 + os * 0.75f;
        Px.Text(this, Px.Big, new Vector2(p.X + 22, oy), ovr.ToString(), os, Px.Hex(0x6b3f00));
        Px.Text(this, Px.Big, new Vector2(p.X + 19, oy - 3), ovr.ToString(), os, Px.Hex(0xffe066));
        float ox = p.X + 26 + Px.Width(Px.Big, ovr.ToString(), os);
        Px.Text(this, Px.Small, new Vector2(ox, oy - os * 0.4f), "TEAM", 8, Px.InkDim);
        Px.Text(this, Px.Small, new Vector2(ox, oy - os * 0.4f + 12), "RATING", 8, Px.InkDim);
        Px.Text(this, Px.Small, new Vector2(p.X + 18, r.End.Y - 14 + (p.Y - r.Position.Y)), $"{Club.Formation.Name} · {Club.S.Cards.Count} PLAYERS", 8, Px.InkDim);
        // The three best starters.
        var kit = Club.Info().Kit;
        var best = Club.Starters().Where(c => c != null).OrderByDescending(c => c.Overall).Take(3).ToList();
        float s = Mathf.Clamp(r.Size.Y * 0.36f, 34, 54);
        float x = r.End.X - 16 - best.Count * (s + 12) + 12 + (p.X - r.Position.X);
        float y = r.End.Y - 16 - s - 14 + (p.Y - r.Position.Y);
        foreach (var c in best)
        {
            var box = new Rect2(x, y, s, s);
            Px.Frame(this, box, new Color(0.08f, 0.07f, 0.2f, 0.9f), Art.RarityColor(c.Rarity), new Color(0, 0, 0, 0.45f), 3, 4);
            Art.Avatar(this, box.Grow(-3), c, kit);
            Px.TextC(this, Px.Big, box.GetCenter().X, box.End.Y + 14, c.Overall.ToString(), 18, Px.Ink, new Color(0, 0, 0, 0.4f), 1);
            x += s + 12;
        }
    }

    void StoreTile(Rect2 r)
    {
        TileFrame("store", r, "STORE", () => _ui.Go(_ui.Store));
        var o = Held("store") ? new Vector2(3, 3) : Vector2.Zero;
        // Two packs floating in steps.
        float bob = Mathf.Floor((float)(T * 2 % 4)) switch { 1 => -3, 2 => -6, 3 => -3, _ => 0 };
        float ph = Mathf.Clamp(r.Size.Y - 58, 44, 110);
        float pw = ph / 1.4f;
        Art.Pack(this, new Rect2(r.End.X - pw * 1.6f - 30 + o.X, r.End.Y - ph - 20 + bob * 0.6f + o.Y, pw, ph), Packs.All[4]);
        Art.Pack(this, new Rect2(r.End.X - pw - 22 + o.X, r.End.Y - ph - 12 + bob + o.Y, pw, ph), Packs.All[3]);
        long ms = Club.FreePackIn;
        string s = ms > 0 ? $"FREE PACK IN {Px.Clock(ms)}" : "FREE PACK READY!";
        float w = Px.Width(Px.Small, s, 8) + 20;
        var chip = new Rect2(r.Position.X + 19 + o.X, r.End.Y - 38 + o.Y, w, 24);
        bool ready = ms == 0;
        var fill = ready ? ((T % 1) < 0.5 ? Px.Hex(0x3ddc84) : Px.Win) : new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.9f);
        Px.Frame(this, chip, fill, ready ? Px.Hex(0x1a7a44) : Px.Line2, Px.ShadowSoft, 3, 4);
        Px.Text(this, Px.Small, chip.Position + new Vector2(10, 16), s, 8, ready ? Px.Hex(0x06240f) : Px.Ink);
    }

    void Footer(float W, float H)
    {
        Px.Text(this, Px.Small, new Vector2(18, H - 12), "JOYSTICK TO MOVE · PASS / THROUGH / KICK", 8, Px.InkDim);
        var notes = new Rect2(W - 16 - 120, H - 30, 120, 24);
        bool held = Held("notes");
        Px.Frame(this, held ? new Rect2(notes.Position + Vector2.One * 2, notes.Size) : notes, new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.85f), Px.Line2, held ? null : Px.ShadowSoft, 3, 4);
        Px.TextC(this, Px.Small, notes.GetCenter().X + (held ? 2 : 0), notes.GetCenter().Y + 5 + (held ? 2 : 0), "PATCH NOTES", 8, Px.Ink);
        Tap("notes", notes, () => _ui.Open(new NotesModal(_ui)));
        Px.TextR(this, Px.Small, notes.Position.X - 14, H - 12, "V" + _ui.Version, 9, Px.Cyan);
    }
}
