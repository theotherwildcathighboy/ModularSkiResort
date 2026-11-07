# probe-surfaces.ps1 - read which .Texture each imported .Surface binds (probe F1, fine-detail pass:
# docs/research/art/assets-2026-10/surgery/probes-B.md). Read-only: never writes under ImportedData.
#
#   .\tools\probe-surfaces.ps1                                   # SkiProbeSheet* surfaces + textures
#   .\tools\probe-surfaces.ps1 -Snapshot docs\research\field\2026-10-13\probesB-batch1.json
#                                                                # also save the table. The parent folder
#                                                                # is CREATED if missing (checker fix 2026-10-12)
#   .\tools\probe-surfaces.ps1 -Compare docs\research\field\2026-10-13\probesB-batch1.json   # diff vs a snapshot
#   .\tools\probe-surfaces.ps1 -Pattern SkiSnowGunFanA -Owner SkiSnowGunFanA   # any asset (sanity: LOD1/2 share LOD0)
#   .\tools\probe-surfaces.ps1 -Logs                             # also grep the logs (see below)
#
# Run it from the repo root (the examples are relative to it). A relative -Snapshot / -Compare resolves against the
# PowerShell location, not .NET's process directory. The tool needs no admin rights and never writes under ImportedData.
#
# Format (measured 2026-10-12 on ImportedData\c030ee3ff256df91\SkiSnowGunFanA_*.Surface): a list of
# [1-byte length][ASCII property name][0xFF][16 bytes] entries. The 16 bytes are the texture's CID with the
# two hex digits of every byte swapped (bytes 1a 03 76 5c ... = SkiSnowGunFanA_BaseColor_*.Texture.cid
# a13067c5...). The texture files live FLAT in the seed folder (AssetImportPipeline.cs:2898-2918, FS8),
# named <asset-group texture name>_<hash>.Texture. Labels:
#   an owner-named texture behind a sharer's slot = SHARED
#   a sharer-named texture = COPY
#   no matching .Texture.cid = UNRESOLVED (orphan)
#   no _BaseColorMap = FALLBACK
# -Logs greps Logs\AssetPipeline.log and Player.log for "Using fallback" (log.InfoFormat, :537-545) and SkiProbe lines.
# The first file comes from logger "AssetPipeline", AssetImportPipeline.cs:291. The FILE NAME is inferred from the logger
# name. No such file exists until an Editor session has run. Player.log sits one level ABOVE Logs\:
# ...\Cities Skylines II\Player.log. If AssetPipeline.log is absent, the tool lists the Logs\ files that look related.
# Archive Logs\ AND Player.log before relaunching the game. Logs\ is cleared at every start. Player.log rotates to
# Player-prev.log.

param(
    [string]$Pattern = "SkiProbeSheet",
    [string]$Owner = "SkiProbeSheetB",
    [string]$ImportedData = (Join-Path $env:USERPROFILE "AppData\LocalLow\Colossal Order\Cities Skylines II\ImportedData"),
    [string]$Seed = "c030ee3ff256df91",
    [string]$Snapshot,
    [string]$Compare,
    [switch]$Logs
)

$ErrorActionPreference = "Stop"
$lib = Join-Path $ImportedData $Seed
if (-not (Test-Path $lib)) { Write-Output "FATAL: seed folder not found: $lib"; exit 1 }

function Get-SurfaceSlots([string]$path) {
    $b = [IO.File]::ReadAllBytes($path)
    $slots = [ordered]@{}
    for ($i = 2; $i -lt $b.Length - 16; $i++) {
        if ($b[$i] -ne 0xFF) { continue }
        # walk back to a length byte that spans exactly an ASCII name starting with '_'
        for ($n = 2; $n -le 40 -and $n -lt $i; $n++) {
            $j = $i - $n - 1
            if ($j -lt 0) { break }
            if ($b[$j] -ne $n -or $b[$j + 1] -ne 0x5F) { continue }
            $ok = $true
            for ($k = $j + 1; $k -lt $i; $k++) { if ($b[$k] -lt 0x20 -or $b[$k] -gt 0x7E) { $ok = $false; break } }
            if (-not $ok) { continue }
            $name = [Text.Encoding]::ASCII.GetString($b, $j + 1, $n)
            $sb = New-Object Text.StringBuilder
            for ($k = $i + 1; $k -le $i + 16; $k++) { [void]$sb.Append(('{0:x}{1:x}' -f ($b[$k] -band 0xF), ($b[$k] -shr 4))) }
            $slots[$name] = $sb.ToString()
            break
        }
    }
    return $slots
}

# every texture CID under ImportedData -> file name
$texByCid = @{}
Get-ChildItem $ImportedData -Recurse -Filter *.Texture.cid -File | ForEach-Object {
    $v = ([IO.File]::ReadAllText($_.FullName)).Trim().ToLower()
    $texByCid[$v] = $_.Name.Substring(0, $_.Name.Length - 4)
}

