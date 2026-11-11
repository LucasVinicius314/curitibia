using System;
using Curitibia.Core;
using Curitibia.World;
using Curitibia.World.Features;
using Xunit;
using Xunit.Abstractions;

namespace Curitibia.Tests;

public class GroveTests
{
  readonly ITestOutputHelper output;

  public GroveTests(ITestOutputHelper output)
  {
    this.output = output;
  }

  static WorldGenerator Make(int seed)
  {
    return new WorldGenerator(new WorldGenParams(seed, 0.02f, VoxelMath.SeaLevel, 3));
  }

  /// <summary>A spot well inside the grove: it and eight points 300 voxels around it are all grove.</summary>
  static (int x, int z) Heart(WorldGenerator g)
  {
    for (int z = -20000; z <= 20000; z += 100)
    {
      for (int x = -20000; x <= 20000; x += 100)
      {
        var all = true;

        for (int i = 0; i < 9 && all; i++)
        {
          all = g.BiomeAt(x + (i % 3 - 1) * 300, z + (i / 3 - 1) * 300) == BiomeId.Grove;
        }

        if (all)
        {
          return (x, z);
        }
      }
    }

    throw new InvalidOperationException("no heart of the grove found");
  }

  [Fact]
  public void The_grove_exists_and_is_wide()
  {
    var g = Make(42);
    var (x, z) = BiomeTests.Find(g, BiomeId.Grove);
    var inside = 0;

    for (int dz = -40; dz <= 40; dz++)
    {
      for (int dx = -40; dx <= 40; dx++)
      {
        inside += g.BiomeAt(x + dx * 25, z + dz * 25) == BiomeId.Grove ? 1 : 0;
      }
    }

    output.WriteLine($"grove found at {x},{z}; {100.0 * inside / 6561:F0}% of the 1 km square around it is grove");
    Assert.True(inside > 600, "a biome you can walk in for a while");
    Assert.Equal("Titan Grove", Biomes.Get(BiomeId.Grove).Name);
  }

  [Fact]
  public void Giant_trees_are_huge_rooted_in_the_ground_and_only_grow_in_the_grove()
  {
    var g = Make(42);
    var (gx, gz) = Heart(g);
    var feature = new TreeFeature(42, g.ColumnHeight, g.TreeDensityAt, g.TreeKindAt, null, g.GiantDensityAt);
    var giants = 0;
    var tallest = 0;
    var widest = 0;

    for (int cz = -30; cz <= 30; cz++)
    {
      for (int cx = -30; cx <= 30; cx++)
      {
        if (!feature.TryGetGiant(gx / TreeFeature.GiantCell + cx, gz / TreeFeature.GiantCell + cz, out var tree))
        {
          continue;
        }

        giants++;
        tallest = Math.Max(tallest, tree.TrunkHeight);
        widest = Math.Max(widest, tree.CanopyRadius * 2);

        Assert.Equal(TreeKind.Giant, tree.Kind);
        Assert.True(tree.TrunkHeight >= 160, "eighty metres at the least");
        Assert.True(tree.CanopyRadius >= 36, "and a very wide canopy");
        Assert.True(Biomes.Get(g.Column(tree.X, tree.Z).SurfaceBiome).GiantDensity > 0f, "only where the biome allows");
        Assert.True(tree.BaseY < g.ColumnHeight(tree.X, tree.Z), "the root is inside the ground");
        Assert.True(tree.IsTrunk(tree.X + tree.Width / 2, tree.BaseY + 10, tree.Z + tree.Width / 2), "a solid trunk at the middle");
        Assert.False(tree.IsTrunk(tree.X + tree.Width / 2, tree.BaseY + tree.TrunkHeight + 2, tree.Z + tree.Width / 2), "it ends at the top");
        Assert.True(tree.Reach <= TreeFeature.GiantReach, "within the reach chunks look for");
      }
    }

    output.WriteLine($"{giants} giants, up to {tallest} voxels tall ({tallest / 2} m), canopies up to {widest} voxels across");
    Assert.True(giants > 20);

    // Nowhere else.
    foreach (var biome in new[] { BiomeId.Plains, BiomeId.Desert, BiomeId.Snow })
    {
      var (x, z) = BiomeTests.Find(g, biome);

      for (int i = 0; i < 20; i++)
      {
        Assert.Equal(0f, g.GiantDensityAt(x + i * 7, z + i * 5));
      }
    }
  }

