using System;
using System.Collections.Generic;
using Sail.Game.Failure;

namespace SailNet.Tests;

/// <summary>
/// The atomic transition (BEHAVIOR-BIBLE §10.2, MECHANICS-BIBLE §2, STATE-CASCADE-TABLE.md).
///
/// <para><b>What this file exists to prevent.</b> The repo's named recurring failure — "a dead
/// player's ragdoll still under player control, with the nameplate floating over a headless body"
/// — was not one bug. It was one state change and five systems that were never told, all at once.
/// The cascade table's whole thesis is that you cannot enumerate the failures (unbounded) but you
/// can enumerate the systems (finite), so if the system list is complete the failures cannot hide.
/// This file is that thesis made executable: it asserts every row of a transition fires, and that
/// a transition that fails partway leaves nothing inconsistent.</para>
///
/// <para><b>And it proves its own detector works.</b> A standing rule here is that a diagnostic
/// reporting "absent" is worthless until it has been shown able to report "present" — a test that
/// can only ever pass certifies nothing. <see cref="NegativeControl_TheDetectorFailsWhenARowIsDropped"/>
/// feeds a deliberately-incomplete sink through the exact same verification path the real tests
/// use, once per row, and requires the verifier to catch each one. If someone deletes a row from
/// <c>IncapacityCascade.Apply</c>, that is the test that goes red.</para>
/// </summary>
public class IncapacityCascadeTests
{
    private const int Peer = 7;

    /// <summary>Records which rows were actually applied, in order. The honest implementation of
    /// "did every system get told" — the observation comes from the SINK side, not from what
    /// <c>Apply</c> intended to do, because a report of its own intentions would prove nothing.</summary>
    private sealed class RecordingSink : IIncapacitySink
    {
        public readonly List<CascadeRow> Rows = new();
        public IncapacityState? Committed;
        public bool? GatesDenied;
        public bool? CameraDetached;
        public int Scatters;

        public void SetInteractionGates(int peerId, bool denied)
        {
            Rows.Add(CascadeRow.InteractionGates);
            GatesDenied = denied;
        }

        public void SetCameraDetached(int peerId, bool detached)
        {
            Rows.Add(CascadeRow.Camera);
            CameraDetached = detached;
        }

        public void SetLegibility(int peerId, IncapacityState state, IncapacityCause cause)
            => Rows.Add(CascadeRow.Legibility);

        public void CommitReplicatedState(int peerId, IncapacityState state, bool impulseRagdoll)
        {
            Rows.Add(CascadeRow.ReplicatedState);
            Committed = state;
        }

        public void ScatterCarried(int peerId)
        {
            Rows.Add(CascadeRow.CarriedProps);
            Scatters++;
        }
    }

    /// <summary>A sink that silently skips exactly one row — the shape of the defect this whole
    /// document exists to catch, injected on purpose.</summary>
    private sealed class PartialSink : IIncapacitySink
    {
        private readonly CascadeRow _skip;
        public readonly List<CascadeRow> Rows = new();
        public PartialSink(CascadeRow skip) => _skip = skip;
        private void Note(CascadeRow row) { if (row != _skip) Rows.Add(row); }
        public void SetInteractionGates(int p, bool d) => Note(CascadeRow.InteractionGates);
        public void SetCameraDetached(int p, bool d) => Note(CascadeRow.Camera);
        public void SetLegibility(int p, IncapacityState s, IncapacityCause c) => Note(CascadeRow.Legibility);
        public void CommitReplicatedState(int p, IncapacityState s, bool i) => Note(CascadeRow.ReplicatedState);
        public void ScatterCarried(int p) => Note(CascadeRow.CarriedProps);
    }

    /// <summary>A sink that throws on one row and records everything it managed first.</summary>
    private sealed class ThrowingSink : IIncapacitySink
    {
        private readonly CascadeRow _throwOn;
        public readonly List<CascadeRow> Rows = new();
        public IncapacityState? Committed;
        public int Scatters;
        public ThrowingSink(CascadeRow throwOn) => _throwOn = throwOn;

        private void Step(CascadeRow row)
        {
            if (row == _throwOn)
                throw new InvalidOperationException($"injected failure at {row}");
            Rows.Add(row);
        }

        public void SetInteractionGates(int p, bool d) => Step(CascadeRow.InteractionGates);
        public void SetCameraDetached(int p, bool d) => Step(CascadeRow.Camera);
        public void SetLegibility(int p, IncapacityState s, IncapacityCause c) => Step(CascadeRow.Legibility);

        public void CommitReplicatedState(int p, IncapacityState s, bool i)
        {
            Step(CascadeRow.ReplicatedState);
            Committed = s;
        }

