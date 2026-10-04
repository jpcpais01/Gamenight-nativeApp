using System;
using Godot;

namespace GameNight.Club;

public enum TifoKind { Giant, End, Fan }

/// <summary>
/// The club's tifos (the PWA's src/ui/tifos.ts): pictures picked from the phone for the giant
/// hanging tifo, the home end's card display and the fan banner on poles. Each is cropped to
/// fill its own shape and kept on this device as user://tifos/{giant|end|fan}.png. Without one
/// the ground paints the club's own design (the fan banner just isn't there).
/// </summary>
public static class Tifos
{
    public readonly record struct Spec(TifoKind Kind, string Id, string Name, string About, int W, int H);

    public static readonly Spec[] All =
    {
        new(TifoKind.Giant, "giant", "Giant tifo", "Dropped from the main stand roof before kick-off", 432, 512),
        new(TifoKind.End, "end", "Home end tifo", "The card display behind the home goal at each kick-off", 512, 160),
        new(TifoKind.Fan, "fan", "Fan banner", "A banner the fans hold up on poles, every match", 640, 320),
    };

    public static Spec Of(TifoKind k) => All[(int)k];

    /// <summary>Where the picture lives (it may not exist).</summary>
    public static string Path(TifoKind k) => $"user://tifos/{Of(k).Id}.png";

    /// <summary>Fired with the kind whenever a picture is set or cleared.</summary>
    public static event Action<TifoKind> Changed;

    static readonly ImageTexture[] _tex = new ImageTexture[3];
    static readonly bool[] _loaded = new bool[3];

    public static bool Has(TifoKind k) => Texture(k) != null;

    /// <summary>The saved picture (W x H), or null.</summary>
    public static Image Load(TifoKind k)
    {
        if (!FileAccess.FileExists(Path(k))) return null;
        var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(Path(k)));
        return img == null || img.IsEmpty() ? null : img;
    }

    /// <summary>The saved picture as a texture, cached; null when there's none.</summary>
    public static ImageTexture Texture(TifoKind k)
    {
        int i = (int)k;
        if (!_loaded[i])
        {
            _loaded[i] = true;
            var img = Load(k);
            _tex[i] = img == null ? null : ImageTexture.CreateFromImage(img);
        }
        return _tex[i];
    }

    /// <summary>Decode a picture file (PNG, JPEG or WebP), crop it to fill the tifo and keep it.</summary>
    public static bool Set(TifoKind k, byte[] bytes)
    {
        var img = Decode(bytes);
        if (img == null) return false;
        var s = Of(k);
        // Crop to fill: the biggest centred region with the tifo's shape, then scale it down.
        float scale = Mathf.Max((float)s.W / img.GetWidth(), (float)s.H / img.GetHeight());
        int cw = Math.Min(img.GetWidth(), Mathf.RoundToInt(s.W / scale));
        int ch = Math.Min(img.GetHeight(), Mathf.RoundToInt(s.H / scale));
        img = img.GetRegion(new Rect2I((img.GetWidth() - cw) / 2, (img.GetHeight() - ch) / 2, cw, ch));
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgb8);
        img.Resize(s.W, s.H, Image.Interpolation.Lanczos);
        DirAccess.MakeDirRecursiveAbsolute("user://tifos");
        if (img.SavePng(Path(k)) != Error.Ok) return false;
        _tex[(int)k] = ImageTexture.CreateFromImage(img);
        _loaded[(int)k] = true;
        Changed?.Invoke(k);
        return true;
    }

    public static void Clear(TifoKind k)
    {
        if (FileAccess.FileExists(Path(k))) DirAccess.RemoveAbsolute(Path(k));
        _tex[(int)k] = null;
        _loaded[(int)k] = true;
        Changed?.Invoke(k);
    }

    static Image Decode(byte[] b)
    {
        if (b == null || b.Length < 12) return null;
        var img = new Image();
        Error e;
        if (b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G') e = img.LoadPngFromBuffer(b);
        else if (b[0] == 0xff && b[1] == 0xd8) e = img.LoadJpgFromBuffer(b);
        else if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E') e = img.LoadWebpFromBuffer(b);
        else if (b[0] == 'B' && b[1] == 'M') e = img.LoadBmpFromBuffer(b);
        else return null;
        return e == Error.Ok && !img.IsEmpty() ? img : null;
    }

    /// <summary>
    /// Ask the phone for a picture (the system picker) and keep it as this tifo. done(true)
    /// when one was set, done(false) when it couldn't be read; nothing when cancelled.
    /// </summary>
    public static void Pick(TifoKind k, Action<bool> done)
    {
        if (!DisplayServer.HasFeature(DisplayServer.Feature.NativeDialogFile))
        {
            done(false);
            return;
        }
        DisplayServer.FileDialogShow("Pick a picture", "", "", false, DisplayServer.FileDialogMode.OpenFile,
            new[] { "*.png, *.jpg, *.jpeg, *.webp;Pictures;image/png,image/jpeg,image/webp" },
            Callable.From((bool ok, string[] paths, long _) =>
            {
                if (!ok || paths == null || paths.Length == 0) return;
                // The callback can arrive off the main loop; finish there.
                Callable.From(() => done(Set(k, FileAccess.GetFileAsBytes(paths[0])))).CallDeferred();
            }));
    }
}
