using Unity.AI.Navigation;
using UnityEngine;

#nullable enable

enum Orientation
{
  up,
  down,
  north,
  south,
  east,
  west,
}

[RequireComponent(typeof(MeshCollider), typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainScript : MonoBehaviour
{
  Mesh? mesh;
  MeshCollider? meshCollider;
  MeshFilter? meshFilter;

  void CreateMesh(Mesh mesh, bool[,,] terrainMap)
  {
    mesh.Clear();

    var offset = new Vector3 { x = -16f, y = -22.5f, z = -16f };

    var right = terrainMap.GetLength(0);
    var up = terrainMap.GetLength(1);
    var forward = terrainMap.GetLength(2);

    var uv = new Vector2[4 * 6 * right * up * forward];
    var triangles = new int[6 * 6 * right * up * forward];
    var vertices = new Vector3[4 * 6 * right * up * forward];

    var vc = 0;
    var tc = 0;

    for (int x = 0; x < right; x++)
    {
      for (int y = 0; y < up; y++)
      {
        for (int z = 0; z < forward; z++)
        {
          if (terrainMap[x, y, z])
          {
            if (x == 0 || !terrainMap[x - 1, y, z])
            {
              RenderQuad(vertices, triangles, uv, ref vc, ref tc, x, y, z, offset, Orientation.west);
            }
            if (x == right - 1 || !terrainMap[x + 1, y, z])
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
            if (z == 0 || !terrainMap[x, y, z - 1])
            {
              RenderQuad(vertices, triangles, uv, ref vc, ref tc, x, y, z, offset, Orientation.south);
            }
            if (z == forward - 1 || !terrainMap[x, y, z + 1])
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
    const int width = 32;
    const int height = 32;
    const int depth = 32;

    var terrainMap = new bool[width, height, depth];

    for (int x = 0; x < width; x++)
    {
      for (int z = 0; z < depth; z++)
      {
        var targetHeight = Mathf.PerlinNoise(x / 7f, z / 7f) * 8f + height / 2;

        for (int y = 0; y < targetHeight; y++)
        {
          terrainMap[x, y, z] = true;
        }
      }
    }

    return terrainMap;
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
  }

  void Start()
  {
    if (mesh != null)
    {
      var terrainMap = GenerateTerrainMap(new Vector3(32, 32, 32));

      CreateMesh(mesh, terrainMap);
    }

    GetComponent<NavMeshSurface>().BuildNavMesh();
  }
}
