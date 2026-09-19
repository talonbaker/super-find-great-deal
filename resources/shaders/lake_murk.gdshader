// The lake surface -- BUBBLE-TEST COPY, WITH THE MURK PASS. (Ported by BT-9, 2026-08-27.)
//
// =================================================================================================
// READ THIS BEFORE EDITING: THIS FILE IS A PORT, AND IT IS A SECOND COPY ON PURPOSE
// =================================================================================================
//
// PROVENANCE. Byte-for-byte `shaders/water/water_surface.gdshader` from the sibling repo
// `C:/repos/shader-lab`, with exactly one line changed: the `#include` at the top of the shader
// body now points at `res://resources/shaders/toon_lib.gdshaderinc` instead of the lab's path.
// The two `toon_lib.gdshaderinc` files were diffed and are identical, so the include is a path
// fix and not a behaviour change. Nothing else was touched.
//
// WHAT SAIL ALREADY SHIPS, AND WHY THIS IS NOT IT. `assets/materials/water/water_surface.gdshader`
// (986 lines) is THE SAME SHADER at an earlier point: the lab's copy (1342 lines) is a strict
// superset that adds the three-part MURK pass -- `silt_scatter` / `silt_density` / `silt_col` /
// `silt_night_scale` (path-length scattering), `turbidity_veil` (depth-independent shallow
// cloudiness) and `sightline_scale` (one multiplier on the day/night extinction PAIR) -- plus a
// `time_scrub` for reproducible captures. Sail's shipped copy has none of it. That was measured,
// not assumed: only 15 lines exist in Sail's copy and not in the lab's, and every one of them is
// either the include or a `TIME` the lab replaced with the scrubbable clock.
//
// WHY A SECOND FILE RATHER THAN UPGRADING THE SHIPPED ONE. `assets/materials/water/water_surface.
// gdshader` is loaded by `camp_gen.gd` (via `water_surface.tres`) and baked into `Camp.tscn`. Adding
// murk there would re-grade the CAMP lake as a side effect of a bubble-test packet, in a repo whose
// worldgen surface is single-writer. The bubble test gets its own material; the camp lake is not
// touched by this branch at all. If the murk is later wanted in camp, the correct move is to port
// these same blocks into the shipped file as a deliberate pass -- not to re-point camp at this file.
//
// THE DIRECTION THIS IS TUNED TO. `/direct`, THRILL-BIBLE §12, "Bubble-test lake", 2026-08-27.
// Talon: "murky depths, a bit ominous." The gate result there is explicit and worth repeating
// where the builder will see it: **this is ATMOSPHERE, NOT DREAD** (§2.2 fails -- nothing in this
// level approaches), and it carries exactly two constitutive constraints:
//
//   (a) §6.0 -- the murk must read as a DENIAL OF DEPTH, not as a dark texture. A player on a
//       stepping stone must be unable to tell HOW FAR the bottom is while staying certain there
//       IS one. A flat black surface FAILS this: it reads as painted floor. This is why
//       `silt_night_scale` is raised in the material rather than left at the lab default -- the
//       scattering term is the only thing in this shader that ADDS light back, and at night it is
//       what keeps the water a value above black instead of a hole.
//   (b) §7.1 -- opacity may hide the bottom and may NEVER hide a traversal affordance. The
//       stepping stones stay legible from above at every phase of the cycle.
//
// And one prohibition that outlives this packet: **nothing is ever shown in or on this water** --
// no shape, no breach, no wake, no silhouette, no uncaused ripple. The register entry is a
// deposit, and one authored event in here spends it and the camp lake's with it.
//
// -------------------------------------------------------------------------------------------------
// The original lab/W3 header follows verbatim.
// -------------------------------------------------------------------------------------------------
//
// Authority: docs/superpowers/specs/2026-08-08-lake-water-contract-design.md §8 (what it must
// do and what it may cost) and docs/superpowers/direction/2026-08-08-lake-direction.md §9
// (what day and night must each feel like). Packet W3.
//
// THE BRIEF IN ONE LINE: the water has to look real for this game's style and react to the sun
// -- morning glare becoming a shimmer that swings across the lake as the day runs -- while
// costing almost nothing, because this is multiplayer and it gets streamed on low-end hardware.
//
// WHAT THIS SHADER DOES NOT DO, AND IT IS THE POINT. Spec §8.2 forbids SCREEN_TEXTURE (and so
// all screen-space refraction), planar reflections, realtime ReflectionProbes, SubViewports,
// fluid simulation, compute passes, player-deformed surfaces and ripple render targets. None of
// them appear below and none of them is a "later". The water LOOKS real; it does not simulate.
// The one permitted extra read is DEPTH_TEXTURE, used once (see `water_depth_m`), and it buys
// the depth tint AND the shoreline fade from a single fetch.
//
// The budget is pass/fail on a GTX 970 at 1080p: 1 draw call, <= 0.5 ms for the water pass, 0
// backbuffer copies. The plane is added to the map's WORST camera -- the trunk's own perf probe
// names "lake shore looking east, whole map in frustum" at 390 draw calls -- and the player
// stands there constantly.
//
// ---------------------------------------------------------------------------------------------
// WHY THIS FILE LIVES IN assets/materials/water/ AND NOT resources/shaders/
//
// House convention puts .gdshader files in resources/shaders/. This one is deliberately outside
// it, because W3's dispatch grants this packet assets/materials/water/ EXCLUSIVELY while four
// sibling water packets land on the same trunk. Keeping the shader, the material and the texture
// in one directory nobody else may write is worth more here than the convention is. It is not a
// precedent for anything: a shader with no such ownership fence belongs in resources/shaders/.
//
// ---------------------------------------------------------------------------------------------
// DAY AND NIGHT ARE ONE SHADER, DRIVEN BY ONE DIAL
//
// Spec §8.1 requires it and direction §9 is written as a parameter table precisely so it lands
// as a dial rather than a rebuild. Every day/night pair below is `mix(<night>, <day>, sky_light)`.
//
// `sky_light` is written once per frame by WaterSurface.cs from the shipped Sun's OWN LightEnergy
// -- not from the clock phase. Same argument GrassField.cs makes for the identical uniform:
// energy is what OutdoorAtmosphere actually decided, so this cannot drift out of step with a
// re-tune of the day curve, and it authors no second clock. Dusk therefore sweeps the whole
// table smoothly rather than switching it.
//
// ---------------------------------------------------------------------------------------------
// THERE IS NO AMBIENT OR EMISSIVE FLOOR HERE, AND THAT IS STRUCTURAL, NOT A SETTING
//
// Direction §9's last row forbids one: a private floor keeping the water visible would resolve
// Talon's open THRILL-BIBLE §6.2 night-ambient-floor fork by side effect, inside a shader, where
// nobody would find it.
//
// This shader makes that PROVABLE rather than merely intended. It writes no EMISSION at all, and
// every lit term it produces is computed inside light() multiplied by LIGHT_COLOR. The only
// other light the surface receives is the engine's own AMBIENT_LIGHT, which is exactly the dial
// OutdoorAtmosphere drives from the darkness level. Set every light to zero energy and set
// ambient to zero and this material renders black. Nothing in it can hold the water above the
// scene's floor, because it has no term that survives the lights going out.
//
// The night dials below only ever DARKEN (albedo value drops, alpha rises, the specular gets
// smaller). None of them can make the night water brighter than the day water, so none of them
// can pre-empt §6.2 in the other direction either.
//
// ---------------------------------------------------------------------------------------------
// THE NIGHT SPECULAR IS SMALLER, SHARPER AND SPARSER -- NOT DIMMER AND BROADER
//
// Direction §9 calls this the most important row in its table and §13/§8.5 explains the failure:
// a broad dim sheen reads as fog on glass, the eye resolves it instantly and gives up; sparse
// hard glints keep the eye catching something and never getting a shape. Dense fine sparkle on a
// dark field is the OTHER failure -- that is noise, and the eye gives up on that too.
//
// W3 was handed the open question "how to get sparse hard glints out of a scrolling-normal
// specular without a second sample" (direction §16). The answer used here is two multiplies:
// raise the lobe exponent hard and then THRESHOLD the result (`glint_floor`). On a real 2D
// normal field, mirroring the sun requires both normal components to line up at once, so the
// surviving set has codimension 2 -- it is isolated points, not a line and not a wash. Raising
// the exponent shrinks each point; raising the floor deletes the dim majority of them. Day runs
// a low exponent and a zero floor, which is the same code path producing a broad hot glare.
//
// No extra texture sample was needed for it, and no second normal field: all three scrolling
// octaves already feed the one combined normal, and the fine octave is what supplies grain small
// enough to break into beads.
//   *** SUPERSEDED 2026-08-21 (WATER-1). There are two octaves now and the fine one is gone, so
//   *** the grain that broke the lobe into beads is gone with it. The facet lattice does that job
//   *** instead and the result is patches rather than beads -- deliberately, because §7B wants
//   *** chunky. The threshold and the exponent below both survive; see the WATER-1 section.
//
// The SAME principle is now applied by day, and it is a second requirement rather than a
// symmetry: glare is glints, not a gradient. The day lobe was originally a wide exponent with no
// floor at all, which produces a smooth falloff that averages to a flat wash -- the surface read
// as a coloured plane with a bright smear on it. Day now runs a tight exponent and a real floor
// too, just brighter and denser than night, so the morning glare is a field of hard points that
// blow out individually.
//
// =================================================================================================
// 2026-08-21 -- WATER-1, THE STYLE PASS. WHAT CHANGED AND WHAT DID NOT.
// =================================================================================================
//
// Everything above this line was written for the 2026-08-08 lake, whose brief was "the water has
// to look REAL for this game's style". That brief is superseded. Talon, 2026-08-21, looking at the
// shipped surface:
//
//   "I like the shader with the shine and the way the light catches the water but this would also
//    need to be adjusted because the water overall feels too 'real' looks too good and doesn't
//    quite match the game look and feel overall."
//
// So this is not a rewrite and it is not a deletion. The technique above was correct for its
// brief and is *converted* here, not thrown away: pipeline brief §7B ("Procedural Noise Capping")
// says cap the octave count, quantise the output into a small number of discrete bands, and keep
// water reflections while dropping their fidelity to blocky. Four changes implement that, and each
// one is annotated at its own uniform block below:
//
//   1. THREE OCTAVES BECAME TWO (§7B "cap resolution/octave count"). The FINE octave -- 2.37 m
//      tiles scrolling at 0.16 m/s -- was the fine grain §7B names, and it is gone outright:
//      uniforms, fetch and distance fade. The broad octave STAYS, because the header above is
//      right about why it exists (it is the only thing keeping far water off mirror), and the
//      coarse octave was pulled from 8.6 m to 6.9 m tiles and given the amplitude the fine one
//      was carrying, so the near field is chunky rather than empty. Cost went DOWN by one
//      texture fetch.
//   2. THE SURFACE NORMAL IS FACETED (§7B "a stepped/flattened normal map, so reflections read
//      blocky"). See `group_uniforms facets`. This is what makes the reflection blocky, and it
//      is also what turns the glint from beads into chunky patches.
//   3. THE DEPTH RAMP AND THE LIT TERMS ARE BANDED through `toon_band()` -- the SAME function
//      resources/shaders/toon_world.gdshader quantises the whole world with, included rather
//      than re-derived. See `group_uniforms bands`.
//   4. THE SURFACE CAN BE ADVECTED DOWNSTREAM by a constant world-space current, so a river reach
//      reads as moving rather than rippling in place. See `group_uniforms current`. It defaults
//      to ZERO, which is bit-identical to the shipped lake.
//
// WHAT WAS DELIBERATELY NOT CHANGED, AND MUST NOT BE:
//
//   * `shallow_col` / `deep_col` still ship the LAKE's tannic green-black. A river is silty
//     brown-grey and wants a different pair, but scenes/game/world/camp/Camp.tscn loads
//     water_surface.tres BY PATH, so retuning the defaults would repaint the lake by side effect.
//     The river's pair lives in a second MATERIAL (river_surface.tres) over this one shader.
//     Whether the lake should follow the river is Talon's call, not this shader's.
//   * ONE DEPTH_TEXTURE FETCH. `water_depth_m()` is still the only `texture(depth_tex, ...)` in
//     the file, and the banding in §3 above quantises the value it returns rather than sampling
//     it again. Nothing added here reads the screen.
//   * NO EMISSION, EVER, AND EVERY LIT TERM STILL LIVES INSIDE light() TIMES LIGHT_COLOR. The
//     band function returns a 0..1 SHAPE which is multiplied by LIGHT_COLOR exactly as the raw
//     term was; a quantised zero is still zero. `toon_band` cannot introduce a floor because it
//     never adds a constant -- at levels = 3 its outputs are {0, 0.5, 1} and the bottom band is
//     literal zero. The no-floor proof in the section above is therefore untouched, and the
//     lights-out capture in this packet's report is the evidence.
//   * The `sky_light` dial. Day and night are still ONE shader on ONE uniform written by
//     WaterSurface.cs from the Sun's own LightEnergy. Nothing below branches on time of day.
shader_type spatial;

