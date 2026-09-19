using Godot;

namespace MpFoundation.Ui.Design;

/// <summary>The typefaces. Paths are tokens like everything else: when the chunky cutout-letter
/// display face the direction brief calls for actually lands as an asset, this is the one line
/// that changes and every rank-1 heading in the game re-sets itself.</summary>
public static class UiTypeface
{
    public const string SansPath = "res://assets/fonts/Sora.woff2";
    public const string MonoPath = "res://assets/fonts/JetBrainsMono.woff2";

    /// <summary>Rank 1 only. Stands in as Sora at display weight until the cutout face exists —
    /// the direction is explicit that a display face is for rank 1 and nothing else.</summary>
    public const string DisplayPath = SansPath;
}

/// <summary>
/// <b>Tokens and recipes in; a complete Godot <see cref="Theme"/> out.</b> This is the single
/// styling system the overhaul's first target row demands — the audit found three (a drifted
/// generated theme, half-obeyed HUD tokens, and bespoke per-screen constants) and this
/// replaces all of them.
///
/// <para><b>Why this is C# and not the old <c>tools/BuildTheme.gd</c>.</b> That generator had
/// drifted from the <c>.tres</c> it produced on at least six values, so regenerating would
/// have silently restyled the game — which is why the shipped code carried a standing "never
/// regenerate" caution, which in turn is why three styling systems grew in the first place.
/// A generator that cannot be run is not a source of truth, it is a fossil. Building the theme
/// <i>at runtime, from the same tokens the C# widgets read</i>, removes the artefact that could
/// drift: there is no intermediate file that can disagree with anything. The exporter in
/// <see cref="UiThemeExport"/> still writes a <c>.tres</c> for editor preview, but it is
/// generated from this factory, so it is build output and nothing reads it in a running game.</para>
///
/// <para><b>Legacy variation names are kept deliberately.</b> Sixteen theme variations
/// (<c>PrimaryAction</c>, <c>MenuAction</c>, <c>Crumb</c>, the <c>Accent</c> colour slots…) are
/// referenced by name from shipped scenes and code. They are semantic slots whose VALUES move
/// to the new tokens here; renaming them is a separate deliberate pass under the Issue #62
/// protocol, never a sweep folded into an art change.</para>
/// </summary>
public static class UiThemeFactory
{
    /// <summary>
    /// Every theme type and variation this factory defines.
    ///
    /// <para><b>Why this list exists.</b> The runtime theme is hung on the root window, and
    /// Godot's lookup falls through to the project's default theme for any item an ancestor does
    /// not define. That default is still the committed <c>UITheme.tres</c> — so a type the
    /// factory forgets does not render unstyled, it renders in the OLD palette, silently, on one
    /// control, and looks like a bug in that screen rather than a hole in the system.
    ///
    /// <para>Declared as data so <c>UiThemeCoverageTests</c> can check it against the shipped
    /// <c>.tres</c> without a Godot runtime, which is the only way that risk is checkable at
    /// all.</para></para>
    /// </summary>
    public static readonly string[] CoveredTypes =
    {
        // buttons
        "Button", "PrimaryAction", "PrimaryOrange", "SecondaryAction", "GhostAction",
        "TertiaryAction", "DangerAction", "MenuAction", "MenuDanger",
        // labels
        "Label", "Display", "Hero", "Title", "Body", "Caption", "SectionLabel", "FieldLabel",
        "Crumb", "Tagline", "Micro", "Danger", "OnScrim", "DisplayOnScrim",
        "Mono", "MonoHero", "MonoTimer", "PerfHud",
        // fields and surfaces
        "LineEdit", "OptionButton", "PanelContainer", "Panel", "HudScrap", "Chip",
        "PopupMenu", "PopupPanel", "TooltipPanel", "TooltipLabel",
        "HSeparator", "VSeparator", "HSlider", "CheckButton", "CheckBox",
        "VScrollBar", "HScrollBar",
        // token slots
        "Accent", "Token",
    };

