<#
.SYNOPSIS
  Playsets through the game's own Paradox SDK (bridge op `playset`, T221). Needs the game RUNNING with the test bridge READY: the SDK
  context only exists inside the game. This script never reads or writes playset_config.json.

.DESCRIPTION
  -Action List                 every local playset (id, name, mods, which is active)
  -Action Active               the active playset
  -Action Activate -Name <n>   make <n> the active playset (any name; -Id <id> when two share a name). The mod set loads at the game's start:
                               QUIT AND RESTART the game after a switch (exit 3 says so).
  -Action Ensure -Name <n>     Activate only if <n> is not active already. Exit 0 = it was active, 3 = switched, restart needed.
  -Action Clone -From <n> -Name "SRM Test ..."   copy an existing playset into a new one (an existing "SRM Test ..." of that name is reused)
  -Action Create -Name "SRM Test ..."            an empty playset
  -Action Enable -ModId <id> -On true|false    enable or disable ONE mod in the ACTIVE playset (only an "SRM Test ..." playset)
  The bridge refuses to create anything not named "SRM Test ..." and has no delete, rename or edit: the owner's playsets are never touched.
  A new playset syncs to the owner's Paradox account like any playset made in the Paradox Mods screen.

  Typical: world.ps1 -World Asset says which playset is wanted; the recipe for the Asset world's playset:
    playset.ps1 -Action Clone -From "Standard Mod Set" -Name "SRM Test Asset"; playset.ps1 -Action Activate -Name "SRM Test Asset"
    playset.ps1 -Action Enable -ModId 167215 -On false     (then quit; start; world.ps1 -World Status)

  Exit: 0 ok, 1 the bridge op failed, 2 the game / bridge is not there, 3 ok but a restart is needed.
#>
param(
    [ValidateSet('List', 'Active', 'Activate', 'Ensure', 'Clone', 'Create', 'Enable')][string]$Action = 'List',
    [string]$Name,
    [string]$Id,
    [string]$From,
    [string]$ModId,
    [string]$On = 'true',
    [int]$TimeoutSec = 90
)

$ErrorActionPreference = 'Stop'
$CallJs = Join-Path $PSScriptRoot 'testbridge\bridge-call.mjs'
if (-not (Get-Process Cities2 -ErrorAction SilentlyContinue)) { Write-Output 'playset: Cities2 is not running (the SDK lives in the game). Start it with the bridge, wait for READY.'; exit 2 }

function Call-Op($request) {
    $json = $request | ConvertTo-Json -Compress
    $raw = & node $CallJs --op ([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)))
    if ($LASTEXITCODE -eq 2) { Write-Output "playset: bridge unreachable: $raw"; exit 2 }
    $text = ($raw | ForEach-Object { "$_" }) -join "`n"
    try { return $text | ConvertFrom-Json } catch { Write-Output "playset: bridge output is not JSON: $text"; exit 2 }
}

function Run-Job($request) {
    $r = Call-Op $request
    if (-not $r.ok) { Write-Output "playset: refused: $($r.error)"; exit 1 }
    $t0 = Get-Date
    while ($true) {
        Start-Sleep -Milliseconds 700
        $s = Call-Op @{ op = 'playset'; action = 'status' }
        if ($s.state -eq 'done') { return $s.result }
        if ($s.state -eq 'failed') { Write-Output "playset: failed: $($s.error)"; exit 1 }
        if (((Get-Date) - $t0).TotalSeconds -gt $TimeoutSec) { Write-Output "playset: no result after $TimeoutSec s"; exit 1 }
    }
}

function Show-Active($res) { if ($res.active) { Write-Output "active: '$($res.active.name)' ($($res.active.id), $($res.active.mods) mods)" } else { Write-Output 'active: none' } }

switch ($Action) {
    'List' {
        $res = Run-Job @{ op = 'playset'; action = 'list' }
        foreach ($p in $res.playsets) { Write-Output ("{0} {1,-40} {2,4} mods  {3}  {4}" -f $(if ($p.active) { '*' } else { ' ' }), $p.name, $p.mods, $p.ownership, $p.id) }
    }
    'Active' { Show-Active (Run-Job @{ op = 'playset'; action = 'active' }) }
    'Activate' {
        if (-not $Name -and -not $Id) { Write-Output 'playset: -Name or -Id'; exit 1 }
        $res = Run-Job @{ op = 'playset'; action = 'activate'; name = $Name; id = $Id }
        if ($res.activated) { Write-Output "activated: '$($res.activated.name)'. QUIT AND RESTART the game before any test."; exit 3 }
        Write-Output "$($res.note): '$($res.active.name)'"
    }
    'Ensure' {
        if (-not $Name) { Write-Output 'playset: -Name'; exit 1 }
        $res = Run-Job @{ op = 'playset'; action = 'active' }
        if ($res.active.name -eq $Name) { Write-Output "playset OK: '$Name' is active"; exit 0 }
        $res = Run-Job @{ op = 'playset'; action = 'activate'; name = $Name }
        Write-Output "activated: '$($res.activated.name)' (was '$($res.active.name)'). QUIT AND RESTART the game before any test."
        exit 3
    }
    'Clone' {
        if (-not $From -or -not $Name) { Write-Output 'playset: -From and -Name'; exit 1 }
        $res = Run-Job @{ op = 'playset'; action = 'clone'; from = $From; name = $Name }
        if ($res.reused) { Write-Output "reused existing '$($res.reused.name)' ($($res.reused.id))" } else { Write-Output "created '$($res.created.name)' ($($res.created.id)) from '$($res.clonedFrom)'" }
    }
    'Create' {
        if (-not $Name) { Write-Output 'playset: -Name'; exit 1 }
        $res = Run-Job @{ op = 'playset'; action = 'create'; name = $Name }
        if ($res.reused) { Write-Output "reused existing '$($res.reused.name)' ($($res.reused.id))" } else { Write-Output "created '$($res.created.name)' ($($res.created.id))" }
    }
    'Enable' {
        if (-not $ModId) { Write-Output 'playset: -ModId'; exit 1 }
        $res = Run-Job @{ op = 'playset'; action = 'enable'; modId = $ModId; enable = ($On -match '^(1|true|yes|on)$') }
        Write-Output "mod $($res.mod) $(if ($res.enabled) { 'enabled' } else { 'disabled' }) in '$($res.playset)'. Restart the game."
        exit 3
    }
}
exit 0
