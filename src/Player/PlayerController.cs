using System;
using Curitibia.Core;
using Curitibia.World;
using Godot;

namespace Curitibia.Player;

/// <summary>
/// The player body. Only the peer that owns it (multiplayer authority) reads input and simulates; every other peer just
/// shows the replicated state (Sync* properties, replicated by a MultiplayerSynchronizer in the prefab) smoothed in
/// _Process. Walking, jumping and the one-block step-up live here; looking and block editing are separate nodes/parts.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
  [Export] public PlayerTuning Tuning { get; set; } = null!;
  [Export] public Node3D CameraPivot { get; set; } = null!;
  [Export] public Camera3D Camera { get; set; } = null!;
  [Export] public PlayerVisual Visual { get; set; } = null!;
  [Export] public BlockInteractor Interactor { get; set; } = null!;

  // Replicated state (the authority writes it every physics tick, everyone else reads it).
  [Export] public Vector3 SyncPosition { get; set; }
  [Export] public float SyncYaw { get; set; }
  [Export] public float SyncPitch { get; set; }
  [Export] public Vector3 SyncVelocity { get; set; }
  [Export] public bool SyncGrounded { get; set; }

  public Team Team => Team.Players;

  /// <summary>True on the peer that controls this player (always true offline).</summary>
  public bool IsLocal => IsMultiplayerAuthority();

  /// <summary>World-space box of the body, used to keep block placement out of the player.</summary>
  public Aabb BodyBounds => new(GlobalPosition + new Vector3(-0.4f, 0f, -0.4f), new Vector3(0.8f, 1.8f, 0.8f));

  const float BodyRadius = 0.4f;
  const float JumpBufferSeconds = 0.12f;
  const float CoyoteSeconds = 0.1f;

  readonly KinematicCollision3D probe = new();

  float gravity;
  float pitch;
  float jumpBuffer;
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
  }

  public override void _Ready()
  {
    gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8);
    cameraBaseY = CameraPivot.Position.Y;

    // A spawned player arrives with the position the host chose in SyncPosition; a hand-placed one reports its own.
    if (SyncPosition != Vector3.Zero)
    {
      GlobalPosition = SyncPosition;
    }
    else
    {
      SyncPosition = GlobalPosition;
    }

    Terrain.Instance?.AddViewer(this);

    if (IsLocal)
    {
      Camera.MakeCurrent();
      Input.MouseMode = Input.MouseModeEnum.Captured;
      Visual.Visible = false; // first person: do not draw our own body around the camera
    }
    else
    {
      Camera.Current = false;
      SetPhysicsProcess(false);
    }
  }

  public override void _ExitTree()
  {
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

    if (@event is InputEventMouseMotion motion)
    {
      // Mouse deltas are already per-frame distances: no delta-time scaling (that would make it frame-rate dependent).
      Look(-motion.Relative.X * Tuning.MouseSensitivity, -motion.Relative.Y * Tuning.MouseSensitivity);
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

    if (IsLocal)
    {
      var stick = Input.GetVector(Actions.LookLeft, Actions.LookRight, Actions.LookUp, Actions.LookDown);

      if (stick != Vector2.Zero && Input.MouseMode == Input.MouseModeEnum.Captured)
      {
        Look(-stick.X * Tuning.StickSensitivity * dt, -stick.Y * Tuning.StickSensitivity * dt);
      }

      // Ease the camera back down after a step-up so climbing a block is not a pop.
      stepCameraOffset = Mathf.Lerp(stepCameraOffset, 0f, 1f - Mathf.Exp(-14f * dt));
      CameraPivot.Position = new Vector3(0f, cameraBaseY + stepCameraOffset, 0f);

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

  public override void _PhysicsProcess(double delta)
  {
    var dt = (float)delta;

    // Wait (frozen) until the ground under us exists, so we never fall through a chunk that is still being built.
    var terrain = Terrain.Instance;

    if (terrain != null && terrain.IsRunning && !terrain.HasCollisionAt(GlobalPosition))
    {
      Velocity = Vector3.Zero;
      Publish();
      return;
    }

    if (GlobalPosition.Y < Tuning.KillHeight)
    {
      Respawn();
    }

    var onFloor = IsOnFloor();
    var velocity = Velocity;

    // Horizontal: smooth toward the wish direction, independent of frame rate.
    var move = Input.MouseMode == Input.MouseModeEnum.Captured
      ? Input.GetVector(Actions.MoveLeft, Actions.MoveRight, Actions.MoveForward, Actions.MoveBack)
      : Vector2.Zero;

    var wish = GlobalTransform.Basis * new Vector3(move.X, 0f, move.Y);
    wish.Y = 0f;

    if (wish.LengthSquared() > 1f)
    {
      wish = wish.Normalized();
    }

    var rate = onFloor ? Tuning.Responsiveness : Tuning.AirResponsiveness;
    horizontal = horizontal.Lerp(wish * Tuning.Speed, 1f - Mathf.Exp(-rate * dt));

    // Vertical: gravity, jump buffer and a little coyote time.
    coyote = onFloor ? CoyoteSeconds : coyote - dt;
    jumpBuffer = Input.IsActionJustPressed(Actions.Jump) ? JumpBufferSeconds : jumpBuffer - dt;

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

    // If we ran into something, do not keep pushing into it with stored speed.
    horizontal = new Vector3(Velocity.X, 0f, Velocity.Z);

    Publish();
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
    stepCameraOffset -= rise;
  }

  void Respawn()
  {
    var x = VoxelMath.FloorToInt(GlobalPosition.X);
    var z = VoxelMath.FloorToInt(GlobalPosition.Z);
    var height = Terrain.Instance?.SurfaceHeightAt(x, z) ?? 20f;

    GlobalPosition = new Vector3(x + 0.5f, height + 2f, z + 0.5f);
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
  }

  /// <summary>A spot just above the terrain surface at a world column, for spawning (set before adding to the tree).</summary>
  public static Vector3 SpawnPoint(int wx, int wz)
  {
    var height = Terrain.Instance?.SurfaceHeightAt(wx, wz) ?? 20f;

    return new Vector3(wx + 0.5f, height + 1.5f, wz + 0.5f);
  }
}
