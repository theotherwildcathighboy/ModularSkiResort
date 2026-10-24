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
#   8. LANES     - every owned NetLaneGeometryPrefab's m_Meshes[].m_Mesh is curve-capable: a known vanilla cable-lane
#                  mesh GUID, or a CID whose mesh prefab carries CurveProperties; the superseded August lanes
#                  (SkiLiftHaulRope / SkiLiftHaulRopeB) must not be referenced by any other prefab (DECISIONS 243;
#                  PIPELINE.md section 6 lift-line invariant). Bug class: a rigid rope rod ~90 degrees across the line.
#   9. IDIOMS    - PIPELINE.md section 6 rules a file check can catch: HOSTED (no PillarObject on SkiLiftTerminalTower*),
#                  SPAWN (no plain spawn marker on a ServiceUpgrade child), WEATHER (no WeatherObject on a spawnable),
#                  NETNULL (no null m_Lane / m_Piece / m_Section), PROP (no gameplay components on scenery props), KEEP (SkiLiftTowerFG2 present), ARRLEN ($rlength /
#                  $plength match the real array counts). Also in LANES: rope-lane m_Hanging <= 0.01 (R31).
#  10. SHIPPED   - every (name, type, CID) of tools/shipped-prefabs.json (tools/freeze-shipped.ps1) is still an owned prefab
#                  with that CID (FAIL otherwise); an owned non-RenderPrefab not in the list WARNs until re-frozen (T74).
#  13. STUBNET   - every station stub marker net is wider than the widest lift line and has no lanes (T121 / T126).
#  14. POWERFLOW - every owned lift line (PowerLinePrefab, not a MarkerNet) carries ElectricityConnection Voltage High (1),
#                  Direction Both (3), Capacity == the vanilla Low-voltage Line's 800000 (DECISIONS 369 / R-1).
#  15. LODGEPARK - every lodge tier and the Welcome Sign carry Game.Prefabs.Park (tools/check-lodge-park.ps1; the stats query
#                  requires Game.Buildings.Park).
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
    "db143cb43e0c43b49b1dbad9e0ebc8de" = "Gravel Pedestrian Lane 1 (T54 CT1 2026-10-16: SkiCatTrack's walking lane)"
    "f4e42245382aea44ca7be75c48162da2" = "Alley Pathfind (T54 CT1: SkiCatTrack pathfind prefab)"
    "d4d220e637b0a2743b798fced877190d" = "Roads service (T54 CT1: SkiCatTrack)"
    "d7ba218ad57ccb741b21620a5f7ee4d3" = "RoadsSmallRoads UI group (T54 CT1 PROBE ONLY: the toolbar entry is a deliberate, temporary exception to DECISIONS 130 until the run lays the net itself)"
    "60c0e17c40a5f1b41bf9d2032670b81d" = "Landscaping>Surfaces UI category"
    "5930db6811522d34495a9e16c4fd4c18" = "Landscaping service"
    "a5749eadf2820a247afc6747989ed959" = "Parks&Rec toolbar menu"
    "8ba97fa363a0af1479f01031029da963" = "LargeSportsParks UI category"
    "5c1f656a5ea20284197a71e4997b4187" = "Parks&Rec service"
    "a1b9ab64e2f648a4da17231062c1e066" = "shared RenderedArea material"
    "0f7db8761506813468a9bb7b91f145be" = "WaterVaporFactorySmall VisualEffectAsset (UnityObjectsMap; T46 2026-10-17: SkiSnowGunPlume's VFX graph; read from the installed 1.6.2 resources.assets UnityObjectsMap, the a1b9ab64 material resolving in the same table as the check; dump-verified: vanilla WaterVaporFactorySmallVFX carries a VFX component with a redacted UnityObjectsMap ref)"
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
    # Fittings v1 (2026-10-17, DECISIONS 263, docs/research/art/fittings-2026-10-17.md): roof vents / kitchen exhausts as rows on the lodges at the next import.
    "e334c9885e132684488e4fa21e06d975" = "ACVentRoofRandom01 placeholder (plumbing vents; dump-verified subobj_bounds.json)"
    "57d3607bc6f3c79408a27d4990e38565" = "CommercialACVentRoofRandom01 placeholder (kitchen exhausts; variants CommercialACVentRoof01-06; dump-verified subobj_bounds.json)"
    # Vanilla SPECIFIC variants (T101 round 3, DECISIONS 324; surgery Y 2026-10-19): one named variant per row instead of a Random
    # placeholder. GUIDs from the installed resources.assets PrefabsMap (tools/vanilla-guid-map.py), bounds in assets/blender/subobj_bounds.json.
    "e73db1817f8e60543897e8c94e92d2ae" = "ACMachineRoof01 (heat pump; SkiSchool back wall at grade; PrefabsMap 2026-10-19)"
    "2ea04b52c3797114cb8e4c281a6c91c3" = "ACMachineRoof02 (rooftop unit; SkiSchool flat front roof; PrefabsMap 2026-10-19)"
    "4a2cdf33f0f230547ace4b40393f8a64" = "ACMachineRoof03 (rooftop unit; SkiSchool flat front roof; PrefabsMap 2026-10-19)"
    "7dc1256672985f840a2e4884a5c4f31a" = "WallLight03 (gooseneck sign lamp; SkiSchool; PrefabsMap 2026-10-19)"
    "1ad78f47f5ed32b4a85ae101abc6d7ae" = "ACVentRoof03 (capped pipe vent; patrol hut / HQ, A-frame, hut lodge; PrefabsMap 2026-10-19)"
    "03b1510ce391829409b4f09a7bffaa49" = "GardenLight03 (deck light; A-frame, hut lodge; PrefabsMap 2026-10-19)"
    "9941294ce11529c42a120ac622d61cbd" = "RoofLight03 (downlight; SkiLodgeGrand porte-cochere; PrefabsMap 2026-10-19)"
    "b916e77fd42bccf448ed1020e15673e4" = "EU_Chimney05 (chimney pot; SkiLodgeSummit, SkiLodgeGrand stacks; PrefabsMap 2026-10-19)"
    "64dc87161400b8b4eab7ef0b1f75fda2" = "WallLight04 (carriage lantern; registered by cs2_fittings VARIANT_GUIDS, placed on no prefab yet; PrefabsMap 2026-10-19)"
    # T51 step 1 (2026-10-16, docs/research/field/2026-10-15/build/T51-markers-step1.md): the two vanilla rows on the A-frame's apron.
    # Both read from the vanilla dump (%TEMP%\pltar\00_BaseGame\...\WaterTower01 Viewing Deck.Prefab: ObjectSubAreas row 0 and the
    # ObjectSubObjects row at (0, 0, 15.5)) and name2guid.json; each is used by hundreds of vanilla prefabs (Hangaround Area: 1963 files).
    "eb0c6cfabb61428468ef54336829d021" = "Hangaround Area (vanilla area prefab, HangaroundArea + NavigationArea Pedestrian; T51 2026-10-16: SkiAFrameDayLodge ObjectSubAreas row; dump-verified WaterTower01 Viewing Deck)"
    "c30226ded1fc8d64abee5d0bd2554bc4" = "Pedestrian Spawn Location (vanilla MarkerObjectPrefab, SpawnLocation ConnectionType Pedestrian; T51 step 2 2026-10-19: SkiSchool x4 door markers - a TOP-LEVEL building only, never a ServiceUpgrade child (Route P, 141/143; the SPAWN gate above checks that); asset-markers-spawners.md section a)"
    "d69b0d6310cb4f5469934fd93238e54b" = "Pedestrian Access Location (vanilla MarkerObjectPrefab, TakeoffLocation types 2/2; T51 2026-10-16: SkiAFrameDayLodge x4 ObjectSubObjects rows; dump-verified Game.Prefabs.MarkerObjectPrefab + WaterTower01 Viewing Deck)"
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

