<#
.SYNOPSIS
  Old-save load check: loads a release's baseline save through the test bridge and checks it as far as a machine can
  (docs/research/field/old-save-checklist.md; the bridge op `loadcheck`, tools/testbridge/TestBridgeSystem.LoadCheck.cs).

.DESCRIPTION
  Needs a running game with the test bridge READY (--developerMode --uiDeveloperMode, live reload off). It never builds, deploys or
  starts the game. The world (DECISIONS 664, T228): the check runs in PLAYER, the published pack with the candidate build laid over
  it (tools/world.ps1 -World Player -Build dev [-Cok]), ImportedData parked, playset "Standard Mod Set". The script asks
  `world.ps1 -World Status -Json` first and stops (exit 2) in any other world; -AnyWorld runs anyway, with the world gate off (the
  result is then no release evidence). A save made on the pack alone resolves its prefabs through the pack; a dev-made save (Editor
  ImportedData ids) shows as "Unknown prefab ID" lines and placeholder boxes. Gate 2 (T228) FAILs part (a) on any such line naming a
  Ski prefab after the load click (lines before it come from the Load Game list: environment), and on a second copy of our prefabs beside the pack ("Duplicate prefab ID" naming Ski with an asset other than the pack's
  own .cok). The pack's own duplicate lines (about 160 per start, asset = pdx_mods/167215_N/ModularSkiResort.cok) are info.

  Steps:
    1. Copies the baseline save (read-only, in release/saves-by-release/) into the game's Saves folder as loadcheck_<stem>.cok with a
       FRESH .cid beside it (T205: observed 2026-11-16: with its own .cid a copy lists unless a byte-identical save is listed already; then
       that save is loaded by its own name, read-only, never saved over). Stage the copy with the game CLOSED (-StageOnly does only
       this and exits): the game indexes the Saves folder at start. The original never loads and never gets saved over.
    2. Op loadcheck begin (log offsets), then the page helper __dload (T224: the pause menu by DOM mouse events, no OS input, so no
       window focus and no mouse; helpers.js), then tools/testbridge/wait-ready.ps1.
    3. Op loadcheck report: (a) log scan since the load, (b) entity census, (c) billing before and after one in-game month at
       sim speed 8. Polled until done.
    4. With -Resave, part (d): census, save under a new bridge* name, load it again, census again, compare.
    5. Writes the JSON report to docs/research/field/<date>/loadcheck/ and prints PASS / FAIL per part.
       The census baseline lives beside the save: release/saves-by-release/<stem>.census.json. The first run writes it;
       later runs diff their census against it (part b). -WriteBaseline replaces it.

  Autosave is switched off before the load and again after it (a load builds a new world with autosave ON). With -NoLoad it is put back to the state
  it was found in; after a load it stays OFF in the loaded city. The sim speed returns to what it was.

  Exit: 0 every part PASS, 1 a part FAILed, 2 a precondition failed (game, bridge, save), 3 the run broke off.

.PARAMETER Save
  The baseline .cok. Default: the newest *.cok in release/saves-by-release/.

.PARAMETER NoLoad
  Check the city that is loaded now (no copy, no load). The log scan then covers the whole session. The baseline diff runs
  only when -Save names the save that city came from.

.PARAMETER NoMonth
  Skip the in-game month (part c is then skipped).

.PARAMETER DiscardCity
  Allow the load while a city is open. The pause menu's load answers Yes to discarding it, so unsaved progress is lost.
  Without it the script refuses unless the game is at the main menu.

.PARAMETER Resave
  Also run part (d): save under a bridge* name, reload, compare the census.

.PARAMETER AnyWorld
  Run in a world other than a clean PLAYER (tools/world.ps1 -World Status). The world gate is off and the report says so.

.PARAMETER ResaveName
  With -Resave: the save name for part (d) (default bridgelc<MMddHHmmss>). tools/resave-baseline.ps1 uses it for "SRM Baseline <x.y.z> ...".
  The bridge stops at an existing name (overwrite dialog), so the name must be new.

