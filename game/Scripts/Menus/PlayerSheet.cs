using Godot;
using GameNight.Club;

namespace GameNight.Menus;

/// <summary>One player: his card, body, traits, every stat, and quick sell for the bench.</summary>
public sealed partial class PlayerSheet : Modal
{
    readonly Card _c;
    bool _armed;

    public PlayerSheet(Menus ui, Card c) : base(ui)
    {
        _c = c;
    }

    protected override Vector2 BoxSize => new(700, 380);

    protected override void PaintBox(Rect2 b)
    {
        var c = _c;
        var club = Ui.Club;
        float ch = b.Size.Y - 40, cw = ch / 1.4f;
        var card = new Rect2(b.Position + new Vector2(20, 20), new Vector2(cw, ch));
        Art.Card(this, card, c, club.Info().Kit, 0.5f);

        float x = card.End.X + 22, y = b.Position.Y + 48;
        float w = b.End.X - x - 20;
        Px.Text(this, Px.Big, new Vector2(x, y), Px.Fit(Px.Big, c.Name, 34, w - 40), 34, Px.Ink, new Color(0, 0, 0, 0.55f), 3);
        var n = Cards.Nations[c.Nation];
        Px.Flag(this, new Rect2(x, y + 10, 18, 12), n);
        Px.Text(this, Px.Small, new Vector2(x + 24, y + 21), $"{n.Code} · {c.Position} · {Cards.Label(c.Rarity).ToUpperInvariant()} · #{c.Number}", 9, Px.InkDim);
        y += 46;
        float bx = x;
        foreach (var (v, l) in new[] { (c.Height.ToString(), "CM"), (c.Weight.ToString(), "KG"), (Cards.BodyName(c), ""), (c.Foot == 'L' ? "Left" : "Right", "FOOT") })
        {
            Px.Text(this, Px.Big, new Vector2(bx, y), v, 20, Px.Ink);
            bx += Px.Width(Px.Big, v, 20) + 4;
            if (l.Length > 0)
            {
                Px.Text(this, Px.Small, new Vector2(bx, y - 1), l, 8, Px.InkDim);
                bx += Px.Width(Px.Small, l, 8);
            }
            bx += 16;
        }
        var traits = Cards.Traits(c);
        if (traits.Count > 0)
        {
            bx = x;
            y += 26;
            foreach (var t in traits)
            {
                float tw = Px.Width(Px.Big, t, 18) + 14;
                Px.Frame(this, new Rect2(bx, y - 18, tw, 24), new Color(Px.Cyan, 0.12f), new Color(Px.Cyan, 0.6f), null, 2);
                Px.Text(this, Px.Big, new Vector2(bx + 7, y), t, 18, Px.Cyan);
                bx += tw + 8;
            }
        }
        // Playstyles: badge, name, what it adds.
        var styles = Playstyles.Of(c);
        if (styles.Count > 0)
        {
            bx = x;
            y += 30;
            foreach (var ps in styles)
            {
                Art.Playstyle(this, new Vector2(bx + 11, y - 6), 11, ps);
                Px.Text(this, Px.Big, new Vector2(bx + 27, y), ps.Name, 18, Px.Hex(ps.Color));
                bx += 27 + Px.Width(Px.Big, ps.Name, 18) + 16;
            }
        }
        // Stats in two columns (playstyle boosts in the playstyle's colour, on top).
        y += 22;
        var groups = new (string, Stat[])[]
        {
            ("PHYSICAL", new[] { Stat.Pace, Stat.Accel, Stat.Agility, Stat.Stamina, Stat.Strength, Stat.Jumping }),
            ("TECHNICAL", new[] { Stat.Power, Stat.Shooting, Stat.Passing, Stat.Dribbling, Stat.Defending, Stat.Keeping }),
        };
        float colW = (w - 20) / 2;
        float rowH = Mathf.Min(19, (b.End.Y - 70 - y) / 7);
        for (int g = 0; g < 2; g++)
        {
            float gx = x + g * (colW + 20), gy = y;
            Px.Text(this, Px.Small, new Vector2(gx, gy), groups[g].Item1, 8, Px.Cyan);
            gy += rowH;
            foreach (var k in groups[g].Item2)
            {
                int v = c.Stats[k];
                int boosted = Playstyles.Boosted(c, k);
                var col = v >= 85 ? Px.Win : v >= 70 ? Px.Gold : v >= 55 ? Px.Hex(0xffb36b) : Px.Loss;
                Px.Text(this, Px.Small, new Vector2(gx, gy), Cards.StatLabel[k].ToUpperInvariant(), 8, Px.InkDim);
                float barX = gx + 98, barW = colW - 98 - 28;
                DrawRect(new Rect2(barX, gy - 7, barW, 8), new Color(1, 1, 1, 0.08f));
                // Segmented bar.
                float fill = barW * v / 99f;
                for (float sx = 0; sx < fill; sx += 8) DrawRect(new Rect2(barX + sx, gy - 7, Mathf.Min(6, fill - sx), 8), col);
                if (boosted > v)
                {
                    float full = barW * boosted / 99f;
                    for (float sx = 0; sx < full; sx += 8)
                        if (sx + 6 > fill) DrawRect(new Rect2(barX + Mathf.Max(sx, fill), gy - 7, Mathf.Min(6, full - sx) - Mathf.Max(0, fill - sx), 8), Px.Cyan);
                }
                Px.TextR(this, Px.Big, gx + colW, gy + 2, boosted.ToString(), 18, boosted > v ? Px.Cyan : Px.Ink);
                gy += rowH;
            }
        }
        // Sell, or a note for starters.
        var sb = new Rect2(x, b.End.Y - 56, 260, 40);
        if (club.IsStarter(c.Id)) Px.Text(this, Px.Small, new Vector2(x, b.End.Y - 30), "IN YOUR STARTING XI", 9, Px.Cyan);
        else if (club.S.Cards.Contains(c))
        {
            string label = _armed ? $"CONFIRM SELL · {Px.Thousands(Cards.SellValue(c))}" : $"QUICK SELL · {Px.Thousands(Cards.SellValue(c))}";
            GhostButton("sell", sb, label, 20, () =>
            {
                if (!_armed)
                {
                    _armed = true;
                    return;
                }
                int v = club.Sell(c.Id);
                Audio.GameAudio.Instance?.Coins();
                Ui.Close(this);
                Ui.Toast($"Sold {c.Name} for {Px.Thousands(v)} coins");
            }, _armed ? Px.Loss : null);
        }
    }
}
