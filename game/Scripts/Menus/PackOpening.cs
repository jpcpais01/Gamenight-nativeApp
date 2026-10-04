using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GameNight.Club;
using Pos = GameNight.Club.Position;

namespace GameNight.Menus;

/// <summary>
/// Opening a pack (the PWA's store.ts Opening): tap the pack three times to charge it, it
/// bursts, then each card flies in face down and flips, the good ones after a walkout (nation,
/// position, rating). The best card is always last. Skip jumps to the summary.
/// </summary>
public sealed partial class PackOpening : PxCanvas
{
    enum Phase { Tease, Burst, Walkout, Enter, Charged, Flip, Shown, Summary }

    struct Bit
    {
        public Vector2 P, V;
        public Color C;
        public float Life, Max, Size, Gravity;
    }

    readonly Menus _ui;
    readonly PackDef _pack;
    readonly List<Card> _cards;
    readonly Sim.Kit _kit;
    readonly int _bestTier;
    readonly List<Bit> _bits = new();
    readonly Random _rng = new();
    Phase _phase = Phase.Tease;
    double _t, _shakeT = 9, _flash, _flashMax = 1, _quake;
    int _taps, _i, _walkStep;
    Color _tint = Colors.White;

    public PackOpening(Menus ui, PackDef pack, List<Card> cards)
    {
        _ui = ui;
        _pack = pack;
        _cards = cards;
        _kit = ui.Club.Info().Kit;
        _bestTier = (int)cards[^1].Rarity;
    }

    Card Cur => _cards[Math.Min(_i, _cards.Count - 1)];
    int Tier => (int)Cur.Rarity;
    Vector2 Mid => Size / 2;

