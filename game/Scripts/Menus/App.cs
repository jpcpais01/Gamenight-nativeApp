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
    ulong _backAt;

    public override void _Ready()
    {
        Engine.MaxFps = 0;
        DisplayServer.ScreenSetKeepOn(true);
        GetTree().QuitOnGoBack = false;
        GetTree().AutoAcceptQuit = true;
        Px.LoadFonts();
        Kick.PrepareGroundPasses();
        _club = new ClubState(OS.GetUserDataDir());

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
            if (arg == "--screen=notes") _menus.Open(new NotesModal(_menus));
            if (arg == "--screen=drills") PickDrill();
            if (arg == "--screen=player") _menus.OpenPlayer(_club.S.Cards[0]);
            if (arg == "--screen=opening") _menus.Open(new PackOpening(_menus, Packs.All[3], Packs.Open(Packs.All[3], 7)));
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
        Swap(new Main { Request = new MatchRequest { Setup = setup, Seed = seed, Demo = true, Done = _ => CallDeferred(nameof(RestartDemo)) } });
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

    void Kickoff()
    {
        int seed = _menus.NextSeed;
        string ground = Grounds.All.Any(g => g.Id == _club.S.Ground) ? _club.S.Ground : Grounds.All[0].Id;
        var setup = _club.MatchSetup(seed);
        Play(new MatchRequest { Setup = setup, Seed = seed, Ground = ground, Done = o => CallDeferred(nameof(MatchOver), o.Finished, o.Home, o.Away) });
    }

    void Play(MatchRequest req)
    {
        _playing = true;
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

    // ---------------------------------------------------------------- training

    public void PickDrill()
    {
        var items = Drill.All.Select(d => (d.Name, d.About, _club.DrillBest(d.Id) > 0 ? $"BEST STREAK {_club.DrillBest(d.Id)}" : "NO STREAK YET")).ToList();
        OptionsModal box = null;
        box = new OptionsModal(_menus, "At the training ground", "Training", items, i =>
        {
            _menus.Close(box);
            StartDrill(Drill.All[i].Id);
        }, hint: "PAUSE TO RESTART OR LEAVE A DRILL");
        _menus.Open(box);
    }

    void StartDrill(DrillKind kind)
    {
        int seed = (int)(ClubState.Now & 0xffff) + 1;
        Play(new MatchRequest
        {
            Setup = _club.MatchSetup(seed), Seed = seed, Drill = kind, DrillBest = _club.DrillBest(kind),
            Done = o => CallDeferred(nameof(DrillOver), (int)kind, o.DrillBest),
        });
    }

    void DrillOver(int kind, int best)
    {
        _club.SaveDrillBest((DrillKind)kind, best);
        _playing = false;
        _menus.Visible = true;
        _menus.Go(_menus.Home);
        StartDemo();
    }

    // ---------------------------------------------------------------- back

    public override void _Notification(int what)
    {
        if (what != NotificationWMGoBackRequest) return;
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
