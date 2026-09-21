using Godot;
using MpFoundation.Game.Sandbox.Feel;
using MpFoundation.Game.Sandbox.Hands;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>HANDS-1's maths</b>: where a hand goes when you grab something, how many hands the thing
/// needs, and how long the reach takes. Everything here is the engine-free half of
/// <c>FirstPersonHands</c> — the node itself only reads live transforms and hands them to these
/// statics, exactly as <c>NetworkedProp</c> does with <see cref="CarryHold"/>.
/// </summary>
public class HandReachTests
{
    /// <summary>Float comparison with an explicit tolerance. xunit 2.4.2's three-argument
    /// Assert.Equal is ambiguous between (double,double,int) and (float,float,float) for a float
    /// pair, which is why every suite in this directory carries one of these.</summary>
    private static void Near(float expected, float actual, float eps) =>
        Assert.True(Mathf.Abs(expected - actual) <= eps,
            $"expected {expected} +/- {eps}, got {actual}");

    // The shipped props, as their .tscn files author them. Every size here is read off the scene
    // file rather than typed from memory: the two-hand rule is a claim about THESE objects.
    private const float CanLongestM = 0.12f;          // Can.tscn: cylinder r 0.035, h 0.12
    private const float CanMassKg = 0.35f;
    private const float ProduceLongestM = 0.16f;      // Produce.tscn: sphere r 0.08
    private const float ProduceMassKg = 0.25f;
    private const float CerealLongestM = 0.28f;       // CerealBox.tscn: 0.19 x 0.28 x 0.06
    private const float CerealMassKg = 0.4f;
    private const float CrateLongestM = 0.44f;        // Crate.tscn: 0.44 cube
    private const float CrateMassKg = 1.0f;           // no `mass` line -> RigidBody3D's default

    // ---------------------------------------------------------------- the arm is FEEL-1's clamp

    [Fact]
    public void TheArmLengthIsFeel1sHoldCeiling_NotASecondNumber()
    {
        // Packet item 4, in one assertion: "HoldMax is the arm length ... read FEEL-1's constant;
        // do not duplicate it." If someone types a literal into HandReach this goes red.
        Near(CarryHold.HoldMaxBaseM, HandReach.ArmLengthM, 0f);
    }

    [Fact]
    public void AHandIsNeverDrawnFurtherFromTheEyeThanTheArmReaches()
    {
        var eye = new Vector3(1f, 1f, 1f);
        Vector3 far = eye + new Vector3(0f, 0f, -5f);
        Vector3 clamped = HandReach.ClampToArm(eye, far, HandReach.ArmLengthM);
        Near(HandReach.ArmLengthM, eye.DistanceTo(clamped), 5e-5f);
        // ...and the direction is kept, so a clamped hand is still pointing at the thing.
        Assert.True((clamped - eye).Normalized().Dot((far - eye).Normalized()) > 0.999f);
    }

    [Fact]
    public void AHandONAHeldThingGetsThatThingsOwnRadiusAsAnAllowance()
    {
        // A crate held at arm's length has its side faces 0.22 m out to either side, and a flat
        // radial clamp at the arm would drag both hands of a two-handed grip inward along the
        // view ray every frame -- a pose distortion, not a guarantee. The allowance is exactly
        // the bulk that can separate the grab point from the surface the hand is on.
        Near(HandReach.ArmLengthM + 0.381f, HandReach.HoldReachM(0.381f), 5e-5f);
        Near(HandReach.ArmLengthM, HandReach.HoldReachM(0f), 5e-5f);
        // A nonsense radius cannot SHRINK the reach below the arm.
        Near(HandReach.ArmLengthM, HandReach.HoldReachM(-5f), 5e-5f);
    }

    [Fact]
    public void APointInsideTheArmIsLeftExactlyWhereItIs()
    {
        var eye = Vector3.Zero;
        var near = new Vector3(0f, 0f, -0.4f);
        Near(0f, near.DistanceTo(HandReach.ClampToArm(eye, near, HandReach.ArmLengthM)), 1e-6f);
    }

    // ------------------------------------------------------------------ one hand or two

    [Theory]
    [InlineData(CanLongestM, CanMassKg)]
    [InlineData(ProduceLongestM, ProduceMassKg)]
    public void CansAndProduceAreOneHanded(float longest, float mass) =>
        Assert.False(HandReach.NeedsTwoHands(longest, mass));

