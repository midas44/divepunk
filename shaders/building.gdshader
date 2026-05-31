shader_type spatial;
// DIVEPUNK — procedural neon building (M4 aesthetic pass, v3). ONE shared material, drawn by each
// chunk's MultiMesh, so a whole skyline stays a handful of draw calls. A DARK tower body with a SPARSE,
// dim, chaotic emissive window grid laid out in WORLD space (so window floors line up across faces AND
// buildings). Every tower carries its lighting profile through the MultiMesh per-instance channels:
//
//   COLOR.rgb        = the building's WHITE window base (cold white / warm white / neutral)
//   COLOR.a          = lit fraction (how many of its windows are on — kept low; most windows are dark)
//   INSTANCE_CUSTOM  = (variation, grid_class, accent_hue, accent_amount)
//
// Most windows glow the building's white base; a per-building FRACTION (`accent_amount`) of them are
// recoloured to a saturated neon `accent_hue` instead — so colour appears as pops scattered AMONG the
// white windows, never a whole building in one flat colour. `variation` decides whether neighbouring
// windows match (a uniform office block) or differ widely (residential — brightness spread, many dim).
// `grid_class` selects the window SIZE/SHAPE preset. The per-building decorrelation seed is derived in
// the vertex stage from the instance's world origin. windows_on = 0 falls back to a flat dim body.

uniform float window_scale = 1.0;      // metres-per-window multiplier (= WorldScale; keeps panes proportional)
uniform float emission_boost = 1.8;    // neon emission strength (drives the bloom on the brightest few)
uniform float base_emission = 0.02;    // tiny body glow so unlit towers aren't pure black
uniform vec3 wall_color : source_color = vec3(0.012, 0.013, 0.020);  // DARK concrete/glass body
uniform float windows_on = 1.0;

// --- Chaotic per-window flicker -------------------------------------------------------------------
// A small share of windows change over time — switching on, switching off, or just shifting brightness
// — each on its own slow, phase-offset clock so the skyline never blinks in unison. Realistically rare:
// most windows hold their state forever; only `flicker_fraction` of them are ever volatile, and each of
// those re-rolls only every several seconds.
uniform float flicker_fraction = 0.07;  // share of windows that ever change (0 = fully static city)
uniform float flicker_rate = 0.13;      // baseline state-changes per second for a volatile window

// Per-building window grid presets: (metresH, metresV, marginH, marginV). The margins are the dark
// mullion fraction (0..0.5) around each pane. grid_class (0..1) picks one, so window size AND aspect
// vary between towers — dense office panes, tall slots, wide ribbons, big loft glass.
// Pane H/V are halved from the original presets so windows stay modest on the larger WorldScale=4 city
// (final pane metres = these × window_scale); margins are fractions, so they're unchanged.
const vec4 GRID[5] = vec4[](
	vec4(2.25, 1.70, 0.20, 0.22),   // 0 — dense small office panes
	vec4(2.80, 2.05, 0.16, 0.18),   // 1 — standard
	vec4(1.55, 2.90, 0.26, 0.12),   // 2 — tall narrow slots
	vec4(4.30, 1.60, 0.10, 0.22),   // 3 — wide horizontal ribbon
	vec4(3.60, 3.20, 0.08, 0.08)    // 4 — big loft glass
);

varying vec3 v_wpos;
varying vec3 v_wnormal;
varying vec3 v_white;   // white window base colour (COLOR.rgb)
varying vec4 v_prof;    // x = lit fraction, y = variation, z = accent hue, w = accent amount
varying float v_grid;
varying float v_seed;   // per-building seed (derived from the instance world origin)

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

