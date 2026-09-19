using Godot;
using MpFoundation.Dev.Playground;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The bike camera's arithmetic, pinned</b> (BIKE-2x-C, 2026-09-02). <c>BikeCameraRig</c> is
/// pure, so every claim the camera makes about a curve, a lag, a blend or a collision pull-in is a
/// claim about a function of values, and those are asserted here without an engine.
///
/// <para><b>This file never touches the Node.</b> <c>BikeCamera</c> reads a <c>Camera3D</c>, an
/// avatar and a physics space; none of those exist in this suite, and a test that tried would be
/// asserting against a harness rather than against the camera. Everything worth pinning was put on
/// the pure side for exactly that reason — the same split <c>BikeRigTests</c> already relies on.</para>
///
/// <para><b>The two that matter most are the damping asymmetry and the look-ahead's
/// independence.</b> The first is the whole feel of the packet — fast in, slow out, on every
/// channel — and it is a property of the DEFAULT rows, so a default edited in the wrong direction
/// has to fail here rather than in a headed run. The second is the packet's stated priority: the
/// look-ahead must be tunable without dragging the FOV and the orbit along with it, and the only
/// honest way to state that is to rewrite the other rows and assert the output does not move a
/// bit.</para>
///
/// <para>Parks no tuning: every assertion takes values and asks pure functions about them.</para>
/// </summary>
public sealed class BikeCameraTests
{
    private static readonly BikeCameraTuning C = BikeCameraTuning.Default;

    /// <summary>The ride cap the live camera normalises against, m/s — the same expression
    /// <c>BikeCamera</c> evaluates, restated here from the shipped defaults so the speed cases
    /// below are stated in metres per second rather than in an abstract 0..1.</summary>
    private static float RideCap => BikeRig.RideCapMps(MotorTuning.Default, BikeTuning.Default);

    // --- The damping asymmetry: fast in, slow out, on all four channels ------------------------

    /// <summary>Each channel's two rates, named, so the asymmetry pin below reads as four
    /// statements about four channels rather than as one loop over anonymous floats.</summary>
    public static TheoryData<string, float, float> ChannelRates => new()
    {
        { "fov", C.FovInRate, C.FovOutRate },
        { "distance", C.DistanceInRate, C.DistanceOutRate },
        { "look-ahead", C.LookAheadInRate, C.LookAheadOutRate },
        { "height", C.HeightInRate, C.HeightOutRate },
    };

    [Theory]
    [MemberData(nameof(ChannelRates))]
    public void EveryChannelMovesFurtherInOneTickTowardMoreThanTowardLess(
        string name, float inRate, float outRate)
    {
        Assert.True(inRate > outRate, $"{name}: the 'in' rate must be the faster one");

        const float start = 4f;
        const float magnitude = 2f;
        const float dt = 1f / 60f;

        float movedIn = BikeCameraRig.Damp(start, start + magnitude, inRate, outRate, dt) - start;
        float movedOut = start - BikeCameraRig.Damp(start, start - magnitude, inRate, outRate, dt);

        Assert.True(movedIn > 0f && movedOut > 0f, $"{name}: a lag that does not move is not a lag");
        Assert.True(movedIn > movedOut,
            $"{name}: covered {movedIn:R} m going up but {movedOut:R} m going down - the "
          + "asymmetry is backwards, and the camera would snap back in on every corner");
    }

    /// <summary>The rate picked is the one the DIRECTION calls for, not the one the sign of the
    /// numbers happens to give: a channel crossing zero, or one whose target is negative, must
    /// still use the "in" rate whenever it is heading toward MORE.</summary>
    [Fact]
    public void TheRateIsPickedByDirectionOfTravel_NotBySign()
    {
        const float dt = 0.05f;
        // Rising from a negative value toward zero is still "in".
        float fast = BikeCameraRig.Damp(-2f, 0f, 8f, 1f, dt);
        float slow = BikeCameraRig.Damp(2f, 0f, 8f, 1f, dt);
        Assert.True(fast - -2f > 2f - slow,
            "rising toward zero must use the in rate and falling toward zero the out rate");
    }

    // --- Frame-rate independence ---------------------------------------------------------------

