# t269-leg.ps1 - one leg of the T269 gun-seed reading on runs (bridge only: DOM + ops, no OS input). Started in r5 (open rule), moved
# here with the 1.0.4 calibration. Per air temperature: turn the chosen runs into snowmaking runs (the hub's gun slider trigger), bare
# the runs' boxes (meltprobe write 0), force the base air so the first run's mid elevation sits at T, no precipitation, run Hours game
# hours at speed 8, and sample runTexels (the full texels' mean depth, quantiles and open share) about every 0.1 game hour.
# Needs: the game running, the bridge READY at the main menu (or the save already loaded). Writes <OutDir>\t269-leg<Leg>-*.json + .log.
#
# The gun trigger takes the run's full entity (index AND version). runTexels gives the index only, so the script reads the live version
# with the bridge's dump op (every area entity with an area node buffer) and refuses to go on when the trigger is refused. A guessed
# version (the r5 copy tried 1..4) silently left the guns off on a run whose version was 35.
param(
    [Parameter(Mandatory)][string]$Leg,
    [Parameter(Mandatory)][string]$OutDir,
    [string]$Runs = '',              # comma-separated run names; empty = choose (groomed, most texels among the flattest)
    [string]$Temps = '1,2,2.9',
    [double]$Hours = 2,
    [int]$Spacing = 30,
    [string]$SaveName = 'SRM Baseline 1.0.3 ashflat'
)
$ErrorActionPreference = 'Stop'
$Bridge = $PSScriptRoot
$CallJs = Join-Path $Bridge 'bridge-call.mjs'
$Helpers = Join-Path $Bridge 'helpers.js'
$WaitReady = Join-Path $Bridge 'wait-ready.ps1'
$ModLog = Join-Path $env:CSII_USERDATAPATH 'Logs\SkiResortMod.Mod.log'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$Log = Join-Path $OutDir "t269-leg$Leg.log"
$HourFrames = 10923
function Say([string]$m) { $l = ('{0:HH:mm:ss} {1}' -f (Get-Date), $m); Write-Output $l; Add-Content -LiteralPath $Log -Value $l }
function Invoke-Node([string[]]$a) {
    $raw = & node $CallJs @a
    $text = ($raw | ForEach-Object { "$_" }) -join "`n"
    if ($LASTEXITCODE -eq 2) { throw "bridge unreachable: $text" }
    try { return $text | ConvertFrom-Json } catch { throw "not JSON: $text" }
}
function Op($req, [int]$t = 60) {
    $json = $req | ConvertTo-Json -Compress -Depth 10
    $r = Invoke-Node @('--op', [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)), '--timeout', "$t")
    if (-not $r.ok) { throw "op $($req.op) $($req.action): $($r.error)" }
    return $r
}
function Page([string]$e, [int]$t = 60) {
    $r = Invoke-Node @('--js', [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($e)), '--timeout', "$t")
    if (-not $r.ok) { throw "page: $($r.error)" }
    return $r.value
}
function Save-Json($obj, [string]$name) { $obj | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $OutDir $name) -Encoding UTF8 }
function Run-Frames([long]$frames, [scriptblock]$every, [long]$everyFrames) {
    $s0 = [long](Op @{ op = 'sim' }).frameIndex
    $target = $s0 + $frames
    $next = $s0 + $everyFrames
    Op @{ op = 'sim'; speed = 8 } | Out-Null
    $t0 = Get-Date
    while ($true) {
        Start-Sleep -Milliseconds 700
        $f = [long](Op @{ op = 'sim' }).frameIndex
        if ($every -and $f -ge $next) { & $every $f; $next += $everyFrames }
        if ($f -ge $target) { break }
        if (((Get-Date) - $t0).TotalMinutes -gt 12) { Say "frame cap at $f"; break }
    }
    Op @{ op = 'sim'; speed = 0 } | Out-Null
}
# The live version of an entity index: the dump op lists "index:version" ids. Runs are areas, so every one carries an area node buffer.
function Get-EntityVersion([int]$index) {
    $d = Op @{ op = 'dump'; all = @('Game.Areas.Node'); limit = 200000 } 120
    $hit = @($d.rows | Where-Object { "$($_.entity)" -like "${index}:*" })
    if ($hit.Count -ne 1) { throw "entity index $index found $($hit.Count) time(s) among $($d.count) area entities" }
    return [int]("$($hit[0].entity)".Split(':')[1])
}
function Get-LogLength { if (Test-Path -LiteralPath $ModLog) { return (Get-Item -LiteralPath $ModLog).Length } return 0 }
function Get-LogTail([long]$from) {
    if (-not (Test-Path -LiteralPath $ModLog)) { return '' }
    $fs = [IO.File]::Open($ModLog, 'Open', 'Read', 'ReadWrite')
    try { [void]$fs.Seek($from, 'Begin'); return (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

Say "T269 leg $Leg start"
$st = Op @{ op = 'status' }
Say "bridge $($st.bridgeBuild), mode $($st.gameMode)"
Invoke-Node @('--inject', $Helpers, '--force') | Out-Null
if ($st.gameMode -eq 'MainMenu') {
    Op @{ op = 'autosave'; on = $false } | Out-Null
    $t = Get-Date
    $r = Page "__dload($(ConvertTo-Json $SaveName))" 180
    Say "__dload: $r"
    if ("$r" -notmatch '^loading ') { throw "load did not start: $r" }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $WaitReady -Since $t.ToString('yyyy-MM-ddTHH:mm:ss.fff') -TimeoutSec 900 | ForEach-Object { Say "  $_" }
    if ($LASTEXITCODE -ne 0) { throw "not READY after load" }
    Invoke-Node @('--inject', $Helpers, '--force') | Out-Null
    Say ("popups: " + (Page '__dclearPopups()' 120))
    Op @{ op = 'autosave'; on = $false } | Out-Null
}
Op @{ op = 'sim'; speed = 0 } | Out-Null
Page 'engine.trigger("skiResort.setHubOpen", true), "hub"' | Out-Null

# let the injection evaluate a few passes (runTexels needs the planes)
Run-Frames 640 $null 0
# choose the runs
$all = Op @{ op = 'runTexels'; kinds = @(0, 1, 2) } 120
Save-Json $all "t269-leg$Leg-runTexels-t0.json"
if ($Runs -eq '') {
    $cand = @($all.runs | Where-Object { $_.kind -eq 0 -and $_.texels -ge 300 } | Sort-Object { $_.yMax - $_.yMin } | Select-Object -First 6 |
        Sort-Object texels -Descending | Select-Object -First 2)
    $names = @($cand | ForEach-Object { $_.name })
} else {
    $names = @($Runs.Split(',') | ForEach-Object { $_.Trim() })
}
$chosen = @($all.runs | Where-Object { $names -contains $_.name })
if ($chosen.Count -eq 0) { throw "no run chosen" }
foreach ($c in $chosen) {
    $c | Add-Member -NotePropertyName version -NotePropertyValue (Get-EntityVersion ([int]$c.entity)) -Force
    Say ("run '{0}' entity {1}:{2} kind {3} y {4}..{5} texels {6} bounds {7}" -f $c.name, $c.entity, $c.version, $c.kind, $c.yMin, $c.yMax, $c.texels, ($c.bounds -join ','))
}

# guns on (the hub's slider trigger with the live entity); a refused trigger stops the leg
foreach ($c in $chosen) {
    $mark = Get-LogLength
    Page ("engine.trigger('skiResort.setRunGunSpacing', {index: $($c.entity), version: $($c.version)}, $Spacing), 'fired'") | Out-Null
    Run-Frames 64 $null 0
    $tail = Get-LogTail $mark
    $said = @($tail -split "`n" | Where-Object { $_ -match 'setRunGunSpacing' })
    foreach ($l in $said) { Say ("  mod log: " + $l.Trim()) }
    if (-not ($said | Where-Object { $_ -match 'stored' })) { throw "setRunGunSpacing not stored for '$($c.name)' $($c.entity):$($c.version) (refused or no log line)" }
}
Run-Frames 1200 $null 0
$k = Op @{ op = 'runTexels'; kinds = @(0, 1, 2); names = $names } 120
foreach ($r in $k.runs) { Say ("after guns: '{0}' kind {1} full n {2} meanM {3}" -f $r.name, $r.kind, $r.classes.full.n, $r.classes.full.meanM) }

# the lapse and base height
$rects = @($chosen | ForEach-Object { @{ name = $_.name; x0 = $_.bounds[0] - 8; z0 = $_.bounds[1] - 8; x1 = $_.bounds[2] + 8; z1 = $_.bounds[3] + 8 } })
$info = Op @{ op = 'meltprobe'; action = 'start'; rects = $rects; every = 4096; max = 1 }
Op @{ op = 'meltprobe'; action = 'stop' } | Out-Null
$lapse = [double]$info.lapse; $base = [double]$info.baseHeight
$mid = (([double]$chosen[0].yMin + [double]$chosen[0].yMax) / 2)
Say "lapse $lapse C/m, base height $base m, run 0 mid elevation $mid m"

foreach ($tStr in $Temps.Split(',')) {
    $T = [double]::Parse($tStr.Trim(), [Globalization.CultureInfo]::InvariantCulture)
    $global = [Math]::Round($T + $lapse * ($mid - $base), 2)
    Op @{ op = 'sim'; speed = 0 } | Out-Null
    Op @{ op = 'climate'; temperature = $global; precipitation = 0 } | Out-Null
    $w = Op @{ op = 'meltprobe'; action = 'write'; rects = @($rects | ForEach-Object { $_ + @{ depthM = 0.0 } }) }
    $st = Op @{ op = 'meltprobe'; action = 'start'; rects = $rects; every = 64; max = 4000 }
    Say ("T = +$T C at run 0 mid: base air forced $global C; bared " + (($w | ConvertTo-Json -Compress -Depth 5)) + "; rect air " + (($st.rects | ForEach-Object { "$($_.name) $($_.airC) C" }) -join ', '))
    $series = New-Object System.Collections.ArrayList
    $f0 = [long](Op @{ op = 'sim' }).frameIndex
    $sample = {
        param($f)
        $rt = Op @{ op = 'runTexels'; kinds = @(0, 1, 2); names = $names } 120
        foreach ($r in $rt.runs) {
            [void]$series.Add([pscustomobject]@{ frame = $rt.frame; h = [Math]::Round(($rt.frame - $f0) / $HourFrames, 3); name = $r.name; kind = $r.kind
                fullN = $r.classes.full.n; fullMeanM = $r.classes.full.meanM; fullOpen = $r.classes.full.openShare; chill = $r.classes.full.meanChill
                warmN = $r.classes.warm.n; fringeN = $r.classes.fringe.n; fringeMeanM = $r.classes.fringe.meanM; stoppedN = $r.classes.stopped.n; q = $r.fullQuantilesM; counted = $r.countedShare })
        }
    }
    & $sample $f0
    Run-Frames ([long]($Hours * $HourFrames)) $sample ([long](0.1 * $HourFrames))
    & $sample 0
    $mp = Op @{ op = 'meltprobe'; action = 'read' } 120
    Op @{ op = 'meltprobe'; action = 'stop' } | Out-Null
    $tag = ('{0:0.0}' -f $T).Replace('.', 'p')
    Save-Json $series "t269-leg$Leg-T$tag-series.json"
    Save-Json $mp "t269-leg$Leg-T$tag-meltprobe.json"
    Save-Json (Op @{ op = 'runSnow'; limit = 200 } 120) "t269-leg$Leg-T$tag-runSnow.json"
    foreach ($n in $names) {
        $rows = @($series | Where-Object { $_.name -eq $n })
        $line = ($(for ($i = 0; $i -lt $rows.Count; $i++) { if ($i % 4 -eq 0 -or $i -eq $rows.Count - 1) { $rows[$i] } }) | ForEach-Object { "$($_.h)h full $($_.fullN)x$($_.fullMeanM) p50 $($_.q.p50) p90 $($_.q.p90) fringe $($_.fringeN)x$($_.fringeMeanM)" }) -join ' | '
        Say "  +$T C '$n': $line"
    }
}
Op @{ op = 'climate'; temperature = $null; precipitation = $null } | Out-Null
Say "T269 leg $Leg done"
