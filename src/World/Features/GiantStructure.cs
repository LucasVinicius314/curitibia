using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Curitibia.Core;

namespace Curitibia.World.Features;

/// <summary>
/// Everything of a giant tree that is not its trunk or its canopy discs, grown once from the tree's own hash and kept: the great
/// limbs that spread under the leaves (each its own length, thickness, curve and number of side branches, some of them absent),
/// the side branches and twigs that fork off them, clumps of foliage along every branch and at its tip, the shorter branches on
/// the bare lower trunk (each its own height, length, droop and thickness, sometimes none at all), the buttress roots that arch
/// out of the ground around the base (a few of them high arches), and the vines that hang from branches and from the underside of
/// the canopy. A pure function of the tree (so every chunk and every peer agrees), engine-free; the result is sorted into the
/// chunks it falls in so that placing it in a chunk is a walk over a short list.
/// </summary>
public sealed class GiantStructure
{
  /// <summary>The wood and leaves of this structure that fall in one chunk: packed as y &lt;&lt; 10 | z &lt;&lt; 5 | x, chunk-local x and z.</summary>
  public sealed class Bucket
  {
    public int[] Wood = Array.Empty<int>();
    public int[] Leaves = Array.Empty<int>();
  }

  const int CacheLimit = 2600;

  static readonly ConcurrentDictionary<Tree, GiantStructure> cache = new();

  readonly Tree tree;
  readonly Dictionary<(int, int), Bucket> buckets = new();
  readonly Dictionary<long, byte> voxels = new();

  uint state;
  int reach;

  /// <summary>The great limbs (the ones that spread under the canopy) that were grown, and how far each one reaches from the trunk, in voxels.</summary>
  public List<float> LimbReaches { get; } = new();

  public int LowBranches { get; private set; }

  public int Roots { get; private set; }

  public int Vines { get; private set; }

  public int WoodVoxels { get; private set; }

  public int LeafVoxels { get; private set; }

  /// <summary>Leaf voxels hanging below the underside of the canopy (foliage on branches, vines).</summary>
  public int HangingLeafVoxels { get; private set; }

  GiantStructure(Tree tree)
  {
    this.tree = tree;
    reach = tree.Reach - 2;
    state = Mix((uint)tree.X * 73856093u ^ (uint)tree.Z * 19349663u ^ (uint)tree.Variant * 83492791u ^ 0x2545f491u);
    Build();
  }

  /// <summary>The structure of a giant (built on first use, then cached).</summary>
  public static GiantStructure For(Tree tree)
  {
    if (cache.TryGetValue(tree, out var known))
    {
      return known;
    }

    var built = new GiantStructure(tree);

    if (cache.Count > CacheLimit)
    {
      cache.Clear();
    }

    cache[tree] = built;

    return built;
  }

  public bool TryGetBucket(int chunkX, int chunkZ, out Bucket? bucket)
  {
    return buckets.TryGetValue((chunkX, chunkZ), out bucket);
  }

  // ------------------------------------------------------------------ random numbers (a small xorshift: deterministic everywhere)

  static uint Mix(uint h)
  {
    h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;

    return h == 0 ? 0x9e3779b9u : h;
  }

  float Next()
  {
    state ^= state << 13;
    state ^= state >> 17;
    state ^= state << 5;

    return (state & 0xffffff) / 16777216f;
  }

  float Range(float low, float high) => low + (high - low) * Next();

  int Range(int low, int highInclusive) => low + (int)(Next() * (highInclusive - low + 1));

  // ------------------------------------------------------------------ voxels

  const byte WoodKind = 1;
  const byte LeafKind = 2;

  void Put(int x, int y, int z, byte kind)
  {
    if (y < 0 || y >= VoxelMath.ChunkHeight)
    {
      return;
    }

    var dx = x - tree.CentreX;
    var dz = z - tree.CentreZ;

    if (dx * dx + dz * dz > (float)reach * reach)
    {
      return; // nothing grows past the reach the chunks look for
    }

    var key = ((long)(x + 1048576) << 36) | ((long)(z + 1048576) << 12) | (uint)y;

    if (kind == WoodKind)
    {
      voxels[key] = WoodKind;
    }
    else
    {
      voxels.TryAdd(key, LeafKind);
    }
  }

