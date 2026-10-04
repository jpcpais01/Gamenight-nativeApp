namespace GameNight.Menus;

/// <summary>What's new in the native app, newest first (shown from the home screen).</summary>
public static class PatchNotes
{
    public static readonly (string v, string note)[] All =
    {
        ("0.4", "Opening packs is a show now: a lit vault, the pack cracking and tearing open, cards charging up and flipping with foil shine, a lights-out walkout for Legendary and Icon cards, and reveal-all dealing the rest out. The store glows too."),
        ("0.3", "Make the club yours: a crest maker (shapes, fields, emblems, lettering, stars), your own pictures for the tifos, the stand banner's words and colour, and your manager's look and temper. The crest now shows on the home screen, the shirt and the results."),
        ("0.2", "The menus come home: launch splash, home screen, your club, squad and line-ups, the store with pack openings, training drills and full-time results. Your club is saved on the phone."),
        ("0.1", "First native build: the real match engine on its own thread at 120 Hz, an evening pitch, the PWA's camera and controls."),
    };
}
