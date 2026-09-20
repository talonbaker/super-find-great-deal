using System.Collections.Immutable;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>What the server observed this tick</b>, and the only way anything reaches
/// <see cref="HideSeekLoop.Step"/>. Every field is a FACT about the world, never a decision the
/// loop should be making — the contract the loop this replaces was written under, and what keeps
/// the whole machine steppable without an engine.
///
/// <para><b>Who fills each field is another lane's job</b> (packet ROUND-1: "this packet exposes
/// the FACTS they fold in and the EVENTS they consume"). <c>HideSeekDriver</c> assembles one of
/// these per sim tick out of every registered <c>IRoundFactSource</c>; a lane that has not landed
/// yet simply contributes nothing, which is why this loop runs standalone.</para>
///
/// <para><b>Presses are EDGES, levels are LEVELS, and mixing them up is the classic round-loop
/// bug.</b> <see cref="HostPressedStart"/>, <see cref="HiderPressedConfirm"/> and
/// <see cref="AnyPressedEnd"/> are true for the one tick the press happened and are latched and
/// cleared by the driver before they arrive here. Everything else is what IS true right now and
/// may be true for many ticks running.</para>
/// </summary>
public readonly record struct HideSeekInput
{
    /// <summary>
    /// Every human peer present this tick, <b>in join order</b>. Not a count: the order is what
    /// assigns the first round's roles (program §1 item 5 — the host hides first), and the
    /// identities are what let the loop notice that a role holder has gone.
    ///
    /// <para>"Human" means A PLAYER PEER, as opposed to the dedicated server process, which
    /// holds no avatar and is not in the round. A <c>--bot</c> client IS one: the gate this feeds
    /// counts players, and a suite's two bots are two players by every definition the round cares
    /// about.</para>
    ///
    /// <para>May be <c>default</c> (an uninitialised <c>ImmutableArray</c>) on a tick nobody
    /// filled it; every read goes through <see cref="Humans"/>, which normalises that to
    /// empty.</para>
    /// </summary>
    public ImmutableArray<int> HumanPeerIds { get; init; }

    /// <summary>Normalised roster — never a default array. Use this, never the field.</summary>
    public ImmutableArray<int> Humans =>
        HumanPeerIds.IsDefault ? ImmutableArray<int>.Empty : HumanPeerIds;

    /// <summary>The host pressed Start this tick (BTN-1). Read only in
    /// <see cref="HideSeekPhase.Holding"/>; a press arriving in any other phase changes nothing,
    /// because no other branch consults it at all.</summary>
    public bool HostPressedStart { get; init; }

    /// <summary>The hider is holding an object taken off the rack (BTN-1's <c>ObjectRack</c>).
    /// A level, not an edge — it is consulted at the instant Start is pressed.</summary>
    public bool HiderHeldRackProp { get; init; }

    /// <summary>The hider pressed Confirm this tick (BTN-1).</summary>
    public bool HiderPressedConfirm { get; init; }

    /// <summary>The hider still has the target in their hands right now (CARRY-1). Hiding it
    /// means putting it down, so Confirm is refused while this is true.</summary>
    public bool HiderHoldsTarget { get; init; }

    /// <summary>
    /// <b>Is the target grab-reachable where it now sits</b> (program §5b layer 3; REACH-1 owns
    /// the geometry, this loop only folds the answer).
    ///
    /// <para><b>Nullable, and that is the whole point.</b> The packet's contract is "default
    /// true; REACH-1 supplies the real value" — and a plain <c>bool</c> on a
    /// <c>readonly record struct</c> defaults to FALSE, so every Confirm in the entire game would
    /// be refused with <see cref="HideSeekRefusal.NobodyCouldReachThat"/> until REACH-1 landed,
    /// on a loop that looked perfectly correct in review. <c>null</c> means "nobody has measured
    /// this"; <see cref="TargetRetrievableOrDefault"/> resolves it to true. The same
    /// unknown-is-not-an-answer shape the loop this replaces used for its witness flag.</para>
    /// </summary>
    public bool? TargetRetrievable { get; init; }

    /// <summary>The resolved answer: unknown reads as retrievable. See
    /// <see cref="TargetRetrievable"/> for why the raw field is nullable.</summary>
    public bool TargetRetrievableOrDefault => TargetRetrievable ?? true;

    /// <summary>The target prop is sitting in the drop-off bin (BTN-1's <c>DropOffBin</c>). This
    /// is the find, and it is a LEVEL rather than an edge deliberately: a bin that reported a
    /// one-tick pulse would lose the find to a dropped tick, and the round's whole payoff hangs
    /// off it.</summary>
    public bool TargetInDropOff { get; init; }

    /// <summary>Sorts the hider has completed this round, <b>absolute, never an increment</b>
    /// (TASK-1). Absolute is what makes the message wire-ready and a re-sent tick harmless — the
    /// same reasoning the loop this replaces used for its carried-coin counts.</summary>
    public int SortsCompleted { get; init; }

    /// <summary>Somebody pressed End this tick (BTN-1). Read only in
    /// <see cref="HideSeekPhase.Together"/>.</summary>
    public bool AnyPressedEnd { get; init; }
}
