using Curitibia.Core;
using Curitibia.Enemies;
using Curitibia.Map;
using Curitibia.Player;
using Godot;

namespace Curitibia.UI;

/// <summary>
/// What the minimap and the full map share: a view of the explored map centred on a spot (in voxel columns) at a scale (screen
/// pixels per voxel column), the tiles drawn with nearest filtering so the pixels stay crisp, and the markers for you, the
/// other players and the enemies. North (-Z) is up.
/// </summary>
public abstract partial class MapSurface : Control
{
  static readonly Color EnemyColor = new(1f, 0.28f, 0.25f);
  static readonly Color PlayerColor = new(0.35f, 0.85f, 1f);
  static readonly Color SelfColor = new(1f, 1f, 1f);
  static readonly Color SelfOutline = new(0f, 0f, 0f, 0.85f);

  // Reused every draw: nothing is allocated per frame.
  readonly Vector2[] arrow = new Vector2[3];
  readonly Vector2[] arrowOutline = new Vector2[3];

  /// <summary>True while drawing inside a rotated frame (the minimap turns with the player): positions are then relative to the middle.</summary>
  bool rotatedFrame;

  protected PlayerController? Player { get; private set; }

  /// <summary>The map spot at the middle of the view, in voxel columns.</summary>
  public Vector2 CenterVoxel { get; set; }

  /// <summary>Screen pixels per voxel column.</summary>
  public float PixelsPerVoxel { get; set; } = 1f;

  public void Bind(PlayerController localPlayer)
  {
    Player = localPlayer;
  }

  protected bool HasPlayer => Player != null && IsInstanceValid(Player);

  /// <summary>The player's spot on the map (voxel columns).</summary>
  protected Vector2 PlayerVoxel()
  {
    var p = Player!.GlobalPosition;

    return new Vector2(p.X, p.Z) / VoxelMath.BlockSize;
  }

  protected Vector2 ToScreen(Vector2 voxel)
  {
    return (voxel - CenterVoxel) * PixelsPerVoxel + (rotatedFrame ? Vector2.Zero : Size * 0.5f);
  }

  /// <summary>Turns everything drawn until <see cref="EndRotatedFrame"/> by <paramref name="angle"/> about the middle of the view.</summary>
  protected void BeginRotatedFrame(float angle)
  {
    rotatedFrame = true;
    DrawSetTransform(Size * 0.5f, angle, Vector2.One);
  }

  protected void EndRotatedFrame()
  {
    rotatedFrame = false;
    DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
  }

  protected Vector2 ToVoxel(Vector2 screen)
  {
    return (screen - Size * 0.5f) / PixelsPerVoxel + CenterVoxel;
  }

  /// <summary>Draws every explored tile that is in view.</summary>
  protected void DrawTiles()
  {
    var cache = MapCache.Instance;

    if (cache == null)
    {
      return;
    }

    // A turned square view reaches as far as its diagonal.
    var half = (rotatedFrame ? new Vector2(Size.Length(), Size.Length()) : Size) / (2f * PixelsPerVoxel);
    var x0 = VoxelMath.FloorDiv(VoxelMath.FloorToInt(CenterVoxel.X - half.X), MapTileBuilder.Size);
    var x1 = VoxelMath.FloorDiv(VoxelMath.FloorToInt(CenterVoxel.X + half.X), MapTileBuilder.Size);
    var z0 = VoxelMath.FloorDiv(VoxelMath.FloorToInt(CenterVoxel.Y - half.Y), MapTileBuilder.Size);
    var z1 = VoxelMath.FloorDiv(VoxelMath.FloorToInt(CenterVoxel.Y + half.Y), MapTileBuilder.Size);
    var tileSize = new Vector2(MapTileBuilder.Size, MapTileBuilder.Size) * PixelsPerVoxel;

    for (int cz = z0; cz <= z1; cz++)
    {
      for (int cx = x0; cx <= x1; cx++)
      {
        var texture = cache.TextureOf(new Vector2I(cx, cz));

        if (texture != null)
        {
          DrawTextureRect(texture, new Rect2(ToScreen(new Vector2(cx, cz) * MapTileBuilder.Size), tileSize), false);
        }
      }
    }
  }

  /// <summary>Enemies and other players as dots, you as an arrow pointing the way you face.</summary>
  protected void DrawMarkers(float dotRadius)
  {
    foreach (var enemy in Enemy.All)
    {
      if (IsInstanceValid(enemy) && !enemy.Health.IsDead)
      {
        DrawCircle(ToScreen(new Vector2(enemy.GlobalPosition.X, enemy.GlobalPosition.Z) / VoxelMath.BlockSize), dotRadius, EnemyColor);
      }
    }

    foreach (var other in PlayerController.All)
    {
      if (other != Player && IsInstanceValid(other) && !other.Health.IsDead)
      {
        DrawCircle(ToScreen(new Vector2(other.GlobalPosition.X, other.GlobalPosition.Z) / VoxelMath.BlockSize), dotRadius, PlayerColor);
      }
    }

    if (!HasPlayer)
    {
      return;
    }

    // The arrow points along the player's facing: forward is -Z, which is up the map.
    var facing = -Player!.CameraPivot.GlobalTransform.Basis.Z;
    var direction = new Vector2(facing.X, facing.Z).Normalized();
    var side = new Vector2(-direction.Y, direction.X);
    var centre = ToScreen(PlayerVoxel());
    var size = dotRadius * 2.4f;

    SetArrow(arrowOutline, centre, direction, side, size + 2f);
    DrawColoredPolygon(arrowOutline, SelfOutline);

    SetArrow(arrow, centre, direction, side, size);
    DrawColoredPolygon(arrow, SelfColor);
  }

  static void SetArrow(Vector2[] points, Vector2 centre, Vector2 direction, Vector2 side, float size)
  {
    points[0] = centre + direction * size;
    points[1] = centre - direction * size * 0.7f + side * size * 0.75f;
    points[2] = centre - direction * size * 0.7f - side * size * 0.75f;
  }
}
