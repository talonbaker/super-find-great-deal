using Godot;

namespace MpFoundation.Game.Sandbox;

/// <summary>
/// <b>Two-bone analytic inverse kinematics, in a limb's own sagittal plane.</b> Given an upper
/// length, a lower length and a target offset from the limb's root, it returns the root angle and
/// the joint angle that put the limb's tip on that target — a knee, or an elbow.
///
/// <para><b>Pure arithmetic: no <c>Node</c>, no scene tree, no <c>Input</c>.</b> Exactly like
/// <see cref="LocomotionProfile"/>, and for the same reason — every claim RIG-1's report makes about
/// the knee is an assertion in <c>tests/unit/LimbIkTests.cs</c> rather than a human squinting at a
/// capture. Godot <i>value</i> types (<see cref="Mathf"/>, <see cref="Vector2"/>) are ordinary
/// managed code and run fine in the engine-free xUnit host; a Godot <i>node</i> would not, which is
/// what CONV-4's <c>3352e99f</c> fixed.</para>
///
/// <para><b>The plane, and the sign convention.</b> <c>X</c> is FORWARD (the rig's <c>-Z</c>) and
/// <c>Y</c> is UP. A limb at rest hangs straight down, so its rest target is
/// <c>(0, -(upper + lower))</c>. A positive angle rotates a segment FORWARD — the same convention
/// <see cref="LocomotionProfile.LegAngleFor"/> already uses ("positive rotation about local X swings
/// the foot forward"), so a solved root angle drops straight into the rig's <c>Rotation.X</c> with no
/// sign juggling at the call site. That matters: a sign flip here would be invisible in a still and
/// obvious only in motion.</para>
///
/// <para><b>Why analytic rather than iterative.</b> Two bones is a triangle, and a triangle has a
/// closed form (the law of cosines). An iterative solver would cost more, converge differently on
/// different frames, and — worst of all — be a source of frame-to-frame jitter in a pose channel
/// where jitter reads as a broken rig. Nothing here loops.</para>
///
/// <para><b>THE LOAD-BEARING PROPERTY, and the reason this is cheap.</b> Today's gait keeps each leg
/// rigid and only rotates it about the hip, so every foot position it asks for is at exactly
/// <c>|leg|</c> from the hip. Feed those same positions through a solver whose segments sum to
/// <c>|leg|</c> and <see cref="Solve"/> returns a <b>straight</b> joint, reproducing the pose
/// exactly. The knee therefore appears ONLY where something genuinely shortens the limb — the jump
/// tuck, the landing absorb, the launch snap — so MOVE-1's ratified walk, run, skid and
/// stride x cadence identity cannot drift. That is a test
/// (<c>LimbIkTests.RigidGaitFootPositions_AreReproducedExactly</c>), not a hope.</para>
///
/// <para><b>NO OUTPUT IS EVER NaN, and that is not defensive padding.</b> A NaN reaching a
/// <see cref="Transform3D"/> is how a body silently vanishes: Godot declines to draw the mesh, and
/// nothing logs a word. Every <see cref="Mathf.Acos"/> argument below is clamped into
/// <c>[-1, 1]</c>, every divisor is floored, and every input is checked for finiteness on the way
/// in — so a rig whose sub-segment failed to measure degrades to a pose rather than to an invisible
/// player.</para>
/// </summary>
public static class LimbIk
{
    /// <summary>Which way the middle joint is allowed to buckle. Not a preference — a limb is
    /// anatomically one or the other, and picking the wrong one gives a leg that bends like an arm,
    /// which reads as a broken export rather than as a tuning mistake.</summary>
    public enum Bend
    {
        /// <summary><b>A knee.</b> The shin TRAILS the thigh: shortening the leg carries the knee
        /// FORWARD of the hip-to-ankle line and swings the shin's lower end back — the human crouch,
        /// knees over toes. The joint angle is negative.</summary>
        KneeBackward,

        /// <summary><b>An elbow.</b> The forearm LEADS the upper arm: shortening the arm carries the
        /// elbow BEHIND the shoulder-to-wrist line and swings the hand forward — a bicep curl. The
        /// joint angle is positive.</summary>
        ElbowForward,
    }

