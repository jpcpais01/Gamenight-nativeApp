using System;
using Godot;
using GameNight.Grounds;
using GameNight.Audio;
using GameNight.Menus;
using GameNight.Render;
using GameNight.Sim;
using GameNight.UI;

namespace GameNight;

/// <summary>
/// The match screen. The engine (GameNight.Sim, the PWA's match ported to C#) runs on its own
/// thread at a fixed 120 steps a second; every rendered frame reads the last two steps and
/// draws between them, so the picture is smooth at any refresh rate and a slow AI moment can
/// never stall a frame. The frame rate is never capped: it runs at whatever the display does.
/// </summary>
public partial class Main : Node
{
    /// <summary>Set by the menus before the node enters the tree: what to play and how to report
    /// back. Without one this is a stand-alone match, as on the first builds.</summary>
    public MatchRequest Request;

    Match _match;
    MatchRunner _runner;
    Drill _drill;
    DrillHud _drillHud;
    bool _reported;
    readonly MatchSnapshot _prev = new(), _cur = new();

    PixelView _view;
    Ground _ground;
    /// <summary>The match's sound and the terraces' director (the stadium can read Sound.Terraces).</summary>
    public readonly MatchSound Sound = new();
    MatchCamera _camera;
    Profiler _prof;
    PlayersView _players;
    Officials _officials;
    Goals _goals;
    DeliveryView _delivery;
    TouchControls _controls;
    Hud _hud;
    PauseMenu _pause;
    readonly Replay _replay = new();
    /// <summary>The walk-out before kick-off (the stadium reads Cutscene.Hang for the giant tifo).</summary>
    public readonly Cutscene Cutscene = new();
    Letterbox _letterbox;
    /// <summary>Debug: `-- --screenshot=out.png` saves the screen after a few seconds and quits.</summary>
    string _shotPath;
    double _time, _shotAt = 6;

