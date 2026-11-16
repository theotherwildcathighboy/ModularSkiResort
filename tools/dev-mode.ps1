# dev-mode.ps1 - switch the game between the PUBLISHED mod (Paradox Mods subscription) and the DEV setup (local build + test bridge).
#
# Why: since the 1.0.0 launch the game must sometimes run the subscribed release (baseline saves, player-eye checks) and sometimes
# the dev build (bridge sessions, deploy tests). Both copies loaded at once = duplicate prefab IDs and two assemblies. The game loads
# mods from ANY folder under its user data folder, so the parked copies live outside it, in %USERPROFILE%\Documents\CS2-mods-aside.
#
#   -Mode Status     (default) read-only report: where each copy is, the subscription, the active playset, launch options.
#   -Mode Dev        moves SkiResortMod-dev + SkiTestBridge from the aside folder into Mods.
#   -Mode Published  moves them back out of Mods into the aside folder.
#   -Mode Bridge     published mod + test bridge only: SkiTestBridge in Mods, SkiResortMod-dev parked. For bridge runs that must
#                    measure the RELEASED build (baseline saves, tools/loadcheck.ps1 baselines).
#   -WhatIf          dry run: prints what would move, moves nothing, allowed while Cities2 runs (conflicts still refuse).
#   -Json            with -Mode Status: ONE compact JSON object instead of the text report (tools/world.ps1 reads it).
#   -ParkImported    with -Mode Published or Bridge: also park the Editor-imported ImportedData folder (T197: dev prefab copies beside the
#                    published .cok give ~1,300 "Duplicate prefab ID" lines). Opt-in, for that duplicate-flood check only. Without the
#                    switch every mode leaves ImportedData in place, and puts it back if an earlier -ParkImported parked it.
#   NOTE: saves built on the dev machine (the release baseline saves) carry the IDs of the ImportedData prefab copies. With ImportedData
#   parked their lifts, stations, towers, lodges and runs do not resolve ("Unknown prefab ID", the mod sees 0 lifts; 2026-11-14, same on the
#   original 1.0.0 pack). Such a save is only a valid test with ImportedData present. A player-made save (published pack only) needs it parked.
#
# What the script does NOT do (by design):
#   - Change playsets. Playset files sync to Paradox and are also written by the launcher and Skyve. Keep a playset without the
#     subscription for dev work and pick it in the game; the script reports whether the ACTIVE playset has 167215 enabled.
#   - Set Steam launch options (Steam rewrites localconfig.vdf while it runs). Dev mode needs --developerMode --uiDeveloperMode:
#     set them in Steam, or use the direct start in tools/testbridge/README.md, which passes them itself.
#   - Guard deploys. A deploying `dotnet build` recreates Mods\SkiResortMod in any mode: run -Mode Status after one.
#
# Refuses to move anything while Cities2 runs. PowerShell 5.1. Exit: 0 ok, 1 refused / conflict.
param([ValidateSet('Status', 'Dev', 'Published', 'Bridge')][string]$Mode = 'Status', [switch]$ParkImported, [switch]$Json, [switch]$WhatIf)

$ErrorActionPreference = 'Stop'
$ModId = '167215'
$UserData = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$ModsDir = Join-Path $UserData 'Mods'
$AsideDir = Join-Path $env:USERPROFILE 'Documents\CS2-mods-aside'
$Pairs = @(
    @{ Local = 'SkiResortMod'; Aside = 'SkiResortMod-dev' },
    @{ Local = 'SkiTestBridge'; Aside = 'SkiTestBridge' }
)

function Get-Where($pair) {
    $inMods = Test-Path (Join-Path $ModsDir $pair.Local)
    $inAside = Test-Path (Join-Path $AsideDir $pair.Aside)
    if ($inMods -and $inAside) { 'BOTH (conflict)' } elseif ($inMods) { 'Mods (loaded)' } elseif ($inAside) { 'aside (parked)' } else { 'missing' }
}

