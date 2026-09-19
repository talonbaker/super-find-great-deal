shader_type canvas_item;
render_mode blend_add, unshaded;

// =================================================================================================
// THE NIGHT LAYER — a campfire off-frame and a sky with stars in it. (MENU-2, 2026-08-29.)
// =================================================================================================
//
// Two additive terms, one fullscreen pass, composited OVER the blurred world and UNDER the bubbles.
// They are together in one shader because they are the same operation on the same layer at the same
// moment, and two fullscreen passes to add two numbers is two passes too many.
//
// -------------------------------------------------------------------------------------------------
// 1. THE CAMPFIRE, low-left and off the edge of the frame.
// -------------------------------------------------------------------------------------------------
// One wide gaussian in warm sRGB, centred outside the picture. It is what makes the frame a PLACE
// rather than a void: without it the night is an evenly graded backdrop that could be anywhere, and
// with it there is something warm just past the edge of the shot that the near ground is catching.
// That is the premise of the whole game stated in one soft shape.
//
// IT IS THE ONLY WARM COLOUR LEFT IN THE DESIGN, AND THAT IS DELIBERATE. Talon removed the orange
// from the primary BUTTON on 2026-08-29 — a UI element — and the accent went cool in answer. Warm
// light stays where it was never the complaint: inside the picture, unfocusable, un-pressable, and
// soft enough that it has no edge to clash with anything.
//
// -------------------------------------------------------------------------------------------------
// 2. THE STARS, and they go on AFTER the world blur.
// -------------------------------------------------------------------------------------------------
// Painted before it, an 18 px gaussian erases them completely. Stars are the one backdrop element
// on this screen that stays sharp, and the sharpness is not incidental: it is what makes them read
// as distance rather than as dust on the lens, against a world that has been blurred to nothing.
//
// A HASHED GRID, NOT A LIST. One star per cell, positioned inside its own cell by the hash, with
// the 3x3 neighbourhood tested so a star near a cell boundary is not clipped by it. That gives a
// fixed, reproducible field with no vertex data and no texture, and — because the cell is a
// FRACTION OF THE FRAME WIDTH — the same picture at every resolution rather than the same star
// count spread thinner.
//
// NO TRANSFER-FUNCTION CONVERSION, deliberately, for the same reason `ui_menu_bubbles.gdshader`
// skips it: Godot's 2D pipeline composites in the render target's own sRGB-encoded space, the world
// layer underneath arrives already tonemapped and encoded, and converting here would put this layer
// in a different space from the one it is being added to.

// ---------------------------------------------------------------------------------- the campfire
uniform vec2 glow_centre = vec2(0.10, 0.86);
uniform vec2 glow_reach = vec2(0.30, 0.34);
uniform vec3 glow_srgb = vec3(0.455, 0.227, 0.078);

// -------------------------------------------------------------------------------------- the stars
uniform vec2 frame_size = vec2(1920.0, 1080.0);

// Cell edge in pixels. The caller derives it from the frame width so the field scales with the
// picture; see `MenuLook.StarCellFraction`.
uniform float star_cell_px = 46.8;

// How far down the frame stars reach, and the exponent on the fade. Nothing below this: a star
// field that runs into the horizon reads as noise over the ground.
uniform float star_field_height : hint_range(0.05, 1.0, 0.005) = 0.55;
uniform float star_fade_power : hint_range(0.1, 4.0, 0.01) = 0.8;

uniform float star_radius_min_px = 0.6;
uniform float star_radius_max_px = 1.8;
uniform float star_brightness_min : hint_range(0.0, 1.0, 0.005) = 0.30;
uniform float star_brightness_max : hint_range(0.0, 2.0, 0.005) = 1.00;
uniform float star_amplitude : hint_range(0.0, 2.0, 0.005) = 0.922;
uniform vec3 star_tint = vec3(0.88, 0.92, 1.0);

// Fixed, like the block scatter's seed: a title screen that reshuffles its own sky every launch
// cannot be compared against a capture.
uniform float star_seed = 19.0;

// Three uncorrelated values from one cell coordinate. The standard Dave Hoskins hash — cheap, and
// notably free of the axis-aligned banding a `fract(sin(dot(...)))` pair produces on a grid, which
// on a star field shows up immediately as rows.
vec3 hash33(vec3 p3) {
	p3 = fract(p3 * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yxz + 33.33);
	return fract((p3.xxy + p3.yxx) * p3.zyx);
}

void fragment() {
	vec2 px = UV * frame_size;

	// -- the campfire -----------------------------------------------------------------------------
	vec2 g = (UV - glow_centre) / max(glow_reach, vec2(1e-4));
	vec3 lit = glow_srgb * exp(-dot(g, g));

	// -- the stars --------------------------------------------------------------------------------
	float band = star_field_height * frame_size.y;
	float fade = pow(clamp(1.0 - px.y / max(band, 1e-4), 0.0, 1.0), star_fade_power);

	vec3 stars = vec3(0.0);
	if (fade > 0.0) {
		float cell = max(star_cell_px, 1.0);
		vec2 home = floor(px / cell);
		for (int oy = -1; oy <= 1; oy++) {
			for (int ox = -1; ox <= 1; ox++) {
				vec2 c = home + vec2(float(ox), float(oy));
				vec3 h = hash33(vec3(c, star_seed));
				vec3 h2 = hash33(vec3(c, star_seed + 7.0));

				vec2 star_px = (c + h.xy) * cell;
				// Its own fade, from its own row — not the fragment's. Otherwise a star straddling
				// the band's edge is brighter on one side of itself than the other.
				float star_fade = pow(clamp(1.0 - star_px.y / max(band, 1e-4), 0.0, 1.0), star_fade_power);
				if (star_fade <= 0.0) {
					continue;
				}

				float r = mix(star_radius_min_px, star_radius_max_px, h2.x);
				float b = mix(star_brightness_min, star_brightness_max, h.z) * star_fade;
				vec2 d = px - star_px;
				stars += star_tint * (b * star_amplitude * exp(-dot(d, d) / (2.0 * r * r)));
			}
		}
	}

	COLOR = vec4(lit + stars, 1.0);
}
