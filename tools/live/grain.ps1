# Film grain check: in a running game (player standing still) takes pairs of full-resolution screenshots
# 50 ms apart with FilmGrain=1 and FilmGrain=0 (ini reload needs [Debug] Log=1) and reports how much the
# picture changes between the two frames - the grain is new noise every frame, a still scene without it
# changes (almost) nothing. Saves crops for a visual check.
param([string]$Out = "$PSScriptRoot\..\bin\grain")
. "$PSScriptRoot\..\env.ps1"
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -ReferencedAssemblies System.Drawing @'
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class Grab {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public static Bitmap Screen(int w, int h) { var b = new Bitmap(w, h, PixelFormat.Format24bppRgb); using (var g = Graphics.FromImage(b)) g.CopyFromScreen(0, 0, 0, 0, new Size(w, h)); return b; }
    public static double Diff(Bitmap a, Bitmap b) {
        var r = new Rectangle(0, 0, a.Width, a.Height);
        var da = a.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb); var db = b.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        int n = da.Stride * a.Height; var x = new byte[n]; var y = new byte[n];
        Marshal.Copy(da.Scan0, x, 0, n); Marshal.Copy(db.Scan0, y, 0, n); a.UnlockBits(da); b.UnlockBits(db);
        long s = 0; for (int i = 0; i < n; i++) s += Math.Abs(x[i] - y[i]); return (double)s / n;
    }
}
'@
[void][Grab]::SetProcessDPIAware()
New-Item -ItemType Directory -Force $Out | Out-Null
$sw = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Width; $sh = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Height
if (-not (Select-String -Path $Ini -Pattern '^FilmGrain=' -Quiet)) {
    (Get-Content $Ini) -replace '^\[Display\]', "[Display]`r`nFilmGrain=1" | Set-Content $Ini -Encoding ASCII
}
function WaitLine { $n = (Get-Content $Log).Count; while ((Get-Content $Log).Count -eq $n) { Start-Sleep -Milliseconds 100 } }
foreach ($g in 1, 0, 1) {
    (Get-Content $Ini) -replace '^FilmGrain=.*', "FilmGrain=$g" | Set-Content $Ini -Encoding ASCII
    WaitLine; WaitLine
    $d = @()
    for ($i = 0; $i -lt 3; $i++) {
        $a = [Grab]::Screen($sw, $sh); Start-Sleep -Milliseconds 50; $b = [Grab]::Screen($sw, $sh)
        $d += [Grab]::Diff($a, $b)
        if ($i -eq 0) {
            $c = $a.Clone([Drawing.Rectangle]::new([int]($sw / 2 - 300), [int]($sh / 2 - 200), 600, 400), $a.PixelFormat)
            $c.Save("$Out\grain_$g.png"); $c.Dispose()
        }
        $a.Dispose(); $b.Dispose(); Start-Sleep -Milliseconds 300
    }
    "FilmGrain={0}: mean change between frames {1}" -f $g, (($d | ForEach-Object { '{0:0.000}' -f $_ }) -join ' / ')
}
Get-Content $Log | Select-String 'film grain|ini reloaded' | Select-Object -Last 4 | ForEach-Object Line
