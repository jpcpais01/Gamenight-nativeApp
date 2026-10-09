using System;
using GameNight.Sim;

namespace GameNight.Club;

/// <summary>A themed event: its cards wear their own finish and play a few points above the
/// rarity they were drawn at. They only come out of the event's own pack.</summary>
public sealed class EventDef
{
    public string Id = "", Name = "", Label = "";
    /// <summary>Card face bands (top to bottom), trim, accent, ink.</summary>
    public int[] Face = Array.Empty<int>();
    public int Trim, Accent, Ink;
    /// <summary>Points added to every stat.</summary>
    public int Boost = 4;
    /// <summary>An epic set: a premium finish (double gold frame, corner gems, foil), and every
    /// card in its pack wears it.</summary>
    public bool Epic;
}

public static class Events
{
    public static readonly EventDef[] All =
    {
        new EventDef
        {
            Id = "halloween", Name = "Fright Night", Label = "FRIGHT NIGHT",
            Face = new[] { 0x3a1458, 0x2a0d42, 0x1c0830, 0x2a0f1c, 0x5a2208 }, Trim = 0xff7a1a, Accent = 0xffa53a, Ink = 0xffe2b8,
        },
        new EventDef
        {
            Id = "frost", Name = "Winter Frost", Label = "WINTER FROST",
            Face = new[] { 0xffffff, 0xeaf7ff, 0xc8ecff, 0x9fd8fa, 0x6fbef0 }, Trim = 0x2f8fd8, Accent = 0xffffff, Ink = 0x0c2a4c,
        },
        new EventDef
        {
            Id = "carnival", Name = "Carnival", Label = "CARNIVAL",
            Face = new[] { 0xff5fb0, 0xf2308c, 0xc8146e, 0x9a0c58, 0x6a0840 }, Trim = 0xffd447, Accent = 0x2ef2c8, Ink = 0xfff6e0,
        },
        new EventDef
        {
            Id = "cosmic", Name = "Cosmic", Label = "COSMIC",
            Face = new[] { 0x1a1450, 0x120e3c, 0x0a0828, 0x0c0a30, 0x1e0c48 }, Trim = 0x7ff6ff, Accent = 0xff7ae6, Ink = 0xeaf6ff,
        },
        new EventDef
        {
            Id = "inferno", Name = "Inferno", Label = "INFERNO",
            Face = new[] { 0x1a0a08, 0x260c08, 0x3a1008, 0x6a1a08, 0xb8360c }, Trim = 0xff5a1a, Accent = 0xffd23a, Ink = 0xfff0d8,
        },
        new EventDef
        {
            Id = "neon", Name = "Neon City", Label = "NEON CITY",
            Face = new[] { 0x14062e, 0x2a0a4e, 0x4c1070, 0x2a0a48, 0x12052a }, Trim = 0x2ef2ff, Accent = 0xff3ad8, Ink = 0xf6eaff,
        },
        new EventDef
        {
            Id = "dragon", Name = "Dragon New Year", Label = "DRAGON",
            Face = new[] { 0xe8382c, 0xcc2220, 0xac1616, 0x8a0e12, 0x620a0c }, Trim = 0xffd447, Accent = 0xffeaa0, Ink = 0xfff4d0,
        },
        // The epic sets.
        new EventDef
        {
            Id = "royal", Name = "Crown Jewels", Label = "CROWN JEWELS", Epic = true, Boost = 6,
            Face = new[] { 0x3448c4, 0x2434a0, 0x18247e, 0x111a62, 0x0b1046 }, Trim = 0xffd447, Accent = 0xff4a6a, Ink = 0xfff6dc,
        },
        new EventDef
        {
            Id = "shogun", Name = "Shogun", Label = "SHOGUN", Epic = true, Boost = 6,
            Face = new[] { 0x34181c, 0x241014, 0x180a0e, 0x221014, 0x341418 }, Trim = 0xe8c060, Accent = 0xff3a3a, Ink = 0xfff2e6,
        },
        new EventDef
        {
            Id = "pharaoh", Name = "Pharaoh", Label = "PHARAOH", Epic = true, Boost = 6,
            Face = new[] { 0xfff0b8, 0xf6d270, 0xe0ac40, 0xc08424, 0x8e5a14 }, Trim = 0x1e44b0, Accent = 0x23b6a6, Ink = 0x1a1030,
        },
        new EventDef
        {
            Id = "thunder", Name = "Thunder God", Label = "THUNDER GOD", Epic = true, Boost = 6,
            Face = new[] { 0x40406a, 0x2e2e52, 0x20203e, 0x16162e, 0x0e0e20 }, Trim = 0xffe84a, Accent = 0xa88aff, Ink = 0xf4f6ff,
        },
        new EventDef
        {
            Id = "atlantis", Name = "Atlantis", Label = "ATLANTIS", Epic = true, Boost = 6,
            Face = new[] { 0x2ee6d2, 0x14b2b8, 0x0a8098, 0x0a5674, 0x06304c }, Trim = 0xc8fff6, Accent = 0xffd447, Ink = 0xf0ffff,
        },
    };

    public static EventDef Of(Card c) => c?.Event == null ? null : Of(c.Event);
    public static EventDef Of(string id) => Array.Find(All, e => e.Id == id);

    /// <summary>Turn a freshly drawn card into an event card: the theme, and every stat lifted.</summary>
    public static Card Make(Card c, EventDef e)
    {
        c.Event = e.Id;
        foreach (var k in Cards.StatKeys)
        {
            if (k == Stat.Keeping && c.Position != Position.GK) continue;
            c.Stats[k] = Math.Min(99, c.Stats[k] + e.Boost);
        }
        return c;
    }
}
