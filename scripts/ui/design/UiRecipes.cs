namespace MpFoundation.Ui.Design;

/// <summary>How a surface's corners are cut.</summary>
public enum SurfaceShape : byte
{
    /// <summary>Square. A sheet of paper trimmed on a guillotine.</summary>
    Square = 0,

    /// <summary>Barely rounded — a scissor-cut corner.</summary>
    Soft = 1,

    /// <summary>A rounded cutout card.</summary>
    Rounded = 2,

    /// <summary>Fully round: a punched-out oval.</summary>
    Pill = 3,
}

/// <summary>How a surface is filled.</summary>
public enum SurfaceFill : byte
{
    /// <summary>Nothing drawn. Text sitting directly on the page.</summary>
    None = 0,

    /// <summary>Border only — a shape cut out and the page showing through.</summary>
    Outline = 1,

    /// <summary>Filled, but the ground reads through it. Paper laid over paper.</summary>
    Translucent = 2,

    /// <summary>Fully opaque. A cutout glued flat.</summary>
    Solid = 3,
}

/// <summary>The paper edge treatment. Semantics, not decoration — <see cref="Singed"/> means
/// <i>danger or loss touched this</i> and is spent at most once per screen.</summary>
public enum SurfaceEdge : byte
{
    /// <summary>The default scissor cut.</summary>
    Scissor = 0,

    /// <summary>Torn by hand. Rarer; used where something was done in a hurry.</summary>
    Torn = 1,

    /// <summary>Singed. Fire touched this. Once per screen, at most, and it always means loss.</summary>
    Singed = 2,

    /// <summary>No paper. <see cref="UiThemeFactory.Box"/>'s flat-colour path, unchanged from
    /// before this kit existed. This is Talon's reversibility dial (2026-08-15: "the option to
    /// make it easy... to move to / back to something else in the future") — flipping every
    /// recipe to <see cref="Flat"/> and back is <see cref="UiRecipeSet.WithEdge"/>, the same
    /// shape as <see cref="UiRecipeSet.WithShape"/> and <see cref="UiRecipeSet.WithFill"/>.</summary>
    Flat = 3,
}

/// <summary>What a surface does under the finger.</summary>
public enum PressFeel : byte
{
    /// <summary>Nothing moves.</summary>
    None = 0,

    /// <summary>Content shifts down: the paper is pressed into the page.</summary>
    Depress = 1,

    /// <summary>The lift collapses: a raised cutout pressed flat.</summary>
    Flatten = 2,
}

/// <summary>Which fill token a surface paints with.</summary>
public enum FillRole : byte { None, Ground, Sheet, Card, Raised, Sunken, Chip, Accent, Danger }

/// <summary>Which ink token a surface's text takes.</summary>
public enum InkRole : byte { Rank1, Rank2, Rank3, OnAccent, Danger }

/// <summary>
/// <b>One component's look, described entirely in dials.</b> Nothing here is a pixel value or
/// a colour — it is a set of choices ("round", "translucent", "loose padding", "presses down"),
/// and <see cref="UiStyle"/> resolves those choices against the current <see cref="UiTokens"/>
/// into something Godot can draw.
///
/// <para><b>This is the layer Talon asked for.</b> To explore a button that is round and
/// translucent instead of square and solid, the edit is one line:</para>
/// <code>
/// UiRecipes.Current = UiRecipes.Current with
/// {
///     Action = UiRecipes.Current.Action with { Shape = SurfaceShape.Pill, Fill = SurfaceFill.Translucent },
/// };
/// </code>
/// <para>...and every button in the game — menu, dialog, HUD, tally, loss screen, at both
/// temperatures, in all five states — is round and translucent on the next rebuild. No screen
/// is touched, because no screen holds a shape.</para>
/// </summary>
public sealed record SurfaceRecipe
{
    public required SurfaceShape Shape { get; init; }
    public required SurfaceFill Fill { get; init; }
    public required FillRole FillToken { get; init; }
    public required InkRole Ink { get; init; }

    /// <summary>Paper edge treatment. Defaults to <see cref="SurfaceEdge.Flat"/> — the shipped
    /// kit is standard flat-colour chrome (Talon, 2026-08-15: "closer to standard polish"), and
    /// the paper 9-patches remain available as an opt-in per recipe via <c>WithEdge</c> rather
    /// than the default look.</summary>
    public SurfaceEdge Edge { get; init; } = SurfaceEdge.Flat;

