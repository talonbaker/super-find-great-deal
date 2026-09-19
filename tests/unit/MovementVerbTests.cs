using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Sail.Game.Water;
using Xunit;
using AirJumpResolution = MpFoundation.Net.AvatarMotor.AirJumpResolution;
using ChainResolution = MpFoundation.Net.AvatarMotor.ChainResolution;
using VerbResolution = MpFoundation.Net.AvatarMotor.VerbResolution;

namespace SailNet.Tests;

/// <summary>
/// <b>The movement verb state machine</b> — MOVE-5b against
/// <c>docs/design/2026-08-27-movement-verbs-state-machine.md</c>: the six states and their
/// transition table (§3), the crouch-slide (§4), the tuck and duck walk (§5), the chain jump (§6),
/// both double-jump variants (§7), the momentum share (§8), the one amendment (§9) and the wire
/// (§10).
///
/// <para><b>Nothing here parks a non-default <see cref="MotorTuning.Current"/>.</b> Every rule
/// under test takes its tuning as a parameter, which is exactly why the MOVE-5 functions were
/// given that shape — xUnit runs test classes in parallel and seven other classes read
/// <c>AvatarMotor</c>'s properties live, so a test that had to park <c>AirJumpMode = 2</c> to
/// exercise the Kick would surface as an intermittent red somewhere else entirely
/// (<c>MotorTuningTests</c>'s class doc states the rule; this class obeys it without a single
/// <c>try/finally</c>).</para>
///
/// <para><b>§11.4's standing rule is obeyed too: these assert PROPERTIES, not feel literals.</b>
/// The anti-chatter gap, the exit-under-every-tuning guarantee, the wire widths, the exact-no-op
/// identity — every one of them survives Talon dragging any slider in the table. The two places a
/// literal appears are the wire constants (§10, which are not feel) and the entry threshold's
/// coincidence with <c>SprintEnterMps</c> (§4.3, which is the rule's whole name).</para>
/// </summary>
public class MovementVerbTests
{
    private const float Dt = AvatarMotor.TickDelta;
    private static MotorTuning Base => MotorTuning.Default;

    /// <summary>The chain's recommended first experiment (§11.1), never a moved default.</summary>
    private static MotorTuning Chained => Base with { ChainBonusMps = 0.60f };

    private static Vector3 Flat(float x, float z) => new(x, 0f, z);

    /// <summary>Float comparison with a stated tolerance and a stated subject. A helper rather than
    /// <c>Assert.Equal</c>'s three-argument form, whose <c>(float, float, int)</c> and
    /// <c>(float, float, float)</c> overloads are ambiguous — and a failure that names the quantity
    /// is worth more than one that names a tolerance.</summary>
    private static void Near(float expected, float actual, float eps, string what) =>
        Assert.True(Mathf.Abs(expected - actual) <= eps,
            $"{what}: expected {expected:0.######}, got {actual:0.######} (tolerance {eps})");

    private static VerbResolution Ground(in MotorTuning t, MoveVerb verb, byte clock, bool held,
        float speed, Vector3? stick = null) =>
        AvatarMotor.StepVerb(t, verb, clock, grounded: true, locked: false,
            jumped: false, jumpHeld: held, WaterState.Dry, Flat(0f, -speed),
            stick ?? Flat(0f, -1f));

    /// <summary>Runs a body forward on the ground with the button held, from Normal, and returns
    /// the state after <paramref name="ticks"/> ticks. The clock is the machine's own, so this is
    /// the hold window as the motor actually accrues it rather than a count kept beside it.</summary>
    private static VerbResolution HoldOnGround(in MotorTuning t, float speed, int ticks)
    {
        var r = new VerbResolution(MoveVerb.Normal, 0);
        for (int i = 0; i < ticks; i++)
            r = Ground(t, r.Verb, r.ClockTicks, held: true, speed);
        return r;
    }

    // =============================================================================================
    // 1. The six states, the transition table and the same-tick precedences (AC 1).
    // =============================================================================================

    /// <summary><b>Every state in §3.2 is reachable, and every one of them has an exit that no
    /// tuning can refuse.</b> MECHANICS §2 wants exactly this: a state a player cannot leave is the
    /// defect, and "there is no such state" is only worth saying if the reaching half is
    /// demonstrated first — which is this test's positive control.</summary>
    [Fact]
    public void EveryStateIsReachable_AndEveryOneOfThemHasAnExit()
    {
        MotorTuning t = Base;

        // Reachable: Normal is the default; Tuck and Slide are the two crouch-trigger outcomes;
        // DuckWalk is the settle.
        Assert.Equal(MoveVerb.Normal, Ground(t, MoveVerb.Normal, 0, held: false, 0f).Verb);
        Assert.Equal(MoveVerb.Tuck, HoldOnGround(t, 0f, t.JumpHoldWindowTicks).Verb);
        Assert.Equal(MoveVerb.Slide, HoldOnGround(t, 8.64f, t.JumpHoldWindowTicks).Verb);

        VerbResolution settled = AvatarMotor.StepVerb(t, MoveVerb.Slide,
            (byte)(t.SlideMaxTicks - 1), grounded: true, locked: false,
            jumped: false, jumpHeld: true, WaterState.Dry, Flat(0f, -1f), Flat(0f, -1f));
        Assert.Equal(MoveVerb.DuckWalk, settled.Verb);

        // Exits: releasing the button leaves Tuck and Slide on the very next tick, and leaving the
        // floor, a control lock and water leave EVERY verb including the latched one.
        foreach (MoveVerb verb in new[] { MoveVerb.Tuck, MoveVerb.Slide })
            Assert.Equal(MoveVerb.Normal, Ground(t, verb, 5, held: false, 3f).Verb);

        foreach (MoveVerb verb in Enum.GetValues<MoveVerb>())
        {
            Assert.Equal(MoveVerb.Normal, AvatarMotor.StepVerb(t, verb, 5, grounded: false,
                locked: false, jumped: false, jumpHeld: true, WaterState.Dry,
                Flat(0f, -3f), Flat(0f, -1f)).Verb);
            Assert.Equal(MoveVerb.Normal, AvatarMotor.StepVerb(t, verb, 5, grounded: true,
                locked: true, jumped: false, jumpHeld: true, WaterState.Dry,
                Flat(0f, -3f), Flat(0f, -1f)).Verb);
            Assert.Equal(MoveVerb.Normal, AvatarMotor.StepVerb(t, verb, 5, grounded: true,
                locked: false, jumped: false, jumpHeld: true, WaterState.Wading,
                Flat(0f, -3f), Flat(0f, -1f)).Verb);
            Assert.Equal(MoveVerb.Normal, AvatarMotor.StepVerb(t, verb, 5, grounded: true,
                locked: false, jumped: true, jumpHeld: true, WaterState.Dry,
                Flat(0f, -3f), Flat(0f, -1f)).Verb);
        }
    }

    /// <summary>
    /// <b>The most important precedence in §3.4's table: a jump edge and a verb entry on the same
    /// tick resolve to the JUMP.</b> Rule 3 runs before rule 4, and the reason is the weight
    /// principle rather than taste — the jump is the one input carrying a hard responsiveness
    /// guarantee, and a verb that could swallow a jump press would cost a frame.
    ///
    /// <para>Set up at the exact tick the verb would otherwise enter, so this is the collision and
    /// not a nearby case.</para></summary>
    [Fact]
    public void AJumpAndAVerbEntryOnTheSameTick_ResolveToTheJump()
    {
        MotorTuning t = Base;
        var onTheBrink = (byte)(t.JumpHoldWindowTicks - 1);

        // Positive control: with no jump, this exact tick enters the Slide.
        Assert.Equal(MoveVerb.Slide, Ground(t, MoveVerb.Normal, onTheBrink, held: true, 8.64f).Verb);

        // And with a jump on the same tick, the jump wins and the verb does not enter.
        VerbResolution jumped = AvatarMotor.StepVerb(t, MoveVerb.Normal, onTheBrink,
            grounded: true, locked: false, jumped: true, jumpHeld: true,
            WaterState.Dry, Flat(0f, -8.64f), Flat(0f, -1f));
        Assert.Equal(MoveVerb.Normal, jumped.Verb);
        Assert.Equal(0, (int)jumped.ClockTicks);
    }

    /// <summary><b>§3.4: a slide's speed-exit and a release on the same tick resolve to the
    /// RELEASE, and therefore to Normal rather than to a DuckWalk.</b> An implicit settle must
    /// never override an explicit input — and the positive control is the same tick with the button
    /// still down, which does settle.</summary>
    [Fact]
    public void ASlidesSpeedExitAndAReleaseOnTheSameTick_ResolveToTheRelease()
    {
        MotorTuning t = Base with { DuckWalkGuaranteed = 1f };
        var pastTheFloor = (byte)(t.SlideMinTicks + 1);
        float belowExit = t.SlideExitSpeedMps - 0.5f;

        Assert.Equal(MoveVerb.DuckWalk,
            Ground(t, MoveVerb.Slide, pastTheFloor, held: true, belowExit).Verb);
        Assert.Equal(MoveVerb.Normal,
            Ground(t, MoveVerb.Slide, pastTheFloor, held: false, belowExit).Verb);
    }