// blend_mix + depth_draw_opaque: the surface is transparent, and on a transparent material
// `depth_draw_opaque` means it does not write depth. That is deliberate. `depth_draw_always`
// would have the water write depth even where the shoreline fade has taken ALPHA to zero,
// planting an invisible occluder along the whole beach.
//
// cull_disabled is the answer to "a PlaneMesh is invisible from below", which W2 makes reachable
// the moment a swimmer's camera drops (SwimEyeAboveWaterM is 0.15 m, and the go-under puts the
// lens under the surface outright). The alternative -- a second inward-facing mesh -- is banned
// by spec §8.2 and would cost a second draw call. A plane never shows both faces at once, so
// cull_disabled costs raster setup and nothing else; see the !FRONT_FACING branch in fragment().
//
// world_vertex_coords: the swell has to have a wavelength in METRES that is the same everywhere,
// and the plane is 108 x 300 m with UVs that stretch differently on each axis. Same reason
// grass.gdshader and DecorativeWater.gdshader both work in world XZ.
//
// unshaded is NOT set: this surface is lit by the real Sun and Moon, which is the entire feature.
render_mode blend_mix, depth_draw_opaque, cull_disabled, world_vertex_coords;

// toon_band() and nothing else. The band function is INCLUDED rather than re-derived here for
// exactly the reason toon_lib.gdshaderinc's own header gives for sharing it between the world
// shader and the decal shader: two quantisers that agree today will drift, and the moment the
// water's band edges sit a rounding difference away from the bank's, the water stops reading as
// part of the same painting and starts reading as a surface laid over it. That is the specific
// failure Talon named -- "doesn't quite match the game look and feel overall" -- so agreeing by
// construction is the point, not a convenience.
//
// This is a READ of a file this packet does not own (resources/shaders/toon_* is STYLE's). Nothing
// here writes it. The unused primitives in that file (the hashes, the noise, toon_break_up) are
// stripped by the compiler; the include costs nothing that is not called.
// SHADER LAB, 2026-08-25: this include's PATH is the only line of the shipped file that was
// changed when it was landed here. In Sail the library lives at res://resources/shaders/; this
// project has no such directory and copies it next to the shader instead. Nothing in toon_lib
// itself was touched, and it has no further includes -- the chain ends there.
#include "res://resources/shaders/toon_lib.gdshaderinc"


// --- the one dial ------------------------------------------------------------------------------
group_uniforms cycle;
// 0 = night (sun below the horizon, moon carrying the scene), 1 = full daylight.
// Written by WaterSurface.cs from Sun.LightEnergy, clamped to [0,1] exactly as GrassField does.
uniform float sky_light : hint_range(0.0, 1.0) = 1.0;


// --- surface break-up: TWO scrolling octaves of the one normal map --------------------------------
// Spec §8.1 asks for two scrolling normal maps at different scales and rates. This is one TILEABLE
// texture sampled twice, which is the standard form of that: separate textures would cost the
// same fetches with worse cache locality and more assets to keep in step.
//
// water_normal.png is baked by gen_water_normal.py in this directory (committed, so the ripple
// shape stays a set of numbers somebody can retune rather than an opaque binary).
//
// KEEP THE SCALES AND THE RATES OFF SIMPLE INTEGER RATIOS. Direction §9 is explicit: a simple ratio
// makes the surface read as a repeating pattern, and a pattern RESOLVES -- which is the one thing
// the night water must never do. 29.7 / 6.9 = 4.304 and the rates go 0.045 / 0.021 = 2.14. Neither
// is an integer and they do not agree with each other, so the combined field's period is far longer
// than anyone will stand and watch and the two tile seams never coincide.
//
// THERE USED TO BE A THIRD, FINE OCTAVE. WATER-1 REMOVED IT, AND HERE IS BOTH SIDES OF THAT.
//
// The fine octave was 2.37 m tiles scrolling at 0.16 m/s. It did two real jobs: it kept the near
// water alive at arm's length, and its grain was small enough to break the thresholded specular
// into isolated beads (see the glint section of the header). It is also, exactly and by
// definition, the "fine, realistic-looking detail" that pipeline brief §7B orders capped:
// "cap resolution/octave count so noise reads as broad, chunky variation rather than fine grain."
// Two of the four §7B moves in this file are its replacement -- the facet lattice below breaks the
// specular into patches without needing sub-metre grain, and the band function steps them -- so
// the octave's job is done by technique that reads chunky instead of by detail that reads real.
//
// THE BROAD OCTAVE STAYS, and the header's defence of it is still correct: the lake is 108 x 300 m,
// a normal map mips toward flat as it recedes, and with a single octave everything past roughly
// 30 m turns into a mirror -- a coloured plane, which is the "extremely plain and flat" read the
// original pass was told to fix. No amplitude value fixes that, because the detail is averaged away
// before the amplitude is applied. So the two remaining octaves are still DISTANCE-WEIGHTED and
// still hand off: as the coarse octave mips out, the broad one -- whose texels are 29.7 m across
// and so never mip out at any distance a player can see -- keeps structure to the far edge, and its
// weight RISES with distance to take over rather than merely surviving.
//
// The coarse octave inherits the near field: its amplitude went UP from 0.42 to 0.55, because it is
// now the only thing between the camera and the broad layer.
//
// BOTH TILES GREW RATHER THAN SHRANK, AND THE FIRST HEADED PASS IS WHY. The obvious move when the
// fine octave goes is to shrink the coarse one to cover for it, and WATER-1 did exactly that first
// (8.6 -> 6.9 m). The capture came back reading as fine realistic chop anyway, because the tile
// size is not what sets the smallest feature -- the TEXTURE's own octave count is, and
// gen_water_normal.py was baking five of them, putting detail at about 5 cm of world. That is fixed
// at the source (the script now bakes two), and with the source chunky the tiles could go the other
// way: 8.6 -> 9.4 m and 29.7 -> 33.1 m, so the surviving features are large in world terms as well
// as few. 33.1 / 9.4 = 3.52, still not an integer and still not equal to the rates' 2.14.
//
// The coarse fade bracket moved with its texel size (9.4 / 8.6 = 1.09, so 70 -> 76 and 200 -> 218):
// the bracket has always been "where this octave's texels fall under a screen pixel", and a larger
// tile pushes that further away.
//
// Cost: ONE FEWER texture fetch than the shipped surface. §7B's cap is not paid for here; it pays.
group_uniforms surface;
// hint_normal, not hint_default_black: if the texture ever fails to resolve, the fallback must be a
// FLAT normal. A black fallback decodes to (-1,-1) tangential and would tilt the whole lake over.
uniform sampler2D normal_map : filter_linear_mipmap, repeat_enable, hint_normal;
uniform float tile_broad_m : hint_range(8.0, 120.0) = 33.1;  // metres per texture repeat
uniform float tile_coarse_m : hint_range(1.0, 40.0) = 9.4;
uniform vec2 flow_broad = vec2(0.63, -1.0);                  // scroll heading, normalised below
uniform vec2 flow_coarse = vec2(1.0, 0.31);
uniform float rate_broad_mps : hint_range(0.0, 1.0) = 0.021;
uniform float rate_coarse_mps : hint_range(0.0, 1.0) = 0.045;
uniform float broad_amp : hint_range(0.0, 2.0) = 0.34;
uniform float coarse_amp : hint_range(0.0, 2.0) = 0.55;

// Where the coarse octave hands over to the broad one, in metres of camera distance. Not arbitrary:
// the pair brackets the range over which its texels fall below about one screen pixel and the mip
// chain starts averaging its structure away.
uniform float coarse_fade_start : hint_range(10.0, 300.0) = 76.0;
uniform float coarse_fade_end : hint_range(20.0, 400.0) = 218.0;
// How much of the broad octave is present up close, where the coarse one is doing the work. It
// rises to full as the coarse fades -- that ramp IS the handover.
uniform float broad_near : hint_range(0.0, 1.0) = 0.45;
// Direction §9: night slows the surface down. Fast detail reads as busy -> legible -> safe; slow
// coarse motion is what reads as depth. With the fine octave gone this is the whole of that row.
uniform float night_flow_scale : hint_range(0.05, 1.0) = 0.58;
// Night also cuts the coarse octave's amplitude, which is the other half of the same direction §9
// row: less break-up at night, so the surface stops offering the eye something to resolve.
uniform float coarse_amp_night_scale : hint_range(0.0, 1.0) = 0.62;


