using System;
using Godot;

namespace Curitibia.Core;

/// <summary>
/// Player options persisted to user://settings.cfg. (The display name is not here: it is shared by every game on the machine
/// through the p2p library, see Net/PlayerProfile.)
/// </summary>
public static class GameSettings
{
  const string Path = "user://settings.cfg";

  public const float MinFieldOfView = 60f;
  public const float MaxFieldOfView = 120f;

  /// <summary>Vertical field of view in degrees. Godot's Camera3D fov is vertical: 90 is about 119 degrees across at 16:9.</summary>
  public const float DefaultFieldOfView = 90f;

  public const float MinMouseSensitivity = 0.25f;
  public const float MaxMouseSensitivity = 3f;

  public const float MinMoveSpeed = 0.25f;
  public const float MaxMoveSpeed = 10f;

  /// <summary>
  /// Debug: multiplier on how fast the player walks, sprints and flies (1 = as tuned). Not saved: it is back to 1 every time
  /// the game starts.
  /// </summary>
  public static float MoveSpeedMultiplier { get; set; } = 1f;

  public const int MinRenderDistance = 4;
  public const int MaxRenderDistance = 16;
  public const int DefaultRenderDistance = 12;

  static int? renderDistance;
  static float? fieldOfView;
  static float? mouseSensitivity;

  /// <summary>Raised after a setting changed.</summary>
  public static event Action? Changed;

  public static float FieldOfView
  {
    get
    {
      if (fieldOfView == null)
      {
        var config = new ConfigFile();
        var loaded = config.Load(Path) == Error.Ok
          ? (float)(double)config.GetValue("view", "fov", (double)DefaultFieldOfView)
          : DefaultFieldOfView;

        fieldOfView = Mathf.Clamp(loaded, MinFieldOfView, MaxFieldOfView);
      }

      return fieldOfView.Value;
    }
    set
    {
      var clamped = Mathf.Clamp(value, MinFieldOfView, MaxFieldOfView);

      if (Mathf.IsEqualApprox(clamped, FieldOfView))
      {
        return;
      }

      fieldOfView = clamped;

      var config = new ConfigFile();
      config.Load(Path); // keep other values if the file exists
      config.SetValue("view", "fov", (double)clamped);
      config.Save(Path);

      Changed?.Invoke();
    }
  }

  /// <summary>Multiplier on the tuned mouse and stick look speed (1 = as tuned).</summary>
  public static float MouseSensitivity
  {
    get
    {
      if (mouseSensitivity == null)
      {
        var config = new ConfigFile();
        var loaded = config.Load(Path) == Error.Ok ? (float)(double)config.GetValue("input", "sensitivity", 1.0) : 1f;

        mouseSensitivity = Mathf.Clamp(loaded, MinMouseSensitivity, MaxMouseSensitivity);
      }

      return mouseSensitivity.Value;
    }
    set
    {
      var clamped = Mathf.Clamp(value, MinMouseSensitivity, MaxMouseSensitivity);

      if (Mathf.IsEqualApprox(clamped, MouseSensitivity))
      {
        return;
      }

      mouseSensitivity = clamped;

      var config = new ConfigFile();
      config.Load(Path);
      config.SetValue("input", "sensitivity", (double)clamped);
      config.Save(Path);

      Changed?.Invoke();
    }
  }

  /// <summary>Chunks (16 m each) kept meshed around the player in every direction. The cost grows with its square.</summary>
  public static int RenderDistance
  {
    get
    {
      if (renderDistance == null)
      {
        var config = new ConfigFile();
        var loaded = config.Load(Path) == Error.Ok ? (int)config.GetValue("view", "render_distance", DefaultRenderDistance) : DefaultRenderDistance;

        renderDistance = Math.Clamp(loaded, MinRenderDistance, MaxRenderDistance);
      }

      return renderDistance.Value;
    }
    set
    {
      var clamped = Math.Clamp(value, MinRenderDistance, MaxRenderDistance);

      if (clamped == RenderDistance)
      {
        return;
      }

      renderDistance = clamped;

      var config = new ConfigFile();
      config.Load(Path);
      config.SetValue("view", "render_distance", clamped);
      config.Save(Path);

      Changed?.Invoke();
    }
  }
}
