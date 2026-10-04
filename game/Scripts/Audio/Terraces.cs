using System;
using System.Collections.Generic;
using GameNight.Sim;

namespace GameNight.Audio;

public enum Vowel { A, E, I, O, U }

public sealed record ChantNote(float B, float D, float P, Vowel V);

public sealed record Chant(string Name, float Bpm, int Beats, ChantNote[] Notes, float[] Claps, float[] Drum);

/// <summary>What an end is singing: a song, or (Chant null) the Viking clap.</summary>
public sealed class Singing
{
    public int Id;
    public int End;
    public Chant Chant;
    public double Start, Until;
    public float Level;
    public double[] Booms = Array.Empty<double>();
}

/// <summary>One frame of the director, handed to the audio thread.</summary>
public sealed class TerraceCue
{
    public double T;
    public Singing Singing;
    public float Level;
    public int[] Boos, Erupts, Groans;
    public float[] Oohs;
}

/// <summary>A flare or smoke bomb in an end (for the stadium's visuals).</summary>
public struct Pyro
{
    public float X, Y, Z;
    public int End;
    public double Born, Life;
    public bool Smoke;
    public float Seed;
}

/// <summary>
/// The terraces on a big European night (the PWA's src/ui/terraces.ts): who's singing what,
/// when the pyro goes up. One director, read by everything that makes the atmosphere: the
/// choir and drums (audio), and for the stadium the fans bouncing on the beat, arms up for the
/// Viking clap, flares, smoke and confetti. The home end (team 0's fans) is behind the left
/// goal, the away end behind the right. It keeps its own clock (stopped while paused) and is
/// driven by the match snapshot, on the game thread.
/// </summary>
public sealed class Terraces
{
    static ChantNote N(float b, float d, float p, Vowel v) => new(b, d, p, v);

    public static readonly Chant[] Chants =
    {
        // The riff every stadium in Europe sings: "oh, oh-oh-oh-oh, oh, oh".
        new("riff", 118, 8, new[] { N(0, 1.5f, 0, Vowel.O), N(1.5f, 0.5f, 0, Vowel.O), N(2, 0.75f, 3, Vowel.O), N(2.75f, 0.75f, 0, Vowel.O), N(3.5f, 0.5f, -2, Vowel.O), N(4, 2, -4, Vowel.O), N(6, 2, -5, Vowel.O) },
            Array.Empty<float>(), new float[] { 0, 2, 4, 6 }),
        // "Olé, olé olé olé, olé, olé!"
        new("ole", 132, 8, new[] { N(0, 1, 4, Vowel.O), N(1, 1, 0, Vowel.E), N(2, 0.5f, 4, Vowel.O), N(2.5f, 0.5f, 0, Vowel.E), N(3, 0.5f, 4, Vowel.O), N(3.5f, 0.5f, 0, Vowel.E), N(4, 1, 2, Vowel.O), N(5, 2.5f, -1, Vowel.E) },
            Array.Empty<float>(), new float[] { 0, 1, 2, 3, 4, 5 }),
        // "Allez, allez, allez!": rolling, the second line answers lower.
        new("allez", 128, 8, new[]
        {
            N(0, 0.5f, 0, Vowel.A), N(0.5f, 1, 0, Vowel.E), N(1.5f, 0.5f, 0, Vowel.A), N(2, 1, 0, Vowel.E), N(3, 0.5f, 0, Vowel.A), N(3.5f, 0.5f, 2, Vowel.E),
            N(4, 0.5f, -2, Vowel.A), N(4.5f, 1, -2, Vowel.E), N(5.5f, 0.5f, -2, Vowel.A), N(6, 1, -2, Vowel.E), N(7, 0.5f, -3, Vowel.A), N(7.5f, 0.5f, 0, Vowel.E),
        }, Array.Empty<float>(), new float[] { 0, 2, 4, 6 }),
        // Clap, clap, clap-clap-clap, clap-clap-clap-clap, then the club's name.
        new("claps", 140, 8, new[] { N(5, 0.5f, 0, Vowel.A), N(5.5f, 0.5f, 0, Vowel.E), N(6, 1.5f, 3, Vowel.O) },
            new float[] { 0, 1, 2, 2.5f, 3, 4, 4.5f }, new float[] { 0, 1, 2, 3, 4, 6 }),
        // A slow, swelling anthem: the whole end holding long notes.
        new("anthem", 84, 8, new[] { N(0, 2, 0, Vowel.O), N(2, 1, 2, Vowel.A), N(3, 1, 4, Vowel.O), N(4, 3, 5, Vowel.A), N(7, 1, 4, Vowel.O) },
            Array.Empty<float>(), new float[] { 0, 4 }),
    };

