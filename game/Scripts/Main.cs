using System;
using Godot;
using GameNight.Grounds;
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
    MatchCamera _camera;
    PlayersView _players;
    DeliveryView _delivery;
    TouchControls _controls;
    Hud _hud;
    PauseMenu _pause;
    /// <summary>Debug: `-- --screenshot=out.png` saves the screen after a few seconds and quits.</summary>
    string _shotPath;
    double _time;

    public override void _Ready()
    {
        Engine.MaxFps = 0;
        DisplayServer.ScreenSetKeepOn(true);
        MatchSettings.Load();
        Kick.PrepareGroundPasses();

        _view = new PixelView();
        AddChild(_view);
        World.Build(_view.WorldRoot);
        _ground = Ground.Create(Request?.Ground ?? "big").AddTo(_view.WorldRoot);
        _players = new PlayersView(_view.WorldRoot);
        _delivery = new DeliveryView(_view.WorldRoot);
        _camera = new MatchCamera(_view.Camera);

        _hud = new Hud { Name = "Hud", View = _view };
        AddChild(_hud);
        _controls = new TouchControls();
        AddChild(_controls);
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
            if (arg.StartsWith("--screenshot=")) _shotPath = arg["--screenshot=".Length..];

        NewMatch();
    }

    /// <summary>A fresh match (first launch, or Restart from the pause menu).</summary>
    void NewMatch()
    {
        _runner?.Stop();
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
    }

    void SetPaused(bool on)
    {
        if (_runner != null) _runner.Paused = on;
        _hud.Paused = on;
        if (on) _controls.ReleaseAll();
        _controls.SetProcessInput(!on);
        _controls.Visible = !on;
    }

    void ApplySettings()
    {
        _camera.BaseDist = MatchCamera.Presets[Math.Clamp(MatchSettings.Camera, 0, 2)];
        _view.TargetHeight = MatchSettings.Pixels > 0 ? MatchSettings.Pixels : 270;
        _hud.ShowFps = MatchSettings.ShowFps;
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
        _camera.Update(_prev, _cur, alpha, _pause.IsOpen ? 0 : dt);
        _players.Update(_prev, _cur, alpha, _time, (float)_match.SwitchT);
        _delivery.Update(_cur);
        _ground.Update(_cur, _time, dt);
        _view.Present(_camera.SubPixelX, _camera.SubPixelY);

        bool attack = _cur.HumanAttacking;
        _controls.SetMode(attack ? TouchControls.Mode.Attack : TouchControls.Mode.Defend);
        _hud.Tick(_prev, _cur, alpha, _controls.Input, attack, delta);
        _drillHud?.Tick();
        // Full time: a few seconds of the scene, then back to the menus.
        if (Request != null && _cur.Phase == Phase.Fulltime && _cur.PhaseT > (Request.Demo ? 3 : 4.5)) Report(true);

        _time += delta;
        if (_shotPath != null && _time > 6)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            GetTree().Quit();
            _shotPath = null;
        }
    }
}
