# freeze-shipped.ps1 - writes tools/shipped-prefabs.json, the frozen list of every hand-made prefab the game loads that a
# save or another prefab can name: (name, type, CID). T74, 2026-10-19; docs/research/release-update-constraints-2026-10-18.md
# section 0 mechanisms 1-4, section 3 rule 1 / 4, section 4 item 6; scaffold note docs/research/release-scaffolds-2026-10-19.md.
#
# WHAT IS LISTED. The owned prefabs under -Source (default gamedata-mirror\ImportedData, the repo copy of what the game
# loads; tools/sync-gamedata.ps1 refreshes it): directories named Ski* (plus legacy FixedGripChairLift), and Ski* files /
# directories inside the Editor library c030ee3ff256df91 - the SAME ownership rule as tools/preflight.ps1 ("collect owned
# files"). Every such prefab is listed EXCEPT RenderPrefab: a mesh prefab is not in saves (mechanism 1; Game.Serialization
# Primary/SecondaryPrefabReferencesSystem only write instance-referenced prefabs) and rule 2 lets a patch replace meshes freely.
#   name = the root "name" (4-space indent), type = the root "$type" Game.Prefabs class (PrefabID = type + name + hash,
#   Game.Prefabs/PrefabID.cs:45-52), cid = the .Prefab.cid beside it (other prefabs reference it as CID:...; rule 7a).
#
# THE GATE. tools/preflight.ps1 SHIPPED compares the LIVE ImportedData with this list: a listed (name, type) that disappears
# or whose CID changes is a FAIL (rule 1: never rename / retype / re-CID a shipped prefab; rule 4: never delete one); an
# owned prefab not in the list is a WARN until the list is regenerated with this script.
#
# RELEASE. Before the first upload the list is "provisional": regenerating it after an intended rename / removal is allowed
# (no save compatibility before release). On release day run
#     tools\freeze-shipped.ps1 -Release 1.0.0
# which writes "released": "1.0.0". From then on this script REFUSES to drop or change any listed entry (exit 1 with the
# list): a regenerated list can only ADD prefabs, so a re-freeze can never launder a removal past the SHIPPED gate. A
# deliberate break of that rule needs a hand edit of the JSON and a changelog line ("Save compatible with previous: no").
#
# Usage:  tools\freeze-shipped.ps1 [-Source <ImportedData root>] [-Release <x.y.z>] [-DryRun]
# Exit:   0 written (or dry run); 1 refused (released list would lose / change entries) or bad input.

