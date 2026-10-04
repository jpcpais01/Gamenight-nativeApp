using Godot;
using GameNight.Sim;

namespace GameNight.Render;

/// <summary>
/// Corner, goal-kick and cross aiming, FIFA-style (the PWA's cornerAim.ts): a gold ring where
/// the delivery will come down and a dotted arc of its real flight, which the engine solves
/// (MatchSnapshot.HasArc).
/// </summary>
public sealed class DeliveryView
{
    readonly MeshInstance3D _ring;
    readonly MultiMeshInstance3D _dots;
    readonly MultiMesh _mm;

    public DeliveryView(Node3D root)
    {
        _ring = Geo.Instance(root, new QuadMesh { Size = new Vector2(3, 3), Orientation = PlaneMesh.OrientationEnum.Y }, Geo.Material("res://Shaders/delivery.gdshader"));
        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = new Vector2(0.24f, 0.24f) },
            InstanceCount = DeliveryPreview.Dots,
        };
        _dots = new MultiMeshInstance3D
        {
            Multimesh = _mm,
            MaterialOverride = Geo.Material("res://Shaders/arc.gdshader"),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 100,
        };
        root.AddChild(_dots);
        _ring.Visible = _dots.Visible = false;
    }

    public void Update(MatchSnapshot b)
    {
        bool on = b.HasArc;
        _ring.Visible = _dots.Visible = on;
        if (!on) return;
        _ring.Position = new Vector3(b.ArcRingX, 0.04f, b.ArcRingZ);
        int n = b.ArcCount;
        for (int i = 0; i < DeliveryPreview.Dots; i++)
        {
            _mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3(b.ArcX[i], b.ArcY[i], b.ArcZ[i])));
            // Faint at the foot, strong where it comes down.
            float a = i < n ? 0.25f + 0.7f * i / (DeliveryPreview.Dots - 1f) : 0;
            _mm.SetInstanceCustomData(i, new Color(a, 0, 0, 0));
        }
    }
}
