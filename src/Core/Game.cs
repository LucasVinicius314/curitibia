using Godot;

namespace Curitibia.Core;

/// <summary>Input action names, so gameplay code never uses string literals.</summary>
public static class Actions
{
  public static readonly StringName MoveForward = "move_forward";
  public static readonly StringName MoveBack = "move_back";
  public static readonly StringName MoveLeft = "move_left";
  public static readonly StringName MoveRight = "move_right";
  public static readonly StringName Jump = "jump";
  public static readonly StringName BreakBlock = "break_block";
  public static readonly StringName PlaceBlock = "place_block";
  public static readonly StringName LookLeft = "look_left";
  public static readonly StringName LookRight = "look_right";
  public static readonly StringName LookUp = "look_up";
  public static readonly StringName LookDown = "look_down";
  public static readonly StringName NextBlock = "next_block";
  public static readonly StringName PrevBlock = "prev_block";
  public static readonly StringName Pause = "pause";
  public static readonly StringName Roster = "roster";
}

/// <summary>
/// Autoload: registers the input map in code (one place to read and edit, instead of serialized events in project.godot).
/// </summary>
public partial class Game : Node
{
  public override void _EnterTree()
  {
    RegisterInput();
  }

  static void RegisterInput()
  {
    AddKeys(Actions.MoveForward, Key.W, Key.Up);
    AddKeys(Actions.MoveBack, Key.S, Key.Down);
    AddKeys(Actions.MoveLeft, Key.A, Key.Left);
    AddKeys(Actions.MoveRight, Key.D, Key.Right);
    AddKeys(Actions.Jump, Key.Space);
    AddKeys(Actions.Pause, Key.Escape);
    AddKeys(Actions.Roster, Key.Tab);

    AddAxis(Actions.MoveForward, JoyAxis.LeftY, -1f);
    AddAxis(Actions.MoveBack, JoyAxis.LeftY, 1f);
    AddAxis(Actions.MoveLeft, JoyAxis.LeftX, -1f);
    AddAxis(Actions.MoveRight, JoyAxis.LeftX, 1f);
    AddAxis(Actions.LookLeft, JoyAxis.RightX, -1f);
    AddAxis(Actions.LookRight, JoyAxis.RightX, 1f);
    AddAxis(Actions.LookUp, JoyAxis.RightY, -1f);
    AddAxis(Actions.LookDown, JoyAxis.RightY, 1f);
    AddButton(Actions.Jump, JoyButton.A);
    AddButton(Actions.Pause, JoyButton.Start);
    AddButton(Actions.Roster, JoyButton.Back);
    AddButton(Actions.NextBlock, JoyButton.RightShoulder);
    AddButton(Actions.PrevBlock, JoyButton.LeftShoulder);

    AddMouse(Actions.BreakBlock, MouseButton.Left);
    AddMouse(Actions.PlaceBlock, MouseButton.Right);
    AddMouse(Actions.NextBlock, MouseButton.WheelDown);
    AddMouse(Actions.PrevBlock, MouseButton.WheelUp);
    AddAxis(Actions.BreakBlock, JoyAxis.TriggerRight, 1f);
    AddAxis(Actions.PlaceBlock, JoyAxis.TriggerLeft, 1f);
  }

  static void Ensure(StringName action)
  {
    if (!InputMap.HasAction(action))
    {
      InputMap.AddAction(action, 0.2f);
    }
  }

  static void AddKeys(StringName action, params Key[] keys)
  {
    Ensure(action);

    foreach (var key in keys)
    {
      InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
  }

  static void AddMouse(StringName action, MouseButton button)
  {
    Ensure(action);
    InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
  }

  static void AddAxis(StringName action, JoyAxis axis, float value)
  {
    Ensure(action);
    InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = axis, AxisValue = value });
  }

  static void AddButton(StringName action, JoyButton button)
  {
    Ensure(action);
    InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });
  }
}
