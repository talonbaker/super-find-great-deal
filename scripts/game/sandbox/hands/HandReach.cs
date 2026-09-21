using Godot;
using MpFoundation.Game.Sandbox.Feel;

namespace MpFoundation.Game.Sandbox.Hands;

/// <summary>
/// <b>WHERE A HAND GOES, AS MATHS</b> (HANDS-1, 2026-09-20). Given where the player's eye is,
/// where the prop is and where FEEL-1 recorded the grab, this says where to draw the hand or
/// hands, how many of them, and how far along the reach is. <see cref="FirstPersonHands"/> is
/// the node that reads live transforms and hands them here; nothing in this file touches the
/// scene tree, so the Godot-free suite pins every derivation.
///
/// <para><b>Why hands at all, and why THESE hands</b> (program RIDE-1 §2). Lethal Company shows
/// arms in a fixed pose per item — the hands say <i>what</i> you carry, never <i>where</i> you
/// hold it. R.E.P.O. shows none at all. Both are wrong for this game, because after FEEL-1 the
/// object is held at the point you grabbed it and the whole verb is putting a specific thing in
/// a specific spot by hand. So the hand's only job is to show the grab point.</para>
///
/// <para><b>No IK, no finger rig, no animation clip.</b> Position and roll come from the grab
/// point and the surface it is on; grip is one mesh swap. Everything here is a straight lerp.
/// Simple over juice.</para>
/// </summary>
public static class HandReach
{
    // ---------------------------------------------------------------------------- the timing

    /// <summary>How long the near hand takes to travel from its idle rest to whatever the click
    /// resolved to, seconds. Long enough to read as a reach rather than a teleport, short enough
    /// that the hand is on the object by the time the server's grab confirmation lands.</summary>
    public const float ReachSec = 0.120f;

    /// <summary>...and how long it takes to come home when the hand lets go, seconds. Slower
    /// than the reach on purpose: going for a thing is a decision, letting go of it is a
    /// consequence, and the slower return is what makes a break-hold read as <i>losing</i> the
    /// object rather than putting it down.</summary>
    public const float ReturnSec = 0.150f;

    /// <summary>How long the finger rests on a button before coming back, seconds. A poke that
    /// reversed on the same frame it arrived reads as a twitch, not a press.</summary>
    public const float PokeDwellSec = 0.060f;

    // ------------------------------------------------------------------------ the arm length

    /// <summary>
    /// <b>The arm, which is FEEL-1's hold ceiling and not a second number</b> (packet item 4).
    /// <see cref="CarryHold.HoldMaxBaseM"/> is how far the wheel may push a held object; the hand
    /// has to be able to reach whatever the player is holding, so the two are one quantity. This
    /// is also the honest reason the clamp exists: past the arm's length a carry stops being a
    /// carry and becomes telekinesis.
    /// </summary>
    public static float ArmLengthM => CarryHold.HoldMaxBaseM;

    /// <summary>
    /// <b>How far a hand ON a held object may be from the eye</b>, metres: the arm, plus that
    /// object's own bounding radius.
    ///
    /// <para><b>Why the hold gets an allowance and a poke does not.</b> FEEL-1 already clamps the
    /// hold DISTANCE to <see cref="ArmLengthM"/>, and it measures that distance to the GRAB
    /// POINT. The hand then sits on the object's surface, which is up to one bounding radius away
    /// from that point in whatever direction the object happens to lie — a crate held at arm's
    /// length has its side faces 0.22 m out to either side, and your shoulders are not at your
    /// eye. A flat radial clamp at the arm would drag both hands of a two-handed grip inward
    /// along the view ray every frame, which is a pose distortion rather than a guarantee. This
    /// bound is still tight: it cannot be exceeded while FEEL-1's own clamp holds, so it is a
    /// guard against a bug rather than a shape anyone can reach.</para></summary>
    public static float HoldReachM(float propRadiusM) => ArmLengthM + Mathf.Max(0f, propRadiusM);

    /// <summary>A hand target brought inside a reach, keeping its direction — the hand is never
    /// drawn further from the eye than the arm goes. Used at <see cref="ArmLengthM"/> for a poke
    /// (a reach at the world, which genuinely has to be bounded) and at
    /// <see cref="HoldReachM"/> for a hand on something already held.</summary>
    public static Vector3 ClampToArm(Vector3 eye, Vector3 point, float armLengthM)
    {
        Vector3 away = point - eye;
        float dist = away.Length();
        return dist <= armLengthM || dist < 1e-6f ? point : eye + away * (armLengthM / dist);
    }

    // ------------------------------------------------------------------- one hand or two

