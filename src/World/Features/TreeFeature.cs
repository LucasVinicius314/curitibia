using System;
using Curitibia.Core;

namespace Curitibia.World.Features;

public enum TreeKind : byte
{
  Oak,
  Pine,
  Cactus,
  Birch,
}

/// <summary>
/// One tree: where its trunk stands and how big it is, all in voxels. <see cref="Variant"/> picks a shape within the kind:
/// oak (0 round, 1 tall, 2 wide and low, 3 bush), birch (0 tall and slim, 1 small), pine (0 spruce, 1 slim, 2 broad fir,
/// 3 young) and cactus (0 classic, 1 two arms, 2 barrel, 3 spire). <see cref="Width"/> is the trunk's thickness (1 or 2
/// voxels); <see cref="Lean"/> bends the upper half of the trunk one voxel (1 +X, 2 -X, 3 +Z, 4 -Z) and the canopy with it;
/// the wide oak flares out at its roots. Cacti use <see cref="ArmSide"/> (-1 left, 1 right, 2 both)
/// for their arms and <see cref="Width"/> (1 or 2) for their thickness.
/// </summary>
public readonly record struct Tree(int X, int Z, int BaseY, int TrunkHeight, int CanopyRadius, TreeKind Kind = TreeKind.Oak, int ArmSide = 0, int Width = 2, int Variant = 0, int Lean = 0)
{
  public int CanopyCenterY => BaseY + TrunkHeight - 1;

  int LeanX => Lean switch { 1 => 1, 2 => -1, _ => 0 };

  int LeanZ => Lean switch { 3 => 1, 4 => -1, _ => 0 };

  /// <summary>The middle of the canopy and of the top of the trunk, in voxel units (between the columns of an even trunk).</summary>
  float CentreX => X + (Width - 1) / 2f + LeanX;

  float CentreZ => Z + (Width - 1) / 2f + LeanZ;

  /// <summary>Block of the trunk (and cactus arm): always wins over leaves.</summary>
  public byte TrunkBlock => Kind switch
  {
    TreeKind.Cactus => BlockIds.Cactus,
    TreeKind.Birch => BlockIds.BirchWood,
    _ => BlockIds.Wood,
  };

  /// <summary>Block of the canopy; only fills air.</summary>
  public byte LeafBlock => Kind switch
  {
    TreeKind.Pine => BlockIds.PineLeaves,
    TreeKind.Birch => BlockIds.BirchLeaves,
    _ => BlockIds.Leaves,
  };

  /// <summary>How flattened a round canopy is (above 1 flat and wide, below 1 tall and egg-shaped).</summary>
  float Squash => Kind switch
  {
    TreeKind.Birch => Variant == 0 ? 0.8f : 1f,
    _ => Variant switch { 1 => 0.85f, 2 => 1.9f, 3 => 1.2f, _ => 1.25f },
  };

  /// <summary>The trunk is <see cref="Width"/> voxels square and covers X..X+Width-1, Z..Z+Width-1 (shifted by the lean above the middle).</summary>
  public bool IsTrunk(int x, int y, int z)
  {
    if (y >= BaseY && y < BaseY + TrunkHeight)
    {
      var middle = BaseY + TrunkHeight / 2;
      var flare = Kind == TreeKind.Oak && Variant == 2 && y < BaseY + 2 ? 1 : 0;

      if ((Lean == 0 || y <= middle) && Covers(x, z, X, Z, flare))
      {
        return true;
      }

      if (Lean != 0 && y >= middle && Covers(x, z, X + LeanX, Z + LeanZ, 0))
      {
        return true;
      }
    }

    return Kind == TreeKind.Cactus && ArmSide != 0 && ((ArmSide != 1 && IsArm(x, y, z, -1)) || (ArmSide != -1 && IsArm(x, y, z, 1)));
  }

  bool Covers(int x, int z, int originX, int originZ, int flare)
  {
    return x >= originX - flare && x < originX + Width + flare && z >= originZ - flare && z < originZ + Width + flare;
  }

  /// <summary>A cactus arm: a short link out of the trunk, then a pillar of the trunk's width going up. The right one sits a bit lower.</summary>
  bool IsArm(int x, int y, int z, int side)
  {
    if (z < Z || z >= Z + Width)
    {
      return false;
    }

    var armY = BaseY + (side < 0 ? TrunkHeight / 2 : TrunkHeight * 2 / 5);
    var rise = Width + (side < 0 ? TrunkHeight / 3 : TrunkHeight / 4);
    var link = side < 0 ? X - 1 : X + Width;
    var pillar0 = side < 0 ? X - 1 - Width : X + Width + 1;

    if (x == link && y >= armY && y < armY + Width)
    {
      return true;
    }

    return x >= pillar0 && x < pillar0 + Width && y >= armY && y < armY + rise;
  }

  public bool IsLeaf(int x, int y, int z)
  {
    switch (Kind)
    {
      case TreeKind.Cactus:
        return false;

      case TreeKind.Pine:
      {
        // A cone from a few voxels above the ground to a tip above the trunk, with every third layer pulled in.
        var bottom = BaseY + (Variant == 3 ? 2 : 3);
        var top = BaseY + TrunkHeight + 1;

        if (y < bottom || y > top)
        {
          return false;
        }

        var r = 1f + (CanopyRadius - 1f) * (top - y) / (top - bottom);

        if ((y - bottom) % 3 == 2)
        {
          r -= 1f;
        }

        var px = x - CentreX;
        var pz = z - CentreZ;

        return px * px + pz * pz <= (r + 0.5f) * (r + 0.5f);
      }

      default:
      {
        // A ball around the top of the trunk (centre between the four trunk columns), squashed or stretched by the shape.
        var dx = x - CentreX;
        var dz = z - CentreZ;
        var dy = (y - CanopyCenterY) * Squash;
        var r = CanopyRadius + 0.5f;

        return dx * dx + dy * dy + dz * dz <= r * r;
      }
    }
  }

  /// <summary>Highest voxel (exclusive) this tree occupies in a column, or 0 when it does not touch it.</summary>
  public int TopInColumn(int x, int z)
  {
    var top = 0;

    for (int y = BaseY + TrunkHeight + CanopyRadius + 1; y >= BaseY; y--)
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
  readonly Func<int, int, float>? density;
  readonly Func<int, int, TreeKind>? kindAt;

  /// <param name="groundHeight">Terrain height (voxels in the column) at a world column.</param>
  /// <param name="density">Chance 0 to 1 of a tree in a cell, by the world column picked for it (null = a fixed 0.3).</param>
  /// <param name="kindAt">Which kind of tree grows at a world column (null = oak everywhere).</param>
  public TreeFeature(int seed, Func<int, int, int> groundHeight, Func<int, int, float>? density = null, Func<int, int, TreeKind>? kindAt = null)
  {
    this.seed = seed;
    this.groundHeight = groundHeight;
    this.density = density;
    this.kindAt = kindAt;
  }

  /// <summary>The tree rooted in a cell, if any.</summary>
  public bool TryGetTree(int cellX, int cellZ, out Tree tree)
  {
    tree = default;

    var h = Hash(cellX, cellZ, 1);

    // Keep the 2x2 trunk and its canopy inside the cell's own margin so neighbouring cells never overlap too much.
    var span = CellSize - 2 * 4;
    var x = cellX * CellSize + 4 + (int)((h >> 16) % (uint)span);
    var z = cellZ * CellSize + 4 + (int)(Hash(cellX, cellZ, 2) % (uint)span);

    if (x * x + z * z < SpawnClearing * SpawnClearing)
    {
      return false;
    }

    if ((h & 0xffff) / 65535f > (density?.Invoke(x, z) ?? Density))
    {
      return false;
    }

    // The lowest ground under the widest trunk, so the trunk never hangs in the air.
    var baseY = int.MaxValue;

    for (int dz = 0; dz < 3; dz++)
    {
      for (int dx = 0; dx < 3; dx++)
      {
        baseY = Math.Min(baseY, groundHeight(x + dx, z + dz));
      }
    }

    var variation = Hash(cellX, cellZ, 3);
    var pick = (int)(Hash(cellX, cellZ, 4) % 100);
    var kind = kindAt?.Invoke(x, z) ?? TreeKind.Oak;
    var root = baseY - 1;

    var thickness = (int)((variation >> 20) % 3);   // 0, 1 or 2: picks a width among those a shape allows
    var bend = (variation >> 24) % 4 == 0 ? 1 + (int)((variation >> 28) % 4) : 0;

    switch (kind)
    {
      case TreeKind.Pine:
        tree = pick switch
        {
          < 40 => new Tree(x, z, root, 11 + (int)(variation % 5) + (thickness == 0 ? 2 : 0), 3 + (int)((variation >> 8) % 2), kind, 0, thickness == 0 ? 2 : 1, 0),   // spruce, 5 to 7 m
          < 65 => new Tree(x, z, root, 15 + (int)(variation % 3), 2 + (int)((variation >> 8) % 2), kind, 0, 1, 1),                        // slim and tall
          < 85 => new Tree(x, z, root, 9 + (int)(variation % 3), 5, kind, 0, 2, 2),                                  // broad fir
          _ => new Tree(x, z, root, 5 + (int)(variation % 3), 2, kind, 0, 1, 3),                                                          // young
        };
        break;

      case TreeKind.Birch:
        tree = pick < 65
          ? new Tree(x, z, root, 11 + (int)(variation % 4), 3, kind, 0, 1, 0, bend)   // tall and slim, sometimes leaning
          : new Tree(x, z, root, 7 + (int)(variation % 3), 2, kind, 0, 1, 1, bend);   // small
        break;

      case TreeKind.Cactus:
      {
        var thin = (variation >> 16) % 2 == 0;
        var arm = (variation >> 8) % 3 == 0 ? 0 : (variation >> 12) % 2 == 0 ? -1 : 1;

        tree = pick switch
        {
          < 45 => new Tree(x, z, root, (thin ? 6 : 7) + (int)(variation % 6), 0, kind, arm, thin ? 1 : 2, 0),      // classic: zero or one arm
          < 70 => new Tree(x, z, root, 10 + (int)(variation % 4), 0, kind, 2, thin ? 1 : 2, 1),                    // two arms
          < 85 => new Tree(x, z, root, 3 + (int)(variation % 2), 0, kind, 0, 2, 2),                                // barrel
          _ => new Tree(x, z, root, 10 + (int)(variation % 5), 0, kind, 0, 1, 3),                                  // spire
        };
      }

        break;

      default:
        tree = pick switch
        {
          < 45 => new Tree(x, z, root, 9 + (int)(variation % 5) + (thickness == 0 ? 0 : 2), 3 + (int)((variation >> 8) % 2), kind, 0, thickness == 0 ? 1 : 2, 0, bend),   // round: thin, or thick and taller
          < 70 => new Tree(x, z, root, 13 + (int)(variation % 4) + (thickness == 0 ? 1 : 0), 3, kind, 0, thickness == 0 ? 2 : 1, 1, bend),                                                   // tall, usually slim
          < 90 => new Tree(x, z, root, 9 + (int)(variation % 3), 5, kind, 0, 2, 2),                                                                               // wide: a thick flared trunk
          _ => new Tree(x, z, root, 5 + (int)(variation % 2), 2, kind, 0, 1, 3),                                                                                  // bush: a single stem
        };
        break;
    }

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
