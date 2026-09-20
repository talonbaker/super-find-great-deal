using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MpFoundation.Game.Props;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>SFX-2's Godot-free gate</b> (2026-09-19): the three pieces of arithmetic that decide what a
/// non-host peer hears — the per-tick impact limiter, the release byte that tells a place from a
/// throw, and the intensity byte that rides the impact event.
///
/// <para><b>What can be pinned here and what cannot.</b> Everything below is pure managed code
/// over <c>Godot.Mathf</c>/<c>Vector3</c> (ordinary struct math in GodotSharp.dll, no native
/// runtime), which is why it runs with no engine. What is NOT here is the thing the packet is
/// actually about — whether the OTHER PLAYER hears the can. That is a fact about two processes
/// and a socket, it is measured by <c>tests/Run-MaterialSfxTest.ps1</c>, and no unit test can
/// stand in for it. These assertions defend the rules that suite's measurement depends on.</para>
/// </summary>
public class PropImpactWireTests
{
    // -----------------------------------------------------------------------------------------
    // 1. The per-tick limiter
    // -----------------------------------------------------------------------------------------

    private static ImpactBudget.PendingImpact P(int id, int intensity) =>
        new(id, (byte)intensity, new Vector3(id, 0, 0));

    [Fact]
    public void Limiter_KeepsTheLoudest_AndDropsTheRest()
    {
        var pending = new List<ImpactBudget.PendingImpact>
        {
            P(1, 10), P(2, 250), P(3, 5), P(4, 200), P(5, 190), P(6, 255), P(7, 1),
        };

        int kept = ImpactBudget.KeepLoudest(pending, ImpactBudget.MaxImpactsPerTick);

        Assert.Equal(4, kept);
        // The survivors are the four loudest, in descending order of intensity. The rest of the
        // list is untouched garbage the caller never reads.
        Assert.Equal(new[] { 6, 2, 4, 5 }, pending.Take(kept).Select(e => e.PropId).ToArray());
        Assert.Equal(new byte[] { 255, 250, 200, 190 },
            pending.Take(kept).Select(e => e.Intensity).ToArray());
    }

    /// <summary><b>The case that actually happens.</b> Forty props released on the same tick from
    /// the same height arrive at the floor with the SAME speed, so the whole burst has one
    /// intensity and "loudest wins" decides nothing at all. Without the prop-id tie-break the
    /// survivors would be whatever <c>List.Sort</c>'s unstable partition happened to leave in
    /// front — a different four every run, which is exactly the shape of bug a suite cannot
    /// catch because it never fails twice the same way.</summary>
    [Fact]
    public void Limiter_AllEqualIntensity_IsDeterministicByPropId()
    {
        // Ids deliberately not in ascending order: a sort that kept input order would pass a
        // version of this test where they were.
        int[] ids = { 40, 7, 19, 3, 28, 11, 33, 1, 22, 15 };

        for (int run = 0; run < 3; run++)
        {
            var pending = ids.Select(id => P(id, 128)).ToList();
            int kept = ImpactBudget.KeepLoudest(pending, ImpactBudget.MaxImpactsPerTick);
            Assert.Equal(4, kept);
            Assert.Equal(new[] { 1, 3, 7, 11 }, pending.Take(kept).Select(e => e.PropId).ToArray());
        }
    }

    [Fact]
    public void Limiter_TiesAtTheCutoff_BreakByPropId()
    {
        // Three at 200 competing for two remaining slots after the single 255.
        var pending = new List<ImpactBudget.PendingImpact> { P(9, 200), P(2, 255), P(4, 200), P(6, 200) };
        int kept = ImpactBudget.KeepLoudest(pending, 3);
        Assert.Equal(3, kept);
        Assert.Equal(new[] { 2, 4, 6 }, pending.Take(kept).Select(e => e.PropId).ToArray());
    }

    [Fact]
    public void Limiter_UnderBudget_KeepsEverythingAndDoesNotReorder()
    {
        var pending = new List<ImpactBudget.PendingImpact> { P(5, 10), P(1, 250), P(3, 90) };
        int kept = ImpactBudget.KeepLoudest(pending, ImpactBudget.MaxImpactsPerTick);
        Assert.Equal(3, kept);
        // Left alone: sorting a list that fits is work nobody asked for, and the caller sends
        // all of it either way.
        Assert.Equal(new[] { 5, 1, 3 }, pending.Select(e => e.PropId).ToArray());
    }

    [Fact]
    public void Limiter_EmptyAndZeroBudget_AreBothWellDefined()
    {
        var empty = new List<ImpactBudget.PendingImpact>();
        Assert.Equal(0, ImpactBudget.KeepLoudest(empty, ImpactBudget.MaxImpactsPerTick));

        var some = new List<ImpactBudget.PendingImpact> { P(1, 200), P(2, 10) };
        Assert.Equal(0, ImpactBudget.KeepLoudest(some, 0));
        Assert.Equal(0, ImpactBudget.KeepLoudest(some, -3));
    }

