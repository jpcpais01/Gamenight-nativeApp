using System;

namespace GameNight.Render;

/// <summary>
/// Getting up off the grass after a slide or a fall, the way players do it: off the back he sits
/// up onto a hand and tucks a leg under; off the front he pushes up on both hands and draws the
/// knees in. Either way he goes through one knee with the other foot planted, drives up off it and
/// settles into his stance. Poses are keyed in a side-neutral form: the lead leg (the one that
/// tackled, or the near one) ends up planted in front, the tucked leg kneels, and the support hand
/// is on the tucked side.
/// </summary>
public sealed partial class PlayersView
{
    struct Limbs
    {
        public float HipY, Lean, Flex, Roll, Head;
        public float LeadHip, LeadKnee, LeadOut, TuckHip, TuckKnee, TuckOut;
        public float SupArm, SupOut, SupElbow, FreeArm, FreeOut, FreeElbow;

        public static Limbs Mix(in Limbs a, in Limbs b, float k)
        {
            if (k <= 0) return a;
            if (k >= 1) return b;
            return new Limbs
            {
                HipY = Lerp(a.HipY, b.HipY, k), Lean = Lerp(a.Lean, b.Lean, k), Flex = Lerp(a.Flex, b.Flex, k),
                Roll = Lerp(a.Roll, b.Roll, k), Head = Lerp(a.Head, b.Head, k),
                LeadHip = Lerp(a.LeadHip, b.LeadHip, k), LeadKnee = Lerp(a.LeadKnee, b.LeadKnee, k), LeadOut = Lerp(a.LeadOut, b.LeadOut, k),
                TuckHip = Lerp(a.TuckHip, b.TuckHip, k), TuckKnee = Lerp(a.TuckKnee, b.TuckKnee, k), TuckOut = Lerp(a.TuckOut, b.TuckOut, k),
                SupArm = Lerp(a.SupArm, b.SupArm, k), SupOut = Lerp(a.SupOut, b.SupOut, k), SupElbow = Lerp(a.SupElbow, b.SupElbow, k),
                FreeArm = Lerp(a.FreeArm, b.FreeArm, k), FreeOut = Lerp(a.FreeOut, b.FreeOut, k), FreeElbow = Lerp(a.FreeElbow, b.FreeElbow, k),
            };
        }
    }

    // Off the back: sat up, leaning back onto the support hand, the tucked leg drawn in and
    // falling across under him, the lead leg bent with its heel on the grass.
    static readonly Limbs SitUp = new()
    {
        HipY = 0.2f, Lean = -0.42f, Flex = 0.35f, Roll = 0.12f, Head = 0.3f,
        LeadHip = 1.75f, LeadKnee = 1.0f, LeadOut = 0.12f, TuckHip = 2.0f, TuckKnee = 2.45f, TuckOut = 0.5f,
        SupArm = -0.5f, SupOut = 0.3f, SupElbow = 0.05f, FreeArm = 0.9f, FreeOut = 0.25f, FreeElbow = 0.7f,
    };

    // Off the front: up on both hands, knees drawn in under the hips.
    static readonly Limbs PushUp = new()
    {
        HipY = 0.4f, Lean = 0.62f, Flex = 0.45f, Roll = 0, Head = -0.35f,
        LeadHip = 1.55f, LeadKnee = 2.2f, LeadOut = 0.1f, TuckHip = 1.5f, TuckKnee = 2.2f, TuckOut = 0.12f,
        SupArm = 0.75f, SupOut = 0.15f, SupElbow = 0.05f, FreeArm = 0.75f, FreeOut = 0.15f, FreeElbow = 0.05f,
    };

    // On one knee: the lead foot planted in front, the hand on that knee, about to drive up.
    static readonly Limbs OneKnee = new()
    {
        HipY = 0.5f, Lean = 0.06f, Flex = 0.5f, Roll = 0, Head = 0.12f,
        LeadHip = 1.5f, LeadKnee = 1.55f, LeadOut = 0.06f, TuckHip = 0.05f, TuckKnee = 1.6f, TuckOut = 0.06f,
        SupArm = 0.65f, SupOut = 0.12f, SupElbow = 0.6f, FreeArm = 0.25f, FreeOut = 0.22f, FreeElbow = 0.8f,
    };

    // Driving up off the front foot, the back foot pushing, the arms swinging through.
    static readonly Limbs DriveUp = new()
    {
        HipY = 0.8f, Lean = 0.12f, Flex = 0.38f, Roll = 0, Head = 0.04f,
        LeadHip = 0.75f, LeadKnee = 1.05f, LeadOut = 0.05f, TuckHip = -0.05f, TuckKnee = 0.75f, TuckOut = 0.05f,
        SupArm = -0.35f, SupOut = 0.12f, SupElbow = 0.5f, FreeArm = 0.45f, FreeOut = 0.12f, FreeElbow = 0.6f,
    };

    /// <summary>
    /// The pose <paramref name="g"/> (0..1) of the way from lying (<paramref name="lie"/>) to the
    /// stance he's going back to (<paramref name="stand"/>). <paramref name="face"/> 0 = on his
    /// back or side, 1 = face down.
    /// </summary>
    static Limbs GetUp(float g, float face, in Limbs lie, in Limbs stand)
    {
        var first = Limbs.Mix(SitUp, PushUp, face);
        var p = Limbs.Mix(lie, first, Smooth(0, 0.3f, g));
        p = Limbs.Mix(p, OneKnee, Smooth(0.24f, 0.55f, g));
        p = Limbs.Mix(p, DriveUp, Smooth(0.5f, 0.78f, g));
        return Limbs.Mix(p, stand, Smooth(0.72f, 1, g));
    }
}