    /// <summary>
    /// <b>The duck walk is LATCHED and the tuck is HELD — the one difference between two states
    /// that share their locomotion exactly.</b> §5.1: a state you may remain in indefinitely cannot
    /// require a held button, and a state you pop out of on release must. This is the assertion
    /// that would catch either of them being given the other's exit rule.
    /// </summary>
    [Fact]
    public void TheDuckWalkIsLatched_AndTheTuckIsHeld()
    {
        MotorTuning t = Base;

        // A hundred ticks with the button up, and the duck walk is still there.
        var r = new VerbResolution(MoveVerb.DuckWalk, 0);
        for (int i = 0; i < 100; i++)
            r = Ground(t, r.Verb, r.ClockTicks, held: false, 2.0f);
        Assert.Equal(MoveVerb.DuckWalk, r.Verb);

        // One tick with the button up, and the tuck is gone.
        Assert.Equal(MoveVerb.Normal, Ground(t, MoveVerb.Tuck, 40, held: false, 0f).Verb);
    }

    /// <summary><b>§5.4, decided: a jump press exits the duck walk and fires the jump on the same
    /// tick.</b> No stand-first, no intermediate state, no delay — a required stand-first is a
    /// recovery lockout by another name, and there is no recovery lockout, ever.</summary>
    [Fact]
    public void AJumpOutOfTheDuckWalkIsImmediate_WithNoStandUpState()
    {
        VerbResolution r = AvatarMotor.StepVerb(Base, MoveVerb.DuckWalk, 30, grounded: true,
            locked: false, jumped: true, jumpHeld: true, WaterState.Dry,
            Flat(0f, -2.4f), Flat(0f, -1f));
        Assert.Equal(MoveVerb.Normal, r.Verb);
        Assert.Equal(0, (int)r.ClockTicks);
    }

    /// <summary><b>Law V1, and it is what stops the oscillation §3.5 exists to prevent.</b> A slide
    /// that runs out of speed while the button is still down goes to Tuck, never to Normal — a fall
    /// back to Normal there would re-arm the hold window and produce a ~5 Hz strobe, which is worse
    /// than a 60 Hz one because it looks tuned rather than broken.</summary>
    [Fact]
    public void ASlideThatRunsOutWhileHeld_NeverFallsBackToNormal()
    {
        MotorTuning t = Base;                       // DuckWalkGuaranteed = 0: the cone must fail
        VerbResolution r = AvatarMotor.StepVerb(t, MoveVerb.Slide, (byte)(t.SlideMinTicks + 1),
            grounded: true, locked: false, jumped: false, jumpHeld: true,
            WaterState.Dry, Flat(0f, -0.5f), Vector3.Zero);   // no stick: the cone cannot pass
        Assert.Equal(MoveVerb.Tuck, r.Verb);
    }

    /// <summary><b>Law V2: a release SPENDS the window.</b> Re-entry after a release costs a full
    /// fresh <c>JumpHoldWindowSec</c> rather than resuming where it left off, which is the
    /// anti-chatter mechanism on the button axis.</summary>
    [Fact]
    public void AReleaseSpendsTheHoldWindow_SoReEntryCostsAFullFreshOne()
    {
        MotorTuning t = Base;
        VerbResolution nearly = HoldOnGround(t, 0f, t.JumpHoldWindowTicks - 1);
        Assert.Equal(MoveVerb.Normal, nearly.Verb);
        Assert.Equal(t.JumpHoldWindowTicks - 1, (int)nearly.ClockTicks);

        VerbResolution released = Ground(t, nearly.Verb, nearly.ClockTicks, held: false, 0f);
        Assert.Equal(0, (int)released.ClockTicks);

        // One held tick after the release is one tick of window, not eleven.
        Assert.Equal(1, (int)Ground(t, released.Verb, released.ClockTicks, held: true, 0f).ClockTicks);
    }

    // =============================================================================================
    // 2. Flicker (AC 2) — §3.5's table, with its numbers.
    // =============================================================================================

    /// <summary>
    /// <b>§3.5's three no-cycle results are STRUCTURAL, not numeric</b> — there is no
    /// Slide-to-DuckWalk-and-back edge, no Tuck-to-Slide row, and no Skid-to-Slide-and-back pair,
    /// at ANY tuning. That is the strongest form the flicker rule can take, so it is swept over a
    /// grid rather than asserted at the defaults: no combination of sliders can create an edge the
    /// table does not have.
    /// </summary>
    [Theory]
    [InlineData(0.05f, 0.5f, 0.10f)]        // every window and bound at its minimum
    [InlineData(0.60f, 40.0f, 3.00f)]       // and at its maximum
    [InlineData(0.20f, 6.0f, 1.40f)]        // and shipped
    public void NoTuningCanCreateACycleTheTableDoesNotHave(float window, float decel, float maxSec)
    {
        MotorTuning t = MotorTuning.Validate(Base with
        {
            JumpHoldWindowSec = window,
            SlideDeceleration = decel,
            SlideMaxSec = maxSec,
        }, out _);

        // DuckWalk never becomes a Slide, at any speed, held or released.
        foreach (float speed in new[] { 0f, 2.4f, 6.588f, 8.64f, 12f })
        foreach (bool held in new[] { true, false })
            Assert.NotEqual(MoveVerb.Slide, Ground(t, MoveVerb.DuckWalk, 200, held, speed).Verb);

        // Tuck never becomes a Slide either — §3.3 has no such row.
        foreach (float speed in new[] { 0f, 2.4f, 6.588f, 8.64f, 12f })
            Assert.NotEqual(MoveVerb.Slide, Ground(t, MoveVerb.Tuck, 200, held: true, speed).Verb);

        // And a skid cannot run inside any crouch verb, which is the other half of the Skid-Slide
        // one-way pair (T15's refusal; T16's cancellation is asserted in the momentum-share test).
        foreach (MoveVerb verb in new[] { MoveVerb.Tuck, MoveVerb.Slide, MoveVerb.DuckWalk })
            Assert.Equal(0f, AvatarMotor.StepSkid(t.SkidMaxSec, grounded: true, locked: false,
                Flat(0f, -8f), Flat(0f, 8f), Dt, verb));
    }

    /// <summary>
    /// <b>The Normal-Slide round trip cannot be shorter than §3.5's floor</b>, and the floor is a
    /// consequence of the anti-chatter gap rather than of a timer: the body must shed from the
    /// entry speed down to the exit speed and then build all the way back up before it can enter
    /// again. Measured on the shipped pure functions rather than asserted.
    /// </summary>
    [Fact]
    public void TheNormalSlideRoundTripCannotBeShorterThanTheGapCosts()
    {
        MotorTuning t = Base;
        float entry = t.SlideEnterSpeedMps;         // 6.588
        float exit = t.SlideExitSpeedMps;           // 2.00

        // Down: the slide's own deceleration is the only speed authority in that state.
        int downTicks = 0;
        var v = Flat(0f, -entry);
        while (new Vector2(v.X, v.Z).Length() > exit && downTicks < 6000)
        {
            v = AvatarMotor.StepSlide(t, v, Vector3.Zero, Dt);
            downTicks++;
        }

        // Up: ground acceleration from the exit speed back to the entry speed.
        int upTicks = 0;
        var u = Flat(0f, -exit);
        Vector3 wish = Flat(0f, -entry);
        while (new Vector2(u.X, u.Z).Length() < entry && upTicks < 6000)
        {
            float rate = AvatarMotor.RateFor(u, wish, grounded: true);
            u = new Vector3(Mathf.MoveToward(u.X, wish.X, rate * Dt), 0f,
                Mathf.MoveToward(u.Z, wish.Z, rate * Dt));
            upTicks++;
        }

        // Plus law V2's fresh window, which a release must spend before re-entry.
        float roundTrip = (downTicks + upTicks + t.JumpHoldWindowTicks) * Dt;
        // MOVE-8: 1.30 s -> 0.95 s, and the floor is re-pinned rather than defended. §3.5's number
        // was never an independent design constant: it is a CONSEQUENCE of the gap, and the gap is
        // (entry - exit) covered twice. Talon's speed ruling took the slide entry down with the
        // gear ladder it is a fraction of (6.588 -> 4.636 m/s) while the exit stayed at 2.00, so
        // the gap itself is 40% narrower and the round trip shortened with it, arithmetically.
        // 0.95 s is still five times the 0.20 s window a player would have to beat to chatter, so
        // the guarantee this test exists for is intact; what changed is the size of the margin,
        // and MOVE-8's report carries it as a consequence of the ruling rather than as a defect.
        Assert.True(roundTrip >= 0.90f,
            $"Normal<->Slide round trip {roundTrip:0.000} s is below MOVE-8's re-measured 0.90 s "
            + "floor at the shipped tuning — the anti-chatter gap has stopped doing its job");
        Assert.True(roundTrip > 4f * t.JumpHoldWindowSec,
            "the round trip must stay several times the re-entry window, or the gap is decorative");
    }

    // =============================================================================================
    // 3. The crouch-slide's entry boundary (AC 3) — §4.2, §4.3.
    // =============================================================================================

    /// <summary>
    /// <b>The entry threshold IS <c>LocomotionProfile.SprintEnterMps</c>, so the rule has a name:
    /// you can only slide out of a sprint.</b> One line in the world, two systems agreeing, and no
    /// second number to keep in step. This is the one place a literal is asserted, because the
    /// coincidence is the rule rather than a value.
    /// </summary>
    [Fact]
    public void TheSlideEntrySpeedIsExactlyTheGearMachinesSprintThreshold()
    {
        Near(LocomotionProfile.SprintEnterMps, Base.SlideEnterSpeedMps, 1e-3f,
            "slide entry vs the gear machine's sprint threshold");
        Assert.NotEqual(Base.SkidEnterSpeedMps, Base.SlideEnterSpeedMps);
    }

