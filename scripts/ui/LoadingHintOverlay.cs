using Godot;
using MpFoundation.Game.World;

namespace MpFoundation.Ui;

/// <summary>
/// L11 (Issue #114), design spec §1 step 2: a thin client overlay shown while the world builds
/// and syncs — one hint line over a dark scrim, or, in a world the hint is not true about, the
/// ground alone (see <see cref="ShowHint"/>). (It originally also carried an ASCII camp-map
/// diagram; that was removed as the garbled loading text — see the root-cause note at the
/// field below.) Auto-dismisses on <see cref="CycleDriver.Synced"/>. No ready-up handshake,
/// by design.
///
/// Built in code — following the HUD's and <see cref="PerfHud"/>'s own
/// code-built convention rather than an authored .tscn, since this Story is built in a cloud
/// session with no way to open the Godot editor to lay one out — using a flowing
/// <see cref="VBoxContainer"/> for the map/hint stack rather than manual-Position children
/// (CLAUDE.md's rule against mixing the two; see the field's own comment for why).
///
/// <b>Never-strand contract (the acceptance criterion this Story calls its most likely
/// soft-lock):</b> dismissal is a POLL of <see cref="CycleDriver.Instance"/>.Synced, checked
/// once synchronously in <see cref="_Ready"/> (in case sync already landed before this overlay
/// even existed) and then every frame in <see cref="_Process"/> (in case it lands later). Both
/// call sites route through the same pure <see cref="LoadingOverlayGate"/> decision — see that
/// class's doc for why a poll can never miss the transition the way a one-shot signal
/// subscription could. Directly unit-tested (no scene tree needed) in
/// <c>LoadingOverlayGateTests</c>; this class itself cannot be exercised outside a running Godot
/// engine, which this cloud session does not have — see the PR body's verification notes.
/// </summary>
public partial class LoadingHintOverlay : CanvasLayer
{
    // PLACEHOLDER copy — canon-neutral; the real hint pass needs /direct.
    private const string HintLine = "Wood by day. Light by night.";

    /// <summary>Whether this world has a loop the hint line above is true about.
    ///
    /// <para>LOSS-1 (Talon note 4, 2026-08-29). He was explicit: <i>"there is no 'cache' there is
    /// no 'winter' and there's no 'goal' for this playtest, it's about movement and
    /// exploration."</i> "Wood by day. Light by night." is the same false statement the loss
    /// screen made, delivered earlier and to every player on every single load — the bubble test
    /// has no wood, no night job and nothing to light. So in a world that runs no scored
    /// playthrough the line is not reworded, it is not shown: this overlay is a ground and a
    /// dismissal, and it says nothing rather than something untrue.</para>
    ///
    /// <para><b>Deliberately not replaced with a different line.</b> A true one would have to
    /// describe what the bubble test actually is, and choosing what a loading screen says about a
    /// level is a tone decision that belongs to Talon and to <c>/direct</c>, not to a packet
    /// removing a lie. Nothing is the honest answer until someone owns that call.</para>
    ///
    /// <para><b>The level has a goal now, and this line still does not get to be it.</b> "Collect
    /// all the bubbles" is said once on entry, by the phase-toast layer, the instant this ground
    /// clears (<see cref="Dismissed"/>, <see cref="PhaseToastText.BubbleGoalToast"/>) — so read
    /// the quoted <i>"there's no 'goal'"</i> above as where the level stood on 2026-08-29, not as
    /// a standing claim. What has not changed is this field's actual subject: the bubble test
    /// still has no wood and no night job, so <c>HintLine</c> is still untrue here and still is
    /// not shown. The goal belongs on a surface the player is looking at when the world appears,
    /// not on the one covering it.</para>
    ///
    /// <para>Reads the world directly rather than taking a parameter — unlike
    /// <c>FlowScreens.Attach</c>, this overlay has exactly ONE construction site (Gameplay's
    /// client-UI block), so there is no lab or capture rig that could be handed the wrong answer
    /// by <c>LaunchOptions.DefaultWorld</c>. See that constant's trap note.</para></summary>
    private static bool ShowHint => Sail.Game.Run.WorldRunFlow.Current;

