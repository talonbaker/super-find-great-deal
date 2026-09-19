shader_type spatial;
render_mode cull_front, unshaded, shadows_disabled, depth_draw_opaque, world_vertex_coords;

// =================================================================================================
// HOVER OUTLINE — the interaction lab's "you can pick this up" cue.
// =================================================================================================
//
// GENERAL-PURPOSE, PROJECT-NEUTRAL. This is a targeting cue, not a style. It says one thing —
// *this object is the one your interact key will act on* — and it has to say it from a loose
// over-the-shoulder camera that shows most of the character, which is farther out than a tight
// action-game shoulder-cam.
//
// -------------------------------------------------------------------------------------------------
// IT IS DRAWN ON A SEPARATE MESH, NOT HUNG OFF THE PROP'S MATERIAL, AND THAT IS THE POINT
// -------------------------------------------------------------------------------------------------
// The repo's other inverted hull (`atmospheric/outline_hull.gdshader`) is a `next_pass` on a lit
// base material. That is right for a character whose material you own. It is WRONG here, because
// this lab's contract is *item-agnostic*: any prop with an `Interactable` component gets the cue,
// including props whose materials came from somewhere else and must not be touched.
//
// So `Interactable.cs` builds a HIDDEN CHILD MeshInstance3D sharing the prop's own `Mesh`, carrying
// this shader as a `material_override`, and shows it only while that prop is the hover target. The
// prop's own material is never read, never duplicated and never modified.
//
// An inverted hull is order-independent for opaque geometry, which is what makes the separate-mesh
// form safe: with `cull_front` the surviving faces are the hull's BACK faces, which are always
// deeper than the prop's own front surface. Hull-first and prop-first both resolve to the same ring.
//
// COST — stated with its axis, per CLAUDE.md §0.3:
//   * ONE EXTRA DRAW CALL, and only while something is hovered. There is at most one hover target,
//     so the steady-state cost of this entire mechanism is zero draws and zero fragments. That is
//     the reason the highlight is a separate hidden mesh rather than a permanent second pass on
//     every interactable in the level.
//   * Per drawn fragment: `unshaded`, no texture fetch, ~10 ALU. Pure ALU, so it degrades toward a
//     low-end floor on the HARDER of the two axes (handoff §4) — but against one small silhouette
//     ring of one object, which is a rounding error at any resolution.
//   * Vertex: ~20 ALU per vertex of the hovered mesh. Vertex counts on props are orders of
//     magnitude below pixel counts; this is the cheap stage to spend in.
//   NOT MEASURED ON A FLOOR GPU. Nothing in this repo has been.
//
// -------------------------------------------------------------------------------------------------
// WHY THE WIDTH IS IN PIXELS, AND WHY THAT MATTERS MORE HERE THAN ANYWHERE ELSE IN THE REPO
// -------------------------------------------------------------------------------------------------
// A hull inflated by a fixed number of METRES is a fat border in close-up and literally invisible
// across a room. For a decorative outline that is a look problem. For a TARGETING CUE it is a
// functional failure: the one thing the player must be able to read at the exact distance this
// camera sits at is the thing that has faded to sub-pixel.
//
// The width is therefore converted from pixels to world units using the actual projection. For a
// perspective matrix, one world unit at view depth d covers |P[1][1]| * VIEWPORT_SIZE.y / (2d)
// pixels, so a pixel is 2d / (|P[1][1]| * height) world units. Derived, so it is correct at any
// FOV, any resolution and any distance rather than correct in the one shot it was tuned in.
//
// THE abs() ON P[1][1] IS LOAD-BEARING. Under Vulkan the projection matrix carries the clip-space
// Y flip and P[1][1] is NEGATIVE (about -2.41 at a 66 degree FOV). Without the abs every width in
// this file comes out negative, the hull inflates INWARD, hides entirely inside the prop, and the
// cue silently stops existing — no error, no magenta, no compile failure, and a symptom identical
// to "the outline is too thin". This repo has paid for that sign convention once already.
//
// -------------------------------------------------------------------------------------------------
// OCCLUSION IS NOT A LIMITATION HERE, IT IS THE SAME RULE DRAWN TWICE
// -------------------------------------------------------------------------------------------------
// This hull respects depth, so a prop behind a wall shows no outline. That is deliberate and it
// MATCHES THE TARGETING RULE: `Interactor.cs` requires line of sight before a prop can become the
// hover target, so an object you cannot see is an object you cannot grab. An x-ray variant would
// draw a cue for a target the interactor would refuse — a highlight that lies about what the
// button will do is worse than no highlight.
//
// (An x-ray variant is also not a uniform away. `depth_test_disabled` is a compile-time
// `render_mode`, so it would have to be a second file — the same forced split as
// `vision/occluder_fade*.gdshader`. If one is ever wanted, split it that way and change the
// targeting rule with it, not instead of it.)
//
// THIS SHADER MUST NEVER WRITE `ALPHA`. A spatial shader that assigns ALPHA at all — even
// `ALPHA = 1.0`, even in a branch that never runs — is classified TRANSPARENT by Godot, which moves
// it behind the backbuffer copy a full-screen post pass reads and destroys the depth behaviour the
// technique depends on. Opacity here is expressed only through `discard`. See
// `vision/occluder_fade.gdshader` for the session that cost.

