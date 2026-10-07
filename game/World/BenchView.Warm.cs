using System;
using System.Collections.Generic;
using Godot;
using GameNight.Render;
using GameNight.Sim;
using GameNight.Club;
using Part = GameNight.Render.BodyMeshes.Part;

namespace GameNight.Grounds;

/// <summary>
/// The warm-up: every so often two to four of a side's subs pull on fluorescent bibs and go
/// through a proper routine along the far touchline, between their dugout and the corner, as a
/// row: a couple of easy jogs, then high knees, heel flicks, side shuffles facing the pitch and
/// sprints that go off in a wave, with leg swings, quad and hamstring stretches, side bends,
/// twists and a breather (hands on hips, watching the game) at the ends. Their fitness coach
/// walks along with them on the pitch side, clapping them through the sprints and pointing out
/// the next one. More of it in the second half; they jog back and sit down (bibs still on) when
/// the session is done.
/// </summary>
public sealed partial class BenchView
{
    enum Drill { None, Jog, Knees, Kicks, Shuffle, Sprint, Swings, Quad, Hams, Bend, Twist, Rest }

    enum Stage { Gather, Travel, Hold }

    sealed class Squad
    {
        public readonly List<int> Who = new();
        public Stage Stage;
        public Drill Drill, Last;
        public int End, Reps;
        public float StageEnd, Over, Next;
        public float Lane;
    }

    // The lane: from just past the dugout to short of the corner, between the dugout front and
    // the touchline (clear of the ball boys by the boards).
    const float LaneIn = 15, LaneOut = 38, RowGap = 0.6f;
    static readonly float LaneZ = DugZ + 1.2f;

    readonly Squad[] _sq = { new(), new() };

    static readonly float[] Shuffling = { 0.45f, 0.45f, 1.25f, 1.25f, 0.42f, 0.42f, -0.2f, -0.2f };
    static readonly float[] Balance = { 0.12f, 0.12f, 0.35f, 0.35f, 1.2f, 1.2f, 0, 0 };
    static readonly int[] BibColours = { 0xc8ff2a, 0xff7a1a, 0xff3d9a, 0x2ee6ff, 0xffe42e };
    readonly int[] _bib = new int[2];
    // Debug: `-- --warm=Knees` (any drill) starts the warm-up at once and keeps to that drill.
    static readonly Drill WarmDebug = Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--warm=")) is string w && Enum.TryParse(w[7..], out Drill d) ? d : Drill.None;

    void WarmInit(Kit home, Kit away)
    {
        // A bib colour per side that stands out from its shirt (and from the other side's bibs).
        for (int team = 0; team < 2; team++)
        {
            int shirt = (team == 0 ? home : away).Shirt, best = 0;
            float bestD = -1;
            foreach (int c in BibColours)
            {
                if (team == 1 && c == _bib[0]) continue;
                float d = Dist(c, shirt) + _rng.NextSingle() * 0.15f;
                if (d > bestD) { bestD = d; best = c; }
            }
            _bib[team] = best;
            _sq[team].Next = WarmDebug != Drill.None ? 0 : R(6, 40) + team * R(10, 30);
        }
        // The fitness coaches: club training top, dark trousers, trainers.
        for (int team = 0; team < 2; team++)
        {
            int i = FitBase + team;
            float sgn = team == 0 ? -1 : 1;
            var f = _f[i] = new Fig { Team = team, Boss = true, Fit = true, Ph = R(0, 100), V = _rng.Next(3), Want = Want.Stand, Sit = 0 };
            f.Temper = _rng.Next(2) == 0 ? CoachTemper.Cool : CoachTemper.Fiery;
            f.B = Body.Shape(R(1.72f, 1.86f), R(70, 84), 0.5, i * 17.3 + _rng.NextDouble() * 50);
            f.Leg = (float)f.B.Leg;
            f.HipBase = (THIGH + SHIN) * f.Leg + (HIP_Y - THIGH - SHIN);
            f.Scale = R(0.97f, 1.02f) * BASE_HEIGHT / (f.HipBase + 0.04f + 0.6f * (float)f.B.TorsoL + ((float)f.B.NeckLen - 1) * 0.08f + HEAD_TOP);
            f.X = f.SpotX = sgn * (DugX + 4.3f);
            f.Z = f.SpotZ = FrontZ + 0.2f;
            f.Facing = PI / 2;
            f.Idle = Array.IndexOf(Idles, Folded);
            f.Until = float.MaxValue;
            var kit = team == 0 ? home : away;
            int skin = TeamData.SkinTones[_rng.Next(TeamData.SkinTones.Length)];
            int top = kit.Shirt, trousers = 0x1c2029;
            f.Hair = _rng.Next(4);
            void Set(Part p, int rgb) => Paint(i, p, rgb);
            Set(Part.Torso, top);
            Set(Part.UpperArm, top);
            Set(Part.Forearm, top);
            Set(Part.Hand, skin);
            Set(Part.Pelvis, trousers);
            Set(Part.ShortsLeg, trousers);
            Set(Part.Thigh, trousers);
            Set(Part.Shin, trousers);
            Set(Part.Neck, skin);
            Set(Part.Head, skin);
            int hair = TeamData.HairColors[_rng.Next(TeamData.HairColors.Length)];
            Set(Part.HairShort, hair);
            Set(Part.HairCurly, hair);
            Set(Part.HairQuiff, hair);
            Set(Part.Boot, 0xf0efe9);
            Shape(f, 0, false);
            Array.Copy(f.Tgt, f.Cur, ShapeN);
        }
    }

