using Godot;

namespace Curitibia.World;

/// <summary>Terrain tuning as data (data/world_gen.tres). The seed is not here: the host picks it per session.</summary>
[GlobalClass]
public partial class WorldGenSettings : Resource
{
  /// <summary>Noise frequency per voxel (Unity used 0.04 on 1 m blocks; half-metre blocks need half of it for the same landscape).</summary>
  [Export] public float Frequency { get; set; } = 0.02f;

  /// <summary>Height range added on top of BaseHeight (10 m of hills = 20 half-metre voxels).</summary>
  [Export] public float Amplitude { get; set; } = 20f;

  /// <summary>Minimum terrain height in voxels (25 m, leaving the rest of the column to build up into).</summary>
  [Export] public float BaseHeight { get; set; } = 50f;

  [Export(PropertyHint.Range, "1,4,1")] public int Octaves { get; set; } = 2;

  /// <summary>Chunks kept meshed around each viewer in every direction (25x25 chunks for 12; a chunk is 16 m wide).</summary>
  [Export(PropertyHint.Range, "1,12,1")] public int ViewRadius { get; set; } = 12;

  /// <summary>Chunks start unloading beyond ViewRadius + this, so walking along a border does not thrash.</summary>
  [Export(PropertyHint.Range, "1,4,1")] public int UnloadMargin { get; set; } = 2;

  /// <summary>Mesh results applied per frame (collision shapes are built on the main thread).</summary>
  [Export(PropertyHint.Range, "1,8,1")] public int MeshAppliesPerFrame { get; set; } = 3;

  public WorldGenParams ToParams(int seed)
  {
    return new WorldGenParams(seed, Frequency, Amplitude, BaseHeight, Octaves);
  }
}
