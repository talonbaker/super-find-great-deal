using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>MOVE-5c — the animation contract, asserted rather than photographed.</b>
/// <c>docs/design/2026-08-27-movement-verbs-state-machine.md</c> §12 states numbers ("~8 degrees",
/// "~15 degrees", "a straight silhouette line from trailing toe to leading hand") and one
/// requirement that is not a number at all: the three crouch verbs must be distinguishable
/// <i>by silhouette, at distance</i>. <see cref="VerbPose"/> is pure, so all of that lands here
/// instead of under a screenshot.
///
/// <para><b>The load-bearing test is <see cref="NormalIsTheExactZeroPose"/>.</b> The packet's
/// fourth rule is that nothing Talon approved may move — the landing absorb, the take-off kick, the
/// gait and the run lean are all shipped and signed off. Every term MOVE-5c added to
/// <see cref="AvatarVisual"/> is multiplied by, added to, or lerped by a weight that is
/// <b>exactly</b> zero at <see cref="MoveVerb.Normal"/> and chain depth 0. That is what makes
/// "composes with, does not retune" a structural claim rather than a careful one, and this is where
/// the zero is pinned.</para>
///
/// <para><b>The silhouette tests carry their own arithmetic and it is stated, not hidden.</b> A
/// 1.20 m body at 30 m in this repo's 36-degree-FOV capture frame is about 62 px tall in 1000 px
/// (the frame spans 2 × 30 × tan(18°) = 19.50 m, so 51.3 px per metre). Six pixels of height
/// difference is not a read; that is why the poses below are separated primarily by where the CROWN
/// SITS OVER THE FEET rather than by how low they are, and why the assertion is on the crown's
/// displacement in the sagittal plane.</para>
/// </summary>
public class VerbPoseTests
{
    /// <summary>Named helper rather than xUnit's <c>Assert.Equal(float, float, int)</c>, which is
    /// ambiguous against its <c>(double, double, int)</c> overload here — <see cref="LimbIkTests"/>
    /// has the same one for the same reason.</summary>
    private static void Near(float expected, float actual, float eps = 1e-4f) =>
        Assert.True(Mathf.Abs(expected - actual) <= eps,
            $"expected {expected:F6}, got {actual:F6} (tolerance {eps:G})");

    // The greybox player's own measurements, MEASURED IN ENGINE on 2026-08-27 by
    // `Run-GreyboxPlayerCapture.ps1 -Verbs` (`[verb-tell] avatar 'greybox' leg 0.548 m rest crown
    // 1.200 m`) and typed here because these tests run without an engine.
    //
    // NOT 0.44. Several comments in AvatarVisual.cs derive their worked examples from a 0.44 m leg
    // ("4.4 cm on the greybox", "the greybox's equal 0.22 m segments"), and the live rig measures
    // 0.548 — `_legLengthM` is measured off vertices at build time, so the constant is what moved,
    // not the arithmetic. Recorded as a finding in MOVE-5c's handoff rather than swept through
    // other packets' comments.
    private const float LegM = 0.548f;
    private const float CrownM = 1.20f;

    /// <summary>Where a pose puts the crown, in the sagittal plane, relative to the feet: the rig
    /// sinks by <c>leg × fold</c> (the hip drop that a bent knee over a planted foot geometrically
    /// forces — see <c>AvatarVisual.CrouchDropM</c>) and then the torso above the hip pitches about
    /// it. This is the quantity a partner at distance is actually reading.</summary>
    private static Vector2 Crown(VerbPose.Targets t)
    {
        float drop = LegM * t.KneeFold;
        float torso = CrownM - LegM;
        return new Vector2(
            torso * Mathf.Sin(t.ForwardTiltRad),          // forward of the feet
            (LegM - drop) + (torso * Mathf.Cos(t.ForwardTiltRad)));
    }

    // --- THE NO-OP, WHICH IS THE PACKET'S FOURTH RULE ------------------------------------------

