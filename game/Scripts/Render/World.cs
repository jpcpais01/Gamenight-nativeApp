using Godot;

namespace GameNight.Render;

/// <summary>
/// The static scene: evening sky and light, the pitch, goals and nets, advertising boards and
/// simple terraces. Everything here is built once and never changes, a handful of draws.
/// </summary>
public static class World
{
    const float HL = 52.5f, HW = 34f, GoalHalf = 3.66f, GoalH = 2.44f, GoalDepth = 2f, RoofDepth = 1f;

    public static void Build(Node3D root)
    {
        BuildEnvironment(root);
        BuildPitch(root);
        BuildGoal(root, 1);
        BuildGoal(root, -1);
        BuildBoards(root);
        BuildStands(root);
    }

    static void BuildEnvironment(Node3D root)
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.10f, 0.13f, 0.30f),
            SkyHorizonColor = new Color(0.86f, 0.52f, 0.42f),
            GroundBottomColor = new Color(0.05f, 0.05f, 0.07f),
            GroundHorizonColor = new Color(0.30f, 0.22f, 0.25f),
            SunAngleMax = 20,
        };
        var env = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky, RadianceSize = Sky.RadianceSizeEnum.Size32 },
            AmbientLightSource = Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.52f, 0.56f, 0.72f),
            AmbientLightEnergy = 0.55f,
            ReflectedLightSource = Environment.ReflectionSource.Disabled,
            TonemapMode = Environment.ToneMapper.Linear,
            TonemapExposure = 1f,
        };
        root.AddChild(new WorldEnvironment { Environment = env });

        // A low evening sun from behind the near-left corner: lit faces, long shadows up the pitch.
        var sun = new DirectionalLight3D
        {
            LightColor = new Color(1f, 0.86f, 0.68f),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = 90,
            ShadowBias = 0.05f,
            ShadowNormalBias = 0.6f,
            LightSpecular = 0,
        };
        root.AddChild(sun);
        sun.RotationDegrees = new Vector3(-32, -35, 0);
    }

    static void BuildPitch(Node3D root)
    {
        var plane = new PlaneMesh { Size = new Vector2(320, 240) };
        var mi = Geo.Instance(root, plane, Geo.Material("res://Shaders/pitch.gdshader"));
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    static void BuildGoal(Node3D root, int side)
    {
        float x = HL * side;
        float back = x + GoalDepth * side;
        float roof = x + RoofDepth * side;
        const float t = 0.06f;

        var frame = new SurfaceTool();
        frame.Begin(Mesh.PrimitiveType.Triangles);
        // Posts and crossbar (the front of the post sits on the goal line).
        float px0 = side > 0 ? x : x - 2 * t, px1 = side > 0 ? x + 2 * t : x;
        Geo.Box(frame, new Vector3(px0, 0, -GoalHalf - 2 * t), new Vector3(px1, GoalH + t, -GoalHalf));
        Geo.Box(frame, new Vector3(px0, 0, GoalHalf), new Vector3(px1, GoalH + t, GoalHalf + 2 * t));
        Geo.Box(frame, new Vector3(px0, GoalH - t, -GoalHalf), new Vector3(px1, GoalH + t, GoalHalf));
        var frameMat = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.95f, 0.93f), Roughness = 0.6f };
        Geo.Instance(root, frame.Commit(), frameMat, shadows: true);

        var net = new SurfaceTool();
        net.Begin(Mesh.PrimitiveType.Triangles);
        var tl = new Vector3(x, GoalH, -GoalHalf);
        var tr = new Vector3(x, GoalH, GoalHalf);
        var rl = new Vector3(roof, GoalH, -GoalHalf);
        var rr = new Vector3(roof, GoalH, GoalHalf);
        var bl = new Vector3(back, 0, -GoalHalf);
        var br = new Vector3(back, 0, GoalHalf);
        var gl = new Vector3(x, 0, -GoalHalf);
        var gr = new Vector3(x, 0, GoalHalf);
        float w = GoalHalf * 2;
        Geo.Quad(net, tl, tr, rr, rl, w, RoofDepth);
        Geo.Quad(net, rl, rr, br, bl, w, 2.6f);
        Geo.Quad(net, gl, bl, rl, tl, GoalDepth, GoalH);
        Geo.Quad(net, gr, br, rr, tr, GoalDepth, GoalH);
        Geo.Instance(root, net.Commit(), Geo.Material("res://Shaders/net.gdshader"));
    }

    static void BuildBoards(Node3D root)
    {
        const float h = 0.9f, bz = HW + 5f, bx = HL + 5f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Geo.Quad(st, new Vector3(-bx, 0, -bz), new Vector3(bx, 0, -bz), new Vector3(bx, h, -bz), new Vector3(-bx, h, -bz), bx * 2, h);
        Geo.Quad(st, new Vector3(bx, 0, bz), new Vector3(-bx, 0, bz), new Vector3(-bx, h, bz), new Vector3(bx, h, bz), bx * 2, h);
        Geo.Quad(st, new Vector3(bx, 0, -bz), new Vector3(bx, 0, bz), new Vector3(bx, h, bz), new Vector3(bx, h, -bz), bz * 2, h);
        Geo.Quad(st, new Vector3(-bx, 0, bz), new Vector3(-bx, 0, -bz), new Vector3(-bx, h, -bz), new Vector3(-bx, h, bz), bz * 2, h);
        Geo.Instance(root, st.Commit(), Geo.Material("res://Shaders/boards.gdshader"), shadows: true);
    }

    static void BuildStands(Node3D root)
    {
        // Far side, then each end: a raked terrace rising away from the pitch.
        Stand(root, new Vector3(-62, 1.5f, -43), new Vector3(62, 1.5f, -43), new Vector3(62, 22, -75), new Vector3(-62, 22, -75), 0.55f);
        Stand(root, new Vector3(-62, 1.5f, 40), new Vector3(-62, 1.5f, -40), new Vector3(-92, 22, -40), new Vector3(-92, 22, 40), 0.95f);
        Stand(root, new Vector3(62, 1.5f, -40), new Vector3(62, 1.5f, 40), new Vector3(92, 22, 40), new Vector3(92, 22, -40), 0.08f);
    }

    static void Stand(Node3D root, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float homeShare)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float len = (b - a).Length(), slope = (d - a).Length();
        Geo.Quad(st, a, b, c, d, len, slope);
        // The front wall down to the ground.
        var a0 = new Vector3(a.X, 0, a.Z);
        var b0 = new Vector3(b.X, 0, b.Z);
        Geo.Quad(st, a0, b0, b, a, len, 0);
        var mat = Geo.Material("res://Shaders/crowd.gdshader");
        mat.SetShaderParameter("home_share", homeShare);
        mat.SetShaderParameter("slope_len", slope);
        Geo.Instance(root, st.Commit(), mat);
    }
}
