using System;
using Godot;
using GameNight.Sim;

namespace GameNight.UI;

/// <summary>
/// The radar at the bottom of the screen (the PWA's minimap.ts): the pitch from above, both
/// teams as dots in their shirt colours, the ball, and the player you control ringed in gold.
/// The same way round as the match camera (home attacking right, the near touchline at the
/// bottom).
/// </summary>
public sealed partial class Minimap : Control
{
    public MatchInfo Info;
    public int HumanTeam;
    public float SafeBottom;

    static readonly Color Grass = new(28 / 255f, 74 / 255f, 40 / 255f, 0.72f);
    static readonly Color Line = new(Style.Ink, 0.55f);
    static readonly Color Frame = new(Style.Ink, 0.25f);
    static readonly Color Dark = Style.Hex(0x0a0c14);

    readonly StyleBoxFlat _bg, _frame;
    readonly float[] _x = new float[MatchSnapshot.N], _z = new float[MatchSnapshot.N];
    readonly bool[] _on = new bool[MatchSnapshot.N];
    readonly byte[] _team = new byte[MatchSnapshot.N];
    readonly bool[] _gk = new bool[MatchSnapshot.N];
    float _bx, _bz, _by;
    int _ctrl = -1;

    public Minimap()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _bg = new StyleBoxFlat { BgColor = Grass, ShadowColor = new Color(0, 0, 0, 0.35f), ShadowSize = 10, ShadowOffset = new Vector2(0, 4), AntiAliasing = true };
        _bg.SetCornerRadiusAll(6);
        _frame = new StyleBoxFlat { DrawCenter = false, BorderColor = Frame, AntiAliasing = true };
        _frame.SetBorderWidthAll(2);
        _frame.SetCornerRadiusAll(7);
    }

    /// <summary>Lum 0..255 (light kits get a dark rim, to stand out on the light lines).</summary>
    static float Lum(int c) => 0.299f * ((c >> 16) & 255) + 0.587f * ((c >> 8) & 255) + 0.114f * (c & 255);

    public void Update(MatchSnapshot a, MatchSnapshot b, float alpha)
    {
        var parent = GetParent<Control>();
        float w = MathF.Round(Math.Clamp(parent.Size.X * 0.14f, 92, 150));
        float h = MathF.Round(w * (float)(Pitch.Width / Pitch.Length));
        Size = new Vector2(w, h);
        Position = new Vector2(MathF.Round((parent.Size.X - w) / 2), parent.Size.Y - 10 - SafeBottom - h);
        for (int i = 0; i < MatchSnapshot.N; i++)
        {
            _on[i] = b.Active[i];
            _x[i] = Mathf.Lerp(a.X[i], b.X[i], alpha);
            _z[i] = Mathf.Lerp(a.Z[i], b.Z[i], alpha);
            _team[i] = b.Team[i];
            _gk[i] = b.Role[i] == Role.GK;
        }
        _bx = Mathf.Lerp(a.BallX, b.BallX, alpha);
        _bz = Mathf.Lerp(a.BallZ, b.BallZ, alpha);
        _by = b.BallY;
        _ctrl = b.Controlled;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Info == null) return;
        float W = Size.X, H = Size.Y;
        var all = new Rect2(Vector2.Zero, Size);
        DrawStyleBox(_bg, all);
        DrawStyleBox(_frame, all.Grow(1.5f));
        float sx = W / (float)Pitch.Length, sz = H / (float)Pitch.Width;
        // Markings: touchlines, halfway line, centre circle, both boxes.
        DrawRect(new Rect2(0.5f, 0.5f, W - 1, H - 1), Line, false, 1);
        DrawLine(new Vector2(W / 2, 0), new Vector2(W / 2, H), Line, 1);
        DrawArc(new Vector2(W / 2, H / 2), (float)Pitch.CircleRadius * sx, 0, MathF.Tau, 32, Line, 1, true);
        float boxW = (float)Pitch.BoxDepth * sx, boxH = (float)Pitch.BoxHalfWidth * 2 * sz;
        DrawRect(new Rect2(0.5f, (H - boxH) / 2, boxW, boxH), Line, false, 1);
        DrawRect(new Rect2(W - boxW - 0.5f, (H - boxH) / 2, boxW, boxH), Line, false, 1);

        Vector2 P(float x, float z) => new(Math.Clamp((x + (float)Pitch.HalfL) * sx, 0, W), Math.Clamp((z + (float)Pitch.HalfW) * sz, 0, H));
        float r = MathF.Max(2.2f, W / 64);
        // The opponents first, so your team draws on top.
        for (int pass = 0; pass < 2; pass++)
        {
            int t = pass == 0 ? 1 - HumanTeam : HumanTeam;
            for (int i = 0; i < MatchSnapshot.N; i++)
            {
                if (!_on[i] || _team[i] != t) continue;
                int rgb = _gk[i] ? Info.GkShirtRgb[t] : Info.ShirtRgb[t];
                bool dark = Lum(rgb) > 150;
                var p = P(_x[i], _z[i]);
                DrawCircle(p, r, Style.Hex(rgb), true, -1, true);
                DrawArc(p, r, 0, MathF.Tau, 16, dark ? new Color(Dark, 0.85f) : new Color(Style.Ink, 0.8f), 1, true);
            }
        }
        // The player you control: a gold ring, no fill.
        if (_ctrl >= 0) DrawArc(P(_x[_ctrl], _z[_ctrl]), r + 2.2f, 0, MathF.Tau, 20, Style.Accent, 1.6f, true);
        // The ball, a little bigger in the air.
        var bp = P(_bx, _bz);
        float br = r * 0.8f + MathF.Min(2, _by * 0.25f);
        DrawCircle(bp, br, Colors.White, true, -1, true);
        DrawArc(bp, br, 0, MathF.Tau, 12, new Color(Dark, 0.9f), 1, true);
    }
}