    /// <summary>The Viking thunder-clap: a boom and a "HUH!", slow, then faster and faster.</summary>
    static readonly double[] VikingGaps = { 3.2, 3.0, 2.6, 2.2, 1.85, 1.5, 1.2, 0.95, 0.78, 0.64, 0.54, 0.47, 0.42, 0.39, 0.37, 0.36, 0.35, 0.35 };

    /// <summary>Director time (seconds); stops when the game does.</summary>
    public double T;
    public Singing Singing;
    public readonly List<Pyro> Pyro = new();
    /// <summary>Smoothed for the visuals: how hard each end is singing (0..1).</summary>
    public float Home, Away;
    /// <summary>Beat phase (0..1) of the song in progress, and the Viking arms-up (0..1).</summary>
    public float Beat, Arms;
    /// <summary>Confetti and ticker tape thrown from an end (drained by whoever draws it).</summary>
    public readonly List<(int end, float amount)> Confetti = new();
    /// <summary>How close each team is to scoring, 0..1 (its end roars louder as it rises).</summary>
    public readonly float[] Danger = new float[2];
    /// <summary>How hard each end is singing right now (the song, ducked under a big attack).</summary>
    public readonly float[] Voice = { 1, 1 };

    readonly List<int> _boos = new(), _erupts = new(), _groans = new();
    readonly List<float> _oohs = new();
    int _shotTeam = -1;
    double _shotAt = -10, _nextConfetti = 60, _next = 3;
    readonly double[] _quiet = new double[2];
    int _ids;
    Phase? _lastPhase;
    readonly Random _rng = new();

    float Rnd() => (float)_rng.NextDouble();

