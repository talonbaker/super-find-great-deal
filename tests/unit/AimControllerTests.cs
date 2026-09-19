using MpFoundation.Game.Aim;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// Pure-logic coverage for the shared aim substrate's stance machine and steadiness ramp
/// (WP-L3, Issue #106). Godot-free — AimController touches nothing but System.Math — so this
/// runs in the same fast, engine-free tier as NetCodec's tests. Boundary cases here are the
/// "logic before feel-testing" half of the story; AimQuery's frustum/raycast boundary cases
/// need a live physics world and live in SandboxSelfTest instead (see Run-SandboxTest.ps1).
/// </summary>
public class AimControllerTests
{
    private const float Tick = 1f / 60f; // matches AvatarMotor.TickDelta's order of magnitude

    // --- Stance: committed transitions -------------------------------------------------

    [Fact]
    public void StartsLowered()
    {
        var a = new AimController();
        Assert.Equal(AimStance.Lowered, a.Stance);
        Assert.Equal(1f, a.SpeedFactor);
    }

    [Fact]
    public void WantRaised_EntersRaising_NotImmediatelyRaised()
    {
        var a = new AimController();
        a.Step(Tick, wantRaised: true, horizontalSpeed: 0f);
        Assert.Equal(AimStance.Raising, a.Stance);
    }

    [Fact]
    public void HoldingRaised_ForFullDuration_ReachesRaised()
    {
        var a = new AimController();
        RaiseFully(a);
        Assert.Equal(AimStance.Raised, a.Stance);
    }

    [Fact]
    public void HugeDt_CompletesTransitionInOneStep()
    {
        // Self-test-only shape (see class doc): a single Step whose dt already exceeds
        // RaiseDurationSec lands fully Raised, not stuck mid-transition.
        var a = new AimController();
        a.Step(AimController.RaiseDurationSec + 1f, wantRaised: true, horizontalSpeed: 0f);
        Assert.Equal(AimStance.Raised, a.Stance);
    }

    [Fact]
    public void ReleasingMidRaise_DoesNotReverse_TransitionIsCommitted()
    {
        var a = new AimController();
        a.Step(Tick, wantRaised: true, horizontalSpeed: 0f); // enters Raising
        Assert.Equal(AimStance.Raising, a.Stance);
        // Release immediately — a committed transition ignores this until it completes.
        a.Step(Tick, wantRaised: false, horizontalSpeed: 0f);
        Assert.Equal(AimStance.Raising, a.Stance);
    }

    [Fact]
    public void CommittedRaise_ThenReleaseAfterCompletion_Lowers()
    {
        var a = new AimController();
        Drive(a, AimController.RaiseDurationSec + Tick, wantRaised: true);
        Assert.Equal(AimStance.Raised, a.Stance);
        Drive(a, AimController.RaiseDurationSec + Tick, wantRaised: false);
        Assert.Equal(AimStance.Lowered, a.Stance);
    }

    [Fact]
    public void Raised_And_Raising_BothCostSpeedFactor()
    {
        var a = new AimController();
        a.Step(Tick, wantRaised: true, horizontalSpeed: 0f); // Raising
        Assert.Equal(AimController.RaisedSpeedFactor, a.SpeedFactor);
        Drive(a, AimController.RaiseDurationSec, wantRaised: true); // -> Raised
        Assert.Equal(AimController.RaisedSpeedFactor, a.SpeedFactor);
    }

    [Fact]
    public void NonFiniteOrNegativeDt_IsANoOp()
    {
        var a = new AimController();
        a.Step(float.NaN, wantRaised: true, horizontalSpeed: 0f);
        Assert.Equal(AimStance.Lowered, a.Stance);
        a.Step(-0.1f, wantRaised: true, horizontalSpeed: 0f);
        Assert.Equal(AimStance.Lowered, a.Stance);
        a.Step(float.PositiveInfinity, wantRaised: true, horizontalSpeed: 0f);
        Assert.Equal(AimStance.Lowered, a.Stance); // Infinity is not finite -> no-op too
    }

    // --- Steadiness ----------------------------------------------------------------------

    [Fact]
    public void Steadiness_IsZero_WhileNotRaised()
    {
        var a = new AimController();
        Assert.Equal(0f, a.Steadiness01);
        a.Step(Tick, wantRaised: true, horizontalSpeed: 0f); // Raising, not Raised yet
        Assert.Equal(0f, a.Steadiness01);
    }

