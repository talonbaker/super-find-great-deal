using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Game.World;

namespace MpFoundation.Ui.Design;

/// <summary>
/// <b>The live theme.</b> Builds the theme from the current tokens and recipes, hangs it on the
/// scene tree's root window so every Control in the game inherits it, and moves it between the
/// two temperatures when the world's day/night clock crosses.
///
/// <para><b>Why the theme is built at runtime.</b> The audit's finding 10 was that
/// <c>tools/BuildTheme.gd</c> had drifted from the <c>UITheme.tres</c> it produced, so
/// regenerating would silently restyle the game — and the standing "never regenerate" caution
/// that followed is how the project ended up with three styling systems. Building from the same
/// C# tokens the widgets read deletes the artefact that could drift. Edit
/// <see cref="UiTokens"/> or <see cref="UiRecipeSet"/>, call <see cref="Rebuild"/>, and the whole
/// interface follows on the next frame — no export step, no stale file, nothing to reconcile.</para>
///
/// <para><b>The temperature swap is not a skin swap.</b> Dusk interpolates the token set (see
/// <see cref="UiTokens.Lerp"/>) and rebuilds; every component, layout and metric is identical
/// at both ends, so nothing reflows as the light goes. That is PAPER &amp; FIRELIGHT's kill
/// rule 2 enforced by construction rather than by review: there is no code path that could
/// express a night-only object.</para>
/// </summary>
public partial class UiThemeService : Node
{
    /// <summary>The one instance. Null in a headless run or before boot — every accessor below
    /// is written to work without it.</summary>
    public static UiThemeService? Instance { get; private set; }

    /// <summary>
    /// <b>Which temperature the front of house is lit at.</b> Menus, settings and the splash sit
    /// outside the round, so they take this rather than the world clock.
    ///
    /// <para>Night, deliberately: the main menu <i>is</i> a warm glow in the dark — the night
    /// menu scene is the shipped anchor and the direction keeps it. Flip this one value to
    /// see the entire front of house at noon.</para>
    /// </summary>
    public static UiTemperature FrontEnd { get; set; } = UiTemperature.Night;

    /// <summary>How many rebuilds the dusk crossfade takes. Twelve over two seconds reads as
    /// continuous and costs twelve theme builds per crossing, twice a round.</summary>
    private const int CrossfadeSteps = 12;

    private static UiTokens _tokens = UiTokens.For(FrontEnd);
    private Theme? _theme;
    private Tween? _crossfade;
    private RunDriver? _subscribed;

    /// <summary>
    /// <b>The live token set</b> — including mid-crossfade blends. Anything that draws itself
    /// rather than taking a stylebox (the HUD scraps, the minimap, the glyph chrome) reads
    /// this, so custom-drawn surfaces and themed controls can never
    /// disagree about what colour the interface is.
    /// </summary>
    public static UiTokens Tokens => _tokens;

    /// <summary>Raised whenever the palette moves — a temperature crossing, a crossfade step, or
    /// a <see cref="Rebuild"/> after a token edit. Custom-drawn controls subscribe and redraw;
    /// themed controls need nothing, because the theme they inherit has already changed.</summary>
    public static event Action<UiTokens>? TokensChanged;

    public override void _Ready()
    {
        Instance = this;
        Name = nameof(UiThemeService);
        ProcessMode = ProcessModeEnum.Always; // the pause overlay is themed too.

        // Adoption has to be armed before the first apply, and the existing tree swept, or the
        // theme lands on the root window and nowhere else. See AdoptionRoots.
        GetTree().NodeAdded += OnNodeAdded;
        // From the root's CHILDREN: the root window is a theme root itself and would end the
        // sweep on its first step, leaving everything already in the tree unadopted.
        foreach (Node child in GetTree().Root.GetChildren())
            SweepForAdoption(child);

        ApplyTokens(UiTokens.For(FrontEnd));
        TrySubscribe();
    }

    public override void _ExitTree()
    {
        if (GetTree() is { } tree)
            tree.NodeAdded -= OnNodeAdded;
        _adopted.Clear();
        Unsubscribe();
        if (Instance == this)
            Instance = null;
    }

    /// <summary>The world clock may not exist yet when the service boots (the menu has no
    /// RunDriver at all). Poll cheaply until it does, then stop.</summary>
    public override void _Process(double delta)
    {
        if (_subscribed == null)
            TrySubscribe();
    }

