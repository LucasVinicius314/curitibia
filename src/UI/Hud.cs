using System.Collections.Generic;
using System.Linq;
using Curitibia.Core;
using Curitibia.Net;
using Curitibia.Player;
using Curitibia.World;
using Godot;

namespace Curitibia.UI;

/// <summary>
/// In-game overlay. The scene holds the always-on labels (FPS, hit points, room, selected block, crosshair, death banner);
/// the rest is built in code from the shared theme: a toast line for session news, a roster (hold Tab) listing everyone in
/// the room, a "Joining" card while connecting, and the pause menu (Esc / Start) with the big room code, the players and
/// Resume / Leave. It observes the game; nothing in the game knows it exists.
/// </summary>
public partial class Hud : CanvasLayer
{
  const string MenuScene = "res://scenes/menu.tscn";

  [Export] public Label InfoLabel { get; set; } = null!;
  [Export] public Label BlockLabel { get; set; } = null!;
  [Export] public Label HealthLabel { get; set; } = null!;
  [Export] public Label BannerLabel { get; set; } = null!;
  [Export] public Label RoomLabel { get; set; } = null!;

  readonly Dictionary<int, Label> rosterHp = new();
  readonly Dictionary<int, Label> pauseHp = new();
  Dictionary<int, string> lastRoster = new();

  NetSession session = null!;
  PlayerController? player;

  Control root = null!;
  PanelContainer toastPanel = null!;
  Label toastLabel = null!;
  Tween? toastTween;
  PanelContainer rosterPanel = null!;
  VBoxContainer rosterRows = null!;
  Control connecting = null!;
  Label connectingLabel = null!;
  Control pause = null!;
  Label pauseCode = null!;
  Control pauseRoom = null!;
  VBoxContainer pauseRows = null!;
  Button resumeButton = null!;
  Button leaveButton = null!;

  double elapsed;
  double textTimer;
  int physicsTicks;
  int physicsPerSecond;
  bool pauseShown;

  public override void _Ready()
  {
    session = NetSession.Instance;

    root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = Ui.Theme };
    root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
    AddChild(root);

    BuildToast();
    BuildRoster();
    BuildConnecting();
    BuildPause();

    session.StatusChanged += OnStatus;
    session.Failed += OnFailed;
    session.RosterChanged += OnRosterChanged;