    /// <summary>
    /// <b>The boundary, both sides, and the third thing that matters more than either: NO input
    /// produces nothing.</b> §4.2 makes entry inclusive at the threshold, matching
    /// <c>ShouldEnterSkid</c>'s <c>&gt;=</c> exactly, and both sides of the line land in a defined
    /// state — Slide above, Tuck below. A trigger with a dead zone on one side is how a boundary
    /// becomes a dropped input.
    /// </summary>
    [Fact]
    public void TheEntryBoundaryIsInclusive_AndBothSidesLandInADefinedState()
    {
        MotorTuning t = Base;
        float entry = t.SlideEnterSpeedMps;

        Assert.Equal(MoveVerb.Slide, AvatarMotor.EntryVerbFor(t, entry));            // at
        Assert.Equal(MoveVerb.Slide, AvatarMotor.EntryVerbFor(t, entry + 0.001f));   // above
        Assert.Equal(MoveVerb.Tuck, AvatarMotor.EntryVerbFor(t, entry - 0.001f));    // below

        // Never "nothing", at any speed at all, including the ones a doctored state could carry.
        foreach (float speed in new[] { 0f, 0.001f, 2.43f, 5.40f, entry, 8.64f, 1e6f, float.NaN })
            Assert.True(AvatarMotor.EntryVerbFor(t, speed) is MoveVerb.Slide or MoveVerb.Tuck);

        // And the same convention the skid uses, so "at or above" means one thing in the motor.
        Assert.True(AvatarMotor.ShouldEnterSkid(true, false,
            Flat(0f, -t.SkidEnterSpeedMps), Flat(0f, 1f)));
    }

    /// <summary><b>A held jog jump lands in a TUCK, not a slide</b> (§4.3's stated knock-on): the
    /// airborne ceiling makes a jog jump land at exactly <c>MoveSpeed</c> = 5.40, which is below
    /// 6.588. Only sprint slides. Stated in the spec as arithmetic, asserted here so it cannot drift
    /// into a surprise.</summary>
    [Fact]
    public void AHeldJogJumpLandsInATuck_BecauseOnlySprintSlides()
    {
        MotorTuning t = Base;
        Assert.Equal(MoveVerb.Tuck, AvatarMotor.EntryVerbFor(t, t.MoveSpeed));
        Assert.Equal(MoveVerb.Slide, AvatarMotor.EntryVerbFor(t, t.MoveSpeed * t.SprintMultiplier));
    }

    /// <summary><b>The anti-chatter gap holds at every legal tuning</b> — the pin on knobs 33 and
    /// 34, stated as the inequality it actually is rather than as a number. A hand-edited file that
    /// collapsed the gap is lowered by the validator, not accepted.</summary>
    [Fact]
    public void TheSlidesAntiChatterGapHolds_AtEveryLegalTuning()
    {
        foreach (float exit in new[] { 0.10f, 2.00f, 5.99f, 6.00f, 50f })
        foreach (float fraction in new[] { 0.50f, 1.22f, 2.00f })
        foreach (float moveSpeed in new[] { 1.0f, 5.4f, 12.0f })
        {
            MotorTuning t = MotorTuning.Validate(Base with
            {
                SlideExitSpeedMps = exit,
                SlideEnterSpeedFraction = fraction,
                MoveSpeed = moveSpeed,
            }, out _);
            Assert.True(t.SlideExitSpeedMps < t.SlideEnterSpeedMps,
                $"exit {t.SlideExitSpeedMps} is not below entry {t.SlideEnterSpeedMps}");
        }
    }

    /// <summary><b>The slide's two duration exits cannot overlap at any legal tuning</b> — the pin
    /// on knobs 37 and 38, and what makes §3.4's "cannot overlap" row true rather than
    /// aspirational.</summary>
    [Fact]
    public void TheSlidesTwoDurationExitsCannotOverlap_AtEveryLegalTuning()
    {
        foreach (float min in new[] { 0.00f, 0.12f, 0.50f, 9f })
        foreach (float max in new[] { 0.10f, 1.40f, 3.00f, -1f })
        {
            MotorTuning t = MotorTuning.Validate(Base with { SlideMinSec = min, SlideMaxSec = max },
                out _);
            Assert.True(t.SlideMinSec < t.SlideMaxSec, $"min {t.SlideMinSec} max {t.SlideMaxSec}");
            Assert.True(t.SlideMinTicks < t.SlideMaxTicks,
                $"ticks {t.SlideMinTicks} / {t.SlideMaxTicks}");
        }
    }

    /// <summary>
    /// <b>The slide always ends, at every legal tuning — including the two sliders that produce an
    /// exitless state only in combination.</b> MECHANICS §2's real requirement, and the reason
    /// <c>SlideDeceleration</c>'s floor and <c>SlideMaxSec</c>'s ceiling are structural rather than
    /// taste: at decel 0 the speed exit never fires, and only the duration cap ends the state.
    /// </summary>
    [Theory]
    [InlineData(0.5f, 3.00f)]       // slowest decay, longest cap — the worst legal case
    [InlineData(0.5f, 0.10f)]
    [InlineData(40.0f, 3.00f)]
    [InlineData(6.0f, 1.40f)]       // shipped
    public void ASlideAlwaysEnds_EvenAtTheWorstLegalCombinationOfSliders(float decel, float maxSec)
    {
        MotorTuning t = MotorTuning.Validate(Base with
        {
            SlideDeceleration = decel,
            SlideMaxSec = maxSec,
        }, out _);

        var v = Flat(0f, -12f);
        var r = new VerbResolution(MoveVerb.Slide, 0);
        int ticks = 0;
        while (r.Verb == MoveVerb.Slide && ticks < 3000)
        {
            v = AvatarMotor.StepSlide(t, v, Flat(0f, -1f), Dt);
            r = AvatarMotor.StepVerb(t, r.Verb, r.ClockTicks, grounded: true, locked: false,
                jumped: false, jumpHeld: true, WaterState.Dry, v, Flat(0f, -1f));
            ticks++;
        }
        Assert.NotEqual(MoveVerb.Slide, r.Verb);
        Assert.True(ticks * Dt <= t.SlideMaxSec + Dt,
            $"the slide ran {ticks * Dt:0.000} s past a {t.SlideMaxSec:0.000} s cap");
    }

    /// <summary>
    /// <b>§4.5's min-duration floor blocks the SELF-termination and never the release.</b> That
    /// distinction is the whole reason the bound is legal under "weight is skin, not friction":
    /// releasing the button exits on the next tick, from any state, at any time. The positive
    /// control is the same tick with the button still held, which does NOT exit.
    /// </summary>
    [Fact]
    public void TheSlideMinimumBlocksOnlyTheSelfTermination_NeverTheRelease()
    {
        MotorTuning t = Base;
        float belowExit = t.SlideExitSpeedMps - 0.5f;

        // Inside the floor and below the exit speed: held, the slide continues (the floor bites).
        VerbResolution held = Ground(t, MoveVerb.Slide, 0, held: true, belowExit);
        Assert.Equal(MoveVerb.Slide, held.Verb);

        // The same tick, released: gone. The floor cannot hold a player anywhere.
        Assert.Equal(MoveVerb.Normal, Ground(t, MoveVerb.Slide, 0, held: false, belowExit).Verb);
    }

    /// <summary>
    /// <b>The carve is a ROTATION, so it preserves speed exactly and cannot fight the
    /// deceleration</b> (§4.4 rule 3). Two properties, both structural: the speed after a carving
    /// tick equals the speed after a straight tick, and the stick contributes no acceleration and
    /// no braking (rule 2) — pointing the stick backwards does not shorten the slide by one tick.
    /// </summary>
    [Fact]
    public void TheCarveIsARotation_AndTheStickNeitherAcceleratesNorBrakes()
    {
        MotorTuning t = Base;
        var v = Flat(0f, -8.64f);

        float straight = new Vector2(AvatarMotor.StepSlide(t, v, Flat(0f, -1f), Dt).X,
            AvatarMotor.StepSlide(t, v, Flat(0f, -1f), Dt).Z).Length();
        float carving = new Vector2(AvatarMotor.StepSlide(t, v, Flat(1f, 0f), Dt).X,
            AvatarMotor.StepSlide(t, v, Flat(1f, 0f), Dt).Z).Length();
        float braking = new Vector2(AvatarMotor.StepSlide(t, v, Flat(0f, 1f), Dt).X,
            AvatarMotor.StepSlide(t, v, Flat(0f, 1f), Dt).Z).Length();
        float noStick = new Vector2(AvatarMotor.StepSlide(t, v, Vector3.Zero, Dt).X,
            AvatarMotor.StepSlide(t, v, Vector3.Zero, Dt).Z).Length();

        Near(straight, carving, 1e-4f, "carving vs straight");
        Near(straight, braking, 1e-4f, "stick-back vs straight");
        Near(straight, noStick, 1e-4f, "no-stick vs straight");

        // And the shed rate is SlideDeceleration, not Deceleration and not SkidDeceleration.
        Near(8.64f - t.SlideDeceleration * Dt, straight, 1e-4f, "one tick of slide decay");
    }

    // =============================================================================================
    // 4. The chain jump (AC 4) — §6.
    // =============================================================================================