function Get-Subscription {
    $dirs = @(Get-ChildItem (Join-Path $UserData '.cache\Mods\pdx_mods') -Directory -Filter "$($ModId)_*" -ErrorAction SilentlyContinue)
    $installed = if ($dirs.Count) { ($dirs | ForEach-Object Name) -join ', ' } else { 'not installed' }
    $enabled = 'unknown'
    $playset = 'unknown'
    $cfgPath = Join-Path $UserData '.cache\Mods\playset_config.json'
    if (Test-Path $cfgPath) {
        try {
            $cfg = Get-Content $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $active = $cfg.playsets | Where-Object { $_.id -eq $cfg.activePlaysetId } | Select-Object -First 1
            if ($active) {
                $playset = $active.presentation.name
                $row = $active.mods | Where-Object { $_.source -eq 'pdx_mods' -and $_.sourceId -eq $ModId } | Select-Object -First 1
                $enabled = if (-not $row) { 'not in playset' } elseif ($row.isEnabled) { 'enabled' } else { 'disabled' }
            }
        } catch { $enabled = "unreadable ($($_.Exception.Message))" }
    }
    [pscustomobject]@{ Installed = $installed; Playset = $playset; Enabled = $enabled }
}

function Get-LaunchOptions {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { return 'unknown (Steam path not found)' }
    foreach ($cfg in Get-ChildItem (Join-Path $steam 'userdata\*\config\localconfig.vdf') -ErrorAction SilentlyContinue) {
        $text = Get-Content $cfg.FullName -Raw -Encoding UTF8
        $m = [regex]::Match($text, '"949230"\s*\{(?:[^{}]|\{[^{}]*\})*?"LaunchOptions"\s*"([^"]*)"')
        if ($m.Success) { return "'$($m.Groups[1].Value)'" }
    }
    'none set'
}

function Show-Status {
    if ($Json) {
        $s = Get-Subscription
        [ordered]@{
            mod = (Get-Where $Pairs[0]); bridge = (Get-Where $Pairs[1]); imported = (Get-ImportedState)
            installed = $s.Installed; playset = $s.Playset; enabled = $s.Enabled; launch = (Get-LaunchOptions)
        } | ConvertTo-Json -Compress
        return
    }
    "Ski mod switch status"
    foreach ($p in $Pairs) { "  {0,-14} {1}" -f $p.Local, (Get-Where $p) }
    "  {0,-14} {1}" -f 'ImportedData', (Get-ImportedState)
    $s = Get-Subscription
    "  Subscription   $ModId $($s.Installed); active playset '$($s.Playset)': $($s.Enabled)"
    "  Launch options $(Get-LaunchOptions)"
    $devLoaded = (Get-Where $Pairs[0]) -ne 'aside (parked)' -and (Get-Where $Pairs[0]) -ne 'missing'
    $bridgeLoaded = (Get-Where $Pairs[1]) -eq 'Mods (loaded)'
    $mode = if ($devLoaded -and $s.Enabled -eq 'enabled') { 'CONFLICT: dev copy AND subscription both load' }
            elseif ($devLoaded) { 'DEV' }
            elseif ($s.Enabled -eq 'enabled' -and $bridgeLoaded) { 'BRIDGE (published mod + test bridge)' }
            elseif ($s.Enabled -eq 'enabled') { 'PUBLISHED' }
            else { 'NONE (no copy of the mod loads)' }
    "  => mode: $mode"
}

function Move-Pair($from, $to, $label) {
    if (Test-Path $to) { throw "refused: $to already exists ($label). Resolve by hand." }
    if (-not (Test-Path $from)) { "  skip $label (not at $from)"; return }
    if ($WhatIf) { "  WOULD move $label -> $to"; return }
    New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
    Move-Item -LiteralPath $from -Destination $to
    "  moved $label -> $to"
}

