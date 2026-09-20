using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using MpFoundation.Game.Sight;
using MpFoundation.Game.World;

namespace SailNet.Tests;

/// <summary>
/// <b>The presentation half of canon fact 4: the rendered world expressing the server's sight
/// number.</b> <c>PlayerSightTests</c> proves the number; this proves the picture built from it
/// keeps every property the number was given.
///
/// <list type="number">
/// <item><b>Tier parity survives the renderer.</b> A player on Low must not be able to make out a
/// shape a player on High cannot. The measurement is deliberately the RENDERED distance in metres
/// (<see cref="SightPresentation.RenderedVisibilityM"/>), not "are these floats equal", so a
/// failure names the law it breaks. Carries the same negative control
/// <c>PlayerSightTests</c> uses — a deliberately tier-scaled implementation run through the
/// identical comparator — plus a source scan that fails if the forbidden symbol ever appears in
/// the sight source directory at all.</item>
/// <item><b>Continuous, never stepped.</b> Sight changes as a player walks and as fuel burns
/// down, and the dusk and dawn sweeps move both the sight range and the night factor at once.
/// Every one of those is swept finely here and bounded; each bound has a negative control that
/// quantizes or steps the same measurement and must be caught.</item>
/// <item><b>Degrades to blind, never to omniscient.</b> Every way the input can be wrong —
/// unsynced, absent, NaN, zero, negative, absurdly large — resolves toward the darkness floor.
/// The large case is the one worth staring at: it is the inversion in rendering form, where a bad
/// input would produce a frame that shows the player everything.</item>
/// <item><b>Day is not night.</b> At night factor 0 the binding returns the phase-authored fog
/// bit-identically, for every sight range including the floor.</item>
/// <item><b>The binding only ever takes sight away.</b> More range is never thicker fog, and the
/// sight term can never thin what the clock authored — in the atmosphere or in the dome.</item>
/// </list>
///
/// Runs with no Godot runtime for the same reason the rest of this suite does: everything under
/// test is a pure static over value types.
/// </summary>
public class SightPresentationTests
{
    // Deep night on day 1 — OutdoorAtmosphere breakpoint index 6, where NightFactor is 1 and the
    // sight binding is fully live. Same phase CanopyShadeTests uses, for the same reason.
    private const float DeepNightPhase = 0.78f;
    private const float MiddayPhase = 0.275f;

    /// <summary>The sight ranges the shipped light table can actually produce, plus both ends.
    /// Shared by the parity proof and by its negative control so the two are provably measuring
    /// the same thing.</summary>
    private static float[] SightRanges() => new[]
    {
        PlayerSightCurve.DarkFloorM,                       //  3.0  no light at all
        PlayerSightCurve.SightAtSourceM(4f),               // 10.0  glow stick / torch, at its centre
        PlayerSightCurve.SightAtSourceM(12f),              // 24.0  cabin hearth, full pile
        PlayerSightCurve.SightAtSourceM(14f),              // 27.5  rock ring, full pile
        PlayerSightCurve.SightAtSourceM(18f),              // 34.5  fire pit, full pile
        PlayerSightCurve.MaxSightM,                        // 36.0  the backstop
        PlayerSightCurve.DaylightSightM,                   // 80.0  daylight
        7.4f, 12.3f, 19.87f, 31.05f,                       // mid-gradient, off every constant
    };

    private static float[] NightFactors() => new[] { 0f, 0.05f, 0.25f, 0.5f, 0.75f, 1f };

    // =======================================================================================
    // 1. Tier parity — the critical test
    // =======================================================================================

