using Sail.Game.Failure;

namespace SailNet.Tests;

/// <summary>
/// The failure-state machine (phase 1c, beta plan §10).
///
/// <para><b>What this file exists to prevent.</b> This repo has shipped "a dead player's ragdoll
/// still under player control" once. The netcode half of that defect is pinned elsewhere
/// (<c>IncapacityWireTests</c> for the replication, <c>IncapacityCascadeTests</c> for the atomic
/// transition); this file pins the half that decides <i>whether a player is playing the game at
/// all</i> — that every state has a defined entry, exit and behaviour, that no boundary can be
/// skipped by a hitch, and above all <b>that dawn cannot be refused</b>. The anti-unwinnable floor
/// (MECHANICS-BIBLE §10.5) is the one guarantee in this system with no valid exception, and a
/// guard added to it later would be silent, correct-looking, and would cost a group their run.</para>
/// </summary>
public class IncapacitationMachineTests
{
    private const float Tick = 1f / 60f;
    private static readonly IncapacitationMachine.AssistInputs Nothing =
        IncapacitationMachine.AssistInputs.None;
    private static readonly IncapacitationMachine.AssistInputs Shaking = new(true, false);
    private static readonly IncapacitationMachine.AssistInputs Warm = new(false, true);

    /// <summary>Run <paramref name="seconds"/> of ticks, returning the first non-None step.</summary>
    private static IncapacityStep Run(IncapacitationMachine m, float seconds,
        IncapacitationMachine.AssistInputs inputs)
    {
        var first = IncapacityStep.None;
        int ticks = (int)(seconds / Tick) + 1;
        for (int i = 0; i < ticks; i++)
        {
            IncapacityStep step = m.Advance(Tick, inputs);
            if (step != IncapacityStep.None && first == IncapacityStep.None)
                first = step;
        }
        return first;
    }

    // --- Active ---------------------------------------------------------------------------------

    [Fact]
    public void Active_IsTheStartingStateAndDeniesNothing()
    {
        var m = new IncapacitationMachine();
        Assert.Equal(IncapacityState.Active, m.State);
        Assert.False(m.Incapacitated);
        Assert.False(m.ControlDenied);
        Assert.False(m.Draggable);
        Assert.Equal(IncapacityCause.None, m.Cause);
        Assert.Equal(InjuryMark.None, m.Injuries);
    }

    [Fact]
    public void Active_TickingForeverChangesNothing()
    {
        var m = new IncapacitationMachine();
        Assert.Equal(IncapacityStep.None, Run(m, 120f, Shaking));
        Assert.Equal(IncapacityState.Active, m.State);
    }

    // --- Entry ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(IncapacityCause.LongOneContact, IncapacityState.KnockedOut)]
    [InlineData(IncapacityCause.BreakerRampage, IncapacityState.KnockedOut)]
    [InlineData(IncapacityCause.WaspStingStack, IncapacityState.KnockedOut)]
    [InlineData(IncapacityCause.NightWaterChill, IncapacityState.Frozen)]
    [InlineData(IncapacityCause.StarerGaze, IncapacityState.Frozen)]
    public void EveryCause_EntersItsDeclaredState(IncapacityCause cause, IncapacityState expected)
    {
        var m = new IncapacitationMachine();
        Assert.True(m.Enter(cause));
        Assert.Equal(expected, m.State);
        Assert.Equal(cause, m.Cause);
        Assert.True(m.Incapacitated);
        Assert.True(m.ControlDenied);
    }

    /// <summary>The plan's "one machine, two skins" asserted rather than assumed: the Starer's
    /// gaze and the lake's cold must be the SAME state, not two that resemble each other. If this
    /// ever fails, phase 3c has quietly grown a second freeze.</summary>
    [Fact]
    public void StarerGazeAndNightWaterChill_AreTheSameState()
    {
        var starer = new IncapacitationMachine();
        var lake = new IncapacitationMachine();
        starer.Enter(IncapacityCause.StarerGaze);
        lake.Enter(IncapacityCause.NightWaterChill);
        Assert.Equal(starer.State, lake.State);
        Assert.Equal(IncapacityState.Frozen, starer.State);
    }

    [Fact]
    public void Enter_IsIdempotent_AndFirstCauseWins()
    {
        var m = new IncapacitationMachine();
        Assert.True(m.Enter(IncapacityCause.NightWaterChill));
        // A second cause landing on a body already down must not restate it — that would restart
        // the rescue timer under the teammate trying to help.
        Assert.False(m.Enter(IncapacityCause.LongOneContact));
        Assert.Equal(IncapacityState.Frozen, m.State);
        Assert.Equal(IncapacityCause.NightWaterChill, m.Cause);
    }

