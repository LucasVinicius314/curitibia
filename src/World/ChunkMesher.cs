using System;
using System.Collections.Generic;
using Curitibia.Core;

namespace Curitibia.World;

/// <summary>Flat arrays ready to be turned into an engine mesh. Engine-free.</summary>
public sealed class MeshData
{
  public float[] Positions = Array.Empty<float>();
  public float[] Normals = Array.Empty<float>();
  public float[] Uvs = Array.Empty<float>();

  /// <summary>Per-vertex colour (RGBA bytes, four per vertex) that tints grass and leaves by biome; empty when the mesh was built without tints.</summary>
  public byte[] Colors = Array.Empty<byte>();
  public int[] Indices = Array.Empty<int>();

  /// <summary>
  /// The solid faces for collision, merged into rectangles of coplanar faces regardless of block type: far fewer triangles than
  /// the visible mesh (three floats per vertex, three vertices per triangle, clockwise like the visible faces). Empty when the
  /// mesh was built without collision.
  /// </summary>
  public float[] CollisionTriangles = Array.Empty<float>();

  public int VertexCount => Positions.Length / 3;
  public int TriangleCount => Indices.Length / 3;
}

/// <summary>
/// Face-culled voxel mesher. Pure: bytes in, arrays out, so it runs on worker threads and is unit-tested. A missing
/// neighbour chunk counts as solid (no wall faces toward the unloaded frontier); the terrain only meshes a chunk once all
/// four neighbours have data, so that case is only the safety net.
/// </summary>
public static class ChunkMesher
{
  public const int NeighbourPlusX = 0;
  public const int NeighbourMinusX = 1;
  public const int NeighbourPlusZ = 2;
  public const int NeighbourMinusZ = 3;

  // Per face: normal, u axis, v axis, origin of the quad inside the voxel. u x v = normal, so corners (0,0) (1,0) (1,1)
  // (0,1) are counter-clockwise seen from outside; triangles are emitted clockwise because Godot's front faces are clockwise.
  // SFromB/FlipT choose how the quad maps onto the tile (side faces keep the texture upright).
  struct Face
  {
    public int Nx, Ny, Nz;
    public int Ux, Uy, Uz;
    public int Vx, Vy, Vz;
    public int Ox, Oy, Oz;
    public bool SFromB;
    public bool FlipT;
  }

  static readonly Face[] Faces =
  {
    // +X
    new Face { Nx = 1, Ux = 0, Uy = 1, Uz = 0, Vx = 0, Vy = 0, Vz = 1, Ox = 1, Oy = 0, Oz = 0, SFromB = true, FlipT = true },
    // -X
    new Face { Nx = -1, Ux = 0, Uy = 0, Uz = 1, Vx = 0, Vy = 1, Vz = 0, Ox = 0, Oy = 0, Oz = 0, SFromB = false, FlipT = false },
    // +Y
    new Face { Ny = 1, Ux = 0, Uy = 0, Uz = 1, Vx = 1, Vy = 0, Vz = 0, Ox = 0, Oy = 1, Oz = 0, SFromB = true, FlipT = false },
    // -Y
    new Face { Ny = -1, Ux = 1, Uy = 0, Uz = 0, Vx = 0, Vy = 0, Vz = 1, Ox = 0, Oy = 0, Oz = 0, SFromB = false, FlipT = false },
    // +Z
    new Face { Nz = 1, Ux = 1, Uy = 0, Uz = 0, Vx = 0, Vy = 1, Vz = 0, Ox = 0, Oy = 0, Oz = 1, SFromB = false, FlipT = true },
    // -Z
    new Face { Nz = -1, Ux = 0, Uy = 1, Uz = 0, Vx = 1, Vy = 0, Vz = 0, Ox = 0, Oy = 0, Oz = 0, SFromB = true, FlipT = true },
  };

  static readonly (int a, int b)[] Corners = { (0, 0), (1, 0), (1, 1), (0, 1) };

