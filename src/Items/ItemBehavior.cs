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

  /// <summary>
  /// Pickaxe power while held primary mines (0 = bare hand): it must reach a block's required power (BlockIds.RequiredPower)
  /// or the block cannot be broken with it at all.
  /// </summary>
  [Export] public int MiningPower { get; set; }

  /// <summary>Swings per second while the primary button is held (0 = this item does not mine): the attack frequency.</summary>
  [Export] public float MiningSpeed { get; set; }

  /// <summary>Damage one swing deals to the targeted block's durability (BlockIds.Durability), if the power is enough.</summary>
  [Export] public float MiningDamage { get; set; }

  /// <summary>Seconds before the next use after this one: the mining swing rate for a primary use of a mining tool, else the Cooldown.</summary>
  public float IntervalAfter(bool primary)
  {
    return primary && MiningSpeed > 0f ? 1f / MiningSpeed : Cooldown;
  }

  /// <summary>Seconds after the use is announced before the host applies it, so damage lands when the swing connects (0 = at once).</summary>
  [Export] public float HitDelay { get; set; }

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
