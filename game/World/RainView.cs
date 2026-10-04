using System;
using Godot;

namespace GameNight.Grounds;

/// <summary>
/// Rain on a floodlit night (the PWA's rain.ts), all on the GPU: thousands of streaks, each
/// drop drawn as a line along its fall (the eye's motion blur), slanted by the wind, falling
/// through a box that sits in front of the camera while the drops stay anchored in the world;
/// and splashes, little crowns flicking up off the grass at a fresh spot each time. Lines are
/// a pixel wide on screen whatever the distance, so they read at the art resolution. Dithered
/// (no blending), no depth writes, drawn after the post pass. One static mesh; the CPU does nothing per frame.
/// </summary>
public sealed class RainView
{
    const int Drops = 5200, Splashes = 1400;
    readonly MeshInstance3D _mi;

    public bool Visible { get => _mi.Visible; set => _mi.Visible = value; }

    public RainView(Node3D root)
    {
        int n = Drops + Splashes;
        var v = new Vector3[n * 4];
        var col = new Color[n * 4];
        var uv = new Vector2[n * 4];
        var idx = new int[n * 6];
        var rng = new Random(23);
        for (int i = 0; i < n; i++)
        {
            var seed = new Color((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
            float kind = i < Drops ? 0 : 1;
            for (int c = 0; c < 4; c++)
            {
                v[i * 4 + c] = Vector3.Zero;
                col[i * 4 + c] = seed;
                // x: side of the line (-1/+1), y: head 0 / tail 1; kind in the vertex's z.
                uv[i * 4 + c] = new Vector2((c & 1) * 2 - 1, c >> 1);
                v[i * 4 + c].Z = kind;
            }
            idx[i * 6] = i * 4; idx[i * 6 + 1] = i * 4 + 2; idx[i * 6 + 2] = i * 4 + 1;
            idx[i * 6 + 3] = i * 4 + 1; idx[i * 6 + 4] = i * 4 + 2; idx[i * 6 + 5] = i * 4 + 3;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v;
        arrays[(int)Mesh.ArrayType.Color] = col;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _mi = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/rain.gdshader"), RenderPriority = 10 },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
            // The drops are placed on the GPU around the camera: never cull them.
            CustomAabb = new Aabb(new Vector3(-1000, -10, -1000), new Vector3(2000, 100, 2000)),
        };
        root.AddChild(_mi);
    }
}
