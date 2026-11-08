using System;
using Curitibia.Core;
using Curitibia.World.Features;

namespace Curitibia.World;

/// <summary>Tuning for terrain generation (filled from the WorldGenSettings resource). Engine-free.</summary>
public readonly record struct WorldGenParams(
  int Seed,
  float Frequency,
  float Amplitude,
  float BaseHeight,
  int Octaves);

/// <summary>
/// Terrain as a pure function: (params, chunk coordinate) gives the same bytes on every peer, on any thread. Grass on the
/// top voxel of each column, stone below (what the Unity terrain branch did).
/// </summary>
public sealed class WorldGenerator
{
  readonly WorldGenParams p;
  readonly GradientNoise noise;
  readonly TreeFeature trees;

  public WorldGenerator(WorldGenParams p)
  {
    this.p = p;
    noise = new GradientNoise(p.Seed);
    trees = new TreeFeature(p.Seed, ColumnHeight);
  }

  /// <summary>Number of solid voxels in the column at world (wx, wz); the top voxel is at height - 1.</summary>
  public int ColumnHeight(int wx, int wz)
  {
    var amp = 1f;
    var freq = p.Frequency;
    var sum = 0f;
    var norm = 0f;

    for (int o = 0; o < Math.Max(1, p.Octaves); o++)
    {
      sum += noise.Sample(wx * freq + 0.5f, wz * freq + 0.5f) * amp;
      norm += amp;
      amp *= 0.35f;
      freq *= 2f;
    }

    // Gradient noise in 2D stays within about +-0.7, so remap that to [0, 1].
    var n01 = Math.Clamp(0.5f + 0.5f * (sum / norm) / 0.7f, 0f, 1f);
    var h = (int)(p.BaseHeight + n01 * p.Amplitude);

    return Math.Clamp(h, 1, VoxelMath.ChunkHeight);
  }

  /// <summary>Height (exclusive) of the highest thing standing in a column, trees included.</summary>
  public int SurfaceTop(int wx, int wz)
  {
    return Math.Min(VoxelMath.ChunkHeight, Math.Max(ColumnHeight(wx, wz), trees.TopAt(wx, wz)));
  }

  /// <summary>
  /// One chunk's voxels. The array is only as tall as the terrain and trees need (rounded up in layers); anything above it
  /// is air, see VoxelMath.Get.
  /// </summary>
  public byte[] GenerateChunk(int cx, int cz)
  {
    const int size = VoxelMath.ChunkSize;
    var x0 = cx * size;
    var z0 = cz * size;

    var heights = new int[size * size];
    var tallest = 0;

    for (int z = 0; z < size; z++)
    {
      for (int x = 0; x < size; x++)
      {
        var h = ColumnHeight(x0 + x, z0 + z);
        heights[x + z * size] = h;
        tallest = Math.Max(tallest, h);
      }
    }

    var layers = Math.Min(VoxelMath.ChunkHeight, VoxelMath.RoundUpLayers(tallest + TreeFeature.MaxHeight));
    var blocks = new byte[layers * VoxelMath.LayerSize];

    for (int z = 0; z < size; z++)
    {
      for (int x = 0; x < size; x++)
      {
        var h = heights[x + z * size];

        for (int y = 0; y < h - 1; y++)
        {
          blocks[VoxelMath.Index(x, y, z)] = BlockIds.Stone;
        }

        blocks[VoxelMath.Index(x, h - 1, z)] = BlockIds.Grass;
      }
    }

    // Trees rooted in this chunk or close enough to reach into it, written only where they fall inside. Wood always wins
    // and leaves only fill air, so the result does not depend on the order neighbouring chunks visit overlapping trees.
    trees.ForTreesNear(x0, z0, x0 + size - 1, z0 + size - 1, tree => PlaceTree(blocks, tree, x0, z0));

    return blocks;
  }

  static void PlaceTree(byte[] blocks, Tree tree, int x0, int z0)
  {
    const int size = VoxelMath.ChunkSize;
    var top = Math.Min(blocks.Length / VoxelMath.LayerSize, tree.BaseY + tree.TrunkHeight + tree.CanopyRadius + 1);
    var minX = Math.Max(x0, tree.X - TreeFeature.MaxReach);
    var maxX = Math.Min(x0 + size - 1, tree.X + TreeFeature.MaxReach);
    var minZ = Math.Max(z0, tree.Z - TreeFeature.MaxReach);
    var maxZ = Math.Min(z0 + size - 1, tree.Z + TreeFeature.MaxReach);

    for (int wz = minZ; wz <= maxZ; wz++)
    {
      for (int wx = minX; wx <= maxX; wx++)
      {
        for (int y = Math.Max(0, tree.BaseY); y < top; y++)
        {
          var index = VoxelMath.Index(wx - x0, y, wz - z0);

          if (tree.IsTrunk(wx, y, wz))
          {
            blocks[index] = BlockIds.Wood;
          }
          else if (blocks[index] == BlockIds.Air && tree.IsLeaf(wx, y, wz))
          {
            blocks[index] = BlockIds.Leaves;
          }
        }
      }
    }
  }
}
