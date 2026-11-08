using System;
using System.Collections.Generic;
using Curitibia.Combat;
using Curitibia.Core;
using Curitibia.Items;
using Curitibia.World;
using Godot;

namespace Curitibia.Player;

/// <summary>
/// The player body. Only the peer that owns it (multiplayer authority) reads input and simulates; every other peer just
/// shows the replicated state (Sync* properties, replicated by a MultiplayerSynchronizer in the prefab) smoothed in
/// _Process. Walking, jumping and the one-block step-up live here; looking and block editing are separate nodes/parts.
/// </summary>
public partial class PlayerController : CharacterBody3D, IDamageable
{
  [Export] public PlayerTuning Tuning { get; set; } = null!;
  [Export] public Node3D CameraPivot { get; set; } = null!;
  [Export] public Camera3D Camera { get; set; } = null!;
  [Export] public PlayerVisual Visual { get; set; } = null!;
  [Export] public BlockInteractor Interactor { get; set; } = null!;
  [Export] public Inventory Inventory { get; set; } = null!;
  [Export] public Health Health { get; set; } = null!;
  [Export] public MultiplayerSynchronizer HealthSync { get; set; } = null!;

  // Replicated state (the authority writes it every physics tick, everyone else reads it).
  [Export] public Vector3 SyncPosition { get; set; }
  [Export] public float SyncYaw { get; set; }
  [Export] public float SyncPitch { get; set; }
  [Export] public Vector3 SyncVelocity { get; set; }
  [Export] public bool SyncGrounded { get; set; }
  [Export] public int SyncHeldItem { get; set; }

  /// <summary>Every player in the scene (kept by enter/exit, so enemies never scan the tree).</summary>
  public static readonly List<PlayerController> All = new();

  public Team Team => Team.Players;

  /// <summary>NetId of the item in hand: read from the inventory on the owner, from the replicated value elsewhere.</summary>
  public int HeldItemId => IsLocal ? Inventory.HeldId : SyncHeldItem;

  /// <summary>Where this player looks from.</summary>
  public Vector3 EyePosition => CameraPivot.GlobalPosition;

  /// <summary>True on the peer that controls this player (always true offline).</summary>
  public bool IsLocal => IsMultiplayerAuthority();

  /// <summary>Third-person camera on (local player only).</summary>
  public bool ThirdPerson { get; private set; }

  /// <summary>Noclip flight for testing (local player only): no collision, no gravity.</summary>
  public bool Noclip { get; private set; }

  /// <summary>World-space box of the body, used to keep block placement out of the player.</summary>
  public Aabb BodyBounds => new(GlobalPosition + new Vector3(-0.4f, 0f, -0.4f), new Vector3(0.8f, 1.8f, 0.8f));

  const float BodyRadius = 0.4f;
  const float BumpCooldownSeconds = 0.4f;
  const float JumpBufferSeconds = 0.12f;
  const float CoyoteSeconds = 0.1f;

  readonly KinematicCollision3D probe = new();

  // Last two tick positions, so the camera glides between ticks at any frame rate (see TickSmoother).
  TickSmoother smoother;

  readonly bool showSelf = DevArgs.Has("show-self");
  uint walkMask;
  float cameraDistance;
  float gravity;
  float pitch;
  float jumpBuffer;
  float bumpCooldown;
  float coyote;
  float cameraBaseY;
  float stepCameraOffset;
  Vector3 horizontal;

  public override void _EnterTree()
  {
    // Players spawned by the network are named after their peer id, which is also who owns them.
    if (int.TryParse(Name, out var peerId))
    {
      SetMultiplayerAuthority(peerId);
    }

    // The owner drives the body, but hit points belong to the host: the host owns the health synchronizer.
    HealthSync.SetMultiplayerAuthority(1);
    All.Add(this);
  }

  public override void _Ready()
  {
    gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8);
    cameraBaseY = CameraPivot.Position.Y;
    walkMask = CollisionMask;

    // A spawned player arrives with the position the host chose in SyncPosition; a hand-placed one reports its own.
    if (SyncPosition != Vector3.Zero)
    {
      GlobalPosition = SyncPosition;
    }
    else
    {
      SyncPosition = GlobalPosition;
    }

