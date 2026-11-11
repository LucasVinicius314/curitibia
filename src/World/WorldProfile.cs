using System;
using System.Diagnostics;
using System.Threading;

namespace Curitibia.World;

/// <summary>The phases whose cost is measured.</summary>
public enum Phase
{
  /// <summary>Column heights and biomes for a chunk (worker).</summary>
  Columns,

  /// <summary>Filling rock and the surface blocks (worker).</summary>
  Fill,

  /// <summary>Carving the caves (worker).</summary>
  Caves,

  /// <summary>The ordinary trees (worker).</summary>
  Trees,

  /// <summary>The giant trees (worker): finding them and placing them.</summary>
  Giants,

  /// <summary>Dead logs (worker).</summary>
  Logs,

  /// <summary>Ore veins (worker).</summary>
  Ores,

  /// <summary>Grass plants and the final compaction (worker).</summary>
  Plants,

  /// <summary>Whole chunk generation (worker).</summary>
  Generate,

  /// <summary>Growing the structure (limbs, roots, vines) of a giant tree, once per tree (worker).</summary>
  GiantBuild,

  /// <summary>The tints of a chunk's columns (worker).</summary>
  Tints,

  /// <summary>The mesher: faces and collision rectangles (worker).</summary>
  Mesh,

  /// <summary>Converting the mesh into engine arrays (worker).</summary>
  MeshConvert,

  /// <summary>Main thread: taking finished generation results.</summary>
  DrainData,

  /// <summary>Main thread: uploading meshes and building collision shapes.</summary>
  Apply,

  /// <summary>Main thread: planning what to generate and mesh next.</summary>
  Plan,

  /// <summary>Within Apply: creating the visible mesh from the arrays.</summary>
  ApplyMesh,

  /// <summary>Within Apply: building the collision shape.</summary>
  ApplyShape,

  /// <summary>Within Apply: freeing the replaced mesh and shape.</summary>
  ApplyFree,
}

/// <summary>
/// Cheap always-on timers: how many ticks (and how many times) each phase of the world pipeline ran, summed over every thread.
/// The benchmark (--bench) reads them per second; nothing else does. Engine-free so the generator can use it.
/// </summary>
public static class WorldProfile
{
  static readonly long[] ticks = new long[Enum.GetValues<Phase>().Length];
  static readonly long[] counts = new long[Enum.GetValues<Phase>().Length];

  /// <summary>Vertices and collision triangles handed to the engine so far (main thread), for the benchmark.</summary>
  public static long Vertices;

  public static long CollisionTriangles;

  public static long Start() => Stopwatch.GetTimestamp();

  public static void Add(Phase phase, long start)
  {
    Interlocked.Add(ref ticks[(int)phase], Stopwatch.GetTimestamp() - start);
    Interlocked.Increment(ref counts[(int)phase]);
  }

  /// <summary>Milliseconds summed over every call so far.</summary>
  public static double Milliseconds(Phase phase) => Interlocked.Read(ref ticks[(int)phase]) * 1000.0 / Stopwatch.Frequency;

  public static long Count(Phase phase) => Interlocked.Read(ref counts[(int)phase]);
}
