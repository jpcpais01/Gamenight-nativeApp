using System;
using System.Collections.Generic;
using Godot;
using GameNight.Audio;
using GameNight.Club;
using Pos = GameNight.Club.Position;

namespace GameNight.Menus;

/// <summary>
/// Opening a pack, in the vault: the pack drops onto a lit pedestal; three taps charge it
/// (it shakes, cracks and leaks light, and from the second tap the light hints at the best card
/// inside), then it rips open in a burst of foil. Each card flies in face down and flips; the
/// good ones charge first, Epic and better get a walkout (nation, position, rating), and
/// Legendary and Icon cards get the lights-out moment before it. The best card is always last.
/// Reveal all deals the rest out and flips them in a cascade.
/// </summary>
public sealed partial class PackOpening : PxCanvas
{
    enum Phase { Intro, Tease, Crack, Burst, Lights, Walkout, Enter, Charged, Flip, Shown, Summary }

    readonly Menus _ui;
    readonly PackDef _pack;
    readonly List<Card> _cards;
    readonly Sim.Kit _kit;
    readonly int _bestTier;
    readonly Fx.World _fx = new();
    readonly Fx.Light _light;
    readonly List<Vector2[]> _cracks = new();
    Phase _phase = Phase.Intro;
    double _t, _shakeT = 9, _flash, _flashMax = 1, _quake, _punch, _spawn;
    int _taps, _i, _walkStep, _lightsOn, _flipped, _summaryBeat;
    float _quakeAmp = 4;
    Color _tint = Colors.White;
    Transform2D _base = Transform2D.Identity;

    static readonly Color[] Rainbow = { Px.Hex(0x7ff6ff), Px.Hex(0xff7ae6), Px.Hex(0xfff27a), Px.Hex(0x8dff9e), Colors.White };

