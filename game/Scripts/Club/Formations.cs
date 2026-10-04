using System;

namespace GameNight.Club;

/// <summary>
/// A formation slot in team frame: x from -1 (own goal line) to +1 (opponent goal line),
/// z from -1 (left touchline, facing the attack) to +1 (right).
/// </summary>
public sealed class FSlot
{
    public Position Pos;
    public double X, Z;

    public FSlot() { }
    public FSlot(Position pos, double x, double z)
    {
        Pos = pos;
        X = x;
        Z = z;
    }
}

public sealed class Formation
{
    public string Id = "", Name = "";
    public FSlot[] Slots = Array.Empty<FSlot>();
}

/// <summary>
/// The PWA's formations (src/meta/formations.ts). The slot ORDER matters: the match engine
/// gives each shirt index a job (0 keeper, 1 / 4 full-backs, 2 / 3 centre-backs, 5 holding
/// mid, 6 / 7 midfielders, 8 / 10 wide forwards, 9 the striker who kicks off). Every formation
/// keeps that order.
/// </summary>
public static class Formations
{
    static FSlot S(Position p, double x, double z) => new(p, x, z);

    public static readonly Formation[] All =
    {
        new Formation
        {
            Id = "433", Name = "4-3-3", Slots = new[]
            {
                S(Position.GK, -0.96, 0), S(Position.LB, -0.62, -0.7), S(Position.CB, -0.7, -0.24), S(Position.CB, -0.7, 0.24),
                S(Position.RB, -0.62, 0.7), S(Position.CDM, -0.4, 0), S(Position.CM, -0.2, -0.4), S(Position.CM, -0.2, 0.4),
                S(Position.LW, 0.2, -0.72), S(Position.ST, 0.3, 0), S(Position.RW, 0.2, 0.72),
            },
        },
        new Formation
        {
            Id = "442", Name = "4-4-2", Slots = new[]
            {
                S(Position.GK, -0.96, 0), S(Position.LB, -0.64, -0.7), S(Position.CB, -0.7, -0.24), S(Position.CB, -0.7, 0.24),
                S(Position.RB, -0.64, 0.7), S(Position.CM, -0.32, -0.2), S(Position.LM, -0.22, -0.72), S(Position.CM, -0.32, 0.2),
                S(Position.RM, -0.22, 0.72), S(Position.ST, 0.28, -0.16), S(Position.ST, 0.22, 0.18),
            },
        },
        new Formation
        {
            Id = "4231", Name = "4-2-3-1", Slots = new[]
            {
                S(Position.GK, -0.96, 0), S(Position.LB, -0.62, -0.7), S(Position.CB, -0.7, -0.24), S(Position.CB, -0.7, 0.24),
                S(Position.RB, -0.62, 0.7), S(Position.CDM, -0.44, -0.18), S(Position.CDM, -0.44, 0.18), S(Position.CAM, -0.08, 0),
                S(Position.LM, 0.0, -0.68), S(Position.ST, 0.3, 0), S(Position.RM, 0.0, 0.68),
            },
        },
        new Formation
        {
            Id = "352", Name = "3-5-2", Slots = new[]
            {
                S(Position.GK, -0.96, 0), S(Position.LM, -0.36, -0.78), S(Position.CB, -0.7, -0.36), S(Position.CB, -0.7, 0.36),
                S(Position.RM, -0.36, 0.78), S(Position.CB, -0.74, 0), S(Position.CM, -0.3, -0.3), S(Position.CM, -0.3, 0.3),
                S(Position.CAM, -0.04, 0), S(Position.ST, 0.28, -0.18), S(Position.ST, 0.24, 0.2),
            },
        },
        new Formation
        {
            Id = "532", Name = "5-3-2", Slots = new[]
            {
                S(Position.GK, -0.96, 0), S(Position.LB, -0.56, -0.78), S(Position.CB, -0.72, -0.34), S(Position.CB, -0.72, 0.34),
                S(Position.RB, -0.56, 0.78), S(Position.CB, -0.75, 0), S(Position.CM, -0.32, -0.36), S(Position.CM, -0.36, 0.0),
                S(Position.CM, -0.32, 0.36), S(Position.ST, 0.26, -0.18), S(Position.ST, 0.22, 0.2),
            },
        },
        new Formation
        {
            Id = "4141", Name = "4-1-4-1", Slots = new[]
            {
                S(Position.GK, -0.96, 0), S(Position.LB, -0.62, -0.7), S(Position.CB, -0.7, -0.24), S(Position.CB, -0.7, 0.24),
                S(Position.RB, -0.62, 0.7), S(Position.CDM, -0.46, 0), S(Position.CM, -0.2, -0.24), S(Position.CM, -0.2, 0.24),
                S(Position.LM, -0.12, -0.72), S(Position.ST, 0.3, 0), S(Position.RM, -0.12, 0.72),
            },
        },
    };

    public static Formation ById(string id) => Array.Find(All, f => f.Id == id) ?? All[0];

    /// <summary>Limits for dragging slots around on the tactics board.</summary>
    public const double MinX = -0.82, MaxX = 0.4, MinZ = -0.88, MaxZ = 0.88;

    /// <summary>Position label for a spot on the board (team frame).</summary>
    public static Position Zone(double x, double z)
    {
        bool left = z < 0;
        double wide = Math.Abs(z);
        if (x < -0.52) return wide > 0.5 ? (left ? Position.LB : Position.RB) : Position.CB;
        if (x < -0.3) return wide > 0.55 ? (left ? Position.LM : Position.RM) : Position.CDM;
        if (x < -0.1) return wide > 0.55 ? (left ? Position.LM : Position.RM) : Position.CM;
        if (x < 0.1) return wide > 0.5 ? (left ? Position.LM : Position.RM) : Position.CAM;
        return wide > 0.42 ? (left ? Position.LW : Position.RW) : Position.ST;
    }
}
