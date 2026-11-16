<#
.SYNOPSIS
  Step 8 of docs/RELEASE-CHECKLIST.md section 8 in one command: re-save a baseline on the NEW pack, write its census, put it into
  release/saves-by-release/ (read-only) and print the README row.

.DESCRIPTION
  After a release is published and its pack has downloaded (.cache\Mods\pdx_mods\167215_<n>), the baseline for the NEXT round is made on
  that pack alone (DECISIONS 664): the world is PLAYER with NO overlay, the save is loaded, saved under a NEW name, reloaded, and its census
  is written beside the copy. Everything runs through tools/loadcheck.ps1 (DOM clicks, bridge ops). The game must be running at the main
  menu with the bridge READY, started with the previous baseline STAGED while it was closed (-Stage does that).

  Gates, each a refusal (exit 2) before anything is written:
    - world.ps1 -World Status -Json: world PLAYER, no overlay, ImportedData parked, playset OK.
    - pack-overlay.ps1 -Do Verify: the pack equals its pristine hash list (exit 0).
    - the pack version equals -Version and the pack folder number equals -Pdx (the new version has really downloaded).
    - the target files in release/saves-by-release/ do not exist yet.

  Steps:
    1. (-Alias, dev-id saves only) the bridge op `prefabalias`: maps the dev-made prefab ids of an old save onto the pack's prefabs
       (T228). Needed for a baseline made in the Editor setup (Ash Flat); a save made on the pack alone needs none. The alias lives until the
       game exits, so the PROOF of the new file is a fresh start: run tools\loadcheck.ps1 -Save <new> in a new game session (see the end).
    2. tools/loadcheck.ps1 -Save <previous> -Resave -ResaveName "<game name>": log scan + dev-id gate, census, one month, save, reload.
    3. The new save (.cok + .cid) is copied from the game's Saves folder to release/saves-by-release/<Version>_pdx<Pdx>_<date>_<what>.cok and
       set read-only; its census baseline is written from the reloaded city (loadcheck -NoLoad -WriteBaseline).
    4. The README row is printed (and appended with -WriteReadme; fill in "Holds" by hand).

.PARAMETER Version     The mod version of the new pack ("1.0.1"). Required.
.PARAMETER Pdx         The Paradox Mods version number of the new pack (167215_<n>). Required.
.PARAMETER Save        The previous baseline .cok (default: the newest in release/saves-by-release).
.PARAMETER What        Short label for the file name (default: the part of the old file name after its last "_").
.PARAMETER GameName    The save name inside the game (default "SRM Baseline <Version> <What>"). Must be new.
.PARAMETER Alias       Run the prefabalias op first (the old save carries dev-made prefab ids).
.PARAMETER Stage       Only stage the copy of -Save (game CLOSED; T205), then exit.
.PARAMETER NoMonth     Skip the in-game month of loadcheck part (c).
.PARAMETER OutDir      Where the loadcheck JSON reports go (default docs\research\field\<date>\loadcheck).
.PARAMETER WriteReadme Append the row to release/saves-by-release/README.md.
.PARAMETER WhatIf      Print the plan and the gate results; write and run nothing.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\resave-baseline.ps1 -Version 1.0.2 -Pdx 3 -Stage       (game closed)
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\resave-baseline.ps1 -Version 1.0.2 -Pdx 3             (game at the main menu)

  Exit: 0 done, 1 a loadcheck part FAILed (nothing is copied), 2 a gate refused, 3 broke off.
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][int]$Pdx,
    [string]$Save,
    [string]$What,
    [string]$GameName,
    [switch]$Alias,
    [switch]$Stage,
    [switch]$NoMonth,
    [string]$OutDir,
    [switch]$WriteReadme,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
$Repo = Split-Path -Parent $PSScriptRoot
$Release = Join-Path $Repo 'release\saves-by-release'
# Saves are git-ignored: a worktree has none. Use the main checkout's folder then (git's common dir is <main>\.git).
if (-not (Get-ChildItem $Release -Filter *.cok -ErrorAction SilentlyContinue)) {
    try {
        $common = (& git -C $Repo rev-parse --path-format=absolute --git-common-dir) -join ''
        $mainRel = Join-Path (Split-Path -Parent $common) 'release\saves-by-release'
        if (Test-Path -LiteralPath $mainRel) { $Release = $mainRel }
    } catch { }
}
$LoadCheck = Join-Path $PSScriptRoot 'loadcheck.ps1'
$World = Join-Path $PSScriptRoot 'world.ps1'
$PackOverlay = Join-Path $PSScriptRoot 'pack-overlay.ps1'
$CallJs = Join-Path $PSScriptRoot 'testbridge\bridge-call.mjs'
$UserData = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$PS = 'powershell'

