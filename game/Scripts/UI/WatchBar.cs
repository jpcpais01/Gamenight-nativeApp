using System;
using Godot;
using GameNight.Menus;

namespace GameNight.UI;

/// <summary>
/// Watching an AI game (a simulated league fixture): a tag that says so, bottom left, and a key
/// bottom right that runs the rest of the match flat out to full time.
/// </summary>
public sealed partial class WatchBar : Control
{
    public event Action Skip;
    /// <summary>Running to full time: the key shows it and stops taking taps.</summary>
    public bool Skipping;
    double _t;
    bool _down;

    public WatchBar()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        // The key's touch area, kept over it as the screen resizes.
        var hit = new Control { MouseFilter = MouseFilterEnum.Stop };
        hit.SetAnchorsPreset(LayoutPreset.BottomRight);
        hit.OffsetLeft = -16 - 210;
        hit.OffsetTop = -16 - 46;
        hit.OffsetRight = -16;
        hit.OffsetBottom = -16;
        hit.GuiInput += OnKey;
        AddChild(hit);
    }

    public override void _Process(double delta)
    {
        _t += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        var tag = new Rect2(size.X - 16 - 210 - 12 - 120, size.Y - 16 - 38, 120, 30);
        Px.Frame(this, tag, new Color(0.06f, 0.05f, 0.16f, 0.82f), Px.Cyan, null, 2, 3);
        bool blink = _t % 1.2 < 0.8;
        DrawRect(new Rect2(tag.Position + new Vector2(10, 11), new Vector2(8, 8)), blink ? Px.Loss : new Color(Px.Loss, 0.3f));
        Px.Text(this, Px.Big, tag.Position + new Vector2(26, 22), "AI VS AI", 20, Px.Ink);

        var r = new Rect2(size.X - 16 - 210, size.Y - 16 - 46, 210, 46);
        if (_down && !Skipping) r.Position += new Vector2(2, 2);
        Px.Frame(this, r, Skipping ? new Color(0.2f, 0.18f, 0.3f, 0.9f) : new Color(0.06f, 0.05f, 0.16f, 0.85f), Skipping ? Px.Line2 : Px.Gold, _down ? null : Px.ShadowSoft);
        string label = Skipping ? "TO FULL TIME" + new string('.', (int)(_t * 3 % 4)) : "SKIP TO FULL TIME";
        Px.Text(this, Px.Big, r.Position + new Vector2(14, 31), label, 22, Skipping ? Px.InkDim : Px.Gold);
        if (!Skipping)
            for (int c = 0; c < 2; c++)
                for (int i = 0; i < 4; i++)
                {
                    float x = r.End.X - 28 + c * 8 + i * 2;
                    DrawRect(new Rect2(x, r.GetCenter().Y - 7 + i * 2, 2, 14 - i * 4), Px.Gold);
                }
    }

    void OnKey(InputEvent e)
    {
        if (Skipping) return;
        bool? pressed = e switch
        {
            InputEventScreenTouch t => t.Pressed,
            InputEventMouseButton m when m.ButtonIndex == MouseButton.Left => m.Pressed,
            _ => null,
        };
        if (pressed == true) _down = true;
        else if (pressed == false && _down)
        {
            _down = false;
            Skipping = true;
            Skip?.Invoke();
        }
    }
}
