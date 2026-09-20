using Godot;

namespace MpFoundation.Ui.Design;

/// <summary>The five states every interactive thing in this game has, without exception. The
/// audit found state coverage "bimodal — themed Buttons full, every custom control partial or
/// none"; making the state list an enum that the resolver switches on exhaustively is what
/// stops a control shipping with three of them.</summary>
public enum UiState : byte
{
    Normal = 0,
    Hover = 1,
    Pressed = 2,

    /// <summary>Controller focus. <b>The loudest state in the system.</b> By day the fastener
    /// moves to the focused element; by night focus is a torchlight halo — the cursor is
    /// literally a beam of light. Never quieter than hover, on anything, ever.</summary>
    Focus = 3,

    Disabled = 4,
}

/// <summary>
/// A fully resolved surface: what Godot should actually draw. Pure data — no engine objects —
/// so the Godot-free suite can assert on the real shipped appearance of every component in
/// every state at both temperatures, which is the only way "five states on everything" is a
/// fact rather than a claim.
/// </summary>
public readonly record struct StyleSpec
{
    public required Color Fill { get; init; }
    public required Color Border { get; init; }
    public required Color Ink { get; init; }
    public required int BorderWidth { get; init; }
    public required int Radius { get; init; }
    public required int PadLeft { get; init; }
    public required int PadRight { get; init; }
    public required int PadTop { get; init; }
    public required int PadBottom { get; init; }
    public required int Elevation { get; init; }
    public required Color Shadow { get; init; }

    /// <summary>Whether anything is painted behind the content at all.</summary>
    public bool DrawsFill => Fill.A > 0.001f;
}

/// <summary>
/// <b>The resolver: recipe + state + temperature → what gets drawn.</b> One function, used by
/// the theme generator, by the code-drawn HUD, and by the tests — so what the tests measure is
/// literally what renders. There is no second implementation of "what does a hovered button
/// look like".
///
/// <para>Every state is derived from the recipe rather than authored per component. That is
/// the mechanism behind the overhaul's state-coverage target: a component cannot ship missing
/// its pressed state, because nobody writes a pressed state.</para>
/// </summary>
public static class UiStyle
{
    /// <summary>How much a hovered translucent surface gains. Paper lifting off the page.</summary>
    private const float HoverAlphaGain = 0.10f;

    /// <summary>How far pressed content shifts down. Total height is preserved — top gains,
    /// bottom loses — so a press never re-lays-out the screen.</summary>
    public const int PressShift = 2;

    /// <summary>Extra shadow radius under a hovered, liftable surface.</summary>
    private const int HoverLiftGain = 6;

    /// <summary>The night focus halo's reach. Focus at night is a light source, so its shadow is
    /// accent-tinted and larger than any elevation the system otherwise uses.</summary>
    private const int TorchHalo = 20;

    /// <summary>Resolve one component, in one state, at one temperature.</summary>
    public static StyleSpec Resolve(SurfaceRecipe recipe, UiState state, UiTokens tokens)
    {
        Color fill = ResolveFill(recipe, state, tokens);
        Color border = ResolveBorder(recipe, state, tokens);
        Color ink = ResolveInk(recipe, state, tokens);

        int padX = UiScale.Px(recipe.PadX);
        int padY = UiScale.Px(recipe.PadY);
        int shift = state == UiState.Pressed && recipe.Press == PressFeel.Depress ? PressShift : 0;

        int borderWidth = state switch
        {
            UiState.Focus => UiScale.BorderFocus,
            // A borderless recipe stays borderless in every state but focus: focus is the one
            // state allowed to add chrome a component does not otherwise have, because a
            // controller player must be able to see where they are.
            _ when recipe.BorderWidth == 0 => 0,
            UiState.Hover => recipe.BorderWidth,
            UiState.Disabled => recipe.BorderWidth,
            _ => recipe.BorderWidth,
        };

        (int elevation, Color shadow) = ResolveLift(recipe, state, tokens);

        return new StyleSpec
        {
            Fill = fill,
            Border = border,
            Ink = ink,
            BorderWidth = borderWidth,
            Radius = RadiusFor(recipe.Shape),
            PadLeft = padX,
            PadRight = padX,
            PadTop = padY + shift,
            PadBottom = padY - shift,
            Elevation = elevation,
            Shadow = shadow,
        };
    }

    /// <summary>Corner radius for a shape. The indirection that makes "make the buttons round"
    /// a one-token edit rather than a search-and-replace.</summary>
    public static int RadiusFor(SurfaceShape shape) => shape switch
    {
        SurfaceShape.Square => 0,
        SurfaceShape.Soft => UiScale.RadiusTight,
        SurfaceShape.Rounded => UiScale.RadiusCard,
        SurfaceShape.Pill => UiScale.RadiusPill,
        _ => 0,
    };

    // --- fill -----------------------------------------------------------------------------------

