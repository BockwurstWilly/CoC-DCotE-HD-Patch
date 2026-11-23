# Live controller test with the fake pad: installs the current build, starts the game, walks into the
# prologue and runs a scenario. Cleans up in any case (fake DLL removed, user ini restored, game closed).
#   -Scenario speed      walk at 100 / 58 / 40 % tilt (distance per frame)
#   -Scenario direction  0 / 10 / 30 / 45 / 90 deg stick angle (heading vs view yaw), then AnalogWalk=0
# Needs tools\build-tools.ps1 once and the user's OK to take over the screen (about 6 minutes).
param([ValidateSet('speed', 'direction')] [string]$Scenario = 'direction')
. "$PSScriptRoot\..\env.ps1"
$p = Start-Process $Setup -ArgumentList '/install', '/silent', "/dir=`"$Game`"" -Wait -PassThru
"install exit $($p.ExitCode)"
Copy-Item $Ini "$Bin\CoCHD.ini.user" -Force
(Get-Content $Ini) -replace '^Log=0', 'Log=1' -replace '^Mode=fullscreen', 'Mode=borderless' | Set-Content $Ini -Encoding ASCII
if (-not (Select-String -Path $Ini -Pattern '^AnalogWalk' -Quiet)) {
    (Get-Content $Ini) -replace '^\[Pad\]', "[Pad]`r`nAnalogWalk=1" | Set-Content $Ini -Encoding ASCII
}
Copy-Item "$Bin\xinput1_4.dll" $Engine -Force
[IO.File]::WriteAllText("$Bin\pad.txt", '0000 0 0 0 0 0 0')
Start-Process "$Bin\padctl.exe" -ArgumentList "`"$Bin\pad.txt`"" -WindowStyle Hidden
try {
    & "$PSScriptRoot\..\live\restart.ps1"
    & "$PSScriptRoot\..\live\toGame.ps1"
    Get-Content $Log | Select-String 'walk direction'
    if ($Scenario -eq 'speed') {
        $cases = @(@(0, 32767, '100 %'), @(0, 19000, '58 %'), @(0, 13000, '40 %'))
    } else {
        $cases = @(@(0, 32767, 'straight'), @(5690, 32270, '10 deg (snap)'), @(16383, 28377, '30 deg'),
                   @(23170, 23170, '45 deg'), @(32767, 0, 'right'), @(16383, 28377, '30 deg, AnalogWalk=0'))
    }
    foreach ($t in $cases) {
        if ($t[2] -like '*AnalogWalk=0') {
            (Get-Content $Ini) -replace '^AnalogWalk=1', 'AnalogWalk=0' | Set-Content $Ini -Encoding ASCII
            Start-Sleep 6
        }
        & "$PSScriptRoot\padtest.ps1" "0000 $($t[0]) $($t[1]) 0 0 0 0" 2000 $t[2]
        Get-Content $Log | Select-String 'walked [1-9]' | Select-Object -Last 1
        & "$PSScriptRoot\padtest.ps1" "0000 $(-$t[0]) $(-$t[1]) 0 0 0 0" 2000 'back'
    }
}
finally {
    Get-Process CoCMainWin32 -ErrorAction SilentlyContinue | Stop-Process -Force
    [IO.File]::WriteAllText("$Bin\pad.txt", 'quit')
    Start-Sleep 2
    New-Item -ItemType Directory -Force "$Project\reference" | Out-Null
    if (Test-Path $Log) {
        Copy-Item $Log "$Project\reference\CoCHD_pad_$Scenario.log" -Force
        Remove-Item $Log
    }
    Remove-Item "$Engine\xinput1_4.dll" -Force -ErrorAction SilentlyContinue
    Copy-Item "$Bin\CoCHD.ini.user" $Ini -Force
    "fake dll left: $(Test-Path "$Engine\xinput1_4.dll")"
}
