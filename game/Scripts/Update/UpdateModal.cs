using Godot;
using GameNight.Menus;

namespace GameNight.Update;

/// <summary>The UPDATE key's box: the new version, what it brings (its patch notes since yours,
/// scrolling), one button, and how far the update got. Closing it doesn't stop a download (the
/// home key keeps showing the percentage).</summary>
public sealed partial class UpdateModal : Modal
{
    public UpdateModal(Menus.Menus ui) : base(ui)
    {
        ScrollAxis = 1;
    }

    protected override Vector2 BoxSize => new(620, 560);

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
        var lines = Px.Wrap(Px.Small, say, 9, b.Size.X - 48);
        float ty = b.End.Y - 70 - lines.Count * 16;
        foreach (var l in lines)
        {
            Px.Text(this, Px.Small, new Vector2(x, ty), l, 9, up.Now == Updater.Stage.Failed ? Px.Loss : Px.InkDim);
            ty += 16;
        }
        WhatsNew(b, y + 70, b.End.Y - 82 - lines.Count * 16, up.Notes);

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

    /// <summary>The new version's patch notes, scrolling between the heading and the button.</summary>
    void WhatsNew(Rect2 b, float top, float bottom, (string v, string note)[] notes)
    {
        float view = bottom - top;
        DrawRect(new Rect2(b.Position.X + 12, top, b.Size.X - 24, 1), Px.Line);
        DrawRect(new Rect2(b.Position.X + 12, bottom, b.Size.X - 24, 1), Px.Line);
        if (notes == null || notes.Length == 0)
        {
            Content = 0;
            Px.TextC(this, Px.Small, b.GetCenter().X, top + view / 2 + 4, notes == null ? "LOADING WHAT'S NEW..." : "COULDN'T LOAD THE PATCH NOTES", 9, Px.InkDim);
            return;
        }
        float y = top + 20 - Scroll, start = y;
        foreach (var (v, note) in notes)
        {
            if (y + 4 > top && y < bottom) Px.Text(this, Px.Big, new Vector2(b.Position.X + 20, y + 4), "v" + v, 20, Px.Cyan);
            foreach (var l in Px.Wrap(Px.Small, note, 9, b.Size.X - 130))
            {
                if (y - 10 > top && y < bottom) Px.Text(this, Px.Small, new Vector2(b.Position.X + 96, y), l, 9, Px.Ink);
                y += 16;
            }
            y += 12;
        }
        float height = y - start + 8;
        // ScrollMax is measured against the whole screen: give it the list's view instead.
        Content = height + Size.Y - view;
        if (height > view)
        {
            float h = Mathf.Max(24, view * view / height);
            float t = (view - h) * Mathf.Clamp(Scroll / Mathf.Max(1, height - view), 0, 1);
            DrawRect(new Rect2(b.End.X - 10, top + t, 3, h), new Color(Px.Cyan, 0.6f));
        }
    }
}