  [Fact]
  public void Every_giant_is_grown_differently_with_limbs_roots_leafy_branches_and_vines()
  {
    var g = Make(42);
    var (gx, gz) = Heart(g);
    var feature = new TreeFeature(42, g.ColumnHeight, g.TreeDensityAt, g.TreeKindAt, null, g.GiantDensityAt);
    var limbCounts = new System.Collections.Generic.HashSet<int>();
    var lowCounts = new System.Collections.Generic.HashSet<int>();
    var rootCounts = new System.Collections.Generic.HashSet<int>();
    var longest = 0f;
    var checkedTrees = 0;
    var withoutLowBranches = 0;

    for (int cz = -6; cz <= 6 && checkedTrees < 14; cz++)
    {
      for (int cx = -6; cx <= 6 && checkedTrees < 14; cx++)
      {
        if (!feature.TryGetGiant(gx / TreeFeature.GiantCell + cx, gz / TreeFeature.GiantCell + cz, out var tree))
        {
          continue;
        }

        checkedTrees++;

        var structure = GiantStructure.For(tree);
        var reaches = structure.LimbReaches;

        limbCounts.Add(reaches.Count);
        lowCounts.Add(structure.LowBranches);
        rootCounts.Add(structure.Roots);
        withoutLowBranches += structure.LowBranches == 0 ? 1 : 0;

        Assert.True(reaches.Count >= 4, "a handful of great limbs at the least");
        Assert.True(reaches.FindAll(r => r >= tree.CanopyRadius * 1.5f).Count >= 2, "and at least two long ones, one and a half times the width of the leaves");
        Assert.True(reaches.Exists(r => r < tree.CanopyRadius * 1.25f), "and shorter ones as well");

        // The longest limb really is that long: wood far out from the trunk, past the edge of the leaves.
        var farthest = 0f;

        foreach (var (chunkX, chunkZ) in ChunksAround(tree))
        {
          if (!structure.TryGetBucket(chunkX, chunkZ, out var bucket) || bucket == null)
          {
            continue;
          }

          foreach (var packed in bucket.Wood)
          {
            var dx = chunkX * 32 + (packed & 31) - tree.CentreX;
            var dz = chunkZ * 32 + ((packed >> 5) & 31) - tree.CentreZ;

            farthest = Math.Max(farthest, MathF.Sqrt(dx * dx + dz * dz));
          }
        }

        longest = Math.Max(longest, farthest);
        Assert.True(farthest >= tree.CanopyRadius * 1.3f, $"wood reaches {farthest:F0} voxels out");
        Assert.True(farthest <= tree.Reach, "and never past the reach the chunks look for");
        Assert.True(structure.Roots >= 5, "buttress roots");
        Assert.True(structure.WoodVoxels > 2000, "a lot of wood");
        Assert.True(structure.HangingLeafVoxels > 150, "leaves and vines hang below the canopy, on the branches");
        Assert.True(structure.Vines > 20);
      }
    }

    output.WriteLine($"{checkedTrees} giants: {limbCounts.Count} different limb counts, {lowCounts.Count} different counts of lower branches, {rootCounts.Count} of roots, longest reach {longest:F0} voxels, {withoutLowBranches} with no lower branches");
    Assert.True(checkedTrees >= 8);
    Assert.True(limbCounts.Count >= 3, "not all the same number of limbs");
    Assert.True(lowCounts.Count >= 3, "not all the same number of lower branches");
    Assert.True(rootCounts.Count >= 2);
  }

