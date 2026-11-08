using System;
using System.Collections.Generic;
using System.Linq;
using Curitibia.Core;
using Curitibia.Net;
using Godot;
using P2PSignaling;

namespace Curitibia.UI;

/// <summary>
/// The start screen, in pages: Main (name, solo, host, join, settings), Host (room options), Join (room code, your friends'
/// rooms, managing friends) and Settings (the server address). Built in code from the shared theme. It only asks NetSession
/// to prepare a start request and changes to the world scene; the world scene does the connecting, and sends you back here
/// with a message if that fails.
/// </summary>
public partial class MainMenu : Control
{
  const string WorldScene = "res://scenes/main.tscn";
  const float FriendRefreshSeconds = 8f;

  enum Page
  {
    Main,
    Host,
    Join,
    Settings,
  }

  readonly Dictionary<Page, Control> pages = new();
  readonly Timer refreshTimer = new() { WaitTime = FriendRefreshSeconds, OneShot = false };

  NetSession session = null!;
  Page current = Page.Main;
  Label status = null!;
  Label serverHint = null!;
  VBoxContainer banner = null!;
  VBoxContainer friendsPanel = null!;
  string? screenshotPath;
  int frames;

  // Main
  LineEdit nameField = null!;

  // Host
  CheckBox friendsCheck = null!;
  SpinBox maxPlayers = null!;
  LineEdit seedField = null!;

  // Join
  LineEdit codeField = null!;
  VBoxContainer friendRooms = null!;
  Label friendsStatus = null!;
  Label friendsList = null!;
  LineEdit addIdField = null!;
  LineEdit addNameField = null!;

  // Settings
  LineEdit serverField = null!;
  Label serverSource = null!;

  public override void _Ready()
  {
    Input.MouseMode = Input.MouseModeEnum.Visible;
    session = NetSession.Instance;

    Theme = Ui.Theme;
    SetAnchorsPreset(LayoutPreset.FullRect);

    var background = new ColorRect { Color = Ui.Background };
    background.SetAnchorsPreset(LayoutPreset.FullRect);
    AddChild(background);

    // A scroll container, so a page taller than a small window scrolls instead of being cut off.
    var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    scroll.SetAnchorsPreset(LayoutPreset.FullRect);
    AddChild(scroll);

    var center = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
    scroll.AddChild(center);

    var margin = new MarginContainer();
    margin.AddThemeConstantOverride("margin_top", 24);
    margin.AddThemeConstantOverride("margin_bottom", 24);
    center.AddChild(margin);

    var column = Ui.Column(14);
    column.CustomMinimumSize = new Vector2(640f, 0f);
    margin.AddChild(column);

    banner = Ui.Column(4);
    banner.AddChild(Ui.Label("CURITIBIA", Ui.Title, HorizontalAlignment.Center));
    banner.AddChild(Ui.Label("a small voxel co-op game", Ui.Subtle, HorizontalAlignment.Center));
    banner.AddChild(Ui.Gap(6f));
    column.AddChild(banner);

    var card = new PanelContainer();
    column.AddChild(card);

    var stack = new VBoxContainer();
    card.AddChild(stack);

    pages[Page.Main] = BuildMain();
    pages[Page.Host] = BuildHost();
    pages[Page.Join] = BuildJoin();
    pages[Page.Settings] = BuildSettings();

    foreach (var page in pages.Values)
    {
      stack.AddChild(page);
    }

    status = Ui.Paragraph("", Ui.Subtle, HorizontalAlignment.Center);
    status.CustomMinimumSize = new Vector2(0f, 48f);
    column.AddChild(status);

    AddChild(refreshTimer);
    refreshTimer.Timeout += RefreshFriendRooms;

    session.FriendRoomsReceived += OnFriendRooms;
    session.Failed += OnFailed;
    session.StatusChanged += OnStatus;

    ShowPage(Page.Main);

    if (session.LastError != null)
    {
      SetStatus(session.LastError, Ui.Bad);
      session.LastError = null;
    }

    // Dev flags for screenshots: --menu-page=main|host|join|settings and --screenshot=FILE.
    if (DevArgs.Get("menu-page") is { } name && Enum.TryParse<Page>(name, true, out var requested))
    {
      ShowPage(requested);
    }

    screenshotPath = DevArgs.Get("screenshot");

    // Scripted runs of an exported build cannot pass a scene path (release templates forbid it), so the menu itself starts
    // the world when asked: --solo, or the --net-host / --net-join=CODE flags (NetSession turns those into a pending request).
    if (session.Pending != null || DevArgs.Has("solo"))
    {
      var request = session.Pending ?? new StartRequest(SessionKind.Solo);
      CallDeferred(MethodName.AutoStart, Variant.From(request.Kind == SessionKind.Solo));
    }
  }

