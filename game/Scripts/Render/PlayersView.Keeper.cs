using System;
using Godot;
using GameNight.Sim;

namespace GameNight.Render;

/// <summary>
/// The ball in a player's hands (the keeper's catches, carries, dive-catches, throws and punts,
/// and throw-ins). The sim keeps a held ball at a fixed spot; here the hands decide where it is:
/// - the arms are solved (a small IK per arm) so both palms close on the ball wherever the pose
///   wants it: met at the height it was caught, gathered into the chest, cradled on the walk,
///   held out for a punt, hugged on landing from a dive-catch, held overhead for a throw-in;
/// - a one-arm throw carries it in the throwing hand;
/// - the ball is drawn in the hands, and eased back onto its real flight once it's let go.
/// </summary>
public sealed partial class PlayersView
{
    /// <summary>This frame: the ball where the holder's hands have it (id -1: nobody's).</summary>
    Vector3 _handBall;
    int _handBallId = -1;
    /// <summary>Drawn ball minus the sim's ball, eased so a catch or a release never pops.</summary>
    Vector3 _ballOff;
    readonly float[] _ikM = new float[16], _ikB = new float[4];

    static void ArmChain(in Transform3D C, float sideSign, float shoulder, float torsoL, float armLen,
        float swing, float outA, float elbow, float rot, out Transform3D j1, out Transform3D j2)
    {
        float raise = MathF.Acos(Clamp(MathF.Cos(swing) * MathF.Cos(outA), -1, 1));
        float elev = Smooth(1.1f, 2.9f, raise);
        float protract = 0.028f * MathF.Sin(Clamp(swing, -1.5f, 1.5f)) * (1 - 0.5f * elev);
        j1 = Chain(C, sideSign * (0.184f * shoulder - 0.014f * elev), 0.5f * torsoL + 0.045f * elev, protract, -swing, 0, sideSign * outA);
        j2 = Chain(j1, 0, -0.29f * armLen, 0, -elbow, sideSign * rot, 0);
    }

    static Vector3 Wrist(in Transform3D C, float sideSign, float shoulder, float torsoL, float armLen, float swing, float outA, float elbow, float rot)
    {
        ArmChain(C, sideSign, shoulder, torsoL, armLen, swing, outA, elbow, rot, out _, out var j2);
        return j2 * new Vector3(0, -0.245f * armLen, 0);
    }

    static Vector3 Palm(in Transform3D C, float sideSign, float shoulder, float torsoL, float armLen, float swing, float outA, float elbow, float rot, float wristX, float g)
    {
        ArmChain(C, sideSign, shoulder, torsoL, armLen, swing, outA, elbow, rot, out _, out var j2);
        var j3 = Chain(j2, 0, -0.245f * armLen, 0, wristX, 0, sideSign * -0.08f);
        return j3 * new Vector3(0, -0.06f * g, 0);
    }