  static System.Collections.Generic.IEnumerable<(int, int)> ChunksAround(Tree tree)
  {
    var c0x = (int)Math.Floor((tree.X - tree.Reach) / 32.0);
    var c1x = (int)Math.Floor((tree.X + tree.Reach) / 32.0);
    var c0z = (int)Math.Floor((tree.Z - tree.Reach) / 32.0);
    var c1z = (int)Math.Floor((tree.Z + tree.Reach) / 32.0);

    for (int cz = c0z; cz <= c1z; cz++)
    {
      for (int cx = c0x; cx <= c1x; cx++)
      {
        yield return (cx, cz);
      }
    }
  }

  [Fact]
  public void The_structure_of_a_giant_is_the_same_every_time()
  {
    var g = Make(42);
    var (gx, gz) = Heart(g);
    var feature = new TreeFeature(42, g.ColumnHeight, g.TreeDensityAt, g.TreeKindAt, null, g.GiantDensityAt);

    for (int cx = -4; cx <= 4; cx++)
    {
      if (!feature.TryGetGiant(gx / TreeFeature.GiantCell + cx, gz / TreeFeature.GiantCell, out var tree))
      {
        continue;
      }

      var a = GiantStructure.For(tree);
      var b = GiantStructure.For(tree with { Variant = tree.Variant }); // the same tree

      Assert.Equal(a.WoodVoxels, b.WoodVoxels);
      Assert.Equal(a.LeafVoxels, b.LeafVoxels);
      Assert.Equal(a.LimbReaches, b.LimbReaches);

      return;
    }

    Assert.Fail("no giant found");
  }

  [Fact]
  public void Generated_giants_stand_in_the_chunks_with_a_trunk_and_a_canopy()
  {
    var g = Make(42);
    var (gx, gz) = Heart(g);
    var cx0 = (int)Math.Floor(gx / 32.0);
    var cz0 = (int)Math.Floor(gz / 32.0);
    long wood = 0, leaves = 0;
    var tallColumns = 0;

    for (int dz = 0; dz < 8; dz++)
    {
      for (int dx = 0; dx < 8; dx++)
      {
        var chunk = g.GenerateChunk(cx0 + dx, cz0 + dz);

        for (int z = 0; z < 32; z++)
        {
          for (int x = 0; x < 32; x++)
          {
            var ground = g.ColumnHeight(32 * (cx0 + dx) + x, 32 * (cz0 + dz) + z);
            var top = chunk.TopLayer(x, z);

            tallColumns += top - ground > 100 ? 1 : 0;
          }
        }

        for (int y = VoxelMath.SeaLevel; y < VoxelMath.SeaLevel + 200; y += 4)
        {
          for (int z = 0; z < 32; z += 2)
          {
            for (int x = 0; x < 32; x += 2)
            {
              var block = chunk.Get(x, y, z);

              wood += block == BlockIds.Wood ? 1 : 0;
              leaves += block == BlockIds.Leaves ? 1 : 0;
            }
          }
        }
      }
    }

    output.WriteLine($"{tallColumns} columns with something over 100 voxels (50 m) above the ground, {wood} wood and {leaves} leaves sampled");
    Assert.True(tallColumns > 100, "huge trees stand here");
    Assert.True(leaves > wood, "with canopies");
  }

  [Fact]
  public void The_grove_is_cut_by_deep_narrow_trenches()
  {
    var g = Make(42);
    var (gx, gz) = Heart(g);
    var deepest = 0;
    var inTrench = 0;
    var total = 0;

    // Walk along lines through the grove and compare each column with the ground a few voxels either side.
    for (int line = 0; line < 40; line++)
    {
      for (int i = -600; i < 600; i++)
      {
        var x = gx + i;
        var z = gz + line * 17;
        var here = g.ColumnHeight(x, z);
        var rim = Math.Max(g.ColumnHeight(x - 14, z), g.ColumnHeight(x + 14, z));

        total++;
        deepest = Math.Max(deepest, rim - here);
        inTrench += rim - here > 12 ? 1 : 0;
      }
    }

    output.WriteLine($"deepest trench {deepest} voxels ({deepest / 2.0:F1} m), {100.0 * inTrench / total:F1}% of the columns are in one");
    Assert.True(deepest >= 20, "deep");
    Assert.InRange(100.0 * inTrench / total, 1.0, 25.0); // narrow: a minority of the ground
  }

