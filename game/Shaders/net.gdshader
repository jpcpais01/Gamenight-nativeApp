// Goal nets as a stipple: a fixed share of art pixels drawn, the rest cut out, so the net
// reads as see-through without blending (pixel-art transparency).
shader_type spatial;
render_mode unshaded, shadows_disabled, cull_disabled;

uniform vec3 color = vec3(0.86, 0.87, 0.9);
uniform float density = 0.4;

float bayer2(vec2 a) { a = floor(a); return fract(dot(a, vec2(0.5, a.y * 0.75))); }
float bayer4(vec2 p) { return bayer2(0.5 * p) * 0.25 + bayer2(p); }

void fragment() {
	// A 25 cm mesh (UV is in metres), stippled in screen space.
	vec2 g = abs(fract(UV * 4.0) - 0.5);
	float mesh = step(0.32, max(g.x, g.y));
	if (bayer4(FRAGCOORD.xy) > density * (0.55 + 0.45 * mesh)) discard;
	ALBEDO = pow(color * (0.85 + 0.15 * mesh), vec3(2.2));
}
