// Goal explosions (World/GoalFx.cs): every piece of every explosion in one MultiMesh.
// Nothing blends (the post pass reads depth): soft edges are an ordered dither, offset per
// piece so overlapping ones build up, and everything is self-lit unless it says otherwise.
//
// Per instance (the transform's columns are free data, not a transform):
//   column 3 = position, column 0 = vector A, column 1 = second colour, column 2 = vector B
//   COLOR = (colour, kind), INSTANCE_CUSTOM = (size, alpha, seed, w)
// Kinds:
//   0 streak  a spark drawn along A (its motion), size = width; hot core
//   1 puff    a cel-shaded billow; B.x 1 = smoke (lit by the sky), 0 = fire (hot core); w = age
//   2 glow    a hot core in a dithered halo
//   3 ring    flat on axes A, B (or facing the camera, radius = size when A is zero);
//             w = thickness + 2 * shape (0 band, 1 soft disc, 2 square band)
//   4 chip    a solid little thing turned by w; B.x = shape (+16 = lit by the sky):
//             0 tumbling square, 1 shard, 2 voxel, 3 coin, 4 star, 5 rock, 6 flake,
//             7 balloon, 8 ice spike (stands on its position)
//   5 beam    from the position to position + A, size = width; hot core
//   6 orb     a disc with a bright rim, swirling; w = time
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

#include "res://World/Shaders/world.gdshaderinc"

varying flat vec4 col;
varying flat vec4 cus;
varying flat vec4 hot;
varying vec2 c;
varying float dist;

float bayer2(vec2 a) { a = floor(a); return fract(dot(a, vec2(0.5, a.y * 0.75))); }
float bayer4(vec2 p) { return bayer2(0.5 * p) * 0.25 + bayer2(p); }

void vertex() {
	col = COLOR;
	cus = INSTANCE_CUSTOM;
	vec3 A = MODEL_MATRIX[0].xyz;
	vec3 B = MODEL_MATRIX[2].xyz;
	vec3 o = MODEL_MATRIX[3].xyz;
	hot = vec4(MODEL_MATRIX[1].xyz, B.x);
	c = VERTEX.xy;
	float kind = col.a;
	float size = cus.x;
	vec4 mv;
	if (kind > 2.5 && kind < 3.5 && dot(A, A) > 1e-6) {
		mv = VIEW_MATRIX * vec4(o + A * c.x + B * c.y, 1.0);
	} else if (kind < 0.5 || (kind > 4.5 && kind < 5.5)) {
		vec4 m0 = VIEW_MATRIX * vec4(o, 1.0);
		vec4 m1 = VIEW_MATRIX * vec4(o + A, 1.0);
		vec2 d = m1.xy - m0.xy;
		float l = length(d);
		vec2 dir = l > 1e-4 ? d / l : vec2(1.0, 0.0);
		vec2 n = vec2(-dir.y, dir.x);
		mv = mix(m0, m1, c.x * 0.5 + 0.5);
		mv.xy += n * c.y * size * 0.5 + dir * c.x * size * 0.5;
	} else {
		mv = VIEW_MATRIX * vec4(o, 1.0);
		vec2 q = c * size * 0.5;
		if (kind > 1.5 && kind < 2.5) {
			// Glows sit a little toward the camera so the net and the posts don't cut them.
			mv.xyz += normalize(-mv.xyz) * 1.2;
		} else if (kind > 3.5 && kind < 4.5) {
			float shape = mod(B.x, 16.0);
			if (shape > 7.5) {
				// An ice spike stands on its foot, taller than wide.
				q = vec2(c.x * 0.32, c.y * 0.5 + 0.5) * size;
			} else {
				// Squares and coins tumble (their width flickers), the rest turn.
				float w = (shape < 0.5 || (shape > 2.5 && shape < 3.5)) ? max(0.18, abs(cos(cus.w * 1.7))) : 1.0;
				float a = (shape > 1.5 && shape < 2.5) || shape > 6.5 ? 0.0 : cus.w;
				q = mat2(vec2(cos(a), sin(a)), vec2(-sin(a), cos(a))) * (q * vec2(w, 1.0));
			}
		}
		mv.xy += q;
	}
	dist = length(mv.xyz);
	POSITION = PROJECTION_MATRIX * mv;
}

