using System;
using System.Collections.Generic;
using GameNight.Sim;

namespace GameNight.Club;

/// <summary>A special way of playing that lifts a group of stats (85+ players only).</summary>
public sealed class Playstyle
{
    public string Id = "", Name = "", Code = "", About = "";
    /// <summary>Who can have it: outfield roles, or keepers only.</summary>
    public Role[] Roles = Array.Empty<Role>();
    public (Stat stat, int plus)[] Boost = Array.Empty<(Stat, int)>();
    /// <summary>Badge colour.</summary>
    public int Color;
}

/// <summary>
/// Playstyles: 20 of them. A card rated 85 or more has a 50% chance of none; otherwise 85-89 get
/// one, 90-94 one or two (even odds), 95+ one, two or three (even odds). The roll comes from the
/// card's id, so every card (old saves included) always has the same ones. The boosts are real:
/// <see cref="Cards.ToSim"/> plays the boosted stats.
/// </summary>
public static class Playstyles
{
    static readonly Role[] Out = { Role.DEF, Role.MID, Role.FWD };
    static readonly Role[] Keep = { Role.GK };

    static Playstyle P(string id, string name, string code, string about, Role[] roles, int color, params (Stat, int)[] boost) =>
        new() { Id = id, Name = name, Code = code, About = about, Roles = roles, Color = color, Boost = boost };

    public static readonly Playstyle[] All =
    {
        P("tank", "Tank", "TK", "Shrugs off challenges and holds his ground", Out, 0x9aa3b5, (Stat.Strength, 6), (Stat.Stamina, 3), (Stat.Jumping, 2)),
        P("rapid", "Rapid", "RP", "Top speed nobody can live with", new[] { Role.DEF, Role.MID, Role.FWD }, 0x4aa3ff, (Stat.Pace, 6), (Stat.Accel, 4)),
        P("quickstep", "Quick Step", "QS", "Explodes off the mark", Out, 0x5ef2ff, (Stat.Accel, 7), (Stat.Agility, 3)),
        P("engine", "Engine", "EN", "Runs all night, box to box", Out, 0x3ddc84, (Stat.Stamina, 8), (Stat.Pace, 2)),
        P("finesse", "Finesse Shot", "FS", "Curls it into the corner", new[] { Role.MID, Role.FWD }, 0xffd447, (Stat.Shooting, 6), (Stat.Dribbling, 2)),
        P("powershot", "Power Shot", "PS", "Hits the ball through the keeper", new[] { Role.MID, Role.FWD }, 0xf28c28, (Stat.Power, 7), (Stat.Shooting, 3)),
        P("maestro", "Maestro", "MA", "Sees passes nobody else sees", new[] { Role.DEF, Role.MID }, 0xb05cff, (Stat.Passing, 7), (Stat.Dribbling, 2)),
        P("technician", "Technician", "TC", "The ball sticks to his feet", new[] { Role.MID, Role.FWD }, 0xe0559b, (Stat.Dribbling, 6), (Stat.Agility, 4)),
        P("aerial", "Aerial", "AE", "Wins everything in the air", Out, 0x7ff6ff, (Stat.Jumping, 7), (Stat.Strength, 3)),
        P("intercept", "Intercept", "IN", "Reads the pass before it's played", new[] { Role.DEF, Role.MID }, 0x2fb6a8, (Stat.Defending, 6), (Stat.Agility, 3)),
        P("bruiser", "Bruiser", "BR", "Tackles that rattle the stands", new[] { Role.DEF, Role.MID }, 0xc8393b, (Stat.Defending, 4), (Stat.Strength, 5)),
        P("anticipate", "Anticipate", "AN", "Always a step ahead of the striker", new[] { Role.DEF }, 0x6a3fd1, (Stat.Defending, 5), (Stat.Pace, 3), (Stat.Accel, 2)),
        P("trickster", "Trickster", "TR", "Step-overs, flicks and nutmegs", new[] { Role.MID, Role.FWD }, 0xff7ae6, (Stat.Dribbling, 5), (Stat.Agility, 5)),
        P("poacher", "Poacher", "PO", "Always in the right place in the box", new[] { Role.FWD }, 0xffe066, (Stat.Shooting, 5), (Stat.Accel, 4)),
        P("longball", "Long Ball", "LB", "Switches play from one flank to the other", new[] { Role.DEF, Role.MID }, 0xe8c35a, (Stat.Passing, 5), (Stat.Power, 4)),
        P("relentless", "Relentless", "RL", "Hunts the ball for ninety minutes", Out, 0x8dff9e, (Stat.Stamina, 6), (Stat.Defending, 3)),
        P("whirlwind", "Whirlwind", "WW", "Twists and turns at full speed", new[] { Role.MID, Role.FWD }, 0x9be0b4, (Stat.Agility, 5), (Stat.Pace, 3), (Stat.Dribbling, 2)),
        P("cat", "Cat", "CT", "Reflexes that steal goals off the line", Keep, 0x5ef2ff, (Stat.Keeping, 5), (Stat.Agility, 5)),
        P("rushout", "Rush Out", "RO", "Sweeps behind the back line", Keep, 0x4aa3ff, (Stat.Accel, 6), (Stat.Pace, 3), (Stat.Keeping, 2)),
        P("farreach", "Far Reach", "FR", "Tips the top corner over the bar", Keep, 0xffd447, (Stat.Keeping, 4), (Stat.Jumping, 6)),
    };

    static readonly Dictionary<string, List<Playstyle>> Cache = new();

    /// <summary>This card's playstyles (empty for most).</summary>
    public static List<Playstyle> Of(Card c)
    {
        string key = c.Id + "|" + c.Position;
        if (Cache.TryGetValue(key, out var got)) return got;
        var list = Roll(c);
        if (Cache.Count > 4000) Cache.Clear();
        Cache[key] = list;
        return list;
    }

    static List<Playstyle> Roll(Card c)
    {
        var list = new List<Playstyle>();
        int ovr = c.Overall;
        if (ovr < 85) return list;
        // FNV-1a over the id: the same card always rolls the same.
        uint h = 2166136261;
        foreach (char ch in c.Id + "playstyle")
        {
            h ^= ch;
            h *= 16777619;
        }
        var rng = new Rng(h);
        if (rng.Next() < 0.5) return list;
        int n = ovr >= 95 ? 1 + Math.Min(2, (int)(rng.Next() * 3)) : ovr >= 90 ? (rng.Next() < 0.5 ? 1 : 2) : 1;
        var role = Cards.RoleOf(c.Position);
        var pool = new List<Playstyle>();
        foreach (var p in All)
            if (Array.IndexOf(p.Roles, role) >= 0) pool.Add(p);
        for (int i = 0; i < n && pool.Count > 0; i++)
        {
            int k = Math.Min(pool.Count - 1, (int)(rng.Next() * pool.Count));
            list.Add(pool[k]);
            pool.RemoveAt(k);
        }
        return list;
    }

    /// <summary>How much this card's playstyles add to a stat.</summary>
    public static int Plus(Card c, Stat s)
    {
        int plus = 0;
        foreach (var p in Of(c))
            foreach (var (k, v) in p.Boost)
                if (k == s) plus += v;
        return plus;
    }

    /// <summary>A stat with the playstyle boosts on (never above 99).</summary>
    public static int Boosted(Card c, Stat s) => Math.Min(99, c.Stats[s] + Plus(c, s));

    /// <summary>"+6 Strength, +3 Stamina".</summary>
    public static string Describe(Playstyle p) =>
        string.Join(", ", Array.ConvertAll(p.Boost, b => $"+{b.plus} {Cards.StatLabel[b.stat]}"));
}
