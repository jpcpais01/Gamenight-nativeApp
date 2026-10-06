namespace GameNight.Sim;

/// <summary>
/// The dotted flight of the human's aimed delivery (the PWA's cornerAim): while a corner or goal
/// kick is aimed (Shoot held floats it), or while Pass is held and slid up on the ball in the
/// crossing zone, the ball's path from the foot until it comes down, sampled evenly in time, and
/// the ring where it lands. Re-solved at most 20 times a second. MatchRunner runs it after every
/// step; without the runner, call Update after Match.Step and Write after Match.Write.
/// </summary>
public sealed class DeliveryPreview
{
    public const int Dots = 34;

    readonly Ball ball = new Ball();
    readonly float[] x = new float[Dots], y = new float[Dots], z = new float[Dots];
    int count;
    bool has;
    float ringX, ringZ;
    double solvedAt = -1;
    string lastKey = "";

    /// <summary>`input2`: a 1v1's other side (whoever is on the ball, or taking the set piece, is shown).</summary>
    public void Update(Match m, InputState input, InputState? input2 = null)
    {
        bool playing = !m.AutoPlay;
        var sp = m.SetPiece;
        int team = sp?.Team ?? m.Owner?.Team ?? m.HumanTeam;
        if (!m.HumanSide(team)) team = m.HumanTeam;
        if (team != m.HumanTeam && input2 != null) input = input2;
        if (playing && m.AimingDelivery && sp?.Target != null)
        {
            var t = sp.Target;
            bool floated = input.Held[Btn.C];
            ringX = (float)t.X;
            ringZ = (float)t.Z;
            has = true;
            // Re-solve the flight only when the aim or the style changes (at most 20 a second).
            string key = $"{t.X:F2}|{t.Z:F2}|{floated}";
            if (key == lastKey || m.Time - solvedAt < 0.05) return;
            lastKey = key;
            solvedAt = m.Time;
            var r = m.SolveDelivery(t.X, t.Z, floated);
            Fly(m, r.Vel, r.Spin, r.Time);
            return;
        }
        if (playing && input.Held[Btn.A] && input.Swipe[Btn.A])
        {
            // The cross follows the stick and the runners: re-solved up to 20 times a second.
            if (m.Time - solvedAt >= 0.05 || lastKey != "cross")
            {
                solvedAt = m.Time;
                lastKey = "cross";
                var c = m.CrossAim(input.MoveX, input.MoveY, team);
                has = c != null;
                if (c != null)
                {
                    ringX = (float)c.X;
                    ringZ = (float)c.Z;
                    Fly(m, c.Vel, c.Spin, c.Time);
                }
            }
            if (has) return;
        }
        lastKey = "";
        has = false;
    }

    /// <summary>Puts the arc and ring into the snapshot (HasArc false when there's nothing to show).</summary>
    public void Write(MatchSnapshot s)
    {
        s.HasArc = has;
        s.ArcCount = has ? count : 0;
        s.ArcRingX = ringX;
        s.ArcRingZ = ringZ;
        System.Array.Copy(x, s.ArcX, Dots);
        System.Array.Copy(y, s.ArcY, Dots);
        System.Array.Copy(z, s.ArcZ, Dots);
    }

    /// <summary>The dotted flight from the ball, sampled evenly in time until it comes down.</summary>
    void Fly(Match m, V3 vel, V3 spin, double time)
    {
        var b = ball;
        b.Pos.Copy(m.Ball.Pos);
        b.PrevPos.Copy(b.Pos);
        b.Vel.Copy(vel);
        b.Spin.Copy(spin);
        b.OnGround = false;
        b.InGoal = false;
        double total = System.Math.Max(0.2, time);
        double every = total / (Dots - 1);
        double next = 0;
        int n = 0;
        double tt = 0;
        while (n < Dots && tt <= total + Tick.DT)
        {
            if (tt >= next)
            {
                x[n] = (float)b.Pos.X;
                y[n] = (float)System.Math.Max(0.12, b.Pos.Y);
                z[n] = (float)b.Pos.Z;
                n++;
                next += every;
            }
            b.Step(Tick.DT);
            tt += Tick.DT;
        }
        count = n;
    }
}
