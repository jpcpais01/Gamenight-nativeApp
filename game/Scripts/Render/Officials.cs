using System;
using Godot;
using GameNight.Sim;

namespace GameNight.Render;

/// <summary>
/// Referee and assistant referees (the PWA's src/render/officials.ts). Purely presentational:
/// they use the same movement as players (so they run, turn and side-step naturally) but they're
/// not part of the match; the ball and players pass straight through them and nobody reacts.
///
/// - Referee: the diagonal system, 12–18 m from the ball, on the diagonal between the left wing
///   of one half and the right wing of the other; jogs, sprints when play breaks away, faces the
///   ball; points the way for a restart, arm out for advantage.
/// - Assistants: one per touchline, each covering a half, side-stepping along the line level with
///   the second-last defender (or the ball beyond him); the flag goes up for offside and for the
///   ball out on his half.
/// Drawn by their own body view; moved on the game thread from the match snapshot.
/// </summary>
public sealed class Officials
{
    static readonly Kit RefKit = new() { Shirt = 0x17181b, Shirt2 = 0xf2c94c, Shorts = 0x17181b, Socks = 0x17181b, GkShirt = 0x17181b, GkShorts = 0x17181b };

    readonly Player _ref;
    readonly Player[] _lines;
    readonly Player[] _all;
    readonly PlayersView _view;
    readonly MatchSnapshot _snap = new();
    float _refPoint, _refSide = 1;
    readonly float[] _flagUp = new float[2];
    bool _adv;
    Phase? _lastPhase;

    public Officials(Node3D root)
    {
        static Attributes Attrs() => new()
        {
            Pace = 0.55, Accel = 0.55, Control = 0.5, Passing = 0.5, Shooting = 0.3, Strength = 0.6, Defending = 0.3,
            Keeping = 0.2, Agility = 0.5, Stamina = 0.8, Jumping = 0.4, Power = 0.4, Height = 1.8, Weight = 76,
        };
        static Look Look(int skin, int hair, int style) => new() { Skin = skin, Hair = hair, HairStyle = style, Height = 1, Build = 1 };
        _ref = new Player(0, 2, 0, Role.MID, 0, 0, Attrs(), Look(0xe0ac7e, 0x2e1f15, 1));
        _lines = new[]
        {
            new Player(1, 2, 1, Role.MID, 0, 0, Attrs(), Look(0xc68a5c, 0x1b1410, 0)),
            new Player(2, 2, 2, Role.MID, 0, 0, Attrs(), Look(0xf1c9a5, 0x7a5532, 1)),
        };
        _all = new[] { _ref, _lines[0], _lines[1] };

        _view = new PlayersView(root, false) { RefSlot = 0, LineSlots = new[] { 1, 2 }, Bodies = 3 };
        _view.AddFlags(root);
        foreach (var o in _all) _view.SetBody(o.Id, o, RefKit, -1, false);
        _view.Flush();
        for (int i = 0; i < 3; i++)
        {
            _snap.Active[i] = true;
            _snap.Team[i] = 2;
            _snap.Role[i] = Role.MID;
            _snap.Height[i] = 1;
            _snap.Build[i] = 1;
            _snap.Stamina[i] = 1;
            _snap.Foot[i] = 1;
            _snap.SinceTouch[i] = 99;
            _snap.PullT[i] = -1;
        }
        Reset();
    }

    public bool Visible
    {
        set => _view.Visible = value;
    }

    public void Reset()
    {
        _ref.Pos.Set(-4, 0, -9);
        _lines[0].Pos.Set(0, 0, -(Pitch.HalfW + 1.2)); // far touchline (in view)
        _lines[1].Pos.Set(0, 0, Pitch.HalfW + 1.2); // near touchline
        foreach (var o in _all)
        {
            o.PrevPos.Copy(o.Pos);
            o.Vel.Set(0, 0, 0);
        }
        _view.Snap();
        _lastPhase = null;
    }

    /// <summary>The foul behind the current stoppage, if any.</summary>
    static Foul RecentFoul(Match m, MatchSnapshot s)
    {
        var f = m.LastFoul;
        var off = m.LastOffside;
        if (f != null && off != null && off.Time > f.Time) return null;
        return f != null && s.Time - f.Time < 12 && (s.Phase == Phase.Out || s.SetPiece == SetPieceKind.FreeKick || s.SetPiece == SetPieceKind.Penalty) ? f : null;
    }

