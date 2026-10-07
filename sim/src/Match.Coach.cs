using System;
using System.Collections.Generic;

namespace GameNight.Sim;

/// <summary>
/// An order from a side's manager in coach mode (online 1v1 where the computer plays and the two
/// friends manage): a change, calling one off, the mentality or the formation.
/// </summary>
public sealed class CoachOrder
{
    public const int Sub = 0, Cancel = 1, Mood = 2, Shape = 3;
    public int Kind;
    /// <summary>Sub / Cancel: the slot id of the man coming off; Sub: the shirt number coming on.</summary>
    public int Off, On;
    /// <summary>Mood: -2 (park the bus) .. 2 (all out attack).</summary>
    public int Value;
    /// <summary>Shape: the formation's id (for the board) and its eleven slots, x then z, in shirt order.</summary>
    public string Formation = "";
    public double[]? Slots;
}

/// <summary>What a manager sees of his side: who's on, who's on the bench, changes left, the
/// mentality and the formation. Plain data so it can travel to the friend's game.</summary>
public sealed class CoachView
{
    public sealed class Man
    {
        public int Id, Index, Number, Card;
        public string Name = "", Role = "";
        public float Stamina;
        public bool Off, SubbedOn;
        /// <summary>Waiting to come on for him (shirt number, 0 none) and his name.</summary>
        public int Coming;
        public string ComingName = "";
    }

    public sealed class Sub
    {
        public int Number;
        public string Name = "", Pos = "";
        public bool Warming;
    }

    public int Left, Mentality;
    public string Formation = "";
    public List<Man> Pitch = new();
    public List<Sub> Bench = new();
}

public sealed partial class Match
{
    /// <summary>Each side's mentality, -2..2 (0 = as the engine always played).</summary>
    public readonly int[] Mentality = new int[2];
    /// <summary>A person manages this side: the computer manager makes no changes for it.</summary>
    public readonly bool[] Managed = new bool[2];
    /// <summary>The formation each side's board shows (set by the game; the shape itself is in the players' slots).</summary>
    public readonly string[] FormationId = { "", "" };

    /// <summary>Carries out a manager's order (on the sim thread).</summary>
    public void Order(int team, CoachOrder o)
    {
        switch (o.Kind)
        {
            case CoachOrder.Sub:
            {
                var off = All.Find(p => p.Id == o.Off && p.Team == team);
                SetupPlayer? on;
                lock (SubGate) on = Bench[team].Find(b => b.Number == o.On);
                if (off != null && on != null) Substitute(off, on);
                break;
            }
            case CoachOrder.Cancel:
            {
                var off = All.Find(p => p.Id == o.Off && p.Team == team);
                if (off != null) CancelSub(off);
                break;
            }
            case CoachOrder.Mood:
                Mentality[team] = Math.Clamp(o.Value, -2, 2);
                break;
            case CoachOrder.Shape:
                if (o.Slots == null || o.Slots.Length < 22) break;
                foreach (var p in All)
                {
                    if (p.Team != team || p.Index < 0 || p.Index > 10) continue;
                    p.BaseX = o.Slots[p.Index];
                    p.BaseZ = o.Slots[11 + p.Index];
                }
                FormationId[team] = o.Formation;
                break;
        }
    }

    /// <summary>The side as its manager sees it (on the sim thread).</summary>
    public CoachView Coach(int team)
    {
        var v = new CoachView { Mentality = Mentality[team], Formation = FormationId[team] };
        var xi = new List<Player>();
        foreach (var p in All) if (p.Team == team) xi.Add(p);
        xi.Sort((a, b) => a.Index.CompareTo(b.Index));
        lock (SubGate)
        {
            v.Left = SubsLeft(team);
            foreach (var p in xi)
            {
                var coming = QueuedFor(p);
                v.Pitch.Add(new CoachView.Man
                {
                    Id = p.Id, Index = p.Index, Number = p.Number, Card = Cards[p.Id],
                    Name = p.Name, Role = p.Role switch { Role.GK => "GK", Role.DEF => "DEF", Role.MID => "MID", _ => "FWD" },
                    Stamina = (float)Math.Clamp(p.Stamina, 0, 1),
                    Off = Red[p.Id] || !Teams[team].Players.Contains(p), SubbedOn = SubbedOn[p.Id],
                    Coming = coming?.Number ?? 0, ComingName = coming?.Name ?? "",
                });
            }
            foreach (var b in Bench[team])
                v.Bench.Add(new CoachView.Sub { Number = b.Number, Name = b.Name, Pos = b.Pos, Warming = SubQueue.Exists(q => q.on == b) });
        }
        return v;
    }
}
