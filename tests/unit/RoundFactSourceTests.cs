using System.Collections.Generic;
using Godot;
using MpFoundation.Game;
using MpFoundation.Game.Round;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The seam where five lanes' answers get reconciled</b>, plus the two facts about the round's
/// reset edge that cannot be seen from inside the loop.
///
/// <para><c>HideSeekDriver</c> itself is a <c>Node</c> and needs a scene tree; everything about it
/// that is a RULE rather than plumbing was deliberately pushed out into
/// <see cref="RoundFacts.Combine"/> so it could be held here. What is left in the driver — the RPC,
/// the teleport calls, the event raising — is what the scene smoke proves.</para>
/// </summary>
public class RoundFactSourceTests
{
    /// <summary>A source that answers exactly what it was constructed with. Stands in for BTN-1's
    /// buttons, CARRY-1's hands, REACH-1's audit and TASK-1's towers at once.</summary>
    private sealed class Fake : IRoundFactSource
    {
        public bool HostPressedStart { get; init; }
        public bool HiderHeldRackProp { get; init; }
        public bool HiderPressedConfirm { get; init; }
        public bool HiderHoldsTarget { get; init; }
        public bool? TargetRetrievable { get; init; }
        public bool TargetInDropOff { get; init; }
        public int TowersCompleted { get; init; }
        public bool AnyPressedEnd { get; init; }

        public int AfterStepCalls { get; private set; }
        public void AfterStep() => AfterStepCalls++;
    }

    private static readonly int[] Roster = { 11, 22 };

    [Fact]
    public void TheNullSource_AnswersNothing_SoTheLoopRunsStandalone()
    {
        HideSeekInput input = RoundFacts.Combine(
            new IRoundFactSource[] { NullRoundFactSource.Instance }, Roster);

        Assert.False(input.HostPressedStart);
        Assert.False(input.HiderHeldRackProp);
        Assert.False(input.TargetInDropOff);
        Assert.Equal(0, input.TowersCompleted);
        Assert.Null(input.TargetRetrievable);
        // And the unmeasured retrievability still reads as retrievable.
        Assert.True(input.TargetRetrievableOrDefault);
    }

    [Fact]
    public void NoSourcesAtAll_IsTheSameAsTheNullSource()
    {
        HideSeekInput input = RoundFacts.Combine(new List<IRoundFactSource>(), Roster);
        Assert.False(input.HostPressedStart);
        Assert.Equal(2, input.Humans.Length);
    }

    /// <summary><b>A source that does not know a fact can never veto one that does.</b> This is
    /// what makes the dev script safe to leave in the build: it can add a press, never swallow
    /// one.</summary>
    [Fact]
    public void BoolsAreOrd_SoASilentSourceNeverVetoesALoudOne()
    {
        HideSeekInput input = RoundFacts.Combine(new IRoundFactSource[]
        {
            new Fake(),                                  // knows nothing
            new Fake { HostPressedStart = true },        // the button
            new Fake { HiderHeldRackProp = true },       // the rack
            new Fake { TargetInDropOff = true },         // the bin
        }, Roster);

        Assert.True(input.HostPressedStart);
        Assert.True(input.HiderHeldRackProp);
        Assert.True(input.TargetInDropOff);
    }

    /// <summary><b>Retrievability is the first NON-NULL answer, not an OR.</b> OR-ing would be
    /// wrong in both directions — a source answering false is a refusal, and one answering null is
    /// not a yes.</summary>
    [Fact]
    public void Retrievability_TakesTheFirstSourceThatActuallyMeasuredIt()
    {
        HideSeekInput refused = RoundFacts.Combine(new IRoundFactSource[]
        {
            new Fake(),                                   // unmeasured
            new Fake { TargetRetrievable = false },       // REACH-1 says no
            new Fake { TargetRetrievable = true },        // a later source disagrees
        }, Roster);
        Assert.False(refused.TargetRetrievableOrDefault);

        HideSeekInput allowed = RoundFacts.Combine(new IRoundFactSource[]
        {
            new Fake { TargetRetrievable = true },
            new Fake { TargetRetrievable = false },
        }, Roster);
        Assert.True(allowed.TargetRetrievableOrDefault);
    }

    /// <summary><b>The tower count is the maximum</b>: absolute, so summing double-counts and
    /// last-wins lets a source that does not know zero a real count.</summary>
    [Fact]
    public void Towers_TakeTheMaximum_NotTheSumAndNotTheLast()
    {
        HideSeekInput input = RoundFacts.Combine(new IRoundFactSource[]
        {
            new Fake { TowersCompleted = 3 },
            new Fake { TowersCompleted = 0 },
            new Fake { TowersCompleted = 2 },
        }, Roster);

        Assert.Equal(3, input.TowersCompleted);
    }

    [Fact]
    public void TheRosterIsCarriedInOrder_BecauseJoinOrderDecidesWhoHides()
    {
        HideSeekInput input = RoundFacts.Combine(
            new IRoundFactSource[] { new Fake() }, new[] { 77, 5, 900 });

        Assert.Equal(3, input.Humans.Length);
        Assert.Equal(77, input.Humans[0]);
        Assert.Equal(5, input.Humans[1]);
        Assert.Equal(900, input.Humans[2]);
    }