// --- §7B, part 1: the facet lattice ---------------------------------------------------------------
// Pipeline brief §7B, water clause, verbatim: "keep the reflections themselves (they're wanted),
// but reduce their fidelity to match -- a lower-resolution reflection buffer or a stepped/flattened
// normal map, so reflections read blocky and low-poly rather than crisp and mirror-sharp."
//
// A lower-resolution reflection BUFFER is not available and never will be here: spec §8.2 bans
// planar reflections, realtime probes and SubViewports outright, and WATER-1 restates the ban as
// "not a later". So this file takes the other route §7B names -- the stepped normal -- and it is
// the cheaper one anyway.
//
// WHAT IT DOES. The combined ripple field is a continuously-varying tangential slope. Snapping that
// slope onto a coarse lattice collapses the surface into a small set of DISCRETE facet directions,
// which is a flat-shaded low-poly water surface produced without any extra geometry. Everything
// downstream of the normal inherits the blockiness for free: the fresnel sheen steps, and the
// specular -- which is a function of the half-vector against this normal -- stops being a smooth
// lobe crawling over a smooth film and becomes hard-edged patches that switch on and off as the
// facets tilt through the mirror configuration. That switching IS the shine Talon said he likes,
// and it now swings with the sun in chunks instead of in a gradient.
//
// WHY IT DOES NOT ALIAS INTO THE DISTANCE, which is the obvious objection. The lattice is spaced in
// SLOPE, not in screen space or in world space, and the octave amplitudes already fall with camera
// distance. Far water therefore has a small slope magnitude, lands inside the zeroth lattice cell,
// and comes out flat -- the faceting dissolves by construction at exactly the range where its edges
// would go sub-pixel. No distance term is needed and none is present.
//
// LARGER `facet_steps` = MORE, SMALLER facets and a finer read; smaller = fewer, bigger, blockier
// plates. Below about 6 the surface reads as three or four enormous panes and stops being water.
group_uniforms facets;
uniform float facet_steps : hint_range(2.0, 64.0) = 9.0;
// Width of the blend at each lattice edge, in fractions of a cell. 0 is a knife edge, which crawls
// visibly on a moving surface; 0.5 dissolves the lattice back into the smooth field it came from.
uniform float facet_softness : hint_range(0.0, 0.5) = 0.085;


// --- §7B, part 2: the bands ------------------------------------------------------------------------
// Pipeline brief §7B: "Quantize/step the output into a small number of discrete bands rather than a
// smooth continuous gradient, so results feel intentional and low-poly rather than naturalistic.
// Applies to: ground surface detail noise and water surface noise."
//
// Both counts go through toon_band() -- the same function, with the same terminator placement and
// the same softness units, that resources/shaders/toon_world.gdshader quantises every static
// surface in the camp with. See the #include note above for why that matters more than it looks.
//
// THE DEPTH RAMP IS THE MORE IMPORTANT OF THE TWO, and it is the one that does the design work.
// A smooth Beer-Lambert ramp tells the player exactly how deep the water is at every pixel -- it is
// a continuous readout of the bed. Quantised into four steps it tells them four things instead:
// shelf, drop, channel, and nothing. WATER-1's brief is that "the player cannot confirm what is a
// few feet down, and that uncertainty is the dread, not the gradient" -- a step function is
// literally that sentence as arithmetic. Four is the ceiling §7B's "small number" was read as, and
// four is what makes a shelf-into-channel legible as terraces rather than as a smear.
//
// THE LIGHT BANDS are three, which is toon_world.gdshader's shipped `toon_bands` and the three
// tones a cel painting is built from. They quantise the diffuse and the glint inside light(),
// so the water's tonal response to the sun steps in the same places the bank's does.
//
// NEITHER COUNT CAN INTRODUCE A NIGHT FLOOR. toon_band returns a 0..1 shape whose bottom band is
// literal zero and which is multiplied by LIGHT_COLOR at every use site; it adds no constant
// anywhere. See the header's no-floor section, which this preserves rather than qualifies.
group_uniforms bands;
uniform float depth_bands : hint_range(2.0, 6.0, 1.0) = 4.0;
uniform float light_bands : hint_range(2.0, 6.0, 1.0) = 3.0;
// Shared terminator width for both, in fractions of a band, matching ToonWorld.tres's 0.055.
uniform float band_softness : hint_range(0.0, 0.5) = 0.055;


// --- the current: a river moves downstream, a lake does not -----------------------------------------
// WATER-1 scope item 4: "the reach must read as moving downstream, not rippling in place." The
// whole sampling frame is advected by a constant world-space velocity, which carries the swell AND
// both normal octaves together as one body of water travelling, with each octave's own scroll
// riding on top as internal churn. Advecting the frame rather than adding a third scroll rate is
// what makes it read as the water moving rather than as a pattern sliding over a stationary sheet.
//
// A CONSTANT DIRECTION IS CORRECT FOR THIS PACKET AND IS NOT A PLACEHOLDER FOR A FLOW MAP. WATER-2
// owns the baked flow map for a curved river; nothing here pre-builds for it, and a flow map when
// it arrives replaces this uniform with a texture read at the same site.
//
// DEFAULT ZERO IS LOAD-BEARING. At current_speed_mps = 0 the advection term is identically zero and
// the shipped lake is bit-identical to what it was before this pass. The river's value lives in
// river_surface.tres, not here.
group_uniforms current;
uniform vec2 current_dir = vec2(0.0, 1.0);   // world XZ heading, normalised in-shader
uniform float current_speed_mps : hint_range(0.0, 4.0) = 0.0;


// --- vertex swell --------------------------------------------------------------------------------
// Two summed sines, vertex shader only (spec §8.1). Not a wave simulation and not player-deformed.
//
// WAVELENGTHS ARE NYQUIST-BOUND BY W1'S MESH. The plane is subdivided at WATER_CELL_M = 1.875 m
// square cells (camp_gen.gd sized it that way for exactly this reason). 14.5 m and 9.3 m give 7.7
// and 5.0 samples per wavelength; anything under ~4 samples starts aliasing into a crawling moire
// instead of reading as water. If a future pass wants a shorter wave it needs a finer mesh first,
// and that trades against §8.3's 20k triangle ceiling (the plane is already 18,676).
//
// AMPLITUDE IS BOUND BY THE ROPE BUOYS. camp_gen.gd:431 places the 22-per-line swim-line buoys
// with their centres exactly at WATER_Y, and the dock deck sits 0.33 m above it. A swell taller
// than ~0.15 m visibly detaches the buoys from the surface. The day value below is 0.055 m, which
// leaves most of that margin unspent; do not raise it past ~0.12 without either sinking the buoys
// or accepting the mismatch. (Displacing the buoys is not this packet's to do -- they are built
// by camp_gen.gd, which W1 owns.)
//
// THE SWELL IS VISUAL ONLY. camp_height.gd's own note is explicit that the wade/swim/go-under
// decision reads the FLAT plane at WATER_Y, not the displaced surface. Nothing may read this back.
//
// Direction §9: night lowers the amplitude at the SAME period, and NEVER to zero -- a dead-flat
// night lake reads as a solid floor and players try to walk on it, which is a fairness break and
// a comedy failure at once.
group_uniforms swell;
uniform float swell_amp_day : hint_range(0.0, 0.15) = 0.055;
uniform float swell_amp_night : hint_range(0.0, 0.15) = 0.034;
uniform float swell_len_a_m : hint_range(4.0, 40.0) = 14.5;
uniform float swell_len_b_m : hint_range(4.0, 40.0) = 9.3;
uniform float swell_speed_a : hint_range(0.0, 3.0) = 0.42;
uniform float swell_speed_b : hint_range(0.0, 3.0) = 0.61;
uniform vec2 swell_dir_a = vec2(1.0, 0.22);
uniform vec2 swell_dir_b = vec2(-0.44, 1.0);


// --- depth tint and the shoreline ----------------------------------------------------------------
// One DEPTH_TEXTURE read serves both, and it is the only extra read in the shader (spec §8.3
// permits exactly this one). It is NOT a backbuffer copy: SCREEN_TEXTURE forces Godot to resolve
// the colour buffer, DEPTH_TEXTURE reads the depth prepass Forward+ has already produced.
//
// WHY NOT MIRROR camp_height.gd'S LAKEBED MATHS IN GLSL INSTEAD. Because grass.gdshader used to
// carry exactly such a hand-mirror of the terrain, its header calls it "the single most likely way
// for your work to break something invisible", and it was deleted rather than corrected. Reading
// the depth the renderer actually drew cannot drift from the bed, includes the bed's micro-noise
// for free, and needs nothing from a file this packet does not own.
//
// Colours: direction §14.2 is a hard constraint -- the night water goes darker and lower-contrast,
// NEVER bluer. Cool is already a threat direction (§6.6) and the dusk sweep's orange->plum->indigo
// grade is the sole warning lane; a second warning colour puts two channels in one lane and
// neither survives. So the deep colour below is a tannic green-black, which is both what a wooded
// lake actually is and the only hue that reads as physics rather than warning.
group_uniforms depth;
// Godot 4.7 removed the bare DEPTH_TEXTURE built-in; it has to be declared. `hint_depth_texture`
// binds the depth prepass Forward+ has already produced -- it does NOT request a backbuffer copy,
// which is what `hint_screen_texture` would do and what spec §8.2 bans outright.
//
// filter_nearest, not linear: SCREEN_UV maps 1:1 onto this texture, so interpolation buys nothing
// and averaging depth across a silhouette edge invents a surface that is not there.
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform vec3 shallow_col : source_color = vec3(0.150, 0.205, 0.163);
uniform vec3 deep_col : source_color = vec3(0.0115, 0.0225, 0.0195);
// Beer-Lambert extinction, per metre. Day is a wide ramp so the shelf is visibly a different
// colour from the deep; direction §9 COMPRESSES it at night so shallow and deep converge and the
// shelf stops advertising where it is safe to stand.
uniform float extinction_day : hint_range(0.05, 6.0) = 0.62;
uniform float extinction_night : hint_range(0.05, 6.0) = 2.10;
// How far the shallow colour is dragged toward the deep colour at night (the other half of the
// same "they converge" row), and how far the whole ramp drops in value and saturation.
uniform float night_converge : hint_range(0.0, 1.0) = 0.70;
uniform float night_value : hint_range(0.0, 1.0) = 0.52;
uniform float night_desaturate : hint_range(0.0, 1.0) = 0.35;
// Transparency over the shelf. Day is see-through, and that is the comedy -- everyone on the dock
// can watch you flailing. Night closes it.
uniform float alpha_shallow_day : hint_range(0.0, 1.0) = 0.18;
uniform float alpha_shallow_night : hint_range(0.0, 1.0) = 0.80;


