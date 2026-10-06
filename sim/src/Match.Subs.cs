using System;
using System.Collections.Generic;

namespace GameNight.Sim;

/// <summary>A change made: who went off and who came on, in shirt slot `Id`.</summary>
public sealed class Substitution
{
    public int Team, Id, Minute;
    public string Off = "", On = "";
    public int OffNumber, OnNumber;
}

/// <summary>
/// Substitutions. Each side has its bench (seven, from the line-up) and five changes. A change
/// asked for during play waits for the next stoppage; then the man coming on takes the slot of
/// the man going off: his place in the shape, his job, his set piece if he was taking it. Only
/// who is in it changes (name, number, body, attributes, fresh legs, a clean card). The computer
/// manages its own side: from the hour mark, at stoppages, it takes off whoever has run himself
/// into the ground (or is walking a tightrope on a yellow), like for like, a forward on when
/// chasing the game late and a defender to see out a narrow lead.
/// </summary>
public sealed partial class Match
{
    public const int MaxSubs = 5;
    /// <summary>Each side's bench: the players still available to come on.</summary>
    public readonly List<SetupPlayer>[] Bench = { new List<SetupPlayer>(), new List<SetupPlayer>() };
    /// <summary>Changes made, in order.</summary>
    public readonly List<Substitution> Subs = new List<Substitution>();
    /// <summary>Changes asked for, waiting for the ball to go dead.</summary>
    public readonly List<(Player off, SetupPlayer on)> SubQueue = new List<(Player, SetupPlayer)>();
    /// <summary>Taken by the pause menu (another thread) to read or change the benches and the queue.</summary>
    public readonly object SubGate = new object();
    /// <summary>Came off the bench this match, per player slot id (the computer never takes them off again).</summary>
    public readonly bool[] SubbedOn = new bool[22];
    /// <summary>Seconds each slot has spent sprinting since its man came on.</summary>
    readonly double[] worked = new double[22];
    /// <summary>When (match minute) each side's manager next looks at his bench, and how many changes he still plans to make.</summary>
    readonly double[] subLook = new double[2];
    Rng subRng = null!;
    bool wasDead;

    /// <summary>Fills the benches (the setup's, or seven made up when there's none).</summary>
    void MakeBenches(double seed, MatchSetup? setup)
    {
        subRng = new Rng(M.ToInt32(seed) ^ 0x5b5b5b);
        for (int t = 0; t < 2; t++)
        {
            subLook[t] = 56 + subRng.Next() * 10;
            var given = setup?.Teams[t]?.Bench;
            if (given != null)
            {
                foreach (var sp in given) Bench[t].Add(sp);
                continue;
            }
            if (setup?.Teams[t] != null) continue;
            Role[] roles = { Role.GK, Role.DEF, Role.DEF, Role.MID, Role.MID, Role.FWD, Role.FWD };
            for (int i = 0; i < roles.Length; i++)
            {
                var r = roles[i];
                var look = new Look
                {
                    Skin = TeamData.SkinTones[(int)Math.Floor(subRng.Next() * TeamData.SkinTones.Length)],
                    Hair = TeamData.HairColors[(int)Math.Floor(subRng.Next() * TeamData.HairColors.Length)],
                    HairStyle = (int)Math.Floor(subRng.Next() * 4),
                    Height = subRng.Range(0.95, 1.06) * (r == Role.GK ? 1.04 : 1),
                    Build = subRng.Range(0.92, 1.1),
                };
                var a = TeamData.MakeAttributes(r, subRng);
                a.Height = 1.8 * look.Height;
                a.Weight = 76 * look.Build * look.Height * look.Height;
                Bench[t].Add(new SetupPlayer { Number = 12 + i, Role = r, Attrs = a, Look = look, Pos = r.ToString() });
            }
        }
    }

    /// <summary>Changes left for a side (made and waiting both count).</summary>
    public int SubsLeft(int team)
    {
        int n = MaxSubs;
        foreach (var s in Subs) if (s.Team == team) n--;
        foreach (var q in SubQueue) if (q.off.Team == team) n--;
        return n;
    }

    /// <summary>Who is waiting to replace `p`, if anyone.</summary>
    public SetupPlayer? QueuedFor(Player p)
    {
        foreach (var q in SubQueue) if (q.off == p) return q.on;
        return null;
    }

