// Flags waved in the crowd (the PWA's crowdFlags): one MultiMesh. Each instance is a fan's
// flag on its pole, swung back and forth round the pole's foot, waves running to the fly end.
// Per instance: COLOR = (colour, pattern + 0.9 x sun visibility), INSTANCE_CUSTOM = (second
// colour, phase). Per vertex: UV over the cloth (v up), UV2.x = 1 on the cloth, 0 on the pole.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

#include "res://World/Shaders/world.gdshaderinc"

uniform sampler2D crest : source_color, filter_linear, repeat_disable;
uniform float has_crest = 0.0;

varying vec3 col;
varying vec3 col2;
varying flat float pat;
varying float shade;
varying float vis;
varying float dist;
varying vec3 wn;

void vertex() {
	float cloth = UV2.x;
	col = COLOR.rgb;
	col2 = INSTANCE_CUSTOM.rgb;
	pat = floor(COLOR.a) + (1.0 - cloth) * 10.0;
	vis = fract(COLOR.a) / 0.9;
	float ph = INSTANCE_CUSTOM.a;
	vec3 p = VERTEX;
	// 0 at the pole, 1 at the fly end.
	float k = clamp(p.x / 2.4, 0.0, 1.0) * cloth;
	float sp = 1.7 + fract(ph) * 0.8;
	float A = 0.5 + gn_excite * 0.35;
	float th = TIME * sp + ph;
	float sway = sin(th) * A;
	float swayVel = cos(th) * A * sp;
	// Waves run faster mid-swing: the phase integrates |cos| (2n + sin of the remainder).
	float n = floor(th / PI + 0.5);
	float t = TIME * 4.0 + (2.0 * n + sin(th - n * PI)) * 0.75;
	float ph1 = p.x * 2.6 - t + ph;
	float ph2 = p.x * 5.6 - t * 1.7 + p.y * 2.2 + ph * 1.3;
	float ph3 = p.x * 11.0 - t * 3.1 + p.y * 5.0;
	float amp = (0.16 + 0.1 * abs(swayVel)) * pow(k, 1.3);
	p.z += (sin(ph1) * 0.6 + sin(ph2) * 0.26 + sin(ph3) * 0.1 * k) * amp;
	float slope = (cos(ph1) * 1.56 + cos(ph2) * 1.46 + cos(ph3) * 1.1 * k) * amp;
	// Gravity at the turn of each swing, flying out mid-swing; drag trails the pole.
	p.y -= k * k * 0.38 * (1.0 - min(1.0, abs(swayVel) * 0.45));
	float ang = sway - swayVel * 0.17 * k;
	p.xy = vec2(p.x * cos(ang) - p.y * sin(ang), p.x * sin(ang) + p.y * cos(ang));
	shade = cloth > 0.5 ? clamp(0.86 - slope * 0.32, 0.5, 1.2) : 1.0;
	VERTEX = p;
	wn = normalize((MODEL_MATRIX * vec4(0.0, 0.0, 1.0, 0.0)).xyz);
	dist = length((MODELVIEW_MATRIX * vec4(p, 1.0)).xyz);
}

void fragment() {
	vec2 u = UV;
	float b = 0.0;
	if (pat < 0.5) b = step(0.36, u.y) * step(u.y, 0.64);                      // band
	else if (pat < 1.5) b = step(0.5, u.x);                                    // halves
	else if (pat < 2.5) b = step(abs(u.y - (u.x * 0.62 + 0.19)), 0.16);       // sash
	else if (pat < 3.5) b = step(0.333, u.x) * step(u.x, 0.667);              // tricolour
	else if (pat < 4.5) {                                                      // crest
		float r = length((u - 0.5) * vec2(1.6, 1.0));
		b = step(r, 0.3) * (1.0 - step(0.2, r) * step(r, 0.24));
	}
	else if (pat < 5.5) b = mod(floor(u.x * 4.0) + floor(u.y * 3.0), 2.0);     // chequers
	else b = step(0.5, fract(u.y * 2.5));                                      // hoops
	vec3 c = pat > 9.5 ? vec3(0.04) : mix(col, col2, b);
	if (has_crest > 0.5 && pat > 3.5 && pat < 4.5) {
		// The club crest in the middle of the cloth.
		vec2 cu = vec2((u.x - 0.3) / 0.4, (u.y - 0.1) / 0.8);
		vec4 cr = (cu.x > 0.0 && cu.x < 1.0 && cu.y > 0.0 && cu.y < 1.0) ? texture(crest, vec2(cu.x, 1.0 - cu.y)) : vec4(0.0);
		c = mix(col, cr.rgb, cr.a);
	}
	// A stitched hem round the fly edges.
	float hem = max(step(0.96, u.x), max(step(u.y, 0.04), step(0.96, u.y)));
	c *= pat > 9.5 ? 1.0 : 1.0 - hem * 0.18;
	vec3 n = FRONT_FACING ? wn : -wn;
	vec3 light = world_light(n, vis) + gn_flood_col * gn_flood * 0.45;
	c *= shade * (FRONT_FACING ? 1.0 : 0.9) * light;
	ALBEDO = world_fog(c, dist, 0.1);
}
