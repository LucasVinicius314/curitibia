using Godot;

namespace Curitibia.Items;

/// <summary>How an item moves when it is used.</summary>
public enum SwingStyle
{
  /// <summary>A wide slash from the left across to the right, the blade turning flat.</summary>
  Sweep,

  /// <summary>An overhead chop, down and forward.</summary>
  Chop,

  /// <summary>A real one-handed cut along an oval around the shoulder, high left to low right, the blade leading (see SlashSwing).</summary>
  Slash,
}

/// <summary>
/// How an item looks: built in code or from a scene by a subclass. The same model is shown in the hand (on the body and as
/// the first-person view) and rendered once into the hotbar icon.
/// </summary>
[GlobalClass]
public partial class ItemModel : Resource
{
  /// <summary>Tilt of the model while held and idle (the tip of a blade raised, a block level).</summary>
  [Export] public float RestPitchDegrees { get; set; }

  /// <summary>Roll of the model around its own forward axis while held (negative = to the right).</summary>
  [Export] public float RestRollDegrees { get; set; }

  [Export] public SwingStyle Swing { get; set; }

  /// <summary>How big the use swing is (1 = a full sword chop).</summary>
  [Export] public float SwingAmount { get; set; } = 1f;

  /// <summary>Grip to tip in metres, for items that swing as a blade (Slash).</summary>
  public virtual float BladeLength => 0.9f;

  /// <summary>Orientation used for the icon render.</summary>
  [Export] public Vector3 IconRotationDegrees { get; set; }

  /// <summary>Builds a fresh copy of the model, centred on its grip or centre, facing -Z.</summary>
  public virtual Node3D Build()
  {
    return new Node3D();
  }
}
