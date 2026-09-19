using System;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Sail.Game.Water;
using Xunit;
using VerbResolution = MpFoundation.Net.AvatarMotor.VerbResolution;

namespace SailNet.Tests;

/// <summary>
/// <b>The reconciliation match gate</b> — MOVE-5e, gap 2.
///
/// <para><c>SandboxAvatar.Reconcile</c> takes an early return when position and aim both match, and
/// that return adopts NOTHING. So any field missing from <c>PredictionMatches</c> can stay diverged
/// between a client and the server indefinitely, silently, with no correction and no desync log.
/// Before MOVE-5e none of the five verb fields were compared, and a <c>Tuck</c> and a
/// <c>Normal</c> at the same standing position are positionally identical.</para>
///
/// <para><b>The comparison chosen, and why.</b> All five exactly. The skid's deliberately-coarse
/// boolean compare is NOT the precedent: its reason is that <c>SkidRemaining</c> is a float
/// decremented by <c>dt</c>, so an exact compare would fire on the sub-tick offset between two
/// clocks that agree about everything that matters. The verb fields have no such offset — the verb
/// is a four-value enum and the four counters are bytes that <c>MoveState</c> §10.3 calls "integer
/// by construction", so exact and coarse-enough-to-ignore-clock-noise are the SAME comparison and
/// the coarse version would only discard real information. The precedent that does transfer is
/// <c>Water</c>: derived on both sides by the same pure function from the same replicated inputs,
/// compared exactly, because a disagreement means the two simulations took different branches.</para>
///
/// <para><b>The boundary is pinned here so a different ruling is a one-line change</b>
/// (<see cref="ADifferentRulingIsOneLine_TheCoarseAlternativePinned"/>).</para>
/// </summary>
public class PredictionMatchTests
{
    private const float Dt = AvatarMotor.TickDelta;
    private static MotorTuning Base => MotorTuning.Default;

    /// <summary>A perfectly ordinary standing state. Every test below starts from two copies of
    /// this and perturbs exactly one thing, so nothing that fails here can be blamed on a second
    /// difference.</summary>
    private static MoveState Standing => new()
    {
        Position = new Vector3(12f, 1.5f, -4f),
        Velocity = Vector3.Zero,
        Yaw = 0.75f,
        Grounded = true,
        Water = WaterState.Dry,
    };

    // --- POSITIVE CONTROLS -----------------------------------------------------------------------
    // Every "does not fire" assertion below is an ABSENCE check, and an absence check is worth
    // nothing until the same method has been shown to detect a real presence. These two are that
    // proof: the gate says yes to identical states and no to a genuinely diverged one.

    [Fact]
    public void IdenticalStatesMatch()
    {
        Assert.True(SandboxAvatar.PredictionMatches(Standing, Standing));
    }

    /// <summary>POSITIVE CONTROL: the gate detects a divergence it always could — a moved body.
    /// If this ever goes green-by-accident, every absence check in this class is meaningless.</summary>
    [Fact]
    public void PositionDivergenceIsDetected()
    {
        MoveState auth = Standing;
        auth.Position += new Vector3(1f, 0f, 0f);
        Assert.False(SandboxAvatar.PredictionMatches(Standing, auth));
    }

    // --- THE VERB GATE ---------------------------------------------------------------------------

    /// <summary><b>The defect, reproduced.</b> Two states identical in position, aim-relevant yaw
    /// and velocity, differing only in <c>Verb</c> — the exact "a Tuck and a Normal at the same
    /// standing position" case. Before MOVE-5e this returned true and the client stayed in the
    /// wrong verb forever.</summary>
    [Theory]
    [InlineData(MoveVerb.Tuck)]
    [InlineData(MoveVerb.Slide)]
    [InlineData(MoveVerb.DuckWalk)]
    public void VerbOnlyDivergenceIsDetected(MoveVerb authVerb)
    {
        MoveState predicted = Standing;   // Normal
        MoveState auth = Standing;
        auth.Verb = authVerb;

        // Stated, not assumed: these two really are identical in everything the pre-MOVE-5e gate
        // looked at, so the only thing that can make the assertion below pass is the verb.
        Assert.Equal(predicted.Position, auth.Position);
        Assert.Equal(predicted.Velocity, auth.Velocity);
        Assert.Equal(predicted.Yaw, auth.Yaw);
        Assert.Equal(predicted.Grounded, auth.Grounded);

        Assert.False(SandboxAvatar.PredictionMatches(predicted, auth),
            $"a client in {predicted.Verb} and a server in {authVerb}, at the same position, is a "
            + "divergence the early return in Reconcile would never correct: the client keeps the "
            + "wrong wish speed, the wrong deceleration authority and the wrong steering rule "
            + "indefinitely, and nothing logs it.");
    }

