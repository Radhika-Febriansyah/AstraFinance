# Generates every Astra Finance brand asset from the vector master (source/astra-symbol.svg):
#   svg/      symbol (light/dark), horizontal + vertical lockups (light/dark), app icon, installer banner
#   png/      each SVG exported at 512 and 1024 px (longest side), transparent where the SVG is
#   src/AstraFinance.App/Resources/astra.ico   Windows icon, 16-256 px frames (small sizes tuned for legibility)
#   installer/branding/   Inno Setup wizard bitmaps (24-bit BMP) for 100-250 % display scaling and the header banner (PNG)
#
# No third-party tools: uses WPF (PresentationCore) for geometry, text outlines and rasterising.
# Usage (repository root, Windows PowerShell 5.1):
#   powershell -NoProfile -ExecutionPolicy Bypass -STA -File assets\branding\build-brand-assets.ps1
param(
    # Wordmark typeface. Text is converted to outlines in the SVG/PNG/BMP outputs.
    [string]$WordmarkFont = 'Segoe UI Variable Display',
    [string]$WordmarkWeight = 'SemiBold'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$inv = [Globalization.CultureInfo]::InvariantCulture
[Threading.Thread]::CurrentThread.CurrentCulture = $inv

$brandDir = $PSScriptRoot
$repo = [IO.Path]::GetFullPath((Join-Path $brandDir '..\..'))
$svgDir = Join-Path $brandDir 'svg'
$pngDir = Join-Path $brandDir 'png'
$icoPath = Join-Path $repo 'src\AstraFinance.App\Resources\astra.ico'
$innoDir = Join-Path $repo 'installer\branding'
foreach ($d in $svgDir, $pngDir, $innoDir) { [IO.Directory]::CreateDirectory($d) | Out-Null }

# ---------------------------------------------------------------- brand colors
$Navy = '#172554'; $Royal = '#2563EB'; $Sky = '#60A5FA'; $White = '#FFFFFF'; $Night = '#0B1229'
function C([string]$hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }

# ---------------------------------------------------------------- geometry sources
$master = [IO.File]::ReadAllText((Join-Path $brandDir 'source\astra-symbol.svg'))
$m = [regex]::Match($master, 'id="astra-symbol"[^>]*?\sd="([^"]+)"', 'Singleline')
if (-not $m.Success) { throw 'Path id="astra-symbol" not found in source/astra-symbol.svg' }
$SymbolD = ($m.Groups[1].Value -replace '\s+', ' ').Trim()
$SymbolGeo = [System.Windows.Media.Geometry]::Parse($SymbolD); $SymbolGeo.Freeze()
$SB = $SymbolGeo.Bounds   # symbol bounds in master units

$typeface = New-Object System.Windows.Media.Typeface (
    (New-Object System.Windows.Media.FontFamily $WordmarkFont),
    [System.Windows.FontStyles]::Normal,
    ([System.Windows.FontWeights]::$WordmarkWeight),
    [System.Windows.FontStretches]::Normal)
# WPF silently falls back to another font when the requested one isn't installed; refuse that.
$glyphs = $null
if (-not $typeface.TryGetGlyphTypeface([ref]$glyphs) -or
    -not ($glyphs.FamilyNames.Values | Where-Object { $WordmarkFont.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) })) {
    throw "Wordmark font '$WordmarkFont' is not installed (resolved to '$(($glyphs.FamilyNames.Values) -join '/')')."
}
$ft = New-Object System.Windows.Media.FormattedText ('Astra Finance', $inv, [System.Windows.FlowDirection]::LeftToRight,
    $typeface, 100, [System.Windows.Media.Brushes]::Black, 1.0)
$TextGeo = [System.Windows.Media.PathGeometry]::CreateFromGeometry($ft.BuildGeometry((New-Object System.Windows.Point 0, 0)))
$TextGeo.Freeze()
$TB = $TextGeo.Bounds     # no descenders in "Astra Finance": height = cap/ascender height
$TextD = $TextGeo.ToString($inv)
$TextFillRule = if ($TextD.StartsWith('F1')) { 'nonzero' } else { 'evenodd' }
$TextD = $TextD -replace '^F[01]\s*', ''

# ---------------------------------------------------------------- layout model
# A layout is a canvas (W x H units) with an optional background and placed items.
# Each item: geometry + its SVG path data, a uniform scale and offset, and a fill (solid hex or gradient pair).
function Item($geo, [string]$d, [string]$rule, [double]$s, [double]$x, [double]$y, $fill) {
    [pscustomobject]@{ Geo = $geo; D = $d; Rule = $rule; S = $s; X = $x; Y = $y; Fill = $fill }
}
function Grad([string]$a, [string]$b) { , @($a, $b) }
function Symbol([double]$s, [double]$x, [double]$y, $fill) { Item $SymbolGeo $SymbolD 'nonzero' $s $x $y $fill }
function Wordmark([double]$s, [double]$x, [double]$y, [string]$fill) { Item $TextGeo $TextD $TextFillRule $s $x $y $fill }

