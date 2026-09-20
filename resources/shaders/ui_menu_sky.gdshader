shader_type sky;

// =================================================================================================
// THE TITLE SCREEN'S SKY — one vertical ramp, in screen space. (MENU-1, 2026-08-28.)
// =================================================================================================
//
// A straight top-to-bottom gradient across the frame, exactly as the approved previz builds it.
// It is deliberately SCREEN-space rather than world-space, for two reasons:
//
//   1. It is what the approved frame is. The previz ramps from `SKY_TOP` at row 0 to `SKY_HORIZ`
//      at the last row and then paints the ground over the bottom half, so the colour visible at
//      the horizon is the ramp's MIDPOINT, not its end. Ramping on elevation angle instead would
//      put the wrong colour at the skyline and there is no FOV that fixes it.
//   2. It cannot break. An elevation-angle ramp has to agree with the camera's field of view and
//      its pitch, and the repo has been bitten before by a hand-derived camera term that only a
//      render can falsify. `SCREEN_UV` has no such coupling.
//
// The camera drifts by a few centimetres, so a fixed sky is also physically defensible here: at
// that amplitude a real sky would not measurably shift either.
//
// COLOURS ARRIVE IN sRGB AND ARE CONVERTED ON THE LAST LINE, for the same reason
// `ui_menu_world.gdshader` does it: the whole frame is authored in sRGB and the environment
// tonemaps `Linear` at exposure 1, so the round trip is the identity.

uniform vec3 top_srgb = vec3(0.408, 0.588, 0.769);
uniform vec3 horizon_srgb = vec3(0.886, 0.839, 0.769);

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + vec3(0.055)) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void sky() {
	COLOR = srgb_to_linear(mix(top_srgb, horizon_srgb, clamp(SCREEN_UV.y, 0.0, 1.0)));
}
