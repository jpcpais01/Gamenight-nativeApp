namespace GameNight.Grounds;

/// <summary>The bare pitch (the PWA's barePitch.ts): the field, its goals and corner flags under
/// an open sky, open fields to the horizon, and nothing else.</summary>
public sealed class BarePitch : Ground
{
    protected override void Setup()
    {
        Land = new Godot.Vector3(0x4a, 0x5e, 0x36) / 255f;
        FloodScale = 0.6f;
    }

    protected override void Build() => Pitchside.CornerFlags(Static);
}
