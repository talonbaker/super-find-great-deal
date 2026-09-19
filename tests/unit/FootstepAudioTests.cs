using System;
using System.Collections.Generic;
using MpFoundation;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.World;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// The engine-free half of "you can hear the others move" (2026-08-13).
///
/// Remote players' footsteps became audible in the same change that gave one-shots a distance
/// cull, and the two halves are not separable: six players' worth of uncapped footsteps is 12 of
/// <c>SfxLab.PoolSize</c> = 14 one-shot slots, which would steal voices from the glow-stick crack,
/// the match strike and (plan §7.3) creature voices. So the tests that matter most here are the
/// budget ones, and they are arithmetic — which is exactly the kind of claim that should never be
/// left as a sentence in a doc comment.
///
/// <b>Every check below carries a positive control or is one.</b> Repo rule, learned the hard way:
/// a diagnostic that reports "absent" is worthless until it has been shown able to report
/// "present". The three that carry an explicit one say what they demonstrated — the taper, the
/// cull, and the steal policy, which is the one that was silently wrong for the whole life of the
/// class and would have passed any test that only ever asked "did something get stolen".
///
/// What cannot be proved here: that a remote proxy's node actually fires the cosmetic (a live
/// scene tree — <c>BlindNightSelfTest</c>), and that any of it sounds right (a headed session with
/// more than one human in it, which no automated tier will ever cover).
/// </summary>
public class FootstepCullTests
{
    // --- The taper -------------------------------------------------------------------------

    [Fact]
    public void FullVolumeInsideTheNearBand()
    {
        Assert.Equal(1f, FootstepAudioDirector.GainFor(0f));
        Assert.Equal(1f, FootstepAudioDirector.GainFor(FootstepAudioDirector.FullVolumeM * 0.5f));
        Assert.Equal(1f, FootstepAudioDirector.GainFor(FootstepAudioDirector.FullVolumeM));
        Assert.Equal(0f, FootstepAudioDirector.TrimDbFor(1f));
    }

    /// <summary>The whole anti-pop argument in one assertion: the gain reaches EXACTLY zero at the
    /// distance the cull happens, so the cut always lands on silence.
    ///
    /// The positive control is the point. A hard on/off cull — the implementation anyone writes
    /// first — would read 1.0 right up to the horizon and 0 immediately after, and every other
    /// assertion in this class would still pass. So the control asserts the value just inside the
    /// horizon is nearly silent, which is the one thing the naive version gets wrong.</summary>
    [Fact]
    public void TheTaperReachesTrueSilenceExactlyAtTheCullDistance()
    {
        Assert.Equal(0f, FootstepAudioDirector.GainFor(FootstepAudioDirector.HorizonM));
        Assert.Equal(0f, FootstepAudioDirector.GainFor(FootstepAudioDirector.HorizonM + 50f));
        Assert.Equal(SfxLab.SilentDb, FootstepAudioDirector.TrimDbFor(FootstepAudioDirector.HorizonM));

        // POSITIVE CONTROL: just inside the horizon the step must already be effectively gone.
        // A hard on/off cull would report full level here and this assertion would fail — which
        // is what makes the assertions above evidence rather than restatement.
        float justInside = FootstepAudioDirector.GainFor(FootstepAudioDirector.HorizonM - 0.25f);
        Assert.True(justInside < 0.05f,
            $"POSITIVE CONTROL FAILED: gain {justInside:F3} a quarter-metre inside the cull — the "
            + "taper is not tapering, so the cull is a hard on/off edge and a footstep would pop "
            + "in and out as somebody walks the threshold");
        Assert.True(FootstepAudioDirector.TrimDbFor(FootstepAudioDirector.HorizonM - 0.25f) < -25f);
    }

