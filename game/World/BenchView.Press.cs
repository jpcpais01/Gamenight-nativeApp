using System;
using Godot;
using GameNight.Render;
using GameNight.Sim;
using Part = GameNight.Render.BodyMeshes.Part;

namespace GameNight.Grounds;

/// <summary>
/// The photographers behind the goal lines: real bodies like the bench's, four either side of
/// each goal, in bibs over dark jackets, each with a long white lens on a monopod, a flash gun on
/// top and his hard case beside him (some sit on it, the rest kneel on one knee).
///
/// They work the game: the lens follows the ball, the camera comes up to the eye as play comes
/// toward their goal (the keen ones sooner), a shot at their end sets off bursts of flashes, and
/// in quiet spells they lower it to check the back screen, rummage in the case for a lens, or
/// turn for a word with the next man. A goal at their end sends them running along the line
/// after the scorer, round the corner if that's where he goes, to kneel and fire away while the
/// team piles on; the far end gets up to shoot it from distance. Then back to their places.
///
/// Drawn in the bench's multimeshes (one more figure each), plus three draws for the kit:
/// cameras, the monopods and cases, and the flash cards.
/// </summary>
public sealed partial class BenchView
{
    const int PerCorner = 4, PressCount = PerCorner * 4, PressSlot = 16;
    const float HL = Pitchside.HL, HW = Pitchside.HW;
    const float CaseTop = 0.38f;
    const float LineX = HL + 3.2f, TrackZ = HW + 2.4f;

    /// <summary>Bib, jacket, trousers, shoes: an outfit per kit slot (16..21).</summary>
    static readonly (int bib, int jacket, int trousers, int shoes)[] PressKit =
    {
        (0xff7a1a, 0x1d1f24, 0x22252b, 0x16161a),
        (0xe8e23a, 0x2a2f3a, 0x1f2229, 0x2b2b2e),
        (0x9be02a, 0x1b2a40, 0x2a2d33, 0x16161a),
        (0x2f6fd8, 0x3a3a3a, 0x1d1f24, 0x6b5a48),
        (0xff7a1a, 0x4a4136, 0x2b2f38, 0x1b1b1d),
        (0x26282d, 0x1d1f24, 0x1d1f24, 0x101012), // no bib, all in black
    };

    // Arm poses (as the bench's: swing, elbow, out, turn; left then right). Camera to the eye
    // with the left hand under the lens and the right elbow up; held at the chest; held up close
    // to look at the back screen; the left hand down in the case.
    static readonly float[] Shoot = { 1.3f, 0.95f, 1.05f, 2.0f, 0.05f, 0.75f, 0, -0.4f };
    static readonly float[] Hold = { 0.5f, 0.45f, 1.45f, 1.5f, 0.12f, 0.16f, -0.3f, -0.3f };
    static readonly float[] Review = { 0.65f, 0.6f, 1.85f, 1.9f, 0.1f, 0.12f, -0.4f, -0.4f };
    static readonly float[] Rummage = { 0.25f, 0.45f, 0.25f, 1.5f, 0.3f, 0.16f, 0, -0.3f };

    enum Idle { Watch, Screen, Case, Chat }

    sealed class Snap
    {
        public int End, Mate;
        public bool Sits, OnCase;
        public int Knee;
        public float HomeX, HomeZ, CaseX, CaseZ, CaseYaw, Spread, Lag;
        public float SpotX, SpotZ, Retarget;
        public float Down = 1, Aim, AimWant;
        public Idle Idle;
        public float IdleUntil;
        public float TX, TY = 0.5f, TZ;
        public float BurstEnd = -1, NextFrame, NextShot, LastFlash = -9;
        public readonly float[] Arms = new float[8];
        public float LeftW = 1, Flex, Head, Side;
    }

    readonly Snap[] _p = new Snap[PressCount];
    MultiMesh _cam, _prop, _flash;
    float[] _camBuf, _propBuf, _flashBuf;
    int _pScore0 = -1, _pScore1 = -1, _goalEnd;
    bool _pReplaying;
    readonly bool _snapDebug = Array.IndexOf(OS.GetCmdlineUserArgs(), "--snap") >= 0;

