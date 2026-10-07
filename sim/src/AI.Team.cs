using System;

namespace GameNight.Sim;

/// <summary>
/// Team play: the moves a side makes together, on top of each player's own reading of the game.
/// Pass and move (one-twos played first time), overlaps and underlaps down the flanks, a striker
/// coming short while his partner spins in behind, counter-attacks when the ball's won with the
/// other side stretched, first-time lay-offs under pressure, pressing traps on a man who receives
/// facing his own goal, the back line squeezing up when the ball goes backwards, and a mood that
/// follows the score and the clock (chasing late, or seeing it out). Every move is a chance, not a
/// rule, weighed by the players' own character, so no two matches run alike.
/// </summary>
public sealed partial class AI
{
    /// <summary>Counter-attack on until (per team).</summary>
    readonly double[] counterUntil = { -1, -1 };
    /// <summary>Where the counter started (team frame x of the ball).</summary>
    readonly double[] counterFrom = { 0, 0 };
    /// <summary>Pressing trap: the second man goes in too, markers get tight.</summary>
    readonly double[] trapUntil = { -1, -1 };
    /// <summary>The back line squeezes up (the ball went backwards).</summary>
    readonly double[] stepUntil = { -1, -1 };
    /// <summary>Pass and move: who's gone for the return, and who he gave it to.</summary>
    readonly Player?[] wallRunner = new Player?[2];
    readonly Player?[] wallMate = new Player?[2];
    readonly double[] wallUntil = { -1, -1 };
    /// <summary>Overlap / underlap / spin in behind made for this team's move: the runner the carrier should look for.</summary>
    readonly Player?[] playRunner = new Player?[2];
    readonly double[] playUntil = { -1, -1 };
    readonly Player?[] overlapCarrier = new Player?[2];
    /// <summary>Per player: no new move before this (nobody makes the same run every ten seconds).</summary>
    readonly double[] playCool = new double[22];
    readonly double[] teamLook = { 0, 0 };
    /// <summary>Who last passed to each player, and when (no endless square balls between the same two).</summary>
    readonly Player?[] passFrom = new Player?[22];
    readonly double[] passFromT = new double[22];
    double kickSeen = -10;
    double oneTouchKick = -10;
    Player? oneTouchFor;
    Player? ownerSeen;
    int possSeen = -1;
    /// <summary>How often each move was made this match (for tuning): one-twos, first-time balls, overlaps, spins, counters, traps.</summary>
    public readonly int[] Moves = new int[6];

    bool Free(Player q) => q.Role != Role.GK && (!m.Piloted(q) || m.AutoPlay) && !q.IsBusy && run[q.Id].Until <= m.Time && m.Time >= playCool[q.Id];

    /// <summary>The team's mood from the score and the clock: chasing (0..1) when behind late, protecting (0..1) when ahead late.</summary>
    public void Mood(int team, out double chase, out double protect)
    {
        chase = protect = 0;
        if (m.Training) return;
        double minute = (m.Half - 1) * 45 + (m.Clock / MatchK.HalfSeconds) * 45;
        int diff = m.Teams[team].Score - m.Teams[1 - team].Score;
        if (diff < 0) chase = M.Smoothstep(50, 85, minute) * (diff <= -2 ? 1 : 0.8) + (diff <= -2 ? 0.15 : 0);
        else if (diff > 0) protect = M.Smoothstep(60, 88, minute) * (diff >= 2 ? 0.6 : 1);
        chase = M.Clamp(chase, 0, 1);
    }

    public bool Countering(int team) => counterUntil[team] > m.Time;

    /// <summary>A player sent off: nobody marks him, runs for him or waits on him any more.</summary>
    public void Forget(Player p)
    {
        for (int t = 0; t < 2; t++)
        {
            if (Chaser[t] == p) Chaser[t] = null;
            if (spBest[t] == p) spBest[t] = null;
            if (wallRunner[t] == p || wallMate[t] == p)
            {
                wallRunner[t] = wallMate[t] = null;
                wallUntil[t] = -1;
            }
            if (playRunner[t] == p || overlapCarrier[t] == p)
            {
                playRunner[t] = overlapCarrier[t] = null;
                playUntil[t] = -1;
            }
        }
        for (int i = 0; i < mark.Length; i++) if (mark[i] == p) mark[i] = null;
        mark[p.Id] = null;
        markAt[0] = markAt[1] = 0;
        if (oneTouchFor == p) oneTouchFor = null;
        if (ownerSeen == p) ownerSeen = null;
        if (lastOwner == p) lastOwner = null;
    }

