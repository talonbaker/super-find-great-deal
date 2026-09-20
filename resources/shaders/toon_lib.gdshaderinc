// Shared primitives for the toon world family — toon_world.gdshader and toon_decal.gdshader.
//
// WHY A SHARED FILE AND NOT TWO COPIES. The decal sits a few millimetres above the surface it
// decorates and is lit by the same sun. If the two shaders quantise light differently — even by
// a rounding difference in the band edges — the decal reads as a sticker of a slightly different
// material rather than as debris lying on the rock. The band function is therefore ONE function,
// not two that agree today.
//
// PROVENANCE. hash13/vnoise3 below are the standard Dave Hoskins "Hash without Sine" family, the
// same lineage resources/shaders/fabric_lib.gdshaderinc records for its 2D siblings. They are
// re-derived here rather than included from fabric_lib because that file's every primitive is
// wrapped in its band_limit() screen-space fade, which is correct for a 0.5 mm weave on a moving
// child and wrong for a 30 cm mottle on a static rock.
//
// SCOPE. Nothing in this file reads a texture, a screen buffer, or a second UV set. That is not
// austerity for its own sake: a backbuffer copy is a full-screen resolve per material and a
// second material is a draw call per instance, and docs/ENVIRONMENT-ASSET-CONTRACT.md §8 puts
// draw calls, not triangles, at the top of the cost list.


// --- hash and noise -----------------------------------------------------------------------

// One 32-bit-ish hash of a 3D point, no trig, no texture fetch. Returns 0..1.
float toon_hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

// Trilinear value noise in whatever space the caller hands it. Eight hashes per sample, one
// octave — deliberately ONE. The job here is to stop a flat 0..1 mask reading as a mechanical
// gradient, and a second octave costs eight more hashes to add detail that is below a pixel at
// the 3-5 m camera distance the world is actually judged from (STYLE breakdown §3).
float toon_vnoise3(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	// Quintic smoothstep: C2-continuous, so a slow camera move across the mottle does not show
	// the lattice as faint creases the way the cubic one does.
	f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

	float n000 = toon_hash13(i + vec3(0.0, 0.0, 0.0));
	float n100 = toon_hash13(i + vec3(1.0, 0.0, 0.0));
	float n010 = toon_hash13(i + vec3(0.0, 1.0, 0.0));
	float n110 = toon_hash13(i + vec3(1.0, 1.0, 0.0));
	float n001 = toon_hash13(i + vec3(0.0, 0.0, 1.0));
	float n101 = toon_hash13(i + vec3(1.0, 0.0, 1.0));
	float n011 = toon_hash13(i + vec3(0.0, 1.0, 1.0));
	float n111 = toon_hash13(i + vec3(1.0, 1.0, 1.0));

	return mix(
		mix(mix(n000, n100, f.x), mix(n010, n110, f.x), f.y),
		mix(mix(n001, n101, f.x), mix(n011, n111, f.x), f.y),
		f.z);
}

// A mask broken up by noise, without ever moving the two ENDS of the mask.
//
// This is the whole trick that makes a flat authored channel stop looking authored. A naive
// `mask + noise` pushes a 0 mask to 0.3 and a 1 mask to 0.7 — so a face the artist marked
// "clean" grows moss and a face marked "fully weathered" stops being fully weathered, and the
// artist's channel no longer means what it says. Remapping the mask's own contrast around the
// noise instead leaves 0 at 0 and 1 at 1 and scrambles only the middle, which is where a real
// weathering boundary is ragged anyway.
//
// amount 0 returns the mask untouched; amount 1 is a hard, fully noise-driven boundary.
float toon_break_up(float mask, float noise, float amount) {
	float lo = mix(0.0, noise, clamp(amount, 0.0, 1.0));
	float hi = mix(1.0, noise + 0.001, clamp(amount, 0.0, 1.0));
	return clamp(smoothstep(lo, max(hi, lo + 0.0001), mask), 0.0, 1.0);
}


// --- the band function ----------------------------------------------------------------------

// Quantise a 0..1 light term into `levels` flat tones with a soft terminator.
//
// levels = 3 gives the three tones a cel painting is built from: shadow (0.0), mid (0.5) and
// light (1.0). Two gives a hard cut-out. Anything past about six is a gradient wearing a
// costume — the steps fall below one 8-bit sRGB step apart and the whole point of the technique
// is gone.
//
// The terminator sits at the MIDDLE of each cell, i.e. at (k + 0.5) / levels, so the returned
// value never exceeds the range of the input and a fully lit surface is exactly 1.0.
// `softness` is measured in fractions of a cell: 0 is a knife edge, 0.5 blends across the whole
// cell and the bands dissolve back into a ramp.
float toon_band(float x, float levels, float softness) {
	float n = max(levels, 2.0);
	float top = n - 1.0;
	float s = clamp(x, 0.0, 1.0) * n;
	float k = min(floor(s), top);
	float t = s - k;
	float w = max(softness, 0.0005);
	float e = smoothstep(0.5 - w, 0.5 + w, t);
	return clamp(mix(k, k + 1.0, e), 0.0, top) / top;
}


// --- shared colour helpers --------------------------------------------------------------------

// Rec.709 luma of a LINEAR colour. Everything in this family works in linear: COLOR_0 arrives
// linear from glTF (contract §6.1) and source_color uniforms are decoded on upload, so a
// saturation push here and a saturation push in ground_wear.gdshader mean the same thing.
float toon_luma(vec3 c) {
	return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

// Push saturation around the colour's own luma. k < 1 drains, k > 1 lifts; luma is preserved
// exactly at every k, so this can never make a surface brighter or darker — only more or less
// colourful. That property is what keeps this family out of THRILL-BIBLE §6.2's night-floor
// fork, the same way ground_wear.gdshader's night_desaturate stays out of it.
vec3 toon_saturate(vec3 c, float k) {
	return max(mix(vec3(toon_luma(c)), c, k), vec3(0.0));
}


// --- depth haze ---------------------------------------------------------------------------

// Brief §5's "subtle fog/fade that increases with distance". A LINEAR fade on view distance,
// not an exponential one, and deliberately not Environment fog:
//
//   * Environment fog belongs to OutdoorAtmosphere.cs, which is the single writer of sky, sun,
//     moon and ambient (.claude/rules/single-writer.md). A shader that wrote fog would be the
//     second writer of an atmosphere the whole repo has exactly one of.
//   * Brief §2 judges every asset under flat noon with NO fog. A haze living in the material
//     can be set to zero for validation and non-zero for play without touching the clock.
//   * At strength 0 the compiler folds the whole term away, so the off case costs nothing.
//
// Returns 0 at/inside `near`, `strength` at/beyond `far`.
float toon_haze(float view_dist, float near_m, float far_m, float strength) {
	if (strength <= 0.0) {
		return 0.0;
	}
	float t = clamp((view_dist - near_m) / max(far_m - near_m, 0.001), 0.0, 1.0);
	return t * strength;
}
