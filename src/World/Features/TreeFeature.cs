using System;
using Curitibia.Core;

namespace Curitibia.World.Features;

public enum TreeKind : byte
{
  Oak,
  Pine,
  Cactus,
  Birch,

  /// <summary>A titan of the Titan Grove: a trunk several voxels thick and 80 to 110 m tall, flared at the roots, with limbs and tiers of canopy.</summary>
  Giant,
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
  public float CentreX => X + (Width - 1) / 2f + LeanX;

  public float CentreZ => Z + (Width - 1) / 2f + LeanZ;

  /// <summary>How far, in voxels, the tree reaches from its root column (a giant's canopy is wide).</summary>
  public int Reach => Kind == TreeKind.Giant ? (int)(CanopyRadius * LimbReachScale) + Width + 2 : TreeFeature.MaxReach;

  /// <summary>The longest limbs of a giant reach this many times as far as the leaves above them (and nothing of the tree goes farther).</summary>
  public const float LimbReachScale = 1.7f;

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
    if (Kind == TreeKind.Giant)
    {
      return IsGiantWood(x, y, z);
    }

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

  // ---- giants: Width is the trunk's thickness at the roots' end (7 to 9), TrunkHeight the whole height from the root, CanopyRadius the
  // radius of the lowest tier of leaves; the variant picks how many tiers there are.

  static readonly float[] TierHeight = { 0.50f, 0.60f, 0.70f, 0.80f, 0.90f };
  static readonly float[] TierScale = { 1f, 0.95f, 0.85f, 0.7f, 0.5f };

  /// <summary>Tiers of leaves, stacked so close that they merge into one deep canopy (4 or 5).</summary>
  public int Tiers => Variant == 1 ? 4 : 5;

  /// <summary>Layers above the root where a giant's trunk stops: at the middle of its top tier of leaves.</summary>
  public int GiantTrunkEnd => (int)(TrunkHeight * TierHeight[Tiers - 1]);

  /// <summary>The layers a tier of leaves (and the limbs under it) occupies, for a giant.</summary>
  public void TierBand(int tier, out int low, out int high)
  {
    var radius = CanopyRadius * TierScale[tier];
    var thickness = LeafThickness(radius);
    var centre = BaseY + TrunkHeight * TierHeight[tier];

    low = (int)(centre - thickness) - 4;
    high = (int)(centre + thickness) + 1;
  }

  static float LeafThickness(float radius) => 8f + radius * 0.22f;

  float Phase(int salt) => Scatter(X, salt, Z) * MathF.Tau;

  /// <summary>The middle of a giant's trunk at a height: it sways gently from side to side (the base stays where it is rooted).</summary>
  public void TrunkCentreAt(int y, out float x, out float z)
  {
    var up = Math.Max(0, y - BaseY);
    var ramp = Math.Min(1f, up / 40f);

    ramp = ramp * ramp * (3f - 2f * ramp);
    x = CentreX + 2.6f * ramp * MathF.Sin(up / 47f + Phase(5));
    z = CentreZ + 2.6f * ramp * MathF.Cos(up / 53f + Phase(6));
  }

  /// <summary>The average radius of a giant's trunk at a height: flared out at the roots, tapering a little as it rises.</summary>
  public float TrunkRadiusAt(int y)
  {
    var up = Math.Max(0, y - BaseY);
    var flare = Math.Max(0f, 1f - up / 16f);

    return Width / 2f * (1f + 1.1f * flare * flare) * (1f - 0.3f * up / TrunkHeight);
  }

  /// <summary>The trunk: round on average but gnarled (bulges and hollows that turn as it climbs), swaying, flared at the roots. The branches, roots and vines are in <see cref="GiantStructure"/>.</summary>
  bool IsGiantWood(int x, int y, int z)
  {
    if (y < BaseY || y >= BaseY + GiantTrunkEnd) // the trunk ends inside the top tier of leaves, not above it
    {
      return false;
    }

    TrunkCentreAt(y, out var cx, out var cz);

    var dx = x - cx;
    var dz = z - cz;
    var d2 = dx * dx + dz * dz;
    var radius = TrunkRadiusAt(y);

    if (d2 > radius * radius * 1.7f)
    {
      return false;
    }

    if (d2 <= radius * radius * 0.5f)
    {
      return true;
    }

    var up = y - BaseY;
    var angle = MathF.Atan2(dz, dx);
    var gnarl = 1f + 0.12f * MathF.Sin(3f * angle + Phase(7) + up / 27f) + 0.07f * MathF.Sin(5f * angle + Phase(8) - up / 17f);

    return d2 <= radius * radius * gnarl * gnarl;
  }

