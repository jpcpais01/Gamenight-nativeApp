// Every static surface of a ground in one draw. Each vertex carries its albedo (COLOR.rgb,
// linear), the sun's baked visibility (COLOR.a), uv in metres and its look (UV2.x) with one
// parameter (UV2.y). Lighting is computed here from the time of day (no live lights, no
// shadow map reads): ambient sky/ground + the sun where it isn't shaded + floodlights.
// Signage (boards, ribbons, fascia, banners, screens) reads the shared atlas.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled, depth_draw_opaque;

#include "res://World/Shaders/world.gdshaderinc"

const float LOOK_ROOF = 1.0, LOOK_GLASS = 2.0, LOOK_CURTAIN = 3.0, LOOK_FASCIA = 4.0, LOOK_RIBBON = 5.0,
	LOOK_LAMP = 6.0, LOOK_ROOFLIGHT = 7.0, LOOK_BOARD = 8.0, LOOK_TIER = 9.0, LOOK_SCREEN = 10.0,
	LOOK_CLOTH = 11.0, LOOK_STIPPLE = 12.0, LOOK_UNLIT = 13.0, LOOK_ARCADE = 14.0, LOOK_COFFER = 15.0, LOOK_WALL = 16.0, LOOK_BRICK = 17.0;

varying vec3 wpos;
varying vec3 wnrm;
varying flat float look;
varying flat float par;
varying float dist;

vec2 px(vec2 p) { return p / 1024.0; }

void vertex() {
	look = floor(UV2.x + 0.5);
	par = UV2.y;
	vec3 v = VERTEX;
	if (look == LOOK_CLOTH) {
		// Cloth in the wind: waves run away from where it's tied and grow toward the free edge.
		bool flag = par < 0.0;
		float f = flag ? UV.x : 1.0 - UV.y;
		float seed = v.x * 0.37 + v.z * 0.61;
		float gust = 0.6 + 0.4 * sin(TIME * 0.53 + seed) * sin(TIME * 1.31 + seed * 1.7);
		float s = UV.x * (flag ? 7.0 : 9.0);
		float wave = sin(s - TIME * 3.4 + seed) * 0.62 + sin(s * 2.3 - TIME * 5.8 + UV.y * 3.0 + seed * 2.0) * 0.26;
		v += NORMAL * (flag ? 0.09 : 0.16) * gust * (pow(f, 1.4) * wave + 0.25 * f);
	}
	wpos = (MODEL_MATRIX * vec4(v, 1.0)).xyz;
	wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	VERTEX = v;
	dist = length((MODELVIEW_MATRIX * vec4(v, 1.0)).xyz);
}