# Places the symbol so its bounds fill a box of height $h with the top-left corner at ($x, $y).
function SymbolInBox([double]$x, [double]$y, [double]$h, $fill) {
    $s = $h / $SB.Height
    Symbol $s ($x - $SB.X * $s) ($y - $SB.Y * $s) $fill
}
# Places the wordmark so its cap height is $cap with its top-left at ($x, $y).
function WordmarkAt([double]$x, [double]$y, [double]$cap, [string]$fill) {
    $s = $cap / $TB.Height
    Wordmark $s ($x - $TB.X * $s) ($y - $TB.Y * $s) $fill
}
function WordmarkWidth([double]$cap) { $TB.Width * $cap / $TB.Height }
function SymbolWidth([double]$h) { $SB.Width * $h / $SB.Height }

function Layout([double]$w, [double]$h, $bg, [object[]]$items) {
    [pscustomobject]@{ W = $w; H = $h; Bg = $bg; Items = $items }
}
# Background: @{ Fill = hex or gradient pair; Radius = corner radius in units (0 = square) }
function Bg($fill, [double]$radius = 0) { @{ Fill = $fill; Radius = $radius } }

# ---------------------------------------------------------------- SVG writer
function SvgFillDefs($fill, [string]$id) {
    if ($fill -is [array]) {
        return "    <linearGradient id=""$id"" x1=""0"" y1=""1"" x2=""1"" y2=""0""><stop offset=""0"" stop-color=""$($fill[0])""/><stop offset=""1"" stop-color=""$($fill[1])""/></linearGradient>`n"
    }
    ''
}
function SvgFillRef($fill, [string]$id) { if ($fill -is [array]) { "url(#$id)" } else { $fill } }
function N([double]$v) { [Math]::Round($v, 4).ToString($inv) }

function Write-Svg($layout, [string]$path, [string]$title) {
    $defs = ''; $body = ''
    if ($layout.Bg) {
        $defs += SvgFillDefs $layout.Bg.Fill 'bg'
        $r = N $layout.Bg.Radius
        $body += "  <rect width=""$(N $layout.W)"" height=""$(N $layout.H)"" rx=""$r"" ry=""$r"" fill=""$(SvgFillRef $layout.Bg.Fill 'bg')""/>`n"
    }
    $i = 0
    foreach ($it in $layout.Items) {
        $id = "fill$i"; $defs += SvgFillDefs $it.Fill $id
        $body += "  <path transform=""matrix($(N $it.S) 0 0 $(N $it.S) $(N $it.X) $(N $it.Y))"" fill-rule=""$($it.Rule)"" fill=""$(SvgFillRef $it.Fill $id)"" d=""$($it.D)""/>`n"
        $i++
    }
    $svg = "<?xml version=""1.0"" encoding=""UTF-8""?>`n" +
        "<!-- Generated by assets/branding/build-brand-assets.ps1 from source/astra-symbol.svg. Do not edit by hand. -->`n" +
        "<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 $(N $layout.W) $(N $layout.H)"" width=""$(N $layout.W)"" height=""$(N $layout.H)"">`n" +
        "  <title>$title</title>`n"
    if ($defs) { $svg += "  <defs>`n$defs  </defs>`n" }
    $svg += $body + "</svg>`n"
    [IO.File]::WriteAllText($path, $svg, (New-Object Text.UTF8Encoding $false))
}

