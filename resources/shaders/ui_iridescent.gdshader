shader_type canvas_item;

// =================================================================================================
// UI IRIDESCENT THREAD — one quiet soap-film accent, and a rule about where it may go.
// (BT-9, 2026-08-27.)
// =================================================================================================
//
// Talon: "apply a light, subtle iridescent thread across this build's UI ... Keep it small -- not a
// big overhaul. Ties into the separate in-progress iridescence shader work (soap bubble /
// mother-of-pearl style refraction). Defer to standard UX/UI best practice for exactly where and
// how much."
//
// -------------------------------------------------------------------------------------------------
// WHERE IT MAY GO. THIS IS THE PART THAT KEEPS IT SMALL, AND IT IS A RULE, NOT A PREFERENCE.
// -------------------------------------------------------------------------------------------------
// **The thread only ever touches non-interactive identity elements. It never touches a control, a
// state, or a focus indicator.** Two placements exist and no third is authorised without a new
// direction: the title screen's wordmark underline, and the HUD's bubble icon fill (BT-8).
//
// The `ui-design` pass considered the obvious alternative -- the start button's focus ring -- and
// REFUSED it, for three reasons that are worth keeping written down because the idea is tempting
// and will come back:
//
//   1. A focus ring is the entire cursor for keyboard, controller and switch users. Decorating it
//      makes "is this focused?" a question about whether something looks pretty, which is the one
//      question a focus indicator must never raise.
//   2. UI components must hold 3:1 contrast against their surround. This shader's colour DRIFTS
//      by design, so its contrast against any given background is a moving number -- fine for
//      decoration, disqualifying for an accessibility-critical affordance.
//   3. It animates continuously. A permanently-visible moving element is the exact case
//      reduced-motion settings exist for, and a focus ring cannot be turned off.
//
// A wordmark underline has none of those obligations: it is decorative by definition, carries no
// state, is not a target, appears exactly once, and sits on the identity element -- which is
// precisely where a house "thread" belongs. Hence: one 2-3 px rule, one icon fill, nothing else.
//
// -------------------------------------------------------------------------------------------------
// HOW MUCH. LOW SATURATION IS THE WHOLE BRIEF.
// -------------------------------------------------------------------------------------------------
// Thin-film interference at full strength is a rainbow, and a rainbow in a UI reads as a gamer
// keyboard, not as mother-of-pearl. `saturation` defaults to 0.30 and `alpha` to 0.55, which lands
// it as a sheen -- you notice the surface is not flat before you notice it has colours.
//
// The drift is deliberately SLOWER than anything else in the interface: ~20 s for a full cycle,
// against the 120-300 ms this repo's UI uses for transitions. Two orders of magnitude apart is
// what stops it competing for attention -- it is never seen moving, only ever noticed to have
// moved. `drift_speed = 0.0` freezes it to a static gradient, which is the reduced-motion state
// and is also what a capture should use for a reproducible screenshot.
//
// -------------------------------------------------------------------------------------------------
// THE COLOUR IS COMPUTED, NOT SAMPLED, AND IT IS THE SAME MATHS AS THE BUBBLE
// -------------------------------------------------------------------------------------------------
// `resources/shaders/iridescent/bubble_film.gdshader` builds its rainbow from real thin-film
// interference -- three cosines at the RGB wavelengths, driven by optical path difference. This
// file uses the same three-cosine construction rather than a hue-rotate or a ramp texture, so the
// UI thread and the in-world bubbles are visibly the same material family instead of merely both
// being colourful. That is what makes it a "thread" rather than a gradient.

// Film thickness in nanometres, as the bubble shader means it. 320 is the bubble's own default and
// sits in the first-order band where the colours are pastel rather than metallic.
uniform float film_nm : hint_range(120.0, 900.0, 1.0) = 320.0;

// How much the thickness varies across the element -- this is what makes it a GRADIENT rather than
// one flat colour. 0.45 gives roughly one and a half colour bands across a wide underline.
uniform float band_spread : hint_range(0.0, 2.0, 0.005) = 0.45;

