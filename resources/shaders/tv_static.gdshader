shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_disabled, shadows_disabled;

// =================================================================================================
// TV STATIC — a dying broadcast on a dead channel. (BT-9, bubble-test level, 2026-08-27.)
// =================================================================================================
//
// PORTED, NOT INVENTED. The look and every timing number come from `TvPortal.cs` in the sibling
// repo `C:/repos/mp-foundation` (`scripts/game/world/TvPortal.cs:53-100, 160-170`), which BT-10
// ports into Sail wholesale. That version drove a StandardMaterial3D: a 128x128 value-noise
// NoiseTexture2D on both albedo and emission, with `_Process` wobbling EmissionEnergyMultiplier.
//
// WHY IT IS A SHADER HERE AND A SCRIPT THERE. The script version costs a per-frame C# `_Process`
// callback and a material write per TV, and its noise never actually MOVES -- a static texture
// pulsing in brightness reads as a lamp behind a curtain, not as static. Seething is the whole
// point of the effect and it is what a fragment shader gets for free, so the port moves the noise
// into the fragment stage and keeps the flicker numbers byte-for-byte. Result: zero script cost,
// no per-TV material instance, and the thing actually crawls.
//
// THE FLICKER NUMBERS ARE COPIED EXACTLY, and are not to be "tuned" without a reason:
//
//     basePulse = 1.05 + 0.25 * sin(t * 9.7) * sin(t * 2.3)
//     if (rand < 0.01) basePulse *= 0.4
//
// Two detuned sines rather than one is what stops it being a strobe: 9.7 and 2.3 Hz are
// deliberately not harmonics, so the product never repeats on a period a viewer can learn, and the
// slow term spends most of its time near zero -- the screen mostly sits still and occasionally
// surges. The 1% stutter to 0.4 is the "dying" half: a hard, rare, single-frame brownout.
//
// THE STUTTER IS THE ONE THING THAT COULD NOT BE COPIED LITERALLY. `_Process` had an RNG and a
// frame boundary; a fragment shader has neither -- it has TIME, and every pixel must agree or the
// screen tears into noise-coloured bands instead of dimming as one. So the stutter is a hashed
// function of the *frame-quantised* time (`floor(TIME * stutter_hz)`), which gives all pixels one
// shared value that changes at a fixed rate and is unpredictable across ticks. `stutter_hz` is
// therefore the effective frame rate of the brownout, not a display rate, and 24 is chosen so a
// 1% chance fires about once every four seconds -- the same felt frequency as the original at
// 60 fps (0.01 * 60 = 0.6/s was noticeably busier than "dying" wants; see `stutter_chance`).
//
// NO SCANLINE ROLL. The brief says "slight scanline". A scanline that SCROLLS is a CRT-rolling
// artefact and reads as a broken vertical hold -- a different, louder effect that would compete
// with the flicker for the same attention. These lines are fixed in UV space and static.

// -------------------------------------------------------------------------------------------------
// Colour. Both defaults are TvPortal.cs's exact values (albedo 0.75/0.78/0.80, emission
// 0.72/0.76/0.82) -- a cold blue-grey, because a warm static reads as firelight.
// -------------------------------------------------------------------------------------------------
uniform vec3 tint : source_color = vec3(0.75, 0.78, 0.80);
uniform vec3 emission_tint : source_color = vec3(0.72, 0.76, 0.82);

// TvPortal.cs's EmissionEnergyMultiplier baseline, before the pulse multiplies it.
uniform float emission_energy : hint_range(0.0, 4.0, 0.01) = 1.2;

// -------------------------------------------------------------------------------------------------
// The noise. `grain_scale` is cells across the screen quad, standing in for the original's
// 128x128 texture at FastNoiseLite frequency 0.9. Value noise, not gradient noise, because
// FastNoiseLite's Value type is what the original asked for and it is blockier -- which is what
// broadcast static looks like.
// -------------------------------------------------------------------------------------------------
uniform float grain_scale : hint_range(8.0, 512.0, 1.0) = 128.0;
uniform float grain_speed : hint_range(0.0, 60.0, 0.1) = 18.0;  // cells reshuffled per second
uniform float grain_contrast : hint_range(0.0, 1.0, 0.005) = 0.72;

