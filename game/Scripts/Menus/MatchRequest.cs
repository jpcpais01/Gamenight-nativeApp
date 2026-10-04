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
    /// <summary>The stadium builder's preview: the camera circles the ground (Main.Focus turns it).</summary>
    public bool Showcase;
    /// <summary>Called once when the match ends (full time) or is left.</summary>
    public Action<MatchOutcome> Done;
}

public sealed class MatchOutcome
{
    /// <summary>Played to full time (false: left early).</summary>
    public bool Finished;
    public int Home, Away;
    /// <summary>Training: the best streak, to keep.</summary>
    public int DrillBest;
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