    /// <summary>One solved limb pose.</summary>
    /// <param name="RootAngleRad">Rotation of the UPPER segment about the limb's root (the hip, the
    /// shoulder), radians, positive forward. Goes straight into the rig node's local
    /// <c>Rotation.X</c>.</param>
    /// <param name="JointAngleRad">Rotation of the LOWER segment relative to the upper one (the
    /// knee, the elbow), radians. Negative for a knee, positive for an elbow — see
    /// <see cref="Bend"/>. Exactly zero when the limb is straight.</param>
    /// <param name="ReachM">The distance actually solved for, in metres, after clamping. Equals the
    /// target's distance when <paramref name="Reached"/> is true; otherwise it is whichever bound
    /// the target was clamped to. Reported rather than inferred because the rig needs it to place a
    /// foot contact point, and re-deriving it at the call site is how two numbers for one quantity
    /// start to disagree.</param>
    /// <param name="Reached">False when the target lay outside the annulus the limb can reach — i.e.
    /// when the pose returned is a clamp rather than a solution. Nothing in the rig currently
    /// branches on it; it exists so a test can tell a clamp from a solve, which is the difference
    /// between "the limb is extended because it was asked to be" and "the limb is extended because
    /// the maths gave up".</param>
    public readonly record struct Solution(
        float RootAngleRad, float JointAngleRad, float ReachM, bool Reached);

    /// <summary>
    /// Solves the limb. See the class doc for the plane, the sign convention and the NaN guarantee.
    /// </summary>
    /// <param name="upperM">Length of the upper segment (thigh, upper arm), metres.</param>
    /// <param name="lowerM">Length of the lower segment (shin, forearm), metres.</param>
    /// <param name="targetLocal">Where the limb's TIP should land, relative to the limb's root, in
    /// the limb's plane: <c>X</c> forward, <c>Y</c> up. The rest pose is
    /// <c>(0, -(upperM + lowerM))</c>.</param>
    /// <param name="bend">Which way the middle joint buckles.</param>
    public static Solution Solve(float upperM, float lowerM, Vector2 targetLocal, Bend bend)
    {
        // A limb with no length has no pose. Returning rest rather than throwing is deliberate: the
        // caller is a per-frame animation path on a rig that may have degraded (a model whose
        // sub-segment did not measure), and a body standing in its rest pose is a legible failure
        // while an exception on the render path is not.
        float upper = Finite(upperM);
        float lower = Finite(lowerM);
        if (upper <= 0f || lower <= 0f)
            return new Solution(0f, 0f, Mathf.Max(0f, upper + lower), false);

        float tx = Finite(targetLocal.X);
        float ty = Finite(targetLocal.Y);

        // The direction to the target, as an angle off straight-down, positive forward. atan2(0, 0)
        // is 0 in .NET rather than NaN, so a target sitting exactly on the root is a defined
        // direction (straight down) and the fold below handles the rest.
        float toTarget = Mathf.Atan2(tx, -ty);

        float maxReach = upper + lower;
        float minReach = Mathf.Abs(upper - lower);
        float distance = Mathf.Sqrt((tx * tx) + (ty * ty));
        // Sqrt of two finite squares can still overflow to infinity for absurd inputs (1e9 squared
        // is 1e18, which is fine, but 1e30 squared is not) — so the clamp is applied to a value that
        // has itself been made finite first.
        distance = Finite(distance);

        // WHICH SIDE. For a knee the upper segment leads the target line (carrying the joint
        // forward) and the lower segment folds back behind it; an elbow is the exact mirror. One
        // sign, applied to both terms with opposite polarity, is what makes the two directions
        // provable mirrors of each other rather than two hand-worked cases.
        float side = bend == Bend.KneeBackward ? 1f : -1f;

        // THE TWO BOUNDS ARE SOLVED EXACTLY, NOT PUT THROUGH THE TRIANGLE, and this is a measured
        // correctness fix rather than a tidy-up. Acos loses precision like a square root at both of
        // its endpoints: at full extension the cosine argument is -1 + O(1e-7), and Acos of that is
        // pi - 5e-4 rather than pi. Pushed through the general case, a target the gait placed at
        // EXACTLY leg length therefore came back with about 1e-3 rad of knee bend out of nowhere —
        // physically 0.03 degrees and invisible, but it makes the "the gait never bends the knee"
        // invariant unassertable at any honest tolerance, and an invariant you cannot assert is one
        // that drifts. At a bound the answer is known in closed form, so it is returned in closed
        // form.
        //
        // The band is RELATIVE to the limb (1e-6 of its length: 0.44 micrometres on the greybox's
        // leg), which is ~14x the float noise in a sin/cos round trip and suppresses at most 0.16
        // degrees of genuine bend. An absolute epsilon would be wrong for a limb of any other scale.
        float band = Mathf.Max(1e-9f, maxReach * 1e-6f);

        if (distance >= maxReach - band)
        {
            // Fully extended, pointing at the target. Reached only if it was not actually further
            // away than the limb can go.
            return new Solution(toTarget, 0f, maxReach, distance <= maxReach + band);
        }

        if (distance <= minReach + band)
        {
            // Fully folded. The tip sits minReach out along the LONGER segment's direction, so the
            // upper segment points at the target when it is the longer one and directly away from it
            // when it is not. Equal-length bones fold onto their own root, where the direction is
            // arbitrary and 0 is as good as anything.
            float away = upper >= lower ? 0f : Mathf.Pi;
            return new Solution(toTarget + away, -side * Mathf.Pi, minReach,
                distance >= minReach - band);
        }

        float d = distance;

        // The interior angle at the joint, and the deviation from straight that it implies. Both
        // Acos arguments are clamped into [-1, 1] before the call, which is the whole NaN defence:
        // float arithmetic near a bound routinely lands at 1.0000001, and Acos of that is NaN.
        float interior = Mathf.Acos(Clamp11(((upper * upper) + (lower * lower) - (d * d))
            / Mathf.Max(1e-9f, 2f * upper * lower)));
        float bendFromStraight = Mathf.Pi - interior;

        // The angle between the upper segment and the line to the target.
        float rootOffset = Mathf.Acos(Clamp11(((upper * upper) + (d * d) - (lower * lower))
            / Mathf.Max(1e-9f, 2f * upper * d)));

        return new Solution(
            toTarget + (side * rootOffset),
            -side * bendFromStraight,
            d,
            true);
    }

