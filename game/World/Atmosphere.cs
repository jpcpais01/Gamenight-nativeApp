using Random = System.Random;
using Godot;

namespace GameNight.Grounds;

/// <summary>The match weathers (the PWA's): evening into floodlights, a sunny day, a rainy night.</summary>
public enum Weather { Evening, Sunny, Rain }

/// <summary>
/// The light and weather of a match. Drives the sun (the one live light: it casts the players'
/// shadows), the sky, the haze and the shared world shader globals; only uniforms change per
/// frame. Three weathers, as in the PWA, and better:
/// - Evening: kick-off in warm late sun, gold then blue through the second half, full time at
///   night with the floodlights carrying the pitch; a few cloud shadows early on.
/// - Sunny day: a high bright sun, crisp deep stand shadows, a deep blue sky with fair-weather
///   clouds whose shadows drift across the pitch, almost no haze, floodlights off.
/// - Rainy night: a black overcast lit from underneath by the floodlights, a cool-white key
///   from high over the main stand, rain haze closing in, everything wet, rain streaking
///   through the lights (RainView), and lightning now and then.
/// The weather lives across matches (user://weather.cfg) and can change mid-match: the stand
/// shadows stay baked from the one sun direction and the weather says how much they count.
/// </summary>
public sealed class Atmosphere
{
    /// <summary>Direction the sunlight travels: from behind the near-left corner, low, across
    /// the pitch toward the far side. Fixed, so the stand shadows can be baked once.</summary>
    public static readonly Vector3 SunDir = Dir(36f, -0.5f);
    /// <summary>The rainy night's key: the floodlights high over the main stand roof.</summary>
    static readonly Vector3 FloodKeyDir = Dir(58f, -0.35f);

    static Vector3 Dir(float elevDeg, float az)
    {
        float e = Mathf.DegToRad(elevDeg);
        float h = Mathf.Cos(e);
        return new Vector3(Mathf.Sin(-az) * h, -Mathf.Sin(e), -Mathf.Cos(az) * h).Normalized();
    }

    // ---- the weather, kept between matches

    const string ConfigPath = "user://weather.cfg";
    static Weather? _saved;

    /// <summary>The weather for matches (saved on this device).</summary>
    public static Weather Saved
    {
        get
        {
            if (_saved == null)
            {
                var cfg = new ConfigFile();
                _saved = cfg.Load(ConfigPath) == Error.Ok ? (Weather)Mathf.Clamp((int)cfg.GetValue("match", "weather", 0), 0, 2) : Weather.Evening;
            }
            return _saved.Value;
        }
        set
        {
            _saved = value;
            var cfg = new ConfigFile();
            cfg.SetValue("match", "weather", (int)value);
            cfg.Save(ConfigPath);
        }
    }

    public static readonly string[] Names = { "Evening", "Sunny day", "Rainy night" };

    readonly DirectionalLight3D _sun;
    readonly Environment _env;
    readonly ShaderMaterial _sky;
    readonly RainView _rain;
    float _last = -1;
    /// <summary>Extra light the ground adds for its floodlights (0 = none, e.g. training).</summary>
    public float FloodScale = 1f;
    /// <summary>The ground's own haze (fog start/end, strength), for the evening.</summary>
    public Vector2 FogRange = new(110, 340);
    public float Haze = 1f;
    public Weather Weather { get; private set; }

    // Evening key colours: kick-off (a), dusk (b), night (c). sRGB.
    static readonly Color SunA = C(0xfff0d4), SunB = C(0xffb47a), SunC = C(0xc8ccff);
    static readonly Color TopA = C(0x6fa6e6), TopB = C(0x2c3a78), TopC = C(0x0b1030);
    static readonly Color HorA = C(0xfbe6c0), HorB = C(0xf09a6a), HorC = C(0x3a3456);
    static readonly Color FogA = C(0xe6dcc4), FogB = C(0x8a82a8), FogC = C(0x2b2c46);
    static readonly Color AmbA = C(0x8fa6d8), AmbB = C(0x7d86c2), AmbC = C(0x3c4474);
    static readonly Color GndA = C(0x5f7044), GndB = C(0x4a5040), GndC = C(0x22281e);

