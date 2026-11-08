using Godot;

namespace Curitibia.Items;

/// <summary>
/// Breaks the targeted block over and over while the primary button is held, one block per Cooldown. It breaks anything,
/// like the creative-style block tools; a later version can add hardness and tiers. Announced, so everyone sees the swings.
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
      context.Player.Interactor.BreakTarget();
    }
  }
}
