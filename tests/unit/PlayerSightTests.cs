using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MpFoundation.Game.Sight;
using MpFoundation.Game.World;
using Xunit;

namespace SailNet.Tests;

/// <summary>
/// <b>Canon fact 4, made executable.</b> "Darkness is the pressure, and vision is proximity-based...
/// Gameplay visibility is server data, identical on every client (the parity law); rendered light is
/// presentation only."
///
/// <para>Five things are pinned here, and the first one is the reason the file exists:</para>
/// <list type="number">
/// <item><b>Parity across graphics tiers.</b> The same world state must yield a bit-identical sight
/// range on Low, Medium and High. The failure this prevents is a player turning their graphics down
/// to see further in the dark, which is a competitive advantage bought with a settings slider and is
/// exactly what canon fact 4's last clause forbids.</item>
/// <item><b>Monotonicity.</b> Nearer to a light is never less sight than further from it.</item>
/// <item><b>Union, not nearest.</b> A guttering ember at your feet must not mask a roaring fire that
/// genuinely reaches you.</item>
/// <item><b>Every degradation goes blind, never omniscient.</b> No lights, a dead source, an
/// unsynced clock, an unknown peer — each yields the darkness floor.</item>
/// <item><b>Day is not night</b>, and the two sweeps between them are ramps rather than steps.</item>
/// </list>
///
/// <para><b>Every claim is made with a positive control.</b> This repo's standing rule is that a
/// verification method is worthless until it can demonstrate the <i>present</i> case as well as the
/// absent one — a test that would still pass with the system deleted is not a test. So the parity
/// proof runs a deliberately tier-scaled implementation through the identical comparator and asserts
/// it is caught; the continuity sweep runs a deliberately stepped implementation through the
/// identical measurement and asserts the step is seen; every "reads the floor" assertion is paired
/// with the "and here is the same setup producing far more than the floor" that proves the setup was
/// capable of it.</para>
///
/// <para><b>On mutating <see cref="MpFoundation.World.GraphicsQuality.Current"/>.</b> It is a global
/// static and this is the only file in the suite that writes it (every other reference is to
/// <c>GraphicsSettings</c>'s pure resolver, which takes its inputs as arguments). The tier is always
/// restored in a <c>finally</c>, and no assertion in this file depends on the tier being any
/// particular value — that is, after all, the thing being proven.</para>
/// </summary>
public class PlayerSightTests
{
    private const float Eps = 1e-4f;

