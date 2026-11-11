using System;
using Curitibia.Core;
using Curitibia.World.Features;

namespace Curitibia.World;

/// <summary>Tuning for terrain generation (filled from the WorldGenSettings resource). Engine-free.</summary>
public readonly record struct WorldGenParams(
  int Seed,
  float Frequency,
  float BaseHeight,
  int Octaves,
  float BiomeFrequency = 0.00022f,
  float ReliefScale = 1f);

/// <summary>
/// What the generator decides for one world column. <see cref="Biome"/> is the biome that rules it (its name, its relief);
/// <see cref="SurfaceBiome"/> is the biome whose blocks and trees it gets, which near a border is sometimes the neighbour's,
/// so two biomes mix into each other instead of meeting along a line.
/// </summary>
public readonly record struct ColumnInfo(int Height, BiomeId Biome, BiomeId SurfaceBiome);

/// <summary>
/// Terrain as a pure function: (params, chunk coordinate) gives the same bytes on every peer, on any thread. A BiomeMap
/// decides which biome rules each column; the height blends the biomes' relief and the biome picks the blocks (surface,
/// filler, stone below) and how many trees grow.
/// </summary>
public sealed class WorldGenerator
{
  /// <summary>
  /// The mountains' own baseline. A steep ramp from the border of a range: how far the mountain biome's weight leads the
  /// strongest rival (zero exactly at the border, so lowland next to a range is untouched) lifts the ground by RampUplift
  /// voxels once the lead reaches RampLead, a few hundred metres in, which is already about the height the peaks used to
  /// reach. Then a slower climb by another CoreUplift voxels as the ruggedness (see BiomeMap) keeps rising toward the heart
  /// of the range. The biome's relief and its sharp ridges stand on top of all of that.
  /// </summary>
  const float RampLead = 0.12f;
  const float RampUplift = 160f;
  const float CoreStart = 0.56f;
  const float CoreUplift = 45f;

  /// <summary>
  /// The mix at a border, sized in voxels, not in weights (so it is as wide on a slow gradient as on a sharp one). How far a
  /// column is from the border is estimated from the weights and their slope there; within BlendHalfWidth of it either side,
  /// the neighbour's surface is used with a probability that rises from 0 to 1 across that distance. Two scales of noise move
  /// the border about (BorderWarp wavy, BorderGrain jagged, in voxels), so it is no clean curve.
  /// </summary>
  const float BlendHalfWidth = 56f;
  const float BorderWarp = 16f;
  const float BorderGrain = 6f;

  /// <summary>How far, in voxels, the weights are sampled to measure their slope.</summary>
  const int SlopeStep = 24;

  /// <summary>Columns along a side of one clump of the mix.</summary>
  const int PatchSize = 3;

  readonly WorldGenParams p;
  readonly GradientNoise noise;
  readonly GradientNoise ridgeNoise;
  readonly BiomeMap biomes;
  readonly CaveCarver caves;
  readonly GradientNoise rockNoise;

  /// <summary>
  /// The biome weights and ruggedness only change over hundreds of voxels, but each is an average of nine samples of several
  /// noises. They are measured on a lattice every LatticeStep columns (cached per thread) and blended between, which is what
  /// every column uses, so it stays a pure function of the position and costs a fraction as much.
  /// </summary>
  const int LatticeStep = 8;

  readonly System.Threading.ThreadLocal<System.Collections.Generic.Dictionary<long, float[]>> lattice =
    new(() => new System.Collections.Generic.Dictionary<long, float[]>());
  readonly TreeFeature trees;
  readonly LogFeature logs;

  public WorldGenerator(WorldGenParams p)
  {
    this.p = p;
    noise = new GradientNoise(p.Seed);
    ridgeNoise = new GradientNoise(p.Seed + 15485863);
    biomes = new BiomeMap(p.Seed, p.BiomeFrequency);
    caves = new CaveCarver(p.Seed);
    rockNoise = new GradientNoise(p.Seed + 60013);
    trees = new TreeFeature(p.Seed, ColumnHeight, TreeDensityAt, TreeKindAt, caves.IsMouth, GiantDensityAt);
    logs = new LogFeature(p.Seed, ColumnHeight, LogDensityAt, caves.IsMouth);
  }

