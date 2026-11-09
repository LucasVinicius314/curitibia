using System;
using System.Collections.Generic;
using Curitibia.World;
using Curitibia.World.Features;
using Xunit;

namespace Curitibia.Tests;

public class TreeVarietyTests
{
  [Fact]
  public void Each_biome_grows_its_own_kind_of_tree()
  {
    var g = new WorldGenerator(new WorldGenParams(42, 0.02f, 700f, 3));
    var feature = new TreeFeature(42, g.ColumnHeight, g.TreeDensityAt, g.TreeKindAt);
    var found = new HashSet<(BiomeId, TreeKind)>();

    foreach (var biome in new[] { BiomeId.Desert, BiomeId.Snow, BiomeId.Forest })
    {
      var (x, z) = BiomeTests.Find(g, biome);

      feature.ForTreesNear(x - 400, z - 400, x + 400, z + 400, tree => found.Add((g.Column(tree.X, tree.Z).SurfaceBiome, tree.Kind)));
    }

    Assert.Contains((BiomeId.Desert, TreeKind.Cactus), found);
    Assert.Contains((BiomeId.Snow, TreeKind.Pine), found);
    Assert.Contains((BiomeId.Forest, TreeKind.Oak), found);
    Assert.DoesNotContain((BiomeId.Desert, TreeKind.Oak), found);
  }

  [Fact]
  public void Cacti_come_thin_and_thick()
  {
    var g = new WorldGenerator(new WorldGenParams(42, 0.02f, 700f, 3));
    var feature = new TreeFeature(42, g.ColumnHeight, g.TreeDensityAt, g.TreeKindAt);
    var (x, z) = BiomeTests.Find(g, BiomeId.Desert);
    var widths = new HashSet<int>();

    feature.ForTreesNear(x - 600, z - 600, x + 600, z + 600, tree =>
    {
      if (tree.Kind == TreeKind.Cactus)
      {
        widths.Add(tree.Width);
      }
    });

    Assert.Contains(1, widths);
    Assert.Contains(2, widths);
  }

  [Fact]
  public void Every_kind_of_tree_comes_in_several_shapes_and_forests_mix_in_birch()
  {
    var g = new WorldGenerator(new WorldGenParams(42, 0.02f, 700f, 3));
    var feature = new TreeFeature(42, g.ColumnHeight, g.TreeDensityAt, g.TreeKindAt);
    var shapes = new Dictionary<TreeKind, HashSet<int>>();
    var sizes = new HashSet<(TreeKind, int, int)>();

    foreach (var biome in new[] { BiomeId.Desert, BiomeId.Snow, BiomeId.Forest, BiomeId.Hills })
    {
      var (x, z) = BiomeTests.Find(g, biome);

      feature.ForTreesNear(x - 500, z - 500, x + 500, z + 500, tree =>
      {
        if (!shapes.TryGetValue(tree.Kind, out var set))
        {
          shapes[tree.Kind] = set = new HashSet<int>();
        }

        set.Add(tree.Variant);
        sizes.Add((tree.Kind, tree.TrunkHeight, tree.CanopyRadius));
      });
    }

    foreach (var kind in new[] { TreeKind.Oak, TreeKind.Pine, TreeKind.Cactus })
    {
      Assert.True(shapes[kind].Count >= 4, $"{kind} has {shapes[kind].Count} shapes");
    }

    Assert.Contains(TreeKind.Birch, shapes.Keys);
    Assert.True(shapes[TreeKind.Birch].Count >= 2);
    Assert.True(sizes.Count > 30, $"only {sizes.Count} different sizes");
  }

  [Fact]
  public void Trunks_come_thin_thick_flared_and_bent()
  {
    var g = new WorldGenerator(new WorldGenParams(42, 0.02f, 700f, 3));
    var feature = new TreeFeature(42, g.ColumnHeight, g.TreeDensityAt, g.TreeKindAt);
    var widths = new System.Collections.Generic.Dictionary<TreeKind, HashSet<int>>();
    var leaning = 0;
    var upright = 0;

    foreach (var biome in new[] { BiomeId.Forest, BiomeId.Snow, BiomeId.Hills })
    {
      var (x, z) = BiomeTests.Find(g, biome);

      feature.ForTreesNear(x - 500, z - 500, x + 500, z + 500, tree =>
      {
        if (!widths.TryGetValue(tree.Kind, out var set))
        {
          widths[tree.Kind] = set = new HashSet<int>();
        }

        set.Add(tree.Width);

        if (tree.Lean != 0)
        {
          leaning++;

          // The trunk really bends: the columns at its foot are not the columns at its top.
          var foot = new HashSet<(int, int)>();
          var top = new HashSet<(int, int)>();

          for (int cx = tree.X - 2; cx <= tree.X + 4; cx++)
          {
            for (int cz = tree.Z - 2; cz <= tree.Z + 4; cz++)
            {
              if (tree.IsTrunk(cx, tree.BaseY, cz))
              {
                foot.Add((cx, cz));
              }

              if (tree.IsTrunk(cx, tree.BaseY + tree.TrunkHeight - 1, cz))
              {
                top.Add((cx, cz));
              }
            }
          }

          Assert.False(foot.SetEquals(top));
        }
        else
        {
          upright++;
        }
      });
    }

    Assert.Equal(new[] { 1, 2 }, new SortedSet<int>(widths[TreeKind.Oak]));
    Assert.Contains(1, widths[TreeKind.Birch]);
    Assert.True(widths[TreeKind.Pine].Count >= 2);
    Assert.True(leaning > 5 && upright > leaning, $"{leaning} leaning, {upright} upright");
  }

  [Fact]
  public void Every_tree_fits_the_reach_and_height_budget()
  {
    var g = new WorldGenerator(new WorldGenParams(7, 0.02f, 700f, 3));
    var feature = new TreeFeature(7, g.ColumnHeight, g.TreeDensityAt, g.TreeKindAt);

    feature.ForTreesNear(-3000, -3000, 3000, 3000, tree =>
    {
      Assert.InRange(tree.TopInColumn(tree.X, tree.Z) - tree.BaseY, 1, TreeFeature.MaxHeight);

      for (int x = tree.X - 12; x <= tree.X + 12; x++)
      {
        for (int z = tree.Z - 12; z <= tree.Z + 12; z++)
        {
          if (tree.TopInColumn(x, z) > 0)
          {
            Assert.InRange(Math.Abs(x - tree.X), 0, TreeFeature.MaxReach);
            Assert.InRange(Math.Abs(z - tree.Z), 0, TreeFeature.MaxReach);
          }
        }
      }
    });
  }
}
