// The delivery's dotted flight: round dots facing the camera, faint at the foot and strong
// where it comes down (INSTANCE_CUSTOM.x), dithered rather than blended.
shader_type spatial;
render_mode unshaded, shadows_disabled, cull_disabled;

varying float alpha;

float bayer2(vec2 a) { a = floor(a); return fract(dot(a, vec2(0.5, a.y * 0.75))); }
float bayer4(vec2 p) { return bayer2(0.5 * p) * 0.25 + bayer2(p); }

void vertex() {
	alpha = INSTANCE_CUSTOM.x;
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
}

void fragment() {
	if (length(UV - 0.5) > 0.5) discard;
	if (bayer4(FRAGCOORD.xy) >= alpha) discard;
	ALBEDO = pow(vec3(1.0, 0.92, 0.6), vec3(2.2));
}
