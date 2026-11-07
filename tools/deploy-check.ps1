# deploy-check.ps1 - READ-ONLY gate (T108, DECISIONS 336): is what the game runs what the repo says?
# Deploys nothing, builds nothing, writes nothing. Run it before judging ANY field test. Origin: the 2026-10-19
# evening test ran the 10-18 DLL against 10-19 prefabs (docs/research/field/2026-10-19-evening/analysis.md).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\deploy-check.ps1
#
# The script prints one PASS / STALE / UNKNOWN line per check (the source it read is named in brackets), then a summary.
# Exit 0 = every check PASS. Exit 1 = at least one STALE or UNKNOWN.
#
# THE STAMP carries NO commit. mod/SkiResortMod/SkiResortMod.csproj (target SkiUiBuild) makes it as
# yyyyMMdd-HHmmss (UTC) + 6 random GUID hex chars. It does this ONLY when the UI bundle is rebuilt (a C#-only rebuild keeps the old
# stamp). "ui-" prefixed = a UI-only npm build. So the stamp proves "same build as" (log vs deployed DLL vs bundle),
# never "equals HEAD". File times (what MSBuild's incremental build uses) and git cleanliness decide
# freshness against the code instead. Stamp readers:
#   deployed DLL   - BuildStamp.Value is a const string, found by scanning the DLL bytes (UTF-16) [dll]
#   deployed bundle- SkiResortMod.mjs beside the DLL                                              [mjs]
#   mod log line 3 - "DLL stamp <stamp>" (Mod.cs ReportUiModule), the stamp of the DLL the GAME loaded [log]
#
# Checks:
#   1 deploy      the deployed DLL + bundle exist and carry a stamp
#   2 log-stamp   log line 3 stamp == deployed DLL stamp (else the game ran an older deploy: restart it)
#   3 log-time    the log's first line is newer than the deployed DLL's file time (game started after the deploy)
#   4 bundle      bundle stamp == DLL stamp (a mismatched pair blanks the hub)
#   5 sources     no mod/ source file is newer than the deployed DLL (else: rebuild + deploy. The owner decides)
#   6 git         no uncommitted changes under mod/ (else the deploy cannot be tied to HEAD)
#   7 prefabs     game ImportedData == gamedata-mirror\ (same ownership + include rules as tools\sync-gamedata.ps1, compared by file hashes).
#                 The mirror is a copy of the game side, so a difference means the mirror is behind (run sync-gamedata.ps1)
#                 or the game lacks a file. Mirror-only files are reported, not fatal.
# Never builds. Do not build while Cities2.exe runs. This script only reads.


$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$userData = $env:CSII_USERDATAPATH
if (-not $userData) { $userData = Join-Path $env:USERPROFILE "AppData\LocalLow\Colossal Order\Cities Skylines II" }
$modDir = Join-Path $userData "Mods\SkiResortMod"
$dll = Join-Path $modDir "SkiResortMod.dll"
$mjs = Join-Path $modDir "SkiResortMod.mjs"
$log = Join-Path $userData "Logs\SkiResortMod.Mod.log"
$game = Join-Path $userData "ImportedData"
$mirror = Join-Path $root "gamedata-mirror\ImportedData"
$stampRe = '(?:ui-)?\d{8}-\d{6}-[0-9a-f]{6}'

$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$Name, [string]$Status, [string]$Detail) {
    $results.Add([pscustomobject]@{ Name = $Name; Status = $Status; Detail = $Detail })
    $color = @{ PASS = "Green"; STALE = "Red"; UNKNOWN = "Yellow" }[$Status]
    Write-Host ("{0,-7} {1,-10} {2}" -f $Status, $Name, $Detail) -ForegroundColor $color
}