    /// <summary>Build the whole theme for one temperature.</summary>
    public static Theme Build(UiTemperature temperature) =>
        Build(UiTokens.For(temperature), UiRecipeSet.Current);

    /// <summary>Build the whole theme from an explicit token set and recipe set. Every styled
    /// thing in the game passes through here exactly once.</summary>
    public static Theme Build(UiTokens t, UiRecipeSet r)
    {
        var theme = new Theme
        {
            DefaultFont = Sans(UiScale.WeightBody),
            DefaultFontSize = UiScale.SizeBody,
        };

        BuildButtons(theme, t, r);
        BuildLabels(theme, t);
        BuildFields(theme, t, r);
        BuildPanels(theme, t, r);
        BuildControls(theme, t, r);
        BuildTokenSlots(theme, t);

        return theme;
    }

    // --- fonts -------------------------------------------------------------------------------

    private static FontVariation Sans(int weight, int tracking = 0) => Variation(UiTypeface.SansPath, weight, tracking);

    private static FontVariation Mono(int weight, int tracking = 0) => Variation(UiTypeface.MonoPath, weight, tracking);

    private static FontVariation Display(int weight, int tracking = 0) => Variation(UiTypeface.DisplayPath, weight, tracking);

    private static FontVariation Variation(string path, int weight, int tracking)
    {
        var font = new FontVariation { VariationOpentype = new Godot.Collections.Dictionary { { "wght", weight } } };
        if (ResourceLoader.Exists(path))
            font.BaseFont = GD.Load<Font>(path);
        if (tracking != 0)
            font.SetSpacing(TextServer.SpacingType.Glyph, tracking);
        return font;
    }

    // --- the stylebox, from a resolved spec ---------------------------------------------------

    /// <summary>The flat-colour path. Unchanged since before the paper kit existed, and kept
    /// exactly as it was: <c>HudTheme.cs</c> calls this directly and is out of this packet's
    /// scope (B2-3 owns applying paper chrome to the HUD), so its signature and behaviour cannot
    /// move. It is also the correct fallback for every case a texture genuinely cannot represent
    /// — see <see cref="BoxStyled"/>.</summary>
    public static StyleBoxFlat Box(SurfaceRecipe recipe, UiState state, UiTokens tokens)
    {
        StyleSpec spec = UiStyle.Resolve(recipe, state, tokens);
        var box = new StyleBoxFlat
        {
            BgColor = spec.Fill,
            DrawCenter = spec.DrawsFill,
            BorderColor = spec.Border,
            ContentMarginLeft = spec.PadLeft,
            ContentMarginRight = spec.PadRight,
            ContentMarginTop = spec.PadTop,
            ContentMarginBottom = spec.PadBottom,
        };
        box.SetCornerRadiusAll(spec.Radius);
        box.SetBorderWidthAll(spec.BorderWidth);
        if (spec.Elevation > 0)
        {
            box.ShadowSize = spec.Elevation;
            box.ShadowColor = spec.Shadow;
            box.ShadowOffset = new Vector2(0, spec.Elevation / 3f);
        }
        return box;
    }

