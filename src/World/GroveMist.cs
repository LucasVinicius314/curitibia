using Curitibia.Core;
using Godot;

namespace Curitibia.World;

/// <summary>
/// The air of the Titan Grove: a cool teal mist (volumetric fog) that thickens as you walk in and thins as you leave, lit by the
/// sun through the gaps in the canopy so that shafts of light cut through it. It only runs where the biome is the grove and the
/// player is on the surface; elsewhere volumetric fog is off and costs nothing. The mist fades in and out over a few seconds, so
/// crossing a border is not a switch.
/// </summary>
public partial class GroveMist : Node
{
  /// <summary>Seconds for the mist to go from nothing to full (and back).</summary>
  const float FadeSeconds = 4f;

  static readonly Color MistAlbedo = new(0.50f, 0.78f, 0.74f);
  static readonly Color MistGlow = new(0.012f, 0.06f, 0.055f);

  [Export] public WorldEnvironment Environment { get; set; } = null!;
  [Export] public DirectionalLight3D Sun { get; set; } = null!;

  /// <summary>0 outside the grove, 1 deep in it (eased).</summary>
  public static float Amount { get; private set; }

  float target;
  float checkTimer;

  public override void _Process(double delta)
  {
    var dt = (float)delta;

    checkTimer -= dt;

    if (checkTimer <= 0f)
    {
      checkTimer = 0.25f;
      target = InGrove() ? 1f : 0f;
    }

    var previous = Amount;

    Amount = Mathf.MoveToward(Amount, target, dt / FadeSeconds);

    if (Amount == previous && Amount != 0f && Amount != 1f)
    {
      return;
    }

    Apply();
  }

  static bool InGrove()
  {
    var terrain = Terrain.Instance;
    var camera = Engine.GetMainLoop() is SceneTree tree ? tree.Root.GetViewport().GetCamera3D() : null;

    if (terrain == null || camera == null)
    {
      return false;
    }

    var position = camera.GlobalPosition;

    return terrain.UndergroundAt(position) == null && terrain.BiomeAt(position) is { Id: BiomeId.Grove };
  }

  void Apply()
  {
    var environment = Environment.Environment;
    var on = Amount > 0.01f;

    environment.VolumetricFogEnabled = on;

    if (!on)
    {
      return;
    }

    environment.VolumetricFogDensity = 0.016f * Amount;
    environment.VolumetricFogAlbedo = MistAlbedo;
    environment.VolumetricFogEmission = MistGlow * Amount;
    environment.VolumetricFogEmissionEnergy = 1f;
    environment.VolumetricFogLength = 220f;
    environment.VolumetricFogAnisotropy = 0.7f; // light scatters forward: shafts toward the sun
    environment.VolumetricFogAmbientInject = 0.35f * Amount;
    environment.VolumetricFogSkyAffect = 0.4f;
    environment.VolumetricFogDetailSpread = 2f;
    Sun.LightVolumetricFogEnergy = 2.2f;
  }
}
