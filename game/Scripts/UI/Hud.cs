using System;
using Godot;
using GameNight.Bridge;

namespace GameNight.UI;

/// <summary>
/// Scoreboard, match captions and the frame-rate readout, drawn by the engine in the same
/// frame as the match (no HTML layer). Redraws only when something it shows changes.
/// </summary>
public sealed partial class Hud : Control
{
    static readonly Color Panel = new(0.06f, 0.07f, 0.09f, 0.78f);
    static readonly Color Ink = new(0.957f, 0.937f, 0.89f);
    static readonly Color Home = new(0.784f, 0.224f, 0.231f);
    static readonly Color Away = new(0.945f, 0.922f, 0.863f);

    Font _font;
    string _score = "", _clock = "", _caption = "", _fps = "";
    int _frames;
    double _fpsT, _worst;

    public Hud()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Ready() => _font = ThemeDB.FallbackFont;

    public void Tick(MatchFrame f, double delta)
    {
        bool dirty = false;
        string score = $"{f.Score[0]}  -  {f.Score[1]}";
        string clock = $"{(int)f.Minute}'";
        string caption = f.Phase switch
        {
            MatchPhase.Goal => "GOAL!",
            MatchPhase.HalfTime => "HALF TIME",
            MatchPhase.FullTime => "FULL TIME",
            _ => "",
        };
        if (score != _score || clock != _clock || caption != _caption)
        {
            _score = score;
            _clock = clock;
            _caption = caption;
            dirty = true;
        }

        // Frame rate: average over half a second, plus the slowest frame in it.
        _frames++;
        _fpsT += delta;
        _worst = Math.Max(_worst, delta);
        if (_fpsT >= 0.5)
        {
            float hz = DisplayServer.ScreenGetRefreshRate();
            _fps = $"{_frames / _fpsT:0} fps  {_worst * 1000:0.0} ms" + (hz > 0 ? $"  {hz:0} Hz" : "");
            _frames = 0;
            _fpsT = 0;
            _worst = 0;
            dirty = true;
        }
        if (dirty) QueueRedraw();
    }

    public override void _Draw()
    {
        // Scoreboard, top left: ROS 0 - 0 ATL  12'
        var o = new Vector2(14, 12);
        DrawRect(new Rect2(o, new Vector2(176, 26)), Panel);
        DrawRect(new Rect2(o, new Vector2(4, 26)), Home);
        DrawString(_font, o + new Vector2(12, 18), "ROS", HorizontalAlignment.Left, -1, 13, Ink);
        DrawString(_font, o + new Vector2(48, 18), _score, HorizontalAlignment.Left, -1, 14, Ink);
        DrawString(_font, o + new Vector2(96, 18), "ATL", HorizontalAlignment.Left, -1, 13, Ink);
        DrawRect(new Rect2(o + new Vector2(126, 0), new Vector2(4, 26)), Away);
        DrawString(_font, o + new Vector2(138, 18), _clock, HorizontalAlignment.Left, -1, 13, Ink);

        // Frame rate, top right.
        if (_fps.Length > 0)
        {
            float w = _font.GetStringSize(_fps, HorizontalAlignment.Left, -1, 11).X;
            DrawString(_font, new Vector2(Size.X - w - 14, 24), _fps, HorizontalAlignment.Left, -1, 11, new Color(Ink, 0.8f));
        }

        if (_caption.Length > 0)
        {
            const int size = 34;
            float w = _font.GetStringSize(_caption, HorizontalAlignment.Left, -1, size).X;
            var p = new Vector2((Size.X - w) / 2, Size.Y * 0.36f);
            DrawString(_font, p + new Vector2(2, 3), _caption, HorizontalAlignment.Left, -1, size, new Color(0, 0, 0, 0.6f));
            DrawString(_font, p, _caption, HorizontalAlignment.Left, -1, size, Ink);
        }
    }
}