    /// <summary>
    /// Arm IK: swing, abduction, elbow and forearm twist that put the wrist on `target`, by a few
    /// damped Gauss-Newton steps from the posed arm (so the arm keeps the pose's character). The
    /// twist is pulled toward a slight turn-in (palms toward the ball).
    /// </summary>
    void SolveArm(in Transform3D C, float sideSign, float shoulder, float torsoL, float armLen, Vector3 target,
        ref float sw, ref float oa, ref float el, ref float rot)
    {
        const float h = 0.01f, lam = 0.004f, lamRot = 0.04f, prefRot = -0.3f;
        var A = _ikM;
        var B = _ikB;
        Span<Vector3> J = stackalloc Vector3[4];
        for (int it = 0; it < 8; it++)
        {
            var w = Wrist(C, sideSign, shoulder, torsoL, armLen, sw, oa, el, rot);
            var e = target - w;
            if (e.LengthSquared() < 4e-6f) break;
            J[0] = (Wrist(C, sideSign, shoulder, torsoL, armLen, sw + h, oa, el, rot) - w) / h;
            J[1] = (Wrist(C, sideSign, shoulder, torsoL, armLen, sw, oa + h, el, rot) - w) / h;
            J[2] = (Wrist(C, sideSign, shoulder, torsoL, armLen, sw, oa, el + h, rot) - w) / h;
            J[3] = (Wrist(C, sideSign, shoulder, torsoL, armLen, sw, oa, el, rot + h) - w) / h;
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++) A[r * 4 + c] = J[r].Dot(J[c]) + (r == c ? (r == 3 ? lamRot : lam) : 0);
                B[r] = J[r].Dot(e) + (r == 3 ? lamRot * (prefRot - rot) : 0);
            }
            if (!Solve4(A, B)) break;
            sw += Clamp(B[0], -0.6f, 0.6f);
            oa = Clamp(oa + Clamp(B[1], -0.6f, 0.6f), -0.7f, 1.7f);
            el = Clamp(el + Clamp(B[2], -0.6f, 0.6f), 0.02f, 2.5f);
            rot = Clamp(rot + Clamp(B[3], -0.6f, 0.6f), -1.2f, 0.9f);
        }
    }

    /// <summary>Gaussian elimination on a 4x4 system (A row-major), solution left in b.</summary>
    static bool Solve4(float[] A, float[] b)
    {
        for (int c = 0; c < 4; c++)
        {
            int p = c;
            for (int r = c + 1; r < 4; r++) if (MathF.Abs(A[r * 4 + c]) > MathF.Abs(A[p * 4 + c])) p = r;
            if (MathF.Abs(A[p * 4 + c]) < 1e-9f) return false;
            if (p != c)
            {
                for (int k = 0; k < 4; k++) (A[c * 4 + k], A[p * 4 + k]) = (A[p * 4 + k], A[c * 4 + k]);
                (b[c], b[p]) = (b[p], b[c]);
            }
            for (int r = c + 1; r < 4; r++)
            {
                float f = A[r * 4 + c] / A[c * 4 + c];
                for (int k = c; k < 4; k++) A[r * 4 + k] -= f * A[c * 4 + k];
                b[r] -= f * b[c];
            }
        }
        for (int r = 3; r >= 0; r--)
        {
            float s = b[r];
            for (int k = r + 1; k < 4; k++) s -= A[r * 4 + k] * b[k];
            b[r] = s / A[r * 4 + r];
        }
        return true;
    }

    /// <summary>
    /// Hands on the ball for player `id` this frame (call with the trunk's frame C, before the
    /// arms are put): works out where the ball sits and bends the arms onto it; records it for
    /// drawing in _handBall. Returns false when the hands aren't on the ball.
    /// </summary>
    bool HandsOnBall(MatchSnapshot b, int id, float x, float z, float facing, in Transform3D C, ActionKind action, float pr,
        bool isHeld, bool throwInSp, float shoulder, float torsoL, float armLen, float g, float wxL, float wxR,
        ref float armL, ref float armR, ref float outL, ref float outR, ref float elbL, ref float elbR, ref float rotL, ref float rotR)
    {
        bool gk = b.Role[id] == Role.GK;
        if (!isHeld && !(gk && action == ActionKind.Catch)) return false;
        var side = C.Basis.Column0.Normalized();
        var up = C.Basis.Column1.Normalized();
        var fwd = C.Basis.Column2.Normalized();
        var pL = Palm(C, 1, shoulder, torsoL, armLen, armL, outL, elbL, rotL, wxL, g);
        var pR = Palm(C, -1, shoulder, torsoL, armLen, armR, outR, elbR, rotR, wxR, g);
        float sc = C.Basis.Column1.Length();

        if (action == ActionKind.Throw && !b.ThrowIn[id])
        {
            // One hand: the ball sits in the throwing palm.
            _handBall = pR + up * 0.05f * sc;
            _handBallId = id;
            return true;
        }

        Vector3 ball;
        float pin = 1;
        var cradle = C * new Vector3(0, 0.14f, 0.25f);
        if (action == ActionKind.Catch)
        {
            // Met where it was caught, then brought in to the chest.
            float cf = MathF.Cos(facing), sf = MathF.Sin(facing);
            float F = Clamp(b.CatchF[id], -0.3f, 1.1f), L = Clamp(b.CatchL[id], -1.2f, 1.2f);
            var contact = new Vector3(x + cf * F - sf * L, b.CatchY[id], z + sf * F + cf * L);
            ball = contact.Lerp(cradle, Smooth(0.2f, 0.8f, pr));
        }
        else if (action == ActionKind.None && !throwInSp) ball = cradle;
        else
        {
            // The pose holds it (a dive-catch on the floor, a punt held out, a throw-in overhead):
            // the hands close on it between them.
            ball = (pL + pR) * 0.5f;
            if (action == ActionKind.Kick)
            {
                float u = b.ActionT[id] / MathF.Max(0.05f, b.KickContact[id]);
                pin = 1 - Smooth(0.62f, 0.85f, u);
            }
        }
        if (pin <= 0.001f) return false;
        // Wrists either side of it, a touch behind (the palms reach on round it).
        var back = -fwd * 0.07f * sc;
        float half = 0.125f * sc;
        float s0 = armL, o0 = outL, e0 = elbL, r0 = rotL;
        SolveArm(C, 1, shoulder, torsoL, armLen, ball + side * half + back, ref s0, ref o0, ref e0, ref r0);
        float s1 = armR, o1 = outR, e1 = elbR, r1 = rotR;
        SolveArm(C, -1, shoulder, torsoL, armLen, ball - side * half + back, ref s1, ref o1, ref e1, ref r1);
        armL = Lerp(armL, s0, pin); outL = Lerp(outL, o0, pin); elbL = Lerp(elbL, e0, pin); rotL = Lerp(rotL, r0, pin);
        armR = Lerp(armR, s1, pin); outR = Lerp(outR, o1, pin); elbR = Lerp(elbR, e1, pin); rotR = Lerp(rotR, r1, pin);
        if (pin < 0.5f) return false;
        // Drawn where the hands really got to (out of reach, it stays between them).
        var wl = Wrist(C, 1, shoulder, torsoL, armLen, armL, outL, elbL, rotL);
        var wr = Wrist(C, -1, shoulder, torsoL, armLen, armR, outR, elbR, rotR);
        _handBall = (wl + wr) * 0.5f - back;
        _handBallId = id;
        return true;
    }

    /// <summary>The ball as drawn: the sim's ball, or in the holder's hands (eased on and off).</summary>
    Vector3 ShownBall(Vector3 sim, float dt)
    {
        var want = _handBallId >= 0 ? _handBall - sim : Vector3.Zero;
        float k = dt <= 0 ? 1 : 1 - MathF.Exp(-dt * (_handBallId >= 0 ? 40 : 16));
        _ballOff = _ballOff.Lerp(want, k);
        if (_handBallId < 0 && _ballOff.LengthSquared() < 1e-6f) _ballOff = Vector3.Zero;
        return sim + _ballOff;
    }
}
