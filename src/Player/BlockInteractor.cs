using System;
using Curitibia.Core;
using Curitibia.World;
using Godot;

namespace Curitibia.Player;

/// <summary>
/// Breaking and placing blocks from the camera: a voxel ray walk (no physics needed, and it gives the face normal for
/// free), a wireframe-ish highlight on the targeted voxel, and the selected block. Edits go through
/// Terrain.RequestSetBlock, which is where the network decides who applies them.
/// </summary>
public partial class BlockInteractor : Node
{
  const float Reach = 6f;

  [Export] public PlayerController Player { get; set; } = null!;
  [Export] public Camera3D Camera { get; set; } = null!;

  static readonly BoxMesh highlightMesh = new() { Size = Vector3.One * 1.006f };

  static readonly StandardMaterial3D highlightMaterial = new()
  {
    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    AlbedoColor = new Color(1f, 1f, 1f, 0.18f),
    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
  };

  MeshInstance3D? highlight;
  int selected;

  /// <summary>The block that will be placed.</summary>
  public byte SelectedBlock => BlockIds.Placeable[selected];

  /// <summary>Raised when the selected block changes (the HUD listens).</summary>
  public event Action<byte>? SelectionChanged;

  public override void _Ready()
  {
    if (!Player.IsLocal)
    {
      SetProcess(false);
      SetProcessUnhandledInput(false);
      return;
    }

    highlight = new MeshInstance3D
    {
      Mesh = highlightMesh,
      MaterialOverride = highlightMaterial,
      TopLevel = true,
      Visible = false,
      CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    AddChild(highlight);
    CallDeferred(MethodName.AnnounceSelection);
  }

  void AnnounceSelection()
  {
    SelectionChanged?.Invoke(SelectedBlock);
  }

  public override void _UnhandledInput(InputEvent @event)
  {
    if (Input.MouseMode != Input.MouseModeEnum.Captured)
    {
      return;
    }

    if (@event.IsActionPressed(Actions.NextBlock))
    {
      Select(selected + 1);
    }
    else if (@event.IsActionPressed(Actions.PrevBlock))
    {
      Select(selected - 1);
    }
    else if (@event is InputEventKey { Pressed: true, Echo: false } key && key.PhysicalKeycode >= Key.Key1 && key.PhysicalKeycode < Key.Key1 + BlockIds.Placeable.Length)
    {
      Select((int)(key.PhysicalKeycode - Key.Key1));
    }
  }

  void Select(int index)
  {
    var count = BlockIds.Placeable.Length;
    selected = ((index % count) + count) % count;
    SelectionChanged?.Invoke(SelectedBlock);
  }

  public override void _Process(double delta)
  {
    var terrain = Terrain.Instance;

    if (terrain == null || highlight == null || Player.Health.IsDead)
    {
      return;
    }

    if (!TryTarget(terrain, out var voxel, out var normal))
    {
      highlight.Visible = false;
      return;
    }

    highlight.Visible = true;
    highlight.GlobalPosition = new Vector3(voxel.X + 0.5f, voxel.Y + 0.5f, voxel.Z + 0.5f);

    if (Input.MouseMode != Input.MouseModeEnum.Captured)
    {
      return;
    }

    if (Input.IsActionJustPressed(Actions.BreakBlock))
    {
      terrain.RequestSetBlock(voxel, BlockIds.Air);
    }
    else if (Input.IsActionJustPressed(Actions.PlaceBlock) && normal != Vector3I.Zero)
    {
      var target = voxel + normal;

      if (VoxelMath.InWorldHeight(target.Y) && !OverlapsPlayer(target))
      {
        terrain.RequestSetBlock(target, SelectedBlock);
      }
    }
  }

  bool TryTarget(Terrain terrain, out Vector3I voxel, out Vector3I normal)
  {
    var origin = Camera.GlobalPosition;
    var direction = -Camera.GlobalTransform.Basis.Z;

    var hit = VoxelRaycast.Cast(
      origin.X, origin.Y, origin.Z,
      direction.X, direction.Y, direction.Z,
      Reach,
      terrain.IsSolid,
      out var hx, out var hy, out var hz,
      out var nx, out var ny, out var nz);

    voxel = new Vector3I(hx, hy, hz);
    normal = new Vector3I(nx, ny, nz);

    return hit;
  }

  bool OverlapsPlayer(Vector3I voxel)
  {
    var cell = new Aabb(new Vector3(voxel.X, voxel.Y, voxel.Z), Vector3.One);

    return cell.Intersects(Player.BodyBounds);
  }
}