    static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    public void Update(float dt, MatchSnapshot m)
    {
        if (dt <= 0) return;
        T += dt;
        double t = T;
        float bx = m.BallX, bz = m.BallZ;

        // Danger: the ball closing on a goal. Rises quickly, ebbs away more slowly.
        for (int team = 0; team < 2; team++)
        {
            float want = 0;
            if (m.Phase == Phase.Play || m.Phase == Phase.SetPiece)
            {
                float gx = (float)Pitch.HalfL * m.Dir[team];
                float dist = MathF.Sqrt((bx - gx) * (bx - gx) + bz * bz);
                int own = m.Owner;
                if (own >= 0 && m.Team[own] == team)
                {
                    // On the ball: inside 20 m of the goal the crowd is at least at half its roar,
                    // and a player running at goal, fast, takes it all the way.
                    float sp = m.Speed[own], px = m.X[own], pz = m.Z[own];
                    float gd = MathF.Max(0.5f, MathF.Sqrt((gx - px) * (gx - px) + pz * pz));
                    float toward = sp > 0.3f ? Math.Clamp(((gx - px) * m.VX[own] - pz * m.VZ[own]) / (gd * sp), 0, 1) : 0;
                    float drive = toward * Smooth(1.5f, 6.5f, sp);
                    want = (0.5f + 0.5f * drive) * (1 - Smooth(20, 50, dist));
                }
                else
                {
                    // Loose, or the other side has it: some of that, less.
                    float near = 1 - Smooth(6, 44, MathF.Sqrt((bx - gx) * (bx - gx) + bz * 0.8f * bz * 0.8f));
                    int theirs = own >= 0 ? own : m.HeldBy;
                    want = near * (theirs >= 0 ? (m.Team[theirs] == team ? 0.6f : 0.2f) : m.LastTouchTeam == team ? 0.6f : 0.3f);
                }
                if (m.ShotTeam == team) want = 1;
                if (m.SetPiece is SetPieceKind k && m.SetPieceTeam == team && (k == SetPieceKind.Penalty || k == SetPieceKind.Corner || m.SetPieceDirect))
                    want = MathF.Max(want, k == SetPieceKind.Penalty ? 0.9f : 0.6f);
            }
            float d = Danger[team];
            Danger[team] += (want - d) * (1 - MathF.Exp(-dt * (want > d ? 3 : 1.1f)));
        }
        // An end stops singing to roar its team on; the other end goes quiet with nerves.
        for (int end = 0; end < 2; end++)
        {
            float roar = Smooth(0.35f, 0.7f, Danger[end]);
            float nerves = Smooth(0.45f, 0.85f, Danger[1 - end]);
            Voice[end] = 1 - MathF.Max(roar, nerves * 0.75f);
        }
        // A shot that flies just wide: the groan from that end.
        if (m.ShotTeam >= 0)
        {
            _shotTeam = m.ShotTeam;
            _shotAt = t;
        }
        if (m.Phase == Phase.Out && _lastPhase == Phase.Play && _shotTeam >= 0 && t - _shotAt < 3)
        {
            float gx = (float)Pitch.HalfL * m.Dir[_shotTeam];
            if (MathF.Abs(bx - gx) < 3 && MathF.Abs(bz) < 14) _groans.Add(_shotTeam);
            _shotTeam = -1;
        }
        // Now and then, a shower of confetti from one of the ends.
        if (t > _nextConfetti && m.Phase == Phase.Play)
        {
            Confetti.Add((Rnd() < 0.7f ? 0 : 1, 140 + Rnd() * 120));
            _nextConfetti = t + 70 + Rnd() * 110;
        }

        // Kick-off of each half: the curtain-raiser, pyro in both ends, the anthem from home.
        if (m.Phase == Phase.Kickoff && _lastPhase != Phase.Kickoff && _lastPhase != Phase.Goal)
        {
            Confetti.Add((0, 320));
            Confetti.Add((1, 220));
            for (int i = 0; i < 4; i++) Light(0, false, 8 + Rnd() * 6);
            for (int i = 0; i < 3; i++) Light(1, false, 8 + Rnd() * 6);
            Light(0, true, 7);
            Light(1, true, 7);
            Sing(0, Chants[4], 1, 2);
        }
        _lastPhase = m.Phase;

        // Ambient pyro: an occasional flare as the night goes on, more when it's tense.
        float ex = m.Excitement;
        float rate = (0.012f + 0.05f * ex) * (m.Phase == Phase.Play ? 1 : 0.4f);
        if (Rnd() < rate * dt && Pyro.Count < 9) Light(Rnd() < 0.7f ? 0 : 1, Rnd() < 0.08f, 14 + Rnd() * 18);
        Pyro.RemoveAll(f => t - f.Born >= f.Life);

        // The songs: one end at a time, a pause between, the end whose team is pressing more
        // likely to start up; nobody sings for a while after conceding.
        if (Singing != null && t > Singing.Until) Singing = null;
        if (Singing == null && t >= _next && m.Phase != Phase.Halftime && m.Phase != Phase.Fulltime)
        {
            int end = Rnd() < (m.AttackingTeam == 1 ? 0.45f : 0.75f) ? 0 : 1;
            if (t < _quiet[end]) end = 1 - end;
            if (t >= _quiet[end])
            {
                if (Rnd() < 0.14f + 0.2f * ex) Viking(end);
                else StartSong(end, m);
            }
            else _next = t + 2;
        }

        // Visual state: who's singing, the beat, arms up for the Viking clap.
        var cur = Singing;
        // The song follows the game: louder when it's lively, ducked under a big attack.
        if (cur != null) cur.Level = (0.6f + 0.4f * ex) * Voice[cur.End];
        float on = cur != null && t >= cur.Start ? (float)Math.Min(1, Math.Min((t - cur.Start) / 1.2, (cur.Until - t) / 1.5)) * cur.Level : 0;
        float kk = 1 - MathF.Exp(-dt * 4);
        Home += ((cur?.End == 0 ? on : 0) - Home) * kk;
        Away += ((cur?.End == 1 ? on : 0) - Away) * kk;
        if (cur?.Chant != null)
        {
            double beats = (t - cur.Start) * cur.Chant.Bpm / 60;
            Beat = (float)(beats - Math.Floor(beats));
        }
        else if (cur != null)
        {
            // Viking: arms up and waiting, then the clap on each boom.
            double last = -1e9;
            foreach (double b in cur.Booms) if (b <= t) last = b;
            Beat = (float)Math.Min(1, (t - last) / 0.6);
        }
        Arms += ((cur != null && cur.Chant == null && t < cur.Until - 1 ? 1 : 0) - Arms) * kk;
    }

