using Godot;

namespace Curitibia.Combat;

/// <summary>
/// A floating hit-point bar: one camera-facing quad with a shared shader, the fill ratio is a per-instance shader parameter
/// (no allocation per update). Hidden while the owner is at full health or dead.
/// </summary>
public partial class HealthBar3D : MeshInstance3D
{
  const float Width = 1.2f;
  const float Height = 0.22f;

  static readonly StringName ratioParam = new("ratio");

  static ShaderMaterial? material;
  static QuadMesh? mesh;

  float shown = -1f;

  static ShaderMaterial Material => material ??= new ShaderMaterial
  {
    Shader = new Shader
    {
      Code = @"
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled, depth_draw_opaque;

instance uniform float ratio = 1.0;

void vertex() {
  // face the camera, keep the node's position
  MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
}

void fragment() {
  vec2 edge = min(UV, 1.0 - UV);
  vec3 fill = mix(vec3(0.9, 0.12, 0.1), vec3(0.2, 0.85, 0.25), smoothstep(0.2, 0.55, ratio));

  if (edge.x < 0.025 || edge.y < 0.14) {
    ALBEDO = vec3(0.02);
  } else if (UV.x < ratio) {
    ALBEDO = fill;
  } else {
    ALBEDO = vec3(0.16, 0.05, 0.05);
  }
}",
    },
  };

  public override void _Ready()
  {
    Mesh = mesh ??= new QuadMesh { Size = new Vector2(Width, Height) };
    MaterialOverride = Material;
    CastShadow = ShadowCastingSetting.Off;
    Visible = false;
  }

  /// <summary>Shows the bar for hp out of max; hides it at full health or zero.</summary>
  public void Set(float hp, float max)
  {
    var ratio = max > 0f ? Mathf.Clamp(hp / max, 0f, 1f) : 0f;

    if (Mathf.IsEqualApprox(ratio, shown))
    {
      return;
    }

    shown = ratio;
    Visible = ratio > 0f && ratio < 1f;
    SetInstanceShaderParameter(ratioParam, ratio);
  }
}
