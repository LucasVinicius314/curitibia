using System;
using System.Collections.Generic;
using Curitibia.Core;
using Curitibia.Player;
using Curitibia.World;
using Godot;

namespace Curitibia.Items;

/// <summary>
/// Dropped items. A MultiplayerSpawner with a custom spawn function (every peer builds the drop from the same data and simulates
/// it the same way), and the host-side rules around it:
/// the host spawns a drop when a block breaks, or when a player asks to throw an item (Q); a player whose hotbar has room for a
/// drop next to them asks the host for it, and the host (which alone frees the drop) hands it over, so two players cannot both
/// get one item.
/// </summary>
public partial class DropSpawner : MultiplayerSpawner
{
  const float PickupRadius = 1.5f;
  const float HostPickupRadius = 3.0f;
  const float Lifetime = 300f;
  const float RequestRetrySeconds = 1.0f;

  public static DropSpawner? Instance { get; private set; }

  readonly Dictionary<string, double> pending = new();
  readonly List<string> expired = new();
  int nextId;
  double lastThrowMsec;

  public override void _EnterTree()
  {
    Instance = this;
  }

  public override void _ExitTree()
  {
    if (Instance == this)
    {
      Instance = null;
    }

    if (Terrain.Instance != null)
    {
      Terrain.Instance.BlockBroken -= OnBlockBroken;
    }
  }

  public override void _Ready()
  {
    SpawnFunction = Callable.From<Variant, Node>(Build);

    if (Terrain.Instance != null)
    {
      Terrain.Instance.BlockBroken += OnBlockBroken;
    }

    // Dev flags for screenshots (they fire once the world has settled): --break=x,y,z breaks that voxel, --spawn-drop=itemId,x,y,z (metres) drops an item.
    devBreak = DevArgs.Get("break");
    devDrop = DevArgs.Get("spawn-drop");
    devCracks = DevArgs.Get("crack-demo");
    devHits = DevArgs.Get("hit-demo");
    devBreakLater = DevArgs.Get("break-later");
  }

  string? devHits;
  string? devBreakLater;

  string? devCracks;

  string? devBreak;
  string? devDrop;

