#!/usr/bin/env bash
# Builds Curitibia for Windows and/or Linux with the Godot export presets in export_presets.cfg.
#
#   ./build.sh                    Windows and Linux, release
#   ./build.sh windows|linux|all  one target
#   ./build.sh linux --debug
#   ./build.sh --package          also make build/Curitibia-windows.zip and build/Curitibia-linux.tar.gz
#   ./build.sh --install-templates   download the export templates that match your Godot version, then exit
#
# Finds Godot (the mono binary) from, in order: the GODOT environment variable, a one-line .godot-path file in
# the repository root (git-ignored, so it can hold your local path), then PATH. Output: build/windows, build/linux.
set -euo pipefail
cd "$(dirname "$0")"

# Godot waits for the .NET build's output pipe to close, and the compiler / MSBuild servers keep it open for about
# ten minutes after the build, so every export would stall that long. Run the builds without the servers.
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 DOTNET_CLI_TELEMETRY_OPTOUT=1

target=all debug=0 package=0 install=0
for arg in "$@"; do
  case "$arg" in
    windows|linux|all) target=$arg ;;
    --debug) debug=1 ;;
    --package) package=1 ;;
    --install-templates) install=1 ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done

find_godot() {
  if [[ -n "${GODOT:-}" ]]; then echo "$GODOT"; return; fi
  if [[ -f .godot-path ]]; then
    local from_file; from_file=$(head -n1 .godot-path | tr -d '\r')
    if [[ -n "$from_file" ]]; then echo "$from_file"; return; fi
  fi
  command -v godot-mono || command -v godot || true
}
godot=$(find_godot)
if [[ -z "$godot" || ! -e "$godot" ]]; then
  echo "Godot not found. Set GODOT, or put the path to the mono binary in a .godot-path file." >&2
  exit 1
fi

# "4.7.2.stable.mono.official.abc123" -> "4.7.2.stable.mono", the name of the export templates folder.
full_version=$("$godot" --version | tr -d '\r')
version=$(echo "$full_version" | cut -d. -f1-5)
[[ "$version" == *.mono ]] || { echo "This needs the mono (C#) build of Godot; '$godot' reports $full_version." >&2; exit 1; }

case "$(uname -s)" in
  Darwin) data_dir="$HOME/Library/Application Support/Godot" ;;
  MINGW*|MSYS*|CYGWIN*) data_dir="$APPDATA/Godot" ;;
  *) data_dir="${XDG_DATA_HOME:-$HOME/.local/share}/godot" ;;
esac
templates="$data_dir/export_templates/$version"

if (( install )); then
  tag=$(echo "$version" | sed -E 's/\.mono$//; s/^([0-9]+\.[0-9]+(\.[0-9]+)?)\.([a-z0-9]+)$/\1-\3/')
  file="Godot_v${tag}_mono_export_templates.tpz"
  base="https://github.com/godotengine/godot/releases/download/$tag"
  work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
  echo "Downloading $file (about 1.2 GB) ..."
  curl -fL -o "$work/$file" "$base/$file"
  curl -fL -o "$work/SHA512-SUMS.txt" "$base/SHA512-SUMS.txt"
  expected=$(grep -F "$file" "$work/SHA512-SUMS.txt" | cut -d' ' -f1)
  actual=$(sha512sum "$work/$file" | cut -d' ' -f1)
  [[ "$expected" == "$actual" ]] || { echo "Checksum mismatch for $file; not installing it." >&2; exit 1; }
  echo "Installing to $templates ..."
  mkdir -p "$work/x" "$templates"
  unzip -q "$work/$file" -d "$work/x"
  cp -r "$work"/x/templates/* "$templates"/
  echo "Export templates installed."
  exit 0
fi

if [[ ! -f "$templates/version.txt" ]]; then
  echo "Export templates for $version are not installed (looked in $templates). Run: ./build.sh --install-templates" >&2
  exit 1
fi

declare -A preset=( [windows]="Windows Desktop" [linux]="Linux" )
declare -A out=( [windows]="build/windows/Curitibia.exe" [linux]="build/linux/Curitibia.x86_64" )
if [[ "$target" == all ]]; then targets=(windows linux); else targets=("$target"); fi
mode=--export-release; (( debug )) && mode=--export-debug

if [[ ! -f libs/p2p-signaling-server/clients/godot-csharp/src/P2PLobby.cs ]]; then
  echo "The p2p-signaling-server submodule is missing. Run: git submodule update --init" >&2
  exit 1
fi
if [[ ! -f addons/webrtc_native/webrtc_native.gdextension ]]; then
  echo "The WebRTC native extension is missing, so the build would have no multiplayer. Run: bash scripts/fetch-webrtc.sh" >&2
  exit 1
fi

echo "Importing project ($full_version) ..."
"$godot" --headless --quiet --path . --import >/dev/null 2>&1 || true

for t in "${targets[@]}"; do
  dest="${out[$t]}"
  mkdir -p "$(dirname "$dest")"
  find "$(dirname "$dest")" -mindepth 1 -delete
  echo "Exporting ${preset[$t]} -> $dest"
  "$godot" --headless --quiet --path . "$mode" "${preset[$t]}" "$dest"
  [[ -f "$dest" ]] || { echo "Export of '${preset[$t]}' failed." >&2; exit 1; }
done

# Each build folder carries its version and the updater, so an unpacked game can update itself.
version_tag="${GITHUB_REF_NAME:-$(git describe --tags --always 2>/dev/null || echo dev)}"
for t in "${targets[@]}"; do
  folder="$(dirname "${out[$t]}")"
  echo "$version_tag" > "$folder/version.txt"
  if [[ "$t" == windows ]]; then
    cp scripts/update/Update.bat scripts/update/update.ps1 "$folder/"
  else
    cp scripts/update/update.sh "$folder/"
    chmod +x "$folder/update.sh"
  fi
done

if (( package )); then
  for t in "${targets[@]}"; do
    if [[ "$t" == windows ]]; then
      rm -f build/Curitibia-windows.zip
      (cd build/windows && zip -qr ../Curitibia-windows.zip .)
      echo "Packaged build/Curitibia-windows.zip"
    else
      chmod +x build/linux/Curitibia.x86_64
      tar -czf build/Curitibia-linux.tar.gz -C build/linux .
      echo "Packaged build/Curitibia-linux.tar.gz"
    fi
  done
fi
echo "Done. Output is in build/"
