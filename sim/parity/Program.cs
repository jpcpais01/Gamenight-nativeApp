// The C# side of the parity check: plays the same matches as harness.ts with the ported engine,
// hashes the state the same way every step, and reports the first step that differs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameNight.Sim;

static class Program
{
    static uint Mix(uint h, double x)
    {
        ulong bits = (ulong)BitConverter.DoubleToInt64Bits(x);
        for (int i = 0; i < 8; i++) h = unchecked((h ^ (byte)(bits >> (8 * i))) * 16777619u);
        return h;
    }

    static uint Hash(Match m)
    {
        uint h = 2166136261;
        var b = m.Ball;
        h = Mix(h, m.Time);
        h = Mix(h, (int)m.Phase);
        h = Mix(h, m.Rng.State);
        foreach (var v in new[] { b.Pos, b.Vel, b.Spin }) { h = Mix(h, v.X); h = Mix(h, v.Y); h = Mix(h, v.Z); }
        h = Mix(h, m.Owner?.Id ?? -1);
        h = Mix(h, m.HeldBy?.Id ?? -1);
        h = Mix(h, m.Controlled.Id);
        h = Mix(h, m.Teams[0].Score * 100 + m.Teams[1].Score);
        foreach (var p in m.All)
        {
            h = Mix(h, p.Pos.X);
            h = Mix(h, p.Pos.Z);
            h = Mix(h, p.Vel.X);
            h = Mix(h, p.Vel.Z);
            h = Mix(h, p.Facing);
            h = Mix(h, (int)p.Action);
            h = Mix(h, p.ActionT);
            h = Mix(h, p.Stamina);
        }
        return h;
    }

    static object Dump(Match m)
    {
        double[] V(V3 q) => new[] { q.X, q.Y, q.Z };
        return new
        {
            time = m.Time, phase = m.Phase.ToString().ToLowerInvariant(), rng = m.Rng.State, score = new[] { m.Teams[0].Score, m.Teams[1].Score },
            ball = new { pos = V(m.Ball.Pos), vel = V(m.Ball.Vel), spin = V(m.Ball.Spin), onGround = m.Ball.OnGround },
            owner = m.Owner?.Id ?? -1, heldBy = m.HeldBy?.Id ?? -1, controlled = m.Controlled.Id, passTarget = m.PassTarget?.Id ?? -1,
            players = m.All.Select(p => new
            {
                id = p.Id, pos = V(p.Pos), vel = V(p.Vel), facing = p.Facing, action = p.Action.ToString().ToLowerInvariant(), actionT = p.ActionT, stamina = p.Stamina,
                moveX = p.MoveX, moveZ = p.MoveZ, wantSpeed = p.WantSpeed, plan = p.Plan != null ? $"{p.Plan.Type.ToString().ToLowerInvariant()}:{p.Plan.TargetId}" : null,
                icept = new { t = m.AI.Intercept[p.Id].T, x = m.AI.Intercept[p.Id].X, z = m.AI.Intercept[p.Id].Z, slack = m.AI.Intercept[p.Id].Slack, m = m.AI.Intercept[p.Id].M, at = m.AI.Intercept[p.Id].At, vx = m.AI.Intercept[p.Id].VX, vz = m.AI.Intercept[p.Id].VZ },
            }).ToArray(),
        };
    }

    static readonly double[][] Dirs = { new[] { 1.0, 0 }, new[] { 0.7, 0.7 }, new[] { 0.0, 1 }, new[] { -0.7, 0.7 }, new[] { -1.0, 0 }, new[] { -0.7, -0.7 }, new[] { 0.0, -1 }, new[] { 0.7, -0.7 } };

    /// <summary>Scripted thumbs, identical to harness.ts.</summary>
    static void Script(int i, InputState inp)
    {
        int k = i / 240 % 9;
        inp.MoveX = k == 8 ? 0 : Dirs[k][0];
        inp.MoveY = k == 8 ? 0 : Dirs[k][1];
        inp.Sprint = i / 500 % 3 == 0;
        int slot = i % 300;
        int btn = i / 300 % 3;
        if (slot == 0)
        {
            inp.Events.Add(ButtonEvent.Down(btn));
            inp.Held[btn] = true;
        }
        if (inp.Held[btn]) inp.HoldTime[btn] = slot / 120.0;
        inp.Swipe[btn] = inp.Held[btn] && i / 300 % 4 == 1;
        if (slot == 40)
        {
            inp.Events.Add(ButtonEvent.Up(btn, 40 / 120.0, i / 300 % 4 == 1));
            inp.Held[btn] = false;
            inp.HoldTime[btn] = 0;
        }
        if (i % 1000 == 500) inp.TackleSwipe = TackleSwipe.Tackle;
    }

    static MatchSetup LoadSetup(string path)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true };
        opts.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Deserialize<MatchSetup>(File.ReadAllText(path), opts)!;
    }

    // args: pwaHashes seed mode steps [dumpStep dumpOut]
    static int Main(string[] args)
    {
        string pwa = args[0];
        double seed = double.Parse(args[1]);
        string mode = args[2];
        int steps = int.Parse(args[3]);
        int dumpStep = args.Length > 4 ? int.Parse(args[4]) : -1;
        Kick.PrepareGroundPasses();
        var setup = mode == "club" ? LoadSetup(pwa + ".setup.json") : null;
        var m = new Match(seed, setup);
        m.AutoPlay = mode == "auto" || mode == "club";
        Drill? drill = null;
        if (mode.StartsWith("drill:"))
        {
            var kind = Enum.GetValues<DrillKind>().First(k => k.ToString().ToLowerInvariant() == mode[6..]);
            drill = new Drill(m, kind);
        }
        var inp = new InputState();
        var want = File.ReadAllBytes(pwa);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int first = -1;
        for (int i = 0; i < steps; i++)
        {
            if (mode == "human" || drill != null) Script(i, inp);
            m.Step(inp);
            drill?.Step();
            inp.Events.Clear();
            inp.TackleSwipe = TackleSwipe.None;
            m.TakeEvents();
            uint h = Hash(m);
            if (first < 0 && h != BitConverter.ToUInt32(want, i * 4)) first = i;
            if (i == dumpStep) File.WriteAllText(args[5], JsonSerializer.Serialize(Dump(m), new JsonSerializerOptions { WriteIndented = true }));
        }
        string score = $"{m.Teams[0].Score}-{m.Teams[1].Score}";
        Console.WriteLine(first < 0
            ? $"{mode} seed {seed}: identical for all {steps} steps ({score}, {sw.ElapsedMilliseconds} ms){(drill != null ? ", drill " + drill.Line : "")}"
            : $"{mode} seed {seed}: FIRST DIFFERENCE at step {first} (t={(first + 1) / 120.0:0.000}s)");
        return first < 0 ? 0 : 1;
    }
}
