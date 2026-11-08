namespace Curitibia.Core;

/// <summary>Collision layers and masks. Keep in step with [layer_names] in project.godot.</summary>
public static class GameLayers
{
  public const uint World = 1u << 0;      // layer 1
  public const uint Player = 1u << 1;     // layer 2
  public const uint Enemy = 1u << 2;      // layer 3
  public const uint Projectile = 1u << 3; // layer 4

  /// <summary>What a body walking on the terrain collides with.</summary>
  public const uint WalkerMask = World;

  /// <summary>What an enemy's line of sight can hit (other enemies do not block it).</summary>
  public const uint SightMask = World | Player;
}