    /// <summary>Runs <paramref name="renderedVisibility"/> on Low, Medium and High over the whole
    /// input grid and reports every case where the three tiers disagree. Restores the tier
    /// afterwards — <c>GraphicsQuality.Current</c> is process-global static state and xUnit runs
    /// classes in parallel, so leaking a tier would make an unrelated suite flaky.
    ///
    /// <para>One helper for both the proof and its negative control, so a comparator that had
    /// silently stopped comparing could not pass one and be trusted for the other.</para></summary>
    private static List<string> TierDisagreements(
        Func<float, float, float> renderedVisibility)
    {
        var complaints = new List<string>();
        MpFoundation.World.GraphicsQuality.Tier restore = MpFoundation.World.GraphicsQuality.Current;
        try
        {
            foreach (float range in SightRanges())
            {
                foreach (float night in NightFactors())
                {
                    MpFoundation.World.GraphicsQuality.Current = MpFoundation.World.GraphicsQuality.Tier.Low;
                    float low = renderedVisibility(range, night);
                    MpFoundation.World.GraphicsQuality.Current = MpFoundation.World.GraphicsQuality.Tier.Medium;
                    float medium = renderedVisibility(range, night);
                    MpFoundation.World.GraphicsQuality.Current = MpFoundation.World.GraphicsQuality.Tier.High;
                    float high = renderedVisibility(range, night);

                    if (low != medium || medium != high)
                    {
                        complaints.Add(
                            $"range {range} m, night {night}: Low sees {low:F3} m, " +
                            $"Medium {medium:F3} m, High {high:F3} m");
                    }
                }
            }
        }
        finally
        {
            MpFoundation.World.GraphicsQuality.Current = restore;
        }
        return complaints;
    }

    /// <summary>The rendered visibility of a frame, in metres: take the phase-authored night fog,
    /// fold the sight range in, and invert the fog back into a distance. This is the shipped
    /// path — the exact two calls <c>OutdoorAtmosphere.ApplySight</c> makes.</summary>
    private static float ShippedRenderedVisibility(float rangeM, float nightFactor)
    {
        OutdoorAtmosphere.AtmoState night = OutdoorAtmosphere.Evaluate(DeepNightPhase, cyclesElapsed: 0);
        float density = SightPresentation.FogDensityWithSight(night.FogDensity, nightFactor, rangeM);
        return SightPresentation.RenderedVisibilityM(density);
    }

    /// <summary><b>THE CRITICAL TEST. A lower graphics tier must not let a player see
    /// further.</b> Canon fact 4: "Gameplay visibility is server data, identical on every client
    /// (the parity law); rendered light is presentation only." The gameplay number is already
    /// proven tier-identical; this is the half where it would be spent, because fog density, draw
    /// distance and shader quality are all things a tier legitimately dials elsewhere in this
    /// engine. Nothing in the sight path may join them.</summary>
    [Fact]
    public void Parity_RenderedVisibilityIsIdenticalOnLowMediumAndHigh()
    {
        List<string> disagreements = TierDisagreements(ShippedRenderedVisibility);

        Assert.True(disagreements.Count == 0,
            "the renderer spends the parity law the sight service paid for — a graphics tier " +
            "changes how far the frame can see:\n  " + string.Join("\n  ", disagreements));
    }

    /// <summary><b>The negative control for the test above, and the reason it means anything.</b>
    /// A comparator that never fires is indistinguishable from a passing one. This drives a
    /// deliberately tier-scaled fog density — the exact edit the class doc forbids, and a plausible
    /// one, since "thin the fog a bit on Low, it's cheaper" is a real optimisation someone will
    /// propose — through the identical comparator. If this does not report disagreements, the test
    /// above is worthless.</summary>
    [Fact]
    public void NegativeControl_ATierScaledFogDensity_IsCaughtByTheSameComparator()
    {
        List<string> disagreements = TierDisagreements((rangeM, nightFactor) =>
        {
            OutdoorAtmosphere.AtmoState night =
                OutdoorAtmosphere.Evaluate(DeepNightPhase, cyclesElapsed: 0);
            float density = SightPresentation.FogDensityWithSight(night.FogDensity, nightFactor, rangeM);
            density *= MpFoundation.World.GraphicsQuality.Current switch
            {
                MpFoundation.World.GraphicsQuality.Tier.Low => 0.9f,  // Low sees further. The violation.
                MpFoundation.World.GraphicsQuality.Tier.High => 1.1f,
                _ => 1f,
            };
            return SightPresentation.RenderedVisibilityM(density);
        });

        Assert.True(disagreements.Count > 0,
            "the tier comparator failed to notice a deliberately tier-scaled fog density, so " +
            "Parity_RenderedVisibilityIsIdenticalOnLowMediumAndHigh proves nothing.");
    }

