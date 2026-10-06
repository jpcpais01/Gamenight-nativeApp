using Godot;
using GameNight.Menus;

namespace GameNight.Update;

/// <summary>The UPDATE key's box: the new version, one button, and how far the update got.
/// Closing it doesn't stop a download (the home key keeps showing the percentage).</summary>
public sealed partial class UpdateModal : Modal
{
    public UpdateModal(Menus.Menus ui) : base(ui) { }

    protected override Vector2 BoxSize => new(500, 250);

    protected override void PaintBox(Rect2 b)
    {
        var up = Updater.Instance;
        if (up == null) return;
        float x = b.Position.X + 24, y = b.Position.Y + 28;
        Kicker(new Vector2(x, y), "New version");
        Heading(new Vector2(x, y + 34), "v" + up.Offer);
        Px.Text(this, Px.Small, new Vector2(x, y + 58), $"YOU HAVE V{Updater.Version} · {up.Size / 1048576f:0} MB DOWNLOAD", 8, Px.InkDim);

        bool android = OS.GetName() == "Android";
        string say = up.Now switch
        {
            Updater.Stage.Downloading => "DOWNLOADING...",
            Updater.Stage.Permission => "ALLOW \"INSTALL UNKNOWN APPS\" FOR GAMENIGHT, THEN COME BACK HERE. IT CARRIES ON BY ITSELF.",
            Updater.Stage.Installing => "HANDING IT TO ANDROID...",
            Updater.Stage.Confirm => "TAP UPDATE ON ANDROID'S BOX. THE GAME CLOSES; OPEN IT AGAIN AFTER.",
            Updater.Stage.Failed => "IT DIDN'T WORK: " + up.Error.ToUpperInvariant(),
            _ => android
                ? "DOWNLOADS IN THE GAME, THEN ANDROID ASKS YOU TO CONFIRM. YOUR CLUB AND SAVES STAY."
                : "DOWNLOADS IN THE GAME, THEN IT CLOSES, SWAPS IN THE NEW FILES AND OPENS AGAIN. YOUR CLUB AND SAVES STAY.",
        };
        float ty = y + 88;
        foreach (var l in Px.Wrap(Px.Small, say, 9, b.Size.X - 48))
        {
            Px.Text(this, Px.Small, new Vector2(x, ty), l, 9, up.Now == Updater.Stage.Failed ? Px.Loss : Px.Ink);
            ty += 16;
        }

        var key = new Rect2(b.GetCenter().X - 130, b.End.Y - 60, 260, 42);
        if (up.Now == Updater.Stage.Downloading)
        {
            // The key becomes the progress bar.
            float k = up.Progress;
            Px.Frame(this, key, new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.85f), Px.Line2, null, 3, 4);
            DrawRect(new Rect2(key.Position + Vector2.One * 4, new Vector2((key.Size.X - 8) * k, key.Size.Y - 8)), Px.Gold);
            Px.TextC(this, Px.Big, key.GetCenter().X, key.GetCenter().Y + 8, $"{k * 100:0}%", 24, k > 0.5f ? Px.Dark : Px.Ink);
        }
        else if (up.Now is Updater.Stage.Available or Updater.Stage.Failed)
            GoldButton("update", key, up.Now == Updater.Stage.Failed ? "TRY AGAIN" : "UPDATE NOW", 24, up.Start);
    }
}
