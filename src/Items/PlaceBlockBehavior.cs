using Godot;

namespace Curitibia.Items;

/// <summary>Primary breaks the targeted block, secondary places this block against its face.</summary>
[GlobalClass]
public partial class PlaceBlockBehavior : ItemBehavior
{
  [Export] public int BlockId { get; set; }

  public override bool WantsBlockTarget => true;

  public override void OwnerUse(ItemUseContext context, bool primary)
  {
    if (primary)
    {
      context.Player.Interactor.BreakTarget();
    }
    else
    {
      context.Player.Interactor.PlaceTarget((byte)BlockId);
    }
  }
}