// --- WATER-1b: THE SEE-INTO ZONE --------------------------------------------------------------
// Talon, 2026-08-21, having approved WATER-1's look ("this water is beautiful"), asked for exactly
// one thing:
//
//   "I would like to see a slight bit more 'depth' is this possible? It's gray, dark, scary water.
//    But I would like to see partway into the water until it gets too deep. This will be creepy
//    like it's something in the water."
//
// A readable near-zone, then loss. A thing you half-see is worse than a thing you cannot see, and
// the near-black at real depth is NOT being lifted -- he did not ask for that and §6.2 is not
// this shader's to resolve.
//
// THE MECHANISM WAS ALREADY BUILT AND COSTS NOTHING EXTRA. `water_depth_m()` returns the depth of
// whatever opaque geometry is behind this fragment, in world Y -- so a rock suspended a metre under
// a five-metre channel already reports one metre, not five. Nothing here adds a fetch; the whole
// feature is one more exp() on a value already in hand.
//
// WHY ALPHA WAS TAKEN OFF THE BANDED RAMP, WHICH IS THE ACTUAL CHANGE.
//
// Before this pass, ALPHA read the same quantised `t` the colour reads. At depth_bands = 4 that put
// the water's opacity on a four-step stair, and by the third terrace it was already ~77% opaque --
// so a submerged object did not fade out of view, it CROSSED A LINE. One step it was there, the
// next it was gone, at a fixed depth, with a visible edge running through it. That is the exact
// opposite of "see partway in until it gets too deep": the whole feeling depends on losing
// confidence gradually rather than on a threshold being crossed.
//
// The precedent for splitting them is in this file already: the shoreline fade was deliberately
// excluded from the banding on the same grounds one pass earlier. §7B's quantisation clause is
// aimed at NOISE-DRIVEN detail ("any noise-driven generation ... so results feel intentional and
// low-poly rather than naturalistic"), and depth is not noise -- it is a measured physical
// quantity read out of the depth buffer. Banding the colour is what makes the terraces countable
// and it stays; banding the transparency only put a hard edge on the one thing that had to be
// soft. `alpha_bands` below keeps the alternative one uniform away so nobody has to take that on
// faith.
//
// KA IS A SEPARATE COEFFICIENT FROM `extinction_*` AND THAT IS THE POINT. The colour ramp wants to
// be spent FAST -- it is what makes the water read as murk and it is what Talon approved. The
// see-into curve wants to be spent SLOWLY over a longer window. One coefficient cannot do both,
// and trying to serve both with `extinction_*` is what kept the window shut.
//
// The three distances these produce are computed and PRINTED by RiverLab from the material's own
// loaded values, so the numbers in the report cannot drift from the numbers that shipped. They are
// VERTICAL depths, not path lengths: water_depth_m works in world Y precisely so the answer is the
// same from every camera angle.
group_uniforms see_into;
// Per metre of water. LARGER = the window shuts sooner and less is visible.
uniform float alpha_extinction_day : hint_range(0.05, 6.0) = 0.55;
// Night closes hard, and must: the day window is the thing being opened here, not the night one.
uniform float alpha_extinction_night : hint_range(0.05, 6.0) = 2.40;
// 0 or 1 = SMOOTH (what ships). >= 2 quantises the see-into curve into that many steps, which is
// the A/B this packet was asked to show rather than argue.
uniform float alpha_bands : hint_range(0.0, 8.0, 1.0) = 0.0;

// THE SEE-INTO TERM USES PATH LENGTH, NOT VERTICAL DEPTH, AND THE FIRST WATER-1b CAPTURE IS WHY.
//
// `water_depth_m()` returns a VERTICAL depth in world Y -- deliberately, so the colour terraces
// grade identically from every camera angle and a capture from the bank and one from the dock can
// be compared. That is right for the colour and it is wrong for transparency, and the difference
// is not academic: the first pass of this feature made a half-metre shelf 65% see-through no
// matter how the player was looking at it, so a grazing shot along the near margin came back
// reading as wet sand rather than as water. The dark scary river had a beach in front of it.
//
// The physics is simply that light leaving a submerged surface travels along the VIEW ray, not
// straight up. Looking down, the path IS the depth. At a graze, the same half-metre of water is
// several metres of water. Dividing by |cos| between the view and vertical gives that for free --
// v_world and CAMERA_POSITION_WORLD are both already in hand for the octave handover, so this
// costs one normalize and one divide, no fetch and no second sample.
//
// The consequence is the one that makes the feature read: you have to LOOK DOWN INTO the water to
// see anything in it, and glancing across it tells you nothing. Every distance this packet quotes
// is therefore a straight-down distance, and RiverLab says so when it prints them.
//
// The clamp bounds a horizon-grazing view: without it cos goes to zero at the vanishing point and
// the path length goes to infinity. LARGER = grazing views close down less hard.
uniform float see_path_min_cos : hint_range(0.02, 1.0) = 0.18;

// HOW MUCH OF THAT PHYSICS TO ACTUALLY SPEND. 1.0 is the real thing; 0.0 ignores the view angle
// entirely and is the behaviour this feature shipped with for one capture.
//
// It is not 1.0, and the reason is a measurement rather than a preference. At full strength a
// player standing on the bank looking across at a figure 1.2 m down meets it at about 18 degrees
// above the water, which is a cosine of 0.31, which is a path of 3.8 m, which leaves 6% of the
// figure on screen -- i.e. the feature Talon asked for does not exist from the one stance a player
// actually spends the game in. Real water does behave that way and that is precisely why real
// water hides things; this game is stylised and the beat has to land from a normal stance.
//
// At 0.75 the same look-across leaves about 12% (a faint suggestion), leaning over the edge at
// 39 degrees leaves about 26% (a clear one), and looking straight down is unchanged -- the
// exponent is 1 at cos = 1, so every distance this packet quotes still holds exactly.
// LARGER = closer to real physics and harsher at a graze.
uniform float see_path_obliquity : hint_range(0.0, 1.0) = 0.75;
// Metres of water depth over which the surface fades in at the bank, so the plane never shows a
// hard intersection line with the shore. Direction §9 WIDENS it at night: a soft boundary makes
// ground / not-ground ambiguous, which is the feeling, while the land itself stays fully readable.
uniform float shore_fade_day_m : hint_range(0.02, 4.0) = 0.35;
uniform float shore_fade_night_m : hint_range(0.02, 4.0) = 1.05;


// =================================================================================================
// SHADER LAB, 2026-08-25 -- MURK. NEW UNIFORMS. ONE OF THEM IS LIVE AT ITS DEFAULT: silt_scatter.
// =================================================================================================
//
// Talon, looking at the shipped surface: "It definitely needs some work to look better. I would
// like to see more murky depths more."
//
// NOTHING ABOVE THIS LINE WAS RETUNED. Not one shipped default was moved, because Camp.tscn loads
// water_surface.tres BY PATH and river_surface.tres sits over the same shader -- so a default
// changed here repaints the camp lake and the river with no diff that says so. That trap is named
// in river_surface.tres's own header and it is the reason every dial below was ADDED at a value at
// which its whole term folds to zero.
//
// THAT LAW HAS SINCE BEEN SPENT ONCE, ON PURPOSE, BY TALON. `silt_scatter` was ruled to 1.0 later
// the same day -- see its own header for the ruling and the measurement behind it. So the sentence
// this block used to carry, "the lab's copy at defaults is the game's water", IS NO LONGER TRUE
// and is corrected here rather than left to be read: at defaults this file now renders the lake
// and the river MURKY, deliberately, and that is what ships.
//
// The law still governs every OTHER dial below, and it still governs this one in the only sense
// that mattered: the lake was repainted by a decision with a diff and a ruling attached, not by a
// default drifting while nobody was looking. Reverting is one number -- `silt_scatter` back to
// 0.0 -- because nothing else was tuned to match it.
//
// WHY THE SHADER NEEDED ANYTHING AT ALL, WHICH IS A PHYSICS ARGUMENT AND NOT A TASTE ONE.
//
// The depth ramp above is pure Beer-Lambert extinction: `1 - exp(-depth * k)` between two colours,
// with the deep end a near-black tannic green. Extinction alone can only ever take water TO BLACK,
// and black water is CLEAR water -- a swimming pool at night, an ink well, a hole. It is not murk.
// Murk is suspended matter IN the water catching the light on its way past, which is why a silty
// river photographs LIGHTER and FLATTER with depth while a clear lake photographs darker. Turning
// `extinction_*` up gets to black sooner; it never gets to murk, and no amount of it will, because
// the missing term is additive and every term in this shader was subtractive.
//
// So the addition is one scattering term and one veil, and they are the only two things here that
// can make the water lighter than the ramp made it.
//
// THE NEVER-BLUER LAW IS ENFORCED IN ARITHMETIC, NOT IN A COMMENT.
//
// Direction §14.2, quoted in the depth block above: the night water goes darker and lower-contrast,
// NEVER bluer, because the dusk sweep's orange->plum->indigo grade is the sole warning lane and a
// second cool channel in that lane means neither survives. A murk dial is exactly the shape of
// mistake that breaks it -- "hazier" is one slider away from "bluer" in every reference photograph
// of dirty water, and the reference photographs are of oceans.
//
// `silt_col` is therefore CLAMPED IN THE SHADER so its blue channel can never exceed its green:
// see the `vec3 silt = ...` line in fragment(). Pick a cool colour in the inspector and the shader
// takes the cool back out. That is deliberate rudeness. A comment saying "keep it green" is a
// comment somebody overrides at 2am; a min() is a constraint. The permitted family is the tannic
// green-black the lake ships and the brown-grey silt the river ships (river_surface.tres: "silt
// suspended in water is greyer than the dirt it came from") -- both have B as the smallest channel,
// which is what the clamp encodes.
//
// AND IT CANNOT CREATE A NIGHT FLOOR. Everything below writes ALBEDO and ALPHA. ALBEDO is not
// EMISSION: it is multiplied by DIFFUSE_LIGHT and by the engine's AMBIENT_LIGHT, both of which
// OutdoorAtmosphere drives to zero when the lights go out. Set every light and the ambient to zero
// and this surface still renders black with every dial below at maximum. The header's no-floor
// proof survives this section unqualified, and the lights-out capture still tests it.
//
// The night side is additionally scaled by `silt_night_scale`, because the header is explicit that
// every night dial in this file only ever DARKENS. A murk term that lifted the night water would
// be the first one that did not.
//
// WHAT THIS DELIBERATELY DOES NOT DUPLICATE:
//   * `alpha_bands` already A/Bs the see-into curve's quantisation. No second banding dial.
//   * `sky_reflect` (0.12, physically wrong on purpose) already owns the murk-vs-MIRROR axis --
//     how much sky comes back off the surface. Murk below is an ALBEDO/ALPHA axis: what the water
//     COLUMN does to what is under it. They are different halves of the same picture and they
//     interact one way worth knowing: raising the silt raises the water's own albedo, so the same
//     sky reflection is a smaller fraction of the frame's brightness and reads weaker. If the
//     surface stops reading as a surface after a big murk push, `sky_reflect` is the dial to
//     answer it with -- not more murk.
//   * `extinction_*` still owns the COLOUR ramp. `sightline_scale` below scales the SEE-INTO
//     coefficient, which is the separate one, and it scales rather than replaces it precisely so
//     the day/night pair keeps its authored ratio.
group_uniforms murk;

// The A/B flag, matching the convention the lab's other twenty debug_* bools use: it disables
// ONLY the effect it is named for and nothing else, so a side-by-side is proof rather than
// argument. With this true the surface is the shipped shader exactly, whatever the dials say.
uniform bool debug_no_murk = false;

