using System;
using Godot;

namespace Curitibia.Player;

/// <summary>
/// A blocky character built from boxes and animated procedurally (limb swing, lean) from the body's velocity. The same
/// pure-function animation runs on every peer from the replicated velocity, so nothing animation-related is sent.
/// Faces -Z like the body.
/// </summary>
public partial class PlayerVisual : Node3D
{
  static readonly BoxMesh bodyMesh = new() { Size = new Vector3(0.6f, 0.7f, 0.3f) };
  static readonly BoxMesh headMesh = new() { Size = new Vector3(0.4f, 0.4f, 0.4f) };
  static readonly BoxMesh visorMesh = new() { Size = new Vector3(0.3f, 0.12f, 0.05f) };
  static readonly BoxMesh legMesh = new() { Size = new Vector3(0.25f, 0.8f, 0.25f) };
  static readonly BoxMesh armMesh = new() { Size = new Vector3(0.18f, 0.7f, 0.18f) };

  static readonly StandardMaterial3D visorMaterial = new()
  {
    AlbedoColor = new Color(0.65f, 0.9f, 1f),
    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
  };

  const float SwingFrequency = 15f;   // radians per second of the limb cycle (Unity: sin(t * 15))
  const float MaxLegSwing = 60f;      // degrees
  const float LegDegreesPerSpeed = 40f;

  Node3D rig = null!;
  Node3D head = null!;
  Node3D legL = null!;
  Node3D legR = null!;
  Node3D armL = null!;
  Node3D armR = null!;

  float phase;
  float lean;

  public override void _Ready()
  {
    rig = new Node3D { Name = "Rig" };
    AddChild(rig);

    var material = new StandardMaterial3D { AlbedoColor = ColorFor(GetParent().Name) };

    AddBox(rig, bodyMesh, material, new Vector3(0f, 1.15f, 0f));

    head = new Node3D { Name = "Head", Position = new Vector3(0f, 1.7f, 0f) };
    rig.AddChild(head);
    AddBox(head, headMesh, material, Vector3.Zero);
    AddBox(head, visorMesh, visorMaterial, new Vector3(0f, 0.04f, -0.2f));

    legL = AddLimb("LegL", legMesh, material, new Vector3(-0.15f, 0.8f, 0f), 0.4f);
    legR = AddLimb("LegR", legMesh, material, new Vector3(0.15f, 0.8f, 0f), 0.4f);
    armL = AddLimb("ArmL", armMesh, material, new Vector3(-0.4f, 1.45f, 0f), 0.3f);
    armR = AddLimb("ArmR", armMesh, material, new Vector3(0.4f, 1.45f, 0f), 0.3f);
  }

  Node3D AddLimb(string name, Mesh mesh, Material material, Vector3 pivotPosition, float halfLength)
  {
    var pivot = new Node3D { Name = name, Position = pivotPosition };
    rig.AddChild(pivot);
    AddBox(pivot, mesh, material, new Vector3(0f, -halfLength, 0f));

    return pivot;
  }

  static void AddBox(Node3D parent, Mesh mesh, Material material, Vector3 position)
  {
    parent.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position });
  }

  /// <summary>A stable colour per player, so peers can tell each other apart.</summary>
  static Color ColorFor(StringName name)
  {
    var hash = (uint)name.ToString().GetHashCode();

    return Color.FromHsv((hash % 360) / 360f, 0.65f, 0.9f);
  }

  public void Animate(float delta, Vector3 velocity, bool grounded, float pitch)
  {
    if (rig == null)
    {
      return;
    }

    var speed = new Vector2(velocity.X, velocity.Z).Length();
    var swing = grounded ? Math.Clamp(speed * LegDegreesPerSpeed, 0f, MaxLegSwing) : 12f;

    if (speed > 0.1f)
    {
      phase += delta * SwingFrequency;
    }

    var s = Mathf.Sin(phase);

    legL.RotationDegrees = new Vector3(s * swing, 0f, 0f);
    legR.RotationDegrees = new Vector3(-s * swing, 0f, 0f);

    var armSwing = grounded ? Math.Clamp(speed, 0f, 3f) * 15f : 25f;

    armL.RotationDegrees = new Vector3(-s * armSwing, 0f, grounded ? 0f : -20f);
    armR.RotationDegrees = new Vector3(s * armSwing, 0f, grounded ? 0f : 20f);

    // Lean into the run, smoothed so it does not snap.
    lean = Mathf.Lerp(lean, grounded ? Math.Clamp(speed, 0f, 6f) * 2f : 0f, 1f - Mathf.Exp(-10f * delta));
    rig.RotationDegrees = new Vector3(-lean, 0f, 0f);

    head.Rotation = new Vector3(pitch, 0f, 0f);
  }
}
