using GameNight.Sim;
using Godot;

namespace GameNight.UI;

/// <summary>
/// What the HUD shows that never changes during a match: team names and kit colours, and each
/// player's shirt name. Read from the Match once, before its thread starts.
/// </summary>
public sealed class MatchInfo
{
    public readonly string[] Name = new string[2], Short = new string[2];
    public readonly Color[] Shirt = new Color[2], GkShirt = new Color[2];
    public readonly int[] ShirtRgb = new int[2], GkShirtRgb = new int[2];
    /// <summary>By player id: the name on the caption (surname, or #n).</summary>
    public readonly string[] Surname = new string[MatchSnapshot.N];

    public MatchInfo(Match m)
    {
        for (int t = 0; t < 2; t++)
        {
            var info = m.Teams[t].Info;
            Name[t] = info.Name;
            Short[t] = info.Short;
            ShirtRgb[t] = info.Kit.Shirt;
            GkShirtRgb[t] = info.Kit.GkShirt;
            Shirt[t] = Style.Hex(info.Kit.Shirt);
            GkShirt[t] = Style.Hex(info.Kit.GkShirt);
        }
        foreach (var p in m.All)
            if (p.Id >= 0 && p.Id < MatchSnapshot.N) Surname[p.Id] = Who(p);
    }

    /// <summary>The PWA's caption name: the last word of the shirt name, or #index.</summary>
    public static string Who(Player p)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) return "#" + (p.Index + 1);
        var parts = p.Name.Trim().Split(' ');
        return parts[^1];
    }
}
