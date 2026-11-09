using Curitibia.Core;
using Godot;

namespace Curitibia.World;

/// <summary>Terrain tuning as data (data/world_gen.tres). The seed is not here: the host picks it per session.</summary>
[GlobalClass]
public partial class WorldGenSettings : Resource
{
  /// <summary>Noise frequency per voxel (Unity used 0.04 on 1 m blocks; half-metre blocks need half of it for the same landscape).</summary>
  [Export] public float Frequency { get; set; } = 0.02f;

  /// <summary>Scales every biome's relief (1 = as defined in the biome table; 0.5 = flatter world, 1.5 = taller mountains).</summary>
  [Export(PropertyHint.Range, "0.25,2,0.05")] public float ReliefScale { get; set; } = 1f;

  /// <summary>Biome noise frequency per voxel: lower = larger biomes (0.00022 gives regions of 1 to 3 km wide regions).</summary>
  [Export(PropertyHint.Range, "0.0001,0.01,0.00005")] public float BiomeFrequency { get; set; } = 0.00022f;

  /// <summary>The layer that is Y 0, the surface baseline that biome offsets are added to (the underground is below it).</summary>
  [Export] public float BaseHeight { get; set; } = VoxelMath.SeaLevel;

  [Export(PropertyHint.Range, "1,4,1")] public int Octaves { get; set; } = 3;

  /// <summary>Unused: the render distance is a player setting now (GameSettings.RenderDistance, default 12 = 25x25 chunks).</summary>
  [Export(PropertyHint.Range, "1,12,1")] public int ViewRadius { get; set; } = 12;

  /// <summary>Chunks start unloading beyond ViewRadius + this, so walking along a border does not thrash.</summary>
  [Export(PropertyHint.Range, "1,4,1")] public int UnloadMargin { get; set; } = 2;

  /// <summary>Mesh results applied per frame (collision shapes are built on the main thread).</summary>
  [Export(PropertyHint.Range, "1,8,1")] public int MeshAppliesPerFrame { get; set; } = 3;

  public WorldGenParams ToParams(int seed)
  {
    return new WorldGenParams(seed, Frequency, BaseHeight, Octaves, BiomeFrequency, ReliefScale);
  }
}
