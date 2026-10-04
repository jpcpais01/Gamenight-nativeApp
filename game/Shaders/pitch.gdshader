// The pitch: mowing stripes, grain and chalk lines computed per pixel (no textures).
// At art resolution this is a handful of maths per pixel for ~130k pixels.
shader_type spatial;
render_mode specular_disabled, diffuse_lambert;

#include "res://World/Shaders/light_map.gdshaderinc"

const float HL = 52.5;
const float HW = 34.0;
const float BOX_D = 16.5;
const float BOX_HW = 20.16;
const float SIX_D = 5.5;
const float SIX_HW = 9.16;
const float SPOT = 11.0;
const float CIRCLE = 9.15;

uniform vec3 grass_dark = vec3(0.20, 0.40, 0.16);
uniform vec3 grass_light = vec3(0.25, 0.47, 0.19);
uniform vec3 surround = vec3(0.17, 0.33, 0.14);
// Beyond the apron: the ground sets it (grey track, or grass at the training ground). sRGB.
global uniform vec3 gn_land;
uniform vec3 chalk = vec3(0.92, 0.93, 0.88);
uniform float line_half_width = 0.06;

varying vec2 wp;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
float seg_dist(vec2 p, vec2 a, vec2 b) {
	vec2 pa = p - a;
	vec2 ba = b - a;
	float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
	return length(pa - ba * h);
}
float rect_dist(vec2 p, vec2 mn, vec2 mx) {
	float d = seg_dist(p, vec2(mn.x, mn.y), vec2(mx.x, mn.y));
	d = min(d, seg_dist(p, vec2(mx.x, mn.y), vec2(mx.x, mx.y)));
	d = min(d, seg_dist(p, vec2(mx.x, mx.y), vec2(mn.x, mx.y)));
	d = min(d, seg_dist(p, vec2(mn.x, mx.y), vec2(mn.x, mn.y)));
	return d;
}
// Distance (m) to the nearest chalk line: the PWA's linesDist.
float lines_dist(vec2 p) {
	vec2 q = vec2(abs(p.x), p.y);
	float d = rect_dist(p, vec2(-HL, -HW), vec2(HL, HW));
	d = min(d, abs(p.x) + step(HW, abs(p.y)) * 1e3);
	d = min(d, abs(length(p) - CIRCLE));
	d = min(d, max(length(p) - 0.22, 0.0));
	d = min(d, rect_dist(q, vec2(HL - BOX_D, -BOX_HW), vec2(HL, BOX_HW)));
	d = min(d, rect_dist(q, vec2(HL - SIX_D, -SIX_HW), vec2(HL, SIX_HW)));
	vec2 spot = vec2(HL - SPOT, 0.0);
	d = min(d, max(length(q - spot) - 0.2, 0.0));
	float arc = abs(length(q - spot) - CIRCLE) + step(HL - BOX_D, q.x) * 1e3;
	d = min(d, arc);
	vec2 c = abs(p) - vec2(HL, HW);
	d = min(d, abs(length(c) - 1.0) + step(0.0, c.x) * 1e3 + step(0.0, c.y) * 1e3);
	return d;
}

void vertex() {
	wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

void fragment() {
	vec2 p = wp;
	// Mowing stripes across the length (18 bands), softened grain and slow patchiness.
	float band = step(0.5, fract((p.x + HL) / (2.0 * HL / 9.0)));
	vec3 g = mix(grass_dark, grass_light, band);
	g *= 0.93 + 0.07 * vnoise(p * 2.3);
	g *= 0.9 + 0.14 * vnoise(p * 0.12);
	// Goalmouth wear.
	vec2 gm = vec2(HL - abs(p.x), p.y);
	float wear = exp(-dot(gm * vec2(0.5, 0.35), gm * vec2(0.5, 0.35)));
	g = mix(g, vec3(0.33, 0.33, 0.2), wear * 0.35);

	// Outside the lines: a darker apron, then the track.
	vec2 o = abs(p) - vec2(HL, HW);
	float outside = max(o.x, o.y);
	g = mix(g, surround * (0.92 + 0.08 * band), step(0.0, outside) * 0.55);
	float trackMask = step(9.0, outside);
	g = mix(g, gn_land * (0.9 + 0.1 * vnoise(p * 1.7)), trackMask);

	// Rain: the grass soaks darker and deeper green, water stands in the low spots, and the
	// floodlights glance off it all at a low angle (a sheen toward the lights, added below).
	float puddle = 0.0;
	if (gn_rain > 0.0) {
		g = pow(g, vec3(1.12)) * vec3(0.8, 0.86, 0.8);
		puddle = smoothstep(0.75, 0.79, vnoise(p * 0.21 + 3.1) * 0.7 + vnoise(p * 0.83) * 0.3) * (1.0 - trackMask);
		g = mix(g, vec3(0.13, 0.17, 0.2), puddle * 0.7);
	}

	// Chalk, filtered by how much of the pixel it covers (as the PWA does): a hard edge made
	// lines a pixel thick here and two there, and they shimmered as the camera moved. A far
	// line thinner than a pixel fades to a steady, paler line instead of breaking up.
	float d = lines_dist(p);
	float aa = fwidth(d) * 0.8 + 0.01;
	float line = (1.0 - smoothstep(line_half_width - aa, line_half_width + aa, d)) * (1.0 - trackMask);
	// Colours above are written in sRGB; the renderer lights in linear.
	ALBEDO = pow(mix(g, chalk, line), vec3(2.2));
	ROUGHNESS = 1.0;
	// The ground's floodlight pools.
	vec3 flood = light_map_flood(vec3(p.x, 0.0, p.y));
	EMISSION = ALBEDO * flood;
	if (gn_rain > 0.0) {
		// The wet sheen: brightest at a grazing view, strongest on the puddles, lit by the
		// floodlight pools (and the lightning).
		float graze = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
		float wet = (0.2 + 0.8 * puddle) * (0.25 + 0.75 * graze);
		EMISSION += (flood * 0.2 + weather_flash() * 0.6) * wet;
	}
}

// The sun, kept off where the stands shade the grass (the ground's baked light map).
void light() {
	DIFFUSE_LIGHT += light_map_sun(LIGHT, NORMAL, LIGHT_COLOR, ATTENUATION, vec3(wp.x, 0.0, wp.y));
}