    /// <summary>React to what just happened on the pitch (the frame's events; fouls and offsides name the team).</summary>
    public void OnEvents(MatchSnapshot e, int foulTeam, int offsideTeam)
    {
        double t = T;
        if (e.Goal >= 0)
        {
            int end = e.Goal, other = 1 - end;
            _erupts.Add(end);
            Confetti.Add((end, 420));
            Danger[end] = 0;
            // The scoring end erupts: pyro all along it, a smoke bomb, then the victory song.
            for (int i = 0; i < 7; i++) Light(end, false, 10 + Rnd() * 14, t + Rnd() * 2.5);
            Light(end, true, 9, t + 0.5);
            _quiet[other] = t + 20;
            Sing(end, Rnd() < 0.5f ? Chants[1] : Chants[0], 1, 1.5);
        }
        // The end whose team was pulled up lets the referee hear it...
        if (e.Foul == 1 && foulTeam >= 0) _boos.Add(foulTeam);
        // ...and the end whose striker was flagged gives the linesman some.
        if (e.Offside > 0 && offsideTeam >= 0) _boos.Add(offsideTeam);
        if (e.Save > 0.5f || e.Post > 0) _oohs.Add(1);
    }

    /// <summary>A shot that went close: the whole ground goes "ooooh".</summary>
    public void NearMiss() => _oohs.Add(0.8f);

    /// <summary>This frame for the audio: the clock, the song, and the reactions (drained).</summary>
    public TerraceCue TakeCue()
    {
        var c = new TerraceCue
        {
            T = T, Singing = Singing, Level = Singing?.Level ?? 0,
            Boos = Drain(_boos), Erupts = Drain(_erupts), Groans = Drain(_groans),
            Oohs = _oohs.Count > 0 ? _oohs.ToArray() : Array.Empty<float>(),
        };
        _oohs.Clear();
        return c;
    }

    static int[] Drain(List<int> l)
    {
        if (l.Count == 0) return Array.Empty<int>();
        var a = l.ToArray();
        l.Clear();
        return a;
    }

    /// <summary>A song to suit the game: cruising, they mock ("olé"); behind, they dig in.</summary>
    void StartSong(int end, MatchSnapshot m)
    {
        int lead = m.Score[end] - m.Score[1 - end];
        Chant Pick(params string[] names) => Array.Find(Chants, c => c.Name == names[(int)(Rnd() * names.Length)]);
        var chant = lead >= 2 ? Pick("ole", "ole", "riff", "claps") : lead < 0 ? Pick("anthem", "allez", "allez", "claps") : Chants[(int)(Rnd() * Chants.Length)];
        Sing(end, chant, 0.6f + 0.4f * m.Excitement);
    }

    void Sing(int end, Chant chant, float level, double delay = 0)
    {
        double start = T + delay;
        double loop = chant.Beats * 60.0 / chant.Bpm;
        int loops = Math.Max(2, (int)Math.Round((10 + Rnd() * 10) / loop));
        Singing = new Singing { Id = ++_ids, End = end, Chant = chant, Start = start, Until = start + loops * loop, Level = level };
        _next = start + loops * loop + 4 + Rnd() * 7;
    }

    void Viking(int end)
    {
        double start = T + 0.5;
        var booms = new double[VikingGaps.Length];
        double b = start + 1.2;
        for (int i = 0; i < VikingGaps.Length; i++)
        {
            booms[i] = b;
            b += VikingGaps[i];
        }
        // The last burst: a roar after the final quick claps.
        Singing = new Singing { Id = ++_ids, End = end, Chant = null, Start = start, Until = b + 2.5, Level = 1, Booms = booms };
        _next = b + 8 + Rnd() * 6;
    }

    /// <summary>Where things happen in each end: the lower tier behind each goal (world space).</summary>
    void Light(int end, bool smoke, double life, double at = double.NaN)
    {
        const float bowlX = (float)Pitch.HalfL + 8.5f;
        float o = 3 + Rnd() * 13; // metres back from the front of the tier
        float z = (Rnd() - 0.5f) * 42;
        float y = 1.4f + (o - 0.4f) / 19.6f * 10.1f + 1.7f; // held up overhead
        Pyro.Add(new Pyro { X = (end == 0 ? -1 : 1) * (bowlX + o), Y = y, Z = z, End = end, Born = double.IsNaN(at) ? T : at, Life = life, Smoke = smoke, Seed = Rnd() });
    }
}
