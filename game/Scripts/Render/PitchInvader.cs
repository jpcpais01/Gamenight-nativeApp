using System;
using Godot;
using GameNight.Audio;
using GameNight.Sim;

namespace GameNight.Render;

/// <summary>
/// The pitch invader (Match.Invader.cs stops play for him). A shirtless fan in jeans drops over
/// the boards and runs about the pitch; three stewards in high-vis come after him. He dodges the
/// first lunge (the crowd: "olé!"), then one of them brings him down, two take an arm each and
/// walk him off at the nearest touchline. Once he's over the line the match is told and the
/// referee drops the ball. Drawn like the officials: bodies of their own, moved here every frame
/// with the players' movement, not part of the match.
/// </summary>
public sealed class PitchInvader
{
    const float HL = 52.5f, HW = 34f;
    const int Fan = 0, Count = 4;

    public enum Beat { On, Ole, Down, Off }

    enum Stage { Off, Run, Down, Escort }

    readonly Player[] _all = new Player[Count];
    readonly PlayersView _view;
    readonly MatchSnapshot _snap = new();
    readonly Action<Match> _look, _end;
    Stage _stage;
    Random _rng = new();
    float _t, _stageT, _side, _wx, _wz, _nextPick, _catchAfter, _sendLook, _lookX, _lookZ, _exitX, _exitZ, _turn;
    float _ole = -1, _offAt;
    int _tackler, _leg;
    bool _missed, _released;

    /// <summary>The crowd's moments (Main hands them to the terraces).</summary>
    public Action<Beat> Cue;

    /// <summary>He's on the pitch (until he's walked off): what the camera follows.</summary>
    public Vector2? Focus => _stage != Stage.Off && !_released ? new Vector2((float)_all[Fan].Pos.X, (float)_all[Fan].Pos.Z) : null;

    /// <summary>Play is stopped for him (the controls stand down).</summary>
    public bool Holding => _stage != Stage.Off && !_released;

    public PitchInvader(Node3D root)
    {
        static Attributes Attrs(double weight) => new()
        {
            Pace = 1, Accel = 0.8, Control = 0.5, Passing = 0.5, Shooting = 0.3, Strength = 0.6, Defending = 0.3,
            Keeping = 0.2, Agility = 0.8, Stamina = 1, Jumping = 0.4, Power = 0.4, Height = 1.8, Weight = weight,
        };
        static Look Look(int skin, int hair, int style, double h, double b) => new() { Skin = skin, Hair = hair, HairStyle = style, Height = h, Build = b };
        _all[0] = new Player(0, 2, 0, Role.MID, 0, 0, Attrs(72), Look(0xf1c9a5, 0xc9a15a, 0, 0.98, 0.95));
        _all[1] = new Player(1, 2, 1, Role.MID, 0, 0, Attrs(92), Look(0xc68a5c, 0x1b1410, 0, 1.04, 1.12));
        _all[2] = new Player(2, 2, 2, Role.MID, 0, 0, Attrs(88), Look(0xe0ac7e, 0x2e1f15, 1, 1.02, 1.1));
        _all[3] = new Player(3, 2, 3, Role.MID, 0, 0, Attrs(84), Look(0x8d5a3b, 0x120d0a, 2, 1, 1.06));

        _view = new PlayersView(root, false) { Bodies = Count };
        // Shirtless, in jeans; the stewards in high-vis (a silver reflective band) and black trousers.
        var fan = new Kit { Shirt = 0xf1c9a5, Shirt2 = 0xf1c9a5, Shorts = 0x3a5a8c, Socks = 0x3a5a8c, GkShirt = 0, GkShorts = 0 };
        var vis = new Kit { Shirt = 0xd8f23a, Shirt2 = 0xc9ced6, Shorts = 0x1b1c20, Socks = 0x1b1c20, GkShirt = 0, GkShorts = 0 };
        for (int i = 0; i < Count; i++) _view.SetBody(i, _all[i], i == Fan ? fan : vis, -1, false);
        _view.Flush();
        for (int i = 0; i < Count; i++)
        {
            _snap.Active[i] = true;
            _snap.Team[i] = 2;
            _snap.Role[i] = Role.MID;
            _snap.Height[i] = (float)_all[i].Look.Height;
            _snap.Build[i] = (float)_all[i].Look.Build;
            _snap.Stamina[i] = 1;
            _snap.Foot[i] = 1;
            _snap.SinceTouch[i] = 99;
            _snap.PullT[i] = -1;
        }
        _view.Visible = false;
        _look = m =>
        {
            m.InvaderLookX = _lookX;
            m.InvaderLookZ = _lookZ;
        };
        _end = m => m.EndInvader();
    }