# ImportedData holds the Editor-imported dev copies of our prefabs (Ski*). Beside the published .cok they collide ("Duplicate prefab ID",
# T197), so -ParkImported (Published and Bridge only) parks the whole folder (a rename on the same volume, nothing is copied or deleted) and
# leaves an empty one; every other call puts it back. Refused when the live folder holds anything in a mode that wants the parked one back (no merge, no overwrite).
$ImportedDir = Join-Path $UserData 'ImportedData'
$ImportedAside = Join-Path $AsideDir 'ImportedData'

function Get-ImportedState {
    $live = Test-Path $ImportedDir
    $liveCount = if ($live) { @(Get-ChildItem $ImportedDir -Force -ErrorAction SilentlyContinue).Count } else { 0 }
    $aside = Test-Path $ImportedAside
    if ($aside -and $liveCount -gt 0) { return 'BOTH (conflict)' }
    if ($aside) { return 'aside (parked)' }
    if ($liveCount -gt 0) { return 'ImportedData (loaded)' }
    'empty'
}

function Set-Imported([bool]$wantLoaded) {
    $state = Get-ImportedState
    if ($state -like 'BOTH*') { throw "refused: ImportedData exists in the game folder AND in $ImportedAside. Resolve by hand." }
    if ($WhatIf -and (($wantLoaded -and $state -eq 'aside (parked)') -or (-not $wantLoaded -and $state -eq 'ImportedData (loaded)'))) {
        "  WOULD move ImportedData $(if ($wantLoaded) { 'back into the game folder' } else { 'aside (parked)' })"
        return
    }
    if ($wantLoaded -and $state -eq 'aside (parked)') {
        if (Test-Path $ImportedDir) { Remove-Item -LiteralPath $ImportedDir -Force }   # an empty folder only (the state check above)
        Move-Item -LiteralPath $ImportedAside -Destination $ImportedDir
        "  moved ImportedData back -> $ImportedDir"
    }
    elseif (-not $wantLoaded -and $state -eq 'ImportedData (loaded)') {
        New-Item -ItemType Directory -Force $AsideDir | Out-Null
        Move-Item -LiteralPath $ImportedDir -Destination $ImportedAside
        New-Item -ItemType Directory -Force $ImportedDir | Out-Null
        "  moved ImportedData -> $ImportedAside (an empty folder is left)"
    }
}

if ($Mode -ne 'Status') {
    if (-not $WhatIf -and (Get-Process -Name Cities2 -ErrorAction SilentlyContinue)) { Write-Host 'refused: Cities2 is running. Close the game first.'; exit 1 }
    try {
        Set-Imported (($Mode -eq 'Dev') -or -not $ParkImported)
        foreach ($p in $Pairs) {
            $local = Join-Path $ModsDir $p.Local
            $aside = Join-Path $AsideDir $p.Aside
            $wantLoaded = ($Mode -eq 'Dev') -or ($Mode -eq 'Bridge' -and $p.Local -eq 'SkiTestBridge')
            if ($wantLoaded) { if (-not (Test-Path $local)) { Move-Pair $aside $local $p.Local } }
            elseif (Test-Path $local) { Move-Pair $local $aside $p.Local }
        }
    } catch { Write-Host $_.Exception.Message; Show-Status; exit 1 }
}

Show-Status
$s = Get-Subscription
if ($Mode -eq 'Dev' -and $s.Enabled -eq 'enabled') {
    "  TODO: pick a playset without Modular Ski Resort (or disable it in '$($s.Playset)') before launching."
}
if ($Mode -eq 'Dev' -or $Mode -eq 'Bridge') { "  TODO: launch with --developerMode --uiDeveloperMode (Steam launch options or the bridge direct start)." }
if (($Mode -eq 'Published' -or $Mode -eq 'Bridge') -and $s.Enabled -ne 'enabled') {
    "  TODO: enable Modular Ski Resort ($ModId) in the active playset before launching."
}
exit 0
