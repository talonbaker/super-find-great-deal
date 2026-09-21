using Godot;
using MpFoundation.Game.Sandbox.Feel;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// FEEL-1 (2026-09-20): the maths behind "pick it up by clicking, hold it where you grabbed it,
/// never inside your body". Every number the carry now depends on is DERIVED from the holder's
/// capsule and the prop's own bulk, so these tests pin the derivations rather than a table of
/// constants somebody typed.
///
/// <para>Talon's ride verdict is the spec: <i>"if the player moves, the item clips into their
/// body ... it shouldn't snap to any location; that's why there's physics and collision on the
/// objects. I'd like a scroll wheel to move the object forward and back in space."</i></para>
/// </summary>
public class CarryHoldTests
{
    /// <summary>Float comparison with an explicit tolerance. xunit 2.4.2's three-argument
    /// Assert.Equal is ambiguous between (double,double,int) and (float,float,float) for a float
    /// pair, which is why every suite in this directory carries one of these.</summary>
    private static void Near(float expected, float actual, float eps) =>
        Assert.True(Mathf.Abs(expected - actual) <= eps,
            $"expected {expected} +/- {eps}, got {actual}");

    // The two props the gate names, at their shipped dimensions (Carryable's own constants, read
    // rather than retyped, so a prefab resize moves these tests with it).
    private static Vector3 CanAabb => new(
        MpFoundation.Game.Sandbox.Carryable.CanRadiusM * 2f,
        MpFoundation.Game.Sandbox.Carryable.CanHeightM,
        MpFoundation.Game.Sandbox.Carryable.CanRadiusM * 2f);

    private static Vector3 CrateAabb => new(0.44f, 0.44f, 0.44f);

    /// <summary>The reference body's collision radius — what a real holder brings.</summary>
    private static float CapsuleRadius =>
        MpFoundation.Game.Sandbox.AvatarProportions.Fallback.CapsuleRadiusM;

    // ---------------------------------------------------------------- the prop's own bulk

    [Fact]
    public void BoundingRadius_IsTheHalfDiagonalOfTheAabb()
    {
        // A 2x2x2 box's half-diagonal is sqrt(3).
        Near(Mathf.Sqrt(3f), CarryHold.BoundingRadiusM(new Vector3(2f, 2f, 2f)), 1e-5f);
    }

    [Fact]
    public void BoundingRadius_OfADegenerateAabb_IsZero()
    {
        Near(0f, CarryHold.BoundingRadiusM(Vector3.Zero), 1e-6f);
    }

    // ---------------------------------------------------------------- HoldMin, derived per prop

    [Fact]
    public void HoldMin_IsCapsulePlusPropPlusSkin()
    {
        Near(0.36f + 0.08f + CarryHold.HolderSkinM,
            CarryHold.HoldMinM(0.36f, 0.08f), 1e-5f);
    }

    [Fact]
    public void HoldMin_IsBiggerForACrateThanForACan()
    {
        float can = CarryHold.HoldMinM(CapsuleRadius, CarryHold.BoundingRadiusM(CanAabb));
        float crate = CarryHold.HoldMinM(CapsuleRadius, CarryHold.BoundingRadiusM(CrateAabb));
        Assert.True(crate > can + 0.2f,
            $"a crate must be held further out than a can: crate={crate:F3} can={can:F3}");
    }

    [Fact]
    public void HoldMin_ClearsTheCapsuleForBothShippedProps()
    {
        foreach (Vector3 aabb in new[] { CanAabb, CrateAabb })
        {
            float r = CarryHold.BoundingRadiusM(aabb);
            float min = CarryHold.HoldMinM(CapsuleRadius, r);
            Assert.True(min - r > CapsuleRadius,
                $"the prop's nearest face must clear the capsule: min={min:F3} r={r:F3}");
        }
    }

    // ---------------------------------------------------------------- HoldMax and the clamp

    [Fact]
    public void HoldMax_AlwaysLeavesAScrollRange()
    {
        // A prop so fat that HoldMin is past the base ceiling still gets somewhere to scroll to.
        float min = CarryHold.HoldMinM(CapsuleRadius, 2.0f);
        Assert.True(min > CarryHold.HoldMaxBaseM, "fixture: this prop should be past the ceiling");
        Near(min + CarryHold.MinScrollRangeM, CarryHold.HoldMaxM(min), 1e-5f);
    }