    /// <summary>Opacity when <see cref="Fill"/> is <see cref="SurfaceFill.Translucent"/>.</summary>
    public float FillAlpha { get; init; } = 0.14f;

    /// <summary>Border thickness at rest. Focus always thickens it — see
    /// <see cref="UiScale.BorderFocus"/>.</summary>
    public int BorderWidth { get; init; } = UiScale.BorderHair;

    /// <summary>Horizontal padding, as a step on the scale.</summary>
    public Space PadX { get; init; } = Space.Loose;

    /// <summary>Vertical padding, as a step on the scale.</summary>
    public Space PadY { get; init; } = Space.Snug;

    /// <summary>Minimum height. Anything a controller lands on takes
    /// <see cref="UiScale.TargetHeight"/>.</summary>
    public int MinHeight { get; init; }

    /// <summary>Whether hover lifts the paper (fill and border strengthen, shadow grows).</summary>
    public bool HoverLift { get; init; } = true;

    public PressFeel Press { get; init; } = PressFeel.Depress;

    /// <summary>Drop shadow radius at rest. Zero for anything flat on the page.</summary>
    public int Elevation { get; init; }

    /// <summary><b>The accent claim.</b> Exactly one component per screen may set this — it is
    /// the screen's single ember instance, and it means what the game means by light. Enforced
    /// by review, and by the fact that only one recipe in <see cref="UiRecipeSet"/> carries an
    /// <see cref="FillRole.Accent"/> fill.</summary>
    public bool IsAccent { get; init; }
}

/// <summary>
/// <b>Every component recipe in the game, in one object.</b> The kit is closed: a screen
/// composes from these and only these, and a new visual element enters here deliberately —
/// one PR, one review — or not at all (PAPER &amp; FIRELIGHT kill rule 1).
///
/// <para>Swap the whole set, or one recipe in it, and rebuild: see
/// <c>UiThemeService.Rebuild</c>.</para>
/// </summary>
public sealed record UiRecipeSet
{
    /// <summary>The primary verb. The screen's one ember instance — the thing the player came
    /// to this screen to do. The audit's finding 4 was this rendered at breadcrumb weight on
    /// Host/Join; a recipe cannot make that mistake, because rank is not a per-screen choice.</summary>
    public required SurfaceRecipe Action { get; init; }

    /// <summary>The quiet alternative — Back, Cancel, the other option.</summary>
    public required SurfaceRecipe Quiet { get; init; }

    /// <summary>An inline verb with no chrome at rest.</summary>
    public required SurfaceRecipe Text { get; init; }

    /// <summary>The destructive verb — Quit, Leave, Delete.</summary>
    public required SurfaceRecipe Danger { get; init; }

    /// <summary>The big front-of-house menu items.</summary>
    public required SurfaceRecipe Menu { get; init; }

    /// <summary>A content card or panel.</summary>
    public required SurfaceRecipe Card { get; init; }

    /// <summary>A surface lifted above a card — dropdown, popup, tooltip.</summary>
    public required SurfaceRecipe Popup { get; init; }

    /// <summary>A text input.</summary>
    public required SurfaceRecipe Field { get; init; }

    /// <summary>A small tag: a count, a name, a state word.</summary>
    public required SurfaceRecipe Chip { get; init; }

    /// <summary>A HUD scrap — taped to the corner of the frame, over live 3D.</summary>
    public required SurfaceRecipe HudScrap { get; init; }

    /// <summary>The mutable live set. Assign a modified copy and rebuild to restyle the game.</summary>
    public static UiRecipeSet Current { get; set; } = Default;

