using System.Collections.Generic;
using Godot;
using MpFoundation.Game.Presentation;

namespace MpFoundation.Ui.Flow;

/// <summary>
/// In-engine headless proof for CORE-PROG-B1 (<c>--screenflow-selftest</c>;
/// Run-ScreenFlowTest.ps1 gates on it): walks THE canonical
/// <see cref="ScriptedPlaythrough.Steps"/> against real kit-built CanvasLayers in a live
/// tree and asserts, per step, that exactly the routed screen is visible; that a FRESH
/// composer attached mid-state (a late joiner that saw no events) lands on the same screen
/// from its first poll; and, at the end, that every kit component is genuinely reused by
/// ≥2 surfaces (counted from the <see cref="UiKit.ComponentMeta"/> stamps in the actual
/// trees, not from a comment).
///
/// What this proves that the (larger) xUnit coverage cannot: the screens CONSTRUCT — theme
/// variations resolve, _Ready builds, tweens run — under the real engine. What the xUnit
/// side proves that this cannot: the display-decision statics' full value tables.
/// </summary>
public partial class ScreenFlowSelfTest : Node
{
    private readonly List<string> _failures = new();

    public override void _Ready() => Run();

    private async void Run()
    {
        // An unhandled throw in an async void would leave the process alive forever with
        // no verdict (observed 2026-08-14, catching the strip's stale-handler bug) — every
        // failure path must still reach Finish and exit nonzero.
        try
        {
            await RunChecks();
        }
        catch (System.Exception e)
        {
            Fail($"unhandled: {e}");
        }
        Finish();
    }

    private async System.Threading.Tasks.Task RunChecks()
    {
        var driver = new FakePlaythroughDriver();
        _driverRef = driver;
        FlowScreens screens = FlowScreens.Attach(this, driver, driver, leaveToMenu: null, withToasts: true);

        // Expected routed screen after each scripted step, index-aligned with Steps.
        ScreenId[] expected =
        {
            ScreenId.Connecting, // pre-sync boot
            ScreenId.Connecting, // synced, still Boot
            ScreenId.RoundIntro,
            ScreenId.None,       // round live (day)
            ScreenId.None,       // bank
            ScreenId.None,       // dusk cue
            ScreenId.None,       // nightfall cue
            ScreenId.None,       // bank
            ScreenId.RoundEnd,
            ScreenId.UpgradeLobby,
            ScreenId.RoundIntro, // round 2
            ScreenId.None,
            ScreenId.None,       // nightfall
            ScreenId.Loss,
        };
        if (expected.Length != ScriptedPlaythrough.Steps.Count)
        {
            Fail($"expectation table has {expected.Length} entries for {ScriptedPlaythrough.Steps.Count} steps");
            return; // Run() finishes.
        }

        for (int i = 0; i < ScriptedPlaythrough.Steps.Count; i++)
        {
            PlaythroughStep step = ScriptedPlaythrough.Steps[i];
            step.Apply?.Invoke(driver);
            screens.ApplyCue(step.Cue);
            await WaitFrames(2);

            ScreenId routed = ScreenRouter.ScreenFor(driver.Synced, driver.State);
            GD.Print($"SCREENFLOW: step={i} \"{step.Name}\" routed={routed}");
            if (routed != expected[i])
                Fail($"step {i} \"{step.Name}\": routed {routed}, expected {expected[i]}");
            AssertVisibility(screens, routed, $"walk step {i}");
            if (step.Cue == StepCue.Nightfall && screens.Nightfall is { Playing: false })
                Fail($"step {i}: nightfall cue did not start the treatment");

            // AC3, the headless half of the controller walk: every interactive screen must
            // have handed focus to a real button by the frame after it showed — a
            // controller player is never focus-stranded on a screen with verbs.
            if (routed is ScreenId.RoundEnd or ScreenId.UpgradeLobby or ScreenId.Loss &&
                GetViewport().GuiGetFocusOwner() is not Button)
                Fail($"step {i}: routed {routed} but focus owner is " +
                    $"{GetViewport().GuiGetFocusOwner()?.GetType().Name ?? "nothing"} — controller focus stranded");

            // The late joiner: a fresh composer over the same driver, mid-state, no events.
            FlowScreens lateJoin = FlowScreens.Attach(this, driver, driver, leaveToMenu: null);
            await WaitFrames(2);
            AssertVisibility(lateJoin, routed, $"late join at step {i}");
            lateJoin.QueueFree();
            await WaitFrames(1);
        }

        // Content spot-checks against the latches (the polled path, not the event path).
        if (FindLabelText(screens, "LossScreen") is { } lossHeading &&
            lossHeading != LossScreen.VerdictHeading(driver.LastOutcome!.Value))
            Fail($"loss heading reads \"{lossHeading}\" — not the verdict's own line");

        AssertKitReuse(screens);

        // LOSS-1 (Talon note 4, 2026-08-29): a host with no scored playthrough builds no round
        // surfaces at all. Absent, not hidden - a hidden LossScreen is one routing bug away from
        // being the full-screen dead end he hit in the bubble test. The connect gate must
        // survive: "this peer has no authoritative state yet" is true of every world.
        FlowScreens flowless = FlowScreens.Attach(this, driver, driver, leaveToMenu: null, roundScreens: false);
        await WaitFrames(2);
        foreach (string absent in new[] { "RoundIntroCard", "RoundEndTallyPanel", "UpgradeLobbyPanel", "LossScreen" })
        {
            if (flowless.GetNodeOrNull(absent) != null)
                Fail($"roundScreens:false still built {absent}");
        }
        if (flowless.GetNodeOrNull("ConnectingGate") == null)
            Fail("roundScreens:false dropped the ConnectingGate - a joining peer would have no gate");
        foreach (Node child in flowless.GetChildren())
        {
            if (child is FlowScreenBase { Visible: true } shown && ScreenRouterIdOf(shown) != ScreenId.Connecting)
                Fail($"roundScreens:false shows {shown.Name} - a state screen with no state behind it");
        }
        flowless.QueueFree();
        await WaitFrames(1);
    }