    /// <summary>
    /// <b>The paper-edge dispatcher.</b> Every internal call site in this factory asks here
    /// instead of <see cref="Box"/> directly, so <see cref="SurfaceEdge"/> — dead data before
    /// this packet — actually selects something. Falls back to <see cref="Box"/> (unchanged,
    /// flat colour) wherever a 9-patch texture cannot honestly represent the recipe:
    ///
    /// <list type="bullet">
    /// <item><description><see cref="SurfaceEdge.Flat"/> — the reversibility dial itself.</description></item>
    /// <item><description><see cref="UiState.Focus"/> — focus is the loudest state in the
    /// system, always, and must keep the flat path's exact border colour/width fidelity; a
    /// static texture cannot track a live, mid-crossfade token colour.</description></item>
    /// <item><description><see cref="SurfaceFill.None"/> — nothing is drawn, so there is no
    /// paper to cut.</description></item>
    /// <item><description><see cref="SurfaceShape.Pill"/> — a rectangular 9-patch cannot fake a
    /// punched-out circle without per-instance-size art; no shipped recipe uses it, only the
    /// theme-demo's exploration dial.</description></item>
    /// </list>
    ///
    /// Every one of these is a real Godot API gap (verified against the engine's C# bindings:
    /// <c>StyleBoxTexture</c> has no border colour/width and no shadow properties, unlike
    /// <c>StyleBoxFlat</c>) rather than a shortcut, and each is named here rather than silently
    /// degrading, per the packet's own correction — the SurfaceEdge doc comment already claimed
    /// something false once.</summary>
    public static StyleBox BoxStyled(SurfaceRecipe recipe, UiState state, UiTokens tokens)
    {
        if (!UsesPaperTexture(recipe, state))
            return Box(recipe, state, tokens);
        return BoxTextured(recipe, state, tokens);
    }

    private static bool UsesPaperTexture(SurfaceRecipe recipe, UiState state) =>
        recipe.Edge != SurfaceEdge.Flat
        && recipe.Fill != SurfaceFill.None
        && (recipe.Shape == SurfaceShape.Soft || recipe.Shape == SurfaceShape.Rounded)
        && state != UiState.Focus;

    /// <summary>Builds the textured stylebox. <see cref="StyleBoxTexture"/> has exactly one
    /// paint colour (<c>ModulateColor</c>, multiplied over the whole 9-patch) — it cannot draw a
    /// separate border colour the way <see cref="StyleBoxFlat"/> can. For an
    /// <see cref="SurfaceFill.Outline"/> recipe at rest, the flat path's real content IS its
    /// border (the fill resolves fully transparent there — see <c>UiStyle.ResolveFill</c>), so
    /// this branches to modulate by <c>spec.Border</c> and suppress the centre cell instead,
    /// which is what a 9-patch's edge-only draw already means. Every other state modulates by
    /// <c>spec.Fill</c> as usual, matching what <see cref="Box"/> would have painted.</summary>
    private static StyleBoxTexture BoxTextured(SurfaceRecipe recipe, UiState state, UiTokens tokens)
    {
        StyleSpec spec = UiStyle.Resolve(recipe, state, tokens);
        bool asOutlineRing = recipe.Fill == SurfaceFill.Outline && state == UiState.Normal;
        Color modulate = asOutlineRing ? spec.Border : spec.Fill;
        bool drawCenter = asOutlineRing ? false : spec.DrawsFill;

        PaperEdgeSheet sheet = PaperEdgeSheet.For(recipe.Edge, recipe.Shape);
        var box = new StyleBoxTexture
        {
            Texture = sheet.Texture,
            ModulateColor = modulate,
            DrawCenter = drawCenter,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile,
            TextureMarginLeft = sheet.Margin,
            TextureMarginRight = sheet.Margin,
            TextureMarginTop = sheet.Margin,
            TextureMarginBottom = sheet.Margin,
            ContentMarginLeft = spec.PadLeft,
            ContentMarginRight = spec.PadRight,
            ContentMarginTop = spec.PadTop,
            ContentMarginBottom = spec.PadBottom,
        };
        return box;
    }

