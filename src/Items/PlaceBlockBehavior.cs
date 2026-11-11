using Godot;

namespace Curitibia.Items;

/// <summary>Primary mines the targeted block by hand (slowly, power 0), secondary places this block against its face and uses one up.</summary>
[GlobalClass]
public partial class PlaceBlockBehavior : ItemBehavior
{
  [Export] public int BlockId { get; set; }

  public override bool WantsBlockTarget => true;

  /// <summary>Holding the primary button keeps punching the block (by hand: power 0).</summary>
  public override bool RepeatsWhileHeld => true;

  public override void OwnerUse(ItemUseContext context, bool primary)
  {
    if (primary)
    {
      context.Player.Interactor.Hit(MiningPower, MiningDamage);
    }
    else if (context.Player.Interactor.PlaceTarget((byte)BlockId))
    {
      context.Player.Inventory.ConsumeSelected(1);
    }
  }
}