  void AutoStart(bool solo)
  {
    if (solo && session.Pending == null)
    {
      Start(new StartRequest(SessionKind.Solo));
    }
    else
    {
      GetTree().ChangeSceneToFile(WorldScene); // Pending is already set
    }
  }

  public override void _ExitTree()
  {
    session.FriendRoomsReceived -= OnFriendRooms;
    session.Failed -= OnFailed;
    session.StatusChanged -= OnStatus;
  }

  public override void _Process(double delta)
  {
    if (screenshotPath == null || ++frames < 40)
    {
      return;
    }

    GetViewport().GetTexture().GetImage().SavePng(screenshotPath);
    GD.Print($"Screenshot {screenshotPath}");
    screenshotPath = null;
    GetTree().Quit();
  }

  // ------------------------------------------------------------------ pages

  VBoxContainer BuildMain()
  {
    var page = Ui.Column(12);

    page.AddChild(Ui.Label("Your name", Ui.Subtle));
    nameField = Ui.Field("Name", 24);
    nameField.Text = PlayerProfile.Name;
    nameField.TextSubmitted += _ => ApplyName();
    nameField.FocusExited += ApplyName;
    page.AddChild(nameField);

    page.AddChild(Ui.Gap(4f));

    var solo = Ui.Button("Play solo", Ui.Primary);
    solo.Pressed += () => Start(new StartRequest(SessionKind.Solo));
    page.AddChild(solo);

    var host = Ui.Button("Host a room");
    host.Pressed += () => ShowPage(Page.Host);
    page.AddChild(host);

    var join = Ui.Button("Join a room");
    join.Pressed += () => ShowPage(Page.Join);
    page.AddChild(join);

    var row = Ui.Row();
    var settings = Ui.Button("Settings");
    settings.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    settings.Pressed += () => ShowPage(Page.Settings);
    row.AddChild(settings);

    var quit = Ui.Button("Quit");
    quit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    quit.Pressed += () => GetTree().Quit();
    row.AddChild(quit);
    page.AddChild(row);

    serverHint = Ui.Paragraph("", Ui.Subtle, HorizontalAlignment.Center);
    page.AddChild(serverHint);

    return page;
  }

  VBoxContainer BuildHost()
  {
    var page = Ui.Column(12);

    page.AddChild(Ui.Label("Host a room", Ui.Heading));
    page.AddChild(Ui.Paragraph("You run the world on your machine and friends connect straight to you. They join with a short code.", Ui.Subtle));
    page.AddChild(Ui.Gap(2f));

    friendsCheck = new CheckBox { Text = "Show this room to my friends", ButtonPressed = true };
    page.AddChild(friendsCheck);
    page.AddChild(Ui.Paragraph("Friends who have your friend ID in their list see the room without typing a code.", Ui.Subtle));

    var players = Ui.Row();
    players.AddChild(Ui.Label("Max players"));
    players.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
    maxPlayers = new SpinBox { MinValue = 2, MaxValue = 8, Value = 4, Step = 1, CustomMinimumSize = new Vector2(130f, 44f) };
    players.AddChild(maxPlayers);
    page.AddChild(players);

    var seed = Ui.Row();
    seed.AddChild(Ui.Label("World seed"));
    seedField = Ui.Field("random");
    seedField.CustomMinimumSize = new Vector2(200f, 44f);
    seedField.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
    seed.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
    seed.AddChild(seedField);
    page.AddChild(seed);

    page.AddChild(Ui.Gap(6f));

    var buttons = Ui.Row();
    var back = Ui.Button("Back", null, 120f);
    back.Pressed += () => ShowPage(Page.Main);
    buttons.AddChild(back);

    var start = Ui.Button("Start hosting", Ui.Primary);
    start.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    start.Pressed += StartHosting;
    buttons.AddChild(start);
    page.AddChild(buttons);

    return page;
  }