    [Fact]
    public void NormalIsTheExactZeroPose()
    {
        VerbPose.Targets n = VerbPose.For(MoveVerb.Normal);
        Assert.Equal(0f, n.KneeFold);
        Assert.Equal(0f, n.ForwardTiltRad);
        Assert.Equal(0f, n.ArmFold);
        Assert.Equal(0f, n.ArmSweepRad);
        Assert.Equal(0f, n.LeadLegFraction);
        Assert.Equal(0f, n.TrailLegFraction);
        Assert.False(n.LegsPlanted);
        // GaitScale is a MULTIPLIER, so its neutral value is one and not zero. A zero here would
        // silently delete the gait on every body that never enters a verb.
        Assert.Equal(1f, n.GaitScale);
        Assert.Equal(VerbPose.Normal, n);
    }

    [Fact]
    public void BlendAtZeroWeight_IsBitIdenticalToNormal()
    {
        // Not "close to": equal. Every consumer in AvatarVisual multiplies or adds these, and a
        // 1e-8 residue on the gait scale would be a body that is 0.000001% less animated forever.
        Assert.Equal(VerbPose.Normal, VerbPose.Blend(0f, 0f, 0f));
    }

    [Fact]
    public void ChainAtDepthZero_ContributesExactlyNothing()
    {
        Assert.Equal(0f, VerbPose.ChainPitchRad(0f));
        Assert.Equal(0f, VerbPose.ChainStrideWeight(0f));
        Assert.Equal(0f, VerbPose.ChainGroundHoldWeight(0f));
        Assert.Equal(0f, VerbPose.ChainArmSweepAt(0f));
        Assert.Equal(0f, VerbPose.ChainLeadArmAt(0f));
        Assert.False(VerbPose.ForcesTakeoffKick(0));
    }

    // --- §12.1's LADDER, AT ITS OWN STATED NUMBERS ----------------------------------------------

    [Fact]
    public void ChainPitch_HitsSpecTwelveOnesStatedAngles()
    {
        Near(0f, VerbPose.ChainPitchRad(0f));
        Near(Mathf.DegToRad(8.02f), VerbPose.ChainPitchRad(1f), 5e-4f);   // §12.1: "~8 degrees"
        Near(Mathf.DegToRad(15.01f), VerbPose.ChainPitchRad(2f), 5e-4f);  // §12.1: "~15 degrees"
        // §12.1 gives depths 3 and 4 DIFFERENT channels (a ground event, a trail and a held pose),
        // not more pitch. A ladder that kept pitching would have the body folded double at depth 4.
        Near(VerbPose.ChainPitchRad(2f), VerbPose.ChainPitchRad(3f));
        Near(VerbPose.ChainPitchRad(2f), VerbPose.ChainPitchRad(4f));
    }

    [Fact]
    public void ChainPitch_IsContinuousAndMonotonic_SoAPartnerReadsDeepeningNotCostumes()
    {
        // §12.1's requirement in the packet's own words: "continuously enough that a partner reads
        // deepening rather than a series of discrete costume changes". A step function would fail
        // this at its steps; the ramp does not.
        float previous = VerbPose.ChainPitchRad(0f);
        for (int i = 1; i <= 400; i++)
        {
            float depth = i * 0.01f;
            float now = VerbPose.ChainPitchRad(depth);
            Assert.True(now >= previous - 1e-6f, $"pitch went backwards at depth {depth:F2}");
            Assert.True(now - previous < 0.01f,
                $"pitch jumped {now - previous:F4} rad in one hundredth of a depth at {depth:F2} — "
                + "that is a costume change, not a deepening");
            previous = now;
        }
    }

    [Fact]
    public void ChainStride_IsTheDepthTwoTell_AndArrivesNowhereElse()
    {
        // §12.1 marks depth 2 as the one thing that MUST ship: "the first depth that changes the
        // outline rather than the shading".
        Assert.Equal(0f, VerbPose.ChainStrideWeight(0f));
        Assert.Equal(0f, VerbPose.ChainStrideWeight(1f));
        Near(0.5f, VerbPose.ChainStrideWeight(1.5f));
        Assert.Equal(1f, VerbPose.ChainStrideWeight(2f));
        Assert.Equal(1f, VerbPose.ChainStrideWeight(4f));
    }

