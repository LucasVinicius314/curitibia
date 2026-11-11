using System;
using System.Linq;
using Curitibia.Core;
using Curitibia.World;
using Curitibia.World.Features;
using Xunit;

namespace Curitibia.Tests;

public class VoxelMathTests
{
  [Theory]
  [InlineData(0, 0, 0)]
  [InlineData(31, 0, 31)]
  [InlineData(32, 1, 0)]
  [InlineData(-1, -1, 31)]
  [InlineData(-32, -1, 0)]
  [InlineData(-33, -2, 31)]
  public void WorldToChunk_floors_negatives(int wx, int expectedChunk, int expectedLocal)
  {
    VoxelMath.WorldToChunk(wx, wx, out var cx, out var cz, out var lx, out var lz);

    Assert.Equal(expectedChunk, cx);
    Assert.Equal(expectedChunk, cz);
    Assert.Equal(expectedLocal, lx);
    Assert.Equal(expectedLocal, lz);
  }

  [Fact]
  public void FloorToInt_handles_negative_fractions()
  {
    Assert.Equal(-1, VoxelMath.FloorToInt(-0.01f));
    Assert.Equal(0, VoxelMath.FloorToInt(0.99f));
  }

  [Fact]
  public void World_and_voxel_coordinates_use_half_metre_blocks()
  {
    Assert.Equal(0, VoxelMath.WorldToVoxel(0.49f));
    Assert.Equal(1, VoxelMath.WorldToVoxel(0.5f));
    Assert.Equal(-1, VoxelMath.WorldToVoxel(-0.01f));
    Assert.Equal(1.25f, VoxelMath.VoxelCenter(2));
    Assert.Equal(1f, VoxelMath.VoxelToWorld(2));
  }

  [Fact]
  public void A_tower_fits_in_a_column()
  {
    Assert.True(VoxelMath.InWorldHeight(VoxelMath.ChunkHeight - 1));
    Assert.True(VoxelMath.ChunkHeight * VoxelMath.BlockSize >= 100f);
  }
}

public class VoxelRaycastTests
{
  [Fact]
  public void Hits_first_solid_voxel_and_reports_entry_face()
  {
    // A solid slab at y = 0.
    bool Solid(int x, int y, int z) => y == 0;

    var hit = VoxelRaycast.Cast(0.5f, 3.5f, 0.5f, 0, -1, 0, 10, Solid, out var hx, out var hy, out var hz, out var nx, out var ny, out var nz);

    Assert.True(hit);
    Assert.Equal((0, 0, 0), (hx, hy, hz));
    Assert.Equal((0, 1, 0), (nx, ny, nz));
  }

  [Fact]
  public void Misses_beyond_max_distance()
  {
    bool Solid(int x, int y, int z) => y == 0;

    Assert.False(VoxelRaycast.Cast(0.5f, 10.5f, 0.5f, 0, -1, 0, 5, Solid, out _, out _, out _, out _, out _, out _));
  }

  [Fact]
  public void Works_through_negative_coordinates()
  {
    bool Solid(int x, int y, int z) => x == -3;

    var hit = VoxelRaycast.Cast(0.5f, 0.5f, 0.5f, -1, 0, 0, 10, Solid, out var hx, out _, out _, out var nx, out _, out _);

    Assert.True(hit);
    Assert.Equal(-3, hx);
    Assert.Equal(1, nx);
  }
}

public class WorldGeneratorTests
{
  /// <summary>True when two chunks hold the same block everywhere.</summary>
  public static bool Same(ChunkData a, ChunkData b)
  {
    for (int y = 0; y < VoxelMath.ChunkHeight; y++)
    {
      for (int z = 0; z < VoxelMath.ChunkSize; z++)
      {
        for (int x = 0; x < VoxelMath.ChunkSize; x++)
        {
          if (a.Get(x, y, z) != b.Get(x, y, z))
          {
            return false;
          }
        }
      }
    }

    return true;
  }

  static WorldGenerator Make(int seed)
  {
    return new WorldGenerator(new WorldGenParams(seed, 0.04f, 700f, 2));
  }

  [Fact]
  public void Same_seed_gives_identical_chunks()
  {
    var a = Make(1234).GenerateChunk(-2, 3);
    var b = Make(1234).GenerateChunk(-2, 3);

    Assert.True(Same(a, b));
  }

  [Fact]
  public void Different_seeds_differ()
  {
    var a = Make(1).GenerateChunk(0, 0);
    var b = Make(2).GenerateChunk(0, 0);

    Assert.False(Same(a, b));
  }

  [Fact]
  public void Columns_are_continuous_across_chunk_borders()
  {
    var g = Make(99);

    // Heights are a function of world position, so neighbouring columns never jump arbitrarily.
    for (int wx = -70; wx < 70; wx++)
    {
      Assert.InRange(Math.Abs(g.ColumnHeight(wx, 5) - g.ColumnHeight(wx + 1, 5)), 0, 6);
    }
  }

