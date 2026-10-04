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
    PlayersView _players;
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
        Engine.MaxFps = 0;
        DisplayServer.ScreenSetKeepOn(true);
        MatchSettings.Load();
        Kick.PrepareGroundPasses();

        _view = new PixelView();
        AddChild(_view);
        World.Build(_view.WorldRoot);
        _ground = Ground.Create(Request?.Ground ?? "big", Request?.Setup).AddTo(_view.WorldRoot);
        _players = new PlayersView(_view.WorldRoot);
        _delivery = new DeliveryView(_view.WorldRoot);
        _camera = new MatchCamera(_view.Camera);

        _hud = new Hud { Name = "Hud", View = _view };
        AddChild(_hud);
        _controls = new TouchControls();
        AddChild(_controls);
        _letterbox = new Letterbox();
        AddChild(_letterbox);
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

    public override void _ExitTree() => _runner?.Stop();

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
        _ground.ShowGiantTifo(Cutscene.Active && Cutscene.Hang > 0.5f);
        _ground.Update(_cur, _time, dt);
        Sound.Frame(_match, _cur, Request?.Demo != true, Request?.Drill == null, Request?.Drill != null, _pause.IsOpen ? 0 : dt);
        _view.Present(_camera.SubPixelX, _camera.SubPixelY);

        bool attack = _cur.HumanAttacking;
        _controls.SetMode(attack ? TouchControls.Mode.Attack : TouchControls.Mode.Defend);
        _hud.Tick(_prev, _cur, alpha, _controls.Input, attack, delta);
        _drillHud?.Tick();
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
}
