# preflight.ps1 - repo-wide prefab pre-flight for the Modular Ski Resort pack.
#
# Extends validate_prefab.ps1 (which stays the single-file tool) to the whole install:
#   1. VALIDATE  - the four single-file checks (type-table forward refs, redeclarations,
#                  shell-mangle shapes, brace balance) over every prefab we own.
#   2. RESOLVE   - every $fstrref:"CID:..." must exist as some .cid under ImportedData.
#   3. VANILLA   - every $fstrref:"UnityGUID:..." / "UnityObjectsMap:..." is listed and
#                  checked against the known-GUID whitelist (unknown = WARN, not failure).
#   4. CIDFILES  - every owned .cid is exactly 32 lowercase hex, no BOM, no newline.
#   5. BOM       - no UTF-8 BOM on owned .Prefab files.
#   6. DUPCID    - no cid value held by two .cid files anywhere under ImportedData (F7 fixer).
#
# Ownership = directories named Ski* under ImportedData, Ski* prefabs inside the
# Editor library folder (c030ee3ff256df91), plus legacy FixedGripChairLift (reported).
# Exit code: number of FAILURES (warnings don't fail).

param(
    [string]$ImportedData = (Join-Path $env:USERPROFILE "AppData\LocalLow\Colossal Order\Cities Skylines II\ImportedData")
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path $ImportedData)) { Write-Output "FATAL: ImportedData not found: $ImportedData"; exit 1 }

# Known vanilla references (docs/PIPELINE.md + docs/research/{vanilla-reuse,golfpack-dissection,trails-surfaces-glades}.md)
$KnownVanilla = @{
    "5d88ac45a40b30341a79535930e3b735" = "High-voltage Cable Lane"
    "37c8abdb015384f4e8b7ce9b5b2b6c9b" = "Low-voltage wire lane"
    "60c0e17c40a5f1b41bf9d2032670b81d" = "Landscaping>Surfaces UI category"
    "5930db6811522d34495a9e16c4fd4c18" = "Landscaping service"
    "a5749eadf2820a247afc6747989ed959" = "Parks&Rec toolbar menu"
    "8ba97fa363a0af1479f01031029da963" = "LargeSportsParks UI category"
    "5c1f656a5ea20284197a71e4997b4187" = "Parks&Rec service"
    "a1b9ab64e2f648a4da17231062c1e066" = "shared RenderedArea material"
    "a534d6be94fb0cb4eb683f62412d0b1a" = "Electricity service (vanilla HV Line ServiceObject)"
    "c92b9810143a9ba4ca5e004fc709d92f" = "vanilla HV Line unlock milestone 1"
    "9af2c85bf8f3400438a82523ed61b8a4" = "vanilla HV Line unlock milestone 2"
    # Relabelled 2026-10-03 (Round M1, hub-u2-spec.md 6.1 b/h): SkiChairliftHV no longer references it
    # (its m_UndergroundPrefab is null since M1); the two station markers still do. Likely, not verified:
    # the dump carries no GUIDs - it is the m_UndergroundPrefab of the vanilla Low-voltage Line and Marker.
    # Relabel, never drop: an unlisted vanilla ref turns into a WARN (section 3 below).
    "9cbf401c04ad63b44a36c0dc0db26e67" = "Low-voltage Ground Cable (m_UndergroundPrefab of the vanilla LV line; used by SkiLiftMarker/LV)"
    "3e47e765f44f210409edf4e6229fd0df" = "Grass Surface 01 unlock requirement (verified: vanilla Grass 01 ships same ref)"
    "ec48568022d46c1448c6620d95f993c1" = "ParkSign placeholder (vanilla CityPark01/02/04 host it; resolves ParkSign01-03)"
    "4010d5851d69dbb418c62e6bd4d19759" = "PowerLinePole01 placeholder (verified: Low-voltage Line NetSubObjects m_Object + PowerLinePole8m01 SpawnableObject back-ref; resolves PowerLinePole 8/13/18m)"
}

# The all-zeros CID is the engine's null-asset sentinel (vanilla HV Cables Piece ships it
# as m_GeometryAsset) - never a broken reference.
$NullCid = "00000000000000000000000000000000"

# ---- collect owned files -------------------------------------------------------------
$ownedPrefabs = New-Object System.Collections.Generic.List[object]
$ownedCids    = New-Object System.Collections.Generic.List[object]

Get-ChildItem $ImportedData -Directory | Where-Object { $_.Name -like "Ski*" -or $_.Name -eq "FixedGripChairLift" } | ForEach-Object {
    $ownedPrefabs.AddRange(@(Get-ChildItem $_.FullName -Recurse -Filter *.Prefab -File))
    $ownedCids.AddRange(@(Get-ChildItem $_.FullName -Recurse -Filter *.cid -File))
}
$lib = Join-Path $ImportedData "c030ee3ff256df91"
if (Test-Path $lib) {
    Get-ChildItem $lib -Recurse -File | Where-Object { $_.Name -like "Ski*" } | ForEach-Object {
        if ($_.Extension -eq ".Prefab") { $ownedPrefabs.Add($_) }
        if ($_.Extension -eq ".cid")    { $ownedCids.Add($_) }
    }
    Get-ChildItem $lib -Directory | Where-Object { $_.Name -like "Ski*" } | ForEach-Object {
        $ownedPrefabs.AddRange(@(Get-ChildItem $_.FullName -Recurse -Filter *.Prefab -File))
        $ownedCids.AddRange(@(Get-ChildItem $_.FullName -Recurse -Filter *.cid -File))
    }
}
$ownedPrefabs = @($ownedPrefabs | Sort-Object FullName -Unique)
$ownedCids    = @($ownedCids    | Sort-Object FullName -Unique)