  [Fact]
  public void Column_has_the_biome_surface_over_filler_over_stone()
  {
    var g = Make(7);
    var blocks = g.GenerateChunk(0, 0);
    var h = g.ColumnHeight(4, 9);
    var biome = Biomes.Get(g.Column(4, 9).SurfaceBiome);

    Assert.Equal(biome.SurfaceAt(h - 700), blocks.Get(4, h - 1, 9));
    Assert.Equal(BlockIds.Air, blocks.Get(4, h, 9));
    Assert.Equal(BlockIds.Hellrock, blocks.Get(4, 0, 9)); // the bottom of the world is the underworld's rock
  }
}

public class ChunkMesherTests
{
  [Fact]
  public void Short_grass_is_a_plant_block_without_collision_that_hides_no_face()
  {
    var blocks = new ChunkData();

    for (int x = 0; x < 16; x++)
    {
      for (int z = 0; z < 16; z++)
      {
        blocks.Set(x, 10, z, BlockIds.Grass);
      }
    }

    var neighbours = new ChunkData?[4];
    var plain = ChunkMesher.Build(blocks, neighbours, BlockIds.TileByBlock, BlockIds.TileCount, 0f);

    blocks.Set(3, 11, 3, BlockIds.ShortGrass);
    blocks.Set(4, 11, 3, BlockIds.ShortGrass); // two side by side: neither hides the other
    blocks.Set(8, 11, 8, BlockIds.Stone);

    var withPlants = ChunkMesher.Build(blocks, neighbours, BlockIds.TileByBlock, BlockIds.TileCount, 0f, chunkX: 2, chunkZ: -3);
    var again = ChunkMesher.Build(blocks, neighbours, BlockIds.TileByBlock, BlockIds.TileCount, 0f, chunkX: 2, chunkZ: -3);

    blocks.Set(8, 11, 8, BlockIds.Air);

    var plantsOnly = ChunkMesher.Build(blocks, neighbours, BlockIds.TileByBlock, BlockIds.TileCount, 0f, chunkX: 2, chunkZ: -3);

    Assert.Equal((plantsOnly.VertexCount - plain.VertexCount), 2 * 16); // 4 quads of 4 vertices per plant, the grass top stays whole
    Assert.Equal(plain.CollisionTriangles, plantsOnly.CollisionTriangles); // plants never collide
    Assert.Equal(withPlants.Positions, again.Positions);
    Assert.False(BlockIds.IsSolid(BlockIds.ShortGrass));
    Assert.True(BlockIds.IsPlant(BlockIds.ShortGrass));
  }

  static MeshData Mesh(ChunkData blocks, ChunkData?[]? neighbours = null)
  {
    return ChunkMesher.Build(blocks, neighbours ?? new ChunkData?[4], BlockIds.TileByBlock, BlockIds.TileCount, 0f);
  }

  [Fact]
  public void Single_block_in_the_air_has_six_faces()
  {
    var blocks = new ChunkData();
    blocks.Set(5, 10, 5, BlockIds.Stone);

    var mesh = Mesh(blocks);

    Assert.Equal(6 * 4, mesh.VertexCount);
    Assert.Equal(6 * 2, mesh.TriangleCount);
  }

  [Fact]
  public void Touching_blocks_hide_their_shared_faces()
  {
    var blocks = new ChunkData();
    blocks.Set(5, 10, 5, BlockIds.Stone);
    blocks.Set(6, 10, 5, BlockIds.Stone);

    // Ten faces are visible; the four long ones (top, bottom, front, back) merge into one quad each: six quads.
    Assert.Equal(6 * 2, Mesh(blocks).TriangleCount);
  }

  [Fact]
  public void Bottom_of_the_world_has_no_floor_faces()
  {
    var blocks = new ChunkData();
    blocks.Set(5, 0, 5, BlockIds.Stone);

    Assert.Equal(5 * 2, Mesh(blocks).TriangleCount);
  }

  [Fact]
  public void Chunk_border_face_is_hidden_by_a_solid_neighbour_and_shown_by_an_empty_one()
  {
    var blocks = new ChunkData();
    blocks.Set(VoxelMath.ChunkSize - 1, 10, 5, BlockIds.Stone);

    var solidNeighbour = new ChunkData();
    solidNeighbour.Set(0, 10, 5, BlockIds.Stone);
    var emptyNeighbour = new ChunkData();

    var hidden = Mesh(blocks, new ChunkData?[] { solidNeighbour, emptyNeighbour, emptyNeighbour, emptyNeighbour });
    var shown = Mesh(blocks, new ChunkData?[] { emptyNeighbour, emptyNeighbour, emptyNeighbour, emptyNeighbour });

    Assert.Equal(5 * 2, hidden.TriangleCount);
    Assert.Equal(6 * 2, shown.TriangleCount);
  }