// -------------------------------------------------------------------------------------------------
// The hover strength is an `instance uniform`: ONE material shared by every interactable's hull,
// with a per-object float written into the instance record by Interactable.cs. No material
// duplication, no per-prop uniform buffer, no extra draw calls from material breaks.
//
// WRITE THE ZERO EXPLICITLY WHEN THE HULL IS BUILT. An instance uniform that has never been written
// does not reliably read as its declared default — it reads whatever is in that object's slot. The
// room already records this trap the expensive way; Interactable.BuildHull does the same thing.
// 0 = no cue, 1 = full cue. Everything below scales off it, so a fade in and out is one float.
instance uniform float hover_amt = 0.0;

group_uniforms Line;
// Drawn width in SCREEN PIXELS at `lock_distance`. Read at the loose camera distance this lab is
// built around, 3-5 px is a confident cue and 2 px is already marginal on a small prop. Past about
// 8 it stops being a contour and starts being a border that hides the object's own silhouette.
uniform float hover_px : hint_range(0.0, 12.0, 0.05) = 4.0;
// The cue colour. Deliberately NOT a saturated primary: this lab is mood-neutral and a cue that is
// "the yellow one" bakes a palette decision into a system that is supposed to survive being dropped
// into any project. Recolour it per project; the default is a bright neutral warm that separates
// from both the grey room and the coloured reference patches without claiming a genre.
uniform vec3 hover_color : source_color = vec3(0.98, 0.90, 0.66);
// Emissive gain. Above 1.0 this pushes past white and will bloom IF the consuming scene runs glow.
// It defaults to 1.0 — flat, no glow dependency — because a cue that only reads with bloom enabled
// is a cue that silently weakens on the low-end settings this repo exists to serve.
uniform float hover_gain : hint_range(0.0, 4.0, 0.01) = 1.0;

// 1.0 = the line holds the same pixel width at every distance. 0.0 = the line is a fixed world size
// and thins with distance like real geometry.
//
// DEFAULT 1.0, AND FOR THIS SHADER THAT IS A FUNCTIONAL DEFAULT RATHER THAN A TASTE ONE. See the
// header: a targeting cue that recedes has failed at the distance it is most needed. The dial is
// exposed anyway because a project that wants the cue to feel like part of the world rather than
// part of the HUD will want to pull it down, and it should be able to see what that costs.
uniform float perspective_lock : hint_range(0.0, 1.0, 0.01) = 1.0;
// The depth at which both ends of `perspective_lock` agree, in metres. Defaulted to roughly where a
// held or hovered prop sits under this lab's camera, so moving the lock reads as a change in
// BEHAVIOUR WITH DISTANCE rather than as a jump in width at the distance you are looking from.
uniform float lock_distance : hint_range(0.2, 20.0, 0.05) = 3.0;

