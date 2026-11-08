using Godot;

namespace Curitibia.Core;

/// <summary>
/// Both Unity characters are scaled capsule primitives (radius 0.5, height 2, squashed with a scale). This builds the same
/// kind of part from one shared, lowish-poly capsule mesh.
/// </summary>
public static class CapsulePart
{
  static CapsuleMesh? mesh;

  static CapsuleMesh SharedMesh => mesh ??= new CapsuleMesh { Radius = 0.5f, Height = 2f, RadialSegments = 20, Rings = 6 };

  /// <summary>A capsule mesh node with its own position and non-uniform scale (children inherit the scale, like in Unity).</summary>
  public static MeshInstance3D Create(Node parent, string name, Material material, Vector3 position, Vector3 scale)
  {
    var part = new MeshInstance3D
    {
      Name = name,
      Mesh = SharedMesh,
      MaterialOverride = material,
      Position = position,
      Scale = scale,
    };

    parent.AddChild(part);

    return part;
  }

  public static Node3D Pivot(Node parent, string name, Vector3 position, Vector3? scale = null, Vector3? rotationDegrees = null)
  {
    var pivot = new Node3D { Name = name, Position = position, Scale = scale ?? Vector3.One };

    if (rotationDegrees != null)
    {
      pivot.RotationDegrees = rotationDegrees.Value;
    }

    parent.AddChild(pivot);

    return pivot;
  }
}
