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
#   7. NAMES     - every prefab name of mod/SkiResortMod/Systems/SkiCatalog.cs (the lines between
#                  // NAMES-BEGIN and // NAMES-END) is the root name of exactly one owned prefab whose
#                  root $type is the catalog's PrefabID type, and a hub build item carries an icon
#                  (Catalog Phase A, 2026-10-11; docs/research/art/assets-2026-10/catalog-a/phase-a-contract.md
#                  R12; extensibility.md F9 / row 24). Catalog Phase B (code-round-2/round-2-contract.md 1.1, D5,
#                  D14): a line ending ", pending" is a designed name not imported yet - absent = OK (pending),
#                  present = WARN until its ", pending" is dropped and its row set Expected (the rows work either way).
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
    "463c051da8a824c4a8f2f830a5011a3e" = "High-voltage Cable Lane Mesh (T01 2026-10-16: the three 64-type rope lanes; dump-verified)"
    "bbb9724d6daa32241821c52b428c196c" = "Low-voltage Cable Lane Mesh (T01 2026-10-16: SkiTramHaulRope; dump-verified)"
    "59a83de6faed898459bde98b0256b452" = "PowerLinePole01 Marker (MarkerObjectPrefab; the vanilla Low-voltage Marker's node sub-object; T19 2026-10-16 on SkiLiftMarkerLV; dump-verified)"
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
    "ec48568022d46c1448c6620d95f993c1" = "ParkSign placeholder (vanilla CityPark01/02/04 host it; resolves ParkSign01-03). Relabelled 2026-10-13: SkiResortGate no longer references it (S1 sign surgery replaced its ParkSign row, build/s1-finish.md); no owned prefab does. Kept, never dropped"
    "4010d5851d69dbb418c62e6bd4d19759" = "PowerLinePole01 placeholder (verified: Low-voltage Line NetSubObjects m_Object + PowerLinePole8m01 SpawnableObject back-ref; resolves PowerLinePole 8/13/18m)"
    # Vanilla sub-object placeholders of the shipped manifests' rows (fine-detail step 3d, 2026-10-12: every
    # sub_objects / detail1 / detail row GUID of assets/import/**/*.manifest.json, archive excluded). Each is the
    # placeholder GUID its variants' m_Placeholders back-reference in the vanilla dump (%TEMP%\pltar\dump.zip);
    # dump-verified by cs2_liftdress.SUBOBJ and / or assets/blender/subobj_bounds.json (lodge.verified_guids).
    "b99325ff4ff4dfd459734d8f765c2597" = "ACVentWallRandom01 placeholder (inferred: variants ACVentWall01/ACVentWall02/ACVentWall03/ACVentWall04 back-ref it; dump-verified subobj_bounds.json)"
    "fb4247f7a6f75b1478d9dd01ac88ce86" = "BarrelRandom01 placeholder (inferred: variants Barrel01/Barrel02/Barrel03 back-ref it; dump-verified cs2_liftdress.SUBOBJ + subobj_bounds.json)"
    # Orange safety-net fence (S1, build/safety-fence.md 4.3): the vanilla UI group and category its staged
    # NetFencePrefab references (read from the installed 1.6.2 resources.assets PrefabsMap, design/safety-fence.md).
    "05564431524791c48846dbd183fd232d" = "UIObject group Landscaping > Pathways (vanilla; the fence's toolbar home)"
    "71df37e0fc45ade46a64fba0eedd7c54" = "UIObjectOverrideGroup Fences category (vanilla; no menu, Find It-side only)"
    "dbc2499e4450e9749a7ee25125c907bf" = "FlagPole01 (S1 GUID harvest 2026-10-13: resources.assets PrefabsMap, field/2026-10-13/guid-harvest.json)"
    "fa676f526a9a7234897c82f0e27f87f9" = "FlagPole02 (S1 GUID harvest 2026-10-13: resources.assets PrefabsMap, field/2026-10-13/guid-harvest.json)"
    "3a7712ef866489b4db3fa543f5f86e74" = "SportParkFlag01 (S1 GUID harvest 2026-10-13: resources.assets PrefabsMap, field/2026-10-13/guid-harvest.json)"
    "99f8212c93f8f5548b69dea85c85e4c2" = "SportParkTrainingCone02 (S1 GUID harvest 2026-10-13: resources.assets PrefabsMap, field/2026-10-13/guid-harvest.json)"
    "fc5ffc8fb618e0045974f880b5ebd72d" = "CampgroundBench02 (S1 GUID harvest 2026-10-13: resources.assets PrefabsMap, field/2026-10-13/guid-harvest.json)"
    "dc9890337b0b891448ae01040562a0c7" = "ElectricalBox01 (S1 GUID harvest 2026-10-13: resources.assets PrefabsMap, field/2026-10-13/guid-harvest.json)"
    "adb8f5879fe0ae64b886341f8dedd97d" = "ElectricalBox02 (S1 GUID harvest 2026-10-13: resources.assets PrefabsMap, field/2026-10-13/guid-harvest.json)"
    "4f10c06b0f1853547bc9d77c0dfe518b" = "CampgroundFireplace01 (S1 GUID harvest 2026-10-13: resources.assets PrefabsMap, field/2026-10-13/guid-harvest.json)"
    "98c77a4f2885dd541a7a3700edefa21e" = "CampgroundFireplace02 (S1 GUID harvest 2026-10-13: resources.assets PrefabsMap, field/2026-10-13/guid-harvest.json)"
    "a95fff86c89ea3146a5730a81b7ecfba" = "CampgroundFireplace03 (S1 GUID harvest 2026-10-13: resources.assets PrefabsMap, field/2026-10-13/guid-harvest.json)"
    "024ff26a34d89b241b55fd5ea12c5dcc" = "ChairCommercialRandom01 placeholder (inferred: variants ChairCommercial01/ChairCommercial02/ChairCommercial03/ChairCommercial04 back-ref it; dump-verified subobj_bounds.json)"
    "63c46490ab59afd4dae01ee442f1e462" = "DeckChairRandom01 placeholder (inferred: variants DeckChair01/DeckChair02/DeckChair03 back-ref it; dump-verified subobj_bounds.json)"
    "68c87b3894d5ed74d8180dd43c12da30" = "FenceResidentialPieceLowRandom01 placeholder (inferred: variants FenceResidentialPieceLow01/FenceResidentialPieceLow02 back-ref it; dump-verified subobj_bounds.json)"
    "ed041afbdfcc3db4188ce264b34304a2" = "FenceResidentialPieceLowShortRandom01 placeholder (inferred: variants FenceResidentialPieceLowShort01/FenceResidentialPieceLowShort02 back-ref it; dump-verified subobj_bounds.json)"
    "0d01c98e6f7cd9b44ac31084b6ba7ccd" = "FirewoodPileRandom01 placeholder (inferred: variants FirewoodPile01 back-ref it; dump-verified subobj_bounds.json)"
    "c1d98e3e6f5e1204cb071deaf0f74020" = "FirewoodShelterRandom01 placeholder (inferred: variants FirewoodShelter01/FirewoodShelter03 back-ref it; dump-verified subobj_bounds.json)"
    "8df0703be09426948b5bc3262d86bc04" = "GardenBenchRandom01 placeholder (inferred: variants GardenBench01/GardenBench02/GardenBench03/GardenBench04 back-ref it; dump-verified cs2_liftdress.SUBOBJ + subobj_bounds.json)"
    "ba47f549eb430ac46a7c1b4cca509594" = "GardenLightRandom01 placeholder (inferred: variants GardenLight01/GardenLight02/GardenLight03 back-ref it; dump-verified subobj_bounds.json)"
    "0bbcb0620e01694448cb78385580351b" = "GasHeaterRandom01 placeholder (inferred: variants GasHeater01/GasHeater02 back-ref it; dump-verified subobj_bounds.json)"
    "c987b53e24a01e744b4a00ee5b1f8a08" = "LightpoleParkRandom01 placeholder (inferred: variants LightpolePark01/LightpolePark02/LightpolePark03/LightpolePark04 back-ref it; dump-verified cs2_liftdress.SUBOBJ + subobj_bounds.json)"
    "b7e2343fd7ff9f642b77084a04738e43" = "ParasolRandom01 placeholder (inferred: variants Parasol01/Parasol02/Parasol03 back-ref it; dump-verified subobj_bounds.json)"
    "3f8dafc1558bce24597bd8ca898947fa" = "ParkInfosignRandom01 placeholder (inferred: variants ParkInfosign01/ParkInfosign02/ParkInfosign03 back-ref it; dump-verified cs2_liftdress.SUBOBJ + subobj_bounds.json)"
    "8d8722db083c3564291e3778cceeb1ae" = "PotRandom01 placeholder (inferred: variants FlowerPot01/FlowerPot02/PlantPot01/PlantPot02 back-ref it; dump-verified cs2_liftdress.SUBOBJ + subobj_bounds.json)"
    "b0ea4a4915b28f14a826bb809e02db7a" = "RoofLightRandom01 placeholder (inferred: variants RoofLight01/RoofLight02/RoofLight03/RoofLight04 back-ref it; dump-verified cs2_liftdress.SUBOBJ + subobj_bounds.json)"
    "dc8abeea43367b24ba2668784a309e56" = "SafetyConeRandom01 placeholder (inferred: variants SafetyCone01/SafetyCone02/SafetyCone03/SafetyCone04 back-ref it; dump-verified cs2_liftdress.SUBOBJ + subobj_bounds.json)"
    "a4fdfd0ffe3c5fa4bb7cc6522ef81d01" = "SnowmanRandom01 placeholder (inferred: variants Snowman01/Snowman02/Snowman03 back-ref it; dump-verified subobj_bounds.json)"
    "fc058a3d03b337e4794c432a84170316" = "TableSetCommercialRandom01 placeholder (inferred: variants TableSetCommercial01/TableSetCommercial02/TableSetCommercial03/TableSetCommercial04 back-ref it; dump-verified cs2_liftdress.SUBOBJ + subobj_bounds.json)"
    "41e3c9ba6e1d6b948a6b1bc3499a5397" = "TrashbinRandom01 placeholder (inferred: variants Trashbin01/Trashbin02/Trashbin03/Trashbin04 back-ref it; dump-verified cs2_liftdress.SUBOBJ + subobj_bounds.json)"
    "f7955fd41c1617847b4a4138acabcbcb" = "TreeWildRandom01 placeholder (inferred: variants BirchTree01/EU_AlderTree01/NA_HickoryTree01/OakTree01 back-ref it; dump-verified subobj_bounds.json)"
    "96d63256a8b9f4942b1949c7067e6613" = "WallLightRandom01 placeholder (inferred: variants WallLight01/WallLight02/WallLight03/WallLight04 back-ref it; dump-verified subobj_bounds.json)"
    "cfc12fde01d9da24b86ddedd657eaae1" = "WallLightShortRangeRandom01 placeholder (inferred: variants WallLightShortRange01/WallLightShortRange02/WallLightShortRange03 back-ref it; dump-verified cs2_liftdress.SUBOBJ + subobj_bounds.json)"
    "6b6861547d4e1964aa977434af734126" = "WarningLightRandom01 placeholder (inferred: variants WarningLight01/WarningLight02 back-ref it; dump-verified cs2_liftdress.SUBOBJ + subobj_bounds.json)"
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