    smoother.Reset(GlobalPosition);
    Terrain.Instance?.AddViewer(this);

    if (Multiplayer.IsServer())
    {
      Health.Died += OnDiedOnHost;
    }

    if (IsLocal)
    {
      Camera.MakeCurrent();
      Camera.Fov = GameSettings.FieldOfView;
      GameSettings.Changed += OnSettingsChanged;
      Input.MouseMode = Input.MouseModeEnum.Captured;
      ThirdPerson = DevArgs.Has("third-person");

      if (DevArgs.Has("noclip"))
      {
        SetNoclip(true);
      }
    }
    else
    {
      Camera.Current = false;
      SetPhysicsProcess(false);
    }
  }

  void OnSettingsChanged()
  {
    Camera.Fov = GameSettings.FieldOfView;
  }

  void SetNoclip(bool on)
  {
    Noclip = on;
    CollisionMask = on ? 0u : walkMask;
    Velocity = Vector3.Zero;
    horizontal = Vector3.Zero;

    if (on)
    {
      return;
    }

    // Coming back from inside the terrain: climb out so the body does not start embedded in a block.
    var terrain = Terrain.Instance;

    if (terrain == null)
    {
      return;
    }

    for (int i = 0; i < VoxelMath.ChunkHeight; i++)
    {
      var p = GlobalPosition;
      var vx = VoxelMath.WorldToVoxel(p.X);
      var vz = VoxelMath.WorldToVoxel(p.Z);

      if (!terrain.IsSolid(vx, VoxelMath.WorldToVoxel(p.Y + 0.1f), vz) && !terrain.IsSolid(vx, VoxelMath.WorldToVoxel(p.Y + 1.6f), vz))
      {
        break;
      }

      GlobalPosition = p + Vector3.Up * VoxelMath.BlockSize;
    }

    smoother.Reset(GlobalPosition);
  }

  public override void _ExitTree()
  {
    GameSettings.Changed -= OnSettingsChanged;
    All.Remove(this);
    Terrain.Instance?.RemoveViewer(this);
  }

  public override void _UnhandledInput(InputEvent @event)
  {
    if (!IsLocal)
    {
      return;
    }

    if (@event.IsActionPressed(Actions.Pause))
    {
      Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
        ? Input.MouseModeEnum.Visible
        : Input.MouseModeEnum.Captured;
      return;
    }

    if (Input.MouseMode != Input.MouseModeEnum.Captured)
    {
      if (@event is InputEventMouseButton { Pressed: true })
      {
        Input.MouseMode = Input.MouseModeEnum.Captured;
      }

      return;
    }

    if (@event.IsActionPressed(Actions.ToggleView))
    {
      ThirdPerson = !ThirdPerson;
    }
    else if (@event.IsActionPressed(Actions.ToggleFly))
    {
      SetNoclip(!Noclip);
    }

    if (@event is InputEventMouseMotion motion)
    {
      // Mouse deltas are already per-frame distances: no delta-time scaling (that would make it frame-rate dependent).
      Look(-motion.Relative.X * Tuning.MouseSensitivity * GameSettings.MouseSensitivity, -motion.Relative.Y * Tuning.MouseSensitivity * GameSettings.MouseSensitivity);
    }
  }

  void Look(float yawDelta, float pitchDelta)
  {
    RotateY(yawDelta);
    pitch = Math.Clamp(pitch + pitchDelta, -Tuning.MaxPitch, Tuning.MaxPitch);
    CameraPivot.Rotation = new Vector3(pitch, 0f, 0f);
  }

  public override void _Process(double delta)
  {
    var dt = (float)delta;
    Visual.Ghost = Health.IsDead;

    if (IsLocal)
    {
      var stick = Input.GetVector(Actions.LookLeft, Actions.LookRight, Actions.LookUp, Actions.LookDown);

      if (stick != Vector2.Zero && Input.MouseMode == Input.MouseModeEnum.Captured)
      {
        // Squared response: small deflections turn slowly (fine aim), full deflection turns at StickSensitivity.
        var curved = stick * stick.Length();
        var stickSpeed = Tuning.StickSensitivity * GameSettings.MouseSensitivity;
        Look(-curved.X * stickSpeed * dt, -curved.Y * stickSpeed * dt);
      }

      // Ease the camera back down after a step-up so climbing a block is not a pop.
      stepCameraOffset = Mathf.Lerp(stepCameraOffset, 0f, 1f - Mathf.Exp(-14f * dt));
      var glide = GlobalTransform.Basis.Inverse() * (smoother.Blend(GlobalPosition) - GlobalPosition);
      CameraPivot.Position = new Vector3(0f, cameraBaseY + stepCameraOffset, 0f) + glide;

      Visual.ShadowOnly = !ThirdPerson && !showSelf; // first person: the body casts its shadow but is not drawn
      UpdateCameraDistance(dt);

      Visual.Animate(dt, Velocity, IsOnFloor(), pitch);
      return;
    }

    // Remote player: follow the replicated state.
    var follow = 1f - Mathf.Exp(-18f * dt);

    GlobalPosition = GlobalPosition.DistanceSquaredTo(SyncPosition) > 25f
      ? SyncPosition
      : GlobalPosition.Lerp(SyncPosition, follow);

    Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, SyncYaw, follow), 0f);
    pitch = Mathf.Lerp(pitch, SyncPitch, follow);
    CameraPivot.Rotation = new Vector3(pitch, 0f, 0f);

    Visual.Animate(dt, SyncVelocity, SyncGrounded, pitch);
  }

  /// <summary>
  /// Third person: the camera sits behind the eye, pulled in when the terrain would be between the two (a voxel walk, no
  /// physics query, so nothing is allocated per frame).
  /// </summary>
  void UpdateCameraDistance(float dt)
  {
    var wanted = ThirdPerson ? Tuning.ThirdPersonDistance : 0f;
    var terrain = Terrain.Instance;

    if (wanted > 0f && terrain != null)
    {
      var origin = CameraPivot.GlobalPosition;
      var back = CameraPivot.GlobalTransform.Basis.Z;

      for (float t = 0.25f; t <= wanted; t += 0.1f)
      {
        var p = origin + back * t;

        if (terrain.IsSolid(VoxelMath.WorldToVoxel(p.X), VoxelMath.WorldToVoxel(p.Y), VoxelMath.WorldToVoxel(p.Z)))
        {
          wanted = Math.Max(0.2f, t - 0.3f);
          break;
        }
      }
    }

    // Snap in when something is in the way, ease back out.
    cameraDistance = wanted < cameraDistance ? wanted : Mathf.Lerp(cameraDistance, wanted, 1f - Mathf.Exp(-10f * dt));
    Camera.Position = new Vector3(0f, 0f, cameraDistance);
  }

  public override void _PhysicsProcess(double delta)
  {
    var dt = (float)delta;

    // Wait (frozen) until the ground under us exists, so we never fall through a chunk that is still being built.
    var terrain = Terrain.Instance;

    if (terrain != null && terrain.IsRunning && !terrain.HasCollisionAt(GlobalPosition))
    {
      Velocity = Vector3.Zero;
      Publish();
      smoother.Record(GlobalPosition);
      return;
    }

    var alive = !Health.IsDead;
    var active = alive && Input.MouseMode == Input.MouseModeEnum.Captured;

    var move = active
      ? Input.GetVector(Actions.MoveLeft, Actions.MoveRight, Actions.MoveForward, Actions.MoveBack)
      : Vector2.Zero;

    var sprinting = active && move != Vector2.Zero && Input.IsActionPressed(Actions.Sprint);

    if (Noclip)
    {
      Fly(dt, move, active, sprinting);
      return;
    }

    if (GlobalPosition.Y < Tuning.KillHeight)
    {
      Respawn();
    }

    var onFloor = IsOnFloor();
    var velocity = Velocity;

    // Horizontal: smooth toward the wish direction, independent of frame rate.

    var wish = GlobalTransform.Basis * new Vector3(move.X, 0f, move.Y);
    wish.Y = 0f;

    if (wish.LengthSquared() > 1f)
    {
      wish = wish.Normalized();
    }

    var rate = onFloor ? Tuning.Responsiveness : Tuning.AirResponsiveness;
    horizontal = horizontal.Lerp(wish * Tuning.Speed * (sprinting ? Tuning.SprintMultiplier : 1f), 1f - Mathf.Exp(-rate * dt));

    // Vertical: gravity, jump buffer and a little coyote time.
    coyote = onFloor ? CoyoteSeconds : coyote - dt;
    // Held, not just pressed: keep hopping for as long as the key is down.
    jumpBuffer = active && Input.IsActionPressed(Actions.Jump) ? JumpBufferSeconds : jumpBuffer - dt;

    if (onFloor)
    {
      velocity.Y = Math.Max(velocity.Y, 0f);
    }
    else
    {
      velocity.Y -= gravity * dt;
    }

    if (jumpBuffer > 0f && coyote > 0f)
    {
      velocity.Y = Tuning.JumpVelocity;
      jumpBuffer = 0f;
      coyote = 0f;
    }

    velocity.X = horizontal.X;
    velocity.Z = horizontal.Z;
    Velocity = velocity;

    if (onFloor)
    {
      TryStepUp(dt);
    }

    MoveAndSlide();
    BumpAgainstEnemies(dt);

    // If we ran into something, do not keep pushing into it with stored speed.
    horizontal = new Vector3(Velocity.X, 0f, Velocity.Z);

    Publish();
    smoother.Record(GlobalPosition);
  }

  /// <summary>Noclip: fly along the view direction, Jump rises and FlyDown sinks; no gravity, no collision.</summary>
  void Fly(float dt, Vector2 move, bool active, bool sprinting)
  {
    var wish = CameraPivot.GlobalTransform.Basis * new Vector3(move.X, 0f, move.Y);

    if (active)
    {
      wish.Y += (Input.IsActionPressed(Actions.Jump) ? 1f : 0f) - (Input.IsActionPressed(Actions.FlyDown) ? 1f : 0f);
    }

    if (wish.LengthSquared() > 1f)
    {
      wish = wish.Normalized();
    }

    var target = wish * Tuning.FlySpeed * (sprinting ? 3f : 1f);
    Velocity = Velocity.Lerp(target, 1f - Mathf.Exp(-10f * dt));
    MoveAndSlide(); // the collision mask is empty, so this just moves

    Publish();
    smoother.Record(GlobalPosition);
  }

  /// <summary>
  /// Walks up a ledge of up to StepHeight without jumping: if something blocks us at foot level but there is room
  /// above it and ground within StepHeight, lift the body onto it. The camera eases the lift out visually.
  /// </summary>
  void TryStepUp(float dt)
  {
    var flat = new Vector3(Velocity.X, 0f, Velocity.Z);

    if (flat.LengthSquared() < 0.25f)
    {
      return;
    }

    var direction = flat.Normalized();
    var near = direction * 0.2f;
    var start = GlobalTransform;

    if (!TestMove(start, near, probe) || Math.Abs(probe.GetNormal().Y) > 0.5f)
    {
      return; // nothing in the way, or it is a slope the normal floor handling deals with
    }

    var lift = Vector3.Up * Tuning.StepHeight;

    if (TestMove(start, lift))
    {
      return; // no headroom
    }

    // Probe far enough ahead that the whole capsule is over the ledge, so the landing test meets its flat top and not
    // just the rim (a rim contact would only lift us a little each tick).
    var far = direction * (BodyRadius + 0.2f);
    var raised = start;
    raised.Origin += lift;

    if (TestMove(raised, far))
    {
      return; // still blocked up there: a wall, not a step
    }

    var ahead = raised;
    ahead.Origin += far;

    if (!TestMove(ahead, Vector3.Down * Tuning.StepHeight, probe))
    {
      return; // no ground to land on
    }

    var rise = Tuning.StepHeight - probe.GetTravel().Length();

    if (rise < 0.02f)
    {
      return;
    }

    GlobalPosition += Vector3.Up * (rise + 0.005f);
    smoother.Reset(GlobalPosition);
    stepCameraOffset -= rise;
  }

  void Respawn()
  {
    GlobalPosition = SpawnPoint(VoxelMath.WorldToVoxel(GlobalPosition.X), VoxelMath.WorldToVoxel(GlobalPosition.Z)) + Vector3.Up * 0.5f;
    smoother.Reset(GlobalPosition);
    Velocity = Vector3.Zero;
    horizontal = Vector3.Zero;
  }

  void Publish()
  {
    SyncPosition = GlobalPosition;
    SyncYaw = Rotation.Y;
    SyncPitch = pitch;
    SyncVelocity = Velocity;
    SyncGrounded = IsOnFloor();
    SyncHeldItem = Inventory.HeldId;
  }

  /// <summary>
  /// Unity's OnCollisionEnter: running into an enemy shoves you away, (normal * 2 + up) normalised, scaled by 2. Only the
  /// owner of the body applies it (the same peer that simulates the movement).
  /// </summary>
  void BumpAgainstEnemies(float dt)
  {
    bumpCooldown -= dt;

    if (bumpCooldown > 0f)
    {
      return;
    }

    for (int i = 0; i < GetSlideCollisionCount(); i++)
    {
      var hit = GetSlideCollision(i);

      if (hit.GetCollider() is CollisionObject3D other && (other.CollisionLayer & GameLayers.Enemy) != 0)
      {
        ApplyKnockback((hit.GetNormal() * 2f + Vector3.Up).Normalized() * 2f);
        bumpCooldown = BumpCooldownSeconds;
        return;
      }
    }
  }

  // ------------------------------------------------------------------ damage (host decides, owner applies the motion)

  /// <summary>Host only (projectiles call this on the host).</summary>
  public void TakeDamage(DamageInfo info)
  {
    if (!Multiplayer.IsServer() || Health.IsDead)
    {
      return;
    }

    Health.Damage(info.Amount);

    var flat = new Vector3(info.Direction.X, 0f, info.Direction.Z).Normalized();
    var impulse = flat * info.Knockback + Vector3.Up * 2f;
    var owner = GetMultiplayerAuthority();

    if (owner == Multiplayer.GetUniqueId())
    {
      ApplyKnockback(impulse);
    }
    else
    {
      RpcId(owner, MethodName.RpcKnockback, impulse);
    }
  }

  void OnDiedOnHost()
  {
    GD.Print($"player {Name} died");

    GetTree().CreateTimer(3.0).Timeout += () =>
    {
      if (!IsInsideTree())
      {
        return;
      }

      Health.Reset();
      var spawn = SpawnPoint(VoxelMath.WorldToVoxel(GlobalPosition.X), VoxelMath.WorldToVoxel(GlobalPosition.Z));
      var owner = GetMultiplayerAuthority();

      if (owner == Multiplayer.GetUniqueId())
      {
        DoRespawn(spawn);
      }
      else
      {
        RpcId(owner, MethodName.RpcRespawn, spawn);
      }
    };
  }

  [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  void RpcKnockback(Vector3 impulse)
  {
    if (Multiplayer.GetRemoteSenderId() == 1)
    {
      ApplyKnockback(impulse);
    }
  }

  [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  void RpcRespawn(Vector3 position)
  {
    if (Multiplayer.GetRemoteSenderId() == 1)
    {
      DoRespawn(position);
    }
  }

  void ApplyKnockback(Vector3 impulse)
  {
    if (IsLocal)
    {
      Input.StartJoyVibration(0, 0.4f, 0.6f, 0.25f); // a short rumble on a controller; nothing without one
    }

    // Both the smoothed walk velocity and the body velocity get the push: the movement code rebuilds one from the other.
    horizontal += new Vector3(impulse.X, 0f, impulse.Z);
    Velocity = new Vector3(Velocity.X + impulse.X, Math.Max(Velocity.Y, impulse.Y), Velocity.Z + impulse.Z);
  }

  void DoRespawn(Vector3 position)
  {
    GlobalPosition = position;
    SyncPosition = position;
    smoother.Reset(position);
    Velocity = Vector3.Zero;
    horizontal = Vector3.Zero;
  }

  /// <summary>A spot just above the terrain surface at a voxel column, for spawning (set before adding to the tree).</summary>
  public static Vector3 SpawnPoint(int wx, int wz)
  {
    var height = Terrain.Instance?.SurfaceHeightAt(wx, wz) ?? 25f;

    return new Vector3(VoxelMath.VoxelCenter(wx), height + 1f, VoxelMath.VoxelCenter(wz));
  }
}
