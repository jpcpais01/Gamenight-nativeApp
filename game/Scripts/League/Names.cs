using System;
using System.Collections.Generic;
using System.Linq;
using GameNight.Club;

namespace GameNight.League;

/// <summary>
/// Made-up football: towns, club names in English and continental styles, nicknames that follow
/// the crest's emblem, grounds, managers, the league and its newspaper.
/// </summary>
public static class Names
{
    static readonly string[] TownA =
    {
        "Ash", "Black", "Brook", "Castle", "Cole", "Dun", "East", "Elm", "Fair", "Glen", "Green", "Hart", "High", "King", "Lang",
        "Marl", "Mill", "North", "Oak", "Red", "Rose", "Salt", "Stone", "Sun", "Thorn", "West", "Whit", "Wolver", "Har", "Bram",
        "Crow", "Ever", "Fox", "Gold", "Iron", "Lark", "Moss", "Raven", "Silver", "Wex",
    };
    static readonly string[] TownB =
    {
        "ford", "ton", "bury", "field", "wick", "mouth", "dale", "ham", "port", "stead", "bridge", "worth", "gate", "haven",
        "moor", "ley", "burgh", "chester", "combe", "pool",
    };
    static readonly string[] LatinA = { "Ver", "Al", "Mon", "San", "Cas", "Por", "Bel", "Cor", "Mar", "Val", "Ter", "Sol", "Lu", "Ri", "Bra", "Sil", "Ca", "Na", "Tor", "Fio", "Le", "Ost" };
    static readonly string[] LatinB = { "ona", "ago", "enza", "ira", "elo", "ano", "ante", "ella", "ia", "ena", "ova", "ino", "ares", "ega", "ida", "orra", "eto", "ara" };

    static readonly string[] English = { "United", "City", "Athletic", "Rovers", "Wanderers", "Town", "Albion", "County", "Harriers", "Orient", "Rangers", "Villa", "Argyle", "Forest", "Alexandra" };
    static readonly string[] Continental = { "Real", "Sporting", "Atlético", "Dynamo", "Inter", "Olympique", "Racing", "Union", "Lokomotiv", "Académica", "Deportivo", "Vitória", "Stella", "Fortuna" };

    static readonly Dictionary<int, string> ByEmblem = new()
    {
        [1] = "The Stars", [3] = "The Royals", [4] = "The Lightning", [5] = "The Castle", [6] = "The Sailors", [7] = "The Flames",
        [8] = "The Flyers", [9] = "The Eagles", [10] = "The Lions", [11] = "The Oaks", [12] = "The Wolves", [13] = "The Sunshine",
        [15] = "The Hearts", [16] = "The Moonrakers", [17] = "The Highlanders", [18] = "The Tritons", [19] = "The Keymen",
        [20] = "The Lilies", [21] = "The Cup Kings", [22] = "The Shamrocks", [24] = "The Mariners", [25] = "The Wheelers",
        [26] = "The Bulls", [27] = "The Stallions", [28] = "The Swallows", [29] = "The Pirates", [30] = "The Jewels",
        [31] = "The Laurels", [32] = "The Bees",
    };
    static readonly string[] Nicknames = { "The Blues", "The Reds", "The Saints", "The Magpies", "The Hornets", "The Foxes", "The Owls", "The Robins", "The Hatters", "The Cobblers", "The Millers", "The Quakers" };

    static readonly string[] GroundStyle = { "{T} Road", "{S} Park", "The {T} Ground", "Estádio {T}", "Stadio {S}", "{T} Arena", "Victoria Park", "{S} Lane", "The Old {T} Field", "{T} Bowl" };

    public static readonly string[] Leagues =
    {
        "Floodlight League", "Neon Premier", "Midnight Division", "Terrace League", "Night Owl League", "Golden Boot League", "Starlight Premier",
    };
    public static readonly string[] Papers =
    {
        "THE EVENING WHISTLE", "DAILY TERRACE", "THE FLOODLIGHT POST", "THE MATCHDAY TIMES", "SPORTING GAZETTE", "THE FINAL WHISTLE",
    };

    static T Pick<T>(Random r, IReadOnlyList<T> a) => a[r.Next(a.Count)];

    public static string Town(Random r) =>
        r.Next(3) == 0 ? Pick(r, LatinA) + Pick(r, LatinB) : Pick(r, TownA) + Pick(r, TownB);

    /// <summary>A club name in one of a few styles, and the town it's from.</summary>
    public static (string name, string town, bool continental) Club(Random r, HashSet<string> taken)
    {
        for (int tries = 0; ; tries++)
        {
            bool latin = r.Next(5) < 2;
            string town = latin ? Pick(r, LatinA) + Pick(r, LatinB) : Pick(r, TownA) + Pick(r, TownB);
            string name = r.Next(7) switch
            {
                0 when !latin => $"{town} FC",
                1 when latin => $"AC {town}",
                2 => $"{town} {1880 + r.Next(45)}",
                _ => latin ? $"{Pick(r, Continental)} {town}" : $"{town} {Pick(r, English)}",
            };
            if (name.Length > 20 && tries < 20) continue;
            if (taken.Add(town) || tries > 40) return (name, town, latin);
        }
    }

    public static string Short(string town, HashSet<string> taken)
    {
        string t = new string(town.ToUpperInvariant().Where(char.IsLetter).ToArray());
        if (t.Length < 3) t = (t + "XXX")[..3];
        foreach (var s in new[] { t[..3], $"{t[0]}{t[1]}{t[^1]}", $"{t[0]}{t[2]}{t[^1]}", $"{t[0]}{t[^2]}{t[^1]}" })
            if (taken.Add(s)) return s;
        return t[..3];
    }

    public static string Nickname(Random r, int emblem) =>
        ByEmblem.TryGetValue(emblem, out var n) && r.Next(5) > 0 ? n : Pick(r, Nicknames);

    public static string Person(Random r)
    {
        var n = Pick(r, Cards.Nations);
        return $"{Pick(r, n.First)} {Pick(r, n.Last)}";
    }

    public static string Ground(Random r, string town, bool latin)
    {
        var n = Pick(r, Cards.Nations);
        string g = latin ? (r.Next(2) == 0 ? "Estádio {T}" : "Stadio {S}") : Pick(r, GroundStyle);
        return g.Replace("{T}", town).Replace("{S}", Pick(r, n.Last));
    }
}
