# Injects mouse counts in the running game and reports the view rotation CoCHD.asi logged for them.
# Example: measure.ps1 -Fps 30 -Fix 1 -Dx 1000   (Mode sens, Sens 2.0: expect 44.00 deg)
param([int]$Fps = 60, [int]$Fix = 1, [int]$Dx = 1000, [int]$Dy = 0, [string]$Mode = 'sens')
. "$PSScriptRoot\..\env.ps1"
Add-Type -Path "$Bin\Live.dll"
function WaitLine { $n = (Get-Content $Log).Count; while ((Get-Content $Log).Count -eq $n) { Start-Sleep -Milliseconds 100 } }
(Get-Content $Ini) -replace '^FPSLimit=.*', "FPSLimit=$Fps" -replace '^FrameIndependent=.*', "FrameIndependent=$Fix" -replace '^Mode=(sens|cm360|scale|off)$', "Mode=$Mode" |
    Set-Content $Ini -Encoding ASCII
# the ini is reloaded at a report line; measure over the next full 5 s window
WaitLine; WaitLine; WaitLine
[Live]::Move($Dx, $Dy, 40, 25)
WaitLine; WaitLine
$lines = Get-Content $Log | Select-Object -Last 6 | Where-Object { $_ -match 'fps|input' }
$yaw = 0; $pitch = 0
foreach ($l in $lines) { if ($l -match 'yaw (-?[\d.]+) deg pitch (-?[\d.]+)') { $yaw += [double]$Matches[1]; $pitch += [double]$Matches[2] } }
$fpsSeen = ($lines | Where-Object { $_ -match '^([\d.]+) fps' } | Select-Object -Last 1) -replace ' fps.*', ''
"mode {0,-5} fps {1,2} (measured {2}) fix {3}: {4} counts -> yaw {5:0.00} deg, {6} counts -> pitch {7:0.00} deg" -f $Mode, $Fps, $fpsSeen, $Fix, $Dx, $yaw, $Dy, $pitch
[Live]::Move(-$Dx, -$Dy, 20, 25)
WaitLine