    OnRosterChanged();
  }

  public override void _ExitTree()
  {
    session.StatusChanged -= OnStatus;
    session.Failed -= OnFailed;
    session.RosterChanged -= OnRosterChanged;
  }

  public void Bind(PlayerController localPlayer)
  {
    player = localPlayer;
    localPlayer.Interactor.SelectionChanged += OnSelectionChanged;
  }

  void OnSelectionChanged(byte block)
  {
    BlockLabel.Text = $"[{BlockIds.DisplayName(block)}]  1-{BlockIds.Placeable.Length} / wheel to change";
  }

  // ------------------------------------------------------------------ building

  void BuildToast()
  {
    toastPanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
    toastPanel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
    toastPanel.GrowHorizontal = Control.GrowDirection.Both;
    toastPanel.Position = new Vector2(0f, 14f);
    toastPanel.OffsetTop = 14f;

    toastLabel = Ui.Label("", null, HorizontalAlignment.Center);
    toastPanel.AddChild(toastLabel);
    root.AddChild(toastPanel);
  }

  void BuildRoster()
  {
    rosterPanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
    rosterPanel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
    rosterPanel.GrowHorizontal = Control.GrowDirection.Begin;
    rosterPanel.OffsetTop = 48f;
    rosterPanel.OffsetRight = -16f;
    rosterPanel.CustomMinimumSize = new Vector2(280f, 0f);

    var column = Ui.Column(8);
    column.AddChild(Ui.Label("Players", Ui.Subtle));
    rosterRows = Ui.Column(6);
    column.AddChild(rosterRows);
    rosterPanel.AddChild(column);
    root.AddChild(rosterPanel);
  }

  void BuildConnecting()
  {
    connecting = CenteredCard(out var column, 420f);
    column.AddChild(Ui.Label("Joining room", Ui.Heading, HorizontalAlignment.Center));
    connectingLabel = Ui.Paragraph("Connecting...", Ui.Subtle, HorizontalAlignment.Center);
    column.AddChild(connectingLabel);

    var cancel = Ui.Button("Cancel", Ui.Danger);
    cancel.Pressed += LeaveToMenu;
    column.AddChild(cancel);

    connecting.Visible = false;
  }

  void BuildPause()
  {
    pause = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
    pause.SetAnchorsPreset(Control.LayoutPreset.FullRect);

    var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = Control.MouseFilterEnum.Ignore };
    dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
    pause.AddChild(dim);

    var center = new CenterContainer();
    center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
    pause.AddChild(center);

    var card = new PanelContainer { CustomMinimumSize = new Vector2(480f, 0f) };
    center.AddChild(card);

    var column = Ui.Column(12);
    card.AddChild(column);

    column.AddChild(Ui.Label("Paused", Ui.Heading, HorizontalAlignment.Center));

    var room = Ui.Column(6);
    room.AddChild(Ui.Label("Room code", Ui.Subtle, HorizontalAlignment.Center));
    pauseCode = Ui.Label("", Ui.Mono, HorizontalAlignment.Center);
    room.AddChild(pauseCode);

    var copy = Ui.Button("Copy code");
    copy.Pressed += () =>
    {
      DisplayServer.ClipboardSet(session.RoomCode ?? "");
      Toast("Room code copied.", Ui.Good);
    };
    room.AddChild(copy);
    room.AddChild(Ui.Paragraph("Friends can join with this code, or find the room in their list if you chose to show it to them.", Ui.Subtle, HorizontalAlignment.Center));
    pauseRoom = room;
    column.AddChild(room);

    column.AddChild(Ui.Separator());
    column.AddChild(Ui.Label("Players", Ui.Subtle));
    pauseRows = Ui.Column(6);
    column.AddChild(pauseRows);
    column.AddChild(Ui.Gap(4f));

    resumeButton = Ui.Button("Resume", Ui.Primary);
    resumeButton.Pressed += () => Input.MouseMode = Input.MouseModeEnum.Captured;
    column.AddChild(resumeButton);

    leaveButton = Ui.Button("Quit to menu", Ui.Danger);
    leaveButton.Pressed += LeaveToMenu;
    column.AddChild(leaveButton);

    root.AddChild(pause);
  }

  Control CenteredCard(out VBoxContainer column, float width)
  {
    var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
    center.SetAnchorsPreset(Control.LayoutPreset.FullRect);

    var card = new PanelContainer { CustomMinimumSize = new Vector2(width, 0f) };
    center.AddChild(card);

    column = Ui.Column(12);
    card.AddChild(column);
    root.AddChild(center);

    return center;
  }

  // ------------------------------------------------------------------ session events

  void OnStatus(string message)
  {
    Toast(message, Ui.Dim);
  }

  void OnFailed(string message)
  {
    // A host whose room could not be opened keeps playing; a client that never got in is sent back to the menu by Main.
    Toast(session.IsHost ? $"{message} You are playing solo." : message, Ui.Bad, 6f);
  }

  void OnRosterChanged()
  {
    var current = session.Roster.ToDictionary(kv => kv.Key, kv => kv.Value);

    // Say who came and who went (not for the very first roster).
    if (lastRoster.Count > 0)
    {
      foreach (var (_, name) in current.Where(kv => !lastRoster.ContainsKey(kv.Key)))
      {
        Toast($"{name} joined", Ui.Good);
      }

      foreach (var (_, name) in lastRoster.Where(kv => !current.ContainsKey(kv.Key)))
      {
        Toast($"{name} left", Ui.Warn);
      }
    }

    lastRoster = current;
    FillRows(rosterRows, rosterHp);
    FillRows(pauseRows, pauseHp);
  }

  void FillRows(VBoxContainer rows, Dictionary<int, Label> hpLabels)
  {
    foreach (var child in rows.GetChildren())
    {
      child.QueueFree();
    }

    hpLabels.Clear();

    var own = Multiplayer.GetUniqueId();

    foreach (var (peer, name) in lastRoster.OrderBy(kv => kv.Key == 1 ? 0 : 1).ThenBy(kv => kv.Value))
    {
      var row = Ui.Row(10);
      var label = Ui.Label(peer == own ? $"{name} (you)" : name);
      label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
      row.AddChild(label);

      if (peer == 1)
      {
        row.AddChild(Ui.Label("host", Ui.Subtle));
      }

      var hp = Ui.Label("", Ui.Subtle);
      hpLabels[peer] = hp;
      row.AddChild(hp);
      rows.AddChild(row);
    }
  }

  void Toast(string message, Color color, float seconds = 3.5f)
  {
    toastLabel.Text = message;
    Ui.Tint(toastLabel, color);
    toastPanel.Visible = true;
    toastPanel.Modulate = Colors.White;

    toastTween?.Kill();
    toastTween = CreateTween();
    toastTween.TweenInterval(seconds);
    toastTween.TweenProperty(toastPanel, "modulate:a", 0f, 0.5f);
    toastTween.TweenCallback(Callable.From(() => toastPanel.Visible = false));
  }

  void LeaveToMenu()
  {
    NetSession.Instance.Leave();
    Input.MouseMode = Input.MouseModeEnum.Visible;
    GetTree().ChangeSceneToFile(MenuScene);
  }

  // ------------------------------------------------------------------ per frame

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

    var hasPlayer = player != null && IsInstanceValid(player);
    BannerLabel.Visible = hasPlayer && player!.Health.IsDead;

    var paused = hasPlayer && Input.MouseMode != Input.MouseModeEnum.Captured;
    pause.Visible = paused;

    if (paused && !pauseShown)
    {
      pauseCode.Text = session.RoomCode ?? "";
      pauseRoom.Visible = session.RoomCode != null;
      leaveButton.Text = session.IsOnline ? "Leave room" : "Quit to menu";
      resumeButton.GrabFocus(); // a controller can press it with A
    }

    pauseShown = paused;

    connecting.Visible = !hasPlayer && session.Kind == SessionKind.Client;

    if (connecting.Visible && session.RoomCode != null)
    {
      connectingLabel.Text = $"Room {session.RoomCode}. Connecting to the host...";
    }

    rosterPanel.Visible = session.IsOnline && !paused && Input.IsActionPressed(Actions.Roster);

    if (textTimer < 0.25)
    {
      return;
    }

    textTimer = 0;

    var chunks = Terrain.Instance?.LoadedChunkCount ?? 0;
    InfoLabel.Text = $"FPS {Engine.GetFramesPerSecond()}   UPS {physicsPerSecond}   chunks {chunks}";

    if (hasPlayer)
    {
      HealthLabel.Text = $"HP {Mathf.CeilToInt(player!.Health.Hp)} / {Mathf.CeilToInt(player.Health.MaxHp)}";
    }

    var code = session.RoomCode;
    var idle = session.IsOnline ? "Online" : "Solo";
    RoomLabel.Text = code != null ? $"Room {code}" : idle;

    UpdateHp(rosterHp);
    UpdateHp(pauseHp);
  }

  void UpdateHp(Dictionary<int, Label> labels)
  {
    foreach (var other in PlayerController.All)
    {
      if (int.TryParse(other.Name, out var peer) && labels.TryGetValue(peer, out var label))
      {
        label.Text = other.Health.IsDead ? "down" : $"HP {Mathf.CeilToInt(other.Health.Hp)}";
      }
    }
  }
}