    [Fact]
    public void ChainStride_IsALongJumpStrideAndNotAJog()
    {
        // §12.1 depth 2: "trailing leg extends into a long-jump stride — a straight silhouette line
        // from trailing toe to leading hand". The split is what makes it a stride rather than a
        // step, and MaxLegSwingRad is the leg's own anatomical stance limit (38.7 degrees).
        float split = (VerbPose.ChainLeadLegFraction - VerbPose.ChainTrailLegFraction)
            * LocomotionProfile.MaxLegSwingRad;
        Assert.True(Mathf.RadToDeg(split) > 70f,
            $"the depth-2 stride only opens {Mathf.RadToDeg(split):F1} degrees — that is a jog");
        // The trail leg goes BACK and the lead leg FORWARD. Two legs at the same sign is the
        // symmetric read SkidTrailLegFraction and LaunchSnapFraction both exist to avoid.
        Assert.True(VerbPose.ChainTrailLegFraction < 0f);
        Assert.True(VerbPose.ChainLeadLegFraction > 0f);
    }

    [Fact]
    public void ChainLeadArm_SweepsBackAtDepthOne_ThenCrossesForwardForTheDiagonal()
    {
        // §12.1 depth 1 is "arms sweep back" — BOTH of them, and the leading hand is not yet a
        // thing. Depth 2 needs a hand out in front or "a line from trailing toe to leading hand"
        // has no far end.
        Near(VerbPose.ChainArmSweepAt(1f), VerbPose.ChainLeadArmAt(1f));
        Assert.True(VerbPose.ChainArmSweepAt(1f) < 0f, "the depth-1 sweep must go BACK");
        Near(VerbPose.ChainArmReachRad, VerbPose.ChainLeadArmAt(2f));
        Assert.True(VerbPose.ChainLeadArmAt(2f) > 0f, "the depth-2 leading hand must go FORWARD");
        // The trailing arm stays back through depth 2 — otherwise both arms are forward and there
        // is no diagonal, just a body reaching.
        Assert.True(VerbPose.ChainArmSweepAt(2f) < 0f);
    }

    [Fact]
    public void ChainGroundHold_IsDepthFourOnly()
    {
        // §12.1 depth 4: "the 15 degree torso pitch HOLDS THROUGH THE LANDING instead of
        // recovering... during the 0.35 s grace it tells a partner he is still hot, which is the
        // only moment a partner could act on it."
        Assert.Equal(0f, VerbPose.ChainGroundHoldWeight(0f));
        Assert.Equal(0f, VerbPose.ChainGroundHoldWeight(3f));
        Near(0.5f, VerbPose.ChainGroundHoldWeight(3.5f));
        Assert.Equal(1f, VerbPose.ChainGroundHoldWeight(4f));
    }

    [Fact]
    public void ForcedTakeoffKick_IsDepthThreeAndAbove()
    {
        // §12.1 depth 3: "the take-off kick fires at full drive". A gate bypass, and only from 3.
        Assert.False(VerbPose.ForcesTakeoffKick(0));
        Assert.False(VerbPose.ForcesTakeoffKick(1));
        Assert.False(VerbPose.ForcesTakeoffKick(2));
        Assert.True(VerbPose.ForcesTakeoffKick(3));
        Assert.True(VerbPose.ForcesTakeoffKick(4));
    }

    // --- §12.2: THE THREE VERBS, DISTINGUISHABLE BY SILHOUETTE ----------------------------------

