using System;
using Curitibia.Core;

namespace Curitibia.World.Features;

/// <summary>One tree: where its trunk stands and how big it is, all in voxels.</summary>
public readonly record struct Tree(int X, int Z, int BaseY, int TrunkHeight, int CanopyRadius)
{
  /// <summary>The trunk is two voxels square (a metre thick) and covers X..X+1, Z..Z+1.</summary>
  public int CanopyCenterY => BaseY + TrunkHeight - 1;

  public bool IsTrunk(int x, int y, int z)
  {
    return x >= X && x <= X + 1 && z >= Z && z <= Z + 1 && y >= BaseY && y < BaseY + TrunkHeight;
  }

  public bool IsLeaf(int x, int y, int z)
  {
    // A slightly squashed ball around the top of the trunk (centre between the four trunk columns).
    var dx = x - X - 0.5f;
    var dz = z - Z - 0.5f;
    var dy = (y - CanopyCenterY) * 1.25f;
    var r = CanopyRadius + 0.5f;

    return dx * dx + dy * dy + dz * dz <= r * r && y >= BaseY + TrunkHeight - CanopyRadius - 1;
  }

  /// <summary>Highest voxel (exclusive) this tree occupies in a column, or 0 when it does not touch it.</summary>
  public int TopInColumn(int x, int z)
  {
    var top = 0;

    for (int y = BaseY + TrunkHeight + CanopyRadius; y >= BaseY; y--)
    {
      if (IsTrunk(x, y, z) || IsLeaf(x, y, z))
      {
        top = y + 1;
        break;
      }
    }

    return top;
  }
}

/// <summary>
/// Trees as a pure function of (seed, voxel column), so every chunk can place the parts of a tree that fall inside it, even
/// when the tree is rooted in the next chunk, and all peers agree. The world is cut into square cells; each cell may hold
/// one tree at a hashed position inside it. Engine-free.
/// </summary>
public sealed class TreeFeature
{
  /// <summary>Edge of a placement cell in voxels (6 m).</summary>
  public const int CellSize = 12;

  /// <summary>How far, in voxels, a tree reaches from its root column (the canopy plus the trunk's second column).</summary>
  public const int MaxReach = 6;

  /// <summary>Tallest a tree can be (trunk plus canopy), in voxels, for sizing chunk arrays.</summary>
  public const int MaxHeight = 22;

  /// <summary>Keep this many voxels around the world origin free of trees, so the spawn is not inside one.</summary>
  const int SpawnClearing = 18;

  const float Density = 0.3f;

  readonly int seed;
  readonly Func<int, int, int> groundHeight;

  /// <param name="groundHeight">Terrain height (voxels in the column) at a world column.</param>
  public TreeFeature(int seed, Func<int, int, int> groundHeight)
  {
    this.seed = seed;
    this.groundHeight = groundHeight;
  }

  /// <summary>The tree rooted in a cell, if any.</summary>
  public bool TryGetTree(int cellX, int cellZ, out Tree tree)
  {
    tree = default;

    var h = Hash(cellX, cellZ, 1);

    if ((h & 0xffff) / 65535f > Density)
    {
      return false;
    }

    // Keep the 2x2 trunk and its canopy inside the cell's own margin so neighbouring cells never overlap too much.
    var span = CellSize - 2 * 4;
    var x = cellX * CellSize + 4 + (int)((h >> 16) % (uint)span);
    var z = cellZ * CellSize + 4 + (int)(Hash(cellX, cellZ, 2) % (uint)span);

    if (x * x + z * z < SpawnClearing * SpawnClearing)
    {
      return false;
    }

    var baseY = Math.Min(Math.Min(groundHeight(x, z), groundHeight(x + 1, z)), Math.Min(groundHeight(x, z + 1), groundHeight(x + 1, z + 1)));
    var variation = Hash(cellX, cellZ, 3);
    var trunk = 8 + (int)(variation % 5);          // 4 to 6 m
    var radius = 3 + (int)((variation >> 8) % 2);  // 3 or 4 voxels

    tree = new Tree(x, z, baseY - 1, trunk + 1, radius);

    return true;
  }

  /// <summary>Calls <paramref name="visit"/> for every tree that can touch the voxel columns minX..maxX, minZ..maxZ (in a fixed order).</summary>
  public void ForTreesNear(int minX, int minZ, int maxX, int maxZ, Action<Tree> visit)
  {
    var c0x = VoxelMath.FloorDiv(minX - MaxReach, CellSize);
    var c1x = VoxelMath.FloorDiv(maxX + MaxReach, CellSize);
    var c0z = VoxelMath.FloorDiv(minZ - MaxReach, CellSize);
    var c1z = VoxelMath.FloorDiv(maxZ + MaxReach, CellSize);

    for (int cz = c0z; cz <= c1z; cz++)
    {
      for (int cx = c0x; cx <= c1x; cx++)
      {
        if (TryGetTree(cx, cz, out var tree))
        {
          visit(tree);
        }
      }
    }
  }

  /// <summary>Top (exclusive) of any tree standing in a world column, or 0 for none.</summary>
  public int TopAt(int x, int z)
  {
    var top = 0;

    ForTreesNear(x, z, x, z, tree => top = Math.Max(top, tree.TopInColumn(x, z)));

    return top;
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