  void RunDevHooks()
  {
    if (Terrain.Instance is not { IsSettled: true })
    {
      return;
    }

    if (devBreak is { } breakAt)
    {
      var p = breakAt.Split(',');

      devBreak = null;
      Terrain.Instance.RequestSetBlock(new Vector3I(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2])), BlockIds.Air);
    }

    if (devBreakLater is { } laterAt)
    {
      var p = laterAt.Split(','); // x,y,z,seconds: breaks that voxel after a delay (a drop resting on it must fall)

      devBreakLater = null;
      GetTree().CreateTimer(double.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture)).Timeout +=
        () => Terrain.Instance?.RequestSetBlock(new Vector3I(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2])), BlockIds.Air);
    }

    if (devHits is { } hitsAt)
    {
      var p = hitsAt.Split(','); // x,y,z,hits: that many copper pickaxe hits (power 1, damage 10) through the real hit path

      devHits = null;

      for (int i = 0; i < int.Parse(p[3]); i++)
      {
        Terrain.Instance.RequestHit(new Vector3I(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2])), 1, 10f);
      }
    }

    if (devCracks is { } cracksAt)
    {
      var p = cracksAt.Split(','); // x,y,z,stage: a block with that crack stage, to see the overlay

      devCracks = null;
      var demo = new MeshInstance3D
      {
        Mesh = new BoxMesh { Size = Vector3.One * VoxelMath.BlockSize * 1.006f },
        MaterialOverride = BreakCracks.Stage(int.Parse(p[3])),
        TopLevel = true,
      };

      AddChild(demo);
      demo.GlobalPosition = new Vector3(VoxelMath.VoxelCenter(int.Parse(p[0])), VoxelMath.VoxelCenter(int.Parse(p[1])), VoxelMath.VoxelCenter(int.Parse(p[2])));
    }

    if (devDrop is { } dropAt)
    {
      var p = dropAt.Split(',');
      var culture = System.Globalization.CultureInfo.InvariantCulture;

      devDrop = null;
      SpawnDrop(int.Parse(p[0]), 1, new Vector3(float.Parse(p[1], culture), float.Parse(p[2], culture), float.Parse(p[3], culture)), Vector3.Zero);
    }
  }

  Node Build(Variant data)
  {
    var info = data.AsGodotDictionary();

    return new ItemDrop
    {
      Name = $"Drop_{info["id"].AsInt32()}",
      Position = info["pos"].AsVector3(),
      ItemId = info["item"].AsInt32(),
      Count = info["count"].AsInt32(),
      Velocity = info["vel"].AsVector3(),
    };
  }

  // ------------------------------------------------------------------ host: making drops

  /// <summary>Host only: a stack of items appears at a position with a velocity.</summary>
  public void SpawnDrop(int itemId, int count, Vector3 position, Vector3 velocity)
  {
    if (!Multiplayer.IsServer() || count <= 0 || ItemRegistry.Get(itemId) == null)
    {
      return;
    }

    Spawn(new Godot.Collections.Dictionary
    {
      ["id"] = nextId++,
      ["pos"] = position,
      ["item"] = itemId,
      ["count"] = count,
      ["vel"] = velocity,
    });
  }

  void OnBlockBroken(Vector3I voxel, byte oldBlock)
  {
    if (!Multiplayer.IsServer())
    {
      return;
    }

    var drop = BlockIds.Drop(oldBlock);
    var item = drop == BlockIds.Air ? null : ItemRegistry.ForBlock(drop);

    if (item == null)
    {
      return;
    }

    var centre = new Vector3(VoxelMath.VoxelCenter(voxel.X), VoxelMath.VoxelCenter(voxel.Y), VoxelMath.VoxelCenter(voxel.Z));

    SpawnDrop(item.NetId, 1, centre, new Vector3((GD.Randf() - 0.5f) * 2.4f, 2.5f + GD.Randf() * 1.5f, (GD.Randf() - 0.5f) * 2.4f));
  }

  // ------------------------------------------------------------------ throwing an item (Q)

  /// <summary>The local player throws items they already took out of their hotbar: the host makes the drop.</summary>
  public void RequestThrow(int itemId, int count, Vector3 origin, Vector3 direction)
  {
    if (Multiplayer.IsServer())
    {
      HostThrow(1, itemId, count, origin, direction);
    }
    else
    {
      RpcId(1, MethodName.RpcThrow, itemId, count, origin, direction);
    }
  }

  [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  void RpcThrow(int itemId, int count, Vector3 origin, Vector3 direction)
  {
    if (Multiplayer.IsServer())
    {
      HostThrow(Multiplayer.GetRemoteSenderId(), itemId, count, origin, direction);
    }
  }

  void HostThrow(int sender, int itemId, int count, Vector3 origin, Vector3 direction)
  {
    var player = PlayerOf(sender);
    var now = Time.GetTicksMsec();

    // Never trust a client: a real item, a sane count, thrown from near its own player, not faster than a person can.
    if (player == null || ItemRegistry.Get(itemId) == null || count < 1 || count > 999 || now - lastThrowMsec < 40
      || origin.DistanceTo(player.GlobalPosition + Vector3.Up) > 3f || direction.LengthSquared() < 0.01f)
    {
      return;
    }

    lastThrowMsec = now;
    SpawnDrop(itemId, count, origin, direction.Normalized() * 4.5f + Vector3.Up * 1.5f);
  }

  // ------------------------------------------------------------------ picking up

  public override void _PhysicsProcess(double delta)
  {
    RunDevHooks();

    var container = GetNodeOrNull(SpawnPath);

    if (container == null)
    {
      return;
    }

    if (Multiplayer.IsServer())
    {
      HostUpkeep(container);
    }

    var local = LocalPlayer();

    if (local == null || local.Health.IsDead)
    {
      return;
    }

    // The player this peer owns asks for the drops lying next to them (only when the hotbar has room for them).
    var now = Time.GetTicksMsec() / 1000.0;
    var position = local.GlobalPosition + Vector3.Up * 0.5f;

    foreach (var child in container.GetChildren())
    {
      if (child is not ItemDrop drop || !drop.CanBePickedUp || drop.GlobalPosition.DistanceTo(position) > PickupRadius || local.Inventory.RoomFor(drop.ItemId) < drop.Count)
      {
        continue;
      }

      if (pending.TryGetValue(drop.Name, out var until) && until > now)
      {
        continue;
      }

      pending[drop.Name] = now + RequestRetrySeconds;

      if (Multiplayer.IsServer())
      {
        HostClaim(1, drop.Name);
      }
      else
      {
        RpcId(1, MethodName.RpcClaim, drop.Name);
      }
    }

    ForgetOldRequests(now);
  }

  void ForgetOldRequests(double now)
  {
    if (pending.Count < 32)
    {
      return;
    }

    expired.Clear();

    foreach (var (name, until) in pending)
    {
      if (until < now)
      {
        expired.Add(name);
      }
    }

    foreach (var name in expired)
    {
      pending.Remove(name);
    }
  }

  [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  void RpcClaim(string dropName)
  {
    if (Multiplayer.IsServer())
    {
      HostClaim(Multiplayer.GetRemoteSenderId(), dropName);
    }
  }

  void HostClaim(int sender, string dropName)
  {
    var player = PlayerOf(sender);

    if (player == null || GetNodeOrNull(SpawnPath)?.GetNodeOrNull(dropName) is not ItemDrop drop || !drop.CanBePickedUp)
    {
      return;
    }

    if (drop.GlobalPosition.DistanceTo(player.GlobalPosition + Vector3.Up * 0.5f) > HostPickupRadius)
    {
      return;
    }

    drop.Claimed = true;
    drop.QueueFree();

    if (sender == 1)
    {
      Grant(drop.ItemId, drop.Count);
    }
    else
    {
      RpcId(sender, MethodName.RpcGrant, drop.ItemId, drop.Count);
    }
  }

  [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  void RpcGrant(int itemId, int count)
  {
    Grant(itemId, count);
  }

  /// <summary>The items reach the hotbar; whatever no longer fits (it filled up meanwhile) is thrown back down.</summary>
  void Grant(int itemId, int count)
  {
    var local = LocalPlayer();

    if (local == null)
    {
      return;
    }

    var left = local.Inventory.Add(itemId, count);

    if (left > 0)
    {
      RequestThrow(itemId, left, local.GlobalPosition + Vector3.Up, Vector3.Up);
    }
  }

  // ------------------------------------------------------------------ host: tidying up

  void HostUpkeep(Node container)
  {
    foreach (var child in container.GetChildren())
    {
      if (child is ItemDrop drop && !drop.Claimed && (drop.Age > Lifetime || drop.GlobalPosition.Y < -20f))
      {
        drop.Claimed = true;
        drop.QueueFree();
      }
    }
  }

  static PlayerController? LocalPlayer()
  {
    foreach (var player in PlayerController.All)
    {
      if (player.IsLocal)
      {
        return player;
      }
    }

    return null;
  }

  static PlayerController? PlayerOf(int peer)
  {
    foreach (var player in PlayerController.All)
    {
      if (player.GetMultiplayerAuthority() == peer)
      {
        return player;
      }
    }

    return null;
  }
}