    /// <summary>Maps an edge treatment and a shape to its baked 9-patch sheet. Two corner
    /// classes exist — <c>Soft</c> (the small scissor-cut corner most of the kit uses) and
    /// <c>Rounded</c> (the larger cut-paper card corner Card/Popup use) — because
    /// <see cref="StyleBoxTexture"/> has no numeric corner-radius property to drive from
    /// <see cref="UiScale"/>; the roundness has to be baked into the art instead, so one sheet
    /// per corner class stands in for the whole numeric range rather than one sheet per exact
    /// radius.</summary>
    private readonly record struct PaperEdgeSheet(Texture2D Texture, int Margin)
    {
        public static PaperEdgeSheet For(SurfaceEdge edge, SurfaceShape shape)
        {
            string edgeName = edge switch
            {
                SurfaceEdge.Torn => "torn",
                SurfaceEdge.Singed => "singed",
                _ => "scissor",
            };
            string corner = shape == SurfaceShape.Rounded ? "card" : "soft";
            int margin = shape == SurfaceShape.Rounded ? 22 : 14;
            var tex = GD.Load<Texture2D>($"res://assets/ui/paper/edges/{edgeName}_{corner}.png");
            return new PaperEdgeSheet(tex, margin);
        }
    }

    /// <summary><b>All five states, always.</b> A component styled through this function cannot
    /// ship missing one, which is the mechanism behind the overhaul's state-coverage target.</summary>
    private static void StyleButton(
        Theme theme, string type, SurfaceRecipe recipe, UiTokens t, int fontSize, int weight, int tracking = 0)
    {
        if (type != "Button")
            theme.SetTypeVariation(type, "Button");

        theme.SetFont("font", type, Sans(weight, tracking));
        theme.SetFontSize("font_size", type, fontSize);

        theme.SetStylebox("normal", type, BoxStyled(recipe, UiState.Normal, t));
        theme.SetStylebox("hover", type, BoxStyled(recipe, UiState.Hover, t));
        theme.SetStylebox("pressed", type, BoxStyled(recipe, UiState.Pressed, t));
        theme.SetStylebox("hover_pressed", type, BoxStyled(recipe, UiState.Pressed, t));
        theme.SetStylebox("focus", type, BoxStyled(recipe, UiState.Focus, t));
        theme.SetStylebox("disabled", type, BoxStyled(recipe, UiState.Disabled, t));

        theme.SetColor("font_color", type, UiStyle.Resolve(recipe, UiState.Normal, t).Ink);
        theme.SetColor("font_hover_color", type, UiStyle.Resolve(recipe, UiState.Hover, t).Ink);
        theme.SetColor("font_pressed_color", type, UiStyle.Resolve(recipe, UiState.Pressed, t).Ink);
        theme.SetColor("font_hover_pressed_color", type, UiStyle.Resolve(recipe, UiState.Pressed, t).Ink);
        theme.SetColor("font_focus_color", type, UiStyle.Resolve(recipe, UiState.Focus, t).Ink);
        theme.SetColor("font_disabled_color", type, UiStyle.Resolve(recipe, UiState.Disabled, t).Ink);

        if (recipe.MinHeight > 0)
            theme.SetConstant("minimum_character_width", type, 0);
    }

    private static void BuildButtons(Theme theme, UiTokens t, UiRecipeSet r)
    {
        // Base Button is the quiet inline verb — Back, Cancel, a link. Even this answers the
        // pointer: the playtest verdict on the old theme was "cheap and non-responsive", and an
        // empty normal box with an empty hover box is exactly what that feels like.
        StyleButton(theme, "Button", r.Text, t, UiScale.SizeBody, UiScale.WeightMedium);

        // The primary verb. Both legacy names for it map to the same recipe, so a screen that
        // says PrimaryAction and a screen that says PrimaryOrange can no longer disagree about
        // what the primary action looks like — they did before.
        StyleButton(theme, "PrimaryAction", r.Action, t, UiScale.SizeBody, UiScale.WeightStrong);
        StyleButton(theme, "PrimaryOrange", r.Action, t, UiScale.SizeBody, UiScale.WeightStrong);

        StyleButton(theme, "SecondaryAction", r.Quiet, t, UiScale.SizeBody, UiScale.WeightMedium);
        StyleButton(theme, "GhostAction", r.Quiet, t, UiScale.SizeBody, UiScale.WeightMedium);
        StyleButton(theme, "TertiaryAction", r.Text, t, UiScale.SizeBody, UiScale.WeightMedium);
        StyleButton(theme, "DangerAction", r.Danger, t, UiScale.SizeBody, UiScale.WeightMedium);

        // Front-of-house menu items: rank 1, so weight and size carry them, not chrome.
        StyleButton(theme, "MenuAction", r.Menu, t, UiScale.SizeTitle, UiScale.WeightStrong, tracking: 2);

        // Quit / Leave: the same menu shape, danger ink. A variation of a variation, so it
        // cannot drift away from the menu items it sits among.
        SurfaceRecipe menuDanger = r.Menu with { Ink = InkRole.Danger };
        StyleButton(theme, "MenuDanger", menuDanger, t, UiScale.SizeTitle, UiScale.WeightStrong, tracking: 2);
    }

