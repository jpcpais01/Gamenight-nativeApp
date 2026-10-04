namespace GameNight.Menus;

/// <summary>What's new in the native app, newest first (shown from the home screen).</summary>
public static class PatchNotes
{
    public static readonly (string v, string note)[] All =
    {
        ("0.2", "The menus come home: launch splash, home screen, your club, squad and line-ups, the store with pack openings, training drills and full-time results. Your club is saved on the phone."),
        ("0.1", "First native build: the real match engine on its own thread at 120 Hz, an evening pitch, the PWA's camera and controls."),
    };
}
