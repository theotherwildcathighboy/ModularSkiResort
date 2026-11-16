# release-check.ps1 - READ-ONLY gate for the folder a publish build uploads (release review 2026-10-30: B1, B2, S4).
# Builds nothing, deploys nothing, writes nothing. mod/SkiResortMod/SkiPublish.targets runs it at the end of every publish build
# (PublishNewMod / PublishNewVersion) and of the dry run (-p:SkiPublishDryRun=true). It runs after the .cok, the layouts and the
# strip, and BEFORE ModPublisher uploads. A FAIL fails the build, so nothing is uploaded. To run it by hand, after a dry run, use
# this command from the repo root:
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\release-check.ps1
#
# -DeployDir <folder> picks another folder. Default: %CSII_LOCALMODSPATH%\SkiResortMod, the toolchain's deploy folder
# (%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\SkiResortMod. It comes from Mod.props LocalModsPath and
# Mod.targets:52).
# One PASS / FAIL line per check, then a SUMMARY. Exit 0 = every check PASS, 1 = any FAIL.
#   cok         exactly one .cok at the folder root (the Editor package, mod/SkiResortMod/Package/README.md), under 2 GB (expected
#               about 0.3 GB, measured 0.29 GB on 2026-11-12. A size near 3 GB means the build packaged the whole ImportedData\c030ee3ff256df91 library)
#   textures    at least 8 Textures\*.png (the run textures. mod/SkiResortMod/Textures is git-ignored, release review S4)
#   layouts     K45_WE\layouts\*.wedefault.xml: the same file names as the repo's assets\we\K45_WE\layouts
#   dll         SkiResortMod.dll and SkiResortMod_win_x86_64.dll (the Burst library the game loads)
#   devsurface  SkiResortMod.dll is a Release build: none of the dev-only names (SKI_DEV, mod/SkiResortMod/SkiResortMod.csproj) is in its
#               metadata. A Debug DLL here means the publish profile lost its <Configuration>Release</Configuration>
#   bundle      SkiResortMod.mjs + .css, and the bundle's build stamp equals the DLL's (a mismatched pair blanks the hub)
#   profile     no file holds the Windows profile folder name or its 8.3 short form, as UTF-8 or UTF-16 bytes, any case. The check
#               reads a .cok entry by entry (names and inflated contents). It prints file names only, never the name.
#   user-paths  node tools\check-no-user-paths.js passes (tracked files: no absolute user path, no owner name)
#   surface     node tools\game-surface-check.js --require-decompile passes. The code, the prefabs, the vanilla references and the
#               tested-version strings all match tools\game-surface.lock. The lock matches the decompile of the game it names
#               (docs/PIPELINE.md section 7)
#   version     the release number is the same in all three places (T227): FileVersion in mod/SkiResortMod/SkiResortMod.csproj equals
#               ModVersion in Properties/PublishConfiguration.xml plus ".0" (1.0.2 and 1.0.2.0), the built SkiResortMod.dll carries that
#               FileVersion, and AssemblyVersion is still the pinned 1.0.0.0 (it never moves: saves name the assembly)
#   ci          CI is green on this commit (DECISIONS 664): the "ci.yml" run for the HEAD commit finished with success (gh, read-only; needs
#               the commit pushed and a clean tracked tree: a dirty tree builds code CI never saw). -SkipCi leaves the check out for an
#               offline dry run. Never skip it for a real upload

param([string]$DeployDir, [switch]$SkipCi)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression.FileSystem

# The profile folder name and its 8.3 short form (TEMP paths use it). Never printed: Hide masks both in every output line.
$needles = New-Object System.Collections.Generic.List[string]
$needles.Add((Split-Path $env:USERPROFILE -Leaf))
try {
    $short = Split-Path ((New-Object -ComObject Scripting.FileSystemObject).GetFolder($env:USERPROFILE).ShortPath) -Leaf
    if ($short -and $short -ne $needles[0]) { $needles.Add($short) }
} catch { }

function Hide([string]$Text) {
    $out = [regex]::Replace($Text, [regex]::Escape($env:USERPROFILE), "%USERPROFILE%", "IgnoreCase")
    foreach ($n in $needles) { $out = [regex]::Replace($out, [regex]::Escape($n), "<profile>", "IgnoreCase") }
    $out
}

