using System;
using System.Collections.Generic;
using Curitibia.Core;
using Curitibia.World;
using Godot;
using P2PSignaling;

namespace Curitibia.Net;

public enum SessionKind
{
  Solo,
  Host,
  Client,
}

/// <summary>What the menu (or a dev flag) asked for; the world scene reads it when it loads.</summary>
public sealed record StartRequest(SessionKind Kind, string? Code = null, bool ListForFriends = true);

/// <summary>
/// Autoload at /root/NetSession (the same path on every peer, which RPCs need). Owns the p2p lobby and speaks Godot's
/// high-level multiplayer on top of it: the host is peer 1 and the single source of truth, clients send requests and
/// follow. Players are replicated by a MultiplayerSpawner/MultiplayerSynchronizer in the world scene; this node carries
/// the session-level messages (handshake, world seed, block edits).
///
/// The wiring follows libs/p2p-signaling-server/docs/integration-guide.md section 6: the peer is assigned in
/// PeerCreated, RPCs only go out after ConnectedToServer / PeerConnected, and only the host may call host-to-client RPCs.
/// </summary>
public partial class NetSession : Node
{
  public const string DefaultServerUrl = "https://p2p.suresure.me";
  public const string Bucket = "curitibia";

  /// <summary>Bump when the handshake or RPC contract changes incompatibly.</summary>
  public const int Protocol = 1;

  const int EditsPerBatch = 400;
  const float MaxEditDistance = 9f;
  const int MaxEditsPerSecond = 30;

  public static NetSession Instance { get; private set; } = null!;

  public StartRequest? Pending { get; set; }
  public SessionKind Kind { get; private set; } = SessionKind.Solo;
  public string PlayerName { get; set; } = "player";
  public string? RoomCode { get; private set; }
  public bool IsOnline => Kind != SessionKind.Solo;
  public bool IsHost => Kind != SessionKind.Client;

  /// <summary>Seed the world was started with (host: chosen locally, client: received in the welcome).</summary>
  public int WorldSeed { get; set; }

  // Menu / HUD feedback.
  public event Action<string>? StatusChanged;
  public event Action<string>? CodeReady;
  public event Action<IReadOnlyList<FriendRoom>>? FriendRoomsReceived;
  public event Action<string>? Failed;
  public event Action<string>? Ended;

  // World wiring (the world scene subscribes).
  public event Action<int>? SeedReceived;
  public event Action<int>? PeerReady;
  public event Action<int>? PeerGone;

  /// <summary>Set by the world: where a peer's player is (for validating its edit requests), null when unknown.</summary>
  public Func<int, Vector3?>? PlayerPositionProvider { get; set; }

  P2PLobby? lobby;
  Terrain? terrain;
  bool retriedWithoutFriends;

  readonly Dictionary<int, string> names = new();
  readonly HashSet<int> synced = new();
  readonly Dictionary<int, (double second, int count)> editRate = new();

  public override void _EnterTree()
  {
    Instance = this;
  }

  public override void _Ready()
  {
    Multiplayer.ConnectedToServer += OnConnectedToServer;
    Multiplayer.PeerConnected += OnPeerConnected;
    Multiplayer.PeerDisconnected += OnPeerDisconnected;
    Multiplayer.ServerDisconnected += OnServerDisconnected;

    PlayerName = DevArgs.Get("net-name") ?? System.Environment.UserName;

    if (DevArgs.Has("net-host"))
    {
      Pending = new StartRequest(SessionKind.Host, ListForFriends: !DevArgs.Has("net-no-friends"));
    }
    else if (DevArgs.Get("net-join") is { } code)
    {
      Pending = new StartRequest(SessionKind.Client, code);
    }

    if (Pending != null || DevArgs.Has("net-report-at") || DevArgs.Has("net-quit-at"))
    {
      AddChild(new NetDevHooks());
    }
  }

  // ------------------------------------------------------------------ lobby

  P2PLobby EnsureLobby()
  {
    if (lobby != null)
    {
      return lobby;
    }

    var url = DevArgs.Get("net-url") ?? DefaultServerUrl;
    lobby = new P2PLobby(url, Bucket, ProjectSettings.GetSetting("application/config/version", "dev").AsString())
    {
      PlayerName = PlayerName,
    };

    AddChild(lobby); // it must be in the tree: it does its work in _Process

    // Rule 1: attach the multiplayer peer the moment it exists, before any link is up.
    lobby.PeerCreated += peer => Multiplayer.MultiplayerPeer = peer;
    lobby.CodeReady += code =>
    {
      RoomCode = code;
      GD.Print($"CODE {code}");
      StatusChanged?.Invoke($"Room code {code}");
      CodeReady?.Invoke(code);
    };
    lobby.FriendRoomsReceived += rooms => FriendRoomsReceived?.Invoke(rooms);
    lobby.Failed += OnLobbyFailed;
    lobby.RoomClosed += reason => End($"room closed: {reason}");
    lobby.SignalingLost += reason =>
    {
      GD.Print($"signaling lost ({reason}); linked peers keep playing");
      RoomCode = null;
      StatusChanged?.Invoke("Lost the signaling server: nobody new can join this room.");
    };

    return lobby;
  }

