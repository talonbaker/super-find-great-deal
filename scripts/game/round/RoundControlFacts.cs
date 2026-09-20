using System;
using System.Collections.Generic;

namespace MpFoundation.Game.Round;

/// <summary>
/// <b>The three buttons, the rack and the bin, as one <see cref="IRoundFactSource"/></b> — and
/// engine-free, so the press → fact mapping is a <c>dotnet test</c> rather than a claim.
///
/// <para><b>Why one source rather than three.</b> ROUND-1's combiner ORs every bool across
/// sources, so three sources would work; but the three facts are produced by one server-side
/// owner (<c>RoundControls</c>), the target's identity is shared between the rack and the bin,
/// and every one of them has to be cleared on the same reset edge. Splitting them would be three
/// registrations that must agree about which prop is the target, which is the one thing in this
/// lane that must not have two answers.</para>
///
/// <para><b>Presses are LATCHED and levels are PROBED.</b> A press is an edge that must survive
/// from whenever the RPC landed until the next sim tick reads it, so it is a bool that
/// <see cref="AfterStep"/> clears. A level ("the hider is holding a rack object", "the target is
/// in the bin") is asked for at the instant the loop folds its facts, through a delegate, so this
/// class never caches a world fact and there is no ordering dependency between this node's tick
/// and the driver's. That difference is the whole reason the interface has
/// <see cref="AfterStep"/> at all.</para>
/// </summary>
public sealed class RoundControlFacts : IRoundFactSource
{
    /// <summary>One press that has been accepted for forwarding and not yet answered.</summary>
    /// <param name="Kind">Which button.</param>
    /// <param name="PeerId">Who pressed it — the presser is the only peer that hears the click.</param>
    /// <param name="PhaseAtPress">The phase the round was in when the press was taken. The
    /// answer is "did the phase move", so the before-value has to be captured before the step,
    /// not re-derived after it.</param>
    public readonly record struct PendingPress(RoundButtonKind Kind, int PeerId,
        HideSeekPhase PhaseAtPress);

    /// <summary>The hider is holding one of the rack's three objects. Null probe answers false —
    /// a world with no rack (every CI world) is silent rather than special-cased.</summary>
    public Func<bool>? RackHoldProbe { get; set; }

    /// <summary>The hider still has THE TARGET in their hands (refuses Confirm).</summary>
    public Func<bool>? TargetHoldProbe { get; set; }

    /// <summary>The target has been delivered to the drop-off bin. A LEVEL, not a pulse — see
    /// <c>DropOffBin</c> for what "delivered" latches on.</summary>
    public Func<bool>? BinProbe { get; set; }

    /// <summary>
    /// Called from <see cref="AfterStep"/>, with every press the loop has just consumed, BEFORE
    /// the latches are cleared. This is where <c>RoundControls</c> reads the round's answer and
    /// broadcasts it.
    ///
    /// <para><b>It is a callback rather than a return value</b> because <see cref="AfterStep"/>
    /// is the interface's method and the driver calls it; a fact source cannot ask to be called
    /// back at any other moment, and this is the one instant at which "what did the loop do with
    /// my press" is answerable.</para>
    /// </summary>
    public Action<IReadOnlyList<PendingPress>>? Adjudicate { get; set; }

    /// <summary>Called at the end of every <see cref="AfterStep"/>, press or no press. The
    /// round's own tick, borrowed: <c>RoundControls</c> keeps the target's identity in step with
    /// the phase here rather than running a <c>_PhysicsProcess</c> of its own, so there is one
    /// clock and no ordering between two nodes to get wrong.</summary>
    public Action? Stepped { get; set; }

    private readonly List<PendingPress> _pending = new();
    private bool _start;
    private bool _confirm;
    private bool _end;

    /// <summary>How many presses this source has latched, ever. The suite reads it to tell "the
    /// press never arrived" from "the press arrived and was refused", which look identical from
    /// the outside and have completely different fixes.</summary>
    public int PressesLatched { get; private set; }

    /// <summary>
    /// Latch one press. Called on the SERVER only, from the adjudicated RPC, after
    /// <see cref="RoundButtonRules.Gate"/> has said the round should be asked.
    ///
    /// <para><b>No debounce, deliberately.</b> The packet is explicit: the loop's one-shot fact
    /// reading IS the idempotence. Two Start presses inside one sim tick set the same bool twice
    /// and produce one transition; a debounce would be a second rule about the same thing, and a
    /// debounce mistaken for a confirmation is literally the first of the three playtest failures
    /// this packet exists to remove.</para>
    ///
    /// <para>Two presses of the SAME button in one tick are deduplicated for the ANSWER only —
    /// the fact is set either way, but one press gets one result message, so the presser does not
    /// see the cap bounce twice for a single transition.</para>
    /// </summary>
    public void Press(RoundButtonKind kind, int peerId, HideSeekPhase phaseAtPress)
    {
        PressesLatched++;
        switch (kind)
        {
            case RoundButtonKind.Start: _start = true; break;
            case RoundButtonKind.Confirm: _confirm = true; break;
            case RoundButtonKind.End: _end = true; break;
        }
        foreach (PendingPress p in _pending)
            if (p.Kind == kind && p.PeerId == peerId)
                return;
        _pending.Add(new PendingPress(kind, peerId, phaseAtPress));
    }

    /// <summary>Everything the round owns about the target is dropped: the bin's latch and the
    /// target's identity live in <c>RoundControls</c>, and the presses die here. Called on the
    /// reset edge (Tally → Holding), where a press latched a tick before the edge would otherwise
    /// start the next round for a player who never touched the button.</summary>
    public void ClearForNewRound()
    {
        _start = false;
        _confirm = false;
        _end = false;
        _pending.Clear();
    }

    // --- IRoundFactSource -----------------------------------------------------------------

    /// <inheritdoc/>
    public bool HostPressedStart => _start;

    /// <inheritdoc/>
    public bool HiderPressedConfirm => _confirm;

    /// <inheritdoc/>
    public bool AnyPressedEnd => _end;

    /// <inheritdoc/>
    public bool HiderHeldRackProp => RackHoldProbe?.Invoke() ?? false;

    /// <inheritdoc/>
    public bool HiderHoldsTarget => TargetHoldProbe?.Invoke() ?? false;

    /// <inheritdoc/>
    public bool TargetInDropOff => BinProbe?.Invoke() ?? false;

    /// <inheritdoc/>
    /// <remarks>REACH-1 owns this one and registers first; answering anything but null here would
    /// win the first-non-null race and silently replace a physics measurement with a guess.</remarks>
    public bool? TargetRetrievable => null;

    /// <inheritdoc/>
    /// <remarks>TASK-1's.</remarks>
    public int TowersCompleted => 0;

    /// <inheritdoc/>
    /// <remarks><b>Answers are delivered BEFORE anything is cleared.</b> The reset edge clears
    /// this source (see <see cref="ClearForNewRound"/>, called from <see cref="Stepped"/>), and a
    /// clear that ran first would swallow the answer to a press made on the same tick — a press
    /// with no result on the presser is this packet's stated defect, and the one tick a round
    /// boundary happens on is not an exemption.</remarks>
    /// <inheritdoc/>
    public void AfterStep()
    {
        if (_pending.Count > 0)
        {
            Adjudicate?.Invoke(_pending);
            _pending.Clear();
        }
        _start = false;
        _confirm = false;
        _end = false;
        Stepped?.Invoke();
    }
}
