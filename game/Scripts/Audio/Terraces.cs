using System;
using System.Collections.Generic;
using GameNight.Sim;

namespace GameNight.Audio;

public enum Vowel { A, E, I, O, U }

/// <summary>A sung note: at beat B for D beats, P semitones from the root. A Call is the capo
/// and the few round him, which the rest of the end answers.</summary>
public sealed record ChantNote(float B, float D, float P, Vowel V, bool Call = false);

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

/// <summary>What the crowd does in reaction to the game.</summary>
public enum React : byte
{
    /// <summary>The whole ground: "ooooh", a chance gone close.</summary>
    Ooh,
    /// <summary>A shot just wide: "aaah-ohh", falling.</summary>
    Groan,
    /// <summary>An attack that came to nothing: a sigh.</summary>
    Aww,
    Applause,
    /// <summary>A long, low boo (and some whistling) at the referee.</summary>
    Boo,
    /// <summary>Whistling and booing at the other side: time-wasting, a card, a defeat.</summary>
    Jeer,
    /// <summary>A short "YEAH!": a big tackle, a card for them, the teams coming back out.</summary>
    Cheer,
    /// <summary>A goal: the scoring end claps along after the roar, the other one deflates.</summary>
    Erupt,
    /// <summary>After conceding: the end claps its team back into it.</summary>
    Rally,
    /// <summary>The PA calls the scorer's first name, the end roars his surname back (three times).</summary>
    NameCall,
    /// <summary>The PA's chime and an announcement.</summary>
    Announce,
    /// <summary>One fan shouting.</summary>
    Heckle,
    /// <summary>One fan whistling.</summary>
    Whistler,
}

/// <summary>A reaction from one end (or -1: the whole ground), at director time At.</summary>
public struct Reaction
{
    public React Kind;
    public int End;
    public double At;
    public float Level, Dur;
}

