using System;
using Godot;
using GameNight.Sim;

namespace GameNight.Render;

/// <summary>
/// The broadcast camera, ported from the PWA's cameraRig.ts.
///
/// In play it frames the way a broadcast operator does:
/// - the subject is the ball, led into the direction of play and pulled partway toward the
///   player you control;
/// - a dead zone, so small touches don't move the picture;
/// - composition: the subject sits a little above centre (the controls cover the bottom);
/// - a safe frame keeping the active player and the ball in shot (the ball wins);
/// - a critically damped spring that stiffens with the ball's speed.
///
/// On top of that come the directed shots, each blended in with its own weight:
/// - a corner pulls back to take in the box and the delivery ring;
/// - a free kick, penalty or goal kick drops in over the taker's shoulder;
/// - a goal swings round in front of the scorer, then cuts to the scoring end's fans;
/// - half time and full time drift across the stadium.
///
/// The broadcast view moves in whole art pixels so the pixel grid never shimmers; the
/// remainder is handed to the display, which scrolls the upscaled picture by it. The directed
/// shots move freely (their remainder fades out with their weight).
/// </summary>
public sealed class MatchCamera
{
    const float HL = 52.5f, HW = 34f;
    const float PitchDeg = 27f;

    public static readonly float[] Presets = { 33f, 40f, 48f };

    public readonly Camera3D Camera;
    public float BaseDist = Presets[1];
    /// <summary>Art height in pixels (the snap step is one art pixel).</summary>
    public int PixelHeight = 270;
    /// <summary>Sub-pixel remainder of the snap, in art pixels.</summary>
    public float SubPixelX, SubPixelY;
    /// <summary>Snap to whole art pixels (one sample per pixel). Off with smooth pixels: the snap
    /// only holds the plane the camera looks at still, so everything nearer or farther judders.</summary>
    public bool Snap = true;
    /// <summary>Point on the pitch the broadcast view frames.</summary>
    public float FocusX => _tx;
    public float FocusZ => _tz;
    /// <summary>A ground-level shot is (partly) on: the near side of the ground can be seen.</summary>
    public bool GroundLevel => _pov > 0 || _front > 0;

    /// <summary>The stadium builder: the drone circles round to look across the pitch from this
    /// yaw (0 from the near side) at the stand being chosen. Null for a match.</summary>
    public float? Showcase;
    /// <summary>Something to follow instead of the ball (a pitch invader), or null.</summary>
    public Vector2? Follow;
    float _orbit = float.NaN, _dt;
    // The builder's camera as dragged: height (an angle), distance, and a shift over the ground.
    float _elev = El0, _dist = 1, _sway = 1, _swayGoal = 1, _side = 1;
    /// <summary>The builder's menus are hidden: frame the stadium in the middle of the screen.</summary>
    public bool Centred;
    Vector3 _pan, _panGoal;
    const float El0 = 0.6f;
    /// <summary>The builder's view with its menus hidden: 0 the drone, 1 down on the pitch looking
    /// up at the stands, 2 high over a corner taking in the ground and all round it.</summary>
    public int View;
    readonly float[] _viewW = { 1, 0, 0 };

    /// <summary>The builder: turn round the ground and tilt (radians), zoom (a factor), and slide
    /// across it (metres to the camera's right and away from it).</summary>
    public void Orbit(float yaw, float tilt, float zoom, Vector2 slide)
    {
        if (Showcase is not float want) return;
        Showcase = want + yaw;
        if (!float.IsNaN(_orbit)) _orbit += yaw;
        _elev = Math.Clamp(_elev + tilt, 0.12f, 1.4f);
        _dist = Math.Clamp(_dist * zoom, 0.35f, 1.6f);
        float a = float.IsNaN(_orbit) ? want : _orbit;
        var right = new Vector3(MathF.Cos(a), 0, -MathF.Sin(a));
        var away = new Vector3(-MathF.Sin(a), 0, -MathF.Cos(a));
        _panGoal += (right * slide.X + away * slide.Y) * _dist;
        if (_panGoal.Length() > 110) _panGoal = _panGoal.Normalized() * 110;
        _pan = _panGoal;
        _swayGoal = 0;
    }

