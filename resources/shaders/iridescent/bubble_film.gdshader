shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_never, diffuse_lambert, specular_schlick_ggx;

// =================================================================================================
// BUBBLE — thin-film iridescence, and a wobble that only appears once the bubble is big.
// GENERAL-PURPOSE, PROJECT-NEUTRAL, MOOD-NEUTRAL.
// =================================================================================================
//
// A soap film: transparent, rainbow-sheened, stable when small and unstable when large.
//
// -------------------------------------------------------------------------------------------------
// THIS ONE IS TRANSPARENT, AND THAT REVERSES THE ARGUMENT THE CRYSTAL SHADERS MAKE
// -------------------------------------------------------------------------------------------------
// `crystal_refraction` goes to some trouble to stay OPAQUE -- it samples the screen itself rather
// than letting the blender composite it -- because transparency costs sorting and overdraw. This
// shader does the opposite on purpose, and the reason is a real trade rather than inconsistency:
//
//   * The crystal's alternative was a BACKBUFFER COPY: a fixed, full-resolution cost paid once per
//     frame whether the prop covers half the screen or nine pixels. For a solid prop that is worth
//     avoiding.
//   * A bubble is a THIN FILM. You see through it essentially undistorted -- that is what makes it
//     a bubble rather than a lens -- so it needs no copy of the screen at all. Alpha blending gives
//     it see-through for the price of sorting and some overdraw, both of which scale with the FEW
//     SMALL objects actually on screen.
//
// So for bubbles, alpha blending is the CHEAPER choice, and for the crystal it was the dearer one.
// The rule is not "transparency is expensive", it is "compare the cost that scales with your object
// against the cost that does not".
//
// `cull_disabled` is here because a bubble has a far wall and you can see it through the near one --
// remove it and the shape reads as a dome. It doubles this object's overdraw, which on something
// this small and this few is the correct place to spend.
//
// `depth_draw_never` so bubbles do not occlude each other's blending, and so a cluster of them
// composites rather than punching holes in one another.
//
// -------------------------------------------------------------------------------------------------
// THE IRIDESCENCE IS THIN-FILM INTERFERENCE, COMPUTED RATHER THAN SAMPLED FROM A RAMP TEXTURE
// -------------------------------------------------------------------------------------------------
// Light reflecting off the front of a soap film interferes with light that travelled through and
// reflected off the back. The extra distance -- the OPTICAL PATH DIFFERENCE -- is about
// `2 * n * thickness * cos(refraction angle)`. Wavelengths that fit a whole number of times into
// that distance reinforce; others cancel. Because the path length depends on VIEW ANGLE, the colour
// sweeps as the surface curves away, which is exactly the rainbow banding a real bubble has.
//
// Evaluating that directly is three cosines and a divide -- about 12 ALU and NO TEXTURE FETCH. The
// usual implementation samples a pre-baked rainbow ramp, which costs a fetch, a texture in memory,
// and the colour is then a fixed artistic guess rather than a function of thickness. Computing it
// means `film_nm` is a real physical dial: thin films are gold and magenta, thicker ones cycle
// through more bands, and very thin ones go colourless -- all of which fall out of the arithmetic
// instead of needing separate art.
//
// -------------------------------------------------------------------------------------------------
// WHY SIZE DRIVES THE WOBBLE, WHICH IS PHYSICS RATHER THAN AN ARBITRARY MAPPING
// -------------------------------------------------------------------------------------------------
// Surface tension pulls a bubble toward a sphere with a pressure that goes as 1/radius. A small
// bubble therefore has an enormous restoring force relative to its mass and snaps back to round
// faster than the eye can follow -- it reads as a hard little bead. As the radius grows that
// restoring pressure falls away while the mass and the drag it meets both grow, so the same passing
// air that could not deform a small bubble now sets a large one oscillating, and it stays deformed
// long enough to see.
//
// That is why `wobble_onset` and `wobble_full` are expressed in METRES rather than as an abstract
// 0..1: the transition is a real one at a real scale, and a project that changes what a metre means
// in its world should change these with it.
//
// THE SIZE IS READ FROM THE OBJECT'S OWN TRANSFORM by default, not from a uniform somebody has to
// remember to set. `MODEL_MATRIX` carries the scale, so a bubble that is grown by animating its
// node scale becomes unstable on its own, with nothing else wired up. A shader whose headline
// behaviour depends on a uniform nobody sets is a shader that looks broken in every scene but the
// one it was authored in.