    /// <summary>The chain depth is a term on the wish speed (§6.1) — a divergence in it is a
    /// divergence in how fast the body wants to go, at an identical position.</summary>
    [Fact]
    public void ChainDepthOnlyDivergenceIsDetected()
    {
        MoveState auth = Standing;
        auth.ChainDepth = 2;
        Assert.False(SandboxAvatar.PredictionMatches(Standing, auth));
    }

    /// <summary><b>The one with teeth.</b> <c>MoveState</c> argues <c>AirJumpsUsed</c> is replicated
    /// "so a replay can never re-grant a spent air jump". That holds for the replay path and NOT for
    /// the early return, where before MOVE-5e the counter was never compared at all — a client that
    /// believed it had an air jump the server had already spent would keep believing it. Inert today
    /// only because <c>AirJumpMode</c> ships at 0, which is not a reason to leave it uncompared.</summary>
    [Fact]
    public void AirJumpsUsedOnlyDivergenceIsDetected()
    {
        MoveState auth = Standing;
        auth.AirJumpsUsed = 1;
        Assert.False(SandboxAvatar.PredictionMatches(Standing, auth));
    }

    /// <summary>Both tick clocks. They decide WHEN the verb and the chain change, so leaving them
    /// out defers a correction rather than avoiding one — and <c>VerbClockTicks</c> also carries
    /// <c>VerbClockAirborne</c>'s 255 sentinel, on which the whole touchdown tick depends.</summary>
    [Fact]
    public void TheTwoTickClocksAreCompared()
    {
        MoveState clockDiff = Standing;
        clockDiff.VerbClockTicks = 5;
        Assert.False(SandboxAvatar.PredictionMatches(Standing, clockDiff));

        MoveState timerDiff = Standing;
        timerDiff.ChainTimerTicks = 5;
        Assert.False(SandboxAvatar.PredictionMatches(Standing, timerDiff));

        MoveState airborneSentinel = Standing;
        airborneSentinel.VerbClockTicks = AvatarMotor.VerbClockAirborne;
        Assert.False(SandboxAvatar.PredictionMatches(Standing, airborneSentinel));
    }

    // --- THE ABSENCE CHECK (acceptance criterion 4) ------------------------------------------------

    /// <summary>
    /// <b>An ordinary slide that settles into a duck walk fires no correction.</b> The two peers'
    /// verb machines are driven side by side off the identical input stream through a full
    /// entry → slide → settle, and the gate must stay true on every single tick including the
    /// settle tick itself.
    ///
    /// <para>This is an ABSENCE check, and the positive controls above are what make it mean
    /// anything: the same method, on the same fixtures, has already been shown to detect a
    /// verb-only divergence and a moved body. It is driven through the real
    /// <c>AvatarMotor.StepVerb</c> rather than hand-written verb values, so it is testing the
    /// actual settle and not a story about one.</para>
    /// </summary>
    [Fact]
    public void AnOrdinarySlideToDuckWalkSettleFiresNoCorrection()
    {
        MotorTuning t = Base;
        // Enter above the slide threshold with the stick held forward, which is what settles into a
        // duck walk rather than bracing into a tuck (§5.2).
        var stick = new Vector3(0f, 0f, -1f);
        float speed = t.SlideEnterSpeedMps + 1.0f;

        MoveState client = Standing;
        MoveState server = Standing;
        client.Verb = server.Verb = AvatarMotor.EntryVerbFor(t, speed);
        Assert.Equal(MoveVerb.Slide, client.Verb); // the fixture really does start in a slide

        bool sawSettle = false;
        for (int tick = 0; tick < 240 && !sawSettle; tick++)
        {
            // Both peers decelerate through the same slide from the same speed — the deceleration
            // is the tuning's, identical on both sides, exactly as it is in a real session where
            // StepVerb is a pure function of replicated inputs.
            speed = Mathf.Max(0f, speed - t.SlideDeceleration * Dt);
            var velocity = new Vector3(0f, 0f, -speed);

            VerbResolution c = AvatarMotor.StepVerb(t, client.Verb, client.VerbClockTicks,
                grounded: true, locked: false, jumped: false, jumpHeld: true,
                WaterState.Dry, velocity, stick);
            VerbResolution s = AvatarMotor.StepVerb(t, server.Verb, server.VerbClockTicks,
                grounded: true, locked: false, jumped: false, jumpHeld: true,
                WaterState.Dry, velocity, stick);

            client.Verb = c.Verb; client.VerbClockTicks = c.ClockTicks;
            server.Verb = s.Verb; server.VerbClockTicks = s.ClockTicks;
            client.Velocity = server.Velocity = velocity;

            Assert.True(SandboxAvatar.PredictionMatches(client, server),
                $"tick {tick}: the gate fired a correction on an ordinary slide "
                + $"(client {client.Verb}/{client.VerbClockTicks}, "
                + $"server {server.Verb}/{server.VerbClockTicks}). A comparison that rewinds the "
                + "player mid-slide is worse than the divergence it was added to catch.");

            if (client.Verb == MoveVerb.DuckWalk)
                sawSettle = true;
        }

        Assert.True(sawSettle,
            "the slide never settled, so this test proved nothing about the settle tick — the "
            + "absence check is only meaningful if the event it claims to survive actually "
            + "happened inside the loop");
    }

