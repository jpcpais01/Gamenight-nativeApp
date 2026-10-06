using System;
using Godot;
using GameNight.Grounds;
using GameNight.Audio;
using GameNight.Menus;
using GameNight.Net;
using GameNight.Render;
using GameNight.Sim;
using GameNight.UI;

namespace GameNight;

/// <summary>
/// The match screen. The engine (GameNight.Sim, the PWA's match ported to C#) runs on its own
/// thread at a fixed 120 steps a second; every rendered frame reads the last two steps and
/// draws between them, so the picture is smooth at any refresh rate and a slow AI moment can
/// never stall a frame. The frame rate is never capped: it runs at whatever the display does.
/// </summary>
public partial class Main : Node
{
    /// <summary>Set by the menus before the node enters the tree: what to play and how to report
    /// back. Without one this is a stand-alone match, as on the first builds.</summary>
    public MatchRequest Request;

    Match _match;
    MatchRunner _runner;
    Drill _drill;
    DrillHud _drillHud;
    bool _reported;
    readonly MatchSnapshot _prev = new(), _cur = new();

    PixelView _view;
    Ground _ground;
    /// <summary>The match's sound and the terraces' director (the stadium can read Sound.Terraces).</summary>
    public readonly MatchSound Sound = new();
    /// <summary>The goal replay's moment while one is showing (the pitchside people play it back too), else null.</summary>
    public MatchSnapshot ReplayView => _replay.Active ? _replay.B : null;
    MatchCamera _camera;
    Profiler _prof;
    PlayersView _players;
    Officials _officials;
    PitchInvader _invader;
    bool _invaderShown;
    Goals _goals;
    GoalFx _goalFx;
    /// <summary>Each side's goal explosion (yours from the club, theirs at random).</summary>
    readonly int[] _fxStyle = { 0, 1 };
    DeliveryView _delivery;
    TouchControls _controls;
    Hud _hud;
    PauseMenu _pause;
    readonly Replay _replay = new();
    /// <summary>The walk-out before kick-off (the stadium reads Cutscene.Hang for the giant tifo).</summary>
    public readonly Cutscene Cutscene = new();
    Letterbox _letterbox;
    /// <summary>Watching an AI game: the tag and the skip key.</summary>
    WatchBar _watchBar;
    /// <summary>Running the rest of a watched match flat out; a chunk of steps is on the engine thread.</summary>
    bool _skipping;
    volatile bool _chunkBusy;
    /// <summary>Goals scored while skipping (written on the engine thread).</summary>
    readonly System.Collections.Concurrent.ConcurrentQueue<GoalEvent> _skipGoals = new();
    // A 1v1 (same screen or online).
    bool _versus;
    /// <summary>Online: the line to the other game; on the friend's screen this one only draws
    /// the host's frames (_feed) and sends its controls (_netIn).</summary>
    Online _online;
    bool _guest;
    NetFeed _feed;
    readonly NetInput _netIn = new();
    readonly FrameCodec _codec = new();
    /// <summary>Host: events since the last frame sent, the frame going out, and when.</summary>
    readonly MatchSnapshot _netEvents = new(), _netOut = new();
    double _sentAt = -1;
    /// <summary>The away side's controls (the friend's online, or the second player's on the same screen).</summary>
    readonly InputState _home = new(), _away = new();
    readonly TouchControls.Mode[] _modes = new TouchControls.Mode[2];
    readonly int[] _picks = { -1, -1 };
    double _pingMs = -1, _pingAt;
    bool _friendGone, _hostDirectedWas;
    /// <summary>Debug: `-- --screenshot=out.png` saves the screen after a few seconds and quits.</summary>
    string _shotPath;
    double _time, _shotAt = 6;

