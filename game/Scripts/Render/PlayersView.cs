using System;
using System.Collections.Generic;
using Godot;
using GameNight.Sim;
using Part = GameNight.Render.BodyMeshes.Part;

namespace GameNight.Render;

/// <summary>
/// The footballers, ported from the PWA's players.ts: shaped, kitted figures (collars, trim,
/// numbers, faces, hair) built from a few smooth parts and posed procedurally from the match
/// every frame, so the motion always matches the physics:
/// - a run cycle with planted feet (two-bone IK: the feet don't skate), hips that drop and
///   turn, shoulders that counter-rotate, a springy spine and a head that tracks the ball;
/// - arms with each player's own carriage, effort and fatigue, and a slow random drift in the
///   upper body so no two moves are quite the same;
/// - strikes timed to the contact, stretches, block and slide tackles, headers, throws,
///   keeper stances, dives and catches, falls, and the goal celebrations;
/// - secondary motion: arms, elbows, head and shoulders carry inertia and overshoot.
/// Every part type is one MultiMesh, so all 22 players are 14 draws.
/// </summary>
public sealed partial class PlayersView
{
    const int N = MatchSnapshot.N;
    const float THIGH = 0.43f, SHIN = 0.42f, HIP_Y = 0.94f, HEAD_TOP = 0.24f;
    const float BASE_HEIGHT = HIP_Y + 0.04f + 0.6f + HEAD_TOP;
    const float PI = MathF.PI, TAU = MathF.Tau;

    // Secondary-motion channels: spring frequency (rad/s) and damping ratio.
    const int ArmL = 0, ArmR = 1, OutL = 2, OutR = 3, ElbowL = 4, ElbowR = 5, HeadPitch = 6, HeadRoll = 7, Twist = 8, SecCount = 9;
    static readonly float[] SecW = { 10, 10, 9, 9, 13, 13, 14, 14, 11 };
    static readonly float[] SecZ = { 0.45f, 0.45f, 0.4f, 0.4f, 0.42f, 0.42f, 0.5f, 0.5f, 0.5f };
    /// <summary>Channels that soak up a jump in the pose (the arms) rather than following it at once.</summary>
    const int SecSoak = 6;

    static readonly int[] PerPlayer = { 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2 };
    /// <summary>Shader part kinds (body.gdshaderinc) per mesh part.</summary>
    static readonly int[] ShaderPart = { 1, 0, 0, 2, 0, 0, 0, 3, 4, 0, 5, 6, 7, 8 };
    /// <summary>Too small to show in the sun's shadow, or inside another part's.</summary>
    static readonly bool[] NoShadow = { false, false, true, false, true, true, true, false, true, true, true, false, false, true };
    static readonly Part[] HairOfStyle = { Part.HairShort, Part.HairShort, Part.HairCurly, Part.HairQuiff };

    /// <summary>Boots: a random pick per player each match (boot, sole).</summary>
    static readonly (int, int)[] Boots =
    {
        (0x1b1b1d, 0xe8e6df), (0xf0efe9, 0x1b1b1d), (0xe9f23a, 0x1b1b1d), (0xff6a2b, 0xf0efe9),
        (0xff4f9a, 0x1b1b1d), (0x2fd3e8, 0xf0efe9), (0xd8262f, 0x1b1b1d), (0x2457d6, 0xf0efe9),
        (0x29b36a, 0x1b1b1d), (0xd9b04a, 0x1b1b1d), (0xb9bdc4, 0x2a2a2a), (0x6a3fd1, 0xe9f23a),
    };

    const int Stride = 20; // floats per instance: 3x4 transform, colour, custom
    readonly MultiMesh[] _mm = new MultiMesh[BodyMeshes.PartCount];
    readonly float[][] _buf = new float[BodyMeshes.PartCount][];
    readonly ShaderMaterial[] _mat = new ShaderMaterial[BodyMeshes.PartCount];
    readonly MeshInstance3D _ball, _ring, _marker, _ring2, _marker2;

    // Per player, fixed for the match.
    readonly BodyShape[] _body = new BodyShape[N];
    readonly float[] _hipBase = new float[N], _bodyScale = new float[N];
    readonly int[] _hair = new int[N];
    /// <summary>Each head's size and proportions (Body.HeadOf).</summary>
    readonly Vector3[] _headScale = new Vector3[N];

    // Per player, frame to frame.
    readonly float[] _sF = new float[N], _spF = new float[N], _sS = new float[N], _spS = new float[N], _headYaw = new float[N];
    readonly float[] _sec = new float[N * SecCount], _secV = new float[N * SecCount], _secPose = new float[N * SecCount];
    readonly bool[] _secReady = new bool[N];
    readonly float[] _bodyY = new float[N], _bodyVy = new float[N], _bodyAy = new float[N], _lastFacing = new float[N];
    readonly float[] _plantX = new float[N * 2], _plantZ = new float[N * 2], _footW = new float[N * 2];
    readonly byte[] _inStance = new byte[N * 2];
    readonly float[] _ikOn = new float[N], _turnS = new float[N];
    readonly byte[] _steerSt = new byte[N], _steerLeg = new byte[N];
    readonly float[] _steerE = new float[N], _gkReady = new float[N];
    readonly float[] _arm = new float[10];
    readonly float[] _poseNow = new float[SecCount], _want = new float[SecCount];
    double _lastTime = -1;

    // Leg IK results.
    float _ikH, _ikK, _ikOut;
    Transform3D _pinv;

    readonly List<MultiMeshInstance3D> _instances = new();
    readonly Vector4[][] _ka = new Vector4[BodyMeshes.PartCount][], _kb = new Vector4[BodyMeshes.PartCount][];

    /// <summary>Only the first this many bodies are drawn (the officials need three of the 22).</summary>
    public int Bodies
    {
        set
        {
            for (int k = 0; k < BodyMeshes.PartCount; k++) _mm[k].VisibleInstanceCount = Math.Min(N, value) * PerPlayer[k];
        }
    }

    /// <summary>The bodies only (no ball, no markers): for the officials, or anyone else drawn as a player.</summary>
    public PlayersView(Node3D root, bool matchProps = true)
    {
        // A figure is a few dozen art pixels tall: 60% of the facets reads the same and saves
        // about 40% of the triangles (players were most of the frame's, shadows included).
        var meshes = BodyMeshes.Build(0.6f);
        var shader = GD.Load<Shader>("res://Shaders/body.gdshader");
        var shaderDouble = GD.Load<Shader>("res://Shaders/body_double.gdshader");
        for (int k = 0; k < BodyMeshes.PartCount; k++)
        {
            var mat = new ShaderMaterial { Shader = k == (int)Part.ShortsLeg ? shaderDouble : shader };
            mat.SetShaderParameter("part", ShaderPart[k]);
            _mat[k] = mat;
            int count = N * PerPlayer[k];
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                UseCustomData = true,
                Mesh = meshes[k],
            };
            mm.InstanceCount = count;
            _mm[k] = mm;
            _buf[k] = new float[count * Stride];
            _ka[k] = new Vector4[N];
            _kb[k] = new Vector4[N];
            var inst = new MultiMeshInstance3D
            {
                Multimesh = mm,
                MaterialOverride = mat,
                CastShadow = NoShadow[k] ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
                // The squad spans the pitch; never cull it.
                ExtraCullMargin = 200,
            };
            root.AddChild(inst);
            _instances.Add(inst);
        }
        if (!matchProps) return;

