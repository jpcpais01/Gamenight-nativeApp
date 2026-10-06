using System;
using Godot;

namespace GameNight.UI;

/// <summary>
/// A volume setting in the pause menu: its name, then ten bars rising left to right. Tap a bar
/// or slide along them; the sound follows as you go, and it's saved when you let go. Tapping
/// the name mutes (and brings back what it was).
/// </summary>
public sealed partial class VolumeBar : Control
{
    readonly string _name;
    readonly Func<int> _get;
    readonly Action<int> _set;
    public event Action Changed, Released;
    int _before = 10;
    bool _dragging;

    const int Steps = 10;
    const float BarsW = 84, Pad = 12;

    public VolumeBar(string name, Func<int> get, Action<int> set)
    {
        _name = name;
        _get = get;
        _set = set;
        CustomMinimumSize = new Vector2(0, 36);
        MouseFilter = MouseFilterEnum.Stop;
    }

    float BarsX => Size.X - Pad - BarsW;

    public override void _Draw()
    {
        int v = _get();
        Style.Corners(this, new Rect2(Vector2.Zero, Size), new Color(Style.Ink, _dragging ? 0.12f : 0.06f), 6, 6, 6, 6);
        Style.Text(this, Style.Font(false, 0.6f), _name, new Rect2(Pad, 0, 0, Size.Y), 12, Style.InkDim, false);
        float x0 = BarsX, step = BarsW / Steps, w = step - 2.5f;
        for (int i = 0; i < Steps; i++)
        {
            float h = 6 + 12 * (i + 1) / (float)Steps;
            var r = new Rect2(x0 + i * step, Size.Y / 2 + 9 - h, w, h);
            Style.Corners(this, r, i < v ? Style.Accent : new Color(Style.Ink, 0.14f), 1, 1, 1, 1);
        }
        // Off says so.
        if (v == 0)
        {
            var f = Style.Font(true, 0.5f);
            Style.Text(this, f, "OFF", new Rect2(x0 - 8 - Style.Width(f, "OFF", 13), 0, 0, Size.Y), 13, Style.Ink, false);
        }
    }

    void Pick(float x)
    {
        int v = Math.Clamp((int)MathF.Ceiling((x - BarsX) / (BarsW / Steps)), 0, Steps);
        if (v == _get()) return;
        _set(v);
        Changed?.Invoke();
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed)
            {
                if (mb.Position.X < BarsX - 6)
                {
                    // The name: mute, or back to where it was.
                    int v = _get();
                    if (v > 0) _before = v;
                    _set(v > 0 ? 0 : Math.Max(1, _before));
                    Changed?.Invoke();
                    Released?.Invoke();
                }
                else
                {
                    _dragging = true;
                    Pick(mb.Position.X);
                }
            }
            else if (_dragging)
            {
                _dragging = false;
                Released?.Invoke();
            }
            QueueRedraw();
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion mm && _dragging)
        {
            Pick(mm.Position.X);
            AcceptEvent();
        }
    }
}
