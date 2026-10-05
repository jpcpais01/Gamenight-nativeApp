using System;
using Godot;
using GameNight.Grounds;

namespace GameNight.Menus;

/// <summary>
/// The club studio's GOAL tab: pick the explosion that goes off in the net when you score. The
/// stage plays it for real (World/GoalFx.cs) on a little goalmouth of its own, over and over.
/// </summary>
public sealed partial class ClubScreen
{
    SubViewportContainer _pv;
    SubViewport _vp;
    GoalFx _fx;
    Vector3 _eye;
    double _fxT, _fxLast, _fxNext;
    int _fxShown = -1;

    /// <summary>The preview on (in the stage) or put away.</summary>
    void GoalPreview(bool on, Rect2 stage)
    {
        if (!on)
        {
            if (_pv == null || !_pv.Visible) return;
            _pv.Visible = false;
            _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            _fx.Clear();
            _fxShown = -1;
            return;
        }
        if (_pv == null) BuildPreview();
        var r = stage.Grow(-6);
        _pv.Position = r.Position;
        _pv.Size = r.Size;
        if (!_pv.Visible)
        {
            _pv.Visible = true;
            _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        }

        // Its own clock: set the chosen one off now and again.
        double dt = Math.Clamp(T - _fxLast, 0, 0.1);
        _fxLast = T;
        _fxT += dt;
        int style = Math.Clamp(Club.S.GoalFx, 0, GoalFx.Count - 1);
        if (style != _fxShown || _fxT >= _fxNext)
        {
            _fxShown = style;
            _fxNext = _fxT + 5.5;
            var k = Club.S.Kit;
            _fx.Fire(style, 53, 1.2f, (float)GD.RandRange(-1.5, 1.5), k.Main, k.Secondary);
        }
        _fx.Update((float)dt, _fxT, _eye);
    }