    /// <summary>
    /// <b>AC 4, both halves.</b> The chain raises the WISH and not the takeoff velocity — proved by
    /// the absence half, that setting a depth changes no velocity at all — and a chained jump none
    /// the less lands faster than an unchained one, proved over a REAL FLIGHT rather than at
    /// takeoff, which is the half a velocity bonus would also have passed.
    ///
    /// <para><b>Why the flight is the part that matters</b> (§6.1): with a velocity bonus, the
    /// airborne ceiling resolves <c>desired</c> back to the unchained sprint wish and brakes the
    /// body down to it at 4.05 m/s², shedding up to 2.90 m/s over a 0.717 s flight — the whole bonus
    /// and then some. A test that only checked the launch tick would have passed the broken
    /// model.</para>
    /// </summary>
    [Fact]
    public void TheChainRaisesTheWishAndNotTheVelocity_AndSurvivesAWholeFlight()
    {
        MotorTuning t = Chained;

        // The absence half: a chain depth is not a velocity event. Nothing in the chain's own
        // resolution touches a velocity, and the ladder is the only thing that changes on a jump.
        ChainResolution r = AvatarMotor.StepChain(t, prevDepth: 0, prevTimerTicks: 10,
            grounded: true, touchdown: false, jumped: true, locked: false);
        Assert.Equal(1, (int)r.Depth);

        // The presence half, over a real flight: run a sprint hop chain and compare landing speeds.
        float unchained = LandingSpeedAfterAHop(t, depth: 0);
        float chained = LandingSpeedAfterAHop(t, depth: 3);

        Assert.True(chained > unchained + 0.25f,
            $"a depth-3 chain landed at {chained:0.000} m/s against an unchained {unchained:0.000} "
            + "— the chain is not surviving the flight");

        // And at the shipped ChainBonusMps of 0.00 the two are the SAME number, bit for bit.
        Assert.Equal(LandingSpeedAfterAHop(Base, depth: 0), LandingSpeedAfterAHop(Base, depth: 3));
    }

    /// <summary>
    /// One hop at sprint from a running start: ground dwell at the chained wish, then a launch, then
    /// a full flight under the shipped air block. Returns the horizontal speed at touchdown.
    ///
    /// <para>Drives the shipped pure functions — <c>RateFor</c>, <c>AirborneWishSpeed</c>,
    /// <c>GravityFor</c> — rather than re-deriving the model, which is the practice
    /// <c>AirborneControlTests</c>'s own simulators established.</para>
    /// </summary>
    private static float LandingSpeedAfterAHop(in MotorTuning t, byte depth)
    {
        float ground = t.MoveSpeed;
        float sprintWish = ground * t.SprintMultiplier + depth * t.ChainBonusMps;

        // Ground dwell: the chain grace is 0.35 s of running between hops, and the raised wish is
        // what the body accelerates toward while it is down there.
        var v = Flat(0f, -ground * t.SprintMultiplier);
        Vector3 wish = Flat(0f, -sprintWish);
        for (int i = 0; i < t.ChainGraceTicks; i++)
        {
            float rate = AvatarMotor.RateFor(v, wish, grounded: true);
            v = new Vector3(v.X, 0f, Mathf.MoveToward(v.Z, wish.Z, rate * Dt));
        }

        // The launch tick runs GROUND rates (prev.Grounded is still true), then the flight.
        float vy = t.JumpVelocity;
        float y = vy * Dt;
        int ticks = 0;
        while (y > 0f && ticks < 600)
        {
            float speed = AvatarMotor.AirborneWishSpeed(sprintWish, ground, v,
                momentumGranted: depth > 0);
            Vector3 airWish = Flat(0f, -speed);
            float rate = AvatarMotor.RateFor(v, airWish, grounded: false);
            v = new Vector3(v.X, 0f, Mathf.MoveToward(v.Z, airWish.Z, rate * Dt));
            vy -= MotorTuningInvariants.GravityFor(t, vy, jumpHeld: true) * Dt;
            y += vy * Dt;
            ticks++;
        }
        return new Vector2(v.X, v.Z).Length();
    }

    /// <summary>
    /// <b>§6.4's counter, all four of its behaviours.</b> Reloaded on touchdown; frozen while
    /// airborne so a long flight can never break a chain; decremented on grounded ticks; and at zero
    /// it takes ONE level and reloads, rather than resetting the chain.
    /// </summary>
    [Fact]
    public void TheChainTimerReloadsOnTouchdown_FreezesInTheAir_AndDecaysOneLevelAtATime()
    {
        MotorTuning t = Chained;

        // Reloaded on touchdown.
        Assert.Equal(t.ChainGraceTicks, (int)AvatarMotor.StepChain(t, 2, 0, grounded: true,
            touchdown: true, jumped: false, locked: false).TimerTicks);

        // Frozen while airborne — a hundred airborne ticks cost nothing.
        var r = new ChainResolution(3, 5);
        for (int i = 0; i < 100; i++)
            r = AvatarMotor.StepChain(t, r.Depth, r.TimerTicks, grounded: false, touchdown: false,
                jumped: false, locked: false);
        Assert.Equal(3, (int)r.Depth);
        Assert.Equal(5, (int)r.TimerTicks);

        // One level at a time, never a reset to zero.
        ChainResolution expiring = AvatarMotor.StepChain(t, 3, 1, grounded: true, touchdown: false,
            jumped: false, locked: false);
        Assert.Equal(2, (int)expiring.Depth);
        Assert.Equal(t.ChainDecayTicks, (int)expiring.TimerTicks);

        // At depth 0 it stops rather than underflowing.
        Assert.Equal(0, (int)AvatarMotor.StepChain(t, 0, 1, grounded: true, touchdown: false,
            jumped: false, locked: false).Depth);
    }

    /// <summary>
    /// <b>The buffered chain works, and it works because of one ordering.</b> A buffered jump fires
    /// on the touchdown tick itself, so §6.3's "the timer is set to the grace on the following
    /// touchdown" has to be applied BEFORE the accrual reads it. Reading a stale timer there would
    /// make every mashed hop start a fresh chain — the chain would simply never accrue, silently.
    /// </summary>
    [Fact]
    public void ABufferedJumpOnTheTouchdownTickChains_BecauseTheGraceIsReloadedFirst()
    {
        MotorTuning t = Chained;
        ChainResolution r = AvatarMotor.StepChain(t, prevDepth: 0, prevTimerTicks: 0,
            grounded: true, touchdown: true, jumped: true, locked: false);
        Assert.Equal(1, (int)r.Depth);
    }

    /// <summary><b>MECHANICS §4, idempotency: the chain cannot double-increment.</b> A grounded tick
    /// must intervene between any two jumps, and the depth changes only on a jump tick — so the
    /// ladder is a function of the number of jumps, not of how long the button was held.</summary>
    [Fact]
    public void TheChainCapsAtItsMaxDepth_AndCannotDoubleIncrementOnOneJump()
    {
        MotorTuning t = Chained;
        var r = new ChainResolution(0, 10);
        for (int i = 0; i < 20; i++)
            r = AvatarMotor.StepChain(t, r.Depth, 10, grounded: true, touchdown: false,
                jumped: true, locked: false);
        Assert.Equal((int)t.ChainMaxDepth, (int)r.Depth);

        // One jump is one level, never two — the ladder is a function of the number of jumps, not
        // of how long the button was held.
        Assert.Equal(1, (int)AvatarMotor.StepChain(t, 0, 10, grounded: true, touchdown: false,
            jumped: true, locked: false).Depth);
    }

    /// <summary><b>A control lock zeroes the chain, the counter and the verb together</b> (§3.2's
    /// control-lock row) — no new state survives a shove, a freeze, a sputter-out or a
    /// blast.</summary>
    [Fact]
    public void AControlLockZeroesTheChainAndTheAirJumpCounter()
    {
        MotorTuning t = Chained;
        Assert.Equal(new ChainResolution(0, 0), AvatarMotor.StepChain(t, 4, 20, grounded: true,
            touchdown: false, jumped: false, locked: true));

        AirJumpResolution air = AvatarMotor.StepAirJump(t with { AirJumpMode = 1f },
            Flat(0f, -8f), grounded: false, prevAirJumpsUsed: 1, jumpEdge: true, jumpAllowed: true,
            locked: true, sprintWish: 8.64f);
        Assert.Equal(0, (int)air.AirJumpsUsed);
        Assert.False(air.Fired);
    }

    // =============================================================================================
    // 5. The double jump (AC 5) — §7.
    // =============================================================================================

    /// <summary><b>Both variants exist behind ONE toggle, and mode 0 is still its exact no-op</b> —
    /// the velocity it was handed, a counter that is already zero, and nothing fired, at every fall
    /// speed and every horizontal speed.
    ///
    /// <para><b>MOVE-8: the toggle no longer ships OFF.</b> Talon ruled the traditional double jump
    /// at the keyboard on 2026-08-28 — <i>"the jump plus double jump works a little bit more than
    /// the jump plus kick"</i>, SHIFT+1 against SHIFT+2 on an identical base — so
    /// <c>AirJumpMode</c> ships at 1. The no-op claim is unchanged and is now asserted on a
    /// candidate tuning; the SHIPPED mode is asserted separately, because "which mode ships" is a
    /// ruling and deserves a line that says so.</para></summary>
    [Fact]
    public void ModeZeroIsStillTheExactNoOp_ThoughItIsNoLongerWhatShips()
    {
        Assert.Equal(1f, MotorTuning.Default.AirJumpMode);
        MotorTuning off = Base with { AirJumpMode = 0f };

        foreach (float vy in new[] { -12f, -5.94f, 0f, 4f })
        foreach (float vz in new[] { 0f, -2.85f, -6.08f })
        {
            var v = new Vector3(0f, vy, vz);
            AirJumpResolution r = AvatarMotor.StepAirJump(off, v, grounded: false,
                prevAirJumpsUsed: 0, jumpEdge: true, jumpAllowed: true, locked: false,
                sprintWish: 6.08f);
            Assert.False(r.Fired);
            Assert.Equal(v, r.Velocity);
            Assert.Equal(0, (int)r.AirJumpsUsed);
        }

        // The positive control: the same inputs at mode 1 and mode 2 DO fire, so the absence above
        // is a real absence rather than a broken call.
        Assert.True(AvatarMotor.StepAirJump(Base with { AirJumpMode = 1f }, new Vector3(0, -5.94f, -6.08f),
            false, 0, true, true, false, 6.08f).Fired);
        Assert.True(AvatarMotor.StepAirJump(Base with { AirJumpMode = 2f }, new Vector3(0, -5.94f, -6.08f),
            false, 0, true, true, false, 6.08f).Fired);
    }

