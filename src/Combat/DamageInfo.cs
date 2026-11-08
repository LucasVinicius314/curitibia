using Curitibia.Core;
using Godot;

namespace Curitibia.Combat;

/// <summary>Everything a hit carries. One struct so weapons never inspect concrete target types.</summary>
public readonly record struct DamageInfo(float Amount, Team SourceTeam, Vector3 Direction, float Knockback = 0f);

/// <summary>Anything that can be hurt. Damage is only ever applied on the host.</summary>
public interface IDamageable
{
  Team Team { get; }

  void TakeDamage(DamageInfo info);
}
