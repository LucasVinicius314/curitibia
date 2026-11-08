# Updates this Curitibia folder to the latest GitHub release. Run it with Update.bat (double-click).
# Your settings and saves live in %APPDATA%\Godot, outside this folder, so they are not touched.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = 'LucasVinicius314/curitibia'
$asset = 'Curitibia-windows.zip'
$dir = $PSScriptRoot
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('curitibia-update-' + [guid]::NewGuid())

try {
    $running = Get-Process -Name Curitibia -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($dir, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) {
        Write-Host 'Curitibia is running. Close the game and run the update again.'
        exit 1
    }

    Write-Host 'Checking for the latest release ...'
    $latest = (Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers @{ 'User-Agent' = 'curitibia-updater' }).tag_name
    if (-not $latest) { throw 'Could not find the latest release.' }

    $versionFile = Join-Path $dir 'version.txt'
    $current = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw).Trim() } else { '' }
    if ($current -eq $latest) {
        Write-Host "Already up to date ($latest)."
        exit 0
    }

    Write-Host "Updating '$current' -> $latest"
    New-Item -ItemType Directory $tmp | Out-Null
    Invoke-WebRequest "https://github.com/$repo/releases/download/$latest/$asset" -OutFile (Join-Path $tmp $asset) -UseBasicParsing
    Expand-Archive (Join-Path $tmp $asset) (Join-Path $tmp 'new') -Force

    # The game's data folder holds its assemblies; drop the old one so nothing stale is left behind.
    Get-ChildItem $dir -Directory -Filter 'data_*' | Remove-Item -Recurse -Force
    # This script and Update.bat go last: they may be the ones running.
    Copy-Item (Join-Path $tmp 'new\*') $dir -Recurse -Force -Exclude 'update.ps1', 'Update.bat'
    foreach ($name in 'Update.bat', 'update.ps1') {
        $fresh = Join-Path $tmp "new\$name"
        if (Test-Path $fresh) { Copy-Item $fresh (Join-Path $dir $name) -Force }
    }
    Set-Content (Join-Path $dir 'version.txt') $latest -Encoding ascii
    Write-Host "Updated to $latest."
}
catch {
    Write-Host "Update failed: $($_.Exception.Message)"
    exit 1
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
