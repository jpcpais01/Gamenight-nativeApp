using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Godot;
using GameNight.Menus;
using GameNight.UI;

namespace GameNight.Link;

/// <summary>
/// The phone as a controller for GameNight on a PC (Home → PLAY ON PC). It finds the PC on the
/// home Wi-Fi by itself (or by its address, typed in), then shows the match's own touch
/// controls, stick, buttons, swipes and all, and sends their state every frame and on every
/// touch. When the PC is in its menus, paused or showing a replay, the phone turns into a
/// touchpad: slide to move the mouse, tap to click, hold CLICK to drag, BACK to go back.
/// </summary>
public sealed partial class ControllerScreen : PxCanvas
{
    public Action Exit;

    readonly TouchControls _controls = new();
    readonly Wire.Pad _out = new();
    readonly byte[] _buf = new byte[256];
    UdpClient _udp;

    readonly List<(string Ip, string Name)> _found = new();
    IPEndPoint _target;
    string _targetName = "";
    double _targetAt, _statusAt = -100, _helloAt = -100, _now;
    float _ping = -1;
    bool _live;
    Wire.View _view = Wire.View.Pad;

    readonly List<(double At, Wire.NumberedEvent E)> _ring = new();
    ushort _nextEvent = 1;

    // The touchpad.
    int _padFinger = -1, _clickFinger = -1;
    Vector2 _padLast, _padStart;
    double _padDownAt;
    bool _padMoved;
    Rect2 _padRect, _clickRect, _backRect, _exitRect, _pauseRect;

    LineEdit _typed;
    const string SaveFile = "user://controller.cfg";

