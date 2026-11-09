using Curitibia.World.Features;

namespace Curitibia.World;

public enum BiomeId : byte
{
  Plains = 0,
  Forest = 1,
  Desert = 2,
  Snow = 3,
  Hills = 4,
  Mountains = 5,
}

/// <summary>
/// One biome: where it sits in climate space (temperature, moisture, ruggedness, each 0 to 1), the relief it gives the
/// terrain, the blocks on top, and what grows there. Engine-free, so the generator and the tests share it.
/// </summary>
/// <param name="BaseOffset">Voxels added to WorldGenParams.BaseHeight where this biome rules. The terrain blends it in with the biome weights over
/// kilometres, so a large offset is a steady climb rather than a step. (The mountains' baseline is not this: it keeps rising across the
/// whole range with ruggedness, see WorldGenerator.)</param>
/// <param name="Relief">Height range of the biome's hills, in voxels (scaled by WorldGenParams.ReliefScale).</param>
/// <param name="Ridged">0 = rolling hills, 1 = sharp ridges and peaks.</param>
/// <param name="FillerDepth">Voxels of Filler between the surface block and the stone.</param>
/// <param name="TreeDensity">Chance 0 to 1 that a tree cell holds a tree.</param>
/// <param name="Tree">The kind of tree that grows here.</param>
/// <param name="AltTree">A second kind mixed in, for this share (0 to 1) of the trees.</param>
/// <param name="SnowLine">Height above BaseHeight from which the surface is snow (int.MaxValue = never).</param>
public sealed record BiomeDef(
  BiomeId Id,
  string Name,
  float Temperature,
  float Moisture,
  float Ruggedness,
  float BaseOffset,
  float Relief,
  float Ridged,
  byte Surface,
  byte Filler,
  int FillerDepth,
  float TreeDensity,
  int SnowLine = int.MaxValue,
  TreeKind Tree = TreeKind.Oak,
  TreeKind AltTree = TreeKind.Oak,
  float AltShare = 0f)
{
  /// <summary>The top block of a column that stands <paramref name="aboveBase"/> voxels over the base height.</summary>
  public byte SurfaceAt(int aboveBase)
  {
    return aboveBase >= SnowLine ? BlockIds.Snow : Surface;
  }
}

/// <summary>The biome table. Add a biome by adding a line here (and an id in <see cref="BiomeId"/>).</summary>
public static class Biomes
{
  public static readonly BiomeDef[] All =
  {
    new(BiomeId.Plains, "Sunlit Plains", 0.5f, 0.3f, 0.1f, 0f, 3f, 0f, BlockIds.Grass, BlockIds.Dirt, 3, 0.01f, int.MaxValue, TreeKind.Oak, TreeKind.Birch, 0.45f),
    new(BiomeId.Forest, "Whispering Woods", 0.45f, 0.8f, 0.2f, 2f, 22f, 0.1f, BlockIds.Grass, BlockIds.Dirt, 3, 0.5f, int.MaxValue, TreeKind.Oak, TreeKind.Birch, 0.3f),
    new(BiomeId.Desert, "Sunscar Desert", 0.8f, 0.15f, 0.1f, -2f, 14f, 0f, BlockIds.Sand, BlockIds.Sand, 6, 0.05f, int.MaxValue, TreeKind.Cactus),
    new(BiomeId.Snow, "Frostlands", 0.05f, 0.5f, 0.15f, 0f, 16f, 0.05f, BlockIds.Snow, BlockIds.Dirt, 2, 0.3f, int.MaxValue, TreeKind.Pine),
    new(BiomeId.Hills, "Verdant Hills", 0.7f, 0.6f, 0.3f, 6f, 45f, 0.1f, BlockIds.Grass, BlockIds.Dirt, 3, 0.07f, int.MaxValue, TreeKind.Oak, TreeKind.Birch, 0.4f),
    new(BiomeId.Mountains, "Stonecrown Mountains", 0.5f, 0.5f, 0.85f, 5f, 140f, 0.9f, BlockIds.Stone, BlockIds.Stone, 0, 0.06f, 250, TreeKind.Pine),
  };

  public static BiomeDef Get(BiomeId id)
  {
    return All[(int)id];
  }
}
