using System;
using Godot;
using GameNight.Render;
using GameNight.Sim;

namespace GameNight.Debug;

/// <summary>
/// Dev tool: one action posed at eight moments side by side, to look at an animation frame by
/// frame. `godot res://Scripts/Debug/PoseLab.tscn -- --pose=slide|fall|fallf --screenshot=out.png`.
/// </summary>
public partial class PoseLab : Node3D
{
    const int Count = 8;
    readonly MatchSnapshot _s = new();
    PlayersView _view;
    string _pose = "slide", _shot;
    double _t;

    public override void _Ready()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--pose=")) _pose = a[7..];
            if (a.StartsWith("--screenshot=")) _shot = a[13..];
        }
        var env = new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.55f, 0.7f, 0.85f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.5f } };
        AddChild(env);
        var sun = new DirectionalLight3D { LightEnergy = 1.2f, ShadowEnabled = true };
        AddChild(sun);
        sun.LookAtFromPosition(new Vector3(3, 8, 6), Vector3.Zero, Vector3.Up);
        var ground = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(60, 20) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.5f, 0.2f) } };
        AddChild(ground);
        var cam = new Camera3D { Fov = 14 };
        AddChild(cam);
        float mid = (Count - 1) * 2.0f / 2;
        cam.LookAtFromPosition(new Vector3(mid, 1.6f, 21), new Vector3(mid, 0.55f, 0), Vector3.Up);

        _view = new PlayersView(this, false) { Bodies = Count, Markers = false };
        var kit = new Kit { Shirt = 0xd8262f, Shirt2 = 0xffffff, Shorts = 0xffffff, Socks = 0xd8262f, GkShirt = 0xe9f23a, GkShorts = 0x1b1b1d };
        for (int i = 0; i < Count; i++)
        {
            var at = new Attributes { Pace = 0.6, Accel = 0.6, Agility = 0.6, Stamina = 0.7, Strength = 0.6, Jumping = 0.5, Power = 0.6, Height = 1.82, Weight = 78 };
            var look = new Look { Skin = 0xc68a5c, Hair = 0x2e1f15, HairStyle = i % 4, Height = 1.01, Build = 1 };
            var p = new Player(i, 0, 5, Role.MID, 0, 0, at, look) { Name = "Lab" + i, Number = 8 };
            _view.SetBody(i, p, kit, 8, false);
            _s.Active[i] = true;
            _s.Team[i] = 0;
            _s.Height[i] = 1.01f;
            _s.Build[i] = 1;
            _s.Stamina[i] = 1;
            _s.Foot[i] = 1;
            _s.KickLeg[i] = 1;
            _s.SinceTouch[i] = 99;
            _s.PullT[i] = -1;
            _s.Role[i] = Role.MID;
        }
        _view.Flush();
        _s.Scorer = _s.Controlled = _s.Owner = _s.HeldBy = _s.DeadBallTaker = -1;
        _s.Phase = Phase.Play;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        bool slide = _pose == "slide";
        float v0 = 7.5f, stop = v0 / (float)Player.SlideDecel;
        float dur = slide ? stop + (float)Match.SlideGetUp : 1.3f;
        for (int i = 0; i < Count; i++)
        {
            // Eight moments: the first at the stop of the slide (or 30% into the fall), the last just before the end.
            float from = slide ? stop - 0.15f : dur * 0.3f;
            float t = from + (dur - 0.02f - from) * i / (Count - 1);
            _s.X[i] = i * 2.0f;
            _s.Z[i] = 0;
            _s.Facing[i] = 0;
            float v = slide ? MathF.Max(0, v0 - (float)Player.SlideDecel * t) : 0;
            _s.VX[i] = v;
            _s.VZ[i] = 0;
            _s.Speed[i] = v;
            _s.Action[i] = slide ? ActionKind.Slide : ActionKind.Fall;
            _s.ActionT[i] = t;
            _s.ActionDur[i] = dur;
            _s.ActionDirX[i] = slide || _pose == "fallf" ? 1 : -1;
            _s.ActionDirZ[i] = 0;
            _s.SlideV0[i] = v0;
            _s.SlideStop[i] = stop;
            _s.LegX[i] = 1;
            _s.LegZ[i] = 0;
        }
        _s.Time = _t;
        _view.Update(_s, _s, 0, _t, 1);
        if (_shot != null && _t > 1.5)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shot);
            GetTree().Quit();
            _shot = null;
        }
    }
}
