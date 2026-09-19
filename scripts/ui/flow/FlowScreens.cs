using System;
using Godot;

namespace MpFoundation.Ui.Flow;

/// <summary>
/// The one composer for CORE-PROG-B1's flow surfaces: builds every routed screen (connect
/// gate, intro card, tally, lobby, loss), the quota strip and the nightfall overlay from
/// the kit, against an <see cref="IPlaythroughView"/>/<see cref="IQuotaView"/> pair.
///
/// <b>This is the Attach seam CORE-INT-1 wires</b> — one call in <c>Gameplay</c> with the
/// real driver's adapter and the real <c>LeaveToMenu</c>; nothing in here touches
/// <c>Gameplay</c> or any live wiring (workstream separation, spec §4). Pre-integration,
/// the same call with a <c>FakePlaythroughDriver</c> is the demo and the self-test.
///
/// Owns the <see cref="WorldUi.Suppressed"/> contribution for state screens: suppressed
/// while any full-screen flow state is routed, released on return to in-round — the
/// SessionSummaryPanel discipline, kept in one place so five screens cannot fight over one
/// flag.
/// </summary>
public partial class FlowScreens : Node
{
    private IPlaythroughView _view = null!;
    private bool _suppressing;

    /// <summary>The routed state screens this composer actually BUILT. Suppression is checked
    /// against it, so a routed screen that does not exist in this world can never take the world
    /// UI away with nothing to show for it — see <see cref="_Process"/>.</summary>
    private readonly System.Collections.Generic.HashSet<ScreenId> _built = new();

    /// <summary>The nightfall treatment, when this composer owns a toast layer (demo /
    /// self-test). In live play the overlay is Gameplay's own PhaseToastLayer's child and
    /// the real crossing drives it — this handle stays null there by design.</summary>
    public NightfallOverlay? Nightfall => Toasts?.Nightfall;

    public PhaseToastLayer? Toasts { get; private set; }

    /// <summary>Whether hiding a flow screen recaptures the mouse (true in live play,
    /// false in the demo, which has no captured-mouse world under it).</summary>
    public static bool RecaptureMouseOnHide { get; set; }

