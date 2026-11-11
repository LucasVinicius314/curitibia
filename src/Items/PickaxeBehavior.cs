using Godot;

namespace Curitibia.Items;

/// <summary>
/// A mining tool, as in Terraria: MiningPower says which blocks it can break at all, MiningSpeed how many times a second it
/// swings while the primary button is held, MiningDamage how much each swing takes off the block's durability. Announced, so
/// everyone sees the swings.
/// </summary>
[GlobalClass]
public partial class PickaxeBehavior : ItemBehavior
{
  public override bool Announces => true;

  public override bool RepeatsWhileHeld => true;

  public override bool WantsBlockTarget => true;

  public override void OwnerUse(ItemUseContext context, bool primary)
  {
    if (primary)
    {
      context.Player.Interactor.Hit(MiningPower, MiningDamage);
    }
  }

}
