using System;
using Godot;
using GameNight.Render;
using GameNight.Sim;
using GameNight.Club;
using Part = GameNight.Render.BodyMeshes.Part;

namespace GameNight.Grounds;

/// <summary>
/// The substitutes and the managers (the PWA's bench.ts and managers.ts): seven real players
/// per dugout, built from the footballers' own parts and kit shader, so they look exactly like
/// the twenty-two on the pitch, and a manager out in each technical area.
///
/// The subs live their own small match day: mostly sat on the bench, each in his own way
/// (upright, elbows on knees, leaning back with a leg out, legs wide, slouched), now and then
/// getting up to stretch, walking out to watch from the front of the dugout, or jogging off
/// toward the corner to warm up. They follow the game: on the edge of the seat when their side
/// attacks, leaping up for a goal, hands on heads or slumped when they concede, clapping or
/// sulking at the end. The managers drift along the line with the play, arms folded, hands in
/// pockets, a hand on the chin, shouting, pointing, waving the team on; a goal brings the fist
/// pump or the arms out wide. The home manager is the club's own, as made in the coach
/// designer (looks, outfit, temper); the visitors' gets a random outfit and temper.
///
/// Purely visual (its own Random): the simulation doesn't know they exist. Every part is one
/// MultiMesh, so the lot is 14 draws.
/// </summary>
public sealed partial class BenchView
{
    const int PerBench = 7, Subs = PerBench * 2, Count = Subs + 2;
    const float THIGH = 0.43f, SHIN = 0.42f, HIP_Y = 0.94f, HEAD_TOP = 0.24f;
    const float BASE_HEIGHT = HIP_Y + 0.04f + 0.6f + HEAD_TOP;
    const float PI = MathF.PI, TAU = MathF.Tau;
    const float SeatTop = 0.45f;
    static readonly float DugX = Pitchside.Dugout.X, DugZ = Pitchside.Dugout.Y;
    static readonly float SeatZ = DugZ - 0.55f, FrontZ = DugZ + 0.95f, AreaZ = DugZ + 1.9f;

    // The same tables as PlayersView: instances per player, shader part kinds.
    static readonly int[] PerPlayer = { 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2 };
    static readonly int[] ShaderPart = { 1, 0, 0, 2, 0, 0, 0, 3, 4, 0, 5, 6, 7, 8 };
    static readonly Part[] HairOfStyle = { Part.HairShort, Part.HairShort, Part.HairCurly, Part.HairBun };
    const int Stride = 20;

    // The pose's smoothed shape: seat thighs, feet (+1 forward, -1 tucked), legs apart, knees
    // out, trunk lean, side bend, twist, head pitch, the eight arm angles and their weight.
    const int ThL = 0, ThR = 1, FtL = 2, FtR = 3, Lo = 4, Yw = 5, Flex = 6, Side = 7, Tw = 8, Head = 9, Arm = 10, ArmW = 18, ShapeN = 19;

    // Arm poses: swing L/R (+ forward), elbow L/R, out L/R, forearm turn L/R.
    static readonly float[] Thighs = { 0.42f, 0.4f, 0.55f, 0.6f, 0.14f, 0.12f, 0, 0 };
    static readonly float[] Knees = { 0.3f, 0.32f, 1.25f, 1.2f, 0.06f, 0.05f, -0.35f, -0.3f };
    static readonly float[] Folded = { 0.5f, 0.45f, 1.95f, 1.8f, 0.06f, 0.04f, -1.45f, -1.4f };
    static readonly float[] Clasped = { 0.62f, 0.6f, 0.45f, 0.45f, -0.06f, -0.06f, 0, 0 };
    static readonly float[] Hips = { -0.2f, -0.2f, 1.6f, 1.6f, 0.62f, 0.62f, -0.9f, -0.9f };
    static readonly float[] Behind = { -0.42f, -0.42f, 0.85f, 0.85f, 0.08f, 0.08f, -0.8f, -0.8f };
    static readonly float[] OnHead = { 2.3f, 2.25f, 2.05f, 2.0f, 0.85f, 0.85f, 0, 0 };
    static readonly float[] Up = { 2.8f, 2.75f, 0.3f, 0.3f, 0.45f, 0.45f, 0, 0 };
    static readonly float[] UpOne = { 2.65f, 0.4f, 0.4f, 1.9f, 0.5f, 0.3f, 0, 0 };
    static readonly float[] Clap = { 0.85f, 0.85f, 1.35f, 1.35f, 0.22f, 0.22f, -0.3f, -0.3f };
    static readonly float[] Reach = { 0.95f, 0.9f, 0.1f, 0.12f, 0.06f, 0.06f, 0, 0 };
    // The managers'.
    static readonly float[] Pockets = { -0.06f, -0.06f, 0.4f, 0.4f, 0.2f, 0.2f, -0.55f, -0.55f };
    static readonly float[] Chin = { 0.5f, 0.75f, 1.95f, 2.45f, 0.06f, 0.12f, -1.45f, -1.1f };
    static readonly float[] Shout = { 1.45f, 1.45f, 2.4f, 2.4f, 0.5f, 0.5f, -0.9f, -0.9f };
    static readonly float[] Point = { -0.05f, 1.55f, 0.35f, 0.08f, 0.12f, 0.25f, 0, 0 };
    static readonly float[] Beckon = { 1.25f, 1.25f, 0.9f, 0.9f, 0.35f, 0.35f, -0.3f, -0.3f };
    static readonly float[] Watch = { 0.95f, -0.04f, 1.85f, 0.35f, 0.08f, 0.14f, -1.25f, 0 };
    static readonly float[] What = { 0.45f, 0.45f, 0.75f, 0.75f, 1.15f, 1.15f, 0.6f, 0.6f };
    static readonly float[] Fist = { 0.1f, 1.6f, 0.4f, 2.3f, 0.15f, 0.45f, 0, -0.3f };
    static readonly float[] Face = { 2.0f, 2.0f, 2.5f, 2.5f, 0.3f, 0.3f, -0.6f, -0.6f };
    static readonly float[] Applaud = { 2.2f, 2.2f, 1.0f, 1.0f, 0.25f, 0.25f, -0.5f, -0.5f };

