# game-lock.ps1 - the one-session-at-a-time lock for the game (DECISIONS 664 round-2 protocol).
#
# The lock file is %TEMP%\srm-game.lock, one text file: line 1 = holder name ("owner" for the owner), line 2 = ISO time taken,
# line 3 = note (-Note). The rules it enforces:
#   -Take <name>   takes the lock. Refuses when the file exists (another session or the owner holds it), and refuses when Cities2 runs
#                  with no lock (the owner is playing). Prints the holder and exits 1 on a refusal.
#   -Owner         the owner takes the game: writes the lock "owner". Same refusals as -Take (it never overwrites a session's lock;
#                  with -Force it does, for the owner's own override).
#   -Release       gives the lock back. Refuses while Cities2 runs, and refuses unless `world.ps1 -World Status -Json` shows the
#                  pristine Player world: world PLAYER, no overlay, ImportedData parked. A wrong playset is a warning only
#                  (this script never edits playsets). -Name <name> must match the holder (or -Force). A lock reading "owner" is
#                  released only with -Owner (the owner hands the game back).
#   -Status        prints one line: free / held by <name> since <time> / game running without a lock (owner playing).
#                  Exit 0 = free, 1 = held or owner playing. -Json: the same as one object.
#   -WhatIf        dry run for -Take / -Owner / -Release: says what would happen, writes nothing.
# PowerShell 5.1. Exit: 0 ok, 1 refused / held.
param(
    [string]$Take,
    [switch]$Release,
    [switch]$Status,
    [switch]$Owner,
    [string]$Name,
    [string]$Note = '',
    [switch]$Force,
    [switch]$WhatIf,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'
$LockFile = Join-Path $env:TEMP 'srm-game.lock'
$World = Join-Path $PSScriptRoot 'world.ps1'

function Get-Lock {
    if (-not (Test-Path -LiteralPath $LockFile)) { return $null }
    $l = @(Get-Content -LiteralPath $LockFile -ErrorAction SilentlyContinue)
    [pscustomobject]@{
        holder = if ($l.Count -ge 1) { ([string]$l[0]).Trim() } else { '' }
        since  = if ($l.Count -ge 2) { ([string]$l[1]).Trim() } else { '' }
        note   = if ($l.Count -ge 3) { ([string]$l[2]).Trim() } else { '' }
    }
}
function Test-Game { [bool](Get-Process -Name Cities2 -ErrorAction SilentlyContinue) }
function Out-State([string]$state, [string]$text, [int]$code) {
    $lock = Get-Lock
    if ($Json) { [pscustomobject]@{ state = $state; holder = if ($lock) { $lock.holder } else { '' }; since = if ($lock) { $lock.since } else { '' }; gameRunning = (Test-Game); text = $text } | ConvertTo-Json -Compress }
    else { Write-Host $text }
    exit $code
}
function Write-Lock([string]$holder) {
    $body = @($holder, (Get-Date -Format 'yyyy-MM-ddTHH:mm:ss'), $Note)
    Set-Content -LiteralPath $LockFile -Value $body -Encoding ASCII
}

$lock = Get-Lock
$running = Test-Game

if ($Status -or (-not $Take -and -not $Release -and -not $Owner)) {
    if ($lock) {
        $by = if ($lock.holder -eq 'owner') { 'the owner (wait)' } else { $lock.holder }
        Out-State 'held' "HELD by $by since $($lock.since)$(if ($lock.note) { " ($($lock.note))" })$(if ($running) { '; Cities2 running' } else { '; Cities2 not running' })" 1
    }
    if ($running) { Out-State 'owner-playing' 'NO LOCK but Cities2 is running: the owner is playing. Wait, run nothing heavy.' 1 }
    Out-State 'free' 'FREE: no lock, Cities2 not running. -Take <name> to take it.' 0
}

# ---- Take / Owner ----------------------------------------------------------------------------------------------------------
if ($Take -or ($Owner -and -not $Release)) {
    $who = if ($Owner) { 'owner' } else { $Take.Trim() }
    if (-not $who) { Out-State 'error' 'refused: -Take needs a session name' 1 }
    if ($lock -and -not $Force) {
        if ($lock.holder -eq $who) { Out-State 'held' "already yours: $who since $($lock.since)" 0 }
        Out-State 'held' "refused: the game lock is held by $($lock.holder) since $($lock.since). Wait." 1
    }
    if (-not $lock -and $running -and -not $Owner) { Out-State 'owner-playing' 'refused: Cities2 is running with no lock (the owner is playing). Wait.' 1 }
    if ($WhatIf) { Out-State 'dry' "WOULD write the lock for '$who' ($LockFile)" 0 }
    Write-Lock $who
    Out-State 'held' "LOCK TAKEN by $who at $((Get-Lock).since). Release with: tools\game-lock.ps1 -Release $(if ($Owner) { '-Owner' } else { "-Name $who" }) (after world.ps1 -World Player and the game closed)." 0
}

# ---- Release ---------------------------------------------------------------------------------------------------------------
if ($Release) {
    if (-not $lock) { Out-State 'free' 'nothing to release: no lock file' 0 }
    if ($lock.holder -eq 'owner' -and -not $Owner -and -not $Force) { Out-State 'held' 'refused: the lock reads "owner". The owner hands the game back (-Release -Owner).' 1 }
    if ($lock.holder -ne 'owner' -and -not $Force -and $Name -ne $lock.holder) {
        Out-State 'held' "refused: the lock is held by '$($lock.holder)'; pass -Name $($lock.holder) to release it (or -Force)." 1
    }
    if ($running -and -not $Force) { Out-State 'held' 'refused: Cities2 is running. Quit the game first, then put the machine back (world.ps1 -World Player).' 1 }
    if ($lock.holder -ne 'owner' -or -not $Owner) {
        # the owner handing back may skip the world check (their playsets and launch are theirs); sessions may not
        $raw = (& $World -World Status -Json) -join ''
        try { $w = $raw | ConvertFrom-Json } catch { Out-State 'held' "refused: world.ps1 -World Status gave no JSON: $raw" 1 }
        $why = @()
        if ($w.world -ne 'PLAYER') { $why += "world is $($w.world), not PLAYER" }
        if ($w.overlay) { $why += "an overlay is active ($($w.label))" }
        if (-not $w.importedParked) { $why += 'ImportedData is loaded, not parked' }
        if ($why.Count -gt 0 -and -not $Force) {
            Out-State 'held' "refused: the machine is not the pristine Player world: $($why -join '; '). Run tools\world.ps1 -World Player (no -Build), check -World Status, then release. Status: $($w.line)" 1
        }
        if (-not $w.playsetOk) { Write-Host "WARNING: $($w.line)" }
    }
    if ($WhatIf) { Out-State 'dry' "WOULD delete the lock ($($lock.holder))" 0 }
    Remove-Item -LiteralPath $LockFile -Force
    Out-State 'free' "LOCK RELEASED (was $($lock.holder) since $($lock.since))." 0
}
