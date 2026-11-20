# Brings the Cities2 window to the front so bridge input steps (move, hover, cursor overlays) reach the game.
# It does nothing else: no input to the game, no window other than Cities2.
# Exit 0 = Cities2 is the foreground window. Exit 1 = Cities2 is not running. Exit 2 = focus failed.
# Allowed by a permission rule (exact command):
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<repo>/tools/testbridge/focus-game.ps1"
$ErrorActionPreference = 'Stop'

$p = Get-Process Cities2 -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
if (-not $p) { Write-Output 'focus-game: Cities2 is not running (or has no window)'; exit 1 }

Add-Type @"
using System;using System.Runtime.InteropServices;
public class SrmFocus{
[DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int c);
[DllImport("user32.dll")]public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);
[DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
}
"@

$h = $p.MainWindowHandle
# A tap of Alt lets this process take the foreground (Windows foreground lock rule).
[SrmFocus]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
[SrmFocus]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
[SrmFocus]::ShowWindow($h, 9) | Out-Null
[SrmFocus]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 300

if ([SrmFocus]::GetForegroundWindow() -eq $h) { Write-Output "focus-game: Cities2 in front (pid $($p.Id))"; exit 0 }
Write-Output 'focus-game: SetForegroundWindow did not take'
exit 2