    [Theory]
    [InlineData(CerealLongestM, CerealMassKg)]
    [InlineData(CrateLongestM, CrateMassKg)]
    public void CratesAndCerealBoxesAreTwoHanded(float longest, float mass) =>
        Assert.True(HandReach.NeedsTwoHands(longest, mass));

    [Fact]
    public void TheSpanArmIsWhatSeparatesTheShippedProps_AndItSitsBetweenProduceAndCereal()
    {
        // The threshold has to fall in the gap the shipped props leave, or it is a number that
        // happens to work rather than one that is derived. Produce is the largest one-hander and
        // the cereal box the smallest two-hander.
        Assert.InRange(HandReach.OneHandSpanM, ProduceLongestM + 0.001f, CerealLongestM - 0.001f);
    }

    [Fact]
    public void AHeavySmallThingStillTakesTwoHands()
    {
        // The mass arm, tested on its own: nothing shipped today trips it (see the handoff), so
        // this is the only place it is exercised until Talon's real meshes arrive with real
        // masses. A tin of paint is a can-sized object nobody one-hands.
        Assert.True(HandReach.NeedsTwoHands(CanLongestM, HandReach.OneHandMassKg + 0.5f));
        Assert.False(HandReach.NeedsTwoHands(CanLongestM, HandReach.OneHandMassKg - 0.5f));
    }

    // ------------------------------------------------------- the hand sits ON the thing

    [Fact]
    public void TheGrabPointIsPushedOutToTheSurfaceOnTheSideFacingThePlayer()
    {
        // FEEL-1's grab point is the foot of the perpendicular from the prop's centre to the view
        // ray, so for any well-aimed grab it is INSIDE the prop -- at the centre of a crate, in
        // the worst case. A hand drawn there is invisible. The hand therefore goes to the point
        // where the grab point's own line of sight leaves the prop.
        var centre = new Vector3(0f, 1f, -1f);
        const float r = 0.381f;                       // the crate's bounding radius
        var towardEye = new Vector3(0f, 0f, 1f);      // the player is at +Z of the crate
        Vector3 hand = HandReach.SurfacePoint(centre, centre, r, towardEye);

        Near(r, centre.DistanceTo(hand), 5e-4f);          // on the surface
        Assert.True(hand.Z > centre.Z);                        // on the near side
    }

    [Fact]
    public void TheHandKeepsTheGrabPointsSidewaysOffset_SoAnOffCentreGrabReadsAsOffCentre()
    {
        var centre = new Vector3(0f, 1f, -1f);
        const float r = 0.381f;
        var towardEye = new Vector3(0f, 0f, 1f);
        Vector3 grab = centre + new Vector3(0.15f, 0.05f, 0f);   // gripped up and to the right

        Vector3 hand = HandReach.SurfacePoint(centre, grab, r, towardEye);

        // The component of the hand across the line of sight is the grab point's, to the
        // millimetre -- that IS "the hand is at the grab point", projected onto the object.
        Near(0f, HandReach.LateralErrorM(hand, grab, towardEye), 5e-5f);
        Near(r, centre.DistanceTo(hand), 5e-4f);
    }

    [Fact]
    public void AGrabPointBeyondTheSurfaceIsBroughtBackOntoIt_NoImaginaryDepth()
    {
        // A grab that clipped to the bounding sphere lands with its lateral offset equal to the
        // radius; without the pull-in the depth term would be sqrt(negative).
        var centre = Vector3.Zero;
        const float r = 0.1f;
        var towardEye = Vector3.Back;
        Vector3 grab = centre + new Vector3(5f, 0f, 0f);

        Vector3 hand = HandReach.SurfacePoint(centre, grab, r, towardEye);

        Assert.True(hand.IsFinite());
        Near(r, centre.DistanceTo(hand), 5e-4f);
    }

