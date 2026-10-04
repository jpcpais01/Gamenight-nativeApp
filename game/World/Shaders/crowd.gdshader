// The crowd: one upright sprite per fan (CrowdSprites: 16 people x 12 poses, each texel a
// region to colour), all in one static mesh. VERTEX is the fan's spot on
// his step, NORMAL the way he faces (the pitch), UV the card corner, UV2 his card in the
// tifo (atlas uv, or -1), COLOR = (seat width, zone/2, roof shade, sun visibility). Zones:
// 0 side stands (seated, mixed), 1 home end (the ultras: standing, bouncing), 2 away end.
// Everything about him comes from a hash of where he stands; the match's mood (excitement,
// goals, the tifo) comes from the shared globals.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

#include "res://World/Shaders/world.gdshaderinc"

// Flares burning in the stands: position and strength (with its flicker).
uniform vec4 flares[8];
uniform int flare_n = 0;

uniform sampler2D sprites : filter_nearest;

// Few varyings (mobile GPUs have as few as 16 slots, and a shader over the limit doesn't
// draw at all): the fan's colours go packed four to a uvec4, 8 bits a channel.
varying vec2 q;
varying float dist;
varying flat vec4 misc;   // pose, person row, shirt pattern, torch glow
varying flat vec3 light;
varying flat uvec4 colA;  // shirt, second shirt colour, jacket, hat
varying flat uvec4 colB;  // skin, hair, legs, club
varying flat uvec2 colC;  // card, whether he holds it up

float h(float s, float k) { return fract(sin(s * 12.9898 + k * 78.233) * 43758.5453); }

