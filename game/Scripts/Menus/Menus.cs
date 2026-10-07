using System;
using Godot;
using GameNight.Club;

namespace GameNight.Menus;

/// <summary>
/// Everything outside a match: the home screen and its sub-screens, modal boxes on top, and
/// toasts. The App owns it and hides it while a match is on.
/// </summary>
public sealed partial class Menus : Control
{
    public readonly ClubState Club;
    public readonly App App;
    public readonly HomeScreen Home;
    public readonly SquadScreen Squad;
    public readonly TrainingScreen Training;
    public readonly StoreScreen Store;
    public readonly ClubScreen ClubStudio;
    public readonly StadiumScreen Stadium;
    /// <summary>The league (game/Scripts/League): its state, saved beside the club, and its hub.</summary>
    public readonly global::GameNight.League.LeagueState Season;
    public readonly global::GameNight.League.LeagueScreen League;
    public readonly global::GameNight.League.MapScreen Map;
    /// <summary>The cup run (one at a time) and its screen.</summary>
    public readonly global::GameNight.League.CupState CupRun;
    public readonly global::GameNight.League.CupScreen CupHub;
    readonly Control _modals;
    readonly ToastLayer _toast;
    Control _current;

    /// <summary>Seed of the next match (the opponent previewed on the home screen).</summary>
    public int NextSeed = (int)(ClubState.Now & 0xffff) + 1;

    public Menus(App app, ClubState club)
    {
        App = app;
        Club = club;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Home = Add(new HomeScreen(this));
        Squad = Add(new SquadScreen(this));
        Training = Add(new TrainingScreen(this));
        Store = Add(new StoreScreen(this));
        ClubStudio = Add(new ClubScreen(this));
        Stadium = Add(new StadiumScreen(this));
        Season = new global::GameNight.League.LeagueState(OS.GetUserDataDir(), club);
        League = Add(new global::GameNight.League.LeagueScreen(this));
        Map = Add(new global::GameNight.League.MapScreen(this));
        CupRun = new global::GameNight.League.CupState(OS.GetUserDataDir(), club, Season);
        CupHub = Add(new global::GameNight.League.CupScreen(this));
        _modals = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _modals.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_modals);
        _toast = new ToastLayer();
        AddChild(_toast);
        Go(Home);
    }

    T Add<T>(T screen) where T : Control
    {
        screen.SetAnchorsPreset(LayoutPreset.FullRect);
        screen.Visible = false;
        AddChild(screen);
        return screen;
    }

    /// <summary>True while a full-screen panel hides the match behind the menus.</summary>
    public bool Opaque => _current != Home && _current != Stadium || _modals.GetChildCount() > 0 && _modals.GetChild(_modals.GetChildCount() - 1) is PackOpening;

    public void Go(Control screen)
    {
        if (_current != null) _current.Visible = false;
        _current = screen;
        screen.Visible = true;
        if (screen is PxCanvas c) c.ResetScroll();
        if (screen is SquadScreen s) s.Opened();
        if (screen is global::GameNight.League.LeagueScreen l) l.Opened();
        if (screen is global::GameNight.League.MapScreen m) m.Opened();
        Audio.GameAudio.Instance?.SetAmbience(screen == Home ? 1 : 0.4f);
        App?.BackdropChanged();
    }

    public void Open(PxCanvas modal)
    {
        modal.SetAnchorsPreset(LayoutPreset.FullRect);
        _modals.AddChild(modal);
        App?.BackdropChanged();
    }

    public void Close(PxCanvas modal)
    {
        if (modal.GetParent() == _modals) _modals.RemoveChild(modal);
        modal.QueueFree();
        App?.BackdropChanged();
    }

    public void CloseAll()
    {
        foreach (var c in _modals.GetChildren())
        {
            _modals.RemoveChild(c);
            c.QueueFree();
        }
    }

    public bool HasModal => _modals.GetChildCount() > 0;

    /// <summary>The stadium builder is up (the stadium behind it is the club's own, under construction).</summary>
    public bool Building => _current == Stadium;

    public void Toast(string msg) => _toast.Show(msg);

    /// <summary>Android back: close the top box, else go back a screen. False on the home screen.</summary>
    public bool Back()
    {
        if (HasModal)
        {
            var top = _modals.GetChild(_modals.GetChildCount() - 1);
            if (top is Modal m) m.Dismiss();
            else if (top is PackOpening) { }
            return true;
        }
        if (_current != Home)
        {
            Go(_current == League || _current == CupHub ? Map : Home);
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- shared actions

    public void OpenPlayer(Card c) => Open(new PlayerSheet(this, c));

    public string Version => (string)ProjectSettings.GetSetting("application/config/version", "0.2.0");
}

/// <summary>A short message at the bottom of the screen; never blocks a touch.</summary>
public sealed partial class ToastLayer : Control
{
    string _msg;
    double _t = 99;

    public ToastLayer()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public void Show(string msg)
    {
        _msg = msg;
        _t = 0;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_t > 3) return;
        _t += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_msg == null || _t > 2.8) return;
        // Slides in, holds, slides out.
        float k = (float)Math.Min(1, Math.Min(_t / 0.2, (2.8 - _t) / 0.3));
        k = 1 - (1 - k) * (1 - k);
        if (k <= 0) return;
        float w = Px.Width(Px.Big, _msg, 20) + 36;
        var r = new Rect2(Size.X / 2 - w / 2, Size.Y - 64 + (1 - k) * 20, w, 38);
        Px.Frame(this, r, Px.Hex(0x12103a), Px.Cyan, Px.Shadow);
        Px.TextC(this, Px.Big, r.GetCenter().X, r.GetCenter().Y + 7, _msg, 20, Px.Ink);
    }
}