    [Fact]
    public void TheHandRidesTheProp_TheSameLocalPointBeforeAndAfterARotation()
    {
        // The whole reason the grab point lives in the prop's own frame: spin the object with RMB
        // and your hand travels with the face it is holding.
        var centre = new Vector3(0f, 1f, -1f);
        const float r = 0.381f;
        var towardEye = Vector3.Back;
        var propAtGrab = new Transform3D(Basis.Identity, centre);
        Vector3 grabLocal = new(0.1f, 0.05f, 0.2f);

        Vector3 handAtGrab = HandReach.SurfacePoint(centre, propAtGrab * grabLocal, r, towardEye);
        Vector3 handLocal = propAtGrab.AffineInverse() * handAtGrab;

        var spun = new Transform3D(new Basis(Vector3.Up, Mathf.Pi * 0.5f), centre);
        Vector3 handAfter = spun * handLocal;

        // Same point of the object, so the same distance from its centre and a different place in
        // the world -- a hand welded to the screen would fail the second half.
        Near(centre.DistanceTo(handAtGrab), centre.DistanceTo(handAfter), 5e-5f);
        Assert.True(handAtGrab.DistanceTo(handAfter) > 0.05f);
    }

    // ----------------------------------------------------------------- two hands, straddling

    [Fact]
    public void TwoHandsTakeOppositeFacesAndTheirMidpointIsTheGrabPoint()
    {
        var centre = new Vector3(0f, 1f, -1f);
        var towardEye = Vector3.Back;
        Vector3 grab = centre + new Vector3(0f, 0.06f, 0f);
        const float halfSpan = 0.22f;                  // the crate's half-width

        (Vector3 a, Vector3 b) = HandReach.StraddlePoints(
            centre, grab, towardEye, Vector3.Up, halfSpan, proudM: 0f);

        Near(halfSpan * 2f, a.DistanceTo(b), 5e-4f);
        Vector3 mid = (a + b) * 0.5f;
        // The pair is centred on the grab point across the line of sight: you are holding the
        // crate where you grabbed it, with a hand either side.
        Near(0f, HandReach.LateralErrorM(mid, grab, towardEye), 5e-5f);
        // ...and at the grab point's height, not at the crate's mid-height.
        Near(grab.Y, a.Y, 5e-5f);
        Near(grab.Y, b.Y, 5e-5f);
    }

    [Fact]
    public void TheTwoHandsAreProudOfTheFaceTheyHold_ByTheAmountAsked()
    {
        var centre = Vector3.Zero;
        (Vector3 a, Vector3 b) = HandReach.StraddlePoints(
            centre, centre, Vector3.Back, Vector3.Up, halfSpanM: 0.22f, proudM: 0.015f);
        Near(0.22f + 0.015f, Mathf.Abs(a.X), 5e-5f);
        Near(0.22f + 0.015f, Mathf.Abs(b.X), 5e-5f);
        Assert.True(a.X * b.X < 0f);   // one each side
    }

    [Fact]
    public void TheStraddleAxisIsAcrossTheVIEW_SoTurningRoundSwapsWhichFaceIsWhich()
    {
        var centre = Vector3.Zero;
        (Vector3 fromFront, _) = HandReach.StraddlePoints(
            centre, centre, Vector3.Back, Vector3.Up, 0.22f, 0f);
        (Vector3 fromLeft, _) = HandReach.StraddlePoints(
            centre, centre, Vector3.Left, Vector3.Up, 0.22f, 0f);
        // Looking at the crate from the front the hands are on its X faces; from the side, on its
        // Z faces. A hand pair fixed to the prop's own axes would put a hand behind it.
        Assert.True(Mathf.Abs(fromFront.X) > 0.2f);
        Assert.True(Mathf.Abs(fromLeft.Z) > 0.2f);
    }

    // --------------------------------------------------------- the box's own half-width

    [Fact]
    public void TheHalfSpanOfAnAxisAlignedCrateIsItsHalfWidth()
    {
        var half = new Vector3(0.22f, 0.22f, 0.22f);
        Near(0.22f, HandReach.SupportHalfExtent(Basis.Identity, half, Vector3.Right), 5e-5f);
    }

    [Fact]
    public void ACrateTurnedFortyFiveDegreesIsWiderAcrossTheView_WhichIsWhatTheHandsMustFollow()
    {
        var half = new Vector3(0.22f, 0.22f, 0.22f);
        var spun = new Basis(Vector3.Up, Mathf.Pi * 0.25f);
        float wide = HandReach.SupportHalfExtent(spun, half, Vector3.Right);
        Near(0.22f * Mathf.Sqrt2, wide, 5e-4f);
    }

