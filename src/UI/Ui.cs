using Godot;

namespace Curitibia.UI;

/// <summary>
/// The game's look in one place: a dark theme built in code (panels, buttons, fields) plus tiny widget helpers. Menus and
/// overlays are built from these so they match and stay short. Apply with <c>control.Theme = Ui.Theme</c> on the root.
/// </summary>
public static class Ui
{
  public static readonly Color Background = new(0.06f, 0.07f, 0.10f);
  public static readonly Color Panel = new(0.11f, 0.13f, 0.18f);
  public static readonly Color PanelRaised = new(0.15f, 0.18f, 0.24f);
  public static readonly Color Border = new(0.22f, 0.26f, 0.35f);
  public static readonly Color Text = new(0.92f, 0.94f, 0.98f);
  public static readonly Color Dim = new(0.60f, 0.66f, 0.76f);
  public static readonly Color Accent = new(0.30f, 0.62f, 1.00f);
  public static readonly Color Good = new(0.40f, 0.85f, 0.55f);
  public static readonly Color Warn = new(0.98f, 0.78f, 0.35f);
  public static readonly Color Bad = new(1.00f, 0.45f, 0.45f);

  public static readonly StringName Primary = "PrimaryButton";
  public static readonly StringName Danger = "DangerButton";
  public static readonly StringName Title = "TitleLabel";
  public static readonly StringName Heading = "HeadingLabel";
  public static readonly StringName Subtle = "SubtleLabel";
  public static readonly StringName Mono = "CodeLabel";

  static Theme? theme;

  public static Theme Theme => theme ??= Build();

  // ------------------------------------------------------------------ theme

  static StyleBoxFlat Box(Color fill, Color? border = null, int radius = 8, int borderWidth = 1, int padX = 14, int padY = 9)
  {
    var box = new StyleBoxFlat
    {
      BgColor = fill,
      CornerRadiusTopLeft = radius,
      CornerRadiusTopRight = radius,
      CornerRadiusBottomLeft = radius,
      CornerRadiusBottomRight = radius,
      ContentMarginLeft = padX,
      ContentMarginRight = padX,
      ContentMarginTop = padY,
      ContentMarginBottom = padY,
    };

    if (border != null)
    {
      box.BorderColor = border.Value;
      box.SetBorderWidthAll(borderWidth);
    }

    return box;
  }

  static void ButtonStyle(Theme t, StringName type, Color fill, Color hover, Color pressed, Color text)
  {
    t.SetStylebox("normal", type, Box(fill, Border));
    t.SetStylebox("hover", type, Box(hover, Accent));
    t.SetStylebox("pressed", type, Box(pressed, Accent));
    t.SetStylebox("focus", type, Box(new Color(0, 0, 0, 0), Accent, 8, 2));
    t.SetStylebox("disabled", type, Box(fill.Darkened(0.35f), Border.Darkened(0.3f)));
    t.SetColor("font_color", type, text);
    t.SetColor("font_hover_color", type, text);
    t.SetColor("font_pressed_color", type, text);
    t.SetColor("font_focus_color", type, text);
    t.SetColor("font_disabled_color", type, Dim.Darkened(0.35f));
  }