    /// <summary><b>Variant 1 overwrites <c>velocity.Y</c>, which is precisely the reading variant 2
    /// exists to answer</b> — all accumulated downward momentum deleted in one frame, the single
    /// most weightless thing a body can do, and it works at zero horizontal speed and at any moment
    /// of the fall.</summary>
    [Fact]
    public void VariantOneOverwritesTheFall_AndWorksEvenAtAStandstill()
    {
        MotorTuning t = Base with { AirJumpMode = 1f };
        AirJumpResolution r = AvatarMotor.StepAirJump(t, new Vector3(0f, -12f, 0f), grounded: false,
            prevAirJumpsUsed: 0, jumpEdge: true, jumpAllowed: true, locked: false,
            sprintWish: 8.64f);

        Assert.True(r.Fired);
        Near(t.JumpVelocity * t.AirJumpVelocityFraction, r.Velocity.Y, 1e-4f,
            "variant 1 launch");
        Assert.Equal(1, (int)r.AirJumpsUsed);
    }

    /// <summary>
    /// <b>The Kick's three conditions, each load-bearing, each asserted.</b> It cannot be taken
    /// while rising, it cannot be taken below a committed run, and it cannot be taken with the
    /// counter spent — which together are why it <i>structurally cannot perform the mid-air undo</i>
    /// that is the traditional variant's floaty read.
    /// </summary>
    [Fact]
    public void TheKickCannotSaveYou_ItNeedsAFallAndACommittedRun()
    {
        MotorTuning t = Base with { AirJumpMode = 2f };

        // Rising: refused.
        Assert.False(AvatarMotor.StepAirJump(t, new Vector3(0f, 4f, -8.64f), false, 0, true, true,
            false, 8.64f).Fired);
        // Descending but below the committed-run floor: refused, so a standing hop has no second jump.
        Assert.False(AvatarMotor.StepAirJump(t, new Vector3(0f, -5.94f, -(t.KickMinSpeedMps - 0.01f)),
            false, 0, true, true, false, 8.64f).Fired);
        // At the floor exactly: allowed — the same inclusive convention every threshold here uses.
        Assert.True(AvatarMotor.StepAirJump(t, new Vector3(0f, -5.94f, -t.KickMinSpeedMps),
            false, 0, true, true, false, 8.64f).Fired);
        // Counter spent: refused.
        Assert.False(AvatarMotor.StepAirJump(t, new Vector3(0f, -5.94f, -8.64f), false,
            (byte)t.AirJumpCountMax, true, true, false, 8.64f).Fired);
    }

    /// <summary>
    /// <b>AC 5's demonstration: the Kick CANNOT compound — shown by attempting to compound it.</b>
    /// The naive <c>flatSpeed += gain</c> adds up to 4.54 m/s per flight against a 0.15 m/s
    /// per-tick ground cost, so speed grows without bound. The shipped
    /// <c>max(flatSpeed, sprintWish + gain)</c> caps the OUTPUT at the ceiling, so <b>the Kick is
    /// how you reach the Kick's speed, not how you exceed it.</b>
    ///
    /// <para>Twenty Kicks in a row, each at the same fall speed and with the counter reset each time
    /// — the most favourable possible conditions for a runaway — and the speed after the twentieth
    /// equals the speed after the first, exactly.</para>
    /// </summary>
    [Fact]
    public void TheKickCannotCompound_DemonstratedByAttemptingToCompoundIt()
    {
        MotorTuning t = Base with { AirJumpMode = 2f };
        const float sprintWish = 8.64f;
        const float fall = -5.94f;

        var v = new Vector3(0f, fall, -sprintWish);
        float first = 0f;
        for (int i = 0; i < 20; i++)
        {
            AirJumpResolution r = AvatarMotor.StepAirJump(t, v, grounded: false,
                prevAirJumpsUsed: 0, jumpEdge: true, jumpAllowed: true, locked: false,
                sprintWish: sprintWish);
            Assert.True(r.Fired);
            v = r.Velocity with { Y = fall };       // hand it the same fall back, every time
            float speed = new Vector2(v.X, v.Z).Length();
            if (i == 0)
                first = speed;
            else
                Near(first, speed, 1e-4f, $"speed after Kick {i + 1} vs after the first");
        }

        // And the ceiling is exactly the one §7.2 states: sprintWish + gain.
        float gain = t.KickHorizontalGainMps + Mathf.Abs(fall) * t.KickConversionFraction;
        Near(sprintWish + gain, first, 1e-3f, "the Kick's ceiling");

        // The Kick arrests the fall; it does not climb.
        Near(t.KickVerticalMps, AvatarMotor.StepAirJump(t, new Vector3(0f, fall, -sprintWish),
            false, 0, true, true, false, sprintWish).Velocity.Y, 1e-4f,
            "the Kick's vertical");
        Assert.True(t.KickVerticalMps < t.JumpVelocity);
    }

    /// <summary><b>The Kick's gain goes along the CURRENT heading; the stick cannot turn it.</b>
    /// §7.2 point 3: mid-air control is what reads as light, so the Kick removes some — it makes the
    /// arc MORE ballistic, not less.</summary>
    [Fact]
    public void TheKicksGainGoesAlongTheCurrentHeading()
    {
        MotorTuning t = Base with { AirJumpMode = 2f };
        var before = new Vector3(3f, -5.94f, -4f);
        Vector3 after = AvatarMotor.StepAirJump(t, before, false, 0, true, true, false,
            8.64f).Velocity;

        var b = new Vector2(before.X, before.Z).Normalized();
        var a = new Vector2(after.X, after.Z).Normalized();
        Near(b.X, a.X, 1e-4f, "heading X");
        Near(b.Y, a.Y, 1e-4f, "heading Z");
    }

    /// <summary><b>The two WIRE-WIDTH knobs are clamped by the VALIDATOR, not by a widget</b>
    /// (§11.3). A slider past them does not produce a bad feel; it produces a truncated field and a
    /// peer whose depth disagrees with the server's, so a hand-edited JSON file must not get past
    /// them either.</summary>
    [Fact]
    public void TheTwoWireWidthKnobsAreClampedByTheValidator_NotByAWidget()
    {
        MotorTuning t = MotorTuning.Validate(
            Base with { ChainMaxDepth = 99f, AirJumpCountMax = 99f }, out IReadOnlyList<string> w);

        Assert.Equal(7f, t.ChainMaxDepth);
        Assert.Equal(3f, t.AirJumpCountMax);
        Assert.Contains(w, n => n.Contains("ChainMaxDepth", StringComparison.Ordinal));
        Assert.Contains(w, n => n.Contains("AirJumpCountMax", StringComparison.Ordinal));

        // And the resolutions never exceed the widths whatever they are handed.
        ChainResolution c = AvatarMotor.StepChain(t, prevDepth: 255, prevTimerTicks: 10,
            grounded: true, touchdown: false, jumped: true, locked: false);
        Assert.True(c.Depth <= 7);

        AirJumpResolution a = AvatarMotor.StepAirJump(t with { AirJumpMode = 1f },
            Flat(0f, -8f), grounded: false, prevAirJumpsUsed: 255, jumpEdge: true,
            jumpAllowed: true, locked: false, sprintWish: 8.64f);
        Assert.True(a.AirJumpsUsed <= 3);
    }

    // =============================================================================================
    // 6. The momentum share (§8) and §9's amendment (AC 6).
    // =============================================================================================

    /// <summary>
    /// <b>§8's momentum share, complete.</b> Entering a Slide sets the skid timer to zero and
    /// touches the velocity vector <i>not at all</i> — magnitude, direction and every bit of
    /// sideways component the skid was carrying pass straight through, and the slide then
    /// decelerates them at 6.0 instead of 13. There is no momentum to transfer, because there is
    /// only one velocity vector and two states that treat it differently.
    /// </summary>
    [Fact]
    public void EnteringASlideCancelsTheSkid_AndLeavesTheVelocityUntouched()
    {
        MotorTuning t = Base;
        var carried = new Vector3(2.5f, 0f, -7.5f);

        // The share: the timer goes to zero on the entry tick.
        Assert.Equal(0f, AvatarMotor.StepSkid(t.SkidMaxSec, grounded: true, locked: false,
            carried, Flat(0f, 8f), Dt, MoveVerb.Slide));

        // The positive control: the identical call in Normal keeps the skid running, so the zero
        // above is the verb clause and not an unrelated exit.
        Assert.True(AvatarMotor.StepSkid(t.SkidMaxSec, grounded: true, locked: false,
            carried, Flat(0f, 8f), Dt, MoveVerb.Normal) > 0f);

        // And the velocity is untouched: the slide's first tick starts from the skid's own heading.
        Vector3 next = AvatarMotor.StepSlide(t, carried, Vector3.Zero, Dt);
        var beforeDir = new Vector2(carried.X, carried.Z).Normalized();
        var afterDir = new Vector2(next.X, next.Z).Normalized();
        Near(beforeDir.X, afterDir.X, 1e-4f, "carried heading X");
        Near(beforeDir.Y, afterDir.Y, 1e-4f, "carried heading Z");
    }

