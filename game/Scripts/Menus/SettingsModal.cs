using System;
using Godot;
using GameNight.UI;

namespace GameNight.Menus;

/// <summary>
/// Settings from the home screen: the same saved settings as the pause menu (MatchSettings, one
/// copy), so sound and picture can be set before a match, plus the app itself: the version, a
/// check for updates and the patch notes.
/// </summary>
public sealed partial class SettingsModal : Modal
{
    double _checkedAt = -1;

    public SettingsModal(Menus ui) : base(ui) { }

    protected override Vector2 BoxSize => new(760, 370);

    static void Changed()
    {
        MatchSettings.Save();
        Audio.GameAudio.Instance?.ApplyVolumes();
    }

    protected override void PaintBox(Rect2 b)
    {
        Kicker(b.Position + new Vector2(22, 28), "GAMENIGHT");
        Heading(b.Position + new Vector2(22, 60), "Settings");
        float colW = (b.Size.X - 44 - 20) / 2;
        float lx = b.Position.X + 22, rx = lx + colW + 20, top = b.Position.Y + 84;
        DrawRect(new Rect2(lx + colW + 9, top, 2, b.End.Y - 20 - top), Px.Line);

        // Sound: the four bars.
        float y = Section(lx, top, colW, "SOUND");
        y = Volume(lx, y, colW, "MASTER", () => MatchSettings.VolMaster, v => MatchSettings.VolMaster = v);
        y = Volume(lx, y, colW, "CROWD", () => MatchSettings.VolCrowd, v => MatchSettings.VolCrowd = v);
        y = Volume(lx, y, colW, "MATCH", () => MatchSettings.VolFx, v => MatchSettings.VolFx = v);
        y = Volume(lx, y, colW, "MENUS", () => MatchSettings.VolUi, v => MatchSettings.VolUi = v);

        // The app.
        y = Section(lx, y + 8, colW, "APP");
        Px.Text(this, Px.Small, new Vector2(lx, y + 18), "VERSION", 8, Px.InkDim);
        Px.Text(this, Px.Big, new Vector2(lx + 60, y + 21), "V" + Ui.Version, 22, Px.Cyan);
        var up = Update.Updater.Instance;
        var ub = new Rect2(lx + colW - 170, y, 170, 30);
        if (up?.Offer != null)
            GoldButton("update", ub, "UPDATE TO V" + up.Offer, 16, () => Ui.Open(new Update.UpdateModal(Ui)));
        else if (_checkedAt >= 0 && T - _checkedAt < 2.5)
            Px.TextC(this, Px.Small, ub.GetCenter().X, ub.GetCenter().Y + 4, "CHECKING" + new string('.', 1 + (int)(T * 3) % 3), 8, Px.Cyan);
        else
        {
            GhostButton("check", ub, "CHECK FOR UPDATES", 16, () =>
            {
                _checkedAt = T;
                up?.CheckNow();
            });
            if (_checkedAt >= 0) Px.TextR(this, Px.Small, ub.Position.X - 8, ub.GetCenter().Y + 4, up == null ? "NOT ON THIS DEVICE" : "UP TO DATE", 8, Px.Win);
        }
        GhostButton("notes", new Rect2(lx, y + 38, colW, 30), "PATCH NOTES", 16, () => Ui.Open(new NotesModal(Ui)));

        // Picture.
        y = Section(rx, top, colW, "PICTURE");
        y = Option(rx, y, colW, "camera", "MATCH CAMERA", new[] { "CLOSE", "NORMAL", "FAR" }, MatchSettings.Camera, v => MatchSettings.Camera = v);
        y = Option(rx, y, colW, "shadows", "SHADOWS", new[] { "OFF", "ON" }, MatchSettings.Fast ? 0 : 1, v => MatchSettings.Fast = v == 0);
        y = Option(rx, y, colW, "smooth", "SMOOTH PIXELS", new[] { "OFF", "ON" }, MatchSettings.Smooth ? 1 : 0, v => MatchSettings.Smooth = v == 1);
        y = Section(rx, y + 8, colW, "PERFORMANCE");
        int cap = Math.Max(0, Array.IndexOf(MatchSettings.FpsCaps, MatchSettings.FpsCap));
        y = Option(rx, y, colW, "fpscap", "FPS LIMIT", new[] { "60", "90", "120" }, cap, v =>
        {
            MatchSettings.FpsCap = MatchSettings.FpsCaps[v];
            MatchSettings.ApplyFpsCap();
        });
        int fps = !MatchSettings.ShowFps ? 0 : MatchSettings.Profile ? 2 : 1;
        Option(rx, y, colW, "fps", "FPS COUNTER", new[] { "OFF", "ON", "DETAIL" }, fps, v =>
        {
            MatchSettings.ShowFps = v > 0;
            MatchSettings.Profile = v == 2;
        });
    }

