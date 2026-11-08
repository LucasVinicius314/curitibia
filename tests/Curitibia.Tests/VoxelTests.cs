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
  static WorldGenerator Make(int seed)
  {
    return new WorldGenerator(new WorldGenParams(seed, 0.04f, 10f, 16f, 2));
  }

  [Fact]
  public void Same_seed_gives_identical_chunks()
  {
    var a = Make(1234).GenerateChunk(-2, 3);
    var b = Make(1234).GenerateChunk(-2, 3);

    Assert.True(a.AsSpan().SequenceEqual(b));
  }

  [Fact]
  public void Different_seeds_differ()
  {
    var a = Make(1).GenerateChunk(0, 0);
    var b = Make(2).GenerateChunk(0, 0);

    Assert.False(a.AsSpan().SequenceEqual(b));
  }

  [Fact]
  public void Columns_are_continuous_across_chunk_borders()
  {
    var g = Make(99);

    // Heights are a function of world position, so neighbouring columns never jump arbitrarily.
    for (int wx = -70; wx < 70; wx++)
    {
      Assert.InRange(Math.Abs(g.ColumnHeight(wx, 5) - g.ColumnHeight(wx + 1, 5)), 0, 3);
    }
  }

  [Fact]
  public void Column_has_grass_on_top_over_soil_over_stone()
  {
    var g = Make(7);
    var blocks = g.GenerateChunk(0, 0);
    var h = g.ColumnHeight(4, 9);

    Assert.Equal(BlockIds.Grass, blocks[VoxelMath.Index(4, h - 1, 9)]);
    Assert.Equal(BlockIds.Air, h < VoxelMath.ChunkHeight ? blocks[VoxelMath.Index(4, h, 9)] : BlockIds.Air);

    if (h > 1)
    {
      Assert.Equal(BlockIds.Stone, blocks[VoxelMath.Index(4, h - 2, 9)]);
    Assert.Equal(BlockIds.Stone, blocks[VoxelMath.Index(4, 0, 9)]);
    }
  }
}

public class ChunkMesherTests
{
  static MeshData Mesh(byte[] blocks, byte[]?[]? neighbours = null)
  {
    return ChunkMesher.Build(blocks, neighbours ?? new byte[]?[4], BlockIds.TileByBlock, BlockIds.TileCount, 0f);
  }

  [Fact]
  public void Single_block_in_the_air_has_six_faces()
  {
    var blocks = new byte[VoxelMath.ChunkVolume];
    blocks[VoxelMath.Index(5, 10, 5)] = BlockIds.Stone;

    var mesh = Mesh(blocks);

    Assert.Equal(6 * 4, mesh.VertexCount);
    Assert.Equal(6 * 2, mesh.TriangleCount);
  }

  [Fact]
  public void Touching_blocks_hide_their_shared_faces()
  {
    var blocks = new byte[VoxelMath.ChunkVolume];
    blocks[VoxelMath.Index(5, 10, 5)] = BlockIds.Stone;
    blocks[VoxelMath.Index(6, 10, 5)] = BlockIds.Stone;

    Assert.Equal(10 * 2, Mesh(blocks).TriangleCount);
  }

  [Fact]
  public void Bottom_of_the_world_has_no_floor_faces()
  {
    var blocks = new byte[VoxelMath.ChunkVolume];
    blocks[VoxelMath.Index(5, 0, 5)] = BlockIds.Stone;

    Assert.Equal(5 * 2, Mesh(blocks).TriangleCount);
  }