    [Fact]
    public void EveryVerbPair_SeparatesTheCrownByMoreThanADistantPixelCanHide()
    {
        // The acceptance bar is silhouette at distance, so the assertion is on the CROWN's position
        // in the sagittal plane — the quantity that survives being 62 px tall. 0.15 m is about
        // 7.7 px at 30 m in this repo's capture frame (51.3 px/m; see the class doc), which on a
        // 62 px figure is an eighth of its own height of head displacement. That is a read.
        VerbPose.Targets slide = VerbPose.For(MoveVerb.Slide);
        VerbPose.Targets tuck = VerbPose.For(MoveVerb.Tuck);
        VerbPose.Targets duck = VerbPose.For(MoveVerb.DuckWalk);

        Assert.True(Crown(slide).DistanceTo(Crown(tuck)) > 0.15f);
        Assert.True(Crown(slide).DistanceTo(Crown(duck)) > 0.15f);
        // THE ONE §12.2 SINGLES OUT. Tuck and DuckWalk share their locomotion exactly (§5.1), so
        // "distinguishable from each other" is a real requirement here rather than a formality —
        // and note it is carried almost entirely by the crown's FORWARD offset, not its height:
        // braced is upright, settled is head-lowered.
        Assert.True(Crown(tuck).DistanceTo(Crown(duck)) > 0.15f);
    }

    [Fact]
    public void BracedIsUpright_AndSettledIsHeadLowered_WhichIsTheInputsOwnDifference()
    {
        // §12.2: Tuck is "braced — compressed, arms in, weight low and still"; DuckWalk is
        // "settled — extended, head lowered, a walking gait". The button held versus released.
        VerbPose.Targets tuck = VerbPose.For(MoveVerb.Tuck);
        VerbPose.Targets duck = VerbPose.For(MoveVerb.DuckWalk);

        Assert.True(tuck.ForwardTiltRad < Mathf.DegToRad(5f), "a braced body does not reach forward");
        Assert.True(duck.ForwardTiltRad > Mathf.DegToRad(12f), "a settled walk lowers its head");
        Assert.True(tuck.KneeFold > duck.KneeFold, "compressed must sit lower than extended");
        Assert.True(tuck.ArmFold > duck.ArmFold, "'arms in' against 'extended'");
    }

    [Fact]
    public void TheSlideIsTheLowestPoseAndTheOnlyOneWithoutAGait()
    {
        VerbPose.Targets slide = VerbPose.For(MoveVerb.Slide);
        Assert.True(slide.KneeFold > VerbPose.For(MoveVerb.Tuck).KneeFold);
        Assert.True(slide.ForwardTiltRad > VerbPose.For(MoveVerb.DuckWalk).ForwardTiltRad);

        // §4.4: the stick contributes no acceleration and no braking — the feet are dragged, not
        // stepped. A body translating with no stride under it is legible at any distance, and it is
        // also the only honest pose: a stride over sliding feet is foot-skate.
        Assert.Equal(0f, slide.GaitScale);
        Assert.True(slide.LegsPlanted);
        // §5.1 gives the Tuck and the DuckWalk identical locomotion, so both really walk and both
        // must really step. Suppressing either would manufacture skate in a state a player can hold
        // indefinitely.
        Assert.Equal(1f, VerbPose.For(MoveVerb.Tuck).GaitScale);
        Assert.Equal(1f, VerbPose.For(MoveVerb.DuckWalk).GaitScale);
        Assert.False(VerbPose.For(MoveVerb.Tuck).LegsPlanted);
        Assert.False(VerbPose.For(MoveVerb.DuckWalk).LegsPlanted);
    }

    [Fact]
    public void TheSlidesTwoLegsPointOppositeWays()
    {
        VerbPose.Targets slide = VerbPose.For(MoveVerb.Slide);
        Assert.True(slide.LeadLegFraction > 0f);
        Assert.True(slide.TrailLegFraction < 0f);
    }

    // --- THE BLEND, AND WHY A WEIGHTED SUM IS SAFE ----------------------------------------------

