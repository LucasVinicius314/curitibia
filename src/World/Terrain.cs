using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Curitibia.Core;
using Godot;

namespace Curitibia.World;

/// <summary>
/// The voxel world: owns chunk data, streams chunks in and out around the viewers (players), and applies block edits.
/// Generation and meshing run on worker threads (pure functions); the main thread only uploads results, a few per frame.
/// Chunks data is kept for ViewRadius + 1 so that the meshed ring always has all four neighbours.
/// </summary>
public partial class Terrain : Node3D
{
  [Export] public WorldGenSettings Settings { get; set; } = null!;

  public static Terrain? Instance { get; private set; }

  /// <summary>Raised on the main thread after a block actually changed (local edit or network edit).</summary>
  public event Action<Vector3I, byte>? BlockChanged;

  /// <summary>
  /// When set, edit requests are routed here instead of being applied directly (the network session installs this on a
  /// client so the host stays the single source of truth). Null means apply locally.
  /// </summary>
  public Action<Vector3I, byte>? EditRouter { get; set; }

  sealed class Entry
  {
    public Vector2I Coord;
    public byte[]? Blocks;
    public bool Generating;
    public bool Meshing;
    public bool Removed;
    public int Version;
    public int MeshedVersion = -1;
    public Chunk? Node;
  }

  sealed class MeshResult
  {
    public Entry Entry = null!;
    public int Version;
    public ChunkBuild Build = null!;
  }

  sealed class DataResult
  {
    public Entry Entry = null!;
    public byte[] Blocks = null!;
  }

  readonly Dictionary<Vector2I, Entry> entries = new();
  readonly Dictionary<Vector2I, Dictionary<int, byte>> edits = new();
  readonly List<Node3D> viewers = new();
  readonly ConcurrentQueue<MeshResult> meshResults = new();
  readonly ConcurrentQueue<DataResult> dataResults = new();

  WorldGenerator? generator;
  int inFlight;
  bool dirty = true;
  Vector2I[] lastViewerChunks = Array.Empty<Vector2I>();

  readonly int maxInFlight = Math.Max(2, System.Environment.ProcessorCount - 2);

  public int Seed { get; private set; }
  public bool IsRunning => generator != null;
  public int LoadedChunkCount => entries.Count;

  public override void _EnterTree()
  {
    Instance = this;
  }

  public override void _ExitTree()
  {
    if (Instance == this)
    {
      Instance = null;
    }
  }

  /// <summary>Starts (or restarts) the world with a seed. Existing chunks and edits are dropped.</summary>
  public void Begin(int seed)
  {
    Clear();

    Seed = seed;
    generator = new WorldGenerator(Settings.ToParams(seed));
    dirty = true;
  }

  public void Clear()
  {
    foreach (var entry in entries.Values)
    {
      entry.Removed = true;
      entry.Node?.QueueFree();
    }

    entries.Clear();
    edits.Clear();
    generator = null;
    lastViewerChunks = Array.Empty<Vector2I>();
  }

  public void AddViewer(Node3D viewer)
  {
    if (!viewers.Contains(viewer))
    {
      viewers.Add(viewer);
      dirty = true;
    }
  }

  public void RemoveViewer(Node3D viewer)
  {
    viewers.Remove(viewer);
    dirty = true;
  }

  // ---------------------------------------------------------------- queries

  /// <summary>The block at a world voxel; air when out of range or when that chunk is not loaded.</summary>
  public byte GetBlock(int wx, int wy, int wz)
  {
    if (!VoxelMath.InWorldHeight(wy))
    {
      return BlockIds.Air;
    }

    VoxelMath.WorldToChunk(wx, wz, out var cx, out var cz, out var lx, out var lz);

    if (!entries.TryGetValue(new Vector2I(cx, cz), out var entry) || entry.Blocks == null)
    {
      return BlockIds.Air;
    }

    return entry.Blocks[VoxelMath.Index(lx, wy, lz)];
  }

  public bool IsSolid(int wx, int wy, int wz)
  {
    return GetBlock(wx, wy, wz) != BlockIds.Air;
  }

  /// <summary>True when the chunk under a world position has collision, so a body can stand there.</summary>
  public bool HasCollisionAt(Vector3 position)
  {
    VoxelMath.WorldToChunk(VoxelMath.FloorToInt(position.X), VoxelMath.FloorToInt(position.Z), out var cx, out var cz, out _, out _);

    return entries.TryGetValue(new Vector2I(cx, cz), out var entry) && entry.Node != null && entry.Node.HasCollision;
  }

  /// <summary>Terrain surface height (top of the highest solid voxel) from the generator, ignoring edits.</summary>
  public float SurfaceHeightAt(int wx, int wz)
  {
    return generator?.ColumnHeight(wx, wz) ?? VoxelMath.ChunkHeight / 2f;
  }