# ---------------------------------------------------------------- raster writer
function WpfBrush($fill) {
    if ($fill -is [array]) {
        $b = New-Object System.Windows.Media.LinearGradientBrush (C $fill[0]), (C $fill[1]), (New-Object System.Windows.Point 0, 1), (New-Object System.Windows.Point 1, 0)
    } else {
        $b = New-Object System.Windows.Media.SolidColorBrush (C $fill)
    }
    $b.Freeze(); $b
}
# Renders a layout to a bitmap of $pw x $ph pixels (layout scaled uniformly and centred).
function Render($layout, [int]$pw, [int]$ph, [string]$matte = $null) {
    $k = [Math]::Min($pw / $layout.W, $ph / $layout.H)
    $ox = ($pw - $layout.W * $k) / 2; $oy = ($ph - $layout.H * $k) / 2
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    if ($matte) { $dc.DrawRectangle((WpfBrush $matte), $null, (New-Object System.Windows.Rect 0, 0, $pw, $ph)) }
    $dc.PushTransform((New-Object System.Windows.Media.MatrixTransform $k, 0, 0, $k, $ox, $oy))
    if ($layout.Bg) {
        $r = $layout.Bg.Radius
        $dc.DrawRoundedRectangle((WpfBrush $layout.Bg.Fill), $null, (New-Object System.Windows.Rect 0, 0, $layout.W, $layout.H), $r, $r)
    }
    foreach ($it in $layout.Items) {
        $dc.PushTransform((New-Object System.Windows.Media.MatrixTransform $it.S, 0, 0, $it.S, $it.X, $it.Y))
        $dc.DrawGeometry((WpfBrush $it.Fill), $null, $it.Geo)
        $dc.Pop()
    }
    $dc.Pop(); $dc.Close()
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $pw, $ph, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv); $bmp.Freeze()
    $bmp
}
function PngBytes($bitmap) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $ms = New-Object IO.MemoryStream; $enc.Save($ms); , $ms.ToArray()
}
function Write-Png($layout, [int]$longest, [string]$path) {
    if ($layout.W -ge $layout.H) { $pw = $longest; $ph = [int][Math]::Round($longest * $layout.H / $layout.W) }
    else { $ph = $longest; $pw = [int][Math]::Round($longest * $layout.W / $layout.H) }
    [IO.File]::WriteAllBytes($path, (PngBytes (Render $layout $pw $ph)))
}
function Write-Bmp($layout, [int]$pw, [int]$ph, [string]$matte, [string]$path) {
    $bmp = Render $layout $pw $ph $matte
    $rgb = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap $bmp, ([System.Windows.Media.PixelFormats]::Bgr24), $null, 0
    $enc = New-Object System.Windows.Media.Imaging.BmpBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rgb))
    $fs = [IO.File]::Create($path); try { $enc.Save($fs) } finally { $fs.Close() }
}

# ---------------------------------------------------------------- layouts
$OnLight = Grad $Navy $Royal     # symbol on light backgrounds
$OnDark = Grad $Royal $Sky       # symbol on Deep Navy / dark backgrounds

# Symbol only: 100 x 100 canvas, symbol centred in its own bounds.
# (PowerShell variable names are case-insensitive: canvas size is $cw/$ch, symbol height $sh.)
function SymbolLayout($fill) {
    $sh = 90; $x = (100 - (SymbolWidth $sh)) / 2; $y = (100 - $sh) / 2
    Layout 100 100 $null @(SymbolInBox $x $y $sh $fill)
}
# App icon: Deep Navy rounded square; the symbol fills $ratio of the tile height.
function IconLayout([double]$ratio) {
    $sh = 100 * $ratio; $x = (100 - (SymbolWidth $sh)) / 2; $y = (100 - $sh) / 2
    Layout 100 100 (Bg $Navy 22) @(SymbolInBox $x $y $sh $OnDark)
}
# Horizontal lockup: symbol left, wordmark right; wordmark centred on the symbol's wings.
function HorizontalLayout($symbolFill, [string]$textFill, $bg = $null, [double]$padX = 8, [double]$padY = 8) {
    $sh = 100; $cap = 34; $gap = 22
    $sw = SymbolWidth $sh; $tw = WordmarkWidth $cap
    $cw = $padX + $sw + $gap + $tw + $padX; $ch = $padY + $sh + $padY
    $textTop = $padY + $sh * 0.58 - $cap / 2
    Layout $cw $ch $bg @(
        (SymbolInBox $padX $padY $sh $symbolFill),
        (WordmarkAt ($padX + $sw + $gap) $textTop $cap $textFill))
}
# Vertical lockup: symbol above, wordmark centred below.
function VerticalLayout($symbolFill, [string]$textFill, [double]$pad = 8) {
    $sh = 100; $cap = 22; $gap = 22
    $sw = SymbolWidth $sh; $tw = WordmarkWidth $cap
    $cw = $pad + [Math]::Max($sw, $tw) + $pad; $ch = $pad + $sh + $gap + $cap + $pad
    Layout $cw $ch $null @(
        (SymbolInBox (($cw - $sw) / 2) $pad $sh $symbolFill),
        (WordmarkAt (($cw - $tw) / 2) ($pad + $sh + $gap) $cap $textFill))
}
# Installer banner: Deep Navy, symbol left + white wordmark right, no tagline.
function BannerLayout { HorizontalLayout $OnDark $White (Bg $Navy 0) 70 46 }

