// The aimed delivery (the PWA's cornerAim): a gold ring on the grass where the ball will come
// down, breathing, with a white disc pulsing in it. Cut out with a dither, not blended, so the
// post pass sees it like everything else.
shader_type spatial;
render_mode unshaded, shadows_disabled, cull_disabled;

const float SIZE = 3.0; // the quad, metres

float bayer2(vec2 a) { a = floor(a); return fract(dot(a, vec2(0.5, a.y * 0.75))); }
float bayer4(vec2 p) { return bayer2(0.5 * p) * 0.25 + bayer2(p); }

void fragment() {
	float d = length(UV - 0.5) * SIZE;
	float s = 1.0 + 0.08 * sin(TIME * 5.0);
	bool ring = d > 1.0 * s && d < 1.35 * s;
	bool disc = d < 0.42;
	if (!ring && !disc) discard;
	if (disc && bayer4(FRAGCOORD.xy) > 0.55 + 0.3 * sin(TIME * 5.0)) discard;
	ALBEDO = ring ? pow(vec3(1.0, 0.831, 0.278), vec3(2.2)) : vec3(1.0);
}
