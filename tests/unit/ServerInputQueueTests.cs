using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Sandbox;
using MpFoundation.Net;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// The server-side input jitter buffer. Covers the ordering / warm-up / catch-up /
/// starvation / gap-grace / far-future-resync / eviction invariants documented on
/// <see cref="ServerInputQueue"/>, plus the uint sequence-boundary behavior.
/// </summary>
public class ServerInputQueueTests
{
    // WarmupDepth=2, TargetMaxDepth=4, MaxCatchUpSteps=2, GapGraceTicks=6,
    // MaxSeqLead=512, MaxStored=128 (mirrors the class constants under test).

    private static NetCodec.InputEntry E(uint seq, float x = 1f, float z = 0f, float sf = 1f) =>
        new(new MoveIntent { MoveDir = new Vector3(x, 0, z), Seq = seq }, sf);

    private static void EnqueueOne(ServerInputQueue q, uint seq, float x = 1f, float z = 0f) =>
        q.Enqueue(new List<NetCodec.InputEntry> { E(seq, x, z) });

    private static void EnqueueRange(ServerInputQueue q, uint from, uint toInclusive)
    {
        var list = new List<NetCodec.InputEntry>();
        for (uint s = from; s <= toInclusive; s++) list.Add(E(s));
        q.Enqueue(list);
    }

    // --- Warm-up / ordering -----------------------------------------------------------

    [Fact]
    public void Warmup_HoldsUntilDepthReached_ThenConsumesInOrder()
    {
        var q = new ServerInputQueue();
        EnqueueOne(q, 1);
        Assert.Empty(q.TakeForTick());            // pending=1 < WarmupDepth: hold
        Assert.Equal(0u, q.LastConsumedSeq);

        EnqueueOne(q, 2);                          // pending=2 -> warmed
        var t1 = q.TakeForTick();
        Assert.Single(t1);
        Assert.Equal(1u, t1[0].Intent.Seq);
        Assert.Equal(1u, q.LastConsumedSeq);

        var t2 = q.TakeForTick();
        Assert.Single(t2);
        Assert.Equal(2u, t2[0].Intent.Seq);
        Assert.Equal(2u, q.LastConsumedSeq);
    }

    [Fact]
    public void Nominal_OneInputPerTick_InSequenceOrder()
    {
        var q = new ServerInputQueue();
        EnqueueRange(q, 1, 4);                     // pending=4 == TargetMaxDepth (not above)
        for (uint expect = 1; expect <= 4; expect++)
        {
            var t = q.TakeForTick();
            Assert.Single(t);
            Assert.Equal(expect, t[0].Intent.Seq);
        }
    }

    [Fact]
    public void CatchUp_DoubleStepsWhenBacklogAboveTarget_ButNeverMoreThanMax()
    {
        var q = new ServerInputQueue();
        EnqueueRange(q, 1, 6);                     // pending=6 > TargetMaxDepth(4)
        var t = q.TakeForTick();
        Assert.Equal(2, t.Count);                 // capped at MaxCatchUpSteps
        Assert.Equal(1u, t[0].Intent.Seq);
        Assert.Equal(2u, t[1].Intent.Seq);
        Assert.Equal(2u, q.LastConsumedSeq);
    }

    // --- Starvation: real inputs are never guessed away -------------------------------

    [Fact]
    public void Starvation_HoldsWithoutConsuming_ThenCatchesLateInput()
    {
        var q = new ServerInputQueue();
        EnqueueRange(q, 1, 2);
        q.TakeForTick(); q.TakeForTick();          // drain 1,2
        Assert.Equal(2u, q.LastConsumedSeq);

        var hold = q.TakeForTick();                // pending empty: pure starvation
        Assert.Empty(hold);
        Assert.Equal(2u, q.LastConsumedSeq);       // ack unchanged, seq not skipped

        EnqueueOne(q, 3);                          // the late packet finally arrives
        var t = q.TakeForTick();
        Assert.Single(t);
        Assert.Equal(3u, t[0].Intent.Seq);         // consumed, not lost
    }

    // --- Stale duplicates (redundancy window) ----------------------------------------

    [Fact]
    public void StaleDuplicates_BelowAck_AreIgnored()
    {
        var q = new ServerInputQueue();
        EnqueueRange(q, 1, 3);
        q.TakeForTick(); q.TakeForTick(); q.TakeForTick();
        Assert.Equal(3u, q.LastConsumedSeq);

        EnqueueRange(q, 1, 3);                      // redundant re-sends of already-acked seqs
        Assert.Equal(0, q.PendingCount);           // all dropped, nothing re-queued
    }

    // --- Gap grace: a lost sequence is bridged after a bounded wait -------------------

