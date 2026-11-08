using System;
using System.Collections.Generic;

namespace Curitibia.Enemies;

/// <summary>
/// A* over walkable voxel cells (a cell is where a body's feet are; the body is `body` cells tall). Engine-free and tested. Used instead of a baked
/// navigation mesh because the world streams and players edit it: there is nothing to rebake, the next query simply
/// sees the new blocks. Moves: 8 directions on the same level, one block up (needs headroom to jump) and up to
/// MaxDrop blocks down.
/// </summary>
public static class VoxelPathfinder
{
  public const int MaxDrop = 4;

  static readonly (int dx, int dz)[] Directions =
  {
    (1, 0), (-1, 0), (0, 1), (0, -1),
    (1, 1), (1, -1), (-1, 1), (-1, -1),
  };

  /// <summary>Feet can stand here: the cell and the one above are free and there is ground below.</summary>
  public static bool IsStandable(Func<int, int, int, bool> isSolid, int x, int y, int z, int body = 2)
  {
    return !BodyBlocked(isSolid, x, y, z, body) && isSolid(x, y - 1, z);
  }

  /// <summary>Finds the standable cell nearest to (x, y, z) in the same column (searching down, then up), or null.</summary>
  public static (int x, int y, int z)? SnapToStandable(Func<int, int, int, bool> isSolid, int x, int y, int z, int range = 4, int body = 2)
  {
    for (int d = 0; d <= range; d++)
    {
      if (IsStandable(isSolid, x, y - d, z, body)) return (x, y - d, z);
      if (d > 0 && IsStandable(isSolid, x, y + d, z, body)) return (x, y + d, z);
    }

    return null;
  }

  /// <summary>
  /// The cells to walk through, start excluded and goal included, or null when there is no path within maxExpansions.
  /// </summary>
  public static List<(int x, int y, int z)>? FindPath(
    Func<int, int, int, bool> isSolid,
    (int x, int y, int z) start,
    (int x, int y, int z) goal,
    int maxExpansions = 3000,
    int body = 2)
  {
    if (start == goal)
    {
      return new List<(int, int, int)>();
    }

    var open = new PriorityQueue<(int x, int y, int z), float>();
    var cameFrom = new Dictionary<(int, int, int), (int, int, int)>();
    var cost = new Dictionary<(int, int, int), float> { [start] = 0f };
    var closed = new HashSet<(int, int, int)>();

    open.Enqueue(start, Heuristic(start, goal));
    var expansions = 0;

    while (open.TryDequeue(out var current, out _))
    {
      if (current == goal)
      {
        return Reconstruct(cameFrom, current);
      }

      if (!closed.Add(current) || ++expansions > maxExpansions)
      {
        if (expansions > maxExpansions) return null;
        continue;
      }

      foreach (var (dx, dz) in Directions)
      {
        var diagonal = dx != 0 && dz != 0;

        // A diagonal must not cut a corner: both orthogonal neighbours at body height have to be open.
        if (diagonal && (BodyBlocked(isSolid, current.x + dx, current.y, current.z, body) || BodyBlocked(isSolid, current.x, current.y, current.z + dz, body)))
        {
          continue;
        }

        if (!TryStep(isSolid, current, dx, dz, body, out var next, out var extra))
        {
          continue;
        }

        var newCost = cost[current] + (diagonal ? 1.41f : 1f) + extra;

        if (!cost.TryGetValue(next, out var known) || newCost < known)
        {
          cost[next] = newCost;
          cameFrom[next] = current;
          open.Enqueue(next, newCost + Heuristic(next, goal));
        }
      }
    }

    return null;
  }

  /// <summary>Any of the <paramref name="body"/> cells standing up from (x, y, z) is solid.</summary>
  static bool BodyBlocked(Func<int, int, int, bool> isSolid, int x, int y, int z, int body)
  {
    for (int i = 0; i < body; i++)
    {
      if (isSolid(x, y + i, z))
      {
        return true;
      }
    }

    return false;
  }

  static bool TryStep(Func<int, int, int, bool> isSolid, (int x, int y, int z) from, int dx, int dz, int body, out (int x, int y, int z) next, out float extra)
  {
    var nx = from.x + dx;
    var nz = from.z + dz;
    extra = 0f;

    // Same level.
    if (IsStandable(isSolid, nx, from.y, nz, body))
    {
      next = (nx, from.y, nz);
      return true;
    }

    // One block up: needs room above the start to jump and room to arrive.
    if (!isSolid(from.x, from.y + body, from.z) && IsStandable(isSolid, nx, from.y + 1, nz, body))
    {
      next = (nx, from.y + 1, nz);
      extra = 0.6f;
      return true;
    }

    // Drop: the way down (the two cells of the body) must be open.
    if (!BodyBlocked(isSolid, nx, from.y, nz, body))
    {
      for (int drop = 1; drop <= MaxDrop; drop++)
      {
        var y = from.y - drop;

        if (IsStandable(isSolid, nx, y, nz, body))
        {
          next = (nx, y, nz);
          extra = 0.4f * drop;
          return true;
        }

        if (isSolid(nx, y, nz))
        {
          break;
        }
      }
    }

    next = default;
    return false;
  }

  static float Heuristic((int x, int y, int z) a, (int x, int y, int z) b)
  {
    var dx = Math.Abs(a.x - b.x);
    var dz = Math.Abs(a.z - b.z);
    var dy = Math.Abs(a.y - b.y);

    // Octile distance on the ground plane plus a little for height.
    return Math.Max(dx, dz) + 0.41f * Math.Min(dx, dz) + 0.3f * dy;
  }

  static List<(int x, int y, int z)> Reconstruct(Dictionary<(int, int, int), (int, int, int)> cameFrom, (int x, int y, int z) end)
  {
    var path = new List<(int, int, int)> { end };

    while (cameFrom.TryGetValue(path[^1], out var previous))
    {
      path.Add(previous);
    }

    path.RemoveAt(path.Count - 1); // the start
    path.Reverse();

    return path;
  }
}
