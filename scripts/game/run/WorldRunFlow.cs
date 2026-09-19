namespace Sail.Game.Run;

/// <summary>
/// <b>Whether this world plays a round-structured, quota-scored playthrough at all.</b>
///
/// <para><b>The defect this closes (Talon, 2026-08-29, note 4).</b> A plain launch lands in
/// <c>bubbletest</c>, a movement-and-exploration playtest with no cache, no winter and nothing
/// to bank. It nevertheless ran the full playthrough spine, so the first NightToDawn crossing
/// evaluated <see cref="QuotaMissedPredicate"/> against a ledger nothing in the level can bank into,
/// committed <c>Loss</c>, and put a full-screen verdict — <i>"THE CACHE RAN DRY / Night 1: the
/// cache held 0 of the 3 winter needed"</i> — over a level that had never asked the player for
/// anything. Measured before the fix: a 12 s-period bubbletest session's applied transition
/// list was exactly <c>[2&gt;5@1]</c>, InRound straight to Loss at round 1, outcome
/// <c>QuotaMissed</c>. It was not a rare state; it was the guaranteed end of every session.
/// (The level was given an objective of its own on 2026-09-04 — collect all the bubbles — which
/// is not a scored, round-structured one and does not change this answer.)</para>
///
/// <para><b>Why the gate is here and not on the screen.</b> Blanking the copy, or declining to
/// build <see cref="MpFoundation.Ui.Flow.LossScreen"/>, would leave the server still entering
/// <c>Loss</c> — a state that is not band-live (<see cref="PlaythroughStates.IsBandLive"/>), so
/// payouts, cutoffs and the store's dawn fan all go quiet, and <c>FlowScreens</c> raises
/// <c>WorldUi.Suppressed</c> for a screen that no longer exists. That is the same bug with the
/// evidence removed. A world that has no quota must not reach the verdict instant at all.</para>
///
/// <para><b>What is deliberately NOT deleted.</b> <see cref="PlaythroughMachine"/>,
/// <see cref="QuotaLedger"/>, the predicate and the loss screen are a real feature with live
/// suites (<c>Run-FlowTest.ps1</c>, <c>Run-ScreenFlowTest.ps1</c>, <c>PlaythroughMachineTests</c>,
/// <c>FlowScreensTests</c>) and a committed <c>QuotaMissed</c> fixture in
/// <c>ScriptedPlaythrough</c>. Every world except the bubble test keeps them unchanged; this is
/// one arm in one switch, in the shape <see cref="MpFoundation.Ui.Hud.HudProfile"/> already
/// established for the same class of defect ("a readout of a system this world does not run").</para>
///
/// <para><b>Unknown world means the full flow</b>, exactly as an unknown world means the full
/// HUD: a lab scene, the flow demo and the in-engine self-tests have no session to ask and must
/// keep the behaviour they measure.</para>
/// </summary>
public static class WorldRunFlow
{
    /// <summary>Does this world id run rounds, a quota and a loss verdict?
    /// One switch, one arm per world, so the question has exactly one answer.</summary>
    public static bool RunsPlaythrough(string? worldId) => worldId switch
    {
        // The bubble test is a movement-and-exploration playtest: nothing to bank, nothing to
        // buy, no round the player is being scored on. See this class's doc.
        Sail.Game.World.BubbleTest.BubbleTestLayout.WorldId => false,
        _ => true,
    };

    /// <summary>The answer for the world this process is actually running, or <c>true</c> when
    /// there is no session to ask (a lab scene, a demo, a headless self-test).</summary>
    public static bool Current =>
        RunsPlaythrough(MpFoundation.NetworkManager.Instance?.Options?.World);
}