    [Fact]
    public void GapGrace_BridgesHoleAfterGraceTicks_RepeatingLastIntentWithEdgesCleared()
    {
        var q = new ServerInputQueue();
        // seq 2 is permanently lost; 1,3,4 present. Use a distinctive MoveDir on seq 1
        // so we can confirm the bridge repeats it.
        q.Enqueue(new List<NetCodec.InputEntry> { E(1, x: 0.5f, z: -0.5f) });
        EnqueueOne(q, 3);
        EnqueueOne(q, 4);

        var t1 = q.TakeForTick();                  // consume seq 1
        Assert.Single(t1);
        Assert.Equal(1u, q.LastConsumedSeq);

        // seq 2 missing but 3,4 present: hold for exactly GapGraceTicks ticks.
        for (int i = 0; i < 6; i++)
        {
            Assert.Empty(q.TakeForTick());
            Assert.Equal(1u, q.LastConsumedSeq);
        }

        // Next tick bridges the hole: ack jumps to just before the next real input (3),
        // repeating seq 1's direction with jump/interact/throw edges cleared.
        var bridge = q.TakeForTick();
        Assert.Single(bridge);
        Assert.Equal(2u, q.LastConsumedSeq);       // FirstKey(3) - 1
        Assert.Equal(new Vector3(0.5f, 0, -0.5f), bridge[0].Intent.MoveDir);
        Assert.False(bridge[0].Intent.Jump);
        Assert.False(bridge[0].Intent.Interact);
        Assert.False(bridge[0].Intent.Throw);

        // Recovery proceeds at stream rate: seq 3 consumed next.
        var t3 = q.TakeForTick();
        Assert.Single(t3);
        Assert.Equal(3u, t3[0].Intent.Seq);
    }

    // --- Eviction must never advance the ack ------------------------------------------

    [Fact]
    public void Eviction_AboveMaxStored_DropsOldest_ButNeverAdvancesAck()
    {
        var q = new ServerInputQueue();
        // seq 1 is the awaited "next" and is deliberately never sent, so the queue cannot
        // drain; flood far past MaxStored(128) to force eviction of the oldest stored.
        EnqueueRange(q, 2, 200);                    // 199 entries, all within MaxSeqLead

        Assert.Equal(128, q.PendingCount);          // capped
        Assert.Equal(0u, q.LastConsumedSeq);        // eviction did NOT push the ack forward

        // Draining still starts from the real ack (0): next awaited is seq 1 (a hole),
        // so the first tick holds rather than skipping the ack ahead.
        Assert.Empty(q.TakeForTick());
        Assert.Equal(0u, q.LastConsumedSeq);
    }

    // --- Far-future resync (the runaway-stream guard, away from the uint boundary) ----

    [Fact]
    public void FarFutureSeq_TriggersResync_ThenStreamDrains()
    {
        var q = new ServerInputQueue();
        EnqueueOne(q, 1000);                        // > MaxSeqLead(512) ahead of ack(0)
        Assert.Equal(999u, q.LastConsumedSeq);      // re-anchored just behind the stream
        Assert.Equal(1, q.PendingCount);

        EnqueueOne(q, 1001);
        EnqueueOne(q, 1002);                        // in-order continuation accumulates
        Assert.Equal(3, q.PendingCount);            // NOT re-classified as far-future

        var a = q.TakeForTick();
        Assert.Single(a);
        Assert.Equal(1000u, a[0].Intent.Seq);
        Assert.Equal(1002u, DrainTo(q, 1002));      // reaches the live stream, no wedge
    }

    // --- uint sequence boundary -------------------------------------------------------

    [Fact]
    public void SeqCrossingUintBoundary_InOrderStream_DrainsInsteadOfWedging()
    {
        // The invariant the overflow-safe guard must satisfy: an in-order input stream that
        // crosses the uint.MaxValue -> 0 sequence wrap must accumulate to warm-up depth and be
        // consumed like any other stream — it must NOT wedge.
        //
        // The old test `seq > _lastConsumed + MaxSeqLead` overflowed once _lastConsumed entered
        // the top MaxSeqLead of the range, misclassifying every in-order input as far-future and
        // freezing the avatar for the session. The fix computes the forward distance as a signed
        // delta (`(int)(seq - _lastConsumed)`), which is correct across the wrap.
        //
        // Park _lastConsumed inside that former overflow zone (within a few of uint.MaxValue) via
        // two forward far-future resyncs — each hop's forward distance stays below 2^31 so it
        // classifies as far-future, not stale — then drive the wrap.
        var q = new ServerInputQueue();
        EnqueueOne(q, (uint)int.MaxValue);              // hop 1: forward 2^31-1 -> ack = 2^31-2
        EnqueueOne(q, uint.MaxValue - 4);               // hop 2: forward <2^31   -> ack = max-5
        Assert.Equal(uint.MaxValue - 5, q.LastConsumedSeq);

        uint start = q.LastConsumedSeq + 1;             // == uint.MaxValue - 4
        int peak = 0;
        uint s = start;
        for (int i = 0; i < 8; i++, s++)                // in-order across ...MaxValue, 0, 1, 2, 3
        {
            EnqueueOne(q, s);
            if (q.PendingCount > peak)
                peak = q.PendingCount;
        }
        Assert.True(peak >= ServerInputQueue.WarmupDepth, "in-order inputs across the uint wrap must accumulate, not wedge");

        var t = q.TakeForTick();
        Assert.NotEmpty(t);                             // warm-up met -> simulation proceeds
        Assert.Equal(start, t[0].Intent.Seq);
    }

    /// <summary>Ticks the queue until it has consumed <paramref name="target"/>, returning
    /// the last consumed seq. Bounded so a wedge fails the test instead of hanging.</summary>
    private static uint DrainTo(ServerInputQueue q, uint target)
    {
        for (int guard = 0; guard < 10_000 && q.LastConsumedSeq != target; guard++)
            q.TakeForTick();
        return q.LastConsumedSeq;
    }
}
