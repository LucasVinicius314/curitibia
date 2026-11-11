using System;
using System.Collections.Generic;
using Godot;

namespace Curitibia.World;

/// <summary>
/// The crack pictures drawn over a block while it is being mined: ten stages of the same cracks growing from the edges toward
/// the middle, built once (a few random walks, revealed pixel by pixel) into unshaded, translucent materials that are only
/// swapped, never created per frame.
/// </summary>
public static class BreakCracks
{
  public const int Stages = 10;

  const int Size = 16;

  static StandardMaterial3D[]? materials;

  /// <summary>The material for a stage (1 to <see cref="Stages"/>).</summary>
  public static StandardMaterial3D Stage(int stage)
  {
    materials ??= Build();

    return materials[Math.Clamp(stage, 1, Stages) - 1];
  }

  static StandardMaterial3D[] Build()
  {
    var rng = new Random(1337);
    var order = new List<(int X, int Y)>();
    var seen = new HashSet<(int, int)>();
    var starts = new (int X, int Y)[] { (0, 4), (15, 10), (6, 0), (10, 15), (0, 12), (15, 3), (12, 0), (3, 15) };
    var walks = new List<List<(int X, int Y)>>();

    // Each crack wanders from an edge toward the middle.
    foreach (var (sx, sy) in starts)
    {
      var walk = new List<(int X, int Y)>();
      int x = sx, y = sy;

      for (int step = 0; step < 11; step++)
      {
        walk.Add((x, y));

        var dx = Math.Sign(Size / 2 - x);
        var dy = Math.Sign(Size / 2 - y);

        if (rng.Next(3) == 0)
        {
          dx = rng.Next(-1, 2);
        }

        if (rng.Next(3) == 0)
        {
          dy = rng.Next(-1, 2);
        }

        x = Math.Clamp(x + dx, 0, Size - 1);
        y = Math.Clamp(y + dy, 0, Size - 1);
      }

      walks.Add(walk);
    }

    // Reveal order: every walk's first pixel, then every walk's second, and so on: the cracks creep in together.
    for (int step = 0; step < 11; step++)
    {
      foreach (var walk in walks)
      {
        if (seen.Add(walk[step]))
        {
          order.Add(walk[step]);
        }
      }
    }

    var result = new StandardMaterial3D[Stages];

    for (int stage = 1; stage <= Stages; stage++)
    {
      var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
      var shown = (int)MathF.Ceiling(order.Count * stage / (float)Stages);

      for (int i = 0; i < shown; i++)
      {
        image.SetPixel(order[i].X, order[i].Y, new Color(0.02f, 0.02f, 0.02f, 0.78f));
      }

      result[stage - 1] = new StandardMaterial3D
      {
        AlbedoTexture = ImageTexture.CreateFromImage(image),
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
      };
    }

    return result;
  }
}