    public ControllerScreen()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        _out.Nonce = (uint)Random.Shared.Next(1, int.MaxValue);
    }

    public override void _Ready()
    {
        _controls.Visible = false;
        _controls.SetProcessInput(false);
        AddChild(_controls);
        try
        {
            _udp = new UdpClient(0, AddressFamily.InterNetwork) { EnableBroadcast = true };
        }
        catch (Exception e)
        {
            GD.Print($"Controller: no network ({e.Message})");
        }
        var cfg = new ConfigFile();
        if (cfg.Load(SaveFile) == Error.Ok)
        {
            string last = (string)cfg.GetValue("pc", "address", "");
            if (IPAddress.TryParse(last, out _)) _found.Add((last, ""));
        }
    }

    public override void _ExitTree() => _udp?.Dispose();

    bool Connected => _now - _statusAt < 1.5;

    // ---------------------------------------------------------------- network

    public override void _Process(double delta)
    {
        base._Process(delta);
        _now += delta;
        Receive();
        if (!Connected)
        {
            if (_now - _helloAt > 0.5) Shout();
            // Nothing back from a PC for a while: look again.
            if (_target != null && _statusAt < _targetAt && _now - _targetAt > 4) _target = null;
            if (_target == null)
                foreach (var f in _found)
                    if (f.Name != "")
                    {
                        Pick(f.Ip, f.Name);
                        break;
                    }
        }
        var want = Connected && _live ? Wire.View.Controls : Wire.View.Pad;
        if (want != _view) Switch(want);
        _controls.Tick((float)delta);
        Send();
    }

    /// <summary>HELLO to everyone on the network (and to the last PC, by name).</summary>
    void Shout()
    {
        _helloAt = _now;
        if (_udp == null) return;
        int n = Wire.WriteHello(_buf, _out.Nonce);
        var to = new List<string> { "255.255.255.255" };
        foreach (var a in IP.GetLocalAddresses())
        {
            var parts = a.Split('.');
            if (parts.Length == 4 && !a.StartsWith("127.")) to.Add($"{parts[0]}.{parts[1]}.{parts[2]}.255");
        }
        foreach (var f in _found) to.Add(f.Ip);
        foreach (var ip in to)
            try { _udp.Send(_buf, n, new IPEndPoint(IPAddress.Parse(ip), Wire.Port)); }
            catch (Exception) { }
    }

    void Receive()
    {
        if (_udp == null) return;
        try
        {
            while (_udp.Available > 0)
            {
                var ep = new IPEndPoint(IPAddress.Any, 0);
                var b = _udp.Receive(ref ep);
                string ip = ep.Address.ToString();
                if (Wire.Is(b, b.Length, Wire.Here))
                {
                    string name = Wire.ReadHere(b, b.Length);
                    int i = _found.FindIndex(f => f.Ip == ip);
                    if (i >= 0) _found[i] = (ip, name);
                    else _found.Add((ip, name));
                }
                else if (Wire.ReadStatus(b, b.Length, out uint echo, out bool live, out int mode, out int picked, out string name)
                         && _target != null && ep.Address.Equals(_target.Address))
                {
                    if (!Connected) Remember(ip);
                    _statusAt = _now;
                    _targetName = name;
                    _live = live;
                    float rtt = (uint)Time.GetTicksMsec() - echo;
                    if (rtt >= 0 && rtt < 2000) _ping = _ping < 0 ? rtt : _ping + (rtt - _ping) * 0.1f;
                    _controls.SetMode((TouchControls.Mode)Math.Clamp(mode, 0, 5), picked);
                }
            }
        }
        catch (SocketException) { }
    }

    void Pick(string ip, string name)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return;
        _target = new IPEndPoint(addr, Wire.Port);
        _targetName = name != "" ? name : ip;
        _targetAt = _now;
        _ping = -1;
        _helloAt = -100;
    }

    static void Remember(string ip)
    {
        var cfg = new ConfigFile();
        cfg.SetValue("pc", "address", ip);
        cfg.Save(SaveFile);
    }

    /// <summary>This frame's controller state to the PC (also straight after every touch).</summary>
    void Send()
    {
        if (_udp == null || _target == null) return;
        var input = _controls.Input;
        foreach (var e in input.Events) _ring.Add((_now, new Wire.NumberedEvent { Id = _nextEvent++, E = e }));
        input.Events.Clear();
        // Every press goes out again and again for half a second, in case a packet is lost.
        _ring.RemoveAll(r => _now - r.At > 0.5);
        while (_ring.Count > 12) _ring.RemoveAt(0);
        if (input.TackleSwipe != Sim.TackleSwipe.None)
        {
            _out.TackleId++;
            _out.Tackle = input.TackleSwipe;
            input.TackleSwipe = Sim.TackleSwipe.None;
        }
        _out.Time = (uint)Time.GetTicksMsec();
        _out.View = _view;
        _out.MoveX = (float)input.MoveX;
        _out.MoveY = (float)input.MoveY;
        _out.Sprint = input.Sprint;
        for (int i = 0; i < 3; i++)
        {
            _out.Held[i] = input.Held[i];
            _out.Swipe[i] = input.Swipe[i];
            _out.HoldTime[i] = (float)input.HoldTime[i];
        }
        _out.Events.Clear();
        foreach (var r in _ring) _out.Events.Add(r.E);
        int n = Wire.WritePad(_buf, _out);
        try { _udp.Send(_buf, n, _target); }
        catch (Exception) { }
    }

    void Switch(Wire.View v)
    {
        _view = v;
        bool controls = v == Wire.View.Controls;
        if (!controls) _controls.ReleaseAll();
        _controls.Visible = controls;
        _controls.SetProcessInput(controls);
        _padFinger = _clickFinger = -1;
        _out.MouseDown = false;
    }

    /// <summary>Android back: back on the PC while connected, else leave.</summary>
    public void Back()
    {
        if (_typed != null) CloseTyping();
        else if (Connected) _out.BackId++;
        else Exit?.Invoke();
    }

    // ---------------------------------------------------------------- touch

    public override void _Input(InputEvent e)
    {
        if (!Connected) return;
        if (e is InputEventScreenTouch t)
        {
            var p = ((InputEventScreenTouch)MakeInputLocal(t)).Position;
            if (t.Pressed)
            {
                if (_exitRect.HasPoint(p)) { Exit?.Invoke(); return; }
                if (_view == Wire.View.Controls && _pauseRect.HasPoint(p)) _out.BackId++;
                if (_view == Wire.View.Pad) PadDown(t.Index, p);
            }
            else if (_view == Wire.View.Pad) PadUp(t.Index);
            // Out at once rather than at the next frame (the touch controls have already seen it).
            Send();
        }
        else if (e is InputEventScreenDrag d && _view == Wire.View.Pad && d.Index == _padFinger)
            PadMove(((InputEventScreenDrag)MakeInputLocal(d)).Position);
    }

    void PadDown(int id, Vector2 p)
    {
        if (_backRect.HasPoint(p)) _out.BackId++;
        else if (_clickRect.HasPoint(p))
        {
            _clickFinger = id;
            _out.MouseDown = true;
        }
        else if (_padFinger < 0 && _padRect.HasPoint(p))
        {
            _padFinger = id;
            _padLast = _padStart = p;
            _padDownAt = _now;
            _padMoved = false;
        }
    }

    void PadMove(Vector2 p)
    {
        var d = p - _padLast;
        _padLast = p;
        if (p.DistanceTo(_padStart) > 10) _padMoved = true;
        // Faster swipes go further, slow ones stay precise; the pad's width is about the PC's.
        float gain = Mathf.Clamp(0.8f + d.Length() * 0.06f, 0.8f, 2.6f);
        float w = Mathf.Max(200, _padRect.Size.X);
        _out.CursorX += d.X * gain / w;
        _out.CursorY += d.Y * gain / w;
    }

    void PadUp(int id)
    {
        if (id == _padFinger)
        {
            if (!_padMoved && _now - _padDownAt < 0.35) _out.ClickId++;
            _padFinger = -1;
        }
        if (id == _clickFinger)
        {
            _clickFinger = -1;
            _out.MouseDown = false;
        }
    }

    // ---------------------------------------------------------------- typing an address

    void OpenTyping()
    {
        if (_typed != null) return;
        _typed = new LineEdit
        {
            PlaceholderText = "192.168.1.20",
            VirtualKeyboardType = LineEdit.VirtualKeyboardTypeEnum.NumberDecimal,
            Alignment = HorizontalAlignment.Center,
            Text = _found.Count > 0 ? _found[0].Ip : "",
        };
        _typed.AddThemeFontSizeOverride("font_size", 26);
        if (Px.Big != null) _typed.AddThemeFontOverride("font", Px.Big);
        _typed.TextSubmitted += _ => ConnectTyped();
        AddChild(_typed);
        _typed.GrabFocus();
    }

    void ConnectTyped()
    {
        string ip = _typed?.Text.Trim() ?? "";
        if (!IPAddress.TryParse(ip, out _)) return;
        CloseTyping();
        Pick(ip, "");
    }

    void CloseTyping()
    {
        _typed?.QueueFree();
        _typed = null;
        DisplayServer.VirtualKeyboardHide();
    }

    // ---------------------------------------------------------------- drawing

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        NightBackdrop();
        _exitRect = new Rect2(14, 12, 74, 30);
        if (!Connected)
        {
            _pauseRect = _padRect = _clickRect = _backRect = new Rect2();
            Searching(W, H);
            return;
        }
        Px.Scanlines(this, new Rect2(0, 0, W, H));
        SmallKey(_exitRect, "EXIT", Px.Ink);
        string link = _targetName + (_ping >= 0 ? $" · {Mathf.RoundToInt(_ping)} MS" : "");
        DrawRect(new Rect2(W - 22, 22, 8, 8), Px.Win);
        Px.TextR(this, Px.Small, W - 30, 30, link.ToUpperInvariant(), 8, Px.InkDim);
        if (_view == Wire.View.Controls)
        {
            _pauseRect = new Rect2(W / 2 - 50, 10, 100, 34);
            _padRect = _clickRect = _backRect = new Rect2();
            SmallKey(_pauseRect, "PAUSE", Px.Gold);
            Px.TextC(this, Px.Small, W / 2, H * 0.36f, "GAMENIGHT CONTROLLER", 8, new Color(Px.Ink, 0.18f));
            return;
        }
        // The touchpad.
        _pauseRect = new Rect2();
        _padRect = new Rect2(24, 56, W - 48 - 150, H - 56 - 22);
        _clickRect = new Rect2(W - 24 - 134, H - 22 - 150, 134, 150);
        _backRect = new Rect2(W - 24 - 134, 56, 134, H - 56 - 22 - 150 - 14);
        Px.Frame(this, _padRect, new Color(0, 0, 0, 0.28f), Px.Line2, null, 2, 0);
        Px.TextC(this, Px.Big, _padRect.GetCenter().X, _padRect.GetCenter().Y - 4, "TOUCHPAD", 30, new Color(Px.Ink, 0.22f));
        Px.TextC(this, Px.Small, _padRect.GetCenter().X, _padRect.GetCenter().Y + 18, "SLIDE TO MOVE · TAP TO CLICK", 8, new Color(Px.Ink, 0.3f));
        bool held = _clickFinger >= 0;
        Px.Frame(this, held ? _clickRect.Translated(Vector2.One * 3) : _clickRect, held ? Px.Gold : Px.Glass2, Px.Hex(0xb37400), held ? null : Px.ShadowSoft);
        Px.TextC(this, Px.Big, _clickRect.GetCenter().X + (held ? 3 : 0), _clickRect.GetCenter().Y + 4, "CLICK", 30, held ? Px.Dark : Px.Gold);
        Px.TextC(this, Px.Small, _clickRect.GetCenter().X + (held ? 3 : 0), _clickRect.GetCenter().Y + 22, "HOLD TO DRAG", 7, held ? Px.Dark : Px.InkDim);
        Px.Frame(this, _backRect, Px.Glass2, Px.Line2, Px.ShadowSoft);
        Px.TextC(this, Px.Big, _backRect.GetCenter().X, _backRect.GetCenter().Y + 8, "BACK", 26, Px.Ink);
    }

    void SmallKey(Rect2 r, string label, Color ink)
    {
        Px.Frame(this, r, Px.Glass2, Px.Line2, Px.ShadowSoft, 2, 3);
        Px.TextC(this, Px.Big, r.GetCenter().X, r.GetCenter().Y + 7, label, 20, ink);
    }

    /// <summary>Not connected yet: how it works, what was found, and typing the address.</summary>
    void Searching(float W, float H)
    {
        Px.Scanlines(this, new Rect2(0, 0, W, H));
        BackButton(new Vector2(16, 12), () => Exit?.Invoke());
        Title(new Vector2(70, 42), "PLAY ON PC", 36);
        float x = 70, y = 82;
        foreach (var line in new[]
        {
            "1. ON THE PC, START GAMENIGHT (WINDOWS DOWNLOAD ON THE RELEASES PAGE).",
            "2. KEEP THIS PHONE ON THE SAME WI-FI AS THE PC.",
            "3. IT CONNECTS BY ITSELF. THE PC'S HOME SCREEN SHOWS ITS ADDRESS IF NOT.",
        })
        {
            Px.Text(this, Px.Small, new Vector2(x, y), line, 8, Px.InkDim);
            y += 18;
        }
        y += 14;
        string dots = new string('.', 1 + (int)(_now * 3) % 3);
        string state = _target != null ? $"CONNECTING TO {_targetName}{dots}" : $"LOOKING FOR YOUR PC{dots}";
        Px.Text(this, Px.Big, new Vector2(x, y + 20), state.ToUpperInvariant(), 26, Px.Cyan);
        y += 40;
        // PCs that answered: tap one to use it.
        foreach (var f in _found)
        {
            if (f.Name == "") continue;
            var r = new Rect2(x, y, 300, 34);
            GhostButton("pc" + f.Ip, r, $"{f.Name}  ·  {f.Ip}".ToUpperInvariant(), 20, () => Pick(f.Ip, f.Name), Px.Cyan);
            y += 42;
        }
        if (_typed != null)
        {
            _typed.Position = new Vector2(x, H - 64);
            _typed.Size = new Vector2(260, 40);
            GoldButton("go", new Rect2(x + 272, H - 64, 140, 40), "CONNECT", 24, ConnectTyped);
        }
        else GhostButton("type", new Rect2(x, H - 64, 260, 40), "TYPE THE PC'S ADDRESS", 20, OpenTyping);
    }
}