  float[] LatticePoint(int ix, int iz)
  {
    var cache = lattice.Value!;
    var key = ((long)ix << 32) ^ (uint)iz;

    if (cache.TryGetValue(key, out var point))
    {
      return point;
    }

    if (cache.Count > 40000)
    {
      cache.Clear();
    }

    point = new float[BiomeMap.Count + 1];
    biomes.Weights(ix * LatticeStep, iz * LatticeStep, point.AsSpan(0, BiomeMap.Count), out var rugged);
    point[BiomeMap.Count] = rugged;
    cache[key] = point;

    return point;
  }

  /// <summary>The biome weights (summing to about 1), the ruggedness, and the strongest biome at a column.</summary>
  BiomeId Weigh(int wx, int wz, Span<float> weights, out float rugged)
  {
    var ix = VoxelMath.FloorDiv(wx, LatticeStep);
    var iz = VoxelMath.FloorDiv(wz, LatticeStep);
    var fx = (wx - ix * LatticeStep) / (float)LatticeStep;
    var fz = (wz - iz * LatticeStep) / (float)LatticeStep;

    var p00 = LatticePoint(ix, iz);
    var p10 = LatticePoint(ix + 1, iz);
    var p01 = LatticePoint(ix, iz + 1);
    var p11 = LatticePoint(ix + 1, iz + 1);

    var best = 0;

    for (int i = 0; i < BiomeMap.Count; i++)
    {
      var a = p00[i] + (p10[i] - p00[i]) * fx;
      var b = p01[i] + (p11[i] - p01[i]) * fx;

      weights[i] = a + (b - a) * fz;

      if (weights[i] > weights[best])
      {
        best = i;
      }
    }

    var n = BiomeMap.Count;
    var ra = p00[n] + (p10[n] - p00[n]) * fx;
    var rb = p01[n] + (p11[n] - p01[n]) * fx;
    rugged = ra + (rb - ra) * fz;

    return (BiomeId)best;
  }

  public BiomeId BiomeAt(int wx, int wz)
  {
    Span<float> weights = stackalloc float[BiomeMap.Count];

    return Weigh(wx, wz, weights, out _);
  }

  /// <summary>The kind of tree at a column: the biome's, with its second kind mixed in by a hash of the position.</summary>
  public TreeKind TreeKindAt(int wx, int wz)
  {
    var biome = Biomes.Get(Column(wx, wz).SurfaceBiome);

    if (biome.AltShare <= 0f)
    {
      return biome.Tree;
    }

    unchecked
    {
      var h = (uint)p.Seed * 2654435761u + (uint)wx * 374761393u + (uint)wz * 668265263u;
      h = (h ^ (h >> 13)) * 1274126177u;
      h ^= h >> 16;

      return (h & 0xffff) / 65535f < biome.AltShare ? biome.AltTree : biome.Tree;
    }
  }

  /// <summary>Chance of a tree for a world column: the biome's density, none on bare snow-capped rock.</summary>
  public float TreeDensityAt(int wx, int wz)
  {
    var column = Column(wx, wz);
    var biome = Biomes.Get(column.SurfaceBiome);

    return biome.SurfaceAt(column.Height - (int)p.BaseHeight) == BlockIds.Snow && biome.Surface != BlockIds.Snow ? 0f : biome.TreeDensity;
  }

  /// <summary>Chance of a giant tree for a world column: the biome's, only where the ground is the biome's own.</summary>
  public float GiantDensityAt(int wx, int wz)
  {
    return Biomes.Get(Column(wx, wz).SurfaceBiome).GiantDensity;
  }

