using System.Collections.Generic;
using System.Linq;
using MpFoundation.Ui.Design;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// The stacking order, as assertions. The audit found two collisions and a pause overlay
/// beneath the HUD it covers; none of that was visible from any single call site, which is
/// precisely why it survived. A ladder in one file is only worth something if adding a rung to
/// it cannot silently re-create the problem.
/// </summary>
public class UiLayerLadderTests
{
    [Fact]
    public void NoTwoSurfacesShareARung()
    {
        var byLayer = new Dictionary<int, string>();
        foreach ((string name, int layer) in UiLayers.All)
        {
            Assert.False(byLayer.TryGetValue(layer, out string? twin),
                $"{name} and {twin} both sit at layer {layer} — their draw order is whichever the tree happens to give them.");
            byLayer[layer] = name;
        }
    }

    /// <summary>A player who presses pause has stopped playing. Nothing the round or the world
    /// is doing outranks that, so pause draws over the HUD, over any state screen, and over
    /// every sensory effect.</summary>
    [Fact]
    public void Pause_CoversEverythingItIsSupposedToCover()
    {
        foreach ((string name, int layer) in new[]
                 {
                     (nameof(UiLayers.GameHud), UiLayers.GameHud),
                     (nameof(UiLayers.QuotaStrip), UiLayers.QuotaStrip),
                     (nameof(UiLayers.StateScreen), UiLayers.StateScreen),
                     (nameof(UiLayers.Nightfall), UiLayers.Nightfall),
                     (nameof(UiLayers.ChillCue), UiLayers.ChillCue),
                     (nameof(UiLayers.InteractPrompt), UiLayers.InteractPrompt),
                     (nameof(UiLayers.ConsequenceWarning), UiLayers.ConsequenceWarning),
                 })
            Assert.True(UiLayers.PauseOverlay > layer,
                $"pause ({UiLayers.PauseOverlay}) does not cover {name} ({layer}).");

        // ...and what pause opens draws over pause.
        Assert.True(UiLayers.PauseChildPanel > UiLayers.PauseOverlay);
    }

    /// <summary>A state screen owns the frame: it is what the round has become, so it covers
    /// every HUD readout. It still sits under the sensory effects and under pause.</summary>
    [Fact]
    public void StateScreens_OwnTheFrameOverTheHud_ButNotOverPause()
    {
        Assert.True(UiLayers.StateScreen > UiLayers.GameHud);
        Assert.True(UiLayers.StateScreen > UiLayers.QuotaStrip);
        Assert.True(UiLayers.StateScreen > UiLayers.Nightfall,
            "a verdict committed mid-nightfall must draw over the treatment, never under it");
        Assert.True(UiLayers.StateScreen < UiLayers.PauseOverlay);
    }

    /// <summary>Everything anchored to something in the world sits under everything anchored to
    /// the frame. The interact chip belongs to a log; the HUD belongs to the screen.</summary>
    [Fact]
    public void WorldFurniture_SitsUnderTheHud()
    {
        foreach ((string name, int layer) in new[]
                 {
                     (nameof(UiLayers.InteractPrompt), UiLayers.InteractPrompt),
                     (nameof(UiLayers.SessionSummary), UiLayers.SessionSummary),
                     // Screen-space, but it belongs to a lever: it says nothing once you walk
                     // away from that lever, so it is furniture for a thing in the world and
                     // ranks with the rest of it.
                     (nameof(UiLayers.ConsequenceWarning), UiLayers.ConsequenceWarning),
                 })
            Assert.True(layer < UiLayers.GameHud, $"{name} ({layer}) is not under the HUD ({UiLayers.GameHud}).");

        // LEVER-2: a state screen means the round is between states and the lever cannot be
        // pressed, so a warning about pressing it must never draw over the screen that took the
        // frame. (It is also actively hidden there — see ConsequenceWarning's WorldUi.Suppressed
        // check — and this is the belt to that's braces.)
        Assert.True(UiLayers.StateScreen > UiLayers.ConsequenceWarning);
    }

    /// <summary>Diagnostics are not part of the interface and must never be mistaken for it:
    /// they sit above everything, including the loading overlay.</summary>
    [Fact]
    public void Diagnostics_SitAboveTheWholeInterface()
    {
        int highestInterface = UiLayers.All
            .Where(r => r.Name is not (nameof(UiLayers.PerfHud) or nameof(UiLayers.Telemetry)))
            .Max(r => r.Layer);

        Assert.True(UiLayers.PerfHud > highestInterface);
        Assert.True(UiLayers.Telemetry > highestInterface);
    }

    /// <summary>The loading overlay's whole contract is that it covers the screen while it is
    /// up — including pause, which a player can otherwise open behind it.</summary>
    [Fact]
    public void LoadingOverlay_CoversTheInterface()
    {
        Assert.True(UiLayers.LoadingOverlay > UiLayers.PauseChildPanel);
        Assert.True(UiLayers.LoadingOverlay > UiLayers.StateScreen);
    }
}
