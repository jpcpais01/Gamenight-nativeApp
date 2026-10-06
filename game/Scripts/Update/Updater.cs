using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Godot;

namespace GameNight.Update;

/// <summary>
/// Updates the app from inside it (an autoload). On the phone and on Windows it asks GitHub for
/// the latest release when the game starts (and every 20 minutes after); when that release is
/// newer, the home screen shows an UPDATE key. On Android the new APK is downloaded and handed to
/// the system installer (GodotApp.installApk), which installs it over this version. On Windows
/// the zip is downloaded and unpacked next to the game, then a tiny script waits for the game to
/// close, moves the new files over the old ones and starts it again.
/// </summary>
public sealed partial class Updater : Node
{
    public static Updater Instance { get; private set; }

    const string Latest = "https://api.github.com/repos/jpcpais01/Gamenight-nativeApp/releases/latest";
    const double Every = 20 * 60;

    public enum Stage { None, Available, Downloading, Permission, Installing, Confirm, Failed }

    public Stage Now { get; private set; }
    /// <summary>The newer version on offer ("0.89.152"), or null.</summary>
    public string Offer { get; private set; }
    public long Size { get; private set; }
    public string Error { get; private set; } = "";

    /// <summary>How much of the download is in, 0..1.</summary>
    public float Progress => _get != null && _get.GetBodySize() > 0 ? Mathf.Clamp((float)_get.GetDownloadedBytes() / _get.GetBodySize(), 0, 1) : 0;

    readonly bool _android = OS.GetName() == "Android", _windows = OS.GetName() == "Windows";
    string _url;
    HttpRequest _ask, _get;
    double _next;
    GodotObject _java;

    string Target => ProjectSettings.GlobalizePath(_android ? "user://update.apk" : "user://update.zip");
    public static string Version => (string)ProjectSettings.GetSetting("application/config/version", "0.0.0");

    public override void _Ready()
    {
        Instance = this;
        if (OS.HasFeature("editor") || !(_android || _windows)) SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if ((_next -= delta) <= 0 && (Now is Stage.None or Stage.Available)) Check();
        if (Now == Stage.Permission && Java?.Call("canInstall").AsBool() == true) Install();
        if (Now is Stage.Installing or Stage.Confirm)
        {
            string s = Java?.Call("installStatus").AsString() ?? "";
            if (s == "confirm") Now = Stage.Confirm;
            else if (s == "cancelled") Now = Stage.Available;
            else if (s.StartsWith("error:")) Fail(s[6..]);
        }
    }

    /// <summary>Ask for the latest release now (the settings' CHECK FOR UPDATES).</summary>
    public void CheckNow()
    {
        if (Now is Stage.None or Stage.Available) Check();
    }

    void Check()
    {
        _next = Every;
        if (_ask != null) return;
        _ask = new HttpRequest { Timeout = 20 };
        AddChild(_ask);
        _ask.RequestCompleted += (result, code, headers, body) =>
        {
            _ask.QueueFree();
            _ask = null;
            if (result == (long)HttpRequest.Result.Success && code == 200) Read(body);
        };
        _ask.Request(Latest, new[] { "User-Agent: GameNight", "Accept: application/vnd.github+json" });
    }

    /// <summary>The release: its version (the tag, v0.89.152) and our platform's file.</summary>
    void Read(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            string tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";
            if (!Newer(tag, Version)) return;
            string want = _android ? "GameNight.apk" : "GameNight-Windows.zip";
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                if (a.GetProperty("name").GetString() != want) continue;
                _url = a.GetProperty("browser_download_url").GetString();
                Size = a.GetProperty("size").GetInt64();
                Offer = tag;
                Now = Stage.Available;
                return;
            }
        }
        catch (Exception e)
        {
            GD.Print("Update check: ", e.Message);
        }
    }

    static bool Newer(string a, string b)
    {
        var x = a.Split('.');
        var y = b.Split('.');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int p = i < x.Length && int.TryParse(x[i], out var u) ? u : 0;
            int q = i < y.Length && int.TryParse(y[i], out var v) ? v : 0;
            if (p != q) return p > q;
        }
        return false;
    }

    /// <summary>The UPDATE key: download, then install (or carry on where it got to).</summary>
    public void Start()
    {
        if (Now == Stage.Failed && _url != null) Now = Stage.Available;
        if (Now != Stage.Available) return;
        Now = Stage.Downloading;
        _get = new HttpRequest { DownloadFile = Target, DownloadChunkSize = 1 << 16, UseThreads = true, Timeout = 0 };
        AddChild(_get);
        _get.RequestCompleted += (result, code, headers, body) =>
        {
            _get.QueueFree();
            _get = null;
            if (result != (long)HttpRequest.Result.Success || code != 200) Fail(result != (long)HttpRequest.Result.Success ? $"download failed ({(HttpRequest.Result)result})" : $"download failed (HTTP {code})");
            else if (_android) Install();
            else Swap();
        };
        _get.Request(_url, new[] { "User-Agent: GameNight" });
    }

    GodotObject Java => _android ? _java ??= JavaClassWrapper.Wrap("com.godot.game.GodotApp") : null;

    void Install()
    {
        string r = Java?.Call("installApk", Target).AsString() ?? "error:no installer";
        if (r == "permission") Now = Stage.Permission;
        else if (r == "started") Now = Stage.Installing;
        else Fail(r.StartsWith("error:") ? r[6..] : r);
    }

    /// <summary>Windows: unpack beside the game, then a script swaps the files in once we close
    /// and starts the new version.</summary>
    void Swap()
    {
        try
        {
            string exe = OS.GetExecutablePath(), dir = Path.GetDirectoryName(exe);
            string stage = Path.Combine(dir, "update");
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            ZipFile.ExtractToDirectory(Target, stage);
            File.Delete(Target);
            string script = Path.Combine(Path.GetTempPath(), "gamenight-update.cmd");
            // Paths come in as arguments (the script itself stays ASCII, so a name like João's works).
            File.WriteAllText(script, string.Join("\r\n",
                "@echo off",
                ":wait",
                "tasklist /FI \"PID eq %1\" 2>nul | find \" %1 \" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)",
                "robocopy \"%~2\" \"%~3\" /E /MOVE /R:10 /W:1 >nul",
                "start \"\" \"%~3\\GameNight.exe\"",
                "del \"%~f0\"", ""));
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\" {OS.GetProcessId()} \"{stage}\" \"{dir}\"\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            GetTree().Quit();
        }
        catch (UnauthorizedAccessException)
        {
            Fail("can't write to the game's folder: move GameNight out of Program Files");
        }
        catch (Exception e)
        {
            Fail(e.Message);
        }
    }

    void Fail(string why)
    {
        GD.PrintErr("Update: ", why);
        Error = why;
        Now = Stage.Failed;
    }
}
