<#
.SYNOPSIS
  Every local gate in parallel, one PASS / FAIL table, one exit code (DECISIONS 669). Replaces running the ui-gates list and the CI steps by hand.

.DESCRIPTION
  -Quick   the compile check (dotnet build -t:Compile, the only form: no flags that deploy), every node gate of ci.yml, the UI gates
           (typecheck, bundle contract, hub rules, history rules, gate fixtures, plain build + check-bundle, hub layout --quick).
  -Full    -Quick plus the test hosts (LiftBatch, ResortBatch, SlopeBatch, GladeBatch, GradeBatch, BuildingBatch, BindingBatch,
           AutoGenBatch when present, SaveBatch after the compile) and tools\preflight.ps1.
  -Only <names>  run just the named gates (comma separated; names are the first column of the table), plus what they depend on.
  -List    print the gate table (name, kind, depends on) and exit.

  Machine rule (CLAUDE.md, DECISIONS 398): no game running -> up to -Jobs (default 3) heavy jobs at once, none while free RAM is under 6 GB
  (it waits). A game running -> every HEAVY gate is REFUSED (the compile, builds, test hosts, layout test); the light node gates still run.
  -AllowGame lets ONE heavy job run at BelowNormal priority beside the game (never use it while the owner plays). The compile check never
  deploys: it is exactly `dotnet build -t:Compile`. Nothing here ever runs a deploying build.

  Each gate's output is kept in %TEMP%\srm-gates-<time>\<name>.log; the table shows the last line, and a FAILED gate's last 25 lines print
  under it. A gate that needs something absent (mod\SkiResortUI\node_modules in a worktree: link it with mklink /J or npm ci) is SKIP, not
  FAIL, and makes the exit code 3.

  Exit: 0 every gate PASS, 1 a gate FAILED, 3 no FAIL but a gate was SKIPped / REFUSED (the run is incomplete: do not call it green).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\gates.ps1 -Quick
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\gates.ps1 -Full -Jobs 3
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\gates.ps1 -Only compile,SaveBatch
#>
param(
    [switch]$Quick,
    [switch]$Full,
    [string[]]$Only,
    [int]$Jobs = 3,
    [switch]$AllowGame,
    [switch]$List,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'
$Repo = Split-Path -Parent $PSScriptRoot
$Ui = Join-Path $Repo 'mod\SkiResortUI'
$gameRuns = [bool](Get-Process -Name Cities2 -ErrorAction SilentlyContinue)

# name, kind (node = light, heavy = one of the limited jobs), cwd, command line (run by cmd /c), after = a gate that must PASS first,
# need = a path that must exist
$G = New-Object System.Collections.Generic.List[object]
function Add-Gate([string]$name, [string]$kind, [string]$cwd, [string]$cmd, [string]$after = '', [string]$need = '', [string]$set = 'quick') {
    $G.Add([pscustomobject]@{ name = $name; kind = $kind; cwd = $cwd; cmd = $cmd; after = $after; need = $need; set = $set })
}
$mod = Join-Path $Repo 'mod\SkiResortMod'
Add-Gate 'compile' 'heavy' $mod 'dotnet build -t:Compile'
# The node gates are DERIVED from the 'gates' job of .github/workflows/ci.yml, so this list cannot drift from CI (2026-11-16: three branches went red on
# check-run-props because a hand-kept list missed it). Every `node ...` command line of that job runs here, in its working-directory.
# Only gates CI does not run (tooltip-units, loadcheck-newer, ui-contract) are added by hand below.
$nm = Join-Path $Ui 'node_modules'
$ci = Join-Path $Repo '.github\workflows\ci.yml'
$ciGates = 0
if (Test-Path -LiteralPath $ci) {
    $inJob = $false; $wd = ''; $inRun = $false
    foreach ($line in Get-Content -LiteralPath $ci) {
        if ($line -match '^  (\w[\w-]*):\s*$') { $inJob = ($Matches[1] -eq 'gates'); $wd = ''; $inRun = $false; continue }
        if (-not $inJob) { continue }
        if ($line -match '^\s+- (name|uses):') { $wd = ''; $inRun = $false }
        if ($line -match '^\s+working-directory:\s*(\S+)') { $wd = $Matches[1]; continue }
        $cmds = @()
        if ($line -match '^\s+run:\s*\|\s*$') { $inRun = $true; continue }
        elseif ($line -match '^\s+run:\s*(.+)$') { $inRun = $false; $cmds = @($Matches[1].Trim()) }
        elseif ($inRun -and $line -match '^\s{10,}(\S.*)$') { $cmds = @($Matches[1].Trim()) }
        elseif ($inRun -and $line.Trim()) { $inRun = $false }
        foreach ($c in $cmds) {
            if ($c -notmatch '^node (\S+)\s*(.*)$') { continue }
            $script = $Matches[1] -replace '/', '\'
            $rest = $Matches[2].Trim()
            $isUi = ($wd -eq 'mod/SkiResortUI')
            $cwd = $Repo; $need = ''
            if ($isUi) { $cwd = $Ui; $need = $nm }
            $name = ([IO.Path]::GetFileNameWithoutExtension($script) -replace '^(check|test)-', '')
            if ($rest) { $name += '-' + ($rest -replace '[^\w]+', '') }
            if ($isUi) { $name = 'ui-' + $name }
            Add-Gate $name 'node' $cwd "node $script $rest" '' $need
            $ciGates++
        }
    }
}
if ($ciGates -lt 10) { Write-Warning "gates: only $ciGates node gates found in ci.yml: the parser is out of date with the workflow" }
Add-Gate 'tooltip-units' 'node' $Repo 'node tools\check-tooltip-units.js'   # local only, CI does not run it
Add-Gate 'loadcheck-newer' 'node' $Repo 'powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-loadcheck-newer.ps1'   # local only (r8-testpass B3)
Add-Gate 'ui-typecheck' 'heavy' $Ui 'npm run typecheck' '' $nm
Add-Gate 'ui-contract' 'node' $Ui 'node tools\check-bundle.js --contract-only' '' $nm   # local only
Add-Gate 'ui-build' 'heavy' $Ui 'npm run build' 'ui-typecheck' $nm
Add-Gate 'ui-hub-layout' 'heavy' $Ui 'node tools\test-hub-layout.js --quick' 'ui-build' $nm
foreach ($h in 'LiftBatch', 'ResortBatch', 'SlopeBatch', 'GladeBatch', 'GradeBatch', 'BuildingBatch', 'BindingBatch', 'AutoGenBatch') {
    $proj = "tests\$h\$h.csproj"
    if (Test-Path (Join-Path $Repo $proj)) { Add-Gate $h 'heavy' $Repo "dotnet run --project $proj -c Release" '' '' 'full' }
}
Add-Gate 'SaveBatch' 'heavy' $Repo 'dotnet run --project tests\SaveBatch\SaveBatch.csproj -c Release' 'compile' '' 'full'
Add-Gate 'preflight' 'heavy' $Repo 'powershell -NoProfile -ExecutionPolicy Bypass -File tools\preflight.ps1' '' '' 'full'

if ($List) { $G | Format-Table name, kind, set, after -AutoSize; exit 0 }
if (-not $Quick -and -not $Full -and -not $Only) { Write-Output 'gates: pass -Quick, -Full or -Only <names> (-List shows the gates)'; exit 2 }

# ---- which gates run -------------------------------------------------------------------------------------------------------
$run = New-Object System.Collections.Generic.List[object]
if ($Only) {
    $want = New-Object System.Collections.Generic.HashSet[string]
    $names = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
    foreach ($n in $names) {
        if (-not ($G | Where-Object name -eq $n)) { Write-Output "gates: no gate named '$n' (-List)"; exit 2 }
        $x = $n
        while ($x) { [void]$want.Add($x); $x = ($G | Where-Object name -eq $x).after }
    }
    foreach ($g in $G) { if ($want.Contains($g.name)) { $run.Add($g) } }
} else {
    foreach ($g in $G) { if ($g.set -eq 'quick' -or $Full) { $run.Add($g) } }
}

$logDir = Join-Path $env:TEMP ("srm-gates-{0:yyyyMMdd-HHmmss}" -f (Get-Date))
New-Item -ItemType Directory -Force $logDir | Out-Null
$state = @{}
foreach ($g in $run) { $state[$g.name] = [pscustomobject]@{ g = $g; status = 'WAIT'; proc = $null; t0 = $null; secs = 0; note = ''; log = (Join-Path $logDir "$($g.name).log") } }
if ($gameRuns -and -not $AllowGame) { Write-Output "gates: Cities2 is running: heavy gates are REFUSED (the machine rule); the node gates run. -AllowGame runs one heavy job at BelowNormal." }
$maxHeavy = if ($gameRuns) { 1 } else { [Math]::Max(1, $Jobs) }
$maxLight = 6
Write-Output "gates: $($run.Count) gate(s), up to $maxHeavy heavy at once, logs in $logDir"

function Get-FreeGb { try { [Math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 1) } catch { 99 } }

function Start-Gate($s) {
    $g = $s.g
    $env:MSBUILDDISABLENODEREUSE = '1'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
    $err = "$($s.log).err"
    $s.proc = Start-Process -FilePath 'cmd.exe' -ArgumentList "/c $($g.cmd)" -WorkingDirectory $g.cwd -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $s.log -RedirectStandardError $err
    $null = $s.proc.Handle   # keeps the exit code readable after exit (PowerShell 5.1)
    if ($g.kind -eq 'heavy' -and $gameRuns) { try { $s.proc.PriorityClass = 'BelowNormal' } catch { } }
    $s.status = 'RUN'; $s.t0 = Get-Date
}

while ($true) {
    # finish
    foreach ($s in $state.Values) {
        if ($s.status -eq 'RUN' -and $s.proc.HasExited) {
            $s.secs = [Math]::Round(((Get-Date) - $s.t0).TotalSeconds, 0)
            $s.status = if ($s.proc.ExitCode -eq 0) { 'PASS' } else { 'FAIL' }
            $s.note = "exit $($s.proc.ExitCode)"
            Write-Output ("  {0,-5} {1,-18} {2,4} s" -f $s.status, $s.g.name, $s.secs)
        }
    }
    # start
    $runningHeavy = @($state.Values | Where-Object { $_.status -eq 'RUN' -and $_.g.kind -eq 'heavy' }).Count
    $runningLight = @($state.Values | Where-Object { $_.status -eq 'RUN' -and $_.g.kind -ne 'heavy' }).Count
    foreach ($s in $state.Values) {
        if ($s.status -ne 'WAIT') { continue }
        $g = $s.g
        if ($g.need -and -not (Test-Path -LiteralPath $g.need)) { $s.status = 'SKIP'; $s.note = "needs $($g.need.Replace($Repo + '\', ''))"; continue }
        if ($g.after) {
            $dep = $state[$g.after]
            if ($dep.status -eq 'FAIL' -or $dep.status -eq 'SKIP' -or $dep.status -eq 'REFUSED') { $s.status = 'SKIP'; $s.note = "after $($g.after): $($dep.status)"; continue }
            if ($dep.status -ne 'PASS') { continue }
        }
        if ($g.kind -eq 'heavy') {
            if ($gameRuns -and -not $AllowGame) { $s.status = 'REFUSED'; $s.note = 'Cities2 is running'; continue }
            if ($runningHeavy -ge $maxHeavy) { continue }
            if ((Get-FreeGb) -lt 6) { continue }
            Start-Gate $s; $runningHeavy++
        } else {
            if ($runningLight -ge $maxLight) { continue }
            Start-Gate $s; $runningLight++
        }
    }
    if (-not ($state.Values | Where-Object { $_.status -eq 'WAIT' -or $_.status -eq 'RUN' })) { break }
    Start-Sleep -Milliseconds 700
}
foreach ($s in $state.Values) { if ($s.status -in 'SKIP', 'REFUSED') { Write-Output ("  {0,-7} {1,-18} {2}" -f $s.status, $s.g.name, $s.note) } }

# ---- table ---------------------------------------------------------------------------------------------------------------------
function Get-LastLine($s) {
    $lines = @()
    foreach ($f in $s.log, "$($s.log).err") { if (Test-Path -LiteralPath $f) { $lines += @(Get-Content -LiteralPath $f -Tail 5 | Where-Object { $_.Trim() }) } }
    if ($lines.Count) { $l = ($lines[-1]).Trim(); if ($l.Length -gt 90) { $l = $l.Substring(0, 90) }; return $l }
    return ''
}
$rows = @($run | ForEach-Object { $s = $state[$_.name]; [pscustomobject]@{ gate = $s.g.name; status = $s.status; secs = $s.secs; detail = $(if ($s.status -in 'SKIP', 'REFUSED') { $s.note } else { Get-LastLine $s }) } })
Write-Output ''
if ($Json) { $rows | ConvertTo-Json -Compress } else { $rows | Format-Table -AutoSize -Wrap | Out-String -Width 200 | Write-Output }
foreach ($s in $state.Values | Where-Object status -eq 'FAIL') {
    Write-Output "---- $($s.g.name) FAILED ($($s.note)); last lines of $($s.log):"
    foreach ($f in $s.log, "$($s.log).err") { if (Test-Path -LiteralPath $f) { Get-Content -LiteralPath $f -Tail 25 } }
}
$fail = @($rows | Where-Object status -eq 'FAIL').Count
$incomplete = @($rows | Where-Object { $_.status -in 'SKIP', 'REFUSED' }).Count
$pass = @($rows | Where-Object status -eq 'PASS').Count
Write-Output "gates: $pass PASS, $fail FAIL, $incomplete SKIP/REFUSED of $($rows.Count)$(if ($fail -eq 0 -and $incomplete -eq 0) { ' - ALL GREEN' } elseif ($fail -eq 0) { ' - INCOMPLETE, not green' })"
if ($fail -gt 0) { exit 1 }
if ($incomplete -gt 0) { exit 3 }
exit 0
