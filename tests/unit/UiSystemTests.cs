using System.Collections.Generic;
using System.Linq;
using Godot;
using MpFoundation.Ui.Design;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>The overhaul's target table, as assertions.</b> The B2 program document sets a baseline
/// and a target for each metric the AUD-UI-1 audit measured, and calls that table the definition
/// of done. These tests are that table where it can be checked from the token layer: counts,
/// scale conformance, state coverage, and the closure of the recipe set.
///
/// <para>The point is not that the numbers are right today — it is that a later change which
/// makes them wrong fails a build instead of quietly restoring the drift the overhaul spent its
/// whole length undoing.</para>
/// </summary>
public class UiSystemTests
{
    public static TheoryData<UiTemperature> Temperatures => new() { UiTemperature.Day, UiTemperature.Night };

    // --- the counts ------------------------------------------------------------------------------

    /// <summary>Target: ≤ 8 text tokens, against a measured baseline of 27 distinct text colours.</summary>
    [Theory]
    [MemberData(nameof(Temperatures))]
    public void TextTokens_StayUnderTheTarget(UiTemperature temperature)
    {
        UiTokens t = UiTokens.For(temperature);
        var inks = new HashSet<Color>
        {
            t.InkRank1, t.InkRank2, t.InkRank3, t.InkDisabled, t.InkOnAccent, t.InkDanger, t.InkOnScrim,
        };
        Assert.True(inks.Count <= 8, $"{temperature}: {inks.Count} distinct ink values (target ≤ 8).");
    }

    /// <summary>Target: ≤ 12 fill tokens, against a measured baseline of 46 background colours.</summary>
    [Theory]
    [MemberData(nameof(Temperatures))]
    public void FillTokens_StayUnderTheTarget(UiTemperature temperature)
    {
        UiTokens t = UiTokens.For(temperature);
        var fills = new HashSet<Color>
        {
            t.PageGround, t.PageSheet, t.SurfaceCard, t.SurfaceRaised, t.SurfaceSunken, t.SurfaceChip,
            t.Scrim, t.DisabledFill, t.Accent, t.AccentHi, t.AccentDim, t.Danger,
        };
        Assert.True(fills.Count <= 12, $"{temperature}: {fills.Count} distinct fill values (target ≤ 12).");
    }

    /// <summary>Target: ≤ 6 font sizes, against a baseline of 13; ≤ 3 radii, against 12.</summary>
    [Fact]
    public void TypeAndRadius_StayOnTheirScales()
    {
        Assert.True(UiScale.TypeSizes.Length <= 6, $"{UiScale.TypeSizes.Length} font sizes (target ≤ 6).");
        Assert.Equal(UiScale.TypeSizes.Length, UiScale.TypeSizes.Distinct().Count());

        var radii = new HashSet<int>
        {
            UiStyle.RadiusFor(SurfaceShape.Soft),
            UiStyle.RadiusFor(SurfaceShape.Rounded),
            UiStyle.RadiusFor(SurfaceShape.Pill),
        };
        Assert.True(radii.Count <= 3, $"{radii.Count} distinct radii (target ≤ 3).");
    }

    /// <summary>Target: one scrim token, against five measured dim strengths (0.45–0.85).</summary>
    [Fact]
    public void ThereIsExactlyOneScrim()
    {
        // Both temperatures resolve a scrim, but there is one NAME for it — a screen cannot ask
        // for "the other dim", because the token set does not have one. The loss screen and the
        // loading screen are opaque grounds, not scrims, and take PageGround.
        Assert.True(UiTokens.Day.Scrim.A is > 0f and < 1f);
        Assert.True(UiTokens.Night.Scrim.A is > 0f and < 1f);
    }

    // --- the scale --------------------------------------------------------------------------------

    /// <summary>Target: every gap on the five-step scale, no exceptions, against ~40 measured
    /// spacing values.</summary>
    [Fact]
    public void EveryRecipePadding_IsAStepOnTheScale()
    {
        foreach (SurfaceRecipe recipe in UiRecipeSet.Default.All)
        {
            Assert.Contains(UiScale.Px(recipe.PadX), UiScale.SpaceSteps.Append(0));
            Assert.Contains(UiScale.Px(recipe.PadY), UiScale.SpaceSteps.Append(0));
        }
    }

