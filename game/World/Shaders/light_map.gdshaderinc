// The baked light map for anything lit live on the pitch (grass, players, ball): r = how much
// of the sun reaches the ground here past the stands, g = the floodlight pools (x2).
// Include it, then:
//   fragment(): EMISSION += ALBEDO * light_map_flood(world_pos);
//   light():    DIFFUSE_LIGHT += light_map_sun(LIGHT, NORMAL, LIGHT_COLOR, ATTENUATION, world_pos);
global uniform sampler2D gn_light_map : filter_linear, repeat_disable;
global uniform vec4 gn_light_rect;
global uniform float gn_flood;
global uniform vec3 gn_flood_col;

vec2 light_map_at(vec3 w) {
	return texture(gn_light_map, (w.xz - gn_light_rect.xy) * gn_light_rect.zw).rg * vec2(1.0, 2.0);
}

vec3 light_map_flood(vec3 w) {
	return gn_flood_col * (gn_flood * light_map_at(w).g * 0.85);
}

vec3 light_map_sun(vec3 l, vec3 n, vec3 col, float att, vec3 w) {
	return col * (max(dot(n, l), 0.0) * att * light_map_at(w).r / PI);
}