    static float Dist(int a, int b)
    {
        float dr = ((a >> 16) & 255) - ((b >> 16) & 255), dg = ((a >> 8) & 255) - ((b >> 8) & 255), db = (a & 255) - (b & 255);
        return MathF.Sqrt(dr * dr + dg * dg + db * db) / 441f;
    }

    /// <summary>On goes the bib: the torso in fluorescent mesh with a darker edge, no number, no
    /// stripes (the sleeves stay the kit's).</summary>
    void PutOnBib(int id, Fig f)
    {
        if (f.Bib) return;
        f.Bib = true;
        int bib = _bib[f.Team];
        int edge = (int)(((bib >> 16) & 255) * 0.62f) << 16 | (int)(((bib >> 8) & 255) * 0.62f) << 8 | (int)((bib & 255) * 0.62f);
        Paint(id, Part.Torso, bib);
        int k = (int)Part.Torso;
        _ka[k][_pid[id]] = Lin4(edge, -1);
        _kb[k][_pid[id]] = Lin4(edge, 0);
        _mats[k].SetShaderParameter("ka", _ka[k]);
        _mats[k].SetShaderParameter("kb", _kb[k]);
    }

    Fig Chatting(Fig f) =>
        f.Mate >= 0 && _t < f.ChatUntil && f.Want == Want.Sit && f.React == React.None && _f[f.Mate].Want == Want.Sit ? _f[f.Mate] : null;

    // ------------------------------------------------------------------ the session

    void WarmUpdate(MatchSnapshot s, float dt)
    {
        for (int team = 0; team < 2; team++)
        {
            var q = _sq[team];
            var coach = _f[FitBase + team];
            float sgn = team == 0 ? -1 : 1;
            if (q.Who.Count == 0)
            {
                if (_t >= q.Next && s.Phase != Phase.Fulltime && (WarmDebug != Drill.None || !(s.Phase == Phase.Kickoff && s.Minute == 0 && s.Half <= 1))) Begin(q, team);
                else
                {
                    // Between sessions: by the end of the dugout, arms folded, watching.
                    coach.SpotX = sgn * (DugX + 4.3f);
                    coach.SpotZ = FrontZ + 0.2f;
                    coach.HasLook = false;
                    if (_t >= coach.Until || coach.Until == float.MaxValue)
                    {
                        coach.Idle = Array.IndexOf(Idles, _rng.Next(3) == 0 ? Hips : Folded);
                        coach.Until = _t + R(8, 20);
                    }
                    continue;
                }
            }

            if (s.Phase == Phase.Fulltime) { Dismiss(q, s); continue; }

            // Everyone at his mark (and slowed) moves the session on.
            bool there = true;
            float mx = 0;
            foreach (int i in q.Who)
            {
                var f = _f[i];
                mx += f.X;
                float dx = f.SpotX - f.X, dz = f.SpotZ - f.Z;
                if (dx * dx + dz * dz > 0.2f * 0.2f || f.Speed > 0.5f || f.Sit > 0) there = false;
            }
            mx /= q.Who.Count;
            switch (q.Stage)
            {
                case Stage.Gather:
                case Stage.Travel:
                    if (there || _t > q.StageEnd)
                    {
                        if (_t >= q.Over && q.Reps >= 3) { Dismiss(q, s); continue; }
                        AtEnd(q);
                    }
                    break;
                case Stage.Hold:
                    if (_t > q.StageEnd) Travel(q, team);
                    break;
            }

            // The fitness coach walks along the pitch side of the row, facing them.
            coach.SpotX = sgn * Clamp(MathF.Abs(mx) + (q.Stage == Stage.Travel ? (q.End == 1 ? 2.5f : -2.5f) : 0), LaneIn, LaneOut);
            coach.SpotZ = LaneZ + 1.3f;
            coach.HasLook = true;
            coach.LookX = mx;
            coach.LookZ = LaneZ;
            if (_t >= coach.Until)
            {
                // Clapping them through the hard ones, otherwise a point, a wave on, hands on hips.
                bool hard = q.Stage == Stage.Travel && q.Drill is Drill.Sprint or Drill.Knees or Drill.Kicks or Drill.Shuffle;
                if (hard && _rng.Next(3) > 0) Act(coach, React.Clap, 0, R(1.5f, 3));
                else coach.Idle = Array.IndexOf(Idles, new[] { Hips, Folded, Point, Beckon, Behind }[_rng.Next(5)]);
                coach.Until = _t + R(2.5f, 5);
            }
        }
    }

