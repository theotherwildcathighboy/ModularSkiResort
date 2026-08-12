# sync-gamedata.ps1 — mirrors OUR hand-made game-side data between the game's
# ImportedData folder and the repo (gamedata-mirror\ImportedData), so the project's most
# surgical work (prefab JSONs, .cids, icons, loc files) is under version control.
#
#   .\sync-gamedata.ps1              # game -> repo (backup; the default, safe)
#   .\sync-gamedata.ps1 -Restore     # repo -> game (disaster recovery; prompts)
#
# What is mirrored: every top-level ImportedData folder we own (Ski*
# and the Editor mesh-prefab folder c030ee3ff256df91) — but ONLY text-ish artifacts:
# *.Prefab, *.cid, *.loc, *.png. The big Editor-minted binaries (.Geometry, .Surface,
# .Texture) are excluded: they are re-mintable by re-importing the FBX/PNGs (new CIDs,
# then re-point per docs\PIPELINE.md §6), and versioning hundreds of MB of opaque
# binaries buys nothing. TextureAssets\ likewise excluded.

param([switch]$Restore)

$game = Join-Path $env:USERPROFILE "AppData\LocalLow\Colossal Order\Cities Skylines II\ImportedData"
$repo = Join-Path $PSScriptRoot "..\gamedata-mirror\ImportedData"
$include = @("*.Prefab", "*.cid", "*.loc", "*.png")

if ($Restore) {
    Write-Host "RESTORE mode: repo -> game. This overwrites game-side files." -ForegroundColor Yellow
    $confirm = Read-Host "Type RESTORE to proceed"
    if ($confirm -ne "RESTORE") { Write-Host "aborted"; exit 1 }
    $src = $repo; $dst = $game
} else {
    $src = $game; $dst = $repo
}

if (-not (Test-Path $src)) { Write-Error "source missing: $src"; exit 1 }

$owned = Get-ChildItem $src -Directory | Where-Object {
    $_.Name -like "Ski*" -or $_.Name -eq "c030ee3ff256df91"
}

$count = 0
foreach ($dir in $owned) {
    $files = Get-ChildItem $dir.FullName -Recurse -File -Include $include
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($src.Length).TrimStart('\')
        $target = Join-Path $dst $rel
        $targetDir = Split-Path $target -Parent
        if (-not (Test-Path $targetDir)) { New-Item -ItemType Directory -Force $targetDir | Out-Null }
        Copy-Item $f.FullName $target -Force
        $count++
    }
}
Write-Host "synced $count file(s) from '$src' to '$dst'"
Write-Host "(binaries .Geometry/.Surface/.Texture excluded by design - see header)"