    [Fact]
    public void TheTaperIsMonotonicAndLinearInAmplitude()
    {
        float prev = 1.01f;
        for (float d = 0f; d <= FootstepAudioDirector.HorizonM; d += 0.25f)
        {
            float g = FootstepAudioDirector.GainFor(d);
            Assert.True(g <= prev, $"gain rose from {prev} to {g} at {d} m — the taper is not monotonic");
            prev = g;
        }

        // Linear in amplitude, not in dB: the midpoint of the band is half amplitude, i.e. -6 dB.
        // Linear-in-dB would put it at -40 dB, which spends most of the band inaudible and then
        // arrives suddenly — a taper with the perceptual shape of a cut. Same argument
        // LoopingSfxEmitter.GainToDb already records for the fire's fade.
        float mid = (FootstepAudioDirector.FullVolumeM + FootstepAudioDirector.HorizonM) * 0.5f;
        Assert.Equal(0.5f, FootstepAudioDirector.GainFor(mid), 0.0001f);
        Assert.Equal(-6.0206f, FootstepAudioDirector.TrimDbFor(mid), 0.01f);
    }

    [Fact]
    public void UnresolvedDistancesAreSilentRatherThanVeryClose()
    {
        Assert.Equal(0f, FootstepAudioDirector.GainFor(float.NaN));
        Assert.Equal(0f, FootstepAudioDirector.GainFor(float.PositiveInfinity));
        Assert.Equal(0f, FootstepAudioDirector.GainFor(-1f));
    }

    // --- The cull, as the director actually ranks it ------------------------------------------

    private static bool[] Rank(float[] distances, bool[]? incumbent = null) =>
        NearestKAudio.Select(distances, incumbent, FootstepAudioDirector.AudibleStepperCap,
            FootstepAudioDirector.RankHysteresisM, FootstepAudioDirector.HorizonM);

    /// <summary>The named requirement: a distant emitter is dropped and a near one is not.
    ///
    /// With the positive control that makes it mean something — the SAME six positions, ranked
    /// with the cull removed (an unlimited cap and an unlimited horizon), must grant all six. If
    /// that control granted only four as well, the four-of-six result below would be telling us
    /// about the fixture rather than about the cull.</summary>
    [Fact]
    public void ADistantStepperIsCulledAndANearOneIsNot()
    {
        // Two close, two mid, two far — one of the far pair beyond the horizon outright.
        var d = new[] { 2f, 6f, 12f, 17f, 25f, 80f };
        bool[] got = Rank(d);

        Assert.True(got[0] && got[1] && got[2] && got[3], "the four nearest steppers should sound");
        Assert.False(got[4], "the fifth-nearest is past the cap and must be silent");
        Assert.False(got[5], "80 m is past the horizon and must be silent regardless of rank");

        int granted = 0;
        foreach (bool g in got)
        {
            if (g)
                granted++;
        }
        Assert.Equal(FootstepAudioDirector.AudibleStepperCap, granted);

        // POSITIVE CONTROL: the same six, unculled.
        bool[] uncapped = NearestKAudio.Select(d, null, 99, 0f, float.PositiveInfinity);
        Assert.All(uncapped, g => Assert.True(g,
            "POSITIVE CONTROL FAILED: with the cap and horizon removed these six positions still "
            + "did not all sound, so the four-of-six result above is a property of the fixture "
            + "rather than of the cull"));
    }

    [Fact]
    public void TheHorizonBindsEvenWhenThereIsRoomUnderTheCap()
    {
        var d = new[] { FootstepAudioDirector.HorizonM + 0.5f, FootstepAudioDirector.HorizonM * 4f };
        Assert.All(Rank(d), g => Assert.False(g,
            "a lone stepper past the horizon must not sound just because no one is competing"));

        // And the gain agrees with the rank: nothing was removed while still audible.
        foreach (float x in d)
            Assert.Equal(0f, FootstepAudioDirector.GainFor(x));
    }

    /// <summary>Boundary chatter is the failure a naive nearest-K always ships, and for footsteps
    /// it is worse than absence: a set of footsteps that flickers on and off as somebody walks
    /// reads as a broken game rather than as distance. Jitter the fifth-nearest stepper around the
    /// rank boundary for 400 frames and require the granted set never to change.</summary>
    [Fact]
    public void TheRankBoundaryDoesNotChatterUnderJitter()
    {
        var baseline = new[] { 3f, 8f, 12f, 15f, 15.4f, 40f };
        bool[] state = Rank(baseline);
        bool[] first = (bool[])state.Clone();

        var rng = new Random(9013);
        var d = (float[])baseline.Clone();
        for (int frame = 0; frame < 400; frame++)
        {
            // Sub-metre wobble on everyone — well inside RankHysteresisM, which is what the
            // incumbent bonus exists to absorb.
            for (int i = 0; i < d.Length; i++)
                d[i] = baseline[i] + (float)(rng.NextDouble() * 0.8 - 0.4);
            state = Rank(d, state);
            Assert.Equal(first, state);
        }
    }

