using Godot;

namespace Sail.Game.Water;

/// <summary>
/// The sputter-out sequence (spec §6), as pure logic — no node, no networking, no scene, so
/// every rule below is directly xUnit-testable and the self-test can drive it thousands of
/// times in a millisecond.
///
/// <b>No child ever dies in this game.</b> Water is a soft boundary with a real cost, never a
/// killer: at <c>chill = 1.0</c> the player goes under, cuts to black, and wakes prone at the
/// nearest shoreline, coughing, having lost the tape. That is the fourth instance of a
/// consequence shape the game already ships (counselor catch, creature catch, creature KO), not
/// a carve-out invented for the lake.
///
/// <b>Two invariants this class exists to hold.</b>
/// <list type="number">
/// <item>Control returns at <b>exactly one moment</b> — the end of <see cref="SputterPhase.Recovering"/>.
/// There is no window in which the player is ragdolled, cut to black, or mid-teleport and still
/// steering. MECHANICS-BIBLE §2 is explicit about this and this repo has shipped that exact bug
/// once already.</item>
/// <item>The cost applies <b>exactly once per episode</b>, idempotently (MECHANICS-BIBLE §4).
/// <see cref="Begin"/> is the only door in and it is guarded by the phase, so re-entering the
/// sputter-out state cannot double-ruin the tape or re-set Soaked.</item>
/// </list>
/// </summary>
public sealed class SputterSequence
{
    /// <summary>What <see cref="Advance"/> just did. One step per call at most: the phases are
    /// long relative to a tick, and collapsing two boundaries into one call would hide a
    /// transition from the event stream.</summary>
    public enum Step : byte
    {
        /// <summary>Nothing happened this step (idle, or still inside a phase).</summary>
        None = 0,

        /// <summary>The go-under finished: this is the hard cut, and the instant the player is
        /// relocated to the shore. Control stays locked.</summary>
        HardCut = 1,

        /// <summary>The recovery finished. This is the single moment control returns.</summary>
        Recovered = 2,
    }

    /// <summary>Which phase the player is in. <see cref="SputterPhase.None"/> is normal play.</summary>
    public SputterPhase Phase { get; private set; } = SputterPhase.None;

    /// <summary>Seconds elapsed inside the current phase. Zero while idle.</summary>
    public float PhaseElapsedSec { get; private set; }

    /// <summary>True for the whole of an episode and false otherwise. The guard that makes
    /// <see cref="Begin"/> idempotent.</summary>
    public bool CostApplied { get; private set; }

    /// <summary>How many times the cost has been applied over this sequence's lifetime. Pure
    /// instrumentation — the exactly-once proof reads this, the same shape an earlier
    /// system's lit-count already used for its own exactly-once assertion.</summary>
    public int CostAppliedCount { get; private set; }

    /// <summary>Control is locked for every phase except <see cref="SputterPhase.None"/>.</summary>
    public bool ControlLocked => Phase != SputterPhase.None;

    /// <summary>True while the body should be sinking (the swallow).</summary>
    public bool Sinking => Phase == SputterPhase.GoingUnder;

    /// <summary>
    /// Start an episode. Returns true only if this call actually started one — a second call
    /// while an episode is already running is a no-op and returns false, which is the whole
    /// idempotency guarantee. Callers apply the cost <b>only</b> when this returns true.
    /// </summary>
    public bool Begin()
    {
        if (Phase != SputterPhase.None)
            return false;
        Phase = SputterPhase.GoingUnder;
        PhaseElapsedSec = 0f;
        CostApplied = true;
        CostAppliedCount++;
        return true;
    }

    /// <summary>
    /// Advance the sequence by <paramref name="dt"/> seconds and report the boundary crossed, if
    /// any. Non-finite or non-positive <paramref name="dt"/> is a no-op rather than a corruption.
    /// </summary>
    public Step Advance(float dt)
    {
        if (Phase == SputterPhase.None || !float.IsFinite(dt) || dt <= 0f)
            return Step.None;

        PhaseElapsedSec += dt;

        if (Phase == SputterPhase.GoingUnder)
        {
            if (PhaseElapsedSec < WaterGeometry.GoUnderSec)
                return Step.None;
            Phase = SputterPhase.Recovering;
            PhaseElapsedSec = 0f;
            return Step.HardCut;
        }

        if (PhaseElapsedSec < WaterGeometry.RecoverSec)
            return Step.None;

        // The single moment control returns. Re-arming CostApplied here — and only here — is
        // what makes a SECOND sputter-out possible while making a double-apply of the FIRST
        // impossible: a new episode has to re-earn chill 0 -> 1 from scratch, because the
        // service zeroes chill at this same instant.
        Phase = SputterPhase.None;
        PhaseElapsedSec = 0f;
        CostApplied = false;
        return Step.Recovered;
    }

    /// <summary>
    /// Hard reset — a disconnect, a run reset, a commanded teleport out of the lake. Returns the
    /// sequence to idle without firing any boundary. Deliberately <b>does not</b> clear
    /// <see cref="CostAppliedCount"/>: that counter is a lifetime tally for the exactly-once
    /// proof, not episode state.
    /// </summary>
    public void Reset()
    {
        Phase = SputterPhase.None;
        PhaseElapsedSec = 0f;
        CostApplied = false;
    }

    /// <summary>How far the body has sunk this episode, in metres. Zero outside
    /// <see cref="SputterPhase.GoingUnder"/>.</summary>
    public float SinkDepthM => Phase == SputterPhase.GoingUnder
        ? Mathf.Min(PhaseElapsedSec, WaterGeometry.GoUnderSec) * WaterGeometry.SinkRate
        : 0f;
}
