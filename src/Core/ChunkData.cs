using System;

namespace Curitibia.Core;

/// <summary>
/// The voxels of one chunk column, stored in vertical sections of <see cref="SectionHeight"/> layers, each in the cheapest form
/// that holds it: one block all through (solid stone for hundreds of layers, open sky: no memory at all), a bitmask choosing
/// between two blocks (rock with caves in it: 2 KB), a palette of up to 16 kinds packed into 2 or 4 bits per voxel (rock with
/// caves and veins of ore: 4 or 8 KB), or a full array (a section being edited, or one with many kinds of block: 16 KB). A world a thousand voxels deep costs memory only where something varies. Reads outside the world are
/// air. Engine-free.
/// </summary>
public sealed class ChunkData
{
  public const int SectionHeight = 16;
  public const int SectionCount = VoxelMath.ChunkHeight / SectionHeight;

  const int SectionVolume = VoxelMath.LayerSize * SectionHeight;
  const int MaskBytes = SectionVolume / 8;
  const byte Air = 0;

  readonly byte[]?[] full = new byte[]?[SectionCount];
  readonly byte[]?[] masks = new byte[]?[SectionCount];

  /// <summary>Palette sections: the kinds of block in the section (at most 16)...</summary>
  readonly byte[]?[] palettes = new byte[]?[SectionCount];

  /// <summary>...and one index into it per voxel, 2 bits each when the palette has at most 4 kinds, else 4 bits.</summary>
  readonly byte[]?[] packed = new byte[]?[SectionCount];

  /// <summary>The block of a one-block section, or the block of the 0 bits of a masked one.</summary>
  readonly byte[] fill = new byte[SectionCount];

  /// <summary>The block of the 1 bits of a masked section.</summary>
  readonly byte[] alt = new byte[SectionCount];

  /// <summary>The lowest column top at generation (layers). The mesher uses it to decide how deep below the ground to draw.</summary>
  public int SurfaceMin { get; set; }

  /// <summary>Sections that take memory (a mask or an array), for checks.</summary>
  public int AllocatedSections
  {
    get
    {
      var count = 0;

      for (int i = 0; i < SectionCount; i++)
      {
        if (full[i] != null || masks[i] != null || packed[i] != null)
        {
          count++;
        }
      }

      return count;
    }
  }

  /// <summary>Approximate bytes the voxels take.</summary>
  public int Bytes
  {
    get
    {
      var total = 0;

      for (int i = 0; i < SectionCount; i++)
      {
        total += full[i] != null ? SectionVolume : packed[i] != null ? packed[i]!.Length : masks[i] != null ? MaskBytes : 0;
      }

      return total;
    }
  }

  static int Local(int x, int y, int z)
  {
    return x + z * VoxelMath.ChunkSize + (y & (SectionHeight - 1)) * VoxelMath.LayerSize;
  }

  public byte Get(int x, int y, int z)
  {
    if ((uint)y >= VoxelMath.ChunkHeight)
    {
      return Air;
    }

    var section = y / SectionHeight;
    var array = full[section];

    if (array != null)
    {
      return array[Local(x, y, z)];
    }

    var bits = packed[section];

    if (bits != null)
    {
      var palette = palettes[section]!;
      var width = palette.Length <= 4 ? 2 : 4;
      var at = Local(x, y, z) * width;

      return palette[(bits[at >> 3] >> (at & 7)) & ((1 << width) - 1)];
    }

    var mask = masks[section];

    if (mask != null)
    {
      var i = Local(x, y, z);

      return (mask[i >> 3] & (1 << (i & 7))) != 0 ? alt[section] : fill[section];
    }

    return fill[section];
  }

  public void Set(int x, int y, int z, byte id)
  {
    if ((uint)y >= VoxelMath.ChunkHeight)
    {
      return;
    }

    var section = y / SectionHeight;
    var array = full[section];

    if (array != null)
    {
      array[Local(x, y, z)] = id;

      return;
    }

    if (packed[section] != null)
    {
      ExpandToArray(section)[Local(x, y, z)] = id;

      return;
    }

    var mask = masks[section];
    var i = Local(x, y, z);

    if (mask == null)
    {
      if (fill[section] == id)
      {
        return;
      }

      // The first different block makes a one-block section a masked one.
      mask = new byte[MaskBytes];
      masks[section] = mask;
      alt[section] = id;
    }

    if (id == fill[section])
    {
      mask[i >> 3] &= (byte)~(1 << (i & 7));
    }
    else if (id == alt[section])
    {
      mask[i >> 3] |= (byte)(1 << (i & 7));
    }
    else
    {
      // A third kind of block: this section needs a full array.
      ExpandToArray(section)[i] = id;
    }
  }

  /// <summary>Makes a whole section one block (frees what it held).</summary>
  public void FillSection(int section, byte id)
  {
    full[section] = null;
    masks[section] = null;
    packed[section] = null;
    palettes[section] = null;
    fill[section] = id;
  }

  /// <summary>True when a section is one block all through (and which).</summary>
  public bool IsUniform(int section, out byte id)
  {
    id = fill[section];

    return full[section] == null && masks[section] == null && packed[section] == null;
  }