  /// <summary>A smooth noise of a world column (about -1 to 1) that makes the outline of the canopy lumpy instead of circular.</summary>
  public float CanopyLump(int x, int z)
  {
    const float scale = 1f / 11f;
    var fx = x * scale;
    var fz = z * scale;
    var ix = (int)MathF.Floor(fx);
    var iz = (int)MathF.Floor(fz);
    var tx = fx - ix;
    var tz = fz - iz;

    tx = tx * tx * (3f - 2f * tx);
    tz = tz * tz * (3f - 2f * tz);

    var a = Scatter(ix, 21, iz);
    var b = Scatter(ix + 1, 21, iz);
    var c = Scatter(ix, 21, iz + 1);
    var d = Scatter(ix + 1, 21, iz + 1);
    var top = a + (b - a) * tx;
    var bottom = c + (d - c) * tx;

    return (top + (bottom - top) * tz) * 2f - 1f;
  }

  bool IsGiantLeaf(int x, int y, int z)
  {
    return IsCanopyLeaf(x, y, z, CanopyLump(x, z));
  }

  /// <summary>
  /// Whether a column at squared horizontal distance <paramref name="d2"/> from the middle of the trunk meets a disc of leaves at all,
  /// and the layers (low to high) where it can: a disc is thick in the middle and thin at its edge. Lets the placer skip every column
  /// a disc does not reach and every layer it does not fill.
  /// </summary>
  public bool CanopyBand(int tier, float lump, float d2, out int low, out int high)
  {
    var radius = CanopyRadius * TierScale[tier] * (1f + 0.17f * lump);
    var fraction = 1.13f - d2 / (radius * radius); // 1.13: the ragged edge reaches 13% past the outline

    low = 0;
    high = -1;

    if (fraction <= 0f)
    {
      return false;
    }

    var half = LeafThickness(radius) * MathF.Sqrt(fraction);
    var centre = BaseY + TrunkHeight * TierHeight[tier];

    low = (int)MathF.Floor(centre - half);
    high = (int)MathF.Ceiling(centre + half);

    return true;
  }

  /// <summary>Whether one voxel is leaf in one disc (see <see cref="CanopyBand"/> for the columns and layers worth asking about).</summary>
  public bool IsLeafInTier(int tier, int x, int y, int z, float lump, float d2)
  {
    var radius = CanopyRadius * TierScale[tier] * (1f + 0.17f * lump);
    var thickness = LeafThickness(radius);
    var dy = y - (BaseY + TrunkHeight * TierHeight[tier]);

    if (Math.Abs(dy) > thickness)
    {
      return false;
    }

    var edge = 1f + 0.26f * (Scatter(x, y, z) - 0.5f);

    return d2 / (radius * radius) + dy * dy / (thickness * thickness) <= edge && Scatter(x * 3 + 1, y, z * 3 + 2) > 0.012f;
  }

  /// <summary>Whether a voxel is in one of the discs of leaves; <paramref name="lump"/> is <see cref="CanopyLump"/> of its column (computed once per column by the placer).</summary>
  public bool IsCanopyLeaf(int x, int y, int z, float lump)
  {
    var dx = x - CentreX;
    var dz = z - CentreZ;
    var horizontal = dx * dx + dz * dz;

    for (int tier = 0; tier < Tiers; tier++)
    {
      var radius = CanopyRadius * TierScale[tier] * (1f + 0.17f * lump);
      var thickness = LeafThickness(radius);
      var dy = y - (BaseY + TrunkHeight * TierHeight[tier]);

      if (Math.Abs(dy) > thickness)
      {
        continue;
      }

      // A flat, lumpy disc: ragged at the edge so it does not read as a perfect lens, with the odd hole in it.
      var edge = 1f + 0.26f * (Scatter(x, y, z) - 0.5f);

      if (horizontal / (radius * radius) + dy * dy / (thickness * thickness) <= edge && Scatter(x * 3 + 1, y, z * 3 + 2) > 0.012f)
      {
        return true;
      }
    }

    return false;
  }

