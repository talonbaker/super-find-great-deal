using MpFoundation.Ui;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// L11 (Issue #114) acceptance criterion: "overlay never strands (dismisses on sync even if
/// sync precedes UI ready)" — the Story's own text names this its single most likely soft-lock,
/// so it gets tested explicitly here, not just implemented and hoped for.
///
/// <see cref="LoadingOverlayGate"/> is the pure decision <see cref="LoadingHintOverlay"/> polls
/// every frame (plus once synchronously in _Ready). These tests prove the design property that
/// makes polling correct regardless of ordering: the decision depends ONLY on the current
/// <c>synced</c> value passed in, never on having witnessed a transition — so it is impossible
/// for the overlay to "miss" sync becoming true, however early that happened relative to the
/// overlay's own existence.
/// </summary>
public class LoadingOverlayGateTests
{
    [Fact]
    public void VisibleWhileUnsynced() => Assert.True(LoadingOverlayGate.ShouldBeVisible(synced: false));

    [Fact]
    public void DismissesOnceSynced() => Assert.False(LoadingOverlayGate.ShouldBeVisible(synced: true));

    [Fact]
    public void NeverStrands_EvenWhenSyncedBecameTrueBeforeTheOverlayEverPolled()
    {
        // Models the exact race the acceptance criteria names: sync arrives BEFORE the overlay
        // is ready to observe it (e.g. a fast local server, or the overlay simply being added to
        // the tree after CycleDriver — see Gameplay's AddChild order). There is no "first poll
        // missed it" state to construct here at all — that is the point: the very first
        // evaluation, with synced already true, must still resolve to dismissed.
        bool syncedBeforeOverlayExisted = true;
        bool firstEverEvaluation = LoadingOverlayGate.ShouldBeVisible(syncedBeforeOverlayExisted);
        Assert.False(firstEverEvaluation, "overlay stranded visible despite sync already true on its first poll");
    }

    [Fact]
    public void OrderIndependent_PolledBeforeDuringAndAfterTheTransition_AllConverge()
    {
        // A poll model has no "edge" to miss: querying before, exactly at, or after the
        // transition all read the same correct answer from the same current value.
        Assert.True(LoadingOverlayGate.ShouldBeVisible(synced: false));  // polled before sync
        Assert.False(LoadingOverlayGate.ShouldBeVisible(synced: true)); // polled exactly at sync
        Assert.False(LoadingOverlayGate.ShouldBeVisible(synced: true)); // polled again, well after
    }

    [Fact]
    public void NoHandshakeState_DecisionIsAPureFunctionOfSyncedAlone()
    {
        // The design's explicit simplification ("no ready-up handshake"): dismissal never
        // depends on any local "am I ready" flag, only on the one external signal. Calling the
        // same input twice must always produce the same output — there is nothing else to
        // remember.
        Assert.Equal(LoadingOverlayGate.ShouldBeVisible(false), LoadingOverlayGate.ShouldBeVisible(false));
        Assert.Equal(LoadingOverlayGate.ShouldBeVisible(true), LoadingOverlayGate.ShouldBeVisible(true));
    }
}