    // --- labels: three ranks, six sizes --------------------------------------------------------

    private static void StyleLabel(
        Theme theme, string type, Font font, int size, Color color, int lineSpacing = 0)
    {
        if (type != "Label")
            theme.SetTypeVariation(type, "Label");
        theme.SetFont("font", type, font);
        theme.SetFontSize("font_size", type, size);
        theme.SetColor("font_color", type, color);
        if (lineSpacing > 0)
            theme.SetConstant("line_spacing", type, lineSpacing);
    }

    private static void BuildLabels(Theme theme, UiTokens t)
    {
        // Rank 2 is the base: most text on screen is body text, so body is what an unmarked
        // Label gets. Rank is carried by weight and colour before size (direction brief).
        StyleLabel(theme, "Label", Sans(UiScale.WeightBody), UiScale.SizeBody, t.InkRank2);

        // Rank 1 — the display face, headings, the one number that matters.
        StyleLabel(theme, "Display", Display(UiScale.WeightDisplay, 1), UiScale.SizeDisplay, t.InkRank1);
        StyleLabel(theme, "Hero", Display(UiScale.WeightDisplay, 2), UiScale.SizeHero, t.InkRank1);
        StyleLabel(theme, "Title", Sans(UiScale.WeightStrong, 1), UiScale.SizeTitle, t.InkRank1);

        // Rank 2 — body and helper prose.
        StyleLabel(theme, "Body", Sans(UiScale.WeightBody), UiScale.SizeBody, t.InkRank2, lineSpacing: UiScale.SpaceTight);

        // Rank 3 — captions, field labels, breadcrumbs, hints.
        StyleLabel(theme, "Caption", Sans(UiScale.WeightMedium, 1), UiScale.SizeCaption, t.InkRank3);
        StyleLabel(theme, "SectionLabel", Sans(UiScale.WeightMedium, 1), UiScale.SizeCaption, t.InkRank3);
        StyleLabel(theme, "FieldLabel", Sans(UiScale.WeightMedium, 1), UiScale.SizeCaption, t.InkRank3);
        StyleLabel(theme, "Crumb", Sans(UiScale.WeightStrong, 2), UiScale.SizeCaption, t.InkRank3);
        StyleLabel(theme, "Tagline", Sans(UiScale.WeightMedium, 2), UiScale.SizeCaption, t.InkRank3);
        StyleLabel(theme, "Micro", Sans(UiScale.WeightMedium, 1), UiScale.SizeMicro, t.InkRank3);

        // Semantic.
        StyleLabel(theme, "Danger", Sans(UiScale.WeightBody), UiScale.SizeBody, t.InkDanger);

        // On the scrim rather than on a card — PAUSED, the loss line.
        StyleLabel(theme, "OnScrim", Sans(UiScale.WeightBody), UiScale.SizeBody, t.InkOnScrim);
        StyleLabel(theme, "DisplayOnScrim", Display(UiScale.WeightDisplay, 1), UiScale.SizeDisplay, t.InkOnScrim);

        // Mono: room codes, timers, anything whose digits must not re-flow as they change.
        StyleLabel(theme, "Mono", Mono(UiScale.WeightMedium), UiScale.SizeBody, t.InkRank2);
        StyleLabel(theme, "MonoHero", Mono(UiScale.WeightStrong, 6), UiScale.SizeHero, t.InkRank1);
        StyleLabel(theme, "MonoTimer", Mono(UiScale.WeightStrong), UiScale.SizeDisplay, t.InkRank1);

        // The performance readout is a developer instrument, not part of the interface. It gets
        // the smallest mono rank and is deliberately outside the type ranks above.
        StyleLabel(theme, "PerfHud", Mono(UiScale.WeightBody), UiScale.SizeMicro, t.InkRank3);
    }