/// <summary>One frame of the director, handed to the audio thread.</summary>
public sealed class TerraceCue
{
    public double T;
    public Singing Singing;
    public float Level;
    public Reaction[] Reactions;
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
    static ChantNote N(float b, float d, float p, Vowel v, bool call = false) => new(b, d, p, v, call);

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
        // Call and response: the capo and his section sing a line, the whole end throws it back.
        new("capo", 120, 8, new[]
        {
            N(0, 1, 2, Vowel.A, true), N(1, 1, 0, Vowel.O, true), N(2, 0.5f, 2, Vowel.A), N(2.5f, 0.5f, 0, Vowel.E), N(3, 1, 0, Vowel.O),
            N(4, 1, 4, Vowel.E, true), N(5, 1, 2, Vowel.O, true), N(6, 0.5f, 4, Vowel.A), N(6.5f, 0.5f, 2, Vowel.E), N(7, 1, 0, Vowel.O),
        }, Array.Empty<float>(), new float[] { 2, 3, 6, 7 }),
        // The club's name in three syllables, then three claps.
        new("name", 100, 4, new[] { N(0, 0.5f, 4, Vowel.A), N(0.5f, 0.5f, 2, Vowel.E), N(1, 1, 0, Vowel.A) },
            new float[] { 2, 2.5f, 3 }, new float[] { 0, 2 }),
        // Everybody bouncing: the drum on every beat and a shout on every other.
        new("bounce", 150, 8, new[] { N(0, 0.5f, 5, Vowel.E), N(2, 0.5f, 5, Vowel.E), N(4, 0.5f, 5, Vowel.E), N(6, 0.75f, 7, Vowel.A) },
            Array.Empty<float>(), new float[] { 0, 1, 2, 3, 4, 5, 6, 7 }),
        // The away end's: "come on you ...", simple enough for a few thousand far from home.
        new("away", 110, 8, new[] { N(0, 1, 0, Vowel.O), N(1, 0.5f, 0, Vowel.O), N(1.5f, 0.5f, 2, Vowel.U), N(2, 2, 4, Vowel.E), N(4, 1, 2, Vowel.O), N(5, 1, 0, Vowel.A), N(6, 2, -1, Vowel.E) },
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

    // What each end is doing, for the stadium to show (0..1, smoothed): clapping (applause, a
    // rally, after a goal), jeering (boos and whistles: arms out, thumbs down), hands on heads
    // (a near miss, a groan), fists up (a cheer, the roar of a name after a goal).
    public readonly float[] Clapping = new float[2], Jeering = new float[2], Heads = new float[2], Fists = new float[2];
    /// <summary>The stunned home crowd after the away side scores (1, then fading over 12 s).</summary>
    public float Hush;
    /// <summary>The tension in the ground: the nearer of the two teams to scoring (0..1).</summary>
    public float Tension => MathF.Max(Danger[0], Danger[1]);

    readonly List<Reaction> _new = new(), _live = new();
    readonly float[] _peak = new float[2];
    double _heldSince = -1, _jeeredFor = -1;
    int _halfCheered;
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
            if (MathF.Abs(bx - gx) < 3 && MathF.Abs(bz) < 14)
            {
                float close = 1 - Smooth(4, 14, MathF.Abs(bz));
                Add(React.Ooh, -1, 0, 0.6f + 0.4f * close);
                Add(React.Groan, _shotTeam, 0.5, 0.7f + 0.3f * close);
                Add(React.Applause, _shotTeam, 1.5, 0.45f + 0.2f * close, 2.6f);
            }
            _shotTeam = -1;
        }
        // Now and then, a shower of confetti from one of the ends.
        // (Nobody drawing confetti yet: keep only the latest few.)
        if (Confetti.Count > 8) Confetti.RemoveRange(0, Confetti.Count - 8);
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
        Moments(m, t, dt);
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
        Show(t, kk);
    }

    /// <summary>The rest of what a crowd does: sighs when an attack dies, whistles at time-wasting,
    /// cheers the teams out, greets half and full time, and the odd fan shouting or whistling.</summary>
    void Moments(MatchSnapshot m, double t, float dt)
    {
        Hush = MathF.Max(0, Hush - dt / 12);
        // An attack that built and came to nothing (no shot): "aww".
        for (int team = 0; team < 2; team++)
        {
            float d = Danger[team];
            if (d > _peak[team]) _peak[team] = d;
            if (d < 0.3f)
            {
                if (_peak[team] > 0.75f && t - _shotAt > 4 && m.Phase == Phase.Play) Add(React.Aww, team, 0, _peak[team]);
                _peak[team] = 0;
            }
        }
        // Time-wasting: their keeper sitting on the ball, or a slow restart of theirs.
        int held = m.HeldBy;
        bool theirs = held >= 0 && m.Team[held] == 1;
        if (!theirs) _heldSince = -1;
        else if (_heldSince < 0) _heldSince = t;
        bool slow = (theirs && t - _heldSince > 3) || (m.Phase == Phase.SetPiece && m.SetPieceTeam == 1 && m.PhaseT > 4.5f);
        if (slow && _jeeredFor < 0)
        {
            Add(React.Jeer, 0, 0, 0.6f, 4);
            _jeeredFor = t;
        }
        if (!slow) _jeeredFor = -1;
        // The teams back out: each half's first touch.
        if (m.Phase == Phase.Play && m.Half != _halfCheered && (_lastPhase == Phase.Kickoff))
        {
            _halfCheered = m.Half;
            Add(React.Cheer, 0, 0, 1);
            Add(React.Cheer, 1, 0.1, 0.8f);
            Add(React.Applause, 0, 0.2, 0.8f, 4);
        }
        // Half and full time: the home end's verdict, and the away end's.
        if ((m.Phase == Phase.Halftime || m.Phase == Phase.Fulltime) && _lastPhase != m.Phase)
        {
            bool full = m.Phase == Phase.Fulltime;
            int lead = m.Score[0] - m.Score[1];
            float k = full ? 1 : 0.7f;
            for (int end = 0; end < 2; end++)
            {
                int ahead = end == 0 ? lead : -lead;
                if (ahead > 0)
                {
                    Add(React.Cheer, end, 0.4, k);
                    Add(React.Applause, end, 0.6, k, full ? 9 : 5);
                }
                else if (ahead < 0 && end == 0)
                {
                    Add(React.Jeer, end, 0.5, k, full ? 6 : 4);
                    Add(React.Boo, end, 0.8, k);
                }
                else Add(React.Applause, end, 0.6, 0.55f * k, 4);
            }
            Add(React.Announce, -1, 4, 1, 4);
        }
        // Somebody near you shouting, or whistling: more of it when it's lively.
        if (m.Phase == Phase.Play || m.Phase == Phase.SetPiece)
        {
            float ex = m.Excitement;
            if (Rnd() < (0.12f + 0.35f * ex) * dt) Add(React.Heckle, Rnd() < 0.72f ? 0 : 1, 0, 0.5f + 0.5f * Rnd());
            if (Rnd() < (0.03f + 0.15f * Tension) * dt) Add(React.Whistler, Rnd() < 0.7f ? 0 : 1, 0, 0.6f + 0.4f * Rnd());
        }
    }

    /// <summary>A reaction, `delay` seconds from now.</summary>
    void Add(React kind, int end, double delay, float level, float dur = 0)
    {
        if (dur <= 0)
            dur = kind switch
            {
                React.Ooh or React.Groan => 2.2f,
                React.Aww => 1.6f,
                React.Boo => 2,
                React.Cheer => 1.2f,
                React.Erupt => 8,
                React.Rally => 5.5f,
                React.NameCall => 8.5f,
                React.Announce => 4,
                React.Heckle => 1,
                React.Whistler => 0.8f,
                _ => 3,
            };
        var r = new Reaction { Kind = kind, End = end, At = T + delay, Level = level, Dur = dur };
        _new.Add(r);
        if (kind != React.Heckle && kind != React.Whistler && kind != React.Announce) _live.Add(r);
    }

    /// <summary>The ends' bodies follow what they're doing.</summary>
    void Show(double t, float kk)
    {
        Span<float> clap = stackalloc float[2], jeer = stackalloc float[2], heads = stackalloc float[2], fists = stackalloc float[2];
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var r = _live[i];
            if (t >= r.At + r.Dur)
            {
                _live.RemoveAt(i);
                continue;
            }
            if (t < r.At) continue;
            float u = (float)(t - r.At), w = Math.Min(1, Math.Min(u / 0.3f, (r.Dur - u) / 0.6f)) * Math.Min(1, r.Level * 1.3f);
            for (int end = 0; end < 2; end++)
            {
                if (r.End >= 0 && r.End != end) continue;
                switch (r.Kind)
                {
                    case React.Applause:
                    case React.Rally: clap[end] = MathF.Max(clap[end], w); break;
                    case React.Boo:
                    case React.Jeer: jeer[end] = MathF.Max(jeer[end], w); break;
                    case React.Ooh:
                    case React.Groan:
                    case React.Aww: heads[end] = MathF.Max(heads[end], w); break;
                    case React.Cheer: fists[end] = MathF.Max(fists[end], w); break;
                    case React.Erupt:
                        if (u > 3.4f) clap[end] = MathF.Max(clap[end], w);
                        else fists[end] = MathF.Max(fists[end], w);
                        break;
                    case React.NameCall:
                        // Fists up on each roar of the name.
                        float c = u % 2.8f;
                        fists[end] = MathF.Max(fists[end], c > 1.9f && c < 2.7f ? w : 0);
                        break;
                }
            }
        }
        for (int end = 0; end < 2; end++)
        {
            Clapping[end] += (clap[end] - Clapping[end]) * kk;
            Jeering[end] += (jeer[end] - Jeering[end]) * kk;
            Heads[end] += (heads[end] - Heads[end]) * kk;
            Fists[end] += (fists[end] - Fists[end]) * kk;
        }
    }

    /// <summary>React to what just happened on the pitch (the frame's events; fouls and offsides name the team).</summary>
    public void OnEvents(MatchSnapshot e, int foulTeam, int offsideTeam)
    {
        double t = T;
        if (e.Goal >= 0)
        {
            int end = e.Goal, other = 1 - end;
            Add(React.Erupt, end, 0, 1);
            // Ours: the PA gives the scorer's name and the end roars it back. Theirs: the PA says
            // it flatly, the home end sits stunned, then claps its team back into it.
            if (end == 0) Add(React.NameCall, 0, 7.5, 1);
            else
            {
                Hush = 1;
                Add(React.Announce, -1, 6.5, 0.8f);
                Add(React.Rally, 0, 10, 0.8f);
            }
            Confetti.Add((end, 420));
            Danger[end] = 0;
            // The scoring end erupts: pyro all along it, a smoke bomb, then the victory song.
            for (int i = 0; i < 7; i++) Light(end, false, 10 + Rnd() * 14, t + Rnd() * 2.5);
            Light(end, true, 9, t + 0.5);
            _quiet[other] = t + 20;
            Sing(end, Rnd() < 0.5f ? Chants[1] : Chants[0], 1, 1.5);
        }
        // The end whose team was pulled up lets the referee hear it, and the other end's
        // loudest let him know it was a foul...
        if (e.Foul == 1 && foulTeam >= 0)
        {
            Add(React.Boo, foulTeam, 0.15, 1);
            if (Rnd() < 0.7f) Add(React.Heckle, 1 - foulTeam, 0.1, 1);
        }
        // ...a card: the offender's end whistles, the other cheers it.
        if (e.Card > 0 && foulTeam >= 0)
        {
            Add(React.Jeer, foulTeam, 0.4, 1, 3);
            Add(React.Cheer, 1 - foulTeam, 0.3, 0.7f);
        }
        // ...and the end whose striker was flagged gives the linesman some.
        if (e.Offside > 0 && offsideTeam >= 0) Add(React.Boo, offsideTeam, 0.15, 0.8f);
        // A save: the ground gasps, the keeper's end applauds him, the shooter's end groans.
        if (e.Save > 0.5f)
        {
            int shooter = _shotTeam >= 0 ? _shotTeam : e.LastTouchTeam >= 0 ? 1 - e.LastTouchTeam : 1;
            Add(React.Ooh, -1, 0, 1);
            Add(React.Groan, shooter, 0.6, 0.6f);
            Add(React.Applause, 1 - shooter, 1.1, 0.85f, 3.5f);
            _shotTeam = -1;
        }
        if (e.Post > 0) Add(React.Ooh, -1, 0, 1);
        // A crunching, clean tackle: the tackler's end gets up for it.
        if (e.Tackle >= 1 && e.LastTouchTeam >= 0 && Rnd() < (e.LastTouchTeam == 0 ? 0.55f : 0.3f))
        {
            int end = e.LastTouchTeam;
            Add(React.Cheer, end, 0.15, end == 0 ? 0.7f : 0.5f);
            Add(React.Applause, end, 0.4, 0.5f, 2.2f);
        }
    }

    /// <summary>This frame for the audio: the clock, the song, and the reactions (drained).</summary>
    public TerraceCue TakeCue()
    {
        var c = new TerraceCue
        {
            T = T, Singing = Singing, Level = Singing?.Level ?? 0,
            Reactions = _new.Count > 0 ? _new.ToArray() : Array.Empty<Reaction>(),
        };
        _new.Clear();
        return c;
    }

    /// <summary>A song to suit the game: cruising, they mock ("olé"); behind, they dig in.</summary>
    void StartSong(int end, MatchSnapshot m)
    {
        int lead = m.Score[end] - m.Score[1 - end];
        Chant Pick(params string[] names) => Array.Find(Chants, c => c.Name == names[(int)(Rnd() * names.Length)]);
        var chant = end == 1 ? Pick("away", "away", "riff", "allez", "claps", "name")
            : lead >= 2 ? Pick("ole", "ole", "riff", "claps", "bounce")
            : lead < 0 ? Pick("anthem", "allez", "allez", "claps", "capo")
            : Pick("riff", "ole", "allez", "claps", "anthem", "capo", "capo", "name", "bounce");
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
