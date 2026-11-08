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

    // Everything in the catalogue, in catalogue order (a creative loadout for now).
    var slot = 0;

    foreach (var def in ItemRegistry.All)
    {
      if (slot >= HotbarSize)
      {
        break;
      }

      slots[slot++] = new ItemStack(def.NetId, def.Infinite ? 1 : def.MaxStack);
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