    // --- fields ---------------------------------------------------------------------------------

    private static void BuildFields(Theme theme, UiTokens t, UiRecipeSet r)
    {
        theme.SetFont("font", "LineEdit", Sans(UiScale.WeightBody));
        theme.SetFontSize("font_size", "LineEdit", UiScale.SizeBody);
        theme.SetColor("font_color", "LineEdit", t.InkRank1);
        theme.SetColor("font_placeholder_color", "LineEdit", t.InkRank3);
        theme.SetColor("font_uneditable_color", "LineEdit", t.InkDisabled);
        theme.SetColor("caret_color", "LineEdit", t.Accent);
        theme.SetColor("selection_color", "LineEdit", new Color(t.Accent, 0.30f));
        theme.SetStylebox("normal", "LineEdit", BoxStyled(r.Field, UiState.Normal, t));
        theme.SetStylebox("focus", "LineEdit", BoxStyled(r.Field, UiState.Focus, t));
        theme.SetStylebox("read_only", "LineEdit", BoxStyled(r.Field, UiState.Disabled, t));

        // The dropdown is a field that behaves like a button, so it takes the field recipe and
        // the full state ladder.
        theme.SetFont("font", "OptionButton", Sans(UiScale.WeightBody));
        theme.SetFontSize("font_size", "OptionButton", UiScale.SizeBody);
        foreach ((string slot, UiState state) in new[]
                 {
                     ("normal", UiState.Normal), ("hover", UiState.Hover), ("pressed", UiState.Pressed),
                     ("focus", UiState.Focus), ("disabled", UiState.Disabled),
                 })
            theme.SetStylebox(slot, "OptionButton", BoxStyled(r.Field, state, t));

        theme.SetColor("font_color", "OptionButton", t.InkRank1);
        theme.SetColor("font_hover_color", "OptionButton", t.InkRank1);
        theme.SetColor("font_pressed_color", "OptionButton", t.InkRank1);
        theme.SetColor("font_focus_color", "OptionButton", t.InkRank1);
        theme.SetColor("font_disabled_color", "OptionButton", t.InkDisabled);
    }

    // --- panels ------------------------------------------------------------------------------