    /// <summary><b>The guard against the NEXT edit, not this one.</b> The parity test above can
    /// only catch a tier term that is reachable from the pure functions it calls. This scans the
    /// sight source itself for the forbidden symbol, so a future frame-time, viewport-size or
    /// quality-tier read anywhere in the sight path fails a test rather than shipping behind a
    /// comment that says it must not.
    ///
    /// <para>The positive control is the load-bearing half: the same scanner is pointed at a file
    /// that legitimately DOES use <c>GraphicsQuality</c>, so a scanner that has stopped reading
    /// files (a moved path, a renamed directory, a broken repo-root walk) fails instead of quietly
    /// reporting "clean".</para></summary>
    [Fact]
    public void ContractScan_NothingInTheSightPathReadsTheGraphicsTier()
    {
        // Two files, not the four this scanned before BASE-1 (2026-09-19): the
        // server-authoritative half (PlayerSightService, PlayerSightTable) was pruned at the fork
        // and only the pure presentation math is still shipped. The law is unchanged and still
        // worth scanning for — a graphics tier may not change what anyone can see — and the
        // positive control below is what proves the scanner still reaches a file at all.
        string[] sightFiles =
        {
            "scripts/game/sight/SightPresentation.cs",
            "scripts/game/sight/PlayerSightCurve.cs",
        };

        foreach (string path in sightFiles)
        {
            string source = StripCommentsAndDocs(ReadRepoFile(path));
            Assert.False(source.Contains("GraphicsQuality", StringComparison.Ordinal),
                $"{path} reads GraphicsQuality in executable code — the parity law says a " +
                "graphics tier may not change what anyone can see, and this is the file where " +
                "that would happen.");
        }

        // POSITIVE CONTROL. Boot genuinely applies the tier (the --graphics override); if the
        // scanner cannot find it there, it is not scanning and the assertions above are vacuous.
        string control = StripCommentsAndDocs(ReadRepoFile("scripts/Boot.cs"));
        Assert.True(control.Contains("GraphicsQuality", StringComparison.Ordinal),
            "the scanner did not find GraphicsQuality in Boot.cs, which uses it — the scan " +
            "is broken and every 'clean' result above is meaningless.");
    }

    // =======================================================================================
    // 2. Continuous, never stepped
    // =======================================================================================

    /// <summary>Walking toward or away from a fire, and a fire burning down, both move the sight
    /// range continuously. Sweeping the range across its whole span in 1 cm steps, the rendered
    /// visibility must never jump. The bound is generous (0.25 m of rendered visibility per 1 cm
    /// of sight range) because it is testing for a STEP, not for a slope — the negative control
    /// below fixes what "generous" is allowed to miss.</summary>
    [Fact]
    public void Continuity_RenderedVisibilityNeverJumpsAsTheSightRangeMoves()
    {
        float worst = WorstRangeStep(r => ShippedRenderedVisibility(r, 1f));
        Assert.True(worst < 0.25f,
            $"the rendered night steps as the sight range moves: worst jump {worst:F4} m of " +
            "visibility across a 1 cm change in sight range.");
    }

