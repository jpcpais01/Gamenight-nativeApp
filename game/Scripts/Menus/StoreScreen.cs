using System;
using Godot;
using GameNight.Club;

namespace GameNight.Menus;

/// <summary>The store (the PWA's store.ts): five packs on a shelf, the free one on a timer.</summary>
public sealed partial class StoreScreen : PxCanvas
{
    readonly Menus _ui;
    readonly Fx.World _fx = new();
    readonly Rect2[] _arts = new Rect2[Packs.All.Length];
    double _sparkle;
    ClubState Club => _ui.Club;

    public StoreScreen(Menus ui)
    {
        _ui = ui;
        AddChild(new Fx.Light(DrawLight));
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!IsVisibleInTree()) return;
        float dt = (float)delta;
        _fx.Step(dt);
        _fx.Motes(Size, Px.Hex(0xc9b6ff), 6, dt, Size.Y);
        // Now and then a glint pops on one of the packs, more often on the rare ones.
        _sparkle -= delta;
        if (_sparkle <= 0)
        {
            _sparkle = 0.25 + _fx.Rng.NextDouble() * 0.5;
            int i = _fx.Rng.Next(Packs.All.Length);
            var r = _arts[i];
            if (r.Size.X > 0)
            {
                var p = r.Position + new Vector2((float)_fx.Rng.NextDouble(), (float)_fx.Rng.NextDouble()) * r.Size;
                _fx.Add(new Fx.Bit { P = p, C = Px.Hex(Packs.All[i].Colors[2]), Life = 0.6f, Max = 0.6f, Size = 4 + Math.Min(i, 5) * 1.2f, Drag = 1, Spin = 6, Kind = Fx.Kind.Star });
            }
        }
        GetChild<Control>(0).QueueRedraw();
    }

    /// <summary>The bright layer: the shine sweeping over each pack, glints and dust.</summary>
    void DrawLight(CanvasItem ci)
    {
        for (int i = 0; i < _arts.Length; i++)
        {
            if (_arts[i].Size.X <= 0) continue;
            float period = 3.2f;
            float k = (float)((T + i * 0.55) % period) / (period * 0.55f);
            Fx.Shine(ci, _arts[i], k, new Color(1, 1, 1, 0.14f + Math.Min(i, 5) * 0.03f));
        }
        _fx.Draw(ci);
    }

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        Fx.Vault(this, Size, Px.Hex(0xb05cff), (float)T, Mathf.Round(H * 0.58f));
        BackButton(new Vector2(14, 12), () => _ui.Go(_ui.Home));
        Title(new Vector2(66, 44), "STORE");
        Coins(W - 16, 14, Club.S.Coins);

        // A row of packs that slides sideways: the core packs, then the special ones.
        ScrollAxis = 2;
        int n = Packs.All.Length;
        float gap = 14, split = 30;
        float tw = Mathf.Clamp((W - 28 - gap * 4) / 5, 150, 176);
        float top = 74, th = H - top - 14;
        int firstSpecial = Array.FindIndex(Packs.All, q => q.Special);
        float X(int i) => 14 + i * (tw + gap) + (firstSpecial >= 0 && i >= firstSpecial ? split : 0) - Scroll;
        Content = X(n - 1) + Scroll + tw + 14;
        Px.Text(this, Px.Small, new Vector2(X(0) + 2, top - 8), "PACKS", 8, Px.Cyan);
        if (firstSpecial >= 0)
        {
            float sx = X(firstSpecial);
            Px.Text(this, Px.Small, new Vector2(sx + 2, top - 8), "SPECIAL PACKS · EACH WITH A TWIST", 8, Px.Neon);
            DrawRect(new Rect2(sx - split / 2 - gap / 2 - 1, top, 2, th), new Color(Px.Neon, 0.35f));
        }
        long freeMs = Club.FreePackIn;
        for (int i = 0; i < n; i++)
        {
            var p = Packs.All[i];
            var r = new Rect2(X(i), top, tw, th);
            if (r.End.X < -10 || r.Position.X > W + 10)
            {
                _arts[i] = default;
                continue;
            }
            bool free = p.Price == 0;
            var pc = Px.Hex(p.Colors[0]);
            Px.Frame(this, r, free ? new Color(20 / 255f, 60 / 255f, 40 / 255f, 0.45f) : new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.55f), free ? Px.Hex(0x2fc070) : new Color(pc, 0.45f), Px.Shadow);
            // A lamp over each pack; the better the pack, the bigger the show.
            var lamp = new Vector2(r.GetCenter().X, r.Position.Y + 3);
            Fx.Spot(this, lamp, new Vector2(lamp.X, r.Position.Y + th * 0.52f), 18, tw * 0.8f, pc.Lerp(Colors.White, 0.4f), 0.08f + Math.Min(i, 5) * 0.015f);
            // The pack, gently bobbing out of step with its neighbours.
            float ah = Mathf.Min(th * 0.5f, (tw - 30) * 1.4f);
            float aw = ah / 1.4f;
            float bob = -2 - Mathf.Sin((float)T * 2.4f + i * 1.3f) * 2;
            var art = new Rect2(r.GetCenter().X - aw / 2, r.Position.Y + 14 + bob, aw, ah);
            if (i >= 3) Fx.Beams(this, art.GetCenter(), pc, 10, aw * 0.85f, 0.08f, (float)T * (0.2f + Math.Min(i - 3, 2) * 0.25f), 0.1f + Math.Min(i - 3, 2) * 0.05f);
            Fx.Glow(this, art.GetCenter(), aw * 0.9f, pc, 0.1f + Math.Min(i, 5) * 0.03f);
            DrawColoredPolygon(Px.Ellipse(new Vector2(art.GetCenter().X, art.End.Y + 8 - bob), aw * 0.42f + bob, 4, 16), new Color(0, 0, 0, 0.4f));
            Art.Pack(this, art, p);
            _arts[i] = art;
            int idx = i;
            Tap("art" + i, art, () => Buy(Packs.All[idx]));
            float y = art.End.Y + 30 - bob;
            Px.TextC(this, Px.Big, r.GetCenter().X, y, p.Name, 24, Px.Ink, new Color(0, 0, 0, 0.55f), 2);
            Px.TextC(this, Px.Small, r.GetCenter().X, y + 16, Px.Fit(Px.Small, p.Tagline.ToUpperInvariant(), 8, tw - 12), 8, Px.InkDim);
            // Odds per rarity.
            float ox = r.Position.X + 10, oy = y + 32;
            for (int k = 0; k < p.Odds.Length; k++)
            {
                if (p.Odds[k] <= 0) continue;
                string s = (p.Odds[k] >= 1 ? Math.Round(p.Odds[k]).ToString() : p.Odds[k].ToString("0.##")) + "%";
                float w = Px.Width(Px.Small, s, 8) + 12;
                if (ox + w > r.End.X - 8)
                {
                    ox = r.Position.X + 10;
                    oy += 14;
                }
                DrawRect(new Rect2(ox, oy - 7, 6, 6), Art.RarityColor((Rarity)k));
                Px.Text(this, Px.Small, new Vector2(ox + 8, oy), s, 8, Px.Ink);
                ox += w + 4;
            }
            // Buy.
            var b = new Rect2(r.Position.X + 10, r.End.Y - 52, tw - 20, 40);
            if (free)
            {
                bool ready = freeMs == 0;
                bool held = Held("buy" + i) && ready;
                var bb = held ? new Rect2(b.Position + Vector2.One * 3, b.Size) : b;
                if (ready)
                {
                    Px.Frame(this, bb, Colors.Transparent, Px.Hex(0x1a7a44), held ? null : new Color(0, 0, 0, 0.45f));
                    Px.Bands(this, bb.Grow(-3), new[] { Px.Hex(0xb6ffd2), Px.Win, Px.Hex(0x2fc070) }, new[] { 0, 0.18f, 0.55f });
                    Px.TextC(this, Px.Big, bb.GetCenter().X, bb.GetCenter().Y + 8, "OPEN FREE", 22, Px.Hex(0x06240f));
                }
                else
                {
                    Px.Frame(this, bb, new Color(0.15f, 0.2f, 0.18f), Px.Line2, Px.ShadowSoft);
                    Px.TextC(this, Px.Big, bb.GetCenter().X, bb.GetCenter().Y + 8, Px.Clock(freeMs), 22, Px.InkDim);
                }
            }
            else
            {
                bool can = Club.S.Coins >= p.Price;
                bool held = Held("buy" + i);
                var bb = held ? new Rect2(b.Position + Vector2.One * 3, b.Size) : b;
                if (can)
                {
                    Px.Frame(this, bb, Colors.Transparent, Px.Hex(0x6b3f00), held ? null : new Color(0, 0, 0, 0.45f));
                    Px.Bands(this, bb.Grow(-3), Px.GoldBands, Px.GoldStops);
                }
                else Px.Frame(this, bb, new Color(0.25f, 0.22f, 0.3f), Px.Line2, Px.ShadowSoft);
                string s = Px.Thousands(p.Price);
                float w = Px.Width(Px.Big, s, 22) + 22;
                Px.Coin(this, new Vector2(bb.GetCenter().X - w / 2, bb.GetCenter().Y - 7), 14);
                Px.Text(this, Px.Big, new Vector2(bb.GetCenter().X - w / 2 + 22, bb.GetCenter().Y + 8), s, 22, can ? Px.Dark : Px.InkDim);
            }
            Tap("buy" + i, b, () => Buy(Packs.All[idx]));
        }
        // More packs off to the right: a blinking chevron says so.
        if (Scroll < ScrollMax - 4)
        {
            float nudge = (T % 1) < 0.5 ? 0 : 3;
            var c = new Vector2(W - 20 + nudge, top + th * 0.42f);
            DrawRect(new Rect2(W - 40, top, 40, th), new Color(0, 0, 0, 0.25f));
            for (int i = 0; i < 5; i++)
            {
                DrawRect(new Rect2(c.X + i * 3 - 12, c.Y - 12 + i * 3, 3, 3), Px.Neon);
                DrawRect(new Rect2(c.X + i * 3 - 12, c.Y + 12 - i * 3, 3, 3), Px.Neon);
            }
            Px.TextR(this, Px.Small, W - 8, c.Y + 28, "MORE", 8, Px.Neon);
        }
        Px.Scanlines(this, new Rect2(0, 0, W, H));
    }

    public void Buy(PackDef p)
    {
        if (p.Price == 0 && Club.FreePackIn > 0)
        {
            _ui.Toast($"Next free pack in {Px.Clock(Club.FreePackIn)}");
            return;
        }
        if (p.Price > 0 && Club.S.Coins < p.Price)
        {
            _ui.Toast("Not enough coins · play a match to earn more");
            return;
        }
        var cards = Club.BuyPack(p);
        if (cards != null) _ui.Open(new PackOpening(_ui, p, cards));
    }
}
