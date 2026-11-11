using Curitibia.Core;
using Curitibia.Player;
using Curitibia.World;
using Godot;

namespace Curitibia.UI;

/// <summary>
/// The map in the corner of the screen: a square view of the explored map around the player that turns with them (what is
/// ahead is up, a small N marks north), with the region name and the player's coordinates underneath. Zoom with = and -.
/// </summary>
public partial class MinimapPanel : VBoxContainer
{
  const float ViewSize = 176f;

  Minimap view = null!;
  Label info = null!;
  PlayerController? player;
  double timer;

  public void Bind(PlayerController localPlayer)
  {
    player = localPlayer;
    view.Bind(localPlayer);
  }

  public override void _Ready()
  {
    MouseFilter = MouseFilterEnum.Ignore;
    SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
    OffsetLeft = -(ViewSize + 16f);
    OffsetRight = -16f;
    OffsetTop = 16f;
    AddThemeConstantOverride("separation", 3);

    view = new Minimap { CustomMinimumSize = new Vector2(ViewSize, ViewSize) };
    AddChild(view);

    info = Ui.Label("", null, HorizontalAlignment.Center);
    info.AddThemeFontSizeOverride("font_size", 12);
    info.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
    info.AddThemeConstantOverride("outline_size", 5);
    info.MouseFilter = MouseFilterEnum.Ignore;
    AddChild(info);
  }

  public override void _Process(double delta)
  {
    timer += delta;

    if (timer < 0.2 || player == null || !IsInstanceValid(player))
    {
      return;
    }

    timer = 0;

    var p = player.GlobalPosition;
    var biome = Terrain.Instance?.UndergroundAt(p) ?? Terrain.Instance?.BiomeAt(p)?.Name;

    info.Text = $"{VoxelMath.WorldToVoxel(p.X)}, {VoxelMath.DisplayY(VoxelMath.WorldToVoxel(p.Y))}, {VoxelMath.WorldToVoxel(p.Z)}"
      + (biome != null ? $"\n{biome}" : "");
  }
}

/// <summary>The square map view itself.</summary>
public partial class Minimap : MapSurface
{
  /// <summary>Screen pixels per voxel column at each zoom step (a voxel column is half a metre).</summary>
  static readonly float[] zoomSteps = { 0.35f, 0.5f, 0.75f, 1f, 1.5f, 2f, 3f, 4f };

  static readonly Color Background = new(0.05f, 0.06f, 0.09f, 0.92f);
  static readonly Color Border = new(0.22f, 0.26f, 0.35f);

  int zoom = 1; // 0.5 pixel per voxel column: about 176 m across

  public override void _Ready()
  {
    MouseFilter = MouseFilterEnum.Ignore;
    ClipContents = true;
    TextureFilter = TextureFilterEnum.Nearest;
    PixelsPerVoxel = zoomSteps[zoom];
  }

  public override void _Input(InputEvent @event)
  {
    if (!Visible || Input.MouseMode != Input.MouseModeEnum.Captured)
    {
      return;
    }

    if (@event.IsActionPressed(Actions.MapZoomIn))
    {
      zoom = Mathf.Min(zoom + 1, zoomSteps.Length - 1);
    }
    else if (@event.IsActionPressed(Actions.MapZoomOut))
    {
      zoom = Mathf.Max(zoom - 1, 0);
    }
    else
    {
      return;
    }

    PixelsPerVoxel = zoomSteps[zoom];
  }

  public override void _Process(double delta)
  {
    if (HasPlayer)
    {
      CenterVoxel = PlayerVoxel();
    }

    QueueRedraw();
  }

  public override void _Draw()
  {
    DrawRect(new Rect2(Vector2.Zero, Size), Background);

    if (HasPlayer)
    {
      // Turn the map so the way the player faces is up: the angle that carries the facing direction onto "up".
      var facing = -Player!.CameraPivot.GlobalTransform.Basis.Z;
      var angle = -Mathf.Atan2(facing.X, -facing.Z);

      BeginRotatedFrame(angle);
      DrawTiles();
      DrawMarkers(3.2f);
      EndRotatedFrame();

      DrawNorth(angle);
    }

    DrawRect(new Rect2(Vector2.Zero, Size), Border, false, 2f);
  }

  /// <summary>An "N" on the edge of the view, in the direction north has been turned to.</summary>
  void DrawNorth(float angle)
  {
    var direction = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
    var reach = (Size.X * 0.5f - 11f) / Mathf.Max(Mathf.Abs(direction.X), Mathf.Abs(direction.Y));
    var centre = Size * 0.5f + direction * reach;

    DrawCircle(centre, 8.5f, new Color(0.05f, 0.06f, 0.09f, 0.85f));
    DrawString(ThemeDB.FallbackFont, centre + new Vector2(-8f, 4.5f), "N", HorizontalAlignment.Center, 16f, 12, new Color(1f, 0.45f, 0.4f));
  }
}
