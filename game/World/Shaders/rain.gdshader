// Rain (World/RainView.cs): streaks and splashes placed by the GPU from a seed per drop
// (COLOR), around a box in front of the camera. VERTEX.z = kind (0 streak, 1 splash), UV.x
// the side of the line, UV.y head (0) or tail (1). Lines are a set width in screen pixels.
// Dithered, no depth writes. It draws after the post pass (RainView sets the render priority:
// a pass that writes no depth sits with the see-through ones, and the post quad would paint
// over it), so the colour here is already the final, graded one.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled, depth_draw_never, fog_disabled;

#include "res://World/Shaders/world.gdshaderinc"

const float BX = 90.0;
const float BY = 30.0;
const float BZ = 80.0;

varying float a;
varying flat float kind;

float bayer2(vec2 q) { q = floor(q); return fract(dot(q, vec2(0.5, q.y * 0.75))); }
float bayer4(vec2 p) { return bayer2(0.5 * p) * 0.25 + bayer2(p); }
float wrapc(float v, float c, float size) { return c + (fract((v - c) / size + 0.5) - 0.5) * size; }

void vertex() {
	vec4 s = COLOR;
	kind = VERTEX.z;
	vec3 cam = CAMERA_POSITION_WORLD;
	vec3 fwd = -INV_VIEW_MATRIX[2].xyz;
	vec2 centre = cam.xz + normalize(fwd.xz + vec2(0.0001)) * 30.0;
	vec2 wind = vec2(0.35, 0.25);
	vec3 head;
	vec3 tail;
	if (kind < 0.5) {
		float speed = 8.5 + s.w * 3.0;
		vec3 vel = vec3(wind.x * 2.2, -speed, wind.y * 2.2);
		float y = fract(s.y - TIME * speed / BY) * BY;
		float fall = (BY - y) / speed;
		vec3 p = vec3(s.x * BX + vel.x * fall, y, s.z * BZ + vel.z * fall);
		p.x = wrapc(p.x, centre.x, BX);
		p.z = wrapc(p.z, centre.y, BZ);
		head = p;
		tail = p - vel * 0.11;
		float d = distance(p, cam);
		// Bright up in the floodlight, gone close to the lens and into the haze.
		a = (0.55 + 0.45 * smoothstep(2.0, 25.0, p.y)) * smoothstep(2.0, 8.0, d) * (1.0 - smoothstep(70.0, 140.0, d)) * (0.45 + 0.4 * s.w);
		a *= mix(1.0, 0.5, UV.y);
	} else {
		// A crown off the grass: up and gone in a tenth of a second, somewhere new each time.
		float period = 0.45 + s.w * 0.5;
		float t = TIME / period + s.y;
		float cyc = floor(t);
		float ph = fract(t);
		float h1 = fract(sin(cyc * 91.7 + s.z * 311.3) * 43758.5);
		float h2 = fract(sin((cyc + 0.5) * 91.7 + s.z * 311.3) * 43758.5);
		vec3 p = vec3(wrapc((s.x + h1) * 60.0, centre.x, 60.0), 0.0, wrapc((s.z + h2) * 44.0, centre.y, 44.0));
		float life = 1.0 - smoothstep(0.0, 0.22, ph);
		p.y = 0.03 + 0.08 * sin(min(ph / 0.22, 1.0) * 3.1416);
		head = p;
		tail = p;
		a = life * (1.0 - smoothstep(45.0, 100.0, distance(p, cam))) * step(abs(p.x), 57.0) * step(abs(p.z), 38.0);
	}
	mat4 vp = PROJECTION_MATRIX * VIEW_MATRIX;
	vec4 ch = vp * vec4(head, 1.0);
	vec4 ct = vp * vec4(tail, 1.0);
	vec4 cp = UV.y < 0.5 ? ch : ct;
	vec2 px = 2.0 / VIEWPORT_SIZE;
	if (kind < 0.5) {
		vec2 dir = (ch.xy / ch.w - ct.xy / ct.w) / px;
		vec2 perp = length(dir) > 0.01 ? normalize(vec2(-dir.y, dir.x)) : vec2(1.0, 0.0);
		cp.xy += perp * UV.x * 0.75 * px * cp.w;
	} else {
		cp = ch;
		cp.xy += vec2(UV.x * 0.8, UV.y * 1.6) * px * cp.w;
	}
	POSITION = cp;
}

void fragment() {
	if (a < bayer4(FRAGCOORD.xy) * 0.8 + 0.04) discard;
	vec3 c = mix(vec3(0.5, 0.57, 0.7), gn_flood_col, 0.5) * (0.3 + 0.32 * gn_flood) + weather_flash() * 1.5;
	ALBEDO = c * (kind > 0.5 ? 1.3 : 1.0);
}