    [Fact]
    public void ANullSourceInTheList_IsSkippedRatherThanThrowing()
    {
        HideSeekInput input = RoundFacts.Combine(
            new IRoundFactSource?[] { null, new Fake { AnyPressedEnd = true } }!, Roster);
        Assert.True(input.AnyPressedEnd);
    }

    // --- the reset edge's two halves ---------------------------------------------------------

    /// <summary>
    /// <b>A resume ticket must not survive the round's reset edge.</b> BASE-1's handoff named this
    /// as the one thing ROUND-1 must not lose, and it is the half of the reset that is invisible
    /// when it breaks: props failing to go home is obvious on the next frame; a peer coming back
    /// for up to 60 s at a pre-reset position holding pre-reset props inside a freshly restored
    /// room is not.
    ///
    /// <para>The fan-out itself is one line in <c>Gameplay.OnRoundResetRequested</c>; what is
    /// testable without an engine is that the slice does what that line is counting on.</para>
    /// </summary>
    [Fact]
    public void TheReconnectRegistry_TearsUpEveryTicketAtTheRoundBoundary()
    {
        var registry = new ReconnectRegistry();
        registry.Capture(steamId: 76561198000000001UL, new Vector3(35f, 1f, 0f),
            new[] { 1, 2 }, colorIndex: 3, nowSec: 0.0);
        Assert.Equal(1, registry.Count);

        // Well inside the 60 s grace: without the reset this ticket is live.
        Assert.True(registry.TryConsume(76561198000000001UL, 5.0, out _));

        registry.Capture(76561198000000001UL, new Vector3(35f, 1f, 0f), new[] { 1 }, 3, 0.0);
        ((Sail.Game.Run.IWorldStateSlice)registry).ResetForNewPlaythrough();

        Assert.Equal(0, registry.Count);
        Assert.False(registry.TryConsume(76561198000000001UL, 5.0, out ReconnectRegistry.ResumeData data),
            "a resume ticket survived the round reset — that is the exploit BASE-1 flagged");
        Assert.Empty(data.HeldPropIds);
    }

    /// <summary>Idempotent, because the driver may raise the edge more than once across a long
    /// session and both slices promise it.</summary>
    [Fact]
    public void TheReconnectRegistrySlice_IsIdempotent()
    {
        var registry = new ReconnectRegistry();
        registry.Capture(1UL, Vector3.Zero, System.Array.Empty<int>(), 0, 0.0);
        registry.ResetForNewPlaythrough();
        registry.ResetForNewPlaythrough();
        Assert.Equal(0, registry.Count);
    }

    // --- the channel ladder -------------------------------------------------------------------

    /// <summary>
    /// <b>Every reliable RPC channel is distinct.</b> A duplicate here compiles clean, turns
    /// nothing red, and the only symptom is two unrelated streams head-of-line-blocking each other
    /// in a playtest — which has already happened once in this codebase's history, when two
    /// systems on two branches both claimed 11 and a textual merge produced two differently-named
    /// constants with the same value.
    ///
    /// <para>ROUND-1 is the first lane to add a channel since the fork and the first of several
    /// this program will add (VOICE-1 and DOOR-1 both reach for one), so the ladder gets a test
    /// rather than a comment asking people to check.</para>
    /// </summary>
    [Fact]
    public void TheChannelLadder_HasNoDuplicates()
    {
        (string Name, int Value)[] ladder =
        {
            (nameof(NetProfile.VoiceChannel), NetProfile.VoiceChannel),
            (nameof(NetProfile.MoveChannel), NetProfile.MoveChannel),
            (nameof(NetProfile.PropChannel), NetProfile.PropChannel),
            (nameof(NetProfile.CycleChannel), NetProfile.CycleChannel),
            (nameof(NetProfile.RunChannel), NetProfile.RunChannel),
            (nameof(NetProfile.WaterChannel), NetProfile.WaterChannel),
            (nameof(NetProfile.IncapacityChannel), NetProfile.IncapacityChannel),
            (nameof(NetProfile.SightChannel), NetProfile.SightChannel),
            (nameof(NetProfile.FlashlightChannel), NetProfile.FlashlightChannel),
            (nameof(NetProfile.HonkChannel), NetProfile.HonkChannel),
            (nameof(NetProfile.RoundChannel), NetProfile.RoundChannel),
        };

        var seen = new Dictionary<int, string>();
        foreach ((string name, int value) in ladder)
        {
            Assert.False(seen.TryGetValue(value, out string? other),
                $"{name} and {other} both claim channel {value}");
            seen[value] = name;
        }

        // The packet reserved 21 for the round. Pinned so a later compaction of the gaps cannot
        // quietly move it out from under the design doc that names it.
        Assert.Equal(21, NetProfile.RoundChannel);
    }
}
