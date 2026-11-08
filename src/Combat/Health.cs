using System;
using Godot;

namespace Curitibia.Combat;

/// <summary>
/// Hit points as a child-node component. Hp is replicated (a synchronizer owned by the host), but only the host ever
/// changes it: Damage and Reset are host calls, clients just read Hp.
/// </summary>
public partial class Health : Node
{
  [Export] public float MaxHp { get; set; } = 100f;

  /// <summary>Current hit points (replicated from the host).</summary>
  [Export] public float Hp { get; set; } = 100f;

  public bool IsDead => Hp <= 0f;

  /// <summary>Raised on the host when Hp reaches zero.</summary>
  public event Action? Died;

  public void Damage(float amount)
  {
    if (IsDead || amount <= 0f)
    {
      return;
    }

    Hp = Math.Max(0f, Hp - amount);

    if (IsDead)
    {
      Died?.Invoke();
    }
  }

  public void Reset()
  {
    Hp = MaxHp;
  }
}