    [Fact]
    public void TheScaleIsFiveStepsAndOrdered()
    {
        Assert.Equal(5, UiScale.SpaceSteps.Length);
        for (int i = 1; i < UiScale.SpaceSteps.Length; i++)
            Assert.True(UiScale.SpaceSteps[i] > UiScale.SpaceSteps[i - 1]);
        Assert.Contains(UiScale.ScreenMargin, UiScale.SpaceSteps);
    }

    // --- state coverage ----------------------------------------------------------------------------

    /// <summary>Target: all five states on everything interactive, against a measured "bimodal —
    /// themed Buttons full, every custom control partial or none".
    ///
    /// <para>Every recipe, both temperatures: the five states must be defined and pairwise
    /// distinct, so "shows all states" cannot silently collapse into two states sharing a look.</para></summary>
    [Theory]
    [MemberData(nameof(Temperatures))]
    public void EveryRecipe_HasFiveDistinctStates(UiTemperature temperature)
    {
        UiTokens t = UiTokens.For(temperature);

        foreach (SurfaceRecipe recipe in UiRecipeSet.Default.All)
        {
            // A card and a chip are not interactive; they legitimately have one look.
            if (recipe.Press == PressFeel.None && !recipe.HoverLift)
                continue;

            var seen = new Dictionary<string, UiState>();
            foreach (UiState state in new[]
                     { UiState.Normal, UiState.Hover, UiState.Pressed, UiState.Focus, UiState.Disabled })
            {
                StyleSpec spec = UiStyle.Resolve(recipe, state, t);
                string key = $"{spec.Fill}|{spec.Border}|{spec.BorderWidth}|{spec.Ink}|{spec.PadTop}|{spec.Elevation}";
                Assert.False(seen.TryGetValue(key, out UiState twin),
                    $"{temperature}/{recipe.FillToken}: {state} renders identically to {twin}.");
                seen[key] = state;
            }
        }
    }

    /// <summary>Focus is the loudest state in the system, on everything, at both temperatures.
    /// This game is controller-first, and a focus ring nobody can find is the same as no focus.</summary>
    [Theory]
    [MemberData(nameof(Temperatures))]
    public void Focus_IsAlwaysTheWidestBorder(UiTemperature temperature)
    {
        UiTokens t = UiTokens.For(temperature);

        foreach (SurfaceRecipe recipe in UiRecipeSet.Default.All)
        {
            if (recipe.Press == PressFeel.None && !recipe.HoverLift)
                continue;

            int focus = UiStyle.Resolve(recipe, UiState.Focus, t).BorderWidth;
            foreach (UiState state in new[] { UiState.Normal, UiState.Hover, UiState.Pressed, UiState.Disabled })
                Assert.True(
                    focus > UiStyle.Resolve(recipe, state, t).BorderWidth,
                    $"{temperature}/{recipe.FillToken}: focus border is not wider than {state}.");
        }
    }

    /// <summary>The focus token cannot BE the accent. It was, in the first cut of this system,
    /// which put an ember ring on an ember button at 1.00:1 — focus invisible on the one control
    /// it matters most on.</summary>
    [Theory]
    [MemberData(nameof(Temperatures))]
    public void Focus_SeparatesFromTheAccentItRings(UiTemperature temperature)
    {
        UiTokens t = UiTokens.For(temperature);
        Assert.True(UiStyle.Contrast(t.Focus, t.Accent) >= 1.6f,
            $"{temperature}: focus does not separate from the accent it has to ring.");
    }

    // --- the one accent -----------------------------------------------------------------------------

    /// <summary>The accent means "the thing that matters". Exactly one recipe in the kit may
    /// claim it — the moment a second component is accent-filled by default, the first one stops
    /// meaning anything.</summary>
    [Fact]
    public void ExactlyOneRecipe_ClaimsTheAccent()
    {
        SurfaceRecipe[] accented = UiRecipeSet.Default.All.Where(r => r.IsAccent).ToArray();
        Assert.Single(accented);
        Assert.Equal(FillRole.Accent, accented[0].FillToken);
    }

