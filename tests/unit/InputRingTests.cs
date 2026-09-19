using Godot;
using MpFoundation.Game.Aim;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// The owner-side unacked-input buffer on the reconciliation critical path. The subtle
/// contracts pinned here: DropThrough drops everything at-or-below the ack but returns a
/// snapshot only when the exact acked entry still exists AND its movement prediction was
/// recorded (null means the caller full-replays); the aim half's validity rides the
/// snapshot independently (HasAimPrediction) instead of gating the return; and the two
/// index-addressed replay setters must never touch each other's half — replaying an
/// already-correct system on top of itself would double-apply it.
/// </summary>
public class InputRingTests
{
    private static MoveIntent Intent(uint seq) => new() { Seq = seq };

    private static MoveState State(float x) => new() { Position = new Vector3(x, 0, 0) };

    private static InputRing RingWithSeqs(params uint[] seqs)
    {
        var ring = new InputRing();
        foreach (uint s in seqs)
            ring.Add(Intent(s), 1f);
        return ring;
    }

    [Fact]
    public void DropThrough_DropsEverythingAtOrBelowAck_KeepsTheRest()
    {
        InputRing ring = RingWithSeqs(1, 2, 3, 4, 5);
        ring.DropThrough(3);
        Assert.Equal(2, ring.Count);
        Assert.Equal(4u, ring.Entries[0].Intent.Seq);
        Assert.Equal(5u, ring.Entries[1].Intent.Seq);
    }

    [Fact]
    public void DropThrough_AckedEntryWithPrediction_ReturnsThatPrediction()
    {
        InputRing ring = RingWithSeqs(1, 2, 3);
        ring.RecordPrediction(2, State(7f), AimStance.Raised, 0.5f);

        InputRing.AckSnapshot? snap = ring.DropThrough(2);

        Assert.NotNull(snap);
        Assert.Equal(7f, snap!.Value.State.Position.X);
        Assert.Equal(AimStance.Raised, snap.Value.AimStance);
        Assert.Equal(0.5f, snap.Value.AimSteadyElapsedSec);
        Assert.True(snap.Value.HasAimPrediction);
        Assert.Equal(1, ring.Count); // seq 3 survives
    }

    [Fact]
    public void DropThrough_AckedEntryWithoutPrediction_ReturnsNull_MeaningFullReplay()
    {
        InputRing ring = RingWithSeqs(1, 2, 3);
        // Prediction recorded for a DIFFERENT seq: the acked one itself is bare.
        ring.RecordPrediction(1, State(1f), AimStance.Lowered, 0f);
        Assert.Null(ring.DropThrough(2));
        Assert.Equal(1, ring.Count);
    }

    [Fact]
    public void DropThrough_AckOlderThanEverything_DropsNothing_ReturnsNull()
    {
        InputRing ring = RingWithSeqs(10, 11);
        Assert.Null(ring.DropThrough(5));
        Assert.Equal(2, ring.Count);
    }

    [Fact]
    public void DropThrough_AckBeyondEverything_EmptiesRing_ReturnsNull()
    {
        InputRing ring = RingWithSeqs(1, 2, 3);
        ring.RecordPrediction(3, State(3f), AimStance.Lowered, 0f);
        // Ack 9 drops all three, but no entry has Seq == 9, so there is no snapshot.
        Assert.Null(ring.DropThrough(9));
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void RecordPrediction_UnknownSeq_IsANoOp()
    {
        InputRing ring = RingWithSeqs(1);
        ring.RecordPrediction(99, State(9f), AimStance.Raised, 1f);
        Assert.False(ring.Entries[0].HasPrediction);
        Assert.False(ring.Entries[0].HasAimPrediction);
    }

    [Fact]
    public void SetPredictionAt_MovementOnly_LeavesAimHalfUntouched()
    {
        InputRing ring = RingWithSeqs(1);
        ring.RecordPrediction(1, State(1f), AimStance.Raised, 0.8f);

        ring.SetPredictionAt(0, State(2f)); // movement replay corrects position only

        InputRing.Entry e = ring.Entries[0];
        Assert.Equal(2f, e.Predicted.Position.X);
        Assert.Equal(AimStance.Raised, e.PredictedAimStance);   // aim half untouched
        Assert.Equal(0.8f, e.PredictedAimSteadyElapsedSec);
        Assert.True(e.HasAimPrediction);
    }

    [Fact]
    public void SetPredictionAtAim_AimOnly_LeavesMovementHalfUntouched()
    {
        InputRing ring = RingWithSeqs(1);
        ring.RecordPrediction(1, State(1f), AimStance.Lowered, 0f);

        ring.SetPredictionAtAim(0, AimStance.Raised, 0.4f);

        InputRing.Entry e = ring.Entries[0];
        Assert.Equal(1f, e.Predicted.Position.X); // movement half untouched
        Assert.True(e.HasPrediction);
        Assert.Equal(AimStance.Raised, e.PredictedAimStance);
        Assert.Equal(0.4f, e.PredictedAimSteadyElapsedSec);
    }

    [Fact]
    public void SetPredictionAtAim_OnBareEntry_MarksOnlyTheAimHalfPresent()
    {
        InputRing ring = RingWithSeqs(1);
        ring.SetPredictionAtAim(0, AimStance.Raised, 0.1f);
        Assert.False(ring.Entries[0].HasPrediction);
        Assert.True(ring.Entries[0].HasAimPrediction);
    }

    [Fact]
    public void Capacity_OldestEntriesAreShed()
    {
        var ring = new InputRing();
        for (uint s = 1; s <= 130; s++)
            ring.Add(Intent(s), 1f);
        Assert.Equal(128, ring.Count);
        Assert.Equal(3u, ring.Entries[0].Intent.Seq);   // 1 and 2 were shed
        Assert.Equal(130u, ring.Entries[^1].Intent.Seq);
    }

    [Fact]
    public void Window_ReturnsNewestNOldestFirst()
    {
        InputRing ring = RingWithSeqs(1, 2, 3, 4, 5);
        var window = ring.Window(3);
        Assert.Equal(3, window.Count);
        Assert.Equal(3u, window[0].Intent.Seq);
        Assert.Equal(5u, window[2].Intent.Seq);
    }

    [Fact]
    public void Window_LargerThanRing_ReturnsEverything()
    {
        InputRing ring = RingWithSeqs(1, 2);
        Assert.Equal(2, ring.Window(8).Count);
    }

    [Fact]
    public void Clear_EmptiesTheRing()
    {
        InputRing ring = RingWithSeqs(1, 2, 3);
        ring.Clear();
        Assert.Equal(0, ring.Count);
    }
}
