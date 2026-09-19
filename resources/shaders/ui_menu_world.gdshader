shader_type spatial;
render_mode unshaded, cull_back, shadows_disabled, fog_disabled, depth_draw_opaque;

// =================================================================================================
// THE TITLE SCREEN'S WORLD — ground and colour blocks, shaded and hazed in sRGB. (MENU-1.)
// =================================================================================================
//
// Talon, 2026-08-28: "more light and bubble and blocks but I still want that came blue depth
// effect." That blue depth effect is this shader's `haze` term: everything reads bluer the
// further away it is, which is the one mechanism carried over from the campfire backdrop this
// screen replaces. It moved from a dark key to a light one; the mechanism did not change.
//
// WHY UNSHADED, AND WHY THE MATHS IS HERE RATHER THAN IN THE ENVIRONMENT.
//
// Three faces, three fixed values — top 1.00, front 0.84, side 0.68 — is a POSTER's shading, not
// a renderer's, and it is what the approved frame is built from. Reproducing it with a real
// directional light means solving for a light direction that happens to land those three ratios,
// and then hoping the tonemapper leaves them there. Stating them is both shorter and exact.
//
// More importantly the whole frame is composited in sRGB byte space: the blur, the bubbles and the
// scrim are canvas items, and Godot's 2D pipeline blends in the render target's own space. A
// world layer shaded and hazed in LINEAR would not sit in the same space as the three layers over
// it — the haze would eat the blocks faster than it eats the bubbles and the depth cue would come
// apart. So the shade and the haze are applied to sRGB values here, and the single conversion to
// linear happens on the last line, immediately before the engine tonemaps and re-encodes. With
// the environment on `Linear` tonemapping at exposure 1 that round trip is the identity, so what
// this shader computes is exactly what lands in the viewport texture.
//
// FOG IS DISABLED ON PURPOSE. `fog_disabled` in the render mode above: the engine's depth fog is a
// linear-space operation and would be a second, disagreeing haze on top of this one.

// The surface's own colour, in sRGB 0..1. An instance uniform, so 56 blocks share ONE material
// and one shader compile while each keeps its own colour. Deliberately NOT hinted `source_color`:
// that hint converts the value to linear on upload, and every mix below happens in sRGB.
instance uniform vec4 base_srgb = vec4(1.0);

// Where distance takes everything. sRGB 0..1, NOT `source_color` — the value must arrive
// un-converted, because every mix in this shader happens before the transfer function.
uniform vec3 haze_srgb = vec3(0.408, 0.569, 0.745);

// A second, nearer target the surface fades toward before the haze takes it. The ground uses it
// (near sand becoming far blue is its own gradient, separate from the atmosphere); the blocks
// leave it equal to the haze so the term collapses.
uniform vec3 far_srgb = vec3(0.408, 0.569, 0.745);

// How much of the residual haze is applied AFTER the far mix. 0 for the blocks, 0.25 for the
// ground — the ground's own blue is already most of the way there and a full second pass would
// flatten it out.
uniform float haze_extra : hint_range(0.0, 1.0, 0.01) = 0.0;

// Beer-Lambert density and the curve on top of it. 0.026 / 0.78 are the approved frame's.
uniform float density : hint_range(0.0, 0.2, 0.0005) = 0.026;
uniform float haze_curve : hint_range(0.1, 2.0, 0.01) = 0.78;

// The light key, as one multiplier on the lit value. 1.07 on the blocks; 1.0 on the ground.
uniform float key : hint_range(0.5, 2.0, 0.01) = 1.0;

// The three face values. Kept as uniforms rather than constants so the ground can flatten them.
uniform float shade_top : hint_range(0.0, 2.0, 0.01) = 1.00;
uniform float shade_front : hint_range(0.0, 2.0, 0.01) = 0.84;
uniform float shade_side : hint_range(0.0, 2.0, 0.01) = 0.68;

varying float v_depth;
varying vec3 v_normal;

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + vec3(0.055)) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void vertex() {
	// View-space forward distance, which is the quantity the approved frame hazes on — not the
	// radial distance. For a ground plane the two differ by the camera height and the difference
	// is a fraction of a percent past the first few metres.
	v_depth = -(MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).z;
	v_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 n = normalize(v_normal);

	// Which of the three poster faces this is. Axis-aligned boxes, so a plain threshold is exact
	// and there is no third case to get wrong.
	float shade = shade_side;
	shade = mix(shade, shade_front, step(0.5, n.z));
	shade = mix(shade, shade_top, step(0.5, n.y));

	vec3 col = clamp(base_srgb.rgb * shade * key, 0.0, 1.0);

	float t = pow(1.0 - exp(-density * max(v_depth, 0.0)), haze_curve);
	col = mix(col, far_srgb, t);
	col = mix(col, haze_srgb, t * haze_extra);

	// The one and only transfer-function conversion in the world layer. See the header.
	ALBEDO = srgb_to_linear(clamp(col, 0.0, 1.0));
}
