using System;
using System.Collections.Generic;
using Curitibia.Core;
using Curitibia.World;
using Godot;

namespace Curitibia.UI;

/// <summary>
/// The options as one full-screen view shared by the main menu and the pause menu: a title bar with Back, a sidebar of categories
/// (Video, World, Controls, Debug, plus an optional extra one such as the server address) and the selected category's rows on
/// the right, each a one-line "name, slider, value" with its description in small text underneath. Dense on purpose (the compact
/// theme): it uses the whole window instead of a card in the middle. Values go straight to GameSettings, which saves them and
/// tells the game.
/// </summary>
public static class SettingsPanel
{
  const float NameWidth = 190f;
  const float ValueWidth = 100f;
  const float SidebarWidth = 170f;

  static readonly (string Keys, string Action)[] ControlList =
  [
    ("W A S D", "Move"),
    ("Space", "Jump (hold to keep jumping)"),
    ("Shift", "Sprint"),
    ("V", "Third person"),
    ("F", "Noclip flight (Space up, Ctrl down)"),
    ("1 - 3 or wheel", "Pick the sword or a block"),
    ("Left click", "Use the item / break a block"),
    ("Right click", "Place a block"),
    ("M", "World map"),
    ("Tab", "Players"),
    ("Esc", "Pause"),
  ];

