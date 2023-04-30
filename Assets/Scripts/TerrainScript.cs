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

  bool[,,] terrainMap = {
    {
      { true, true, true, true, true, true, true },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
    },
    {
      { true, true, true, true, true, true, true },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
    },
    {
      { true, true, true, true, true, true, true },
      { false, false, false, false, true, false, false },
      { false, false, false, false, true, false, false },
      { false, false, false, true, true, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
    },
    {
      { true, true, true, true, true, true, true },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
    },
    {
      { true, true, true, true, true, true, true },
      { false, false, false, false, true, false, false },
      { false, false, false, false, true, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
    },
    {
      { true, true, true, true, true, true, true },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, true, false, false },
      { false, false, false, false, false, false, false },
    },
    {
      { true, true, true, true, true, true, true },
      { false, false, false, false, false, false, true },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
      { false, false, false, false, false, false, false },
    },
  };

  void Generate(Mesh mesh)
  {
    mesh.Clear();

    var offset = new Vector3 { x = -3.5f, y = -.5f, z = -3.5f };

    var right = terrainMap.GetLength(0);
    var up = terrainMap.GetLength(1);
    var forward = terrainMap.GetLength(2);

    var uv = new Vector2[6 * 6 * right * up * forward];
    var triangles = new int[6 * 6 * right * up * forward];
    var vertices = new Vector3[6 * 6 * right * up * forward];

    var i = 0;

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
              RenderQuad(vertices, triangles, uv, ref i, x, y, z, offset, Orientation.west);
            }
            if (x == right - 1 || !terrainMap[x + 1, y, z])
            {
              RenderQuad(vertices, triangles, uv, ref i, x, y, z, offset, Orientation.east);
            }
            if (y == 0 || !terrainMap[x, y - 1, z])
            {
              RenderQuad(vertices, triangles, uv, ref i, x, y, z, offset, Orientation.down);
            }
            if (y == up - 1 || !terrainMap[x, y + 1, z])
            {
              RenderQuad(vertices, triangles, uv, ref i, x, y, z, offset, Orientation.up);
            }
            if (z == 0 || !terrainMap[x, y, z - 1])
            {
              RenderQuad(vertices, triangles, uv, ref i, x, y, z, offset, Orientation.south);
            }
            if (z == forward - 1 || !terrainMap[x, y, z + 1])
            {
              RenderQuad(vertices, triangles, uv, ref i, x, y, z, offset, Orientation.north);
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

  void RenderQuad(Vector3[] vertices, int[] triangles, Vector2[] uv, ref int i, int x, int y, int z, Vector3 offset, Orientation orientation)
  {
    switch (orientation)
    {
      case Orientation.up:
        vertices[i] = new Vector3(x, y + 1, z) + offset;
        vertices[i + 1] = new Vector3(x, y + 1, z + 1) + offset;
        vertices[i + 2] = new Vector3(x + 1, y + 1, z) + offset;
        vertices[i + 3] = new Vector3(x + 1, y + 1, z) + offset;
        vertices[i + 4] = new Vector3(x, y + 1, z + 1) + offset;
        vertices[i + 5] = new Vector3(x + 1, y + 1, z + 1) + offset;
        break;
      case Orientation.down:
        vertices[i] = new Vector3(x, y, z) + offset;
        vertices[i + 1] = new Vector3(x + 1, y, z) + offset;
        vertices[i + 2] = new Vector3(x, y, z + 1) + offset;
        vertices[i + 3] = new Vector3(x, y, z + 1) + offset;
        vertices[i + 4] = new Vector3(x + 1, y, z) + offset;
        vertices[i + 5] = new Vector3(x + 1, y, z + 1) + offset;
        break;
      case Orientation.north:
        vertices[i] = new Vector3(x + 1, y, z + 1) + offset;
        vertices[i + 1] = new Vector3(x + 1, y + 1, z + 1) + offset;
        vertices[i + 2] = new Vector3(x, y, z + 1) + offset;
        vertices[i + 3] = new Vector3(x, y, z + 1) + offset;
        vertices[i + 4] = new Vector3(x + 1, y + 1, z + 1) + offset;
        vertices[i + 5] = new Vector3(x, y + 1, z + 1) + offset;
        break;
      case Orientation.south:
        vertices[i] = new Vector3(x, y, z) + offset;
        vertices[i + 1] = new Vector3(x, y + 1, z) + offset;
        vertices[i + 2] = new Vector3(x + 1, y, z) + offset;
        vertices[i + 3] = new Vector3(x + 1, y, z) + offset;
        vertices[i + 4] = new Vector3(x, y + 1, z) + offset;
        vertices[i + 5] = new Vector3(x + 1, y + 1, z) + offset;
        break;
      case Orientation.east:
        vertices[i] = new Vector3(x + 1, y, z) + offset;
        vertices[i + 1] = new Vector3(x + 1, y + 1, z) + offset;
        vertices[i + 2] = new Vector3(x + 1, y, z + 1) + offset;
        vertices[i + 3] = new Vector3(x + 1, y, z + 1) + offset;
        vertices[i + 4] = new Vector3(x + 1, y + 1, z) + offset;
        vertices[i + 5] = new Vector3(x + 1, y + 1, z + 1) + offset;
        break;
      case Orientation.west:
        vertices[i] = new Vector3(x, y, z) + offset;
        vertices[i + 1] = new Vector3(x, y, z + 1) + offset;
        vertices[i + 2] = new Vector3(x, y + 1, z) + offset;
        vertices[i + 3] = new Vector3(x, y + 1, z) + offset;
        vertices[i + 4] = new Vector3(x, y, z + 1) + offset;
        vertices[i + 5] = new Vector3(x, y + 1, z + 1) + offset;
        break;
    }

    uv[i] = new Vector2(0, 0);
    uv[i + 1] = new Vector2(0, 1);
    uv[i + 2] = new Vector2(1, 0);
    uv[i + 3] = new Vector2(1, 0);
    uv[i + 4] = new Vector2(0, 1);
    uv[i + 5] = new Vector2(1, 1);

    triangles[i] = i;
    triangles[i + 1] = i + 1;
    triangles[i + 2] = i + 2;
    triangles[i + 3] = i + 3;
    triangles[i + 4] = i + 4;
    triangles[i + 5] = i + 5;

    i += 6;
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
      Generate(mesh);
    }

    GetComponent<NavMeshSurface>().BuildNavMesh();
  }
}