  VBoxContainer BuildJoin()
  {
    var page = Ui.Column(12);

    page.AddChild(Ui.Label("Join a room", Ui.Heading));

    var codeRow = Ui.Row();
    codeField = Ui.Field("ROOM CODE", 8);
    codeField.Alignment = HorizontalAlignment.Center;
    codeField.AddThemeFontSizeOverride("font_size", 26);
    codeField.CustomMinimumSize = new Vector2(0f, 56f);
    codeField.TextChanged += OnCodeChanged;
    codeField.TextSubmitted += _ => JoinTyped();
    codeRow.AddChild(codeField);

    var join = Ui.Button("Join", Ui.Primary, 130f);
    join.CustomMinimumSize = new Vector2(130f, 56f);
    join.Pressed += JoinTyped;
    codeRow.AddChild(join);
    page.AddChild(codeRow);

    page.AddChild(Ui.Separator());

    var header = Ui.Row();
    var title = Ui.Label("Your friends' rooms", Ui.Heading);
    title.AddThemeFontSizeOverride("font_size", 22);
    title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    header.AddChild(title);

    var refresh = Ui.Button("Refresh", null, 110f);
    refresh.Pressed += RefreshFriendRooms;
    header.AddChild(refresh);
    page.AddChild(header);

    friendsStatus = Ui.Paragraph("", Ui.Subtle);
    page.AddChild(friendsStatus);

    friendRooms = Ui.Column(8);
    page.AddChild(friendRooms);

    var buttons = Ui.Row();
    var back = Ui.Button("Back", null, 120f);
    back.Pressed += () => ShowPage(Page.Main);
    buttons.AddChild(back);

    var manage = Ui.Button("Manage friends");
    manage.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    manage.Pressed += () =>
    {
      friendsPanel.Visible = !friendsPanel.Visible;
      manage.Text = friendsPanel.Visible ? "Hide friends" : "Manage friends";
    };
    buttons.AddChild(manage);
    page.AddChild(buttons);

    // Friends: who is on your list, your own ID to give out, and adding one. Folded away until asked for.
    friendsPanel = Ui.Column(10);
    friendsPanel.Visible = false;
    friendsPanel.AddChild(Ui.Separator());

    friendsList = Ui.Paragraph("");
    friendsPanel.AddChild(friendsList);

    var idRow = Ui.Row();
    idRow.AddChild(Ui.Label("Your friend ID", Ui.Subtle));
    var idField = Ui.Field();
    idField.Editable = false;
    idField.Text = P2PIdentity.OwnId;
    idRow.AddChild(idField);
    var copy = Ui.Button("Copy", null, 90f);
    copy.Pressed += () =>
    {
      DisplayServer.ClipboardSet(P2PIdentity.OwnId);
      SetStatus("Friend ID copied. Send it to a friend so they can add you.", Ui.Good);
    };
    idRow.AddChild(copy);
    friendsPanel.AddChild(idRow);

    var addRow = Ui.Row();
    addIdField = Ui.Field("Friend's ID");
    addNameField = Ui.Field("Name (optional)");
    addNameField.CustomMinimumSize = new Vector2(170f, 44f);
    addNameField.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
    addRow.AddChild(addIdField);
    addRow.AddChild(addNameField);
    var add = Ui.Button("Add", null, 90f);
    add.Pressed += AddFriend;
    addRow.AddChild(add);
    friendsPanel.AddChild(addRow);

    var folder = Ui.Button("Open the friends file folder");
    folder.Pressed += () => OS.ShellOpen(P2PIdentity.HomeFolder);
    friendsPanel.AddChild(folder);
    page.AddChild(friendsPanel);

    return page;
  }

  VBoxContainer BuildSettings()
  {
    var page = Ui.Column(12);

    page.AddChild(Ui.Label("Settings", Ui.Heading));

    page.AddChild(SettingsPanel.Build(showControls: false));

    page.AddChild(Ui.Gap(4f));
    page.AddChild(Ui.Label("Server address", Ui.Subtle));

    var row = Ui.Row();
    serverField = Ui.Field("https://your-server.example");
    serverField.TextSubmitted += _ => SaveServer();
    row.AddChild(serverField);

    var save = Ui.Button("Save", Ui.Primary, 110f);
    save.Pressed += SaveServer;
    row.AddChild(save);
    page.AddChild(row);

    serverSource = Ui.Paragraph("", Ui.Subtle);
    page.AddChild(serverSource);

    page.AddChild(Ui.Paragraph(
      "This is the signaling server that introduces players to each other. It is shared by every game on this machine that uses it, and only has to be set once. Game traffic never goes through it.",
      Ui.Subtle));

    page.AddChild(Ui.Gap(6f));

    var back = Ui.Button("Back");
    back.Pressed += () => ShowPage(Page.Main);
    page.AddChild(back);

    return page;
  }