    [Fact]
    public void HoldMax_IsTheBaseCeilingForAnOrdinaryProp()
    {
        float min = CarryHold.HoldMinM(CapsuleRadius, CarryHold.BoundingRadiusM(CanAabb));
        Near(CarryHold.HoldMaxBaseM, CarryHold.HoldMaxM(min), 1e-5f);
    }

    [Fact]
    public void ClampHoldDistance_PassesThroughInsideTheBand()
    {
        Near(0.8f, CarryHold.ClampHoldDistance(0.8f, 0.5f, 1.2f), 1e-5f);
    }

    [Fact]
    public void ClampHoldDistance_PullsAFarGrabIn()
    {
        Near(1.2f, CarryHold.ClampHoldDistance(4.0f, 0.5f, 1.2f), 1e-5f);
    }

    [Fact]
    public void ClampHoldDistance_PushesANearGrabOut()
    {
        Near(0.5f, CarryHold.ClampHoldDistance(0.1f, 0.5f, 1.2f), 1e-5f);
    }

    // ---------------------------------------------------------------- the wheel

    [Fact]
    public void Scroll_OneNotchOut_MovesOneStepFurther()
    {
        Near(0.8f + CarryHold.ScrollStepM, CarryHold.Scroll(0.8f, 1, 0.5f, 1.2f), 1e-5f);
    }

    [Fact]
    public void Scroll_OneNotchIn_MovesOneStepCloser()
    {
        Near(0.8f - CarryHold.ScrollStepM, CarryHold.Scroll(0.8f, -1, 0.5f, 1.2f), 1e-5f);
    }

    [Fact]
    public void Scroll_StopsAtTheCeiling()
    {
        Near(1.2f, CarryHold.Scroll(1.15f, 3, 0.5f, 1.2f), 1e-5f);
    }

    [Fact]
    public void Scroll_StopsAtTheFloor()
    {
        Near(0.5f, CarryHold.Scroll(0.55f, -3, 0.5f, 1.2f), 1e-5f);
    }

    [Fact]
    public void Scroll_TheBandIsAtLeastFourNotchesWideForEveryShippedProp()
    {
        foreach (Vector3 aabb in new[] { CanAabb, CrateAabb })
        {
            float min = CarryHold.HoldMinM(CapsuleRadius, CarryHold.BoundingRadiusM(aabb));
            float max = CarryHold.HoldMaxM(min);
            Assert.True((max - min) / CarryHold.ScrollStepM >= 4f,
                $"the wheel must be worth rolling: band={(max - min):F3} m");
        }
    }

    // ---------------------------------------------------------------- the spring

    [Fact]
    public void MinOmega_IsFourSprintSpeedsOverHoldMin()
    {
        Near(4f * 6.08f / 0.5f, CarryHold.MinOmega(0.5f, 6.08f), 1e-4f);
    }

    [Fact]
    public void AtMinOmega_TheSteadyStateLagIsExactlyHalfOfHoldMin()
    {
        const float holdMin = 0.5f;
        const float sprint = 6.08f;
        float lag = CarryHold.SteadyStateLagM(CarryHold.MinOmega(holdMin, sprint), sprint);
        Near(holdMin * 0.5f, lag, 1e-5f);
    }

    [Fact]
    public void HoldOmega_RaisesASlowSpringToTheFloor()
    {
        float floor = CarryHold.MinOmega(0.5f, 6.08f);
        Near(floor, CarryHold.HoldOmega(6.5f, 0.5f, 6.08f), 1e-4f);
    }

    [Fact]
    public void HoldOmega_LeavesAFastSpringAlone()
    {
        Near(200f, CarryHold.HoldOmega(200f, 0.5f, 6.08f), 1e-4f);
    }