// -------------------------------------------------------------------------------------------------
// Flicker — the ported numbers. Renamed to uniforms so they are visible and pinnable, but the
// defaults ARE TvPortal.cs and changing them is a look change, not a tuning nicety.
// -------------------------------------------------------------------------------------------------
uniform float pulse_base : hint_range(0.0, 2.0, 0.005) = 1.05;
uniform float pulse_amp : hint_range(0.0, 1.0, 0.005) = 0.25;
uniform float pulse_fast_hz : hint_range(0.1, 30.0, 0.1) = 9.7;
uniform float pulse_slow_hz : hint_range(0.1, 30.0, 0.1) = 2.3;
uniform float stutter_chance : hint_range(0.0, 0.2, 0.001) = 0.01;
uniform float stutter_depth : hint_range(0.0, 1.0, 0.005) = 0.4;
uniform float stutter_hz : hint_range(1.0, 120.0, 1.0) = 24.0;

// -------------------------------------------------------------------------------------------------
// Scanlines. Fixed, not rolling (see the header). `scanline_depth` is deliberately small -- at
// 0.12 they are a texture on the glass; past ~0.3 they become the subject.
// -------------------------------------------------------------------------------------------------
uniform float scanline_count : hint_range(0.0, 600.0, 1.0) = 180.0;
uniform float scanline_depth : hint_range(0.0, 0.6, 0.005) = 0.12;

// A CRT is brighter in the middle. Cheap, and it is most of what sells "screen" over "lit panel".
uniform float vignette : hint_range(0.0, 1.0, 0.005) = 0.35;

// Reproducible captures: >= 0 freezes the effect at that time. -1 runs live. Same idiom as the
// lab's `time_scrub`, and it is what makes a headed capture of this shader comparable run to run.
uniform float time_scrub : hint_range(-1.0, 120.0, 0.01) = -1.0;

float hash21(vec2 p) {
	// Integer-ish hash. Cheap, no texture fetch, and stable across drivers for the small
	// coordinates this shader feeds it.
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float hash11(float x) {
	return fract(sin(x * 127.1) * 43758.5453);
}

void fragment() {
	float t = time_scrub >= 0.0 ? time_scrub : TIME;

	// Value noise: one hash per cell, no interpolation. The `floor(t * grain_speed)` term is what
	// makes the field reshuffle in discrete ticks rather than sliding, which is the difference
	// between static and a moving cloud.
	vec2 cell = floor(UV * grain_scale);
	float n = hash21(cell + vec2(floor(t * grain_speed) * 1.7, 0.0));
	n = mix(0.5, n, grain_contrast);

	// Scanlines, fixed in UV. cos() rather than a step so they are a soft modulation.
	float lines = 1.0 - scanline_depth * (0.5 + 0.5 * cos(UV.y * scanline_count * 6.2831853));

	// Vignette from the quad's centre.
	float r = length(UV - vec2(0.5));
	float vig = 1.0 - vignette * smoothstep(0.25, 0.75, r);

	// --- The ported flicker, verbatim ---------------------------------------------------------
	float pulse = pulse_base + pulse_amp * sin(t * pulse_fast_hz) * sin(t * pulse_slow_hz);
	// The stutter, quantised so every pixel agrees on this tick (see the header).
	float tick = floor(t * stutter_hz);
	if (hash11(tick) < stutter_chance) {
		pulse *= stutter_depth;
	}
	// ------------------------------------------------------------------------------------------

	float lum = n * lines * vig;

	ALBEDO = tint * lum;
	EMISSION = emission_tint * lum * emission_energy * pulse;
	ROUGHNESS = 0.2;   // TvPortal.cs's value.
	METALLIC = 0.0;
	SPECULAR = 0.5;
}
