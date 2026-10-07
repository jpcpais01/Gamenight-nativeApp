using System;
using System.Collections.Generic;
using Godot;
using GameNight.Audio;
using GameNight.Club;
using GameNight.Render;
using GameNight.Sim;

namespace GameNight.Menus;

/// <summary>
/// The mystery walkout: after the clues, the player himself walks out of a blazing tunnel mouth
/// through the smoke, a dark silhouette against the light; he stops, the lights slam on and
/// there he is in full, in his own look and your kit, celebrating while his name lands. He is
/// the real match figure (Render/PlayersView) in a little 3D scene of his own, drawn over the
/// vault at the game's pixel size. A tap hurries him along.
/// </summary>
public sealed partial class PackOpening
{
    const float WalkFrom = -9.5f, WalkTo = -0.9f, WalkEnd = 3.3f, RevealAt = 3.55f, TunnelDone = 7.6f;

    SubViewportContainer _tv;
    SubViewport _tvp;
    Camera3D _cam;
    PlayersView _body;
    DirectionalLight3D _key, _fill, _rim;
    Godot.Environment _env;
    Control _front;
    readonly MatchSnapshot _snap = new();
    int _bodyOf = -1;
    bool _revealed;
    double _tt;
    float _stride;
    readonly List<(Vector2 p, float r, float v, float a)> _smoke = new();

    // ---------------------------------------------------------------- the scene

    void BuildTunnel()
    {
        _tv = new SubViewportContainer
        {
            Stretch = true, StretchShrink = 2, TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore, Visible = false,
        };
        _tv.SetAnchorsPreset(LayoutPreset.FullRect);
        _tvp = new SubViewport
        {
            OwnWorld3D = true, TransparentBg = true, Msaa3D = Viewport.Msaa.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        _tv.AddChild(_tvp);
        AddChild(_tv);
        // In front of him: the smoke and his name (plain drawing), then the bright layer on top.
        _front = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _front.SetAnchorsPreset(LayoutPreset.FullRect);
        _front.Draw += () => DrawFront(_front);
        AddChild(_front);
        MoveChild(_light, -1);

        var root = new Node3D();
        _tvp.AddChild(root);
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.5f, 0.55f, 0.8f),
            AmbientLightEnergy = 0.04f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
        };
        _tvp.AddChild(new WorldEnvironment { Environment = _env });
        _cam = new Camera3D { Fov = 34, Current = true };
        root.AddChild(_cam);
        _rim = new DirectionalLight3D { LightEnergy = 1.6f };
        _key = new DirectionalLight3D { LightEnergy = 0 };
        _fill = new DirectionalLight3D { LightEnergy = 0 };
        root.AddChild(_rim);
        root.AddChild(_key);
        root.AddChild(_fill);
        _rim.LookAtFromPosition(new Vector3(0.6f, 3.2f, -14), new Vector3(0, 0.6f, 0), Vector3.Up);
        _key.LookAtFromPosition(new Vector3(1.8f, 4.5f, 7), new Vector3(0, 1, 0), Vector3.Up);
        _fill.LookAtFromPosition(new Vector3(-5, 1.5f, 3), new Vector3(0, 1.1f, 0), Vector3.Up);

