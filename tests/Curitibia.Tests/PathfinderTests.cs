using System;
using System.Collections.Generic;
using Curitibia.Enemies;
using Xunit;

namespace Curitibia.Tests;

public class PathfinderTests
{
  // A world given as a set of solid cells over a solid floor at y < 0.
  static Func<int, int, int, bool> World(params (int x, int y, int z)[] solid)
  {
    var set = new HashSet<(int, int, int)>(solid);

    return (x, y, z) => y < 0 || set.Contains((x, y, z));
  }

  static List<(int x, int y, int z)>? Path(Func<int, int, int, bool> world, (int, int, int) a, (int, int, int) b)
  {
    return VoxelPathfinder.FindPath(world, a, b);
  }

  [Fact]
  public void Straight_walk_on_flat_ground()
  {
    var path = Path(World(), (0, 0, 0), (5, 0, 0));

    Assert.NotNull(path);
    Assert.Equal((5, 0, 0), path![^1]);
    Assert.Equal(5, path.Count);
  }

  [Fact]
  public void Walks_around_a_wall()
  {
    var wall = new List<(int, int, int)>();

    for (int z = -3; z <= 3; z++)
    {
      wall.Add((2, 0, z));
      wall.Add((2, 1, z));
    }

    var path = Path(World(wall.ToArray()), (0, 0, 0), (4, 0, 0));

    Assert.NotNull(path);
    Assert.DoesNotContain(path!, c => c.x == 2 && c.z >= -3 && c.z <= 3 && c.y <= 1);
    Assert.Equal((4, 0, 0), path![^1]);
  }

  [Fact]
  public void Steps_up_one_block_but_not_two()
  {
    // A one-block ledge at x = 2 (cell (2,0,0) solid, so you stand at y = 1), and a two-block wall at x = 5.
    var world = World((2, 0, 0), (3, 0, 0), (5, 0, 0), (5, 1, 0), (5, 0, 1), (5, 1, 1), (5, 0, -1), (5, 1, -1));

    var up = Path(world, (0, 0, 0), (3, 1, 0));

    Assert.NotNull(up);
    Assert.Equal((3, 1, 0), up![^1]);
  }

  [Fact]
  public void No_path_into_a_sealed_pit()
  {
    var ring = new List<(int, int, int)>();

    for (int x = -1; x <= 1; x++)
    {
      for (int z = -1; z <= 1; z++)
      {
        if (x != 0 || z != 0)
        {
          ring.Add((x + 6, 0, z));
          ring.Add((x + 6, 1, z));
          ring.Add((x + 6, 2, z));
        }
      }
    }

    Assert.Null(Path(World(ring.ToArray()), (0, 0, 0), (6, 0, 0)));
  }

  [Fact]
  public void Drops_down_a_small_ledge()
  {
    // Ground at y < 1 for x <= 2 (a raised platform), lower ground beyond: start on the platform at y = 1.
    bool World2(int x, int y, int z) => x <= 2 ? y < 1 : y < -1;

    var path = VoxelPathfinder.FindPath(World2, (0, 1, 0), (5, -1, 0));

    Assert.NotNull(path);
    Assert.Equal((5, -1, 0), path![^1]);
  }

  [Fact]
  public void Does_not_cut_corners_through_solid_diagonals()
  {
    // Two solid pillars touching diagonally at (1,0,0) and (0,0,1): the diagonal move (0,0,0) -> (1,0,1) is not allowed.
    var path = Path(World((1, 0, 0), (1, 1, 0), (0, 0, 1), (0, 1, 1)), (0, 0, 0), (1, 0, 1));

    Assert.NotNull(path);
    Assert.True(path!.Count > 1);
  }

  [Fact]
  public void SnapToStandable_finds_the_ground_below()
  {
    var found = VoxelPathfinder.SnapToStandable(World(), 3, 3, 3);

    Assert.Equal((3, 0, 3), found);
  }
}