  [Fact]
  public void The_grove_has_tall_grass_and_dead_logs_lying_on_the_ground()
  {
    var g = Make(42);
    var (gx, gz) = Heart(g);
    var cx0 = (int)Math.Floor(gx / 32.0);
    var cz0 = (int)Math.Floor(gz / 32.0);
    long tall = 0, shortGrass = 0, logs = 0;

    for (int dz = 0; dz < 6; dz++)
    {
      for (int dx = 0; dx < 6; dx++)
      {
        var chunk = g.GenerateChunk(cx0 + dx, cz0 + dz);

        for (int z = 0; z < 32; z++)
        {
          for (int x = 0; x < 32; x++)
          {
            var ground = g.ColumnHeight(32 * (cx0 + dx) + x, 32 * (cz0 + dz) + z);
            var above = chunk.Get(x, ground, z);

            if (g.Column(32 * (cx0 + dx) + x, 32 * (cz0 + dz) + z).SurfaceBiome != BiomeId.Grove)
            {
              continue;
            }

            tall += above == BlockIds.TallGrass ? 1 : 0;
            shortGrass += above == BlockIds.ShortGrass ? 1 : 0;

            // A log: wood right on the ground whose neighbours along a row are wood too.
            logs += above is BlockIds.Wood or BlockIds.BirchWood && x > 0 && x < 31 && chunk.Get(x - 1, ground, z) == above && chunk.Get(x + 1, ground, z) == above ? 1 : 0;
          }
        }
      }
    }

    output.WriteLine($"{tall} tall grass, {shortGrass} short grass, {logs} log voxels lying on the ground (in 36 chunks)");
    Assert.True(tall > shortGrass * 3, "mostly tall grass");
    Assert.True(tall > 3000);
    Assert.True(logs > 30, "dead logs lie about");
  }

  [Fact]
  public void A_dead_log_rests_on_level_ground_with_its_stump()
  {
    var g = Make(42);
    var (gx, gz) = Heart(g);
    var feature = new LogFeature(42, g.ColumnHeight, g.LogDensityAt);
    var found = 0;

    for (int cz = -25; cz <= 25; cz++)
    {
      for (int cx = -25; cx <= 25; cx++)
      {
        if (!feature.TryGetLog(gx / LogFeature.CellSize + cx, gz / LogFeature.CellSize + cz, out var log))
        {
          continue;
        }

        found++;
        Assert.InRange(log.Length, 8, 16);

        for (int i = 0; i < log.Length; i++)
        {
          var wx = log.AlongX ? log.X + i : log.X;
          var wz = log.AlongX ? log.Z : log.Z + i;

          Assert.True(log.IsWood(wx, log.BaseY, wz), "the body lies on the ground");
          Assert.True(g.ColumnHeight(wx, wz) <= log.BaseY && log.BaseY - g.ColumnHeight(wx, wz) <= 2, "no more than two voxels of air under it");

          if (i > log.Thickness)
          {
            Assert.False(log.IsWood(wx, log.BaseY + log.Thickness, wz), "and it is not stacked higher (the stump aside)");
          }
        }

        Assert.True(log.IsWood(log.X, log.BaseY + 1, log.Z), "the stump stands at the start");
        Assert.True(log.Reach <= LogFeature.MaxReach);
      }
    }

    output.WriteLine($"{found} logs near the grove");
    Assert.True(found > 20);
  }
}

public class BiomeTintAndTeleportTests
{
  static WorldGenerator Make(int seed)
  {
    return new WorldGenerator(new WorldGenParams(seed, 0.02f, VoxelMath.SeaLevel, 3));
  }