#include "res://resources/shaders/iridescent/variation.gdshaderinc"

group_uniforms Film;
// Film thickness in NANOMETRES. This is the colour dial and it is physical: around 200 nm reads
// gold and magenta, 400-600 nm cycles through more bands, and below about 100 nm the film goes
// nearly colourless -- which is what a bubble does just before it pops.
uniform float film_nm : hint_range(0.0, 1200.0, 1.0) = 320.0;
// How much the thickness varies over the surface. Real films are thinner at the top because the
// liquid drains downward, which is why the bands drift and why the crown goes clear first.
uniform float film_variation : hint_range(0.0, 1.0, 0.005) = 0.45;
uniform float film_drain : hint_range(0.0, 1.0, 0.005) = 0.55;
uniform float film_scale : hint_range(0.5, 12.0, 0.1) = 3.0;
// Refractive index of the film. Soap water is about 1.33; the dial is here because the same
// arithmetic describes oil on water at ~1.45 and a few other films worth having.
uniform float film_ior : hint_range(1.0, 2.0, 0.005) = 1.33;
uniform float iridescence : hint_range(0.0, 3.0, 0.005) = 1.0;
uniform float iridescence_sat : hint_range(0.0, 1.0, 0.005) = 0.85;

group_uniforms Body;
// How visible the film is face-on. Near zero: you look straight through the middle of a bubble,
// which is most of why it reads as a bubble rather than as a ball.
uniform float alpha_center : hint_range(0.0, 1.0, 0.005) = 0.06;
// How visible it is edge-on. A bubble's rim is bright because you are looking through a long
// slanted path of film there -- the same Fresnel term that drives the colour also drives this.
uniform float alpha_edge : hint_range(0.0, 1.0, 0.005) = 0.85;
uniform float fresnel_power : hint_range(0.5, 8.0, 0.05) = 2.6;
// A plain white specular pip, on top of the interference. Real bubbles carry a hard little
// reflection of whatever light is in the room, and without it the surface reads as a gradient.
uniform float highlight : hint_range(0.0, 2.0, 0.005) = 0.45;
uniform float roughness_v : hint_range(0.0, 1.0, 0.005) = 0.04;

// ADDED 2026-08-27 (BT-9). The bubble-test level's D10 aid: "bubbles glow faintly at night."
// DEFAULT 0.0, WHICH IS AN EXACT NO-OP -- every existing user of this shader (IridescentProps'
// scramble-course props) renders bit-identically unless something sets this, which is the only
// reason it was safe to add a uniform to a shared ported shader rather than fork it.
//
// It lifts the film's OWN interference colour rather than adding a white glow, because a bubble
// that glows white at night is a lamp and stops being a bubble. The alpha lift is the half that
// actually makes it visible: at alpha_center = 0.06 a bubble against a black sky is invisible no
// matter how bright its emission is, so coverage has to rise with the glow or the uniform does
// nothing a player can see. BT-6's script drives this from the cycle phase.
//
// RANGE WIDENED 0.8 -> 3.0 BY DARK-1 (2026-08-28), and the reason is compositing, not taste.
// This surface is TRANSPARENT, so a glow pass never sees EMISSION -- it sees
// `emission * ALPHA + background * (1 - ALPHA)`. At 0.8 that composite lands around 0.2 in
// linear HDR, under any bloom threshold that does not also bloom every unshaded white label in
// the level (an unshaded white surface sits at exactly 1.0, and a first pass at threshold 0.62
// blew the hub's 5 m billboard label into a white disc). `BubbleOscillation.MaxEmissionEnergy`
// is now 2.4, which puts the composite over 1.15 and leaves the labels alone. The 0.25 alpha
// coupling below is UNCHANGED and does the rest: at 2.4 it carries alpha_center 0.06 -> 0.66,
// so at deep night the film stops being a film and reads as a lit orb, which is the point.
//
// STILL AN EXACT NO-OP AT ITS DEFAULT. Every other user of this shader (IridescentProps'
// scramble-course props) sets nothing and renders bit-identically; widening a range cannot move
// a value nobody writes.
uniform float emission_energy : hint_range(0.0, 3.0, 0.005) = 0.0;

