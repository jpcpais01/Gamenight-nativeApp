// The marker under the player you control. Opaque with a cut-out (no blending), so the
// post pass sees it like everything else.
shader_type spatial;
render_mode unshaded, shadows_disabled, cull_disabled;

uniform vec3 color = vec3(1.0, 0.85, 0.3);

void fragment() {
	float r = length(UV - 0.5) * 2.0;
	if (r > 1.0 || r < 0.72) discard;
	ALBEDO = pow(color, vec3(2.2));
}
