shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_schlick_ggx;

// =================================================================================================
// NIGHT EDGE — geometry that draws its own outline once it gets dark. (BT-9, 2026-08-27.)
// =================================================================================================
//
// Program decision D10, verbatim: "Blue platform edges, the balance beam, the red goal blocks and
// the cyan obstacles carry an emissive edge that fades in with darkness (driven by the shipped
// cycle phase). Bubbles glow. The hub's counter is lit. NOTHING ELSE IN THE LEVEL GETS A LIGHT --
// the darkness is real and the aids are the point."
//
// That last clause is the design and it is also the constraint on this file. This shader emits; it
// never lights. There is no OmniLight, no light probe, no bounce -- an object wearing this material
// becomes visible IN THE DARK without making anything AROUND it visible. A player at night can see
// where the ledge is and still cannot see the ground they are standing on, which is the entire
// point of Talon's "some sections should become easier to navigate (not harder) when dark."
//
// -------------------------------------------------------------------------------------------------
// WHY THE EDGE IS GEOMETRIC AND NOT A FRESNEL RIM -- A CORRECTION, MADE BY A CAPTURE
// -------------------------------------------------------------------------------------------------
// The packet's requirement is that this "must read as 'an edge' on a plain box (fresnel-ish rim,
// not a full glow)". The reason is legibility, not taste. A box whose whole face emits is a glowing
// box: at night it is a bright blob whose SILHOUETTE you can see and whose near edge -- the one you
// are about to put a foot on -- you cannot.
//
// THE FIRST VERSION OF THIS SHADER USED A FRESNEL TERM, AND IT WAS WRONG. The reasoning was that
// grazing angles are where a surface turns away from you, so a Fresnel would collapse to a line
// along the silhouette. That is true of a SPHERE and false of a BOX, which is the shape this
// material is actually for. A box face is FLAT: every pixel on it shares one normal, so it shares
// one Fresnel value, and the term is constant per face. The headed capture
// (`docs/qa/bubble-test/BT-9/03-night-edge-on-boxes.png`, first run) showed exactly that failure --
// the faces angled away from the camera lit up as whole flat panels while the face the camera was
// looking at went black. That is a glowing box with some faces missing: the precise opposite of the
// requirement, and it survived being reasoned about because it looks correct on the sphere sheet.
//
// So the edge is now GEOMETRIC: a band inset from each face's border, measured in UV space, which
// draws a rectangle around every face including the one pointing at the player. Three properties
// follow, and all three are why this is the right term rather than a workaround:
//
//   * It works HEAD-ON. Looking straight down at a platform top -- the exact moment a player is
//     lining up a landing, and the moment a Fresnel is at its weakest -- the top face wears a
//     complete outline. The old `up_face_floor` patch existed only to paper over the Fresnel's
//     failure here and has been deleted rather than kept alongside a term that does the job.
//   * It is VIEW-INDEPENDENT, so the edge does not swim as the player walks around the platform.
//     A rim that moves is a rim that cannot be aimed at.
//   * Its width is a real, tunable number (`edge_width`) rather than an exponent whose apparent
//     thickness depends on the viewing angle.
//
// REQUIREMENT THIS PLACES ON GEOMETRY, AND IT IS THE ONE THING THAT CAN GO WRONG: the mesh needs
// per-face UVs in the 0..1 range. `BoxMesh` and `CSGBox3D` give this by default and so does any
// normally-unwrapped authored mesh, but a mesh whose UVs are atlassed, tiled, or scaled will get a
// band in the wrong place. A weak Fresnel is retained (`fresnel_mix`) as a floor so an unwrapped or
// organic mesh still reads as *something* rather than going dark -- it is a safety net, not the
// instrument.
//
// -------------------------------------------------------------------------------------------------
// THE DARKNESS INPUT IS A GLOBAL, WRITTEN ONCE PER FRAME, AND THIS SHADER NEVER COMPUTES IT
// -------------------------------------------------------------------------------------------------
// `bt_darkness` is a `[shader_globals]` float, 0 at noon -> 1 at deep night, published by
// `NightAidDriver.cs` from `OutdoorAtmosphere.Evaluate(phase, cyclesElapsed).NightFactor` -- the
// SAME curve the sky reads, not a second one. Deriving darkness twice is how two systems come to
// disagree about what time it is, and this repo already has a standing rule that `OutdoorAtmosphere`
// is the sole writer of the sky. The driver reads it and republishes; it never writes back.
//
// CONSEQUENCE FOR ANYONE DEBUGGING THIS: `NightAidDriver` republishes every frame, so a
// `--set bt_darkness=...` on the command line, or a value poked in the inspector, is overwritten
// silently on the next frame and looks exactly like the uniform being ignored. To hold a value,
// stop the driver (`SetProcess(false)`) or use `darkness_override` below.
global uniform float bt_darkness;

// Test/capture escape hatch: >= 0 wins over the global. This exists so a headed capture can
// photograph the day and night states of this material without waiting out a cycle, and it is the
// ONLY supported way to pin the value while the driver is alive (see the note above).
uniform float darkness_override : hint_range(-1.0, 1.0, 0.005) = -1.0;

