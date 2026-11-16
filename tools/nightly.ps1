<#
.SYNOPSIS
  The unattended nightly check (DECISIONS 669 c): main over the published pack, the newest baseline under a new name, loadcheck, a two-month
  soak at speed 8, the pristine Player world restored, a dated report. Meant for the scheduled task of tools\nightly-task.ps1 (created
  DISABLED; the owner enables it). Safe to run by hand: -WhatIf prints the plan.

.DESCRIPTION
  It runs ONLY when all of these hold, else it logs why and exits 0 without touching anything:
    - Cities2 is closed (the owner is not playing),
    - tools\game-lock.ps1 -Take nightly succeeds (no session or the owner holds the game),
    - the machine has at least 8 GB free RAM and no BF6 / battlefield process runs.
  Steps (each logged with its exit code into <report dir>\nightly.log):
    1  take the lock "nightly"
    2  a dedicated worktree _wt-nightly (detached at origin/main; created on first use) is updated; tools\deploy-worktree.ps1 builds and
       deploys it into Mods\SkiResortMod (the only deploying build; game closed)
    3  tools\world.ps1 -World Player -Build dev -Label "nightly <sha>"  (the pack's own .cok stays: assets added after the last release are not
       in a nightly)
    4  tools\loadcheck.ps1 -StageOnly on the newest baseline of release\saves-by-release, then the game starts with the bridge (direct start)
    5  tools\loadcheck.ps1 -Resave: log scan + dev-id gate, census vs the baseline, one month at speed 8, save under a new name, reload
    6  tools\testbridge\sheet.mjs sheets\nightly-soak.json: two more in-game months at speed 8, the census must not lose anything
    7  the game quits (DOM __dexit), then (always, also after a failure) tools\world.ps1 -World Player restores the pristine pack and the
       lock is released ONLY when world.ps1 -World Status shows the pristine Player world; else the lock stays and the report says so
  The report: docs\research\field\nightly\<yyyy-MM-dd>\report.md (+ the loadcheck / sheet JSON) in the MAIN checkout. Exit 0 = every part
  PASS or nothing to do, 1 = a part FAILED, 3 = the run broke off (the world restore still ran).

  The worktree, the deployed build and the .cok-less overlay are the nightly's own: it never touches the owner's saves, playsets or
  playset_config.json, never publishes, never merges. The saves it makes are named "bridgelc..." (loadcheck) and "SRM Test nightly <date>".

