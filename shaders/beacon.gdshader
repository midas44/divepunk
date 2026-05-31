shader_type spatial;
// DIVEPUNK — rooftop aviation beacon (M4 view pass). A small sphere on the tallest roofs / mast tips
// that blinks deep red, like the obstruction lights on real skyscrapers. Drawn by a per-chunk
// MultiMesh (one draw call per chunk) and phase-offset per instance from its WORLD position, so the
// skyline blinks out of sync instead of in lockstep. Purely emissive — it reads as a light source,
// not a lit surface, and is bright enough to bloom through the haze.

uniform vec3 beacon_color : source_color = vec3(1.0, 0.05, 0.04);
uniform float blink_speed = 1.7;       // radians/sec of the blink cycle
uniform float emission_energy = 8.5;   // hot enough to clear the glow HDR threshold + bloom at distance
uniform float min_glow = 0.30;         // always-on red floor so the beacon reads even between flashes

varying vec3 v_origin;

void vertex() {
	// Instance world origin — a stable per-beacon seed for the blink phase.
	v_origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
}

void fragment() {
	float phase = fract(sin(dot(floor(v_origin.xz), vec2(12.9898, 78.233))) * 43758.5453) * 6.2831853;
	// Mostly dark with a brief bright flash each cycle (the classic obstruction-light blink).
	float s = 0.5 + 0.5 * sin(TIME * blink_speed + phase);
	float flash = smoothstep(0.55, 0.95, s);
	float glow = mix(min_glow, 1.0, flash);
	ALBEDO = vec3(0.0);
	METALLIC = 0.0;
	ROUGHNESS = 1.0;
	EMISSION = beacon_color * emission_energy * glow;
}
