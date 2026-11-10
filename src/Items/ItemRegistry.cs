using System.Collections.Generic;
using Godot;

namespace Curitibia.Items;

/// <summary>
/// Every item in the game, loaded once from data/items. The list is explicit (not a directory scan, which does not survive
/// an export): a new item is a .tres file plus a line here. Ids are the stable NetId of each definition.
/// </summary>
public static class ItemRegistry
{
  static readonly string[] Paths =
  {
    "res://data/items/copper_sword.tres",
    "res://data/items/copper_pickaxe.tres",
    "res://data/items/stone.tres",
    "res://data/items/grass.tres",
    "res://data/items/wood.tres",
    "res://data/items/leaves.tres",
  };

  static List<ItemDef>? all;
  static Dictionary<int, ItemDef>? byId;

  public static IReadOnlyList<ItemDef> All
  {
    get
    {
      Load();

      return all!;
    }
  }

  /// <summary>The definition for a NetId, or null (0 and unknown ids included).</summary>
  public static ItemDef? Get(int netId)
  {
    Load();

    return byId!.TryGetValue(netId, out var def) ? def : null;
  }

  static void Load()
  {
    if (all != null)
    {
      return;
    }

    all = new List<ItemDef>();
    byId = new Dictionary<int, ItemDef>();

    foreach (var path in Paths)
    {
      var def = GD.Load<ItemDef>(path);

      if (def == null || def.NetId <= 0 || byId.ContainsKey(def.NetId))
      {
        GD.PushError($"item {path}: missing, or its NetId is not a unique positive number");
        continue;
      }

      all.Add(def);
      byId[def.NetId] = def;
    }
  }
}