    /// <summary>How far the team's shape shifts up the pitch (team frame metres) for its mood and the squeeze.</summary>
    double ShapeShift(Player p, bool attacking)
    {
        Mood(p.Team, out double chase, out double protect);
        double s = chase * (attacking ? 7 : 5) - protect * (attacking ? 6 : 5);
        if (!attacking && stepUntil[p.Team] > m.Time && p.Role != Role.FWD) s += 5;
        // The manager's mentality (coach mode): the whole block higher or deeper.
        int mood = m.Mentality[p.Team];
        if (mood != 0) s += mood * (attacking ? 4 : 3.5);
        return s;
    }

    /// <summary>The runner this team's move was made for (the carrier looks for him first).</summary>
    bool IsPlayRunner(Player q) =>
        (playRunner[q.Team] == q && playUntil[q.Team] > m.Time) || (wallRunner[q.Team] == q && wallUntil[q.Team] > m.Time);

    void TeamPlay()
    {
        if (m.Phase != Phase.Play || m.SetPiece != null || m.Training)
        {
            possSeen = -1;
            ownerSeen = null;
            kickSeen = m.LastKickTime;
            for (int t = 0; t < 2; t++) counterUntil[t] = trapUntil[t] = stepUntil[t] = wallUntil[t] = playUntil[t] = -1;
            return;
        }
        var carrier = m.Owner;

        // A new man on the ball.
        if (carrier != null && carrier != ownerSeen)
        {
            int t = carrier.Team;
            if (possSeen == 1 - t) Turnover(carrier);
            OnReceive(carrier);
            possSeen = t;
        }
        ownerSeen = carrier;

        // A new kick.
        if (m.LastKickTime != kickSeen)
        {
            kickSeen = m.LastKickTime;
            var k = m.LastKicker;
            if (k != null && m.ShotBy == null) OnKick(k);
        }

        // The counter runs out once the ball's deep in their half, or slows.
        for (int t = 0; t < 2; t++)
        {
            if (counterUntil[t] <= m.Time) continue;
            double bx = m.Ball.Pos.X * m.Teams[t].Dir;
            if (bx > Pitch.HalfL - 18 || (m.Owner != null && m.Owner.Team != t)) counterUntil[t] = -1;
        }

        int att = m.AttackingTeam();
        if (att >= 0 && m.Time >= teamLook[att])
        {
            teamLook[att] = m.Time + 0.3;
            if (Countering(att)) CounterRuns(att);
            else if (carrier != null && carrier.Team == att)
            {
                Overlap(att, carrier);
                CheckAndSpin(att, carrier);
            }
        }

        OneTouch();
    }

    /// <summary>The ball's been won: a counter is on when the other side is caught upfield.</summary>
    void Turnover(Player c)
    {
        int t = c.Team;
        double dir = m.Teams[t].Dir;
        // Their runs are over.
        foreach (var q in m.Teams[1 - t].Players) run[q.Id].Until = -1;
        counterUntil[1 - t] = -1;
        double bx = m.Ball.Pos.X * dir;
        if (bx > 15 || c.Role == Role.GK) return;
        int back = 0;
        foreach (var o in m.Teams[1 - t].Players)
            if (o.Role != Role.GK && o.Pos.X * dir > bx + 2) back++;
        Mood(t, out double chase, out double protect);
        // Few of them behind the ball: go. (Seeing a game out, a counter is still the way to kill it.)
        double p = back <= 3 ? 0.9 : back <= 4 ? 0.6 : back <= 5 ? 0.25 : 0;
        if (m.Rng.Next() < p * (1 + chase * 0.3))
        {
            counterUntil[t] = m.Time + 5.5 + m.Rng.Next() * 2;
            counterFrom[t] = bx;
            Moves[4]++;
        }
    }

