// The pixel-art post pass. It runs per rendered sample (four per art pixel when the view is
// supersampled, then averaged down) but thinks in art pixels (art_size): 1-art-pixel outlines
// from the depth buffer, the PWA's grade (green lift, cool shade, warm light, S-curve,
// vibrance) and an ordered-dither quantise, all in one pass. Drawn on a full-screen quad
// that renders last in the 3D viewport.
shader_type spatial;
render_mode unshaded, depth_test_disabled, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform float levels = 22.0;
uniform float night = 0.0;
/// The art target's size in art pixels (the 3D may render at a multiple of it).
uniform vec2 art_size = vec2(480.0, 270.0);

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

float lin_depth(vec2 uv, mat4 inv_proj) {
	float z = texture(depth_tex, uv).r;
#if CURRENT_RENDERER == RENDERER_COMPATIBILITY
	vec3 ndc = vec3(uv * 2.0 - 1.0, z * 2.0 - 1.0);
#else
	vec3 ndc = vec3(uv * 2.0 - 1.0, z);
#endif
	vec4 v = inv_proj * vec4(ndc, 1.0);
	return -v.z / v.w;
}

float bayer2(vec2 a) { a = floor(a); return fract(dot(a, vec2(0.5, a.y * 0.75))); }
float bayer4(vec2 p) { return bayer2(0.5 * p) * 0.25 + bayer2(p); }

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 e = 1.0 / art_size;
	vec3 c = texture(screen_tex, uv).rgb;

	// Silhouette outlines: this pixel is clearly nearer than a neighbour.
	float d = lin_depth(uv, INV_PROJECTION_MATRIX);
	float dn = max(max(lin_depth(uv + vec2(e.x, 0.0), INV_PROJECTION_MATRIX), lin_depth(uv - vec2(e.x, 0.0), INV_PROJECTION_MATRIX)),
			max(lin_depth(uv + vec2(0.0, e.y), INV_PROJECTION_MATRIX), lin_depth(uv - vec2(0.0, e.y), INV_PROJECTION_MATRIX)));
	float edge = step(0.35 + d * 0.06, dn - d) * (1.0 - smoothstep(70.0, 110.0, d));

	// Clarity: a touch of local contrast against the 1-pixel neighbourhood.
	vec3 nb = (texture(screen_tex, uv + vec2(e.x, 0.0)).rgb + texture(screen_tex, uv - vec2(e.x, 0.0)).rgb
			+ texture(screen_tex, uv + vec2(0.0, e.y)).rgb + texture(screen_tex, uv - vec2(0.0, e.y)).rgb) * 0.25;
	c = max(c + clamp(c - nb, -0.25, 0.25) * 0.35, 0.0);

	// Display gamma, then the grade.
	c = pow(clamp(c, 0.0, 1.0), vec3(1.0 / 2.2));
	float l = dot(c, vec3(0.299, 0.587, 0.114));
	float green = smoothstep(0.0, 0.08, c.g - max(c.r, c.b));
	c = mix(c, c * vec3(1.02, 1.06, 0.92), green * 0.6);
	vec3 shade = mix(vec3(0.84, 0.97, 1.1), vec3(0.86, 0.84, 1.14), night);
	c = mix(c, c * shade, (1.0 - l) * (0.32 + 0.2 * night));
	c = mix(c, c * vec3(1.07, 1.0, 0.88), smoothstep(0.55, 1.0, l) * 0.4);
	c = mix(vec3(l), c, 1.12);
	c = max(c - 0.015, 0.0) / 0.985;
	c = mix(c, c * c * (3.0 - 2.0 * c), 0.3);
	float hi = max(c.r, max(c.g, c.b));
	float lo = min(c.r, min(c.g, c.b));
	float sat = (hi - lo) / max(hi, 1e-3);
	float l2 = dot(c, vec3(0.299, 0.587, 0.114));
	c = mix(vec3(l2), c, 1.0 + 0.6 * (1.0 - sat) * smoothstep(0.03, 0.2, hi));
	c *= mix(vec3(0.94, 0.98, 1.08), vec3(1.05, 1.01, 0.95), smoothstep(0.2, 0.75, l2));

	// Quantise with a light ordered dither: gradients become crisp pixel bands.
	// (The pattern is per art pixel, so all of a pixel's samples agree.)
	float th = bayer4(floor(uv * art_size));
	c = floor(clamp(c, 0.0, 1.0) * levels + 0.5 + (th - 0.5) * 0.4) / levels;
	// Outlines in a deeper shade of the object's own colour.
	c = mix(c, c * c * vec3(0.55, 0.5, 0.7), edge * 0.9);

	// Back to linear: the viewport converts to sRGB on output.
	ALBEDO = pow(clamp(c, 0.0, 1.0), vec3(2.2));
}
