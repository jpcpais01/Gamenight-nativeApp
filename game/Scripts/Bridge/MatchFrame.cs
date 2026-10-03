using System;

namespace GameNight.Bridge;

public enum MatchPhase : byte { Kickoff, Play, Out, SetPiece, Goal, HalfTime, FullTime }

/// <summary>
/// Everything the renderer and HUD read about the match at one instant, as flat arrays.
/// The match writes one after each fixed step; the renderer keeps the previous one and
/// interpolates between the two, so the sim can later run on its own thread and simply
/// hand frames over.
///
/// World units are metres. X runs along the pitch (+X is the right-hand goal), Z across it
/// (+Z is the near touchline, toward the camera), Y is up: the same as the PWA.
/// </summary>
public sealed class MatchFrame
{
    public const int MaxPlayers = 22;

    public int Count;
    public readonly float[] X = new float[MaxPlayers];
    public readonly float[] Y = new float[MaxPlayers];
    public readonly float[] Z = new float[MaxPlayers];
    /// <summary>Direction the body faces, radians: atan2(z, x) of the facing vector.</summary>
    public readonly float[] Facing = new float[MaxPlayers];
    /// <summary>Ground speed, m/s (drives the run cycle).</summary>
    public readonly float[] Speed = new float[MaxPlayers];
    /// <summary>0 = home, 1 = away.</summary>
    public readonly byte[] Team = new byte[MaxPlayers];
    public readonly bool[] Keeper = new bool[MaxPlayers];
    /// <summary>Standing height in metres (the body is scaled to it).</summary>
    public readonly float[] Height = new float[MaxPlayers];
    /// <summary>Skin tone index (the renderer has a small table).</summary>
    public readonly byte[] Skin = new byte[MaxPlayers];

    public float BallX, BallY, BallZ;
    public float BallVX, BallVY, BallVZ;

    /// <summary>Index of the player the human controls, -1 for none.</summary>
    public int Controlled = -1;
    /// <summary>Team in possession (or last in possession), -1 for none.</summary>
    public int Attacking = -1;
    /// <summary>Which way each team attacks: +1 toward +X, -1 toward -X.</summary>
    public readonly int[] Dir = { 1, -1 };
    public readonly int[] Score = new int[2];
    public MatchPhase Phase;
    /// <summary>Match clock in game minutes (0..90).</summary>
    public float Minute;

    public void CopyFrom(MatchFrame o)
    {
        Count = o.Count;
        Array.Copy(o.X, X, Count);
        Array.Copy(o.Y, Y, Count);
        Array.Copy(o.Z, Z, Count);
        Array.Copy(o.Facing, Facing, Count);
        Array.Copy(o.Speed, Speed, Count);
        Array.Copy(o.Team, Team, Count);
        Array.Copy(o.Keeper, Keeper, Count);
        Array.Copy(o.Height, Height, Count);
        Array.Copy(o.Skin, Skin, Count);
        BallX = o.BallX; BallY = o.BallY; BallZ = o.BallZ;
        BallVX = o.BallVX; BallVY = o.BallVY; BallVZ = o.BallVZ;
        Controlled = o.Controlled;
        Attacking = o.Attacking;
        Dir[0] = o.Dir[0]; Dir[1] = o.Dir[1];
        Score[0] = o.Score[0]; Score[1] = o.Score[1];
        Phase = o.Phase;
        Minute = o.Minute;
    }
}