  /// <summary>
  /// The whole screen: fills its parent, so add it as a sibling of the content it replaces. <paramref name="back"/> runs from the
  /// Back button; <paramref name="extra"/> is one more category (title and content) after Debug.
  /// </summary>
  public static Control BuildScreen(Action back, bool showControls = true, (string Title, Control Content)? extra = null)
  {
    var screen = new MarginContainer { Theme = Ui.Compact, Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
    screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
    screen.AddThemeConstantOverride("margin_left", 40);
    screen.AddThemeConstantOverride("margin_right", 40);
    screen.AddThemeConstantOverride("margin_top", 28);
    screen.AddThemeConstantOverride("margin_bottom", 28);

    var surface = Ui.Surface();
    screen.AddChild(surface);

    var shell = Ui.Column(10);
    surface.AddChild(shell);

    var header = Ui.Row();
    var title = Ui.Label("Settings", Ui.Heading);
    title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    header.AddChild(title);

    var backButton = Ui.Button("Back", Ui.Primary, 110f);
    backButton.CustomMinimumSize = new Vector2(110f, 32f);
    backButton.Pressed += back;
    header.AddChild(backButton);
    shell.AddChild(header);
    shell.AddChild(Ui.Separator());

    var body = Ui.Row(16);
    body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
    shell.AddChild(body);

    var sidebar = Ui.Column(4);
    sidebar.CustomMinimumSize = new Vector2(SidebarWidth, 0f);
    body.AddChild(sidebar);
    body.AddChild(new VSeparator());

    var pages = Ui.Column(0);
    pages.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    pages.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
    body.AddChild(pages);

    var categories = new List<(string Title, Control Content)>
    {
      ("Video", Page(VideoRows())),
      ("World", Page(WorldRows())),
      ("Controls", Page(ControlRows(showControls))),
      ("Debug", Page(DebugRows())),
    };

    if (extra != null)
    {
      categories.Add((extra.Value.Title, Page([extra.Value.Content])));
    }

    var group = new ButtonGroup();
    var first = true;

    foreach (var (name, content) in categories)
    {
      var shown = content;
      var button = new Button
      {
        Text = name,
        ToggleMode = true,
        ButtonGroup = group,
        ThemeTypeVariation = Ui.Nav,
        FocusMode = Control.FocusModeEnum.None,
        CustomMinimumSize = new Vector2(0f, 32f),
        ButtonPressed = first,
      };

      button.Pressed += () =>
      {
        foreach (var other in pages.GetChildren())
        {
          ((Control)other).Visible = other == shown;
        }
      };

      content.Visible = first;
      first = false;
      sidebar.AddChild(button);
      pages.AddChild(content);
    }

    return screen;
  }

  // ------------------------------------------------------------------ categories

  static Control[] VideoRows() =>
  [
    Slider(
      "Field of view",
      "Vertical field of view. Higher shows more to the sides (90 is about 119 degrees across on a 16:9 screen).",
      GameSettings.MinFieldOfView, GameSettings.MaxFieldOfView, 1f, GameSettings.FieldOfView,
      value => GameSettings.FieldOfView = value,
      value => $"{value:0}°"),
    Slider(
      "Render distance",
      "How far the world is drawn, in chunks of 16 m. Lower is lighter on memory and the GPU; the cost grows with the square.",
      GameSettings.MinRenderDistance, GameSettings.MaxRenderDistance, 1f, GameSettings.RenderDistance,
      value => GameSettings.RenderDistance = (int)value,
      value => $"{value:0} ({value * 16:0} m)"),
    Slider(
      "Shadow distance",
      "How far from you the sun casts shadows. Farther looks better but costs GPU time and makes nearby shadows softer.",
      GameSettings.MinShadowDistance, GameSettings.MaxShadowDistance, 10f, GameSettings.ShadowDistance,
      value => GameSettings.ShadowDistance = value,
      value => $"{value:0} m"),
  ];

  static Control[] WorldRows()
  {
    var cycle = new CheckButton
    {
      Text = "Day and night cycle",
      ButtonPressed = GameSettings.DayNightCycle,
      FocusMode = Control.FocusModeEnum.None,
    };

    cycle.Toggled += on => GameSettings.DayNightCycle = on;

    var clock = Slider(
      "Time of day",
      "Drag to set the hour (0 midnight, 12 noon). With the cycle on, the day carries on from there.",
      0f, 24f, 0.25f, Wrap24(), value => DayNight.SetTime(value / 24f), Clock, out var slider);

    // With the cycle running the hour moves on its own, so the slider follows it unless the player is using it.
    var refresh = new Godot.Timer { WaitTime = 0.5, Autostart = true };

    refresh.Timeout += () =>
    {
      if (!slider.HasFocus() && !Input.IsMouseButtonPressed(MouseButton.Left))
      {
        slider.SetValueNoSignal(Wrap24());
        slider.EmitSignal(Godot.Range.SignalName.Changed);
      }
    };

    clock.AddChild(refresh);

    return
    [
      WithHint(cycle, "A full day lasts 10 minutes and enemies come out more at night. Off keeps it noon all the time."),
      clock,
    ];
  }

  static Control[] ControlRows(bool showControls)
  {
    var rows = new List<Control>
    {
      Slider(
        "Look speed",
        "Multiplies mouse and stick look speed.",
        GameSettings.MinMouseSensitivity, GameSettings.MaxMouseSensitivity, 0.05f, GameSettings.MouseSensitivity,
        value => GameSettings.MouseSensitivity = value,
        value => $"{value:0.00}x"),
    };

    if (showControls)
    {
      rows.Add(Ui.Separator());
      rows.Add(Ui.Label("Keys", Ui.Subtle));

      var grid = new GridContainer { Columns = 2 };
      grid.AddThemeConstantOverride("h_separation", 24);
      grid.AddThemeConstantOverride("v_separation", 4);

      foreach (var (keys, action) in ControlList)
      {
        var key = Ui.Label(keys);
        Ui.Tint(key, Ui.Accent);
        grid.AddChild(key);
        grid.AddChild(Ui.Label(action));
      }

      rows.Add(grid);
    }

    return rows.ToArray();
  }

  static Control[] DebugRows() =>
  [
    Slider(
      "Move speed",
      "Multiplies walking, sprinting and flying speed. Not saved: back to 1x next time you start the game.",
      GameSettings.MinMoveSpeed, GameSettings.MaxMoveSpeed, 0.25f, GameSettings.MoveSpeedMultiplier,
      value => GameSettings.MoveSpeedMultiplier = value,
      value => $"{value:0.00}x"),
  ];

  // ------------------------------------------------------------------ widgets

  /// <summary>One category's page: its rows in a vertical scroll area (the wheel scrolls it; sliders ignore the wheel).</summary>
  static ScrollContainer Page(Control[] rows)
  {
    var scroll = new ScrollContainer
    {
      HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
      SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
      SizeFlagsVertical = Control.SizeFlags.ExpandFill,
    };

    var column = Ui.Column(12);
    column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    scroll.AddChild(column);

    foreach (var row in rows)
    {
      column.AddChild(row);
    }

    return scroll;
  }

  static float Wrap24() => DayNight.TimeOfDay * 24f;

  static string Clock(float hours)
  {
    var minutes = (int)Mathf.Round(hours * 60f) % 1440;

    return $"{minutes / 60:00}:{minutes % 60:00}";
  }

  /// <summary>A control with its description in small text underneath.</summary>
  static Control WithHint(Control control, string hint)
  {
    var box = Ui.Column(2);
    box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    box.AddChild(control);
    box.AddChild(Ui.Paragraph(hint, Ui.Subtle));

    return box;
  }

  static Control Slider(string title, string hint, float min, float max, float step, float value, Action<float> apply, Func<float, string> format)
  {
    return Slider(title, hint, min, max, step, value, apply, format, out _);
  }

  /// <summary>A row: the name, the slider (about half the width, so it stays a sensible length) and its value; the description below.</summary>
  static Control Slider(string title, string hint, float min, float max, float step, float value, Action<float> apply, Func<float, string> format, out HSlider slider)
  {
    var row = Ui.Row(12);

    var name = Ui.Label(title);
    name.CustomMinimumSize = new Vector2(NameWidth, 0f);
    row.AddChild(name);

    slider = new HSlider
    {
      MinValue = min,
      MaxValue = max,
      Step = step,
      Value = value,
      Scrollable = false,
      SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
      SizeFlagsStretchRatio = 2f,
      SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
      CustomMinimumSize = new Vector2(220f, 22f),
    };

    var shown = Ui.Label(format(value), null, HorizontalAlignment.Right);
    shown.CustomMinimumSize = new Vector2(ValueWidth, 0f);

    var self = slider;

    slider.ValueChanged += v =>
    {
      apply((float)v);
      shown.Text = format((float)v);
    };
    slider.Changed += () => shown.Text = format((float)self.Value);

    row.AddChild(slider);
    row.AddChild(shown);
    row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2f });

    return WithHint(row, hint);
  }
}
