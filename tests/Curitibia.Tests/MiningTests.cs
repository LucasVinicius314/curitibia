using System.Linq;
using Curitibia.Core;
using Curitibia.World;
using Curitibia.World.Features;
using Xunit;
using Xunit.Abstractions;

namespace Curitibia.Tests;

public class MiningTests
{
  readonly ITestOutputHelper output;

  public MiningTests(ITestOutputHelper output)
  {
    this.output = output;
  }

  /// <summary>Swings (hits) a tool of this power and damage needs to break a block, or -1 when it cannot.</summary>
  static int HitsToBreak(byte block, int power, float damage)
  {
    var world = new BlockDamage();

    for (int hits = 1; hits <= 1000; hits++)
    {
      var result = world.Hit(0, 0, 0, block, power, damage);

      if (result == MiningResult.Broken)
      {
        return hits;
      }

      if (result == MiningResult.TooHard)
      {
        return -1;
      }
    }

    return -1;
  }

  [Fact]
  public void A_copper_pickaxe_breaks_dirt_in_two_hits_like_terraria()
  {
    Assert.Equal(2, HitsToBreak(BlockIds.Dirt, 1, 10f)); // copper pickaxe: power 1, 10 damage a hit
    Assert.Equal(4, HitsToBreak(BlockIds.Stone, 1, 10f));
    Assert.Equal(5, HitsToBreak(BlockIds.CopperOre, 1, 10f));
    Assert.Equal(1, HitsToBreak(BlockIds.ShortGrass, 0, 4f));
    Assert.True(HitsToBreak(BlockIds.Dirt, 0, 4f) is >= 4 and <= 6, "by hand it takes a while");
    Assert.True(HitsToBreak(BlockIds.IronOre, 2, 14f) < HitsToBreak(BlockIds.IronOre, 2, 10f), "more damage, fewer hits");
  }

  [Fact]
  public void Every_hit_grows_the_cracks_and_the_last_one_breaks_the_block()
  {
    var world = new BlockDamage();
    var lastStage = 0;

    for (int hit = 1; hit < 4; hit++)
    {
      Assert.Equal(MiningResult.Mining, world.Hit(1, 2, 3, BlockIds.Stone, 1, 10f));

      var stage = world.Stage(1, 2, 3, 10);

      Assert.True(stage > lastStage, "each hit shows more cracks");
      lastStage = stage;
    }

    Assert.Equal(MiningResult.Broken, world.Hit(1, 2, 3, BlockIds.Stone, 1, 10f)); // the fourth
    Assert.Equal(0, world.Stage(1, 2, 3, 10)); // gone with the block
    Assert.Equal(0, world.Count);
  }

  [Fact]
  public void A_weak_tool_cannot_break_a_hard_block_at_all()
  {
    Assert.Equal(-1, HitsToBreak(BlockIds.IronOre, 1, 1000f)); // no amount of damage makes up for power
    Assert.Equal(-1, HitsToBreak(BlockIds.Magma, 4, 1000f));
    Assert.True(HitsToBreak(BlockIds.IronOre, 2, 10f) > 0);
    Assert.True(HitsToBreak(BlockIds.PlatinumOre, 4, 24f) > 0);
    Assert.Equal(-1, HitsToBreak(BlockIds.GoldOre, 2, 24f));
  }

  [Fact]
  public void The_damage_belongs_to_the_block_so_everyones_hits_add_up_and_it_heals_after_ten_seconds()
  {
    var world = new BlockDamage();
    var healed = new System.Collections.Generic.List<(int X, int Y, int Z)>();

    // Two blocks cracked, by whoever.
    world.Hit(0, 0, 0, BlockIds.Stone, 1, 10f);
    world.Hit(5, 5, 5, BlockIds.Dirt, 1, 10f);
    world.Hit(0, 0, 0, BlockIds.Stone, 1, 10f); // another player's hit on the same block adds to it
    Assert.True(world.Progress(0, 0, 0) > 0.45f);
    Assert.Equal(2, world.Count);

    world.Tick(9f, healed);
    Assert.Empty(healed);
    Assert.True(world.Progress(0, 0, 0) > 0.45f, "still cracked after nine seconds");

    world.Hit(5, 5, 5, BlockIds.Dirt, 1, 0.1f); // a hit refreshes the clock of that block only
    world.Tick(1.5f, healed);
    Assert.Equal(new[] { (0, 0, 0) }, healed.ToArray()); // the untouched one healed completely
    Assert.Equal(0f, world.Progress(0, 0, 0));
    Assert.True(world.Progress(5, 5, 5) > 0f);

    // A different block in the same place (it was replaced): the old damage does not carry over.
    world.Hit(5, 5, 5, BlockIds.Stone, 1, 10f);
    Assert.True(world.Progress(5, 5, 5) < 0.3f);
  }

  [Fact]
  public void Harder_blocks_need_more_power_and_more_time_and_drops_make_sense()
  {
    byte[] ladder = { BlockIds.Dirt, BlockIds.Stone, BlockIds.CopperOre, BlockIds.IronOre, BlockIds.GoldOre, BlockIds.PlatinumOre };

    for (int i = 1; i < ladder.Length; i++)
    {
      Assert.True(BlockIds.Durability(ladder[i]) >= BlockIds.Durability(ladder[i - 1]), "deeper blocks are tougher");
      Assert.True(BlockIds.RequiredPower(ladder[i]) >= BlockIds.RequiredPower(ladder[i - 1]), "and need a better pickaxe");
    }

    Assert.Equal(BlockIds.Dirt, BlockIds.Drop(BlockIds.Grass));
    Assert.Equal(BlockIds.IronOre, BlockIds.Drop(BlockIds.IronOre));
    Assert.Equal(BlockIds.Air, BlockIds.Drop(BlockIds.ShortGrass));
    Assert.Equal(BlockIds.Air, BlockIds.Drop(BlockIds.Magma));

    for (byte id = 1; id <= BlockIds.MaxId; id++)
    {
      Assert.True(BlockIds.Durability(id) > 0f);
    }
  }

