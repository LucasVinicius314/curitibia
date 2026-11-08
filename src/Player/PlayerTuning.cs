using Godot;

namespace Curitibia.Player;

/// <summary>Player movement numbers as data (data/player_tuning.tres). Initial values come from the Unity project.</summary>
[GlobalClass]
public partial class PlayerTuning : Resource
{
  /// <summary>Walk speed in m/s (blocks are half a metre now, so this feels faster than the number suggests).</summary>
  [Export] public float Speed { get; set; } = 3.5f;

  /// <summary>Walk speed is multiplied by this while the sprint key is held.</summary>
  [Export] public float SprintMultiplier { get; set; } = 1.5f;

  /// <summary>Noclip flight speed in m/s (sprint triples it).</summary>
  [Export] public float FlySpeed { get; set; } = 12f;

  /// <summary>Third-person camera distance behind the eye, metres.</summary>
  [Export] public float ThirdPersonDistance { get; set; } = 3.2f;

  /// <summary>How fast horizontal velocity follows the input, per second (frame-rate independent smoothing).</summary>
  [Export] public float Responsiveness { get; set; } = 12f;

  /// <summary>Same, while airborne (less control in the air).</summary>
  [Export] public float AirResponsiveness { get; set; } = 3f;

  /// <summary>Initial upward velocity of a jump: 5.5 m/s peaks at about 1.5 m, three half-metre blocks.</summary>
  [Export] public float JumpVelocity { get; set; } = 5.5f;

  /// <summary>Tallest ledge walked up without jumping (one half-metre block).</summary>
  [Export] public float StepHeight { get; set; } = 0.55f;

  /// <summary>Radians per pixel of mouse movement.</summary>
  [Export] public float MouseSensitivity { get; set; } = 0.0025f;

  /// <summary>Radians per second at full right-stick deflection.</summary>
  [Export] public float StickSensitivity { get; set; } = 3.2f;

  /// <summary>Pitch limit, radians (a hair under 90 degrees avoids flipping at the poles).</summary>
  [Export] public float MaxPitch { get; set; } = 1.54f;

  /// <summary>Below this height the player is put back on the surface.</summary>
  [Export] public float KillHeight { get; set; } = -30f;
}
