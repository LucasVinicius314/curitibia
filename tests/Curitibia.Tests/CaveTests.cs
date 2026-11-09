using System;
using System.Collections.Generic;
using System.Diagnostics;
using Curitibia.Core;
using Curitibia.World;
using Xunit;
using Xunit.Abstractions;

namespace Curitibia.Tests;

public class CaveTests
{
  readonly ITestOutputHelper output;

  public CaveTests(ITestOutputHelper output)
  {
    this.output = output;
  }

  static WorldGenerator Make(int seed)
  {
    return new WorldGenerator(new WorldGenParams(seed, 0.02f, VoxelMath.SeaLevel, 3));
  }

  /// <summary>Chunks spread over the world around the origin (not the spawn clearing only).</summary>
  static IEnumerable<ChunkData> Chunks(WorldGenerator g, int count = 12)
  {
    for (int i = 0; i < count; i++)
    {
      yield return g.GenerateChunk(i * 17 - 90, i * 11 - 60);
    }
  }

  [Fact]
  public void Y0_is_the_surface_baseline_and_the_world_is_deep_enough_for_the_underworld()
  {
    Assert.Equal(700, VoxelMath.SeaLevel);
    Assert.Equal(-700, VoxelMath.DisplayY(0));
    Assert.Equal(-500, VoxelMath.DisplayY(Underground.HellTop));
    Assert.True(VoxelMath.ChunkHeight >= VoxelMath.SeaLevel + 400, "room for the mountains above Y 0");

    var g = Make(42);

    // The lowlands sit just above Y 0, and the world never starts under it.
    Assert.InRange(VoxelMath.DisplayY(g.ColumnHeight(0, 0)), -5, 30);
  }

  [Fact]
  public void Rock_is_stone_above_the_underworld_and_hellrock_in_it()
  {
    var g = Make(42);

    foreach (var chunk in Chunks(g))
    {
      for (int z = 0; z < 32; z += 3)
      {
        for (int x = 0; x < 32; x += 3)
        {
          for (int y = 0; y < Underground.HellTop - Underground.BlendBand; y += 5)
          {
            var block = chunk.Get(x, y, z);
            Assert.True(block is BlockIds.Air or BlockIds.Hellrock or BlockIds.Magma, $"{block} at layer {y}");
          }

          for (int y = Underground.HellTop + Underground.BlendBand; y < Underground.HellTop + 200; y += 5)
          {
            var block = chunk.Get(x, y, z);
            Assert.True(block is BlockIds.Air or BlockIds.Stone, $"{block} at layer {y}");
          }
        }
      }
    }
  }

  [Fact]
  public void The_underworld_is_an_open_hall_held_up_by_pillars()
  {
    var g = Make(42);
    long columns = 0, pillars = 0, openCells = 0, cells = 0;

    foreach (var chunk in Chunks(g, 20))
    {
      for (int z = 0; z < 32; z += 2)
      {
        for (int x = 0; x < 32; x += 2)
        {
          columns++;

          // A pillar: rock all the way up the hall, from just above the magma to where the hall's roof starts.
          var solid = true;

          for (int y = Underground.FloorHigh + 8; y < Underground.HellTop - 30; y += 4)
          {
            if (chunk.Get(x, y, z) == BlockIds.Air)
            {
              solid = false;
              break;
            }
          }

          if (solid)
          {
            pillars++;
          }

          for (int y = Underground.FloorHigh + 8; y < Underground.HellTop - 30; y += 6)
          {
            cells++;

            if (chunk.Get(x, y, z) == BlockIds.Air)
            {
              openCells++;
            }
          }
        }
      }
    }

    var pillarShare = 100.0 * pillars / columns;
    var open = 100.0 * openCells / cells;

    output.WriteLine($"{pillarShare:F1}% of the columns are pillars from the magma to the roof, {open:F1}% of the hall is open");
    Assert.InRange(pillarShare, 1.5, 40.0);
    Assert.InRange(open, 60.0, 92.0);
  }

  [Fact]
  public void The_underworld_floor_is_mostly_solid_with_magma_lakes()
  {
    var g = Make(42);
    long columns = 0, lake = 0, dry = 0;

    foreach (var chunk in Chunks(g, 30))
    {
      for (int z = 0; z < 32; z += 2)
      {
        for (int x = 0; x < 32; x += 2)
        {
          columns++;

          if (chunk.Get(x, Underground.LavaTop - 2, z) == BlockIds.Magma)
          {
            lake++;
          }
          else if (chunk.Get(x, Underground.LavaTop - 2, z) == BlockIds.Hellrock)
          {
            dry++;
          }
        }
      }
    }

    var lakeShare = 100.0 * lake / columns;
    output.WriteLine($"floor: {lakeShare:F1}% magma lakes, {100.0 * dry / columns:F1}% solid ground at Y -672");
    Assert.InRange(lakeShare, 8.0, 40.0);
    Assert.True(dry > lake * 1.5, "the floor is mostly rock");
  }

