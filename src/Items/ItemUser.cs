using System;
using Curitibia.Core;
using Curitibia.Player;
using Curitibia.World;
using Godot;

namespace Curitibia.Items;

/// <summary>
/// Turns the primary and secondary buttons into using the held item. Behaviors that announce their use (a sword swing) go
/// through one RPC from the owner to everyone, so every peer can animate it and the host can resolve it with its own
/// cooldown; the others (block edits) run on the owner and rely on the validated edit path.
/// </summary>
public partial class ItemUser : Node
{
  [Export] public PlayerController Player { get; set; } = null!;

  double localReadyAt;
  ulong hostReadyAtMsec;

  /// <summary>Raised on every peer when the player used an item (the held model plays its swing).</summary>
  public event Action? Used;

  public override void _Ready()
  {
    if (!Player.IsLocal)
    {
      SetProcess(false);
    }
  }

  ItemUseContext Context => new(Player, Terrain.Instance);

  public override void _Process(double delta)
  {
    if (Player.Health.IsDead || Input.MouseMode != Input.MouseModeEnum.Captured)
    {
      return;
    }

    var held = Player.Inventory.Held;
    var behavior = held?.Behavior;

    if (held == null || behavior == null)
    {
      return;
    }

    var primary = behavior.RepeatsWhileHeld ? Input.IsActionPressed(Actions.BreakBlock) : Input.IsActionJustPressed(Actions.BreakBlock);
    var secondary = Input.IsActionJustPressed(Actions.PlaceBlock);

    if (!primary && !secondary)
    {
      return;
    }

    var now = Time.GetTicksMsec() / 1000.0;

    if (now < localReadyAt)
    {
      return;
    }

    localReadyAt = now + behavior.Cooldown;

    behavior.OwnerUse(Context, primary);

    if (behavior.Announces)
    {
      Rpc(MethodName.RpcUse, held.NetId, primary);
    }
    else
    {
      Used?.Invoke();
    }
  }

  [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  void RpcUse(int itemId, bool primary)
  {
    Used?.Invoke();

    if (Multiplayer.IsServer())
    {
      HostResolve(itemId, primary);
    }
  }

  void HostResolve(int itemId, bool primary)
  {
    var behavior = ItemRegistry.Get(itemId)?.Behavior;
    var now = Time.GetTicksMsec();

    if (behavior == null || now < hostReadyAtMsec || Player.Health.IsDead)
    {
      return;
    }

    hostReadyAtMsec = now + (ulong)(behavior.Cooldown * 900f); // a little slack for network jitter

    if (behavior.HitDelay <= 0f)
    {
      behavior.HostUse(Context, primary);
      return;
    }

    // The blow lands when the blade connects, not when the button went down.
    GetTree().CreateTimer(behavior.HitDelay).Timeout += () =>
    {
      if (IsInstanceValid(this) && IsInsideTree() && !Player.Health.IsDead)
      {
        behavior.HostUse(Context, primary);
      }
    };
  }
}
