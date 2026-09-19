using System.Collections.Generic;
using Godot;

namespace MpFoundation.Ui.Design;

/// <summary>
/// <b>Does the theme actually reach a Control?</b> The in-engine guard for the one property
/// the xUnit suite structurally cannot check.
///
/// <para>The Godot-free suite measures tokens, recipes, resolved specs and contrast — the whole
/// design system as data — and it passed 1722 tests while the running game rendered none of it.
/// <see cref="UiThemeService"/> hung the built theme on the root window, and Godot propagates a
/// theme only into <c>CanvasItem</c> and <c>Window</c> children: <c>Boot</c> is a plain Node and
/// <see cref="UiLayers"/> is a ladder of CanvasLayers, so the theme reached the root window and
/// stopped there. Every Control in the game fell through to the committed
/// <c>resources/UITheme.tres</c> instead.</para>
///
/// <para>It shipped invisible because that .tres was exported at Night from this same factory —
/// at Night the wrong answer and the right answer are the same colour. Only Day disagreed, and
/// Day had never been rendered. A test that lives outside the engine could not have caught it,
/// and a test that only checked Night would not have either.</para>
///
/// <para><b>Positive control included.</b> Asserting "the three parents agree" is worthless on
/// its own: they agreed perfectly before the fix, at Night, while everything was broken. So this
/// also asserts that two different recipe states resolve DIFFERENT values — proving the probe
/// can see a change at all — and that what a Control resolves equals what the factory built.</para>
///
/// <para><b>2026-08-15 (Talon): light mode is retired</b> — <see cref="UiTokens.For"/> always
/// returns <see cref="UiTokens.Night"/> now, so Day and Night are no longer a valid two-value
/// probe (they are, by design, identical). The signal moved to <see cref="UiRecipeSet"/> instead
/// — the thing that is still genuinely live-changeable — toggling <c>Action.Ink</c> between two
/// distinct ranks and rebuilding is the same "did a real edit actually reach a Control" proof,
/// on the axis that still varies.</para>
///
/// <code>godot --headless --path . -- --ui-theme-selftest</code>
///
/// <para>Exits 0 on pass, 1 on failure, and prints every measured value either way.</para>
/// </summary>
public partial class UiThemeSelfTest : Node
{
    private readonly List<string> _failures = new();

    public override void _Ready() => Callable.From(Run).CallDeferred();

    private void Run()
    {
        GD.Print("[ui-theme-selftest] theme reach and recipe response");

        UiRecipeSet shipped = UiRecipeSet.Current;
        (string Label, Color Root, Color UnderNode, Color UnderLayer, Color Built)[] measured =
        {
            Measure("rank1", shipped with { Action = shipped.Action with { Ink = InkRole.Rank1 } }),
            Measure("rank3", shipped with { Action = shipped.Action with { Ink = InkRole.Rank3 } }),
        };
        UiRecipeSet.Current = shipped;
        UiThemeService.Instance?.Rebuild();

        foreach ((string label, Color root, Color underNode, Color underLayer, Color built) in measured)
        {
            GD.Print($"[ui-theme-selftest] {label}: built={built.ToHtml()} root={root.ToHtml()} " +
                     $"under-Node={underNode.ToHtml()} under-CanvasLayer={underLayer.ToHtml()}");

            // The theme the service built is the theme a Control must resolve — through every
            // parent shape the game actually uses.
            Check(root == built, $"{label}: a Control parented to the root window resolved " +
                                 $"{root.ToHtml()}, but the built theme says {built.ToHtml()}.");
            Check(underNode == built, $"{label}: a Control under a plain Node resolved " +
                                      $"{underNode.ToHtml()}, but the built theme says {built.ToHtml()}. " +
                                      "The theme is not reaching Boot's subtree.");
            Check(underLayer == built, $"{label}: a Control under a CanvasLayer resolved " +
                                       $"{underLayer.ToHtml()}, but the built theme says {built.ToHtml()}. " +
                                       "The theme is not reaching the UiLayers ladder.");
        }

        // The positive control. Without this the three checks above pass on a game that renders
        // one hardcoded palette forever — which is exactly the state this test was written for.
        Check(measured[0].Built != measured[1].Built,
            "Two different Action.Ink recipes built the SAME PrimaryAction font colour. A recipe " +
            "edit is not moving the theme, so the agreement checks above prove nothing.");
        Check(measured[0].UnderLayer != measured[1].UnderLayer,
            "A Control under a CanvasLayer resolved the same colour for two different recipes. It " +
            "is pinned to one build — the defect this test exists to catch.");

        foreach (string failure in _failures)
            GD.PushError($"[ui-theme-selftest] FAIL {failure}");

        GD.Print(_failures.Count == 0
            ? "[ui-theme-selftest] PASS"
            : $"[ui-theme-selftest] FAIL {_failures.Count} check(s)");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    /// <summary>Resolve a PrimaryAction button's font colour through each parent shape the game
    /// puts Controls under, after rebuilding the theme from <paramref name="recipes"/>.</summary>
    private (string, Color, Color, Color, Color) Measure(string label, UiRecipeSet recipes)
    {
        UiRecipeSet.Current = recipes;
        UiThemeService.Instance?.Rebuild();

        var underRoot = new Button { ThemeTypeVariation = "PrimaryAction" };
        GetTree().Root.AddChild(underRoot);

        var underNode = new Button { ThemeTypeVariation = "PrimaryAction" };
        AddChild(underNode);

        var layer = new CanvasLayer();
        GetTree().Root.AddChild(layer);
        var underLayer = new Button { ThemeTypeVariation = "PrimaryAction" };
        layer.AddChild(underLayer);

        Color built = UiThemeService.Instance?.CurrentTheme?.GetColor("font_color", "PrimaryAction")
                      ?? new Color(0f, 0f, 0f, 0f);

        var result = (label, underRoot.GetThemeColor("font_color"),
            underNode.GetThemeColor("font_color"), underLayer.GetThemeColor("font_color"), built);

        underRoot.QueueFree();
        underNode.QueueFree();
        layer.QueueFree();
        return result;
    }

    private void Check(bool condition, string failure)
    {
        if (!condition)
            _failures.Add(failure);
    }
}