    float Section(float x, float y, float w, string name)
    {
        Px.Text(this, Px.Small, new Vector2(x, y + 8), name, 9, Px.Gold);
        float tw = Px.Width(Px.Small, name, 9);
        DrawRect(new Rect2(x + tw + 8, y + 4, w - tw - 8, 1), Px.Line);
        return y + 18;
    }

    /// <summary>A volume bar: ten segments to tap, with - and + at its ends.</summary>
    float Volume(float x, float y, float w, string name, Func<int> get, Action<int> set)
    {
        int v = get();
        Px.Text(this, Px.Small, new Vector2(x, y + 17), name, 8, Px.Ink);
        float bx = x + 70, bw = w - 70 - 92;
        Step("vm" + name, new Rect2(bx, y + 2, 24, 22), "-", () => { set(Math.Max(0, get() - 1)); Changed(); });
        float sx = bx + 30, sw = (bw - 4) / 10f;
        for (int i = 0; i < 10; i++)
        {
            var seg = new Rect2(sx + i * sw, y + 4, sw - 3, 18);
            bool on = i < v;
            DrawRect(seg, on ? (i < 7 ? Px.Cyan : Px.Gold) : Px.Hex(0x2b2670));
            if (on) DrawRect(new Rect2(seg.Position, new Vector2(seg.Size.X, 3)), new Color(1, 1, 1, 0.3f));
            int to = i + 1;
            Tap($"vs{name}{i}", seg.Grow(1.5f), () => { set(get() == to ? to - 1 : to); Changed(); });
        }
        Step("vp" + name, new Rect2(sx + sw * 10 + 4, y + 2, 24, 22), "+", () => { set(Math.Min(10, get() + 1)); Changed(); });
        Px.TextR(this, Px.Big, x + w, y + 20, v == 0 ? "OFF" : (v * 10).ToString(), 18, v == 0 ? Px.Loss : Px.Ink);
        return y + 30;
    }

    void Step(string key, Rect2 r, string s, Action tap)
    {
        bool held = Held(key);
        var rr = held ? r.Translated(Vector2.One * 2) : r;
        Px.Frame(this, rr, Px.Glass2, Px.Line2, held ? null : Px.ShadowSoft, 2, 2);
        Px.TextC(this, Px.Big, rr.GetCenter().X, rr.GetCenter().Y + 7, s, 20, Px.Ink);
        Tap(key, r, tap);
    }

    /// <summary>A row of choices, the current one lit gold.</summary>
    float Option(float x, float y, float w, string key, string name, string[] values, int current, Action<int> pick)
    {
        Px.Text(this, Px.Small, new Vector2(x, y + 17), name, 8, Px.Ink);
        float ox = x + 118, ow = (w - 118 - (values.Length - 1) * 4) / values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            int idx = i;
            var r = new Rect2(ox + i * (ow + 4), y + 2, ow, 22);
            bool on = i == current, held = Held(key + i);
            var rr = held ? r.Translated(Vector2.One * 2) : r;
            Px.Frame(this, rr, on ? Px.Gold : new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.85f), on ? Px.Hex(0xb37400) : Px.Line2, null, 2, 0);
            Px.TextC(this, Px.Small, rr.GetCenter().X, rr.GetCenter().Y + 4, values[i], 8, on ? Px.Dark : Px.Ink);
            Tap(key + i, r, () =>
            {
                pick(idx);
                Changed();
            });
        }
        return y + 30;
    }
}
