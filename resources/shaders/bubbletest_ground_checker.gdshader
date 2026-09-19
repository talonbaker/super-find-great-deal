shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// ============================================================================================
// THE BUBBLE-TEST GROUND: A SPEEDOMETER, NOT A PATTERN.
//
// Talon, 2026-08-28 (N4): "update the ground plane, just what the different squares of level
// are sitting on, with a subtle checker squares pattern. This will help the player realize
// their movement and how fast."
//
// WHY A SHADER AND NOT A TEXTURE. The cells are WORLD-space, so one cell is one metre on every
// plate regardless of how that plate's mesh is scaled or subdivided. A UV checker stretches
// with the mesh and therefore measures nothing — and this pattern exists to be measured
// against: crossing two cells is two metres, and the rate at which cells pass under you IS the
// speed readout. It is also cheaper than sampling an image: a floor(), a mod() and a mix().
//
// WHY IT ALSO EARNS ITS PLACE IN DARK-1'S GRADE. This pass takes the ground albedo down 44 % in
// linear luminance, and the failure mode of a darker ground is that a large flat plate becomes
// a dead unreadable mass — the exact complaint ("clinical and sterile") arriving from the other
// end. A 1 m modulation gives the eye local contrast that survives the drop, and it gives the
// ground-vs-neutral pair a SECOND discriminator (patterned vs plain) on top of the value ladder
// BT-9b built, which matters because a uniform darkening necessarily compresses a value ladder
// in L* even when it preserves its luminance ratios exactly.
//
// AXIS PICKED FROM THE WORLD NORMAL — the idiom is MovementCourse.CheckerMaterial's, deliberately
// shared so the dev playground and this world read as one language. Floors project on XZ, walls
// on XY or ZY, so the squares stay square on GreenHills' slopes instead of smearing to stripes.
//
// SUBTLE BY CONSTRUCTION: the contrast is a +/- fraction of the plate's OWN colour, never a
// black-and-white grid. The palette still carries the section's meaning; this only carries
// motion. `strength` is the taste knob and 0.09 is where a run reads without the grid becoming
// the subject.
// ============================================================================================

uniform vec3 base_color : source_color = vec3(0.5);
uniform float cell = 1.0;
uniform float strength : hint_range(0.0, 0.5) = 0.09;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.92;
uniform float metallic_specular : hint_range(0.0, 1.0) = 0.35;

varying vec3 world_pos;
varying vec3 world_nrm;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_nrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 n = abs(world_nrm);
	vec2 uv = n.y >= max(n.x, n.z)
		? world_pos.xz
		: (n.x >= n.z ? world_pos.zy : world_pos.xy);
	vec2 c = floor(uv / max(cell, 0.001));
	float check = mod(c.x + c.y, 2.0);
	// +/- strength around the authored colour, so the plate's MEAN albedo is unchanged and the
	// value ladder the .tres files state still holds as an average.
	ALBEDO = base_color * (1.0 + (check * 2.0 - 1.0) * strength);
	ROUGHNESS = roughness_value;
	SPECULAR = metallic_specular;
	METALLIC = 0.0;
}
