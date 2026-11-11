using System.Collections.Generic;
using Curitibia.Core;
using Godot;

namespace Curitibia.World;

/// <summary>
/// Draws the cracks of every damaged block in the world, whoever hit it: one translucent cube over each cracked voxel, its
/// material the crack picture of the block's stage. It follows the terrain's CrackChanged events (the host's own, and the ones the
/// host sends to the clients) and removes a block's cracks when the block changes or breaks. Nothing is created per frame:
/// overlays come and go with events, and the materials are shared.
/// </summary>
public partial class BlockCracks : Node3D
{
  static readonly BoxMesh mesh = new() { Size = Vector3.One * VoxelMath.BlockSize * 1.006f };

  readonly Dictionary<Vector3I, MeshInstance3D> overlays = new();
  Terrain? terrain;

  public override void _Ready()
  {
    TopLevel = true;
    terrain = Terrain.Instance;

    if (terrain != null)
    {
      terrain.CrackChanged += Show;
      terrain.BlockChanged += OnBlockChanged;
    }
  }

  public override void _ExitTree()
  {
    if (terrain != null)
    {
      terrain.CrackChanged -= Show;
      terrain.BlockChanged -= OnBlockChanged;
    }
  }

  void OnBlockChanged(Vector3I voxel, byte id)
  {
    Show(voxel, 0);
  }

  /// <summary>Shows a block's cracks at a stage (1 to <see cref="BreakCracks.Stages"/>), or removes them (0).</summary>
  void Show(Vector3I voxel, int stage)
  {
    overlays.TryGetValue(voxel, out var overlay);

    if (stage <= 0)
    {
      if (overlay != null)
      {
        overlay.QueueFree();
        overlays.Remove(voxel);
      }

      return;
    }

    if (overlay == null)
    {
      overlay = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
      AddChild(overlay);
      overlay.GlobalPosition = new Vector3(VoxelMath.VoxelCenter(voxel.X), VoxelMath.VoxelCenter(voxel.Y), VoxelMath.VoxelCenter(voxel.Z));
      overlays[voxel] = overlay;
    }

    overlay.MaterialOverride = BreakCracks.Stage(stage);
  }
}