    void PressInit(Node3D root, Vector4[][] ka, Vector4[][] kb)
    {
        for (int o = 0; o < PressKit.Length; o++)
        {
            var (_, jacket, trousers, _) = PressKit[o];
            int slot = PressSlot + o;
            ka[(int)Part.Torso][slot] = Lin4(jacket, -1);
            kb[(int)Part.Torso][slot] = Lin4(jacket, 0);
            ka[(int)Part.UpperArm][slot] = Lin4(jacket, 0);
            kb[(int)Part.UpperArm][slot] = Lin4(jacket, 0);
            ka[(int)Part.Forearm][slot] = Lin4(jacket, 0);
            ka[(int)Part.ShortsLeg][slot] = Lin4(trousers, 0);
            ka[(int)Part.Shin][slot] = Lin4(trousers, 0);
            ka[(int)Part.Boot][slot] = Lin4(0x1b1b1d, 0);
        }

        _camBuf = new float[PressCount * Stride];
        _propBuf = new float[PressCount * 2 * Stride];
        _flashBuf = new float[PressCount * Stride];
        var gear = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/body.gdshader") };
        _cam = Multi(root, CameraMesh(), PressCount, gear);
        _prop = Multi(root, BoxMesh(), PressCount * 2, gear);
        _flash = Multi(root, CardMesh(), PressCount, new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/flash.gdshader") });

        int n = 0;
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                for (int k = 0; k < PerCorner; k++, n++)
                {
                    int id = Count + n;
                    bool sits = _rng.Next(3) == 0;
                    var p = _p[n] = new Snap
                    {
                        End = sx,
                        Sits = sits,
                        OnCase = sits,
                        Knee = _rng.Next(2),
                        Lag = R(0, 1),
                        Spread = ((sz < 0 ? PerCorner - 1 - k : PerCorner + k) - (PerCorner - 0.5f)) * 1.15f + R(-0.2f, 0.2f),
                        Mate = n + (k % 2 == 0 ? 1 : -1),
                        Idle = (Idle)_rng.Next(2),
                        IdleUntil = R(2, 10),
                        NextShot = R(0, 4),
                    };
                    p.HomeX = sx * (HL + (sits ? 2.75f : 2.9f + (k % 2) * 0.5f));
                    p.HomeZ = sz * (Pitchside.GoalHalf + 1.9f + k * 2.1f + R(-0.2f, 0.2f));
                    p.CaseYaw = sx > 0 ? PI / 2 : -PI / 2;
                    if (sits)
                    {
                        // Sat on the case: it's under him, his feet out in front.
                        p.CaseX = p.HomeX + sx * 0.42f;
                        p.CaseZ = p.HomeZ;
                    }
                    else
                    {
                        // Kneeling: the case on the ground at his left hand.
                        p.CaseX = p.HomeX;
                        p.CaseZ = p.HomeZ - sx * 0.55f;
                    }
                    p.SpotX = p.HomeX;
                    p.SpotZ = p.HomeZ;
                    p.TX = 0;
                    p.TZ = p.HomeZ * 0.3f;

                    var f = _f[id] = new Fig { Team = -1, Ph = R(0, 100), X = p.HomeX, Z = p.HomeZ, Facing = sx > 0 ? PI : 0 };
                    double hM = R(1.66f, 1.9f), wKg = R(62, 98);
                    f.B = Body.Shape(hM, wKg, 0.5, id * 17.3 + _rng.NextDouble() * 50);
                    f.Leg = (float)f.B.Leg;
                    f.HipBase = (THIGH + SHIN) * f.Leg + (HIP_Y - THIGH - SHIN);
                    f.Scale = R(0.96f, 1.03f) * BASE_HEIGHT / (f.HipBase + 0.04f + 0.6f * (float)f.B.TorsoL + ((float)f.B.NeckLen - 1) * 0.08f + HEAD_TOP);
                    f.Hair = _rng.Next(5) - 1;
                    int outfit = _rng.Next(PressKit.Length);
                    _pid[id] = PressSlot + outfit;
                    var (bib, jacket, trousers, shoes) = PressKit[outfit];
                    int skin = TeamData.SkinTones[_rng.Next(TeamData.SkinTones.Length)];
                    int hair = TeamData.HairColors[_rng.Next(TeamData.HairColors.Length)];
                    Paint(id, Part.Torso, bib);
                    Paint(id, Part.UpperArm, jacket);
                    Paint(id, Part.Forearm, jacket);
                    Paint(id, Part.Hand, skin);
                    Paint(id, Part.Pelvis, trousers);
                    Paint(id, Part.ShortsLeg, trousers);
                    Paint(id, Part.Thigh, trousers);
                    Paint(id, Part.Shin, trousers);
                    Paint(id, Part.Neck, skin);
                    Paint(id, Part.Head, skin);
                    Paint(id, Part.HairShort, hair);
                    Paint(id, Part.HairCurly, hair);
                    Paint(id, Part.HairBun, hair);
                    Paint(id, Part.Boot, shoes);

                    // The case: black, or now and then a silver one.
                    var box = new Transform3D(new Basis(Vector3.Up, p.CaseYaw).Scaled(new Vector3(0.52f, CaseTop, 0.34f)), new Vector3(p.CaseX, CaseTop / 2, p.CaseZ));
                    PutGear(_propBuf, n * 2 + 1, box, Lin(_rng.Next(4) == 0 ? 0x9a9ca0 : 0x232427));
                }
    }

    static MultiMesh Multi(Node3D root, Mesh mesh, int count, Material mat)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh };
        mm.InstanceCount = count;
        root.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 30 });
        return mm;
    }

    static void PutGear(float[] a, int i, in Transform3D m, Color c, float c0 = 0)
    {
        int o = i * Stride;
        Vector3 x = m.Basis.Column0, y = m.Basis.Column1, z = m.Basis.Column2;
        a[o] = x.X; a[o + 1] = y.X; a[o + 2] = z.X; a[o + 3] = m.Origin.X;
        a[o + 4] = x.Y; a[o + 5] = y.Y; a[o + 6] = z.Y; a[o + 7] = m.Origin.Y;
        a[o + 8] = x.Z; a[o + 9] = y.Z; a[o + 10] = z.Z; a[o + 11] = m.Origin.Z;
        a[o + 12] = c.R; a[o + 13] = c.G; a[o + 14] = c.B; a[o + 15] = 1;
        a[o + 16] = c0; a[o + 17] = 0; a[o + 18] = 0; a[o + 19] = PressSlot;
    }

    // ------------------------------------------------------------------ the kit's meshes

    /// <summary>A triangle, wound as Godot wants its front faces (clockwise) for outward normal n.</summary>
    static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 n, Color col)
    {
        if ((b - a).Cross(c - a).Dot(n) > 0) (b, c) = (c, b);
        foreach (var v in new[] { a, b, c })
        {
            st.SetColor(col);
            st.SetNormal(n);
            st.AddVertex(v);
        }
    }

    static void Box(SurfaceTool st, Vector3 a, Vector3 b, int rgb)
    {
        var col = Lin(rgb);
        for (int ax = 0; ax < 3; ax++)
            for (int sg = 0; sg < 2; sg++)
            {
                var n = Vector3.Zero;
                n[ax] = sg == 0 ? -1 : 1;
                int u = (ax + 1) % 3, w = (ax + 2) % 3;
                Vector3 P(float pu, float pw)
                {
                    var p = new Vector3();
                    p[ax] = sg == 0 ? a[ax] : b[ax];
                    p[u] = pu;
                    p[w] = pw;
                    return p;
                }
                Vector3 p0 = P(a[u], a[w]), p1 = P(b[u], a[w]), p2 = P(b[u], b[w]), p3 = P(a[u], b[w]);
                Tri(st, p0, p1, p2, n, col);
                Tri(st, p0, p2, p3, n, col);
            }
    }

    /// <summary>A lens barrel along +z from z0 to z1, radius r0 to r1, its front end capped.</summary>
    static void Tube(SurfaceTool st, float y, float z0, float z1, float r0, float r1, int rgb, bool cap)
    {
        const int Seg = 8;
        var col = Lin(rgb);
        for (int i = 0; i < Seg; i++)
        {
            float a0 = i * TAU / Seg, a1 = (i + 1) * TAU / Seg;
            Vector3 d0 = new(MathF.Cos(a0), MathF.Sin(a0), 0), d1 = new(MathF.Cos(a1), MathF.Sin(a1), 0);
            var c = new Vector3(0, y, 0);
            Vector3 p0 = c + d0 * r0 + new Vector3(0, 0, z0), p1 = c + d1 * r0 + new Vector3(0, 0, z0);
            Vector3 q0 = c + d0 * r1 + new Vector3(0, 0, z1), q1 = c + d1 * r1 + new Vector3(0, 0, z1);
            var n = (d0 + d1).Normalized();
            Tri(st, p0, p1, q1, n, col);
            Tri(st, p0, q1, q0, n, col);
            if (cap) Tri(st, c + new Vector3(0, 0, z1), q1, q0, Vector3.Back, Lin(0x0c0d10));
        }
    }

    /// <summary>The camera, looking down +z from its eyepiece at the origin: the body, the prism,
    /// the flash gun on the hot shoe and a long white telephoto with its black hood.</summary>
    static ArrayMesh CameraMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Box(st, new Vector3(-0.075f, -0.065f, 0), new Vector3(0.075f, 0.06f, 0.09f), 0x1a1a1c);
        Box(st, new Vector3(-0.03f, 0.06f, 0.01f), new Vector3(0.03f, 0.095f, 0.07f), 0x1a1a1c);
        Box(st, new Vector3(-0.02f, 0.095f, 0.02f), new Vector3(0.02f, 0.12f, 0.06f), 0x1a1a1c);
        Box(st, new Vector3(-0.045f, 0.12f, 0), new Vector3(0.045f, 0.175f, 0.075f), 0x202022);
        Box(st, new Vector3(-0.04f, 0.125f, 0.075f), new Vector3(0.04f, 0.17f, 0.082f), 0xd8d8d0);
        Tube(st, -0.005f, 0.09f, 0.25f, 0.05f, 0.06f, 0xe4e1d6, false);
        Tube(st, -0.005f, 0.25f, 0.3f, 0.068f, 0.068f, 0x1a1a1c, false);
        Tube(st, -0.005f, 0.3f, 0.5f, 0.064f, 0.072f, 0xe4e1d6, false);
        Tube(st, -0.005f, 0.5f, 0.62f, 0.08f, 0.086f, 0x16161a, true);
        return st.Commit();
    }

    /// <summary>A unit box (the instance scales and colours it).</summary>
    static ArrayMesh BoxMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Box(st, new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f), 0xffffff);
        return st.Commit();
    }

    /// <summary>A camera-facing card for the flash (flash.gdshader spreads its corners).</summary>
    static ArrayMesh CardMesh()
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[4];
        arrays[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 2, 1, 1, 2, 3 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.CustomAabb = new Aabb(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));
        return mesh;
    }

    // ------------------------------------------------------------------ per frame

    void PressUpdate(MatchSnapshot s, float dt, bool replaying)
    {
        // Into or out of a replay is a cut: everyone back in his place, kneeling or sat,
        // and the score read afresh from the tape (or the live game).
        if (replaying != _pReplaying)
        {
            _pReplaying = replaying;
            _pScore0 = _pScore1 = -1;
            _goalEnd = 0;
            for (int n = 0; n < PressCount; n++)
            {
                var p = _p[n];
                var f = _f[Count + n];
                f.X = p.SpotX = p.HomeX;
                f.Z = p.SpotZ = p.HomeZ;
                f.Speed = 0;
                f.Facing = p.End > 0 ? PI : 0;
                p.Down = 1;
                p.OnCase = p.Sits;
                p.BurstEnd = -1;
            }
        }
        if (s != null) PressEvents(s);
        for (int n = 0; n < PressCount; n++)
        {
            int id = Count + n;
            var f = _f[id];
            var p = _p[n];
            if (s != null) Think(p, s, dt);
            MovePress(f, p, dt);
            ShapePress(p, dt);
            PosePress(id, n, f, p, dt);
        }
        _cam.Buffer = _camBuf;
        _prop.Buffer = _propBuf;
        _flash.Buffer = _flashBuf;
    }

    /// <summary>Shots, saves and goals: who fires, who runs.</summary>
    void PressEvents(MatchSnapshot s)
    {
        if (_pScore0 < 0)
        {
            _pScore0 = s.Score[0];
            _pScore1 = s.Score[1];
        }
        bool goal = s.Score[0] > _pScore0 || s.Score[1] > _pScore1;
        _pScore0 = s.Score[0];
        _pScore1 = s.Score[1];
        if (goal)
        {
            _goalEnd = s.BallX >= 0 ? 1 : -1;
            foreach (var p in _p)
                if (p.End == _goalEnd) Burst(p, R(0, 0.25f), R(0.8f, 1.6f));
        }
        else if (s.Phase != Phase.Goal) _goalEnd = 0;
        // A shot fires the end it's aimed at; a save or the woodwork, the end it happened at.
        int end = s.ShotTeam >= 0 ? (s.Dir[s.ShotTeam] >= 0 ? 1 : -1) : s.Save > 0.5f || s.Post > 0 ? (s.BallX >= 0 ? 1 : -1) : 0;
        if (end != 0)
            foreach (var p in _p)
                if (p.End == end && p.Aim > 0.4f) Burst(p, R(0.02f, 0.3f), R(0.6f, 1.4f));
    }

    void Burst(Snap p, float delay, float dur)
    {
        float at = _t + delay;
        p.NextFrame = MathF.Max(p.BurstEnd > _t ? p.NextFrame : 0, at);
        p.BurstEnd = MathF.Max(p.BurstEnd, at + dur);
    }

    /// <summary>Where he wants to be, what the lens follows, whether it's up, when it fires.</summary>
    void Think(Snap p, MatchSnapshot s, float dt)
    {
        float t = _t;
        bool celebrating = s.Phase == Phase.Goal && _goalEnd != 0;
        bool here = celebrating && _goalEnd == p.End, there = celebrating && !here;

        float gx = s.BallX, gy = MathF.Max(0.3f, s.BallY), gz = s.BallZ;
        if (celebrating && s.Scorer >= 0)
        {
            gx = s.X[s.Scorer];
            gy = 1.1f;
            gz = s.Z[s.Scorer];
        }
        float k = 1 - MathF.Exp(-dt * 5);
        p.TX += (gx - p.TX) * k;
        p.TY += (gy - p.TY) * k;
        p.TZ += (gz - p.TZ) * k;

        if (here)
        {
            if (t >= p.Retarget)
            {
                (p.SpotX, p.SpotZ) = Track(gx, gz, p.End, p.Spread);
                p.Retarget = t + R(1.5f, 3);
            }
        }
        else
        {
            p.SpotX = p.HomeX;
            p.SpotZ = p.HomeZ;
        }

        // Up to the eye as play comes his way (the keen ones first), for a set piece at this end,
        // and through a celebration at either end.
        float near = s.BallX * p.End;
        bool setHere = s.SetPiece is SetPieceKind.Corner or SetPieceKind.Penalty or SetPieceKind.FreeKick && s.SetPieceX * p.End > 18;
        p.AimWant = celebrating || setHere || near > 12 + 14 * p.Lag || near > 4 && s.Excitement > 0.7f ? 1 : 0;

        // Debug: `-- --snap` keeps every camera up and firing.
        if (_snapDebug)
        {
            p.AimWant = 1;
            if (t >= p.BurstEnd) Burst(p, 0, 1);
        }

        // Firing: bursts through the celebration, now and then a frame when the action's close.
        if (celebrating && p.Aim > 0.7f && t >= p.NextShot && t >= p.BurstEnd)
        {
            Burst(p, 0, here ? R(0.6f, 1.8f) : R(0.3f, 0.8f));
            p.NextShot = p.BurstEnd + (here ? R(0.3f, 1.4f) : R(1.5f, 4));
        }
        else if (!celebrating && p.Aim > 0.85f && near > 25 && t >= p.NextShot)
        {
            Burst(p, 0, _rng.Next(3) == 0 ? R(0.2f, 0.5f) : 0.01f);
            p.NextShot = t + R(1.5f, 6);
        }
        if (t < p.BurstEnd && t >= p.NextFrame)
        {
            p.LastFlash = t;
            p.NextFrame = t + 1 / R(6, 10);
        }

        // Between the action: watch, check the screen, a look in the case, a word with the next man.
        if (p.AimWant < 0.5f && t >= p.IdleUntil)
        {
            double r = _rng.NextDouble();
            p.Idle = r < 0.45 ? Idle.Watch : r < 0.75 ? Idle.Screen : r < 0.87 && !p.OnCase ? Idle.Case : Idle.Chat;
            p.IdleUntil = t + p.Idle switch { Idle.Watch => R(4, 12), Idle.Screen => R(2, 5), Idle.Case => R(2.5f, 4), _ => R(3, 7) };
        }
    }

    /// <summary>The nearest place to (x, z) on the photographers' run: along the end line behind
    /// the goal, and round the corner a little way up the touchline; spread out along it.</summary>
    static (float, float) Track(float x, float z, int end, float spread)
    {
        float ex = end * LineX, sz = z >= 0 ? 1 : -1;
        float cz = Clamp(z, -TrackZ, TrackZ);
        float tx = end * Clamp(x * end, HL - 14, LineX);
        float dEnd = (ex - x) * (ex - x) + (cz - z) * (cz - z);
        float dSide = (tx - x) * (tx - x) + (sz * TrackZ - z) * (sz * TrackZ - z);
        if (dEnd <= dSide) return (ex, Clamp(cz + spread, -TrackZ, TrackZ));
        return (end * Clamp(tx * end - end * spread * sz, HL - 14, LineX), sz * TrackZ);
    }

    void MovePress(Fig f, Snap p, float dt)
    {
        float dx = p.SpotX - f.X, dz = p.SpotZ - f.Z, d = MathF.Sqrt(dx * dx + dz * dz);
        bool home = p.SpotX == p.HomeX && p.SpotZ == p.HomeZ;
        bool celebrating = _goalEnd != 0;
        float face;
        if (d > 0.15f && p.Down > 0)
        {
            // Up off the knee (or the case) before he goes anywhere.
            p.Down = MathF.Max(0, p.Down - dt / (celebrating ? 0.35f : 0.7f));
            f.Speed = 0;
            face = f.Facing;
        }
        else if (d > 0.15f)
        {
            p.OnCase = false;
            float pace = celebrating ? 4.6f : 1.4f;
            f.Speed += Clamp(MathF.Min(pace, d * 2 + 0.3f) - f.Speed, -6 * dt, 5 * dt);
            float step = MathF.Min(d, f.Speed * dt);
            f.X += dx / d * step;
            f.Z += dz / d * step;
            face = MathF.Atan2(dz, dx);
        }
        else
        {
            f.Speed = MathF.Max(0, f.Speed - 6 * dt);
            // At his place: down on the knee or the case; the far end stands to shoot a celebration.
            if (p.Down == 0) p.OnCase = p.Sits && home;
            bool stand = celebrating && _goalEnd != p.End;
            p.Down = Clamp(p.Down + (stand ? -dt / 0.5f : dt / 0.8f), 0, 1);
            face = MathF.Atan2(p.TZ - f.Z, p.TX - f.X);
            if (home)
            {
                float pitch = p.End > 0 ? PI : 0;
                face = pitch + Clamp(Wrap(face - pitch), -1.25f, 1.25f);
            }
        }
        float rate = f.Speed > 0.5f ? 8 : p.Down > 0.5f ? 1.8f : 4;
        f.Facing = dt <= 0 ? face : Wrap(f.Facing + Wrap(face - f.Facing) * (1 - MathF.Exp(-dt * rate)));
        f.Phi += f.Speed / (1.1f + 0.35f * f.Speed) * TAU * 0.5f * dt;
    }

    void ShapePress(Snap p, float dt)
    {
        float a = dt <= 0 ? 1 : 1 - MathF.Exp(-dt * 5);
        float want = p.AimWant;
        p.Aim += (want - p.Aim) * (dt <= 0 ? 1 : 1 - MathF.Exp(-dt * (want > p.Aim ? 6 : 2.5f)));
        var idle = p.Aim > 0.5f ? Idle.Watch : p.Idle;
        float[] low = idle switch { Idle.Screen => Review, Idle.Case => Rummage, _ => Hold };
        float flex = p.OnCase ? 0.22f : 0.08f, head = 0, side = 0;
        if (idle == Idle.Screen) { flex += 0.15f; head = 0.55f; }
        else if (idle == Idle.Case) { flex += 0.45f; head = 0.4f; side = 0.3f; }
        flex = Lerp(flex, 0.12f, p.Aim);
        for (int i = 0; i < 8; i++) p.Arms[i] += (Lerp(low[i], Shoot[i], p.Aim) - p.Arms[i]) * a;
        p.Flex += (flex - p.Flex) * a;
        p.Head += (head * (1 - p.Aim) - p.Head) * a;
        p.Side += (side * (1 - p.Aim) - p.Side) * a;
    }

    /// <summary>The skeleton (as the bench's Pose): walking, kneeling on one knee or sat on the
    /// case; trunk, head and arms working the camera; then the camera, monopod and flash.</summary>
    void PosePress(int id, int n, Fig f, Snap p, float dt)
    {
        var b = f.B;
        float sc = f.Scale, leg = f.Leg, down = Ease(p.Down);
        float seat = p.OnCase ? down : 0, kneel = down - seat, walk = 1 - down;
        float move = Smooth(0.05f, 0.6f, f.Speed), jog = Smooth(1.6f, 3.2f, f.Speed);
        float phi = f.Phi;

        // Hips: standing, on the knee, or on the case (whose middle is behind his standing spot).
        float standHipY = f.HipBase - (0.012f + 0.05f * jog) * MathF.Abs(MathF.Cos(phi)) * move;
        const float KneelTh = 0.12f;
        float kneelHipY = THIGH * leg * MathF.Cos(KneelTh) + 0.07f;
        float hj = (CaseTop + 0.075f) / sc;
        float hipY = standHipY * walk + kneelHipY * kneel + (hj + 0.03f) * seat;
        float rx = Lerp(f.X, p.CaseX, seat), rz = Lerp(f.Z, p.CaseZ, seat);

        // Aim: the shoulders turn toward what he's shooting, the head the rest of the way.
        float rel = Wrap(MathF.Atan2(p.TZ - rz, p.TX - rx) - f.Facing);
        float tw = -Clamp(rel * 0.6f, -0.6f, 0.6f) * (1 - move);
        float headTo = p.Idle == Idle.Chat && p.Aim < 0.5f && p.Mate >= 0 && p.Mate < PressCount
            ? Wrap(MathF.Atan2(_f[Count + p.Mate].Z - rz, _f[Count + p.Mate].X - rx) - f.Facing) : rel;
        float wantY = Clamp(-headTo, -1.3f, 1.3f) - tw;
        f.HeadYaw = dt <= 0 ? wantY : f.HeadYaw + (wantY - f.HeadYaw) * (1 - MathF.Exp(-dt * 5));

        var root = new Transform3D(new Basis(Vector3.Up, PI / 2 - f.Facing).Scaled(new Vector3(sc, sc, sc)), new Vector3(rx, 0, rz));
        root = Chain(root, 0, 0, 0, 0.08f * jog, 0, 0);
        float pelvisYaw = -0.08f * MathF.Sin(phi) * move;
        var P = Chain(root, 0, hipY, 0, 0, pelvisYaw, 0);
        float torsoW = (float)b.TorsoW, torsoD = (float)b.TorsoD, torsoL = (float)b.TorsoL;
        Put(Part.Pelvis, id, P, torsoW, 1, torsoD);
        var T = ChainT(P, 0, 0.04f, 0);
        float flex = p.Flex + 0.04f, twist = tw - pelvisYaw, side = p.Side;
        Put(Part.Torso, id, T, torsoW, torsoL, torsoD, flex, twist, side);
        var C = Chain(T, 0, 0, 0, flex, twist, side);
        // Head pitch: down at the screen or the case, else level with the shot.
        float eyeY = hipY * sc + 0.75f;
        float dist = MathF.Max(1, MathF.Sqrt((p.TX - rx) * (p.TX - rx) + (p.TZ - rz) * (p.TZ - rz)));
        float hP = p.Head + MathF.Atan2(eyeY - p.TY, dist) * p.Aim - flex * 0.75f;
        float neckLen = (float)b.NeckLen, neckW = (float)b.Neck;
        var Nk = Chain(C, 0, 0.58f * torsoL, 0, hP * 0.4f, f.HeadYaw * 0.3f, -side * 0.4f);
        Put(Part.Neck, id, Nk, neckW, neckLen, neckW);
        float top = 0.075f * neckLen;
        var Hd = Chain(Nk, 0, top, 0, hP * 0.6f, f.HeadYaw * 0.7f, -side * 0.4f);
        Hd = ChainT(Hd, 0, 0.02f * torsoL + (neckLen - 1) * 0.08f - top, 0);
        Put(Part.Head, id, Hd);
        if (f.Hair == 1) Put(Part.HairShort, id, Hd, 0.985f, 0.95f, 0.985f);
        else if (f.Hair >= 0) Put(HairOfStyle[f.Hair], id, Hd);

        // Arms: the right always on the camera; the left swings free when he runs.
        float armLen = (float)b.ArmLen, armW = (float)b.Arm, shoulder = (float)b.Shoulder;
        for (int sd = 0; sd < 2; sd++)
        {
            float sideSign = sd == 0 ? 1 : -1;
            float aw = sd == 0 ? 1 - move : 1;
            float swing0 = (sd == 0 ? 1 : -1) * MathF.Sin(phi) * (0.3f + 0.4f * jog) * move + 0.1f * jog;
            float swing = Lerp(swing0, p.Arms[sd], aw);
            float elbow = Lerp(0.25f + 1.0f * jog, p.Arms[2 + sd], aw);
            float outA = Lerp(0.1f, p.Arms[4 + sd], aw);
            float rot = Lerp(0, p.Arms[6 + sd], aw);
            float raise = MathF.Acos(Clamp(MathF.Cos(swing) * MathF.Cos(outA), -1, 1));
            float elev = Smooth(1.1f, 2.9f, raise);
            var j1 = Chain(C, sideSign * (0.198f * shoulder - 0.014f * elev), 0.5f * torsoL + 0.045f * elev, 0, -swing, 0, sideSign * outA);
            Put(Part.UpperArm, id * 2 + sd, j1, armW, armLen, armW);
            var j2 = Chain(j1, 0, -0.29f * armLen, 0, -elbow, sideSign * rot, 0);
            Put(Part.Forearm, id * 2 + sd, j2, 0.5f + 0.5f * armW, armLen, 0.5f + 0.5f * armW);
            var j3 = Chain(j2, 0, -0.245f * armLen, 0, 0.1f, 0, sideSign * -0.08f);
            Put(Part.Hand, id * 2 + sd, j3);
        }

        // Legs: walking, one knee down and the other foot planted, or sat with the feet out.
        float thighW = (float)b.Thigh, calfW = (float)b.Calf;
        for (int sd = 0; sd < 2; sd++)
        {
            float sideSign = sd == 0 ? 1 : -1;
            int fi = id * 2 + sd;
            float hipX = sideSign * 0.092f * (1 + (torsoW - 1) * 0.6f);
            float ph = phi + (sd == 0 ? 0 : PI);
            float wHip = MathF.Sin(ph) * (0.32f + 0.3f * jog) * move;
            float cs = MathF.Max(0, MathF.Cos(ph));
            float wKnee = move * (0.06f + (0.75f + 0.7f * jog) * cs * cs) + 0.04f;
            // Kneeling: the down knee on the grass with the shin flat behind; the other foot flat.
            float kHip, kKnee;
            if (sd == p.Knee)
            {
                kHip = KneelTh;
                kKnee = KneelTh + 1.5f;
            }
            else
            {
                kHip = 1.45f;
                float ky = kneelHipY - THIGH * leg * MathF.Cos(kHip);
                kKnee = kHip - MathF.Acos(Clamp((ky - 0.075f) / (SHIN * leg), -1, 1));
            }
            // Sat on the case: thighs level, shins down to the grass a little forward.
            const float SeatTh = 1.35f;
            float sy = hj - THIGH * leg * MathF.Cos(SeatTh);
            float sKnee = SeatTh - MathF.Acos(Clamp((sy - 0.075f) / (SHIN * leg), -1, 1)) * 0.5f;
            float hip = wHip * walk + kHip * kneel + SeatTh * seat;
            float knee = wKnee * walk + MathF.Max(0.05f, kKnee) * kneel + MathF.Max(0.05f, sKnee) * seat;
            float outA = 0.12f * kneel + 0.2f * seat;
            var j1 = Chain(P, hipX, -0.03f, 0, -hip, 0, sideSign * outA);
            float soft = knee * 0.22f;
            var j2 = ChainX(j1, 0, 0, 0, soft);
            j2 = ChainX(j2, 0, -THIGH * leg, 0, knee - soft);
            Put(Part.ShortsLeg, fi, j1, thighW, 1, thighW);
            Put(Part.Thigh, fi, j1, thighW, leg, thighW, knee * 0.22f);
            Put(Part.Shin, fi, j2, calfW, leg, calfW);
            float ankle = hip - knee - 0.08f * jog;
            var j3 = ChainX(j2, 0, -SHIN * leg, 0, ankle);
            Put(Part.Boot, fi, j3);
        }

        // The camera: at his eye looking where he shoots, or held at the chest (tipped to show
        // him the back screen when he's checking his pictures).
        var eye = Hd * new Vector3(0, 0.1f, 0.12f);
        var aimDir = new Vector3(p.TX, p.TY, p.TZ) - eye;
        aimDir = aimDir.LengthSquared() > 1e-4f ? aimDir.Normalized() : C.Basis.Z.Normalized();
        var fwd = C.Basis.Z;
        fwd = new Vector3(fwd.X, 0, fwd.Z).Normalized();
        float tip = p.Idle == Idle.Screen ? 1.1f : 0.45f;
        var lowDir = fwd * MathF.Cos(tip) + Vector3.Down * MathF.Sin(tip);
        var lowPos = C * new Vector3(0, 0.36f * torsoL, 0.17f);
        var dir = lowDir.Lerp(aimDir, p.Aim).Normalized();
        var camPos = lowPos.Lerp(eye + aimDir * 0.02f, p.Aim);
        var cam = new Transform3D(Basis.LookingAt(dir, Vector3.Up, true), camPos);
        PutGear(_camBuf, n, cam, Colors.White);

        // The monopod, from under the lens to the grass (swinging short while he runs).
        var top3 = cam * new Vector3(0, -0.07f, 0.3f);
        var foot = f.Speed > 0.6f ? top3 + new Vector3(0, -0.6f, 0) : new Vector3(top3.X, 0, top3.Z) + fwd * 0.08f;
        var up = top3 - foot;
        var ax = up.Cross(Vector3.Back);
        if (ax.LengthSquared() < 1e-6f) ax = Vector3.Right;
        ax = ax.Normalized() * 0.028f;
        var az = ax.Cross(up).Normalized() * 0.028f;
        PutGear(_propBuf, n * 2, new Transform3D(new Basis(ax, up, az), (top3 + foot) / 2), Lin(0x2a2a2c));

        // The flash: on for a frame or two each time he fires.
        float since = _t - p.LastFlash;
        float on = since < 0.045f ? 1 : since < 0.08f ? 0.55f : 0;
        PutGear(_flashBuf, n, new Transform3D(Basis.Identity, cam * new Vector3(0, 0.15f, 0.09f)), Colors.White, on);
    }
}
