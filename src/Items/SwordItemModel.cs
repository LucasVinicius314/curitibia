using Curitibia.Core;
using Godot;

namespace Curitibia.Items;

/// <summary>A copper sword of three boxes (blade, guard, wooden grip). The grip is at the origin and the blade points along -Z.</summary>
[GlobalClass]
public partial class SwordItemModel : ItemModel
{
  static readonly BoxMesh blade = new() { Size = new Vector3(0.07f, 0.02f, 0.8f) };
  static readonly BoxMesh guard = new() { Size = new Vector3(0.2f, 0.035f, 0.04f) };
  static readonly BoxMesh grip = new() { Size = new Vector3(0.04f, 0.04f, 0.16f) };

  /// <summary>Grip to tip: the guard, the blade and the half-length of the grip behind.</summary>
  public override float BladeLength => 0.92f;

  public override Node3D Build()
  {
    var root = new Node3D();

    // Grip from +0.08 to -0.08, the guard right in front of it, then the blade, all touching.
    Add(root, grip, GameMaterials.WoodHandle, new Vector3(0f, 0f, 0f));
    Add(root, guard, GameMaterials.Copper, new Vector3(0f, 0f, -0.10f));
    Add(root, blade, GameMaterials.Copper, new Vector3(0f, 0f, -0.52f));

    return root;
  }

  static void Add(Node3D parent, Mesh mesh, Material material, Vector3 position)
  {
    parent.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position });
  }
}
