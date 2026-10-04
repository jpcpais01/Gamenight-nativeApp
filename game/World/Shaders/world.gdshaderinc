// Shared by every ground's shaders: the time of day (set by Atmosphere), the match's mood
// (set by the ground each frame) and the baked light map (stand shadows + floodlight pools).
global uniform vec3 gn_sun_dir;
global uniform vec3 gn_sun;
global uniform vec3 gn_amb_sky;
global uniform vec3 gn_amb_ground;
global uniform float gn_flood;
global uniform vec3 gn_flood_col;
global uniform vec3 gn_fog;
global uniform vec2 gn_fog_range;
global uniform float gn_haze;
global uniform float gn_night;
global uniform float gn_excite;
// The terraces: how hard the home / away end sings, the song's beat (0..1), Viking arms up.
global uniform vec4 gn_chant;
/** x: team that just scored (0 home, 1 away, -1 none), y: seconds since. */
global uniform vec2 gn_goal;
global uniform float gn_tifo;
global uniform vec3 gn_home;
global uniform vec3 gn_away;
global uniform sampler2D gn_atlas : source_color, filter_linear_mipmap, repeat_disable;

// Light from the time of day on a surface facing n, with the sun's share scaled by vis.
vec3 world_light(vec3 n, float vis) {
	vec3 amb = mix(gn_amb_ground, gn_amb_sky, n.y * 0.5 + 0.5);
	return amb + gn_sun * max(dot(n, -gn_sun_dir), 0.0) * vis;
}

// Atmospheric haze: the background sits back behind the play.
vec3 world_fog(vec3 c, float dist, float base) {
	float f = smoothstep(gn_fog_range.x, gn_fog_range.y, dist);
	return mix(c, gn_fog, (base + f * 0.7) * gn_haze);
}

float world_hash(vec2 p) { return fract(sin(dot(p, vec2(41.3, 289.1))) * 15731.743); }
