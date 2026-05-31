shader_type canvas_item;
render_mode blend_add;
// DIVEPUNK — fullscreen speed lines (M4, spec §7.8 "feels fast"). Radial streaks that rush outward
// from a clear focal point in the screen centre, intensifying with `strength` (the ship's speed
// ratio). Additive so the streaks read as light and bloom. Drawn on a fullscreen ColorRect.

uniform float strength = 0.0;                       // 0..1, fed from the ship's speed ratio
uniform vec4 line_color : source_color = vec4(0.62, 0.86, 1.0, 1.0);
uniform float line_count = 90.0;                    // number of radial streaks
uniform float inner_radius = 0.16;                  // clear zone around the focal point
uniform float flow_speed = 2.5;                     // how fast streaks rush outward

float hash(float x) { return fract(sin(x * 127.1) * 43758.5453); }

void fragment() {
	vec2 d = UV - vec2(0.5);
	float r = length(d);
	float a = atan(d.y, d.x);
	float idx = floor((a / 6.2831853 + 0.5) * line_count);
	float rnd = hash(idx);
	float comb = pow(0.5 + 0.5 * sin(a * line_count), 6.0);        // sharp angular comb of streaks
	float flow = fract(r * 3.0 - TIME * (flow_speed + rnd * 2.0) - rnd * 10.0);
	float radial = smoothstep(inner_radius, 0.75, r);              // fade in away from the centre
	float intensity = comb * radial * smoothstep(0.2, 1.0, flow) * strength;
	COLOR = vec4(line_color.rgb * intensity, 1.0);
}
