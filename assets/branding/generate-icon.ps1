# Generates the Astra Finance application icon (multi-resolution .ico with PNG frames).
# Usage (Windows PowerShell 5.1):  powershell -ExecutionPolicy Bypass -File assets/branding/generate-icon.ps1
param(
    [string]$Output = "$PSScriptRoot\..\..\src\AstraFinance.App\Resources\astra.ico"
)

Add-Type -AssemblyName System.Drawing

$coral = [System.Drawing.Color]::FromArgb(255, 0xF4, 0x80, 0x6B)
$ink   = [System.Drawing.Color]::FromArgb(255, 0x15, 0x17, 0x1C)
$sizes = 16, 24, 32, 48, 64, 128, 256

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded square (radius = 22% of size)
    $r = [single]($size * 0.22); $d = 2 * $r; $s = [single]($size - 1)
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bg.AddArc(0, 0, $d, $d, 180, 90)
    $bg.AddArc($s - $d, 0, $d, $d, 270, 90)
    $bg.AddArc($s - $d, $s - $d, $d, $d, 0, 90)
    $bg.AddArc(0, $s - $d, $d, $d, 90, 90)
    $bg.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $coral), $bg)

    # Four-point "astra" star on a 24-unit grid (matches Brand.Star in Icons.xaml), inset to 64%.
    $k = $size / 24.0 * 0.64; $o = $size * 0.18
    function P([double]$x, [double]$y) { New-Object System.Drawing.PointF ([single]($o + $x * $k)), ([single]($o + $y * $k)) }
    $star = New-Object System.Drawing.Drawing2D.GraphicsPath
    $star.AddBezier((P 12 3), (P 12.7 8.2), (P 15.8 11.3), (P 21 12))
    $star.AddBezier((P 21 12), (P 15.8 12.7), (P 12.7 15.8), (P 12 21))
    $star.AddBezier((P 12 21), (P 11.3 15.8), (P 8.2 12.7), (P 3 12))
    $star.AddBezier((P 3 12), (P 8.2 11.3), (P 11.3 8.2), (P 12 3))
    $star.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $ink), $star)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$frames = @(); foreach ($sz in $sizes) { $frames += ,(New-Frame $sz) }

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $frames[$i].Length
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $w.Write($f) }
$w.Flush()
$full = [System.IO.Path]::GetFullPath($Output)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($full)) | Out-Null
[System.IO.File]::WriteAllBytes($full, $out.ToArray())
Write-Output "Wrote $full ($($out.Length) bytes)"
