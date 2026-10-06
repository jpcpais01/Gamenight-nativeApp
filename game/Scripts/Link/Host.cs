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
/// On a computer it listens on the home network for a phone running GameNight in controller
/// mode, on its own thread, so a packet is taken the moment it lands rather than at the next
/// frame. In a match the phone's buttons, a gamepad and the keyboard all play together
/// (<see cref="Mix"/>); in the menus and the pause menu the phone is a touchpad driving the
/// mouse. Also on a computer: fullscreen at the screen's own resolution and refresh rate
/// (F11 or Alt+Enter for a window), Escape for back.
/// </summary>
public sealed partial class Host : Node
{
    public static Host Instance { get; private set; }

    /// <summary>What the match is fed: the touch controls, the gamepad and the phone, together.</summary>
    public readonly InputState Mixed = new();
    readonly Gamepad _pad = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly string _name = System.Environment.MachineName;
    readonly bool _pc = OS.HasFeature("pc");

    UdpClient _udp;
    WebLink _web;
    System.Threading.Thread _thread;
    volatile bool _running;

    // Shared with the network thread (under _gate).
    readonly object _gate = new();
    readonly Wire.Pad _phone = new();
    IPEndPoint _from;
    long _phoneAt = -100000;
    ushort _lastEvent, _lastTackle, _lastBack, _lastClick;
    readonly List<ButtonEvent> _events = new();
    TackleSwipe _tackle;
    int _backs, _clicks;
    float _cx, _cy, _dx, _dy;

    // What the phone's buttons should say, set by the match each frame.
    long _liveAt = -100000;
    volatile int _mode, _picked = -1;

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
        get { lock (_gate) return Now - _phoneAt < 1000; }
    }

    /// <summary>The phone or a gamepad is doing the playing: the on-screen buttons can go.</summary>
    public bool RemotePlay => PhoneConnected || _clock.Elapsed.TotalSeconds - _pad.LastUsed < 20;

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
        Take(rx, ep);
        bool live = Now - Volatile.Read(ref _liveAt) < 250;
        return Wire.WriteStatus(buf, rx.Time, live, _mode, _picked, _name);
    }

    /// <summary>A phone's packet: its state replaces the last; numbered presses, tackles, backs
    /// and clicks not seen before are queued for the game.</summary>
    void Take(Wire.Pad rx, IPEndPoint ep)
    {
        lock (_gate)
        {
            long now = Now;
            if (_from == null || !_from.Equals(ep) || rx.Nonce != _phone.Nonce || now - _phoneAt > 3000)
            {
                // A new phone (or the same one back): nothing it did before counts again.
                _from = new IPEndPoint(ep.Address, ep.Port);
                _lastEvent = 0;
                foreach (var e in rx.Events)
                    if (Wire.Newer(e.Id, _lastEvent)) _lastEvent = (ushort)(e.Id - 1);
                _lastTackle = rx.TackleId;
                _lastBack = rx.BackId;
                _lastClick = rx.ClickId;
                _cx = rx.CursorX;
                _cy = rx.CursorY;
                _events.Clear();
                _tackle = TackleSwipe.None;
            }
            foreach (var e in rx.Events)
                if (Wire.Newer(e.Id, _lastEvent))
                {
                    _events.Add(e.E);
                    _lastEvent = e.Id;
                }
            if (Wire.Newer(rx.TackleId, _lastTackle))
            {
                _lastTackle = rx.TackleId;
                _tackle = rx.Tackle;
            }
            if (Wire.Newer(rx.BackId, _lastBack))
            {
                _backs += (ushort)(rx.BackId - _lastBack);
                _lastBack = rx.BackId;
            }
            if (Wire.Newer(rx.ClickId, _lastClick))
            {
                _clicks += (ushort)(rx.ClickId - _lastClick);
                _lastClick = rx.ClickId;
            }
            _dx += rx.CursorX - _cx;
            _dy += rx.CursorY - _cy;
            _cx = rx.CursorX;
            _cy = rx.CursorY;

            _phone.Nonce = rx.Nonce;
            _phone.View = rx.View;
            _phone.MoveX = rx.MoveX;
            _phone.MoveY = rx.MoveY;
            _phone.Sprint = rx.Sprint;
            _phone.MouseDown = rx.MouseDown;
            for (int i = 0; i < 3; i++)
            {
                _phone.Held[i] = rx.Held[i];
                _phone.Swipe[i] = rx.Swipe[i];
                _phone.HoldTime[i] = rx.HoldTime[i];
            }
            _phoneAt = now;
        }
    }

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
        h._mode = (int)mode;
        h._picked = picked;
        if (live) Volatile.Write(ref h._liveAt, h.Now);
        var m = h.Mixed;
        m.MoveX = local.MoveX;
        m.MoveY = local.MoveY;
        m.Sprint = local.Sprint;
        for (int i = 0; i < 3; i++)
        {
            m.Held[i] = local.Held[i];
            m.HoldTime[i] = local.HoldTime[i];
            m.Swipe[i] = local.Swipe[i];
        }
        m.Events.AddRange(local.Events);
        local.Events.Clear();
        if (local.TackleSwipe != TackleSwipe.None) m.TackleSwipe = local.TackleSwipe;
        local.TackleSwipe = TackleSwipe.None;

        var p = h._pad.Input;
        Add(m, p.MoveX, p.MoveY, p.Sprint, p.Held, p.HoldTime, p.Swipe);
        if (live)
        {
            m.Events.AddRange(p.Events);
            if (p.TackleSwipe != TackleSwipe.None) m.TackleSwipe = p.TackleSwipe;
        }
        p.Events.Clear();
        p.TackleSwipe = TackleSwipe.None;

        lock (h._gate)
        {
            var ph = h._phone;
            if (h.Now - h._phoneAt < 300 && ph.View == Wire.View.Controls)
            {
                var hold = new double[3];
                for (int i = 0; i < 3; i++) hold[i] = ph.HoldTime[i];
                Add(m, ph.MoveX, ph.MoveY, ph.Sprint, ph.Held, hold, ph.Swipe);
            }
            if (live)
            {
                m.Events.AddRange(h._events);
                if (h._tackle != TackleSwipe.None) m.TackleSwipe = h._tackle;
            }
            h._events.Clear();
            h._tackle = TackleSwipe.None;
        }
        return m;
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
        _pad.Poll((float)delta, (TouchControls.Mode)_mode, _clock.Elapsed.TotalSeconds);
        if (_pad.Back) GoBack();
        if (_udp == null) return;

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
            pad = Now - _phoneAt < 1000 && _phone.View == Wire.View.Pad;
            down = pad && _phone.MouseDown;
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
