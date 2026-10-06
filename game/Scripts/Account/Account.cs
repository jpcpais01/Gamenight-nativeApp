using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using HttpClient = Godot.HttpClient;

namespace GameNight.Account;

/// <summary>
/// Your GameNight account (an autoload): a username and a password, and your save kept in the
/// cloud so the same club follows you between the phone and the PC.
///
/// Firebase underneath, over its REST APIs (the same code on Android and Windows). Firebase Auth
/// only knows email logins, so a username is a hidden address, name@project.firebaseapp.com, which
/// also keeps names unique; nothing is ever sent to it. The save is one Firestore document,
/// users/{uid}: the club, the league and the career files, deflated, with a hash and the time.
///
/// The files on the device stay the real save, so everything works offline. They are watched:
/// a change goes up a moment later. On launch, sign-in and coming back to the app the cloud copy
/// is fetched; if another device saved since, it replaces the files here (a copy of the old ones
/// goes to user://backup) and the game reloads around it. Both changed: the newer one wins.
/// </summary>
public sealed partial class Account : Node
{
    public static Account Instance { get; private set; }

    public enum State { Off, Working, Synced, Offline }

    /// <summary>The save files that travel with the account.</summary>
    static readonly (string key, string file)[] Files = { ("club", "club.json"), ("league", "league.json"), ("career", "career.json") };

    const string ConfigPath = "user://account.cfg";
    const double Debounce = 2.5, RetryAfter = 30;

    public State Now { get; private set; }
    /// <summary>The username, as it was typed when the account was made.</summary>
    public string User { get; private set; } = "";
    public bool SignedIn => _uid.Length > 0;
    /// <summary>Unix ms of the last time the save and the cloud matched.</summary>
    public long SyncedAt { get; private set; }

    string _uid = "", _refresh = "", _token = "", _hash = "", _cloudTime = "", _device = "";
    double _tokenUntil;
    bool _busy, _dirty, _pull, _reload;
    double _dirtyAt, _retryAt, _clock;
    long _seen;
    string _dir;

    public override void _Ready()
    {
        Instance = this;
        _dir = OS.GetUserDataDir();
        var c = new ConfigFile();
        c.Load(ConfigPath);
        _uid = (string)c.GetValue("account", "uid", "");
        User = (string)c.GetValue("account", "name", "");
        _refresh = (string)c.GetValue("account", "refresh", "");
        _hash = (string)c.GetValue("account", "hash", "");
        _cloudTime = (string)c.GetValue("account", "cloud", "");
        SyncedAt = (long)c.GetValue("account", "synced", 0L);
        _device = (string)c.GetValue("account", "device", "");
        if (_device.Length == 0)
        {
            _device = $"{OS.GetName()}-{Guid.NewGuid():N}"[..20];
            Remember();
        }
        _seen = Stamp();
        _pull = SignedIn;
        Now = SignedIn ? State.Working : State.Off;
    }

    void Remember()
    {
        var c = new ConfigFile();
        c.SetValue("account", "uid", _uid);
        c.SetValue("account", "name", User);
        c.SetValue("account", "refresh", _refresh);
        c.SetValue("account", "hash", _hash);
        c.SetValue("account", "cloud", _cloudTime);
        c.SetValue("account", "synced", SyncedAt);
        c.SetValue("account", "device", _device);
        c.Save(ConfigPath);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        // The save files changed: they go up once they've been still for a moment.
        long st = Stamp();
        if (st != _seen)
        {
            _seen = st;
            _dirty = true;
            _dirtyAt = _clock;
        }
        // Nothing moves while a match is on: its result is saved at full time, then it goes up.
        if (GetTree().CurrentScene is Menus.App { Playing: true }) return;
        if (_reload)
        {
            // A save came down from the cloud: start the game again around it.
            _reload = false;
            GetTree().ReloadCurrentScene();
            return;
        }
        if (_busy || !SignedIn || !FirebaseConfig.Ready || _clock < _retryAt) return;
        if (_pull || _dirty && _clock - _dirtyAt > Debounce) _ = Sync();
    }

    public override void _Notification(int what)
    {
        // Back in the app: another device may have played since.
        if (what == NotificationApplicationResumed && SignedIn) _pull = true;
    }