  [Fact]
  public void The_red_aura_ramps_from_0_at_Y_minus_450_to_100_at_Y_minus_500()
  {
    float At(int y) => Underground.AuraBlend(y + VoxelMath.SeaLevel);

    Assert.Equal(0f, At(-300));
    Assert.Equal(0f, At(-440));
    Assert.Equal(0f, At(-450));
    Assert.InRange(At(-475), 0.45f, 0.55f);
    Assert.Equal(1f, At(-500));
    Assert.Equal(1f, At(-690));

    var previous = 0f;

    for (int y = -450; y >= -500; y -= 2)
    {
      var now = At(y);
      Assert.True(now >= previous, $"never goes back down ({y})");
      Assert.True(now - previous < 0.1f, $"no sudden step ({y})");
      previous = now;
    }
  }

  [Fact]
  public void Stone_blends_into_hellrock_over_a_band_instead_of_a_line()
  {
    var g = Make(42);
    var hellrockByLayer = new int[Underground.BlendBand * 2];
    var rockByLayer = new int[Underground.BlendBand * 2];

    foreach (var chunk in Chunks(g, 20))
    {
      for (int z = 0; z < 32; z += 2)
      {
        for (int x = 0; x < 32; x += 2)
        {
          for (int i = 0; i < hellrockByLayer.Length; i++)
          {
            var block = chunk.Get(x, Underground.HellTop - Underground.BlendBand + i, z);

            if (block == BlockIds.Hellrock)
            {
              hellrockByLayer[i]++;
              rockByLayer[i]++;
            }
            else if (block == BlockIds.Stone)
            {
              rockByLayer[i]++;
            }
          }
        }
      }
    }

    var low = (double)hellrockByLayer[Underground.BlendBand * 2 - 5] / Math.Max(1, rockByLayer[Underground.BlendBand * 2 - 5]);
    var mid = (double)hellrockByLayer[Underground.BlendBand] / Math.Max(1, rockByLayer[Underground.BlendBand]);
    var high = (double)hellrockByLayer[4] / Math.Max(1, rockByLayer[4]);

    output.WriteLine($"hellrock share of rock: top of the band {low:P0}, middle {mid:P0}, bottom {high:P0}");
    Assert.True(low < 0.15, "stone at the top of the band (the caverns side)");
    Assert.InRange(mid, 0.3, 0.7);
    Assert.True(high > 0.85, "hellrock at the bottom of the band");
  }

  [Fact]
  public void The_bottom_of_the_world_is_solid_and_magma_only_fills_the_lowest_caverns()
  {
    var g = Make(42);
    var magma = 0;

    foreach (var chunk in Chunks(g, 20))
    {
      for (int z = 0; z < 32; z++)
      {
        for (int x = 0; x < 32; x++)
        {
          for (int y = 0; y < Underground.SolidFloor; y++)
          {
            Assert.Equal(BlockIds.Hellrock, chunk.Get(x, y, z));
          }

          for (int y = Underground.LavaTop; y < VoxelMath.ChunkHeight; y += 3)
          {
            Assert.NotEqual(BlockIds.Magma, chunk.Get(x, y, z));
          }

          for (int y = 0; y < Underground.LavaTop; y++)
          {
            if (chunk.Get(x, y, z) == BlockIds.Magma)
            {
              magma++;
            }
          }
        }
      }
    }

    output.WriteLine($"{magma} magma voxels in 20 chunks");
    Assert.True(magma > 1000, "the underworld has a sea of magma");
  }

  [Fact]
  public void Caves_open_up_with_depth_and_the_underworld_is_mostly_open()
  {
    var g = Make(42);
    long caverns = 0, cavernsOpen = 0, hell = 0, hellOpen = 0;

    foreach (var chunk in Chunks(g, 20))
    {
      for (int z = 0; z < 32; z += 2)
      {
        for (int x = 0; x < 32; x += 2)
        {
          for (int y = Underground.LavaTop + 4; y < Underground.HellTop; y += 3)
          {
            hell++;

            if (chunk.Get(x, y, z) == BlockIds.Air)
            {
              hellOpen++;
            }
          }

          for (int y = Underground.HellTop + 4; y < VoxelMath.SeaLevel - 40; y += 3)
          {
            caverns++;

            if (chunk.Get(x, y, z) == BlockIds.Air)
            {
              cavernsOpen++;
            }
          }
        }
      }
    }

    var cavern = 100.0 * cavernsOpen / caverns;
    var under = 100.0 * hellOpen / hell;

    output.WriteLine($"caverns {cavern:F1}% open, underworld {under:F1}% open");
    Assert.InRange(cavern, 8.0, 20.0);
    Assert.InRange(under, 55.0, 90.0);
    Assert.True(under > cavern * 2, "the underworld is far more open");
  }