.PARAMETER StageOnly
  Copy the baseline save into the Saves folder with a fresh .cid and exit. Run it with the game closed, before the launch, so the game
  lists the copy (T205). Needs no game, no bridge and no node.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\loadcheck.ps1
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\loadcheck.ps1 -Resave -MonthTimeoutMinutes 60
#>
param(
    [string]$Save,
    [switch]$NoLoad,
    [switch]$NoMonth,
    [switch]$Resave,
    [switch]$WriteBaseline,
    [switch]$DiscardCity,
    [switch]$AnyWorld,
    [switch]$StageOnly,
    [string]$ResaveName,
    [int]$MonthTimeoutMinutes = 45,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$Repo = Split-Path -Parent $PSScriptRoot
$Bridge = Join-Path $PSScriptRoot 'testbridge'
$CallJs = Join-Path $Bridge 'bridge-call.mjs'
$Helpers = Join-Path $Bridge 'helpers.js'
$WaitReady = Join-Path $Bridge 'wait-ready.ps1'
$SavesByRelease = Join-Path $Repo 'release\saves-by-release'
$UserData = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$Started = Get-Date

function Stop-Check([int]$code, [string]$message) {
    Write-Output "loadcheck: $message"
    exit $code
}

function Invoke-Node([string[]]$arguments) {
    # No 2>&1: in PowerShell 5.1 a native stderr line becomes an error record, which Stop turns into a throw.
    $raw = & node $CallJs @arguments
    $text = ($raw | ForEach-Object { "$_" }) -join "`n"
    if ($LASTEXITCODE -eq 2) { Stop-Check 2 "bridge unreachable: $text" }
    try { return $text | ConvertFrom-Json } catch { throw "bridge-call output is not JSON: $text" }
}

function Invoke-Bridge($request) {
    $json = $request | ConvertTo-Json -Compress -Depth 10
    $reply = Invoke-Node @('--op', [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)))
    if (-not $reply.ok) { throw "bridge op $($request.op) $($request.action): $($reply.error)" }
    return $reply
}

function Invoke-Page([string]$expression, [int]$timeoutSec = 60) {
    $reply = Invoke-Node @('--js', [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($expression)), '--timeout', "$timeoutSec")
    if (-not $reply.ok) { throw "page call failed: $($reply.error)" }
    return $reply.value
}

function Show-Game {
    # T224: the load, the save and the quit are DOM mouse events on the page (helpers.js __dload / __dsave / __dexit), not OS input, so the
    # game window needs no focus and the script never takes the mouse. This only reports the focus state (the old script forced the window
    # to the front with SetForegroundWindow, which fought the owner's own windows).
    $st = Invoke-Bridge @{ op = 'status' }
    Write-Output "  focus: $($st.focused) (DOM clicks, focus not needed)"
}

