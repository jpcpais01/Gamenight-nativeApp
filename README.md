# GameNight (native)

The native Android rebuild of [GameNight](https://github.com/jpcpais01/football-game): the same
pixel-art 11-a-side football game, built with **Godot 4.7 (C#, Mobile renderer)** so it owns the
screen directly, with no browser in between. The PWA stays live as the reference for look, feel
and numbers.

**Install the latest build:** open
<https://github.com/jpcpais01/Gamenight-nativeApp/releases/latest/download/GameNight.apk>
on the phone (on Xiaomi/HyperOS, allow Chrome to install unknown apps the first time).

## Layout

| Path | What it is |
| --- | --- |
| `game/` | The Godot project (`project.godot`, `GameNight.csproj`, `Main.tscn`). |
| `game/Scripts/Main.cs` | The match screen: the engine runs on its own thread at 120 Hz (`MatchRunner`); each frame reads the last two steps (`MatchSnapshot`) and draws between them. No frame cap. |
| `game/Scripts/Render/` | `PixelView` (art-resolution viewport, integer upscale, sub-pixel scroll), `MatchCamera` (the PWA's broadcast camera), `PlayersView` (22 players in one instanced draw), `World` (pitch, goals, boards, stands). |
| `game/Scripts/UI/` | `TouchControls` (joystick + Pass/Through/Kick/Sprint, as in the PWA) and `Hud` (score, clock, fps), drawn by the engine. |
| `game/Shaders/` | Pitch, players, post pass (outline + grade + dither in one pass), nets, crowd, boards. |
| `game/android-overlay/` | Our Android activity, copied over Godot's template at build time: it asks Android for the display's highest refresh rate. |
| `sim/` | The match engine: the PWA's `src/sim` ported to a plain C# library (no Godot types), step-for-step identical to the PWA. See [`sim/README.md`](sim/README.md). |
| `.github/workflows/android.yml` | Builds the signed APK on every push to `main` and publishes it as a Release. |

## The match engine

`sim/GameNight.Sim.csproj` is the PWA's whole simulation (rules, ball physics, AI, keepers, set
pieces, offside, training drills) in C#. Same seed and inputs give the same match as the PWA,
every step: `sim/parity/run.sh` checks that against the PWA's own code. `Match.Step(input)` at
120 Hz, `Match.Write(snapshot)` for flat arrays to draw, or `MatchRunner` to run it on its own
thread. Details in [`sim/README.md`](sim/README.md).

## How it renders

The 3D world is drawn once at art resolution (about 270 pixels tall, a whole fraction of the
screen height), one sample per pixel, into a `SubViewport`. A single post pass on a full-screen
quad does the outlines (from depth), the grade and the ordered dither. The picture is upscaled
with nearest filtering at a whole-number scale and scrolled by the camera's sub-pixel remainder,
so it glides while the pixel grid stays put. The HUD and controls are drawn by the engine in the
same frame. Players are one instanced draw animated in the vertex shader; the sun casts one
shadow map; everything else is static.

## Building

CI does it (see the workflow). Locally, with Godot 4.7.2 .NET and the .NET 8 SDK:

```sh
godot --path game                 # run on desktop (mouse = touch; WASD/arrows, J K L, Shift)
godot --path game -- --screenshot=shot.png   # save a frame after 4 s and quit
```

An Android export also needs the Android SDK, JDK 17, the .NET 9 SDK and the 4.7.2 .NET export
templates; the workflow shows every step.

## Versions and signing

- The version name is `config/version` in `project.godot` with its last number replaced by the
  CI run number, so every push gets a new, higher version automatically.
- Releases are signed with the key in the repository secrets `ANDROID_KEYSTORE_BASE64`,
  `ANDROID_KEYSTORE_PASSWORD` and `ANDROID_KEY_ALIAS` when they're set. Without them CI signs
  with `game/android-signing/sideload.keystore` (a throwaway key, fine for sideloading), so
  builds are always installable and update one another. Moving from one key to the other means
  uninstalling once; keep whichever key you choose forever after.
