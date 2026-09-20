using System;
using Godot;
using MpFoundation.Game.Sight;

namespace MpFoundation.Game.World;

/// <summary>
/// Outdoor atmosphere component for the loop-v1 5-minute day (hand-loop design §1): the
/// day → golden-dusk → night → golden-dawn look, driven ENTIRELY by
/// <see cref="CycleDriver.Instance"/>.Phase — no timer of its own, nothing here authors a
/// second clock. Because every visible value is a pure function of the one replicated phase
/// (<see cref="Evaluate"/>), every synced client renders the identical sky for free — zero
/// new netcode.
///
/// <b>Relation to <c>DayNightSky</c> (the reuse decision, stated):</b> this component
/// ADAPTS DayNightSky's proven architecture — pure static Evaluate → record, piecewise
/// keyframe curves over explicit breakpoints, wrap-seam discipline (last key == first key),
/// the Synced gate, single-writer ownership of its Environment — rather than instancing or
/// extending it. Basis: DayNightSky's band structure is hard-keyed to the 120 s tidal
/// timeline (day 0–50 %, golden hour 50–58.3 %, night to 91.7 %) in private static arrays,
/// while design §1 specifies different bands (day 0–55 %, dusk sweep 55–65 %, night 65 %–,
/// dawn sweep at wrap), and this component needs channels DayNightSky has no seam for (fog
/// density/colour, height fog, star energy, the darkness dial). Extending in place would
/// mean editing the shipped file, which this draft packet forbids. No scene ever contains
/// both components, so the "one environment writer" rule holds: this node owns its own
/// WorldEnvironment outright. If loop-v1 later supersedes the tidal timeline, folding these
/// keyframes back into DayNightSky's LightState seam is the natural promotion path.
///
/// <b>Phase bands (design §1, 5-minute day; breakpoints scale with any period):</b> day-1
/// defaults are day 0.000–0.550 (0:00–2:45) · golden dusk 0.550–0.650 (2:45–3:15) · night
/// 0.650–0.900 (3:15–4:30) · golden dawn 0.900–1.000 (4:30–5:00, the wrap). Value pick,
/// stated: §1 gives the dawn sweep no explicit width ("golden hour at wrap"), so it mirrors
/// the dusk sweep's 30 s as the last 10 % of the cycle — a sweep needs width to read as a
/// sweep (§5: dawn sweep out is the relief beat). Dusk and dawn are deliberately NOT the same
/// gold: dusk is hot saturated orange-to-red (the warning channel — §5 "golden hour IS the
/// warning"), dawn is soft pink-gold (the relief).
///
/// <b>Night lengthens across the run (design §1, WP-N1 Scope 2):</b> those boundaries are
/// NOT this component's own copy — every frame's breakpoints come from
/// <see cref="CycleBands.AtmosphereBreakpoints"/>, keyed off <see cref="CycleDriver.CyclesElapsed"/>,
/// so day 1's short night and day 5's long night both read correctly from one source. See
/// <see cref="CycleBands"/>'s class doc for the full escalation table.
///
/// <b>The darkness dial (THRILL-BIBLE §6.2 — RESOLVED 2026-08-09, side (b): the floor
/// drops):</b> the night floor is no longer a Talon-selectable dial. What shipped as three
/// stepped debug levels (a <c>DarknessLevel</c> export, 1..3, so Talon could compare darker
/// nights side by side) existed only so §6.2 could be settled by experience rather than by a
/// constant silently deciding it first — see
/// <c>docs/superpowers/specs/2026-08-09-steam-beta-mvp-PLAN.md</c> §5.1, "the darkness law":
/// "Night, away from all light: near-total black… Fixed near-zero floor." A fixed floor is not
/// a stepped one, so the property that exposed the steps is gone (Issue #214) —
/// <see cref="NightAmbientFloor"/> and <see cref="NightMoonMax"/> are single constants now,
/// reusing the old level-3 values (the darkest step that already existed in the build) rather
/// than inventing fresh ones.
///
/// <b>The sight binding (2026-08-13):</b> the depth fog this component authors is also where
/// <see cref="PlayerSightService"/>'s replicated per-player sight range becomes something a player
/// can see. The phase curves below still author the fog for the time of day; the sight range then
/// thickens and re-colours it so the distance at which the world disappears IS the distance the
/// server says this player can see. All of the arithmetic and every constant live in
/// <see cref="SightPresentation"/> — this component holds only the one piece of state a pure
/// function cannot, the smoothed range (<see cref="RenderedSightRangeM"/>). <b>It reads and never
/// writes:</b> nothing here feeds anything back into the service, and the darkness dial
/// (<see cref="NightAmbientFloor"/>, THRILL-BIBLE §6.2) is not touched by the binding — see
/// <see cref="SightPresentation.FogDensityWithSight"/> for why it does not have to be.
///
/// <b>Shadows:</b> <see cref="SunShadowEnabled"/> defaults false — the house convention
/// (zero real-time shadow casters, PR #55's draw-call measurement; blob shadows do
/// grounding). The golden-hour long-shadow read is delivered the house way: near-horizon
/// sun elevation plus the saturated warm hue sweep. When a world opts in, the strategy is the
/// cheapest real one — one Orthogonal cascade (or two splits) clamped to a short max
/// distance, never a 4-split PSSM.
///
/// <b>Per-world grading (DARK-1, 2026-08-28):</b> the day's sun energy, ambient energy and sky
/// value can be scaled per world, and glow can be enabled per world, without touching a single
/// shared constant — see the "per-world daylight profile" block below. Every knob is an exact
/// no-op at its default and every one of them ramps out with <c>NightFactor</c>, so a graded
/// world's night is bit-identical to an ungraded one's. <b>This is the seam any future
/// "make world X darker" request should use.</b> Re-authoring <see cref="MaxSunEnergy"/>,
/// <c>DayAmbientEnergy</c> or the sky keys moves every world that reads them
/// at once, which is what `.claude/rules/single-writer.md` is pointing at.
/// </summary>
public partial class OutdoorAtmosphere : Node3D
{
    // Ten phase breakpoints — design §1's bands as fractions of the cycle. Day-1 (d=0)
    // values, reproduced exactly by CycleBands.AtmosphereBreakpoints(0), kept here ONLY as a
    // documented reference for what each index means; the live per-frame array now always
    // comes from CycleBands (Scope 1 — one source, not a second copy):
    //   0.000000  0: Day start (morning)          (0:00 of a 5:00 day)
    //   0.275000  1: Midday                       (1:22)
    //   0.550000  2: Dusk sweep start — golden    (2:45, design §1 exact)
    //   0.583333  3: Dusk gold peak               (2:55)
    //   0.616667  4: Dusk purple                  (3:05)
    //   0.650000  5: Night start                  (3:15, design §1 exact)
    //   0.780000  6: Deep night                   (3:54)
    //   0.900000  7: Night end / dawn sweep start (4:30 — value pick, see class doc)
    //   0.955000  8: Dawn gold peak               (4:46)
    //   1.000000  9: Day start again (wrap)       — MUST equal index 0.

