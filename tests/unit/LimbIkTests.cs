using Godot;
using MpFoundation.Game.Sandbox;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>RIG-1's two-bone solver, asserted rather than eyeballed.</b> <see cref="LimbIk"/> is pure
/// arithmetic with no <c>Node</c> and no scene tree, so every claim RIG-1's report makes about the
/// knee is an assertion here instead of a human squinting at a capture.
///
/// <para><b>The load-bearing test is <see cref="RigidGaitFootPositions_AreReproducedExactly"/>.</b>
/// Today's gait keeps each leg rigid and only rotates it about the hip, so every foot position it
/// asks for sits at exactly <c>|leg|</c> from the hip. Feed those same positions through a two-bone
/// solver whose segments sum to <c>|leg|</c> and it must return a straight knee and reproduce the
/// pose exactly — which is what makes the knee free: it appears ONLY where something genuinely
/// shortens the leg (the jump tuck, the landing absorb, the launch snap), so the ratified walk, run,
/// skid and stride x cadence identity cannot drift.</para>
///
/// <para><b>The NaN prohibition is not defensive padding.</b> A NaN reaching a transform is how a
/// body silently vanishes — the mesh is simply not drawn and nothing logs. Every clamp in
/// <see cref="LimbIk"/> exists for that, and every one of them is exercised below.</para>
/// </summary>
public class LimbIkTests
{
    /// <summary>Named helper rather than xUnit's <c>Assert.Equal(float, float, int)</c>, which is
    /// ambiguous against its <c>(double, double, int)</c> overload here.</summary>
    private static void Near(float expected, float actual, float eps = 1e-4f) =>
        Assert.True(Mathf.Abs(expected - actual) <= eps,
            $"expected {expected:F6}, got {actual:F6} (tolerance {eps:G})");

    private static void AllFinite(LimbIk.Solution s)
    {
        Assert.True(float.IsFinite(s.RootAngleRad), $"RootAngleRad = {s.RootAngleRad}");
        Assert.True(float.IsFinite(s.JointAngleRad), $"JointAngleRad = {s.JointAngleRad}");
        Assert.True(float.IsFinite(s.ReachM), $"ReachM = {s.ReachM}");
    }

    // =============================================================================================
    // 1. A reachable target is hit exactly.
    // =============================================================================================

    /// <summary>A target inside the annulus is reproduced by forward kinematics to 1e-4 m, for both
    /// bend directions and for asymmetric bones.</summary>
    [Theory]
    // upper, lower, targetX, targetY
    [InlineData(0.22f, 0.22f, 0.00f, -0.40f)]
    [InlineData(0.22f, 0.22f, 0.12f, -0.34f)]
    [InlineData(0.22f, 0.22f, -0.15f, -0.30f)]
    [InlineData(0.22f, 0.22f, 0.30f, -0.10f)]
    [InlineData(0.20f, 0.18f, 0.05f, -0.30f)]
    [InlineData(0.20f, 0.18f, -0.20f, -0.22f)]
    [InlineData(0.30f, 0.10f, 0.00f, -0.32f)]
    public void ReachableTarget_IsReproducedByForwardKinematics(
        float upper, float lower, float tx, float ty)
    {
        var target = new Vector2(tx, ty);
        foreach (LimbIk.Bend bend in new[] { LimbIk.Bend.KneeBackward, LimbIk.Bend.ElbowForward })
        {
            LimbIk.Solution s = LimbIk.Solve(upper, lower, target, bend);
            AllFinite(s);
            Assert.True(s.Reached, $"{bend}: target {target} should be reachable");
            Vector2 tip = LimbIk.Tip(upper, lower, s.RootAngleRad, s.JointAngleRad);
            Near(target.X, tip.X);
            Near(target.Y, tip.Y);
        }
    }

    /// <summary>A limb at full extension pointing straight down is the rest pose: both angles are
    /// zero. This is the identity the whole gait-invariance argument rests on.</summary>
    [Fact]
    public void FullyExtendedStraightDown_IsTheRestPose()
    {
        LimbIk.Solution s = LimbIk.Solve(0.22f, 0.22f, new Vector2(0f, -0.44f),
            LimbIk.Bend.KneeBackward);
        AllFinite(s);
        Near(0f, s.RootAngleRad, 1e-5f);
        Near(0f, s.JointAngleRad, 1e-5f);
        Near(0.44f, s.ReachM, 1e-5f);
    }