        public void ScatterCarried(int p)
        {
            Step(CascadeRow.CarriedProps);
            Scatters++;
        }
    }

    // --- Completeness ------------------------------------------------------------------------------

    [Theory]
    [InlineData(IncapacityState.KnockedOut)]
    [InlineData(IncapacityState.Frozen)]
    public void EnteringAState_FiresEveryCascadeRow(IncapacityState to)
    {
        var sink = new RecordingSink();
        CascadeOutcome outcome = IncapacityCascade.Apply(
            sink, Peer, IncapacityState.Active, to, IncapacityCause.LongOneContact, false);

        Assert.True(outcome.Committed);
        Assert.True(outcome.Clean);
        Assert.Empty(IncapacityCascade.MissingRows(IncapacityState.Active, to, sink.Rows));
        Assert.Equal(to, sink.Committed);
        Assert.True(sink.GatesDenied);
        Assert.True(sink.CameraDetached);
        Assert.Equal(1, sink.Scatters);
    }

    [Theory]
    [InlineData(IncapacityState.KnockedOut)]
    [InlineData(IncapacityState.Frozen)]
    public void Recovering_FiresEveryRowExceptTheScatter(IncapacityState from)
    {
        var sink = new RecordingSink();
        CascadeOutcome outcome = IncapacityCascade.Apply(
            sink, Peer, from, IncapacityState.Active, IncapacityCause.None, false);

        Assert.True(outcome.Committed);
        Assert.Empty(IncapacityCascade.MissingRows(from, IncapacityState.Active, sink.Rows));
        Assert.Equal(IncapacityState.Active, sink.Committed);
        Assert.False(sink.GatesDenied);
        Assert.False(sink.CameraDetached);
        // Getting up does not un-drop what you dropped.
        Assert.Equal(0, sink.Scatters);
    }

    /// <summary>The expected-row list is derived from the transition, never hand-written in a
    /// test — a hand-list would drift from <c>Apply</c> and then the test would be asserting its
    /// own stale copy of the answer.</summary>
    [Fact]
    public void TheExpectedRowSet_IsDerivedFromTheTransition()
    {
        Assert.Contains(CascadeRow.CarriedProps,
            IncapacityCascade.ExpectedRows(IncapacityState.Active, IncapacityState.Frozen));
        Assert.DoesNotContain(CascadeRow.CarriedProps,
            IncapacityCascade.ExpectedRows(IncapacityState.Frozen, IncapacityState.Active));
    }

    // --- THE NEGATIVE CONTROL -----------------------------------------------------------------------

    /// <summary>
    /// <b>Proof that the tests above can fail.</b> For every row the cascade is supposed to touch,
    /// drop exactly that row and require the verifier to name it. Without this, every assertion in
    /// this file would be indistinguishable from a verifier that always returns "complete" — which
    /// is the standing rule in this repo about diagnostics that have never been shown able to
    /// report "present".
    /// </summary>
    [Fact]
    public void NegativeControl_TheDetectorFailsWhenARowIsDropped()
    {
        const IncapacityState from = IncapacityState.Active;
        const IncapacityState to = IncapacityState.Frozen;
        IReadOnlyList<CascadeRow> expected = IncapacityCascade.ExpectedRows(from, to);
        Assert.NotEmpty(expected);

        foreach (CascadeRow dropped in expected)
        {
            var sink = new PartialSink(dropped);
            IncapacityCascade.Apply(sink, Peer, from, to, IncapacityCause.NightWaterChill, false);

            IReadOnlyList<CascadeRow> missing = IncapacityCascade.MissingRows(from, to, sink.Rows);
            Assert.True(missing.Count == 1 && missing[0] == dropped,
                $"the cascade verifier failed to notice that row {dropped} never fired — "
                + "it cannot distinguish a complete transition from an incomplete one, so every "
                + "other assertion in this file is worthless");
        }
    }

    /// <summary>The same control from the other side: a complete run must produce an EMPTY missing
    /// list. A verifier that reported a false positive would be just as useless as one that
    /// reported nothing.</summary>
    [Fact]
    public void NegativeControl_ACompleteRunReportsNothingMissing()
    {
        var sink = new RecordingSink();
        IncapacityCascade.Apply(sink, Peer, IncapacityState.Active, IncapacityState.Frozen,
            IncapacityCause.NightWaterChill, false);
        Assert.Empty(IncapacityCascade.MissingRows(
            IncapacityState.Active, IncapacityState.Frozen, sink.Rows));
    }

    // --- Atomicity: a transition that fails partway ---------------------------------------------------

