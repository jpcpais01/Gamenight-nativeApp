using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Godot;
using GameNight.Sim;
using GameNight.UI;

namespace GameNight.Link;

/// <summary>
/// The PC end of the phone controller (an autoload, so it lives across menus and matches).
/// On a computer it listens on the home network for phones running GameNight in controller
/// mode (or the web controller), on its own thread, so a packet is taken the moment it lands
/// rather than at the next frame. In a match the phones' buttons, gamepads and the keyboard
/// all play together (<see cref="Mix"/>), or, in a same-screen 1v1, each for the side it was
/// put on (<see cref="MixVersus"/>, <see cref="Controllers"/>). In the menus and the pause
/// menu a phone is a touchpad driving the mouse. Also on a computer: fullscreen at the
/// screen's own resolution and refresh rate (F11 or Alt+Enter for a window), Escape for back.
/// A phone listens too, but only while a 1v1 wants a second controller (<see cref="Serve"/>).
/// </summary>
public sealed partial class Host : Node
{
    public static Host Instance { get; private set; }

    /// <summary>What the match is fed: the touch controls, the gamepads and the phones, together.</summary>
    public readonly InputState Mixed = new();
    readonly Dictionary<int, Gamepad> _pads = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly string _name = System.Environment.MachineName;
    readonly bool _pc = OS.HasFeature("pc");

    UdpClient _udp;
    WebLink _web;
    System.Threading.Thread _thread;
    volatile bool _running;

    /// <summary>One phone playing as a controller (two can, for a 1v1).</summary>
    sealed class Phone
    {
        public readonly Wire.Pad Pad = new();
        public IPEndPoint From;
        public long At = -100000;
        public ushort LastEvent, LastTackle, LastBack, LastClick;
        public readonly List<ButtonEvent> Events = new();
        public TackleSwipe Tackle;
        public float Cx, Cy;
    }

    // Shared with the network thread (under _gate).
    readonly object _gate = new();
    readonly Phone[] _phones = { new(), new() };
    int _backs, _clicks;
    float _dx, _dy;

    // What the phones' buttons should say (per side), set by the match each frame.
    long _liveAt = -100000;
    readonly int[] _mode = new int[2], _picked = { -1, -1 };

