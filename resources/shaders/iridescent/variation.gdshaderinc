// PER-INSTANCE VARIATION FROM WORLD POSITION — no per-object setup at all.
//
// The problem: a field of beetles, bubbles or wings all rendered with one material comes out
// IDENTICAL, and a hundred identical iridescent props reads as wallpaper rather than as a hundred
// things. Every one of them needs its own colour.
//
// The usual fixes all cost something:
//   * a material per object -- kills batching, allocates per prop, and somebody has to author them
//   * an `instance uniform` per object -- cheap at runtime, but something must WRITE it, so every
//     spawn path in the game has to remember to, and the one that forgets ships a grey field
//   * a vertex-colour or UV2 channel -- needs authored meshes and an art pipeline
//
// This costs none of them. `MODEL_MATRIX[3].xyz` is the object's world position, which every
// instance already has and no two co-located objects share. Hashing it gives a stable per-object
// random number for about six instructions, with NOTHING to set up, nothing to author, and no way
// to forget.
//
// IT IS STABLE, WHICH IS THE PROPERTY THAT MATTERS. The same object at the same place hashes to the
// same value every frame, so its colour does not flicker. Move the object and its colour changes --
// which is the one real caveat, and it is the right trade for scattered static props. Anything that
// travels a long way and must keep its colour should use an instance uniform instead and can, since
// this only shifts a value the shader already had.

group_uniforms Variation;
// How far apart instances are allowed to land, in the units of whatever this modulates. 0 is an
// exact no-op, so this module costs one multiply until somebody asks for it.
uniform float variation_amount : hint_range(0.0, 1.0, 0.005) = 0.0;
// Scrambles which position maps to which value, for when a particular arrangement lands badly --
// two neighbours the same colour, or a row that happens to read as a gradient.
uniform float variation_seed : hint_range(0.0, 100.0, 0.01) = 0.0;

// Signed, roughly -1..1, stable per object, free of setup.
float fx_pos_variation(mat4 model) {
	if (variation_amount <= 0.0) {
		return 0.0;
	}
	vec3 p = model[3].xyz + vec3(variation_seed);
	// Quantised before hashing so that objects which are very close together still separate
	// cleanly, rather than hashing to nearly the same value and reading as a pair.
	p = floor(p * 7.31);
	float h = fract(sin(dot(p, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
	return (h * 2.0 - 1.0) * variation_amount;
}
