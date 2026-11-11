using Curitibia.Core;
using Curitibia.World;
using Godot;

namespace Curitibia.Player;

/// <summary>
/// Targeting for block tools: a voxel ray walk from the eye along the camera (no physics needed, and it gives the face
/// normal for free) and a translucent highlight on the targeted voxel while the held item wants it. It does no input:
/// the held item's behavior calls Hit / PlaceTarget, which go through Terrain.RequestHit / RequestSetBlock, where the network
/// decides who applies them.
/// </summary>
public partial class BlockInteractor : Node
{
  const float Reach = 6f;

  [Export] public PlayerController Player { get; set; } = null!;
  [Export] public Camera3D Camera { get; set; } = null!;

  static readonly BoxMesh highlightMesh = new() { Size = Vector3.One * VoxelMath.BlockSize * 1.012f };

  static readonly StandardMaterial3D highlightMaterial = new()
  {
    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    AlbedoColor = new Color(1f, 1f, 1f, 0.18f),
    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
  };

  MeshInstance3D? highlight;
  bool hasTarget;
  Vector3I targetVoxel;
  Vector3I targetNormal;

  public override void _Ready()
  {
    if (!Player.IsLocal)
    {
      SetProcess(false);

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
  }

  public override void _Process(double delta)
  {
    var terrain = Terrain.Instance;
    var wanted = Player.Inventory.Held?.Behavior?.WantsBlockTarget == true && !Player.Health.IsDead;

    hasTarget = false;

    if (terrain != null && wanted && TryTarget(terrain, out targetVoxel, out targetNormal))
    {
      hasTarget = true;
    }

    if (highlight == null)
    {
      return;
    }

    highlight.Visible = hasTarget;

    if (hasTarget)
    {
      highlight.GlobalPosition = new Vector3(VoxelMath.VoxelCenter(targetVoxel.X), VoxelMath.VoxelCenter(targetVoxel.Y), VoxelMath.VoxelCenter(targetVoxel.Z));
    }
  }

  /// <summary>
  /// One swing at the block under the crosshair with a tool of this power that deals this much damage. The damage belongs to the
  /// block and is kept by the world (the host, which tells everyone the cracks and breaks the block when it is spent): this only
  /// asks. A tool too weak for the block does nothing to it.
  /// </summary>
  public void Hit(int power, float damage)
  {
    if (hasTarget)
    {
      Terrain.Instance?.RequestHit(targetVoxel, power, damage);
    }
  }

  /// <summary>Places a block against the face under the crosshair, unless it would overlap the player; false when nothing was placed.</summary>
  public bool PlaceTarget(byte block)
  {
    if (!hasTarget || targetNormal == Vector3I.Zero)
    {
      return false;
    }

    var target = targetVoxel + targetNormal;

    if (!VoxelMath.InWorldHeight(target.Y) || OverlapsPlayer(target))
    {
      return false;
    }

    Terrain.Instance?.RequestSetBlock(target, block);

    return true;
  }

  bool TryTarget(Terrain terrain, out Vector3I voxel, out Vector3I normal)
  {
    // From the eye, along the camera's direction: in third person the camera itself sits behind the player.
    var origin = Player.EyePosition / VoxelMath.BlockSize;
    var direction = -Camera.GlobalTransform.Basis.Z;

    var hit = VoxelRaycast.Cast(
      origin.X, origin.Y, origin.Z,
      direction.X, direction.Y, direction.Z,
      Reach / VoxelMath.BlockSize,
      terrain.IsTargetable,
      out var hx, out var hy, out var hz,
      out var nx, out var ny, out var nz);

    voxel = new Vector3I(hx, hy, hz);
    normal = new Vector3I(nx, ny, nz);

    return hit;
  }

  bool OverlapsPlayer(Vector3I voxel)
  {
    var cell = new Aabb(new Vector3(voxel.X, voxel.Y, voxel.Z) * VoxelMath.BlockSize, Vector3.One * VoxelMath.BlockSize);

    return cell.Intersects(Player.BodyBounds);
  }
}
