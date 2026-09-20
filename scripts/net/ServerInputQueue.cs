using System.Collections.Generic;
using MpFoundation.Game.Sandbox;

namespace MpFoundation.Net;

/// <summary>
/// The server's per-avatar input jitter buffer. Inputs arrive over an unreliable channel
/// with jitter, loss, duplication (redundancy windows), and reordering; the simulation
/// needs exactly one input per fixed tick, in order. This class absorbs that mismatch:
///
///  - Warm-up: consumption starts only once <see cref="WarmupDepth"/> inputs are queued,
///    so one buffered tick of slack rides between arrival and use (the "client runs
///    ahead" property, achieved by buffering on the server rather than by clock-syncing
///    the client — see the PR notes for why).
///  - Starvation: if the next input has not arrived, the avatar HOLDS (state unchanged,
///    no sequence consumed). Real inputs are never guessed away; late packets are
///    caught up instead.
///  - Catch-up: after a stall the buffer drains at up to <see cref="MaxCatchUpSteps"/>
///    sim steps per tick — bounded, so a client can never bank inputs and speed-run.
///  - Permanent gaps: if a sequence is missing but newer inputs keep arriving past the
///    redundancy window (all datagrams that carried it are provably lost), the whole
///    hole is bridged after a short grace: the last real intent is repeated once
///    (edge flags cleared) and the ack jumps to just before the next real input, so
///    recovery always proceeds at stream rate no matter how wide the loss burst was.
///  - Runaway streams: a stream more than <see cref="MaxSeqLead"/> ahead of the ack
///    (sustained outage, machine asleep) triggers a resync to the live stream instead
///    of being rejected forever.
///
/// Everything entering the buffer has already been sanitized by <see cref="NetCodec"/>.
/// </summary>
public sealed class ServerInputQueue
{
    public const int WarmupDepth = 2;

    /// <summary>Buffered inputs above this trigger catch-up (double-stepping).</summary>
    public const int TargetMaxDepth = 4;
    public const int MaxCatchUpSteps = 2;

    /// <summary>Ticks to wait for a missing sequence (with newer ones present) before
    /// declaring it lost and skipping it. ~100 ms — beyond any plausible reorder.</summary>
    private const int GapGraceTicks = 6;

    /// <summary>Sanitation bounds: reject far-future sequences, cap stored entries.</summary>
    private const uint MaxSeqLead = 512;
    private const int MaxStored = 128;

    private readonly SortedDictionary<uint, NetCodec.InputEntry> _pending = new();
    private uint _lastConsumed;
    private bool _started;
    private int _gapTicks;
    private MoveIntent _lastIntent = MoveIntent.None;
    private float _lastSpeedFactor = 1f;

    public int PendingCount => _pending.Count;
    public uint LastConsumedSeq => _lastConsumed;

    public void Enqueue(IReadOnlyList<NetCodec.InputEntry> entries)
    {
        foreach (NetCodec.InputEntry e in entries)
        {
            uint seq = e.Intent.Seq;
            // Overflow-safe forward distance: compute (seq - _lastConsumed) in unsigned wrap
            // arithmetic, then read it as a SIGNED delta (the technique VoiceSpeaker uses). This
            // is correct at every point in the uint range, including near uint.MaxValue — unlike
            // the direct comparisons `seq <= _lastConsumed` / `seq > _lastConsumed + MaxSeqLead`,
            // whose ADDITION overflows once _lastConsumed nears the top of the range and
            // misclassifies ordinary in-order inputs as far-future, permanently wedging the queue
            // (found by the ServerInputQueue fuzz test).
            int delta = unchecked((int)(seq - _lastConsumed));
            if (delta <= 0)
                continue; // stale duplicate (or already consumed)
            if (delta > (int)MaxSeqLead)
            {
                // The live stream ran further ahead than any plausible jitter (sustained
                // loss, a long hitch, a machine asleep). Rejecting from here on would
                // freeze this avatar for the rest of the session — instead resync: abandon
                // the unreachable past and re-anchor the ack just behind the stream. The
                // skipped inputs are simply never simulated (server authority holds; the
                // owner reconciles from the jumped ack like any other correction). A
                // malicious far-future seq only invalidates the sender's own real inputs.
                _pending.Clear();
                _lastConsumed = seq - 1;
                _gapTicks = 0;
            }
            _pending[seq] = e;
        }
        // Bounded memory: shed oldest above the cap. This drops the entry only — it
        // must NOT advance _lastConsumed, which is the ack of what was actually
        // simulated (LastProcessedSeq). Forcing it forward here would decouple the ack
        // from real simulation and snap the owner's reconciliation. TakeForTick's
        // existing gap-grace logic discovers the resulting hole and degrades over it
        // at its own paced rate, same as ordinary packet loss.
        while (_pending.Count > MaxStored)
            _pending.Remove(FirstKey());
    }

    /// <summary>
    /// Called once per server tick. Returns the inputs to simulate this tick, in order:
    /// empty (hold — warming up or starving), one (nominal), or up to
    /// <see cref="MaxCatchUpSteps"/> (draining a backlog).
    /// </summary>
    public List<NetCodec.InputEntry> TakeForTick()
    {
        var take = new List<NetCodec.InputEntry>(MaxCatchUpSteps);
        if (!_started)
        {
            if (_pending.Count < WarmupDepth)
                return take;
            _started = true;
        }

        int steps = _pending.Count > TargetMaxDepth ? MaxCatchUpSteps : 1;
        for (int i = 0; i < steps; i++)
        {
            uint next = _lastConsumed + 1;
            if (_pending.TryGetValue(next, out NetCodec.InputEntry entry))
            {
                _pending.Remove(next);
                _lastConsumed = next;
                _gapTicks = 0;
                _lastIntent = entry.Intent;
                _lastSpeedFactor = entry.SpeedFactor;
                take.Add(entry);
                continue;
            }

            if (_pending.Count == 0)
                break; // pure starvation: hold, keep every real input for catch-up

            // Newer inputs exist but `next` is missing. Wait out plausible reordering,
            // then declare the WHOLE hole lost and bridge it in one step: repeat the
            // last real intent once (edges cleared) and land the ack just before the
            // next input that actually arrived. Skipping one sequence per grace period
            // instead can never keep up with a live input stream — under sustained loss
            // the ack falls ever further behind until every fresh input is rejected as
            // far-future and the avatar freezes for good.
            if (++_gapTicks <= GapGraceTicks)
                break;
            _gapTicks = 0;
            _lastConsumed = FirstKey() - 1;
            var repeat = new MoveIntent
            {
                MoveDir = _lastIntent.MoveDir,
                Seq = _lastConsumed,
            };
            take.Add(new NetCodec.InputEntry(repeat, _lastSpeedFactor));
        }
        return take;
    }

    private uint FirstKey()
    {
        foreach (uint key in _pending.Keys)
            return key;
        return 0;
    }
}
