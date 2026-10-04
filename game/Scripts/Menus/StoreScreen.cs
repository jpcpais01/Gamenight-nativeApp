using System;
using Godot;
using GameNight.Club;

namespace GameNight.Menus;

/// <summary>The store (the PWA's store.ts): five packs on a shelf, the free one on a timer.</summary>
public sealed partial class StoreScreen : PxCanvas
{
    readonly Menus _ui;
    ClubState Club => _ui.Club;

    public StoreScreen(Menus ui)
    {
        _ui = ui;
    }

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        NightBackdrop(new[] { Px.Hex(0x120a2a), Px.Hex(0x1c0f3e), Px.Hex(0x2a1450), Px.Hex(0x3a1a58) });
        BackButton(new Vector2(14, 12), () => _ui.Go(_ui.Home));
        Title(new Vector2(66, 44), "STORE");
        Coins(W - 16, 14, Club.S.Coins);

        int n = Packs.All.Length;
        float gap = 14;
        float tw = (W - 28 - gap * (n - 1)) / n;
        float top = 66, th = H - top - 14;
        long freeMs = Club.FreePackIn;
        for (int i = 0; i < n; i++)
        {
            var p = Packs.All[i];
            var r = new Rect2(14 + i * (tw + gap), top, tw, th);
            bool free = p.Price == 0;
            Px.Frame(this, r, free ? new Color(20 / 255f, 60 / 255f, 40 / 255f, 0.5f) : new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.7f), free ? Px.Hex(0x2fc070) : Px.Line, Px.Shadow);
            // The pack, gently bobbing out of step with its neighbours.
            float ah = Mathf.Min(th * 0.5f, (tw - 30) * 1.4f);
            float aw = ah / 1.4f;
            float bob = ((int)(T * 2 + i) % 4) switch { 1 => -2, 2 => -4, 3 => -2, _ => 0 };
            var art = new Rect2(r.GetCenter().X - aw / 2, r.Position.Y + 14 + bob, aw, ah);
            Art.Pack(this, art, p);
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