$rows = New-Object System.Collections.Generic.List[object]
$surfaces = @(Get-ChildItem $lib -File -Filter "$Pattern*.Surface" | Sort-Object Name)
foreach ($s in $surfaces) {
    $asset = ($s.BaseName -replace '_[0-9a-f]{32}$', '')
    $slots = Get-SurfaceSlots $s.FullName
    if ($slots.Count -eq 0) {
        $rows.Add([pscustomobject]@{ Surface = $asset; Slot = "(none)"; Cid = ""; Texture = "NO TEXTURE SLOTS ($($s.Length) bytes) = FALLBACK"; Verdict = "FALLBACK" })
        continue
    }
    foreach ($k in $slots.Keys) {
        $cid = $slots[$k]
        $tex = $texByCid[$cid]
        $verdict = "OWN"
        if (-not $tex) { $tex = "UNRESOLVED"; $verdict = "ORPHAN" }
        elseif ($asset -ne $Owner -and $asset -like "$Pattern*" -and $tex -like "${Owner}_*") { $verdict = "SHARED" }
        elseif ($asset -ne $Owner -and $tex -like "${asset}_*") { $verdict = "COPY" }
        $rows.Add([pscustomobject]@{ Surface = $asset; Slot = $k; Cid = $cid; Texture = $tex; Verdict = $verdict })
    }
    if (-not $slots.Contains("_BaseColorMap")) {
        $rows.Add([pscustomobject]@{ Surface = $asset; Slot = "_BaseColorMap"; Cid = ""; Texture = "MISSING"; Verdict = "FALLBACK" })
    }
}
Write-Output ("probe-surfaces: {0} surface(s) matching {1}* in {2}" -f $surfaces.Count, $Pattern, $lib)
$rows | Format-Table -AutoSize | Out-String -Width 220 | Write-Output

$tex = @(Get-ChildItem $lib -File -Filter "$Pattern*.Texture" | Sort-Object Name)
Write-Output ("{0} .Texture file(s) matching {1}*  (shared = only the owner's 4; a copy adds 4 per sharer):" -f $tex.Count, $Pattern)
foreach ($t in $tex) {
    $c = ""
    if (Test-Path ($t.FullName + ".cid")) { $c = ([IO.File]::ReadAllText($t.FullName + ".cid")).Trim() }
    Write-Output ("  {0,-70} {1,10:N0} bytes  cid {2}" -f $t.Name, $t.Length, $c)
}

if ($Snapshot) {
    # [IO.File] resolves a relative path against the process directory, not the PowerShell location.
    # Resolve the path first. Create the parent folder too (the field-test folder docs\research\field\<date>\ may not exist yet).
    $snapPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Snapshot)
    $snapDir = Split-Path -Parent $snapPath
    if ($snapDir -and -not (Test-Path -LiteralPath $snapDir)) {
        New-Item -ItemType Directory -Force -Path $snapDir | Out-Null
        Write-Output "created folder $snapDir"
    }
    $snap = [ordered]@{ written = (Get-Date).ToString("s"); pattern = $Pattern; rows = $rows; textures = @($tex | ForEach-Object { $_.Name }) }
    [IO.File]::WriteAllText($snapPath, ($snap | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    Write-Output "snapshot -> $snapPath"
}
if ($Compare) {
    if (-not (Test-Path -LiteralPath $Compare)) { Write-Output "FATAL: snapshot not found: $Compare"; exit 1 }
    $old = Get-Content -LiteralPath $Compare -Raw | ConvertFrom-Json
    $changed = 0
    foreach ($o in @($old.rows)) {
        $now = $rows | Where-Object { $_.Surface -eq $o.Surface -and $_.Slot -eq $o.Slot } | Select-Object -First 1
        if (-not $now) { Write-Output "GONE     $($o.Surface) $($o.Slot) (was $($o.Texture))"; $changed++ }
        elseif ($now.Cid -ne $o.Cid -or $now.Texture -ne $o.Texture) {
            Write-Output "CHANGED  $($o.Surface) $($o.Slot): $($o.Texture) [$($o.Cid)] -> $($now.Texture) [$($now.Cid)] ($($now.Verdict))"; $changed++
        }
    }
    Write-Output ("compare vs {0}: {1} slot(s) changed" -f $Compare, $changed)
}
if ($Logs) {
    $root = Split-Path $ImportedData
    foreach ($f in @((Join-Path $root "Logs\AssetPipeline.log"), (Join-Path $root "Player.log"))) {
        if (-not (Test-Path $f)) {
            Write-Output "log not found: $f"
            if ((Split-Path $f -Leaf) -eq "AssetPipeline.log") {
                $alt = @(Get-ChildItem (Join-Path $root "Logs") -Filter *.log -File -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -match 'Asset|Editor|Import|Pipeline' } | ForEach-Object { $_.Name })
                $altText = "none"; if ($alt.Count -gt 0) { $altText = $alt -join ", " }
                Write-Output ("  (file name unverified; Logs\ files that look related: {0}; Player.log is scanned next)" -f $altText)
            }
            continue
        }
        $hits = @(Select-String -LiteralPath $f -Pattern "Using fallback|$Pattern|SkiProbe" -SimpleMatch:$false)
        Write-Output ("{0}: {1} line(s) (mtime {2})" -f $f, $hits.Count, (Get-Item $f).LastWriteTime)
        $hits | Select-Object -First 60 | ForEach-Object { Write-Output ("  {0}: {1}" -f $_.LineNumber, $_.Line.Trim()) }
    }
}