    // The ASCII camp-map diagram that used to sit above the hint was REMOVED here
    // (CORE-PROG-B1, Talon's standing ruling on the garbled loading text). Root cause,
    // measured 2026-08-14: the diagram was ~17 lines of Unicode pseudographics —
    // box-drawing (U+2500s), block/shade elements (U+2588/U+2591), geometric shapes,
    // arrows, an emoji — and NEITHER shipped font (assets/fonts/Sora.woff2,
    // JetBrainsMono.woff2) carries a single one of those codepoints (cmap-probed with
    // fontTools; zero of nine present in either), with no fallback font configured in the
    // theme. Every glyph therefore rendered as a .notdef box: the "character salad" shown
    // between Join and the world. A real map graphic is L2 asset work, not a string.

    /// <summary><b>Raised exactly once, the instant this ground begins to clear</b> — which is
    /// the moment the local player actually enters the level, and a different moment from
    /// <c>Gameplay._Ready</c>. A host is its own authority and syncs on the first frame; a joining
    /// client waits for the server's first authoritative update, so a line raised at _Ready would
    /// spend some or all of its life behind an opaque ground (this layer is
    /// <c>UiLayers.LoadingOverlay</c> = 100; the phase toasts are 60) and on a slow join the
    /// player would never see it at all. Fired BEFORE the quarter-second fade starts, so a line
    /// raised from here comes up WITH the world rather than after a beat of dead screen.
    ///
    /// <para><b>Subscribe before <c>AddChild</c>.</b> <see cref="_Ready"/> polls and can dismiss
    /// synchronously on the very first frame — the exact race <see cref="LoadingOverlayGate"/>
    /// exists for — so a subscription made after this node is in the tree can miss it outright.
    /// A plain C# event, not a Godot signal, for the same reason the rest of this overlay is
    /// code-built: nothing here needs to cross into GDScript.</para></summary>
    public event System.Action? Dismissed;

    private ColorRect _scrim = null!;
    private bool _dismissed;

    public override void _Ready()
    {
        Layer = Design.UiLayers.LoadingOverlay; // must cover the whole screen while it is up.

        // Fully opaque, and therefore a GROUND rather than a scrim: nothing shows through a
        // loading screen, so it takes the page token, not the dim.
        _scrim = new ColorRect { Color = new Color(Design.UiThemeService.Tokens.PageGround, 1f) };
        _scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_scrim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _scrim.AddChild(center);

        // A flowing VBoxContainer, not a plain Control with manual Position: the map label's
        // rendered height depends on font metrics this cloud session cannot measure (no Godot
        // engine available — see class doc), so the hint line below it must stack after it, not
        // sit at a guessed fixed offset that risks overlapping. CLAUDE.md's "plain Control for
        // coded-position UI" rule is about NOT mixing manual Position children into a Container
        // (the container silently overrides them); it is not a ban on Container subclasses for
        // naturally sequential content — PauseOverlay's own code-built dialog uses the same
        // VBoxContainer flow for exactly this reason.
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
        column.AddThemeConstantOverride("separation", Design.UiScale.SpaceWide);
        center.AddChild(column);

        if (ShowHint)
        {
            var hint = new Label
            {
                Text = HintLine,
                HorizontalAlignment = HorizontalAlignment.Center,
                ThemeTypeVariation = "Body",
            };
            hint.ThemeTypeVariation = "Title";
            // The loading screen's one accent instance. It was the ember - "the one warm thing on
            // it" - until UI-3 (2026-08-29) took warm hues off the interface entirely on Talon's
            // note 1; warm light now lives only in the world. The hint is still the single
            // accented element here, which is the part of that comment that survived.
            hint.AddThemeColorOverride("font_color", Design.UiThemeService.Tokens.AccentHi);
            column.AddChild(hint);
        }

        // The race the acceptance criteria names explicitly: CycleDriver.Synced may already be
        // true by the time this overlay's own _Ready runs (a fast local server, or this overlay
        // simply being added to the tree after CycleDriver — see Gameplay's AddChild order).
        // Check synchronously here, not just in _Process, so that case dismisses on the very
        // first frame rather than one frame of a visible-then-gone flash.
        PollAndMaybeDismiss();
    }

    public override void _Process(double delta) => PollAndMaybeDismiss();

    private void PollAndMaybeDismiss()
    {
        if (_dismissed)
            return;
        bool synced = CycleDriver.Instance?.Synced ?? false;
        if (LoadingOverlayGate.ShouldBeVisible(synced))
            return;
        _dismissed = true;
        Dismissed?.Invoke();
        Dismiss();
    }

    private void Dismiss()
    {
        Tween tween = CreateTween();
        tween.TweenProperty(_scrim, "modulate:a", 0f, 0.25).SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
