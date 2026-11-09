using System;
using Curitibia.Core;

namespace Curitibia.World;

/// <summary>The vertical layout of the world: where Y 0 is and where the underground layers start. Engine-free.</summary>
public static class Underground
{
  /// <summary>The layer that is Y 0.</summary>
  public const int SeaLevel = VoxelMath.SeaLevel;

  /// <summary>Below this layer (Y -500) is the underworld: open, rocky red caverns over a sea of magma.</summary>
  public const int HellTop = SeaLevel - 500;

  /// <summary>Half the height, in layers, of the band around <see cref="HellTop"/> where the caverns blend into the underworld.</summary>
  public const int BlendBand = 60;

  /// <summary>0 in the caverns, 1 in the underworld, smooth over the blend band.</summary>
  public static float DepthBlend(int layer)
  {
    var t = Math.Clamp((HellTop + BlendBand - layer) / (2f * BlendBand), 0f, 1f);

    return t * t * (3f - 2f * t);
  }

  /// <summary>The red aura is 0% at this layer (Y -450) and above...</summary>
  public const int AuraStart = HellTop + 50;

  /// <summary>...and 100% from this layer (Y -500, the top of the underworld) down.</summary>
  public const int AuraFull = HellTop;

  /// <summary>0 at Y -450 and above, 1 at Y -500 and below, a smoothstep between (no start, no end).</summary>
  public static float AuraBlend(int layer)
  {
    var t = Math.Clamp((AuraStart - layer) / (float)(AuraStart - AuraFull), 0f, 1f);

    return t * t * (3f - 2f * t);
  }

  /// <summary>Open space in the underworld below this layer (Y -672) is magma.</summary>
  public const int LavaTop = 28;

  /// <summary>The lowest layers are never carved, so the world has a floor.</summary>
  public const int SolidFloor = 3;

  /// <summary>The solid floor of the underworld hall rises to about this layer (Y -664); the magma lakes are the low spots below <see cref="LavaTop"/>.</summary>
  public const int FloorHigh = 38;

  /// <summary>The bottom of a magma lake.</summary>
  public const int LakeBed = 12;

  public const string CavernsName = "The Caverns";
  public const string UnderworldName = "The Underworld";

  /// <summary>The name of the underground layer at a layer, or null on or above the ground.</summary>
  public static string? NameAt(int layer, bool belowGround)
  {
    if (!belowGround)
    {
      return null;
    }

    return layer < HellTop ? UnderworldName : CavernsName;
  }

  /// <summary>What solid rock is made of at a layer: stone above the underworld, hellrock in it.</summary>
  public static byte RockAt(int layer)
  {
    return layer < HellTop ? BlockIds.Hellrock : BlockIds.Stone;
  }
}

/// <summary>
/// Carves caves out of solid rock with 3D noise. Tunnels are where two noise fields are both near zero (their zero sheets cross
/// in winding tubes); caverns are where a third, slower field is high. Both open up with depth, and in the underworld the
/// whole layer is mostly open, with the open floor filled with magma. Near the surface the rock is left alone except in
/// scattered regions where tunnels break out into cave mouths. A pure function of (seed, chunk); engine-free.
/// </summary>
public sealed class CaveCarver
{
  /// <summary>Edge of a noise cell in voxels: the noise is measured at the corners and blended between them.</summary>
  const int Cell = 4;
  const int Corners = VoxelMath.ChunkSize / Cell + 1;

  /// <summary>Rock left between the surface and the caves, in voxels (5 m), except where a cave mouth opens.</summary>
  const int Crust = 10;

  const float TunnelFrequency = 0.022f;
  const float CavernFrequency = 0.011f;

  /// <summary>Stretches the noise vertically (1 = round tunnels; more makes flat, thin ones).</summary>
  const float Squash = 1.0f;

  [ThreadStatic] static float[]? bufferA;
  [ThreadStatic] static float[]? bufferB;
  [ThreadStatic] static float[]? bufferC;
  [ThreadStatic] static float[]? bufferD;

  readonly GradientNoise tunnelA;
  readonly GradientNoise tunnelB;
  readonly GradientNoise cavern;
  readonly GradientNoise entrance;
  readonly GradientNoise pillar;
  readonly GradientNoise floorNoise;

