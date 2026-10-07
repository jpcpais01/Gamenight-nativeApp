using System;
using System.Collections.Generic;
using GameNight.Sim;

namespace GameNight.Club;

/// <summary>
/// Skill moves, 1 to 5 stars, on every card. A better player is more likely to have more, and a
/// dribbler more than his overall says (a centre-back fewer): about 75 rated is mostly 2 stars
/// (the one-star moves), about 84 mostly 3-4 (the two-star moves too), 92 and up mostly 5 (all
/// ten). Trickster adds a star. Rolled from the card's id like playstyles, so every card, old
/// saves included, always has the same.
/// </summary>
public static class SkillStars
{
    static readonly Dictionary<string, int> Cache = new();

    public static int Of(Card c)
    {
        string key = c.Id + "|" + c.Position;
        if (Cache.TryGetValue(key, out int got)) return got;
        int stars = Roll(c);
        if (Cache.Count > 4000) Cache.Clear();
        Cache[key] = stars;
        return stars;
    }

    /// <summary>His signature skill (double tap SPRINT): a 4-star man one of the 4-star three, a
    /// 5-star man one of the 5-star three, rolled from the card's id; none below 4 stars.</summary>
    public static SkillMove Signature(Card c)
    {
        int stars = Of(c);
        if (stars < 4) return SkillMove.None;
        uint h = 2166136261;
        foreach (char ch in c.Id + "signature")
        {
            h ^= ch;
            h *= 16777619;
        }
        return Skills.SignatureFor(stars, h);
    }

    static int Roll(Card c)
    {
        if (c.Position == Position.GK) return 1;
        uint h = 2166136261;
        foreach (char ch in c.Id + "skill")
        {
            h ^= ch;
            h *= 16777619;
        }
        double u = new Rng(h).Next();
        int ovr = c.Overall;
        double mean = 1 + (ovr - 68) / 6.0 + (c.Stats[Stat.Dribbling] - ovr) / 12.0;
        int stars = (int)Math.Round(mean + (u - 0.5) * 1.6);
        foreach (var ps in Playstyles.Of(c))
            if (ps.Id == "trickster") stars++;
        return Math.Clamp(stars, 1, 5);
    }
}
