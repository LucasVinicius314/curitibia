using Curitibia.Core;
using Curitibia.Net;
using Curitibia.Player;
using Curitibia.UI;
using Curitibia.World;
using Godot;

namespace Curitibia;

/// <summary>
/// The playable world scene. What it does depends on the start request (from the menu or the dev flags):
///   Solo / Host: choose a seed, start the terrain, spawn the local player (the host's spawner replicates it); Host also
///                opens an online room.
///   Client:      start the multiplayer link; the host sends the seed, the edits, then spawns our player.
/// Developer flags (after `--`): --seed=N, --cam=x,y,z,yaw,pitch (spectator camera instead of a player), --screenshot=FILE
/// (save a PNG once the world has settled, then quit); plus the --net-* flags in NetDevHooks.
/// </summary>
public partial class Main : Node3D
{
  [Export] public Terrain WorldTerrain { get; set; } = null!;
  [Export] public Node3D Players { get; set; } = null!;
  [Export] public PackedScene PlayerScene { get; set; } = null!;
  [Export] public MultiplayerSpawner PlayerSpawner { get; set; } = null!;
  [Export] public Hud Hud { get; set; } = null!;

  const int SettleFrames = 20;
  const string MenuScene = "res://scenes/menu.tscn";

  NetSession session = null!;
  string? screenshotPath;
  int settledFrames;

  public override void _Ready()
  {
    session = NetSession.Instance;
    screenshotPath = DevArgs.Get("screenshot");

    var request = session.Pending ?? new StartRequest(SessionKind.Solo);
    session.Pending = null;

    // Every peer builds a player the same way from the data the host spawns it with (peer id and position).
    PlayerSpawner.SpawnFunction = Callable.From<Variant, Node>(BuildPlayer);
    Players.ChildEnteredTree += OnPlayerEntered;
    session.Ended += OnSessionEnded;
    session.PeerReady += SpawnPlayerFor;
    session.PeerGone += DespawnPlayerFor;
    session.PlayerPositionProvider = PlayerPosition;

    if (request.Kind == SessionKind.Client)
    {
      StartAsClient(request.Code ?? "");
    }
    else
    {
      StartAsHost(request.Kind == SessionKind.Host, request.ListForFriends);
    }
  }

  public override void _ExitTree()
  {
    session.Ended -= OnSessionEnded;
    session.PeerReady -= SpawnPlayerFor;
    session.PeerGone -= DespawnPlayerFor;
    session.PlayerPositionProvider = null;
    session.DetachWorld();
  }

  // ------------------------------------------------------------------ start-up

  void StartAsHost(bool online, bool listForFriends)
  {
    var seed = DevArgs.GetInt("seed") ?? (int)(GD.Randi() & 0x7fffffff);
    session.WorldSeed = seed;
    GD.Print($"World seed {seed}");

    session.StartSolo(); // offline state; OpenRoom turns it into a hosted room once the lobby answers
    WorldTerrain.Begin(seed);

    if (online)
    {
      session.OpenRoom(listForFriends);
    }

    session.AttachWorld(WorldTerrain);

    if (DevArgs.Cam is { } cam)
    {
      SpawnSpectator(cam.position, cam.yaw, cam.pitch);
    }
    else
    {
      SpawnPlayerFor(1);
    }
  }

  void StartAsClient(string code)
  {
    // The terrain starts when the host's welcome brings the seed.
    session.SeedReceived += seed =>
    {
      GD.Print($"World seed {seed}");
      WorldTerrain.Begin(seed);
    };

    session.JoinRoom(code); // sets the role first, so AttachWorld installs the client's edit routing
    session.AttachWorld(WorldTerrain);
  }

  // ------------------------------------------------------------------ players

  /// <summary>Host only: spawns a player for a peer; the spawner replicates it to everyone.</summary>
  void SpawnPlayerFor(int peerId)
  {
    // Spread newcomers a little so they do not spawn inside each other.
    var slot = Players.GetChildCount();

    PlayerSpawner.Spawn(new Godot.Collections.Dictionary
    {
      ["peer"] = peerId,
      ["pos"] = PlayerController.SpawnPoint(slot * 2, 0),
    });
  }

  /// <summary>Runs on every peer when a player is spawned.</summary>
  Node BuildPlayer(Variant data)
  {
    var info = data.AsGodotDictionary();
    var position = info["pos"].AsVector3();

    var player = PlayerScene.Instantiate<PlayerController>();
    player.Name = info["peer"].AsInt32().ToString(); // the name is the owner: PlayerController derives its authority from it
    player.Position = position;
    player.SyncPosition = position;

    return player;
  }

  void DespawnPlayerFor(int peerId)
  {
    Players.GetNodeOrNull(peerId.ToString())?.QueueFree();
  }

  Vector3? PlayerPosition(int peerId)
  {
    return Players.GetNodeOrNull<PlayerController>(peerId.ToString())?.GlobalPosition;
  }

  void OnPlayerEntered(Node node)
  {
    // The HUD follows the player this peer controls (replicated players arrive through the spawner).
    if (node is PlayerController { IsLocal: true } player)
    {
      Hud.Bind(player);
    }
  }

  void SpawnSpectator(Vector3 position, float yaw, float pitch)
  {
    var camera = new Camera3D { Fov = 80f };
    AddChild(camera);
    camera.GlobalPosition = position;
    camera.Rotation = new Vector3(pitch, yaw, 0f);
    camera.MakeCurrent();

    // The terrain streams around viewers; a spectator camera is one too.
    var viewer = new Node3D { Name = "SpectatorViewer" };
    AddChild(viewer);
    viewer.GlobalPosition = position;
    WorldTerrain.AddViewer(viewer);
  }

  void OnSessionEnded(string reason)
  {
    GD.Print($"leaving the world: {reason}");
    Input.MouseMode = Input.MouseModeEnum.Visible;
    GetTree().ChangeSceneToFile(MenuScene);
  }

  // ------------------------------------------------------------------ dev screenshot

  public override void _Process(double delta)
  {
    if (screenshotPath == null || !WorldTerrain.IsSettled)
    {
      return;
    }

    // Let a few frames render after the last chunk landed, then capture.
    settledFrames++;

    if (settledFrames < SettleFrames)
    {
      return;
    }

    var image = GetViewport().GetTexture().GetImage();
    var error = image.SavePng(screenshotPath);
    GD.Print($"Screenshot {screenshotPath}: {error}");
    screenshotPath = null;
    GetTree().Quit();
  }
}