    /// <summary>The ways of sitting: thighs, feet, spread, knees out, trunk, arms.</summary>
    static readonly (float thL, float thR, float ftL, float ftR, float lo, float yw, float flex, float[] arms)[] Seats =
    {
        (1.4f, 1.38f, 0.4f, 0.5f, 0.14f, 0, -0.04f, Thighs),    // upright, hands on thighs
        (1.45f, 1.45f, -0.35f, -0.2f, 0.2f, 0.08f, 0.55f, Knees), // elbows on knees
        (1.05f, 1.4f, 1, 0.5f, 0.12f, 0, -0.2f, Folded),         // back, arms folded, a leg out
        (1.42f, 1.42f, 0.1f, 0.1f, 0.3f, 0.22f, 0.32f, Clasped),  // legs wide, hands clasped
        (1.1f, 1.08f, 1, 1, 0.1f, -0.05f, -0.15f, Thighs),        // slouched, legs stretched
    };
    static readonly float[][] Stands = { Folded, Hips, Behind, null };
    static readonly float[][] Idles = { Folded, Hips, Behind, Pockets, Chin, Shout, Point, Beckon, Watch, Clap };

    enum Want { Sit, Stand, Warm }
    enum React { None, Cheer, Despair, Sulk, Clap, Fist, What, Face, Applaud, Slump }

    sealed class Fig
    {
        public int Team;
        public bool Gk, Boss;
        public BodyShape B;
        public float HipBase, Scale, Leg;
        public int Hair;
        public float SeatX, X, Z, Facing = PI / 2, Speed, Phi, HeadYaw;
        public Want Want;
        public float Until, SpotX, SpotZ, StretchUntil;
        public float Sit = 1;
        public int Style, Stand, Idle;
        public CoachTemper Temper;
        public React React;
        public float ReactAt, ReactEnd, Ph;
        public int V;
        public readonly float[] Cur = new float[ShapeN], Tgt = new float[ShapeN];
        public float Lift, Clap;
    }

    readonly Fig[] _f;
    // Each figure's slot in the kit arrays (ka/kb): the bench its own, the photographers their outfit's.
    readonly int[] _pid;
    readonly int _n;
    readonly MultiMesh[] _mm = new MultiMesh[BodyMeshes.PartCount];
    readonly float[][] _buf = new float[BodyMeshes.PartCount][];
    readonly Random _rng = new();
    readonly float[] _edge = new float[2];
    float _t;
    int _score0 = -1, _score1 = -1;
    Phase _phase;
    float _ballX, _ballZ;

    public BenchView(Node3D root, Kit home, Kit away, Coach coach = null, bool press = false)
    {
        _n = Count + (press ? PressCount : 0);
        _f = new Fig[_n];
        _pid = new int[_n];
        for (int i = 0; i < Count; i++) _pid[i] = i;
        var meshes = BodyMeshes.Build(0.5f);
        var shader = GD.Load<Shader>("res://Shaders/body.gdshader");
        var shaderDouble = GD.Load<Shader>("res://Shaders/body_double.gdshader");
        var mats = new ShaderMaterial[BodyMeshes.PartCount];
        var ka = new Vector4[BodyMeshes.PartCount][];
        var kb = new Vector4[BodyMeshes.PartCount][];
        for (int k = 0; k < BodyMeshes.PartCount; k++)
        {
            mats[k] = new ShaderMaterial { Shader = k == (int)Part.ShortsLeg ? shaderDouble : shader };
            mats[k].SetShaderParameter("part", ShaderPart[k]);
            ka[k] = new Vector4[MatchSnapshot.N];
            kb[k] = new Vector4[MatchSnapshot.N];
            int count = _n * PerPlayer[k];
            _mm[k] = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = meshes[k] };
            _mm[k].InstanceCount = count;
            _buf[k] = new float[count * Stride];
            root.AddChild(new MultiMeshInstance3D
            {
                Multimesh = _mm[k],
                MaterialOverride = mats[k],
                // Under the dugout roof or in its shade: no shadows to cast.
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                ExtraCullMargin = 30,
            });
        }

