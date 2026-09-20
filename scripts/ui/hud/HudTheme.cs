using Godot;
using MpFoundation.Ui.Design;

namespace MpFoundation.Ui.Hud;

/// <summary>
/// The in-game HUD's view of the design system. <b>It no longer owns anything</b> — every value
/// below forwards to <see cref="UiTokens"/>, <see cref="UiScale"/> and <see cref="UiRecipes"/>,
/// so the HUD is styled by the same substrate as the menus, the flow screens and the dialogs.
///
/// <para><b>What this file used to be, and why it changed.</b> It was the second of the
/// project's three styling systems: a set of hand-picked HUD tokens that read a few slots out of
/// the shipped theme and hardcoded the rest, carrying its own spacing scale, its own corner
/// radius and its own measured scrim. That was a real improvement on the ~35 colour literals it
/// replaced, but two token systems that agree today are two token systems that disagree in a
/// month — and the audit measured exactly that drift. The scale it defined was the good one, so
/// the consolidation moved the project onto <i>this</i> scale rather than moving the HUD off it;
/// the constants below are now aliases pointing at the shared definitions.</para>
///
/// <para><b>The HUD gets the night temperature for free.</b> Nothing here branches on day or
/// night: the tokens do, and the scraps re-read them when
/// <see cref="UiThemeService.TokensChanged"/> fires. That is the direction's kill rule 2 in
/// practice — the night HUD is the day HUD under different light, not a second HUD.</para>
/// </summary>
public static class HudTheme
{
    // --- the spacing scale ----------------------------------------------------------------
    // Aliases onto the one scale. Kept as names so the ~30 existing call sites are unchanged;
    // the values are no longer defined here.

    public const int Space1 = UiScale.SpaceTight;
    public const int Space2 = UiScale.SpaceSnug;
    public const int Space3 = UiScale.SpaceNormal;
    public const int Space4 = UiScale.SpaceLoose;
    public const int Space6 = UiScale.SpaceWide;

    /// <summary>Distance from every panel to the screen edge — the shared value, so HUD scraps
    /// and menu cards sit the same distance in from the frame.</summary>
    public const float ScreenMargin = UiScale.ScreenMargin;

    /// <summary>Corner radius for HUD chrome. Comes from the HUD scrap's <i>recipe</i>, not from
    /// a constant, so re-cutting the game's corners re-cuts the HUD's too.</summary>
    public static int CornerRadius => UiStyle.RadiusFor(UiRecipes.Current.HudScrap.Shape);

    // --- colour, from the live token set ----------------------------------------------------

    /// <summary>The live tokens, at whatever temperature the world is currently lit at.</summary>
    private static UiTokens T => UiThemeService.Tokens;

    /// <summary>The scrim behind HUD chrome. Still measured rather than chosen — the alpha lives
    /// on the HudScrap recipe with the contrast reasoning attached — but the colour is now the
    /// shared card token, so HUD chrome and menu cards are the same paper.</summary>
    public static Color Scrim => new(T.SurfaceCard, UiRecipes.Current.HudScrap.FillAlpha);

    /// <summary>Hairline border. Flips polarity with temperature, which a hardcoded value could
    /// not do: dark on cream by day, light on near-black at night.</summary>
    public static Color Hairline => T.Hairline;

    /// <summary><b>The one accent.</b> Moonlight — used for exactly one thing at a time: the
    /// live carry slot, and a transient flash when a value changes. The moment a second thing on
    /// screen is ember-coloured, the first one stops meaning anything.</summary>
    public static Color AccentPrimary => T.Accent;

    /// <summary>The lifted accent, for a flash that has to read against the accent itself.</summary>
    public static Color AccentHi => T.AccentHi;

    /// <summary>Full-contrast text.</summary>
    public static Color TextPrimary => T.InkRank1;

    /// <summary>The dim tier, for custom-drawn glyph chrome.</summary>
    public static Color TextMuted => T.InkRank3;

