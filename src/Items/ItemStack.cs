namespace Curitibia.Items;

/// <summary>A number of one kind of item in an inventory slot. ItemId 0 is "nothing".</summary>
public readonly record struct ItemStack(int ItemId, int Count)
{
  public static readonly ItemStack Empty = new(0, 0);

  public bool IsEmpty => ItemId == 0 || Count <= 0;
}
