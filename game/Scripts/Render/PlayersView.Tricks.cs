using System;
using Godot;
using GameNight.Sim;

namespace GameNight.Render;

/// <summary>
/// Skill moves on the body. The sim moves the man and the ball through the move (Skills.Script);
/// this puts his feet on the ball where the move touches it (IK to the ball itself, so sole,
/// inside and outside meet it wherever it really is), swings the legs through the step-overs,
/// wind-ups and flicks, and drops the shoulder, dips the hips and throws the arms out for balance.
/// "A" is the foot on the dummy side (the one that sells it), "B" the one on the exit side.
/// </summary>
public sealed partial class PlayersView
{
    /// <summary>Per leg (id * 2 + side): pull toward a target (0..1) and the ankle's target, world.</summary>
    readonly float[] _trkW = new float[N * 2];
    readonly Vector3[] _trkT = new Vector3[N * 2];
    readonly float[] _tH = new float[2], _tK = new float[2], _tO = new float[2], _tY = new float[2];
    readonly float[] _tAS = new float[2], _tAO = new float[2], _tEl = new float[2];

    static float Bump(float a, float b, float t) => t <= a || t >= b ? 0 : MathF.Sin((t - a) / (b - a) * PI);
    static float Hold(float a, float b, float c, float d, float t) => Smooth(a, b, t) * (1 - Smooth(c, d, t));

