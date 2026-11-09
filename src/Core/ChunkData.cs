using System;

namespace Curitibia.Core;

/// <summary>
/// The voxels of one chunk column, stored in vertical sections of <see cref="SectionHeight"/> layers, each in the cheapest form
/// that holds it: one block all through (solid stone for hundreds of layers, open sky: no memory at all), a bitmask choosing
/// between two blocks (rock with caves in it: 2 KB), or a full array (the few sections near the surface, where there are many
/// kinds of block: 16 KB). A world a thousand voxels deep costs memory only where something varies. Reads outside the world are
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
        if (full[i] != null || masks[i] != null)
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
        total += full[i] != null ? SectionVolume : masks[i] != null ? MaskBytes : 0;
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
    fill[section] = id;
  }

  /// <summary>True when a section is one block all through (and which).</summary>
  public bool IsUniform(int section, out byte id)
  {
    id = fill[section];

    return full[section] == null && masks[section] == null;
  }

  public bool IsEmptySection(int section)
  {
    return full[section] == null && masks[section] == null && fill[section] == Air;
  }

  /// <summary>One block all through and not air.</summary>
  public bool IsSolidSection(int section)
  {
    return full[section] == null && masks[section] == null && fill[section] != Air;
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

        continue;
      }

      var bits = masks[section];

      if (bits != null && bits.AsSpan().IndexOfAnyExcept((byte)0) < 0)
      {
        masks[section] = null; // every bit is clear: one block again
      }
    }
  }

  byte[] ExpandToArray(int section)
  {
    var array = new byte[SectionVolume];
    var mask = masks[section];

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