    /// <summary>The cap is a number the handoff quotes and the suite measures against, so it is
    /// pinned rather than left free to drift.</summary>
    [Fact]
    public void Limiter_BudgetIsFourPerTick()
    {
        Assert.Equal(4, ImpactBudget.MaxImpactsPerTick);
    }

    // -----------------------------------------------------------------------------------------
    // 2. The release byte
    // -----------------------------------------------------------------------------------------

    /// <summary>Ordinals ride the wire. Renumbering one would turn every place in a mixed-version
    /// session into a throw, which is a sound about the arm played for a set-down — the exact
    /// defect this byte exists to remove.</summary>
    [Fact]
    public void ReleaseOrdinals_AreTheWireContract()
    {
        Assert.Equal(0, (int)PropRelease.None);
        Assert.Equal(1, (int)PropRelease.Dropped);
        Assert.Equal(2, (int)PropRelease.Placed);
        Assert.Equal(3, (int)PropRelease.Thrown);
    }

    [Theory]
    [InlineData(PropRelease.None)]
    [InlineData(PropRelease.Dropped)]
    [InlineData(PropRelease.Placed)]
    [InlineData(PropRelease.Thrown)]
    public void ReleaseByte_RoundTripsThroughTheWireAndThroughPropState(PropRelease release)
    {
        // The wire half: encode -> int -> decode.
        Assert.Equal(release, PropReleaseWire.Decode(PropReleaseWire.Encode(release)));

        // The state half: AsLoose carries it, and a `with` that changes something else keeps it.
        var at = new Transform3D(Basis.Identity, new Vector3(1, 2, 3));
        PropState held = new PropState(7, PropKind.Can, PropMode.Held, 42, Transform3D.Identity)
            .AsHeld(42);
        PropState loose = held.AsLoose(at, release);

        Assert.Equal(PropMode.Loose, loose.Mode);
        Assert.Equal(0, loose.HolderPeerId);
        Assert.Equal(at, loose.Transform);
        Assert.Equal(release, loose.Release);
        Assert.Equal(release, (loose with { Transform = Transform3D.Identity }).Release);

        // And the whole trip a real release takes: registry -> broadcast int -> receiving peer.
        Assert.Equal(release, PropReleaseWire.Decode(PropReleaseWire.Encode(loose.Release)));
    }

