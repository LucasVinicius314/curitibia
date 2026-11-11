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
    "res://data/items/iron_pickaxe.tres",
    "res://data/items/gold_pickaxe.tres",
    "res://data/items/platinum_pickaxe.tres",
    "res://data/items/stone.tres",
    "res://data/items/grass.tres",
    "res://data/items/wood.tres",
    "res://data/items/leaves.tres",
    "res://data/items/dirt.tres",
    "res://data/items/sand.tres",
    "res://data/items/snow.tres",
    "res://data/items/cactus.tres",
    "res://data/items/pine_leaves.tres",
    "res://data/items/birch_log.tres",
    "res://data/items/birch_leaves.tres",
    "res://data/items/hellrock.tres",
    "res://data/items/copper_ore.tres",
    "res://data/items/tin_ore.tres",
    "res://data/items/iron_ore.tres",
    "res://data/items/silver_ore.tres",
    "res://data/items/gold_ore.tres",
    "res://data/items/platinum_ore.tres",
  };

  static List<ItemDef>? all;
  static Dictionary<int, ItemDef>? byId;
  static Dictionary<int, ItemDef>? byBlock;

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

  /// <summary>The item that places a block (what breaking that block drops), or null when there is none.</summary>
  public static ItemDef? ForBlock(byte block)
  {
    Load();

    return byBlock!.TryGetValue(block, out var def) ? def : null;
  }

  static void Load()
  {
    if (all != null)
    {
      return;
    }

    all = new List<ItemDef>();
    byId = new Dictionary<int, ItemDef>();
    byBlock = new Dictionary<int, ItemDef>();

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

      if (def.Behavior is PlaceBlockBehavior place)
      {
        byBlock[place.BlockId] = def;
      }
    }
  }
}
