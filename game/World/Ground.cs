using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GameNight.Sim;

namespace GameNight.Grounds;

/// <summary>
/// A ground: everything round the pitch (stands, roofs, floodlights, boards, dugouts, the
/// crowd), the sky and the time of day. A subclass describes its geometry in <see cref="Build"/>;
/// this base bakes the light, commits it all as a few draws and drives the mood per frame:
///   1 draw  every static surface (stadium.gdshader, vertex colours + baked sun)
///   1 draw  the crowd (crowd.gdshader)
///   1 draw  the floodlight glows
/// The pitch, goals and players are drawn by the match view; they read this ground's light
/// map (stand shadows, floodlight pools) through the gn_light_map global.
/// </summary>
public abstract class Ground
{
    public readonly Node3D Root = new() { Name = "Ground" };
    public Atmosphere Atmosphere { get; private set; }

    /// <summary>Drawn static geometry.</summary>
    protected readonly MeshData Static = new();
    /// <summary>Solid but never drawn: casts the stand shadows only (the near stand behind the camera).</summary>
    protected readonly MeshData ShadowOnly = new();
    protected readonly Crowd Crowd = new();
    /// <summary>Floodlight banks (positions), for the pitch's light pools and the glows.</summary>
    protected Vector3[] Lamps = Array.Empty<Vector3>();
    protected BannerArt[] Banners = Array.Empty<BannerArt>();
    /// <summary>The pitchside boards' designs (8).</summary>
    protected (string text, uint bg, uint fg)[] BoardArt = Signage.Boards;
    /// <summary>How far the floodlights carry here (1 big stadium, lower at small grounds).</summary>
    protected float FloodScale = 1f;
    /// <summary>Haze: fog start/end distance and strength.</summary>
    protected Vector2 FogRange = new(110, 340);
    protected float Haze = 1f;
    /// <summary>The height map's extent (must cover every caster, the near stand included).</summary>
    protected Rect2 BakeArea = new(-130, -120, 260, 230);

    public uint HomeColor = 0xc8393b, AwayColor = 0x2a4a8c;
    public string ClubName = "Rossoneri";

    GlowView _glows;
    int _goalTeam = -1;
    double _goalAt = -100;
    Phase _lastPhase;
    float _tifo;
    int _lastScore0, _lastScore1;

    protected abstract void Build();

    /// <summary>Debug: `-- --cam=x,y,z,tx,ty,tz[,progress]` holds the camera there (to look at the ground).</summary>
    float[] _debugCam;

    void PlaceDebugCamera()
    {
        var cam = Root.GetViewport().GetCamera3D();
        var c = _debugCam;
        cam.LookAtFromPosition(new Vector3(c[0], c[1], c[2]), new Vector3(c[3], c[4], c[5]), Vector3.Up);
        cam.Fov = 50;
    }

    /// <summary>Banks that get a visible glow (default: all of them).</summary>
    protected virtual Vector3[] GlowSpots => Lamps;

