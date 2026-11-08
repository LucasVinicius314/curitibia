#!/usr/bin/env bash
# Downloads the WebRTC native extension that multiplayer needs on desktop into addons/webrtc_native.
# It is about 90 MB of binaries, so it is not committed (see .gitignore). The download is pinned to one release
# and verified against its SHA-256. Run it once after cloning, and in CI before exporting.
set -euo pipefail

VERSION="1.2.2-stable"
SHA256="98e9446921740d995bd9ca1be48798dc3c2ceed51e044a25ce18b3cff11f56e5"
URL="https://github.com/godotengine/webrtc-native/releases/download/${VERSION}/godot-extension-webrtc_native.zip"

cd "$(dirname "${BASH_SOURCE[0]}")/.."
if [[ -f addons/webrtc_native/webrtc_native.gdextension && "$(cat addons/webrtc_native/.version 2>/dev/null || true)" == "$VERSION" ]]; then
  echo "webrtc-native $VERSION is already in addons/webrtc_native"
  exit 0
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
echo "Downloading webrtc-native $VERSION ..."
curl -fL --retry 3 -o "$tmp/webrtc.zip" "$URL"
echo "$SHA256  $tmp/webrtc.zip" | sha256sum -c -
unzip -q "$tmp/webrtc.zip" -d "$tmp/extracted"

mkdir -p addons
rm -rf addons/webrtc_native
cp -r "$tmp/extracted/addons/webrtc_native" addons/
echo "$VERSION" > addons/webrtc_native/.version
echo "Installed addons/webrtc_native ($VERSION)"
