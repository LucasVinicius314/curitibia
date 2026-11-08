using Godot;

namespace Curitibia.Items;

/// <summary>
/// One kind of item as data (data/items/*.tres). What it does is its Behavior (composition, not subclasses of the item),
/// how it looks in the hand and in the hotbar is its Model. Adding an item means a new .tres and one line in
/// ItemRegistry; the hotbar icon is rendered from the model automatically.
/// </summary>
[GlobalClass]
public partial class ItemDef : Resource
{
  /// <summary>Stable id used on the network and in saves. Never reuse or renumber one.</summary>
  [Export] public int NetId { get; set; }

  [Export] public string DisplayName { get; set; } = "";

  [Export] public int MaxStack { get; set; } = 1;

  /// <summary>Using it never consumes it and the hotbar shows no count (creative-style blocks).</summary>
  [Export] public bool Infinite { get; set; }

  [Export] public ItemBehavior? Behavior { get; set; }

  [Export] public ItemModel? Model { get; set; }
}
