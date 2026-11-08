shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back;

// Port of the Unity TransparentShader graph: a colour whose alpha is a Fresnel term, so a body is see-through in the
// middle and glows at its rim. Unity used it on the hidden pose-target copy of the player; here it is how a dead player
// is shown to everyone.

uniform vec4 ghost_color : source_color = vec4(0.55, 0.85, 1.0, 1.0);
uniform float fresnel_power = 2.0;
uniform float max_alpha = 0.85;

void fragment() {
	float fresnel = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), fresnel_power);
	ALBEDO = ghost_color.rgb;
	ALPHA = clamp(fresnel * ghost_color.a, 0.05, max_alpha);
}