group_uniforms Wobble;
// Radius, in metres, below which the bubble is effectively rigid and above which it is fully
// unstable. See the header: this is a physical transition, not an arbitrary curve.
uniform float wobble_onset_m : hint_range(0.01, 2.0, 0.005) = 0.12;
uniform float wobble_full_m : hint_range(0.02, 4.0, 0.005) = 0.55;
// Peak displacement as a FRACTION OF RADIUS, so a big bubble wobbles by proportionally the same
// amount rather than by a fixed distance that would be invisible on one and violent on another.
uniform float wobble_amp : hint_range(0.0, 0.5, 0.002) = 0.11;
uniform float wobble_freq : hint_range(0.5, 8.0, 0.05) = 2.1;
uniform float wobble_speed : hint_range(0.0, 4.0, 0.01) = 0.85;
// The slow twist about the vertical axis. A large bubble does not only dent, it turns as it goes,
// because the same drag that deforms it is not symmetric about its centre.
uniform float twist_amount : hint_range(0.0, 2.0, 0.005) = 0.55;
// Vertical bob, in metres, again scaled by instability. NOTE this moves the MESH, not the node, so
// shadows and any collider stay where the node is -- fine for a drifting prop, wrong for anything
// that has to be hit.
uniform float float_bob_m : hint_range(0.0, 0.5, 0.005) = 0.05;
uniform float float_speed : hint_range(0.0, 3.0, 0.01) = 0.6;
// Overrides the size read from the object's transform. 0 means "use the transform", which is the
// default and the reason this works with no wiring.
uniform float radius_override_m : hint_range(0.0, 4.0, 0.005) = 0.0;
// Below 0: live. At or above 0: every animated term freezes at that second, so two captures of the
// same build are bit-identical. Same device and same reasoning as the water shader's time_scrub --
// without it, an A/B of an animated effect is noise-limited rather than exact.
uniform float time_scrub : hint_range(-1.0, 120.0, 0.01) = -1.0;

group_uniforms Pop;
// Seconds since the film ruptured. Below 0 the bubble is intact and every pop term folds out, so
// this whole section costs one comparison until something actually pops.
uniform float pop_time : hint_range(-1.0, 4.0, 0.005) = -1.0;
// Object-space direction of the rupture point. A real film breaks where it is thinnest, which is
// usually the crown, because the liquid drains downward -- hence the default pointing up.
uniform vec3 pop_origin = vec3(0.0, 1.0, 0.0);
// How fast the hole's edge travels across the film, in METRES PER SECOND.
//
// READ THIS BEFORE SETTING IT. Real soap film retracts at sqrt(2*surface_tension/(density*
// thickness)), which for soapy water at a few hundred nanometres is roughly 5 to 10 m/s. That means
// a 10 cm bubble is completely gone in about 10-20 MILLISECONDS -- ONE FRAME at 60 Hz, and never
// more than two. A physically accurate pop is not slow enough to see; it is a bubble in one frame
// and no bubble in the next.
//
// So the default here is deliberately WRONG BY ABOUT 20x, because the honest number produces no
// visible effect at all. This is one of the rare places where matching reality is the bug.
uniform float pop_speed : hint_range(0.05, 20.0, 0.01) = 0.45;
// The retracting edge is where the film PILES UP -- the liquid it sweeps has to go somewhere, and it
// collects in a thickening rim that is brighter, more opaque, and more strongly coloured than the
// film behind it. That rim is the whole visual event of a pop; without it the bubble just fades.
// It is also physically why a pop makes droplets: the rim gets fat enough to break up.
uniform float pop_rim_width : hint_range(0.005, 0.6, 0.005) = 0.13;
uniform float pop_rim_gain : hint_range(0.0, 6.0, 0.01) = 2.6;
// How much thicker the rim's film reads, which shifts its interference colour toward the warm end
// as the liquid gathers. Set to 1.0 for no shift.
uniform float pop_rim_thicken : hint_range(1.0, 4.0, 0.01) = 1.9;

