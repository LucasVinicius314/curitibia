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
  Grove = 6,
}

/// <summary>A colour multiplier (each channel 0 to 1.5 or so; 1 leaves a texture as it is).</summary>
public readonly record struct Rgb(float R, float G, float B)
{
  public static readonly Rgb White = new(1f, 1f, 1f);
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
/// <param name="GiantDensity">Chance 0 to 1 that a giant-tree cell (about 28 m across) holds one of the huge trees (0 = none).</param>
/// <param name="LogDensity">Chance 0 to 1 that a fallen-log cell holds a dead log lying on the ground (0 = none).</param>
/// <param name="TrenchDepth">Depth in voxels of the narrow trenches cut into the ground (0 = none).</param>
/// <param name="TallGrass">Open grass is tall grass (a share of the grass tops, much denser than the usual short tufts).</param>
/// <param name="GrassTint">Multiplies the colour of grass (the block and the plants) here; blends smoothly across borders (null = unchanged).</param>
/// <param name="FoliageTint">Multiplies the colour of leaves here (null = unchanged).</param>
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
  float AltShare = 0f,
  float GiantDensity = 0f,
  float LogDensity = 0f,
  float TrenchDepth = 0f,
  bool TallGrass = false,
  Rgb? GrassTint = null,
  Rgb? FoliageTint = null)
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
    new(BiomeId.Plains, "Sunlit Plains", 0.5f, 0.3f, 0.1f, 0f, 3f, 0f, BlockIds.Grass, BlockIds.Dirt, 3, 0.01f, int.MaxValue, TreeKind.Oak, TreeKind.Birch, 0.45f, 0f, 0f, 0f, false, new Rgb(1.02f, 1.0f, 0.9f), new Rgb(1.0f, 1.0f, 0.92f)),
    new(BiomeId.Forest, "Whispering Woods", 0.45f, 0.8f, 0.2f, 2f, 22f, 0.1f, BlockIds.Grass, BlockIds.Dirt, 3, 0.5f, int.MaxValue, TreeKind.Oak, TreeKind.Birch, 0.3f, 0f, 0f, 0f, false, new Rgb(0.88f, 0.98f, 0.86f), new Rgb(0.88f, 1.0f, 0.88f)),
    new(BiomeId.Desert, "Sunscar Desert", 0.8f, 0.15f, 0.1f, -2f, 14f, 0f, BlockIds.Sand, BlockIds.Sand, 6, 0.05f, int.MaxValue, TreeKind.Cactus, TreeKind.Oak, 0f, 0f, 0f, 0f, false, new Rgb(1.1f, 1.0f, 0.62f), new Rgb(1.1f, 1.0f, 0.7f)),
    new(BiomeId.Snow, "Frostlands", 0.05f, 0.5f, 0.15f, 0f, 16f, 0.05f, BlockIds.Snow, BlockIds.Dirt, 2, 0.3f, int.MaxValue, TreeKind.Pine, TreeKind.Oak, 0f, 0f, 0f, 0f, false, new Rgb(0.8f, 0.96f, 0.98f), new Rgb(0.84f, 0.98f, 1.0f)),
    new(BiomeId.Hills, "Verdant Hills", 0.7f, 0.6f, 0.3f, 6f, 45f, 0.1f, BlockIds.Grass, BlockIds.Dirt, 3, 0.07f, int.MaxValue, TreeKind.Oak, TreeKind.Birch, 0.4f, 0f, 0f, 0f, false, new Rgb(0.94f, 1.02f, 0.88f), new Rgb(0.96f, 1.02f, 0.9f)),
    new(BiomeId.Mountains, "Stonecrown Mountains", 0.5f, 0.5f, 0.85f, 5f, 140f, 0.9f, BlockIds.Stone, BlockIds.Stone, 0, 0.06f, 250, TreeKind.Pine, TreeKind.Oak, 0f, 0f, 0f, 0f, false, new Rgb(0.82f, 0.9f, 0.82f), new Rgb(0.8f, 0.92f, 0.86f)),
    new(BiomeId.Grove, "Titan Grove", 0.62f, 0.96f, 0.12f, 3f, 9f, 0.05f, BlockIds.Grass, BlockIds.Dirt, 3, 0.2f, int.MaxValue, TreeKind.Oak, TreeKind.Birch, 0.25f, 0.9f, 0.55f, 30f, true, new Rgb(0.5f, 0.76f, 0.58f), new Rgb(0.58f, 0.86f, 0.74f)),
  };

  public static BiomeDef Get(BiomeId id)
  {
    return All[(int)id];
  }
}