  public bool IsEmptySection(int section)
  {
    return full[section] == null && masks[section] == null && packed[section] == null && fill[section] == Air;
  }

  /// <summary>One block all through and not air.</summary>
  public bool IsSolidSection(int section)
  {
    return full[section] == null && masks[section] == null && packed[section] == null && fill[section] != Air;
  }

  /// <summary>
  /// Layers up to and including the highest block of a column (0 for an empty column). Open sky is skipped a section at a
  /// time, so this is cheap even in a tall world.
  /// </summary>
  public int TopLayer(int x, int z)
  {
    for (int section = SectionCount - 1; section >= 0; section--)
    {
      if (IsEmptySection(section))
      {
        continue;
      }

      if (IsSolidSection(section))
      {
        return (section + 1) * SectionHeight;
      }

      for (int y = SectionHeight - 1; y >= 0; y--)
      {
        if (Get(x, section * SectionHeight + y, z) != Air)
        {
          return section * SectionHeight + y + 1;
        }
      }
    }

    return 0;
  }

  /// <summary>One more than the highest layer that holds anything (0 for an empty chunk).</summary>
  public int TopLayer()
  {
    for (int section = SectionCount - 1; section >= 0; section--)
    {
      if (IsEmptySection(section))
      {
        continue;
      }

      if (IsSolidSection(section))
      {
        return (section + 1) * SectionHeight;
      }

      for (int y = SectionHeight - 1; y >= 0; y--)
      {
        for (int z = 0; z < VoxelMath.ChunkSize; z++)
        {
          for (int x = 0; x < VoxelMath.ChunkSize; x++)
          {
            if (Get(x, section * SectionHeight + y, z) != Air)
            {
              return section * SectionHeight + y + 1;
            }
          }
        }
      }
    }

    return 0;
  }

  /// <summary>
  /// Stores every section in its cheapest form (call after generating): one block, a two-block mask, or an array. Arrays that
  /// turned out to hold one or two kinds of block shrink.
  /// </summary>
  public void Compact()
  {
    for (int section = 0; section < SectionCount; section++)
    {
      var array = full[section];

      if (array != null)
      {
        var first = array[0];
        var different = array.AsSpan().IndexOfAnyExcept(first);

        if (different < 0)
        {
          full[section] = null;
          fill[section] = first;
          continue;
        }

        var second = array[different];
        var onlyTwo = true;

        for (int i = different; i < array.Length; i++)
        {
          if (array[i] != first && array[i] != second)
          {
            onlyTwo = false;
            break;
          }
        }

        if (onlyTwo)
        {
          var mask = new byte[MaskBytes];

          for (int i = different; i < array.Length; i++)
          {
            if (array[i] == second)
            {
              mask[i >> 3] |= (byte)(1 << (i & 7));
            }
          }

          full[section] = null;
          masks[section] = mask;
          fill[section] = first;
          alt[section] = second;
        }
        else
        {
          Pack(section, array);
        }

        continue;
      }

      var bits = masks[section];

      if (bits != null && bits.AsSpan().IndexOfAnyExcept((byte)0) < 0)
      {
        masks[section] = null; // every bit is clear: one block again
      }
    }
  }

  /// <summary>Stores an array section as a palette (up to 16 kinds of block, 2 or 4 bits per voxel) when it has few enough kinds.</summary>
  void Pack(int section, byte[] array)
  {
    Span<byte> indexOf = stackalloc byte[256];
    Span<byte> kinds = stackalloc byte[16];
    var count = 0;

    indexOf.Fill(255);

    foreach (var id in array)
    {
      if (indexOf[id] != 255)
      {
        continue;
      }

      if (count == 16)
      {
        return; // too many kinds: stay a plain array
      }

      indexOf[id] = (byte)count;
      kinds[count++] = id;
    }

    var width = count <= 4 ? 2 : 4;
    var bits = new byte[SectionVolume * width / 8];

    for (int i = 0; i < array.Length; i++)
    {
      var at = i * width;

      bits[at >> 3] |= (byte)(indexOf[array[i]] << (at & 7));
    }

    palettes[section] = kinds[..count].ToArray();
    packed[section] = bits;
    full[section] = null;
  }

  byte[] ExpandToArray(int section)
  {
    var array = new byte[SectionVolume];
    var mask = masks[section];

    if (packed[section] != null)
    {
      for (int y = 0; y < SectionHeight; y++)
      {
        for (int z = 0; z < VoxelMath.ChunkSize; z++)
        {
          for (int x = 0; x < VoxelMath.ChunkSize; x++)
          {
            array[Local(x, y, z)] = Get(x, section * SectionHeight + y, z);
          }
        }
      }

      packed[section] = null;
      palettes[section] = null;
      full[section] = array;

      return array;
    }

    if (fill[section] != Air)
    {
      Array.Fill(array, fill[section]);
    }

    if (mask != null)
    {
      for (int i = 0; i < SectionVolume; i++)
      {
        if ((mask[i >> 3] & (1 << (i & 7))) != 0)
        {
          array[i] = alt[section];
        }
      }
    }

    masks[section] = null;
    full[section] = array;

    return array;
  }
}
