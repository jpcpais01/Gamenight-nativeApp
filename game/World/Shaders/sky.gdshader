// The evening sky behind the stands: a gradient from the horizon up, the sun's glow, soft
// streaky clouds low down and stars once it's dark. No TIME: the sky only changes when the
// time of day does, so Godot never re-renders it for nothing.
shader_type sky;

uniform vec3 top = vec3(0.15, 0.35, 0.8);
uniform vec3 horizon = vec3(0.95, 0.8, 0.55);
uniform vec3 sun_color = vec3(1.0, 0.9, 0.7);
uniform vec3 sun_dir = vec3(0.0, 0.5, 0.8);
uniform float stars = 0.0;
uniform float clouds = 0.5;
// Rainy night: a low overcast lit from underneath by the floodlights, lightning flashes.
uniform float overcast = 0.0;
uniform vec3 belly_glow = vec3(0.0);
uniform float flash = 0.0;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

void sky() {
	vec3 d = EYEDIR;
	float h = clamp(d.y, 0.0, 1.0);
	vec3 c = mix(horizon, top, pow(h, 0.5));
	float s = max(dot(d, normalize(sun_dir)), 0.0);
	c += sun_color * (pow(s, 6.0) * 0.25 + pow(s, 80.0) * 0.6);
	float cl = 0.0;
	if (d.y > 0.02 && d.y < 0.6) {
		vec2 cp = d.xz / max(0.08, d.y + 0.12) * 1.4;
		cl = smoothstep(0.55, 0.85, noise(cp * vec2(0.6, 2.2)) * 0.7 + noise(cp * 2.3) * 0.3);
		cl *= smoothstep(0.02, 0.12, d.y) * (1.0 - smoothstep(0.25, 0.6, d.y));
	}
	vec3 cloud = mix(horizon * 1.05, sun_color + horizon * 0.3, pow(s, 3.0) * 0.6);
	c = mix(c, cloud, cl * clouds);
	if (stars > 0.0 && d.y > 0.1) {
		vec2 sg = floor(vec2(atan(d.z, d.x) * 95.0, d.y * 130.0));
		float star = step(0.9965, hash(sg)) * smoothstep(0.1, 0.35, d.y) * (1.0 - cl);
		c += vec3(0.92, 0.94, 1.0) * star * (0.5 + 0.5 * hash(sg + 1.7)) * stars * 1.2;
	}
	if (overcast > 0.0) {
		vec2 op = d.xz / max(0.05, d.y + 0.1);
		float n = noise(op * 0.7) * 0.55 + noise(op * 1.9 + 3.1) * 0.3 + noise(op * 4.7) * 0.15;
		vec3 belly = mix(vec3(0.008, 0.01, 0.016), vec3(0.045, 0.05, 0.066), smoothstep(0.3, 0.8, n));
		// The floodlights' glow on the cloud base, strongest low over the stands.
		belly += belly_glow * (1.0 - smoothstep(0.0, 0.5, d.y)) * (0.45 + 0.8 * n);
		c = mix(c, belly, overcast);
		c += vec3(0.55, 0.6, 0.78) * flash * (0.25 + 0.9 * smoothstep(0.35, 0.75, n));
	}
	COLOR = c;
}