    [Fact]
    public void BlendWeights_NeverSumPastOne_AcrossEveryHandover()
    {
        // VerbPose.Blend sums three weighted poses. That is only safe because the weights are
        // one-hot and ease at ONE rate: at most one target is 1, so a Slide->DuckWalk handover has
        // them moving at equal and opposite rates with a sum of exactly 1 throughout. Simulated
        // here over every ordered pair of verbs rather than argued, because the day somebody gives
        // one verb its own blend rate this is the test that goes red.
        MoveVerb[] verbs = { MoveVerb.Normal, MoveVerb.Tuck, MoveVerb.Slide, MoveVerb.DuckWalk };
        const float dt = 1f / 60f;
        foreach (MoveVerb from in verbs)
        {
            foreach (MoveVerb to in verbs)
            {
                float tuck = from == MoveVerb.Tuck ? 1f : 0f;
                float slide = from == MoveVerb.Slide ? 1f : 0f;
                float duck = from == MoveVerb.DuckWalk ? 1f : 0f;
                for (int tick = 0; tick < 60; tick++)
                {
                    float step = dt * VerbPose.VerbBlendRate;
                    tuck = Mathf.MoveToward(tuck, to == MoveVerb.Tuck ? 1f : 0f, step);
                    slide = Mathf.MoveToward(slide, to == MoveVerb.Slide ? 1f : 0f, step);
                    duck = Mathf.MoveToward(duck, to == MoveVerb.DuckWalk ? 1f : 0f, step);
                    float sum = tuck + slide + duck;
                    Assert.True(sum <= 1f + 1e-5f,
                        $"{from} -> {to} summed to {sum:F4} at tick {tick}");
                    Assert.True(sum >= -1e-6f);
                    // The gait multiplier must stay inside [0, 1] the whole way through, or a
                    // handover would briefly amplify the gait past what the ground asked for.
                    float gait = VerbPose.Blend(tuck, slide, duck).GaitScale;
                    Assert.InRange(gait, -1e-5f, 1f + 1e-5f);
                }
            }
        }
    }

    [Fact]
    public void BlendAtFullWeight_ReproducesThatVerbExactly()
    {
        Assert.Equal(VerbPose.For(MoveVerb.Tuck), VerbPose.Blend(1f, 0f, 0f));
        Assert.Equal(VerbPose.For(MoveVerb.Slide), VerbPose.Blend(0f, 1f, 0f));
        Assert.Equal(VerbPose.For(MoveVerb.DuckWalk), VerbPose.Blend(0f, 0f, 1f));
    }

    [Fact]
    public void TheBlendReachesAVerbInsideAQuarterOfASecond()
    {
        // §12.3: "every verb entry and every verb exit gets a pose change on the tick it happens —
        // in particular the pop-up on release must be visible on the tick the button comes up".
        // An eased blend satisfies that by MOVING on that tick; what it must not be is a hold.
        // 18/s is 0.056 s of full travel, the same rate the shipped skid uses.
        Near(1f / 18f, 1f / VerbPose.VerbBlendRate, 1e-6f);
        float w = 0f;
        int ticks = 0;
        while (w < 1f && ticks < 600)
        {
            w = Mathf.MoveToward(w, 1f, (1f / 60f) * VerbPose.VerbBlendRate);
            ticks++;
            if (ticks == 1)
                Assert.True(w > 0f, "the pose did not move on the tick the state changed");
        }
        Assert.True(ticks <= 15, $"the pose took {ticks} ticks to arrive — that reads as a delay");
    }

    [Fact]
    public void TheChainReadEasesSlowerThanTheVerbs_ButInsideOneJump()
    {
        // Deepening, not costumes (§12.1) — but a read that took longer than a jump's airtime would
        // never arrive before the next depth landed on top of it. The ratified arc's airtime is
        // 0.833 s held-sprint / 0.317 s jog-tap (MOVE-5b measured both on this lineage); one whole
        // depth at 6/s is 0.167 s, comfortably inside the short one.
        Assert.True(VerbPose.ChainBlendRate < VerbPose.VerbBlendRate);
        Assert.True(1f / VerbPose.ChainBlendRate < 0.317f);
    }

    // --- THE RAMP ITSELF ------------------------------------------------------------------------

