# Starts the game (optionally with a freshly built CoCHD.asi from out\obj) and keeps it in the foreground.
param([switch]$CopyAsi, [int]$Wait = 12)
. "$PSScriptRoot\..\env.ps1"
Get-Process CoCMainWin32 -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
if ($CopyAsi) { Copy-Item "$Project\out\obj\CoCHD.asi" "$Engine\scripts\" -Force }
Add-Type -Path "$Bin\Live.dll"
$g = Start-Process "$Engine\CoCMainWin32.exe" -WorkingDirectory $Engine -PassThru
for ($i = 0; $i -lt 80; $i++) {
    Start-Sleep -Milliseconds 100
    $g.Refresh()
    if ($g.MainWindowHandle -ne 0) { [Live]::Focus($g.MainWindowHandle) | Out-Null }
}
Start-Sleep $Wait
"foreground: $([Live]::Foreground() -eq $g.MainWindowHandle)"
