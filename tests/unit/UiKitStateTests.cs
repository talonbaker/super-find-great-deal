using System.Collections.Generic;
using Godot;
using MpFoundation.Ui;
using MpFoundation.Ui.Design;

namespace SailNet.Tests;

/// <summary>
/// CORE-PROG-B1 acceptance criterion 3, the Godot-free half: every interactive state of the
/// kit's primary button is DEFINED and pairwise DISTINCT in the pure state functions the
/// factory consumes — so "shows all states" cannot silently collapse into two states
/// sharing one look. (The secondary button rides the shipped theme's GhostAction
/// variation, which carries its own five styleboxes — verified by read of UITheme.tres,
/// re-verified in-engine by the screenflow self-test constructing it.)
/// </summary>
public class UiKitStateTests
{
    [Fact]
    public void PrimaryButton_AllFiveStates_Defined()
    {
        Assert.Equal(5, UiKitStates.All.Length);
        foreach (UiKitStates.VisualState state in UiKitStates.All)
        {
            Color fill = UiKitStates.PrimaryFill(state);
            Assert.True(fill.A > 0f, $"{state}: fill is fully transparent — the state would be invisible");
            Assert.True(UiKitStates.PrimaryBorderWidth(state) >= 1, $"{state}: no border");
        }
    }

    [Fact]
    public void PrimaryButton_States_PairwiseDistinct()
    {
        var seen = new Dictionary<string, UiKitStates.VisualState>();
        foreach (UiKitStates.VisualState state in UiKitStates.All)
        {
            // A state's identity is the whole visual tuple the factory renders.
            string key = $"{UiKitStates.PrimaryFill(state)}|{UiKitStates.PrimaryBorder(state)}|{UiKitStates.PrimaryBorderWidth(state)}";
            Assert.False(seen.TryGetValue(key, out UiKitStates.VisualState twin),
                $"{state} renders identically to {twin}");
            seen[key] = state;
        }
    }

    [Fact]
    public void Focus_IsTheLoudestState_ControllerVisibilityGuarantee()
    {
        // The packet's hard rule: controller focus visible always. Structurally: focus has the
        // widest border of any state, and that border is unmistakable against the fill behind it.
        int focusWidth = UiKitStates.PrimaryBorderWidth(UiKitStates.VisualState.Focus);
        foreach (UiKitStates.VisualState state in UiKitStates.All)
            if (state != UiKitStates.VisualState.Focus)
                Assert.True(focusWidth > UiKitStates.PrimaryBorderWidth(state),
                    $"focus border ({focusWidth}) not wider than {state}");

        // B1 asserted this border was literally Colors.White. B2 replaces that with the focus
        // TOKEN — the fastener by day, the torchlight halo at night — because a white ring is
        // very nearly invisible on cream paper, which is what the day temperature is. The
        // invariant the test is named for is what gets asserted instead, and it is the stronger
        // claim: whatever colour focus resolves to, it must separate from the surface it rings,
        // at both temperatures.
        foreach (UiTemperature temperature in new[] { UiTemperature.Day, UiTemperature.Night })
        {
            StyleSpec focus = UiStyle.Resolve(
                UiRecipeSet.Default.Action, UiState.Focus, UiTokens.For(temperature));
            float ratio = UiStyle.Contrast(focus.Border, focus.Fill);
            Assert.True(ratio >= 1.6f, $"{temperature}: focus ring is {ratio:0.00}:1 against its own fill");
        }
    }

    [Fact]
    public void Disabled_ReadsDisabled_NotJustDimmer()
    {
        // Disabled must leave the ember family entirely (a greyed accent still reads
        // clickable); assert it is desaturated relative to every live state.
        Color disabled = UiKitStates.PrimaryFill(UiKitStates.VisualState.Disabled);
        float disabledSat = Saturation(disabled);
        foreach (UiKitStates.VisualState state in new[]
                 { UiKitStates.VisualState.Normal, UiKitStates.VisualState.Hover, UiKitStates.VisualState.Pressed })
            Assert.True(disabledSat < Saturation(UiKitStates.PrimaryFill(state)) * 0.5f,
                $"disabled fill is not clearly desaturated vs {state}");

        // --- added 2026-08-29 by UI-3. The assertion above is UNCHANGED and still has to pass on
        // its own terms; these are additional bars, not a replacement for it.
        //
        // WHY IT NEEDED PROPPING UP. The relative bar measures the disabled fill against the
        // accent, so how strict it is depends on how saturated the accent happens to be. Under
        // the ember (saturation 0.750) it demanded under 0.375 and got 0.119. Under moonlight
        // (0.238) it demands under 0.1189 and the shipped fill was 0.1190 — it failed by a
        // ten-thousandth, and it would have been UNSATISFIABLE against a fully neutral accent,
        // where the bar is "strictly less than zero". A rule stated only in relative terms gets
        // quietly harder every time the accent gets quieter, for reasons that have nothing to do
        // with the disabled control.
        //
        // So the rule is also stated absolutely: inert is NEUTRAL, full stop. That does not move
        // with the accent and cannot be re-broken by a future palette.
        Assert.True(disabledSat <= 0.02f,
            $"the inert fill is not neutral (saturation {disabledSat:0.000}) — a disabled control "
            + "reads as inert by leaving the accent's hue family, not by being a dimmer accent");

        // ...and the requirement the whole test is named for, measured directly rather than
        // inferred from saturation: a player must be able to tell a dead button from a live one.
        // Saturation alone cannot answer that — a neutral fill and a neutral accent would satisfy
        // every check above and be indistinguishable on screen.
        float deadVsLive = UiStyle.ValueSeparation(
            disabled, UiKitStates.PrimaryFill(UiKitStates.VisualState.Normal));
        Assert.True(deadVsLive >= UiStyle.ValueSeparationFloor,
            $"a disabled plate sits only {deadVsLive:0.0} L* from a live one — it reads as the "
            + "same button, which is the failure this test exists to prevent");
    }

    private static float Saturation(Color c)
    {
        float max = Mathf.Max(c.R, Mathf.Max(c.G, c.B));
        float min = Mathf.Min(c.R, Mathf.Min(c.G, c.B));
        return max <= 0f ? 0f : (max - min) / max;
    }
}