  void Sphere(float cx, float cy, float cz, float radius, byte kind)
  {
    var r = (int)MathF.Ceiling(radius);
    var r2 = radius * radius + 0.3f;
    var x0 = (int)MathF.Floor(cx);
    var y0 = (int)MathF.Floor(cy);
    var z0 = (int)MathF.Floor(cz);

    for (int y = y0 - r; y <= y0 + r; y++)
    {
      for (int z = z0 - r; z <= z0 + r; z++)
      {
        for (int x = x0 - r; x <= x0 + r; x++)
        {
          var dx = x + 0.5f - cx;
          var dy = y + 0.5f - cy;
          var dz = z + 0.5f - cz;

          if (dx * dx + dy * dy + dz * dz <= r2)
          {
            Put(x, y, z, kind);
          }
        }
      }
    }
  }

  /// <summary>A lump of foliage: a ragged ellipsoid, flatter than it is wide, never a perfect ball.</summary>
  void Foliage(float cx, float cy, float cz, float radius)
  {
    var ry = radius * Range(0.55f, 0.85f);
    var r = (int)MathF.Ceiling(radius) + 1;
    var x0 = (int)MathF.Floor(cx);
    var y0 = (int)MathF.Floor(cy);
    var z0 = (int)MathF.Floor(cz);

    for (int y = y0 - r; y <= y0 + r; y++)
    {
      for (int z = z0 - r; z <= z0 + r; z++)
      {
        for (int x = x0 - r; x <= x0 + r; x++)
        {
          var dx = (x + 0.5f - cx) / radius;
          var dy = (y + 0.5f - cy) / ry;
          var dz = (z + 0.5f - cz) / radius;
          var edge = 1f + 0.35f * (Next() - 0.5f);

          if (dx * dx + dy * dy + dz * dz <= edge)
          {
            Put(x, y, z, LeafKind);
          }
        }
      }
    }
  }

  /// <summary>A vine: a thin string of leaves hanging straight down from a point.</summary>
  void Vine(float x, float y, float z, int length)
  {
    var xi = (int)MathF.Floor(x);
    var zi = (int)MathF.Floor(z);

    for (int i = 0; i < length; i++)
    {
      Put(xi, (int)MathF.Floor(y) - i, zi, LeafKind);

      // Now and then a vine swells to two leaves wide, or wanders a voxel sideways.
      if (Next() < 0.12f)
      {
        Put(xi + (Next() < 0.5f ? 1 : -1), (int)MathF.Floor(y) - i, zi, LeafKind);
      }
    }

    Vines++;
  }

  // ------------------------------------------------------------------ trunk geometry

  /// <summary>The middle of the trunk at a height and the radius there (the same function the tree itself uses).</summary>
  void Trunk(float height, out float x, out float z, out float radius)
  {
    tree.TrunkCentreAt((int)height, out x, out z);
    radius = tree.TrunkRadiusAt((int)height);
  }

  // ------------------------------------------------------------------ branches

  /// <summary>
  /// Grows a branch from a point: it follows a curve (rising at <paramref name="elevation"/>, curling up by
  /// <paramref name="curl"/> and sagging by <paramref name="sag"/> along its length, drifting sideways), tapers from
  /// <paramref name="r0"/> to <paramref name="r1"/>, forks into side branches while it is thick enough, carries lumps of foliage and
  /// sheds vines. <paramref name="depth"/> 0 is a great limb; the side branches and twigs are 1 and 2.
  /// </summary>
  void Grow(float x, float y, float z, float azimuth, float elevation, float length, float r0, float r1, float curl, float sag, int depth, float leafiness, float forking)
  {
    var steps = Math.Max(2, (int)length);
    var drift = Range(-0.03f, 0.03f);
    var wobbleAmount = Range(0.01f, 0.05f);
    var wobblePhase = Range(0f, MathF.Tau);
    var wobbleRate = Range(0.12f, 0.35f);
    var forks = 0;

    for (int i = 0; i <= steps; i++)
    {
      var t = i / (float)steps;
      var radius = r0 + (r1 - r0) * MathF.Pow(t, 0.7f);
      var heading = azimuth + drift * i + wobbleAmount * MathF.Sin(wobblePhase + i * wobbleRate) * i * 0.2f;
      var pitch = elevation + curl * t - sag * t * t;

      Sphere(x, y, z, radius, WoodKind);

      // Foliage clings to the branch: sparse along it, thicker toward the tip.
      if (depth <= 1 && t > 0.2f && Next() < leafiness * (0.06f + 0.16f * t))
      {
        Foliage(x + Range(-1f, 1f), y + Range(-0.5f, 1.5f), z + Range(-1f, 1f), Range(2.4f, 4.6f) * (depth == 0 ? 1f : 0.8f));
      }

      // Side branches fork off the thicker part.
      if (depth < 2 && t > 0.18f && t < 0.9f && radius > 0.9f && Next() < forking * (depth == 0 ? 0.075f : 0.05f))
      {
        var side = Next() < 0.5f ? 1f : -1f;

        forks++;
        Grow(
          x, y, z,
          heading + side * Range(0.5f, 1.25f),
          pitch + Range(0.1f, 0.55f),
          length * Range(0.2f, 0.5f) * (1f - t * 0.5f),
          Math.Max(0.8f, radius * Range(0.45f, 0.7f)),
          0.6f,
          Range(0f, 0.4f),
          Range(0.1f, 0.6f),
          depth + 1,
          leafiness,
          forking);
      }

      if (depth == 0 && t > 0.15f && Next() < 0.1f)
      {
        Vine(x + Range(-0.5f, 0.5f), y - radius, z + Range(-0.5f, 0.5f), Range(3, 12));
      }

      x += MathF.Cos(pitch) * MathF.Cos(heading);
      y += MathF.Sin(pitch);
      z += MathF.Cos(pitch) * MathF.Sin(heading);
    }

    // The tip: a big lump of foliage on a great limb or a branch, a small tuft on a twig.
    if (Next() < Math.Min(1f, leafiness + 0.25f))
    {
      Foliage(x, y + 0.5f, z, depth == 0 ? Range(4.5f, 8.5f) : depth == 1 ? Range(3f, 5.5f) : Range(2f, 3.6f));
    }

    if (depth == 0 && Next() < 0.8f)
    {
      Vine(x, y - 2f, z, Range(5, 16));
    }
  }