    // Elevation in degrees (0 = horizon, 90 = zenith, negative = below horizon). Sun sits
    // near the horizon through both golden sweeps — the long-raking-light read costs a
    // rotation, not a shadow pass (house precedent, DayNightSky).
    private static readonly float[] SunElevDeg = { 14f, 62f, 10f, 5f, -1f, -8f, -20f, -14f, 4f, 14f };
    private static readonly float[] MoonElevDeg = { -30f, -30f, -30f, -26f, -14f, -3f, 55f, 40f, -2f, -30f };

    private static readonly float[] SunEnergyMul = { 1f, 1f, 0.95f, 0.7f, 0.25f, 0f, 0f, 0f, 0.55f, 1f };
    private static readonly float[] MoonEnergyMul = { 0f, 0f, 0f, 0f, 0.05f, 0.2f, 1f, 0.9f, 0.05f, 0f };

    // NightFactor drives ambient energy toward the darkness dial's floor, and nothing else —
    // hue is authored directly per channel (a scalar-derived hue is the "flat orange" trap
    // DayNightSky already replaced).
    private static readonly float[] NightFactor = { 0f, 0f, 0.05f, 0.25f, 0.5f, 0.75f, 1f, 1f, 0.35f, 0f };

    // Star visibility (multiplies the dome shader's emission). Stars pre-glow in the purple
    // dusk key — first stars while light still holds is part of the warning read.
    private static readonly float[] StarMul = { 0f, 0f, 0f, 0.1f, 0.35f, 0.6f, 1f, 1f, 0.1f, 0f };

    /// <summary>Sun light/disc colour. Dusk keys (2–4) are the warning channel: hot,
    /// saturated. Dawn key (8) is deliberately softer/pinker — relief, not threat.</summary>
    private static readonly Color[] SunColorKeys =
    {
        new(1.00f, 0.90f, 0.75f), // 0: morning warm-white.
        new(1.00f, 0.97f, 0.90f), // 1: midday neutral.
        new(1.00f, 0.62f, 0.22f), // 2: dusk start — golden orange.
        new(1.00f, 0.45f, 0.10f), // 3: dusk peak — saturated amber, the warning.
        new(0.75f, 0.30f, 0.50f), // 4: dusk purple.
        new(0.35f, 0.30f, 0.55f), // 5: night start (energy 0 — inert, kept on-arc).
        new(0.35f, 0.30f, 0.55f), // 6: night (inert).
        new(0.35f, 0.30f, 0.55f), // 7: night (inert).
        new(1.00f, 0.72f, 0.45f), // 8: dawn gold — soft, pinker than dusk.
        new(1.00f, 0.90f, 0.75f), // 9: = index 0, the wrap seam.
    };

    private static readonly Color[] SkyTopKeys =
    {
        new(0.50f, 0.65f, 0.86f), // 0: morning blue.
        new(0.45f, 0.68f, 0.95f), // 1: midday bright blue.
        new(0.48f, 0.40f, 0.58f), // 2: dusk start — greying violet.
        new(0.38f, 0.22f, 0.35f), // 3: dusk peak — plum.
        new(0.22f, 0.14f, 0.38f), // 4: dusk purple.
        new(0.09f, 0.09f, 0.22f), // 5: night start — indigo.
        new(0.04f, 0.06f, 0.14f), // 6: deep night.
        new(0.04f, 0.06f, 0.14f), // 7: deep night hold.
        new(0.30f, 0.32f, 0.52f), // 8: dawn — lightening blue.
        new(0.50f, 0.65f, 0.86f), // 9: = index 0.
    };

    private static readonly Color[] SkyHorizonKeys =
    {
        new(0.85f, 0.80f, 0.70f), // 0: soft morning horizon.
        new(0.90f, 0.90f, 0.88f), // 1: near-white day horizon.
        new(1.00f, 0.60f, 0.22f), // 2: dusk start — hot orange, unmistakable.
        new(1.00f, 0.42f, 0.12f), // 3: dusk peak — saturated amber-red.
        new(0.62f, 0.26f, 0.52f), // 4: dusk purple — magenta.
        new(0.18f, 0.15f, 0.32f), // 5: night start — indigo.
        new(0.10f, 0.12f, 0.22f), // 6: deep night.
        new(0.10f, 0.12f, 0.22f), // 7: deep night hold.
        new(0.95f, 0.70f, 0.60f), // 8: dawn — pink-gold, the relief.
        new(0.85f, 0.80f, 0.70f), // 9: = index 0.
    };

    // -----------------------------------------------------------------------------------
    // The night void (WP-N1 follow-up, "Night is night in every direction" — 2026-08-08
    // playtest, D7): BGMode.Sky is a GRADIENT keyed to elevation, and a gradient is a
    // thing you can turn away from — "looking beyond the trees the view is perfect
    // night... but when the camera leaves that view it turns back into what looks like
    // normal day again." AmbientLightSource=Sky inherits the same shape: a surface's
    // ambient term depends on which part of that gradient it faces, so two differently
    // oriented surfaces under IDENTICAL night can read as two different times of day.
    // Neither failure is tunable away inside the gradient architecture — it has to stop
    // being a gradient. The reference is the original night main-menu backdrop's
    // environment (since removed): BGMode.Color (no sky at all — nothing bright
    // to turn toward) plus AmbientSource.Color (a fixed hue, so a surface's ambient no
    // longer depends on which way it faces). "Move the camp's night toward it" (Talon,
    // 2026-08-08) is read here literally: for the true-night span (breakpoints[5]..[8],
    // i.e. from night-start through the top of the dawn sweep — the SAME span
    // CycleBands already treats as "night" for every other system) the camp adopts that
    // exact architecture, then hands back to BGMode.Sky for the rest of the day.
    //
    // WHY THE HANDOFF NEVER POPS. The two crossover keyframes (index 5 and index 8,
    // below) are not independently authored — they are COPIES of SkyHorizonKeys[5] and
    // SkyHorizonKeys[8], the exact colour BGMode.Sky is already rendering on the frame
    // immediately before/after the switch (Curve()'s piecewise Lerp reaches a
    // keyframe's raw value with zero blend exactly at its own breakpoint). Flipping
    // BGMode/AmbientLightSource at that instant swaps the WRITER, not the VALUE, so
    // there is nothing on screen for a player to see change. Whatever "night" ends up
    // looking like (a-moonlit or otherwise) is authored ONLY in indices 6-7 below.
    private static readonly Color NightVoidColor = new(0.012f, 0.022f, 0.038f); // the original night menu backdrop's BackgroundColor.
    private static readonly Color NightAmbientHue = new(0.28f, 0.46f, 0.72f);   // a-moonlit's MoonColour.