    static Color C(uint hex) => MeshData.Srgb(hex);
    static Vector3 V(Color c, float k = 1) => new Vector3(c.R, c.G, c.B) * k;

    // Clouds drifting over the pitch, and the storm's lightning.
    Vector2 _cloudOfs;
    float _cloudShade;
    double _nextFlash = 12, _flashAt = -10;
    /// <summary>When the last lightning struck (match time), for the thunder.</summary>
    public double FlashAt => _flashAt;
    readonly Random _rng = new();

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
        _rain = new RainView(root);
        Weather = Saved;
        // Debug: `-- --weather=sunny|rain|evening` (not saved).
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--weather=")) Weather = arg[10..] switch { "sunny" => Weather.Sunny, "rain" => Weather.Rain, _ => Weather.Evening };
        Apply();
    }

    /// <summary>Changes the weather now (and for the matches after).</summary>
    public void SetWeather(Weather w)
    {
        Saved = w;
        Weather = w;
        Apply();
    }

    /// <summary>The next weather in the list (the pause menu's button).</summary>
    public Weather Cycle()
    {
        SetWeather((Weather)(((int)Weather + 1) % 3));
        return Weather;
    }

    /// <summary>Re-applies everything (after the ground has set its haze and floodlights).</summary>
    public void Refresh() => Apply();

    void Apply()
    {
        var dir = Weather == Weather.Rain ? FloodKeyDir : SunDir;
        _sun.Basis = Basis.LookingAt(dir, Vector3.Up);
        RenderingServer.GlobalShaderParameterSet("gn_sun_dir", dir);
        RenderingServer.GlobalShaderParameterSet("gn_rain", Weather == Weather.Rain ? 1f : 0f);
        RenderingServer.GlobalShaderParameterSet("gn_shadow", Weather == Weather.Rain ? 0f : 1f);
        _rain.Visible = Weather == Weather.Rain;
        float t = _last < 0 ? 0 : _last;
        _last = -1;
        Set(t);
    }

    /// <summary>Match progress 0 (kick-off) to 1 (full time). Cheap when it hasn't moved.</summary>
    public void Set(float t)
    {
        t = Mathf.Clamp(t, 0, 1);
        if (Mathf.Abs(t - _last) < 0.002f) return;
        _last = t;
        switch (Weather)
        {
            case Weather.Sunny: SetSunny(); break;
            case Weather.Rain: SetRain(); break;
            default: SetEvening(t); break;
        }
    }

    /// <summary>Per frame: the clouds drift, the storm flashes.</summary>
    public void Tick(float dt, double time)
    {
        _cloudOfs += new Vector2(2.4f, 1.1f) * dt;
        RenderingServer.GlobalShaderParameterSet("gn_clouds", new Vector4(_cloudShade, _cloudOfs.X, _cloudOfs.Y, 0));
        if (Weather != Weather.Rain) return;
        if (time >= _nextFlash)
        {
            _flashAt = time;
            _nextFlash = time + 14 + _rng.NextDouble() * 30;
        }
        // A strike: a flash, a flicker, a second flash, gone in under half a second.
        float u = (float)(time - _flashAt);
        float f = u < 0.45f ? Mathf.Max(0, 1 - u / 0.08f) * 0.9f + Mathf.Max(0, 1 - Mathf.Abs(u - 0.2f) / 0.1f) * 1.2f : 0;
        RenderingServer.GlobalShaderParameterSet("gn_flash", f * 0.35f);
        if (f > 0.01f || u < 0.6f) _sky.SetShaderParameter("flash", f);
    }

    /// <summary>How cold the air is (0..1): breath shows on a cold night and in the rain.</summary>
    public float Cold => Weather == Weather.Rain ? 1 : Weather == Weather.Sunny ? 0 : _night;
    public float Rain => Weather == Weather.Rain ? 1 : 0;
    float _night;
    /// <summary>How dark it is (0 day .. 1 night).</summary>
    public float Night => _night;

    void Globals(Color sun, float energy, Color amb, Color gnd, float flood, Color fog, float night, Vector2 fogRange, float haze)
    {
        _night = night;
        _sun.LightColor = sun.LinearToSrgb();
        _sun.LightEnergy = energy;
        _env.AmbientLightColor = amb.LinearToSrgb();
        _env.AmbientLightEnergy = 1f;
        RenderingServer.GlobalShaderParameterSet("gn_sun", V(sun, energy));
        RenderingServer.GlobalShaderParameterSet("gn_amb_sky", V(amb));
        RenderingServer.GlobalShaderParameterSet("gn_amb_ground", V(gnd));
        RenderingServer.GlobalShaderParameterSet("gn_flood", flood);
        RenderingServer.GlobalShaderParameterSet("gn_fog", V(fog));
        RenderingServer.GlobalShaderParameterSet("gn_night", night);
        RenderingServer.GlobalShaderParameterSet("gn_fog_range", fogRange);
        RenderingServer.GlobalShaderParameterSet("gn_haze", haze);
    }

    void SetSky(Color top, Color hor, Vector3 sunCol, float stars, float clouds, float overcast = 0, Vector3 glow = default)
    {
        _sky.SetShaderParameter("top", V(top));
        _sky.SetShaderParameter("horizon", V(hor));
        _sky.SetShaderParameter("sun_color", sunCol);
        _sky.SetShaderParameter("sun_dir", -SunDir);
        _sky.SetShaderParameter("stars", stars);
        _sky.SetShaderParameter("clouds", clouds);
        _sky.SetShaderParameter("overcast", overcast);
        _sky.SetShaderParameter("belly_glow", glow);
        _sky.SetShaderParameter("flash", 0f);
    }

    void SetEvening(float t)
    {
        float dusk = Mathf.SmoothStep(0.25f, 0.7f, t);
        float night = Mathf.SmoothStep(0.6f, 1f, t);
        Color K(Color a, Color b, Color c) => a.Lerp(b, dusk).Lerp(c, night);
        var sun = K(SunA, SunB, SunC);
        float energy = Mathf.Lerp(Mathf.Lerp(1.25f, 0.95f, dusk), 0.32f, night);
        var amb = K(AmbA, AmbB, AmbC) * Mathf.Lerp(0.62f, 0.5f, night);
        var gnd = K(GndA, GndB, GndC) * 0.55f;
        // Floodlights: faintly on from the start, carrying the light by full time.
        float flood = Mathf.Lerp(0.1f, 1f, Mathf.SmoothStep(0.15f, 0.95f, t)) * FloodScale;
        Globals(sun, energy, amb, gnd, flood, K(FogA, FogB, FogC), night, FogRange, Haze);
        // A few fair-weather clouds early on, their shadows sliding over the grass.
        _cloudShade = Mathf.Lerp(0.3f, 0, dusk);
        SetSky(K(TopA, TopB, TopC), K(HorA, HorB, HorC), V(sun) * (1 - night), night, Mathf.Lerp(0.55f, 0.15f, dusk));
    }

    void SetSunny()
    {
        var sun = C(0xfff6e6);
        var amb = C(0xb4cdf5) * 0.6f;
        var gnd = C(0x6f8a45) * 0.55f;
        Globals(sun, 1.42f, amb, gnd, 0, C(0xcfe3f2), 0, new Vector2(240, 900), 0.18f);
        _cloudShade = 0.42f;
        SetSky(C(0x2f74d6), C(0xc7e2f7), V(sun) * 1.1f, 0, 1.0f);
    }

    void SetRain()
    {
        var key = C(0xdfe7ff);
        var amb = C(0x46557c) * 0.42f;
        var gnd = C(0x1d281a) * 0.6f;
        Globals(key, 0.6f, amb, gnd, 1.05f * Mathf.Max(FloodScale, 0.6f), C(0x232a38), 1, new Vector2(45, 220), 1.2f);
        _cloudShade = 0;
        SetSky(C(0x05070d), C(0x1b2130), Vector3.Zero, 0, 0, 1, V(C(0xb8c4e0), 0.22f * Mathf.Max(FloodScale, 0.6f)));
    }
}