    /// <summary>The builder: swing round to look at a stand, back over the ground's middle.</summary>
    public void Face(float yaw)
    {
        Showcase = yaw;
        _panGoal = Vector3.Zero;
        _swayGoal = 1;
    }

    float _tx, _tz, _vx, _vz, _aimX, _aimZ, _leadX, _leadZ;
    float _zoom = 1, _shake;
    float _aspect = 16f / 9f;
    float _baseFov = 30f;
    double _time;

    // Directed shots: weight (0 off, 1 on) and where each one stands and looks.
    float _pov, _front, _cine;
    int _lastCrowd;
    /// <summary>An away day: the home end (left) is team 1's, so team 0's goals cut to the right.</summary>
    public bool Away;
    Vector3 _povPos, _povLook, _frontPos, _frontLook;

    public MatchCamera(Camera3D camera)
    {
        Camera = camera;
        Camera.Fov = _baseFov;
        Camera.Near = 1f;
        Camera.Far = 600f;
        Place();
    }

    public void SetAspect(float aspect)
    {
        _aspect = aspect;
        // Narrow screens see less of the pitch side to side; pull back a little.
        _baseFov = aspect < 1.6f ? 36f : 30f;
        Camera.Fov = _baseFov;
    }

    /// <summary>A knock (the woodwork, a goal): a short shake that dies away.</summary>
    public void Bump(float amount) => _shake = MathF.Max(_shake, amount);

    static float Pitch => Mathf.DegToRad(PitchDeg);

    /// <summary>Who's attacking right now (Match.attackingTeam), -1 for a loose ball.</summary>
    static int Attacking(MatchSnapshot b)
    {
        if (b.HeldBy >= 0) return b.Team[b.HeldBy];
        if (b.Owner >= 0) return b.Team[b.Owner];
        if (b.SetPiece != null) return b.SetPieceTeam;
        if (b.PassTarget >= 0) return b.Team[b.PassTarget];
        return -1;
    }

