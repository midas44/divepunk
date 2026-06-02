shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// DIVEPUNK dusk ocean — a flat plane at world Y=0. Fragment-only (no vertex displacement): world-anchored
// FBM ripples perturb the normal; a fresnel rim brightens at grazing angles (catches the horizon glow); a
// depth-tinted base reads as deep water. Visual-only (no collision). Task 6 retunes for synthwave dusk.

uniform vec3 deep_color : source_color = vec3(0.04, 0.05, 0.11);     // deep dusk teal-violet body
uniform vec3 shallow_color : source_color = vec3(0.14, 0.12, 0.20);  // shallows lit toward mauve
uniform vec3 fresnel_color : source_color = vec3(1.0, 0.50, 0.42);   // warm gold-pink rim catching the sunset at grazing angles
uniform float fresnel_power = 4.0;
uniform float water_roughness = 0.08;     // low -> SSR/specular catches the neon
uniform float ripple_scale = 0.015;       // world-space ripple frequency (1/m)
uniform float ripple_speed = 0.04;
uniform float ripple_strength = 0.25;     // normal-perturbation amount
uniform float water_energy = 1.0;

varying vec3 world_pos;

float hash21(vec2 p){ p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float vnoise(vec2 p){ vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i), b = hash21(i + vec2(1,0)), c = hash21(i + vec2(0,1)), d = hash21(i + vec2(1,1));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y); }
float fbm(vec2 p){ float v = 0.0, amp = 0.5; for (int k = 0; k < 4; k++){ v += amp * vnoise(p); p *= 2.02; amp *= 0.5; } return v; }
float wave(vec2 uv, float t){ return fbm(uv + vec2(t, t * 0.6)) + fbm(uv * 2.3 - vec2(t * 0.8, t)); }

void vertex(){ world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }

void fragment(){
    vec2 uv = world_pos.xz * ripple_scale;
    float t = TIME * ripple_speed;
    float h  = wave(uv, t);
    float hx = wave(uv + vec2(0.06, 0.0), t);
    float hz = wave(uv + vec2(0.0, 0.06), t);
    vec3 n = normalize(vec3((h - hx) * ripple_strength, 1.0, (h - hz) * ripple_strength));
    NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

    float fres = pow(1.0 - clamp(dot(normalize(VIEW), NORMAL), 0.0, 1.0), fresnel_power);
    ALBEDO = mix(deep_color, shallow_color, clamp(h * 0.5, 0.0, 1.0)) * water_energy;
    EMISSION = fresnel_color * fres * water_energy;   // grazing-angle glow toward the horizon
    ROUGHNESS = water_roughness;
    METALLIC = 0.0;
    SPECULAR = 0.5;
}
