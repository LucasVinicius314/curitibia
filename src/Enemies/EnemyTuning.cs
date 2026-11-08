using Godot;

namespace Curitibia.Enemies;

/// <summary>Enemy numbers as data (data/enemy_tuning.tres). Initial values come from the Unity enemy script and prefab.</summary>
[GlobalClass]
public partial class EnemyTuning : Resource
{
  /// <summary>Chase speed in m/s (the Unity agent used 2; a little more makes it a threat).</summary>
  [Export] public float Speed { get; set; } = 2.6f;

  /// <summary>Wander speed as a fraction of Speed.</summary>
  [Export] public float WanderSpeedFactor { get; set; } = 0.45f;

  [Export] public float JumpVelocity { get; set; } = 5.5f;

  /// <summary>Longest distance at which a player can be seen (Unity: 13).</summary>
  [Export] public float VisionRange { get; set; } = 13f;

  /// <summary>Half-angle of the view cone in degrees (Unity: 70, so a 140 degree cone).</summary>
  [Export] public float FieldOfViewHalfAngle { get; set; } = 70f;

  /// <summary>A player this close is noticed even outside the cone (Unity: 3).</summary>
  [Export] public float AwarenessRadius { get; set; } = 3f;

  /// <summary>How long a lost target is still chased toward its last known position (Unity: 3).</summary>
  [Export] public float MemorySeconds { get; set; } = 3f;

  /// <summary>Seconds between decisions (Unity: 0.2).</summary>
  [Export] public float ThinkInterval { get; set; } = 0.2f;

  /// <summary>Stops approaching and just shoots inside this distance.</summary>
  [Export] public float HoldDistance { get; set; } = 6f;

  [Export] public float ShootRange { get; set; } = 12f;
  [Export] public float ShootCooldown { get; set; } = 1.6f;

  /// <summary>Random aim error in degrees.</summary>
  [Export] public float AimError { get; set; } = 3f;

  /// <summary>Turn rate (Unity slerped at 6 per second).</summary>
  [Export] public float TurnSpeed { get; set; } = 6f;

  /// <summary>Wander picks a spot within this many blocks.</summary>
  [Export] public int WanderRadius { get; set; } = 8;
}