// PART 1 -- SUSPENDED SEDIMENT. The main dial, and the one to start on.
//
// How much of the scattering column is spent. 0 = OFF, and off is bit-identical to the shipped
// surface: the whole term multiplies out. 1 = the deep water reads as a flat lit plate of
// `silt_col` rather than as a hole.
//
// It is scaled by PATH LENGTH, not by vertical depth, and the choice matches the see-into curve
// rather than the colour ramp -- for the same reason that block gives. Scattered light travels
// along the view ray; a graze crosses more water than a look-down does, so the same half metre
// hazes over harder from the bank than from directly above. The consequence is the one the
// see-into feature already relies on: you have to LOOK DOWN INTO the water to see anything in it.
// Two dials that answer to the same geometry are one behaviour; two that disagree about it are a
// bug nobody can name.
//
// ==========================================================================================
// TALON'S RULING, 2026-08-25: THIS DEFAULT IS 1.0 AND IT REPAINTS BOTH BODIES OF WATER.
// ==========================================================================================
// Every murk dial in this file used to default to a NO-OP, deliberately, so the lab at defaults
// was still the game's water and every murk change was an A/B against it. That law existed to
// stop the camp lake being repainted BY ACCIDENT, with no diff saying so -- Camp.tscn loads
// water_surface.tres by path, and that .tres overrides nothing here.
//
// It was never a law against Talon deciding. It has now been spent, on purpose, by him. Shown
// the 0.00 / 0.50 / 1.00 matrix: "I think they could all work. They all look good. From up
// close it's probably ones with more murkiness. I would go with whatever one is cheapest to put
// into the game at runtime."
//
// THE PICK WAS ASKED FOR ON COST, AND COST TURNED OUT TO BE IDENTICAL. Measured headed on the
// RTX 4070, 1920x1080, 90 frames, vsync off: water_surface median 1.374 ms at silt_scatter 0.0
// and 1.377 ms at 1.0 -- 0.2%, against a run-to-run noise floor of 1.7% (p90 across 37 shaders,
// two identical runs). There is no cheapest, and the arithmetic says why: the murk block in
// fragment() is gated on `debug_no_murk` and NEVER on this uniform's value, so the exp(), both
// clamps and the mix() execute identically at 0.0 and at 1.0. The dial is free.
//
// Cost could not decide it, so his other sentence did: murkier reads better up close, and it
// costs nothing to have. 1.0.
//
// WHAT THIS ACTUALLY CHANGES, stated plainly because it is canon-level:
//   * the camp LAKE -- water_surface.tres overrides nothing, so it takes this default;
//   * the RIVER -- river_surface.tres overrides colour and extinction but NOT this, so it stays
//     the murkier of the two, its silt browner and its extinction tighter.
// The open "does the lake follow the river into murk" fork is answered YES by this one line.
//
// NIGHT WAS CHECKED BEFORE THIS LANDED and does not blow out: `silt_night_scale` (0.35) gates
// the night side, and a headed day/night A/B at 0.0 vs 1.0 leaves the night pair nearly
// unchanged while the day pair loses the dark half of the contrast ladder, as designed.
//
// HEADROOM ABOVE IT STILL EXISTS. Maxing this is not painting into a corner: `turbidity_veil`
// (0.0) and `silt_density` are both still available if he wants murkier again.
//
// TO REVERT: set this back to 0.0. That is the entire change -- nothing else was tuned to match
// it, deliberately, so reverting is one number rather than an archaeology exercise.
uniform float silt_scatter : hint_range(0.0, 1.0) = 1.0;

// How fast the scattering column saturates, per metre of path. Larger = the water goes flat
// sooner and the murk lives closer to the surface; smaller = a long, slow haze that only closes
// over the channel. Inert while `silt_scatter` is 0, which is why it may carry a real default.
//
// At 0.9 the column is 59% spent at 1 m of path, 88% at 2.4 m, and 99% by 5 m -- i.e. it saturates
// across roughly the same window the see-into curve closes in, which is what makes "it went flat
// at about the depth it went invisible" read as one event instead of two.
// RAISED 0.9 -> 1.7 on 2026-08-25 after the first headed A/B. Inert while `silt_scatter` is 0,
// so this cannot change the shipped surface -- it changes how far up the scatter slider you have
// to go before anything happens. At 0.9 the murk only read with silt_scatter, turbidity_veil AND
// sightline_scale all at maximum SIMULTANEOUSLY; at half settings the frame was within 1.7/255 of
// the shipped one, i.e. the entire useful effect lived in the top ~10% of the slider with no
// headroom left. At 1.7 the column is 82% spent at 1 m of path and 99% by 2.7 m, which puts a
// usable look near the MIDDLE of the scatter slider and leaves room above it.
uniform float silt_density : hint_range(0.02, 4.0) = 1.7;

// The colour the murk goes TO. See the never-bluer note above: the blue channel is clamped to the
// green channel in fragment(), so this cannot be dragged cool no matter what is typed here.
//
// The default is the LAKE's family -- a tannic grey-green, the shallow colour drained of most of
// its saturation and dropped in value, which is what a wooded lake's suspended matter actually
// looks like when the sun is in it. river_surface.tres's silt is browner and greyer (#454038's
// family); the lab's water subject switches both together, so the A/B is a real one.
//
// It is a vec3 and the panel cannot generate a slider for a vec3 -- see the entry doc. It is set
// by the material preset in scripts/lab.gd, which is also how `shallow_col` and `deep_col` are
// reached. That is a limitation of the panel, not of the shader.
// RAISED in value on 2026-08-25, same headed pass, and this is the more important of the two.
//
// The argument FOR a scattering term is that Beer-Lambert extinction can only ever reach BLACK,
// and black water is CLEAR water -- deep, still, nothing suspended in it. Murk goes lighter and
// FLATTER with depth because the suspended matter is catching light on its way past. The previous
// default, vec3(0.105, 0.112, 0.088), was DARKER than the water it was tinting, so it could only
// ever darken: the term contradicted the reason it exists. Measured, moving it lighter tripled the
// visible effect at an unchanged dial setting.
//
// Still the LAKE's tannic family -- green highest, blue lowest, so the never-bluer clamp in
// fragment() is never even reached -- just at the value suspended matter actually sits at with sun
// in it. river_surface.tres carries the browner, greyer silt (#454038's family) and the lab's
// preset picker switches both together.
//
// THIS COLOUR IS A STARTING POINT, NOT A RULING. It was raised to make the dial usable, not
// because anyone judged it right; where the murk finally lands is a look call.
//
// NOTE WHAT CHANGED UNDER IT: `silt_scatter` now defaults to 1.0, so this colour is no longer
// inert at the defaults -- it is the colour BOTH bodies of water are actually being pulled
// toward, in the game, right now. Judging it is live work rather than hypothetical. See the
// header and `silt_scatter`'s own ruling block.
uniform vec3 silt_col : source_color = vec3(0.255, 0.285, 0.230);

// How much of the murk the NIGHT is allowed to have. The header is explicit that every night dial
// in this file only ever darkens, and a scattering term is the first one in the file with the
// arithmetic to do the opposite. At 0.35 the night keeps enough haze to stay low-contrast --
// which is the direction §9 row -- without ever lifting above what the moon actually lights.
//
// 1.0 gives night and day the same murk, and is the honest way to see how much of the night's
// character was the extinction pair rather than the murk. It is not a shipping value.
uniform float silt_night_scale : hint_range(0.0, 1.0) = 0.35;

// PART 2 -- THE VEIL. Depth-INDEPENDENT cloudiness in the shallows.
//
// Part 1 is a function of path length, so it necessarily goes to zero at the waterline: the
// shallows stay as clear as they ship. Real turbid water does not do that -- an ankle-deep silty
// margin is already cloudy, and that cloudiness is most of what stops a river reading as wet sand.
// This raises a floor under both the water's opacity and its pull toward `silt_col`, with no depth
// term at all.
//
// IT IS STILL MULTIPLIED BY THE SHORELINE FADE, which is not optional. The fade takes ALPHA to
// zero where the bed comes up through the surface, and it is the only thing standing between this
// plane and a hard intersection line drawn along the whole beach. A veil applied after it would
// plant exactly that line. 0 = off and identical to the shipped surface.
uniform float turbidity_veil : hint_range(0.0, 1.0) = 0.0;

// PART 3 -- SIGHTLINE. "You can see about N metres down" is how murk is described by everybody who
// is not writing a shader, and it is far easier to reason about than a per-metre coefficient.
//
// This is a SCALE on the existing `alpha_extinction_*` pair, not a second coefficient, and the
// distinction is the whole reason it is shaped this way. Those two numbers are authored as a pair
// with a deliberate day/night ratio (river_surface.tres tabulates exactly what depths they produce
// and says RiverLab recomputes them rather than trusting the comment); dragging them independently
// destroys the ratio and nobody notices until the night looks wrong. One multiplier keeps it.
//
// LARGER = YOU SEE FURTHER (the coefficient goes DOWN). 1.0 is the shipped sightline exactly.
// 2.0 doubles every distance in river_surface.tres's table; 0.5 halves them.
uniform float sightline_scale : hint_range(0.25, 4.0) = 1.0;


// --- the sun, and what it does to the surface -----------------------------------------------------
// See the header for why the night glint is a threshold rather than a dimmer.
// EVERY GAIN BELOW IS MULTIPLIED BY LIGHT_COLOR, WHICH IS NOT WHAT IT LOOKS LIKE. For a
// directional light Godot's light() receives LIGHT_COLOR = light_color * light_energy * PI, so the
// midday sun (energy 1.3) arrives as roughly 4.1, and the deep-night moon at darkness level 1
// (0.60) as roughly 1.9. The numbers here are sized against that, which is why they look small for
// something meant to blow out. Multiply by ~4 before judging one by eye.
//
// WATER-1 LOWERED BOTH EXPONENTS, AND IT IS A CONSEQUENCE OF THE FACET LATTICE RATHER THAN A TASTE
// CHANGE. The shipped values (420 day / 1500 night) were sized against a SMOOTH normal field, where
// a razor lobe is what isolates the beads. Against a faceted field the normal is piecewise constant,
// so the lobe is already hard-edged and the exponent no longer controls sharpness -- it controls how
// many facets are close enough to the mirror direction to light up at all. Left at 420 the glitter
// road thinned to a scatter of single plates. These values put the density back where the old
// exponents put it on the old field; the shine is the same size on screen, it is simply made of
// facets now instead of beads.
group_uniforms sun;
uniform float glint_exp_day : hint_range(4.0, 4000.0) = 190.0;
uniform float glint_exp_night : hint_range(4.0, 4000.0) = 900.0;
uniform float glint_floor_day : hint_range(0.0, 0.99) = 0.14;
uniform float glint_floor_night : hint_range(0.0, 0.99) = 0.55;
// Day gain is deliberately sized so the mirror configuration BLOWS OUT (1.6 * 4.1 = 6.5, well over
// white). There is no glow in the camp Environment -- OutdoorAtmosphere builds it with
// TonemapMode = Filmic and never enables glow -- so this saturates through the filmic roll-off
// into white glare rather than blooming. That is the OPPOSITE of the constraint
// DecorativeWater.gdshader's header documents: that shader is bounded by Playground.tscn's glow
// gate at 1.0, and this surface is not in that scene. Do not carry its `specular_disabled`
// reasoning across; it does not apply here, and applying it would delete the headline feature.
uniform float glint_gain_day : hint_range(0.0, 8.0) = 1.75;
uniform float glint_gain_night : hint_range(0.0, 8.0) = 2.60;

