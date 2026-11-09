<#
.SYNOPSIS
  Waits until SkiTestBridge reports READY in its log, without one call into the game's UI (README "Wait for ready").

.DESCRIPTION
  The bridge samples its readiness parts twice a second and logs each change as a READY or NOT READY line
  (TestBridgeSystem.Ready.cs). This script reads only SkiTestBridge.log. It sends nothing to the game, so it is safe
  while the UI loads or reloads, which is the window the first bridge or game_eval call must avoid.

  Exit codes: 0 ready, 1 timeout, 2 the game is not running.

.PARAMETER Since
  Only log lines at or after this time count. Default: the start time of the running Cities2 process.
  After a save load or a "return to menu", pass the time the load began: (Get-Date) taken before the click.

.PARAMETER TimeoutSec
  Default 600.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\testbridge\wait-ready.ps1
#>
param(
    [datetime]$Since,
    [int]$TimeoutSec = 600
)

$log = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\SkiTestBridge.log'
$game = Get-Process Cities2 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $game) { Write-Output 'wait-ready: Cities2 is not running'; exit 2 }
if (-not $PSBoundParameters.ContainsKey('Since')) { $Since = $game.StartTime }

$pattern = '^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d),(\d{3})\] \[INFO\]\s+(READY|NOT READY|MODE) '
$start = Get-Date
while ($true) {
    if (-not (Get-Process -Id $game.Id -ErrorAction SilentlyContinue)) { Write-Output 'wait-ready: Cities2 exited'; exit 2 }
    $last = $null
    $mode = $null
    if (Test-Path $log) {
        # The game holds the log open for writing: read it shared.
        $fs = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
        try { $text = (New-Object System.IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
        foreach ($line in $text -split "`r?`n") {
            $m = [regex]::Match($line, $pattern)
            if (-not $m.Success) { continue }
            $t = [datetime]::ParseExact($m.Groups[1].Value, 'yyyy-MM-dd HH:mm:ss', $null).AddMilliseconds([int]$m.Groups[2].Value)
            if ($t -lt $Since) { continue }
            if ($m.Groups[3].Value -eq 'MODE') { $mode = $t } else { $last = @{ kind = $m.Groups[3].Value; time = $t; line = $line } }
        }
    }
    if ($last -and $last.kind -eq 'READY' -and (-not $mode -or $last.time -ge $mode)) {
        $after = if ($mode) { ' ({0:N1} s after the MODE line)' -f ($last.time - $mode).TotalSeconds } else { '' }
        Write-Output ("wait-ready: READY at {0:HH:mm:ss.fff}{1}" -f $last.time, $after)
        Write-Output $last.line
        exit 0
    }
    if (((Get-Date) - $start).TotalSeconds -ge $TimeoutSec) {
        Write-Output "wait-ready: timeout after $TimeoutSec s"
        if ($last) { Write-Output $last.line }
        exit 1
    }
    Start-Sleep -Milliseconds 1000
}
