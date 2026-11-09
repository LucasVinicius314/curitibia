using System;
using Curitibia.Core;
using Curitibia.Items;
using Curitibia.Player;
using Curitibia.World;
using Godot;

namespace Curitibia.UI;

/// <summary>
/// Shows what the local player is looking at, at the top of the screen: a small card with the block's icon and name. A
/// voxel ray from the eye along the camera (no physics) a few times longer than the mining reach; the text and icon only
/// change when the target block does.
/// </summary>
public partial class LookAtPanel : CenterContainer
{
  /// <summary>How far, in metres, the panel looks.</summary>
  const float Distance = 14f;

  const float IconSize = 24f;

  PlayerController? player;
  PanelContainer card = null!;
  TextureRect icon = null!;
  Label nameLabel = null!;

  Terrain? boundTerrain;
  Func<int, int, int, bool>? solid;

  byte shownBlock;

  public void Bind(PlayerController localPlayer)
  {
    player = localPlayer;
  }

  public override void _Ready()
  {
    // A full-width strip along the top that centres the card; the card itself shows and hides.
    MouseFilter = MouseFilterEnum.Ignore;
    SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
    OffsetTop = 12f;
    OffsetBottom = 44f;

    card = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore };

    var style = new StyleBoxFlat
    {
      BgColor = new Color(Ui.Panel, 0.88f),
      BorderColor = Ui.Border,
      CornerRadiusTopLeft = 7,
      CornerRadiusTopRight = 7,
      CornerRadiusBottomLeft = 7,
      CornerRadiusBottomRight = 7,
      BorderWidthLeft = 1,
      BorderWidthRight = 1,
      BorderWidthTop = 1,
      BorderWidthBottom = 1,
      ContentMarginLeft = 8f,
      ContentMarginRight = 11f,
      ContentMarginTop = 4f,
      ContentMarginBottom = 4f,
    };

    card.AddThemeStyleboxOverride("panel", style);
    AddChild(card);

    var row = Ui.Row(7);

    icon = new TextureRect
    {
      CustomMinimumSize = new Vector2(IconSize, IconSize),
      ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
      StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
      TextureFilter = CanvasItem.TextureFilterEnum.Linear,
      MouseFilter = MouseFilterEnum.Ignore,
    };

    nameLabel = Ui.Label("", null);
    nameLabel.AddThemeFontSizeOverride("font_size", 14);
    nameLabel.VerticalAlignment = VerticalAlignment.Center;

    row.AddChild(icon);
    row.AddChild(nameLabel);
    card.AddChild(row);

    ItemIconRenderer.IconsReady += OnIconsReady;
  }

  public override void _ExitTree()
  {
    ItemIconRenderer.IconsReady -= OnIconsReady;
  }

  void OnIconsReady()
  {
    // Icons for blocks arrive after the first frames: pick up the one for what is shown.
    if (shownBlock != BlockIds.Air)
    {
      icon.Texture = ItemIconRenderer.IconForBlock(shownBlock);
    }
  }

  public override void _Process(double delta)
  {
    var terrain = Terrain.Instance;

    if (player == null || !IsInstanceValid(player) || terrain == null || player.Health.IsDead || Input.MouseMode != Input.MouseModeEnum.Captured)
    {
      card.Visible = false;

      return;
    }

    if (terrain != boundTerrain)
    {
      boundTerrain = terrain;
      solid = terrain.IsSolid; // one delegate per world, not per frame
    }

    var origin = player.EyePosition / VoxelMath.BlockSize;
    var direction = -player.Camera.GlobalTransform.Basis.Z;

    var hit = VoxelRaycast.Cast(
      origin.X, origin.Y, origin.Z,
      direction.X, direction.Y, direction.Z,
      Distance / VoxelMath.BlockSize,
      solid!,
      out var hx, out var hy, out var hz,
      out _, out _, out _);

    var block = hit ? terrain.GetBlock(hx, hy, hz) : BlockIds.Air;

    if (block == BlockIds.Air)
    {
      card.Visible = false;

      return;
    }

    card.Visible = true;

    if (block == shownBlock)
    {
      return;
    }

    shownBlock = block;
    nameLabel.Text = BlockIds.DisplayName(block);
    icon.Texture = ItemIconRenderer.IconForBlock(block);
  }
}
