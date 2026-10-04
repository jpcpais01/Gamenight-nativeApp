using System;
using System.Collections.Generic;

namespace GameNight.Audio;

/// <summary>
/// The crowd as a soundtrack that follows the match. Each end is a stack of continuous layers
/// from the bank (people talking, an excited buzz, the roar, applause, whistling and booing),
/// every layer read by two heads at slightly different speeds so the loops never repeat the
/// same way; the levels glide between them with the mood (each at its own pace: a roar comes up
/// fast and ebbs slowly), and the buzz and roar climb in pitch as their team closes on goal. The
/// side stands are a wide, mixed version of both ends. Songs rise out of an end in time with the
/// director's beat and sink back into it; moments (an "ooh", a groan, a goal's eruption, a cheer,
/// the PA and the scorer's name) play on top. Runs on the audio thread, a block at a time.
/// </summary>
public sealed class CrowdScore
{
    readonly Mixer _mx;
    readonly Random _rng = new();
    CrowdBank _bank;
    float _ready;

    /// <summary>The whole crowd's level (1 at a match; the menus keep it low).</summary>
    public float Level = 1;
    float _level = 1;

    struct Head
    {
        public double Pos;
        public float Rate;
    }

    /// <summary>One end's layers: two heads each, the level now, and where it's heading.</summary>
    sealed class Stack
    {
        public readonly Head[,] Heads = new Head[5, 2];
        public readonly float[] Gain = new float[5], Want = new float[5];
        public float Lift;
        public float Boost;
    }

    readonly Stack[] _end = { new(), new() };
    readonly Stack _stands = new();

    /// <summary>A stretch of applause or jeering (audio time) at a level.</summary>
    struct Spell
    {
        public double From, To;
        public float Level;
    }

    readonly List<Spell>[,] _spells = { { new(), new() }, { new(), new() } };

    sealed class Song
    {
        public float[] Buf;
        public int End, Id;
        public double Start, Until;
        public float Level;
    }

    readonly Song[] _songs = new Song[2];

    /// <summary>A moment playing: to an end (0, 1) or the stands (2, panned).</summary>
    sealed class Play
    {
        public float[] Buf;
        public double Start;
        public float Gain, PanL = 1, PanR = 1;
        public int To;
    }

    readonly List<Play> _plays = new();

    // The mood, from the director.
    readonly float[] _danger = new float[2];
    float _ex, _hush;

    public CrowdScore(Mixer mx)
    {
        _mx = mx;
        foreach (var s in new[] { _end[0], _end[1], _stands })
            for (int l = 0; l < 5; l++)
            {
                s.Heads[l, 0] = new Head { Pos = _rng.NextDouble() * 1e5, Rate = 1 };
                s.Heads[l, 1] = new Head { Pos = _rng.NextDouble() * 1e5, Rate = 0.983f + (float)_rng.NextDouble() * 0.01f };
            }
        _end[1].Heads[0, 0].Rate = 1.02f;
    }

    public bool Ready => _bank != null;

    /// <summary>The bank's baked: the crowd fades in.</summary>
    public void Use(CrowdBank bank) => _bank = bank;

    public float[] Take(Shot s) => _bank?.Shots[(int)s] is { Length: > 0 } a ? a[_rng.Next(a.Length)] : null;

    float Rand() => (float)_rng.NextDouble();

    // ------------------------------------------------------------------ cues

    /// <summary>One frame of the director: the mood, the song, the reactions.</summary>
    public void Cue(TerraceCue c)
    {
        double now = _mx.Now;
        double toCtx = now + 0.06 - c.T;
        _danger[0] = c.Danger0;
        _danger[1] = c.Danger1;
        _ex = c.Excitement;
        _hush = c.Hush;
        foreach (var r in c.Reactions) React(r, Math.Max(now + 0.02, r.At + toCtx));
        Sing(c, toCtx);
    }

