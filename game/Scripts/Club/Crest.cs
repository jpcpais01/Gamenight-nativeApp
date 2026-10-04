namespace GameNight.Club;

/// <summary>
/// The club crest (the PWA's src/meta/crest.ts): a shield shape, a field division, an emblem,
/// lettering and trim, in three colours.
/// </summary>
public sealed class Crest
{
    public int Shape, Division = 1, Emblem = 2;
    /// <summary>Up to 4 letters.</summary>
    public string Text = "ROS";
    public int TextStyle = 1, Border = 2;
    /// <summary>0..5 champion stars above the crest.</summary>
    public int Stars;
    /// <summary>Founding year on the ribbon ("" = none).</summary>
    public string Year = "1899";
    public int Primary = 0xc8393b, Secondary = 0x14121c, Accent = 0xf3ede0;

    public static readonly string[] Shapes = { "Classic", "Heater", "Round", "Roundel", "Swiss", "Diamond", "Hexagon", "French", "Pennant" };
    public static readonly string[] Divisions = { "Plain", "Halves", "Split", "Quarters", "Stripes", "Hoops", "Bend", "Chevron", "Saltire", "Cross", "Chief", "Gyronny" };
    public static readonly string[] Emblems = { "None", "Star", "Ball", "Crown", "Bolt", "Castle", "Anchor", "Flame", "Wings", "Eagle", "Lion", "Oak", "Wolf", "Sun", "Three stars" };
    public static readonly string[] TextStyles = { "Centre", "Ribbon", "Top band", "Hidden" };
    public static readonly string[] Borders = { "None", "Thin", "Bold", "Double", "Gold" };

    public Crest Clone() => (Crest)MemberwiseClone();

    /// <summary>A key that changes whenever anything drawn changes.</summary>
    public string Key => $"{Shape}|{Division}|{Emblem}|{Text}|{TextStyle}|{Border}|{Stars}|{Year}|{Primary}|{Secondary}|{Accent}";
}

/// <summary>The drop banner over the home end: its words, and which club colour it's painted in.</summary>
public sealed class Banner
{
    public string Text = "ONE CLUB · ONE NIGHT";
    /// <summary>"main", "secondary" or "dark".</summary>
    public string Color = "main";
}

public enum CoachStyle { Suit, Coat, Track, Puffer }
public enum CoachTemper { Cool, Fiery, Showman }

/// <summary>The manager: how he looks and dresses, and how he takes it on the touchline.</summary>
public sealed class Coach
{
    public string Name = "The Gaffer";
    public int Skin = 0xd9a77c;
    /// <summary>Hair colour; -1 = bald.</summary>
    public int Hair = 0x8f8f8f;
    public int HairStyle;
    /// <summary>Metres.</summary>
    public double Height = 1.8;
    /// <summary>0 slim, 1 average, 2 heavy.</summary>
    public int Build = 1;
    public CoachStyle Style = CoachStyle.Suit;
    public CoachTemper Temper = CoachTemper.Fiery;

    public static readonly int[] Skins = { 0xf1c9a5, 0xe0ac7e, 0xd9a77c, 0xc68a5c, 0x8d5a3b, 0x5e3a24 };
    public static readonly int[] Hairs = { 0x1b1410, 0x4a3324, 0x8a5a2e, 0xc9a25a, 0x8f8f8f, 0xd6d3cc, -1 };
    public static readonly (int style, string name)[] HairStyles = { (0, "Short"), (2, "Curly"), (3, "Bun") };
    public static readonly string[] Builds = { "Slim", "Average", "Heavy" };
    public static readonly string[] StyleNames = { "Suit", "Coat & scarf", "Tracksuit", "Puffer" };
    public static readonly (string name, string about)[] Tempers =
    {
        ("Cool", "Arms folded, a fist pump at most"),
        ("Fiery", "Rages at fouls, boots the water bottle"),
        ("Showman", "Sprints down the line for goals"),
    };

    public Coach Clone() => (Coach)MemberwiseClone();
}

/// <summary>What a manager wears: coat (with its trim and shirt pattern), cuffs, trousers, shoes.</summary>
public readonly record struct Outfit(int Coat, int Trim, int Pattern, int Cuff, int Trousers, int Stripe, int Shoes, int Sole)
{
    static int Shade(int c, double k) => ((int)System.Math.Round(((c >> 16) & 255) * k) << 16) | ((int)System.Math.Round(((c >> 8) & 255) * k) << 8) | (int)System.Math.Round((c & 255) * k);

    /// <summary>The clothes for a style, in the club's colours where it has any.</summary>
    public static Outfit For(CoachStyle style, int shirt, bool away = false)
    {
        switch (style)
        {
            case CoachStyle.Suit:
            {
                int c = away ? 0x1c2438 : 0x23262e;
                return new Outfit(c, 0xf1efe8, 8, 0xf1efe8, c, c, 0x16110d, 0x16110d);
            }
            case CoachStyle.Coat:
                return new Outfit(0xa27a4c, shirt, 8, 0xa27a4c, 0x25262a, 0x25262a, 0x3a2618, 0x1a120c);
            case CoachStyle.Track:
                return new Outfit(Shade(shirt, 0.45), shirt, 0, shirt, 0x1c1e23, shirt, 0xf0efe9, 0x1b1b1d);
            default:
                return new Outfit(0x15171b, 0x2a2d33, 2, 0x15171b, 0x15171b, 0x15171b, 0x1b1b1d, 0xe8e6df);
        }
    }
}
