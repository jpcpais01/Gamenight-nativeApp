using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GameNight.Club;
using GameNight.Menus;
using GameNight.Sim;

namespace GameNight.Grounds;

/// <summary>What a ground shows of the home club beyond its colours: crest, founding year,
/// motto banner and the supporters' own tifo pictures (any may be missing).</summary>
public sealed class ClubArt
{
    public Texture2D Crest, EndTifo, GiantTifo, FanTifo;
    public string Founded = "1903";
    public (string text, uint bg, uint fg)? Motto;
}

/// <summary>
/// A ground: everything round the pitch (stands, roofs, floodlights, boards, dugouts, the
/// crowd), the sky and the time of day. A subclass describes its geometry in <see cref="Build"/>;
/// this base bakes the light, commits it all as a few draws and drives the mood per frame:
///   1 draw  every static surface (stadium.gdshader, vertex colours + baked sun)
///  12 draws the crowd in wedges (crowd.gdshader), most culled
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
    /// <summary>The near stand, behind the match camera: drawn (it shows when the camera turns
    /// for a goal kick or a free kick) but cut away wherever it would hide the pitch.</summary>
    protected readonly MeshData Near = new();
    protected readonly Crowd Crowd = new();
    protected readonly Flags Flags = new();
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
    /// <summary>The land beyond the pitch's apron (sRGB): the grey track, or grass.</summary>
    protected Vector3 Land = new(0.16f, 0.15f, 0.16f);

    public uint HomeColor = 0xc8393b, AwayColor = 0x2a4a8c;
    public string ClubName = "Rossoneri";
    public string HomeShort = "ROS", AwayShort = "ATL";
    /// <summary>The ground has big screens (they show the live score).</summary>
    protected bool HasScreen;
    protected readonly FanBanners FanBanners = new();
    ScreenView _screen;
    /// <summary>The two sides' kits (the substitutes on the benches wear them).</summary>
    readonly GameNight.Sim.Kit[] _kits =
    {
        new() { Shirt = 0xc8393b, Shirt2 = 0xf3ede0, Shorts = 0xf3ede0, Socks = 0xc8393b, GkShirt = 0xe9c24a, GkShorts = 0x1d1d1d },
        new() { Shirt = 0xf1ebdc, Shirt2 = 0x23345e, Shorts = 0x23345e, Socks = 0xf1ebdc, GkShirt = 0x2ba59a, GkShorts = 0x1d1d1d },
    };
    bool _hasBench;
    BenchView _bench;
    protected readonly ClubArt Art = new();
    /// <summary>The giant hanging tifo, at grounds that have one.</summary>
    protected GiantTifo Giant;
    bool _showGiant, _debugHang;
    float _drop;
    StandFx _fx;
    Vector3[] _steamSpots = Array.Empty<Vector3>();
    readonly CrowdMood _mood = new();
    ShaderMaterial _crowdMat;
    Main _main;
    bool _pyroDone;
    bool? _debugTifo;
    const float HLx = 52.5f;

    /// <summary>The player's club (set once by the app): its crest, motto and tifos dress the
    /// ground whenever it's the home side.</summary>
    public static ClubState Club;

    GlowView _glows;
    int _goalTeam = -1;
    double _goalAt = -100;
    Phase _lastPhase;
    float _tifo;
    int _lastScore0, _lastScore1;

    /// <summary>Names, colours and art that depend on the clubs (runs before anything is built).</summary>
    protected virtual void Setup() { }

    protected abstract void Build();

    /// <summary>Drop the giant tifo now (true) or let it wind back up (false). It also comes
    /// down by itself at each kick-off and goes back up once play is under way.</summary>
    public void ShowGiantTifo(bool show = true) => _showGiant = show;

    /// <summary>The supporters' own banner held up at `p`, `o` m back on a tier from a to b
    /// (offset, height), `w` m wide: only when they've made one.</summary>
    protected void HoldBanner(PathPt p, float o, Vector2 a, Vector2 b, float w)
    {
        if (Art.FanTifo == null) return;
        FanBanners.Hold(Static, p, p.At(o, a.Y + (o - a.X) / (b.X - a.X) * (b.Y - a.Y)), w);
    }

    /// <summary>Fans waving flags over a tier from a to b (offset, height), by the path's zones.</summary>
    protected void WaveFlags(List<PathPt> path, Vector2 a, Vector2 b) =>
        Flags.Add(path, a, b, HomeColor, AwayColor, Art.Crest != null ? 0.35f : 0);

    /// <summary>Debug: `-- --cam=x,y,z,tx,ty,tz[,progress]` holds the camera there (to look at the ground).</summary>
    float[] _debugCam;

    void PlaceDebugCamera()
    {
        var cam = Root.GetViewport().GetCamera3D();
        var c = _debugCam;
        cam.LookAtFromPosition(new Vector3(c[0], c[1], c[2]), new Vector3(c[3], c[4], c[5]), Vector3.Up);
        cam.Fov = 50;
    }

    /// <summary>This ground's own art in the atlas (x 0..1024, y 640..768).</summary>
    protected virtual void Paint(Signage s) { }

    /// <summary>A sign facing `n`, centred at `c`, w x h metres, showing atlas pixels `px`.</summary>
    protected static void Sign(MeshData m, Vector3 c, Vector3 n, float w, float h, Rect2 px, bool selfLit = false)
    {
        m.Hex(0xffffff, Look.Decal, selfLit ? 1 : 0);
        var x = Vector3.Up.Cross(n).Normalized() * (w / 2);
        var y = new Vector3(0, h / 2, 0);
        Vector2 u0 = px.Position / 1024f, u1 = px.End / 1024f;
        m.QuadUV(c - x - y, c + x - y, c + x + y, c - x + y, new(u0.X, u1.Y), new(u1.X, u1.Y), new(u1.X, u0.Y), new(u0.X, u0.Y));
    }

    /// <summary>Banks that get a visible glow (default: all of them).</summary>
    protected virtual Vector3[] GlowSpots => Lamps;

    /// <summary>Builds the ground with this id, in the colours of the match's two clubs (the
    /// home side's shirt for the home fans and the stadium, the visitors' for the away end).
    /// Debug: `-- --ground=id` overrides the id.</summary>
    public static Ground Create(string id, MatchSetup setup = null)
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--ground=")) id = arg[9..];
        Ground g = id switch
        {
            "comunale" => new Comunale(),
            "old" => new OldGround(),
            "bare" => new BarePitch(),
            "training" => new TrainingGround(),
            "custom" => new Build.BuiltGround(Club?.S.Stadium, false),
            "custom:preview" => new Build.BuiltGround(Club?.S.Stadium, true),
            _ => new BigStadium(),
        };
        g.Dress(setup);
        return g;
    }

    void Dress(MatchSetup setup)
    {
        if (setup?.Teams is { Length: 2 } t && t[0] != null && t[1] != null)
        {
            GameNight.Sim.Kit h = t[0].Info.Kit, a = t[1].Info.Kit;
            HomeColor = (uint)h.Shirt;
            AwayColor = (uint)a.Shirt;
            if (!string.IsNullOrWhiteSpace(t[0].Info.Name)) ClubName = t[0].Info.Name;
            if (!string.IsNullOrWhiteSpace(t[0].Info.Short)) HomeShort = t[0].Info.Short;
            if (!string.IsNullOrWhiteSpace(t[1].Info.Short)) AwayShort = t[1].Info.Short;
            _kits[0] = h;
            _kits[1] = a;
        }
        var club = Club?.S;
        // The player's club is the home side in every match (as in the PWA).
        if (club == null) return;
        Art.Crest = CrestArt.Texture(club.Crest, 256);
        Art.Founded = club.Crest.Year ?? "";
        var (text, bg, fg) = Club.BannerColors();
        if (!string.IsNullOrWhiteSpace(text)) Art.Motto = (text.ToUpperInvariant(), (uint)bg, (uint)fg);
        Art.EndTifo = Tifos.Texture(TifoKind.End);
        Art.GiantTifo = Tifos.Texture(TifoKind.Giant);
        Art.FanTifo = Tifos.Texture(TifoKind.Fan);
    }

    /// <summary>The dugouts, with the substitutes and the managers in them.</summary>
    protected void Dugouts(MeshData m)
    {
        Pitchside.Dugouts(m);
        _hasBench = true;
    }

    /// <summary>Builds the ground under `parent`: geometry, baked light, crowd, signage.</summary>
    public Ground AddTo(Node3D parent)
    {
        Setup();
        // The motto is the ultras' big drop banner (the last banner slot).
        if (Art.Motto is var (mt, mbg, mfg) && Banners.Length >= 10) Banners[9] = new BannerArt(mt, mbg, mfg, 2);
        parent.AddChild(Root);
        Atmosphere = new Atmosphere(Root) { FloodScale = FloodScale, FogRange = FogRange, Haze = Haze };
        RenderingServer.GlobalShaderParameterSet("gn_home", Lin(HomeColor));
        RenderingServer.GlobalShaderParameterSet("gn_away", Lin(AwayColor));
        RenderingServer.GlobalShaderParameterSet("gn_land", Land);
        RenderingServer.GlobalShaderParameterSet("gn_flood_col", Lin(0xfff4e0));
        RenderingServer.GlobalShaderParameterSet("gn_goal", new Vector2(-1, 100));
        Atmosphere.Refresh();
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--cam=")) _debugCam = System.Array.ConvertAll(arg[6..].Split(','), float.Parse);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        Build();
        // The land runs on to the horizon under everything (the pitch's own plane stops at 160 m).
        Static.Hex(((uint)(Land.X * 255) << 16) | ((uint)(Land.Y * 255) << 8) | (uint)(Land.Z * 255));
        Static.Quad(new Vector3(-1500, -0.08f, 1500), new Vector3(1500, -0.08f, 1500), new Vector3(1500, -0.08f, -1500), new Vector3(-1500, -0.08f, -1500), 3000, 3000);
        long tBuild = clock.ElapsedMilliseconds;
        if (Crowd.Fans > 0) _steamSpots = Crowd.Sample(240);
        if (Crowd.Fans > 0 && _hasBench) Pitchside.Staff(Crowd);

        var bake = new LightBake(BakeArea.Position.X, BakeArea.Position.Y, BakeArea.End.X, BakeArea.End.Y);
        bake.AddCaster(Static);
        bake.AddCaster(ShadowOnly);
        bake.AddCaster(Near);
        bake.BakeVertices(Static);
        if (Near.Count > 0) bake.BakeVertices(Near);
        bake.PublishLightMap(Lamps);

        var mi = new MeshInstance3D
        {
            Mesh = Static.Commit(),
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://World/Shaders/stadium.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        Root.AddChild(mi);
        if (Near.Count > 0)
        {
            var nearMat = new ShaderMaterial { Shader = mi.MaterialOverride is ShaderMaterial sm ? sm.Shader : null };
            nearMat.SetShaderParameter("near_cut", true);
            Root.AddChild(new MeshInstance3D { Mesh = Near.Commit(), MaterialOverride = nearMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        var crowd = Crowd.Build(Root, bake);
        if (Crowd.Fans > 0)
        {
            _crowdMat = (ShaderMaterial)crowd.MaterialOverride;
            _fx = new StandFx(Root, HomeColor, AwayColor);
        }
        Flags.Build(Root, bake, Art.Crest);
        if (GlowSpots.Length > 0) _glows = new GlowView(Root, GlowSpots);
        Root.AddChild(new Signage(ClubName, HomeColor, AwayColor, Banners, BoardArt, Paint, Art));
        Giant?.Attach(Root, Art, ClubName, HomeColor, Art.Motto?.text ?? "ONE CLUB · ONE NIGHT");
        FanBanners.Build(Root, Art.FanTifo, HomeColor);
        if (_hasBench) _bench = new BenchView(Root, _kits[0], _kits[1], Club?.S.Coach);
        if (HasScreen) Root.AddChild(_screen = new ScreenView(ClubName, HomeShort, AwayShort, HomeColor, AwayColor, Art));
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--hang") >= 0) { _debugHang = true; _drop = 1; }
        GD.Print($"Ground {GetType().Name}: {Static.Count / 3} triangles, {Crowd.Fans} fans, {Flags.Count} flags; built in {tBuild} ms, total {clock.ElapsedMilliseconds} ms");
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
        // The match screen this ground belongs to: its terraces director and walk-out.
        if (_main == null)
            for (Node n = Root.GetParent(); n != null && _main == null; n = n.GetParent()) _main = n as Main;
        var terraces = _main?.Sound.Terraces;
        // Debug: `-- --pyro` lights the home end at once.
        if (terraces != null && !_pyroDone && Array.IndexOf(OS.GetCmdlineUserArgs(), "--pyro") >= 0)
        {
            _pyroDone = true;
            for (int i = 0; i < 7; i++)
                terraces.Pyro.Add(new GameNight.Audio.Pyro { X = -(HLx + 8.5f + 3 + i * 1.7f), Y = 1.4f + (3 + i * 1.7f) / 19.6f * 10.1f + 1.7f, Z = -18 + i * 6, Born = terraces.T, Life = 60, Seed = i * 0.13f, Smoke = i == 3 });
            terraces.Confetti.Add((0, 300));
        }
        float progress = s.Phase == Phase.Fulltime ? 1f : Mathf.Clamp(s.Minute / 90f, 0, 1);
        if (_debugCam != null && _debugCam.Length > 6) progress = _debugCam[6];
        Atmosphere.Set(progress);
        Atmosphere.Tick(dt, time);

        // A goal: whoever's score went up. The scoring side's fans go wild for a while.
        if (s.Score[0] != _lastScore0 || s.Score[1] != _lastScore1)
        {
            if (s.Score[0] > _lastScore0) { _goalTeam = 0; _goalAt = time; _fx?.Goal(0, -20); }
            else if (s.Score[1] > _lastScore1) { _goalTeam = 1; _goalAt = time; _fx?.Goal(1, 20); }
            _lastScore0 = s.Score[0];
            _lastScore1 = s.Score[1];
        }
        double since = time - _goalAt;
        RenderingServer.GlobalShaderParameterSet("gn_goal", since < 20 ? new Vector2(_goalTeam, (float)since) : new Vector2(-1, 100));
        _screen?.Show(s.Score[0], s.Score[1], s.Minute, since < 8 ? _goalTeam : -1, (int)(since * 3) % 2 == 0);
        RenderingServer.GlobalShaderParameterSet("gn_excite", s.Excitement);
        _bench?.Update(s, dt);
        if (Crowd.Fans > 0) _mood.Update(s, terraces, dt);
        _fx?.Air(s, dt, Atmosphere.Cold, Atmosphere.Rain, _steamSpots);
        RenderingServer.GlobalShaderParameterSet("gn_chant", terraces == null ? Vector4.Zero : new Vector4(terraces.Home, terraces.Away, terraces.Beat, terraces.Arms));
        if (_fx != null)
        {
            var (flares, count) = _fx.Update(dt, time, terraces);
            _crowdMat.SetShaderParameter("flares", flares);
            _crowdMat.SetShaderParameter("flare_n", count);
        }

        // The card displays (as the PWA): at every kick-off, and held up through the first few
        // seconds of play of each half (a game minute is 3.3 s).
        bool kickoff = s.Phase == Phase.Kickoff || s.SetPiece == SetPieceKind.Kickoff && s.Phase == Phase.SetPiece
            || s.Phase == Phase.Play && s.Minute - (s.Half >= 2 ? 45 : 0) < 3;
        // Debug: `-- --tifo` holds the card displays up.
        _debugTifo ??= Array.IndexOf(OS.GetCmdlineUserArgs(), "--tifo") >= 0;
        float want = kickoff || _debugTifo == true ? 1 : 0;
        _tifo += (want - _tifo) * (1 - Mathf.Exp(-dt * 2.5f));
        RenderingServer.GlobalShaderParameterSet("gn_tifo", _tifo);
        // The giant tifo unrolls in about three seconds and is wound back up a little slower.
        if (Giant != null)
        {
            float step = Mathf.Min(dt, 0.1f);
            bool walkOut = _main != null && _main.Cutscene.Active && _main.Cutscene.Hang > 0.5f;
            _drop = _showGiant || walkOut || _debugHang || _tifo > 0.5f ? Mathf.Min(1, _drop + step / 3) : Mathf.Max(0, _drop - step / 4);
            Giant.Set(_drop * _drop * (3 - 2 * _drop));
        }
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

    /// <summary>A curva's card display (the Comunale's): the picture over the middle of each
    /// end as EndTifos, and stripes of the end's colour and cream every 9 m everywhere else in
    /// it (swatches at the atlas's y 576..608).</summary>
    protected static Func<Vector3, float, int, Vector2?> CurvaTifos(float cz, float slope)
    {
        var picture = EndTifos(cz, slope);
        return (p, sv, zone) =>
        {
            if (picture(p, sv, zone) is Vector2 uv) return uv;
            if (zone == 0 || sv < 0.6f || sv > slope - 0.4f) return null;
            bool cream = Mathf.PosMod(Mathf.Atan2(p.Z, Mathf.Abs(p.X)) * 60 / 9, 2) >= 1;
            return new Vector2(cream ? 96 : zone == 1 ? 32 : 160, 592) / 1024f;
        };
    }

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