  // ------------------------------------------------------------------ the whole tree

  void Build()
  {
    var height = tree.TrunkHeight;
    var radiusCanopy = tree.CanopyRadius;
    var leafiness = Range(0.45f, 0.95f);
    var forking = Range(0.35f, 1.4f);

    // 1. The great limbs that spread out under the canopy: unevenly spaced, a few missing, each its own length and thickness;
    //    at least two of them are long (1.5 to 1.7 times the reach of the leaves above).
    var limbSlots = Range(6, 11);
    var phase = Range(0f, MathF.Tau);
    var skipped = 0;

    for (int i = 0; i < limbSlots; i++)
    {
      var guaranteedLong = i < 2;

      if (!guaranteedLong && skipped < limbSlots - 5 && Next() < 0.16f)
      {
        skipped++;

        continue; // no limb here
      }

      var azimuth = phase + (i + Range(-0.45f, 0.45f)) * MathF.Tau / limbSlots;
      var above = tree.BaseY + height * Range(0.30f, 0.46f);
      var length = guaranteedLong ? radiusCanopy * Range(1.5f, 1.66f) : Next() < 0.3f ? radiusCanopy * Range(1.3f, 1.6f) : radiusCanopy * Range(0.55f, 1.25f);
      var r0 = guaranteedLong ? Range(2.4f, 3.6f) : Range(1.3f, 3.9f);

      Trunk(above, out var tx, out var tz, out var tr);

      Grow(
        tx + MathF.Cos(azimuth) * tr * 0.6f, above, tz + MathF.Sin(azimuth) * tr * 0.6f,
        azimuth,
        Range(0.05f, 0.3f),
        length * 1.03f,
        r0,
        Range(0.6f, 1.1f),
        Range(0.05f, 0.35f),
        Range(0.05f, 0.4f),
        0,
        leafiness,
        forking);

      LimbReaches.Add(length);
    }

    // 2. Branches on the lower trunk: a handful or none, each at its own height, length, thickness and droop.
    LowBranches = Next() < 0.12f ? 0 : Range(2, 8);

    for (int i = 0; i < LowBranches; i++)
    {
      var above = tree.BaseY + height * Range(0.1f, 0.34f);
      var azimuth = Range(0f, MathF.Tau);
      var length = Next() < 0.25f ? Range(14f, 30f) : Range(4f, 14f);
      var r0 = Next() < 0.3f ? Range(2f, 3.2f) : Range(0.9f, 1.8f);

      Trunk(above, out var tx, out var tz, out var tr);

      Grow(
        tx + MathF.Cos(azimuth) * tr * 0.7f, above, tz + MathF.Sin(azimuth) * tr * 0.7f,
        azimuth,
        Range(-0.35f, 0.4f),
        length,
        r0,
        Range(0.55f, 0.9f),
        Range(-0.1f, 0.35f),
        Range(0f, 0.6f),
        1,
        leafiness * 0.8f,
        forking * 0.7f);
    }

    // 3. Buttress roots: arching out of the ground around the base, some of them high arches that bridge over the ground.
    Roots = Range(5, 9);

    var rootPhase = Range(0f, MathF.Tau);

    for (int i = 0; i < Roots; i++)
    {
      var azimuth = rootPhase + (i + Range(-0.4f, 0.4f)) * MathF.Tau / Roots;
      var arching = Next() < 0.4f;
      var run = arching ? Range(26f, 46f) : Range(14f, 30f);
      var archHeight = arching ? Range(5f, 13f) : Range(0f, 3f);
      var start = tree.BaseY + Range(7f, 17f);
      var thickness = Range(2.6f, 4.6f);

      Trunk(start, out var tx, out var tz, out var tr);

      var steps = (int)(run * 1.4f);

      for (int s = 0; s <= steps; s++)
      {
        var t = s / (float)steps;
        var d = tr * 0.6f + run * t;
        var sway = MathF.Sin(t * 5f + i) * 1.8f * t;
        var a = azimuth + sway / Math.Max(4f, d);
        var h = start - (start - tree.BaseY + 4f) * MathF.Pow(t, 1.7f) + archHeight * MathF.Sin(t * MathF.PI);
        var r = thickness * (1f - 0.78f * t) + 0.5f;

        Sphere(tx + MathF.Cos(a) * d, h, tz + MathF.Sin(a) * d, r, WoodKind);

        // Moss and fern on the roots, and a vine now and then from an arch.
        if (t > 0.1f && t < 0.9f && Next() < 0.07f)
        {
          Foliage(tx + MathF.Cos(a) * d, h + r + 0.5f, tz + MathF.Sin(a) * d, Range(1.8f, 3.4f));
        }

        if (arching && t > 0.25f && t < 0.75f && Next() < 0.06f)
        {
          Vine(tx + MathF.Cos(a) * d, h - r, tz + MathF.Sin(a) * d, Range(3, 9));
        }
      }
    }

    // 4. Vines from the underside of the canopy's lowest tier, at every distance, in clumps.
    var centreY = tree.BaseY + height * 0.5f;
    var thickness0 = 8f + radiusCanopy * 0.22f;
    var undersideY = centreY - thickness0;
    var clumps = Range(14, 34);

    for (int c = 0; c < clumps; c++)
    {
      var angle = Range(0f, MathF.Tau);
      var distance = radiusCanopy * Range(0.25f, 0.98f);
      var cx = tree.CentreX + MathF.Cos(angle) * distance;
      var cz = tree.CentreZ + MathF.Sin(angle) * distance;
      var y = centreY - thickness0 * MathF.Sqrt(Math.Max(0f, 1f - distance * distance / (radiusCanopy * radiusCanopy))) + 2f;
      var strands = Range(2, 6);

      for (int s = 0; s < strands; s++)
      {
        Vine(cx + Range(-2.5f, 2.5f), y + Range(0f, 3f), cz + Range(-2.5f, 2.5f), Range(3, Next() < 0.2f ? 26 : 13));
      }
    }

    // Sort the voxels into chunks.
    Dictionary<(int, int), (List<int> wood, List<int> leaves)> lists = new();

    foreach (var (key, kind) in voxels)
    {
      var x = (int)(key >> 36) - 1048576;
      var z = (int)((key >> 12) & 0xffffff) - 1048576;
      var y = (int)(key & 0xfff);
      var chunk = (VoxelMath.FloorDiv(x, VoxelMath.ChunkSize), VoxelMath.FloorDiv(z, VoxelMath.ChunkSize));

      if (!lists.TryGetValue(chunk, out var entry))
      {
        entry = (new List<int>(), new List<int>());
        lists[chunk] = entry;
      }

      var packed = (y << 10) | ((z - chunk.Item2 * VoxelMath.ChunkSize) << 5) | (x - chunk.Item1 * VoxelMath.ChunkSize);

      if (kind == WoodKind)
      {
        entry.wood.Add(packed);
        WoodVoxels++;
      }
      else
      {
        entry.leaves.Add(packed);
        LeafVoxels++;

        if (y < undersideY)
        {
          HangingLeafVoxels++;
        }
      }
    }

    foreach (var (chunk, entry) in lists)
    {
      buckets[chunk] = new Bucket { Wood = entry.wood.ToArray(), Leaves = entry.leaves.ToArray() };
    }

    voxels.Clear();
    voxels.TrimExcess();
  }
}
