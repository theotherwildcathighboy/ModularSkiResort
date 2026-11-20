# Starts Cities2 without the launcher (testbridge README "Starting the game without the launcher"), after it copies the logs of the
# previous start aside. The game overwrites Player.log, Modding.log, SkiResortMod.Mod.log and the other *.log files at every start, so
# evidence from one start is gone after the next (2026-11-20: a "Mod failed to load" notice could not be traced). Use this script for
# every game start.
#   -Label   who starts it and why (folder name part), for example "r4-tex import Q"
#   -NoStart only archive (for example right after the game exits)
#   -Keep    archive folders kept (oldest are deleted first), default 40
# Archive: <game user data>\LogArchive\<yyyyMMdd-HHmmss>_<label>\ with the top-level *.log (Player.log, Player-prev.log), Logs\*.log
# (Modding.log, SkiResortMod.Mod.log, SceneFlow.log, the other mods' logs) and START.txt (label, world status).
# Exit 0 = archived (and started), 1 = Cities2 already runs, 2 = no logs and nothing to do.
param(
    [string]$Label = 'start',
    [switch]$NoStart,
    [int]$Keep = 40
)
$ErrorActionPreference = 'Stop'

if (Get-Process Cities2 -ErrorAction SilentlyContinue) { Write-Output 'start-game: Cities2 already runs; archive skipped, nothing started'; exit 1 }

$data = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$logs = @(Get-ChildItem -Path $data -Filter '*.log' -File -ErrorAction SilentlyContinue) +
        @(Get-ChildItem -Path (Join-Path $data 'Logs') -Filter '*.log' -File -ErrorAction SilentlyContinue)
$safe = ($Label -replace '[^A-Za-z0-9._-]+', '-').Trim('-')
if ($safe.Length -gt 40) { $safe = $safe.Substring(0, 40) }
$root = Join-Path $data 'LogArchive'

if ($logs.Count -gt 0) {
    $dir = Join-Path $root ((Get-Date -Format 'yyyyMMdd-HHmmss') + '_' + $safe)
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $dir 'Logs') | Out-Null
    foreach ($f in $logs) {
        $to = if ($f.DirectoryName -eq $data) { $dir } else { Join-Path $dir 'Logs' }
        Copy-Item -LiteralPath $f.FullName -Destination $to
    }
    $world = ''
    $worldPs1 = Join-Path $PSScriptRoot 'world.ps1'
    if (Test-Path $worldPs1) {
        try { $world = (& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $worldPs1 -World Status 2>&1) -join ' ' } catch { $world = "world status failed: $_" }
    }
    $newest = ($logs | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
    @(
        "label: $Label"
        "archived: $(Get-Date -Format 's') (logs of the start before this one; newest log written $($newest.ToString('s')))"
        "files: $($logs.Count)"
        "world now: $world"
    ) | Set-Content -Path (Join-Path $dir 'START.txt') -Encoding UTF8
    Write-Output "start-game: $($logs.Count) log(s) archived to $dir"

    $old = @(Get-ChildItem -Path $root -Directory | Sort-Object Name -Descending | Select-Object -Skip $Keep)
    foreach ($o in $old) { Remove-Item -LiteralPath $o.FullName -Recurse -Force }
    if ($old.Count -gt 0) { Write-Output "start-game: $($old.Count) old archive folder(s) removed (keep $Keep)" }
} elseif ($NoStart) {
    Write-Output 'start-game: no logs found, nothing to do'; exit 2
}

if ($NoStart) { exit 0 }

$env:SteamAppId = '949230'; $env:SteamGameId = '949230'; $env:SteamOverlayGameId = '949230'
$g = 'C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II'
Start-Process -FilePath "$g\Cities2.exe" -WorkingDirectory $g -ArgumentList '--developerMode', '--uiDeveloperMode' | Out-Null
Write-Output 'start-game: Cities2 started (port 9444 opens after 1 to 2 minutes; then testbridge\wait-ready.ps1)'
exit 0