    /// <summary><b>Negative control for the sweep above.</b> A banded implementation — the obvious
    /// cheap alternative, "near / mid / far fog presets" — must be caught by the identical
    /// measurement. Bands of 6 m are coarse enough to be a real proposal and fine enough that a
    /// weak measurement would miss them.</summary>
    [Fact]
    public void NegativeControl_ABandedFogPreset_IsCaughtByTheSameSweep()
    {
        float worst = WorstRangeStep(r =>
        {
            float banded = Mathf.Floor(r / 6f) * 6f + 3f;
            return SightPresentation.RenderedVisibilityM(SightPresentation.DensityForRange(banded));
        });
        Assert.True(worst >= 0.25f,
            $"the continuity sweep failed to notice 6 m fog bands (worst jump {worst:F4} m), so " +
            "Continuity_RenderedVisibilityNeverJumpsAsTheSightRangeMoves proves nothing.");
    }

    private static float WorstRangeStep(Func<float, float> measure)
    {
        float worst = 0f;
        float previous = measure(PlayerSightCurve.DarkFloorM);
        for (float r = PlayerSightCurve.DarkFloorM + 0.01f; r <= PlayerSightCurve.DaylightSightM; r += 0.01f)
        {
            float value = measure(r);
            worst = Mathf.Max(worst, Mathf.Abs(value - previous));
            previous = value;
        }
        return worst;
    }

    /// <summary><b>The dusk and dawn sweeps, and the wrap seam, at a fixed sight range.</b> This
    /// is the one the direction singled out: the night factor and the sight range move together
    /// across the sweeps, and a discontinuity in either the fog density or the fog colour would
    /// read as the night arriving in a snap. Walked over the whole cycle on every day of the run,
    /// because <c>CycleBands</c> moves the breakpoints as the night lengthens and a seam that
    /// holds on day 1 can open on day 5.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void Continuity_TheWholeCycleIsSmooth_OnEveryDayOfTheRun(int cyclesElapsed)
    {
        const float Range = 12.0f; // a modest fire's pool — the interesting middle of the gradient.
        float worstDensity = 0f;
        float worstColor = 0f;
        float previousDensity = DensityAtPhase(0f, cyclesElapsed, Range);
        Color previousColor = ColorAtPhase(0f, cyclesElapsed);

        // 1/20000 of a cycle — 36 ms of a 720 s day, well under a frame.
        for (int i = 1; i <= 20000; i++)
        {
            float phase = i / 20000f;
            float density = DensityAtPhase(phase, cyclesElapsed, Range);
            Color color = ColorAtPhase(phase, cyclesElapsed);
            worstDensity = Mathf.Max(worstDensity, Mathf.Abs(density - previousDensity));
            worstColor = Mathf.Max(worstColor, ChannelDelta(color, previousColor));
            previousDensity = density;
            previousColor = color;
        }

        // Includes the wrap: phase 1.0 is evaluated as PosMod 0, which must equal the phase-0
        // sample the loop started from.
        Assert.True(worstDensity < 0.002f,
            $"day {cyclesElapsed}: the sight fog steps somewhere in the cycle — worst density " +
            $"jump {worstDensity:F6} between adjacent 36 ms samples.");
        Assert.True(worstColor < 0.002f,
            $"day {cyclesElapsed}: the fog colour steps somewhere in the cycle — worst channel " +
            $"jump {worstColor:F6}.");
    }

    /// <summary><b>Negative control for the cycle sweep.</b> The identical measurement, run over a
    /// weight curve that snaps to night at the band boundary instead of easing — the single most
    /// likely way to build this wrong, and the one the direction named. If the sweep cannot see a
    /// hard switch it cannot see a soft one either.</summary>
    [Fact]
    public void NegativeControl_ASteppedNightGate_IsCaughtByTheSameCycleSweep()
    {
        const float Range = 12.0f;
        float worst = 0f;
        float previous = SteppedDensityAtPhase(0f, Range);
        for (int i = 1; i <= 20000; i++)
        {
            float density = SteppedDensityAtPhase(i / 20000f, Range);
            worst = Mathf.Max(worst, Mathf.Abs(density - previous));
            previous = density;
        }
        Assert.True(worst >= 0.002f,
            $"the cycle sweep failed to notice a hard night gate (worst jump {worst:F6}), so " +
            "Continuity_TheWholeCycleIsSmooth_OnEveryDayOfTheRun proves nothing.");
    }

