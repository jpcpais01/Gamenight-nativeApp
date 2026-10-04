// The stands' effects in one MultiMesh (World/StandFx.cs): flare sparks, smoke, confetti in
// the air and lying on the grass, and the glow of each burning flare. Camera-facing cards
// (landed confetti lies flat). Nothing blends (the post pass reads depth): soft things are an
// ordered dither, offset per particle so overlapping puffs build up.
// Per instance: origin = position, COLOR = (colour, kind), INSTANCE_CUSTOM = (size, alpha,
// seed, spin). Kinds: 0 spark, 1 smoke, 2 confetti, 3 landed confetti, 4 flare glow.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

#include "res://World/Shaders/world.gdshaderinc"

varying flat vec4 col;
varying flat vec4 cus;
varying vec2 c;
varying float dist;

float bayer2(vec2 a) { a = floor(a); return fract(dot(a, vec2(0.5, a.y * 0.75))); }
float bayer4(vec2 p) { return bayer2(0.5 * p) * 0.25 + bayer2(p); }

void vertex() {
	col = COLOR;
	cus = INSTANCE_CUSTOM;
	c = VERTEX.xy;
	float size = cus.x;
	vec4 mv;
	if (col.a > 2.5 && col.a < 3.5) {
		// Landed: flat on the grass, turned by its seed.
		float a = cus.z * 6.2832;
		vec2 r = mat2(vec2(cos(a), sin(a)), vec2(-sin(a), cos(a))) * c * size * 0.5;
		vec3 wp = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz + vec3(r.x, 0.0, r.y);
		mv = VIEW_MATRIX * vec4(wp, 1.0);
	} else {
		mv = MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
		// Confetti tumbles: its width flickers with the spin.
		float w = col.a > 1.5 && col.a < 2.5 ? max(0.25, abs(sin(cus.w))) : 1.0;
		// The flare's glow is pulled toward the camera so the fans in front don't cut it.
		if (col.a > 3.5) mv.xyz += normalize(-mv.xyz) * 1.5;
		mv.xy += c * vec2(w, 1.0) * size * 0.5;
	}
	dist = length(mv.xyz);
	POSITION = PROJECTION_MATRIX * mv;
}

void fragment() {
	float kind = col.a;
	float r = length(c);
	vec2 fc = FRAGCOORD.xy + floor(cus.z * vec2(37.0, 91.0));
	vec3 o;
	if (kind < 0.5) {
		// Spark: a hot speck.
		if (r > 1.0 || cus.y < bayer4(fc)) discard;
		o = col.rgb * 1.6;
	} else if (kind < 1.5) {
		// Smoke: a lumpy puff, densest in the middle, lit by the sky and the floodlights.
		float lump = 0.6 + 0.4 * sin(c.x * 3.1 + cus.z * 20.0) * sin(c.y * 2.7 + cus.z * 13.0);
		float d = cus.y * pow(max(0.0, 1.0 - r), 1.4) * lump;
		if (r > 1.0 || d < bayer4(fc)) discard;
		vec3 light = gn_amb_sky * 1.1 + gn_amb_ground * 0.5 + gn_sun * 0.45 + gn_flood_col * gn_flood * 0.5;
		o = world_fog(col.rgb * light, dist, 0.06);
	} else if (kind < 3.5) {
		// Confetti and ticker tape: little lit squares.
		if (cus.y < bayer4(fc)) discard;
		vec3 light = world_light(vec3(0.0, 1.0, 0.0), 1.0) + gn_flood_col * gn_flood * 0.45;
		o = world_fog(col.rgb * light, dist, 0.04);
	} else {
		// The flare itself: a white-hot core, a red halo that dithers out.
		float core = 1.0 - smoothstep(0.1, 0.18, r);
		float halo = pow(max(0.0, 1.0 - r), 2.0) * cus.y;
		if (core < 0.5 && halo < bayer4(FRAGCOORD.xy) + 0.06) discard;
		o = mix(col.rgb * (0.9 + gn_night * 0.6), vec3(1.0, 0.92, 0.85) * 1.6, core);
	}
	ALBEDO = o;
}
