<#
.SYNOPSIS
  Daily reach snapshot: Paradox Mods subscribers/likes + GitHub traffic, appended to docs/reach/*.csv.

.DESCRIPTION
  docs/ is local-only (.gitignore), so the CSVs never reach the public repo.
    paradox.csv           one row per run: subscriptions, likes (ratingsTotal), version, GitHub stars/forks
    github-daily.csv      one row per UTC day: views, unique visitors, clones, unique cloners
                          (GitHub keeps only 14 days; each run upserts the window, latest count wins)
    github-referrers.csv  one block per run: the 14-day top referrers (where visitors come from)
    snapshot.log          one line per run, errors included
  The Paradox endpoint is the one the mod page itself calls; it needs no login.
  GitHub traffic needs the gh CLI logged in as the repo owner (push access).

  Register as a daily task (once):  .\tools\reach-snapshot.ps1 -Register
  Run by hand:                      .\tools\reach-snapshot.ps1
#>
param(
    [switch]$Register,
    [string]$Time = '20:00',
    [int]$ModId = 167215,
    [string]$Repo = 'theotherwildcathighboy/ModularSkiResort'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'docs\reach'
$taskName = 'ModularSkiResort reach snapshot'

if ($Register) {
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$PSCommandPath`""
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

# --- GitHub (needs gh) -----------------------------------------------------------------------
$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    # winget installs gh outside PATH until PATH refreshes; a scheduled task may not see it either.
    $found = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GitHub.cli_*\bin\gh.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { Set-Alias gh $found.FullName; $gh = $found }
}
# A scheduled task cannot read gh's keyring login (measured 2026-11-12: "not logged into any GitHub hosts").
# REACH_GH_TOKEN (user env var, fine-grained, this repo only, Administration: read) feeds gh for this process only.
if ($env:REACH_GH_TOKEN) { $env:GH_TOKEN = $env:REACH_GH_TOKEN }
$repoInfo = $null; $views = $null; $clones = $null; $refs = $null
if ($gh) {
    # gh failures do not throw (native exe): check the exit code and keep its message.
    function Get-Gh($path) {
        $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        $r = & gh api $path 2>&1
        $ErrorActionPreference = $prev
        if ($LASTEXITCODE -ne 0) { throw "gh api $path exit $LASTEXITCODE`: $(($r | Out-String).Trim())" }
        ($r | Out-String) | ConvertFrom-Json
    }
    try {
        $repoInfo = Get-Gh "repos/$Repo"
        $views = Get-Gh "repos/$Repo/traffic/views"
        $clones = Get-Gh "repos/$Repo/traffic/clones"
        $refs = Get-Gh "repos/$Repo/traffic/popular/referrers"
    } catch { $notes += "github: $($_.Exception.Message)" }
} else { $notes += 'github: gh not on PATH' }

if ($pdx) {
    Add-Csv (Join-Path $out 'paradox.csv') ([pscustomobject]@{
        timestamp_utc = $now
        subscriptions = $pdx.subscriptions
        likes         = $pdx.ratingsTotal
        version       = $pdx.userModVersion
        latest_update = $pdx.latestUpdate
        gh_stars      = $(if ($repoInfo) { $repoInfo.stargazers_count } else { '' })
        gh_forks      = $(if ($repoInfo) { $repoInfo.forks_count } else { '' })
    })
}

if ($views -and $clones) {
    $dailyPath = Join-Path $out 'github-daily.csv'
    $byDate = [ordered]@{}
    if (Test-Path $dailyPath) { Import-Csv $dailyPath | ForEach-Object { $byDate[$_.date] = $_ } }
    $days = @($views.views | ForEach-Object { $_.timestamp.Substring(0, 10) }) + @($clones.clones | ForEach-Object { $_.timestamp.Substring(0, 10) }) | Sort-Object -Unique
    foreach ($d in $days) {
        $v = $views.views | Where-Object { $_.timestamp.StartsWith($d) } | Select-Object -First 1
        $c = $clones.clones | Where-Object { $_.timestamp.StartsWith($d) } | Select-Object -First 1
        $byDate[$d] = [pscustomobject]@{
            date          = $d
            views         = $(if ($v) { $v.count } else { 0 })
            view_uniques  = $(if ($v) { $v.uniques } else { 0 })
            clones        = $(if ($c) { $c.count } else { 0 })
            clone_uniques = $(if ($c) { $c.uniques } else { 0 })
        }
    }
    $byDate.Values | Sort-Object date | Export-Csv -Path $dailyPath -NoTypeInformation -Encoding UTF8
}

if ($refs) {
    $rows = @($refs | ForEach-Object { [pscustomobject]@{ snapshot_utc = $now; referrer = $_.referrer; views = $_.count; uniques = $_.uniques } })
    if ($rows.Count) { Add-Csv (Join-Path $out 'github-referrers.csv') $rows }
}

$summary = "$now subs=$(if ($pdx) { $pdx.subscriptions } else { '?' }) likes=$(if ($pdx) { $pdx.ratingsTotal } else { '?' }) gh_views14d=$(if ($views) { $views.count } else { '?' })"
if ($notes) { $summary += ' ERR ' + ($notes -join '; ') }
Add-Content -Path $log -Value $summary -Encoding UTF8
$summary