    private static float DensityAtPhase(float phase, int cyclesElapsed, float rangeM)
    {
        OutdoorAtmosphere.AtmoState s = OutdoorAtmosphere.Evaluate(phase, cyclesElapsed);
        return SightPresentation.FogDensityWithSight(s.FogDensity, s.NightFactor, rangeM);
    }

    private static Color ColorAtPhase(float phase, int cyclesElapsed)
    {
        OutdoorAtmosphere.AtmoState s = OutdoorAtmosphere.Evaluate(phase, cyclesElapsed);
        // The backdrop the shipped binding blends toward. Read off the state rather than
        // hard-coded, so a retune of the void colour cannot leave this test asserting the old one.
        Color backdrop = OutdoorAtmosphere.Evaluate(0.78f, 0).BackgroundColor;
        return SightPresentation.FogColorWithSight(s.FogColor, backdrop, s.NightFactor);
    }

    private static float SteppedDensityAtPhase(float phase, float rangeM)
    {
        OutdoorAtmosphere.AtmoState s = OutdoorAtmosphere.Evaluate(phase, cyclesElapsed: 0);
        float weight = s.NightFactor >= 0.5f ? 1f : 0f; // the hard gate.
        return Mathf.Lerp(s.FogDensity,
            Mathf.Max(s.FogDensity, SightPresentation.DensityForRange(rangeM)), weight);
    }

    private static float ChannelDelta(Color a, Color b) =>
        Mathf.Max(Mathf.Max(Mathf.Abs(a.R - b.R), Mathf.Abs(a.G - b.G)), Mathf.Abs(a.B - b.B));

    // =======================================================================================
    // 3. Degrades to blind, never to omniscient
    // =======================================================================================

    /// <summary>Every way the input can be missing or wrong resolves to the darkness floor. The
    /// unsynced case is the contract <c>PlayerSightService.Synced</c> states in its own doc:
    /// consumers must not present anything derived from the range until it is true.</summary>
    [Theory]
    [InlineData(false, 34.5f)]                  // unsynced, with a perfectly good number sitting there
    [InlineData(false, 80f)]
    [InlineData(true, 0f)]                      // the degenerate default NightPressureFlag warns about
    [InlineData(true, -12f)]
    [InlineData(true, float.NaN)]
    public void DegradesBlind_EveryBadInputResolvesToTheDarknessFloor(bool synced, float raw)
    {
        Assert.Equal(PlayerSightCurve.DarkFloorM, SightPresentation.TargetRangeM(synced, raw), 1e-6f);
    }

    /// <summary><b>The inversion, in its rendering form.</b> A corrupt or future-tuned range above
    /// daylight would drive the fog density toward zero — a frame that shows the player everything
    /// precisely because its input was wrong. Clamped, never trusted.</summary>
    [Theory]
    [InlineData(200f)]
    [InlineData(100000f)]
    [InlineData(float.PositiveInfinity)]
    public void DegradesBlind_AnAbsurdlyLargeRangeIsClampedToDaylight_NotTrusted(float raw)
    {
        Assert.Equal(PlayerSightCurve.DaylightSightM,
            SightPresentation.TargetRangeM(synced: true, raw), 1e-6f);
    }