    /// <summary>
    /// The shipped composition: cut-paper cards, scissor-soft actions, one ember verb.
    /// </summary>
    public static UiRecipeSet Default => new()
    {
        // UI-3, 2026-08-29 — THE THREE BUTTON PLATES TOOK THE MAIN MENU'S CORNER. Talon, note 5:
        // "Please make the rest of the menus match this main menu UI... with the same accents and
        // buttons and things." The accent half of that is a token change (UiTokens); this is the
        // other half. MenuLook.ButtonRadius is 10 in a 1600x900 design frame scaled by
        // viewport-height/900, so at 1080p the title screen's Host button renders a 12 px corner —
        // exactly SurfaceShape.Rounded. Action, Quiet and Danger were Soft (4 px), which is what
        // made a lobby button read as a different object from the menu button that opened it.
        //
        // Deliberately NOT changed with it: the 1 px resting border on the accent plate. The main
        // menu has no ring at rest, and MenuLook explains why (a button that wears a ring at rest
        // already looks focused, leaving nothing to express real focus with) — but here the ring
        // is AccentHi at 55% over the Accent plate, i.e. a hairline of the accent's own next ramp
        // step, which is not the ember ring that reasoning was written about. Removing it would
        // also break UiKitStateTests' "every state has a border" invariant for no visible gain.
        Action = new SurfaceRecipe
        {
            Shape = SurfaceShape.Rounded,
            Fill = SurfaceFill.Solid,
            FillToken = FillRole.Accent,
            Ink = InkRole.OnAccent,
            BorderWidth = UiScale.BorderHair,
            PadX = Space.Loose,
            PadY = Space.Normal,
            MinHeight = UiScale.TargetHeight,
            Press = PressFeel.Depress,
            Elevation = 6,
            IsAccent = true,
        },

        Quiet = new SurfaceRecipe
        {
            Shape = SurfaceShape.Rounded,
            Fill = SurfaceFill.Outline,
            FillToken = FillRole.Card,
            Ink = InkRole.Rank2,
            BorderWidth = UiScale.BorderHair,
            PadX = Space.Loose,
            PadY = Space.Normal,
            MinHeight = UiScale.TargetHeight,
            Press = PressFeel.Depress,
        },

        Text = new SurfaceRecipe
        {
            Shape = SurfaceShape.Soft,
            Fill = SurfaceFill.None,
            FillToken = FillRole.None,
            Ink = InkRole.Rank3,
            BorderWidth = 0,
            PadX = Space.Snug,
            PadY = Space.Tight,
            MinHeight = UiScale.TargetHeight,
            Press = PressFeel.Depress,
        },

        Danger = new SurfaceRecipe
        {
            Shape = SurfaceShape.Rounded,
            Fill = SurfaceFill.Outline,
            FillToken = FillRole.Danger,
            Ink = InkRole.Danger,
            BorderWidth = UiScale.BorderHair,
            PadX = Space.Loose,
            PadY = Space.Normal,
            MinHeight = UiScale.TargetHeight,
            Press = PressFeel.Depress,
        },

        Menu = new SurfaceRecipe
        {
            Shape = SurfaceShape.Soft,
            Fill = SurfaceFill.None,
            FillToken = FillRole.None,
            Ink = InkRole.Rank1,
            BorderWidth = 0,
            PadX = Space.Normal,
            PadY = Space.Snug,
            MinHeight = UiScale.TargetHeight,
            Press = PressFeel.Depress,
        },

        Card = new SurfaceRecipe
        {
            Shape = SurfaceShape.Rounded,
            Fill = SurfaceFill.Solid,
            FillToken = FillRole.Card,
            Ink = InkRole.Rank2,
            BorderWidth = UiScale.BorderHair,
            PadX = Space.Wide,
            PadY = Space.Wide,
            HoverLift = false,
            Press = PressFeel.None,
            Elevation = 18,
        },

        Popup = new SurfaceRecipe
        {
            Shape = SurfaceShape.Rounded,
            Fill = SurfaceFill.Solid,
            FillToken = FillRole.Raised,
            Ink = InkRole.Rank2,
            BorderWidth = UiScale.BorderHair,
            PadX = Space.Snug,
            PadY = Space.Snug,
            HoverLift = false,
            Press = PressFeel.None,
            Elevation = 24,
        },

        Field = new SurfaceRecipe
        {
            Shape = SurfaceShape.Soft,
            Fill = SurfaceFill.Solid,
            FillToken = FillRole.Sunken,
            Ink = InkRole.Rank1,
            BorderWidth = UiScale.BorderHair,
            PadX = Space.Normal,
            PadY = Space.Normal,
            MinHeight = UiScale.TargetHeight,
            HoverLift = false,
            Press = PressFeel.None,
        },

        Chip = new SurfaceRecipe
        {
            Shape = SurfaceShape.Soft,
            Fill = SurfaceFill.Solid,
            FillToken = FillRole.Chip,
            Ink = InkRole.Rank3,
            BorderWidth = 0,
            PadX = Space.Snug,
            PadY = Space.Tight,
            HoverLift = false,
            Press = PressFeel.None,
        },

        HudScrap = new SurfaceRecipe
        {
            Shape = SurfaceShape.Soft,
            Fill = SurfaceFill.Translucent,
            FillToken = FillRole.Card,
            Ink = InkRole.Rank2,
            // Measured, not chosen: anything compositing over live 3D has no fixed contrast
            // pair. 0.82 is the alpha at which the card token composites to at worst a value
            // that holds rank-2 ink near 10:1 against the brightest frame the game produces.
            FillAlpha = 0.82f,
            BorderWidth = UiScale.BorderHair,
            PadX = Space.Normal,
            PadY = Space.Snug,
            HoverLift = false,
            Press = PressFeel.None,
        },
    };

