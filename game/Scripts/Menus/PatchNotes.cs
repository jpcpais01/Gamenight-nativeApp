namespace GameNight.Menus;

/// <summary>What's new in the native app, newest first (shown from the home screen).</summary>
public static class PatchNotes
{
    public static readonly (string v, string note)[] All =
    {
        ("0.16", "Build your own stadium: tap STADIUM on the home screen and pick a stand for each side, end and corner from five sets (Arena, Terrace, Curva, The Wall, Citadel). Any mix joins up, the stadium builds behind you as you pick, and Your stadium is a ground to play at."),
        ("0.15", "With FPS DETAIL on, the pause menu has SAVE PERFORMANCE REPORT: a small text file in Downloads (and copied, ready to paste) with your phone, settings, every spike and its cause, and the last ten minutes of frame timings."),
        ("0.14", "FPS DETAIL now catches spikes: a live frame-time graph, and for each hitch what caused it (a garbage collection, a shader compiling, the GPU, a part of the game, or Android itself), how many ms it cost and what was happening in the match."),
        ("0.13", "Pick the weather in the pause menu (MATCH: EVENING, SUNNY DAY or RAINY NIGHT); it switches at once and is remembered. In the rain the pitch soaks dark with standing water that catches the floodlights, the rain hisses, and thunder rolls after each flash."),
        ("0.12", "See where the phone's power goes: tap FPS COUNTER in the pause menu twice for DETAIL. It shows the frame time, GPU against CPU, the engine's step, draw calls, and a ranked bar for each part of the game."),
        ("0.11", "Sound no longer stays off after you leave a match from the pause menu. The goal nets have smaller holes, in a softer grey, and still show from the match camera."),
        ("0.10", "The goal nets now look like real nets in a match: a diamond mesh of cord you can see from the normal camera, not a faint haze, still bulging and rippling when the ball hits them."),
        ("0.9", "Shot aim fixed from wide: the stick now picks the post it points at as seen from the ball, so cutting in from the wing you can go near post by pointing at it. Stick idle always means far post."),
        ("0.8", "Each download now carries its version in the file name (GameNight-0.8.x.apk) and still installs as an update over the one you have."),
        ("0.7", "The buttons change with the moment, like the browser game: gold celebration moves after you score (the one you pick lights up), Whip, Short, Float on your corners, Drive, Short, Float on goal kicks, and Dive in goal at training."),
        ("0.6", "A referee and two linesmen run the match: the ref points for fouls and free kicks, the linesmen flag offsides and throw-ins. Real goals with round posts and a cord net that bulges and ripples when the ball hits it. The pitch lines no longer flicker."),
        ("0.5", "Far more crest options: 19 shapes, 24 fields, patterns, 32 emblems, new borders, low-band and monogram lettering, a pixel face. Surprise me now picks matching colours too."),
        ("0.4", "Opening packs is a show now: a lit vault, the pack cracking and tearing open, cards charging up and flipping with foil shine, a lights-out walkout for Legendary and Icon cards, and reveal-all dealing the rest out. The store glows too."),
        ("0.3", "Make the club yours: a crest maker (shapes, fields, emblems, lettering, stars), your own pictures for the tifos, the stand banner's words and colour, and your manager's look and temper. The crest now shows on the home screen, the shirt and the results."),
        ("0.2", "The menus come home: launch splash, home screen, your club, squad and line-ups, the store with pack openings, training drills and full-time results. Your club is saved on the phone."),
        ("0.1", "First native build: the real match engine on its own thread at 120 Hz, an evening pitch, the PWA's camera and controls."),
    };
}
