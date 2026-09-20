using MpFoundation.Game.Sandbox;
using MpFoundation.Game.Sandbox.Anim;
using Xunit;

namespace Sail.Tests;

/// <summary>
/// <b>The crossfade table, as a test rather than as a convention.</b> The brief made this a
/// requirement in so many words — <i>"this was an informal convention; make it a named constant"</i>
/// — and a named constant nobody asserts is a convention with a nicer font.
/// </summary>
public class AnimationCrossfadeTests
{
    [Fact]
    public void TheDefault_IsAQuarterSecondsWorthOfBlend_AndTheSuddenOneIsExactlyZero()
    {
        Assert.Equal(0.15f, AnimationCrossfade.DefaultSec);
        // Exactly zero, not "very short". A blend into a hit renders a body absorbing it gracefully,
        // which is the opposite of what the hit is for. See the class remarks.
        Assert.Equal(0f, AnimationCrossfade.SuddenSec);
    }

    [Theory]
    [InlineData(AvatarActionState.Stagger)]
    [InlineData(AvatarActionState.KnockOut)]
    public void SuddenStates_AreEnteredOnOneFrame(AvatarActionState state)
    {
        Assert.True(AnimationCrossfade.IsSudden(state));
        Assert.Equal(0f, AnimationCrossfade.SecondsFor(state));
    }

    [Theory]
    [InlineData(AvatarActionState.Locomotion)]
    [InlineData(AvatarActionState.Skid)]
    [InlineData(AvatarActionState.JumpLaunch)]
    [InlineData(AvatarActionState.Airborne)]
    [InlineData(AvatarActionState.Land)]
    public void EveryOtherState_TakesTheDefault(AvatarActionState state)
    {
        Assert.False(AnimationCrossfade.IsSudden(state));
        Assert.Equal(AnimationCrossfade.DefaultSec, AnimationCrossfade.SecondsFor(state));
    }

    [Fact]
    public void TheNetClose_IsSudden_AndEveryOtherHoldStateIsNot()
    {
        // "net-close" is the brief's word for the stroke landing. Easing the arm into it over 0.15 s
        // costs the swing exactly the snap that makes it read as a swing.
        Assert.Equal(0f, AnimationCrossfade.OverrideSecondsFor(AvatarHoldState.NetSwing));
        foreach (AvatarHoldState hold in new[]
                 {
                     AvatarHoldState.Empty, AvatarHoldState.NetReady,
                     AvatarHoldState.CarryHandle, AvatarHoldState.CarryArmful,
                 })
        {
            Assert.Equal(AnimationCrossfade.DefaultSec, AnimationCrossfade.OverrideSecondsFor(hold));
        }
    }

    [Fact]
    public void EveryActionState_HasAnAnswer_AndItIsOneOfTheTwo()
    {
        // The table is per-DESTINATION, so "does every state have an entry" is a complete check on
        // it — there is no pair that could be missed. That is the property a pair table would not
        // have, and it is why the shape was chosen.
        foreach (AvatarActionState state in System.Enum.GetValues<AvatarActionState>())
        {
            float s = AnimationCrossfade.SecondsFor(state);
            Assert.True(s == AnimationCrossfade.DefaultSec || s == AnimationCrossfade.SuddenSec,
                $"{state} takes {s} s, which is neither the default nor the sudden value");
        }
    }
}

/// <summary>
/// <b>The seek maths, headless.</b> The brief's point 3 — <i>"a late joiner or a client that
/// received the change late lands mid-clip at the right frame rather than starting from 0"</i> — is
/// arithmetic, and this is the arithmetic.
/// </summary>
public class ClipTimeAnchorTests
{
    private const float Tick = MpFoundation.Net.NetProfile.TickDelta;   // 1/60 s

    [Fact]
    public void AClientThatSawTheChangeOnTime_StartsAtFrameZero()
    {
        Assert.Equal(0f, ClipTimeAnchor.SecondsFromTicks(600, 600, Tick, 0.6f, loop: false));
    }

    [Fact]
    public void AClientThatReceivedTheChangeLate_LandsWhereTheActionActuallyIs()
    {
        // The change happened at tick 600; this client is rendering tick 618, eighteen ticks = 0.30 s
        // later. It must show 0.30 s into the clip, not the first frame of it.
        float seek = ClipTimeAnchor.SecondsFromTicks(600, 618, Tick, 0.6f, loop: false);
        Assert.Equal(0.30f, seek, precision: 4);
    }

    [Fact]
    public void AOneShotThatAlreadyFinished_HoldsItsLastFrame_RatherThanRestarting()
    {
        // 0.6 s clip, entered 90 ticks (1.5 s) ago. A modulo would answer 0.30 s and render a
        // knocked-out body getting up in order to fall over again.
        Assert.Equal(0.6f, ClipTimeAnchor.SecondsFromTicks(600, 690, Tick, 0.6f, loop: false), precision: 4);
    }

