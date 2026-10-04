using System;
using System.Collections.Generic;

namespace GameNight.Audio;

/// <summary>The crowd's continuous layers, each a seamless loop the live mix fades between.</summary>
public enum Layer { Murmur, Buzz, Roar, Applause, Jeer }

/// <summary>The crowd's moments, each a one-shot (a few takes of each).</summary>
public enum Shot { Ooh, Groan, Aww, Erupt, Cheer, Boo, Huh, Laugh, Ole, Surname, Rally, Heckle, Whistle, Announce, Name, Chime, Drum, AwayDrum }

/// <summary>
/// Everything the crowd can sound like, baked once (on a background thread at start-up) from
/// dozens of synthesised voices each: the continuous layers (people talking, an excited buzz,
/// a roar, applause, whistling and booing) as seamless loops; the moments (an "ooh", a groan,
/// a goal's eruption, a cheer, a laugh, the scorer's name, the PA...) in a few takes each;
/// and every song both ends sing, as a loop in time with the director's beat. The live mix
/// (<see cref="CrowdScore"/>) only plays these back, so it costs almost nothing per frame.
/// </summary>
public sealed class CrowdBank
{
    public const float Rate = 22050;
    const float LoopSeconds = 10;

    public readonly float[][] Loops = new float[5][];
    public readonly float[][][] Shots = new float[Enum.GetValues<Shot>().Length][][];
    /// <summary>Each end's songs by chant name.</summary>
    public readonly Dictionary<string, float[]>[] Songs = { new(), new() };
    public double BakeMs;

    /// <summary>The keys the ends sing in (semitones from the root): the away end pitches its own.</summary>
    public static readonly float[] Key = { 0, 2 };
    const float Root = 146.8f;

    static readonly string[] AwaySongs = { "riff", "allez", "claps", "name", "away" };

    public static CrowdBank Bake()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var b = new CrowdBank();
        var jobs = new List<Action>();
        uint seed = 1;
        void Job(Action<Rng> a)
        {
            var r = new Rng(seed++ * 7919);
            jobs.Add(() => a(r));
        }

        // The layers.
        Job(r => b.Loops[(int)Layer.Murmur] = Murmur(r));
        Job(r => b.Loops[(int)Layer.Buzz] = Buzz(r));
        Job(r => b.Loops[(int)Layer.Roar] = Roar(r));
        Job(r => b.Loops[(int)Layer.Applause] = Applause(r));
        Job(r => b.Loops[(int)Layer.Jeer] = Jeer(r));

        // The moments, a few takes of each.
        void Takes(Shot s, int n, Func<Rng, float[]> make)
        {
            b.Shots[(int)s] = new float[n][];
            for (int i = 0; i < n; i++)
            {
                int k = i;
                Job(r => b.Shots[(int)s][k] = make(r));
            }
        }
        Takes(Shot.Ooh, 3, Ooh);
        Takes(Shot.Groan, 2, Groan);
        Takes(Shot.Aww, 2, Aww);
        Takes(Shot.Erupt, 2, Erupt);
        Takes(Shot.Cheer, 3, Cheer);
        Takes(Shot.Boo, 2, Boo);
        Takes(Shot.Huh, 2, Huh);
        Takes(Shot.Laugh, 2, Laugh);
        Takes(Shot.Ole, 2, Ole);
        Takes(Shot.Surname, 3, Surname);
        Takes(Shot.Rally, 1, Rally);
        Takes(Shot.Heckle, 10, Heckle);
        Takes(Shot.Whistle, 6, r => { var o = new float[(int)(1.3f * Rate)]; CrowdSynth.Whistle(o, Rate, 0, false, r, 0.25f); return o; });
        Takes(Shot.Announce, 3, r => Announcer(r, false));
        Takes(Shot.Name, 3, r => Announcer(r, true));
        Takes(Shot.Chime, 1, Chime);
        Takes(Shot.Drum, 1, _ => { var o = new float[(int)(0.6f * Rate)]; CrowdSynth.Drum(o, Rate, 0, false, 95, 0.5f); return o; });
        Takes(Shot.AwayDrum, 1, _ => { var o = new float[(int)(0.6f * Rate)]; CrowdSynth.Drum(o, Rate, 0, false, 125, 0.4f); return o; });