    Vector2 _cursor = new(-1, -1);
    bool _mouseDown;
    string _address = "";
    double _addressAt = -100;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        if (!_pc) return;
        if (!OS.HasFeature("editor")) DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
        // The taskbar/Alt+Tab icon, once the engine has finished starting (it sets its own icon
        // after the first scene loads, which would undo an earlier one).
        Callable.From(WinIcon.Apply).CallDeferred();
        Serve(true);
    }

    /// <summary>Listen for phones (always on a computer; on a phone only while a 1v1 is set up
    /// or played, so its own controller mode can't find itself).</summary>
    public void Serve(bool on)
    {
        if (on == Listening) return;
        if (!on)
        {
            if (_pc) return;
            _running = false;
            _udp?.Dispose();
            _udp = null;
            _web?.Stop();
            _web = null;
            return;
        }
        if (Code == 0) LoadCode();
        try
        {
            _udp = new UdpClient(AddressFamily.InterNetwork);
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, Wire.Port));
            // Windows: an unreachable phone must not break the socket (SIO_UDP_CONNRESET off).
            if (OperatingSystem.IsWindows()) _udp.Client.IOControl(-1744830452, new byte[] { 0 }, null);
            _running = true;
            _thread = new System.Threading.Thread(Listen) { IsBackground = true, Name = "PhoneLink", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
            _web = new WebLink(this);
        }
        catch (Exception e)
        {
            GD.Print($"Phone controller: can't listen on port {Wire.Port} ({e.Message})");
            _udp?.Dispose();
            _udp = null;
        }
    }

    public override void _ExitTree()
    {
        _running = false;
        _udp?.Dispose();
        _web?.Stop();
    }

    long Now => _clock.ElapsedMilliseconds;

    /// <summary>A phone is sending (within the last second).</summary>
    public bool PhoneConnected
    {
        get
        {
            lock (_gate)
                foreach (var p in _phones)
                    if (Now - p.At < 1000) return true;
            return false;
        }
    }

    /// <summary>A phone or a gamepad is doing the playing: the on-screen buttons can go.</summary>
    public bool RemotePlay
    {
        get
        {
            if (PhoneConnected) return true;
            double t = _clock.Elapsed.TotalSeconds;
            foreach (var p in _pads.Values)
                if (t - p.LastUsed < 20) return true;
            return false;
        }
    }

    /// <summary>Listening for a phone (on a computer, with the port free).</summary>
    public bool Listening => _udp != null;

    /// <summary>This computer's address on the home network, for typing into the phone.</summary>
    public string Address
    {
        get
        {
            double t = _clock.Elapsed.TotalSeconds;
            if (t - _addressAt < 5) return _address;
            _addressAt = t;
            int best = -1;
            _address = "";
            foreach (var a in IP.GetLocalAddresses())
            {
                if (!IPAddress.TryParse(a, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) continue;
                if (a.StartsWith("127.") || a.StartsWith("169.254.")) continue;
                int rank = a.StartsWith("192.168.") ? 3 : a.StartsWith("10.") ? 2 : a.StartsWith("172.") ? 1 : 0;
                if (rank > best)
                {
                    best = rank;
                    _address = a;
                }
            }
            return _address;
        }
    }

    /// <summary>The controller web page's address, for any phone's browser ("" if not serving).</summary>
    public string WebAddress => _web?.Serving == true && Address != "" ? $"http://{Address}:{WebLink.Port}" : "";

    /// <summary>The page with the pairing code in it, for the QR code (scanning it pairs).</summary>
    public string PairingAddress => WebAddress != "" ? $"{WebAddress}/?c={Code}" : "";

    /// <summary>The pairing code: a phone must send it before its input counts. Kept between
    /// sessions so a paired phone stays paired, until a new one is picked.</summary>
    public int Code { get; private set; }
    const string CodeFile = "user://link.cfg";

    void LoadCode()
    {
        var cfg = new ConfigFile();
        cfg.Load(CodeFile);
        Code = (int)cfg.GetValue("pairing", "code", 0);
        if (Code < 1000 || Code > 9999) NewCode();
    }

    /// <summary>A fresh code: every phone has to pair again.</summary>
    public void NewCode()
    {
        int old = Code;
        while (Code == old) Code = Random.Shared.Next(1000, 10000);
        var cfg = new ConfigFile();
        cfg.SetValue("pairing", "code", Code);
        cfg.Save(CodeFile);
        lock (_gate)
            foreach (var p in _phones) p.At = -100000;
    }

    // ---------------------------------------------------------------- network thread

    void Listen()
    {
        var buf = new byte[256];
        var rx = new Wire.Pad();
        var ep = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            byte[] b;
            try
            {
                b = _udp.Receive(ref ep);
            }
            catch (ObjectDisposedException) { return; }
            catch (SocketException)
            {
                if (!_running) return;
                continue;
            }
            try
            {
                if (Wire.Is(b, b.Length, Wire.Hello))
                {
                    int n = Wire.WriteHere(buf, _name);
                    _udp.Send(buf, n, ep);
                }
                else if (Wire.ReadPad(b, b.Length, rx))
                {
                    int n = Answer(rx, ep, buf);
                    _udp.Send(buf, n, ep);
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { return; }
        }
    }

    /// <summary>A controller's packet (from the app, or the web page on any phone): taken in,
    /// and the STATUS answer written into `buf` (its length returned). Any thread.</summary>
    internal int Answer(Wire.Pad rx, IPEndPoint ep, byte[] buf)
    {
        bool paired = rx.Code == Code;
        int slot = paired ? Take(rx, ep) : -1;
        bool live = Now - Volatile.Read(ref _liveAt) < 250;
        int side = slot >= 0 ? Math.Max(0, SideOf("phone:" + slot)) : 0;
        int mode, picked;
        lock (_gate)
        {
            mode = _mode[side];
            picked = _picked[side];
        }
        return Wire.WriteStatus(buf, rx.Time, live, mode, picked, paired && slot >= 0, _name);
    }

    /// <summary>A phone's packet: its state replaces the last; numbered presses, tackles, backs
    /// and clicks not seen before are queued for the game. Returns its slot (-1: two other
    /// phones are already playing).</summary>
    int Take(Wire.Pad rx, IPEndPoint ep)
    {
        lock (_gate)
        {
            long now = Now;
            // The slot it already has, else a free (or long silent) one.
            int slot = -1;
            for (int i = 0; i < _phones.Length && slot < 0; i++)
                if (_phones[i].From != null && _phones[i].From.Equals(ep) && now - _phones[i].At < 3000) slot = i;
            for (int i = 0; i < _phones.Length && slot < 0; i++)
                if (now - _phones[i].At > 3000) slot = i;
            if (slot < 0) return -1;
            var ph = _phones[slot];
            if (ph.From == null || !ph.From.Equals(ep) || rx.Nonce != ph.Pad.Nonce || now - ph.At > 3000)
            {
                // A new phone (or the same one back): nothing it did before counts again.
                ph.From = new IPEndPoint(ep.Address, ep.Port);
                ph.LastEvent = 0;
                foreach (var e in rx.Events)
                    if (Wire.Newer(e.Id, ph.LastEvent)) ph.LastEvent = (ushort)(e.Id - 1);
                ph.LastTackle = rx.TackleId;
                ph.LastBack = rx.BackId;
                ph.LastClick = rx.ClickId;
                ph.Cx = rx.CursorX;
                ph.Cy = rx.CursorY;
                ph.Events.Clear();
                ph.Tackle = TackleSwipe.None;
            }
            foreach (var e in rx.Events)
                if (Wire.Newer(e.Id, ph.LastEvent))
                {
                    ph.Events.Add(e.E);
                    ph.LastEvent = e.Id;
                }
            if (Wire.Newer(rx.TackleId, ph.LastTackle))
            {
                ph.LastTackle = rx.TackleId;
                ph.Tackle = rx.Tackle;
            }
            if (Wire.Newer(rx.BackId, ph.LastBack))
            {
                _backs += (ushort)(rx.BackId - ph.LastBack);
                ph.LastBack = rx.BackId;
            }
            if (Wire.Newer(rx.ClickId, ph.LastClick))
            {
                _clicks += (ushort)(rx.ClickId - ph.LastClick);
                ph.LastClick = rx.ClickId;
            }
            _dx += rx.CursorX - ph.Cx;
            _dy += rx.CursorY - ph.Cy;
            ph.Cx = rx.CursorX;
            ph.Cy = rx.CursorY;

            var p = ph.Pad;
            p.Nonce = rx.Nonce;
            p.View = rx.View;
            p.MoveX = rx.MoveX;
            p.MoveY = rx.MoveY;
            p.Sprint = rx.Sprint;
            p.MouseDown = rx.MouseDown;
            for (int i = 0; i < 3; i++)
            {
                p.Held[i] = rx.Held[i];
                p.Swipe[i] = rx.Swipe[i];
                p.HoldTime[i] = rx.HoldTime[i];
            }
            ph.At = now;
            return slot;
        }
    }

    // ---------------------------------------------------------------- controllers and sides

    /// <summary>A controller that can play: the keyboard (on a phone: its own screen), a gamepad, a phone.</summary>
    public readonly record struct Controller(string Id, string Name, float StickX);

    /// <summary>Same-screen 1v1: which side each controller plays for (0 home, 1 away, -1 neither).</summary>
    readonly Dictionary<string, int> _sides = new();

    public int SideOf(string id)
    {
        lock (_sides) return _sides.TryGetValue(id, out int s) ? s : -1;
    }

    public void SetSide(string id, int side)
    {
        lock (_sides) _sides[id] = Math.Clamp(side, -1, 1);
    }

    /// <summary>The controllers there are now (the keyboard or screen always), with their sticks.</summary>
    public List<Controller> Controllers()
    {
        var list = new List<Controller> { new("keys", _pc ? "KEYBOARD" : "THIS PHONE", 0) };
        foreach (var (dev, pad) in _pads) list.Add(new($"pad:{dev}", $"GAMEPAD {dev + 1}", (float)pad.Input.MoveX));
        lock (_gate)
            for (int i = 0; i < _phones.Length; i++)
                if (Now - _phones[i].At < 1500) list.Add(new($"phone:{i}", $"PHONE {i + 1}", _phones[i].Pad.MoveX));
        // Someone new: onto an empty side, if there is one.
        lock (_sides)
            foreach (var c in list)
            {
                if (_sides.ContainsKey(c.Id)) continue;
                bool home = false, away = false;
                foreach (var o in list)
                    if (_sides.TryGetValue(o.Id, out int v))
                    {
                        home |= v == 0;
                        away |= v == 1;
                    }
                _sides[c.Id] = !home ? 0 : !away ? 1 : -1;
            }
        return list;
    }

    /// <summary>The phones show their match controls (not the touchpad) while this is called every frame.</summary>
    public void KeepLive() => Volatile.Write(ref _liveAt, Now);

    // ---------------------------------------------------------------- the game's side

    /// <summary>
    /// Called by the match every frame with the touch controls' input: returns everything that
    /// plays (touch, keyboard, gamepad, phone) in one state. `live` says the controls are up
    /// (not paused, no replay), which turns the phone from touchpad into controller; `mode` and
    /// `picked` are what its buttons should say.
    /// </summary>
    public static InputState Mix(InputState local, bool live, TouchControls.Mode mode, int picked)
    {
        var h = Instance;
        if (h == null) return local;
        lock (h._gate)
        {
            h._mode[0] = h._mode[1] = (int)mode;
            h._picked[0] = h._picked[1] = picked;
        }
        h.Gather(h.Mixed, null, local, live);
        return h.Mixed;
    }

    /// <summary>A same-screen 1v1: each controller's input goes to the side it's on (see
    /// <see cref="SetSide"/>); `modes` and `picked` are what each side's buttons say.</summary>
    public static void MixVersus(InputState local, bool live, TouchControls.Mode[] modes, int[] picked, InputState home, InputState away)
    {
        var h = Instance;
        if (h == null) return;
        lock (h._gate)
            for (int s = 0; s < 2; s++)
            {
                h._mode[s] = (int)modes[s];
                h._picked[s] = picked[s];
            }
        h.Gather(home, away, local, live);
    }

    readonly double[] _hold = new double[3];

    /// <summary>Everything that plays, into `a` (or, for a 1v1, each controller into its side's).
    /// The sticks and held buttons are this frame's; the presses are added (whoever submits
    /// them takes them out).</summary>
    void Gather(InputState a, InputState b, InputState local, bool live)
    {
        if (live) Volatile.Write(ref _liveAt, Now);
        Reset(a);
        if (b != null) Reset(b);
        InputState To(string id)
        {
            if (b == null) return a;
            int s = SideOf(id);
            return s == 0 ? a : s == 1 ? b : null;
        }

        var to = To("keys");
        if (to != null)
        {
            Add(to, local.MoveX, local.MoveY, local.Sprint, local.Held, local.HoldTime, local.Swipe);
            to.Events.AddRange(local.Events);
            if (local.TackleSwipe != TackleSwipe.None) to.TackleSwipe = local.TackleSwipe;
        }
        local.Events.Clear();
        local.TackleSwipe = TackleSwipe.None;

        foreach (var (dev, pad) in _pads)
        {
            var p = pad.Input;
            to = To($"pad:{dev}");
            if (to != null)
            {
                Add(to, p.MoveX, p.MoveY, p.Sprint, p.Held, p.HoldTime, p.Swipe);
                if (live)
                {
                    to.Events.AddRange(p.Events);
                    if (p.TackleSwipe != TackleSwipe.None) to.TackleSwipe = p.TackleSwipe;
                }
            }
            p.Events.Clear();
            p.TackleSwipe = TackleSwipe.None;
        }

        lock (_gate)
            for (int k = 0; k < _phones.Length; k++)
            {
                var ph = _phones[k];
                to = To($"phone:{k}");
                if (to != null && Now - ph.At < 300 && ph.Pad.View == Wire.View.Controls)
                {
                    for (int i = 0; i < 3; i++) _hold[i] = ph.Pad.HoldTime[i];
                    Add(to, ph.Pad.MoveX, ph.Pad.MoveY, ph.Pad.Sprint, ph.Pad.Held, _hold, ph.Pad.Swipe);
                }
                if (to != null && live)
                {
                    to.Events.AddRange(ph.Events);
                    if (ph.Tackle != TackleSwipe.None) to.TackleSwipe = ph.Tackle;
                }
                ph.Events.Clear();
                ph.Tackle = TackleSwipe.None;
            }
    }

    static void Reset(InputState m)
    {
        m.MoveX = m.MoveY = 0;
        m.Sprint = false;
        for (int i = 0; i < 3; i++)
        {
            m.Held[i] = m.Swipe[i] = false;
            m.HoldTime[i] = 0;
        }
    }

    static void Add(InputState m, double x, double y, bool sprint, bool[] held, double[] hold, bool[] swipe)
    {
        if (x * x + y * y > m.MoveX * m.MoveX + m.MoveY * m.MoveY)
        {
            m.MoveX = x;
            m.MoveY = y;
        }
        m.Sprint |= sprint;
        for (int i = 0; i < 3; i++)
        {
            m.Held[i] |= held[i];
            m.HoldTime[i] = Math.Max(m.HoldTime[i], hold[i]);
            m.Swipe[i] |= swipe[i];
        }
    }

    public override void _Process(double delta)
    {
        // Every gamepad plugged in, each read for its own side's buttons.
        var joy = Godot.Input.GetConnectedJoypads();
        foreach (int dev in joy)
            if (!_pads.ContainsKey(dev)) _pads[dev] = new Gamepad(dev);
        foreach (int dev in new List<int>(_pads.Keys))
            if (!joy.Contains(dev)) _pads.Remove(dev);
        foreach (var (dev, gp) in _pads)
        {
            int mode;
            lock (_gate) mode = _mode[Math.Max(0, SideOf($"pad:{dev}"))];
            gp.Poll((float)delta, (TouchControls.Mode)mode, _clock.Elapsed.TotalSeconds);
            if (gp.Back) GoBack();
        }
        if (_udp == null || !_pc) return;

        int backs, clicks;
        float dx, dy;
        bool pad, down;
        lock (_gate)
        {
            backs = _backs;
            clicks = _clicks;
            dx = _dx;
            dy = _dy;
            _backs = _clicks = 0;
            _dx = _dy = 0;
            pad = down = false;
            foreach (var p in _phones)
                if (Now - p.At < 1000 && p.Pad.View == Wire.View.Pad)
                {
                    pad = true;
                    down |= p.Pad.MouseDown;
                }
        }
        for (int i = 0; i < Math.Min(backs, 3); i++) GoBack();
        if (!pad && !_mouseDown) return;
        Touchpad(dx, dy, down, clicks);
    }

    /// <summary>The phone as a touchpad: it moves the mouse, a tap clicks, and holding its
    /// CLICK key holds the button down (to drag and scroll).</summary>
    void Touchpad(float dx, float dy, bool down, int clicks)
    {
        var size = (Vector2)DisplayServer.WindowGetSize();
        if (_cursor.X < 0) _cursor = (Vector2)(DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition());
        var move = new Vector2(dx, dy) * size.X;
        if (move != Vector2.Zero)
        {
            _cursor = (_cursor + move).Clamp(Vector2.Zero, size - Vector2.One);
            Godot.Input.WarpMouse(_cursor);
            if (_mouseDown)
                Godot.Input.ParseInputEvent(new InputEventMouseMotion { Position = _cursor, GlobalPosition = _cursor, Relative = move, ButtonMask = MouseButtonMask.Left });
        }
        if (down != _mouseDown)
        {
            _mouseDown = down;
            Click(down);
        }
        for (int i = 0; i < Math.Min(clicks, 3); i++)
        {
            Click(true);
            Click(false);
        }
    }

    void Click(bool pressed) => Godot.Input.ParseInputEvent(new InputEventMouseButton
    {
        Position = _cursor, GlobalPosition = _cursor, ButtonIndex = MouseButton.Left, Pressed = pressed,
        ButtonMask = pressed ? MouseButtonMask.Left : 0,
    });

    /// <summary>Back, as Android's back key: the menus go back a screen, a match pauses or resumes.</summary>
    void GoBack() => GetTree().CurrentScene?.Notification((int)NotificationWMGoBackRequest);

    public override void _Input(InputEvent e)
    {
        if (!_pc || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == Key.F11 || k.Keycode == Key.Enter && k.AltPressed)
        {
            bool full = DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
            DisplayServer.WindowSetMode(full ? DisplayServer.WindowMode.Maximized : DisplayServer.WindowMode.ExclusiveFullscreen);
            WinIcon.Apply();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // Escape in the menus (in a match the pause menu takes it first).
        if (_pc && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GoBack();
            GetViewport().SetInputAsHandled();
        }
    }
}
