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
/// A melee fighter that hunts players. The host runs all of the thinking (vision, memory, pathing, attacking); every other peer
/// only shows the replicated state, interpolated. Behaviour from the Unity enemy: closest visible player inside a 140
/// degree cone and 13 m (line of sight must reach it), noticed anyway within 3 m, kept as a target for 3 s after it is lost
/// and then searched for at its last known position.
/// </summary>
public partial class Enemy : CharacterBody3D, IDamageable
{
  [Export] public EnemyTuning Tuning { get; set; } = null!;
  [Export] public EnemyVisual Visual { get; set; } = null!;
  [Export] public Health Health { get; set; } = null!;

  // Replicated state (host writes, others read).
  [Export] public Vector3 SyncPosition { get; set; }
  [Export] public float SyncYaw { get; set; }
  [Export] public Vector3 SyncVelocity { get; set; }
  [Export] public bool SyncGrounded { get; set; }

  /// <summary>Voxels the body occupies standing up (1.8 m of half-metre blocks), for the pathfinder.</summary>
  const int BodyCells = 4;

  /// <summary>Every enemy in the scene (kept by enter/exit so weapons never scan the tree).</summary>
  public static readonly List<Enemy> All = new();

  public Team Team => Team.Enemies;
  public EnemyState State { get; private set; } = EnemyState.Idle;

  readonly PhysicsRayQueryParameters3D sightQuery = new() { CollisionMask = GameLayers.SightMask };
  readonly Func<int, int, int, bool> isSolid;
  readonly RandomNumberGenerator rng = new();

  float gravity;
  double clock;
  float thinkTimer;
  float wanderTimer;
  float attackTimer;
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

  public override void _EnterTree()
  {
    All.Add(this);
  }

  public override void _ExitTree()
  {
    All.Remove(this);
  }

  HealthBar3D healthBar = null!;

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

    Health.MaxHp = Tuning.MaxHp;
    Health.Hp = Tuning.MaxHp;

    healthBar = new HealthBar3D { Position = new Vector3(0f, 2.2f, 0f) };
    Visual.AddChild(healthBar);

    if (Multiplayer.IsServer())
    {
      Health.Died += OnDied;
    }
  }

  // ------------------------------------------------------------------ damage (host only)

  public void TakeDamage(DamageInfo info)
  {
    if (!Multiplayer.IsServer() || Health.IsDead)
    {
      return;
    }

    Health.Damage(info.Amount);

    var flat = new Vector3(info.Direction.X, 0f, info.Direction.Z).Normalized();
    horizontal += flat * info.Knockback;
    Velocity = new Vector3(Velocity.X, Math.Max(Velocity.Y, 2.5f), Velocity.Z);

    // Being hit makes it notice the attacker even if it was looking elsewhere.
    lastSeen = clock;
  }

  void OnDied()
  {
    QueueFree(); // the spawner removes it on every peer
  }

  // ------------------------------------------------------------------ clients

  public override void _Process(double delta)
  {
    var dt = (float)delta;

    healthBar.Set(Health.Hp, Health.MaxHp);

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
    attackTimer -= dt;
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

    TryAttack();
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

  void TryAttack()
  {
    if (target == null || !targetVisible || attackTimer > 0f || target.Health.IsDead)
    {
      return;
    }

    var toTarget = target.GlobalPosition - GlobalPosition;
    toTarget.Y = 0f;

    if (toTarget.Length() > Tuning.MeleeRange || Math.Abs(target.GlobalPosition.Y - GlobalPosition.Y) > 1.4f)
    {
      return;
    }

    attackTimer = Tuning.MeleeCooldown;
    target.TakeDamage(new DamageInfo(Tuning.MeleeDamage, Team, toTarget.Normalized(), Tuning.MeleeKnockback));
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
    path = VoxelPathfinder.FindPath(isSolid, start.Value, goal.Value, body: BodyCells);
    pathIndex = 0;
  }

  (int x, int y, int z)? SnapCell(Vector3 position)
  {
    return VoxelPathfinder.SnapToStandable(
      isSolid,
      VoxelMath.WorldToVoxel(position.X),
      VoxelMath.WorldToVoxel(position.Y + 0.05f),
      VoxelMath.WorldToVoxel(position.Z),
      body: BodyCells);
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
    var goal = VoxelPathfinder.SnapToStandable(isSolid, gx, start.Value.y, gz, body: BodyCells);

    if (goal == null)
    {
      return;
    }

    pathGoal = goal.Value;
    path = VoxelPathfinder.FindPath(isSolid, start.Value, goal.Value, 800, BodyCells);
    pathIndex = 0;
  }

  // ------------------------------------------------------------------ movement

  void Move(float dt)
  {
    var desired = Vector3.Zero;
    var wantJump = false;

    // Stop walking once next to the target and just fight.
    var holding = State == EnemyState.Chase && targetVisible && target != null
      && GlobalPosition.DistanceTo(target.GlobalPosition) < Tuning.HoldDistance;

    if (!holding && path != null && pathIndex < path.Count)
    {
      var cell = path[pathIndex];
      var waypoint = new Vector3(VoxelMath.VoxelCenter(cell.x), VoxelMath.VoxelToWorld(cell.y), VoxelMath.VoxelCenter(cell.z));
      var flat = waypoint - GlobalPosition;
      flat.Y = 0f;

      if (flat.Length() < 0.2f)
      {
        pathIndex++;
      }
      else
      {
        desired = flat.Normalized();
        var feetCell = VoxelMath.WorldToVoxel(GlobalPosition.Y + 0.05f);
        wantJump = cell.y > feetCell && IsOnFloor() && (flat.Length() < 0.9f || IsOnWall());
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