    /// <summary>Second-last defender line (x) of the team defending the goal at side s (±1).</summary>
    static double OffsideLine(MatchSnapshot s, int side)
    {
        int team = s.Dir[0] == side ? 1 : 0; // the team whose goal is at side s
        double a = -1e9, b = -1e9;
        for (int i = 0; i < MatchSnapshot.N; i++)
        {
            if (!s.Active[i] || s.Team[i] != team) continue;
            double v = s.X[i] * side;
            if (v > a)
            {
                b = a;
                a = v;
            }
            else if (v > b) b = v;
        }
        return b * side;
    }

    /// <summary>Move them on (dt 0 holds them), then draw them. The match objects read here (fouls,
    /// offsides, advantage) are replaced, never changed, by the match thread.</summary>
    public void Update(Match m, MatchSnapshot s, double time, float dt)
    {
        if (dt > 0) Step(m, s, MathF.Min(dt, 1 / 30f));
        for (int i = 0; i < 3; i++)
        {
            var o = _all[i];
            _snap.X[i] = (float)o.Pos.X;
            _snap.Z[i] = (float)o.Pos.Z;
            _snap.VX[i] = (float)o.Vel.X;
            _snap.VZ[i] = (float)o.Vel.Z;
            _snap.Facing[i] = (float)o.Facing;
            _snap.Speed[i] = (float)o.Speed;
            _snap.HasLook[i] = o.LookAt != null;
            _snap.LookX[i] = o.LookAt != null ? (float)o.LookAt.X : 0;
            _snap.LookZ[i] = o.LookAt != null ? (float)o.LookAt.Z : 0;
            _snap.StridePhase[i] = (float)o.StridePhase;
            _snap.LeanFwd[i] = (float)o.LeanFwd;
            _snap.LeanSide[i] = (float)o.LeanSide;
            _snap.AccelFwd[i] = (float)o.AccelFwd;
            _snap.Sprinting[i] = o.Sprinting;
        }
        _snap.BallX = s.BallX;
        _snap.BallY = s.BallY;
        _snap.BallZ = s.BallZ;
        _snap.BallVX = s.BallVX;
        _snap.BallVY = s.BallVY;
        _snap.BallVZ = s.BallVZ;
        _snap.Time = s.Time;
        _snap.Phase = s.Phase == Phase.Goal || s.Phase == Phase.Fulltime ? Phase.Play : s.Phase;
        _snap.Scorer = _snap.Controlled = _snap.Owner = _snap.HeldBy = _snap.DeadBallTaker = -1;
        _view.RefPoint = _refPoint;
        _view.RefPointSide = _refSide;
        _view.FlagUp[0] = _flagUp[0];
        _view.FlagUp[1] = _flagUp[1];
        // Drawn where they are now (they're moved here, every frame: nothing to interpolate).
        _view.Update(_snap, _snap, 0, time, 1);
    }

