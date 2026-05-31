shader_type spatial;
// DIVEPUNK — procedural neon building (M4 aesthetic pass). ONE shared material, drawn by each
// chunk's MultiMesh, so a whole skyline stays a handful of draw calls. A dark tower body with an
// emissive window grid laid out in WORLD space, so window floors line up across every building AND
// across the box's faces, tinted per-instance by the MultiMesh COLOR. windows_on = 0 falls back to
// a flat, dim emissive body (the pre-M4 look) for performance / preference.

uniform float window_size_v = 4.0;    // vertical metres per window row
uniform float window_size_h = 5.0;    // horizontal metres per window column
uniform float window_margin = 0.16;   // dark mullion around each pane (0..0.5)
uniform float lit_fraction = 0.35;    // fraction of windows that are lit (rest dark)
uniform float emission_boost = 2.0;   // neon emission strength (drives the bloom)
uniform float base_emission = 0.05;   // dim body glow so towers aren't pure black
uniform float windows_on = 1.0;

varying vec3 v_wpos;
varying vec3 v_wnormal;
varying vec3 v_tint;

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

void vertex() {
	v_wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// Use the normal matrix (inverse-transpose of the model basis): under the heavy NON-UNIFORM scale
	// of a tall/thin/tapered tower, transforming the normal by MODEL_MATRIX skews it toward vertical,
	// which made `wallness` below cull the windows off round/tapered buildings. This keeps it correct.
	v_wnormal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
	v_tint = COLOR.rgb;
}

void fragment() {
	// Build a vivid neon from the (dark, low-saturation) instance tint: keep the hue, push the
	// value and saturation so each tower glows in its own colour.
	float mx = max(v_tint.r, max(v_tint.g, v_tint.b));
	vec3 hue = v_tint / max(mx, 1e-4);
	float l = dot(hue, vec3(0.3333));
	vec3 neon = clamp(mix(vec3(l), hue, 1.8), 0.0, 1.0);

	// Window grid in world space. Pick the horizontal axis from which wall this face is (a face
	// looking along X uses world Z for its columns, and vice-versa); world Y is always the rows.
	float horiz = (abs(v_wnormal.x) >= abs(v_wnormal.z)) ? v_wpos.z : v_wpos.x;
	vec2 cell = vec2(horiz / window_size_h, v_wpos.y / window_size_v);
	vec2 id = floor(cell);
	vec2 f = fract(cell);
	vec2 inpane = step(vec2(window_margin), f) * step(f, vec2(1.0 - window_margin));
	float pane = inpane.x * inpane.y;
	float lit = step(1.0 - lit_fraction, hash21(id + vec2(7.0, 3.0)));
	float bright = 0.55 + 0.45 * hash21(id + vec2(19.0, 5.0));
	float wallness = smoothstep(0.35, 0.7, 1.0 - abs(v_wnormal.y)); // no windows on roofs/undersides
	float windows = pane * lit * bright * wallness;

	float emit = mix(base_emission, max(windows, base_emission * 0.4), windows_on);
	ALBEDO = v_tint * 0.10;
	METALLIC = 0.0;
	ROUGHNESS = 0.6;
	EMISSION = neon * emit * emission_boost;
}
