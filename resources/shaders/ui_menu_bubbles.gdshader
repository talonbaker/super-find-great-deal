shader_type canvas_item;
render_mode blend_premul_alpha, unshaded;

// =================================================================================================
// THE MENU'S SOAP BUBBLES — five thin films over the blurred world. (MENU-1, 2026-08-28.)
// =================================================================================================
//
// Talon, 2026-08-28: "a bubble against a similar kind of background but not dark ... more light
// and bubble and blocks but I still want that came blue depth effect."
//
// ONE FULLSCREEN PASS, FIVE BUBBLES. Each bubble is a disc evaluated analytically: there is no
// mesh, no sprite and no texture. That matters for two reasons. The rim's colour comes from the
// viewing angle across the sphere, so it has to be computed per pixel from the disc's own
// geometry; and doing all five in one fragment lets them composite in a defined order without
// five transparent draws fighting over depth.
//
// THE COLOUR IS COMPUTED, NOT SAMPLED, AND IT IS THE HOUSE MATHS. Real two-beam thin-film
// interference — three cosines at the sRGB primaries, driven by optical path difference — the
// same construction `resources/shaders/iridescent/bubble_film.gdshader` and
// `resources/shaders/ui_iridescent.gdshader` use. That shared physics is what makes the menu
// bubble, the wordmark's underline and the in-world bubbles read as one material rather than as
// three things that happen to be colourful.
//
// EVERYTHING MOVES OFF ONE `phase`. The film thickness, the swirl and the rim all advance on the
// single phase term the menu also feeds to the underline — Talon asked for the rule to "move
// along with the bubble", and sharing the driver is the only way that is true rather than
// approximately true. `phase` is set by the caller every frame, so reduced motion is expressed by
// simply not advancing it: the screen freezes whole, not in pieces.
//
// PREMULTIPLIED OUTPUT. See `ui_menu_blur.gdshader` — the layer is blurred after this, and a
// premultiplied buffer is the only kind a linear blur handles without ringing the rims dark.

uniform float phase = 0.0;

// Height / width of the frame. The bubble field is authored in WIDTH-normalised coordinates so a
// disc stays a disc at any aspect: x runs 0..1, y runs 0..aspect.
uniform float aspect = 0.5625;

// Overall opacity of the whole layer, for fades. Not a look knob.
uniform float layer_alpha : hint_range(0.0, 1.0, 0.01) = 1.0;

// -------------------------------------------------------------------------------------------------
// THE EMISSIVE TERMS. (MENU-2, 2026-08-29 — the frame went from a light world to a night one.)
// -------------------------------------------------------------------------------------------------
// Against a dark ground the film is the brightest thing on screen, and interference colours read
// far harder here than they ever did over white: they are being seen against black instead of
// competing with a lit sky. Six numbers express that, and every one of them is a `MenuLook`
// constant rather than a literal, because the difference between "lit" and "giving off light" is
// exactly the difference between one set of these and another.
//
//   film_gain   the film's own contrast. UP on dark: nothing is washing it out any more.
//   base_lift   the flat white the film floats on. DOWN on dark: on the light frame it existed to
//               carry the sky's brightness through a translucent object, and there is no longer a
//               sky brightness to carry.
//   spec_gain   the two speculars, which are the part the eye reads as a wet surface.
//   alpha_gain  overall presence.
//   spec_alpha  how much the speculars contribute OPACITY as well as colour — a highlight on a
//               transparent film has to make the film locally less transparent, or it reads as
//               something behind the bubble rather than on it.
//   halo_gain   the term that actually says "emissive": a faint bloom OUTSIDE the disc. Everything
//               above only makes the bubble brighter; this is the one that makes the frame AROUND
//               it brighter, which is what a light source does and a lit object does not.
uniform float film_gain : hint_range(0.0, 2.0, 0.005) = 0.94;
uniform float base_lift : hint_range(0.0, 1.0, 0.005) = 0.118;
uniform float spec_gain : hint_range(0.0, 2.0, 0.005) = 0.922;
uniform float alpha_gain : hint_range(0.0, 4.0, 0.005) = 1.25;
uniform float spec_alpha : hint_range(0.0, 2.0, 0.005) = 0.60;
uniform float halo_gain : hint_range(0.0, 1.0, 0.005) = 0.16;

// How far past the rim the halo is evaluated, in units of r-squared. Past this the exponential is
// under one code value and the pixels are wasted work.
const float HALO_EXTENT = 2.5;

// PI and TAU are Godot shading-language built-ins; declaring them again is a compile error, and
// the message ("Redefinition of 'PI'") does not name the file it came from.
//
// Refractive index of a soap film. 1.34 is water plus surfactant, and it is the value the
// in-world bubble shader uses.
const float FILM_IOR = 1.34;

// Two-beam interference at the three sRGB primaries. The pi offset is the half-wave shift a
// reflection off the denser medium picks up; without it the film's thinnest point comes out
// white instead of black and the whole ramp is off by one band.
vec3 interference(float opd) {
	const vec3 lambda = vec3(612.0, 549.0, 465.0);
	return 0.5 + 0.5 * cos(TAU * opd / lambda + PI);
}

