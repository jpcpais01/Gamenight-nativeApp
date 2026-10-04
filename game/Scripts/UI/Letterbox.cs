using System;
using Godot;

namespace GameNight.UI;

/// <summary>
/// The cinema look the PWA's cutscenes and replays share (.cutscene in style.css): black bars
/// that grow in top and bottom, a caption sliding in bottom left on a gold edge, "Tap to skip"
/// bottom right, and for a replay a blinking red dot and REPLAY in the top corner. A tap
/// anywhere skips.
/// </summary>
public sealed partial class Letterbox : Control
{
    public event Action Skip;

    double _t, _capT = -1;
    bool _replay;
    string _title = "", _sub = "";

    public Letterbox()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
    }

    /// <summary>Bars in (replay: with the REPLAY tag).</summary>
    public void Open(bool replay)
    {
        _replay = replay;
        _t = 0;
        _capT = -1;
        Visible = true;
        QueueRedraw();
    }

    public void Close() => Visible = false;

    /// <summary>A caption: a big title over a spaced subtitle (empty title: none).</summary>
    public void Caption(string title, string sub)
    {
        _title = title?.ToUpperInvariant() ?? "";
        _sub = sub?.ToUpperInvariant() ?? "";
        _capT = string.IsNullOrEmpty(_title) ? -1 : 0;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventScreenTouch { Pressed: true } || e is InputEventMouseButton { Pressed: true })
        {
            AcceptEvent();
            Skip?.Invoke();
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _t += delta;
        if (_capT >= 0) _capT += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        float bar = size.Y * 0.09f * Style.EaseOut((float)(_t / 0.5));
        DrawRect(new Rect2(0, 0, size.X, bar), Colors.Black);
        DrawRect(new Rect2(0, size.Y - bar, size.X, bar), Colors.Black);
        float full = size.Y * 0.09f;

        // Slides in from the left and fades up (cs-cap).
        float In(double t, double dur) => Style.EaseOut((float)(t / dur));

        if (_replay)
        {
            float k = In(_t, 0.4);
            int fs = (int)Math.Clamp(size.X * 0.026f, 16, 24);
            var f = Style.Font(true, fs * 0.14f);
            float x = size.X * 0.04f - 16 * (1 - k), y = full + 12;
            var col = new Color(Style.Ink, k);
            if (((int)(_t * 2) & 1) == 0)
            {
                float d = fs * 0.5f;
                DrawCircle(new Vector2(x + d / 2, y + fs * 0.55f), d / 2, new Color(0xe8322eff) { A = k });
            }
            DrawString(f, new Vector2(x + fs * 0.95f, y + f.GetAscent(fs)), "REPLAY", HorizontalAlignment.Left, -1, fs, col);
        }

        if (_capT >= 0)
        {
            float k = In(_capT, 0.6);
            int tfs = (int)Math.Clamp(size.X * 0.046f, 26, 46), sfs = (int)Math.Clamp(size.X * 0.02f, 13, 19);
            var tf = Style.Font(true, tfs * 0.05f);
            var sf = Style.Font(false, sfs * 0.18f);
            float w = MathF.Max(Style.Width(tf, _title, tfs), Style.Width(sf, _sub, sfs)) + 32;
            float h = tfs + 4 + sfs + 14;
            float x = size.X * 0.05f - 16 * (1 - k), y = size.Y - full - 18 - h;
            // The gold edge, and a dark fade behind the words.
            for (int i = 0; i < 16; i++)
            {
                float a = 0.85f * (1 - i / 16f) * k;
                DrawRect(new Rect2(x + 4 + w * i / 16f, y, w / 16f + 1, h), new Color(8 / 255f, 12 / 255f, 22 / 255f, a));
            }
            DrawRect(new Rect2(x, y, 4, h), new Color(Style.Accent, k));
            DrawString(tf, new Vector2(x + 20, y + 6 + tf.GetAscent(tfs) * 0.92f), _title, HorizontalAlignment.Left, -1, tfs, new Color(Style.Ink, k));
            DrawString(sf, new Vector2(x + 20, y + 6 + tfs + 4 + sf.GetAscent(sfs)), _sub, HorizontalAlignment.Left, -1, sfs, new Color(Style.InkDim, Style.InkDim.A * k));
        }

        {
            float k = In(_t - 0.8, 0.6);
            if (k > 0)
            {
                const int fs = 13;
                var f = Style.Font(true, fs * 0.16f);
                string s = "TAP TO SKIP ›";
                float w = Style.Width(f, s, fs);
                DrawString(f, new Vector2(size.X * 0.96f - w + 16 * (1 - k), size.Y - full - 14), s, HorizontalAlignment.Left, -1, fs, new Color(1, 1, 1, 0.6f * k));
            }
        }
    }
}