  /// <summary>True when everything wanted around the viewers is meshed and nothing is pending.</summary>
  public bool IsSettled
  {
    get
    {
      if (generator == null || viewers.Count == 0)
      {
        return false;
      }

      if (inFlight > 0 || !meshResults.IsEmpty || !dataResults.IsEmpty)
      {
        return false;
      }

      foreach (var viewer in viewers)
      {
        var c = ChunkOf(viewer.GlobalPosition);

        for (int dz = -Settings.ViewRadius; dz <= Settings.ViewRadius; dz++)
        {
          for (int dx = -Settings.ViewRadius; dx <= Settings.ViewRadius; dx++)
          {
            if (!entries.TryGetValue(c + new Vector2I(dx, dz), out var e) || e.Node == null || e.MeshedVersion != e.Version)
            {
              return false;
            }
          }
        }
      }

      return true;
    }
  }

  // ---------------------------------------------------------------- edits

  /// <summary>Entry point for gameplay: routes to the network when connected as a client, else applies directly.</summary>
  public void RequestSetBlock(Vector3I voxel, byte id)
  {
    if (EditRouter != null)
    {
      EditRouter(voxel, id);
    }
    else
    {
      ApplyEdit(voxel, id);
    }
  }

  /// <summary>Applies an edit for real (authoritative path) and remembers it for late joiners and reloaded chunks.</summary>
  public bool ApplyEdit(Vector3I voxel, byte id)
  {
    if (!VoxelMath.InWorldHeight(voxel.Y))
    {
      return false;
    }

    VoxelMath.WorldToChunk(voxel.X, voxel.Z, out var cx, out var cz, out var lx, out var lz);
    var coord = new Vector2I(cx, cz);
    var index = VoxelMath.Index(lx, voxel.Y, lz);

    if (entries.TryGetValue(coord, out var entry) && entry.Blocks != null)
    {
      if (entry.Blocks[index] == id)
      {
        return false; // nothing changes: do not record or announce it
      }

      entry.Blocks[index] = id;
      entry.Version++;
      BumpBorderNeighbours(coord, lx, lz);
      dirty = true;
    }

    // Remember the edit for late joiners and for chunks that are not loaded (or get reloaded) later.
    if (!edits.TryGetValue(coord, out var chunkEdits))
    {
      chunkEdits = new Dictionary<int, byte>();
      edits[coord] = chunkEdits;
    }

    chunkEdits[index] = id;

    BlockChanged?.Invoke(voxel, id);

    return true;
  }

  /// <summary>All edits so far as (voxel, id), for syncing a late joiner.</summary>
  public List<(Vector3I voxel, byte id)> SnapshotEdits()
  {
    var list = new List<(Vector3I, byte)>();

    foreach (var (coord, chunkEdits) in edits)
    {
      foreach (var (index, id) in chunkEdits)
      {
        var lx = index % VoxelMath.ChunkSize;
        var lz = index / VoxelMath.ChunkSize % VoxelMath.ChunkSize;
        var y = index / (VoxelMath.ChunkSize * VoxelMath.ChunkSize);

        list.Add((new Vector3I(coord.X * VoxelMath.ChunkSize + lx, y, coord.Y * VoxelMath.ChunkSize + lz), id));
      }
    }

    return list;
  }

  void BumpBorderNeighbours(Vector2I coord, int lx, int lz)
  {
    // A border voxel changes the faces drawn by the chunk next to it, so that chunk must be re-meshed too.
    void Bump(Vector2I c)
    {
      if (entries.TryGetValue(c, out var e))
      {
        e.Version++;
      }
    }

    if (lx == 0) Bump(coord + Vector2I.Left);
    if (lx == VoxelMath.ChunkSize - 1) Bump(coord + Vector2I.Right);
    if (lz == 0) Bump(coord + Vector2I.Up);
    if (lz == VoxelMath.ChunkSize - 1) Bump(coord + Vector2I.Down);
  }

  // ---------------------------------------------------------------- streaming

  static Vector2I ChunkOf(Vector3 position)
  {
    VoxelMath.WorldToChunk(VoxelMath.FloorToInt(position.X), VoxelMath.FloorToInt(position.Z), out var cx, out var cz, out _, out _);

    return new Vector2I(cx, cz);
  }

  public override void _Process(double delta)
  {
    if (generator == null)
    {
      return;
    }

    DrainDataResults();
    DrainMeshResults();

    var chunks = new Vector2I[viewers.Count];

    for (int i = 0; i < chunks.Length; i++)
    {
      chunks[i] = ChunkOf(viewers[i].GlobalPosition);
    }

    if (!SameChunks(chunks, lastViewerChunks))
    {
      lastViewerChunks = chunks;
      dirty = true;
    }

    if (!dirty)
    {
      return;
    }

    dirty = false;
    Plan(chunks);
  }

  static bool SameChunks(Vector2I[] a, Vector2I[] b)
  {
    if (a.Length != b.Length)
    {
      return false;
    }

    for (int i = 0; i < a.Length; i++)
    {
      if (a[i] != b[i])
      {
        return false;
      }
    }

    return true;
  }