    [Fact]
    public void AGenuineOvertakeStillSwapsTheSet()
    {
        var baseline = new[] { 3f, 8f, 12f, 15f, 30f };
        bool[] state = Rank(baseline);
        Assert.False(state[4], "30 m starts outside the cap of four");

        // Walk index 4 decisively past index 3 — more than the incumbent's bonus, so this is
        // intent rather than jitter and the set must follow.
        var moved = (float[])baseline.Clone();
        moved[4] = 15f - FootstepAudioDirector.RankHysteresisM - 1f;
        state = Rank(moved, state);
        Assert.True(state[4], "a challenger that cleared the hysteresis outright should take the slot");
        Assert.False(state[3], "and the incumbent it beat should have lost it");
    }

    // --- The budget -----------------------------------------------------------------------

    /// <summary>The reason half 2 of this change is not optional, as arithmetic.
    ///
    /// Six sprinting players planting on the same frame is the adversarial peak, and the positive
    /// control is the uncapped figure: 12 of 14, which the same comparison must recognise as
    /// unsafe. A budget assertion that has only ever been run against a passing configuration
    /// certifies nothing — the same reasoning <c>BlindNightSelfTest</c>'s ceiling check records.</summary>
    [Fact]
    public void SixSprintingPlayersStayInsideTheOneShotBudget()
    {
        const int Players = Protocol.MaxPlayers; // 6
        const int ShotsPerFootfall = 2;          // step_pad + step_squeak, per puffling_presentation.tres

        int culled = FootstepAudioDirector.WorstCaseOneShotSlots(Players, ShotsPerFootfall);
        Assert.Equal(8, culled);
        Assert.True(culled <= SfxLab.PoolSize,
            $"{culled} of {SfxLab.PoolSize} one-shot slots held by footsteps alone");

        // The headroom is the whole point: what is LEFT for the cues that carry meaning.
        int headroom = SfxLab.PoolSize - culled;
        Assert.True(headroom >= 6,
            $"only {headroom} one-shot slots left for the glow-stick crack, the match strike, the "
            + "shutter and creature voices — the cap is too generous");

        // Water self-caps at four voices and cannot stack in the worst case (a player is either
        // thrashing in the lake or running on land), but even the impossible sum stays inside.
        Assert.True(culled + 4 <= SfxLab.PoolSize,
            "footsteps at cap plus water at its own cap would exceed the one-shot pool");

        // POSITIVE CONTROL: the uncapped figure this change exists to prevent.
        int uncapped = Players * ShotsPerFootfall;
        Assert.Equal(12, uncapped);
        Assert.True(uncapped > SfxLab.PoolSize - 6,
            $"POSITIVE CONTROL FAILED: {uncapped} of {SfxLab.PoolSize} slots was not recognised as "
            + "eating the headroom, so the safety assertion above certifies nothing");
    }

    [Fact]
    public void TheCapIsWhatBoundsTheBudgetRatherThanTheLobbySize()
    {
        // Under the cap, cost tracks the lobby. Over it, it flattens — that flattening IS the cull.
        Assert.Equal(2, FootstepAudioDirector.WorstCaseOneShotSlots(1, 2));
        Assert.Equal(6, FootstepAudioDirector.WorstCaseOneShotSlots(3, 2));
        Assert.Equal(8, FootstepAudioDirector.WorstCaseOneShotSlots(4, 2));
        Assert.Equal(8, FootstepAudioDirector.WorstCaseOneShotSlots(6, 2));
        Assert.Equal(8, FootstepAudioDirector.WorstCaseOneShotSlots(60, 2));
        Assert.Equal(0, FootstepAudioDirector.WorstCaseOneShotSlots(0, 2));

        // A third layer added to the profile is a 50% cost increase, and the function says so
        // rather than hiding it behind a hardcoded 2.
        Assert.Equal(12, FootstepAudioDirector.WorstCaseOneShotSlots(6, 3));
    }
}