    // =============================================================================================
    // 2. Beyond reach clamps to fully extended, and never NaNs.
    // =============================================================================================

    /// <summary>A target further than <c>upper + lower</c> gives a STRAIGHT limb pointing at it —
    /// not an <c>Acos</c> out of domain, and not a NaN.</summary>
    [Fact]
    public void TargetBeyondReach_ClampsToStraightAndPointsAtIt()
    {
        LimbIk.Solution s = LimbIk.Solve(0.22f, 0.22f, new Vector2(0.30f, -0.60f),
            LimbIk.Bend.KneeBackward);
        AllFinite(s);
        Assert.False(s.Reached, "a target 0.67 m away on a 0.44 m limb is not reachable");
        Near(0f, s.JointAngleRad, 1e-5f);
        // atan2(x, -y): the angle from straight down, positive forward.
        Near(Mathf.Atan2(0.30f, 0.60f), s.RootAngleRad, 1e-5f);
        Near(0.44f, s.ReachM, 1e-5f);
    }

    /// <summary>Absurd, degenerate and hostile targets all produce finite output. Each row is a
    /// separate way an <c>Acos</c> argument or a division could leave its domain.</summary>
    [Theory]
    [InlineData(0f, 0f)]                     // the root itself: zero length, atan2(0,0)
    [InlineData(1e9f, -1e9f)]                // absurdly far
    [InlineData(0f, 1e9f)]                   // straight up, absurdly far
    [InlineData(float.NaN, -0.3f)]           // poisoned X
    [InlineData(0.1f, float.NaN)]            // poisoned Y
    [InlineData(float.PositiveInfinity, 0f)] // infinite X
    [InlineData(0f, float.NegativeInfinity)] // infinite Y
    public void HostileTargets_NeverProduceNan(float tx, float ty)
    {
        foreach (LimbIk.Bend bend in new[] { LimbIk.Bend.KneeBackward, LimbIk.Bend.ElbowForward })
        {
            LimbIk.Solution s = LimbIk.Solve(0.22f, 0.22f, new Vector2(tx, ty), bend);
            AllFinite(s);
        }
    }