  public CaveCarver(int seed)
  {
    tunnelA = new GradientNoise(seed + 20011);
    tunnelB = new GradientNoise(seed + 30011);
    cavern = new GradientNoise(seed + 40009);
    entrance = new GradientNoise(seed + 50021);
    pillar = new GradientNoise(seed + 70001);
    floorNoise = new GradientNoise(seed + 80021);
  }

  /// <summary>Half-width of a tunnel in noise units at a layer: roomy in the caverns, wider still in the underworld, smooth between.</summary>
  static float TunnelWidth(int layer)
  {
    var depth = Math.Clamp((Underground.SeaLevel - layer) / 500f, 0f, 1f);
    var caverns = 0.125f + 0.03f * depth;
    var underworld = 0.24f;

    return caverns + (underworld - caverns) * Underground.DepthBlend(layer);
  }

  /// <summary>The cavern noise must exceed this: rare near the surface, common deep down, nearly everywhere in the underworld.</summary>
  static float CavernThreshold(int layer)
  {
    var depth = Math.Clamp((Underground.SeaLevel - layer) / 500f, 0f, 1f);
    var caverns = 0.62f - 0.12f * depth;
    var underworld = -0.38f; // nearly everything is open: a hall, held up by pillars

    return caverns + (underworld - caverns) * Underground.DepthBlend(layer);
  }

  /// <summary>
  /// The pillar noise must exceed this for rock to stand in the underworld's open hall (never, in the caverns). Lower = fatter
  /// and more frequent pillars.
  /// </summary>
  static float PillarThreshold(int layer)
  {
    var blend = Underground.DepthBlend(layer);

    return blend <= 0.02f ? 10f : 0.9f + (PillarCore - 0.9f) * blend;
  }

  const float PillarCore = 0.27f;
  const float PillarFrequency = 0.03f;

  /// <summary>Pillars are tall: the noise barely changes with height.</summary>
  const float PillarVertical = 0.0035f;

  /// <summary>
  /// The top of the solid floor of the underworld hall at a column: a rocky plain (FloorHigh, a little uneven) with magma lakes
  /// where a slow 2D noise is low, sloping down to the lake bed at the edges.
  /// </summary>
  int FloorAt(int wx, int wz)
  {
    var n = floorNoise.Sample(wx * LakeFrequency, wz * LakeFrequency) / 0.7f; // about -1 to 1
    var rough = floorNoise.Sample(wx * 0.07f + 9.1f, wz * 0.07f + 4.7f) / 0.7f;
    var t = Math.Clamp((n - LakeEdgeLow) / (LakeEdgeHigh - LakeEdgeLow), 0f, 1f);
    t = t * t * (3f - 2f * t);

    return (int)MathF.Round(Underground.LakeBed + (FloorHighAt(rough) - Underground.LakeBed) * t);
  }

  static float FloorHighAt(float rough)
  {
    return Underground.FloorHigh - 2.5f + 2.5f * rough;
  }

  /// <summary>Lakes are where the lake noise is below LakeEdgeLow; the plain starts at LakeEdgeHigh.</summary>
  const float LakeFrequency = 0.0085f;
  const float LakeEdgeLow = -0.5f;
  const float LakeEdgeHigh = -0.2f;

