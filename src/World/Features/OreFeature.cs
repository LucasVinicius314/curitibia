using System;
using Curitibia.Core;

namespace Curitibia.World.Features;

/// <summary>
/// Veins of ore in the rock: small blobs of copper, tin, iron, silver, gold and platinum, each in its own range of depth (the
/// deeper, the rarer and the more precious). A pure function of (seed, chunk), engine-free: every chunk places the part of any
/// vein centred in it or in a neighbour that falls inside it (a vein is smaller than a chunk, so the eight neighbours are
/// enough), and veins only replace solid rock, so caves carved before are never filled.
/// </summary>
public static class OreFeature
{
  /// <summary>One kind of ore: where it lives (Y relative to the surface baseline, so negative is down), how many veins a chunk tries, and how big they are.</summary>
  public readonly record struct Ore(byte Block, int TopY, int BottomY, int Veins, float Radius, bool InHellrock);

  public static readonly Ore[] All =
  {
    new(BlockIds.CopperOre, -15, -250, 36, 2.4f, false),
    new(BlockIds.TinOre, -40, -300, 28, 2.2f, false),
    new(BlockIds.IronOre, -150, -420, 26, 2.4f, false),
    new(BlockIds.SilverOre, -250, -480, 18, 2.2f, false),
    new(BlockIds.GoldOre, -330, -600, 14, 2.0f, true),
    new(BlockIds.PlatinumOre, -420, -690, 10, 2.0f, true),
  };

  /// <summary>Places the ore of every vein that reaches the chunk (chunkX, chunkZ); <paramref name="top"/> is the highest layer to touch (kept under the ground).</summary>
  public static void Place(ChunkData chunk, int seed, int chunkX, int chunkZ, int top)
  {
    const int size = VoxelMath.ChunkSize;

    for (int ore = 0; ore < All.Length; ore++)
    {
      var kind = All[ore];
      var high = Math.Min(Underground.SeaLevel + kind.TopY, top);
      var low = Math.Max(Underground.SolidFloor + 2, Underground.SeaLevel + kind.BottomY);

      if (high <= low)
      {
        continue;
      }

      for (int dz = -1; dz <= 1; dz++)
      {
        for (int dx = -1; dx <= 1; dx++)
        {
          var sourceX = chunkX + dx;
          var sourceZ = chunkZ + dz;

          for (int vein = 0; vein < kind.Veins; vein++)
          {
            var h = Hash(seed, sourceX, sourceZ, ore, vein);
            var cx = sourceX * size + (int)(h & 0x1f);
            var cz = sourceZ * size + (int)((h >> 5) & 0x1f);
            var cy = low + (int)(((h >> 10) & 0xfffff) % (uint)(high - low));

            PlaceVein(chunk, kind, h, cx, cy, cz, chunkX * size, chunkZ * size);
          }
        }
      }
    }
  }

  static void PlaceVein(ChunkData chunk, Ore kind, uint h, int cx, int cy, int cz, int x0, int z0)
  {
    const int size = VoxelMath.ChunkSize;
    var reach = (int)MathF.Ceiling(kind.Radius * 1.5f);

    // A lumpy ellipsoid: each axis has its own stretch, and a hash per voxel nibbles the edge.
    var sx = 0.8f + ((h >> 3) & 0x7) / 10f;
    var sy = 0.7f + ((h >> 7) & 0x7) / 12f;
    var sz = 0.8f + ((h >> 11) & 0x7) / 10f;

    for (int y = cy - reach; y <= cy + reach; y++)
    {
      if (y < Underground.SolidFloor)
      {
        continue;
      }

      for (int z = cz - reach; z <= cz + reach; z++)
      {
        var lz = z - z0;

        if (lz < 0 || lz >= size)
        {
          continue;
        }

        for (int x = cx - reach; x <= cx + reach; x++)
        {
          var lx = x - x0;

          if (lx < 0 || lx >= size)
          {
            continue;
          }

          var ex = (x - cx) / (kind.Radius * sx);
          var ey = (y - cy) / (kind.Radius * sy);
          var ez = (z - cz) / (kind.Radius * sz);
          var distance = ex * ex + ey * ey + ez * ez;
          var nibble = (Hash((int)h, x, z, y, 7) & 0xff) / 255f * 0.5f;

          if (distance + nibble > 1f)
          {
            continue;
          }

          var block = chunk.Get(lx, y, lz);

          if (block == BlockIds.Stone || (kind.InHellrock && block == BlockIds.Hellrock))
          {
            chunk.Set(lx, y, lz, kind.Block);
          }
        }
      }
    }
  }

  static uint Hash(int a, int b, int c, int d, int e)
  {
    var h = (uint)a * 73856093u ^ (uint)b * 19349663u ^ (uint)c * 83492791u ^ (uint)d * 2654435761u ^ (uint)e * 40503u ^ 0x9e3779b9u;

    h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;

    return h;
  }
}
