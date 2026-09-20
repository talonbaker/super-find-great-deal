using System;
using System.Collections.Generic;
using Godot;
using MpFoundation.Ui.Design;

namespace MpFoundation.Ui.Menu;

/// <summary>
/// <b>The splash screen and the main menu — two layouts, one scene.</b> A night world with real
/// depth, one glowing bubble right of centre, a
/// star field, an off-frame campfire low-left, blurred colour blocks wearing lit rims, and a
/// completely unblurred column of type sitting in front of all of it.
///
/// <para><b>2026-08-30, Talon's playtest note 17 — the two tiers are now two SCREENS.</b> His
/// words: <i>"right when you hit start you go to what looks like just the same menu with an
/// additional two options which is really stupid… take away the settings and quit and replace this
/// with 'press start'. The studio mark is different than the splash screen and the splash screen
/// should be different than the main menu."</i> He is right, and the defect was structural rather
/// than cosmetic: pressing start changed the CONTENTS of one row and nothing else, so the
/// transition read as a no-op. The splash is now the wordmark and one breathing prompt — SETTINGS
/// and QUIT came off it, and they are one press away on the main menu, which no longer shares a
/// single line of layout with it (see <see cref="ApplyTier"/>). The studio mark
/// (<see cref="Splash"/>) was the one part of the flow he said he liked, and is untouched.</para>
///
/// <para>Built to <c>docs/agents/handoffs/2026-08-28-menu-previz/menu-night-1.jpg</c>. It was built
/// to the LIGHT previz first (MENU-1, 2026-08-28) and re-skinned to this one the next day, after
/// Talon reversed the direction — "clinical and sterile"; target "dark, moody, campfire-at-night" —
/// and after his 2026-08-29 note removing the orange from the primary button.
/// <see cref="MenuLook"/> holds every number; <see cref="MenuBackdrop"/> builds and composites the
/// picture; this class assembles the screen and owns the navigation. <b>Nothing in this file
/// changed for the reversal, which was the point of putting every number in one place.</b></para>
///
/// <para><b>The old campfire register is gone, not disabled — and this screen going dark again did
/// not bring it back.</b> The night woods, the plank lettering and the burned boards were an answer
/// to a different brief and were deleted rather than left switched off, because a dark backdrop
/// nothing points at is exactly the kind of thing that gets found again in six months and mistaken
/// for the design. What MENU-2 restored is the night's VALUE, not its furniture: same blocks, same
/// bubble, same type, a darker palette. The file names stay (<c>MainMenu</c>,
/// <c>scenes/ui/MainMenu.tscn</c>): renaming them touches the boot path and every scene-path
/// constant, which is a separate and larger change.</para>
///
/// <para><b>The type column, and why it was wrong before.</b> Talon: "The black text against that
/// background is terrible ... I'm struggling to find the words exactly why." It was never contrast
/// — the old ink measured 8.06:1 on the sand and 6.18:1 on the warm block and still looked wrong.
/// Three real causes: an ink at full opacity that outranked the bubble that is supposed to be the
/// subject; razor-sharp glyphs over a backdrop blurred to zero hard edges, with no optical
/// relationship to it, reading as pasted on; and a field that CHANGED underneath the word, so the
/// wordmark's own legibility varied across its own length. The fixes are, in order: an ink drawn
/// from the sky's own family (bone at night, navy-slate on the light frame), the scrim
/// <see cref="MenuBackdrop"/> lays under the column, and a wordmark reduced from 96 px with
/// tracking buying the presence back. <b>All three fixes survived the direction reversal
/// unchanged</b>, which is what says they were diagnoses rather than decoration.</para>
///
/// <para><b>Layout is in a 1600 x 900 design space, scaled by height.</b> Every number in
/// <see cref="MenuLook"/> is a previz pixel, and the UI layer carries one scale factor, so the
/// screen is proportionally the approved frame at any resolution rather than correct at one and
/// cramped at the rest.</para>
///
/// <para><b>Reduced motion stops the whole picture at once.</b> Everything that moves — the bubbles'
/// film, the camera's few centimetres of drift, the underline's travelling colour, the grain — runs
/// off one phase number. Honouring the preference is a matter of not advancing it, and the screen
/// still reads completely: the resting frame IS the approved frame.</para>
/// </summary>
public partial class MainMenu : Node3D
{
    /// <summary>Set once the player has passed the title tier, so returning from Host / Join /
    /// Settings lands back on the options rather than making them press play again. Static because
    /// the scene is torn down and rebuilt on every transition.</summary>
    private static bool _pastTitle;

