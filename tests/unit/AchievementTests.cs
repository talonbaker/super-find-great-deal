using System.Collections.Generic;
using MpFoundation.Game.Aim;
using MpFoundation.Net;
using Sail.Game.Achievements;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// W7-5 (Talon's playtest note 12): four in-game achievements — Duck walk, Tough guy, Tough
/// guy duck walk, and Bubblholic (his spelling, shipped verbatim). Godot-free: everything here
/// is <see cref="AchievementUnlocker"/> / <see cref="AchievementTracker"/> / <see
/// cref="BubblholicRule"/>'s pure logic, fed the same <see cref="MoveVerb"/> / <see
/// cref="AimStance"/> values <c>SandboxAvatar.OwnerTick</c> already resolves each tick — no
/// Godot node, no persistence, no toast. The disk-persistence half
/// (<c>AchievementStore</c>, a <c>Godot.ConfigFile</c> reader/writer) follows the
/// <c>OnboardingSettings.cs</c> / <c>GraphicsSettings.cs</c> precedent of being exercised at
/// runtime rather than under this Godot-free suite — see those classes' own test files, neither
/// of which tests Save/Load either.
/// </summary>
public class AchievementTests
{
    // ---------------------------------------------------------------------------------------
    // 1. AchievementUnlocker — idempotency (MECHANICS-BIBLE §4)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TryUnlock_FirstCall_UnlocksAndFiresEventExactlyOnce()
    {
        var unlocker = new AchievementUnlocker();
        int fired = 0;
        unlocker.Unlocked += _ => fired++;

        bool result = unlocker.TryUnlock(AchievementId.DuckWalk);

        Assert.True(result);
        Assert.True(unlocker.IsEarned(AchievementId.DuckWalk));
        Assert.Equal(1, fired);
    }

    /// <summary>Acceptance criterion 3: "test it over a long trigger stream, not a single
    /// repeat." Five thousand repeat triggers of the same id: earned once, event fires once.</summary>
    [Fact]
    public void TryUnlock_TriggeredAcrossALongStream_UnlocksExactlyOnceAndFiresExactlyOnce()
    {
        var unlocker = new AchievementUnlocker();
        int fired = 0;
        unlocker.Unlocked += id =>
        {
            if (id == AchievementId.ToughGuy)
                fired++;
        };

        for (int i = 0; i < 5000; i++)
            unlocker.TryUnlock(AchievementId.ToughGuy);

        Assert.True(unlocker.IsEarned(AchievementId.ToughGuy));
        Assert.Equal(1, fired);
    }

    /// <summary>Two different ids unlocked on the very same call each fire independently —
    /// idempotency per-id, not a single global latch.</summary>
    [Fact]
    public void TryUnlock_TwoDifferentIds_BothUnlockIndependently()
    {
        var unlocker = new AchievementUnlocker();

        Assert.True(unlocker.TryUnlock(AchievementId.DuckWalk));
        Assert.True(unlocker.TryUnlock(AchievementId.ToughGuy));
        Assert.False(unlocker.TryUnlock(AchievementId.DuckWalk));

        Assert.True(unlocker.IsEarned(AchievementId.DuckWalk));
        Assert.True(unlocker.IsEarned(AchievementId.ToughGuy));
    }

    /// <summary>Loading already-earned ids (the persisted-state constructor path) seeds the
    /// earned set WITHOUT firing the event — a boot must never re-pop a toast for something
    /// earned last session.</summary>
    [Fact]
    public void ConstructedWithAlreadyEarnedIds_DoesNotFireUnlockedOnLoad()
    {
        int fired = 0;
        var unlocker = new AchievementUnlocker(new[] { AchievementId.Bubblholic });
        unlocker.Unlocked += _ => fired++;

        Assert.True(unlocker.IsEarned(AchievementId.Bubblholic));
        Assert.Equal(0, fired);

        // And it stays idempotent from here on.
        Assert.False(unlocker.TryUnlock(AchievementId.Bubblholic));
        Assert.Equal(0, fired);
    }

    // ---------------------------------------------------------------------------------------
    // 2. AchievementTracker — the four triggers, read off real player state
    // ---------------------------------------------------------------------------------------

    private const float Tick = 1f / 60f;

    /// <summary>"Duck walk" fires the instant MoveVerb becomes DuckWalk — the verb IS the
    /// duck walk's latch (MoveState.cs's own doc), so there is nothing else to detect.</summary>
    [Fact]
    public void DuckWalk_UnlocksTheInstantVerbBecomesDuckWalk()
    {
        var unlocker = new AchievementUnlocker();
        var tracker = new AchievementTracker(unlocker);

        tracker.Step(Tick, MoveVerb.Normal, AimStance.Lowered);
        Assert.False(unlocker.IsEarned(AchievementId.DuckWalk));

        tracker.Step(Tick, MoveVerb.DuckWalk, AimStance.Lowered);
        Assert.True(unlocker.IsEarned(AchievementId.DuckWalk));
    }