// Full cycles per second. 0.05 = one 20-second cycle. 0.0 freezes (reduced-motion / capture).
uniform float drift_speed : hint_range(0.0, 1.0, 0.005) = 0.05;

// Pulled toward grey. This is the single knob that decides "sheen" vs "rainbow".
uniform float saturation : hint_range(0.0, 1.0, 0.005) = 0.30;

// Overall lightness of the band. 0.85 keeps it bright on the dark UI this repo ships (light mode
// is retired) without blowing to white.
uniform float value : hint_range(0.0, 2.0, 0.005) = 0.85;

// Element alpha, multiplied into the modulate the control already carries.
uniform float alpha : hint_range(0.0, 1.0, 0.005) = 0.55;

// Along which axis the bands run. Default is horizontal, for an underline; set to (0,1) for a
// vertical element and (0.7,0.7) for an icon fill, where a diagonal reads better than either axis.
uniform vec2 band_axis = vec2(1.0, 0.0);

// Capture reproducibility, same idiom as the other BT-9 shaders. >= 0 freezes at that time.
uniform float time_scrub : hint_range(-1.0, 120.0, 0.01) = -1.0;

// -------------------------------------------------------------------------------------------------
// SPECTRUM-SLICE MODE — the wordmark underline's parameters. (MENU-1, 2026-08-28.)
// -------------------------------------------------------------------------------------------------
// EVERY UNIFORM BELOW DEFAULTS TO OFF, AND THE LEGACY PATH BELOW IS TEXTUALLY UNCHANGED. The HUD's
// bubble-icon placement sets `band_axis`, `alpha` and `value` and nothing else, so its output is
// bit-identical to what BT-9 shipped. This mode exists because the underline — BT-9's own second
// authorised placement, and the one that had never been built — needs three things the drift path
// cannot express, all of them agreed with Talon before this was written:
//
//   1. "make the rainbow effect underline more subtle ... use a zoomed in piece of the spectrum."
//      The drift path spreads a fixed FRACTION of `film_nm` across the element, so a wide rule
//      always shows a wide sweep of hues and reads as a pride bar. Slice mode instead names an
//      ABSOLUTE window in nanometres — the agreed 58 nm — and walks that narrow window ALONG the
//      spectrum, so only a few neighbouring hues exist at any one moment.
//   2. "use something to make that move along with the bubble." The window's centre and the
//      travelling highlight both run on `phase`, which the caller feeds from the same term that
//      drives the menu's bubbles. Sharing the driver is what makes "along with" true rather than
//      merely simultaneous.
//   3. A peak, so the rule has a moving bright point instead of one flat opacity: base 0.30 rising
//      to the agreed 0.64 where the highlight is.
//
// The BT-9 rule is untouched and still binds: this is a decorative, non-interactive identity
// element. It never goes on a control, a state, or a focus indicator.

// Width of the visible window, in nanometres of film thickness. 0 keeps the legacy drift path.
uniform float span_nm : hint_range(0.0, 400.0, 0.5) = 0.0;

// Where the window sits, and how far along the spectrum it walks. 430 + 118 puts the walk across
// the blue-green-gold part of the first-order band and never reaches the metallic higher orders.
uniform float centre_nm : hint_range(120.0, 900.0, 1.0) = 430.0;
uniform float centre_walk_nm : hint_range(0.0, 400.0, 1.0) = 118.0;

// The shared driver. Negative means "derive one from TIME", so the material still animates on its
// own if nobody is feeding it.
uniform float phase = -1.0;

// How much the travelling highlight adds to the base alpha. Base 0.30 + 0.34 = the agreed 0.64
// peak. 0 leaves the element at a flat `alpha`.
uniform float highlight_gain : hint_range(0.0, 1.0, 0.005) = 0.0;

// Optical path multiplier. 2.0 is a normal-incidence film, which is what the legacy path assumes.
// 2.5629 is 2 * 1.34 * cos(refracted) for a film viewed at the angle a screen rule is seen at, and
// is what makes the underline the same physical material as the menu's bubbles.
uniform float opd_scale : hint_range(0.5, 4.0, 0.0001) = 2.0;

