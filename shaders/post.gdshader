shader_type canvas_item;
// DIVEPUNK — fullscreen post pass (M4, spec §7.8). Radial chromatic aberration that ramps with
// speed / boost, plus an always-on mood vignette. Reads the already-composited frame via
// hint_screen_texture (Godot inserts the back-buffer copy automatically) and re-presents it, so it
// sits ABOVE the 3D + speed lines but BELOW the HUD (which must stay crisp).

uniform sampler2D screen_tex : hint_screen_texture, filter_linear, repeat_disable;
uniform float aberration = 0.0;     // 0..~1.6, radial RGB split (speed ratio + boost)
uniform float vignette = 0.35;      // edge darkening strength

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 d = uv - vec2(0.5);
	vec2 dir = d * dot(d, d);             // grows toward the edges (zero at centre)
	float amt = aberration * 0.02;
	float cr = texture(screen_tex, uv - dir * amt).r;
	float cg = texture(screen_tex, uv).g;
	float cb = texture(screen_tex, uv + dir * amt).b;
	vec3 col = vec3(cr, cg, cb);
	float vig = smoothstep(0.85, 0.2, length(d));   // 1 at centre -> 0 at corners
	col *= mix(1.0, vig, vignette);
	COLOR = vec4(col, 1.0);
}