        var ballMesh = new SphereMesh { Radius = 0.11f, Height = 0.22f, RadialSegments = 8, Rings = 4 };
        _ball = Geo.Instance(root, ballMesh, new StandardMaterial3D { AlbedoColor = new Color(0.97f, 0.97f, 0.95f), Roughness = 0.5f }, shadows: true);
        // The marker under your player, and the little arrow over his head.
        _ring = Geo.Instance(root, new QuadMesh { Size = new Vector2(1.28f, 1.28f), Orientation = PlaneMesh.OrientationEnum.Y }, Geo.Material("res://Shaders/ring.gdshader"));
        var gold = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.83f, 0.28f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _marker = Geo.Instance(root, new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0, Height = 0.28f, RadialSegments = 3, Rings = 1 }, gold);
        // A 1v1: the other human's, in cyan.
        var ring2 = Geo.Material("res://Shaders/ring.gdshader");
        ring2.SetShaderParameter("color", new Vector3(0.31f, 0.85f, 1f));
        _ring2 = Geo.Instance(root, _ring.Mesh, ring2);
        _marker2 = Geo.Instance(root, _marker.Mesh, new StandardMaterial3D { AlbedoColor = new Color(0.31f, 0.85f, 1f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        _ring2.Visible = _marker2.Visible = false;
    }

    static Color Lin(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f).SrgbToLinear();
    static Vector4 Lin4(int rgb, float w) { var c = Lin(rgb); return new Vector4(c.R, c.G, c.B, w); }

    /// <summary>The controlled player's ring and marker (off during replays and cutscenes).</summary>
    public bool Markers = true;

    /// <summary>Every body jumped (a replay rewound or ended): settle feet and secondary motion afresh.</summary>
    public void Snap()
    {
        Array.Clear(_secReady);
        Array.Clear(_footW);
        Array.Clear(_inStance);
        Array.Clear(_ikOn);
    }

    // ---- the officials (set only on their view): the referee's pointing arm, the linesmen's flags.
    public float RefPoint, RefPointSide = 1;
    public int RefSlot = -1;
    public int[] LineSlots = Array.Empty<int>();
    public readonly float[] FlagUp = new float[2];
    MeshInstance3D[] _flags;

    /// <summary>A flag in each linesman's right hand: a short stick with a checked cloth.</summary>
    public void AddFlags(Node3D root)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetColor(new Color(0, 0, 0));
        Geo.Box(st, new Vector3(-0.01f, -0.475f, -0.01f), new Vector3(0.01f, 0.075f, 0.01f));
        st.SetColor(new Color(1, 1, 1));
        Geo.Quad(st, new Vector3(0.15f, -0.46f, 0.15f), new Vector3(0.15f, -0.46f, -0.15f), new Vector3(0.15f, -0.24f, -0.15f), new Vector3(0.15f, -0.24f, 0.15f), 1, 1);
        var mesh = st.Commit();
        var mat = Geo.Material("res://Shaders/flag.gdshader");
        _flags = new MeshInstance3D[2];
        for (int i = 0; i < 2; i++)
        {
            _flags[i] = Geo.Instance(root, mesh, mat);
            _flags[i].CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }

    /// <summary>Shown or hidden as a whole.</summary>
    public bool Visible
    {
        set
        {
            foreach (var i in _instances) i.Visible = value;
            if (_flags != null)
                foreach (var f in _flags) f.Visible = value;
        }
    }

    /// <summary>A new match: each player's body, kit, skin, hair and boots.</summary>
    public void SetMatch(Match m)
    {
        Snap();
        _lastTime = -1;
        foreach (var p in m.All) Dress(m, p);
        Flush();
    }

    /// <summary>One player's body and shirt as the match has him now (a substitute coming on). Call Flush when done.</summary>
    public void Dress(Match m, Player p)
    {
        if (p.Id < 0 || p.Id >= N) return;
        var team = m.Teams[p.Team];
        bool captain = team.Captain == p.Index && team.Players.Count > p.Index && team.Players[p.Index] == p;
        int number = p.Number > 0 ? p.Number : p.Role == Role.GK ? 1 : p.Index + 1;
        SetBody(p.Id, p, team.Info.Kit, number, captain);
    }

    readonly Random _rng = new();

    /// <summary>One body in slot id: his build, the kit (a keeper wears its keeper colours), number
    /// (negative: none) and armband. Call Flush when done.</summary>
    public void SetBody(int id, Player p, Kit kit, int number, bool captain)
    {
        var ka = _ka;
        var kb = _kb;
        {
            var b = Body.Shape(p.Attrs.Height, p.Attrs.Weight, p.Attrs.Strength, id * 7 + p.Index + (p.Name.Length > 0 ? p.Name.Length * 13 : 0));
            _body[id] = b;
            float hip = (THIGH + SHIN) * (float)b.Leg + (HIP_Y - THIGH - SHIN);
            _hipBase[id] = hip;
            _bodyScale[id] = BASE_HEIGHT / (hip + 0.04f + 0.6f * (float)b.TorsoL + ((float)b.NeckLen - 1) * 0.08f + HEAD_TOP);
            _hair[id] = Math.Clamp(p.Look.HairStyle, 0, 3);
            var hd = Body.HeadOf(p.Name, id * 31 + p.Index);
            // (A touch bigger than the PWA's head, in proportion to the shoulders.)
            float hs = 1.05f * (float)hd.Size;
            _headScale[id] = new Vector3(hs * (float)hd.Width, hs * (float)hd.Height, hs * (float)hd.Depth);

            bool gk = p.Role == Role.GK;
            int shirt = gk ? kit.GkShirt : kit.Shirt;
            int trim = gk ? kit.GkShorts : kit.Shirt2;
            int shorts = gk ? kit.GkShorts : kit.Shorts;
            int skin = p.Look.Skin, hair = p.Look.Hair;
            void Set(Part part, int rgb)
            {
                int k = (int)part, per = PerPlayer[k];
                var c = Lin(rgb);
                for (int s = 0; s < per; s++)
                {
                    int o = (id * per + s) * Stride + 12;
                    _buf[k][o] = c.R;
                    _buf[k][o + 1] = c.G;
                    _buf[k][o + 2] = c.B;
                    _buf[k][o + 3] = 1;
                }
            }
            Set(Part.Torso, shirt);
            ka[(int)Part.Torso][id] = Lin4(trim, number);
            // Numbers in the trim colour unless that's too close to the shirt.
            kb[(int)Part.Torso][id] = Lin4(gk ? 0x1d1d1d : kit.Shirt2 == kit.Shirt ? 0xffffff : kit.Shirt2, gk ? 0 : kit.Pattern);
            Set(Part.UpperArm, shirt);
            ka[(int)Part.UpperArm][id] = Lin4(trim, captain ? 1 : 0);
            kb[(int)Part.UpperArm][id] = Lin4(gk ? shirt : skin, 0);
            Set(Part.Forearm, gk ? shirt : skin);
            ka[(int)Part.Forearm][id] = Lin4(gk ? 0xf2f0ea : skin, 0);
            Set(Part.Hand, gk ? 0xf2f0ea : skin);
            Set(Part.Pelvis, shorts);
            Set(Part.ShortsLeg, shorts);
            ka[(int)Part.ShortsLeg][id] = Lin4(gk ? shirt : kit.Shirt2 == kit.Shorts ? kit.Shirt : kit.Shirt2, 0);
            Set(Part.Shin, gk ? kit.GkShorts : kit.Socks);
            ka[(int)Part.Shin][id] = Lin4(gk ? kit.GkShirt : kit.Shirt2, 0);
            Set(Part.Neck, skin);
            Set(Part.Head, skin);
            // The head's face: brows and facial hair in his hair colour.
            ka[(int)Part.Head][id] = Lin4(hair, Body.FacialHair(p.Name, id * 31 + p.Index));
            // ... and the build of the face: jaw, chin, cheekbones, nose, brow (-1..1, shader).
            kb[(int)Part.Head][id] = new Vector4((float)hd.Jaw, (float)hd.Chin, (float)hd.Cheek, (float)(hd.Nose + 3 * Math.Round(hd.Brow * 4)));
            Set(Part.Thigh, skin);
            Set(Part.HairShort, hair);
            Set(Part.HairCurly, hair);
            Set(Part.HairQuiff, hair);
            var (boot, sole) = Boots[_rng.Next(Boots.Length)];
            Set(Part.Boot, boot);
            ka[(int)Part.Boot][id] = Lin4(sole, 0);
        }
    }

    /// <summary>Hand the kits set by SetBody to the shaders.</summary>
    public void Flush()
    {
        for (int k = 0; k < BodyMeshes.PartCount; k++)
        {
            _mat[k].SetShaderParameter("ka", _ka[k]);
            _mat[k].SetShaderParameter("kb", _kb[k]);
        }
    }

    // ------------------------------------------------------------------ helpers

    static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static float Smooth(float e0, float e1, float x)
    {
        float t = Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }
    static float Hash01(float id, float k)
    {
        float v = MathF.Sin(id * 12.9898f + k * 78.233f) * 43758.5453f;
        return v - MathF.Floor(v);
    }
    static float Rnd(float id, float k) => Hash01(id, k) * 2 - 1;
    /// <summary>Walking (1) or running (0): people change gait at about 2.2 m/s.</summary>
    static float WalkGait(float speed) => 1 - Smooth(1.8f, 2.7f, speed);
    /// <summary>Share of a stride each foot is on the ground: about 0.6 walking (both feet down
    /// between steps), 0.4 at a jog, a quarter at a sprint.</summary>
    static float Duty(float speed) => Lerp(Lerp(0.42f, 0.25f, Smooth(2.5f, 8.5f, speed)), 0.62f, WalkGait(speed));
    /// <summary>A slow, smooth wander in [-1, 1] for player id (channel k, about f rad/s).</summary>
    static float Drift(float t, int id, int k, float f) =>
        MathF.Sin(t * f + Hash01(id, k) * 6.283f) * 0.65f + MathF.Sin(t * f * 2.3f + Hash01(id, k + 1) * 6.283f) * 0.35f;

    /// <summary>local = T(x,y,z) * Ry * Rx * Rz; parent * local.</summary>
    static Transform3D Chain(in Transform3D parent, float x, float y, float z, float rx, float ry, float rz) =>
        parent * new Transform3D(Basis.FromEuler(new Vector3(rx, ry, rz), EulerOrder.Yxz), new Vector3(x, y, z));
    static Transform3D ChainT(in Transform3D parent, float x, float y, float z) =>
        new(parent.Basis, parent * new Vector3(x, y, z));
    /// <summary>A hinge: parent * T(x,y,z) * Rx.</summary>
    static Transform3D ChainX(in Transform3D parent, float x, float y, float z, float rx) =>
        parent * new Transform3D(new Basis(Vector3.Right, rx), new Vector3(x, y, z));

    /// <summary>Instance `index` of a part = m * scale(sx, sy, sz), with its custom data.</summary>
    void Put(Part part, int index, in Transform3D m, float sx = 1, float sy = 1, float sz = 1, float c0 = 0, float c1 = 0, float c2 = 0)
    {
        var a = _buf[(int)part];
        int o = index * Stride;
        Vector3 x = m.Basis.Column0 * sx, y = m.Basis.Column1 * sy, z = m.Basis.Column2 * sz;
        a[o] = x.X; a[o + 1] = y.X; a[o + 2] = z.X; a[o + 3] = m.Origin.X;
        a[o + 4] = x.Y; a[o + 5] = y.Y; a[o + 6] = z.Y; a[o + 7] = m.Origin.Y;
        a[o + 8] = x.Z; a[o + 9] = y.Z; a[o + 10] = z.Z; a[o + 11] = m.Origin.Z;
        a[o + 16] = c0; a[o + 17] = c1; a[o + 18] = c2;
        a[o + 19] = PerPlayer[(int)part] == 2 ? index / 2 : index;
    }

    void Hide(Part part, int index)
    {
        var a = _buf[(int)part];
        Array.Clear(a, index * Stride, 12);
    }

    void LegChain(in Transform3D P, float hipX, float hip, float yaw, float outA, float knee, float leg, out Transform3D j1, out Transform3D j2)
    {
        j1 = Chain(P, hipX, -0.03f, 0, -hip, yaw, outA);
        float soft = knee * 0.22f;
        j2 = ChainX(j1, 0, 0, 0, soft);
        j2 = ChainX(j2, 0, -THIGH * leg, 0, knee - soft);
    }

    /// <summary>
    /// Two-bone leg IK: hip swing, knee and hip roll that put the ankle on world point `t`, for
    /// the leg at hipX on the pelvis (_pinv is its inverse). False when out of reach, unless
    /// `stretch`: then the leg straightens out toward a point beyond it.
    /// </summary>
    bool LegIK(Vector3 t, float hipX, float yaw, float sideSign, float l1, float l2, bool stretch = false)
    {
        var tv = _pinv * t;
        float dx0 = tv.X - hipX, dz0 = tv.Z;
        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw);
        float dx = dx0 * cy - dz0 * sy;
        float dy = tv.Y + 0.03f;
        float dz = dx0 * sy + dz0 * cy;
        float D = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (stretch && D > (l1 + l2) * 0.97f)
        {
            float f = (l1 + l2) * 0.97f / D;
            dx *= f; dy *= f; dz *= f; D *= f;
        }
        if (D > (l1 + l2) * 1.12f || D < 0.25f) return false;
        float kr = MathF.Acos(Clamp((D * D - l1 * l1 - l2 * l2) / (2 * l1 * l2), -1, 1));
        float k = MathF.Min(2.4f, kr / 0.78f);
        float sk = 0.22f * k;
        float ay = -l1 - l2 * MathF.Cos(k - sk);
        float az = -l2 * MathF.Sin(k - sk);
        float vy = ay * MathF.Cos(sk) - az * MathF.Sin(sk);
        float vz = ay * MathF.Sin(sk) + az * MathF.Cos(sk);
        float th = MathF.Asin(Clamp(dx / MathF.Max(0.05f, -vy), -0.6f, 0.6f));
        float h = MathF.Atan2(vz, vy * MathF.Cos(th)) - MathF.Atan2(dz, dy);
        h -= TAU * MathF.Floor((h + PI) / TAU);
        _ikH = h;
        _ikK = k;
        _ikOut = th * sideSign;
        return true;
    }

    /// <summary>Running arms into _arm: swing L/R, elbow L/R, out L/R, rot L/R, wrist L/R.</summary>
    void RunArms(int id, float accelFwd, float stamina, float phi, float s, float moveAmt, float time)
    {
        var o = _arm;
        // His carriage (fixed per player) ...
        float amp = 0.85f + 0.3f * Hash01(id, 1);
        float asym = (Hash01(id, 2) - 0.5f) * 0.24f;
        float elbowK = 0.8f + 0.4f * Hash01(id, 3);
        float elAsym = (Hash01(id, 4) - 0.5f) * 0.3f;
        float width = (Hash01(id, 5) - 0.5f) * 0.08f;
        float cross = 0.6f + 0.8f * Hash01(id, 6);
        // ... a slow drift from stride to stride ...
        float n1 = MathF.Sin(time * 1.3f + Hash01(id, 7) * 6.28f) * 0.6f + MathF.Sin(time * 0.47f + Hash01(id, 8) * 6.28f) * 0.4f;
        float n2 = MathF.Sin(time * 0.9f + Hash01(id, 9) * 6.28f);
        // ... effort (driving on) and fatigue.
        float effort = Clamp(accelFwd * 0.04f, -0.15f, 0.25f) * moveAmt;
        float tired = (1 - Smooth(0.15f, 0.45f, stamina)) * s;
        float run = s * moveAmt;
        float reachF = 1 + 0.45f * s, reachB = 1 - 0.3f * s;
        float bias = 0.15f * run;
        float bounce = MathF.Cos(2 * phi - 0.9f);
        for (int a = 0; a < 2; a++)
        {
            float side = a == 0 ? 1 : -1;
            float th = phi - 0.18f + (a == 0 ? PI : 0);
            // + = forward. Skewed: the drive forward is quicker than the float back.
            float u = MathF.Sin(th + 0.25f * MathF.Cos(th));
            float sw = (0.12f + 0.68f * s) * moveAmt * amp * (1 + side * asym) * (1 + 0.1f * n1 + effort) * (1 - 0.15f * tired);
            o[a] = bias + sw * u * (u > 0 ? reachF : reachB);
            float uE = MathF.Sin(th - 0.35f);
            float e0 = (0.22f + 1.1f * run) * elbowK + side * elAsym * run + 0.08f * n2 * run + 0.2f * effort - 0.25f * tired;
            o[2 + a] = MathF.Max(0.05f, e0 + 0.5f * run * (MathF.Max(0, uE) - 0.6f * MathF.Max(0, -uE)) + 0.07f * run * bounce);
            o[4 + a] = MathF.Max(0.04f, 0.1f + width + 0.05f * run * (1 - MathF.Max(0, u)) - 0.04f * run * cross * MathF.Max(0, u) + 0.06f * tired);
            o[6 + a] = -0.3f * run * cross * MathF.Max(0, u);
            // The hand lags the forearm's swing.
            o[8 + a] = 0.1f + (0.12f + 0.18f * s) * moveAmt * MathF.Cos(th);
        }
    }

    // ------------------------------------------------------------------ per frame

    public void Update(MatchSnapshot a, MatchSnapshot b, float alpha, double timeD, float switchT)
    {
        float time = (float)timeD;
        float dt = _lastTime < 0 ? 0 : Clamp((float)(timeD - _lastTime), 0, 0.05f);
        _lastTime = timeD;
        int held = b.HeldBy, owner = b.Owner;
        bool throwInSp = b.SetPiece == SetPieceKind.Throw;
        float ballX = b.BallX, ballY = b.BallY, ballZ = b.BallZ, ballVX = b.BallVX, ballVZ = b.BallVZ;
        float mt = (float)b.Time;
        _handBallId = -1;

        for (int id = 0; id < N; id++)
        {
            if (!b.Active[id] || _body[id] == null)
            {
                for (int k = 0; k < BodyMeshes.PartCount; k++)
                    for (int s2 = 0; s2 < PerPlayer[k]; s2++) Hide((Part)k, id * PerPlayer[k] + s2);
                continue;
            }
            PosePlayer(a, b, alpha, id, time, dt, mt, held, owner, throwInSp, ballX, ballY, ballZ, ballVX, ballVZ);
        }
        for (int k = 0; k < BodyMeshes.PartCount; k++) _mm[k].Buffer = _buf[k];
        if (_ball == null) return;

        _ball.Position = ShownBall(new Vector3(Mathf.Lerp(a.BallX, b.BallX, alpha), Mathf.Lerp(a.BallY, b.BallY, alpha), Mathf.Lerp(a.BallZ, b.BallZ, alpha)), dt);

        // Your player: the ring (it pulses on a switch) and the arrow over his head (a 1v1: both humans').
        bool show = b.Phase != Phase.Fulltime && b.DeadBallTaker < 0 && b.Phase != Phase.Goal && Markers;
        Mark(_ring, _marker, show ? b.Controlled : -1, a, b, alpha, time, switchT);
        Mark(_ring2, _marker2, show ? b.Controlled2 : -1, a, b, alpha, time, 1);
    }

    void Mark(MeshInstance3D ring, MeshInstance3D marker, int c, MatchSnapshot a, MatchSnapshot b, float alpha, float time, float switchT)
    {
        ring.Visible = marker.Visible = c >= 0;
        if (c < 0) return;
        float cx = Mathf.Lerp(a.X[c], b.X[c], alpha), cz = Mathf.Lerp(a.Z[c], b.Z[c], alpha);
        float pulse = switchT < 0.3f ? 1 + (0.3f - switchT) * 2 : 1;
        ring.Position = new Vector3(cx, 0.02f, cz);
        ring.Scale = new Vector3(pulse, 1, pulse);
        marker.Position = new Vector3(cx, 2.3f * b.Height[c] + MathF.Sin(time * 4) * 0.05f, cz);
        marker.Rotation = new Vector3(0, time * 1.5f, 0);
    }

    void PosePlayer(MatchSnapshot a, MatchSnapshot b, float alpha, int id, float time, float dt, float mt,
        int held, int owner, bool throwInSp, float ballX, float ballY, float ballZ, float ballVX, float ballVZ)
    {
        float x = Lerp(a.X[id], b.X[id], alpha);
        float z = Lerp(a.Z[id], b.Z[id], alpha);
        float df = b.Facing[id] - a.Facing[id];
        if (df > PI) df -= TAU;
        if (df < -PI) df += TAU;
        float facing = a.Facing[id] + df * alpha;
        float speed = b.Speed[id];
        float s = Clamp(speed / 8.5f, 0, 1);
        float phi = Lerp(a.StridePhase[id], b.StridePhase[id], alpha);
        float h = b.Height[id];
        var bs = _body[id];
        float vx = b.VX[id], vz = b.VZ[id];
        var action = b.Action[id];
        float accelFwd = b.AccelFwd[id];
        bool isHeld = held == id;
        bool gk = b.Role[id] == Role.GK;
        sbyte kickLeg = b.KickLeg[id];

        // ---------------- base gait
        float sinP = MathF.Sin(phi), cosP = MathF.Cos(phi);
        float moveAmt = Smooth(0.15f, 1.2f, speed);
        // Feet shuffle when turning on the spot (the sim advances the stride for it).
        float stepAmt = MathF.Max(moveAmt, MathF.Min(1, MathF.Abs(df) * 18));
        // Walking is its own gait: longer, straighter-legged steps, the swing knee folding only
        // to clear the grass; running drives the thigh and tucks the heel up behind.
        float walk = WalkGait(speed);
        float aHip = Lerp(0.12f + 0.62f * s, 0.36f, walk) * stepAmt;
        float hipL = aHip * sinP, hipR = -aHip * sinP;
        float kneeAmp = Lerp(0.25f + 1.35f * s, 0.85f, walk) * stepAmt;
        float kneeL = 0.1f + kneeAmp * MathF.Pow(MathF.Max(0, cosP), 1.4f) + 0.12f * s;
        float kneeR = 0.1f + kneeAmp * MathF.Pow(MathF.Max(0, -cosP), 1.4f) + 0.12f * s;
        float legOutL = 0.04f, legOutR = 0.04f, legYawL = 0, legYawR = 0;
        // Extra ankle angle on top of the auto-levelled foot (- = toes pointed).
        float ankleL = 0, ankleR = 0;
        // Arms: driven from the shoulders a beat behind the legs (see RunArms).
        RunArms(id, accelFwd, b.Stamina[id], phi, s, moveAmt, time);
        var ra = _arm;
        float armL = ra[0], armR = ra[1], elbowL = ra[2], elbowR = ra[3], armOutL = ra[4], armOutR = ra[5];
        float armRotL = ra[6], armRotR = ra[7], wristL = ra[8], wristR = ra[9];
        float hip0 = _hipBase[id];
        // (Less drop with planted feet: the knees then bend to take it instead.)
        // A runner is lowest as the stance foot takes his weight; a walker vaults over a straight
        // leg, highest mid-stance.
        float hipY = hip0 - (0.016f + 0.07f * s) * MathF.Abs(cosP) * moveAmt * (1 - 0.3f * _ikOn[id]) * (1 - walk)
            - 0.025f * (1 - MathF.Abs(cosP)) * moveAmt * walk;
        // Hips rotate and drop with each stride; the shoulders counter-rotate.
        float pelvisYaw = -0.1f * s * sinP * moveAmt;
        float pelvisRoll = 0.06f * (0.4f + s) * sinP * moveAmt;
        float twist = (0.05f + 0.2f * s) * sinP * moveAmt;
        float flexExtra = 0, sideExtra = 0;
        // The sim's lean is the physical one (tan = acceleration / g): the whole body tilts with
        // it, from the feet; in an action the pose carries most of the body.
        float leanF = b.LeanFwd[id] * (action == ActionKind.None ? 0.85f : 0.35f);
        float leanS = -b.LeanSide[id];
        float roll = 0, lift = 0, headPitch = 0;
        bool headLook = true;
        float yawExtra = 0, fwdShift = 0;

        // Side-steps and backpedalling: the legs shuffle instead of striding.
        {
            float cf0 = MathF.Cos(facing), sf0 = MathF.Sin(facing);
            float vf = vx * cf0 + vz * sf0;
            float vl = -vx * sf0 + vz * cf0;
            float slowEnough = 1 - Smooth(4.5f, 6.5f, speed);
            float sideAmt = speed > 0.25f ? Clamp(MathF.Abs(vl) / speed, 0, 1) * moveAmt * slowEnough : 0;
            float backAmt = vf < -0.3f ? Clamp(-vf / MathF.Max(speed, 0.01f), 0, 1) * slowEnough : 0;
            if (backAmt > 0.5f)
            {
                hipL = -hipL * 0.7f;
                hipR = -hipR * 0.7f;
                flexExtra += 0.12f;
            }
            if (sideAmt > 0)
            {
                float k = sideAmt * sideAmt;
                hipL *= 1 - k * 0.85f;
                hipR *= 1 - k * 0.85f;
                legOutL += k * (0.06f + 0.22f * MathF.Max(0, sinP));
                legOutR += k * (0.06f + 0.22f * MathF.Max(0, -sinP));
                kneeL += k * 0.3f;
                kneeR += k * 0.3f;
                hipL += k * 0.15f;
                hipR += k * 0.15f;
                hipY -= k * 0.07f;
                armL *= 1 - k;
                armR *= 1 - k;
                armOutL += k * 0.25f;
                armOutR += k * 0.25f;
                pelvisYaw *= 1 - k;
                twist *= 1 - k;
                flexExtra += k * 0.12f;
            }
        }

        // Changing direction: the body turns from the ground up; head first, hips lead.
        float turn = Clamp(_turnS[id], -10, 10);
        float headLead = 0;
        if (action == ActionKind.None)
        {
            float lean = Clamp(MathF.Abs(leanS) / 0.3f, 0, 1) * moveAmt;
            float pivot = MathF.Min(1, MathF.Abs(turn) / 9) * (1 - 0.5f * moveAmt);
            headLead = Clamp(-0.045f * turn, -0.4f, 0.4f);
            pelvisYaw += Clamp(-0.03f * turn, -0.3f, 0.3f);
            legYawL += Clamp(-0.035f * turn, -0.3f, 0.3f);
            legYawR += Clamp(-0.035f * turn, -0.3f, 0.3f);
            float inR = leanS > 0 ? 1 : 0;
            kneeL += lean * 0.28f * (1 - inR) + 0.1f * lean;
            kneeR += lean * 0.28f * inR + 0.1f * lean;
            legOutL += lean * 0.12f * inR;
            legOutR += lean * 0.12f * (1 - inR);
            hipY -= 0.05f * lean + 0.04f * pivot;
            kneeL += 0.2f * pivot;
            kneeR += 0.2f * pivot;
            flexExtra += 0.06f * lean + 0.08f * pivot;
        }

        // Braking: the heels dig in ahead of him, the knees give and the arms come out for balance.
        if (action == ActionKind.None)
        {
            float brake = Smooth(2.5f, 8, -accelFwd) * moveAmt;
            if (brake > 0)
            {
                kneeL += 0.25f * brake;
                kneeR += 0.25f * brake;
                hipY -= 0.05f * brake;
                armL += 0.2f * brake;
                armR += 0.2f * brake;
                armOutL += 0.18f * brake;
                armOutR += 0.18f * brake;
            }
        }

        // Shoulder to shoulder with an opponent: he leans into him and an arm comes out to hold
        // him off.
        if (action == ActionKind.None && !isHeld)
        {
            int myTeam = b.Team[id];
            float cf1 = MathF.Cos(facing), sf1 = MathF.Sin(facing);
            float best = 0, bestLat = 0;
            for (int j = 0; j < N; j++)
            {
                if (j == id || !b.Active[j] || b.Team[j] == myTeam) continue;
                float dx = b.X[j] - x, dz = b.Z[j] - z;
                float d2 = dx * dx + dz * dz;
                if (d2 > 0.95f * 0.95f || d2 < 1e-4f) continue;
                float d = MathF.Sqrt(d2);
                float lat = (dx * sf1 - dz * cf1) / d;
                float k = (1 - Smooth(0.55f, 0.95f, d)) * Smooth(0.3f, 0.8f, MathF.Abs(lat));
                if (k > best) { best = k; bestLat = lat; }
            }
            if (best > 0)
            {
                leanS -= 0.1f * best * MathF.Sign(bestLat);
                if (bestLat > 0) { armOutL += 0.45f * best; armL += 0.2f * best; elbowL += 0.3f * best; }
                else { armOutR += 0.45f * best; armR += 0.2f * best; elbowR += 0.3f * best; }
            }
        }

        // Idle breathing.
        if (moveAmt < 1)
        {
            float br = MathF.Sin(time * 2.1f + id) * 0.015f * (1 - moveAmt);
            armOutL += br;
            armOutR += br;
            flexExtra += br * 0.6f;
        }

        // Keeper ready stance: crouched, hands out at the waist; set when danger is close.
        if (gk)
        {
            float want = speed < 4.5f && action == ActionKind.None && !isHeld && b.Phase == Phase.Play ? 1 : 0;
            float gr = _gkReady[id] += (want - _gkReady[id]) * (1 - MathF.Exp(-dt * 6));
            if (gr > 0.001f)
            {
                float own = -b.Dir[b.Team[id]] * 52.5f;
                float ballD = MathF.Sqrt((ballX - own) * (ballX - own) + ballZ * ballZ);
                bool threat = (owner >= 0 && b.Team[owner] != b.Team[id] && ballD < 30) || (ballVX * MathF.Sign(own) > 8 && ballD < 35);
                float set = threat ? 1 : Smooth(45, 25, ballD) * 0.5f;
                float calm = (1 - Smooth(0.3f, 2.5f, speed) * 0.5f) * gr;
                float still = 1 - moveAmt;
                kneeL += (0.3f + 0.25f * set) * calm;
                kneeR += (0.3f + 0.25f * set) * calm;
                hipL += (0.18f + 0.12f * set) * calm;
                hipR += (0.18f + 0.12f * set) * calm;
                hipY -= (0.07f + 0.07f * set) * calm;
                if (speed < 1) hipY += MathF.Max(0, MathF.Sin(time * 9 + id)) * 0.018f * set * gr;
                roll += 0.035f * MathF.Sin(time * 1.6f + id * 1.3f) * still * (1 - 0.6f * set) * gr;
                float Hand(float k) => 0.06f * MathF.Sin(time * 2.3f + id + k) * (1 - 0.5f * set);
                armOutL = Lerp(armOutL, 0.36f + 0.12f * set, gr);
                armOutR = Lerp(armOutR, 0.36f + 0.12f * set, gr);
                armL = Lerp(armL, 0.45f + 0.35f * set + Hand(0), gr);
                armR = Lerp(armR, 0.45f + 0.35f * set + Hand(1.9f), gr);
                elbowL = Lerp(elbowL, 0.7f + 0.2f * set, gr);
                elbowR = Lerp(elbowR, 0.7f + 0.2f * set, gr);
                armRotL = Lerp(armRotL, 0.25f, gr);
                armRotR = Lerp(armRotR, 0.25f, gr);
                flexExtra += (0.2f + 0.1f * set) * gr;
                legOutL = Lerp(legOutL, MathF.Max(legOutL, 0.12f), gr);
                legOutR = Lerp(legOutR, MathF.Max(legOutR, 0.12f), gr);
            }
        }

        float sinceTouch = b.SinceTouch[id];
        // Dribble touch: quick flick of the leading leg, body over the ball.
        if (action == ActionKind.None && sinceTouch < 0.2f && owner == id)
        {
            float k = MathF.Sin(sinceTouch / 0.2f * PI);
            if (sinP > 0) { hipL += 0.35f * k; kneeL *= 1 - 0.5f * k; }
            else { hipR += 0.35f * k; kneeR *= 1 - 0.5f * k; }
            flexExtra += 0.08f * k;
        }

        // Steering touch: while close control bends the ball round a turn, the free leg's
        // swing goes out to it and brings it round.
        bool steering = false;
        float pullX = b.PullX[id], pullZ = b.PullZ[id];
        if (action == ActionKind.None && owner == id && ballY < 0.35f && mt - b.PullT[id] < 0.05f)
        {
            float bsp = MathF.Sqrt(ballVX * ballVX + ballVZ * ballVZ);
            float across = bsp > 0.5f ? MathF.Abs(pullX * ballVZ - pullZ * ballVX) / bsp : 0;
            steering = across > 1.4f;
        }
        if (owner != id || action != ActionKind.None) _steerSt[id] = 0;
        else if (steering && _steerSt[id] == 0)
        {
            float dA0 = Duty(speed) * PI;
            int best = 0;
            float bestT = 99;
            for (int sd = 0; sd < 2; sd++)
            {
                float sg = phi + (sd == 0 ? 0 : PI) - PI;
                sg -= TAU * MathF.Floor((sg + PI) / TAU);
                float aa = sg - dA0;
                if (aa < 0) aa += TAU;
                float u = aa / (TAU - 2 * dA0);
                float wait = u < 0.5f ? 0 : u <= 1 ? 9 : aa - (TAU - 2 * dA0);
                if (wait < bestT) { bestT = wait; best = sd; }
            }
            _steerLeg[id] = (byte)best;
            _steerSt[id] = 1;
            _steerE[id] = 0;
        }

        // Cushioning a high ball: chest out over it, or the thigh lifted to meet it.
        float touchH = b.TouchH[id];
        if (action == ActionKind.None && touchH > 0.5f && sinceTouch < 0.5f)
        {
            float st = sinceTouch;
            float k = Smooth(0, 0.06f, st) * (1 - Smooth(0.22f, 0.5f, st));
            if (touchH > (float)PlayerK.ControlHeight)
            {
                flexExtra -= 0.38f * k;
                leanF -= 0.15f * k;
                armOutL = Lerp(armOutL, 0.85f, k);
                armOutR = Lerp(armOutR, 0.85f, k);
                armL = Lerp(armL, 0.25f, k);
                armR = Lerp(armR, 0.25f, k);
                elbowL = Lerp(elbowL, 0.6f, k);
                elbowR = Lerp(elbowR, 0.6f, k);
                kneeL += 0.25f * k;
                kneeR += 0.25f * k;
                hipY -= 0.05f * k;
            }
            else if (kickLeg > 0)
            {
                hipR = Lerp(hipR, 1.15f, k);
                kneeR = Lerp(kneeR, 1.35f, k);
                armOutL = Lerp(armOutL, 0.5f, k);
                armOutR = Lerp(armOutR, 0.35f, k);
            }
            else
            {
                hipL = Lerp(hipL, 1.15f, k);
                kneeL = Lerp(kneeL, 1.35f, k);
                armOutR = Lerp(armOutR, 0.5f, k);
                armOutL = Lerp(armOutL, 0.35f, k);
            }
            headPitch += 0.35f * k;
            if (k > 0.3f) headLook = false;
        }

        // ---------------- actions
        float actionT = b.ActionT[id], actionDur = b.ActionDur[id];
        float pr = actionDur > 0 ? Clamp(actionT / actionDur, 0, 1) : 0;
        if (action != ActionKind.Trick) _trkW[id * 2] = _trkW[id * 2 + 1] = 0;
        // The legs and arms as the get-up sees them: lead leg, tucked leg, support hand on the tucked side.
        Limbs Grab(bool leadR) => new()
        {
            HipY = hipY, Lean = leanF, Flex = flexExtra, Roll = roll, Head = headPitch,
            LeadHip = leadR ? hipR : hipL, LeadKnee = leadR ? kneeR : kneeL, LeadOut = leadR ? legOutR : legOutL,
            TuckHip = leadR ? hipL : hipR, TuckKnee = leadR ? kneeL : kneeR, TuckOut = leadR ? legOutL : legOutR,
            SupArm = leadR ? armL : armR, SupOut = leadR ? armOutL : armOutR, SupElbow = leadR ? elbowL : elbowR,
            FreeArm = leadR ? armR : armL, FreeOut = leadR ? armOutR : armOutL, FreeElbow = leadR ? elbowR : elbowL,
        };
        void Pose(in Limbs q, bool leadR)
        {
            hipY = q.HipY; leanF = q.Lean; flexExtra = q.Flex; roll = q.Roll; headPitch = q.Head;
            if (leadR)
            {
                hipR = q.LeadHip; kneeR = q.LeadKnee; legOutR = q.LeadOut; hipL = q.TuckHip; kneeL = q.TuckKnee; legOutL = q.TuckOut;
                armL = q.SupArm; armOutL = q.SupOut; elbowL = q.SupElbow; armR = q.FreeArm; armOutR = q.FreeOut; elbowR = q.FreeElbow;
            }
            else
            {
                hipL = q.LeadHip; kneeL = q.LeadKnee; legOutL = q.LeadOut; hipR = q.TuckHip; kneeR = q.TuckKnee; legOutR = q.TuckOut;
                armR = q.SupArm; armOutR = q.SupOut; elbowR = q.SupElbow; armL = q.FreeArm; armOutL = q.FreeOut; elbowL = q.FreeElbow;
            }
        }
        switch (action)
        {
            case ActionKind.Trick:
                TrickPose(a, b, alpha, id, facing, actionT, actionDur, ref hipL, ref hipR, ref kneeL, ref kneeR, ref legOutL, ref legOutR,
                    ref legYawL, ref legYawR, ref armL, ref armR, ref armOutL, ref armOutR, ref elbowL, ref elbowR, ref hipY, ref leanF, ref leanS,
                    ref flexExtra, ref pelvisYaw, ref twist, ref lift, ref headPitch);
                break;
            case ActionKind.Kick:
            {
                // A real strike, timed to the moment the ball leaves the foot (KickContact):
                // the last stride plants beside the ball; back-lift with the knee folded; the
                // thigh drives and the shin whips through; follow-through, a hop on big shots.
                var type = b.KickType[id];
                float power = MathF.Min(1.15f, b.KickPower[id]);
                bool shot = type == KickType.Shot;
                bool lofted = b.KickLofted[id] && !shot;
                bool ground = !shot && !lofted;
                bool finesse = shot && power < 0.55f;
                float kh = b.KickHeight[id];
                float vol = shot ? Smooth(0.45f, 0.95f, kh) : 0;
                float halfV = shot ? Smooth(0.18f, 0.4f, kh) * (1 - vol) : 0;
                float st = b.KickStretch[id];
                float bf = b.KickBallF[id], bl = b.KickBallL[id];
                float sideFrac = Clamp(MathF.Abs(bl) / MathF.Max(0.3f, MathF.Sqrt(bf * bf + bl * bl)), 0, 1);
                float tc = MathF.Max(0.05f, b.KickContact[id]);
                float tf = MathF.Max(tc + 0.05f, actionDur);
                float t = actionT;
                float u = Clamp(t / tc, 0, 1);
                float v = Clamp((t - tc) / (tf - tc), 0, 1);
                float big = shot ? 0.55f + 0.45f * MathF.Min(1, power) : lofted ? 0.75f : 0.4f;
                static float EaseOut(float q) => 1 - (1 - q) * (1 - q);
                static float Ease(float q) => q * q * (3 - 2 * q);
                float inK = Smooth(0, 0.22f, u);
                float outK = Smooth(0.7f, 1, v);
                bool right = kickLeg > 0;

                // ---- kicking leg
                float backLift = ground ? -(0.12f + 0.4f * big) : -(0.2f + 0.65f * big) * (1 - 0.4f * vol);
                float heel = (ground ? 0.55f + 0.35f * big : 1.55f + 0.6f * big) * (1 - 0.35f * vol);
                float contactHip = (ground ? 0.4f : 0.32f) + 1.05f * vol + 0.08f * halfV + 0.3f * st * (1 - sideFrac);
                float followHip = (ground ? 0.7f : lofted ? 1.55f : finesse ? 0.95f : 0.95f + 0.75f * big) + 0.4f * vol;
                float kHip, kKnee;
                if (u < 0.5f)
                {
                    kHip = Lerp(0.12f, backLift, Ease(u / 0.5f));
                    kKnee = Lerp(0.35f, heel, EaseOut(MathF.Min(1, u / 0.32f)));
                }
                else if (t < tc)
                {
                    float q = (u - 0.5f) / 0.5f;
                    kHip = Lerp(backLift, contactHip, Ease(q));
                    float fold = heel * (1 + 0.08f * Smooth(0, 0.4f, q));
                    kKnee = Lerp(fold, 0.1f, MathF.Pow(Smooth(0.38f, 1, q), 1.3f));
                }
                else
                {
                    float rise = EaseOut(Smooth(0, 0.42f, v));
                    float fall = Smooth(0.42f, 1, v);
                    kHip = Lerp(Lerp(contactHip, followHip, rise), 0.18f, fall);
                    kKnee = Lerp(Lerp(0.1f, 0.22f, rise), 0.42f, fall);
                }
                float lockK = Smooth(0.35f, 0.6f, u) * (1 - Smooth(0.3f, 0.7f, v));
                float kAnkle = (ground ? -0.12f : lofted ? -0.55f : -0.85f) * lockK;
                float kOut = 0.04f + 0.14f * big * MathF.Sin(PI * MathF.Min(1, u / 0.85f)) * (u < 1 ? 1 : 0) - (shot ? 0.24f : 0.1f) * big * Smooth(0, 0.6f, v) * (1 - Smooth(0.6f, 1, v));
                float open = ground ? (right ? -0.65f : 0.65f) * (1 - Smooth(0.4f, 1, v)) : 0;
                float tgt = Clamp(-b.KickRel[id], -1.2f, 1.2f);
                float swingPhase = Smooth(0.5f, 1, u);
                float acrossK = tgt * 0.45f * swingPhase * (1 - v * 0.5f);
                float kOutVol = (0.35f * vol + 0.45f * st * sideFrac) * swingPhase * (1 - Smooth(0.5f, 1, v));

                // ---- standing leg: reaches, plants, takes the load; a big strike lifts it.
                float pHip, pKnee;
                if (u < 0.55f)
                {
                    float q = Ease(u / 0.55f);
                    pHip = Lerp(0.2f, 0.48f, q);
                    pKnee = Lerp(0.55f, 0.2f, q);
                }
                else if (t < tc)
                {
                    float q = (u - 0.55f) / 0.45f;
                    pHip = Lerp(0.48f, 0.14f, Ease(q));
                    pKnee = Lerp(0.2f, shot ? 0.48f : 0.36f, Ease(q));
                }
                else
                {
                    pHip = Lerp(0.14f, -0.38f, Ease(Smooth(0, 0.85f, v)));
                    pKnee = Lerp(shot ? 0.48f : 0.36f, 0.3f, v);
                }
                pKnee += 0.5f * st * Smooth(0.4f, 1, u) * (1 - Smooth(0.4f, 1, v));
                float hop = (shot && power > 0.55f ? MathF.Sin(PI * Smooth(0.08f, 0.62f, v)) * (0.05f + 0.05f * big) : 0)
                    + Smooth(0.75f, 1, vol) * MathF.Sin(PI * Smooth(0.7f, 1, u) * (1 - v * 0.6f)) * 0.14f;
                if (hop > 0) pKnee += hop * 2.5f;

                float kk = inK * (1 - outK);
                if (right)
                {
                    hipR = Lerp(hipR, kHip, kk);
                    kneeR = Lerp(kneeR, kKnee, kk);
                    hipL = Lerp(hipL, pHip, kk);
                    kneeL = Lerp(kneeL, pKnee, kk);
                    ankleR = kAnkle;
                    legYawR = (open + acrossK) * inK;
                    legOutR = Lerp(legOutR, kOut + kOutVol, inK * (1 - outK));
                }
                else
                {
                    hipL = Lerp(hipL, kHip, kk);
                    kneeL = Lerp(kneeL, kKnee, kk);
                    hipR = Lerp(hipR, pHip, kk);
                    kneeR = Lerp(kneeR, pKnee, kk);
                    ankleL = kAnkle;
                    legYawL = (open + acrossK) * inK;
                    legOutL = Lerp(legOutL, kOut + kOutVol, inK * (1 - outK));
                }

                // ---- arms: the opposite arm rises out wide and sweeps through; the other counters.
                float wind = Smooth(0, 0.55f, u) * (1 - Smooth(0.1f, 0.9f, v));
                float thru = Smooth(0.6f, 1, u) * (1 - outK);
                float oppOut = Lerp(0.15f, (ground ? 0.75f : 1.2f) * (0.7f + 0.3f * big) + 0.45f * st, MathF.Max(wind, st * swingPhase));
                float oppSwing = Lerp(0.35f * wind, -0.35f, thru);
                float sameSwing = Lerp(-0.55f * wind * big, 0.55f * big, thru);
                float keep = 1 - inK * (1 - outK);
                if (right)
                {
                    armOutL = Lerp(oppOut, armOutL, keep);
                    armL = Lerp(oppSwing, armL, keep);
                    armOutR = Lerp(0.35f, armOutR, keep);
                    armR = Lerp(sameSwing, armR, keep);
                    elbowL = Lerp(0.45f, elbowL, keep);
                }
                else
                {
                    armOutR = Lerp(oppOut, armOutR, keep);
                    armR = Lerp(oppSwing, armR, keep);
                    armOutL = Lerp(0.35f, armOutL, keep);
                    armL = Lerp(sameSwing, armL, keep);
                    elbowR = Lerp(0.45f, elbowR, keep);
                }
                // A punt: the ball held out in both hands, let go as the leg comes through.
                if (isHeld)
                {
                    float hold = inK * (1 - Smooth(0.75f, 1, u));
                    armL = Lerp(armL, 0.95f, hold);
                    armR = Lerp(armR, 0.95f, hold);
                    elbowL = Lerp(elbowL, 0.55f, hold);
                    elbowR = Lerp(elbowR, 0.55f, hold);
                    armOutL = Lerp(armOutL, 0.14f, hold);
                    armOutR = Lerp(armOutR, 0.14f, hold);
                }

                // ---- trunk: arch on the back-lift, over the ball for a driven strike.
                float atContact = MathF.Exp(-MathF.Pow((u - 1 + v * 2) * 2.2f, 2));
                float overBall = ground ? 0.12f : lofted ? -0.26f : finesse ? 0.06f : power > 1 ? -0.2f : 0.22f;
                flexExtra += (-0.08f * big * Smooth(0.1f, 0.5f, u) * (1 - swingPhase) + overBall * swingPhase * (1 - outK) + (shot && !finesse ? 0.12f * MathF.Sin(PI * v) : 0)) * inK;
                sideExtra += -kickLeg * (0.08f + 0.16f * big + 0.4f * vol) * MathF.Max(atContact, wind * 0.6f) * inK;
                float reachK = st * swingPhase * (1 - outK);
                hipY -= 0.13f * reachK;
                sideExtra += -kickLeg * 0.22f * sideFrac * reachK;
                leanF -= 0.1f * (1 - sideFrac) * reachK;
                leanF -= 0.2f * vol * swingPhase * (1 - outK);
                flexExtra += 0.14f * halfV * swingPhase * (1 - outK);
                pelvisRoll += -kickLeg * 0.2f * vol * swingPhase * (1 - outK);
                float turnBack = wind * (1 - swingPhase);
                float turnThru = swingPhase * (1 - outK);
                pelvisYaw = Lerp(pelvisYaw, kickLeg * 0.32f * big * turnBack - kickLeg * 0.22f * big * turnThru + tgt * 0.3f * turnThru, inK);
                twist = Lerp(twist, -kickLeg * 0.32f * big * turnBack + kickLeg * 0.18f * big * turnThru + tgt * 0.4f * turnThru, inK);
                pelvisRoll += -kickLeg * 0.06f * big * wind;
                hipY -= ((shot ? 0.05f : 0.035f) + 0.03f * big) * Smooth(0.45f, 0.85f, u) * (1 - outK);
                lift = hop;
                if (lofted) leanF -= 0.06f * swingPhase * (1 - outK);
                headLook = false;
                headPitch = 0.38f * (1 - Smooth(0.15f, 0.6f, v)) + 0.05f;
                break;
            }
            case ActionKind.Stretch:
            {
                // Reaching a leg out for a ball just beyond him, the same curve as the sim's reach.
                float ext = (float)Player.StretchExt(actionT, actionDur);
                float fwd = MathF.Max(0, b.KickBallF[id]);
                float lat = b.KickBallL[id] * kickLeg;
                float r = MathF.Max(0.3f, MathF.Sqrt(fwd * fwd + lat * lat));
                float ca = fwd / r, sa = lat / r;
                float reachA = 0.62f + 0.25f * Clamp((r - (float)PlayerK.Reach) / 0.4f, 0, 1);
                float sHip = reachA * ca, sOut = reachA * sa;
                bool right = kickLeg > 0;
                if (right)
                {
                    hipR = Lerp(hipR, sHip, ext);
                    kneeR = Lerp(kneeR, 0.08f, ext);
                    legOutR = Lerp(legOutR, sOut, ext);
                    ankleR = -0.45f * ext;
                    hipL = Lerp(hipL, 0.32f, ext);
                    kneeL = Lerp(kneeL, 0.75f, ext);
                }
                else
                {
                    hipL = Lerp(hipL, sHip, ext);
                    kneeL = Lerp(kneeL, 0.08f, ext);
                    legOutL = Lerp(legOutL, sOut, ext);
                    ankleL = -0.45f * ext;
                    hipR = Lerp(hipR, 0.32f, ext);
                    kneeR = Lerp(kneeR, 0.75f, ext);
                }
                hipY -= 0.17f * ext;
                flexExtra += 0.18f * ca * ext;
                sideExtra += -kickLeg * 0.3f * MathF.Abs(sa) * ext;
                pelvisRoll += -kickLeg * 0.12f * ext;
                float farOut = 0.95f * ext, nearOut = 0.4f * ext;
                if (right)
                {
                    armOutL = MathF.Max(armOutL, farOut);
                    armOutR = MathF.Max(armOutR, nearOut);
                    armL = Lerp(armL, -0.25f, ext);
                }
                else
                {
                    armOutR = MathF.Max(armOutR, farOut);
                    armOutL = MathF.Max(armOutL, nearOut);
                    armR = Lerp(armR, -0.25f, ext);
                }
                headLook = false;
                headPitch = 0.3f * ext;
                break;
            }
            case ActionKind.Tackle:
            {
                // Block tackle: sink on the standing leg, the tackling leg low and nearly straight.
                float load = Smooth(0, 0.2f, pr) * (1 - Smooth(0.75f, 1, pr));
                float reach = Smooth(0.12f, 0.42f, pr) * (1 - Smooth(0.62f, 0.9f, pr));
                bool right = kickLeg > 0;
                float side = right ? -1 : 1;
                const float tHip = 1.05f, tKnee = 0.18f;
                float tcf = MathF.Cos(facing), tsf = MathF.Sin(facing);
                float lx = b.LegX[id], lz = b.LegZ[id];
                float aim = Clamp(MathF.Atan2(-lx * tsf + lz * tcf, lx * tcf + lz * tsf), -0.6f, 0.6f) * reach;
                if (right)
                {
                    hipR = Lerp(hipR, tHip, reach);
                    kneeR = Lerp(kneeR, tKnee, reach);
                    legYawR = Lerp(legYawR, -0.5f, reach) - aim;
                    legOutR = Lerp(legOutR, 0.12f, reach);
                    hipL = Lerp(hipL, -0.15f, load);
                    kneeL = Lerp(kneeL, 0.75f, load);
                    armL = Lerp(armL, 0.65f, reach);
                    armR = Lerp(armR, -0.45f, reach);
                }
                else
                {
                    hipL = Lerp(hipL, tHip, reach);
                    kneeL = Lerp(kneeL, tKnee, reach);
                    legYawL = Lerp(legYawL, 0.5f, reach) + aim;
                    legOutL = Lerp(legOutL, 0.12f, reach);
                    hipR = Lerp(hipR, -0.15f, load);
                    kneeR = Lerp(kneeR, 0.75f, load);
                    armR = Lerp(armR, 0.65f, reach);
                    armL = Lerp(armL, -0.45f, reach);
                }
                hipY -= 0.17f * load;
                leanF += 0.12f * load - 0.22f * reach;
                pelvisYaw += side * 0.22f * reach;
                twist += side * 0.18f * reach;
                armOutL = armOutR = 0.1f + 0.5f * load;
                elbowL = elbowR = 0.5f;
                break;
            }
            case ActionKind.Slide:
            {
                // Down as he commits, the lead leg along the grass on its committed line, a small
                // bounce on landing, the support hand a beat later; once the slide dies he sits up
                // on that hand, tucks a leg under and comes up through one knee (GetUp).
                float speedNow = MathF.Sqrt(vx * vx + vz * vz);
                float entry = Clamp((b.SlideV0[id] - 6) / 2.5f, 0, 1);
                float vary = MathF.Sin(id * 12.9898f) * 0.5f;
                float t = actionT;
                float stop = b.SlideStop[id];
                float down = Smooth(0, 0.13f, t);
                float landT = MathF.Max(0, t - 0.12f);
                float bounce = landT > 0 ? MathF.Exp(-landT * 9) * MathF.Sin(landT * 26) : 0;
                float g = Clamp((t - stop + 0.05f) / (actionDur - stop + 0.05f), 0, 1) * (1 - Smooth(0.7f, 2.4f, speedNow));
                float lying = down;
                float reach = Smooth(0.03f, 0.12f, t) * (1 - Smooth(stop - 0.05f, stop + 0.15f, t));
                float cf0 = MathF.Cos(facing), sf0 = MathF.Sin(facing);
                float lx = b.LegX[id], lz = b.LegZ[id];
                float aim = Clamp(MathF.Atan2(-lx * sf0 + lz * cf0, lx * cf0 + lz * sf0), -0.6f, 0.6f) * reach;
                bool right = kickLeg > 0;
                var stand = Grab(right);
                float tuck = right ? 1 : -1;
                hipY = Lerp(hipY, 0.27f - 0.05f * entry + 0.04f * bounce, lying);
                leanF = Lerp(leanF, -(0.78f + 0.25f * entry + 0.08f * vary), lying) + 0.06f * bounce * lying;
                roll += tuck * (0.22f + 0.12f * entry + 0.05f * vary) * lying;
                flexExtra += (0.18f + 0.06f * vary) * lying;
                float leadHip = Lerp(0.3f, 0.6f + 0.05f * entry, reach), leadKnee = Lerp(0.55f, 0.06f, reach);
                float foldHip = 0.48f + 0.06f * vary, foldKnee = 1.95f;
                float supp = Smooth(0.08f, 0.26f, t);
                float freeArm = -1.0f - 0.25f * entry - 0.3f * bounce;
                if (right)
                {
                    hipR = Lerp(hipR, leadHip, lying);
                    kneeR = Lerp(kneeR, leadKnee, lying);
                    legYawR -= aim;
                    hipL = Lerp(hipL, foldHip, lying);
                    kneeL = Lerp(kneeL, foldKnee, lying);
                    legOutL = Lerp(legOutL, 0.28f, lying);
                    armL = Lerp(armL, 0.75f, supp);
                    armOutL = Lerp(armOutL, 0.45f, supp);
                    elbowL = Lerp(elbowL, 0.15f, supp);
                    armR = Lerp(armR, freeArm, lying);
                    armOutR = Lerp(armOutR, 0.8f + 0.1f * vary, lying);
                    elbowR = Lerp(elbowR, 0.5f, lying);
                }
                else
                {
                    hipL = Lerp(hipL, leadHip, lying);
                    kneeL = Lerp(kneeL, leadKnee, lying);
                    legYawL += aim;
                    hipR = Lerp(hipR, foldHip, lying);
                    kneeR = Lerp(kneeR, foldKnee, lying);
                    legOutR = Lerp(legOutR, 0.28f, lying);
                    armR = Lerp(armR, 0.75f, supp);
                    armOutR = Lerp(armOutR, 0.45f, supp);
                    elbowR = Lerp(elbowR, 0.15f, supp);
                    armL = Lerp(armL, freeArm, lying);
                    armOutL = Lerp(armOutL, 0.8f + 0.1f * vary, lying);
                    elbowL = Lerp(elbowL, 0.5f, lying);
                }
                if (g > 0) Pose(GetUp(g, 0, Grab(right), stand), right);
                break;
            }
            case ActionKind.Dive:
            {
                // The same pose the physics uses for the hands (KeeperPose), so saves happen where
                // you see them, on the same clock: spring off the near foot, fly at full stretch,
                // land on the side (a ball he's caught hugged in as he comes down), up via a knee.
                float t = actionT, flyT = b.DiveFly[id];
                float dirX = b.ActionDirX[id], dirZ = b.ActionDirZ[id];
                float side = MathF.Sign(dirX * MathF.Sin(facing) - dirZ * MathF.Cos(facing));
                if (side == 0) side = 1;
                float reachOut = Smooth(0.02f, (float)KeeperPose.ReachTime(flyT), t);
                float land = Smooth(flyT - 0.04f, flyT + 0.18f, t);
                float getUp = Smooth(flyT + 0.5f, flyT + 0.85f, t);
                float tRoll = b.DiveRoll[id], tLift = b.DiveLift[id];
                roll = -side * Lerp(Lerp(tRoll * reachOut, MathF.Max(tRoll, 1.5f), land), 0, getUp);
                lift = tLift * reachOut * (1 - land);
                leanF = 0;
                leanS = 0;
                float dip = 1 - Smooth(0, 0.1f, t);
                float fly = Smooth(0.03f, (float)KeeperPose.ReachTime(flyT) - 0.03f, t) * (1 - land);
                float lie = land * (1 - Smooth(flyT + 0.5f, flyT + 0.65f, t));
                float rise = Smooth(flyT + 0.45f, flyT + 0.65f, t) * (1 - Smooth(flyT + 0.75f, flyT + 0.95f, t));
                float reach = reachOut * (1 - Smooth(flyT + 0.45f, flyT + 0.65f, t));
                // Low (a smother, a ball skidding in at the post): flat along the grass.
                float flat = Smooth(1.25f, 1.5f, tRoll);
                hipY = hip0 - dip * (0.14f + 0.08f * flat) - rise * 0.38f;
                bool nearL = side > 0;
                float curl = lie;
                float pushHip = 0.1f * fly + 0.5f * curl, pushKnee = Lerp(0.9f * dip + 0.15f, 0.08f, fly) + 0.8f * curl;
                float trailHip = 0.75f * fly + 0.85f * curl, trailKnee = 1.3f * fly + 1.3f * curl;
                float kneelHip = 0.9f * rise, kneelKnee = 1.6f * rise;
                if (nearL)
                {
                    hipL = pushHip + kneelHip; kneeL = pushKnee + kneelKnee;
                    hipR = trailHip + kneelHip * 0.4f; kneeR = trailKnee + kneelKnee * 0.6f;
                }
                else
                {
                    hipR = pushHip + kneelHip; kneeR = pushKnee + kneelKnee;
                    hipL = trailHip + kneelHip * 0.4f; kneeL = trailKnee + kneelKnee * 0.6f;
                }
                // Arms: both out along the dive (the top hand over for a high one); after a catch
                // they bring the ball in to the chest as he lands, and keep it there.
                float gather = isHeld ? Smooth(0.12f, 0.3f, t) * (1 - rise) : land * (1 - Smooth(flyT + 0.45f, flyT + 0.62f, t)) * 0.6f;
                float up = Lerp(0.6f, 3.0f - 0.35f * flat, reach);
                float downA = isHeld ? 1.1f : 2.2f;
                float nearArm = Lerp(Lerp(Lerp(nearL ? armL : armR, up - 0.15f, MathF.Max(reach, 0.2f)), downA, gather), 0.3f, rise);
                float farArm = Lerp(Lerp(Lerp(nearL ? armR : armL, up + 0.08f, MathF.Max(reach, 0.2f)), downA, gather), 0.8f, rise);
                float nearOut = Lerp(Lerp(0.05f, 0.6f, rise), 0.2f, gather * (isHeld ? 1 : 0)), farOut = Lerp(0.1f, 0.25f, rise);
                float elb = Lerp(Lerp(Lerp(0.6f, 0.18f, reach), isHeld ? 1.6f : 0.5f, gather), 0.25f, rise);
                if (nearL) { armL = nearArm; armR = farArm; armOutL = nearOut; armOutR = farOut; }
                else { armR = nearArm; armL = farArm; armOutR = nearOut; armOutL = farOut; }
                elbowL = elbowR = elb;
                sideExtra += -side * 0.18f * fly;
                flexExtra += 0.35f * lie + 0.45f * rise + (isHeld ? 0.25f * gather : 0);
                headLook = false;
                headPitch = -0.15f * fly + 0.2f * lie;
                break;
            }
            case ActionKind.Catch:
            {
                // The hands take it where it met them (HandsOnBall) and bring it in to the chest;
                // the body goes to the ball: up off one leg for a high one, curled round it at the
                // chest, down behind it for a low one (one knee to the grass when it's wide or skidding).
                float yH = Clamp(b.CatchY[id], 0.1f, 2.7f);
                float latX = -b.CatchL[id];
                float gather = Smooth(0.2f, 0.8f, pr);
                float settle = 1 - Smooth(0.75f, 1, pr);
                float high = Smooth(1.65f, 2.05f, yH);
                float bump = MathF.Sin(MathF.Min(1, pr * 1.7f) * PI);
                lift = Clamp((yH - 1.85f) * 0.8f, 0, 0.42f) * bump;
                hipL += 0.95f * high * bump;
                kneeL += 1.5f * high * bump;
                flexExtra -= 0.14f * high * (1 - gather);
                float mid = Smooth(0.7f, 1.0f, yH) * (1 - high);
                flexExtra += 0.2f * mid * gather + 0.12f * gather;
                kneeL += 0.22f * mid;
                kneeR += 0.22f * mid;
                hipY -= 0.04f * mid;
                float low = 1 - Smooth(0.45f, 0.9f, yH);
                float kneel = low * Smooth(0.25f, 0.6f, MathF.Abs(latX) + (yH < 0.3f ? 0.4f : 0)) * Smooth(0, 0.12f, pr) * settle;
                float stoop = low * (1 - kneel) * settle;
                kneeL += 0.8f * stoop;
                kneeR += 0.8f * stoop;
                hipL += 0.5f * stoop;
                hipR += 0.5f * stoop;
                hipY -= 0.22f * stoop;
                flexExtra += 0.6f * stoop * (1 - 0.5f * gather);
                hipR = Lerp(hipR, 0.12f, kneel);
                kneeR = Lerp(kneeR, 1.6f, kneel);
                legYawR = Lerp(legYawR, -0.55f, kneel);
                hipL = Lerp(hipL, 0.95f, kneel);
                kneeL = Lerp(kneeL, 1.35f, kneel);
                hipY = Lerp(hipY, 0.5f, kneel);
                flexExtra += 0.4f * kneel * (1 - 0.5f * gather);
                sideExtra += -Clamp(latX, -0.6f, 0.6f) * 0.35f * (1 - gather);
                headLook = false;
                headPitch = 0.15f + 0.3f * low * (1 - gather) - 0.3f * high * (1 - gather);
                break;
            }
            case ActionKind.Punch:
            {
                // Up off the ground, both fists driven through the ball.
                float k = Smooth(0, 0.1f, pr) * (1 - Smooth(0.45f, 0.9f, pr));
                float jab = Smooth(0.02f, 0.14f, pr);
                lift = 0.3f * MathF.Sin(MathF.Min(1, pr * 1.8f) * PI);
                armL = Lerp(armL, 2.7f, k);
                armR = Lerp(armR, 2.8f, k);
                elbowL = elbowR = Lerp(elbowL, Lerp(1.7f, 0.12f, jab), k);
                armOutL = armOutR = Lerp(armOutL, 0.06f, k);
                hipL += 0.85f * k;
                kneeL += 1.3f * k;
                flexExtra -= 0.18f * k;
                headLook = false;
                headPitch = -0.4f * k;
                break;
            }
            case ActionKind.Header:
            {
                float k = MathF.Sin(pr * PI);
                lift = 0.38f * k;
                // Arch back, then snap the upper body through the ball.
                flexExtra += pr < 0.45f ? -0.3f * (pr / 0.45f) : Lerp(-0.3f, 0.35f, Smooth(0.45f, 0.7f, pr)) * (1 - Smooth(0.75f, 1, pr));
                headPitch = 0.35f * MathF.Sin(MathF.Min(1, pr * 2) * PI);
                armOutL = armOutR = 0.7f * k + 0.1f;
                armL = armR = 0.3f * k;
                kneeL = kneeR = 0.45f * k + 0.1f;
                headLook = false;
                break;
            }
            case ActionKind.Throw:
            {
                if (b.ThrowIn[id])
                {
                    // From above the head, back behind it, then over and through.
                    float back = Smooth(0, 0.4f, pr), over = Smooth(0.4f, 0.75f, pr);
                    armL = armR = Lerp(Lerp(2.6f, 3.45f, back), 2.0f, over);
                    elbowL = elbowR = Lerp(Lerp(0.5f, 1.5f, back), 0.15f, over);
                    flexExtra += Lerp(-0.25f, 0.25f, Smooth(0.3f, 0.7f, pr));
                }
                else if (b.KickLofted[id])
                {
                    // Keeper's overarm throw: the ball taken back behind the head, the other arm
                    // pointing where it's going, a step into it and the arm whipped over the top.
                    float tc = MathF.Max(0.05f, b.KickContact[id]);
                    float u = Clamp(actionT / tc, 0, 1), v = Clamp((actionT - tc) / MathF.Max(0.05f, actionDur - tc), 0, 1);
                    float back = Smooth(0, 0.5f, u), over = Smooth(0.55f, 1, u);
                    armR = Lerp(Lerp(0.6f, -1.5f, back), -3.75f, over) - 1.3f * Smooth(0, 0.7f, v);
                    elbowR = Lerp(Lerp(1.2f, 1.5f, back), 0.15f, over);
                    armOutR = 0.3f - 0.15f * over;
                    armL = Lerp(Lerp(0.5f, 1.45f, back), -0.2f, over);
                    elbowL = 0.25f;
                    armOutL = 0.25f;
                    twist = Lerp(-0.5f * back, 0.45f, over);
                    pelvisYaw = Lerp(-0.25f * back, 0.25f, over);
                    hipL = 0.55f * back * (1 - 0.3f * over);
                    kneeL = 0.3f + 0.15f * over;
                    hipR = -0.2f * over;
                    flexExtra += Lerp(-0.18f * back, 0.28f, over) * (1 - Smooth(0.5f, 1, v));
                    headLook = false;
                }
                else
                {
                    // Keeper's roll: down low on a bent front knee, the ball bowled along the grass.
                    float tc = MathF.Max(0.05f, b.KickContact[id]);
                    float u = Clamp(actionT / tc, 0, 1), v = Clamp((actionT - tc) / MathF.Max(0.05f, actionDur - tc), 0, 1);
                    float crouch = Smooth(0, 0.5f, u) * (1 - Smooth(0.3f, 1, v));
                    hipY -= 0.26f * crouch;
                    hipL += 0.95f * crouch;
                    kneeL += 1.05f * crouch;
                    hipR += 0.15f * crouch;
                    kneeR += 1.25f * crouch;
                    flexExtra += 0.6f * crouch;
                    armR = u < 1 ? Lerp(Lerp(0.3f, -0.95f, Smooth(0, 0.55f, u)), 0.45f, Smooth(0.55f, 1, u)) : Lerp(0.45f, 1.3f, Smooth(0, 0.6f, v));
                    elbowR = 0.12f;
                    armOutR = 0.12f;
                    armL = Lerp(0.4f, 0.9f, crouch);
                    armOutL = 0.4f;
                    elbowL = 0.35f;
                    headLook = false;
                    headPitch = 0.25f * crouch;
                }
                break;
            }
            case ActionKind.Fall:
            {
                // Knocked down the way the hit sends him, arms out to break it, a moment on the
                // grass, then up: off his front via both hands, off his back by sitting up (GetUp).
                float lying = Smooth(0, 0.2f, pr);
                float g = Clamp((pr - 0.5f) / 0.5f, 0, 1);
                bool right = (id & 1) == 0;
                var stand = Grab(right);
                float cf0 = MathF.Cos(facing), sf0 = MathF.Sin(facing);
                float adx = b.ActionDirX[id], adz = b.ActionDirZ[id];
                float fwd = adx * cf0 + adz * sf0;
                float lft = -adx * sf0 + adz * cf0;
                leanF = Lerp(leanF, fwd * 1.3f, lying);
                roll += lft * 1.1f * lying;
                hipY = Lerp(hipY, 0.28f, lying);
                flexExtra += 0.25f * lying;
                float reachArm = fwd >= 0 ? 0.6f + 0.8f * fwd : 0.6f + 1.5f * fwd;
                armL = Lerp(armL, reachArm, lying);
                armR = Lerp(armR, reachArm, lying);
                armOutL = Lerp(armOutL, 0.55f + MathF.Max(0, lft) * 0.5f, lying);
                armOutR = Lerp(armOutR, 0.55f + MathF.Max(0, -lft) * 0.5f, lying);
                elbowL = elbowR = Lerp(elbowL, 0.3f, lying);
                hipL = Lerp(hipL, 0.55f - fwd * 0.4f, lying);
                kneeL = Lerp(kneeL, 0.9f, lying);
                hipR = Lerp(hipR, 0.35f - fwd * 0.4f, lying);
                kneeR = Lerp(kneeR, 0.5f, lying);
                legOutL = Lerp(legOutL, 0.2f, lying);
                legOutR = Lerp(legOutR, 0.2f, lying);
                headPitch = -0.2f * lying * fwd;
                if (g > 0) Pose(GetUp(g, Smooth(-0.2f, 0.5f, fwd), Grab(right), stand), right);
                headLook = g > 0.85f;
                break;
            }
            case ActionKind.Stumble:
            {
                float k = MathF.Sin(pr * PI);
                leanF += 0.2f * k;
                flexExtra += 0.35f * k;
                sideExtra += MathF.Sin(id * 3.1f) * 0.25f * k;
                armOutL = armOutR = 0.9f * k;
                armL = armR = -0.5f * k;
                break;
            }
        }

        // Holding the ball.
        if (isHeld && action == ActionKind.None)
        {
            if (throwInSp)
            {
                armL = armR = 2.6f;
                elbowL = elbowR = 0.5f;
                armOutL = armOutR = 0.2f;
                flexExtra -= 0.12f;
            }
            else
            {
                // Tucked in at the chest (the hands are put on it in HandsOnBall), upright.
                armL = armR = 0.5f;
                elbowL = elbowR = 1.5f;
                armOutL = armOutR = 0.1f;
                flexExtra += 0.06f;
            }
        }

        // Goal celebration: the one picked with the buttons, or the classic.
        bool scorer = b.Phase == Phase.Goal && b.Scorer == id;
        float phaseT = b.PhaseT;
        bool cel = scorer && b.Celebration != null && phaseT >= b.CelebrationAt && phaseT < GoalSeq.Cut;
        if (cel)
        {
            float u = phaseT - b.CelebrationAt;
            float turnDir = b.CelebrationTurn;
            headLook = false;
            switch (b.Celebration.Value)
            {
                case CelebrationKind.Slide:
                {
                    // Down onto both knees, skidding at the camera; a roar; up, soaking it in.
                    float knees = Smooth(0.7f, 0.88f, u) * (1 - Smooth(3.7f, 4.2f, u));
                    float skid = Smooth(0.82f, 1.15f, u) * (1 - Smooth(2.4f, 2.8f, u));
                    float roar = Smooth(2.4f, 2.75f, u) * (1 - Smooth(3.6f, 4.0f, u));
                    float pump = MathF.Max(0, MathF.Sin((u - 2.5f) * 10)) * roar;
                    float fin = Smooth(3.9f, 4.4f, u);
                    hipY = Lerp(hipY, 0.5f, knees);
                    hipL = Lerp(hipL, 0.14f, knees);
                    hipR = Lerp(hipR, 0.06f, knees);
                    kneeL = Lerp(kneeL, 1.62f, knees);
                    kneeR = Lerp(kneeR, 1.56f, knees);
                    legOutL = Lerp(legOutL, 0.15f, knees);
                    legOutR = Lerp(legOutR, 0.15f, knees);
                    leanF = Lerp(leanF, -0.1f, knees);
                    leanS *= 1 - knees;
                    flexExtra += -0.5f * skid + 0.3f * roar + 0.06f * pump - 0.18f * fin;
                    headPitch += -0.5f * skid - 0.25f * roar - 0.2f * fin;
                    float ArmUp(float q) => Lerp(Lerp(Lerp(q, -0.55f, skid), 0.3f + 0.4f * pump, roar), 0.15f, fin);
                    armL = ArmUp(armL);
                    armR = ArmUp(armR);
                    armOutL = armOutR = Lerp(Lerp(Lerp(armOutL, 1.3f, skid), 0.38f, roar), 1.35f, fin);
                    elbowL = elbowR = Lerp(Lerp(Lerp(elbowL, 0.55f, skid), 2.05f - 0.55f * pump, roar), 0.15f, fin);
                    break;
                }
                case CelebrationKind.Plane:
                {
                    // The aeroplane: arms out like wings, banked into the turn.
                    float k = Smooth(0, 0.35f, u);
                    float bank = turnDir * 0.36f * k * (1 - Smooth(2.8f, 3.3f, u));
                    float fin = Smooth(3.6f, 4.1f, u);
                    float wob = MathF.Sin(time * 6.5f) * 0.07f * k * (1 - fin);
                    roll += bank;
                    armOutL = Lerp(Lerp(armOutL, 1.5f + wob, k), 0.5f, fin);
                    armOutR = Lerp(Lerp(armOutR, 1.5f - wob, k), 0.5f, fin);
                    armL = Lerp(Lerp(armL, 0.05f, k), -2.75f, fin);
                    armR = Lerp(Lerp(armR, 0.05f, k), -2.75f, fin);
                    elbowL = elbowR = Lerp(Lerp(elbowL, 0.05f, k), 0.15f, fin);
                    flexExtra -= 0.12f * k + 0.12f * fin;
                    headPitch -= 0.12f * k + 0.25f * fin;
                    break;
                }
                case CelebrationKind.Siu:
                {
                    // A crouch, a leap with a half turn, the landing: feet wide, arms flung down.
                    float load = Smooth(0.28f, 0.45f, u) * (1 - Smooth(0.45f, 0.52f, u));
                    float q = Clamp((u - 0.45f) / 0.6f, 0, 1);
                    float air = q > 0 && q < 1 ? MathF.Sin(PI * q) : 0;
                    float land = Smooth(1.0f, 1.06f, u) * (1 - Smooth(1.06f, 1.35f, u));
                    float pose = Smooth(1.02f, 1.22f, u);
                    if (u >= 0.45f) yawExtra = PI * turnDir * (1 - Smooth(0.47f, 0.98f, u));
                    lift += 0.6f * air;
                    kneeL += 0.8f * load + 0.7f * air + 0.5f * land;
                    kneeR += 0.8f * load + 0.7f * air + 0.5f * land;
                    hipL += 0.45f * load + 0.4f * air;
                    hipR += 0.45f * load + 0.25f * air;
                    hipY -= 0.18f * load + 0.12f * land + 0.11f * pose;
                    armL = Lerp(Lerp(armL, 0.9f, load), -2.4f, air);
                    armR = Lerp(Lerp(armR, 0.9f, load), -2.4f, air);
                    armOutL = armOutR = Lerp(armOutL, 0.35f, air);
                    legOutL = Lerp(legOutL, 0.3f, pose);
                    legOutR = Lerp(legOutR, 0.3f, pose);
                    hipL = Lerp(hipL, 0.3f, pose);
                    hipR = Lerp(hipR, 0.3f, pose);
                    kneeL = Lerp(kneeL, 0.5f, pose) + 0.4f * land;
                    kneeR = Lerp(kneeR, 0.5f, pose) + 0.4f * land;
                    armL = Lerp(armL, 0.5f, pose);
                    armR = Lerp(armR, 0.5f, pose);
                    armOutL = Lerp(armOutL, 0.8f, pose);
                    armOutR = Lerp(armOutR, 0.8f, pose);
                    elbowL = elbowR = Lerp(elbowL, 0.08f, pose);
                    flexExtra += 0.3f * load - 0.38f * pose;
                    headPitch -= 0.42f * pose;
                    break;
                }
                case CelebrationKind.Flip:
                {
                    // A standing backflip: load, arms swung up, tucked over, landed, arms to the sky.
                    float load = Smooth(0.55f, 0.85f, u) * (1 - Smooth(0.85f, 0.92f, u));
                    float k = Clamp((u - 0.85f) / 0.75f, 0, 1);
                    bool flying = k > 0 && k < 1;
                    float tuck = Smooth(0.1f, 0.32f, k) * (1 - Smooth(0.68f, 0.9f, k));
                    float land = Smooth(1.56f, 1.62f, u) * (1 - Smooth(1.62f, 1.95f, u));
                    float sky = Smooth(1.8f, 2.25f, u);
                    if (flying)
                    {
                        float th = -TAU * k * k * (3 - 2 * k);
                        const float c = 0.95f;
                        lift = c + 0.8f * MathF.Sin(PI * k) - c * MathF.Cos(th);
                        fwdShift = -c * MathF.Sin(th) * h;
                        leanF = th;
                        leanS = 0;
                        roll = 0;
                    }
                    hipL = Lerp(hipL + 0.65f * load + 0.5f * land, 1.85f, tuck);
                    hipR = Lerp(hipR + 0.65f * load + 0.5f * land, 1.85f, tuck);
                    kneeL = Lerp(kneeL + 1.0f * load + 0.9f * land, 2.15f, tuck);
                    kneeR = Lerp(kneeR + 1.0f * load + 0.9f * land, 2.15f, tuck);
                    hipY -= 0.28f * load + 0.25f * land;
                    float swing = flying ? 1 - tuck : 0;
                    float Arm(float q) => Lerp(Lerp(Lerp(Lerp(q, 1.0f, load), -2.8f, swing), -1.0f, tuck), -1.2f, land);
                    armL = Lerp(Arm(armL), -2.75f + 0.12f * MathF.Sin(time * 11), sky);
                    armR = Lerp(Arm(armR), -2.75f + 0.12f * MathF.Sin(time * 11 + 1.3f), sky);
                    armOutL = armOutR = Lerp(Lerp(armOutL, 0.12f, tuck), 0.45f, sky);
                    elbowL = elbowR = Lerp(Lerp(elbowL, 1.3f, tuck), 0.12f, sky);
                    flexExtra += 0.35f * load + 0.4f * tuck + 0.2f * land - 0.2f * sky;
                    headPitch += 0.25f * tuck - 0.35f * sky;
                    break;
                }
            }
        }
        else if (scorer && phaseT > 0.4f && phaseT < GoalSeq.Cut)
        {
            float u = phaseT - (float)GoalSeq.Front;
            if (u < 0.3f)
            {
                // The run: arms flung up.
                armL = armR = -2.7f;
                armOutL = armOutR = 0.5f;
                elbowL = elbowR = 0.2f;
                flexExtra -= 0.25f;
            }
            else
            {
                // A roar with pumped fists, sunk into a wide stance, then his signature pose.
                float set = 1 - Smooth(0.5f, 2.5f, speed);
                float roar = Smooth(0.3f, 0.7f, u) * (1 - Smooth(1.7f, 2.2f, u));
                float sig = Smooth(1.9f, 2.5f, u);
                float pump = MathF.Max(0, MathF.Sin((u - 0.3f) * 11)) * (1 - Smooth(1.3f, 1.7f, u));
                float st = set * (roar + sig * 0.6f);
                legOutL = Lerp(legOutL, 0.17f, st);
                legOutR = Lerp(legOutR, 0.17f, st);
                hipL += 0.22f * set * roar;
                hipR += 0.22f * set * roar;
                kneeL += 0.4f * set * roar;
                kneeR += 0.4f * set * roar;
                hipY -= (0.07f + 0.02f * pump) * set * roar;
                flexExtra += (0.28f + 0.1f * pump) * roar;
                headPitch -= 0.25f * roar + 0.12f * sig;
                headLook = false;
                float rA = 0.35f + 0.35f * pump;
                armL = Lerp(-2.7f, rA, roar);
                armR = Lerp(-2.7f, rA, roar);
                armOutL = armOutR = Lerp(0.5f, 0.35f, roar);
                elbowL = elbowR = Lerp(0.2f, 2.1f - 0.5f * pump, roar);
                int vv = id % 3;
                if (vv == 0)
                {
                    // Arms folded across the chest, shoulders back.
                    armL = Lerp(armL, 0.5f, sig);
                    armR = Lerp(armR, 0.45f, sig);
                    armOutL = Lerp(armOutL, 0.06f, sig);
                    armOutR = Lerp(armOutR, 0.04f, sig);
                    elbowL = Lerp(elbowL, 1.95f, sig);
                    elbowR = Lerp(elbowR, 1.8f, sig);
                    armRotL = -1.45f * sig;
                    armRotR = -1.4f * sig;
                    flexExtra -= 0.12f * sig;
                    headPitch += 0.04f * MathF.Sin(time * 2.2f) * sig;
                }
                else if (vv == 1)
                {
                    // Arms spread wide, chest out.
                    armL = Lerp(armL, 0.15f, sig);
                    armR = Lerp(armR, 0.15f, sig);
                    armOutL = Lerp(armOutL, 1.35f, sig);
                    armOutR = Lerp(armOutR, 1.35f, sig);
                    elbowL = Lerp(elbowL, 0.15f, sig);
                    elbowR = Lerp(elbowR, 0.15f, sig);
                    flexExtra -= 0.2f * sig;
                }
                else
                {
                    // "Calm down": palms pressed slowly toward the ground.
                    float press = 0.5f + 0.5f * MathF.Sin(time * 3.2f);
                    armL = Lerp(armL, 0.55f + 0.15f * press, sig);
                    armR = Lerp(armR, 0.55f + 0.15f * press, sig);
                    armOutL = Lerp(armOutL, 0.4f, sig);
                    armOutR = Lerp(armOutR, 0.4f, sig);
                    elbowL = Lerp(elbowL, 0.45f - 0.15f * press, sig);
                    elbowR = Lerp(elbowR, 0.45f - 0.15f * press, sig);
                    hipY -= 0.025f * press * sig * set;
                    kneeL += 0.12f * press * sig * set;
                    kneeR += 0.12f * press * sig * set;
                }
            }
        }
        else if (b.Phase == Phase.Goal && b.Scorer >= 0 && b.Team[b.Scorer] == b.Team[id] && phaseT > 1.2f)
        {
            armOutL = armOutR = 0.3f + 0.2f * MathF.Sin(time * 9 + id);
        }

        // Officials' signals: the referee points for a restart, linesmen raise the flag.
        if (id == RefSlot && RefPoint > 0)
        {
            // Arm straight out, level, toward the way play goes: the arm on that side, swung
            // up to horizontal and round from the front by the angle to it.
            float k = Smooth(0, 0.25f, RefPoint) * Smooth(2.2f, 1.9f, RefPoint);
            float fw = RefPointSide * MathF.Cos(facing), lf = RefPointSide * MathF.Sin(facing);
            float ang = MathF.Atan2(MathF.Abs(lf), fw);
            if (lf > 0)
            {
                armL = Lerp(armL, PI / 2, k);
                armOutL = Lerp(armOutL, ang, k);
                elbowL = Lerp(elbowL, 0.05f, k);
            }
            else
            {
                armR = Lerp(armR, PI / 2, k);
                armOutR = Lerp(armOutR, ang, k);
                elbowR = Lerp(elbowR, 0.05f, k);
            }
        }
        int li = Array.IndexOf(LineSlots, id);
        if (li >= 0)
        {
            float up = Smooth(0, 0.2f, FlagUp[li]) * Smooth(2.0f, 1.8f, FlagUp[li]);
            armR = Lerp(armR * 0.5f, -2.95f, up);
            armOutR = Lerp(0.12f, 0.08f, up);
            elbowR = Lerp(0.35f, 0.05f, up);
        }

        // ---------------- secondary motion: limbs and head carry inertia
        bool sc0 = !_secReady[id];
        float yNow = hipY + lift;
        if (sc0 || dt <= 0)
        {
            if (sc0)
            {
                _bodyY[id] = yNow;
                _bodyVy[id] = 0;
                _bodyAy[id] = 0;
                _lastFacing[id] = facing;
            }
        }
        else
        {
            float vy = (yNow - _bodyY[id]) / dt;
            float ay = Clamp((vy - _bodyVy[id]) / dt, -40, 40);
            _bodyAy[id] += (ay - _bodyAy[id]) * (1 - MathF.Exp(-dt * 25));
            _bodyVy[id] = vy;
            _bodyY[id] = yNow;
        }
        // Variation: nothing done exactly the same way twice (keyed on the player, the action's
        // start and the match clock), and a slow drift through the upper body all the time.
        {
            float handsOn = isHeld || action == ActionKind.Catch || action == ActionKind.Throw || throwInSp ? 0.35f : 1;
            float dA = Drift(mt, id, 11, 0.7f) * handsOn;
            float dB = Drift(mt, id, 13, 0.55f) * handsOn;
            float dT = Drift(mt, id, 15, 0.62f);
            float dS = Drift(mt, id, 17, 0.48f);
            armL += 0.06f * dA;
            armR += 0.06f * dB;
            armOutL += 0.03f * (0.5f + 0.5f * dB);
            armOutR += 0.03f * (0.5f + 0.5f * dA);
            elbowL *= 1 + 0.08f * dB;
            elbowR *= 1 + 0.08f * dA;
            twist += 0.035f * dT;
            sideExtra += 0.03f * dS;
            flexExtra += 0.025f * dT * dS;
            headPitch += 0.03f * dS;
            if (action != ActionKind.None && actionDur > 0)
            {
                float seed = id + 0.1373f * MathF.Round((mt - actionT) * 20) + 0.0371f * ActionNameLength(action);
                float e = Smooth(0, 0.15f, pr) * (1 - Smooth(0.82f, 1, pr));
                float ea = e * handsOn;
                armL = armL * (1 + 0.22f * ea * Rnd(seed, 1)) + 0.14f * ea * Rnd(seed, 2);
                armR = armR * (1 + 0.22f * ea * Rnd(seed, 3)) + 0.14f * ea * Rnd(seed, 4);
                armOutL += 0.08f * ea * Rnd(seed, 5);
                armOutR += 0.08f * ea * Rnd(seed, 6);
                elbowL *= 1 + 0.18f * ea * Rnd(seed, 7);
                elbowR *= 1 + 0.18f * ea * Rnd(seed, 8);
                armRotL += 0.12f * ea * Rnd(seed, 9);
                armRotR += 0.12f * ea * Rnd(seed, 10);
                twist = twist * (1 + 0.2f * e * Rnd(seed, 11)) + 0.1f * e * Rnd(seed, 12);
                flexExtra += 0.07f * e * Rnd(seed, 13);
                sideExtra += 0.06f * e * Rnd(seed, 14);
                headPitch += 0.06f * e * Rnd(seed, 15);
            }
        }
        float turnRate = 0;
        if (dt > 0)
        {
            float dF = facing - _lastFacing[id];
            if (dF > PI) dF -= TAU;
            if (dF < -PI) dF += TAU;
            turnRate = Clamp(dF / dt, -14, 14);
            _lastFacing[id] = facing;
        }
        _turnS[id] = sc0 ? 0 : _turnS[id] + (turnRate - _turnS[id]) * (1 - MathF.Exp(-dt * 12));
        {
            // Free to swing, or held to a pose the physics depends on (hands on the ball).
            float free = action is ActionKind.Dive or ActionKind.Catch or ActionKind.Throw ? 0.25f : isHeld ? 0.5f : 1;
            float aF = Clamp(accelFwd, -12, 12) * free;
            float aY = _bodyAy[id] * free;
            float lat = leanS;
            int bse = id * SecCount;
            var pose = _poseNow;
            pose[0] = armL; pose[1] = armR; pose[2] = armOutL; pose[3] = armOutR; pose[4] = elbowL; pose[5] = elbowR;
            var want = _want;
            float spin = _turnS[id] * free;
            float flare = MathF.Min(0.25f, MathF.Abs(spin) * 0.02f);
            want[ArmL] = -0.03f * aF - 0.025f * spin;
            want[ArmR] = -0.03f * aF + 0.025f * spin;
            want[OutL] = MathF.Max(0, -lat) * 0.5f - 0.15f * lat + flare;
            want[OutR] = MathF.Max(0, lat) * 0.5f + 0.15f * lat + flare;
            want[ElbowL] = want[ElbowR] = -0.02f * aF - 0.006f * aY;
            want[HeadPitch] = -0.012f * aF + 0.004f * aY;
            want[HeadRoll] = -0.2f * lat;
            want[Twist] = Clamp(0.03f * turnRate, -0.3f, 0.3f) * free;
            for (int c = 0; c < SecCount; c++)
            {
                int i = bse + c;
                if (sc0)
                {
                    _sec[i] = want[c];
                    _secV[i] = 0;
                }
                else
                {
                    // A pose that jumps is reached by swinging there: the jump goes into the spring.
                    if (c < SecSoak)
                    {
                        float jump = pose[c] - _secPose[i];
                        if (MathF.Abs(jump) > 0.2f + 14 * dt) _sec[i] = Clamp(_sec[i] - jump, -3, 3);
                    }
                    float w0 = SecW[c];
                    _secV[i] += (w0 * w0 * (want[c] - _sec[i]) - 2 * SecZ[c] * w0 * _secV[i]) * dt;
                    _sec[i] += _secV[i] * dt;
                }
                _secPose[i] = pose[c];
            }
            _secReady[id] = true;
            armL += _sec[bse + ArmL];
            armR += _sec[bse + ArmR];
            armOutL += _sec[bse + OutL];
            armOutR += _sec[bse + OutR];
            elbowL = MathF.Max(0, elbowL + _sec[bse + ElbowL]);
            elbowR = MathF.Max(0, elbowR + _sec[bse + ElbowR]);
            headPitch += _sec[bse + HeadPitch];
            twist += _sec[bse + Twist];
        }
        float headLag = _sec[id * SecCount + HeadRoll];

        // Feet plant during the stance of a stride (not while an action poses the legs).
        bool legsFree = action == ActionKind.None && lift < 0.01f && !cel && !scorer;
        _ikOn[id] += ((legsFree ? 1 : 0) - _ikOn[id]) * (1 - MathF.Exp(-dt * 10));
        bool wristFree = legsFree;

        // ---------------- body physics: a springy spine driven by the movement
        float flexTarget = Clamp(b.LeanFwd[id] * 0.35f - accelFwd * 0.01f + s * 0.12f + flexExtra, -0.6f, 0.7f);
        float sideTarget = Clamp(-leanS * 0.15f + sideExtra, -0.45f, 0.45f);
        const float w = 13, zeta = 0.34f;
        _spF[id] += (w * w * (flexTarget - _sF[id]) - 2 * zeta * w * _spF[id]) * dt;
        _sF[id] += _spF[id] * dt;
        _spS[id] += (w * w * (sideTarget - _sS[id]) - 2 * zeta * w * _spS[id]) * dt;
        _sS[id] += _spS[id] * dt;
        float spineFlex = _sF[id], spineSide = _sS[id];

        if (headLook)
        {
            float rel = MathF.Atan2(ballZ - z, ballX - x) - facing;
            float r = MathF.Atan2(MathF.Sin(rel), MathF.Cos(rel));
            float limit = 1.1f - 0.6f * s;
            float wantY = MathF.Abs(r) < 2.4f ? Clamp(-r, -limit, limit) : 0;
            _headYaw[id] += (wantY - _headYaw[id]) * (1 - MathF.Exp(-dt * 6));
        }
        else _headYaw[id] *= MathF.Exp(-dt * 8);

        // ---------------- skeleton
        float sc = h * _bodyScale[id];
        var R = new Transform3D(new Basis(Vector3.Up, PI / 2 - facing - yawExtra).Scaled(new Vector3(sc, sc, sc)),
            new Vector3(x + MathF.Cos(facing) * fwdShift, 0, z + MathF.Sin(facing) * fwdShift));
        // (Basis.Scaled scales in the parent's frame; for a uniform scale that's the same.)
        // Whole-body tilt about the ground point: lean into turns and accelerations.
        R = Chain(R, 0, lift, 0, leanF, 0, leanS + roll);

        float torsoW = (float)bs.TorsoW, torsoD = (float)bs.TorsoD, torsoL = (float)bs.TorsoL;
        var P = Chain(R, 0, hipY, 0, 0, pelvisYaw, pelvisRoll);
        Put(Part.Pelvis, id, P, torsoW, 1, torsoD);
        // The torso sits at the waist unrotated; the shader bends it through the spine.
        var T = ChainT(P, 0, 0.04f, 0);
        // Each footfall gives through the trunk; the shoulders sway over the stance leg.
        float give = MathF.Cos(2 * phi) * moveAmt * (action == ActionKind.None ? 1 : 0);
        float flex = spineFlex + 0.04f + (0.015f + 0.045f * s) * give;
        float tw = twist - pelvisYaw;
        float spineRoll = spineSide - 0.8f * pelvisRoll;
        Put(Part.Torso, id, T, torsoW, torsoL, torsoD, flex, tw, spineRoll);
        var C = Chain(T, 0, 0, 0, flex, tw, spineRoll);
        // Neck and head: level gaze, turned toward the ball, lagging the body a touch.
        float headLevel = -(leanF + flex) * 0.75f;
        float headRollA = -(leanS + roll * 0.2f + spineRoll) * 0.6f + headLag;
        float hP = headPitch + headLevel;
        float hY = _headYaw[id] + headLead;
        float neckLen = (float)bs.NeckLen, neckW = (float)bs.Neck;
        var Nk = Chain(C, 0, 0.58f * torsoL, 0, hP * 0.4f, hY * 0.3f, headRollA * 0.4f);
        Put(Part.Neck, id, Nk, neckW, neckLen, neckW);
        float top = 0.075f * neckLen;
        var Hd = Chain(Nk, 0, top, 0, hP * 0.6f, hY * 0.7f, headRollA * 0.6f);
        Hd = ChainT(Hd, 0, 0.02f * torsoL + (neckLen - 1) * 0.08f - top, 0);
        // His own head: size and proportions here (the hair follows), the face's build in the shader.
        var hk = _headScale[id];
        Put(Part.Head, id, Hd, hk.X, hk.Y, hk.Z);
        int style = _hair[id];
        for (int hp = (int)Part.HairShort; hp <= (int)Part.HairQuiff; hp++) Hide((Part)hp, id);
        // A buzz cut: the crop's shape hugging the skull.
        if (style == 1) Put(Part.HairShort, id, Hd, 0.985f * hk.X, 0.95f * hk.Y, 0.985f * hk.Z);
        else Put(HairOfStyle[style], id, Hd, hk.X, hk.Y, hk.Z);

        // Arms (left = +x local; swing + = forward). The shoulder moves with the arm.
        float armLen = (float)bs.ArmLen, armW = (float)bs.Arm, shoulder = (float)bs.Shoulder;
        float gl = gk ? 1.25f : 1;
        float wxL = wristFree ? wristL : 0.1f, wxR = wristFree ? wristR : 0.1f;
        // Hands on the ball: the arms are bent onto it wherever the pose holds it.
        HandsOnBall(b, id, x, z, facing, C, action, pr, isHeld, throwInSp, shoulder, torsoL, armLen, gl, wxL, wxR,
            ref armL, ref armR, ref armOutL, ref armOutR, ref elbowL, ref elbowR, ref armRotL, ref armRotR);
        for (int sd = 0; sd < 2; sd++)
        {
            float sideSign = sd == 0 ? 1 : -1;
            ArmChain(C, sideSign, shoulder, torsoL, armLen, sd == 0 ? armL : armR, sd == 0 ? armOutL : armOutR,
                sd == 0 ? elbowL : elbowR, sd == 0 ? armRotL : armRotR, out var j1, out var j2);
            Put(Part.UpperArm, id * 2 + sd, j1, armW, armLen, armW);
            Put(Part.Forearm, id * 2 + sd, j2, 0.5f + 0.5f * armW, armLen, 0.5f + 0.5f * armW);
            // Hand at the wrist, relaxed, the palm toward the body; keeper gloves are bigger.
            var j3 = Chain(j2, 0, -0.245f * armLen, 0, sd == 0 ? wxL : wxR, 0, sideSign * -0.08f);
            float g = gl;
            Put(Part.Hand, id * 2 + sd, j3, g, g, g);
            if (sd == 1 && _flags != null)
            {
                int fi = Array.IndexOf(LineSlots, id);
                if (fi >= 0) _flags[fi].Transform = ChainT(j3, 0, -0.08f, 0.02f);
            }
        }

        // Legs: the thigh curves into a soft knee (shader bend), the shin takes the rest. Through
        // the stance the ball of the foot stays where it landed and the leg is solved to reach it.
        float ik = _ikOn[id];
        float cf = MathF.Cos(facing), sf = MathF.Sin(facing);
        float duty = Duty(speed);
        float dutyA = duty * PI;
        float standing = 1 - Smooth(0.03f, 0.3f, stepAmt);
        float stepLen = (float)Player.StepLength(speed, h);
        float legLen = (float)bs.Leg;
        float l1 = THIGH * legLen, l2 = SHIN * legLen;
        bool upright = hipY > 0.55f && action != ActionKind.Slide && action != ActionKind.Dive && action != ActionKind.Fall;
        _pinv = P.AffineInverse();
        float thighW = (float)bs.Thigh, calfW = (float)bs.Calf;
        for (int sd = 0; sd < 2; sd++)
        {
            float sideSign = sd == 0 ? 1 : -1;
            int fi = id * 2 + sd;
            float hip = sd == 0 ? hipL : hipR;
            float knee = sd == 0 ? kneeL : kneeR;
            float outA = sd == 0 ? legOutL : legOutR;
            float hipX = sideSign * 0.092f * (1 + (torsoW - 1) * 0.6f);

            // Where this leg is in its stride: sg = 0 mid-stance, |q| < 1 through the stance.
            float sg = phi + (sd == 0 ? 0 : PI) - PI;
            sg -= TAU * MathF.Floor((sg + PI) / TAU);
            float q = standing > 0.5f ? 0 : sg / dutyA;
            bool stance = MathF.Abs(q) < 1;
            float go = 1 - standing;

            // The steering touch rides this leg's swing.
            float twS = 0;
            if (_steerSt[id] > 0 && _steerLeg[id] == sd)
            {
                float aa = sg - dutyA;
                if (aa < 0) aa += TAU;
                float u = stance ? 1 : aa / (TAU - 2 * dutyA);
                if (_steerSt[id] == 1 && u < 0.5f) _steerSt[id] = 2;
                else if (_steerSt[id] == 2 && stance) _steerSt[id] = 0;
                bool on = _steerSt[id] == 2 && go > 0.3f;
                _steerE[id] += ((on ? 1 : 0) - _steerE[id]) * (1 - MathF.Exp(-dt * (on ? 25 : 12)));
                float reach = MathF.Sqrt((ballX - x) * (ballX - x) + (ballZ - z) * (ballZ - z));
                twS = 0.8f * _steerE[id] * (1 - Smooth(0.6f, 0.95f, u)) * (1 - Smooth(0.95f, 1.35f, reach));
            }
            float yaw = (sd == 0 ? legYawL : legYawR) + sideSign * 0.35f * twS;
            float heelUp = stance ? (0.35f + 0.25f * s) * Smooth(0.15f, 1, q) * go : 0;
            float toesUp = 0.24f * (1 - 0.5f * s) * (stance ? 1 - Smooth(-1, -0.55f, q) : sg < 0 ? Smooth(-dutyA - 0.9f, -dutyA, sg) : 0) * go;
            float trail = !stance && sg > 0 ? 0.35f * (0.4f + s) * (1 - Smooth(dutyA, dutyA + 1.0f, sg)) * go : 0;

            if (ik > 0.001f && stance)
            {
                if (_inStance[fi] == 0)
                {
                    // Touch-down: put the foot where the stance will be centred under him.
                    float ahead = -q * 0.85f * duty * stepLen * (1 - standing) + 0.11f * sc;
                    float latP = (hipX + sideSign * MathF.Sin(outA) * (l1 + l2)) * sc;
                    _plantX[fi] = x + cf * ahead + sf * latP;
                    _plantZ[fi] = z + sf * ahead - cf * latP;
                    _inStance[fi] = 1;
                }
                _footW[fi] = _inStance[fi] == 1 ? (1 - Smooth(0.6f, 1, MathF.Abs(q))) * ik : _footW[fi] * MathF.Exp(-dt * 30);
            }
            else
            {
                _inStance[fi] = 0;
                _footW[fi] = 0;
            }
            float wf = _footW[fi];

            if (twS > 0.01f)
            {
                // The swing foot goes out to the ball's far side, just off the grass.
                float pm = MathF.Sqrt(pullX * pullX + pullZ * pullZ);
                if (pm < 1e-6f) pm = 1;
                var tgt = new Vector3(ballX - pullX / pm * 0.12f, 0.09f * sc, ballZ - pullZ / pm * 0.12f);
                if (LegIK(tgt, hipX, yaw, sideSign, l1, l2, true))
                {
                    hip = Lerp(hip, _ikH, twS);
                    knee = Lerp(knee, _ikK, twS);
                    outA = Lerp(outA, _ikOut, twS);
                }
            }

            // A skill move's foot on the ball (PlayersView.Tricks).
            float trkW = _trkW[fi];
            if (trkW > 0.001f && LegIK(_trkT[fi], hipX, yaw, sideSign, l1, l2, true))
            {
                hip = Lerp(hip, _ikH, trkW);
                knee = Lerp(knee, _ikK, trkW);
                outA = Lerp(outA, _ikOut, trkW);
            }

            if (wf > 0.001f)
            {
                // Ankle target: behind the planted ball of the foot, raised as the heel lifts.
                float rbY = (-0.068f * MathF.Cos(heelUp) - 0.11f * MathF.Sin(heelUp)) * sc;
                float rbZ = (-0.068f * MathF.Sin(heelUp) + 0.11f * MathF.Cos(heelUp)) * sc;
                var tgt = new Vector3(_plantX[fi] - cf * rbZ, 0.003f - rbY, _plantZ[fi] - sf * rbZ);
                if (LegIK(tgt, hipX, yaw, sideSign, l1, l2))
                {
                    hip = Lerp(hip, _ikH, wf);
                    knee = Lerp(knee, _ikK, wf);
                    outA = Lerp(outA, _ikOut, wf);
                }
                else if (stance) _inStance[fi] = 2; // out of reach (pushed off it): pick the foot up
            }
            Transform3D lj1 = default, lj2 = default;
            bool posed = false;
            if (upright && wf < 0.999f)
            {
                // Never through the grass: an action's foot that would go below it stands on it.
                LegChain(P, hipX, hip, yaw, sideSign * outA, knee, legLen, out lj1, out lj2);
                posed = true;
                var foot = lj2 * new Vector3(0, -SHIN * legLen, 0);
                float floor = 0.07f * sc;
                if (foot.Y < floor)
                {
                    foot.Y = floor;
                    if (LegIK(foot, hipX, yaw, sideSign, l1, l2))
                    {
                        hip = _ikH;
                        knee = _ikK;
                        outA = _ikOut;
                        posed = false;
                    }
                }
            }
            if (!posed) LegChain(P, hipX, hip, yaw, sideSign * outA, knee, legLen, out lj1, out lj2);
            Put(Part.ShortsLeg, fi, lj1, thighW, 1, thighW);
            Put(Part.Thigh, fi, lj1, thighW, legLen, thighW, knee * 0.22f);
            Put(Part.Shin, fi, lj2, calfW, legLen, calfW);
            // Ankle (+ = toes down): free, roughly level with the ground; planted, flat on it.
            float extra = sd == 0 ? ankleL : ankleR;
            float freeA = Clamp(hip - knee, -1.2f, 0.6f) - 0.35f * Smooth(0.6f, 1.0f, knee) + (trail - toesUp) * ik - extra;
            float flatA = hip - knee - leanF + heelUp - toesUp;
            float ankle = Lerp(Lerp(freeA, flatA, wf), hip - knee - leanF, twS);
            var j3 = ChainX(lj2, 0, -SHIN * legLen, 0, ankle);
            Put(Part.Boot, fi, j3, 1, 1, 1, heelUp * wf + 0.25f * trail * ik);
        }
    }

    /// <summary>The PWA keys its variation on the action's name length ('kick'.length ...).</summary>
    static int ActionNameLength(ActionKind k) => k switch
    {
        ActionKind.Kick => 4, ActionKind.Tackle => 6, ActionKind.Slide => 5, ActionKind.Dive => 4, ActionKind.Stumble => 7,
        ActionKind.Fall => 4, ActionKind.Header => 6, ActionKind.Throw => 5, ActionKind.Catch => 5, ActionKind.Celebrate => 9,
        ActionKind.Stretch => 7, ActionKind.Punch => 5, ActionKind.Trick => 5, _ => 4,
    };
}
