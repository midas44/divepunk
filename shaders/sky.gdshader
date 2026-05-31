shader_type sky;

// ── DIVEPUNK neon-night procedural sky (M4 view pass) ──
// Dark zenith, a glowing neon band on the horizon (distant-city glow), a drifting
// cloud layer whose undersides catch the neon, and optional faint stars. Gives the
// long view some "landscape" above the rooftops. Built in code by game.gd; a few
// uniforms are driven from settings.cfg [fx]. Procedural noise only — no textures.

// Colours are deliberately bright here: ACES tonemapping (white point 6.0) + the eye adapting to
// the neon city crushes dim values to black, so a "night sky" still needs healthy absolute values.
// sky_energy is the master brightness multiplier (driven from settings.cfg [fx] sky_energy).
uniform vec3 zenith_color : source_color = vec3(0.030, 0.040, 0.110);   // overhead — visible deep night-blue, NOT black
uniform vec3 horizon_color : source_color = vec3(0.20, 0.10, 0.34);
uniform vec3 glow_color : source_color = vec3(0.55, 0.22, 0.80);   // neon band sitting on the horizon (distant-city glow)
uniform float horizon_falloff = 3.5;                              // lower = the glow spreads higher up the dome (more visible above rooftops)
uniform float sky_energy = 1.5;

uniform vec3 cloud_color : source_color = vec3(0.22, 0.20, 0.34);
uniform vec3 cloud_lit_color : source_color = vec3(0.60, 0.32, 0.72);  // undersides catching the neon
uniform float cloud_coverage = 0.5;   // 0 = clear, 1 = overcast
uniform float cloud_density = 1.0;
uniform float cloud_scale = 2.2;
uniform float cloud_speed = 0.006;
uniform float clouds_on = 1.0;

uniform float star_amount = 0.0;      // 0 = off; subtle sparkle when raised
uniform float star_density = 220.0;

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
	float v = 0.0;
	float amp = 0.5;
	for (int i = 0; i < 5; i++) {
		v += amp * vnoise(p);
		p *= 2.02;
		amp *= 0.5;
	}
	return v;
}

void sky() {
	vec3 dir = EYEDIR;
	float up = dir.y;

	// Vertical gradient: neon horizon -> dark zenith.
	float t = clamp(up, 0.0, 1.0);
	vec3 col = mix(horizon_color, zenith_color, pow(t, 0.45));

	// Neon band hugging the horizon (the distant-city glow).
	float band = exp(-horizon_falloff * abs(up));
	col += glow_color * band;

	// Drifting clouds in the upper hemisphere; project the view dir onto a sky plane so they
	// spread toward the horizon and thin out overhead.
	if (clouds_on > 0.5 && up > 0.0) {
		vec2 uv = (dir.xz / (up + 0.12)) * cloud_scale;
		uv += vec2(TIME * cloud_speed, TIME * cloud_speed * 0.6);
		float n = fbm(uv);
		float mask = smoothstep(1.0 - cloud_coverage, 1.0 - cloud_coverage + 0.35, n) * cloud_density;
		mask *= smoothstep(0.02, 0.20, up);   // fade clouds in just above the horizon line
		vec3 cloud = mix(cloud_color, cloud_lit_color, band);  // undersides near the horizon catch the glow
		col = mix(col, cloud, clamp(mask, 0.0, 1.0));
	}

	// Faint stars in the clear upper sky (off by default).
	if (star_amount > 0.001 && up > 0.05) {
		vec2 suv = floor((dir.xz / (up + 0.05)) * star_density);
		float s = hash21(suv);
		col += vec3(step(0.997, s) * star_amount * smoothstep(0.1, 0.5, up));
	}

	COLOR = max(col, vec3(0.0)) * sky_energy;
}
