using Godot;

namespace GameNight.Render;

/// <summary>
/// The pitch (the goals are Goals). Built once, a handful of draws. (The sky, light and everything
/// round the pitch is the ground: game/World.)
/// </summary>
public static class World
{
    public static void Build(Node3D root)
    {
        BuildPitch(root);
    }

    static void BuildPitch(Node3D root)
    {
        var plane = new PlaneMesh { Size = new Vector2(320, 240) };
        var mi = Geo.Instance(root, plane, Geo.Material("res://Shaders/pitch.gdshader"));
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }
}