    // --- the map's own colour domain ------------------------------------------------------
    // The minimap is an information surface, not chrome: its markers encode WHICH ENTITY, and a
    // single scarce accent cannot express "you" versus "someone else". This is the one
    // documented exception to the one-accent rule, and it is why these are named here rather
    // than pulled from the accent slots.

    /// <summary>The local player's arrow — the brightest mark on the map, because it is the one
    /// the player looks for first. The ember: on a map, you are the fire you carry.</summary>
    public static Color MarkerSelf => T.AccentHi;

    /// <summary>Teammates. Distinct from <see cref="MarkerSelf"/> in VALUE as well as hue so the
    /// pair survives a colour-blind read (ART-BIBLE §3), and reinforced by shape — self is an
    /// arrow, teammates are dots. Takes a decoration primary rather than a second accent.</summary>
    public static Color MarkerTeammate => T.Primaries[3];

    /// <summary>The dark ring drawn behind a marker. The map composites over an arbitrary frame,
    /// so — exactly like text with no paper under it — the outline is what actually guarantees
    /// the mark is visible, and it takes the same token.</summary>
    public static Color MarkerOutline => T.TextOutline;

    // --- type ---------------------------------------------------------------------------------
    // Roles, not sizes. Each maps to a theme variation carrying weight, size, letter-spacing and
    // colour. Sizes are deliberately NOT restated here — restating them is how the HUD ended up
    // with a type ladder that disagreed with every other screen.

    /// <summary>Big mono numerals — the clock face.</summary>
    public const string RoleTimer = "MonoTimer";

    /// <summary>Mono values that change while on screen. Mono so a digit rolling over does not
    /// re-flow the string, which reads as the HUD twitching.</summary>
    public const string RoleValue = "Mono";

    /// <summary>Body text — slot names, the day/phase line.</summary>
    public const string RoleBody = "Body";

    /// <summary>Panel captions. Small, letter-spaced, rank 3.</summary>
    public const string RoleCaption = "Caption";

    /// <summary>The dimmest tier — mouse-verb hints and the IN HAND caption, read once while
    /// learning.</summary>
    public const string RoleHint = "Caption";

    /// <summary>A label wired to a theme role. Everything visual — family, weight, size, tracking,
    /// colour — arrives from the variation, so a token change lands here without an edit.
    ///
    /// The soft shadow is the only thing added on top, and it is a safety net rather than the
    /// mechanism: with the measured scrim behind the text, contrast is already guaranteed, so
    /// this only has to survive the frame or two while a panel fades in.</summary>
    public static Label MakeLabel(
        string text, string role, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Text = text,
            ThemeTypeVariation = role,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_shadow_color", T.TextOutline);
        label.AddThemeConstantOverride("shadow_offset_x", 0);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        return label;
    }

    /// <summary>A HUD panel, built from the shared HudScrap recipe. <paramref name="accented"/>
    /// marks the one live element — the recipe's focus treatment, which is the accent border the
    /// rest of the system already uses for "this is the thing". Never the only channel: callers
    /// pair it with a text or shape change (ART-BIBLE §3 — separate in VALUE, not just hue).</summary>
    public static StyleBoxFlat PanelStyle(bool accented = false)
    {
        SurfaceRecipe recipe = UiRecipes.Current.HudScrap;
        if (accented)
            recipe = recipe with { FillAlpha = recipe.FillAlpha + 0.08f, IsAccent = true, BorderWidth = UiScale.BorderWeight };
        return UiThemeFactory.Box(recipe, UiState.Normal, T);
    }

    /// <summary>Applies the scrap style to a panel and keeps applying it as the light changes.
    /// A HUD widget that sets its stylebox once in <c>_Ready</c> would still be lit at noon
    /// after nightfall; this is the one line that stops that.</summary>
    public static void BindPanel(Control panel, System.Func<bool>? accented = null) =>
        UiThemeService.Bind(panel, _ => panel.AddThemeStyleboxOverride("panel", PanelStyle(accented?.Invoke() ?? false)));
}
