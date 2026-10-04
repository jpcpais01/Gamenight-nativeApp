using Godot;
using GameNight.Sim;

namespace GameNight.Menus;

/// <summary>
/// Training's scoreboard (the PWA's drill HUD): the drill, the tally, the streak and best, and
/// each attempt's verdict as it lands. Read from the drill as it runs on the match thread; the
/// numbers are only ever shown, so a frame-old value is fine.
/// </summary>
public sealed partial class DrillHud : Control
{
    public Drill Drill;
    int _seen;
    Verdict _verdict;
    double _verdictT = 99;

    public DrillHud()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public void Tick()
    {
        if (Drill == null) return;
        _verdictT += GetProcessDeltaTime();
        if (Drill.Verdicts != _seen)
        {
            _seen = Drill.Verdicts;
            _verdict = Drill.Verdict;
            _verdictT = 0;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Drill == null) return;
        Px.LoadFonts();
        string name = System.Array.Find(Drill.All, d => d.Id == Drill.Kind)?.Name ?? "Training";
        var r = new Rect2(14, 10, 250, 58);
        Px.Frame(this, r, new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.82f), Px.Line2, Px.ShadowSoft);
        Px.Text(this, Px.Small, r.Position + new Vector2(12, 18), ("TRAINING · " + name).ToUpperInvariant(), 8, Px.Cyan);
        Px.Text(this, Px.Big, r.Position + new Vector2(12, 46), Drill.Line, 26, Px.Ink);
        string streak = $"STREAK {Drill.Streak} · BEST {Drill.Best}";
        Px.TextR(this, Px.Small, r.End.X - 10, r.Position.Y + 18, Drill.Task.Length > 0 ? Drill.Task.ToUpperInvariant() : "", 8, Px.Gold);
        Px.TextR(this, Px.Small, r.End.X - 10, r.End.Y - 12, streak, 8, Px.InkDim);
        if (_verdict != null && _verdictT < 1.6)
        {
            var col = _verdict.Good == true ? Px.Win : _verdict.Good == false ? Px.Loss : Px.Ink;
            float k = Mathf.Min(1, (float)_verdictT / 0.15f);
            int size = (int)(56 * (0.8f + 0.2f * k));
            Px.TextC(this, Px.Big, Size.X / 2, Size.Y * 0.32f, _verdict.Title.ToUpperInvariant(), size, col, new Color(0, 0, 0, 0.6f), 4);
            if (_verdict.Sub.Length > 0) Px.TextC(this, Px.Small, Size.X / 2, Size.Y * 0.32f + 26, _verdict.Sub.ToUpperInvariant(), 10, Px.Ink, new Color(0, 0, 0, 0.6f), 1);
        }
    }
}
