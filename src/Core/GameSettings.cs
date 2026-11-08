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

  static float? fieldOfView;

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
}
