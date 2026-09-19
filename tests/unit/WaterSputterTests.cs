using Sail.Game.Water;

namespace SailNet.Tests;

/// <summary>
/// The sputter-out sequence (lake-water contract §6): its timing, its single control-return
/// moment, and — the one that actually matters — the exactly-once idempotency of the cost.
///
/// The repo has shipped a ragdoll the player could still steer. MECHANICS-BIBLE §2 was written
/// because of it, and every assertion below about <see cref="SputterSequence.ControlLocked"/>
/// exists so that defect cannot be reintroduced by an off-by-one in a phase machine.
/// </summary>
public class WaterSputterTests
{
    private const float Dt = 1f / 60f;

    private static SputterSequence Started()
    {
        var s = new SputterSequence();
        Assert.True(s.Begin());
        return s;
    }

    /// <summary>Advance until the given step fires, or fail. Returns the seconds it took.</summary>
    private static float RunTo(SputterSequence s, SputterSequence.Step wanted, float capSec = 30f)
    {
        float t = 0f;
        while (t < capSec)
        {
            t += Dt;
            if (s.Advance(Dt) == wanted)
                return t;
        }
        Assert.Fail($"{wanted} never fired within {capSec}s");
        return t;
    }

    // --- Timing -------------------------------------------------------------------------------

    [Fact]
    public void Sequence_RunsGoUnderThenHardCutThenRecoverThenIdle()
    {
        var s = new SputterSequence();
        Assert.Equal(SputterPhase.None, s.Phase);
        Assert.False(s.ControlLocked);

        Assert.True(s.Begin());
        Assert.Equal(SputterPhase.GoingUnder, s.Phase);

        float toCut = RunTo(s, SputterSequence.Step.HardCut);
        Assert.Equal(WaterGeometry.GoUnderSec, toCut, precision: 1);
        Assert.Equal(SputterPhase.Recovering, s.Phase);

        float toRecover = RunTo(s, SputterSequence.Step.Recovered);
        Assert.Equal(WaterGeometry.RecoverSec, toRecover, precision: 1);
        Assert.Equal(SputterPhase.None, s.Phase);
    }

