# pack-overlay.ps1 - lay a local build of the code mod over the installed Paradox Mods pack, and put the pack back (T198, T228).
#
# The Player test setup (DECISIONS 664) runs the published pack. To test new code in that setup, the build's code and UI files replace the
# pack's copies in place; the .cok (assets) stays the released file. Adapted from docs/research/field/2026-11-15/pack-overlay.ps1
# (167215_1) for any installed version: the pack folder is .cache\Mods\pdx_mods\<Pack>, the backups live in
# %USERPROFILE%\Documents\CS2-mods-aside\<Pack>-originals.
#
#   -Do Snapshot  record the SHA-256 of every pack file (_original-hashes.csv) and back up the files an overlay may replace. Refuses
#                 when a snapshot exists (the pack may already be overlaid). Run once on a pristine pack.
#   -Do Verify    compare the pack with the snapshot: MISSING / MISMATCH / EXTRA. Exit 0 only when it matches.
#   -Do Overlay   Verify first (refuses on any difference), then copy the build's files over the pack. -From: the deployed build folder
#                 (default Mods\SkiResortMod, else the parked dev copy).
#   -Do Restore   remove the files the overlay added, copy the backups back, Verify.
#
# Refuses while Cities2 runs. PowerShell 5.1. Exit 0 ok, 1 refused or not matching.
param(
    [ValidateSet('Snapshot', 'Verify', 'Overlay', 'Restore')][string]$Do,
    [string]$Pack = '167215_2',
    [string]$From
)
$ErrorActionPreference = 'Stop'
if (Get-Process Cities2 -ErrorAction SilentlyContinue) { 'refused: game running'; exit 1 }
$ud = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$packDir = Join-Path $ud ".cache\Mods\pdx_mods\$Pack"
if (-not (Test-Path -LiteralPath $packDir)) { "refused: no pack folder $packDir"; exit 1 }
$bk = Join-Path $env:USERPROFILE "Documents\CS2-mods-aside\$Pack-originals"
$csvPath = Join-Path $bk '_original-hashes.csv'
$addedPath = Join-Path $bk '_added-files.txt'
$code = @('SkiResortMod.dll', 'SkiResortMod.mjs', 'SkiResortMod.mjs.LICENSE.txt', 'SkiResortMod.css', 'SkiResortMod_win_x86_64.dll')
$dirs = @('SkiResortMod-img', 'SkiResortMod-icons', 'Textures')

function Rel([string]$full) { $full.Substring($packDir.Length + 1) }

if ($Do -eq 'Snapshot') {
    if (Test-Path -LiteralPath $csvPath) { "refused: snapshot exists ($csvPath)"; exit 1 }
    New-Item -ItemType Directory -Force $bk | Out-Null
    $rows = Get-ChildItem -LiteralPath $packDir -Recurse -File | ForEach-Object {
        [pscustomobject]@{ rel = (Rel $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
    $rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
    $n = 0
    foreach ($r in $rows) {
        $top = ($r.rel -split '\\')[0]
        if ($code -contains $r.rel -or $dirs -contains $top) {
            $b = Join-Path $bk $r.rel
            New-Item -ItemType Directory -Force (Split-Path $b) | Out-Null
            Copy-Item -LiteralPath (Join-Path $packDir $r.rel) -Destination $b
            $n++
        }
    }
    "snapshot: $($rows.Count) files hashed, $n backed up to $bk"
    exit 0
}

$csv = Import-Csv -LiteralPath $csvPath
function Verify {
    $bad = 0
    foreach ($r in $csv) {
        $p = Join-Path $packDir $r.rel
        if (-not (Test-Path -LiteralPath $p)) { Write-Host "MISSING $($r.rel)"; $bad++; continue }
        if ((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ne $r.sha256) { Write-Host "MISMATCH $($r.rel)"; $bad++ }
    }
    $added = @(Get-ChildItem -LiteralPath $packDir -Recurse -File | ForEach-Object { Rel $_.FullName } | Where-Object { $csv.rel -notcontains $_ })
    foreach ($a in $added) { Write-Host "EXTRA $a" }
    Write-Host "verify: $($csv.Count) files in list, $bad bad, $($added.Count) extra"
    return $bad + $added.Count
}

if ($Do -eq 'Verify') { $n = Verify; exit ([int]($n -gt 0)) }

if ($Do -eq 'Overlay') {
    if ((Verify) -ne 0) { 'refused: pack is not pristine'; exit 1 }
    if (-not $From) {
        $From = Join-Path $ud 'Mods\SkiResortMod'
        if (-not (Test-Path -LiteralPath $From)) { $From = Join-Path $env:USERPROFILE 'Documents\CS2-mods-aside\SkiResortMod-dev' }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $From 'SkiResortMod.dll'))) { "refused: no SkiResortMod.dll in $From"; exit 1 }
    $files = @($code | Where-Object { Test-Path -LiteralPath (Join-Path $From $_) })
    foreach ($d in $dirs) {
        if (Test-Path -LiteralPath (Join-Path $From $d)) { $files += Get-ChildItem -LiteralPath (Join-Path $From $d) -File | ForEach-Object { "$d\$($_.Name)" } }
    }
    $added = @()
    foreach ($f in $files) {
        $dst = Join-Path $packDir $f
        if (-not (Test-Path -LiteralPath $dst)) { $added += $f }
        elseif (-not (Test-Path -LiteralPath (Join-Path $bk $f))) {
            New-Item -ItemType Directory -Force (Split-Path (Join-Path $bk $f)) | Out-Null
            Copy-Item -LiteralPath $dst -Destination (Join-Path $bk $f); "backed up late: $f"
        }
        New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
        Copy-Item -LiteralPath (Join-Path $From $f) -Destination $dst -Force
    }
    $added | Set-Content -LiteralPath $addedPath
    "overlaid $($files.Count) files from $From, $($added.Count) new: $($added -join ', ')"
    exit 0
}

if ($Do -eq 'Restore') {
    if (Test-Path -LiteralPath $addedPath) {
        foreach ($a in @(Get-Content -LiteralPath $addedPath | Where-Object { $_ })) {
            $p = Join-Path $packDir $a
            if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force }
        }
    }
    foreach ($r in $csv) {
        $src = Join-Path $bk $r.rel
        if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $packDir $r.rel) -Force }
    }
    $n = Verify
    exit ([int]($n -gt 0))
}