    private void AssertVisibility(FlowScreens screens, ScreenId routed, string context)
    {
        foreach (Node child in screens.GetChildren())
        {
            if (child is FlowScreenBase screen)
            {
                bool expectedVisible = ScreenRouterIdOf(screen) == routed;
                if (screen.Visible != expectedVisible)
                    Fail($"{context}: {screen.Name} visible={screen.Visible}, expected {expectedVisible} (routed {routed})");
            }
            if (child is QuotaStripWidget strip)
            {
                bool expectStrip = ScreenRouter.QuotaStripVisible(_driverRef.Synced, _driverRef.State);
                if (strip.Visible != expectStrip)
                    Fail($"{context}: quota strip visible={strip.Visible}, expected {expectStrip}");
            }
        }
    }

    // The screens expose their routed id only through behavior; for the assert we map by
    // node name so a mis-routed screen cannot vouch for itself.
    private static ScreenId ScreenRouterIdOf(FlowScreenBase screen) => screen.Name.ToString() switch
    {
        "ConnectingGate" => ScreenId.Connecting,
        "RoundIntroCard" => ScreenId.RoundIntro,
        "RoundEndTallyPanel" => ScreenId.RoundEnd,
        "UpgradeLobbyPanel" => ScreenId.UpgradeLobby,
        "LossScreen" => ScreenId.Loss,
        _ => ScreenId.None,
    };

    private FakePlaythroughDriver _driverRef = null!;

    private string? FindLabelText(FlowScreens screens, string screenName)
    {
        Node? screen = screens.GetNodeOrNull(screenName);
        if (screen == null)
            return null;
        foreach (Node node in Walk(screen))
            if (node is Label { } label && label.HasMeta(UiKit.ComponentMeta) &&
                (string)label.GetMeta(UiKit.ComponentMeta) == "Heading")
                return label.Text;
        return null;
    }

    private void AssertKitReuse(FlowScreens screens)
    {
        string[] surfaces =
        {
            "ConnectingGate", "RoundIntroCard", "RoundEndTallyPanel", "UpgradeLobbyPanel",
            "LossScreen", "QuotaStripWidget", "PhaseToastLayer",
        };
        var usage = new Dictionary<string, HashSet<string>>();
        foreach (string surface in surfaces)
        {
            Node? node = screens.GetNodeOrNull(surface);
            if (node == null)
            {
                Fail($"reuse audit: surface {surface} missing");
                continue;
            }
            foreach (Node item in Walk(node))
                if (item.HasMeta(UiKit.ComponentMeta))
                {
                    string component = (string)item.GetMeta(UiKit.ComponentMeta);
                    if (!usage.TryGetValue(component, out HashSet<string>? set))
                        usage[component] = set = new HashSet<string>();
                    set.Add(surface);
                }
        }
        string[] components =
        {
            "Heading", "Body", "Caption", "PrimaryButton", "SecondaryButton", "Panel", "StateScreenScaffold",
        };
        foreach (string component in components)
        {
            int count = usage.TryGetValue(component, out HashSet<string>? set) ? set.Count : 0;
            GD.Print($"SCREENFLOW: component={component} usedBySurfaces={count}");
            if (count < 2)
                Fail($"kit component {component} used by {count} surface(s) — reuse is the deliverable, ≥2 required");
        }
    }

    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        foreach (Node child in node.GetChildren())
            foreach (Node descendant in Walk(child))
                yield return descendant;
    }

    private async System.Threading.Tasks.Task WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Fail(string message)
    {
        _failures.Add(message);
        GD.PrintErr($"[screenflow-selftest] FAIL: {message}");
    }

    private void Finish()
    {
        if (_failures.Count == 0)
            GD.Print("[screenflow-selftest] PASS");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
