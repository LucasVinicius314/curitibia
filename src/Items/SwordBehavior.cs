using Curitibia.Combat;
using Curitibia.Core;
using Curitibia.Enemies;
using Godot;

namespace Curitibia.Items;

/// <summary>
/// A melee swing: hurts every enemy inside a cone in front of the eye. Decided on the host from where the owner's body and
/// view are there, so a client cannot hit farther or faster than the numbers allow.
/// </summary>
[GlobalClass]
public partial class SwordBehavior : ItemBehavior
{
  [Export] public float Damage { get; set; } = 25f;
  [Export] public float Knockback { get; set; } = 7f;

  /// <summary>From the eye, metres.</summary>
  [Export] public float Reach { get; set; } = 2.8f;

  /// <summary>Cosine of the half-angle of the swing cone (0.45 is about 63 degrees).</summary>
  [Export] public float MinAlignment { get; set; } = 0.45f;

  public override bool Announces => true;

  public override bool RepeatsWhileHeld => true;

  public override void HostUse(ItemUseContext context, bool primary)
  {
    if (!primary)
    {
      return;
    }

    var player = context.Player;
    var eye = player.EyePosition;
    var look = -player.CameraPivot.GlobalTransform.Basis.Z;

    foreach (var enemy in Enemy.All.ToArray())
    {
      if (!GodotObject.IsInstanceValid(enemy) || enemy.Health.IsDead)
      {
        continue;
      }

      var toEnemy = enemy.GlobalPosition + Vector3.Up * 0.9f - eye;
      var distance = toEnemy.Length();

      if (distance > Reach || distance < 0.01f || look.Dot(toEnemy / distance) < MinAlignment)
      {
        continue;
      }

      var flat = new Vector3(toEnemy.X, 0f, toEnemy.Z);
      enemy.TakeDamage(new DamageInfo(Damage, Team.Players, flat.LengthSquared() < 0.0001f ? look : flat.Normalized(), Knockback));
    }
  }
}
