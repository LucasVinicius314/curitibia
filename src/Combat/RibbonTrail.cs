using System;
using Curitibia.Core;
using Godot;

namespace Curitibia.Combat;

/// <summary>
/// A camera-facing ribbon that follows its parent: the last few positions as a tapering triangle strip, drawn with the
/// ported Unity trail shader (scrolling smoke texture, red to blue). One ImmediateMesh is rebuilt each frame (no engine
/// objects are created per frame); it is a top-level node so the ribbon stays in the world while the parent moves.
/// </summary>
public partial class RibbonTrail : MeshInstance3D
{
  /// <summary>Positions kept (one per physics tick), so the trail is about Points / 60 seconds long.</summary>
  [Export] public int Points { get; set; } = 28;

  /// <summary>Ribbon width at the head, in metres.</summary>
  [Export] public float Width { get; set; } = 0.6f;

  readonly ImmediateMesh ribbon = new();
  Vector3[] positions = Array.Empty<Vector3>();
  int count;
  Node3D? source;

  public override void _Ready()
  {
    positions = new Vector3[Math.Max(2, Points)];
    source = GetParent<Node3D>();

    TopLevel = true;
    GlobalTransform = Transform3D.Identity;
    Mesh = ribbon;
    MaterialOverride = GameMaterials.Trail;
    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    ExtraCullMargin = 4f;

    Push(source.GlobalPosition);
  }

  public override void _PhysicsProcess(double delta)
  {
    if (source != null)
    {
      Push(source.GlobalPosition);
    }
  }

  void Push(Vector3 position)
  {
    Array.Copy(positions, 0, positions, 1, positions.Length - 1);
    positions[0] = position;
    count = Math.Min(count + 1, positions.Length);
  }

  public override void _Process(double delta)
  {
    ribbon.ClearSurfaces();

    var camera = GetViewport().GetCamera3D();

    if (count < 2 || camera == null)
    {
      return;
    }

    var eye = camera.GlobalPosition;

    ribbon.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);

    for (int i = 0; i < count; i++)
    {
      var previous = positions[Math.Max(i - 1, 0)];
      var next = positions[Math.Min(i + 1, count - 1)];
      var tangent = (previous - next).Normalized();
      var side = tangent.Cross(eye - positions[i]);

      if (side.LengthSquared() < 1e-8f)
      {
        side = Vector3.Right;
      }

      var along = i / (float)(count - 1);
      var half = side.Normalized() * (Width * 0.5f) * (1f - along * 0.85f);

      ribbon.SurfaceSetUV(new Vector2(along, 0f));
      ribbon.SurfaceAddVertex(positions[i] - half);
      ribbon.SurfaceSetUV(new Vector2(along, 1f));
      ribbon.SurfaceAddVertex(positions[i] + half);
    }

    ribbon.SurfaceEnd();
  }
}
