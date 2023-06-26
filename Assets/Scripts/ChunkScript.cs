using System.Collections.Generic;
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
  public static int height = 32;
  public static int depth = 32;

  public (int, int) chunkCoordinate;

  Block?[,,]? terrainMap;

  Mesh? mesh;
  MeshCollider? meshCollider;
  MeshFilter? meshFilter;
  public TerrainScript? terrainScript;

  public System.Collections.IEnumerator CreateMesh()
  {
    if (terrainScript != null && terrainMap != null && mesh != null)
    {
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
            if (terrainMap[x, y, z] != null)
            {
              var textureOffset = Textures.GetTextureOffset(terrainMap[x, y, z]!.texture);

              var textureXUvStart = textureOffset.Item1;
              var textureXUvEnd = textureOffset.Item2;

              if (x == 0 ? westernChunk?.GetBlock(width - 1, y, z) == null : terrainMap[x - 1, y, z] == null)
              {
                RenderQuad(vertices, triangles, uv, ref vc, ref tc, x, y, z, offset, Orientation.west, textureXUvStart, textureXUvEnd);
              }
              if (x == right - 1 ? easternChunk?.GetBlock(0, y, z) == null : terrainMap[x + 1, y, z] == null)
              {
                RenderQuad(vertices, triangles, uv, ref vc, ref tc, x, y, z, offset, Orientation.east, textureXUvStart, textureXUvEnd);
              }
              if (y == 0 || terrainMap[x, y - 1, z] == null)
              {
                RenderQuad(vertices, triangles, uv, ref vc, ref tc, x, y, z, offset, Orientation.down, textureXUvStart, textureXUvEnd);
              }
              if (y == up - 1 || terrainMap[x, y + 1, z] == null)
              {
                RenderQuad(vertices, triangles, uv, ref vc, ref tc, x, y, z, offset, Orientation.up, textureXUvStart, textureXUvEnd);
              }
              if (z == 0 ? southernChunk?.GetBlock(x, y, depth - 1) == null : terrainMap[x, y, z - 1] == null)
              {
                RenderQuad(vertices, triangles, uv, ref vc, ref tc, x, y, z, offset, Orientation.south, textureXUvStart, textureXUvEnd);
              }
              if (z == forward - 1 ? northernChunk?.GetBlock(x, y, 0) == null : terrainMap[x, y, z + 1] == null)
              {
                RenderQuad(vertices, triangles, uv, ref vc, ref tc, x, y, z, offset, Orientation.north, textureXUvStart, textureXUvEnd);
              }
            }
          }
        }

        if (Time.deltaTime > 1f / 60f)
        {
          yield return null;
        }
      }

      var newUv = new Vector2[vc];

      for (int i = 0; i < vc; i++)
      {
        newUv[i] = uv[i];
      }

      var newTriangles = new int[tc];

      for (int i = 0; i < tc; i++)
      {
        newTriangles[i] = triangles[i];
      }

      var newVertices = new Vector3[vc];

      for (int i = 0; i < vc; i++)
      {
        newVertices[i] = vertices[i];
      }

      mesh.vertices = newVertices;
      mesh.triangles = newTriangles;
      mesh.uv = newUv;

      mesh.RecalculateNormals();

      if (meshCollider != null && meshFilter != null)
      {
        meshCollider.sharedMesh = mesh;
        meshFilter.mesh = mesh;
      }
    }
  }

  public void GenerateTerrainMap(float seed, List<Block> blocks)
  {
    var weight = .04f;
    var amplitude = 10f;

    var dimensions = new Vector3(width, height, depth);

    var newTerrainMap = new Block[width, height, depth];

    for (int x = 0; x < width; x++)
    {
      var tX = (x + chunkCoordinate.Item1 * width + seed) * weight;

      for (int z = 0; z < depth; z++)
      {
        var tZ = (z + chunkCoordinate.Item2 * depth + seed) * weight;

        var targetHeight = Mathf.PerlinNoise(tX, tZ) * amplitude + height / 2;

        for (int y = 0; y < targetHeight - 1; y++)
        {
          newTerrainMap[x, y, z] = blocks[0];
        }

        newTerrainMap[x, (int)targetHeight - 1, z] = blocks[1];
      }
    }

    terrainMap = newTerrainMap;
  }

  public Block? GetBlock(int x, int y, int z)
  {
    if (terrainMap == null)
    {
      return null;
    }

    return terrainMap[x, y, z];
  }

  void RenderQuad(Vector3[] vertices, int[] triangles, Vector2[] uv, ref int vc, ref int tc, int x, int y, int z, Vector3 offset, Orientation orientation, float textureXUvStart, float textureXUvEnd)
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

    uv[vc] = new Vector2(textureXUvStart, 0);
    uv[vc + 1] = new Vector2(textureXUvStart, 1);
    uv[vc + 2] = new Vector2(textureXUvEnd, 0);
    uv[vc + 3] = new Vector2(textureXUvEnd, 1);

    triangles[tc] = vc;
    triangles[tc + 1] = vc + 1;
    triangles[tc + 2] = vc + 2;
    triangles[tc + 3] = vc + 2;
    triangles[tc + 4] = vc + 1;
    triangles[tc + 5] = vc + 3;

    vc += 4;
    tc += 6;
  }

  public System.Collections.IEnumerator SetBlock(int x, int y, int z, Block? block)
  {
    if (terrainMap != null)
    {
      terrainMap[x, y, z] = block;

      yield return StartCoroutine(CreateMesh());
    }
  }

  void Awake()
  {
    mesh = new Mesh();

    meshCollider = GetComponent<MeshCollider>();
    meshFilter = GetComponent<MeshFilter>();
  }
}