  /// <param name="blocks">The chunk's voxels.</param>
  /// <param name="neighbours">Neighbour chunks indexed by NeighbourPlusX/MinusX/PlusZ/MinusZ; null entries count as solid.</param>
  /// <param name="tileByBlock">Atlas tile per block id (-1 = not drawn).</param>
  /// <param name="topTiles">Optional tile per block id for the top face (-1 or missing = the side tile).</param>
  /// <param name="bottomTiles">Same for the bottom face.</param>
  /// <param name="tileCount">Tiles in the atlas (laid out in one row).</param>
  /// <param name="uvInset">UV inset per tile edge (fraction of the whole atlas width) to avoid bleeding between tiles.</param>
  /// <param name="floorLayer">Only layers from here up are meshed (a multiple of the section height); nothing is drawn across the cut. A deep world is only drawn where somebody can see.</param>
  /// <param name="collision">Also build the merged collision rectangles (<see cref="MeshData.CollisionTriangles"/>).</param>
  /// <param name="grassTint">RGB bytes (three per column, index x + z * 32) that multiply the colour of grass blocks and grass plants of this chunk (null = no tint).</param>
  /// <param name="foliageTint">The same for leaves.</param>
  /// <param name="groundHeights">The natural ground height of each column of this chunk (index x + z * 32). With it, leaves higher than <paramref name="canopyCollisionHeight"/> voxels above the ground add nothing to the collision (nobody can stand in a giant's canopy, and its triangles cost a lot to build); null = every solid face collides.</param>
  /// <param name="ceilingLayer">Only layers below this are meshed (a multiple of the section height, or the world height for no cut).</param>
  public static MeshData Build(ChunkData blocks, ChunkData?[] neighbours, int[] tileByBlock, int tileCount, float uvInset, int[]? topTiles = null, int[]? bottomTiles = null, int floorLayer = 0, int ceilingLayer = VoxelMath.ChunkHeight, bool collision = true, int chunkX = 0, int chunkZ = 0, byte[]? grassTint = null, byte[]? foliageTint = null, int[]? groundHeights = null, int canopyCollisionHeight = 26, bool canopyWoodCollision = true)
  {
    var positions = new List<float>(4096);
    var normals = new List<float>(4096);
    var uvs = new List<float>(4096);
    var indices = new List<int>(4096);
    var colors = new List<byte>(4096);
    var visualKeys = new List<long>[Faces.Length];
    var visualAttributes = new List<int>[Faces.Length];

    for (int i = 0; i < visualKeys.Length; i++)
    {
      visualKeys[i] = new List<long>();
      visualAttributes[i] = new List<int>();
    }

    // What makes two faces mergeable into one quad: the tile and the tint (quantised to steps of four, so a slowly changing tint
    // does not break a run of grass into single columns). The tile index travels in the alpha channel of the vertex colour, the
    // tint in its rgb; the shader repeats the tile across the merged quad.
    int VisualAttribute(byte id, int x, int z, int tile)
    {
      var tint = id is BlockIds.Grass or BlockIds.ShortGrass or BlockIds.TallGrass ? grassTint
        : id is BlockIds.Leaves or BlockIds.PineLeaves or BlockIds.BirchLeaves ? foliageTint
        : null;

      if (tint == null)
      {
        return tile | (255 << 8) | (255 << 16) | (255 << 24);
      }

      var i = (x + z * VoxelMath.ChunkSize) * 3;

      return tile | (Quantise(tint[i]) << 8) | (Quantise(tint[i + 1]) << 16) | (Quantise(tint[i + 2]) << 24);
    }

    void AddTuftColor(byte plant, int x, int z)
    {
      var attribute = VisualAttribute(plant, x, z, BlockIds.TuftTile);

      colors.Add((byte)(attribute >> 8)); colors.Add((byte)(attribute >> 16)); colors.Add((byte)(attribute >> 24)); colors.Add((byte)BlockIds.TuftTile);
    }

    void AddTuft(int x, int y, int z, int wx, int wz, bool tall)
    {
      var plant = tall ? BlockIds.TallGrass : BlockIds.ShortGrass;
      var h = TuftHash(wx, wz);
      var cx = x + 0.2f + (h & 0xff) / 255f * 0.6f;
      var cz = z + 0.2f + ((h >> 8) & 0xff) / 255f * 0.6f;
      var height = (0.55f + ((h >> 16) & 0xff) / 255f * 0.4f) * (tall ? 2.6f : 1f);

      // Two crossed vertical quads (the diagonals of the voxel), each drawn from both sides; the normal points up so they are
      // lit like the ground they stand on.
      for (int plane = 0; plane < 2; plane++)
      {
        var dx = 0.5f;
        var dz = plane == 0 ? 0.5f : -0.5f;

        for (int side = 0; side < 2; side++)
        {
          var baseVertex = positions.Count / 3;
          var ax = cx - dx; var az = cz - dz;
          var bx = cx + dx; var bz = cz + dz;

          if (side == 1)
          {
            (ax, bx) = (bx, ax);
            (az, bz) = (bz, az);
          }

          void Vertex(float vx, float vy, float vz, float s, float t)
          {
            positions.Add(vx); positions.Add(vy); positions.Add(vz);
            normals.Add(0f); normals.Add(1f); normals.Add(0f);
            uvs.Add(s); uvs.Add(t);
            AddTuftColor(plant, x, z);
          }

          Vertex(ax, y, az, 0f, 1f);
          Vertex(bx, y, bz, 1f, 1f);
          Vertex(bx, y + height, bz, 1f, 0f);
          Vertex(ax, y + height, az, 0f, 0f);

          indices.Add(baseVertex); indices.Add(baseVertex + 2); indices.Add(baseVertex + 1);
          indices.Add(baseVertex); indices.Add(baseVertex + 3); indices.Add(baseVertex + 2);
        }
      }
    }

    var faceKeys = new List<long>[Faces.Length];

    for (int i = 0; i < faceKeys.Length; i++)
    {
      faceKeys[i] = new List<long>();
    }

    const int size = VoxelMath.ChunkSize;
    // Most of a tall world is open sky or rock all through: walk only the sections that can show a face.
    var top = Math.Min(blocks.TopLayer(), ceilingLayer);

    if (top <= floorLayer)
    {
      return new MeshData();
    }

    var firstSection = floorLayer / ChunkData.SectionHeight;
    var lastSection = (top - 1) / ChunkData.SectionHeight;

    for (int section = firstSection; section <= lastSection; section++)
    {
      if (!MayShowFaces(blocks, neighbours, section, firstSection, lastSection, ceilingLayer >= VoxelMath.ChunkHeight))
      {
        continue;
      }

      var yEnd = (section + 1) * ChunkData.SectionHeight;

      for (int y = section * ChunkData.SectionHeight; y < yEnd; y++)
      {
      for (int z = 0; z < size; z++)
      {
        for (int x = 0; x < size; x++)
        {
          var id = blocks.Get(x, y, z);

          if (id == BlockIds.Air)
          {
            continue;
          }

          var tile = id < tileByBlock.Length ? tileByBlock[id] : -1;

          if (tile < 0)
          {
            continue;
          }

          if (BlockIds.IsPlant(id))
          {
            AddTuft(x, y, z, chunkX * size + x, chunkZ * size + z, id == BlockIds.TallGrass);
            continue;
          }

          for (int f = 0; f < Faces.Length; f++)
          {
            var face = Faces[f];

            if (IsSolidAt(blocks, neighbours, floorLayer, ceilingLayer, x + face.Nx, y + face.Ny, z + face.Nz))
            {
              continue;
            }

            var faceTile = tile;

            if (f == 2 && topTiles != null && id < topTiles.Length && topTiles[id] >= 0)
            {
              faceTile = topTiles[id];
            }
            else if (f == 3 && bottomTiles != null && id < bottomTiles.Length && bottomTiles[id] >= 0)
            {
              faceTile = bottomTiles[id];
            }

            visualKeys[f].Add(FaceKey(f, x, y, z));
            visualAttributes[f].Add(VisualAttribute(id, x, z, faceTile));

            if (collision && !(groundHeights != null && y > groundHeights[x + z * size] + canopyCollisionHeight
              && (id is BlockIds.Leaves or BlockIds.PineLeaves or BlockIds.BirchLeaves || (!canopyWoodCollision && id is BlockIds.Wood or BlockIds.BirchWood))))
            {
              faceKeys[f].Add(FaceKey(f, x, y, z));
            }
          }
        }
      }
      }
    }

    var collisionTriangles = collision ? MergeCollision(faceKeys) : Array.Empty<float>();

    // Where the window cuts through open space (a cave that goes on beyond it), close it with a face of rock, so you never see
    // through the cut into nothing.
    if (floorLayer > 0 || ceilingLayer < VoxelMath.ChunkHeight)
    {
      var voidTile = tileCount - 1; // near black: a cut through open space reads as dark depth, not as a stone wall

      for (int z = 0; z < size; z++)
      {
        for (int x = 0; x < size; x++)
        {
          if (floorLayer > 0 && floorLayer < top && blocks.Get(x, floorLayer, z) == BlockIds.Air)
          {
            visualKeys[2].Add(FaceKey(2, x, floorLayer - 1, z)); // the top of the rock below the cut
            visualAttributes[2].Add(voidTile | (255 << 8) | (255 << 16) | (255 << 24));
          }

          if (ceilingLayer < VoxelMath.ChunkHeight && ceilingLayer > floorLayer && blocks.Get(x, ceilingLayer - 1, z) == BlockIds.Air)
          {
            visualKeys[3].Add(FaceKey(3, x, ceilingLayer, z)); // the underside of the rock above the cut
            visualAttributes[3].Add(voidTile | (255 << 8) | (255 << 16) | (255 << 24));
          }
        }
      }
    }

    MergeVisual(visualKeys, visualAttributes, positions, normals, uvs, colors, indices);

    return new MeshData
    {
      Positions = positions.ToArray(),
      Normals = normals.ToArray(),
      Uvs = uvs.ToArray(),
      Colors = colors.ToArray(),
      Indices = indices.ToArray(),
      CollisionTriangles = collisionTriangles,
    };
  }

