using Godot;

namespace Curitibia.Items;

/// <summary>
/// What using an item does, as a resource an ItemDef points to. Two hooks, depending on how the effect must reach the other
/// peers: OwnerUse runs only on the peer that owns the player (effects that already go through a validated path, such as
/// block edits), HostUse runs on the host after the owner announced the use (damage and other host-decided effects).
/// </summary>
[GlobalClass]
public partial class ItemBehavior : Resource
{
  /// <summary>Seconds between uses.</summary>
  [Export] public float Cooldown { get; set; } = 0.25f;

  /// <summary>The use is announced to every peer (so they can animate it) and resolved on the host through HostUse.</summary>
  public virtual bool Announces => false;

  /// <summary>Holding the primary button keeps using it.</summary>
  public virtual bool RepeatsWhileHeld => false;

  /// <summary>The block highlight and targeting are wanted while this item is held.</summary>
  public virtual bool WantsBlockTarget => false;

  public virtual void OwnerUse(ItemUseContext context, bool primary)
  {
  }

  public virtual void HostUse(ItemUseContext context, bool primary)
  {
  }
}