group_uniforms Merge;
// Seconds since two bubbles coalesced. Below 0, nothing here runs.
uniform float merge_time : hint_range(-1.0, 6.0, 0.005) = -1.0;
// The axis the two bubbles came together along, in object space.
//
// THIS IS THE PART THAT MAKES A MERGE LOOK LIKE A MERGE RATHER THAN LIKE A WOBBLE. When two bubbles
// coalesce, the film between them retracts along the line joining their centres, and that impulse
// is ALONG THE AXIS -- so the merged bubble does not wobble randomly, it rings in the l=2 mode:
// stretching along the merge axis, then flattening perpendicular to it, then back, decaying.
// Everything about the resulting shape is organised around this direction.
uniform vec3 merge_axis = vec3(1.0, 0.0, 0.0);
// Peak deformation as a fraction of radius. A real coalescence overshoots hard -- the merged bubble
// visibly elongates -- so this wants to be well above the ambient wobble amplitude.
uniform float merge_amp : hint_range(0.0, 0.8, 0.005) = 0.34;
// Ringing frequency, in Hz. The physical l=2 mode goes as sqrt(surface_tension / (density * r^3)),
// so BIGGER BUBBLES RING SLOWER -- which is the same 1/radius-ish relationship that makes big
// bubbles wobbly in the first place, and it is applied below rather than left to the dial.
uniform float merge_hz : hint_range(0.2, 12.0, 0.05) = 3.2;
// Seconds for the ringing to fall to 1/e. Viscosity damps a real one out in well under a second.
uniform float merge_decay : hint_range(0.05, 4.0, 0.01) = 0.55;

varying float v_instability;
varying vec3 v_dir;
varying float v_radius_m;

float btime() {
	return time_scrub < 0.0 ? TIME : time_scrub;
}

// The radial displacement field, in units of radius. Three lobes at incommensurate frequencies so
// the shape never repeats into an obvious pulse -- a single sine reads as breathing, which is a
// different and much more deliberate-looking motion than a wobble.
float wob(vec3 p, float t) {
	float a = sin(p.y * wobble_freq * 3.1 + t * 1.00);
	float b = sin(p.x * wobble_freq * 2.3 - t * 1.37 + 1.7);
	float c = sin(p.z * wobble_freq * 2.9 + t * 0.79 + 4.1);
	return (a + b + c) * 0.3333;
}