    public PackOpening(Menus ui, PackDef pack, List<Card> cards)
    {
        _ui = ui;
        _pack = pack;
        _cards = cards;
        _kit = ui.Club.Info().Kit;
        _bestTier = (int)cards[^1].Rarity;
        _light = new Fx.Light(DrawLight);
        AddChild(_light);
        // Cracks across the pack (unit coordinates), revealed tap by tap.
        var rng = new Random();
        for (int i = 0; i < 9; i++)
        {
            var pts = new Vector2[4];
            pts[0] = new Vector2(0.5f + ((float)rng.NextDouble() - 0.5f) * 0.3f, 0.45f + ((float)rng.NextDouble() - 0.5f) * 0.3f);
            float a = (float)(rng.NextDouble() * Mathf.Tau);
            for (int k = 1; k < 4; k++)
            {
                a += ((float)rng.NextDouble() - 0.5f) * 1.2f;
                pts[k] = pts[k - 1] + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 1.4f) * (0.1f + (float)rng.NextDouble() * 0.1f);
            }
            _cracks.Add(pts);
        }
        GameAudio.Instance?.SetAmbience(0);
        GameAudio.Instance?.Whoosh();
    }

    Card Cur => _cards[Math.Min(_i, _cards.Count - 1)];
    int Tier => (int)Cur.Rarity;
    static Color Col(int tier) => Art.RarityColor((Rarity)tier);
    float Horizon => Mathf.Round(Size.Y * 0.6f);
    Color Hue => Color.FromHsv((float)(T * 0.22 % 1), 0.55f, 1);

    // ---------------------------------------------------------------- flow

    /// <summary>Debug screenshots: hold a moment (`--phase=name@seconds`) on the best card.</summary>
    public void Hold(string phase, double at)
    {
        _hold = at;
        _i = _cards.Count - 1;
        _taps = 2;
        _tint = Col(_bestTier);
        _lightsOn = 3;
        _summaryBeat = 1;
        _flipped = phase == "summary" ? _cards.Count : 0;
        _phase = Enum.Parse<Phase>(phase, true);
        if (_phase == Phase.Shown) _fx.Rain(Size, Rainbow, 80);
    }

    double _hold = -1;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += delta;
        if (_hold >= 0) _t = _hold;
        _shakeT += delta;
        _flash = Math.Max(0, _flash - delta);
        _quake = Math.Max(0, _quake - delta);
        _punch = Math.Max(0, _punch - delta * 4);
        _fx.Step(dt);
        _fx.Motes(Size, _tint, 7, dt, Horizon + 30);
        _spawn += delta;
        switch (_phase)
        {
            case Phase.Intro when _t > 0.55:
                To(Phase.Tease);
                break;
            case Phase.Crack:
                if (_spawn > 0.04)
                {
                    _spawn = 0;
                    _fx.Burst(PackRect().GetCenter(), new[] { _tint, Colors.White }, 3, 260, 60);
                }
                if (_t > 0.45) Burst();
                break;
            case Phase.Burst when _t > 0.9:
                Begin();
                break;
            case Phase.Lights:
                int on = _t > 0.7 ? 3 : _t > 0.45 ? 2 : _t > 0.2 ? 1 : 0;
                while (_lightsOn < on)
                {
                    _lightsOn++;
                    GameAudio.Instance?.PackShake(_lightsOn);
                    Input.VibrateHandheld(30);
                }
                if (_t > 0.95 && _summaryBeat == 0)
                {
                    // The rarity's name slams down.
                    _summaryBeat = 1;
                    Shake(0.35, 6);
                    Flash(0.6);
                    _fx.Shock(Stage(), Col(Tier), 900, 0.7f, 6);
                    _fx.Burst(Stage(), new[] { Col(Tier), Colors.White }, 60, 420, 90);
                    GameAudio.Instance?.Stinger(2);
                }
                if (_t > 2.1) StartWalkout();
                break;
            case Phase.Walkout:
                if (Tier >= 4 && _spawn > 0.32)
                {
                    _spawn = 0;
                    Firework(new Vector2(_fx.Rng.Next(60, (int)Size.X - 60), _fx.Rng.Next(40, (int)(Size.Y * 0.45f))));
                }
                if (_t > (Tier >= 3 ? 1.2 : 1.0)) NextWalk();
                break;
            case Phase.Enter when _t > (Tier >= 2 ? 0.5 : 0.36):
                if (Tier >= 2) To(Phase.Charged);
                else Flip();
                break;
            case Phase.Charged:
                if (_spawn > 0.05 - Tier * 0.008)
                {
                    _spawn = 0;
                    _fx.Implode(CardRect().GetCenter(), Tier >= 4 ? Hue : Col(Tier), 3 + Tier, CardRect().Size.Y * 0.9f);
                }
                if (_t > ChargeTime) Flip();
                break;
            case Phase.Flip when _t > 0.32:
                To(Phase.Shown);
                Pop();
                break;
            case Phase.Summary:
                SummaryTick();
                break;
        }
        _light.QueueRedraw();
        base._Process(delta);
    }

    double ChargeTime => Tier switch { 2 => 0.6, 3 => 0.95, _ => 1.25 };

    void To(Phase p)
    {
        _phase = p;
        _t = 0;
        _spawn = 0;
        if (p == Phase.Enter) GameAudio.Instance?.Whoosh();
        if (p == Phase.Tease)
        {
            _fx.Shock(PedestalTop(), _tint, 320, 0.5f, 3);
            _fx.Burst(PedestalTop(), new[] { new Color(1, 1, 1, 0.7f) }, 16, 160, 200);
            GameAudio.Instance?.PackShake(0);
        }
    }

    void Flash(double strength) => _flash = _flashMax = 0.3 * strength + 0.08;

    void Shake(double time, float amp)
    {
        _quake = Math.Max(_quake, time);
        _quakeAmp = amp;
    }

    void Charge()
    {
        _taps++;
        _shakeT = 0;
        _punch = 1;
        _tint = _taps >= 2 && _bestTier >= 2 ? Col(_bestTier) : _bestTier >= 1 && _taps >= 2 ? Px.Hex(0xe6f1ff) : Colors.White;
        if (_taps >= 2 && _bestTier >= 4) _tint = Hue;
        var c = PackRect().GetCenter();
        _fx.Burst(c, new[] { _tint, Colors.White }, 10 + _taps * 8, 240 + _taps * 60, 80);
        _fx.Shock(c, _tint, 380 + _taps * 160, 0.55f, 3 + _taps);
        Shake(0.12 + _taps * 0.05, 2 + _taps);
        Input.VibrateHandheld(20 + _taps * 20);
        GameAudio.Instance?.PackShake(_taps);
        if (_taps >= 3) To(Phase.Crack);
    }

    void Burst()
    {
        var c = _bestTier >= 2 ? Col(_bestTier) : Colors.White;
        if (_bestTier >= 4) c = Hue;
        _tint = c;
        var at = PackRect().GetCenter();
        Flash(1.2);
        Shake(0.45, 7);
        _fx.Shock(at, c, 1100, 0.8f, 8);
        _fx.Shock(at, Colors.White, 700, 0.6f, 4);
        _fx.Burst(at, new[] { c, Colors.White }, 90 + _bestTier * 30, 560 + _bestTier * 70, 160);
        var pc = new[] { Px.Hex(_pack.Colors[0]), Px.Hex(_pack.Colors[2]), Px.Hex(_pack.Colors[1]) };
        _fx.Burst(at, pc, 50, 520, 260, Fx.Kind.Shard, 1.4f);
        Input.VibrateHandheld(120);
        GameAudio.Instance?.PackBurst(_bestTier);
        To(Phase.Burst);
    }

    /// <summary>The next card: lights out for the rarest, a walkout for the good ones, then it flies in.</summary>
    void Begin()
    {
        _tint = Tier >= 4 ? Hue : Tier >= 1 ? Col(Tier) : Colors.White;
        _walkStep = 0;
        _lightsOn = 0;
        _summaryBeat = 0;
        if (Tier >= 3) To(Phase.Lights);
        else if (Tier >= 2) StartWalkout();
        else To(Phase.Enter);
    }

    void StartWalkout()
    {
        To(Phase.Walkout);
        Slam();
    }

    void Slam()
    {
        var c = Tier >= 4 ? Hue : Col(Tier);
        Flash(0.35);
        Shake(0.2, 4);
        _fx.Shock(Stage(), c, 700, 0.6f, 5);
        _fx.Burst(Stage(), new[] { c, Colors.White }, 24, 300, 120);
        _fx.Burst(PedestalTop(), new[] { new Color(1, 1, 1, 0.6f) }, 14, 180, 220);
        Input.VibrateHandheld(40);
        GameAudio.Instance?.Stinger(_walkStep);
    }

    void NextWalk()
    {
        _walkStep++;
        int steps = (Tier >= 3 ? 3 : 2) + (Playstyles.Of(Cur).Count > 0 ? 1 : 0);
        if (_walkStep >= steps) To(Phase.Enter);
        else
        {
            _t = 0;
            Slam();
        }
    }

    void Flip()
    {
        To(Phase.Flip);
        GameAudio.Instance?.Reveal(Tier);
        var r = CardRect();
        var c = Tier >= 4 ? Hue : Col(Tier);
        var cols = Tier >= 4 ? Rainbow : new[] { c, Colors.White };
        _fx.Burst(r.GetCenter(), cols, 40 + Tier * 35, 300 + Tier * 90, 140);
        for (int i = 0; i <= Math.Min(Tier, 3); i++) _fx.Shock(r.GetCenter(), i % 2 == 0 ? c : Colors.White, 500 + i * 260, 0.6f + i * 0.1f, 6 - i);
        if (Tier >= 1) Flash(0.4 + Tier * 0.18);
        if (Tier >= 3)
        {
            Shake(0.55, 4 + Tier * 1.5f);
            _fx.Rain(Size, cols, 70 + Tier * 30);
            Input.VibrateHandheld(180);
        }
        if (Tier >= 4)
            for (int i = 0; i < 3; i++) Firework(new Vector2(Size.X * (0.2f + i * 0.3f), Size.Y * (0.22f + (i % 2) * 0.1f)));
    }

    void Firework(Vector2 p)
    {
        var c = Rainbow[_fx.Rng.Next(Rainbow.Length - 1)];
        _fx.Burst(p, new[] { c, Colors.White, c }, 36, 260, 120, Fx.Kind.Spark, 1.1f);
        _fx.Shock(p, c, 300, 0.45f, 2);
        GameAudio.Instance?.UiTap();
    }

    /// <summary>The card lands face up: a pop in size, and its lingering sparkle.</summary>
    void Pop() => _punch = 1;

    void Next()
    {
        if (_i < _cards.Count - 1)
        {
            _i++;
            Begin();
        }
        else Summary();
    }

    void Summary()
    {
        _i = _cards.Count - 1;
        _tint = _bestTier >= 4 ? Colors.White : Col(_bestTier);
        _flipped = 0;
        GameAudio.Instance?.Whoosh();
        To(Phase.Summary);
    }

    /// <summary>The summary deals the cards face down, then flips them one by one, best last.</summary>
    void SummaryTick()
    {
        int due = 0;
        for (int i = 0; i < _cards.Count; i++)
            if (_t >= FlipAt(i)) due = i + 1;
        while (_flipped < due)
        {
            int i = _flipped++;
            var c = _cards[i];
            int tier = (int)c.Rarity;
            var r = SummaryRect(i);
            var col = tier >= 4 ? Hue : Col(tier);
            _fx.Burst(r.GetCenter(), new[] { col, Colors.White }, 12 + tier * 10, 160 + tier * 50, 120);
            _fx.Shock(r.GetCenter(), col, 260 + tier * 80, 0.45f, 3);
            if (i == _cards.Count - 1)
            {
                GameAudio.Instance?.Reveal(tier);
                if (tier >= 2) Flash(0.3 + tier * 0.1);
                if (tier >= 3)
                {
                    Shake(0.35, 5);
                    _fx.Rain(Size, tier >= 4 ? Rainbow : new[] { col, Colors.White }, 60);
                }
            }
            else GameAudio.Instance?.UiTap();
        }
    }

    double FlipAt(int i) => 0.55 + i * 0.16 + (i == _cards.Count - 1 ? 0.25 : 0);

    void Close()
    {
        _ui.Close(this);
        GameAudio.Instance?.SetAmbience(0.4f);
    }

    protected override void Background() => OnTap();

    void OnTap()
    {
        switch (_phase)
        {
            case Phase.Intro: To(Phase.Tease); break;
            case Phase.Tease: Charge(); break;
            case Phase.Walkout: NextWalk(); break;
            case Phase.Lights when _t > 1.0: StartWalkout(); break;
            case Phase.Enter or Phase.Charged: Flip(); break;
            case Phase.Shown when _t > 0.2: Next(); break;
            case Phase.Summary when _flipped < _cards.Count: _t = Math.Max(_t, FlipAt(_cards.Count - 1)); break;
        }
    }

    // ---------------------------------------------------------------- layout

    Vector2 Stage() => new(Size.X / 2, Size.Y * 0.44f);

    Vector2 PedestalTop() => new(_phase >= Phase.Enter && _phase <= Phase.Shown ? CardRect().GetCenter().X : Size.X / 2, Horizon + Size.Y * 0.16f);

    Rect2 PackRect()
    {
        float h = Mathf.Round(Size.Y * 0.56f), w = Mathf.Round(h / 1.4f);
        float bob = _phase == Phase.Tease ? Mathf.Sin((float)T * 2.2f) * 4 : 0;
        float y = PedestalTop().Y - h - 26 + bob;
        if (_phase == Phase.Intro)
        {
            float k = Mathf.Min(1, (float)_t / 0.55f);
            k *= k;
            y = Mathf.Lerp(-h - 20, y, k);
        }
        return new Rect2(Size.X / 2 - w / 2, y, w, h);
    }

    Rect2 CardRect()
    {
        float h = Mathf.Min(Size.Y * 0.7f, 320), w = h / 1.4f;
        return new Rect2(new Vector2(Size.X * 0.34f - w / 2, Size.Y * 0.47f - h / 2).Round(), new Vector2(w, h).Round());
    }

    Rect2 SummaryRect(int i)
    {
        int n = _cards.Count;
        float gap = 12;
        float ch = Mathf.Min(Size.Y - 170, (Size.X - 60 - gap * (n - 1)) / n * 1.4f);
        float cw = ch / 1.4f;
        float total = n * cw + (n - 1) * gap;
        float x = (Size.X - total) / 2 + i * (cw + gap);
        float y = 84 + Mathf.Abs(i - (n - 1) / 2f) * 6;
        var r = new Rect2(x, y, cw, ch);
        return i == n - 1 ? r.Grow(r.Size.X * 0.06f) : r;
    }

    // ---------------------------------------------------------------- drawing

    void Transform(CanvasItem ci, Rect2 r, float rot, Vector2 scale)
    {
        var c = r.GetCenter();
        ci.DrawSetTransformMatrix(_base * new Transform2D(rot, scale, 0, c) * new Transform2D(0, -c));
    }

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        _base = _quake > 0
            ? new Transform2D(0, new Vector2(_fx.Rng.Next(-1, 2), _fx.Rng.Next(-1, 2)) * _quakeAmp * (float)Math.Min(1, _quake * 4))
            : Transform2D.Identity;
        DrawSetTransformMatrix(_base);

        float dim = _phase switch
        {
            Phase.Lights => _lightsOn == 0 ? 1 : 0.92f,
            Phase.Walkout => 0.8f,
            Phase.Charged => Mathf.Min(0.6f, (float)_t * (Tier >= 3 ? 0.8f : 0.4f)),
            _ => 0,
        };
        var tint = Tier >= 4 && _phase >= Phase.Lights && _phase <= Phase.Shown ? Hue : _tint;
        // The room is drawn a little oversize so the shake never shows its edge.
        DrawSetTransformMatrix(_base * new Transform2D(0, new Vector2(-10, -10)));
        Fx.Vault(this, Size + new Vector2(20, 20), tint, (float)T, Horizon + 10, dim);
        DrawSetTransformMatrix(_base);
        Tap("stage", new Rect2(Vector2.Zero, Size), OnTap);

        // The pedestal and the lamp over it.
        var ped = PedestalTop();
        float spotA = _phase == Phase.Intro ? Mathf.Min(1, (float)_t * 2) * 0.14f : _phase == Phase.Lights ? 0 : 0.14f;
        if (spotA > 0) Fx.Spot(this, new Vector2(ped.X, -10), ped, 60, 230, tint.Lerp(Colors.White, 0.5f), spotA);
        if (_phase != Phase.Summary) Fx.Pedestal(this, ped, 170, tint, _phase == Phase.Tease ? _taps / 3f : 0.6f);

        switch (_phase)
        {
            case Phase.Intro or Phase.Tease or Phase.Crack: PackStage(); break;
            case Phase.Burst: BurstStage(); break;
            case Phase.Lights: LightsStage(); break;
            case Phase.Walkout: WalkoutStage(); break;
            case Phase.Enter or Phase.Charged or Phase.Flip or Phase.Shown: CardStage(); break;
            case Phase.Summary: SummaryStage(); break;
        }

        DrawSetTransformMatrix(Transform2D.Identity);
        if (_phase is Phase.Lights or Phase.Walkout)
        {
            // Cinema bars.
            float k = _phase == Phase.Lights ? Mathf.Min(1, (float)_t * 4) : 1;
            float bar = Mathf.Round(34 * k);
            DrawRect(new Rect2(0, 0, W, bar), Colors.Black);
            DrawRect(new Rect2(0, H - bar, W, bar), Colors.Black);
        }
        if (_phase != Phase.Summary)
        {
            if (_phase >= Phase.Lights) Px.TextR(this, Px.Small, W - 150, 30, $"{_i + 1} / {_cards.Count}", 9, Px.InkDim);
            GhostButton("skip", new Rect2(W - 140, 10, 126, 32), "REVEAL ALL", 20, () =>
            {
                if (_phase < Phase.Burst) Burst();
                Summary();
            });
        }
        Px.Scanlines(this, new Rect2(0, 0, W, H));
    }

    /// <summary>The bright layer: sparks, shockwaves, cracks, foil shine and the flash.</summary>
    void DrawLight(CanvasItem ci)
    {
        ci.DrawSetTransformMatrix(_base);
        switch (_phase)
        {
            case Phase.Tease or Phase.Crack:
            {
                // Light leaking through the cracks.
                var r = PackTransformRect(out float rot, out float sc);
                Transform(ci, r, rot, new Vector2(sc, sc));
                int n = _phase == Phase.Crack ? _cracks.Count : _taps * 2;
                float pulse = 0.6f + 0.4f * Mathf.Sin((float)T * 20);
                for (int i = 0; i < n; i++)
                {
                    var pts = Array.ConvertAll(_cracks[i], p => r.Position + p * r.Size);
                    ci.DrawPolyline(pts, new Color(_tint, pulse), _phase == Phase.Crack ? 3 : 2);
                }
                Fx.Shine(ci, r, (float)(T * 0.45 % 1.6), new Color(1, 1, 1, 0.22f));
                if (_phase == Phase.Crack) ci.DrawRect(r, new Color(_tint, (float)_t / 0.45f * 0.6f));
                ci.DrawSetTransformMatrix(_base);
                break;
            }
            case Phase.Burst:
            {
                // A column of light out of the torn top.
                var r = PackRect();
                float k = 1 - Mathf.Min(1, (float)_t / 0.9f);
                float w = r.Size.X * (0.6f + (1 - k) * 0.5f);
                ci.DrawRect(new Rect2(r.GetCenter().X - w / 2, -20, w, r.Position.Y + 40 + r.Size.Y * 0.2f), new Color(_tint, 0.5f * k));
                ci.DrawRect(new Rect2(r.GetCenter().X - w / 4, -20, w / 2, r.Position.Y + 40 + r.Size.Y * 0.2f), new Color(1, 1, 1, 0.4f * k));
                break;
            }
            case Phase.Shown:
            {
                var r = CardRect();
                float rot = Mathf.Sin((float)T * 1.3f) * 0.022f;
                float s = 1 + (float)_punch * 0.12f;
                Transform(ci, r.Translated(new Vector2(0, Mathf.Sin((float)T * 2) * 3)), rot, new Vector2(s, s));
                Foil(ci, r, Tier, (float)T);
                ci.DrawSetTransformMatrix(_base);
                if (Tier >= 2) Orbit(ci, r, Tier >= 4 ? Hue : Col(Tier), 4 + Tier * 2);
                break;
            }
            case Phase.Summary:
                for (int i = 0; i < Math.Min(_flipped, _cards.Count); i++)
                {
                    int tier = (int)_cards[i].Rarity;
                    if (tier >= 1) Foil(ci, SummaryRect(i), tier, (float)T + i * 0.4f);
                }
                if (_flipped == _cards.Count && _bestTier >= 2) Orbit(ci, SummaryRect(_cards.Count - 1), _bestTier >= 4 ? Hue : Col(_bestTier), 6);
                break;
        }
        _fx.Draw(ci);
        ci.DrawSetTransformMatrix(Transform2D.Identity);
        if (_flash > 0) ci.DrawRect(new Rect2(Vector2.Zero, Size), new Color(1, 1, 1, (float)(_flash / _flashMax) * 0.9f));
    }

    /// <summary>The sheen on a face-up card: a sweep for Rare, a double sweep for Legendary, holo for Icon.</summary>
    static void Foil(CanvasItem ci, Rect2 r, int tier, float t)
    {
        if (tier <= 0) return;
        float period = tier >= 3 ? 1.8f : 2.6f;
        Fx.Shine(ci, r, t % period / (period * 0.6f), new Color(1, 1, 1, 0.16f + tier * 0.05f));
        if (tier >= 3) Fx.Shine(ci, r, (t + period / 2) % period / (period * 0.6f), new Color(Col(tier), 0.22f), 0.12f);
        if (tier >= 4) Fx.Holo(ci, r, t, 0.16f);
    }

    /// <summary>Glints wheeling around a card.</summary>
    void Orbit(CanvasItem ci, Rect2 r, Color c, int n)
    {
        var m = r.GetCenter();
        for (int i = 0; i < n; i++)
        {
            float a = (float)T * 0.8f + i * Mathf.Tau / n;
            var p = m + new Vector2(Mathf.Cos(a) * r.Size.X * 0.78f, Mathf.Sin(a) * r.Size.Y * 0.6f);
            float tw = Mathf.Abs(Mathf.Sin((float)T * 3 + i * 1.7f));
            Fx.Twinkle(ci, p.Round(), 3 + tw * 5, new Color(c, 0.4f + tw * 0.6f));
        }
    }

    Rect2 PackTransformRect(out float rot, out float scale)
    {
        var r = PackRect();
        rot = 0;
        if (_shakeT < 0.35) rot = Mathf.Sin((float)_shakeT * 60) * 0.06f * (1 - (float)_shakeT / 0.35f) * (0.6f + _taps * 0.4f);
        if (_phase == Phase.Crack) rot = Mathf.Sin((float)_t * 90) * 0.05f;
        scale = 1 + (float)_punch * 0.07f + _taps * 0.025f;
        if (_phase == Phase.Crack) scale += (float)_t * 0.15f;
        return r;
    }

    void PackStage()
    {
        var r = PackTransformRect(out float rot, out float sc);
        var c = r.GetCenter();
        // Beams behind, faster and brighter with each tap.
        float charge = _phase == Phase.Crack ? 4 : _taps;
        Fx.Beams(this, c, _tint, 14, Size.Length(), 0.05f + charge * 0.01f, (float)T * (0.15f + charge * 0.18f), 0.05f + charge * 0.035f);
        Fx.Glow(this, c, r.Size.Y * (0.55f + charge * 0.08f), _tint, 0.12f + charge * 0.05f);
        // Sparks wheeling round the pack.
        int n = 3 + (int)charge * 3;
        for (int i = 0; i < n; i++)
        {
            float a = (float)T * (1.2f + charge * 0.5f) + i * Mathf.Tau / n;
            var p = c + new Vector2(Mathf.Cos(a) * r.Size.X * 0.85f, Mathf.Sin(a) * r.Size.Y * 0.3f + r.Size.Y * 0.05f);
            DrawRect(new Rect2(p.Round(), new Vector2(3, 3)), new Color(_tint, 0.8f));
        }
        // The pack's shadow on the plinth.
        DrawColoredPolygon(Px.Ellipse(PedestalTop(), r.Size.X * 0.4f, 8, 20), new Color(0, 0, 0, 0.35f));
        Transform(this, r, rot, new Vector2(sc, sc));
        Art.Pack(this, r, _pack, _taps / 3f);
        DrawSetTransformMatrix(_base);
        if (_phase == Phase.Tease)
        {
            // Three charge pips, and the hint.
            for (int i = 0; i < 3; i++)
            {
                var pr = new Rect2(Size.X / 2 - 34 + i * 26, PedestalTop().Y + 30, 16, 16);
                Px.Frame(this, pr, i < _taps ? _tint : new Color(0, 0, 0, 0.5f), i < _taps ? Colors.White : Px.Line2, null, 2, 0);
            }
            string hint = _taps == 0 ? "TAP TO OPEN" : _taps == 1 ? "AGAIN!" : "ONE MORE!";
            if (T % 1 < 0.7) Px.TextC(this, Px.Big, Size.X / 2, Size.Y - 14, hint, 28, Px.Ink, new Color(0, 0, 0, 0.6f), 3);
        }
    }

    void BurstStage()
    {
        var r = PackRect();
        float t = (float)_t;
        Fx.Beams(this, r.GetCenter(), _tint, 18, Size.Length(), 0.09f, t * 1.2f, 0.16f * (1 - t / 0.9f));
        // The body drops and fades; the torn top flies off spinning.
        float a = Mathf.Clamp(1 - (t - 0.2f) / 0.5f, 0, 1);
        if (a > 0)
        {
            var body = r.Translated(new Vector2(0, t * t * 160));
            Art.Pack(this, body, _pack, 1);
            DrawRect(new Rect2(body.Position, new Vector2(body.Size.X, body.Size.Y * 0.18f)), _tint.Lerp(Colors.White, 0.6f));
            if (a < 1) DrawRect(body.Grow(4), new Color(0.02f, 0.015f, 0.06f, 1 - a));
        }
        var lidC = r.Position + new Vector2(r.Size.X / 2 + t * 260, r.Size.Y * 0.08f - t * 420 + t * t * 500);
        var lid = new Rect2(lidC - new Vector2(r.Size.X / 2, r.Size.Y * 0.09f), new Vector2(r.Size.X, r.Size.Y * 0.18f));
        Transform(this, lid, t * 7, Vector2.One);
        var c1 = Px.Hex(_pack.Colors[0]);
        Px.Frame(this, lid, c1, Px.Hex(_pack.Colors[1]).Darkened(0.4f), null, 3, 0);
        for (float x = lid.Position.X + 4; x < lid.End.X - 4; x += lid.Size.X / 12f)
            DrawRect(new Rect2(x, lid.Position.Y + 4, lid.Size.X / 26f, lid.Size.Y * 0.3f), new Color(0, 0, 0, 0.2f));
        DrawSetTransformMatrix(_base);
    }

    void LightsStage()
    {
        var c = Tier >= 4 ? Hue : Col(Tier);
        float y0 = 0, pool = PedestalTop().Y;
        float[] xs = { Size.X * 0.2f, Size.X * 0.8f, Size.X * 0.5f };
        for (int i = 0; i < _lightsOn; i++)
            Fx.Spot(this, new Vector2(xs[i], y0), new Vector2(Size.X / 2 + (xs[i] - Size.X / 2) * 0.25f, pool), 30, 180, i == 2 ? c : Colors.White, i == 2 ? 0.2f : 0.1f);
        if (_summaryBeat == 0) return;
        float k = Mathf.Min(1, ((float)_t - 0.95f) / 0.12f);
        float s = 1.6f - 0.6f * (1 - (1 - k) * (1 - k));
        var m = Stage();
        Fx.Beams(this, m, c, 16, Size.Length(), 0.06f, (float)T * 0.6f, 0.12f);
        string name = Cards.Label(Cur.Rarity).ToUpperInvariant();
        int size = (int)Mathf.Min(120 * s, (Size.X - 80) / Mathf.Max(1, Px.Width(Px.Big, name, 1)));
        Px.TextC(this, Px.Big, m.X, m.Y + size * 0.3f, name, size, c, new Color(0, 0, 0, 0.7f), 5);
        Px.TextC(this, Px.Small, m.X, m.Y + size * 0.3f + 30, Tier >= 4 ? "THE RAREST CARD THERE IS" : "ONE OF THE GREATS", 10, Px.Ink);
    }

    void WalkoutStage()
    {
        var c = Cur;
        var col = Tier >= 4 ? Hue : Col(Tier);
        float k = Mathf.Min(1, (float)_t / 0.14f);
        float s = 1.5f - 0.5f * (1 - (1 - k) * (1 - k));
        var m = Stage();
        Fx.Spot(this, new Vector2(m.X, -10), PedestalTop(), 50, 220, col, 0.18f);
        Fx.Beams(this, m, col, 12, Size.Length(), 0.05f, (float)T * 0.4f, 0.1f);
        Fx.Glow(this, m, 150, col, 0.18f);
        // The playstyles step comes last (after the rating for the best cards).
        int styleStep = Tier >= 3 ? 3 : 2;
        string[] labels = { "NATION", "POSITION", "RATING" };
        Px.TextC(this, Px.Small, m.X, 58, _walkStep >= styleStep ? "PLAYSTYLE" : labels[Math.Min(_walkStep, 2)], 10, Px.InkDim);
        if (_walkStep >= styleStep)
        {
            var ps = Playstyles.Of(c);
            float gap = 190, x0 = m.X - (ps.Count - 1) * gap / 2;
            for (int i = 0; i < ps.Count; i++)
            {
                var at = new Vector2(x0 + i * gap, m.Y - 6);
                Fx.Glow(this, at, 70 * s, Px.Hex(ps[i].Color), 0.25f);
                Art.Playstyle(this, at, 46 * s, ps[i]);
                Px.TextC(this, Px.Big, at.X, at.Y + 82 * s, ps[i].Name.ToUpperInvariant(), (int)(30 * s), Px.Hex(ps[i].Color), new Color(0, 0, 0, 0.7f), 3);
                Px.TextC(this, Px.Small, at.X, at.Y + 104 * s, Playstyles.Describe(ps[i]).ToUpperInvariant(), 8, Px.Ink);
            }
            return;
        }
        if (_walkStep == 0)
        {
            var n = Cards.Nations[c.Nation];
            var fs = new Vector2(150, 100) * s;
            var fr = new Rect2(m - fs / 2 - new Vector2(0, 16), fs);
            DrawRect(fr.Grow(4), Colors.Black);
            Px.Flag(this, fr, n);
            Px.TextC(this, Px.Big, m.X, m.Y + 76 * s, n.Code, (int)(46 * s), col, new Color(0, 0, 0, 0.7f), 3);
        }
        else if (_walkStep == 1) Px.TextC(this, Px.Big, m.X, m.Y + 52 * s, c.Position.ToString(), (int)(150 * s), col, new Color(0, 0, 0, 0.7f), 5);
        else Px.TextC(this, Px.Big, m.X, m.Y + 62 * s, c.Overall.ToString(), (int)(190 * s), col, new Color(0, 0, 0, 0.7f), 6);
    }

    void CardStage()
    {
        var c = Cur;
        var r = CardRect();
        float t = (float)_t;
        var col = Tier >= 4 ? Hue : Col(Tier);
        var m = r.GetCenter();
        if (_phase == Phase.Enter)
        {
            // Flies in from below, slowing as it arrives.
            float dur = Tier >= 2 ? 0.5f : 0.36f;
            float k = Mathf.Min(1, t / dur);
            k = 1 - (1 - k) * (1 - k) * (1 - k);
            var er = r.Translated(new Vector2(0, (1 - k) * (Size.Y - r.Position.Y + 30)));
            Transform(this, er, (1 - k) * 0.4f, Vector2.One);
            Art.CardBack(this, er, c.Rarity);
            DrawSetTransformMatrix(_base);
            return;
        }
        if (_phase == Phase.Charged)
        {
            float k = t / (float)ChargeTime;
            Fx.Beams(this, m, col, 16, Size.Length(), 0.04f + k * 0.05f, (float)T * (0.4f + k * 2.5f), 0.06f + k * 0.14f);
            Fx.Glow(this, m, r.Size.Y * (0.55f + k * 0.3f), col, 0.1f + k * 0.25f);
            var jitter = new Vector2(_fx.Rng.Next(-1, 2), _fx.Rng.Next(-1, 2)) * k * (2 + Tier);
            var cr = r.Translated(jitter.Round());
            float pulse = Mathf.Floor(t * (8 + k * 14)) % 2;
            for (int i = 3; i >= 1; i--) DrawRect(cr.Grow(i * (5 + k * 6)), new Color(col, (0.06f + 0.1f * pulse) * (0.5f + k)));
            Art.CardBack(this, cr, c.Rarity);
            return;
        }
        if (Tier >= 1) Fx.Beams(this, m, col, 12, Size.Length(), 0.045f, (float)T * 0.25f, Tier >= 3 ? 0.11f : 0.07f);
        Fx.Glow(this, m, r.Size.Y * 0.62f, col, 0.1f + Tier * 0.05f);
        if (_phase == Phase.Flip)
        {
            float k = Mathf.Min(1, t / 0.32f);
            float sx = Mathf.Abs(Mathf.Cos(k * Mathf.Pi));
            float lift = Mathf.Sin(k * Mathf.Pi) * 0.12f;
            Transform(this, r, 0, new Vector2(Mathf.Max(0.02f, sx) * (1 + lift), 1 + lift));
            if (k < 0.5f) Art.CardBack(this, r, c.Rarity);
            else Art.Card(this, r, c, _kit, 0.6f);
            DrawSetTransformMatrix(_base);
            return;
        }
        // Face up: floating, swaying a touch, the glow breathing.
        float bob = Mathf.Sin((float)T * 2) * 3;
        float rot = Mathf.Sin((float)T * 1.3f) * 0.022f;
        float s = 1 + (float)_punch * 0.12f;
        DrawColoredPolygon(Px.Ellipse(PedestalTop(), r.Size.X * 0.42f - bob, 8, 20), new Color(0, 0, 0, 0.4f));
        Transform(this, r.Translated(new Vector2(0, bob)), rot, new Vector2(s, s));
        Art.Card(this, r, c, _kit, Tier >= 1 ? 0.7f + 0.3f * Mathf.Sin((float)T * 3) : 0.3f);
        DrawSetTransformMatrix(_base);
        Caption(c, r);
    }

    void Caption(Card c, Rect2 r)
    {
        float k = Mathf.Min(1, (float)_t / 0.25f);
        float off = (1 - k) * (1 - k) * 80;
        float x = r.End.X + 44 + off, y = r.Position.Y + 20;
        var col = Tier >= 4 ? Hue : Col(Tier);
        string label = Cards.Label(c.Rarity).ToUpperInvariant() + (c.Position == Pos.GK ? " · GOALKEEPER" : "");
        float lw = Px.Width(Px.Big, label, 20) + 20;
        Px.Frame(this, new Rect2(x, y, lw, 28), col, col.Darkened(0.5f), new Color(0, 0, 0, 0.5f), 2, 4);
        Px.Text(this, Px.Big, new Vector2(x + 10, y + 21), label, 20, col.Luminance > 0.55f ? Px.Dark : Px.Ink);
        y += 74;
        foreach (var line in Px.Wrap(Px.Big, c.Name, 46, Size.X - x - 20))
        {
            Px.Text(this, Px.Big, new Vector2(x, y), line, 46, Px.Ink, new Color(0, 0, 0, 0.6f), 3);
            y += 40;
        }
        var n = Cards.Nations[c.Nation];
        Px.Flag(this, new Rect2(x, y - 4, 24, 16), n);
        Px.Text(this, Px.Small, new Vector2(x + 32, y + 9), $"{n.Code} · {c.Height} CM · {c.Weight} KG · {Cards.BodyName(c).ToUpperInvariant()}", 9, Px.InkDim);
        y += 34;
        foreach (var tr in Cards.Traits(c))
        {
            float w = Px.Width(Px.Big, tr, 20) + 16;
            Px.Frame(this, new Rect2(x, y - 20, w, 28), new Color(col, 0.25f), col, null, 2);
            Px.Text(this, Px.Big, new Vector2(x + 8, y), tr, 20, Px.Ink);
            x += w + 8;
        }
        // Playstyles, each with what it adds.
        float px0 = r.End.X + 44 + off;
        foreach (var ps in Playstyles.Of(c))
        {
            y += 38;
            Art.Playstyle(this, new Vector2(px0 + 14, y - 6), 13, ps);
            Px.Text(this, Px.Big, new Vector2(px0 + 34, y), ps.Name, 22, Px.Hex(ps.Color));
            Px.Text(this, Px.Small, new Vector2(px0 + 40 + Px.Width(Px.Big, ps.Name, 22), y - 1), Playstyles.Describe(ps).ToUpperInvariant(), 8, Px.InkDim);
        }
        if (T % 1 < 0.7)
            Px.TextC(this, Px.Big, Size.X / 2, Size.Y - 12, _i < _cards.Count - 1 ? "TAP FOR NEXT" : "TAP TO FINISH", 22, Px.Ink, new Color(0, 0, 0, 0.6f), 2);
    }

    void SummaryStage()
    {
        float W = Size.X, H = Size.Y;
        Px.TextC(this, Px.Big, W / 2, 54, _pack.Name.ToUpperInvariant(), 40, Px.Ink, new Color(0, 0, 0, 0.6f), 3);
        int n = _cards.Count;
        var deck = new Vector2(W / 2, H + 80);
        for (int i = 0; i < n; i++)
        {
            var c = _cards[i];
            int tier = (int)c.Rarity;
            var r = SummaryRect(i);
            // Dealt out from below, then flipped in turn.
            float dk = Mathf.Clamp(((float)_t - i * 0.06f) / 0.3f, 0, 1);
            if (dk <= 0) continue;
            dk = 1 - (1 - dk) * (1 - dk) * (1 - dk);
            var at = deck.Lerp(r.GetCenter(), dk) - r.Size / 2;
            var dr = new Rect2(at, r.Size);
            bool best = i == n - 1;
            bool face = i < _flipped;
            if (face && tier >= 1)
            {
                var col = tier >= 4 ? Hue : Col(tier);
                if (best) Fx.Beams(this, r.GetCenter(), col, 12, r.Size.Y * 1.3f, 0.06f, (float)T * 0.4f, 0.12f);
                Fx.Glow(this, r.GetCenter(), r.Size.Y * 0.6f, col, 0.06f + tier * 0.04f);
            }
            float fk = face ? Mathf.Min(1, ((float)_t - (float)FlipAt(i)) / 0.2f) : 0;
            float sx = face ? Mathf.Abs(Mathf.Cos((0.5f + fk * 0.5f) * Mathf.Pi)) : 1;
            Transform(this, dr, (i - (n - 1) / 2f) * 0.035f * (1 - fk * 0.5f), new Vector2(Mathf.Max(0.05f, sx), 1));
            if (face) Art.Card(this, dr, c, _kit, best ? 0.8f : 0);
            else Art.CardBack(this, dr, c.Rarity);
            DrawSetTransformMatrix(_base);
            if (face) Tap("card" + i, r, () => _ui.OpenPlayer(c));
        }
        if (_flipped < n) return;
        DrawSetTransformMatrix(Transform2D.Identity);
        float by = H - 56;
        bool again = _pack.Price > 0 && _ui.Club.S.Coins >= _pack.Price;
        float bw = again ? 200 : 0;
        float total = 140 + 170 + bw + (again ? 32 : 16);
        float bx = (W - total) / 2;
        GhostButton("done", new Rect2(bx, by, 140, 44), "DONE", 24, Close);
        bx += 156;
        if (again)
        {
            GoldButton("again", new Rect2(bx, by, bw, 44), $"OPEN ANOTHER · {Px.Thousands(_pack.Price)}", 20, () =>
            {
                Close();
                _ui.Store.Buy(_pack);
            });
            bx += bw + 16;
        }
        GhostButton("squad", new Rect2(bx, by, 170, 44), "GO TO SQUAD", 22, () =>
        {
            Close();
            _ui.Go(_ui.Squad);
        });
    }
}
