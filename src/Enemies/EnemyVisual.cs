using System;
using Curitibia.Core;
using Godot;

namespace Curitibia.Enemies;

/// <summary>
/// The enemy from the Unity project: an Among-Us-style crewmate made of capsules (red-orange body, backpack, reflective
/// visor with a black rim, stubby legs, hands out to the sides). Dimensions are those of the Unity "Enemy 2" prefab, z
/// mirrored because Unity faces +Z and Godot -Z. Animation is the Unity enemy's: legs swing with speed, and the body
/// leans forward and waddles (a yaw wobble) with speed.
/// </summary>
public partial class EnemyVisual : Node3D
{
  const float SwingFrequency = 10f;
  const float RootHeight = 0.75f; // the lowest point of the model is 0.75 below its origin

  Node3D root = null!;
  Node3D legL = null!;
  Node3D legR = null!;

  double clock;
  float phaseOffset;

  public override void _Ready()
  {
    var rng = new RandomNumberGenerator();
    rng.Randomize();
    phaseOffset = rng.RandfRange(0f, 50f);

    var body = GameMaterials.EnemyBody;

    root = CapsulePart.Pivot(this, "Root", new Vector3(0f, RootHeight, 0f));

    CapsulePart.Create(root, "Body", body, new Vector3(0f, 0.24f, 0f), new Vector3(1f, 0.7f, 1f));
    CapsulePart.Create(root, "Backpack", body, new Vector3(0f, 0.22f, 0.51f), new Vector3(0.8f, 0.6f, 0.5f));

    var visor = CapsulePart.Pivot(root, "Visor", new Vector3(0f, 0.36f, -0.36f));
    CapsulePart.Create(visor, "Glass", GameMaterials.Reflective, Vector3.Zero, new Vector3(0.7f, 0.24f, 0.5f));
    CapsulePart.Create(visor, "Rim", GameMaterials.MatteBlack, new Vector3(0f, 0f, 0.18f), new Vector3(0.8f, 0.29f, 0.8f));

    var legs = CapsulePart.Pivot(root, "Legs", Vector3.Zero);
    legL = CapsulePart.Pivot(legs, "LegL", new Vector3(-0.2f, 0f, 0f));
    legR = CapsulePart.Pivot(legs, "LegR", new Vector3(0.2f, 0f, 0f));
    CapsulePart.Create(legL, "FootL", body, new Vector3(0f, -0.6f, 0f), new Vector3(0.4f, 0.15f, 0.4f));
    CapsulePart.Create(legR, "FootR", body, new Vector3(0f, -0.6f, 0f), new Vector3(0.4f, 0.15f, 0.4f));

    // The Unity hands sit out at the sides.
    CapsulePart.Create(root, "HandL", body, new Vector3(-0.6f, 0f, 0f), new Vector3(0.4f, 0.15f, 0.4f));
    CapsulePart.Create(root, "HandR", body, new Vector3(0.6f, 0f, 0f), new Vector3(0.4f, 0.15f, 0.4f));
  }

  public void Animate(float delta, Vector3 velocity, bool grounded, float lookPitch)
  {
    if (root == null)
    {
      return;
    }

    clock += delta;

    var speed = new Vector2(velocity.X, velocity.Z).Length();
    var s = Mathf.Sin((float)(clock + phaseOffset) * SwingFrequency);
    var swing = Math.Clamp(speed * 40f, -60f, 60f);

    legR.RotationDegrees = new Vector3(swing * s, 0f, 0f);
    legL.RotationDegrees = new Vector3(-swing * s, 0f, 0f);

    // Lean forward and waddle with speed (Unity: Euler(speed * 4, sin * speed * 6, 0)).
    root.RotationDegrees = new Vector3(-speed * 4f, s * speed * 6f, 0f);
  }
}
