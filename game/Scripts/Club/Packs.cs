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
    /// <summary>The big mark on the pack's badge.</summary>
    public string Mark = "GN";
    /// <summary>Special packs: only these positions, all from one nation, every card a playstyle
    /// carrier rated at least MinOvr.</summary>
    public Position[] Positions;
    public bool OneNation, Styled, Special;
    public int MinOvr;
    /// <summary>Event packs: the theme (Events.All). The best card is always an event card, the
    /// others sometimes.</summary>
    public string Event;
}

/// <summary>The store's packs (the PWA's src/meta/packs.ts).</summary>
public static class Packs
{
    // Declared before All: static fields initialise in order.
    static readonly double[] EventOdds = { 0, 20, 50, 24, 6 };
    static readonly double[] EpicOdds = { 0, 0, 68, 25, 7 };

    public static readonly PackDef[] All =
    {
        new PackDef { Id = "free", Name = "Daily Pack", Tagline = "Free every few hours", Price = 0, Cards = 3, Odds = new double[] { 80, 17, 2.7, 0.3, 0 }, Colors = new[] { 0x3b7a57, 0x1e3f2e, 0x9be0b4 }, Emblem = "DAILY" },
        new PackDef { Id = "bronze", Name = "Bronze Pack", Tagline = "5 players", Price = 750, Cards = 5, Odds = new double[] { 72, 23, 4.4, 0.55, 0.05 }, Colors = new[] { 0xb8763f, 0x5a3216, 0xf0c08a }, Emblem = "BRONZE" },
        new PackDef { Id = "silver", Name = "Silver Pack", Tagline = "5 players · 1 Rare+", Price = 2000, Cards = 5, Odds = new double[] { 45, 43, 10.5, 1.3, 0.2 }, Guarantee = Rarity.Rare, Colors = new[] { 0xaab7c7, 0x3d4a5c, 0xf2f6fb }, Emblem = "SILVER" },
        new PackDef { Id = "gold", Name = "Gold Pack", Tagline = "6 players · 1 Epic+", Price = 5000, Cards = 6, Odds = new double[] { 20, 50, 25, 4.4, 0.6 }, Guarantee = Rarity.Epic, Colors = new[] { 0xf4c542, 0x7a5410, 0xfff1b8 }, Emblem = "GOLD" },
        new PackDef { Id = "legend", Name = "Legend Pack", Tagline = "3 players · 1 Legend+", Price = 12000, Cards = 3, Odds = new double[] { 0, 35, 45, 17, 3 }, Guarantee = Rarity.Legendary, Colors = new[] { 0xb05cff, 0x2a0f55, 0xf0d6ff }, Emblem = "LEGEND" },
        new PackDef { Id = "ultimate", Name = "Ultimate Pack", Tagline = "4 players · 1 Icon · all Epic+", Price = 60000, Cards = 4, Odds = new double[] { 0, 0, 45, 38, 17 }, Guarantee = Rarity.Icon, Colors = new[] { 0x4a3d1c, 0x0d0a04, 0xffe680 }, Emblem = "ULTIMATE", Mark = "U" },

        // Special packs: a twist each.
        new PackDef { Id = "strike", Name = "Strikeforce", Tagline = "5 attackers · 1 Epic+", Price = 9000, Cards = 5, Odds = new double[] { 10, 50, 32, 7, 1 }, Guarantee = Rarity.Epic, Colors = new[] { 0xe0482f, 0x4a0d0a, 0xffc2a8 }, Emblem = "STRIKERS", Mark = "ATK", Special = true,
            Positions = new[] { Position.ST, Position.ST, Position.LW, Position.RW, Position.CAM } },
        new PackDef { Id = "wall", Name = "Iron Wall", Tagline = "5 defenders · 1 Epic+", Price = 9000, Cards = 5, Odds = new double[] { 10, 50, 32, 7, 1 }, Guarantee = Rarity.Epic, Colors = new[] { 0x3d6fb8, 0x0c1c3a, 0xbcd6ff }, Emblem = "DEFENDERS", Mark = "DEF", Special = true,
            Positions = new[] { Position.CB, Position.CB, Position.LB, Position.RB, Position.CDM } },
        new PackDef { Id = "gloves", Name = "Safe Hands", Tagline = "3 keepers · 1 Legend+", Price = 15000, Cards = 3, Odds = new double[] { 0, 40, 42, 15, 3 }, Guarantee = Rarity.Legendary, Colors = new[] { 0x2fb35f, 0x0a3018, 0xc8ffd8 }, Emblem = "KEEPERS", Mark = "GK", Special = true,
            Positions = new[] { Position.GK } },
        new PackDef { Id = "nation", Name = "One Nation", Tagline = "5 players · one country · 1 Legend+", Price = 25000, Cards = 5, Odds = new double[] { 0, 45, 40, 12, 3 }, Guarantee = Rarity.Legendary, Colors = new[] { 0xf2f2f2, 0x2a2a3a, 0xffffff }, Emblem = "NATION", Mark = "1", Special = true, OneNation = true },
        new PackDef { Id = "styled", Name = "Signature", Tagline = "3 players · 85+ · all with playstyles", Price = 40000, Cards = 3, Odds = new double[] { 0, 0, 0, 78, 22 }, Colors = new[] { 0xff4fa3, 0x3a0828, 0xffd0ea }, Emblem = "SIGNATURE", Mark = "PS", Special = true, Styled = true, MinOvr = 85 },

        // Event packs: themed cards with their own look, a few points stronger.
        new PackDef { Id = "ev-halloween", Name = "Fright Night", Tagline = "4 players · Halloween cards", Price = 30000, Cards = 4, Odds = EventOdds, Guarantee = Rarity.Epic, Colors = new[] { 0xff7a1a, 0x1c0830, 0xffd08a }, Emblem = "FRIGHT", Mark = "", Event = "halloween" },
        new PackDef { Id = "ev-frost", Name = "Winter Frost", Tagline = "4 players · frozen cards", Price = 30000, Cards = 4, Odds = EventOdds, Guarantee = Rarity.Epic, Colors = new[] { 0x8fd6ff, 0x1c4a7a, 0xffffff }, Emblem = "FROST", Mark = "", Event = "frost" },
        new PackDef { Id = "ev-carnival", Name = "Carnival", Tagline = "4 players · carnival cards", Price = 30000, Cards = 4, Odds = EventOdds, Guarantee = Rarity.Epic, Colors = new[] { 0xf2308c, 0x4a0830, 0xffd447 }, Emblem = "CARNIVAL", Mark = "", Event = "carnival" },
        new PackDef { Id = "ev-cosmic", Name = "Cosmic", Tagline = "4 players · cards from space", Price = 30000, Cards = 4, Odds = EventOdds, Guarantee = Rarity.Epic, Colors = new[] { 0x5a3ad8, 0x0a0828, 0x7ff6ff }, Emblem = "COSMIC", Mark = "", Event = "cosmic" },
        new PackDef { Id = "ev-inferno", Name = "Inferno", Tagline = "4 players · cards forged in fire", Price = 30000, Cards = 4, Odds = EventOdds, Guarantee = Rarity.Epic, Colors = new[] { 0xff5a1a, 0x1a0a08, 0xffd23a }, Emblem = "INFERNO", Mark = "", Event = "inferno" },
        new PackDef { Id = "ev-neon", Name = "Neon City", Tagline = "4 players · synthwave cards", Price = 30000, Cards = 4, Odds = EventOdds, Guarantee = Rarity.Epic, Colors = new[] { 0xff3ad8, 0x14062e, 0x2ef2ff }, Emblem = "NEON", Mark = "", Event = "neon" },
        new PackDef { Id = "ev-dragon", Name = "Dragon New Year", Tagline = "4 players · lunar new year cards", Price = 30000, Cards = 4, Odds = EventOdds, Guarantee = Rarity.Epic, Colors = new[] { 0xcc2220, 0x600808, 0xffd447 }, Emblem = "DRAGON", Mark = "", Event = "dragon" },
        // Epic sets: three cards, every one Epic or better and every one in the set's finish.
        new PackDef { Id = "set-royal", Name = "Crown Jewels", Tagline = "3 epic+ players · all in the set", Price = 60000, Cards = 3, Odds = EpicOdds, Guarantee = Rarity.Epic, Colors = new[] { 0x2434a0, 0x0b1046, 0xffd447 }, Emblem = "ROYAL", Mark = "", Event = "royal" },
        new PackDef { Id = "set-shogun", Name = "Shogun", Tagline = "3 epic+ players · all in the set", Price = 60000, Cards = 3, Odds = EpicOdds, Guarantee = Rarity.Epic, Colors = new[] { 0xc81e2a, 0x180a0e, 0xe8c060 }, Emblem = "SHOGUN", Mark = "", Event = "shogun" },
        new PackDef { Id = "set-pharaoh", Name = "Pharaoh", Tagline = "3 epic+ players · all in the set", Price = 60000, Cards = 3, Odds = EpicOdds, Guarantee = Rarity.Epic, Colors = new[] { 0xe0ac40, 0x1e2c70, 0xfff0b8 }, Emblem = "PHARAOH", Mark = "", Event = "pharaoh" },
        new PackDef { Id = "set-thunder", Name = "Thunder God", Tagline = "3 epic+ players · all in the set", Price = 60000, Cards = 3, Odds = EpicOdds, Guarantee = Rarity.Epic, Colors = new[] { 0x5a4ad8, 0x0e0e20, 0xffe84a }, Emblem = "THUNDER", Mark = "", Event = "thunder" },
        new PackDef { Id = "set-atlantis", Name = "Atlantis", Tagline = "3 epic+ players · all in the set", Price = 60000, Cards = 3, Odds = EpicOdds, Guarantee = Rarity.Epic, Colors = new[] { 0x14b2b8, 0x06304c, 0xc8fff6 }, Emblem = "ATLANTIS", Mark = "", Event = "atlantis" },
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
        int? nation = def.OneNation ? (int)(rng.Next() * Cards.Nations.Length) : null;
        foreach (var r in rarities)
        {
            Position? pos = def.Positions != null ? def.Positions[(int)(rng.Next() * def.Positions.Length)] : null;
            Card c = null;
            // A Signature card re-rolls until it carries a playstyle (each card has its own roll).
            for (int tries = 0; tries < 60; tries++)
            {
                int? ovr = def.MinOvr > 0 && r == Rarity.Legendary ? Cards.JsRound(def.MinOvr + (88 - def.MinOvr) * Math.Pow(rng.Next(), 1.6)) : null;
                c = Cards.Generate(rng, r, pos, ovr, nation);
                if (!def.Styled || Playstyles.Of(c).Count > 0) break;
            }
            cards.Add(c);
        }
        if (def.Event != null && Array.Find(Events.All, e => e.Id == def.Event) is EventDef ev)
        {
            // The best card is always the event's; each other one has a fair chance.
            int best = 0;
            for (int i = 1; i < cards.Count; i++)
                if (cards[i].Rarity > cards[best].Rarity || cards[i].Rarity == cards[best].Rarity && cards[i].Overall > cards[best].Overall) best = i;
            for (int i = 0; i < cards.Count; i++)
                if (ev.Epic || i == best || rng.Next() < 0.4) Events.Make(cards[i], ev);
        }
        cards.Sort((a, b) => a.Rarity != b.Rarity ? a.Rarity.CompareTo(b.Rarity) : (a.Event != null) != (b.Event != null) ? (a.Event != null).CompareTo(b.Event != null) : a.Overall.CompareTo(b.Overall));
        return cards;
    }
}
