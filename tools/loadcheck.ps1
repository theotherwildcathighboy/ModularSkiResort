<#
.SYNOPSIS
  Old-save load check: loads a release's baseline save through the test bridge and checks it as far as a machine can
  (docs/research/field/old-save-checklist.md; the bridge op `loadcheck`, tools/testbridge/TestBridgeSystem.LoadCheck.cs).

.DESCRIPTION
  Needs a running game in the DEV setup (tools/dev-mode.ps1 -Mode Dev, a playset WITHOUT the Paradox Mods subscription,
  --developerMode --uiDeveloperMode, live reload off) with the bridge READY. It never builds, deploys or starts the game.

  Steps:
    1. Copies the baseline save (read-only, in release/saves-by-release/) into the game's Saves folder as loadcheck_<stem>.cok,
       so the original never loads and never gets saved over. The .cid is not copied (the game's own copy of that city keeps its id).
    2. Op loadcheck begin (log offsets), then the page helper __load (the pause menu's real clicks: the game window must be free,
       the script brings it to the front), then tools/testbridge/wait-ready.ps1.
    3. Op loadcheck report: (a) log scan since the load, (b) entity census, (c) billing before and after one in-game month at
       sim speed 8. Polled until done.
    4. With -Resave, part (d): census, save under a new bridge* name, load it again, census again, compare.
    5. Writes the JSON report to docs/research/field/<date>/loadcheck/ and prints PASS / FAIL per part.
       The census baseline lives beside the save: release/saves-by-release/<stem>.census.json. The first run writes it;
       later runs diff their census against it (part b). -WriteBaseline replaces it.

  Autosave is switched off for the run and switched back afterward. The sim speed returns to what it was.

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
    # README "Starting the game without the launcher": input steps fail unless the game window has focus.
    if (-not ('LoadCheckWin' -as [type])) {
        Add-Type @"
using System;using System.Runtime.InteropServices;
public class LoadCheckWin{[DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);[DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int c);[DllImport("user32.dll")]public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);}
"@
    }
    $h = (Get-Process Cities2).MainWindowHandle
    [LoadCheckWin]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [LoadCheckWin]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    [LoadCheckWin]::ShowWindow($h, 9) | Out-Null
    [LoadCheckWin]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 500
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
            Write-Output ("  {0:HH:mm:ss} phase {1}{2}" -f (Get-Date), $s.phase, $extra)
        }
        if (((Get-Date) - $t0).TotalSeconds -gt $timeoutSec) { Stop-Check 3 "no report after $timeoutSec s (phase $($s.phase))" }
    }
}

function Load-City([string]$name, [string]$label) {
    $t = Get-Date
    Invoke-Bridge @{ op = 'loadcheck'; action = 'begin'; save = $label } | Out-Null
    Show-Game
    $r = Invoke-Page "__load($(ConvertTo-Json $name))" 60
    Write-Output "  __load: $r"
    if ("$r" -notmatch '^loading ') {
        Stop-Check 2 "the load did not start ($r). A save copied while the game ran may need a game restart to be listed (unverified)."
    }
    Wait-Ready $t
    Invoke-Node @('--inject', $Helpers) | Out-Null
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
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { Stop-Check 2 'node is not on PATH' }
if (-not (Get-Process Cities2 -ErrorAction SilentlyContinue)) {
    Stop-Check 2 'Cities2 is not running. Start it in the dev setup (tools\dev-mode.ps1 -Mode Dev, a playset without the subscription, --developerMode --uiDeveloperMode).'
}

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
Invoke-Node @('--inject', $Helpers) | Out-Null

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
        $savesRoot = Join-Path $UserData 'Saves'
        $savesDir = Get-ChildItem $savesRoot -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $savesDir) { Stop-Check 2 "no Saves folder under $savesRoot" }
        $gameName = "loadcheck_$Stem"
        $target = Join-Path $savesDir.FullName "$gameName.cok"
        $copied = $false
        if (-not (Test-Path $target) -or (Get-FileHash $target).Hash -ne (Get-FileHash $Save).Hash) {
            Copy-Item $Save $target -Force
            (Get-Item $target).IsReadOnly = $false
            $copied = $true
        }
        Write-Output "  save copy: $target$(if ($copied) { ' (new)' } else { ' (unchanged)' })"
        $result.gameSave = $gameName
        Load-City $gameName $Stem
    }

    # ---- report (a, b, c) ----------------------------------------------------------------------------------------------
    Write-Output "  report: logs, census$(if (-not $NoMonth) { ', one month at speed 8' })"
    Invoke-Bridge @{ op = 'loadcheck'; action = 'report'; month = (-not $NoMonth); speed = 8; monthTimeoutMinutes = $MonthTimeoutMinutes } | Out-Null
    $done = Wait-Report (60 * ($MonthTimeoutMinutes + 10))
    if ($done.state -eq 'failed') { $result.report = $done.report; throw "report failed: $($done.error)" }
    $report = $done.report
    $result.report = $report
    $verdict = [ordered]@{}

    # (a)
    $a = $report.parts.a
    $verdict.a = [ordered]@{ pass = [bool]$a.pass; detail = "$($a.gatedHits) gated log hit(s), $($report.logs.gameErrorsOurs) game error(s) naming SkiResortMod" }

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
        $saveName = 'bridgelc' + (Get-Date).ToString('MMddHHmmss')
        Show-Game
        $s = Invoke-Page "__save($(ConvertTo-Json $saveName))" 60
        Write-Output "  __save: $s"
        if ("$s" -notmatch '^saved ') { throw "the save failed: $s" }
        Load-City $saveName "$Stem resaved as $saveName"
        Invoke-Bridge @{ op = 'loadcheck'; action = 'report'; month = $false } | Out-Null
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
    try { Invoke-Bridge @{ op = 'autosave'; on = $autosave } | Out-Null } catch { Write-Output "  could not restore autosave ($autosave): $($_.Exception.Message)" }
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
