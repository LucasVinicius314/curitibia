using UnityEngine;

#nullable enable

public enum Orientation
{
  up,
  down,
  north,
  south,
  east,
  west,
}

[RequireComponent(typeof(MeshCollider), typeof(MeshFilter), typeof(MeshRenderer))]
public class ChunkScript : MonoBehaviour
{
  public static int width = 32;
  public static int height = 8;
  public static int depth = 32;

  public (int, int) chunkCoordinate;

  bool[,,]? terrainMap;

  Mesh? mesh;
  MeshCollider? meshCollider;
  MeshFilter? meshFilter;
  public TerrainScript? terrainScript;

  public void CreateMesh()
  {
    if (terrainScript == null || terrainMap == null || mesh == null)
    {
      return;
    }

    mesh.Clear();

    var offset = new Vector3 { x = -width / 2, y = -height, z = -depth / 2 };

    var right = terrainMap.GetLength(0);
    var up = terrainMap.GetLength(1);
    var forward = terrainMap.GetLength(2);

    var uv = new Vector2[4 * 6 * right * up * forward];
    var triangles = new int[6 * 6 * right * up * forward];
    var vertices = new Vector3[4 * 6 * right * up * forward];

    var vc = 0;
    var tc = 0;

    var northernChunk = terrainScript.GetNeighbourChunk(chunkCoordinate, Orientation.north);
    var southernChunk = terrainScript.GetNeighbourChunk(chunkCoordinate, Orientation.south);
    var easternChunk = terrainScript.GetNeighbourChunk(chunkCoordinate, Orientation.east);
    var westernChunk = terrainScript.GetNeighbourChunk(chunkCoordinate, Orientation.west);

    for (int x = 0; x < right; x++)
    {
      for (int y = 0; y < up; y++)
      {
        for (int z = 0; z < forward; z++)
        {
          if (terrainMap[x, y, z])
          {
            if (x == 0 ? (westernChunk?.HasBlock(width - 1, y, z) ?? false) == false : !terrainMap[x - 1, y, z])
            {
              RenderQuad(vertices, triangles, uv, ref vc, ref tc, x, y, z, offset, Orientation.west);
            }
            if (x == right - 1 ? (easternChunk?.HasBlock(0, y, z) ?? false) == false : !terrainMap[x + 1, y, z])
            {
              RenderQuad(vertices, triangles, uv, ref vc, ref tc, x, y, z, offset, Orientation.east);
            }
            if (y == 0 || !terrainMap[x, y - 1, z])
            {
              RenderQuad(vertices, triangles, uv, ref vc, ref tc, x, y, z, offset, Orientation.down);
            }
            if (y == up - 1 || !terrainMap[x, y + 1, z])
            {
              RenderQuad(vertices, triangles, uv, ref vc, ref tc, x, y, z, offset, Orientation.up);
            }
            if (z == 0 ? (southernChunk?.HasBlock(x, y, depth - 1) ?? false) == false : !terrainMap[x, y, z - 1])
            {
              RenderQuad(vertices, triangles, uv, ref vc, ref tc, x, y, z, offset, Orientation.south);
            }
            if (z == forward - 1 ? (northernChunk?.HasBlock(x, y, 0) ?? false) == false : !terrainMap[x, y, z + 1])
            {
              RenderQuad(vertices, triangles, uv, ref vc, ref tc, x, y, z, offset, Orientation.north);
            }
          }
        }
      }
    }

    mesh.vertices = vertices;
    mesh.triangles = triangles;
    mesh.uv = uv;

    mesh.RecalculateNormals();

    if (meshCollider != null && meshFilter != null)
    {
      meshCollider.sharedMesh = mesh;
      meshFilter.mesh = mesh;
    }
  }

  bool[,,] GenerateTerrainMap(Vector3 dimensions)
  {
    var terrainMap = new bool[width, height, depth];

    for (int x = 0; x < width; x++)
    {
      for (int z = 0; z < depth; z++)
      {
        var targetHeight = Mathf.PerlinNoise(x / 7f, z / 7f) * 4f + height / 2;

        for (int y = 0; y < targetHeight; y++)
        {
          terrainMap[x, y, z] = true;
        }
      }
    }

    return terrainMap;
  }

  public bool HasBlock(int x, int y, int z)
  {
    if (terrainMap == null)
    {
      return false;
    }

    return terrainMap[x, y, z];
  }

  void RenderQuad(Vector3[] vertices, int[] triangles, Vector2[] uv, ref int vc, ref int tc, int x, int y, int z, Vector3 offset, Orientation orientation)
  {
    switch (orientation)
    {
      case Orientation.up:
        vertices[vc] = new Vector3(x, y + 1, z) + offset;
        vertices[vc + 1] = new Vector3(x, y + 1, z + 1) + offset;
        vertices[vc + 2] = new Vector3(x + 1, y + 1, z) + offset;
        vertices[vc + 3] = new Vector3(x + 1, y + 1, z + 1) + offset;
        break;
      case Orientation.down:
        vertices[vc] = new Vector3(x, y, z) + offset;
        vertices[vc + 1] = new Vector3(x + 1, y, z) + offset;
        vertices[vc + 2] = new Vector3(x, y, z + 1) + offset;
        vertices[vc + 3] = new Vector3(x + 1, y, z + 1) + offset;
        break;
      case Orientation.north:
        vertices[vc] = new Vector3(x + 1, y, z + 1) + offset;
        vertices[vc + 1] = new Vector3(x + 1, y + 1, z + 1) + offset;
        vertices[vc + 2] = new Vector3(x, y, z + 1) + offset;
        vertices[vc + 3] = new Vector3(x, y + 1, z + 1) + offset;
        break;
      case Orientation.south:
        vertices[vc] = new Vector3(x, y, z) + offset;
        vertices[vc + 1] = new Vector3(x, y + 1, z) + offset;
        vertices[vc + 2] = new Vector3(x + 1, y, z) + offset;
        vertices[vc + 3] = new Vector3(x + 1, y + 1, z) + offset;
        break;
      case Orientation.east:
        vertices[vc] = new Vector3(x + 1, y, z) + offset;
        vertices[vc + 1] = new Vector3(x + 1, y + 1, z) + offset;
        vertices[vc + 2] = new Vector3(x + 1, y, z + 1) + offset;
        vertices[vc + 3] = new Vector3(x + 1, y + 1, z + 1) + offset;
        break;
      case Orientation.west:
        vertices[vc] = new Vector3(x, y, z) + offset;
        vertices[vc + 1] = new Vector3(x, y, z + 1) + offset;
        vertices[vc + 2] = new Vector3(x, y + 1, z) + offset;
        vertices[vc + 3] = new Vector3(x, y + 1, z + 1) + offset;
        break;
    }

    uv[vc] = new Vector2(0, 0);
    uv[vc + 1] = new Vector2(0, 1);
    uv[vc + 2] = new Vector2(1, 0);
    uv[vc + 3] = new Vector2(1, 1);

    triangles[tc] = vc;
    triangles[tc + 1] = vc + 1;
    triangles[tc + 2] = vc + 2;
    triangles[tc + 3] = vc + 2;
    triangles[tc + 4] = vc + 1;
    triangles[tc + 5] = vc + 3;

    vc += 4;
    tc += 6;
  }

  void Awake()
  {
    mesh = new Mesh();

    meshCollider = GetComponent<MeshCollider>();
    meshFilter = GetComponent<MeshFilter>();

    terrainMap = GenerateTerrainMap(new Vector3(width, height, depth));
  }
}
