# Live test of menu navigation with the fake pad: main menu, d-pad down twice ("Optionen"), A (Enter) opens
# it, Start goes back. Screenshots after each step in tools\bin\menunav. Cleans up in any case.
. "$PSScriptRoot\..\env.ps1"
Add-Type -Path "$Bin\Live.dll"
$Out = "$Bin\menunav"; New-Item -ItemType Directory -Force $Out | Out-Null
$p = Start-Process $Setup -ArgumentList '/install', '/silent', "/dir=`"$Game`"" -Wait -PassThru
"install exit $($p.ExitCode)"
Copy-Item $Ini "$Bin\CoCHD.ini.user" -Force
(Get-Content $Ini) -replace '^Log=0', 'Log=1' | Set-Content $Ini -Encoding ASCII
Copy-Item "$Bin\xinput1_4.dll" $Engine -Force
$pad = "$Bin\pad.txt"
function Put($s) { for ($i = 0; $i -lt 20; $i++) { try { [IO.File]::WriteAllText($pad, $s); return } catch { Start-Sleep -Milliseconds 20 } } }
function Tap($buttons, $what) {
    Put "$buttons 0 0 0 0 0 0"; Start-Sleep -Milliseconds 150; Put '0000 0 0 0 0 0 0'; Start-Sleep -Milliseconds 700
    [Live]::Shot("$Out\$what.png", 1280, 720); $what
}
Put '0000 0 0 0 0 0 0'
Start-Process "$Bin\padctl.exe" -ArgumentList "`"$pad`"" -WindowStyle Hidden
try {
    & "$PSScriptRoot\..\live\restart.ps1" -Wait 15
    [Live]::Shot("$Out\0_start.png", 1280, 720)
    Tap '0002' '1_down'
    Tap '0002' '2_down'
    Tap '1000' '3_a'
    Start-Sleep 1
    Tap '0010' '4_start'
    Get-Content $Log | Select-String 'pad \d connected|pad buttons' | Select-Object -Last 6 | ForEach-Object Line
}
finally {
    Get-Process CoCMainWin32 -ErrorAction SilentlyContinue | Stop-Process -Force
    Put 'quit'
    Start-Sleep 2
    if (Test-Path $Log) { Copy-Item $Log "$Project\reference\CoCHD_menunav.log" -Force; Remove-Item $Log }
    Remove-Item "$Engine\xinput1_4.dll" -Force -ErrorAction SilentlyContinue
    Copy-Item "$Bin\CoCHD.ini.user" $Ini -Force
    "fake dll left: $(Test-Path "$Engine\xinput1_4.dll")"
}
