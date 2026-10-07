using System;
using GameNight.Sim;

namespace GameNight.Menus;

/// <summary>
/// What the menus ask the match screen (Main) to play, and how it reports back. Main reads it
/// in _Ready; without one it plays a stand-alone match as before.
/// </summary>
public sealed class MatchRequest
{
    public MatchSetup Setup;
    public double Seed = 1;
    /// <summary>A training drill instead of a match.</summary>
    public DrillKind? Drill;
    public int DrillBest;
    /// <summary>Where to play (see Grounds).</summary>
    public string Ground = "big";
    /// <summary>The computer plays both sides behind the home screen: no controls, no HUD.</summary>
    public bool Demo;
    /// <summary>A real match the computer plays for both sides while you watch (a simulated
    /// league fixture): walk-out, HUD, replays and sound, no controls, a skip to full time.</summary>
    public bool Watch;
    /// <summary>The stadium builder's preview: the camera circles the ground (Main.Focus turns it).</summary>
    public bool Showcase;
    /// <summary>Called once when the match ends (full time) or is left.</summary>
    public Action<MatchOutcome> Done;
    /// <summary>An away day (the league): the hosts' crest. The ground dresses in side 1's
    /// colours and crest instead of your club's; null = you're the home side.</summary>
    public GameNight.Club.Crest HostCrest;
    /// <summary>An away day at a ground built from a plan (a league club's own stadium; Ground "custom").</summary>
    public GameNight.Grounds.Build.StadiumPlan HostPlan;
    /// <summary>The hosts' own goal explosion (side 1's); null picks one at random.</summary>
    public int? HostGoalFx;
    public bool AwayDay => HostCrest != null;
    /// <summary>Same-screen 1v1: a human on each side, each with the controllers put on it.</summary>
    public bool Versus;
    /// <summary>An online match: this game hosts it (runs the engine) or is the friend's (draws
    /// the host's frames, side 1).</summary>
    public GameNight.Net.Online Online;
    /// <summary>Online, the friend's screen: the hosts' crest for the ground (they're side 0).</summary>
    public GameNight.Club.Crest HomeCrest;
    /// <summary>Each side's goal explosion, when both are known (online).</summary>
    public int[] GoalFx;
    /// <summary>Online coach mode: the computer plays both sides; each friend manages theirs.</summary>
    public bool Coach;
    /// <summary>Each side's formation id, for the coach's board.</summary>
    public string[] Formations;
    public bool Guest => Online != null && !Online.IsHost;
}

public sealed class MatchOutcome
{
    /// <summary>Played to full time (false: left early).</summary>
    public bool Finished;
    public int Home, Away;
    /// <summary>Training: the best streak, to keep.</summary>
    public int DrillBest;
    /// <summary>Every goal in order: side, shirt slot of the scorer, match minute.</summary>
    public System.Collections.Generic.List<GoalEvent> Goals = new();
}

public sealed class GoalEvent
{
    public int Team, Index, Minute;
    /// <summary>Who scored (the slot may have changed hands through a substitution).</summary>
    public string Name = "";
}

/// <summary>The grounds a match can be played at. The stadium thread adds its grounds here; the
/// menus only offer a choice once there is more than one.</summary>
public static class Grounds
{
    public sealed class Ground
    {
        public string Id = "", Name = "", About = "";
    }

    public static readonly System.Collections.Generic.List<Ground> All = new()
    {
        new Ground { Id = "custom", Name = "Your stadium", About = "Built by you: change it in the stadium builder" },
        new Ground { Id = "big", Name = "The big stadium", About = "Floodlights on, a full house" },
        new Ground { Id = "comunale", Name = "Stadio Comunale", About = "An Italian bowl: the open Curva, spiral towers, umbrella pines" },
        new Ground { Id = "old", Name = "Old Ground", About = "Terraces, the Shed, pylons and the town beyond" },
        new Ground { Id = "training", Name = "Training ground", About = "The club's own: clean, modern, no crowd" },
        new Ground { Id = "bare", Name = "Bare pitch", About = "Just the field: no stands, no crowd" },
    };
}