  [Fact]
  public void Chunk_border_face_is_hidden_by_a_solid_neighbour_and_shown_by_an_empty_one()
  {
    var blocks = new byte[VoxelMath.ChunkVolume];
    blocks[VoxelMath.Index(VoxelMath.ChunkSize - 1, 10, 5)] = BlockIds.Stone;

    var solidNeighbour = new byte[VoxelMath.ChunkVolume];
    solidNeighbour[VoxelMath.Index(0, 10, 5)] = BlockIds.Stone;
    var emptyNeighbour = new byte[VoxelMath.ChunkVolume];

    var hidden = Mesh(blocks, new byte[]?[] { solidNeighbour, emptyNeighbour, emptyNeighbour, emptyNeighbour });
    var shown = Mesh(blocks, new byte[]?[] { emptyNeighbour, emptyNeighbour, emptyNeighbour, emptyNeighbour });

    Assert.Equal(5 * 2, hidden.TriangleCount);
    Assert.Equal(6 * 2, shown.TriangleCount);
  }

  [Fact]
  public void Triangles_wind_clockwise_seen_from_outside()
  {
    var blocks = new byte[VoxelMath.ChunkVolume];
    blocks[VoxelMath.Index(5, 10, 5)] = BlockIds.Stone;

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
    var blocks = new byte[VoxelMath.ChunkVolume];
    blocks[VoxelMath.Index(5, 10, 5)] = BlockIds.Stone;
    blocks[VoxelMath.Index(7, 10, 5)] = BlockIds.Grass;

    var mesh = Mesh(blocks);

    Assert.All(mesh.Uvs, uv => Assert.InRange(uv, 0f, 1f));
  }
}

public class TallChunkMeshTests
{
  [Fact]
  public void A_block_high_in_a_tall_column_is_meshed_and_an_empty_column_is_not()
  {
    var empty = ChunkMesher.Build(new byte[VoxelMath.ChunkVolume], new byte[]?[4], BlockIds.TileByBlock, BlockIds.TileCount, 0f);
    Assert.Equal(0, empty.TriangleCount);

    var blocks = new byte[VoxelMath.ChunkVolume];
    blocks[VoxelMath.Index(5, VoxelMath.ChunkHeight - 1, 5)] = BlockIds.Stone;
    var tower = ChunkMesher.Build(blocks, new byte[]?[4], BlockIds.TileByBlock, BlockIds.TileCount, 0f);

    Assert.Equal(12, tower.TriangleCount);
  }
}

public class TreeTests
{
  const int Seed = 5;

  static WorldGenerator Generator()
  {
    return new WorldGenerator(new WorldGenParams(Seed, 0.02f, 20f, 50f, 2));
  }

  [Fact]
  public void Whole_trees_appear_even_when_they_cross_chunk_borders()
  {
    var generator = Generator();
    var feature = new TreeFeature(Seed, generator.ColumnHeight);
    var chunks = new System.Collections.Generic.Dictionary<(int, int), byte[]>();

    byte At(int wx, int y, int wz)
    {
      VoxelMath.WorldToChunk(wx, wz, out var cx, out var cz, out var lx, out var lz);

      if (!chunks.TryGetValue((cx, cz), out var blocks))
      {
        chunks[(cx, cz)] = blocks = generator.GenerateChunk(cx, cz);
      }

      return VoxelMath.Get(blocks, lx, y, lz);
    }

    var trees = 0;
    var crossing = 0;

    feature.ForTreesNear(-100, -100, 100, 100, tree =>
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
              Assert.Equal(BlockIds.Wood, At(x, y, z));
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

    Assert.Equal(a, b);
    Assert.True(a.Length < VoxelMath.ChunkVolume); // trimmed to the layers in use

    var feature = new TreeFeature(Seed, Generator().ColumnHeight);
    Assert.Equal(0, feature.TopAt(0, 0));
  }

  [Fact]
  public void EnsureHeight_grows_by_layers_and_keeps_contents()
  {
    var blocks = new byte[VoxelMath.LayerSize * VoxelMath.LayerStep];
    blocks[5] = 9;

    var same = VoxelMath.EnsureHeight(blocks, 3);
    var grown = VoxelMath.EnsureHeight(blocks, 40);

    Assert.Same(blocks, same);
    Assert.Equal(VoxelMath.LayerSize * 48, grown.Length);
    Assert.Equal(9, grown[5]);
    Assert.Equal(0, VoxelMath.Get(blocks, 1, 200, 1));
  }
}
