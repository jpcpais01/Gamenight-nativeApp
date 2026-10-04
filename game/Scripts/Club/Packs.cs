using System;
using System.Collections.Generic;
using GameNight.Sim;

namespace GameNight.Club;

public sealed class PackDef
{
    public string Id = "", Name = "", Tagline = "";
    /// <summary>0 = the free pack (on a timer).</summary>
    public int Price;
    public int Cards;
    /// <summary>Chance weights per rarity (Common .. Icon).</summary>
    public double[] Odds = Array.Empty<double>();
    /// <summary>At least one card of this rarity or better.</summary>
    public Rarity? Guarantee;
    /// <summary>Pack art: main, dark, light.</summary>
    public int[] Colors = Array.Empty<int>();
    public string Emblem = "*";
}

/// <summary>The store's packs (the PWA's src/meta/packs.ts).</summary>
public static class Packs
{
    public static readonly PackDef[] All =
    {
        new PackDef { Id = "free", Name = "Daily Pack", Tagline = "Free every few hours", Price = 0, Cards = 3, Odds = new double[] { 80, 17, 2.7, 0.3, 0 }, Colors = new[] { 0x3b7a57, 0x1e3f2e, 0x9be0b4 }, Emblem = "DAILY" },
        new PackDef { Id = "bronze", Name = "Bronze Pack", Tagline = "5 players", Price = 750, Cards = 5, Odds = new double[] { 72, 23, 4.4, 0.55, 0.05 }, Colors = new[] { 0xb8763f, 0x5a3216, 0xf0c08a }, Emblem = "BRONZE" },
        new PackDef { Id = "silver", Name = "Silver Pack", Tagline = "5 players · 1 Rare+", Price = 2000, Cards = 5, Odds = new double[] { 45, 43, 10.5, 1.3, 0.2 }, Guarantee = Rarity.Rare, Colors = new[] { 0xaab7c7, 0x3d4a5c, 0xf2f6fb }, Emblem = "SILVER" },
        new PackDef { Id = "gold", Name = "Gold Pack", Tagline = "6 players · 1 Epic+", Price = 5000, Cards = 6, Odds = new double[] { 20, 50, 25, 4.4, 0.6 }, Guarantee = Rarity.Epic, Colors = new[] { 0xf4c542, 0x7a5410, 0xfff1b8 }, Emblem = "GOLD" },
        new PackDef { Id = "legend", Name = "Legend Pack", Tagline = "3 players · 1 Legend+", Price = 12000, Cards = 3, Odds = new double[] { 0, 35, 45, 17, 3 }, Guarantee = Rarity.Legendary, Colors = new[] { 0xb05cff, 0x2a0f55, 0xf0d6ff }, Emblem = "LEGEND" },
    };

    static Rarity Roll(Rng rng, double[] odds)
    {
        double total = 0;
        foreach (var o in odds) total += o;
        double x = rng.Next() * total;
        for (int i = 0; i < odds.Length; i++)
        {
            x -= odds[i];
            if (x <= 0) return (Rarity)i;
        }
        return Rarity.Common;
    }

    /// <summary>Open a pack: cards sorted so the best one is revealed last.</summary>
    public static List<Card> Open(PackDef def, long seed)
    {
        var rng = new Rng((uint)seed ^ 0x9e3779b9u);
        var rarities = new Rarity[def.Cards];
        for (int i = 0; i < def.Cards; i++) rarities[i] = Roll(rng, def.Odds);
        if (def.Guarantee is Rarity min && Array.TrueForAll(rarities, r => r < min))
        {
            // Upgrade one card: usually exactly to the guarantee, sometimes beyond.
            int r = (int)min;
            while (r < Cards.Rarities.Length - 1 && rng.Next() < 0.12) r++;
            rarities[0] = (Rarity)r;
        }
        var cards = new List<Card>();
        foreach (var r in rarities) cards.Add(Cards.Generate(rng, r));
        cards.Sort((a, b) => a.Rarity != b.Rarity ? a.Rarity.CompareTo(b.Rarity) : a.Overall.CompareTo(b.Overall));
        return cards;
    }
}