    /// <summary>
    /// <b>One step of 0.1 s lands where ten steps of 0.01 s land.</b> This is the whole reason
    /// <c>Damp</c> is <c>1 - exp(-rate x dt)</c> and not <c>lerp(a, b, rate x dt)</c>: the naive
    /// form is linear in dt while the decay it approximates is exponential, so a channel damped
    /// that way settles measurably faster on a 144 Hz machine than on a 60 Hz one, and this camera
    /// runs on the render clock.
    ///
    /// <para>The tolerance is 1e-5 of the travel, which is float rounding across ten compositions
    /// and nothing else — the identity <c>exp(-r x 0.01)^10 = exp(-r x 0.1)</c> is exact in real
    /// arithmetic. The naive form is checked beside it and MISSES by four orders of magnitude
    /// more, so this pin would notice the regression rather than merely permitting the fix.</para>
    /// </summary>
    [Theory]
    [InlineData(1.5f)]
    [InlineData(5f)]
    [InlineData(20f)]
    public void DampIsFrameRateIndependent(float rate)
    {
        const float start = 0f;
        const float target = 10f;

        float oneBigStep = BikeCameraRig.Damp(start, target, rate, rate, 0.1f);

        float manySmall = start;
        for (int i = 0; i < 10; i++)
            manySmall = BikeCameraRig.Damp(manySmall, target, rate, rate, 0.01f);

        Assert.Equal(oneBigStep, manySmall, 1e-4f);

        // The control: the same two schedules through a naive per-frame lerp, which is what this
        // form exists instead of. If this ever stops disagreeing, the assertion above has stopped
        // proving anything.
        float naiveBig = start + (target - start) * Mathf.Min(rate * 0.1f, 1f);
        float naiveSmall = start;
        for (int i = 0; i < 10; i++)
            naiveSmall += (target - naiveSmall) * Mathf.Min(rate * 0.01f, 1f);
        Assert.True(Mathf.Abs(naiveBig - naiveSmall) > 1e-2f,
            "the naive form must visibly disagree across frame rates, or this test is vacuous");
    }

    // --- Convergence: no overshoot, monotone ---------------------------------------------------

    [Theory]
    [InlineData(0f, 10f)]     // in
    [InlineData(10f, 0f)]     // out
    [InlineData(-3f, 2.5f)]   // through zero
    public void DampNeverOvershootsAndConvergesMonotonically(float start, float target)
    {
        float v = start;
        float previous = start;
        for (int i = 0; i < 600; i++)
        {
            v = BikeCameraRig.Damp(v, target, C.LookAheadInRate, C.LookAheadOutRate, 1f / 60f);

            if (target >= start)
                Assert.True(v <= target + 1e-5f && v >= previous - 1e-6f,
                    $"tick {i}: {previous} -> {v} overshot or went backwards toward {target}");
            else
                Assert.True(v >= target - 1e-5f && v <= previous + 1e-6f,
                    $"tick {i}: {previous} -> {v} overshot or went backwards toward {target}");

            previous = v;
        }
        Assert.Equal(target, v, 1e-3f);
    }

    /// <summary>A dt of zero, a negative dt or a dead rate must hold the value rather than jump
    /// it. A paused frame is a real thing in this lab (the panel opens, the mouse is released) and
    /// a lag that moved on a zero tick would step the lens the frame the game resumes.</summary>
    [Fact]
    public void DampHoldsOnADeadTickOrADeadRate()
    {
        Assert.Equal(3f, BikeCameraRig.Damp(3f, 9f, 5f, 2f, 0f));
        Assert.Equal(3f, BikeCameraRig.Damp(3f, 9f, 5f, 2f, -0.5f));
        Assert.Equal(3f, BikeCameraRig.Damp(3f, 9f, 0f, 0f, 1f / 60f));
    }

    // --- The look-ahead: the row set this packet exists for -------------------------------------

