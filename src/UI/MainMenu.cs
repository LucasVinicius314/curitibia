using System.Collections.Generic;
using Curitibia.Net;
using Godot;
using P2PSignaling;

namespace Curitibia.UI;

/// <summary>
/// Start screen: play solo, host a room (optionally visible to friends), join by code, or join a room one of your friends
/// is hosting. It only asks NetSession to prepare a start request and changes to the world scene.
/// </summary>
public partial class MainMenu : Control
{
  const string WorldScene = "res://scenes/main.tscn";

  [Export] public LineEdit NameEdit { get; set; } = null!;
  [Export] public Button SoloButton { get; set; } = null!;
  [Export] public Button HostButton { get; set; } = null!;
  [Export] public CheckBox FriendsCheck { get; set; } = null!;
  [Export] public LineEdit CodeEdit { get; set; } = null!;
  [Export] public Button JoinButton { get; set; } = null!;
  [Export] public Button RefreshButton { get; set; } = null!;
  [Export] public VBoxContainer FriendList { get; set; } = null!;
  [Export] public LineEdit IdEdit { get; set; } = null!;
  [Export] public Button CopyIdButton { get; set; } = null!;
  [Export] public Label StatusLabel { get; set; } = null!;

  NetSession session = null!;

  public override void _Ready()
  {
    Input.MouseMode = Input.MouseModeEnum.Visible;
    session = NetSession.Instance;

    NameEdit.Text = session.PlayerName;
    IdEdit.Text = P2PIdentity.OwnId;

    SoloButton.Pressed += () => Start(new StartRequest(SessionKind.Solo));
    HostButton.Pressed += () => Start(new StartRequest(SessionKind.Host, ListForFriends: FriendsCheck.ButtonPressed));
    JoinButton.Pressed += JoinTyped;
    CodeEdit.TextSubmitted += _ => JoinTyped();
    RefreshButton.Pressed += Refresh;
    CopyIdButton.Pressed += () =>
    {
      DisplayServer.ClipboardSet(IdEdit.Text);
      StatusLabel.Text = "Friend ID copied. Send it to a friend; add theirs to the friends file.";
    };

    session.FriendRoomsReceived += OnFriendRooms;
    session.Failed += OnFailed;
    session.StatusChanged += OnStatus;

    Refresh();
  }

  public override void _ExitTree()
  {
    session.FriendRoomsReceived -= OnFriendRooms;
    session.Failed -= OnFailed;
    session.StatusChanged -= OnStatus;
  }

  void Start(StartRequest request)
  {
    session.PlayerName = NameEdit.Text.Trim().Length > 0 ? NameEdit.Text.Trim() : session.PlayerName;
    session.Pending = request;
    GetTree().ChangeSceneToFile(WorldScene);
  }

  void JoinTyped()
  {
    var code = CodeEdit.Text.Trim();

    if (code.Length == 0)
    {
      StatusLabel.Text = "Type a room code first.";
      return;
    }

    Start(new StartRequest(SessionKind.Client, code));
  }

  void Refresh()
  {
    StatusLabel.Text = "Looking for your friends' rooms...";
    session.PlayerName = NameEdit.Text.Trim().Length > 0 ? NameEdit.Text.Trim() : session.PlayerName;
    session.RefreshFriendRooms();
  }

  void OnFriendRooms(IReadOnlyList<FriendRoom> rooms)
  {
    foreach (var child in FriendList.GetChildren())
    {
      child.QueueFree();
    }

    var problems = session.FriendFileProblems;

    if (problems.Count > 0)
    {
      StatusLabel.Text = "Friends file: " + string.Join("; ", problems);
    }
    else
    {
      StatusLabel.Text = rooms.Count == 0
        ? $"No friend rooms right now. Friends file: {P2PIdentity.HomeFolder}"
        : $"{rooms.Count} friend room(s).";
    }

    foreach (var room in rooms)
    {
      var who = room.FriendLabel.Length > 0 ? room.FriendLabel : room.HostName;
      var button = new Button { Text = $"{who}   ({room.Players}/{room.MaxPlayers})   {room.Code}" };
      var code = room.Code;
      button.Pressed += () => Start(new StartRequest(SessionKind.Client, code));
      FriendList.AddChild(button);
    }
  }

  void OnFailed(string message)
  {
    StatusLabel.Text = message;
  }

  void OnStatus(string message)
  {
    StatusLabel.Text = message;
  }
}