group_uniforms Pulse;
// A slow brightness breath, so a hovered prop is distinguishable from a prop that merely has a
// bright edge in the lighting. Frequency in Hz.
//
// KEEP THIS SLOW. Above ~3 Hz it stops reading as "selected" and starts reading as "warning", which
// is a genre statement this lab is not allowed to make; above ~8 Hz it is an accessibility problem
// outright. The range is capped at 6 for the first reason and the default sits well under it.
uniform float pulse_hz : hint_range(0.0, 6.0, 0.05) = 1.1;
// Depth of the breath, as a fraction of the line brightness. 0 disables it and costs nothing extra
// either way — the term is 3 ALU whether it does anything or not, so there is no cheap/expensive
// fork hiding in this dial.
uniform float pulse_amt : hint_range(0.0, 1.0, 0.01) = 0.22;
// A matching width breath. Separate from the brightness one because they read differently: a width
// pulse survives being seen in peripheral vision and a brightness pulse does not, while a width
// pulse on a busy silhouette is the one that looks like a rendering fault. Default is off.
uniform float pulse_width_amt : hint_range(0.0, 1.0, 0.01) = 0.0;

// Below 0 the pulse is live. At or above 0 it FREEZES at that second.
//
// This is the same `time_scrub` contract the water shader and the post stack carry, and it exists
// for the same reason: two captures of an animated effect differ by the animation, so an A/B of a
// width or colour change is noise-limited until the clock stops. Two captures at a frozen scrub are
// bit-identical.
uniform float time_scrub : hint_range(-1.0, 30.0, 0.01) = -1.0;

group_uniforms Debug;
// Removes the hull and nothing else. This is the A/B: one frame of a hovered prop with a cue, one
// frame of the same prop, in the same pose, without one.
uniform bool debug_no_hover = false;

void vertex() {
	float t = time_scrub >= 0.0 ? time_scrub : TIME;
	float breath = sin(t * pulse_hz * 6.2831853);

	// `world_vertex_coords`: VERTEX and NORMAL arrive already in world space, so the offset is added
	// straight to VERTEX and stays perpendicular to the surface under any model transform. The
	// alternative — inflating in view space and writing POSITION — is a second sign convention to
	// get wrong for no gain, and a pass that writes POSITION cannot be used as a next_pass at all.
	vec3 n_world = normalize(NORMAL);
	float depth = max(-(VIEW_MATRIX * vec4(VERTEX, 1.0)).z, 0.001);

	// Pixels -> world units, at this vertex's depth and at the lock distance. See the header for the
	// derivation and for why the abs() is the difference between a cue and no cue at all.
	float px_scale = 2.0 / (abs(PROJECTION_MATRIX[1][1]) * VIEWPORT_SIZE.y);
	float world_per_px = mix(lock_distance, depth, perspective_lock) * px_scale;

	float amt = debug_no_hover ? 0.0 : clamp(hover_amt, 0.0, 1.0);
	float w_px = hover_px * (1.0 + breath * pulse_width_amt);

	// The fade in and out is a WIDTH ramp, not an opacity ramp, and that is the whole reason this
	// stays an opaque shader. Fading a cue by writing ALPHA would move the material into the
	// transparent queue for the entire lifetime of the lab (see the header), to animate 0.12 s of
	// appearance. Growing the line from nothing reads at least as well and costs nothing.
	VERTEX += n_world * (w_px * amt * world_per_px);
}

void fragment() {
	// Discard rather than relying on a zero-width hull. At width 0 the hull is exactly coincident
	// with the surface it inflates, and coincident back faces z-fight in a speckle pattern that
	// looks like a BROKEN outline rather than like no outline — which is a bug report, not a
	// hidden cue. 0.002 is comfortably below the smallest visible width and above float noise.
	float amt = debug_no_hover ? 0.0 : clamp(hover_amt, 0.0, 1.0);
	if (amt < 0.002) {
		discard;
	}

	float t = time_scrub >= 0.0 ? time_scrub : TIME;
	float breath = sin(t * pulse_hz * 6.2831853) * 0.5 + 0.5;
	float bright = hover_gain * mix(1.0 - pulse_amt, 1.0, breath);

	// ALBEDO carries it rather than EMISSION so the cue is a flat drawn line at gain 1.0 under any
	// tonemap, with EMISSION available for the case where a project DOES want it to bloom. Both are
	// written from one term so the two can never disagree about how bright the cue is.
	ALBEDO = hover_color * bright * amt;
	EMISSION = hover_color * max(bright - 1.0, 0.0) * amt;
}
