using System;
using Curitibia.Core;

namespace Curitibia.World.Features;

/// <summary>
/// A dead log lying on the ground: a trunk on its side, two voxels thick, with a broken, jagged stump standing at one end where
/// the tree snapped. Block is oak or birch wood.
/// </summary>
public readonly record struct FallenLog(int X, int Z, int BaseY, int Length, bool AlongX, int Thickness, int StumpHeight, byte Block)
{
  /// <summary>How far the log reaches from its start column, along its axis and across it.</summary>
  public int Reach => Length + 3;

  public bool IsWood(int x, int y, int z)
  {
    var along = AlongX ? x - X : z - Z;
    var across = AlongX ? z - Z : x - X;

    // The body.
    if (y >= BaseY && y < BaseY + Thickness && along >= 0 && along < Length && across >= 0 && across < Thickness)
    {
      return true;
    }

    // The stump at the start: a bit thicker than the log, broken off at a ragged height.
    if (along >= -1 && along < Thickness + 1 && across >= -1 && across < Thickness + 1)
    {
      var jag = (int)(Hash(x, z) % 3);

      return y >= BaseY && y < BaseY + StumpHeight - jag;
    }

    return false;
  }

  static uint Hash(int x, int z)
  {
    unchecked
    {
      var h = (uint)x * 668265263u ^ (uint)z * 2246822519u;

      h = (h ^ (h >> 13)) * 1274126177u;

      return h ^ (h >> 16);
    }
  }
}

/// <summary>
/// Dead logs as a pure function of (seed, cell), like trees: every chunk places the part of any log that falls inside it. Only on
/// nearly level, solid ground; the log rests on the highest point under it. Engine-free.
/// </summary>
public sealed class LogFeature
{
  public const int CellSize = 28;

  /// <summary>The farthest a log reaches from its cell, for finding the logs that touch a chunk.</summary>
  public const int MaxReach = 20;

  const int SpawnClearing = 24;

  readonly int seed;
  readonly Func<int, int, int> groundHeight;
  readonly Func<int, int, float> density;
  readonly Func<int, int, bool>? groundOpen;

  /// <param name="density">Chance 0 to 1 of a log in a cell, by the world column picked for it.</param>
  /// <param name="groundOpen">Whether a cave breaks through the ground at a world column (no log there).</param>
  public LogFeature(int seed, Func<int, int, int> groundHeight, Func<int, int, float> density, Func<int, int, bool>? groundOpen = null)
  {
    this.seed = seed;
    this.groundHeight = groundHeight;
    this.density = density;
    this.groundOpen = groundOpen;
  }

  public bool TryGetLog(int cellX, int cellZ, out FallenLog log)
  {
    log = default;

    var h = Hash(cellX, cellZ, 1);
    var v = Hash(cellX, cellZ, 2);
    var x = cellX * CellSize + 3 + (int)((h >> 8) % 18u);
    var z = cellZ * CellSize + 3 + (int)(Hash(cellX, cellZ, 3) % 18u);

    if (x * x + z * z < SpawnClearing * SpawnClearing || (h & 0xffff) / 65535f > density(x, z))
    {
      return false;
    }

    var alongX = (v & 1) == 0;
    var length = 8 + (int)((v >> 4) % 9u);
    var thickness = (v >> 9) % 3u == 0 ? 1 : 2;
    var top = int.MinValue;
    var bottom = int.MaxValue;

    // Level, solid ground along the whole log and its stump.
    for (int i = -1; i <= length; i++)
    {
      for (int j = -1; j <= thickness; j++)
      {
        var gx = alongX ? x + i : x + j;
        var gz = alongX ? z + j : z + i;
        var ground = groundHeight(gx, gz);

        top = Math.Max(top, ground);
        bottom = Math.Min(bottom, ground);

        if (groundOpen != null && groundOpen(gx, gz))
        {
          return false;
        }
      }
    }

    if (top - bottom > 2)
    {
      return false;
    }

    log = new FallenLog(x, z, top, length, alongX, thickness, 4 + (int)((v >> 12) % 4u), (v >> 16) % 3u == 0 ? BlockIds.BirchWood : BlockIds.Wood);

    return true;
  }

  public void ForLogsNear(int minX, int minZ, int maxX, int maxZ, Action<FallenLog> visit)
  {
    var c0x = VoxelMath.FloorDiv(minX - MaxReach, CellSize);
    var c1x = VoxelMath.FloorDiv(maxX + MaxReach, CellSize);
    var c0z = VoxelMath.FloorDiv(minZ - MaxReach, CellSize);
    var c1z = VoxelMath.FloorDiv(maxZ + MaxReach, CellSize);

    for (int cz = c0z; cz <= c1z; cz++)
    {
      for (int cx = c0x; cx <= c1x; cx++)
      {
        if (TryGetLog(cx, cz, out var log))
        {
          visit(log);
        }
      }
    }
  }

  uint Hash(int x, int z, int salt)
  {
    unchecked
    {
      var h = (uint)seed * 374761393u + (uint)x * 668265263u + (uint)z * 2246822519u + (uint)salt * 3266489917u;

      h = (h ^ (h >> 13)) * 1274126177u;

      return h ^ (h >> 16);
    }
  }
}