void vertex() {
	float t = btime();

	// SIZE FROM THE TRANSFORM. length() of a basis column is that axis's scale, so this tracks a
	// bubble being grown by animating its node and needs nothing wired up.
	float radius = radius_override_m;
	if (radius <= 0.0) {
		radius = length(MODEL_MATRIX[0].xyz) * length(VERTEX);
	}

	// Smooth, and clamped at both ends: below onset it is exactly zero, so a small bubble is
	// genuinely rigid rather than very slightly wobbly, which is the difference the brief asks for.
	v_instability = smoothstep(wobble_onset_m, max(wobble_full_m, wobble_onset_m + 0.001), radius);
	v_dir = normalize(VERTEX);
	v_radius_m = radius;

	// ---------------------------------------------------------------- COALESCENCE RINGING
	//
	// When two bubbles merge, the film between them retracts ALONG THE LINE JOINING THEM, so the
	// impulse has an axis and the resulting oscillation is organised around it. The merged bubble
	// rings in the l = 2 mode: elongating along the merge axis, then flattening perpendicular to it,
	// then back, decaying. That is why this takes a direction rather than just an amplitude -- an
	// axis-less wobble reads as "this bubble is unstable", and only an axis-aligned one reads as
	// "these two just became one".
	//
	// The shape term is the second Legendre polynomial, P2(cos t) = (3cos^2 t - 1)/2, which IS the
	// l = 2 mode rather than an approximation of it: +1 along the axis, -0.5 around the equator, so
	// the volume it moves out of the poles is the volume it moves into the waist. A bubble ringing
	// this way does not visibly change size, which is exactly right -- it has already finished
	// changing size, at the moment of the merge.
	if (merge_time >= 0.0) {
		float ct = dot(normalize(VERTEX), normalize(merge_axis));
		float p2 = (3.0 * ct * ct - 1.0) * 0.5;

		// BIGGER BUBBLES RING SLOWER, and this is not a taste call: the l = 2 frequency goes as
		// sqrt(surface_tension / (density * r^3)), so it falls off as r^-1.5. Scaled against a 0.3 m
		// reference so `merge_hz` stays a number somebody can reason about.
		float hz = merge_hz * pow(max(radius, 0.02) / 0.3, -1.5);

		float env = exp(-merge_time / max(merge_decay, 0.01));
		float ring = sin(merge_time * hz * 6.2831853) * env * merge_amp;
		VERTEX += NORMAL * (p2 * ring * length(VERTEX));

		// The normal follows the deformation. d(P2)/d(theta) = -3*cos*sin, taken along the tangent
		// that lies in the plane containing the axis -- without this the ringing changes the outline
		// while the rainbow stays put, which is the same defect the ambient wobble avoids above.
		float st = sqrt(max(0.0, 1.0 - ct * ct));
		vec3 axis = normalize(merge_axis);
		vec3 tang = normalize(cross(cross(axis, normalize(VERTEX)), normalize(VERTEX)) + vec3(1e-6));
		NORMAL = normalize(NORMAL - tang * (-3.0 * ct * st) * ring);
	}

	if (v_instability > 0.0) {
		vec3 p = normalize(VERTEX);
		float amp = wobble_amp * v_instability;

		// THE NORMAL IS RECOMPUTED, NOT LEFT ALONE. This shader's entire look is a function of the
		// view angle against the normal, so a displaced surface carrying its old normal would wobble
		// in silhouette while the rainbow stayed painted on -- the bands would slide over a shape
		// that no longer matches them. `pulsing_organic` in this lab has exactly that defect and is
		// listed as such; this is the worked fix.
		//
		// Two finite differences along the tangent frame. Three evaluations of `wob` instead of one,
		// in the VERTEX stage, where the count is orders of magnitude below the pixel count.
		float e = 0.06;
		vec3 tg = normalize(TANGENT);
		vec3 bt = normalize(BINORMAL);
		float d0 = wob(p, t);
		float dt = wob(normalize(p + tg * e), t);
		float db = wob(normalize(p + bt * e), t);

		VERTEX += NORMAL * (d0 * amp * length(VERTEX));
		NORMAL = normalize(NORMAL - tg * ((dt - d0) / e) * amp - bt * ((db - d0) / e) * amp);

		// The twist: a rotation about Y whose angle grows with height, so the bubble shears rather
		// than simply spinning. Scaled by instability like everything else here.
		float ang = VERTEX.y * twist_amount * v_instability * sin(t * 0.53);
		float s = sin(ang);
		float c = cos(ang);
		VERTEX = vec3(VERTEX.x * c - VERTEX.z * s, VERTEX.y, VERTEX.x * s + VERTEX.z * c);
		NORMAL = normalize(vec3(NORMAL.x * c - NORMAL.z * s, NORMAL.y, NORMAL.x * s + NORMAL.z * c));

		VERTEX.y += sin(t * float_speed) * float_bob_m * v_instability;
	}
}

// Thin-film interference. `opd` is the optical path difference in nanometres; the three cosines are
// the response at roughly the peak sensitivities of the eye's red, green and blue receptors.
vec3 interference(float opd) {
	vec3 lambda = vec3(680.0, 550.0, 440.0);
	vec3 phase = 6.2831853 * opd / lambda;
	// Half-wave shift at the front surface: the reflection off the denser medium inverts, which is
	// why a film thinner than a quarter wavelength is DARK rather than bright. Dropping this term
	// gets the colour ordering visibly wrong at low thickness.
	vec3 c = 0.5 + 0.5 * cos(phase + 3.1415927);
	// Toward white rather than toward grey: interference desaturates by adding all wavelengths back
	// in, not by losing chroma.
	return mix(vec3(dot(c, vec3(0.3333))), c, iridescence_sat);
}

