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

// The crowd's mood (World/CrowdMood.cs): who's rising, who just went close, a ball gone out.
global uniform vec4 gn_mood;
global uniform vec4 gn_gasp;
global uniform vec4 gn_ballout;

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
	float zone = COLOR.g * 4.0;
	float ultra = step(0.75, zone) * step(zone, 1.25);
	float awayEnd = step(1.75, zone) * step(zone, 2.5);
	// Zone 3: the people working the match (COLOR.r: 0 steward, 1/4 photographer, 2/4 ball boy).
	float staff = step(2.5, zone);
	float role = floor(COLOR.r * 4.0 + 0.5);
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
	// Round a burning flare the fans go wild: jumping, arms and scarves up, whatever the game's doing.
	float fire = 0.0;
	for (int i = 0; i < flare_n; i++) {
		vec3 d = flares[i].xyz - root;
		fire = max(fire, step(0.3, flares[i].w) * (1.0 - smoothstep(3.0, 9.0, length(d.xz))));
	}
	fire *= step(h(s, 1.4), 0.9);
	// Calm by default: they bounce only while their end sings (or the place is at boiling point).
	float boil = max(smoothstep(0.8, 1.0, ex) * ultra, fire);
	float beat = mix(idle * boil, onBeat, singing);
	float jump = max(boil, singing) * max(0.0, beat) * (0.08 + 0.1 * max(ex, sing) + 0.1 * fire) * (1.0 - viking) * step(h(s, 2.8), 0.85)
		+ (1.0 - ultra) * max(0.0, sin(t * (7.0 + h(s, 1.1) * 3.0) + ph)) * ex2 * 0.16
		+ ours * max(0.0, sin(t * (8.5 + h(s, 2.1) * 4.0) + ph)) * 0.4;
	float stand = max(max(max(ultra * step(h(s, 6.2), 0.85), fire), awayEnd * max(step(h(s, 6.2), 0.55), singing)), clamp(ex * 2.4 - 1.25 - h(s, 6.6) * 0.5, 0.0, 1.0));
	// His team closing on goal: up out of the seat, the end they attack first, then the rest.
	float rise = team < 0.5 ? gn_mood.x : gn_mood.y;
	float dirT = team < 0.5 ? gn_mood.z : -gn_mood.z;
	float nearEnd = smoothstep(-10.0, 50.0, root.x * dirT);
	stand = max(stand, clamp(rise * (1.2 + 0.8 * nearEnd) - 0.3 - h(s, 6.6) * 0.6, 0.0, 1.0));
	// A chance that went close: his team's (mine) or theirs, gn_gasp.y seconds ago.
	float gAge = gn_gasp.y;
	float gasp = step(0.0, gn_gasp.x) * (1.0 - step(3.2 + h(s, 5.1) * 1.4, gAge));
	float mine = gasp * (1.0 - abs(gn_gasp.x - team));
	float relief = gasp * abs(gn_gasp.x - team);
	stand = max(stand, mine * step(h(s, 5.2), 0.92));
	jump += mine * max(0.0, 1.0 - gAge * 2.5) * 0.25;
	stand = max(stand, ours) * (1.0 - theirs * step(0.3, h(s, 8.1)) * (1.0 - ultra));
	float tall = 0.92 + 0.14 * h(s, 2.9);
	float wide = 0.85 + 0.3 * h(s, 1.7);
	// Scarves come up for the songs (most of a singing end), hardly ever otherwise.
	float scarf = step(h(s, 5.5), max(max(sing * 0.8, ex2 * ex2 * 0.2), fire * 0.45)) * (1.0 - ours) * (1.0 - viking);
	float arms = max(fire * step(0.3, beat) * step(0.4, h(s, 7.7)), max(singing * ultra * step(0.6, beat) * step(0.5, h(s, 7.7)), viking * step(h(s, 2.2) * 0.25, gn_chant.z + 0.3)));

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
	if (ours > 0.5) {
		float k = h(s, 9.9);
		pose = k < 0.3 ? 10.0 : k < 0.7 ? 2.0 + step(0.0, sin(t * 6.0 + ph)) : 4.0 + clapK;
	} else if (mine > 0.5 && h(s, 3.7) < 0.9) {
		// Up with the shot... then hands on heads (a few still reaching, a few pointing at the ref).
		float k = h(s, 3.9);
		pose = gAge < 0.3 + h(s, 3.8) * 0.35 ? 2.0 + step(0.5, h(s, 7.3)) : k < 0.66 ? 8.0 : k < 0.86 ? 3.0 : 11.0;
	} else if (scarf > 0.5) {
		// Scarves stretched overhead, lifted on the song's beat when his end is singing.
		pose = 6.0 + (singing > 0.5 ? step(0.5, gn_chant.z) : step(0.0, sin(t * 2.6 + ph)));
	} else if (relief > 0.5 && gn_gasp.z > 1.5 && h(s, 4.2) < 0.5) {
		// Their shot saved: applause for the keeper.
		pose = 4.0 + clapK;
	} else if (rise > 0.7 && h(s, 4.3) < (rise - 0.7) * 1.6 * (0.5 + nearEnd)) {
		// Nearly there: arms going up already.
		pose = 2.0;
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
	if (staff > 0.5) {
		// At work: the steward stands watching the stands (in a jacket), the photographer kneels
		// with the camera to his eye, the ball boy sits by the boards and is up with a ball
		// held out when it goes out near him.
		cardOn = 0.0;
		jump = 0.0;
		if (role < 0.5) {
			stand = 1.0;
			vrow = h(s, 2.6) < 0.5 ? 2.0 : 5.0;
			pose = step(0.7, sin(t * 0.5 + ph));
		} else if (role < 1.5) {
			stand = 0.0;
			vrow = floor(h(s, 1.9) * 13.99);
			pose = h(s, 2.7) < 0.75 ? 8.0 : 0.0;
		} else {
			float near = gn_ballout.z * (1.0 - step(13.0, distance(root.xz, gn_ballout.xy)));
			stand = near;
			vrow = 14.0 + step(0.5, h(s, 2.5));
			pose = near > 0.5 ? 2.0 : 0.0;
		}
	}

	float w = 0.8 * wide;
	vec3 face = normalize(NORMAL);
	vec3 right = normalize(vec3(-face.z, 0.0, face.x));
	float cx = UV.x * w;
	float cy = UV.y * 2.25 * tall;
	q = vec2(UV.x + 0.5, UV.y);
	VERTEX = root + right * (cx + sin(t * 1.3 + ph) * 0.02) + vec3(0.0, cy - (1.0 - stand) * (0.44 + staff * 0.38) + jump, 0.0) + face * 0.05;
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
	if (staff > 0.5) {
		pat = 0.0;
		if (role < 0.5) {
			// High-vis jacket, dark trousers.
			shirt = vec3(0.95, 0.62, 0.0);
			jacket = shirt;
			legs = vec3(0.012, 0.014, 0.02);
			hat = vec3(0.01);
		} else if (role < 1.5) {
			// Black, under an orange bib.
			shirt = vec3(0.85, 0.22, 0.01);
			jacket = vec3(0.015);
			legs = vec3(0.015);
			hat = vec3(0.02);
		} else {
			// The home club's tracksuit.
			shirt = gn_home * 0.9;
			jacket = gn_home * 0.9;
			legs = gn_home * 0.25;
			hat = gn_home;
		}
	}

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
	float moment = max(max(fresh, smoothstep(0.75, 1.0, gn_excite)), gasp * (1.0 - step(1.0, gAge)));
	float flashes = staff > 0.5 ? (role > 0.5 && role < 1.5 ? 0.02 + 0.3 * moment : 0.0) : 0.004 * moment;
	torch = max(torch * (1.0 - staff), step(1.0 - flashes, h(s + floor(t * 9.0 + h(s, 0.6) * 10.0), 6.0)) * 1.4);
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
	// Seen from behind (stewards face the stands): the back of the head is hair. (The sprite
	// is wound so that the side facing the pitch is the back face.)
	if (FRONT_FACING && (reg == 10 || reg == 1 && py < 15)) reg = 2;
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