  [Fact]
  public void The_closest_place_of_every_biome_can_be_found_and_is_really_that_biome()
  {
    var g = Make(42);

    foreach (var biome in Biomes.All)
    {
      Assert.True(g.FindNearestBiome(0, 0, biome.Id, out var x, out var z), $"{biome.Name} is somewhere");
      Assert.Equal(biome.Id, g.BiomeAt(x, z));

      // Asking from inside it finds a spot right there.
      Assert.True(g.FindNearestBiome(x, z, biome.Id, out var x2, out var z2));
      Assert.True(Math.Abs(x2 - x) <= 150 && Math.Abs(z2 - z) <= 150, "already there");
    }
  }

  [Fact]
  public void Biome_tints_make_the_grove_darker_green_and_blend_smoothly()
  {
    var g = Make(42);
    var gx = 0;
    var gz = 0;

    // The heart of the grove: it and eight points 400 voxels around it are all grove.
    for (int z = -20000; z <= 20000 && gx == 0; z += 100)
    {
      for (int x = -20000; x <= 20000 && gx == 0; x += 100)
      {
        var all = true;

        for (int i = 0; i < 9 && all; i++)
        {
          all = g.BiomeAt(x + (i % 3 - 1) * 400, z + (i / 3 - 1) * 400) == BiomeId.Grove;
        }

        if (all)
        {
          gx = x;
          gz = z;
        }
      }
    }

    var (px, pz) = BiomeTests.Find(g, BiomeId.Plains);
    var groveGrass = new byte[32 * 32 * 3];
    var groveFoliage = new byte[groveGrass.Length];
    var plainsGrass = new byte[groveGrass.Length];
    var plainsFoliage = new byte[groveGrass.Length];

    g.ColumnTints(gx / 32, gz / 32, groveGrass, groveFoliage);
    g.ColumnTints(px / 32, pz / 32, plainsGrass, plainsFoliage);

    int grove = groveGrass[0] + groveGrass[1] + groveGrass[2];
    int plains = plainsGrass[0] + plainsGrass[1] + plainsGrass[2];

    Assert.True(grove < plains * 0.8, $"the grove's grass is darker ({grove} against {plains})");
    Assert.True(groveGrass[1] > groveGrass[0] && groveGrass[1] > groveGrass[2], "and still green");
    Assert.True(groveFoliage[0] < plainsFoliage[0], "the leaves too");

    // Across a long line the tint never jumps: it changes by a few steps per column at the most.
    var tint = new byte[groveGrass.Length];
    var tint2 = new byte[groveGrass.Length];
    var worst = 0;
    var last = -1;

    for (int chunk = -150; chunk < 150; chunk++)
    {
      g.ColumnTints(chunk, 7, tint, tint2);

      for (int x = 0; x < 32; x++)
      {
        var value = tint[x * 3 + 1];

        if (last >= 0)
        {
          worst = Math.Max(worst, Math.Abs(value - last));
        }

        last = value;
      }
    }

    Assert.True(worst <= 8, $"the colour blends across borders (largest step {worst}/255)");
  }

  [Fact]
  public void The_mesher_colours_grass_and_leaves_with_the_tint_and_leaves_the_rest_white()
  {
    var blocks = new ChunkData();

    blocks.Set(2, 10, 3, BlockIds.Grass);
    blocks.Set(5, 10, 5, BlockIds.Stone);
    blocks.Set(8, 10, 8, BlockIds.Leaves);

    var tint = new byte[32 * 32 * 3];

    for (int i = 0; i < tint.Length; i += 3)
    {
      tint[i] = 100;
      tint[i + 1] = 200;
      tint[i + 2] = 52; // (the mesher rounds tints to steps of four, so neighbouring grass columns merge)
    }

    var neighbours = new ChunkData?[4];
    var plain = ChunkMesher.Build(blocks, neighbours, BlockIds.TileByBlock, BlockIds.TileCount, 0f);
    var tinted = ChunkMesher.Build(blocks, neighbours, BlockIds.TileByBlock, BlockIds.TileCount, 0f, grassTint: tint, foliageTint: tint);

    Assert.Equal(plain.VertexCount * 4, plain.Colors.Length); // always present: the tile in the alpha channel

    for (int v = 0; v < plain.VertexCount; v++)
    {
      Assert.Equal(255, plain.Colors[v * 4]); // and white without tints
    }

    Assert.Equal(tinted.VertexCount * 4, tinted.Colors.Length);

    var coloured = 0;
    var white = 0;

    for (int v = 0; v < tinted.VertexCount; v++)
    {
      var white3 = tinted.Colors[v * 4] == 255 && tinted.Colors[v * 4 + 1] == 255 && tinted.Colors[v * 4 + 2] == 255;

      if (white3)
      {
        white++;
      }
      else
      {
        Assert.Equal(100, tinted.Colors[v * 4]);
        Assert.Equal(200, tinted.Colors[v * 4 + 1]);
        Assert.Equal(52, tinted.Colors[v * 4 + 2]);
        coloured++;
      }
    }

    Assert.Equal(48, coloured); // the grass block and the leaves block: six faces of four vertices each
    Assert.Equal(24, white); // the stone
  }
}

