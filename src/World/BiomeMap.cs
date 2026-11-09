using System;
using Curitibia.Core;

namespace Curitibia.World;

/// <summary>
/// Where each biome lives: three slow noise fields (temperature, moisture, ruggedness) place every column in climate space,
/// and its distance to each biome's centre gives a smooth weight. The generator blends terrain height by those weights, so
/// two biomes meet in a slope instead of a cliff, while the strongest weight picks the surface blocks. A pure function of
/// (seed, column), engine-free.
/// </summary>
public sealed class BiomeMap
{
  /// <summary>Spread of a biome's influence in climate space; larger = softer borders, smaller = crisper ones.</summary>
  const float Spread = 0.09f;

  /// <summary>Ruggedness counts more than the other axes so mountains stand apart from every flat biome.</summary>
  const float RuggednessWeight = 1f;

  /// <summary>Radius in voxels of the ring the ruling biome is averaged over (225 m): the smallest feature that survives.</summary>
  const float DominanceRadius = 900f;

  /// <summary>Around the world origin the ruggedness is damped so the spawn is never in the mountains.</summary>
  const float SpawnFlat = 800f;
  const float SpawnBlend = 700f;

  readonly GradientNoise temperature;
  readonly GradientNoise moisture;
  readonly GradientNoise ruggedness;
  readonly float frequency;

  public BiomeMap(int seed, float frequency)
  {
    temperature = new GradientNoise(seed + 7919);
    moisture = new GradientNoise(seed + 104729);
    ruggedness = new GradientNoise(seed + 1299709);
    this.frequency = frequency;
  }

  public static int Count => Biomes.All.Length;

  /// <summary>The three climate values (0 to 1) of a world column.</summary>
  public void Climate(int wx, int wz, out float t, out float m, out float r)
  {
    t = Unit(Field(temperature, wx, wz, 1f));
    m = Unit(Field(moisture, wx, wz, 1f));
    r = Unit(Field(ruggedness, wx, wz, 1.3f));
    r = MathF.Pow(r, 2.6f); // skewed low, so high ruggedness (mountains) is the rare end

    var dist = MathF.Sqrt((float)wx * wx + (float)wz * wz);
    var calm = Math.Clamp((dist - SpawnFlat) / SpawnBlend, 0f, 1f);

    r *= calm * calm * (3f - 2f * calm);
  }

  /// <summary>
  /// Fills <paramref name="weights"/> (one per biome, summing to 1; they drive the height blend) and returns the ruling
  /// biome, the strongest of them. The weights are averaged over a ring around the column, so a patch narrower than
  /// <see cref="DominanceRadius"/> cannot exist (thin bands and islands where the climate only briefly crosses into a biome
  /// fold into their neighbours), and the height and the surface blocks always agree on which biome is where.
  /// </summary>
  public BiomeId Weights(int wx, int wz, Span<float> weights)
  {
    return Weights(wx, wz, weights, out _);
  }

  /// <summary>
  /// As above, and also the ruggedness (0 to 1) averaged over the same ring: a smooth measure of how mountainous the country
  /// is that keeps rising toward the middle of a mountain region instead of saturating at its edge, which the terrain uses to
  /// raise the baseline across the whole biome.
  /// </summary>
  public BiomeId Weights(int wx, int wz, Span<float> weights, out float ruggedness)
  {
    Span<float> sample = stackalloc float[Biomes.All.Length];

    weights.Clear();
    ruggedness = 0f;

    foreach (var (ox, oz) in ring)
    {
      Blend(wx + ox, wz + oz, sample, out var r);
      ruggedness += r / ring.Length;

      for (int i = 0; i < weights.Length; i++)
      {
        weights[i] += sample[i] / ring.Length;
      }
    }

    var best = 0;

    for (int i = 1; i < weights.Length; i++)
    {
      if (weights[i] > weights[best])
      {
        best = i;
      }
    }

    return (BiomeId)best;
  }

  static readonly (int, int)[] ring = BuildRing();

  static (int, int)[] BuildRing()
  {
    var d = (int)(DominanceRadius * 0.7071f);
    var r = (int)DominanceRadius;

    return new (int, int)[] { (0, 0), (r, 0), (-r, 0), (0, r), (0, -r), (d, d), (d, -d), (-d, d), (-d, -d) };
  }

  /// <summary>The normalized weight of every biome at one column.</summary>
  void Blend(int wx, int wz, Span<float> weights, out float ruggedness)
  {
    Climate(wx, wz, out var t, out var m, out var r);
    ruggedness = r;

    var sum = 0f;
    var nearest = 0;
    var nearestD = float.MaxValue;

    for (int i = 0; i < Biomes.All.Length; i++)
    {
      var b = Biomes.All[i];
      var dt = t - b.Temperature;
      var dm = m - b.Moisture;
      var dr = (r - b.Ruggedness) * RuggednessWeight;
      var d2 = dt * dt + dm * dm + dr * dr;
      var w = MathF.Exp(-d2 / (2f * Spread));

      weights[i] = w;
      sum += w;

      if (d2 < nearestD)
      {
        nearestD = d2;
        nearest = i;
      }
    }

    // Far from every centre all weights can underflow; fall back to the nearest biome alone.
    if (sum < 1e-12f)
    {
      for (int i = 0; i < Biomes.All.Length; i++)
      {
        weights[i] = i == nearest ? 1f : 0f;
      }

      return;
    }

    for (int i = 0; i < Biomes.All.Length; i++)
    {
      weights[i] /= sum;
    }
  }

  public BiomeId BiomeAt(int wx, int wz)
  {
    Span<float> w = stackalloc float[Biomes.All.Length];

    return Weights(wx, wz, w);
  }

  float Field(GradientNoise noise, int wx, int wz, float scale)
  {
    var f = frequency * scale;

    return noise.Sample(wx * f + 0.5f, wz * f + 0.5f);
  }

  /// <summary>Perlin stays in a narrow band around 0; stretch it so the whole 0 to 1 climate range gets used.</summary>
  static float Unit(float v)
  {
    return Math.Clamp(0.5f + v * 1.4f, 0f, 1f);
  }
}
