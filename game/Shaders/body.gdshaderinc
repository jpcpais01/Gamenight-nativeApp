// The footballers (the PWA's players.ts materials), one shader for every part. Each part is
// one MultiMesh: the instance transform places it on the skeleton, the instance colour is its
// base colour (linear), INSTANCE_CUSTOM carries the soft-body bend (torso, thigh: xyz) or the
// toe break (boot: x) and the player's id (w), which indexes the per-player kit details.
//
// part: 0 plain, 1 torso, 2 head, 3 sleeve, 4 forearm, 5 shorts leg, 6 thigh, 7 shin, 8 boot.

uniform int part = 0;
// Per player (linear colours): torso a = trim + number, b = number colour + pattern;
// sleeve a = trim + captain's band, b = skin; forearm a = cuff; shorts, shin, boot a = trim.
uniform vec4 ka[22];
uniform vec4 kb[22];

#include "res://World/Shaders/light_map.gdshaderinc"

varying vec3 base;
varying vec3 wpos;
varying flat int pid;

mat3 bendRot(vec3 a) {
	float cx = cos(a.x), sx = sin(a.x), cy = cos(a.y), sy = sin(a.y), cz = cos(a.z), sz = sin(a.z);
	mat3 rx = mat3(vec3(1.0, 0.0, 0.0), vec3(0.0, cx, sx), vec3(0.0, -sx, cx));
	mat3 ry = mat3(vec3(cy, 0.0, -sy), vec3(0.0, 1.0, 0.0), vec3(sy, 0.0, cy));
	mat3 rz = mat3(vec3(cz, sz, 0.0), vec3(-sz, cz, 0.0), vec3(0.0, 0.0, 1.0));
	return ry * rx * rz;
}

const vec3 TOE_PIVOT = vec3(0.0, -0.068, 0.105);

vec3 toeRot(vec3 v, float a) {
	float c = cos(a), s = sin(a);
	return vec3(v.x, v.y * c - v.z * s, v.y * s + v.z * c);
}

