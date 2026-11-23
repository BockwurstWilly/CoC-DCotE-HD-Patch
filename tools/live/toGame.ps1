# From a fresh game start: main menu "Weiter" -> tips "Weiter" -> wait for the prologue cutscene to end.
# Menu positions are given for 3200x1800 and scaled to the resolution CoCHD.asi chose (see its log):
# with the monitor switched off Windows lists fewer modes and auto picks a smaller one.
param([int]$CutsceneWait = 150)
. "$PSScriptRoot\..\env.ps1"
Add-Type -Path "$Bin\Live.dll"
$w = 3200; $h = 1800
$m = Select-String -Path $Log -Pattern '-> game (\d+)x(\d+)' | Select-Object -First 1
if ($m) { $w = [int]$m.Matches[0].Groups[1].Value; $h = [int]$m.Matches[0].Groups[2].Value }
$sx = $w / 3200.0; $sy = $h / 1800.0
"menu scale for ${w}x${h}"
# the menu cursor starts at (0,0) in back buffer pixels and moves 1 pixel per count
[Live]::Move([int](1600 * $sx), [int](710 * $sy), 40, 16); Start-Sleep -Milliseconds 300; [Live]::Click()
Start-Sleep 30
[Live]::Move(0, [int](497 * $sy), 20, 16); Start-Sleep -Milliseconds 300; [Live]::Click()
Start-Sleep $CutsceneWait
Get-Content $Log | Select-Object -Last 2
