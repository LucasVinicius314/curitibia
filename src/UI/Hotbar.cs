using Curitibia.Items;
using Godot;

namespace Curitibia.UI;

/// <summary>
/// The row of hotbar slots at the bottom of the screen: a rendered icon, (for finite items) the count,
/// with the selected slot outlined. It only observes the inventory and redraws on its change events, never per frame.
/// </summary>
public partial class Hotbar : HBoxContainer
{
  const float SlotSize = 48f;

  static readonly StyleBoxFlat normalStyle = Slot(new Color(0.08f, 0.09f, 0.13f, 0.78f), Ui.Border, 2);
  static readonly StyleBoxFlat selectedStyle = Slot(new Color(0.15f, 0.18f, 0.26f, 0.9f), Ui.Accent, 3);

  readonly PanelContainer[] panels = new PanelContainer[Inventory.HotbarSize];
  readonly TextureRect[] icons = new TextureRect[Inventory.HotbarSize];
  readonly Label[] counts = new Label[Inventory.HotbarSize];

  Inventory? inventory;

  static StyleBoxFlat Slot(Color fill, Color border, int borderWidth)
  {
    var box = new StyleBoxFlat { BgColor = fill, BorderColor = border };
    box.SetBorderWidthAll(borderWidth);
    box.SetCornerRadiusAll(8);
    box.SetContentMarginAll(4f);

    return box;
  }

  public override void _Ready()
  {
    AddThemeConstantOverride("separation", 5);
    MouseFilter = MouseFilterEnum.Ignore;

    for (int i = 0; i < Inventory.HotbarSize; i++)
    {
      var panel = new PanelContainer { CustomMinimumSize = new Vector2(SlotSize, SlotSize), MouseFilter = MouseFilterEnum.Ignore };
      panel.AddThemeStyleboxOverride("panel", normalStyle);

      var icon = new TextureRect
      {
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        MouseFilter = MouseFilterEnum.Ignore,
      };
      panel.AddChild(icon);

      var count = Ui.Label("", null, HorizontalAlignment.Right);
      count.VerticalAlignment = VerticalAlignment.Bottom;
      count.MouseFilter = MouseFilterEnum.Ignore;
      panel.AddChild(count);

      panels[i] = panel;
      icons[i] = icon;
      counts[i] = count;
      AddChild(panel);
    }

    ItemIconRenderer.IconsReady += Refresh;
  }

  public override void _ExitTree()
  {
    ItemIconRenderer.IconsReady -= Refresh;

    if (inventory != null)
    {
      inventory.Changed -= Refresh;
    }
  }

  public void Bind(Inventory source)
  {
    inventory = source;
    inventory.Changed += Refresh;
    Refresh();
  }

  void Refresh()
  {
    if (inventory == null)
    {
      return;
    }

    for (int i = 0; i < Inventory.HotbarSize; i++)
    {
      var stack = inventory.Get(i);
      var def = ItemRegistry.Get(stack.ItemId);

      icons[i].Texture = stack.IsEmpty ? null : ItemIconRenderer.IconFor(stack.ItemId);
      counts[i].Text = !stack.IsEmpty && def is { Infinite: false } && stack.Count > 1 ? stack.Count.ToString() : "";
      panels[i].AddThemeStyleboxOverride("panel", i == inventory.Selected ? selectedStyle : normalStyle);
    }
  }
}