    /// <summary>
    /// <b>AC 6 — §9's momentum grant is an EXACT no-op at the shipped defaults, proven rather than
    /// asserted, with its positive control.</b>
    ///
    /// <para><b>The absence half</b> sweeps the amended function against the pre-amendment formula,
    /// re-derived here from MOVE-3a §2.3's own line rather than called, over a grid of requests,
    /// ground wishes and carried speeds — and they agree bit for bit whenever the gate is
    /// false.</para>
    ///
    /// <para><b>The positive control</b> is the same sweep with the gate true, which finds cases
    /// where the two differ. Without it, "they agree" would be worth nothing: a method that always
    /// returned its first argument would also have passed the absence half.</para>
    /// </summary>
    [Fact]
    public void TheMomentumGrantIsAnExactNoOpWhenItsGateIsFalse_AndAControlShowsItWouldSeeAChange()
    {
        float[] requests = { 0f, 2.43f, 5.40f, 8.64f, 9.24f, 11.04f, 15.58f };
        float[] grounds = { 2.97f, 5.40f };
        float[] carried = { 0f, 4.05f, 5.40f, 8.64f, 9.55f, 12f };

        int differences = 0;
        foreach (float req in requests)
        foreach (float ground in grounds)
        foreach (float flat in carried)
        {
            var v = Flat(0f, -flat);

            // MOVE-3a §2.3, exactly as it read before the amendment.
            float preAmendment = Mathf.Min(req, Mathf.Max(flat, ground));

            Near(preAmendment,
                AvatarMotor.AirborneWishSpeed(req, ground, v, momentumGranted: false), 1e-6f,
                $"gate false at req {req} ground {ground} flat {flat}");

            // Every existing call site omits the argument, so the DEFAULT must be the no-op too.
            Near(preAmendment, AvatarMotor.AirborneWishSpeed(req, ground, v), 1e-6f,
                $"default argument at req {req} ground {ground} flat {flat}");

            if (Math.Abs(AvatarMotor.AirborneWishSpeed(req, ground, v, momentumGranted: true)
                         - preAmendment) > 1e-4f)
                differences++;
        }

        Assert.True(differences > 0,
            "the positive control found no case where the grant changes the answer — the absence "
            + "half above is therefore worthless, because the method may not be able to see a "
            + "change at all");
    }

    /// <summary>
    /// <b>The grant's gate is false at the shipped defaults on every tick of a ten-hop chain — and
    /// this test is the one that falsified spec §9's stated reason for believing it.</b>
    ///
    /// <para>§9 justifies its no-op with "<c>ChainBonusMps = 0.00</c> means <c>ChainDepth</c> never
    /// leaves 0". <b>The depth demonstrably DOES leave 0 at the shipped defaults</b> — §6.3's
    /// accrual does not consult the bonus — and that is asserted below rather than worked around
    /// silently, so the discrepancy is visible to the next reader instead of being rediscovered.
    /// <see cref="AvatarMotor.MomentumGranted"/> carries the correction and the argument for it.
    /// </para>
    ///
    /// <para>The positive control is the same loop with the chain switched on, which does raise the
    /// gate — without it, "the gate was never true" would pass just as well for a predicate
    /// hard-wired to false.</para>
    /// </summary>
    [Fact]
    public void AtTheShippedDefaultsTheGateIsNeverTrue_EvenThoughTheDepthDoesLeaveZero()
    {
        MotorTuning t = Base;
        var chain = new ChainResolution(0, 0);
        var air = new AirJumpResolution(Vector3.Zero, 0, false);
        bool depthLeftZero = false;

        for (int i = 0; i < 600; i++)
        {
            bool grounded = i % 60 < 20;
            bool jump = i % 60 == 19;
            chain = AvatarMotor.StepChain(t, chain.Depth, chain.TimerTicks, grounded,
                touchdown: i % 60 == 0, jumped: jump, locked: false);
            air = AvatarMotor.StepAirJump(t, new Vector3(0f, -6f, -8.64f), grounded,
                air.AirJumpsUsed, jumpEdge: jump, jumpAllowed: true, locked: false,
                sprintWish: 8.64f);

            depthLeftZero |= chain.Depth > 0;
            Assert.False(AvatarMotor.MomentumGranted(t, chain.Depth, air.AirJumpsUsed),
                $"the momentum grant's gate went true at tick {i} at the SHIPPED defaults");
        }

        // The finding, asserted rather than merely described: spec §9's stated reason is false.
        Assert.True(depthLeftZero,
            "ChainDepth stayed at zero through ten hops at the shipped defaults — if that has "
            + "become true, spec §9's justification is now correct and MomentumGranted's extra "
            + "ChainBonusMps term has stopped being load-bearing. Re-read both before removing it.");

        // The positive control: the same loop with the chain switched on does raise the gate.
        MotorTuning on = Chained;
        var c = new ChainResolution(0, 0);
        bool everTrue = false;
        for (int i = 0; i < 600; i++)
        {
            c = AvatarMotor.StepChain(on, c.Depth, c.TimerTicks, grounded: i % 60 < 20,
                touchdown: i % 60 == 0, jumped: i % 60 == 19, locked: false);
            everTrue |= AvatarMotor.MomentumGranted(on, c.Depth, 0);
        }
        Assert.True(everTrue, "the control never raised the gate — the absence check above proves "
                            + "nothing");

        // And an air jump raises it on its own, with no chain at all.
        Assert.True(AvatarMotor.MomentumGranted(Base, chainDepth: 0, airJumpsUsed: 1));
    }

    // =============================================================================================
    // 7. The wire (AC 7) and the wading guard (AC 8).
    // =============================================================================================

