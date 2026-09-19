using System.Collections.Generic;
using MpFoundation.Game.Aim;

namespace MpFoundation.Net;

/// <summary>
/// The owning client's buffer of not-yet-acknowledged inputs, in sequence order. Each
/// entry also records the state the client predicted after applying that input, so a
/// server ack that matches the prediction can skip the replay entirely (the common case
/// on a clean connection). The aim-substrate prediction (WP-L3) rides alongside the movement
/// one, in its own fields — independent, mirroring CamcorderController's Task-4 precedent
/// (feat/tier0-camcorder): a lost aim-raise edge can diverge Stance/Steadiness even while
/// position prediction matches perfectly (or vice versa), and the two must be able to correct
/// without disturbing each other (see SandboxAvatar.Reconcile for why: replaying an
/// already-correct system on top of itself would double-apply it).
/// </summary>
public sealed class InputRing
{
    /// <summary>~2 s of inputs at 60 Hz. If the server falls further behind than this,
    /// the oldest entries are shed; the next reconciliation full-replays from
    /// authoritative state anyway, so nothing is lost but the skip-optimization.</summary>
    private const int Capacity = 128;

    public struct Entry
    {
        public Game.Sandbox.MoveIntent Intent;
        public float SpeedFactor;
        public MoveState Predicted;
        public bool HasPrediction;
        public AimStance PredictedAimStance;
        public float PredictedAimSteadyElapsedSec;
        public bool HasAimPrediction;
    }

    /// <summary>What was predicted for the acked sequence — movement and, independently,
    /// whether an aim-substrate prediction was ALSO recorded for it and what it was. The two
    /// halves are deliberately not coupled: a caller that only cares about movement never needs
    /// to know whether the aim half is populated.</summary>
    public readonly record struct AckSnapshot(
        MoveState State, AimStance AimStance, float AimSteadyElapsedSec, bool HasAimPrediction);

    private readonly List<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries => _entries;
    public int Count => _entries.Count;

    public void Add(in Game.Sandbox.MoveIntent intent, float speedFactor)
    {
        _entries.Add(new Entry { Intent = intent, SpeedFactor = speedFactor });
        if (_entries.Count > Capacity)
            _entries.RemoveAt(0);
    }

    /// <summary>Stores the movement state AND aim stance/steadiness predicted immediately after
    /// simulating <paramref name="seq"/> — always recorded together, since both are derived
    /// from stepping the same tick's intent (see SandboxAvatar.OwnerTick).</summary>
    public void RecordPrediction(uint seq, in MoveState predicted, AimStance aimStance, float aimSteadyElapsedSec)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Intent.Seq != seq)
                continue;
            Entry e = _entries[i];
            e.Predicted = predicted;
            e.HasPrediction = true;
            e.PredictedAimStance = aimStance;
            e.PredictedAimSteadyElapsedSec = aimSteadyElapsedSec;
            e.HasAimPrediction = true;
            _entries[i] = e;
            return;
        }
    }

    /// <summary>Index-addressed movement-prediction update for reconciliation replay (avoids
    /// re-searching by sequence for every replayed input). Leaves the aim fields on this entry
    /// untouched — see <see cref="SetPredictionAtAim"/>, the independent sibling a caller uses
    /// only when the aim half is ALSO being replayed this pass.</summary>
    public void SetPredictionAt(int index, in MoveState predicted)
    {
        Entry e = _entries[index];
        e.Predicted = predicted;
        e.HasPrediction = true;
        _entries[index] = e;
    }

    /// <summary>Index-addressed aim-substrate-prediction update for reconciliation replay — the
    /// independent sibling of <see cref="SetPredictionAt"/>. Deliberately a separate call (not
    /// combined) so a replay that only needed to correct ONE of the two systems never has to
    /// touch, and so never risks corrupting, the other's already-correct stored prediction.</summary>
    public void SetPredictionAtAim(int index, AimStance aimStance, float aimSteadyElapsedSec)
    {
        Entry e = _entries[index];
        e.PredictedAimStance = aimStance;
        e.PredictedAimSteadyElapsedSec = aimSteadyElapsedSec;
        e.HasAimPrediction = true;
        _entries[index] = e;
    }

    /// <summary>
    /// Drops every entry with sequence &lt;= <paramref name="ackSeq"/> and returns what was
    /// predicted for the acked sequence itself (null when that entry is already gone or its
    /// movement prediction was never recorded — the caller then does a full movement replay).
    /// The aim half's validity is reported independently via <see cref="AckSnapshot.HasAimPrediction"/>
    /// rather than gating the whole return on it, matching movement's existing null-means-replay
    /// contract without also forcing an unrelated movement replay just because the aim half
    /// happened to be unpopulated.
    /// </summary>
    public AckSnapshot? DropThrough(uint ackSeq)
    {
        AckSnapshot? result = null;
        int drop = 0;
        while (drop < _entries.Count && _entries[drop].Intent.Seq <= ackSeq)
        {
            if (_entries[drop].Intent.Seq == ackSeq && _entries[drop].HasPrediction)
            {
                Entry e = _entries[drop];
                result = new AckSnapshot(e.Predicted, e.PredictedAimStance, e.PredictedAimSteadyElapsedSec, e.HasAimPrediction);
            }
            drop++;
        }
        if (drop > 0)
            _entries.RemoveRange(0, drop);
        return result;
    }

    /// <summary>The newest <paramref name="max"/> entries (oldest first) — the redundancy
    /// window each input packet carries.</summary>
    public List<NetCodec.InputEntry> Window(int max)
    {
        int start = _entries.Count > max ? _entries.Count - max : 0;
        var window = new List<NetCodec.InputEntry>(_entries.Count - start);
        for (int i = start; i < _entries.Count; i++)
            window.Add(new NetCodec.InputEntry(_entries[i].Intent, _entries[i].SpeedFactor));
        return window;
    }

    public void Clear() => _entries.Clear();
}