    void BuildPreview()
    {
        _pv = new SubViewportContainer { Stretch = true, StretchShrink = 2, TextureFilter = TextureFilterEnum.Nearest, MouseFilter = MouseFilterEnum.Ignore };
        _vp = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Disabled, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        _pv.AddChild(_vp);
        AddChild(_pv);

        var root = new Node3D();
        _vp.AddChild(root);
        _vp.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0x0d1030ff),
                TonemapMode = Godot.Environment.ToneMapper.Linear,
            },
        });
        // Looking at the goal from just off the six-yard box, a little above head height.
        const float hl = 52.5f;
        _eye = new Vector3(hl - 13, 4.5f, -7);
        var cam = new Camera3D { Fov = 62, Current = true };
        root.AddChild(cam);
        cam.LookAtFromPosition(_eye, new Vector3(hl, 5, 0), Vector3.Up);

        // The grass, mown in stripes; the lines; the goal and its net.
        var stripes = Image.CreateEmpty(2, 1, false, Image.Format.Rgb8);
        stripes.SetPixel(0, 0, new Color(0x3f8f3aff));
        stripes.SetPixel(1, 0, new Color(0x367f32ff));
        var grass = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(stripes),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Uv1Scale = new Vector3(8, 1, 1),
        };
        root.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(70, 60) }, MaterialOverride = grass, Position = new Vector3(hl - 25, 0, 0) });
        var line = new StandardMaterial3D { AlbedoColor = new Color(0xf2f0e6ff), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        void Line(Vector3 a, Vector3 b)
        {
            var d = b - a;
            var size = new Vector3(Mathf.Max(Mathf.Abs(d.X), 0.12f), 0.01f, Mathf.Max(Mathf.Abs(d.Z), 0.12f));
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = line, Position = (a + b) / 2 + new Vector3(0, 0.01f, 0) });
        }
        Line(new Vector3(hl, 0, -30), new Vector3(hl, 0, 30));
        Line(new Vector3(hl, 0, -9.16f), new Vector3(hl - 5.5f, 0, -9.16f));
        Line(new Vector3(hl, 0, 9.16f), new Vector3(hl - 5.5f, 0, 9.16f));
        Line(new Vector3(hl - 5.5f, 0, -9.16f), new Vector3(hl - 5.5f, 0, 9.16f));
        Line(new Vector3(hl, 0, -20.16f), new Vector3(hl - 16.5f, 0, -20.16f));
        Line(new Vector3(hl - 16.5f, 0, -20.16f), new Vector3(hl - 16.5f, 0, 20.16f));
        _ = new GameNight.Render.Goals(root);

        // A packed stand behind the goal: a speckle of fans under a roof.
        var rng = new Random(3);
        var fans = Image.CreateEmpty(160, 32, false, Image.Format.Rgb8);
        int[] shirts = { 0xc8393b, 0xf3ede0, 0x23345e, 0xe0c060, 0x2a2a2a, 0x8f1f24, 0x4aa3ff, 0xd8b08c };
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 160; x++)
            {
                var c = new Color((uint)(shirts[rng.Next(shirts.Length)] << 8 | 0xff));
                fans.SetPixel(x, y, c.Darkened(0.6f + (float)rng.NextDouble() * 0.2f));
            }
        var stand = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(fans),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(70, 14) },
            MaterialOverride = stand,
            Position = new Vector3(hl + 9, 8, 0),
            Rotation = new Vector3(0, -Mathf.Pi / 2, 0),
        });
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0x15142aff), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(4, 1.2f, 70) }, MaterialOverride = dark, Position = new Vector3(hl + 8, 15.6f, 0) });
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.5f, 1, 70) }, MaterialOverride = dark, Position = new Vector3(hl + 6.5f, 0.5f, 0) });

        _fx = new GoalFx(root)
        {
            Lightning = false,
            Sound = (style, cue) => { if (Visible) GameNight.Audio.GameAudio.Instance?.GoalFx(style, cue); },
        };
    }

    void GoalTab(Rect2 stage, float px, float pw)
    {
        Px.Text(this, Px.Small, new Vector2(stage.Position.X + 12, stage.End.Y - 12), "YOUR GOAL EXPLOSION", 8, Px.InkDim);
        Px.Text(this, Px.Big, new Vector2(px, 82), "GOAL EXPLOSION", 24, Px.Ink);
        int cur = Math.Clamp(Club.S.GoalFx, 0, GoalFx.Count - 1);
        float top = 96, gap = 6;
        float cw = (pw - gap) / 2, ch = Mathf.Min(54, (Size.Y - 14 - top - gap * 4) / 5);
        for (int i = 0; i < GoalFx.Count; i++)
        {
            int idx = i;
            var r = new Rect2(px + i % 2 * (cw + gap), top + i / 2 * (ch + gap), cw, ch);
            bool on = i == cur;
            Px.Frame(this, r, on ? new Color(0.35f, 0.3f, 0.05f, 0.55f) : Panel, on ? Px.Gold : Px.Line2, null, on ? 3 : 2, 0);
            var sw = new Rect2(r.Position.X + 8, r.Position.Y + 8, 10, ch - 16);
            DrawRect(sw.Grow(1), Colors.Black);
            DrawRect(sw, Px.Hex(GoalFx.Swatches[i]));
            float tx = sw.End.X + 9, tw = r.End.X - tx - 6;
            Px.Text(this, Px.Big, new Vector2(tx, r.Position.Y + 22), GoalFx.Names[i], 18, on ? Px.Gold : Px.Ink);
            var words = Px.Wrap(Px.Small, GoalFx.Blurbs[i], 8, tw);
            for (int l = 0; l < Math.Min(2, words.Count); l++)
                Px.Text(this, Px.Small, new Vector2(tx, r.Position.Y + 34 + l * 10), words[l], 8, Px.InkDim);
            Tap("fx" + i, r, () =>
            {
                if (idx == Club.S.GoalFx) _fxNext = 0;
                else Club.SetGoalFx(idx);
            });
        }
    }
}