# ---- 8. LANES: every rope/wire lane's mesh must be able to bend (DECISIONS 243, PIPELINE.md section 6) ------
# A lift-line lane is bent into the lane curve by its mesh's CurveProperties (HangingSwaying / SubFlow). The
# August 2026 custom chain (SkiLiftHaulRope / SkiLiftHaulRopeB -> "SkiLiftCable64 Mesh") has none, so a lane that
# meshes with it draws as a rigid 64 m rod ~90 degrees across the line; the S1 round copied HaulRopeB for the DQ,
# gondola and tram lanes and shipped exactly that (T01). The rule, per owned NetLaneGeometryPrefab, for each
# m_Meshes[].m_Mesh: a UnityGUID must be a known vanilla CABLE-LANE MESH ($CableLaneMeshes below, dump-verified to
# carry CurveProperties); a CID must resolve to a mesh prefab under ImportedData whose components include
# CurveProperties. A lane that fails is a FAIL, unless it is one of the retained-but-superseded August lanes
# ($SupersededLanes), which are then only legal while NO other owned prefab (outside the superseded chain
# $SupersededChain) references them by CID. Add a new vanilla lane mesh GUID to $CableLaneMeshes only after
# checking the dump for CurveProperties on it.
$CableLaneMeshes = @{
    "463c051da8a824c4a8f2f830a5011a3e" = "High-voltage Cable Lane Mesh (CurveProperties HangingSwaying+SubFlow; dump-verified, DECISIONS 243)"
    "bbb9724d6daa32241821c52b428c196c" = "Low-voltage Cable Lane Mesh (CurveProperties; dump-verified, DECISIONS 243)"
}
$SupersededLanes = @("SkiLiftHaulRope", "SkiLiftHaulRopeB")
$SupersededChain = @("FixedGripChairLift", "SkiLiftSection", "SkiLiftPiece", "SkiLiftHaulRope", "SkiLiftHaulRopeB")
$laneMeshRe = [regex]'"m_Mesh":\s*(?:\$fstrref:"(CID|UnityGUID|UnityObjectsMap):([0-9a-fA-F]{32})"|null)'
$laneNameRe = [regex]'(?m)^ {4}"name":\s*"([^"]+)"'
$supersededCids = @{}
$laneCount = 0
foreach ($p in $ownedPrefabs) {
    $text = [IO.File]::ReadAllText($p.FullName)
    if ($text -notmatch '"\$type":\s*"0\|Game\.Prefabs\.NetLaneGeometryPrefab, Game"') { continue }
    $laneCount++
    $rel = $p.FullName.Substring($ImportedData.Length + 1)
    $nm = $laneNameRe.Match($text)
    $laneName = $p.BaseName
    if ($nm.Success) { $laneName = $nm.Groups[1].Value }
    $meshRefs = $laneMeshRe.Matches($text)
    $why = New-Object System.Collections.Generic.List[string]
    if ($meshRefs.Count -eq 0) { $why.Add("no m_Meshes[].m_Mesh found") }
    foreach ($m in $meshRefs) {
        if (-not $m.Groups[1].Success) { $why.Add("m_Mesh is null (no mesh at all)"); continue }
        $kind = $m.Groups[1].Value; $val = $m.Groups[2].Value.ToLower()
        if ($kind -eq "UnityGUID") {
            if (-not $CableLaneMeshes.ContainsKey($val)) { $why.Add("UnityGUID mesh $val is not a known vanilla cable-lane mesh (tag it in `$CableLaneMeshes only after the dump shows CurveProperties on it)") }
        }
        elseif ($kind -eq "CID") {
            if (-not $allCidValues.ContainsKey($val)) { $why.Add("mesh CID $val does not resolve") ; continue }
            $meshPath = $allCidValues[$val]
            if ($meshPath.EndsWith(".cid")) { $meshPath = $meshPath.Substring(0, $meshPath.Length - 4) }
            if (-not (Test-Path -LiteralPath $meshPath)) { $why.Add("mesh CID $val has no prefab file beside its .cid"); continue }
            if ([IO.File]::ReadAllText($meshPath) -notmatch 'Game\.Prefabs\.CurveProperties') {
                $why.Add("mesh CID $val ($([IO.Path]::GetFileName($meshPath))) has no CurveProperties - it draws as a rigid rod")
            }
        }
        else { $why.Add("m_Mesh uses $kind (unsupported for a lane mesh)") }
    }
    if ($why.Count -eq 0) {
        # R31 / DECISIONS 243: a long-span rope lane sags 0.004, not the superseded 0.03 (at 250 m the mid-span cabin
        # underside drops 2.0 m). Rope classes = utility type 64 (HV) and 8 (LV); the fence lane (16) is exempt.
        $um = [regex]::Match($text, '"m_UtilityType":\s*(\d+)'); $hm = [regex]::Match($text, '"m_Hanging":\s*([0-9.]+)')
        if ($um.Success -and $hm.Success -and (@("64", "8") -contains $um.Groups[1].Value) -and [double]::Parse($hm.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) -gt 0.01) {
            $rows.Add("FAIL  LANES     ${rel}: rope lane m_Hanging $($hm.Groups[1].Value) > 0.01 (R31: long-span rope lanes use 0.004; 0.03 is the superseded HaulRopeB value)"); $failures++
        }
        else { $rows.Add("OK    LANES     $laneName : curve-capable mesh <- $rel") }
        continue
    }
    if ($SupersededLanes -contains $laneName) {
        $cidFile = $p.FullName + ".cid"
        if (Test-Path -LiteralPath $cidFile) { $supersededCids[([IO.File]::ReadAllText($cidFile).Trim().ToLower())] = $laneName }
        $rows.Add("OK    LANES     $laneName : SUPERSEDED August lane, retained (legal only while nothing outside the superseded chain references it) <- $rel")
        continue
    }
    foreach ($w in $why) { $rows.Add("FAIL  LANES     ${rel}: $w"); $failures++ }
}
if ($supersededCids.Count -gt 0) {
    foreach ($p in $ownedPrefabs) {
        $text = [IO.File]::ReadAllText($p.FullName)
        $nm = $laneNameRe.Match($text)
        $nameHere = $p.BaseName
        if ($nm.Success) { $nameHere = $nm.Groups[1].Value }
        if ($SupersededChain -contains $nameHere) { continue }
        foreach ($k in $supersededCids.Keys) {
            if ($text.ToLower().Contains("cid:$k")) {
                $rows.Add("FAIL  LANES     $($p.FullName.Substring($ImportedData.Length + 1)): references the SUPERSEDED lane $($supersededCids[$k]) (CID $k) - use the vanilla HV lane UnityGUID:5d88ac45a40b30341a79535930e3b735 or a lane whose mesh carries CurveProperties"); $failures++
            }
        }
    }
}
if ($laneCount -eq 0) { $rows.Add("WARN  LANES     no owned NetLaneGeometryPrefab found (check the ownership filter)"); $warnings++ }