// The fresnel curve. Direction §9 calls this the highest-value row after the specular.
//
// Day: the face-on term is high enough to see into the shallows.
// Night: RAISE the grazing term, DROP the face-on term. Looking out across the water gives you a
// sheen off the horizon that does not exist; looking DOWN, which is what a swimmer does, gives you
// nothing. That is literally "you cannot see below you", achieved with a curve and zero content.
//
// The grazing term is a broad sheen and NOT a reflection: reflecting the actual treeline needs
// planar reflections or a realtime probe, both banned by §8.2. It is computed in light() against
// LIGHT_COLOR, so like everything else here it dies with the lights rather than floating on top
// of them.
uniform float fresnel_power_day : hint_range(0.5, 12.0) = 4.2;
uniform float fresnel_power_night : hint_range(0.5, 12.0) = 6.8;
uniform float sheen_face_day : hint_range(0.0, 1.0) = 0.010;
uniform float sheen_face_night : hint_range(0.0, 1.0) = 0.0015;
// WATER-1 cut this from 0.050 to 0.034. Banded, the grazing sheen stops being a soft wash and
// becomes chunky pale terraces -- which is the intended read, and at the shipped strength those
// terraces were covering roughly half the near surface at V 0.52 while the murk underneath sat at
// V 0.28. The water photographed as a pale grey plate with dark holes in it, which is the exact
// opposite of "murky, gloomy, scary dark". The sheen is kept because the terraces are good; it is
// spent down to where the murk is the loudest thing in the frame.
uniform float sheen_grazing_day : hint_range(0.0, 1.0) = 0.034;
uniform float sheen_grazing_night : hint_range(0.0, 1.0) = 0.045;
// How much of the depth colour the sun lights diffusely. Water is not a lambertian surface; this
// is small on purpose and exists so the shelf is not lit purely by ambient.
uniform float diffuse_amount : hint_range(0.0, 1.0) = 0.14;

// HOW FAR THE NIGHT SPECULAR IS PULLED TOWARD NEUTRAL, AND WHY IT HAS TO BE.
//
// Direction §14.2 is a hard constraint: the night water goes darker and lower-contrast, NEVER
// bluer. The first headed capture failed it outright and the cause was not a colour choice --
// it was physics. Every specular term here is multiplied by LIGHT_COLOR, and at night LIGHT_COLOR
// is the MOON, which OutdoorAtmosphere authors as a cool blue. The glints and the sheen therefore
// inherited that blue and, being the brightest thing on a near-black surface, they WERE the
// water's colour. The lake came back a bright cornflower sea.
//
// Desaturating the specular toward its own luminance keeps the term strictly proportional to the
// real light -- so the no-floor guarantee is untouched, it still goes to zero when the moon does --
// while taking the hue out of it. The dusk sweep's orange->plum->indigo stays the sole warning
// lane, which is what §14.2 is protecting.
//
// It is applied on the NIGHT side only: the day sun's warmth on the water is wanted, and the
// morning glare going gold is most of what makes it read as morning.
uniform float spec_neutralise_night : hint_range(0.0, 1.0) = 0.85;

// --- underside -------------------------------------------------------------------------------------
// What a submerged camera sees (W1's fourth finding, and W2 already ships the swim state that
// makes it reachable). The decision: a DARK, NEAR-OPAQUE CEILING in the deep colour, with the
// glints heavily attenuated and the sheen gone.
//
// Reasoning. W2 holds a swimmer's eye 0.15 m ABOVE the surface, so the underside is only ever seen
// during the 1.5 s go-under and while sputtering. Direction §5 wants that beat to be a complete
// release by day and the identical event with the release WITHHELD at night -- neither is served by
// a spectacle overhead, and §13/§8.4 forbids dressing the go-under as an event at all. So the
// underside is featureless on purpose. It is the same mesh, the same draw call and one branch;
// spec §8.2 bans the second "underwater" mesh that would be the alternative.
group_uniforms underside;
uniform float under_glint_scale : hint_range(0.0, 1.0) = 0.12;
uniform float under_alpha : hint_range(0.0, 1.0) = 0.94;


// --- §7B, part 3: THE SKY MIRROR, which is the reflection §7B is actually talking about ------------
//
// THIS IS THE SINGLE BIGGEST FINDING OF WATER-1'S FIRST HEADED PASS AND IT IS WORTH READING BEFORE
// TOUCHING EITHER NUMBER BELOW.
//
// Everything the original header describes -- the glints, the fresnel sheen -- is written by hand
// inside light(). But light() is not the only thing that lights this surface. Godot's Forward+
// renderer ALSO runs its own ambient/reflection pass, which is not overridden by a custom light()
// function and which reflects the sky's radiance off the surface using ROUGHNESS, METALLIC and
// SPECULAR. The shipped surface set ROUGHNESS = 0.04, which is a near-perfect mirror, so the water
// was reflecting the noon sky at full sharpness through a path nothing in the file mentions.
//
// THAT is what made it "too real", and it is also why the first pass of this packet appeared to
// have no palette at all: the depth ramp was correctly producing a near-black silty brown, the sky
// mirror was adding roughly 0.10 linear of bright blue-white on top of it, and 0.10 on top of black
// is a blue-grey plane. Every colour decision in this file was invisible underneath it.
//
// §7B, water clause: "keep the reflections themselves (they're wanted), but reduce their fidelity to
// match -- a lower-resolution reflection buffer or a stepped/flattened normal map, so reflections
// read blocky and low-poly rather than crisp and mirror-sharp." The facet lattice is the stepped
// normal half. These two uniforms are the lower-fidelity-buffer half, done with the dials Godot
// actually exposes: roughness blurs the reflection, and the dielectric F0 sets how much of it comes
// back at all. The reflection is KEPT -- it is what makes the water read as a surface and not as a
// hole -- and it is spent down to where the murk is the loudest thing in the frame.
//
// ROUGHNESS IS ALSO THE UNDERSIDE FLAG (see fragment(), where light() reads it back as
// step(0.5, ROUGHNESS)), so the topside value MUST stay below 0.5. 0.42 is deliberately close to
// that ceiling: it is as rough as the flag channel permits. If a future pass wants a rougher
// topside than 0.5 it has to move the flag to another channel FIRST -- silently raising this past
// 0.5 does not make the water rougher, it makes every top-facing fragment take the underside
// branch, and the surface goes flat and near-opaque with no error anywhere.
group_uniforms reflection;
uniform float top_roughness : hint_range(0.0, 0.49) = 0.42;
// Dielectric reflectance dial: Godot maps this to F0 = 0.08 * specular, so the shipped default of
// 0.5 gives water's textbook 4% and 0.18 gives about 1.4%. Physically wrong on purpose -- §2.4's
// argument is that the landscape's job is to be quiet so the events can be loud, and a river that
// mirrors the sky at 4% is the brightest thing in a frame it is supposed to be the floor of.
uniform float sky_reflect : hint_range(0.0, 1.0) = 0.12;


varying vec3 v_world;
varying vec2 v_swell_slope;


group_uniforms lab;
// SHADER LAB, 2026-08-25. Below 0: LIVE, driven by TIME -- the shipped behaviour, identically.
// At or above 0: the whole surface FREEZES at that many seconds into its cycle.
//
// WHY A SHIPPING SHADER CARRIES A LAB DIAL. This surface animates on raw TIME, so two captures of
// the SAME build are never the same picture, and every A/B against it is noise-limited rather than
// exact. Measured 2026-08-25 at a fixed 1280x720: two runs of one unchanged build differ across
// 4.0% of pixels with a peak channel delta of 225. That is larger than some real changes worth
// seeing, and it is why proving the murk-compositing fix was an identity at its defaults needed a
// separate noise-floor control rather than a plain diff.
//
// `vertex_wind` already carries exactly this dial for exactly this reason, and its header states
// the principle: a still frame cannot distinguish an effect that is not moving from one that is
// broken, so the clock gets exposed. Two captures at two scrub values are a reproducible
// before/after of the same water.
//
// It defaults to LIVE, so it is a no-op for the game -- and the branch reads a uniform, so it is
// uniform control flow across the draw call and never divergent.
uniform float time_scrub : hint_range(-1.0, 120.0, 0.01) = -1.0;

// The one clock every animated term in this file reads. One accumulator, many consumers -- the
// same argument the lab's Conductor makes. Two terms each reading TIME separately are two timers
// that agree at t=0 and disagree forever after, and a scrub that moved only some of them would
// freeze the water into a pose it never actually holds.
float wtime() {
	return time_scrub < 0.0 ? TIME : time_scrub;
}


// How far the whole sampling frame has slid downstream by now, in world XZ metres.
//
// Sampling a static field at `p - v*t` makes a feature that lives at p0 appear where p - v*t = p0,
// i.e. at p0 + v*t -- so the pattern travels along +current_dir at current_speed_mps. It is the one
// place the current enters the shader, and BOTH stages call it, which is what keeps the swell's
// crests and the ripple field travelling as one body of water instead of sliding past each other.
//
// At current_speed_mps = 0 this returns exactly vec2(0.0) and every downstream expression is
// unchanged from the shipped lake -- not approximately, identically.
vec2 current_offset() {
	if (current_speed_mps <= 0.0) {
		return vec2(0.0);
	}
	vec2 d = current_dir;
	float len = length(d);
	// A zero or degenerate direction with a non-zero speed is an authoring mistake, not a request
	// for a NaN. Fall back to still water rather than tilting the surface into the void.
	if (len < 1e-4) {
		return vec2(0.0);
	}
	return -(d / len) * (wtime() * current_speed_mps);
}


void vertex() {
	// VERTEX is world space here (world_vertex_coords).
	// The swell is evaluated in the ADVECTED frame so its crests travel downstream with the
	// current; v_world below stays the true, un-advected world position, because the fragment
	// stage needs it for camera distance and for its own advection.
	vec2 wxz = VERTEX.xz + current_offset();

	float amp = mix(swell_amp_night, swell_amp_day, sky_light);
	vec2 da = normalize(swell_dir_a);
	vec2 db = normalize(swell_dir_b);
	float ka = 6.2831853 / swell_len_a_m;
	float kb = 6.2831853 / swell_len_b_m;
	float pa = dot(wxz, da) * ka + wtime() * swell_speed_a;
	float pb = dot(wxz, db) * kb + wtime() * swell_speed_b;

	VERTEX.y += amp * (0.62 * sin(pa) + 0.38 * sin(pb));

	// The swell's analytic slope, carried to the fragment stage. Two cos() calls that are already
	// paid for, and they matter more than the 2 cm of displacement does: a long, gentle tilt of a
	// degree or two is what SWINGS the glitter path around as the swell passes, and without it the
	// glare sits still on a surface that is visibly moving.
	v_swell_slope = vec2(
		amp * (0.62 * cos(pa) * ka * da.x + 0.38 * cos(pb) * kb * db.x),
		amp * (0.62 * cos(pa) * ka * da.y + 0.38 * cos(pb) * kb * db.y));

	v_world = VERTEX;
}


// Decode one tangent-space sample. Z is RECONSTRUCTED rather than read, so the texture survives
// being imported as a compressed normal map (BC5/RGTC keeps only two channels) with no change here
// and no silent flattening. gen_water_normal.py holds peak |XY| at 0.710 so the sqrt never
// degenerates. The green convention does not matter -- the field is isotropic, so a flipped green
// rotates the ripples and changes nothing anybody can name.
vec3 sample_ripple(vec2 uv, float amp) {
	vec2 xy = (texture(normal_map, uv).rg * 2.0 - 1.0) * amp;
	return vec3(xy.x, sqrt(max(1.0 - dot(xy, xy), 1e-4)), xy.y);
}


