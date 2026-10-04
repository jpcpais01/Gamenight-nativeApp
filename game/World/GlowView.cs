using Godot;

namespace GameNight.Grounds;

/// <summary>The floodlight banks' glows: one camera-facing card per bank, all in one draw.
/// No blending (the post pass reads depth): a bright core and a dithered halo.</summary>
public sealed class GlowView
{
    public GlowView(Node3D root, Vector3[] spots)
    {
        var v = new Vector3[spots.Length * 4];
        var uv = new Vector2[spots.Length * 4];
        var idx = new int[spots.Length * 6];
        for (int i = 0; i < spots.Length; i++)
        {
            for (int c = 0; c < 4; c++)
            {
                v[i * 4 + c] = spots[i];
                uv[i * 4 + c] = new Vector2((c & 1) * 2 - 1, (c >> 1) * 2 - 1);
            }
            idx[i * 6] = i * 4; idx[i * 6 + 1] = i * 4 + 2; idx[i * 6 + 2] = i * 4 + 1;
            idx[i * 6 + 3] = i * 4 + 1; idx[i * 6 + 4] = i * 4 + 2; idx[i * 6 + 5] = i * 4 + 3;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        root.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/glow.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 20,
        });
    }
}
