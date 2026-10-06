using System;
using System.Collections.Generic;

namespace GameNight.Sim;

/// <summary>
/// Red cards. The player shown red leaves the game at the next stoppage (at once when the foul
/// was whistled; after the attack when advantage was played) and walks off over the nearer
/// touchline. His side plays on a man short.
/// </summary>
public sealed partial class Match
{
    /// <summary>Shown red, still on the pitch (advantage is being played).</summary>
    readonly List<Player> sendOff = new List<Player>();
    /// <summary>Sent off and walking to the touchline: drawn, but out of the game.</summary>
    public readonly List<Player> Leaving = new List<Player>();
    /// <summary>Sent off this match, per player id.</summary>
    public readonly bool[] Red = new bool[22];

    void SendOffStep()
    {
        if (sendOff.Count > 0 && Phase != Phase.Play && Phase != Phase.Goal)
        {
            foreach (var p in sendOff) Dismiss(p);
            sendOff.Clear();
        }
        for (int i = Leaving.Count - 1; i >= 0; i--)
        {
            var p = Leaving[i];
            // Head down, the shortest way off, drifting toward halfway.
            double side = p.Pos.Z >= 0 ? 1 : -1;
            double tx = p.Pos.X * 0.6, tz = side * (Pitch.HalfW + 4);
            double dx = tx - p.Pos.X, dz = tz - p.Pos.Z, d = Math.Max(0.01, JsMath.Hypot(dx, dz));
            p.MoveX = dx / d;
            p.MoveZ = dz / d;
            p.WantSpeed = 1.6;
            p.LookAt = null;
            p.Move(DT);
            if (Math.Abs(p.Pos.Z) > Pitch.HalfW + 3.5) Leaving.RemoveAt(i);
        }
    }

    void Dismiss(Player p)
    {
        var team = Teams[p.Team];
        if (!team.Players.Remove(p)) return;
        Players.Remove(p);
        Red[p.Id] = true;
        Roster++;
        p.Plan = null;
        p.Sprinting = false;
        if (p.Action != ActionKind.Fall) p.Action = ActionKind.None;
        Leaving.Add(p);

        if (Owner == p) Owner = null;
        if (HeldBy == p) HeldBy = null;
        if (PassTarget == p) PassTarget = null;
        if (LastTouch == p) LastTouch = null;
        if (LastKicker == p) LastKicker = null;
        if (ShotBy == p) ShotBy = null;
        offsideFlagged?.Remove(p);
        if (invaderTaker == p) invaderTaker = null;
        var sp = SetPiece;
        if (sp != null)
        {
            if (sp.Wall != null)
            {
                int w = sp.Wall.Players.IndexOf(p);
                if (w >= 0)
                {
                    sp.Wall.Players.RemoveAt(w);
                    sp.Wall.Slots.RemoveAt(w);
                }
            }
            if (sp.Taker == p) sp.Taker = NearestMate(p, sp.X, sp.Z);
        }
        if (Seats[p.Team].Controlled == p) SetControlled(NearestMate(p, Ball.Pos.X, Ball.Pos.Z));
        AI.Forget(p);
        Log?.Invoke($"{F1(Time)} SENT OFF T{p.Team} #{p.Index}");
    }

    /// <summary>The outfield team-mate nearest (x, z) (the keeper if there's nobody else).</summary>
    Player NearestMate(Player p, double x, double z)
    {
        var ps = Teams[p.Team].Players;
        Player best = ps[0];
        double bd = 1e9;
        foreach (var q in ps)
        {
            if (q.Role == Role.GK) continue;
            double d = M.Dist2D(q.Pos.X, q.Pos.Z, x, z);
            if (d < bd)
            {
                bd = d;
                best = q;
            }
        }
        return best;
    }
}
