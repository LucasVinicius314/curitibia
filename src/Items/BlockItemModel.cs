using System.Collections.Generic;
using System.Runtime.InteropServices;
using Curitibia.Core;
using Curitibia.World;
using Godot;

namespace Curitibia.Items;

/// <summary>A block item shown as one textured cube, meshed by the same mesher as the terrain so it uses the same tiles.</summary>
[GlobalClass]
public partial class BlockItemModel : ItemModel
{
  /// <summary>Edge of the cube in the hand, metres.</summary>
  const float HeldSize = 0.22f;

  static readonly Dictionary<int, ArrayMesh> meshes = new();

  [Export] public int BlockId { get; set; }

  public override Node3D Build()
  {
    return new MeshInstance3D
    {
      Mesh = MeshFor(BlockId),
      MaterialOverride = TextureAtlas.ChunkMaterial,
      Scale = Vector3.One * HeldSize,
    };
  }

  static ArrayMesh MeshFor(int blockId)
  {
    if (meshes.TryGetValue(blockId, out var cached))
    {
      return cached;
    }

    // One block one layer up (the mesher never draws the bottom of the world), with empty neighbours so every face shows.
    var blocks = new ChunkData();
    blocks.Set(0, 1, 0, (byte)blockId);
    var empty = new ChunkData();
    var data = ChunkMesher.Build(blocks, new ChunkData?[] { empty, empty, empty, empty }, BlockIds.TileByBlock, BlockIds.TileCount, TextureAtlas.UvInset);

    var vertices = MemoryMarshal.Cast<float, Vector3>(data.Positions).ToArray();

    for (int i = 0; i < vertices.Length; i++)
    {
      vertices[i] -= new Vector3(0.5f, 1.5f, 0.5f); // centre the cube on the origin
    }

    var arrays = new Godot.Collections.Array();
    arrays.Resize((int)Mesh.ArrayType.Max);
    arrays[(int)Mesh.ArrayType.Vertex] = vertices;
    arrays[(int)Mesh.ArrayType.Normal] = MemoryMarshal.Cast<float, Vector3>(data.Normals).ToArray();
    arrays[(int)Mesh.ArrayType.TexUV] = MemoryMarshal.Cast<float, Vector2>(data.Uvs).ToArray();

    // The vertex colour carries the tile index (alpha) and the tint (rgb) that the chunk shader reads.
    var colors = new Color[data.Colors.Length / 4];

    for (int i = 0; i < colors.Length; i++)
    {
      colors[i] = new Color(data.Colors[i * 4] / 255f, data.Colors[i * 4 + 1] / 255f, data.Colors[i * 4 + 2] / 255f, data.Colors[i * 4 + 3] / 255f);
    }

    arrays[(int)Mesh.ArrayType.Color] = colors;
    arrays[(int)Mesh.ArrayType.Index] = data.Indices;

    var mesh = new ArrayMesh();
    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    meshes[blockId] = mesh;

    return mesh;
  }
}
