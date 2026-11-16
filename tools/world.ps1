# world.ps1 - one switch for the two test worlds (DECISIONS 664, T228). Wraps dev-mode.ps1 + the ImportedData parking + pack-overlay.ps1.
#
# Why: the game files a placed object under name + hash. The pack's prefabs hash the platform id 167215, the Editor-imported prefabs in
# ImportedData hash their asset guid. So a save works only in the world it was built in:
#
#   PLAYER  the published pack (the Paradox Mods subscription, playset "Standard Mod Set") + the test bridge. ImportedData parked.
#           Optionally with a candidate build laid over the pack (-Build, and -Cok for its new .cok). Used for ALL play, tests, field tests
#           and baseline saves: it is what a player has.
#   ASSET   the dev mod (Mods\SkiResortMod) + the test bridge + ImportedData, the pack not loaded (playset "Standard Mod Set Unsub SRM").
#           For Editor imports and previews only. Its saves are throwaway, named "DEV ...".
#
#   -World Status   (default) ONE line, e.g.
#                     PLAYER: pack 1.0.1 + overlay r2/x abc123 (.cok laid), ImportedData parked, bridge on, playset OK
#                     ASSET: dev mod + ImportedData, bridge on, pack disabled, playset OK
#                   Exit 0 = a clean world with the right playset, 1 = mixed state or wrong playset. -Json: the same as one JSON object.
#   -World Player   switches to PLAYER. Without -Build the pack is put back to the shipped files (an active overlay is restored).
#                   -Build <folder>  lays that build over the pack ("dev" = the dev mod folder, Mods\SkiResortMod or the parked copy),
#                   -Cok also lays its ModularSkiResort.cok, -Label <text> names the overlay in Status. -NoBridge leaves the bridge out.
#                   -ReplaceAside renames an old parked dev copy to SkiResortMod-dev.stale-<time> when a fresh deploy in Mods would collide.
#   -World Asset    switches to ASSET (an active overlay is restored first).
#   -WhatIf         dry run: prints WOULD lines, moves and writes nothing. Allowed while Cities2 runs.
#
# It refuses a real switch while Cities2 runs. It CANNOT change playsets (they sync to Paradox, and the launcher and Skyve write them): it
# reads the active playset and says which one to pick. -Playset <name> names another expected Player playset (the Vanilla / Light variants).
# It does not set Steam launch options (--developerMode --uiDeveloperMode). PowerShell 5.1. Exit: 0 ok, 1 refused / mixed / wrong playset.
param(
    [ValidateSet('Status', 'Player', 'Asset')][string]$World = 'Status',
    [string]$Build,
    [switch]$Cok,
    [string]$Label,
    [switch]$NoBridge,
    [switch]$ReplaceAside,
    [string]$Playset,
    [switch]$WhatIf,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'
$DevMode = Join-Path $PSScriptRoot 'dev-mode.ps1'
$PackOverlay = Join-Path $PSScriptRoot 'pack-overlay.ps1'
$UserData = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$AsideDir = Join-Path $env:USERPROFILE 'Documents\CS2-mods-aside'
$PlayerPlayset = 'Standard Mod Set'
$AssetPlayset = 'Standard Mod Set Unsub SRM'

function Get-Dev { (& $DevMode -Mode Status -Json) -join '' | ConvertFrom-Json }
function Get-Pack {
    $raw = (& $PackOverlay -Do Status -Json) -join ''
    try { return $raw | ConvertFrom-Json } catch { return $null }
}

# The state as an object: world name, what is wrong, the one-line text.
function Get-WorldState {
    $d = Get-Dev
    $p = Get-Pack
    $devLoaded = $d.mod -like 'Mods*' -or $d.mod -like 'BOTH*'
    $bridgeOn = $d.bridge -eq 'Mods (loaded)'
    $importedLoaded = $d.imported -eq 'ImportedData (loaded)'
    $packOn = $d.enabled -eq 'enabled'
    $problems = @()
    $name = 'MIXED'
    if (-not $devLoaded -and -not $importedLoaded -and $packOn) { $name = 'PLAYER' }
    elseif ($devLoaded -and $importedLoaded -and -not $packOn) { $name = 'ASSET' }
    else {
        $problems += "dev mod $(if ($devLoaded) { 'loaded' } else { 'parked' })"
        $problems += "ImportedData $(if ($importedLoaded) { 'loaded' } else { 'parked' })"
        $problems += "pack $(if ($packOn) { 'enabled' } else { "not enabled ($($d.enabled))" }) in playset '$($d.playset)'"
    }
    $want = if ($name -eq 'ASSET') { $AssetPlayset } elseif ($Playset) { $Playset } else { $PlayerPlayset }
    $playsetOk = ($d.playset -eq $want)
    $bridgeText = if ($bridgeOn) { 'bridge on' } else { 'bridge off' }
    $overlayText = ''
    if ($name -eq 'PLAYER' -and $p) {
        $overlayText = if ($p.overlay) { " + overlay $($p.label)$(if ($p.cok) { ' (.cok laid)' })$(if (-not $p.sizesMatch) { ' [files changed since]' })" } else { '' }
    }
    $playsetText = if ($playsetOk) { 'playset OK' } else { "playset '$($d.playset)' WRONG: pick '$want' in the game" }
    $line = switch ($name) {
        'PLAYER' { "PLAYER: pack $(if ($p) { $p.version } else { '?' })$overlayText, ImportedData parked, $bridgeText, $playsetText" }
        'ASSET' { "ASSET: dev mod + ImportedData, $bridgeText, pack disabled, $playsetText" }
        default { "MIXED: $($problems -join ', '); run world.ps1 -World Player or -World Asset" }
    }
    [pscustomobject]@{
        world = $name; playsetOk = $playsetOk; wantPlayset = $want; playset = $d.playset; bridge = $bridgeOn
        overlay = [bool]($p -and $p.overlay); label = if ($p) { $p.label } else { '' }; cok = [bool]($p -and $p.cok)
        packVersion = if ($p) { $p.version } else { '' }; importedParked = -not $importedLoaded; line = $line
        clean = ($name -ne 'MIXED' -and $playsetOk); launch = $d.launch
    }
}

if ($World -eq 'Status') {
    $s = Get-WorldState
    if ($Json) { $s | ConvertTo-Json -Compress } else { $s.line }
    exit ([int](-not $s.clean))
}

# ---- switching -----------------------------------------------------------------------------------------------------------
if (-not $WhatIf -and (Get-Process -Name Cities2 -ErrorAction SilentlyContinue)) { Write-Host 'refused: Cities2 is running. Close the game first.'; exit 1 }
if ($WhatIf) { Write-Host "DRY RUN (-WhatIf): $World; nothing is moved or written" }

# dev-mode ends with its own status block; only its move lines and refusals are shown here (the final line comes from Get-WorldState).
function Invoke-DevMode([hashtable]$a) {
    $out = & $DevMode @a
    $out | Where-Object { $_ -match '^\s+(moved|skip|WOULD)|^refused' } | ForEach-Object { Write-Host $_ }
}
function Step([string]$text) { Write-Host "-- $text" }

try {
    $before = Get-WorldState
    Write-Host "now:  $($before.line)"

    # A fresh deploy in Mods beside an older parked copy collides in dev-mode (it refuses, "resolve by hand").
    $liveDev = Join-Path $UserData 'Mods\SkiResortMod'
    $asideDev = Join-Path $AsideDir 'SkiResortMod-dev'
    if ($World -eq 'Player' -and (Test-Path -LiteralPath $liveDev) -and (Test-Path -LiteralPath $asideDev)) {
        if (-not $ReplaceAside) { throw "refused: both Mods\SkiResortMod (a fresh deploy) and the parked $asideDev exist. Pass -ReplaceAside to rename the parked one to SkiResortMod-dev.stale-<time> (nothing is deleted)." }
        $stale = "$asideDev.stale-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Step "rename the parked dev copy -> $stale"
        if (-not $WhatIf) { Move-Item -LiteralPath $asideDev -Destination $stale }
    }

    if ($World -eq 'Player') {
        if ($Build) {
            # The overlay reads the build first (the dev copy is still in Mods or aside), then dev-mode parks it.
            Step "overlay the build over the pack$(if ($Cok) { ' with its .cok' })"
            $args1 = @{ Do = 'Overlay'; WhatIf = [bool]$WhatIf; Cok = [bool]$Cok }
            if ($Build -ne 'dev') { $args1.From = $Build }
            if ($Label) { $args1.Label = $Label }
            & $PackOverlay @args1
            if ($LASTEXITCODE -ne 0) { throw "pack-overlay Overlay failed (exit $LASTEXITCODE)" }
        } else {
            Step 'pack back to the shipped files (restore an active overlay)'
            $args1 = @{ Do = 'Restore'; WhatIf = [bool]$WhatIf }
            & $PackOverlay @args1
            if ($LASTEXITCODE -ne 0) { throw "pack-overlay Restore failed (exit $LASTEXITCODE)" }
        }
        Step "dev mod parked, ImportedData parked, bridge $(if ($NoBridge) { 'off' } else { 'on' })"
        $mode = if ($NoBridge) { 'Published' } else { 'Bridge' }
        Invoke-DevMode @{ Mode = $mode; ParkImported = $true; WhatIf = [bool]$WhatIf }
        if ($LASTEXITCODE -ne 0) { throw "dev-mode -Mode $mode failed (exit $LASTEXITCODE)" }
    } else {
        Step 'pack back to the shipped files (restore an active overlay)'
        $args1 = @{ Do = 'Restore'; WhatIf = [bool]$WhatIf }
        & $PackOverlay @args1
        if ($LASTEXITCODE -ne 0) { throw "pack-overlay Restore failed (exit $LASTEXITCODE)" }
        Step 'dev mod + bridge into Mods, ImportedData back'
        Invoke-DevMode @{ Mode = 'Dev'; WhatIf = [bool]$WhatIf }
        if ($LASTEXITCODE -ne 0) { throw "dev-mode -Mode Dev failed (exit $LASTEXITCODE)" }
    }
} catch {
    Write-Host $_.Exception.Message
    Write-Host "now:  $((Get-WorldState).line)"
    exit 1
}

if ($WhatIf) { Write-Host 'dry run done. Nothing changed.'; exit 0 }
$after = Get-WorldState
Write-Host "now:  $($after.line)"
if (-not $after.playsetOk) { Write-Host "TODO: pick the playset '$($after.wantPlayset)' in the game's Paradox Mods screen (this script never edits playsets)." }
if ($after.launch -notmatch 'uiDeveloperMode') { Write-Host 'TODO: launch with --developerMode --uiDeveloperMode (Steam launch options, or the direct start in tools/testbridge/README.md).' }
exit ([int](-not $after.clean))
