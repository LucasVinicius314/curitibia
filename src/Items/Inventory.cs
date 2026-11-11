using System;
using Curitibia.Core;
using Curitibia.Player;
using Godot;

namespace Curitibia.Items;

/// <summary>
/// The hotbar: a fixed row of slots and the selected one. It lives on the peer that owns the player; the other peers only
/// learn which item is held (replicated by the player). Every change goes through Set/Select so a later move to a
/// host-authoritative inventory (finite items, pickups) only has to put validation in these methods.
/// </summary>
public partial class Inventory : Node
{
  public const int HotbarSize = 9;

  [Export] public PlayerController Player { get; set; } = null!;

  readonly ItemStack[] slots = new ItemStack[HotbarSize];

  public int Selected { get; private set; }

  /// <summary>Raised after any slot or the selection changed (the HUD and the held-item display listen).</summary>
  public event Action? Changed;

  public ItemStack Get(int slot)
  {
    return slots[slot];
  }

  /// <summary>The selected item, or null for an empty slot.</summary>
  public ItemDef? Held => ItemRegistry.Get(slots[Selected].ItemId);

  public int HeldId => slots[Selected].IsEmpty ? 0 : slots[Selected].ItemId;

  public override void _Ready()
  {
    if (!Player.IsLocal)
    {
      SetProcessUnhandledInput(false);

      return;
    }

    // The tools, one of each (no crafting yet, so every pickaxe is here to try the power levels); blocks come from mining.
    var slot = 0;

    foreach (var def in ItemRegistry.All)
    {
      if (slot >= HotbarSize)
      {
        break;
      }

      if (def.Behavior is SwordBehavior or PickaxeBehavior)
      {
        slots[slot++] = new ItemStack(def.NetId, 1);
      }
    }

    if (DevArgs.Get("give") is { } give && give.Split(',') is { Length: 2 } parts)
    {
      Add(int.Parse(parts[0]), int.Parse(parts[1])); // dev flag --give=itemId,count (screenshots)
    }

    if (DevArgs.GetInt("slot") is { } start)
    {
      Selected = Math.Clamp(start, 0, HotbarSize - 1); // dev flag --slot=N: start with that hotbar slot selected (screenshots)
    }

    CallDeferred(MethodName.Announce);
  }

  void Announce()
  {
    Changed?.Invoke();
  }

  public void Set(int slot, ItemStack stack)
  {
    slots[slot] = stack;
    Changed?.Invoke();
  }

  /// <summary>How many of an item still fit (into its stacks and the empty slots).</summary>
  public int RoomFor(int itemId)
  {
    var def = ItemRegistry.Get(itemId);

    if (def == null)
    {
      return 0;
    }

    var room = 0;

    foreach (var stack in slots)
    {
      if (stack.IsEmpty)
      {
        room += def.MaxStack;
      }
      else if (stack.ItemId == itemId)
      {
        room += Math.Max(0, def.MaxStack - stack.Count);
      }
    }

    return room;
  }

  /// <summary>Puts items into the hotbar (onto stacks of the same item first, then into empty slots) and returns how many did not fit.</summary>
  public int Add(int itemId, int count)
  {
    var def = ItemRegistry.Get(itemId);

    if (def == null)
    {
      return count;
    }

    for (int pass = 0; pass < 2 && count > 0; pass++)
    {
      for (int i = 0; i < HotbarSize && count > 0; i++)
      {
        var stack = slots[i];

        if (pass == 0 ? (stack.IsEmpty || stack.ItemId != itemId) : !stack.IsEmpty)
        {
          continue;
        }

        var moved = Math.Min(count, def.MaxStack - (stack.IsEmpty ? 0 : stack.Count));

        if (moved > 0)
        {
          slots[i] = new ItemStack(itemId, (stack.IsEmpty ? 0 : stack.Count) + moved);
          count -= moved;
        }
      }
    }

    Changed?.Invoke();

    return count;
  }

  /// <summary>Uses up some of the selected stack (not for infinite items); an emptied slot becomes empty.</summary>
  public void ConsumeSelected(int count)
  {
    var stack = slots[Selected];

    if (stack.IsEmpty || ItemRegistry.Get(stack.ItemId)?.Infinite == true)
    {
      return;
    }

    slots[Selected] = stack.Count - count > 0 ? stack with { Count = stack.Count - count } : ItemStack.Empty;
    Changed?.Invoke();
  }

  /// <summary>Takes up to <paramref name="count"/> out of the selected slot (to drop it) and returns what was taken.</summary>
  public ItemStack TakeSelected(int count)
  {
    var stack = slots[Selected];

    if (stack.IsEmpty)
    {
      return ItemStack.Empty;
    }

    var taken = Math.Min(count, stack.Count);

    slots[Selected] = stack.Count - taken > 0 ? stack with { Count = stack.Count - taken } : ItemStack.Empty;
    Changed?.Invoke();

    return new ItemStack(stack.ItemId, taken);
  }

  public void Select(int index)
  {
    Selected = ((index % HotbarSize) + HotbarSize) % HotbarSize;
    Changed?.Invoke();
  }

  public override void _UnhandledInput(InputEvent @event)
  {
    if (Input.MouseMode != Input.MouseModeEnum.Captured)
    {
      return;
    }

    if (@event.IsActionPressed(Actions.NextItem))
    {
      Select(Selected + 1);
    }
    else if (@event.IsActionPressed(Actions.PrevItem))
    {
      Select(Selected - 1);
    }
    else if (@event is InputEventKey { Pressed: true, Echo: false } key && key.PhysicalKeycode >= Key.Key1 && key.PhysicalKeycode < Key.Key1 + HotbarSize)
    {
      Select((int)(key.PhysicalKeycode - Key.Key1));
    }
  }
}