    /// <summary>Suppresses the modal panels (the playtest foreword, HOW TO PLAY) for a capture run.
    ///
    /// <para>Not a convenience. The foreword panel is a full-screen modal that a fresh profile gets
    /// on first launch, and a capture lab runs on whatever profile the machine happens to have — so
    /// without this the acceptance evidence for a look packet is a photograph of a text panel with
    /// the screen behind it. Static, because the scene is rebuilt between shots.</para></summary>
    private static bool _suppressPanels;

    /// <summary>Forces the reduced-motion reading for a capture run, so the accessible state can be
    /// photographed without writing the machine's own preference file. Null means "read the
    /// preference", which is what a player ever gets.</summary>
    private static bool? _forcedReducedMotion;

    private MenuBackdrop _backdrop = null!;
    private CanvasLayer _ui = null!;
    private Control _root = null!;
    private Label _wordmark = null!;
    private WordmarkRule _rule = null!;
    private MenuActionButton? _primary;
    private Label? _pressStart;
    private Control? _actions;
    private readonly List<MenuTextLink> _links = new();

    private bool _optionsTier;
    private bool _reducedMotion;
    private bool _frozen;
    private float _phase = MenuLook.RestPhase;

    public override void _Ready()
    {
        // The title screen is where a player lands first, so it cannot assume the HUD has already
        // read the preference file. Load is a disk read with defaults on failure, and idempotent.
        Hud.HudSettings.Load();
        _reducedMotion = _forcedReducedMotion ?? Hud.HudSettings.ReducedMotion;

        _backdrop = new MenuBackdrop { Name = "Backdrop", ReducedMotion = _reducedMotion };
        AddChild(_backdrop);

        BuildUi();

        if (_pastTitle)
            ShowOptions();
        else if (!_suppressPanels && !OnboardingSettings.PlaytestForewordDismissed)
            ShowPanel(ScenePaths.PlaytestForewordPanel);

        ApplyPhase();
    }

    public override void _Process(double delta)
    {
        if (_reducedMotion || _frozen)
            return;
        _phase += MenuLook.PhaseRate * (float)delta;
        ApplyPhase();
    }

    /// <summary>The one number the whole screen moves on, pushed to everything that reads it. The
    /// bubbles and the underline are handed the SAME value in the same frame, which is what makes
    /// Talon's "make that move along with the bubble" true rather than approximately true.</summary>
    private void ApplyPhase()
    {
        _backdrop.SetPhase(_phase);
        _rule.SetPhase(_phase);
        if (_pressStart != null)
        {
            // The prompt breathes off the SAME number as the bubbles and the rule, which is what
            // makes "reduced motion stops the whole picture at once" true of it too: honouring the
            // preference is still a matter of not advancing the phase, and the resting frame still
            // says PRESS START at full strength rather than at the bottom of a fade.
            float breath = _reducedMotion
                ? 1f
                : Mathf.Lerp(MenuLook.PressStartRestAlpha, 1f,
                    0.5f + 0.5f * Mathf.Sin(_phase * MenuLook.PressStartBreathRate));
            _pressStart.Modulate = new Color(1, 1, 1, breath);
        }
    }

    // ==============================================================================================
    // The UI layer — never blurred, never tinted
    // ==============================================================================================