    public override void _Ready()
    {
        DisplayServer.ScreenSetKeepOn(true);
        MatchSettings.Load();
        MatchSettings.ApplyFpsCap();
        Kick.PrepareGroundPasses();

        _view = new PixelView();
        AddChild(_view);
        World.Build(_view.WorldRoot);
        _ground = Ground.Create(Request?.Ground ?? "big", Request?.Setup).AddTo(_view.WorldRoot);
        Acoustics();
        _players = new PlayersView(_view.WorldRoot);
        _officials = new Officials(_view.WorldRoot);
        _goals = new Goals(_view.WorldRoot);
        _delivery = new DeliveryView(_view.WorldRoot);
        _camera = new MatchCamera(_view.Camera);
        if (Request?.Showcase == true) _camera.Showcase = 0;

        _hud = new Hud { Name = "Hud", View = _view };
        AddChild(_hud);
        _controls = new TouchControls();
        AddChild(_controls);
        _letterbox = new Letterbox();
        AddChild(_letterbox);
        _prof = new Profiler { Visible = false };
        AddChild(_prof);
        _prof.Watch(_view.Viewport, GetViewport());
        _prof.SimStepMs = () => _runner?.StepMs ?? 0;
        _prof.AudioBlockMs = () => GameAudio.Instance?.BlockMs ?? 0;
        _prof.SimStepPeak = () =>
        {
            if (_runner == null) return 0;
            double peak = _runner.StepPeak;
            _runner.StepPeak = 0;
            return peak;
        };
        _prof.Context = SpikeContext;
        _letterbox.Skip += () =>
        {
            if (Cutscene.Active)
            {
                Cutscene.Next();
                if (!Cutscene.Active) EndDirected();
            }
            else EndDirected();
        };
        Cutscene.OnCaption = _letterbox.Caption;
        Cutscene.OnJump = _players.Snap;
        _replay.OnRewind = _players.Snap;
        _replay.OnEvents = f =>
        {
            if (f.Net > 0) _goals.Impact(f.BallX, f.BallY, f.BallZ, f.Net, _time);
            var audio = GameAudio.Instance;
            if (audio == null) return;
            if (f.KickMax > 0) audio.Kick(f.KickMax);
            if (f.Post > 0) audio.Post(f.Post);
            if (f.Net > 0) audio.Net(f.Net);
        };
        _pause = new PauseMenu { CurrentHeight = () => _view.ArtHeight };
        AddChild(_pause);
        _pause.Opened += () => SetPaused(true);
        _pause.Resumed += () => SetPaused(false);
        _pause.Restart += NewMatch;
        _pause.SettingsChanged += ApplySettings;
        if (Request != null && !Request.Demo) _pause.Leave = () => Report(false);
        _pause.WeatherName = () => Atmosphere.Names[(int)_ground.Atmosphere.Weather];
        _pause.CycleWeather = () => Atmosphere.Names[(int)_ground.Atmosphere.Cycle()];
        _pause.SaveReport = SaveReport;
        if (Request?.Demo != true && Request?.Drill == null) _pause.Foul = () => _runner?.Invoke(m => m.DebugFoul());
        ApplySettings();
        if (Request?.Demo == true)
        {
            // Behind the home screen: the computer plays both sides, nothing to touch.
            _hud.Visible = _controls.Visible = _pause.Visible = false;
            GameAudio.Instance?.SetAmbience(1);
            _hud.ProcessMode = _controls.ProcessMode = _pause.ProcessMode = ProcessModeEnum.Disabled;
        }
        if (Request?.Drill != null)
        {
            _hud.Visible = false;
            _drillHud = new DrillHud();
            AddChild(_drillHud);
            MoveChild(_drillHud, _hud.GetIndex());
        }

        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--screenshot=")) _shotPath = arg["--screenshot=".Length..];
            if (arg.StartsWith("--shot-at=")) _shotAt = double.Parse(arg["--shot-at=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        }

        NewMatch();
    }

    /// <summary>A fresh match (first launch, or Restart from the pause menu).</summary>
    void NewMatch()
    {
        _runner?.Stop();
        if (Directed) EndDirected();
        _replay.Reset();
        _officials.Reset();
        _match = Request != null ? new Match(Request.Seed, Request.Setup) : new Match(seed: DateTime.Now.Ticks % 2147483647);
        if (Request?.Demo == true) _match.AutoPlay = true;
        // Names and kits are read before the match's own thread starts.
        _players.SetMatch(_match);
        _hud.SetMatch(_match);
        _runner = new MatchRunner(_match);
        if (Request?.Drill is DrillKind kind)
        {
            _drill = new Drill(_match, kind, Math.Max(Request.DrillBest, _drill?.Best ?? 0));
            _runner.AfterStep = _drill.Step;
            _drillHud.Drill = _drill;
        }
        _runner.Read(_prev, _cur, out _);
        _runner.Paused = _pause.IsOpen;
        _runner.Start();
        // A real match opens with the walk-out.
        if (Request?.Demo != true && Request?.Drill == null && (_shotPath == null || Array.IndexOf(OS.GetCmdlineUserArgs(), "--cutscene") >= 0))
        {
            Direct(false);
            Cutscene.Start(_match, _cur, Request?.Ground ?? "big");
            _prewarm = 4;
        }
    }

    void SetPaused(bool on)
    {
        if (_runner != null) _runner.Paused = on || Directed;
        _hud.Paused = on;
        if (GameAudio.Instance != null) GameAudio.Instance.Suspended = on;
        if (on) _controls.ReleaseAll();
        _controls.SetProcessInput(!on);
        _controls.Visible = !on && !Directed;
    }

    /// <summary>A replay or the walk-out on screen: the match waits.</summary>
    bool Directed => _replay.Active || Cutscene.Active;

    /// <summary>The cut after a goal (or the walk-out): the match waits while it plays.</summary>
    void Direct(bool replay)
    {
        _runner.Paused = true;
        _players.Markers = false;
        _hud.Visible = false;
        _controls.ReleaseAll();
        _controls.Visible = false;
        _letterbox.Open(replay);
    }

    /// <summary>Skipped or done: back to the match.</summary>
    void EndDirected()
    {
        _replay.Finish();
        Cutscene.Cancel();
        _players.Snap();
        _players.Markers = true;
        _hud.Visible = Request?.Drill == null && Request?.Demo != true;
        _controls.Visible = !_pause.IsOpen;
        _letterbox.Close();
        if (_runner != null) _runner.Paused = _pause.IsOpen;
    }

    void ApplySettings()
    {
        _camera.BaseDist = MatchCamera.Presets[Math.Clamp(MatchSettings.Camera, 0, 2)];
        _view.TargetHeight = MatchSettings.Pixels > 0 ? MatchSettings.Pixels : 270;
        _hud.ShowFps = MatchSettings.ShowFps;
        _prof.On = MatchSettings.ShowFps && MatchSettings.Profile && Request?.Demo != true;
        if (GameAudio.Instance != null) GameAudio.Instance.Muted = !MatchSettings.Sound;
        // Fast graphics: the sun casts no shadows (the biggest cost on a weak GPU).
        foreach (var n in _view.WorldRoot.FindChildren("*", nameof(DirectionalLight3D), true, false))
            ((DirectionalLight3D)n).ShadowEnabled = !MatchSettings.Fast;
    }

    public override void _Notification(int what)
    {
        // Backgrounded or covered: the pause menu comes up and the match clock stops.
        if (_runner == null || _shotPath != null || Request?.Demo == true) return;
        if (what == NotificationApplicationPaused || what == NotificationApplicationFocusOut) _pause.Open();
    }

    public override void _ExitTree()
    {
        _runner?.Stop();
        // Left from the pause menu: the sound it stopped comes back for the menus.
        if (Request?.Demo != true && GameAudio.Instance != null)
        {
            GameAudio.Instance.Suspended = false;
            GameAudio.Instance.SetRain(false);
            GameAudio.Instance.SetTension(0);
            GameAudio.Instance.SetVenue(Venue.Default);
        }
    }

    /// <summary>Tells the menus the match is over (full time, or left from the pause menu).</summary>
    void Report(bool finished)
    {
        if (_reported || Request?.Done == null) return;
        _reported = true;
        _runner.Paused = true;
        Request.Done(new MatchOutcome { Finished = finished, Home = _cur.Score[0], Away = _cur.Score[1], DrillBest = _drill?.Best ?? 0 });
    }

    /// <summary>Android back during a match: the pause menu, open or closed.</summary>
    public void Back()
    {
        if (_pause.IsOpen) _pause.Close();
        else _pause.Open();
    }

    /// <summary>The stadium builder: build the ground again (the plan changed).</summary>
    public void RebuildGround()
    {
        var old = _ground;
        old.Root.GetParent()?.RemoveChild(old.Root);
        old.Root.QueueFree();
        _ground = Ground.Create(Request?.Ground ?? "big", Request?.Setup).AddTo(_view.WorldRoot);
        Acoustics();
    }

    /// <summary>How the ground sounds: its crowd, its roof, how near the fans are.</summary>
    void Acoustics() => GameAudio.Instance?.SetVenue(Venue.For(Request?.Ground ?? "big", Ground.Club?.S.Stadium?.Sets));

    /// <summary>The stadium builder: swing the camera round to look at a stand (yaw, 0 = from the near side).</summary>
    public void Focus(float yaw)
    {
        if (_camera != null) _camera.Showcase = yaw;
    }

    /// <summary>The demo match behind the menus: hidden (and not drawn or stepped) under a full screen.</summary>
    public void Backdrop(bool shown)
    {
        if (_view == null) return;
        ProcessMode = shown ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _view.Visible = shown;
        _view.Viewport.RenderTargetUpdateMode = shown ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (_runner != null) _runner.Paused = !shown;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _prof.Begin();
        _controls.Tick(dt);
        _runner.Submit(_controls.Input);
        _runner.Read(_prev, _cur, out float alpha);

        if (_view.Fit())
        {
            _camera.PixelHeight = _view.Viewport.Size.Y;
            _camera.SetAspect(_view.Aspect);
        }
        if (_cur.Goal >= 0) _camera.Bump(0.4f);
        if (_cur.Post > 0) _camera.Bump(0.6f);
        float run = _pause.IsOpen ? 0 : dt;
        if (_replay.Active)
        {
            _replay.Update(run, _camera);
            if (!_replay.Active) EndDirected();
        }
        if (Cutscene.Active)
        {
            Cutscene.Update(run, _camera);
            if (!Cutscene.Active) EndDirected();
        }
        _prof.Lap(Profiler.Sys.Camera);
        if (Cutscene.Active)
            _players.Update(Cutscene.Frame, Cutscene.Frame, 0, _time, 1);
        else if (_replay.Active)
            _players.Update(_replay.A, _replay.B, _replay.Alpha, _time, 1);
        else
        {
            _camera.Update(_prev, _cur, alpha, run);
            _players.Update(_prev, _cur, alpha, _time, (float)_match.SwitchT);
            if (Request?.Demo != true && Request?.Drill == null)
            {
                _replay.Record(_cur);
                if (_replay.Start(_cur, GoalSeq.Cut)) Direct(true);
            }
        }
        _delivery.Update(_cur);
        // The referee and his assistants (not at training, and off screen during the walk-out).
        _officials.Visible = Request?.Drill == null && !Cutscene.Active;
        if (Request?.Drill == null) _officials.Update(_match, _cur, _time, Directed ? 0 : run);
        // The net takes the ball (live, or again on the replay's tape).
        if (!Directed && _cur.Net > 0) _goals.Impact(_cur.BallX, _cur.BallY, _cur.BallZ, _cur.Net, _time);
        _goals.Update(_time);
        _prof.Lap(Profiler.Sys.Players);
        _ground.Update(_cur, _time, dt);
        Weather();
        _prof.Lap(Profiler.Sys.Stadium);
        Sound.Frame(_match, _cur, Request?.Demo != true, Request?.Drill == null, Request?.Drill != null, _pause.IsOpen ? 0 : dt);
        GameAudio.Instance?.Place(_view.Camera.GlobalPosition.X, Sound.Terraces.Tension);
        _prof.Lap(Profiler.Sys.Sound);
        _view.Present(_camera.SubPixelX, _camera.SubPixelY);

        bool attack = _cur.HumanAttacking;
        _controls.SetMode(ButtonMode(_cur, attack, out int picked), picked);
        _hud.Tick(_prev, _cur, alpha, _controls.Input, attack, delta);
        _drillHud?.Tick();
        if (_prewarm > 0) Prewarm(--_prewarm == 0);
        _pause.FoulShown = !Directed;
        _prof.Lap(Profiler.Sys.Hud);
        _prof.End(delta);
        // Full time: a few seconds of the scene, then back to the menus.
        if (Request != null && _cur.Phase == Phase.Fulltime && _cur.PhaseT > (Request.Demo ? 3 : 4.5)) Report(true);

        _time += delta;
        if (_shotPath != null && _time > _shotAt)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            GetTree().Quit();
            _shotPath = null;
        }
    }

    double _thunderFor = -10;
    int _prewarm;

    /// <summary>The first frames of the walk-out draw the match HUD, the controls, the markers
    /// and the officials once, all but invisible, so their shaders compile behind the opening
    /// shot instead of stuttering at kick-off (the PWA's prewarm).</summary>
    void Prewarm(bool done)
    {
        if (!done)
        {
            _hud.Visible = _controls.Visible = _officials.Visible = true;
            _players.Markers = true;
        }
        else
        {
            _hud.Visible = !Directed;
            _controls.Visible = !Directed && !_pause.IsOpen;
            _players.Markers = !Directed;
        }
        _hud.Modulate = _controls.Modulate = new Color(1, 1, 1, done ? 1 : 0.004f);
    }

    /// <summary>The rain's hiss, and thunder after each flash (later the farther off it struck).</summary>
    void Weather()
    {
        var atm = _ground.Atmosphere;
        var audio = GameAudio.Instance;
        if (atm == null || audio == null || Request?.Demo == true) return;
        audio.SetRain(atm.Weather == Grounds.Weather.Rain);
        if (atm.FlashAt > _thunderFor && atm.FlashAt <= _time)
        {
            _thunderFor = atm.FlashAt;
            audio.Thunder(0.4f + (float)Random.Shared.NextDouble() * 3f);
        }
    }

    /// <summary>The frame-time report into Downloads (and onto the clipboard, to paste anywhere).</summary>
    string SaveReport()
    {
        string version = (string)ProjectSettings.GetSetting("application/config/version", "?");
        var header = $"App {version} · {DateTime.Now:yyyy-MM-dd HH:mm} · ground {Request?.Ground ?? "big"} · weather {Atmosphere.Names[(int)_ground.Atmosphere.Weather]}"
            + $"\nSettings: graphics {(MatchSettings.Fast ? "fast" : "full")} · pixels {_view.ArtHeight} tall (setting {(MatchSettings.Pixels > 0 ? MatchSettings.Pixels.ToString() : "auto")}) · camera {MatchSettings.Camera} · sound {(MatchSettings.Sound ? "on" : "off")}"
            + $"\nMatch: {_cur.ClockLabel} · {_cur.Score[0]}-{_cur.Score[1]} · {SpikeContext()}{(Request?.Drill != null ? $" · training {Request.Drill}" : "")}";
        string text = _prof.Report(header);
        DisplayServer.ClipboardSet(text);
        string name = $"GameNight-report-{version}-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
        foreach (var dir in new[] { OS.GetSystemDir(OS.SystemDir.Downloads), OS.GetUserDataDir() })
        {
            if (string.IsNullOrEmpty(dir)) continue;
            using var f = FileAccess.Open(dir + "/" + name, FileAccess.ModeFlags.Write);
            if (f == null) continue;
            f.StoreString(text);
            return dir == OS.GetUserDataDir() ? "COPIED (PASTE IT IN THE CHAT)" : "SAVED IN DOWNLOADS · ALSO COPIED";
        }
        return "COPIED (PASTE IT IN THE CHAT)";
    }

    /// <summary>What's happening, in a few words, for the frame-time breakdown's spike list.</summary>
    string SpikeContext()
    {
        if (_pause.IsOpen) return "paused";
        if (Cutscene.Active) return "walk-out";
        if (_replay.Active) return "goal replay";
        if (_time < 3) return "first seconds";
        return _cur.Phase switch
        {
            Phase.Goal => "goal",
            Phase.Kickoff => "kick-off",
            Phase.SetPiece => _cur.SetPiece?.ToString().ToLowerInvariant() ?? "set piece",
            Phase.Out => "ball out",
            Phase.Halftime => "half time",
            Phase.Fulltime => "full time",
            _ => "open play",
        };
    }

    /// <summary>What the buttons say (the PWA's main loop): after your goal they pick the
    /// celebration (and show which while it plays), in goal at training they dive, and lining up
    /// your corner or goal kick they name the delivery.</summary>
    TouchControls.Mode ButtonMode(MatchSnapshot s, bool attack, out int picked)
    {
        picked = -1;
        bool drill = Request?.Drill != null;
        if (s.CelebrationOpen && !drill) return TouchControls.Mode.Celebrate;
        if (s.HumanScored && s.Celebration is { } kind && s.PhaseT < s.CelebrationAt + 1.6 && !drill)
        {
            picked = Array.IndexOf(Match.Celebrations, kind);
            return TouchControls.Mode.Celebrate;
        }
        if (s.KeeperButtons) return TouchControls.Mode.Keeper;
        if (s.AimingCorner) return TouchControls.Mode.Corner;
        if (s.AimingGoalKick) return TouchControls.Mode.GoalKick;
        return attack ? TouchControls.Mode.Attack : TouchControls.Mode.Defend;
    }
}
