shader_type spatial;
// DIVEPUNK — hazard telegraphing (M4 aesthetic pass). Shared by the static obstacles (amber) and
// the moving traffic (magenta) via uniforms — one shader, two looks. A slow emissive pulse plus a
// fresnel rim make hazards read clearly at speed and pop against the cool city (spec §5.11,
// "fairness via telegraphing"). pulse_on = 0 gives a steady emissive (the pre-M4 look).

uniform vec3 base_color : source_color = vec3(0.6, 0.25, 0.05);
uniform vec3 emission_color : source_color = vec3(1.0, 0.45, 0.1);
uniform float emission_energy = 2.6;
uniform float pulse_speed = 3.5;
uniform float pulse_depth = 0.35;     // 0..1 pulse amplitude
uniform float fresnel_strength = 1.3; // rim-light glow on the silhouette edges
uniform float pulse_on = 1.0;

void fragment() {
	// cos() pulse in [1 - pulse_depth .. 1], so the hazard "breathes" between dim and full.
	float pulse = 1.0 - pulse_on * pulse_depth * (0.5 - 0.5 * cos(TIME * pulse_speed));
	// NORMAL and VIEW are both view-space here, so this fresnel rims the silhouette toward the camera.
	float fres = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), 3.0);
	ALBEDO = base_color;
	METALLIC = 0.0;
	ROUGHNESS = 0.5;
	EMISSION = emission_color * (emission_energy * pulse + fresnel_strength * fres);
}
