# PreToolUse hook (matcher Bash|PowerShell): blocks the two commands that deploy to the game.
# Rule source: CLAUDE.md "Build & deploy" (Claude and subagents DEPLOY NOTHING). Added 2026-10-18
# after the 2026-10-13 deploy accident (a full `dotnet build` fired DeployWIP over the Mods folder).
#   (a) `dotnet build` is allowed ONLY as the compile check `-t:Compile`, with no
#       --no-incremental / Rebuild alongside it (those turn it back into a deploying build).
#   (b) `--env deploy` (webpack UI deploy, also what `npm run dev` runs) is never allowed.
# Reads the hook JSON on stdin; exit 2 + one stderr line = block (Claude sees the line);
# exit 0 = allow. A command that merely MENTIONS these strings (grep, echo) is blocked too:
# rephrase it or ask the owner.
$ErrorActionPreference = 'Stop'
try {
    $raw = [Console]::In.ReadToEnd()
    $cmd = [string](($raw | ConvertFrom-Json).tool_input.command)
} catch {
    [Console]::Error.WriteLine("block-forbidden-build.ps1: cannot parse hook input ($($_.Exception.Message)); not enforcing")
    exit 1   # non-blocking error: shown in the transcript, the tool call proceeds
}

function Block([string]$why) { [Console]::Error.WriteLine("BLOCKED by CLAUDE.md 'Build & deploy': $why"); exit 2 }

if ($cmd -match '(?i)\bdotnet\s+build\b') {
    if ($cmd -notmatch '(?i)[-/](t|target):Compile\b') {
        Block "dotnet build without -t:Compile deploys to the Mods folder (2026-10-13). The only allowed form is exactly: dotnet build -t:Compile"
    }
    if ($cmd -match '(?i)--no-incremental|Rebuild') {
        Block "dotnet build -t:Compile must carry no other flags: --no-incremental / Rebuild turn it into a deploying full build"
    }
}
if ($cmd -match '(?i)--env[\s=]+deploy\b') {
    Block "'--env deploy' (UI deploy) touches the game's Mods folder; Claude deploys nothing. Use plain: npm run build"
}
if ($cmd -match '(?i)\bnpm\s+run\s+dev\b') {
    Block "'npm run dev' is 'webpack --watch --env deploy' (mod/SkiResortUI/package.json): it deploys. Use plain: npm run build"
}
exit 0
