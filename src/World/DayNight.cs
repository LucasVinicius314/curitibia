using System;
using Curitibia.Core;
using Godot;

namespace Curitibia.World;

/// <summary>
/// The day and night cycle: turns the sun across the sky, fades its strength and colour, and moves the procedural sky between
/// its day, dusk and night colours (the ambient light follows the sky). The clock is the system clock, so every peer in a
/// session shows the same time of day without sending anything. With GameSettings.DayNightCycle off it is always noon.
/// CaveLight multiplies its own underground fade by <see cref="LightStrength"/>; the enemy spawner reads <see cref="IsNight"/>.
/// </summary>
public partial class DayNight : Node
{
  /// <summary>Real seconds in a whole day.</summary>
  public const float DayLength = 600f;

  /// <summary>Time of day when the cycle is off: noon.</summary>
  const float Noon = 0.5f;

  static readonly Color DayTop = new(0.32f, 0.5f, 0.85f);
  static readonly Color DayHorizon = new(0.72f, 0.82f, 0.92f);
  static readonly Color DuskTop = new(0.28f, 0.28f, 0.5f);
  static readonly Color DuskHorizon = new(0.95f, 0.52f, 0.3f);
  static readonly Color NightTop = new(0.03f, 0.045f, 0.13f);
  static readonly Color NightHorizon = new(0.06f, 0.08f, 0.18f);
  static readonly Color SunDay = new(1f, 1f, 0.96f);
  static readonly Color SunDusk = new(1f, 0.55f, 0.28f);
  static readonly Color Moon = new(0.55f, 0.65f, 1f);

  /// <summary>Moonlight: the fraction of the sun's tuned strength left at night, so the world stays readable.</summary>
  const float MoonStrength = 0.09f;

  /// <summary>0 at night, 1 in full daylight: the share of the sun's tuned strength that is left.</summary>
  public static float SunStrength { get; private set; } = 1f;

  /// <summary>Time of day, 0 to 1: 0 midnight, 0.25 sunrise, 0.5 noon, 0.75 sunset.</summary>
  public static float TimeOfDay { get; private set; } = Noon;

  /// <summary>Strength of the directional light (sun by day, moon at night); CaveLight scales the real light by it.</summary>
  public static float LightStrength { get; private set; } = 1f;

  public static bool IsNight => SunStrength < 0.25f;

  [Export] public DirectionalLight3D Sun { get; set; } = null!;
  [Export] public WorldEnvironment Environment { get; set; } = null!;

  ProceduralSkyMaterial? sky;
  float? fixedTime;

  public override void _Ready()
  {
    sky = Environment.Environment.Sky?.SkyMaterial as ProceduralSkyMaterial;
    fixedTime = float.TryParse(DevArgs.Get("time"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null;
    ApplyShadowDistance();
    GameSettings.Changed += ApplyShadowDistance;
  }

  public override void _ExitTree()
  {
    GameSettings.Changed -= ApplyShadowDistance;
  }

  void ApplyShadowDistance()
  {
    Sun.DirectionalShadowMaxDistance = GameSettings.ShadowDistance;
  }

  static float Wrap(float t) => t - MathF.Floor(t);

  /// <summary>Sets the time of day from the settings (0 to 1): moves the clock when the cycle runs, or the fixed time when it does not.</summary>
  public static void SetTime(float t)
  {
    if (GameSettings.DayNightCycle)
    {
      GameSettings.TimeOffset = Wrap(t - (float)(Time.GetUnixTimeFromSystem() % DayLength / DayLength));
    }
    else
    {
      GameSettings.FixedTime = Wrap(t);
    }
  }

  public override void _Process(double delta)
  {
    var clock = (float)(Time.GetUnixTimeFromSystem() % DayLength / DayLength);

    TimeOfDay = fixedTime ?? (GameSettings.DayNightCycle ? Wrap(clock + GameSettings.TimeOffset) : GameSettings.FixedTime);

    // Sun height: 1 at noon, -1 at midnight, 0 at sunrise and sunset.
    var height = MathF.Sin((TimeOfDay - 0.25f) * MathF.Tau);
    var dusk = 1f - Mathf.SmoothStep(0f, 0.35f, MathF.Abs(height)); // 1 right at the horizon
    var day = Mathf.SmoothStep(-0.08f, 0.3f, height);

    SunStrength = day;
    LightStrength = Math.Max(day, MoonStrength);

    // The sun travels east to west over the south side, tilted by its height (the scene's own heading is kept).
    // By night the same light is the moon: it keeps shining from above (never from under the horizon), blue, and without a sun disc.
    var lift = height >= 0f ? height : Math.Max(-height, 0.35f);

    Sun.RotationDegrees = new Vector3(-lift * 75f, 30f + (TimeOfDay - 0.5f) * 30f, 0f);
    Sun.LightColor = Moon.Lerp(SunDay.Lerp(SunDusk, dusk), day);
    Sun.SkyMode = height >= 0f ? DirectionalLight3D.SkyModeEnum.LightAndSky : DirectionalLight3D.SkyModeEnum.LightOnly;

    if (sky == null)
    {
      return;
    }

    var top = NightTop.Lerp(DayTop, day).Lerp(DuskTop, dusk * 0.6f * day);
    var horizon = NightHorizon.Lerp(DayHorizon, day).Lerp(DuskHorizon, dusk * (0.4f + 0.6f * day));

    sky.SkyTopColor = top;
    sky.SkyHorizonColor = horizon;
    sky.GroundHorizonColor = horizon;
    sky.GroundBottomColor = top.Darkened(0.5f);
  }
}