    [Fact]
    public void Sequence_IdleAdvanceDoesNothing()
    {
        var s = new SputterSequence();
        for (int i = 0; i < 100; i++)
            Assert.Equal(SputterSequence.Step.None, s.Advance(Dt));
        Assert.Equal(SputterPhase.None, s.Phase);
        Assert.Equal(0, s.CostAppliedCount);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void Sequence_NonPositiveDt_DoesNotAdvance(float dt)
    {
        var s = Started();
        Assert.Equal(SputterSequence.Step.None, s.Advance(dt));
        Assert.Equal(0f, s.PhaseElapsedSec, precision: 5);
        Assert.Equal(SputterPhase.GoingUnder, s.Phase);
    }

    [Fact]
    public void Sequence_OneEnormousStep_StillCrossesOneBoundaryAtATime()
    {
        // A hitch (an alt-tab, a loading spike) must not collapse the whole sequence into a
        // single frame and skip the hard cut — the cut is where the relocation happens, so
        // skipping it would recover the camper in deep water.
        var s = Started();
        Assert.Equal(SputterSequence.Step.HardCut, s.Advance(999f));
        Assert.Equal(SputterPhase.Recovering, s.Phase);
        Assert.Equal(SputterSequence.Step.Recovered, s.Advance(999f));
        Assert.Equal(SputterPhase.None, s.Phase);
    }

    // --- Control returns at exactly one moment (MECHANICS-BIBLE §2) --------------------------------

    [Fact]
    public void Control_IsLockedForEveryTickOfTheEpisodeAndReturnsExactlyOnce()
    {
        var s = Started();
        int returns = 0;
        bool wasLocked = s.ControlLocked;
        Assert.True(wasLocked);

        for (int i = 0; i < 600; i++)
        {
            SputterSequence.Step step = s.Advance(Dt);
            bool locked = s.ControlLocked;

            // The lock may only ever change on the Recovered boundary. Every other tick, and
            // notably the hard cut, must leave it exactly as it was.
            if (locked != wasLocked)
            {
                Assert.Equal(SputterSequence.Step.Recovered, step);
                Assert.False(locked);
                returns++;
            }
            if (step == SputterSequence.Step.HardCut)
                Assert.True(locked, "control came back at the hard cut — that is the ragdoll bug");
            wasLocked = locked;
        }
        Assert.Equal(1, returns);
    }

    [Fact]
    public void Sinking_IsTrueOnlyDuringGoUnder_AndTheDepthIsBounded()
    {
        var s = Started();
        Assert.True(s.Sinking);
        RunTo(s, SputterSequence.Step.HardCut);
        Assert.False(s.Sinking);
        Assert.Equal(0f, s.SinkDepthM, precision: 5);

        var t = Started();
        t.Advance(WaterGeometry.GoUnderSec * 0.5f);
        Assert.Equal(WaterGeometry.GoUnderSec * 0.5f * WaterGeometry.SinkRate, t.SinkDepthM, precision: 3);
        Assert.True(t.SinkDepthM <= WaterGeometry.GoUnderSec * WaterGeometry.SinkRate);
    }

    // --- Idempotency (MECHANICS-BIBLE §4) -------------------------------------------------------------

    [Fact]
    public void Cost_AppliesExactlyOnce_NoMatterHowOftenBeginIsCalled()
    {
        var s = new SputterSequence();
        Assert.True(s.Begin());
        for (int i = 0; i < 1_000; i++)
            Assert.False(s.Begin());
        Assert.Equal(1, s.CostAppliedCount);
        Assert.True(s.CostApplied);
    }

    [Fact]
    public void Cost_CannotBeReAppliedMidEpisode_IncludingAcrossTheHardCut()
    {
        var s = Started();
        for (int i = 0; i < 600; i++)
        {
            SputterSequence.Step step = s.Advance(Dt);
            if (step == SputterSequence.Step.Recovered)
                break;
            Assert.False(s.Begin(), "a second cost was applied inside a running episode");
        }
        Assert.Equal(1, s.CostAppliedCount);
    }

    [Fact]
    public void Cost_ASecondEpisodeIsPossibleOnceTheFirstHasFullyEnded()
    {
        // Exactly-once is per EPISODE, not per lifetime. A camper who swims out twice in an
        // evening loses two tapes; the guarantee is that one sputter-out cannot cost two.
        var s = Started();
        RunTo(s, SputterSequence.Step.HardCut);
        RunTo(s, SputterSequence.Step.Recovered);
        Assert.False(s.CostApplied);
        Assert.True(s.Begin());
        Assert.Equal(2, s.CostAppliedCount);
    }

    [Fact]
    public void Cost_PositiveControl_TheCounterCanReportMoreThanOne()
    {
        // The exactly-once assertions above are only meaningful if CostAppliedCount is capable of
        // reading 2. It is — through the legitimate door, and only through it.
        var s = new SputterSequence();
        for (int episode = 0; episode < 3; episode++)
        {
            Assert.True(s.Begin());
            RunTo(s, SputterSequence.Step.HardCut);
            RunTo(s, SputterSequence.Step.Recovered);
        }
        Assert.Equal(3, s.CostAppliedCount);
    }

    // --- Reset (interrupt handling, INTERACTION-BIBLE §7) ------------------------------------------------

    [Fact]
    public void Reset_ReturnsToIdleWithoutFiringABoundaryOrLosingTheTally()
    {
        var s = Started();
        s.Advance(Dt);
        s.Reset();
        Assert.Equal(SputterPhase.None, s.Phase);
        Assert.False(s.ControlLocked);
        Assert.False(s.CostApplied);
        // The lifetime tally survives — it is evidence, not episode state.
        Assert.Equal(1, s.CostAppliedCount);
        // And a reset never leaves the camper locked out of control (the softlock §7 forbids).
        Assert.Equal(SputterSequence.Step.None, s.Advance(Dt));
        Assert.False(s.ControlLocked);
    }

    [Fact]
    public void Reset_IsIdempotent()
    {
        var s = new SputterSequence();
        s.Reset();
        s.Reset();
        Assert.Equal(SputterPhase.None, s.Phase);
        Assert.Equal(0, s.CostAppliedCount);
    }
}