    void Begin(Squad q, int team)
    {
        // Two to four outfield subs who aren't busy reacting to something.
        var pool = new List<int>();
        for (int seat = 1; seat < PerBench; seat++)
        {
            int i = team * PerBench + seat;
            if (_f[i].React == React.None) pool.Add(i);
        }
        int n = Math.Min(pool.Count, 2 + _rng.Next(3));
        if (n < 2) { q.Next = _t + 10; return; }
        q.Who.Clear();
        for (int k = 0; k < n; k++)
        {
            int pick = _rng.Next(pool.Count);
            q.Who.Add(pool[pick]);
            pool.RemoveAt(pick);
        }
        q.Who.Sort();
        q.Over = _t + R(55, 110);
        q.Reps = 0;
        q.Last = Drill.None;
        q.End = 0;
        q.Lane = 0;
        // Out of the dugout to the near end of the lane, at a walk.
        for (int k = 0; k < n; k++)
        {
            var f = _f[q.Who[k]];
            f.Want = Want.Warm;
            f.Until = float.MaxValue;
            f.Mate = -1;
            f.ChatUntil = 0;
            f.Drill = Drill.None;
            f.Pace = 1.5f;
            f.Go = _t + k * R(0.3f, 0.9f);
            PutOnBib(q.Who[k], f);
            Mark(q, team, k, f);
        }
        q.Stage = Stage.Gather;
        q.StageEnd = _t + 25;
    }

    /// <summary>His place in the row at the squad's end of the lane.</summary>
    void Mark(Squad q, int team, int k, Fig f)
    {
        float sgn = team == 0 ? -1 : 1;
        int n = q.Who.Count;
        f.SpotX = sgn * (q.End == 0 ? LaneIn : LaneOut);
        f.SpotZ = LaneZ + (k - (n - 1) * 0.5f) * RowGap;
    }

    /// <summary>At an end: a stretch, a breather, or straight back across.</summary>
    void AtEnd(Squad q)
    {
        int team = _f[q.Who[0]].Team;
        double r = _rng.NextDouble();
        if (WarmDebug != Drill.None && !LegDrill(WarmDebug) || WarmDebug is Drill.Swings or Drill.Quad or Drill.Hams)
        {
            if (WarmDebug is Drill.Jog or Drill.Sprint) { Travel(q, team); return; }
            HoldOn(q, WarmDebug, 999);
            return;
        }
        if (q.Stage == Stage.Travel && q.Drill == Drill.Sprint || r < 0.18)
            HoldOn(q, Drill.Rest, R(4, 9)); // hands on hips, getting the breath back, watching the game
        else if (q.Reps >= 1 && r < 0.55)
        {
            var d = new[] { Drill.Swings, Drill.Swings, Drill.Quad, Drill.Quad, Drill.Hams, Drill.Hams, Drill.Bend, Drill.Twist }[_rng.Next(8)];
            HoldOn(q, d, d is Drill.Bend or Drill.Twist ? R(5, 8) : R(7, 11));
        }
        else Travel(q, team);
    }

    void HoldOn(Squad q, Drill d, float dur)
    {
        q.Stage = Stage.Hold;
        q.StageEnd = _t + dur;
        int side = _rng.Next(2);
        foreach (int i in q.Who)
        {
            var f = _f[i];
            f.Drill = d;
            // The one-legged ones change legs halfway (Pose's side switch eases through).
            f.Side = side;
            f.Go = _t;
            f.V = _rng.Next(3);
            f.SpotX = f.X;
            f.SpotZ = f.Z;
        }
        if (d is Drill.Swings or Drill.Quad or Drill.Hams) _swapAt[_f[q.Who[0]].Team] = _t + dur * 0.5f;
    }