    /// <summary>Every recipe in the set, for the tests that walk them.</summary>
    public SurfaceRecipe[] All => new[] { Action, Quiet, Text, Danger, Menu, Card, Popup, Field, Chip, HudScrap };

    /// <summary><b>The one-line exploration.</b> Re-cuts every corner in the game to one shape,
    /// preserving each recipe's other choices. <c>UiRecipeSet.Current = UiRecipeSet.Current
    /// .WithShape(SurfaceShape.Pill)</c> and rebuild — every button, card, field, chip and HUD
    /// scrap is round, at both temperatures, with no screen edited.</summary>
    public UiRecipeSet WithShape(SurfaceShape shape) => new()
    {
        Action = Action with { Shape = shape },
        Quiet = Quiet with { Shape = shape },
        Text = Text with { Shape = shape },
        Danger = Danger with { Shape = shape },
        Menu = Menu with { Shape = shape },
        Card = Card with { Shape = shape },
        Popup = Popup with { Shape = shape },
        Field = Field with { Shape = shape },
        Chip = Chip with { Shape = shape },
        HudScrap = HudScrap with { Shape = shape },
    };

    /// <summary>The same move for fill treatment — every surface goes outline, or translucent,
    /// or solid together. Recipes that draw nothing at rest stay drawing nothing.</summary>
    public UiRecipeSet WithFill(SurfaceFill fill)
    {
        SurfaceRecipe Apply(SurfaceRecipe r) => r.Fill == SurfaceFill.None ? r : r with { Fill = fill };
        return new UiRecipeSet
        {
            Action = Apply(Action),
            Quiet = Apply(Quiet),
            Text = Apply(Text),
            Danger = Apply(Danger),
            Menu = Apply(Menu),
            Card = Apply(Card),
            Popup = Apply(Popup),
            Field = Apply(Field),
            Chip = Apply(Chip),
            HudScrap = Apply(HudScrap),
        };
    }

    /// <summary><b>The reversibility dial (B2-ART).</b> Re-cuts every corner's EDGE treatment —
    /// paper vs flat colour — in one move, the same shape as <see cref="WithShape"/> and
    /// <see cref="WithFill"/>. <c>UiRecipeSet.Current = UiRecipeSet.Current.WithEdge(SurfaceEdge
    /// .Flat)</c> returns the whole game to flat-colour surfaces; <c>.WithEdge(SurfaceEdge
    /// .Scissor)</c> (or any paper edge) brings the paper back. No screen is touched, because no
    /// screen holds an edge.</summary>
    public UiRecipeSet WithEdge(SurfaceEdge edge) => new()
    {
        Action = Action with { Edge = edge },
        Quiet = Quiet with { Edge = edge },
        Text = Text with { Edge = edge },
        Danger = Danger with { Edge = edge },
        Menu = Menu with { Edge = edge },
        Card = Card with { Edge = edge },
        Popup = Popup with { Edge = edge },
        Field = Field with { Edge = edge },
        Chip = Chip with { Edge = edge },
        HudScrap = HudScrap with { Edge = edge },
    };
}

/// <summary>Convenience alias so call sites read <c>UiRecipes.Current.Action</c>.</summary>
public static class UiRecipes
{
    /// <summary>The live recipe set. Assign a modified copy and call
    /// <c>UiThemeService.Rebuild()</c> to see it everywhere.</summary>
    public static UiRecipeSet Current
    {
        get => UiRecipeSet.Current;
        set => UiRecipeSet.Current = value;
    }
}
