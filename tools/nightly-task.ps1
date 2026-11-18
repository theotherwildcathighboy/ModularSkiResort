<#
.SYNOPSIS
  Registers the Windows scheduled task "SRM nightly check" (tools\nightly.ps1 at 03:00). It is created DISABLED. The owner enables it.

  -Create    register (or replace) the task, disabled        -Enable / -Disable   switch it        -Remove   delete it       -Status   show it
  -Time HH:mm  start time (default 03:00)
  The task runs as the current user, only while that user is logged on (the game needs the desktop session), never wakes the machine, 4 hours
  at most, one instance. nightly.ps1 itself does nothing when Cities2 runs or the game lock is held, so a night the owner plays is skipped.
  The commands the owner needs:
      powershell -NoProfile -ExecutionPolicy Bypass -File tools\nightly-task.ps1 -Enable
      powershell -NoProfile -ExecutionPolicy Bypass -File tools\nightly-task.ps1 -Disable
#>
param([switch]$Create, [switch]$Enable, [switch]$Disable, [switch]$Remove, [switch]$Status, [string]$Time = '03:00')

$ErrorActionPreference = 'Stop'
$Name = 'SRM nightly check'
$script = Join-Path $PSScriptRoot 'nightly.ps1'

if ($Create) {
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`"" -WorkingDirectory (Split-Path -Parent $PSScriptRoot)
    $trigger = New-ScheduledTaskTrigger -Daily -At $Time
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 4) -MultipleInstances IgnoreNew -DontStopIfGoingOnBatteries -AllowStartIfOnBatteries -StartWhenAvailable:$false
    $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName $Name -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description 'tools\nightly.ps1: main over the pack, baseline loadcheck + 2-month soak, pristine world restored. Created disabled.' -Force | Out-Null
    Disable-ScheduledTask -TaskName $Name | Out-Null
    Write-Output "created '$Name' at $Time, DISABLED. Enable: tools\nightly-task.ps1 -Enable"
}
if ($Enable) { Enable-ScheduledTask -TaskName $Name | Out-Null; Write-Output "'$Name' enabled" }
if ($Disable) { Disable-ScheduledTask -TaskName $Name | Out-Null; Write-Output "'$Name' disabled" }
if ($Remove) { Unregister-ScheduledTask -TaskName $Name -Confirm:$false; Write-Output "'$Name' removed" }
if ($Status -or -not ($Create -or $Enable -or $Disable -or $Remove)) {
    $t = Get-ScheduledTask -TaskName $Name -ErrorAction SilentlyContinue
    if (-not $t) { Write-Output "no task '$Name'" } else { $i = Get-ScheduledTaskInfo -TaskName $Name; Write-Output "'$Name': $($t.State), next $($i.NextRunTime), last $($i.LastRunTime), last result $($i.LastTaskResult)" }
}