  /// <summary>The friends-list part of the lobby: used by the menu before any room exists.</summary>
  public void RefreshFriendRooms()
  {
    var l = EnsureLobby();
    l.PlayerName = PlayerName;
    l.ListFriendRooms();
  }

  public IReadOnlyList<string> FriendFileProblems => EnsureLobby().FriendFileProblems;

  /// <summary>Host an online room (the world is already running). Friends who have your id can see it.</summary>
  public void OpenRoom(bool listForFriends)
  {
    lobby?.Leave(); // drop the menu's browsing connection so the room starts from a clean lobby
    var l = EnsureLobby();
    Kind = SessionKind.Host;
    retriedWithoutFriends = !listForFriends;
    l.PlayerName = PlayerName;
    l.ListForFriends = listForFriends;
    StatusChanged?.Invoke("Opening room...");
    l.CreateRoom();
  }

  public void JoinRoom(string code)
  {
    lobby?.Leave();
    var l = EnsureLobby();
    Kind = SessionKind.Client;
    l.PlayerName = PlayerName;
    StatusChanged?.Invoke($"Joining {code}...");
    l.JoinRoom(code.Trim().ToUpperInvariant());
  }

  public void StartSolo()
  {
    Kind = SessionKind.Solo;
  }

  void OnLobbyFailed(string code, string message)
  {
    // A server older than the friends feature answers bad_message to a friends-listed room: host without the listing.
    if (Kind == SessionKind.Host && !retriedWithoutFriends && code == "bad_message")
    {
      retriedWithoutFriends = true;
      lobby!.ListForFriends = false;
      StatusChanged?.Invoke("The server does not support the friends list yet; hosting without it.");
      lobby.CreateRoom();
      return;
    }

    GD.PrintErr($"net failed: {code}: {message}");
    Failed?.Invoke($"{message} ({code})");
  }

  /// <summary>Tear the session down and return to offline play state. The caller changes scenes.</summary>
  public void Leave()
  {
    lobby?.Leave();
    Multiplayer.MultiplayerPeer = null;
    ResetState();
  }

  void End(string reason)
  {
    GD.Print($"session ended: {reason}");
    Leave();
    Ended?.Invoke(reason);
  }

  void ResetState()
  {
    Kind = SessionKind.Solo;
    RoomCode = null;
    names.Clear();
    synced.Clear();
    editRate.Clear();

    if (terrain != null)
    {
      terrain.BlockChanged -= OnHostBlockChanged;
      terrain.EditRouter = null;
      terrain = null;
    }
  }

  // ------------------------------------------------------------------ world wiring

  /// <summary>The world scene calls this once its terrain exists, so edits are routed the right way for this role.</summary>
  public void AttachWorld(Terrain world)
  {
    terrain = world;

    if (Kind == SessionKind.Client)
    {
      terrain.EditRouter = (voxel, id) => RpcId(1, MethodName.RpcRequestSetBlock, voxel, id);
    }
    else
    {
      terrain.EditRouter = null;
      terrain.BlockChanged += OnHostBlockChanged;
    }
  }

  public void DetachWorld()
  {
    if (terrain != null)
    {
      terrain.BlockChanged -= OnHostBlockChanged;
      terrain.EditRouter = null;
      terrain = null;
    }
  }

  void OnHostBlockChanged(Vector3I voxel, byte id)
  {
    // Every applied edit (the host's own and validated client ones) is told to everyone who has the world.
    foreach (var peer in synced)
    {
      RpcId(peer, MethodName.RpcBlockChanged, voxel, id);
    }
  }

  // ------------------------------------------------------------------ link events

  void OnConnectedToServer()
  {
    GD.Print("client: linked to the host");
    StatusChanged?.Invoke("Linked. Loading the world...");
    RpcId(1, MethodName.RpcHello, PlayerName, Protocol);
  }

  void OnPeerConnected(long id)
  {
    GD.Print($"peer {id} connected");
  }

