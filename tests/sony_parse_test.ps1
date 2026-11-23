# Builds and runs the offline PlayStation report parser test (no controller needed).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$obj = Join-Path $root 'out\obj'
New-Item -ItemType Directory -Force $obj | Out-Null
$vc = Get-ChildItem 'C:\Program Files*\Microsoft Visual Studio\*\*\VC\Auxiliary\Build\vcvars32.bat' | Select-Object -First 1
cmd /c "call `"$($vc.FullName)`" >nul 2>nul && cd /d `"$obj`" && cl /nologo /W3 /WX `"$PSScriptRoot\sony_parse_test.c`" /Fe:sony_parse_test.exe >nul"
if ($LASTEXITCODE) { throw 'build failed' }
& "$obj\sony_parse_test.exe"
exit $LASTEXITCODE