    [Fact]
    public void TheLookAheadIsTheBaseAtAStandstillAndTheFullLeadAtAndAboveTheRideCap()
    {
        float cap = RideCap;

        Assert.Equal(C.LookAheadBaseM,
            BikeCameraRig.LookAheadFor(BikeCameraRig.Speed01(0f, cap, C), C), 1e-5f);
        Assert.Equal(C.LookAheadBaseM + C.LookAheadAtCapM,
            BikeCameraRig.LookAheadFor(BikeCameraRig.Speed01(cap, cap, C), C), 1e-5f);
        // Above the cap it saturates rather than growing: a slope bonus can put a mounted body
        // past its own wish speed, and a lead that kept extending would keep re-framing a rider
        // who has stopped accelerating.
        Assert.Equal(C.LookAheadBaseM + C.LookAheadAtCapM,
            BikeCameraRig.LookAheadFor(BikeCameraRig.Speed01(cap * 3f, cap, C), C), 1e-5f);
        // And a negative speed cannot exist, but a garbage one must not produce a negative lead.
        Assert.Equal(C.LookAheadBaseM,
            BikeCameraRig.LookAheadFor(BikeCameraRig.Speed01(-5f, cap, C), C), 1e-5f);
    }

    [Fact]
    public void TheLookAheadIsMonotoneNonDecreasingInSpeed()
    {
        float cap = RideCap;
        float previous = float.NegativeInfinity;
        for (float mps = 0f; mps <= cap * 1.5f; mps += 0.05f)
        {
            float lead = BikeCameraRig.LookAheadFor(BikeCameraRig.Speed01(mps, cap, C), C);
            Assert.True(lead >= previous, $"the lead fell at {mps:F2} m/s: {previous} -> {lead}");
            previous = lead;
        }
    }

    /// <summary>
    /// <b>The look-ahead is provably independent of the FOV and distance rows.</b> Not "reads
    /// differently in the source" — rewritten, to absurd values, in both directions, and the
    /// output compared for exact equality at every sample. This is the property the packet named
    /// as its most important, and it is the only one an implementation could plausibly break by
    /// factoring the four channels through one shared curve.
    /// </summary>
    [Fact]
    public void TheLookAheadDoesNotMoveWhenTheFovAndDistanceRowsAreRewritten()
    {
        BikeCameraTuning wild = C with
        {
            FovBaseDeg = 30f,
            FovAtCapDeg = 55f,
            DistanceBaseM = 9f,
            DistanceAtCapM = -4f,
            FovInRate = 99f,
            FovOutRate = 0.01f,
            DistanceInRate = 0.02f,
            DistanceOutRate = 77f,
            HeightBaseM = 6f,
            HeightAtCapM = -6f,
        };

        float cap = RideCap;
        for (float mps = 0f; mps <= cap * 1.25f; mps += 0.1f)
        {
            float s = BikeCameraRig.Speed01(mps, cap, C);
            float sWild = BikeCameraRig.Speed01(mps, cap, wild);
            Assert.Equal(s, sWild);   // the shared shaping row was not touched, so this is exact
            Assert.Equal(BikeCameraRig.LookAheadFor(s, C), BikeCameraRig.LookAheadFor(sWild, wild));
        }

        // And the converse, so this is a statement about independence rather than about the
        // look-ahead being inert: moving the look-ahead's OWN rows does move it.
        BikeCameraTuning longer = C with { LookAheadAtCapM = C.LookAheadAtCapM * 2f };
        Assert.True(BikeCameraRig.LookAheadFor(1f, longer) > BikeCameraRig.LookAheadFor(1f, C));
    }

    /// <summary>The other three channels get the same treatment in miniature — each reads only its
    /// own two rows — so "four independent curves" is pinned as four facts and not as one.</summary>
    [Fact]
    public void EachChannelReadsOnlyItsOwnTwoRows()
    {
        var noLead = C with { LookAheadBaseM = 0f, LookAheadAtCapM = 0f };
        var noHeight = C with { HeightBaseM = -3f, HeightAtCapM = 12f };
        var noDistance = C with { DistanceBaseM = -1f, DistanceAtCapM = 40f };
        var noFov = C with { FovBaseDeg = 10f, FovAtCapDeg = 120f };

        for (float s = 0f; s <= 1f; s += 0.05f)
        {
            Assert.Equal(BikeCameraRig.FovFor(s, C), BikeCameraRig.FovFor(s, noLead));
            Assert.Equal(BikeCameraRig.FovFor(s, C), BikeCameraRig.FovFor(s, noHeight));
            Assert.Equal(BikeCameraRig.FovFor(s, C), BikeCameraRig.FovFor(s, noDistance));

            Assert.Equal(BikeCameraRig.DistanceFor(s, C), BikeCameraRig.DistanceFor(s, noLead));
            Assert.Equal(BikeCameraRig.DistanceFor(s, C), BikeCameraRig.DistanceFor(s, noFov));

            Assert.Equal(BikeCameraRig.HeightFor(s, C), BikeCameraRig.HeightFor(s, noFov));
            Assert.Equal(BikeCameraRig.HeightFor(s, C), BikeCameraRig.HeightFor(s, noDistance));
        }
    }

