// Players: one instanced draw. Each vertex carries its colour slot (COLOR.r) and its body
// part + pivot height (UV2); the instance carries the run phase, run amount and kit
// and skin tone (INSTANCE_CUSTOM). Legs and arms swing in the vertex
// shader, so the whole squad animates with no per-player CPU work beyond one transform.
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;

// Kits: 0 home, 1 away, 2 home keeper, 3 away keeper. Linear colours (set from C#).
uniform vec3 shirt[4];
uniform vec3 trim[4];
uniform vec3 shorts[4];
uniform vec3 socks[4];
uniform vec3 skins[4];
uniform vec3 boots = vec3(0.08, 0.08, 0.09);

varying vec3 col;

vec2 rot(vec2 v, float a) {
	float s = sin(a);
	float c = cos(a);
	return vec2(c * v.x - s * v.y, s * v.x + c * v.y);
}

void vertex() {
	int slot = int(COLOR.r * 255.0 + 0.5);
	int part = int(UV2.x + 0.5);
	float pivot = UV2.y;
	float ph = INSTANCE_CUSTOM.x;
	float amt = INSTANCE_CUSTOM.y;
	int kit = int(INSTANCE_CUSTOM.z + 0.5);

	float swing = 0.0;
	if (part == 1) swing = sin(ph) * 0.75;
	else if (part == 2) swing = -sin(ph) * 0.75;
	else if (part == 3) swing = -sin(ph) * 0.6 - 0.05;
	else if (part == 4) swing = sin(ph) * 0.6 - 0.05;
	swing *= amt;
	// Body's forward is +X: limbs swing in the X-Y plane about their joint.
	vec2 xy = rot(vec2(VERTEX.x, VERTEX.y - pivot), swing);
	VERTEX.xy = vec2(xy.x, xy.y + pivot);
	NORMAL.xy = rot(NORMAL.xy, swing);
	// Lean into the run and bob on each stride.
	float lean = -0.16 * amt;
	VERTEX.xy = rot(VERTEX.xy, lean);
	NORMAL.xy = rot(NORMAL.xy, lean);
	VERTEX.y += abs(cos(ph)) * 0.05 * amt;

	vec3 skin = skins[int(INSTANCE_CUSTOM.w + 0.5)];
	if (slot == 0) col = shirt[kit];
	else if (slot == 1) col = trim[kit];
	else if (slot == 2) col = shorts[kit];
	else if (slot == 3) col = socks[kit];
	else if (slot == 4) col = skin;
	else if (slot == 5) col = skin * vec3(0.22, 0.17, 0.14);
	else col = pow(boots, vec3(2.2));
}

void fragment() {
	ALBEDO = col;
	ROUGHNESS = 0.85;
}