// Water depth in metres under this fragment, from the one permitted DEPTH_TEXTURE read.
//
// Reconstructs the WORLD position of whatever opaque geometry is behind the surface and subtracts
// its height from this fragment's own. Working in world Y rather than in view-space distance makes
// the result a physical depth that is the same from every camera angle -- which is what both the
// colour ramp and the shoreline fade actually want, and it means a capture taken from the dock and
// one taken from the drop-off grade identically.
//
// Where the plane overruns the bank (camp_gen runs it 3 m east of the shoreline so the water tucks
// UNDER the bank instead of stopping short) the bed is above the surface, this goes negative, and
// the shoreline fade takes ALPHA to zero. Where nothing is behind the surface at all -- open sky
// past the map edge -- the reconstruction lands at the far plane and the water reads as fully deep,
// which is correct for a lake with no far shore.
float water_depth_m(vec2 screen_uv, float surface_y, mat4 inv_proj, mat4 inv_view) {
	float raw = texture(depth_tex, screen_uv).r;
	vec4 view_pos = inv_proj * vec4(screen_uv * 2.0 - 1.0, raw, 1.0);
	view_pos.xyz /= view_pos.w;
	vec4 world_pos = inv_view * vec4(view_pos.xyz, 1.0);
	return surface_y - world_pos.y;
}


void fragment() {
	float night = 1.0 - sky_light;

	// --- the two scrolling octaves ----------------------------------------------------------------
	// Scroll rates are authored in metres per second and divided by the tile size, so changing a
	// tile scale re-scales the pattern without also changing how fast it appears to move.
	//
	// The current's advection is added to the sampling position BEFORE the per-octave scroll, so
	// the octaves ride on the moving water rather than replacing its motion. On a lake
	// (current_speed_mps = 0) `adv` is exactly zero and this is the shipped expression.
	float flow_scale = mix(night_flow_scale, 1.0, sky_light);
	vec2 adv = current_offset();
	vec2 fb = normalize(flow_broad);
	vec2 fc = normalize(flow_coarse);

	vec2 uv_b = (v_world.xz + adv + fb * (wtime() * rate_broad_mps * flow_scale)) / tile_broad_m;
	vec2 uv_c = (v_world.xz + adv + fc * (wtime() * rate_coarse_mps * flow_scale)) / tile_coarse_m;

	// THE HANDOVER. Weights are a function of camera distance only, so they vary smoothly across
	// the surface and never branch -- every fragment does the same two fetches, which keeps the
	// texture reads out of divergent control flow (where their implicit mip derivatives would be
	// undefined) and keeps the cost flat and predictable.
	float cam_dist = length(v_world - CAMERA_POSITION_WORLD);
	float w_coarse = 1.0 - smoothstep(coarse_fade_start, coarse_fade_end, cam_dist);
	// Rises as the coarse falls: the far water keeps structure instead of going to mirror.
	float w_broad = mix(broad_near, 1.0, 1.0 - w_coarse);

	vec3 n_b = sample_ripple(uv_b, broad_amp * w_broad);
	vec3 n_c = sample_ripple(uv_c, coarse_amp * mix(coarse_amp_night_scale, 1.0, sky_light) * w_coarse);

	// Combine in world space. Summing the tangential components and keeping Y = 1 is the cheap
	// standard blend; it is not "correct" normal reorientation, but on a horizontal plane with
	// slopes this shallow the difference is invisible and the correct version costs a normalize per
	// octave.
	//
	// The swell slope goes in here too, which is what makes the glare path travel with the swell.
	vec2 slope = vec2(
		n_b.x + n_c.x + v_swell_slope.x,
		n_b.z + n_c.z + v_swell_slope.y);

	// --- §7B: THE FACET LATTICE ---------------------------------------------------------------
	// Snap the tangential slope onto a coarse lattice, with a soft edge at each cell boundary. This
	// is the same shape as toon_band() -- floor, fractional part, smoothstep across the middle --
	// but 2D and unbounded, because a slope is not a 0..1 term and clamping it would flatten the
	// surface rather than facet it. See `group_uniforms facets` for what this buys and why it
	// dissolves harmlessly into the distance instead of aliasing.
	vec2 q = slope * facet_steps;
	vec2 qf = floor(q);
	vec2 qt = q - qf;
	float fw = max(facet_softness, 0.0005);
	vec2 qe = smoothstep(vec2(0.5 - fw), vec2(0.5 + fw), qt);
	slope = (qf + qe) / facet_steps;

	vec3 n_world = normalize(vec3(slope.x, 1.0, slope.y));

	// Seen from below, the surface's outward normal points down. Flipping it here is what lets ONE
	// mesh serve both sides -- see the underside note above.
	if (!FRONT_FACING) {
		n_world = -n_world;
	}

	NORMAL = (VIEW_MATRIX * vec4(n_world, 0.0)).xyz;

	// --- depth tint --------------------------------------------------------------------------------
	float depth_m = water_depth_m(SCREEN_UV, v_world.y, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX);

	// SHADER LAB: these three lines were MOVED UP from the alpha block below and are otherwise
	// unchanged -- same expressions, same operands, same result. They are hoisted because the murk
	// term needs the path length while the tint is still being built, and computing it twice would
	// be two places to edit and one place to forget. Their original comments came with them.
	//
	// Path length through the water along the view ray -- see `see_path_min_cos` for why this is
	// not the vertical depth the colour ramp uses. abs() so a submerged camera looking up gets the
	// same treatment as one looking down rather than a negative path.
	vec3 to_cam = normalize(CAMERA_POSITION_WORLD - v_world);
	float cos_v = max(abs(to_cam.y), see_path_min_cos);
	// pow(cos, 1) is the true path length; the exponent spends only part of it. At cos = 1 (looking
	// straight down) pow() is 1 for every exponent, so the straight-down case is untouched no
	// matter how this is tuned -- which is what keeps the quoted distances honest.
	float path_m = max(depth_m, 0.0) / pow(cos_v, see_path_obliquity);

	float k = mix(extinction_night, extinction_day, sky_light);
	float t = 1.0 - exp(-max(depth_m, 0.0) * k);

	// §7B: QUANTISE THE DEPTH RAMP. The one DEPTH_TEXTURE fetch above is unchanged and is still the
	// only one; what is banded is the VALUE it produced, not a second sample of it.
	//
	// A continuous `t` is a per-pixel readout of exactly how deep the bed is. Stepped into four it
	// says four things -- shelf, drop, channel, nothing -- which is WATER-1's brief ("the player
	// cannot confirm what is a few feet down, and that uncertainty is the dread, not the gradient")
	// expressed as arithmetic rather than as a colour choice. It is also what makes the terraces
	// countable in the capture, which is this packet's third acceptance criterion.
	//
	// IT IS APPLIED TO THE COLOUR ONLY. Not to the shoreline fade (a banded waterline would put a
	// hard stair along the whole bank), and -- since WATER-1b -- not to ALPHA either, which now has
	// its own curve below. See `group_uniforms see_into` for why those two had to be separated.
	t = toon_band(t, depth_bands, band_softness);

	// Night drags shallow toward deep (they converge), then drops the whole ramp in value and
	// saturation. Both operations can only ever darken; neither can lift the water above the
	// scene's own light, which is what keeps §6.2 Talon's to decide.
	vec3 shallow = mix(shallow_col, deep_col, night * night_converge);
	vec3 tint = mix(shallow, deep_col, t);

	// --- SHADER LAB: MURK ---------------------------------------------------------------------
	// THE NEVER-BLUER LAW, AS ARITHMETIC. Direction §14.2 forbids the water going bluer, and this
	// clamp is that sentence in a form nobody can override by accident: whatever `silt_col` holds,
	// its blue channel is not allowed above its green. Tannic green-black and brown-grey silt both
	// pass untouched; anything cool is taken back.
	vec3 silt = vec3(silt_col.r, silt_col.g, min(silt_col.b, silt_col.g));
	// Declared out here because the alpha block below spends the same two numbers -- the murk has
	// to occlude by exactly as much as it lightens, or the water goes pale without going opaque
	// and reads as fog on glass rather than as dirty water.
	float silt_amt = 0.0;
	float veil_amt = 0.0;
	if (!debug_no_murk) {
		// The scattering column, saturating with PATH length. This is the term the shipped shader
		// does not have: extinction can only reach black, and black is clear water. See the murk
		// uniform block for the whole argument.
		float sc = 1.0 - exp(-max(path_m, 0.0) * silt_density);
		float night_gate = mix(silt_night_scale, 1.0, sky_light);
		silt_amt = clamp(silt_scatter * sc * night_gate, 0.0, 1.0);
		veil_amt = clamp(turbidity_veil * night_gate, 0.0, 1.0);
		// The veil fills what the scattering has not already taken, so the two compose to at most
		// 1.0 instead of fighting over the same pixel.
		tint = mix(tint, silt, clamp(silt_amt + veil_amt * (1.0 - silt_amt), 0.0, 1.0));
	}
	// The two night operations below run AFTER the murk on purpose. night_desaturate and
	// night_value can then only ever take the murk DOWN, which is what keeps the header's promise
	// that the night dials in this file darken and never lift -- structurally, rather than by a
	// number somebody chose. Nothing about §6.2 is decided here.
	//
	// The murk is NOT banded, and that is the one place it deliberately departs from §7B's
	// quantisation clause. The bands exist to make the depth ramp countable; the murk exists to
	// make it uncountable. Stepping the flattening would put a hard edge on the one term whose
	// entire job is to remove edges -- the same argument the see_into block makes for taking ALPHA
	// off the banded ramp, one feature later.
	float grey = dot(tint, vec3(0.2126, 0.7152, 0.0722));
	tint = mix(tint, vec3(grey), night * night_desaturate);
	tint *= mix(1.0, night_value, night);

	ALBEDO = tint;

	// --- the see-into zone, alpha, and the soft shoreline ----------------------------------------
	//
	// ALPHA NO LONGER READS THE BANDED `t`. It runs its own Beer-Lambert curve on the same
	// depth_m -- no second fetch, no second sample, one more exp() -- with its own coefficient and
	// its own quantisation setting. `group_uniforms see_into` carries the whole argument for why.
	//
	// The quantity being modelled here is not "how opaque is the water", it is "how much of a
	// submerged surface survives to the eye", which is (1 - a_shallow) * exp(-depth * ka). Writing
	// it as a mix from a_shallow to 1 is the same arithmetic and keeps the shipped shape.
	float ka = mix(alpha_extinction_night, alpha_extinction_day, sky_light);
	// SHADER LAB: "you can see about N metres down", as a scale on the authored pair rather than
	// as a replacement for it -- see `sightline_scale`. Larger scale, smaller coefficient, further
	// sightline. At 1.0 this is an exact identity and the shipped distances are unchanged.
	if (!debug_no_murk) {
		ka /= max(sightline_scale, 0.01);
	}
	float a_shallow = mix(alpha_shallow_night, alpha_shallow_day, sky_light);
	// `to_cam`, `cos_v` and `path_m` are computed up in the depth-tint block -- the murk term
	// needs the path length before this point. Same expressions, moved, not changed.
	float u = 1.0 - exp(-path_m * ka);
	// alpha_bands < 2 means SMOOTH, and smooth is what ships. The uniform exists because WATER-1b
	// was asked to test banded transparency rather than assume, and a reviewer should be able to
	// reproduce both sides without editing the file. It is uniform control flow, so every fragment
	// takes the same branch and nothing here is inside divergent flow.
	if (alpha_bands >= 2.0) {
		u = toon_band(u, alpha_bands, band_softness);
	}
	float alpha = mix(a_shallow, 1.0, u);
	// SHADER LAB: suspended matter is opaque as well as bright. Raising the tint toward `silt_col`
	// without raising ALPHA to match would light the water column and then let you see straight
	// through it -- fog on glass, the exact failure the header's night-specular section names for
	// a different term.
	//
	// ==========================================================================================
	// THIS WAS A max() UNTIL 2026-08-25, AND THE max() WAS THE FLAT RESPONSE CURVE.
	// ==========================================================================================
	// The old comment argued: "a max() rather than an add: the see-into curve may already be more
	// opaque than the murk is, and in that case the murk has nothing left to occlude." The INTENT
	// is right and max() does not implement it. max() is a hard gate -- while the murk is less
	// opaque than the water already is, raising it changes NOTHING, and the moment it crosses,
	// everything happens at once.
	//
	// The arithmetic, at the lab's day defaults (sky_light 1.0, alpha_shallow_day 0.18,
	// alpha_extinction_day 0.55), for a fragment whose view path through the water is ~3 m:
	//
	//     u     = 1 - exp(-3.0 * 0.55) = 0.808
	//     alpha = mix(0.18, 1.0, 0.808) = 0.843
	//     sc    = 1 - exp(-3.0 * 1.70) = 0.994,  so  silt_amt ~= silt_scatter
	//
	// so max(0.843, silt_scatter) is CONSTANT for every silt_scatter below ~0.84. That is the
	// measured behaviour exactly: ladder contrast 52.40 / 52.19 / 51.99 / 50.56 / 45.39 at
	// silt_scatter 0 / 0.25 / 0.50 / 0.75 / 1.00 -- flat, then a cliff, with the cliff sitting
	// where the gate is. The dead zone was never a tuning problem; it was this operator.
	//
	// TWO ATTENUATORS COMPOSE BY MULTIPLYING TRANSMITTANCE, not by taking the larger opacity.
	// Light has to survive the water column AND the silt suspended in it, so T = T_water * T_silt
	// and opacity = 1 - (1 - alpha) * (1 - m). That is precisely "the murk occludes what is
	// left", which is what the old comment said it wanted. It is monotonic from the first
	// increment, it has no gate, and it cannot exceed 1.
	//
	// THIS OPERATOR CHANGE WAS AN EXACT IDENTITY AT THE DEFAULTS IN FORCE WHEN IT LANDED, and that
	// is a claim about the change, not about what ships today. At that commit both `silt_scatter`
	// and `turbidity_veil` were 0 and neither .tres overrode either, so m = 0 and alpha came back
	// untouched -- 0 differing pixels, measured, which is how the fix was proved to be a reshaping
	// of the curve rather than a retune of the water.
	//
	// WHAT SHIPS TODAY IS NOT THAT. `silt_scatter` was ruled to 1.0 hours later (see its header),
	// so m is large at the defaults and the camp lake IS repainted. It is repainted by THAT
	// ruling and not by this line: revert the one number and this operator goes back to returning
	// alpha unchanged. Do not read the identity above as "the lake is untouched" -- it is the
	// reason the two changes can still be reasoned about separately.
	//
	// `veil_amt` reaches the waterline where the scattering term cannot, which is what makes the
	// shallows read as CLOUDY rather than merely less transparent. It is still multiplied by the
	// shoreline fade on the very next line, so it can never plant a hard edge along the beach.
	if (!debug_no_murk) {
		float m = clamp(silt_amt + veil_amt * (1.0 - silt_amt), 0.0, 1.0);
		alpha = 1.0 - (1.0 - alpha) * (1.0 - m);
	}
	// The shoreline fade. Tuned LAST and deliberately generous, because it is the one parameter that
	// blends the water directly into the ground plane -- and the ground plane on this trunk still
	// renders day-green at night while trunks, sky and grass correctly go night-blue. Every value in
	// this shader was graded against trunks, sky and grass; any shoreline capture is provisional
	// until that bug is fixed. See the report.
	float fade_m = mix(shore_fade_night_m, shore_fade_day_m, sky_light);
	alpha *= smoothstep(0.0, fade_m, depth_m);

	// ROUGHNESS IS BEING USED AS A ONE-BIT CHANNEL, AND THIS IS THE REASON. light() has to know
	// which face it is shading, and FRONT_FACING is a fragment-stage built-in that the light
	// function cannot read. ROUGHNESS is one of the few values light() DOES receive, this shader's
	// lobe is shaped by the glint_exp_* uniforms rather than by roughness, so the channel is free --
	// and the meaning is honest rather than a smuggled flag: the underside genuinely is the matte
	// side of this surface and the topside genuinely is the polished one. light() reads it back as
	// step(0.5, ROUGHNESS).
	//
	// `top_roughness` is clamped to 0.49 by its own hint_range and clamped again here, because
	// crossing 0.5 does not roughen the water -- it flips every top-facing fragment onto the
	// underside branch, silently. See the `reflection` uniform block.
	ROUGHNESS = clamp(top_roughness, 0.0, 0.49);
	// The dielectric F0 the engine's own ambient/reflection pass uses. It does NOT touch anything
	// light() computes -- the glints and the sheen are added to SPECULAR_LIGHT directly -- so this
	// is a dial on the SKY MIRROR alone.
	SPECULAR = sky_reflect;
	if (!FRONT_FACING) {
		// From underneath there is no shoreline to fade against and no bed behind the surface to
		// read a depth from -- the reconstruction above is measuring whatever is beyond the water,
		// which is the sky. A flat, near-opaque ceiling is the decided answer.
		ALBEDO = deep_col * mix(1.0, night_value, night);
		alpha = under_alpha;
		ROUGHNESS = 1.0;
	}

	ALPHA = clamp(alpha, 0.0, 1.0);
	METALLIC = 0.0;
}


