using System;
using Godot;

namespace Curitibia.Core;

/// <summary>
/// Developer command-line flags (everything after `--` on the Godot command line). Used for scripted runs: screenshots,
/// fixed seeds, two-process multiplayer checks. Nothing here changes normal play.
/// </summary>
public static class DevArgs
{
  static readonly string[] args = OS.GetCmdlineUserArgs();

  public static bool Has(string name)
  {
    foreach (var arg in args)
    {
      if (arg == $"--{name}" || arg.StartsWith($"--{name}=", StringComparison.Ordinal))
      {
        return true;
      }
    }

    return false;
  }

  public static string? Get(string name)
  {
    foreach (var arg in args)
    {
      if (arg.StartsWith($"--{name}=", StringComparison.Ordinal))
      {
        return arg[(name.Length + 3)..];
      }
    }

    return null;
  }

  public static int? GetInt(string name)
  {
    return int.TryParse(Get(name), out var v) ? v : null;
  }

  /// <summary>--cam=x,y,z,yawDeg,pitchDeg : a fixed spectator camera instead of a player.</summary>
  public static (Vector3 position, float yaw, float pitch)? Cam
  {
    get
    {
      var raw = Get("cam");

      if (raw == null)
      {
        return null;
      }

      var parts = raw.Split(',');

      if (parts.Length != 5)
      {
        return null;
      }

      var f = new float[5];

      for (int i = 0; i < 5; i++)
      {
        if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f[i]))
        {
          return null;
        }
      }

      return (new Vector3(f[0], f[1], f[2]), Mathf.DegToRad(f[3]), Mathf.DegToRad(f[4]));
    }
  }
}
