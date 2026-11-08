# Building Curitibia

(Just want to play? See the download command in [README.md](README.md).)

One command builds a standalone game for Windows and Linux (x86_64) from this folder.

```powershell
.\build.ps1                       # Windows and Linux, release  ->  build\windows, build\linux
.\build.ps1 -Target windows       # or: linux
.\build.ps1 -Debug                # debug export (keeps the console wrapper on Windows)
.\build.ps1 -Package              # also build\Curitibia-windows.zip and build\Curitibia-linux.tar.gz
```

From VS Code: **Terminal > Run Build Task** (Ctrl+Shift+B) runs the same thing; the other tasks build one platform,
package, or install the export templates.

On Linux, macOS or Git Bash use `./build.sh` (or `bash build.sh` if git did not keep its executable bit) with the same options, e.g. `./build.sh linux --debug` or `./build.sh --package`.

## One-time setup

1. **Godot 4.7.2, the .NET (mono) build**, and the **.NET 8 or newer SDK** (`dotnet --version`).
2. **Tell the script where Godot is.** It uses, in order: `-Godot <path>`, the `GODOT` environment variable, a
   one-line `.godot-path` file in this folder (git-ignored, so it can hold your own path), then `godot` /
   `godot-mono` on your PATH. Use the *console* exe on Windows (`..._console.exe`).
3. **Export templates** that match the Godot version, about 1.2 GB (1.9 GB installed):

   ```powershell
   .\build.ps1 -InstallTemplates     # downloads from the Godot GitHub release and checks its SHA-512
   ```

   They install to `%APPDATA%\Godot\export_templates\4.7.2.stable.mono` (`~/.local/share/godot/export_templates/...`
   on Linux). The same templates cover both targets, so you can build for Linux from Windows and the reverse.

### Clone with submodules

The multiplayer client library comes from a git submodule, `libs/p2p-signaling-server`. Clone with
`git clone --recurse-submodules`, or run `git submodule update --init` in an existing clone. The build scripts stop with a
message if it is missing, and the release workflow checks it out.

### Multiplayer needs one more download

Run `scripts\fetch-webrtc.ps1` (or `bash scripts/fetch-webrtc.sh`) once. It downloads the WebRTC native extension
(about 90 MB, pinned and SHA-256 verified) into `addons/webrtc_native`, which is not committed. The build scripts stop
with a message if it is missing, and the release workflow runs the same script.

## What you get

| Target | Output | Run it |
|---|---|---|
| Windows | `build\windows\Curitibia.exe` + `data_Curitibia_windows_x86_64\` | double-click; keep the folder next to the exe |
| Linux | `build/linux/Curitibia.x86_64` + `data_Curitibia_linuxbsd_x86_64/` | `chmod +x Curitibia.x86_64` after unpacking, then run it |

Both are self-contained: the target machine does not need Godot or .NET installed. The Linux build needs a normal
desktop Linux with a Vulkan-capable GPU; Windows needs the D3D12 driver the project already uses.

## Publishing a release

Releases are built and published by GitHub Actions (`.github/workflows/release.yml`). Tag a commit that is on `main`
and push the tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The workflow then:

1. refuses to continue unless the tagged commit is on `main`;
2. runs the unit tests;
3. gets Godot and the export templates (official downloads, SHA-512 checked, **cached** so only the first run pays
   the 1.2 GB download; bump `GODOT_VERSION` at the top of the workflow when you move to a new Godot);
4. runs `build.sh --package`, the same build you run locally;
5. creates the release `v0.1.0` with `Curitibia-windows.zip` and `Curitibia-linux.tar.gz` attached and generated
   release notes. Every tag is published as a full release, never a pre-release.

The README's download commands fetch exactly those two file names from the latest release, so they must not change.
If a run fails after the tag was pushed, fix the problem, delete the tag (`git push origin :refs/tags/v0.1.0`,
`git tag -d v0.1.0`) and tag again.

Publishing by hand still works: run `.\build.ps1 -Package`, then on github.com use Releases > *Draft a new release*
and drag in both files, or with the GitHub CLI:

```powershell
gh release create v0.1.0 build\Curitibia-windows.zip build\Curitibia-linux.tar.gz --title "Curitibia 0.1.0" --generate-notes
```

The repository must be public for anonymous downloads. Each file must stay under 2 GB (ours are about 60 to 75 MB).

## Notes

- Settings live in the export presets in `export_presets.cfg` (committed). Edit them in the Godot editor under
  *Project > Export* if you want an icon, a version, or another architecture.
- `build\` is git-ignored. The Windows zip is plain; the Linux tarball also holds the contents directly (no wrapping folder) but Windows cannot store the
  executable bit, hence the `chmod +x`.
- Each build folder also gets `version.txt` (the tag in CI via `GITHUB_REF_NAME`, otherwise `git describe`) and the
  updater from `scripts/update/` (`Update.bat` + `update.ps1` on Windows, `update.sh` on Linux), which downloads the
  latest release into the folder. It is test-run against the published release; keep its asset names in step with
  the packaging above.
- The scripts turn off the .NET compiler servers. Left on, they keep Godot's output pipe open for about ten
  minutes after each export and every build appears to hang.
- Tested here: the Windows export builds and runs (`--headless --quit-after`). Not yet tested: the Linux export running,
  macOS, ARM, the scripts' own `-InstallTemplates` download path (the templates were installed by hand with the same
  steps), the release workflow, and the README download commands (no release has been published yet).
