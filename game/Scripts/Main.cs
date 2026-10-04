using System;
using Godot;
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
    Match _match;
    MatchRunner _runner;
    readonly MatchSnapshot _prev = new(), _cur = new();

    PixelView _view;
    MatchCamera _camera;
    PlayersView _players;
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
        _players = new PlayersView(_view.WorldRoot);
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
        ApplySettings();

        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--screenshot=")) _shotPath = arg["--screenshot=".Length..];

        NewMatch();
    }

    /// <summary>A fresh match (first launch, or Restart from the pause menu).</summary>
    void NewMatch()
    {
        _runner?.Stop();
        _match = new Match(seed: DateTime.Now.Ticks % 2147483647);
        // Names and kits are read before the match's own thread starts.
        _hud.SetMatch(_match);
        _runner = new MatchRunner(_match);
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
        if (_runner == null || _shotPath != null) return;
        if (what == NotificationApplicationPaused || what == NotificationApplicationFocusOut) _pause.Open();
    }

    public override void _ExitTree() => _runner?.Stop();

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
        _players.Update(_prev, _cur, alpha);
        _view.Present(_camera.SubPixelX, _camera.SubPixelY);

        bool attack = _cur.HumanAttacking;
        _controls.SetMode(attack ? TouchControls.Mode.Attack : TouchControls.Mode.Defend);
        _hud.Tick(_prev, _cur, alpha, _controls.Input, attack, delta);

        _time += delta;
        if (_shotPath != null && _time > 6)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            GetTree().Quit();
            _shotPath = null;
        }
    }
}
