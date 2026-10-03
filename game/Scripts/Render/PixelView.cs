using System;
using Godot;

namespace GameNight.Render;

/// <summary>
/// The pixel-art presentation. The 3D world renders once, at art resolution (about 270 pixels
/// tall), into a SubViewport: one sample per pixel, no supersampling, the post pass on a
/// full-screen quad inside it. The result is shown with nearest-neighbour filtering at a
/// whole-number scale, so every art pixel is the same size on screen, and scrolled by the
/// camera's sub-pixel remainder so motion glides while the pixel grid stays put.
/// </summary>
public sealed partial class PixelView : Control
{
    /// <summary>Wanted art height in pixels; the real one divides the screen evenly.</summary>
    public int TargetHeight = 270;

    public readonly SubViewport Viewport;
    public readonly Node3D WorldRoot;
    public readonly Camera3D Camera;
    public int ArtHeight { get; private set; } = 270;
    public int PixelScale { get; private set; } = 1;

    readonly TextureRect _display;
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
            Scaling3DScale = 1f,
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
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(2, 2) },
            MaterialOverride = Geo.Material("res://Shaders/post.gdshader"),
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
    static Vector2I ScreenPixels() => DisplayServer.WindowGetSize();

    /// <summary>Size the art target for the screen: a whole number of device pixels per art pixel.</summary>
    void Resize(Vector2I screen)
    {
        _lastScreen = screen;
        PixelScale = Math.Max(1, (int)MathF.Round(screen.Y / (float)TargetHeight));
        ArtHeight = (int)MathF.Ceiling(screen.Y / (float)PixelScale);
        int artW = (int)MathF.Ceiling(screen.X / (float)PixelScale);
        // One spare pixel all round, for the sub-pixel scroll.
        Viewport.Size = new Vector2I(artW + 2, ArtHeight + 2);
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
