<#
.SYNOPSIS
  Downloads the WebRTC native extension that multiplayer needs on desktop into addons\webrtc_native.
.DESCRIPTION
  About 90 MB of binaries, so it is not committed (see .gitignore). The download is pinned to one release and
  verified against its SHA-256. Run it once after cloning.
#>
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$version = '1.2.2-stable'
$sha256 = '98e9446921740d995bd9ca1be48798dc3c2ceed51e044a25ce18b3cff11f56e5'
$url = "https://github.com/godotengine/webrtc-native/releases/download/$version/godot-extension-webrtc_native.zip"

Set-Location (Join-Path $PSScriptRoot '..')
$versionFile = 'addons\webrtc_native\.version'
if ((Test-Path 'addons\webrtc_native\webrtc_native.gdextension') -and (Test-Path $versionFile) -and ((Get-Content $versionFile -Raw).Trim() -eq $version)) {
    Write-Host "webrtc-native $version is already in addons\webrtc_native"
    return
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('webrtc-native-' + [guid]::NewGuid())
New-Item -ItemType Directory $tmp | Out-Null
try {
    $zip = Join-Path $tmp 'webrtc.zip'
    Write-Host "Downloading webrtc-native $version ..."
    Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha256) { throw "SHA-256 mismatch: expected $sha256, got $actual" }
    Expand-Archive $zip (Join-Path $tmp 'extracted') -Force

    New-Item -ItemType Directory addons -Force | Out-Null
    if (Test-Path 'addons\webrtc_native') { Remove-Item 'addons\webrtc_native' -Recurse -Force }
    Copy-Item (Join-Path $tmp 'extracted\addons\webrtc_native') 'addons\webrtc_native' -Recurse
    Set-Content $versionFile $version -Encoding ascii
    Write-Host "Installed addons\webrtc_native ($version)"
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