    /// <summary>Gone at once (a new match, or skipped): the match drops the ball now.</summary>
    public void Clear(MatchRunner runner)
    {
        if (_stage != Stage.Off && !_released) runner?.Invoke(_end);
        _stage = Stage.Off;
        _released = false;
        _view.Visible = false;
    }

    /// <summary>One frame: start him when the match stops for him, move everyone on (dt 0 holds
    /// them), draw.</summary>
    public void Update(MatchSnapshot s, MatchRunner runner, double time, float dt)
    {
        if (_stage == Stage.Off)
        {
            if (s.InvaderT < 0 || s.InvaderT > 2) return;
            Begin(s);
        }
        else if (s.InvaderT < 0 && !_released)
        {
            // The match got there first (it waited a minute, or the half ended): off he goes.
            Clear(null);
            return;
        }
        if (dt > 0) Step(s, runner, MathF.Min(dt, 1 / 30f));
        if (_stage == Stage.Off) return;
        Draw(s, time);
    }

    void Begin(MatchSnapshot s)
    {
        _rng = new Random(s.InvaderSeed);
        _stage = Stage.Run;
        _t = _stageT = 0;
        _released = _missed = false;
        _ole = -1;
        _tackler = -1;
        // Over the boards on either touchline, somewhere along it.
        _side = _rng.Next(2) == 0 ? 1 : -1;
        float x = (float)(_rng.NextDouble() * 2 - 1) * 36;
        float edge = HW + 3.2f;
        var fan = _all[Fan];
        Place(fan, x, _side * edge, -_side);
        fan.StartAction(ActionKind.Stumble, 0.45, 0, -_side);
        // The stewards: two further along that touchline, one from behind the nearer goal line.
        Place(_all[1], x - 13 - (float)_rng.NextDouble() * 6, _side * edge, -_side);
        Place(_all[2], x + 13 + (float)_rng.NextDouble() * 6, _side * edge, -_side);
        float end = x < 0 ? -1 : 1;
        Place(_all[3], end * (HL + 3.5f), _side * (8 + (float)_rng.NextDouble() * 14), 0);
        _all[3].Facing = end > 0 ? Math.PI : 0;
        // First he heads for the middle.
        _wx = x * 0.4f;
        _wz = -_side * (4 + (float)_rng.NextDouble() * 10);
        _nextPick = 2.6f;
        _turn = _rng.Next(2) == 0 ? 1 : -1;
        _catchAfter = 8 + (float)_rng.NextDouble() * 5;
        _lookX = x;
        _lookZ = _side * HW;
        _view.Snap();
        _view.Visible = true;
        Cue?.Invoke(Beat.On);
    }

    static void Place(Player p, float x, float z, float faceZ)
    {
        p.Pos.Set(x, 0, z);
        p.PrevPos.Copy(p.Pos);
        p.Vel.Set(0, 0, 0);
        p.Facing = faceZ > 0 ? Math.PI / 2 : faceZ < 0 ? -Math.PI / 2 : 0;
        p.PrevFacing = p.Facing;
        p.Action = ActionKind.None;
        p.LookAt = null;
        p.MoveX = p.MoveZ = p.WantSpeed = 0;
    }

    float Rnd() => (float)_rng.NextDouble();

