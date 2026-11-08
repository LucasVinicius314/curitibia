using System;
using Curitibia.Core;
using Curitibia.World;
using Godot;

namespace Curitibia.Enemies;

/// <summary>
/// A MultiplayerSpawner for enemies with a custom spawn function (every peer builds the enemy from the same data). The
/// host calls SpawnAround; clients receive the spawns.
/// </summary>
public partial class EnemySpawner : MultiplayerSpawner
{
  [Export] public PackedScene EnemyScene { get; set; } = null!;

  /// <summary>Enemies spawned in a ring around the spawn point when a world starts.</summary>
  [Export] public int Count { get; set; } = 3;

  int nextId;

  public override void _Ready()
  {
    SpawnFunction = Callable.From<Variant, Node>(Build);
  }

  /// <summary>Host only: a ring of enemies on the terrain surface around a voxel column.</summary>
  public void SpawnAround(int centerX, int centerZ)
  {
    var terrain = Terrain.Instance;

    if (terrain == null)
    {
      return;
    }

    for (int i = 0; i < Count; i++)
    {
      var angle = i * Mathf.Tau / Math.Max(1, Count) + 0.6f;
      var radius = 28 + i * 6; // voxels
      var wx = centerX + (int)(Mathf.Cos(angle) * radius);
      var wz = centerZ + (int)(Mathf.Sin(angle) * radius);
      var y = terrain.SurfaceHeightAt(wx, wz) + 1f;

      Spawn(new Godot.Collections.Dictionary
      {
        ["id"] = nextId++,
        ["pos"] = new Vector3(VoxelMath.VoxelCenter(wx), y, VoxelMath.VoxelCenter(wz)),
      });
    }
  }

  Node Build(Variant data)
  {
    var info = data.AsGodotDictionary();
    var enemy = EnemyScene.Instantiate<Enemy>();

    enemy.Name = $"Enemy_{info["id"].AsInt32()}";
    enemy.Position = info["pos"].AsVector3();
    enemy.SyncPosition = enemy.Position;

    return enemy;
  }
}