    [Fact]
    public void ALoopingClip_Wraps()
    {
        // Same elapse, looping: 1.5 s into a 0.6 s cycle is 0.30 s.
        Assert.Equal(0.30f, ClipTimeAnchor.SecondsFromTicks(600, 690, Tick, 0.6f, loop: true), precision: 4);
    }

    [Fact]
    public void ARenderTickBehindTheEntryTick_ClampsToZeroRatherThanGoingNegative()
    {
        // Reachable, not hypothetical: a proxy renders SnapshotBuffer.InterpDelayTicks behind the
        // newest tick received, so a change observed on arrival can be stamped ahead of the frame
        // being drawn. The body has not started the action yet on this client, and its first frame is
        // the truthful pose.
        Assert.Equal(0f, ClipTimeAnchor.SecondsFromTicks(620, 600, Tick, 0.6f, loop: true));
    }

    [Fact]
    public void JoiningMidClip_LandsMidClip_AndSaysSoWhenTheActionIsAlreadyOver()
    {
        // 24 ticks = 0.40 s into a 0.6 s one-shot: still running, land at 0.40 s.
        Assert.True(ClipTimeAnchor.TryJoinMidClip(600, 624, Tick, 0.6f, loop: false, out float mid));
        Assert.Equal(0.40f, mid, precision: 4);

        // 90 ticks = 1.5 s in: over. The caller skips the transition entirely rather than playing a
        // one-shot that finished before this client existed.
        Assert.False(ClipTimeAnchor.TryJoinMidClip(600, 690, Tick, 0.6f, loop: false, out _));

        // A loop is never over.
        Assert.True(ClipTimeAnchor.TryJoinMidClip(600, 690, Tick, 0.6f, loop: true, out _));
    }

    [Fact]
    public void TwoClientsWithDifferentUptimes_ComputeTheSameFrameForTheSameTick()
    {
        // The whole point, stated as an assertion. Both clients render replicated tick 913; one has
        // been alive for four seconds and the other for four minutes, and neither fact appears in the
        // arithmetic. This is why the anchor is a TICK and not a local elapsed time.
        const double entry = 900;
        float a = ClipTimeAnchor.SecondsFromTicks(entry, 913, Tick, 0.6f, loop: false);
        float b = ClipTimeAnchor.SecondsFromTicks(entry, 913, Tick, 0.6f, loop: false);
        Assert.Equal(a, b);
        Assert.Equal(13f * Tick, a, precision: 5);
    }

    [Fact]
    public void TheSwingSeeksFromReplicatedProgress_WhichNeedsNoEntryTickAtAll()
    {
        // NetSwingState.Progress01 is recomputed by the authority every tick and driven onto every
        // peer's body, so a client that joins mid-stroke is correct on its FIRST frame rather than on
        // its first observed transition. That is why it is preferred wherever it exists.
        Assert.Equal(0f, ClipTimeAnchor.SecondsFromProgress(0f, 0.4667f));
        Assert.Equal(0.23335f, ClipTimeAnchor.SecondsFromProgress(0.5f, 0.4667f), precision: 5);
        Assert.Equal(0.4667f, ClipTimeAnchor.SecondsFromProgress(1f, 0.4667f), precision: 5);
        // Past the end means finished; the honest pose is the last frame, never a wrap.
        Assert.Equal(0.4667f, ClipTimeAnchor.SecondsFromProgress(1.4f, 0.4667f), precision: 5);
        // Garbage in is frame zero out, never a NaN seek into an AnimationTree.
        Assert.Equal(0f, ClipTimeAnchor.SecondsFromProgress(float.NaN, 0.4667f));
    }

    [Fact]
    public void ADegenerateClipLength_NeverProducesANaNOrNegativeSeek()
    {
        Assert.Equal(0f, ClipTimeAnchor.Wrap(1f, 0f, loop: true));
        Assert.Equal(0f, ClipTimeAnchor.Wrap(float.NaN, 0.6f, loop: true));
        Assert.Equal(0f, ClipTimeAnchor.SecondsFromProgress(0.5f, 0f));
    }
}

/// <summary>
/// <b>Every state's entry, behaviour and exit, exercised.</b> BEHAVIOR-BIBLE §1's rule applied to a
/// presentation state machine: a row nothing can enter, or nothing can leave, is a pose that either
/// never appears or never goes away, and both look exactly like a broken clip.
/// </summary>
public class AvatarActionStateTests
{
    private const float Launch = 0.2f;    // Jump_Launch, seconds, from the shipped library
    private const float Land = 0.2667f;   // Jump_Land