# The stamp compiled into the DLL: scan for the UTF-16 string at both byte alignments.
function Get-DllStamp([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $found = @()
    foreach ($skip in 0, 1) {
        $text = [System.Text.Encoding]::Unicode.GetString($bytes, $skip, $bytes.Length - $skip - (($bytes.Length - $skip) % 2))
        foreach ($m in [regex]::Matches($text, $stampRe)) { $found += $m.Value }
    }
    $found | Select-Object -Unique
}

$gameRunning = [bool](Get-Process -Name "Cities2" -ErrorAction SilentlyContinue)
Write-Host "deploy-check: HEAD $(git -C $root rev-parse --short HEAD), game running: $gameRunning, deployed folder: $modDir"

# 1 deploy
$dllStamps = @(); $dllTime = $null; $mjsStamps = @()
if (-not (Test-Path $dll)) {
    Add-Result "deploy" "UNKNOWN" "[dll] not found: $dll (never deployed, or Mods folder cleared)"
} else {
    $dllTime = (Get-Item $dll).LastWriteTime
    $dllStamps = @(Get-DllStamp $dll)
    if ($dllStamps.Count -eq 1) {
        Add-Result "deploy" "PASS" ("[dll] SkiResortMod.dll stamp {0}, file time {1:yyyy-MM-dd HH:mm}" -f $dllStamps[0], $dllTime)
    } elseif ($dllStamps.Count -eq 0) {
        Add-Result "deploy" "UNKNOWN" "[dll] no build stamp string in the DLL (built without the UI: 'no-ui'?), file time $($dllTime.ToString('yyyy-MM-dd HH:mm'))"
    } else {
        Add-Result "deploy" "UNKNOWN" "[dll] several stamp-shaped strings in the DLL: $($dllStamps -join ', ')"
    }
}
if (Test-Path $mjs) { $mjsStamps = @([regex]::Matches([System.IO.File]::ReadAllText($mjs), $stampRe) | ForEach-Object { $_.Value } | Select-Object -Unique) }
$dllStamp = if ($dllStamps.Count -eq 1) { $dllStamps[0] } else { $null }

# 2 + 3 log
$logStamp = $null; $logStart = $null
if (-not (Test-Path $log)) {
    Add-Result "log-stamp" "UNKNOWN" "[log] not found: $log (the game has not run since Logs\ was cleared)"
    Add-Result "log-time" "UNKNOWN" "[log] not found"
} else {
    $lines = @(Get-Content $log -TotalCount 12)
    $idx = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "DLL stamp ($stampRe|no-ui)") { $logStamp = $Matches[1]; $idx = $i + 1; break }
    }
    if ($lines.Count -gt 0 -and $lines[0] -match '^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})') {
        $logStart = [datetime]::ParseExact($Matches[1], "yyyy-MM-dd HH:mm:ss", $null)
    }
    if (-not $logStamp) {
        Add-Result "log-stamp" "UNKNOWN" "[log] no 'DLL stamp' in the first 12 lines (log layout changed, or the mod did not load)"
    } elseif (-not $dllStamp) {
        Add-Result "log-stamp" "UNKNOWN" "[log] line $idx says $logStamp, [dll] stamp unreadable"
    } elseif ($logStamp -eq $dllStamp) {
        Add-Result "log-stamp" "PASS" "[log line $idx vs dll] both $dllStamp"
    } else {
        Add-Result "log-stamp" "STALE" "[log line $idx vs dll] the game ran $logStamp, the deployed DLL is ${dllStamp}: restart the game before judging"
    }
    if (-not $logStart -or -not $dllTime) {
        Add-Result "log-time" "UNKNOWN" "[log line 1 vs dll file time] unreadable"
    } elseif ($logStart -ge $dllTime) {
        Add-Result "log-time" "PASS" ("[log line 1 vs dll file time] game started {0:yyyy-MM-dd HH:mm}, after the deploy {1:yyyy-MM-dd HH:mm}" -f $logStart, $dllTime)
    } else {
        Add-Result "log-time" "STALE" ("[log line 1 vs dll file time] game started {0:yyyy-MM-dd HH:mm}, BEFORE the deploy {1:yyyy-MM-dd HH:mm}: the log is from an older DLL" -f $logStart, $dllTime)
    }
}

# 4 bundle
if (-not $dllStamp) {
    Add-Result "bundle" "UNKNOWN" "[dll] stamp unreadable"
} elseif (-not (Test-Path $mjs)) {
    Add-Result "bundle" "UNKNOWN" "[mjs] SkiResortMod.mjs not found beside the DLL"
} elseif ($mjsStamps.Count -eq 1 -and $mjsStamps[0] -eq $dllStamp) {
    Add-Result "bundle" "PASS" "[mjs vs dll] both $dllStamp"
} else {
    Add-Result "bundle" "STALE" "[mjs vs dll] bundle has '$($mjsStamps -join ', ')', DLL has $dllStamp"
}

# 5 sources newer than the deployed DLL
if (-not $dllTime) {
    Add-Result "sources" "UNKNOWN" "[dll] not deployed"
} else {
    $skip = '\\(obj|bin|node_modules|build)\\'
    $src = @()
    $src += Get-ChildItem (Join-Path $root "mod\SkiResortMod") -Recurse -File -Include *.cs, *.csproj, *.targets, *.props -ErrorAction SilentlyContinue
    $src += Get-ChildItem (Join-Path $root "mod\SkiResortUI\src"), (Join-Path $root "mod\SkiResortUI\tools") -Recurse -File -ErrorAction SilentlyContinue
    $src += Get-ChildItem (Join-Path $root "mod\SkiResortUI") -File -Include package.json, webpack.config.js, tsconfig.json, mod.json -ErrorAction SilentlyContinue
    $src = @($src | Where-Object { $_.FullName -notmatch $skip })
    $newer = @($src | Where-Object { $_.LastWriteTime -gt $dllTime } | Sort-Object LastWriteTime -Descending)
    if ($newer.Count -eq 0) {
        Add-Result "sources" "PASS" ("[{0} files under mod/ vs dll file time] none newer than {1:yyyy-MM-dd HH:mm}" -f $src.Count, $dllTime)
    } else {
        $top = ($newer | Select-Object -First 3 | ForEach-Object { "{0} ({1:MM-dd HH:mm})" -f $_.Name, $_.LastWriteTime }) -join ", "
        Add-Result "sources" "STALE" ("[mod/ file times vs dll file time] {0} of {1} source files are newer than the deployed DLL ({2:yyyy-MM-dd HH:mm}); newest: {3}" -f $newer.Count, $src.Count, $dllTime, $top)
    }
}

