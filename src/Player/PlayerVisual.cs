using System;
using System.Collections.Generic;
using Curitibia.Core;
using Godot;

namespace Curitibia.Player;

/// <summary>
/// The player character from the Unity project: a blue body with a yellow head and black eyes, metal armour and stubby
/// metal legs, hands out to the sides. All capsule primitives, the same dimensions as the Unity prefab (z mirrored, because
/// Unity faces +Z and Godot -Z).
///
/// The animation is the Unity one, computed locally on every peer from the replicated velocity (nothing about it is sent):
/// the body faces the direction it is travelling (not the camera) and leans into the run and into turns, legs and arms swing
/// with speed, limbs hold their last pose in the air unless a wall is next to you (then you run up it), and every part
/// eases toward its target pose.
/// </summary>
public partial class PlayerVisual : Node3D
{
  const float PoseResponsiveness = 24f;   // how fast parts ease to the target pose (Unity lerpPower)
  const float SwingFrequency = 15f;       // limb cycle, radians per second
  const float DirectionResponsiveness = 3f;
  const float WallCheckAngle = 45f;
  const float WallCheckDistance = 1f;

  // Unity put the model root a little above the capsule bottom; here the feet are just off the ground.
  const float RootHeight = 0.8f;

  readonly PhysicsRayQueryParameters3D wallQuery = new() { CollisionMask = GameLayers.World };
  readonly List<(MeshInstance3D mesh, Material material)> parts = new();
  readonly RandomNumberGenerator rng = new();

  Node3D rig = null!;
  Node3D chest = null!;
  Node3D arms = null!;
  Node3D legL = null!;
  Node3D legR = null!;

  Quaternion legLTarget = Quaternion.Identity;
  Quaternion legRTarget = Quaternion.Identity;
  Quaternion armsTarget = Quaternion.Identity;
  Quaternion chestTarget = Quaternion.Identity;

  Vector3 targetDirection = Vector3.Forward * 4f;
  Vector3 smoothedDirection = Vector3.Forward * 4f;
  double clock;
  float wallTimer;
  bool touchingWall;
  float phaseOffset;
  bool ghost;

  /// <summary>Draws the body as the translucent Fresnel ghost (a dead player).</summary>
  public bool Ghost
  {
    get => ghost;
    set
    {
      if (ghost == value || parts.Count == 0)
      {
        ghost = value;
        return;
      }

      ghost = value;

      foreach (var (mesh, material) in parts)
      {
        mesh.MaterialOverride = ghost ? GameMaterials.Ghost : material;
      }
    }
  }

  public override void _Ready()
  {
    rng.Randomize();
    phaseOffset = rng.RandfRange(0f, 50f);
    wallQuery.HitFromInside = false;

    // Everyone gets Unity's blue chest by hue-shifting it per player, so peers can be told apart.
    var bodyMaterial = GameMaterials.Matte(ColorFor(GetParent().Name));

    rig = CapsulePart.Pivot(this, "Rig", new Vector3(0f, RootHeight, 0f));
    chest = CapsulePart.Pivot(rig, "Chest", Vector3.Zero);

    Add(chest, "Torso", bodyMaterial, new Vector3(0f, -0.06f, 0f), new Vector3(0.6f, 0.4f, 0.6f));

    var head = Add(chest, "Head", GameMaterials.MatteYellow, new Vector3(0f, 0.54f, 0f), new Vector3(0.7f, 0.36f, 0.7f));
    var face = CapsulePart.Pivot(head, "Face", new Vector3(0f, -0.09f, -0.41f), new Vector3(1.11f, 2.5f, 1.11f));
    Add(face, "EyeL", GameMaterials.MatteBlack, new Vector3(-0.12f, 0f, -0.05f), Vector3.One * 0.1f);
    Add(face, "EyeR", GameMaterials.MatteBlack, new Vector3(0.12f, 0f, -0.05f), Vector3.One * 0.1f);

    var armor = CapsulePart.Pivot(chest, "Armor", Vector3.Zero);
    Add(armor, "Collar", GameMaterials.Metal, new Vector3(0f, 0.04f, 0f), new Vector3(0.8f, 0.3f, 0.7f));
    Add(armor, "Belt", GameMaterials.Metal, new Vector3(0f, -0.17f, 0f), new Vector3(0.7f, 0.3f, 0.6f));
    Add(armor, "ShoulderL", GameMaterials.Metal, new Vector3(-0.3f, 0.14f, 0f), new Vector3(0.5f, 0.15f, 0.5f));
    Add(armor, "ShoulderR", GameMaterials.Metal, new Vector3(0.3f, 0.14f, 0f), new Vector3(0.5f, 0.15f, 0.5f));

    // Arms hang out to the sides (the Unity arm pivots are turned about 80 degrees), a hand at the end of each.
    arms = CapsulePart.Pivot(chest, "Arms", Vector3.Zero);
    var armL = CapsulePart.Pivot(arms, "ArmL", Vector3.Zero, null, new Vector3(0f, 0f, -79.6f));
    var armR = CapsulePart.Pivot(arms, "ArmR", Vector3.Zero, null, new Vector3(0f, 0f, 79.6f));
    Add(armL, "HandL", GameMaterials.MatteYellow, new Vector3(0f, -0.6f, 0f), new Vector3(0.4f, 0.15f, 0.4f));
    Add(armR, "HandR", GameMaterials.MatteYellow, new Vector3(0f, -0.6f, 0f), new Vector3(0.4f, 0.15f, 0.4f));

    var legs = CapsulePart.Pivot(rig, "Legs", Vector3.Zero);
    legL = CapsulePart.Pivot(legs, "LegL", new Vector3(-0.2f, 0f, 0f));
    legR = CapsulePart.Pivot(legs, "LegR", new Vector3(0.2f, 0f, 0f));
    Add(legL, "FootL", GameMaterials.Metal, new Vector3(0f, -0.6f, 0f), new Vector3(0.4f, 0.15f, 0.4f));
    Add(legR, "FootR", GameMaterials.Metal, new Vector3(0f, -0.6f, 0f), new Vector3(0.4f, 0.15f, 0.4f));
  }

