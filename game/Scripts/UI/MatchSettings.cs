using Godot;

namespace GameNight.UI;

/// <summary>The pause menu's settings, kept between launches (user://match.cfg).</summary>
public static class MatchSettings
{
    const string Path = "user://match.cfg";

    /// <summary>0 close, 1 normal, 2 far (MatchCamera.Presets).</summary>
    public static int Camera = 1;
    /// <summary>Art height in pixels (0: the default, about 270).</summary>
    public static int Pixels;
    public static bool ShowFps;
    /// <summary>Fast graphics: no sun shadows.</summary>
    public static bool Fast;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        Camera = (int)cfg.GetValue("match", "camera", 1);
        Pixels = (int)cfg.GetValue("match", "pixels", 0);
        ShowFps = (bool)cfg.GetValue("match", "fps", false);
        Fast = (bool)cfg.GetValue("match", "fast", false);
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("match", "camera", Camera);
        cfg.SetValue("match", "pixels", Pixels);
        cfg.SetValue("match", "fps", ShowFps);
        cfg.SetValue("match", "fast", Fast);
        cfg.Save(Path);
    }
}