  // Which axis (0 = x, 1 = y, 2 = z) each face's normal, u and v run along.
  static readonly int[] NormalAxis = AxisOf(f => f.Nx != 0 ? 0 : f.Ny != 0 ? 1 : 2);
  static readonly int[] UAxis = AxisOf(f => f.Ux != 0 ? 0 : f.Uy != 0 ? 1 : 2);
  static readonly int[] VAxis = AxisOf(f => f.Vx != 0 ? 0 : f.Vy != 0 ? 1 : 2);

  static int[] AxisOf(Func<Face, int> pick)
  {
    var axes = new int[Faces.Length];

    for (int i = 0; i < axes.Length; i++)
    {
      axes[i] = pick(Faces[i]);
    }

    return axes;
  }

  static uint TuftHash(int x, int z)
  {
    var h = (uint)x * 73856093u ^ (uint)z * 19349663u ^ 0x9e3779b9u;

    h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;

    return h;
  }

  static int Coordinate(int axis, int x, int y, int z)
  {
    return axis == 0 ? x : axis == 1 ? y : z;
  }

  /// <summary>Packs a face's (slice along its normal, v, u) so sorting groups the faces of a plane and orders them row by row.</summary>
  static long FaceKey(int f, int x, int y, int z)
  {
    return ((long)Coordinate(NormalAxis[f], x, y, z) << 40) | ((long)Coordinate(VAxis[f], x, y, z) << 20) | (long)Coordinate(UAxis[f], x, y, z);
  }

