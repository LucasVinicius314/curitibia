using Curitibia.Player;
using Curitibia.World;
using Godot;

namespace Curitibia.UI;

/// <summary>
/// In-game overlay: FPS and physics ticks per second, loaded chunks, the selected block and a crosshair. It observes the
/// game; nothing in the game knows it exists.
/// </summary>
public partial class Hud : CanvasLayer
{
  [Export] public Label InfoLabel { get; set; } = null!;
  [Export] public Label BlockLabel { get; set; } = null!;

  double elapsed;
  double textTimer;
  int physicsTicks;
  int physicsPerSecond;

  public void Bind(PlayerController player)
  {
    player.Interactor.SelectionChanged += OnSelectionChanged;
  }

  void OnSelectionChanged(byte block)
  {
    BlockLabel.Text = $"[{BlockIds.DisplayName(block)}]  1-{BlockIds.Placeable.Length} / wheel to change";
  }

  public override void _PhysicsProcess(double delta)
  {
    physicsTicks++;
  }

  public override void _Process(double delta)
  {
    elapsed += delta;
    textTimer += delta;

    if (elapsed >= 1.0)
    {
      physicsPerSecond = physicsTicks;
      physicsTicks = 0;
      elapsed = 0;
    }

    if (textTimer < 0.25)
    {
      return;
    }

    textTimer = 0;

    var chunks = Terrain.Instance?.LoadedChunkCount ?? 0;
    InfoLabel.Text = $"FPS {Engine.GetFramesPerSecond()}   UPS {physicsPerSecond}   chunks {chunks}";
  }
}
