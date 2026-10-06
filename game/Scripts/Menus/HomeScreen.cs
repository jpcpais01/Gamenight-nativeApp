using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GameNight.Club;
using GameNight.Sim;
using Lg = GameNight.League;

namespace GameNight.Menus;

/// <summary>
/// The home screen. Your club hangs down the left as a banner in its own colours (crest, name,
/// record; tap it to edit the club). Tonight's match is a ticket split between the two clubs'
/// colours with the big PLAY key; under it a dock of tiles leads everywhere else (squad, store,
/// league, training, stadium). The demo match plays behind, washed in your colours.
/// </summary>
public sealed partial class HomeScreen : PxCanvas
{
    readonly Menus _ui;
    ClubState Club => _ui.Club;

    // Where the bright things are this frame, for the light layer.
    Rect2 _crest, _ticket, _play, _store;
    bool _storeReady;

    public HomeScreen(Menus ui)
    {
        _ui = ui;
        AddChild(new Fx.Light(DrawLight));
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (IsVisibleInTree()) GetChild<Control>(0).QueueRedraw();
    }

    /// <summary>Text that reads on a colour: dark ink on light colours, light ink on dark.</summary>
    static Color InkOn(int c) => Px.Hex(c).Luminance > 0.6f ? Px.Hex(0x14121c) : Px.Ink;

    static int Fit(string s, int size, float w)
    {
        while (size > 14 && Px.Width(Px.Big, s, size) > w) size -= 2;
        return size;
    }

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        var main = Px.Hex(Club.S.Kit.Main);
        // The match plays behind: a night wash, with the club's colour bleeding in from the left.
        DrawRect(new Rect2(0, 0, W, H), new Color(10 / 255f, 8 / 255f, 32 / 255f, 0.6f));
        for (int i = 0; i < 12; i++)
            DrawRect(new Rect2(i * W * 0.045f, 0, W * 0.045f, H), new Color(main, 0.2f * (1 - i / 12f)));
        DrawRect(new Rect2(0, H * 0.62f, W, H * 0.38f), new Color(6 / 255f, 4 / 255f, 20 / 255f, 0.35f));
        Px.Scanlines(this, new Rect2(0, 0, W, H));

