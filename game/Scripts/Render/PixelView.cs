using System;
using Godot;

namespace GameNight.Render;

/// <summary>
/// The pixel-art presentation. The 3D world renders at art resolution (about 270 pixels tall)
/// into a SubViewport, the post pass on a full-screen quad inside it. With Smooth on, each art
/// pixel is four samples (the 3D renders at twice the size and is averaged down), so thin
/// things (lines, nets, far fans) cover a pixel partly instead of popping in and out of it
/// as the camera moves; the post pass still works in whole art pixels. The result is shown with nearest-neighbour filtering at a
/// whole-number scale, so every art pixel is the same size on screen, and scrolled by the
/// camera's sub-pixel remainder so motion glides while the pixel grid stays put.
/// </summary>
public sealed partial class PixelView : Control
{
    /// <summary>Wanted art height in pixels; the real one divides the screen evenly.</summary>
    public int TargetHeight
    {
        get => _target;
        set { _target = value; _lastScreen = default; }
    }
    int _target = 270;

    public readonly SubViewport Viewport;
    public readonly Node3D WorldRoot;
    public readonly Camera3D Camera;
    public int ArtHeight { get; private set; } = 270;
    public int PixelScale { get; private set; } = 1;

    readonly TextureRect _display;
    readonly ShaderMaterial _post;
    bool _smooth = true;

    /// <summary>Four samples per art pixel (steady edges in motion), or one.</summary>
    public bool Smooth
    {
        get => _smooth;
        set
        {
            _smooth = value;
            Viewport.Scaling3DScale = value ? 2 : 1;
            RenderingServer.GlobalShaderParameterSet("gn_ss", value ? 2f : 1f);
        }
    }
    Vector2I _lastScreen;

    public PixelView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        Viewport = new SubViewport
        {
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Godot.Viewport.Msaa.Disabled,
            ScreenSpaceAA = Godot.Viewport.ScreenSpaceAAEnum.Disabled,
            Scaling3DMode = Godot.Viewport.Scaling3DModeEnum.Bilinear,
            Scaling3DScale = 2f,
            PositionalShadowAtlasSize = 0,
            HandleInputLocally = false,
            GuiDisableInput = true,
            OwnWorld3D = true,
        };
        AddChild(Viewport);
        WorldRoot = new Node3D();
        Viewport.AddChild(WorldRoot);
        Camera = new Camera3D { Current = true };
        WorldRoot.AddChild(Camera);

        // The post pass: a full-screen quad drawn last (outline, grade, dither).
        _post = Geo.Material("res://Shaders/post.gdshader");
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(2, 2) },
            MaterialOverride = _post,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384,
        };
        Camera.AddChild(quad);
        quad.Position = new Vector3(0, 0, -2);

        _display = new TextureRect
        {
            Texture = Viewport.GetTexture(),
            TextureFilter = TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_display);
    }

    /// <summary>Screen size in real device pixels.</summary>
    public static Vector2I ScreenPixels() => DisplayServer.WindowGetSize();

    /// <summary>The pixel sizes this screen can have, fine to coarse: the art height at each whole
    /// number of device pixels per art pixel (between lo and hi rows).</summary>
    public static int[] Levels(int lo, int hi)
    {
        int h = ScreenPixels().Y;
        var list = new System.Collections.Generic.List<int>();
        for (int n = 1; n <= 64; n++)
        {
            int art = (int)MathF.Ceiling(h / (float)n);
            if (art >= lo && art <= hi) list.Add(art);
        }
        return list.ToArray();
    }

    /// <summary>Where a point of the world is on screen, in this control's units (null when behind the camera).</summary>
    public Vector2? WorldToUnits(Vector3 p)
    {
        if (Camera.IsPositionBehind(p)) return null;
        float toUnits = Size.Y / Math.Max(1, _lastScreen.Y);
        return _display.Position + Camera.UnprojectPosition(p) * PixelScale * toUnits;
    }

    /// <summary>Size the art target for the screen: a whole number of device pixels per art pixel.</summary>
    void Resize(Vector2I screen)
    {
        _lastScreen = screen;
        PixelScale = Math.Max(1, (int)MathF.Round(screen.Y / (float)_target));
        ArtHeight = (int)MathF.Ceiling(screen.Y / (float)PixelScale);
        int artW = (int)MathF.Ceiling(screen.X / (float)PixelScale);
        // One spare pixel all round, for the sub-pixel scroll.
        Viewport.Size = new Vector2I(artW + 2, ArtHeight + 2);
        _post.SetShaderParameter("art_size", new Vector2(Viewport.Size.X, Viewport.Size.Y));
    }

    public float Aspect => Viewport.Size.X / (float)Viewport.Size.Y;

    /// <summary>Per frame, before the camera moves: keep the art target sized to the screen.
    /// Returns true when it changed.</summary>
    public bool Fit()
    {
        var screen = ScreenPixels();
        if (screen == _lastScreen) return false;
        Resize(screen);
        return true;
    }

    /// <summary>Per frame: place the picture, scrolled by the camera's sub-pixel remainder.</summary>
    public void Present(float subX, float subY)
    {
        var screen = _lastScreen;
        // Device pixels to this control's (stretched) units.
        float toUnits = Size.Y / Math.Max(1, screen.Y);
        float k = PixelScale * toUnits;
        _display.Size = new Vector2(Viewport.Size.X, Viewport.Size.Y) * k;
        _display.Position = new Vector2(-1 - subX, -1 + subY) * k;
    }
}