  /// <summary>Chance of a dead log for a world column.</summary>
  public float LogDensityAt(int wx, int wz)
  {
    return Biomes.Get(Column(wx, wz).SurfaceBiome).LogDensity;
  }

  /// <summary>
  /// The colour multipliers of grass and of leaves for each column of a chunk (RGB bytes, three per column, index x + z * 32): the
  /// biomes' tints blended by the biome weights, so the colour changes smoothly across a border instead of in a line.
  /// </summary>
  public void ColumnTints(int chunkX, int chunkZ, byte[] grass, byte[] foliage)
  {
    const int size = VoxelMath.ChunkSize;
    Span<float> weights = stackalloc float[BiomeMap.Count];

    for (int z = 0; z < size; z++)
    {
      for (int x = 0; x < size; x++)
      {
        Weigh(chunkX * size + x, chunkZ * size + z, weights, out _);

        float gr = 0f, gg = 0f, gb = 0f, fr = 0f, fg = 0f, fb = 0f, total = 0f;

        // The biome weights are blurred over hundreds of metres and often close to each other; a softmax (temperature 1/40) of them
        // favours the ruling biome, so a biome's colour is its own in its heartland and still blends across the border.
        var leader = 0f;

        for (int i = 0; i < weights.Length; i++)
        {
          leader = Math.Max(leader, weights[i]);
        }

        for (int i = 0; i < weights.Length; i++)
        {
          var biome = Biomes.All[i];
          var grassTint = biome.GrassTint ?? Rgb.White;
          var foliageTint = biome.FoliageTint ?? Rgb.White;
          var w = MathF.Exp((weights[i] - leader) * 40f);

          total += w;
          gr += w * grassTint.R; gg += w * grassTint.G; gb += w * grassTint.B;
          fr += w * foliageTint.R; fg += w * foliageTint.G; fb += w * foliageTint.B;
        }

        if (total > 1e-9f)
        {
          gr /= total; gg /= total; gb /= total; fr /= total; fg /= total; fb /= total;
        }

        var at = (x + z * size) * 3;

        grass[at] = Channel(gr); grass[at + 1] = Channel(gg); grass[at + 2] = Channel(gb);
        foliage[at] = Channel(fr); foliage[at + 1] = Channel(fg); foliage[at + 2] = Channel(fb);
      }
    }
  }

  static byte Channel(float multiplier) => (byte)Math.Clamp((int)(multiplier * 255f + 0.5f), 0, 255);

  /// <summary>
  /// The closest column of a biome to a column, looking outward in square rings on a coarse grid (the smallest patch of a biome is
  /// hundreds of metres across), preferring a spot well inside it (not on its very edge). Thread-safe; false when there is none within
  /// about 19 km.
  /// </summary>
  public bool FindNearestBiome(int wx, int wz, BiomeId biome, out int foundX, out int foundZ)
  {
    const int step = 150;
    const int maxRing = 260;

    foundX = wx;
    foundZ = wz;

    for (int ring = 0; ring <= maxRing; ring++)
    {
      var hits = new System.Collections.Generic.List<(int x, int z)>();

      for (int dz = -ring; dz <= ring; dz++)
      {
        var edge = Math.Abs(dz) == ring;

        for (int dx = -ring; dx <= ring; dx += edge ? 1 : Math.Max(1, 2 * ring))
        {
          var x = wx + dx * step;
          var z = wz + dz * step;

          if (BiomeAt(x, z) == biome)
          {
            hits.Add((x, z));
          }
        }
      }

      if (hits.Count == 0)
      {
        continue;
      }

      hits.Sort((a, b) => (((long)a.x - wx) * ((long)a.x - wx) + ((long)a.z - wz) * ((long)a.z - wz)).CompareTo(((long)b.x - wx) * ((long)b.x - wx) + ((long)b.z - wz) * ((long)b.z - wz)));

      var pick = hits[0];

      foreach (var (x, z) in hits)
      {
        if (BiomeAt(x + 300, z) == biome && BiomeAt(x - 300, z) == biome && BiomeAt(x, z + 300) == biome && BiomeAt(x, z - 300) == biome)
        {
          pick = (x, z);
          break;
        }
      }

      foundX = pick.x;
      foundZ = pick.z;

      return true;
    }

    return false;
  }

