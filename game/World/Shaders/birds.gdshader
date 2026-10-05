// The ground's birds (Birds.cs): one MultiMesh. The instance places the bird and tints it;
// INSTANCE_CUSTOM = (wing beat angle, how spread the wings are, peck). Per vertex: UV.x = 0 left
// wing, 1 right wing, 0.5 the rest; UV.y = 1 on the head; COLOR shades the parts.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

#include "res://World/Shaders/world.gdshaderinc"

varying vec3 col;
varying float dist;

void vertex() {
	vec3 p = VERTEX;
	float side = UV.x * 2.0 - 1.0;
	if (abs(side) > 0.5) {
		// The wing hinges at the shoulder: beaten up and down, folded along the flank on the ground.
		float span = (abs(p.x) - 0.04) * mix(0.3, 1.0, INSTANCE_CUSTOM.y);
		float a = INSTANCE_CUSTOM.x;
		p.x = side * (0.04 + span * cos(a));
		p.y = 0.155 + span * sin(a);
		p.z *= mix(0.8, 1.0, INSTANCE_CUSTOM.y);
	}
	// Pecking: the head dips to the grass.
	if (UV.y > 0.5) {
		p.y -= INSTANCE_CUSTOM.z * 0.1;
		p.z += INSTANCE_CUSTOM.z * 0.04;
	}
	VERTEX = p;
	col = COLOR.rgb;
	dist = length((MODELVIEW_MATRIX * vec4(p, 1.0)).xyz);
}

void fragment() {
	vec3 light = world_light(vec3(0.0, 1.0, 0.0), 1.0) + gn_flood_col * gn_flood * 0.45;
	ALBEDO = world_fog(col * light, dist, 0.1);
}
