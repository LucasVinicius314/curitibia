using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

#nullable enable

[RequireComponent(typeof(NavMeshSurface))]
public class TerrainScript : MonoBehaviour
{
  [SerializeField]
  [Range(1, 32)]
  int size = 3;

  NavMeshSurface? navMeshSurface;

  Dictionary<(int, int), ChunkScript> chunkMap = new Dictionary<(int, int), ChunkScript>();

  void BuildTerrain()
  {
    var seed = UnityEngine.Random.value * 5000f;

    var material = (Material)Utils.LoadPrefabFromFile("Materials/GrassMat");

    for (int x = -size + 1; x < size; x++)
    {
      for (int z = -size + 1; z < size; z++)
      {
        var chunk = new GameObject("Chunk");

        var chunkScript = chunk.AddComponent<ChunkScript>();

        chunkScript.terrainScript = this;
        chunkScript.chunkCoordinate = (x, z);
        chunkScript.GenerateTerrainMap(seed);
        chunkMap.Add((x, z), chunkScript);

        chunk.GetComponent<MeshRenderer>().material = material;
        chunk.transform.SetParent(transform);
        chunk.transform.position = new Vector3
        {
          x = x * ChunkScript.width,
          z = z * ChunkScript.depth,
        };
      }
    }
  }

  public ChunkScript? GetNeighbourChunk((int, int) coordinates, Orientation orientation)
  {
    var tempX = coordinates.Item1;
    var tempZ = coordinates.Item2;

    switch (orientation)
    {
      case Orientation.up:
      case Orientation.down:
        throw new ArgumentException("Invalid orientation.");
      case Orientation.east:
        tempX++;
        break;
      case Orientation.west:
        tempX--;
        break;
      case Orientation.north:
        tempZ++;
        break;
      case Orientation.south:
        tempZ--;
        break;
    }

    return chunkMap.GetValueOrDefault((tempX, tempZ));
  }

  void Awake()
  {
    navMeshSurface = GetComponent<NavMeshSurface>();
  }

  void Start()
  {
    BuildTerrain();

    foreach (var chunk in chunkMap.Values)
    {
      chunk.CreateMesh();
    }

    navMeshSurface?.BuildNavMesh();
  }
}