    private static readonly Color[] BackgroundVoidKeys =
    {
        SkyHorizonKeys[0], SkyHorizonKeys[1], SkyHorizonKeys[2], SkyHorizonKeys[3],
        SkyHorizonKeys[4], // 0-4: unused — NightVoid is false for the whole day/dusk span.
        SkyHorizonKeys[5], // 5: night starts — the crossover match (see class doc above).
        NightVoidColor,    // 6: deep night — no sky at all.
        NightVoidColor,    // 7: deep night hold.
        SkyHorizonKeys[8], // 8: dawn gold peak — the crossover match back to BGMode.Sky.
        SkyHorizonKeys[9], // 9: unused (= index 0, the wrap seam).
    };

    private static readonly Color[] AmbientVoidKeys =
    {
        SkyHorizonKeys[0], SkyHorizonKeys[1], SkyHorizonKeys[2], SkyHorizonKeys[3],
        SkyHorizonKeys[4], // 0-4: unused.
        SkyHorizonKeys[5], // 5: crossover match — Sky-sourced ambient reads close to this hue here.
        NightAmbientHue,   // 6: deep night — direction-independent moonlight blue.
        NightAmbientHue,   // 7: deep night hold.
        SkyHorizonKeys[8], // 8: crossover match back to Sky-sourced ambient.
        SkyHorizonKeys[9], // 9: unused.
    };

    // Depth-fog density: subtle by day, thickening toward night (distance closes in as the
    // light goes — the cheapest way to make the clock felt; per-pixel blend in the existing
    // pass, no second pass). Starting points seeded around Playground's authored 0.004
    // baseline, the only density this repo has ever looked at; playtest outranks all.
    private static readonly float[] FogDensityKeys = { 0.0015f, 0.001f, 0.002f, 0.003f, 0.004f, 0.006f, 0.009f, 0.009f, 0.003f, 0.0015f };

    private static readonly Color[] FogColorKeys =
    {
        new(0.80f, 0.80f, 0.82f), // 0: morning haze.
        new(0.85f, 0.88f, 0.92f), // 1: day — cool bright.
        new(0.95f, 0.65f, 0.35f), // 2: dusk — warm.
        new(0.90f, 0.50f, 0.25f), // 3: dusk peak — amber fog.
        new(0.55f, 0.35f, 0.50f), // 4: dusk purple.
        new(0.20f, 0.22f, 0.32f), // 5: night start.
        new(0.12f, 0.15f, 0.24f), // 6: deep night — cold.
        new(0.12f, 0.15f, 0.24f), // 7: deep night hold.
        new(0.85f, 0.68f, 0.62f), // 8: dawn — pink.
        new(0.80f, 0.80f, 0.82f), // 9: = index 0.
    };

    // Height-fog density (Environment.FogHeightDensity; positive = denser below FogHeight,
    // i.e. ground-hugging). Zero by day; the ground breathes fog up as night comes.
    private static readonly float[] FogHeightDensityKeys = { 0f, 0f, 0.01f, 0.02f, 0.03f, 0.05f, 0.09f, 0.09f, 0.02f, 0f };

    /// <summary>Matches GameWorld/DayNightSky's daylight peak so this component reads exactly
    /// as bright at midday as the rest of the game.</summary>
    private const float MaxSunEnergy = 1.3f;

    private const float DayAmbientEnergy = 0.55f; // matches the shipped day ambient.

    /// <summary>The shipped night floor — ambient energy at deep night, open sky. THRILL-BIBLE
    /// §6.2 resolved 2026-08-09 (side (b): the floor drops), so this is a single fixed value
    /// per the ratified darkness law (steam-beta-mvp-PLAN.md §5.1: "near-total black… fixed
    /// near-zero floor"), not a Talon-selectable step. Reuses the old level-3 value (0.05) —
    /// the darkest step that already existed in the build — rather than inventing a fresh
    /// number for a direction that was already this specific.</summary>
    private const float NightAmbientFloor = 0.05f;

    /// <summary>The shipped night floor's moon peak — see <see cref="NightAmbientFloor"/> for
    /// why this is one value and not three. Reuses the old level-3 value (0.12).</summary>
    private const float NightMoonMax = 0.12f;

    /// <summary>
    /// <b>A diagnostic dial, not a tuning change</b> (CATCH-1, 2026-08-16). Multiplies the
    /// RENDERED night ambient and moon energy, ramped in by the night factor so daytime output is
    /// bit-identical. 1 is the shipped darkness and is the only value a default launch can have —
    /// written once by <c>Boot</c> from <c>--night-brightness</c> and by nothing else, the same
    /// single-writer shape every launch-gated flag in this repo uses.
    ///
    /// <para>It lives on this class rather than in a flag type of its own because
    /// <c>.claude/rules/single-writer.md</c> makes <c>OutdoorAtmosphere</c> the sole writer of
    /// sky/sun/moon/ambient: a second component reaching for the <c>Environment</c> to brighten it
    /// would be exactly the second writer that rule forbids.</para>
    ///
    /// <para><b>Applied after <see cref="Evaluate"/>, never inside it.</b> That function is defined
    /// as a pure function of the two replicated numbers and several suites assert against it
    /// without a scene; folding a local dial into it would make peers disagree about a record
    /// whose whole point is that they cannot. This is the same division
    /// <see cref="ApplySight"/> already uses. And it touches only what is drawn — no sight curve,
    /// no ward query, nothing the server believes (canon fact 4, the parity law).</para></summary>
    public static float NightBrightnessMul { get; set; } = 1f;

    /// <summary>Below this the brightness dial is skipped outright, so an unset launch runs the
    /// identical code it ran before the dial existed rather than multiplying by a float that
    /// happens to be one.</summary>
    private const float NightBrightnessOff = 1.001f;

    /// <summary>Ceiling on the boosted moon so a large multiplier cannot make the moon brighter
    /// than midday sun and turn the diagnostic into a different bug to look at.</summary>
    private const float BoostedMoonMax = MaxSunEnergy;

    // a-moonlit's MoonColour (MenuLook.Presets[0], "chosen in the chair" 2026-08-08) — the
    // approved target, not this component's own invention. Deliberately the SAME literal as
    // NightAmbientHue above: a-moonlit uses one moon colour for both the light and the
    // ambient it casts, and there is no reason for the camp's directional moon light to be a
    // paler, uncredited colour of its own now that a specific one has been signed off.
    private static readonly Color MoonColor = new(0.28f, 0.46f, 0.72f);

