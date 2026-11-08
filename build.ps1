<#
.SYNOPSIS
  Builds Curitibia for Windows and/or Linux with the Godot export presets in export_presets.cfg.

.EXAMPLE
  .\build.ps1                      # Windows and Linux, release
  .\build.ps1 -Target windows
  .\build.ps1 -Target linux -Debug
  .\build.ps1 -Package             # also make build\Curitibia-windows.zip and build\Curitibia-linux.tar.gz
  .\build.ps1 -InstallTemplates    # download the export templates that match your Godot version, then exit

.NOTES
  Finds Godot (the mono *console* exe) from, in order: -Godot, the GODOT environment variable, a one-line
  .godot-path file in the repository root (git-ignored, so it can hold your local path), then PATH.
  Output goes to build\windows and build\linux.
#>
param(
    [ValidateSet('windows', 'linux', 'all')] [string] $Target = 'all',
    [switch] $Debug,
    [switch] $Package,
    [switch] $InstallTemplates,
    [string] $Godot
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Godot waits for the .NET build's output pipe to close, and the compiler / MSBuild servers keep it open for about
# ten minutes after the build, so every export would stall that long. Run the builds without the servers.
$env:UseSharedCompilation = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Find-Godot {
    if ($Godot) { return $Godot }
    if ($env:GODOT) { return $env:GODOT }
    if (Test-Path .godot-path) {
        $fromFile = (Get-Content .godot-path -TotalCount 1).Trim()
        if ($fromFile) { return $fromFile }
    }
    foreach ($name in 'godot-mono', 'godot') {
        $found = Get-Command $name -ErrorAction SilentlyContinue
        if ($found) { return $found.Source }
    }
    throw "Godot not found. Pass -Godot <path to the mono console exe>, set the GODOT environment variable, or put the path in a .godot-path file."
}

$godotExe = Find-Godot
if (-not (Test-Path $godotExe)) { throw "Godot not found at '$godotExe'." }

# "4.7.2.stable.mono.official.abc123" -> "4.7.2.stable.mono", the name of the export templates folder.
$fullVersion = (& $godotExe --version).Trim()
$version = ($fullVersion -split '\.')[0..4] -join '.'
if ($version -notmatch '\.mono$') { throw "This needs the mono (C#) build of Godot, but '$godotExe' reports $fullVersion." }
$templates = Join-Path $env:APPDATA "Godot\export_templates\$version"

function Install-Templates {
    $tag = ($version -replace '\.mono$', '' -replace '^(\d+\.\d+(\.\d+)?)\.(\w+)$', '$1-$3')
    $file = "Godot_v$tag" + "_mono_export_templates.tpz"
    $base = "https://github.com/godotengine/godot/releases/download/$tag"
    $work = Join-Path ([IO.Path]::GetTempPath()) "curitibia-templates-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory $work | Out-Null
    try {
        Write-Host "Downloading $file (about 1.2 GB) ..."
        Invoke-WebRequest "$base/$file" -OutFile (Join-Path $work $file)
        Invoke-WebRequest "$base/SHA512-SUMS.txt" -OutFile (Join-Path $work 'SHA512-SUMS.txt')
        $expected = (Select-String -Path (Join-Path $work 'SHA512-SUMS.txt') -Pattern ([regex]::Escape($file))).Line.Split(' ')[0]
        $actual = (Get-FileHash (Join-Path $work $file) -Algorithm SHA512).Hash
        if ($actual -ne $expected.ToUpper()) { throw "Checksum mismatch for $file; not installing it." }

        Write-Host "Installing to $templates ..."
        Copy-Item (Join-Path $work $file) (Join-Path $work 'templates.zip')
        Expand-Archive (Join-Path $work 'templates.zip') (Join-Path $work 'x')
        New-Item -ItemType Directory $templates -Force | Out-Null
        Copy-Item (Join-Path $work 'x\templates\*') $templates -Recurse -Force
        Write-Host "Export templates installed."
    }
    finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($InstallTemplates) { Install-Templates; return }
if (-not (Test-Path (Join-Path $templates 'version.txt'))) {
    throw "Export templates for $version are not installed (looked in $templates). Run: .\build.ps1 -InstallTemplates"
}

$presets = @{
    windows = @{ Name = 'Windows Desktop'; Out = 'build\windows\Curitibia.exe' }
    linux   = @{ Name = 'Linux';           Out = 'build\linux\Curitibia.x86_64' }
}
$targets = if ($Target -eq 'all') { 'windows', 'linux' } else { @($Target) }
$mode = if ($Debug) { '--export-debug' } else { '--export-release' }

if (-not (Test-Path 'libs\p2p-signaling-server\clients\godot-csharp\src\P2PLobby.cs')) {
    throw 'The p2p-signaling-server submodule is missing. Run: git submodule update --init'
}
if (-not (Test-Path 'addons\webrtc_native\webrtc_native.gdextension')) {
    throw 'The WebRTC native extension is missing, so the build would have no multiplayer. Run: .\scripts\fetch-webrtc.ps1'
}

Write-Host "Importing project ($fullVersion) ..."
& $godotExe --headless --quiet --path . --import | Out-Null

foreach ($t in $targets) {
    $preset = $presets[$t]
    $out = $preset.Out
    New-Item -ItemType Directory (Split-Path $out) -Force | Out-Null
    Get-ChildItem (Split-Path $out) -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force

    Write-Host "Exporting $($preset.Name) -> $out"
    & $godotExe --headless --quiet --path . $mode $preset.Name $out
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $out)) { throw "Export of '$($preset.Name)' failed (exit code $LASTEXITCODE)." }
}

# Each build folder carries its version and the updater, so an unpacked game can update itself.
$versionTag = $env:GITHUB_REF_NAME
if (-not $versionTag) { try { $versionTag = (git describe --tags --always 2>$null) } catch { } }
if (-not $versionTag) { $versionTag = 'dev' }
foreach ($t in $targets) {
    $folder = Split-Path $presets[$t].Out
    Set-Content (Join-Path $folder 'version.txt') $versionTag -Encoding ascii
    if ($t -eq 'windows') { Copy-Item scripts\update\Update.bat, scripts\update\update.ps1 $folder -Force }
    else { Copy-Item scripts\update\update.sh $folder -Force }
}

if ($Package) {
    foreach ($t in $targets) {
        if ($t -eq 'windows') {
            $zip = 'build\Curitibia-windows.zip'
            Remove-Item $zip -ErrorAction SilentlyContinue
            Compress-Archive -Path 'build\windows\*' -DestinationPath $zip
            Write-Host "Packaged $zip"
        }
        else {
            # tar packs the folder's contents (no wrapping folder); the Linux binary needs `chmod +x` after unpacking (NTFS has no mode bits).
            tar -czf 'build\Curitibia-linux.tar.gz' -C build\linux .
            Write-Host "Packaged build\Curitibia-linux.tar.gz"
        }
    }
}

Write-Host "Done. Output is in build\"