    /// <summary>Someone has just taken the ball: is it a moment for the other side to spring a trap?</summary>
    void OnReceive(Player c)
    {
        int d = 1 - c.Team;
        double cdir = m.Teams[c.Team].Dir;
        double bx = m.Ball.Pos.X * cdir;
        // Back to goal (facing his own end), or pinned on the touchline in his own half: squeeze him.
        bool backTurned = JsMath.Cos(c.Facing) * cdir < -0.25;
        bool pinned = Math.Abs(c.Pos.Z) > Pitch.HalfW - 9 && bx < 10;
        Mood(d, out double chase, out double protect);
        double p = (backTurned ? 0.4 : 0) + (pinned ? 0.3 : 0);
        p *= 1 + chase * 0.5 - protect * 0.5;
        if (c.Role != Role.GK && m.Rng.Next() < p)
        {
            trapUntil[d] = m.Time + 1.6 + m.Rng.Next() * 0.8;
            Moves[5]++;
        }
    }

    void OnKick(Player k)
    {
        int t = k.Team;
        double dir = m.Teams[t].Dir;
        var b = m.Ball;
        double vx = b.Vel.X * dir;
        // The ball's gone backwards: the other side's line steps up as one.
        if (vx < -5 && m.Rng.Next() < 0.7)
        {
            Mood(1 - t, out double c2, out double p2);
            if (p2 < 0.6) stepUntil[1 - t] = m.Time + 2 + m.Rng.Next();
        }
        var q = m.PassTarget;
        if (q != null && q.Team == t && q != k)
        {
            passFrom[q.Id] = k;
            passFromT[q.Id] = m.Time;
        }
        if (q == null || q.Team != t || q == k || k.Role == Role.GK) return;
        // Pass and move: the passer goes for the return.
        if (b.Pos.Y > 0.8 || b.Vel.Y > 3) return;
        if ((m.Piloted(k) && !m.AutoPlay) || m.Time < playCool[k.Id]) return;
        double px = k.Pos.X * dir;
        if (px < -30) return;
        var tr = Traits[k.Id];
        Mood(t, out double chase, out double protect);
        double role = k.Role == Role.DEF ? 0.3 : k.Role == Role.MID ? 1 : 0.85;
        double chance = (0.18 + tr.Creativity * 0.45 + (Countering(t) ? 0.15 : 0) + chase * 0.15 - protect * 0.3) * role;
        if (m.Rng.Next() >= chance) return;
        double line = offside[t];
        double qx = q.Pos.X * dir;
        double pz = k.Pos.Z * dir;
        double qz = q.Pos.Z * dir;
        // Beyond the man he gave it to, on his own side of him: the return goes round the defender.
        double tx = Math.Max(px + 10 + m.Rng.Next() * 5, qx + 5);
        tx = Math.Min(tx, Math.Min(line + 7, Pitch.HalfL - 7));
        if (tx < px + 4) return;
        double tz = pz + M.Clamp((pz - qz) * 0.35, -5, 5);
        if (tx > Pitch.HalfL - 26) tz *= 0.6; // into the box, not to the flag
        tz = M.Clamp(tz, -Pitch.HalfW + 3, Pitch.HalfW - 3);
        SetRun(k, tx * dir, tz * dir, 2.6);
        wallRunner[t] = k;
        wallMate[t] = q;
        wallUntil[t] = m.Time + 2.8;
        playCool[k.Id] = m.Time + 5;
        Moves[0]++;
    }

