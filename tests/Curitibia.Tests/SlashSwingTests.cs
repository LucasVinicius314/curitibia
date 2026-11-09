using System;
using System.Collections.Generic;
using System.Numerics;
using Curitibia.Items;
using Xunit;
using Xunit.Abstractions;

namespace Curitibia.Tests;

public class SlashSwingTests
{
  const float Blade = 0.92f;
  static readonly Vector3 Centre = new(0.16f, -0.34f, -0.18f);

  readonly ITestOutputHelper output;

  public SlashSwingTests(ITestOutputHelper output)
  {
    this.output = output;
  }

  static SlashSwing Make(SlashArc arc)
  {
    return new SlashSwing(Centre, 0.5f, Blade, arc);
  }

  /// <summary>Name, the arc, and which way the tip travels across the body (+1 left to right, -1 right to left).</summary>
  public static IEnumerable<object[]> Arcs()
  {
    yield return new object[] { "forehand", SlashArc.Forehand, -1f };
    yield return new object[] { "backhand", SlashArc.Backhand, 1f };
  }

  static SwingPose Rest()
  {
    return new SwingPose(new Vector3(0.3f, -0.28f, -0.42f), Quaternion.CreateFromYawPitchRoll(0f, 1.48f, -1.57f));
  }

  static Vector3 Tip(SwingPose pose)
  {
    return pose.Position + Vector3.Transform(new Vector3(0f, 0f, -Blade), pose.Rotation);
  }

  static Vector3 BladeDirection(SwingPose pose)
  {
    return Vector3.Transform(new Vector3(0f, 0f, -1f), pose.Rotation);
  }

  [Theory]
  [MemberData(nameof(Arcs))]
  public void The_tip_crosses_the_body_and_comes_down(string name, SlashArc arc, float across)
  {
    var swing = Make(arc);
    var start = Tip(swing.Cut(0f));
    var end = Tip(swing.Cut(1f));

    output.WriteLine($"{name}: tip start {start}, end {end}");
    Assert.True((end.X - start.X) * across > 1.2f, "travels across the body");
    Assert.True(start.Y > end.Y + 0.3f, "comes down through the cut");
    Assert.True(start.Z < Centre.Z - 0.3f, "starts in front of the swinger");
    Assert.True(end.Z < Centre.Z + 0.4f, "never ends far behind the shoulder");
  }

  [Fact]
  public void The_forehand_starts_high_on_the_right_and_the_backhand_high_on_the_left()
  {
    var forehand = Tip(Make(SlashArc.Forehand).Cut(0f));
    var backhand = Tip(Make(SlashArc.Backhand).Cut(0f));

    Assert.True(forehand.X > Centre.X + 0.3f && forehand.Y > Centre.Y + 0.3f);
    Assert.True(backhand.X < Centre.X - 0.3f && backhand.Y > Centre.Y + 0.3f);
  }

  [Theory]
  [MemberData(nameof(Arcs))]
  public void The_tip_stays_on_an_oval_around_the_centre(string name, SlashArc arc, float across)
  {
    var swing = Make(arc);
    var min = float.MaxValue;
    var max = 0f;

    for (float p = 0f; p <= 1f; p += 0.02f)
    {
      var d = (Tip(swing.Cut(p)) - Centre).Length();
      min = Math.Min(min, d);
      max = Math.Max(max, d);
    }

    output.WriteLine($"{name}: tip distance from the centre {min:F2} to {max:F2} m (arm 0.5 + blade {Blade})");
    Assert.InRange(min, 1.0f, 1.8f);
    Assert.InRange(max, 1.0f, 1.8f);
    Assert.True(max - min < 0.45f, "the path is a smooth oval, not a wobble");
  }

  [Theory]
  [MemberData(nameof(Arcs))]
  public void The_blade_leans_into_the_motion_and_the_edge_leads(string name, SlashArc arc, float across)
  {
    var swing = Make(arc);
    var lean = new List<float>();

    for (float p = 0.3f; p <= 0.9f; p += 0.1f)
    {
      var a = swing.Cut(p - 0.02f);
      var b = swing.Cut(p + 0.02f);
      var travel = Vector3.Normalize(Tip(b) - Tip(a));
      var pose = swing.Cut(p);
      var blade = BladeDirection(pose);
      var edge = Vector3.Transform(new Vector3(1f, 0f, 0f), pose.Rotation);

      lean.Add(Vector3.Dot(blade, travel));
      Assert.True(Vector3.Dot(blade, travel) > 0.05f, $"{name}: blade leans along the travel at {p}");
      Assert.True(Math.Abs(Vector3.Dot(edge, travel)) > 0.8f, $"{name}: edge faces the travel at {p}");
    }

    output.WriteLine($"{name}: blade . travel: " + string.Join(", ", lean.ConvertAll(x => x.ToString("F2"))));
    Assert.True(lean[^1] > lean[0] + 0.1f, "the lean grows through the cut (the wrist snaps)");
  }

