# Generates the Nova Client "N-Star" logo (concept 6, refined) as PNGs + multi-size app.ico.
# Original artwork: custom geometric slanted N with a nested 4-point star, on a dark rounded tile.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot "..\src\NovaClient.Launcher\Assets"
New-Item -ItemType Directory -Force $assets | Out-Null

function Get-StarPoints([double]$cx, [double]$cy, [double]$outer, [double]$inner) {
    $pts = @()
    for ($k = 0; $k -lt 8; $k++) {
        $a = [Math]::PI/4 * $k - [Math]::PI/2
        $r = if ($k % 2 -eq 0) { $outer } else { $inner }
        $pts += New-Object System.Drawing.PointF(($cx + [Math]::Cos($a)*$r), ($cy + [Math]::Sin($a)*$r))
    }
    return $pts
}

function Draw-Logo([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $s = $size / 256.0

    # Rounded dark tile
    $radius = 52 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($size-$d, 0, $d, $d, 270, 90)
    $path.AddArc($size-$d, $size-$d, $d, $d, 0, 90); $path.AddArc(0, $size-$d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0,0)), (New-Object System.Drawing.Point(0,$size)),
        [System.Drawing.Color]::FromArgb(255,32,35,52), [System.Drawing.Color]::FromArgb(255,13,14,20))
    $g.FillPath($bg, $path)

    # Soft radial glow behind the mark
    for ($i = 5; $i -ge 1; $i--) {
        $gs = (150 + $i * 15) * $s
        $gb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb((11*$i), 124, 92, 255))
        $g.FillEllipse($gb, ($size-$gs)/2, ($size-$gs)/2, $gs, $gs); $gb.Dispose()
    }

    # Custom geometric N (0..100 design space, slight forward slant), centered
    $nPts = @(
        @(0,100), @(0,0), @(26,0), @(74,58), @(74,0), @(100,0),
        @(100,100), @(74,100), @(26,42), @(26,100)
    )
    $shear = 0.12      # forward slant
    $scale = 1.30 * $s
    $ox = 74 * $s; $oy = 72 * $s
    $poly = @()
    foreach ($p in $nPts) {
        $x = $p[0] * $scale + (100 - $p[1]) * $shear * $scale + $ox
        $y = $p[1] * $scale + $oy
        $poly += New-Object System.Drawing.PointF($x, $y)
    }

    # Depth shadow behind the N
    $shadow = @(); foreach ($p in $poly) { $shadow += New-Object System.Drawing.PointF(($p.X + 5*$s), ($p.Y + 6*$s)) }
    $shb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(120, 8, 6, 20))
    $g.FillPolygon($shb, $shadow); $shb.Dispose()

    # Gradient N
    $nb = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0,([int]($oy)))), (New-Object System.Drawing.Point($size,$size)),
        [System.Drawing.Color]::FromArgb(255,172,144,255), [System.Drawing.Color]::FromArgb(255,104,72,235))
    $g.FillPolygon($nb, $poly); $nb.Dispose()

    # Main sparkle nested into the N's top-right corner
    $spx = 196 * $s; $spy = 62 * $s
    for ($i = 3; $i -ge 1; $i--) {
        $gb2 = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb((7*$i), 255, 255, 255))
        $g.FillEllipse($gb2, $spx - (14+$i*7)*$s, $spy - (14+$i*7)*$s, (28+$i*14)*$s, (28+$i*14)*$s); $gb2.Dispose()
    }
    $g.FillPolygon([System.Drawing.Brushes]::White, (Get-StarPoints $spx $spy (30*$s) (9*$s)))

    # Tiny balancing star bottom-left
    $g.FillPolygon((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(210,255,255,255))),
        (Get-StarPoints (56*$s) (206*$s) (11*$s) (3.6*$s)))

    $g.Dispose()
    return $bmp
}

# PNGs + ICO
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngBytes = @{}
foreach ($sz in $sizes) {
    $bmp = Draw-Logo $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes[$sz] = $ms.ToArray(); $ms.Dispose()
    if ($sz -eq 256) { $bmp.Save((Join-Path $assets "logo.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}
$icoPath = Join-Path $assets "app.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($sz in $sizes) {
    $data = $pngBytes[$sz]
    $bw.Write([Byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([Byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($sz in $sizes) { $bw.Write($pngBytes[$sz]) }
$bw.Dispose(); $fs.Dispose()
Write-Host "Wrote $icoPath and logo.png"