    /// <summary>
    /// Every revocable row runs before the commit, so a failure in any of them means the
    /// transition <b>simply did not happen</b>: nothing was committed, nothing replicated, and
    /// crucially nothing irreversible was done. That is a clean abort, not a partial update.
    /// </summary>
    [Theory]
    [InlineData(CascadeRow.InteractionGates)]
    [InlineData(CascadeRow.Camera)]
    [InlineData(CascadeRow.Legibility)]
    [InlineData(CascadeRow.ReplicatedState)]
    public void AFailureBeforeOrAtTheCommit_LeavesNothingCommittedAndNothingScattered(CascadeRow failAt)
    {
        var sink = new ThrowingSink(failAt);
        CascadeOutcome outcome = IncapacityCascade.Apply(
            sink, Peer, IncapacityState.Active, IncapacityState.KnockedOut,
            IncapacityCause.LongOneContact, false);

        Assert.False(outcome.Committed);
        Assert.Equal(failAt, outcome.FailedAt);
        Assert.Null(sink.Committed);
        // The irreversible row is the one that must never run on an aborted transition: a player
        // whose whole loadout scattered and who then did NOT go down has lost their items to a
        // state change that never happened.
        Assert.Equal(0, sink.Scatters);
    }

    /// <summary>
    /// The commit is second-to-last, so the one irreversible row is the only thing that can fail
    /// after the transition is real. When it does, <b>every state system still agrees</b> — the
    /// player is genuinely down everywhere — and the only consequence is that they kept some of
    /// what they were carrying. Both Held and Loose are legal prop modes, so even that leaves
    /// nothing in an inconsistent state.
    ///
    /// <para>Reverse the order and the same failure scatters a player's entire loadout and then
    /// leaves them standing up holding nothing, with no record of why.</para>
    /// </summary>
    [Fact]
    public void AFailureInTheIrreversibleTail_StillCommitsAConsistentState()
    {
        var sink = new ThrowingSink(CascadeRow.CarriedProps);
        CascadeOutcome outcome = IncapacityCascade.Apply(
            sink, Peer, IncapacityState.Active, IncapacityState.Frozen,
            IncapacityCause.NightWaterChill, false);

        Assert.True(outcome.Committed);
        Assert.False(outcome.Clean);
        Assert.Equal(CascadeRow.CarriedProps, outcome.FailedAt);
        Assert.Equal(IncapacityState.Frozen, sink.Committed);
        Assert.Contains(CascadeRow.InteractionGates, sink.Rows);
        Assert.Contains(CascadeRow.Camera, sink.Rows);
        Assert.Contains(CascadeRow.Legibility, sink.Rows);
    }

    /// <summary>The ordering the whole argument rests on, pinned so a future refactor cannot
    /// quietly move the commit and turn the mild failure mode into the bad one.</summary>
    [Fact]
    public void TheCommitIsSecondToLast_AndTheIrreversibleRowIsLast()
    {
        var sink = new RecordingSink();
        IncapacityCascade.Apply(sink, Peer, IncapacityState.Active, IncapacityState.KnockedOut,
            IncapacityCause.BreakerRampage, false);

        Assert.Equal(CascadeRow.CarriedProps, sink.Rows[^1]);
        Assert.Equal(CascadeRow.ReplicatedState, sink.Rows[^2]);
    }

    /// <summary>Rows run in the declared order every time. Order is the mechanism here, not a
    /// detail: it is what makes "revocable first, irreversible last" true.</summary>
    [Fact]
    public void RowsRunInTheDeclaredOrder()
    {
        var sink = new RecordingSink();
        IncapacityCascade.Apply(sink, Peer, IncapacityState.Active, IncapacityState.Frozen,
            IncapacityCause.StarerGaze, false);
        Assert.Equal(IncapacityCascade.Order, sink.Rows);
    }

    // --- The impulse ragdoll's narrower cascade -------------------------------------------------------

    /// <summary>An impulse ragdoll denies control but is not incapacitation, so the camera stays
    /// in the player's hands. A comic 1.2-second stumble that yanked the camera away would read
    /// as a bug, and beta plan §10 is explicit that this is not a failure state.</summary>
    [Fact]
    public void AnImpulseRagdoll_DeniesControlWithoutDetachingTheCamera()
    {
        var sink = new RecordingSink();
        IncapacityCascade.Apply(sink, Peer, IncapacityState.Active, IncapacityState.Active,
            IncapacityCause.None, impulseRagdoll: true);

        Assert.True(sink.GatesDenied);
        Assert.False(sink.CameraDetached);
        Assert.Equal(0, sink.Scatters);
    }

    [Fact]
    public void ANullSink_IsARefusalRatherThanASilentNoOp()
    {
        Assert.Throws<ArgumentNullException>(() => IncapacityCascade.Apply(
            null!, Peer, IncapacityState.Active, IncapacityState.Frozen,
            IncapacityCause.StarerGaze, false));
    }
}
