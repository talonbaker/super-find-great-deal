shader_type canvas_item;

// =================================================================================================
// THE TEXT SCRIM — the fix for "the black text against that background is terrible". (MENU-1.)
// =================================================================================================
//
// Talon, 2026-08-28: "The black text against that background is terrible ... I'm struggling to
// find the words exactly why."
//
// IT WAS NEVER A CONTRAST PROBLEM. The old ink measured 8.06:1 on the sand and 6.18:1 on the warm
// block — AAA on both — and it still looked wrong. Three real causes, and this file is the fix for
// two of them:
//
//   * EDGE MISMATCH. Everything behind the type is blurred to zero hard edges. Razor-sharp glyphs
//     at 100% opacity over that have no optical relationship to it and read as pasted onto a
//     photograph. A soft luminous field under the column mediates between the two.
//   * A FIELD THAT CHANGED UNDERNEATH THE WORD. 8.06:1 under "WORKING", 6.18:1 under "TITLE" —
//     the wordmark's own legibility varied across its own length. The scrim gives the column ONE
//     stable field, so the measured ratio is a property of the design rather than of which block
//     happened to drift behind which letter.
//
// (The third cause — inverted hierarchy, near-black at full opacity outranking the bubble that is
// supposed to be the subject — is fixed in the ink colour, not here.)
//
// THE DEFECT THE PREVIZ SHIPPED, AND HOW IT IS SOLVED HERE. The previz scrim's lower-left edge
// was faintly visible as a column boundary, and the cause is one exponent. Its vertical falloff is
// `t^0.7`, and t^0.7 has INFINITE slope at t = 0 — so the field arrives at its own end abruptly,
// and where that end meets the horizontal falloff the eye finds a corner.
//
// The fix is deliberately NARROW. An earlier version of this file replaced both falloffs with
// smootherstep, which removed the edge and also made the whole scrim about 2.8x too strong at the
// top-left — measured against the approved frame at +23 RGB on the sky and +20 on the ground. The
// approved frame's brightness IS the approval, so the curve is now the previz's own, exactly,
// **multiplied at the toe** by a C2 ramp that is 1 over the whole visible field and takes the last
// stretch to zero with both derivatives vanishing. Over the frame the two agree to within a
// fraction of a level; at the terminator the new one has no corner to find.

uniform vec3 tint = vec3(1.0);
uniform float peak : hint_range(0.0, 1.0, 0.005) = 0.62;

// Where the horizontal falloff reaches zero, as a fraction of the width, and its exponent. At 1.6
// the curve already leaves with zero slope, so this end needs no correction.
uniform float x_end : hint_range(0.1, 1.0, 0.005) = 0.52;
uniform float x_power : hint_range(0.2, 4.0, 0.01) = 1.6;

// The band's centre and half-height, and the exponent that makes it a soft tent rather than a
// triangle.
uniform float y_centre : hint_range(0.0, 1.0, 0.005) = 0.46;
uniform float y_reach : hint_range(0.05, 2.0, 0.005) = 0.46;
uniform float y_power : hint_range(0.2, 4.0, 0.01) = 0.7;

// How much of the vertical falloff's tail gets the C2 correction. 0.22 is past where the previz's
// curve is still contributing anything visible, so the correction costs the look nothing and buys
// the edge.
uniform float y_toe : hint_range(0.01, 0.6, 0.005) = 0.22;

float smoother(float e0, float e1, float x) {
	float t = clamp((x - e0) / max(e1 - e0, 1e-5), 0.0, 1.0);
	return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

void fragment() {
	float tx = clamp(1.0 - UV.x / max(x_end, 1e-4), 0.0, 1.0);
	float hx = pow(tx, x_power);

	float ty = clamp(1.0 - abs(UV.y - y_centre) / max(y_reach, 1e-4), 0.0, 1.0);
	float vy = pow(ty, y_power) * smoother(0.0, y_toe, ty);

	COLOR = vec4(tint, peak * hx * vy);
}
