# Curitibia (Godot remake)

Voxel co-op prototype remade from the old Unity project (see `GODOT_REMAKE_PLAN.md`; the Unity code is in git history before commit `1431b1c`). Godot 4.7.2 mono, **C# only**, Jolt, Forward+, D3D12. Follows the user's global `godot-games` conventions; this file wins where it differs.

## Layout
- `src/` by feature: `Core` (no dependencies: `GameLayers`, `Team`, `VoxelMath`, `VoxelRaycast`, `GradientNoise`, `DevArgs`, `Game` autoload = input map in code), `World` (terrain, chunks, mesher, generator), `Player`, `Net`, `UI`; `Main.cs` is the world scene script.
- `scenes/` (menu, main), `prefabs/` (player, hud), `data/*.tres` (tuning as `Resource`s: `world_gen`, `player_tuning`), `assets/textures`, `tests/Curitibia.Tests` (xUnit), `libs/p2p-signaling-server` (git submodule, **never edit**), `scripts/fetch-webrtc.*`.
- Terrain, chunks and meshes are built in code (procedural); player/HUD/menu are scenes with `[Export]` node refs (`node_paths=` in the `.tscn`).

## Rules specific to this project
- **Engine-free code stays engine-free** (`VoxelMath`, `VoxelRaycast`, `GradientNoise`, `Blocks`, `WorldGenerator`, `ChunkMesher`): no `using Godot`, so `tests/` links them. Add a test with any change to them. Run: `dotnet test tests/Curitibia.Tests`.
- Terrain generation is a pure function of (seed, chunk coord): the host sends only the seed plus the edit list, never terrain. Do not make it depend on anything per-peer.
- Chunk data is `byte[32*32*32]` (`VoxelMath.Index`), block ids in `BlockIds`. World coordinates use `VoxelMath.WorldToChunk` (floor division on negatives).
- **Multiplayer = Godot's multiplayer API over p2p-signaling-server** (host = peer 1). `NetSession` is the autoload at `/root/NetSession` (RPC path must match on every peer). Players are built by a `MultiplayerSpawner` with a custom `SpawnFunction` in `Main` (node name = owning peer id = authority). Host-authoritative world: clients *request* block edits (`RpcRequestSetBlock`), the host validates (reach, rate, block id) and broadcasts. Players replicate `Sync*` properties via `MultiplayerSynchronizer`; animation is computed locally from replicated velocity.
- The p2p wiring rules (assign the peer in `PeerCreated`, RPCs only after `ConnectedToServer`/`PeerConnected`, library compiled into this assembly, `addons/webrtc_native` present) are in `libs/p2p-signaling-server/docs/integration-guide.md`: read it before touching networking. Server `https://p2p.suresure.me`, bucket `curitibia`.
- Never create a Godot object per frame (chunk meshes are replaced only on remesh and the old ones are `Dispose`d; share materials; cache `StringName` statics).

## Run, verify
```
dotnet build Curitibia.csproj
Godot_..._console.exe --headless --path . --import            # load errors, .uid files
Godot_..._console.exe --path . res://scenes/main.tscn -- --seed=42 --cam=-20,34,-20,-135,-25 --screenshot=out.png   # look at it
```
Dev flags (after `--`): `--seed=N`, `--cam=x,y,z,yawDeg,pitchDeg`, `--screenshot=FILE`; networking: `--net-host`, `--net-join=CODE`, `--net-name=`, `--net-url=` (e.g. `http://127.0.0.1:8099` for the dev server), `--net-no-friends`, `--net-edit=x,y,z,id`, `--net-report-at=SEC`, `--net-quit-at=SEC` (see `NetDevHooks`). Two-process check: host prints `CODE xxxx`; start the client with `--net-join=xxxx` and a different `P2P_SIGNALING_HOME`; compare the `NET REPORT` lines. Local server: `go run ./cmd/devserver -bucket curitibia` inside the submodule. A compile proves nothing for networking.
First time on a machine: `git submodule update --init`, `scripts/fetch-webrtc.ps1`, `dotnet build`.

## Status
Done and verified: voxel terrain (streaming, threaded gen + mesh, per-frame upload budget), first-person player (walk, jump, 1-block step-up, block break/place with highlight, hotbar 1-2/wheel), menu (solo, host, join by code, friends list), hosting/joining on the real server, spawn replication, host-authoritative block edits, late-join sync.
Not done yet: enemies (`NavigationAgent3D`, vision, chase, see plan section 7), projectiles/health, pause menu "Leave" button and room code on the HUD, item/hotbar system, navigation for a changing world.

## Known architectural debt
- Block break/place waits one network round trip on a client (no prediction).
- Host-side edit validation uses the sender's replicated position; no per-block permission rules.
- Block types are a static table (`BlockIds`), not `.tres` data yet.
- Border chunks re-mesh through the version counter; worker meshing may read a neighbour array while the main thread edits it (a later remesh corrects any glitch).
- Players have no health, damage or respawn rules beyond falling out of the world.