    // MERGE NOTE (PLAYTEST-1 trunk, 2026-08-22): ANIM-M2b's LevelAnkle and CARRY-1's spatial
    // solver were added at the same insertion point in this static class and conflicted purely
    // on position. They share no state and call none of each other's members — the ankle is a
    // planar post-step on Solve, the spatial solver is a separate 3D entry point — so both are
    // kept verbatim.
    /// <summary>
    /// <b>The ankle angle that holds a foot's sole flat on the ground (ANIM-M2b).</b> Given the two
    /// angles <see cref="Solve"/> just returned, this is the rotation the foot node needs RELATIVE
    /// to the shin for the foot to come out level in the world.
    ///
    /// <para><b>Why the ankle belongs to the solver rather than to a clip, which is the load-bearing
    /// decision in ANIM-M2b.</b> Rotations compose down the chain, so a foot's world pitch is
    /// <c>hip + knee + ankle</c> and "level" means that sum is zero. The knee is
    /// <see cref="Solve"/>'s: it is whatever the jump tuck, the landing absorb or the launch snap
    /// left it at on THIS frame. A clip author has no way to know the number they would have to
    /// cancel, so a hand-keyed ankle is not merely harder than this — it is wrong by construction on
    /// every frame the knee is not straight. That is the same argument that gave the middle joint to
    /// the solver in the first place, applied one segment further down.</para>
    ///
    /// <para><b>The clamp is anatomy, not a safety net.</b> The leg reaches ±38.74° at the ratified
    /// stance extremes and much further in a knock-out, and an unclamped level would fold the foot
    /// through the shin at the far end of that. Past its limits the ankle stops levelling and the
    /// foot simply rides the leg — which is what a real ankle at its stop does.</para>
    ///
    /// <para><b>What it deliberately does NOT do: ask where the ground is.</b> It levels against the
    /// rig's own rest plane, which is the plane the whole gait is built on. A per-foot terrain query
    /// would be a second, differently-timed source of truth for a body whose vertical is already
    /// owned by the derived bob and by reconciliation smoothing. Sloped ground is a later packet
    /// with a raycast budget attached, not a line here.</para>
    ///
    /// <para><b>Pure arithmetic, and it never returns NaN</b> — same contract as everything else in
    /// this class, and for the same reason: a NaN on a foot transform is an invisible body.</para>
    /// </summary>
    /// <param name="rootAngleRad">The hip angle — <see cref="Solution.RootAngleRad"/>.</param>
    /// <param name="jointAngleRad">The knee angle — <see cref="Solution.JointAngleRad"/>.</param>
    /// <param name="maxDorsiRad">How far the toe may lift toward the shin (a positive result).</param>
    /// <param name="maxPlantarRad">How far it may point away from the shin (a negative result).</param>
    public static float LevelAnkle(float rootAngleRad, float jointAngleRad,
        float maxDorsiRad, float maxPlantarRad)
    {
        float leg = Finite(rootAngleRad) + Finite(jointAngleRad);
        float dorsi = Mathf.Max(0f, Finite(maxDorsiRad));
        float plantar = Mathf.Max(0f, Finite(maxPlantarRad));
        return Mathf.Clamp(-leg, -plantar, dorsi);
    }

