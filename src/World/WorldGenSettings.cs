using Godot;

namespace Curitibia.World;

/// <summary>Terrain tuning as data (data/world_gen.tres). The seed is not here: the host picks it per session.</summary>
[GlobalClass]
public partial class WorldGenSettings : Resource
{
  /// <summary>Noise frequency per voxel (Unity used 0.04).</summary>
  [Export] public float Frequency { get; set; } = 0.04f;

  /// <summary>Height range added on top of BaseHeight (Unity used 10).</summary>
  [Export] public float Amplitude { get; set; } = 10f;

  /// <summary>Minimum terrain height in voxels (Unity used half the chunk height, 16).</summary>
  [Export] public float BaseHeight { get; set; } = 16f;

  [Export(PropertyHint.Range, "1,4,1")] public int Octaves { get; set; } = 2;

  /// <summary>Chunks kept meshed around each viewer in every direction (7x7 chunks for 3).</summary>
  [Export(PropertyHint.Range, "1,12,1")] public int ViewRadius { get; set; } = 3;

  /// <summary>Chunks start unloading beyond ViewRadius + this, so walking along a border does not thrash.</summary>
  [Export(PropertyHint.Range, "1,4,1")] public int UnloadMargin { get; set; } = 2;

  /// <summary>Mesh results applied per frame (collision shapes are built on the main thread).</summary>
  [Export(PropertyHint.Range, "1,8,1")] public int MeshAppliesPerFrame { get; set; } = 2;

  public WorldGenParams ToParams(int seed)
  {
    return new WorldGenParams(seed, Frequency, Amplitude, BaseHeight, Octaves);
  }
}