/// <summary>
/// The one-shot steal policy — <c>SfxLab</c>'s doc and its code disagreed for the whole life of
/// the class, and remote footsteps are what made the disagreement reachable.
/// </summary>
public class OneShotStealPolicyTests
{
    /// <summary>The correction, and the control that shows it is a correction rather than a
    /// restatement.
    ///
    /// The old policy stole <c>Pool[_next]</c>, a cursor advanced only when a steal happened. The
    /// fixture below is the ordinary case that breaks it: slot 0 was refreshed most recently
    /// (the free-list scan always prefers the lowest index, so slot 0 is the busiest), and the
    /// cursor sits at 0 because nothing had been stolen yet. Oldest-first must pick slot 3;
    /// the cursor would have picked slot 0 — the NEWEST sound in the pool, i.e. the one whose
    /// theft is most audible.</summary>
    [Fact]
    public void TheOldestShotIsStolenAndNotTheCursorsTarget()
    {
        // Rent stamps, index-parallel to the pool. Slot 3 was rented longest ago.
        var rentedAt = new ulong[] { 90, 71, 64, 12, 55 };
        int victim = SfxLab.OldestRentedIndex(rentedAt);
        Assert.Equal(3, victim);

        // POSITIVE CONTROL: what the round-robin cursor would have done with the same pool.
        // If these agreed, this test could not tell the two policies apart and would have passed
        // against the defect it exists to pin.
        const int cursorVictim = 0;
        Assert.NotEqual(cursorVictim, victim);
        Assert.True(rentedAt[cursorVictim] > rentedAt[victim],
            "POSITIVE CONTROL FAILED: the cursor's victim was not newer than the oldest-first "
            + "victim on this fixture, so the fixture does not distinguish the two policies");
    }

    [Fact]
    public void OldestFirstIsStableAndTotal()
    {
        Assert.Equal(-1, SfxLab.OldestRentedIndex(Array.Empty<ulong>()));
        Assert.Equal(-1, SfxLab.OldestRentedIndex(null!));
        Assert.Equal(0, SfxLab.OldestRentedIndex(new ulong[] { 7 }));

        // A monotonic counter cannot tie, but if it somehow did the lowest index must win, so the
        // choice is deterministic frame to frame rather than dependent on iteration luck.
        Assert.Equal(0, SfxLab.OldestRentedIndex(new ulong[] { 5, 5, 5 }));
    }

    /// <summary>Drive the policy the way the pool does: fourteen slots, a stream of rents that
    /// prefers free slots and steals when full, and the invariant that no shot is ever stolen
    /// while an older one is still in the pool.</summary>
    [Fact]
    public void AFullPoolNeverStealsANewerShotThanTheOldestAvailable()
    {
        int n = SfxLab.PoolSize;
        var rentedAt = new ulong[n];
        ulong counter = 0;
        for (int i = 0; i < n; i++)
            rentedAt[i] = ++counter;

        var rng = new Random(4242);
        for (int shot = 0; shot < 5000; shot++)
        {
            int victim = SfxLab.OldestRentedIndex(rentedAt);
            for (int i = 0; i < n; i++)
            {
                Assert.True(rentedAt[i] >= rentedAt[victim],
                    $"slot {i} (stamp {rentedAt[i]}) is older than the chosen victim {victim} "
                    + $"(stamp {rentedAt[victim]})");
            }
            rentedAt[victim] = ++counter;

            // Occasionally a slot frees up and gets reused out of order — the exact situation the
            // cursor got wrong, since a freed low-index slot becomes the newest while the cursor
            // still points at it.
            if (rng.NextDouble() < 0.3)
                rentedAt[rng.Next(n)] = ++counter;
        }
    }

    [Fact]
    public void TheListOverloadAgreesWithTheArrayOverload()
    {
        var list = new List<ulong> { 40, 3, 99 };
        Assert.Equal(1, SfxLab.OldestRentedIndex(list));
    }
}
