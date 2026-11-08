using System;

namespace Curitibia.Core;

/// <summary>Engine-free voxel coordinate math (no Godot types, so it is unit-testable).</summary>
public static class VoxelMath
{
  public const int ChunkSize = 32;
  /// <summary>Voxels per column: 256 half-metre blocks is 128 m of build height.</summary>
  public const int ChunkHeight = 256;

  /// <summary>Edge length of one voxel in world units (metres).</summary>
  public const float BlockSize = 0.5f;
  public const int ChunkVolume = ChunkSize * ChunkSize * ChunkHeight;

  /// <summary>Voxels in one horizontal layer of a chunk.</summary>
  public const int LayerSize = ChunkSize * ChunkSize;

  /// <summary>Chunk arrays only hold the layers that can contain something, in steps of this many layers.</summary>
  public const int LayerStep = 16;

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

  /// <summary>
  /// A voxel of a chunk array, air when it lies above the array. Chunk arrays are trimmed to the layers in use (a tall world
  /// is mostly sky), so reading past the end is normal.
  /// </summary>
  public static byte Get(byte[] blocks, int x, int y, int z)
  {
    var index = Index(x, y, z);

    return index < blocks.Length ? blocks[index] : (byte)0;
  }

  /// <summary>An array tall enough to hold layer <paramref name="y"/>: the same one when it already is, else a grown copy.</summary>
  public static byte[] EnsureHeight(byte[] blocks, int y)
  {
    var needed = Math.Min(ChunkVolume, RoundUpLayers(y + 1) * LayerSize);

    if (blocks.Length >= needed)
    {
      return blocks;
    }

    var grown = new byte[needed];
    Buffer.BlockCopy(blocks, 0, grown, 0, blocks.Length);

    return grown;
  }

  /// <summary>Rounds a layer count up to the array step.</summary>
  public static int RoundUpLayers(int layers)
  {
    return (layers + LayerStep - 1) / LayerStep * LayerStep;
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
