using System;
using Curitibia.Core;
using Curitibia.World;
using Godot;

namespace Curitibia.Items;

/// <summary>
/// An item lying in the world. Every peer builds it from the same data (the host spawns it through the DropSpawner) and moves
/// it the same way: a tiny voxel simulation (gravity, the ground and walls are the terrain's blocks, no physics engine), so no
/// positions need to be sent. It spins and bobs once it rests. Picking it up is decided by the host.
/// </summary>
public partial class ItemDrop : Node3D
{
  const float Gravity = 22f;
  const float Radius = 0.14f;
  const float RestHeight = 0.18f;

  /// <summary>Seconds after it appeared before it can be picked up (so a thrown item does not jump straight back into the thrower's hands).</summary>
  public const float DefaultPickupDelay = 0.6f;

  public int ItemId { get; set; }

  public int Count { get; set; }

  public Vector3 Velocity { get; set; }

  public float PickupDelay { get; set; } = DefaultPickupDelay;

  /// <summary>Host only: set when a player has claimed it, so two requests cannot both get it.</summary>
  public bool Claimed { get; set; }

  /// <summary>Seconds since it appeared.</summary>
  public float Age { get; private set; }

  public bool CanBePickedUp => !Claimed && Age >= PickupDelay;

  Node3D? pivot;
  bool resting;

  public override void _Ready()
  {
    pivot = new Node3D();
    AddChild(pivot);

    var def = ItemRegistry.Get(ItemId);
    var model = def?.Model?.Build();

    if (model == null)
    {
      return;
    }

    // Tools are modelled for the hand (a long thing along -Z): lay them down and shrink them; blocks are small cubes already.
    if (def!.Behavior is not PlaceBlockBehavior)
    {
      model.Scale = Vector3.One * 0.8f;
      model.RotationDegrees = new Vector3(0f, 0f, 90f); // the head bar lies flat
    }

    pivot.AddChild(model);
  }

  public override void _PhysicsProcess(double delta)
  {
    var dt = (float)delta;
    Age += dt;

    var terrain = Terrain.Instance;

    if (terrain == null)
    {
      return;
    }

    if (resting)
    {
      // Gravity never stops: if the block it lies on is broken (or changes to something not solid), it falls again.
      var here = GlobalPosition;

      if (Solid(terrain, here.X, here.Y - RestHeight - 0.05f, here.Z))
      {
        return;
      }

      resting = false;
    }

    // Wait for the ground to exist (a client that has not received that chunk yet) instead of falling through it.
    var position = GlobalPosition;
    VoxelMath.WorldToChunk(VoxelMath.WorldToVoxel(position.X), VoxelMath.WorldToVoxel(position.Z), out var cx, out var cz, out _, out _);

    if (!terrain.TryGetChunk(new Vector2I(cx, cz), out _))
    {
      return;
    }

    var velocity = Velocity;
    velocity.Y -= Gravity * dt;

    // One axis at a time, so it slides along walls and lands on floors.
    position.X += velocity.X * dt;

    if (Solid(terrain, position.X + Math.Sign(velocity.X) * Radius, position.Y, position.Z))
    {
      position.X -= velocity.X * dt;
      velocity.X = 0f;
    }

    position.Z += velocity.Z * dt;

    if (Solid(terrain, position.X, position.Y, position.Z + Math.Sign(velocity.Z) * Radius))
    {
      position.Z -= velocity.Z * dt;
      velocity.Z = 0f;
    }

    position.Y += velocity.Y * dt;

    if (velocity.Y <= 0f && Solid(terrain, position.X, position.Y - RestHeight, position.Z))
    {
      // On the ground: stand on top of the block it is in.
      position.Y = (VoxelMath.WorldToVoxel(position.Y - RestHeight) + 1) * VoxelMath.BlockSize + RestHeight;
      velocity.Y = 0f;
      velocity.X *= 0.6f;
      velocity.Z *= 0.6f;

      if (velocity.X * velocity.X + velocity.Z * velocity.Z < 0.04f)
      {
        velocity = Vector3.Zero;
        resting = true;
      }
    }
    else if (velocity.Y > 0f && Solid(terrain, position.X, position.Y + Radius, position.Z))
    {
      velocity.Y = 0f; // bumped its head
    }

    Velocity = velocity;
    GlobalPosition = position;
  }

  public override void _Process(double delta)
  {
    if (pivot == null)
    {
      return;
    }

    var t = Time.GetTicksMsec() / 1000f + GetInstanceId() % 7;

    pivot.RotationDegrees = new Vector3(0f, t * 90f % 360f, 0f);
    pivot.Position = new Vector3(0f, resting ? 0.06f + MathF.Sin(t * 2.4f) * 0.04f : 0f, 0f);
  }

  static bool Solid(Terrain terrain, float x, float y, float z)
  {
    return terrain.IsSolid(VoxelMath.WorldToVoxel(x), VoxelMath.WorldToVoxel(y), VoxelMath.WorldToVoxel(z));
  }
}