    private void TrySubscribe()
    {
        if (RunDriver.Instance is not { } driver || _subscribed == driver)
            return;
        Unsubscribe();
        driver.PhaseCrossed += OnPhaseCrossed;
        _subscribed = driver;
    }

    private void Unsubscribe()
    {
        if (_subscribed is { } previous && GodotObject.IsInstanceValid(previous))
            previous.PhaseCrossed -= OnPhaseCrossed;
        _subscribed = null;
    }

    /// <summary>The interface's light dies down on the same exactly-once crossing every other
    /// day/night consumer uses — never its own clock, so the page and the world can never
    /// disagree about what time it is.</summary>
    private void OnPhaseCrossed(PhaseEventKind kind, int cyclesElapsedAfter)
    {
        switch (kind)
        {
            case PhaseEventKind.DayToDusk:
                SetTemperature(UiTemperature.Night, animate: true);
                break;
            case PhaseEventKind.NightToDawn:
                SetTemperature(UiTemperature.Day, animate: true);
                break;
        }
    }

    /// <summary>Move to a temperature. Animated, this is the one long motion in the system —
    /// the page visibly losing its light over ~2s — and it is choreography, not decoration.</summary>
    public void SetTemperature(UiTemperature temperature, bool animate = true)
    {
        UiTokens target = UiTokens.For(temperature);
        _crossfade?.Kill();

        if (!animate)
        {
            ApplyTokens(target);
            return;
        }

        UiTokens from = _tokens;
        _crossfade = CreateTween();
        _crossfade.SetProcessMode(Tween.TweenProcessMode.Idle);

        for (int step = 1; step <= CrossfadeSteps; step++)
        {
            float k = (float)step / CrossfadeSteps;
            _crossfade
                .TweenCallback(Callable.From(() => ApplyTokens(UiTokens.Lerp(from, target, k))))
                .SetDelay(UiScale.MotionTemperature / CrossfadeSteps);
        }
    }

    /// <summary><b>The one-line exploration, applied.</b> Call after editing
    /// <see cref="UiRecipeSet.Current"/> or a token value and the whole interface re-styles —
    /// every button, card, field, chip, HUD scrap and flow screen, at the current temperature,
    /// in all five states.</summary>
    public void Rebuild() => ApplyTokens(_tokens);

    private void ApplyTokens(UiTokens tokens)
    {
        _tokens = tokens;
        _theme = UiThemeFactory.Build(tokens, UiRecipeSet.Current);

        if (IsInsideTree())
        {
            GetTree().Root.Theme = _theme;
            ApplyToAdopted();
        }

        TokensChanged?.Invoke(tokens);
    }

    // --- reaching every Control -------------------------------------------------------------

    /// <summary>
    /// <b>The severed branches.</b> Godot propagates a theme only into a node's <c>CanvasItem</c>
    /// and <c>Window</c> children, and a Control inherits on parenting only from an immediate
    /// Control-or-Window parent. Any other node in the chain — a plain <c>Node</c>, and
    /// critically <b>every CanvasLayer</b> — cuts the branch off: the Controls under it resolve
    /// nothing from the tree and fall through to the project's committed
    /// <c>resources/UITheme.tres</c> instead.
    ///
    /// <para>This codebase is built almost entirely out of those two cases — <c>Boot</c> is a
    /// Node, and <see cref="UiLayers"/> is a ladder of CanvasLayers — so hanging the theme on the
    /// root window alone reached the root window and nothing else. Measured, not reasoned: a
    /// Label parented to the root resolved the live Day ink, and the same Label under a Node or a
    /// CanvasLayer resolved the .tres's Night ink, at both temperatures.</para>
    ///
    /// <para>It shipped invisible because the committed .tres was exported at Night from this
    /// same factory: at night the wrong answer and the right answer are the same colour. Day was
    /// the first temperature to disagree, and it had never been rendered.</para>
    ///
    /// <para>So the service adopts each severed branch's head instead. A Control or Window whose
    /// parent is neither a Control nor a Window is a theme root; it gets the theme assigned
    /// directly, and Godot's own propagation covers everything beneath it.</para>
    /// </summary>
    private readonly List<Node> _adopted = new();

    /// <summary>A node Godot will not hand the theme to, so this service must.</summary>
    private static bool NeedsAdoption(Node node) =>
        node is Control or Window && node.GetParent() is not (Control or Window);

    private void OnNodeAdded(Node node)
    {
        if (NeedsAdoption(node))
            Adopt(node);
    }