    void Sing(TerraceCue c, double toCtx)
    {
        var s = c.Singing;
        if (s == null || _bank == null) return;
        if (s.Chant == null)
        {
            Viking(s, c.T, toCtx);
            return;
        }
        var cur = _songs[s.End];
        if (cur == null || cur.Id != s.Id)
        {
            var songs = _bank.Songs[s.End];
            if (!songs.TryGetValue(s.Chant.Name, out var buf) && !songs.TryGetValue(s.End == 1 ? "away" : "riff", out buf)) return;
            _songs[s.End] = cur = new Song { Buf = buf, End = s.End, Id = s.Id, Start = s.Start + toCtx };
        }
        cur.Until = s.Until + toCtx;
        cur.Level = c.Level;
    }

    double _vikingTo;
    int _vikingId = -1;

    /// <summary>The Viking clap: the "HUH!" on each boom, and the roar after the last.</summary>
    void Viking(Singing s, double dirT, double toCtx)
    {
        if (s.Id != _vikingId)
        {
            _vikingId = s.Id;
            _vikingTo = s.Start;
        }
        double horizon = dirT + 0.3, from = Math.Max(_vikingTo, dirT - 0.05);
        if (horizon <= from) return;
        _vikingTo = horizon;
        for (int i = 0; i < s.Booms.Length; i++)
        {
            double b = s.Booms[i];
            if (b <= from || b > horizon) continue;
            float lv = 0.7f + 0.3f * i / s.Booms.Length;
            Shoot(Shot.Huh, s.End, b + toCtx, lv);
            Shoot(s.End == 0 ? Shot.Drum : Shot.AwayDrum, s.End, b + toCtx, 0.9f);
        }
        double roar = s.Booms[^1] + 0.5;
        if (roar > from && roar <= horizon)
        {
            Shoot(Shot.Cheer, s.End, roar + toCtx, 1);
            Shoot(Shot.Erupt, s.End, roar + toCtx + 0.1, 0.55f);
        }
    }

    /// <summary>A moment, to an end (or both, end -1).</summary>
    void Shoot(Shot s, int end, double t, float gain)
    {
        if (end < 0)
        {
            Shoot(s, 0, t, gain);
            Shoot(s, 1, t + 0.04, gain * 0.9f);
            return;
        }
        var buf = Take(s);
        if (buf != null) _plays.Add(new Play { Buf = buf, Start = t, Gain = gain, To = end });
    }

    /// <summary>A moment in the stands at large, placed from left (-1) to right (1).</summary>
    void Stands(float[] buf, double t, float gain, float pan)
    {
        if (buf == null) return;
        float x = (Math.Clamp(pan, -1, 1) + 1) * MathF.PI / 4;
        _plays.Add(new Play { Buf = buf, Start = t, Gain = gain, To = 2, PanL = MathF.Cos(x) * 1.414f, PanR = MathF.Sin(x) * 1.414f });
    }

    /// <summary>The PA: the same sound from the near speakers, then later and fainter from across the ground.</summary>
    void Pa(float[] buf, double t, float gain)
    {
        Stands(buf, t, gain, 0);
        Stands(buf, t + 0.21, gain * 0.45f, -0.7f);
        Stands(buf, t + 0.47, gain * 0.28f, 0.7f);
    }

    void AddSpell(int layer, int end, double from, double dur, float level)
    {
        for (int e = 0; e < 2; e++)
            if (end < 0 || end == e) _spells[e, layer == (int)Layer.Applause ? 0 : 1].Add(new Spell { From = from, To = from + dur, Level = level });
    }

