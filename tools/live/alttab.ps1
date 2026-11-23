# Alt+Tab away from the game, come back, report whether it survived.
. "$PSScriptRoot\..\env.ps1"
Add-Type -Path "$Bin\Live.dll"
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class K { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra); }
'@
[K]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 50
[K]::keybd_event(0x09, 0, 0, [UIntPtr]::Zero); [K]::keybd_event(0x09, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 100; [K]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
Start-Sleep 6
"alive while away: $([bool](Get-Process CoCMainWin32 -ErrorAction SilentlyContinue))"
$g = Get-Process CoCMainWin32 -ErrorAction SilentlyContinue
if ($g) { [Live]::Focus($g.MainWindowHandle) | Out-Null; Start-Sleep 10 }
"alive after return: $([bool](Get-Process CoCMainWin32 -ErrorAction SilentlyContinue))"
Get-Content $Log | Select-Object -Last 8