    /// <summary>
    /// Down the flank: with a wide man on the ball, the full-back behind him bursts round the
    /// outside (and the carrier cuts in to draw the defender); with the full-back on it, the wide
    /// man ahead of him darts inside into the channel.
    /// </summary>
    void Overlap(int t, Player c)
    {
        if (playUntil[t] > m.Time) return;
        double dir = m.Teams[t].Dir;
        double cx = c.Pos.X * dir;
        double side = JsMath.Sign(c.Pos.Z);
        if (Math.Abs(c.Pos.Z) < 10 || cx < -22 || cx > Pitch.HalfL - 14) return;
        Mood(t, out double chase, out double protect);
        double line = offside[t];
        double held = m.Time - possStart;
        bool wideMan = c.Role != Role.DEF || Math.Abs(c.BaseZ) < 0.5;
        Player? best = null;
        double bd = 30;
        foreach (var q in m.Teams[t].Players)
        {
            if (q == c || !Free(q) || JsMath.Sign(q.Pos.Z) != side) continue;
            double qx = q.Pos.X * dir;
            if (wideMan)
            {
                // The full-back (or a wide midfielder) behind him.
                if (q.Role == Role.FWD || Math.Abs(q.BaseZ) < 0.45 || qx > cx - 1) continue;
            }
            else if (q.Role == Role.DEF || qx < cx + 2) continue;
            double d = M.Dist2D(q.Pos.X, q.Pos.Z, c.Pos.X, c.Pos.Z);
            if (d < bd)
            {
                bd = d;
                best = q;
            }
        }
        if (best == null) return;
        var tr = Traits[best.Id];
        double chance = (0.07 + tr.Creativity * 0.08 + tr.Work * 0.08) * (held > 0.8 ? 1.6 : 1) * (1 + chase) * (1 - protect * 0.8);
        if (m.Rng.Next() >= chance) return;
        double tx;
        double tz;
        if (wideMan)
        {
            tx = Math.Min(cx + 12 + m.Rng.Next() * 5, Math.Min(line + 5, Pitch.HalfL - 6));
            tz = side * (Pitch.HalfW - 3) * dir;
            overlapCarrier[t] = c;
        }
        else
        {
            tx = Math.Min(best.Pos.X * dir + 11 + m.Rng.Next() * 4, Math.Min(line + 6, Pitch.HalfL - 8));
            tz = side * (9 + m.Rng.Next() * 4) * dir;
            overlapCarrier[t] = null;
        }
        if (tx < best.Pos.X * dir + 4) return;
        SetRun(best, tx * dir, tz * dir, 3.2);
        playRunner[t] = best;
        playUntil[t] = m.Time + 3.4;
        playCool[best.Id] = m.Time + 9;
        Moves[2]++;
    }

    /// <summary>
    /// A striker tight-marked with the ball behind him comes short to receive; the man next to
    /// him spins into the space that leaves behind.
    /// </summary>
    void CheckAndSpin(int t, Player c)
    {
        double dir = m.Teams[t].Dir;
        double cx = c.Pos.X * dir;
        if (cx > 18 || playUntil[t] > m.Time) return;
        double line = offside[t];
        foreach (var f in m.Teams[t].Players)
        {
            if (f.Role != Role.FWD || f == c || !Free(f)) continue;
            double fx = f.Pos.X * dir;
            if (fx < cx + 8) continue;
            // Marked: a defender tight and goal-side.
            bool marked = false;
            foreach (var o in m.Teams[1 - t].Players)
                if (o.Role != Role.GK && o.Pos.X * dir > fx - 0.5 && M.Dist2D(o.Pos.X, o.Pos.Z, f.Pos.X, f.Pos.Z) < 3) marked = true;
            if (!marked) continue;
            var tr = Traits[f.Id];
            if (m.Rng.Next() >= 0.05 + tr.Creativity * 0.06) continue;
            double dx = c.Pos.X - f.Pos.X;
            double dz = c.Pos.Z - f.Pos.Z;
            double d = Math.Max(0.1, JsMath.Hypot(dx, dz));
            double l = Math.Min(8, d - 6);
            if (l < 3) continue;
            SetRun(f, f.Pos.X + dx / d * l, f.Pos.Z + dz / d * l, 1.3);
            Moves[3]++;
            playCool[f.Id] = m.Time + 6;
            // His partner goes the other way: in behind.
            Player? mate = null;
            double md = 30;
            foreach (var q in m.Teams[t].Players)
            {
                if (q == f || q == c || !Free(q) || q.Role == Role.DEF) continue;
                double qx = q.Pos.X * dir;
                if (qx < line - 12 || qx > line + 0.3) continue;
                double dd = M.Dist2D(q.Pos.X, q.Pos.Z, f.Pos.X, f.Pos.Z);
                if (dd < md)
                {
                    md = dd;
                    mate = q;
                }
            }
            if (mate != null)
            {
                double z = (mate.Pos.Z * 0.6 + f.Pos.Z * 0.4) * dir;
                SetRun(mate, (line + 6 + m.Rng.Next() * 5) * dir, z * dir, 2.4);
                playRunner[t] = mate;
                playUntil[t] = m.Time + 2.6;
                playCool[mate.Id] = m.Time + 6;
            }
            return;
        }
    }