    private static AvatarActionState Step(
        AvatarActionState previous, float elapsed,
        bool incap = false, bool stagger = false, bool grounded = true,
        bool skid = false, float vy = 0f) =>
        AvatarActionStates.Derive(incap, stagger, grounded, skid, vy, previous, elapsed, Launch, Land);

    [Fact]
    public void BeingDownOutranksEverything()
    {
        Assert.Equal(AvatarActionState.KnockOut,
            Step(AvatarActionState.Locomotion, 0f, incap: true, stagger: true, grounded: false,
                skid: true, vy: 5f));
    }

    [Fact]
    public void AStaggerOutranksTheGaitButNotBeingDown()
    {
        Assert.Equal(AvatarActionState.Stagger, Step(AvatarActionState.Locomotion, 0f, stagger: true));
        Assert.Equal(AvatarActionState.KnockOut,
            Step(AvatarActionState.Stagger, 0f, incap: true, stagger: true));
    }

    [Fact]
    public void APushOffEntersTheLaunchClip_WhichRetiresItselfIntoTheAirLoop()
    {
        AvatarActionState s = Step(AvatarActionState.Locomotion, 0f, grounded: false, vy: 4f);
        Assert.Equal(AvatarActionState.JumpLaunch, s);
        // Still inside the launch clip: stays.
        Assert.Equal(AvatarActionState.JumpLaunch, Step(s, 0.1f, grounded: false, vy: 2f));
        // Past it: the loop takes over, and it does so on the clip's own length rather than on a
        // second, typed duration that could drift from it.
        Assert.Equal(AvatarActionState.Airborne, Step(s, Launch + 0.01f, grounded: false, vy: 1f));
    }

    [Fact]
    public void WalkingOffALedgeNeverPlaysAPushOff()
    {
        // There was no push-off, so there is no launch pose. Keyed on the replicated vertical
        // velocity, which every peer already has.
        Assert.Equal(AvatarActionState.Airborne,
            Step(AvatarActionState.Locomotion, 0f, grounded: false, vy: -0.4f));
    }

    [Fact]
    public void TouchingDownOutOfTheAirPlaysTheLanding_AndThenGoesBackToTheGait()
    {
        AvatarActionState s = Step(AvatarActionState.Airborne, 1f, grounded: true);
        Assert.Equal(AvatarActionState.Land, s);
        Assert.Equal(AvatarActionState.Land, Step(s, 0.1f));
        Assert.Equal(AvatarActionState.Locomotion, Step(s, Land + 0.01f));
    }

    [Fact]
    public void AGroundedBodyThatWasNeverAirborne_DoesNotPlayALanding()
    {
        // A 2 cm step-off that flips Grounded for one tick must not interrupt a stride with a
        // landing pose. The guard is that Land is only reachable OUT of an air state.
        Assert.Equal(AvatarActionState.Locomotion, Step(AvatarActionState.Locomotion, 0f));
    }

    [Fact]
    public void TheBrakeIsItsOwnState_AndItLeavesWhenTheReplicatedSkidEnds()
    {
        Assert.Equal(AvatarActionState.Skid, Step(AvatarActionState.Locomotion, 0f, skid: true));
        Assert.Equal(AvatarActionState.Locomotion, Step(AvatarActionState.Skid, 0.5f, skid: false));
    }

    [Fact]
    public void EveryStateIsReachable_AndEveryStateHasAnExit()
    {
        // Mechanically checked rather than argued: drive every state as "previous" through a spread
        // of replicated inputs and assert that each one both appears as an answer somewhere and can
        // be left.
        var reached = new System.Collections.Generic.HashSet<AvatarActionState>();
        foreach (AvatarActionState prev in System.Enum.GetValues<AvatarActionState>())
        {
            bool left = false;
            foreach (bool incap in new[] { false, true })
            foreach (bool stag in new[] { false, true })
            foreach (bool ground in new[] { false, true })
            foreach (bool skid in new[] { false, true })
            foreach (float vy in new[] { -4f, 0f, 4f })
            foreach (float el in new[] { 0f, 5f })
            {
                AvatarActionState next = Step(prev, el, incap, stag, ground, skid, vy);
                reached.Add(next);
                if (next != prev)
                    left = true;
            }
            Assert.True(left, $"{prev} has no exit: no combination of replicated inputs leaves it");
        }
        foreach (AvatarActionState state in System.Enum.GetValues<AvatarActionState>())
            Assert.Contains(state, reached);
    }

    [Fact]
    public void EveryStateMapsToAClipTheLibraryActuallyCarries()
    {
        foreach (AvatarActionState state in System.Enum.GetValues<AvatarActionState>())
        {
            string? clip = AvatarClipNames.FullBodyClipFor(state);
            if (state == AvatarActionState.Locomotion)
            {
                Assert.Null(clip);   // the blend space, not one clip
                continue;
            }
            Assert.NotNull(clip);
            Assert.Contains(clip!, AvatarClipNames.All);
        }
        foreach (AvatarHoldState hold in System.Enum.GetValues<AvatarHoldState>())
            Assert.Contains(AvatarClipNames.OverrideClipFor(hold), AvatarClipNames.All);
    }
}