$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$Name, [string]$Status, [string]$Detail) {
    $results.Add([pscustomobject]@{ Name = $Name; Status = $Status })
    $color = if ($Status -eq "PASS") { "Green" } else { "Red" }
    Write-Host ("{0,-5} {1,-11} {2}" -f $Status, $Name, (Hide $Detail)) -ForegroundColor $color
}

if (-not $DeployDir) {
    $mods = [Environment]::GetEnvironmentVariable("CSII_LOCALMODSPATH", "User")
    if (-not $mods) { $mods = Join-Path $env:USERPROFILE "AppData\LocalLow\Colossal Order\Cities Skylines II\Mods" }
    $DeployDir = Join-Path $mods "SkiResortMod"
}
$DeployDir = $DeployDir.TrimEnd("\")
Write-Host (Hide "release-check: deploy folder $DeployDir")
if (-not (Test-Path -LiteralPath $DeployDir -PathType Container)) {
    Add-Result "deploy" "FAIL" "the deploy folder does not exist (nothing deployed yet?)"
    Write-Host "SUMMARY: 0 PASS, 1 FAIL - nothing to upload." -ForegroundColor Red
    exit 1
}
$DeployDir = (Get-Item -LiteralPath $DeployDir).FullName.TrimEnd("\")
$all = @(Get-ChildItem -LiteralPath $DeployDir -Recurse -File)
function Get-Rel($File) { $File.FullName.Substring($DeployDir.Length + 1) }
$top = @(Get-ChildItem -LiteralPath $DeployDir | ForEach-Object {
    if ($_.PSIsContainer) { "{0}\ ({1} file(s))" -f $_.Name, @(Get-ChildItem -LiteralPath $_.FullName -Recurse -File).Count } else { $_.Name }
})
Write-Host (Hide ("contents: {0}" -f ($top -join ", ")))

# cok
$coks = @(Get-ChildItem -LiteralPath $DeployDir -File | Where-Object { $_.Extension -eq ".cok" })
if ($coks.Count -ne 1) {
    Add-Result "cok" "FAIL" ("{0} .cok file(s) at the folder root, expected exactly 1 (mod\SkiResortMod\Package\README.md)" -f $coks.Count)
} elseif ($coks[0].Length -ge 2GB) {
    Add-Result "cok" "FAIL" ("{0} is {1:N2} GB, expected about 0.3 GB: near 3 GB means the whole c030ee3ff256df91 library was packaged. Package only the prefabs of tools\shipped-prefabs.json" -f $coks[0].Name, ($coks[0].Length / 1GB))
} else {
    Add-Result "cok" "PASS" ("{0}, {1:N2} GB" -f $coks[0].Name, ($coks[0].Length / 1GB))
}

# textures
$texDir = Join-Path $DeployDir "Textures"
$tex = @()
if (Test-Path -LiteralPath $texDir) { $tex = @(Get-ChildItem -LiteralPath $texDir -File | Where-Object { $_.Extension -eq ".png" }) }
if ($tex.Count -ge 8) { Add-Result "textures" "PASS" ("Textures\ holds {0} png file(s)" -f $tex.Count) }
else { Add-Result "textures" "FAIL" ("Textures\ holds {0} png file(s), expected 8: the runs ship untextured (copy the 8 PNGs into the git-ignored mod\SkiResortMod\Textures, see its README.md, and rebuild)" -f $tex.Count) }

# layouts
$repoLayouts = @(Get-ChildItem -LiteralPath (Join-Path $root "assets\we\K45_WE\layouts") -File | Where-Object { $_.Name -like "*.wedefault.xml" } | ForEach-Object { $_.Name })
$weDir = Join-Path $DeployDir "K45_WE\layouts"
$deployedLayouts = @()
if (Test-Path -LiteralPath $weDir) { $deployedLayouts = @(Get-ChildItem -LiteralPath $weDir -File | Where-Object { $_.Name -like "*.wedefault.xml" } | ForEach-Object { $_.Name }) }
$missingLayouts = @($repoLayouts | Where-Object { $deployedLayouts -notcontains $_ })
if ($repoLayouts.Count -gt 0 -and $missingLayouts.Count -eq 0) { Add-Result "layouts" "PASS" ("K45_WE\layouts\ holds the repo's {0} Write Everywhere layout(s)" -f $repoLayouts.Count) }
else { Add-Result "layouts" "FAIL" ("K45_WE\layouts\ lacks {0} of the repo's {1} layout(s): {2}" -f $missingLayouts.Count, $repoLayouts.Count, ($missingLayouts -join ", ")) }

# dll
$dllMissing = @("SkiResortMod.dll", "SkiResortMod_win_x86_64.dll" | Where-Object { -not (Test-Path -LiteralPath (Join-Path $DeployDir $_)) })
if ($dllMissing.Count -eq 0) { Add-Result "dll" "PASS" "SkiResortMod.dll and SkiResortMod_win_x86_64.dll present" }
else { Add-Result "dll" "FAIL" ("missing: {0}" -f ($dllMissing -join ", ")) }

# devsurface: member and type names live in the metadata string heap as UTF-8, so a Latin-1 read finds them as plain text
$devNames = @("QuickRunsDevUISystem", "BuildSkiResortDebugUI", "BuildSkiResortHubDebugUI", "DevRequest", "OnProbePlaceStation")
$dllPath = Join-Path $DeployDir "SkiResortMod.dll"
if (-not (Test-Path -LiteralPath $dllPath)) {
    Add-Result "devsurface" "FAIL" "no SkiResortMod.dll to check"
} else {
    $dllText = [System.Text.Encoding]::GetEncoding(28591).GetString([System.IO.File]::ReadAllBytes($dllPath))
    $devFound = @($devNames | Where-Object { $dllText.Contains($_) })
    if ($devFound.Count -eq 0) { Add-Result "devsurface" "PASS" ("none of the {0} dev-only names in SkiResortMod.dll (a Release build)" -f $devNames.Count) }
    else { Add-Result "devsurface" "FAIL" ("SkiResortMod.dll holds dev-only name(s) {0}: a Debug build. Publish with -c Release (the .pubxml Configuration)" -f ($devFound -join ", ")) }
}

# bundle: the build stamp in the DLL (a UTF-16 const string, at either byte alignment) vs the one in SkiResortMod.mjs
$stampRe = '(?:ui-)?\d{8}-\d{6}-[0-9a-f]{6}'
$dll = Join-Path $DeployDir "SkiResortMod.dll"
$mjs = Join-Path $DeployDir "SkiResortMod.mjs"
$css = Join-Path $DeployDir "SkiResortMod.css"
if (-not (Test-Path -LiteralPath $mjs) -or -not (Test-Path -LiteralPath $css)) {
    Add-Result "bundle" "FAIL" "SkiResortMod.mjs or SkiResortMod.css is missing: the hub UI would not load"
} elseif (-not (Test-Path -LiteralPath $dll)) {
    Add-Result "bundle" "FAIL" "no SkiResortMod.dll to compare the bundle stamp with"
} else {
    $bytes = [System.IO.File]::ReadAllBytes($dll)
    $dllStamps = @()
    foreach ($skip in 0, 1) {
        $text = [System.Text.Encoding]::Unicode.GetString($bytes, $skip, $bytes.Length - $skip - (($bytes.Length - $skip) % 2))
        foreach ($m in [regex]::Matches($text, $stampRe)) { $dllStamps += $m.Value }
    }
    $dllStamps = @($dllStamps | Select-Object -Unique)
    $mjsStamps = @([regex]::Matches([System.IO.File]::ReadAllText($mjs), $stampRe) | ForEach-Object { $_.Value } | Select-Object -Unique)
    if ($dllStamps.Count -eq 1 -and $mjsStamps.Count -eq 1 -and $dllStamps[0] -eq $mjsStamps[0]) {
        Add-Result "bundle" "PASS" ("SkiResortMod.mjs + .css, stamp {0} in both the DLL and the bundle" -f $dllStamps[0])
    } else {
        Add-Result "bundle" "FAIL" ("stamps differ: DLL '{0}', bundle '{1}' (a mismatched pair blanks the hub; rebuild)" -f ($dllStamps -join ", "), ($mjsStamps -join ", "))
    }
}

# profile: every file's bytes, read as Latin-1 so one string search covers the UTF-8 and the UTF-16LE spelling of each needle
$latin1 = [System.Text.Encoding]::GetEncoding(28591)
$patterns = New-Object System.Collections.Generic.List[string]
foreach ($n in $needles) {
    $patterns.Add($latin1.GetString([System.Text.Encoding]::UTF8.GetBytes($n)))
    $patterns.Add($latin1.GetString([System.Text.Encoding]::Unicode.GetBytes($n)))
}
function Test-Name([string]$Text) {
    foreach ($n in $needles) { if ($Text.IndexOf($n, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true } }
    return $false
}
function Test-Bytes([byte[]]$Bytes) {
    $text = $latin1.GetString($Bytes)
    foreach ($p in $patterns) { if ($text.IndexOf($p, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true } }
    return $false
}
$hits = New-Object System.Collections.Generic.List[string]
$unreadable = New-Object System.Collections.Generic.List[string]
$entries = 0
foreach ($f in $all) {
    $rel = Get-Rel $f
    if (Test-Name $rel) { $hits.Add($rel); continue }
    if ($f.Extension -eq ".cok") {
        try {
            $zip = [System.IO.Compression.ZipFile]::OpenRead($f.FullName)
            try {
                foreach ($e in $zip.Entries) {
                    $entries++
                    if (Test-Name $e.FullName) { $hits.Add("$rel!$($e.FullName)"); continue }
                    $ms = New-Object System.IO.MemoryStream
                    $s = $e.Open()
                    try { $s.CopyTo($ms) } finally { $s.Dispose() }
                    if (Test-Bytes $ms.ToArray()) { $hits.Add("$rel!$($e.FullName)") }
                    $ms.Dispose()
                }
            } finally { $zip.Dispose() }
        } catch {
            $unreadable.Add($rel)
        }
    } else {
        if (Test-Bytes ([System.IO.File]::ReadAllBytes($f.FullName))) { $hits.Add($rel) }
    }
}
if ($hits.Count -eq 0 -and $unreadable.Count -eq 0) {
    Add-Result "profile" "PASS" ("{0} file(s) scanned, {1} of them entries inside the .cok: none holds the profile folder name" -f ($all.Count + $entries), $entries)
} elseif ($hits.Count -gt 0) {
    Add-Result "profile" "FAIL" ("{0} file(s) hold the profile folder name: {1}" -f $hits.Count, (($hits | Select-Object -First 12) -join ", "))
}
if ($unreadable.Count -gt 0) {
    Add-Result "profile" "FAIL" ("cannot read as a ZIP, so not scanned: {0}" -f ($unreadable -join ", "))
}

# user-paths
try {
    $out = @(& node (Join-Path $root "tools\check-no-user-paths.js"))
    $code = $LASTEXITCODE
    $last = if ($out.Count) { $out[-1] } else { "(no output)" }
    if ($code -eq 0) { Add-Result "user-paths" "PASS" $last }
    else { Add-Result "user-paths" "FAIL" ("{0}; first hits: {1}" -f $last, (($out | Select-Object -First 3) -join " | ")) }
} catch {
    Add-Result "user-paths" "FAIL" ("node tools\check-no-user-paths.js did not run: {0}" -f $_.Exception.Message)
}

try {
    $out = @(& node (Join-Path $root "tools\game-surface-check.js") --require-decompile)
    $code = $LASTEXITCODE
    $last = if ($out.Count -gt 0) { $out[$out.Count - 1] } else { "no output" }
    if ($code -eq 0) { Add-Result "surface" "PASS" $last }
    else { Add-Result "surface" "FAIL" ("{0}; first: {1}" -f $last, (($out | Select-Object -First 2) -join " | ")) }
} catch {
    Add-Result "surface" "FAIL" ("node tools\game-surface-check.js did not run: {0}" -f $_.Exception.Message)
}

# version (T227): csproj FileVersion == ModVersion + ".0", the DLL carries it, AssemblyVersion stays pinned
try {
    $csprojText = [System.IO.File]::ReadAllText((Join-Path $root "mod\SkiResortMod\SkiResortMod.csproj"))
    $fileVer = [regex]::Match($csprojText, "<FileVersion>([^<]+)</FileVersion>").Groups[1].Value
    $asmVer = [regex]::Match($csprojText, "<AssemblyVersion>([^<]+)</AssemblyVersion>").Groups[1].Value
    $pubText = [System.IO.File]::ReadAllText((Join-Path $root "mod\SkiResortMod\Properties\PublishConfiguration.xml"))
    $modVer = [regex]::Match($pubText, '<ModVersion\s+Value="([^"]+)"').Groups[1].Value
    $dllVer = if (Test-Path -LiteralPath $dllPath) { [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath).FileVersion } else { "(no dll)" }
    $problems = @()
    if (-not $fileVer -or -not $modVer) { $problems += "could not read FileVersion ($fileVer) or ModVersion ($modVer)" }
    else {
        if ($fileVer -ne "$modVer.0") { $problems += "FileVersion $fileVer in SkiResortMod.csproj is not ModVersion $modVer + .0 (PublishConfiguration.xml)" }
        if ($dllVer -ne "$modVer.0") { $problems += "the DLL carries FileVersion $dllVer, expected $modVer.0 (rebuild after the bump)" }
    }
    if ($asmVer -ne "1.0.0.0") { $problems += "AssemblyVersion is $asmVer, it must stay 1.0.0.0" }
    if ($problems.Count -eq 0) { Add-Result "version" "PASS" ("ModVersion {0}, FileVersion {1} in the csproj and the DLL, AssemblyVersion {2} pinned" -f $modVer, $fileVer, $asmVer) }
    else { Add-Result "version" "FAIL" ($problems -join "; ") }
} catch {
    Add-Result "version" "FAIL" ("could not check the versions: {0}" -f $_.Exception.Message)
}

# ci: the ci.yml run of the HEAD commit is a success (gh is read-only here)
if ($SkipCi) {
    Add-Result "ci" "PASS" "SKIPPED (-SkipCi): not a valid check for an upload"
} else {
    try {
        $ghGlob = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages\GitHub.cli_*\bin\gh.exe'
        $gh = @(Get-ChildItem $ghGlob -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName)
        if (-not $gh) { $cmd = Get-Command gh -ErrorAction SilentlyContinue; if ($cmd) { $gh = @($cmd.Source) } }
        if (-not $gh) { throw "gh not found (winget GitHub.cli)" }
        $head = ((& git -C $root rev-parse HEAD) -join "").Trim()
        if ($head.Length -ne 40) { throw "no git HEAD in $root" }
        $dirty = @(& git -C $root status --porcelain --untracked-files=no)
        Push-Location $root
        try { $json = (& $gh[0] run list --workflow ci.yml --commit $head --limit 5 --json status,conclusion,url,headSha) -join "" }
        finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) { throw "gh run list failed (exit $LASTEXITCODE): $json" }
        $runs = @($json | ConvertFrom-Json)
        $short = $head.Substring(0, 8)
        $ok = @($runs | Where-Object { $_.status -eq "completed" -and $_.conclusion -eq "success" })
        if ($dirty.Count -gt 0) { Add-Result "ci" "FAIL" ("{0} tracked file(s) differ from commit {1}: CI never saw this code (commit and push first)" -f $dirty.Count, $short) }
        elseif ($ok.Count -gt 0) { Add-Result "ci" "PASS" ("ci.yml is green on {0}: {1}" -f $short, $ok[0].url) }
        elseif ($runs.Count -eq 0) { Add-Result "ci" "FAIL" ("no ci.yml run for {0}: push the commit and wait for CI" -f $short) }
        else { Add-Result "ci" "FAIL" ("ci.yml on {0} is {1}/{2}: {3}" -f $short, $runs[0].status, $runs[0].conclusion, $runs[0].url) }
    } catch {
        Add-Result "ci" "FAIL" ("could not read CI: {0}" -f $_.Exception.Message)
    }
}

$fail = @($results | Where-Object { $_.Status -eq "FAIL" }).Count
$pass = @($results | Where-Object { $_.Status -eq "PASS" }).Count
if ($fail -eq 0) {
    Write-Host "SUMMARY: ALL $pass CHECKS PASS - this folder is ready to upload." -ForegroundColor Green
    exit 0
}
Write-Host "SUMMARY: $pass PASS, $fail FAIL - do NOT upload this folder." -ForegroundColor Red
exit 1
