namespace MpFoundation.Ui.Flow;

/// <summary>Which full-screen flow surface a peer should be showing. One value at a time —
/// the flow screens are mutually exclusive by construction, which is what makes late-join
/// "lands on the correct screen" a single pure decision.</summary>
public enum ScreenId : byte
{
    None = 0,       // in-round: the world (plus the quota strip) is the UI.
    Connecting = 1, // Synced gate — spec §3.5 row 6.
    RoundIntro = 2,
    RoundEnd = 3,
    UpgradeLobby = 4,
    Loss = 5,
}

/// <summary>
/// The one routing decision every flow surface polls: playthrough state → which screen
/// shows. Pure and static (the LoadingOverlayGate seam) so the xUnit walk, the in-engine
/// self-test and every real screen all read the SAME rule — a screen never invents its own
/// visibility logic, it asks whether it is the routed screen.
/// </summary>
public static class ScreenRouter
{
    /// <summary>The routed screen. Never-strand by construction: this is a total function
    /// of current state, so a subscriber that missed every event still lands correctly from
    /// one poll (spec §3.4).</summary>
    public static ScreenId ScreenFor(bool synced, PlaythroughState state)
    {
        if (!synced)
            return ScreenId.Connecting;
        return state switch
        {
            PlaythroughState.Boot => ScreenId.Connecting,
            PlaythroughState.RoundIntro => ScreenId.RoundIntro,
            PlaythroughState.RoundEnd => ScreenId.RoundEnd,
            PlaythroughState.UpgradeLobby => ScreenId.UpgradeLobby,
            PlaythroughState.Loss => ScreenId.Loss,
            _ => ScreenId.None, // InRound — the world is the screen.
        };
    }

    /// <summary>Whether the in-round quota strip may show. RoundIntro counts: banking is
    /// already accepted there (spec §1.3) and the intro card re-anchors the demand the
    /// strip words.</summary>
    public static bool QuotaStripVisible(bool synced, PlaythroughState state) =>
        synced && state is PlaythroughState.RoundIntro or PlaythroughState.InRound;

    /// <summary>Whether the dusk/night telegraph (toasts + nightfall treatment) may fire —
    /// spec §3.5 row 1: suppressed outside the band-live states. A null flow view (the
    /// pre-integration wiring, where only RunDriver exists) permits everything: today's
    /// shipped behavior, unchanged until INT-1 attaches the view.</summary>
    public static bool TelegraphAllowed(IPlaythroughView? view) =>
        view == null || (view.Synced && view.State is PlaythroughState.RoundIntro or PlaythroughState.InRound);

    // --- the CanvasLayer ladder ---------------------------------------------------------
    // B1 documented the rungs it could see, in a comment, here. B2 moved the ladder itself to
    // Design.UiLayers — where every rung in the project lives, including the two collisions and
    // the sunken pause overlay this comment could not have caught. These forward so the flow
    // screens' call sites are unchanged.

    /// <summary>Flow state screens (intro/tally/lobby/loss).</summary>
    public const int StateScreenLayer = Design.UiLayers.StateScreen;

    /// <summary>The nightfall treatment: one rung below the state screens so a verdict screen
    /// committed mid-treatment draws over it, never under it.</summary>
    public const int NightfallLayer = Design.UiLayers.Nightfall;

    /// <summary>The quota strip is HUD furniture, under every state screen.</summary>
    public const int QuotaStripLayer = Design.UiLayers.QuotaStrip;
}