  static WorldGenerator Make(int seed)
  {
    return new WorldGenerator(new WorldGenParams(seed, 0.02f, VoxelMath.SeaLevel, 3));
  }

  [Fact]
  public void Every_ore_is_found_in_its_own_depths_and_the_deeper_the_rarer()
  {
    var g = Make(42);
    var counts = new long[BlockIds.MaxId + 1];
    var shallowest = new int[BlockIds.MaxId + 1];
    var deepest = new int[BlockIds.MaxId + 1];

    for (int i = 0; i < BlockIds.MaxId + 1; i++)
    {
      shallowest[i] = int.MinValue;
      deepest[i] = int.MaxValue;
    }

    for (int n = 0; n < 12; n++)
    {
      var chunk = g.GenerateChunk(n * 7 - 40, n * 5 - 30);

      for (int y = 0; y < VoxelMath.ChunkHeight; y += 1)
      {
        for (int z = 0; z < 32; z++)
        {
          for (int x = 0; x < 32; x++)
          {
            var block = chunk.Get(x, y, z);

            if (block >= BlockIds.CopperOre && block <= BlockIds.PlatinumOre)
            {
              counts[block]++;
              shallowest[block] = System.Math.Max(shallowest[block], y - VoxelMath.SeaLevel);
              deepest[block] = System.Math.Min(deepest[block], y - VoxelMath.SeaLevel);
            }
          }
        }
      }
    }

    foreach (var ore in OreFeature.All)
    {
      output.WriteLine($"{BlockIds.DisplayName(ore.Block)}: {counts[ore.Block]} voxels, Y {deepest[ore.Block]} to {shallowest[ore.Block]}");
      Assert.True(counts[ore.Block] > 50, $"{BlockIds.DisplayName(ore.Block)} exists");
      Assert.True(shallowest[ore.Block] <= ore.TopY + 4, "never above its range (a vein reaches a few voxels past its centre)");
      Assert.True(deepest[ore.Block] >= ore.BottomY - 4, "never below its range");
    }

    Assert.True(counts[BlockIds.CopperOre] > counts[BlockIds.PlatinumOre] * 2, "copper is common, platinum is rare");
  }

  [Fact]
  public void Ore_generation_is_the_same_every_time_and_only_replaces_rock()
  {
    var a = Make(7).GenerateChunk(3, -4);
    var b = Make(7).GenerateChunk(3, -4);
    var ores = 0;

    for (int y = 0; y < VoxelMath.ChunkHeight; y += 3)
    {
      for (int z = 0; z < 32; z += 3)
      {
        for (int x = 0; x < 32; x += 3)
        {
          Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
          ores += a.Get(x, y, z) >= BlockIds.CopperOre ? 1 : 0;
        }
      }
    }

    Assert.True(ores > 0);
  }
}

public class ChunkDataPaletteTests
{
  [Fact]
  public void A_section_with_a_few_kinds_of_block_is_packed_and_reads_back_the_same()
  {
    var data = new ChunkData();
    var expected = new byte[32, 16, 32];
    var rng = new System.Random(5);
    byte[] kinds = { BlockIds.Stone, BlockIds.Air, BlockIds.CopperOre, BlockIds.IronOre, BlockIds.Hellrock };

    for (int y = 0; y < 16; y++)
    {
      for (int z = 0; z < 32; z++)
      {
        for (int x = 0; x < 32; x++)
        {
          var id = kinds[rng.Next(kinds.Length)];

          expected[x, y, z] = id;
          data.Set(x, 320 + y, z, id);
        }
      }
    }

    var before = data.Bytes;

    data.Compact();
    Assert.True(data.Bytes < before, "five kinds fit a 4-bit palette, half the size of an array");

    for (int y = 0; y < 16; y++)
    {
      for (int z = 0; z < 32; z++)
      {
        for (int x = 0; x < 32; x++)
        {
          Assert.Equal(expected[x, y, z], data.Get(x, 320 + y, z));
        }
      }
    }

    // Editing a packed section works (it becomes a plain array again) and keeps everything else.
    data.Set(3, 325, 4, BlockIds.GoldOre);
    Assert.Equal(BlockIds.GoldOre, data.Get(3, 325, 4));
    Assert.Equal(expected[5, 6, 7], data.Get(5, 326, 7));
  }

  [Fact]
  public void A_section_with_three_or_four_kinds_uses_two_bits_per_voxel()
  {
    var data = new ChunkData();

    for (int z = 0; z < 32; z++)
    {
      for (int x = 0; x < 32; x++)
      {
        data.Set(x, 100, z, (x + z) % 3 == 0 ? BlockIds.Stone : (x + z) % 3 == 1 ? BlockIds.CopperOre : BlockIds.Dirt);
      }
    }

    data.Compact();
    Assert.True(data.Bytes <= 4096, "three kinds: 2 bits per voxel");
    Assert.Equal(BlockIds.CopperOre, data.Get(1, 100, 0));
    Assert.Equal(BlockIds.Dirt, data.Get(2, 100, 0));
    Assert.Equal(BlockIds.Air, data.Get(2, 101, 0));
  }
}
