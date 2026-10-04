// The linesman's flag: a dark stick and a red-and-yellow checked cloth (vertex colour picks).
shader_type spatial;
render_mode cull_disabled, specular_disabled;

void fragment() {
	vec2 g = floor(UV * vec2(4.0, 3.0));
	vec3 cloth = mod(g.x + g.y, 2.0) < 1.0 ? vec3(1.0, 0.75, 0.05) : vec3(0.85, 0.08, 0.06);
	ALBEDO = pow(mix(vec3(0.08), cloth, COLOR.r), vec3(2.2));
	ROUGHNESS = 0.8;
}
