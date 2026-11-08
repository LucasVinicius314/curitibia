using Curitibia.Core;
using Godot;

namespace Curitibia.Combat;

/// <summary>
/// A straight-flying bolt. Every peer moves it the same way from the spawn data (position, direction, speed), so nothing
/// is synchronised; only the host tests for hits and removes it, and the spawner replicates the removal.
/// </summary>
public partial class Projectile : Node3D
{
  [Export] public float Speed { get; set; } = 14f;
  [Export] public float Lifetime { get; set; } = 5f;
  [Export] public float Damage { get; set; } = 12f;
  [Export] public float Knockback { get; set; } = 4f;

  public Vector3 Direction { get; set; } = Vector3.Forward;
  public Team SourceTeam { get; set; } = Team.Neutral;

  readonly PhysicsRayQueryParameters3D query = new() { CollisionMask = GameLayers.World | GameLayers.Player };

  float life;

  public override void _PhysicsProcess(double delta)
  {
    var dt = (float)delta;
    var step = Direction * Speed * dt;

    if (Multiplayer.IsServer())
    {
      query.From = GlobalPosition;
      query.To = GlobalPosition + step;

      var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

      if (hit.Count > 0)
      {
        if (hit["collider"].AsGodotObject() is IDamageable target && target.Team.IsHostileTo(SourceTeam))
        {
          target.TakeDamage(new DamageInfo(Damage, SourceTeam, Direction, Knockback));
        }

        QueueFree();
        return;
      }
    }

    GlobalPosition += step;
    life += dt;

    if (life >= Lifetime && Multiplayer.IsServer())
    {
      QueueFree();
    }
  }
}
