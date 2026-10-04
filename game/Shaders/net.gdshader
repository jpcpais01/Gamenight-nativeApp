// The goal net (the PWA's net material): a fine mesh of cord that, from afar, settles into a
// soft haze instead of moiré; it bulges out where the ball hits it and ripples. See-through
// without blending: the cord's coverage picks which art pixels draw (a fixed stipple).
shader_type spatial;
render_mode unshaded, shadows_disabled, cull_disabled;

// Where the ball hit (the goal's own frame) and when; how hard (0..1); the clock.
uniform vec4 hit = vec4(0.0, 0.0, 0.0, -10.0);
uniform float strength = 0.0;
uniform float time = 0.0;
uniform vec3 color = vec3(0.86, 0.84, 0.8);

float bayer2(vec2 a) { a = floor(a); return fract(dot(a, vec2(0.5, a.y * 0.75))); }
float bayer4(vec2 p) { return bayer2(0.5 * p) * 0.25 + bayer2(p); }

void vertex() {
	float t = time - hit.w;
	if (t >= 0.0 && t < 3.5) {
		vec3 out_dir = normalize(COLOR.rgb * 2.0 - 1.0);
		float d = length(VERTEX - hit.xyz);
		float env = exp(-d * d * 0.8) * exp(-t * 2.0);
		// A slack bulge that swells out and lets go, with a softer ripple on top.
		float give = 1.0 - exp(-t * 20.0);
		float wave = cos(t * 13.0 - d * 4.0);
		VERTEX += out_dir * env * (give * 0.45 + wave * 0.55) * strength * 0.75;
	}
}

void fragment() {
	// Diamond mesh in metres. At art res the real 14 cm cell is far below a pixel, so the cell
	// doubles until it spans a few pixels: the net always reads as cord, one pixel thick, and
	// the coarser levels are every other cord of the finer one, so the seams between them sit
	// on shared cords.
	vec2 m = vec2(UV.x + UV.y, UV.x - UV.y) * 0.70710678;
	vec2 px = fwidth(m);
	// Sized on the less foreshortened axis, so a grazing roof doesn't blow the cells up.
	float wpx = min(px.x, px.y);
	float level = clamp(ceil(log2(wpx * 4.0 / 0.14)), 0.0, 3.0);
	float cell = 0.14 * exp2(level);
	vec2 g = m / cell;
	vec2 dpx = (0.5 - abs(fract(g) - 0.5)) * cell / max(px, vec2(1e-5));
	bool cord = min(dpx.x, dpx.y) < 0.5;
	// Farther than even the coarsest cell can show: a soft stipple haze.
	float far = smoothstep(1.2, 2.0, wpx * 4.0 / (0.14 * 8.0));
	float a = mix(cord ? 1.0 : 0.0, 0.24, far);
	if (bayer4(FRAGCOORD.xy) >= a) discard;
	// The side panels sit a touch darker than the roof and back, for depth; a tight mesh is
	// greyer, so a fine net reads as cord and not as a white sheet.
	float tight = 1.0 - smoothstep(4.0, 8.0, cell / wpx);
	ALBEDO = pow(color * mix(0.82, 1.0, step(0.6, COLOR.g)) * (1.0 - 0.28 * tight), vec3(2.2));
}
