// The weather (World/Atmosphere.cs): rain 0/1, how much the sun's baked shadows count, the
// drifting cloud shadows (strength, scroll offset xz) and a lightning flash. Included by
// world.gdshaderinc and light_map.gdshaderinc (guarded: a shader may include both).
#ifndef GN_WEATHER
#define GN_WEATHER
global uniform float gn_rain;
global uniform float gn_shadow;
global uniform vec4 gn_clouds;
global uniform float gn_flash;

float weather_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = fract(sin(dot(i, vec2(127.1, 311.7))) * 43758.5453);
	float b = fract(sin(dot(i + vec2(1.0, 0.0), vec2(127.1, 311.7))) * 43758.5453);
	float c = fract(sin(dot(i + vec2(0.0, 1.0), vec2(127.1, 311.7))) * 43758.5453);
	float d = fract(sin(dot(i + vec2(1.0, 1.0), vec2(127.1, 311.7))) * 43758.5453);
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// Share of the sun the clouds let through at world point w (1 = clear).
float weather_cloud(vec3 w) {
	if (gn_clouds.x <= 0.0) return 1.0;
	vec2 p = (w.xz + gn_clouds.yz) * 0.016;
	float n = weather_noise(p) * 0.65 + weather_noise(p * 2.7 + 5.3) * 0.35;
	return 1.0 - gn_clouds.x * smoothstep(0.5, 0.68, n);
}

vec3 weather_flash() { return vec3(0.75, 0.8, 1.0) * gn_flash; }
#endif
