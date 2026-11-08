# Curitibia

A small voxel co-op game made with Godot 4 (C#). Walk around a procedurally generated block world, break and place
blocks, and survive enemies that hunt you; play alone or host a room and let friends join straight from their own
machine.

- How the project is organised, how to run it and the dev flags: [CLAUDE.md](CLAUDE.md)
- The plan the Godot remake follows: [GODOT_REMAKE_PLAN.md](GODOT_REMAKE_PLAN.md)
- Building from source for Windows and Linux: [BUILDING.md](BUILDING.md)

## Play a release

Builds are attached to the [latest GitHub release](https://github.com/LucasVinicius314/curitibia/releases/latest).
Paste one of these into a terminal, in an empty folder: it downloads the release, unpacks it and starts the game.
It uses only what is already installed (PowerShell on Windows; `curl` and `tar` on Linux).

**Windows (PowerShell)**

```powershell
$ProgressPreference='SilentlyContinue'; Invoke-WebRequest https://github.com/LucasVinicius314/curitibia/releases/latest/download/Curitibia-windows.zip -OutFile Curitibia-windows.zip; Expand-Archive Curitibia-windows.zip Curitibia -Force; .\Curitibia\Curitibia.exe
```

**Linux (bash)**

```bash
curl -fL https://github.com/LucasVinicius314/curitibia/releases/latest/download/Curitibia-linux.tar.gz -o Curitibia-linux.tar.gz && mkdir -p Curitibia && tar -xzf Curitibia-linux.tar.gz -C Curitibia && chmod +x Curitibia/Curitibia.x86_64 && ./Curitibia/Curitibia.x86_64
```

## Controls

| | Keyboard and mouse | Controller |
|---|---|---|
| Move / look | WASD / mouse | left stick / right stick |
| Jump | Space | A |
| Break / place block | left / right click | right / left trigger |
| Change block | 1-2 or mouse wheel | bumpers |
| Players list | hold Tab | Back |
| Pause (room code, leave) | Esc | Start |

## Play with friends

Pick **Host a room** (you get a room code to share; friends who have your friend ID see it in their list too) or **Join a room**
and type a code or pick a friend's room. The host runs the world and the enemies; everyone else sees the same world.
The game finds players through a small signaling server ([p2p-signaling-server](https://github.com/LucasVinicius314/p2p-signaling-server))
and then plays peer to peer. Enter that server's address once under **Settings** (it is shared with other games that use
the same library, so it is stored in `~/.p2p-signaling-server/server`); the server needs a bucket named `curitibia`.

## Update

Inside the game folder, double-click **Update.bat** (Windows) or run `bash update.sh` (Linux). It looks up the
latest release, and if it is newer than the `version.txt` in the folder it downloads it and replaces the game files.
Close the game first. Your settings live outside the folder and are kept.

Notes:

- `releases/latest/download/<file>` always points at the newest release's file with that name, so the commands never
  change. For a specific version use `releases/download/<tag>/<file>` (for example `.../releases/download/v0.1.0/...`).
  Drafts are skipped by `latest`.
- The repository (and so the release) must be public for these to work without logging in.
- The exports are self-contained (no Godot or .NET needed). Linux needs a desktop with a Vulkan-capable GPU.