# 6 git cleanliness under mod/
try {
    $dirty = @(git -C $root status --porcelain -- mod/ 2>$null)
    $head = (git -C $root rev-parse --short HEAD).Trim()
    if ($dirty.Count -eq 0) { Add-Result "git" "PASS" "[git status mod/] clean at HEAD $head" }
    else { Add-Result "git" "STALE" "[git status mod/] $($dirty.Count) uncommitted path(s) at HEAD $head, e.g. $(($dirty | Select-Object -First 3) -join '; ')" }
} catch {
    Add-Result "git" "UNKNOWN" "[git] $($_.Exception.Message)"
}

# 7 prefabs: game ImportedData vs gamedata-mirror (ownership + include rules of tools\sync-gamedata.ps1)
if (-not (Test-Path $game)) {
    Add-Result "prefabs" "UNKNOWN" "[ImportedData] not found: $game"
} elseif (-not (Test-Path $mirror)) {
    Add-Result "prefabs" "UNKNOWN" "[gamedata-mirror] not found: $mirror"
} else {
    $include = @("*.Prefab", "*.cid", "*.loc", "*.png")
    function Get-Owned([string]$Base) {
        $out = @{}
        Get-ChildItem $Base -Directory | Where-Object { $_.Name -like "Ski*" -or $_.Name -eq "c030ee3ff256df91" } | ForEach-Object {
            Get-ChildItem $_.FullName -Recurse -File -Include $include | ForEach-Object {
                $out[$_.FullName.Substring($Base.Length).TrimStart('\')] = $_.FullName
            }
        }
        $out
    }
    $g = Get-Owned $game; $m = Get-Owned $mirror
    $differ = New-Object System.Collections.Generic.List[string]
    $gameOnly = New-Object System.Collections.Generic.List[string]
    $mirrorOnly = New-Object System.Collections.Generic.List[string]
    foreach ($rel in $g.Keys) {
        if (-not $m.ContainsKey($rel)) { $gameOnly.Add($rel); continue }
        if ((Get-FileHash $g[$rel] -Algorithm SHA256).Hash -ne (Get-FileHash $m[$rel] -Algorithm SHA256).Hash) { $differ.Add($rel) }
    }
    foreach ($rel in $m.Keys) { if (-not $g.ContainsKey($rel)) { $mirrorOnly.Add($rel) } }
    $bad = $differ.Count + $gameOnly.Count
    $note = if ($mirrorOnly.Count) { "; $($mirrorOnly.Count) mirror-only file(s) (the game lacks them; sync never deletes): $((@($mirrorOnly) | Select-Object -First 3) -join ', ')" } else { "" }
    if ($bad -eq 0 -and $mirrorOnly.Count -eq 0) {
        Add-Result "prefabs" "PASS" "[ImportedData vs gamedata-mirror] $($g.Count) files identical"
    } elseif ($bad -eq 0) {
        Add-Result "prefabs" "PASS" "[ImportedData vs gamedata-mirror] $($g.Count) game files identical$note"
    } else {
        $ex = @($differ) + @($gameOnly | ForEach-Object { "$_ (not in mirror)" })
        Add-Result "prefabs" "STALE" "[ImportedData vs gamedata-mirror] $($differ.Count) differ, $($gameOnly.Count) game-only of $($g.Count); e.g. $((@($ex) | Select-Object -First 3) -join ', '); run tools\sync-gamedata.ps1 if the game side is right$note"
    }
}

$stale = @($results | Where-Object Status -eq "STALE").Count
$unknown = @($results | Where-Object Status -eq "UNKNOWN").Count
$pass = @($results | Where-Object Status -eq "PASS").Count
if ($stale -eq 0 -and $unknown -eq 0) {
    Write-Host "SUMMARY: ALL $pass CHECKS PASS - the game runs the deployed build (stamp $dllStamp) and the prefabs match the mirror." -ForegroundColor Green
    exit 0
}
Write-Host "SUMMARY: $pass PASS, $stale STALE, $unknown UNKNOWN - do NOT judge a field test until this is resolved." -ForegroundColor Red
exit 1