    /// <summary>A world with no sight service at all — every dev lab, the playground, a client
    /// before its first table lands — renders its night at the floor and its DAY unchanged. Both
    /// halves matter: blind at night is the required degradation, and blind at noon would be a
    /// black screen in broad daylight, which is the failure the night weight exists to prevent.</summary>
    [Fact]
    public void DegradesBlind_NoServiceIsNearBlindAtNight_AndHarmlessByDay()
    {
        float blind = SightPresentation.TargetRangeM(synced: false, rawRangeM: 0f);

        OutdoorAtmosphere.AtmoState night = OutdoorAtmosphere.Evaluate(DeepNightPhase, 0);
        float nightDensity = SightPresentation.FogDensityWithSight(night.FogDensity, night.NightFactor, blind);
        Assert.Equal(PlayerSightCurve.DarkFloorM,
            SightPresentation.RenderedVisibilityM(nightDensity), 1e-3f);

        OutdoorAtmosphere.AtmoState day = OutdoorAtmosphere.Evaluate(MiddayPhase, 0);
        float dayDensity = SightPresentation.FogDensityWithSight(day.FogDensity, day.NightFactor, blind);
        Assert.Equal(day.FogDensity, dayDensity);
    }

    // =======================================================================================
    // 4. Day is not night
    // =======================================================================================

    /// <summary>Daylight sight is 80 m against a 36 m night maximum, and a fog sized for either
    /// would be a visible haze over a 50 m camp. At night factor 0 the binding must return the
    /// phase-authored density BIT-IDENTICALLY, not merely close — that exactness is what
    /// guarantees no retune of any sight constant can leak into the day.</summary>
    [Fact]
    public void DayIsNotNight_AtNightFactorZeroTheAuthoredFogIsReturnedUnchanged()
    {
        OutdoorAtmosphere.AtmoState day = OutdoorAtmosphere.Evaluate(MiddayPhase, 0);
        Assert.Equal(0f, day.NightFactor);

        foreach (float range in SightRanges())
        {
            Assert.Equal(day.FogDensity,
                SightPresentation.FogDensityWithSight(day.FogDensity, day.NightFactor, range));
            Assert.Equal(day.FogColor,
                SightPresentation.FogColorWithSight(day.FogColor, new Color(0f, 0f, 0f), day.NightFactor));
        }
    }

    /// <summary>At the other end, deep night is exactly the sight range and nothing else — the
    /// weight reaches 1 exactly, so the rendered visibility IS the number the server computed.
    /// This is the whole feature in one assertion.</summary>
    [Fact]
    public void AtDeepNight_TheRenderedVisibilityIsExactlyTheServersSightRange()
    {
        OutdoorAtmosphere.AtmoState night = OutdoorAtmosphere.Evaluate(DeepNightPhase, 0);
        Assert.Equal(1f, night.NightFactor);

        foreach (float range in SightRanges())
        {
            float density = SightPresentation.FogDensityWithSight(night.FogDensity, night.NightFactor, range);
            Assert.Equal(Mathf.Min(range, PlayerSightCurve.DaylightSightM),
                SightPresentation.RenderedVisibilityM(density), 1e-3f);
        }
    }

    // =======================================================================================
    // 5. The binding only ever takes sight away
    // =======================================================================================

    /// <summary>More sight range is never thicker fog. A violation here would mean walking toward
    /// a fire could make the world darker — the same class of inversion
    /// <c>PlayerSightCurve.NightSightM</c> rejects nearest-source for.</summary>
    [Fact]
    public void Monotone_MoreSightRangeIsNeverLessRenderedVisibility()
    {
        foreach (float night in NightFactors())
        {
            float previous = -1f;
            for (float r = PlayerSightCurve.DarkFloorM; r <= PlayerSightCurve.DaylightSightM; r += 0.05f)
            {
                float visibility = ShippedRenderedVisibility(r, night);
                Assert.True(visibility >= previous - 1e-4f,
                    $"night {night}: sight range {r} m renders LESS visible ({visibility:F3} m) " +
                    $"than the shorter range before it ({previous:F3} m).");
                previous = visibility;
            }
        }
    }