# T205: the game lists a save by its .cid (32 hex characters, the asset id) beside the .cok. A copy made without one was never listed,
# even after a restart, and a copy WITH the original's .cid would duplicate its asset id. So a staged copy gets its own fresh id.
# Observed 2026-11-16: with a fresh .cid the ashflat copy listed (its original is not in Saves); the light copy did NOT, while the byte-identical
# lightvale1.cok sat in Saves. So a duplicate of a listed save stays hidden (hypothesis: the embedded save id; the AssetDatabase source is not in
# the 1.6.4f1 decompile). The game indexes the Saves folder at its start:
# stage with the game closed (-StageOnly). A copy staged while the game runs may stay unlisted until the next start.
function Install-SaveCopy([string]$source, [string]$gameName) {
    $savesRoot = Join-Path $UserData 'Saves'
    $savesDir = Get-ChildItem $savesRoot -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $savesDir) { Stop-Check 2 "no Saves folder under $savesRoot" }
    # T205 (observed in game 2026-11-16, not a controlled test): the game lists a copy when it has its own .cid and no byte-identical save is listed already
    # (a copy of lightvale1.cok stayed hidden while the original sat in Saves; a copy of a baseline whose original is gone lists fine).
    # So an identical save that is already in Saves is used by its own name: nothing is staged, nothing is written.
    # Twins: of two byte-identical saves the game lists only ONE, and which one changes between game starts (2026-11-17: the staged
    # loadcheck_* copy was listed and the original hidden; for another save it was the other way round). So every identical save is a
    # candidate name ($script:StagedNames, originals first) and Load-City tries them in turn.
    $srcLen = (Get-Item -LiteralPath $source).Length
    $srcHash = $null
    $twins = @()
    foreach ($c in Get-ChildItem $savesDir.FullName -Filter *.cok -File | Where-Object { $_.Length -eq $srcLen } | Sort-Object { $_.Name -like 'loadcheck_*' }, Name) {
        if (-not $srcHash) { $srcHash = (Get-FileHash -LiteralPath $source).Hash }
        if ((Get-FileHash -LiteralPath $c.FullName).Hash -eq $srcHash) { $twins += $c }
    }
    if ($twins.Count -gt 0) {
        # the game escapes "." as %2E in file names; the list shows the plain name
        $script:StagedNames = @($twins | ForEach-Object { [Uri]::UnescapeDataString([IO.Path]::GetFileNameWithoutExtension($_.Name)) })
        $script:StagedName = $script:StagedNames[0]
        Write-Output "  save copy: not needed, the game already lists a byte-identical save: $((($script:StagedNames | ForEach-Object { "'$_'" }) -join ', ')) (loaded read-only by name, never saved over; the first name the list shows is used)"
        return $twins[0].FullName
    }
    $script:StagedName = $gameName
    $script:StagedNames = @($gameName)
    $target = Join-Path $savesDir.FullName "$gameName.cok"
    $cid = "$target.cid"
    $copied = $false
    if (-not (Test-Path $target) -or (Get-FileHash $target).Hash -ne (Get-FileHash $source).Hash) {
        Copy-Item $source $target -Force
        (Get-Item $target).IsReadOnly = $false
        $copied = $true
    }
    # Observed 2026-11-16 (snow report section 7): a staged 1.0.0 copy stayed unlisted while its .cid, kept from an earlier staging (10:50), matched the
    # hash; the 1.0.1 copy, whose .cid was rewritten, listed. The rule is not in the decompile, so staging with the game closed now ALWAYS writes a
    # fresh .cid (new id, new time) and touches the .cok. With the game running a kept .cid stays (the running game already indexed it).
    $newCid = $false
    $gameClosed = -not (Get-Process Cities2 -ErrorAction SilentlyContinue)
    if (-not (Test-Path $cid) -or $gameClosed) {
        if (Test-Path $cid) { (Get-Item $cid).IsReadOnly = $false }
        [IO.File]::WriteAllText($cid, [guid]::NewGuid().ToString('N'), (New-Object Text.UTF8Encoding $false))
        if ($gameClosed) { (Get-Item $target).LastWriteTime = Get-Date }
        $newCid = $true
    }
    Write-Output "  save copy: $target$(if ($copied) { ' (new)' } else { ' (unchanged)' }), .cid $(if ($newCid) { 'written (fresh id)' } else { 'kept' })"
    return $target
}

function Wait-Ready([datetime]$since) {
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $WaitReady -Since $since.ToString('yyyy-MM-ddTHH:mm:ss.fff') -TimeoutSec 900
    $code = $LASTEXITCODE
    $out | ForEach-Object { Write-Output "  $_" }
    if ($code -ne 0) { Stop-Check 3 "the game did not report READY after the load (wait-ready exit $code)" }
}

