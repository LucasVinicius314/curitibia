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
  public int[] Indices = Array.Empty<int>();

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

  /// <param name="blocks">The chunk's voxels (VoxelMath.Index layout).</param>
  /// <param name="neighbours">Neighbour chunk voxels indexed by NeighbourPlusX/MinusX/PlusZ/MinusZ; null entries count as solid.</param>
  /// <param name="tileByBlock">Atlas tile per block id (-1 = not drawn).</param>
  /// <param name="topTiles">Optional tile per block id for the top face (-1 or missing = the side tile).</param>
  /// <param name="bottomTiles">Same for the bottom face.</param>
  /// <param name="tileCount">Tiles in the atlas (laid out in one row).</param>
  /// <param name="uvInset">UV inset per tile edge (fraction of the whole atlas width) to avoid bleeding between tiles.</param>
  public static MeshData Build(byte[] blocks, byte[]?[] neighbours, int[] tileByBlock, int tileCount, float uvInset, int[]? topTiles = null, int[]? bottomTiles = null)
  {
    var positions = new List<float>(4096);
    var normals = new List<float>(4096);
    var uvs = new List<float>(4096);
    var indices = new List<int>(4096);

    const int size = VoxelMath.ChunkSize;
    // Most of a tall column is air: only walk up to the highest layer that has anything in it.
    var top = blocks.AsSpan().LastIndexOfAnyExcept(BlockIds.Air);

    if (top < 0)
    {
      return new MeshData();
    }

    var height = top / (size * size) + 1;

    for (int y = 0; y < height; y++)
    {
      for (int z = 0; z < size; z++)
      {
        for (int x = 0; x < size; x++)
        {
          var id = blocks[VoxelMath.Index(x, y, z)];

          if (id == BlockIds.Air)
          {
            continue;
          }

          var tile = id < tileByBlock.Length ? tileByBlock[id] : -1;

          if (tile < 0)
          {
            continue;
          }

          for (int f = 0; f < Faces.Length; f++)
          {
            var face = Faces[f];

            if (IsSolidAt(blocks, neighbours, x + face.Nx, y + face.Ny, z + face.Nz))
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

            var u0 = (float)faceTile / tileCount + uvInset;
            var u1 = (float)(faceTile + 1) / tileCount - uvInset;

            var baseVertex = positions.Count / 3;

            foreach (var (a, b) in Corners)
            {
              positions.Add(x + face.Ox + a * face.Ux + b * face.Vx);
              positions.Add(y + face.Oy + a * face.Uy + b * face.Vy);
              positions.Add(z + face.Oz + a * face.Uz + b * face.Vz);

              normals.Add(face.Nx);
              normals.Add(face.Ny);
              normals.Add(face.Nz);

              var s = face.SFromB ? b : a;
              var t = face.SFromB ? a : b;

              if (face.FlipT)
              {
                t = 1 - t;
              }

              uvs.Add(u0 + s * (u1 - u0));
              uvs.Add(t);
            }

            // clockwise triangles
            indices.Add(baseVertex);
            indices.Add(baseVertex + 2);
            indices.Add(baseVertex + 1);
            indices.Add(baseVertex);
            indices.Add(baseVertex + 3);
            indices.Add(baseVertex + 2);
          }
        }
      }
    }

    return new MeshData
    {
      Positions = positions.ToArray(),
      Normals = normals.ToArray(),
      Uvs = uvs.ToArray(),
      Indices = indices.ToArray(),
    };
  }

  static bool IsSolidAt(byte[] blocks, byte[]?[] neighbours, int x, int y, int z)
  {
    const int size = VoxelMath.ChunkSize;

    if (y < 0)
    {
      return true; // below the world: never draw bottom faces
    }

    if (y >= VoxelMath.ChunkHeight)
    {
      return false;
    }

    if (x >= 0 && x < size && z >= 0 && z < size)
    {
      return VoxelMath.Get(blocks, x, y, z) != BlockIds.Air;
    }

    byte[]? other;

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

    return VoxelMath.Get(other, x, y, z) != BlockIds.Air;
  }
}