  [ThreadStatic] static bool[]? grid;

  /// <summary>
  /// Greedy-merges the exposed faces of each plane into rectangles. Faces are merged whatever block they belong to, since
  /// collision does not care about textures. Returns triangles with the same winding as the visible faces.
  /// </summary>
  static float[] MergeCollision(List<long>[] faceKeys)
  {
    const int rowWidth = VoxelMath.ChunkHeight;
    var cells = grid ??= new bool[rowWidth * rowWidth];
    var output = new List<float>(4096);

    for (int f = 0; f < faceKeys.Length; f++)
    {
      var keys = faceKeys[f];

      if (keys.Count == 0)
      {
        continue;
      }

      keys.Sort();
      var start = 0;

      while (start < keys.Count)
      {
        var slice = (int)(keys[start] >> 40);
        var end = start;
        int uMin = int.MaxValue, uMax = 0, vMin = int.MaxValue, vMax = 0;

        while (end < keys.Count && (int)(keys[end] >> 40) == slice)
        {
          var u = (int)(keys[end] & 0xfffff);
          var v = (int)((keys[end] >> 20) & 0xfffff);

          cells[v * rowWidth + u] = true;
          uMin = Math.Min(uMin, u);
          uMax = Math.Max(uMax, u);
          vMin = Math.Min(vMin, v);
          vMax = Math.Max(vMax, v);
          end++;
        }

        for (int v = vMin; v <= vMax; v++)
        {
          for (int u = uMin; u <= uMax; u++)
          {
            if (!cells[v * rowWidth + u])
            {
              continue;
            }

            var w = 1;

            while (u + w <= uMax && cells[v * rowWidth + u + w])
            {
              w++;
            }

            var h = 1;

            while (v + h <= vMax && RowIsSolid(cells, (v + h) * rowWidth + u, w))
            {
              h++;
            }

            for (int dv = 0; dv < h; dv++)
            {
              Array.Clear(cells, (v + dv) * rowWidth + u, w);
            }

            EmitRect(output, f, slice, u, v, w, h);
          }
        }

        start = end;
      }
    }

    return output.ToArray();
  }

