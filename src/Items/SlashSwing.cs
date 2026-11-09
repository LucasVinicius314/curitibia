using System;
using System.Numerics;

namespace Curitibia.Items;

/// <summary>
/// Where a cut starts and ends as seen from the swinger's centre, in radians. Yaw: negative is left. Elevation: positive is
/// up. The cutting plane is the one through the centre and these two directions.
/// </summary>
public readonly record struct SlashArc(float StartYaw, float StartElevation, float EndYaw, float EndElevation)
{
  const float Deg = MathF.PI / 180f;

  /// <summary>From high on the right across the body to low on the left: the opening cut of the combo.</summary>
  public static readonly SlashArc Forehand = new(52f * Deg, 34f * Deg, -46f * Deg, -12f * Deg);

  /// <summary>From high on the left across to low on the right: the return cut.</summary>
  public static readonly SlashArc Backhand = new(-54f * Deg, 32f * Deg, 46f * Deg, -10f * Deg);
}

/// <summary>
/// Which cut comes next in a string of swings: the forehand first, then the backhand, then the forehand again and so on,
/// starting over with the forehand once the player has paused long enough for the arm to come back to rest. Engine-free.
/// </summary>
public sealed class SlashCombo
{
  /// <summary>Seconds after one swing starts within which the next one still continues the string.</summary>
  public const float Window = SlashSwing.Duration + 0.45f;

  double lastStart = double.NegativeInfinity;
  int next;

  /// <summary>The cut for a swing starting at <paramref name="now"/> (seconds): 0 is the forehand, 1 the backhand.</summary>
  public int Next(double now)
  {
    if (now - lastStart > Window)
    {
      next = 0;
    }

    lastStart = now;

    var cut = next;
    next = 1 - next;

    return cut;
  }

  public void Reset()
  {
    lastStart = double.NegativeInfinity;
    next = 0;
  }
}

/// <summary>Where the grip is and how the item is turned (blade along -Z, edge along +X), in the holder's frame.</summary>
public readonly record struct SwingPose(Vector3 Position, Quaternion Rotation);

/// <summary>
/// A one-handed sword cut as geometry, not as a pile of offsets. The swinger has a centre (the shoulder) and the cut lies in
/// one tilted plane through it, a diagonal across the body (see <see cref="SlashArc"/>: forehand from high right to low left,
/// backhand from high left to low right). The hand
/// travels an oval in that plane (the reach of the arm) and the tip of the blade a bigger oval (arm plus blade); the blade
/// is the line between the two, and the tip's point on its oval runs ahead of the hand along the cut by an angle that grows
/// through the stroke (the wrist snapping), so the blade always leans into the motion and whips through at the end. Because
/// everything stays in the cutting plane the sword never spins in the hand: its flat faces the plane's normal and its edge
/// turns with the stroke, facing the way the tip travels.
///
/// Time is: wind-up (hand drawn back and the blade cocked high on the left), the cut (accelerates through the middle and
/// stops), a short settle past the end, and a recovery to the rest pose. A swing that starts while another is still going
/// begins from wherever the item is. Engine-free (System.Numerics; +X right, +Y up, -Z forward) so it is unit-tested.
/// </summary>
public sealed class SlashSwing
{
  /// <summary>Total length of a swing in seconds.</summary>
  public const float Duration = 0.5f;

  public const float WindUp = 0.07f;
  public const float CutTime = 0.21f;
  public const float SettleTime = 0.03f;

  /// <summary>When the blade passes the middle of the cut: the moment damage should land.</summary>
  public const float ContactTime = WindUp + CutTime * 0.5f;

  /// <summary>How far, in the cutting plane, the tip is ahead of the hand: trailing a little as the cut starts, whipping ahead by its end.</summary>
  const float LeadStart = 0f * Rad;
  const float LeadEnd = 42f * Rad;

  /// <summary>Path parameter where the wind-up ends (a little before the cut proper, the blade drawn back) and how far past the end the follow-through goes.</summary>
  const float Cocked = -0.12f;
  const float Overtravel = 0.07f;

  const float Rad = MathF.PI / 180f;

  readonly Vector3 centre;
  readonly Vector3 handRadii;
  readonly Vector3 tipRadii;
  readonly float bladeLength;

  // The cutting plane: a unit vector from the centre where the cut starts, one across the plane toward where it goes, and the
  // angle between the start and the end directions.
  readonly Vector3 planeStart;
  readonly Vector3 planeAcross;
  readonly float arcAngle;

  /// <param name="centre">The shoulder: the middle of both ovals, in the holder's frame.</param>
  /// <param name="armLength">Reach from the centre to the hand.</param>
  /// <param name="bladeLength">Grip to tip of the item.</param>
  /// <param name="arc">Where the cut starts and ends.</param>
  public SlashSwing(Vector3 centre, float armLength, float bladeLength, SlashArc arc)
  {
    this.centre = centre;
    this.bladeLength = bladeLength;

    // Ovals: wider across the body than front to back, and flatter than they are wide.
    handRadii = new Vector3(0.95f, 0.8f, 1f) * armLength;
    tipRadii = new Vector3(1.05f, 0.8f, 0.95f) * (armLength + bladeLength * 0.95f);

    var end = Direction(arc.EndYaw, arc.EndElevation);

    planeStart = Direction(arc.StartYaw, arc.StartElevation);
    arcAngle = MathF.Acos(Math.Clamp(Vector3.Dot(planeStart, end), -1f, 1f));
    planeAcross = Vector3.Normalize(end - planeStart * Vector3.Dot(planeStart, end));
  }