    /// <summary>Default false — house convention (no real-time shadow casters; PR #55).
    /// True enables a short-range cascade. <b>Per-world, never global</b> — see the
    /// per-world daylight profile block below, and DARK-1's report for the measurement that
    /// re-opened PR #55's ruling.</summary>
    [Export]
    public bool SunShadowEnabled { get; set; }

    // -----------------------------------------------------------------------------------
    // THE PER-WORLD DAYLIGHT PROFILE (DARK-1, 2026-08-28)
    // -----------------------------------------------------------------------------------
    //
    // Talon, 2026-08-28, on the bubble-test movement harness: it "looks clinical and sterile
    // rather than atmospheric" and has "drifted into a bright white test lab look" — and,
    // second message, "I would like bolder overall."
    //
    // WHY THESE ARE INSTANCE PROPERTIES AND NOT RE-AUTHORED CONSTANTS. `.claude/rules/
    // single-writer.md` makes this class the sole writer of sky/sun/moon/ambient FOR EVERY
    // WORLD. MaxSunEnergy, DayAmbientEnergy and the sky keys are shared by every world that
    // has read them, and suites assert against their rendered result.
    // Re-authoring any of them to fix one world moves all of them. So the shared curves are
    // untouched and every knob below defaults to an EXACT no-op: a world that sets none of
    // them renders the same floats it rendered before this block existed.
    //
    // WHY THEY RAMP OUT WITH NightFactor. Night in this repo is already very dark and its fog
    // is already a wall; the brief is about the DAY. Every scale below is interpolated toward
    // 1.0 by NightFactor — the exact mirror of ApplyPlayLight's lift — so at deep night
    // (NightFactor = 1) the profile is arithmetically inert and the shipped night is preserved
    // exactly. Dusk gets a partial move, which is where the grade wants it anyway.
    //
    // WHY THE SKY SCALE IS ALSO PART OF THE AMBIENT SCALE. AmbientLightSource is Sky, so the
    // sky keys ARE the ambient hue and a darker sky is a darker bounce for free. The two knobs
    // compose; do not tune one expecting the other to hold still.

    /// <summary>Multiplies the sun's daytime energy. 1 = the shipped curve exactly.</summary>
    [Export]
    public float DaySunEnergyScale { get; set; } = 1f;

    /// <summary>Multiplies the daytime ambient energy. 1 = the shipped curve exactly. Ramped
    /// out by <c>NightFactor</c>, so <see cref="NightAmbientFloor"/> — THRILL-BIBLE §6.2's live
    /// blank, Talon's — is still reached exactly and is NOT touched by this knob.</summary>
    [Export]
    public float DayAmbientEnergyScale { get; set; } = 1f;

    /// <summary>Multiplies the daytime sky gradient (top and horizon). 1 = the shipped keys
    /// exactly. This is the knob that takes the near-white day horizon
    /// (<c>SkyHorizonKeys[1]</c> = 0.90/0.90/0.88) off the top of the histogram. It is a VALUE
    /// move and deliberately not a hue move — the DARK-1 direction may not re-spend
    /// THRILL-BIBLE §10's colour-temperature row inside this level, which the lake already
    /// holds <c>spent-here</c>.</summary>
    [Export]
    public float DaySkyColorScale { get; set; } = 1f;

    /// <summary>Two-split PSSM instead of the single Orthogonal cascade, when
    /// <see cref="SunShadowEnabled"/> is on. Cost measured in DARK-1's report, not assumed.</summary>
    [Export]
    public bool SunShadowTwoSplits { get; set; }

    /// <summary>Cascade range in metres; the default is the shipped 80 m. Keep it tight to the
    /// play area — paying for a 300 m map to shade a 100 m arm is the expensive mistake.</summary>
    [Export]
    public float SunShadowMaxDistanceM { get; set; } = 80f;

    /// <summary>Environment glow/bloom, per world. Off everywhere by default: it is a
    /// full-screen post pass and no shipped world has ever paid for one. On, it is what makes an
    /// emissive surface read as GLOWING rather than merely brighter — without it the bubbles'
    /// night emission is a value change nobody can name.</summary>
    [Export]
    public bool GlowEnabled { get; set; }

    /// <summary>Linear-HDR luminance above which a pixel blooms. Deliberately above the
    /// darkened ground and below the bubbles' emission peak, so the bloom is a property of the
    /// things that emit rather than a haze over the whole frame.</summary>
    [Export]
    public float GlowThreshold { get; set; } = 0.7f;

    /// <summary>Bloom intensity. Modest by construction — THRILL-BIBLE §8.2: a glow that
    /// spikes is a startle nobody directed.</summary>
    [Export]
    public float GlowIntensity { get; set; } = 0.85f;

    /// <summary>World-space Y where ground fog sits at full density (Environment.FogHeight).</summary>
    [Export]
    public float FogHeightM { get; set; } = 2.0f;

    /// <summary>Dev/test hook (--cycle-start-day via LaunchOptions): added to
    /// <see cref="CycleDriver.CyclesElapsed"/> before every <see cref="CycleBands"/> lookup, so
    /// a specific day's escalation (e.g. day 5's long night) is reachable in one launch instead
    /// of waiting through the days before it. The real replicated CyclesElapsed is never
    /// touched — CycleDriver.cs stays untouched by this packet (Scope 1) — this is purely a
    /// local, presentation-side day-index derivation.</summary>
    [Export]
    public int DayIndexOffset { get; set; }

    /// <summary>The day index actually fed to <see cref="CycleBands"/> this frame (already
    /// clamped to CycleBands.MaxDayIndex) — exposed for the dev readout (Scope 6).</summary>
    public int EffectiveDayIndex { get; private set; }

    // -----------------------------------------------------------------------------------
    // The sight binding — see the class doc, and SightPresentation for all of the reasoning
    // -----------------------------------------------------------------------------------

    /// <summary>Where the local player's sight range comes from, as (is it authoritative yet, how
    /// many metres). <b>Optional, and null is the shipped path:</b> left null this component reads
    /// <see cref="PlayerSightService.Instance"/> directly, which is the same static-singleton
    /// idiom it already uses for <see cref="CycleDriver.Instance"/> and for the same reason — the
    /// atmosphere is created at runtime by the world and a NodePath between them would bake in an
    /// assumption neither owns.
    ///
    /// <para>The seam exists so a dev lab or a scene test can drive the night to a known sight
    /// range without standing up a multiplayer session, exactly as <c>DayIndexOffset</c> exists so
    /// a specific day is reachable in one launch. <b>It is an input only.</b> Nothing this
    /// component does can be observed by the service.</para></summary>
    public Func<(bool Synced, float RangeM)>? SightSource { get; set; }

