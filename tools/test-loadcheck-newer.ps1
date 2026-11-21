<#
.SYNOPSIS
  Gate for tools/loadcheck.ps1 part (b), r8-testpass B3: Get-NewerPersistedTypes names the persisted types that are newer than a
  save (its release's CHANGELOG "Layout versions" line lacks them), so 0 entities of them on an old save is info, not a FAIL.
  Runs the function alone (taken from the script's syntax tree) against the repo's CHANGELOG and SaveLayouts.cs. No game.
  A new persisted type in a release adds itself to the expected lists below. Exit 0 PASS, else the number of failures.
#>
param([string]$Script = (Join-Path $PSScriptRoot "loadcheck.ps1"))
$ErrorActionPreference = 'Stop'
$ast = [System.Management.Automation.Language.Parser]::ParseFile($Script, [ref]$null, [ref]$null)
$fn = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-NewerPersistedTypes' }, $true) | Select-Object -First 1
if (-not $fn) { throw 'function not found' }
$Repo = Split-Path -Parent (Split-Path -Parent $Script)
. ([scriptblock]::Create($fn.Extent.Text))
$fail = 0
function Check($stem, [string[]]$want, $wantRelease) {
    $r = Get-NewerPersistedTypes $stem
    $got = ($r.newer -join ',')
    $ok = ($got -eq ($want -join ',')) -and ("$($r.release)" -eq "$wantRelease")
    if (-not $ok) { $script:fail++ }
    Write-Output ("{0} {1}: release={2} newer=[{3}] note={4}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $stem, $r.release, $got, $r.note)
}
Check '1.0.3_pdx4_2026-11-19_ashflat' @('SkiPassPlan', 'SkiSeasonPassArea') '1.0.3'
Check 'loadcheck_1.0.3_pdx4_2026-11-19_ashflat' @('SkiPassPlan', 'SkiSeasonPassArea') '1.0.3'
Check '1.0.0_pdx1_2026-11-12_ashflat' @('SkiPassPlan', 'SkiRulesStamp', 'SkiSeasonPassArea', 'SkiTrackPack') '1.0.0'
Check '1.0.1_pdx2_x' @('SkiPassPlan', 'SkiRulesStamp', 'SkiSeasonPassArea') '1.0.1'
Check '1.0.4_dev_x' @() ''
Check 'loaded-city' @() ''
Check '0.9.0_beta' @() ''
Write-Output "test-loadcheck-newer: $(7 - $fail) of 7 PASS, failures=$fail"
exit $fail