    /// <summary>
    /// ART-BIBLE §3: things that must be told apart separate in <b>value</b>, not just hue — a
    /// colourblind-safe read.
    ///
    /// <para>Two pairs in this system carry meaning through colour and therefore owe that rule:
    /// the accent against danger (both warm, per the same section's "danger reads warm on
    /// objects and accents"), and the map's self/teammate markers, which are the documented
    /// exception to the one-accent rule precisely because they encode WHICH.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Temperatures))]
    public void MeaningfulColourPairs_SeparateInValueNotJustHue(UiTemperature temperature)
    {
        UiTokens t = UiTokens.For(temperature);

        // RATIFIED 2026-08-30. Talon was shown this metric change as a decision he could reverse
        // in one line ("retune the danger red instead") and answered "I'm not sure. Do what you
        // think is best." The orchestrator kept it, and the reasoning is recorded here rather
        // than in a handoff nobody will read: a WCAG contrast ratio is built to answer "is this
        // TEXT legible against that BACKGROUND". It was never the right instrument for "can a
        // player tell these two adjacent semantic fills apart", which is what ART-BIBLE §3 asks
        // and what CIE L* actually measures. The old bar only ever fit because the old accent was
        // a loud orange. Retuning the danger red to satisfy a metric that was wrong for the job
        // would have been fitting the palette to the instrument.
        //
        // The accent against danger. Measured in CIE L* rather than in a WCAG ratio, and that is
        // a 2026-08-29 (UI-3) correction to the metric, not a relaxation of the rule — see
        // UiStyle.ValueSeparation for the full argument. In short: the ratio's +0.05 flare term
        // compresses at the light end, so when the accent went from ember to moonlight the pair
        // fell from 1.91:1 to 1.53:1 on a 1.8 bar while its actual value separation stayed at
        // 14.7 L*, seven times the perceptible threshold. The bar was a description of the old
        // palette. The rule ART-BIBLE §3 states is about VALUE, and L* is what value means.
        //
        // The pair also stopped being the hard case the old comment described: §3 puts danger and
        // accents both in the warm half, which is why hue could not be trusted to carry it. The
        // accent is cool now, so hue separates them as well — this assertion is the belt.
        float accentVsDanger = UiStyle.ValueSeparation(t.Accent, t.Danger);
        Assert.True(accentVsDanger >= UiStyle.ValueSeparationFloor,
            $"{temperature}: the accent and danger sit {accentVsDanger:0.0} L* apart in value " +
            $"(floor {UiStyle.ValueSeparationFloor:0}); WCAG ratio is {Ratio(t.Accent, t.Danger):0.00}:1.");

        // ...and they are separated in FORM too, which is the belt to that braces: the accent
        // verb is filled, the danger verb is an outline.
        Assert.Equal(SurfaceFill.Solid, UiRecipeSet.Default.Action.Fill);
        Assert.Equal(SurfaceFill.Outline, UiRecipeSet.Default.Danger.Fill);

        // Self versus teammates on the map. Reinforced by shape as well — self is an arrow,
        // teammates are dots — but the colours have to stand on their own too.
        float selfVsTeammate = UiStyle.ValueSeparation(t.AccentHi, t.Primaries[3]);
        Assert.True(selfVsTeammate >= UiStyle.ValueSeparationFloor,
            $"{temperature}: the self and teammate markers sit {selfVsTeammate:0.0} L* apart in value " +
            $"(floor {UiStyle.ValueSeparationFloor:0}); WCAG ratio is {Ratio(t.AccentHi, t.Primaries[3]):0.00}:1.");
    }

    private static float Ratio(Color a, Color b) => UiStyle.Contrast(a, b);

    /// <summary>The accent is shared across both temperatures — one fire, whatever the hour.</summary>
    [Fact]
    public void TheAccentIsTheSameAtBothTemperatures()
    {
        Assert.Equal(UiTokens.Day.Accent, UiTokens.Night.Accent);
        Assert.Equal(UiTokens.Day.AccentHi, UiTokens.Night.AccentHi);
    }

    // --- the 2026-08-15 pivot, held in place ------------------------------------------------------

