// Flags waved in the crowd (the PWA's crowdFlags): one MultiMesh. Each instance is a fan's
// flag on its pole. The pole swings from his hands (the instance origin): side to side, in a
// figure of eight, or round in circles, bigger on songs, fists and goals; when his end is quiet
// some poles come down to rest on a shoulder with the cloth hanging. The cloth trails the pole:
// each column follows the pole's swing a little later the further it is from the pole, so the
// motion travels out to the fly end as a wave; it flies out flat when the pole moves fast and
// drapes and sags when it slows, with ripples running along it.
// Per instance: COLOR = (colour, pattern + 0.9 x sun visibility), INSTANCE_CUSTOM = (second
// colour, phase). Per vertex: UV over the cloth (v up), UV2.x = 1 on the cloth, 0 on the pole.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

#include "res://World/Shaders/world.gdshaderinc"

global uniform vec4 gn_mood;
global uniform vec4 gn_react_home;
global uniform vec4 gn_react_away;

uniform sampler2D crest : source_color, filter_linear, repeat_disable;
uniform float has_crest = 0.0;

varying vec3 col;
varying vec3 col2;
varying flat float pat;
varying float shade;
varying float vis;
varying float dist;
varying vec3 wn;

// The pole's lean (roll: side to side in the cloth's plane; pitch: toward or away from the
// pitch) at swing phase th: a pendulum, with the figure of eight's double beat or the circle's
// quarter-turn on the other axis.
vec2 swing(float th, float f8, float circ) {
	return vec2(sin(th), mix(f8 * 0.5 * sin(2.0 * th), 0.65 * cos(th), circ));
}

mat3 lean(vec2 a) {
	float cr = cos(a.x), sr = sin(a.x), cp = cos(a.y), sp = sin(a.y);
	// Roll about z, then pitch about x.
	return mat3(vec3(cr, sr * cp, sr * sp), vec3(-sr, cr * cp, cr * sp), vec3(0.0, -sp, cp));
}

void vertex() {
	float cloth = UV2.x;
	col = COLOR.rgb;
	col2 = INSTANCE_CUSTOM.rgb;
	pat = floor(COLOR.a) + (1.0 - cloth) * 10.0;
	vis = fract(COLOR.a) / 0.9;
	float ph = INSTANCE_CUSTOM.a;
	float h = fract(ph * 0.618 + 0.13), h2 = fract(ph * 0.371 + 0.57);
	vec3 p = VERTEX;
	// 0 at the pole, 1 at the fly end.
	float k = clamp(p.x / 2.4, 0.0, 1.0) * cloth;
	float x = p.x * cloth;

	// How lively his end is: singing, fists up, a chance, their goal; hushed after the other
	// side's. Home end behind the -x goal, away end behind +x, the sides a share of both.
	float ox = MODEL_MATRIX[3].x;
	float side = ox < -45.0 ? 0.0 : ox > 45.0 ? 1.0 : 0.5;
	float sing = mix(gn_chant.x, gn_chant.y, side);
	float fists = mix(gn_react_home.w, gn_react_away.w, side);
	float goal = gn_goal.x >= 0.0 && abs(gn_goal.x - side) < 0.6 ? 1.0 - smoothstep(6.0, 18.0, gn_goal.y) : 0.0;
	float hush = side < 0.25 ? mod(gn_mood.w, 2.0) : 0.0;
	float act = clamp(max(max(sing, fists), max(goal, gn_excite * 0.8)), 0.0, 1.0) * (1.0 - hush);
	// Quiet: two in five poles come down to rest.
	float rest = (1.0 - smoothstep(0.05, 0.4, act)) * step(h, 0.4);

	// The swing: his own pace and style; on the beat when the end sings; round and round for a goal.
	float f8 = smoothstep(0.35, 0.75, h2);
	float circ = max(step(0.82, h2), goal);
	float sp = (1.25 + h2 * 0.6) * mix(0.8, 1.35, act);
	float A = mix(0.32, 0.78, act) * (1.0 - rest) * (1.0 - gn_chant.w * 0.75);
	// The cloth lags the pole by ~0.5 rad per metre: the swing travels out along it.
	float lag = x * 0.55;
	float th = TIME * sp + ph * 6.2832;
	vec2 a = swing(th - lag, f8, circ);
	vec2 da = swing(th - lag + 0.1, f8, circ) - a;
	float beatK = smoothstep(0.0, 0.5, sing) * (1.0 - gn_chant.w);
	if (beatK > 0.01) {
		float bt = (gn_chant.z + h * 0.03) * 6.2832;
		vec2 ab = swing(bt - lag, f8, circ);
		a = mix(a, ab, beatK);
		da = mix(da, swing(bt - lag + 0.1, f8, circ) - ab, beatK);
	}
	a *= A;
	// Resting: leant on the shoulder to one side, held lower.
	a.x += rest * (h < 0.2 ? 0.55 : -0.55);
	a.y += rest * 0.25;
	// How fast the cloth here is moving through the air: flat out when fast, draped when slow.
	float speed = length(da) * 10.0 * sp * A;
	float fly = clamp(speed * 0.75 + 0.12, 0.0, 1.0) * (1.0 - rest * 0.9);
	float hang = 1.0 - fly;

	// The cloth in its own plane: draped down from the pole as it slows, the fly end sagging.
	float droop = hang * 1.1 * (0.3 + 0.7 * k);
	vec2 off = vec2(x * cos(droop), -x * sin(droop) - k * k * 0.25 * hang);
	p.xy = vec2(p.x * (1.0 - cloth), p.y) + off;
	// Ripples running out to the fly end (quicker and finer when flying), and slack folds hanging.
	float t = TIME * (3.0 + 3.0 * fly);
	float ph1 = x * 2.6 - t + ph * 6.2832;
	float ph2 = x * 5.6 - t * 1.7 + p.y * 2.2 + ph * 8.17;
	float amp = (0.08 + 0.16 * fly) * pow(k, 1.2);
	float folds = sin(x * 7.0 + p.y * 1.3 + ph * 5.0) * 0.12 * k * hang;
	p.z += (sin(ph1) * 0.65 + sin(ph2) * 0.3) * amp + folds;
	float slope = (cos(ph1) * 1.69 + cos(ph2) * 1.68) * amp + cos(x * 7.0 + p.y * 1.3 + ph * 5.0) * 0.84 * k * hang;

	mat3 r = lean(a);
	p = r * p;
	p.y -= rest * 0.5;
	shade = cloth > 0.5 ? clamp(0.86 - slope * 0.32, 0.5, 1.2) : 1.0;
	VERTEX = p;
	wn = normalize((MODEL_MATRIX * vec4(r * normalize(vec3(-slope, 0.0, 1.0)), 0.0)).xyz);
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
	else if (pat < 6.5) b = step(0.5, fract(u.y * 2.5));                       // hoops
	else if (pat < 7.5) b = step(0.5, fract(u.x * 3.5));                       // stripes
	else if (pat < 8.5) b = step(u.x * 0.62 + 0.19, u.y);                      // diagonal halves
	else b = max(step(abs(u.y - 0.5), 0.1), step(abs(u.x - 0.36), 0.065));     // cross
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
