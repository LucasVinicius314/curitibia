using System;
using Curitibia.Core;
using Curitibia.World;

namespace Curitibia.Map;

/// <summary>
/// Turns one chunk into a top-down map picture: one pixel per voxel column, coloured by the highest block (leaves and cactus
/// included, so forests read as canopy) and shaded by the slope against the columns to the west and north, so hills and cliffs
/// stand out the way they do on a relief map. Engine-free, so the colours and the shading are unit-tested.
/// </summary>
public static class MapTileBuilder
{
  /// <summary>Pixels along one side of a tile (one per voxel column of a chunk).</summary>
  public const int Size = VoxelMath.ChunkSize;

  /// <summary>Bytes in a tile: RGBA, row by row.</summary>
  public const int Bytes = Size * Size * 4;

  /// <summary>What a column with nothing in it looks like (the bottom of the world).</summary>
  static readonly (byte r, byte g, byte b) Void = (16, 20, 30);

  /// <summary>Map colour per block id.</summary>
  static readonly (byte r, byte g, byte b)[] palette = BuildPalette();

  static (byte, byte, byte)[] BuildPalette()
  {
    var colors = new (byte, byte, byte)[BlockIds.MaxId + 1];

    for (int i = 0; i < colors.Length; i++)
    {
      colors[i] = (128, 128, 128);
    }

    colors[BlockIds.Grass] = (98, 176, 78);
    colors[BlockIds.Stone] = (122, 128, 138);
    colors[BlockIds.Wood] = (92, 64, 40);
    colors[BlockIds.Leaves] = (52, 128, 60);
    colors[BlockIds.Dirt] = (121, 85, 58);
    colors[BlockIds.Sand] = (222, 205, 150);
    colors[BlockIds.Snow] = (240, 245, 250);
    colors[BlockIds.Cactus] = (60, 128, 58);
    colors[BlockIds.PineLeaves] = (30, 90, 62);
    colors[BlockIds.BirchWood] = (230, 228, 220);
    colors[BlockIds.BirchLeaves] = (122, 168, 60);
    colors[BlockIds.Hellrock] = (92, 42, 44);
    colors[BlockIds.Magma] = (235, 110, 30);

    return colors;
  }

  /// <summary>The map colour of a block id.</summary>
  public static (byte r, byte g, byte b) ColorOf(byte block)
  {
    return block < palette.Length ? palette[block] : ((byte)128, (byte)128, (byte)128);
  }

  /// <summary>Layers up to and including the highest solid block of a column, or 0 for an empty column (or no chunk).</summary>
  public static int TopHeight(ChunkData? blocks, int lx, int lz)
  {
    if (blocks == null)
    {
      return 0;
    }

    var top = blocks.TopLayer(lx, lz);

    // Short grass on top is not ground: the column reads as the grass under it.
    return top > 0 && BlockIds.IsPlant(blocks.Get(lx, top - 1, lz)) ? top - 1 : top;
  }

  /// <summary>
  /// Fills <paramref name="rgba"/> (<see cref="Bytes"/> long) for a chunk. <paramref name="west"/> and <paramref name="north"/>
  /// are the neighbouring chunks (or null), only used to shade the columns along the shared border.
  /// </summary>
  public static void Render(ChunkData blocks, ChunkData? west, ChunkData? north, byte[] rgba)
  {
    Span<int> heights = stackalloc int[Size * Size];

    for (int lz = 0; lz < Size; lz++)
    {
      for (int lx = 0; lx < Size; lx++)
      {
        heights[lx + lz * Size] = TopHeight(blocks, lx, lz);
      }
    }

    for (int lz = 0; lz < Size; lz++)
    {
      for (int lx = 0; lx < Size; lx++)
      {
        var h = heights[lx + lz * Size];
        var o = (lx + lz * Size) * 4;

        if (h == 0)
        {
          rgba[o] = Void.r;
          rgba[o + 1] = Void.g;
          rgba[o + 2] = Void.b;
          rgba[o + 3] = 255;
          continue;
        }

        var hw = lx > 0 ? heights[lx - 1 + lz * Size] : west != null ? TopHeight(west, Size - 1, lz) : h;
        var hn = lz > 0 ? heights[lx + (lz - 1) * Size] : north != null ? TopHeight(north, lx, Size - 1) : h;

        // Lit from the north west: higher than the columns on that side is lighter, lower is darker; and a little brighter with altitude.
        var slope = h - (hw + hn) * 0.5f;
        var shade = 1f + Math.Clamp(slope * 0.07f, -0.35f, 0.25f);
        shade *= 1f + Math.Clamp((h - VoxelMath.SeaLevel - 10) * 0.004f, -0.15f, 0.25f);

        var (r, g, b) = ColorOf(blocks.Get(lx, h - 1, lz));

        rgba[o] = Scale(r, shade);
        rgba[o + 1] = Scale(g, shade);
        rgba[o + 2] = Scale(b, shade);
        rgba[o + 3] = 255;
      }
    }
  }

  static byte Scale(byte channel, float shade)
  {
    return (byte)Math.Clamp((int)(channel * shade + 0.5f), 0, 255);
  }
}