  /// <summary>Number of solid voxels in the column at world (wx, wz); the top voxel is at height - 1.</summary>
  public int ColumnHeight(int wx, int wz)
  {
    return Column(wx, wz).Height;
  }

  /// <summary>Height and ruling biome of a column. Heights blend by the biome weights, so borders are slopes, not cliffs.</summary>
  public ColumnInfo Column(int wx, int wz)
  {
    Span<float> weights = stackalloc float[BiomeMap.Count];
    var dominant = Weigh(wx, wz, weights, out var rugged);

    var smooth = Fbm(wx, wz);
    var ridge = Ridges(wx, wz);
    var h = 0f;

    for (int i = 0; i < weights.Length; i++)
    {
      if (weights[i] < 0.00022f)
      {
        continue;
      }

      var b = Biomes.All[i];
      var shape = smooth + (ridge - smooth) * b.Ridged;

      h += weights[i] * (b.BaseOffset + b.Relief * p.ReliefScale * shape);
    }

    // The mountains' own baseline: the ground keeps climbing with how mountainous the country is, from the foothills at the
    // edge of the range to the heart of it, and the sharp ridges and peaks stand on top of that.
    var rival = 0f;

    for (int i = 0; i < weights.Length; i++)
    {
      if (i != (int)BiomeId.Mountains)
      {
        rival = Math.Max(rival, weights[i]);
      }
    }

    // How far the mountains lead the strongest other biome: zero exactly at the border, growing inside it.
    var lead = weights[(int)BiomeId.Mountains] - rival;

    h += p.ReliefScale * (RampUplift * SmoothStep(0f, RampLead, lead) + CoreUplift * SmoothStep(CoreStart, 1f, rugged));

    // The Titan Grove is cut by narrow, deep trenches, only in its heartland (the cut fades out with the biome's weight).
    var grove = weights[(int)BiomeId.Grove];

    if (grove > 0.2f)
    {
      h -= Biomes.Get(BiomeId.Grove).TrenchDepth * SmoothStep(0.15f, 0.35f, grove) * TrenchProfile(wx, wz);
    }

    return new ColumnInfo(Math.Clamp((int)(p.BaseHeight + h), 1, VoxelMath.ChunkHeight), dominant, SurfaceBiomeOf(wx, wz, weights, dominant));
  }

  /// <summary>
  /// 1 in the middle of a trench, 0 beside it: trenches run where a slow noise crosses zero (winding lines, as in a river bed),
  /// wobbled sideways by a second noise so they are not straight, with steep walls.
  /// </summary>
  float TrenchProfile(int wx, int wz)
  {
    var warpX = wx + 22f * ridgeNoise.Sample(wx * 0.02f + 40.3f, wz * 0.02f + 13.7f) / 0.7f;
    var warpZ = wz + 22f * ridgeNoise.Sample(wx * 0.02f + 8.1f, wz * 0.02f + 71.9f) / 0.7f;
    var line = MathF.Abs(ridgeNoise.Sample(warpX * 0.009f + 31.1f, warpZ * 0.009f + 17.3f) / 0.7f);

    return 1f - SmoothStep(0.05f, 0.1f, line);
  }