    [Fact]
    public void Enter_WithNoCause_IsRefused()
    {
        var m = new IncapacitationMachine();
        Assert.False(m.Enter(IncapacityCause.None));
        Assert.Equal(IncapacityState.Active, m.State);
    }

    /// <summary>Issue #179's "named open item", made structural: a cause with no declared state is
    /// a design fork to surface, never a runtime fallback that quietly picks one.</summary>
    [Fact]
    public void ACauseWithNoDeclaredState_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => IncapacityRules.StateForCause((IncapacityCause)200));
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => IncapacityRules.StateForCause(IncapacityCause.None));
    }

    // --- Knocked Out: behaviour and exit ----------------------------------------------------------

    [Fact]
    public void KnockedOut_ShakingForTheFullDuration_Wakes()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.LongOneContact);
        Assert.Equal(IncapacityStep.Woke, Run(m, IncapacityRules.ShakeSecondsToWake, Shaking));
        Assert.Equal(IncapacityState.Active, m.State);
        Assert.Equal(IncapacityExit.TeammateShake, m.LastExit);
    }

    [Fact]
    public void KnockedOut_ShakingJustShortOfTheDuration_DoesNotWake()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.LongOneContact);
        Run(m, IncapacityRules.ShakeSecondsToWake - 0.2f, Shaking);
        Assert.Equal(IncapacityState.KnockedOut, m.State);
    }

    /// <summary>Non-cumulative, the same rule (and the same reasoning) as the fireside dry-off:
    /// five seconds OF shaking, never five seconds banked across the night. Without this the verb
    /// costs a rescuer nothing — they tap it whenever they happen to pass.</summary>
    [Fact]
    public void KnockedOut_ShakeProgress_ResetsTheMomentTheRescuerStops()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.LongOneContact);
        Run(m, IncapacityRules.ShakeSecondsToWake * 0.9f, Shaking);
        Assert.True(m.ShakeSec > 0f);
        m.Advance(Tick, Nothing);
        Assert.Equal(0f, m.ShakeSec);
        // And the leftover progress is genuinely gone: a fresh near-full shake still does not wake.
        Run(m, IncapacityRules.ShakeSecondsToWake * 0.9f, Shaking);
        Assert.Equal(IncapacityState.KnockedOut, m.State);
    }

    [Fact]
    public void KnockedOut_IsNotThawedByAFire()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.LongOneContact);
        Assert.Equal(IncapacityStep.None, Run(m, 60f, Warm));
        Assert.Equal(IncapacityState.KnockedOut, m.State);
    }

    [Fact]
    public void KnockedOut_IsNotDraggable()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.LongOneContact);
        Assert.False(m.Draggable);
    }

    // --- Frozen: behaviour and exit ----------------------------------------------------------------

    [Fact]
    public void Frozen_ThawingForTheFullDuration_Recovers()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.NightWaterChill);
        Assert.Equal(IncapacityStep.Thawed, Run(m, IncapacityRules.ThawSecondsAtFire, Warm));
        Assert.Equal(IncapacityState.Active, m.State);
        Assert.Equal(IncapacityExit.FireThaw, m.LastExit);
    }

    [Fact]
    public void Frozen_ThawProgress_ResetsOnLeavingTheWarmth()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.StarerGaze);
        Run(m, IncapacityRules.ThawSecondsAtFire * 0.9f, Warm);
        Assert.True(m.ThawSec > 0f);
        m.Advance(Tick, Nothing);
        Assert.Equal(0f, m.ThawSec);
    }

    [Fact]
    public void Frozen_IsNotWokenByShaking()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.NightWaterChill);
        Assert.Equal(IncapacityStep.None, Run(m, 60f, Shaking));
        Assert.Equal(IncapacityState.Frozen, m.State);
    }

    [Fact]
    public void Frozen_IsDraggable()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.NightWaterChill);
        Assert.True(m.Draggable);
    }

    // --- The dawn floor -----------------------------------------------------------------------------

    /// <summary>Beta plan §4.1 and MECHANICS-BIBLE §10.5. Every state, every cause,
    /// unconditionally. There is no argument, no world condition and no tuning value that can be
    /// added to <c>RecoverAtDawn</c> — if this test ever needs a caveat, the caveat is the bug.</summary>
    [Theory]
    [InlineData(IncapacityCause.LongOneContact)]
    [InlineData(IncapacityCause.BreakerRampage)]
    [InlineData(IncapacityCause.WaspStingStack)]
    [InlineData(IncapacityCause.NightWaterChill)]
    [InlineData(IncapacityCause.StarerGaze)]
    public void Dawn_RecoversEveryStateFromEveryCause(IncapacityCause cause)
    {
        var m = new IncapacitationMachine();
        m.Enter(cause);
        Assert.True(m.RecoverAtDawn());
        Assert.Equal(IncapacityState.Active, m.State);
        Assert.Equal(IncapacityExit.Dawn, m.LastExit);
        Assert.False(m.ControlDenied);
    }

    /// <summary>Issue #179's acceptance criterion, verbatim: "dawn recovers every state from every
    /// cause, <b>including a state entered one tick before dawn</b>". Dawn is an edge on the run's
    /// clock, never a sample of anybody's state, so how long you have been down cannot matter.</summary>
    [Fact]
    public void Dawn_RecoversAStateEnteredOneTickEarlier()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.NightWaterChill);
        m.Advance(Tick, Nothing);
        Assert.True(m.RecoverAtDawn());
        Assert.Equal(IncapacityState.Active, m.State);
    }

    [Fact]
    public void Dawn_AlsoClearsAMomentaryImpulseRagdoll()
    {
        var m = new IncapacitationMachine();
        m.Impulse();
        Assert.True(m.ImpulseRagdolled);
        Assert.True(m.RecoverAtDawn());
        Assert.False(m.ControlDenied);
    }

    [Fact]
    public void Dawn_OnAnAlreadyActiveCamper_IsAHarmlessNoOp()
    {
        var m = new IncapacitationMachine();
        Assert.False(m.RecoverAtDawn());
        Assert.Equal(IncapacityState.Active, m.State);
    }

    [Fact]
    public void Dawn_LeavesTheInjuryLedgerAlone()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.NightWaterChill);
        m.RecoverAtDawn();
        // Beta plan §4.4: dawn gives you your body back, not your dignity. The bus photo reads this.
        Assert.True(m.Injuries.HasFlag(InjuryMark.FrostCoating));
    }

    // --- The impulse ragdoll ------------------------------------------------------------------------

    [Fact]
    public void Impulse_DeniesControlWithoutIncapacitating()
    {
        var m = new IncapacitationMachine();
        Assert.Equal(IncapacityStep.None, m.Impulse());
        Assert.True(m.ImpulseRagdolled);
        Assert.True(m.ControlDenied);
        // The distinction beta plan §10 draws, and the one phase 4a depends on existing: an
        // impulse ragdoll is NOT incapacitation, so it must not count toward the loss condition.
        Assert.False(m.Incapacitated);
        Assert.Equal(IncapacityState.Active, m.State);
    }

    [Fact]
    public void Impulse_ClearsItselfOnItsOwnTimer()
    {
        var m = new IncapacitationMachine();
        m.Impulse();
        Assert.Equal(IncapacityStep.ImpulseEnded, Run(m, IncapacityRules.ImpulseRagdollSec, Nothing));
        Assert.False(m.ControlDenied);
    }

    [Fact]
    public void Impulse_StacksIntoARealKnockoutAtTheThreshold()
    {
        var m = new IncapacitationMachine();
        for (int i = 1; i < IncapacityRules.ImpulseHitsToKnockout; i++)
        {
            Assert.Equal(IncapacityStep.None, m.Impulse());
            Assert.False(m.Incapacitated);
        }
        Assert.Equal(IncapacityStep.StackedToKnockout, m.Impulse());
        Assert.Equal(IncapacityState.KnockedOut, m.State);
        Assert.Equal(IncapacityCause.BreakerRampage, m.Cause);
    }

    [Fact]
    public void Impulse_StackCauseIsTheCallersNotTheBreakers()
    {
        var m = new IncapacitationMachine();
        for (int i = 0; i < IncapacityRules.ImpulseHitsToKnockout; i++)
            m.Impulse(IncapacityCause.WaspStingStack);
        Assert.Equal(IncapacityCause.WaspStingStack, m.Cause);
        Assert.True(m.Injuries.HasFlag(InjuryMark.Bandage));
    }

    /// <summary>Hits outside the window do not count. Without the decay a player shoved once per
    /// minute across a whole night is eventually flattened by a hit that had no context.</summary>
    [Fact]
    public void Impulse_HitsOutsideTheStackingWindow_DoNotCount()
    {
        var m = new IncapacitationMachine();
        for (int i = 1; i < IncapacityRules.ImpulseHitsToKnockout; i++)
        {
            m.Impulse();
            Run(m, IncapacityRules.ImpulseStackWindowSec + 0.5f, Nothing);
        }
        Assert.Equal(0, m.ImpulseHits);
        Assert.Equal(IncapacityStep.None, m.Impulse());
        Assert.False(m.Incapacitated);
    }

    /// <summary>You cannot shove over something already flat. Without this the Breaker could pin a
    /// knocked-out camper on the floor indefinitely — a soft-lock wearing a physics effect's
    /// clothes.</summary>
    [Fact]
    public void Impulse_OnAnAlreadyDownedBody_IsARefusal()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.NightWaterChill);
        Assert.Equal(IncapacityStep.None, m.Impulse());
        Assert.False(m.ImpulseRagdolled);
        Assert.Equal(IncapacityState.Frozen, m.State);
    }

    [Fact]
    public void Entering_WipesTheImpulseLedger()
    {
        var m = new IncapacitationMachine();
        m.Impulse();
        m.Enter(IncapacityCause.LongOneContact);
        Assert.Equal(0, m.ImpulseHits);
        Assert.False(m.ImpulseRagdolled);
    }

    // --- Degenerate input and hitches ----------------------------------------------------------------

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void DegenerateDelta_IsANoOp(float dt)
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.LongOneContact);
        Assert.Equal(IncapacityStep.None, m.Advance(dt, Shaking));
        Assert.Equal(0f, m.ShakeSec);
        Assert.Equal(0f, m.ElapsedSec);
        Assert.Equal(IncapacityState.KnockedOut, m.State);
    }

    /// <summary>One boundary per call, the guarantee <c>SputterSequence.Advance</c> gives. A
    /// 999-second hitch — a loading stall, a debugger break, a coarse headless step — must not
    /// carry a body through a state it was supposed to occupy.</summary>
    [Fact]
    public void AHugeHitch_CrossesAtMostOneBoundary()
    {
        var m = new IncapacitationMachine();
        m.Impulse();
        // The impulse ends. It must NOT also stack, wake, or thaw anything in the same call.
        Assert.Equal(IncapacityStep.ImpulseEnded, m.Advance(999f, Shaking));
        Assert.False(m.ControlDenied);
        Assert.Equal(IncapacityState.Active, m.State);
    }

    [Fact]
    public void AHugeHitch_WhileShaken_WakesExactlyOnceAndStops()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.LongOneContact);
        Assert.Equal(IncapacityStep.Woke, m.Advance(999f, Shaking));
        Assert.Equal(IncapacityState.Active, m.State);
        Assert.Equal(IncapacityStep.None, m.Advance(999f, Shaking));
    }

    // --- Injuries ------------------------------------------------------------------------------------

    [Fact]
    public void Injuries_AccumulateAcrossARunAndSurviveRecovery()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.NightWaterChill);
        m.RecoverAtDawn();
        m.Enter(IncapacityCause.WaspStingStack);
        m.RecoverAtDawn();
        Assert.True(m.Injuries.HasFlag(InjuryMark.FrostCoating));
        Assert.True(m.Injuries.HasFlag(InjuryMark.Bandage));
        Assert.True(m.Injuries.HasFlag(InjuryMark.BirdsHalo));
    }

    /// <summary>A new run is a new camper. Row 17 of the cascade table — a summary screen showing
    /// the previous run's frostbite is exactly the "we forgot system X" defect it exists to
    /// catch.</summary>
    [Fact]
    public void Reset_ClearsTheInjuryLedgerWhereDawnDoesNot()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.NightWaterChill);
        m.Reset();
        Assert.Equal(InjuryMark.None, m.Injuries);
        Assert.Equal(IncapacityState.Active, m.State);
    }

    // --- Rollback (a cascade that failed before its commit) --------------------------------------------

    /// <summary>The machine must never believe a transition no other system ever saw — the
    /// one-sided version of the defect the cascade table exists to prevent.</summary>
    [Fact]
    public void RollBack_RestoresThePreviousState()
    {
        var m = new IncapacitationMachine();
        InjuryMark before = m.Injuries;
        m.Enter(IncapacityCause.LongOneContact);
        m.RollBackTo(IncapacityState.Active, IncapacityCause.None, before);
        Assert.Equal(IncapacityState.Active, m.State);
        Assert.Equal(IncapacityCause.None, m.Cause);
        Assert.False(m.ControlDenied);
    }

    /// <summary>Found in review, and it would have been invisible in play: rolling back through
    /// <c>Reset</c> wipes the injury LEDGER, so one failed transition would erase every soot mark
    /// and bandage the camper had collected on earlier nights — and the bus photo would quietly
    /// show a clean kid who had been flattened four times.</summary>
    [Fact]
    public void RollBack_DoesNotWipeInjuriesEarnedOnEarlierNights()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.WaspStingStack);
        m.RecoverAtDawn();
        InjuryMark earned = m.Injuries;
        Assert.True(earned.HasFlag(InjuryMark.Bandage));

        // A second incapacitation whose cascade fails before its commit.
        InjuryMark snapshot = m.Injuries;
        m.Enter(IncapacityCause.NightWaterChill);
        m.RollBackTo(IncapacityState.Active, IncapacityCause.None, snapshot);

        Assert.Equal(earned, m.Injuries);
        Assert.False(m.Injuries.HasFlag(InjuryMark.FrostCoating)); // the aborted one left no mark
    }

    [Fact]
    public void RollBack_CanRestoreAPreviouslyDownedState()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.NightWaterChill);
        InjuryMark snapshot = m.Injuries;
        m.RollBackTo(IncapacityState.Frozen, IncapacityCause.NightWaterChill, snapshot);
        Assert.Equal(IncapacityState.Frozen, m.State);
        Assert.Equal(IncapacityCause.NightWaterChill, m.Cause);
        Assert.True(m.Incapacitated);
    }

    // --- The run-end query (beta plan §4.1, for phase 2c) ---------------------------------------------

    [Fact]
    public void AllIncapacitated_IsFalseForAnEmptyCamp()
    {
        // Zero connected campers is a session that has not started or has ended — never a group
        // that has lost. Vacuous truth here would end a run at the main menu.
        Assert.False(IncapacitationMachine.AllIncapacitated(System.Array.Empty<IncapacitationMachine>()));
    }

    [Fact]
    public void AllIncapacitated_NeedsEveryCamperDown()
    {
        var down = new IncapacitationMachine();
        var up = new IncapacitationMachine();
        down.Enter(IncapacityCause.LongOneContact);
        Assert.False(IncapacitationMachine.AllIncapacitated(new[] { down, up }));
        up.Enter(IncapacityCause.StarerGaze);
        Assert.True(IncapacitationMachine.AllIncapacitated(new[] { down, up }));
    }

    /// <summary>Beta plan §4.1's own words: "Frozen counts as incapacitated". A group frozen solid
    /// has lost exactly as much as a group knocked out.</summary>
    [Fact]
    public void AllIncapacitated_CountsFrozen()
    {
        var a = new IncapacitationMachine();
        var b = new IncapacitationMachine();
        a.Enter(IncapacityCause.NightWaterChill);
        b.Enter(IncapacityCause.StarerGaze);
        Assert.True(IncapacitationMachine.AllIncapacitated(new[] { a, b }));
    }

    /// <summary>And an impulse ragdoll does not. Otherwise the Breaker could end a run by
    /// bowling over the last two campers standing for a second and a bit.</summary>
    [Fact]
    public void AllIncapacitated_DoesNotCountAnImpulseRagdoll()
    {
        var a = new IncapacitationMachine();
        var b = new IncapacitationMachine();
        a.Enter(IncapacityCause.LongOneContact);
        b.Impulse();
        Assert.False(IncapacitationMachine.AllIncapacitated(new[] { a, b }));
    }

    [Fact]
    public void AllIncapacitated_ReArmsWhenSomebodyGetsUp()
    {
        var a = new IncapacitationMachine();
        var b = new IncapacitationMachine();
        a.Enter(IncapacityCause.LongOneContact);
        b.Enter(IncapacityCause.LongOneContact);
        Assert.True(IncapacitationMachine.AllIncapacitated(new[] { a, b }));
        b.RecoverAtDawn();
        Assert.False(IncapacitationMachine.AllIncapacitated(new[] { a, b }));
    }

    // --- Progress reporting ---------------------------------------------------------------------------

    [Fact]
    public void AssistProgress_TracksTheRelevantTimerAndClampsToOne()
    {
        var m = new IncapacitationMachine();
        m.Enter(IncapacityCause.LongOneContact);
        Assert.Equal(0f, m.AssistProgress01);
        Run(m, IncapacityRules.ShakeSecondsToWake * 0.5f, Shaking);
        Assert.InRange(m.AssistProgress01, 0.4f, 0.6f);
        // Warmth is the wrong verb for this state, so it must move nothing.
        var frozen = new IncapacitationMachine();
        frozen.Enter(IncapacityCause.StarerGaze);
        Run(frozen, IncapacityRules.ThawSecondsAtFire * 0.5f, Shaking);
        Assert.Equal(0f, frozen.AssistProgress01);
    }
}