  /// <summary>The direction in the cutting plane at an angle from where the cut starts.</summary>
  Vector3 Arc(float angle)
  {
    return planeStart * MathF.Cos(angle) + planeAcross * MathF.Sin(angle);
  }

  /// <summary>The pose at a point along the cut: 0 is its start, 1 its end (values outside are the wind-up and follow-through).</summary>
  public SwingPose Cut(float p)
  {
    const float step = 0.01f;

    var (hand, blade) = Frame(p);
    var (handAhead, bladeAhead) = Frame(p + step);
    var (handBehind, bladeBehind) = Frame(p - step);

    // Where the tip is going, from the path itself (not from time, so it never vanishes at the ends of the cut).
    var tipAhead = handAhead + bladeAhead * bladeLength;
    var tipBehind = handBehind + bladeBehind * bladeLength;
    var travel = tipAhead - tipBehind;

    // The edge faces the travel: the part of the travel across the blade.
    var edge = travel - blade * Vector3.Dot(travel, blade);

    if (edge.LengthSquared() < 1e-8f)
    {
      edge = Vector3.Cross(Vector3.UnitY, blade);
    }

    edge = Vector3.Normalize(edge);

    var z = -blade;
    var y = Vector3.Cross(z, edge);

    var rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
      edge.X, edge.Y, edge.Z, 0f,
      y.X, y.Y, y.Z, 0f,
      z.X, z.Y, z.Z, 0f,
      0f, 0f, 0f, 1f)));

    return new SwingPose(hand, rotation);
  }

  /// <summary>Hand position and the direction the blade points, at a path parameter.</summary>
  (Vector3 hand, Vector3 blade) Frame(float p)
  {
    var along = Math.Clamp(p, 0f, 1f);
    var snap = along * along * (3f - 2f * along);

    var hand = centre + handRadii * Arc(p * arcAngle);
    var tip = centre + tipRadii * Arc(p * arcAngle + Lerp(LeadStart, LeadEnd, snap));

    return (hand, Vector3.Normalize(tip - hand));
  }

  static Vector3 Direction(float yaw, float elevation)
  {
    var c = MathF.Cos(elevation);

    return new Vector3(MathF.Sin(yaw) * c, MathF.Sin(elevation), -MathF.Cos(yaw) * c);
  }

  /// <summary>
  /// The pose <paramref name="t"/> seconds into a swing. <paramref name="from"/> is where the item was when the swing
  /// started, <paramref name="rest"/> where it settles afterwards.
  /// </summary>
  public SwingPose At(float t, SwingPose from, SwingPose rest)
  {
    if (t <= 0f)
    {
      return from;
    }

    if (t < WindUp)
    {
      return Blend(from, Cut(Cocked), SmoothStep(t / WindUp));
    }

    if (t < WindUp + CutTime)
    {
      var u = (t - WindUp) / CutTime;

      return Cut(Lerp(Cocked, 1f, SmoothStep(u)));
    }

    var settleEnd = WindUp + CutTime + SettleTime;

    if (t < settleEnd)
    {
      var u = (t - WindUp - CutTime) / SettleTime;

      return Cut(1f + Overtravel * EaseOut(u));
    }

    if (t < Duration)
    {
      var u = (t - settleEnd) / (Duration - settleEnd);

      // Back the way it came: up the arc (so the blade never swings through the holder's face, and the turn stays about the
      // cutting plane), fading into the rest pose only once the two are well under half a turn apart. The end of the cut is
      // almost exactly opposite the rest orientation, so blending straight to it would flip between two ways round.
      var back = Lerp(1f + Overtravel, 0.65f, SmoothStep(u / 0.85f));
      var fade = SmoothStep((u - 0.3f) / 0.7f);

      return Blend(Cut(back), rest, fade);
    }

    return rest;
  }

  /// <summary>How much the holder's arm follows the grip (0 resting, 1 swinging), for drawing the arm.</summary>
  public static float ArmWeight(float t)
  {
    if (t <= 0f || t >= Duration)
    {
      return 0f;
    }

    var recoveryStart = WindUp + CutTime + SettleTime;

    if (t < WindUp * 0.8f)
    {
      return SmoothStep(t / (WindUp * 0.8f));
    }

    return t < recoveryStart ? 1f : 1f - SmoothStep((t - recoveryStart) / (Duration - recoveryStart));
  }

  static SwingPose Blend(SwingPose a, SwingPose b, float k)
  {
    return new SwingPose(Vector3.Lerp(a.Position, b.Position, k), Quaternion.Slerp(a.Rotation, b.Rotation, k));
  }

  static float Lerp(float a, float b, float k)
  {
    return a + (b - a) * k;
  }

  static float SmoothStep(float x)
  {
    x = Math.Clamp(x, 0f, 1f);

    return x * x * (3f - 2f * x);
  }

  static float EaseOut(float x)
  {
    x = Math.Clamp(x, 0f, 1f);

    return 1f - (1f - x) * (1f - x) * (1f - x);
  }
}
