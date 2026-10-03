using Godot;

namespace GameNight.Render;

/// <summary>Small mesh-building helpers on top of SurfaceTool.</summary>
public static class Geo
{
    /// <summary>A quad a-b-c-d (counter-clockwise seen from the front) with UVs in metres.</summary>
    public static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uw, float uh)
    {
        var n = (b - a).Cross(d - a).Normalized();
        st.SetNormal(n);
        // Godot's front faces wind clockwise.
        st.SetUV(new Vector2(0, uh)); st.AddVertex(a);
        st.SetUV(new Vector2(uw, 0)); st.AddVertex(c);
        st.SetUV(new Vector2(uw, uh)); st.AddVertex(b);
        st.SetUV(new Vector2(0, uh)); st.AddVertex(a);
        st.SetUV(new Vector2(0, 0)); st.AddVertex(d);
        st.SetUV(new Vector2(uw, 0)); st.AddVertex(c);
    }

    /// <summary>An axis-aligned box between min and max (all six faces, outward normals).</summary>
    public static void Box(SurfaceTool st, Vector3 mn, Vector3 mx)
    {
        Vector3 p000 = new(mn.X, mn.Y, mn.Z), p100 = new(mx.X, mn.Y, mn.Z), p010 = new(mn.X, mx.Y, mn.Z), p110 = new(mx.X, mx.Y, mn.Z);
        Vector3 p001 = new(mn.X, mn.Y, mx.Z), p101 = new(mx.X, mn.Y, mx.Z), p011 = new(mn.X, mx.Y, mx.Z), p111 = new(mx.X, mx.Y, mx.Z);
        float sx = mx.X - mn.X, sy = mx.Y - mn.Y, sz = mx.Z - mn.Z;
        Quad(st, p001, p101, p111, p011, sx, sy); // +Z
        Quad(st, p100, p000, p010, p110, sx, sy); // -Z
        Quad(st, p101, p100, p110, p111, sz, sy); // +X
        Quad(st, p000, p001, p011, p010, sz, sy); // -X
        Quad(st, p011, p111, p110, p010, sx, sz); // +Y
        Quad(st, p000, p100, p101, p001, sx, sz); // -Y
    }

    public static ShaderMaterial Material(string shaderPath)
    {
        return new ShaderMaterial { Shader = GD.Load<Shader>(shaderPath) };
    }

    public static MeshInstance3D Instance(Node parent, Mesh mesh, Material mat, bool shadows = false)
    {
        var mi = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = mat,
            CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(mi);
        return mi;
    }
}
