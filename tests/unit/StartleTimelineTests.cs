using System;
using System.Collections.Generic;
using System.Linq;
using MpFoundation.Game.Round;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The startle, without an engine</b> (DOOR-1's first gate). Everything the burst door does
/// that is a RULE rather than a node: which beats fire and in what order, where the leaf is at
/// any instant, how hard the camera is kicked, how the impulse falls off, and every way the JSON
/// overlay can be wrong.
///
/// <para><b>What is deliberately NOT here.</b> That every peer ran this timeline at the same
/// instant, and that the blocker really stopped blocking — neither is reachable from a pure
/// function, and both are <c>tests/Run-BurstDoorTest.ps1</c>'s job. The split is the point: the
/// scene suite is expensive and load-flaky, so it should only ever be asked the questions a cheap
/// deterministic test cannot answer.</para>
/// </summary>
public class StartleTimelineTests
{
    private const double Eps = 1e-4;

    /// <summary>xUnit's three-argument <c>Assert.Equal</c> is ambiguous between the
    /// <c>(double, double, int digits)</c> and <c>(float, float, float tolerance)</c> overloads
    /// the moment either side is a <c>float</c>, which every number on this timeline is. One
    /// helper that widens to double is the fix; casting at eighty call sites is not.</summary>
    private static void Near(double expected, double actual, int digits) =>
        Assert.Equal(expected, actual, digits);

    // =========================================================================================
    // 1. The beats, and their order
    // =========================================================================================

    [Fact]
    public void Defaults_AreTheNumbersThePacketAndSectionFiveName()
    {
        StartleTuning d = StartleTuning.Default;
        Assert.Equal(0.4f, d.TellSec);
        Assert.Equal(0.12f, d.LeafOpenSec);
        Assert.Equal(110f, d.LeafOpenDeg);
        Assert.Equal(125f, d.LeafOvershootDeg);
        Assert.Equal(0.25f, d.LeafSettleSec);
        Assert.Equal(0.6f, d.LeafCloseSec);
        Assert.Equal(3.0f, d.BurstRadiusM);
        Assert.Equal(6f, d.BurstImpulseNs);
        Assert.Equal(4f, d.KickDegrees);
        Assert.Equal(0.3f, d.KickSec);
        Assert.Equal(-3f, d.BangFlatDb);
        Assert.True(d.ForceDropOnBurst);
        // The three anticipation knobs ship OFF (§5: "default off for the first ride").
        Assert.Equal(0f, d.FakeKnockRatePerMin);
        Assert.False(d.SeekerKnockKey);
        Assert.False(d.SeekerHeatOnIntercom);
    }

    [Fact]
    public void Beats_OnTheShippedTuning_AreTheFiveInSectionFivesOrderAtTheirStatedTimes()
    {
        IReadOnlyList<StartleBeat> beats = StartleTimeline.Beats(StartleTuning.Default);

        Assert.Equal(
            new[]
            {
                StartleBeatKind.Tell,
                StartleBeatKind.Burst,
                StartleBeatKind.LeafOvershoot,
                StartleBeatKind.KickEnded,
                StartleBeatKind.LeafSettled,
            },
            beats.Select(b => b.Kind).ToArray());

        Near(0.00, beats[0].AtSec, 4);   // the tell opens at the Found instant
        Near(0.40, beats[1].AtSec, 4);   // TellSec
        Near(0.52, beats[2].AtSec, 4);   // + LeafOpenSec
        Near(0.70, beats[3].AtSec, 4);   // + KickSec
        Near(0.77, beats[4].AtSec, 4);   // + LeafOpenSec + LeafSettleSec
    }

    [Fact]
    public void Beats_AreAlwaysSorted_EvenWhenAKnobReordersThem()
    {
        // A 0.9 s kick outlasts a 0.12 + 0.25 s leaf, so KickEnded moves from fourth to last.
        // The list has to stay sorted or a consumer walking it fires the settle before the
        // overshoot. This is the case that made Beats() sort rather than append in a fixed order.
        var t = StartleTuning.Default with { KickSec = 0.9f };
        IReadOnlyList<StartleBeat> beats = StartleTimeline.Beats(t);

        Assert.Equal(StartleBeatKind.KickEnded, beats[^1].Kind);
        for (int i = 1; i < beats.Count; i++)
            Assert.True(beats[i].AtSec >= beats[i - 1].AtSec,
                $"beat {beats[i].Kind} at {beats[i].AtSec} came after {beats[i - 1].Kind} at "
                + $"{beats[i - 1].AtSec}");
    }

    [Fact]
    public void TellSecZero_RemovesTheTellBeatEntirely_RatherThanMakingItZeroLength()
    {
        // §5: "At 0 there is no tell at all." A zero-length Tell beat would still dip the light
        // and click the intercom for one frame, which is a flicker rather than an absence.
        var t = StartleTuning.Default with { TellSec = 0f };
        IReadOnlyList<StartleBeat> beats = StartleTimeline.Beats(t);

        Assert.DoesNotContain(StartleBeatKind.Tell, beats.Select(b => b.Kind));
        Assert.Equal(StartleBeatKind.Burst, beats[0].Kind);
        Near(0.0, beats[0].AtSec, 4);
        Near(0.0, StartleTimeline.BurstAtSec(t), 4);
    }

    [Fact]
    public void EndsAtSec_IsTheLastBeat_UnderBothOrderings()
    {
        Near(0.77, StartleTimeline.EndsAtSec(StartleTuning.Default), 4);
        Near(0.40 + 0.9, StartleTimeline.EndsAtSec(StartleTuning.Default with { KickSec = 0.9f }), 4);
    }

    [Fact]
    public void TellSec_MovesEveryLaterBeatWithIt_AndTheGapsBetweenThemAreUnchanged()
    {
        // The tell is a DELAY on the burst, not a phase with its own internal schedule: doubling
        // it must not stretch the leaf's throw. A timeline that computed each beat from its own
        // fraction of the whole would pass the "order" test above and fail this one.
        IReadOnlyList<StartleBeat> a = StartleTimeline.Beats(StartleTuning.Default with { TellSec = 0.4f });
        IReadOnlyList<StartleBeat> b = StartleTimeline.Beats(StartleTuning.Default with { TellSec = 1.2f });

        for (int i = 1; i < a.Count; i++)
            Near(a[i].AtSec + 0.8, b[i].AtSec, 4);
    }

    // =========================================================================================
    // 2. The leaf
    // =========================================================================================

    [Fact]
    public void Leaf_IsShutThroughTheWholeTell_AndOpenForever_After()
    {
        StartleTuning t = StartleTuning.Default;

        Assert.Equal(0f, StartleTimeline.LeafAngleDegAt(0.0, t));
        Assert.Equal(0f, StartleTimeline.LeafAngleDegAt(0.399, t));
        Assert.Equal(0f, StartleTimeline.LeafAngleDegAt(0.40, t));   // the burst frame itself
        Near(t.LeafOpenDeg, StartleTimeline.LeafAngleDegAt(0.77, t), 3);
        Near(t.LeafOpenDeg, StartleTimeline.LeafAngleDegAt(600.0, t), 3);
    }

    [Fact]
    public void Leaf_ReachesExactlyTheOvershoot_AndSettlesExactlyToTheRestAngle()
    {
        StartleTuning t = StartleTuning.Default;
        Near(t.LeafOvershootDeg, StartleTimeline.LeafAngleDegAt(0.40 + 0.12, t), 3);
        Near(t.LeafOpenDeg, StartleTimeline.LeafAngleDegAt(0.40 + 0.12 + 0.25, t), 3);
    }

    [Fact]
    public void Leaf_Overshoots_SoItIsPastItsRestAngleBeforeItComesBack()
    {
        // The bounce, asserted as a fact about the CURVE rather than about one sample: somewhere
        // in the throw the leaf must be beyond where it ends up, or "overshoot" is a word in a
        // comment. A linear 0 -> 110 tween passes every other leaf test here and fails this one.
        StartleTuning t = StartleTuning.Default;
        double peak = 0;
        for (double s = 0.40; s <= 0.77; s += 0.002)
            peak = Math.Max(peak, StartleTimeline.LeafAngleDegAt(s, t));
        Assert.True(peak >= t.LeafOvershootDeg - 0.01,
            $"the leaf only reached {peak:F2} deg; the overshoot is {t.LeafOvershootDeg}");
        Assert.True(peak > t.LeafOpenDeg, "the leaf never went past its rest angle");
    }

    [Fact]
    public void Leaf_IsMonotonicWhileOpening_AndMonotonicWhileSettling()
    {
        // A door that went back and forth on the way out would read as a glitch rather than as a
        // slam. Two separate monotone legs, checked separately, because the whole curve is not
        // monotone by design.
        StartleTuning t = StartleTuning.Default;
        double previous = -1;
        for (double s = 0.40; s <= 0.52; s += 0.002)
        {
            double a = StartleTimeline.LeafAngleDegAt(s, t);
            Assert.True(a >= previous - Eps, $"the leaf went backwards at {s:F3}s");
            previous = a;
        }
        previous = double.MaxValue;
        for (double s = 0.52; s <= 0.77; s += 0.002)
        {
            double a = StartleTimeline.LeafAngleDegAt(s, t);
            Assert.True(a <= previous + Eps, $"the leaf went forwards again at {s:F3}s");
            previous = a;
        }
    }

    [Fact]
    public void Leaf_ThrowsItselfOutOfTheFrameRatherThanEasingOut()
    {
        // The feel claim, made measurable: at the QUARTER point of the 0.12 s throw the leaf must
        // already be past half its travel. A linear tween is at 25 % there and an ease-IN is
        // lower still; both read as a gate opening rather than as a door being thrown.
        StartleTuning t = StartleTuning.Default;
        double quarter = StartleTimeline.LeafAngleDegAt(0.40 + 0.03, t);
        Assert.True(quarter > t.LeafOvershootDeg * 0.5,
            $"a quarter of the way through the throw the leaf was only at {quarter:F1} deg of "
            + $"{t.LeafOvershootDeg}");
    }

    [Fact]
    public void Leaf_BeforeItsOwnStart_IsShutRatherThanExtrapolated()
    {
        Assert.Equal(0f, StartleTimeline.LeafAngleDegAt(-5.0, StartleTuning.Default));
    }

    [Fact]
    public void Closing_StartsFromWhereverTheLeafActuallyIs_AndEndsExactlyShut()
    {
        // A reset can legally land mid-throw, so the close takes its start angle as an argument
        // rather than assuming the leaf is at LeafOpenDeg.
        StartleTuning t = StartleTuning.Default;
        Near(117f, StartleTimeline.ClosingAngleDegAt(0.0, 117f, t), 3);
        Near(0f, StartleTimeline.ClosingAngleDegAt(t.LeafCloseSec, 117f, t), 3);
        Near(0f, StartleTimeline.ClosingAngleDegAt(99.0, 117f, t), 3);

        double previous = double.MaxValue;
        for (double s = 0.0; s <= t.LeafCloseSec; s += 0.01)
        {
            double a = StartleTimeline.ClosingAngleDegAt(s, 110f, t);
            Assert.True(a <= previous + Eps, $"the leaf re-opened while closing, at {s:F3}s");
            previous = a;
        }
    }

    // =========================================================================================
    // 3. The camera kick
    // =========================================================================================

    [Fact]
    public void Kick_StartsAtZero_EndsAtZero_AndIsZeroOutsideItsWindow()
    {
        StartleTuning t = StartleTuning.Default;
        Near(0f, StartleTimeline.KickPitchDegAt(0.0, t), 4);
        Near(0f, StartleTimeline.KickPitchDegAt(t.KickSec, t), 4);
        Near(0f, StartleTimeline.KickPitchDegAt(t.KickSec + 1.0, t), 4);
        Near(0f, StartleTimeline.KickPitchDegAt(-0.1, t), 4);
    }

    [Fact]
    public void Kick_NeverExceedsItsAmplitude_AndActuallyReachesMostOfIt()
    {
        // Both halves matter. The cap is what keeps a "small kick" small; without the floor, a
        // kick that silently produced 0.2 degrees would pass the cap and do nothing on screen.
        StartleTuning t = StartleTuning.Default;
        double peak = 0;
        for (double s = 0; s < t.KickSec; s += 0.001)
            peak = Math.Max(peak, Math.Abs(StartleTimeline.KickPitchDegAt(s, t)));
        Assert.True(peak <= t.KickDegrees + Eps, $"the kick peaked at {peak:F3} deg, over its {t.KickDegrees}");
        Assert.True(peak > t.KickDegrees * 0.6, $"the kick only ever reached {peak:F3} deg");
    }

    [Fact]
    public void Kick_ChangesSign_SoItIsAShakeAndNotAShove()
    {
        // A decaying sine goes both ways; a decaying exponential does not. A one-directional
        // "kick" is the camera being pushed and left there for 0.3 s, which is the thing that
        // reads as a lost frame of control rather than as a flinch.
        StartleTuning t = StartleTuning.Default;
        bool positive = false, negative = false;
        for (double s = 0; s < t.KickSec; s += 0.001)
        {
            float v = StartleTimeline.KickPitchDegAt(s, t);
            positive |= v > 0.1f;
            negative |= v < -0.1f;
        }
        Assert.True(positive && negative, "the kick never came back through zero");
    }

    [Fact]
    public void Kick_DecaysMonotonicallyInAmplitude_SoTheSecondSwingIsSmallerThanTheFirst()
    {
        StartleTuning t = StartleTuning.Default;
        double firstPeak = 0, secondPeak = 0;
        for (double s = 0; s < t.KickSec * 0.5; s += 0.001)
            firstPeak = Math.Max(firstPeak, Math.Abs(StartleTimeline.KickPitchDegAt(s, t)));
        for (double s = t.KickSec * 0.5; s < t.KickSec; s += 0.001)
            secondPeak = Math.Max(secondPeak, Math.Abs(StartleTimeline.KickPitchDegAt(s, t)));
        Assert.True(secondPeak < firstPeak,
            $"the kick's return swing ({secondPeak:F3} deg) was not smaller than its first "
            + $"({firstPeak:F3} deg)");
    }

    [Fact]
    public void Kick_OfZeroDegreesOrZeroSeconds_IsNothingAtAll()
    {
        Near(0f, StartleTimeline.KickPitchDegAt(0.05, StartleTuning.Default with { KickDegrees = 0f }), 4);
        Near(0f, StartleTimeline.KickPitchDegAt(0.05, StartleTuning.Default with { KickSec = 0f }), 4);
    }

    // =========================================================================================
    // 4. The impulse falloff — the packet's second named xUnit target
    // =========================================================================================

    [Fact]
    public void Impulse_IsFullAtTheDoorway_HalfAtHalfTheRadius_AndZeroAtTheEdge()
    {
        StartleTuning t = StartleTuning.Default;   // 6 Ns over 3 m
        Near(6f, StartleTimeline.ImpulseNsAt(0.0, t), 4);
        Near(4f, StartleTimeline.ImpulseNsAt(1.0, t), 4);
        Near(3f, StartleTimeline.ImpulseNsAt(1.5, t), 4);
        Near(2f, StartleTimeline.ImpulseNsAt(2.0, t), 4);
        Near(0f, StartleTimeline.ImpulseNsAt(3.0, t), 4);
    }

    [Fact]
    public void Impulse_IsZeroBeyondTheRadius_AndNeverNegative()
    {
        // The whole reason the falloff is written as a clamped linear rather than as
        // `ns * (1 - d/r)`: past the radius that expression goes negative, and a negative impulse
        // is a prop being SUCKED toward the door by a blast.
        StartleTuning t = StartleTuning.Default;
        foreach (double d in new[] { 3.0, 3.01, 5.0, 40.0, 1e6 })
            Near(0f, StartleTimeline.ImpulseNsAt(d, t), 4);
    }

    [Fact]
    public void Impulse_IsLinear_NotInverseSquare()
    {
        // Stated as a shape rather than as three samples: the midpoint of any two distances must
        // give the mean of their impulses. An inverse-square falloff satisfies "full at zero,
        // zero at the edge" and fails this, and it is the falloff somebody reaches for by habit.
        StartleTuning t = StartleTuning.Default;
        for (double a = 0.0; a < 3.0; a += 0.25)
        {
            for (double b = a; b < 3.0; b += 0.25)
            {
                double mid = (a + b) / 2.0;
                Near(
                    (StartleTimeline.ImpulseNsAt(a, t) + StartleTimeline.ImpulseNsAt(b, t)) / 2.0,
                    StartleTimeline.ImpulseNsAt(mid, t), 3);
            }
        }
    }

    [Fact]
    public void Impulse_WithANonPositiveRadiusOrANegativeDistance_IsDefined()
    {
        Near(0f, StartleTimeline.ImpulseNsAt(1.0, StartleTuning.Default with { BurstRadiusM = 0f }), 4);
        Near(0f, StartleTimeline.ImpulseNsAt(1.0, StartleTuning.Default with { BurstRadiusM = -1f }), 4);
        // A negative distance is nonsense from a caller, not a reason to hand back more than the
        // full impulse.
        Near(6f, StartleTimeline.ImpulseNsAt(-2.0, StartleTuning.Default), 4);
    }

    [Fact]
    public void FoundTickSentinel_MatchesTheWiresOwn()
    {
        // HideSeekWire.NoFoundTick is -1 and the door must agree with it, or a round with no find
        // would stage a burst at tick -1.
        Assert.False(StartleTimeline.IsRealFoundTick(HideSeekWire.NoFoundTick));
        Assert.False(StartleTimeline.IsRealFoundTick(-2));
        Assert.True(StartleTimeline.IsRealFoundTick(0));   // tick 0 is a real tick
        Assert.True(StartleTimeline.IsRealFoundTick(41));
    }

    // =========================================================================================
    // 5. Validation and the JSON overlay
    // =========================================================================================

    [Fact]
    public void Validate_ClampsOutOfRange_ReplacesNonFinite_AndSaysSoOnce_Each()
    {
        var wild = StartleTuning.Default with
        {
            TellSec = 99f,             // over the 1.5 s ceiling §5 states
            KickDegrees = float.NaN,   // not a number at all
            BurstRadiusM = -4f,        // under the floor
        };
        StartleTuning v = StartleTuning.Validate(wild, out IReadOnlyList<string> warnings);

        Assert.Equal(1.5f, v.TellSec);
        Assert.Equal(StartleTuning.Default.KickDegrees, v.KickDegrees);
        Assert.Equal(0f, v.BurstRadiusM);
        Assert.Equal(3, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("TellSec"));
        Assert.Contains(warnings, w => w.Contains("KickDegrees"));
        Assert.Contains(warnings, w => w.Contains("BurstRadiusM"));
    }

    [Fact]
    public void Validate_RaisesAnOvershootThatSitsUnderTheRestAngle()
    {
        // The one ORDERING rule. An overshoot below the rest angle is not a bounce; it is the
        // leaf arriving from the wrong side, and on screen it is a door that opens past itself
        // and keeps going.
        var t = StartleTuning.Default with { LeafOpenDeg = 110f, LeafOvershootDeg = 80f };
        StartleTuning v = StartleTuning.Validate(t, out IReadOnlyList<string> warnings);
        Assert.Equal(110f, v.LeafOvershootDeg);
        Assert.Contains(warnings, w => w.Contains("LeafOvershootDeg"));
    }

    [Fact]
    public void Validate_LeavesTheShippedTuningAloneAndSaysNothing()
    {
        StartleTuning v = StartleTuning.Validate(StartleTuning.Default, out IReadOnlyList<string> warnings);
        Assert.Equal(StartleTuning.Default, v);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Overlay_RoundTrips_AndIsSparse()
    {
        var t = StartleTuning.Default with { TellSec = 0.9f, KickDegrees = 7f };
        string json = StartleTuningFile.ToJson(t, DateTimeOffset.UnixEpoch);

        // Sparse: only what MOVED. A file that restated a default would silently pin it against a
        // future change to the shipped constant.
        Assert.Contains("TellSec", json);
        Assert.Contains("KickDegrees", json);
        Assert.DoesNotContain("LeafOpenDeg", json);
        Assert.DoesNotContain("BangFlatDb", json);

        StartleTuningLoad back = StartleTuningFile.FromJson(json);
        Assert.False(back.Discarded);
        Assert.Empty(back.Warnings);
        Assert.Equal(t, back.Tuning);
    }

    [Fact]
    public void Overlay_AppliesOnlyWhatItNames_AndEverythingElseKeepsItsShippedDefault()
    {
        StartleTuningLoad load = StartleTuningFile.FromJson(
            "{\"version\":1,\"values\":{\"TellSec\":0.0}}");
        Assert.False(load.Discarded);
        Assert.Equal(0f, load.Tuning.TellSec);
        Assert.Equal(StartleTuning.Default.LeafOpenDeg, load.Tuning.LeafOpenDeg);
        Assert.Equal(StartleTuning.Default.BurstImpulseNs, load.Tuning.BurstImpulseNs);
    }

    [Fact]
    public void Overlay_MatchesKnobNamesWithoutCaring_AboutCase()
    {
        // The file is hand-edited by a person, so "tellsec" failing silently is an evening.
        StartleTuningLoad load = StartleTuningFile.FromJson(
            "{\"version\":1,\"values\":{\"tellsec\":1.1,\"BURSTIMPULSENS\":9}}");
        Assert.Empty(load.Warnings);
        Near(1.1f, load.Tuning.TellSec, 4);
        Near(9f, load.Tuning.BurstImpulseNs, 4);
    }

    [Fact]
    public void Overlay_TakesABooleanEitherWay_AsZeroOneOrAsTrueFalse()
    {
        Assert.False(StartleTuningFile.FromJson(
            "{\"version\":1,\"values\":{\"ForceDropOnBurst\":0}}").Tuning.ForceDropOnBurst);
        Assert.False(StartleTuningFile.FromJson(
            "{\"version\":1,\"values\":{\"ForceDropOnBurst\":false}}").Tuning.ForceDropOnBurst);
        Assert.True(StartleTuningFile.FromJson(
            "{\"version\":1,\"values\":{\"SeekerKnockKey\":true}}").Tuning.SeekerKnockKey);
        Assert.True(StartleTuningFile.FromJson(
            "{\"version\":1,\"values\":{\"SeekerHeatOnIntercom\":1}}").Tuning.SeekerHeatOnIntercom);
    }

    [Fact]
    public void Overlay_DiscardsTheWholeDocument_OnBadJson_OrAWrongVersion_OrANonObject()
    {
        foreach (string bad in new[]
                 {
                     "{ this is not json",
                     "[1, 2, 3]",
                     "{\"version\":2,\"values\":{\"TellSec\":0.9}}",
                     "{\"values\":{\"TellSec\":0.9}}",
                 })
        {
            StartleTuningLoad load = StartleTuningFile.FromJson(bad);
            Assert.True(load.Discarded, $"'{bad}' was not discarded");
            Assert.Equal(StartleTuning.Default, load.Tuning);
            Assert.NotEmpty(load.Warnings);
        }
    }

    [Fact]
    public void Overlay_IgnoresAKnobThisBuildDoesNotHave_RatherThanFailingTheFile()
    {
        StartleTuningLoad load = StartleTuningFile.FromJson(
            "{\"version\":1,\"values\":{\"TellSec\":0.7,\"KnobFromTheFuture\":3}}");
        Assert.False(load.Discarded);
        Near(0.7f, load.Tuning.TellSec, 4);
        Assert.Single(load.Warnings);
        Assert.Contains("KnobFromTheFuture", load.Warnings[0]);
    }

    [Fact]
    public void Overlay_ReplacesANonNumberValue_AndKeepsReadingTheRestOfTheFile()
    {
        StartleTuningLoad load = StartleTuningFile.FromJson(
            "{\"version\":1,\"values\":{\"TellSec\":\"fast\",\"KickDegrees\":9}}");
        Assert.False(load.Discarded);
        Assert.Equal(StartleTuning.Default.TellSec, load.Tuning.TellSec);
        Near(9f, load.Tuning.KickDegrees, 4);
        Assert.Single(load.Warnings);
    }

    [Fact]
    public void Overlay_AnEmptyValuesObject_IsTheLegitimateNothingMovedFile()
    {
        StartleTuningLoad load = StartleTuningFile.FromJson("{\"version\":1,\"values\":{}}");
        Assert.False(load.Discarded);
        Assert.Empty(load.Warnings);
        Assert.Equal(StartleTuning.Default, load.Tuning);
    }

    [Fact]
    public void Overlay_ClampsThroughValidate_SoAHandEditedFileCannotShipAnIllegalTuning()
    {
        StartleTuningLoad load = StartleTuningFile.FromJson(
            "{\"version\":1,\"values\":{\"TellSec\":50,\"LeafOvershootDeg\":20,\"LeafOpenDeg\":100}}");
        Assert.False(load.Discarded);
        Assert.Equal(1.5f, load.Tuning.TellSec);
        Assert.Equal(100f, load.Tuning.LeafOvershootDeg);
        Assert.True(load.Warnings.Count >= 2);
    }

    [Fact]
    public void Overlay_AMissingFile_IsSilentAndIsNotADiscard()
    {
        StartleTuningLoad load = StartleTuningFile.LoadFrom(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "sfgd-no-such-startle-file-" + Guid.NewGuid().ToString("N") + ".json"));
        Assert.False(load.FileExisted);
        Assert.False(load.Discarded);
        Assert.Empty(load.Warnings);
        Assert.Equal(StartleTuning.Default, load.Tuning);
    }

    [Fact]
    public void Overlay_EveryKnobInTheTable_IsReachableByName_AndNoTwoShareOne()
    {
        // The table is hand-written, so a copy-paste that left two rows reading the same field
        // would make one knob silently un-settable. Checked as a property rather than row by row.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StartleKnob k in StartleKnobs.All)
        {
            Assert.True(names.Add(k.Name), $"{k.Name} appears twice in the knob table");
            Assert.True(StartleKnobs.TryByName(k.Name, out StartleKnob found));
            Assert.Equal(k.Name, found.Name);

            // Set each end of its own window and read it back: a row whose Get and Set point at
            // different fields passes every other test in this file. The ENDS rather than a
            // fraction of the window, because a 0/1 row rounds anything in between to 1 and the
            // round trip would then fail for a knob that is working perfectly.
            foreach (float probe in new[] { k.Min, k.Max })
            {
                StartleTuning moved = k.Set(StartleTuning.Default, probe);
                Near(probe, k.Get(moved), 4);
            }
        }
        Assert.False(StartleKnobs.TryByName("NotAKnob", out _));
    }

    [Fact]
    public void Overlay_EveryKnobsTableDefault_IsTheLiteralOnTheShippedTuning()
    {
        // ToJson's sparseness is decided by comparing against StartleKnob.Default, so a table
        // default that disagreed with StartleTuning.Default would write a knob that had not
        // moved — or, worse, omit one that had.
        foreach (StartleKnob k in StartleKnobs.All)
            Assert.Equal(k.Default, k.Get(StartleTuning.Default));
    }
}