  static int Quantise(byte channel) => Math.Min(255, (channel + 2) & ~3);

  [ThreadStatic] static int[]? visualGrid;
  [ThreadStatic] static bool[]? visualUsed;

  /// <summary>
  /// Greedy-merges the visible faces of each plane into big quads: neighbouring faces with the same tile and tint become one rectangle
  /// whose texture coordinates run from 0 to its size in tiles (the shader repeats the tile), so a flat field of grass or the top of a
  /// canopy is a handful of quads instead of one per block.
  /// </summary>
  static void MergeVisual(List<long>[] faceKeys, List<int>[] faceAttributes, List<float> positions, List<float> normals, List<float> uvs, List<byte> colors, List<int> indices)
  {
    const int rowWidth = VoxelMath.ChunkHeight;
    var attributes = visualGrid ??= new int[rowWidth * rowWidth];
    var used = visualUsed ??= new bool[rowWidth * rowWidth];

    for (int f = 0; f < faceKeys.Length; f++)
    {
      if (faceKeys[f].Count == 0)
      {
        continue;
      }

      // Sorted by plane, then row, then column (the attributes follow their keys); sorting two plain arrays is far cheaper than
      // sorting a list of pairs with a comparison delegate.
      var keys = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(faceKeys[f]);
      var items = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(faceAttributes[f]);

      keys.Sort(items);

      var start = 0;

      while (start < keys.Length)
      {
        var slice = (int)(keys[start] >> 40);
        var end = start;
        int uMin = int.MaxValue, uMax = 0, vMin = int.MaxValue, vMax = 0;

        while (end < keys.Length && (int)(keys[end] >> 40) == slice)
        {
          var u = (int)(keys[end] & 0xfffff);
          var v = (int)((keys[end] >> 20) & 0xfffff);

          attributes[v * rowWidth + u] = items[end];
          used[v * rowWidth + u] = true;
          uMin = Math.Min(uMin, u);
          uMax = Math.Max(uMax, u);
          vMin = Math.Min(vMin, v);
          vMax = Math.Max(vMax, v);
          end++;
        }

        for (int v = vMin; v <= vMax; v++)
        {
          for (int u = uMin; u <= uMax; u++)
          {
            var at = v * rowWidth + u;

            if (!used[at])
            {
              continue;
            }

            var attribute = attributes[at];
            var w = 1;

            while (u + w <= uMax && used[at + w] && attributes[at + w] == attribute)
            {
              w++;
            }

            var h = 1;

            while (v + h <= vMax && RowMatches(attributes, used, (v + h) * rowWidth + u, w, attribute))
            {
              h++;
            }

            for (int dv = 0; dv < h; dv++)
            {
              Array.Clear(used, (v + dv) * rowWidth + u, w);
            }

            EmitVisualQuad(positions, normals, uvs, colors, indices, f, slice, u, v, w, h, attribute);
          }
        }

        start = end;
      }
    }
  }

  static bool RowMatches(int[] attributes, bool[] used, int from, int width, int attribute)
  {
    for (int i = 0; i < width; i++)
    {
      if (!used[from + i] || attributes[from + i] != attribute)
      {
        return false;
      }
    }

    return true;
  }