    void Step(Match m, MatchSnapshot s, float dt)
    {
        double bx = s.BallX, bz = s.BallZ;

        // Whistled restart: the referee points the way it goes; the linesman on that side
        // raises his flag for a throw / goal kick / corner on his half.
        if (s.Phase != _lastPhase)
        {
            if (s.Phase == Phase.Out || (s.Phase == Phase.SetPiece && _lastPhase != Phase.Out))
            {
                _refPoint = 2.2f;
                var foul = RecentFoul(m, s);
                var off = m.LastOffside != null && s.Time - m.LastOffside.Time < 0.5 ? m.LastOffside : null;
                int att = off != null ? off.Team : foul != null ? foul.Victim.Team : s.SetPiece != null ? s.SetPieceTeam : s.PossTeam;
                _refSide = att >= 0 && att < 2 ? s.Dir[att] : 1;
                int li = bz < 0 ? 0 : 1;
                // Offside: the linesman on that half holds his flag up.
                if (off != null) _flagUp[off.X > 0 ? 0 : 1] = 2.6f;
                else if (foul == null && (Math.Abs(bz) > Pitch.HalfW - 1 || Math.Abs(bx) > Pitch.HalfL - 1)) _flagUp[li] = 2.0f;
            }
            _lastPhase = s.Phase;
        }
        // Advantage: arm out toward the fouled side's attack, play on.
        var adv = m.Advantage;
        if (adv != null && !_adv)
        {
            _refPoint = 1.6f;
            _refSide = s.Dir[adv.Team];
        }
        _adv = adv != null;
        _refPoint = MathF.Max(0, _refPoint - dt);
        _flagUp[0] = MathF.Max(0, _flagUp[0] - dt);
        _flagUp[1] = MathF.Max(0, _flagUp[1] - dt);

        // ---- referee
        var r = _ref;
        double tx, tz;
        if (s.Phase == Phase.Kickoff || s.Phase == Phase.Halftime)
        {
            tx = -3;
            tz = -10;
        }
        else if (s.Phase == Phase.Goal)
        {
            tx = bx * 0.3;
            tz = -6;
        }
        else if (s.Phase == Phase.Fulltime)
        {
            tx = r.Pos.X;
            tz = r.Pos.Z;
        }
        else
        {
            // Diagonal system: from the far-left corner region to the near-right one.
            double t = Math.Clamp(bx / Pitch.HalfL, -1, 1);
            double diagZ = -t * 14;
            tx = bx - Math.Sign(bx == 0 ? 1 : bx) * 6;
            tz = diagZ;
            // Keep 12-18 m from the ball and out of the play.
            double dx = tx - bx, dz = tz - bz;
            double d = Math.Max(0.1, Math.Sqrt(dx * dx + dz * dz));
            double want = Math.Clamp(d, 12, 18);
            tx = bx + dx / d * want;
            tz = bz + dz / d * want;
            // After a foul he goes to the spot to manage the free kick (or to the box for a penalty).
            var foul = RecentFoul(m, s);
            if (foul != null && (s.Phase == Phase.Out || s.Phase == Phase.SetPiece))
            {
                double fx = foul.Penalty ? Math.Sign(foul.X) * (Pitch.HalfL - 14) : foul.X;
                double fz = foul.Penalty ? -6 : foul.Z;
                tx = fx + (fx > 0 ? -5 : 5);
                tz = fz + (fz > 0 ? -4 : 4);
            }
            if (s.SetPiece == SetPieceKind.Corner)
            {
                tx = Math.Sign(s.SetPieceX) * (Pitch.HalfL - 14);
                tz = -Math.Sign(s.SetPieceZ) * 6;
            }
            tx = Math.Clamp(tx, -Pitch.HalfL + 4, Pitch.HalfL - 4);
            tz = Math.Clamp(tz, -Pitch.HalfW + 3, Pitch.HalfW - 3);
        }
        Steer(r, tx, tz, 18);
        r.LookTarget.Set(bx, 0, bz);
        r.LookAt = r.LookTarget;

        // ---- assistant referees
        for (int i = 0; i < 2; i++)
        {
            var o = _lines[i];
            // Line 0 covers the half at +x on the far touchline, line 1 the half at -x (near).
            int side = i == 0 ? 1 : -1;
            double off = OffsideLine(s, side);
            double x = side > 0 ? Math.Max(Math.Max(off, bx), 0) : Math.Min(Math.Min(off, bx), 0);
            if (s.Phase == Phase.Kickoff) x = side * 12;
            x = Math.Clamp(x, -Pitch.HalfL, Pitch.HalfL);
            double z = (i == 0 ? -1 : 1) * (Pitch.HalfW + 1.2);
            Steer(o, x, z, 10);
            // Face across the pitch so moving along the line is a side-step.
            o.LookTarget.Set(o.Pos.X, 0, 0);
            o.LookAt = o.LookTarget;
        }

        foreach (var o in _all) o.Move(dt);
    }

    static void Steer(Player o, double x, double z, double sprintAt)
    {
        double dx = x - o.Pos.X, dz = z - o.Pos.Z;
        double d = Math.Sqrt(dx * dx + dz * dz);
        if (d < 0.4)
        {
            o.MoveX = o.MoveZ = o.WantSpeed = 0;
            return;
        }
        o.MoveX = dx / d;
        o.MoveZ = dz / d;
        o.WantSpeed = Math.Min(d > sprintAt ? o.TopSpeed : PlayerK.JogSpeed * 0.9, d * 1.4 + 0.4);
        o.Sprinting = d > sprintAt;
    }
}
