# End-to-end test of CoCHD_Setup.exe against a pristine copy of the GOG game folder.
#   e2e.ps1 -GameCopy <folder with an unpatched GOG install (at least Engine, Development\pcvideo, MainMenu)>
# Install -> verify -> update (settings must survive) -> uninstall -> byte-compare with the start state.
# The game's registry settings are saved before and restored afterwards.
param([Parameter(Mandatory)] [string]$GameCopy)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$setup = Join-Path $root 'out\CoCHD_Setup.exe'
$log = Join-Path $env:TEMP 'CoCHD_Setup.log'
$regKey = 'HKCU\Software\Bethesda Softworks\Call Of Cthulhu DCoTE'
$regBackup = Join-Path $env:TEMP 'cochd_e2e_registry.reg'
$script:fails = 0
function Check($ok, $what) { if ($ok) { "PASS  $what" } else { $script:fails++; "FAIL  $what" } }
function Snap($dir) {
    # device*.txt are written by the game itself on every start
    Get-ChildItem $dir -Recurse -File | Where-Object { $_.Name -notlike 'device*.txt' } | Sort-Object FullName |
        ForEach-Object { '{0}|{1}' -f $_.FullName.Substring($dir.Length), (Get-FileHash $_.FullName -Algorithm SHA1).Hash }
}
function Setup([string[]]$a) {
    $p = Start-Process $setup -ArgumentList $a -Wait -PassThru
    # an uninstall started from the game folder continues in a temporary copy
    Start-Sleep -Milliseconds 500
    while (Get-Process CoCHD_Uninstall -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }
    $p.ExitCode
}

