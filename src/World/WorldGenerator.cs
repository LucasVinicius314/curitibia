using System;
using Curitibia.Core;

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

  public WorldGenerator(WorldGenParams p)
  {
    this.p = p;
    noise = new GradientNoise(p.Seed);
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

  public byte[] GenerateChunk(int cx, int cz)
  {
    var blocks = new byte[VoxelMath.ChunkVolume];

    for (int z = 0; z < VoxelMath.ChunkSize; z++)
    {
      for (int x = 0; x < VoxelMath.ChunkSize; x++)
      {
        var h = ColumnHeight(cx * VoxelMath.ChunkSize + x, cz * VoxelMath.ChunkSize + z);

        for (int y = 0; y < h - 1; y++)
        {
          blocks[VoxelMath.Index(x, y, z)] = BlockIds.Stone;
        }

        blocks[VoxelMath.Index(x, h - 1, z)] = BlockIds.Grass;
      }
    }

    return blocks;
  }
}