param(
    [string]$Source = (Join-Path $PSScriptRoot "..\gamedata-mirror\ImportedData"),
    [string]$Release = "",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$out = Join-Path $PSScriptRoot "shipped-prefabs.json"
if (-not (Test-Path -LiteralPath $Source)) { Write-Output "FATAL: source not found: $Source"; exit 1 }
$srcRoot = (Resolve-Path -LiteralPath $Source).ProviderPath.TrimEnd('\')

# ---- owned prefabs (preflight.ps1 "collect owned files", same rule) ------------------------------------------------
$owned = New-Object System.Collections.Generic.List[object]
Get-ChildItem $srcRoot -Directory | Where-Object { $_.Name -like "Ski*" -or $_.Name -eq "FixedGripChairLift" } | ForEach-Object {
    $owned.AddRange(@(Get-ChildItem $_.FullName -Recurse -Filter *.Prefab -File))
}
$lib = Join-Path $srcRoot "c030ee3ff256df91"
if (Test-Path $lib) {
    Get-ChildItem $lib -Recurse -File -Filter *.Prefab | Where-Object { $_.Name -like "Ski*" } | ForEach-Object { $owned.Add($_) }
    Get-ChildItem $lib -Directory | Where-Object { $_.Name -like "Ski*" } | ForEach-Object {
        $owned.AddRange(@(Get-ChildItem $_.FullName -Recurse -Filter *.Prefab -File))
    }
}
$owned = @($owned | Sort-Object FullName -Unique)

$rootNameRe = [regex]'(?m)^ {4}"name":\s*"([^"]+)"'
$rootTypeRe = [regex]'"\$type":\s*"0\|Game\.Prefabs\.(\w+), Game"'
$entries = New-Object System.Collections.Generic.List[object]
$problems = New-Object System.Collections.Generic.List[string]
foreach ($p in $owned) {
    $text = [IO.File]::ReadAllText($p.FullName)
    $tm = $rootTypeRe.Match($text)
    $rel = $p.FullName.Substring($srcRoot.Length + 1)
    if (-not $tm.Success) { $problems.Add("no root Game.Prefabs `$type: $rel"); continue }
    if ($tm.Groups[1].Value -ceq "RenderPrefab") { continue }
    $nm = $rootNameRe.Match($text)
    if (-not $nm.Success) { $problems.Add("no root name: $rel"); continue }
    $cidFile = $p.FullName + ".cid"
    if (-not (Test-Path -LiteralPath $cidFile)) { $problems.Add("no .cid beside: $rel"); continue }
    $cid = ([IO.File]::ReadAllText($cidFile)).Trim().ToLower()
    $entries.Add([pscustomobject]@{ name = $nm.Groups[1].Value; type = $tm.Groups[1].Value; cid = $cid; file = $rel.Replace('\', '/') })
}
$entries = @($entries | Sort-Object @{ Expression = { $_.name }; Ascending = $true }, @{ Expression = { $_.type }; Ascending = $true })
foreach ($x in $problems) { Write-Output "PROBLEM  $x" }
if ($problems.Count -gt 0) { Write-Output "FATAL: $($problems.Count) owned prefab(s) could not be read; nothing written"; exit 1 }

# duplicate (name, type) = two prefabs with one save identity (PrefabSystem drops the second: "Duplicate prefab ID")
$dups = @($entries | Group-Object { "$($_.type):$($_.name)" } | Where-Object { $_.Count -gt 1 })
foreach ($d in $dups) { Write-Output "PROBLEM  duplicate identity $($d.Name): $((($d.Group | ForEach-Object { $_.file }) -join ', '))" }
if ($dups.Count -gt 0) { Write-Output "FATAL: duplicate (type, name); nothing written"; exit 1 }

# ---- compare with the existing list ----------------------------------------------------------------------------------
$prevReleased = ""
$lost = New-Object System.Collections.Generic.List[string]
$added = 0
if (Test-Path -LiteralPath $out) {
    $prev = Get-Content -LiteralPath $out -Raw | ConvertFrom-Json
    if ($prev.released) { $prevReleased = [string]$prev.released }
    $now = @{}
    foreach ($e in $entries) { $now["$($e.type):$($e.name)"] = $e }
    $old = @{}
    foreach ($e in $prev.prefabs) {
        $k = "$($e.type):$($e.name)"; $old[$k] = $true
        if (-not $now.ContainsKey($k)) { $lost.Add("removed  $k (cid $($e.cid))") }
        elseif ($now[$k].cid -ne $e.cid) { $lost.Add("re-CID   $k $($e.cid) -> $($now[$k].cid)") }
    }
    foreach ($e in $entries) { if (-not $old.ContainsKey("$($e.type):$($e.name)")) { $added++; Write-Output "ADDED    $($e.type):$($e.name) <- $($e.file)" } }
    foreach ($l in $lost) { Write-Output "CHANGED  $l" }
}
$released = $prevReleased
if ($Release -ne "") { $released = $Release }
if ($prevReleased -ne "" -and $lost.Count -gt 0) {
    Write-Output "REFUSED: the list is released ($prevReleased); $($lost.Count) shipped entr(y/ies) would be removed or re-CID'd (constraints section 3 rules 1 and 4). Restore the prefab(s), or alias with ObsoleteIdentifiers (rule 6) and hand-edit the list."
    exit 1
}

# ---- write (stable, one entry per line: reviewable diffs) ------------------------------------------------------------
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append("{`n")
[void]$sb.Append("  `"comment`": `"Frozen shipped prefabs (T74). Generated by tools/freeze-shipped.ps1 from gamedata-mirror/ImportedData; checked by tools/preflight.ps1 SHIPPED. Never hand-edit except to record a deliberate break (see the script header).`",`n")
[void]$sb.Append("  `"generated`": `"$((Get-Date).ToString('yyyy-MM-dd'))`",`n")
$rel = if ($released -eq "") { "null" } else { "`"$released`"" }
[void]$sb.Append("  `"released`": $rel,`n")
[void]$sb.Append("  `"prefabs`": [`n")
for ($i = 0; $i -lt $entries.Count; $i++) {
    $e = $entries[$i]
    $comma = if ($i -lt $entries.Count - 1) { "," } else { "" }
    [void]$sb.Append("    { `"name`": `"$($e.name)`", `"type`": `"$($e.type)`", `"cid`": `"$($e.cid)`", `"file`": `"$($e.file)`" }$comma`n")
}
[void]$sb.Append("  ]`n}`n")

$byType = $entries | Group-Object type | Sort-Object Count -Descending | ForEach-Object { "$($_.Count) $($_.Name)" }
Write-Output ("freeze-shipped: {0} prefab(s) [{1}] | added {2}, removed/changed {3} | released: {4}" -f $entries.Count, ($byType -join ", "), $added, $lost.Count, $(if ($released -eq "") { "no (provisional)" } else { $released }))
if ($DryRun) { Write-Output "dry run: nothing written"; exit 0 }
[IO.File]::WriteAllText($out, $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))
Write-Output "wrote $out"
exit 0
