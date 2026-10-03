using System.Collections.Generic;

namespace GameNight.Sim;

public sealed class Kit
{
    public int Shirt;
    /// <summary>Trim / sleeves.</summary>
    public int Shirt2;
    public int Shorts;
    public int Socks;
    public int GkShirt;
    public int GkShorts;
    /// <summary>Shirt design, index into Teams.KitPatterns (0 = plain).</summary>
    public int Pattern;
}

public sealed class TeamInfo
{
    public string Name = "";
    public string Short = "";
    public Kit Kit = new Kit();
}

public readonly struct Slot
{
    public readonly Role Role;
    public readonly double X, Z;

    public Slot(Role role, double x, double z)
    {
        Role = role;
        X = x;
        Z = z;
    }
}

public static class TeamData
{
    public static readonly string[] KitPatterns = { "Plain", "Stripes", "Hoops", "Pinstripes", "Halves", "Sash", "Chevron", "Quarters", "Centre band", "Fade" };

    public static readonly TeamInfo[] Default =
    {
        new TeamInfo
        {
            Name = "Rossoneri Athletic", Short = "ROS",
            Kit = new Kit { Shirt = 0xc8393b, Shirt2 = 0x8f1f24, Shorts = 0xf3ede0, Socks = 0xc8393b, GkShirt = 0xe9c24a, GkShorts = 0x2a2a2a },
        },
        new TeamInfo
        {
            Name = "Atlantic Rovers", Short = "ATL",
            Kit = new Kit { Shirt = 0xf1ebdc, Shirt2 = 0x23345e, Shorts = 0x23345e, Socks = 0xf1ebdc, GkShirt = 0x2ba59a, GkShorts = 0x163a36 },
        },
    };

    /// <summary>4-3-3 in team frame: x from -1 (own goal line) to +1 (opponent goal line).</summary>
    public static readonly Slot[] Formation433 =
    {
        new Slot(Role.GK, -0.96, 0),
        new Slot(Role.DEF, -0.62, -0.7),
        new Slot(Role.DEF, -0.7, -0.24),
        new Slot(Role.DEF, -0.7, 0.24),
        new Slot(Role.DEF, -0.62, 0.7),
        new Slot(Role.MID, -0.4, 0),
        new Slot(Role.MID, -0.2, -0.4),
        new Slot(Role.MID, -0.2, 0.4),
        new Slot(Role.FWD, 0.2, -0.72),
        new Slot(Role.FWD, 0.3, 0),
        new Slot(Role.FWD, 0.2, 0.72),
    };

    public static readonly int[] SkinTones = { 0xf1c9a5, 0xe0ac7e, 0xc68a5c, 0x9a6440, 0x6e4529, 0x4b2e1c };
    public static readonly int[] HairColors = { 0x1b1410, 0x2e1f15, 0x4a3020, 0x7a5532, 0xc9a35e, 0x0e0e0e };

    public static Attributes MakeAttributes(Role role, Rng rng)
    {
        // (Drawn in the PWA's object-literal order: the rng stream depends on it.)
        var a = new Attributes();
        a.Pace = rng.Range(0.5, 0.85);
        a.Accel = rng.Range(0.5, 0.85);
        a.Control = rng.Range(0.55, 0.85);
        a.Passing = rng.Range(0.55, 0.85);
        a.Shooting = rng.Range(0.4, 0.7);
        a.Strength = rng.Range(0.5, 0.85);
        a.Defending = rng.Range(0.4, 0.7);
        a.Keeping = 0.2;
        // Neutral values (no extra rng draws, so seeded matches stay reproducible).
        a.Agility = 0.6;
        a.Stamina = 0.55;
        a.Jumping = role == Role.DEF || role == Role.GK ? 0.6 : 0.5;
        a.Power = role == Role.FWD ? 0.6 : 0.5;
        a.Height = 1.8;
        a.Weight = 76;
        if (role == Role.GK)
        {
            a.Keeping = rng.Range(0.7, 0.9);
            a.Pace = rng.Range(0.35, 0.55);
            a.Control = rng.Range(0.45, 0.65);
        }
        else if (role == Role.DEF)
        {
            a.Defending = rng.Range(0.7, 0.9);
            a.Strength = rng.Range(0.7, 0.9);
            a.Shooting = rng.Range(0.3, 0.55);
        }
        else if (role == Role.MID)
        {
            a.Passing = rng.Range(0.72, 0.92);
            a.Control = rng.Range(0.7, 0.9);
            a.Defending = rng.Range(0.5, 0.75);
        }
        else
        {
            a.Shooting = rng.Range(0.72, 0.92);
            a.Pace = rng.Range(0.7, 0.95);
            a.Accel = rng.Range(0.7, 0.95);
            a.Control = rng.Range(0.7, 0.9);
        }
        a.Agility = a.Accel * 0.9;
        return a;
    }
}

/// <summary>One player of a prepared line-up (club squads): who he is and where he plays.</summary>
public sealed class SetupPlayer
{
    public string Name = "";
    public int Number;
    public Attributes Attrs = new Attributes();
    public Look Look = new Look();
    public Role Role;
    /// <summary>Preferred foot: 1 right, -1 left.</summary>
    public int Foot = 1;
    /// <summary>Formation slot, team frame.</summary>
    public double X, Z;
}

public sealed class TeamSetup
{
    public TeamInfo Info = new TeamInfo();
    /// <summary>Eleven players in shirt-index order (0 keeper ... 9 striker).</summary>
    public List<SetupPlayer> Players = new List<SetupPlayer>();
    /// <summary>Shirt index of the captain (wears the armband); null = the striker.</summary>
    public int? Captain;
}

public sealed class MatchSetup
{
    public TeamSetup[] Teams = new TeamSetup[2];
}