// -------------------------------------------------------------------------------------------------
// The body. This is a normal matte surface by day -- the section palette's own colour -- and the
// rim is added on top of it. One material, two readings, so geometry packets assign ONE material
// to a rim mesh rather than swapping materials at dusk.
// -------------------------------------------------------------------------------------------------
uniform vec3 albedo : source_color = vec3(0.55, 0.55, 0.56);
uniform float roughness_v : hint_range(0.0, 1.0, 0.005) = 0.85;

// The edge colour. Set per section by the three shipped materials (blue rims + beam, red goal
// blocks, cyan obstacles). Kept separate from `albedo` because the daylight body wants to be
// neutral-ish while the night edge wants to be the section's saturated marker -- the same
// "only the goal is vivid" rule the palette follows, applied across time instead of across space.
uniform vec3 edge_color : source_color = vec3(0.119, 0.302, 0.850);

// Peak emission at full darkness. 1.1 is a value pick, arrived at from the headed capture rather
// than from arithmetic: bright enough to hold up against the shipped night ambient floor (0.05),
// dim enough that a cluster of rims does not bloom into one shape. Measured against a same-albedo
// neutral control (`neutral_gray`, whose colour IS this shader's daylight body): at night the edge
// bands read 2.8x-3.9x the control's rim, and in daylight they sit within 0.06 of it.
uniform float edge_energy : hint_range(0.0, 6.0, 0.01) = 1.1;

// THE BAND WIDTH, as a fraction of each face, measured in from its border. This is the real edge
// control and it is a distance rather than an exponent, so "this far in from the rim" means the
// same thing from every angle. 0.055 on a 2 m platform face is an ~11 cm outline.
//
// TUNED DOWN FROM 0.10 BY THE CAPTURE, and the direction of the error is worth knowing: the first
// geometric version at 0.10 read as a glowing FRAME rather than an edge, because the band is a
// fraction of the FACE and a box's short faces are therefore banded proportionally thicker in world
// terms than its long ones. If a geometry packet finds the outline too heavy on a thin rim mesh,
// this is the number to drop -- not `edge_energy`, which would dim the aid instead of thinning it.
uniform float edge_width : hint_range(0.005, 0.5, 0.005) = 0.055;

// How hard the band's inner boundary is. 0 is a hard line (aliases badly at distance); the default
// softens it over a third of the band, which is what keeps it from shimmering as a platform gets
// small on screen.
uniform float edge_softness : hint_range(0.0, 1.0, 0.005) = 0.35;

// The Fresnel SAFETY NET, not the instrument -- see the header. Only reaches this fraction of full
// strength, so a properly-UV'd box is drawn by the geometric band and an oddly-unwrapped or organic
// mesh still shows a silhouette instead of vanishing. Set to 0 to make the edge purely geometric.
uniform float fresnel_mix : hint_range(0.0, 1.0, 0.005) = 0.15;
uniform float edge_power : hint_range(0.5, 12.0, 0.05) = 3.4;

// Darkness below which nothing happens at all. Without this the rims begin glowing in the middle
// of a bright afternoon at 5% strength, which reads as a rendering bug rather than as an aid --
// and D10 says the aids belong to the dark. The dusk band starts at phase 0.55, and NightFactor is
// 0.05 there, so 0.12 keeps the whole golden sweep clean and lights the rims through the purple.
uniform float darkness_onset : hint_range(0.0, 1.0, 0.005) = 0.12;

void fragment() {
	float dark = darkness_override >= 0.0 ? darkness_override : bt_darkness;
	// Remap so the aid is genuinely OFF above the onset, then rises across the remaining range.
	// smoothstep rather than a linear ramp so the rims arrive with the dusk rather than snapping.
	float aid = smoothstep(darkness_onset, 1.0, clamp(dark, 0.0, 1.0));

	// THE GEOMETRIC BAND -- the instrument. Distance to the nearest face border in UV space, so
	// every face gets a complete outline whatever direction it points, including straight at the
	// player. `min` over both axes and both sides gives the distance to the closest of the four
	// borders; smoothstep turns that into a band of width `edge_width` with a softened inner lip.
	vec2 d2 = min(UV, vec2(1.0) - UV);
	float border_dist = min(d2.x, d2.y);
	float inner = edge_width * (1.0 - edge_softness);
	float band = 1.0 - smoothstep(inner, max(edge_width, inner + 0.0001), border_dist);

	// THE FRESNEL SAFETY NET -- see the header. Capped by `fresnel_mix` so it can never become the
	// dominant term on a box, where it is constant per face and therefore lights whole faces.
	float ndv = clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0);
	float rim = pow(1.0 - ndv, edge_power) * fresnel_mix;

	float edge = clamp(max(band, rim), 0.0, 1.0);

	ALBEDO = albedo;
	ROUGHNESS = roughness_v;
	METALLIC = 0.0;
	EMISSION = edge_color * edge * aid * edge_energy;
}