  /// <summary>The biome whose surface this column gets: the ruling one, or near a border sometimes the strongest neighbour.</summary>
  BiomeId SurfaceBiomeOf(int wx, int wz, Span<float> weights, BiomeId dominant)
  {
    var second = -1;

    for (int i = 0; i < weights.Length; i++)
    {
      if (i != (int)dominant && (second < 0 || weights[i] > weights[second]))
      {
        second = i;
      }
    }

    if (second < 0 || weights[second] < 0.02f)
    {
      return dominant; // nowhere near a border
    }

    // The pair in a fixed order (lower id first), whichever of them rules here, so the signed distance below is continuous
    // across the border.
    var low = Math.Min((int)dominant, second);
    var high = Math.Max((int)dominant, second);
    var lead = weights[high] - weights[low];

    // Far from a border in weight terms: skip the slope measurement.
    if (Math.Abs(lead) > 0.6f)
    {
      return (BiomeId)(lead > 0f ? high : low);
    }

    // Distance to the border = how far the lead is from zero, over how fast it changes (measured a few voxels along each axis).
    Span<float> probe = stackalloc float[weights.Length];

    Weigh(wx + SlopeStep, wz, probe, out _);
    var dx = probe[high] - probe[low] - lead;

    Weigh(wx, wz + SlopeStep, probe, out _);
    var dz = probe[high] - probe[low] - lead;

    var slope = MathF.Sqrt(dx * dx + dz * dz) / SlopeStep;
    var distance = slope < 1e-7f ? (lead > 0f ? 1e6f : -1e6f) : lead / slope;

    distance += BorderWarp * ridgeNoise.Sample(wx * 0.015f + 3.1f, wz * 0.015f + 7.7f) / 0.7f
      + BorderGrain * ridgeNoise.Sample(wx * 0.07f + 11.3f, wz * 0.07f + 5.9f) / 0.7f;

    var chance = SmoothStep(-BlendHalfWidth, BlendHalfWidth, distance);

    return ColumnNoise(wx, wz) < chance ? (BiomeId)high : (BiomeId)low;
  }

  /// <summary>
  /// A noise for a world column in [0, 1): the same on every peer and every thread. Part of it is shared by blocks of a few
  /// columns, so the mix at a border clumps into small patches instead of being static, and part is per column, so the
  /// patches have ragged edges.
  /// </summary>
  float ColumnNoise(int wx, int wz)
  {
    return 0.6f * Hash01(VoxelMath.FloorDiv(wx, PatchSize), VoxelMath.FloorDiv(wz, PatchSize), 1u) + 0.4f * Hash01(wx, wz, 2u);
  }

  float Hash01(int x, int z, uint salt)
  {
    unchecked
    {
      var h = (uint)p.Seed * 2246822519u + (uint)x * 374761393u + (uint)z * 668265263u + salt * 0x9E3779B1u;
      h = (h ^ (h >> 15)) * 2246822519u;
      h = (h ^ (h >> 13)) * 3266489917u;
      h ^= h >> 16;

      return (h & 0xffffff) / 16777216f;
    }
  }

  static float SmoothStep(float edge0, float edge1, float x)
  {
    var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);

