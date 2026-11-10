# PreToolUse hook (matcher Bash|PowerShell): guards the commands that deploy to the game's Mods folder.
# Rule source: CLAUDE.md "Build & deploy". Since 2026-11-09 (DECISIONS 633) Claude may build and deploy when the work needs it;
# the owner knows when the game is in use. What stays guarded:
#   (a) No deploying build while Cities2 runs: a full `dotnet build` (DeployWIP), `--env deploy` (UI) and `npm run dev`
#       would hit the locked DLL or swap files under the running game.
#   (b) A compile check `-t:Compile` must carry no --no-incremental / Rebuild: those turn it into a deploying full build
#       nobody meant (the 2026-10-13 accident).
# Reads the hook JSON on stdin. Exit 2 + one stderr line = block (Claude sees the line). Exit 0 = allow.
$ErrorActionPreference = 'Stop'
try {
    $raw = [Console]::In.ReadToEnd()
    $cmd = [string](($raw | ConvertFrom-Json).tool_input.command)
} catch {
    [Console]::Error.WriteLine("block-forbidden-build.ps1: cannot parse hook input ($($_.Exception.Message)); not enforcing")
    exit 1   # non-blocking error: shown in the transcript, the tool call proceeds
}

function Block([string]$why) { [Console]::Error.WriteLine("BLOCKED by CLAUDE.md 'Build & deploy': $why"); exit 2 }

$gameRunning = [bool](Get-Process -Name Cities2 -ErrorAction SilentlyContinue)
$deploys = $false
if ($cmd -match '(?i)\bdotnet\s+build\b') {
    $compileOnly = $cmd -match '(?i)[-/](t|target):Compile\b'
    if ($compileOnly -and $cmd -match '(?i)--no-incremental|Rebuild') {
        Block "a compile check carries no other flags: --no-incremental / Rebuild turn it into a deploying full build. For a deploy, use a plain dotnet build"
    }
    if (-not $compileOnly) { $deploys = $true }
}
if ($cmd -match '(?i)--env[\s=]+deploy\b' -or $cmd -match '(?i)\bnpm\s+run\s+dev\b') { $deploys = $true }
if ($deploys -and $gameRunning) {
    Block "Cities2 is running: a deploying build would hit the locked DLL or swap files under the game. Wait until the owner closes it"
}
exit 0
