shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

// =================================================================================================
// BLOCK RIMS — how section colour survives a dark frame. (MENU-2, 2026-08-29.)
// =================================================================================================
//
// A thin sliver laid along a block's top edges, drawn in the block's own colour at FULL saturation
// and taking none of the shading, none of the key and none of the haze the block itself takes.
//
// WHY THIS EXISTS AT ALL. On a dark base a coloured fill has two options and both are losses: stay
// dark, and stop being identifiable — nobody can say "the green one" about two adjacent charcoals;
// or get lifted, and drag the whole palette back toward the light frame that was rejected. The rim
// is the third option. The body falls to silhouette, one edge states the colour undimmed, and the
// TOTAL quantity of saturated pixels on screen goes down rather than up, which is what makes it a
// darkening move rather than a brightening one dressed up.
//
// The same technique DARK-1 uses for the playtest scene's zone sections. It was measured here
// first; the menu does not own it.
//
// WHY IT IS GEOMETRY AND NOT A SHADER TERM ON THE BLOCK. A rim is an EDGE, and a fragment shader
// on a box has no cheap, exact way to know it is within n screen pixels of one — the box's own UV
// layout differs per face and any threshold on it is a guess that only a render can falsify. A
// sliver is exact, costs one more draw of a mesh that is already in memory, and its thickness can
// be set per block so the rim is a constant number of SCREEN pixels at every distance. See
// `MenuBackdrop.RimThickness`.
//
// `depth_draw_never` with the depth TEST still on: a nearer opaque block occludes the rims behind
// it correctly, and no rim can ever occlude anything.
//
// COLOURS ARRIVE IN sRGB AND ARE CONVERTED ON THE LAST LINE, exactly as `ui_menu_world.gdshader`
// does it and for the same reason — the whole frame is composited in sRGB byte space.

// rgb: the rim's colour in sRGB 0..1.  a: its opacity, already faded for distance by the caller.
// One instance uniform rather than two, because Godot's instance-uniform budget is small and a
// vec4 costs the same slot as a float.
instance uniform vec4 rim_srgb = vec4(1.0);

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + vec3(0.055)) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void fragment() {
	ALBEDO = srgb_to_linear(clamp(rim_srgb.rgb, 0.0, 1.0));
	ALPHA = clamp(rim_srgb.a, 0.0, 1.0);
}