  [Fact]
  public void Rock_is_left_under_the_surface_except_where_a_cave_mouth_opens()
  {
    var g = Make(42);
    var crusted = 0;
    var mouths = 0;
    var total = 0;

    for (int i = 0; i < 25; i++)
    {
      var cx = i * 13 - 150;
      var cz = i * 7 - 80;
      var chunk = g.GenerateChunk(cx, cz);

      for (int z = 0; z < 32; z++)
      {
        for (int x = 0; x < 32; x++)
        {
          var surface = g.Column(cx * 32 + x, cz * 32 + z).Height;
          var solid = true;

          for (int y = surface - 10; y < surface - 1; y++)
          {
            if (chunk.Get(x, y, z) == BlockIds.Air)
            {
              solid = false;
              break;
            }
          }

          total++;

          if (solid)
          {
            crusted++;
          }
          else
          {
            mouths++;
          }
        }
      }
    }

    output.WriteLine($"{crusted} columns with a crust, {mouths} with a cave under the surface, of {total}");
    Assert.True(crusted > total * 0.8, "most of the ground is solid just under the surface");
    Assert.True(mouths > 0, "but some caves open toward it");
  }

  [Fact]
  public void Merged_collision_covers_exactly_the_visible_faces_with_far_fewer_triangles()
  {
    var g = Make(42);

    foreach (var (cx, cz) in new[] { (0, 0), (-60, 10), (-75, 3) })
    {
      var chunk = g.GenerateChunk(cx, cz);
      var neighbours = new ChunkData?[] { g.GenerateChunk(cx + 1, cz), g.GenerateChunk(cx - 1, cz), g.GenerateChunk(cx, cz + 1), g.GenerateChunk(cx, cz - 1) };
      var mesh = ChunkMesher.Build(chunk, neighbours, BlockIds.TileByBlock, BlockIds.TileCount, 0f); // the whole column: no cut, so no caps

      var visibleFaces = mesh.TriangleCount / 2;
      var triangles = mesh.CollisionTriangles;
      double area = 0;

      for (int t = 0; t + 8 < triangles.Length; t += 9)
      {
        var ax = triangles[t + 3] - triangles[t];
        var ay = triangles[t + 4] - triangles[t + 1];
        var az = triangles[t + 5] - triangles[t + 2];
        var bx = triangles[t + 6] - triangles[t];
        var by = triangles[t + 7] - triangles[t + 1];
        var bz = triangles[t + 8] - triangles[t + 2];
        var cxp = ay * bz - az * by;
        var cyp = az * bx - ax * bz;
        var czp = ax * by - ay * bx;

        area += 0.5 * Math.Sqrt(cxp * cxp + cyp * cyp + czp * czp);
      }

      output.WriteLine($"chunk ({cx},{cz}): {visibleFaces * 2} visible triangles, {triangles.Length / 9} collision triangles, area {area} vs {visibleFaces} faces");
      Assert.Equal(visibleFaces, area, 3);
      Assert.True(triangles.Length / 9 < visibleFaces * 2 / 2, "at least half the triangles are gone");
    }
  }

  [Fact]
  public void The_mesher_closes_a_cut_through_a_cave_and_skips_rock_all_through()
  {
    var chunk = new ChunkData();

    for (int section = 0; section < 30; section++)
    {
      chunk.FillSection(section, BlockIds.Stone);
    }

    // a shaft of air at (5, 5) from layer 160 to 320
    for (int y = 160; y < 320; y++)
    {
      chunk.Set(5, y, 5, BlockIds.Air);
    }

    var neighbours = new ChunkData?[4];
    var watch = Stopwatch.StartNew();
    var mesh = ChunkMesher.Build(chunk, neighbours, BlockIds.TileByBlock, BlockIds.TileCount, 0f, floorLayer: 192, ceilingLayer: 288);
    watch.Stop();

    output.WriteLine($"mesh of a slab with a shaft: {mesh.TriangleCount} triangles in {watch.ElapsedMilliseconds} ms");

    // The four walls of the shaft (96 layers each) plus a cap on the floor and one on the ceiling: no hole to look through.
    Assert.Equal(4 * 96 * 2 + 2 * 2, mesh.TriangleCount);

    var floorCap = false;
    var ceilingCap = false;

    for (int v = 0; v < mesh.VertexCount; v++)
    {
      var y = mesh.Positions[v * 3 + 1];
      var ny = mesh.Normals[v * 3 + 1];

      floorCap |= y == 192 && ny > 0.5f;
      ceilingCap |= y == 288 && ny < -0.5f;
    }

    // Collision: each of the four shaft walls is one merged rectangle (two triangles) instead of 96 faces, and the caps (not blocks) are left out.
    Assert.Equal(4 * 2, mesh.CollisionTriangles.Length / 9);
    Assert.True(floorCap, "the shaft is closed at the bottom of the window");
    Assert.True(ceilingCap, "and at the top");
  }
}
