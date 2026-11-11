using System;
using Curitibia.Core;
using Curitibia.Player;
using Curitibia.World;
using Godot;

namespace Curitibia.Enemies;

/// <summary>
/// A MultiplayerSpawner for enemies with a custom spawn function (every peer builds the enemy from the same data), plus the
/// natural spawn and despawn rules, which run on the host only: every second or so it tops enemies up around each player, up
/// to a cap that is higher at night, on the surface at a random spot out of immediate reach, and removes enemies that have been
/// left far behind (or that the day leaves over the cap) so the world never fills up with idle ones. Enemies killed by players
/// are removed by Enemy itself.
/// </summary>
public partial class EnemySpawner : MultiplayerSpawner
{
  [Export] public PackedScene EnemyScene { get; set; } = null!;

  /// <summary>Natural spawning on (off with --no-enemies).</summary>
  public bool Enabled { get; set; } = true;

  /// <summary>Enemies allowed near each player by day and by night.</summary>
  const int DayCapPerPlayer = 2;
  const int NightCapPerPlayer = 7;

  /// <summary>Spawn ring around a player, in metres: never closer than Min (so nothing appears in their face), never past Max.</summary>
  const float MinSpawnDistance = 18f;
  const float MaxSpawnDistance = 42f;

  /// <summary>An enemy farther than this from every player is despawned after DespawnDelay seconds; one in the open nearer than this stays.</summary>
  const float DespawnDistance = 75f;
  const float DespawnDelay = 8f;

  /// <summary>Seconds between spawn attempts, and the spread of the attempts.</summary>
  const float SpawnInterval = 2.5f;

  /// <summary>A player deeper than this under the ground gets no surface spawns (the caves have none yet).</summary>
  const float MaxPlayerDepth = 12f;

  readonly RandomNumberGenerator random = new();
  float spawnTimer = 4f;
  int nextId;

  public override void _Ready()
  {
    SpawnFunction = Callable.From<Variant, Node>(Build);
    random.Randomize();
  }

  public override void _Process(double delta)
  {
    var terrain = Terrain.Instance;

    if (!Enabled || terrain == null || Multiplayer.MultiplayerPeer == null || !Multiplayer.IsServer())
    {
      return;
    }

    Despawn(delta);

    spawnTimer -= (float)delta;

    if (spawnTimer > 0f || PlayerController.All.Count == 0)
    {
      return;
    }

    spawnTimer = SpawnInterval * random.RandfRange(0.7f, 1.3f);

    // One attempt per tick, for a random player: more players means more tries, not a burst.
    var player = PlayerController.All[random.RandiRange(0, PlayerController.All.Count - 1)];

    TrySpawnNear(terrain, player);
  }

  void TrySpawnNear(Terrain terrain, PlayerController player)
  {
    if (terrain.DepthBelowGround(player.GlobalPosition) > MaxPlayerDepth)
    {
      return;
    }

    var cap = DayNight.IsNight ? NightCapPerPlayer : DayCapPerPlayer;
    var near = 0;

    foreach (var enemy in Enemy.All)
    {
      if (enemy.GlobalPosition.DistanceTo(player.GlobalPosition) < DespawnDistance)
      {
        near++;
      }
    }

    if (near >= cap)
    {
      return;
    }

    var angle = random.Randf() * Mathf.Tau;
    var distance = random.RandfRange(MinSpawnDistance, MaxSpawnDistance);
    var wx = VoxelMath.WorldToVoxel(player.GlobalPosition.X + MathF.Cos(angle) * distance);
    var wz = VoxelMath.WorldToVoxel(player.GlobalPosition.Z + MathF.Sin(angle) * distance);
    // Not on top of a giant tree (its canopy is tens of metres above anyone who could be in reach).
    if (terrain.SurfaceHeightAt(wx, wz) - terrain.GroundHeightAt(wx, wz) > 12f)
    {
      return;
    }

    var position = new Vector3(VoxelMath.VoxelCenter(wx), terrain.SurfaceHeightAt(wx, wz) + 1f, VoxelMath.VoxelCenter(wz));

    // The ground must exist there (its chunk is meshed with collision) and no player may be able to see the spot appear.
    if (!terrain.HasCollisionAt(position + Vector3.Down * 0.6f) || SeenByAnyPlayer(position))
    {
      return;
    }

    Spawn(new Godot.Collections.Dictionary
    {
      ["id"] = nextId++,
      ["pos"] = position,
    });
  }

  /// <summary>True when a player is looking toward the point from within sight range (so a spawn would pop in front of them).</summary>
  static bool SeenByAnyPlayer(Vector3 point)
  {
    foreach (var player in PlayerController.All)
    {
      var offset = point - player.EyePosition;

      if (offset.Length() > 30f)
      {
        continue;
      }

      var forward = -player.CameraPivot.GlobalTransform.Basis.Z;

      if (forward.Dot(offset.Normalized()) > 0.5f)
      {
        return true;
      }
    }

    return false;
  }

  void Despawn(double delta)
  {
    var cap = (DayNight.IsNight ? NightCapPerPlayer : DayCapPerPlayer) * Math.Max(1, PlayerController.All.Count);
    var over = Enemy.All.Count - cap;

    foreach (var enemy in Enemy.All.ToArray())
    {
      var nearest = float.MaxValue;

      foreach (var player in PlayerController.All)
      {
        nearest = Math.Min(nearest, enemy.GlobalPosition.DistanceTo(player.GlobalPosition));
      }

      // Left behind (or the world is over its cap, as at dawn): it goes once it has been out of everyone's way for a while.
      var surplus = over > 0 && nearest > MaxSpawnDistance;

      if (nearest > DespawnDistance || surplus)
      {
        enemy.IdleSeconds += (float)delta;

        if (enemy.IdleSeconds >= DespawnDelay && !enemy.HasTarget)
        {
          enemy.QueueFree(); // the spawner removes it on every peer
          over--;
        }
      }
      else
      {
        enemy.IdleSeconds = 0f;
      }
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