$assets = [ordered]@{
    'astra-symbol-light'          = @{ L = (SymbolLayout $OnLight); T = 'Astra Finance symbol (for light backgrounds)' }
    'astra-symbol-dark'           = @{ L = (SymbolLayout $OnDark); T = 'Astra Finance symbol (for dark backgrounds)' }
    'astra-logo-horizontal-light' = @{ L = (HorizontalLayout $OnLight $Navy); T = 'Astra Finance logo, horizontal (for light backgrounds)' }
    'astra-logo-horizontal-dark'  = @{ L = (HorizontalLayout $OnDark $White); T = 'Astra Finance logo, horizontal (for dark backgrounds)' }
    'astra-logo-vertical-light'   = @{ L = (VerticalLayout $OnLight $Navy); T = 'Astra Finance logo, vertical (for light backgrounds)' }
    'astra-logo-vertical-dark'    = @{ L = (VerticalLayout $OnDark $White); T = 'Astra Finance logo, vertical (for dark backgrounds)' }
    'astra-app-icon'              = @{ L = (IconLayout 0.68); T = 'Astra Finance app icon' }
    'astra-installer-banner'      = @{ L = (BannerLayout); T = 'Astra Finance installer banner' }
}
foreach ($name in $assets.Keys) {
    $a = $assets[$name]
    Write-Svg $a.L (Join-Path $svgDir "$name.svg") $a.T
    foreach ($px in 512, 1024) { Write-Png $a.L $px (Join-Path $pngDir "$name-$px.png") }
}

# ---------------------------------------------------------------- Windows icon (.ico, PNG frames)
# Small frames give the symbol more of the tile so its silhouette survives at 16-24 px.
$icoSizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($sz in $icoSizes) {
    $ratio = if ($sz -le 24) { 0.80 } elseif ($sz -le 40) { 0.74 } else { 0.68 }
    , (PngBytes (Render (IconLayout $ratio) $sz $sz))
}
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$icoSizes.Count)
$offset = 6 + 16 * $icoSizes.Count
for ($i = 0; $i -lt $icoSizes.Count; $i++) {
    $sz = $icoSizes[$i]; $len = $frames[$i].Length
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $w.Write($f) }
$w.Flush()
[IO.File]::WriteAllBytes($icoPath, $out.ToArray())

# ---------------------------------------------------------------- Inno Setup wizard bitmaps
# WizardImageFile (welcome/finish pages, 164 x 314 at 100 %) and WizardSmallImageFile (55 x 55 at 100 %),
# one file per display scale; Setup picks the closest. 24-bit BMP, no transparency.
function WizardImageLayout {
    $cw = 164; $ch = 314; $sh = 70; $cap = 12.5
    $sw = SymbolWidth $sh; $tw = WordmarkWidth $cap
    $top = 108
    Layout $cw $ch (Bg (Grad $Night $Navy) 0) @(
        (SymbolInBox (($cw - $sw) / 2) $top $sh $OnDark),
        (WordmarkAt (($cw - $tw) / 2) ($top + $sh + 20) $cap $White))
}
foreach ($pct in 100, 125, 150, 175, 200, 225, 250) {
    $k = $pct / 100
    Write-Bmp (WizardImageLayout) ([int][Math]::Round(164 * $k)) ([int][Math]::Round(314 * $k)) $Night (Join-Path $innoDir "wizard-image-$pct.bmp")
    $s = [int][Math]::Round(55 * $k)
    Write-Bmp (IconLayout 0.68) $s $s $White (Join-Path $innoDir "wizard-small-$pct.bmp")
}

# Header banner shown on every inner page of Setup and in the uninstaller ("Minimalist Corporate": Deep Navy,
# symbol left, white wordmark right, no tagline). PNG, scaled by Setup to the header height at run time.
function HeaderBannerLayout {
    $sh = 58; $cap = 19; $gap = 14; $padX = 24; $ch = 100
    $sw = SymbolWidth $sh; $tw = WordmarkWidth $cap
    $top = ($ch - $sh) / 2
    Layout ($padX + $sw + $gap + $tw + $padX) $ch (Bg $Navy 0) @(
        (SymbolInBox $padX $top $sh $OnDark),
        (WordmarkAt ($padX + $sw + $gap) ($top + $sh * 0.58 - $cap / 2) $cap $White))
}
$hb = HeaderBannerLayout
[IO.File]::WriteAllBytes((Join-Path $innoDir 'header-banner.png'),
    (PngBytes (Render $hb ([int][Math]::Round(200 * $hb.W / $hb.H)) 200)))

"Symbol bounds: $($SB.ToString($inv)); wordmark font: $WordmarkFont $WordmarkWeight"
"SVG: $((Get-ChildItem $svgDir -Filter *.svg).Count)  PNG: $((Get-ChildItem $pngDir -Filter *.png).Count)  ICO: $((Get-Item $icoPath).Length) bytes  BMP: $((Get-ChildItem $innoDir -Filter *.bmp).Count)"