  void OnPeerDisconnected(long id)
  {
    var peer = (int)id;
    GD.Print($"peer {peer} disconnected");

    if (!IsHost)
    {
      return;
    }

    names.Remove(peer);
    editRate.Remove(peer);

    if (synced.Remove(peer))
    {
      PeerGone?.Invoke(peer);
    }
  }

  void OnServerDisconnected()
  {
    End("the host left");
  }

  // ------------------------------------------------------------------ RPCs: client to host

  [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  void RpcHello(string playerName, int protocol)
  {
    if (!Multiplayer.IsServer() || terrain == null)
    {
      return;
    }

    var sender = Multiplayer.GetRemoteSenderId();

    if (protocol != Protocol)
    {
      RpcId(sender, MethodName.RpcRejected, $"version mismatch (host {Protocol}, you {protocol})");
      return;
    }

    names[sender] = Sanitize(playerName);
    GD.Print($"host: hello from {names[sender]} (peer {sender})");

    RpcId(sender, MethodName.RpcWelcome, WorldSeed);

    var edits = terrain.SnapshotEdits();

    for (int i = 0; i < edits.Count; i += EditsPerBatch)
    {
      var voxels = new Godot.Collections.Array<Vector3I>();
      var ids = new List<byte>();

      for (int j = i; j < Math.Min(i + EditsPerBatch, edits.Count); j++)
      {
        voxels.Add(edits[j].voxel);
        ids.Add(edits[j].id);
      }

      RpcId(sender, MethodName.RpcEdits, voxels, ids.ToArray());
    }

    RpcId(sender, MethodName.RpcSyncDone);
  }

  [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  void RpcReady()
  {
    if (!Multiplayer.IsServer())
    {
      return;
    }

    var sender = Multiplayer.GetRemoteSenderId();

    if (!names.ContainsKey(sender) || !synced.Add(sender))
    {
      return;
    }

    PeerReady?.Invoke(sender);
  }

  [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  void RpcRequestSetBlock(Vector3I voxel, byte id)
  {
    if (!Multiplayer.IsServer() || terrain == null)
    {
      return;
    }

    var sender = Multiplayer.GetRemoteSenderId();

    // Never trust a client: it must be a synced peer, within reach of where its player is, asking for a real block,
    // and not flooding.
    if (!synced.Contains(sender) || !VoxelMath.InWorldHeight(voxel.Y) || !IsAllowedBlock(id) || !WithinRate(sender))
    {
      return;
    }

    var position = PlayerPositionProvider?.Invoke(sender);

    if (position == null || position.Value.DistanceTo(new Vector3(voxel.X + 0.5f, voxel.Y + 0.5f, voxel.Z + 0.5f)) > MaxEditDistance)
    {
      return;
    }

    terrain.ApplyEdit(voxel, id); // BlockChanged then broadcasts it
  }

  // ------------------------------------------------------------------ RPCs: host to client (Authority mode)

  [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  void RpcWelcome(int seed)
  {
    WorldSeed = seed;
    SeedReceived?.Invoke(seed);
  }

  [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  void RpcEdits(Godot.Collections.Array<Vector3I> voxels, byte[] ids)
  {
    if (terrain == null)
    {
      return;
    }

    for (int i = 0; i < voxels.Count && i < ids.Length; i++)
    {
      terrain.ApplyEdit(voxels[i], ids[i]);
    }
  }

  [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  void RpcSyncDone()
  {
    StatusChanged?.Invoke("In the world.");
    RpcId(1, MethodName.RpcReady);
  }

  [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  void RpcBlockChanged(Vector3I voxel, byte id)
  {
    terrain?.ApplyEdit(voxel, id);
  }

  [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
  void RpcRejected(string reason)
  {
    Failed?.Invoke(reason);
    End(reason);
  }

  // ------------------------------------------------------------------ helpers

  public string NameOf(int peer)
  {
    return names.TryGetValue(peer, out var n) ? n : $"peer {peer}";
  }

  static bool IsAllowedBlock(byte id)
  {
    return id == BlockIds.Air || Array.IndexOf(BlockIds.Placeable, id) >= 0;
  }

  bool WithinRate(int peer)
  {
    var second = Math.Floor(Time.GetTicksMsec() / 1000.0);
    editRate.TryGetValue(peer, out var state);

    state = state.second == second ? (second, state.count + 1) : (second, 1);
    editRate[peer] = state;

    return state.count <= MaxEditsPerSecond;
  }

  static string Sanitize(string raw)
  {
    var trimmed = raw.Trim();

    if (trimmed.Length == 0)
    {
      return "player";
    }

    return trimmed.Length > 24 ? trimmed[..24] : trimmed;
  }
}