  [Theory]
  [MemberData(nameof(Arcs))]
  public void The_swing_is_continuous_and_fast_only_in_the_cut(string name, SlashArc arc, float across)
  {
    var swing = Make(arc);
    var rest = Rest();
    var from = rest;
    var previous = swing.At(0.0005f, from, rest);
    var peakAt = 0f;
    var peak = 0f;

    for (float t = 0.001f; t <= SlashSwing.Duration; t += 0.001f)
    {
      var pose = swing.At(t, from, rest);
      var tipStep = (Tip(pose) - Tip(previous)).Length();
      var angle = 2f * MathF.Acos(Math.Clamp(Math.Abs(Quaternion.Dot(pose.Rotation, previous.Rotation)), 0f, 1f));

      Assert.True(tipStep < 0.036f, $"{name}: tip jumped {tipStep} m at {t}");
      Assert.True(angle < 0.04f, $"{name}: blade turned {angle} rad in a millisecond at {t}");

      if (tipStep > peak)
      {
        peak = tipStep;
        peakAt = t;
      }

      previous = pose;
    }

    output.WriteLine($"{name}: fastest tip {peak * 1000f:F1} m/s at {peakAt:F3} s");
    Assert.InRange(peakAt, SlashSwing.WindUp, SlashSwing.WindUp + SlashSwing.CutTime);
  }

  [Theory]
  [MemberData(nameof(Arcs))]
  public void It_starts_where_it_was_and_ends_at_rest(string name, SlashArc arc, float across)
  {
    var swing = Make(arc);
    var rest = Rest();
    var from = new SwingPose(new Vector3(0.5f, 0f, -0.3f), Quaternion.Identity);

    Assert.Equal(from, swing.At(0f, from, rest));
    Assert.True((swing.At(SlashSwing.Duration, from, rest).Position - rest.Position).Length() < 1e-5f);
    Assert.True((swing.At(SlashSwing.Duration - 0.002f, from, rest).Position - rest.Position).Length() < 0.02f);
  }

  [Fact]
  public void The_arm_weight_rises_holds_and_falls()
  {
    Assert.Equal(0f, SlashSwing.ArmWeight(0f));
    Assert.Equal(1f, SlashSwing.ArmWeight(SlashSwing.ContactTime));
    Assert.Equal(0f, SlashSwing.ArmWeight(SlashSwing.Duration));
    Assert.InRange(SlashSwing.ArmWeight(0.45f), 0.01f, 0.99f);
  }

  [Fact]
  public void Consecutive_swings_alternate_forehand_then_backhand()
  {
    var combo = new SlashCombo();

    // Held down: one swing every half second.
    Assert.Equal(0, combo.Next(10.0));
    Assert.Equal(1, combo.Next(10.5));
    Assert.Equal(0, combo.Next(11.0));
    Assert.Equal(1, combo.Next(11.5));
  }

  [Fact]
  public void A_pause_starts_the_string_over_with_the_forehand()
  {
    var combo = new SlashCombo();

    Assert.Equal(0, combo.Next(5.0));
    Assert.Equal(1, combo.Next(5.6));
    Assert.Equal(0, combo.Next(7.5)); // waited: back to the opening cut, not the backhand again
    Assert.Equal(1, combo.Next(8.0));
    Assert.Equal(0, combo.Next(30.0));
  }

  [Fact]
  public void A_chained_swing_starts_from_the_end_of_the_last_one()
  {
    // The forehand ends low on the left, where the backhand begins from: the second swing carries on from where the sword is.
    var rest = Rest();
    var forehand = Make(SlashArc.Forehand);
    var backhand = Make(SlashArc.Backhand);
    var endOfFirst = forehand.At(0.4f, rest, rest);
    var startOfSecond = backhand.At(0.0005f, endOfFirst, rest);

    Assert.True((startOfSecond.Position - endOfFirst.Position).Length() < 0.01f);
  }
}