  [Fact]
  public void Triangles_wind_clockwise_seen_from_outside()
  {
    var blocks = new ChunkData();
    blocks.Set(5, 10, 5, BlockIds.Stone);

    var mesh = Mesh(blocks);

    // For each triangle the geometric normal of the clockwise triangle (a, b, c) is (a - b) x (c - b)... in a right-handed
    // system a clockwise triangle seen from the front has cross(c - a, b - a) pointing toward the viewer.
    for (int i = 0; i < mesh.Indices.Length; i += 3)
    {
      float[] P(int idx) => new[] { mesh.Positions[idx * 3], mesh.Positions[idx * 3 + 1], mesh.Positions[idx * 3 + 2] };

      var a = P(mesh.Indices[i]);
      var b = P(mesh.Indices[i + 1]);
      var c = P(mesh.Indices[i + 2]);

      var ab = new[] { b[0] - a[0], b[1] - a[1], b[2] - a[2] };
      var ac = new[] { c[0] - a[0], c[1] - a[1], c[2] - a[2] };

      var cross = new[]
      {
        ac[1] * ab[2] - ac[2] * ab[1],
        ac[2] * ab[0] - ac[0] * ab[2],
        ac[0] * ab[1] - ac[1] * ab[0],
      };

      var n = new[] { mesh.Normals[mesh.Indices[i] * 3], mesh.Normals[mesh.Indices[i] * 3 + 1], mesh.Normals[mesh.Indices[i] * 3 + 2] };
      var dot = cross[0] * n[0] + cross[1] * n[1] + cross[2] * n[2];

      Assert.True(dot > 0, $"triangle {i / 3} winds the wrong way");
    }
  }

  [Fact]
  public void Uvs_stay_inside_the_tiles_row()
  {
    var blocks = new ChunkData();
    blocks.Set(5, 10, 5, BlockIds.Stone);
    blocks.Set(7, 10, 5, BlockIds.Grass);

    var mesh = Mesh(blocks);

    Assert.All(mesh.Uvs, uv => Assert.InRange(uv, 0f, 1f));
  }
}

public class TallChunkMeshTests
{
  [Fact]
  public void A_block_high_in_a_tall_column_is_meshed_and_an_empty_column_is_not()
  {
    var empty = ChunkMesher.Build(new ChunkData(), new ChunkData?[4], BlockIds.TileByBlock, BlockIds.TileCount, 0f);
    Assert.Equal(0, empty.TriangleCount);

    var blocks = new ChunkData();
    blocks.Set(5, VoxelMath.ChunkHeight - 1, 5, BlockIds.Stone);
    var tower = ChunkMesher.Build(blocks, new ChunkData?[4], BlockIds.TileByBlock, BlockIds.TileCount, 0f);

    Assert.Equal(12, tower.TriangleCount);
  }
}

public class TreeTests
{
  const int Seed = 5;

  static WorldGenerator Generator()
  {
    return new WorldGenerator(new WorldGenParams(Seed, 0.02f, 700f, 3));
  }

  [Fact]
  public void Whole_trees_appear_even_when_they_cross_chunk_borders()
  {
    var generator = Generator();
    var feature = new TreeFeature(Seed, generator.ColumnHeight, generator.TreeDensityAt, generator.TreeKindAt, new CaveCarver(Seed).IsMouth, generator.GiantDensityAt);
    var chunks = new System.Collections.Generic.Dictionary<(int, int), ChunkData>();

    byte At(int wx, int y, int wz)
    {
      VoxelMath.WorldToChunk(wx, wz, out var cx, out var cz, out var lx, out var lz);

      if (!chunks.TryGetValue((cx, cz), out var blocks))
      {
        chunks[(cx, cz)] = blocks = generator.GenerateChunk(cx, cz);
      }

      return blocks.Get(lx, y, lz);
    }

    var trees = 0;
    var crossing = 0;

    feature.ForTreesNear(-400, -400, 400, 400, tree =>
    {
      trees++;

      if (VoxelMath.FloorDiv(tree.X - 4, VoxelMath.ChunkSize) != VoxelMath.FloorDiv(tree.X + 5, VoxelMath.ChunkSize)
        || VoxelMath.FloorDiv(tree.Z - 4, VoxelMath.ChunkSize) != VoxelMath.FloorDiv(tree.Z + 5, VoxelMath.ChunkSize))
      {
        crossing++;
      }

      for (int x = tree.X - 5; x <= tree.X + 6; x++)
      {
        for (int z = tree.Z - 5; z <= tree.Z + 6; z++)
        {
          for (int y = tree.BaseY; y < tree.BaseY + tree.TrunkHeight + tree.CanopyRadius + 1; y++)
          {
            if (tree.IsTrunk(x, y, z))
            {
              // (in the Titan Grove a giant's limb or a dead log may be wood of another kind in the same place)
              if (generator.GiantDensityAt(tree.X, tree.Z) > 0f)
              {
                Assert.True(At(x, y, z) is BlockIds.Wood or BlockIds.BirchWood);
              }
              else
              {
                Assert.Equal(tree.TrunkBlock, At(x, y, z));
              }
            }
            else if (tree.IsLeaf(x, y, z))
            {
              Assert.NotEqual(BlockIds.Air, At(x, y, z));
            }
          }
        }
      }
    });

    Assert.True(trees > 10);
    Assert.True(crossing > 0);
  }