  static Theme Build()
  {
    var t = new Theme { DefaultFontSize = 18 };

    t.SetColor("font_color", "Label", Text);

    t.SetStylebox("panel", "PanelContainer", Box(Panel, Border, 14, 1, 22, 20));

    ButtonStyle(t, "Button", PanelRaised, new Color(0.20f, 0.24f, 0.32f), new Color(0.12f, 0.15f, 0.21f), Text);

    t.SetTypeVariation(Primary, "Button");
    ButtonStyle(t, Primary, new Color(0.20f, 0.45f, 0.85f), new Color(0.28f, 0.55f, 0.95f), new Color(0.16f, 0.36f, 0.70f), Colors.White);

    t.SetTypeVariation(Danger, "Button");
    ButtonStyle(t, Danger, new Color(0.45f, 0.18f, 0.20f), new Color(0.60f, 0.22f, 0.25f), new Color(0.35f, 0.13f, 0.15f), Colors.White);

    t.SetStylebox("normal", "LineEdit", Box(new Color(0.07f, 0.08f, 0.12f), Border));
    t.SetStylebox("focus", "LineEdit", Box(new Color(0.07f, 0.08f, 0.12f), Accent, 8, 2));
    t.SetStylebox("read_only", "LineEdit", Box(new Color(0.09f, 0.10f, 0.14f), Border));
    t.SetColor("font_color", "LineEdit", Text);
    t.SetColor("font_placeholder_color", "LineEdit", Dim.Darkened(0.3f));
    t.SetColor("caret_color", "LineEdit", Accent);
    t.SetColor("selection_color", "LineEdit", new Color(Accent, 0.4f));

    t.SetColor("font_color", "CheckBox", Text);
    t.SetColor("font_hover_color", "CheckBox", Text);
    t.SetColor("font_pressed_color", "CheckBox", Text);

    t.SetTypeVariation(Title, "Label");
    t.SetFontSize("font_size", Title, 52);
    t.SetColor("font_color", Title, Text);

    t.SetTypeVariation(Heading, "Label");
    t.SetFontSize("font_size", Heading, 28);
    t.SetColor("font_color", Heading, Text);

    t.SetTypeVariation(Subtle, "Label");
    t.SetFontSize("font_size", Subtle, 15);
    t.SetColor("font_color", Subtle, Dim);

    t.SetTypeVariation(Mono, "Label");
    t.SetFontSize("font_size", Mono, 44);
    t.SetColor("font_color", Mono, Accent);

    // Sliders: a thin dark track with the accent colour for the filled part.
    var track = Box(new Color(0.07f, 0.08f, 0.12f), Border, 4, 1, 0, 3);
    t.SetStylebox("slider", "HSlider", track);
    t.SetStylebox("grabber_area", "HSlider", Box(Accent, null, 4, 1, 0, 3));
    t.SetStylebox("grabber_area_highlight", "HSlider", Box(Accent.Lightened(0.2f), null, 4, 1, 0, 3));

    t.SetStylebox("separator", "HSeparator", new StyleBoxFlat { BgColor = Border, ContentMarginTop = 1, ContentMarginBottom = 1 });

    return t;
  }

  // ------------------------------------------------------------------ widgets

  /// <summary>A single-line label (never wraps, so it is safe inside a row).</summary>
  public static Label Label(string text, StringName? variation = null, HorizontalAlignment align = HorizontalAlignment.Left)
  {
    var label = new Label { Text = text, HorizontalAlignment = align };

    if (variation != null)
    {
      label.ThemeTypeVariation = variation;
    }

    return label;
  }

  /// <summary>A wrapping paragraph. Only use it where the label gets the full width (a column, not a row).</summary>
  public static Label Paragraph(string text, StringName? variation = null, HorizontalAlignment align = HorizontalAlignment.Left)
  {
    var label = Label(text, variation, align);
    label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    label.CustomMinimumSize = new Vector2(10f, 0f);

    return label;
  }

  public static Button Button(string text, StringName? variation = null, float minWidth = 0f)
  {
    var button = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 44f), FocusMode = Control.FocusModeEnum.All };

    if (variation != null)
    {
      button.ThemeTypeVariation = variation;
    }

    return button;
  }

  public static LineEdit Field(string placeholder = "", int maxLength = 0)
  {
    return new LineEdit { PlaceholderText = placeholder, MaxLength = maxLength, CustomMinimumSize = new Vector2(0f, 44f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
  }

  public static HBoxContainer Row(int separation = 10)
  {
    var row = new HBoxContainer();
    row.AddThemeConstantOverride("separation", separation);

    return row;
  }

  public static VBoxContainer Column(int separation = 10)
  {
    var column = new VBoxContainer();
    column.AddThemeConstantOverride("separation", separation);

    return column;
  }

  public static Control Gap(float height)
  {
    return new Control { CustomMinimumSize = new Vector2(0f, height) };
  }

  /// <summary>A raised row-card, used for list entries (friend rooms, players).</summary>
  public static PanelContainer Card()
  {
    var card = new PanelContainer();
    card.AddThemeStyleboxOverride("panel", Box(PanelRaised, Border, 10, 1, 14, 10));

    return card;
  }

  public static HSeparator Separator()
  {
    return new HSeparator();
  }

  public static void Tint(Control control, Color color)
  {
    control.AddThemeColorOverride("font_color", color);
  }
}