  static float Scatter(int x, int y, int z)
  {
    unchecked
    {
      var h = (uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)z * 83492791u;

      h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;

      return (h & 0xffff) / 65535f;
    }
  }

  public bool IsLeaf(int x, int y, int z)
  {
    if (Kind == TreeKind.Giant)
    {
      return IsGiantLeaf(x, y, z);
    }

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
  readonly Func<int, int, bool>? groundOpen;
  readonly Func<int, int, float>? giantDensity;

  // Every chunk within reach asks about the same cells: the answers never change, so they are kept.
  readonly System.Collections.Concurrent.ConcurrentDictionary<long, (bool found, Tree tree)> giantCache = new();
  readonly System.Collections.Concurrent.ConcurrentDictionary<long, (bool found, int x, int z, uint priority)> candidateCache = new();

  static long CellKey(int cellX, int cellZ) => ((long)cellX << 32) ^ (uint)cellZ;

  /// <param name="groundHeight">Terrain height (voxels in the column) at a world column.</param>
  /// <param name="density">Chance 0 to 1 of a tree in a cell, by the world column picked for it (null = a fixed 0.3).</param>
  /// <param name="kindAt">Which kind of tree grows at a world column (null = oak everywhere).</param>
  /// <param name="groundOpen">Whether a cave opens through the ground at a world column (a tree must not take root there: its trunk would hang in the air). Null = never.</param>
  /// <param name="giantDensity">Chance 0 to 1 of a giant tree in a giant cell, by the world column picked for it (null = no giants).</param>
  public TreeFeature(int seed, Func<int, int, int> groundHeight, Func<int, int, float>? density = null, Func<int, int, TreeKind>? kindAt = null, Func<int, int, bool>? groundOpen = null, Func<int, int, float>? giantDensity = null)
  {
    this.giantDensity = giantDensity;
    this.groundOpen = groundOpen;
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

    // No tree where a cave breaks through the ground under the trunk (or right beside it): it would stand on nothing.
    if (groundOpen != null)
    {
      for (int dz = -1; dz < 4; dz++)
      {
        for (int dx = -1; dx < 4; dx++)
        {
          if (groundOpen(x + dx, z + dz))
          {
            return false;
          }
        }
      }
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

  /// <summary>Edge of a placement cell of the giants, in voxels (16 m): about twice as many as with 44-voxel cells.</summary>
  public const int GiantCell = 22;

  /// <summary>The widest a giant reaches from its root column: tier radius plus trunk, with room to spare.</summary>
  public const int GiantReach = 100;

  /// <summary>Giants keep this far from the world origin (their canopies are wide, the spawn must stay in the open).</summary>
  const int GiantSpawnClearing = 70;

  /// <summary>Trunks of two giants keep at least this far apart, in voxels (a trunk is up to 12 thick at the roots).</summary>
  const int GiantSeparation = 16;

  /// <summary>Where a giant cell would put a giant, and its priority, if the biome and the density allow one (before the ground is checked).</summary>
  bool GiantCandidate(int cellX, int cellZ, out int x, out int z, out uint priority)
  {
    var key = CellKey(cellX, cellZ);

    if (!candidateCache.TryGetValue(key, out var known))
    {
      var found = ComputeGiantCandidate(cellX, cellZ, out var cx, out var cz, out var cp);

      known = (found, cx, cz, cp);

      if (candidateCache.Count > 200000)
      {
        candidateCache.Clear();
      }

      candidateCache[key] = known;
    }

    x = known.x;
    z = known.z;
    priority = known.priority;

    return known.found;
  }

  bool ComputeGiantCandidate(int cellX, int cellZ, out int x, out int z, out uint priority)
  {
    var h = Hash(cellX, cellZ, 31);
    var span = GiantCell - 2 * 4;

    x = cellX * GiantCell + 4 + (int)((h >> 12) % (uint)span);
    z = cellZ * GiantCell + 4 + (int)(Hash(cellX, cellZ, 33) % (uint)span);
    priority = Hash(cellX, cellZ, 34);

    if (giantDensity == null || x * x + z * z < GiantSpawnClearing * GiantSpawnClearing)
    {
      return false;
    }

    var chance = giantDensity(x, z);

    return chance > 0f && (h & 0xffff) / 65535f <= chance;
  }

  /// <summary>The giant rooted in a giant cell, if any: only on level, solid ground (a trunk this thick cannot stand on a slope or a cave mouth).</summary>
  public bool TryGetGiant(int cellX, int cellZ, out Tree tree)
  {
    tree = default;

    if (giantDensity == null)
    {
      return false;
    }

    var key = CellKey(cellX, cellZ);

    if (!giantCache.TryGetValue(key, out var known))
    {
      var found = ComputeGiant(cellX, cellZ, out var computed);

      known = (found, computed);

      if (giantCache.Count > 100000)
      {
        giantCache.Clear();
      }

      giantCache[key] = known;
    }

    tree = known.tree;

    return known.found;
  }

  bool ComputeGiant(int cellX, int cellZ, out Tree tree)
  {
    tree = default;

    var variation = Hash(cellX, cellZ, 32);

    if (!GiantCandidate(cellX, cellZ, out var x, out var z, out var priority))
    {
      return false;
    }

    // Two trunks must not grow into each other: of two candidates closer than GiantSeparation, the one with the lower priority gives way.
    for (int dz = -1; dz <= 1; dz++)
    {
      for (int dx = -1; dx <= 1; dx++)
      {
        if ((dx != 0 || dz != 0) && GiantCandidate(cellX + dx, cellZ + dz, out var ox, out var oz, out var otherPriority)
          && (ox - x) * (ox - x) + (oz - z) * (oz - z) < GiantSeparation * GiantSeparation
          && (otherPriority > priority || (otherPriority == priority && (dz < 0 || (dz == 0 && dx < 0)))))
        {
          return false;
        }
      }
    }

    var width = 9 + (int)(variation % 4);
    var half = width / 2 + 3;
    var low = int.MaxValue;
    var high = int.MinValue;

    // Level, solid ground under the whole foot of the tree.
    for (int dz = -half; dz <= half + 1; dz += 3)
    {
      for (int dx = -half; dx <= half + 1; dx += 3)
      {
        var ground = groundHeight(x + dx, z + dz);

        low = Math.Min(low, ground);
        high = Math.Max(high, ground);

        if (groundOpen != null && groundOpen(x + dx, z + dz))
        {
          return false;
        }
      }
    }

    if (high - low > 3)
    {
      return false;
    }

    var height = 160 + (int)((variation >> 4) % 60);   // 80 to 110 m
    var canopy = 36 + (int)((variation >> 10) % 13);   // tiers up to 48 voxels (24 m) in radius, wider than the spacing: neighbouring canopies merge

    tree = new Tree(x, z, low - 3, height, canopy, TreeKind.Giant, 0, width, (int)((variation >> 16) % 3));

    return true;
  }

  /// <summary>Calls <paramref name="visit"/> for every giant that can touch the voxel columns minX..maxX, minZ..maxZ (in a fixed order).</summary>
  public void ForGiantsNear(int minX, int minZ, int maxX, int maxZ, Action<Tree> visit)
  {
    if (giantDensity == null)
    {
      return;
    }

    var c0x = VoxelMath.FloorDiv(minX - GiantReach, GiantCell);
    var c1x = VoxelMath.FloorDiv(maxX + GiantReach, GiantCell);
    var c0z = VoxelMath.FloorDiv(minZ - GiantReach, GiantCell);
    var c1z = VoxelMath.FloorDiv(maxZ + GiantReach, GiantCell);

    for (int cz = c0z; cz <= c1z; cz++)
    {
      for (int cx = c0x; cx <= c1x; cx++)
      {
        if (TryGetGiant(cx, cz, out var tree))
        {
          visit(tree);
        }
      }
    }
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
    ForGiantsNear(x, z, x, z, tree => top = Math.Max(top, tree.TopInColumn(x, z)));

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