public class VisualMergeTests
{
  [Fact]
  public void A_flat_field_of_one_block_is_one_quad_and_the_tile_repeats_across_it()
  {
    var blocks = new ChunkData();

    for (int x = 5; x < 13; x++)
    {
      for (int z = 5; z < 13; z++)
      {
        blocks.Set(x, 10, z, BlockIds.Stone);
      }
    }

    var mesh = ChunkMesher.Build(blocks, new ChunkData?[4], BlockIds.TileByBlock, BlockIds.TileCount, 0f);

    // Top, bottom and four sides: six quads, not 64 + 64 + 32 faces.
    Assert.Equal(6, mesh.VertexCount / 4);

    // The top quad counts 8 by 8 tiles in its texture coordinates (the shader repeats the tile).
    var found = false;

    for (int v = 0; v < mesh.VertexCount && !found; v += 4)
    {
      if (mesh.Normals[v * 3 + 1] > 0.5f)
      {
        var maxU = 0f;
        var maxV = 0f;

        for (int k = 0; k < 4; k++)
        {
          maxU = Math.Max(maxU, mesh.Uvs[(v + k) * 2]);
          maxV = Math.Max(maxV, mesh.Uvs[(v + k) * 2 + 1]);
        }

        Assert.Equal(8f, maxU);
        Assert.Equal(8f, maxV);
        Assert.Equal(BlockIds.TileByBlock[BlockIds.Stone], mesh.Colors[v * 4 + 3]); // the tile index rides in the alpha
        found = true;
      }
    }

    Assert.True(found);
  }

  [Fact]
  public void Faces_with_a_different_tile_or_tint_are_not_merged()
  {
    var blocks = new ChunkData();

    for (int x = 5; x < 11; x++)
    {
      blocks.Set(x, 10, 7, x < 8 ? BlockIds.Stone : BlockIds.Dirt); // two kinds of block side by side
    }

    var mixed = ChunkMesher.Build(blocks, new ChunkData?[4], BlockIds.TileByBlock, BlockIds.TileCount, 0f);

    // Each face direction has two runs (three stones, three dirt), except the two ends: top, bottom, front, back = 8, plus 2 ends.
    Assert.Equal(10, mixed.VertexCount / 4);

    // The same row of grass with a tint that changes along it (steps of 8, past the rounding): two runs again.
    var grass = new ChunkData();

    for (int x = 5; x < 11; x++)
    {
      grass.Set(x, 10, 7, BlockIds.Grass);
    }

    var tint = new byte[32 * 32 * 3];

    for (int i = 0; i < 32; i++)
    {
      for (int c = 0; c < 3; c++)
      {
        tint[(i + 7 * 32) * 3 + c] = (byte)(i < 8 ? 100 : 140); // columns 5, 6, 7 one tint, 8, 9, 10 another
      }
    }

    var tinted = ChunkMesher.Build(grass, new ChunkData?[4], BlockIds.TileByBlock, BlockIds.TileCount, 0f, grassTint: tint, foliageTint: tint);

    Assert.Equal(10, tinted.VertexCount / 4);
  }
}
