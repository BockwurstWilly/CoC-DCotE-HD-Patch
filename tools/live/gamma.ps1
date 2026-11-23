# Gamma ramp check (needs [Debug] Log=1 so the ini is re-read live): in the running game, takes screenshot pairs
# with [Display] GameGamma=0 and =1 a moment apart and compares brightness levels; then an Alt+Tab round trip.
# The game's ramp (brightness option) brightens the mid tones, so GameGamma=1 must raise the mean.
param([int]$Pairs = 3)
. "$PSScriptRoot\..\env.ps1"
Add-Type -Path "$Bin\Live.dll"
Add-Type -ReferencedAssemblies System.Drawing @'
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class Lv {
    public static string Stats(string path) {
        using (var b = new Bitmap(path)) {
            var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var x = new byte[d.Stride * b.Height]; Marshal.Copy(d.Scan0, x, 0, x.Length); b.UnlockBits(d);
            var hist = new long[256]; double sum = 0; long n = 0;
            for (int y = 0; y < b.Height * 85 / 100; y += 2) for (int i = 0; i < b.Width; i += 2) {
                int o = y * d.Stride + i * 3; int l = (x[o + 2] * 299 + x[o + 1] * 587 + x[o] * 114) / 1000; hist[l]++; sum += l; n++; }
            long a = 0; int p2 = 0, p50 = 0, p98 = 0;
            for (int i = 0; i < 256; i++) { a += hist[i]; if (a < n * 0.02) p2 = i; if (a < n * 0.5) p50 = i; if (a < n * 0.98) p98 = i; }
            return string.Format("mean {0,5:0.0}  dark(2%) {1,3}  median {2,3}  bright(98%) {3,3}", sum / n, p2, p50, p98);
        }
    }
}
'@
$Out = "$Bin\gamma"; New-Item -ItemType Directory -Force $Out | Out-Null
function SetGamma($v) {
    $lines = Get-Content $Ini
    if (-not ($lines -match '^GameGamma=')) { $lines = $lines -replace '^\[Display\]', "[Display]`r`nGameGamma=1" }
    for ($i = 0; $i -lt 20; $i++) { try { [IO.File]::WriteAllLines($Ini, [string[]]($lines -replace '^GameGamma=.*', "GameGamma=$v")); return } catch { Start-Sleep -Milliseconds 30 } }
}
for ($p = 0; $p -lt $Pairs; $p++) {
    foreach ($v in 0, 1) {
        SetGamma $v; Start-Sleep -Milliseconds 700
        [Live]::Shot("$Out\gamma_${p}_$v.png", 1920, 1080)
        "pair $p GameGamma=$v  " + [Lv]::Stats("$Out\gamma_${p}_$v.png")
    }
}
SetGamma 1
Get-Content $Log | Select-String 'gamma ramp emulation|gamma ramp:' | Select-Object -First 4 | ForEach-Object Line
& "$PSScriptRoot\alttab.ps1" | Select-Object -First 2
Get-Content $Log | Select-String 'fps \(' | Select-Object -Last 2 | ForEach-Object Line