    // --- The speed input ------------------------------------------------------------------------

    [Fact]
    public void TheSpeedCueIsNormalisedAgainstTheRideCapAndClampedAtBothEnds()
    {
        float cap = RideCap;
        Assert.Equal(0f, BikeCameraRig.Speed01(0f, cap, C));
        Assert.Equal(1f, BikeCameraRig.Speed01(cap, cap, C), 1e-5f);
        Assert.Equal(1f, BikeCameraRig.Speed01(cap * 10f, cap, C), 1e-5f);
        Assert.Equal(0f, BikeCameraRig.Speed01(-1f, cap, C));
        Assert.Equal(0f, BikeCameraRig.Speed01(5f, 0f, C));            // a dead cap divides nothing
        Assert.Equal(0f, BikeCameraRig.Speed01(float.NaN, cap, C));

        // The foot sprint is well short of the ride cap, which is the fact that justifies this
        // whole class: SandboxCamera's own cue saturates there and says nothing above it.
        float footSprint = MotorTuning.Default.MoveSpeed * MotorTuning.Default.SprintMultiplier;
        float atFootSprint = BikeCameraRig.Speed01(footSprint, cap, C);
        Assert.True(atFootSprint > 0.5f && atFootSprint < 0.75f,
            $"the foot sprint reads {atFootSprint:F3} of the ride cap - if it ever reaches 1 the "
          + "bike camera has stopped having anything the shipped one does not");
    }

    [Fact]
    public void TheShapingExponentBendsTheCurveWithoutMovingItsEnds()
    {
        var steep = C with { SpeedCurveExponent = 2f };
        var soft = C with { SpeedCurveExponent = 0.5f };
        float cap = RideCap;

        foreach (BikeCameraTuning t in new[] { C, steep, soft })
        {
            Assert.Equal(0f, BikeCameraRig.Speed01(0f, cap, t), 1e-6f);
            Assert.Equal(1f, BikeCameraRig.Speed01(cap, cap, t), 1e-5f);
        }
        float half = cap * 0.5f;
        Assert.True(BikeCameraRig.Speed01(half, cap, steep) < BikeCameraRig.Speed01(half, cap, C));
        Assert.True(BikeCameraRig.Speed01(half, cap, soft) > BikeCameraRig.Speed01(half, cap, C));
        // A zero or negative exponent is floored rather than allowed to make a standstill read as
        // full speed (Pow(0, 0) is 1).
        Assert.Equal(0f, BikeCameraRig.Speed01(0f, cap, C with { SpeedCurveExponent = 0f }), 1e-6f);
    }

    // --- The blend ------------------------------------------------------------------------------

    [Fact]
    public void AtBlendZeroEveryChannelReturnsExactlyTheUndecoratedValue()
    {
        Assert.Equal(75f, BikeCameraRig.Blend(75f, 93f, 0f));
        Assert.Equal(75f, BikeCameraRig.Blend(75f, 93f, -1f));
        Assert.Equal(93f, BikeCameraRig.Blend(75f, 93f, 1f));
        Assert.Equal(93f, BikeCameraRig.Blend(75f, 93f, 4f));      // clamped

        // The FOV write goes through FovMix, so the identity has to hold there too - and the max
        // must not be able to leak a wider lens through at blend zero.
        Assert.Equal(89f, BikeCameraRig.FovMix(89f, 200f, 0f));
        Assert.Equal(89f, BikeCameraRig.FovMix(89f, 10f, 1f));
        Assert.Equal(93f, BikeCameraRig.FovMix(89f, 93f, 1f));
    }