# ---- 9. IDIOMS: PIPELINE.md section 6 rules that a file check can catch (lessons that became gates, 2026-10-17) -------
# Each row names the DECISIONS entry that paid for the rule; the idiom text lives in docs/PIPELINE.md section 6.
#   HOSTED   hosted terminal towers (SkiLiftTerminalTower*) never carry PillarObject (entry 8 / PIPELINE section 6): the
#            flag chain is an else-if and PillarData preempts the UtilityObject branch that grants IgnoreSecondaryCollision.
#   SPAWN    a ServiceUpgrade child never references the plain Pedestrian / Restricted Spawn Location markers (entries
#            141 / 143 / 248): the engine pairs the lodge's plain mask-0 markers with the sign's and the sign reads
#            "No pedestrian access". Use Pedestrian Access Location (d69b0d63...) + Hangaround Area (eb0c6cfa...).
#   WEATHER  WeatherObject never rides a SpawnableObject / placeholder-group member (entry 76 trap 1): CheckRequirements
#            returns false and deletes the prop in every season.
#   NETNULL  net prefabs: m_Lane / m_Piece / m_Section (and a NetSubObjects m_Object) are never null - NetInitializeSystem
#            dereferences them unguarded and the game hard-crashes (SPEC 8.3, the Editor "Duplicate" trap).
#   KEEP     owned prefabs nothing references but that the engine reaches through a back-reference must stay: the
#            SkiLiftTowerFG2 object prefab in the Editor library is the 8 m tower family member (entry 173, "the FG2 trap");
#            deleting it leaves RESOLVE green and buries the towers 4 m.
#   PROP     scenery props (SkiSnowGun*, SkiPisteFloodlight*, SkiHydrantBox*, SkiBeacon*) carry none of the gameplay/network
#            components (prop-integration-plan.md section 4: UtilityObject, PillarObject, ObjectSubLanes/Nets, UI/service/economy ...).
#   ARRLEN   Odin arrays: "$rlength" / "$plength" equals the real element count of "$rcontent" / "$pcontent"
#            (T51 / entry 248: validate_prefab.ps1 never checked it). Hand-made prefabs only - the Editor's own
#            RenderPrefab "Mesh" files are skipped (they are not strict JSON and are never hand-edited).
Add-Type -AssemblyName System.Web.Extensions
$jsonSer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$jsonSer.MaxJsonLength = [int]::MaxValue; $jsonSer.RecursionLimit = 4000
$script:arrBad = New-Object System.Collections.Generic.List[string]
function Test-OdinArrays($node, [string]$where) {
    if ($node -is [System.Collections.IDictionary]) {
        foreach ($pair in @(@('$rlength', '$rcontent'), @('$plength', '$pcontent'))) {
            if ($node.ContainsKey($pair[0])) {
                $content = $node[$pair[1]]
                $actual = -1
                if ($null -ne $content) { $actual = @($content).Count }
                if ($actual -ne [int]$node[$pair[0]]) { $script:arrBad.Add("$where : $($pair[0]) = $($node[$pair[0]]) but $($pair[1]) holds $actual") }
            }
        }
        foreach ($k in @($node.Keys)) { Test-OdinArrays $node[$k] "$where/$k" }
    }
    elseif (($node -is [System.Collections.IList]) -and -not ($node -is [string])) {
        $i = 0; foreach ($e in $node) { Test-OdinArrays $e "$where[$i]" ; $i++ }
    }
}
$spawnGuids = @("c30226ded1fc8d64abee5d0bd2554bc4", "aecdab70637b3bb40ab53772984876e1")
$idiomRows = 0
$arrSkipped = 0
foreach ($p in $ownedPrefabs) {
    $rel = $p.FullName.Substring($ImportedData.Length + 1)
    $text = [IO.File]::ReadAllText($p.FullName)
    $isRender = $text -match '"\$type":\s*"0\|Game\.Prefabs\.RenderPrefab'
    $nm = [regex]::Match($text, '(?m)^ {4}"name":\s*"([^"]+)"')
    $rootName = $p.BaseName
    if ($nm.Success) { $rootName = $nm.Groups[1].Value }

    if (($rootName -like "SkiLiftTerminalTower*") -and ($text -match 'Game\.Prefabs\.PillarObject')) {
        $rows.Add("FAIL  HOSTED    ${rel}: a hosted terminal tower carries PillarObject (UtilityObject + StandingObject ONLY)"); $failures++
    }
    # PROP: scenery props (snow guns, floodlights, hydrants, beacons) carry NONE of the gameplay / network components
    # (prop-integration-plan.md section 4: a decorative prop on a run border must never plant lanes, bill, place or light).
    if (($rootName -match '^Ski(SnowGun|PisteFloodlight|HydrantBox|Beacon)') -and ($text -match '"\$type":\s*"0\|Game\.Prefabs\.StaticObjectPrefab, Game"')) {
        $forbidden = [regex]::Matches($text, 'Game\.Prefabs\.(UtilityObject|PillarObject|ObjectSubLanes|ObjectSubNets|PlaceableObject|UIObject|Unlockable|ServiceObject|CityServiceBuilding|ServiceUpkeepItem|Workplace|ServiceConsumption|Attraction|LeisureProvider|ServiceCoverage|BuildingTerraformOverride|StreetLightObject|WeatherObject|PlaceholderObject)\b') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
        if ($forbidden) { $rows.Add("FAIL  PROP       ${rel}: scenery prop carries $($forbidden -join ', ') (prop-integration-plan.md section 4; PIPELINE section 6)"); $failures++ }
        # T46 (2026-10-17, snow-gun-plume-2026-10-17.md section 3): a prop VFX is gated by OUR EffectPrefab (Disaster + a lease),
        # never by a vanilla Operational-gated VFX: on a prop that one is always on, and the only per-instance switch for
        # Operational (Game.Buildings.Extension) is a Burst crash class on props (BuildingLotRenderSystem).
        $opVfx = [regex]::Matches($text, 'UnityGUID:(9c2a3524858d71648877eb2e25375199|1c3a42b06b7d07a478704b8ad0ad7a4e|99d95c6abfdb5114180ed7f75837c364|30b9f1f56fc96274bb7c0c76ca80df30|cd4b962b7ecd94a4a860b6686b251ba2|02a8e7da8dbccc04fb2dc98d62260cc2)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
        if ($opVfx) { $rows.Add("FAIL  PROP       ${rel}: scenery prop references a vanilla Operational-gated VFX ($($opVfx -join ', ')): always on, use SkiSnowGunPlume (PIPELINE section 6 Plumes)"); $failures++ }
    }
    if ($text -match 'Game\.Prefabs\.ServiceUpgrade, Game') {
        foreach ($g in $spawnGuids) {
            if ($text.ToLower().Contains($g)) { $rows.Add("FAIL  SPAWN     ${rel}: a ServiceUpgrade child references the plain spawn marker $g (entries 141/143/248: 'No pedestrian access'); use Pedestrian Access Location + Hangaround Area"); $failures++ }
        }
    }
    if (($text -match 'Game\.Prefabs\.WeatherObject') -and ($text -match 'Game\.Prefabs\.(SpawnableObject|PlaceholderObject)')) {
        $rows.Add("FAIL  WEATHER   ${rel}: WeatherObject on a spawnable / placeholder-group member (entry 76: the prop is deleted in every season)"); $failures++
    }
    foreach ($m in [regex]::Matches($text, '"(m_Lane|m_Piece|m_Section)":\s*null')) {
        $rows.Add("FAIL  NETNULL   ${rel}: $($m.Groups[1].Value) is null (NetInitializeSystem dereferences it unguarded: hard crash, SPEC 8.3)"); $failures++
    }
    if (($text -match 'Game\.Prefabs\.NetSubObjects') -and ($text -match '"m_Object":\s*null')) {
        $rows.Add("FAIL  NETNULL   ${rel}: a NetSubObjects row has m_Object null (hard crash, SPEC 8.3)"); $failures++
    }
    if (-not $isRender) {
        $idiomRows++
        $parsed = $null
        try { $parsed = $jsonSer.DeserializeObject(($text -replace '\$fstrref:("[^"]*")', '$1')) }
        catch {
            # Odin writes a UnityEngine.Color as bare numbers after its "$type" line (not strict JSON): those few files
            # are skipped and counted; any other parse failure is a real malformed prefab.
            if ($text -match '"\$type":\s*"\d+\|UnityEngine\.Color,') { $arrSkipped++ }
            else { $rows.Add("FAIL  ARRLEN     ${rel}: does not parse as JSON once `$fstrref is quoted ($($_.Exception.Message.Split([char]10)[0]))"); $failures++ }
        }
        if ($null -ne $parsed) {
            $script:arrBad.Clear()
            Test-OdinArrays $parsed ""
            foreach ($b in $script:arrBad) { $rows.Add("FAIL  ARRLEN     ${rel}: $b"); $failures++ }
        }
    }
}
if ($lib -and (Test-Path $lib)) {
    $fg2 = @(Get-ChildItem (Join-Path $lib "SkiLiftTowerFG2") -Filter "SkiLiftTowerFG2_*.Prefab" -File -ErrorAction SilentlyContinue)
    if ($fg2.Count -eq 0) { $rows.Add("FAIL  KEEP       c030ee3ff256df91\SkiLiftTowerFG2\SkiLiftTowerFG2_*.Prefab is missing (the 8 m FG tower family member; entry 173 'the FG2 trap')"); $failures++ }
    else { $rows.Add("OK    KEEP      SkiLiftTowerFG2 object prefab present") }
}
$rows.Add("OK    IDIOMS    $idiomRows hand-made prefab(s) scanned (HOSTED, SPAWN, WEATHER, NETNULL, PROP, ARRLEN; $arrSkipped skipped by ARRLEN: Odin Color literals)")

# ---- 10. SHIPPED: the frozen shipped-prefab list (T74, 2026-10-19) -------------------------------------------------------
# tools/shipped-prefabs.json (written by tools/freeze-shipped.ps1 from gamedata-mirror; its header has the rules) lists every
# owned non-RenderPrefab as (name, type, cid). A save names a prefab by (type, name, hash) and our other prefabs by CID
# (Game.Prefabs/PrefabID.cs:45-52; constraints doc section 0 mechanisms 1-4), so for every listed entry the live install
# must still hold an owned prefab with that root name and type whose .Prefab.cid is unchanged: else FAIL (section 3 rule 1:
# never rename / retype / re-CID; rule 4: never delete). An owned prefab NOT in the list is a WARN until the list is
# regenerated (tools/sync-gamedata.ps1, then tools/freeze-shipped.ps1). Release day: tools/freeze-shipped.ps1 -Release x.y.z.
$shippedPath = Join-Path $PSScriptRoot "shipped-prefabs.json"
if (-not (Test-Path -LiteralPath $shippedPath)) {
    $rows.Add("FAIL  SHIPPED   tools/shipped-prefabs.json is missing (run tools/freeze-shipped.ps1)"); $failures++
}
else {
    $shipped = Get-Content -LiteralPath $shippedPath -Raw | ConvertFrom-Json
    $shIdRoot = (Resolve-Path -LiteralPath $ImportedData).ProviderPath.TrimEnd('\')
    $shNameRe = [regex]'(?m)^ {4}"name":\s*"([^"]+)"'
    $shTypeRe = [regex]'"\$type":\s*"0\|Game\.Prefabs\.(\w+), Game"'
    $live = @{}
    foreach ($p in $ownedPrefabs) {
        $text = [IO.File]::ReadAllText($p.FullName)
        $tm = $shTypeRe.Match($text); $nm = $shNameRe.Match($text)
        if (-not $tm.Success -or -not $nm.Success -or $tm.Groups[1].Value -ceq "RenderPrefab") { continue }
        $cid = ""
        if (Test-Path -LiteralPath ($p.FullName + ".cid")) { $cid = ([IO.File]::ReadAllText($p.FullName + ".cid")).Trim().ToLower() }
        $k = "$($tm.Groups[1].Value):$($nm.Groups[1].Value)"
        if (-not $live.ContainsKey($k)) { $live[$k] = New-Object System.Collections.Generic.List[object] }
        $live[$k].Add([pscustomobject]@{ Cid = $cid; Rel = $p.FullName.Substring($shIdRoot.Length + 1) })
    }
    $frozen = @{}
    $shOk = 0
    foreach ($e in $shipped.prefabs) {
        $k = "$($e.type):$($e.name)"; $frozen[$k] = $true
        if (-not $live.ContainsKey($k)) {
            $rows.Add("FAIL  SHIPPED   $k (cid $($e.cid), was $($e.file)) is gone: a shipped prefab was renamed, retyped or deleted (constraints section 3 rules 1 / 4; ObsoleteIdentifiers if unavoidable)"); $failures++
            continue
        }
        if (-not ($live[$k] | Where-Object { $_.Cid -eq $e.cid })) {
            $rows.Add("FAIL  SHIPPED   ${k}: .Prefab.cid $((($live[$k] | ForEach-Object { $_.Cid }) -join ', ')) != the frozen $($e.cid) (rule 1 / row 7a: keep the old file's CID) <- $((($live[$k] | ForEach-Object { $_.Rel }) -join ', '))"); $failures++
            continue
        }
        $shOk++
    }
    foreach ($k in ($live.Keys | Sort-Object)) {
        if (-not $frozen.ContainsKey($k)) {
            $rows.Add("WARN  SHIPPED   $k not in tools/shipped-prefabs.json <- $((($live[$k] | ForEach-Object { $_.Rel }) -join ', ')) (new prefab: tools/sync-gamedata.ps1, then tools/freeze-shipped.ps1)"); $warnings++
        }
    }
    $relState = if ($shipped.released) { "released $($shipped.released)" } else { "provisional, not released" }
    $rows.Add("OK    SHIPPED   $shOk of $(@($shipped.prefabs).Count) frozen prefab(s) present with their CID ($relState; list of $($shipped.generated))")
}

# ---- 11. EMISSIVE: no BuildingActive (72) light on an owned mesh prefab (DECISIONS 336) ------------
# EmissiveProperties.Purpose 72 = BuildingActive: ObjectInterpolateSystem.cs:1194-1198 drives it to 0 unless the entity
# is a Building with BuildingOption.Inactive clear - dark on every ServiceUpgrade child and on stations. The engine's own
# importer gives an emissive map 34 = DecorativeLight @ 5 (AssetImportPipeline.cs:2259), which only needs electricity.
# 71 = EffectSource (the beacon lens, driven by the effect) is allowed.
# BRIGHTNESS (T119): 33 / 34 have NO day / night gate on a static object (ObjectInterpolateSystem.cs:1089-1096; the night
# factor reaches only colossal_BuildingState, the `_Win` path, BatchDataSystem.cs:643-699 + :2644-2648), so a building mesh
# glows by day at its intensity. Vanilla DecorativeLight meshes (dump scan 2026-10-20, field/2026-10-20/lights-round-2.md):
# 44 of 64 at 0.05, 54 of 64 within 0.02-0.5; >= 1 only on colored accents (parking, airport, carousel) and garage signs (5,
# the importer's default). A 33 / 34 light above 1 on a mesh a BuildingPrefab points at WARNs.
# Only mesh prefabs a hand-made prefab points at today: superseded letters stay on disk (never delete, rule 4) and are not judged.
$emRefd = @{}
$emBldg = @{}
foreach ($p in $ownedPrefabs) {
    if ($p.Name -match ' Mesh_[0-9a-f]+\.Prefab$') { continue }
    try { $t0 = [IO.File]::ReadAllText($p.FullName) } catch { continue }
    $isBldg = $t0 -match '^\s*\{\s*"\$id": \d+,\s*"\$type": "\d+\|Game\.Prefabs\.BuildingPrefab,'
    foreach ($m in [regex]::Matches($t0, '"m_Mesh": \$fstrref:"CID:([0-9a-f]{32})"')) {
        $emRefd[$m.Groups[1].Value] = $true
        if ($isBldg) { $emBldg[$m.Groups[1].Value] = $true }
    }
}
$emBright = 0
$emOk = 0
foreach ($p in $ownedPrefabs) {
    if ($p.Name -notmatch ' Mesh_[0-9a-f]+\.Prefab$') { continue }
    $cidFile = $p.FullName + ".cid"
    if (-not (Test-Path $cidFile)) { continue }
    try { $cidVal = ([IO.File]::ReadAllText($cidFile)).Trim().ToLower() } catch { continue }
    if (-not $emRefd.ContainsKey($cidVal)) { continue }
    try { $txt = [IO.File]::ReadAllText($p.FullName) } catch { continue }
    if ($txt -notmatch '"purpose": ') { continue }
    $bad = [regex]::Matches($txt, '"purpose": (\d+)') | Where-Object { $_.Groups[1].Value -eq '72' }
    $hot = @()
    if ($emBldg.ContainsKey($cidVal)) {
        foreach ($lm in [regex]::Matches($txt, '"purpose": (\d+),.*?"intensity": ([-0-9.eE]+)', 'Singleline')) {
            $pv = $lm.Groups[1].Value; $iv = [double]::Parse($lm.Groups[2].Value, [Globalization.CultureInfo]::InvariantCulture)
            if (($pv -eq '33' -or $pv -eq '34') -and $iv -gt 1) { $hot += "$pv @ $iv" }
        }
    }
    if ($bad.Count -gt 0) {
        $rows.Add("WARN  EMISSIVE  $($p.Name): EmissiveProperties purpose 72 (BuildingActive) never lights a ServiceUpgrade child or a station - use 34 (DecorativeLight) at a vanilla building intensity (0.05-0.5), DECISIONS 336 / T119"); $warnings++
    } elseif ($hot.Count -gt 0) {
        $rows.Add("WARN  EMISSIVE  $($p.Name): building mesh light $($hot -join ', ') - 33 / 34 glow by day too (no night gate on a static object); vanilla building lights run 0.02-0.5 (0.05 typical), the importer's 5 is for small signs; ours 0.1 (T119)"); $warnings++
    } else { $emOk++; if ($emBldg.ContainsKey($cidVal)) { $emBright++ } }
}
$rows.Add("OK    EMISSIVE  $emOk owned mesh prefab(s) with an emissive block, none on purpose 72; $emBright building mesh(es) at <= 1")

# ---- 12. USENGLISH: US spelling in every player-facing text (T130, 2026-10-20; DECISIONS 350) --------------------------------
# tools/check-us-english.js (word list inside) scans the locale sources, the hub's locale tuples, the mod's log calls and the
# docs-site prose, and (T130 part 2) the comments of every source file and the prose of docs/; any British spelling is a FAIL. Its --self-test proves the word list still catches its fixtures.
$usJs = Join-Path $PSScriptRoot "check-us-english.js"
$usOut = @(& node $usJs 2>&1)
$usExit = $LASTEXITCODE
$usSelf = @(& node $usJs --self-test 2>&1)
$usSelfExit = $LASTEXITCODE
if ($usExit -ne 0) {
    foreach ($l in $usOut) { if ("$l" -match '^\S+:\d+: ') { $rows.Add("FAIL  USENGLISH $l"); $failures++ } }
    if (-not ($usOut | Where-Object { "$_" -match '^\S+:\d+: ' })) { $rows.Add("FAIL  USENGLISH check-us-english.js exit $usExit : $($usOut -join ' ')"); $failures++ }
} elseif ($usSelfExit -ne 0) {
    $rows.Add("FAIL  USENGLISH check-us-english.js --self-test: $($usSelf -join ' ')"); $failures++
} else {
    $rows.Add("OK    USENGLISH $(($usOut | Select-Object -Last 1)) | $(($usSelf | Select-Object -Last 1))")
}

# ---- 12b. USERPATHS: no absolute C:\Users paths in tracked or new files (release blocker 2026-10-22; DECISIONS 377) -----
# Generators (cs2_bldg.py, welcome_sign.py, lodge.py, cs2_review.write_pack) still write absolute paths into new manifests:
# this gate stops them reaching a commit. Use repo-relative paths or %USERPROFILE% / %TEMP% placeholders.
$upOut = @(& node (Join-Path $PSScriptRoot "check-no-user-paths.js") 2>&1)
if ($LASTEXITCODE -ne 0) {
    foreach ($l in $upOut) { if ("$l" -match '^\S+:\d+: ') { $rows.Add("FAIL  USERPATHS $l"); $failures++ } }
    if (-not ($upOut | Where-Object { "$_" -match '^\S+:\d+: ' })) { $rows.Add("FAIL  USERPATHS $($upOut -join ' ')"); $failures++ }
} else {
    $rows.Add("OK    USERPATHS $(($upOut | Select-Object -Last 1))")
}

# ---- 12c. LOCALE: locale/en-US.json (the translation template) matches the English dictionaries -------------------------------
# tools/export-locale.js --check regenerates the file in memory from LocaleEN.cs and Settings/LocaleEN.cs and compares it with the
# one on disk; it also fails on a duplicate key, an unreadable value or a hub text that differs from hubText.ts.
$locOut = @(& node (Join-Path $PSScriptRoot "export-locale.js") --check 2>&1)
if ($LASTEXITCODE -ne 0) {
    $rows.Add("FAIL  LOCALE $(($locOut | Select-Object -Last 1))"); $failures++
} else {
    $rows.Add("OK    LOCALE $(($locOut | Select-Object -Last 1))")
}

# ---- 13. STUBNET: station stub marker nets (T121 / T126, 2026-10-20; research/art/station-stubs-2026-10-20.md) -----------
# A shared node takes the net prefab with the highest NetData.m_NodePriority (GenerateNodesSystem.cs:1690-1765, strict >),
# which is the composition width (NetInitializeSystem.cs:135): a stub marker no wider than a line hands its tip node to the
# line and the engine stands the line's pillar there (T126). A stub piece with lanes draws them as floating rope pieces
# wherever the station has no anchor sub-lane of that same lane prefab (LaneSystem.cs:4049-4070 matches by prefab; T121).
# FAIL: an owned MarkerNet power line whose width is <= the widest owned lift line, or whose pieces carry NetPieceLanes.
$snByCid = @{}
foreach ($p in $ownedPrefabs) {
    $c = "$($p.FullName).cid"
    if (Test-Path $c) { $snByCid[([IO.File]::ReadAllText($c)).Trim().ToLower()] = $p.FullName }
}
function Get-StubNetInfo([string]$netText) {
    $w = 0.0; $lanes = $false
    foreach ($sm in [regex]::Matches($netText, '"m_Section":\s*\$fstrref:"CID:([0-9a-f]{32})"')) {
        $sf = $snByCid[$sm.Groups[1].Value]; if (-not $sf) { continue }
        foreach ($pm in [regex]::Matches([IO.File]::ReadAllText($sf), '"m_Piece":\s*\$fstrref:"CID:([0-9a-f]{32})"')) {
            $pf = $snByCid[$pm.Groups[1].Value]; if (-not $pf) { continue }
            $pt = [IO.File]::ReadAllText($pf)
            $wm = [regex]::Match($pt, '(?m)^    "m_Width":\s*([0-9.]+)')
            if ($wm.Success) { $w += [double]$wm.Groups[1].Value }
            if ($pt -match 'Game\.Prefabs\.NetPieceLanes,') { $lanes = $true }
        }
    }
    return @{ Width = $w; Lanes = $lanes }
}
$snLines = @{}; $snMarkers = @{}
foreach ($p in $ownedPrefabs) {
    $t = [IO.File]::ReadAllText($p.FullName)
    if ($t -notmatch '^\{\s*"\$id":\s*0,\s*"\$type":\s*"0\|Game\.Prefabs\.PowerLinePrefab,') { continue }
    $nm = [regex]::Match($t, '(?m)^    "name":\s*"([^"]+)"').Groups[1].Value
    if ($t -match 'Game\.Prefabs\.MarkerNet,') { $snMarkers[$nm] = Get-StubNetInfo $t } else { $snLines[$nm] = Get-StubNetInfo $t }
}
$snMax = 0.0; $snMaxName = "-"
foreach ($k in $snLines.Keys) { if ($snLines[$k].Width -gt $snMax) { $snMax = $snLines[$k].Width; $snMaxName = $k } }
foreach ($k in ($snMarkers.Keys | Sort-Object)) {
    $m = $snMarkers[$k]
    if ($m.Width -le $snMax) { $rows.Add("FAIL  STUBNET   ${k}: width $($m.Width) <= $snMaxName $snMax - the stub tip node takes the line prefab and grows a pillar (T126; strict > in FindNodePrefab)"); $failures++ }
    elseif ($m.Lanes) { $rows.Add("FAIL  STUBNET   ${k}: its piece carries NetPieceLanes - unanchored stub lanes float beyond the station nose (T121)"); $failures++ }
    else { $rows.Add("OK    STUBNET   ${k}: width $($m.Width) > widest line $snMaxName $snMax, no lanes") }
}
if ($snMarkers.Count -eq 0) { $rows.Add("WARN  STUBNET   no owned MarkerNet power line found (check the ownership filter)"); $warnings++ }

# ---- 14. POWERFLOW: the lift lines' electricity connection (DECISIONS 369 / R-1, 2026-10-22) -------------------------------
# Lifts carry power up the mountain on purpose, at the vanilla Low-voltage Line's rating (dump 00_BaseGame/Game.Prefabs.PowerLinePrefab/
# Low-voltage Line: m_Voltage 0, m_Direction 3, m_Capacity 800000; the High-voltage Line is 4000000). Voltage stays High (1): High is
# what makes the lines snap to our station stubs (NetInitializeSystem.cs:2416-2425, layer PowerlineHigh). Capacity is the edge capacity
# of the city flow graph (ElectricityEdgeGraphSystem.cs:77-79; Simulation.Flow/Edge.cs:28-43), re-read from the prefab on every load
# (Serialization/ElectricityGraphSystem.cs:68-77,:99-121); a prefab with NO ElectricityConnection gets 400000 / Both (:70-71).
# Demand through a line is tiny (tram base 1213 W wanted, top stations 0, lodges 2500-3500), so the cap never binds. FAIL on any drift:
# a 4000000 here would make a lift a vanilla HV trunk, a 0 would cut the power-up-the-mountain feature, a missing component falls to 400000.
$pfWant = @{ Voltage = 1; Direction = 3; Capacity = 800000 }
$pfSeen = 0
foreach ($p in $ownedPrefabs) {
    $t = [IO.File]::ReadAllText($p.FullName)
    if ($t -notmatch '^\{\s*"\$id":\s*0,\s*"\$type":\s*"0\|Game\.Prefabs\.PowerLinePrefab,') { continue }
    if ($t -match 'Game\.Prefabs\.MarkerNet,') { continue }
    $nm = [regex]::Match($t, '(?m)^    "name":\s*"([^"]+)"').Groups[1].Value
    $m = [regex]::Match($t, '"\$type":\s*"\d+\|Game\.Prefabs\.ElectricityConnection, Game",\s*"name":\s*"ElectricityConnection",\s*"active":\s*true,\s*"m_Voltage":\s*(\d+),\s*"m_Direction":\s*(\d+),\s*"m_Capacity":\s*(\d+)')
    $pfSeen++
    if (-not $m.Success) { $rows.Add("FAIL  POWERFLOW  ${nm}: no ElectricityConnection block in the expected shape (a missing component loads as 400000 / Both, Serialization/ElectricityGraphSystem.cs:70-71)"); $failures++; continue }
    $v = [int]$m.Groups[1].Value; $d = [int]$m.Groups[2].Value; $c = [int]$m.Groups[3].Value
    if ($v -ne $pfWant.Voltage -or $d -ne $pfWant.Direction -or $c -ne $pfWant.Capacity) {
        $rows.Add("FAIL  POWERFLOW  ${nm}: Voltage $v / Direction $d / Capacity $c, want $($pfWant.Voltage) / $($pfWant.Direction) / $($pfWant.Capacity) (High snaps to our stubs; Both; the vanilla Low-voltage Line's 800000; DECISIONS 369)"); $failures++
    } else { $rows.Add("OK    POWERFLOW  ${nm}: High / Both / $c (the vanilla LV rating)") }
}
if ($pfSeen -eq 0) { $rows.Add("WARN  POWERFLOW  no owned lift line PowerLinePrefab found (check the ownership filter)"); $warnings++ }

# ---- 15. LODGEPARK: every lodge tier and the Welcome Sign carry Game.Prefabs.Park ----------------------------------------------
# SkiResortStatsSystem's building query requires Game.Buildings.Park, so a lodge or sign prefab without Park is never counted.
# tools/check-lodge-park.ps1 reads the names from SkiCatalog.cs and checks the mirror's prefab JSON; any FAIL line it prints is a FAIL.
$lpOut = @(& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "check-lodge-park.ps1") 2>&1)
if ($LASTEXITCODE -ne 0) {
    $lpFails = @($lpOut | Where-Object { "$_" -match '^FAIL' })
    foreach ($l in $lpFails) { $rows.Add("FAIL  LODGEPARK $l"); $failures++ }
    if ($lpFails.Count -eq 0) { $rows.Add("FAIL  LODGEPARK check-lodge-park.ps1 exit $LASTEXITCODE : $($lpOut -join ' ')"); $failures++ }
} else {
    $rows.Add("OK    LODGEPARK $(($lpOut | Select-Object -Last 1))")
}

# ---- summary -------------------------------------------------------------------------
$rows | ForEach-Object { Write-Output $_ }
Write-Output ""
Write-Output ("preflight: {0} prefabs, {1} cid files checked | {2} FAIL, {3} WARN" -f $ownedPrefabs.Count, $ownedCids.Count, $failures, $warnings)
exit $failures