    /// <summary>The sight range this frame is actually being rendered at, metres — the smoothed,
    /// gated value, not the raw replicated one. Starts at
    /// <see cref="PlayerSightCurve.DarkFloorM"/> so a frame that renders before any authoritative
    /// table has landed renders blind rather than omniscient. Exposed for the dev readout and for
    /// scene tests; no gameplay may read it.</summary>
    public float RenderedSightRangeM { get; private set; } = PlayerSightCurve.DarkFloorM;

    // False until the first SYNCED reading arrives, which is the one moment the smoothing is
    // deliberately skipped: a joining player should not watch their sight sweep up from the
    // darkness floor over half a second while the world pops into existence around them. Every
    // subsequent change — including losing sync again — eases, because those are events in the
    // world rather than the client catching up with it.
    private bool _snappedToAuthority;

    /// <summary>Everything a frame needs — the pure, scene-free half (DayNightSky.LightState
    /// pattern), so a future headless test can walk the wrap seam without a scene.
    /// <paramref name="NightFactor"/> is 0 by day and 1 at deep night; it is carried out of
    /// <see cref="Evaluate"/> rather than recomputed because the presentation stages need the
    /// same value, and two derivations of one curve is how they drift apart.</summary>
    public readonly record struct AtmoState(
        float SunElevDeg, float SunAzimuthDeg, Color SunColor, float SunEnergy,
        float MoonElevDeg, float MoonAzimuthDeg, Color MoonColor, float MoonEnergy,
        float AmbientEnergy, Color SkyTop, Color SkyHorizon,
        float FogDensity, Color FogColor, float FogHeightDensity, float StarEnergy,
        float NightFactor,
        bool NightVoid, Color BackgroundColor, Color AmbientColor);

    /// <summary>Pure function: phase [0,1) + day index → full atmosphere state. All clients
    /// derive identical output from the identical replicated phase AND the identical
    /// replicated <see cref="CycleDriver.CyclesElapsed"/> (Scope 2: night lengthens across the
    /// run, so day index is now load-bearing input, not just phase). No longer takes a darkness
    /// level — THRILL-BIBLE §6.2 resolved to a single fixed floor (see
    /// <see cref="NightAmbientFloor"/>), so there is nothing left to select.</summary>
    public static AtmoState Evaluate(float phase, int cyclesElapsed)
    {
        phase = Mathf.PosMod(phase, 1f); // defensive; callers pass CycleDriver.Phase.
        float[] breakpoints = CycleBands.AtmosphereBreakpoints(cyclesElapsed);

        float night = Curve(NightFactor, phase, breakpoints);
        float sunAzimuth = phase * 360f;
        float moonAzimuth = Mathf.PosMod(sunAzimuth + 180f, 360f); // literal 180° opposition.

        // The night void (see the class doc above BackgroundVoidKeys): live for the same
        // night-start..dawn-gold-peak span the sky/ambient crossover keyframes are authored
        // against — breakpoints[5] through breakpoints[8], day-dependent like everything else
        // sourced from CycleBands.
        bool nightVoid = phase >= breakpoints[5] && phase < breakpoints[8];
        Color backgroundColor = nightVoid ? ColorCurve(BackgroundVoidKeys, phase, breakpoints) : default;
        Color ambientColor = nightVoid ? ColorCurve(AmbientVoidKeys, phase, breakpoints) : default;

        return new AtmoState(
            Curve(SunElevDeg, phase, breakpoints), sunAzimuth,
            ColorCurve(SunColorKeys, phase, breakpoints), MaxSunEnergy * Curve(SunEnergyMul, phase, breakpoints),
            Curve(MoonElevDeg, phase, breakpoints), moonAzimuth,
            MoonColor, NightMoonMax * Curve(MoonEnergyMul, phase, breakpoints),
            Mathf.Lerp(DayAmbientEnergy, NightAmbientFloor, night),
            ColorCurve(SkyTopKeys, phase, breakpoints), ColorCurve(SkyHorizonKeys, phase, breakpoints),
            Curve(FogDensityKeys, phase, breakpoints), ColorCurve(FogColorKeys, phase, breakpoints),
            Curve(FogHeightDensityKeys, phase, breakpoints), Curve(StarMul, phase, breakpoints),
            night,
            nightVoid, backgroundColor, ambientColor);
    }

    /// <summary>Piecewise-linear interpolation over the given day's <paramref name="breakpoints"/>
    /// (DayNightSky's exact scheme — wrap seam holds because every array's last entry equals
    /// its first; <see cref="CycleBands.AtmosphereBreakpoints"/> preserves that by construction).</summary>
    private static float Curve(float[] values, float phase, float[] breakpoints)
    {
        for (int i = 0; i < breakpoints.Length - 1; i++)
        {
            if (phase < breakpoints[i + 1] || i == breakpoints.Length - 2)
            {
                float span = breakpoints[i + 1] - breakpoints[i];
                float t = span > 0f ? (phase - breakpoints[i]) / span : 0f;
                return Mathf.Lerp(values[i], values[i + 1], Mathf.Clamp(t, 0f, 1f));
            }
        }
        return values[^1];
    }

    private static Color ColorCurve(Color[] values, float phase, float[] breakpoints)
    {
        for (int i = 0; i < breakpoints.Length - 1; i++)
        {
            if (phase < breakpoints[i + 1] || i == breakpoints.Length - 2)
            {
                float span = breakpoints[i + 1] - breakpoints[i];
                float t = span > 0f ? (phase - breakpoints[i]) / span : 0f;
                return values[i].Lerp(values[i + 1], Mathf.Clamp(t, 0f, 1f));
            }
        }
        return values[^1];
    }

    private DirectionalLight3D _sun = null!;
    private DirectionalLight3D _moon = null!;
    private Godot.Environment _env = null!;
    private ProceduralSkyMaterial _skyMat = null!;
    private ShaderMaterial? _starMat;

