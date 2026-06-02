shader_type canvas_item;

// DIVEPUNK — fullscreen "lens rain" (Task 6, user-requested cyberpunk scope extension). Procedural rain
// streaks on the camera glass that SHEAR BACK and intensify with flight speed. Hash/value-noise only (no
// textures), same family as speed_lines / sky / water. ALPHA-blended (NOT additive) so the cool streaks never
// wash out the warm dusk sky or lift the neon bloom. Drawn on a fullscreen ColorRect at CanvasLayer 31 (above
// the chromatic-aberration/vignette post pass at 30, below the HUD at 50). Fed `speed` (0..1, eased ship
// speed ratio) by RainFX.cs. Single pass — ~3 hash calls + a few fract/smoothstep per fragment.

uniform float speed = 0.0;                                              // 0..1 eased speed ratio (from the ship)
uniform float rain_strength = 1.0;                                      // master gain (settings.cfg [fx] rain_strength)
uniform vec4 rain_color : source_color = vec4(0.62, 0.72, 0.82, 1.0);  // cool desaturated highlight (not saturated cyan)
uniform float base_lean = 0.12;                                        // streak shear at rest (slight gravity lean)
uniform float max_lean  = 1.6;                                         // extra shear at full speed (streaks blast back)
uniform float rest_intensity = 0.45;                                   // streak intensity floor at rest (always-on drizzle)
uniform float aspect = 1.7;                                            // ~screen aspect; keeps streaks near-vertical

float hash11(float x) { return fract(sin(x * 127.1) * 43758.5453); }
float hash21(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }

// One layer of scrolling streak columns. density = columns across, len = streaks per column (vertical
// frequency), scroll = fall speed, seed = decorrelates layers, fatten widens the near-layer streaks.
float streak_layer(vec2 uv, float density, float len, float scroll, float seed, float fatten) {
	float col   = floor(uv.x * density);                               // which column
	float rnd   = hash11(col + seed);                                  // per-column phase + speed jitter
	float cx    = fract(uv.x * density);                              // 0..1 across the column
	float width = 1.0 - abs(cx * 2.0 - 1.0);                          // tent: 1 at column centre -> 0 at edges
	float horiz = pow(width, mix(42.0, 14.0, fatten));               // sharp -> a thin streak (softer for near layer)
	float yflow = fract(uv.y * len - TIME * (scroll + rnd * scroll * 0.6) - rnd * 13.0);
	float vert  = smoothstep(0.0, 0.06, yflow) * smoothstep(0.45, 0.12, yflow);  // a short falling dash
	return horiz * vert;
}

void fragment() {
	vec2 uv = UV;
	uv.x *= aspect;                                                    // square-ish space so streaks stay near-vertical

	// SPEED -> shear the whole field so the columns tilt back; longer & faster streaks at speed.
	float lean   = base_lean + speed * max_lean;
	uv.x += uv.y * lean;                                              // shear (tilt) BEFORE column quantisation
	float intensity_mul = mix(rest_intensity, 1.0, speed);
	float len_mul = mix(1.0, 0.6, speed);                            // lower vertical freq at speed = longer streaks

	// three layers: far/fine/faint, mid, near/fat/bright/fast.
	float s  = streak_layer(uv, 130.0, 9.0 * len_mul, 1.6 + speed * 3.0, 0.0,  0.0) * 0.06;
	s += streak_layer(uv,  70.0, 6.0 * len_mul, 2.4 + speed * 4.0, 7.0,  0.4) * 0.12;
	s += streak_layer(uv,  34.0, 4.0 * len_mul, 3.2 + speed * 5.5, 19.0, 1.0) * 0.20;

	// gentle low-freq "wet patches" so the rain isn't perfectly uniform (one hash; cheap).
	float patch = mix(0.7, 1.15, hash21(floor(uv * 3.0)));

	float a = clamp(s * patch * intensity_mul * rain_strength, 0.0, 1.0) * rain_color.a;
	COLOR = vec4(rain_color.rgb, a);                                  // alpha-blended cool streaks over the frame
}
