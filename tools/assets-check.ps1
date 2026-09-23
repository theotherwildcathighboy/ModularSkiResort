# assets-check.ps1 - validate every CS2 asset folder under a letter folder (art pipeline
# v2 section 3.9) by running assets\blender\cs2_validate.py in headless Blender.
#
#   .\tools\assets-check.ps1 -Root .\assets\import\stations\fixedgrip\L
#   .\tools\assets-check.ps1 -Root .\assets\import\towers\N -Kind prop
#
# An asset folder is a direct child of -Root that holds settings.json or <folder>.fbx.
# One Blender process validates all of them (FBX round-trip, texture rules, LOD budgets,
# sub-mesh transforms, engine-base ring on buildings) and writes a JSON result the table
# below is built from. The full Blender log is kept next to the JSON in %TEMP%.
# Exit code: 1 when any folder reports an ERROR (or Blender fails), else 0.
# Companion of preflight.ps1 (prefabs) and validate_prefab.ps1 (single prefab).

param(
    [Parameter(Mandatory = $true)][string]$Root,
    [ValidateSet("auto", "prop", "building", "hanging")][string]$Kind = "auto",
    [string]$Blender = "C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe",
    [switch]$ShowLog
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Root -PathType Container)) { Write-Output "FATAL: -Root not found: $Root"; exit 1 }
if (-not (Test-Path $Blender -PathType Leaf)) { Write-Output "FATAL: Blender not found: $Blender"; exit 1 }

$Root = (Resolve-Path $Root).Path
$repo = Split-Path -Parent $PSScriptRoot
$script = Join-Path $repo "assets\blender\cs2_validate.py"
if (-not (Test-Path $script)) { Write-Output "FATAL: validator missing: $script"; exit 1 }

# ---- enumerate asset folders (same rule as cs2_validate.iter_asset_folders) ----------
$folders = @(Get-ChildItem $Root -Directory | Where-Object {
    (Test-Path (Join-Path $_.FullName "settings.json")) -or
    (Test-Path (Join-Path $_.FullName ($_.Name + ".fbx")))
} | Sort-Object Name)
if ($folders.Count -eq 0) { Write-Output "FATAL: no asset folders under $Root"; exit 1 }
Write-Output ("assets-check: {0} folder(s) under {1}" -f $folders.Count, $Root)

# ---- one Blender run over all of them --------------------------------------------------
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$tmp = Join-Path $env:TEMP "assets-check"
New-Item -ItemType Directory -Force $tmp | Out-Null
$jsonPath = Join-Path $tmp ("result-{0}.json" -f $stamp)
$logPath = Join-Path $tmp ("blender-{0}.log" -f $stamp)
$errPath = Join-Path $tmp ("blender-{0}.err" -f $stamp)

$args = @("--background", "--factory-startup", "--python", ('"{0}"' -f $script), "--",
          "--root", ('"{0}"' -f $Root), "--kind", $Kind, "--json", ('"{0}"' -f $jsonPath))
$proc = Start-Process -FilePath $Blender -ArgumentList $args -NoNewWindow -Wait -PassThru `
    -RedirectStandardOutput $logPath -RedirectStandardError $errPath
$blenderExit = $proc.ExitCode

$logLines = @()
if (Test-Path $logPath) { $logLines = @(Get-Content $logPath) }
if ($ShowLog) {
    $logLines | Where-Object { $_ -match '^\[(validate|assets-check|manifest)\]' } | ForEach-Object { Write-Output $_ }
} else {
    $logLines | Where-Object { $_ -match '^\[validate\] (ERROR|WARN) ' } | ForEach-Object { Write-Output $_ }
}

# ---- table --------------------------------------------------------------------------
$failures = 0
if (-not (Test-Path $jsonPath)) {
    Write-Output "FATAL: Blender produced no result (exit $blenderExit). Log: $logPath"
    if (Test-Path $errPath) { Get-Content $errPath | Select-Object -Last 20 | ForEach-Object { Write-Output $_ } }
    exit 1
}
$rows = ConvertFrom-Json ([IO.File]::ReadAllText($jsonPath))
$table = foreach ($r in @($rows)) {
    $status = "OK"
    if ($r.errors -gt 0) { $status = "ERROR"; $failures++ }
    elseif ($r.warnings -gt 0) { $status = "WARN" }
    [PSCustomObject]@{
        Asset  = $r.name
        Kind   = $r.kind
        Status = $status
        Err    = $r.errors
        Warn   = $r.warnings
        Tris   = $r.tris
        Tex    = $r.tex
        Base   = $r.engine_base
        First  = $r.first_error
    }
}
$table | Format-Table -AutoSize -Wrap | Out-String -Width 220 | ForEach-Object { Write-Output $_.TrimEnd() }

$checked = @($rows).Count
if ($checked -ne $folders.Count) {
    Write-Output ("WARN: {0} folders found but {1} validated (see {2})" -f $folders.Count, $checked, $logPath)
}
Write-Output ("assets-check: {0} folder(s), {1} with errors; blender exit {2}; log {3}" -f $checked, $failures, $blenderExit, $logPath)
if ($failures -gt 0 -or $blenderExit -ne 0) { exit 1 }
exit 0