    readonly float[] _swapAt = new float[2];

    void Travel(Squad q, int team)
    {
        // Two easy jogs to start; then the drills, never the same one twice running, and an easy
        // jog back after every sprint.
        Drill d;
        if (q.Reps < 2 || q.Last == Drill.Sprint) d = Drill.Jog;
        else
        {
            var pick = new[] { Drill.Jog, Drill.Knees, Drill.Knees, Drill.Kicks, Drill.Kicks, Drill.Shuffle, Drill.Shuffle, Drill.Sprint, Drill.Sprint, Drill.Sprint };
            do d = pick[_rng.Next(pick.Length)]; while (d == q.Last && d != Drill.Jog);
        }
        if (WarmDebug != Drill.None) d = WarmDebug;
        q.Last = q.Drill = d;
        q.Stage = Stage.Travel;
        q.Reps++;
        q.End ^= 1;
        float span = LaneOut - LaneIn;
        float pace = d switch { Drill.Jog => 3.0f, Drill.Knees => 1.45f, Drill.Kicks => 1.7f, Drill.Shuffle => 1.9f, Drill.Sprint => 6.6f, _ => 1.5f };
        q.StageEnd = _t + span / pace + 8;
        // Sprints go off in a wave down the row; the rest set off together.
        float wave = d == Drill.Sprint ? R(0.45f, 0.7f) : 0;
        int s0 = _rng.Next(2);
        for (int k = 0; k < q.Who.Count; k++)
        {
            var f = _f[q.Who[k]];
            f.Drill = d;
            f.Side = 0;
            f.Pace = pace * R(0.97f, 1.03f);
            f.Go = _t + R(0.2f, 0.5f) + wave * (s0 == 0 ? k : q.Who.Count - 1 - k);
            Mark(q, team, k, f);
        }
    }

    void Dismiss(Squad q, MatchSnapshot s)
    {
        // Back to the bench; they sit down in their bibs.
        foreach (int i in q.Who)
        {
            var f = _f[i];
            f.Want = Want.Stand;
            f.Drill = Drill.None;
            f.Until = _t;
            f.SpotX = f.SeatX;
            f.SpotZ = FrontZ + 0.2f;
        }
        q.Who.Clear();
        q.Next = _t + (s.Half >= 2 ? R(20, 55) : R(45, 100));
    }

    // ------------------------------------------------------------------ the drills' bodies

    /// <summary>A drill's own beat (cycles a second), or 0 to step with the speed.</summary>
    static float Cadence(Drill d) => d switch { Drill.Knees => 2.5f, Drill.Kicks => 2.3f, Drill.Shuffle => 1.75f, _ => 0 };

    /// <summary>One leg in the drill: hip (+ forward), knee bend, out to the side, toes (+ pointed).</summary>
    (float hip, float knee, float outA, float toe) DrillLeg(Fig f, int sd, float ph)
    {
        float t = _t + f.Ph;
        bool mine = sd == f.ShownSide;
        switch (f.Shown)
        {
            case Drill.Knees:
            {
                // Thigh driven up to level, toes down, the other leg straight beneath him.
                float u = MathF.Max(0, MathF.Sin(ph));
                u = u * u * (3 - 2 * u);
                return (0.04f + 1.5f * u, 0.08f + 1.7f * u, 0.04f, 0.35f * u);
            }
            case Drill.Kicks:
            {
                // Heel flicked up to the backside, thigh staying down.
                float u = MathF.Max(0, MathF.Sin(ph));
                return (-0.04f + 0.14f * MathF.Sin(ph), 0.1f + 2.05f * u, 0.03f, 0.5f * u);
            }
            case Drill.Shuffle:
            {
                // Low and wide, the feet opening and closing on the beat.
                float open = 0.5f + 0.5f * MathF.Sin(f.Phi);
                return (0.42f, 0.82f, 0.08f + 0.2f * open, 0);
            }
            case Drill.Swings:
            {
                if (!mine) return (0, 0.05f, 0.04f, 0);
                float w = MathF.Sin(t * TAU * 0.8f);
                // Front-to-back, or across the body and out.
                return f.V == 2
                    ? (0.35f, 0.12f, 0.1f + 0.45f * w, 0.1f)
                    : (0.25f + 0.85f * w, 0.08f + 0.25f * MathF.Max(0, -w), 0.04f, 0.15f);
            }
            case Drill.Quad:
                // Heel held to the backside, standing tall on the other leg.
                return mine ? (-0.18f, 2.3f, 0.02f, 0.55f) : (0, 0.04f, 0.03f, 0);
            case Drill.Hams:
                // Front leg straight, heel down, toes up; back knee soft.
                return mine ? (0.55f, 0.02f, 0.05f, -0.5f) : (-0.14f, 0.5f, 0.05f, 0);
            default:
                return (0, 0.04f, 0.04f, 0);
        }
    }