  static void EmitVisualQuad(List<float> positions, List<float> normals, List<float> uvs, List<byte> colors, List<int> indices, int f, int slice, int u, int v, int w, int h, int attribute)
  {
    var face = Faces[f];
    Span<float> origin = stackalloc float[3];

    origin[NormalAxis[f]] = slice + (NormalAxis[f] == 0 ? face.Ox : NormalAxis[f] == 1 ? face.Oy : face.Oz);
    origin[UAxis[f]] = u;
    origin[VAxis[f]] = v;

    var baseVertex = positions.Count / 3;
    var tile = (byte)(attribute & 0xff);

    foreach (var (a, b) in Corners)
    {
      for (int axis = 0; axis < 3; axis++)
      {
        positions.Add(origin[axis] + (UAxis[f] == axis ? a * w : 0) + (VAxis[f] == axis ? b * h : 0));
      }

      normals.Add(face.Nx);
      normals.Add(face.Ny);
      normals.Add(face.Nz);

      // Texture coordinates in tiles: they run 0 to w and 0 to h (or the other way round for the faces whose texture is turned).
      float s = face.SFromB ? b * h : a * w;
      float t = face.SFromB ? a * w : b * h;

      if (face.FlipT)
      {
        t = (face.SFromB ? w : h) - t;
      }

      uvs.Add(s);
      uvs.Add(t);

      colors.Add((byte)(attribute >> 8));
      colors.Add((byte)(attribute >> 16));
      colors.Add((byte)(attribute >> 24));
      colors.Add(tile);
    }

    // clockwise triangles
    indices.Add(baseVertex);
    indices.Add(baseVertex + 2);
    indices.Add(baseVertex + 1);
    indices.Add(baseVertex);
    indices.Add(baseVertex + 3);
    indices.Add(baseVertex + 2);
  }

  static bool RowIsSolid(bool[] cells, int from, int width)
  {
    for (int i = 0; i < width; i++)
    {
      if (!cells[from + i])
      {
        return false;
      }
    }

    return true;
  }

  static void EmitRect(List<float> output, int f, int slice, int u, int v, int w, int h)
  {
    var face = Faces[f];
    Span<float> origin = stackalloc float[3];

    origin[NormalAxis[f]] = slice + (NormalAxis[f] == 0 ? face.Ox : NormalAxis[f] == 1 ? face.Oy : face.Oz);
    origin[UAxis[f]] = u;
    origin[VAxis[f]] = v;

    Span<float> c = stackalloc float[12];

    for (int corner = 0; corner < 4; corner++)
    {
      var (a, b) = Corners[corner];

      for (int axis = 0; axis < 3; axis++)
      {
        c[corner * 3 + axis] = origin[axis] + (UAxis[f] == axis ? a * w : 0) + (VAxis[f] == axis ? b * h : 0);
      }
    }

    // clockwise: (0, 2, 1) and (0, 3, 2), like the visible faces
    foreach (var corner in new[] { 0, 2, 1, 0, 3, 2 })
    {
      output.Add(c[corner * 3]);
      output.Add(c[corner * 3 + 1]);
      output.Add(c[corner * 3 + 2]);
    }
  }

  /// <summary>
  /// A section of rock all through shows a face only where something next to it is open: the section above or below, or the
  /// same section of a neighbouring chunk. Open sky shows nothing. A section with its own array has to be walked.
  /// </summary>
  static bool MayShowFaces(ChunkData blocks, ChunkData?[] neighbours, int section, int firstSection, int lastSection, bool openAbove)
  {
    if (blocks.IsEmptySection(section))
    {
      return false;
    }

    if (!blocks.IsSolidSection(section))
    {
      return true;
    }

    if (section + 1 < ChunkData.SectionCount && (section < lastSection || openAbove) && !blocks.IsSolidSection(section + 1))
    {
      return true;
    }

    if (section > firstSection && !blocks.IsSolidSection(section - 1))
    {
      return true;
    }

    foreach (var other in neighbours)
    {
      if (other != null && !other.IsSolidSection(section))
      {
        return true;
      }
    }

    return false;
  }

  static bool IsSolidAt(ChunkData blocks, ChunkData?[] neighbours, int floorLayer, int ceilingLayer, int x, int y, int z)
  {
    const int size = VoxelMath.ChunkSize;

    if (y < floorLayer)
    {
      return true; // below the cut (or the bottom of the world): never draw faces there
    }

    if (y >= ceilingLayer)
    {
      return ceilingLayer < VoxelMath.ChunkHeight; // a cut in the middle of the rock draws nothing; the real sky is open
    }

    if (x >= 0 && x < size && z >= 0 && z < size)
    {
      return BlockIds.IsSolid(blocks.Get(x, y, z));
    }

    ChunkData? other;

    if (x < 0)
    {
      other = neighbours[NeighbourMinusX];
      x += size;
    }
    else if (x >= size)
    {
      other = neighbours[NeighbourPlusX];
      x -= size;
    }
    else if (z < 0)
    {
      other = neighbours[NeighbourMinusZ];
      z += size;
    }
    else
    {
      other = neighbours[NeighbourPlusZ];
      z -= size;
    }

    if (other == null)
    {
      return true;
    }

    return BlockIds.IsSolid(other.Get(x, y, z));
  }
}
