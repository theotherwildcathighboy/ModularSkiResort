# M7 gate (code review 2026-10-19): SkiResortStatsSystem.m_BuildingQuery requires Game.Buildings.Park, so every prefab it
# must find (the LodgeDefs prefabs in SkiCatalog + the Welcome Sign) has to carry the Game.Prefabs.Park component in its
# prefab JSON (gamedata-mirror). Exit 0 = all carry it, 1 = at least one does not (or is missing from the mirror).
# Usage: powershell -File tools/check-lodge-park.ps1
$root = Split-Path -Parent $PSScriptRoot
$mirror = Join-Path $root 'gamedata-mirror\ImportedData'
$catalog = Get-Content (Join-Path $root 'mod\SkiResortMod\Systems\SkiCatalog.cs') -Raw
$names = New-Object System.Collections.Generic.List[string]
foreach ($const in 'LodgeAFrame', 'LodgeHut', 'LodgeMid', 'LodgeSummit', 'LodgeGrand', 'BusDepot', 'Sign') {
    if ($catalog -match "public const string $const = `"([^`"]+)`"") { $names.Add($Matches[1]) }
    else { Write-Host "FAIL: SkiCatalog.Names.$const not found"; exit 1 }
}
$bad = 0
foreach ($n in $names) {
    $f = Get-ChildItem $mirror -Recurse -Filter "$n.Prefab" -File | Where-Object { (Get-Content $_.FullName -Raw) -match "`"name`":\s*`"$n`"" } | Select-Object -First 1
    if (-not $f) { Write-Host "FAIL  $n : prefab JSON not found in the mirror"; $bad++; continue }
    $raw = Get-Content $f.FullName -Raw
    $isBuilding = $raw -match 'Game\.Prefabs\.BuildingPrefab,'
    $hasPark = $raw -match 'Game\.Prefabs\.Park,'
    if ($isBuilding -and $hasPark) { Write-Host "ok    $n : BuildingPrefab + Game.Prefabs.Park ($($f.Name))" }
    else { Write-Host "FAIL  $n : BuildingPrefab=$isBuilding Park=$hasPark ($($f.Name))"; $bad++ }
}
Write-Host "Park check: $($names.Count - $bad) of $($names.Count) carry Park"
exit $(if ($bad -eq 0) { 0 } else { 1 })
