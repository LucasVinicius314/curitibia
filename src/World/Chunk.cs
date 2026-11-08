using System.Runtime.InteropServices;
using Curitibia.Core;
using Godot;

namespace Curitibia.World;

/// <summary>Engine arrays for one chunk mesh, converted on a worker thread so the main thread only uploads them.</summary>
public sealed class ChunkBuild
{
  public Vector3[] Vertices = System.Array.Empty<Vector3>();
  public Vector3[] Normals = System.Array.Empty<Vector3>();
  public Vector2[] Uvs = System.Array.Empty<Vector2>();
  public int[] Indices = System.Array.Empty<int>();
  public Vector3[] CollisionFaces = System.Array.Empty<Vector3>();

  public bool IsEmpty => Indices.Length == 0;

  public static ChunkBuild From(MeshData data)
  {
    var build = new ChunkBuild
    {
      Vertices = MemoryMarshal.Cast<float, Vector3>(data.Positions).ToArray(),
      Normals = MemoryMarshal.Cast<float, Vector3>(data.Normals).ToArray(),
      Uvs = MemoryMarshal.Cast<float, Vector2>(data.Uvs).ToArray(),
      Indices = data.Indices,
    };

    var faces = new Vector3[data.Indices.Length];

    for (int i = 0; i < faces.Length; i++)
    {
      faces[i] = build.Vertices[data.Indices[i]];
    }

    build.CollisionFaces = faces;

    return build;
  }
}

/// <summary>
/// The scene side of one chunk: a mesh and a trimesh collider (a static body on the World layer). Voxel data and
/// streaming live in Terrain; this node only shows what it is given.
/// </summary>
public partial class Chunk : Node3D
{
  readonly MeshInstance3D meshInstance = new();
  readonly StaticBody3D body = new();
  readonly CollisionShape3D shape = new();

  public Vector2I Coord { get; private set; }

  /// <summary>True once collision exists, so a body can stand here.</summary>
  public bool HasCollision { get; private set; }

  public void Setup(Vector2I coord)
  {
    Coord = coord;
    Name = $"Chunk_{coord.X}_{coord.Y}";
    Position = new Vector3(coord.X * VoxelMath.ChunkSize, 0, coord.Y * VoxelMath.ChunkSize);

    meshInstance.MaterialOverride = TextureAtlas.ChunkMaterial;
    body.CollisionLayer = GameLayers.World;
    body.CollisionMask = 0;

    AddChild(meshInstance);
    AddChild(body);
    body.AddChild(shape);
  }

  public void Apply(ChunkBuild build)
  {
    var oldMesh = meshInstance.Mesh;
    var oldShape = shape.Shape;

    if (build.IsEmpty)
    {
      meshInstance.Mesh = null;
      shape.Shape = null;
      HasCollision = true;
      oldMesh?.Dispose();
      oldShape?.Dispose();
      return;
    }

    var arrays = new Godot.Collections.Array();
    arrays.Resize((int)Mesh.ArrayType.Max);
    arrays[(int)Mesh.ArrayType.Vertex] = build.Vertices;
    arrays[(int)Mesh.ArrayType.Normal] = build.Normals;
    arrays[(int)Mesh.ArrayType.TexUV] = build.Uvs;
    arrays[(int)Mesh.ArrayType.Index] = build.Indices;

    var mesh = new ArrayMesh();
    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    meshInstance.Mesh = mesh;

    var collider = new ConcavePolygonShape3D { Data = build.CollisionFaces };
    shape.Shape = collider;
    HasCollision = true;

    // Free the replaced native objects now instead of waiting for the .NET GC.
    oldMesh?.Dispose();
    oldShape?.Dispose();
  }
}