    /// <summary>The body's bounce off the ground in a drill.</summary>
    float DrillBounce(Fig f) => f.Shown switch
    {
        Drill.Knees => 0.045f * MathF.Abs(MathF.Sin(f.Phi)),
        Drill.Kicks => 0.035f * MathF.Abs(MathF.Sin(f.Phi)),
        Drill.Shuffle => 0.03f * MathF.Abs(MathF.Sin(f.Phi)),
        _ => 0,
    };

    static bool LegDrill(Drill d) => d is Drill.Knees or Drill.Kicks or Drill.Shuffle or Drill.Swings or Drill.Quad or Drill.Hams;

    /// <summary>Trunk and arms for the drill he's on; and his legs eased from one drill to the
    /// next through a plain stance, so nothing snaps.</summary>
    void WarmShape(Fig f, float[] g, float t, ref float[] arms, ref float w)
    {
        // The swap halfway through a one-legged stretch.
        if (f.Drill is Drill.Swings or Drill.Quad or Drill.Hams && t >= _swapAt[f.Team] && _swapAt[f.Team] > f.Go)
        {
            f.Side ^= 1;
            f.Go = t;
        }
        float osc = MathF.Sin((t + f.Ph) * (f.V == 2 ? 2.2f : 1.5f));
        switch (f.Drill)
        {
            case Drill.Sprint:
                g[Flex] += 0.12f * Smooth(3, 6, f.Speed);
                break;
            case Drill.Knees:
                g[Flex] -= 0.06f;
                break;
            case Drill.Kicks:
                g[Flex] += 0.1f;
                break;
            case Drill.Shuffle:
                arms = Shuffling;
                g[Flex] += 0.32f;
                g[Head] -= 0.32f;
                w = 1;
                break;
            case Drill.Swings:
                arms = Balance;
                w = 1;
                break;
            case Drill.Quad:
            {
                // The hand on the held foot reaches back; the other out for balance.
                var a = f.ArmBuf;
                Array.Copy(Balance, a, 8);
                int s = f.Shown == Drill.Quad ? f.ShownSide : f.Side;
                a[s] = -0.62f;
                a[2 + s] = 0.3f;
                a[4 + s] = 0.12f;
                a[1 - s] = 0.2f;
                a[4 + (1 - s)] = 0.75f;
                arms = a;
                w = 1;
                break;
            }
            case Drill.Hams:
                arms = Reach;
                g[Flex] += 0.8f;
                g[Head] -= 0.3f;
                w = 1;
                break;
            case Drill.Bend:
                arms = Up;
                g[Side] += 0.38f * osc;
                w = 1;
                break;
            case Drill.Twist:
                arms = Hips;
                g[Tw] += 0.6f * osc;
                w = 1;
                break;
            case Drill.Rest:
                // Hands on hips (or on the knees, bent over), breathing hard.
                arms = f.V == 0 ? Knees : Hips;
                g[Flex] += f.V == 0 ? 0.55f : 0.06f + 0.03f * MathF.Sin((t + f.Ph) * 5);
                w = 1;
                break;
        }
    }

    /// <summary>The legs' drill, faded out and in at each change.</summary>
    void DrillBlend(Fig f, float dt)
    {
        var want = f.Want == Want.Warm && f.Sit <= 0 && (f.React == React.None || _t < f.ReactAt) ? f.Drill : Drill.None;
        int side = f.Side;
        // Moving drills only while on the move (and set off); stretches only once stood still.
        if (want is Drill.Knees or Drill.Kicks or Drill.Shuffle && (_t < f.Go || MathF.Abs(f.SpotX - f.X) + MathF.Abs(f.SpotZ - f.Z) < 0.15f)) want = Drill.None;
        if (want is Drill.Swings or Drill.Quad or Drill.Hams && f.Speed > 0.3f) want = Drill.None;
        if (!LegDrill(want)) want = Drill.None;
        if (want != f.Shown || side != f.ShownSide && want != Drill.None)
        {
            f.DrillK = MathF.Max(0, f.DrillK - dt * 4.5f);
            if (f.DrillK <= 0)
            {
                f.Shown = want;
                f.ShownSide = side;
            }
        }
        else if (want != Drill.None) f.DrillK = MathF.Min(1, f.DrillK + dt * 3.5f);
    }
}
