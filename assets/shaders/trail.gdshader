shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;

// Port of the Unity TrailShader graph: a smoke texture scrolling along the trail, a colour gradient from the head to the
// tail, a noise dissolve and a fade toward the tail. UV.x runs 0 (head) to 1 (tail) along the ribbon, UV.y across it.

uniform sampler2D main_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform vec2 main_texture_speed = vec2(-0.5, 0.0);
uniform vec2 main_texture_tiling = vec2(1.0, 1.0);
uniform vec4 color_1 : source_color = vec4(1.0, 0.0, 0.0, 1.0);
uniform vec4 color_2 : source_color = vec4(0.0, 0.035, 1.0, 1.0);
uniform float dissolve_scale = 35.0;
uniform float dissolve_speed = 0.5;
uniform float fade_distance = 0.8;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
		mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x),
		f.y);
}

void fragment() {
	float along = UV.x;
	float across = 1.0 - abs(UV.y * 2.0 - 1.0);

	vec4 tex = texture(main_texture, UV * main_texture_tiling + TIME * main_texture_speed);
	float fade = clamp((1.0 - along) / max(fade_distance, 0.001), 0.0, 1.0);
	float dissolve = smoothstep(along * 0.9, along * 0.9 + 0.25, value_noise(vec2(along * 4.0, UV.y * 2.0) * (dissolve_scale / 10.0) + TIME * dissolve_speed));

	ALBEDO = mix(color_1.rgb, color_2.rgb, along);
	ALPHA = clamp(tex.r * 2.2, 0.0, 1.0) * fade * smoothstep(0.0, 0.5, across) * dissolve;
}
