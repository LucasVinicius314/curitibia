using Curitibia.World;
using Godot;

namespace Curitibia.UI;

/// <summary>
/// The "entering a region" title: the region's name large and tinted in the upper third of the screen, with a small
/// "Entering" line above it. Fades in, holds, fades out. Built in code; the HUD calls Announce when the player's biome
/// changes.
/// </summary>
public partial class RegionBanner : VBoxContainer
{
  static readonly Color fallback = new(0.9f, 0.9f, 0.9f);

  static readonly Color[] colors =
  {
    new(0.60f, 0.88f, 0.42f), // Plains
    new(0.32f, 0.78f, 0.46f), // Forest
    new(0.98f, 0.82f, 0.48f), // Desert
    new(0.80f, 0.92f, 1.00f), // Snow
    new(0.55f, 0.84f, 0.58f), // Hills
    new(0.74f, 0.78f, 0.92f), // Mountains
  };

  Label title = null!;
  Tween? tween;

  public override void _Ready()
  {
    MouseFilter = MouseFilterEnum.Ignore;
    SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
    OffsetTop = 130f;
    Modulate = new Color(1f, 1f, 1f, 0f);
    AddThemeConstantOverride("separation", 0);

    var entering = Ui.Label("ENTERING", null, HorizontalAlignment.Center);
    entering.AddThemeFontSizeOverride("font_size", 13);
    Ui.Tint(entering, Ui.Dim);
    AddChild(entering);

    title = Ui.Label("", null, HorizontalAlignment.Center);
    title.AddThemeFontSizeOverride("font_size", 38);
    title.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
    title.AddThemeConstantOverride("outline_size", 8);
    AddChild(title);

    foreach (var child in GetChildren())
    {
      if (child is Control c)
      {
        c.MouseFilter = MouseFilterEnum.Ignore;
      }
    }
  }

  public static Color ColorOf(BiomeId id)
  {
    return (int)id < colors.Length ? colors[(int)id] : fallback;
  }

  public void Announce(BiomeDef biome)
  {
    Announce(biome.Name, ColorOf(biome.Id));
  }

  public void Announce(string name, Color color)
  {
    title.Text = name;
    Ui.Tint(title, color);

    tween?.Kill();
    tween = CreateTween();
    tween.TweenProperty(this, "modulate:a", 1f, 0.6f);
    tween.TweenInterval(3f);
    tween.TweenProperty(this, "modulate:a", 0f, 1.4f);
  }
}
