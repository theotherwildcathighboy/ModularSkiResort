# PostToolUse hook (matcher Edit|Write|MultiEdit): validates every edited *.Prefab.
# Rule source: docs/PIPELINE.md section 6 / CLAUDE.md "Game-side data" (validate after ANY prefab edit);
# added 2026-10-18. Runs tools/validate_prefab.ps1 <file> (param: -Path; prints ERROR lines on
# STDOUT, exit 1 on any error, 0 if clean). On failure this hook re-emits the output on STDERR
# and exits 2: for PostToolUse, exit 2 does not undo the edit (the tool already ran) but is the
# documented way to show stderr to Claude. Exit 0 stderr is never shown, so a clean file is silent.
# Non-.Prefab files and missing files exit 0 without output.
$ErrorActionPreference = 'Stop'
try {
    $raw = [Console]::In.ReadToEnd()
    $path = [string](($raw | ConvertFrom-Json).tool_input.file_path)
} catch {
    [Console]::Error.WriteLine("validate-prefab-hook.ps1: cannot parse hook input ($($_.Exception.Message))")
    exit 1
}
if ($path -notmatch '(?i)\.Prefab$' -or -not (Test-Path -LiteralPath $path)) { exit 0 }

$validator = Join-Path $PSScriptRoot '..\validate_prefab.ps1'
$ErrorActionPreference = 'Continue'   # native stderr must not throw under 2>&1 (PS 5.1)
$out = & powershell -NoProfile -ExecutionPolicy Bypass -File $validator -Path $path 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("validate_prefab.ps1 FAILED for ${path} (fix before continuing; PIPELINE section 6):`n$($out.Trim())")
    exit 2
}
exit 0
