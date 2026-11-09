using System;
using System.Collections.Generic;
using Curitibia.Core;
using Curitibia.World;
using Curitibia.World.Features;
using Xunit;
using Xunit.Abstractions;

namespace Curitibia.Tests;

public class BiomeTests
{
  readonly ITestOutputHelper output;

  public BiomeTests(ITestOutputHelper output)
  {
    this.output = output;
  }

  static WorldGenerator Make(int seed)
  {
    return new WorldGenerator(new WorldGenParams(seed, 0.02f, 700f, 3));
  }

  /// <summary>Biomes are about a kilometre wide, so look for one on a coarse grid around the origin.</summary>
  public static (int x, int z) Find(WorldGenerator g, BiomeId biome)
  {
    for (int z = -20000; z <= 20000; z += 100)
    {
      for (int x = -20000; x <= 20000; x += 100)
      {
        if (g.BiomeAt(x, z) == biome)
        {
          return (x, z);
        }
      }
    }

    throw new InvalidOperationException($"no {biome} found");
  }

  [Fact]
  public void Every_biome_appears_and_none_dominates()
  {
    var g = Make(42);
    var counts = new int[BiomeMap.Count];
    var total = 0;

    for (int z = -20000; z <= 20000; z += 200)
    {
      for (int x = -20000; x <= 20000; x += 200)
      {
        counts[(int)g.BiomeAt(x, z)]++;
        total++;
      }
    }

    for (int i = 0; i < counts.Length; i++)
    {
      var share = counts[i] / (float)total;
      output.WriteLine($"{Biomes.All[i].Name}: {share:P0}");
      Assert.InRange(share, 0.05f, 0.45f);
    }
  }

  [Fact]
  public void Biomes_are_wide_enough_to_walk_through_for_minutes()
  {
    var g = Make(42);
    var runs = new List<int>();

    // Length (in 100-voxel steps) of each stretch of the same biome along a line.
    var current = g.BiomeAt(-20000, 123);
    var length = 0;

    for (int x = -20000; x <= 20000; x += 100)
    {
      var b = g.BiomeAt(x, 123);

      if (b == current)
      {
        length++;
        continue;
      }

      runs.Add(length);
      current = b;
      length = 1;
    }

    runs.Sort();
    var median = runs[runs.Count / 2] * 100 * VoxelMath.BlockSize;
    output.WriteLine($"median stretch: {median} m over {runs.Count} stretches");

    // 5 m/s on foot: a median biome should take at least a minute and a half to cross along a line.
    Assert.True(median >= 450f);
  }

  [Fact]
  public void Little_of_the_world_is_in_tiny_patches()
  {
    // One grid cell is 100 x 100 voxels (50 x 50 m); count connected patches of one biome and the area of the small ones.
    const int n = 401;
    const int step = 100;

    foreach (var seed in new[] { 42, 7 })
    {
      var g = Make(seed);
      var grid = new BiomeId[n * n];

      for (int j = 0; j < n; j++)
      {
        for (int i = 0; i < n; i++)
        {
          grid[i + j * n] = g.BiomeAt((i - n / 2) * step, (j - n / 2) * step);
        }
      }

      var seen = new bool[n * n];
      var stack = new Stack<int>();
      var smallArea = 0;

      for (int start = 0; start < n * n; start++)
      {
        if (seen[start])
        {
          continue;
        }

        var count = 0;
        seen[start] = true;
        stack.Push(start);

        while (stack.Count > 0)
        {
          var c = stack.Pop();
          count++;

          foreach (var next in new[] { c % n + 1 < n ? c + 1 : -1, c % n > 0 ? c - 1 : -1, c + n < n * n ? c + n : -1, c - n >= 0 ? c - n : -1 })
          {
            if (next >= 0 && !seen[next] && grid[next] == grid[start])
            {
              seen[next] = true;
              stack.Push(next);
            }
          }
        }

        // under 100 cells = under a quarter of a square kilometre
        if (count < 100)
        {
          smallArea += count;
        }
      }

      var share = smallArea / (float)(n * n);
      output.WriteLine($"seed {seed}: {share:P1} of the world is in patches under 0.25 km2");
      Assert.True(share < 0.03f);
    }
  }