void fragment() {
	vec3 n = normalize(NORMAL);
	float ndv = clamp(dot(n, normalize(VIEW)), 0.0, 1.0);
	float fres = pow(1.0 - ndv, fresnel_power);

	// ---------------------------------------------------------------- THE POP
	//
	// A film does not fade out and it does not shatter. A hole NUCLEATES at one point -- wherever
	// the film happened to get thinnest -- and its edge sweeps outward across the surface until
	// there is no film left. Everything you actually see of a pop is that travelling edge.
	//
	// So this is an expanding spherical cap: the angle from the rupture point to the hole's edge
	// grows as (speed * time) / radius. Inside it there is nothing. Just outside it is the RIM.
	float pop_rim = 0.0;
	float pop_hole = 0.0;
	if (pop_time >= 0.0) {
		float ang = acos(clamp(dot(normalize(v_dir), normalize(pop_origin)), -1.0, 1.0));
		// Arc length over radius gives the angle the edge has reached. Dividing by the radius is
		// what makes a big bubble take proportionally longer to clear at the same film speed --
		// correct, and the reason a large bubble's pop is the one you can actually watch.
		float edge = (pop_time * pop_speed) / max(v_radius_m, 0.005);
		float d = ang - edge;

		// Inside the hole: gone. Not faded -- gone. A film that dims as it opens reads as a
		// dissolve, which is a different event.
		pop_hole = 1.0 - step(0.0, d);

		// The rim is where the retracting film PILES UP. The liquid the edge sweeps has to go
		// somewhere and it collects here, which is why a popping bubble flashes bright along the
		// opening edge -- and why the rim eventually breaks into droplets. This shader draws the
		// rim; the droplets are particles and belong to the particle lab.
		pop_rim = smoothstep(pop_rim_width, 0.0, abs(d)) * (1.0 - pop_hole);
	}

	// Thickness across the surface. `film_drain` thins the top, because the liquid in a real film
	// runs downward and the crown goes colourless first -- it is the cue that a bubble is about to
	// go, and it costs one dot product.
	vec3 wp = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float band = sin(n.y * film_scale + wp.x * 0.7) * 0.5 + 0.5;
	float thin = 1.0 - clamp(n.y * 0.5 + 0.5, 0.0, 1.0) * film_drain;
	// The rim carries thicker film, which walks its interference colour toward the warm end -- the
	// gathered liquid genuinely is a thicker film, so the colour shift falls out of the same
	// arithmetic rather than being painted on.
	// Per-instance film thickness, so a cloud of bubbles is a cloud of DIFFERENT bubbles. Varying
	// the thickness rather than a tint means each one's bands sweep differently as you move past,
	// which is the whole reason a drift of real bubbles is worth looking at.
	float nm_i = film_nm * (1.0 + fx_pos_variation(MODEL_MATRIX) * 0.5);
	float thickness_nm = nm_i * (1.0 + (band - 0.5) * 2.0 * film_variation) * thin
		* mix(1.0, pop_rim_thicken, pop_rim);

	// Snell's law inside the film: a grazing view travels a longer path through it, which is what
	// makes the bands sweep toward the rim.
	float sin_t = (1.0 / film_ior) * sqrt(max(0.0, 1.0 - ndv * ndv));
	float cos_t = sqrt(max(0.0, 1.0 - sin_t * sin_t));
	float opd = 2.0 * film_ior * thickness_nm * cos_t;

	vec3 iri = interference(opd) * iridescence;

	// The film is brightest where you look through the most of it, which is the same Fresnel curve
	// that drives the alpha -- so colour and coverage rise together, as they do on a real bubble.
	vec3 col = iri * (0.25 + 0.75 * fres);
	col += vec3(pow(fres, 3.0) * highlight);
	// The rim is brighter than the film it came from, because it is more of it.
	col += iri * pop_rim * pop_rim_gain;

	ALBEDO = vec3(0.0);
	// The night lift (BT-9): the film's own interference colour, brighter. Not a white term --
	// see the uniform's note. At emission_energy = 0 this is `col` exactly.
	EMISSION = col + iri * emission_energy;
	ROUGHNESS = roughness_v;
	METALLIC = 0.0;
	SPECULAR = 0.5;
	// ALPHA IS WRITTEN HERE DELIBERATELY, unlike every other material in this directory. See the
	// header: a bubble is a thin film and wants real blending, and the transparent queue is the
	// cheaper of the two options for an object this small.
	// The `emission_energy` term is the half that makes the glow VISIBLE rather than merely bright:
	// a 6%-alpha film against a night sky is nothing regardless of its emission. Also 0 by default.
	ALPHA = clamp(mix(alpha_center, alpha_edge, fres) + emission_energy * 0.25, 0.0, 1.0);
	// The rim is also more OPAQUE -- a thick bead of liquid, not a film -- and the hole is nothing.
	ALPHA = mix(ALPHA, 1.0, pop_rim * 0.85);
	ALPHA *= (1.0 - pop_hole);
}