  // ------------------------------------------------------------------ navigation

  void ShowPage(Page page)
  {
    current = page;

    foreach (var (key, control) in pages)
    {
      control.Visible = key == page;
    }

    UpdateServerUi();
    refreshTimer.Stop();
    banner.Visible = page == Page.Main; // the big title only on the first page, to leave room

    switch (page)
    {
      case Page.Join:
        RefreshFriendList();
        RefreshFriendRooms();
        refreshTimer.Start();
        codeField.GrabFocus();
        break;

      case Page.Host:
        seedField.Text = "";
        break;

      case Page.Settings:
        serverField.GrabFocus();
        break;
    }

    // Controller users land on the first sensible button of the page.
    if (page != Page.Join && page != Page.Settings)
    {
      FocusFirstButton(pages[page]);
    }
  }

  static void FocusFirstButton(Node root)
  {
    foreach (var child in root.GetChildren())
    {
      if (child is Button { Visible: true, Disabled: false } button)
      {
        button.GrabFocus();
        return;
      }

      FocusFirstButton(child);
    }
  }

  void UpdateServerUi()
  {
    var url = ServerSettings.Current;

    serverHint.Text = url == null
      ? "No server set yet. Open Settings to host or join online."
      : $"Server: {url}";
    Ui.Tint(serverHint, url == null ? Ui.Warn : Ui.Dim);

    if (!serverField.HasFocus())
    {
      serverField.Text = url ?? "";
    }

    serverSource.Text = ServerSettings.Source switch
    {
      ServerSource.Environment => "From the P2P_SIGNALING_URL environment variable (it overrides the file).",
      ServerSource.File => $"From {ServerSettings.FilePath}",
      _ => "Not set yet.",
    };
  }

  void SetStatus(string text, Color color)
  {
    status.Text = text;
    Ui.Tint(status, color);
  }

  // ------------------------------------------------------------------ actions

  void ApplyName()
  {
    PlayerProfile.Save(nameField.Text); // shared by every game on this machine (the library's name file)
    nameField.Text = PlayerProfile.Name;
    session.PlayerName = PlayerProfile.Name;
  }

  /// <summary>Online starts need a server address; without one, send the player to Settings with a clear message.</summary>
  bool RequireServer()
  {
    if (ServerSettings.Current != null)
    {
      return true;
    }

    SetStatus("Set the server address first.", Ui.Warn);
    ShowPage(Page.Settings);

    return false;
  }

  void Start(StartRequest request)
  {
    ApplyName();
    session.Pending = request;
    GetTree().ChangeSceneToFile(WorldScene);
  }

  void StartHosting()
  {
    if (!RequireServer())
    {
      return;
    }

    int? seed = null;
    var text = seedField.Text.Trim();

    if (text.Length > 0)
    {
      seed = int.TryParse(text, out var parsed) ? parsed : StableHash(text);
    }

    Start(new StartRequest(SessionKind.Host, ListForFriends: friendsCheck.ButtonPressed, MaxPlayers: (int)maxPlayers.Value, Seed: seed));
  }

  void JoinTyped()
  {
    var code = codeField.Text.Trim();

    if (code.Length == 0)
    {
      SetStatus("Type a room code first.", Ui.Warn);
      return;
    }

    if (RequireServer())
    {
      Start(new StartRequest(SessionKind.Client, code));
    }
  }