    // Steadiness fixtures below start from AdoptAuthoritative(Raised, 0f) — RaiseFully's own
    // Step-driven path is exercised separately (Stance tests above) and is deliberately NOT
    // reused here: the very tick a raise transition completes, that SAME Step call's
    // AdvanceSteadiness already runs with Stance==Raised (see class doc — Stance advances
    // before Steadiness within one Step), so a Step-driven raise always lands with a sliver of
    // steadiness already accrued (about one tick's worth). That's correct product behaviour,
    // but it would make these boundary assertions imprecise for the wrong reason. Adopt gives
    // an exact, independently-tested (see AdoptAuthoritative_* below) zero-steadiness start.

    [Fact]
    public void Steadiness_RampsToOne_AfterFullSettleDuration_Stationary()
    {
        var a = new AimController();
        a.AdoptAuthoritative(AimStance.Raised, 0f);
        Drive(a, AimController.SteadySettleDurationSec + Tick, wantRaised: true, horizontalSpeed: 0f);
        Assert.Equal(1f, a.Steadiness01);
    }

    [Fact]
    public void Steadiness_Clamps_DoesNotOvershootPastOne()
    {
        var a = new AimController();
        a.AdoptAuthoritative(AimStance.Raised, 0f);
        Drive(a, AimController.SteadySettleDurationSec * 5f, wantRaised: true, horizontalSpeed: 0f);
        Assert.Equal(1f, a.Steadiness01);
        Assert.Equal(AimController.SteadySettleDurationSec, a.SteadyElapsedSec);
    }

    [Fact]
    public void Steadiness_ExactlyAtSettleDuration_IsFullySteady_BoundaryInclusive()
    {
        var a = new AimController();
        a.AdoptAuthoritative(AimStance.Raised, 0f);
        // A SINGLE step of exactly SteadySettleDurationSec so the boundary itself (elapsed ==
        // SteadySettleDurationSec, not a hair over) is what's actually asserted.
        a.Step(AimController.SteadySettleDurationSec, wantRaised: true, horizontalSpeed: 0f);
        Assert.Equal(1f, a.Steadiness01);
    }

    [Fact]
    public void Steadiness_JustUnderSettleDuration_IsNotYetFullySteady()
    {
        var a = new AimController();
        a.AdoptAuthoritative(AimStance.Raised, 0f);
        a.Step(AimController.SteadySettleDurationSec - 0.01f, wantRaised: true, horizontalSpeed: 0f);
        Assert.True(a.Steadiness01 < 1f);
        Assert.True(a.Steadiness01 > 0f);
    }

    [Fact]
    public void Steadiness_MovingAboveThreshold_DecaysInsteadOfBuilding()
    {
        var a = new AimController();
        a.AdoptAuthoritative(AimStance.Raised, 0f);
        Drive(a, AimController.SteadySettleDurationSec, wantRaised: true, horizontalSpeed: 0f);
        Assert.Equal(1f, a.Steadiness01);
        // Now start moving: steadiness must go DOWN, not continue up or hold.
        a.Step(Tick, wantRaised: true, horizontalSpeed: AimController.SteadyMoveSpeedThreshold + 1f);
        Assert.True(a.Steadiness01 < 1f);
    }

    [Fact]
    public void Steadiness_MovingFully_DecaysBackToZero()
    {
        var a = new AimController();
        a.AdoptAuthoritative(AimStance.Raised, 0f);
        Drive(a, AimController.SteadySettleDurationSec, wantRaised: true, horizontalSpeed: 0f);
        Drive(a, AimController.SteadyDecaySettleDurationSec + Tick, wantRaised: true,
            horizontalSpeed: AimController.SteadyMoveSpeedThreshold + 5f);
        Assert.Equal(0f, a.Steadiness01);
    }

    [Fact]
    public void Steadiness_ExactlyAtMoveThreshold_StillCountsAsStationary_BoundaryInclusive()
    {
        var a = new AimController();
        a.AdoptAuthoritative(AimStance.Raised, 0f);
        a.Step(Tick, wantRaised: true, horizontalSpeed: AimController.SteadyMoveSpeedThreshold);
        // At the threshold (not over it) steadiness must have BUILT, not decayed.
        Assert.True(a.Steadiness01 > 0f);
    }

    [Fact]
    public void Steadiness_JustOverMoveThreshold_CountsAsMoving()
    {
        var a = new AimController();
        a.AdoptAuthoritative(AimStance.Raised, AimController.SteadySettleDurationSec);
        Assert.Equal(1f, a.Steadiness01);
        a.Step(Tick, wantRaised: true, horizontalSpeed: AimController.SteadyMoveSpeedThreshold + 0.01f);
        Assert.True(a.Steadiness01 < 1f);
    }

