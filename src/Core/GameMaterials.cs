using Godot;

namespace Curitibia.Core;

/// <summary>
/// The shared materials from the Unity project (MatteBlue/Yellow/Black, Metal, Reflective, the enemy colour) plus the two
/// ported shaders. Built once on first use and shared by every character, never per instance or per frame.
/// </summary>
public static class GameMaterials
{
  static StandardMaterial3D? matteBlue;
  static StandardMaterial3D? matteYellow;
  static StandardMaterial3D? matteBlack;
  static StandardMaterial3D? metal;
  static StandardMaterial3D? reflective;
  static StandardMaterial3D? enemyBody;
  static ShaderMaterial? ghost;
  static ShaderMaterial? trail;

  public static StandardMaterial3D MatteBlue => matteBlue ??= Matte(new Color(0f, 0.4276f, 1f));
  public static StandardMaterial3D MatteYellow => matteYellow ??= Matte(new Color(1f, 0.8884f, 0f));
  public static StandardMaterial3D MatteBlack => matteBlack ??= Matte(new Color(0.3113f, 0.3113f, 0.3113f));
  static StandardMaterial3D? copper;
  static StandardMaterial3D? woodHandle;

  /// <summary>Copper for the tools: warm orange metal.</summary>
  public static StandardMaterial3D Copper => copper ??= new StandardMaterial3D
  {
    AlbedoColor = new Color(0.78f, 0.46f, 0.24f),
    Metallic = 0.6f,
    Roughness = 0.45f,
  };

  public static StandardMaterial3D WoodHandle => woodHandle ??= Matte(new Color(0.45f, 0.30f, 0.16f));

  public static StandardMaterial3D EnemyBody => enemyBody ??= Matte(new Color(1f, 0.2647f, 0.1651f));

  public static StandardMaterial3D Metal => metal ??= new StandardMaterial3D
  {
    AlbedoColor = new Color(0.8538f, 0.8988f, 1f),
    Metallic = 1f,
    Roughness = 0.5f,
  };

  public static StandardMaterial3D Reflective => reflective ??= new StandardMaterial3D
  {
    AlbedoColor = new Color(0.6368f, 0.8334f, 1f),
    Metallic = 1f,
    Roughness = 0f,
  };

  /// <summary>The Fresnel rim shader (Unity TransparentShader): a dead player is drawn with it.</summary>
  public static ShaderMaterial Ghost => ghost ??= new ShaderMaterial
  {
    Shader = GD.Load<Shader>("res://assets/shaders/fresnel_ghost.gdshader"),
  };

  /// <summary>The scrolling smoke trail (Unity TrailShader + TrailMaterial).</summary>
  public static ShaderMaterial Trail
  {
    get
    {
      if (trail == null)
      {
        trail = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/trail.gdshader") };
        trail.SetShaderParameter("main_texture", GD.Load<Texture2D>("res://assets/textures/smokey_trail.jpg"));
      }

      return trail;
    }
  }

  /// <summary>A matte material of any colour (used to tell players apart).</summary>
  public static StandardMaterial3D Matte(Color color)
  {
    return new StandardMaterial3D { AlbedoColor = color, Roughness = 0.5f };
  }
}