    void React(Reaction r, double t)
    {
        int end = r.End;
        float lv = r.Level;
        switch (r.Kind)
        {
            case Audio.React.Ooh: Shoot(Shot.Ooh, end, t, lv); break;
            case Audio.React.Groan: Shoot(Shot.Groan, end, t, lv); break;
            case Audio.React.Aww: Shoot(Shot.Aww, end, t, lv * 0.8f); break;
            case Audio.React.Applause: AddSpell((int)Layer.Applause, end, t, r.Dur, lv); break;
            case Audio.React.Boo:
                Shoot(Shot.Boo, end, t, lv);
                AddSpell((int)Layer.Jeer, end, t + 0.3, 2, lv * 0.5f);
                break;
            case Audio.React.Jeer:
                AddSpell((int)Layer.Jeer, end, t, r.Dur, lv);
                Shoot(Shot.Boo, end, t + 0.4, lv * 0.6f);
                break;
            case Audio.React.Cheer: Shoot(Shot.Cheer, end, t, lv); break;
            case Audio.React.Erupt:
            {
                int e = Math.Max(0, end);
                Shoot(Shot.Erupt, e, t, 1.8f);
                _goalAt[e] = t;
                AddSpell((int)Layer.Applause, e, t + 2.5, 7, 0.8f);
                Shoot(Shot.Groan, 1 - e, t + 0.3, 0.7f);
                break;
            }
            case Audio.React.Rally:
                Shoot(Shot.Rally, end, t, lv);
                AddSpell((int)Layer.Applause, end, t + 4, 3, 0.5f * lv);
                break;
            case Audio.React.NameCall: NameCall(Math.Max(0, end), t, lv); break;
            case Audio.React.Announce:
                Pa(Take(Shot.Chime), t, 0.5f * lv);
                Pa(Take(Shot.Announce), t + 1.5, 0.6f * lv);
                break;
            case Audio.React.Heckle:
            {
                // A small ground near the pitch: you hear the people round you; a big bowl swallows them.
                var v = _mx.Venue;
                if (Rand() < 1.1f - 0.5f * v.Size)
                    Stands(Take(Shot.Heckle), t, lv * (0.35f + 0.5f * v.Near), (end == 0 ? -0.35f : 0.35f) + (Rand() - 0.5f) * 1.2f);
                break;
            }
            case Audio.React.Whistler:
                Stands(Take(Shot.Whistle), t, lv * 0.5f, (end == 0 ? -0.4f : 0.4f) + (Rand() - 0.5f));
                break;
            case Audio.React.Laugh: Shoot(Shot.Laugh, end, t, lv); break;
            case Audio.React.Ole: Shoot(Shot.Ole, end, t, lv); break;
        }
    }

    readonly double[] _goalAt = { -1e9, -1e9 };

    /// <summary>After our goal: the chime, then three times the announcer calls the scorer's
    /// first name and the end roars his surname back, the drum on it.</summary>
    void NameCall(int end, double t, float lv)
    {
        if (_bank == null) return;
        var surname = Take(Shot.Surname);
        Pa(Take(Shot.Chime), t, 0.5f * lv);
        double at = t + 1.4;
        for (int i = 0; i < 3; i++)
        {
            var call = Take(Shot.Name);
            if (call == null) return;
            Pa(call, at, 0.65f * lv);
            double roar = at + Voiced(call) + 0.08;
            if (surname != null) _plays.Add(new Play { Buf = surname, Start = roar, Gain = (0.8f + 0.15f * i) * lv, To = end });
            Shoot(Shot.Drum, end, roar, 0.9f);
            Shoot(Shot.Drum, end, roar + 0.27, 0.9f);
            at = roar + 1.25;
        }
        AddSpell((int)Layer.Applause, end, at - 0.5, 4, 0.8f * lv);
    }

    /// <summary>Where a moment's sound ends (seconds).</summary>
    static double Voiced(float[] b)
    {
        float peak = 0;
        foreach (float v in b) peak = MathF.Max(peak, MathF.Abs(v));
        int i = b.Length - 1;
        while (i > 0 && MathF.Abs(b[i]) < peak * 0.05f) i--;
        return i / CrowdBank.Rate;
    }

    /// <summary>Thunder overhead: a startled "whoa", whistles, then a cheer as if they'd ordered it.</summary>
    public void Thunder(double t, float near)
    {
        Shoot(Shot.Ooh, -1, t, 0.8f * near);
        for (int i = 0; i < 4; i++) Stands(Take(Shot.Whistle), t + 0.6 + Rand() * 1.5, 0.5f * near, (Rand() - 0.5f) * 1.6f);
        Shoot(Shot.Cheer, 0, t + 1.3, 0.6f * near);
    }