    /// <summary>Builds and attaches the whole surface set under <paramref name="root"/>.
    /// <paramref name="leaveToMenu"/> null (demo) leaves the loss screen's menu verb
    /// visibly labeled but inert. <paramref name="withToasts"/> adds a PhaseToastLayer for
    /// hosts that don't already have one (the demo); Gameplay already builds its own.
    ///
    /// <para><paramref name="roundScreens"/> (LOSS-1) is whether this host has a scored
    /// playthrough to narrate. It is a PARAMETER rather than a read of
    /// <c>Sail.Game.Run.WorldRunFlow.Current</c> on purpose: <c>NetworkManager</c> is an autoload,
    /// so <c>Options.World</c> is never null — it is <c>LaunchOptions.DefaultWorld</c>
    /// ("bubbletest") in any scene launched without <c>--world</c>. A profile read inside this
    /// method would therefore delete four screens out of the flow demo, the screen self-test and
    /// UiCaptureLab, which build them against a FakePlaythroughDriver and have no world at all.
    /// (<c>Run-ScreenFlowTest.ps1</c> already passes <c>--world camp</c> for exactly this reason,
    /// against the QuotaStrip line below — so this is a known trap, not a hypothetical.) The
    /// default is TRUE and only <c>Gameplay</c>, which has a real session, passes the world's
    /// answer.</para></summary>
    public static FlowScreens Attach(
        Node root, IPlaythroughView view, IQuotaView quota, Action? leaveToMenu,
        bool withToasts = false, bool roundScreens = true)
    {
        var screens = new FlowScreens { Name = "FlowScreens", _view = view };
        // The connect gate is not a playthrough surface — it is "this peer has no authoritative
        // state yet", true of every world — so it is built unconditionally.
        screens.AddChild(new ConnectingGate(view) { Name = "ConnectingGate" });
        // LOSS-1 (Talon note 4, 2026-08-29). The four ROUND surfaces exist to narrate a scored
        // playthrough: a round beginning, a round's tally, the shop between rounds, and the run
        // ending. A world that plays no playthrough (Sail.Game.Run.WorldRunFlow — the bubble
        // test) has none of those moments, and building the screens for them is what put "THE
        // CACHE RAN DRY / Night 1: the cache held 0 of the 3 winter needed" over a movement-and-
        // exploration playtest with a pair of buttons and no way out. The server-side half of
        // the fix (PlaythroughMachine.RunsPlaythrough) means the states are never entered at
        // all; not constructing the screens is the same decision stated on the client, so a
        // future regression on either side cannot resurrect the screen on its own.
        //
        // Same shape and same reason as the QuotaStrip line below, which BT-8 already cut for
        // this world: a surface for a system this world does not run is not neutral clutter.
        if (roundScreens)
        {
            screens.AddChild(new RoundIntroCard(view, quota) { Name = "RoundIntroCard" });
            screens.AddChild(new RoundEndTallyPanel(view) { Name = "RoundEndTallyPanel" });
            screens.AddChild(new UpgradeLobbyPanel(view) { Name = "UpgradeLobbyPanel" });
            screens.AddChild(new LossScreen(view, leaveToMenu) { Name = "LossScreen" });
            screens._built.Add(ScreenId.RoundIntro);
            screens._built.Add(ScreenId.RoundEnd);
            screens._built.Add(ScreenId.UpgradeLobby);
            screens._built.Add(ScreenId.Loss);
        }
        // BT-8: the winter-cache strip is a readout of the camp's quota, and the same per-world
        // HudProfile that removes the wallet and the minimap removes it.
        //
        // CORRECTION (LOSS-1, 2026-08-29): this comment used to say "the demo and the flow
        // self-test have no session and therefore no world id". That is FALSE - NetworkManager is
        // an autoload, so HudProfile.Current resolves LaunchOptions.DefaultWorld ("bubbletest")
        // and hands them the STRIPPED profile. They keep the strip only because
        // Run-ScreenFlowTest.ps1 passes an explicit --world camp, which its own header records as
        // the difference between red and green. That is why roundScreens above is a parameter and
        // not a second read of the world. See LaunchOptions.DefaultWorld's trap note.
        if (Hud.HudProfile.Current.QuotaStrip)
            screens.AddChild(new QuotaStripWidget(view, quota) { Name = "QuotaStripWidget" });
        if (withToasts)
        {
            screens.Toasts = new PhaseToastLayer { Name = "PhaseToastLayer" };
            screens.AddChild(screens.Toasts);
        }
        root.AddChild(screens);
        return screens;
    }

    /// <summary>Plays a scripted band cue the way the live crossing would — the demo's and
    /// self-test's stand-in for RunDriver.PhaseCrossed (see ScriptedPlaythrough's doc).</summary>
    public void ApplyCue(Game.Presentation.StepCue cue)
    {
        switch (cue)
        {
            case Game.Presentation.StepCue.Dusk:
                Toasts?.ShowLine(PhaseToastText.TextFor(Game.World.PhaseEventKind.DayToDusk)!);
                break;
            case Game.Presentation.StepCue.Nightfall:
                Toasts?.ShowLine(PhaseToastText.TextFor(Game.World.PhaseEventKind.DuskToNight)!);
                Nightfall?.ShowNightfall();
                break;
        }
    }

    public override void _Process(double delta)
    {
        ScreenId routed = ScreenRouter.ScreenFor(_view.Synced, _view.State);
        // Suppress for a screen that is actually up. Routing alone is not enough: a world that
        // builds no state screens (LOSS-1) would otherwise blank nameplates and the bubble tally
        // for a panel nobody can see — the invisible half of the very bug this packet closes.
        bool wantSuppression = routed is not (ScreenId.None or ScreenId.Connecting)
                               && _built.Contains(routed);
        if (wantSuppression == _suppressing)
            return;
        _suppressing = wantSuppression;
        WorldUi.Suppressed = wantSuppression;
        if (!wantSuppression && RecaptureMouseOnHide)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _ExitTree()
    {
        // A stale flag must not survive this node dying mid-screen — the
        // SessionSummaryPanel._ExitTree discipline.
        if (_suppressing)
            WorldUi.Suppressed = false;
    }
}