  [Fact]
  public void The_spawn_is_never_in_the_mountains()
  {
    for (int seed = 0; seed < 40; seed++)
    {
      var g = Make(seed);

      Assert.NotEqual(BiomeId.Mountains, g.BiomeAt(0, 0));
      Assert.InRange(g.ColumnHeight(0, 0), VoxelMath.SeaLevel - 20, VoxelMath.SeaLevel + 40);
    }
  }

  [Fact]
  public void Height_changes_gradually_between_biomes()
  {
    var g = Make(5);
    var worst = 0;

    for (int x = -8000; x < 8000; x++)
    {
      worst = Math.Max(worst, Math.Abs(g.ColumnHeight(x, 321) - g.ColumnHeight(x + 1, 321)));
    }

    output.WriteLine($"steepest step: {worst} voxels");
    Assert.InRange(worst, 0, 8);
  }

  [Fact]
  public void Mountains_rise_far_above_the_lowlands()
  {
    var g = Make(42);
    var best = new Dictionary<BiomeId, int>();

    for (int z = -20000; z <= 20000; z += 100)
    {
      for (int x = -20000; x <= 20000; x += 100)
      {
        var c = g.Column(x, z);
        best[c.Biome] = Math.Max(best.GetValueOrDefault(c.Biome), c.Height);
      }
    }

    foreach (var (id, h) in best)
    {
      output.WriteLine($"{id}: highest {h}");
    }

    Assert.True(best[BiomeId.Mountains] > best[BiomeId.Plains] + 40);
  }

  /// <summary>Heights and the average slope (voxels per voxel along x) of every biome, over a coarse grid of the world.</summary>
  static (Dictionary<BiomeId, List<int>> heights, Dictionary<BiomeId, double> slope) Survey(WorldGenerator g)
  {
    var heights = new Dictionary<BiomeId, List<int>>();
    var steps = new Dictionary<BiomeId, double>();

    for (int z = -20000; z <= 20000; z += 100)
    {
      for (int x = -20000; x <= 20000; x += 100)
      {
        var c = g.Column(x, z);

        if (!heights.TryGetValue(c.Biome, out var list))
        {
          heights[c.Biome] = list = new List<int>();
        }

        list.Add(c.Height);
        steps[c.Biome] = steps.GetValueOrDefault(c.Biome) + Math.Abs(g.ColumnHeight(x + 4, z) - c.Height) / 4.0;
      }
    }

    var slope = new Dictionary<BiomeId, double>();

    foreach (var (biome, list) in heights)
    {
      list.Sort();
      slope[biome] = steps[biome] / list.Count;
    }

    return (heights, slope);
  }

  [Fact]
  public void Mountains_stand_on_a_raised_base_not_just_peaks()
  {
    var (heights, _) = Survey(Make(42));
    var plains = heights[BiomeId.Plains];
    var mountains = heights[BiomeId.Mountains];
    var plainsMedian = plains[plains.Count / 2];
    var mountainMedian = mountains[mountains.Count / 2];
    var mountainLowTenth = mountains[mountains.Count / 10];

    output.WriteLine($"plains median {plainsMedian}, mountains median {mountainMedian}, mountains lowest tenth {mountainLowTenth}");

    // Typical mountain ground is high above typical plains, and even the low end of it is: a plateau you climb onto.
    Assert.True(mountainMedian >= plainsMedian + 35);
    Assert.True(mountainLowTenth >= plainsMedian + 15);
  }