    public void Update(MatchSnapshot a, MatchSnapshot b, float alpha, float dt)
    {
        _time += dt;
        _dt = dt;
        if (dt <= 0 || Showcase != null) { Place(); return; }
        float bx = Mathf.Lerp(a.BallX, b.BallX, alpha);
        float bz = Mathf.Lerp(a.BallZ, b.BallZ, alpha);
        if (Follow is Vector2 follow)
        {
            bx = follow.X;
            bz = follow.Y;
        }
        // After a goal: follow the scorer's celebration (then the crowd shot, then back to the field).
        bool goal = b.Phase == Phase.Goal && b.Scorer >= 0 && b.PhaseT < GoalSeq.Back;
        // (A 1v1 has two players of its own: the camera stays with the ball.)
        int ci = goal ? b.Scorer : Follow != null || b.Versus ? -1 : b.Controlled;
        float cx = ci >= 0 ? Mathf.Lerp(a.X[ci], b.X[ci], alpha) : bx;
        float cz = ci >= 0 ? Mathf.Lerp(a.Z[ci], b.Z[ci], alpha) : bz;
        // Aiming a corner: frame the ring in the box along with the taker at the flag.
        bool aimT = b.AimingCorner && b.HasSetPieceTarget;
        if (aimT)
        {
            cx = b.SetPieceTargetX;
            cz = b.SetPieceTargetZ;
        }
        float ballSpeed = MathF.Sqrt(b.BallVX * b.BallVX + b.BallVY * b.BallVY + b.BallVZ * b.BallVZ);

        float pitch = Pitch;
        float dist = BaseDist * _zoom;
        float fv = Mathf.DegToRad(_baseFov) / 2;
        float tanV = MathF.Tan(fv);
        float halfX = dist * tanV * _aspect;
        float halfZ = dist * tanV / MathF.Sin(pitch + 0.15f);

        // Lead: where play is going, smoothed so it doesn't flick on every touch.
        int att = Attacking(b);
        bool live = b.Phase == Phase.Play;
        float wantLX = live ? Math.Clamp(b.BallVX * 0.4f, -9, 9) + (att >= 0 ? b.Dir[att] * 3.5f : 0) : 0;
        float wantLZ = live ? Math.Clamp(b.BallVZ * 0.25f, -4, 4) : 0;
        float kl = 1 - MathF.Exp(-dt * 1.8f);
        _leadX += (wantLX - _leadX) * kl;
        _leadZ += (wantLZ - _leadZ) * kl;

        // Subject: the led ball, pulled toward the active player (less when he's far from it).
        float cd = MathF.Sqrt((cx - bx) * (cx - bx) + (cz - bz) * (cz - bz));
        float wc = goal ? 1 : aimT ? 0.55f : 0.4f * (1 - Math.Clamp((cd - 8) / 22, 0, 1));
        float sx = bx + _leadX + (cx - bx - _leadX) * wc;
        float sz = bz + _leadZ + (cz - bz - _leadZ) * wc;
        bool corner = b.Phase == Phase.SetPiece && b.SetPiece == SetPieceKind.Corner;
        if (corner)
        {
            // A corner: the penalty box is the picture. Aim at the middle of the box (or between
            // it and the delivery ring while it's being aimed); the safe frame below then slides
            // just far enough toward the flag to keep the taker in shot.
            int d = b.Dir[b.SetPieceTeam];
            float boxX = HL * d - d * 10;
            sx = aimT ? (boxX + cx) / 2 : boxX;
            sz = aimT ? cz * 0.5f : 0;
        }
        else if (!goal)
        {
            // Closing on a goal (or a set piece near one): lean the frame toward it, so the goal
            // and what's in front of it come into the picture with the ball.
            bool set = b.Phase == Phase.SetPiece && b.SetPiece != null;
            int team = set ? b.SetPieceTeam : att;
            if (team >= 0 && (live || set))
            {
                float gx = HL * b.Dir[team];
                float wg = 0.42f * (1 - SmoothStep(14, 44, MathF.Abs(gx - bx)));
                sx += (gx - sx) * wg;
                sz += (0 - sz) * wg * 0.6f;
            }
        }
        // Composition: subject above centre (the controls cover the bottom).
        sz += halfZ * 0.12f;

        // Dead zone around the current aim.
        float dzx = halfX * 0.12f, dzz = halfZ * 0.1f;
        float ex = sx - _aimX, ez = sz - _aimZ;
        if (MathF.Abs(ex) > dzx) _aimX += ex - MathF.Sign(ex) * dzx;
        if (MathF.Abs(ez) > dzz) _aimZ += ez - MathF.Sign(ez) * dzz;

        // Safe frame: the active player, then the ball (applied last so it wins).
        _aimX = KeepIn(_aimX, cx, halfX * 0.78f, halfX * 0.78f);
        // At a corner the framing is tight, so it uses what the camera really sees up and down
        // the pitch (a tilted camera sees much further to the far side than to the near).
        float camH = MathF.Sin(pitch) * dist, camD = MathF.Cos(pitch) * dist;
        float farExt = camH / MathF.Tan(MathF.Max(0.05f, pitch - fv)) - camD;
        float nearExt = camD - camH / MathF.Tan(pitch + fv);
        _aimZ = corner ? KeepIn(_aimZ, cz, farExt * 0.6f, nearExt * 0.45f) : KeepIn(_aimZ, cz, halfZ * 0.72f, halfZ * 0.5f);
        float bm = corner ? 0.9f : 1;
        _aimX = KeepIn(_aimX, bx, halfX * 0.82f * bm, halfX * 0.82f * bm);
        _aimZ = corner ? KeepIn(_aimZ, bz, farExt * 0.8f, nearExt * 0.86f) : KeepIn(_aimZ, bz, halfZ * 0.78f, halfZ * 0.55f);

        // Don't show more than a little beyond the pitch.
        // (At a corner the stand behind the goal may come in, so the goal sits inside the frame.)
        float mx = MathF.Max(0, HL + 8 - halfX * (corner ? 0.55f : 1));
        _aimX = Math.Clamp(_aimX, -mx, mx);
        if (!corner) _aimZ = Math.Clamp(_aimZ, -HW + halfZ * 0.55f - 6, HW - halfZ * 0.45f + 4);

        // Critically damped follow, stiffer when the ball is travelling; slow pans in a goal.
        float w = b.Phase == Phase.Goal ? 1.3f : 2.3f + MathF.Min(2.2f, ballSpeed * 0.1f);
        _vx += ((_aimX - _tx) * w * w - 2 * w * _vx) * dt;
        _vz += ((_aimZ - _tz) * w * w - 2 * w * _vz) * dt;
        _tx += _vx * dt;
        _tz += _vz * dt;
        // Never lose the ball, whatever the spring is doing.
        _tx = KeepIn(_tx, bx, halfX * 0.92f, halfX * 0.92f);
        _tz = corner ? KeepIn(_tz, bz, farExt * 0.9f, nearExt * 0.92f) : KeepIn(_tz, bz, halfZ * 0.9f, halfZ * 0.75f);

        // A corner pulls back a little, to take in the taker and the whole box.
        _zoom += ((corner ? 1.3f : 1) - _zoom) * (1 - MathF.Exp(-dt * 1.6f));
        _shake *= MathF.Exp(-dt * 6);

        UpdatePov(a, b, alpha, dt);
        UpdateFront(a, b, alpha, dt);
        // Half time, full time: a slow crane across the bowl. A goal: the scoring end's fans.
        int crowd = b.Phase == Phase.Goal && b.Scorer >= 0 && b.PhaseT >= GoalSeq.Crowd && b.PhaseT < GoalSeq.Back
            ? (b.Team[b.Scorer] == (Away ? 1 : 0) ? -1 : 1) : 0;
        bool cinematic = b.Phase is Phase.Halftime or Phase.Fulltime;
        if (crowd != 0) _lastCrowd = crowd;
        else if (cinematic) _lastCrowd = 0;
        _cine += ((cinematic || crowd != 0 ? 1 : 0) - _cine) * (1 - MathF.Exp(-dt * (crowd != 0 ? 1.9f : 1.5f)));
        if (_cine < 0.002f) _cine = _lastCrowd = 0;
        Place();
    }

