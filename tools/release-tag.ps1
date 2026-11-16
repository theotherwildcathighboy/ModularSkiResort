<#
.SYNOPSIS
  Step 9 of docs/RELEASE-CHECKLIST.md section 8: the annotated tag vX.Y.Z on the built commit, pushed, and the GitHub release with the
  changelog entry as its notes. DRY RUN by default. A real run needs -Execute AND the owner typing the tag name at the prompt.

.DESCRIPTION
  It refuses (exit 2, in the dry run too) unless ALL of these hold:
    - the tag vX.Y.Z does not exist, locally or on origin;
    - -Commit (default HEAD) IS the checked-out HEAD, and the tracked tree is clean;
    - the commit is on origin/main (the integration branch is merged and pushed first, step 9);
    - FileVersion in SkiResortMod.csproj = X.Y.Z.0 and ModVersion in PublishConfiguration.xml = X.Y.Z;
    - release/CHANGELOG.md has "## X.Y.Z (PDX version N, <date>)" (the GitHub release notes are that entry);
    - tools\release-check.ps1 passes on this commit NOW (exit 0, "ALL n CHECKS PASS"; it includes the ci.yml gate for HEAD, so a commit
      without a green CI run never gets tagged), and the built SkiResortMod.dll's build stamp names this commit.
  The release-check result is not remembered between runs: this script runs it itself, against -DeployDir (default: the toolchain's deploy
  folder, the one the dry-run build filled).

  The tag message follows v1.0.0: "X.Y.Z = Paradox Mods 167215 PDX version N (date); build stamp <ProductVersion of the DLL>".
  The tag is pushed (git push origin vX.Y.Z); main is NOT pushed by this script. Then `gh release create vX.Y.Z --title ... --notes-file`.
  Agents never run this for real: the owner publishes (step 7) and tags (step 9).

.PARAMETER Version     "1.0.2". Required.
.PARAMETER Pdx         the Paradox Mods version number the upload got ("3"). Required (record it after step 7).
.PARAMETER Commit      the built commit (default HEAD; must equal HEAD).
.PARAMETER DeployDir   passed to release-check.ps1.
.PARAMETER Execute     really tag, push and create the release (after the typed confirmation).
.PARAMETER SkipReleaseCheck  offline test of the OTHER gates only. Never with -Execute (refused).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\release-tag.ps1 -Version 1.0.2 -Pdx 3
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\release-tag.ps1 -Version 1.0.2 -Pdx 3 -Execute
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][int]$Pdx,
    [string]$Commit,
    [string]$DeployDir,
    [switch]$Execute,
    [switch]$SkipReleaseCheck
)