void fragment() {
	float kind = col.a;
	float a = cus.y;
	float r = length(c);
	vec2 fc = FRAGCOORD.xy + floor(cus.z * vec2(37.0, 91.0));
	float th = bayer4(fc);
	vec3 o;
	if (kind < 0.5) {
		// Streak: a hot white head in a coloured sheath, tapering away behind it.
		float along = c.x * 0.5 + 0.5;
		float across = abs(c.y) / (1.0 - along * 0.75);
		if (across > 1.0 || a * (1.0 - along * 0.5) < th) discard;
		o = mix(col.rgb, hot.rgb, step(across, 0.45) * step(along, 0.6)) * 1.5;
	} else if (kind < 1.5) {
		// Puff: a cluster of round lobes turning as it grows, in three flat bands of light.
		float age = cus.w;
		float sd = cus.z * 40.0;
		float rot = cus.z * 6.2832 + age * 1.8;
		float m = 1.0 - r / 0.6;
		float top = 0.0;
		for (int i = 0; i < 5; i++) {
			float an = rot + float(i) * 1.2566;
			vec2 off = vec2(cos(an), sin(an)) * 0.4 * (0.75 + 0.25 * sin(sd + float(i) * 1.7));
			float rr = 0.42 + 0.1 * sin(sd * 1.3 + float(i) * 2.3);
			float d = 1.0 - length(c - off) / rr;
			if (d > m) { m = d; top = off.y; }
		}
		if (m <= 0.0) discard;
		if (a < th || (m < 0.18 && a * 0.75 < th)) discard;
		float lit = clamp(0.45 + 0.45 * c.y + 0.25 * top + 0.3 * m, 0.0, 1.0);
		lit = floor(lit * 3.0 + 0.5) / 3.0;
		if (hot.a > 0.5) {
			vec3 light = gn_amb_sky * 1.1 + gn_amb_ground * 0.5 + gn_sun * 0.45 + gn_flood_col * gn_flood * 0.5 + weather_flash();
			o = world_fog(col.rgb * mix(0.5, 1.12, lit) * light, dist, 0.04);
		} else {
			float core = step(0.42, m + lit * 0.18);
			o = mix(col.rgb, hot.rgb, core) * mix(0.7, 1.25, lit) * 1.25;
		}
	} else if (kind < 2.5) {
		// Glow.
		float core = 1.0 - smoothstep(0.12, 0.22, r);
		float halo = pow(max(0.0, 1.0 - r), 1.8) * a;
		if (core * a < 0.4 && halo < th + 0.05) discard;
		o = mix(col.rgb * 1.3, hot.rgb * 1.8, core);
	} else if (kind < 3.5) {
		// Ring.
		float shape = floor(cus.w * 0.5);
		float thick = cus.w - shape * 2.0;
		if (shape > 0.5 && shape < 1.5) {
			// A soft disc of light (or scorch) on the grass.
			if (r > 1.0 || a * (1.0 - r * r) < th) discard;
			o = mix(col.rgb, hot.rgb, 1.0 - smoothstep(0.0, 0.6, r));
		} else {
			float d = shape > 1.5 ? max(abs(c.x), abs(c.y)) : r;
			float inner = 1.0 - thick;
			if (d > 1.0 || d < inner || a < th) discard;
			float mid = 1.0 - abs((d - inner) / max(thick, 1e-3) * 2.0 - 1.0);
			o = mix(col.rgb, hot.rgb, step(0.5, mid)) * 1.3;
		}
	} else if (kind < 4.5) {
		// Chip.
		float shape = mod(hot.a, 16.0);
		bool lit = hot.a > 15.5;
		if (a < th) discard;
		vec3 base = col.rgb;
		if (shape < 0.5) {
			o = base;
		} else if (shape < 1.5) {
			// Shard: a long diamond, one face catching the light.
			if (abs(c.x) / 0.42 + abs(c.y) > 1.0) discard;
			o = c.x + c.y * 0.2 > 0.0 ? hot.rgb : base;
		} else if (shape < 2.5) {
			// Voxel: a lit top, a shaded side, a dark rim.
			float e = max(abs(c.x), abs(c.y));
			o = e > 0.8 ? base * 0.35 : c.y > 0.3 ? hot.rgb : c.x > 0.35 ? base * 0.65 : base;
		} else if (shape < 3.5) {
			// Coin: a disc, a darker rim, a glint.
			if (r > 1.0) discard;
			o = r > 0.72 ? base * 0.65 : length(c - vec2(-0.25, 0.3)) < 0.25 ? hot.rgb : base;
		} else if (shape < 4.5) {
			// Star: five points.
			float an = atan(c.y, c.x) + 1.5708;
			float k = abs(fract(an * 0.7958 + 0.5) - 0.5) * 2.0;
			if (r > mix(1.0, 0.42, k)) discard;
			o = r < 0.35 ? hot.rgb : base;
		} else if (shape < 5.5) {
			// Rock: a lumpy blob, lit from the top, its hot heart showing through cracks.
			float an = atan(c.y, c.x);
			float edge = 0.78 + 0.16 * sin(an * 3.0 + cus.z * 20.0) + 0.08 * sin(an * 5.0 + cus.z * 9.0);
			if (r > edge) discard;
			float crack = step(0.82, abs(sin(c.x * 7.0 + cus.z * 30.0) + sin(c.y * 6.0 - cus.z * 11.0)));
			o = mix(base * (c.y > 0.15 ? 1.15 : 0.7), hot.rgb, crack);
		} else if (shape < 6.5) {
			// Flake: a six-armed star of thin lines.
			if (r > 1.0) discard;
			vec2 p = abs(c);
			float d1 = p.x;
			float d2 = abs(dot(c, vec2(0.866, 0.5)));
			float d3 = abs(dot(c, vec2(0.866, -0.5)));
			if (min(d1, min(d2, d3)) > 0.17) discard;
			o = base;
		} else if (shape < 7.5) {
			// Balloon: round, a highlight, a knot and a string.
			vec2 b = (c - vec2(0.0, 0.22)) * vec2(1.15, 1.0);
			bool body = length(b) < 0.72;
			bool str = c.y < -0.5 && abs(c.x - 0.08 * sin(c.y * 9.0 + cus.w)) < 0.06;
			if (!body && !str) discard;
			o = str ? vec3(0.85) : length(b - vec2(-0.25, 0.28)) < 0.17 ? hot.rgb : base;
		} else {
			// Ice spike: a tall point, two faces.
			float w = 0.5 * (1.0 - (c.y * 0.5 + 0.5));
			if (abs(c.x) > w * 2.0) discard;
			o = c.x > 0.0 ? hot.rgb : base;
			if (abs(c.x) > w * 1.6) o *= 0.75;
		}
		if (lit) o *= world_light(vec3(0.0, 1.0, 0.0), 1.0) + gn_flood_col * gn_flood * 0.45 + vec3(0.2);
	} else if (kind < 5.5) {
		// Beam: a white-hot core in a coloured sheath.
		float across = abs(c.y);
		if (a < th) discard;
		o = mix(col.rgb, hot.rgb, 1.0 - step(0.4, across)) * 1.45;
	} else {
		// Orb: darkness with a burning, swirling rim.
		if (r > 1.0) discard;
		float an = atan(c.y, c.x);
		float swirl = 0.5 + 0.5 * sin(an * 3.0 - cus.w * 9.0 + r * 10.0);
		float rim = smoothstep(0.62, 0.92, r + swirl * 0.12);
		if (a < th) discard;
		o = mix(col.rgb, hot.rgb * 1.6, rim);
	}
	ALBEDO = o;
}