        for (int i = 0; i < Count; i++)
        {
            bool boss = i >= Subs;
            int team = boss ? i - Subs : i / PerBench, seat = i % PerBench;
            float cx = (team == 0 ? -1 : 1) * DugX;
            var f = _f[i] = new Fig { Team = team, Boss = boss, Gk = !boss && seat == 0, Ph = R(0, 100), V = _rng.Next(3) };
            var mine = boss && team == 0 ? coach : null;
            double hM = mine?.Height ?? (boss ? R(1.72f, 1.88f) : R(1.74f, 1.94f));
            double wKg = mine != null ? hM * hM * (mine.Build == 0 ? 21.5 : mine.Build == 2 ? 29 : 25) : boss ? R(76, 92) : R(68, 86);
            f.B = Body.Shape(hM, wKg, 0.5, i * 17.3 + _rng.NextDouble() * 50);
            f.Leg = (float)f.B.Leg;
            f.HipBase = (THIGH + SHIN) * f.Leg + (HIP_Y - THIGH - SHIN);
            float h = mine != null ? 1 : boss ? R(0.97f, 1.02f) : R(0.97f, 1.05f);
            f.Scale = h * BASE_HEIGHT / (f.HipBase + 0.04f + 0.6f * (float)f.B.TorsoL + ((float)f.B.NeckLen - 1) * 0.08f + HEAD_TOP);
            if (boss)
            {
                f.Want = Want.Stand;
                f.Sit = 0;
                f.X = f.SpotX = cx;
                f.Z = f.SpotZ = AreaZ;
                f.Temper = mine?.Temper ?? (CoachTemper)_rng.Next(3);
                f.Idle = _rng.Next(Idles.Length);
                f.Until = R(3, 8);
            }
            else
            {
                f.SeatX = cx - 2.7f + seat * 0.9f + R(-0.05f, 0.05f);
                f.X = f.SeatX;
                f.Z = SeatZ;
                f.Style = _rng.Next(Seats.Length);
                f.Until = R(3, 25);
            }
            Dress(i, f, team == 0 ? home : away, ka, kb, mine);
            // Settle straight into the first pose.
            Shape(f, 0, false);
            Array.Copy(f.Tgt, f.Cur, ShapeN);
        }
        if (press) PressInit(root, ka, kb);
        for (int k = 0; k < BodyMeshes.PartCount; k++)
        {
            mats[k].SetShaderParameter("ka", ka[k]);
            mats[k].SetShaderParameter("kb", kb[k]);
        }
        for (int i = 0; i < Count; i++) Pose(i, _f[i], 0);
        if (press) PressUpdate(null, 0);
        Upload();
    }

    float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

    static Color Lin(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f).SrgbToLinear();
    static Vector4 Lin4(int rgb, float w) { var c = Lin(rgb); return new Vector4(c.R, c.G, c.B, w); }

    /// <summary>A sub in his side's kit (the keeper in his), with his number on the back; a
    /// manager in a dark suit, a camel coat, a puffer or the club tracksuit.</summary>
    void Dress(int id, Fig f, Kit kit, Vector4[][] ka, Vector4[][] kb, Coach coach)
    {
        int skin = TeamData.SkinTones[_rng.Next(TeamData.SkinTones.Length)];
        int hair = TeamData.HairColors[_rng.Next(TeamData.HairColors.Length)];
        f.Hair = _rng.Next(4);
        int shirt, trim, sleeve, cuff, hands, shorts, shortsTrim, legs, socks, sockTrim, boot, sole, number, pattern, numCol;
        if (f.Boss)
        {
            // The designer's coach (home), else a random one; dressed as Outfit.For has it.
            var style = coach?.Style ?? (CoachStyle)_rng.Next(4);
            var o = Outfit.For(style, kit.Shirt, f.Team == 1);
            if (coach != null)
            {
                skin = coach.Skin;
                hair = coach.Hair;
                f.Hair = coach.Hair < 0 ? -1 : coach.HairStyle switch { 2 => 2, 3 => 3, _ => 1 };
            }
            else
            {
                hair = new[] { 0x8f8f8f, 0xd6d3cc, 0x1b1410, 0x4a3324, 0x2e1f15 }[_rng.Next(5)];
                f.Hair = _rng.Next(5) == 0 ? -1 : 1;
            }
            shirt = o.Coat;
            trim = o.Trim;
            sleeve = o.Coat;
            cuff = o.Cuff;
            hands = skin;
            legs = shorts = socks = o.Trousers;
            shortsTrim = sockTrim = o.Stripe;
            boot = o.Shoes;
            sole = o.Sole;
            number = -1;
            pattern = o.Pattern;
            numCol = trim;
        }
        else
        {
            bool gk = f.Gk;
            shirt = gk ? kit.GkShirt : kit.Shirt;
            trim = gk ? kit.GkShorts : kit.Shirt2;
            sleeve = gk ? shirt : skin;
            cuff = gk ? 0xf2f0ea : skin;
            hands = gk ? 0xf2f0ea : skin;
            shorts = gk ? kit.GkShorts : kit.Shorts;
            shortsTrim = gk ? shirt : kit.Shirt2 == kit.Shorts ? kit.Shirt : kit.Shirt2;
            legs = skin;
            socks = gk ? kit.GkShorts : kit.Socks;
            sockTrim = gk ? kit.GkShirt : kit.Shirt2;
            (boot, sole) = new[] { (0x1b1b1d, 0xe8e6df), (0xf0efe9, 0x1b1b1d), (0xff6a2b, 0xf0efe9), (0x2fd3e8, 0xf0efe9), (0xd8262f, 0x1b1b1d) }[_rng.Next(5)];
            number = 12 + id % PerBench;
            pattern = gk ? 0 : kit.Pattern;
            numCol = gk ? 0x1d1d1d : kit.Shirt2 == kit.Shirt ? 0xffffff : kit.Shirt2;
        }
        void Set(Part part, int rgb) => Paint(id, part, rgb);
        Set(Part.Torso, shirt);
        ka[(int)Part.Torso][id] = Lin4(trim, number);
        kb[(int)Part.Torso][id] = Lin4(numCol, pattern);
        Set(Part.UpperArm, shirt);
        ka[(int)Part.UpperArm][id] = Lin4(f.Boss ? shirt : trim, 0);
        kb[(int)Part.UpperArm][id] = Lin4(f.Boss ? shirt : sleeve, 0);
        Set(Part.Forearm, f.Boss ? shirt : sleeve);
        ka[(int)Part.Forearm][id] = Lin4(cuff, 0);
        Set(Part.Hand, hands);
        Set(Part.Pelvis, shorts);
        Set(Part.ShortsLeg, shorts);
        ka[(int)Part.ShortsLeg][id] = Lin4(shortsTrim, 0);
        Set(Part.Thigh, legs);
        Set(Part.Shin, socks);
        ka[(int)Part.Shin][id] = Lin4(sockTrim, 0);
        Set(Part.Neck, skin);
        Set(Part.Head, skin);
        Set(Part.HairShort, hair);
        Set(Part.HairCurly, hair);
        Set(Part.HairBun, hair);
        Set(Part.Boot, boot);
        ka[(int)Part.Boot][id] = Lin4(sole, 0);
    }

    /// <summary>A figure's part in one colour (linear, in the instance colour).</summary>
    void Paint(int id, Part part, int rgb)
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

    // ------------------------------------------------------------------ helpers

    static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static float Smooth(float e0, float e1, float x)
    {
        float t = Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }
    static float Ease(float k) => Smooth(0, 1, k);
    static float Wrap(float a) => MathF.Atan2(MathF.Sin(a), MathF.Cos(a));

    static Transform3D Chain(in Transform3D parent, float x, float y, float z, float rx, float ry, float rz) =>
        parent * new Transform3D(Basis.FromEuler(new Vector3(rx, ry, rz), EulerOrder.Yxz), new Vector3(x, y, z));
    static Transform3D ChainT(in Transform3D parent, float x, float y, float z) =>
        new(parent.Basis, parent * new Vector3(x, y, z));
    static Transform3D ChainX(in Transform3D parent, float x, float y, float z, float rx) =>
        parent * new Transform3D(new Basis(Vector3.Right, rx), new Vector3(x, y, z));

    void Put(Part part, int index, in Transform3D m, float sx = 1, float sy = 1, float sz = 1, float c0 = 0, float c1 = 0, float c2 = 0)
    {
        var a = _buf[(int)part];
        int o = index * Stride;
        Vector3 x = m.Basis.Column0 * sx, y = m.Basis.Column1 * sy, z = m.Basis.Column2 * sz;
        a[o] = x.X; a[o + 1] = y.X; a[o + 2] = z.X; a[o + 3] = m.Origin.X;
        a[o + 4] = x.Y; a[o + 5] = y.Y; a[o + 6] = z.Y; a[o + 7] = m.Origin.Y;
        a[o + 8] = x.Z; a[o + 9] = y.Z; a[o + 10] = z.Z; a[o + 11] = m.Origin.Z;
        a[o + 16] = c0; a[o + 17] = c1; a[o + 18] = c2;
        a[o + 19] = _pid[PerPlayer[(int)part] == 2 ? index / 2 : index];
    }

    void Upload()
    {
        for (int k = 0; k < BodyMeshes.PartCount; k++) _mm[k].Buffer = _buf[k];
    }

    // ------------------------------------------------------------------ per frame

    public void Update(MatchSnapshot s, float dt)
    {
        if (dt <= 0) return;
        dt = MathF.Min(dt, 1 / 30f);
        _t += dt;
        _ballX = s.BallX;
        _ballZ = s.BallZ;
        Events(s);
        for (int team = 0; team < 2; team++)
        {
            bool attacking = s.Phase == Phase.Play && s.PossTeam == team && s.BallX * s.Dir[team] > 18;
            float att = attacking ? Smooth(0.5f, 0.85f, s.Excitement) : 0;
            _edge[team] += (att - _edge[team]) * (1 - MathF.Exp(-dt * 2));
        }
        for (int i = 0; i < Count; i++)
        {
            var f = _f[i];
            bool reacting = f.React != React.None && _t >= f.ReactAt;
            if (f.React != React.None && _t >= f.ReactEnd) f.React = React.None;
            if (!reacting && _t >= f.Until)
            {
                if (f.Boss) DecideBoss(f);
                else Decide(f);
            }
            Move(f, dt, reacting);
            Shape(f, dt, reacting);
            Pose(i, f, dt);
        }
        if (_n > Count) PressUpdate(s, dt);
        Upload();
    }

    /// <summary>Goals and the final whistle.</summary>
    void Events(MatchSnapshot s)
    {
        if (_score0 < 0)
        {
            _score0 = s.Score[0];
            _score1 = s.Score[1];
            _phase = s.Phase;
        }
        int scored = s.Score[0] > _score0 ? 0 : s.Score[1] > _score1 ? 1 : -1;
        _score0 = s.Score[0];
        _score1 = s.Score[1];
        if (scored >= 0)
            for (int i = 0; i < Count; i++)
            {
                var f = _f[i];
                bool ours = f.Team == scored;
                if (f.Boss)
                {
                    float sgn = f.Team == 0 ? -1 : 1;
                    if (ours)
                        switch (f.Temper)
                        {
                            case CoachTemper.Cool: Act(f, React.Fist, R(0.3f, 0.8f), R(1.5f, 2.5f)); f.V = 1; break; // one fist, no jump
                            case CoachTemper.Fiery: Act(f, _rng.Next(2) == 0 ? React.Fist : React.Cheer, R(0.1f, 0.4f), R(4, 6)); f.V = 0; break;
                            default:
                                // The showman's sprint down the touchline, arms up.
                                Act(f, React.Cheer, R(0.1f, 0.3f), R(5, 7));
                                f.SpotX = sgn * DugX + sgn * R(8, 12);
                                f.SpotZ = AreaZ + 0.6f;
                                f.Until = _t + R(7, 9);
                                break;
                        }
                    else
                        switch (f.Temper)
                        {
                            case CoachTemper.Cool: Act(f, React.Slump, R(0.5f, 1), R(2, 3)); break;
                            case CoachTemper.Fiery: Act(f, React.What, R(0.2f, 0.6f), R(4, 6)); break;
                            default: Act(f, React.Face, R(0.3f, 0.8f), R(3, 5)); break;
                        }
                }
                else if (ours)
                {
                    Act(f, React.Cheer, R(0.05f, 0.5f), R(4, 7));
                    // A couple run out to the touchline.
                    if (_rng.NextDouble() < 0.3 && !f.Gk) GoTo(f, Want.Stand, f.SeatX + R(-1.5f, 1.5f), FrontZ + R(0, 0.1f), R(7, 11));
                }
                else Act(f, _rng.NextDouble() < 0.7 ? React.Despair : React.Sulk, R(0.3f, 1.2f), R(3, 6));
            }
        if (s.Phase != _phase && s.Phase == Phase.Fulltime)
            for (int i = 0; i < Count; i++)
            {
                var f = _f[i];
                bool won = f.Team == 0 ? s.Score[0] >= s.Score[1] : s.Score[1] >= s.Score[0];
                if (f.Boss) Act(f, won ? React.Applaud : React.Slump, R(0.5f, 1.5f), R(6, 10));
                else Act(f, won ? React.Clap : React.Sulk, R(0.2f, 1.5f), R(6, 10));
            }
        _phase = s.Phase;
    }

    void Act(Fig f, React r, float delay, float dur)
    {
        f.React = r;
        f.ReactAt = _t + delay;
        f.ReactEnd = f.ReactAt + dur;
        f.V = _rng.Next(3);
    }

    void GoTo(Fig f, Want want, float x, float z, float dur)
    {
        f.Want = want;
        f.SpotX = x;
        f.SpotZ = z;
        f.Until = _t + dur;
        f.Stand = _rng.Next(Stands.Length);
    }

    /// <summary>A sub's next move, when he's done with what he was doing.</summary>
    void Decide(Fig f)
    {
        float cx = (f.Team == 0 ? -1 : 1) * DugX;
        if (f.Want != Want.Sit)
        {
            f.Want = Want.Sit;
            f.Style = _rng.Next(Seats.Length);
            f.Until = _t + R(10, 30);
            return;
        }
        int up = 0;
        for (int i = 0; i < Count; i++) if (_f[i] is var o && o != f && !o.Boss && o.Team == f.Team && o.Want != Want.Sit) up++;
        double r = _rng.NextDouble();
        if (up >= 2 || r < 0.66)
        {
            // Stay put; maybe shift into another way of sitting.
            if (_rng.NextDouble() < 0.6) f.Style = _rng.Next(Seats.Length);
            f.Until = _t + R(6, 22);
        }
        else if (r < 0.79) GoTo(f, Want.Stand, f.SeatX + R(-0.25f, 0.25f), DugZ + R(0.1f, 0.4f), R(4, 10)); // up to stretch the legs
        else if (r < 0.92 || f.Gk) GoTo(f, Want.Stand, cx + R(-4.5f, 4.5f), FrontZ + R(0, 0.12f), R(8, 18)); // out to watch
        else
        {
            // Warm-up: jog out toward the corner, stretch, jog on, back after a while.
            float sgn = f.Team == 0 ? -1 : 1;
            GoTo(f, Want.Warm, cx + sgn * R(5, 11), DugZ + R(0.3f, 0.9f), R(18, 32));
            f.StretchUntil = 0;
        }
    }

    /// <summary>A manager's: another stance, a step along the line with the play.</summary>
    void DecideBoss(Fig f)
    {
        float hx = (f.Team == 0 ? -1 : 1) * DugX;
        // A cool head folds his arms and watches; a fiery one shouts and points; a showman waves
        // them on. Each moves about the area as restless as he is.
        float[][] pick = f.Temper switch
        {
            CoachTemper.Cool => new[] { Folded, Folded, Folded, Pockets, Pockets, Chin, Chin, Behind, Hips },
            CoachTemper.Fiery => new[] { Shout, Shout, Point, Point, Hips, Folded, Beckon, Watch },
            _ => new[] { Beckon, Beckon, Point, Shout, Hips, Folded, Pockets, Clap },
        };
        f.Idle = Array.IndexOf(Idles, pick[_rng.Next(pick.Length)]);
        float roam = f.Temper == CoachTemper.Cool ? 0.4f : f.Temper == CoachTemper.Fiery ? 1.2f : 1;
        f.SpotX = hx + Clamp(_ballX * 0.08f, -3, 3) + R(-0.8f, 0.8f) * roam;
        f.SpotZ = AreaZ + R(-0.3f, 0.4f) * roam;
        f.Until = _t + (f.Temper == CoachTemper.Cool ? R(7, 14) : R(3, 8));
    }

    void Move(Fig f, float dt, bool reacting)
    {
        // Reactions that bring a sub to his feet; the rest he has where he is.
        bool upNow = !f.Boss && reacting && (f.React == React.Cheer || f.React == React.Clap || f.React == React.Sulk && f.Sit < 0.5f);
        Want want = upNow && f.Want == Want.Sit ? Want.Stand : f.Want;
        float sx = f.SpotX, sz = f.SpotZ;
        if (!f.Boss && f.Want == Want.Sit)
        {
            sx = f.SeatX;
            sz = SeatZ + 0.42f;
        }
        float rate = dt / (reacting && f.React == React.Cheer ? 0.45f : 0.9f);
        if (f.Sit > 0 && want != Want.Sit) f.Sit = MathF.Max(0, f.Sit - rate);
        if (f.Sit > 0)
        {
            // Down (or on the way up or down): square to the pitch; rising carries the hips
            // forward over the feet.
            f.Z = Lerp(SeatZ + 0.42f, SeatZ, Ease(f.Sit));
            f.X = f.SeatX;
            f.Speed = 0;
            f.Facing += (PI / 2 - f.Facing) * (1 - MathF.Exp(-dt * 4));
            if (want == Want.Sit) f.Sit = MathF.Min(1, f.Sit + rate);
            return;
        }
        // On his feet: walk (or jog) to the spot, out and in through the front of the dugout.
        bool warm = want == Want.Warm;
        if (warm && _t < f.StretchUntil)
        {
            sx = f.X;
            sz = f.Z;
        }
        float tx = sx, tz = sz;
        bool Inside(float z) => !f.Boss && z < FrontZ;
        if (MathF.Abs(tx - f.X) > 0.35f && (Inside(f.Z) || Inside(tz)))
        {
            if (Inside(f.Z)) tx = f.X;
            tz = FrontZ + 0.25f;
        }
        float dx = tx - f.X, dz = tz - f.Z, d = MathF.Sqrt(dx * dx + dz * dz);
        bool final = tx == sx && tz == sz;
        float wantSpeed = 0, face;
        if (final && d < 0.12f)
        {
            if (warm && _t >= f.StretchUntil && f.Speed < 0.3f)
            {
                // Arrived: stretch here a while, then jog on to the next spot.
                f.StretchUntil = _t + R(4, 7);
                f.V = _rng.Next(3);
                float cx = (f.Team == 0 ? -1 : 1) * DugX, sgn = f.Team == 0 ? -1 : 1;
                f.SpotX = cx + sgn * R(5, 12);
                f.SpotZ = DugZ + R(0.3f, 0.9f);
            }
            // Stood: watch the ball (or face the pitch to sit down).
            face = want == Want.Sit ? PI / 2 : MathF.Atan2(_ballZ - f.Z, _ballX - f.X);
            if (want != Want.Sit) face = PI / 2 + Clamp(Wrap(face - PI / 2), -1.2f, 1.2f);
        }
        else
        {
            float pace = warm ? 3.2f : reacting && (f.React == React.Cheer || f.React == React.Fist) ? 4.5f : 1.35f;
            wantSpeed = MathF.Min(pace, d * 2 + 0.3f);
            face = MathF.Atan2(dz, dx);
        }
        f.Speed += Clamp(wantSpeed - f.Speed, -5 * dt, 4 * dt);
        f.Facing += Wrap(face - f.Facing) * (1 - MathF.Exp(-dt * 6));
        f.Facing = Wrap(f.Facing);
        if (d > 0.01f)
        {
            float step = MathF.Min(d, f.Speed * dt);
            f.X += dx / d * step;
            f.Z += dz / d * step;
        }
        f.Phi += f.Speed / (1.1f + 0.35f * f.Speed) * TAU * 0.5f * dt;
        // At the seat and square to the pitch: down he goes.
        if (want == Want.Sit && final && d < 0.15f && f.Speed < 0.4f && MathF.Abs(Wrap(f.Facing - PI / 2)) < 0.25f) f.Sit = 0.001f;
    }

    /// <summary>The pose he's going for, eased in so nothing snaps.</summary>
    void Shape(Fig f, float dt, bool reacting)
    {
        var g = f.Tgt;
        float t = _t, st = Ease(f.Sit), standing = 1 - st;
        float edge = f.Boss ? 0 : _edge[f.Team];
        var seat = Seats[f.Style];
        g[ThL] = Lerp(seat.thL, 1.38f, edge * 0.6f);
        g[ThR] = Lerp(seat.thR, 1.38f, edge * 0.6f);
        g[FtL] = Lerp(seat.ftL, -0.4f, edge * 0.6f);
        g[FtR] = Lerp(seat.ftR, -0.3f, edge * 0.6f);
        g[Lo] = Lerp(0.04f, seat.lo, st);
        g[Yw] = seat.yw * st;
        g[Flex] = st * Lerp(seat.flex, 0.5f, edge * 0.7f);
        g[Side] = 0;
        g[Head] = 0;
        float moving = Smooth(0.3f, 1.0f, f.Speed);
        float[] arms;
        float w;
        if (f.Boss)
        {
            arms = Idles[f.Idle];
            w = 1 - moving;
        }
        else if (standing > 0.5f)
        {
            arms = f.Want == Want.Warm && t < f.StretchUntil ? null : Stands[f.Stand];
            w = arms != null ? 1 - moving : 0;
        }
        else
        {
            arms = edge > 0.3f && seat.arms != Folded ? Knees : seat.arms;
            w = st;
        }
        f.Lift = 0;
        f.Clap = 0;
        if (reacting)
        {
            float k = Smooth(0, 0.3f, t - f.ReactAt) * Smooth(0, 0.4f, f.ReactEnd - t);
            w = Lerp(w, 1, k);
            float jump = MathF.Max(0, MathF.Sin((t + f.Ph) * 7.5f));
            switch (f.React)
            {
                case React.Cheer:
                    arms = f.V == 1 ? UpOne : Up;
                    if (standing > 0.95f) f.Lift = 0.09f * jump * jump * k;
                    g[Flex] -= 0.2f * k;
                    g[Head] -= 0.15f * k;
                    break;
                case React.Fist:
                    arms = Fist;
                    if (f.V == 0) f.Lift = 0.07f * jump * jump * k;
                    g[Flex] += 0.1f * k;
                    break;
                case React.Despair:
                    arms = f.V == 0 ? OnHead : st > 0.5f ? Knees : Hips;
                    g[Flex] += (f.V == 0 ? 0.25f : 0.45f) * k;
                    g[Head] += 0.5f * k;
                    break;
                case React.Sulk:
                case React.Slump:
                    arms = Hips;
                    g[Head] += 0.4f * k;
                    g[Flex] += 0.1f * k;
                    break;
                case React.Clap:
                case React.Applaud:
                    arms = f.React == React.Clap ? Clap : Applaud;
                    f.Clap = MathF.Max(0, MathF.Sin((t + f.Ph) * 13)) * 0.2f * k;
                    break;
                case React.What:
                    arms = What;
                    g[Flex] -= 0.08f * k;
                    break;
                case React.Face:
                    arms = Face;
                    g[Flex] += 0.3f * k;
                    g[Head] += 0.3f * k;
                    break;
            }
        }
        // Seated, the shoulders turn a little with the head toward the ball.
        float rel = Wrap(MathF.Atan2(_ballZ - f.Z, _ballX - f.X) - f.Facing);
        g[Tw] = -Clamp(rel * 0.35f, -0.4f, 0.4f) * st;
        // Warm-up stretches on the spot: a hamstring reach, an overhead side bend, trunk twists.
        if (f.Want == Want.Warm && t < f.StretchUntil && standing > 0.9f && !reacting)
        {
            float k = Smooth(0, 0.6f, f.StretchUntil - t);
            float osc = MathF.Sin((t + f.Ph) * (f.V == 2 ? 2.2f : 1.5f));
            if (f.V == 0)
            {
                g[Flex] += 0.65f * k;
                arms = Reach;
            }
            else if (f.V == 1)
            {
                arms = Up;
                g[Side] += 0.35f * osc * k;
            }
            else
            {
                arms = Hips;
                g[Tw] += 0.55f * osc * k;
            }
            w = k;
        }
        if (arms != null) Array.Copy(arms, 0, g, Arm, 8);
        else w = 0;
        // Little fidgets: a slow sway of the trunk and a hand shifting on the thigh.
        float fid = MathF.Sin((t + f.Ph) * 0.37f) * MathF.Sin((t + f.Ph) * 0.11f);
        g[Side] += 0.05f * fid * st;
        g[Arm] += 0.08f * fid * st;
        g[ArmW] = w;
        var c = f.Cur;
        float a = 1 - MathF.Exp(-dt * 4.5f);
        for (int i = 0; i < ShapeN; i++) c[i] += (g[i] - c[i]) * a;
    }

    /// <summary>The skeleton, as PlayersView builds it: seated legs on the bench (each shin
    /// finding the floor), a simple walk or jog cycle on his feet, trunk, head and arms from the
    /// shape.</summary>
    void Pose(int id, Fig f, float dt)
    {
        var c = f.Cur;
        var b = f.B;
        float sc = f.Scale, leg = f.Leg, st = Ease(f.Sit);
        float move = Smooth(0.05f, 0.6f, f.Speed), jog = Smooth(1.6f, 3.2f, f.Speed);
        float phi = f.Phi;

        // Head: toward the ball unless it's down.
        float relB = Wrap(MathF.Atan2(_ballZ - f.Z, _ballX - f.X) - f.Facing);
        float wantY = c[Head] < 0.25f && MathF.Abs(relB) < 2.4f ? Clamp(-relB, -1.1f, 1.1f) : 0;
        f.HeadYaw += (wantY - f.HeadYaw) * (1 - MathF.Exp(-dt * 5));

        // Legs: walking ones and seated ones, blended by how far down he is.
        float hj = (SeatTop + 0.075f) / sc;
        float seatHipY = hj + 0.03f;
        float standHipY = f.HipBase - (0.012f + 0.05f * jog) * MathF.Abs(MathF.Cos(phi)) * move;
        float hipY = Lerp(standHipY, seatHipY, st);

        var root = new Transform3D(new Basis(Vector3.Up, PI / 2 - f.Facing).Scaled(new Vector3(sc, sc, sc)), new Vector3(f.X, 0, f.Z));
        root = Chain(root, 0, f.Lift / sc, 0, 0.08f * jog, 0, 0);
        float pelvisYaw = -0.08f * MathF.Sin(phi) * move;
        var P = Chain(root, 0, hipY, 0, 0, pelvisYaw, 0);
        float torsoW = (float)b.TorsoW, torsoD = (float)b.TorsoD, torsoL = (float)b.TorsoL;
        Put(Part.Pelvis, id, P, torsoW, 1, torsoD);
        var T = ChainT(P, 0, 0.04f, 0);
        float flex = c[Flex] + 0.04f, tw = c[Tw] - pelvisYaw, side = c[Side];
        Put(Part.Torso, id, T, torsoW, torsoL, torsoD, flex, tw, side);
        var C = Chain(T, 0, 0, 0, flex, tw, side);
        float hP = c[Head] - flex * 0.75f;
        float neckLen = (float)b.NeckLen, neckW = (float)b.Neck;
        var Nk = Chain(C, 0, 0.58f * torsoL, 0, hP * 0.4f, f.HeadYaw * 0.3f, -side * 0.4f);
        Put(Part.Neck, id, Nk, neckW, neckLen, neckW);
        float top = 0.075f * neckLen;
        var Hd = Chain(Nk, 0, top, 0, hP * 0.6f, f.HeadYaw * 0.7f, -side * 0.4f);
        Hd = ChainT(Hd, 0, 0.02f * torsoL + (neckLen - 1) * 0.08f - top, 0);
        Put(Part.Head, id, Hd);
        if (f.Hair == 1) Put(Part.HairShort, id, Hd, 0.985f, 0.95f, 0.985f);
        else if (f.Hair >= 0) Put(HairOfStyle[f.Hair], id, Hd);

        // Arms: the pose's, over a natural swing with the stride.
        float armLen = (float)b.ArmLen, armW = (float)b.Arm, shoulder = (float)b.Shoulder, aw = c[ArmW];
        for (int sd = 0; sd < 2; sd++)
        {
            float sideSign = sd == 0 ? 1 : -1;
            float swing0 = (sd == 0 ? 1 : -1) * MathF.Sin(phi) * (0.3f + 0.4f * jog) * move + 0.1f * jog;
            float swing = Lerp(swing0, c[Arm + sd], aw);
            float elbow = Lerp(0.25f + 1.0f * jog, c[Arm + 2 + sd], aw);
            float outA = Lerp(0.1f, c[Arm + 4 + sd] + f.Clap, aw);
            float rot = Lerp(0, c[Arm + 6 + sd], aw);
            float raise = MathF.Acos(Clamp(MathF.Cos(swing) * MathF.Cos(outA), -1, 1));
            float elev = Smooth(1.1f, 2.9f, raise);
            var j1 = Chain(C, sideSign * (0.198f * shoulder - 0.014f * elev), 0.5f * torsoL + 0.045f * elev, 0, -swing, 0, sideSign * outA);
            Put(Part.UpperArm, id * 2 + sd, j1, armW, armLen, armW);
            var j2 = Chain(j1, 0, -0.29f * armLen, 0, -elbow, sideSign * rot, 0);
            Put(Part.Forearm, id * 2 + sd, j2, 0.5f + 0.5f * armW, armLen, 0.5f + 0.5f * armW);
            var j3 = Chain(j2, 0, -0.245f * armLen, 0, 0.1f, 0, sideSign * -0.08f);
            float g = f.Gk ? 1.25f : 1;
            Put(Part.Hand, id * 2 + sd, j3, g, g, g);
        }

        float thighW = (float)b.Thigh, calfW = (float)b.Calf;
        for (int sd = 0; sd < 2; sd++)
        {
            float sideSign = sd == 0 ? 1 : -1;
            int fi = id * 2 + sd;
            float hipX = sideSign * 0.092f * (1 + (torsoW - 1) * 0.6f);
            // Walking: the thigh swings, the knee folds through the swing forward.
            float ph = phi + (sd == 0 ? 0 : PI);
            float wHip = MathF.Sin(ph) * (0.32f + 0.3f * jog) * move;
            float cs = MathF.Max(0, MathF.Cos(ph));
            float wKnee = move * (0.06f + (0.75f + 0.7f * jog) * cs * cs) + 0.04f;
            // Seated: the thigh rests on the seat, the shin finds the floor (feet forward or tucked).
            float th = c[sd == 0 ? ThL : ThR], ft = c[sd == 0 ? FtL : FtR];
            float kneeY = hj - THIGH * leg * MathF.Cos(th);
            float a = MathF.Acos(Clamp((kneeY - 0.075f) / (SHIN * leg), -1, 1));
            float sKnee = th - a * ft;
            float hip = Lerp(wHip, th, st), knee = Lerp(wKnee, MathF.Max(0.05f, sKnee), st);
            float outA = c[Lo];
            float yaw = sideSign * c[Yw];
            var j1 = Chain(P, hipX, -0.03f, 0, -hip, yaw, sideSign * outA);
            float soft = knee * 0.22f;
            var j2 = ChainX(j1, 0, 0, 0, soft);
            j2 = ChainX(j2, 0, -THIGH * leg, 0, knee - soft);
            Put(Part.ShortsLeg, fi, j1, thighW, 1, thighW);
            Put(Part.Thigh, fi, j1, thighW, leg, thighW, knee * 0.22f);
            Put(Part.Shin, fi, j2, calfW, leg, calfW);
            // Feet flat (level with the ground), heels down for a leg stretched out.
            float ankle = hip - knee - 0.08f * jog - 0.3f * Smooth(0.6f, 1, a * ft) * st;
            var j3 = ChainX(j2, 0, -SHIN * leg, 0, ankle);
            Put(Part.Boot, fi, j3);
        }
    }
}
