using Curitibia.Core;
using Godot;

namespace Curitibia.Items;

/// <summary>A pickaxe of boxes: a wooden shaft and a copper head across its end, in the plane of the chop, its ends bent back toward the grip (nothing points sideways). The grip is at the origin, the head ahead (-Z).</summary>
[GlobalClass]
public partial class PickaxeItemModel : ItemModel
{
  static readonly BoxMesh shaft = new() { Size = new Vector3(0.04f, 0.04f, 0.56f) };
  static readonly BoxMesh headBar = new() { Size = new Vector3(0.05f, 0.34f, 0.07f) };
  static readonly BoxMesh headTip = new() { Size = new Vector3(0.05f, 0.06f, 0.1f) };

  static readonly System.Collections.Generic.Dictionary<Color, StandardMaterial3D> metals = new();

  /// <summary>The metal of the head (copper, iron, gold, platinum...).</summary>
  [Export] public Color HeadColor { get; set; } = new Color(0.78f, 0.46f, 0.24f);

  public override Node3D Build()
  {
    var root = new Node3D();

    Add(root, shaft, GameMaterials.WoodHandle, new Vector3(0f, 0f, -0.2f));
    var metal = MetalOf(HeadColor);

    Add(root, headBar, metal, new Vector3(0f, 0f, -0.5f));
    Add(root, headTip, metal, new Vector3(0f, -0.19f, -0.45f));
    Add(root, headTip, metal, new Vector3(0f, 0.19f, -0.45f));

    return root;
  }

  static StandardMaterial3D MetalOf(Color color)
  {
    if (!metals.TryGetValue(color, out var material))
    {
      material = new StandardMaterial3D { AlbedoColor = color, Metallic = 0.6f, Roughness = 0.45f };
      metals[color] = material;
    }

    return material;
  }

  static void Add(Node3D parent, Mesh mesh, Material material, Vector3 position)
  {
    parent.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position });
  }
}