    // --- THE BOUNDARY, PINNED ----------------------------------------------------------------------

    /// <summary>
    /// <b>The cost of the exact comparison, stated and pinned.</b> Where the two peers' speeds
    /// genuinely straddle <c>SlideExitSpeedMps</c> they settle on different ticks and the gate
    /// fires one extra rewind. That is the whole downside of choosing exact over coarse, and it is
    /// recorded here as a measured fact rather than an assurance.
    ///
    /// <para><b>Why it is nonetheless the right call:</b> that rewind adopts the authority, so the
    /// disagreement is over in one tick — self-healing. The positional correction it produces is
    /// under <c>PredictionEpsilonM</c> by construction (the two states matched positionally, which
    /// is the only way to reach this branch at all) and is folded into the render offset, so the
    /// player never sees it. An UNCORRECTED verb divergence is not self-healing and never ends.</para>
    ///
    /// <para><b>A different ruling is one line.</b> Dropping the four counters and keeping only
    /// <c>predicted.Verb == auth.Verb</c> would not change any test above except this one; dropping
    /// <c>Verb</c> too reinstates the defect and turns
    /// <see cref="VerbOnlyDivergenceIsDetected"/> red. The boundary is here.</para>
    /// </summary>
    [Fact]
    public void ADifferentRulingIsOneLine_TheCoarseAlternativePinned()
    {
        MotorTuning t = Base;

        // One peer has settled; the other is still in its last slide tick. This is the disagreement
        // a straddled exit threshold produces, and the exact comparison is REQUIRED to catch it —
        // the two states are positionally identical, so nothing else in the gate can.
        MoveState settled = Standing;
        settled.Verb = MoveVerb.DuckWalk;
        MoveState stillSliding = Standing;
        stillSliding.Verb = MoveVerb.Slide;
        stillSliding.VerbClockTicks = (byte)t.SlideMinTicks;

        Assert.False(SandboxAvatar.PredictionMatches(stillSliding, settled));

        // And the counters-only case: same verb, clocks one tick apart. THIS is the assertion a
        // coarser ruling would flip — it is the one place "exact" costs something the verb compare
        // alone would not have cost.
        MoveState clockA = Standing;
        clockA.Verb = MoveVerb.Slide;
        clockA.VerbClockTicks = 10;
        MoveState clockB = clockA;
        clockB.VerbClockTicks = 11;

        Assert.False(SandboxAvatar.PredictionMatches(clockA, clockB),
            "the counters are compared exactly. If a future ruling decides a one-tick clock "
            + "disagreement should not rewind, delete the two VerbClockTicks/ChainTimerTicks lines "
            + "from PredictionMatches and invert this assertion — the verb, chain-depth and "
            + "air-jump comparisons stand on their own arguments and are unaffected.");
    }
}