        // The songs.
        foreach (var c in Terraces.Chants)
            for (int end = 0; end < 2; end++)
            {
                if (end == 1 && Array.IndexOf(AwaySongs, c.Name) < 0) continue;
                if (end == 0 && c.Name == "away") continue;
                var chant = c;
                int e = end;
                Job(r =>
                {
                    var buf = Song(r, chant, e);
                    lock (b.Songs) b.Songs[e][chant.Name] = buf;
                });
            }

        // On a few low-priority threads, so the game and its menus keep their pace meanwhile.
        var queue = new System.Collections.Concurrent.ConcurrentQueue<Action>(jobs);
        var threads = new System.Threading.Thread[Math.Max(1, Environment.ProcessorCount - 2)];
        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new System.Threading.Thread(() =>
            {
                while (queue.TryDequeue(out var j)) j();
            }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "Crowd bake" };
            threads[i].Start();
        }
        foreach (var t in threads) t.Join();
        b.BakeMs = sw.Elapsed.TotalMilliseconds;
        return b;
    }

    /// <summary>Bump when the bake changes, so a saved bank from before is baked again.</summary>
    public const int Version = 1;

    /// <summary>Save the bank (raw floats), so the next launch loads it instead of baking.</summary>
    public void Write(System.IO.Stream to)
    {
        using var w = new System.IO.BinaryWriter(to);
        w.Write(Version);
        void Buf(float[] b)
        {
            w.Write(b.Length);
            var bytes = new byte[b.Length * 4];
            Buffer.BlockCopy(b, 0, bytes, 0, bytes.Length);
            w.Write(bytes);
        }
        foreach (var l in Loops) Buf(l);
        foreach (var takes in Shots)
        {
            w.Write(takes.Length);
            foreach (var b in takes) Buf(b);
        }
        foreach (var songs in Songs)
        {
            w.Write(songs.Count);
            foreach (var kv in songs)
            {
                w.Write(kv.Key);
                Buf(kv.Value);
            }
        }
    }

    /// <summary>A saved bank, or null if it's from another version (or damaged).</summary>
    public static CrowdBank Read(System.IO.Stream from)
    {
        try
        {
            using var rd = new System.IO.BinaryReader(from);
            if (rd.ReadInt32() != Version) return null;
            float[] Buf()
            {
                int n = rd.ReadInt32();
                var bytes = rd.ReadBytes(n * 4);
                if (bytes.Length != n * 4) throw new System.IO.EndOfStreamException();
                var b = new float[n];
                Buffer.BlockCopy(bytes, 0, b, 0, bytes.Length);
                return b;
            }
            var bank = new CrowdBank();
            for (int i = 0; i < bank.Loops.Length; i++) bank.Loops[i] = Buf();
            for (int i = 0; i < bank.Shots.Length; i++)
            {
                var takes = new float[rd.ReadInt32()][];
                for (int k = 0; k < takes.Length; k++) takes[k] = Buf();
                bank.Shots[i] = takes;
            }
            foreach (var songs in bank.Songs)
            {
                int n = rd.ReadInt32();
                for (int k = 0; k < n; k++) songs[rd.ReadString()] = Buf();
            }
            return bank;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static float[] Seconds(float s) => new float[(int)(s * Rate)];
    static int At(float s) => (int)(s * Rate);

    // ------------------------------------------------------------------ the layers

    /// <summary>Tens of thousands talking among themselves.</summary>
    static float[] Murmur(Rng r)
    {
        var o = Seconds(LoopSeconds);
        for (int i = 0; i < 70; i++)
        {
            float hz = CrowdSynth.BaseHz(r, out bool w);
            CrowdSynth.Voice(o, Rate, 0, true, CrowdSynth.Talking(r, LoopSeconds, hz, r.R(0.85f, 1.1f), 1, 1), Tone.Talk(r, w), r, r.R(0.35f, 1));
        }
        CrowdSynth.Normalise(o, 0.1f);
        return o;
    }

    /// <summary>The ground on its toes: everyone talking faster and higher, shouts breaking through.</summary>
    static float[] Buzz(Rng r)
    {
        var o = Seconds(LoopSeconds);
        for (int i = 0; i < 75; i++)
        {
            float hz = CrowdSynth.BaseHz(r, out bool w);
            var tone = Tone.Shout(r, w);
            tone.Bright *= 0.7f;
            var segs = i % 3 == 0
                ? CrowdSynth.Shouting(r, LoopSeconds, hz, r.R(1.4f, 1.7f), 0.7f, 2.2f)
                : CrowdSynth.Talking(r, LoopSeconds, hz, r.R(1.2f, 1.5f), r.R(1.2f, 1.45f), 1);
            CrowdSynth.Voice(o, Rate, 0, true, segs, tone, r, r.R(0.35f, 1));
        }
        CrowdSynth.Normalise(o, 0.1f);
        return o;
    }

    /// <summary>The whole stand roaring its team on: open-throated, high, never stopping.</summary>
    static float[] Roar(Rng r)
    {
        var o = Seconds(LoopSeconds);
        for (int i = 0; i < 85; i++)
        {
            float hz = CrowdSynth.BaseHz(r, out bool w);
            CrowdSynth.Voice(o, Rate, 0, true, CrowdSynth.Shouting(r, LoopSeconds, hz, r.R(1.75f, 2.2f), 1, 0.35f), Tone.Shout(r, w), r, r.R(0.35f, 1));
        }
        CrowdSynth.Normalise(o, 0.1f);
        return o;
    }

    /// <summary>Applause: a hundred and sixty pairs of hands, each at its own pace.</summary>
    static float[] Applause(Rng r)
    {
        var o = Seconds(LoopSeconds);
        for (int i = 0; i < 160; i++)
        {
            float rate = r.R(3.5f, 6), g = r.R(0.3f, 1), far = r.R(2000, 7000);
            for (float t = r.F() / rate; t < LoopSeconds; t += 1 / rate * (1 + r.N() * 0.06f))
                CrowdSynth.Clap(o, Rate, At(t), true, r, g * r.R(0.7f, 1), far);
        }
        CrowdSynth.Normalise(o, 0.1f);
        return o;
    }

    /// <summary>Jeering: whistling everywhere, and a low boo rolling under it.</summary>
    static float[] Jeer(Rng r)
    {
        var o = Seconds(LoopSeconds);
        for (int i = 0; i < 35; i++)
            for (float t = r.R(-1, 1); t < LoopSeconds; t += r.R(0.8f, 2.2f))
                CrowdSynth.Whistle(o, Rate, At(t), true, r, r.R(0.08f, 0.25f));
        for (int i = 0; i < 45; i++)
        {
            float hz = CrowdSynth.BaseHz(r, out bool w) * r.R(0.95f, 1.15f);
            var segs = new List<Seg>();
            for (float t = r.R(-2, 0); t < LoopSeconds; t += r.R(1.4f, 3))
                segs.Add(Seg.Note(t, r.R(1, 2.4f), hz, hz * r.R(0.9f, 1), Vowel.U, r.R(0.6f, 1), 0.15f, 0.4f));
            CrowdSynth.Voice(o, Rate, 0, true, segs, Tone.Shout(r, w), r, r.R(0.4f, 1));
        }
        CrowdSynth.Normalise(o, 0.1f);
        return o;
    }

    // ------------------------------------------------------------------ the moments

    /// <summary>`n` voices, each given its segments by `make` (start offset, base pitch), shouting.</summary>
    static void Many(float[] o, Rng r, int n, Func<Rng, float, List<Seg>> make, float bright = 1, float loud = 1)
    {
        for (int i = 0; i < n; i++)
        {
            float hz = CrowdSynth.BaseHz(r, out bool w);
            var tone = Tone.Shout(r, w);
            tone.Bright *= bright;
            CrowdSynth.Voice(o, Rate, 0, false, make(r, hz), tone, r, r.R(0.35f, 1) * loud);
        }
    }

    static List<Seg> L(params Seg[] s) => new(s);

    /// <summary>"Ooooh!": up with the chance, and down as it goes.</summary>
    static float[] Ooh(Rng r)
    {
        var o = Seconds(2.8f);
        Many(o, r, 55, (r, b) =>
        {
            float t = r.R(0, 0.12f), hz = b * r.R(1.45f, 1.9f);
            return L(Seg.Note(t, 0.4f, hz * 0.85f, hz * 1.22f, Vowel.U, 1, 0.12f, 0.06f),
                Seg.Note(t + 0.38f, r.R(1, 1.6f), hz * 1.2f, hz * 0.8f, r.Pick(Vowel.O, Vowel.U), 0.85f, 0.05f, 0.6f));
        }, 0.6f);
        CrowdSynth.Normalise(o, 0.13f);
        return o;
    }

    /// <summary>A shot just wide: "aaah-ohhh", sinking.</summary>
    static float[] Groan(Rng r)
    {
        var o = Seconds(2.6f);
        Many(o, r, 50, (r, b) =>
        {
            float t = r.R(0, 0.1f), hz = b * r.R(1.7f, 2.1f);
            return L(Seg.Note(t, 0.45f, hz, hz * 0.85f, Vowel.A, 1, 0.06f, 0.06f),
                Seg.Note(t + 0.43f, r.R(0.9f, 1.4f), hz * 0.85f, hz * 0.55f, Vowel.O, 0.8f, 0.05f, 0.6f));
        }, 0.7f);
        CrowdSynth.Normalise(o, 0.12f);
        return o;
    }

    /// <summary>An attack that came to nothing: a short, deflating "aww".</summary>
    static float[] Aww(Rng r)
    {
        var o = Seconds(1.8f);
        Many(o, r, 40, (r, b) =>
        {
            float t = r.R(0, 0.15f), hz = b * r.R(1.5f, 1.8f);
            return L(Seg.Note(t, r.R(0.6f, 0.9f), hz, hz * 0.7f, Vowel.A, 1, 0.08f, 0.4f));
        }, 0.6f);
        CrowdSynth.Normalise(o, 0.1f);
        return o;
    }

    /// <summary>A goal: the stand explodes, screaming itself hoarse, whistling, then applauding.</summary>
    static float[] Erupt(Rng r)
    {
        var o = Seconds(7);
        Many(o, r, 80, (r, b) =>
        {
            var s = new List<Seg>();
            float t = r.R(0, 0.3f), lv = 1;
            for (int k = 0; k < 3 && t < 6; k++)
            {
                float d = r.R(1.2f, 2.6f) * (k == 0 ? 1 : 0.7f), hz = b * r.R(2.0f, 2.5f) * (k == 0 ? 1 : 0.93f);
                s.Add(Seg.Note(t, d, hz * 0.9f, hz * r.R(1.02f, 1.12f), r.Pick(Vowel.A, Vowel.A, Vowel.E, Vowel.O), lv, 0.04f, 0.3f));
                t += d + r.R(0.1f, 0.45f);
                lv *= 0.7f;
            }
            return s;
        }, 1.2f);
        for (int i = 0; i < 14; i++) CrowdSynth.Whistle(o, Rate, At(r.R(0.3f, 5)), false, r, r.R(0.05f, 0.15f));
        for (int i = 0; i < 120; i++)
        {
            float rate = r.R(4, 6), g = r.R(0.15f, 0.5f), far = r.R(2000, 6000);
            for (float t = r.R(1.5f, 3); t < 7; t += 1 / rate) CrowdSynth.Clap(o, Rate, At(t), false, r, g * (1 - t / 8), far);
        }
        CrowdSynth.Normalise(o, 0.15f);
        return o;
    }

    /// <summary>"YEAH!": the end on its feet for a moment.</summary>
    static float[] Cheer(Rng r)
    {
        var o = Seconds(1.6f);
        Many(o, r, 45, (r, b) =>
        {
            float t = r.R(0, 0.1f), hz = b * r.R(1.9f, 2.3f);
            return L(Seg.Note(t, 0.22f, hz, hz * 1.08f, Vowel.E, 1, 0.03f, 0.04f),
                Seg.Note(t + 0.2f, r.R(0.35f, 0.6f), hz * 1.08f, hz * 0.85f, Vowel.A, 0.9f, 0.03f, 0.35f));
        }, 1.1f);
        for (int i = 0; i < 4; i++) CrowdSynth.Whistle(o, Rate, At(r.R(0.1f, 0.6f)), false, r, r.R(0.05f, 0.12f));
        CrowdSynth.Normalise(o, 0.13f);
        return o;
    }

    /// <summary>A long, low boo, the whistlers over it.</summary>
    static float[] Boo(Rng r)
    {
        var o = Seconds(3);
        Many(o, r, 55, (r, b) =>
        {
            float t = r.R(0, 0.25f), hz = b * r.R(0.95f, 1.15f);
            return L(Seg.Note(t, r.R(1.4f, 2.3f), hz, hz * r.R(0.88f, 0.98f), Vowel.U, 1, 0.2f, 0.4f));
        }, 0.6f);
        for (int i = 0; i < 8; i++) CrowdSynth.Whistle(o, Rate, At(r.R(0.1f, 1.6f)), false, r, r.R(0.04f, 0.1f));
        CrowdSynth.Normalise(o, 0.12f);
        return o;
    }

    /// <summary>The Viking clap's "HUH!" and the clap with it.</summary>
    static float[] Huh(Rng r)
    {
        var o = Seconds(0.9f);
        Many(o, r, 50, (r, b) =>
        {
            float t = r.R(0, 0.04f), hz = b * r.R(1.5f, 1.8f);
            return L(Seg.Note(t, 0.14f, hz, hz * 0.85f, r.Pick(Vowel.U, Vowel.U, Vowel.A), 1, 0.015f, 0.12f));
        }, 1.2f);
        for (int i = 0; i < 100; i++) CrowdSynth.Clap(o, Rate, At(MathF.Max(0, 0.01f + r.N() * 0.018f)), false, r, r.R(0.3f, 1), r.R(2000, 6000));
        CrowdSynth.Normalise(o, 0.14f);
        return o;
    }

    /// <summary>Laughter rippling round a stand: little groups going "ha-ha-ha".</summary>
    static float[] Laugh(Rng r)
    {
        var o = Seconds(2.6f);
        Many(o, r, 32, (r, b) =>
        {
            var s = new List<Seg>();
            float t = r.R(0, 0.9f), hz = b * r.R(1.4f, 2.1f), step = r.R(0.12f, 0.18f), lv = 1;
            int n = 3 + r.I(5);
            for (int k = 0; k < n; k++)
            {
                s.Add(Seg.Consonant(t, 0.035f, lv * 0.5f));
                s.Add(Seg.Note(t + 0.03f, step * 0.55f, hz, hz * 0.94f, Vowel.A, lv, 0.015f, 0.04f));
                t += step;
                hz *= 0.97f;
                lv *= 0.86f;
            }
            return s;
        }, 0.8f);
        CrowdSynth.Normalise(o, 0.1f);
        return o;
    }

    /// <summary>"Olé!"</summary>
    static float[] Ole(Rng r)
    {
        var o = Seconds(1.4f);
        Many(o, r, 50, (r, b) =>
        {
            float t = r.R(0, 0.06f), hz = b * r.R(1.9f, 2.2f);
            return L(Seg.Note(t, 0.2f, hz, hz, Vowel.O, 0.9f, 0.03f, 0.03f), Seg.Note(t + 0.22f, 0.5f, hz * 1.12f, hz * 0.9f, Vowel.E, 1, 0.03f, 0.3f));
        }, 1.1f);
        CrowdSynth.Normalise(o, 0.13f);
        return o;
    }

    /// <summary>The scorer's surname, roared back at the PA: two syllables.</summary>
    static float[] Surname(Rng r)
    {
        var o = Seconds(1.3f);
        var a = r.Pick(Vowel.A, Vowel.E, Vowel.I, Vowel.O);
        var c = r.Pick(Vowel.A, Vowel.O);
        Many(o, r, 60, (r, b) =>
        {
            float t = r.R(0, 0.05f), hz = b * r.R(2.0f, 2.4f);
            return L(Seg.Consonant(t, 0.04f, 0.6f), Seg.Note(t + 0.03f, 0.2f, hz, hz, a, 1, 0.02f, 0.03f),
                Seg.Consonant(t + 0.25f, 0.04f, 0.5f), Seg.Note(t + 0.28f, 0.55f, hz * 1.06f, hz * 0.85f, c, 1, 0.02f, 0.3f));
        }, 1.2f);
        CrowdSynth.Normalise(o, 0.15f);
        return o;
    }

    /// <summary>After conceding: the end claps its team back into it, louder each beat, "come on!".</summary>
    static float[] Rally(Rng r)
    {
        var o = Seconds(6.5f);
        const float beat = 0.55f;
        for (int i = 0; i < 130; i++)
        {
            float g = r.R(0.3f, 1), far = r.R(2000, 6000);
            for (int k = 0; k < 11; k++)
                if (r.F() < 0.4f + k * 0.06f) CrowdSynth.Clap(o, Rate, At(k * beat + r.N() * 0.02f + 0.02f), false, r, g * (0.4f + k * 0.06f), far);
        }
        Many(o, r, 40, (r, b) =>
        {
            float t = 2.2f + r.R(0, 0.05f), hz = b * r.R(1.9f, 2.2f);
            return L(Seg.Consonant(t, 0.04f, 0.5f), Seg.Note(t + 0.03f, 0.22f, hz, hz, Vowel.U, 0.9f, 0.02f, 0.04f),
                Seg.Note(t + 0.3f, 0.5f, hz * 1.06f, hz * 0.9f, Vowel.O, 1, 0.03f, 0.3f));
        });
        CrowdSynth.Normalise(o, 0.12f);
        return o;
    }

    /// <summary>One fan near you, shouting a few words.</summary>
    static float[] Heckle(Rng r)
    {
        var o = Seconds(1.6f);
        float hz = CrowdSynth.BaseHz(r, out bool w) * r.R(1.5f, 1.9f);
        var s = new List<Seg>();
        float t = 0.02f;
        int n = 1 + r.I(3);
        for (int k = 0; k < n; k++)
        {
            bool last = k == n - 1;
            if (r.F() < 0.6f) s.Add(Seg.Consonant(t, 0.045f, 0.4f));
            float d = last ? r.R(0.3f, 0.6f) : r.R(0.12f, 0.22f);
            s.Add(Seg.Note(t + 0.035f, d, hz * (last ? 1.1f : 1), hz * (last ? 0.8f : 0.97f), (Vowel)r.I(5), 1, 0.02f, last ? 0.15f : 0.03f));
            t += d + 0.06f;
        }
        var tone = Tone.Shout(r, w);
        tone.Far = 8000;
        CrowdSynth.Voice(o, Rate, 0, false, s, tone, r, 1);
        CrowdSynth.Normalise(o, 0.12f);
        return o;
    }

    /// <summary>
    /// The stadium announcer through the PA's horns: a phrase, or ("name") a lead-in, a beat,
    /// then a first name drawn out and lifted for the crowd to answer.
    /// </summary>
    static float[] Announcer(Rng r, bool name)
    {
        var o = Seconds(name ? 3.2f : 4.2f);
        var s = new List<Seg>();
        float t = 0.05f, hz0 = r.R(105, 125);
        int n = name ? 6 : 10 + r.I(5);
        for (int k = 0; k < n; k++)
        {
            if (r.F() < 0.6f) s.Add(Seg.Consonant(t, r.R(0.03f, 0.06f), 0.4f));
            t += 0.03f;
            float d = r.R(0.09f, 0.19f), hz = hz0 * (1.15f - 0.25f * k / n) * (1 + r.N() * 0.05f);
            s.Add(Seg.Note(t, d, hz, hz * 0.97f, (Vowel)r.I(5), r.R(0.7f, 1), 0.015f, 0.03f));
            t += d + (r.F() < 0.18f ? 0.15f : 0.02f);
        }
        if (name)
        {
            t += 0.3f;
            s.Add(Seg.Consonant(t, 0.05f, 0.5f));
            s.Add(Seg.Note(t + 0.04f, 0.24f, hz0 * 1.2f, hz0 * 1.25f, (Vowel)r.I(5), 1, 0.02f, 0.03f));
            t += 0.32f;
            s.Add(Seg.Note(t, 0.8f, hz0 * 1.3f, hz0 * 1.5f, r.Pick(Vowel.O, Vowel.A, Vowel.U), 1, 0.03f, 0.25f));
        }
        var tone = new Tone { Size = 1, Bright = 1100, Breath = 0.05f, Vib = 0.004f, VibHz = 5, Jitter = 0.01f, Far = 9000 };
        CrowdSynth.Voice(o, Rate, 0, false, s, tone, r, 1);
        // The horns: no lows, no highs, a little pushed.
        CrowdSynth.Normalise(o, 0.1f);
        float hp = 0, lp = 0, lp2 = 0, kh = 1 - MathF.Exp(-2 * MathF.PI * 380 / Rate), kl = 1 - MathF.Exp(-2 * MathF.PI * 3300 / Rate);
        for (int i = 0; i < o.Length; i++)
        {
            hp += (o[i] - hp) * kh;
            float x = o[i] - hp;
            lp += (x - lp) * kl;
            lp2 += (lp - lp2) * kl;
            o[i] = MathF.Tanh(lp2 * 4);
        }
        CrowdSynth.Normalise(o, 0.12f);
        return o;
    }

    /// <summary>The PA's three-note chime.</summary>
    static float[] Chime(Rng r)
    {
        var o = Seconds(2.6f);
        float[] notes = { 784, 659, 523 };
        for (int k = 0; k < 3; k++)
        {
            int at = At(k * 0.42f);
            for (int i = 0; at + i < o.Length; i++)
            {
                float t = i / Rate, e = MathF.Exp(-t / 0.5f) * MathF.Min(1, t / 0.004f);
                o[at + i] += (MathF.Sin(2 * MathF.PI * notes[k] * t) + 0.25f * MathF.Sin(4 * MathF.PI * notes[k] * t) * MathF.Exp(-t / 0.15f)) * e;
            }
        }
        CrowdSynth.Normalise(o, 0.1f);
        return o;
    }

    // ------------------------------------------------------------------ the songs

    /// <summary>
    /// One end singing a song, one bar-loop long (the notes' tails wrap round, so it loops
    /// without a seam): every singer a little early or late, a little sharp or flat, scooping
    /// into the notes; the capo's few on the calls; the drum and the claps in it.
    /// </summary>
    static float[] Song(Rng r, Chant c, int end)
    {
        float beat = 60f / c.Bpm, loop = c.Beats * beat;
        var o = Seconds(loop);
        int singers = end == 0 ? 36 : 20;
        for (int i = 0; i < singers; i++)
        {
            float hz = CrowdSynth.BaseHz(r, out bool w);
            float oct = w ? 2 : r.F() < 0.08f ? 0.5f : 1;
            float tune = MathF.Pow(2, r.N() * 0.022f), late = r.R(0, 0.05f);
            bool capo = i < 5;
            var s = new List<Seg>();
            foreach (var n in c.Notes)
            {
                if (n.Call && !capo) continue;
                float t = n.B * beat + late + r.N() * 0.015f;
                float f = Root * MathF.Pow(2, (n.P + Key[end]) / 12) * oct * tune * MathF.Pow(2, r.N() * 0.012f);
                if (r.F() < 0.5f) s.Add(Seg.Consonant(t - 0.03f, 0.035f, 0.4f));
                s.Add(Seg.Note(t, n.D * beat * 0.92f, f * 0.965f, f, n.V, n.Call ? 1.6f : 1, 0.04f, 0.1f));
            }
            var tone = Tone.Shout(r, w);
            tone.Bright *= 0.75f;
            CrowdSynth.Voice(o, Rate, 0, true, s, tone, r, r.R(0.4f, 1) * (capo ? 1.3f : 1));
        }
        foreach (float b in c.Drum) CrowdSynth.Drum(o, Rate, At(b * beat), true, end == 0 ? 95 : 125, b == 0 ? 0.9f : 0.65f);
        foreach (float b in c.Claps)
            for (int i = 0; i < 100; i++)
                CrowdSynth.Clap(o, Rate, At(b * beat + 0.015f + r.N() * 0.018f), true, r, r.R(0.2f, 0.7f), r.R(2000, 6000));
        CrowdSynth.Normalise(o, 0.11f);
        return o;
    }
}
