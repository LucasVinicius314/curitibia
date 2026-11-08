using System;
using System.Collections.Generic;
using Curitibia.Combat;
using Curitibia.Core;
using Curitibia.Player;
using Curitibia.World;
using Godot;

namespace Curitibia.Enemies;

public enum EnemyState
{
  Idle,
  Chase,
  Search,
}

/// <summary>
/// A shooter that hunts players. The host runs all of the thinking (vision, memory, pathing, shooting); every other peer
/// only shows the replicated state, interpolated. Behaviour from the Unity enemy: closest visible player inside a 140
/// degree cone and 13 m (line of sight must reach it), noticed anyway within 3 m, kept as a target for 3 s after it is lost
/// and then searched for at its last known position.
/// </summary>
public partial class Enemy : CharacterBody3D
{
  [Export] public EnemyTuning Tuning { get; set; } = null!;
  [Export] public EnemyVisual Visual { get; set; } = null!;

  // Replicated state (host writes, others read).
  [Export] public Vector3 SyncPosition { get; set; }
  [Export] public float SyncYaw { get; set; }
  [Export] public Vector3 SyncVelocity { get; set; }
  [Export] public bool SyncGrounded { get; set; }

  public Team Team => Team.Enemies;
  public EnemyState State { get; private set; } = EnemyState.Idle;

  readonly PhysicsRayQueryParameters3D sightQuery = new() { CollisionMask = GameLayers.SightMask };
  readonly Func<int, int, int, bool> isSolid;
  readonly RandomNumberGenerator rng = new();

  float gravity;
  double clock;
  float thinkTimer;
  float wanderTimer;
  float shootTimer;
  double lastSeen = double.NegativeInfinity;
  Vector3 lastKnown;
  PlayerController? target;
  bool targetVisible;
  Vector3 horizontal;
  TickSmoother smoother;

  List<(int x, int y, int z)>? path;
  int pathIndex;
  (int x, int y, int z) pathGoal;
  float repathTimer;

  Vector3 stuckSample;
  float stuckTimer;

  public Enemy()
  {
    isSolid = (x, y, z) => Terrain.Instance?.IsSolid(x, y, z) ?? true;
  }

  public override void _Ready()
  {
    gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8);
    rng.Randomize();
    sightQuery.Exclude = new Godot.Collections.Array<Rid> { GetRid() };

    if (SyncPosition != Vector3.Zero)
    {
      GlobalPosition = SyncPosition;
    }
    else
    {
      SyncPosition = GlobalPosition;
    }