  [Fact]
  public void Plains_are_the_flattest_green_biome()
  {
    var (_, slope) = Survey(Make(42));

    output.WriteLine($"mean slope: plains {slope[BiomeId.Plains]:F2}, forest {slope[BiomeId.Forest]:F2}, hills {slope[BiomeId.Hills]:F2}, mountains {slope[BiomeId.Mountains]:F2}");
    Assert.True(slope[BiomeId.Plains] < slope[BiomeId.Forest] * 0.85);
    Assert.True(slope[BiomeId.Plains] < slope[BiomeId.Hills]);
    Assert.True(slope[BiomeId.Mountains] > slope[BiomeId.Plains] * 1.5, "mountains still vary more");
  }

  [Fact]
  public void Borders_mix_the_two_surfaces_instead_of_meeting_in_a_line()
  {
    var g = Make(42);

    // Over the whole world only the columns near a border take the neighbour's surface.
    var mixed = 0;
    var total = 0;

    for (int z = -20000; z <= 20000; z += 100)
    {
      for (int x = -20000; x <= 20000; x += 100)
      {
        var c = g.Column(x, z);
        total++;

        if (c.SurfaceBiome != c.Biome)
        {
          mixed++;
        }
      }
    }

    var share = mixed / (double)total;
    output.WriteLine($"{share:P1} of the columns wear their neighbour's surface");
    Assert.InRange(share, 0.001, 0.06);

    // Walk across several borders one voxel at a time: the surface flips back and forth many times, where a clean line flips once.
    var crossings = 0;
    var best = 0;

    for (int x = -8000; x < 8000 && crossings < 12; x += 100)
    {
      if (g.Column(x, 321).Biome == g.Column(x + 100, 321).Biome)
      {
        continue;
      }

      crossings++;
      var flips = 0;
      var previous = g.Column(x - 300, 321).SurfaceBiome;

      for (int i = x - 300; i <= x + 400; i++)
      {
        var now = g.Column(i, 321).SurfaceBiome;

        if (now != previous)
        {
          flips++;
          previous = now;
        }
      }

      best = Math.Max(best, flips);
    }

    // ...and only close to a border: 150 voxels (75 m) from where the ruling biome changes, every column has its own surface.
    var strays = 0;
    var sampled = 0;

    for (int x = -8000; x < 8000; x += 7)
    {
      var c = g.Column(x, 321);
      var near = g.Column(x - 150, 321).Biome != c.Biome || g.Column(x + 150, 321).Biome != c.Biome;

      if (near)
      {
        continue;
      }

      sampled++;

      if (c.SurfaceBiome != c.Biome)
      {
        strays++;
      }
    }

    output.WriteLine($"{strays} of {sampled} columns away from a border wear a neighbour's surface");
    Assert.True(strays <= sampled / 200, $"{strays} stray columns: the mix is too wide");

    output.WriteLine($"{crossings} borders crossed, the most mixed flipped {best} times");
    Assert.True(crossings >= 3);
    Assert.True(best >= 8, $"only {best} flips: it is still a line");
  }

  [Fact]
  public void Each_biome_builds_its_own_surface()
  {
    var g = Make(42);
    var expected = new (BiomeId, byte)[]
    {
      (BiomeId.Desert, BlockIds.Sand),
      (BiomeId.Snow, BlockIds.Snow),
      (BiomeId.Plains, BlockIds.Grass),
      (BiomeId.Mountains, BlockIds.Stone),
    };

    foreach (var (biome, block) in expected)
    {
      var (x, z) = Find(g, biome);

      // Find lands anywhere in the biome, maybe on a border where the surface mixes: step on until a column has its own surface.
      while (g.Column(x, z).SurfaceBiome != biome)
      {
        x += 7;
      }

      VoxelMath.WorldToChunk(x, z, out var cx, out var cz, out var lx, out var lz);

      var blocks = g.GenerateChunk(cx, cz);
      var c = g.Column(x, z);

      Assert.Equal(biome, c.SurfaceBiome);
      Assert.Equal(biome == BiomeId.Mountains && c.Height - 700 >= Biomes.Get(biome).SnowLine ? BlockIds.Snow : block, blocks.Get(lx, c.Height - 1, lz));
    }

    Assert.Equal(TreeKind.Cactus, Biomes.Get(BiomeId.Desert).Tree);
  }
}
