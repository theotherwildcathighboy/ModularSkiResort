<#
.SYNOPSIS
  Daily reach snapshot: Paradox Mods subscribers and likes, appended to docs/reach/paradox.csv.

.DESCRIPTION
  docs/ is local-only (.gitignore), so the CSVs never reach the public repo.
    paradox.csv           one row per run: subscriptions, likes (ratingsTotal), version, last update
    snapshot.log          one line per run, errors included
  The Paradox endpoint is the one the mod page itself calls; it needs no login.
  GitHub traffic was dropped on purpose (few players visit the repo).

  Register as a daily task (once):  .\tools\reach-snapshot.ps1 -Register
  Run by hand:                      .\tools\reach-snapshot.ps1
#>
param(
    [switch]$Register,
    [string]$Time = '20:00',
    [int]$ModId = 167215
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'docs\reach'
$taskName = 'ModularSkiResort reach snapshot'

if ($Register) {
    # conhost --headless: no console window at all (-WindowStyle Hidden still flashes one for an Interactive task).
    $action = New-ScheduledTaskAction -Execute 'conhost.exe' `
        -Argument "--headless powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    $trigger = New-ScheduledTaskTrigger -Daily -At $Time
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 5)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null
    "Registered '$taskName', daily at $Time (catches up after a missed run)."
    return
}

New-Item -ItemType Directory -Force $out | Out-Null
$now = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$log = Join-Path $out 'snapshot.log'
$notes = @()

function Add-Csv($path, $rows) {
    $rows | Export-Csv -Path $path -Append -NoTypeInformation -Encoding UTF8
}

# --- Paradox Mods --------------------------------------------------------------------------
$pdx = $null
try {
    $pdx = (Invoke-RestMethod "https://api.paradox-interactive.com/mods?modId=$ModId&os=Windows" -TimeoutSec 30).modDetail
} catch { $notes += "paradox: $($_.Exception.Message)" }

if ($pdx) {
    Add-Csv (Join-Path $out 'paradox.csv') ([pscustomobject]@{
        timestamp_utc = $now
        subscriptions = $pdx.subscriptions
        likes         = $pdx.ratingsTotal
        version       = $pdx.userModVersion
        latest_update = $pdx.latestUpdate
    })
}

$summary = "$now subs=$(if ($pdx) { $pdx.subscriptions } else { '?' }) likes=$(if ($pdx) { $pdx.ratingsTotal } else { '?' })"
if ($notes) { $summary += ' ERR ' + ($notes -join '; ') }
Add-Content -Path $log -Value $summary -Encoding UTF8
$summary