    smoother.Reset(GlobalPosition);
    SetPhysicsProcess(Multiplayer.IsServer());
  }

  // ------------------------------------------------------------------ clients

  public override void _Process(double delta)
  {
    var dt = (float)delta;

    if (Multiplayer.IsServer())
    {
      // The body moves on physics ticks; draw it between the last two so it glides at any frame rate.
      Visual.GlobalPosition = smoother.Blend(GlobalPosition);
      Visual.Animate(dt, Velocity, IsOnFloor(), 0f);
      return;
    }

    var follow = 1f - Mathf.Exp(-14f * dt);

    GlobalPosition = GlobalPosition.DistanceSquaredTo(SyncPosition) > 25f
      ? SyncPosition
      : GlobalPosition.Lerp(SyncPosition, follow);

    Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, SyncYaw, follow), 0f);
    Visual.Animate(dt, SyncVelocity, SyncGrounded, 0f);
  }

  // ------------------------------------------------------------------ host

  public override void _PhysicsProcess(double delta)
  {
    var dt = (float)delta;
    clock += dt;

    var terrain = Terrain.Instance;

    // Stand still until the ground under us exists (the chunk may not be built yet, or may have been unloaded).
    if (terrain == null || !terrain.HasCollisionAt(GlobalPosition))
    {
      Velocity = Vector3.Zero;
      Publish();
      smoother.Record(GlobalPosition);
      return;
    }

    thinkTimer -= dt;
    shootTimer -= dt;
    repathTimer -= dt;

    if (thinkTimer <= 0f)
    {
      thinkTimer = Tuning.ThinkInterval;
      Think();
    }

    Move(dt);
    Publish();
    smoother.Record(GlobalPosition);
  }

  void Think()
  {
    PickTarget();

    switch (State)
    {
      case EnemyState.Chase:
        if (!targetVisible && clock - lastSeen > Tuning.MemorySeconds)
        {
          State = EnemyState.Search;
        }

        break;

      case EnemyState.Search:
        if (FlatDistance(GlobalPosition, lastKnown) < 1.5f)
        {
          State = EnemyState.Idle;
          path = null;
        }

        break;

      case EnemyState.Idle:
        wanderTimer -= Tuning.ThinkInterval;

        if (wanderTimer <= 0f)
        {
          wanderTimer = rng.RandfRange(3f, 6f);
          Wander();
        }

        break;
    }

    if (State != EnemyState.Idle)
    {
      RepathTo(lastKnown);
    }

    TryShoot();
  }

  void PickTarget()
  {
    PlayerController? best = null;
    var bestDistance = float.MaxValue;
    var seen = false;

    foreach (var player in PlayerController.All)
    {
      if (player.Health.IsDead)
      {
        continue;
      }

      var distance = GlobalPosition.DistanceTo(player.GlobalPosition);
      var noticed = distance <= Tuning.AwarenessRadius || CanSee(player);

      if (noticed && distance < bestDistance)
      {
        best = player;
        bestDistance = distance;
        seen = true;
      }
    }

    targetVisible = seen;

    if (best != null)
    {
      target = best;
      lastKnown = best.GlobalPosition;
      lastSeen = clock;
      State = EnemyState.Chase;
    }
  }

  bool CanSee(PlayerController player)
  {
    var eye = GlobalPosition + Vector3.Up * 1.5f;
    var chest = player.GlobalPosition + Vector3.Up * 1.1f;
    var toPlayer = chest - eye;

    if (toPlayer.Length() > Tuning.VisionRange)
    {
      return false;
    }

    var forward = -GlobalTransform.Basis.Z;

    if (Mathf.RadToDeg(forward.AngleTo(toPlayer)) > Tuning.FieldOfViewHalfAngle)
    {
      return false;
    }

    sightQuery.From = eye;
    sightQuery.To = chest;

    var hit = GetWorld3D().DirectSpaceState.IntersectRay(sightQuery);

    // The first thing the ray meets has to be the player (terrain in between blocks the view).
    return hit.Count > 0 && hit["collider"].AsGodotObject() == player;
  }

  void TryShoot()
  {
    if (target == null || !targetVisible || shootTimer > 0f || target.Health.IsDead)
    {
      return;
    }

    var spawner = ProjectileSpawner.Instance;
    var origin = GlobalPosition + Vector3.Up * 1.3f;
    var aim = target.GlobalPosition + Vector3.Up * 1.1f - origin;

    if (spawner == null || aim.Length() > Tuning.ShootRange)
    {
      return;
    }

    var error = Mathf.DegToRad(Tuning.AimError);
    var direction = aim.Normalized()
      .Rotated(Vector3.Up, rng.RandfRange(-error, error))
      .Rotated(Vector3.Right, rng.RandfRange(-error, error));

    shootTimer = Tuning.ShootCooldown;
    spawner.Fire(origin + direction * 0.6f, direction, Team);
  }

  // ------------------------------------------------------------------ pathing

  void RepathTo(Vector3 worldGoal)
  {
    var goal = SnapCell(worldGoal);

    if (goal == null)
    {
      return;
    }

    var moved = Math.Abs(goal.Value.x - pathGoal.x) + Math.Abs(goal.Value.z - pathGoal.z) + Math.Abs(goal.Value.y - pathGoal.y);

    // Only recompute when the goal moved, the path ran out, or it has been a while (the world may have changed).
    if (path != null && pathIndex < path.Count && moved <= 1 && repathTimer > 0f)
    {
      return;
    }

    var start = SnapCell(GlobalPosition);

    if (start == null)
    {
      return;
    }

    repathTimer = 1.0f;
    pathGoal = goal.Value;
    path = VoxelPathfinder.FindPath(isSolid, start.Value, goal.Value);
    pathIndex = 0;
  }

  (int x, int y, int z)? SnapCell(Vector3 position)
  {
    return VoxelPathfinder.SnapToStandable(
      isSolid,
      VoxelMath.FloorToInt(position.X),
      VoxelMath.FloorToInt(position.Y + 0.05f),
      VoxelMath.FloorToInt(position.Z));
  }

  void Wander()
  {
    var start = SnapCell(GlobalPosition);

    if (start == null)
    {
      return;
    }

    var r = Tuning.WanderRadius;
    var gx = start.Value.x + rng.RandiRange(-r, r);
    var gz = start.Value.z + rng.RandiRange(-r, r);
    var goal = VoxelPathfinder.SnapToStandable(isSolid, gx, start.Value.y, gz);

    if (goal == null)
    {
      return;
    }

    pathGoal = goal.Value;
    path = VoxelPathfinder.FindPath(isSolid, start.Value, goal.Value, 800);
    pathIndex = 0;
  }

  // ------------------------------------------------------------------ movement

  void Move(float dt)
  {
    var desired = Vector3.Zero;
    var wantJump = false;

    // Hold position and just shoot once close enough with a clear view.
    var holding = State == EnemyState.Chase && targetVisible && target != null
      && GlobalPosition.DistanceTo(target.GlobalPosition) < Tuning.HoldDistance;

    if (!holding && path != null && pathIndex < path.Count)
    {
      var cell = path[pathIndex];
      var waypoint = new Vector3(cell.x + 0.5f, cell.y, cell.z + 0.5f);
      var flat = waypoint - GlobalPosition;
      flat.Y = 0f;

      if (flat.Length() < 0.3f)
      {
        pathIndex++;
      }
      else
      {
        desired = flat.Normalized();
        var feetCell = VoxelMath.FloorToInt(GlobalPosition.Y + 0.05f);
        wantJump = cell.y > feetCell && IsOnFloor() && flat.Length() < 1.2f;
      }
    }

    var speed = Tuning.Speed * (State == EnemyState.Idle ? Tuning.WanderSpeedFactor : 1f);
    horizontal = horizontal.Lerp(desired * speed, 1f - Mathf.Exp(-10f * dt));

    var velocity = Velocity;
    velocity.X = horizontal.X;
    velocity.Z = horizontal.Z;

    if (IsOnFloor())
    {
      velocity.Y = Math.Max(velocity.Y, 0f);
    }
    else
    {
      velocity.Y -= gravity * dt;
    }

    if (wantJump)
    {
      velocity.Y = Tuning.JumpVelocity;
    }

    Velocity = velocity;
    MoveAndSlide();

    Face(dt, desired);
    CheckStuck(dt, desired);
  }

  void Face(float dt, Vector3 desired)
  {
    Vector3 look;

    if (targetVisible && target != null)
    {
      look = target.GlobalPosition - GlobalPosition;
    }
    else
    {
      look = desired;
    }

    look.Y = 0f;

    if (look.LengthSquared() < 0.01f)
    {
      return;
    }

    var yaw = Mathf.Atan2(-look.X, -look.Z); // the body faces -Z
    Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, yaw, 1f - Mathf.Exp(-Tuning.TurnSpeed * dt)), 0f);
  }

  void CheckStuck(float dt, Vector3 desired)
  {
    stuckTimer += dt;

    if (stuckTimer < 1.2f)
    {
      return;
    }

    if (desired != Vector3.Zero && GlobalPosition.DistanceTo(stuckSample) < 0.2f)
    {
      path = null; // something is in the way that the path did not know about: plan again
      repathTimer = 0f;
    }

    stuckSample = GlobalPosition;
    stuckTimer = 0f;
  }

  void Publish()
  {
    SyncPosition = GlobalPosition;
    SyncYaw = Rotation.Y;
    SyncVelocity = Velocity;
    SyncGrounded = IsOnFloor();
  }

  static float FlatDistance(Vector3 a, Vector3 b)
  {
    return new Vector2(a.X - b.X, a.Z - b.Z).Length();
  }
}
