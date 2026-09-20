shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// =================================================================================================
// CONTACT SMUDGES under the title screen's blocks. (MENU-1, 2026-08-28.)
// =================================================================================================
//
// Not shadows in the renderer's sense — `OutdoorAtmosphere.SunShadowEnabled` is FALSE by standing
// ruling (Talon, 2026-08-07: shadows are simulated with techniques, never real shadow maps) and
// this screen has no sun to cast one anyway. These are the soft dark ellipses the approved frame
// paints under each block, which are what stop the blocks reading as floating stickers once the
// 30 px blur has removed every other cue that they are standing on something.
//
// One quad per block, laid flat, with a radial falloff computed from UV. Blended in the render
// target's space like everything else on this screen; `depth_draw_never` so a smudge can never
// occlude the block above it.

// Alpha at the centre of the smudge. Set per instance because the approved frame fades them with
// distance (52/255 at the camera, decaying at exp(-0.024 d)).
instance uniform float peak_alpha = 0.2;

uniform vec3 smudge_srgb = vec3(0.275, 0.337, 0.408);

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + vec3(0.055)) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void fragment() {
	// Distance from the quad's centre, 0 at the middle and 1 at the edge midpoints.
	float d = length(UV - vec2(0.5)) * 2.0;
	// Squared smoothstep: no rim, and no visible circle after the world blur has had it.
	float f = 1.0 - smoothstep(0.0, 1.0, d);
	ALBEDO = srgb_to_linear(smudge_srgb);
	ALPHA = clamp(peak_alpha * f * f, 0.0, 1.0);
}
