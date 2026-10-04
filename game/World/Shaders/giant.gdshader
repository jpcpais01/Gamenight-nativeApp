// The giant hanging tifo: tied along its top, rippling more toward its foot. Everything below
// cut_y is still rolled up (discarded). Floodlit from the front: a self-lit floor so it reads
// in the roof's shade.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

#include "res://World/Shaders/world.gdshaderinc"

uniform sampler2D art : source_color, filter_linear_mipmap, repeat_disable;
uniform float cut_y = 1000.0;

varying vec3 wpos;
varying vec3 wn;
varying float dist;

void vertex() {
	vec3 v = VERTEX;
	float f = UV.y;
	float s = UV.x * 6.0;
	float wave = sin(s * 1.3 - TIME * 1.6 + f * 2.0) * 0.6 + sin(s * 2.9 - TIME * 2.7 + f * 3.0) * 0.3;
	v.z += 0.45 * pow(f, 1.2) * wave;
	wpos = (MODEL_MATRIX * vec4(v, 1.0)).xyz;
	wn = normalize((MODEL_MATRIX * vec4(0.0, 0.0, 1.0, 0.0)).xyz);
	dist = length((MODELVIEW_MATRIX * vec4(v, 1.0)).xyz);
	VERTEX = v;
}

void fragment() {
	if (wpos.y < cut_y) discard;
	vec3 a = texture(art, UV).rgb;
	vec3 n = FRONT_FACING ? wn : -wn;
	vec3 c = a * (world_light(n, 0.3) + gn_flood_col * gn_flood * 0.5) * (FRONT_FACING ? 1.0 : 0.6) + a * 0.3;
	ALBEDO = world_fog(c, dist, 0.06);
}
