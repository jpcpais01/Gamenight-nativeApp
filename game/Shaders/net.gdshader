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
	vec2 g = UV / 0.14;
	vec2 f = abs(fract(g) - 0.5);
	vec2 w = fwidth(g);
	float lx = 1.0 - smoothstep(0.5 - w.x * 1.2, 0.5, f.x + 0.06);
	float ly = 1.0 - smoothstep(0.5 - w.y * 1.2, 0.5, f.y + 0.06);
	float line = max(1.0 - lx, 1.0 - ly);
	// Tiny on screen: a soft haze instead of moiré.
	float far = smoothstep(0.25, 0.8, max(w.x, w.y));
	float a = mix(line * 0.8, 0.24, far);
	if (bayer4(FRAGCOORD.xy) >= a) discard;
	ALBEDO = pow(color, vec3(2.2));
}
