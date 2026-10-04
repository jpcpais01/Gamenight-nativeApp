using System;
using Godot;
using GameNight.Grounds;
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
    MatchRunner _runner;
    readonly MatchSnapshot _prev = new(), _cur = new();

    PixelView _view;
    Ground _ground;
    MatchCamera _camera;
    PlayersView _players;
    TouchControls _controls;
    Hud _hud;
    /// <summary>Debug: `-- --screenshot=out.png` saves the screen after a few seconds and quits.</summary>
    string _shotPath;
    double _time;

    public override void _Ready()
    {
        Engine.MaxFps = 0;
        DisplayServer.ScreenSetKeepOn(true);

        Kick.PrepareGroundPasses();
        _runner = new MatchRunner(new Match(seed: DateTime.Now.Ticks % 2147483647));

        _view = new PixelView();
        AddChild(_view);
        World.Build(_view.WorldRoot);
        _ground = Ground.Create("big").AddTo(_view.WorldRoot);
        _players = new PlayersView(_view.WorldRoot);
        _camera = new MatchCamera(_view.Camera);

        _hud = new Hud();
        AddChild(_hud);
        _controls = new TouchControls();
        AddChild(_controls);

        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--screenshot=")) _shotPath = arg["--screenshot=".Length..];

        _runner.Start();
    }

    public override void _Notification(int what)
    {
        // Backgrounded or covered: stop the match clock; it resumes without a jump.
        if (_runner == null) return;
        if (what == NotificationApplicationPaused || what == NotificationApplicationFocusOut) _runner.Paused = true;
        else if (what == NotificationApplicationResumed || what == NotificationApplicationFocusIn) _runner.Paused = false;
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
        _camera.Update(_prev, _cur, alpha, dt);
        _players.Update(_prev, _cur, alpha);
        _ground.Update(_cur, _time, dt);
        _view.Present(_camera.SubPixelX, _camera.SubPixelY);

        _controls.SetMode(_cur.HumanAttacking ? TouchControls.Mode.Attack : TouchControls.Mode.Defend);
        _hud.Tick(_cur, delta);

        _time += delta;
        if (_shotPath != null && _time > 6)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            GetTree().Quit();
            _shotPath = null;
        }
    }
}