function Wait-Report([int]$timeoutSec) {
    $t0 = Get-Date
    $lastPrint = [datetime]::MinValue
    while ($true) {
        Start-Sleep -Seconds 5
        $s = Invoke-Bridge @{ op = 'loadcheck'; action = 'status' }
        if ($s.state -eq 'done' -or $s.state -eq 'failed') { return $s }
        if (((Get-Date) - $lastPrint).TotalSeconds -ge 30) {
            $lastPrint = Get-Date
            $extra = if ($s.phase -eq 'month') { " date $($s.date), $($s.monthFrames) frames, turned $($s.monthTurned), $($s.elapsedS) s" } else { '' }
            Write-Host ("  {0:HH:mm:ss} phase {1}{2}" -f (Get-Date), $s.phase, $extra)
        }
        if (((Get-Date) - $t0).TotalSeconds -gt $timeoutSec) { Stop-Check 3 "no report after $timeoutSec s (phase $($s.phase))" }
    }
}

# A report that is already running (a manual `begin` + `report` before this script, or a second call racing the first) makes the bridge answer
# "a report is running" and refuse. Wait for that run instead of breaking off: its result is the one this call wanted.
function Start-Report($request) {
    try { Invoke-Bridge $request | Out-Null }
    catch {
        if ("$($_.Exception.Message)" -notmatch 'a report is running') { throw }
        Write-Output '  report: one is already running on the bridge, waiting for it instead of starting another'
    }
}

# $names: one or more game names of the same save (twins: byte-identical files, only one of them is listed). Tries each until the list shows one.
function Load-City([string[]]$names, [string]$label) {
    $t = Get-Date
    Invoke-Bridge @{ op = 'loadcheck'; action = 'begin'; save = $label } | Out-Null
    Show-Game
    $r = $null
    foreach ($name in $names) {
        $r = Invoke-Page "__dload($(ConvertTo-Json $name))" 180
        Write-Output "  __dload '$name': $r"
        if ("$r" -match '^loading ') { $script:StagedName = $name; break }
        if ("$r" -notmatch 'no save ') { break }   # anything but "the list does not show this name": no point in trying the twin
        if ($names.Count -gt 1) { Write-Output "  '$name' is not listed, trying the next twin" }
    }
    if ("$r" -notmatch '^loading ') {
        Stop-Check 2 "the load did not start ($r). 'no save <name>' means the game does not list the copy: stage it with the game closed (loadcheck.ps1 -StageOnly), then start the game (T205)."
    }
    # (The page helper __dload calls loadcheck/loadMark just before the load dialog click: the debug port does not answer while the load runs.)
    Wait-Ready $t
    Invoke-Node @('--inject', $Helpers, '--force') | Out-Null
    $pop = Invoke-Page '__dclearPopups()' 120
    if ("$pop" -notmatch ' 0$') { Write-Output "  $pop (each one paused the simulation)" }
    # A game load builds a new world: its autosave system starts ON again, so the off switch from before the load is gone.
    Invoke-Bridge @{ op = 'autosave'; on = $false } | Out-Null
}

# Flattens the countable parts of a census (no motion) into "section.key" = number.
function Get-Counts($census) {
    $counts = [ordered]@{}
    foreach ($section in 'persisted', 'summary', 'buildings', 'netEdges', 'runs', 'objects') {
        $obj = $census.$section
        if ($null -eq $obj) { continue }
        foreach ($p in $obj.PSObject.Properties) { $counts["$section.$($p.Name)"] = $p.Value }
    }
    return $counts
}

function Compare-Counts($expected, $actual) {
    $diffs = @()
    $keys = @($expected.Keys) + @($actual.Keys) | Sort-Object -Unique
    foreach ($k in $keys) {
        # A persisted type the baseline does not list is new since that release (SaveLayouts appends rows, e.g. SkiTrackPack in
        # 1.0.1): an old save has none of it, and the new build adds it. Not a difference.
        if (-not $expected.Contains($k) -and $k -like 'persisted.*') { continue }
        $e = if ($expected.Contains($k)) { $expected[$k] } else { 0 }
        $a = if ($actual.Contains($k)) { $actual[$k] } else { 0 }
        if ("$e" -ne "$a") { $diffs += "$k expected $e, got $a" }
    }
    return , $diffs
}