    /// <summary>
    /// Lining up a free kick, penalty, corner or goal kick: the camera drops in behind the taker,
    /// over the shoulder on the ball's side, looking down the line of the shot at the goal (into
    /// the box for a corner, up the pitch for a goal kick). It holds still
    /// through his run-up, and the moment the ball is struck it eases back up.
    /// </summary>
    void UpdatePov(MatchSnapshot a, MatchSnapshot b, float alpha, float dt)
    {
        int t = b.DeadBallTaker;
        bool want = t >= 0 && b.SetPiece != null;
        // On his run-up the camera holds still where it was and lets him run into the ball.
        if (want && !(b.DeadBallRun && _pov > 0))
        {
            float tx = Mathf.Lerp(a.X[t], b.X[t], alpha), tz = Mathf.Lerp(a.Z[t], b.Z[t], alpha);
            float gx = b.DeadBallX, gz = b.DeadBallZ;
            float ux = gx - tx, uz = gz - tz;
            float n = MathF.Sqrt(ux * ux + uz * uz);
            if (n < 1e-4f) n = 1;
            ux /= n;
            uz /= n;
            float rx = -uz, rz = ux;
            float side = b.Foot[t], h = b.Height[t];
            bool gk = b.DeadBallKind == SetPieceKind.GoalKick, corner = b.DeadBallKind == SetPieceKind.Corner;
            // (A corner a little higher, to see over the crowd in the box.)
            // (A corner a touch closer in: the flag is right by the advertising boards.)
            float back = corner ? 2.1f : 2.7f;
            _povPos = new Vector3(tx - ux * back + rx * side * 1.05f, (gk ? 2.25f : corner ? 2.4f : 1.95f) * h, tz - uz * back + rz * side * 1.05f);
            // Between the ball and the goal mouth: ball low in the frame, goal and wall above it.
            float k = b.SetPiece == SetPieceKind.Penalty ? 0.75f : gk ? 0.3f : corner ? 0.55f : 0.62f;
            _povLook = new Vector3(Mathf.Lerp(b.SetPieceX, gx, k), 1.05f, Mathf.Lerp(b.SetPieceZ, gz, k));
        }
        // Quick cut in, a smooth crane back out as he runs up.
        float wantW = want ? 1 : 0;
        _pov += (wantW - _pov) * (1 - MathF.Exp(-dt * (want ? 3.2f : 2.0f)));
        if (_pov < 0.002f) _pov = 0;
    }