    /// <summary>One limb solved to a target in THREE dimensions — see <see cref="SolveSpatial"/>.</summary>
    /// <param name="Root">Orientation for the limb's ROOT node, relative to its authored rest. Maps
    /// the rest direction <c>(0, -1, 0)</c> onto the upper segment and its local <c>X</c> onto the axis
    /// the middle joint bends about, so the lower segment's existing <c>Rotation.X</c> channel is still
    /// the only thing that has to move. Exactly <see cref="Basis.Identity"/> at rest.</param>
    /// <param name="JointAngleRad">The middle joint's bend, radians, positive — an elbow, in the same
    /// sign convention <see cref="Bend.ElbowForward"/> reports.</param>
    /// <param name="ShortfallM">How far short of <paramref name="ShortfallM"/>'s own target the tip
    /// landed: zero whenever the target was inside the limb's reach, otherwise the distance the limb
    /// could not cover. Reported rather than swallowed — a pose that asks for a hand the arm cannot
    /// reach is a fact about the pose.</param>
    public readonly record struct SpatialSolution(
        Basis Root, float JointAngleRad, float ShortfallM);

    /// <summary>
    /// <b>Two-bone IK to a target anywhere around the limb's root, not just in one plane</b>
    /// (CARRY-1). <see cref="Solve"/> is deliberately planar, and a carry is not: the net hangs on the
    /// body's own midline, 17.5 cm inboard of the shoulder that has to reach it, so the plane itself
    /// has to tilt. Same law of cosines, applied in the plane through the root that contains both the
    /// target and the joint's pole.
    ///
    /// <para><b>The root is never translated, and that is the whole point of this overload's
    /// existence.</b> The pose it replaced wrote the shoulder node's POSITION, which on a rig whose
    /// arm origin IS its shoulder translates the joint out of the torso. Nothing here can move a
    /// joint; it returns an orientation and a bend.</para>
    ///
    /// <para><b>The pole swings from BACK to <paramref name="outwardPole"/> as the joint folds.</b> A
    /// straight-back elbow is the textbook answer and it is wrong for a cross-body reach — on the
    /// greybox it solves the elbow to the dead centre of the chest. Swinging the pole outboard fixes
    /// that; scaling the swing BY THE FOLD is what keeps a straight limb honest, because at zero bend
    /// the pole is exactly <see cref="Vector3.Back"/>, the bend axis is exactly <c>+X</c>, and
    /// <see cref="SpatialSolution.Root"/> is exactly the identity — so a straight limb is
    /// bit-for-bit the pose the planar path produces and a blend between the two cannot pop.</para>
    ///
    /// <para><b>The NaN guarantee is this class's, unchanged.</b> Every <see cref="Mathf.Acos"/>
    /// argument is clamped, every divisor floored, every input made finite, and the reach bounds are
    /// ordered before they are used — a limb with no lower segment has
    /// <c>minReach == maxReach</c>, and <see cref="Mathf.Clamp"/> forwards to
    /// <c>System.Math.Clamp</c>, <b>which throws when min exceeds max</b>. That is not hypothetical:
    /// it is the exception every stub-armed body in the game would have raised on its render
    /// path, found by CARRY-1's degradation control.</para>
    /// </summary>
    /// <param name="upperM">Length of the upper segment, metres.</param>
    /// <param name="lowerM">Length of the lower segment, metres. Zero for a limb with no middle joint,
    /// which degrades to pointing the whole limb at the target.</param>
    /// <param name="targetLocal">Where the TIP should land, relative to the root, in the limb's
    /// rest-local frame. The rest target is <c>(0, -(upperM + lowerM), 0)</c>.</param>
    /// <param name="outwardPole">The direction the middle joint should bulge toward at a full fold.
    /// Non-finite or degenerate falls back to <see cref="Vector3.Back"/>.</param>
    public static SpatialSolution SolveSpatial(
        float upperM, float lowerM, Vector3 targetLocal, Vector3 outwardPole)
    {
        float upper = Mathf.Max(0f, Finite(upperM));
        float lower = Mathf.Max(0f, Finite(lowerM));
        float maxReach = Mathf.Max(1e-4f, upper + lower);
        float minReach = Mathf.Abs(upper - lower);

        // A non-finite or degenerate target resolves to REST rather than to an exception on the render
        // path — the same choice Solve makes, for the same reason.
        Vector3 t = new(Finite(targetLocal.X), Finite(targetLocal.Y), Finite(targetLocal.Z));
        if (t.LengthSquared() < 1e-12f)
            t = new Vector3(0f, -maxReach, 0f);

        // Sqrt of two finite squares can still overflow to infinity, and a non-finite distance would
        // poison the direction. Same choke point Finite() is everywhere else in this class.
        float asked = Finite(t.Length());
        if (asked <= 1e-9f)
        {
            t = new Vector3(0f, -maxReach, 0f);
            asked = maxReach;
        }
        float lo = Mathf.Min(minReach + 1e-5f, maxReach);   // see the class doc: ORDER, then clamp
        float dist = Mathf.Clamp(asked, lo, maxReach);
        float shortfall = Mathf.Max(0f, asked - maxReach);
        Vector3 u = t / asked;
        if (!u.IsFinite() || u.LengthSquared() < 1e-12f)
            u = Vector3.Down;
        u = u.Normalized();

        // THE TWO BOUNDS ARE SOLVED EXACTLY, NOT PUT THROUGH THE TRIANGLE — Solve's own reasoning,
        // and it is not a tidy-up here either. Acos loses precision like a square root at its
        // endpoints: at full extension the cosine argument is -1 + O(1e-7) and Acos of that is
        // pi - 5e-4, so a target at EXACTLY limb length came back with 4.88e-4 rad of bend out of
        // nowhere. Physically 0.028 degrees and invisible — and it makes "a straight arm is exactly
        // the pose SolveArm produces" unassertable at any honest tolerance, which is the one property
        // the rig's branch between the two paths depends on. The band is RELATIVE to the limb, for the
        // reason Solve's is.
        float band = Mathf.Max(1e-9f, maxReach * 1e-6f);
        float rootOffset = 0f;
        float bend = 0f;
        if (upper > 0f && lower > 0f && dist < maxReach - band)
        {
            if (dist <= minReach + band)
            {
                // Fully folded: the tip sits minReach out along the longer segment's direction.
                rootOffset = upper >= lower ? 0f : Mathf.Pi;
                bend = Mathf.Pi;
            }
            else
            {
                rootOffset = Mathf.Acos(Clamp11(((upper * upper) + (dist * dist) - (lower * lower))
                    / Mathf.Max(1e-9f, 2f * upper * dist)));
                float interior = Mathf.Acos(Clamp11(((upper * upper) + (lower * lower) - (dist * dist))
                    / Mathf.Max(1e-9f, 2f * upper * lower)));
                bend = Mathf.Pi - interior;
            }
        }

        Vector3 outward = outwardPole.IsFinite() && outwardPole.LengthSquared() > 1e-8f
            ? outwardPole.Normalized()
            : Vector3.Back;
        Vector3 pole = Vector3.Back.Lerp(outward, Mathf.Clamp(bend / (Mathf.Pi * 0.5f), 0f, 1f));
        Vector3 axis = pole.Cross(u);
        axis = axis.LengthSquared() > 1e-8f ? axis.Normalized() : Vector3.Right;

        // Rotating the target direction BACK by rootOffset about the plane's normal is where the upper
        // segment points; the joint then sits toward the pole, which is what makes it an elbow.
        Vector3 upperDir = u.Rotated(axis, -Finite(rootOffset));
        if (!upperDir.IsFinite() || upperDir.LengthSquared() < 1e-12f)
            upperDir = Vector3.Down;
        upperDir = upperDir.Normalized();

        // Columns: local X onto the bend axis, local -Y down the upper segment, Z right-handed. The
        // axis is perpendicular to upperDir by construction — upperDir is u rotated ABOUT it — except
        // on the degenerate fallback, where a re-derived axis is the honest answer rather than letting
        // Orthonormalized() divide by a zero-length column and hand a NaN to a Transform3D.
        Vector3 xCol = axis;
        Vector3 yCol = -upperDir;
        Vector3 zCol = xCol.Cross(yCol);
        if (zCol.LengthSquared() < 1e-8f)
        {
            xCol = Mathf.Abs(yCol.Y) > 0.9f ? Vector3.Right : Vector3.Up.Cross(yCol).Normalized();
            zCol = xCol.Cross(yCol);
        }
        var root = new Basis(xCol, yCol, zCol.Normalized()).Orthonormalized();
        if (!root.Column0.IsFinite() || !root.Column1.IsFinite() || !root.Column2.IsFinite())
            root = Basis.Identity;
        return new SpatialSolution(root, Finite(bend), Finite(shortfall));
    }