    /// <summary><b>AC 7, all four of its numbers.</b> The snapshot grew by exactly the three bytes
    /// §10.2 costed, the protocol bumped once, the input entry did NOT grow, and no new input bit
    /// was spent — <c>MoveIntent</c> has the same fields it had at v12.</summary>
    [Fact]
    public void TheWireCostIsThreeSnapshotBytes_OneProtocolBump_AndNoInputBitAtAll()
    {
        Assert.Equal(58, NetCodec.SnapshotBytes);
        Assert.Equal(26, NetCodec.InputEntryBytes);
        Assert.Equal(14, NetProfile.ProtocolVersion);

        // Zero new input bits: every verb is carried by three fields that were already on the wire.
        string[] intentFields = typeof(MoveIntent)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "AimPitch", "AimRaise", "AimYaw", "Fire", "Interact", "Jump", "JumpHeld", "MoveDir",
                "Seq", "Sprint", "Throw",
            }.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            intentFields);
    }

    /// <summary><b>All five new fields round-trip, and a stale-length packet is REFUSED rather than
    /// misread.</b> The 55-byte v12 snapshot is the case that matters: a peer that parsed it
    /// cleanly would render a teammate's carve as an upright run and never know.</summary>
    [Fact]
    public void TheFiveVerbFieldsRoundTrip_AndAV12SnapshotIsRefused()
    {
        var snap = new NetCodec.Snapshot(7, 1, 9, new MoveState
        {
            Position = new Vector3(1f, 2f, 3f),
            Velocity = new Vector3(4f, 5f, 6f),
            Grounded = true,
            Verb = MoveVerb.DuckWalk,
            VerbClockTicks = 137,
            ChainDepth = 3,
            ChainTimerTicks = 21,
            AirJumpsUsed = 1,
        });

        byte[] packet = NetCodec.PackSnapshot(snap);
        Assert.Equal(58, packet.Length);

        NetCodec.Snapshot? got = NetCodec.UnpackSnapshot(packet);
        Assert.NotNull(got);
        Assert.Equal(MoveVerb.DuckWalk, got!.Value.State.Verb);
        Assert.Equal(137, (int)got.Value.State.VerbClockTicks);
        Assert.Equal(3, (int)got.Value.State.ChainDepth);
        Assert.Equal(21, (int)got.Value.State.ChainTimerTicks);
        Assert.Equal(1, (int)got.Value.State.AirJumpsUsed);

        Assert.Null(NetCodec.UnpackSnapshot(new byte[55]));      // the v12 length
    }

    /// <summary><b>Every one of the four verbs survives the two-bit field</b>, and the airborne
    /// sentinel survives the clock byte — a value a proxy must read back exactly, or its pose layer
    /// would think a landed teammate had never left the floor.</summary>
    [Theory]
    [InlineData(MoveVerb.Normal)]
    [InlineData(MoveVerb.Tuck)]
    [InlineData(MoveVerb.Slide)]
    [InlineData(MoveVerb.DuckWalk)]
    public void EveryVerbOrdinalRoundTrips_AlongWithTheAirborneSentinel(MoveVerb verb)
    {
        var snap = new NetCodec.Snapshot(1, 0, 0, new MoveState
        {
            Verb = verb,
            VerbClockTicks = AvatarMotor.VerbClockAirborne,
        });
        NetCodec.Snapshot? got = NetCodec.UnpackSnapshot(NetCodec.PackSnapshot(snap));
        Assert.NotNull(got);
        Assert.Equal(verb, got!.Value.State.Verb);
        Assert.Equal(AvatarMotor.VerbClockAirborne, (int)got.Value.State.VerbClockTicks);
    }

    /// <summary>
    /// <b>AC 8 — a wading player holding jump does not tuck</b>, and this is the bug the bible check
    /// caught rather than the design. <c>WaterGeometry.JumpAllowed</c> denies the jump while
    /// swimming, so a wading player holding the key is grounded, holding, and firing no jump —
    /// which is the crouch trigger's exact precondition. The body would have tucked in the shallows
    /// and it would have read as a physics glitch.
    ///
    /// <para>The positive control is the identical input on dry land, which does tuck.</para>
    /// </summary>
    [Theory]
    [InlineData(WaterState.Wading)]
    [InlineData(WaterState.Swimming)]
    public void AWadingPlayerHoldingJumpDoesNotTuck(WaterState water)
    {
        MotorTuning t = Base;

        var wet = new VerbResolution(MoveVerb.Normal, 0);
        var dry = new VerbResolution(MoveVerb.Normal, 0);
        for (int i = 0; i < t.JumpHoldWindowTicks * 4; i++)
        {
            wet = AvatarMotor.StepVerb(t, wet.Verb, wet.ClockTicks, grounded: true, locked: false,
                jumped: false, jumpHeld: true, water, Vector3.Zero,
                Flat(0f, -1f));
            dry = AvatarMotor.StepVerb(t, dry.Verb, dry.ClockTicks, grounded: true, locked: false,
                jumped: false, jumpHeld: true, WaterState.Dry, Vector3.Zero,
                Flat(0f, -1f));
        }

        Assert.Equal(MoveVerb.Normal, wet.Verb);        // the guard
        Assert.Equal(MoveVerb.Tuck, dry.Verb);          // the positive control
    }

    /// <summary><b>A wading SPRINTER does not slide either</b> — the same guard, on the branch of
    /// the trigger that would have produced the more visible bug.</summary>
    [Fact]
    public void AWadingSprinterHoldingJumpDoesNotSlide()
    {
        MotorTuning t = Base;
        var wet = new VerbResolution(MoveVerb.Normal, 0);
        for (int i = 0; i < t.JumpHoldWindowTicks * 4; i++)
            wet = AvatarMotor.StepVerb(t, wet.Verb, wet.ClockTicks, grounded: true, locked: false,
                jumped: false, jumpHeld: true, WaterState.Wading,
                Flat(0f, -8.64f), Flat(0f, -1f));
        Assert.Equal(MoveVerb.Normal, wet.Verb);
    }

    // =============================================================================================
    // 8. The touchdown sentinel and the TouchdownSlideImmediate knob (§4.3, ruling 3).
    // =============================================================================================

    /// <summary>
    /// <b>The airborne tick leaves a distinguished clock value, and the touchdown tick consumes
    /// it.</b> That is how T8 is implemented without a sixth <c>MoveState</c> field and without
    /// spending the <c>flags2</c> byte's last spare bit. <b>It is a not-counting value:</b> nothing
    /// compares it against a window, and no counted clock can reach it, because every increment is
    /// clamped one below.
    /// </summary>
    [Fact]
    public void AnAirborneTickLeavesTheSentinel_AndNoCountedClockCanEverReachIt()
    {
        MotorTuning t = Base;

        VerbResolution air = AvatarMotor.StepVerb(t, MoveVerb.Slide, 30, grounded: false,
            locked: false, jumped: false, jumpHeld: true, WaterState.Dry,
            Flat(0f, -8f), Flat(0f, -1f));
        Assert.Equal(MoveVerb.Normal, air.Verb);
        Assert.Equal(AvatarMotor.VerbClockAirborne, (int)air.ClockTicks);

        // Counting for a very long time never reaches it.
        byte clock = 0;
        for (int i = 0; i < 5000; i++)
            clock = AvatarMotor.AdvanceVerbClock(clock, jumpHeld: true);
        Assert.Equal(AvatarMotor.VerbClockAirborne - 1, (int)clock);

        // And no knob range can ask for a window at or above it either.
        foreach (MotorKnob knob in MotorTuningKnobs.All.Where(k => k.Unit == "s"))
        {
            MotorTuning maxed = MotorTuning.Validate(knob.Set(Base, knob.Max), out _);
            foreach (int ticks in new[]
                     {
                         maxed.JumpHoldWindowTicks, maxed.SlideMinTicks, maxed.SlideMaxTicks,
                         maxed.ChainGraceTicks, maxed.ChainDecayTicks,
                     })
                Assert.True(ticks < AvatarMotor.VerbClockAirborne, knob.Name);
        }
    }

    /// <summary><b>Ruling 3: the touchdown case is a KNOB, and both behaviours are built.</b> At 1
    /// the verb starts on the touchdown tick itself; at 0 — the shipped value — that same tick
    /// merely starts the hold window, and the verb is twelve ticks away.</summary>
    [Fact]
    public void TouchdownSlideImmediate_BuildsBothBehavioursBehindOneKnob()
    {
        VerbResolution waits = AvatarMotor.StepVerb(Base, MoveVerb.Normal,
            AvatarMotor.VerbClockAirborne, grounded: true, locked: false,
            jumped: false, jumpHeld: true, WaterState.Dry, Flat(0f, -8.64f), Flat(0f, -1f));
        Assert.Equal(MoveVerb.Normal, waits.Verb);
        Assert.Equal(1, (int)waits.ClockTicks);

        MotorTuning now = Base with { TouchdownSlideImmediate = 1f };
        VerbResolution slides = AvatarMotor.StepVerb(now, MoveVerb.Normal,
            AvatarMotor.VerbClockAirborne, grounded: true, locked: false,
            jumped: false, jumpHeld: true, WaterState.Dry, Flat(0f, -8.64f), Flat(0f, -1f));
        Assert.Equal(MoveVerb.Slide, slides.Verb);

        // Below the entry speed the same knob produces a Tuck, so neither side has a dead zone.
        VerbResolution tucks = AvatarMotor.StepVerb(now, MoveVerb.Normal,
            AvatarMotor.VerbClockAirborne, grounded: true, locked: false,
            jumped: false, jumpHeld: true, WaterState.Dry, Flat(0f, -1f), Flat(0f, -1f));
        Assert.Equal(MoveVerb.Tuck, tucks.Verb);
    }

    /// <summary><b>The settle cone is the brief's "hold FORWARD through the slide", and the
    /// guaranteed variant deletes the condition</b> (§5.2). Both are built; 0 ships, because the
    /// brief's sentence has a condition in it.</summary>
    [Fact]
    public void TheSettleConeIsTheBriefsForwardCondition_AndTheGuaranteedVariantDeletesIt()
    {
        MotorTuning cone = Base;                            // DuckWalkGuaranteed = 0, cone 60
        MotorTuning always = Base with { DuckWalkGuaranteed = 1f };
        var travel = Flat(0f, -6f);

        Assert.True(AvatarMotor.SlideSettles(cone, travel, Flat(0f, -1f)));      // dead ahead
        Assert.False(AvatarMotor.SlideSettles(cone, travel, Flat(1f, 0f)));      // 90 deg off
        Assert.False(AvatarMotor.SlideSettles(cone, travel, Vector3.Zero));      // no stick
        Assert.True(AvatarMotor.SlideSettles(always, travel, Flat(0f, 1f)));     // deleted

        // And the cone widens monotonically with the knob, so the slider means what it says.
        Assert.False(AvatarMotor.SlideSettles(Base with { DuckWalkEntryConeDeg = 30f },
            travel, Flat(1f, -1f)));
        Assert.True(AvatarMotor.SlideSettles(Base with { DuckWalkEntryConeDeg = 90f },
            travel, Flat(1f, -1f)));
    }

    // =============================================================================================
    // 9. The weight principle, as an INTERACTION constraint over the whole table.
    // =============================================================================================

    /// <summary>
    /// <b>The principle that outranks every mechanic in this wave, asserted as a property of the
    /// whole table rather than of one state: NO verb costs a frame of input responsiveness, and
    /// there is no recovery lockout anywhere.</b>
    ///
    /// <list type="bullet">
    /// <item>A release exits Tuck and Slide on the NEXT tick, from any clock value, at any speed —
    /// there is no minimum, no window and no condition on the pop-up.</item>
    /// <item>A jump press exits every verb on the tick it arrives, the duck walk included.</item>
    /// <item>The Tuck and the DuckWalk cap the wish speed and change nothing else — full
    /// acceleration, full deceleration, full turn rate — which is what <c>RateFor</c> returning the
    /// identical rate for a capped wish demonstrates.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void NoVerbCostsAFrameOfInputResponsiveness_AndThereIsNoRecoveryLockout()
    {
        MotorTuning t = Base;

        foreach (MoveVerb verb in new[] { MoveVerb.Tuck, MoveVerb.Slide })
        foreach (byte clock in new byte[] { 0, 1, 5, 60, 200, 254 })
        foreach (float speed in new[] { 0f, 2.43f, 8.64f, 15f })
            Assert.Equal(MoveVerb.Normal, Ground(t, verb, clock, held: false, speed).Verb);

        foreach (MoveVerb verb in Enum.GetValues<MoveVerb>())
            Assert.Equal(MoveVerb.Normal, AvatarMotor.StepVerb(t, verb, 60, grounded: true,
                locked: false, jumped: true, jumpHeld: true, WaterState.Dry,
                Flat(0f, -6f), Flat(0f, -1f)).Verb);

        // The crouch changes the wish CAP and nothing about the rates that chase it.
        var v = Flat(0f, -2f);
        Near(AvatarMotor.RateFor(v, Flat(0f, -t.DuckWalkSpeedMps), grounded: true),
            AvatarMotor.RateFor(v, Flat(0f, -t.MoveSpeed), grounded: true), 1e-4f,
            "the rate chasing a capped wish vs an uncapped one");
    }

    // =============================================================================================
    // 10. The invariant readout (§7.2, §11.3).
    // =============================================================================================

    /// <summary>
    /// <b>§11.3: "<c>MotorTuning.Validate</c> must clamp these, and <c>MotorTuningInvariants</c>
    /// must report the coupled ones."</b> Both, not either — a silent clamp moves a value without
    /// telling the person who moved it, which is a lab lying to its user. Each row goes red on the
    /// combination that breaches it, which is what makes the readout worth having.
    /// </summary>
    [Fact]
    public void BothSlideCouplingsAreReportedByTheInvariantReadout_AndEachGoesRedWhenBreached()
    {
        IReadOnlyList<MotorInvariant> ok = MotorTuningInvariants.Evaluate(Base);
        Assert.Equal(2, ok.Count(i => i.Name.StartsWith("slide", StringComparison.Ordinal)));
        Assert.All(ok.Where(i => i.Name.StartsWith("slide", StringComparison.Ordinal)),
            i => Assert.True(i.Holds, i.Name));

        // Breached deliberately, bypassing Validate — a hand-edited file is exactly the case the
        // readout exists for, and the clamp is what a panel applies, not what a file arrives in.
        Assert.False(MotorTuningInvariants.Evaluate(Base with { SlideExitSpeedMps = 9f })
            .Single(i => i.Name == "slide exit vs slide entry").Holds);
        Assert.False(MotorTuningInvariants.Evaluate(Base with { SlideMinSec = 2f })
            .Single(i => i.Name == "slide min vs max").Holds);
    }

    /// <summary>
    /// <b>§7.2's composed ceiling is a READOUT ROW, not a fourth knob</b> — "so it cannot drift out
    /// of step". It is a consequence of five sliders, and no number printed beside any one of them
    /// could say what they compose to.
    ///
    /// <para>At the shipped defaults it reads plain sprint speed, because both mechanisms ship off.
    /// With the chain at §6.2's ladder and the Kick on it reaches §7.2's stated figure — and the
    /// fall speed it uses is DERIVED from this tuning's own simulated apex rather than typed, so
    /// moving gravity moves the row instead of stranding it.</para>
    /// </summary>
    [Fact]
    public void TheComposedCeilingIsAReadoutRow_DerivedFromTheTuningRatherThanTyped()
    {
        MotorTuning shipped = Base;
        Near(shipped.MoveSpeed * shipped.SprintMultiplier,
            MotorTuningInvariants.ComposedCeilingMps(shipped), 1e-4f,
            "composed ceiling at the shipped no-ops");

        MotorTuning loud = Base with { ChainBonusMps = 0.60f, AirJumpMode = 2f };
        float ceiling = MotorTuningInvariants.ComposedCeilingMps(loud);

        // Spec §7.2: chain 4 gives an 11.04 m/s sprint wish and the Kick tops it up by
        // 1.20 + 0.35 x 9.55 = 4.54, for 15.58 m/s = 1.80x sprint.
        //
        // MOVE-8: 15.58 -> 13.37. The row is DERIVED and this is the first time anything moved
        // the inputs, so it is worth reading as the proof it works: the sprint wish fell with
        // Talon's speed ruling (8.64 -> 6.08, chain cap unchanged at +2.40), while the Kick's
        // top-up ROSE, because heavier fall gravity means a harder landing means a bigger
        // conversion (1.20 + 0.35 x 10.55 = 4.89, was 4.54). Nothing was retyped. The ceiling as a
        // MULTIPLE of a sprint went UP, 1.80x -> 2.20x, which is the readout doing the job §13's
        // open question O4 wants it for.
        Near(13.374f, ceiling, 0.25f, "the composed ceiling, derived at the ruled tuning");
        Assert.True(ceiling / (loud.MoveSpeed * loud.SprintMultiplier) > 1.7f);

        // Derived, not typed: heavier fall gravity is a harder landing is a bigger Kick.
        Assert.True(
            MotorTuningInvariants.ComposedCeilingMps(loud with { FallGravityMultiplier = 2.0f })
            > ceiling);

        // Present in the readout, and red only on energy-from-nothing.
        Assert.True(MotorTuningInvariants.Evaluate(loud)
            .Single(i => i.Name.StartsWith("composed ceiling", StringComparison.Ordinal)).Holds);
        Assert.False(MotorTuningInvariants.Evaluate(loud with { KickConversionFraction = 1.5f })
            .Single(i => i.Name.StartsWith("composed ceiling", StringComparison.Ordinal)).Holds);
    }

    /// <summary><b>The duck walk costs no speed relative to a walk</b> (§5.3): 0.45 x MoveSpeed is
    /// <c>LocomotionProfile.WalkSpeedMps</c> exactly. "Weight is skin, not friction" means the
    /// crouch's cost is its pose and its low profile, not a tax — and the brief calls it a
    /// legitimate persistent traversal option, which a taxed state is not.</summary>
    [Fact]
    public void TheDuckWalkIsExactlyWalkSpeed_BecauseTheCrouchIsNotATax()
    {
        Near(LocomotionProfile.WalkSpeedMps, Base.DuckWalkSpeedMps, 1e-3f,
            "the duck walk vs the walk gear");
    }

    // =============================================================================================
    // 11. MOVE-8 scope item 2 — the crouch cap reads the SPRINT wish (MOVE-7 §3.2).
    // =============================================================================================

    /// <summary>
    /// <b>MOVE-8 scope item 2: a crouch entered from a sprint keeps its sprint.</b>
    ///
    /// <para><b>The defect, in one line.</b> <c>Step</c> capped the wish at
    /// <c>DuckWalkSpeedFraction × groundWish</c>, and <c>groundWish</c> is the NON-sprint wish. So
    /// the crouch's ceiling was the jog at every value of the fraction: entering a roll at a sprint
    /// braked from 6.08 m/s to at most 3.8 m/s on the entry tick, and no setting of the knob could
    /// buy it back. Talon felt it as <i>"the rolling does not work"</i> and MOVE-7 §3.2 traced it.
    /// The fix is that the cap now reads the wish the body actually had.</para>
    ///
    /// <para><b>The load-bearing case is the fraction at 1.00</b>, because that is the only value
    /// at which the two implementations give visibly different ANSWERS rather than different
    /// ceilings: before the fix a sprint crouch at 1.00 still braked 38%, and after it there is no
    /// loss at all. That is the assertion the packet asks for, and the pre-fix arithmetic is
    /// written out beside it as the negative control, so this test would fail if the old line came
    /// back.</para>
    /// </summary>
    [Fact]
    public void MOVE8_ACrouchEnteredAtASprint_KeepsTheSprintWish()
    {
        MotorTuning t = Base with { DuckWalkSpeedFraction = 1.00f };
        float groundWish = t.MoveSpeed;                        // 3.80
        float sprintWish = groundWish * t.SprintMultiplier;    // 6.08

        foreach (MoveVerb verb in new[] { MoveVerb.Tuck, MoveVerb.DuckWalk })
        {
            // THE PACKET'S ASSERTION: no speed is lost on the entry tick.
            Assert.Equal(sprintWish, AvatarMotor.CrouchCappedWish(t, verb, sprintWish));

            // THE NEGATIVE CONTROL: the pre-MOVE-8 line, written out. It brakes to the jog, and if
            // it ever agrees with the line above then the fix has been reverted and this test has
            // stopped being able to see it.
            float preFix = Mathf.Min(sprintWish, t.DuckWalkSpeedFraction * groundWish);
            Assert.Equal(groundWish, preFix);
            Assert.True(sprintWish - preFix > 2.2f,
                $"the pre-fix cap only cost {sprintWish - preFix:F3} m/s — the control is blind");
        }
    }

    /// <summary>
    /// <b>The cap is a fraction of the wish you had, at every gear and every fraction.</b> The
    /// companion to the test above: it states the RULE rather than the one case, so a fix that
    /// special-cased the sprint instead of reading it would fail here.
    /// </summary>
    [Theory]
    [InlineData(0.10f)]
    [InlineData(0.45f)]   // the shipped fraction
    [InlineData(1.00f)]
    public void MOVE8_TheCrouchCapIsAlwaysThatFractionOfTheWishTheBodyHad(float fraction)
    {
        MotorTuning t = Base with { DuckWalkSpeedFraction = fraction };

        foreach (float wish in new[] { 0f, t.MoveSpeed * 0.45f, t.MoveSpeed,
                                       t.MoveSpeed * t.SprintMultiplier, 12f })
        {
            Near(fraction * wish, AvatarMotor.CrouchCappedWish(t, MoveVerb.DuckWalk, wish), 1e-4f,
                $"the crouch cap at wish {wish:F2}");
            Near(fraction * wish, AvatarMotor.CrouchCappedWish(t, MoveVerb.Tuck, wish), 1e-4f,
                $"the tuck cap at wish {wish:F2}");

            // Every other verb passes the wish through untouched — the cap is the crouch's and
            // nothing else's.
            foreach (MoveVerb other in new[] { MoveVerb.Normal, MoveVerb.Slide })
                Assert.Equal(wish, AvatarMotor.CrouchCappedWish(t, other, wish));
        }

        // The ratio a player feels, at the SHIPPED fraction: a sprint crouch is now 1.6x a jog
        // crouch, where it used to be exactly equal to one. That multiplier IS the fix.
        if (Mathf.Abs(fraction - 0.45f) < 1e-6f)
        {
            float jogCrouch = AvatarMotor.CrouchCappedWish(t, MoveVerb.DuckWalk, t.MoveSpeed);
            float sprintCrouch = AvatarMotor.CrouchCappedWish(t, MoveVerb.DuckWalk,
                t.MoveSpeed * t.SprintMultiplier);
            Near(t.SprintMultiplier, sprintCrouch / jogCrouch, 1e-4f,
                "a sprint crouch against a jog crouch");
            Near(LocomotionProfile.WalkSpeedMps, jogCrouch, 1e-3f,
                "the jog crouch is still exactly the walk gear — §5.3 is untouched by the fix");
        }
    }
}