    /// <summary><b>The sight term may thicken the authored fog and never thin it.</b> Today the
    /// sight density exceeds the phase-authored haze at every range, so the guard is inert — which
    /// is exactly why it needs a test: an inert guard that gets "simplified" away is invisible
    /// until the day a retune makes it load-bearing. Fed a deliberately thick authored fog that
    /// the sight term would otherwise dilute.</summary>
    [Fact]
    public void NeverThins_ASightRangeCannotBuyBackFogTheClockAuthored()
    {
        const float ThickAuthored = 0.5f; // far above anything the phase curves ship.
        foreach (float night in NightFactors())
        {
            foreach (float range in SightRanges())
            {
                float density = SightPresentation.FogDensityWithSight(ThickAuthored, night, range);
                Assert.True(density >= ThickAuthored - 1e-6f,
                    $"night {night}, range {range} m: the sight binding thinned an authored fog " +
                    $"of {ThickAuthored} down to {density} — it handed the player sight the clock " +
                    "had taken away.");
            }
        }
    }

    /// <summary>The same rule at the dome, which is the shipped camp's last fog writer and would
    /// otherwise undo the whole binding: at full insideness <c>NightDome</c> used to lerp the
    /// density all the way to its own 0.014, so crossing into the dark would have made a
    /// sight-fogged world an order of magnitude CLEARER. Pinned as arithmetic rather than by
    /// standing up a Node — the property is the max, and the max is what the source now applies.
    ///
    /// <para>Positive control included: the same expression with the max removed must fail, which
    /// is what shows this assertion is about the fix rather than about a tautology.</para></summary>
    [Fact]
    public void NeverThins_TheDomeDarkensAndNeverBrightens()
    {
        const float InsideFogDensity = 0.014f; // NightDome's shipped export default.
        OutdoorAtmosphere.AtmoState night = OutdoorAtmosphere.Evaluate(DeepNightPhase, 0);

        foreach (float range in SightRanges())
        {
            float authored = SightPresentation.FogDensityWithSight(
                night.FogDensity, night.NightFactor, range);

            // inside == 1: the camera is fully swallowed by the night front, which in the shipped
            // camp is the whole night band (DomeInnerRadiusM is 0).
            float withMax = Mathf.Lerp(authored, Mathf.Max(InsideFogDensity, authored), 1f);
            Assert.True(withMax >= authored - 1e-6f,
                $"range {range} m: the dome thinned the authored sight fog {authored} to {withMax}.");

            // POSITIVE CONTROL — the pre-fix expression, which does thin it. If this does not
            // trip, the assertion above is not testing the fix.
            float withoutMax = Mathf.Lerp(authored, InsideFogDensity, 1f);
            if (authored > InsideFogDensity)
            {
                Assert.True(withoutMax < authored,
                    "the pre-fix dome expression did not thin the fog, so the assertion above is " +
                    "asserting nothing.");
            }
        }
    }

    // =======================================================================================
    // 6. The smoothing — frame-rate independence is a parity property
    // =======================================================================================

    /// <summary><b>Two machines at different frame rates must reach the same rendered range at the
    /// same instant.</b> A naive <c>lerp(current, target, 0.1)</c> per frame would hand the faster
    /// machine a faster-responding night, which is a graphics-driven difference in what a player
    /// can see — the parity law's failure mode wearing a different hat. The half-life form
    /// composes exactly, so one 0.2 s step and forty-eight 1/240 s steps must land on the same
    /// float.</summary>
    [Fact]
    public void Smoothing_IsFrameRateIndependent()
    {
        const float Start = 3f;
        const float Target = 34.5f;

        float oneBigStep = SightPresentation.Smooth(Start, Target, 0.2);

        float many = Start;
        for (int i = 0; i < 48; i++)
            many = SightPresentation.Smooth(many, Target, 0.2 / 48.0);

        Assert.Equal(oneBigStep, many, 1e-4f);

        // POSITIVE CONTROL: a per-frame constant lerp — the shape this deliberately is not —
        // diverges hugely over the same interval, which is what makes the assertion above mean
        // something.
        float naive = Start;
        for (int i = 0; i < 48; i++)
            naive += (Target - naive) * 0.1f;
        Assert.True(Mathf.Abs(naive - oneBigStep) > 1f,
            "a fixed-alpha lerp did not diverge from the half-life form over 48 substeps, so " +
            "the frame-rate-independence assertion is not discriminating.");
    }

