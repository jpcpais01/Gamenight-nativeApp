// LED advertising boards: two-colour panels with a slow scrolling sheen.
shader_type spatial;
render_mode unshaded, shadows_disabled;

uniform vec3 a = vec3(0.12, 0.2, 0.46);
uniform vec3 b = vec3(0.95, 0.75, 0.25);

void fragment() {
	float panel = step(0.5, fract(UV.x / 12.0 + TIME * 0.02));
	float stripe = step(0.35, UV.y) * step(UV.y, 0.65) * step(0.3, fract(UV.x * 0.5));
	vec3 c = mix(a, b * 0.9, panel);
	c = mix(c, vec3(0.95), stripe * 0.6);
	ALBEDO = pow(c, vec3(2.2));
}