    /// <summary>Every other crouch verb (Tuck, Slide) must NOT trip the duck-walk achievement —
    /// it is a specific verb, not "any crouch".</summary>
    [Theory]
    [InlineData(MoveVerb.Normal)]
    [InlineData(MoveVerb.Tuck)]
    [InlineData(MoveVerb.Slide)]
    public void OtherVerbs_NeverUnlockDuckWalk(MoveVerb verb)
    {
        var unlocker = new AchievementUnlocker();
        var tracker = new AchievementTracker(unlocker);

        for (int i = 0; i < 200; i++)
            tracker.Step(Tick, verb, AimStance.Lowered);

        Assert.False(unlocker.IsEarned(AchievementId.DuckWalk));
    }

    /// <summary>"Tough guy" needs the aim rig fully Raised for at least
    /// <see cref="AchievementTracker.ToughGuyHoldSec"/> continuous seconds — Talon's "holds
    /// right mouse click for three seconds at least". Ticking just under the threshold must
    /// not unlock it.</summary>
    [Fact]
    public void ToughGuy_DoesNotUnlock_BeforeTheHoldThreshold()
    {
        var unlocker = new AchievementUnlocker();
        var tracker = new AchievementTracker(unlocker);

        float elapsed = 0f;
        while (elapsed < AchievementTracker.ToughGuyHoldSec - Tick)
        {
            tracker.Step(Tick, MoveVerb.Normal, AimStance.Raised);
            elapsed += Tick;
        }

        Assert.False(unlocker.IsEarned(AchievementId.ToughGuy));
    }

    /// <summary>Crossing the threshold, tick by tick, unlocks it.</summary>
    [Fact]
    public void ToughGuy_Unlocks_OnceHeldContinuouslyPastTheThreshold()
    {
        var unlocker = new AchievementUnlocker();
        var tracker = new AchievementTracker(unlocker);

        float elapsed = 0f;
        while (elapsed < AchievementTracker.ToughGuyHoldSec + Tick)
        {
            tracker.Step(Tick, MoveVerb.Normal, AimStance.Raised);
            elapsed += Tick;
        }

        Assert.True(unlocker.IsEarned(AchievementId.ToughGuy));
    }

    /// <summary>Releasing the pose before the threshold resets the hold clock — a player who
    /// dips in and out for a total of 3+ seconds spread across several short holds must not be
    /// credited; only a genuinely continuous hold counts.</summary>
    [Fact]
    public void ToughGuy_ReleasingBeforeThreshold_ResetsTheHoldClock_SoIntermittentHoldingNeverUnlocks()
    {
        var unlocker = new AchievementUnlocker();
        var tracker = new AchievementTracker(unlocker);

        // Hold for just under the threshold, release for one tick, then repeat many times.
        // Total held time across the run comfortably exceeds the threshold, but no single
        // continuous hold ever does.
        for (int cycle = 0; cycle < 50; cycle++)
        {
            float elapsed = 0f;
            while (elapsed < AchievementTracker.ToughGuyHoldSec - Tick)
            {
                tracker.Step(Tick, MoveVerb.Normal, AimStance.Raised);
                elapsed += Tick;
            }
            tracker.Step(Tick, MoveVerb.Normal, AimStance.Lowered); // release: resets the clock
        }

        Assert.False(unlocker.IsEarned(AchievementId.ToughGuy));
    }

    /// <summary>Raising and lowering mid-transition (Raising/Lowering) must not count as held —
    /// only the fully-committed Raised stance does.</summary>
    [Fact]
    public void ToughGuy_MidTransitionStances_DoNotAccumulateHoldTime()
    {
        var unlocker = new AchievementUnlocker();
        var tracker = new AchievementTracker(unlocker);

        for (int i = 0; i < 400; i++)
            tracker.Step(Tick, MoveVerb.Normal, AimStance.Raising);

        Assert.False(unlocker.IsEarned(AchievementId.ToughGuy));
    }

    /// <summary>
    /// Acceptance criterion 4, the one a sequence-based implementation silently breaks: "Tough
    /// guy duck walk" must be earnable WITHOUT having previously earned either component
    /// achievement. Fresh unlocker (nothing earned), a single step that crosses the tough-guy
    /// hold threshold while already duck-walking: the conjunction achievement lands in that
    /// step even though 1 and 2 had never separately fired before it.
    /// </summary>
    [Fact]
    public void ToughGuyDuckWalk_UnlocksOnTheVeryFirstQualifyingStep_WithNeitherComponentPreviouslyEarned()
    {
        var unlocker = new AchievementUnlocker();
        var tracker = new AchievementTracker(unlocker);

        Assert.False(unlocker.IsEarned(AchievementId.DuckWalk));
        Assert.False(unlocker.IsEarned(AchievementId.ToughGuy));
        Assert.False(unlocker.IsEarned(AchievementId.ToughGuyDuckWalk));

        // One big step, at least the full hold duration, already duck-walking and already
        // raised — a player who happens to land both for the first time at once. A
        // sequence-based gate ("if DuckWalk earned AND ToughGuy earned") would see both flags
        // still false going into this very step and refuse it.
        tracker.Step(AchievementTracker.ToughGuyHoldSec, MoveVerb.DuckWalk, AimStance.Raised);

        Assert.True(unlocker.IsEarned(AchievementId.ToughGuyDuckWalk));
    }