    /// <summary>Forward kinematics for <see cref="SolveSpatial"/>: where the TIP lands, relative to the
    /// root, for a solved pose. The only honest way to assert that a spatial solve hit its target.
    /// Mirrors how the rig composes it — the lower segment is a child of the upper and rotates about
    /// its own local X.</summary>
    public static Vector3 SpatialTip(float upperM, float lowerM, in SpatialSolution s)
    {
        float upper = Mathf.Max(0f, Finite(upperM));
        float lower = Mathf.Max(0f, Finite(lowerM));
        Vector3 joint = s.Root * new Vector3(0f, -upper, 0f);
        Basis lowerBasis = s.Root * new Basis(Vector3.Right, Finite(s.JointAngleRad));
        return joint + (lowerBasis * new Vector3(0f, -lower, 0f));
    }

    /// <summary>Forward kinematics: where the limb's TIP lands for a given pose. The inverse of
    /// <see cref="Solve"/>, and the only honest way to assert that a solve hit its target — comparing
    /// angles against expected angles would test the arithmetic against itself.</summary>
    public static Vector2 Tip(float upperM, float lowerM, float rootAngleRad, float jointAngleRad)
    {
        Vector2 joint = Joint(upperM, rootAngleRad);
        return joint + Segment(lowerM, rootAngleRad + jointAngleRad);
    }

    /// <summary>Where the middle JOINT lands — the knee, the elbow — for a given root angle. The
    /// point a test looks at to say "the knee is forward of the line", which is the whole of the bend
    /// direction being right.</summary>
    public static Vector2 Joint(float upperM, float rootAngleRad) => Segment(upperM, rootAngleRad);

    /// <summary>One segment's tip, offset from its own root, for a segment hanging DOWN and rotated
    /// forward by <paramref name="angleRad"/>.</summary>
    private static Vector2 Segment(float lengthM, float angleRad)
    {
        float len = Mathf.Max(0f, Finite(lengthM));
        float a = Finite(angleRad);
        return new Vector2(Mathf.Sin(a) * len, -Mathf.Cos(a) * len);
    }

    /// <summary>Non-finite in, zero out. The single choke point every input passes through, so the
    /// NaN guarantee is one line to audit rather than a scatter of ad-hoc checks.</summary>
    private static float Finite(float v) => float.IsFinite(v) ? v : 0f;

    private static float Clamp11(float v) => Mathf.Clamp(Finite(v), -1f, 1f);
}
