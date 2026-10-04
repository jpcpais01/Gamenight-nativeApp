// The crowd: one upright card per fan, all in one static mesh. VERTEX is the fan's spot on
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

varying vec2 q;
varying flat vec3 shirt;
varying flat vec3 skin;
varying flat vec3 hair;
varying flat vec3 legs;
varying flat vec3 club;
varying flat vec3 light;
varying flat vec3 card;
varying flat float arms;
varying flat float scarf;
varying flat float hairOn;
varying flat float torch;
varying flat float cardOn;
varying float dist;

float h(float s, float k) { return fract(sin(s * 12.9898 + k * 78.233) * 43758.5453); }

void vertex() {
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
	scarf = step(h(s, 5.5), max(max(max(ultra, awayEnd) * 0.7, ex2 * 0.75), sing * 0.95)) * (1.0 - ours) * (1.0 - viking);
	arms = max(max(scarf, ultra * step(0.6, beat) * step(0.5, h(s, 7.7))), ours * step(0.15, h(s, 9.9)));
	arms = max(arms, viking * step(h(s, 2.2) * 0.25, gn_chant.z + 0.3));

	// Card display for the kick-off: held up overhead, edge to edge, they make the picture.
	cardOn = 0.0;
	card = vec3(0.0);
	if (UV2.x >= 0.0 && gn_tifo > h(s, 4.1) * 0.7 + 0.15) {
		cardOn = step(h(s, 8.8), 0.985);
		card = textureLod(gn_atlas, UV2, 0.0).rgb * (0.86 + 0.14 * h(s, 4.4));
	}

	float w = COLOR.r * 0.98;
	vec3 face = normalize(NORMAL);
	vec3 right = normalize(vec3(-face.z, 0.0, face.x));
	float cx = UV.x * w;
	float cy = UV.y * 2.25 * tall;
	q = vec2(cx / wide, cy / tall);
	VERTEX = root + right * (cx + sin(t * 1.3 + ph) * 0.02) + vec3(0.0, cy - (1.0 - stand) * 0.44 + jump, 0.0) + face * 0.05;
	dist = length((MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz);

	// Dressed by his hash.
	float hs = h(s, 7.1);
	float share = mix(0.55, 0.85, max(ultra, awayEnd));
	shirt = hs < share ? club * (0.78 + 0.22 * h(s, 2.0))
		: hs < share + 0.12 ? vec3(0.7, 0.67, 0.6)
		: hs < share + 0.22 ? vec3(0.025, 0.028, 0.035)
		: vec3(0.09, 0.11, 0.17);
	skin = mix(vec3(0.84, 0.53, 0.32), vec3(0.15, 0.065, 0.028), h(s, 9.2));
	float hh = h(s, 3.9);
	hair = mix(vec3(0.007, 0.005, 0.004), vec3(0.17, 0.09, 0.03), hh * hh);
	hairOn = step(0.3, h(s, 0.7));
	legs = mix(vec3(0.012, 0.016, 0.03), vec3(0.07, 0.07, 0.08), h(s, 4.7));

	// Light: sky and floodlights, and the sun where the stands don't shade him.
	float shade = COLOR.b;
	light = (gn_amb_sky * 1.05 + gn_amb_ground * 0.4 + gn_sun * 0.5 * COLOR.a + gn_flood_col * gn_flood * 0.55) * shade;
	// Flares: a hot red glow on everyone near one, stronger as it gets dark.
	for (int i = 0; i < flare_n; i++) {
		vec3 d = flares[i].xyz - (root + vec3(0.0, 1.4, 0.0));
		light += vec3(1.0, 0.32, 0.12) * flares[i].w * (0.3 + gn_night * 0.9) * 2.0 / (1.0 + dot(d, d) * 0.12);
	}
	// Phone torches once it's dark.
	torch = step(0.9965, h(s + floor(t * 3.0 + h(s, 0.5) * 10.0), 5.0)) * gn_night;
}

void fragment() {
	float x = q.x;
	float y = q.y;
	float ax = abs(x);
	vec3 c;
	float glow = 0.0;
	if (cardOn > 0.5 && ax < 0.29 && y > 1.2 && y < 1.95) {
		c = card;
	} else {
		vec2 hq = vec2(x, (y - 1.6) * 0.92);
		bool up = arms > 0.5;
		if (scarf > 0.5 && ax < 0.27 && y > 1.86 && y < 1.97) {
			c = mix(club, vec3(0.88, 0.85, 0.77), step(0.5, fract(x * 6.0 + 0.25)));
		} else if (length(hq) < 0.105) {
			c = mix(skin, hair, step(0.04, hq.y) * hairOn) * (1.0 - 0.25 * smoothstep(0.05, 0.105, ax));
			glow = torch;
		} else if (up ? (abs(ax - 0.21) < 0.045 && y > 1.32 && y < 1.92) : (abs(ax - 0.225) < 0.04 && y > 1.0 && y < 1.42)) {
			float hand = up ? step(1.84, y) : step(y, 1.07);
			c = mix(shirt * 0.85, skin, hand);
		} else if (y > 0.86 && y < 1.46 && ax < 0.2 - max(0.0, y - 1.38) * 1.3) {
			c = shirt * (1.0 - 0.28 * smoothstep(0.09, 0.2, ax));
		} else if (y > 0.0 && y < 0.88 && abs(ax - 0.075) < 0.068) {
			c = legs;
		} else {
			discard;
		}
		c *= 0.62 + 0.38 * smoothstep(0.0, 1.7, y);
	}
	ALBEDO = world_fog(c * light + vec3(1.0, 0.97, 0.9) * glow * 1.6, dist, 0.04);
}