  /// <summary>
  /// Carves the chunk whose first column is (<paramref name="x0"/>, <paramref name="z0"/>). <paramref name="heights"/> holds
  /// each column's surface layer count, indexed x + z * ChunkSize.
  /// </summary>
  public void Carve(ChunkData chunk, int x0, int z0, int[] heights, int tallest)
  {
    const int size = VoxelMath.ChunkSize;

    // How high a column may be carved: under the crust, or right up to the surface where a cave mouth opens.
    Span<int> limit = stackalloc int[size * size];
    Span<int> floorTop = stackalloc int[size * size]; // the solid floor of the underworld hall, per column

    for (int z = 0; z < size; z++)
    {
      for (int x = 0; x < size; x++)
      {
        var mouth = entrance.Sample((x0 + x) * 0.008f + 17.3f, (z0 + z) * 0.008f + 5.1f) > 0.3f;

        limit[x + z * size] = heights[x + z * size] - 1 - (mouth ? 0 : Crust);
        floorTop[x + z * size] = FloorAt(x0 + x, z0 + z);
      }
    }

    var layers = Math.Min(VoxelMath.ChunkHeight, tallest);
    var cubeLayers = (layers + Cell - 1) / Cell;
    var points = Corners * Corners * (cubeLayers + 1);

    var a = bufferA ??= new float[Corners * Corners * (VoxelMath.ChunkHeight / Cell + 1)];
    var b = bufferB ??= new float[a.Length];
    var c = bufferC ??= new float[a.Length];
    var d = bufferD ??= new float[a.Length];
    var pillarTop = Underground.HellTop + Underground.BlendBand + Cell; // layers above this never hold pillars

    for (int cy = 0; cy <= cubeLayers; cy++)
    {
      var y = cy * Cell;

      for (int cz = 0; cz < Corners; cz++)
      {
        for (int cx = 0; cx < Corners; cx++)
        {
          var wx = x0 + cx * Cell;
          var wz = z0 + cz * Cell;
          var i = (cy * Corners + cz) * Corners + cx;

          a[i] = tunnelA.Sample(wx * TunnelFrequency, y * TunnelFrequency * Squash, wz * TunnelFrequency);
          b[i] = tunnelB.Sample(wx * TunnelFrequency + 31.7f, y * TunnelFrequency * Squash + 11.9f, wz * TunnelFrequency + 7.3f);
          c[i] = cavern.Sample(wx * CavernFrequency, y * CavernFrequency * Squash, wz * CavernFrequency);
          d[i] = y <= pillarTop ? pillar.Sample(wx * PillarFrequency, y * PillarVertical, wz * PillarFrequency) : -1f;
        }
      }
    }

    Span<float> layerWidth = stackalloc float[Cell];
    Span<float> layerThreshold = stackalloc float[Cell];
    Span<float> layerPillar = stackalloc float[Cell];

    for (int cy = 0; cy < cubeLayers; cy++)
    {
      var yLow = cy * Cell;
      var yHigh = yLow + Cell - 1;

      if (yHigh < Underground.SolidFloor)
      {
        continue;
      }

      var width = Math.Max(TunnelWidth(yLow), TunnelWidth(yHigh));
      var threshold = Math.Min(CavernThreshold(yLow), CavernThreshold(yHigh));

      for (int ly = 0; ly < Cell; ly++)
      {
        layerWidth[ly] = TunnelWidth(yLow + ly);
        layerThreshold[ly] = CavernThreshold(yLow + ly);
        layerPillar[ly] = PillarThreshold(yLow + ly);
      }

      for (int cz = 0; cz < Corners - 1; cz++)
      {
        for (int cx = 0; cx < Corners - 1; cx++)
        {
          var i000 = (cy * Corners + cz) * Corners + cx;
          var i100 = i000 + 1;
          var i010 = i000 + Corners;
          var i110 = i010 + 1;
          var i001 = i000 + Corners * Corners;
          var i101 = i001 + 1;
          var i011 = i001 + Corners;
          var i111 = i011 + 1;

          var aMin = Min8(a, i000, i100, i010, i110, i001, i101, i011, i111);
          var aMax = Max8(a, i000, i100, i010, i110, i001, i101, i011, i111);
          var bMin = Min8(b, i000, i100, i010, i110, i001, i101, i011, i111);
          var bMax = Max8(b, i000, i100, i010, i110, i001, i101, i011, i111);
          var cMax = Max8(c, i000, i100, i010, i110, i001, i101, i011, i111);
          var cMin = Min8(c, i000, i100, i010, i110, i001, i101, i011, i111);

          var a0 = a[i000]; var a1 = a[i100]; var a2 = a[i010]; var a3 = a[i110]; var a4 = a[i001]; var a5 = a[i101]; var a6 = a[i011]; var a7 = a[i111];
          var b0 = b[i000]; var b1 = b[i100]; var b2 = b[i010]; var b3 = b[i110]; var b4 = b[i001]; var b5 = b[i101]; var b6 = b[i011]; var b7 = b[i111];
          var d0 = d[i000]; var d1 = d[i100]; var d2 = d[i010]; var d3 = d[i110]; var d4 = d[i001]; var d5 = d[i101]; var d6 = d[i011]; var d7 = d[i111];
          var c0 = c[i000]; var c1 = c[i100]; var c2 = c[i010]; var c3 = c[i110]; var c4 = c[i001]; var c5 = c[i101]; var c6 = c[i011]; var c7 = c[i111];

          var tunnels = aMin <= width && aMax >= -width && bMin <= width && bMax >= -width;
          var caverns = cMax > threshold;

          // Blending the corner values can never leave their range: no corner range reaching a cave means no cave in the cell.
          if (!tunnels && !caverns)
          {
            continue;
          }

          // Pillars: where the pillar noise is above its threshold the rock stays, open hall or not.
          var dMax = Max8(d, i000, i100, i010, i110, i001, i101, i011, i111);
          var dMin = Min8(d, i000, i100, i010, i110, i001, i101, i011, i111);
          var lowestPillarThreshold = layerPillar[0]; // the lowest layer has the lowest threshold
          var pillars = dMax > lowestPillarThreshold;

          if (pillars && dMin > layerPillar[Cell - 1])
          {
            continue; // the whole cell is inside a pillar
          }

          // A blend of values that are all inside a range stays inside it, so a cell whose corners are all inside a tunnel or a
          // cavern is open from corner to corner: no per-voxel noise needed.
          var wholeCell = (aMax < layerWidth[0] && aMin > -layerWidth[0] && bMax < layerWidth[0] && bMin > -layerWidth[0] && layerWidth[0] <= layerWidth[Cell - 1])
            || cMin > layerThreshold[0];
          var allOpen = !pillars && wholeCell && layerWidth[Cell - 1] >= layerWidth[0] && layerThreshold[Cell - 1] <= layerThreshold[0];

          for (int lz = 0; lz < Cell; lz++)
          {
            for (int lx = 0; lx < Cell; lx++)
            {
              var x = cx * Cell + lx;
              var z = cz * Cell + lz;
              var top = limit[x + z * size];

              for (int ly = 0; ly < Cell; ly++)
              {
                var layer = yLow + ly;

                if (layer < Underground.SolidFloor || layer > top || layer < floorTop[x + z * size])
                {
                  continue;
                }

                if (allOpen)
                {
                  chunk.Set(x, layer, z, layer < Underground.LavaTop ? BlockIds.Magma : BlockIds.Air);
                  continue;
                }

                var fx = lx / (float)Cell;
                var fy = ly / (float)Cell;
                var fz = lz / (float)Cell;

                var open = false;

                if (caverns && Blend(c0, c1, c2, c3, c4, c5, c6, c7, fx, fy, fz) > layerThreshold[ly])
                {
                  open = true;
                }
                else if (tunnels)
                {
                  var w = layerWidth[ly];

                  open = MathF.Abs(Blend(a0, a1, a2, a3, a4, a5, a6, a7, fx, fy, fz)) < w
                    && MathF.Abs(Blend(b0, b1, b2, b3, b4, b5, b6, b7, fx, fy, fz)) < w;
                }

                if (open && pillars && Blend(d0, d1, d2, d3, d4, d5, d6, d7, fx, fy, fz) > layerPillar[ly])
                {
                  open = false;
                }

                if (open)
                {
                  chunk.Set(x, layer, z, layer < Underground.LavaTop ? BlockIds.Magma : BlockIds.Air);
                }
              }
            }
          }
        }
      }
    }
  }