    return t * t * (3f - 2f * t);
  }

  /// <summary>Rolling-hill shape in [0, 1].</summary>
  float Fbm(int wx, int wz)
  {
    var amp = 1f;
    var freq = p.Frequency;
    var sum = 0f;
    var norm = 0f;

    for (int o = 0; o < Math.Max(1, p.Octaves); o++)
    {
      sum += noise.Sample(wx * freq + 0.5f, wz * freq + 0.5f) * amp;
      norm += amp;
      amp *= 0.35f;
      freq *= 2f;
    }

    // Gradient noise in 2D stays within about +-0.7, so remap that to [0, 1].
    return Math.Clamp(0.5f + 0.5f * (sum / norm) / 0.7f, 0f, 1f);
  }

  /// <summary>Ridged shape in [0, 1]: sharp crests where the noise crosses zero, squared so peaks are narrow and valleys broad.</summary>
  float Ridges(int wx, int wz)
  {
    var amp = 1f;
    var freq = p.Frequency * 0.4f;
    var sum = 0f;
    var norm = 0f;

    for (int o = 0; o < 3; o++)
    {
      var v = Math.Clamp(1f - MathF.Abs(ridgeNoise.Sample(wx * freq + 0.5f, wz * freq + 0.5f)) / 0.7f, 0f, 1f);

      sum += v * v * amp;
      norm += amp;
      amp *= 0.25f;
      freq *= 2f;
    }

    return sum / norm;
  }

  /// <summary>Height (exclusive) of the highest thing standing in a column, trees included.</summary>
  public int SurfaceTop(int wx, int wz)
  {
    return Math.Min(VoxelMath.ChunkHeight, Math.Max(ColumnHeight(wx, wz), trees.TopAt(wx, wz)));
  }

  /// <summary>
  /// One chunk's voxels: rock all the way down (stone, and hellrock in the underworld), the biome's blocks near the surface,
  /// caves carved out of the rock, then trees. Sections that are one block all through cost no memory.
  /// </summary>
  public ChunkData GenerateChunk(int cx, int cz)
  {
    const int size = VoxelMath.ChunkSize;
    var x0 = cx * size;
    var z0 = cz * size;

    var columns = new ColumnInfo[size * size];
    var heights = new int[size * size];
    var tallest = 0;
    var lowest = int.MaxValue;

    for (int z = 0; z < size; z++)
    {
      for (int x = 0; x < size; x++)
      {
        var column = Column(x0 + x, z0 + z);
        columns[x + z * size] = column;
        heights[x + z * size] = column.Height;
        tallest = Math.Max(tallest, column.Height);
        lowest = Math.Min(lowest, column.Height);
      }
    }

    var chunk = new ChunkData { SurfaceMin = lowest };

    // Every section that lies wholly below the lowest surface is plain rock: no array, just the block.
    var rockSections = Math.Max(0, (lowest - 1) / ChunkData.SectionHeight);

    for (int section = 0; section < rockSections; section++)
    {
      var base0 = section * ChunkData.SectionHeight;

      chunk.FillSection(section, Underground.RockAt(base0 + ChunkData.SectionHeight / 2));
    }

    // Where the caverns turn into the underworld the rock is a mix: hellrock takes over gradually, in patches, over a band of layers.
    var bandLow = Underground.HellTop - Underground.BlendBand;
    var bandHigh = Underground.HellTop + Underground.BlendBand;

    for (int y = bandLow; y < bandHigh && y < rockSections * ChunkData.SectionHeight; y++)
    {
      for (int z = 0; z < size; z++)
      {
        for (int x = 0; x < size; x++)
        {
          var wobble = rockNoise.Sample((x0 + x) * 0.06f, y * 0.09f, (z0 + z) * 0.06f) / 0.7f * 0.45f;

          chunk.Set(x, y, z, Underground.DepthBlend(y) + wobble > 0.5f ? BlockIds.Hellrock : BlockIds.Stone);
        }
      }
    }

    // Above that, each column in turn.
    var firstLayer = rockSections * ChunkData.SectionHeight;

    for (int z = 0; z < size; z++)
    {
      for (int x = 0; x < size; x++)
      {
        var column = columns[x + z * size];
        var h = column.Height;
        var biome = Biomes.Get(column.SurfaceBiome);
        var fillerFloor = h - 1 - biome.FillerDepth;

        for (int y = firstLayer; y < h - 1; y++)
        {
          chunk.Set(x, y, z, y >= fillerFloor ? biome.Filler : Underground.RockAt(y));
        }

        chunk.Set(x, h - 1, z, biome.SurfaceAt(h - (int)p.BaseHeight));
      }
    }

    caves.Carve(chunk, x0, z0, heights, tallest);

    // Trees rooted in this chunk or close enough to reach into it, written only where they fall inside. Wood always wins
    // and leaves only fill air, so the result does not depend on the order neighbouring chunks visit overlapping trees.
    trees.ForTreesNear(x0, z0, x0 + size - 1, z0 + size - 1, tree => PlaceTree(chunk, tree, x0, z0));
    trees.ForGiantsNear(x0, z0, x0 + size - 1, z0 + size - 1, tree => PlaceTree(chunk, tree, x0, z0));
    logs.ForLogsNear(x0, z0, x0 + size - 1, z0 + size - 1, log => PlaceLog(chunk, log, x0, z0));

    Features.OreFeature.Place(chunk, p.Seed, cx, cz, tallest - 1);

    PlacePlants(chunk, x0, z0, columns);

    chunk.Compact();

    return chunk;
  }

  /// <summary>Out of 256: how many open grass tops carry a blade of short grass.</summary>
  const int PlantShare = 90;

  /// <summary>Short grass on about a third of the grass tops that are still open after the caves and trees, chosen by a hash of (seed, column).</summary>
  void PlacePlants(ChunkData chunk, int x0, int z0, ColumnInfo[] columns)
  {
    const int size = VoxelMath.ChunkSize;

    for (int z = 0; z < size; z++)
    {
      for (int x = 0; x < size; x++)
      {
        var h = columns[x + z * size].Height;

        if (h >= VoxelMath.ChunkHeight || chunk.Get(x, h - 1, z) != BlockIds.Grass || chunk.Get(x, h, z) != BlockIds.Air)
        {
          continue;
        }

        var hash = (uint)(x0 + x) * 73856093u ^ (uint)(z0 + z) * 19349663u ^ (uint)p.Seed * 83492791u ^ 0x9e3779b9u;

        hash ^= hash >> 15; hash *= 0x2c1b3c6du; hash ^= hash >> 12; hash *= 0x297a2d39u; hash ^= hash >> 15;

        if (Biomes.Get(columns[x + z * size].SurfaceBiome).TallGrass)
        {
          // Tall grass nearly everywhere in the grove, with a little short grass among it.
          if ((hash >> 24) < 205)
          {
            chunk.Set(x, h, z, (hash >> 16 & 0xff) < 40 ? BlockIds.ShortGrass : BlockIds.TallGrass);
          }
        }
        else if ((hash >> 24) < PlantShare)
        {
          chunk.Set(x, h, z, BlockIds.ShortGrass);
        }
      }
    }
  }

  /// <summary>A dead log and its stump, written only where they fall inside the chunk; wood replaces air and plants, never ground.</summary>
  static void PlaceLog(ChunkData chunk, FallenLog log, int x0, int z0)
  {
    const int size = VoxelMath.ChunkSize;
    var minX = Math.Max(x0, log.X - log.Reach);
    var maxX = Math.Min(x0 + size - 1, log.X + log.Reach);
    var minZ = Math.Max(z0, log.Z - log.Reach);
    var maxZ = Math.Min(z0 + size - 1, log.Z + log.Reach);

    for (int wz = minZ; wz <= maxZ; wz++)
    {
      for (int wx = minX; wx <= maxX; wx++)
      {
        for (int y = log.BaseY; y < log.BaseY + Math.Max(log.StumpHeight, log.Thickness); y++)
        {
          if (log.IsWood(wx, y, wz) && chunk.Get(wx - x0, y, wz - z0) is BlockIds.Air)
          {
            chunk.Set(wx - x0, y, wz - z0, log.Block);
          }
        }
      }
    }
  }

  /// <summary>
  /// A giant: its grown structure (limbs, branches, roots, vines and the foliage on them, see <see cref="GiantStructure"/>), its
  /// gnarled trunk column by column, and its canopy discs only in their own layers (the rest of the height is air), which keeps a
  /// 200-voxel tree cheap. Wood always wins and leaves only fill air, like every tree.
  /// </summary>
  static void PlaceGiant(ChunkData chunk, Tree tree, int x0, int z0)
  {
    const int size = VoxelMath.ChunkSize;

    if (GiantStructure.For(tree).TryGetBucket(VoxelMath.FloorDiv(x0, size), VoxelMath.FloorDiv(z0, size), out var bucket) && bucket != null)
    {
      foreach (var packed in bucket.Wood)
      {
        chunk.Set(packed & 31, packed >> 10, (packed >> 5) & 31, BlockIds.Wood);
      }
    }

    var minX = Math.Max(x0, tree.X - tree.Reach);
    var maxX = Math.Min(x0 + size - 1, tree.X + tree.Reach);
    var minZ = Math.Max(z0, tree.Z - tree.Reach);
    var maxZ = Math.Min(z0 + size - 1, tree.Z + tree.Reach);
    var trunkReach = tree.Width * 1.15f + 6f;
    var woodTop = Math.Min(VoxelMath.ChunkHeight, tree.BaseY + tree.GiantTrunkEnd + 1);
    var canopyReach = tree.CanopyRadius * 1.25f + 2f;

    for (int wz = minZ; wz <= maxZ; wz++)
    {
      for (int wx = minX; wx <= maxX; wx++)
      {
        var dx = wx - tree.CentreX;
        var dz = wz - tree.CentreZ;
        var distance2 = dx * dx + dz * dz;

        if (distance2 > canopyReach * canopyReach)
        {
          continue;
        }

        if (distance2 <= trunkReach * trunkReach)
        {
          for (int y = Math.Max(0, tree.BaseY); y < woodTop; y++)
          {
            if (tree.IsTrunk(wx, y, wz))
            {
              chunk.Set(wx - x0, y, wz - z0, BlockIds.Wood);
            }
          }
        }

        var lump = tree.CanopyLump(wx, wz);

        for (int tier = 0; tier < tree.Tiers; tier++)
        {
          tree.TierBand(tier, out var low, out var high);

          for (int y = Math.Max(0, low); y <= Math.Min(VoxelMath.ChunkHeight - 1, high); y++)
          {
            if (tree.IsCanopyLeaf(wx, y, wz, lump) && chunk.Get(wx - x0, y, wz - z0) == BlockIds.Air)
            {
              chunk.Set(wx - x0, y, wz - z0, BlockIds.Leaves);
            }
          }
        }
      }
    }

    // The foliage and vines that hang from the branches fill only air, after everything solid is in.
    if (bucket != null)
    {
      foreach (var packed in bucket.Leaves)
      {
        var lx = packed & 31;
        var ly = packed >> 10;
        var lz = (packed >> 5) & 31;

        if (chunk.Get(lx, ly, lz) == BlockIds.Air)
        {
          chunk.Set(lx, ly, lz, BlockIds.Leaves);
        }
      }
    }
  }

  static void PlaceTree(ChunkData chunk, Tree tree, int x0, int z0)
  {
    if (tree.Kind == TreeKind.Giant)
    {
      PlaceGiant(chunk, tree, x0, z0);

      return;
    }

    const int size = VoxelMath.ChunkSize;
    var top = Math.Min(VoxelMath.ChunkHeight, tree.BaseY + tree.TrunkHeight + tree.CanopyRadius + 1);
    var minX = Math.Max(x0, tree.X - tree.Reach);
    var maxX = Math.Min(x0 + size - 1, tree.X + tree.Reach);
    var minZ = Math.Max(z0, tree.Z - tree.Reach);
    var maxZ = Math.Min(z0 + size - 1, tree.Z + tree.Reach);

    for (int wz = minZ; wz <= maxZ; wz++)
    {
      for (int wx = minX; wx <= maxX; wx++)
      {
        for (int y = Math.Max(0, tree.BaseY); y < top; y++)
        {
          if (tree.IsTrunk(wx, y, wz))
          {
            chunk.Set(wx - x0, y, wz - z0, tree.TrunkBlock);
          }
          else if (chunk.Get(wx - x0, y, wz - z0) == BlockIds.Air && tree.IsLeaf(wx, y, wz))
          {
            chunk.Set(wx - x0, y, wz - z0, tree.LeafBlock);
          }
        }
      }
    }
  }
}