    /// <summary>
    /// <b>The blend has no step in it anywhere on 0..1.</b> Sampled at a thousandth and asserted
    /// against a bound derived from the sample spacing rather than a magic epsilon: the function is
    /// a straight line between two fixed endpoints, so the largest legal step between adjacent
    /// samples is exactly the endpoint gap times the spacing, and anything larger is a
    /// discontinuity. A dismount walks this whole range in 0.20 s, so a step here is a visible cut
    /// in the frame.
    /// </summary>
    [Theory]
    [InlineData(75f, 93f)]
    [InlineData(0f, 1.4f)]
    [InlineData(3f, -2f)]     // a decorated value BELOW the undecorated one still may not step
    public void TheBlendIsContinuousAcrossItsWholeRange(float undecorated, float decorated)
    {
        const int samples = 1000;
        float span = Mathf.Abs(decorated - undecorated);
        // The +2e-4 is float rounding on a lerp whose endpoints are tens of degrees, not slack:
        // two samples each carry up to ~1e-5 of representation error at 93 degrees, and their
        // difference carries both. It is still two orders below the smallest step a real
        // discontinuity in this function could produce.
        float allowed = span / samples + 2e-4f;

        float previous = BikeCameraRig.Blend(undecorated, decorated, 0f);
        Assert.Equal(undecorated, previous);
        for (int i = 1; i <= samples; i++)
        {
            float v = BikeCameraRig.Blend(undecorated, decorated, i / (float)samples);
            Assert.True(Mathf.Abs(v - previous) <= allowed,
                $"step of {Mathf.Abs(v - previous):R} at blend {i / (float)samples:F3}, "
              + $"allowed {allowed:R}");
            previous = v;
        }
        Assert.Equal(decorated, previous);
    }

    [Fact]
    public void TheFovMixNeverNarrowsTheLensAMountFound()
    {
        // At a mid ride speed the bike's own curve sits BELOW the shipped rig's saturated cue.
        // Mixing straight toward it would narrow the lens as the rider got faster, which is a
        // speed cue running backwards; the max removes the case.
        for (float blend = 0f; blend <= 1f; blend += 0.05f)
            Assert.True(BikeCameraRig.FovMix(89f, 86.6f, blend) >= 89f - 1e-5f,
                $"the mount narrowed the lens at blend {blend:F2}");
    }

    // --- Collision ------------------------------------------------------------------------------

    [Fact]
    public void ABlockedSweepPullsInToExactlyTheBlockedDistanceInOneTick()
    {
        const float current = 1.40f;
        const float blocked = 0.35f;
        float after = BikeCameraRig.CollisionStep(current, 1.40f, blocked,
            C.CollisionEaseOutRate, 1f / 60f);
        Assert.Equal(blocked, after);      // exactly, not approximately, and not next tick

        // Even an absurd dt or a dead rate cannot delay a pull-in: it is not damped at all.
        Assert.Equal(blocked, BikeCameraRig.CollisionStep(current, 1.40f, blocked, 0f, 0f));
        Assert.Equal(0f, BikeCameraRig.CollisionStep(current, 1.40f, 0f, C.CollisionEaseOutRate, 1f / 60f));
    }

    [Fact]
    public void AnUnblockedSweepEasesOutAndTakesStrictlyMoreThanOneTick()
    {
        const float want = 1.40f;
        const float dt = 1f / 60f;

        float first = BikeCameraRig.CollisionStep(0f, want, want, C.CollisionEaseOutRate, dt);
        Assert.True(first > 0f, "the ease-out must actually move");
        Assert.True(first < want * 0.5f,
            $"one tick covered {first:F3} m of {want:F2} m - that is a snap, not an ease");

        int ticks = 1;
        float v = first;
        while (v < want - 1e-3f && ticks < 10_000)
        {
            v = BikeCameraRig.CollisionStep(v, want, want, C.CollisionEaseOutRate, dt);
            ticks++;
        }
        Assert.True(ticks > 1, "an unblocked push must not arrive in one tick");
        Assert.Equal(want, v, 1e-3f);
    }