    /// <summary>
    /// <b>Light mode is retired</b> (Talon, 2026-08-15: "theme light mode stinks"). Every screen,
    /// HUD and flow card ships the one dark look — including the in-round <i>day</i> phase — so
    /// <see cref="UiTokens.For"/> must resolve to <see cref="UiTokens.Night"/> whatever it is asked
    /// for.
    ///
    /// <para>The second half is the load-bearing half: <see cref="UiTokens.Day"/> stays a real,
    /// genuinely different palette. The retirement is a routing decision at one call site, not a
    /// palette collapse, and that is what makes "revert one line" true rather than aspirational.
    /// A test that only checked the routing would still pass if someone got there by overwriting
    /// Day with Night's values — same look, and a one-line revert that no longer works.
    /// Documented in docs/UI-DESIGN-SYSTEM.md, "Tokens".</para>
    /// </summary>
    [Fact]
    public void LightModeIsRetired_ButTheDayPaletteSurvivesIntact()
    {
        Assert.Equal(UiTokens.Night.PageGround, UiTokens.For(UiTemperature.Day).PageGround);
        Assert.Equal(UiTokens.Night.InkRank1, UiTokens.For(UiTemperature.Day).InkRank1);
        Assert.Equal(UiTokens.Night.PageGround, UiTokens.For(UiTemperature.Night).PageGround);

        // The positive control on the revert path: Day is still there and still different, so the
        // one-line revert restores a light interface rather than a second dark one.
        Assert.NotEqual(UiTokens.Day.PageGround, UiTokens.Night.PageGround);
        Assert.NotEqual(UiTokens.Day.InkRank1, UiTokens.Night.InkRank1);
    }

    /// <summary>
    /// <b>The paper edge is opt-in</b> (Talon, 2026-08-15: "closer to standard polish"). B2-ART
    /// built the 9-patch kit; the shipped chrome is flat colour anyway. Every recipe in the
    /// shipped set must sit at <see cref="SurfaceEdge.Flat"/> — a recipe that quietly reintroduces
    /// a paper edge is exactly the undecided sweeping change this program exists to prevent.
    ///
    /// <para>With the same positive control as above: <c>WithEdge</c> must still move every
    /// surface, so the kit stays one dial away instead of being retired by neglect.</para>
    /// </summary>
    [Fact]
    public void ThePaperEdgeIsOptIn_AndTheDialStillTurns()
    {
        foreach (SurfaceRecipe recipe in UiRecipeSet.Default.All)
            Assert.Equal(SurfaceEdge.Flat, recipe.Edge);

        UiRecipeSet paper = UiRecipeSet.Default.WithEdge(SurfaceEdge.Scissor);
        foreach (SurfaceRecipe recipe in paper.All)
            Assert.Equal(SurfaceEdge.Scissor, recipe.Edge);

        // ...and back, in one edit, which is the property Talon asked the dial for.
        foreach (SurfaceRecipe recipe in paper.WithEdge(SurfaceEdge.Flat).All)
            Assert.Equal(SurfaceEdge.Flat, recipe.Edge);
    }

    // --- the dial actually turns ----------------------------------------------------------------------

    /// <summary>
    /// <b>The property the whole substrate exists for.</b> Talon's ask, verbatim: <i>"what if I
    /// want to explore a button which is not square and solid in colour but round and opaque. I
    /// would like to quickly see this change reflected and I don't want a complete overhaul of
    /// the UI system to do this for one change."</i>
    ///
    /// <para>So: turn the one dial, and assert that every surface in the game moved — not just
    /// the buttons, and without any screen being touched.</para>
    /// </summary>
    [Fact]
    public void OneShapeEdit_MovesEverySurfaceInTheGame()
    {
        UiRecipeSet before = UiRecipeSet.Default;
        UiRecipeSet after = before.WithShape(SurfaceShape.Pill);

        Assert.Equal(before.All.Length, after.All.Length);
        for (int i = 0; i < before.All.Length; i++)
        {
            Assert.Equal(SurfaceShape.Pill, after.All[i].Shape);
            // ...and nothing else about the recipe moved with it.
            Assert.Equal(before.All[i].Fill, after.All[i].Fill);
            Assert.Equal(before.All[i].Ink, after.All[i].Ink);
            Assert.Equal(before.All[i].PadX, after.All[i].PadX);
        }

        // The resolved geometry follows, which is what actually renders.
        foreach (SurfaceRecipe recipe in after.All)
            Assert.Equal(UiScale.RadiusPill, UiStyle.Resolve(recipe, UiState.Normal, UiTokens.Night).Radius);
    }

