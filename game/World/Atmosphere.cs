using Godot;

namespace GameNight.Grounds;

/// <summary>
/// Time of day across the match, as in the PWA's evening: kick-off in warm late sun, the light
/// going gold then blue through the second half, full time at night with the floodlights
/// carrying the pitch. Drives the sun (the one live light: it casts the players' shadows), the
/// sky, the haze and the shared world shader globals. Only uniforms change per frame.
/// </summary>
public sealed class Atmosphere
{
    /// <summary>Direction the sunlight travels: from behind the near-left corner, low, across
    /// the pitch toward the far side. Fixed, so the stand shadows can be baked once.</summary>
    public static readonly Vector3 SunDir = Dir(36f, -0.5f);

    static Vector3 Dir(float elevDeg, float az)
    {
        float e = Mathf.DegToRad(elevDeg);
        float h = Mathf.Cos(e);
        return new Vector3(Mathf.Sin(-az) * h, -Mathf.Sin(e), -Mathf.Cos(az) * h).Normalized();
    }

    readonly DirectionalLight3D _sun;
    readonly Environment _env;
    readonly ShaderMaterial _sky;
    float _last = -1;
    /// <summary>Extra light the ground adds for its floodlights (0 = none, e.g. training).</summary>
    public float FloodScale = 1f;

    // Key colours: kick-off (a), dusk (b), night (c). sRGB.
    static readonly Color SunA = C(0xfff0d4), SunB = C(0xffb47a), SunC = C(0xc8ccff);
    static readonly Color TopA = C(0x6fa6e6), TopB = C(0x2c3a78), TopC = C(0x0b1030);
    static readonly Color HorA = C(0xfbe6c0), HorB = C(0xf09a6a), HorC = C(0x3a3456);
    static readonly Color FogA = C(0xe6dcc4), FogB = C(0x8a82a8), FogC = C(0x2b2c46);
    static readonly Color AmbA = C(0x8fa6d8), AmbB = C(0x7d86c2), AmbC = C(0x3c4474);
    static readonly Color GndA = C(0x5f7044), GndB = C(0x4a5040), GndC = C(0x22281e);

    static Color C(uint hex) => MeshData.Srgb(hex);

    public Atmosphere(Node3D root)
    {
        _sky = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/sky.gdshader") };
        _env = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky, RadianceSize = Sky.RadianceSizeEnum.Size32, ProcessMode = Sky.ProcessModeEnum.Quality },
            AmbientLightSource = Environment.AmbientSource.Color,
            ReflectedLightSource = Environment.ReflectionSource.Disabled,
            TonemapMode = Environment.ToneMapper.Linear,
            TonemapExposure = 1f,
        };
        root.AddChild(new WorldEnvironment { Environment = _env });

        _sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = 90,
            ShadowBias = 0.05f,
            ShadowNormalBias = 0.6f,
            LightSpecular = 0,
        };
        root.AddChild(_sun);
        _sun.Basis = Basis.LookingAt(SunDir, Vector3.Up);
        RenderingServer.GlobalShaderParameterSet("gn_sun_dir", SunDir);
        Set(0);
    }

    /// <summary>Match progress 0 (kick-off) to 1 (full time). Cheap when it hasn't moved.</summary>
    public void Set(float t)
    {
        t = Mathf.Clamp(t, 0, 1);
        if (Mathf.Abs(t - _last) < 0.002f) return;
        _last = t;
        float dusk = Mathf.SmoothStep(0.25f, 0.7f, t);
        float night = Mathf.SmoothStep(0.6f, 1f, t);
        Color K(Color a, Color b, Color c) => a.Lerp(b, dusk).Lerp(c, night);

        var sun = K(SunA, SunB, SunC);
        float energy = Mathf.Lerp(Mathf.Lerp(1.25f, 0.95f, dusk), 0.32f, night);
        _sun.LightColor = sun.LinearToSrgb();
        _sun.LightEnergy = energy;
        var amb = K(AmbA, AmbB, AmbC) * Mathf.Lerp(0.62f, 0.5f, night);
        var gnd = K(GndA, GndB, GndC) * 0.55f;
        _env.AmbientLightColor = amb.LinearToSrgb();
        _env.AmbientLightEnergy = 1f;

        // Floodlights: faintly on from the start, carrying the light by full time.
        float flood = Mathf.Lerp(0.1f, 1f, Mathf.SmoothStep(0.15f, 0.95f, t)) * FloodScale;
        var fog = K(FogA, FogB, FogC);

        RenderingServer.GlobalShaderParameterSet("gn_sun", new Vector3(sun.R, sun.G, sun.B) * energy);
        RenderingServer.GlobalShaderParameterSet("gn_amb_sky", new Vector3(amb.R, amb.G, amb.B));
        RenderingServer.GlobalShaderParameterSet("gn_amb_ground", new Vector3(gnd.R, gnd.G, gnd.B));
        RenderingServer.GlobalShaderParameterSet("gn_flood", flood);
        RenderingServer.GlobalShaderParameterSet("gn_fog", new Vector3(fog.R, fog.G, fog.B));
        RenderingServer.GlobalShaderParameterSet("gn_night", night);

        var top = K(TopA, TopB, TopC);
        var hor = K(HorA, HorB, HorC);
        _sky.SetShaderParameter("top", new Vector3(top.R, top.G, top.B));
        _sky.SetShaderParameter("horizon", new Vector3(hor.R, hor.G, hor.B));
        _sky.SetShaderParameter("sun_color", new Vector3(sun.R, sun.G, sun.B) * (1 - night));
        _sky.SetShaderParameter("sun_dir", -SunDir);
        _sky.SetShaderParameter("stars", night);
        _sky.SetShaderParameter("clouds", Mathf.Lerp(0.55f, 0.15f, dusk));
    }
}
