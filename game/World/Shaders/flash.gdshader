// The photographers' flash guns: a camera-facing card per camera (INSTANCE_CUSTOM.x = how lit,
// 0 off). Opaque like the floodlight glows: a white-hot core and a dithered halo, sized so it
// still reads as a few pixels across the pitch at art resolution.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled, fog_disabled;

varying vec2 c;
varying float lit;

float bayer2(vec2 a) { a = floor(a); return fract(dot(a, vec2(0.5, a.y * 0.75))); }
float bayer4(vec2 p) { return bayer2(0.5 * p) * 0.25 + bayer2(p); }

void vertex() {
	c = UV;
	lit = INSTANCE_CUSTOM.x;
	vec4 mv = MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
	float size = max(0.18, -mv.z * 0.013) * lit;
	// Pulled toward the camera so the photographer's own head never covers it.
	mv.xyz += normalize(-mv.xyz) * 0.5;
	mv.xy += UV * size;
	POSITION = PROJECTION_MATRIX * mv;
}

void fragment() {
	float r = length(c);
	if (lit < 0.01 || r > 1.0) discard;
	float core = 1.0 - smoothstep(0.28, 0.42, r);
	float halo = pow(1.0 - r, 1.8) * lit;
	if (core < 0.5 && halo < bayer4(FRAGCOORD.xy) + 0.05) discard;
	ALBEDO = mix(vec3(0.7, 0.82, 1.0) * 1.6, vec3(4.0), core * lit);
}
