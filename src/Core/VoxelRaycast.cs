using System;

namespace Curitibia.Core;

/// <summary>Engine-free voxel ray walk (Amanatides and Woo). Gives the hit voxel and the face normal for free.</summary>
public static class VoxelRaycast
{
  /// <summary>
  /// Walks from (ox, oy, oz) along (dx, dy, dz) up to maxDistance. Returns true on the first solid voxel. The normal
  /// points out of the face that was entered (zero when the ray starts inside a solid voxel).
  /// </summary>
  public static bool Cast(
    float ox, float oy, float oz,
    float dx, float dy, float dz,
    float maxDistance,
    Func<int, int, int, bool> isSolid,
    out int hx, out int hy, out int hz,
    out int nx, out int ny, out int nz)
  {
    var len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    nx = ny = nz = 0;
    hx = VoxelMath.FloorToInt(ox);
    hy = VoxelMath.FloorToInt(oy);
    hz = VoxelMath.FloorToInt(oz);

    if (len < 1e-6f)
    {
      return false;
    }

    dx /= len;
    dy /= len;
    dz /= len;

    if (isSolid(hx, hy, hz))
    {
      return true;
    }

    var stepX = dx > 0 ? 1 : -1;
    var stepY = dy > 0 ? 1 : -1;
    var stepZ = dz > 0 ? 1 : -1;

    var tDeltaX = dx != 0 ? MathF.Abs(1f / dx) : float.PositiveInfinity;
    var tDeltaY = dy != 0 ? MathF.Abs(1f / dy) : float.PositiveInfinity;
    var tDeltaZ = dz != 0 ? MathF.Abs(1f / dz) : float.PositiveInfinity;

    var tMaxX = dx > 0 ? (hx + 1 - ox) / dx : dx < 0 ? (ox - hx) / -dx : float.PositiveInfinity;
    var tMaxY = dy > 0 ? (hy + 1 - oy) / dy : dy < 0 ? (oy - hy) / -dy : float.PositiveInfinity;
    var tMaxZ = dz > 0 ? (hz + 1 - oz) / dz : dz < 0 ? (oz - hz) / -dz : float.PositiveInfinity;

    while (true)
    {
      float t;

      if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
      {
        t = tMaxX;
        if (t > maxDistance) return false;
        hx += stepX;
        tMaxX += tDeltaX;
        nx = -stepX; ny = 0; nz = 0;
      }
      else if (tMaxY <= tMaxZ)
      {
        t = tMaxY;
        if (t > maxDistance) return false;
        hy += stepY;
        tMaxY += tDeltaY;
        nx = 0; ny = -stepY; nz = 0;
      }
      else
      {
        t = tMaxZ;
        if (t > maxDistance) return false;
        hz += stepZ;
        tMaxZ += tDeltaZ;
        nx = 0; ny = 0; nz = -stepZ;
      }

      if (isSolid(hx, hy, hz))
      {
        return true;
      }
    }
  }
}