    private static Color ResolveFill(SurfaceRecipe recipe, UiState state, UiTokens tokens)
    {
        if (state == UiState.Disabled)
            return recipe.Fill == SurfaceFill.None ? Transparent : tokens.DisabledFill;

        if (recipe.Fill is SurfaceFill.None or SurfaceFill.Outline)
        {
            // A surface with no fill at rest still has to answer the pointer, or it reads as
            // dead — the "cheap and non-responsive" playtest verdict this system exists to fix.
            // Hover and press paint the faintest wash of the ink it will show.
            return state switch
            {
                UiState.Hover => new Color(tokens.InkRank1, 0.08f),
                UiState.Pressed => new Color(tokens.InkRank1, 0.14f),
                _ => Transparent,
            };
        }

        Color baseFill = BaseFill(recipe.FillToken, state, tokens);

        if (recipe.Fill == SurfaceFill.Translucent)
        {
            float alpha = recipe.FillAlpha + state switch
            {
                UiState.Hover => HoverAlphaGain,
                UiState.Pressed => -HoverAlphaGain * 0.5f,
                _ => 0f,
            };
            return new Color(baseFill, Mathf.Clamp(alpha, 0f, 1f));
        }

        return baseFill;
    }

    /// <summary>The token a fill role names, already stepped for the state. The accent's three
    /// values ARE its states — rest, brightening, banked — which is why hover and press on the
    /// one accent component read as the light moving rather than as a generic tint. Since UI-3
    /// that ramp is the main menu's, so a Host button in a lobby steps through exactly the values
    /// the title screen's Host button steps through.</summary>
    private static Color BaseFill(FillRole role, UiState state, UiTokens tokens) => role switch
    {
        FillRole.Accent => state switch
        {
            UiState.Hover => tokens.AccentHi,
            UiState.Pressed => tokens.AccentDim,
            UiState.Focus => tokens.Accent,
            _ => tokens.Accent,
        },
        FillRole.Danger => state switch
        {
            UiState.Hover => new Color(tokens.Danger, 0.22f),
            UiState.Pressed => new Color(tokens.Danger, 0.34f),
            _ => new Color(tokens.Danger, 0.12f),
        },
        FillRole.Ground => tokens.PageGround,
        FillRole.Sheet => tokens.PageSheet,
        FillRole.Card => Step(tokens.SurfaceCard, state, tokens),
        FillRole.Raised => Step(tokens.SurfaceRaised, state, tokens),
        FillRole.Sunken => Step(tokens.SurfaceSunken, state, tokens),
        FillRole.Chip => Step(tokens.SurfaceChip, state, tokens),
        _ => Transparent,
    };

    /// <summary>Hover and press move a neutral surface toward and away from the light. The
    /// direction depends on temperature: by day a lifted cutout catches more light, at night
    /// it catches more fire — either way it gets brighter, and pressing it flattens it back.</summary>
    private static Color Step(Color surface, UiState state, UiTokens tokens)
    {
        float amount = tokens.Temperature == UiTemperature.Day ? 0.06f : 0.10f;
        return state switch
        {
            UiState.Hover => surface.Lightened(amount),
            UiState.Pressed => surface.Darkened(amount),
            _ => surface,
        };
    }

    // --- border ---------------------------------------------------------------------------------

    private static Color ResolveBorder(SurfaceRecipe recipe, UiState state, UiTokens tokens)
    {
        if (state == UiState.Focus)
            return tokens.Focus;

        if (state == UiState.Disabled)
            return tokens.Hairline;

        if (recipe.FillToken == FillRole.Danger)
            return state == UiState.Normal ? new Color(tokens.Danger, 0.55f) : tokens.Danger;

        if (recipe.IsAccent)
            return state == UiState.Hover ? tokens.AccentHi : new Color(tokens.AccentHi, 0.55f);

        return state == UiState.Hover ? tokens.HairlineStrong : tokens.Hairline;
    }

    // --- ink -------------------------------------------------------------------------------------

    private static Color ResolveInk(SurfaceRecipe recipe, UiState state, UiTokens tokens)
    {
        if (state == UiState.Disabled)
            return tokens.InkDisabled;

        Color ink = recipe.Ink switch
        {
            InkRole.Rank1 => tokens.InkRank1,
            InkRole.Rank2 => tokens.InkRank2,
            InkRole.Rank3 => tokens.InkRank3,
            InkRole.OnAccent => tokens.InkOnAccent,
            InkRole.Danger => tokens.InkDanger,
            _ => tokens.InkRank2,
        };

        // A quiet or text recipe brightens one rank under the pointer — hierarchy answering,
        // rather than a colour change that means nothing.
        if (state is UiState.Hover or UiState.Focus or UiState.Pressed && recipe.Ink == InkRole.Rank3)
            return tokens.InkRank1;

        return ink;
    }

    // --- lift ------------------------------------------------------------------------------------

