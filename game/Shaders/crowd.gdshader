// The stands: rows of supporters as coloured cells on dark seats, in one draw. UV is in
// metres along (x) and up (y) the terrace.
shader_type spatial;
render_mode unshaded, shadows_disabled;

uniform vec3 seat = vec3(0.07, 0.07, 0.1);
uniform vec3 team_a = vec3(0.78, 0.22, 0.23);
uniform vec3 team_b = vec3(0.94, 0.92, 0.86);
uniform float home_share = 0.6;
uniform float slope_len = 38.0;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

void fragment() {
	vec2 cell = floor(UV * vec2(1.6, 1.4));
	float h = hash(cell);
	float h2 = hash(cell + 17.3);
	vec3 shirt = mix(vec3(0.3, 0.3, 0.34), h2 < home_share ? team_a : team_b, step(0.35, h));
	float filled = step(0.15, hash(cell + 3.1));
	// Faces catch the floodlights at the front, fading into the dark at the back.
	float depth = clamp(1.0 - UV.y / slope_len, 0.0, 1.0);
	vec3 c = mix(seat, shirt * (0.55 + 0.45 * h2), filled);
	ALBEDO = pow(c * mix(0.9, 0.35, depth), vec3(2.2));
}
