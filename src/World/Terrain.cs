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
/// Chunks data is kept for the render distance (GameSettings.RenderDistance, a player setting) + 1 so that the meshed ring always has all four neighbours.
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

  /// <summary>
  /// Raised on the main thread when a chunk's voxels are new or changed (it finished generating, or a block in it was
  /// edited). The map uses it to redraw that chunk's tile.
  /// </summary>
  public event Action<Vector2I>? ChunkChanged;

  /// <summary>Counts the worlds started (Begin / Clear), so a view of the world can tell when to throw its cache away.</summary>
  public int WorldVersion { get; private set; }

  sealed class Entry
  {
    public Vector2I Coord;
    public ChunkData? Blocks;
    public bool Generating;
    public bool Meshing;
    public bool Removed;
    public int Version;
    public int MeshedVersion = -1;

    /// <summary>The layers the current mesh covers (a deep world is only drawn where somebody can see).</summary>
    public int MeshedFloor = -1;
    public int MeshedCeiling = -1;
    public bool MeshedCollision;
    public Chunk? Node;
  }

  sealed class MeshResult
  {
    public Entry Entry = null!;
    public int Version;
    public int Floor;
    public int Ceiling;
    public bool Collision;
    public ChunkBuild Build = null!;
  }

  sealed class DataResult
  {
    public Entry Entry = null!;
    public ChunkData Blocks = null!;
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
  int[] lastViewerSections = Array.Empty<int>();
  bool[] lastViewerUnder = Array.Empty<bool>();

  /// <summary>Chunks around a viewer on the surface whose mesh is extended down to include the viewer's own layer.</summary>
  const int NearRadius = 3;

  /// <summary>Sections (16 layers each) the underground slab moves in: it only changes when the viewer leaves the band.</summary>
  const int SlabBand = 4;

  /// <summary>
  /// Chunks within this many chunks of a viewer have collision; the rest are only drawn. A collision shape is built on the main
  /// thread and costs milliseconds (an underground chunk has tens of thousands of triangles), and nothing that collides with the
  /// world gets that far from a player.
  /// </summary>
  const int CollisionRadius = 4;

  /// <summary>Main-thread time per frame spent uploading finished meshes (at least one is always applied).</summary>
  const double ApplyBudgetMs = 3.0;

  /// <summary>Layers below and above the viewer's band that every chunk is drawn underground (the render distance applies as on the surface).</summary>
  const int DeepBelow = 48;
  const int DeepAbove = 64;

  /// <summary>How far below its surface a chunk is drawn when nobody is near it (so cliffs and cave mouths show).</summary>
  const int SurfaceReach = 24;

  readonly int maxInFlight = Math.Max(2, System.Environment.ProcessorCount - 2);

  public int Seed { get; private set; }
  public bool IsRunning => generator != null;
  public int LoadedChunkCount => entries.Count;

  public override void _EnterTree()
  {
    Instance = this;
    GameSettings.Changed += OnSettingsChanged;
  }

  void OnSettingsChanged()
  {
    dirty = true; // a new render distance: plan again (load or unload the difference)
  }

  public override void _ExitTree()
  {
    GameSettings.Changed -= OnSettingsChanged;

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
    WorldVersion++;

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

    return entry.Blocks.Get(lx, wy, lz);
  }

  public bool IsSolid(int wx, int wy, int wz)
  {
    return GetBlock(wx, wy, wz) != BlockIds.Air;
  }

  /// <summary>The voxels of a loaded chunk (do not modify them), or false when that chunk is not loaded.</summary>
  public bool TryGetChunk(Vector2I coord, out ChunkData? blocks)
  {
    if (entries.TryGetValue(coord, out var entry) && entry.Blocks != null)
    {
      blocks = entry.Blocks;

      return true;
    }

    blocks = null;

    return false;
  }

  /// <summary>
  /// True when the chunk under a world position has collision that covers where a body is standing: its mesh is current and
  /// its window of layers includes the body's feet (a stale slab or a mesh drawn for somewhere else does not count, so a body
  /// waits for the right ground instead of falling through the wrong one).
  /// </summary>
  public bool HasCollisionAt(Vector3 position)
  {
    VoxelMath.WorldToChunk(VoxelMath.WorldToVoxel(position.X), VoxelMath.WorldToVoxel(position.Z), out var cx, out var cz, out _, out _);

    if (!entries.TryGetValue(new Vector2I(cx, cz), out var entry) || entry.Node == null || !entry.Node.HasCollision)
    {
      return false;
    }

    var layer = VoxelMath.WorldToVoxel(position.Y);

    return entry.MeshedCollision && entry.MeshedVersion == entry.Version && layer >= entry.MeshedFloor && layer < entry.MeshedCeiling;
  }

  /// <summary>Surface height in world units (top of the highest terrain or tree voxel of voxel column wx, wz) from the generator, ignoring edits.</summary>
  public float SurfaceHeightAt(int wx, int wz)
  {
    return (generator?.SurfaceTop(wx, wz) ?? VoxelMath.SeaLevel) * VoxelMath.BlockSize;
  }

  /// <summary>The biome under a world position (from the generator, so every peer agrees); null before the world starts.</summary>
  public BiomeDef? BiomeAt(Vector3 position)
  {
    return generator == null
      ? null
      : Biomes.Get(generator.BiomeAt(VoxelMath.WorldToVoxel(position.X), VoxelMath.WorldToVoxel(position.Z)));
  }

  /// <summary>How far, in metres, a world position is below the ground (the terrain itself, not the trees) at its own column; negative above it.</summary>
  public float DepthBelowGround(Vector3 position)
  {
    if (generator == null)
    {
      return 0f;
    }

    return generator.ColumnHeight(VoxelMath.WorldToVoxel(position.X), VoxelMath.WorldToVoxel(position.Z)) * VoxelMath.BlockSize - position.Y;
  }

  /// <summary>
  /// The name of the underground layer at a world position ("The Caverns", "The Underworld"), or null when it is on or near
  /// the ground. The ground is the terrain itself, not the trees on it, and a few metres below it counts as underground.
  /// </summary>
  public string? UndergroundAt(Vector3 position)
  {
    if (generator == null)
    {
      return null;
    }

    var vx = VoxelMath.WorldToVoxel(position.X);
    var vz = VoxelMath.WorldToVoxel(position.Z);
    var ground = generator.ColumnHeight(vx, vz) * VoxelMath.BlockSize;

    return Underground.NameAt(VoxelMath.WorldToVoxel(position.Y), position.Y < ground - 4f);
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

        for (int dz = -GameSettings.RenderDistance; dz <= GameSettings.RenderDistance; dz++)
        {
          for (int dx = -GameSettings.RenderDistance; dx <= GameSettings.RenderDistance; dx++)
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
      if (entry.Blocks.Get(lx, voxel.Y, lz) == id)
      {
        return false; // nothing changes: do not record or announce it
      }

      entry.Blocks.Set(lx, voxel.Y, lz, id);
      entry.Version++;
      BumpBorderNeighbours(coord, lx, lz);
      dirty = true;
      ChunkChanged?.Invoke(coord);
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
    VoxelMath.WorldToChunk(VoxelMath.WorldToVoxel(position.X), VoxelMath.WorldToVoxel(position.Z), out var cx, out var cz, out _, out _);

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
    var sections = new int[viewers.Count];
    var under = new bool[viewers.Count];

    for (int i = 0; i < chunks.Length; i++)
    {
      chunks[i] = ChunkOf(viewers[i].GlobalPosition);
      var position = viewers[i].GlobalPosition;
      var layer = VoxelMath.WorldToVoxel(position.Y);

      // Only a viewer well below the ground under itself wants the slab around it; anyone on or above the ground sees the
      // normal surface (compared with the ground at the viewer's own column, not with whatever chunk is being meshed).
      var ground = generator.ColumnHeight(VoxelMath.WorldToVoxel(position.X), VoxelMath.WorldToVoxel(position.Z));

      sections[i] = VoxelMath.FloorDiv(layer, ChunkData.SectionHeight);
      under[i] = layer + ChunkData.SectionHeight < ground - SurfaceReach;
    }

    // Going up or down a section changes how deep the chunks around a viewer need to be drawn.
    if (!SameChunks(chunks, lastViewerChunks) || !sections.AsSpan().SequenceEqual(lastViewerSections) || !under.AsSpan().SequenceEqual(lastViewerUnder))
    {
      lastViewerChunks = chunks;
      lastViewerSections = sections;
      lastViewerUnder = under;
      dirty = true;
    }

    if (!dirty)
    {
      return;
    }

    dirty = false;
    Plan(chunks, sections, under);
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

  /// <summary>
  /// The layers a chunk's mesh covers. A chunk nobody is near is drawn from a little under its surface up (what can be seen
  /// from a distance). A chunk near a viewer is drawn as a slab around the viewer instead, so the ground you can see into (a
  /// cave you are in, a hole you dug) is there and the rest of the underground, which nobody can see, is not.
  /// </summary>
  void WindowFor(Entry entry, Vector2I[] viewerChunks, int[] viewerSections, bool[] viewerUnder, out int floor, out int ceiling)
  {
    var surfaceFloor = entry.Blocks!.SurfaceMin - SurfaceReach;
    var wantsSurface = viewerChunks.Length == 0;
    var low = int.MaxValue;
    var high = 0;

    for (int i = 0; i < viewerChunks.Length; i++)
    {
      var offset = entry.Coord - viewerChunks[i];

      if (!viewerUnder[i])
      {
        // On or just under the ground: the normal surface mesh. Chunks right around the viewer reach down far enough to include
        // the layer the viewer is on (a cave a few metres below the surface), so a body never stands outside its own chunk's mesh.
        wantsSurface = true;

        if (Math.Max(Math.Abs(offset.X), Math.Abs(offset.Y)) <= NearRadius)
        {
          low = Math.Min(low, viewerSections[i] * ChunkData.SectionHeight - ChunkData.SectionHeight);
        }
      }
      else
      {
        // Underground: every chunk in render distance is drawn as the same slab around the viewer's height, so the distance you
        // see is the one the player chose, as on the surface. The slab moves in bands of SlabBand sections, not with every step up
        // or down, so the chunks are not all remeshed each time the viewer changes height a little.
        var band = VoxelMath.FloorDiv(viewerSections[i], SlabBand) * SlabBand;

        low = Math.Min(low, band * ChunkData.SectionHeight - DeepBelow);
        high = Math.Max(high, (band + SlabBand) * ChunkData.SectionHeight + DeepAbove);
      }
    }

    if (low == int.MaxValue)
    {
      wantsSurface = true;
    }

    var top = VoxelMath.ChunkHeight;

    if (wantsSurface)
    {
      low = Math.Min(low, surfaceFloor);
      high = top;
    }
    else if (high >= entry.Blocks!.SurfaceMin)
    {
      high = top; // the slab touches the ground: draw all of what is above it too
    }

    // The underworld is one open hall with magma lakes on its floor: a window that ended inside it would put a cut (and its dark
    // cap) across the lakes or over your head, so near it the window always spans the whole hall, from the bottom of the world.
    if (low < Underground.HellTop)
    {
      low = 0;

      // ...and up past the hall's roof (through the rock blend band), so you never look up at the lid of the window.
      high = Math.Max(high, Underground.HellTop + Underground.BlendBand + ChunkData.SectionHeight);
    }

    floor = Math.Max(0, VoxelMath.FloorDiv(low, ChunkData.SectionHeight) * ChunkData.SectionHeight);
    ceiling = Math.Min(top, (high + ChunkData.SectionHeight - 1) / ChunkData.SectionHeight * ChunkData.SectionHeight);
  }

  void Plan(Vector2I[] viewerChunks, int[] viewerSections, bool[] viewerUnder)
  {
    var radius = GameSettings.RenderDistance;

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

    // First: meshes for the inner square (nearest first) whose four neighbours have data. A mesh that is stale or missing near the
    // player has to be redone before more distant chunks are even generated, or the ground under you can stay out of date.
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

      if (!entries.TryGetValue(coord, out var entry) || entry.Blocks == null || entry.Meshing)
      {
        continue;
      }

      WindowFor(entry, viewerChunks, viewerSections, viewerUnder, out var floor, out var ceiling);

      var collide = WantsCollision(entry.Coord, viewerChunks);

      if (entry.MeshedVersion == entry.Version && entry.MeshedFloor == floor && entry.MeshedCeiling == ceiling && (entry.MeshedCollision || !collide))
      {
        continue;
      }

      if (!NeighboursHaveData(coord))
      {
        continue;
      }

      StartMesh(entry, floor, ceiling, collide);
    }
    // Then: data for the whole wanted square (radius + 1).
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

  static bool WantsCollision(Vector2I coord, Vector2I[] viewerChunks)
  {
    foreach (var v in viewerChunks)
    {
      if (Math.Max(Math.Abs(coord.X - v.X), Math.Abs(coord.Y - v.Y)) <= CollisionRadius)
      {
        return true;
      }
    }

    return false;
  }

  void StartMesh(Entry entry, int floor, int ceiling, bool collide)
  {
    var version = entry.Version;
    var blocks = entry.Blocks!;
    var neighbours = new ChunkData?[]
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
      var data = ChunkMesher.Build(blocks, neighbours, BlockIds.TileByBlock, BlockIds.TileCount, TextureAtlas.UvInset, floorLayer: floor, ceilingLayer: ceiling, collision: collide);
      meshResults.Enqueue(new MeshResult { Entry = entry, Version = version, Floor = floor, Ceiling = ceiling, Collision = collide, Build = ChunkBuild.From(data, collide) });
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
          blocks.Set(index % VoxelMath.ChunkSize, index / VoxelMath.LayerSize, index / VoxelMath.ChunkSize % VoxelMath.ChunkSize, id);
        }
      }

      result.Entry.Blocks = blocks;
      result.Entry.Version++;
      dirty = true;
      ChunkChanged?.Invoke(result.Entry.Coord);
    }
  }

  void DrainMeshResults()
  {
    var applied = 0;
    var started = System.Diagnostics.Stopwatch.GetTimestamp();

    // Uploading a mesh and building its collision happen on this thread and are the main cost of walking through new ground:
    // take finished meshes until the frame's budget is spent (the cap is a safety net).
    while (applied < Settings.MeshAppliesPerFrame * 4
      && (applied == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < ApplyBudgetMs)
      && meshResults.TryDequeue(out var result))
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
      entry.MeshedFloor = result.Floor;
      entry.MeshedCeiling = result.Ceiling;
      entry.MeshedCollision = result.Collision;
      dirty = true;
    }
  }
}
