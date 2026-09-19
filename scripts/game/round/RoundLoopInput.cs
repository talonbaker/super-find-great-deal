using System.Collections.Immutable;

namespace MpFoundation.Game.Round;

/// <summary>
/// One shatter this tick — a fact, not a decision. <see cref="Witnessed"/> is nullable because
/// WITNESS-1 (the instrument that would set it) has not landed yet; every shatter reported in
/// phase A carries <c>null</c>, and <see cref="RoundLoop"/> only ever promotes a <c>true</c> into
/// the per-rider witnessed count — it never treats an unknown witness state as either yes or no.
/// <see cref="Cause"/> is carried for wire-readiness and future H1-H5 splitting (WITNESS-1 / phase
/// B); the phase-A loop does not branch on it.
/// </summary>
public readonly record struct RoundShatterEvent(int RiderId, string Cause, bool? Witnessed);

/// <summary>
/// <b>What the engine observed this tick</b>, and the only way anything reaches
/// <see cref="RoundLoop.Step"/> — <c>ShiftLoopInput</c>'s exact contract (every field a fact about
/// the world, never a decision the loop should be making, which is what keeps this steppable on a
/// server later).
/// </summary>
public readonly record struct RoundLoopInput
{
    /// <summary>At least one human is present this tick. Arms Gathering's timer on the first tick
    /// this is true; irrelevant once armed or once a phase other than Gathering is active.</summary>
    public bool HumansPresent { get; init; }

    /// <summary>The host pressed start this tick. An edge, not a level — latched and cleared by
    /// the driver before it reaches here, exactly as <c>ShiftLoopInput.ReadyPulled</c>. Read only
    /// while <see cref="RoundLoopState.Phase"/> is <see cref="RoundPhase.Gathering"/>; a press
    /// arriving any other phase changes nothing (R4, idempotent).</summary>
    public bool HostPressedStart { get; init; }

    /// <summary>
    /// <b>Absolute carried-coin counts, per rider, as of this tick — never increments.</b> The
    /// loop does not compute a spill or a pickup; it is told what each rider is carrying right
    /// now and stores that value verbatim (clamped >= 0). This is what makes the message
    /// wire-ready: a late joiner or a resent tick can apply it twice with no drift (MECHANICS §4),
    /// the same reason SN-2's crew ledger carries absolute ints rather than deltas.
    ///
    /// <para>Riders not present in this dictionary on a given tick keep their last-known value —
    /// this is a sparse update, not a full roster snapshot every tick.</para>
    /// </summary>
    public ImmutableDictionary<int, int>? CarriedCoinsPerRider { get; init; }

    /// <summary>Every shatter that happened this tick. Empty most ticks.</summary>
    public ImmutableArray<RoundShatterEvent> Shatters { get; init; }
}