    void Step(MatchSnapshot s, MatchRunner runner, float dt)
    {
        _t += dt;
        _stageT += dt;
        var fan = _all[Fan];
        switch (_stage)
        {
            case Stage.Run: Run(dt); break;
            case Stage.Down:
                // The tackler gets up off him; the other two come to take an arm each.
                for (int i = 1; i < Count; i++)
                {
                    var o = _all[i];
                    if (o.Action != ActionKind.None) continue;
                    if (i == _tackler) Steer(o, (float)fan.Pos.X, (float)fan.Pos.Z, 1.4f, 2.5f);
                    else
                    {
                        float side = i == Side(0) ? 1 : -1;
                        Steer(o, (float)fan.Pos.X + side * 1.2f, (float)fan.Pos.Z + 0.6f, 0.5f, 6.5f);
                    }
                }
                Steer(fan, (float)fan.Pos.X, (float)fan.Pos.Z, 1, 0);
                if (_stageT > 2.5f) Escort();
                break;
            case Stage.Escort: Walk(runner); break;
        }
        foreach (var o in _all) o.Move(dt);

        // The players turn to watch him.
        _lookX += ((float)fan.Pos.X - _lookX) * MathF.Min(1, dt * 4);
        _lookZ += ((float)fan.Pos.Z - _lookZ) * MathF.Min(1, dt * 4);
        if ((_sendLook -= dt) <= 0 && !_released)
        {
            _sendLook = 0.2f;
            runner?.Invoke(_look);
        }
    }

    int Side(int k)
    {
        // The two stewards who weren't the tackler, in order.
        int n = 0;
        for (int i = 1; i < Count; i++)
        {
            if (i == _tackler) continue;
            if (n++ == k) return i;
        }
        return 1;
    }

    void Run(float dt)
    {
        var fan = _all[Fan];
        float fx = (float)fan.Pos.X, fz = (float)fan.Pos.Z;
        // The nearest steward on his feet.
        int near = -1;
        float nd = 1e9f;
        for (int i = 1; i < Count; i++)
        {
            var o = _all[i];
            if (o.Action != ActionKind.None) continue;
            float d = Dist(o, fx, fz);
            if (d < nd)
            {
                nd = d;
                near = i;
            }
        }

        // His run: a point on the pitch, picked again every few seconds or on arrival.
        _nextPick -= dt;
        if (_nextPick <= 0 || MathF.Abs(_wx - fx) + MathF.Abs(_wz - fz) < 3)
        {
            _wx = (Rnd() * 2 - 1) * (HL - 12);
            _wz = (Rnd() * 2 - 1) * (HW - 8);
            _nextPick = 2.5f + Rnd() * 2;
            if (Rnd() < 0.3f) _turn = -_turn;
        }
        float mx = _wx - fx, mz = _wz - fz;
        Norm(ref mx, ref mz);
        if (near >= 0 && nd < 7)
        {
            // Away from him, swerving round (the way he's chosen), harder the closer he is.
            var o = _all[near];
            float ax = fx - (float)o.Pos.X, az = fz - (float)o.Pos.Z;
            Norm(ref ax, ref az);
            float k = 1 - nd / 7;
            mx = mx * 0.5f + (ax * 1.4f - az * _turn * 0.9f) * k;
            mz = mz * 0.5f + (az * 1.4f + ax * _turn * 0.9f) * k;
            Norm(ref mx, ref mz);
        }
        // The touchlines and goal lines turn him back in.
        if (MathF.Abs(fx) > HL - 4) mx -= MathF.Sign(fx) * (MathF.Abs(fx) - HL + 4) * 0.6f;
        if (MathF.Abs(fz) > HW - 4) mz -= MathF.Sign(fz) * (MathF.Abs(fz) - HW + 4) * 0.6f;
        Norm(ref mx, ref mz);
        bool landed = fan.Action == ActionKind.None;
        float tired = _t > _catchAfter + 4 ? 5.4f : _t > _catchAfter ? 6.6f : 7.3f;
        fan.MoveX = mx;
        fan.MoveZ = mz;
        fan.WantSpeed = landed ? tired : 0;
        fan.Sprinting = true;
        fan.LookAt = null;

        // The stewards: after a moment to react, flat out at where he's going.
        for (int i = 1; i < Count; i++)
        {
            var o = _all[i];
            if (o.Action != ActionKind.None) continue;
            float react = 0.7f + i * 0.35f;
            if (_t < react) continue;
            float d = Dist(o, fx, fz);
            float lead = Math.Clamp(d / 7, 0, i == 3 ? 1.8f : 1.1f);
            float tx = fx + (float)fan.Vel.X * lead, tz = fz + (float)fan.Vel.Z * lead;
            float pace = MathF.Min(8.6f, 6.1f + 0.12f * _t + (_t > _catchAfter + 3 ? 2 : 0));
            Steer(o, tx, tz, 0, pace);
            o.Sprinting = true;

            if (d < 1.9f && landed)
            {
                float dx = fx - (float)o.Pos.X, dz = fz - (float)o.Pos.Z;
                Norm(ref dx, ref dz);
                if (!_missed && _t < _catchAfter)
                {
                    // The first lunge: he skips round it.
                    _missed = true;
                    Lunge(o, dx, dz);
                    _turn = -_turn;
                    _wx = fx - dz * _turn * 14;
                    _wz = fz + dx * _turn * 14;
                    _nextPick = 2;
                    fan.Vel.Set(fan.Vel.X - dz * _turn * 2.5, 0, fan.Vel.Z + dx * _turn * 2.5);
                    Cue?.Invoke(Beat.Ole);
                }
                else if (_t >= _catchAfter || _missed && _t > 5)
                {
                    // Brought down.
                    Lunge(o, dx, dz);
                    _tackler = i;
                    fan.StartAction(ActionKind.Fall, 2.6, dx, dz);
                    fan.Vel.Set(fan.Vel.X * 0.5 + dx * 2.2, 0, fan.Vel.Z * 0.5 + dz * 2.2);
                    _stage = Stage.Down;
                    _stageT = 0;
                    Cue?.Invoke(Beat.Down);
                    return;
                }
            }
        }
    }

