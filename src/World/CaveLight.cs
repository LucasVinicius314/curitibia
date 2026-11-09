using System;
using Curitibia.Core;
using Curitibia.Player;
using Godot;

namespace Curitibia.World;

/// <summary>
/// Stand-in for a real light engine: below the ground the sun and the sky light fade out and a small warm lamp follows the
/// player, so caves are dark but not black. It depends only on how deep the player is below the terrain at its own column
/// (DimStart to DimEnd, a graded fade), never on what blocks are above the head: trees, leaves and overhangs must not turn
/// the world to night (the sun's own shadows already shade the ground under a tree). Direct sunlight is handled by the real
/// directional light and its shadows; only the global ambient and sun strength are faded here.
/// </summary>
public partial class CaveLight : Node
{
  const float CheckInterval = 0.2f;

  /// <summary>Metres below the terrain where the dimming starts, and where it is complete.</summary>
  const float DimStart = 3f;
  const float DimEnd = 8f;

  /// <summary>How much sky light is left in a cave (0 = pitch black).</summary>
  const float CaveAmbient = 0.05f;

  /// <summary>Fog density underground (exponential: 0.09 is about 80% fogged at 18 m).</summary>
  const float CaveFog = 0.09f;

  /// <summary>Ambient light in the underworld (reddish and bright: it is lit by magma). It fades in between Y -450 and -500 (Underground.AuraBlend).</summary>
  const float HellAmbient = 0.5f;

  static readonly Color HellFog = new(0.42f, 0.12f, 0.06f);
  const float HellFogDensity = 0.016f;

  [Export] public DirectionalLight3D Sun { get; set; } = null!;
  [Export] public WorldEnvironment Environment { get; set; } = null!;

  PlayerController? player;
  OmniLight3D? lamp;
  float target = 1f;
  float light = 1f;
  float timer;
  float sunEnergy;

  public override void _Ready()
  {
    sunEnergy = Sun.LightEnergy;
  }

  public override void _Process(double delta)
  {
    var terrain = Terrain.Instance;

    if (player == null || !IsInstanceValid(player))
    {
      player = null;

      foreach (var p in PlayerController.All)
      {
        if (p.IsLocal)
        {
          player = p;
          break;
        }
      }

      if (player == null)
      {
        return;
      }

      lamp = new OmniLight3D
      {
        OmniRange = 9f,
        LightEnergy = 0f,
        LightColor = new Color(1f, 0.82f, 0.6f),
        OmniAttenuation = 1.8f,
        ShadowEnabled = false,
        Visible = false,
      };

      player.CameraPivot.AddChild(lamp);
    }

    if (terrain == null)
    {
      return;
    }

    timer -= (float)delta;

    if (timer <= 0f)
    {
      timer = CheckInterval;
      target = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp((terrain.DepthBelowGround(player.EyePosition) - DimStart) / (DimEnd - DimStart), 0f, 1f));
    }

    light = Mathf.MoveToward(light, target, (float)delta * 2.5f);

    Sun.LightEnergy = sunEnergy * light;
    Sun.Visible = light > 0.02f;
    var environment = Environment.Environment;

    // The underworld is the opposite of a cave: how deep into it the player is (0 in the caverns, 1 in the underworld, smooth
    // over a long band, see Underground.AuraBlend) turns the dark into a red glow, with plenty of ambient light, a red haze and a bigger lamp.
    var hell = Underground.AuraBlend(VoxelMath.WorldToVoxel(player.EyePosition.Y)) * (1f - light);

    // Always the sky as the ambient source, and the red is blended in with the sky contribution: a switch between the sky and a
    // plain colour the moment the aura became non-zero is what made it start suddenly.
    environment.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
    environment.AmbientLightColor = new Color(1f, 0.46f, 0.3f);
    environment.AmbientLightSkyContribution = 1f - hell;
    environment.AmbientLightEnergy = Mathf.Lerp(Mathf.Lerp(CaveAmbient, 1f, light), HellAmbient, hell);

    // Fog: black underground (beyond a few metres everything sinks into the dark, which also hides where the meshing window
    // ends), a glowing red haze in the underworld.
    environment.FogEnabled = light < 0.98f;
    environment.FogLightColor = Colors.Black.Lerp(HellFog, hell);
    environment.FogLightEnergy = hell;
    environment.FogSkyAffect = 1f;
    environment.FogDensity = Mathf.Lerp((1f - light) * CaveFog, HellFogDensity, hell);

    if (lamp != null)
    {
      var dark = 1f - light;

      lamp.LightEnergy = dark * Mathf.Lerp(0.9f, 1.7f, hell);
      lamp.OmniRange = Mathf.Lerp(9f, 20f, hell);
      lamp.LightColor = new Color(1f, 0.82f, 0.6f).Lerp(new Color(1f, 0.5f, 0.32f), hell);
      lamp.Visible = dark > 0.02f;
    }
  }
}