    // ------------------------------------------------------------------ the mood

    static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    static float SpellLevel(List<Spell> list, double t)
    {
        float v = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var s = list[i];
            if (t > s.To + 3)
            {
                list.RemoveAt(i);
                continue;
            }
            if (t >= s.From && t < s.To) v = MathF.Max(v, s.Level);
        }
        return v;
    }

    /// <summary>What each layer of each end should be at now.</summary>
    void Mood(double t)
    {
        for (int e = 0; e < 2; e++)
        {
            var s = _end[e];
            float own = _danger[e], other = _danger[1 - e];
            float hush = e == 0 ? _hush : 0;
            // A goal: the roar held through the celebration, ebbing over several seconds.
            float since = (float)(t - _goalAt[e]);
            s.Boost = since >= 0 && since < 14 ? MathF.Exp(-MathF.Max(0, since - 3) / 3.5f) : 0;
            var song = _songs[e];
            float sing = song != null && t >= song.Start && t < song.Until ? song.Level : 0;
            float buzz = Math.Clamp(0.05f + 0.3f * _ex + 0.85f * own + 0.25f * other, 0, 1);
            float roar = Math.Max(Smooth(0.45f, 0.95f, own) * 1.1f, 1.5f * s.Boost);
            float murmur = 0.12f + 0.42f * (1 - 0.7f * buzz);
            murmur *= (1 - 0.6f * sing) * (1 - 0.4f * hush) * (1 - 0.6f * s.Boost);
            buzz *= (1 - 0.5f * sing) * (1 - 0.85f * hush) * (1 - 0.5f * s.Boost);
            roar *= 1 - hush;
            s.Want[(int)Layer.Murmur] = murmur;
            s.Want[(int)Layer.Buzz] = buzz * 0.8f;
            s.Want[(int)Layer.Roar] = roar;
            s.Want[(int)Layer.Applause] = SpellLevel(_spells[e, 0], t);
            s.Want[(int)Layer.Jeer] = SpellLevel(_spells[e, 1], t);
            // The buzz and the roar climb as the chance does.
            s.Lift = 1 + 0.06f * own + 0.04f * s.Boost;
        }
        // The side stands: both lots, mostly home, in their talk, buzz and roar.
        for (int l = 0; l < 3; l++) _stands.Want[l] = 0.65f * _end[0].Want[l] + 0.35f * _end[1].Want[l];
        _stands.Want[(int)Layer.Applause] = 0.5f * (_end[0].Want[(int)Layer.Applause] + _end[1].Want[(int)Layer.Applause]);
        _stands.Want[(int)Layer.Jeer] = 0;
        _stands.Lift = MathF.Max(_end[0].Lift, _end[1].Lift);
    }

    /// <summary>How fast each layer moves toward where it's heading (seconds: up, down).</summary>
    static readonly (float up, float down)[] Pace = { (1.2f, 1.2f), (0.4f, 1.5f), (0.25f, 1.8f), (0.3f, 1.4f), (0.3f, 1.2f) };

    // ------------------------------------------------------------------ render

    readonly float[] _g0 = new float[5], _g1 = new float[5];

    /// <summary>Mix one block into the ends' buses and the stands (stereo).</summary>
    public void Render(float[] e0, float[] e1, float[] wl, float[] wr, double t0, float sr)
    {
        if (_bank == null) return;
        int n = e0.Length;
        float block = n / sr;
        _ready = MathF.Min(1, _ready + block / 2.5f);
        _level += (Level - _level) * (1 - MathF.Exp(-block / 0.4f));
        float master = _ready * _level * 0.5f;
        Mood(t0);
        double step = CrowdBank.Rate / sr;
        RenderStack(_end[0], e0, null, t0, n, sr, step, master);
        RenderStack(_end[1], e1, null, t0, n, sr, step, master);
        RenderStack(_stands, wl, wr, t0, n, sr, step, master * 0.55f);

        // The songs, in time with the director.
        for (int e = 0; e < 2; e++)
        {
            var s = _songs[e];
            if (s == null) continue;
            if (t0 > s.Until + 0.5)
            {
                _songs[e] = null;
                continue;
            }
            var outBuf = e == 0 ? e0 : e1;
            var buf = s.Buf;
            int len = buf.Length;
            for (int i = 0; i < n; i++)
            {
                double t = t0 + i / sr;
                if (t < s.Start || t >= s.Until) continue;
                // In and out gently: the end joins in and peters out.
                float g = (float)Math.Min(1, Math.Min((t - s.Start) / 2.5 + 0.25, (s.Until - t) / 2)) * s.Level * master * 1.1f;
                double p = (t - s.Start) * CrowdBank.Rate % len;
                int j = (int)p;
                float f = (float)(p - j);
                outBuf[i] += (buf[j] * (1 - f) + buf[(j + 1) % len] * f) * g;
            }
        }

        // The moments.
        for (int k = _plays.Count - 1; k >= 0; k--)
        {
            var p = _plays[k];
            var buf = p.Buf;
            int first = (int)Math.Ceiling((p.Start - t0) * sr);
            if (first >= n) continue;
            float g = p.Gain * master;
            bool done = false;
            for (int i = Math.Max(0, first); i < n; i++)
            {
                double pos = (t0 + i / sr - p.Start) * CrowdBank.Rate;
                int j = (int)pos;
                if (j + 1 >= buf.Length)
                {
                    done = true;
                    break;
                }
                float f = (float)(pos - j);
                float v = (buf[j] * (1 - f) + buf[j + 1] * f) * g;
                if (p.To == 0) e0[i] += v;
                else if (p.To == 1) e1[i] += v;
                else
                {
                    wl[i] += v * p.PanL;
                    wr[i] += v * p.PanR;
                }
            }
            if (done)
            {
                _plays[k] = _plays[^1];
                _plays.RemoveAt(_plays.Count - 1);
            }
        }
    }

    /// <summary>A stack's layers into a mono bus (or a stereo pair: the two heads go one each side).</summary>
    void RenderStack(Stack s, float[] l, float[] r, double t0, int n, float sr, double step, float master)
    {
        float block = n / sr;
        for (int k = 0; k < 5; k++)
        {
            _g0[k] = s.Gain[k];
            var (up, down) = Pace[k];
            float tau = s.Want[k] > s.Gain[k] ? up : down;
            s.Gain[k] += (s.Want[k] - s.Gain[k]) * (1 - MathF.Exp(-block / tau));
            _g1[k] = s.Gain[k];
        }
        for (int k = 0; k < 5; k++)
        {
            if (_g0[k] < 1e-4f && _g1[k] < 1e-4f) continue;
            var buf = _bank.Loops[k];
            int len = buf.Length;
            float lift = k == (int)Layer.Buzz || k == (int)Layer.Roar ? s.Lift : 1;
            float a = _g0[k] * master, da = (_g1[k] - _g0[k]) * master / n;
            // Two heads, a little apart in speed, so the loop never comes round the same.
            double p0 = s.Heads[k, 0].Pos, p1 = s.Heads[k, 1].Pos;
            double s0 = step * s.Heads[k, 0].Rate * lift, s1 = step * s.Heads[k, 1].Rate * lift;
            for (int i = 0; i < n; i++)
            {
                p0 %= len;
                p1 %= len;
                int j0 = (int)p0, j1 = (int)p1;
                float f0 = (float)(p0 - j0), f1 = (float)(p1 - j1);
                float v0 = buf[j0] * (1 - f0) + buf[(j0 + 1) % len] * f0;
                float v1 = buf[j1] * (1 - f1) + buf[(j1 + 1) % len] * f1;
                if (r == null) l[i] += (v0 + v1) * 0.7f * a;
                else
                {
                    l[i] += (v0 + v1 * 0.35f) * a;
                    r[i] += (v1 + v0 * 0.35f) * a;
                }
                p0 += s0;
                p1 += s1;
                a += da;
            }
            s.Heads[k, 0].Pos = p0;
            s.Heads[k, 1].Pos = p1;
        }
    }
}