    /// <summary>
    /// The scorer's celebration: once his run is over the camera swings down and round to stand
    /// between him and the centre spot at chest height, the celebrating end's stands behind
    /// him, and drifts slowly across him. Held while the crowd shot takes over, then let go.
    /// </summary>
    void UpdateFront(MatchSnapshot a, MatchSnapshot b, float alpha, float dt)
    {
        int s = b.Scorer;
        float t = b.Phase == Phase.Goal && s >= 0 ? b.PhaseT : -1;
        bool want = t >= GoalSeq.Front && t < GoalSeq.Crowd + 1;
        if (s >= 0 && t >= 0 && t < GoalSeq.Crowd)
        {
            float x = Mathf.Lerp(a.X[s], b.X[s], alpha), z = Mathf.Lerp(a.Z[s], b.Z[s], alpha);
            float d = MathF.Sqrt(x * x + z * z);
            if (d < 1e-4f) d = 1;
            float u = Math.Clamp((t - (float)GoalSeq.Front) / (float)(GoalSeq.Crowd - GoalSeq.Front), 0, 1);
            float ang = MathF.Atan2(-z / d, -x / d) + (0.55f - 0.7f * u) * (z >= 0 ? 1 : -1);
            float h = b.Height[s];
            float r = 4.4f - 0.6f * u;
            _frontPos = new Vector3(x + MathF.Cos(ang) * r, 1.25f * h, z + MathF.Sin(ang) * r);
            _frontLook = new Vector3(x, 1.08f * h, z);
        }
        // A slow, swooping pan in; held through the cut to the crowd.
        _front += ((want ? 1 : 0) - _front) * (1 - MathF.Exp(-dt * (want ? 1.6f : 3f)));
        if (_front < 0.002f) _front = 0;
    }

