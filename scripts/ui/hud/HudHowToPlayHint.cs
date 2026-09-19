using Godot;

namespace MpFoundation.Ui.Hud;

/// <summary>
/// <b>Bottom-right, always: where the controls live.</b> One muted line — "ESC · HOW TO PLAY" —
/// standing in for the door that used to open itself.
///
/// <para><b>The defect this closes</b> (2026-09-04, Talon's direction). How-to-Play was shown
/// unprompted on level entry, behind a persisted "don't show this again" tick, and that door is
/// gone: what the player meets on entry now is one transient line naming the objective
/// (<see cref="PhaseToastText.BubbleGoalToast"/>). Remove the door and say nothing, though, and
/// the panel becomes unreachable in practice — it lives inside the pause menu, and a player who
/// never presses ESC never learns there is a control reference at all. A permanent, quiet hint is
/// what pays for removing the interruption.</para>
///
/// <para><b>It names the pause menu, not Settings.</b> HOW TO PLAY is a button on
/// <c>PauseOverlay</c>. Writing "Settings" would send the player to a screen that does not have
/// it, which is the same class of defect as copy naming a mechanic the world lacks.</para>
///
/// <para><b>The key is resolved, never typed.</b> <see cref="ControlGlyphs.BindingFor"/> reads the
/// live <c>InputMap</c>, so a rebound pause key re-labels this hint instead of lying about it —
/// spec §8 scenario 6, the same rule every cap on the How-to-Play panel itself obeys. On the
/// shipped binding it resolves to exactly the authored copy, "ESC · HOW TO PLAY".</para>
///
/// <para><b>It never covers anything.</b> A corner scrap on <see cref="GameHud"/>'s layer, no
/// panel, no input, content-sized: non-diegetic UI may cover the frame only where camera <i>and</i>
/// controls have been taken, and neither is (the standing law, and <c>UiCoverageLaw</c>'s own
/// subject). It sits in the bottom-right because every other reserved region is spoken for — the
/// top-centre column carries the tally and the day/phase readout, the bottom-left carries the room
/// code, and the interact chip is world-projected onto whatever the player is looking at, which is
/// near the middle of the frame by construction. It reserves its height through
/// <see cref="Design.UiColumns"/> so a future bottom-right occupant stacks off it rather than onto
/// it.</para>
///
/// <para><b>Static text, so no <c>Tick()</c>.</b> Every sibling on this HUD is polled because it
/// reports a value that moves; this one reports a key binding, which cannot change inside a
/// session. It is built once and then costs nothing but a rectangle.</para>
/// </summary>
public partial class HudHowToPlayHint : PanelContainer
{
    /// <summary>The action whose binding this hint advertises — the same one
    /// <c>HowToPlayPanel._Input</c> closes on, so the hint and the door can never name different
    /// keys.</summary>
    public const string PauseAction = "pause";

    /// <summary>The rendered line for a resolved key label. Upper-cased here rather than at the
    /// binding table, which is shared with the How-to-Play panel's sentence-cased caps ("Esc"):
    /// this is a HUD scrap and reads in the HUD's register.
    ///
    /// <para>Split out as a pure function so the copy is asserted without an engine — the
    /// <c>InputMap</c> the label comes from needs a live Godot process, the string it lands in does
    /// not.</para></summary>
    public static string Format(string keyLabel) => $"{keyLabel.ToUpperInvariant()} · HOW TO PLAY";

    /// <summary>The line as it will actually render this session, key resolved live.</summary>
    public static string Line => Format(ControlGlyphs.BindingFor(PauseAction).Label);

    public HudHowToPlayHint() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Ready()
    {
        // Bound rather than set once: a scrap that takes its stylebox in _Ready is still lit at
        // noon after nightfall. Never accented — the accent is the HUD's one "this is the thing
        // right now" channel, and a permanent hint is the opposite of that.
        HudTheme.BindPanel(this);
        AddChild(HudTheme.MakeLabel(Line, "Micro"));
    }
}