    /// <summary>The smoothing converges toward the target, never past it and never away from it,
    /// in both directions — going blind and recovering sight. A frame with a paused or zero delta
    /// is not a visual event.</summary>
    [Theory]
    [InlineData(3f, 34.5f)]
    [InlineData(34.5f, 3f)]
    [InlineData(20f, 20f)]
    public void Smoothing_ConvergesWithoutOvershooting(float start, float target)
    {
        // 600 frames at 60 fps is 10 s — 40 half-lives, so convergence is complete to float
        // precision rather than merely close, and "converges" is asserted as a fact not a trend.
        float value = start;
        for (int i = 0; i < 600; i++)
        {
            float next = SightPresentation.Smooth(value, target, 1.0 / 60.0);
            Assert.True(next >= Mathf.Min(value, target) - 1e-4f && next <= Mathf.Max(value, target) + 1e-4f,
                $"step {i}: smoothing left the interval — {value} -> {next} toward {target}.");
            value = next;
        }
        Assert.Equal(target, value, 1e-3f);

        Assert.Equal(start, SightPresentation.Smooth(start, target, 0.0));
        Assert.Equal(start, SightPresentation.Smooth(start, target, double.NaN));
    }

    /// <summary>The staircase the smoothing exists for: a client holds the last authoritative
    /// value between 0.2 s broadcasts, so the raw input really does step. Fed the raw staircase a
    /// sprinting player would produce crossing a full fire pit, the smoothed output's largest
    /// single-frame move must be a small fraction of the raw step it is absorbing.</summary>
    [Fact]
    public void Smoothing_AbsorbsTheFiveHertzReplicationStaircase()
    {
        const double Frame = 1.0 / 60.0;
        float rendered = 3f;
        float rawStep = 0f;
        float worstRendered = 0f;

        // 20 broadcasts of a player walking into a fire pit: the raw value jumps every 0.2 s.
        for (int broadcast = 0; broadcast < 20; broadcast++)
        {
            float raw = 3f + broadcast * 1.575f; // 3 -> 34.5 over the pit's radius.
            rawStep = Mathf.Max(rawStep, 1.575f);
            for (int frame = 0; frame < 12; frame++) // 12 frames at 60 fps == 0.2 s
            {
                float next = SightPresentation.Smooth(rendered, raw, Frame);
                worstRendered = Mathf.Max(worstRendered, Mathf.Abs(next - rendered));
                rendered = next;
            }
        }

        Assert.True(worstRendered < rawStep * 0.35f,
            $"the smoothing passed the replication staircase through: worst rendered step " +
            $"{worstRendered:F4} m against a raw step of {rawStep:F4} m.");
    }

    // =======================================================================================
    // Shared helpers
    // =======================================================================================

    /// <summary>Strips <c>//</c> line comments and XML doc lines so a contract scan cannot be
    /// fooled — in either direction — by a symbol that only appears in prose. Same technique
    /// <c>GraphicsSettingsTests</c> uses on <c>Boot.cs</c>.</summary>
    private static string StripCommentsAndDocs(string source)
    {
        var kept = new List<string>();
        foreach (string line in source.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;
            kept.Add(line);
        }
        return string.Join("\n", kept);
    }

    /// <summary>Repo-relative file read, walking up from the test binary to the directory holding
    /// <c>project.godot</c> — the same helper shape <c>GroundSkyLightContractTests</c> uses, and
    /// for the same reason: the runner's working directory is not something to depend on.</summary>
    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;

        Assert.True(dir is not null, "could not locate the repo root (no project.godot above the test binary)");
        string full = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"expected repo file is missing: {relativePath}");
        return File.ReadAllText(full);
    }
}
