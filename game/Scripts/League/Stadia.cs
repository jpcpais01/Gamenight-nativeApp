using System;
using System.Linq;
using GameNight.Grounds.Build;

namespace GameNight.League;

/// <summary>
/// Every league club's own ground, drawn up once from the club itself, so it's the same stadium
/// on every visit: one stand set or two, laid out mirrored end to end like a designed ground,
/// mostly in the club's colours, in its own surroundings. Bigger clubs get the taller sets.
/// </summary>
public static class Stadia
{
    /// <summary>A stable hash (string.GetHashCode changes from run to run).</summary>
    public static int Hash(string s)
    {
        uint h = 2166136261;
        foreach (char ch in s) h = (h ^ ch) * 16777619;
        return (int)(h & 0x7fffffff);
    }

    public static StadiumPlan Make(LClub c, double level)
    {
        var r = new Random(Hash(c.Name + "|" + c.Town + "|" + c.Founded));
        int n = Kit.Sets.Length;
        // Sets from the lowest to the tallest; a club's level picks where on that scale it builds.
        var bySize = Enumerable.Range(0, n).OrderBy(i => Kit.Sets[i].Natural(Kind.Side)).ToArray();
        double p = Math.Clamp((level - 50) / 36, 0, 1);
        int Around(double at) => bySize[Math.Clamp((int)Math.Round(at * (n - 1) + (r.NextDouble() - 0.5) * n * 0.35), 0, n - 1)];
        int a = Around(p), b = Around(p);
        if (b == a) b = bySize[(Array.IndexOf(bySize, a) + 1 + r.Next(2)) % n];

        var plan = new StadiumPlan();
        var second = r.Next(4) switch
        {
            0 => Array.Empty<Slot>(),
            1 => new[] { Slot.Home, Slot.Away, Slot.HomeFar, Slot.AwayFar, Slot.HomeNear, Slot.AwayNear },
            2 => new[] { Slot.Home, Slot.Away },
            _ => new[] { Slot.Main },
        };
        foreach (Slot s in Enum.GetValues<Slot>()) plan.Set(s, Array.IndexOf(second, s) >= 0 ? b : a);
        uint Any() => Kit.Paints[2 + r.Next(Kit.Paints.Length - 2)];
        uint first = r.NextDouble() < 0.7 ? Kit.ClubPaint : Any();
        uint other = second.Length == 0 || r.NextDouble() < 0.5 ? first
            : first == Kit.ClubPaint ? new[] { 0xeceae4u, 0x2a2c33u, 0x8c8f95u }[r.Next(3)] : Kit.ClubPaint;
        plan.SetPaint(a, first);
        if (b != a) plan.SetPaint(b, other);
        plan.Area = r.Next(Surroundings.Names.Length);
        return plan;
    }

    /// <summary>The club's own goal explosion, the same every time they score at home.</summary>
    public static int GoalFxOf(LClub c) => Hash(c.Name + "fx") % GameNight.Grounds.GoalFx.Count;
}
