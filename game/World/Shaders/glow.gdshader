// Floodlight glows: a camera-facing card per bank (VERTEX = the bank, UV = corner -1..1).
// Opaque: a hot core, then a halo whose density falls off in an ordered dither, so it reads
// as a soft bloom in pixel art without any blending.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

#include "res://World/Shaders/world.gdshaderinc"

varying vec2 c;

float bayer2(vec2 a) { a = floor(a); return fract(dot(a, vec2(0.5, a.y * 0.75))); }
float bayer4(vec2 p) { return bayer2(0.5 * p) * 0.25 + bayer2(p); }

void vertex() {
	c = UV;
	float size = 3.0 + gn_flood * 6.0;
	vec4 mv = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	// Pulled toward the camera so the roof front never cuts it.
	mv.xyz += normalize(-mv.xyz) * 6.0;
	mv.xy += UV * size;
	POSITION = PROJECTION_MATRIX * mv;
}

void fragment() {
	float r = length(c);
	float core = 1.0 - smoothstep(0.12, 0.2, r);
	float halo = pow(max(0.0, 1.0 - r), 2.2) * (0.25 + 0.75 * gn_flood);
	if (core < 0.5 && halo < bayer4(FRAGCOORD.xy) + 0.08) discard;
	vec3 hot = vec3(1.0, 0.97, 0.88) * (0.8 + 2.2 * gn_flood);
	ALBEDO = mix(vec3(1.0, 0.86, 0.62) * (0.4 + 1.1 * gn_flood), hot, core);
}
