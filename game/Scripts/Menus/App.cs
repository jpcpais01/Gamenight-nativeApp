using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GameNight.Club;
using GameNight.Sim;

namespace GameNight.Menus;

/// <summary>
/// The app: launch splash, then the menus over a demo match the computer plays against itself
/// (the PWA's live stadium behind the home screen). Play or Training swaps the demo for your
/// match; full time or Leave books the result and brings the menus back. The club is saved in
/// the app's own storage.
/// </summary>
public sealed partial class App : Node
{
    ClubState _club;
    Menus _menus;
    CanvasLayer _layer;
    Main _match;
    bool _playing;
    int _demoKit;
    /// <summary>The demo behind the menus is the stadium builder's preview.</summary>
    bool _showcase;
    ulong _backAt;

    /// <summary>A match (or drill) is on, rather than the menus.</summary>
    public bool Playing => _playing;

    public override void _Ready()
    {
        UI.MatchSettings.Load();
        UI.MatchSettings.ApplyFpsCap();
        DisplayServer.ScreenSetKeepOn(true);
        GetTree().QuitOnGoBack = false;
        GetTree().AutoAcceptQuit = true;
        Px.LoadFonts();
        Kick.PrepareGroundPasses();
        _club = new ClubState(OS.GetUserDataDir());
        global::GameNight.Grounds.Ground.Club = _club;

        _layer = new CanvasLayer { Layer = 10 };
        AddChild(_layer);
        _menus = new Menus(this, _club);
        _layer.AddChild(_menus);
        var splash = new Splash();
        var top = new CanvasLayer { Layer = 20 };
        AddChild(top);
        top.AddChild(splash);
        StartDemo();
        // Debug: `-- --shot=out.png [--screen=squad|store|club|notes|drills]` saves a frame and quits.
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shot=")) _shot = arg["--shot=".Length..];
            if (arg == "--screen=squad") _menus.Go(_menus.Squad);
            if (arg == "--screen=store") _menus.Go(_menus.Store);
            if (arg == "--screen=club") _menus.Go(_menus.ClubStudio);
            if (arg == "--screen=stadium") _menus.Go(_menus.Stadium);
            global::GameNight.League.LeagueScreen.Debug(_menus, arg);
            if (arg.StartsWith("--tab=")) _menus.ClubStudio.Tab = int.Parse(arg[6..]);
            if (arg.StartsWith("--crest="))
            {
                var a = arg[8..].Split('@');
                _menus.ClubStudio.CrestPage(int.Parse(a[0]), int.Parse(a[1]));
                if (a.Length > 2) _club.SetCrest(CrestArt.Random(_club.S.Crest, 0xc8393b, 0x14121c));
            }
            if (arg == "--screen=notes") _menus.Open(new NotesModal(_menus));
            if (arg == "--screen=settings") _menus.Open(new SettingsModal(_menus));
            if (arg == "--screen=drills") PickDrill();
            if (arg == "--screen=player") _menus.OpenPlayer(_club.S.Cards[0]);
            if (arg == "--screen=opening") _menus.Open(new PackOpening(_menus, Packs.All[3], Packs.Open(Packs.All[3], 7)));
            if (arg.StartsWith("--phase="))
            {
                // --phase=name@seconds[@pack]: a held moment of a pack opening.
                var a = arg[8..].Split('@');
                var pk = Packs.All[a.Length > 2 ? int.Parse(a[2]) : 4];
                // The first seed whose best card has two playstyles, to show them off.
                int sd = 11;
                for (int q = 1; q < 3000; q++)
                    if (Playstyles.Of(Packs.Open(pk, q)[^1]).Count >= 2)
                    {
                        sd = q;
                        break;
                    }
                var po = new PackOpening(_menus, pk, Packs.Open(pk, sd));
                _menus.Open(po);
                po.Hold(a[0], double.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    string _shot;
    double _shotT;

    public override void _Process(double delta)
    {
        if (_shot == null) return;
        _shotT += delta;
        if (_shotT < 4) return;
        GetViewport().GetTexture().GetImage().SavePng(_shot);
        GetTree().Quit();
        _shot = null;
    }

    static int KitHash(Kit k) => HashCode.Combine(k.Shirt, k.Shirt2, k.Shorts, k.Pattern);

    /// <summary>A computer-v-computer match behind the menus, your club against the next opponent.</summary>
    void StartDemo()
    {
        int seed = _menus.NextSeed;
        var setup = _club.MatchSetup(seed);
        _demoKit = KitHash(setup.Teams[0].Info.Kit);
        var req = new MatchRequest { Setup = setup, Seed = seed, Demo = true, Done = _ => CallDeferred(nameof(RestartDemo)) };
        if (_showcase)
        {
            req.Ground = "custom:preview";
            req.Showcase = true;
        }
        Swap(new Main { Request = req });
        if (_showcase)
        {
            StadiumFocus(_menus.Stadium.Selected);
            StadiumBare(_menus.Stadium.Bare);
        }
        BackdropChanged();
    }

    void RestartDemo()
    {
        if (!_playing) StartDemo();
    }

    void Swap(Main m)
    {
        if (_match != null)
        {
            RemoveChild(_match);
            _match.QueueFree();
        }
        _match = m;
        AddChild(m);
        MoveChild(m, 0);
    }

    /// <summary>Menus changed what covers the screen: the demo only runs while you can see it.</summary>
    public void BackdropChanged()
    {
        if (_playing || _match == null || _menus == null) return;
        bool shown = !_menus.Opaque;
        // In and out of the stadium builder: its preview replaces the demo, and back.
        if (_menus.Building != _showcase)
        {
            _showcase = _menus.Building;
            CallDeferred(nameof(StartDemo));
            return;
        }
        // Kit edited in the club studio: the demo restarts dressed in it.
        if (shown && KitHash(_club.Info().Kit) != _demoKit)
        {
            CallDeferred(nameof(StartDemo));
            return;
        }
        _match.Backdrop(shown);
    }

    // ---------------------------------------------------------------- matches

    /// <summary>Before kick-off: where to play (only asked once there's a choice).</summary>
    public void PickGround()
    {
        if (Grounds.All.Count <= 1)
        {
            Kickoff();
            return;
        }
        var items = Grounds.All.Select(g => (g.Name, g.About, (string)null)).ToList();
        OptionsModal box = null;
        box = new OptionsModal(_menus, "Before kick-off", "Choose ground", items,
            i => _club.SetGround(Grounds.All[i].Id),
            i => Grounds.All[i].Id == _club.S.Ground,
            "KICK OFF >", () =>
            {
                _menus.Close(box);
                Kickoff();
            });
        _menus.Open(box);
    }

    /// <summary>The stadium builder: the plan changed, build the preview again.</summary>
    public void StadiumChanged()
    {
        if (_showcase && !_playing) _match?.RebuildGround();
    }

    /// <summary>The stadium builder: look at this stand.</summary>
    public void StadiumFocus(GameNight.Grounds.Build.Slot slot)
    {
        if (_showcase && !_playing) _match?.Focus(GameNight.Grounds.Build.Kit.ViewAngle(slot));
    }

    /// <summary>The stadium builder: the camera dragged round the ground.</summary>
    public void StadiumOrbit(float yaw, float tilt, float zoom, Vector2 slide)
    {
        if (_showcase && !_playing) _match?.Orbit(yaw, tilt, zoom, slide);
    }

    /// <summary>The stadium builder's menus hidden or back.</summary>
    public void StadiumBare(bool bare)
    {
        if (_showcase && !_playing) _match?.Centre(bare);
    }

    /// <summary>Straight into a match at this ground.</summary>
    public void PlayAt(string ground)
    {
        _club.SetGround(ground);
        Kickoff();
    }

    void Kickoff()
    {
        int seed = _menus.NextSeed;
        string ground = Grounds.All.Any(g => g.Id == _club.S.Ground) ? _club.S.Ground : Grounds.All[0].Id;
        var setup = _club.MatchSetup(seed);
        Play(new MatchRequest { Setup = setup, Seed = seed, Ground = ground, Done = o => CallDeferred(nameof(MatchOver), o.Finished, o.Home, o.Away) });
    }

    /// <summary>A match set up elsewhere (the league): played, then the menus come back and
    /// `after` gets the outcome.</summary>
    public void PlayFixture(MatchRequest req, Action<MatchOutcome> after)
    {
        req.Done = o => Callable.From(() =>
        {
            _playing = false;
            _menus.NextSeed = (int)(ClubState.Now & 0xffff) + 1;
            _menus.Visible = true;
            after(o);
            StartDemo();
        }).CallDeferred();
        Play(req);
    }

    void Play(MatchRequest req)
    {
        _playing = true;
        _showcase = false;
        _menus.CloseAll();
        _menus.Visible = false;
        Swap(new Main { Request = req });
    }

    void MatchOver(bool finished, int home, int away)
    {
        var setup = _club.MatchSetup(_menus.NextSeed);
        _playing = false;
        _menus.NextSeed = (int)(ClubState.Now & 0xffff) + 1;
        _menus.Visible = true;
        _menus.Go(_menus.Home);
        if (finished)
        {
            var (coins, result) = _club.RecordResult(home, away);
            _menus.Open(new ResultModal(_menus, home, away, setup.Teams[0].Info, setup.Teams[1].Info, coins, result));
        }
        else
        {
            _club.RecordForfeit();
            _menus.Toast("Walked off · booked as a 0-3 defeat");
        }
        StartDemo();
    }

    // ---------------------------------------------------------------- 1v1

    /// <summary>Play a friend online. The host picks the seed and its own ground and sends the
    /// whole set-up; the friend plays it from the host's frames. Nothing is booked to the club.</summary>
    public void PlayOnline(Net.Online o)
    {
        var req = new MatchRequest { Online = o };
        if (o.IsHost)
        {
            int seed = _menus.NextSeed;
            string ground = Grounds.All.Any(g => g.Id == _club.S.Ground) ? _club.S.Ground : Grounds.All[0].Id;
            req.Setup = o.KickOff(seed, ground, out var k);
            req.Seed = seed;
            req.Ground = ground;
            req.GoalFx = new[] { k.Home.GoalFx, k.Away.GoalFx };
        }
        else
        {
            var k = o.Match;
            req.Setup = new MatchSetup { Teams = new[] { k.Home.Restore(), k.Away.Restore() } };
            req.Seed = k.Seed;
            req.Ground = Grounds.All.Any(g => g.Id == k.Ground) ? k.Ground : "big";
            req.HostPlan = k.Plan;
            req.HomeCrest = k.Home.Crest;
            req.GoalFx = new[] { k.Home.GoalFx, k.Away.GoalFx };
        }
        req.Done = r => CallDeferred(nameof(FriendlyOver), r.Finished, r.Home, r.Away, o.IsHost ? 0 : 1);
        Play(req);
    }

    /// <summary>Same-screen 1v1: the controllers picked on the PLAY A FRIEND card, one side each.</summary>
    public void PlayVersus()
    {
        int seed = _menus.NextSeed;
        string ground = Grounds.All.Any(g => g.Id == _club.S.Ground) ? _club.S.Ground : Grounds.All[0].Id;
        Play(new MatchRequest
        {
            Setup = _club.MatchSetup(seed), Seed = seed, Ground = ground, Versus = true,
            Done = r => CallDeferred(nameof(FriendlyOver), r.Finished, r.Home, r.Away, -1),
        });
    }

    void FriendlyOver(bool finished, int home, int away, int side)
    {
        Net.Online.Current?.Dispose();
        Link.Host.Instance?.Serve(false);
        _playing = false;
        _menus.NextSeed = (int)(ClubState.Now & 0xffff) + 1;
        _menus.Visible = true;
        _menus.Go(_menus.Home);
        string score = $"{home}-{away}";
        if (!finished) _menus.Toast($"Match over early · {score}");
        else if (side < 0) _menus.Toast(home == away ? $"A {score} draw" : home > away ? $"Home side wins {score}" : $"Away side wins {score}");
        else
        {
            int mine = side == 0 ? home : away, theirs = side == 0 ? away : home;
            _menus.Toast(mine > theirs ? $"You beat your friend {score}" : mine < theirs ? $"Your friend won {score}" : $"A {score} draw with your friend");
        }
        StartDemo();
    }

    // ---------------------------------------------------------------- training

    public void PickDrill() => _menus.Go(_menus.Training);

    public void StartDrill(DrillKind kind)
    {
        int seed = (int)(ClubState.Now & 0xffff) + 1;
        Play(new MatchRequest
        {
            Setup = _club.MatchSetup(seed), Seed = seed, Drill = kind, DrillBest = _club.DrillBest(kind),
            // Drills run at the training ground once it's built.
            Ground = Grounds.All.Any(g => g.Id == "training") ? "training" : Grounds.All[0].Id,
            Done = o => CallDeferred(nameof(DrillOver), (int)kind, o.DrillBest),
        });
    }

    void DrillOver(int kind, int best)
    {
        _club.SaveDrillBest((DrillKind)kind, best);
        _playing = false;
        _menus.Visible = true;
        _menus.Go(_menus.Training);
        StartDemo();
    }

    // ---------------------------------------------------------------- phone as controller

    Link.ControllerScreen _controller;
    CanvasLayer _controllerLayer;

    /// <summary>Home → PLAY ON PC: the phone becomes a controller for GameNight on a computer.
    /// The menus and the match behind them stop while it's up.</summary>
    public void OpenController()
    {
        if (_controller != null) return;
        _controllerLayer = new CanvasLayer { Layer = 15 };
        AddChild(_controllerLayer);
        _controller = new Link.ControllerScreen { Exit = () => Callable.From(CloseController).CallDeferred() };
        _controllerLayer.AddChild(_controller);
        _menus.Visible = false;
        _match?.Backdrop(false);
    }

    void CloseController()
    {
        if (_controller == null) return;
        _controllerLayer.QueueFree();
        _controller = null;
        _controllerLayer = null;
        _menus.Visible = true;
        BackdropChanged();
    }

    // ---------------------------------------------------------------- back

    public override void _Notification(int what)
    {
        if (what != NotificationWMGoBackRequest) return;
        if (_controller != null)
        {
            _controller.Back();
            return;
        }
        if (_playing)
        {
            _match?.Back();
            return;
        }
        if (_menus.Back()) return;
        // On the home screen: a second back within two seconds closes the app.
        ulong now = Time.GetTicksMsec();
        if (now - _backAt < 2000) GetTree().Quit();
        else
        {
            _backAt = now;
            _menus.Toast("Press back again to quit");
        }
    }
}