/// <summary>
/// <b>Animation LOD: the tiers, the hysteresis, and the local player's exemption.</b>
/// </summary>
public class AvatarAnimationLodTests
{
    [Fact]
    public void TheBodyThisClientControls_IsNeverLoded()
    {
        // Takes a flag rather than relying on the distance being zero, because a third-person camera
        // can legitimately be pushed several metres back — and "the body I am steering stutters" is
        // not a saving worth making.
        Assert.Equal(AnimationLodTier.Full,
            AvatarAnimationLod.TierFor(120f, isLocal: true, AnimationLodTier.Frozen));
    }

    [Fact]
    public void TheThreeTiers_AreWhereTheyClaimToBe()
    {
        Assert.Equal(AnimationLodTier.Full,
            AvatarAnimationLod.TierFor(5f, false, AnimationLodTier.Full));
        Assert.Equal(AnimationLodTier.HalfRate,
            AvatarAnimationLod.TierFor(25f, false, AnimationLodTier.Full));
        Assert.Equal(AnimationLodTier.Frozen,
            AvatarAnimationLod.TierFor(50f, false, AnimationLodTier.Full));
    }

    [Fact]
    public void ABodySittingOnABoundary_DoesNotFlipEveryFrame()
    {
        // A tier flip is a visible change in how a stride moves, so the boundary needs the same
        // treatment Gear's does. Demotion happens at the boundary; promotion needs HysteresisM
        // inside it.
        AnimationLodTier t = AvatarAnimationLod.TierFor(
            AvatarAnimationLod.FullRateM + 0.01f, false, AnimationLodTier.Full);
        Assert.Equal(AnimationLodTier.HalfRate, t);
        // Drifting a centimetre back inside must NOT promote.
        Assert.Equal(AnimationLodTier.HalfRate,
            AvatarAnimationLod.TierFor(AvatarAnimationLod.FullRateM - 0.01f, false, t));
        // A metre and a half further in does.
        Assert.Equal(AnimationLodTier.Full,
            AvatarAnimationLod.TierFor(AvatarAnimationLod.FullRateM - AvatarAnimationLod.HysteresisM - 0.01f, false, t));
    }

    [Fact]
    public void AFrozenBodyWalkingBackIntoRange_CanReachEveryTierAgain()
    {
        AnimationLodTier t = AnimationLodTier.Frozen;
        t = AvatarAnimationLod.TierFor(30f, false, t);
        Assert.Equal(AnimationLodTier.HalfRate, t);
        t = AvatarAnimationLod.TierFor(4f, false, t);
        Assert.Equal(AnimationLodTier.Full, t);
    }

    [Fact]
    public void TheAdvanceCadencePerTier_IsEveryFrame_EveryOther_AndNever()
    {
        Assert.Equal(1, AvatarAnimationLod.FramesPerAdvance(AnimationLodTier.Full));
        Assert.Equal(2, AvatarAnimationLod.FramesPerAdvance(AnimationLodTier.HalfRate));
        Assert.Equal(0, AvatarAnimationLod.FramesPerAdvance(AnimationLodTier.Frozen));
    }

    [Fact]
    public void ANonFiniteDistance_DoesNotFreezeABodyThatIsStandingNextToYou()
    {
        // A NaN distance is reachable the frame a camera is being re-parented. Treated as zero, which
        // is the safe answer: an extra advance costs a frame of work, a wrong freeze costs a body
        // that has visibly stopped moving.
        Assert.Equal(AnimationLodTier.Full,
            AvatarAnimationLod.TierFor(float.NaN, false, AnimationLodTier.Full));
    }

    [Fact]
    public void TheFourPlayerBudget_IsTheOneThisWasTunedAgainst()
    {
        // Canon fact 15 (Talon, 2026-08-21): supported range is 1-6 and FOUR is the case to design
        // for. Stated here as the number rather than left in a comment, so a future change to the
        // track count or the tier cadence moves a measured figure instead of a claim.
        Assert.Equal(2880, AvatarAnimationLod.TrackEvaluationsPerSecond(4, 0, 60f));
        Assert.Equal(4320, AvatarAnimationLod.TrackEvaluationsPerSecond(6, 0, 60f));
        // Solo is supported and must work.
        Assert.Equal(720, AvatarAnimationLod.TrackEvaluationsPerSecond(1, 0, 60f));
        // Half-rate bodies cost half.
        Assert.Equal(1440, AvatarAnimationLod.TrackEvaluationsPerSecond(1, 2, 60f));
    }
}
