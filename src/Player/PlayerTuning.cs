using Godot;

namespace Curitibia.Player;

/// <summary>Player movement numbers as data (data/player_tuning.tres). Initial values come from the Unity project.</summary>
[GlobalClass]
public partial class PlayerTuning : Resource
{
  /// <summary>Walk speed in m/s (Unity: 5).</summary>
  [Export] public float Speed { get; set; } = 5f;

  /// <summary>How fast horizontal velocity follows the input, per second (frame-rate independent smoothing).</summary>
  [Export] public float Responsiveness { get; set; } = 12f;

  /// <summary>Same, while airborne (less control in the air).</summary>
  [Export] public float AirResponsiveness { get; set; } = 3f;

  /// <summary>Initial upward velocity of a jump: 5.5 m/s clears one block (1.0) with a little to spare.</summary>
  [Export] public float JumpVelocity { get; set; } = 5.5f;

  /// <summary>Tallest ledge walked up without jumping (Unity: 1, exactly one block).</summary>
  [Export] public float StepHeight { get; set; } = 1.05f;

  /// <summary>Radians per pixel of mouse movement.</summary>
  [Export] public float MouseSensitivity { get; set; } = 0.0025f;

  /// <summary>Radians per second at full right-stick deflection.</summary>
  [Export] public float StickSensitivity { get; set; } = 3.2f;

  /// <summary>Pitch limit, radians (a hair under 90 degrees avoids flipping at the poles).</summary>
  [Export] public float MaxPitch { get; set; } = 1.54f;

  /// <summary>Below this height the player is put back on the surface.</summary>
  [Export] public float KillHeight { get; set; } = -30f;
}
