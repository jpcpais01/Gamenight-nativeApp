using System;
using Godot;
using GameNight.Bridge;
using GameNight.Render;
using GameNight.UI;

namespace GameNight;

/// <summary>
/// The match screen. The match runs at a fixed 120 steps a second; every rendered frame the
/// view interpolates between the last two steps, so the picture is smooth at any refresh
/// rate. The frame rate is never capped: it runs at whatever the display refreshes at.
/// </summary>
public partial class Main : Node
{
    IMatchSource _match;
    readonly MatchFrame _prev = new(), _cur = new();
    double _acc;

    PixelView _view;
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

        _match = new StubMatch(seed: 1);
        _match.Write(_cur);
        _prev.CopyFrom(_cur);

        _view = new PixelView();
        AddChild(_view);
        World.Build(_view.WorldRoot);
        _players = new PlayersView(_view.WorldRoot);
        _camera = new MatchCamera(_view.Camera);

        _hud = new Hud();
        AddChild(_hud);
        _controls = new TouchControls();
        AddChild(_controls);

        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--screenshot=")) _shotPath = arg["--screenshot=".Length..];
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _controls.Tick(dt);

        // Fixed steps (at most a quarter second's worth after a stall).
        _acc = Math.Min(_acc + delta, 0.25);
        double step = _match.Dt;
        while (_acc >= step)
        {
            _prev.CopyFrom(_cur);
            _match.Step(_controls.Input);
            _match.Write(_cur);
            _acc -= step;
        }
        float alpha = (float)(_acc / step);

        if (_view.Fit())
        {
            _camera.PixelHeight = _view.Viewport.Size.Y;
            _camera.SetAspect(_view.Aspect);
        }
        _camera.Update(_prev, _cur, alpha, dt);
        _players.Update(_prev, _cur, alpha, dt);
        _view.Present(_camera.SubPixelX, _camera.SubPixelY);

        _controls.SetMode(_match.HumanAttacking ? TouchControls.Mode.Attack : TouchControls.Mode.Defend);
        _hud.Tick(_cur, delta);

        _time += delta;
        if (_shotPath != null && _time > 4)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            GetTree().Quit();
            _shotPath = null;
        }
    }
}