  void OnCodeChanged(string text)
  {
    // Codes are upper-case letters and digits: accept a pasted code in any case or with stray spaces.
    var cleaned = new string(text.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    if (cleaned != text)
    {
      codeField.Text = cleaned;
      codeField.CaretColumn = cleaned.Length;
    }
  }

  void SaveServer()
  {
    var ok = ServerSettings.TrySave(serverField.Text, out var message);
    SetStatus(message, ok ? Ui.Good : Ui.Bad);
    UpdateServerUi();
  }

  void AddFriend()
  {
    var ok = FriendsFile.TryAdd(addIdField.Text, addNameField.Text, out var message);
    SetStatus(message, ok ? Ui.Good : Ui.Bad);

    if (ok)
    {
      addIdField.Text = "";
      addNameField.Text = "";
      RefreshFriendList();
      RefreshFriendRooms();
    }
  }

  void RefreshFriendList()
  {
    var friends = P2PIdentity.LoadFriends().Friends;

    friendsList.Text = friends.Count == 0
      ? "No friends added yet. Paste a friend's ID below."
      : string.Join(", ", friends.Select(f => f.Label.Length > 0 ? f.Label : f.Id));
    Ui.Tint(friendsList, friends.Count == 0 ? Ui.Dim : Ui.Text);
  }

  void RefreshFriendRooms()
  {
    if (current != Page.Join)
    {
      return;
    }

    if (ServerSettings.Current == null)
    {
      friendsStatus.Text = "Set the server address in Settings to see your friends' rooms.";
      Ui.Tint(friendsStatus, Ui.Warn);
      return;
    }

    friendsStatus.Text = "Looking for your friends' rooms...";
    Ui.Tint(friendsStatus, Ui.Dim);
    session.PlayerName = PlayerProfile.Name;
    session.RefreshFriendRooms();
  }

  // ------------------------------------------------------------------ session events

  void OnFriendRooms(IReadOnlyList<FriendRoom> rooms)
  {
    foreach (var child in friendRooms.GetChildren())
    {
      child.QueueFree();
    }

    var problems = session.FriendFileProblems;

    if (problems.Count > 0)
    {
      friendsStatus.Text = "Friends file: " + string.Join("; ", problems);
      Ui.Tint(friendsStatus, Ui.Bad);
    }
    else if (rooms.Count == 0)
    {
      friendsStatus.Text = P2PIdentity.LoadFriends().Friends.Count == 0
        ? "Add a friend (Manage friends) to see the rooms they host."
        : "None of your friends are hosting right now. This list refreshes by itself.";
      Ui.Tint(friendsStatus, Ui.Dim);
    }
    else
    {
      friendsStatus.Text = rooms.Count == 1 ? "1 room open" : $"{rooms.Count} rooms open";
      Ui.Tint(friendsStatus, Ui.Good);
    }

    foreach (var room in rooms)
    {
      friendRooms.AddChild(BuildRoomCard(room));
    }
  }

  PanelContainer BuildRoomCard(FriendRoom room)
  {
    var who = room.FriendLabel.Length > 0 ? room.FriendLabel : room.HostName;
    var card = Ui.Card();
    var row = Ui.Row(12);
    card.AddChild(row);

    var info = Ui.Column(2);
    info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    info.AddChild(Ui.Label(who));
    info.AddChild(Ui.Label($"{room.Players}/{room.MaxPlayers} players  ·  {Age(room.AgeSeconds)}  ·  {room.Code}", Ui.Subtle));
    row.AddChild(info);

    var full = room.Players >= room.MaxPlayers;
    var join = Ui.Button(full ? "Full" : "Join", Ui.Primary, 100f);
    join.Disabled = full;
    var code = room.Code;
    join.Pressed += () => Start(new StartRequest(SessionKind.Client, code));
    row.AddChild(join);

    return card;
  }

  static string Age(int seconds)
  {
    if (seconds < 60)
    {
      return "just opened";
    }

    return seconds < 3600 ? $"open {seconds / 60} min" : $"open {seconds / 3600} h";
  }

  void OnFailed(string message)
  {
    SetStatus(message, Ui.Bad);

    if (current == Page.Join)
    {
      friendsStatus.Text = "Could not reach the server.";
      Ui.Tint(friendsStatus, Ui.Bad);
    }
  }

  void OnStatus(string message)
  {
    SetStatus(message, Ui.Dim);
  }

  /// <summary>A stable hash (string.GetHashCode changes between runs), so a typed seed gives the same world every time.</summary>
  static int StableHash(string text)
  {
    unchecked
    {
      var hash = 2166136261u;

      foreach (var c in text)
      {
        hash = (hash ^ c) * 16777619u;
      }

      return (int)(hash & 0x7fffffff);
    }
  }
}