    /// <summary>Float equality with a stated tolerance. <c>Assert.Equal(float, float, int)</c>
    /// is ambiguous against xUnit's <c>(float, float, float)</c> overload, and the message this
    /// produces names both numbers, which the built-in one does not do usefully for metres.</summary>
    private static void Near(float expected, float actual, float tolerance = 1e-3f) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"expected {expected:F5} m, got {actual:F5} m (tolerance {tolerance})");

    // Day index 0 throughout unless a test says otherwise, so the band boundaries are the table's
    // first column (dusk 0.550, night 0.650, dawn 0.900) and a reader can check the phases by eye.
    private const int Day1 = 0;

    private static Vector3 At(float x, float z) => new(x, 0f, z);

    /// <summary>A phase comfortably inside the Night band on day 1 (0.650 .. 0.900).</summary>
    private const float Midnight = 0.775f;

    /// <summary>A phase comfortably inside the Day band on day 1 (0.000 .. 0.550).</summary>
    private const float Noon = 0.275f;

    // The ladder of lit radii the curve is exercised against. These used to be read off the
    // fire and glow-stick systems, which the MVP extraction removed; the curve itself never
    // knew where a radius came from, so the numbers are pinned here as plain metres. The
    // arithmetic in the assertions below (the 18 m edge, the 25.75 m value in the sweep note)
    // depends only on FullFireRadiusM; the rest need to be spread from small to large and
    // nothing else — the two middle rungs are an authored spread, not a recovered constant.
    private const float EmberRadiusM = 2f;        // a source nearly dead
    private const float FlareRadiusM = 4f;        // an emergency flare
    private const float StickRadiusM = 4f;        // a personal light (the 6 m figure was the ceiling)
    private const float IndoorFireRadiusM = 8f;
    private const float RingFireRadiusM = 12f;
    private const float FullFireRadiusM = 18f;    // the largest light the curve was tuned for

    // ---------------------------------------------------------------------------------------
    // 1. THE PARITY PROOF. The single most important test in this file.
    // ---------------------------------------------------------------------------------------

    /// <summary>Every scenario the model can be asked about, as (label, evaluated value). One list,
    /// used by the parity proof and by its own negative control, so the two are provably measuring
    /// the same thing.</summary>
    private static List<(string Label, float Value)> EvaluateWholeMatrix(
        Func<Vector3, IReadOnlyList<LightSample>, float, int, float> resolve)
    {
        var results = new List<(string, float)>();

        // Single sources across every shipped light radius, sampled from the centre out past the
        // edge — the whole gradient plus both boundaries.
        float[] radii = { EmberRadiusM, FlareRadiusM, IndoorFireRadiusM,
                          RingFireRadiusM, FullFireRadiusM };
        float[] distances = { 0f, 0.5f, 1f, 4f, 9f, 13.9f, 14f, 18f, 25f, 60f };
        foreach (float r in radii)
        foreach (float d in distances)
        {
            var lights = new List<LightSample> { new(At(d, 0f), r) };
            results.Add(($"r={r} d={d} night", resolve(Vector3.Zero, lights, Midnight, Day1)));
            results.Add(($"r={r} d={d} day", resolve(Vector3.Zero, lights, Noon, Day1)));
        }

        // A mixed scene of several sources at once, so the union path is swept as well as the
        // single-source path. (The registry this used to be gathered from is gone with the MVP
        // extraction; the service's gather is now an empty list, so there is no tier-dependent
        // gather left to catch and the samples are built directly.)
        var gathered = new List<LightSample>
        {
            new(At(0f, 0f), FullFireRadiusM),
            new(At(30f, 0f), RingFireRadiusM),
            new(At(-6f, 0f), FlareRadiusM),
            new(At(8f, 8f), StickRadiusM),
        };

        // Every band, sampled densely enough to cross all four and both sweeps.
        for (int i = 0; i <= 40; i++)
        {
            float phase = i / 40f;
            for (int x = -35; x <= 35; x += 5)
                results.Add(($"world x={x} phase={phase:F3}",
                    resolve(At(x, 0f), gathered, phase, Day1)));
        }

        // Every day of the run, since the band boundaries move with it.
        for (int day = 0; day <= CycleBands.MaxDayIndex + 1; day++)
            results.Add(($"day={day}", resolve(At(4f, 0f), gathered, 0.62f, day)));

        return results;
    }

    /// <summary>Runs <paramref name="resolve"/> across the whole matrix on Low, Medium and High and
    /// returns every scenario whose three answers were not identical. The comparator both the parity
    /// proof and its negative control go through.</summary>
    private static List<string> TierDisagreements(
        Func<Vector3, IReadOnlyList<LightSample>, float, int, float> resolve,
        out List<(string Label, float Value)> mediumRun)
    {
        MpFoundation.World.GraphicsQuality.Tier restore = MpFoundation.World.GraphicsQuality.Current;
        try
        {
            MpFoundation.World.GraphicsQuality.Current = MpFoundation.World.GraphicsQuality.Tier.Low;
            List<(string Label, float Value)> low = EvaluateWholeMatrix(resolve);
            MpFoundation.World.GraphicsQuality.Current = MpFoundation.World.GraphicsQuality.Tier.Medium;
            List<(string Label, float Value)> medium = EvaluateWholeMatrix(resolve);
            MpFoundation.World.GraphicsQuality.Current = MpFoundation.World.GraphicsQuality.Tier.High;
            List<(string Label, float Value)> high = EvaluateWholeMatrix(resolve);

            mediumRun = medium;
            var bad = new List<string>();
            for (int i = 0; i < medium.Count; i++)
            {
                // Bit-exact, not approximate. Every input is the same float and every operation is
                // the same sequence, so any difference at all is a difference in the CODE, and a
                // tolerance here would let a small tier scaling through.
                if (low[i].Value != medium[i].Value || medium[i].Value != high[i].Value)
                    bad.Add($"{medium[i].Label}: low={low[i].Value} medium={medium[i].Value} high={high[i].Value}");
            }
            return bad;
        }
        finally
        {
            MpFoundation.World.GraphicsQuality.Current = restore;
        }
    }

    /// <summary><b>THE PARITY LAW.</b> The same world state yields the identical sight range on
    /// every graphics tier.
    ///
    /// <para>The positive control is inside the same test rather than beside it: the matrix is also
    /// asserted to span a real range of values, from the darkness floor up past 30 m. Without that,
    /// a build whose sight range was the constant 0 would pass the equality check triumphantly —
    /// three tiers agreeing about nothing is not parity, it is an absent feature.</para></summary>
    [Fact]
    public void Parity_SightRangeIsIdenticalOnLowMediumAndHigh()
    {
        List<string> disagreements = TierDisagreements(
            (at, lights, phase, day) => PlayerSightCurve.ResolveFromClock(at, lights, phase, day),
            out List<(string Label, float Value)> run);

        Assert.True(disagreements.Count == 0,
            "sight range varied with the graphics tier, which is canon fact 4's parity law broken — " +
            "a player could see further in the dark by turning their graphics down:\n  " +
            string.Join("\n  ", disagreements.Take(10)));

        // Positive control: the matrix actually exercised the model.
        float[] values = run.Select(r => r.Value).ToArray();
        Assert.True(values.Distinct().Count() > 20,
            $"the parity matrix produced only {values.Distinct().Count()} distinct values — three " +
            "tiers agreeing about a near-constant proves nothing");
        Assert.True(values.Min() <= PlayerSightCurve.DarkFloorM + Eps,
            $"the matrix never reached the darkness floor (min {values.Min():F2} m)");
        Assert.True(values.Max() >= 30f,
            $"the matrix never reached a well-lit sight range (max {values.Max():F2} m)");
    }

    /// <summary><b>The negative control for the test above, and the reason it means anything.</b>
    /// The identical matrix and the identical comparator, run against an implementation that scales
    /// the sight range by the active graphics tier — the exact defect the parity law exists to
    /// forbid. If this test ever goes green, the comparator has stopped being able to see a
    /// violation and <see cref="Parity_SightRangeIsIdenticalOnLowMediumAndHigh"/> is worthless.</summary>
    [Fact]
    public void NegativeControl_ATierScaledSightRange_IsCaughtByTheSameComparator()
    {
        List<string> disagreements = TierDisagreements(
            (at, lights, phase, day) =>
            {
                float honest = PlayerSightCurve.ResolveFromClock(at, lights, phase, day);
                // The defect: "High-end machines can see further." Small enough that a human
                // eyeballing a log would never notice; that is why the check is bit-exact.
                float tierBonus = MpFoundation.World.GraphicsQuality.Current switch
                {
                    MpFoundation.World.GraphicsQuality.Tier.Low => 0.98f,
                    MpFoundation.World.GraphicsQuality.Tier.High => 1.02f,
                    _ => 1.00f,
                };
                return honest * tierBonus;
            },
            out _);

        Assert.True(disagreements.Count > 0,
            "a deliberately tier-scaled sight range was NOT caught — the parity comparator cannot " +
            "detect the very defect it exists to detect, so the parity proof above certifies nothing");
    }

    // ---------------------------------------------------------------------------------------
    // 2. MONOTONICITY
    // ---------------------------------------------------------------------------------------

    /// <summary>Walking toward a light never costs you sight, at any radius, at any distance.
    /// Swept at 5 cm — finer than a physics step's worth of movement — so a non-monotone kink
    /// anywhere in the curve is caught rather than stepped over.
    ///
    /// <para>Positive control: the same sweep must also have <i>gained</i> real sight, so a build
    /// returning a constant cannot pass by being trivially non-decreasing.</para></summary>
    [Theory]
    [InlineData(EmberRadiusM)]
    [InlineData(FlareRadiusM)]
    [InlineData(IndoorFireRadiusM)]
    [InlineData(RingFireRadiusM)]
    [InlineData(FullFireRadiusM)]
    public void Monotone_ApproachingALight_NeverReducesSight(float litRadiusM)
    {
        var lights = new List<LightSample> { new(Vector3.Zero, litRadiusM) };
        float previous = float.NegativeInfinity;
        float atEdgeOut = PlayerSightCurve.NightSightM(At(litRadiusM * 2f, 0f), lights);

        for (float d = litRadiusM * 2f; d >= 0f; d -= 0.05f)
        {
            float here = PlayerSightCurve.NightSightM(At(d, 0f), lights);
            Assert.True(here >= previous - Eps,
                $"stepping 5 cm closer to a {litRadiusM} m light at d={d:F2} m REDUCED sight from " +
                $"{previous:F4} m to {here:F4} m — nearer to a light must never be less sight");
            previous = here;
        }

        // Positive control.
        float atCentre = PlayerSightCurve.NightSightM(Vector3.Zero, lights);
        Near(PlayerSightCurve.DarkFloorM, atEdgeOut);
        Assert.True(atCentre > atEdgeOut + 1f,
            $"a {litRadiusM} m light granted no meaningful sight at its own centre " +
            $"({atCentre:F2} m vs {atEdgeOut:F2} m outside it) — the sweep above is monotone but empty");
    }

    /// <summary>No single step across the light's own edge can jump. The boundary is where a cliff
    /// would live if the curve had one — a build that returned the full grant inside the radius and
    /// the floor outside it would be monotone, would pass every test above, and would snap a
    /// player's sight by 31 m in one footstep.</summary>
    [Fact]
    public void Continuous_CrossingALightsEdge_IsNotACliff()
    {
        float r = FullFireRadiusM;
        var lights = new List<LightSample> { new(Vector3.Zero, r) };
        float inside = PlayerSightCurve.NightSightM(At(r - 0.01f, 0f), lights);
        float outside = PlayerSightCurve.NightSightM(At(r + 0.01f, 0f), lights);

        Assert.True(Math.Abs(inside - outside) < 0.1f,
            $"crossing the 18 m edge of a full fire changed sight from {inside:F3} m to " +
            $"{outside:F3} m in 2 cm — that is the cliff the model must not have");
        Near(PlayerSightCurve.DarkFloorM, outside);
    }

    // ---------------------------------------------------------------------------------------
    // 3. UNION, NOT NEAREST
    // ---------------------------------------------------------------------------------------

    /// <summary><b>A guttering ember at your feet must not mask the roaring pit that is genuinely
    /// reaching you.</b> The two answers are computed side by side from the same two sources so the
    /// difference between the union rule and the nearest-source rule is the assertion itself, not a
    /// claim about it.
    ///
    /// <para>This is the case that decides the whole design. Under nearest-source, dropping a glow
    /// stick beside a big fire would make you <i>blinder</i>, which is a rule no player would ever
    /// guess and every player would eventually be bitten by.</para></summary>
    [Fact]
    public void Union_ADyingLightAtYourFeet_DoesNotMaskARoaringOneThatReachesYou()
    {
        var ember = new LightSample(At(0.5f, 0f), 1.0f);            // nearest, and nearly dead
        var pit = new LightSample(At(10f, 0f), FullFireRadiusM); // further, and enormous
        var both = new List<LightSample> { ember, pit };

        float nearestOnly = PlayerSightCurve.FromSource(0.5f, ember.LitRadiusM);
        float union = PlayerSightCurve.NightSightM(Vector3.Zero, both);
        float pitAlone = PlayerSightCurve.FromSource(10f, pit.LitRadiusM);

        // The ember really is the nearest source, or the test is not testing what it says.
        Assert.True(0.5f < 10f);
        Near(pitAlone, union);
        Assert.True(union > nearestOnly + 10f,
            $"union gave {union:F2} m and nearest-source would have given {nearestOnly:F2} m — if " +
            "these are close, the model is picking the nearest light instead of the best one");
    }

    /// <summary>Adding a light can never reduce anybody's sight. The property that makes the union
    /// rule safe to build a trail mechanic on: laying a glow stick is never a mistake.</summary>
    [Fact]
    public void Union_AddingALight_NeverReducesSight()
    {
        var baseLights = new List<LightSample>
        {
            new(At(12f, 0f), FullFireRadiusM),
            new(At(-20f, 5f), RingFireRadiusM),
        };

        for (float x = -30f; x <= 30f; x += 0.5f)
        for (float z = -30f; z <= 30f; z += 5f)
        {
            Vector3 at = At(x, z);
            float before = PlayerSightCurve.NightSightM(at, baseLights);
            var after = new List<LightSample>(baseLights) { new(At(3f, 3f), StickRadiusM) };
            Assert.True(PlayerSightCurve.NightSightM(at, after) >= before - Eps,
                $"dropping a glow stick at (3,3) made the player at ({x},{z}) see LESS");
        }
    }

    /// <summary>Two lights are not additive. Six sticks in a heap must not out-see the campfire, or
    /// canon fact 3 ("every other light stays personal-scale so the fire keeps its weight") is
    /// broken by arithmetic.</summary>
    [Fact]
    public void Union_ManySmallLights_DoNotOutshineTheCampfire()
    {
        var heap = new List<LightSample>();
        for (int i = 0; i < 12; i++)
            heap.Add(new LightSample(At(i * 0.05f, 0f), StickRadiusM));
        float heapSight = PlayerSightCurve.NightSightM(Vector3.Zero, heap);

        var fire = new List<LightSample> { new(Vector3.Zero, FullFireRadiusM) };
        float fireSight = PlayerSightCurve.NightSightM(Vector3.Zero, fire);

        Assert.True(heapSight < fireSight,
            $"twelve glow sticks in a heap gave {heapSight:F2} m and a full campfire gave " +
            $"{fireSight:F2} m — light is being summed, not unioned");
        // Positive control: the heap did grant something, so the comparison is not two zeroes.
        Assert.True(heapSight > PlayerSightCurve.DarkFloorM + 1f);
    }

    // ---------------------------------------------------------------------------------------
    // 4. EVERY DEGRADATION GOES BLIND
    // ---------------------------------------------------------------------------------------

    /// <summary>No lights at all: the floor, not the maximum, and not zero.</summary>
    [Fact]
    public void Blind_WithNoLights_ReadsTheFloor()
    {
        Near(PlayerSightCurve.DarkFloorM,
            PlayerSightCurve.NightSightM(Vector3.Zero, new List<LightSample>()));
        Near(PlayerSightCurve.DarkFloorM,
            PlayerSightCurve.NightSightM(Vector3.Zero, null));
        Assert.True(PlayerSightCurve.DarkFloorM > 0f,
            "the darkness floor is zero — a literal zero sight range is a black screen and a " +
            "divide-by waiting to happen downstream");
    }

    /// <summary><b>An unsynced clock reads the floor, even standing in a roaring fire at what the
    /// zero-initialized clock would call high noon.</b>
    ///
    /// <para>This is the night-pressure inversion in its purest form and the single
    /// most dangerous default in the whole feature: <see cref="CycleDriver"/>'s unset phase is 0,
    /// which is the middle of the Day band, so a build that read the phase without checking
    /// <see cref="CycleDriver.Synced"/> would hand every player 80 m of sight in the dark and would
    /// pass every other test in this file.</para></summary>
    [Fact]
    public void Blind_AnUnsyncedClock_ReadsTheFloor_NotDaylight()
    {
        var lights = new List<LightSample> { new(Vector3.Zero, FullFireRadiusM) };

        float unsynced = PlayerSightCurve.ResolveForClock(Vector3.Zero, lights,
            clockSynced: false, phase: 0f, cyclesElapsed: 0);
        Near(PlayerSightCurve.DarkFloorM, unsynced);

        // The positive control, and the whole point: the SAME inputs with the clock synced return
        // daylight. So the guard is doing work, not agreeing with an inevitable answer.
        float synced = PlayerSightCurve.ResolveForClock(Vector3.Zero, lights,
            clockSynced: true, phase: 0f, cyclesElapsed: 0);
        Near(PlayerSightCurve.DaylightSightM, synced);
        Assert.True(synced - unsynced > 70f);
    }

    /// <summary>A dead source contributes nothing even at zero distance, and a hostile radius cannot
    /// invent light. MECHANICS-BIBLE §6 — the extremes are defined rather than left to propagate.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void Blind_ADegenerateRadius_GrantsNothingEvenAtZeroDistance(float radius)
    {
        var lights = new List<LightSample> { new(Vector3.Zero, radius) };
        Near(PlayerSightCurve.DarkFloorM, PlayerSightCurve.NightSightM(Vector3.Zero, lights));
        Near(PlayerSightCurve.DarkFloorM, PlayerSightCurve.SightAtSourceM(radius));
        Near(0f, PlayerSightCurve.Reach01(0f, radius));
    }

    /// <summary>The ceiling is a real backstop and it is above every shipped light, so it never bites
    /// in play. If a later light source is added that trips it, this test says so out loud rather
    /// than letting the cap silently flatten the top of the curve.</summary>
    [Fact]
    public void MaxSight_IsABackstopAboveEveryShippedLight()
    {
        Assert.True(PlayerSightCurve.SightAtSourceM(FullFireRadiusM) < PlayerSightCurve.MaxSightM,
            "the largest fire in the game is being clamped by MaxSightM — the ceiling has stopped " +
            "being a backstop and become part of the everyday curve");
        Near(PlayerSightCurve.MaxSightM, PlayerSightCurve.SightAtSourceM(1000f));
        Assert.True(PlayerSightCurve.DaylightSightM > PlayerSightCurve.MaxSightM,
            "daylight is not brighter than the biggest fire, so the dusk sweep would be a gain " +
            "of sight rather than a loss");
    }

    // ---------------------------------------------------------------------------------------
    // 5. DAY IS NOT NIGHT, AND THE SWEEPS ARE RAMPS
    // ---------------------------------------------------------------------------------------

    /// <summary>By day, sight is not governed by firelight: a roaring fire, a dead fire and no fire
    /// at all give the same answer. By night the same three give three very different ones.</summary>
    [Fact]
    public void Day_IsNotGovernedByFirelight_AndNightIs()
    {
        var none = new List<LightSample>();
        var dying = new List<LightSample> { new(Vector3.Zero, EmberRadiusM) };
        var roaring = new List<LightSample> { new(Vector3.Zero, FullFireRadiusM) };

        float dayNone = PlayerSightCurve.ResolveFromClock(Vector3.Zero, none, Noon, Day1);
        float dayDying = PlayerSightCurve.ResolveFromClock(Vector3.Zero, dying, Noon, Day1);
        float dayRoaring = PlayerSightCurve.ResolveFromClock(Vector3.Zero, roaring, Noon, Day1);
        Near(PlayerSightCurve.DaylightSightM, dayNone);
        Near(dayNone, dayDying);
        Near(dayNone, dayRoaring);

        // The positive control: at night those three inputs are emphatically not interchangeable.
        float nightNone = PlayerSightCurve.ResolveFromClock(Vector3.Zero, none, Midnight, Day1);
        float nightDying = PlayerSightCurve.ResolveFromClock(Vector3.Zero, dying, Midnight, Day1);
        float nightRoaring = PlayerSightCurve.ResolveFromClock(Vector3.Zero, roaring, Midnight, Day1);
        Near(PlayerSightCurve.DarkFloorM, nightNone);
        Assert.True(nightDying > nightNone + 2f);
        Assert.True(nightRoaring > nightDying + 20f);
        Assert.True(nightRoaring < dayRoaring,
            "the best fire in the game outsees daylight, which makes the night a reward");
    }

    /// <summary><b>The whole cycle, swept, with no step anywhere — including across the wrap.</b>
    /// The dusk and dawn sweeps have to be ramps: a hard cut from 80 m to 7 m at a phase boundary is
    /// a jump scare made of a clock, and it would arrive at a different wall-clock instant on every
    /// day of the run as the boundaries move.
    ///
    /// <para>Positive control inside the test: the sweep must actually have traversed the full range
    /// from daylight down to the fire's night value, so a build returning a constant cannot pass by
    /// being trivially smooth.</para></summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(CycleBands.MaxDayIndex)]
    public void Sweeps_AreContinuousAcrossTheWholeCycle(int day)
    {
        var lights = new List<LightSample> { new(Vector3.Zero, FullFireRadiusM) };
        Vector3 at = At(5f, 0f);
        const int steps = 5000;

        float worst = 0f;
        float worstAt = 0f;
        float min = float.MaxValue, max = float.MinValue;
        float previous = PlayerSightCurve.ResolveFromClock(at, lights, 0f, day);
        for (int i = 1; i <= steps; i++)
        {
            // Includes phase == 1.0, which CycleBands wraps to 0 — so the wrap itself is measured.
            float phase = i / (float)steps;
            float here = PlayerSightCurve.ResolveFromClock(at, lights, phase, day);
            float step = Math.Abs(here - previous);
            if (step > worst) { worst = step; worstAt = phase; }
            min = Math.Min(min, here);
            max = Math.Max(max, here);
            previous = here;
        }

        // One step is 1/5000 of a cycle; a 0.100-wide sweep spans 500 of them, so a ramp moves at
        // most (80 - 25.75) / 500 = 0.11 m per step. Anything at 1 m is a cut, not a ramp.
        Assert.True(worst < 1f,
            $"day {day}: sight jumped {worst:F3} m in one 1/5000-cycle step at phase {worstAt:F4} — " +
            "the band transition is a cut rather than a sweep");

        // Positive control.
        Near(PlayerSightCurve.DaylightSightM, max);
        Assert.True(max - min > 40f,
            $"day {day}: the sweep only spanned {max - min:F2} m — it never actually got dark, so " +
            "the continuity measurement above proves nothing");
    }

    /// <summary><b>The negative control for the sweep test.</b> The identical measurement, run
    /// against a band-stepped implementation — day is bright, night is dark, and the sweeps snap
    /// instead of ramping. If this does not register a large step, the measurement above cannot see
    /// a cut and certifies nothing.</summary>
    [Fact]
    public void NegativeControl_ASteppedBandTransition_IsCaughtByTheSameMeasurement()
    {
        var lights = new List<LightSample> { new(Vector3.Zero, FullFireRadiusM) };
        Vector3 at = At(5f, 0f);
        const int steps = 5000;

        float Stepped(float phase)
        {
            CycleBands.Band band = CycleBands.GetBand(phase, Day1, out _);
            float night = PlayerSightCurve.NightSightM(at, lights);
            return band == CycleBands.Band.Day ? PlayerSightCurve.DaylightSightM : night;
        }

        float worst = 0f;
        float previous = Stepped(0f);
        for (int i = 1; i <= steps; i++)
        {
            float here = Stepped(i / (float)steps);
            worst = Math.Max(worst, Math.Abs(here - previous));
            previous = here;
        }

        Assert.True(worst >= 1f,
            $"a deliberately stepped band transition only moved {worst:F3} m in one step — the " +
            "continuity measurement cannot detect a cut, so the sweep test above certifies nothing");
    }

    // ---------------------------------------------------------------------------------------
    // 6. WHAT COUNTS AS LIGHT
    // ---------------------------------------------------------------------------------------

    /// <summary>A source's sight grant tracks its radius, all the way down: every metre of lit
    /// radius is a step up in sight at the source, and the largest light outsees the smallest by a
    /// wide margin. This is the legibility channel a shrinking light used to be read through, one
    /// layer down — the curve half of it survives the systems that fed it.</summary>
    [Fact]
    public void Lights_SightTracksTheRadius_AcrossTheWholeLadder()
    {
        float previous = 0f;
        for (float r = 1f; r <= FullFireRadiusM; r += 1f)
        {
            var lights = new List<LightSample> { new(Vector3.Zero, r) };
            float sight = PlayerSightCurve.NightSightM(Vector3.Zero, lights);
            Assert.True(sight > previous,
                $"a {r} m light granted {sight:F2} m, no more than a {r - 1} m light's {previous:F2} m");
            previous = sight;
        }

        var thin = new List<LightSample> { new(Vector3.Zero, 1f) };
        Assert.True(previous - PlayerSightCurve.NightSightM(Vector3.Zero, thin) > 15f,
            "an 18 m light and a 1 m light grant nearly the same sight — the curve has stopped " +
            "being a readout of the radius");
    }

    /// <summary>The union spans distant sources, not just overlapping ones: a small light far out
    /// lights ground the big one cannot reach, which is what makes a trail a trail.</summary>
    [Fact]
    public void Lights_ADistantSmallLight_UnionsWithTheBigOne()
    {
        var lights = new List<LightSample>
        {
            new(Vector3.Zero, FullFireRadiusM),
            new(At(40f, 0f), StickRadiusM),
        };

        // Out past the big light's 18 m reach, standing on the small one.
        float onTheStick = PlayerSightCurve.NightSightM(At(40f, 0f), lights);
        float besideIt = PlayerSightCurve.NightSightM(At(48f, 0f), lights);
        Assert.True(onTheStick > PlayerSightCurve.DarkFloorM + 5f,
            $"standing on a {StickRadiusM} m light 40 m from the big one granted {onTheStick:F2} m");
        Near(PlayerSightCurve.DarkFloorM, besideIt);
    }

    /// <summary>Y is ignored. A player standing on a rock two metres above a fire is still in its
    /// light — the reason a spherical test would silently blind anyone on the smallest rise.</summary>
    [Fact]
    public void Lights_HeightDoesNotAffectSight()
    {
        var lights = new List<LightSample> { new(new Vector3(0f, 0f, 0f), FullFireRadiusM) };
        float onTheGround = PlayerSightCurve.NightSightM(new Vector3(2f, 0f, 0f), lights);
        float onARock = PlayerSightCurve.NightSightM(new Vector3(2f, 2f, 0f), lights);
        Near(onTheGround, onARock);
    }

    // ---------------------------------------------------------------------------------------
    // 7. THE REPLICATED TABLE
    // ---------------------------------------------------------------------------------------

    /// <summary>Every way of not knowing reads the floor: unsynced, unknown peer, emptied table.
    /// Paired with the value that IS known, so none of the three passes by accident.</summary>
    [Fact]
    public void Table_EveryUnknownReadsTheFloor()
    {
        var table = new PlayerSightTable();
        Assert.False(table.Synced);
        Near(PlayerSightCurve.DarkFloorM, table.RangeFor(7));

        // Unsynced, and filled anyway: still the floor. A half-built server must not publish.
        table.ServerBeginFrame();
        table.ServerSet(7, 30f);
        Near(PlayerSightCurve.DarkFloorM, table.RangeFor(7));

        // Synced: the positive control.
        table.MarkSynced();
        Near(30f, table.RangeFor(7));
        Near(PlayerSightCurve.DarkFloorM, table.RangeFor(8)); // unknown peer

        // A peer who left is gone on the next frame, not frozen at their last value.
        table.ServerBeginFrame();
        Near(PlayerSightCurve.DarkFloorM, table.RangeFor(7));

        table.Reset();
        Assert.False(table.Synced);
        Near(PlayerSightCurve.DarkFloorM, table.RangeFor(7));
    }

    /// <summary>The wire path: latest-wins for the unreliable broadcast, always-wins for the reliable
    /// late-join sync, wholesale replacement, malformed payloads refused, and a NaN landing on the
    /// floor rather than reaching a consumer.</summary>
    [Fact]
    public void Table_TheWirePath_IsOrderedSanitisedAndWholesale()
    {
        var table = new PlayerSightTable();

        Assert.True(table.Apply(new[] { 1, 2 }, new[] { 20f, 30f }, seq: 5, isSync: false));
        Assert.True(table.Synced);
        Near(20f, table.RangeFor(1));
        Near(30f, table.RangeFor(2));

        // Stale: refused outright, and the good values survive.
        Assert.False(table.Apply(new[] { 1, 2 }, new[] { 5f, 5f }, seq: 4, isSync: false));
        Near(20f, table.RangeFor(1));

        // Same seq: also refused (<=, not <) — a duplicated unreliable delivery is a no-op.
        Assert.False(table.Apply(new[] { 1, 2 }, new[] { 5f, 5f }, seq: 5, isSync: false));
        Near(20f, table.RangeFor(1));

        // Newer: accepted, and peer 2 is GONE rather than merged forward.
        Assert.True(table.Apply(new[] { 1 }, new[] { 25f }, seq: 6, isSync: false));
        Near(25f, table.RangeFor(1));
        Near(PlayerSightCurve.DarkFloorM, table.RangeFor(2));
        Assert.Equal(1, table.Count);

        // A reliable late-join/reconnect sync wins even against a higher applied seq.
        Assert.True(table.Apply(new[] { 1, 9 }, new[] { 12f, 40f }, seq: 2, isSync: true));
        Near(12f, table.RangeFor(1));
        Near(40f, table.RangeFor(9));

        // Malformed: refused, nothing disturbed.
        Assert.False(table.Apply(new[] { 1, 2 }, new[] { 1f }, seq: 99, isSync: true));
        Assert.False(table.Apply(null, new[] { 1f }, seq: 99, isSync: true));
        Assert.False(table.Apply(new[] { 1 }, null, seq: 99, isSync: true));
        Near(12f, table.RangeFor(1));

        // Sanitisation, in the blind direction for a NaN and bounded for a wild value.
        Assert.True(table.Apply(new[] { 1, 2, 3 },
            new[] { float.NaN, 9999f, -50f }, seq: 100, isSync: true));
        Near(PlayerSightCurve.DarkFloorM, table.RangeFor(1));
        Near(PlayerSightCurve.DaylightSightM, table.RangeFor(2));
        Near(PlayerSightCurve.DarkFloorM, table.RangeFor(3));
    }
}
