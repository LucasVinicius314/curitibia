using Curitibia.Player;
using Godot;

namespace Curitibia.Items;

/// <summary>
/// Shows the item a player holds: on the body for everyone (so others see it, and you do in third person) and, for the local
/// player in first person, as a view model on the camera. It follows the player's held item id, which is replicated, and
/// rebuilds the models only when that id changes.
/// </summary>
public partial class HeldItemDisplay : Node
{
  [Export] public PlayerController Player { get; set; } = null!;
  [Export] public ItemUser User { get; set; } = null!;
  [Export] public Camera3D Camera { get; set; } = null!;

  HeldModel body = null!;
  HeldModel? view;
  int shownId = -1;
  bool hasModel;

  public override void _Ready()
  {
    body = new HeldModel { Position = new Vector3(0.42f, -0.12f, -0.12f) };
    Player.Visual.HeldItemAnchor.AddChild(body);

    if (Player.IsLocal)
    {
      view = new HeldModel { Position = new Vector3(0.3f, -0.28f, -0.42f) };
      Camera.AddChild(view);
    }

    User.Used += OnUsed;
  }

  public override void _ExitTree()
  {
    User.Used -= OnUsed;
  }

  void OnUsed()
  {
    body.Play();
    view?.Play();
  }

  public override void _Process(double delta)
  {
    var id = Player.HeldItemId;

    if (id != shownId)
    {
      Rebuild(id);
    }

    if (view != null)
    {
      view.Visible = hasModel && !Player.ThirdPerson && !Player.Health.IsDead;
    }
  }

  void Rebuild(int id)
  {
    shownId = id;

    var model = ItemRegistry.Get(id)?.Model;
    hasModel = model != null;
    body.Visible = hasModel;

    if (model == null)
    {
      return;
    }

    body.SetModel(model);
    view?.SetModel(model);

    // The new meshes must follow the body's shadow-only mode (first person).
    Player.Visual.RefreshShadowMode();
  }
}
