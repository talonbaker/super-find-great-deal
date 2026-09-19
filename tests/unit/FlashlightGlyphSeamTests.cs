using System.Linq;
using MpFoundation.Game.Light;
using MpFoundation.Ui;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The one seam between NIGHT-2 and HOWTO-2, pinned — because it fails SILENTLY.</b>
///
/// <para>Talon's 2026-08-29 note 10 asked for the night toast to read <i>"Press F to toggle
/// flashlight"</i>. Two packets built the two halves in parallel branches that never saw each
/// other: NIGHT-2 wrote the flashlight and registered its input action; HOWTO-2 rebuilt the How
/// To Play screen so every row's glyph is resolved from the live <c>InputMap</c> rather than
/// typed, which is what stops the legend lying about a binding.</para>
///
/// <para><b>HOWTO-2 could not know the action's name</b>, so its flashlight row is a conditional
/// forward declaration listing four candidates, and <c>ControlGlyphs.DeclaredSections</c> drops
/// any row whose actions are all absent. That guard is correct and deliberate — it is what stops
/// the screen advertising a verb the build does not have. But it means a name mismatch produces
/// **no error and no warning**: the row simply is not there, and the flashlight becomes a feature
/// with no discoverability at all except the one toast.</para>
///
/// <para><b>Why a test and not a capture.</b> The obvious verification is to photograph the
/// screen. It would have lied. <c>UiCaptureLab</c> — the <c>--ui-capture</c> rig — builds UI in
/// isolation and never runs <c>FlashlightController.Attach</c>, so the action is not registered
/// and the row is legitimately absent there. A capture through that rig shows an empty row and
/// gives no way to tell a rig artefact from a real mismatch. This assertion reads both sides'
/// declarations directly, so it cannot be fooled by which harness is running.</para>
///
/// <para><b>The other half of the seam — ordering — is structural and is noted here rather than
/// asserted.</b> <c>DeclaredSections</c> is a lazily-evaluated property, so it reads the
/// <c>InputMap</c> at render time, and in <c>Gameplay</c> the controller attaches (registering
/// the action) well before the panel is instantiated. Both facts are load-bearing; if someone
/// ever builds the panel earlier than the controller, this test still passes and the row still
/// vanishes. That is the residual risk, and it is written down rather than left to be
/// rediscovered.</para>
/// </summary>
public class FlashlightGlyphSeamTests
{
    private static string[] FlashlightRowActions() =>
        ControlGlyphs.Sections
            .SelectMany(s => s.Rows)
            .Where(r => r.Label == "Flashlight")
            .SelectMany(r => r.Actions)
            .ToArray();

    [Fact]
    public void HowToPlayCanResolveTheFlashlight_OrTheRowVanishesWithNoError()
    {
        string[] candidates = FlashlightRowActions();

        Assert.True(candidates.Length > 0,
            "ControlGlyphs has no row labelled \"Flashlight\". If the row was renamed, rename it "
            + "here too - this assertion is the only thing standing between a renamed action and "
            + "a flashlight the How To Play screen never mentions.");

        Assert.True(
            candidates.Contains(FlashlightController.ActionName),
            $"FlashlightController registers the input action "
            + $"\"{FlashlightController.ActionName}\", but ControlGlyphs' "
            + $"Flashlight row only looks for [{string.Join(", ", candidates)}]. "
            + "DeclaredSections drops a row whose actions are all absent, so this mismatch does "
            + "NOT raise anything at runtime - the row silently disappears and the flashlight "
            + "loses its only discoverability besides the nightfall toast. Add the new name to "
            + "the row's candidate list, or rename the action back.");
    }

    [Fact]
    public void TheNightfallToastNamesTheKeyThatIsActuallyBound()
    {
        // The toast is the flashlight's whole discoverability today - nothing else in the game
        // mentions F. If the binding ever moves off F, this sentence becomes a lie in the one
        // place a player is guaranteed to read it, at the exact moment they need it.
        Assert.Contains("Press F", PhaseToastText.NightfallToast);
        Assert.Contains("flashlight", PhaseToastText.NightfallToast);
    }
}