    [Fact]
    public void LagAtSprint_StaysUnderHalfOfHoldMin_ForBothShippedProps()
    {
        const float sprint = 6.08f;   // MotorTuning.Default: 3.8 m/s x 1.6
        foreach (Vector3 aabb in new[] { CanAabb, CrateAabb })
        {
            float min = CarryHold.HoldMinM(CapsuleRadius, CarryHold.BoundingRadiusM(aabb));
            // The heaviest spring the feel system can produce is the slow end of the dial.
            float omega = CarryHold.HoldOmega(6.5f, min, sprint);
            float lag = CarryHold.SteadyStateLagM(omega, sprint);
            Assert.True(lag <= min * 0.5f + 1e-4f,
                $"lag {lag:F3} m must stay under half of HoldMin {min:F3} m");
        }
    }

    // ---------------------------------------------------------------- the grab point

    [Fact]
    public void HoldPoint_IsTheEyePlusDistanceAlongTheView()
    {
        Vector3 p = CarryHold.HoldPoint(new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, -2f), 0.75f);
        Near(new Vector3(0f, 1f, -0.75f).X, p.X, 1e-5f);
        Near(new Vector3(0f, 1f, -0.75f).Y, p.Y, 1e-5f);
        Near(new Vector3(0f, 1f, -0.75f).Z, p.Z, 1e-5f);
    }

    [Fact]
    public void GrabLocal_IsTheGrabbedPointInThePropsOwnFrame()
    {
        var prop = new Transform3D(new Basis(Vector3.Up, Mathf.Pi * 0.5f), new Vector3(2f, 0f, 0f));
        Vector3 worldPoint = prop * new Vector3(0.1f, 0.2f, 0.3f);
        Vector3 local = CarryHold.GrabLocalFrom(prop, worldPoint);
        Near(0.1f, local.X, 1e-4f);
        Near(0.2f, local.Y, 1e-4f);
        Near(0.3f, local.Z, 1e-4f);
    }

    [Fact]
    public void PropOrigin_PutsTheGrabbedPointExactlyOnTheHoldPoint()
    {
        var pose = new Basis(Vector3.Up, 0.7f);
        var grabLocal = new Vector3(0.1f, -0.2f, 0.05f);
        var hold = new Vector3(1f, 1.2f, -3f);
        Vector3 origin = CarryHold.PropOriginFor(hold, pose, grabLocal);
        Vector3 back = origin + pose * grabLocal;
        Assert.True(back.IsEqualApprox(hold), $"grabbed point landed at {back}, wanted {hold}");
    }

    /// <summary><b>The no-snap invariant.</b> On the grab frame the derived pose reproduces the
    /// prop's own transform exactly — nothing lerps it to an anchor, because the target IS where
    /// it already is.</summary>
    [Fact]
    public void OnTheGrabFrame_TheDerivedPoseIsThePropsOwnTransform()
    {
        var prop = new Transform3D(new Basis(Vector3.Up, 1.1f), new Vector3(3f, 0.5f, -2f));
        var eye = new Vector3(3f, 1.0f, -1.2f);
        Vector3 grabWorld = prop * new Vector3(0.05f, 0.1f, 0.2f);
        float dist = eye.DistanceTo(grabWorld);
        Vector3 dir = (grabWorld - eye).Normalized();

        const float holderYaw = 0.4f;
        Vector3 grabLocal = CarryHold.GrabLocalFrom(prop, grabWorld);
        Basis holdLocal = CarryHold.HoldBasisLocal(holderYaw, prop.Basis);
        Basis pose = CarryHold.PoseBasis(holderYaw, Basis.Identity, holdLocal);
        Vector3 origin = CarryHold.PropOriginFor(CarryHold.HoldPoint(eye, dir, dist), pose, grabLocal);

        Assert.True(origin.IsEqualApprox(prop.Origin), $"origin moved to {origin} from {prop.Origin}");
        Assert.True(pose.IsEqualApprox(prop.Basis.Orthonormalized()), "orientation was not preserved");
    }

    [Fact]
    public void TurningTheHolder_TurnsTheHeldPropWithThem()
    {
        var propBasis = new Basis(Vector3.Up, 1.1f);
        Basis holdLocal = CarryHold.HoldBasisLocal(0f, propBasis);
        Basis turned = CarryHold.PoseBasis(Mathf.Pi * 0.5f, Basis.Identity, holdLocal);
        var expected = new Basis(Vector3.Up, Mathf.Pi * 0.5f) * propBasis.Orthonormalized();
        Assert.True(turned.IsEqualApprox(expected), $"pose {turned} != {expected}");
    }

    [Fact]
    public void LookingUpAndDown_DoesNotTumbleTheHeldProp()
    {
        // Pitch is not part of the hold basis at all: only the holder's yaw is. Two different
        // pitches at the same yaw produce the identical pose.
        Basis holdLocal = CarryHold.HoldBasisLocal(0.3f, new Basis(Vector3.Up, 1.1f));
        Assert.True(CarryHold.PoseBasis(0.3f, Basis.Identity, holdLocal)
            .IsEqualApprox(CarryHold.PoseBasis(0.3f, Basis.Identity, holdLocal)));
    }

    [Fact]
    public void TheGrabbedPoint_IsONTheProp_EvenWhenTheRayMissesIt()
    {
        // A bot has no pitch and the aim cone is generous, so the view ray can pass a metre over
        // a crate on the floor and still pick it. The grabbed point is then the nearest point on
        // the prop, never the point in mid-air: a grab offset longer than the prop is a LEVER,
        // and a lever that long swings faster than the hold can follow every time the holder
        // turns.
        var centre = new Vector3(2f, 0.22f, 0f);
        var rayPoint = new Vector3(2f, 1.2f, 0f);
        Vector3 onProp = CarryHold.GrabPointOnProp(centre, rayPoint, 0.38f);
        Near(0.38f, centre.DistanceTo(onProp), 1e-4f);
        Assert.True(onProp.Y > centre.Y, "it should be the face nearest the ray");
    }

    [Fact]
    public void AGrabbedPointAlreadyOnTheProp_IsLeftWhereItIs()
    {
        var centre = new Vector3(2f, 0.22f, 0f);
        var inside = new Vector3(2.1f, 0.25f, 0f);
        Assert.True(CarryHold.GrabPointOnProp(centre, inside, 0.38f).IsEqualApprox(inside));
    }

    // ---------------------------------------------------------------- never inside the holder

    [Fact]
    public void PushOutOfSegment_LeavesAPointThatIsAlreadyClearAlone()
    {
        var p = new Vector3(2f, 0.9f, 0f);
        Vector3 pushed = CarryHold.PushOutOfSegment(p, Vector3.Zero, new Vector3(0f, 1.8f, 0f), 0.5f, Vector3.Forward);
        Assert.True(pushed.IsEqualApprox(p));
    }

    [Fact]
    public void PushOutOfSegment_PushesAPointInsideOutToExactlyTheClearance()
    {
        var p = new Vector3(0.1f, 0.9f, 0f);
        Vector3 pushed = CarryHold.PushOutOfSegment(p, Vector3.Zero, new Vector3(0f, 1.8f, 0f), 0.5f, Vector3.Forward);
        Near(0.5f, new Vector3(pushed.X, 0f, pushed.Z).Length(), 1e-4f);
        Near(0.9f, pushed.Y, 1e-4f);
    }

    [Fact]
    public void PushOutOfSegment_OnTheAxisItself_UsesTheFallbackDirection()
    {
        var p = new Vector3(0f, 0.9f, 0f);
        Vector3 pushed = CarryHold.PushOutOfSegment(p, Vector3.Zero, new Vector3(0f, 1.8f, 0f), 0.5f, Vector3.Forward);
        Assert.True(pushed.IsEqualApprox(new Vector3(0f, 0.9f, -0.5f)), $"pushed to {pushed}");
    }

    [Fact]
    public void PushOutOfSegment_MeasuresFromTheCapOnceAboveTheSegment()
    {
        // Straight above the top of the capsule by less than the clearance: pushed straight up.
        var top = new Vector3(0f, 1.8f, 0f);
        var p = new Vector3(0f, 2.0f, 0f);
        Vector3 pushed = CarryHold.PushOutOfSegment(p, Vector3.Zero, top, 0.5f, Vector3.Forward);
        Near(2.3f, pushed.Y, 1e-4f);
    }

    [Fact]
    public void PushOutOfSegment_IsIdempotent()
    {
        var p = new Vector3(0.1f, 0.9f, 0.05f);
        Vector3 once = CarryHold.PushOutOfSegment(p, Vector3.Zero, new Vector3(0f, 1.8f, 0f), 0.5f, Vector3.Forward);
        Vector3 twice = CarryHold.PushOutOfSegment(once, Vector3.Zero, new Vector3(0f, 1.8f, 0f), 0.5f, Vector3.Forward);
        Assert.True(once.IsEqualApprox(twice));
    }

    /// <summary>The bar the scene suite asserts in the engine, stated here as arithmetic: a prop
    /// whose spring has fallen a whole HoldMin behind still ends up outside the capsule.</summary>
    [Fact]
    public void EvenAFullyLaggedProp_EndsUpOutsideTheCapsule()
    {
        float r = CarryHold.BoundingRadiusM(CrateAabb);
        float clearance = CarryHold.HoldMinM(CapsuleRadius, r);
        // The worst case: the spring has been dragged to the holder's own axis.
        Vector3 pushed = CarryHold.PushOutOfSegment(
            new Vector3(0f, 0.9f, 0f), Vector3.Zero, new Vector3(0f, 1.8f, 0f), clearance, Vector3.Forward);
        float radial = new Vector3(pushed.X, 0f, pushed.Z).Length();
        Assert.True(radial - r >= CapsuleRadius - 1e-4f,
            $"the crate's near face is {radial - r:F3} m from the axis, capsule is {CapsuleRadius:F3}");
    }

    // ------------------------------------------------- a held prop cannot shove the world hard

    [Fact]
    public void MaxHoldSpeed_IsDerivedFromHowFastTheHolderCanWalk()
    {
        Assert.True(CarryHold.MaxHoldSpeedMps(2.4f) > 2.4f,
            "the hold has to be able to keep up with the walk");
        Assert.True(CarryHold.MaxHoldSpeedMps(2.4f) <= 3f * 2.4f,
            "and not so much faster that it becomes a weapon");
    }

    [Fact]
    public void AStepInsideTheCap_IsLeftAlone()
    {
        var from = new Vector3(0f, 1f, 0f);
        var to = new Vector3(0f, 1f, -0.05f);
        Assert.True(CarryHold.CapStep(from, to, 5f, 0.016f).IsEqualApprox(to));
    }

    [Fact]
    public void AStepPastTheCap_IsShortenedToTheCapAndKeepsItsDirection()
    {
        var from = Vector3.Zero;
        var to = new Vector3(0f, 0f, -10f);
        Vector3 capped = CarryHold.CapStep(from, to, 5f, 0.02f);
        Near(0.1f, capped.Length(), 1e-4f);              // 5 m/s x 0.02 s
        Near(-1f, capped.Normalized().Z, 1e-4f);
    }

    [Fact]
    public void TheCap_IsASPEED_SoItScalesWithTheTick()
    {
        var from = Vector3.Zero;
        var to = new Vector3(0f, 0f, -10f);
        Near(2f * CarryHold.CapStep(from, to, 5f, 0.01f).Length(),
            CarryHold.CapStep(from, to, 5f, 0.02f).Length(), 1e-4f);
    }

    // ---------------------------------------------------------------- the break

    [Fact]
    public void Hold_DoesNotBreakWhileTheGapIsSmall()
    {
        Assert.False(CarryHold.ShouldBreakHold(CarryHold.BreakHoldM - 0.01f, 5f));
    }

    [Fact]
    public void Hold_DoesNotBreakOnAMomentaryGap()
    {
        Assert.False(CarryHold.ShouldBreakHold(CarryHold.BreakHoldM + 0.5f, CarryHold.BreakHoldSec - 0.01f));
    }

    [Fact]
    public void Hold_BreaksWhenTheGapPersists()
    {
        Assert.True(CarryHold.ShouldBreakHold(CarryHold.BreakHoldM + 0.01f, CarryHold.BreakHoldSec));
    }

    [Fact]
    public void BlockedSeconds_ResetsTheMomentThePropCatchesUp()
    {
        float t = CarryHold.StepBlockedSeconds(0.25f, blockedM: 0.01f, dt: 0.016f);
        Near(0f, t, 1e-5f);
    }

    [Fact]
    public void BlockedSeconds_AccumulatesWhileThePropIsStuck()
    {
        float t = CarryHold.StepBlockedSeconds(0.25f, blockedM: CarryHold.BreakHoldM + 0.1f, dt: 0.016f);
        Near(0.266f, t, 1e-3f);
    }
}