    // ---------------------------------------------------------------- flow

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += delta;
        _shakeT += delta;
        _flash = Math.Max(0, _flash - delta);
        _quake = Math.Max(0, _quake - delta);
        for (int i = _bits.Count - 1; i >= 0; i--)
        {
            var b = _bits[i];
            b.Life -= dt;
            if (b.Life <= 0)
            {
                _bits.RemoveAt(i);
                continue;
            }
            b.V.Y += b.Gravity * dt;
            b.V *= Mathf.Pow(0.4f, dt);
            b.P += b.V * dt;
            _bits[i] = b;
        }
        switch (_phase)
        {
            case Phase.Burst when _t > 0.7: Begin(); break;
            case Phase.Walkout when _t > (Tier >= 3 ? 1.15 : 0.95): NextWalk(); break;
            case Phase.Enter when _t > (Tier >= 2 ? 0.7 : 0.38):
                if (Tier >= 2) To(Phase.Charged);
                else Flip();
                break;
            case Phase.Charged when _t > 0.6: Flip(); break;
            case Phase.Flip when _t > 0.3: To(Phase.Shown); break;
        }
        base._Process(delta);
    }

    void To(Phase p)
    {
        _phase = p;
        _t = 0;
    }

    void Flash(double strength)
    {
        _flash = _flashMax = 0.35 * strength + 0.1;
    }

    void Charge()
    {
        _taps++;
        _shakeT = 0;
        _tint = _taps >= 2 && _bestTier >= 2 ? Art.RarityColor((Rarity)_bestTier) : _bestTier >= 1 && _taps >= 2 ? Px.Hex(0xe6f1ff) : Colors.White;
        Embers(Mid, _tint, 6 + _taps * 5, 220);
        Input.VibrateHandheld(20 + _taps * 20);
        if (_taps >= 3) Burst();
    }

    void Burst()
    {
        var c = _bestTier >= 2 ? Art.RarityColor((Rarity)_bestTier) : Colors.White;
        _tint = c;
        Flash(1);
        Spray(Mid, new[] { c, Colors.White }, 90 + _bestTier * 30, 520 + _bestTier * 60);
        Input.VibrateHandheld(120);
        To(Phase.Burst);
    }

    /// <summary>The next card: a walkout for the good ones, then it flies in.</summary>
    void Begin()
    {
        _tint = Tier >= 1 ? Art.RarityColor(Cur.Rarity) : Colors.White;
        _walkStep = 0;
        if (Tier >= 2)
        {
            To(Phase.Walkout);
            Flash(0.35);
        }
        else To(Phase.Enter);
    }

    void NextWalk()
    {
        _walkStep++;
        int steps = Tier >= 3 ? 3 : 2;
        if (_walkStep >= steps) To(Phase.Enter);
        else
        {
            _t = 0;
            Flash(0.35);
            Embers(Mid, _tint, 10, 160);
        }
    }

    void Flip()
    {
        To(Phase.Flip);
        var c = Art.RarityColor(Cur.Rarity);
        var cols = Tier >= 4 ? new[] { Px.Hex(0x7ff6ff), Px.Hex(0xff7ae6), Px.Hex(0xfff27a), Colors.White } : new[] { c, Colors.White };
        Spray(CardRect().GetCenter(), cols, 40 + Tier * 30, 300 + Tier * 80);
        if (Tier >= 2) Flash(0.5 + Tier * 0.12);
        if (Tier >= 3)
        {
            _quake = 0.5;
            Confetti(cols, 80 + Tier * 20);
            Input.VibrateHandheld(160);
        }
    }

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
        _tint = Art.RarityColor((Rarity)_bestTier);
        To(Phase.Summary);
    }

    void Close()
    {
        _ui.Close(this);
    }

    protected override void Background() => OnTap();

    void OnTap()
    {
        switch (_phase)
        {
            case Phase.Tease: Charge(); break;
            case Phase.Walkout: NextWalk(); break;
            case Phase.Enter or Phase.Charged: Flip(); break;
            case Phase.Shown: Next(); break;
        }
    }

    // ---------------------------------------------------------------- particles

    void Embers(Vector2 at, Color c, int n, float speed)
    {
        for (int i = 0; i < n; i++)
        {
            float a = (float)(_rng.NextDouble() * Mathf.Tau);
            float s = speed * (0.4f + (float)_rng.NextDouble() * 0.8f);
            _bits.Add(new Bit { P = at, V = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s, C = c, Life = 0.7f, Max = 0.7f, Size = 3 + (float)_rng.NextDouble() * 3, Gravity = 60 });
        }
    }

    void Spray(Vector2 at, Color[] cols, int n, float speed)
    {
        for (int i = 0; i < n; i++)
        {
            float a = (float)(_rng.NextDouble() * Mathf.Tau);
            float s = speed * (0.3f + (float)_rng.NextDouble());
            float life = 0.6f + (float)_rng.NextDouble() * 0.6f;
            _bits.Add(new Bit { P = at, V = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s, C = cols[i % cols.Length], Life = life, Max = life, Size = 3 + (float)_rng.NextDouble() * 4, Gravity = 140 });
        }
    }

    void Confetti(Color[] cols, int n)
    {
        for (int i = 0; i < n; i++)
        {
            float life = 1.4f + (float)_rng.NextDouble();
            _bits.Add(new Bit
            {
                P = new Vector2((float)_rng.NextDouble() * Size.X, -10 - (float)_rng.NextDouble() * 80),
                V = new Vector2(((float)_rng.NextDouble() - 0.5f) * 80, 60 + (float)_rng.NextDouble() * 80),
                C = cols[i % cols.Length], Life = life, Max = life, Size = 4 + (float)_rng.NextDouble() * 3, Gravity = 90,
            });
        }
    }

    // ---------------------------------------------------------------- drawing

    Rect2 CardRect()
    {
        float h = Mathf.Min(Size.Y * 0.72f, 320), w = h / 1.4f;
        return new Rect2(new Vector2(Size.X * 0.36f - w / 2, Size.Y / 2 - h / 2).Round(), new Vector2(w, h).Round());
    }

    protected override void Paint()
    {
        float W = Size.X, H = Size.Y;
        var shake = _quake > 0 ? new Vector2(_rng.Next(-4, 5), _rng.Next(-4, 5)) : Vector2.Zero;
        DrawSetTransform(shake);
        Px.Bands(this, new Rect2(-8, -8, W + 16, H + 16), new[] { Px.Hex(0x08061c), Px.Hex(0x0d0a26), Px.Hex(0x120d30), Px.Hex(0x0d0a26) }, new[] { 0, 0.3f, 0.55f, 0.8f });
        Rays();
        Tap("stage", new Rect2(Vector2.Zero, Size), OnTap);

        switch (_phase)
        {
            case Phase.Tease: Tease(); break;
            case Phase.Burst: PackBig(1 + (float)_t * 0.6f, Mathf.Max(0, 1 - (float)_t * 2.5f)); break;
            case Phase.Walkout: Walkout(); break;
            case Phase.Enter or Phase.Charged or Phase.Flip or Phase.Shown: Reveal(); break;
            case Phase.Summary: SummaryView(); break;
        }

        foreach (var b in _bits)
        {
            float a = Mathf.Clamp(b.Life / b.Max * 1.5f, 0, 1);
            DrawRect(new Rect2(b.P.Round(), new Vector2(b.Size, b.Size).Round()), new Color(b.C, a));
        }
        if (_phase != Phase.Summary)
        {
            Px.TextR(this, Px.Small, W - 110, 28, _phase == Phase.Tease ? "" : $"{_i + 1} / {_cards.Count}", 9, Px.InkDim);
            GhostButton("skip", new Rect2(W - 96, 10, 82, 32), "SKIP >", 20, Summary);
        }
        DrawSetTransform(Vector2.Zero);
        if (_flash > 0) DrawRect(new Rect2(Vector2.Zero, Size), new Color(1, 1, 1, (float)(_flash / _flashMax) * 0.85f));
        Px.Scanlines(this, new Rect2(0, 0, W, H));
    }

    /// <summary>Slow stepped rays from the centre in the scene's light colour.</summary>
    void Rays()
    {
        var c = new Vector2(_phase == Phase.Summary || _phase == Phase.Tease || _phase == Phase.Burst || _phase == Phase.Walkout ? Size.X / 2 : CardRect().GetCenter().X, Size.Y / 2);
        float rot = Mathf.Floor((float)T * 4) / 4 * 0.12f;
        float R = Size.Length();
        for (int i = 0; i < 12; i++)
        {
            float a = rot + i * Mathf.Tau / 12;
            DrawColoredPolygon(new[] { c, c + new Vector2(Mathf.Cos(a - 0.09f), Mathf.Sin(a - 0.09f)) * R, c + new Vector2(Mathf.Cos(a + 0.09f), Mathf.Sin(a + 0.09f)) * R }, new Color(_tint, 0.07f));
        }
        foreach (var (k, al) in new[] { (0.5f, 0.06f), (0.32f, 0.08f), (0.18f, 0.1f) })
            DrawColoredPolygon(Px.Ellipse(c, Size.Y * k, Size.Y * k, 24), new Color(_tint, al));
    }

    void PackBig(float scale, float alpha)
    {
        if (alpha <= 0) return;
        float h = Size.Y * 0.62f * scale, w = h / 1.4f;
        var off = Vector2.Zero;
        if (_shakeT < 0.3) off = new Vector2(Mathf.Sin((float)_shakeT * 70) * 8 * (1 - (float)_shakeT / 0.3f) * _taps, 0);
        float bob = _phase == Phase.Tease ? Mathf.Floor((float)T * 2 % 4) switch { 1 => -3, 2 => -6, 3 => -3, _ => 0 } : 0;
        var r = new Rect2(Mid - new Vector2(w, h) / 2 + off + new Vector2(0, bob), new Vector2(w, h));
        // Aura grows with each tap.
        for (int i = 3; i >= 1; i--) DrawRect(r.Grow(i * 6 * (1 + _taps * 0.5f)), new Color(_tint, 0.06f * (1 + _taps)));
        Art.Pack(this, r, _pack, _taps / 3f);
        if (alpha < 1) DrawRect(r.Grow(4), new Color(1, 1, 1, 1 - alpha));
    }

    void Tease()
    {
        PackBig(1, 1);
        string hint = _taps == 0 ? "TAP TO OPEN" : _taps == 1 ? "AGAIN!" : "ONE MORE!";
        if ((T % 1) < 0.7) Px.TextC(this, Px.Big, Size.X / 2, Size.Y - 22, hint, 30, Px.Ink, new Color(0, 0, 0, 0.6f), 3);
    }

    void Walkout()
    {
        var c = Cur;
        var col = Art.RarityColor(c.Rarity);
        float k = Mathf.Min(1, (float)_t / 0.15f);
        float s = 0.6f + 0.4f * Mathf.Floor(k * 4) / 4;
        var m = Mid;
        if (_walkStep == 0)
        {
            var n = Cards.Nations[c.Nation];
            var fs = new Vector2(150, 100) * s;
            Px.Flag(this, new Rect2(m - fs / 2 - new Vector2(0, 20), fs), n);
            Px.TextC(this, Px.Big, m.X, m.Y + 72 * s, n.Code, (int)(48 * s), col, new Color(0, 0, 0, 0.6f), 3);
        }
        else if (_walkStep == 1) Px.TextC(this, Px.Big, m.X, m.Y + 50 * s, c.Position.ToString(), (int)(150 * s), col, new Color(0, 0, 0, 0.6f), 5);
        else Px.TextC(this, Px.Big, m.X, m.Y + 60 * s, c.Overall.ToString(), (int)(190 * s), col, new Color(0, 0, 0, 0.6f), 6);
    }

    void Reveal()
    {
        var c = Cur;
        var r = CardRect();
        float t = (float)_t;
        if (_phase == Phase.Enter)
        {
            // Flies in from below in steps.
            float dur = Tier >= 2 ? 0.7f : 0.38f;
            float k = Mathf.Floor(Mathf.Min(1, t / dur) * 6) / 6;
            r.Position += new Vector2(0, (1 - k) * (Size.Y - r.Position.Y + 20));
            Art.CardBack(this, r, c.Rarity);
            return;
        }
        if (_phase == Phase.Charged)
        {
            float pulse = (Mathf.Floor(t * 10) % 2) * 0.5f + 0.5f;
            for (int i = 3; i >= 1; i--) DrawRect(r.Grow(i * 8), new Color(Art.RarityColor(c.Rarity), 0.12f * pulse));
            Art.CardBack(this, r, c.Rarity);
            return;
        }
        if (_phase == Phase.Flip)
        {
            float k = Mathf.Min(1, t / 0.3f);
            float sx = Mathf.Abs(Mathf.Cos(k * Mathf.Pi));
            var fr = new Rect2(r.GetCenter().X - r.Size.X * sx / 2, r.Position.Y, Mathf.Max(2, r.Size.X * sx), r.Size.Y);
            if (k < 0.5f) Art.CardBack(this, fr, c.Rarity);
            else if (sx > 0.3f) Art.Card(this, fr, c, _kit, 0.6f);
            else DrawRect(fr, Art.RarityColor(c.Rarity));
            return;
        }
        Art.Card(this, r, c, _kit, Tier >= 1 ? 1 : 0.4f);
        // The caption, to the right.
        float x = r.End.X + 34, y = r.Position.Y + 40;
        Px.Text(this, Px.Small, new Vector2(x, y), (Cards.Label(c.Rarity) + (c.Position == Pos.GK ? " · GOALKEEPER" : "")).ToUpperInvariant(), 9, Art.RarityColor(c.Rarity));
        y += 44;
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
            Px.Frame(this, new Rect2(x, y - 20, w, 28), new Color(Art.RarityColor(c.Rarity), 0.25f), Art.RarityColor(c.Rarity), null, 2);
            Px.Text(this, Px.Big, new Vector2(x + 8, y), tr, 20, Px.Ink);
            x += w + 8;
        }
        if ((T % 1) < 0.7)
            Px.TextC(this, Px.Big, Size.X / 2, Size.Y - 18, _i < _cards.Count - 1 ? "TAP FOR NEXT" : "TAP TO FINISH", 24, Px.Ink, new Color(0, 0, 0, 0.6f), 2);
    }

    void SummaryView()
    {
        float W = Size.X, H = Size.Y;
        Px.TextC(this, Px.Big, W / 2, 50, _pack.Name.ToUpperInvariant(), 40, Px.Ink, new Color(0, 0, 0, 0.6f), 3);
        var list = Enumerable.Reverse(_cards).ToList();
        float gap = 14;
        float ch = Mathf.Min(H - 160, (W - 40 - gap * (list.Count - 1)) / list.Count * 1.4f);
        float cw = ch / 1.4f;
        float total = list.Count * cw + (list.Count - 1) * gap;
        float x = (W - total) / 2, y = 72;
        for (int i = 0; i < list.Count; i++)
        {
            // Dealt in one by one.
            if (_t < i * 0.09) continue;
            var c = list[i];
            var r = new Rect2(x + i * (cw + gap), y, cw, ch);
            Art.Card(this, r, c, _kit, i == 0 ? 1 : 0);
            Tap("card" + i, r, () => _ui.OpenPlayer(c));
        }
        float by = H - 58;
        bool again = _pack.Price > 0 && _ui.Club.S.Coins >= _pack.Price;
        float bw = again ? 190 : 0;
        float total2 = 140 + 160 + bw + (again ? 32 : 16);
        float bx = (W - total2) / 2;
        GhostButton("done", new Rect2(bx, by, 140, 44), "DONE", 24, Close);
        bx += 156;
        if (again)
        {
            GoldButton("again", new Rect2(bx, by, bw, 44), $"AGAIN · {Px.Thousands(_pack.Price)}", 22, () =>
            {
                Close();
                _ui.Store.Buy(_pack);
            });
            bx += bw + 16;
        }
        GhostButton("squad", new Rect2(bx, by, 160, 44), "GO TO SQUAD", 22, () =>
        {
            Close();
            _ui.Go(_ui.Squad);
        });
    }
}