    /// <summary>The push never exceeds what the sweep allows, and never exceeds what the speed
    /// channels asked for, whichever is smaller — over a swept sequence of blockages rather than a
    /// single case, because the interesting failure is a push that eases past a wall that arrived
    /// while it was easing.</summary>
    [Fact]
    public void ThePushIsNeverMoreThanTheSweepOrTheDesireAllow()
    {
        float v = 0f;
        float want = 1.6f;
        for (int i = 0; i < 400; i++)
        {
            // A wall that closes in, opens up, then closes again.
            float blocked = 1.6f * (0.5f + 0.5f * Mathf.Sin(i * 0.05f));
            v = BikeCameraRig.CollisionStep(v, want, blocked, C.CollisionEaseOutRate, 1f / 60f);
            Assert.True(v <= blocked + 1e-5f, $"tick {i}: pushed {v} past a block at {blocked}");
            Assert.True(v <= want + 1e-5f, $"tick {i}: pushed {v} past the wanted {want}");
            Assert.True(v >= -1e-5f, $"tick {i}: the push went negative ({v})");
        }
    }

    // --- The defaults ---------------------------------------------------------------------------

    [Fact]
    public void TheDefaultsAreSane()
    {
        // Fast in, slow out, on every one of the four - the packet's central feel claim.
        Assert.True(C.FovInRate > C.FovOutRate);
        Assert.True(C.DistanceInRate > C.DistanceOutRate);
        Assert.True(C.LookAheadInRate > C.LookAheadOutRate);
        Assert.True(C.HeightInRate > C.HeightOutRate);
        foreach (float rate in new[]
        {
            C.FovInRate, C.FovOutRate, C.DistanceInRate, C.DistanceOutRate,
            C.LookAheadInRate, C.LookAheadOutRate, C.HeightInRate, C.HeightOutRate,
            C.CollisionEaseOutRate,
        })
            Assert.True(rate > 0.1f && rate < 60f, $"a lag rate of {rate}/s is not a lag");

        // The lens. 75 is SandboxCamera's own DefaultFov, so a mount at a stand is a no-op; the
        // ceiling is kept under the band a third-person camera starts making people ill in.
        Assert.Equal(75f, C.FovBaseDeg);
        Assert.True(C.FovAtCapDeg > 0f && C.FovBaseDeg + C.FovAtCapDeg <= 100f,
            "the flat-out lens must be wider than the standing one and under 100 degrees");

        // The orbit and the height are EXTRAS on top of the shipped 3.15 -> 4.5 m arm; both must
        // be additions, and the flat-out orbit must stay inside something a 1.2 m body can still
        // be read at.
        Assert.True(C.DistanceBaseM >= 0f && C.DistanceAtCapM > 0f);
        Assert.True(C.DistanceBaseM + C.DistanceAtCapM < 3f,
            "more than 3 m of extra orbit puts the rider off the far end of the frame");
        Assert.True(C.HeightBaseM >= 0f && C.HeightAtCapM > 0f);
        Assert.True(C.HeightBaseM + C.HeightAtCapM < 2f);

        // The lead. Flat out it must be worth roughly the half-second of travel that makes a
        // corner readable at the ride cap, and it must not be so long that the rider leaves frame.
        Assert.True(C.LookAheadBaseM >= 0f && C.LookAheadAtCapM > 0f);
        float leadAtCap = C.LookAheadBaseM + C.LookAheadAtCapM;
        Assert.True(leadAtCap > RideCap * 0.25f && leadAtCap < RideCap * 0.75f,
            $"the flat-out lead is {leadAtCap:F2} m against a {RideCap:F2} m/s cap - that is "
          + $"{leadAtCap / RideCap:F2} s of travel");

        // Collision. The probe must be at least the shipped spring arm's own 0.25 m sphere, or the
        // extra push would carry less clearance than the orbit it is added to.
        Assert.True(C.CollisionRadiusM >= 0.25f && C.CollisionRadiusM < 1f);

        // The shaping row opens linear on purpose; see its doc comment.
        Assert.Equal(1f, C.SpeedCurveExponent);

        Assert.Equal(BikeCameraTuning.Default, BikeCameraTuning.Current);
    }
}
