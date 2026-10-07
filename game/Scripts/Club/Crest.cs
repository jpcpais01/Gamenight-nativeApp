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
    /// <summary>A texture over the field (see Patterns).</summary>
    public int Pattern;
    /// <summary>Lettering face: 0 block, 1 pixel.</summary>
    public int Font;
    /// <summary>0..5 champion stars above the crest.</summary>
    public int Stars;
    /// <summary>Founding year on the ribbon ("" = none).</summary>
    public string Year = "1899";
    public int Primary = 0xc8393b, Secondary = 0x14121c, Accent = 0xf3ede0;

    public static readonly string[] Shapes =
    {
        "Classic", "Heater", "Round", "Roundel", "Swiss", "Diamond", "Hexagon", "French", "Pennant",
        "Oval", "Square", "Octagon", "Arch", "Tall", "Crowned", "Gothic", "Triangle", "Drop", "Plate",
    };
    public static readonly string[] Divisions =
    {
        "Plain", "Halves", "Split", "Quarters", "Stripes", "Hoops", "Bend", "Chevron", "Saltire", "Cross", "Chief", "Gyronny",
        "Checks", "Tricolour", "Bend left", "Pall", "Pile", "Waves", "Pinstripes", "Bordure", "Diamonds", "Sunburst", "Base", "Canton",
    };
    public static readonly string[] Patterns = { "None", "Dots", "Lines", "Grid", "Diagonal", "Zigzag", "Rings", "Scales", "Stars" };
    public static readonly string[] Emblems =
    {
        "None", "Star", "Ball", "Crown", "Bolt", "Castle", "Anchor", "Flame", "Wings", "Eagle", "Lion", "Oak", "Wolf", "Sun", "Three stars",
        "Heart", "Moon", "Mountain", "Trident", "Keys", "Fleur", "Trophy", "Clover", "Cross", "Ship", "Wheel", "Bull", "Horse", "Swallow", "Skull", "Gem", "Laurel", "Bee",
    };
    public static readonly string[] TextStyles = { "Centre", "Ribbon", "Top band", "Hidden", "Low band", "Monogram" };
    public static readonly string[] Fonts = { "Block", "Pixel" };
    public static readonly string[] Borders = { "None", "Thin", "Bold", "Double", "Gold", "Dashed", "Studs", "Inset", "Triple" };

    /// <summary>Colours for the crest that look good together.</summary>
    public static readonly int[] Palette =
    {
        0xc8393b, 0x8f1f24, 0xe0522b, 0xf28c28, 0xffd447, 0xe8c35a, 0x3ddc84, 0x1f6b4a,
        0x0f3d2e, 0x2fb6a8, 0x7ff6ff, 0x4aa3ff, 0x2457d6, 0x23345e, 0x14123a, 0x6a3fd1,
        0xb05cff, 0xe0559b, 0xf3ede0, 0xffffff, 0xb9bdc4, 0x6b6f78, 0x2a2a2a, 0x0e0e10,
    };

    public Crest Clone() => (Crest)MemberwiseClone();

    /// <summary>A key that changes whenever anything drawn changes.</summary>
    public string Key => $"{Shape}|{Division}|{Pattern}|{Emblem}|{Text}|{TextStyle}|{Font}|{Border}|{Stars}|{Year}|{Primary}|{Secondary}|{Accent}";
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
    /// <summary>0 clean-shaven, 1 stubble, 2 a beard, 3 moustache and goatee (as Body.FacialHair).</summary>
    public int Facial;
    /// <summary>Coat colour; -1 = the outfit's own.</summary>
    public int Coat = -1;
    /// <summary>Tie, scarf, stripes and trim: -1 the outfit's own, -2 club main, -3 club second, else a colour.</summary>
    public int Accent = -1;

    public static readonly int[] Skins = { 0xf6d5b8, 0xf1c9a5, 0xe0ac7e, 0xd9a77c, 0xc68a5c, 0xa86f45, 0x8d5a3b, 0x5e3a24 };
    public static readonly int[] Hairs = { 0x1b1410, 0x2e1f15, 0x4a3324, 0x8a5a2e, 0xb5482a, 0xc9a25a, 0x8f8f8f, 0xd6d3cc, 0xf2efe6, -1 };
    public static readonly (int style, string name)[] HairStyles = { (1, "Buzz"), (0, "Short"), (2, "Curly"), (3, "Quiff") };
    public static readonly string[] Facials = { "Clean", "Stubble", "Beard", "Goatee" };
    public static readonly int[] Coats = { -1, 0x23262e, 0x1c2438, 0x15171b, 0x5a5f69, 0xa27a4c, 0x6b2430, 0x2c4a32, 0xe9e6dd };
    public static readonly int[] Accents = { -1, -2, -3, 0xf1efe8, 0x16110d, 0xd8b04a, 0xb22a2a, 0x2a5ab2 };

    /// <summary>Facial hair takes the hair's colour; a bald man's is a dark brown.</summary>
    public int BeardColor => Hair < 0 ? 0x4a3324 : Hair;

    /// <summary>A random manager (keeps the name).</summary>
    public static Coach Random(System.Random r, string name) => new()
    {
        Name = name,
        Skin = Skins[r.Next(Skins.Length)],
        Hair = r.Next(6) == 0 ? -1 : Hairs[r.Next(Hairs.Length - 1)],
        HairStyle = HairStyles[r.Next(HairStyles.Length)].style,
        Height = System.Math.Round(1.68 + r.NextDouble() * 0.26, 2),
        Build = r.Next(3),
        Style = (CoachStyle)r.Next(4),
        Temper = (CoachTemper)r.Next(3),
        Facial = r.Next(4),
        Coat = r.Next(2) == 0 ? -1 : Coats[r.Next(Coats.Length)],
        Accent = r.Next(2) == 0 ? -1 : Accents[r.Next(Accents.Length)],
    };
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

    /// <summary>A manager's own outfit: the style's, with his coat and accent colours on it.</summary>
    public static Outfit ForCoach(Coach c, int shirt, int second, bool away = false)
    {
        var o = For(c.Style, shirt, away);
        if (c.Coat >= 0)
            o = o with { Coat = c.Coat, Cuff = o.Cuff == o.Coat ? c.Coat : o.Cuff, Trousers = c.Style == CoachStyle.Suit ? c.Coat : o.Trousers, Stripe = c.Style == CoachStyle.Suit ? c.Coat : o.Stripe };
        int acc = c.Accent switch { -2 => shirt, -3 => second, _ => c.Accent };
        if (c.Accent != -1) o = o with { Trim = acc, Stripe = c.Style == CoachStyle.Track ? acc : o.Stripe };
        return o;
    }
}