    [Fact]
    public void Steadiness_ResetsToZero_OnLowering_NoCarryOverAcrossReRaise()
    {
        var a = new AimController();
        a.AdoptAuthoritative(AimStance.Raised, AimController.SteadySettleDurationSec);
        Assert.Equal(1f, a.Steadiness01);

        // Lower fully, then raise again: steadiness must start over, not resume from 1.
        Drive(a, AimController.RaiseDurationSec + Tick, wantRaised: false);
        Assert.Equal(AimStance.Lowered, a.Stance);
        a.Step(Tick, wantRaised: true, horizontalSpeed: 0f); // back into Raising
        Assert.Equal(0f, a.Steadiness01);
        Drive(a, AimController.RaiseDurationSec, wantRaised: true); // -> Raised again
        // Freshly Raised, not pre-steadied: nowhere near the 1.0 it held before lowering. Not
        // asserted as bit-exact 0 — the tick that completes the raise transition also runs one
        // AdvanceSteadiness pass while already Raised (see the fixture-choice comment above),
        // so a sliver may have accrued; the no-carry-over guarantee is "far below 1", not "0".
        Assert.True(a.Steadiness01 < 0.1f);
    }

    [Fact]
    public void Steadiness_NonFiniteSpeed_TreatedAsMoving_FailsSafe()
    {
        var a = new AimController();
        a.AdoptAuthoritative(AimStance.Raised, AimController.SteadySettleDurationSec);
        Assert.Equal(1f, a.Steadiness01);
        a.Step(Tick, wantRaised: true, horizontalSpeed: float.NaN);
        Assert.True(a.Steadiness01 < 1f); // NaN speed must not read as "stationary"
    }

    // --- AdoptAuthoritative ---------------------------------------------------------------

    [Fact]
    public void AdoptAuthoritative_CommittedStance_IsExact()
    {
        var a = new AimController();
        a.AdoptAuthoritative(AimStance.Raised, 0.5f);
        Assert.Equal(AimStance.Raised, a.Stance);
        Assert.Equal(0.5f, a.SteadyElapsedSec);
    }

    [Fact]
    public void AdoptAuthoritative_MidTransition_CollapsesToNearestCommittedEndpoint()
    {
        var a = new AimController();
        a.AdoptAuthoritative(AimStance.Raising, 0.3f);
        Assert.Equal(AimStance.Raised, a.Stance);

        var b = new AimController();
        b.AdoptAuthoritative(AimStance.Lowering, 0.3f);
        Assert.Equal(AimStance.Lowered, b.Stance);
    }

    [Fact]
    public void AdoptAuthoritative_NonRaisedStance_ForcesSteadyElapsedToZero()
    {
        var a = new AimController();
        // A caller handing Lowered a nonzero elapsed value is exactly the "don't trust the
        // caller" case AdoptAuthoritative's own doc comment calls out.
        a.AdoptAuthoritative(AimStance.Lowered, 0.9f);
        Assert.Equal(0f, a.SteadyElapsedSec);
        Assert.Equal(0f, a.Steadiness01);
    }

    [Fact]
    public void AdoptAuthoritative_ClampsOutOfRangeSteadyElapsed()
    {
        var a = new AimController();
        a.AdoptAuthoritative(AimStance.Raised, -5f);
        Assert.Equal(0f, a.SteadyElapsedSec);

        var b = new AimController();
        b.AdoptAuthoritative(AimStance.Raised, 999f);
        Assert.Equal(AimController.SteadySettleDurationSec, b.SteadyElapsedSec);
    }

    [Fact]
    public void AdoptAuthoritative_AfterMidRaise_ThenStep_ResumesCleanlyFromAdoptedState()
    {
        // Reconciliation replay shape: adopt, then replay buffered ticks through Step.
        var a = new AimController();
        a.AdoptAuthoritative(AimStance.Raised, 0.4f);
        a.Step(Tick, wantRaised: true, horizontalSpeed: 0f);
        Assert.Equal(AimStance.Raised, a.Stance);
        Assert.True(a.SteadyElapsedSec > 0.4f); // kept accumulating from the adopted point
    }

    // --- helpers ---------------------------------------------------------------------------

    private static void RaiseFully(AimController a) =>
        Drive(a, AimController.RaiseDurationSec + Tick, wantRaised: true, horizontalSpeed: 0f);

    private static void Drive(AimController a, float totalSec, bool wantRaised, float horizontalSpeed = 0f)
    {
        float elapsed = 0f;
        while (elapsed < totalSec)
        {
            float step = System.Math.Min(Tick, totalSec - elapsed);
            a.Step(step, wantRaised, horizontalSpeed);
            elapsed += step;
        }
    }
}