    public override void _Ready()
    {
        DisplayServer.ScreenSetKeepOn(true);
        MatchSettings.Load();
        MatchSettings.ApplyFpsCap();
        Kick.PrepareGroundPasses();

        _view = new PixelView();
        AddChild(_view);
        World.Build(_view.WorldRoot);
        _ground = Ground.Create(Request?.Ground ?? "big", Request?.Setup, Request?.HostCrest, Request?.HostPlan, Request?.HomeCrest).AddTo(_view.WorldRoot);
        // An away day: the stands' home end is the other side's, for the crowd's sound too.
        Sound.Terraces.Flip = Request?.AwayDay == true;
        Acoustics();
        _players = new PlayersView(_view.WorldRoot);
        _officials = new Officials(_view.WorldRoot);
        _invader = new PitchInvader(_view.WorldRoot) { Cue = beat => PitchInvader.Crowd(Sound.Terraces, beat) };
        _goals = new Goals(_view.WorldRoot);
        _goalFx = new GoalFx(_view.WorldRoot);
        // The demo behind the menus goes off quietly.
        if (Request?.Demo != true) _goalFx.Sound = (style, cue) => GameAudio.Instance?.GoalFx(style, cue);
        _delivery = new DeliveryView(_view.WorldRoot);
        _camera = new MatchCamera(_view.Camera);
        _view.Camera.Far = _ground.ViewRange;
        if (Request?.Showcase == true) _camera.Showcase = 0;

        _hud = new Hud { Name = "Hud", View = _view };
        AddChild(_hud);
        _controls = new TouchControls();
        AddChild(_controls);
        _letterbox = new Letterbox();
        AddChild(_letterbox);
        if (Request?.Watch == true)
        {
            // Nothing to steer: the controls go (still there, unseen and untouchable), the skip key comes.
            _controls.Modulate = new Color(1, 1, 1, 0);
            _controls.ProcessMode = ProcessModeEnum.Disabled;
            _watchBar = new WatchBar();
            _watchBar.Skip += () => _skipping = true;
            AddChild(_watchBar);
            MoveChild(_watchBar, _letterbox.GetIndex());
        }
        _prof = new Profiler { Visible = false };
        AddChild(_prof);
        _prof.Watch(_view.Viewport, GetViewport());
        _prof.SimStepMs = () => _runner?.StepMs ?? 0;
        _prof.AudioBlockMs = () => GameAudio.Instance?.BlockMs ?? 0;
        _prof.SimStepPeak = () =>
        {
            if (_runner == null) return 0;
            double peak = _runner.StepPeak;
            _runner.StepPeak = 0;
            return peak;
        };
        _prof.Context = SpikeContext;
        _online = Request?.Online;
        _guest = Request?.Guest == true;
        _versus = Request?.Versus == true || _online != null;
        if (_guest) _feed = new NetFeed();
        _netEvents.ClearEvents();
        _hud.Side = _guest ? 1 : 0;
        _hud.Remote = _guest;
        _hud.Versus = Request?.Versus == true;
        _letterbox.Skip += () => SkipDirected(true);
        Cutscene.OnCaption = _letterbox.Caption;
        Cutscene.OnSubtitle = _letterbox.Subtitle;
        Cutscene.OnBeat = WalkOutCrowd;
        Cutscene.Away = _camera.Away = Request?.AwayDay == true;
        Cutscene.OnJump = _players.Snap;
        _replay.OnRewind = () =>
        {
            _players.Snap();
            _goalFx.Clear();
        };
        _replay.OnGoal = Explode;
        _replay.OnEvents = f =>
        {
            if (f.Net > 0) _goals.Impact(f.BallX, f.BallY, f.BallZ, f.Net, _time);
            var audio = GameAudio.Instance;
            if (audio == null) return;
            if (f.KickMax > 0) audio.Kick(f.KickMax);
            if (f.Post > 0) audio.Post(f.Post);
            if (f.Net > 0) audio.Net(f.Net);
        };
        _pause = new PauseMenu { CurrentHeight = () => _view.ArtHeight };
        AddChild(_pause);
        _pause.Opened += () => SetPaused(true);
        _pause.Resumed += () => SetPaused(false);
        _pause.Restart += NewMatch;
        _pause.SettingsChanged += ApplySettings;
        if (Request != null && !Request.Demo) _pause.Leave = () => Report(false);
        if (_online != null) _pause.Online = true;
        _pause.WeatherName = () => Atmosphere.Names[(int)_ground.Atmosphere.Weather];
        _pause.CycleWeather = () => Atmosphere.Names[(int)_ground.Atmosphere.Cycle()];
        _pause.SaveReport = SaveReport;
        if (Request?.Demo != true && Request?.Watch != true && Request?.Drill == null && !_versus)
        {
            _pause.Foul = () => _runner?.Invoke(m => m.DebugFoul());
            _pause.Invader = () => _runner?.Invoke(m => m.DebugInvader(0.3));
        }
        ApplySettings();
        if (Request?.Demo == true)
        {
            // Behind the home screen: the computer plays both sides, nothing to touch.
            _hud.Visible = _controls.Visible = _pause.Visible = false;
            GameAudio.Instance?.SetAmbience(1);
            _hud.ProcessMode = _controls.ProcessMode = _pause.ProcessMode = ProcessModeEnum.Disabled;
        }
        if (Request?.Drill != null)
        {
            _pause.Training = true;
            // The drill has its own board; the match HUD stays for the aim and the power bars.
            _hud.OverlayOnly = true;
            _drillHud = new DrillHud();
            AddChild(_drillHud);
            MoveChild(_drillHud, _hud.GetIndex());
        }

        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--screenshot=")) _shotPath = arg["--screenshot=".Length..];
            if (arg.StartsWith("--shot-at=")) _shotAt = double.Parse(arg["--shot-at=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        }

        NewMatch();
        if (_watchBar != null)
        {
            _players.Markers = false;
            // Debug: `-- --skip` runs a watched match straight to full time.
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--skip") >= 0) _skipping = _watchBar.Skipping = true;
        }
        // Debug: `-- --pause` opens the pause menu straight away.
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--pause") >= 0) _pause.Open();
    }

    /// <summary>A fresh match (first launch, or Restart from the pause menu).</summary>
    void NewMatch()
    {
        _runner?.Stop();
        if (Directed) EndDirected();
        _replay.Reset();
        _officials.Reset();
        _invader.Clear(null);
        _goalLog.Clear();
        _logged = _subsDressed = 0;
        _skipping = false;
        if (_watchBar != null) _watchBar.Skipping = false;
        while (_skipGoals.TryDequeue(out _)) { }
        _match = Request != null ? new Match(Request.Seed, Request.Setup) : new Match(seed: DateTime.Now.Ticks % 2147483647);
        PickExplosions();
        if (Request?.Demo == true || Request?.Watch == true) _match.AutoPlay = true;
        _match.Versus = _versus;
        // Names and kits are read before the match's own thread starts.
        _players.SetMatch(_match);
        _hud.SetMatch(_match);
        // (The friend's subs aren't theirs to make online: the engine is on the host's side.)
        _pause.Match = Request?.Drill == null && !_guest ? _match : null;
        _runner = new MatchRunner(_match);
        if (Request?.Drill is DrillKind kind)
        {
            _drill = new Drill(_match, kind, Math.Max(Request.DrillBest, _drill?.Best ?? 0));
            _runner.AfterStep = _drill.Step;
            _drillHud.Drill = _drill;
        }
        _runner.Read(_prev, _cur, out _);
        _runner.Paused = _pause.IsOpen && _online == null;
        // The friend's screen draws the host's match: its own engine only holds the line-ups.
        if (!_guest) _runner.Start();
        // Debug: `-- --invader` sends a fan on a few seconds into the match.
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--invader") >= 0) _runner.Invoke(m => m.DebugInvader(3));
        // A real match opens with the walk-out.
        if (Request?.Demo != true && Request?.Drill == null && (_shotPath == null || Array.IndexOf(OS.GetCmdlineUserArgs(), "--cutscene") >= 0))
        {
            Direct(false);
            Cutscene.Start(_match, _cur, Request?.Ground ?? "big");
            _prewarm = 4;
        }
    }

    void SetPaused(bool on)
    {
        // Online the match goes on behind the menu (the other player is still playing).
        if (_runner != null) _runner.Paused = (on && _online == null) || Directed;
        _hud.Paused = on;
        if (GameAudio.Instance != null) GameAudio.Instance.Suspended = on;
        if (on) _controls.ReleaseAll();
        _controls.SetProcessInput(!on);
        _controls.Visible = !on && !Directed && !_invaderShown;
    }

    /// <summary>The ground through the walk-out (end 0 is the home end).</summary>
    void WalkOutCrowd(Cutscene.Beat beat)
    {
        if (Request?.Ground == "bare") return;
        var t = Sound.Terraces;
        switch (beat)
        {
            case Cutscene.Beat.Emerge:
                // Out of the tunnel: the noise breaks over them and the clapping doesn't stop.
                t.Cue(React.Cheer, 0, 0, 1);
                t.Cue(React.Cheer, 1, 0.25, 0.8f);
                t.Cue(React.Rally, 0, 0.6, 0.9f);
                t.Cue(React.Applause, -1, 0.3, 0.9f, 14);
                break;
            case Cutscene.Beat.HomeLine:
                t.Cue(React.Announce, -1, 0, 0.8f);
                t.Cue(React.Applause, 0, 0, 0.7f, 6);
                break;
            case Cutscene.Beat.HomeName:
                t.Cue(React.Cheer, 0, 0.15, 0.55f);
                break;
            case Cutscene.Beat.AwayLine:
                t.Cue(React.Announce, -1, 0, 0.7f);
                t.Cue(React.Jeer, 0, 0.4, 0.45f, 5);
                t.Cue(React.Applause, 1, 0.2, 0.6f, 5);
                break;
            case Cutscene.Beat.AwayName:
                t.Cue(React.Whistler, 0, 0.1, 0.8f);
                t.Cue(React.Cheer, 1, 0.15, 0.35f);
                break;
            case Cutscene.Beat.Captains:
                t.Cue(React.Applause, -1, 0, 0.6f, 4);
                break;
            case Cutscene.Beat.Ready:
                // Up for the start: both ends in full voice.
                t.Cue(React.Rally, 0, 0, 1);
                t.Cue(React.Rally, 1, 0.8, 0.7f);
                t.Cue(React.Applause, -1, 0, 0.8f, 6);
                break;
        }
    }

    /// <summary>A replay or the walk-out on screen: the match waits.</summary>
    bool Directed => _replay.Active || Cutscene.Active;

    /// <summary>The cut after a goal (or the walk-out): the match waits while it plays.</summary>
    void Direct(bool replay)
    {
        _goalFx.Clear();
        _runner.Paused = true;
        _players.Markers = false;
        _hud.Visible = false;
        _controls.ReleaseAll();
        _controls.Visible = false;
        _letterbox.Open(replay);
    }

    /// <summary>Skipped or done: back to the match.</summary>
    void EndDirected()
    {
        _replay.Finish();
        _goalFx.Clear();
        Cutscene.Cancel();
        _players.Snap();
        _players.Markers = Request?.Watch != true;
        _hud.Visible = Request?.Demo != true;
        _controls.Visible = !_pause.IsOpen;
        _letterbox.Close();
        if (_runner != null) _runner.Paused = _pause.IsOpen && _online == null;
    }

    /// <summary>The walk-out, a replay or a pitch invader skipped (tapped here, or, online, by the
    /// other player: either skips both).</summary>
    void SkipDirected(bool here)
    {
        if (here && _online != null && (Directed || _invader.Holding)) _online.Send(Online.Skip, Array.Empty<byte>());
        if (_invader.Holding)
        {
            _invader.Clear(_runner);
            return;
        }
        if (!Directed) return;
        if (Cutscene.Active)
        {
            Cutscene.Next();
            if (!Cutscene.Active) EndDirected();
        }
        else EndDirected();
    }

    void ApplySettings()
    {
        _camera.BaseDist = MatchCamera.Presets[Math.Clamp(MatchSettings.Camera, 0, 2)];
        _view.TargetHeight = MatchSettings.Pixels > 0 ? MatchSettings.Pixels : 270;
        _view.Smooth = MatchSettings.Smooth;
        _camera.Snap = !MatchSettings.Smooth;
        _hud.ShowFps = MatchSettings.ShowFps;
        _prof.On = MatchSettings.ShowFps && MatchSettings.Profile && Request?.Demo != true;
        GameAudio.Instance?.ApplyVolumes();
        // Fast graphics: the sun casts no shadows (the biggest cost on a weak GPU).
        foreach (var n in _view.WorldRoot.FindChildren("*", nameof(DirectionalLight3D), true, false))
            ((DirectionalLight3D)n).ShadowEnabled = !MatchSettings.Fast;
    }

    public override void _Notification(int what)
    {
        // Backgrounded or covered: the pause menu comes up and the match clock stops.
        if (_runner == null || _shotPath != null || Request?.Demo == true) return;
        if (what == NotificationApplicationPaused || what == NotificationApplicationFocusOut) _pause.Open();
    }

    public override void _ExitTree()
    {
        _runner?.Stop();
        // Left from the pause menu: the sound it stopped comes back for the menus.
        if (Request?.Demo != true && GameAudio.Instance != null)
        {
            GameAudio.Instance.Suspended = false;
            GameAudio.Instance.SetVenue(Venue.Default);
        }
    }

    /// <summary>Skipping a watched match: the engine runs the rest in chunks of steps between
    /// frames (so the score ticks on screen), keeping a note of every goal for the table.</summary>
    void Skip()
    {
        _watchBar.Visible = !Directed;
        if (!_skipping || _chunkBusy || _cur.Phase == Phase.Fulltime) return;
        if (Directed) EndDirected();
        _chunkBusy = true;
        _runner.Invoke(m =>
        {
            var none = new InputState();
            int total = m.Teams[0].Score + m.Teams[1].Score;
            // About a minute of match per chunk.
            for (int i = 0; i < 1200 && m.Phase != Phase.Fulltime; i++)
            {
                m.Step(none);
                m.TakeEvents();
                int now = m.Teams[0].Score + m.Teams[1].Score;
                if (now > total && m.Scorer != null)
                    _skipGoals.Enqueue(new GoalEvent { Team = m.Scorer.Team, Index = m.Scorer.Index, Minute = Math.Max(1, m.DisplayMinute) });
                total = now;
            }
            _chunkBusy = false;
        });
    }

    /// <summary>Tells the menus the match is over (full time, or left from the pause menu).</summary>
    void Report(bool finished)
    {
        if (_reported || Request?.Done == null) return;
        _online?.Dispose();
        _reported = true;
        _runner.Paused = true;
        while (_skipGoals.TryDequeue(out var g)) _goalLog.Add(g);
        _goalLog.Sort((a, b) => a.Minute.CompareTo(b.Minute));
        Request.Done(new MatchOutcome { Finished = finished, Home = _cur.Score[0], Away = _cur.Score[1], DrillBest = _drill?.Best ?? 0, Goals = _goalLog });
    }

    /// <summary>Android back during a match: the pause menu, open or closed.</summary>
    public void Back()
    {
        if (_pause.IsOpen) _pause.Close();
        else _pause.Open();
    }

    /// <summary>The stadium builder: build the ground again (the plan changed).</summary>
    public void RebuildGround()
    {
        var old = _ground;
        old.Root.GetParent()?.RemoveChild(old.Root);
        old.Root.QueueFree();
        _ground = Ground.Create(Request?.Ground ?? "big", Request?.Setup, Request?.HostCrest, Request?.HostPlan, Request?.HomeCrest).AddTo(_view.WorldRoot);
        _view.Camera.Far = _ground.ViewRange;
        Acoustics();
    }

    /// <summary>How the ground sounds: its crowd, its roof, how near the fans are.</summary>
    void Acoustics() => GameAudio.Instance?.SetVenue(Venue.For(Request?.Ground ?? "big", Ground.Club?.S.Stadium?.Sets));

    /// <summary>The stadium builder: swing the camera round to look at a stand (yaw, 0 = from the near side).</summary>
    public void Focus(float yaw)
    {
        _camera?.Face(yaw);
    }

    /// <summary>The stadium builder: the camera dragged round (see <see cref="MatchCamera.Orbit"/>).</summary>
    public void Orbit(float yaw, float tilt, float zoom, Vector2 slide) => _camera?.Orbit(yaw, tilt, zoom, slide);

    /// <summary>The stadium builder's menus hidden: the camera frames the ground in the middle.</summary>
    public void Centre(bool on)
    {
        if (_camera != null) _camera.Centred = on;
    }

    /// <summary>The demo match behind the menus: hidden (and not drawn or stepped) under a full screen.</summary>
    public void Backdrop(bool shown)
    {
        if (_view == null) return;
        ProcessMode = shown ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _view.Visible = shown;
        _view.Viewport.RenderTargetUpdateMode = shown ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (_runner != null) _runner.Paused = !shown;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _prof.Begin();
        _controls.Tick(dt);
        // The keyboard, a gamepad and a phone used as a controller play alongside the touch controls.
        bool live = Request?.Demo != true && Request?.Watch != true && _controls.Visible && !_pause.IsOpen && !Directed && !_invaderShown;
        InputState input;
        if (Request?.Versus == true)
        {
            // Same screen: each controller plays for the side it was put on.
            _modes[0] = ButtonMode(_cur, 0, out _picks[0]);
            _modes[1] = ButtonMode(_cur, 1, out _picks[1]);
            Link.Host.MixVersus(_controls.Input, live, _modes, _picks, _home, _away);
            input = _home;
        }
        else input = Link.Host.Mix(_controls.Input, live, _controls.Current, _controls.Picked);
        // (A phone's own screen still plays its side of a same-screen 1v1, pads or not.)
        bool touchPlays = Request?.Versus == true && !OS.HasFeature("pc") && Link.Host.Instance?.SideOf("keys") >= 0;
        _controls.SelfModulate = Link.Host.Instance?.RemotePlay == true && !touchPlays ? Colors.Transparent : Colors.White;
        float alpha;
        if (_guest)
        {
            OnlineGuest(input);
            if (!_feed.Read(_prev, _cur, out alpha, _time)) _runner.Read(_prev, _cur, out alpha);
        }
        else
        {
            _runner.Submit(input);
            if (_online != null) OnlineHost();
            else if (Request?.Versus == true) _runner.Submit2(_away);
            _runner.Read(_prev, _cur, out alpha);
            if (_online != null) SendFrame();
        }
        if (_watchBar != null) Skip();

        if (_view.Fit())
        {
            _camera.PixelHeight = _view.Viewport.Size.Y;
            _camera.SetAspect(_view.Aspect);
        }
        if (_cur.Goal >= 0) _camera.Bump(0.4f);
        if (_cur.Goal >= 0 && !Directed) Explode(_cur);
        LogGoal();
        if (_cur.Sub != 0 && !_guest) Substituted();
        if (_cur.Post > 0) _camera.Bump(0.6f);
        float run = _pause.IsOpen ? 0 : dt;
        if (_replay.Active)
        {
            _replay.Update(run, _camera);
            if (!_replay.Active) EndDirected();
        }
        if (Cutscene.Active)
        {
            Cutscene.Update(run, _camera);
            if (!Cutscene.Active) EndDirected();
        }
        _prof.Lap(Profiler.Sys.Camera);
        if (Cutscene.Active)
            _players.Update(Cutscene.Frame, Cutscene.Frame, 0, _time, 1);
        else if (_replay.Active)
            _players.Update(_replay.A, _replay.B, _replay.Alpha, _time, 1);
        else
        {
            _camera.Follow = _invader.Focus;
            _camera.Update(_prev, _cur, alpha, run);
            _players.Update(_prev, _cur, alpha, _time, _guest ? 1 : (float)_match.SwitchT);
            if (Request?.Demo != true && Request?.Drill == null)
            {
                _replay.Record(_cur);
                if (!_skipping && _replay.Start(_cur, GoalSeq.Cut)) Direct(true);
            }
        }
        _delivery.Update(_cur);
        // The referee and his assistants (not at training, and off screen during the walk-out).
        _officials.Visible = Request?.Drill == null && !Cutscene.Active;
        if (Request?.Drill == null) _officials.Update(_match, _cur, _time, Directed ? 0 : run);
        if (Request?.Drill == null) _invader.Update(_cur, _runner, _time, Directed ? 0 : run);
        InvaderFrame();
        // The net takes the ball (live, or again on the replay's tape).
        if (!Directed && _cur.Net > 0) _goals.Impact(_cur.BallX, _cur.BallY, _cur.BallZ, _cur.Net, _time);
        _goals.Update(_time);
        _goalFx.Update(run, _time, _view.Camera.GlobalPosition);
        if (_goalFx.Shake > 0)
        {
            _camera.Bump(Math.Min(1, _goalFx.Shake));
            _goalFx.Shake = 0;
        }
        _prof.Lap(Profiler.Sys.Players);
        _ground.Update(_cur, _time, dt);
        _prof.Lap(Profiler.Sys.Stadium);
        Sound.Frame(_match, _cur, Request?.Demo != true, Request?.Drill == null, Request?.Drill != null, _pause.IsOpen ? 0 : dt);
        GameAudio.Instance?.Place(_view.Camera.GlobalPosition.X, Sound.Terraces.Tension);
        _prof.Lap(Profiler.Sys.Sound);
        _view.Present(_camera.SubPixelX, _camera.SubPixelY);

        // Whose buttons the screen shows: the friend's side online; on a shared screen the side it's on.
        int side = _guest ? 1 : Request?.Versus == true ? Math.Max(0, Link.Host.Instance?.SideOf("keys") ?? 0) : 0;
        bool attack = side == 1 ? _cur.HumanAttacking2 : _cur.HumanAttacking;
        _controls.SetMode(ButtonMode(_cur, side, out int picked), picked);
        if (Request?.Versus == true) _hud.Tick(_prev, _cur, alpha, input, attack, delta, _away, _cur.HumanAttacking2);
        else _hud.Tick(_prev, _cur, alpha, input, attack, delta);
        _drillHud?.Tick();
        if (_prewarm > 0) Prewarm(--_prewarm == 0);
        _pause.FoulShown = !Directed;
        _prof.Lap(Profiler.Sys.Hud);
        _prof.End(delta);
        // Full time: a few seconds of the scene, then back to the menus.
        if (Request != null && _cur.Phase == Phase.Fulltime && _cur.PhaseT > (Request.Demo ? 3 : 4.5)) Report(true);

        _time += delta;
        if (_shotPath != null && _time > _shotAt)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            GetTree().Quit();
            _shotPath = null;
        }
    }

    /// <summary>The ball's in: the scorers' explosion goes off in the net (live, or on the replay).</summary>
    void Explode(MatchSnapshot f)
    {
        int team = f.BallX > 0 == f.Dir[0] > 0 ? 0 : 1;
        var kit = Request?.Setup?.Teams?[team]?.Info?.Kit;
        int shirt = kit?.Shirt ?? (team == 0 ? 0xc8393b : 0xf1ebdc), trim = kit?.Shirt2 ?? (team == 0 ? 0xf3ede0 : 0x23345e);
        _goalFx.Fire(_fxStyle[team], f.BallX, f.BallY, f.BallZ, shirt, trim);
    }

    /// <summary>Your club's chosen explosion for your side (team 0); the other side gets its own
    /// (a league host's) or another at random.</summary>
    void PickExplosions()
    {
        if (Request?.GoalFx is { Length: 2 } both)
        {
            _fxStyle[0] = Math.Clamp(both[0], 0, GoalFx.Count - 1);
            _fxStyle[1] = Math.Clamp(both[1], 0, GoalFx.Count - 1);
            return;
        }
        var rng = new Random((int)(DateTime.Now.Ticks & 0x7fffffff));
        bool mine = Request != null && Request.Demo != true && Ground.Club != null;
        _fxStyle[0] = mine ? Math.Clamp(Ground.Club.S.GoalFx, 0, GoalFx.Count - 1) : rng.Next(GoalFx.Count);
        _fxStyle[1] = Request?.HostGoalFx is int host ? Math.Clamp(host, 0, GoalFx.Count - 1) : (_fxStyle[0] + 1 + rng.Next(GoalFx.Count - 1)) % GoalFx.Count;
        // Debug: `-- --goalfx=N` puts style N on both sides.
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--goalfx=") && int.TryParse(a[9..], out int n)) _fxStyle[0] = _fxStyle[1] = Math.Clamp(n, 0, GoalFx.Count - 1);
    }

    /// <summary>A pitch invader on: the cinema bars and a caption (tap to skip), the controls
    /// put away; back as he's walked off.</summary>
    void InvaderFrame()
    {
        bool on = _invader.Holding && Request?.Demo != true;
        if (on == _invaderShown) return;
        _invaderShown = on;
        if (on)
        {
            _controls.ReleaseAll();
            _controls.Visible = false;
            _letterbox.Open(false);
            _letterbox.Caption("Pitch invader!", "The stewards give chase");
        }
        else if (!Directed)
        {
            _controls.Visible = !_pause.IsOpen;
            _letterbox.Close();
        }
    }

    readonly System.Collections.Generic.List<GoalEvent> _goalLog = new();
    int _logged;

    /// <summary>Who scored and when, for the league's scorers and the paper. The goal event and
    /// the scorer can land a frame apart, so it's read off the score and the goal phase.</summary>
    void LogGoal()
    {
        int total = _cur.Score[0] + _cur.Score[1];
        // (Skipping to full time, the engine keeps its own note of the goals.)
        if (_skipping || total <= _logged || _cur.Phase != Phase.Goal || _cur.Scorer < 0) return;
        _logged = total;
        var p = _match.All.Find(x => x.Id == _cur.Scorer);
        if (p == null) return;
        // An own goal goes down to the side that gained it (its striker, as the engine credits it).
        _goalLog.Add(new GoalEvent { Team = p.Team, Index = p.Index, Minute = Math.Max(1, _cur.Minute), Name = p.Name });
    }

    int _subsDressed;

    /// <summary>Substitutes have come on: dress them.</summary>
    void Substituted()
    {
        lock (_match.SubGate)
        {
            for (; _subsDressed < _match.Subs.Count; _subsDressed++)
                _players.Dress(_match, _match.All[_match.Subs[_subsDressed].Id]);
        }
        _players.Flush();
    }

    int _prewarm;

    /// <summary>The first frames of the walk-out draw the match HUD, the controls, the markers
    /// and the officials once, all but invisible, so their shaders compile behind the opening
    /// shot instead of stuttering at kick-off (the PWA's prewarm).</summary>
    void Prewarm(bool done)
    {
        if (!done)
        {
            _hud.Visible = _controls.Visible = _officials.Visible = true;
            _players.Markers = true;
        }
        else
        {
            _hud.Visible = !Directed;
            _controls.Visible = !Directed && !_pause.IsOpen;
            _players.Markers = !Directed;
        }
        _hud.Modulate = _controls.Modulate = new Color(1, 1, 1, done ? 1 : 0.004f);
    }

    /// <summary>The frame-time report into Downloads (and onto the clipboard, to paste anywhere).</summary>
    string SaveReport()
    {
        string version = (string)ProjectSettings.GetSetting("application/config/version", "?");
        var header = $"App {version} · {DateTime.Now:yyyy-MM-dd HH:mm} · ground {Request?.Ground ?? "big"} · weather {Atmosphere.Names[(int)_ground.Atmosphere.Weather]}"
            + $"\nSettings: graphics {(MatchSettings.Fast ? "fast" : "full")} · smooth {(MatchSettings.Smooth ? "on" : "off")} · pixels {_view.ArtHeight} tall (setting {(MatchSettings.Pixels > 0 ? MatchSettings.Pixels.ToString() : "auto")}) · camera {MatchSettings.Camera} · sound {MatchSettings.VolMaster}/{MatchSettings.VolCrowd}/{MatchSettings.VolFx}/{MatchSettings.VolUi}"
            + $"\nMatch: {_cur.ClockLabel} · {_cur.Score[0]}-{_cur.Score[1]} · {SpikeContext()}{(Request?.Drill != null ? $" · training {Request.Drill}" : "")}";
        string text = _prof.Report(header);
        DisplayServer.ClipboardSet(text);
        string name = $"GameNight-report-{version}-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
        foreach (var dir in new[] { OS.GetSystemDir(OS.SystemDir.Downloads), OS.GetUserDataDir() })
        {
            if (string.IsNullOrEmpty(dir)) continue;
            using var f = FileAccess.Open(dir + "/" + name, FileAccess.ModeFlags.Write);
            if (f == null) continue;
            f.StoreString(text);
            return dir == OS.GetUserDataDir() ? "COPIED (PASTE IT IN THE CHAT)" : "SAVED IN DOWNLOADS · ALSO COPIED";
        }
        return "COPIED (PASTE IT IN THE CHAT)";
    }

    /// <summary>What's happening, in a few words, for the frame-time breakdown's spike list.</summary>
    string SpikeContext()
    {
        if (_pause.IsOpen) return "paused";
        if (Cutscene.Active) return "walk-out";
        if (_replay.Active) return "goal replay";
        if (_time < 3) return "first seconds";
        return _cur.Phase switch
        {
            Phase.Goal => "goal",
            Phase.Kickoff => "kick-off",
            Phase.SetPiece => _cur.SetPiece?.ToString().ToLowerInvariant() ?? "set piece",
            Phase.Out => "ball out",
            Phase.Halftime => "half time",
            Phase.Fulltime => "full time",
            _ => "open play",
        };
    }

    /// <summary>What the buttons say (the PWA's main loop): after your goal they pick the
    /// celebration (and show which while it plays), in goal at training they dive, and lining up
    /// your corner or goal kick they name the delivery.</summary>
    TouchControls.Mode ButtonMode(MatchSnapshot s, int side, out int picked)
    {
        picked = -1;
        bool drill = Request?.Drill != null;
        bool attack = side == 1 ? s.HumanAttacking2 : s.HumanAttacking;
        // A 1v1: only the scorer's side celebrates, only the taker's side aims.
        bool mine = s.Scorer >= 0 && s.Team[s.Scorer] == side;
        bool scored = s.Phase == Phase.Goal && mine && (s.Versus || s.HumanScored);
        if (s.CelebrationOpen && mine && !drill) return TouchControls.Mode.Celebrate;
        if (scored && s.Celebration is { } kind && s.PhaseT < s.CelebrationAt + 1.6 && !drill)
        {
            picked = Array.IndexOf(Match.Celebrations, kind);
            return TouchControls.Mode.Celebrate;
        }
        if (s.KeeperButtons && side == 0) return TouchControls.Mode.Keeper;
        bool taker = s.SetPieceTeam == side;
        if (s.AimingCorner && taker) return TouchControls.Mode.Corner;
        if (s.AimingGoalKick && taker) return TouchControls.Mode.GoalKick;
        return attack ? TouchControls.Mode.Attack : TouchControls.Mode.Defend;
    }

    // ---------------------------------------------------------------- online

    /// <summary>The host's side of the line each frame: the friend's controls into the engine,
    /// skips, and what to do when they've gone.</summary>
    void OnlineHost()
    {
        while (_online.TryReceive(out var m))
        {
            if (m.Length == 0) continue;
            if (m[0] == Online.Input)
            {
                uint clock = NetInput.Unpack(m, _away);
                // Their clock straight back now and then, for the ping they see.
                if (_time - _pingAt > 0.5)
                {
                    _pingAt = _time;
                    _online.Send(Online.Pong, BitConverter.GetBytes(clock));
                }
            }
            else if (m[0] == Online.Skip) SkipDirected(false);
            else if (m[0] == Online.Leave) _friendGone = true;
        }
        _runner.Submit2(_away);
        if ((_online.Lost || _friendGone) && _versus)
        {
            // The friend left: the computer takes their side for the rest of the match.
            _versus = false;
            _runner.Invoke(m => m.Versus = false);
            _hud.Note = "YOUR FRIEND LEFT · THE COMPUTER TAKES OVER";
        }
    }

    /// <summary>Host: the newest frame to the friend, at most 30 a second, with every event since the last.</summary>
    void SendFrame()
    {
        NetFeed.Fold(_netEvents, _cur);
        if (!_versus || _time - _sentAt < 1 / 30.0) return;
        _sentAt = _time;
        _netOut.CopyFrom(_cur);
        _netOut.ClearEvents();
        NetFeed.Fold(_netOut, _netEvents);
        _netEvents.ClearEvents();
        _online.SendRaw(_codec.Encode(_netOut, Online.Frame, (byte)(Directed ? 1 : 0)));
    }

    /// <summary>The friend's side each frame: its controls to the host, the host's frames in,
    /// the walk-out and replays kept in step, and the end if the host goes.</summary>
    void OnlineGuest(InputState input)
    {
        var msg = _netIn.Pack(input, _time, (uint)Time.GetTicksMsec());
        if (msg != null) _online.SendRaw(msg);
        while (_online.TryReceive(out var m))
        {
            if (m.Length == 0) continue;
            if (m[0] == Online.Frame) _feed.Add(m, _time);
            else if (m[0] == Online.Pong && m.Length >= 5)
            {
                double ms = (uint)Time.GetTicksMsec() - BitConverter.ToUInt32(m, 1);
                _pingMs = _pingMs < 0 ? ms : _pingMs + (ms - _pingMs) * 0.3;
            }
            else if (m[0] == Online.Skip) SkipDirected(false);
            else if (m[0] == Online.Leave) _friendGone = true;
        }
        // The host's walk-out or replay is over: so is ours.
        if (_hostDirectedWas && !_feed.HostDirected && Directed) SkipDirected(false);
        _hostDirectedWas = _feed.HostDirected;
        bool stalled = _feed.LastFrameAt >= 0 && _time - _feed.LastFrameAt > 1.5;
        _hud.Note = _pingMs >= 0 ? $"ONLINE · {_pingMs:0} MS" + (stalled ? " · WAITING FOR THE HOST" : "") : "ONLINE";
        if ((_online.Lost || _friendGone) && !_reported)
        {
            _hud.Note = "THE HOST LEFT";
            Report(false);
        }
        // Substitutes the host made: dress them here too.
        if (_cur.Sub != 0) GuestSubs();
    }

    /// <summary>Friend's screen: a sub came on at the host's. The frame says who (by number); the
    /// line-ups here say what he looks like.</summary>
    void GuestSubs()
    {
        foreach (var p in _match.All)
        {
            if (p.Id < 0 || p.Id >= MatchSnapshot.N || p.Number == _cur.Number[p.Id] || _cur.Number[p.Id] == 0) continue;
            var on = _match.Bench[p.Team].Find(b => b.Number == _cur.Number[p.Id]);
            if (on != null)
            {
                p.Name = on.Name;
                p.Look = on.Look;
            }
            p.Number = _cur.Number[p.Id];
            _players.Dress(_match, p);
        }
        _players.Flush();
    }
}
