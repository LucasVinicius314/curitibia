using System;

namespace Curitibia.Core;

/// <summary>
/// Seeded 2D gradient (Perlin) noise in plain C#: deterministic on every peer, safe to call from worker threads and
/// unit-testable (no engine objects). Output is roughly in [-1, 1].
/// </summary>
public sealed class GradientNoise
{
  readonly byte[] perm = new byte[512];

  public GradientNoise(int seed)
  {
    var p = new byte[256];

    for (int i = 0; i < 256; i++)
    {
      p[i] = (byte)i;
    }

    // splitmix-style generator: the sequence is fully defined here, so it cannot change between runtimes.
    var state = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x1234567UL;

    for (int i = 255; i > 0; i--)
    {
      state += 0x9E3779B97F4A7C15UL;
      var z = state;
      z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
      z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
      z ^= z >> 31;

      var j = (int)(z % (ulong)(i + 1));
      (p[i], p[j]) = (p[j], p[i]);
    }

    for (int i = 0; i < 512; i++)
    {
      perm[i] = p[i & 255];
    }
  }

  static float Fade(float t)
  {
    return t * t * t * (t * (t * 6 - 15) + 10);
  }

  static float Grad(int hash, float x, float y)
  {
    // 8 gradient directions
    switch (hash & 7)
    {
      case 0: return x + y;
      case 1: return -x + y;
      case 2: return x - y;
      case 3: return -x - y;
      case 4: return x;
      case 5: return -x;
      case 6: return y;
      default: return -y;
    }
  }

  public float Sample(float x, float y)
  {
    var xi = (int)MathF.Floor(x);
    var yi = (int)MathF.Floor(y);

    var xf = x - xi;
    var yf = y - yi;

    var xm = xi & 255;
    var ym = yi & 255;

    var u = Fade(xf);
    var v = Fade(yf);

    var aa = perm[perm[xm] + ym];
    var ab = perm[perm[xm] + ym + 1];
    var ba = perm[perm[xm + 1] + ym];
    var bb = perm[perm[xm + 1] + ym + 1];

    var x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
    var x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);

    return Lerp(x1, x2, v);
  }

  /// <summary>
  /// Seeded 3D gradient noise (improved Perlin), roughly in [-1, 1]. For caves. Same guarantees as the 2D version: plain C#,
  /// deterministic, safe on any thread.
  /// </summary>
  public float Sample(float x, float y, float z)
  {
    var xi = (int)MathF.Floor(x);
    var yi = (int)MathF.Floor(y);
    var zi = (int)MathF.Floor(z);

    var xf = x - xi;
    var yf = y - yi;
    var zf = z - zi;

    var xm = xi & 255;
    var ym = yi & 255;
    var zm = zi & 255;

    var u = Fade(xf);
    var v = Fade(yf);
    var w = Fade(zf);

    var a = perm[xm] + ym;
    var aa = perm[a] + zm;
    var ab = perm[a + 1] + zm;
    var b = perm[xm + 1] + ym;
    var ba = perm[b] + zm;
    var bb = perm[b + 1] + zm;

    return Lerp(
      Lerp(
        Lerp(Grad(perm[aa], xf, yf, zf), Grad(perm[ba], xf - 1, yf, zf), u),
        Lerp(Grad(perm[ab], xf, yf - 1, zf), Grad(perm[bb], xf - 1, yf - 1, zf), u),
        v),
      Lerp(
        Lerp(Grad(perm[aa + 1], xf, yf, zf - 1), Grad(perm[ba + 1], xf - 1, yf, zf - 1), u),
        Lerp(Grad(perm[ab + 1], xf, yf - 1, zf - 1), Grad(perm[bb + 1], xf - 1, yf - 1, zf - 1), u),
        v),
      w);
  }

  static float Grad(int hash, float x, float y, float z)
  {
    // 12 edge directions of a cube (Ken Perlin's improved noise)
    var h = hash & 15;
    var u = h < 8 ? x : y;
    var v = h < 4 ? y : h == 12 || h == 14 ? x : z;

    return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
  }

  static float Lerp(float a, float b, float t)
  {
    return a + (b - a) * t;
  }
}
