# Builds the test helpers into tools\bin (not part of the repository):
#   Live.dll       focus, screenshot, relative mouse, scan-code keys (SendInput)
#   padctl.exe     copies pad.txt into the shared memory the fake pad reads
#   xinput1_4.dll  fake XInput for tests - copied into the game only while a test runs
. "$PSScriptRoot\env.ps1"
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Bin | Out-Null
$csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
& $csc /nologo /target:library /out:"$Bin\Live.dll" /r:System.Drawing.dll "$PSScriptRoot\live\Live.cs"
if ($LASTEXITCODE) { throw 'Live.dll failed' }
& $csc /nologo /out:"$Bin\padctl.exe" "$PSScriptRoot\fakepad\padctl.cs"
if ($LASTEXITCODE) { throw 'padctl.exe failed' }
$vc = Get-ChildItem 'C:\Program Files*\Microsoft Visual Studio\*\*\VC\Auxiliary\Build\vcvars32.bat' | Select-Object -First 1
cmd /c "call `"$($vc.FullName)`" >nul 2>nul && cd /d `"$Bin`" && cl /nologo /LD /O1 `"$PSScriptRoot\fakepad\fakepad.c`" /link /DEF:`"$PSScriptRoot\fakepad\fakepad.def`" /OUT:xinput1_4.dll >nul"
if ($LASTEXITCODE) { throw 'xinput1_4.dll failed' }
Remove-Item "$Bin\*.obj", "$Bin\*.exp", "$Bin\*.lib" -ErrorAction SilentlyContinue
"tools built in $Bin"