void light() {
	// Everything in here is multiplied by LIGHT_COLOR, which is the sun's or the moon's real colour
	// times its real energy. That is what makes the shimmer track the actual DirectionalLight3D --
	// no sun direction is pushed from C# and there is no second source of truth to drift -- and it
	// is also what makes the no-floor claim in the header structural: at zero energy this function
	// contributes exactly zero.

	// 1.0 when this is the underside of the plane, 0.0 on top -- see the ROUGHNESS note in
	// fragment() for why the flag travels in this channel.
	float under = step(0.5, ROUGHNESS);

	vec3 n = normalize(NORMAL);
	vec3 v = normalize(VIEW);
	vec3 l = normalize(LIGHT);

	// Diffuse. Small: water is not lambertian, and this exists so the shelf is not lit by ambient
	// alone. Half-lambert wrap keeps the far side of a swell from going to a hard terminator.
	//
	// §7B BAND. The wrapped lambert is quantised into `light_bands` flat tones before it is spent,
	// by the same toon_band() the bank's material uses at the same count, so the water's tonal
	// response to the sun steps where the bank's steps rather than sliding smoothly past it.
	// toon_band returns a 0..1 SHAPE and is still multiplied by LIGHT_COLOR here -- at zero energy
	// this term is zero, banded or not, so the header's no-floor guarantee is untouched.
	float ndl = dot(n, l) * 0.5 + 0.5;
	float diff = toon_band(ndl * ndl, light_bands, band_softness);
	DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR * ATTENUATION * diff * diffuse_amount;

	// --- the glint ------------------------------------------------------------------------------
	// Blinn half-vector rather than a reflect(): one add and a normalize instead of a reflect, and
	// the exponent is the only shape control the direction table actually asks for.
	vec3 h = normalize(l + v);
	float ndh = max(dot(n, h), 0.0);
	float e = mix(glint_exp_night, glint_exp_day, sky_light);
	float spec = pow(ndh, e);

	// The threshold that turns a lobe into patches, and it is spent at BOTH ends of the day. Night
	// runs it high over a razor exponent, leaving isolated hard points on a black field. Day runs
	// it low but NOT zero: a zero floor gives a smooth falloff that averages into a flat wash,
	// which is what made the first pass photograph as a coloured plane with a smear on it. Glare is
	// glints; the day glints are simply brighter, denser and larger than the night's.
	//
	// §7B BAND, AND THIS IS THE SHINE TALON ASKED TO KEEP. He said, in the words this packet was
	// written from, that he likes "the shine and the way the light catches the water". Removing it
	// would have been the easy way to stop the surface reading as real, and it is explicitly not
	// what was asked for. So the highlight is kept at full strength and QUANTISED: the smoothstep
	// still decides WHERE there is a highlight, and toon_band then decides how bright it is out of
	// `light_bands` fixed steps, so what used to be a continuous falloff is now a hard-edged core
	// with one stepped halo around it. Because the normal it is computed against is faceted, those
	// steps land on facet boundaries -- the highlight breaks into chunky plates that switch on and
	// off as the surface tilts through the mirror configuration, and it still swings across the
	// water as the sun moves, because nothing here touched the half-vector.
	float floor_t = mix(glint_floor_night, glint_floor_day, sky_light);
	float glint = toon_band(smoothstep(floor_t, 1.0, spec), light_bands, band_softness);

	float gain = mix(glint_gain_night, glint_gain_day, sky_light) * mix(1.0, under_glint_scale, under);

	// --- the fresnel sheen ------------------------------------------------------------------------
	// Broad, view-dependent, and NOT a reflection of anything real -- see the fresnel uniforms.
	//
	// §7B BAND. The fresnel curve is the one remaining smooth continuous gradient on the surface,
	// and left alone it undoes the rest of this pass: a banded, faceted water plane with a
	// perfectly smooth sheen laid over it reads as a toon surface behind glass. Banding it turns
	// the grazing sheen into a small number of horizontal terraces receding toward the far bank,
	// which is the same instrument the depth ramp uses and reads as the same painting.
	float fres = toon_band(
			pow(clamp(1.0 - max(dot(n, v), 0.0), 0.0, 1.0),
					mix(fresnel_power_night, fresnel_power_day, sky_light)),
			light_bands, band_softness);
	float face = mix(sheen_face_night, sheen_face_day, sky_light);
	float grazing = mix(sheen_grazing_night, sheen_grazing_day, sky_light);
	float sheen = mix(face, grazing, fres) * (1.0 - under);

	// The specular's own colour: the real light, hue-neutralised toward its luminance as the night
	// comes on. See spec_neutralise_night for why this is a §14.2 requirement and not a taste call.
	// Luminance-preserving, so this changes the hue and never the brightness -- it cannot act as a
	// floor, and it cannot make the night water lighter than the moon actually makes it.
	vec3 spec_col = mix(LIGHT_COLOR, vec3(dot(LIGHT_COLOR, vec3(0.2126, 0.7152, 0.0722))),
			spec_neutralise_night * (1.0 - sky_light));

	SPECULAR_LIGHT += spec_col * ATTENUATION * (glint * gain + sheen);
}
