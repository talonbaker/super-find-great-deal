using Godot;
using MpFoundation.Ui.Design;

namespace MpFoundation.Ui;

/// <summary>
/// The flow screens' component kit — Talon's rescope, verbatim: "If you're going to make a
/// button make a button that can be reused and this will be reused over and over." Every flow
/// screen (RoundIntroCard, RoundEndTallyPanel, UpgradeLobbyPanel, LossScreen, QuotaStripWidget,
/// the connecting gate and the nightfall treatment) is composed from these factories and ONLY
/// these — a screen with bespoke controls fails review.
///
/// <para><b>B2 has now landed, and this file got smaller.</b> B1 shipped the kit with its own
/// token block and its own hand-written five-state table, deliberately, so that PAPER &amp;
/// FIRELIGHT would be a token pass rather than a rebuild. It was: the tokens moved to
/// <see cref="UiTokens"/>, the state table moved to <see cref="UiStyle"/>, the ember styling
/// that had to ship as per-button code overrides (because the old theme carried no accent
/// variation and its generator could not safely be run) is now simply the
/// <c>PrimaryAction</c> variation in the generated theme. <b>Not one screen changed.</b> That is
/// the seam working exactly as designed.</para>
///
/// <para><b>What is left here</b> is composition, not styling: which node types a screen is
/// allowed to build, and the scaffold they sit in. The look arrives from the theme, so these
/// components follow a token edit and a temperature crossing without knowing either happened.</para>
///
/// <para>Every factory stamps its product with <see cref="ComponentMeta"/> so the in-engine
/// self-test can prove reuse across screens by walking trees rather than by trusting this
/// comment.</para>
/// </summary>
public static class UiKit
{
    /// <summary>Meta key naming which kit component built a node.</summary>
    public const string ComponentMeta = "uikit_component";

    // --- the scrims a full-screen surface may use -------------------------------------------

    /// <summary><b>The one scrim</b>, live at the current temperature. State screens dim with
    /// this and nothing else.</summary>
    public static Color StateScrim => UiThemeService.Tokens.Scrim;

    /// <summary>The loss screen's ground. <b>Not a scrim</b> — the run is over, so the frame is
    /// replaced rather than dimmed, and a replaced frame is a ground at full opacity. This is
    /// the one hard cut in the system and the only surface allowed to make it.</summary>
    public static Color LossScrim => new(UiThemeService.Tokens.PageGround, 1f);

    // Spacing on the one scale.
    public const int SpaceS = UiScale.SpaceSnug;
    public const int SpaceM = UiScale.SpaceLoose;
    public const int SpaceL = UiScale.SpaceWide;

    public const int CardMinWidth = UiScale.CardMinWidth;

    // Type roles ride the theme's variations — same source as every menu screen.
    public const string RoleHeading = "Display";
    public const string RoleBody = "Body";
    public const string RoleCaption = "Caption";

    // --- labels ----------------------------------------------------------------------------

    public static Label Heading(string text) => MakeLabel(text, RoleHeading, "Heading");

    public static Label Body(string text) => MakeLabel(text, RoleBody, "Body");

    public static Label Caption(string text) => MakeLabel(text, RoleCaption, "Caption");

    /// <summary>A heading drawn directly on the scrim rather than on a card — the loss line, the
    /// connecting gate. Its own role because ink on a dim is a different token from ink on
    /// paper, and guessing that per screen is how a project ends up with 27 text colours.</summary>
    public static Label HeadingOnScrim(string text) => MakeLabel(text, "DisplayOnScrim", "Heading");

    /// <summary>Body text drawn directly on the scrim.</summary>
    public static Label BodyOnScrim(string text) => MakeLabel(text, "OnScrim", "Body");

