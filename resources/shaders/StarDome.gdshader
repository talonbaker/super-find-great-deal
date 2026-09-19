// Star dome for OutdoorAtmosphere — procedural hash stars on an inverted sphere, no
// textures (ART-BIBLE §4.3 respected: fully procedural, flat-colour world untouched).
// Additive and driven by star_energy (0 = invisible by day); OutdoorAtmosphere sets the
// uniform every frame from the phase-derived StarMul curve, so star visibility is as
// deterministic across clients as the rest of the sky.
shader_type spatial;
render_mode unshaded, blend_add, cull_front, depth_draw_never, shadows_disabled, fog_disabled;

uniform float star_energy : hint_range(0.0, 1.0) = 0.0;

varying vec3 dome_dir;

// Cheap 3D hash (Dave Hoskins style) — one candidate star per direction cell.
float hash13(vec3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return fract((p.x + p.y) * p.z);
}

void vertex() {
    dome_dir = VERTEX; // local-space direction from dome center; dome is a sphere at origin.
}

void fragment() {
    if (star_energy <= 0.001) {
        ALBEDO = vec3(0.0); // blend_add: black adds nothing — invisible by day.
    } else {
        vec3 dir = normalize(dome_dir);
        float cells = 90.0; // star density dial.
        vec3 cell = floor(dir * cells);
        float h = hash13(cell);
        vec3 center = normalize((cell + 0.5) / cells);
        float d = length(dir - center);
        float size = 0.0015 + 0.0025 * fract(h * 7.0);
        float star = (h > 0.90) ? smoothstep(size, size * 0.25, d) : 0.0;
        // Slow twinkle, per-star phase offset; subtle, never a startle.
        float tw = 0.75 + 0.25 * sin(TIME * (0.8 + fract(h * 13.0) * 2.0) + h * 40.0);
        // Faint horizon fade so stars don't pop at the fog line.
        float horizon = smoothstep(0.0, 0.15, dir.y);
        // Unshaded spatial shaders output ALBEDO only (EMISSION is ignored); with
        // blend_add this IS the additive star light.
        ALBEDO = vec3(0.85, 0.90, 1.0) * star * tw * horizon * star_energy;
    }
}