    /// <summary><b>An ordinal this build has no member for is silence, not a guess.</b> A doctored
    /// client, or a future build with a fifth verb, must not be able to pick which sound plays by
    /// sending a number — and the one safe direction for an unknown value is the one that plays
    /// nothing.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(255)]
    [InlineData(9999)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void ReleaseByte_UnknownOrdinalDecodesToNone(int wire)
    {
        Assert.Equal(PropRelease.None, PropReleaseWire.Decode(wire));
    }

    /// <summary><b>The packet's own rule, as a test.</b> A reset edge and a settle are Resting
    /// transitions, and a Resting transition must clear the verb — a byte that survived the
    /// latch would replay the throw the moment the can stopped rolling, and the round's reset
    /// would announce every prop in the world being dropped at once.</summary>
    [Fact]
    public void RestingAndHeld_ClearTheReleaseByte()
    {
        var at = new Transform3D(Basis.Identity, new Vector3(4, 0, 0));
        PropState thrown = new PropState(3, PropKind.Box, PropMode.Held, 9, Transform3D.Identity)
            .AsLoose(at, PropRelease.Thrown);
        Assert.Equal(PropRelease.Thrown, thrown.Release);

        Assert.Equal(PropRelease.None, thrown.AsResting(at).Release);
        Assert.Equal(PropRelease.None, thrown.AsHeld(9).Release);
    }

    /// <summary>A caller with no verb to report stores None rather than inheriting whatever the
    /// prop was last released by.</summary>
    [Fact]
    public void AsLoose_DefaultsToNone()
    {
        PropState s = new PropState(1, PropKind.Produce, PropMode.Held, 5, Transform3D.Identity)
            .AsLoose(Transform3D.Identity, PropRelease.Placed)
            .AsHeld(5)
            .AsLoose(Transform3D.Identity);
        Assert.Equal(PropRelease.None, s.Release);
    }

    /// <summary>The registry is the funnel every release goes through, and it must carry the verb
    /// the caller names — and refuse one for a prop nobody is holding, exactly as before.</summary>
    [Fact]
    public void Registry_Release_CarriesTheVerb()
    {
        var reg = new PropRegistry();
        int id = reg.Register(PropKind.Can, Transform3D.Identity);
        var at = new Transform3D(Basis.Identity, new Vector3(2, 0, 2));

        Assert.False(reg.Release(id, at, PropRelease.Placed)); // not held yet
        Assert.True(reg.SetHolder(id, 11));
        Assert.True(reg.Release(id, at, PropRelease.Placed));
        Assert.True(reg.TryGet(id, out PropState s));
        Assert.Equal(PropMode.Loose, s.Mode);
        Assert.Equal(PropRelease.Placed, s.Release);

        Assert.True(reg.SetResting(id, at));
        Assert.True(reg.TryGet(id, out PropState rested));
        Assert.Equal(PropRelease.None, rested.Release);
    }

    // -----------------------------------------------------------------------------------------
    // 3. The intensity byte
    // -----------------------------------------------------------------------------------------

    /// <summary><b>Harder hits are never quieter on the wire.</b> The whole chain — relative
    /// contact speed in m/s, through <c>Carryable.ImpactIntensity</c>, through the byte, back to
    /// the 0..1 a receiving peer plays at — has to be monotonic, or a peer's sense of how hard
    /// something was hit disagrees with the physics that hit it. Swept at 0.05 m/s from below the
    /// audible floor to well past the ceiling, including the clamped tails.</summary>
    [Fact]
    public void IntensityByte_IsMonotonicFromMetresPerSecondAndBack()
    {
        byte lastByte = 0;
        float lastPlayed = -1f;
        for (float mps = 0f; mps <= 14f; mps += 0.05f)
        {
            float i01 = Carryable.ImpactIntensity(mps);
            byte wire = ImpactBudget.IntensityToByte(i01);
            float played = ImpactBudget.ByteToIntensity(wire);

            Assert.True(wire >= lastByte,
                $"byte went DOWN at {mps:F2} m/s: {lastByte} -> {wire}");
            Assert.True(played >= lastPlayed,
                $"played intensity went DOWN at {mps:F2} m/s: {lastPlayed} -> {played}");
            Assert.InRange(played, 0f, 1f);
            lastByte = wire;
            lastPlayed = played;
        }
        // And it actually moved: a constant function is monotonic too.
        Assert.Equal(255, lastByte);
    }

    [Fact]
    public void IntensityByte_ClampsAtBothEndsOfTheSpeedRamp()
    {
        // At or below the audible floor the quietest audible hit is 0; at or above the ceiling,
        // full. Both ends are Carryable's clamp, restated here because the BYTE's endpoints are
        // what a peer receives.
        Assert.Equal(0, ImpactBudget.IntensityToByte(Carryable.ImpactIntensity(0f)));
        Assert.Equal(0, ImpactBudget.IntensityToByte(Carryable.ImpactIntensity(Carryable.ThunkSpeedThreshold)));
        Assert.Equal(255, ImpactBudget.IntensityToByte(Carryable.ImpactIntensity(Carryable.ImpactSpeedCeiling)));
        Assert.Equal(255, ImpactBudget.IntensityToByte(Carryable.ImpactIntensity(500f)));

        // Pathological inputs cannot escape the byte.
        Assert.Equal(0, ImpactBudget.IntensityToByte(-5f));
        Assert.Equal(255, ImpactBudget.IntensityToByte(5f));
        Assert.Equal(0, ImpactBudget.IntensityToByte(float.NegativeInfinity));
        Assert.Equal(255, ImpactBudget.IntensityToByte(float.PositiveInfinity));
    }

    [Fact]
    public void IntensityByte_EndpointsInvertExactly_AndTheMiddleIsWithinHalfAStep()
    {
        Assert.Equal(0f, ImpactBudget.ByteToIntensity(ImpactBudget.IntensityToByte(0f)));
        Assert.Equal(1f, ImpactBudget.ByteToIntensity(ImpactBudget.IntensityToByte(1f)));

        const float halfStep = 0.5f / 255f + 1e-6f;
        for (float i = 0f; i <= 1f; i += 1f / 512f)
        {
            float back = ImpactBudget.ByteToIntensity(ImpactBudget.IntensityToByte(i));
            Assert.True(Math.Abs(back - i) <= halfStep,
                $"quantisation error {Math.Abs(back - i):F6} at intensity {i:F4}");
        }
    }

    /// <summary>Byte -> intensity -> byte is the identity for all 256 values, which is what makes
    /// the limiter's ranking (done on bytes) and the receiver's volume (done on floats) describe
    /// the same ordering.</summary>
    [Fact]
    public void IntensityByte_SurvivesARoundTripForEveryValue()
    {
        for (int b = 0; b <= 255; b++)
            Assert.Equal((byte)b, ImpactBudget.IntensityToByte(ImpactBudget.ByteToIntensity((byte)b)));
    }
}
