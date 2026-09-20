using Godot;
using MpFoundation.Game.World;

namespace MpFoundation.Ui.Hud;

/// <summary>
/// Top-centre: which day of the run this is, and which part of it — "DAY 2 · NIGHT".
///
/// <b>Why the centre and not a corner.</b> Talon's brief puts it there, and the reason it works
/// is that this is the only readout on screen that changes the meaning of everything else. Your
/// tally means one thing on day 1 and another on day 5; the same walk into the woods is a
/// chore in DAY and a decision in DUSK. A value that reframes the rest of the HUD belongs where
/// the eye returns to, not in a corner it has to go looking for.
///
/// <b>It names the sweeps.</b> DUSK and DAWN are short bands, but they are the ones that carry an
/// instruction (see <see cref="HudClock.BandName"/>) — folding dusk into "DAY" would tell the
/// player they have more daylight than they do, which is the exact failure the phase toasts
/// ("Sunset. Head for the light." — <see cref="PhaseToastText.DuskToast"/>) exist to prevent. This readout and those toasts are driven
/// off the same <see cref="CycleBands"/> call, so they cannot disagree.
///
/// <b>The panel gains weight at dusk and the word changes with it</b> — two channels, so this
/// still satisfies ART-BIBLE §3 (read in VALUE, not just hue) with the palette monochrome.
///
/// <b>What the white-film pass cost here.</b> This widget used to swing from a warm accent to a
/// cool one at dusk, which meant a player could tell day from night without reading anything.
/// Talon pulled the black backing and asked for pure white letters (2026-08-08), so that
/// warm/cool pair is gone and day/night now rides on the word plus an opacity step. The
/// information is intact; the at-a-glance read is weaker. Restoring it as a border tint is one
/// line, and it is the first thing to revisit when the settled UI design lands.
///
/// If it does come back, it takes the ZONE convention, not the object one: ART-BIBLE §3's other
/// corollary has danger reading WARM on *objects and accents*, which taken naively would make
/// night the amber one — but that corollary scopes itself to objects and hands ambient
/// temperature to LEVEL-BIBLE §2.2, where danger reads cool/dark and safety warm/open. A
/// day/night readout is an ambient condition, so night is the cool one.
/// </summary>
public partial class DayPhaseWidget : PanelContainer
{
    private Label _line = null!;
    private string _lastText = string.Empty;
    private bool _lastNightSide;
    private bool _styled;

    public DayPhaseWidget() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Ready()
    {
        // Bound rather than set: the night side of the readout is also the side where the
        // interface's own light has gone, so this panel has to re-take the palette at dusk.
        HudTheme.BindPanel(this, () => _lastNightSide);
        _line = HudTheme.MakeLabel("DAY 1 · DAY", HudTheme.RoleBody, HorizontalAlignment.Center);
        AddChild(_line);
        Tick();
    }

    /// <summary>Driven by <see cref="GameHud"/>'s poll. Holds the last-applied text until
    /// <see cref="CycleDriver"/> is synced — the same "never render an uninitialized frame"
    /// discipline <c>DayNightSky._Process</c> applies, and for the same reason: CycleDriver's
    /// zero-initialized default would briefly render "DAY 1 · DAY" on a client joining a run that
    /// is actually on night 3.</summary>
    public void Tick()
    {
        if (CycleDriver.Instance is not { Synced: true } driver)
            return;

        string text = HudClock.DayPhaseText(driver.Phase, driver.CyclesElapsed);
        bool nightSide = HudClock.IsNightSide(
            CycleBands.GetBand(driver.Phase, driver.CyclesElapsed, out _));

        if (_styled && text == _lastText && nightSide == _lastNightSide)
            return;

        // Compare BANDS, not the whole line: the text also carries the day number, and rolling
        // from "DAY 1 · DAWN" to "DAY 2 · DAY" must flash once for the phase crossing, not twice
        // because the digit moved too. Captured before _lastText is overwritten — the previous
        // value is the whole input to this decision.
        bool bandChanged = _styled && BandOf(text) != BandOf(_lastText);
        bool first = !_styled;

        _styled = true;
        _lastText = text;
        _lastNightSide = nightSide;

        _line.Text = text;
        // The panel gains weight on the night side — the HUD equivalent of the light going.
        AddThemeStyleboxOverride("panel", HudTheme.PanelStyle(accented: nightSide));

        // A phase crossing is the most consequential thing this HUD reports — dusk means start
        // walking back. It used to change silently, so the readout only helped a player already
        // looking at it. Suppressed on first paint: a client joining at night must not be told
        // night just "happened" to it.
        if (bandChanged && !first)
            HudMotion.FlashText(_line, HudTheme.AccentPrimary);
    }

    /// <summary>The band word out of "DAY 2 · NIGHT". Splitting the rendered string rather than
    /// re-deriving the band keeps this in lockstep with what is actually on screen — if the two
    /// ever disagreed, the flash would fire for a transition the player never saw.</summary>
    private static string BandOf(string line)
    {
        int sep = line.LastIndexOf('·');
        return sep >= 0 ? line[(sep + 1)..].Trim() : line;
    }
}