    void TrickPose(MatchSnapshot a, MatchSnapshot b, float alpha, int id, float facing, float t, float dur,
        ref float hipL, ref float hipR, ref float kneeL, ref float kneeR, ref float legOutL, ref float legOutR,
        ref float legYawL, ref float legYawR, ref float armL, ref float armR, ref float armOutL, ref float armOutR,
        ref float elbowL, ref float elbowR, ref float hipY, ref float leanF, ref float leanS, ref float flexExtra,
        ref float pelvisYaw, ref float twist, ref float lift, ref float headPitch)
    {
        var move = b.Trick[id];
        int e = b.TrickSide[id] >= 0 ? 1 : -1;
        // Legs and arms by side: 0 = left, 1 = right. A is the dummy-side foot.
        int A = e > 0 ? 1 : 0, B = 1 - A;
        float sA = A == 0 ? 1 : -1;
        _tH[0] = hipL; _tH[1] = hipR; _tK[0] = kneeL; _tK[1] = kneeR; _tO[0] = legOutL; _tO[1] = legOutR;
        _tY[0] = legYawL; _tY[1] = legYawR; _tAS[0] = armL; _tAS[1] = armR; _tAO[0] = armOutL; _tAO[1] = armOutR;
        _tEl[0] = elbowL; _tEl[1] = elbowR;
        int fA = id * 2 + A, fB = id * 2 + B;
        _trkW[fA] = _trkW[fB] = 0;

        // The ball where it's drawn, the move's frame, and his body's frame now.
        float bx = Lerp(a.BallX, b.BallX, alpha), by = Lerp(a.BallY, b.BallY, alpha), bz = Lerp(a.BallZ, b.BallZ, alpha);
        var ball = new Vector3(bx, by, bz);
        float fx = b.ActionDirX[id], fz = b.ActionDirZ[id];
        var exitL = new Vector3(fz, 0, -fx) * e;
        var fwd = new Vector3(MathF.Cos(facing), 0, MathF.Sin(facing));
        var up = Vector3.Up;
        float env = Smooth(0, 0.06f, t) * (1 - Smooth(dur - 0.12f, dur, t));
        float r = (float)BallK.Radius;

        // A foot on the ball: the sole on top, or the inside / outside against its side.
        Vector3 Sole() => ball - fwd * 0.04f + up * (r + 0.1f);
        Vector3 Side(Vector3 from) => new Vector3(bx, 0.095f, bz) + from * 0.16f;
        void Foot(int f, Vector3 at, float w)
        {
            if (w <= _trkW[f]) return;
            _trkW[f] = Math.Clamp(w, 0, 1);
            _trkT[f] = at;
        }
        void Leg(int sd, float hip, float knee, float w)
        {
            _tH[sd] = Lerp(_tH[sd], hip, w);
            _tK[sd] = Lerp(_tK[sd], knee, w);
        }
        void Arms(float out0, float swing, float w)
        {
            for (int sd = 0; sd < 2; sd++)
            {
                _tAO[sd] = Lerp(_tAO[sd], out0, w);
                _tAS[sd] = Lerp(_tAS[sd], swing, w);
                _tEl[sd] = Lerp(_tEl[sd], 0.55f, w);
            }
        }

        switch (move)
        {
            case SkillMove.BodyFeint:
            {
                // The dummy-side leg steps out wide and the shoulder drops over it; the outside of
                // the other foot takes it away.
                float sell = Bump(0, 0.34f, t);
                _tO[A] += 0.28f * sell;
                _tK[A] += 0.35f * sell;
                _tK[B] += 0.25f * sell;
                hipY -= 0.07f * sell;
                leanS += e * 0.2f * sell;
                twist += -sA * 0.25f * sell;
                _tAO[A] += 0.45f * sell;
                _tAO[B] += 0.2f * sell;
                Foot(fB, Side(-exitL) - fwd * 0.03f, Bump(0.26f, 0.46f, t));
                headPitch += 0.15f * sell;
                break;
            }
            case SkillMove.StepOver:
            {
                // Inside to outside over the front of the ball, high enough to clear it, then
                // planted wide; the body dips with it. The outside of the other foot goes.
                float u = Math.Clamp((t - 0.06f) / 0.32f, 0, 1);
                float arc = MathF.Sin(u * PI);
                var over = new Vector3(bx, 0.11f + 0.16f * arc, bz) + exitL * (0.24f * MathF.Cos(u * PI)) + fwd * (0.06f + 0.16f * arc);
                Foot(fA, over, Hold(0.02f, 0.08f, 0.4f, 0.48f, t));
                float dip = Bump(0.04f, 0.46f, t);
                hipY -= 0.09f * dip;
                _tK[B] += 0.4f * dip;
                leanS += e * 0.18f * dip;
                twist += -sA * 0.2f * dip;
                Arms(0.45f, 0.1f, 0.6f * dip);
                _tAO[A] += 0.25f * dip;
                Foot(fB, Side(-exitL) - fwd * 0.02f, Bump(0.34f, 0.5f, t));
                headPitch += 0.25f * dip;
                break;
            }
            case SkillMove.DragBack:
            {
                // Foot up on the ball and rolled back under him, leaning back on the standing leg.
                float on = Hold(0.02f, 0.08f, 0.46f, 0.56f, t);
                Foot(fA, Sole(), on);
                _tK[B] += 0.4f * on;
                hipY -= 0.06f * on;
                leanF -= 0.1f * on;
                Arms(0.35f, 0.15f, 0.7f * on);
                headPitch += 0.3f * on;
                break;
            }
            case SkillMove.CruyffTurn:
            {
                // Shapes for the pass (the leg drawn back, the other arm out), then the inside of
                // the foot goes round in front of it and drags it back behind the standing leg.
                float wind = Bump(0, 0.3f, t);
                Leg(A, -0.45f, 1.15f, wind);
                pelvisYaw += -sA * 0.28f * wind;
                twist += sA * 0.25f * wind;
                _tAO[B] = Lerp(_tAO[B], 0.85f, wind);
                Foot(fA, new Vector3(bx, 0.1f, bz) + fwd * 0.15f + exitL * 0.04f, Hold(0.2f, 0.26f, 0.42f, 0.5f, t));
                float spin = Bump(0.24f, 0.62f, t);
                _tK[B] += 0.45f * spin;
                hipY -= 0.08f * spin;
                Arms(0.5f, 0.1f, 0.5f * spin);
                headPitch += 0.25f * env;
                break;
            }
            case SkillMove.FakeShot:
            {
                // The full back-lift of a strike... and the leg comes down over the ball instead;
                // the inside of the foot pulls it across.
                float wind = Smooth(0, 0.22f, t) * (1 - Smooth(0.22f, 0.32f, t));
                Leg(A, -0.8f, 1.65f, wind);
                pelvisYaw += -sA * 0.34f * wind;
                twist += sA * 0.34f * wind;
                flexExtra -= 0.08f * wind;
                _tAO[B] = Lerp(_tAO[B], 1.15f, wind);
                _tAS[B] = Lerp(_tAS[B], 0.3f, wind);
                _tK[B] += 0.3f * wind;
                float drag = Hold(0.24f, 0.3f, 0.44f, 0.52f, t);
                Foot(fA, Side(-exitL) + fwd * 0.04f, drag);
                hipY -= 0.07f * drag;
                flexExtra += 0.12f * drag;
                headPitch += 0.3f * env;
                break;
            }
            case SkillMove.Roulette:
            {
                // Sole of one foot, then the other, rolling it round as he spins; arms out wide.
                Foot(fA, Sole(), Hold(0.02f, 0.08f, 0.34f, 0.42f, t));
                Foot(fB, Sole(), Hold(0.34f, 0.42f, 0.66f, 0.74f, t));
                hipY -= 0.07f * env;
                _tK[0] += 0.25f * env;
                _tK[1] += 0.25f * env;
                Arms(0.6f, 0.05f, 0.75f * env);
                leanF -= 0.05f * env;
                headPitch += 0.3f * env;
                break;
            }
            case SkillMove.Croqueta:
            {
                // Inside of one foot across to the inside of the other, low and quick.
                Foot(fA, Side(-exitL), Bump(0, 0.17f, t));
                Foot(fB, Side(exitL), Bump(0.1f, 0.28f, t));
                float low = Bump(0, 0.36f, t);
                hipY -= 0.08f * low;
                _tK[0] += 0.3f * low;
                _tK[1] += 0.3f * low;
                leanS += -e * 0.15f * low;
                Arms(0.4f, 0.05f, 0.5f * low);
                headPitch += 0.25f * low;
                break;
            }
            case SkillMove.HeelToHeel:
            {
                // The front heel rolls it back, the trailing heel flicks it on; then the burst.
                Foot(fA, new Vector3(bx, 0.17f, bz) + fwd * 0.12f, Bump(0, 0.17f, t));
                float flick = Bump(0.11f, 0.3f, t);
                Leg(B, -0.35f, 1.5f, flick);
                Foot(fB, new Vector3(bx, 0.15f, bz) - fwd * 0.12f, 0.6f * flick);
                _tK[A] += 0.3f * flick;
                leanF += 0.14f * Smooth(0.22f, 0.36f, t) * env;
                Arms(0.35f, 0.1f, 0.4f * Bump(0, 0.3f, t));
                headPitch += 0.25f * env;
                break;
            }
            case SkillMove.Elastico:
            {
                // The same foot: the outside pushes it out the dummy way, then whips round it and
                // the inside snaps it back; the body sells it, then goes.
                float swap = Smooth(0.17f, 0.3f, t);
                var at = new Vector3(bx, 0.095f, bz) + exitL * (0.16f * MathF.Cos(swap * PI)) + fwd * (0.03f + 0.06f * MathF.Sin(swap * PI));
                Foot(fA, at, Hold(0, 0.05f, 0.31f, 0.38f, t));
                _tY[A] += -sA * 0.35f * swap * env;
                float sell = Bump(0, 0.26f, t), go = Bump(0.22f, 0.5f, t);
                leanS += e * 0.24f * sell - e * 0.26f * go;
                hipY -= 0.09f * (sell + go * 0.6f);
                _tK[B] += 0.45f * env;
                twist += -sA * 0.22f * sell + sA * 0.2f * go;
                Arms(0.55f, 0.1f, 0.65f * env);
                headPitch += 0.25f * env;
                break;
            }
            case SkillMove.Rainbow:
            {
                // One foot rolls it up the back of the other leg, the heel of that leg flicks it
                // up over his head; a little hop, and his eyes go up with it.
                Foot(fB, ball + up * (r + 0.08f) + fwd * 0.03f, Bump(0, 0.22f, t));
                float flick = Bump(0.16f, 0.38f, t);
                Leg(A, -0.55f, 2.0f, flick);
                _tK[B] += 0.35f * Bump(0.1f, 0.34f, t);
                leanF += 0.16f * flick;
                lift += 0.05f * Bump(0.22f, 0.4f, t);
                Arms(0.5f, 0.2f, 0.6f * env);
                headPitch += 0.3f * (1 - Smooth(0.25f, 0.35f, t)) * env - 0.35f * Smooth(0.3f, 0.45f, t) * env;
                break;
            }
            case SkillMove.RonaldoChop:
            {
                // The far foot swings across behind the standing leg and the inside of it chops
                // the ball square; he sits down on the standing leg and turns with it.
                float reach = Bump(0.06f, 0.34f, t);
                Leg(A, -0.25f, 1.35f, reach);
                _tY[A] += sA * 0.4f * reach;
                Foot(fA, Side(-exitL) - fwd * 0.06f, Bump(0.12f, 0.32f, t));
                _tK[B] += 0.45f * reach;
                hipY -= 0.1f * reach;
                leanF -= 0.08f * reach;
                leanS += e * 0.22f * Bump(0.2f, 0.5f, t);
                Arms(0.55f, 0.1f, 0.6f * env);
                headPitch += 0.25f * env;
                break;
            }
            case SkillMove.McGeadySpin:
            {
                // The exit-side sole drags it across while he spins the other way, arms wrapped
                // in tight, then flung out as he comes round.
                Foot(fB, Sole(), Hold(0.03f, 0.08f, 0.5f, 0.6f, t));
                float spin = Bump(0.06f, 0.7f, t);
                _tK[A] += 0.35f * spin;
                hipY -= 0.08f * spin;
                Arms(0.15f + 0.5f * Smooth(0.45f, 0.65f, t), 0.05f, 0.7f * env);
                headPitch += 0.25f * env;
                break;
            }
            case SkillMove.HocusPocus:
            {
                // A lean the dummy way, then the dummy-side foot hooks behind the standing leg
                // and flicks it across with the outside.
                float sell = Bump(0, 0.2f, t);
                leanS += e * 0.2f * sell;
                twist += -sA * 0.2f * sell;
                float hook = Bump(0.06f, 0.26f, t);
                Leg(A, -0.35f, 1.25f, hook);
                _tY[A] += sA * 0.35f * hook;
                Foot(fA, new Vector3(bx, 0.1f, bz) - fwd * 0.1f - exitL * 0.04f, Bump(0.08f, 0.24f, t));
                _tK[B] += 0.35f * hook;
                hipY -= 0.07f * hook;
                leanS -= e * 0.2f * Bump(0.2f, 0.45f, t);
                Arms(0.5f, 0.1f, 0.55f * env);
                headPitch += 0.25f * env;
                break;
            }
            case SkillMove.StepOverStorm:
            {
                // Three step overs, foot after foot, inside to out, the hips dropping with each;
                // then the outside of the foot takes it away.
                for (int k = 0; k < 3; k++)
                {
                    float t0 = 0.04f + k * 0.26f;
                    int f = k % 2 == 0 ? fA : fB;
                    float sd = k % 2 == 0 ? 1 : -1;
                    float u = Math.Clamp((t - t0) / 0.24f, 0, 1);
                    float arc = MathF.Sin(u * PI);
                    var over = new Vector3(bx, 0.11f + 0.15f * arc, bz) + exitL * (sd * 0.22f * MathF.Cos(u * PI)) + fwd * (0.06f + 0.14f * arc);
                    Foot(f, over, Hold(t0 - 0.02f, t0 + 0.03f, t0 + 0.22f, t0 + 0.27f, t));
                }
                float dip = Bump(0.02f, 0.84f, t);
                hipY -= 0.09f * dip;
                _tK[0] += 0.3f * dip;
                _tK[1] += 0.3f * dip;
                Arms(0.5f, 0.1f, 0.6f * dip);
                Foot(fB, Side(-exitL) - fwd * 0.02f, Bump(0.78f, 0.92f, t));
                headPitch += 0.25f * env;
                break;
            }
            case SkillMove.Sombrero:
            {
                // He checks, the toe slides under the ball and the leg lifts it up and over.
                Foot(fA, ball - up * 0.02f + fwd * -0.1f, Bump(0.08f, 0.28f, t));
                float scoop = Bump(0.18f, 0.42f, t);
                Leg(A, 0.75f, 0.35f, scoop);
                _tK[B] += 0.3f * Bump(0.04f, 0.3f, t);
                leanF -= 0.12f * scoop;
                Arms(0.55f, 0.15f, 0.6f * env);
                headPitch += 0.3f * (1 - Smooth(0.24f, 0.34f, t)) * env - 0.3f * Smooth(0.3f, 0.42f, t) * env;
                break;
            }
            case SkillMove.Panna:
            {
                // The inside of the foot slips it through the man's legs; then the run round him.
                Foot(fA, Side(-exitL) - fwd * 0.04f, Bump(0.04f, 0.2f, t));
                float push = Bump(0.04f, 0.24f, t);
                _tK[B] += 0.3f * push;
                hipY -= 0.05f * push;
                leanS += -e * 0.18f * Bump(0.15f, 0.72f, t);
                Arms(0.4f, 0.15f, 0.5f * env);
                headPitch += 0.2f * env;
                break;
            }
        }

        hipL = _tH[0]; hipR = _tH[1]; kneeL = _tK[0]; kneeR = _tK[1]; legOutL = _tO[0]; legOutR = _tO[1];
        legYawL = _tY[0]; legYawR = _tY[1]; armL = _tAS[0]; armR = _tAS[1]; armOutL = _tAO[0]; armOutR = _tAO[1];
        elbowL = _tEl[0]; elbowR = _tEl[1];
    }
}
