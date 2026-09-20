using Godot;
using MpFoundation.Ui.Design;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The overhaul's contrast target, as a test.</b> The AUD-UI-1 audit measured the shipped
/// interface at 3.28:1 (main-menu planks) and 3.36–3.79:1 (disabled text on all three
/// surfaces). The B2 acceptance bar is "0 pairs below 4.5:1; night body text ≥ ~6:1".
///
/// <para>These run against the real <see cref="UiTokens"/> and the real <see cref="UiStyle"/>
/// resolver — not a copy of the palette — so a token edit that breaks a floor fails the build
/// rather than shipping as "moody". That is kill rule 3 made mechanical: <i>a night screen
/// that fails contrast floors is not moody, it is broken.</i></para>
/// </summary>
public class UiContrastTests
{
    public static TheoryData<UiTemperature> Temperatures => new() { UiTemperature.Day, UiTemperature.Night };

    /// <summary>Every ink rank against every surface it can legally be drawn on. This is the
    /// whole matrix, not a spot check: the audit's failures were all pairs nobody had checked.</summary>
    [Theory]
    [MemberData(nameof(Temperatures))]
    public void EveryInkOnEverySurface_ClearsTheFloor(UiTemperature temperature)
    {
        UiTokens t = UiTokens.For(temperature);

        (string Name, Color Value)[] surfaces =
        {
            ("PageGround", t.PageGround),
            ("PageSheet", t.PageSheet),
            ("SurfaceCard", t.SurfaceCard),
            ("SurfaceRaised", t.SurfaceRaised),
            ("SurfaceSunken", t.SurfaceSunken),
            ("SurfaceChip", t.SurfaceChip),
        };

        (string Name, Color Value)[] inks =
        {
            ("InkRank1", t.InkRank1),
            ("InkRank2", t.InkRank2),
            ("InkRank3", t.InkRank3),
            ("InkDisabled", t.InkDisabled),
        };

        foreach ((string sName, Color surface) in surfaces)
        foreach ((string iName, Color ink) in inks)
        {
            float ratio = UiStyle.Contrast(ink, surface);
            Assert.True(
                ratio >= UiStyle.ContrastFloor,
                $"{temperature}: {iName} on {sName} is {ratio:0.00}:1, below the {UiStyle.ContrastFloor}:1 floor.");
        }
    }

    /// <summary>Disabled text is the audit's named failure (3.36–3.79:1). The fix is not a
    /// brighter grey bolted on — it is that "inert" is carried by the material (untaped,
    /// unfilled, borderless), so the ink is free to stay legible.</summary>
    [Theory]
    [MemberData(nameof(Temperatures))]
    public void DisabledText_IsInertButNeverIllegible(UiTemperature temperature)
    {
        UiTokens t = UiTokens.For(temperature);
        float onCard = UiStyle.Contrast(t.InkDisabled, t.SurfaceCard);

        Assert.True(onCard >= UiStyle.ContrastFloor, $"{temperature}: disabled ink is {onCard:0.00}:1 on a card.");

        // ...and it must still be visibly quieter than body text, or "disabled" stops reading.
        float body = UiStyle.Contrast(t.InkRank2, t.SurfaceCard);
        Assert.True(body > onCard, $"{temperature}: disabled ink ({onCard:0.00}) is not quieter than body ({body:0.00}).");
    }

    /// <summary>Night body text is held above the ordinary floor. Dark reading is measurably
    /// worse at 4.5:1, and the direction brief sets ~6:1 for exactly this reason.</summary>
    [Fact]
    public void NightBodyText_ClearsTheHigherNightFloor()
    {
        UiTokens t = UiTokens.Night;

        foreach ((string name, Color surface) in new[]
                 {
                     ("PageGround", t.PageGround),
                     ("PageSheet", t.PageSheet),
                     ("SurfaceCard", t.SurfaceCard),
                     ("SurfaceRaised", t.SurfaceRaised),
                 })
        {
            float ratio = UiStyle.Contrast(t.InkRank2, surface);
            Assert.True(
                ratio >= UiStyle.NightBodyFloor,
                $"Night body ink on {name} is {ratio:0.00}:1, below the {UiStyle.NightBodyFloor}:1 night floor.");
        }
    }

    /// <summary>White on ember is ~2.6:1 and fails everything — the shipped kit did exactly
    /// that. <see cref="UiTokens.InkOnAccent"/> exists so the accent's text colour is a decided
    /// token rather than a per-button guess.</summary>
    [Theory]
    [MemberData(nameof(Temperatures))]
    public void TextOnTheAccent_ClearsTheFloorInEveryAccentState(UiTemperature temperature)
    {
        UiTokens t = UiTokens.For(temperature);

        foreach ((string name, Color fill) in new[] { ("Accent", t.Accent), ("AccentHi", t.AccentHi), ("AccentDim", t.AccentDim) })
        {
            float ratio = UiStyle.Contrast(t.InkOnAccent, fill);
            Assert.True(ratio >= UiStyle.ContrastFloor, $"{temperature}: accent ink on {name} is {ratio:0.00}:1.");
        }

        // The defect this token replaces, pinned so nobody reintroduces it by "just using white".
        Assert.True(UiStyle.Contrast(Colors.White, t.Accent) < UiStyle.ContrastFloor);
    }

    /// <summary>The one scrim has to actually carry text, at both temperatures, over the worst
    /// ground it can be laid on. A scrim that only works over a dark frame is not a scrim.</summary>
    [Theory]
    [MemberData(nameof(Temperatures))]
    public void TheOneScrim_CarriesTextOverTheBrightestGround(UiTemperature temperature)
    {
        UiTokens t = UiTokens.For(temperature);

        // The brightest thing the game can put behind a scrim: a sunlit frame.
        Color worstCase = UiStyle.Over(t.Scrim, Colors.White);
        float ratio = UiStyle.Contrast(t.InkOnScrim, worstCase);

        Assert.True(
            ratio >= UiStyle.ContrastFloor,
            $"{temperature}: scrim ink over a white frame is {ratio:0.00}:1.");
    }

    /// <summary>The resolver's own output, walked exhaustively: every recipe, every state, both
    /// temperatures. Ink against the fill it is actually drawn on — including the hover and
    /// pressed fills, which is where hand-authored states historically broke.</summary>
    [Theory]
    [MemberData(nameof(Temperatures))]
    public void EveryRecipeInEveryState_IsLegible(UiTemperature temperature)
    {
        UiTokens t = UiTokens.For(temperature);
        UiRecipeSet set = UiRecipeSet.Default;

        foreach (SurfaceRecipe recipe in set.All)
        foreach (UiState state in new[] { UiState.Normal, UiState.Hover, UiState.Pressed, UiState.Focus, UiState.Disabled })
        {
            StyleSpec spec = UiStyle.Resolve(recipe, state, t);

            // What the ink is really read against: the resolved fill if it is opaque, otherwise
            // the fill composited over the card the component sits on.
            Color ground = spec.Fill.A >= 0.999f
                ? spec.Fill
                : UiStyle.Over(spec.Fill, t.SurfaceCard);

            float ratio = UiStyle.Contrast(spec.Ink, ground);
            Assert.True(
                ratio >= UiStyle.ContrastFloor,
                $"{temperature}: {recipe.FillToken}/{recipe.Ink} in {state} is {ratio:0.00}:1.");
        }
    }
}