$ErrorActionPreference = 'Stop'
$Repo = Split-Path -Parent $PSScriptRoot
$Tag = "v$Version"
$results = New-Object System.Collections.Generic.List[object]
function Gate([string]$name, [bool]$ok, [string]$detail) {
    $results.Add([pscustomobject]@{ name = $name; ok = $ok })
    Write-Host ("{0,-5} {1,-13} {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail)
}
function Invoke-Git { $o = & git -C $Repo @args 2>&1; [pscustomobject]@{ code = $LASTEXITCODE; out = (($o | ForEach-Object { "$_" }) -join "`n").Trim() } }

if ($Version -notmatch '^\d+\.\d+\.\d+$') { Write-Host "release-tag: -Version must look like 1.0.2"; exit 2 }
if ($Execute -and $SkipReleaseCheck) { Write-Host 'release-tag: refused: -SkipReleaseCheck never goes with -Execute'; exit 2 }
Write-Host "release-tag: $Tag (PDX version $Pdx)$(if (-not $Execute) { ' [DRY RUN: nothing is tagged, pushed or created]' })"

# 1. the tag is free
$local = Invoke-Git rev-parse -q --verify "refs/tags/$Tag"
$remote = Invoke-Git ls-remote --tags origin "refs/tags/$Tag"
Gate 'tag-free' ($local.code -ne 0 -and -not $remote.out) "$Tag $(if ($local.code -eq 0) { 'exists locally' } elseif ($remote.out) { 'exists on origin' } else { 'does not exist yet' })"

# 2. the commit is HEAD and the tree is clean
$head = (Invoke-Git rev-parse HEAD).out
if (-not $Commit) { $Commit = $head }
$full = (Invoke-Git rev-parse "$Commit^{commit}").out
$dirty = Invoke-Git status --porcelain --untracked-files=no
Gate 'head' ($full -eq $head -and -not $dirty.out) "commit $($full.Substring(0, [Math]::Min(8, $full.Length)))$(if ($full -ne $head) { " is not the checked-out HEAD ($($head.Substring(0, 8)))" })$(if ($dirty.out) { '; tracked files differ from the commit' })"

# 3. the commit is on origin/main
$null = Invoke-Git fetch -q origin main
$anc = Invoke-Git merge-base --is-ancestor $full origin/main
Gate 'on-origin-main' ($anc.code -eq 0) "$(if ($anc.code -eq 0) { 'the commit is on origin/main' } else { 'the commit is NOT on origin/main: merge r<N>/integration into main and push main first (step 9)' })"

# 4. version numbers
$csproj = Get-Content (Join-Path $Repo 'mod\SkiResortMod\SkiResortMod.csproj') -Raw -Encoding UTF8
$pub = Get-Content (Join-Path $Repo 'mod\SkiResortMod\Properties\PublishConfiguration.xml') -Raw -Encoding UTF8
$fv = if ($csproj -match '<FileVersion>([\d.]+)</FileVersion>') { $Matches[1] } else { '?' }
$mv = if ($pub -match '<ModVersion\s+Value="([\d.]+)"') { $Matches[1] } else { '?' }
Gate 'version' ($fv -eq "$Version.0" -and $mv -eq $Version) "FileVersion $fv, ModVersion $mv, wanted $Version.0 and $Version"

# 5. the changelog entry (also the release notes)
$cl = Get-Content (Join-Path $Repo 'release\CHANGELOG.md') -Raw -Encoding UTF8
$m = [regex]::Match($cl, "(?ms)^## $([regex]::Escape($Version)) \(PDX version (\d+), (\d{4}-\d{2}-\d{2})\)\r?\n(.*?)(?=^## |\z)")
$date = if ($m.Success) { $m.Groups[2].Value } else { '' }
Gate 'changelog' ($m.Success -and [int]$m.Groups[1].Value -eq $Pdx) $(if (-not $m.Success) { "no '## $Version (PDX version $Pdx, <date>)' entry in release/CHANGELOG.md" } elseif ([int]$m.Groups[1].Value -ne $Pdx) { "the entry says PDX version $($m.Groups[1].Value), not $Pdx" } else { "entry found ($date)" })
$notes = if ($m.Success) { $m.Groups[3].Value.Trim() } else { '' }

# 6. release-check on this commit now, and the DLL's build stamp
$stamp = ''
if ($SkipReleaseCheck) { Gate 'release-check' $false 'skipped (-SkipReleaseCheck): this dry run proves nothing about the folder' }
else {
    $rc = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'release-check.ps1'))
    if ($DeployDir) { $rc += @('-DeployDir', $DeployDir) }
    $rcOut = & powershell @rc 2>&1 | ForEach-Object { "$_" }
    $rcCode = $LASTEXITCODE
    $summary = @($rcOut | Where-Object { $_ -match '^SUMMARY' }) | Select-Object -Last 1
    Gate 'release-check' ($rcCode -eq 0 -and $summary -match 'ALL \d+ CHECKS PASS') "exit $rcCode, $summary"
    if ($rcCode -ne 0) { $rcOut | Where-Object { $_ -match '^FAIL' } | ForEach-Object { Write-Host "      $_" } }
    $dir = if ($DeployDir) { $DeployDir } else { Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\SkiResortMod' }
    $dll = Join-Path $dir 'SkiResortMod.dll'
    if (Test-Path -LiteralPath $dll) { $stamp = (Get-Item -LiteralPath $dll).VersionInfo.ProductVersion }
    Gate 'build-stamp' ($stamp -match '\+([0-9a-f]{7,40})$' -and $full.StartsWith($Matches[1])) "the DLL says '$stamp'; the commit is $($full.Substring(0, 8))"
}

$failed = @($results | Where-Object { -not $_.ok }).Count
if ($failed -gt 0) { Write-Host "release-tag: refused: $failed gate(s) FAILED. Nothing was tagged."; exit 2 }

$short = $full.Substring(0, 8)
$tagMessage = "$Version = Paradox Mods 167215 PDX version $Pdx ($date); built commit $short (build stamp $stamp)"
$notesFile = Join-Path $env:TEMP "release-notes-$Version.md"
Write-Host ''
Write-Host "WOULD run:"
Write-Host "  git tag -a $Tag $short -m `"$tagMessage`""
Write-Host "  git push origin $Tag"
Write-Host "  gh release create $Tag --verify-tag --title `"$Tag`" --notes-file <the $Version entry of release/CHANGELOG.md, $($notes.Split("`n").Count) lines>"
if (-not $Execute) { Write-Host ''; Write-Host 'dry run done: every gate PASSED. The owner runs it for real with -Execute.'; exit 0 }

# ---- real run ---------------------------------------------------------------------------------------------------------------
$typed = Read-Host "Type $Tag to tag, push and publish the GitHub release"
if ($typed -ne $Tag) { Write-Host 'release-tag: not confirmed. Nothing was done.'; exit 2 }
$gh = if (Get-Command gh -ErrorAction SilentlyContinue) { 'gh' } else {
    @(Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages') -Directory -Filter 'GitHub.cli_*' -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'bin\gh.exe' } | Where-Object { Test-Path $_ })[0]
}
if (-not $gh) { Write-Host 'release-tag: gh not found; nothing was done.'; exit 2 }
[IO.File]::WriteAllText($notesFile, $notes + "`n", (New-Object Text.UTF8Encoding $false))
$t = Invoke-Git tag -a $Tag $full -m $tagMessage
if ($t.code -ne 0) { Write-Host "release-tag: git tag failed: $($t.out)"; exit 3 }
$p = Invoke-Git push origin $Tag
if ($p.code -ne 0) { Write-Host "release-tag: the tag is local only, push failed: $($p.out)"; exit 3 }
& $gh release create $Tag --verify-tag --title $Tag --notes-file $notesFile
if ($LASTEXITCODE -ne 0) { Write-Host "release-tag: the tag is pushed but 'gh release create' failed (exit $LASTEXITCODE). Create the release by hand with the same notes."; exit 3 }
Write-Host "release-tag: $Tag tagged, pushed and released."
exit 0