// One bubble, returned STRAIGHT (not premultiplied) so the caller can composite in order.
//   c        centre, width-normalised
//   r        radius, in width units
//   ph       this bubble's phase (the shared phase plus its own offset, so they are not in step)
//   thick    nominal film thickness in nm at the middle of the sphere
//   strength overall opacity multiplier
//   dist     a distance term: bubbles further back are fainter. Purely a look dial.
vec4 bubble(vec2 p, vec2 c, float r, float ph, float thick, float strength, float dist) {
	vec2 n = (p - c) / r;
	float rr = dot(n, n);
	// Not `rr > 1.0`. The halo lives OUTSIDE the disc, so the early-out has to move out with it;
	// cutting at the rim is what made the light-frame version incapable of emitting anything.
	if (rr > HALO_EXTENT) {
		return vec4(0.0);
	}

	// Cosine of the view angle against the sphere's surface. 1 at the centre of the disc, 0 at
	// the rim — this single term produces both the rim brightening and the colour crowding.
	float cos_t = clamp(sqrt(max(1.0 - rr, 0.0)), 1e-4, 1.0);

	// Thickness varies over the film: it drains downward under gravity (the 0.55 + 0.85 term),
	// it is stirred by surface currents (the swirl), and it thins overall as the bubble ages
	// (the phase term). A film of one uniform thickness is one flat colour and reads as a
	// coloured ball, not as a bubble.
	float swirl = sin(n.x * 5.1 + ph * 2.0) * cos(n.y * 4.3 - ph * 1.5)
	            + 0.5 * sin((n.x + n.y) * 8.0 + ph * 3.1);
	float d_nm = thick * (0.55 + 0.85 * (0.5 - n.y * 0.5)) + 105.0 * swirl + 60.0 * ph;

	// Optical path difference through the film, with the refracted angle taken properly.
	float sin_i2 = (1.0 - cos_t * cos_t) / (FILM_IOR * FILM_IOR);
	float cos_r = sqrt(clamp(1.0 - sin_i2, 0.0, 1.0));
	vec3 film = interference(2.0 * FILM_IOR * d_nm * cos_r);

	// Pulled 8% toward its own mid-point. Full-contrast interference is a rainbow; the brief for
	// every iridescent surface in this build is a sheen.
	film = 0.5 + (film - 0.5) * 0.92;

	// Schlick-ish Fresnel. A soap film is almost invisible face-on and almost a mirror at the
	// rim, and that ratio is most of what says "bubble" before any colour arrives.
	float fres = 0.028 + 0.972 * pow(1.0 - cos_t, 4.2);
	float a = clamp(0.055 + fres * 1.5, 0.0, 0.9) * strength * exp(-0.012 * dist);

	// Two speculars: a tight key upper-left and a broad fill lower-right. Both anchored to the
	// disc, never rotating — a highlight that travels reads as a turning sticker.
	float spec = exp(-(pow(n.x + 0.42, 2.0) + pow(n.y + 0.46, 2.0)) / 0.016) * 0.95
	           + exp(-(pow(n.x - 0.50, 2.0) + pow(n.y - 0.34, 2.0)) / 0.055) * 0.22;

	vec3 col = film * film_gain + vec3(base_lift) + vec3(spec * spec_gain);

	// Inside the disc the film is the surface; outside it, only the halo exists. Branchless because
	// every fragment of this pass evaluates all five bubbles regardless.
	float inside = step(rr, 1.0);
	float halo = exp(-max(rr - 1.0, 0.0) * 5.0) * (1.0 - inside);
	a = clamp(a * alpha_gain + spec * spec_alpha, 0.0, 1.0) * inside + halo * halo_gain;
	return vec4(col, a);
}

// `src` over `dst`, both premultiplied.
vec4 over(vec4 src_pm, vec4 dst_pm) {
	return src_pm + dst_pm * (1.0 - src_pm.a);
}

vec4 pm(vec4 straight) {
	return vec4(straight.rgb * straight.a, straight.a);
}

void fragment() {
	vec2 p = vec2(UV.x, UV.y * aspect);
	vec4 acc = vec4(0.0);

	// The hero goes down first and the rest sit over it, which is the order the approved previz
	// frame composites in. Centres and radii are the agreed numbers: hero at x 0.702 of the
	// width, radius 0.14625 of the width (234 px in the 1600-wide previz frame), distance term 11.
	acc = over(pm(bubble(p, vec2(0.702, 0.437 * aspect), 0.146250, phase,        335.0, 0.94, 11.0)), acc);
	acc = over(pm(bubble(p, vec2(0.845, 0.300 * aspect), 0.057500, phase + 0.7,  300.0, 0.85, 16.0)), acc);
	acc = over(pm(bubble(p, vec2(0.392, 0.715 * aspect), 0.040000, phase + 1.6,  360.0, 0.75, 22.0)), acc);
	acc = over(pm(bubble(p, vec2(0.925, 0.615 * aspect), 0.025000, phase + 2.4,  280.0, 0.65, 30.0)), acc);
	acc = over(pm(bubble(p, vec2(0.300, 0.235 * aspect), 0.020625, phase + 3.1,  320.0, 0.60, 34.0)), acc);

	// NO TRANSFER-FUNCTION CONVERSION HERE, DELIBERATELY. Godot's 2D pipeline composites in the
	// render target's own (sRGB-encoded, 8-bit) space, and the world layer this sits over arrives
	// as an already-tonemapped sRGB viewport texture. Converting to linear would darken the film
	// by roughly a stop against a background that was never converted. It also happens to be the
	// space the approved previz composited in, so the frames are comparable rather than merely
	// similar. The 3D half of the screen — where lighting is genuinely linear — does its own
	// conversion, in `ui_menu_world.gdshader`.
	COLOR = acc * layer_alpha;
}
