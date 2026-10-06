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
    /// <summary>The counter with the full breakdown of where each frame's time goes.</summary>
    public static bool Profile;
    /// <summary>Fast graphics: no sun shadows.</summary>
    public static bool Fast;
    /// <summary>Volume bars, 0..10: everything, the crowd, the match's own sounds (ball, whistle,
    /// nets, goal explosions), the menus.</summary>
    public static int VolMaster = 10, VolCrowd = 10, VolFx = 10, VolUi = 10;
    /// <summary>Four samples per art pixel: steady lines, nets and crowds while the camera moves.</summary>
    public static bool Smooth = true;
    /// <summary>The frame rate limit the player picked: 60, 90 or 120.</summary>
    public static int FpsCap = 120;
    public static readonly int[] FpsCaps = { 60, 90, 120 };

    /// <summary>Holds the game to the picked limit.</summary>
    public static void ApplyFpsCap() => Engine.MaxFps = FpsCap;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        Camera = (int)cfg.GetValue("match", "camera", 1);
        Pixels = (int)cfg.GetValue("match", "pixels", 0);
        ShowFps = (bool)cfg.GetValue("match", "fps", false);
        Profile = ShowFps && (bool)cfg.GetValue("match", "profile", false);
        Fast = (bool)cfg.GetValue("match", "fast", false);
        int Vol(string key) => System.Math.Clamp((int)cfg.GetValue("sound", key, 10), 0, 10);
        // Before the bars there was only on / off.
        VolMaster = (bool)cfg.GetValue("match", "sound", true) ? Vol("master") : 0;
        VolCrowd = Vol("crowd");
        VolFx = Vol("match");
        VolUi = Vol("menus");
        Smooth = (bool)cfg.GetValue("match", "smooth", true);
        FpsCap = (int)cfg.GetValue("match", "fpscap", 120);
        if (System.Array.IndexOf(FpsCaps, FpsCap) < 0) FpsCap = 120;
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("match", "camera", Camera);
        cfg.SetValue("match", "pixels", Pixels);
        cfg.SetValue("match", "fps", ShowFps);
        cfg.SetValue("match", "profile", Profile);
        cfg.SetValue("match", "fast", Fast);
        cfg.SetValue("sound", "master", VolMaster);
        cfg.SetValue("sound", "crowd", VolCrowd);
        cfg.SetValue("sound", "match", VolFx);
        cfg.SetValue("sound", "menus", VolUi);
        cfg.SetValue("match", "smooth", Smooth);
        cfg.SetValue("match", "fpscap", FpsCap);
        cfg.Save(Path);
    }
}
