shader_type canvas_item;
render_mode blend_premul_alpha, unshaded;

// =================================================================================================
// SEPARABLE GAUSSIAN — the one blur the title screen uses, run twice per layer. (MENU-1, 2026-08-28.)
// =================================================================================================
//
// Talon, 2026-08-28: "blur the bubble and then HEAVILY BLUR the color blocks ... It's overall too
// colourful." The agreed numbers are 30 px on the world and 9 px on the bubbles, at 1920 x 1080.
//
// WHY A SHADER AND NOT DEPTH OF FIELD. Godot's `CameraAttributesPractical` DOF tops out at a few
// pixels of circle-of-confusion; 30 px is an order of magnitude past it. More importantly the
// three layers must blur INDEPENDENTLY — world heavy, bubbles light, UI never — and one camera
// has one DOF. So each layer renders to its own SubViewport and this shader is applied twice
// (horizontal, then vertical) on the way out.
//
// WHY SEPARABLE. A 2D gaussian of radius r costs (2r+1)^2 taps; two 1D passes cost 2*(2r+1) and
// are mathematically identical for a gaussian. At the world layer's sigma that is 47 taps instead
// of 2209.
//
// WHY `blend_premul_alpha`. The bubble layer is drawn into a transparent SubViewport, and Godot
// stores such a viewport's contents ALREADY PREMULTIPLIED (mix-blending onto a cleared
// (0,0,0,0) buffer leaves rgb*a in the colour channels). Blurring premultiplied RGBA linearly is
// the correct operation; blurring straight alpha pulls transparent black inward and leaves a dark
// ring at every bubble rim. Premultiplied blending is also a no-op for the opaque world layer,
// where alpha is 1 everywhere — so one render_mode serves both chains.

// Which axis this pass runs along: (1,0) horizontal, (0,1) vertical.
uniform vec2 direction = vec2(1.0, 0.0);

// Standard deviation, in pixels OF THE PROXY the layer renders at — not of the final frame. The
// caller scales: a 30 px blur at 1920 wide is 7.5 px on a 480-wide proxy.
uniform float sigma_px : hint_range(0.1, 32.0, 0.05) = 4.0;

// Proxy dimensions, so one texel can be stepped exactly. Passing this rather than reading
// TEXTURE_PIXEL_SIZE keeps the shader honest when the caller resizes the chain.
uniform vec2 source_size = vec2(480.0, 270.0);

// Compile-time tap ceiling. 24 covers 3 sigma at sigma = 8, which is the largest the world layer
// asks for at its proxy size; the loop breaks out early for anything smaller.
const int MAX_HALF = 24;

void fragment() {
	vec2 step_uv = direction / source_size;
	float inv_two_sigma_sq = 1.0 / (2.0 * sigma_px * sigma_px);
	float reach = sigma_px * 3.0;

	vec4 sum = texture(TEXTURE, UV);
	float wsum = 1.0;

	for (int i = 1; i <= MAX_HALF; i++) {
		float fi = float(i);
		if (fi > reach) {
			break;
		}
		float w = exp(-fi * fi * inv_two_sigma_sq);
		// Clamped, so the kernel edge-extends rather than wrapping or sampling the void. This is
		// what PIL's GaussianBlur does, and it is why the previz frame has no dark border.
		sum += texture(TEXTURE, clamp(UV + step_uv * fi, vec2(0.0), vec2(1.0))) * w;
		sum += texture(TEXTURE, clamp(UV - step_uv * fi, vec2(0.0), vec2(1.0))) * w;
		wsum += 2.0 * w;
	}

	COLOR = sum / wsum;
}
