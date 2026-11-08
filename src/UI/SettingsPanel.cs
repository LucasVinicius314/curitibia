using Curitibia.Core;
using Godot;

namespace Curitibia.UI;

/// <summary>
/// The in-game options (field of view, look speed) as one reusable block, shared by the main menu's Settings page and the
/// pause menu. Values go straight to GameSettings, which saves them and tells the game.
/// </summary>
public static class SettingsPanel
{
  public static VBoxContainer Build(bool showControls = true)
  {
    var column = Ui.Column(10);

    column.AddChild(Slider(
      "Field of view",
      GameSettings.MinFieldOfView, GameSettings.MaxFieldOfView, 1f, GameSettings.FieldOfView,
      value => GameSettings.FieldOfView = value,
      value => $"{value:0}°"));
    column.AddChild(Ui.Paragraph("Vertical field of view. Higher shows more to the sides (90 is about 119 degrees across on a 16:9 screen).", Ui.Subtle));

    column.AddChild(Ui.Gap(2f));

    column.AddChild(Slider(
      "Look speed",
      GameSettings.MinMouseSensitivity, GameSettings.MaxMouseSensitivity, 0.05f, GameSettings.MouseSensitivity,
      value => GameSettings.MouseSensitivity = value,
      value => $"{value:0.00}x"));

    if (!showControls)
    {
      return column;
    }

    column.AddChild(Ui.Gap(2f));
    column.AddChild(Ui.Label("Controls", Ui.Subtle));
    column.AddChild(Ui.Paragraph(
      "WASD move, Space jump (hold to keep jumping), Shift sprint, V third person, F noclip flight (Space up, Ctrl down), "
      + "1-3 or wheel pick the sword or a block, left click use, right click place, Tab players, Esc pause.",
      Ui.Subtle));

    return column;
  }

  static Control Slider(string title, float min, float max, float step, float value, System.Action<float> apply, System.Func<float, string> format)
  {
    var box = Ui.Column(4);
    box.AddChild(Ui.Label(title, Ui.Subtle));

    var row = Ui.Row(14);
    var shown = Ui.Label(format(value));
    shown.CustomMinimumSize = new Vector2(64f, 0f);

    var slider = new HSlider
    {
      MinValue = min,
      MaxValue = max,
      Step = step,
      Value = value,
      SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
      SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
      CustomMinimumSize = new Vector2(0f, 28f),
    };

    slider.ValueChanged += v =>
    {
      apply((float)v);
      shown.Text = format((float)v);
    };

    row.AddChild(slider);
    row.AddChild(shown);
    box.AddChild(row);

    return box;
  }
}
