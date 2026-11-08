using System;

namespace Curitibia.Core;

/// <summary>Engine-free voxel coordinate math (no Godot types, so it is unit-testable).</summary>
public static class VoxelMath
{
  public const int ChunkSize = 32;
  public const int ChunkHeight = 32;
  public const int ChunkVolume = ChunkSize * ChunkSize * ChunkHeight;

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