    private void BuildUi()
    {
        _ui = new CanvasLayer { Name = "Ui", Layer = 1 };
        AddChild(_ui);

        _root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.Size = new Vector2(MenuLook.DesignWidth, MenuLook.DesignHeight);
        _ui.AddChild(_root);
        ApplyDesignScale();
        GetViewport().SizeChanged += ApplyDesignScale;

        BuildWordmark();
        BuildCaption();
    }

    /// <summary>
    /// <b>The splash's one input, and it is not a button.</b> Talon wrote note 17's affordance as
    /// the words "press start", so anything a player might reasonably do to start the game has to
    /// work: a key, a click anywhere, a controller face button.
    ///
    /// <para>Read here rather than through an input action on purpose — <b>no <c>ui_*</c> action is
    /// written into project.godot</b> (the standing rule), and inventing a game action for "any
    /// button" would be a binding nobody could ever usefully rebind. Only live on the splash tier;
    /// on the main menu the controls own their own input and a stray key must not navigate.</para>
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_optionsTier)
            return;
        if (@event is InputEventKey { Pressed: true, Echo: false }
            or InputEventMouseButton { Pressed: true }
            or InputEventJoypadButton { Pressed: true })
        {
            GetViewport().SetInputAsHandled();
            ShowOptions();
        }
    }

    /// <summary>Maps the 1600 x 900 design space onto whatever the window actually is. Scaled on
    /// HEIGHT, so a wider-than-16:9 display gets more picture rather than a stretched title.</summary>
    private void ApplyDesignScale()
    {
        Vector2 view = GetViewport().GetVisibleRect().Size;
        float scale = view.Y / MenuLook.DesignHeight;
        _ui.Scale = new Vector2(scale, scale);
    }

    private void BuildWordmark()
    {
        (FontVariation face, int size) = MenuType.Wordmark(Branding.Wordmark);

        _wordmark = new Label
        {
            Name = "Wordmark",
            Text = Branding.Wordmark,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings
            {
                Font = face,
                FontSize = size,
                FontColor = MenuLook.Ink,
            },
        };
        _root.AddChild(_wordmark);

        _rule = new WordmarkRule { Name = "Rule" };
        _root.AddChild(_rule);

        _wordmarkFace = face;
        _wordmarkSize = size;
        ApplyTier();
    }

    private FontVariation _wordmarkFace = null!;
    private int _wordmarkSize;

    /// <summary>
    /// <b>Lays out whichever of the two screens this is.</b> Note 17's whole point: the splash and
    /// the main menu do not share a layout, so pressing start visibly goes somewhere.
    ///
    /// <para><b>Splash</b> — the wordmark is the subject at full size on the axis, the rule under
    /// it, one breathing prompt below that, and nothing else. <b>Main menu</b> — the wordmark drops
    /// to about half size and climbs to the top of the frame as a header, its rule shortens with
    /// it, and the destinations become the subject as a vertical column. A player looking at two
    /// stills can tell which is which without reading a word of either.</para>
    /// </summary>
    private void ApplyTier()
    {
        bool menu = _optionsTier;

        _wordmark.LabelSettings!.FontSize = menu
            ? Mathf.Max(16, Mathf.RoundToInt(_wordmarkSize * MenuLook.MenuWordmarkScale))
            : _wordmarkSize;
        _wordmark.LabelSettings.Font = _wordmarkFace;
        _wordmark.Position = new Vector2(
            MenuLook.AxisX, menu ? MenuLook.MenuWordmarkTop : MenuLook.WordmarkTop);

        _rule.Position = new Vector2(
            MenuLook.AxisX, menu ? MenuLook.MenuRuleTop : MenuLook.RuleTop);
        _rule.Size = new Vector2(
            menu ? MenuLook.MenuRuleWidth : MenuLook.RuleWidth, MenuLook.RuleHeight);

        RebuildActions();
        if (_caption != null)
            PositionCaption();
    }

    /// <summary>Builds the tier's action area from scratch — a prompt on the splash, a column of
    /// destinations on the main menu. Rebuilt rather than reconfigured because the two are not the
    /// same object: one is a Label, the other is a container of five controls.</summary>
    private void RebuildActions()
    {
        foreach (MenuTextLink link in _links)
            link.QueueFree();
        _links.Clear();
        _primary = null;
        _pressStart = null;
        if (_actions != null)
        {
            _actions.QueueFree();
            _actions = null;
        }

        if (_optionsTier)
            BuildMenuColumn();
        else
            BuildPressStart();
    }

    /// <summary>The splash: one prompt, no controls. Note 17 took SETTINGS and QUIT off this screen
    /// — both are one press away on the main menu, and leaving them here is what made the two
    /// screens read as one.</summary>
    private void BuildPressStart()
    {
        _pressStart = new Label
        {
            Name = "PressStart",
            Text = "PRESS START",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(MenuLook.AxisX, MenuLook.CtaTop),
            LabelSettings = new LabelSettings
            {
                Font = MenuType.Face(UiScale.WeightBody, MenuLook.PressStartTracking),
                FontSize = MenuLook.PressStartSize,
                FontColor = MenuLook.Ink,
            },
        };
        _root.AddChild(_pressStart);
        _actions = _pressStart;
        ApplyPhase(); // so the prompt is at its real alpha on the first frame, not at 1 then popping
    }

    /// <summary>
    /// The main menu: HOST as the primary action, then every other destination under it.
    ///
    /// <para><b>A column, not the old row.</b> Every destination the old screen reached is still
    /// here and nothing was added — the difference a player sees is the arrangement, which is
    /// exactly what note 17 asked for, and a five-item vertical list is also the shape a controller
    /// stick expects.</para>
    /// </summary>
    private void BuildMenuColumn()
    {
        var column = new VBoxContainer
        {
            Name = "Actions",
            MouseFilter = Control.MouseFilterEnum.Pass,
            Alignment = BoxContainer.AlignmentMode.Begin,
            Position = new Vector2(MenuLook.AxisX, MenuLook.MenuListTop),
            Size = new Vector2(MenuLook.DesignWidth * 0.4f, 0f),
        };
        column.AddThemeConstantOverride("separation", MenuLook.MenuListSeparation);
        _root.AddChild(column);
        _actions = column;

        _primary = new MenuActionButton("HOST")
        {
            Name = "Primary",
            ReducedMotion = _reducedMotion,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        };
        _primary.Pressed += () => Go(ScenePaths.HostMenu);
        column.AddChild(_primary);

        (string Label, Action Action)[] entries =
        {
            ("JOIN", () => Go(ScenePaths.JoinMenu)),
            ("HOW TO PLAY", () => ShowPanel(ScenePaths.HowToPlayPanel)),
            ("SETTINGS", () => Go(ScenePaths.SettingsMenu)),
            ("QUIT", Quit),
        };
        foreach ((string label, Action action) in entries)
        {
            var link = new MenuTextLink(label)
            {
                Name = label.Replace(" ", ""),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            };
            link.Pressed += action;
            column.AddChild(link);
            _links.Add(link);
        }

        WireFocusNeighbours();
        _primary.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Explicit neighbours down the column.
    ///
    /// <para>Godot's default focus stepping follows tree order, which happens to be correct here —
    /// but "happens to be correct" is how a controller-first screen ends up broken by an unrelated
    /// layout change. Stating the ring makes stick navigation a property of this file. It wraps, so
    /// a player holding down never falls off the end into nothing.</para>
    ///
    /// <para><b>The axis flipped with the layout</b> (note 17): the destinations are a column now,
    /// so up/down is the real ring and left/right is what holds. Wiring the old horizontal ring
    /// under a vertical list is precisely the "unrelated layout change" this method exists to stop
    /// being silent.</para></summary>
    private void WireFocusNeighbours()
    {
        var chain = new List<Control>();
        if (_primary != null)
            chain.Add(_primary);
        chain.AddRange(_links);
        if (chain.Count < 2)
            return;

        for (int i = 0; i < chain.Count; i++)
        {
            Control up = chain[(i - 1 + chain.Count) % chain.Count];
            Control down = chain[(i + 1) % chain.Count];
            chain[i].FocusNeighborTop = up.GetPath();
            chain[i].FocusNeighborBottom = down.GetPath();
            chain[i].FocusPrevious = up.GetPath();
            chain[i].FocusNext = down.GetPath();
            // Left and right have nowhere else to go on a single-column screen, so they hold rather
            // than dropping focus into nothing — losing the cursor is worse than a stick that does
            // not move it.
            chain[i].FocusNeighborLeft = chain[i].GetPath();
            chain[i].FocusNeighborRight = chain[i].GetPath();
        }
    }

    private void BuildCaption()
    {
        var caption = new Label
        {
            Name = "Caption",
            // W7-7, 2026-08-30: this used to read the SYSTEM CLOCK, not the build. The same
            // binary would say "BUILD 2026-08-30" today and "BUILD 2026-10-01" in October, so a
            // tester quoting it named the day they played rather than the build they played — and
            // the number it showed disagreed with the exe's own file version by six weeks. It now
            // reads BuildInfo.Version, which is also what telemetry reports as
            // TelemetryConfig.BuildVersion and what the export scripts stamp into the Windows
            // version quad and the macOS Info.plist. One string, four places, no drift.
            Text = $"BUILD {BuildInfo.Version}  ·  PLAYTEST",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings
            {
                Font = MenuType.Face(UiScale.WeightBody, MenuLook.SecondaryTracking),
                FontSize = MenuLook.CaptionSize,
                // Full-strength ink at full opacity, and subordinate by SIZE and WEIGHT rather than
                // by being faded. A build stamp on a playtest build is information, not decoration —
                // the light previz's faded grey measured 1.97:1 on this field and the night previz's
                // measured 2.50:1 on the night one. Same verdict either way; see MenuLook.InkStrong.
                FontColor = MenuLook.InkStrong,
            },
        };
        _caption = caption;
        _root.AddChild(caption);
        PositionCaption();
    }

    private Label _caption = null!;

    /// <summary>The build stamp moves with the tier: under the CTA on the splash (the approved
    /// frame), at the foot of the frame on the main menu, whose destination column now runs
    /// through the band the stamp used to own.</summary>
    private void PositionCaption() =>
        _caption.Position = new Vector2(
            MenuLook.AxisX, _optionsTier ? MenuLook.MenuCaptionTop : MenuLook.CaptionTop);

    // ==============================================================================================
    // Tiers and navigation
    // ==============================================================================================

    /// <summary>Leaves the splash for the main menu. The one transition on this screen, and after
    /// note 17 it is a visible one: <see cref="ApplyTier"/> relays the whole frame rather than
    /// appending two links to a row.</summary>
    private void ShowOptions()
    {
        bool wasTitle = !_optionsTier;
        _optionsTier = true;
        _pastTitle = true;
        ApplyTier();

        // THE FIRST-RUN USAGE NOTICE (Talon, 2026-08-30 note 15): "after the user starts the game
        // for the first time and presses the start button". PLAY is that button and this is the
        // moment it is pressed — not boot, which is before the splash, and not the foreword's slot
        // in _Ready, which is before the player has done anything at all.
        //
        // Gated on wasTitle so the tier transition raises it once rather than on every re-entry
        // (returning from Host/Join/Settings lands here with _pastTitle already set); on the same
        // _suppressPanels flag the foreword uses, so no capture run photographs it; and on
        // TelemetryConfig.IsConfigured, because a notice announcing reporting that cannot be sent
        // would be its own false statement to the player.
        // The predicate itself lives in UsageNoticeGate so all three launches ("fresh", "second",
        // "after don't show again") are provable without a window; the log line is what a headed
        // run is then gated on.
        bool show = UsageNoticeGate.ShouldShow(
            wasTitle, _suppressPanels, OnboardingSettings.UsageNoticeSeen,
            Telemetry.TelemetryConfig.IsConfigured);
        if (wasTitle)
        {
            GD.Print($"[usage-notice] start pressed: show={show} "
                     + $"(seen={OnboardingSettings.UsageNoticeSeen} "
                     + $"suppressed={_suppressPanels} "
                     + $"configured={Telemetry.TelemetryConfig.IsConfigured})");
        }
        if (show)
            ShowPanel(ScenePaths.UsageNoticePanel);
    }

    private void Go(string scenePath) => GetTree().ChangeSceneToFile(scenePath);

    private static void Quit() => Telemetry.Telemetry.Instance.RequestQuit();

    /// <summary>Puts a modal panel up over the menu — the playtest foreword on a fresh profile, and
    /// HOW TO PLAY on demand.
    ///
    /// <para>Both panels are <see cref="CanvasLayer"/>-rooted (each brings its own scrim and its own
    /// Layer 11), NOT Controls. Instantiating them as <c>Control</c> threw an InvalidCastException
    /// straight out of <c>_Ready</c>, and because Godot's C# bridge LOGS an exception rather than
    /// halting, the only symptom was that neither panel ever appeared.</para></summary>
    private void ShowPanel(string scenePath)
    {
        var panel = GD.Load<PackedScene>(scenePath).Instantiate<CanvasLayer>();
        if (panel.HasSignal("Closed"))
            panel.Connect("Closed", Callable.From(panel.QueueFree));
        AddChild(panel);
    }

    // ==============================================================================================
    // Capture hooks (dev only)
    // ==============================================================================================

    /// <summary>Resets the cross-scene "past the title" flag, so a capture lab rebuilding the menu
    /// always starts on the title tier instead of inheriting the last shot's state.</summary>
    public static void CaptureResetTier() => _pastTitle = false;

    /// <summary>Keeps the modal panels off a capture run. See <c>_suppressPanels</c>.</summary>
    public static void CaptureSuppressPanels(bool suppress) => _suppressPanels = suppress;

    /// <summary>Photographs the reduced-motion reading without touching the machine's preference
    /// file. Null restores "read the preference".</summary>
    public static void CaptureForceReducedMotion(bool? reduced) => _forcedReducedMotion = reduced;

    public void CaptureShowOptions() => ShowOptions();

    public void CaptureFocus(int index)
    {
        if (index < 0)
            _primary?.GrabFocus();
        else if (index < _links.Count)
            _links[index].GrabFocus();
    }

    /// <summary>Pins the screen's one moving number. Every capture uses this: a frame shot off a
    /// free-running clock is not reproducible, and two captures taken to be compared have to be at
    /// the same phase or the comparison is between two different pictures.</summary>
    public void CaptureFreezeAt(float phase)
    {
        _frozen = true;
        _phase = phase;
        ApplyPhase();
    }

    /// <summary>Forces the primary action into one state for the state sheet. A button with one
    /// state is not a built button, and hover/pressed/disabled cannot be reached reliably by
    /// synthesising input into a headed capture.</summary>
    public void CaptureButtonState(UiState? state)
    {
        // No-op on the splash: after note 17 that screen has no button at all, and a lab that asks
        // for a button state there is asking about a control that is not on the screen. Silently
        // doing nothing beats a NullReferenceException out of a capture rig, and the shot itself
        // will show a press-start screen, which is the honest answer.
        if (_primary == null)
            return;
        _primary.ForcedState = state;
        if (state == UiState.Disabled)
            _primary.Disabled = true;
        _primary.QueueRedraw();
    }

    /// <summary>The primary action, or null on the splash tier — which has none (note 17).</summary>
    public MenuActionButton? CapturePrimary => _primary;
}