    /// <summary>Counter-attack: everyone ahead of the midfield sprints forward into his own lane, onside, ahead of the ball.</summary>
    void CounterRuns(int t)
    {
        double dir = m.Teams[t].Dir;
        double bx = m.Ball.Pos.X * dir;
        double line = offside[t];
        foreach (var q in m.Teams[t].Players)
        {
            if (q.Role == Role.GK || q.Role == Role.DEF || q == m.Owner || m.PassTarget == q) continue;
            if (m.Piloted(q) && !m.AutoPlay) continue;
            if (q.IsBusy) continue;
            var r = run[q.Id];
            // Midfielders well behind the ball join only if they've the legs and the head for it.
            double qx = q.Pos.X * dir;
            if (q.Role == Role.MID && qx < bx - 6 && Traits[q.Id].Work + Traits[q.Id].Creativity < 1.1) continue;
            // Lanes: wide men stay wide, the middle stays central, spread across the pitch.
            double lz = M.Clamp(q.BaseZ * Pitch.HalfW * 0.75, -Pitch.HalfW + 4, Pitch.HalfW - 4);
            double tx = Math.Max(qx + 10, bx + 6);
            // Onside: beyond the line only once the ball's on its way.
            tx = Math.Min(tx, Math.Min(line - 0.5 + (m.Owner == null ? 6 : 0), Pitch.HalfL - 10));
            if (tx < qx + 1.5) tx = qx + 1.5;
            if (r.Until > m.Time + 0.4) continue;
            SetRun(q, tx * dir, lz * dir, 0.8);
        }
    }

    /// <summary>
    /// First time: the ball's arriving at a computer player. The one-two goes straight back into
    /// the runner's stride; with a man on his back he lays it off; on the break he slips the runner in.
    /// </summary>
    void OneTouch()
    {
        var q = m.PassTarget;
        if (q == null || m.Owner != null || m.HeldBy != null || q.Role == Role.GK || q.Plan != null || q.IsBusy) return;
        if (m.Piloted(q) && !m.AutoPlay) return;
        if (oneTouchKick == m.LastKickTime && oneTouchFor == q) return;
        var b = m.Ball;
        double d = m.BallDist(q);
        if (d > 4.5 || d < 1.2 || b.Pos.Y > 0.9) return;
        // Coming to him (not rolling away).
        if ((q.Pos.X - b.Pos.X) * b.Vel.X + (q.Pos.Z - b.Pos.Z) * b.Vel.Z <= 0) return;
        oneTouchKick = m.LastKickTime;
        oneTouchFor = q;
        int t = q.Team;
        double dir = m.Teams[t].Dir;
        var tr = Traits[q.Id];
        Mood(t, out double chase, out double protect);

        // The one-two.
        var w = wallRunner[t];
        if (w != null && wallMate[t] == q && wallUntil[t] > m.Time && (w.Pos.X - q.Pos.X) * dir > 1.5 && m.Rng.Next() < 0.45 + tr.Creativity * 0.4)
        {
            var tp = PlanThrough(q, false, w);
            if (tp != null)
            {
                FirstTime(q, KickType.Through, w);
                return;
            }
        }
        // Slipping a runner in on the break.
        if (Countering(t) && m.Rng.Next() < 0.25 + tr.Creativity * 0.3)
        {
            var tp = PlanThrough(q, false, null);
            if (tp != null && tp.Score > 1.6)
            {
                FirstTime(q, KickType.Through, tp.Receiver);
                return;
            }
        }
        // A man on his back: set it back or square, first time.
        double tight = m.NearestOpponentDist(q);
        if (tight < 3 && m.Rng.Next() < 0.2 + tr.Creativity * 0.35 - protect * 0.1)
        {
            Player? best = null;
            double bs = 0.35;
            foreach (var o in m.Teams[t].Players)
            {
                if (o == q || o.Role == Role.GK) continue;
                double s = PassScore(q, o, false);
                if (s > bs)
                {
                    bs = s;
                    best = o;
                }
            }
            if (best != null) FirstTime(q, KickType.Pass, best);
        }
    }

    void FirstTime(Player q, KickType type, Player to)
    {
        double dx = to.Pos.X - q.Pos.X;
        double dz = to.Pos.Z - q.Pos.Z;
        double d = Math.Max(0.1, JsMath.Hypot(dx, dz));
        q.Plan = Plan(type, dx / d, dz / d, 0, to.Id, m.Time + 0.8);
        q.LookTarget.Set(to.Pos.X, 0, to.Pos.Z);
        q.LookAt = q.LookTarget;
        Moves[1]++;
    }
}