function ConvertTo-OrderedCounts($obj) {
    $h = [ordered]@{}
    foreach ($p in $obj.PSObject.Properties) { $h[$p.Name] = $p.Value }
    return $h
}

# ---- preconditions ----------------------------------------------------------------------------------------------------
if ($StageOnly) {
    if (Get-Process Cities2 -ErrorAction SilentlyContinue) { Stop-Check 2 '-StageOnly runs with the game CLOSED: it indexes the Saves folder at its start (T205).' }
    if (-not $Save) {
        $Save = Get-ChildItem $SavesByRelease -Filter *.cok -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $Save -or -not (Test-Path $Save)) { Stop-Check 2 "no baseline save to stage (-Save <file> or $SavesByRelease)" }
    $null = Install-SaveCopy $Save ("loadcheck_" + [IO.Path]::GetFileNameWithoutExtension($Save))
    Stop-Check 0 'staged. Start the game (Player world), then run loadcheck.ps1 without -StageOnly.'
}
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { Stop-Check 2 'node is not on PATH' }
if (-not (Get-Process Cities2 -ErrorAction SilentlyContinue)) {
    Stop-Check 2 'Cities2 is not running. Start it in the Player world with the bridge, --developerMode --uiDeveloperMode (tools/world.ps1 -World Player -Build dev -Cok; release/saves-by-release/README.md).'
}

# World gate (DECISIONS 664): the check is evidence only in a clean PLAYER world.
$worldScript = Join-Path $PSScriptRoot 'world.ps1'
$world = $null
try { $world = (& powershell -NoProfile -ExecutionPolicy Bypass -File $worldScript -World Status -Json) -join '' | ConvertFrom-Json } catch { }
if ($world) {
    Write-Output "  world: $($world.line)"
    if ($world.world -ne 'PLAYER' -or -not $world.playsetOk) {
        if (-not $AnyWorld) { Stop-Check 2 "not a clean Player world ($($world.line)). Switch with tools\world.ps1 -World Player (and pick the playset it names), or pass -AnyWorld for a run that is no release evidence." }
        Write-Output '  -AnyWorld: the world gate is OFF for this run.'
    }
} else {
    if (-not $AnyWorld) { Stop-Check 2 'could not read tools\world.ps1 -World Status -Json; fix that or pass -AnyWorld.' }
    Write-Output '  -AnyWorld: world unknown, the world gate is OFF for this run.'
}
$worldGate = [bool]($world -and $world.world -eq 'PLAYER' -and $world.playsetOk)

if (-not $Save -and -not $NoLoad) {
    $Save = Get-ChildItem $SavesByRelease -Filter *.cok -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $Save) { Stop-Check 2 "no baseline save in $SavesByRelease (README there: how to make one)" }
}
$Stem = if ($Save) { [IO.Path]::GetFileNameWithoutExtension($Save) } else { 'loaded-city' }
if ($Save -and -not (Test-Path $Save)) { Stop-Check 2 "save not found: $Save" }
$BaselinePath = if ($Save) { Join-Path (Split-Path -Parent $Save) "$Stem.census.json" } else { $null }
if (-not $OutDir) { $OutDir = Join-Path $Repo ("docs\research\field\{0:yyyy-MM-dd}\loadcheck" -f $Started) }
New-Item -ItemType Directory -Force $OutDir | Out-Null

Write-Output "loadcheck: $Stem ($(if ($NoLoad) { 'the loaded city' } else { $Save }))"
& powershell -NoProfile -ExecutionPolicy Bypass -File $WaitReady -TimeoutSec 600 | ForEach-Object { Write-Output "  $_" }
if ($LASTEXITCODE -ne 0) { Stop-Check 2 "the bridge is not READY (wait-ready exit $LASTEXITCODE)" }