vec3 hsv2rgb(vec3 c) {
	vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
	vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
	return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

// Per-window brightness from two hashes + the building's `variation`. The baseline is low (most lit
// windows glow softly; only a few blaze and bloom); `variation` widens the spread so residential towers
// mix a handful of bright windows with many near-dark ones, and a second hash dims further so even a
// uniform office never reads as a flat bright panel. Shared by the static and the flickering paths.
float win_bright(float rb, float rb2, float variation) {
	float base = mix(0.46, 0.26, variation);
	float spread = mix(0.42, 0.90, variation);
	float b = base + (rb - 0.5) * spread;
	b *= 0.42 + 0.45 * rb2;
	return clamp(b, 0.025, 1.0);
}

void vertex() {
	v_wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// Normal matrix (inverse-transpose of the model basis): under the heavy NON-UNIFORM scale of a
	// tall/thin/tapered tower, transforming the normal by MODEL_MATRIX skews it vertical, which made
	// `wallness` cull the windows off round/tapered buildings. This keeps the wall test correct.
	v_wnormal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
	v_white = COLOR.rgb;
	v_prof = vec4(COLOR.a, INSTANCE_CUSTOM.x, INSTANCE_CUSTOM.z, INSTANCE_CUSTOM.w);
	v_grid = INSTANCE_CUSTOM.y;
	// Per-building seed from the instance world origin — decorrelates the lit pattern (and the grid
	// phase) between towers, so two same-profile buildings never share a window layout.
	vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	v_seed = fract(sin(dot(floor(origin.xz), vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	float lit_frac = v_prof.x;
	float variation = v_prof.y;
	float accent_hue = v_prof.z;
	float accent_amt = v_prof.w;

	// Pick this building's window grid (size + shape), scaled to the world.
	int gi = int(clamp(floor(v_grid * 5.0), 0.0, 4.0));
	vec4 g = GRID[gi];
	float size_h = g.x * window_scale;
	float size_v = g.y * window_scale;
	vec2 margin = g.zw;

	// World-space window cells. The horizontal axis comes from whichever wall this face is (a face
	// looking along X uses world Z for its columns, and vice-versa); world Y is always the rows. The
	// per-building seed offset shifts the whole grid so towers never line up.
	float horiz = (abs(v_wnormal.x) >= abs(v_wnormal.z)) ? v_wpos.z : v_wpos.x;
	vec2 cell = vec2(horiz / size_h, v_wpos.y / size_v) + vec2(v_seed * 13.0, v_seed * 7.0);
	vec2 id = floor(cell);
	vec2 f = fract(cell);
	// Window pane shape. Most buildings use rectangular panes; a few "special" towers (picked by the
	// per-building seed) instead use round / oval / hex windows — sparingly, so they read as landmarks.
	vec2 pc = f - 0.5;                                   // position within the cell, -0.5..0.5
	float rect = step(margin.x, f.x) * step(f.x, 1.0 - margin.x)
	           * step(margin.y, f.y) * step(f.y, 1.0 - margin.y);
	vec2 erad = max(vec2(0.5) - margin, vec2(0.02));     // ellipse half-axes (pane minus the mullion)
	float rmin = min(erad.x, erad.y);
	float oval = step(length(pc / erad), 1.0);
	float circ = step(length(pc / rmin), 1.0);
	vec2 hp = abs(pc) / rmin;                            // pointy-top hexagon
	float hex = step(max(hp.x * 0.8660254 + hp.y * 0.5, hp.y), 1.0);
	float ws = hash21(vec2(v_seed * 91.7 + 4.3, v_seed * 13.1 + 1.7));
	float pane = rect;
	if (ws > 0.93) pane = hex;
	else if (ws > 0.88) pane = circ;
	else if (ws > 0.84) pane = oval;

	// Per-window randoms (decorrelated between buildings by the seed offset above).
	float r_lit  = hash21(id + vec2(7.0, 3.0));
	float r_bri  = hash21(id + vec2(19.0, 5.0));
	float r_bri2 = hash21(id + vec2(41.0, 17.0));
	float r_temp = hash21(id + vec2(33.0, 11.0));
	float r_acc  = hash21(id + vec2(53.0, 29.0));

	// STATIC look — the vast majority of windows hold a fixed lit state and brightness.
	float lit = step(1.0 - lit_frac, r_lit);
	float bri = win_bright(r_bri, r_bri2, variation);

	// CHAOTIC FLICKER — a small share of windows (`flicker_fraction`) instead run a slow private clock and
	// re-roll their lit state AND brightness each tick, cross-fading the switch over a fraction of a second.
	// A flip between differing lit states reads as a light switching on or off; a flip while both ticks are
	// lit reads as a brightness change. Per-window phase + rate jitter keep the changes scattered in time,
	// never synchronised. Because each tick is lit with probability `lit_frac` just like the static path,
	// the overall fraction of windows alight stays put — the city doesn't drift brighter or darker.
	float r_vol = hash21(id + vec2(67.0, 23.0));
	if (r_vol < flicker_fraction) {
		float r_phase = hash21(id + vec2(83.0, 47.0));
		float r_rate  = hash21(id + vec2(13.0, 71.0));
		float clock = TIME * flicker_rate * (0.6 + 0.8 * r_rate) + r_phase * 90.0;
		float ep = floor(clock);
		float fade = smoothstep(0.0, 0.10, fract(clock));   // quick flip in the first tenth of each tick
		vec2 jc = vec2(ep) * 1.7;            // current tick's hash offset
		vec2 jp = vec2(ep - 1.0) * 1.7;      // previous tick's
		float lit_c = step(1.0 - lit_frac, hash21(id + vec2(7.0, 3.0) + jc));
		float lit_p = step(1.0 - lit_frac, hash21(id + vec2(7.0, 3.0) + jp));
		float bri_c = win_bright(hash21(id + vec2(19.0, 5.0) + jc), hash21(id + vec2(41.0, 17.0) + jc), variation);
		float bri_p = win_bright(hash21(id + vec2(19.0, 5.0) + jp), hash21(id + vec2(41.0, 17.0) + jp), variation);
		lit = mix(lit_p, lit_c, fade);
		bri = mix(bri_p, bri_c, fade);
	}

	// Colour: most windows are the building's white base, lightly jittered warm/cool per window by
	// `variation`. A minority (`accent_amt`) are recoloured to a saturated neon hue — colour pops sitting
	// among the white, never the whole tower.
	float temp = (r_temp - 0.5) * 2.0 * variation;
	vec3 white = v_white;
	white.r *= 1.0 + 0.18 * temp;
	white.b *= 1.0 - 0.18 * temp;
	vec3 accent = hsv2rgb(vec3(accent_hue, 0.92, 1.0));
	float is_accent = step(r_acc, accent_amt);
	vec3 wcol = mix(white, accent, is_accent);

	float wallness = smoothstep(0.35, 0.7, 1.0 - abs(v_wnormal.y));  // no windows on roofs/undersides
	float win = pane * lit * bri * wallness;

	// Dark body — the concrete catches only a faint, dark spill of the building's own light colour.
	vec3 wall = wall_color * (0.65 + 0.7 * v_seed);
	wall = mix(wall, wall * (0.4 + v_white), 0.25);

	vec3 win_emis = wcol * win * emission_boost;
	vec3 lit_body = win_emis + wall * base_emission;
	vec3 flat_body = v_white * base_emission * 3.0;   // windows_on = 0 fallback: dim coloured body

	ALBEDO = wall;
	METALLIC = 0.0;
	ROUGHNESS = 0.65;
	EMISSION = mix(flat_body, lit_body, windows_on);
}
