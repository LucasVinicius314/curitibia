using System;

namespace Curitibia.Core;

/// <summary>Engine-free voxel coordinate math (no Godot types, so it is unit-testable).</summary>
public static class VoxelMath
{
  public const int ChunkSize = 32;
  /// <summary>
  /// Voxels per column: 1152 half-metre blocks is 576 m. Layer 0 is the bottom of the underworld (Y -700), layer
  /// <see cref="SeaLevel"/> is Y 0, and the rest is room for the mountains above it.
  /// </summary>
  public const int ChunkHeight = 1152;

  /// <summary>The layer that is Y 0: the surface baseline (where water will sit). The underground is below it.</summary>
  public const int SeaLevel = 700;

  /// <summary>The Y a layer is called in the game (Y 0 at the baseline of the surface, negative underground).</summary>
  public static int DisplayY(int layer)
  {
    return layer - SeaLevel;
  }

  /// <summary>Edge length of one voxel in world units (metres).</summary>
  public const float BlockSize = 0.5f;
  public const int ChunkVolume = ChunkSize * ChunkSize * ChunkHeight;

  /// <summary>Voxels in one horizontal layer of a chunk.</summary>
  public const int LayerSize = ChunkSize * ChunkSize;

  /// <summary>Floor division that rounds toward negative infinity (the classic voxel bug is using / on negatives).</summary>
  public static int FloorDiv(int a, int b)
  {
    var q = a / b;
    return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
  }

  public static int FloorMod(int a, int b)
  {
    var m = a % b;
    return (m != 0 && (m < 0) != (b < 0)) ? m + b : m;
  }

  public static int FloorToInt(float v)
  {
    return (int)MathF.Floor(v);
  }

  /// <summary>The voxel coordinate containing a world-space coordinate.</summary>
  public static int WorldToVoxel(float world)
  {
    return FloorToInt(world / BlockSize);
  }

  /// <summary>World coordinate of a voxel's minimum corner.</summary>
  public static float VoxelToWorld(int voxel)
  {
    return voxel * BlockSize;
  }

  /// <summary>World coordinate of a voxel's centre.</summary>
  public static float VoxelCenter(int voxel)
  {
    return (voxel + 0.5f) * BlockSize;
  }

  public static int Index(int x, int y, int z)
  {
    return x + z * ChunkSize + y * ChunkSize * ChunkSize;
  }

  public static bool InWorldHeight(int y)
  {
    return y >= 0 && y < ChunkHeight;
  }

  public static void WorldToChunk(int wx, int wz, out int cx, out int cz, out int lx, out int lz)
  {
    cx = FloorDiv(wx, ChunkSize);
    cz = FloorDiv(wz, ChunkSize);
    lx = wx - cx * ChunkSize;
    lz = wz - cz * ChunkSize;
  }
}
