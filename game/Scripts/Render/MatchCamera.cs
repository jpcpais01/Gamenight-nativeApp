using System;
using Godot;
using GameNight.Bridge;

namespace GameNight.Render;

/// <summary>
/// The broadcast camera, ported from the PWA's cameraRig.ts (the in-play framing):
/// - the subject is the ball, led into the direction of play and pulled partway toward the
///   player you control;
/// - a dead zone, so small touches don't move the picture;
/// - composition: the subject sits a little above centre (the controls cover the bottom);
/// - a safe frame keeping the active player and the ball in shot (the ball wins);
/// - a critically damped spring that stiffens with the ball's speed.
/// The camera moves in whole art pixels so the pixel grid never shimmers; the remainder is
/// handed to the display, which scrolls the upscaled picture by it.
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

    float _tx, _tz, _vx, _vz, _aimX, _aimZ, _leadX, _leadZ;
    float _aspect = 16f / 9f;
    float _fov = 30f;

    public MatchCamera(Camera3D camera)
    {
        Camera = camera;
        Camera.Fov = _fov;
        Camera.Near = 1f;
        Camera.Far = 500f;
        Place();
    }

    public void SetAspect(float aspect)
    {
        _aspect = aspect;
        // Narrow screens see less of the pitch side to side; pull back a little.
        _fov = aspect < 1.6f ? 36f : 30f;
        Camera.Fov = _fov;
    }

    static float Pitch => Mathf.DegToRad(PitchDeg);

    public void Update(MatchFrame a, MatchFrame b, float alpha, float dt)
    {
        if (dt <= 0) { Place(); return; }
        float bx = Mathf.Lerp(a.BallX, b.BallX, alpha);
        float bz = Mathf.Lerp(a.BallZ, b.BallZ, alpha);
        int ci = b.Controlled;
        float cx = ci >= 0 ? Mathf.Lerp(a.X[ci], b.X[ci], alpha) : bx;
        float cz = ci >= 0 ? Mathf.Lerp(a.Z[ci], b.Z[ci], alpha) : bz;
        float bvx = b.BallVX, bvz = b.BallVZ;
        float ballSpeed = MathF.Sqrt(bvx * bvx + b.BallVY * b.BallVY + bvz * bvz);

        float dist = BaseDist;
        float tanV = MathF.Tan(Mathf.DegToRad(_fov) / 2);
        float halfX = dist * tanV * _aspect;
        float halfZ = dist * tanV / MathF.Sin(Pitch + 0.15f);

        // Lead: where play is going, smoothed so it doesn't flick on every touch.
        bool live = b.Phase == MatchPhase.Play;
        int att = b.Attacking;
        float wantLX = live ? Math.Clamp(bvx * 0.4f, -9, 9) + (att >= 0 ? b.Dir[att] * 3.5f : 0) : 0;
        float wantLZ = live ? Math.Clamp(bvz * 0.25f, -4, 4) : 0;
        float kl = 1 - MathF.Exp(-dt * 1.8f);
        _leadX += (wantLX - _leadX) * kl;
        _leadZ += (wantLZ - _leadZ) * kl;

        // Subject: the led ball, pulled toward the active player (less when he's far from it).
        float cd = MathF.Sqrt((cx - bx) * (cx - bx) + (cz - bz) * (cz - bz));
        float wc = 0.4f * (1 - Math.Clamp((cd - 8) / 22, 0, 1));
        float sx = bx + _leadX + (cx - bx - _leadX) * wc;
        float sz = bz + _leadZ + (cz - bz - _leadZ) * wc;
        // Closing on a goal: lean the frame toward it.
        if (att >= 0 && live)
        {
            float gx = HL * b.Dir[att];
            float wg = 0.42f * (1 - SmoothStep(14, 44, MathF.Abs(gx - bx)));
            sx += (gx - sx) * wg;
            sz += (0 - sz) * wg * 0.6f;
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
        _aimZ = KeepIn(_aimZ, cz, halfZ * 0.72f, halfZ * 0.5f);
        _aimX = KeepIn(_aimX, bx, halfX * 0.82f, halfX * 0.82f);
        _aimZ = KeepIn(_aimZ, bz, halfZ * 0.78f, halfZ * 0.55f);

        // Don't show more than a little beyond the pitch.
        float mx = MathF.Max(0, HL + 8 - halfX);
        _aimX = Math.Clamp(_aimX, -mx, mx);
        _aimZ = Math.Clamp(_aimZ, -HW + halfZ * 0.55f - 6, HW - halfZ * 0.45f + 4);

        // Critically damped follow, stiffer when the ball is travelling.
        float w = b.Phase == MatchPhase.Goal ? 1.3f : 2.3f + MathF.Min(2.2f, ballSpeed * 0.1f);
        _vx += ((_aimX - _tx) * w * w - 2 * w * _vx) * dt;
        _vz += ((_aimZ - _tz) * w * w - 2 * w * _vz) * dt;
        _tx += _vx * dt;
        _tz += _vz * dt;
        // Never lose the ball, whatever the spring is doing.
        _tx = KeepIn(_tx, bx, halfX * 0.92f, halfX * 0.92f);
        _tz = KeepIn(_tz, bz, halfZ * 0.9f, halfZ * 0.75f);
        Place();
    }

    void Place()
    {
        float pitch = Pitch;
        float dist = BaseDist;
        // Snap the look point to whole art pixels; keep the remainder for the display.
        float unit = 2 * dist * MathF.Tan(Mathf.DegToRad(_fov) / 2) / PixelHeight;
        float unitZ = unit / MathF.Sin(pitch);
        float sxp = MathF.Round(_tx / unit) * unit;
        float szp = MathF.Round(_tz / unitZ) * unitZ;
        SubPixelX = (_tx - sxp) / unit;
        SubPixelY = (szp - _tz) / unitZ;
        var look = new Vector3(sxp, 0, szp);
        var pos = new Vector3(sxp, MathF.Sin(pitch) * dist, szp + MathF.Cos(pitch) * dist);
        Camera.LookAtFromPosition(pos, look, Vector3.Up);
    }

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