reg export $regKey $regBackup /y | Out-Null
try {
    $before = Snap $GameCopy
    $exeBefore = (Get-FileHash "$GameCopy\Engine\CoCMainWin32.exe" -Algorithm SHA1).Hash

    "== install"
    $rc = Setup @('/install', '/silent', "/dir=`"$GameCopy`"", '/nodesktop', '/nostartmenu')
    Check ($rc -eq 0) "setup exit code 0 ($rc)"
    Get-Content $log -Encoding UTF8 | Select-String '^!!' | ForEach-Object { "      log: $_" }
    foreach ($f in Get-ChildItem "$root\payload\Engine" -File -Recurse) {
        $rel = $f.FullName.Substring("$root\payload\".Length)
        Check ((Get-FileHash $f.FullName).Hash -eq (Get-FileHash "$GameCopy\$rel").Hash) "installed $rel"
    }
    Check ((Get-FileHash "$root\out\obj\CoCHD.asi").Hash -eq (Get-FileHash "$GameCopy\Engine\scripts\CoCHD.asi").Hash) 'installed CoCHD.asi'
    Check ((Get-FileHash "$root\out\obj\CoCDCoTELauncher.exe").Hash -eq (Get-FileHash "$GameCopy\Engine\CoCDCoTELauncher.exe").Hash) 'launcher replaced'
    Check (Test-Path "$GameCopy\_HDPatch_Backup\Engine\CoCDCoTELauncher.exe") 'original launcher backed up'
    $a = [IO.File]::ReadAllBytes("$GameCopy\_HDPatch_Backup\Engine\CoCMainWin32.exe"); $b = [IO.File]::ReadAllBytes("$GameCopy\Engine\CoCMainWin32.exe")
    $d = @(0..($a.Length - 1) | Where-Object { $a[$_] -ne $b[$_] })
    Check ($d.Count -eq 1 -and ($b[$d[0]] -band 0x20) -and ($b[$d[0]] -bxor $a[$d[0]]) -eq 0x20) 'exe: only the Large Address Aware bit changed'
    Check (-not (Test-Path "$GameCopy\Development\pcvideo\beth_logo.wmv") -and (Test-Path "$GameCopy\_HDPatch_Backup\pcvideo\beth_logo.wmv")) 'logo videos moved aside'
    Check (Test-Path "$GameCopy\Development\pcvideo\credits.wmv") 'story videos stay'
    Check (Test-Path "$GameCopy\_HDPatch\CoCHD_Setup.exe") 'uninstaller copy'
    Check (Test-Path "$GameCopy\_HDPatch\README.txt") 'readme'
    Check (Test-Path "$GameCopy\Call of Cthulhu - HD Launcher.lnk") 'shortcut in game folder'
    $k = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CoCDCotEHDPatch' -ErrorAction SilentlyContinue
    Check ($k -and $k.InstallLocation -eq $GameCopy) 'Apps & Features entry'
    $s = Get-ItemProperty 'HKCU:\Software\Bethesda Softworks\Call Of Cthulhu DCoTE\Settings'
    Check ($s.MultisampleMode -eq 4 -and $s.VideoModeSet -eq 1 -and $s.ScreenWidth -le 3276) "registry defaults (MSAA $($s.MultisampleMode), $($s.ScreenWidth)x$($s.ScreenHeight))"

    "== launcher reads the installation"
    $snap = Join-Path $env:TEMP 'cochd_e2e_snap'
    if (Test-Path $snap) { Remove-Item $snap -Recurse -Force }
    Start-Process "$GameCopy\Engine\CoCDCoTELauncher.exe" -ArgumentList "/snapshot=$snap" -Wait
    Check ((Get-ChildItem $snap -Filter 'launcher_*.png').Count -eq 6) 'launcher renders all pages from the installed copy'

    "== update keeps settings"
    (Get-Content "$GameCopy\Engine\scripts\CoCHD.ini") -replace '^Sens=.*', 'Sens=4.2' | Set-Content "$GameCopy\Engine\scripts\CoCHD.ini"
    (Get-Content "$GameCopy\Engine\scripts\CallOfCthulhu.WidescreenFix.ini") -replace 'StretchFullscreenImages = 1', 'StretchFullscreenImages = 0' |
        Set-Content "$GameCopy\Engine\scripts\CallOfCthulhu.WidescreenFix.ini"
    $rc = Setup @('/install', '/silent', "/dir=`"$GameCopy`"", '/nodesktop', '/nostartmenu')
    Check ($rc -eq 0) "update exit code 0 ($rc)"
    Check ((Select-String -Path "$GameCopy\Engine\scripts\CoCHD.ini" -Pattern '^Sens=4.2$' -Quiet)) 'CoCHD.ini kept'
    Check ((Select-String -Path "$GameCopy\Engine\scripts\CallOfCthulhu.WidescreenFix.ini" -Pattern 'StretchFullscreenImages = 0' -Quiet)) 'widescreen fix ini kept'
    Check ((Get-FileHash "$GameCopy\_HDPatch_Backup\Engine\CoCMainWin32.exe" -Algorithm SHA1).Hash -eq $exeBefore) 'backup still holds the original exe'
    Check ((Get-FileHash "$GameCopy\_HDPatch_Backup\Engine\CoCDCoTELauncher.exe" -Algorithm SHA1).Hash -eq 'B890A414C525FB44DC0E24223F43849AE5AEBB8A') 'backup still holds the original launcher'

    "== uninstall (Apps & Features path: the copy in _HDPatch)"
    $p = Start-Process "$GameCopy\_HDPatch\CoCHD_Setup.exe" -ArgumentList '/uninstall', '/silent' -Wait -PassThru
    Start-Sleep -Milliseconds 500
    while (Get-Process CoCHD_Uninstall -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }
    Get-Content $log -Encoding UTF8 | Select-String '^!!' | ForEach-Object { "      log: $_" }
    $after = Snap $GameCopy
    $diff = Compare-Object $before $after | ForEach-Object { '{0} {1}' -f $_.SideIndicator, $_.InputObject }
    Check (@($diff).Count -eq 0) 'game folder byte-identical to the start'
    $diff | ForEach-Object { "      $_" }
    Check (-not (Test-Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CoCDCotEHDPatch')) 'Apps & Features entry removed'
    Start-Sleep 4
    Check (-not (Test-Path (Join-Path $env:TEMP 'CoCHD_Uninstall.exe'))) 'temporary uninstaller removed itself'
}
finally {
    # reg.exe reports success on stderr: run it through cmd so PowerShell does not treat that as an error
    cmd /c "reg delete `"$regKey`" /f >nul 2>nul & reg import `"$regBackup`" >nul 2>nul"
    "(game registry settings restored)"
}
"== $(if ($script:fails) { "$($script:fails) FAILED" } else { 'ALL PASSED' })"
exit $script:fails
