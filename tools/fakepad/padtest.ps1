# Holds a fake pad state for a time inside one log window and reports what CoCHD.asi logged for it.
# State: "<buttons hex> <lx> <ly> <rx> <ry> <lt> <rt>" (sticks -32768..32767, triggers 0..255).
param([string]$State, [int]$Ms, [string]$What)
. "$PSScriptRoot\..\env.ps1"
$pad = "$Bin\pad.txt"
function WaitLine { $n = (Get-Content $Log).Count; while ((Get-Content $Log).Count -eq $n) { Start-Sleep -Milliseconds 100 } }
function Put($s) { for ($i = 0; $i -lt 20; $i++) { try { [IO.File]::WriteAllText($pad, $s); return } catch { Start-Sleep -Milliseconds 20 } } }
WaitLine; WaitLine
Put $State
Start-Sleep -Milliseconds $Ms
Put '0000 0 0 0 0 0 0'
WaitLine; WaitLine
$lines = Get-Content $Log | Select-Object -Last 8
$yaw = 0; $pitch = 0; $keys = 0; $mode = ''
foreach ($l in $lines) {
    if ($l -match 'yaw (-?[\d.]+) deg pitch (-?[\d.]+) deg \((\w+)\)') { $yaw += [double]$Matches[1]; $pitch += [double]$Matches[2]; $mode += $Matches[3] + ' ' }
    if ($l -match '(\d+) key-down samples') { $keys += [int]$Matches[1] }
}
"{0,-28} yaw {1,8:0.00}  pitch {2,7:0.00}  key-down samples {3,4}  ({4})" -f $What, $yaw, $pitch, $keys, $mode.Trim()