        _body = new PlayersView(root, false) { Bodies = 1, Markers = false };
        var s = _snap;
        s.Active[0] = true;
        s.Team[0] = 0;
        s.Height[0] = 1;
        s.Build[0] = 1;
        s.Stamina[0] = 1;
        s.Foot[0] = 1;
        s.SinceTouch[0] = 99;
        s.PullT[0] = -1;
        s.Scorer = s.Controlled = s.Owner = s.HeldBy = s.DeadBallTaker = -1;
    }

    /// <summary>The current card's player, dressed and at the back of the tunnel.</summary>
    void TunnelOn()
    {
        if (_tv == null) BuildTunnel();
        var c = Cur;
        if (_bodyOf != _i)
        {
            _bodyOf = _i;
            var sp = Cards.ToSim(c, c.Position);
            var role = Cards.RoleOf(c.Position);
            var p = new Player(0, 0, 0, role, 0, 0, sp.Attrs, sp.Look) { Name = sp.Name, Number = sp.Number };
            _body.SetBody(0, p, _kit, c.Number, false);
            _body.Flush();
            _snap.Role[0] = role;
            _snap.Height[0] = (float)sp.Look.Height;
            _snap.Build[0] = (float)sp.Look.Build;
            _snap.Foot[0] = (sbyte)sp.Foot;
        }
        _body.Snap();
        _revealed = false;
        _stride = 0;
        _key.LightEnergy = _fill.LightEnergy = 0;
        _rim.LightColor = Tier >= 4 ? Colors.White : Col(Tier).Lerp(Colors.White, 0.45f);
        _env.AmbientLightEnergy = 0.04f;
        _smoke.Clear();
        var rng = new Random(c.Id.GetHashCode());
        for (int i = 0; i < 26; i++)
            _smoke.Add((new Vector2((float)rng.NextDouble(), 0.55f + (float)rng.NextDouble() * 0.4f), 40 + (float)rng.NextDouble() * 70,
                ((float)rng.NextDouble() - 0.5f) * 0.03f, 0.05f + (float)rng.NextDouble() * 0.06f));
        _tv.Visible = true;
        _tvp.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
    }

    void TunnelOff()
    {
        if (_tv == null || !_tv.Visible) return;
        _tv.Visible = false;
        _tvp.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        _front.QueueRedraw();
    }

    /// <summary>The walk: brisk out of the tunnel, slowing to a stop just short of the camera.</summary>
    static float WalkZ(float t)
    {
        float k = Mathf.Clamp(t / WalkEnd, 0, 1);
        k = 1 - (1 - k) * (1 - k) * (1 - k * 0.3f);
        return Mathf.Lerp(WalkFrom, WalkTo, k);
    }

    /// <summary>His celebration, the same one every time he's pulled.</summary>
    CelebrationKind Celebration
    {
        get
        {
            var kinds = Tier >= 4 ? new[] { CelebrationKind.Flip, CelebrationKind.Siu } : new[] { CelebrationKind.Siu, CelebrationKind.Plane, CelebrationKind.Siu, CelebrationKind.Flip };
            return kinds[(int)((uint)Cur.Id.GetHashCode() % kinds.Length)];
        }
    }

    void TunnelTick(float dt)
    {
        if (_tv == null || !_tv.Visible) TunnelOn();
        float t = (float)_t;
        _tt += dt;
        // Footsteps in the dark: a low beat with every other step.
        float z = WalkZ(t), z0 = WalkZ(t - dt);
        float speed = dt > 0 ? Mathf.Max(0, (z - z0) / dt) : 0;
        float before = _stride;
        _stride += speed / (float)Player.StepLength(speed, 1) * Mathf.Pi * dt;
        if (t > 0.2f && t < WalkEnd && Mathf.Floor(before / Mathf.Pi) != Mathf.Floor(_stride / Mathf.Pi)) GameAudio.Instance?.Footstep();

        if (!_revealed && t >= RevealAt) Reveal();
        var s = _snap;
        s.X[0] = 0;
        s.Z[0] = z;
        s.VX[0] = 0;
        s.VZ[0] = speed;
        s.Speed[0] = speed;
        s.Facing[0] = Mathf.Pi / 2;
        s.StridePhase[0] = _stride;
        s.LeanFwd[0] = 0;
        s.AccelFwd[0] = 0;
        s.Time = _tt;
        if (_revealed)
        {
            s.Phase = Sim.Phase.Goal;
            s.Scorer = 0;
            s.Celebration = Celebration;
            s.CelebrationAt = 0;
            s.CelebrationTurn = 1;
            s.PhaseT = Mathf.Min(t - RevealAt - 0.15f, 6.5f);
        }
        else
        {
            s.Phase = Sim.Phase.Play;
            s.Scorer = -1;
        }
        _body.Update(s, s, 0, _tt, 1);

        // The camera eases in as he comes; the lights come up at the reveal.
        float dolly = Mathf.Clamp(t / RevealAt, 0, 1);
        var eye = new Vector3(0, 1.3f, 6.6f - dolly * 1.2f + Mathf.Max(0, t - RevealAt) * 0.12f);
        _cam.LookAtFromPosition(eye, new Vector3(0, 1.15f, z * 0.35f), Vector3.Up);
        // Until the lights come on he is only a shape against the tunnel's glare.
        _tv.Modulate = _revealed ? new Color(0.05f, 0.04f, 0.09f).Lerp(Colors.White, Mathf.Min(1, (t - RevealAt) / 0.12f)) : new Color(0.05f, 0.04f, 0.09f);
        if (_revealed)
        {
            float k = Mathf.Min(1, (t - RevealAt) / 0.18f);
            _key.LightEnergy = 1.25f * k;
            _fill.LightEnergy = 0.45f * k;
            _env.AmbientLightEnergy = Mathf.Lerp(0.04f, 0.35f, k);
            _rim.LightEnergy = Mathf.Lerp(1.6f, 0.9f, k);
        }
        for (int i = 0; i < _smoke.Count; i++)
        {
            var (p, r, v, a) = _smoke[i];
            p.X = (p.X + v * dt + 1.2f) % 1.2f;
            _smoke[i] = (p, r, v, a);
        }
        if (_revealed && Tier >= 3 && _spawn > 0.45)
        {
            _spawn = 0;
            Firework(new Vector2(_fx.Rng.Next(60, (int)Size.X - 60), _fx.Rng.Next(40, (int)(Size.Y * 0.4f))));
        }
        _front.QueueRedraw();
        if (t > TunnelDone) To(Phase.Enter);
    }

    void Reveal()
    {
        _revealed = true;
        _t = Math.Max(_t, RevealAt);
        var c = Tier >= 4 ? Hue : Col(Tier);
        var at = Feet() - new Vector2(0, Size.Y * 0.28f);
        Flash(1.1);
        Shake(0.4, 6);
        _fx.Shock(at, c, 1000, 0.7f, 7);
        _fx.Shock(at, Colors.White, 650, 0.55f, 3);
        _fx.Burst(at, Tier >= 4 ? Rainbow : new[] { c, Colors.White }, 80 + Tier * 20, 520, 120);
        Input.VibrateHandheld(160);
        GameAudio.Instance?.Reveal(Tier);
    }

    void TunnelTap()
    {
        if (!_revealed) Reveal();
        else if (_t > RevealAt + 0.5) To(Phase.Enter);
    }

    /// <summary>A point of his little world on the screen.</summary>
    Vector2 Scr(Vector3 p) => _cam == null ? Size / 2 : _cam.UnprojectPosition(p) * _tv.StretchShrink;

    Vector2 Feet() => Scr(new Vector3(0, 0, WalkZ((float)_t)));

    // ---------------------------------------------------------------- 2D around him

    /// <summary>Behind him: the vault floor, the tunnel mouth blazing, its light spilling out.</summary>
    void TunnelStage()
    {
        if (_cam == null) return;
        var col = Tier >= 4 ? Hue : Col(Tier);
        float t = (float)_t;
        // The mouth: a doorway of light at the back.
        var tl = Scr(new Vector3(-1.5f, 3.6f, -11));
        var br = Scr(new Vector3(1.5f, 0, -11));
        var mouth = new Rect2(tl, br - tl);
        float open = Mathf.Min(1, t / 0.5f);
        var m = mouth.GetCenter();
        Fx.Beams(this, new Vector2(m.X, mouth.Position.Y + mouth.Size.Y * 0.4f), col, 14, Size.Length(), 0.05f, t * 0.12f, 0.06f * open);
        Fx.Glow(this, m, mouth.Size.Y * 1.6f, col, 0.22f * open);
        // The tunnel's concrete face, its lamps, then the doorway of light.
        var face = new Rect2(mouth.Position - new Vector2(mouth.Size.X * 0.45f, mouth.Size.Y * 0.18f), mouth.Size * new Vector2(1.9f, 1.18f));
        Px.Bands(this, face, new[] { Px.Hex(0x15122c), Px.Hex(0x100e24), Px.Hex(0x0b0a1a) }, new[] { 0, 0.5f, 0.85f });
        DrawRect(new Rect2(face.Position.X, face.Position.Y, face.Size.X, 3), new Color(col, 0.35f));
        foreach (float lx in new[] { face.Position.X + face.Size.X * 0.12f, face.End.X - face.Size.X * 0.12f })
        {
            var lamp = new Vector2(lx, face.Position.Y + face.Size.Y * 0.2f);
            DrawRect(new Rect2(lamp - new Vector2(4, 2), new Vector2(8, 4)), Px.Hex(0xfff3c0, open));
            Fx.Glow(this, lamp, 18, Px.Hex(0xfff3c0), 0.25f * open);
        }
        DrawRect(mouth.Grow(4), new Color(0.03f, 0.02f, 0.08f, 1));
        // Its name over the door.
        Px.TextC(this, Px.Small, m.X, face.Position.Y + face.Size.Y * 0.1f, "PLAYERS' TUNNEL", 8, new Color(col.Lerp(Colors.White, 0.5f), open));
        Px.Bands(this, mouth, new[] { col.Lerp(Colors.White, 0.55f), col.Lerp(Colors.White, 0.8f), Colors.White, col.Lerp(Colors.White, 0.7f) }, new[] { 0, 0.3f, 0.55f, 0.85f });
        DrawRect(mouth, new Color(0, 0, 0, 1 - open));
        // Light pouring out across the floor towards us.
        var f0 = Scr(new Vector3(-1.7f, 0, -11));
        var f1 = Scr(new Vector3(1.7f, 0, -11));
        var n0 = Scr(new Vector3(-4.5f, 0, 2));
        var n1 = Scr(new Vector3(4.5f, 0, 2));
        DrawColoredPolygon(new[] { f0, f1, n1, n0 }, new Color(col, 0.12f * open));
        // His shadow, thrown towards us by the light behind.
        var feet = Feet();
        float d = Mathf.Abs(WalkZ(t) - _cam.Position.Z);
        float sw = Size.Y * 0.9f / Mathf.Max(1, d);
        if (_revealed) DrawColoredPolygon(Px.Ellipse(feet, sw * 0.5f, sw * 0.12f, 18), new Color(0, 0, 0, 0.45f));
        else DrawColoredPolygon(new[] { feet + new Vector2(-sw * 0.18f, 0), feet + new Vector2(sw * 0.18f, 0), feet + new Vector2(sw * 0.5f, sw * 1.4f), feet + new Vector2(-sw * 0.5f, sw * 1.4f) }, new Color(0, 0, 0, 0.35f));
        // Smoke behind.
        foreach (var (p, r, _, a) in _smoke)
            if (p.Y < 0.72f) Puff(this, new Vector2(p.X * 1.2f - 0.1f, p.Y) * Size, r * 1.3f, col, a * 0.7f);
    }

    static void Puff(CanvasItem ci, Vector2 c, float r, Color tint, float a)
    {
        var col = new Color(tint.Lerp(new Color(0.7f, 0.72f, 0.82f), 0.75f), a);
        ci.DrawColoredPolygon(Px.Ellipse(c, r, r * 0.42f, 16), col);
        ci.DrawColoredPolygon(Px.Ellipse(c + new Vector2(r * 0.3f, -r * 0.12f), r * 0.6f, r * 0.3f, 14), new Color(col, a * 0.8f));
    }

    /// <summary>In front of him: low smoke, the cinema bars, and once he's lit, his name.</summary>
    void DrawFront(CanvasItem ci)
    {
        if (_phase != Phase.Tunnel || _cam == null) return;
        float W = Size.X, H = Size.Y, t = (float)_t;
        var col = Tier >= 4 ? Hue : Col(Tier);
        float thin = _revealed ? 0.45f : 1;
        foreach (var (p, r, _, a) in _smoke)
            if (p.Y >= 0.72f) Puff(ci, new Vector2(p.X * 1.2f - 0.1f, p.Y) * Size, r, col, a * thin);
        ci.DrawRect(new Rect2(0, 0, W, 34), Colors.Black);
        ci.DrawRect(new Rect2(0, H - 34, W, 34), Colors.Black);
        if (!_revealed)
        {
            if (T % 1.2 < 0.8) Px.TextC(ci, Px.Small, W / 2, H - 14, "WHO IS IT?", 10, Px.InkDim);
            return;
        }
        // His name slams in on the left, the rating on the right.
        var c = Cur;
        float k = Mathf.Min(1, (t - RevealAt - 0.25f) / 0.2f);
        if (k <= 0) return;
        float e = 1 - (1 - k) * (1 - k);
        float x = 36 - (1 - e) * 120;
        var n = Cards.Nations[c.Nation];
        Px.Text(ci, Px.Small, new Vector2(x, H * 0.42f - 40), $"{Cards.Label(c.Rarity).ToUpperInvariant()} · {c.Position}", 10, col);
        var names = c.Name.Split(' ', 2);
        int big = (int)Mathf.Min(64, (W * 0.3f) / Mathf.Max(1, Px.Width(Px.Big, names[^1].ToUpperInvariant(), 1)));
        if (names.Length > 1) Px.Text(ci, Px.Big, new Vector2(x, H * 0.42f - 10), names[0].ToUpperInvariant(), 26, Px.Ink, new Color(0, 0, 0, 0.7f), 3);
        Px.Text(ci, Px.Big, new Vector2(x, H * 0.42f + big * 0.8f), names[^1].ToUpperInvariant(), big, col, new Color(0, 0, 0, 0.75f), 4);
        Px.Flag(ci, new Rect2(x, H * 0.42f + big * 0.8f + 14, 30, 20), n);
        Px.Text(ci, Px.Small, new Vector2(x + 38, H * 0.42f + big * 0.8f + 30), n.Code, 10, Px.Ink);
        float rx = W - 36 + (1 - e) * 120;
        string ovr = c.Overall.ToString();
        Px.TextR(ci, Px.Big, rx, H * 0.42f + 30, ovr, 96, col, new Color(0, 0, 0, 0.75f), 5);
        Px.TextR(ci, Px.Small, rx, H * 0.42f + 50, "OVERALL", 10, Px.Ink);
        var ps = Playstyles.Of(c);
        for (int i = 0; i < ps.Count; i++)
            Art.Playstyle(ci, new Vector2(rx - 14 - i * 34, H * 0.42f + 76), 13, ps[i]);
        if (t > RevealAt + 0.8f && T % 1.2 < 0.8) Px.TextC(ci, Px.Small, W / 2, H - 14, "TAP TO SEE HIS CARD", 10, Px.InkDim);
    }
}