void vertex() {
	float pose, vrow, pat, torch, cardOn;
	vec3 shirt, shirt2, jacket, hat, skin, hair, legs, club, card;
	vec3 root = VERTEX;
	float s = fract(sin(dot(root.xz, vec2(12.9898, 78.233)) + root.y * 3.7) * 43758.5453) * 100.0;
	float zone = COLOR.g * 2.0;
	float ultra = step(0.75, zone) * step(zone, 1.25);
	float awayEnd = step(1.75, zone);
	// Whose side he's on: the ends are their own fans', the side stands mostly home.
	float team = awayEnd > 0.5 ? step(h(s, 1.0), 0.95) : ultra > 0.5 ? step(0.98, h(s, 1.0)) : step(0.8, h(s, 1.0));
	club = mix(gn_home, gn_away, team);

	// The match's mood for him.
	float goal = step(0.0, gn_goal.x);
	float fresh = goal * (1.0 - smoothstep(9.0, 14.0, gn_goal.y));
	float ours = fresh * (1.0 - abs(gn_goal.x - team));
	float theirs = fresh * abs(gn_goal.x - team);
	float ex = gn_excite * (1.0 - theirs);
	float ex2 = ex * ex;
	float ph = h(s, 3.3) * 6.2832;
	float t = TIME;

	// When an end sings it bounces on the song's beat, rippling back up the rows; between songs
	// the ultras keep their own bounce. For the Viking clap every arm goes up between booms.
	float sing = ultra * gn_chant.x + awayEnd * gn_chant.y;
	float singing = step(0.05, sing);
	float viking = gn_chant.w * singing;
	float idle = sin(t * 7.5 - root.y * 0.5 + h(s, 4.0) * 0.6);
	float onBeat = sin((gn_chant.z - root.y * 0.025 - h(s, 0.3) * 0.06) * 6.2832);
	float beat = mix(idle * ultra, onBeat, singing);
	float jump = max(ultra, singing) * max(0.0, beat) * (0.08 + 0.1 * max(ex, sing)) * (1.0 - viking)
		+ (1.0 - ultra) * max(0.0, sin(t * (7.0 + h(s, 1.1) * 3.0) + ph)) * ex2 * 0.16
		+ ours * max(0.0, sin(t * (8.5 + h(s, 2.1) * 4.0) + ph)) * 0.4;
	float stand = max(max(ultra, awayEnd * max(0.7, singing)), clamp(ex * 2.4 - 1.25 - h(s, 6.6) * 0.5, 0.0, 1.0));
	stand = max(stand, ours) * (1.0 - theirs * step(0.3, h(s, 8.1)) * (1.0 - ultra));
	float tall = 0.92 + 0.14 * h(s, 2.9);
	float wide = 0.85 + 0.3 * h(s, 1.7);
	float scarf = step(h(s, 5.5), max(max(max(ultra, awayEnd) * 0.7, ex2 * 0.75), sing * 0.95)) * (1.0 - ours) * (1.0 - viking);
	float arms = max(ultra * step(0.6, beat) * step(0.5, h(s, 7.7)), viking * step(h(s, 2.2) * 0.25, gn_chant.z + 0.3));

	// Card display for the kick-off: held up overhead, edge to edge, they make the picture.
	cardOn = 0.0;
	card = vec3(0.0);
	if (UV2.x >= 0.0 && gn_tifo > h(s, 4.1) * 0.7 + 0.15) {
		cardOn = step(h(s, 8.8), 0.985);
		card = textureLod(gn_atlas, UV2, 0.0).rgb * (0.86 + 0.14 * h(s, 4.4));
	}

	// His pose (CrowdSprites.Pose): standing about, arms up or the V on the beat, the scarf held
	// up, clapping when it gets going, a fist pump or clapping for a goal, hands on the head
	// when they concede, now and then pointing; the card for the tifo.
	float clapK = step(0.0, sin(t * 13.0 + ph));
	pose = step(0.55, sin(t * 0.7 + ph));
	if (scarf > 0.5) {
		pose = 6.0 + step(0.0, sin(t * 2.6 + ph));
	} else if (ours > 0.5) {
		float k = h(s, 9.9);
		pose = k < 0.3 ? 10.0 : k < 0.7 ? 2.0 + step(0.0, sin(t * 6.0 + ph)) : 4.0 + clapK;
	} else if (arms > 0.5) {
		pose = 2.0 + step(0.5, h(s, 7.3));
	} else if (theirs > 0.5 && h(s, 8.3) < 0.45 && gn_goal.y < 7.0) {
		pose = 8.0;
	} else if (h(s, 6.1) < 0.4 * smoothstep(0.45, 0.8, ex)) {
		pose = 4.0 + clapK;
	} else if (step(0.994, h(s + floor(t * 0.4 + h(s, 0.9) * 10.0), 7.0)) > 0.5) {
		pose = 11.0;
	}
	if (cardOn > 0.5) pose = 9.0;
	// Who he is: one of the 16 people (the kids are rare).
	vrow = h(s, 2.4) < 0.06 ? 14.0 + step(0.5, h(s, 2.5)) : floor(h(s, 1.9) * 13.99);

	float w = 0.8 * wide;
	vec3 face = normalize(NORMAL);
	vec3 right = normalize(vec3(-face.z, 0.0, face.x));
	float cx = UV.x * w;
	float cy = UV.y * 2.25 * tall;
	q = vec2(UV.x + 0.5, UV.y);
	VERTEX = root + right * (cx + sin(t * 1.3 + ph) * 0.02) + vec3(0.0, cy - (1.0 - stand) * 0.44 + jump, 0.0) + face * 0.05;
	dist = length((MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz);

	// Dressed by his hash.
	float hs = h(s, 7.1);
	float share = mix(0.55, 0.85, max(ultra, awayEnd));
	shirt = hs < share ? club * (0.78 + 0.22 * h(s, 2.0))
		: hs < share + 0.12 ? vec3(0.7, 0.67, 0.6)
		: hs < share + 0.22 ? vec3(0.025, 0.028, 0.035)
		: vec3(0.09, 0.11, 0.17);
	// Replica shirts: some striped or hooped in the second colour.
	pat = hs < share ? floor(h(s, 3.1) * 5.0) - 2.0 : 0.0;
	shirt2 = h(s, 3.2) < 0.5 ? vec3(0.85, 0.82, 0.74) : club * 0.35;
	float hj = h(s, 6.4);
	jacket = hj < 0.3 ? vec3(0.018, 0.02, 0.025) : hj < 0.5 ? vec3(0.03, 0.04, 0.09) : hj < 0.65 ? vec3(0.18, 0.18, 0.19)
		: hj < 0.75 ? vec3(0.07, 0.08, 0.04) : club * 0.5;
	float hk = h(s, 6.7);
	hat = hk < 0.5 ? club : hk < 0.7 ? vec3(0.02) : hk < 0.85 ? vec3(0.75, 0.73, 0.68) : vec3(0.04, 0.05, 0.12);
	skin = mix(vec3(0.84, 0.53, 0.32), vec3(0.15, 0.065, 0.028), h(s, 9.2));
	float hh = h(s, 3.9);
	hair = hh < 0.86 ? mix(vec3(0.007, 0.005, 0.004), vec3(0.17, 0.09, 0.03), hh * hh) : hh < 0.94 ? vec3(0.55, 0.4, 0.17) : vec3(0.45, 0.44, 0.42);
	float hl = h(s, 4.7);
	legs = hl < 0.45 ? mix(vec3(0.03, 0.05, 0.11), vec3(0.07, 0.1, 0.2), h(s, 4.8)) : hl < 0.85 ? vec3(0.015, 0.017, 0.022) : vec3(0.2, 0.18, 0.14);

	// Light: sky and floodlights, and the sun where the stands don't shade him.
	float shade = COLOR.b;
	light = (gn_amb_sky * 1.05 + gn_amb_ground * 0.4 + gn_sun * 0.5 * mix(1.0, COLOR.a, gn_shadow) + weather_flash() + gn_flood_col * gn_flood * 0.55) * shade;
	// Flares: a hot red glow on everyone near one, stronger as it gets dark.
	for (int i = 0; i < flare_n; i++) {
		vec3 d = flares[i].xyz - (root + vec3(0.0, 1.4, 0.0));
		light += vec3(1.0, 0.32, 0.12) * flares[i].w * (0.3 + gn_night * 0.9) * 2.0 / (1.0 + dot(d, d) * 0.12);
	}
	// Phone torches once it's dark.
	torch = step(0.9965, h(s + floor(t * 3.0 + h(s, 0.5) * 10.0), 5.0)) * gn_night;
	// Camera flashes popping round the ground on the big moments: a chance, a goal.
	float moment = max(fresh, smoothstep(0.75, 1.0, gn_excite));
	torch = max(torch, step(1.0 - 0.004 * moment, h(s + floor(t * 9.0 + h(s, 0.6) * 10.0), 6.0)) * 1.4);
	misc = vec4(pose, vrow, pat, torch);
	colA = uvec4(packUnorm4x8(vec4(shirt, 0.0)), packUnorm4x8(vec4(shirt2, 0.0)), packUnorm4x8(vec4(jacket, 0.0)), packUnorm4x8(vec4(hat, 0.0)));
	colB = uvec4(packUnorm4x8(vec4(skin, 0.0)), packUnorm4x8(vec4(hair, 0.0)), packUnorm4x8(vec4(legs, 0.0)), packUnorm4x8(vec4(club, 0.0)));
	colC = uvec2(packUnorm4x8(vec4(card, 0.0)), uint(cardOn > 0.5));
}

void fragment() {
	float pose = misc.x, vrow = misc.y, pat = misc.z, torch = misc.w;
	vec3 shirt = unpackUnorm4x8(colA.x).rgb, shirt2 = unpackUnorm4x8(colA.y).rgb, jacket = unpackUnorm4x8(colA.z).rgb, hat = unpackUnorm4x8(colA.w).rgb;
	vec3 skin = unpackUnorm4x8(colB.x).rgb, hair = unpackUnorm4x8(colB.y).rgb, legs = unpackUnorm4x8(colB.z).rgb, club = unpackUnorm4x8(colB.w).rgb;
	vec3 card = unpackUnorm4x8(colC.x).rgb;
	float cardOn = float(colC.y);
	int px = int(floor(q.x * 16.0));
	int py = int(floor((1.0 - q.y) * 45.0));
	if (px < 0 || px > 15 || py < 0 || py > 44) discard;
	vec2 rg = texelFetch(sprites, ivec2(int(pose) * 16 + px, int(vrow) * 45 + py), 0).rg;
	int reg = int(rg.r * 255.0 + 0.5);
	if (reg == 0) discard;
	vec3 c;
	float glow = 0.0;
	if (reg == 1) { c = skin; glow = torch * step(float(py), 16.0); }
	else if (reg == 2) { c = hair; glow = torch; }
	else if (reg == 3) {
		c = shirt;
		if (pat > 0.5 && pat < 1.5) c = mix(c, shirt2, float((px / 2) % 2));
		else if (pat > 1.5) c = mix(c, shirt2, float((py / 3) % 2));
	}
	else if (reg == 4) c = jacket;
	else if (reg == 5) c = legs;
	else if (reg == 6) c = vec3(0.03, 0.03, 0.035);
	else if (reg == 7) { c = hat; glow = torch; }
	else if (reg == 8) c = mix(club, vec3(0.88, 0.85, 0.77), float(((px + py) / 2) % 2));
	else if (reg == 9) c = cardOn > 0.5 ? card : shirt;
	else c = vec3(0.03, 0.025, 0.02);
	c *= rg.g * 2.0;
	c *= 0.68 + 0.32 * smoothstep(0.0, 0.75, q.y);
	ALBEDO = world_fog(c * light + vec3(1.0, 0.97, 0.9) * glow * 1.6, dist, 0.04);
}
