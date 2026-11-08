using Curitibia.Core;
using Godot;

namespace Curitibia.Combat;

/// <summary>
/// A MultiplayerSpawner for projectiles with a custom spawn function, so every peer builds the bolt from the same data.
/// The host calls Fire; clients just receive the spawn.
/// </summary>
public partial class ProjectileSpawner : MultiplayerSpawner
{
  [Export] public PackedScene ProjectileScene { get; set; } = null!;

  public static ProjectileSpawner? Instance { get; private set; }

  public override void _EnterTree()
  {
    Instance = this;
  }

  public override void _ExitTree()
  {
    if (Instance == this)
    {
      Instance = null;
    }
  }

  public override void _Ready()
  {
    SpawnFunction = Callable.From<Variant, Node>(Build);
  }

  /// <summary>Host only.</summary>
  public void Fire(Vector3 position, Vector3 direction, Team team)
  {
    Spawn(new Godot.Collections.Dictionary
    {
      ["pos"] = position,
      ["dir"] = direction.Normalized(),
      ["team"] = (int)team,
    });
  }

  Node Build(Variant data)
  {
    var info = data.AsGodotDictionary();
    var projectile = ProjectileScene.Instantiate<Projectile>();

    projectile.Position = info["pos"].AsVector3();
    projectile.Direction = info["dir"].AsVector3();
    projectile.SourceTeam = (Team)info["team"].AsInt32();

    return projectile;
  }
}