# ---- 7. NAMES: the prefab catalog's names against the owned prefabs ----------------------
# Catalog Phase A (phase-a-contract.md R12; extensibility.md F9, row 24). A prefab's save identity is its PrefabID =
# root $type + name (PrefabSystem.cs:827-865 Serialize, :878-892 the load lookup; tools/decompile/1.6.0f1), so a
# catalog name must be the root "name" of exactly ONE owned prefab, with the catalog's type. Lines between the
# markers must each match the NAMES format exactly; the regex is tests/LiftBatch CatalogTests CT-8's, and it is
# matched case-sensitively here too ([regex]::Match, like CT-8; PowerShell's -match would not be).
# Root identity per owned prefab (both layouts above): the first 4-space-indented "name" and the first
# "$type": "0|Game.Prefabs.X, Game". Library render prefabs are named "X Mesh" and never collide.
# Catalog Phase B (round-2-contract.md 1.1, D5): the grammar gains an optional trailing ", pending" (group 4). A pending
# name with no owned prefab is "OK NAMES ... pending (not imported yet)"; an owned one runs the same type / icon checks
# (a wrong type still FAILs: the type is half the save identity) and then WARNs "pending name exists" so the import
# commit flips it (drop ", pending" and set the row's Expected = true in SkiCatalog.cs; LiftBatch CT-13 pins the pair).
$catalog = Join-Path $PSScriptRoot "..\mod\SkiResortMod\Systems\SkiCatalog.cs"
if (-not (Test-Path -LiteralPath $catalog)) {
    $rows.Add("FAIL  NAMES     SkiCatalog.cs not found: $catalog"); $failures++
}
else {
    $catLines = [IO.File]::ReadAllLines((Resolve-Path -LiteralPath $catalog).ProviderPath)
    $begin = -1; $end = -1; $nBegin = 0; $nEnd = 0
    for ($i = 0; $i -lt $catLines.Count; $i++) {
        $t = $catLines[$i].Trim()
        if ($t -ceq "// NAMES-BEGIN") { $begin = $i; $nBegin++ }
        elseif ($t -ceq "// NAMES-END") { $end = $i; $nEnd++ }
    }
    $catNames = New-Object System.Collections.Generic.List[object]
    if ($nBegin -ne 1 -or $nEnd -ne 1 -or $begin -gt $end) {
        $rows.Add("FAIL  NAMES     SkiCatalog.cs: $nBegin NAMES-BEGIN / $nEnd NAMES-END marker(s), want one each in that order (format drift)"); $failures++
    }
    else {
        $lineRe = [regex]'^\s*public const string \w+\s*=\s*"([^"]+)";\s*//\s*(\w+Prefab)(?:,\s*item\s+(\w+))?(?:,\s*(pending))?\s*$'
        for ($i = $begin + 1; $i -lt $end; $i++) {
            $m = $lineRe.Match($catLines[$i])
            if (-not $m.Success) {
                $rows.Add("FAIL  NAMES     SkiCatalog.cs line $($i + 1) does not match the NAMES format (format drift): $($catLines[$i].Trim())"); $failures++
                continue
            }
            $item = ""
            if ($m.Groups[3].Success) { $item = $m.Groups[3].Value }
            $catNames.Add([pscustomobject]@{ Name = $m.Groups[1].Value; Type = $m.Groups[2].Value; Item = $item; Pending = $m.Groups[4].Success })
        }
        if ($catNames.Count -eq 0) { $rows.Add("FAIL  NAMES     SkiCatalog.cs: 0 names between the markers (format drift)"); $failures++ }
    }

    $idRoot = (Resolve-Path -LiteralPath $ImportedData).ProviderPath.TrimEnd('\')
    $rootNameRe = [regex]'(?m)^ {4}"name":\s*"([^"]+)"'
    $rootTypeRe = [regex]'"\$type":\s*"0\|Game\.Prefabs\.(\w+), Game"'
    $iconRe = [regex]'"m_Icon":\s*"assetdb://'
    $roots = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($p in $ownedPrefabs) {
        $text = [IO.File]::ReadAllText($p.FullName)
        $nm = $rootNameRe.Match($text)
        if (-not $nm.Success) { continue }
        $tm = $rootTypeRe.Match($text)
        $type = "?"
        if ($tm.Success) { $type = $tm.Groups[1].Value }
        $rel = $p.FullName
        if ($rel.StartsWith($idRoot + "\", [StringComparison]::OrdinalIgnoreCase)) { $rel = $rel.Substring($idRoot.Length + 1) }
        $key = $nm.Groups[1].Value
        if (-not $roots.ContainsKey($key)) { $roots[$key] = New-Object System.Collections.Generic.List[object] }
        $roots[$key].Add([pscustomobject]@{ Rel = $rel; Type = $type; Icon = $iconRe.IsMatch($text) })
    }

    foreach ($n in $catNames) {
        $label = "$($n.Name) ($($n.Type))"
        if (-not $roots.ContainsKey($n.Name)) {
            if ($n.Pending) { $rows.Add("OK    NAMES     $label pending (not imported yet)"); continue }
            $rows.Add("FAIL  NAMES     ${label}: no owned prefab named '$($n.Name)'"); $failures++
            continue
        }
        $found = $roots[$n.Name]
        if ($found.Count -gt 1) {
            $rows.Add("WARN  NAMES     ${label}: $($found.Count) owned prefabs carry this root name (ambiguous identity): $((($found | ForEach-Object { $_.Rel }) -join ', '))"); $warnings++
        }
        $hit = $null
        foreach ($f in $found) { if ($f.Type -ceq $n.Type) { $hit = $f; break } }
        if ($null -eq $hit) {
            $rows.Add("FAIL  NAMES     ${label}: root type $((($found | ForEach-Object { $_.Type }) -join ', ')) != the catalog's $($n.Type) (the PrefabID type is half the save identity) <- $((($found | ForEach-Object { $_.Rel }) -join ', '))"); $failures++
            continue
        }
        if ($n.Item -ne "" -and -not $hit.Icon) {
            $rows.Add("FAIL  NAMES     ${label}, item $($n.Item): no `"m_Icon`": `"assetdb://...`" in $($hit.Rel) (a build tile without an icon)"); $failures++
            continue
        }
        if ($n.Pending) {
            $rows.Add("WARN  NAMES     ${label}: pending name exists - drop `", pending`" and set Expected = true in SkiCatalog.cs <- $($hit.Rel)"); $warnings++
            continue
        }
        $rows.Add("OK    NAMES     $label <- $($hit.Rel)")
    }
}

# ---- summary -------------------------------------------------------------------------
$rows | ForEach-Object { Write-Output $_ }
Write-Output ""
Write-Output ("preflight: {0} prefabs, {1} cid files checked | {2} FAIL, {3} WARN" -f $ownedPrefabs.Count, $ownedCids.Count, $failures, $warnings)
exit $failures