    /// <summary>
    /// <b>How wide a thing may be and still be held in one hand</b>, metres — about the span of
    /// an adult hand from thumb tip to little finger.
    ///
    /// <para>It is a SPAN and not a volume because the failure it describes is leverage: a thing
    /// longer than your hand is long cannot be controlled from a single grip, it levers out. The
    /// shipped props fall either side of it with room to spare — produce is 0.16 m and the cereal
    /// box 0.28 m — which is what makes 0.20 a threshold rather than a number that happens to
    /// sort today's list.</para></summary>
    public const float OneHandSpanM = 0.20f;

    /// <summary>
    /// ...and how heavy, kilograms.
    ///
    /// <para><b>Nothing shipped today trips this arm</b>, and that is stated rather than hidden:
    /// every one-handed prop in the build is under 0.4 kg and every two-handed one is already
    /// over the span. It exists because size and weight come apart the moment Talon's real meshes
    /// arrive — a tin of paint is can-sized and nobody one-hands it — and because a rule with one
    /// clause would have to be rewritten rather than extended when they do.</para></summary>
    public const float OneHandMassKg = 2.0f;

    /// <summary>Does this prop need the second hand? Either arm is enough on its own.</summary>
    public static bool NeedsTwoHands(float longestAxisM, float massKg) =>
        longestAxisM > OneHandSpanM || massKg > OneHandMassKg;

    // --------------------------------------------------------------- the hand sits ON the thing

    /// <summary>How far proud of the face it holds a hand sits, metres — half the placeholder
    /// mitten's own thickness, so the slab rests on the surface instead of half inside it.</summary>
    public const float PalmProudM = 0.015f;

    /// <summary>
    /// <b>The grab point, pushed out to the surface on the side facing the player.</b>
    ///
    /// <para><b>Why it has to be pushed at all, with the arithmetic.</b> FEEL-1 records the grab
    /// point as the foot of the perpendicular from the prop's centre onto the view ray
    /// (<c>NetworkedProp.BindToHolderRayHold</c>), clamped onto the prop's bounding sphere only
    /// when the ray misses it entirely. For any well-aimed grab the ray does not miss, so the
    /// recorded point is INSIDE the prop — at the centre of a 0.44 m crate, in the ordinary case.
    /// A hand drawn there is a hand nobody can see. This takes the same point and follows its own
    /// line of sight out to where it leaves the prop, which keeps every property that matters (it
    /// is the grab point across the view, it rides the prop's rotation) and adds the one the
    /// player needs: you can see it.</para>
    ///
    /// <para><paramref name="towardEye"/> points from the prop toward the player. A grab point
    /// further across the view than the prop's own radius is pulled in first, so the depth term
    /// is never the square root of a negative number.</para>
    /// </summary>
    public static Vector3 SurfacePoint(Vector3 centre, Vector3 grabPoint, float radiusM, Vector3 towardEye)
    {
        Vector3 n = Normalized(towardEye, Vector3.Back);
        float r = Mathf.Max(radiusM, 1e-4f);
        Vector3 d = grabPoint - centre;
        Vector3 lateral = d - n * d.Dot(n);
        float lat = lateral.Length();
        // 0.999 rather than 1.0: a hand exactly on the silhouette edge is a hand seen edge-on,
        // and the depth term there is zero, which reads as the hand lying in the prop's own plane.
        float maxLat = r * 0.999f;
        if (lat > maxLat)
        {
            lateral = lat > 1e-6f ? lateral * (maxLat / lat) : Vector3.Zero;
            lat = maxLat;
        }
        float depth = Mathf.Sqrt(Mathf.Max(0f, r * r - lat * lat));
        return centre + lateral + n * depth;
    }

    /// <summary>
    /// <b>Two hands, one on each opposite face, straddling the grab point.</b>
    ///
    /// <para>The axis is taken across the VIEW rather than along one of the prop's own axes: you
    /// put your hands on whichever two faces are to your left and right, and if you walk round
    /// the crate they are different faces. An axis fixed to the prop would eventually put a hand
    /// behind it.</para>
    ///
    /// <para>The pair's midpoint is the grab point across the view, so the crate is still held
    /// where it was grabbed — and the hands keep the grab's HEIGHT, so grabbing a crate low holds
    /// it low.</para>
    /// </summary>
    public static (Vector3 A, Vector3 B) StraddlePoints(
        Vector3 centre, Vector3 grabPoint, Vector3 towardEye, Vector3 worldUp, float halfSpanM, float proudM)
    {
        Vector3 across = AcrossView(towardEye, worldUp);
        Vector3 d = grabPoint - centre;
        // Everything of the grab point except its sideways component: its height, and its depth
        // along the line of sight. The hands replace only the sideways part.
        Vector3 keep = d - across * d.Dot(across);
        Vector3 offset = across * (halfSpanM + proudM);
        return (centre + keep + offset, centre + keep - offset);
    }