    [Fact]
    public void ACerealBoxHeldEdgeOnIsThinAcrossTheViewAndTheHandsComeIn()
    {
        var half = new Vector3(0.095f, 0.14f, 0.03f);   // CerealBox.tscn / 2
        // Turned so its thin face points at the player: the hands take the 0.19 m faces.
        var edgeOn = new Basis(Vector3.Up, Mathf.Pi * 0.5f);
        Near(0.03f, HandReach.SupportHalfExtent(edgeOn, half, Vector3.Right), 5e-4f);
    }

    // ------------------------------------------------------------------------ the timing

    [Fact]
    public void TheReachIsOneHundredAndTwentyMillisecondsAndTheReturnIsOneFifty()
    {
        // The packet's numbers, pinned so a "feels better" edit has to be a deliberate one.
        Near(0.120f, HandReach.ReachSec, 5e-5f);
        Near(0.150f, HandReach.ReturnSec, 5e-5f);
    }

    [Theory]
    [InlineData(0.0, 0f)]
    [InlineData(0.060, 0.5f)]
    [InlineData(0.120, 1f)]
    [InlineData(0.500, 1f)]
    public void ProgressIsLinearAndSaturates(double elapsed, float expected) =>
        Near(expected, HandReach.Progress(elapsed, HandReach.ReachSec), 5e-4f);

    [Fact]
    public void AZeroLengthReachIsInstant_NeverADivideByZero()
    {
        Near(1f, HandReach.Progress(0.0, 0f), 5e-5f);
    }

    [Fact]
    public void TheLerpIsStraight_NoOvershootAndNoEasing()
    {
        // "a straight lerp, no animation clips" (program S2.2). An eased curve would pass the
        // endpoints and fail the middle.
        var from = new Vector3(0f, 0f, 0f);
        var to = new Vector3(1f, 2f, -3f);
        Near(0f, new Vector3(0.5f, 1f, -1.5f).DistanceTo(HandReach.Lerp(from, to, 0.5f)), 5e-6f);
        Near(0f, new Vector3(0.25f, 0.5f, -0.75f).DistanceTo(HandReach.Lerp(from, to, 0.25f)), 5e-6f);
    }

    // ------------------------------------------------------------------- the hand's angle

    [Fact]
    public void TheHandsPalmFacesTheWayItIsTold_AndTheBasisStaysRightHanded()
    {
        var palm = new Vector3(0f, 0f, 1f);
        var fingers = new Vector3(0f, 1f, 0f);
        Basis b = HandReach.Orient(palm, fingers);

        Near(0f, palm.DistanceTo(b.Y), 5e-4f);
        Near(0f, (-fingers).DistanceTo(b.Z), 5e-4f);
        Assert.True(b.Determinant() > 0.99f);
    }

    [Fact]
    public void AFingerDirectionParallelToThePalmDoesNotProduceGarbage()
    {
        // The degenerate case: the straddle can ask for fingers along the palm normal when the
        // player looks straight down a face. A NaN basis here is an invisible hand.
        Basis b = HandReach.Orient(Vector3.Up, Vector3.Up);
        Assert.True(b.X.IsFinite() && b.Y.IsFinite() && b.Z.IsFinite());
        Assert.True(b.Determinant() > 0.99f);
    }

    // --------------------------------------------------------------- the suite's own instrument

    [Fact]
    public void LateralErrorIgnoresDepth_SoAHandOnTheNearFaceIsStillAtTheGrabPoint()
    {
        var towardEye = Vector3.Back;
        var grab = new Vector3(1f, 2f, 3f);
        Vector3 nearer = grab + new Vector3(0f, 0f, 0.4f);
        Near(0f, HandReach.LateralErrorM(nearer, grab, towardEye), 5e-6f);
        Near(0.3f, HandReach.LateralErrorM(grab + new Vector3(0.3f, 0f, 9f), grab, towardEye), 5e-5f);
    }

    [Fact]
    public void APlantedSidewaysOffsetIsExactlyWhatLateralErrorReports()
    {
        // The positive control the smoke plants (--hands-plant-offset): the instrument has to
        // report it, or a red build would look green.
        var towardEye = Vector3.Back;
        var grab = new Vector3(0f, 1f, -1f);
        Vector3 planted = grab + Vector3.Right * 0.08f;
        Near(0.08f, HandReach.LateralErrorM(planted, grab, towardEye), 5e-5f);
    }
}
