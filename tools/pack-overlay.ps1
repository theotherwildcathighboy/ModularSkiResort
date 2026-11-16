# pack-overlay.ps1 - lay a candidate build over the PUBLISHED pack folder, and put the pack back (T198, DECISIONS 664).
#
# Why: a save made on the published pack resolves its prefab ids through the pack (platform id 167215), so an old-save or Player test must run
# the pack folder itself (.cache\Mods\pdx_mods\167215_<n>) with the build under test laid over it. tools\world.ps1 -World Player calls this
# script; run it by hand only when you need one step.
#
#   -Do Status   (default) read-only, no hashing: pack folder and version, whether an overlay is active, its label, whether a .cok was laid.
#   -Do Verify   hashes the pack. With no overlay: every file equals the pristine list. With an overlay: the laid files equal what was laid,
#                every other file equals the pristine list. Exit 1 on MISSING / MISMATCH / EXTRA.
#   -Do Overlay  lays -From (default: the dev mod folder, Mods\SkiResortMod or the aside copy) over the pack. Code, UI bundle, css, images,
#                textures, layouts. Never .pdb, never .cpatch. With -Cok the .cok is laid too (T208: the pack .cok is the shipped assets,
#                so a patch with changed assets is only visible with the new .cok). Each overwritten file is first copied into the
#                originals folder (Documents\CS2-mods-aside\<pack>-originals), the first time only.
#                The first overlay of a pack records the pristine hash list there (_original-hashes.csv): it must run on an untouched pack.
#                An overlay that is already active is restored first, then the new one is laid.
#   -Do Restore  puts every original back, removes the files the overlay added, verifies the hashes (exit 1 on a bad one).
#
#   -From <folder>   the build to lay (needs SkiResortMod.dll). -Cok / -CokPath <file>  also lay a .cok (default: the one .cok in -From).
#   -Label <text>    what Status shows ("r2/x abc123"). Default: branch + short hash of the checkout this script lives in.
#   -WhatIf          dry run: prints WOULD lines and changes nothing (reads and hashes only). Allowed while Cities2 runs.
#   -Json            Status as one JSON line (for tools\world.ps1).
#
# Real runs refuse while Cities2 runs (the game holds the DLLs). Never touches playsets. PowerShell 5.1. Exit: 0 ok, 1 refused / bad hash.
param(
    [ValidateSet('Status', 'Verify', 'Overlay', 'Restore')][string]$Do = 'Status',
    [string]$From,
    [switch]$Cok,
    [string]$CokPath,
    [string]$Label,
    [switch]$WhatIf,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'
$ModId = '167215'
$UserData = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$PdxMods = Join-Path $UserData '.cache\Mods\pdx_mods'
$AsideDir = Join-Path $env:USERPROFILE 'Documents\CS2-mods-aside'
$Repo = Split-Path -Parent $PSScriptRoot
$PackCok = 'ModularSkiResort.cok'

function Get-PackDir {
    $dirs = @(Get-ChildItem $PdxMods -Directory -Filter "$($ModId)_*" -ErrorAction SilentlyContinue |
        Sort-Object { [int](($_.Name -split '_')[1]) } -Descending)
    if ($dirs.Count) { return $dirs[0].FullName }
    return $null
}

# "1.0.1" for pack folder 167215_2: the launcher's cache lists userModVersion per installed version.
function Get-PackVersion([string]$packName) {
    $num = ($packName -split '_')[1]
    try {
        $c = Get-Content (Join-Path $UserData '.cache\Mods\pdx_mods_cache.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($i in $c.items.PSObject.Properties.Value) {
            if ("$($i.id)" -eq $ModId -and "$($i.version)" -eq $num) { return "$($i.userModVersion)" }
        }
    } catch { }
    return "v$num"
}

$Pack = Get-PackDir
if (-not $Pack) { Write-Host "refused: no $ModId pack folder under $PdxMods (subscribe to the mod and start the launcher once)"; exit 1 }
$PackName = Split-Path $Pack -Leaf
$Bk = Join-Path $AsideDir "$PackName-originals"
$CsvPath = Join-Path $Bk '_original-hashes.csv'
$AddedPath = Join-Path $Bk '_added-files.txt'
$StatePath = Join-Path $Bk '_overlay-state.json'

function Get-Rel([string]$full) { $full.Substring($Pack.Length + 1) }
function Get-State { if (Test-Path -LiteralPath $StatePath) { Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null } }
function Mb([long]$n) { '{0:N1} MB' -f ($n / 1MB) }
function Say([string]$text) { if (-not $Json) { Write-Host $text } }

function Get-DefaultLabel {
    try {
        $b = (& git -C $Repo rev-parse --abbrev-ref HEAD) -join ''
        $h = (& git -C $Repo rev-parse --short HEAD) -join ''
        if ($b -and $h) { return "$b $h" }
    } catch { }
    return 'build'
}

function Write-PristineList {
    $rows = foreach ($f in Get-ChildItem -LiteralPath $Pack -Recurse -File -Force) {
        [pscustomobject]@{ rel = (Get-Rel $f.FullName); sha256 = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash; len = $f.Length }
    }
    New-Item -ItemType Directory -Force $Bk | Out-Null
    $rows | Export-Csv -LiteralPath $CsvPath -NoTypeInformation -Encoding UTF8
    return @($rows).Count
}

# Expected hash per relative path: the laid files' own hashes while an overlay is active, the pristine list for the rest.
function Invoke-Verify {
    if (-not (Test-Path -LiteralPath $CsvPath)) { Say "verify: no pristine hash list yet ($CsvPath); the first Overlay records it"; return 0 }
    $csv = @(Import-Csv -LiteralPath $CsvPath)
    $state = Get-State
    $expect = @{}
    foreach ($r in $csv) { $expect[$r.rel] = $r.sha256 }
    $addedOk = @{}
    if ($state) {
        foreach ($l in @($state.laid)) { $expect[$l.rel] = $l.sha256 }
        foreach ($a in @($state.added)) { if ($a) { $addedOk[$a] = $true } }
    }
    $bad = 0
    foreach ($rel in $expect.Keys) {
        $p = Join-Path $Pack $rel
        if (-not (Test-Path -LiteralPath $p)) { Write-Host "MISSING $rel"; $bad++; continue }
        $h = (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash
        if ($h -ne $expect[$rel]) { Write-Host "MISMATCH $rel"; $bad++ }
    }
    $extra = 0
    foreach ($f in Get-ChildItem -LiteralPath $Pack -Recurse -File -Force) {
        $rel = Get-Rel $f.FullName
        if (-not $expect.ContainsKey($rel) -and -not $addedOk.ContainsKey($rel)) { Write-Host "EXTRA $rel"; $extra++ }
    }
    $kind = if ($state) { "overlay '$($state.label)' active" } else { 'no overlay' }
    Write-Host "verify: $($expect.Count) files in list, $bad bad, $extra extra ($kind)"
    return $bad + $extra
}

function Get-Source {
    if (-not $From) {
        $live = Join-Path $UserData 'Mods\SkiResortMod'
        $From = if (Test-Path -LiteralPath $live) { $live } else { Join-Path $AsideDir 'SkiResortMod-dev' }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $From 'SkiResortMod.dll'))) { throw "refused: $From holds no SkiResortMod.dll (-From <folder of the build>)" }
    if ((Get-Item -LiteralPath $From).FullName.TrimEnd('\') -eq (Get-Item -LiteralPath $Pack).FullName.TrimEnd('\')) { throw 'refused: -From is the pack itself' }
    return (Get-Item -LiteralPath $From).FullName.TrimEnd('\')
}

function Invoke-Restore([bool]$dry) {
    $state = Get-State
    if (-not (Test-Path -LiteralPath $CsvPath)) { Write-Host 'restore: nothing to restore (no pristine list, no overlay was ever laid on this pack)'; return 0 }
    $added = if (Test-Path -LiteralPath $AddedPath) { @(Get-Content -LiteralPath $AddedPath | Where-Object { $_ }) } else { @() }
    foreach ($a in $added) {
        $p = Join-Path $Pack $a
        if (Test-Path -LiteralPath $p) { if ($dry) { Write-Host "WOULD remove added file $a" } else { Remove-Item -LiteralPath $p -Force } }
    }
    $n = 0
    foreach ($r in Import-Csv -LiteralPath $CsvPath) {
        $src = Join-Path $Bk $r.rel
        if (Test-Path -LiteralPath $src) {
            $dst = Join-Path $Pack $r.rel
            if ($dry) { Write-Host "WOULD restore $($r.rel)" } else { New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null; Copy-Item -LiteralPath $src -Destination $dst -Force }
            $n++
        }
    }
    if ($dry) { Write-Host "WOULD restore $n file(s) from $Bk, remove $($added.Count) added, delete the overlay state"; return 0 }
    if (Test-Path -LiteralPath $StatePath) { Remove-Item -LiteralPath $StatePath -Force }
    if (Test-Path -LiteralPath $AddedPath) { Remove-Item -LiteralPath $AddedPath -Force }
    Write-Host "restored $n file(s), removed $($added.Count) added$(if ($state) { " (overlay '$($state.label)')" })"
    return (Invoke-Verify)
}

# ---- Status ---------------------------------------------------------------------------------------------------------------
if ($Do -eq 'Status') {
    $state = Get-State
    $ver = Get-PackVersion $PackName
    $intact = $true
    if ($state) {
        foreach ($l in @($state.laid)) {
            $p = Join-Path $Pack $l.rel
            if (-not (Test-Path -LiteralPath $p) -or (Get-Item -LiteralPath $p).Length -ne [long]$l.len) { $intact = $false; break }
        }
    }
    $o = [ordered]@{
        pack = $PackName; version = $ver; overlay = [bool]$state; label = if ($state) { "$($state.label)" } else { '' }
        cok = [bool]($state -and $state.cok); laidAt = if ($state) { "$($state.at)" } else { '' }; sizesMatch = $intact
        pristineList = (Test-Path -LiteralPath $CsvPath)
    }
    if ($Json) { $o | ConvertTo-Json -Compress; exit 0 }
    Write-Host "pack $PackName (v$ver): $(if ($state) { "overlay '$($state.label)' laid $($state.at), $(@($state.laid).Count) file(s), .cok $(if ($state.cok) { 'laid' } else { 'not laid' }), sizes $(if ($intact) { 'match' } else { 'DIFFER (pack changed since)' })" } else { 'no overlay' }); pristine hash list $(if ($o.pristineList) { 'present' } else { 'not recorded yet' })"
    exit 0
}

if ($Do -eq 'Verify') { $n = Invoke-Verify; exit ([int]($n -gt 0)) }

# ---- real runs refuse while the game runs ---------------------------------------------------------------------------------
if (-not $WhatIf -and (Get-Process Cities2 -ErrorAction SilentlyContinue)) { Write-Host 'refused: Cities2 is running. Close the game first.'; exit 1 }
$dry = [bool]$WhatIf
if ($dry) { Write-Host "DRY RUN (-WhatIf): nothing is copied, removed or written" }

if ($Do -eq 'Restore') { $n = Invoke-Restore $dry; exit ([int]($n -gt 0)) }

# ---- Overlay --------------------------------------------------------------------------------------------------------------
try {
    $src = Get-Source
    if (-not $Label) { $Label = Get-DefaultLabel }
    $files = New-Object System.Collections.Generic.List[string]
    foreach ($f in Get-ChildItem -LiteralPath $src -Recurse -File -Force) {
        $rel = $f.FullName.Substring($src.Length + 1)
        if ($f.Extension -eq '.pdb' -or $f.Extension -eq '.cok' -or $rel -like '.cpatch\*') { continue }
        $files.Add($rel)
    }
    $cokSrc = $null
    if ($Cok -or $CokPath) {
        $cokSrc = if ($CokPath) { $CokPath } else {
            $c = @(Get-ChildItem -LiteralPath $src -File | Where-Object { $_.Extension -eq '.cok' })
            if ($c.Count -ne 1) { throw "refused: -Cok needs exactly one .cok at the root of $src (found $($c.Count)); pass -CokPath <file>" }
            $c[0].FullName
        }
        if (-not (Test-Path -LiteralPath $cokSrc)) { throw "refused: .cok not found: $cokSrc" }
        $files.Add($PackCok)   # always laid under the pack's own name
    }
    Write-Host "overlay: $src -> $Pack ($PackName, v$(Get-PackVersion $PackName)), label '$Label', $($files.Count) file(s)$(if ($cokSrc) { ", .cok from $cokSrc" })"

    $state = Get-State
    if ($state) {
        Write-Host "an overlay is active ('$($state.label)'): it is restored first"
        $n = Invoke-Restore $dry
        if ($n -gt 0) { throw 'refused: the pack is not pristine after the restore' }
    }
    if (Test-Path -LiteralPath $CsvPath) {
        $n = Invoke-Verify
        if ($n -gt 0) { throw 'refused: the pack is not pristine (the hash list is in ' + $Bk + ')' }
    } elseif ($dry) {
        $total = (Get-ChildItem -LiteralPath $Pack -Recurse -File -Force | Measure-Object Length -Sum).Sum
        Write-Host "WOULD record the pristine hash list of the pack now ($(Mb $total) hashed) into $CsvPath. It assumes the pack is untouched."
    } else {
        $n = Write-PristineList
        Write-Host "recorded the pristine hash list: $n file(s) -> $CsvPath (assumes the pack is untouched: let the launcher repair it first if unsure)"
    }

    $laid = New-Object System.Collections.Generic.List[object]
    $added = New-Object System.Collections.Generic.List[string]
    $dryBackups = 0; $dryBackupBytes = 0
    foreach ($rel in $files) {
        $from = if ($rel -eq $PackCok -and $cokSrc) { $cokSrc } else { Join-Path $src $rel }
        $dst = Join-Path $Pack $rel
        $exists = Test-Path -LiteralPath $dst
        $len = (Get-Item -LiteralPath $from).Length
        if (-not $exists) { $added.Add($rel) }
        elseif (-not (Test-Path -LiteralPath (Join-Path $Bk $rel))) {
            if ($dry) { $dryBackups++; $dryBackupBytes += (Get-Item -LiteralPath $dst).Length }
            else {
                $b = Join-Path $Bk $rel
                New-Item -ItemType Directory -Force (Split-Path $b) | Out-Null
                Copy-Item -LiteralPath $dst -Destination $b
            }
        }
        if ($dry) { Write-Host "WOULD lay $rel ($(Mb $len))$(if (-not $exists) { ' [new file]' })" }
        else {
            New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
            Copy-Item -LiteralPath $from -Destination $dst -Force
            $laid.Add([pscustomobject]@{ rel = $rel; sha256 = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash; len = $len })
        }
    }
    if ($dry) { Write-Host "WOULD back up $dryBackups original file(s), $(Mb $dryBackupBytes), into $Bk (only those without a backup yet)" }
    if ($dry) { Write-Host "WOULD write the overlay state ($StatePath): label '$Label', $($files.Count) laid, $($added.Count) new, .cok $(if ($cokSrc) { 'yes' } else { 'no' })"; exit 0 }

    $added | Set-Content -LiteralPath $AddedPath -Encoding UTF8
    $stateObj = [ordered]@{ label = $Label; at = (Get-Date).ToString('s'); from = $src; cok = [bool]$cokSrc; laid = $laid.ToArray(); added = $added.ToArray() }
    [IO.File]::WriteAllText($StatePath, ($stateObj | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
    Write-Host "overlaid $($laid.Count) file(s), $($added.Count) new; .cok $(if ($cokSrc) { 'laid' } else { 'not laid (the pack keeps its shipped assets)' })"
    $bad = Invoke-Verify
    exit ([int]($bad -gt 0))
} catch {
    Write-Host $_.Exception.Message; if ($env:SRM_DEBUG) { Write-Host $_.InvocationInfo.PositionMessage }
    exit 1
}