# All resolvable CIDs anywhere in the install (vanilla refs never point here; ours must)
$failures = 0
$warnings = 0
$rows = New-Object System.Collections.Generic.List[string]

# A cid value held by two files is two assets with one GUID: the asset database resolves the reference to
# whichever it indexes (F7 fixer: a bulk copy of ski_icons.py output once could duplicate the launcher's cid).
$allCidValues = @{}
Get-ChildItem $ImportedData -Recurse -Filter *.cid -File | ForEach-Object {
    try { $v = ([IO.File]::ReadAllText($_.FullName)).Trim().ToLower() } catch { return }
    if ($v.Length -eq 32) {
        if ($allCidValues.ContainsKey($v) -and $allCidValues[$v] -ne $_.FullName) {
            $first = $allCidValues[$v].Substring($ImportedData.Length + 1)
            $rows.Add("FAIL  DUPCID    $v : $first and $($_.FullName.Substring($ImportedData.Length + 1))"); $failures++
        }
        else { $allCidValues[$v] = $_.FullName }
    }
}

# ---- 1. validator checks per prefab --------------------------------------------------
function Test-PrefabFile([string]$path) {
    $lines = Get-Content $path
    $declared = @{}
    $errs = New-Object System.Collections.Generic.List[string]
    $braces = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $braces += ([regex]::Matches($line, '{')).Count - ([regex]::Matches($line, '}')).Count
        if ($line.Length -gt 400 -or $line -match '&n |\\n"') { $errs.Add("line $($i+1): suspicious line (len $($line.Length)) - possible shell-mangle") }
        if ($line -match '"\$type":\s*"(\d+)\|([^"]+)"') {
            $idx = [int]$Matches[1]; $name = $Matches[2]
            if ($declared.ContainsKey($idx) -and $declared[$idx] -ne $name) { $errs.Add("line $($i+1): type id $idx redeclared as '$name' (was '$($declared[$idx])')") }
            $declared[$idx] = $name
        }
        elseif ($line -match '"\$type":\s*(\d+)\s*,?\s*$') {
            $idx = [int]$Matches[1]
            if (-not $declared.ContainsKey($idx)) { $errs.Add("line $($i+1): forward/undeclared type reference $idx") }
        }
    }
    if ($braces -ne 0) { $errs.Add("unbalanced braces (delta $braces)") }
    return , $errs
}

$refRe = [regex]'\$fstrref:"(CID|UnityGUID|UnityObjectsMap):([0-9a-fA-F]{32})"'
$vanillaSeen = @{}

foreach ($p in $ownedPrefabs) {
    $rel = $p.FullName.Substring($ImportedData.Length + 1)

    $errs = Test-PrefabFile $p.FullName
    foreach ($e in $errs) { $rows.Add("FAIL  VALIDATE  $rel : $e"); $failures++ }

    $bytes = [IO.File]::ReadAllBytes($p.FullName)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $rows.Add("FAIL  BOM       $rel : UTF-8 BOM present"); $failures++
    }

    $text = [Text.Encoding]::UTF8.GetString($bytes)
    foreach ($m in $refRe.Matches($text)) {
        $kind = $m.Groups[1].Value; $val = $m.Groups[2].Value.ToLower()
        if ($kind -eq "CID") {
            if (-not $allCidValues.ContainsKey($val) -and $val -ne $NullCid) { $rows.Add("FAIL  RESOLVE   $rel : unresolved CID $val"); $failures++ }
        }
        else {
            if (-not $vanillaSeen.ContainsKey($val)) { $vanillaSeen[$val] = New-Object System.Collections.Generic.List[string] }
            $vanillaSeen[$val].Add($rel)
        }
    }
}

# ---- 3. vanilla ref listing ----------------------------------------------------------
foreach ($k in ($vanillaSeen.Keys | Sort-Object)) {
    $where = ($vanillaSeen[$k] | Sort-Object -Unique) -join ", "
    if ($KnownVanilla.ContainsKey($k)) {
        $rows.Add("OK    VANILLA   $k ($($KnownVanilla[$k]))  <- $where")
    } else {
        $rows.Add("WARN  VANILLA   $k UNLISTED vanilla ref - verify in dump  <- $where"); $warnings++
    }
}

# ---- 4. cid file hygiene -------------------------------------------------------------
foreach ($c in $ownedCids) {
    $rel = $c.FullName.Substring($ImportedData.Length + 1)
    $bytes = [IO.File]::ReadAllBytes($c.FullName)
    $ok = $true
    if ($bytes.Length -ne 32) { $rows.Add("FAIL  CIDFILE   $rel : length $($bytes.Length) (want exactly 32 bytes)"); $failures++; $ok = $false }
    if ($ok) {
        $s = [Text.Encoding]::ASCII.GetString($bytes)
        if ($s -cnotmatch '^[0-9a-f]{32}$') { $rows.Add("FAIL  CIDFILE   $rel : not 32 lowercase hex"); $failures++ }
    }
}

# ---- summary -------------------------------------------------------------------------
$rows | ForEach-Object { Write-Output $_ }
Write-Output ""
Write-Output ("preflight: {0} prefabs, {1} cid files checked | {2} FAIL, {3} WARN" -f $ownedPrefabs.Count, $ownedCids.Count, $failures, $warnings)
exit $failures
