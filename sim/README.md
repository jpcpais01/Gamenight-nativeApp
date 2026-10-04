# GameNight.Sim — the match engine

A line-for-line C# port of the PWA's `src/sim` (football-game, v0.96.27): players, ball
physics, kick solvers, the match rules (restarts, fouls, advantage, cards, offside, penalties,
walls), the AI (shape, marking, pressing, passing and through balls, crosses, keepers,
celebrations) and the training drills. Plain .NET 8, no Godot types, no allocations worth
mentioning per step, safe to run on its own thread.

**Since app 0.23 the human controls differ on purpose** (one Press/Sprint button, the active
player's ball seeking in `Match.GoForBall`, your through ball in `AI.AimedThrough`), so
`parity/run.sh` matches the PWA only for autoplay. Before that:

**Same seed, same line-ups, same inputs: the same match as the PWA, step for step.** Every
random draw goes through the same mulberry32 stream in the same order, and the maths is
bit-identical to V8's: `JsMath` ports V8's fdlibm `sin`, `cos`, `atan2`, `exp`, `pow` and its
`Math.hypot` (.NET's own differ in the last bit on a few percent of inputs, which is enough for a
match to drift apart within seconds). Float32 arrays the PWA uses stay float32 (`F32`).

## Using it

```csharp
Kick.PrepareGroundPasses();                 // once at boot (the PWA does it while loading)
var match = new Match(seed, setup);         // setup: null = the two default teams
var input = new InputState();               // the PWA's controls: stick, buttons, events
var frame = new MatchSnapshot();

match.Step(input);                          // one fixed step, Tick.DT = 1/120 s
var events = match.TakeEvents();            // kicks, whistles, goals, saves... of that step
match.Write(frame);                         // flat arrays for the renderer
```

Or let it run on its own thread:

```csharp
var runner = new MatchRunner(match);
runner.Start();
runner.Submit(input);                       // each frame: copies the stick, moves the events
runner.Read(previous, current, out alpha);  // last two steps + blend, events folded in
runner.Invoke(m => m.SetControlled(p));     // anything else runs between steps
```

- `Match` — the whole simulation. Public state mirrors the PWA (`Ball`, `Players`, `Teams`,
  `Phase`, `SetPiece`, `Owner`, `Controlled`, `AutoPlay`...), plus the aiming helpers the HUD
  uses (`AimPoint`, `CrossAim`, `SolveDelivery`, `DeadBallView`, `ClockLabel`).
- `InputState` / `ButtonEvent` — exactly what the PWA's touch controls feed the sim. `Btn.A`
  Pass/Tackle, `Btn.B` Through/Switch, `Btn.C` Shoot/Press, `Sprint`, `TackleSwipe`.
- `MatchSnapshot` — per-player arrays indexed by id (0–10 home, 11–21 away): position, velocity,
  facing, look target, run cycle and lean, action and its timing, kick leg/type/reach, slide and
  dive pose, look and kit indices; the ball (with spin); phase, clock, score, set piece,
  celebration, and the step's events.
- `DeliveryPreview` — the dotted flight and ring of an aimed corner, goal kick or cross (the
  PWA's corner aim). `MatchRunner` keeps it in the snapshot (`HasArc`, `ArcX/Y/Z`, `ArcRingX/Z`).
- `MatchSetup` — club line-ups (names, numbers, attributes, looks, formation slots, captain).
- `Drill` — training (free kicks, penalties, one on one, 2 v 2, goalkeeper): call `Step()` after
  each `Match.Step` (or set `MatchRunner.AfterStep`).

## Checking it against the PWA

`parity/run.sh` bundles the PWA's own engine with esbuild, plays the same matches on both sides
and compares a hash of the whole state (ball, every player, the RNG) after every step: two
autoplay matches to full time, one with scripted thumbs on the controls, one with a club line-up,
and three training drills. All identical, every step. Rerun it after porting any PWA sim change:

```sh
PWA=/path/to/football-game sim/parity/run.sh
```

A full 90 minutes simulates headless in under a second on a desktop.

## Not here

- The club and cards (`src/meta`): building a `MatchSetup` from the squad is the game's job.
- The renderer-only `Body` shapes are ported but nothing in the sim reads them.
- `sin`/`cos` of angles beyond ~10^9 radians fall back to .NET's (the game never makes one).