    private static void BuildPanels(Theme theme, UiTokens t, UiRecipeSet r)
    {
        StyleBox card = BoxStyled(r.Card, UiState.Normal, t);
        theme.SetStylebox("panel", "PanelContainer", card);
        theme.SetStylebox("panel", "Panel", card);

        // A panel that is chrome rather than content — the HUD scrap over live 3D.
        theme.SetTypeVariation("HudScrap", "PanelContainer");
        theme.SetStylebox("panel", "HudScrap", BoxStyled(r.HudScrap, UiState.Normal, t));

        theme.SetTypeVariation("Chip", "PanelContainer");
        theme.SetStylebox("panel", "Chip", BoxStyled(r.Chip, UiState.Normal, t));

        StyleBox popup = BoxStyled(r.Popup, UiState.Normal, t);
        theme.SetStylebox("panel", "PopupMenu", popup);
        theme.SetStylebox("panel", "PopupPanel", popup);
        theme.SetColor("font_color", "PopupMenu", t.InkRank2);
        theme.SetColor("font_hover_color", "PopupMenu", t.InkRank1);
        theme.SetColor("font_disabled_color", "PopupMenu", t.InkDisabled);
        theme.SetColor("font_accelerator_color", "PopupMenu", t.InkRank3);
        theme.SetFont("font", "PopupMenu", Sans(UiScale.WeightBody));
        theme.SetFontSize("font_size", "PopupMenu", UiScale.SizeBody);

        var popupHover = new StyleBoxFlat { BgColor = new Color(t.Accent, 0.20f) };
        popupHover.SetCornerRadiusAll(UiStyle.RadiusFor(r.Chip.Shape));
        theme.SetStylebox("hover", "PopupMenu", popupHover);

        // Tooltips are popups too — they were unstyled, which is how a project ends up with a
        // grey engine-default box floating over a designed screen.
        theme.SetStylebox("panel", "TooltipPanel", popup);
        theme.SetColor("font_color", "TooltipLabel", t.InkRank2);
        theme.SetFontSize("font_size", "TooltipLabel", UiScale.SizeCaption);

        var separator = new StyleBoxLine { Color = t.Hairline, Thickness = UiScale.BorderHair };
        theme.SetStylebox("separator", "HSeparator", separator);
        theme.SetStylebox("separator", "VSeparator", separator);
    }

    // --- the remaining controls ------------------------------------------------------------------

    private static void BuildControls(Theme theme, UiTokens t, UiRecipeSet r)
    {
        // Slider: a track, a fill, and — the audit's finding 6 — an actual focus state, which
        // it did not have. A controller player could not see which slider they were on.
        var track = new StyleBoxFlat { BgColor = t.Hairline, ContentMarginTop = 2, ContentMarginBottom = 2 };
        track.SetCornerRadiusAll(UiScale.RadiusTight);
        theme.SetStylebox("slider", "HSlider", track);

        var fill = new StyleBoxFlat { BgColor = new Color(t.Accent, 0.55f) };
        fill.SetCornerRadiusAll(UiScale.RadiusTight);
        theme.SetStylebox("grabber_area", "HSlider", fill);

        var fillHot = new StyleBoxFlat { BgColor = t.Accent };
        fillHot.SetCornerRadiusAll(UiScale.RadiusTight);
        theme.SetStylebox("grabber_area_highlight", "HSlider", fillHot);

        var sliderFocus = new StyleBoxFlat
        {
            DrawCenter = false,
            BorderColor = t.Focus,
            ContentMarginTop = 2,
            ContentMarginBottom = 2,
        };
        sliderFocus.SetBorderWidthAll(UiScale.BorderFocus);
        sliderFocus.SetCornerRadiusAll(UiScale.RadiusTight);
        theme.SetStylebox("focus", "HSlider", sliderFocus);

        foreach (string type in new[] { "CheckButton", "CheckBox" })
        {
            theme.SetFont("font", type, Sans(UiScale.WeightBody));
            theme.SetFontSize("font_size", type, UiScale.SizeBody);
            theme.SetColor("font_color", type, t.InkRank2);
            theme.SetColor("font_hover_color", type, t.InkRank1);
            theme.SetColor("font_pressed_color", type, t.InkRank1);
            theme.SetColor("font_focus_color", type, t.InkRank1);
            theme.SetColor("font_disabled_color", type, t.InkDisabled);
            theme.SetStylebox("focus", type, Box(r.Text, UiState.Focus, t));
        }

        var scroll = new StyleBoxFlat { BgColor = new Color(t.InkRank1, 0.06f) };
        scroll.SetCornerRadiusAll(UiScale.RadiusTight);
        theme.SetStylebox("scroll", "VScrollBar", scroll);
        theme.SetStylebox("scroll", "HScrollBar", scroll);

        var grabber = new StyleBoxFlat { BgColor = new Color(t.InkRank1, 0.28f) };
        grabber.SetCornerRadiusAll(UiScale.RadiusTight);
        theme.SetStylebox("grabber", "VScrollBar", grabber);
        theme.SetStylebox("grabber", "HScrollBar", grabber);

        var grabberHot = new StyleBoxFlat { BgColor = new Color(t.InkRank1, 0.45f) };
        grabberHot.SetCornerRadiusAll(UiScale.RadiusTight);
        theme.SetStylebox("grabber_highlight", "VScrollBar", grabberHot);
        theme.SetStylebox("grabber_highlight", "HScrollBar", grabberHot);
    }