    /// <summary>Degenerate bone lengths — zero, negative, non-finite — produce finite output. A rig
    /// whose sub-segment failed to measure must degrade to a pose, never to a vanished body.</summary>
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0f, 0.22f)]
    [InlineData(0.22f, 0f)]
    [InlineData(-0.22f, 0.22f)]
    [InlineData(float.NaN, 0.22f)]
    [InlineData(0.22f, float.PositiveInfinity)]
    public void DegenerateBoneLengths_NeverProduceNan(float upper, float lower)
    {
        foreach (LimbIk.Bend bend in new[] { LimbIk.Bend.KneeBackward, LimbIk.Bend.ElbowForward })
        {
            LimbIk.Solution s = LimbIk.Solve(upper, lower, new Vector2(0.05f, -0.30f), bend);
            AllFinite(s);
        }
    }

    // =============================================================================================
    // 3. Nearer than |upper - lower| clamps to fully folded.
    // =============================================================================================

    /// <summary>A target inside the inner dead zone of an asymmetric limb folds it completely — the
    /// joint at 180 degrees — rather than dividing by a zero-length triangle side.</summary>
    [Fact]
    public void TargetInsideMinimumReach_ClampsToFullyFolded()
    {
        // |0.30 - 0.10| = 0.20; a target 0.05 m out cannot be reached by any pose.
        LimbIk.Solution s = LimbIk.Solve(0.30f, 0.10f, new Vector2(0f, -0.05f),
            LimbIk.Bend.KneeBackward);
        AllFinite(s);
        Assert.False(s.Reached, "0.05 m is inside the 0.20 m dead zone");
        Near(Mathf.Pi, Mathf.Abs(s.JointAngleRad), 1e-4f);
        Near(0.20f, s.ReachM, 1e-5f);
    }

    /// <summary>Equal-length bones have a dead zone of exactly zero, so a target AT the root folds
    /// the limb flat instead of dividing by zero.</summary>
    [Fact]
    public void TargetAtTheRoot_OnEqualBones_FoldsFlatWithoutDividingByZero()
    {
        LimbIk.Solution s = LimbIk.Solve(0.22f, 0.22f, Vector2.Zero, LimbIk.Bend.KneeBackward);
        AllFinite(s);
        Near(Mathf.Pi, Mathf.Abs(s.JointAngleRad), 1e-4f);
    }

    // =============================================================================================
    // 4. Bend direction is explicit: knees backward, elbows forward.
    // =============================================================================================

    /// <summary>A shortened leg puts its KNEE forward of the hip-to-ankle line and its shin trailing
    /// — the human crouch. The joint rotation is negative (backward) in this rig's convention, where
    /// a positive rotation about local X swings a limb forward.</summary>
    [Fact]
    public void Knee_BendsBackward_ShinTrails()
    {
        LimbIk.Solution s = LimbIk.Solve(0.22f, 0.22f, new Vector2(0f, -0.34f),
            LimbIk.Bend.KneeBackward);
        AllFinite(s);
        Assert.True(s.RootAngleRad > 0.01f,
            $"the thigh should carry the knee FORWARD; root angle was {s.RootAngleRad:F4} rad");
        Assert.True(s.JointAngleRad < -0.01f,
            $"the shin should TRAIL the thigh; joint angle was {s.JointAngleRad:F4} rad");
        // And the knee itself is forward of the straight line down from the hip.
        Vector2 knee = LimbIk.Joint(0.22f, s.RootAngleRad);
        Assert.True(knee.X > 0.01f, $"knee should sit forward; it was at x = {knee.X:F4} m");
    }

    /// <summary>A shortened arm puts its ELBOW behind the shoulder-to-wrist line and its forearm
    /// leading forward — a bicep curl, the opposite of the knee.</summary>
    [Fact]
    public void Elbow_BendsForward_ForearmLeads()
    {
        LimbIk.Solution s = LimbIk.Solve(0.20f, 0.18f, new Vector2(0f, -0.30f),
            LimbIk.Bend.ElbowForward);
        AllFinite(s);
        Assert.True(s.RootAngleRad < -0.01f,
            $"the upper arm should carry the elbow BACK; root angle was {s.RootAngleRad:F4} rad");
        Assert.True(s.JointAngleRad > 0.01f,
            $"the forearm should LEAD forward; joint angle was {s.JointAngleRad:F4} rad");
        Vector2 elbow = LimbIk.Joint(0.20f, s.RootAngleRad);
        Assert.True(elbow.X < -0.01f, $"elbow should sit behind; it was at x = {elbow.X:F4} m");
    }

    /// <summary>The two directions are exact mirrors of each other on a target in the sagittal
    /// midline — the same triangle, solved to opposite sides.</summary>
    [Fact]
    public void TheTwoBendDirections_AreMirrorsOnAMidlineTarget()
    {
        var target = new Vector2(0f, -0.34f);
        LimbIk.Solution knee = LimbIk.Solve(0.22f, 0.22f, target, LimbIk.Bend.KneeBackward);
        LimbIk.Solution elbow = LimbIk.Solve(0.22f, 0.22f, target, LimbIk.Bend.ElbowForward);
        Near(knee.RootAngleRad, -elbow.RootAngleRad, 1e-5f);
        Near(knee.JointAngleRad, -elbow.JointAngleRad, 1e-5f);
    }

    // =============================================================================================
    // 5. THE SAFETY PROPERTY: the ratified gait cannot drift (acceptance criterion 6).
    // =============================================================================================

    /// <summary>
    /// <b>The invariance sweep.</b> Over every gait speed from idle to sprint and every phase of the
    /// cycle, the foot position a two-bone solve produces is identical to the rigid-leg foot position
    /// today's gait produces, and the knee comes back straight.
    ///
    /// <para>This is the proof that RIG-1 cannot have moved MOVE-1's walk, run or skid: the gait asks
    /// for a foot at exactly <c>|leg|</c> from the hip, and a solver whose segments sum to
    /// <c>|leg|</c> answers with a straight limb at the same angle.</para>
    /// </summary>
    [Fact]
    public void RigidGaitFootPositions_AreReproducedExactly()
    {
        // The greybox's re-proportioned leg: hip at 0.44 m, knee at 0.22 m.
        const float leg = 0.44f;
        const float thigh = 0.22f;
        const float shin = leg - thigh;

        float[] speeds =
        {
            0f, 0.3f, 0.5f, 1.0f, LocomotionProfile.WalkSpeedMps, 2.5f,
            LocomotionProfile.JogSpeedMps, 6.0f, LocomotionProfile.SprintSpeedMps, 12f,
        };

        float worstPos = 0f;
        float worstJoint = 0f;
        foreach (float v in speeds)
        {
            float duty = LocomotionProfile.DutyFactorAt(v, leg);
            float reach = LocomotionProfile.StanceReachAt(v, leg);
            for (int i = 0; i <= 240; i++)
            {
                float phase = i / 240f;
                float track = LocomotionProfile.FootTrackAt(phase, duty, reach);
                float hip = LocomotionProfile.LegAngleFor(track, leg);

                // What today's rigid leg puts the foot at, relative to the hip node.
                var rigid = new Vector2(Mathf.Sin(hip) * leg, -Mathf.Cos(hip) * leg);

                LimbIk.Solution s = LimbIk.Solve(thigh, shin, rigid, LimbIk.Bend.KneeBackward);
                AllFinite(s);
                Vector2 tip = LimbIk.Tip(thigh, shin, s.RootAngleRad, s.JointAngleRad);

                worstPos = Mathf.Max(worstPos, (tip - rigid).Length());
                worstJoint = Mathf.Max(worstJoint, Mathf.Abs(s.JointAngleRad));
            }
        }

        Assert.True(worstPos <= 1e-4f,
            $"worst-case IK foot deviation from the rigid gait was {worstPos:E3} m");
        Assert.True(worstJoint <= 1e-3f,
            $"the gait must never bend the knee; worst joint angle was {worstJoint:E3} rad");
    }

    /// <summary>
    /// <b>The same property for the arm (RIG-1's acceptance criterion 7).</b> Across the whole swing
    /// stroke — <c>_swingDrive</c> -1 to +1 against <c>_swingBlend</c> 0 to 1 — the hand a two-bone
    /// solve produces from a shoulder PITCH is the hand a rigid arm produces, and the elbow stays
    /// straight.
    ///
    /// <para><b>What it no longer says, since CARRY-1.</b> This used to open "the carry, aim and swing
    /// poses TRANSLATE the arm node", and they did — that translation moved the shoulder JOINT out of
    /// the torso and is the defect CARRY-1 removed. Nothing writes an arm node's position any more;
    /// the poses are hand targets and the elbow bends to reach them. The property asserted here is
    /// unchanged and still load-bearing, because it is what keeps the GAIT path
    /// (<c>AvatarVisual.SolveArm</c>, still driven by a scalar pitch) identical to what MOVE-1
    /// ratified. The carry poses' own invariant is measured in engine, not here — see
    /// <c>GreyboxPlayerLab.MeasureCarryPose</c>.</para>
    /// </summary>
    [Fact]
    public void RigidArmHandPositions_AreReproducedExactly()
    {
        const float upper = 0.20f;
        const float lower = 0.18f;
        const float arm = upper + lower;

        float worstPos = 0f;
        float worstJoint = 0f;
        for (int d = -20; d <= 20; d++)
        {
            float drive = d / 20f;
            for (int b = 0; b <= 20; b++)
            {
                float blend = b / 20f;
                // AvatarVisual's own swing pitch: SwingArmPitchRad * drive * blend.
                float pitch = 0.50f * drive * blend;
                var rigid = new Vector2(Mathf.Sin(pitch) * arm, -Mathf.Cos(pitch) * arm);

                LimbIk.Solution s = LimbIk.Solve(upper, lower, rigid, LimbIk.Bend.ElbowForward);
                AllFinite(s);
                Vector2 tip = LimbIk.Tip(upper, lower, s.RootAngleRad, s.JointAngleRad);

                worstPos = Mathf.Max(worstPos, (tip - rigid).Length());
                worstJoint = Mathf.Max(worstJoint, Mathf.Abs(s.JointAngleRad));
            }
        }

        Assert.True(worstPos <= 1e-4f,
            $"worst-case IK hand deviation from the rigid arm was {worstPos:E3} m");
        Assert.True(worstJoint <= 1e-3f,
            $"a swing must never bend the elbow; worst joint angle was {worstJoint:E3} rad");
    }

    // =============================================================================================
    // 6. Monotonicity: a deeper shortening is a deeper bend. The jump reads on this.
    // =============================================================================================

    /// <summary>Folding the leg further always bends the knee further and never reverses direction —
    /// so the launch snap, the apex tuck and the landing absorb read as one continuous channel
    /// rather than three amplitudes that fight.</summary>
    [Fact]
    public void DeeperShortening_IsAlwaysADeeperBend()
    {
        const float leg = 0.44f;
        float previous = 0f;
        for (int i = 0; i <= 40; i++)
        {
            float shorten = i / 100f;   // 0 .. 0.40 of leg length
            LimbIk.Solution s = LimbIk.Solve(0.22f, 0.22f,
                new Vector2(0f, -leg * (1f - shorten)), LimbIk.Bend.KneeBackward);
            AllFinite(s);
            float bend = -s.JointAngleRad;   // knees bend negative
            Assert.True(bend >= previous - 1e-5f,
                $"shorten {shorten:F2}: bend {bend:F4} went backwards from {previous:F4}");
            previous = bend;
        }
        Assert.True(previous > 1.5f,
            $"a 40% fold should be a deep knee; the bend only reached {previous:F3} rad");
    }

    // MERGE NOTE (PLAYTEST-1 trunk, 2026-08-22): ANIM-M2b's ankle section and CARRY-1's
    // spatial-solve section are independent test suites appended at the same point. Both
    // kept; neither asserts anything about the other's subject.
    // =============================================================================================
    // 7. The ankle (ANIM-M2b). LevelAnkle is what turns a leg into something that stands on a foot.
    // =============================================================================================

    /// <summary><b>Inside its limits the ankle cancels the leg exactly, so the sole is level.</b>
    /// Rotations compose down the chain, so the sole's world pitch is <c>hip + knee + ankle</c> and
    /// "flat on the ground" is that sum being zero. This is the whole contract in one assertion, and
    /// it is swept across the ratified gait's real range rather than checked at one pose: the leg
    /// reaches ±38.74° at the stance extremes, which is where a foot that fails to level is most
    /// visible.</summary>
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.676f, 0f)]        // +38.74 deg, the front of stance, knee straight
    [InlineData(-0.676f, 0f)]       // -38.74 deg, the back of it
    [InlineData(0.30f, -0.25f)]     // mid-stride with a landing absorb folding the knee
    [InlineData(-0.10f, -0.40f)]    // a deep tuck
    [InlineData(0.20f, 0.05f)]
    public void InsideItsLimits_TheAnkleCancelsTheLegExactly(float hip, float knee)
    {
        float ankle = LimbIk.LevelAnkle(hip, knee, 0.7854f, 1.0472f);
        Assert.True(float.IsFinite(ankle), $"ankle = {ankle}");
        Near(0f, hip + knee + ankle, 1e-5f);
    }

    /// <summary><b>Past its limits the ankle stops levelling and the foot rides the leg</b> — which
    /// is what a real ankle at its stop does, and is the read a knock-out wants. The clamp is
    /// anatomy, not a NaN guard; without it the foot folds through the shin at the far end of a
    /// knock-out's leg throw.</summary>
    [Theory]
    [InlineData(2.0f, 0f, -1.0472f)]      // way past plantar: clamped, foot points away from the shin
    [InlineData(-2.0f, 0f, 0.7854f)]      // way past dorsi: clamped the other way
    [InlineData(1.2f, -0.6f, -0.6f)]      // hip + knee = 0.6, inside plantar, so NOT clamped
    public void PastItsLimits_TheAnkleClampsRatherThanFoldingThroughTheShin(
        float hip, float knee, float expected)
    {
        float ankle = LimbIk.LevelAnkle(hip, knee, 0.7854f, 1.0472f);
        Near(expected, ankle, 1e-5f);
        Assert.True(ankle >= -1.0472f - 1e-5f && ankle <= 0.7854f + 1e-5f,
            $"ankle {ankle:F4} escaped its own limits");
    }

    /// <summary><b>No input makes it NaN, and a nonsensical limit degrades to a stop rather than to
    /// an invisible body.</b> Same guarantee the rest of this class carries and for the same reason:
    /// a NaN on a foot transform means Godot declines to draw the mesh and logs nothing.</summary>
    [Fact]
    public void TheAnkleIsNeverNaN_AndNegativeLimitsBecomeZero()
    {
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.True(float.IsFinite(LimbIk.LevelAnkle(bad, 0.2f, 0.78f, 1.04f)));
            Assert.True(float.IsFinite(LimbIk.LevelAnkle(0.2f, bad, 0.78f, 1.04f)));
            Assert.True(float.IsFinite(LimbIk.LevelAnkle(0.2f, 0.2f, bad, 1.04f)));
            Assert.True(float.IsFinite(LimbIk.LevelAnkle(0.2f, 0.2f, 0.78f, bad)));
        }

        // A negative limit is a limit of zero, not an inverted range that would make Clamp throw.
        Near(0f, LimbIk.LevelAnkle(0.5f, 0f, -1f, -1f), 1e-6f);
    }

    /// <summary><b>The ankle never touches the two angles the gait is ratified on.</b> The whole
    /// safety argument for adding a third leg segment is that it is a READ of the solve rather than a
    /// participant in it — <c>Solve</c> is unchanged, so
    /// <see cref="RigidGaitFootPositions_AreReproducedExactly"/> is untouched by construction. This
    /// asserts the shape of that claim: the same inputs give the same solution whether or not anyone
    /// asks for an ankle afterwards.</summary>
    [Fact]
    public void AskingForAnAnkle_DoesNotChangeTheSolve()
    {
        const float leg = 0.548f;
        const float thigh = 0.274f;
        for (int i = -20; i <= 20; i++)
        {
            float hip = i * 0.04f;
            var target = new Vector2(Mathf.Sin(hip) * leg, -Mathf.Cos(hip) * leg);
            LimbIk.Solution before = LimbIk.Solve(thigh, leg - thigh, target, LimbIk.Bend.KneeBackward);
            float ankle = LimbIk.LevelAnkle(before.RootAngleRad, before.JointAngleRad, 0.7854f, 1.0472f);
            LimbIk.Solution after = LimbIk.Solve(thigh, leg - thigh, target, LimbIk.Bend.KneeBackward);
            Assert.Equal(before.RootAngleRad, after.RootAngleRad);
            Assert.Equal(before.JointAngleRad, after.JointAngleRad);
            Assert.True(float.IsFinite(ankle));
        }
    }

    // --- CARRY-1: the SPATIAL solve, which is what keeps a shoulder in its socket ----------------
    //
    // The defect CARRY-1 removed was a pose writing an arm node's POSITION on a rig whose arm origin
    // IS its shoulder, which translates the joint out of the torso. The replacement is a hand target
    // solved through LimbIk.SolveSpatial, and every property that makes that safe is asserted here
    // rather than measured only in a capture.

    /// <summary>The upper arm and forearm the greybox actually measures (0.20 / 0.18).</summary>
    private const float Upper = 0.20f;
    private const float Lower = 0.18f;

    /// <summary>AvatarVisual's own outward pole for a right arm — outboard 1, down 0.55, forward
    /// 0.25.</summary>
    private static readonly Vector3 Outward = new(1f, -0.55f, -0.25f);

    /// <summary>
    /// <b>At rest the spatial solve is the IDENTITY, exactly.</b> This is what lets the rig branch
    /// between the planar path (the ratified gait) and the spatial one (the carries) on a blend weight
    /// without a pop: at zero fold the pole is exactly <c>Vector3.Back</c>, the bend axis is exactly
    /// <c>+X</c>, and the returned basis is the one the arm already has. Break this and a carry blends
    /// in by snapping the arm through a roll on its first frame — which a constant outward pole does,
    /// measured at 45 degrees.
    /// </summary>
    [Fact]
    public void SpatialSolve_AtRest_ReturnsTheIdentityBasisAndAStraightJoint()
    {
        LimbIk.SpatialSolution s = LimbIk.SolveSpatial(
            Upper, Lower, new Vector3(0f, -(Upper + Lower), 0f), Outward);

        Near(0f, s.JointAngleRad, 1e-5f);
        Near(0f, s.ShortfallM, 1e-6f);
        for (int c = 0; c < 3; c++)
        {
            Vector3 col = s.Root.Column0;
            col = c == 1 ? s.Root.Column1 : c == 2 ? s.Root.Column2 : col;
            Vector3 want = c == 0 ? Vector3.Right : c == 1 ? Vector3.Up : Vector3.Back;
            Assert.True((col - want).Length() <= 1e-5f,
                $"column {c} was {col} rather than {want} — the rest pose is not the identity");
        }
    }

    /// <summary>
    /// <b>A pure PITCH target reproduces the planar path's own answer.</b> The gait, the jump and the
    /// swing's rotational arc all reach the arm through a scalar shoulder angle
    /// (<c>AvatarVisual.SolveArm</c>); the carries reach it through a hand target. Wherever a hand
    /// target happens to be the one that scalar angle implies, the two must agree — otherwise the
    /// moment a carry blends in over a walk, the arm jumps.
    /// </summary>
    [Fact]
    public void SpatialSolve_OnAPurePitchTarget_MatchesTheRigidArm()
    {
        const float arm = Upper + Lower;
        float worst = 0f;
        for (int i = -30; i <= 30; i++)
        {
            float pitch = i / 30f * 0.9f;   // well past SwingArmPitchRad's 0.50
            var target = new Vector3(0f, -Mathf.Cos(pitch) * arm, -Mathf.Sin(pitch) * arm);

            LimbIk.SpatialSolution s = LimbIk.SolveSpatial(Upper, Lower, target, Outward);
            AllFiniteSpatial(s);
            Near(0f, s.JointAngleRad, 2e-3f);   // a full-extension target must not bend the elbow

            Basis rigid = Basis.FromEuler(new Vector3(pitch, 0f, 0f));
            worst = Mathf.Max(worst, (s.Root.Column0 - rigid.Column0).Length());
            worst = Mathf.Max(worst, (s.Root.Column1 - rigid.Column1).Length());
            worst = Mathf.Max(worst, (s.Root.Column2 - rigid.Column2).Length());
        }
        Assert.True(worst <= 1e-3f,
            $"worst column deviation from the rigid arm's own basis was {worst:E3}");
    }

    /// <summary>
    /// <b>The tip lands on the target.</b> Forward-kinematics the solved pose the way the rig composes
    /// it — the lower segment a child of the upper, rotating about its own local X — and compare
    /// against what was asked for. Comparing angles against expected angles would test the arithmetic
    /// against itself; this tests it against the thing it is for.
    /// </summary>
    [Fact]
    public void SpatialSolve_PutsTheTipOnEveryReachableTarget()
    {
        float worst = 0f;
        string where = "nothing";
        for (int xi = -4; xi <= 4; xi++)
        {
            for (int yi = -6; yi <= 2; yi++)
            {
                for (int zi = -4; zi <= 4; zi++)
                {
                    var target = new Vector3(xi * 0.05f, yi * 0.05f, zi * 0.05f);
                    float len = target.Length();
                    if (len < Mathf.Abs(Upper - Lower) + 0.01f || len > Upper + Lower - 0.001f)
                        continue;   // outside the annulus is the clamp's job, tested separately

                    LimbIk.SpatialSolution s = LimbIk.SolveSpatial(Upper, Lower, target, Outward);
                    AllFiniteSpatial(s);
                    Near(0f, s.ShortfallM, 1e-6f);

                    float miss = (LimbIk.SpatialTip(Upper, Lower, s) - target).Length();
                    if (miss > worst)
                    {
                        worst = miss;
                        where = target.ToString();
                    }
                }
            }
        }
        Assert.True(worst <= 1e-4f, $"worst tip miss was {worst:E3} m at {where}");
    }

    /// <summary>
    /// <b>The joint bulges toward the pole, not across the body.</b> The measured reason the pole
    /// swings outboard at all: on the greybox the tool hand reaches a grip on the body's own midline,
    /// and a straight-back elbow solves to the dead centre of the torso. Asserted as a direction
    /// rather than as a coordinate, so it survives a body of another size.
    /// </summary>
    [Fact]
    public void SpatialSolve_PutsTheElbowTowardThePole_NotAcrossTheBody()
    {
        // The greybox's real handle-carry target: right shoulder (0.175, 0.82, 0) to the carry mount
        // at (0, 0.800, -0.241), expressed relative to the shoulder.
        var target = new Vector3(-0.175f, -0.020f, -0.241f);

        LimbIk.SpatialSolution back = LimbIk.SolveSpatial(Upper, Lower, target, Vector3.Back);
        LimbIk.SpatialSolution outward = LimbIk.SolveSpatial(Upper, Lower, target, Outward);
        AllFiniteSpatial(back);
        AllFiniteSpatial(outward);

        float backElbowX = (back.Root * new Vector3(0f, -Upper, 0f)).X;
        float outElbowX = (outward.Root * new Vector3(0f, -Upper, 0f)).X;

        Assert.True(outElbowX > backElbowX + 0.05f,
            $"the outward pole must carry the elbow clear of the body: back-poled elbow at " +
            $"x = {backElbowX:F4} m, outward-poled at x = {outElbowX:F4} m");
        // Both must still put the tip where it was asked to go — the pole picks AMONG solutions, it
        // never trades accuracy for one.
        Near(0f, (LimbIk.SpatialTip(Upper, Lower, back) - target).Length(), 1e-4f);
        Near(0f, (LimbIk.SpatialTip(Upper, Lower, outward) - target).Length(), 1e-4f);
    }

    /// <summary>
    /// <b>Out of reach extends the limb AT the target and reports the shortfall.</b> The swing asks
    /// for a hand 0.42-0.51 m out on a 0.38 m arm, so this is not a corner case — it is what happens
    /// every stroke. The limb must point at the target, stay straight, and say how far short it fell;
    /// the code this replaced closed that gap by translating the shoulder, which is the whole defect.
    /// </summary>
    [Fact]
    public void SpatialSolve_BeyondReach_ExtendsAtTheTargetAndReportsTheShortfall()
    {
        Vector3 direction = new Vector3(0.28f, -0.18f, -0.26f).Normalized();
        const float arm = Upper + Lower;
        for (int i = 1; i <= 10; i++)
        {
            float asked = arm + (i * 0.02f);
            LimbIk.SpatialSolution s = LimbIk.SolveSpatial(Upper, Lower, direction * asked, Outward);
            AllFiniteSpatial(s);

            Near(asked - arm, s.ShortfallM, 1e-5f);
            Near(0f, s.JointAngleRad, 2e-3f);

            Vector3 tip = LimbIk.SpatialTip(Upper, Lower, s);
            Near(arm, tip.Length(), 1e-4f);
            float dot = tip.Normalized().Dot(direction);
            Assert.True(dot > 0.9999f, $"the extended limb must point AT the target; dot was {dot:F6}");
        }
    }

    /// <summary>
    /// <b>A limb with no lower bone solves instead of throwing.</b> Every -uffling and every camper on
    /// the roster has one — <c>HasElbows</c> is false and the forearm length is exactly zero — and on
    /// such a limb <c>minReach == maxReach</c>. <c>Mathf.Clamp</c> forwards to
    /// <c>System.Math.Clamp</c>, <b>which throws when min exceeds max</b>, so before the bounds were
    /// ordered this raised <c>ArgumentException: '0.105009995' cannot be greater than 0.105</c> on the
    /// render path of every no-elbow body in the game. CARRY-1's degradation control found it on its
    /// first run; this is the regression test.
    /// </summary>
    [Fact]
    public void SpatialSolve_WithNoLowerBone_PointsTheLimbAndNeverThrows()
    {
        const float only = 0.105f;
        Vector3[] targets =
        {
            new(0f, -only, 0f),
            new(0.16f, 0.10f, -0.34f),
            new(-0.175f, -0.020f, -0.241f),
            new(0f, 0f, 0.4f),
            new(0f, 0.001f, 0f),
        };
        foreach (Vector3 target in targets)
        {
            LimbIk.SpatialSolution s = LimbIk.SolveSpatial(only, 0f, target, Outward);
            AllFiniteSpatial(s);
            Near(0f, s.JointAngleRad, 1e-6f);   // no joint to bend
            Near(only, LimbIk.SpatialTip(only, 0f, s).Length(), 1e-4f);
        }
    }

    /// <summary>
    /// <b>No spatial output is ever NaN either</b>, on the same reasoning the planar guarantee rests
    /// on: a NaN reaching a <c>Basis</c> is how a body silently stops being drawn. Hostile lengths,
    /// hostile targets, hostile poles.
    /// </summary>
    [Fact]
    public void SpatialSolve_IsNeverNaN_OnHostileInput()
    {
        float[] nasty = { 0f, -1f, float.NaN, float.PositiveInfinity, 1e20f, 1e-20f, 0.2f };
        foreach (float a in nasty)
        {
            foreach (float b in nasty)
            {
                foreach (float c in nasty)
                {
                    LimbIk.SpatialSolution s = LimbIk.SolveSpatial(
                        a, b, new Vector3(c, -c, c), new Vector3(c, -0.55f, -0.25f));
                    AllFiniteSpatial(s);
                }
            }
        }
    }

    private static void AllFiniteSpatial(in LimbIk.SpatialSolution s)
    {
        Assert.True(float.IsFinite(s.JointAngleRad), $"joint angle was {s.JointAngleRad}");
        Assert.True(float.IsFinite(s.ShortfallM), $"shortfall was {s.ShortfallM}");
        Vector3[] columns = { s.Root.Column0, s.Root.Column1, s.Root.Column2 };
        for (int c = 0; c < 3; c++)
        {
            Assert.True(columns[c].IsFinite(), $"basis column {c} was {columns[c]}");
            Assert.True(Mathf.Abs(columns[c].Length() - 1f) <= 1e-3f,
                $"basis column {c} had length {columns[c].Length():F6} — the basis is not orthonormal");
        }
    }
}