    /// <summary>A diving slide at him (the players' slide tackle, along the grass).</summary>
    static void Lunge(Player o, float dx, float dz)
    {
        o.StartAction(ActionKind.Slide, 1.1, dx, dz);
        o.Facing = Math.Atan2(dz, dx);
        o.Vel.Set(dx * 7.2, 0, dz * 7.2);
        o.SlideV0 = 7.2;
        o.SlideStop = 0.6;
        o.LegX = dx;
        o.LegZ = dz;
        o.KickLeg = 1;
    }

    void Escort()
    {
        var fan = _all[Fan];
        _stage = Stage.Escort;
        _stageT = 0;
        // Straight off at the nearest touchline, then along it, behind the nearer goal line.
        float fz = (float)fan.Pos.Z;
        _exitZ = (fz >= 0 ? 1 : -1) * (HW + 1.1f);
        _exitX = (float)fan.Pos.X;
        _leg = 0;
    }

    void Walk(MatchRunner runner)
    {
        var fan = _all[Fan];
        float fx = (float)fan.Pos.X, fz = (float)fan.Pos.Z;
        float dx = _exitX - fx, dz = _exitZ - fz;
        Norm(ref dx, ref dz);
        float pace = MathF.Min(2.7f, 0.8f + _stageT * 1.5f);
        if (_leg == 0 && MathF.Abs(fz) > HW + 0.8f)
        {
            _leg = 1;
            _exitX = (fx >= 0 ? 1 : -1) * (HL + 6);
        }
        Steer(fan, _exitX, _exitZ, 0, pace);
        // One at each arm, the third a step behind.
        Steer(_all[Side(0)], fx - dz * 0.72f + dx * 0.1f, fz + dx * 0.72f + dz * 0.1f, 0, pace + 2.5f);
        Steer(_all[Side(1)], fx + dz * 0.72f + dx * 0.1f, fz - dx * 0.72f + dz * 0.1f, 0, pace + 2.5f);
        Steer(_all[_tackler], fx - dx * 1.5f, fz - dz * 1.5f, 0, pace + 2.5f);
        for (int i = 0; i < Count; i++)
        {
            if (i == Fan) continue;
            var o = _all[i];
            o.LookTarget.Set(fx + dx * 3, 0, fz + dz * 3);
            o.LookAt = o.LookTarget;
        }
        if (!_released && MathF.Abs(fz) > HW + 0.6f)
        {
            // Over the line: play on.
            _released = true;
            _offAt = _t;
            runner?.Invoke(_end);
            Cue?.Invoke(Beat.Off);
        }
        // Round behind the goal line, and gone (or, at the latest, out of the picture by now).
        if (MathF.Abs(fx) > HL + 2.5f || (_released && _t - _offAt > 30))
        {
            _stage = Stage.Off;
            _view.Visible = false;
        }
    }

