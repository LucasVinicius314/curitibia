#!/usr/bin/env bash
# Updates this Curitibia folder to the latest GitHub release: ./update.sh (or `bash update.sh`).
# Your settings and saves live in ~/.local/share/godot, outside this folder, so they are not touched.
set -euo pipefail

# Everything is inside a function and the file ends with `exit`, so bash has read the whole script before
# the update below replaces it.
main() {
  local repo="LucasVinicius314/curitibia" asset="Curitibia-linux.tar.gz"
  local dir; dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

  # Compare each process executable with the game binary (a command line may be relative, so match on /proc).
  local exe
  for exe in /proc/[0-9]*/exe; do
    if [[ "$(readlink -f "$exe" 2>/dev/null)" == "$dir/Curitibia.x86_64" ]]; then
      echo "Curitibia is running. Close the game and run the update again." >&2
      return 1
    fi
  done

  echo "Checking for the latest release ..."
  local latest
  latest="$(curl -fsSL "https://api.github.com/repos/$repo/releases/latest" \
    | grep -m1 '"tag_name"' | sed -E 's/.*"tag_name": *"([^"]+)".*/\1/' || true)"
  if [[ -z "$latest" ]]; then
    echo "Could not find the latest release." >&2
    return 1
  fi

  local current=""
  [[ -f "$dir/version.txt" ]] && current="$(tr -d '[:space:]' < "$dir/version.txt")"
  if [[ "$current" == "$latest" ]]; then
    echo "Already up to date ($latest)."
    return 0
  fi

  echo "Updating '$current' -> $latest"
  local tmp; tmp="$(mktemp -d)"
  trap "rm -rf '$tmp'" EXIT  # expanded now: $tmp is out of scope by the time the trap runs
  curl -fL --progress-bar "https://github.com/$repo/releases/download/$latest/$asset" -o "$tmp/$asset"
  mkdir "$tmp/new"
  tar -xzf "$tmp/$asset" -C "$tmp/new"

  # The data folder holds the game's assemblies; drop the old one so nothing stale is left behind.
  rm -rf "$dir"/data_*
  # Remove each item before copying its replacement, so a file in use cannot make the copy fail halfway.
  local item name
  for item in "$tmp"/new/* "$tmp"/new/.[!.]*; do
    [[ -e "$item" ]] || continue
    name="$(basename "$item")"
    [[ "$name" == update.sh ]] && continue
    rm -rf "${dir:?}/$name"
    cp -a "$item" "$dir/$name"
  done
  chmod +x "$dir/Curitibia.x86_64"
  if [[ -f "$tmp/new/update.sh" ]]; then
    # Replace this script by renaming, so the copy that is running keeps its own file.
    cp "$tmp/new/update.sh" "$dir/update.sh.new"
    chmod +x "$dir/update.sh.new"
    mv -f "$dir/update.sh.new" "$dir/update.sh"
  fi
  echo "$latest" > "$dir/version.txt"
  echo "Updated to $latest."
}

main "$@"
exit $?