    /// <summary>The conjunction is a real AND: duck-walking alone, or holding tough-guy alone,
    /// must never unlock it by itself.</summary>
    [Fact]
    public void ToughGuyDuckWalk_NeverUnlocksFromEitherHalfAlone()
    {
        var duckOnly = new AchievementUnlocker();
        var duckTracker = new AchievementTracker(duckOnly);
        for (int i = 0; i < 400; i++)
            duckTracker.Step(Tick, MoveVerb.DuckWalk, AimStance.Lowered);
        Assert.False(duckOnly.IsEarned(AchievementId.ToughGuyDuckWalk));

        var toughOnly = new AchievementUnlocker();
        var toughTracker = new AchievementTracker(toughOnly);
        for (int i = 0; i < 400; i++)
            toughTracker.Step(Tick, MoveVerb.Normal, AimStance.Raised);
        Assert.True(toughOnly.IsEarned(AchievementId.ToughGuy)); // sanity: this alone DOES unlock #2
        Assert.False(toughOnly.IsEarned(AchievementId.ToughGuyDuckWalk));
    }

    /// <summary>The conjunction requires the FULL hold, not a mere instantaneous overlap — the
    /// packet's "held at the same time" reading: duck-walking while the aim rig is merely
    /// Raised for one tick (short of the 3 s threshold) is not "makes a tough guy pose", it is
    /// mid-raise.</summary>
    [Fact]
    public void ToughGuyDuckWalk_RequiresTheFullHoldConcurrentWithDuckWalking_NotAMereInstant()
    {
        var unlocker = new AchievementUnlocker();
        var tracker = new AchievementTracker(unlocker);

        float elapsed = 0f;
        while (elapsed < AchievementTracker.ToughGuyHoldSec - Tick)
        {
            tracker.Step(Tick, MoveVerb.DuckWalk, AimStance.Raised);
            elapsed += Tick;
        }

        Assert.False(unlocker.IsEarned(AchievementId.ToughGuyDuckWalk));
    }

    // ---------------------------------------------------------------------------------------
    // 3. Bubblholic — server-authoritative, identical on every peer (acceptance criterion 5)
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, false)]   // an empty world never trivially "completes"
    [InlineData(0, 12, false)]
    [InlineData(11, 12, false)]
    [InlineData(12, 12, true)]
    public void BubblholicRule_AllPopped_MatchesCountAgainstTotal(int count, int total, bool expected)
    {
        Assert.Equal(expected, BubblholicRule.AllPopped(count, total));
    }

    /// <summary>The whole multiplayer-parity argument, made as a test: two independent
    /// "peers" (two separate AchievementUnlocker instances, standing in for two players'
    /// clients) fed the IDENTICAL server-broadcast (count, total) pair reach the identical
    /// verdict. Nothing here is peer-specific — that is the point; a client-only guess is the
    /// defect this criterion exists to prevent, and there is structurally nowhere for one to
    /// creep in because the rule takes no peer identity at all.</summary>
    [Fact]
    public void BubblholicRule_GivenTheSameBroadcastCountAndTotal_EveryPeerReachesTheIdenticalVerdict()
    {
        var peerA = new AchievementUnlocker();
        var peerB = new AchievementUnlocker();

        const int count = 12, total = 12;
        bool aResult = BubblholicRule.AllPopped(count, total) && peerA.TryUnlock(AchievementId.Bubblholic);
        bool bResult = BubblholicRule.AllPopped(count, total) && peerB.TryUnlock(AchievementId.Bubblholic);

        Assert.True(aResult);
        Assert.True(bResult);
        Assert.Equal(peerA.IsEarned(AchievementId.Bubblholic), peerB.IsEarned(AchievementId.Bubblholic));
    }

    // ---------------------------------------------------------------------------------------
    // 4. Names — his spelling, verbatim
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void AllFourAchievements_CarryHisNamesExactly()
    {
        Assert.Equal("Duck walk", AchievementCatalog.NameFor(AchievementId.DuckWalk));
        Assert.Equal("Tough guy", AchievementCatalog.NameFor(AchievementId.ToughGuy));
        Assert.Equal("Tough guy duck walk", AchievementCatalog.NameFor(AchievementId.ToughGuyDuckWalk));
        // His spelling — "Bubblholic", not "Bubbleholic" — shipped exactly as written.
        Assert.Equal("Bubblholic", AchievementCatalog.NameFor(AchievementId.Bubblholic));
    }

    [Fact]
    public void AllFourIds_AreListedInTheCatalog()
    {
        var all = new HashSet<AchievementId>(AchievementCatalog.All);
        Assert.Equal(4, all.Count);
        Assert.Contains(AchievementId.DuckWalk, all);
        Assert.Contains(AchievementId.ToughGuy, all);
        Assert.Contains(AchievementId.ToughGuyDuckWalk, all);
        Assert.Contains(AchievementId.Bubblholic, all);
    }
}