  void Plan(Vector2I[] viewerChunks)
  {
    var radius = Settings.ViewRadius;

    // Unload what is far from every viewer (with a margin so walking along a border does not thrash).
    var unloadRadius = radius + 1 + Settings.UnloadMargin;
    List<Vector2I>? toRemove = null;

    foreach (var (coord, entry) in entries)
    {
      if (Distance(coord, viewerChunks) > unloadRadius)
      {
        (toRemove ??= new List<Vector2I>()).Add(coord);
      }
    }

    if (toRemove != null)
    {
      foreach (var coord in toRemove)
      {
        var entry = entries[coord];
        entry.Removed = true;
        entry.Node?.QueueFree();
        entries.Remove(coord);
      }
    }

    // Wanted chunks, nearest first.
    var wanted = new List<(int dist, Vector2I coord)>();
    var seen = new HashSet<Vector2I>();

    foreach (var viewer in viewerChunks)
    {
      for (int dz = -(radius + 1); dz <= radius + 1; dz++)
      {
        for (int dx = -(radius + 1); dx <= radius + 1; dx++)
        {
          var coord = viewer + new Vector2I(dx, dz);

          if (seen.Add(coord))
          {
            wanted.Add((Distance(coord, viewerChunks), coord));
          }
        }
      }
    }

    wanted.Sort((a, b) => a.dist.CompareTo(b.dist));

    // Pass 1: data for the whole wanted square (radius + 1).
    foreach (var (_, coord) in wanted)
    {
      if (inFlight >= maxInFlight)
      {
        dirty = true;
        return;
      }

      if (!entries.TryGetValue(coord, out var entry))
      {
        entry = new Entry { Coord = coord };
        entries[coord] = entry;
      }

      if (entry.Blocks == null && !entry.Generating)
      {
        StartGenerate(entry);
      }
    }

    // Pass 2: meshes for the inner square once the four neighbours have data.
    foreach (var (dist, coord) in wanted)
    {
      if (dist > radius)
      {
        continue;
      }

      if (inFlight >= maxInFlight)
      {
        dirty = true;
        return;
      }

      var entry = entries[coord];

      if (entry.Blocks == null || entry.Meshing || entry.MeshedVersion == entry.Version)
      {
        continue;
      }

      if (!NeighboursHaveData(coord))
      {
        continue;
      }

      StartMesh(entry);
    }
  }

  static int Distance(Vector2I coord, Vector2I[] viewerChunks)
  {
    var best = int.MaxValue;

    foreach (var v in viewerChunks)
    {
      best = Math.Min(best, Math.Max(Math.Abs(coord.X - v.X), Math.Abs(coord.Y - v.Y)));
    }

    return best;
  }

  bool NeighboursHaveData(Vector2I coord)
  {
    return HasData(coord + Vector2I.Right) && HasData(coord + Vector2I.Left)
      && HasData(coord + Vector2I.Down) && HasData(coord + Vector2I.Up);
  }

  bool HasData(Vector2I coord)
  {
    return entries.TryGetValue(coord, out var e) && e.Blocks != null;
  }

  void StartGenerate(Entry entry)
  {
    var gen = generator!;

    entry.Generating = true;
    inFlight++;

    Task.Run(() =>
    {
      var blocks = gen.GenerateChunk(entry.Coord.X, entry.Coord.Y);
      dataResults.Enqueue(new DataResult { Entry = entry, Blocks = blocks });
    });
  }

  void StartMesh(Entry entry)
  {
    var version = entry.Version;
    var blocks = entry.Blocks!;
    var neighbours = new byte[]?[]
    {
      entries[entry.Coord + Vector2I.Right].Blocks,
      entries[entry.Coord + Vector2I.Left].Blocks,
      entries[entry.Coord + Vector2I.Down].Blocks,
      entries[entry.Coord + Vector2I.Up].Blocks,
    };

    entry.Meshing = true;
    inFlight++;

    Task.Run(() =>
    {
      var data = ChunkMesher.Build(blocks, neighbours, BlockIds.TileByBlock, BlockIds.TileCount, TextureAtlas.UvInset);
      meshResults.Enqueue(new MeshResult { Entry = entry, Version = version, Build = ChunkBuild.From(data) });
    });
  }

  void DrainDataResults()
  {
    while (dataResults.TryDequeue(out var result))
    {
      inFlight--;
      result.Entry.Generating = false;

      if (result.Entry.Removed)
      {
        continue;
      }

      var blocks = result.Blocks;

      // Replay edits made while this chunk was not loaded.
      if (edits.TryGetValue(result.Entry.Coord, out var chunkEdits))
      {
        foreach (var (index, id) in chunkEdits)
        {
          blocks[index] = id;
        }
      }

      result.Entry.Blocks = blocks;
      result.Entry.Version++;
      dirty = true;
    }
  }

  void DrainMeshResults()
  {
    var applied = 0;

    while (applied < Settings.MeshAppliesPerFrame && meshResults.TryDequeue(out var result))
    {
      inFlight--;
      applied++;

      var entry = result.Entry;
      entry.Meshing = false;

      if (entry.Removed)
      {
        continue;
      }

      if (entry.Node == null)
      {
        entry.Node = new Chunk();
        entry.Node.Setup(entry.Coord);
        AddChild(entry.Node);
      }

      entry.Node.Apply(result.Build);
      entry.MeshedVersion = result.Version;
      dirty = true;
    }
  }
}