void fragment() {
	vec3 n = normalize(FRONT_FACING ? wnrm : -wnrm);
	vec3 alb = COLOR.rgb;
	vec3 emis = vec3(0.0);
	float lit = 1.0;
	vec2 uv = UV;

	if (look == LOOK_ROOF) {
		// Sheeting seen from below: rafters across, purlins along.
		float rafter = 1.0 - smoothstep(0.0, 0.18, abs(fract(uv.x / 7.5) - 0.5) * 7.5);
		float purlin = 1.0 - smoothstep(0.0, 0.08, abs(fract(uv.y / 2.6) - 0.5) * 2.6);
		alb *= (0.93 + 0.07 * step(0.5, fract(uv.x / 0.9))) * (1.0 - 0.35 * rafter) * (1.0 - 0.18 * purlin);
	} else if (look == LOOK_GLASS) {
		// Hospitality boxes: dark glass by day, warm rooms at night.
		float bay = floor(uv.x / 3.2);
		float pane = step(0.08, fract(uv.x / 3.2)) * step(0.12, uv.y) * step(uv.y, 2.75);
		vec3 room = vec3(1.0, 0.62, 0.3) * (0.3 + 0.7 * world_hash(vec2(bay, 3.0))) * max(0.0, gn_flood - 0.12) * 1.1;
		emis = (gn_amb_sky * 0.1 + room) * pane;
		alb = mix(vec3(0.03), vec3(0.0), pane);
	} else if (look == LOOK_CURTAIN) {
		vec2 g = vec2(uv.x / 2.4, uv.y / 3.2);
		float mull = max(step(0.94, abs(fract(g.x) - 0.5) * 2.0), step(0.9, abs(fract(g.y) - 0.5) * 2.0));
		float on = step(0.72, world_hash(floor(g)));
		emis = vec3(1.0, 0.62, 0.3) * on * gn_flood * 0.35 * (1.0 - mull);
		alb = mix(alb, vec3(0.36, 0.38, 0.4), mull);
	} else if (look == LOOK_FASCIA) {
		alb = texture(gn_atlas, px(vec2(fract(uv.x / 48.0) * 512.0, 512.0 + (1.0 - clamp(uv.y / 2.2, 0.0, 1.0)) * 63.0))).rgb;
	} else if (look == LOOK_RIBBON) {
		// LED ribbon on the tier fronts: scrolling messages; a goal sets it pulsing in the club colour.
		vec2 t = vec2(fract((uv.x - TIME * 2.5) / 40.0) * 1024.0, 608.0 + (1.0 - clamp(uv.y / 1.3, 0.0, 1.0)) * 31.0);
		vec3 c = texture(gn_atlas, px(t)).rgb;
		float goal = step(0.0, gn_goal.x) * (1.0 - smoothstep(6.0, 10.0, gn_goal.y)) * (0.5 + 0.5 * sin(TIME * 14.0 - uv.x * 0.15));
		c = mix(c, (gn_goal.x < 0.5 ? gn_home : gn_away) * 1.3, goal * 0.8);
		c *= 0.75 + 0.25 * step(0.35, fract(uv.y * 16.0));
		emis = c * (0.45 + gn_flood * 0.7);
		alb = vec3(0.0);
	} else if (look == LOOK_LAMP) {
		vec2 g = fract(uv * vec2(5.0, 2.0)) - 0.5;
		float l = 1.0 - step(0.36, length(g));
		emis = vec3(1.0, 0.95, 0.84) * l * (0.5 + 2.4 * gn_flood);
		alb = vec3(0.04) * (1.0 - l);
	} else if (look == LOOK_ROOFLIGHT) {
		emis = vec3(1.0, 0.94, 0.82) * (0.2 + gn_flood * 1.3);
		alb = vec3(0.0);
	} else if (look == LOOK_BOARD) {
		// Pitchside LED boards: the front shows its design from the atlas and glows at night.
		if (FRONT_FACING) {
			vec3 b = texture(gn_atlas, px(vec2(clamp(uv.x / 6.0, 0.0, 1.0) * 511.0, par * 64.0 + (1.0 - clamp(uv.y / 0.9, 0.0, 1.0)) * 63.0))).rgb;
			alb = b * 0.75;
			emis = b * (0.15 + gn_flood * 0.55);
		}
	} else if (look == LOOK_TIER) {
		// The tier under the crowd: rows of seats in the seat colour, grey aisle steps,
		// dark vomitories. Mostly covered by fans; this is what shows between them.
		float row = fract(uv.y / 0.8);
		float back = step(0.5, row) * step(row, 0.85);
		alb = mix(alb * 0.45, alb, back);
		float aisle = par > 0.5 ? step(abs(fract(uv.x / 15.0) - 0.5) * 15.0, 0.55) : 0.0;
		alb = mix(alb, vec3(0.16, 0.165, 0.18) * (0.8 + 0.2 * step(0.5, row)), aisle);
		float vom = par > 1.5 ? step(abs(fract(uv.x / 30.0 + 0.25) - 0.5) * 30.0, 1.5) * step(7.2, uv.y) * step(uv.y, 10.2) : 0.0;
		alb = mix(alb, vec3(0.002), vom);
	} else if (look == LOOK_SCREEN) {
		vec3 s = texture(gn_atlas, px(vec2(512.0 + clamp(uv.x, 0.0, 1.0) * 511.0, (1.0 - clamp(uv.y, 0.0, 1.0)) * 287.0))).rgb;
		emis = s * (0.6 + gn_flood * 0.6);
		alb = vec3(0.0);
	} else if (look == LOOK_CLOTH) {
		if (par >= 0.0) {
			float col = step(4.5, par);
			float r = par - col * 5.0;
			alb = texture(gn_atlas, px(vec2(col * 512.0 + clamp(uv.x, 0.0, 1.0) * 511.0, 768.0 + r * 48.0 + (1.0 - clamp(uv.y, 0.0, 1.0)) * 47.0))).rgb;
			emis = alb * 0.12;
		}
		lit = FRONT_FACING ? 1.0 : 0.85;
	} else if (look == LOOK_STIPPLE) {
		// See-through glass without blending: one pixel in four (the post pass reads depth).
		if (mod(floor(FRAGCOORD.x), 2.0) + mod(floor(FRAGCOORD.y), 2.0) > 0.5) discard;
	} else if (look == LOOK_ARCADE) {
		// Arches: piers and round-headed openings, par = bay + 1000 x storey (metres, storey
		// to a tenth). Dark inside by day; at night most of them glow with the concourse lights.
		float s10 = floor(par / 100.0);
		float storey = s10 / 10.0;
		float bay = par - s10 * 100.0;
		vec2 size = vec2(bay, storey);
		vec2 cell = floor(uv / size);
		vec2 c = (fract(uv / size) - vec2(0.5, 0.0)) * size;
		float r = bay * 0.3;
		float spring = storey * 0.8 - r;
		float open = step(storey * 0.12, c.y) * max(step(abs(c.x), r) * step(c.y, spring), step(length(vec2(c.x, c.y - spring)), r));
		float on = step(0.3, world_hash(cell));
		float stone = 0.94 + 0.08 * world_hash(cell + 7.1);
		alb *= mix(stone * (1.0 + 0.14 * step(c.y, 0.3)), 0.06, open);
		emis = vec3(1.0, 0.55, 0.2) * open * on * gn_flood * 0.5;
	} else if (look == LOOK_COFFER) {
		// A cast concrete canopy's underside: coffers between the ribs.
		vec2 k = fract(uv / vec2(3.2, 2.6));
		float rib = max(step(0.86, k.x), step(0.84, k.y));
		alb *= mix(0.84, 1.0, rib) * mix(0.88, 1.0, smoothstep(0.0, 14.0, uv.y));
		emis = alb * 0.12;
	} else if (look == LOOK_WALL) {
		// The pitch wall: painted in the club colour (COLOR), a white band along the top.
		alb = mix(alb, vec3(0.83, 0.81, 0.75), step(1.05, uv.y)) * (1.0 - 0.18 * step(0.96, fract(uv.x / 2.0)));
	} else if (look == LOOK_BRICK) {
		// Old brick: courses, uneven firing, soot low down.
		vec2 b = vec2(uv.x / 0.9, uv.y / 0.32);
		b.x += 0.5 * step(0.5, fract(b.y * 0.5));
		float mortar = max(step(0.9, fract(b.y)), step(0.94, fract(b.x)));
		alb *= mix(0.86 + 0.2 * world_hash(floor(b)), 1.35, mortar) * (1.0 - 0.25 * smoothstep(3.0, 0.0, uv.y));
	} else if (look == LOOK_UNLIT) {
		emis = alb;
		alb = vec3(0.0);
	}

	vec3 light = world_light(n, COLOR.a) + gn_flood_col * gn_flood * 0.3;
	vec3 c = alb * light * lit + emis;
	ALBEDO = world_fog(c, dist, 0.06);
}