    void Place()
    {
        float pitch = Pitch;
        float dist = BaseDist * _zoom;
        float fov = _baseFov;
        // Snap the look point to whole art pixels; keep the remainder for the display.
        float unit = 2 * dist * MathF.Tan(Mathf.DegToRad(fov) / 2) / PixelHeight;
        float unitZ = unit / MathF.Sin(pitch);
        float sxp = Snap ? MathF.Round(_tx / unit) * unit : _tx;
        float szp = Snap ? MathF.Round(_tz / unitZ) * unitZ : _tz;
        float subX = (_tx - sxp) / unit, subY = (szp - _tz) / unitZ;
        float shX = MathF.Sin((float)_time * 41) * _shake * 0.25f;
        float shY = MathF.Cos((float)_time * 37) * _shake * 0.2f;
        var pos = new Vector3(sxp + shX, MathF.Sin(pitch) * dist + shY, szp + MathF.Cos(pitch) * dist);
        var look = new Vector3(sxp, 0, szp);

        if (_pov > 0)
        {
            float k = Smooth(_pov);
            pos = pos.Lerp(_povPos, k);
            look = look.Lerp(_povLook, k);
            subX *= 1 - k;
            subY *= 1 - k;
            fov = Mathf.Lerp(fov, 46, k);
        }
        if (_front > 0)
        {
            float k = Smooth(_front);
            pos = pos.Lerp(_frontPos, k);
            look = look.Lerp(_frontLook, k);
            subX *= 1 - k;
            subY *= 1 - k;
            fov = Mathf.Lerp(fov, 38, k);
        }
        if (_cine > 0)
        {
            Vector3 cp, cl;
            if (_lastCrowd != 0)
            {
                // After a goal: from the edge of the box up at the celebrating end, drifting across it.
                float drift = MathF.Sin((float)_time * 0.35f) * 10;
                cp = new Vector3(_lastCrowd * (HL - 20), 5, 14 + drift * 0.4f);
                cl = new Vector3(_lastCrowd * (HL + 22), 10, drift);
            }
            else
            {
                // A slow crane sweep from the open near side across the bowl.
                float an = MathF.Sin((float)_time * 0.05f) * 0.55f;
                cp = new Vector3(MathF.Sin(an) * 78, 17 + MathF.Sin((float)_time * 0.07f) * 3, 22 + MathF.Cos(an) * 52);
                cl = new Vector3(-MathF.Sin(an) * 30, 9, -30);
            }
            float k = Smooth(_cine);
            pos = pos.Lerp(cp, k);
            look = look.Lerp(cl, k);
            subX *= 1 - k;
            subY *= 1 - k;
        }
        if (Showcase is float want)
        {
            // High over the ground, easing round the shortest way, swaying a little.
            if (float.IsNaN(_orbit)) _orbit = want;
            float ease = 1 - MathF.Exp(-_dt * 1.6f);
            _orbit += Mathf.Wrap(want - _orbit, -Mathf.Pi, Mathf.Pi) * ease;
            _pan = _pan.Lerp(_panGoal, ease);
            _sway += (_swayGoal - _sway) * ease;
            _side += ((Centred ? 0 : 1) - _side) * ease;
            float a = _orbit + MathF.Sin((float)_time * 0.13f) * 0.22f * _sway;
            float fh = MathF.Cos(_elev) / MathF.Cos(El0) * _dist, fv = MathF.Sin(_elev) / MathF.Sin(El0) * _dist;
            pos = new Vector3(MathF.Sin(a) * 158 * fh, Math.Max(4, 104 * fv), MathF.Cos(a) * 142 * fh) + _pan;
            // Aimed a little to the left of the stand, so it sits to the right of the builder's panel.
            var right = new Vector3(MathF.Cos(a), 0, -MathF.Sin(a));
            look = new Vector3(-MathF.Sin(_orbit) * 40, 8, -MathF.Cos(_orbit) * 30) - right * 26 * _dist * _side + _pan;
            fov = 40;

            // The other views, blended in by weight so a switch glides across.
            for (int i = 0; i < 3; i++) _viewW[i] += ((View == i ? 1 : 0) - _viewW[i]) * ease;
            if (_viewW[1] > 0.001f || _viewW[2] > 0.001f)
            {
                // On the pitch: standing near the centre circle, the stand towering up ahead.
                var ahead = new Vector3(-MathF.Sin(a), 0, -MathF.Cos(a));
                var p1 = -ahead * (6 + 22 * _dist) + new Vector3(_pan.X, 1.7f, _pan.Z);
                var l1 = p1 + ahead * 80 + new Vector3(0, 4 + 22 * (_elev - 0.12f), 0);
                // Over a corner: the bowl below and the surroundings out to the horizon.
                float c = a + 0.75f;
                var p2 = new Vector3(MathF.Sin(c) * 300 * fh, Math.Max(40, 230 * fv), MathF.Cos(c) * 270 * fh) + _pan;
                var l2 = new Vector3(-MathF.Sin(c) * 30, 0, -MathF.Cos(c) * 25) + _pan;
                float w0 = _viewW[0], sum = w0 + _viewW[1] + _viewW[2];
                pos = (pos * w0 + p1 * _viewW[1] + p2 * _viewW[2]) / sum;
                look = (look * w0 + l1 * _viewW[1] + l2 * _viewW[2]) / sum;
                fov = (40 * w0 + 58 * _viewW[1] + 42 * _viewW[2]) / sum;
            }
            subX = subY = 0;
        }
        SubPixelX = subX;
        SubPixelY = subY;
        if (MathF.Abs(Camera.Fov - fov) > 0.01f) Camera.Fov = fov;
        Camera.LookAtFromPosition(pos, look, Vector3.Up);
    }

    /// <summary>A directed shot from outside the rig (a replay, a cutscene): placed exactly, no pixel snap.</summary>
    public void Cut(Vector3 pos, Vector3 look, float fov)
    {
        SubPixelX = SubPixelY = 0;
        if (MathF.Abs(Camera.Fov - fov) > 0.01f) Camera.Fov = fov;
        Camera.LookAtFromPosition(pos, look, Vector3.Up);
    }

    static float Smooth(float x) => x * x * (3 - 2 * x);

    static float KeepIn(float aim, float p, float far, float near)
    {
        if (p < aim - far) return p + far;
        if (p > aim + near) return p - near;
        return aim;
    }

    static float SmoothStep(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
