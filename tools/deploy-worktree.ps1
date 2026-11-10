# deploy-worktree.ps1 - deploy the mod from a git worktree (or from the main checkout) in one safe step.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\deploy-worktree.ps1
#
# WHY: a deploying `dotnet build` from a worktree fails or ships a broken mod, because two git-ignored inputs are
# missing there: mod\SkiResortUI\node_modules (no UI bundle) and mod\SkiResortMod\Textures\*.png (the 8 run textures).
# This script supplies both, builds, checks the deployed folder and removes what it added.
#
# Steps (the main checkout runs only 1, 4, 5 and 7):
#   1  refuse when Cities2 runs (a running game locks the DLL)
#   2  worktree only: node_modules missing -> create a JUNCTION to the main checkout's node_modules (remembered)
#   3  worktree only: run textures missing -> copy the PNGs from the main checkout
#   4  plain `dotnet build` in mod\SkiResortMod (deploys to the game's Mods folder)
#   5  verify the deployed folder: DLL, exactly one .mjs, the run textures, the file count (derived, see below)
#   6  ALWAYS (finally): remove the junction THIS script made, with `cmd /c rmdir` ONLY (never rmdir /s, never
#      Remove-Item -Recurse: that would delete the main checkout's packages through the junction). The path is checked
#      to be a reparse point first, and the main checkout's node_modules\webpack is checked afterwards. A real
#      directory at that path is never touched.
#   7  tools\deploy-check.ps1: its output, then the deployed build (stamp and commit)
#
# The main checkout is the first entry of `git worktree list --porcelain`. The expected file count is derived, not
# hard-coded: every file under the build output (bin\Debug\<tfm>, which Mod.targets DeployWIP mirrors) plus every
# file of mod\SkiResortUI\build except the two markers (.ui-stamp, .gate-pass) that SkiUiDeploy leaves out.
# Exit 0 = built and verified. Exit 1 = refused, build failed or verification failed.

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path
$failed = $false