    [Fact]
    public void RampIsClampedAndNeverDividesByZero()
    {
        Assert.Equal(0f, VerbPose.Ramp(-5f, 0f, 1f));
        Assert.Equal(1f, VerbPose.Ramp(5f, 0f, 1f));
        Near(0.25f, VerbPose.Ramp(0.25f, 0f, 1f));
        // A degenerate window must answer a bound rather than an infinity: a NaN reaching a
        // transform is how a body silently vanishes (see LimbIk's own note).
        Assert.Equal(1f, VerbPose.Ramp(3f, 2f, 2f));
        Assert.Equal(0f, VerbPose.Ramp(1f, 2f, 2f));
        Assert.False(float.IsNaN(VerbPose.Ramp(2f, 2f, 2f)));
    }

    [Fact]
    public void EveryPoseStaysInsideTheRigsOwnStructuralLimits()
    {
        // MaxBodyTilt is the floor-clamp invariant SandboxSelfTest.phys_fall_mesh_clamped pins, and
        // MaxLimbFold is the point past which a limb reads as broken. A verb whose own amplitude
        // already exceeded either would be a pose spending its whole existence at a clamp.
        foreach (MoveVerb verb in new[] { MoveVerb.Normal, MoveVerb.Tuck, MoveVerb.Slide, MoveVerb.DuckWalk })
        {
            VerbPose.Targets t = VerbPose.For(verb);
            Assert.InRange(t.ForwardTiltRad, 0f, AvatarVisual.MaxBodyTilt);
            Assert.InRange(t.KneeFold, 0f, 0.50f);   // AvatarVisual.MaxLimbFold
            Assert.InRange(t.ArmFold, 0f, 0.50f);
            Assert.InRange(t.GaitScale, 0f, 1f);
            Assert.True(Mathf.Abs(t.ArmSweepRad) < Mathf.Pi * 0.5f);
        }
    }

    [Fact]
    public void TheVerbAndTheChainCannotBothOwnTheTorso()
    {
        // They CAN overlap: law V0 forces Verb = Normal off the floor, but a depth-4 chain's held
        // pitch is a GROUNDED term (§12.1) and TouchdownSlideImmediate can land a body straight into
        // a slide. Summed, the deepest overlap is 0.42 + 0.262 = 0.682 rad — past MaxBodyTilt, where
        // the pose would spend its whole existence pinned at the clamp reading as neither tell.
        //
        // AvatarVisual composes them with Mathf.Max instead, which is the same argument this repo
        // already settled for the squashed mesh and the bent knee: two readings of one event, and a
        // body must not do both. This test pins the property that argument protects — that the
        // WORST CASE still fits, so nothing here ever lives at a clamp.
        float worst = Mathf.Max(
            VerbPose.For(MoveVerb.Slide).ForwardTiltRad, VerbPose.ChainPitchRad(4f));
        Assert.True(worst < AvatarVisual.MaxBodyTilt,
            $"the deepest verb-and-chain overlap is {worst:F3} rad against a "
            + $"{AvatarVisual.MaxBodyTilt:F3} clamp");
        // And it must leave real room for the terms it composes with — the lean, the anticipation,
        // the skid's backward pitch and the landing fold all share this clamp.
        Assert.True(AvatarVisual.MaxBodyTilt - worst > 0.15f,
            "no headroom left for the lean, the skid pitch and the landing fold");
    }