.PARAMETER Save     baseline .cok (default the newest in the main checkout's release\saves-by-release).
.PARAMETER SkipBuild  reuse what Mods\SkiResortMod holds (testing the script).
.PARAMETER Days     soak length in game days = months (default 2).
.PARAMETER WhatIf   print the plan and the preconditions, change nothing.
#>
param(
    [string]$Save,
    [switch]$SkipBuild,
    [int]$Days = 2,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
$Repo = Split-Path -Parent $PSScriptRoot
$common = ((& git -C $Repo rev-parse --path-format=absolute --git-common-dir) -join '').Trim()
$Main = Split-Path -Parent $common                       # the main checkout
$NightlyTree = Join-Path (Split-Path -Parent $Main) '_wt-nightly'
$PS = 'powershell'
$date = Get-Date -Format 'yyyy-MM-dd'
$Report = Join-Path $Main "docs\research\field\nightly\$date"
$LogFile = Join-Path $Report 'nightly.log'
$Lock = Join-Path $PSScriptRoot 'game-lock.ps1'
$World = Join-Path $PSScriptRoot 'world.ps1'
$results = New-Object System.Collections.Generic.List[object]
$tookLock = $false
$overall = 0

function Log([string]$text) {
    $line = "{0:HH:mm:ss} {1}" -f (Get-Date), $text
    Write-Output $line
    if (-not $WhatIf) { New-Item -ItemType Directory -Force $Report | Out-Null; Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8 }
}
function Part([string]$name, [bool]$ok, [string]$detail) {
    $results.Add([pscustomobject]@{ part = $name; ok = $ok; detail = $detail })
    Log ("{0,-5} {1,-14} {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail)
    if (-not $ok) { $script:overall = 1 }
}
function Run([string]$name, [scriptblock]$block) {
    Log "-- $name"
    $out = @(& $block 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $out | Select-Object -Last 40 | ForEach-Object { Add-Content -LiteralPath $LogFile -Value "    $_" -Encoding UTF8 }
    return [pscustomobject]@{ code = $code; out = $out }
}

# ---- preconditions ---------------------------------------------------------------------------------------------------------------
$skip = $null
if (Get-Process -Name Cities2 -ErrorAction SilentlyContinue) { $skip = 'Cities2 is running (the owner is playing)' }
elseif (Get-Process | Where-Object { $_.Name -match '^(bf6|battlefield)' }) { $skip = 'a Battlefield process is running' }
else {
    $freeGb = [Math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 1)
    if ($freeGb -lt 8) { $skip = "only $freeGb GB free RAM" }
}
if (-not $skip -and (Test-Path -LiteralPath (Join-Path $env:TEMP 'srm-game.lock'))) { $skip = "the game lock is held: $((& $PS -NoProfile -File $Lock -Status) -join ' ')" }
if ($skip) { Write-Output "nightly: nothing to do: $skip"; exit 0 }

if (-not $Save) { $Save = Get-ChildItem (Join-Path $Main 'release\saves-by-release') -Filter *.cok -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName }
if (-not $Save) { Write-Output 'nightly: no baseline in release\saves-by-release'; exit 0 }
$stem = [IO.Path]::GetFileNameWithoutExtension($Save)

if ($WhatIf) {
    Write-Output "nightly: DRY RUN. Would: take the lock 'nightly'; update $NightlyTree to origin/main and deploy it$(if ($SkipBuild) { ' (SkipBuild: reuse the deployed build)' }); world.ps1 -World Player -Build dev; stage + load '$stem'; loadcheck -Resave; soak $Days day(s) at 8x; quit; world.ps1 -World Player; release the lock; report in $Report"
    exit 0
}

# ---- run -------------------------------------------------------------------------------------------------------------------------
try {
    New-Item -ItemType Directory -Force $Report | Out-Null
    Log "nightly ${date}: baseline $stem"
    $r = Run 'game-lock -Take nightly' { & $PS -NoProfile -ExecutionPolicy Bypass -File $Lock -Take nightly -Note 'nightly check' }
    if ($r.code -ne 0) { Log 'the lock was taken meanwhile: nothing to do'; exit 0 }
    $tookLock = $true

    $sha = 'main'
    if (-not $SkipBuild) {
        if (-not (Test-Path -LiteralPath $NightlyTree)) { $null = Run 'worktree add' { & git -C $Main worktree add --detach $NightlyTree origin/main } }
        $null = Run 'fetch' { & git -C $NightlyTree fetch -q origin main }
        $null = Run 'checkout origin/main' { & git -C $NightlyTree checkout -q --detach origin/main }
        $sha = ((& git -C $NightlyTree rev-parse --short HEAD) -join '').Trim()
        $r = Run "deploy-worktree ($sha)" { & $PS -NoProfile -ExecutionPolicy Bypass -File (Join-Path $NightlyTree 'tools\deploy-worktree.ps1') }
        Part 'build+deploy' ($r.code -eq 0) "origin/main $sha, exit $($r.code)"
        if ($r.code -ne 0) { throw 'the build or deploy failed' }
    }
    $r = Run 'world Player -Build dev' { & $PS -NoProfile -ExecutionPolicy Bypass -File $World -World Player -Build dev -Label "nightly $sha" }
    Part 'world+overlay' ($r.code -eq 0) ((& $PS -NoProfile -File $World -World Status) -join ' ')
    if ($r.code -ne 0) { throw 'the overlay failed' }

    $r = Run 'stage the baseline' { & $PS -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'loadcheck.ps1') -StageOnly -Save $Save }
    $env:SteamAppId = '949230'; $env:SteamGameId = '949230'; $env:SteamOverlayGameId = '949230'
    $g = 'C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II'
    Start-Process -FilePath "$g\Cities2.exe" -WorkingDirectory $g -ArgumentList '--developerMode', '--uiDeveloperMode' | Out-Null
    $r = Run 'wait for READY' { & $PS -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'testbridge\wait-ready.ps1') -TimeoutSec 900 }
    Part 'game-start' ($r.code -eq 0) "wait-ready exit $($r.code)"
    if ($r.code -ne 0) { throw 'the game did not become READY' }

    $r = Run 'loadcheck -Resave' { & $PS -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'loadcheck.ps1') -Save $Save -Resave -OutDir $Report }
    Part 'loadcheck' ($r.code -eq 0) (($r.out | Where-Object { $_ -match '^\s+\([abcd]\)' }) -join ' | ')
    if ($r.code -ge 2) { throw "loadcheck broke off (exit $($r.code))" }

    $r = Run 'soak sheet' { & node (Join-Path $PSScriptRoot 'testbridge\sheet.mjs') (Join-Path $PSScriptRoot 'testbridge\sheets\nightly-soak.json') --out $Report --var "days=$Days" --var "name=SRM Test nightly $date" }
    Part 'soak' ($r.code -eq 0) (($r.out | Select-Object -Last 2) -join ' | ')
} catch {
    Log "BROKE OFF: $($_.Exception.Message)"
    if ($overall -eq 0) { $overall = 3 }
    $results.Add([pscustomobject]@{ part = 'run'; ok = $false; detail = $_.Exception.Message })
} finally {
  if ($tookLock) {
    # quit the game, restore the world, release the lock only on a pristine Player world
    try {
        if (Get-Process -Name Cities2 -ErrorAction SilentlyContinue) {
            try { & node (Join-Path $PSScriptRoot 'testbridge\bridge-call.mjs') --js ([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('__dexit()'))) --timeout 30 | Out-Null } catch { }
            for ($i = 0; $i -lt 40 -and (Get-Process -Name Cities2 -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Seconds 3 }
            if (Get-Process -Name Cities2 -ErrorAction SilentlyContinue) { Log 'the game did not quit by itself; stopping it'; Stop-Process -Name Cities2 -Force; Start-Sleep -Seconds 5 }
        }
        $r = Run 'world Player (restore)' { & $PS -NoProfile -ExecutionPolicy Bypass -File $World -World Player }
        $status = (& $PS -NoProfile -ExecutionPolicy Bypass -File $World -World Status) -join ' '
        Part 'restore' ($r.code -eq 0) $status
        if ($tookLock) {
            $r = Run 'game-lock -Release' { & $PS -NoProfile -ExecutionPolicy Bypass -File $Lock -Release -Name nightly }
            if ($r.code -ne 0) { Log 'THE LOCK STAYS: the machine is not the pristine Player world. Fix it by hand.'; $script:overall = [Math]::Max($overall, 1) }
        }
    } catch { Log "cleanup error: $($_.Exception.Message)"; $script:overall = 3 }

    $md = @("# Nightly check $date", '', "Baseline: ``$stem``. Exit $overall.", '', '| part | result | detail |', '|---|---|---|')
    foreach ($x in $results) { $md += "| $($x.part) | $(if ($x.ok) { 'PASS' } else { 'FAIL' }) | $(($x.detail -replace '\|', '/')) |" }
    $md += ''; $md += "Log: nightly.log in this folder. Loadcheck and sheet JSON beside it."
    Set-Content -LiteralPath (Join-Path $Report 'report.md') -Value $md -Encoding UTF8
    Log "report: $(Join-Path $Report 'report.md')"
  }
}
exit $overall
