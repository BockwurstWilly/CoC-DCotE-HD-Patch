# Builds the Call of Cthulhu: Dark Corners of the Earth HD Patch: CoCHD.asi (MSVC x86),
# CoCDCoTELauncher.exe (the new launcher) and CoCHD_Setup.exe (C#, .NET Framework 4).
# Output: out\CoCHD_Setup.exe (single file, everything embedded) and out\release\ (package for upload)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$payload = Join-Path $root 'payload'
$out = Join-Path $root 'out'
$obj = Join-Path $out 'obj'
New-Item -ItemType Directory -Force $obj | Out-Null

$vcvars = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars32.bat'
$csc = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
$fw = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319'

function Invoke-Checked([string]$what, [scriptblock]$cmd) {
    & $cmd
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

# --- CoCHD.asi ----------------------------------------------------------------------------------
Write-Host '== CoCHD.asi'
Invoke-Checked 'CoCHD.asi' {
    cmd /c "call `"$vcvars`" >nul 2>nul && cd /d `"$obj`" && rc /nologo /fo cochd.res `"$src\asi\cochd.rc`" && cl /nologo /W3 /WX /O1 /LD /GS- /MT /D_CRT_SECURE_NO_WARNINGS `"$src\asi\cochd.c`" cochd.res /link winmm.lib user32.lib kernel32.lib shell32.lib setupapi.lib hid.lib /OUT:CoCHD.asi"
}

# --- icon (generated, so the package carries no game artwork): an elder sign on dark green ------
$ico = Join-Path $obj 'cochd.ico'
Add-Type -AssemblyName System.Drawing
$frames = foreach ($size in 256, 48, 32, 16) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::FromArgb(255, 14, 24, 21))
    $c = $size / 2.0
    $brass = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 196, 160, 96)), ([Math]::Max(1.2, $size / 18))
    # five-pointed star
    $pts = for ($i = 0; $i -lt 5; $i++) {
        $a = -[Math]::PI / 2 + $i * 4 * [Math]::PI / 5
        New-Object System.Drawing.PointF ([float]($c + [Math]::Cos($a) * $size * 0.40)), ([float]($c + [Math]::Sin($a) * $size * 0.40))
    }
    $g.DrawPolygon($brass, [System.Drawing.PointF[]]$pts)
    # the flame-eye in the middle
    $green = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 112, 176, 146))
    $r = $size * 0.09
    $g.FillEllipse($green, [float]($c - $r), [float]($c - $r * 1.6), [float]($r * 2), [float]($r * 3.2))
    $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 70, 84, 72)), ([Math]::Max(1, $size / 32))
    $g.DrawRectangle($edge, 0, 0, $size - 1, $size - 1)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , @($size, $ms.ToArray())
}
$fs = [IO.File]::Create($ico)
$w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = if ($f[0] -ge 256) { 0 } else { $f[0] }
    $w.Write([byte]$s); $w.Write([byte]$s); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$f[1].Length); $w.Write([uint32]$offset)
    $offset += $f[1].Length
}
foreach ($f in $frames) { $w.Write($f[1]) }
$w.Close()

# --- C# ---------------------------------------------------------------------------------------
$refs = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'Microsoft.CSharp.dll') |
    ForEach-Object { "/reference:$fw\$_" }
$common = Get-ChildItem "$src\common\*.cs" | ForEach-Object { $_.FullName }
$cscBase = @('/nologo', '/target:winexe', '/optimize+', '/warnaserror+', '/platform:anycpu', '/langversion:7.3',
             "/win32manifest:$src\app.manifest", "/win32icon:$ico", '/utf8output') + $refs

Write-Host '== CoCDCoTELauncher.exe'
$launcher = Join-Path $obj 'CoCDCoTELauncher.exe'
Invoke-Checked 'launcher' { & $csc @cscBase "/out:$launcher" @common (Get-ChildItem "$src\launcher\*.cs" | ForEach-Object { $_.FullName }) }

Write-Host '== CoCHD_Setup.exe'
# payload\Engine\... is installed into the game folder as is; the asi and launcher come from the build
$resources = @()
foreach ($f in Get-ChildItem "$payload\Engine" -File -Recurse) {
    $rel = $f.FullName.Substring($payload.Length + 1).Replace('\', '/')
    $resources += "/resource:$($f.FullName),payload/$rel"
}
$resources += "/resource:$obj\CoCHD.asi,payload/Engine/scripts/CoCHD.asi"
$resources += "/resource:$launcher,payload/Engine/CoCDCoTELauncher.exe"
$resources += "/resource:$root\README.txt,payload/doc/README.txt"
$resources += "/resource:$root\THIRD-PARTY.txt,payload/doc/THIRD-PARTY.txt"
foreach ($f in Get-ChildItem "$payload\licenses" -File) { $resources += "/resource:$($f.FullName),payload/doc/licenses/$($f.Name)" }
$setup = Join-Path $out 'CoCHD_Setup.exe'
Invoke-Checked 'setup' { & $csc @cscBase "/out:$setup" @common (Get-ChildItem "$src\setup\*.cs" | ForEach-Object { $_.FullName }) @resources }

$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
Write-Host ("== done: {0} ({1:N1} MB) SHA256 {2}" -f $setup, ((Get-Item $setup).Length / 1MB), $hash)

# --- release package ----------------------------------------------------------------------------
$version = ([regex]::Match((Get-Content "$src\common\Text.cs" -Raw), 'Text = "([^"]+)"')).Groups[1].Value
$name = "CoCHD_Patch_$version"
$rel = Join-Path $out "release\$name"
if (Test-Path $rel) { Remove-Item $rel -Recurse -Force }
New-Item -ItemType Directory -Force "$rel\Lizenzen - Licenses" | Out-Null
Copy-Item $setup "$rel\CoCHD_Setup.exe"
Copy-Item "$root\README.txt" "$rel\Liesmich - Readme.txt"
Copy-Item "$root\THIRD-PARTY.txt" "$rel\Lizenzen - Licenses\THIRD-PARTY.txt"
Copy-Item "$payload\licenses\*" "$rel\Lizenzen - Licenses\"
Copy-Item "$root\LICENSE" "$rel\Lizenzen - Licenses\HD-Patch-License.txt"
"$hash *CoCHD_Setup.exe" | Set-Content "$rel\CoCHD_Setup.exe.sha256" -Encoding ASCII
$zip = Join-Path $out "release\$name.zip"
if (Test-Path $zip) { Remove-Item $zip }
# entries with forward slashes (Compress-Archive in PowerShell 5.1 writes backslashes, which other tools reject)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$za = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    foreach ($f in Get-ChildItem $rel -Recurse -File) {
        $entry = $name + '/' + $f.FullName.Substring($rel.Length + 1).Replace([char]92, [char]47)
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $f.FullName, $entry, 'Optimal')
    }
}
finally { $za.Dispose() }
Write-Host ("== release: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