void vertex() {
	base = COLOR.rgb;
	pid = int(INSTANCE_CUSTOM.w + 0.5);
	vec3 p = VERTEX;
	if (part == 1 || part == 6) {
		// Soft-body bend: vertices turn progressively about the part's origin, so the torso
		// curves through the waist and the thigh into the knee instead of hinging.
		float t = part == 1 ? clamp(p.y / 0.5, 0.0, 1.0) : clamp(-p.y / 0.43, 0.0, 1.0);
		t = part == 1 ? t * t * (3.0 - 2.0 * t) : t * t;
		mat3 r = bendRot(INSTANCE_CUSTOM.xyz * t);
		VERTEX = r * VERTEX;
		NORMAL = r * NORMAL;
	} else if (part == 8) {
		// Toe break: the front of the boot turns about the ball of the foot.
		float w = smoothstep(TOE_PIVOT.z - 0.02, TOE_PIVOT.z + 0.025, p.z);
		float a = -INSTANCE_CUSTOM.x * w;
		VERTEX = TOE_PIVOT + toeRot(VERTEX - TOE_PIVOT, a);
		NORMAL = toeRot(NORMAL, a);
	}
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float band(float x, float a, float b) { return step(a, x) * step(x, b); }

float blob(vec2 p, vec2 c, vec2 r) { vec2 d = (p - c) / r; return 1.0 - smoothstep(0.7, 1.0, dot(d, d)); }

// Shirt designs (Teams.KitPatterns): how much of the secondary colour shows at (u, v).
// u runs round the body (0 = front centre, 0.5 = back), v up from the hem.
float kitPattern(float pat, float u, float v) {
	float uc = fract(u + 0.5) - 0.5;
	float aa = 0.006;
	if (pat < 0.5) return 0.0;
	if (pat < 1.5) return smoothstep(0.5 - aa * 8.0, 0.5 + aa * 8.0, fract(u * 9.0));
	if (pat < 2.5) return smoothstep(0.5 - aa * 5.0, 0.5 + aa * 5.0, fract(v * 5.0 + 0.25));
	if (pat < 3.5) return 1.0 - smoothstep(0.1, 0.16, abs(fract(u * 22.0) - 0.5) * 2.0);
	if (pat < 4.5) return smoothstep(-aa, aa, uc);
	if (pat < 5.5) return 1.0 - smoothstep(0.075, 0.085, abs(uc * 1.25 + (v - 0.5) * 0.9));
	if (pat < 6.5) return 1.0 - smoothstep(0.045, 0.055, abs(v - 0.66 + abs(uc) * 1.1));
	if (pat < 7.5) return abs(smoothstep(-aa, aa, uc) - smoothstep(0.5 - aa, 0.5 + aa, v));
	if (pat < 8.5) return 1.0 - smoothstep(0.1, 0.11, abs(uc));
	return smoothstep(0.75, 0.05, v);
}

// Shirt numbers: blocky 3x5 digits (at the art resolution a number is a handful of pixels).
const int DIGITS[10] = int[](31599, 29850, 29671, 31207, 18925, 31183, 31695, 18727, 31727, 31215);

float digit(vec2 p, float d) {
	vec2 g = vec2((p.x - 0.2) / 0.6, (0.92 - p.y) / 0.84);
	if (g.x < 0.0 || g.x >= 1.0 || g.y < 0.0 || g.y >= 1.0) return 0.0;
	int col = int(g.x * 3.0), row = int(g.y * 5.0);
	return float((DIGITS[int(d)] >> (row * 3 + col)) & 1);
}

void fragment() {
	vec3 c = base;
	float u = UV.x, v = UV.y;
	if (part == 1) {
		vec4 a = ka[pid], b = kb[pid];
		vec3 trim = a.rgb;
		c = mix(c, trim, clamp(kitPattern(b.a, u, v), 0.0, 1.0));
		// Collar and side panels.
		float collar = smoothstep(0.86, 0.875, v);
		float side = (1.0 - smoothstep(0.012, 0.02, abs(u - 0.25))) + (1.0 - smoothstep(0.012, 0.02, abs(u - 0.75)));
		side *= smoothstep(0.1, 0.2, v) * (1.0 - smoothstep(0.7, 0.8, v)) * step(b.a, 0.5);
		c = mix(c, trim, clamp(collar + side * 0.85, 0.0, 1.0));
		// Crest on the left chest.
		float crest = 1.0 - smoothstep(0.018, 0.024, length(vec2((u - 0.085) * 3.0, v - 0.72)));
		c = mix(c, trim, crest);
		// Number on the back.
		float n = a.a;
		vec2 nb = vec2((u - 0.5) * 6.2, (v - 0.33) / 0.24);
		float tens = floor(max(n, 0.0) / 10.0);
		float ones = max(n, 0.0) - tens * 10.0;
		float num = tens > 0.0
			? max(digit(vec2(nb.x * 1.6 + 1.0, nb.y), tens), digit(vec2(nb.x * 1.6, nb.y), ones))
			: digit(vec2(nb.x * 1.6 + 0.5, nb.y), ones);
		c = mix(c, b.rgb, num * step(0.0, n));
		// Soft folds near the waist.
		float fold = 0.04 * sin(u * 60.0 + v * 9.0) * (1.0 - smoothstep(0.0, 0.25, v));
		c *= 1.0 - fold - 0.06 * (1.0 - smoothstep(0.0, 0.08, v));
	} else if (part == 2) {
		// A calm face: brows, eyes, a hint of nose and mouth. The front is u = 0.25.
		vec2 f = vec2((u - 0.25) * 4.0, v);
		vec3 skin = c;
		float eyes = blob(vec2(abs(f.x), f.y), vec2(0.17, 0.535), vec2(0.045, 0.022));
		float brows = blob(vec2(abs(f.x), f.y), vec2(0.18, 0.6), vec2(0.07, 0.012));
		float mouth = blob(f, vec2(0.0, 0.395), vec2(0.08, 0.008));
		float nose = blob(f, vec2(0.0, 0.47), vec2(0.03, 0.05));
		float cheek = blob(vec2(abs(f.x), f.y), vec2(0.27, 0.45), vec2(0.08, 0.06));
		c *= 1.0 - nose * 0.12;
		c = mix(c, skin * vec3(1.05, 0.92, 0.9), cheek * 0.25);
		c = mix(c, skin * 0.45, brows * 0.85);
		c = mix(c, vec3(0.08, 0.06, 0.05), eyes * 0.9);
		c = mix(c, skin * vec3(0.7, 0.5, 0.48), mouth * 0.7);
		float ear = blob(vec2(abs(u - 0.5), v), vec2(0.25, 0.5), vec2(0.04, 0.06));
		c *= 1.0 - ear * 0.15;
	} else if (part == 3) {
		vec4 a = ka[pid];
		c = mix(c, a.rgb, band(v, 0.43, 0.5));
		// The captain's armband.
		c = mix(c, vec3(1.0, 0.72, 0.05), a.a * band(v, 0.26, 0.4));
		c = mix(c, kb[pid].rgb, step(0.5, v));
	} else if (part == 4) {
		c = mix(c, ka[pid].rgb, smoothstep(0.84, 0.86, v));
	} else if (part == 5) {
		c = mix(c, ka[pid].rgb, clamp((1.0 - smoothstep(0.015, 0.025, abs(u - 0.25))) * 0.9 + band(v, 0.9, 1.0) * 0.6, 0.0, 1.0));
	} else if (part == 7) {
		c = mix(c, ka[pid].rgb, clamp(band(v, 0.07, 0.11) + band(v, 0.14, 0.17), 0.0, 1.0));
	} else if (part == 8) {
		c = mix(c, ka[pid].rgb, 1.0 - smoothstep(0.018, 0.03, wpos.y));
	}
	// Ground occlusion: darker toward the grass.
	float ao = mix(0.62, 1.0, smoothstep(0.0, 0.6, wpos.y));
	ALBEDO = c * mix(0.85, 1.0, ao);
	AO = ao;
	AO_LIGHT_AFFECT = 0.0;
	ROUGHNESS = 0.75;
	// The ground's floodlights.
	EMISSION = ALBEDO * light_map_flood(wpos);
}

// The sun, kept off in the stands' shadow (the ground's baked light map), with Godot's own
// rim term (rim 0.3, tint 0.55 at this roughness) in the sunlight.
void light() {
	DIFFUSE_LIGHT += light_map_sun(LIGHT, NORMAL, LIGHT_COLOR, ATTENUATION, wpos);
	float rim = pow(max(1e-4, 1.0 - max(dot(NORMAL, VIEW), 0.0)), 4.0);
	DIFFUSE_LIGHT += rim * 0.3 * mix(vec3(1.0), ALBEDO, 0.55) * LIGHT_COLOR / PI * ATTENUATION * light_map_at(wpos).r;
}
