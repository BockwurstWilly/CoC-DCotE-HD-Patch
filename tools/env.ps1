# Shared paths for the test tools. Game folder: $env:COC_GAME, else the GOG registry entry.
$Project = Split-Path $PSScriptRoot
$Game = $env:COC_GAME
if (-not $Game) {
    foreach ($k in 'HKLM:\SOFTWARE\WOW6432Node\GOG.com\Games\1189711155', 'HKLM:\SOFTWARE\GOG.com\Games\1189711155') {
        $v = Get-ItemProperty $k -ErrorAction SilentlyContinue
        if ($v -and $v.path) { $Game = $v.path; break }
    }
}
if (-not $Game) { throw 'Game folder not found: set $env:COC_GAME' }
$Engine = Join-Path $Game 'Engine'
$Ini = "$Engine\scripts\CoCHD.ini"
$Log = "$Engine\scripts\CoCHD.log"
$Bin = Join-Path $PSScriptRoot 'bin'
$Setup = Join-Path $Project 'out\CoCHD_Setup.exe'
