using System;
using Godot;
using GameNight.Sim;

namespace GameNight.Render;

/// <summary>
/// The goals (the PWA's src/render/goals.ts): round white posts and crossbar, thin back
/// supports, and a real net: one folded sheet over the roof and down the back, and the two side
/// panels, drawn as a fine mesh of cord that bulges out where the ball hits it and ripples.
/// </summary>
public sealed class Goals
{
    const float HL = (float)Pitch.HalfL, HW = (float)Pitch.GoalHalfWidth, H = (float)Pitch.GoalHeight;
    const float Depth = 2f, Roof = 1f, PostR = 0.06f;

    readonly ShaderMaterial[] _nets = new ShaderMaterial[2];

    public Goals(Node3D root)
    {
        // Unshaded: a post is a pixel or two wide at art res, and shading turns it blue in the shade.
        var postMat = new StandardMaterial3D { AlbedoColor = new Color(0xf4f3eeff), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var supMat = new StandardMaterial3D { AlbedoColor = new Color(0xb8b9b5ff), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var post = new CylinderMesh { TopRadius = PostR, BottomRadius = PostR, Height = H + PostR, RadialSegments = 10, Rings = 1 };
        var bar = new CylinderMesh { TopRadius = PostR, BottomRadius = PostR, Height = HW * 2 + PostR * 2, RadialSegments = 10, Rings = 1 };
        float supLen = MathF.Sqrt((Depth - Roof) * (Depth - Roof) + H * H);
        var sup = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = supLen, RadialSegments = 6, Rings = 1 };
        var net = NetMesh();
        var shader = GD.Load<Shader>("res://Shaders/net.gdshader");

        for (int g = 0; g < 2; g++)
        {
            int side = g == 0 ? 1 : -1;
            // Built for the goal at +x; the one at -x is turned round.
            var goal = new Node3D { Position = new Vector3(side * HL, 0, 0), Rotation = new Vector3(0, side > 0 ? 0 : MathF.PI, 0) };
            root.AddChild(goal);
            foreach (int s in new[] { -1, 1 })
            {
                Add(goal, post, postMat, new Vector3(0, (H + PostR) / 2, s * HW), Vector3.Zero, true);
                Add(goal, sup, supMat, new Vector3((Roof + Depth) / 2, H / 2, s * HW), new Vector3(0, 0, MathF.Atan2(Depth - Roof, H)), false);
            }
            Add(goal, bar, postMat, new Vector3(0, H, 0), new Vector3(MathF.PI / 2, 0, 0), true);
            var mat = new ShaderMaterial { Shader = shader };
            mat.SetShaderParameter("hit", new Vector4(0, 0, 0, -10));
            _nets[g] = mat;
            var mi = new MeshInstance3D { Mesh = net, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            goal.AddChild(mi);
        }
    }

    static void Add(Node3D parent, Mesh mesh, Material mat, Vector3 pos, Vector3 rot, bool shadow)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = mat,
            Position = pos,
            Rotation = rot,
            CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>The ball hit the net at (x, y, z) in the world, this hard (m/s), at this time.</summary>
    public void Impact(float x, float y, float z, float strength, double time)
    {
        int g = x > 0 ? 0 : 1;
        // Into the goal's own frame (the -x goal is turned round).
        float lx = x > 0 ? x - HL : -(x + HL);
        float lz = x > 0 ? z : -z;
        _nets[g].SetShaderParameter("hit", new Vector4(lx, y, lz, (float)time));
        _nets[g].SetShaderParameter("strength", MathF.Min(1, strength / 16));
    }

    public void Update(double time)
    {
        foreach (var n in _nets) n.SetShaderParameter("time", (float)time);
    }

    /// <summary>The net for the goal at +x, in metres: the roof and back as one folded sheet, and
    /// the two side panels. COLOR carries the way each panel bulges out.</summary>
    static ArrayMesh NetMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float backLen = MathF.Sqrt((Depth - Roof) * (Depth - Roof) + H * H);
        float total = Roof + backLen;
        // v: 0 at the crossbar, along the roof, then down the back to the ground.
        (float d, float y) Profile(float v)
        {
            float s = v * total;
            if (s <= Roof) return (s, H);
            float t = (s - Roof) / backLen;
            return (Roof + (Depth - Roof) * t, H * (1 - t));
        }
        int vert = 0;
        void Surface(int nu, int nv, Func<float, float, Vector3> f, Vector3 outDir, float su, float sv)
        {
            int baseI = vert;
            var col = new Color(outDir.X * 0.5f + 0.5f, outDir.Y * 0.5f + 0.5f, outDir.Z * 0.5f + 0.5f);
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                {
                    float u = i / (float)nu, v = j / (float)nv;
                    st.SetColor(col);
                    st.SetUV(new Vector2(u * su, v * sv));
                    st.AddVertex(f(u, v));
                    vert++;
                }
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = baseI + j * (nu + 1) + i;
                    st.AddIndex(a);
                    st.AddIndex(a + nu + 2);
                    st.AddIndex(a + 1);
                    st.AddIndex(a);
                    st.AddIndex(a + nu + 1);
                    st.AddIndex(a + nu + 2);
                }
        }
        Surface(20, 14, (u, v) =>
        {
            var (d, y) = Profile(v);
            return new Vector3(d, y, -HW + u * 2 * HW);
        }, new Vector3(1, 0.3f, 0).Normalized(), HW * 2, total);
        foreach (int s in new[] { -1, 1 })
            Surface(8, 8, (u, v) =>
            {
                var (d, y) = Profile(u);
                return new Vector3(d, y * (1 - v), s * HW);
            }, new Vector3(0.2f, 0, s).Normalized(), total, H);
        return st.Commit();
    }
}
