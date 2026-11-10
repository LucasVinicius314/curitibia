using System;
using Curitibia.Core;
using Curitibia.Player;
using Godot;

namespace Curitibia.World;

/// <summary>
/// Stand-in for a real light engine: how much of the sky the local player can see decides how much sun and sky light there is,
/// and a small warm lamp follows the player when there is little. The sky view is measured with a cone of rays up from the head
/// (see SkyExposure): a wide hole in the ground is bright, a narrow shaft or a cave roof is dark, and leaves only thin the
/// light a little, so standing under a tree does not turn the world to night. Direct sunlight is the real directional light and
/// its shadows; only the global ambient and sun strength are faded here. Deeper than MaxSkyDepth below the terrain it is always
/// dark (a ray that long never reaches the sky anyway), which also keeps the underworld's big open hall dark.
/// </summary>
public partial class CaveLight : Node
{
  const float CheckInterval = 0.2f;

  /// <summary>Rays reach this far (voxels, 60 m); deeper than MaxSkyDepth metres below the terrain it is simply dark.</summary>
  const int RayLength = 120;
  const float MaxSkyDepth = 40f;

  /// <summary>Sky exposure (0 to 1) below which it is dark, and above which it is fully bright.</summary>
  const float DarkBelow = 0.2f;
  const float BrightAbove = 0.55f;

  /// <summary>How much of a ray's light a leaf voxel, and a trunk or cactus voxel, takes away (stone and soil take all of it).</summary>
  const float LeafAbsorb = 0.05f;
  const float WoodAbsorb = 0.2f;

  static readonly Vector3[] rays = BuildRays();
  static readonly float[] rayWeights = BuildWeights();

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
      target = terrain.DepthBelowGround(player.EyePosition) > MaxSkyDepth
        ? 0f
        : Mathf.SmoothStep(0f, 1f, Mathf.Clamp((SkyExposure(terrain, player.EyePosition) - DarkBelow) / (BrightAbove - DarkBelow), 0f, 1f));
    }

    light = Mathf.MoveToward(light, target, (float)delta * 2.5f);

    Sun.LightEnergy = sunEnergy * light * DayNight.LightStrength;
    Sun.Visible = light * DayNight.LightStrength > 0.02f;
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

  // Straight up, then a ring of 6 steep rays and a ring of 6 shallower ones: a hole that is wide open lets most of them out.
  static Vector3[] BuildRays()
  {
    var list = new System.Collections.Generic.List<Vector3> { Vector3.Up };

    foreach (var elevation in new[] { 68f, 42f })
    {
      for (int i = 0; i < 6; i++)
      {
        var azimuth = Mathf.DegToRad(i * 60f + (elevation < 50f ? 30f : 0f));
        var e = Mathf.DegToRad(elevation);

        list.Add(new Vector3(Mathf.Cos(e) * Mathf.Cos(azimuth), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Sin(azimuth)));
      }
    }

    return list.ToArray();
  }

  static float[] BuildWeights()
  {
    var weights = new float[13];

    weights[0] = 3f; // straight up counts the most
    for (int i = 1; i < 7; i++) { weights[i] = 1.5f; }
    for (int i = 7; i < 13; i++) { weights[i] = 1f; }

    return weights;
  }

  /// <summary>
  /// The share of the sky the eye can see (0 to 1): the weighted average over a cone of rays of how much light gets through the
  /// blocks along each (air all of it, leaves most, trunks some, anything else none).
  /// </summary>
  static float SkyExposure(Terrain terrain, Vector3 eye)
  {
    var ox = eye.X / VoxelMath.BlockSize;
    var oy = eye.Y / VoxelMath.BlockSize;
    var oz = eye.Z / VoxelMath.BlockSize;
    var total = 0f;
    var weightSum = 0f;

    for (int r = 0; r < rays.Length; r++)
    {
      var direction = rays[r];
      var transmission = 1f;

      for (int step = 2; step < RayLength && transmission > 0f; step++)
      {
        var y = VoxelMath.FloorToInt(oy + direction.Y * step);

        if (y >= VoxelMath.ChunkHeight)
        {
          break; // out into the open sky
        }

        var block = terrain.GetBlock(VoxelMath.FloorToInt(ox + direction.X * step), y, VoxelMath.FloorToInt(oz + direction.Z * step));

        if (block == BlockIds.Air || BlockIds.IsPlant(block))
        {
          continue;
        }

        transmission -= block is BlockIds.Leaves or BlockIds.PineLeaves or BlockIds.BirchLeaves ? LeafAbsorb
          : block is BlockIds.Wood or BlockIds.BirchWood or BlockIds.Cactus ? WoodAbsorb
          : 1f;
      }

      total += Math.Max(0f, transmission) * rayWeights[r];
      weightSum += rayWeights[r];
    }

    return total / weightSum;
  }
}