    private static Label MakeLabel(string text, string role, string component)
    {
        var label = new Label
        {
            Text = text,
            ThemeTypeVariation = role,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.SetMeta(ComponentMeta, component);
        return label;
    }

    // --- buttons ---------------------------------------------------------------------------

    /// <summary>The primary action — the screen's single ember instance. All five states,
    /// including a focus state louder than hover, arrive from the theme's PrimaryAction
    /// variation; there is nothing to override here any more.</summary>
    public static Button PrimaryButton(string text)
    {
        var button = new Button { Text = text, ThemeTypeVariation = "PrimaryAction" };
        button.SetMeta(ComponentMeta, "PrimaryButton");
        return button;
    }

    /// <summary>The quiet action.</summary>
    public static Button SecondaryButton(string text)
    {
        var button = new Button { Text = text, ThemeTypeVariation = "SecondaryAction" };
        button.SetMeta(ComponentMeta, "SecondaryButton");
        return button;
    }

    /// <summary>The destructive action — leaving, abandoning, quitting a run.</summary>
    public static Button DangerButton(string text)
    {
        var button = new Button { Text = text, ThemeTypeVariation = "DangerAction" };
        button.SetMeta(ComponentMeta, "DangerButton");
        return button;
    }

    // --- the accent -----------------------------------------------------------------------------

    /// <summary><b>The one accent</b>, live. Moonlight = the thing that matters, used once
    /// per screen or it stops meaning anything.</summary>
    public static Color Accent => UiThemeService.Tokens.Accent;

    /// <summary>The ember keyline: a screen's single accent instance where it has no primary
    /// action to carry one. A component rather than a raw colour precisely because the
    /// once-per-screen rule is easy to break by accident and hard to break when the accent has
    /// exactly one shape to arrive in.</summary>
    public static ColorRect Keyline()
    {
        var keyline = new ColorRect
        {
            Color = Accent,
            CustomMinimumSize = new Vector2(64, 2),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UiThemeService.Bind(keyline, tokens => keyline.Color = tokens.Accent);
        keyline.SetMeta(ComponentMeta, "Keyline");
        return keyline;
    }

    // --- panel / card ------------------------------------------------------------------------

    /// <summary>A content card with a flowing column inside. A VBox flow rather than anchored
    /// children because every caller has variable-row content (roster lines, upgrade slots).
    /// The card's paper — fill, edge, corner, lift — is the theme's, so re-cutting the game's
    /// corners re-cuts these.</summary>
    public static PanelContainer Panel(out VBoxContainer content)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(CardMinWidth, 0) };
        panel.SetMeta(ComponentMeta, "Panel");

        content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", SpaceM);
        panel.AddChild(content);
        return panel;
    }

    /// <summary>A small tag: a count, a name, a state word.</summary>
    public static PanelContainer Chip(string text)
    {
        var chip = new PanelContainer { ThemeTypeVariation = "Chip" };
        chip.AddChild(Caption(text));
        chip.SetMeta(ComponentMeta, "Chip");
        return chip;
    }

    // --- the full-screen state scaffold ------------------------------------------------------

    /// <summary>The full-screen state scaffold every flow screen sits in: a CanvasLayer at a
    /// documented ladder slot (see <see cref="UiLayers"/>), a full-rect scrim, and a centred
    /// content column. Starts hidden — visibility is the caller's routed decision, never the
    /// scaffold's.
    ///
    /// <para>The scrim follows the temperature: pass one of the surface accessors above rather
    /// than a colour, and it re-takes the palette at dusk like everything else.</para></summary>
    public static CanvasLayer StateScreenScaffold(string name, int layer, Color scrim, out VBoxContainer column)
    {
        var canvasLayer = new CanvasLayer { Name = name, Layer = layer, Visible = false };

        var scrimRect = new ColorRect { Color = scrim, Name = "Scrim" };
        scrimRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        canvasLayer.AddChild(scrimRect);

        // Bound so the dim is the CURRENT one scrim, not the one that existed when the screen
        // was built — a tally card constructed at noon and shown after dusk would otherwise be
        // dimmed by daylight.
        bool isLoss = scrim.A >= 0.999f;
        UiThemeService.Bind(scrimRect, tokens =>
            scrimRect.Color = isLoss ? new Color(tokens.PageGround, 1f) : tokens.Scrim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scrimRect.AddChild(center);

        column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", SpaceM);
        column.CustomMinimumSize = new Vector2(CardMinWidth, 0);
        center.AddChild(column);

        canvasLayer.SetMeta(ComponentMeta, "StateScreenScaffold");
        return canvasLayer;
    }
}

/// <summary>
/// Retained as the kit's state vocabulary for the tests and self-tests that were written against
/// it. The definitions now live in <see cref="UiStyle"/> and cover every component in the game
/// rather than the kit's buttons alone — these forward, so the B1 proofs keep measuring the real
/// shipped appearance rather than a copy that could drift from it.
/// </summary>
public static class UiKitStates
{
    /// <summary>The five states. An alias of <see cref="UiState"/>.</summary>
    public enum VisualState : byte { Normal = 0, Hover = 1, Pressed = 2, Focus = 3, Disabled = 4 }

    public static readonly VisualState[] All =
    {
        VisualState.Normal, VisualState.Hover, VisualState.Pressed, VisualState.Focus, VisualState.Disabled,
    };

    private static UiState Map(VisualState state) => (UiState)(byte)state;

    private static StyleSpec Spec(VisualState state) =>
        UiStyle.Resolve(UiRecipes.Current.Action, Map(state), UiThemeService.Tokens);

    public static Color PrimaryFill(VisualState state) => Spec(state).Fill;

    public static Color PrimaryBorder(VisualState state) => Spec(state).Border;

    /// <summary>Focus thickens the border: focus must survive squint distance on a TV.</summary>
    public static int PrimaryBorderWidth(VisualState state) => Spec(state).BorderWidth;
}