function Stop-Resave([int]$code, [string]$message) { Write-Output "resave-baseline: $message"; exit $code }

if ($Version -notmatch '^\d+\.\d+\.\d+$') { Stop-Resave 2 "-Version must look like 1.0.2 (got '$Version')" }
if (-not $Save) {
    $Save = Get-ChildItem $Release -Filter *.cok -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Save -or -not (Test-Path -LiteralPath $Save)) { Stop-Resave 2 "no previous baseline to start from (-Save <file>, or one in $Release)" }
$oldStem = [IO.Path]::GetFileNameWithoutExtension($Save)
if (-not $What) { $What = ($oldStem -split '_')[-1] }
if (-not $GameName) { $GameName = "SRM Baseline $Version $What" }
$date = Get-Date -Format 'yyyy-MM-dd'
$newStem = "${Version}_pdx${Pdx}_${date}_$What"
$newCok = Join-Path $Release "$newStem.cok"
Write-Output "resave-baseline: $oldStem -> $newStem (game save '$GameName')$(if ($WhatIf) { ' [DRY RUN]' })"

if ($Stage) {
    if ($WhatIf) { Write-Output '  WOULD stage the copy (loadcheck.ps1 -StageOnly)'; exit 0 }
    & $PS -NoProfile -ExecutionPolicy Bypass -File $LoadCheck -StageOnly -Save $Save
    exit $LASTEXITCODE
}

# ---- gates ---------------------------------------------------------------------------------------------------------------
$w = $null
try { $w = (& $PS -NoProfile -ExecutionPolicy Bypass -File $World -World Status -Json) -join '' | ConvertFrom-Json } catch { }
if (-not $w) { Stop-Resave 2 'could not read world.ps1 -World Status -Json' }
Write-Output "  world: $($w.line)"
if ($w.world -ne 'PLAYER' -or $w.overlay -or -not $w.importedParked -or -not $w.playsetOk) {
    Stop-Resave 2 'refused: the baseline is made in the clean Player world with NO overlay (tools\world.ps1 -World Player, then pick the playset it names)'
}
if ($w.packVersion -ne $Version) { Stop-Resave 2 "refused: the pack is $($w.packVersion), not $Version (has the new version downloaded? launcher / subscription)" }
$p = (& $PS -NoProfile -ExecutionPolicy Bypass -File $PackOverlay -Do Status -Json) -join '' | ConvertFrom-Json
if ($p.pack -ne "167215_$Pdx") { Stop-Resave 2 "refused: the newest pack folder is $($p.pack), not 167215_$Pdx" }
$null = & $PS -NoProfile -ExecutionPolicy Bypass -File $PackOverlay -Do Verify
if ($LASTEXITCODE -ne 0) { Stop-Resave 2 'refused: pack-overlay -Do Verify found a changed file in the pack (the baseline must be made on the shipped files)' }
foreach ($ext in '.cok', '.cok.cid', '.census.json') {
    if (Test-Path -LiteralPath (Join-Path $Release "$newStem$ext")) { Stop-Resave 2 "refused: $newStem$ext already exists in $Release" }
}
Write-Output "  gates: world PLAYER (no overlay), pack 167215_$Pdx v$Version verified, target free"

if ($WhatIf) {
    Write-Output "  WOULD $(if ($Alias) { 'run prefabalias, then ' })loadcheck.ps1 -Save $Save -Resave -ResaveName '$GameName'$(if ($NoMonth) { ' -NoMonth' })"
    Write-Output "  WOULD copy the new save + .cid to $newCok (read-only), write $newStem.census.json, print the README row"
    exit 0
}
if (-not (Get-Process Cities2 -ErrorAction SilentlyContinue)) { Stop-Resave 2 'Cities2 is not running: stage with -Stage (game closed), start the game, wait for the bridge READY, run this again' }