    /// <summary>Existing tree at boot — everything the <c>NodeAdded</c> hook was armed too late
    /// to see. Recursion stops descending as soon as it adopts, because Godot covers the rest.</summary>
    private void SweepForAdoption(Node node)
    {
        if (NeedsAdoption(node))
        {
            Adopt(node);
            return;
        }
        foreach (Node child in node.GetChildren())
            SweepForAdoption(child);
    }

    private void Adopt(Node node)
    {
        // A node carrying a theme of its own is a bespoke styling system, not a branch to fix.
        // Overwriting it would silently delete someone's deliberate scene work, so it is
        // reported instead — UiNoBespokeStylingTests is the place that fails on it.
        Theme? existing = node switch
        {
            Control c => c.Theme,
            Window w => w.Theme,
            _ => null,
        };
        if (existing != null && existing != _theme)
        {
            GD.PushWarning($"[ui] {node.Name} ({node.GetType().Name}) carries its own Theme and was " +
                           "left alone; it will not follow the temperature.");
            return;
        }

        if (!_adopted.Contains(node))
            _adopted.Add(node);
        Assign(node);
    }

    private void ApplyToAdopted()
    {
        for (int i = _adopted.Count - 1; i >= 0; i--)
        {
            if (!IsInstanceValid(_adopted[i]))
                _adopted.RemoveAt(i);
            else
                Assign(_adopted[i]);
        }
    }

    private void Assign(Node node)
    {
        switch (node)
        {
            case Control control:
                control.Theme = _theme;
                break;
            case Window window:
                window.Theme = _theme;
                break;
        }
    }

    /// <summary>The theme as currently built — for the exporter and for tests. Never null once
    /// the service has readied.</summary>
    public Theme? CurrentTheme => _theme;

    /// <summary>
    /// <b>Follow the temperature.</b> Runs <paramref name="apply"/> once now and again on every
    /// palette change, unsubscribing when <paramref name="node"/> leaves the tree.
    ///
    /// <para>Controls that take their look from the theme need none of this — they re-read it
    /// themselves. This is for the ones that cache a stylebox or draw their own pixels: the HUD
    /// scraps, the minimap. Without it a widget built at noon would still
    /// be lit at noon after nightfall, which is the exact class of bug that makes a
    /// "two-temperature" system quietly become a one-temperature system.</para>
    ///
    /// <para>A node removed and re-added loses its binding; bind from <c>_Ready</c>, which is
    /// where every current caller does it.</para>
    /// </summary>
    public static void Bind(Node node, Action<UiTokens> apply)
    {
        void Handler(UiTokens tokens)
        {
            if (GodotObject.IsInstanceValid(node))
                apply(tokens);
        }

        apply(_tokens);
        TokensChanged += Handler;
        node.TreeExiting += () => TokensChanged -= Handler;
    }

    /// <summary>The common case of <see cref="Bind"/>: a control that paints its own pixels from
    /// the tokens and only needs telling that they moved.</summary>
    public static void BindRedraw(CanvasItem item) => Bind(item, _ => item.QueueRedraw());

    /// <summary>
    /// Binds a full-screen dim declared in a <c>.tscn</c> to <b>the one scrim</b>.
    ///
    /// <para>Six of these were hiding in scene files — a black at 0.55 under the pause menu and a
    /// near-black at 0.85 under five dialogs — which the C#-only sweep could not see and the
    /// source guard was not scanning. Their <c>color</c> lines are gone from the scenes now, so
    /// this call is the only thing that gives them a colour: a scrim that loses its binding turns
    /// white and screams, rather than quietly reverting to a palette we retired.</para>
    /// </summary>
    public static void BindScrim(Node owner, string path = "Scrim")
    {
        if (owner.GetNodeOrNull<ColorRect>(path) is not { } rect)
        {
            GD.PushWarning($"[ui] {owner.Name}: no scrim ColorRect at '{path}' to bind.");
            return;
        }
        Bind(rect, tokens => rect.Color = tokens.Scrim);
    }

    /// <summary>Install the service under a root, if it is not already there. Called from boot;
    /// idempotent, so a scene that wants to be sure can call it too.</summary>
    public static UiThemeService Install(Node root)
    {
        if (Instance is { } existing && GodotObject.IsInstanceValid(existing))
            return existing;
        var service = new UiThemeService();
        root.AddChild(service);
        return service;
    }
}