$status = Invoke-Bridge @{ op = 'status' }
Write-Output "  bridge $($status.bridgeBuild), mode $($status.gameMode)"
if (-not $NoLoad -and $status.gameMode -ne 'MainMenu' -and -not $DiscardCity) {
    Stop-Check 2 "a city is open (mode $($status.gameMode)); the load would discard it. Return to the main menu or pass -DiscardCity."
}
try { Invoke-Bridge @{ op = 'loadcheck'; action = 'status' } | Out-Null }
catch { Stop-Check 2 "the deployed bridge has no loadcheck op: deploy tools/testbridge with the game closed ($($_.Exception.Message))" }
Invoke-Node @('--inject', $Helpers, '--force') | Out-Null

$autosave = (Invoke-Bridge @{ op = 'autosave' }).enabled
Invoke-Bridge @{ op = 'autosave'; on = $false } | Out-Null
$exitCode = 0
$result = [ordered]@{
    tool = 'tools/loadcheck.ps1'
    save = $Stem
    started = $Started.ToString('o')
    options = [ordered]@{ noLoad = [bool]$NoLoad; noMonth = [bool]$NoMonth; resave = [bool]$Resave; monthTimeoutMinutes = $MonthTimeoutMinutes }
}
try {
    # ---- load --------------------------------------------------------------------------------------------------------
    if (-not $NoLoad) {
        $null = Install-SaveCopy $Save "loadcheck_$Stem"
        $gameNames = $script:StagedNames
        Load-City $gameNames $Stem
        $result.gameSave = $script:StagedName   # the name the list showed
    }

    # ---- report (a, b, c) ----------------------------------------------------------------------------------------------
    Write-Output "  report: logs, census$(if (-not $NoMonth) { ', one month at speed 8' })"
    Start-Report @{ op = 'loadcheck'; action = 'report'; month = (-not $NoMonth); speed = 8; monthTimeoutMinutes = $MonthTimeoutMinutes }
    $done = Wait-Report (60 * ($MonthTimeoutMinutes + 10))
    if ($done.state -eq 'failed') { $result.report = $done.report; throw "report failed: $($done.error)" }
    $report = $done.report
    $result.report = $report
    $verdict = [ordered]@{}

    # (a)
    $a = $report.parts.a
    $aPass = [bool]$a.pass
    $aDetail = "$($a.gatedHits) gated log hit(s), $($report.logs.gameErrorsOurs) game error(s) naming SkiResortMod"
    # Gate 2 (T228) and the duplicate classification, from the bridge's exact counts (logs.prefabIds). An older deployed bridge has no such
    # field: then the capped hit list is read instead (a lower bound, said so).
    $ids = $report.logs.prefabIds
    $idSource = 'bridge counts'
    if ($null -eq $ids) {
        $idSource = 'hit list (the deployed bridge predates logs.prefabIds: a lower bound)'
        $packRe = 'pdx_mods[/\\]167215_\d+[/\\]ModularSkiResort\.cok'
        $hl = @($report.logs.hits)
        $ids = [pscustomobject]@{
            unknownSki = @($hl | Where-Object { $_.pattern -like 'Unknown prefab ID*' -and $_.text -match '\bSki' }).Count
            unknownSkiPreLoad = @($hl | Where-Object { $_.pattern -like 'Unknown prefab ID*pre-load*' -and $_.text -match '\bSki' }).Count
            dupSkiPack = @($hl | Where-Object { $_.pattern -like 'Duplicate prefab ID*' -and $_.text -match '\bSki' -and $_.text -match $packRe }).Count
            dupSkiForeign = @($hl | Where-Object { $_.pattern -like 'Duplicate prefab ID*' -and $_.text -match '\bSki' -and $_.text -notmatch $packRe }).Count
            dupOther = @($hl | Where-Object { $_.pattern -like 'Duplicate prefab ID*' -and $_.text -notmatch '\bSki' }).Count
        }
    }
    $aInfo = "info: $($ids.dupSkiPack) Duplicate prefab ID line(s) are the pack's own copy and $($ids.dupOther) name other prefabs (harmless), counted from the $idSource"
    # Only lines AFTER the load click count: lines between `begin` and the click come from the Load Game list reading the other saves
    # (commit 1425f48f), not from this city. The ids themselves are the city's: a pack-built save resolves all of them through the pack.
    $unknownAfterLoad = [int]$ids.unknownSki - [int]$ids.unknownSkiPreLoad
    if ($worldGate) {
        if ($unknownAfterLoad -gt 0) {
            $aPass = $false
            $aDetail += "; WORLD GATE: $unknownAfterLoad Unknown prefab ID line(s) name a Ski prefab. The save holds ids the pack does not resolve (built with ImportedData present?): placeholder boxes in game (T228)"
        }
        if ([int]$ids.dupSkiForeign -gt 0) {
            $aPass = $false
            $aDetail += "; WORLD GATE: $($ids.dupSkiForeign) Duplicate prefab ID line(s) name a Ski prefab from an asset other than the pack's .cok: a second copy of our prefabs beside the pack (ImportedData or a local package loaded)"
        }
    } else {
        $aDetail += "; world gate OFF (-AnyWorld): $unknownAfterLoad Ski Unknown id(s) after the load, $($ids.dupSkiForeign) foreign Ski duplicate(s) not judged"
    }
    $aDetail += " | $aInfo"
    $verdict.a = [ordered]@{ pass = $aPass; detail = $aDetail }
    $result.world = if ($world) { $world.line } else { 'unknown' }
    $result.worldGate = $worldGate

    # (b): baseline diff
    $counts = Get-Counts $report.census
    $bDetail = @()
    $bPass = $report.census.zeroTypes.Count -eq 0 -and $report.census.unresolvedTypes.Count -eq 0
    if ($report.census.zeroTypes.Count) { $bDetail += "persisted types with 0 entities: $($report.census.zeroTypes -join ', ')" }
    if ($report.census.unresolvedTypes.Count) { $bDetail += "persisted types not found: $($report.census.unresolvedTypes -join ', ')" }
    if ($BaselinePath -and (Test-Path $BaselinePath) -and -not $WriteBaseline) {
        $base = Get-Content $BaselinePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $diffs = Compare-Counts (ConvertTo-OrderedCounts $base.counts) $counts
        if ($diffs.Count) { $bPass = $false; $bDetail += "differs from the baseline of $($base.created) ($($base.modBuild)): $($diffs -join '; ')" }
        else { $bDetail += "matches the baseline of $($base.created) ($($base.modBuild)), $($counts.Count) counts" }
        $result.baselineDiff = $diffs
    } elseif ($BaselinePath) {
        $baseline = [ordered]@{
            save = $Stem; created = (Get-Date).ToString('o'); modBuild = $report.modBuild; gameVersion = $report.gameVersion
            note = 'Census right after loading this save (tools/loadcheck.ps1). Later runs diff against counts.'; counts = $counts
        }
        [IO.File]::WriteAllText($BaselinePath, ($baseline | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
        $bDetail += "baseline written: $BaselinePath ($($counts.Count) counts, build $($report.modBuild))"
    } else {
        $bDetail += 'no baseline (no -Save with -NoLoad)'
    }
    $motion = $report.census.motion
    if ($motion -and $report.census.summary.chairsAndCabins -gt 0 -and $motion.chairsMoving -eq 0) {
        $bDetail += "info: no chair moved over $($motion.frames) frames at $($motion.time) (night, closed hours or no power?)"
    }
    $verdict.b = [ordered]@{ pass = $bPass; detail = ($bDetail -join ' | ') }

    # (c)
    $c = $report.parts.c
    if ($c.skipped) { $verdict.c = [ordered]@{ pass = $null; detail = "skipped ($($c.skipped))" } }
    else {
        $m = $report.month
        $cDetail = "money $($report.billingBefore.money) -> $($report.billingAfter.money) ($($c.moneyDelta)); bind charges $($c.bindChargeLines), restores $($c.restoreLines), paid rose $($c.paidRose.Count), fresh paid $($c.freshPaidBindings.Count); month $($m.startDate) -> $($m.endDate) in $($m.seconds) s"
        $cPass = [bool]$c.pass
        if (-not $m.turned) { $cPass = $false; $cDetail += "; the month did not turn ($($m.incomplete))" }
        $verdict.c = [ordered]@{ pass = $cPass; detail = $cDetail }
    }

    # (d)
    if ($Resave) {
        $before = (Invoke-Bridge @{ op = 'loadcheck'; action = 'census' }).census
        $saveName = if ($ResaveName) { $ResaveName } else { 'bridgelc' + (Get-Date).ToString('MMddHHmmss') }
        Show-Game
        $s = Invoke-Page "__dsave($(ConvertTo-Json $saveName))" 60
        Write-Output "  __dsave: $s"
        if ("$s" -notmatch '^saved ') { throw "the save failed: $s" }
        Load-City $saveName "$Stem resaved as $saveName"
        Start-Report @{ op = 'loadcheck'; action = 'report'; month = $false }
        $done2 = Wait-Report 600
        if ($done2.state -eq 'failed') { throw "resave report failed: $($done2.error)" }
        $result.resave = $done2.report
        $diffs = Compare-Counts (Get-Counts $before) (Get-Counts $done2.report.census)
        $dPass = [bool]$done2.report.parts.a.pass -and $diffs.Count -eq 0
        $verdict.d = [ordered]@{
            pass = $dPass
            detail = "saved as $saveName; log hits $($done2.report.parts.a.gatedHits); census $(if ($diffs.Count) { 'differs: ' + ($diffs -join '; ') } else { 'unchanged' })"
        }
    } else {
        $verdict.d = [ordered]@{ pass = $null; detail = 'skipped (no -Resave)' }
    }
    $result.verdict = $verdict
} catch {
    $result.error = $_.Exception.Message
    $exitCode = 3
} finally {
    # -NoLoad: the city that was open keeps the state it was found in. After a load, the found state belonged to the world before it (the main
    # menu's), so the loaded city stays OFF: forcing it on wrote a stray autosave (2026-11-14, twice).
    try {
        $restore = if ($NoLoad) { $autosave } else { $false }
        Invoke-Bridge @{ op = 'autosave'; on = $restore } | Out-Null
        Write-Output "  autosave: found $autosave, left $restore"
    } catch { Write-Output "  could not set autosave: $($_.Exception.Message)" }
}

$result.finished = (Get-Date).ToString('o')
$file = Join-Path $OutDir ("{0}-{1:HHmmss}.json" -f $Stem, $Started)
[IO.File]::WriteAllText($file, ($result | ConvertTo-Json -Depth 60), (New-Object Text.UTF8Encoding $false))

if ($exitCode -eq 3) { Write-Output "loadcheck: BROKE OFF: $($result.error)"; Write-Output "report: $file"; exit 3 }
foreach ($part in 'a', 'b', 'c', 'd') {
    $v = $result.verdict[$part]
    $word = if ($null -eq $v.pass) { 'SKIP' } elseif ($v.pass) { 'PASS' } else { 'FAIL'; $exitCode = 1 }
    Write-Output ("  ({0}) {1}  {2}" -f $part, $word, $v.detail)
}
Write-Output "report: $file"
Write-Output "Manual steps left: old-save-checklist.md (b) look, (c) to (g)."
exit $exitCode