    static float Dist(Player o, float x, float z)
    {
        float dx = (float)o.Pos.X - x, dz = (float)o.Pos.Z - z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    static void Norm(ref float x, ref float z)
    {
        float d = MathF.Sqrt(x * x + z * z);
        if (d < 1e-4f) return;
        x /= d;
        z /= d;
    }

    static void Steer(Player o, float x, float z, float stopAt, float speed)
    {
        float dx = x - (float)o.Pos.X, dz = z - (float)o.Pos.Z;
        float d = MathF.Sqrt(dx * dx + dz * dz);
        if (d < MathF.Max(0.15f, stopAt))
        {
            o.MoveX = o.MoveZ = o.WantSpeed = 0;
            o.Sprinting = false;
            return;
        }
        o.MoveX = dx / d;
        o.MoveZ = dz / d;
        o.WantSpeed = MathF.Min(speed, d * 2.5f + 0.3f);
        o.Sprinting = o.WantSpeed > PlayerK.JogSpeed;
    }

    void Draw(MatchSnapshot s, double time)
    {
        for (int i = 0; i < Count; i++)
        {
            var o = _all[i];
            _snap.X[i] = (float)o.Pos.X;
            _snap.Z[i] = (float)o.Pos.Z;
            _snap.VX[i] = (float)o.Vel.X;
            _snap.VZ[i] = (float)o.Vel.Z;
            _snap.Facing[i] = (float)o.Facing;
            _snap.Speed[i] = (float)o.Speed;
            _snap.HasLook[i] = o.LookAt != null;
            _snap.LookX[i] = o.LookAt != null ? (float)o.LookAt.X : 0;
            _snap.LookZ[i] = o.LookAt != null ? (float)o.LookAt.Z : 0;
            _snap.StridePhase[i] = (float)o.StridePhase;
            _snap.LeanFwd[i] = (float)o.LeanFwd;
            _snap.LeanSide[i] = (float)o.LeanSide;
            _snap.AccelFwd[i] = (float)o.AccelFwd;
            _snap.Sprinting[i] = o.Sprinting;
            _snap.Action[i] = o.Action;
            _snap.ActionT[i] = (float)o.ActionT;
            _snap.ActionDur[i] = (float)o.ActionDur;
            _snap.ActionDirX[i] = (float)o.ActionDirX;
            _snap.ActionDirZ[i] = (float)o.ActionDirZ;
            _snap.SlideV0[i] = (float)o.SlideV0;
            _snap.SlideStop[i] = (float)o.SlideStop;
            _snap.LegX[i] = (float)o.LegX;
            _snap.LegZ[i] = (float)o.LegZ;
            _snap.KickLeg[i] = (sbyte)o.KickLeg;
        }
        _snap.BallX = s.BallX;
        _snap.BallY = s.BallY;
        _snap.BallZ = s.BallZ;
        _snap.Time = time;
        _snap.Phase = Phase.Play;
        _snap.Scorer = _snap.Controlled = _snap.Owner = _snap.HeldBy = _snap.DeadBallTaker = -1;
        _view.Update(_snap, _snap, 0, time, 1);
    }

    /// <summary>What the terraces do at each moment: a cheer and a laugh as he gets on, "olé!"
    /// as he skips the lunge, a roar and a laugh as he goes down, applause as he's walked off.</summary>
    public static void Crowd(Terraces t, Beat beat)
    {
        switch (beat)
        {
            case Beat.On:
                t.Cue(React.Cheer, 0, 0.2, 0.9f);
                t.Cue(React.Cheer, 1, 0.35, 0.8f);
                t.Cue(React.Laugh, -1, 0.6, 0.9f, 3);
                t.Cue(React.Whistler, 0, 0.9, 1);
                t.Cue(React.Applause, -1, 1.6, 0.6f, 4);
                break;
            case Beat.Ole:
                t.Cue(React.Ole, -1, 0.15, 1);
                t.Cue(React.Laugh, -1, 0.9, 1, 3);
                break;
            case Beat.Down:
                t.Cue(React.Ooh, -1, 0, 0.8f);
                t.Cue(React.Cheer, -1, 0.35, 1);
                t.Cue(React.Laugh, -1, 0.8, 1, 3.5f);
                t.Cue(React.Applause, -1, 1.5, 0.8f, 4);
                break;
            case Beat.Off:
                t.Cue(React.Applause, -1, 0, 0.7f, 3.5f);
                t.Cue(React.Heckle, 0, 0.4, 1);
                break;
        }
    }
}
