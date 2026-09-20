shader_type canvas_item;

// Film grain + vignette for the campfire menu (scenes/ui/CampfireMenu.tscn).
//
// One fullscreen CanvasItem pass on a CanvasLayer above the 3D viewport. It samples the
// screen because the grain has to be SIGNED — it must darken as often as it lightens, or it
// lifts the near-black floor the whole scene depends on, and plain alpha blending can only
// ever darken. That costs one backbuffer copy per frame, which is the honest price of the
// effect and is affordable here precisely because a menu renders ~40 objects and nothing
// else. Do not lift this shader into gameplay on the assumption it is free.
//
// ART-BIBLE §7 requires every post effect to be a named, measured choice recorded in the
// style spec. This one is scoped to the menu only and is declared in
// docs/superpowers/specs/2026-08-07-campfire-main-menu-design.md §6.

uniform sampler2D screen_tex : hint_screen_texture, repeat_disable, filter_nearest;

uniform float grain_strength : hint_range(0.0, 0.5) = 0.055;
uniform float grain_scale : hint_range(64.0, 2048.0) = 640.0;
uniform float grain_speed : hint_range(0.0, 60.0) = 14.0;
uniform float vignette_strength : hint_range(0.0, 1.5) = 0.4;
uniform float vignette_softness : hint_range(0.05, 1.5) = 0.55;

// Cheap hash noise. Three-term so successive frames decorrelate — a two-term hash animated
// on TIME crawls in visible diagonal bands instead of shimmering.
float hash13(vec3 p) {
    p = fract(p * vec3(443.897, 441.423, 437.195));
    p += dot(p, p.yzx + 19.19);
    return fract((p.x + p.y) * p.z);
}

void fragment() {
    vec3 src = texture(screen_tex, SCREEN_UV).rgb;

    // Quantise time so the grain steps at a filmic rate rather than smearing at the display
    // refresh: grain that updates every frame at 144 Hz reads as a clean gradient.
    float t = floor(TIME * grain_speed);
    float n = hash13(vec3(floor(SCREEN_UV * grain_scale), t));

    // Signed, and scaled by a luma term so the grain sits in the mid-tones and does not
    // speckle the pure black the treeline depends on staying.
    float luma = dot(src, vec3(0.2126, 0.7152, 0.0722));
    float weight = smoothstep(0.0, 0.22, luma);
    vec3 grained = src + (n - 0.5) * grain_strength * weight;

    // Vignette from the frame centre. smoothstep, not a pow curve — on a near-black
    // background a pow falloff bands visibly.
    float d = length(SCREEN_UV - vec2(0.5));
    float vig = 1.0 - vignette_strength * smoothstep(vignette_softness * 0.5, 0.95, d);

    COLOR = vec4(max(grained * vig, vec3(0.0)), 1.0);
}
