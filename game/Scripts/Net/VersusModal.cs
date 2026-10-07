using System;
using System.Collections.Generic;
using Godot;
using GameNight.Menus;

namespace GameNight.Net;

/// <summary>
/// Home → PLAY A FRIEND. Two ways to play someone: ONLINE (host a match and read out the
/// 4-digit code, or type your friend's code in) and SAME SCREEN (everyone on this device: the
/// keyboard or this phone's screen, gamepads, phones as controllers, each pushed to a side,
/// FIFA-style, with its stick or the arrows).
/// </summary>
public sealed partial class VersusModal : Modal
{
    bool _online = true;
    /// <summary>Host in coach mode (the computer plays, you both manage).</summary>
    bool _coach;
    Online _session;
    string _code = "";
    readonly Dictionary<string, bool> _stickWas = new();

    public VersusModal(Menus.Menus ui) : base(ui)
    {
        // On a phone the controllers can only find it while this is up (a computer always listens).
        Link.Host.Instance?.Serve(true);
    }

    protected override Vector2 BoxSize => new(700, 400);

    public override void Dismiss()
    {
        if (!Closable) return;
        Leave();
        base.Dismiss();
    }

    /// <summary>Hangs up a session that didn't make it to kick-off.</summary>
    void Leave()
    {
        if (_session != null && _session.Now != Online.Stage.Playing) _session.Dispose();
        _session = null;
        if (!OS.HasFeature("pc")) Link.Host.Instance?.Serve(false);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!IsVisibleInTree()) return;
        if (_session != null)
        {
            _session.Poll();
            // The host kicked off: the friend's game goes straight in.
            if (!_session.IsHost && _session.Now == Online.Stage.Playing)
            {
                var s = _session;
                _session = null;
                Ui.Close(this);
                Ui.App.PlayOnline(s);
                return;
            }
        }
        if (!_online) Sides();
    }

    // ---------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsVisibleInTree() || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (_online && _session == null)
        {
            if (k.Keycode >= Key.Key0 && k.Keycode <= Key.Key9) Digit((char)('0' + (k.Keycode - Key.Key0)));
            else if (k.Keycode >= Key.Kp0 && k.Keycode <= Key.Kp9) Digit((char)('0' + (k.Keycode - Key.Kp0)));
            else if (k.Keycode == Key.Backspace) Digit('<');
            else if (k.Keycode is Key.Enter or Key.KpEnter) Join();
            else return;
            GetViewport().SetInputAsHandled();
        }
        else if (!_online && k.Keycode is Key.Left or Key.Right or Key.A or Key.D)
        {
            var h = Link.Host.Instance;
            h?.SetSide("keys", h.SideOf("keys") + (k.Keycode is Key.Left or Key.A ? -1 : 1) is var v && v < -1 ? -1 : Math.Min(v, 1));
            GetViewport().SetInputAsHandled();
        }
    }

    void Digit(char c)
    {
        if (c == '<') _code = _code.Length > 0 ? _code[..^1] : "";
        else if (_code.Length < 4) _code += c;
    }

    void Join()
    {
        if (_code.Length != 4 || !Online.Available) return;
        _session = Online.Join(Ui.Club, _code);
    }

    /// <summary>Same screen: a controller's stick pushed left or right moves it a side that way
    /// (once per push); the phones keep their match controls up meanwhile.</summary>
    void Sides()
    {
        var h = Link.Host.Instance;
        if (h == null) return;
        h.KeepLive();
        foreach (var c in h.Controllers())
        {
            if (c.Id == "keys") continue;
            bool pushed = Math.Abs(c.StickX) > 0.6f;
            _stickWas.TryGetValue(c.Id, out bool was);
            if (pushed && !was) h.SetSide(c.Id, Math.Clamp(h.SideOf(c.Id) + Math.Sign(c.StickX), -1, 1));
            _stickWas[c.Id] = Math.Abs(c.StickX) > 0.3f && (was || pushed);
        }
    }

    // ---------------------------------------------------------------- drawing

    protected override void PaintBox(Rect2 box)
    {
        float x = box.Position.X + 24, y = box.Position.Y + 28;
        Kicker(new Vector2(x, y), "1 v 1");
        Heading(new Vector2(x, y + 34), "Play a friend");
        float cx = x + 250;
        float w = Chip("tab-online", new Vector2(cx, y + 10), "ONLINE", _online, () => _online = true);
        Chip("tab-local", new Vector2(cx + w + 8, y + 10), "SAME SCREEN", !_online, () =>
        {
            if (_session != null) Leave();
            Link.Host.Instance?.Serve(true);
            _online = false;
        });
        var body = new Rect2(x, y + 56, box.End.X - 24 - x, box.End.Y - 24 - (y + 56));
        if (_online) OnlinePage(body);
        else LocalPage(body);
    }

    void Line(ref float y, float x, float w, string s, Color c, int size = 8)
    {
        foreach (var l in Px.Wrap(Px.Small, s, size, w))
        {
            Px.Text(this, Px.Small, new Vector2(x, y), l, size, c);
            y += size + 7;
        }
        y += 4;
    }

    /// <summary>The host's pick: play the match, or manage while the computer plays it.</summary>
    void Modes(Vector2 p, float w, bool coach, Action<bool> set)
    {
        float cw = Chip("mode-play", p, "PLAY 1V1", !coach, () => set(false), 16, 24);
        Chip("mode-coach", new Vector2(p.X + cw + 8, p.Y), "COACH 1V1", coach, () => set(true), 16, 24);
        Px.Text(this, Px.Small, new Vector2(p.X, p.Y + 38), Px.Fit(Px.Small, coach
            ? "THE COMPUTER PLAYS; YOU BOTH MANAGE: SUBS, FORMATION, MENTALITY."
            : "YOU EACH CONTROL YOUR OWN TEAM.", 8, w), 8, Px.InkDim);
    }

    void OnlinePage(Rect2 r)
    {
        float x = r.Position.X, y = r.Position.Y;
        if (!Online.Available)
        {
            float ly = y + 30;
            Line(ref ly, x, r.Size.X, "ONLINE PLAY ISN'T SWITCHED ON IN THIS BUILD YET: IT NEEDS THE ONLINE SERVER, WHICH THE NEXT BUILD SETS UP ONCE IT'S CONNECTED.", Px.InkDim, 9);
            Line(ref ly, x, r.Size.X, "SAME SCREEN WORKS NOW.", Px.Cyan, 9);
            return;
        }
        if (_session == null)
        {
            // Host on the left; the code pad on the right.
            var hostR = new Rect2(x, y, r.Size.X * 0.42f, r.Size.Y);
            Px.Frame(this, hostR, Px.Glass, Px.Line2, null, 2, 0);
            float hy = hostR.Position.Y + 30;
            Px.Text(this, Px.Big, new Vector2(hostR.Position.X + 16, hy), "HOST", 28, Px.Gold);
            hy += 18;
            Line(ref hy, hostR.Position.X + 16, hostR.Size.X - 32, "YOUR GAME RUNS THE MATCH AND GETS A 4-DIGIT CODE. READ IT TO YOUR FRIEND: THEY TYPE IT IN ON THEIR PHONE OR PC.", Px.Ink);
            Modes(new Vector2(hostR.Position.X + 16, hostR.End.Y - 104), hostR.Size.X - 32, _coach, on => _coach = on);
            GoldButton("host", new Rect2(hostR.Position.X + 16, hostR.End.Y - 60, hostR.Size.X - 32, 44), "HOST A MATCH", 24, () =>
            {
                _session = Online.Host(Ui.Club);
                _session.CoachMode = _coach;
            });

            var joinR = new Rect2(hostR.End.X + 16, y, r.End.X - hostR.End.X - 16, r.Size.Y);
            Px.Frame(this, joinR, Px.Glass, Px.Line2, null, 2, 0);
            Px.Text(this, Px.Big, new Vector2(joinR.Position.X + 16, joinR.Position.Y + 30), "JOIN", 28, Px.Cyan);
            // The four boxes.
            float bw = 40, bx = joinR.Position.X + 16, by = joinR.Position.Y + 42;
            for (int i = 0; i < 4; i++)
            {
                var b = new Rect2(bx + i * (bw + 8), by, bw, 50);
                Px.Frame(this, b, new Color(0, 0, 0, 0.45f), i == _code.Length ? Px.Cyan : Px.Line2, null, 2, 0);
                if (i < _code.Length) Px.TextC(this, Px.Big, b.GetCenter().X, b.GetCenter().Y + 13, _code[i].ToString(), 38, Px.Ink);
            }
            // The keypad.
            float kx = bx + 4 * (bw + 8) + 12, kw = joinR.End.X - 16 - kx, kh = (joinR.Size.Y - 32) / 4 - 6;
            string keys = "123456789<0>";
            for (int i = 0; i < 12; i++)
            {
                char c = keys[i];
                var k = new Rect2(kx + i % 3 * (kw / 3), joinR.Position.Y + 16 + i / 3 * (kh + 6), kw / 3 - 6, kh);
                string label = c == '<' ? "DEL" : c == '>' ? "JOIN" : c.ToString();
                if (c == '>') GoldButton("key>", k, label, 18, Join, _code.Length == 4);
                else GhostButton("key" + c, k, label, c == '<' ? 16 : 22, () => Digit(c));
            }
            float jy = by + 74;
            Line(ref jy, bx, kx - bx - 10, "YOUR FRIEND'S CODE. YOU PLAY WITH YOUR OWN CLUB.", Px.InkDim);
            return;
        }

        var s = _session;
        float sy = y + 26;
        if (s.Now == Online.Stage.Failed)
        {
            Px.Text(this, Px.Big, new Vector2(x, sy), s.Problem, 28, Px.Loss);
            sy += 22;
            Line(ref sy, x, r.Size.X, s.IsHost ? "OPEN A NEW GAME TO GET A NEW CODE." : "CHECK THE CODE WITH YOUR FRIEND AND TRY AGAIN.", Px.InkDim, 9);
            GhostButton("again", new Rect2(x, r.End.Y - 48, 180, 40), "BACK", 22, () =>
            {
                _session.Dispose();
                _session = null;
                _code = "";
            });
            return;
        }
        if (s.IsHost)
        {
            Px.Text(this, Px.Small, new Vector2(x, sy), "YOUR CODE", 9, Px.InkDim);
            Px.Text(this, Px.Big, new Vector2(x, sy + 62), s.Now == Online.Stage.Connecting ? "····" : s.Code, 72, Px.Gold, new Color(0, 0, 0, 0.5f), 3);
            float ty = sy + 96;
            string state = s.Now switch
            {
                Online.Stage.Connecting => "OPENING THE GAME...",
                Online.Stage.Waiting => "TELL YOUR FRIEND THIS CODE. THEY TAP PLAY A FRIEND > ONLINE AND TYPE IT IN.",
                _ => "YOUR FRIEND IS IN!",
            };
            Line(ref ty, x, r.Size.X * 0.5f, state, s.Now == Online.Stage.Ready ? Px.Win : Px.Ink, 9);
            Modes(new Vector2(x, r.End.Y - 100), r.Size.X * 0.5f, s.CoachMode, on => s.CoachMode = _coach = on);
            if (s.Now == Online.Stage.Ready && s.Friend?.Team?.Info is { } info)
            {
                var cr = new Rect2(r.End.X - 150, r.Position.Y + 6, 110, 136);
                CrestArt.Draw(this, cr, s.Friend.Crest ?? new Club.Crest());
                Px.TextC(this, Px.Big, cr.GetCenter().X, cr.End.Y + 26, Px.Fit(Px.Big, info.Name, 22, 220), 22, Px.Ink);
                Px.TextC(this, Px.Small, cr.GetCenter().X, cr.End.Y + 44, "YOUR OPPONENT", 8, Px.InkDim);
            }
            GoldButton("kickoff", new Rect2(x, r.End.Y - 52, 240, 46), "KICK OFF  >", 26, () =>
            {
                var go = _session;
                _session = null;
                Ui.Close(this);
                Ui.App.PlayOnline(go);
            }, s.Now == Online.Stage.Ready);
            GhostButton("cancel", new Rect2(x + 256, r.End.Y - 52, 130, 46), "CANCEL", 22, () => Leave());
        }
        else
        {
            Px.Text(this, Px.Small, new Vector2(x, sy), "JOINING", 9, Px.InkDim);
            Px.Text(this, Px.Big, new Vector2(x, sy + 62), s.Code, 72, Px.Cyan, new Color(0, 0, 0, 0.5f), 3);
            float ty = sy + 96;
            Line(ref ty, x, r.Size.X, s.Now == Online.Stage.Connecting ? "CONNECTING..." : "YOU'RE IN! WAITING FOR YOUR FRIEND TO KICK OFF.", s.Now == Online.Stage.Waiting ? Px.Win : Px.Ink, 9);
            GhostButton("cancel", new Rect2(x, r.End.Y - 52, 130, 46), "CANCEL", 22, () => Leave());
        }
    }

    void LocalPage(Rect2 r)
    {
        var h = Link.Host.Instance;
        if (h == null) return;
        var list = h.Controllers();
        float x = r.Position.X, y = r.Position.Y;
        float colW = r.Size.X / 3;
        var club = Ui.Club.Info();
        var opp = Ui.Club.OpponentInfo();
        Px.TextC(this, Px.Big, x + colW / 2, y + 14, Px.Fit(Px.Big, club.Name.ToUpperInvariant(), 20, colW - 10), 20, Px.Gold);
        Px.TextC(this, Px.Small, x + colW / 2, y + 28, "HOME", 8, Px.InkDim);
        Px.TextC(this, Px.Big, x + colW * 2.5f, y + 14, Px.Fit(Px.Big, opp.Name.ToUpperInvariant(), 20, colW - 10), 20, Px.Cyan);
        Px.TextC(this, Px.Small, x + colW * 2.5f, y + 28, "AWAY", 8, Px.InkDim);
        Px.TextC(this, Px.Small, x + colW * 1.5f, y + 28, "NOT PLAYING", 8, Px.InkDim);
        float ry = y + 40;
        bool home = false, away = false;
        foreach (var c in list)
        {
            int side = h.SideOf(c.Id);
            home |= side == 0;
            away |= side == 1;
            int col = side == 0 ? 0 : side == 1 ? 2 : 1;
            var chip = new Rect2(x + col * colW + 8, ry, colW - 16, 30);
            var ink = side == 0 ? Px.Gold : side == 1 ? Px.Cyan : Px.Ink;
            Px.Frame(this, chip, new Color(0, 0, 0, 0.45f), new Color(ink, 0.8f), null, 2, 0);
            Px.TextC(this, Px.Big, chip.GetCenter().X, chip.GetCenter().Y + 7, c.Name, 20, ink);
            string id = c.Id;
            if (side > -1) Arrow("l" + id, new Rect2(chip.Position.X + 2, ry + 2, 26, 26), "<", () => h.SetSide(id, side - 1));
            if (side < 1) Arrow("r" + id, new Rect2(chip.End.X - 28, ry + 2, 26, 26), ">", () => h.SetSide(id, side + 1));
            ry += 36;
            if (ry > r.End.Y - 110) break;
        }
        float ty = r.End.Y - 96;
        string phones = h.Listening
            ? $"MORE PLAYERS: PLUG IN A GAMEPAD, OR A PHONE AS CONTROLLER: THE APP'S PLAY ON PC, OR ANY PHONE'S BROWSER AT {h.WebAddress.Replace("http://", "")} · CODE {h.Code}"
            : "MORE PLAYERS: PLUG IN A GAMEPAD.";
        Line(ref ty, x, r.Size.X - 260, phones, Px.InkDim);
        Line(ref ty, x, r.Size.X - 260, "PUSH A STICK (OR TAP THE ARROWS, OR THE KEYBOARD'S ARROWS) TO PICK A SIDE.", Px.InkDim);
        if (h.Listening) GhostButton("qr", new Rect2(r.End.X - 250, r.End.Y - 46, 70, 40), "QR", 20, () => Ui.Open(new Link.PhoneModal(Ui)));
        GoldButton("local", new Rect2(r.End.X - 170, r.End.Y - 46, 170, 46), "KICK OFF  >", 24, () =>
        {
            Ui.Close(this);
            Ui.App.PlayVersus();
        }, home && away);
    }

    void Arrow(string key, Rect2 r, string s, Action tap)
    {
        bool held = Held(key);
        Px.Frame(this, held ? r.Translated(Vector2.One) : r, Px.Glass2, Px.Line2, null, 2, 0);
        Px.TextC(this, Px.Big, r.GetCenter().X, r.GetCenter().Y + 7, s, 20, Px.Ink);
        Tap(key, r, tap);
    }
}