    /// <summary>Builds the ground with this id; debug `-- --ground=id` overrides it.</summary>
    public static Ground Create(string id)
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--ground=")) id = arg[9..];
        return id switch
        {
            "comunale" => new Comunale(),
            _ => new BigStadium(),
        };
    }

    /// <summary>Builds the ground under `parent`: geometry, baked light, crowd, signage.</summary>
    public Ground AddTo(Node3D parent)
    {
        parent.AddChild(Root);
        Atmosphere = new Atmosphere(Root) { FloodScale = FloodScale };
        RenderingServer.GlobalShaderParameterSet("gn_home", Lin(HomeColor));
        RenderingServer.GlobalShaderParameterSet("gn_away", Lin(AwayColor));
        RenderingServer.GlobalShaderParameterSet("gn_fog_range", FogRange);
        RenderingServer.GlobalShaderParameterSet("gn_haze", Haze);
        RenderingServer.GlobalShaderParameterSet("gn_flood_col", Lin(0xfff4e0));
        RenderingServer.GlobalShaderParameterSet("gn_goal", new Vector2(-1, 100));
        Atmosphere.Set(0);
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--cam=")) _debugCam = System.Array.ConvertAll(arg[6..].Split(','), float.Parse);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        Build();
        long tBuild = clock.ElapsedMilliseconds;

        var bake = new LightBake(BakeArea.Position.X, BakeArea.Position.Y, BakeArea.End.X, BakeArea.End.Y);
        bake.AddCaster(Static);
        bake.AddCaster(ShadowOnly);
        bake.BakeVertices(Static);
        bake.PublishLightMap(Lamps);

        var mi = new MeshInstance3D
        {
            Mesh = Static.Commit(),
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/stadium.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        Root.AddChild(mi);
        Crowd.Build(Root, bake);
        if (GlowSpots.Length > 0) _glows = new GlowView(Root, GlowSpots);
        Root.AddChild(new Signage(ClubName, HomeColor, AwayColor, Banners, BoardArt));
        GD.Print($"Ground {GetType().Name}: {Static.Count / 3} triangles, {Crowd.Fans} fans; built in {tBuild} ms, total {clock.ElapsedMilliseconds} ms");
        return this;
    }

    static Vector3 Lin(uint hex)
    {
        var c = MeshData.Srgb(hex);
        return new Vector3(c.R, c.G, c.B);
    }

    /// <summary>Per frame: the time of day from the match clock, the crowd's mood from the match.</summary>
    public virtual void Update(MatchSnapshot s, double time, float dt)
    {
        if (_debugCam != null) PlaceDebugCamera();
        float progress = s.Phase == Phase.Fulltime ? 1f : Mathf.Clamp(s.Minute / 90f, 0, 1);
        if (_debugCam != null && _debugCam.Length > 6) progress = _debugCam[6];
        Atmosphere.Set(progress);

        // A goal: whoever's score went up. The scoring side's fans go wild for a while.
        if (s.Score[0] != _lastScore0 || s.Score[1] != _lastScore1)
        {
            if (s.Score[0] > _lastScore0) { _goalTeam = 0; _goalAt = time; }
            else if (s.Score[1] > _lastScore1) { _goalTeam = 1; _goalAt = time; }
            _lastScore0 = s.Score[0];
            _lastScore1 = s.Score[1];
        }
        double since = time - _goalAt;
        RenderingServer.GlobalShaderParameterSet("gn_goal", since < 20 ? new Vector2(_goalTeam, (float)since) : new Vector2(-1, 100));
        RenderingServer.GlobalShaderParameterSet("gn_excite", s.Excitement);

        // The kick-off card displays, at the start of each half.
        bool kickoff = (s.Phase == Phase.Kickoff || s.SetPiece == SetPieceKind.Kickoff && s.Phase == Phase.SetPiece)
            && (s.Minute == 0 || s.Minute == 45) && s.Score[0] + s.Score[1] == _lastScore0 + _lastScore1 && since > 20;
        float want = kickoff ? 1 : 0;
        _tifo += (want - _tifo) * (1 - Mathf.Exp(-dt * 2.5f));
        RenderingServer.GlobalShaderParameterSet("gn_tifo", _tifo);
        _lastPhase = s.Phase;
    }

    /// <summary>Supporters' banners over the railings at the front of the lower tier, and the
    /// ultras' big drop banner over the boxes behind the home goal.</summary>
    /// <summary>The kick-off card displays over the straight of each end (the corners stay as
    /// they are): home end from the atlas's home tifo, away end from the away one.</summary>
    protected static Func<Vector3, float, int, Vector2?> EndTifos(float cz, float slope) => (p, sv, zone) =>
    {
        if (zone == 0 || Mathf.Abs(p.Z) > cz - 1 || sv < 0.6f || sv > slope - 0.4f) return null;
        float u = zone == 1 ? (cz - p.Z) / (2 * cz) : (p.Z + cz) / (2 * cz);
        float v = (sv - 0.6f) / (slope - 1f);
        return new Vector2(512 + u * 512, (zone == 1 ? 288 : 448) + (1 - v) * 160) / 1024f;
    };

    protected static void RailBanners(MeshData m, List<PathPt> path, float dropO, float dropY, float dropW, float dropH)
    {
        var straights = new[] { 0, 1, 2 }.Select(z => path.Where(p => p.Zone == z && (z == 0 ? p.NZ < -0.999f : Mathf.Abs(p.NX) > 0.999f)).ToList()).ToArray();
        void Hang(int row, PathPt p, float o, float y, float w, float h)
        {
            m.Hex(0xffffff, Look.Cloth, row);
            var c = p.At(o, y);
            var right = new Vector3(-p.NZ, 0, p.NX);
            int seg = Mathf.Max(2, (int)(w / 1.5f));
            for (int s = 0; s < seg; s++)
            {
                float u0 = s / (float)seg, u1 = (s + 1) / (float)seg;
                var a = c + right * (w * (u0 - 0.5f));
                var b = c + right * (w * (u1 - 0.5f));
                var dy = new Vector3(0, h / 2, 0);
                m.QuadUV(a - dy, b - dy, b + dy, a + dy, new(u0, 0), new(u1, 0), new(u1, 1), new(u0, 1));
            }
        }
        void Rail(int row, int zone, float f, float w)
        {
            var pts = straights[zone];
            Hang(row, pts[Mathf.Min(pts.Count - 1, (int)(f * pts.Count))], -0.08f, 0.72f, w, 1.35f);
        }
        Rail(0, 1, 0.22f, 15); Rail(1, 1, 0.5f, 11); Rail(2, 1, 0.8f, 14);
        Rail(3, 0, 0.12f, 13); Rail(4, 0, 0.36f, 11); Rail(5, 0, 0.6f, 9); Rail(6, 0, 0.86f, 13);
        Rail(7, 2, 0.3f, 15); Rail(8, 2, 0.72f, 10);
        if (dropW > 0) Hang(9, straights[1][straights[1].Count / 2], dropO, dropY, dropW, dropH);
    }
}
