using System.Runtime.InteropServices;
using Curitibia.Core;
using Godot;

namespace Curitibia.World;

/// <summary>
/// Engine arrays for one chunk mesh, converted on a worker thread into the engine's own packed arrays (the copy out of managed
/// memory is the expensive part) and cut into slices of a few thousand quads, so the main thread can upload a chunk a slice at a
/// time over several frames instead of in one long stall.
/// </summary>
public sealed class ChunkBuild
{
  /// <summary>Quads in one slice (four vertices each): about 2.5 ms of upload on the main thread.</summary>
  const int QuadsPerSlice = 10000;

  public sealed class Slice
  {
    public Variant Vertices;
    public Variant Normals;
    public Variant Uvs;
    public Variant Colors;
    public Variant Indices;
    public bool HasColors;
    public int VertexCount;
  }

  public readonly System.Collections.Generic.List<Slice> Slices = new();
  public Vector3[] CollisionFaces = System.Array.Empty<Vector3>();

  /// <summary>Number of vertices in all the slices.</summary>
  public int VertexCount;

  /// <summary>True when this build was made with collision (chunks far from every player are only drawn).</summary>
  public bool WithCollision;

  public bool IsEmpty => VertexCount == 0;

  public static ChunkBuild From(MeshData data, bool withCollision = true)
  {
    var build = new ChunkBuild { VertexCount = data.VertexCount };
    var quads = data.VertexCount / 4;
    var hasColors = data.Colors.Length > 0; // always: the tint in rgb and the tile index in alpha

    // Every face and every grass plant is a quad of four consecutive vertices and six indices, so a slice is a range of quads.
    for (int first = 0; first < quads; first += QuadsPerSlice)
    {
      var count = System.Math.Min(QuadsPerSlice, quads - first);
      var vertices = new Vector3[count * 4];
      var normals = new Vector3[count * 4];
      var uvs = new Vector2[count * 4];
      var indices = new int[count * 6];
      var colors = hasColors ? new Color[count * 4] : null;

      for (int v = 0; v < count * 4; v++)
      {
        var at = (first * 4 + v) * 3;

        // The mesher works in voxel units; the world is built from half-metre blocks.
        vertices[v] = new Vector3(data.Positions[at], data.Positions[at + 1], data.Positions[at + 2]) * VoxelMath.BlockSize;
        normals[v] = new Vector3(data.Normals[at], data.Normals[at + 1], data.Normals[at + 2]);
        uvs[v] = new Vector2(data.Uvs[(first * 4 + v) * 2], data.Uvs[(first * 4 + v) * 2 + 1]);

        if (colors != null)
        {
          var c = (first * 4 + v) * 4;

          colors[v] = new Color(data.Colors[c] / 255f, data.Colors[c + 1] / 255f, data.Colors[c + 2] / 255f, data.Colors[c + 3] / 255f);
        }
      }

      for (int k = 0; k < count * 6; k++)
      {
        indices[k] = data.Indices[first * 6 + k] - first * 4;
      }

      // A Variant made here holds the engine's own packed array: the copy out of managed memory happens on this worker, and the
      // main thread later only passes the handle on.
      build.Slices.Add(new Slice
      {
        Vertices = Variant.From(vertices),
        Normals = Variant.From(normals),
        Uvs = Variant.From(uvs),
        Indices = Variant.From(indices),
        Colors = colors != null ? Variant.From(colors) : default,
        HasColors = colors != null,
        VertexCount = count * 4,
      });
    }

    build.WithCollision = withCollision;

    if (!withCollision)
    {
      return build;
    }

    var triangles = data.CollisionTriangles;
    var faces = new Vector3[triangles.Length / 3];

    for (int i = 0; i < faces.Length; i++)
    {
      faces[i] = new Vector3(triangles[i * 3], triangles[i * 3 + 1], triangles[i * 3 + 2]) * VoxelMath.BlockSize;
    }

    build.CollisionFaces = faces;

    return build;
  }
}

/// <summary>
/// Putting a ChunkBuild into a Chunk a step at a time (one slice of the mesh, then the collision shape, then the swap), so no single
/// frame pays for a whole chunk. The old mesh and shape stay in place until the last step, so a chunk being replaced never shows a
/// half-built mesh.
/// </summary>
public sealed class ChunkApply
{
  readonly Chunk chunk;
  readonly ChunkBuild build;
  ArrayMesh? mesh;
  ConcavePolygonShape3D? shape;
  int next;
  bool shapeBuilt;

  public ChunkApply(Chunk chunk, ChunkBuild build)
  {
    this.chunk = chunk;
    this.build = build;
  }

  /// <summary>Does the next piece of the work; true when the chunk has been completely replaced.</summary>
  public bool Step()
  {
    if (build.IsEmpty)
    {
      chunk.Install(null, null, build.WithCollision);

      return true;
    }

    if (next < build.Slices.Count)
    {
      var started = WorldProfile.Start();
      var slice = build.Slices[next++];
      var arrays = new Godot.Collections.Array();

      arrays.Resize((int)Mesh.ArrayType.Max);
      arrays[(int)Mesh.ArrayType.Vertex] = slice.Vertices;
      arrays[(int)Mesh.ArrayType.Normal] = slice.Normals;
      arrays[(int)Mesh.ArrayType.TexUV] = slice.Uvs;

      if (slice.HasColors)
      {
        arrays[(int)Mesh.ArrayType.Color] = slice.Colors;
      }

      arrays[(int)Mesh.ArrayType.Index] = slice.Indices;

      mesh ??= new ArrayMesh();
      mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
      WorldProfile.Add(Phase.ApplyMesh, started);
      System.Threading.Interlocked.Add(ref WorldProfile.Vertices, slice.VertexCount);

      return false;
    }

    if (build.WithCollision && !shapeBuilt)
    {
      var started = WorldProfile.Start();

      shape = new ConcavePolygonShape3D { Data = build.CollisionFaces };
      shapeBuilt = true;
      WorldProfile.Add(Phase.ApplyShape, started);
      System.Threading.Interlocked.Add(ref WorldProfile.CollisionTriangles, build.CollisionFaces.Length / 3);

      return false;
    }

    chunk.Install(mesh, shape, build.WithCollision);

    return true;
  }

  /// <summary>The chunk went away before the job finished: free what was built.</summary>
  public void Abandon()
  {
    mesh?.Dispose();
    shape?.Dispose();
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
    Position = new Vector3(coord.X * VoxelMath.ChunkSize, 0, coord.Y * VoxelMath.ChunkSize) * VoxelMath.BlockSize;

    meshInstance.MaterialOverride = TextureAtlas.ChunkMaterial;
    body.CollisionLayer = GameLayers.World;
    body.CollisionMask = 0;

    AddChild(meshInstance);
    AddChild(body);
    body.AddChild(shape);
  }

  /// <summary>Swaps in a finished mesh and shape (either may be null: nothing to show or nothing to collide with) and frees the old ones.</summary>
  public void Install(ArrayMesh? mesh, ConcavePolygonShape3D? collider, bool withCollision)
  {
    var started = WorldProfile.Start();
    var oldMesh = meshInstance.Mesh;
    var oldShape = shape.Shape;

    meshInstance.Mesh = mesh;
    shape.Shape = collider;
    HasCollision = withCollision;

    // Free the replaced native objects now instead of waiting for the .NET GC.
    oldMesh?.Dispose();
    oldShape?.Dispose();
    WorldProfile.Add(Phase.ApplyFree, started);
  }
}
