namespace MpFoundation.Ui;

/// <summary>
/// Pure dismiss decision for <see cref="LoadingHintOverlay"/> (L11, Issue #114), pulled out for
/// the same reason <c>RunPhaseTracker</c>/<c>CyclePhase</c> are pulled out of their Node hosts:
/// directly testable without a scene tree.
///
/// The whole "never strand" acceptance criterion collapses to one rule: dismiss the instant a
/// POLL observes <c>CycleDriver.Synced == true</c>, with no edge/one-shot-signal dependency in
/// between. An edge-triggered subscriber ("fire once when Synced flips false-&gt;true") can miss
/// a transition that already happened before it subscribed — exactly the soft-lock this Story
/// calls out as the single most likely bug it could ship. A poll of the CURRENT value, run every
/// frame starting the instant the overlay exists, cannot strand: the first poll after the
/// overlay exists always sees the true current state, however early that state became true.
///
/// <see cref="LoadingHintOverlay"/> is the sole caller, from both <c>_Ready</c> (in case Synced
/// is already true on the very first frame the overlay exists — the exact race described above)
/// and every <c>_Process</c> tick after that (in case it becomes true later). No ready-up
/// handshake, no "am I ready yet" state on this side at all — the design's own explicit
/// simplification.
/// </summary>
public static class LoadingOverlayGate
{
    public static bool ShouldBeVisible(bool synced) => !synced;
}
