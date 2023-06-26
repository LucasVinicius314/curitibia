using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

#nullable enable

[RequireComponent(typeof(NavMeshSurface))]
public class TerrainScript : MonoBehaviour
{
  NavMeshSurface? navMeshSurface;

  Dictionary<(int, int), ChunkScript> chunkMap = new Dictionary<(int, int), ChunkScript>();

  public List<Block> blocks = new List<Block>();

  public static TerrainScript? instance;

  float seed = 0f;

  Material? chunkMaterial;

  public void SetBlock(Vector3 position, Block? block)
  {
    var tX = position.x + 16;
    var tZ = position.z + 16;

    var chunkX = Mathf.FloorToInt(tX / 32);
    var chunkZ = Mathf.FloorToInt(tZ / 32);

    var blockX = Mathf.FloorToInt(tX - chunkX * 32);
    var blockY = Mathf.FloorToInt(position.y + 32);
    var blockZ = Mathf.FloorToInt(tZ - chunkZ * 32);

    var chunk = chunkMap[(chunkX, chunkZ)];

    chunk.SetBlock(blockX, blockY, blockZ, block);
  }

  public ChunkScript? GetChunk((int, int) coordinates)
  {
    var tempX = coordinates.Item1;
    var tempZ = coordinates.Item2;

    return chunkMap.GetValueOrDefault((tempX, tempZ));
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

  public ChunkScript LoadChunk((int, int) key)
  {
    var chunk = new GameObject("Chunk");

    var chunkScript = chunk.AddComponent<ChunkScript>();

    chunkScript.terrainScript = this;
    chunkScript.chunkCoordinate = key;
    chunkScript.GenerateTerrainMap(seed, blocks);
    chunkMap.Add(key, chunkScript);

    chunk.GetComponent<MeshRenderer>().material = chunkMaterial;
    chunk.transform.SetParent(transform);
    chunk.transform.position = new Vector3
    {
      x = key.Item1 * ChunkScript.width,
      z = key.Item2 * ChunkScript.depth,
    };

    return chunkScript;
  }

  public System.Collections.IEnumerator LoadChunks((int, int) key, int distance)
  {
    for (int x = -distance + 1; x < distance; x++)
    {
      for (int z = -distance + 1; z < distance; z++)
      {
        var newKey = (key.Item1 + x, key.Item2 + z);

        if (!chunkMap.ContainsKey(newKey))
        {
          var chunk = LoadChunk(newKey);

          yield return StartCoroutine(chunk.CreateMesh());
        }
      }
    }

    // TODO: fix, fix navmesh lag
    // navMeshSurface?.BuildNavMesh();
  }

  public void UnloadChunk((int, int) key)
  {
    Destroy(chunkMap[key].gameObject);

    chunkMap.Remove(key);
  }

  public void UnloadChunks((int, int) source, int distance)
  {
    var keys = new (int, int)[chunkMap.Keys.Count];

    chunkMap.Keys.CopyTo(keys, 0);

    foreach (var key in keys)
    {
      var chunk = chunkMap[key];

      var isFarX = Mathf.Abs(key.Item1 - source.Item1) > distance;
      var isFarZ = Mathf.Abs(key.Item2 - source.Item2) > distance;

      if (isFarX || isFarZ)
      {
        UnloadChunk(key);
      }
    }
  }

  void Awake()
  {
    instance = this;

    chunkMaterial = Materials.@default;
    chunkMaterial.mainTexture = Textures.atlas;

    seed = UnityEngine.Random.value * 5000f;

    navMeshSurface = GetComponent<NavMeshSurface>();

    blocks = new List<Block>() {
      Block.stone,
      Block.grass,
    };
  }

  void Start()
  {
    StartCoroutine(LoadChunks((0, 0), 3));
  }
}
