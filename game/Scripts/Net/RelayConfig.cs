namespace GameNight.Net;

/// <summary>Where the online relay lives (relay/ in the repository). The build fills Built in
/// from the Cloudflare account; empty means online play isn't set up yet.</summary>
public static class RelayConfig
{
    public const string Built = "";

    /// <summary>Debug: `-- --relay=ws://host:8787` plays through another relay (a local `wrangler dev`).</summary>
    public static string Url
    {
        get
        {
            foreach (var a in Godot.OS.GetCmdlineUserArgs())
                if (a.StartsWith("--relay=")) return a[8..].TrimEnd('/');
            return Built.TrimEnd('/');
        }
    }
}