// What low saturation pulls TOWARD. Negative keeps BT-9's behaviour (the band's own luminance,
// which is what stops a dark UI's sheen looking washed out). A light frame wants the opposite —
// pulling toward white keeps the rule pale and lets it read as a tint rather than as a grey line.
uniform float grey_point : hint_range(-1.0, 1.0, 0.005) = -1.0;

// Fraction of the rule's length over which each end fades in. Feathered ends, so the rule has no
// terminators — the same reason the scrim under it has none.
const float END_FEATHER = 0.05;

// Width of the travelling highlight, as a gaussian variance in u. 0.012 is roughly a fifth of the
// rule, which reads as a moving brightness rather than as a dot sliding along a wire.
const float HIGHLIGHT_VAR = 0.012;

// Thin-film interference, condensed from bubble_film.gdshader's `interference()`. The three
// wavelengths are the standard sRGB primaries in nm; cos(2*pi*opd/lambda) is the two-beam
// interference term, remapped to 0..1.
vec3 interference(float opd) {
	const vec3 lambda = vec3(680.0, 550.0, 440.0);
	// Named `phi` rather than `phase` since 2026-08-28: `phase` is now a uniform (see the
	// spectrum-slice block above) and a local of the same name is a hard compile error. Nothing
	// about this function changed.
	vec3 phi = 6.2831853 * opd / lambda;
	return 0.5 + 0.5 * cos(phi);
}

void fragment() {
	float t = time_scrub >= 0.0 ? time_scrub : TIME;

	// Position along the band axis, 0..1.
	float u = clamp(dot(UV, normalize(band_axis)), 0.0, 1.0);

	float thickness;
	float a = alpha;

	if (span_nm > 0.0) {
		// SLICE MODE. A narrow window, walked along the spectrum by the shared phase.
		float ph = phase >= 0.0 ? phase : t * drift_speed * 6.2831853;
		float centre = centre_nm + centre_walk_nm * (0.5 + 0.5 * sin(ph * 1.7));
		thickness = centre + span_nm * (u - 0.5);

		// The highlight rides the SAME term as the window's centre, so the bright point and the
		// colour turn over together instead of beating against each other.
		float hx = 0.5 + 0.42 * sin(ph * 1.7);
		a = alpha + highlight_gain * exp(-((u - hx) * (u - hx)) / HIGHLIGHT_VAR);
		a *= clamp(min(u, 1.0 - u) / END_FEATHER, 0.0, 1.0);
	} else {
		// Thickness varies along the element and drifts with time. The drift is a thickness change,
		// not a hue rotation -- a real film thins and thickens, and doing it this way is why the
		// colours move through each other rather than sliding around a wheel.
		thickness = film_nm * (1.0 + (u - 0.5) * 2.0 * band_spread + sin(t * drift_speed * 6.2831853) * 0.18);
	}

	vec3 iri = interference(opd_scale * thickness);

	// THE HALF-WAVE SHIFT, slice mode only. A reflection off the denser medium picks up a pi phase
	// shift, which is why a real film's thinnest point is BLACK rather than white. `interference()`
	// above omits it — BT-9's HUD placement is tuned around the un-shifted ramp and inverting it
	// there would restyle a shipped icon — so the correction is applied here, where the approved
	// previz frame includes it. `0.5 + 0.5*cos(x + pi)` is exactly `1 - (0.5 + 0.5*cos(x))`, so this
	// is the shift and not an approximation of it. Without it the underline comes out magenta where
	// the approved frame is pale green, which is the whole visible difference.
	if (span_nm > 0.0) {
		iri = vec3(1.0) - iri;
	}

	// Desaturate toward the band's own luminance. Keeping the grey point at the film's OWN
	// brightness (rather than at 0.5) is what stops low saturation reading as "washed out".
	float lum = dot(iri, vec3(0.2126, 0.7152, 0.0722));
	float grey = grey_point >= 0.0 ? grey_point : lum;
	vec3 col = mix(vec3(grey), iri, saturation) * value;

	COLOR = vec4(col, a) * COLOR;
}
