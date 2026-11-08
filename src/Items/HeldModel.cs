using Godot;

namespace Curitibia.Items;

/// <summary>
/// An item model in somebody's hand plus the use animation (a right-to-left sweep with the blade turning into the cut, or an
/// overhead chop), then back to rest. Pure presentation, one per place an item is shown (the body, and the first-person view).
/// </summary>
public partial class HeldModel : Node3D
{
  const float SwingSeconds = 0.5f;

  float swing = SwingSeconds;
  float rest;
  float roll;
  float amount = 1f;
  SwingStyle style;
  Vector3 restPosition;
  bool restKnown;

  public void SetModel(ItemModel model)
  {
    if (!restKnown)
    {
      restPosition = Position;
      restKnown = true;
    }

    Position = restPosition;

    foreach (var child in GetChildren())
    {
      child.QueueFree();
    }

    rest = Mathf.DegToRad(model.RestPitchDegrees);
    roll = Mathf.DegToRad(model.RestRollDegrees);
    amount = model.SwingAmount;
    style = model.Swing;
    Rotation = new Vector3(rest, 0f, roll);
    AddChild(model.Build());
  }

  public void Play()
  {
    swing = 0f;
  }

  public override void _Process(double delta)
  {
    if (swing >= SwingSeconds)
    {
      return;
    }

    swing = Mathf.Min(SwingSeconds, swing + (float)delta);
    var t = swing / SwingSeconds;

    if (style == SwingStyle.Chop)
    {
      Chop(t);
    }
    else
    {
      Sweep(t);
    }
  }

  /// <summary>
  /// A slash from the right across to the left, like a real sword cut: the blade is drawn back to the right and a little
  /// up (anticipation), whips across the body in a fast arc while it lays down and the edge turns 90 degrees into the cut,
  /// carries a little past the target, then comes back to rest. Offsets are from the rest pose and scale with SwingAmount.
  /// </summary>
  void Sweep(float t)
  {
    const float anticipation = 0.18f; // end of the draw-back
    const float strike = 0.52f;       // end of the cut
    const float followThrough = 0.66f;

    // Poses. Tilt is the lean of the blade across the screen (negative = tip to the right, positive = to the left): it is
    // what makes the diagonal swipe. Yaw and pitch add the cut across and forward, the edge turn rolls the blade 90 degrees.
    var drawTilt = -0.65f;  var drawPitch = 0f;      var drawYaw = 0f;     var drawHand = new Vector3(0.1f, 0.07f, 0.05f);
    var cutTilt = 1.4f;     var cutPitch = -0.55f;   var cutYaw = 0.45f;   var cutHand = new Vector3(-0.16f, -0.1f, -0.06f);

    float tilt;
    float yaw;
    float pitch;
    float turned;
    Vector3 hand;

    if (t < anticipation)
    {
      var k = Mathf.Ease(t / anticipation, 0.5f); // quick, slowing into the draw
      tilt = Mathf.Lerp(0f, drawTilt, k);
      yaw = drawYaw * k;
      pitch = drawPitch * k;
      turned = 0f;
      hand = Vector3.Zero.Lerp(drawHand, k);
    }
    else if (t < strike)
    {
      // The cut: slow to start, then fast, so the blade whips through.
      var k = Mathf.Ease((t - anticipation) / (strike - anticipation), 2.4f);
      tilt = Mathf.Lerp(drawTilt, cutTilt, k);
      yaw = Mathf.Lerp(drawYaw, cutYaw, k);
      pitch = Mathf.Lerp(drawPitch, cutPitch, Mathf.Sqrt(k)); // the blade drops forward early and stays out in front
      turned = k;
      hand = drawHand.Lerp(cutHand, k);
      hand.Z -= Mathf.Sin(k * Mathf.Pi) * 0.12f; // the arm extends through the middle of the cut
    }
    else if (t < followThrough)
    {
      var k = (t - strike) / (followThrough - strike);
      tilt = cutTilt + 0.1f * k;
      yaw = cutYaw;
      pitch = cutPitch;
      turned = 1f;
      hand = cutHand;
    }
    else
    {
      var k = Mathf.Ease((t - followThrough) / (1f - followThrough), 0.6f);
      tilt = Mathf.Lerp(cutTilt + 0.1f, 0f, k);
      yaw = Mathf.Lerp(cutYaw, 0f, k);
      pitch = Mathf.Lerp(cutPitch, 0f, k);
      turned = 1f - k;
      hand = cutHand.Lerp(Vector3.Zero, k);
    }

    var local = Basis.FromEuler(new Vector3(rest + pitch * amount, yaw * amount, roll + Mathf.Pi / 2f * turned * amount));
    Basis = new Basis(Vector3.Back, tilt * amount) * local;
    Position = restPosition + hand * amount;
  }

  /// <summary>An overhead chop: raised, then down and forward past the target, then back.</summary>
  void Chop(float t)
  {
    float pitch;

    if (t < 0.5f)
    {
      var k = Mathf.Ease(t / 0.5f, 2.0f);
      pitch = Mathf.Lerp(0.5f, -1.5f, k);
    }
    else
    {
      var k = Mathf.Ease((t - 0.5f) / 0.5f, 0.6f);
      pitch = Mathf.Lerp(-1.5f, 0f, k);
    }

    Rotation = new Vector3(rest + pitch * amount, 0f, roll);
    Position = restPosition + new Vector3(0f, 0f, -Mathf.Sin(Mathf.Clamp(t / 0.5f, 0f, 1f) * Mathf.Pi) * 0.1f);
  }
}