    /// <summary>The axis the two hands straddle along: horizontal, across the player's line of
    /// sight. One definition, used by <see cref="StraddlePoints"/> and by the caller that has to
    /// ask the prop how wide it is along that same axis — two copies of this cross product is how
    /// a hand ends up half inside a crate.</summary>
    public static Vector3 AcrossView(Vector3 towardEye, Vector3 worldUp)
    {
        Vector3 n = Normalized(towardEye, Vector3.Back);
        Vector3 across = worldUp.Cross(n);
        if (across.LengthSquared() < 1e-8f)
            across = Vector3.Right.Cross(n);           // looking straight up or down a face
        return Normalized(across, Vector3.Right);
    }

    /// <summary>
    /// <b>How far a box reaches along an arbitrary world axis</b>, metres — the support function
    /// of an oriented box, which is the sum over its own three axes of how much each contributes.
    ///
    /// <para>Used for the straddle's half-span, so the hands land on the crate's actual side
    /// faces rather than on its bounding sphere: for a 0.44 m cube those differ by 0.16 m, which
    /// is a hand floating in mid-air. It also follows a turned prop — a crate spun 45° really is
    /// wider across the view, and the hands have to go wider with it.</para></summary>
    public static float SupportHalfExtent(Basis basis, Vector3 halfSizeLocal, Vector3 axis)
    {
        Vector3 a = Normalized(axis, Vector3.Right);
        return Mathf.Abs(basis.X.Dot(a)) * halfSizeLocal.X
             + Mathf.Abs(basis.Y.Dot(a)) * halfSizeLocal.Y
             + Mathf.Abs(basis.Z.Dot(a)) * halfSizeLocal.Z;
    }

    // ------------------------------------------------------------------------- the hand's angle

    /// <summary>
    /// The hand's world orientation: the palm faces <paramref name="palmNormal"/> and the fingers
    /// point along <paramref name="fingerDir"/>. The placeholder mesh is a slab whose thickness
    /// is its local Y and whose fingers run down its local −Z, so those are the two axes a real
    /// hand mesh has to be authored to when it replaces the primitive.
    ///
    /// <para>The finger direction is orthogonalised against the palm rather than trusted, and a
    /// direction parallel to the palm falls back to any perpendicular — the straddle asks for
    /// exactly that when the player looks straight down a face, and a NaN basis is an invisible
    /// hand.</para>
    /// </summary>
    public static Basis Orient(Vector3 palmNormal, Vector3 fingerDir)
    {
        Vector3 y = Normalized(palmNormal, Vector3.Up);
        Vector3 z = -fingerDir;
        z -= y * z.Dot(y);
        if (z.LengthSquared() < 1e-8f)
        {
            z = Vector3.Forward - y * Vector3.Forward.Dot(y);
            if (z.LengthSquared() < 1e-8f)
                z = Vector3.Right - y * Vector3.Right.Dot(y);
        }
        z = Normalized(z, Vector3.Back);
        Vector3 x = y.Cross(z);
        return new Basis(x, y, z);
    }

    // ------------------------------------------------------------------------------ the timing

    /// <summary>How far through a reach of <paramref name="durationSec"/> we are, 0..1. A zero
    /// duration is instant rather than a divide by zero.</summary>
    public static float Progress(double elapsedSec, float durationSec) =>
        durationSec <= 0f ? 1f : Mathf.Clamp((float)elapsedSec / durationSec, 0f, 1f);

    /// <summary>A straight lerp. Deliberately not eased: program §2.2 says "a straight lerp, no
    /// animation clips", and every easing curve is a shape somebody then has to defend.</summary>
    public static Vector3 Lerp(Vector3 from, Vector3 to, float t) => from + (to - from) * t;

    // --------------------------------------------------------------- the suite's own instrument

    /// <summary>
    /// <b>How far the hand is from the grab point ACROSS the line of sight</b>, metres — the
    /// quantity <c>Run-HandsSmoke</c> asserts at 1 cm every frame.
    ///
    /// <para>Across rather than through, and that is the whole of the measurement's honesty: the
    /// hand is deliberately pushed out along the line of sight to the prop's surface (see
    /// <see cref="SurfacePoint"/>), so a raw 3-D distance would report the prop's own radius and
    /// measure nothing. Sideways is where an attach bug shows up, and sideways is what a planted
    /// offset moves.</para></summary>
    public static float LateralErrorM(Vector3 hand, Vector3 grabPoint, Vector3 towardEye)
    {
        Vector3 n = Normalized(towardEye, Vector3.Back);
        Vector3 d = hand - grabPoint;
        return (d - n * d.Dot(n)).Length();
    }

    private static Vector3 Normalized(Vector3 v, Vector3 fallback) =>
        v.LengthSquared() > 1e-10f ? v.Normalized() : fallback;
}