function Fail([string]$Msg) { Write-Host "FAIL  $Msg" -ForegroundColor Red; $script:failed = $true }
function Ok([string]$Msg) { Write-Host "ok    $Msg" -ForegroundColor Green }
function IsReparse([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    return [bool]((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)
}

# --- 1 the game must not run
if (Get-Process -Name "Cities2" -ErrorAction SilentlyContinue) {
    Write-Host "REFUSED: Cities2 is running (it locks the DLL). Close the game and run this again." -ForegroundColor Red
    exit 1
}
Ok "Cities2 is not running"

# --- where is the main checkout?
$main = $null
foreach ($line in @(git -C $root worktree list --porcelain)) {
    if ($line -like "worktree *") { $main = $line.Substring(9).Trim(); break }
}
if (-not $main) { Write-Host "REFUSED: cannot read the main checkout from git worktree list." -ForegroundColor Red; exit 1 }
$main = [IO.Path]::GetFullPath($main).TrimEnd('\')
$isWorktree = ($main -ne $root.TrimEnd('\'))
if ($isWorktree) { Write-Host "worktree $root`nmain     $main" } else { Write-Host "main checkout $root" }

$modDir = Join-Path $root "mod\SkiResortMod"
$uiDir = Join-Path $root "mod\SkiResortUI"
$link = Join-Path $uiDir "node_modules"
$mainModules = Join-Path $main "mod\SkiResortUI\node_modules"
$madeLink = $false

try {
    if ($isWorktree) {
        # --- 2 node_modules junction
        if (-not (Test-Path -LiteralPath $link)) {
            if (-not (Test-Path -LiteralPath $mainModules)) { throw "the main checkout has no $mainModules (run npm ci there)" }
            cmd /c mklink /J "$link" "$mainModules" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "mklink /J failed (exit $LASTEXITCODE)" }
            $madeLink = $true
            if (-not (IsReparse $link)) { throw "the new node_modules is not a junction" }
            Ok "node_modules: junction created -> $mainModules"
        } else {
            Ok "node_modules: already present in the worktree ($(if (IsReparse $link) { 'a junction you made earlier, left alone' } else { 'a real directory' }))"
        }
        # --- 3 run textures
        $tex = Join-Path $modDir "Textures"
        $have = @(Get-ChildItem -LiteralPath $tex -Filter *.png -File -ErrorAction SilentlyContinue)
        $mainTex = Join-Path $main "mod\SkiResortMod\Textures"
        $src = @(Get-ChildItem -LiteralPath $mainTex -Filter *.png -File -ErrorAction SilentlyContinue)
        if ($src.Count -lt 8) { throw "the main checkout holds $($src.Count) run textures in $mainTex, expected at least 8" }
        if ($have.Count -lt $src.Count) {
            if (-not (Test-Path -LiteralPath $tex)) { New-Item -ItemType Directory -Path $tex | Out-Null }
            foreach ($f in $src) { Copy-Item -LiteralPath $f.FullName -Destination $tex -Force }
            Ok "Textures: copied $($src.Count) PNGs from the main checkout (git-ignored, left in place)"
        } else {
            Ok "Textures: $($have.Count) PNGs already present"
        }
    }

    # --- 4 build (deploys)
    Write-Host "dotnet build (deploying) in $modDir"
    Push-Location $modDir
    try { dotnet build; $buildExit = $LASTEXITCODE } finally { Pop-Location }
    if ($buildExit -ne 0) { throw "dotnet build failed (exit $buildExit); the Mods folder may hold an older or partial build" }
    Ok "dotnet build succeeded"

    # --- 5 verify the deployed folder
    $userData = $env:CSII_USERDATAPATH
    if (-not $userData) { $userData = Join-Path $env:USERPROFILE "AppData\LocalLow\Colossal Order\Cities Skylines II" }
    $deploy = Join-Path $userData "Mods\SkiResortMod"
    if (-not (Test-Path -LiteralPath $deploy)) { Fail "deployed folder missing: $deploy" } else {
        if (Test-Path -LiteralPath (Join-Path $deploy "SkiResortMod.dll")) { Ok "deployed: SkiResortMod.dll" } else { Fail "deployed: SkiResortMod.dll missing" }
        $mjs = @(Get-ChildItem -LiteralPath $deploy -Filter *.mjs -File)
        if ($mjs.Count -eq 1) { Ok "deployed: exactly one .mjs ($($mjs[0].Name))" } else { Fail "deployed: $($mjs.Count) .mjs files (expected exactly 1)" }
        $srcTex = @(Get-ChildItem -LiteralPath (Join-Path $modDir "Textures") -Filter *.png -File -ErrorAction SilentlyContinue)
        $depTex = @(Get-ChildItem -LiteralPath (Join-Path $deploy "Textures") -Filter *.png -File -ErrorAction SilentlyContinue)
        if ($depTex.Count -ge 8 -and $depTex.Count -eq $srcTex.Count) { Ok "deployed: $($depTex.Count) run textures" } else { Fail "deployed: $($depTex.Count) run textures, source has $($srcTex.Count) (need at least 8)" }
        # expected count: build output + bundle files (minus the two markers)
        $outDll = @(Get-ChildItem -Path (Join-Path $modDir "bin\Debug") -Filter SkiResortMod.dll -File -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
        if ($outDll.Count -eq 0) { Fail "no build output under mod\SkiResortMod\bin\Debug" } else {
            $outDir = $outDll[0].DirectoryName
            $outCount = @(Get-ChildItem -LiteralPath $outDir -File -Recurse).Count
            $bundleDir = Join-Path $uiDir "build"
            $bundleCount = 0
            if (Test-Path -LiteralPath $bundleDir) {
                $bundleCount = @(Get-ChildItem -LiteralPath $bundleDir -File -Recurse | Where-Object { $_.Name -ne ".ui-stamp" -and $_.Name -ne ".gate-pass" }).Count
            }
            $expected = $outCount + $bundleCount
            $actual = @(Get-ChildItem -LiteralPath $deploy -File -Recurse).Count
            if ($actual -eq $expected) { Ok "deployed: $actual files = $outCount build output + $bundleCount bundle" }
            else { Fail "deployed: $actual files, expected $expected ($outCount build output + $bundleCount bundle)" }
        }
    }
}
catch {
    Fail $_.Exception.Message
}
finally {
    # --- 6 remove ONLY the junction this script made
    if ($madeLink) {
        if (IsReparse $link) {
            cmd /c rmdir "$link" | Out-Null
            if (Test-Path -LiteralPath $link) { Write-Host "FAIL  the junction $link is still there: remove it with cmd /c rmdir (never rmdir /s)" -ForegroundColor Red; $failed = $true }
            else { Ok "junction removed (rmdir, no /s)" }
        } else {
            Write-Host "FAIL  $link is no longer a junction; NOT touching it" -ForegroundColor Red
            $failed = $true
        }
        if (Test-Path -LiteralPath (Join-Path $mainModules "webpack")) { Ok "main checkout intact: node_modules\webpack exists" }
        else { Write-Host "FAIL  the main checkout's node_modules\webpack is gone: run npm ci in $($main)\mod\SkiResortUI" -ForegroundColor Red; $failed = $true }
    }
}

if ($failed) { Write-Host "RESULT: FAILED (see above)" -ForegroundColor Red; exit 1 }

# --- 7 deploy-check
Write-Host "--- tools\deploy-check.ps1"
$out = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "tools\deploy-check.ps1") 2>&1 | ForEach-Object { "$_" }
$checkExit = $LASTEXITCODE
$out | ForEach-Object { Write-Host $_ }
$dep = @($out | Where-Object { $_ -match "SkiResortMod\.dll stamp" })
if ($dep.Count -gt 0) { Write-Host "DEPLOYED BUILD: $(([regex]::Match($dep[0], 'stamp \S+(, commit \S+)?')).Value -replace ',$','')" }
Write-Host "RESULT: built and verified. deploy-check exit $checkExit (STALE log checks are expected until the game starts on this build)."
exit 0