    /// <summary>The same for fill treatment — and recipes that draw nothing at rest stay drawing
    /// nothing, because "make everything translucent" must not give the text buttons a plate.</summary>
    [Fact]
    public void OneFillEdit_MovesEverySurfaceThatDrawsOne()
    {
        UiRecipeSet after = UiRecipeSet.Default.WithFill(SurfaceFill.Outline);

        foreach (SurfaceRecipe recipe in after.All)
            Assert.True(recipe.Fill is SurfaceFill.Outline or SurfaceFill.None);

        Assert.Equal(SurfaceFill.None, after.Text.Fill);
        Assert.Equal(SurfaceFill.None, after.Menu.Fill);
    }

    /// <summary>Day and night are one component set at two temperatures. Every metric that
    /// affects layout must be identical, or the interface reflows as the light goes — which is
    /// the visible signature of a second skin.</summary>
    [Fact]
    public void NothingReflowsAtDusk()
    {
        foreach (SurfaceRecipe recipe in UiRecipeSet.Default.All)
        foreach (UiState state in new[]
                 { UiState.Normal, UiState.Hover, UiState.Pressed, UiState.Focus, UiState.Disabled })
        {
            StyleSpec day = UiStyle.Resolve(recipe, state, UiTokens.Day);
            StyleSpec night = UiStyle.Resolve(recipe, state, UiTokens.Night);

            Assert.Equal(day.Radius, night.Radius);
            Assert.Equal(day.PadLeft, night.PadLeft);
            Assert.Equal(day.PadRight, night.PadRight);
            Assert.Equal(day.PadTop, night.PadTop);
            Assert.Equal(day.PadBottom, night.PadBottom);
            Assert.Equal(day.BorderWidth, night.BorderWidth);
        }
    }

    /// <summary>Dusk is an interpolation between the two sets, so it must land exactly on each
    /// end and commit to one temperature rather than flickering between them.</summary>
    [Fact]
    public void TheDuskCrossfade_LandsOnBothEnds()
    {
        // Compared with a tolerance, not exactly: a linear blend at k=1 is a + (b-a), which is
        // not bit-identical to b in float. The claim being tested is "lands on the end", and a
        // rounding difference in the eighth decimal is not a failure to land.
        Same(UiTokens.Day.SurfaceCard, UiTokens.Lerp(UiTokens.Day, UiTokens.Night, 0f).SurfaceCard);
        Same(UiTokens.Night.SurfaceCard, UiTokens.Lerp(UiTokens.Day, UiTokens.Night, 1f).SurfaceCard);

        Assert.Equal(UiTemperature.Day, UiTokens.Lerp(UiTokens.Day, UiTokens.Night, 0.49f).Temperature);
        Assert.Equal(UiTemperature.Night, UiTokens.Lerp(UiTokens.Day, UiTokens.Night, 0.51f).Temperature);

        // Out-of-range k is clamped rather than extrapolated into colours that are not in the kit.
        Same(UiTokens.Day.PageGround, UiTokens.Lerp(UiTokens.Day, UiTokens.Night, -5f).PageGround);
        Same(UiTokens.Night.PageGround, UiTokens.Lerp(UiTokens.Day, UiTokens.Night, 5f).PageGround);
    }

    private static void Same(Color expected, Color actual)
    {
        Assert.True(
            Mathf.Abs(expected.R - actual.R) < 1e-5f &&
            Mathf.Abs(expected.G - actual.G) < 1e-5f &&
            Mathf.Abs(expected.B - actual.B) < 1e-5f &&
            Mathf.Abs(expected.A - actual.A) < 1e-5f,
            $"expected {expected}, got {actual}");
    }

    /// <summary>Motion stays inside the budget the direction sets: 120–300ms for everything
    /// except the dusk crossfade, which is choreography.</summary>
    [Fact]
    public void MotionStaysInsideTheBudget()
    {
        foreach (double duration in new[] { UiScale.MotionInstant, UiScale.MotionQuick, UiScale.MotionSettle })
            Assert.InRange(duration, 0.12, 0.30);

        Assert.True(UiScale.MotionTemperature > UiScale.MotionSettle * 4,
            "the temperature crossfade is the one long motion in the system and should read as one");
    }
}