    private static (int, Color) ResolveLift(SurfaceRecipe recipe, UiState state, UiTokens tokens)
    {
        // The night focus halo is the ACCENT, not the focus colour — the ring is the beam and the
        // halo is the light around it. It was the ember when the accent was fire; UI-3 repointed it
        // with the rest of the accent role.
        //
        // KNOWN, MEASURED, AND NOT FIXED HERE: the focus RING is one colour, and one colour cannot
        // do this job on an accent plate. Focus (PaperWhite) sits 1.86:1 from the accent it rings
        // — it was 2.32:1 against the ember, so this is worse, and both are under the 3:1 a
        // non-text indicator owes. The ring still reads (22.2 L* of value separation, 3 px wide),
        // so this is a shortfall rather than an invisible focus state, and it predates the accent
        // change. The main menu already solved it properly with a TWO-TONE ring — light outside
        // against the frame (12.31:1), dark inside against the plate (8.31:1); see
        // MenuLook.FocusRingOuter/FocusRingInner. Bringing that here means a second focus tone in
        // UiTokens and a second border pass in StyleSpec and the factory, which is a packet, not a
        // line. UI-3 measured it and left it; it is in the UI-3 report.
        if (state == UiState.Focus && tokens.Temperature == UiTemperature.Night)
            return (recipe.Elevation + TorchHalo, new Color(tokens.AccentHi, 0.45f));

        if (state == UiState.Disabled || state == UiState.Pressed)
            return (0, tokens.Shadow);

        if (state == UiState.Hover && recipe.HoverLift && recipe.Elevation > 0)
            return (recipe.Elevation + HoverLiftGain, tokens.Shadow);

        return (recipe.Elevation, tokens.Shadow);
    }

    private static readonly Color Transparent = new(0f, 0f, 0f, 0f);

    // --- contrast, measured rather than eyeballed ------------------------------------------------

    /// <summary>WCAG relative luminance. Alpha is ignored — composite first with
    /// <see cref="Over"/> if the colour is translucent.</summary>
    public static float Luminance(Color c)
    {
        static float Channel(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        return 0.2126f * Channel(c.R) + 0.7152f * Channel(c.G) + 0.0722f * Channel(c.B);
    }

    /// <summary>WCAG contrast ratio, 1.0–21.0.</summary>
    public static float Contrast(Color a, Color b)
    {
        float la = Luminance(a), lb = Luminance(b);
        return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
    }

    /// <summary><b>CIE L*, 0–100.</b> Perceptual lightness, which is what "separate in value"
    /// (ART-BIBLE §3) actually names — the greyscale a colourblind player is reading.</summary>
    public static float Lightness(Color c)
    {
        float y = Luminance(c);
        return y > 0.008856f
            ? 116f * Mathf.Pow(y, 1f / 3f) - 16f
            : 903.3f * y;
    }

    /// <summary>
    /// <b>How far apart two colours sit in value</b>, in CIE L* units. About 2 is the perceptible
    /// threshold; <see cref="ValueSeparationFloor"/> is the bar this system holds meaningful pairs
    /// to.
    ///
    /// <para><b>Why this exists rather than reusing <see cref="Contrast"/>.</b> The WCAG ratio is a
    /// <i>legibility</i> metric built around a +0.05 flare term, and that term compresses hard at
    /// the light end: two colours a reader can tell apart at a glance can measure under 1.6:1 if
    /// both are light. That was invisible while every meaningful pair in the kit had a dark member.
    /// It stopped being invisible on 2026-08-29, when the accent went from ember (L* 65) to
    /// moonlight (L* 72) and the accent-vs-danger pair fell from 1.91:1 to 1.53:1 on a ratio bar of
    /// 1.8 — while its actual value separation, 14.7 L*, stayed seven times the perceptible
    /// threshold and the two colours moved from both-warm to warm-versus-cool. The ratio bar was
    /// descriptive of the old palette, not prescriptive of the rule. Contrast still governs every
    /// ink-on-surface pair, where legibility is genuinely the question.</para>
    /// </summary>
    public static float ValueSeparation(Color a, Color b) => Mathf.Abs(Lightness(a) - Lightness(b));

    /// <summary>The bar for a pair that carries meaning through colour. 12 L* is six times the
    /// ~2 L* perceptible threshold and below both pairs the shipped kit actually has (accent vs
    /// danger 14.7, self vs teammate 46.0), so it fails on a real collapse rather than on a
    /// retune.</summary>
    public const float ValueSeparationFloor = 12f;

    /// <summary>Composite a translucent colour over an opaque one, so a contrast check on a
    /// scrim or a translucent card measures what the eye actually receives.</summary>
    public static Color Over(Color top, Color under) => new(
        top.R * top.A + under.R * (1f - top.A),
        top.G * top.A + under.G * (1f - top.A),
        top.B * top.A + under.B * (1f - top.A),
        1f);

    /// <summary>The floor for ordinary text.</summary>
    public const float ContrastFloor = 4.5f;

    /// <summary>The floor for body text at night. Dark reading is measurably worse at 4.5:1, so
    /// the night temperature is held to a higher bar — "a night screen that fails contrast
    /// floors is not moody, it is broken" (kill rule 3).</summary>
    public const float NightBodyFloor = 6.0f;
}