    /// <summary>"2 MIN AGO": when the save last matched the cloud.</summary>
    public string SyncedAgo
    {
        get
        {
            if (SyncedAt == 0) return "NOT YET";
            var d = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(SyncedAt);
            return d.TotalMinutes < 1 ? "JUST NOW" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} MIN AGO" : d.TotalDays < 1 ? $"{(int)d.TotalHours} H AGO" : $"{(int)d.TotalDays} DAYS AGO";
        }
    }

    /// <summary>The account box's SYNC NOW.</summary>
    public void SyncNow()
    {
        _pull = true;
        _retryAt = 0;
    }

    // ---------------------------------------------------------------- signing in

    /// <summary>Why a username can't be used, or null.</summary>
    public static string CheckName(string name)
    {
        if (name.Length < 3) return "Usernames need at least 3 characters";
        if (name.Length > 16) return "Usernames are 16 characters at most";
        if (!name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_' || ch == '-')) return "Use letters, numbers, _ and - only";
        return null;
    }

    static string Address(string name) => $"{name.ToLowerInvariant()}@{FirebaseConfig.ProjectId}.firebaseapp.com";

    /// <summary>Create the account (fresh = true) or sign in to it. Null when it worked, else
    /// what to tell the player. The club here goes up to a new account; signing in to one that
    /// has a save brings that save down.</summary>
    public async Task<string> Enter(string name, string password, bool fresh)
    {
        if (!FirebaseConfig.Ready) return "Accounts aren't switched on in this version yet";
        name = name.Trim();
        if (CheckName(name) is string bad) return bad;
        if (fresh && password.Length < 6) return "Passwords need at least 6 characters";
        if (_busy) return "One moment, still saving";
        _busy = true;
        try
        {
            var body = new JsonObject { ["email"] = Address(name), ["password"] = password, ["returnSecureToken"] = true };
            var (code, r) = await Call(HttpClient.Method.Post,
                $"https://identitytoolkit.googleapis.com/v1/accounts:{(fresh ? "signUp" : "signInWithPassword")}?key={FirebaseConfig.ApiKey}", body.ToJsonString());
            if (code == 0) return "No connection. Check the internet and try again";
            if (code != 200) return AuthError(r);
            string uid = (string)r["localId"];
            if (uid != _uid)
            {
                // Another account on this device: whatever is here was never its save.
                _hash = "";
                _cloudTime = "";
            }
            _uid = uid;
            User = name;
            Took(r, "idToken", "refreshToken", "expiresIn");
            Remember();
        }
        finally
        {
            _busy = false;
        }
        await Sync(fresh);
        return null;
    }

    public void SignOut()
    {
        _uid = _refresh = _token = _hash = _cloudTime = "";
        User = "";
        SyncedAt = 0;
        Now = State.Off;
        Remember();
    }

    static string AuthError(JsonNode r)
    {
        string m = (string)r?["error"]?["message"] ?? "";
        if (m.StartsWith("EMAIL_EXISTS")) return "That username is taken";
        if (m.StartsWith("INVALID_LOGIN_CREDENTIALS") || m.StartsWith("EMAIL_NOT_FOUND") || m.StartsWith("INVALID_PASSWORD") || m.StartsWith("INVALID_EMAIL"))
            return "Wrong username or password";
        if (m.StartsWith("WEAK_PASSWORD")) return "Passwords need at least 6 characters";
        if (m.StartsWith("TOO_MANY_ATTEMPTS")) return "Too many tries. Wait a minute";
        if (m.StartsWith("USER_DISABLED")) return "This account is switched off";
        if (m.StartsWith("OPERATION_NOT_ALLOWED") || m.StartsWith("CONFIGURATION_NOT_FOUND") || m.Contains("API key")) return "Accounts aren't switched on yet";
        GD.Print("Account: ", m);
        return "That didn't work (" + (m.Length > 0 ? m.Split(' ')[0] : "error") + ")";
    }

    void Took(JsonNode r, string token, string refresh, string expires)
    {
        _token = (string)r[token];
        _refresh = (string)r[refresh] ?? _refresh;
        _tokenUntil = _clock + (double.TryParse((string)r[expires], out var s) ? s : 3600) - 120;
    }

    /// <summary>A fresh ID token (they last an hour). False when offline or signed out.</summary>
    async Task<bool> Token()
    {
        if (_token.Length > 0 && _clock < _tokenUntil) return true;
        var (code, r) = await Call(HttpClient.Method.Post, $"https://securetoken.googleapis.com/v1/token?key={FirebaseConfig.ApiKey}",
            $"grant_type=refresh_token&refresh_token={Uri.EscapeDataString(_refresh)}", "Content-Type: application/x-www-form-urlencoded");
        if (code == 200)
        {
            Took(r, "id_token", "refresh_token", "expires_in");
            Remember();
            return true;
        }
        if (code is 400 or 401 or 403)
        {
            // The account was removed or its sign-in revoked: back to signed out, save untouched.
            GD.Print("Account: sign-in expired, ", (string)r?["error"]?["message"]);
            SignOut();
        }
        return false;
    }

    // ---------------------------------------------------------------- the save

    string DocUrl => $"https://firestore.googleapis.com/v1/projects/{FirebaseConfig.ProjectId}/databases/(default)/documents/users/{_uid}";

    string[] Auth => new[] { "Authorization: Bearer " + _token, "Content-Type: application/json" };

    /// <summary>Bring this device and the cloud together (see the class notes).</summary>
    async Task Sync(bool fresh = false, bool again = true)
    {
        _busy = true;
        _pull = false;
        _dirty = false;
        Now = State.Working;
        bool ok = false;
        try
        {
            if (!await Token()) return;
            var (code, doc) = await Call(HttpClient.Method.Get, DocUrl, null, Auth);
            if (code != 200 && code != 404) return;
            var local = ReadLocal();
            if (code == 404)
            {
                ok = await Upload(local, null, again);
                return;
            }
            var f = doc["fields"];
            string cloudHash = (string)f?["hash"]?["stringValue"] ?? "";
            string cloudTime = (string)doc["updateTime"] ?? "";
            User = (string)f?["name"]?["stringValue"] ?? User;
            if (cloudHash == local.hash)
            {
                Matched(local.hash, cloudTime);
                ok = true;
                return;
            }
            // Signing in on a device that hasn't shared this account's save yet: the account's
            // save wins. Afterwards, whichever side changed since the last match; both, the newer.
            bool linked = _hash.Length > 0;
            bool mineChanged = local.hash != _hash, cloudChanged = cloudTime != _cloudTime;
            long cloudAt = long.TryParse((string)f?["at"]?["integerValue"], out var a) ? a : 0;
            bool takeCloud = linked ? !mineChanged || cloudChanged && cloudAt >= local.at : !fresh;
            if (takeCloud)
            {
                Apply(f?["files"]?["mapValue"]?["fields"]);
                _seen = Stamp();
                Matched(ReadLocal().hash, cloudTime);
                _reload = true;
                ok = true;
            }
            else ok = await Upload(local, cloudTime, again);
        }
        catch (Exception e)
        {
            GD.Print("Account sync: ", e.Message);
        }
        finally
        {
            _busy = false;
            if (SignedIn) Now = ok ? State.Synced : State.Offline;
            if (!ok)
            {
                // Try again in a while; the change keeps waiting.
                _dirty = true;
                _retryAt = _clock + RetryAfter;
            }
        }
    }

    void Matched(string hash, string cloudTime)
    {
        _hash = hash;
        _cloudTime = cloudTime;
        SyncedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Remember();
    }

    /// <summary>Write the save to the cloud, but only over the version this device last saw
    /// (else a fresh sync sorts out who's newer).</summary>
    async Task<bool> Upload((Dictionary<string, byte[]> files, string hash, long at) local, string over, bool again)
    {
        var files = new JsonObject();
        foreach (var (key, data) in local.files) files[key] = new JsonObject { ["bytesValue"] = Convert.ToBase64String(Deflate(data)) };
        var body = new JsonObject
        {
            ["fields"] = new JsonObject
            {
                ["name"] = new JsonObject { ["stringValue"] = User },
                ["hash"] = new JsonObject { ["stringValue"] = local.hash },
                ["at"] = new JsonObject { ["integerValue"] = local.at.ToString() },
                ["device"] = new JsonObject { ["stringValue"] = _device },
                ["files"] = new JsonObject { ["mapValue"] = new JsonObject { ["fields"] = files } },
            },
        };
        string pre = over == null ? "currentDocument.exists=false" : "currentDocument.updateTime=" + Uri.EscapeDataString(over);
        var (code, r) = await Call(HttpClient.Method.Patch, $"{DocUrl}?{pre}", body.ToJsonString(), Auth);
        if (code == 200)
        {
            Matched(local.hash, (string)r["updateTime"]);
            return true;
        }
        // Another device saved in between: look again, once.
        string status = (string)r?["error"]?["status"] ?? "";
        if (again && (status is "FAILED_PRECONDITION" or "ALREADY_EXISTS" or "NOT_FOUND" || code == 409))
        {
            await Sync(false, false);
            return Now == State.Synced;
        }
        GD.Print("Account upload: ", code, " ", status);
        return false;
    }

    // ---------------------------------------------------------------- files

    string PathOf(string file) => System.IO.Path.Combine(_dir, file);

    /// <summary>The save files' last-write times together: changes whenever one is saved.</summary>
    long Stamp()
    {
        long s = 0;
        foreach (var (_, file) in Files)
        {
            var p = PathOf(file);
            s = s * 31 + (File.Exists(p) ? File.GetLastWriteTimeUtc(p).Ticks : 1);
        }
        return s;
    }

    (Dictionary<string, byte[]> files, string hash, long at) ReadLocal()
    {
        var files = new Dictionary<string, byte[]>();
        var all = new MemoryStream();
        long at = 0;
        foreach (var (key, file) in Files)
        {
            var p = PathOf(file);
            all.Write(Encoding.UTF8.GetBytes(key + ":"));
            if (!File.Exists(p)) continue;
            var data = File.ReadAllBytes(p);
            files[key] = data;
            all.Write(data);
            at = Math.Max(at, new DateTimeOffset(File.GetLastWriteTimeUtc(p)).ToUnixTimeMilliseconds());
        }
        return (files, Convert.ToHexString(SHA256.HashData(all.ToArray())), at);
    }

    /// <summary>The cloud's files over the ones here, after copying those to user://backup.</summary>
    void Apply(JsonNode cloud)
    {
        var backup = PathOf("backup");
        Directory.CreateDirectory(backup);
        foreach (var (key, file) in Files)
        {
            var p = PathOf(file);
            if (File.Exists(p)) File.Copy(p, System.IO.Path.Combine(backup, file), true);
            string b64 = (string)cloud?[key]?["bytesValue"];
            if (b64 == null)
            {
                if (File.Exists(p)) File.Delete(p);
                continue;
            }
            File.WriteAllBytes(p + ".tmp", Inflate(Convert.FromBase64String(b64)));
            File.Move(p + ".tmp", p, true);
        }
    }

    static byte[] Deflate(byte[] data)
    {
        var o = new MemoryStream();
        using (var z = new DeflateStream(o, CompressionLevel.Optimal)) z.Write(data);
        return o.ToArray();
    }

    static byte[] Inflate(byte[] data)
    {
        var o = new MemoryStream();
        using (var z = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress)) z.CopyTo(o);
        return o.ToArray();
    }

    // ---------------------------------------------------------------- http

    /// <summary>One request through Godot's own HTTP (its TLS certificates work the same on every
    /// platform). Code 0 = no answer.</summary>
    async Task<(int code, JsonNode body)> Call(HttpClient.Method method, string url, string body, params string[] headers)
    {
        if (headers.Length == 0) headers = new[] { "Content-Type: application/json" };
        var req = new HttpRequest { Timeout = 20 };
        AddChild(req);
        try
        {
            if (req.Request(url, headers, method, body ?? "") != Error.Ok) return (0, null);
            var r = await ToSignal(req, HttpRequest.SignalName.RequestCompleted);
            if (r[0].AsInt64() != (long)HttpRequest.Result.Success) return (0, null);
            var bytes = r[3].AsByteArray();
            JsonNode json = null;
            try
            {
                if (bytes.Length > 0) json = JsonNode.Parse(bytes);
            }
            catch
            {
                /* not JSON */
            }
            return (r[1].AsInt32(), json);
        }
        finally
        {
            req.QueueFree();
        }
    }
}