    public override void _Ready()
    {
        _sun = new DirectionalLight3D { Name = "Sun", ShadowEnabled = false };
        if (SunShadowEnabled)
        {
            // Off by default everywhere; a world opts in. Single Orthogonal split is the
            // cheapest real cascade and stays the default shape — SunShadowTwoSplits buys
            // near-field resolution over a longer range when a world has measured that it
            // wants it (DARK-1). Nothing here is a 4-split PSSM.
            _sun.ShadowEnabled = true;
            _sun.DirectionalShadowMode = SunShadowTwoSplits
                ? DirectionalLight3D.ShadowMode.Parallel2Splits
                : DirectionalLight3D.ShadowMode.Orthogonal;
            _sun.DirectionalShadowMaxDistance = SunShadowMaxDistanceM;
            // Blending the split seam costs a second shadow sample on the overlap band for a
            // transition nobody is looking for in a block world. Off is the cheap read.
            _sun.DirectionalShadowBlendSplits = false;
            // Fade the far edge instead of ending the cascade on a hard line across the ground.
            _sun.DirectionalShadowFadeStart = 0.85f;
            // Peter-panning vs. acne on 1 m-scale blocks standing on a flat plane: normal bias
            // carries this, bias stays low so contact stays attached to the block that casts it.
            _sun.ShadowBias = 0.03f;
            _sun.ShadowNormalBias = 1.4f;
            // Not fully black. A shadow at opacity 1 under a dark ambient is a hole, and a hole
            // reads as missing geometry rather than as shade — the same failure the histogram
            // has at the other end.
            _sun.ShadowOpacity = 0.86f;
        }
        AddChild(_sun);

        _moon = new DirectionalLight3D
        {
            Name = "Moon",
            ShadowEnabled = false,
            // LightOnly (2026-08-08, "also in scope"): Godot's default LightAndSky makes
            // every DirectionalLight3D automatically paint an automatic glow-disc into the sky
            // shader scaled by its own energy — which is exactly "the moon reads as a bright
            // light source" Talon flagged, and it is not tunable away by any energy/colour
            // value on this light. BuildMoonDisc below is the deliberate replacement: a
            // fixed-colour shape, not a light's own glow.
            SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly,
        };
        AddChild(_moon);

        _skyMat = new ProceduralSkyMaterial
        {
            SkyTopColor = SkyTopKeys[0],
            SkyHorizonColor = SkyHorizonKeys[0],
            GroundBottomColor = SkyHorizonKeys[0],
            GroundHorizonColor = SkyHorizonKeys[0],
        };
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _skyMat },
            // Ambient colour comes from the sky (AmbientLightColor is inert under
            // sky-ambient — verified repo trap); the sky keys ARE the ambient hue channel.
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = DayAmbientEnergy,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogDensity = FogDensityKeys[0],
            FogLightColor = FogColorKeys[0],
            FogHeight = FogHeightM,
            FogHeightDensity = FogHeightDensityKeys[0],
            // The sky is the clock and never lies (design §2); fog must not deny the clock
            // read, so it only lightly tints the sky. Value pick: 0.15.
            FogSkyAffect = 0.15f,
        };
        if (GlowEnabled)
            EnableGlow(_env);
        AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = _env });

        BuildStarDome();
        BuildMoonDisc();

        // Apply once at a sane default so no garbage frame renders while waiting for the
        // first authoritative phase (DayNightSky's hold-at-defined-state discipline).
        EffectiveDayIndex = CycleBands.DayIndex(DayIndexOffset);
        Apply(Evaluate(0f, DayIndexOffset));
    }

    /// <summary>Bloom, for the worlds that opt in (<see cref="GlowEnabled"/>).
    ///
    /// <para><b>Why this is not optional polish for an emissive surface.</b> An emissive
    /// material without a glow pass is arithmetically brighter and perceptually identical — it
    /// still ends at its own silhouette, so it reads as "a pale object", never as "a light".
    /// <c>bubble_film.gdshader</c>'s own comment makes the point from the other side: a
    /// 6 %-alpha film against a night sky "is nothing regardless of its emission". Emission and
    /// bloom are two halves of one effect and shipping either alone ships nothing.</para>
    ///
    /// <para><b>Levels 1–4 only, and the threshold is the whole design.</b> The wide levels
    /// (5–7) are a full-screen haze that would lift the frame's black point — which is the exact
    /// defect this packet exists to remove, arriving by the back door. The threshold is set
    /// above the graded ground and below the emitters, so bloom is a property of the things that
    /// emit rather than of the picture.</para></summary>
    private void EnableGlow(Godot.Environment env)
    {
        env.GlowEnabled = true;
        env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive;
        env.GlowHdrThreshold = GlowThreshold;
        env.GlowHdrScale = 2f;
        // THE CEILING, AND IT IS LOAD-BEARING RATHER THAN A SAFETY MARGIN. A glow pass is a
        // contract with every emissive material already in the level, and those were authored
        // when nothing bloomed: `BubbleCounterDisplay.tscn` carries
        // `emission_energy_multiplier = 200`, which was a reasonable way to say "bright" in a
        // world with no bloom and is a floodlight in one with it — measured, it blew a white disc
        // over the hub at every threshold that let the bubbles through. The cap clamps what any
        // pixel may CONTRIBUTE to the bloom without touching what it renders as, so a 200x
        // emitter halos exactly as hard as a bubble does and no harder. Fixing it here rather
        // than in the prop is also correct ownership: that scene belongs to BT-8, and one number
        // in the world's own environment is a smaller blast radius than editing someone's .tscn.
        env.GlowHdrLuminanceCap = 2.2f;
        env.GlowIntensity = GlowIntensity;
        env.GlowStrength = 1f;
        // Bloom is the pre-threshold lift applied to the WHOLE frame; anything above zero is a
        // milky black point. The threshold does this effect's work, not this.
        env.GlowBloom = 0f;
        // THE INDEX IS ZERO-BASED AND THE DOC ABOVE IS ONE-BASED, WHICH COST A LAUNCH ERROR.
        // Godot's SetGlowLevel takes 0..6 for the seven levels; the first version of this loop
        // ran 1..7, so it threw `Index p_level = 7 is out of bounds` on EVERY launch of every
        // world that opted in, and — quietly worse than the error — it enabled levels 2-5
        // instead of the 1-4 the comment claims. The error printed to stderr and nothing failed,
        // which is the same class of silence as the `night_glow` uniform this packet also fixed.
        for (int i = 0; i <= 6; i++)
            env.SetGlowLevel(i, i <= LastNarrowGlowLevelIndex ? 1f : 0f);
    }

    /// <summary>Zero-based index of the last glow level this world blooms — 3, i.e. levels 1-4
    /// in the editor's one-based labelling. Named rather than inlined because the off-by-one
    /// between the two conventions has already produced one defect here.</summary>
    private const int LastNarrowGlowLevelIndex = 3;

    /// <summary>How much daylight there is, 0 (night) … 1 (full sun), from the sun's own
    /// <see cref="Light3D.LightEnergy"/>.
    ///
    /// <para><b>Why a shared static rather than the same clamp written twice.</b> Two surfaces now
    /// need this number — <c>grass.gdshader</c>'s blades and <c>ground_wear.gdshader</c>'s terrain —
    /// and they must agree, because the whole point of the ground fix is that the two stop
    /// disagreeing about what time it is. Two derivations of one curve is how they drift apart
    /// (the same reasoning that carries <see cref="AtmoState.NightFactor"/> out of
    /// <see cref="Evaluate"/> instead of recomputing it downstream).</para>
    ///
    /// <para>Read from ENERGY, not from the clock phase: energy is what the atmosphere actually
    /// decided this frame, so a re-tune of the day
    /// curve cannot leave a consumer reading a stale idea of daylight, and no second clock is
    /// authored. <see cref="MaxSunEnergy"/> is 1.3, so midday clamps to 1.</para></summary>
    public static float SkyLight(float sunEnergy) => Mathf.Clamp(sunEnergy, 0f, 1f);

    /// <summary>2026-08-08 playtest, "also in scope": Talon's own words — the moon "currently
    /// reads as a bright light source"; he wants "a classic cartoon moon — a shape you look
    /// at, not a lamp." The menu's own moon achieves its look entirely as ambient colour with
    /// no visible disc (see the class doc on <see cref="MoonColor"/>), so there is no value to
    /// copy across here — this shape is new work, scoped exactly as the decision default asks:
    /// an unlit disc with a hard edge, no art pass.
    ///
    /// <b>Why a real sphere and not a billboard+shader.</b> At <see cref="MoonDiscDistanceM"/>
    /// parallax across the whole playable camp is imperceptible, so a plain
    /// <see cref="SphereMesh"/> gives an exactly circular, genuinely hard-edged silhouette
    /// from any angle with zero shader code — a billboard quad would need a manual
    /// radial-alpha cutoff to get the same edge and buys nothing here that the primitive
    /// doesn't already give for free.</summary>
    private const float MoonDiscDistanceM = 650f; // inside StarDome's 700 m radius.
    private const float MoonDiscRadiusM = 34f;    // stylised, not real-moon scale — PROPORTION-STYLE's exaggeration dial, deliberately generous so it reads at a glance.
    private static readonly Color MoonDiscColor = new(0.92f, 0.95f, 1.0f);

    private MeshInstance3D? _moonDisc;

    private void BuildMoonDisc()
    {
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = MoonDiscColor,
            EmissionEnabled = true,
            Emission = MoonDiscColor,
            EmissionEnergyMultiplier = 0.6f,
            // Same reason StarDome's own shader sets fog_disabled: at 650m, NightDome's inside
            // fog density (exponential, tuned for TERRESTRIAL distances) would extinguish this
            // disc almost completely before it ever reached the camera. Depth fog is meant to
            // occlude the swallowed WORLD, not the sky sitting behind it.
            DisableFog = true,
        };
        _moonDisc = new MeshInstance3D
        {
            Name = "MoonDisc",
            Mesh = new SphereMesh
            {
                Radius = MoonDiscRadiusM, Height = MoonDiscRadiusM * 2f, RadialSegments = 24, Rings = 12,
            },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // Never distance-culled or frustum-popped, same reasoning as StarDome: it is meant
            // to be visible from anywhere in the playable field.
            ExtraCullMargin = MoonDiscRadiusM,
            Visible = false,
        };
        AddChild(_moonDisc);
    }

    /// <summary>Positions the disc along the moon light's OWN already-updated basis rather
    /// than re-deriving an elevation/azimuth rotation independently — two computations of one
    /// direction is how they drift apart, the same reasoning <see cref="AtmoState.NightFactor"/>
    /// is carried rather than recomputed. A <see cref="DirectionalLight3D"/> shines along its
    /// local -Z, so the visible object sits back along +Z.</summary>
    private void ApplyMoonDisc(in AtmoState s)
    {
        if (_moonDisc is null)
            return;
        // Below the horizon: hidden outright, not faded — a fade would read as a second light
        // source rising out of the ground instead of a shape that has simply set.
        _moonDisc.Visible = s.MoonElevDeg > 0f;
        if (_moonDisc.Visible)
            _moonDisc.Position = _moon.GlobalTransform.Basis.Z * MoonDiscDistanceM;
    }

    private void BuildStarDome()
    {
        var shader = GD.Load<Shader>("res://resources/shaders/StarDome.gdshader");
        if (shader == null)
            return; // stars are additive polish; the component functions without them.
        _starMat = new ShaderMaterial { Shader = shader };
        var dome = new MeshInstance3D
        {
            Name = "StarDome",
            Mesh = new SphereMesh { Radius = 700f, Height = 1400f, RadialSegments = 32, Rings = 16 },
            MaterialOverride = _starMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // Never distance-culled or frustum-popped: the dome surrounds everything.
            ExtraCullMargin = 700f,
        };
        AddChild(dome);
    }

    public override void _Process(double delta)
    {
        // Hold the last-applied state until the driver is synced — a client must never
        // render t=0 first (the named most-likely bug; DayNightSky._Process's exact guard).
        if (CycleDriver.Instance is not { Synced: true } driver)
            return;
        int cyclesElapsed = driver.CyclesElapsed + DayIndexOffset;
        EffectiveDayIndex = CycleBands.DayIndex(cyclesElapsed);
        AtmoState state = Evaluate(driver.Phase, cyclesElapsed);
        Apply(ApplyPlayLight(ApplySight(ApplyProfile(state), delta)));
    }

    /// <summary>Folds this world's daylight profile into the frame. Runs FIRST of the four
    /// presentation stages, before sight and before the play-light lift, because it is a
    /// statement about how bright this world's daylight is rather than a correction applied on
    /// top of one — and because <see cref="ApplySight"/> reads the fog it must already have.
    ///
    /// <para>Every scale ramps to 1 by <c>NightFactor</c>, so this returns <paramref name="s"/>
    /// unchanged at deep night and on every world that leaves the knobs at their defaults. That
    /// is not a convenience: it is what makes a per-world grade safe on a class the
    /// single-writer rule points at every world's Environment.</para></summary>
    private AtmoState ApplyProfile(in AtmoState s)
    {
        // Cheapest possible early-out, and the one that guarantees an untouched world runs the
        // identical code it ran before this stage existed (NightBrightnessMul's own shape).
        if (DaySunEnergyScale == 1f && DayAmbientEnergyScale == 1f && DaySkyColorScale == 1f)
            return s;

        float night = Mathf.Clamp(s.NightFactor, 0f, 1f);
        float sunMul = Mathf.Lerp(DaySunEnergyScale, 1f, night);
        float ambientMul = Mathf.Lerp(DayAmbientEnergyScale, 1f, night);
        float skyMul = Mathf.Lerp(DaySkyColorScale, 1f, night);

        return s with
        {
            SunEnergy = s.SunEnergy * sunMul,
            AmbientEnergy = s.AmbientEnergy * ambientMul,
            SkyTop = ScaleValue(s.SkyTop, skyMul),
            SkyHorizon = ScaleValue(s.SkyHorizon, skyMul),
            // THE FOG COLOUR RIDES THE SKY SCALE, and this was the biggest single contributor
            // left after the first measured pass. FogColorKeys[1] is 0.85/0.88/0.92 — a
            // NEAR-WHITE veil laid over everything at distance, which in a level with 100 m
            // sightlines is most of the frame. Coupling it to the sky is also the physically
            // honest relation: fog is lit by the sky, so a dim sky cannot produce bright fog.
            // ApplySight runs after this and takes FogColor as an INPUT, so the scaled value
            // flows through its night blend rather than being overwritten by it.
            FogColor = ScaleValue(s.FogColor, skyMul),
        };
    }

    /// <summary>Scales a colour's VALUE while leaving its alpha alone. A flat per-channel
    /// multiply, deliberately: it darkens without rotating hue, which is the constraint the
    /// DARK-1 direction puts on this whole pass.</summary>
    private static Color ScaleValue(Color c, float mul) => new(c.R * mul, c.G * mul, c.B * mul, c.A);

    /// <summary>
    /// Folds <see cref="NightBrightnessMul"/> into the frame. Last of the presentation stages
    /// (sight, then this) and deliberately so: it is a lift on whatever the earlier stage arrived
    /// at, so a player on a dark night gets the boost applied to the darkness they are actually
    /// in rather than to the open-sky number.
    ///
    /// <para>The lift is interpolated by <c>NightFactor</c>, so the day is returned untouched and
    /// dusk brightens gradually instead of stepping. Pure arithmetic on the record, no node
    /// access — see the property's own doc for why it is not in <see cref="Evaluate"/>.</para>
    /// </summary>
    private static AtmoState ApplyPlayLight(in AtmoState s)
    {
        if (NightBrightnessMul < NightBrightnessOff)
            return s;
        float lift = Mathf.Lerp(1f, NightBrightnessMul, s.NightFactor);
        return s with
        {
            AmbientEnergy = s.AmbientEnergy * lift,
            MoonEnergy = Mathf.Min(s.MoonEnergy * lift, BoostedMoonMax),
        };
    }

    /// <summary>Advances the smoothed sight range and folds it into the frame's fog.
    ///
    /// <para><b>The sight range is not carried in <see cref="AtmoState"/>, on purpose.</b> That
    /// record is defined as a pure function of phase and day index — every field in it is
    /// reproducible from those two replicated numbers, which is what lets the wrap-seam tests
    /// assert against it without a scene. Sight is
    /// a function of where the player is standing and what is burning, so putting it in there
    /// would quietly make that record impure and every test built on the property wrong.</para></summary>
    private AtmoState ApplySight(in AtmoState s, double delta)
    {
        (bool synced, float raw) = ReadSight();
        float target = SightPresentation.TargetRangeM(synced, raw);
        if (synced && !_snappedToAuthority)
        {
            _snappedToAuthority = true;
            RenderedSightRangeM = target;
        }
        else
        {
            RenderedSightRangeM = SightPresentation.Smooth(RenderedSightRangeM, target, delta);
        }

        return s with
        {
            FogDensity = SightPresentation.FogDensityWithSight(
                s.FogDensity, s.NightFactor, RenderedSightRangeM),
            FogColor = SightPresentation.FogColorWithSight(
                s.FogColor, NightVoidColor, s.NightFactor),
        };
    }

    /// <summary>The one read of the sight service, in one place. Absent service, absent instance
    /// and unsynced table all resolve to "not authoritative", which
    /// <see cref="SightPresentation.TargetRangeM"/> turns into the darkness floor — blind, never
    /// omniscient. A world with no sight service at all (every dev lab, the playground) therefore
    /// renders its night at the floor, and the day is untouched because the weight is zero there.</summary>
    /// <para><b>BASE-1 (2026-09-19): there is no sight service in this repo.</b> The whole
    /// server-authoritative sight stack (<c>PlayerSightService</c>, <c>PlayerSightTable</c>) was
    /// pruned at the fork — it existed for an outdoor night nobody plays here — so the absent-service
    /// branch below is now the only branch, and it resolves exactly as it always did for a world
    /// that had no service: not authoritative, which is the darkness floor. The pure presentation
    /// half (<see cref="SightPresentation"/>, <see cref="PlayerSightCurve"/>) is kept because this
    /// fog math and its xUnit suite are still live.</para>
    private (bool Synced, float RangeM) ReadSight()
        => SightSource != null ? SightSource() : (false, PlayerSightCurve.DarkFloorM);

    private void Apply(in AtmoState s)
    {
        _sun.RotationDegrees = new Vector3(-s.SunElevDeg, s.SunAzimuthDeg, 0);
        _sun.LightColor = s.SunColor;
        _sun.LightEnergy = s.SunEnergy;

        _moon.RotationDegrees = new Vector3(-s.MoonElevDeg, s.MoonAzimuthDeg, 0);
        _moon.LightColor = s.MoonColor;
        _moon.LightEnergy = s.MoonEnergy;

        // The night void (class doc above BackgroundVoidKeys): swap the WRITER, never the
        // value, at the two crossover breakpoints — see that doc for why the swap is
        // guaranteed invisible. Sky-mode's own gradient fields are still written every frame
        // below (cheap, and it keeps the non-void state fully authored for the instant the
        // gate flips back), they are simply not what is on screen while NightVoid is true.
        if (s.NightVoid)
        {
            _env.BackgroundMode = Godot.Environment.BGMode.Color;
            _env.BackgroundColor = s.BackgroundColor;
            _env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            _env.AmbientLightColor = s.AmbientColor;
        }
        else
        {
            _env.BackgroundMode = Godot.Environment.BGMode.Sky;
            _env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
        }
        // The darkness dial's ENERGY (THRILL-BIBLE §6.2, untouched by this packet) applies
        // identically in both architectures — only the colour SOURCE changed above.
        _env.AmbientLightEnergy = s.AmbientEnergy;

        _skyMat.SkyTopColor = s.SkyTop;
        _skyMat.SkyHorizonColor = s.SkyHorizon;
        _skyMat.GroundBottomColor = s.SkyHorizon;
        _skyMat.GroundHorizonColor = s.SkyHorizon;

        _env.FogDensity = s.FogDensity;
        _env.FogLightColor = s.FogColor;
        _env.FogHeightDensity = s.FogHeightDensity;

        _starMat?.SetShaderParameter("star_energy", s.StarEnergy);

        ApplyMoonDisc(s);
    }
}
