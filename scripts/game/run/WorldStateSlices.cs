using System.Collections.Generic;

namespace Sail.Game.Run;

// The store SEAM only (core-spine spec §5.3, CORE-PROG-A1 scope item 7): these interfaces
// compile and are registered-against by PlaythroughDriver's playthrough-boundary entry guard
// (spec §5.4). The WorldStateStore implementation, the slice migrations of the eight ad-hoc
// RunReset subscribers, and the dead-reset wiring are ALL CORE-PROG-A2's — nothing in this
// packet implements a store, and QuotaLedger (the one new slice) stays a direct RunReset
// subscriber until A2 migrates it (the documented migration-window overlap, spec §5.4:
// idempotent slices make the double pass harmless).

/// <summary>One resettable unit of world state (spec §5.3). Every stateful manager becomes
/// exactly one of these under A2's migration; new systems are born as one.</summary>
public interface IWorldStateSlice
{
    /// <summary>Stable id, for logs, ordering audits, and VER's completeness check.</summary>
    string SliceId { get; }

    /// <summary>REQUIRED idempotent: running it twice == running it once. Restores the slice
    /// to its pristine, session-start state. Replicated-slice rule (the <c>_seq</c> trap,
    /// learned on an earlier replicated manager): a slice owning sequence-guarded replicated state must NOT
    /// rewind its sequence counters here — clients would reject post-reset state as stale.</summary>
    void ResetForNewPlaythrough();
}

/// <summary>run-scoped-forgets, additionally cut off at each dawn — the precedent set by a
/// since-removed night consumable's dawn cutoff. Fanned at <c>PhaseCrossed(NightToDawn)</c>.</summary>
public interface INightScopedSlice : IWorldStateSlice
{
    void OnNightEnded(int round);
}

/// <summary>map-scoped-remembers (canon fact 8): persists across every round boundary within
/// the playthrough; reset only at the playthrough boundary.</summary>
public interface IMapScopedSlice : IWorldStateSlice
{
    /// <summary>RESERVED across-nights write path, fanned at <c>PhaseCrossed(NightToDawn)</c>
    /// after the night-scoped cutoffs. No-op bodies are legal and expected today; when disk
    /// persistence is ever built it lands here without re-plumbing a single manager. No disk
    /// write exists or is added by the CORE program (spec §5.1).</summary>
    void CaptureNightSnapshot(int round);
}

/// <summary>The store surface <see cref="PlaythroughDriver"/> runs the playthrough boundary
/// against (spec §5.4: an ENTRY guard — a playthrough may not begin unless the store has
/// affirmatively reset every registered slice in that commit). A2's <c>WorldStateStore</c>
/// (Gameplay-child node, constructed after RunDriver and before PlaythroughDriver) implements
/// this; until it lands, PlaythroughDriver receives null and fails loudly at every
/// playthrough start (see its RunPlaythroughBoundary).</summary>
public interface IWorldStateStore
{
    /// <summary>From each manager's Setup; registration order = construction order = fan-out
    /// order.</summary>
    void Register(IWorldStateSlice slice);

    /// <summary>VER's completeness probe against spec §5.2's mutator table.</summary>
    IReadOnlyList<string> RegisteredSliceIds { get; }

    /// <summary>Fan-out in registration order; idempotent because the slices are.</summary>
    void ResetForNewPlaythrough();
}