  MeshInstance3D Add(Node parent, string name, Material material, Vector3 position, Vector3 scale)
  {
    var part = CapsulePart.Create(parent, name, material, position, scale);
    parts.Add((part, material));

    return part;
  }

  /// <summary>A stable colour per player, so peers can tell each other apart.</summary>
  static Color ColorFor(StringName name)
  {
    var hash = (uint)name.ToString().GetHashCode();

    return Color.FromHsv((hash % 360) / 360f, 0.75f, 0.95f);
  }

  public void Animate(float delta, Vector3 velocity, bool grounded, float lookPitch)
  {
    if (rig == null)
    {
      return;
    }

    clock += delta;

    var flat = new Vector3(velocity.X, 0f, velocity.Z);
    var speed = flat.Length();

    // Remember the direction of travel while moving (Unity: only above speed 1), and ease a second direction toward it.
    if (velocity.Length() > 1f && flat.LengthSquared() > 0.0001f)
    {
      targetDirection = flat.Normalized() * 4f;
    }

    smoothedDirection = smoothedDirection.Lerp(targetDirection, Math.Min(1f, delta * DirectionResponsiveness));

    // Ray queries hand back a fresh engine object each time, so check twenty times a second and reuse the answer.
    wallTimer -= delta;

    if (grounded)
    {
      touchingWall = false;
      wallTimer = 0f;
    }
    else if (wallTimer <= 0f)
    {
      wallTimer = 0.05f;
      touchingWall = TouchesWall();
    }


    // The model keeps world orientation: yaw from the travelled direction, lean forward with speed, roll into turns.
    var targetYaw = Mathf.Atan2(-targetDirection.X, -targetDirection.Z);
    var smoothYaw = Mathf.Atan2(-smoothedDirection.X, -smoothedDirection.Z);
    var turn = Mathf.RadToDeg(Mathf.Wrap(targetYaw - smoothYaw, -Mathf.Pi, Mathf.Pi));
    var roll = Math.Clamp(turn, -35f, 35f);

    var rootTarget = Basis.FromEuler(new Vector3(-Mathf.DegToRad(speed * 4f), smoothYaw, Mathf.DegToRad(roll))).GetRotationQuaternion();

    // Limb targets: the run cycle on the ground or against a wall; held as they were while airborne otherwise.
    if (grounded || touchingWall)
    {
      var s = Mathf.Sin((float)(clock + phaseOffset) * SwingFrequency);
      var legSwing = Math.Clamp(speed * 40f, -60f, 60f);
      var armTwist = Math.Clamp(speed, 0f, 3f);

      legRTarget = Quaternion.FromEuler(new Vector3(Mathf.DegToRad(legSwing * s), 0f, 0f));
      legLTarget = Quaternion.FromEuler(new Vector3(Mathf.DegToRad(-legSwing * s), 0f, 0f));
      armsTarget = Quaternion.FromEuler(new Vector3(0f, Mathf.DegToRad(s * armTwist * 15f), 0f));
      chestTarget = Quaternion.FromEuler(new Vector3(0f, Mathf.DegToRad(-s * armTwist * 4f), 0f));
    }

    var k = Math.Min(1f, PoseResponsiveness * delta);

    rig.GlobalBasis = new Basis(rig.GlobalBasis.GetRotationQuaternion().Slerp(rootTarget, k));
    legL.Quaternion = legL.Quaternion.Slerp(legLTarget, k);
    legR.Quaternion = legR.Quaternion.Slerp(legRTarget, k);
    arms.Quaternion = arms.Quaternion.Slerp(armsTarget, k);
    chest.Quaternion = chest.Quaternion.Slerp(chestTarget, k);
  }

  /// <summary>Two short rays 45 degrees either side of the travel direction (Unity PlayerRaycasts).</summary>
  bool TouchesWall()
  {
    if (smoothedDirection.LengthSquared() < 0.01f)
    {
      return false;
    }

    var origin = rig.GlobalPosition + Vector3.Down * 0.5f;
    var direction = smoothedDirection.Normalized();
    var space = GetWorld3D().DirectSpaceState;
    var angle = Mathf.DegToRad(WallCheckAngle);

    wallQuery.From = origin;
    wallQuery.To = origin + direction.Rotated(Vector3.Up, angle) * WallCheckDistance;

    if (space.IntersectRay(wallQuery).Count > 0)
    {
      return true;
    }

    wallQuery.To = origin + direction.Rotated(Vector3.Up, -angle) * WallCheckDistance;

    return space.IntersectRay(wallQuery).Count > 0;
  }
}