# ---- 1. alias (dev-id saves) ---------------------------------------------------------------------------------------------
if ($Alias) {
    $list = Join-Path $PSScriptRoot 'shipped-prefabs.json'
    $json = @{ op = 'prefabalias'; list = $list; dry = $false } | ConvertTo-Json -Compress
    $raw = & node $CallJs --op ([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)))
    $text = ($raw | ForEach-Object { "$_" }) -join "`n"
    Write-Output "  prefabalias: $($text.Substring(0, [Math]::Min(600, $text.Length)))"
    try { $r = $text | ConvertFrom-Json } catch { Stop-Resave 3 'prefabalias gave no JSON' }
    if (-not $r.ok) { Stop-Resave 3 "prefabalias failed: $($r.error)" }
}

# ---- 2. loadcheck: load, report, month, save under the new name, reload ---------------------------------------------------------
$lc = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $LoadCheck, '-Save', $Save, '-Resave', '-ResaveName', $GameName)
if ($NoMonth) { $lc += '-NoMonth' }
if ($OutDir) { $lc += @('-OutDir', $OutDir) }
$out = & $PS @lc 2>&1 | ForEach-Object { "$_"; Write-Output "$_" | Out-Null }
$code = $LASTEXITCODE
$out | ForEach-Object { Write-Output $_ }
if ($code -ne 0) { Stop-Resave $(if ($code -eq 1) { 1 } else { 3 }) "loadcheck exit ${code}: nothing was copied into $Release. Read the lines above." }
$reportLine = @($out | Where-Object { $_ -match '^report: ' }) | Select-Object -Last 1
$report = $null
if ($reportLine) { try { $report = Get-Content ($reportLine -replace '^report: ', '') -Raw -Encoding UTF8 | ConvertFrom-Json } catch { } }

# ---- 3. copy the new save into release/saves-by-release, census baseline ------------------------------------------------------
$savesDir = Get-ChildItem (Join-Path $UserData 'Saves') -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$src = Join-Path $savesDir.FullName "$GameName.cok"
if (-not (Test-Path -LiteralPath $src)) { Stop-Resave 3 "the new save is not at $src" }
Copy-Item -LiteralPath $src -Destination $newCok
if (Test-Path -LiteralPath "$src.cid") { Copy-Item -LiteralPath "$src.cid" -Destination "$newCok.cid" }
Write-Output "  copied: $newCok ($('{0:N1}' -f ((Get-Item $newCok).Length / 1MB)) MB)"
# The reloaded city IS the new save: write its census baseline beside the copy.
$lc2 = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $LoadCheck, '-NoLoad', '-NoMonth', '-WriteBaseline', '-Save', $newCok)
if ($OutDir) { $lc2 += @('-OutDir', $OutDir) }
$out2 = & $PS @lc2 2>&1
$out2 | ForEach-Object { Write-Output "$_" }
if (-not (Test-Path -LiteralPath (Join-Path $Release "$newStem.census.json"))) { Stop-Resave 3 'the census baseline was not written' }
foreach ($f in "$newCok", "$newCok.cid") { if (Test-Path -LiteralPath $f) { (Get-Item -LiteralPath $f).IsReadOnly = $true } }

# ---- 4. README row -------------------------------------------------------------------------------------------------------------
$game = if ($report -and $report.report.gameVersion) { $report.report.gameVersion } else { '?' }
$row = "| ``$newStem.cok`` | $Version | $Pdx | $game | (same layout versions as the row it came from; check SaveLayouts.cs) | Re-saved on the pack alone from ``$oldStem`` (Player world, no overlay$(if ($Alias) { ', dev prefab ids aliased onto the pack' })). FILL IN what the city holds. Census: $newStem.census.json. |"
Write-Output ''
Write-Output 'README row:'
Write-Output $row
if ($WriteReadme) {
    $readme = Join-Path $Release 'README.md'
    $text = Get-Content $readme -Raw -Encoding UTF8
    $marker = "`n`nThe light and modded saves were checked"
    if ($text.Contains($marker)) { $text = $text.Replace($marker, "`n$row$marker") } else { $text += "`n$row`n" }
    [IO.File]::WriteAllText($readme, $text, (New-Object Text.UTF8Encoding $false))
    Write-Output "  README row appended to $readme"
}
Write-Output ''
Write-Output "PROOF on the pack alone (no alias): quit the game, start it again in the same world, then:"
Write-Output "  tools\loadcheck.ps1 -StageOnly -Save $newCok   (before the start, game closed)"
Write-Output "  tools\loadcheck.ps1 -Save $newCok              (exit 0 = every part PASS)"
exit 0
