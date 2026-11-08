using Godot;

namespace Curitibia.Core;

/// <summary>
/// Bodies only move on physics ticks, which do not line up with rendered frames: at 160 fps on 60 Hz physics most frames
/// show no motion and a few show a jump. This keeps the last two tick positions so presentation (the camera, a visual) can
/// sit between them using Engine.GetPhysicsInterpolationFraction(). Engine physics interpolation stays off (it lags and
/// ghosts a first-person camera); this is the manual version, as in fracture-protocol.
/// </summary>
public struct TickSmoother
{
  Vector3 previous;
  Vector3 current;
  bool started;

  /// <summary>Call once per physics tick with the body's position.</summary>
  public void Record(Vector3 position)
  {
    previous = started ? current : position;
    current = position;
    started = true;

    // A teleport (respawn, step into a new spot) must not be smeared across the jump.
    if (previous.DistanceSquaredTo(current) > 4f)
    {
      previous = current;
    }
  }

  /// <summary>Forget the history after moving the body by hand.</summary>
  public void Reset(Vector3 position)
  {
    previous = position;
    current = position;
    started = true;
  }

  /// <summary>Where the body is right now, between the last two ticks.</summary>
  public readonly Vector3 Blend(Vector3 fallback)
  {
    return started
      ? previous.Lerp(current, (float)Engine.GetPhysicsInterpolationFraction())
      : fallback;
  }
}