  static float Min8(float[] v, int i0, int i1, int i2, int i3, int i4, int i5, int i6, int i7)
  {
    return MathF.Min(MathF.Min(MathF.Min(v[i0], v[i1]), MathF.Min(v[i2], v[i3])), MathF.Min(MathF.Min(v[i4], v[i5]), MathF.Min(v[i6], v[i7])));
  }

  static float Max8(float[] v, int i0, int i1, int i2, int i3, int i4, int i5, int i6, int i7)
  {
    return MathF.Max(MathF.Max(MathF.Max(v[i0], v[i1]), MathF.Max(v[i2], v[i3])), MathF.Max(MathF.Max(v[i4], v[i5]), MathF.Max(v[i6], v[i7])));
  }

  static float Blend(float v000, float v100, float v010, float v110, float v001, float v101, float v011, float v111, float fx, float fy, float fz)
  {
    // Corner (x, z, y) layout: "010" is +z and "001" is +y.
    var x00 = v000 + (v100 - v000) * fx;
    var x10 = v010 + (v110 - v010) * fx;
    var x01 = v001 + (v101 - v001) * fx;
    var x11 = v011 + (v111 - v011) * fx;

    var z0 = x00 + (x10 - x00) * fz;
    var z1 = x01 + (x11 - x01) * fz;

    return z0 + (z1 - z0) * fy;
  }
}