    // --- queryable token slots -------------------------------------------------------------------

    /// <summary>
    /// Tokens exposed as theme colours, so a custom-drawn control (and any GDScript tool) can
    /// read the live palette without taking a compile-time dependency on this assembly.
    ///
    /// <para>The <c>Accent</c> slot names are <b>legacy Meridian identifiers</b> kept on purpose:
    /// eleven call sites and sixteen scene references read them, and renaming them is a separate
    /// deliberate pass (Issue #62 protocol — one pass, never a sweep). Read the token they map
    /// to, never the name: <c>teal</c> is now the moonlight accent, <c>orange</c> is now danger.
    /// The honest names live under <c>Token</c> alongside them and are what new code should use.</para>
    /// </summary>
    private static void BuildTokenSlots(Theme theme, UiTokens t)
    {
        // Legacy slots — same names, token values.
        theme.SetColor("teal", "Accent", t.Accent);
        theme.SetColor("teal_hi", "Accent", t.AccentHi);
        theme.SetColor("mint", "Accent", t.AccentHi);
        theme.SetColor("orange", "Accent", t.Danger);
        theme.SetColor("blush", "Accent", t.InkDanger);
        theme.SetColor("surface", "Accent", t.PageSheet);
        theme.SetColor("panel", "Accent", t.SurfaceCard);
        theme.SetColor("hair", "Accent", t.Hairline);

        // The honest names.
        theme.SetColor("page_ground", "Token", t.PageGround);
        theme.SetColor("page_sheet", "Token", t.PageSheet);
        theme.SetColor("surface_card", "Token", t.SurfaceCard);
        theme.SetColor("surface_raised", "Token", t.SurfaceRaised);
        theme.SetColor("surface_sunken", "Token", t.SurfaceSunken);
        theme.SetColor("surface_chip", "Token", t.SurfaceChip);
        theme.SetColor("scrim", "Token", t.Scrim);
        theme.SetColor("accent", "Token", t.Accent);
        theme.SetColor("accent_hi", "Token", t.AccentHi);
        theme.SetColor("accent_dim", "Token", t.AccentDim);
        theme.SetColor("danger", "Token", t.Danger);
        theme.SetColor("ink_rank1", "Token", t.InkRank1);
        theme.SetColor("ink_rank2", "Token", t.InkRank2);
        theme.SetColor("ink_rank3", "Token", t.InkRank3);
        theme.SetColor("ink_disabled", "Token", t.InkDisabled);
        theme.SetColor("ink_on_accent", "Token", t.InkOnAccent);
        theme.SetColor("ink_on_scrim", "Token", t.InkOnScrim);
        theme.SetColor("ink_danger", "Token", t.InkDanger);
        theme.SetColor("hairline", "Token", t.Hairline);
        theme.SetColor("hairline_strong", "Token", t.HairlineStrong);
        theme.SetColor("focus", "Token", t.Focus);
        theme.SetColor("shadow", "Token", t.Shadow);

        // Which temperature this theme is, readable by anything that has the theme but not the
        // service — 0 = Day, 1 = Night.
        theme.SetConstant("temperature", "Token", (int)t.Temperature);
    }
}