    /// <summary>Ask for a change: `on` (from the bench) for `off` (on the pitch) at the next
    /// stoppage. A second ask for the same man replaces the first. Thread-safe.</summary>
    public bool Substitute(Player off, SetupPlayer on)
    {
        lock (SubGate)
        {
            int t = off.Team;
            if (Phase == Phase.Fulltime || !Teams[t].Players.Contains(off) || !Bench[t].Contains(on)) return false;
            int was = SubQueue.FindIndex(q => q.off == off);
            if (was >= 0) SubQueue.RemoveAt(was);
            else if (SubsLeft(t) <= 0) return false;
            SubQueue.RemoveAll(q => q.on == on);
            SubQueue.Add((off, on));
            return true;
        }
    }

    /// <summary>Call off a change still waiting. Thread-safe.</summary>
    public void CancelSub(Player off)
    {
        lock (SubGate) SubQueue.RemoveAll(q => q.off == off);
    }

    void SubStep()
    {
        if (Training) return;
        foreach (var p in Players)
            if (p.Sprinting) worked[p.Id] += DT;
        bool dead = Phase != Phase.Play && Phase != Phase.Goal && Phase != Phase.Fulltime;
        if (dead && !wasDead)
            for (int t = 0; t < 2; t++)
                if (t != HumanTeam || AutoPlay) ManagerLooks(t);
        wasDead = dead;
        if (!dead || SubQueue.Count == 0) return;
        lock (SubGate)
        {
            foreach (var (off, on) in SubQueue)
                if (Teams[off.Team].Players.Contains(off) && Bench[off.Team].Remove(on)) Swap(off, on);
            SubQueue.Clear();
        }
    }

    void Swap(Player p, SetupPlayer on)
    {
        var s = new Substitution { Team = p.Team, Id = p.Id, Minute = Math.Max(1, DisplayMinute), Off = p.Name, OffNumber = p.Number, On = on.Name, OnNumber = on.Number };
        p.Name = on.Name;
        p.Number = on.Number;
        p.Foot = on.Foot == -1 ? -1 : 1;
        p.Attrs = on.Attrs.Clone();
        p.Look = on.Look;
        p.Stamina = 1;
        p.Sprinting = false;
        Cards[p.Id] = 0;
        worked[p.Id] = 0;
        SubbedOn[p.Id] = true;
        AI.Refresh(p);
        Subs.Add(s);
        Events.Sub = 1;
        Log?.Invoke($"{F1(Time)} SUB T{p.Team} #{p.Index} {s.Off} -> {s.On}");
    }

    /// <summary>The computer's manager at a stoppage: from the hour, a change or two every
    /// ten minutes or so while he has any left.</summary>
    void ManagerLooks(int t)
    {
        if (Half < 2 || DisplayMinute < subLook[t] || Bench[t].Count == 0) return;
        int left;
        lock (SubGate) left = SubsLeft(t);
        if (left <= 0) return;
        subLook[t] = DisplayMinute + 7 + subRng.Next() * 8;
        int lead = Teams[t].Score - Teams[1 - t].Score;
        int n = Math.Min(left, DisplayMinute < 70 && subRng.Next() < 0.6 ? 2 : 1);
        for (int k = 0; k < n; k++)
        {
            // Off: the most run-down outfielder (a booked man counts as half spent).
            Player? off = null;
            double worst = 0;
            foreach (var p in Teams[t].Players)
            {
                if (p.Role == Role.GK || SubbedOn[p.Id] || QueuedFor(p) != null) continue;
                double tired = worked[p.Id] * (1.35 - p.Attrs.Stamina) + (Cards[p.Id] > 0 ? 25 : 0) + subRng.Next() * 6;
                if (tired > worst)
                {
                    worst = tired;
                    off = p;
                }
            }
            if (off == null) return;
            // On: like for like, or the shape the score asks for late on.
            var want = off.Role;
            if (DisplayMinute >= 70 && lead < 0 && off.Role != Role.FWD) want = off.Role == Role.DEF ? Role.MID : Role.FWD;
            else if (DisplayMinute >= 78 && lead == 1 && off.Role == Role.FWD) want = Role.MID;
            else if (DisplayMinute >= 78 && lead == 1 && off.Role == Role.MID) want = Role.DEF;
            SetupPlayer? on = null;
            double best = -1;
            foreach (var b in Bench[t])
            {
                if (b.Role == Role.GK || SubQueue.Exists(q => q.on == b)) continue;
                double fit = (b.Role == want ? 10 : b.Role == off.Role ? 5 : 0) + b.Attrs.Pace + b.Attrs.Passing + b.Attrs.Shooting + b.Attrs.Defending;
                if (fit > best)
                {
                    best = fit;
                    on = b;
                }
            }
            if (on == null) return;
            Substitute(off, on);
        }
    }
}
