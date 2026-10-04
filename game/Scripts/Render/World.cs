using Godot;

namespace GameNight.Render;

/// <summary>
/// The pitch, goals and nets. Built once, a handful of draws. (The sky, light and everything
/// round the pitch is the ground: game/World.)
/// </summary>
public static class World
{
    const float HL = 52.5f, HW = 34f, GoalHalf = 3.66f, GoalH = 2.44f, GoalDepth = 2f, RoofDepth = 1f;

    public static void Build(Node3D root)
    {
        BuildPitch(root);
        BuildGoal(root, 1);
        BuildGoal(root, -1);
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
}
