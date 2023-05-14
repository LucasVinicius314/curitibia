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

  public List<Block> blocks = new List<Block>();

  void BuildTerrain()
  {
    var seed = UnityEngine.Random.value * 5000f;

    var material = Materials.@default;

    material.mainTexture = Textures.atlas;

    for (int x = -size + 1; x < size; x++)
    {
      for (int z = -size + 1; z < size; z++)
      {
        var chunk = new GameObject("Chunk");

        var chunkScript = chunk.AddComponent<ChunkScript>();

        chunkScript.terrainScript = this;
        chunkScript.chunkCoordinate = (x, z);
        chunkScript.GenerateTerrainMap(seed, blocks);
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

  public void SetBlock(Vector3 position)
  {
    var tX = position.x + 16;
    var tZ = position.z + 16;

    var chunkX = Mathf.FloorToInt(tX / 32);
    var chunkZ = Mathf.FloorToInt(tZ / 32);

    var blockX = Mathf.FloorToInt(tX - chunkX * 32);
    var blockY = Mathf.FloorToInt(position.y + 32);
    var blockZ = Mathf.FloorToInt(tZ - chunkZ * 32);

    var chunk = chunkMap[(chunkX, chunkZ)];

    chunk.SetBlock(blockX, blockY, blockZ, blocks[0]);
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

    blocks = new List<Block>() {
      new Block(name: "Stone", texture: Textures.stone),
      new Block(name: "Grass", texture: Textures.grass),
    };
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
