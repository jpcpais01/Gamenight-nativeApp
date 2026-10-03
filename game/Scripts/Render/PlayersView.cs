using System;
using Godot;
using GameNight.Bridge;

namespace GameNight.Render;

/// <summary>
/// All 22 players in one instanced draw, plus the ball and the marker under your player.
/// Per frame the CPU writes one transform and four floats per player; the vertex shader
/// does the running animation.
/// </summary>
public sealed class PlayersView
{
    const float ModelHeight = 1.8f;

    // Colour slots baked into the mesh (read by player.gdshader).
    const int Shirt = 0, Trim = 1, Shorts = 2, Socks = 3, Skin = 4, Hair = 5, Boots = 6;
    // Body parts: 0 torso/head, 1 left leg, 2 right leg, 3 left arm, 4 right arm.


    readonly MultiMesh _mm;
    readonly MeshInstance3D _ball;
    readonly MeshInstance3D _ring;
    readonly float[] _phase = new float[MatchFrame.MaxPlayers];

    public PlayersView(Node3D root)
    {
        var mat = Geo.Material("res://Shaders/player.gdshader");
        // Kits from the PWA's two teams (Rossoneri Athletic, Atlantic Rovers).
        mat.SetShaderParameter("shirt", new[] { Hex(0xc8393b), Hex(0xf1ebdc), Hex(0xe9c24a), Hex(0x2ba59a) });
        mat.SetShaderParameter("trim", new[] { Hex(0x8f1f24), Hex(0x23345e), Hex(0x2a2a2a), Hex(0x163a36) });
        mat.SetShaderParameter("shorts", new[] { Hex(0xf3ede0), Hex(0x23345e), Hex(0x2a2a2a), Hex(0x163a36) });
        mat.SetShaderParameter("socks", new[] { Hex(0xc8393b), Hex(0xf1ebdc), Hex(0xe9c24a), Hex(0x2ba59a) });
        mat.SetShaderParameter("skins", new[] { Hex(0xf0c29e), Hex(0xcc946b), Hex(0x8c5e40), Hex(0x573826) });

        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = BuildBody(),
        };
        _mm.InstanceCount = MatchFrame.MaxPlayers;
        var mmi = new MultiMeshInstance3D
        {
            Multimesh = _mm,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            // The squad spans the pitch; never cull it.
            ExtraCullMargin = 200,
        };
        root.AddChild(mmi);

        var ballMesh = new SphereMesh { Radius = 0.11f, Height = 0.22f, RadialSegments = 8, Rings = 4 };
        _ball = Geo.Instance(root, ballMesh, new StandardMaterial3D { AlbedoColor = new Color(0.97f, 0.97f, 0.95f), Roughness = 0.5f }, shadows: true);

        var ringMesh = new QuadMesh { Size = new Vector2(1.5f, 1.5f), Orientation = PlaneMesh.OrientationEnum.Y };
        _ring = Geo.Instance(root, ringMesh, Geo.Material("res://Shaders/ring.gdshader"));
    }

    /// <summary>An sRGB hex colour as a linear vec3 (the shader lights in linear).</summary>
    static Vector3 Hex(int rgb)
    {
        var c = new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f).SrgbToLinear();
        return new Vector3(c.R, c.G, c.B);
    }

    public void Update(MatchFrame a, MatchFrame b, float alpha, float dt)
    {
        int n = b.Count;
        _mm.VisibleInstanceCount = n;
        for (int i = 0; i < n; i++)
        {
            float x = Mathf.Lerp(a.X[i], b.X[i], alpha);
            float y = Mathf.Lerp(a.Y[i], b.Y[i], alpha);
            float z = Mathf.Lerp(a.Z[i], b.Z[i], alpha);
            float face = Mathf.LerpAngle(a.Facing[i], b.Facing[i], alpha);
            float speed = b.Speed[i];
            // One stride cycle per ~2.2 m.
            _phase[i] = (_phase[i] + speed / 2.2f * MathF.Tau * dt) % MathF.Tau;
            float run = Math.Clamp(speed / 4f, 0, 1);
            float s = b.Height[i] / ModelHeight;
            var basis = new Basis(Vector3.Up, -face).Scaled(new Vector3(s, s, s));
            _mm.SetInstanceTransform(i, new Transform3D(basis, new Vector3(x, y, z)));
            int kit = b.Team[i] + (b.Keeper[i] ? 2 : 0);
            _mm.SetInstanceCustomData(i, new Color(_phase[i], run, kit, b.Skin[i] & 3));
        }

        _ball.Position = new Vector3(
            Mathf.Lerp(a.BallX, b.BallX, alpha),
            Mathf.Lerp(a.BallY, b.BallY, alpha),
            Mathf.Lerp(a.BallZ, b.BallZ, alpha));

        int c = b.Controlled;
        _ring.Visible = c >= 0 && b.Phase != MatchPhase.Goal;
        if (c >= 0)
            _ring.Position = new Vector3(Mathf.Lerp(a.X[c], b.X[c], alpha), 0.03f, Mathf.Lerp(a.Z[c], b.Z[c], alpha));
    }

    /// <summary>A low-poly footballer, 1.8 m tall, facing +X. Built once.</summary>
    static ArrayMesh BuildBody()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        void Part(int slot, int part, float pivot, Vector3 mn, Vector3 mx)
        {
            st.SetColor(new Color(slot / 255f, 0, 0));
            st.SetUV2(new Vector2(part, pivot));
            Geo.Box(st, mn, mx);
        }

        const float hip = 0.92f, shoulder = 1.40f;
        foreach (var (part, z) in new[] { (1, -0.1f), (2, 0.1f) })
        {
            Part(Boots, part, hip, new(-0.07f, 0, z - 0.075f), new(0.13f, 0.08f, z + 0.075f));
            Part(Socks, part, hip, new(-0.075f, 0.08f, z - 0.075f), new(0.075f, 0.45f, z + 0.075f));
            Part(Skin, part, hip, new(-0.07f, 0.45f, z - 0.07f), new(0.07f, 0.6f, z + 0.07f));
            Part(Shorts, part, hip, new(-0.095f, 0.6f, z - 0.095f), new(0.095f, hip + 0.04f, z + 0.095f));
        }
        Part(Shirt, 0, 0, new(-0.12f, hip, -0.21f), new(0.12f, 1.46f, 0.21f));
        Part(Trim, 0, 0, new(-0.125f, 1.40f, -0.12f), new(0.125f, 1.47f, 0.12f));
        foreach (var (part, z) in new[] { (3, -0.27f), (4, 0.27f) })
        {
            Part(Shirt, part, shoulder, new(-0.06f, 1.18f, z - 0.06f), new(0.06f, 1.45f, z + 0.06f));
            Part(Skin, part, shoulder, new(-0.05f, 0.9f, z - 0.05f), new(0.05f, 1.18f, z + 0.05f));
        }
        Part(Skin, 0, 0, new(-0.05f, 1.46f, -0.05f), new(0.05f, 1.52f, 0.05f));
        Part(Skin, 0, 0, new(-0.1f, 1.52f, -0.095f), new(0.1f, 1.74f, 0.095f));
        Part(Hair, 0, 0, new(-0.115f, 1.68f, -0.105f), new(0.09f, 1.79f, 0.105f));
        return st.Commit();
    }
}