  [Fact]
  public void Generation_is_deterministic_and_the_spawn_is_clear()
  {
    var a = Generator().GenerateChunk(0, 0);
    var b = Generator().GenerateChunk(0, 0);

    Assert.True(WorldGeneratorTests.Same(a, b));
    Assert.True(a.Bytes < 400_000); // rock all through and open sky cost nothing, caves a bitmask

    var feature = new TreeFeature(Seed, Generator().ColumnHeight, Generator().TreeDensityAt, Generator().TreeKindAt);
    Assert.Equal(0, feature.TopAt(0, 0));
  }

  [Fact]
  public void Chunk_sections_cost_nothing_until_something_varies()
  {
    var chunk = new ChunkData();

    for (int section = 0; section < 40; section++)
    {
      chunk.FillSection(section, BlockIds.Stone);
    }

    Assert.Equal(0, chunk.AllocatedSections);
    Assert.Equal(BlockIds.Stone, chunk.Get(3, 100, 4));
    Assert.Equal(BlockIds.Air, chunk.Get(3, 900, 4));

    chunk.Set(3, 100, 4, BlockIds.Air); // a hole in the rock: this one section now has an array
    chunk.Set(7, 100, 4, BlockIds.Stone); // setting the block that is already there allocates nothing more

    Assert.Equal(1, chunk.AllocatedSections);
    Assert.Equal(BlockIds.Air, chunk.Get(3, 100, 4));
    Assert.Equal(BlockIds.Stone, chunk.Get(3, 101, 4));
    Assert.Equal(BlockIds.Stone, chunk.Get(7, 100, 4));

    chunk.Set(3, 100, 4, BlockIds.Stone);
    chunk.Compact();

    Assert.Equal(0, chunk.AllocatedSections); // filled back in: it is one block again
    Assert.Equal(40 * ChunkData.SectionHeight, chunk.TopLayer());
    Assert.Equal(40 * ChunkData.SectionHeight, chunk.TopLayer(5, 5));
    Assert.Equal(0, new ChunkData().TopLayer(5, 5));
  }

  [Fact]
  public void A_section_with_two_kinds_of_block_is_a_small_mask_and_with_three_an_array_that_compacts_to_a_palette()
  {
    var chunk = new ChunkData();
    chunk.FillSection(10, BlockIds.Stone);

    chunk.Set(1, 170, 1, BlockIds.Air);
    chunk.Set(2, 171, 2, BlockIds.Air);
    var masked = chunk.Bytes;

    Assert.InRange(masked, 1, 4096); // rock and air: a bitmask, not 16 KB

    chunk.Set(3, 172, 3, BlockIds.Magma); // a third kind
    var expanded = chunk.Bytes;

    Assert.True(expanded > 10000);
    Assert.Equal(BlockIds.Air, chunk.Get(1, 170, 1));
    Assert.Equal(BlockIds.Air, chunk.Get(2, 171, 2));
    Assert.Equal(BlockIds.Magma, chunk.Get(3, 172, 3));
    Assert.Equal(BlockIds.Stone, chunk.Get(4, 173, 4)); // what the mask held survived the change

    chunk.Compact();
    Assert.True(chunk.Bytes <= 4096); // three kinds: a 2-bit palette, a quarter of the array
    Assert.Equal(BlockIds.Magma, chunk.Get(3, 172, 3));
    Assert.Equal(BlockIds.Air, chunk.Get(1, 170, 1));
  }

  [Fact]
  public void Reads_outside_the_world_are_air_and_writes_are_ignored()
  {
    var chunk = new ChunkData();

    chunk.Set(1, -1, 1, BlockIds.Stone);
    chunk.Set(1, VoxelMath.ChunkHeight, 1, BlockIds.Stone);

    Assert.Equal(BlockIds.Air, chunk.Get(1, -1, 1));
    Assert.Equal(BlockIds.Air, chunk.Get(1, VoxelMath.ChunkHeight, 1));
    Assert.Equal(0, chunk.AllocatedSections);
  }
}