        float pw = Mathf.Clamp(Mathf.Round(W * 0.26f), 210, 270);
        Pennant(new Rect2(20, 0, pw, H - 46));
        float x0 = 20 + pw + 20, x1 = W - 16;
        TopBar(x0, x1);
        float dockH = Mathf.Clamp(Mathf.Round(H * 0.29f), 96, 124);
        float dockY = H - 30 - dockH;
        Ticket(new Rect2(x0, 58, x1 - x0, dockY - 16 - 58));
        Dock(new Rect2(x0, dockY, x1 - x0, dockH));
        Footer(x0, W, H);
    }

    // ---------------------------------------------------------------- the club banner

    void Pennant(Rect2 r)
    {
        var s = Club.S;
        var k = s.Kit;
        bool held = Held("club");
        float dy = held ? 3 : 0;
        float x = r.Position.X, w = r.Size.X, b = r.End.Y + dy, cx = x + w / 2, tip = 26;
        Vector2[] Shape(float ox, float oy) => new[]
        {
            new Vector2(x + ox, oy), new Vector2(x + w + ox, oy), new Vector2(x + w + ox, b + oy),
            new Vector2(cx + ox, b + tip + oy), new Vector2(x + ox, b + oy),
        };
        if (!held) DrawColoredPolygon(Shape(7, 7), new Color(0, 0, 0, 0.45f));
        DrawColoredPolygon(Shape(0, 0), Px.Hex(k.Secondary));
        // The cloth: main colour inside a secondary hem, darkening towards the tail.
        float hem = 7;
        DrawColoredPolygon(new[]
        {
            new Vector2(x + hem, 0), new Vector2(x + w - hem, 0), new Vector2(x + w - hem, b - 2),
            new Vector2(cx, b + tip - hem - 2), new Vector2(x + hem, b - 2),
        }, Px.Hex(k.Main));
        float stripe = Mathf.Round(w * 0.06f);
        DrawRect(new Rect2(x + hem + 8, 0, stripe, b - 8), new Color(Px.Hex(k.Secondary), 0.55f));
        DrawRect(new Rect2(x + w - hem - 8 - stripe, 0, stripe, b - 8), new Color(Px.Hex(k.Secondary), 0.55f));
        for (int i = 0; i < 5; i++)
        {
            float y0 = b * (0.55f + i * 0.09f);
            DrawColoredPolygon(new[]
            {
                new Vector2(x + hem, y0), new Vector2(x + w - hem, y0), new Vector2(x + w - hem, b - 2),
                new Vector2(cx, b + tip - hem - 2), new Vector2(x + hem, b - 2),
            }, new Color(0, 0, 0, 0.06f));
        }
        // Folds: soft vertical shading.
        DrawRect(new Rect2(x + w * 0.3f, 0, w * 0.08f, b), new Color(1, 1, 1, 0.04f));
        DrawRect(new Rect2(x + w * 0.62f, 0, w * 0.1f, b), new Color(0, 0, 0, 0.06f));
        // The rod it hangs from.
        DrawRect(new Rect2(x - 8, 0, w + 16, 9), Px.Hex(0x2a2440));
        DrawRect(new Rect2(x - 8, 6, w + 16, 3), Px.Hex(0x14102a));
        DrawRect(new Rect2(x - 12, 0, 8, 11), Px.Gold);
        DrawRect(new Rect2(x + w + 4, 0, 8, 11), Px.Gold);

        var ink = InkOn(k.Main);
        var dim = new Color(ink, 0.7f);
        float y = 30 + dy;
        Px.TextC(this, Px.Small, cx, y, "YOUR CLUB", 8, dim);
        float ch = Mathf.Min((w - 70) * 1.24f, r.Size.Y * 0.36f), cw = ch / 1.24f;
        _crest = new Rect2(cx - cw / 2, y + 10, cw, ch);
        CrestArt.Draw(this, _crest, s.Crest);
        y = _crest.End.Y + 30;
        var lines = Px.Wrap(Px.Big, s.Name, 28, w - 34);
        if (lines.Count > 2) lines = new List<string> { Px.Fit(Px.Big, s.Name, 28, w - 34) };
        foreach (var l in lines)
        {
            Px.TextC(this, Px.Big, cx, y, l, 28, ink, new Color(0, 0, 0, 0.35f), 2);
            y += 24;
        }
        if (s.Crest.Year.Length > 0) Px.TextC(this, Px.Small, cx, y + 2, $"EST. {s.Crest.Year} · {Club.ShortName}", 8, dim);
        y += 16;

        // The record.
        var rec = s.Record;
        float bw = (w - 44 - 12) / 3;
        float bx = x + 22;
        foreach (var (n, l, c) in new[] { (rec.Won, "WON", Px.Win), (rec.Drawn, "DRAWN", Px.Ink), (rec.Lost, "LOST", Px.Loss) })
        {
            var box = new Rect2(bx, y, bw, 38);
            DrawRect(box, new Color(0, 0, 0, 0.38f));
            DrawRect(new Rect2(box.Position, new Vector2(bw, 2)), new Color(c, 0.8f));
            Px.TextC(this, Px.Big, box.GetCenter().X, box.Position.Y + 22, n.ToString(), 22, c);
            Px.TextC(this, Px.Small, box.GetCenter().X, box.End.Y - 5, l, 7, Px.InkDim);
            bx += bw + 6;
        }

        // Team rating, big, in the space above the tail.
        y += 38 + 4;
        if (b - 30 - y > 30)
        {
            string ovr = Club.TeamRating().ToString();
            float ow = Px.Width(Px.Big, ovr, 44);
            float ry = y + (b - 30 - y) / 2 + 15;
            Px.Text(this, Px.Big, new Vector2(cx - (ow + 60) / 2 + 3, ry + 3), ovr, 44, new Color(0, 0, 0, 0.4f));
            Px.Text(this, Px.Big, new Vector2(cx - (ow + 60) / 2, ry), ovr, 44, Px.Hex(0xffe066));
            Px.Text(this, Px.Small, new Vector2(cx - (ow + 60) / 2 + ow + 8, ry - 20), "TEAM", 8, dim);
            Px.Text(this, Px.Small, new Vector2(cx - (ow + 60) / 2 + ow + 8, ry - 8), "RATING", 8, dim);
        }

        // Edit tag at the tail.
        string tag = "EDIT CLUB";
        float tw = Px.Width(Px.Small, tag, 8) + 22;
        var t = new Rect2(cx - tw / 2, b - 24, tw, 20);
        Px.Frame(this, t, new Color(0, 0, 0, 0.5f), new Color(ink, 0.5f), null, 2, 0);
        Px.TextC(this, Px.Small, t.GetCenter().X, t.GetCenter().Y + 4, tag, 8, ink);
        Tap("club", new Rect2(x, 0, w, r.End.Y + tip), () => _ui.Go(_ui.ClubStudio));
    }

    // ---------------------------------------------------------------- top

    void TopBar(float x0, float x1)
    {
        // The wordmark.
        float y = 42;
        Px.Text(this, Px.Big, new Vector2(x0 + 3, y + 3), "GAMENIGHT", 34, Px.Hex(0x7a1a5c));
        Px.Text(this, Px.Big, new Vector2(x0, y), "GAMENIGHT", 34, Px.Gold);
        float lw = Px.Width(Px.Big, "GAMENIGHT", 34);
        Px.Text(this, Px.Small, new Vector2(x0 + lw + 10, y - 4), "FOOTBALL · 11 v 11", 8, Px.InkDim);
        Coins(x1, 14, Club.S.Coins);
    }

    // ---------------------------------------------------------------- tonight's match

    void Ticket(Rect2 r)
    {
        var info = Club.Info();
        var opp = Club.OpponentInfo();
        var k = Club.S.Kit;
        float stubW = Mathf.Clamp(Mathf.Round(r.Size.X * 0.23f), 118, 160);
        var body = new Rect2(r.Position, new Vector2(r.Size.X - stubW, r.Size.Y));
        var stub = new Rect2(body.End.X, r.Position.Y, stubW, r.Size.Y);
        _ticket = body;

        DrawRect(r.Translated(new Vector2(7, 7)), new Color(0, 0, 0, 0.45f));
        // Split between the two clubs, on a slant.
        float mid = body.GetCenter().X, lean = r.Size.Y * 0.14f, top = body.Position.Y, bot = body.End.Y;
        var ours = Px.Shade(k.Main, 0.62f);
        var theirs = Px.Shade(opp.Kit.Shirt, 0.62f);
        DrawColoredPolygon(new[] { body.Position, new Vector2(mid + lean, top), new Vector2(mid - lean, bot), new Vector2(body.Position.X, bot) }, ours);
        DrawColoredPolygon(new[] { new Vector2(mid + lean, top), new Vector2(body.End.X, top), new Vector2(body.End.X, bot), new Vector2(mid - lean, bot) }, theirs);
        // The seam in each club's second colour.
        DrawLine(new Vector2(mid + lean - 3, top), new Vector2(mid - lean - 3, bot), Px.Hex(k.Secondary), 4);
        DrawLine(new Vector2(mid + lean + 3, top), new Vector2(mid - lean + 3, bot), Px.Hex(opp.Kit.Shirt2), 4);
        for (float ly = top + 4; ly < bot; ly += 6) DrawRect(new Rect2(body.Position.X, ly, body.Size.X, 2), new Color(0, 0, 0, 0.07f));
        DrawRect(new Rect2(body.Position.X, bot - body.Size.Y * 0.36f, body.Size.X, body.Size.Y * 0.36f), new Color(0, 0, 0, 0.18f));

        // Header strip.
        DrawRect(new Rect2(body.Position, new Vector2(body.Size.X, 24)), new Color(0, 0, 0, 0.42f));
        Px.Text(this, Px.Small, body.Position + new Vector2(14, 16), "★ TONIGHT · FRIENDLY", 8, Px.Gold);
        Px.TextR(this, Px.Small, body.End.X - 14, top + 16, $"MATCH {Club.S.Record.Played + 1}", 8, Px.InkDim);

        // The two clubs, face to face: crests out wide, names towards the seam.
        float playH = 44;
        float zoneTop = top + 32, zoneBot = bot - playH - 18;
        float ch = Mathf.Clamp(zoneBot - zoneTop, 40, 92), cw = ch / 1.24f;
        float cy = zoneTop + ch / 2;
        CrestArt.Draw(this, new Rect2(body.Position.X + 16, zoneTop, cw, ch), Club.S.Crest);
        Art.Crest(this, new Rect2(body.End.X - 16 - cw, zoneTop + 2, cw, ch * 0.96f), opp.Kit.Shirt, opp.Kit.Shirt2, opp.Short);
        float d = 24;
        float nameW = mid - d - 14 - (body.Position.X + 16 + cw + 12);
        Side(body.Position.X + 16 + cw + 12, cy, info.Name, Club.TeamRating(), nameW, false);
        Side(body.End.X - 16 - cw - 12, cy, opp.Name, Club.OpponentLevel(_ui.NextSeed), nameW, true);
        // VS in a gold diamond on the seam.
        var vc = new Vector2(mid + lean * ((bot + top) / 2 - cy) / (bot - top) * 2, cy);
        DrawColoredPolygon(new[] { vc + new Vector2(0, -d - 3), vc + new Vector2(d + 3, 0), vc + new Vector2(0, d + 3), vc + new Vector2(-d - 3, 0) }, Px.Hex(0x2a1404));
        DrawColoredPolygon(new[] { vc + new Vector2(0, -d), vc + new Vector2(d, 0), vc + new Vector2(0, d), vc + new Vector2(-d, 0) }, Px.Gold);
        Px.TextC(this, Px.Big, vc.X, vc.Y + 7, "VS", 22, Px.Dark);

        // The big key.
        float pw = Mathf.Min(320, body.Size.X * 0.62f);
        _play = new Rect2(mid - pw / 2, bot - playH - 12, pw, playH);
        Tap("play", body, () => _ui.App.PickGround());
        GoldButton("play", _play, "PLAY MATCH  >", Fit("PLAY MATCH  >", 32, pw - 20), () => _ui.App.PickGround());

        // The stub: what's at stake, and a barcode.
        Px.Bands(this, stub, new[] { Px.Hex(0x221c52), Px.Hex(0x1a1544), Px.Hex(0x131036) }, new[] { 0, 0.4f, 0.75f });
        float sx = stub.Position.X + 16, sr = stub.End.X - 14;
        Px.Text(this, Px.Small, new Vector2(sx, top + 22), "PRIZES", 8, Px.Cyan);
        float py = top + 46;
        foreach (var (l, v) in new[] { ("WIN", "+1,500"), ("DRAW", "+800"), ("GOAL", "+150") })
        {
            Px.Text(this, Px.Big, new Vector2(sx, py), l, 18, Px.InkDim);
            Px.TextR(this, Px.Big, sr, py, v, 20, Px.Hex(0xffe066));
            py += 25;
        }
        var bars = new Rect2(sx, bot - 44, sr - sx, 26);
        uint h = (uint)_ui.NextSeed * 2654435761u;
        for (float bx = bars.Position.X; bx < bars.End.X - 2;)
        {
            h ^= h << 13;
            h ^= h >> 17;
            h ^= h << 5;
            float bw = 1 + h % 3;
            if ((h >> 8) % 3 != 0) DrawRect(new Rect2(bx, bars.Position.Y, bw, bars.Size.Y), new Color(Px.Ink, 0.75f));
            bx += bw + 1;
        }
        Px.Text(this, Px.Small, new Vector2(sx, bot - 8), $"No {_ui.NextSeed % 1000000:000000}", 7, Px.InkDim);

        // Perforation with notches, then the gold edge.
        for (float py2 = top + 12; py2 < bot - 12; py2 += 9) DrawRect(new Rect2(stub.Position.X - 1, py2, 2, 5), new Color(Px.Ink, 0.4f));
        Px.Ring(this, r, Px.Hex(0xffd447, 0.85f), 2);
        var notch = new Color(10 / 255f, 8 / 255f, 30 / 255f);
        DrawColoredPolygon(Px.Ellipse(new Vector2(stub.Position.X, top), 9, 9, 16), notch);
        DrawColoredPolygon(Px.Ellipse(new Vector2(stub.Position.X, bot), 9, 9, 16), notch);
    }

    /// <summary>A club's name (up to two lines) and rating, set from x towards the seam.</summary>
    void Side(float x, float cy, string name, int ovr, float w, bool right)
    {
        var lines = Px.Wrap(Px.Big, name, 24, w);
        if (lines.Count > 2) lines = new List<string> { Px.Fit(Px.Big, name, 24, w) };
        float y = cy - lines.Count * 22 / 2f + 8;
        foreach (var l in lines)
        {
            if (right) Px.TextR(this, Px.Big, x, y, l, 24, Px.Ink, new Color(0, 0, 0, 0.5f), 2);
            else Px.Text(this, Px.Big, new Vector2(x, y), l, 24, Px.Ink, new Color(0, 0, 0, 0.5f), 2);
            y += 22;
        }
        // Rating: the number and a little bar.
        string o = ovr.ToString();
        float ow = Px.Width(Px.Big, o, 20);
        float bw = Mathf.Min(70, w - ow - 30);
        float ox = right ? x - ow : x;
        Px.Text(this, Px.Big, new Vector2(ox, y + 2), o, 20, Px.Hex(0xffe066), new Color(0, 0, 0, 0.5f), 2);
        float bx = right ? ox - 8 - bw : ox + ow + 8;
        var bar = new Rect2(bx, y - 7, bw, 4);
        DrawRect(bar, new Color(0, 0, 0, 0.45f));
        float fill = bw * Mathf.Clamp((ovr - 40) / 59f, 0, 1);
        DrawRect(new Rect2(right ? bar.End.X - fill : bar.Position.X, bar.Position.Y, fill, 4), Px.Hex(0xffe066));
        Px.Text(this, Px.Small, new Vector2(right ? bx : bx + bw - Px.Width(Px.Small, "OVR", 7), y + 3), "OVR", 7, Px.Hex(0xffe0a8));
    }

    // ---------------------------------------------------------------- the dock

    record struct Tile(string Key, string Label, Color Accent, string Sub, Action Go, Action<Rect2> Art, string Badge = null);

    void Dock(Rect2 r)
    {
        var lg = _ui.Season;
        long free = Club.FreePackIn;
        _storeReady = free == 0;
        var tiles = new List<Tile>
        {
            new("squad", "SQUAD", Px.Cyan, $"{Club.TeamRating()} OVR · {Club.Formation.Name}", () => _ui.Go(_ui.Squad), SquadArt),
            new("store", "STORE", Px.Gold, free > 0 ? $"FREE PACK {Px.Clock(free)}" : "FREE PACK READY", () => _ui.Go(_ui.Store), StoreArt, free > 0 ? null : "FREE"),
            new("leagues", "LEAGUES", Px.Neon, "", () => _ui.Go(_ui.Map), null),
            new("train", "TRAINING", Px.Win, "SKILL DRILLS", () => _ui.App.PickDrill(), TrainArt),
            new("stadium", "STADIUM", Px.Hex(0xb98cff), "BUILD YOURS", () => _ui.Go(_ui.Stadium), StadiumArt),
        };
        // The leagues take a double-width tile: a window onto the map.
        float gap = 10;
        int units = tiles.Count + 1;
        float tw = (r.Size.X - gap * (tiles.Count - 1)) / units;
        float x = r.Position.X;
        foreach (var t in tiles)
        {
            float w = t.Key == "leagues" ? tw * 2 : tw;
            var tr = new Rect2(x, r.Position.Y, w, r.Size.Y);
            if (t.Art == null) LeaguesTile(tr);
            else DockTile(tr, t);
            x += w + gap;
        }
    }

    /// <summary>The leagues: a window onto the map of Europe with your league's pin in it, where you
    /// stand and your trophies. Opens the map.</summary>
    void LeaguesTile(Rect2 r)
    {
        var lg = _ui.Season;
        bool held = Held("leagues");
        var rr = held ? r.Translated(new Vector2(3, 3)) : r;
        Px.Frame(this, rr, Px.Hex(0x120c2e), new Color(Px.Neon, 0.75f), held ? null : Px.Shadow);
        var inner = rr.Grow(-3);
        // The map fills the right of the tile, your league's pin in it.
        var tex = Lg.EuropeMap.Texture(Size);
        var def = lg.Def;
        var pin = Lg.EuropeMap.Project(def.Lon, def.Lat);
        const int S = Lg.EuropeMap.Scale;
        var win = new Rect2(inner.Position.X + inner.Size.X * 0.3f, inner.Position.Y, inner.Size.X * 0.7f, inner.Size.Y);
        var src = new Rect2(((pin - win.Size * new Vector2(0.5f, 0.62f)) / S).Floor(), (win.Size / S).Floor());
        var origin = src.Position;
        DrawRect(win, Px.Hex(0x173f68));
        var clip = src.Intersection(new Rect2(0, 0, tex.GetWidth(), tex.GetHeight()));
        DrawTextureRectRegion(tex, new Rect2(win.Position + (clip.Position - origin) * S, clip.Size * S), clip);
        for (int i = 0; i < 6; i++)
            DrawRect(new Rect2(win.Position.X + i * 10, win.Position.Y, 10, win.Size.Y), new Color(0x12 / 255f, 0x0c / 255f, 0x2e / 255f, 0.9f - i * 0.16f));
        DrawRect(new Rect2(inner.Position.X, inner.End.Y - 40, inner.Size.X, 40), new Color(0x12 / 255f, 0x0c / 255f, 0x2e / 255f, 0.55f));
        var p = win.Position + pin - origin * S;
        float ring = 7 + (float)(T * 10 % 10);
        DrawArc(p + new Vector2(0, -17), ring, 0, Mathf.Tau, 18, new Color(Px.Neon, 1 - (ring - 7) / 10), 2);
        DrawColoredPolygon(Px.Ellipse(p, 6, 2, 10), new Color(0, 0, 0, 0.45f));
        DrawRect(new Rect2(p.X - 1, p.Y - 9, 2, 9), Px.Hex(0x1a1406));
        if (lg.HasCareer) Lg.LeagueArt.Badge(this, new Rect2(p.X - 8, p.Y - 28, 16, 20), Club.S.Crest);
        else
        {
            var tc = Px.Hex(Lg.Ladder.TierColors[def.Tier - 1]);
            Px.Frame(this, new Rect2(p.X - 7, p.Y - 23, 14, 14), tc, tc.Darkened(0.55f), null, 2, 2);
        }
        DrawRect(new Rect2(inner.Position, new Vector2(inner.Size.X, 3)), Px.Neon);
        Px.Text(this, Px.Small, inner.Position + new Vector2(9, 17), "THE LEAGUES OF EUROPE", 7, Px.Cyan, new Color(0, 0, 0, 0.7f), 1);

        string where = !lg.HasCareer ? "PICK YOUR HOME COUNTRY"
            : !lg.Active ? "PICK A LEAGUE ON THE MAP"
            : lg.SeasonOver ? $"{def.Name.ToUpperInvariant()} · SEASON OVER"
            : $"{def.Name.ToUpperInvariant()} · MD {lg.S.Round + 1}" + (lg.S.Round > 0 ? $" · {Lg.LeagueState.Ordinal(lg.Place(0))}" : "");
        Px.Text(this, Px.Big, new Vector2(inner.Position.X + 9, inner.End.Y - 17), "LEAGUES", 22, Px.Neon, new Color(0x5a / 255f, 0x0a / 255f, 0x30 / 255f), 2);
        Px.Text(this, Px.Small, new Vector2(inner.Position.X + 9, inner.End.Y - 5), Px.Fit(Px.Small, where, 7, inner.Size.X - 18), 7, Px.Ink, new Color(0, 0, 0, 0.7f), 1);

        // Trophies, or NEW! before the first season.
        if (lg.HasCareer)
        {
            string t = lg.Trophies.ToString();
            var tr = new Rect2(inner.End.X - Px.Width(Px.Big, t, 18) - 30, inner.Position.Y + 8, Px.Width(Px.Big, t, 18) + 24, 22);
            Px.Frame(this, tr, new Color(0.16f, 0.11f, 0.02f, 0.9f), Px.Hex(0xb37400), null, 2, 3);
            var cp = tr.Position + new Vector2(6, 4);
            DrawRect(new Rect2(cp.X, cp.Y, 10, 7), Px.Gold);
            DrawRect(new Rect2(cp.X + 3, cp.Y + 7, 4, 4), Px.Gold);
            DrawRect(new Rect2(cp.X + 1, cp.Y + 11, 8, 2), Px.Gold);
            Px.Text(this, Px.Big, tr.Position + new Vector2(20, 17), t, 18, Px.Hex(0xffe066));
        }
        else
        {
            bool on = (T % 1) < 0.6;
            var b = new Rect2(rr.End.X - 44, rr.Position.Y - 7, 48, 16);
            Px.Frame(this, b, on ? Px.Neon : Px.Neon.Darkened(0.25f), Px.Hex(0x1a1406, 0.8f), null, 2, 0);
            Px.TextC(this, Px.Small, b.GetCenter().X, b.GetCenter().Y + 4, "NEW!", 8, Px.Dark);
        }
        Tap("leagues", r, () => _ui.Go(_ui.Map));
    }

    void DockTile(Rect2 r, Tile t)
    {
        bool held = Held(t.Key);
        var rr = held ? r.Translated(new Vector2(3, 3)) : r;
        if (t.Key == "store") _store = rr;
        Px.Frame(this, rr, Px.Glass, new Color(t.Accent, 0.55f), held ? null : Px.Shadow);
        var inner = rr.Grow(-3);
        DrawRect(new Rect2(inner.Position, new Vector2(inner.Size.X, 3)), t.Accent);
        DrawRect(new Rect2(inner.Position.X, inner.Position.Y + 3, inner.Size.X, inner.Size.Y * 0.55f), new Color(t.Accent, 0.07f));
        t.Art(new Rect2(inner.Position.X + 8, inner.Position.Y + 9, inner.Size.X - 16, inner.Size.Y - 50));
        Px.Text(this, Px.Big, new Vector2(inner.Position.X + 9, inner.End.Y - 17), Px.Fit(Px.Big, t.Label, 22, inner.Size.X - 18), 22, Px.Ink, new Color(0, 0, 0, 0.5f), 2);
        Px.Text(this, Px.Small, new Vector2(inner.Position.X + 9, inner.End.Y - 5), Px.Fit(Px.Small, t.Sub, 7, inner.Size.X - 14), 7, new Color(t.Accent, 0.9f));
        if (t.Badge != null)
        {
            float bw = Px.Width(Px.Small, t.Badge, 8) + 12;
            bool on = (T % 1) < 0.6;
            var b = new Rect2(rr.End.X - bw + 4, rr.Position.Y - 7, bw, 16);
            Px.Frame(this, b, on ? t.Accent : t.Accent.Darkened(0.25f), Px.Hex(0x1a1406, 0.8f), null, 2, 0);
            Px.TextC(this, Px.Small, b.GetCenter().X, b.GetCenter().Y + 4, t.Badge, 8, Px.Dark);
        }
        Tap(t.Key, r, t.Go);
    }

    void SquadArt(Rect2 a)
    {
        var kit = Club.Info().Kit;
        var best = Club.Starters().Where(c => c != null).OrderByDescending(c => c.Overall).Take(3).ToList();
        if (best.Count == 0) return;
        float s = Mathf.Min(a.Size.Y, a.Size.X * 0.48f);
        var c = a.GetCenter();
        int[] order = best.Count == 3 ? new[] { 1, 2, 0 } : Enumerable.Range(0, best.Count).Reverse().ToArray();
        foreach (int i in order)
        {
            float k = i == 0 ? 1 : 0.78f;
            float ox = i == 0 ? 0 : i == 1 ? -s * 0.62f : s * 0.62f;
            var box = new Rect2(c.X + ox - s * k / 2, a.End.Y - s * k, s * k, s * k);
            Px.Frame(this, box, new Color(0.08f, 0.07f, 0.2f, 0.95f), Art.RarityColor(best[i].Rarity), new Color(0, 0, 0, 0.45f), 2, 3);
            Art.Avatar(this, box.Grow(-2), best[i], kit);
        }
    }

    void StoreArt(Rect2 a)
    {
        float ph = a.Size.Y, pw = ph / 1.4f;
        float bob = Mathf.Sin((float)T * 2.4f) * 2.5f;
        var c = a.GetCenter();
        Art.Pack(this, new Rect2(c.X - pw * 0.95f, a.Position.Y + 4 - bob * 0.6f, pw * 0.9f, ph * 0.9f), Packs.All[4]);
        Art.Pack(this, new Rect2(c.X - pw * 0.3f, a.Position.Y - bob, pw, ph), Packs.All[3]);
    }

    void TrainArt(Rect2 a)
    {
        // Slalom cones and a ball on a strip of grass.
        DrawRect(new Rect2(a.Position.X, a.End.Y - 8, a.Size.X, 8), Px.Hex(0x1f6b2e));
        DrawRect(new Rect2(a.Position.X, a.End.Y - 8, a.Size.X, 2), Px.Hex(0x2f8a40));
        float ch = a.Size.Y * 0.62f;
        for (int i = 0; i < 3; i++)
        {
            float k = 0.7f + i * 0.15f;
            float cx = a.Position.X + a.Size.X * (0.2f + i * 0.28f), by = a.End.Y - 6 - i * 2;
            float h = ch * k, w = h * 0.7f;
            DrawColoredPolygon(new[] { new Vector2(cx, by - h), new Vector2(cx + w / 2, by), new Vector2(cx - w / 2, by) }, Px.Hex(0xf28c28));
            DrawColoredPolygon(new[] { new Vector2(cx - w * 0.2f, by - h * 0.6f), new Vector2(cx + w * 0.2f, by - h * 0.6f), new Vector2(cx + w * 0.28f, by - h * 0.42f), new Vector2(cx - w * 0.28f, by - h * 0.42f) }, Px.Ink);
            DrawRect(new Rect2(cx - w * 0.6f, by - 3, w * 1.2f, 3), Px.Hex(0xc0601a));
        }
        // The ball, hopping between the cones.
        float hop = Mathf.Abs(Mathf.Sin((float)T * 3.2f));
        float bx = a.Position.X + a.Size.X * (0.34f + 0.28f * (0.5f + 0.5f * Mathf.Sin((float)T * 1.6f)));
        float br = Mathf.Max(5, a.Size.Y * 0.11f);
        var bc = new Vector2(bx, a.End.Y - 8 - br - hop * a.Size.Y * 0.3f);
        DrawColoredPolygon(Px.Ellipse(new Vector2(bx, a.End.Y - 5), br, br * 0.3f, 12), new Color(0, 0, 0, 0.35f));
        DrawColoredPolygon(Px.Ellipse(bc, br, br, 14), Px.Ink);
        DrawColoredPolygon(Px.Ellipse(bc + new Vector2(br * 0.15f, -br * 0.1f), br * 0.38f, br * 0.38f, 5), Px.Hex(0x14121c));
    }

    void StadiumArt(Rect2 a)
    {
        var c = new Vector2(a.GetCenter().X, a.GetCenter().Y + a.Size.Y * 0.12f);
        float rx = Mathf.Min(a.Size.X * 0.48f, a.Size.Y * 1.1f), ry = rx * 0.42f;
        var seats = Px.Hex(Club.S.Kit.Main);
        // Floodlight masts behind.
        foreach (float sx in new[] { -1f, 1f })
        {
            var foot = c + new Vector2(sx * rx * 0.92f, -ry * 0.4f);
            var head = foot + new Vector2(sx * 2, -a.Size.Y * 0.62f);
            DrawLine(foot, head, Px.Hex(0x8a84b8), 2);
            DrawRect(new Rect2(head.X - 6, head.Y - 4, 12, 6), Px.Hex(0x3a3470));
            bool lit = ((int)(T * 2) + (sx > 0 ? 1 : 0)) % 4 != 0;
            DrawRect(new Rect2(head.X - 5, head.Y - 3, 10, 3), lit ? Px.Hex(0xfff3c0) : Px.Hex(0xc8c0a0));
        }
        DrawColoredPolygon(Px.Ellipse(c + new Vector2(0, 3), rx, ry, 30), Px.Hex(0x0c0a20));
        DrawColoredPolygon(Px.Ellipse(c, rx, ry, 30), Px.Hex(0x3a3470));
        DrawColoredPolygon(Px.Ellipse(c, rx * 0.9f, ry * 0.86f, 30), seats.Darkened(0.25f));
        DrawColoredPolygon(Px.Ellipse(c, rx * 0.78f, ry * 0.72f, 30), seats);
        DrawColoredPolygon(Px.Ellipse(c, rx * 0.62f, ry * 0.54f, 30), Px.Hex(0x2f8a40));
        DrawColoredPolygon(Px.Ellipse(c, rx * 0.62f, ry * 0.54f, 30).Select(p => new Vector2(p.X, Mathf.Max(p.Y, c.Y))).ToArray(), Px.Hex(0x27783a));
        DrawLine(c - new Vector2(0, ry * 0.54f), c + new Vector2(0, ry * 0.54f), new Color(1, 1, 1, 0.6f), 1);
        // Roof rim.
        DrawPolyline(Px.Ellipse(c, rx, ry, 30).Append(Px.Ellipse(c, rx, ry, 30)[0]).ToArray(), Px.Hex(0x8a84b8), 2);
    }

    // ---------------------------------------------------------------- bottom

    void Footer(float x0, float W, float H)
    {
        var notes = new Rect2(W - 16 - 120, H - 26, 120, 22);
        PhoneLink(x0, notes.Position.X - 60, H);
        bool held = Held("notes");
        Px.Frame(this, held ? notes.Translated(Vector2.One * 2) : notes, new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.85f), Px.Line2, held ? null : Px.ShadowSoft, 2, 3);
        Px.TextC(this, Px.Small, notes.GetCenter().X + (held ? 2 : 0), notes.GetCenter().Y + 4 + (held ? 2 : 0), "PATCH NOTES", 8, Px.Ink);
        Tap("notes", notes, () => _ui.Open(new NotesModal(_ui)));
        Px.TextR(this, Px.Small, notes.Position.X - 12, H - 11, "V" + _ui.Version, 9, Px.Cyan);
    }

    /// <summary>The phone-as-controller corner: on the phone a PLAY ON PC key, on a computer how
    /// to connect (or that a phone is).</summary>
    void PhoneLink(float x0, float x1, float H)
    {
        var host = Link.Host.Instance;
        if (host == null || !host.Listening)
        {
            var r = new Rect2(x0, H - 26, 130, 22);
            bool held = Held("pc");
            Px.Frame(this, held ? r.Translated(Vector2.One * 2) : r, new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.85f), new Color(Px.Cyan, 0.6f), held ? null : Px.ShadowSoft, 2, 3);
            Px.TextC(this, Px.Small, r.GetCenter().X + (held ? 2 : 0), r.GetCenter().Y + 4 + (held ? 2 : 0), "PLAY ON PC", 8, Px.Cyan);
            Tap("pc", r, () => _ui.App.OpenController());
            Px.Text(this, Px.Small, new Vector2(r.End.X + 12, H - 11), Px.Fit(Px.Small, "PHONE AS CONTROLLER", 8, x1 - r.End.X - 12), 8, Px.InkDim);
            return;
        }
        string s = host.PhoneConnected ? "PHONE CONNECTED · IT'S YOUR CONTROLLER"
            : $"PHONE AS CONTROLLER: PLAY ON PC IN THE PHONE APP{(host.Address != "" ? " · THIS PC " + host.Address : "")}";
        Px.Text(this, Px.Small, new Vector2(x0, H - 11), Px.Fit(Px.Small, s, 8, x1 - x0), 8, host.PhoneConnected ? Px.Win : Px.InkDim);
    }

    // ---------------------------------------------------------------- light

    void DrawLight(CanvasItem ci)
    {
        if (_crest.Size.X <= 0) return;
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)T * 1.4f);
        Fx.Glow(ci, _crest.GetCenter(), _crest.Size.Y * 0.62f, Px.Hex(Club.S.Kit.Secondary).Lerp(Colors.White, 0.4f), 0.12f + pulse * 0.05f);
        Fx.Shine(ci, _ticket, (float)((T + 1.5) % 6.0) / 3.2f, new Color(1, 1, 1, 0.07f), 0.18f);
        Fx.Shine(ci, _play, (float)((T + 0.4) % 2.6) / 1.4f, new Color(1, 1, 1, 0.22f));
        if (_storeReady) Fx.Glow(ci, _store.GetCenter(), _store.Size.X * 0.55f, Px.Gold, 0.06f + pulse * 0.06f);
    }
}
