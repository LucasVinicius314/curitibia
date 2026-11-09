using Curitibia.Core;
using Curitibia.World;
using Godot;

namespace Curitibia.UI;

/// <summary>
/// The full-screen map (M): everywhere the player has been, to drag around (left or middle button) and zoom (wheel, about the
/// cursor). The cursor's spot and region are read out at the bottom. Space centres on the player; M or Escape closes it.
/// While it is open the mouse is free and the player stands still, but the pause menu stays away.
/// </summary>
public partial class WorldMap : MapSurface
{
  const float MinScale = 0.2f;
  const float MaxScale = 16f;
  const float ZoomStep = 1.25f;

  static readonly Color Dim = new(0.03f, 0.04f, 0.07f, 0.96f);
  static readonly Color Band = new(0.02f, 0.03f, 0.05f, 0.7f);

  Label readout = null!;
  bool dragging;
  int framesWithPlayer;
  bool openedByFlag;

  public bool IsOpen => Visible;

  public override void _Ready()
  {
    Visible = false;
    SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    MouseFilter = MouseFilterEnum.Stop;
    ClipContents = true;
    TextureFilter = TextureFilterEnum.Nearest;
    PixelsPerVoxel = 2f;

    var hint = Ui.Label("Drag to move   Wheel to zoom   Space to centre   M or Esc to close", Ui.Subtle, HorizontalAlignment.Center);
    hint.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
    hint.OffsetTop = 14f;
    hint.MouseFilter = MouseFilterEnum.Ignore;
    AddChild(hint);

    readout = Ui.Label("", null, HorizontalAlignment.Center);
    readout.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
    readout.OffsetTop = -44f;
    readout.OffsetBottom = -14f;
    readout.MouseFilter = MouseFilterEnum.Ignore;
    AddChild(readout);
  }

  public override void _Input(InputEvent @event)
  {
    if (@event.IsActionPressed(Actions.Map))
    {
      if (IsOpen)
      {
        Close();
      }
      else if (HasPlayer && !Player!.Health.IsDead && Input.MouseMode == Input.MouseModeEnum.Captured)
      {
        Open();
      }
      else
      {
        return;
      }

      GetViewport().SetInputAsHandled();

      return;
    }

    if (!IsOpen)
    {
      return;
    }

    if (@event.IsActionPressed(Actions.Pause))
    {
      Close();
      GetViewport().SetInputAsHandled();
    }
    else if (@event is InputEventKey { Pressed: true, PhysicalKeycode: Key.Space } && HasPlayer)
    {
      CenterVoxel = PlayerVoxel();
      GetViewport().SetInputAsHandled();
    }
  }

  void Open()
  {
    CenterVoxel = PlayerVoxel();
    Visible = true;
    dragging = false;
    Input.MouseMode = Input.MouseModeEnum.Visible;
  }

  void Close()
  {
    Visible = false;
    dragging = false;
    Input.MouseMode = Input.MouseModeEnum.Captured;
  }

  public override void _GuiInput(InputEvent @event)
  {
    switch (@event)
    {
      case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Middle } button:
        dragging = button.Pressed;
        break;

      case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } wheel:
        ZoomAt(wheel.Position, ZoomStep);
        break;

      case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } wheel:
        ZoomAt(wheel.Position, 1f / ZoomStep);
        break;

      case InputEventMouseMotion motion:
        if (dragging)
        {
          CenterVoxel -= motion.Relative / PixelsPerVoxel;
        }

        UpdateReadout(motion.Position);
        break;
    }

    AcceptEvent();
  }

  void ZoomAt(Vector2 screen, float factor)
  {
    var before = ToVoxel(screen);

    PixelsPerVoxel = Mathf.Clamp(PixelsPerVoxel * factor, MinScale, MaxScale);

    // Keep the spot under the cursor where it was.
    CenterVoxel += before - ToVoxel(screen);
    UpdateReadout(screen);
  }

  void UpdateReadout(Vector2 screen)
  {
    var voxel = ToVoxel(screen);
    var x = VoxelMath.FloorToInt(voxel.X);
    var z = VoxelMath.FloorToInt(voxel.Y);

    VoxelMath.WorldToChunk(x, z, out var cx, out var cz, out _, out _);

    var explored = Map.MapCache.Instance?.TextureOf(new Vector2I(cx, cz)) != null;
    var biome = explored ? Terrain.Instance?.BiomeAt(new Vector3(VoxelMath.VoxelCenter(x), 0f, VoxelMath.VoxelCenter(z))) : null;

    readout.Text = explored ? $"{x}, {z}   {biome?.Name}" : $"{x}, {z}   Unexplored";
  }

  public override void _Process(double delta)
  {
    // --open-map: open the map a moment after the player exists (for scripted screenshots).
    if (!openedByFlag && DevArgs.Has("open-map") && HasPlayer && ++framesWithPlayer > 240)
    {
      openedByFlag = true;
      Open();
    }

    if (IsOpen)
    {
      QueueRedraw();
    }
  }

  public override void _Draw()
  {
    DrawRect(new Rect2(Vector2.Zero, Size), Dim);

    if (HasPlayer)
    {
      DrawTiles();
      DrawMarkers(Mathf.Clamp(PixelsPerVoxel * 1.6f, 4f, 9f));
    }

    // Bands behind the hint and the readout so they stay legible over any terrain.
    DrawRect(new Rect2(0f, 0f, Size.X, 44f), Band);
    DrawRect(new Rect2(0f, Size.Y - 52f, Size.X, 52f), Band);
  }
}