    [Theory]
    [InlineData(MoveVerb.Tuck)]
    [InlineData(MoveVerb.DuckWalk)]
    public void ACrouchedWalkDoesNotFootSkate(MoveVerb verb)
    {
        // A crouch genuinely shortens the leg. If the gait keeps deriving its hip angle from the
        // FULL leg, the foot — hanging `fold` closer to that hip — covers only `track x (1 - fold)`
        // of the ground the stride asked for, and the difference is skate: 16% of it, forever, in a
        // state a player can hold indefinitely. MOVE-1's whole thesis was removing exactly this, and
        // AvatarVisual therefore derives the whole gait from `gaitLegM = leg x (1 - fold)`.
        //
        // Asserted on the identity itself: the foot's horizontal displacement must be the track.
        VerbPose.Targets t = VerbPose.For(verb);
        float gaitLeg = LegM * (1f - t.KneeFold);
        float speed = LocomotionProfile.WalkSpeedMps;   // §5.3: the duck walk is exactly a walk
        float reach = LocomotionProfile.StanceReachAt(speed, gaitLeg);
        float duty = LocomotionProfile.DutyFactorAt(speed, gaitLeg);
        Assert.True(reach > 0.01f, "the crouched reach collapsed — the substitution went too far");

        for (int i = 0; i < 40; i++)
        {
            float phase = i / 40f;
            float track = LocomotionProfile.FootTrackAt(phase, duty, reach);
            float angle = LocomotionProfile.LegAngleFor(track, gaitLeg);
            // LegAngleFor negates against the track, hence the sign; the magnitudes are the claim.
            float footX = Mathf.Sin(angle) * gaitLeg;
            Near(Mathf.Abs(track), Mathf.Abs(footX), 1e-3f);
        }

        // THE POSITIVE CONTROL FOR THE FIX — the bug this prevents, measured. Solving the same
        // track against the FULL leg and then hanging the foot on the FOLDED one leaves it short by
        // exactly the fold, which is what the substitution removes.
        float midTrack = LocomotionProfile.FootTrackAt(0.25f, duty, reach);
        float wrongAngle = LocomotionProfile.LegAngleFor(midTrack, LegM);
        float wrongFootX = Mathf.Sin(wrongAngle) * gaitLeg;
        // MOVE-8: asserted as a FRACTION of the track rather than as an absolute 0.01 m. Talon's
        // speed ruling took the walk gear 2.43 -> 1.71 m/s, which shortens every stride, and the
        // absolute gap fell under a threshold that was never about absolute distance: the skate is
        // "the fold, forever", i.e. a proportion. A relative bound is what the claim always meant
        // and it no longer moves when the gear ladder does.
        float skateFraction = Mathf.Abs(Mathf.Abs(midTrack) - Mathf.Abs(wrongFootX))
                              / Mathf.Abs(midTrack);
        Assert.True(skateFraction > 0.05f,
            $"the counterexample skates by only {skateFraction * 100f:F1}% of the track — this test "
            + "cannot fail, so it proves nothing");
    }

    [Fact]
    public void TheDeepestCrouchLeavesTheTorsoClearOfTheGround()
    {
        // The hip drop is leg x fold (AvatarVisual.CrouchDropM: with the feet planted, shortening
        // both legs lowers everything above them by exactly that). The deepest pose the rig can
        // reach is the slide's fold plus a full landing absorb on top, capped at MaxLimbFold.
        //
        // WHAT THIS DOES AND DOES NOT CLAIM, and the distinction is a measurement rather than a
        // hedge. The TORSO clears: the greybox's belly sits at y 0.42 and the deepest drop is
        // 0.274 m, so 0.146 m is left. The FOOT does not, quite — the 2026-08-27 capture measured
        // the silhouette 27 mm below the floor plane in a slide, because `LimbIk.LevelAnkle` pins
        // at AnkleDorsiLimitRad (45.0 deg, measured pinned in the log) and a sole that cannot finish
        // levelling points its toe down instead. That is an ankle-solver limitation, not a fold one
        // — a real deep squat lifts the HEEL, which the solver has no term for — and it is recorded
        // as a finding rather than tuned away here, because tuning the fold down would trade a
        // measured silhouette tell for 27 mm of toe and would not remove the clamp anyway.
        const float lowestBellyVertexY = 0.42f;
        float deepest = Mathf.Min(VerbPose.SlideKneeFold + 0.10f, 0.50f) * LegM;
        Assert.True(lowestBellyVertexY - deepest > 0.05f,
            $"a {deepest:F3} m drop leaves only {lowestBellyVertexY - deepest:F3} m of clearance");
    }
}
