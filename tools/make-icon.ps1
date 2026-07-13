# Generates the Nova Client logo PNGs and a multi-size app.ico (PNG-compressed entries).
# Original artwork: a four-point "nova" star with a soft glow on a dark rounded tile.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot "..\src\NovaClient.Launcher\Assets"
New-Item -ItemType Directory -Force $assets | Out-Null

function Draw-Logo([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $s = $size / 256.0

    # Rounded dark tile
    $radius = 52 * $s
    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)), (New-Object System.Drawing.Point(0, $size)),
        [System.Drawing.Color]::FromArgb(255, 33, 37, 52), [System.Drawing.Color]::FromArgb(255, 14, 16, 21))
    $g.FillPath($bgBrush, $path)

    # Soft glow behind the star
    for ($i = 5; $i -ge 1; $i--) {
        $alpha = 12 * $i
        $glowSize = (150 + $i * 14) * $s
        $glowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($alpha, 124, 92, 255))
        $g.FillEllipse($glowBrush, ($size - $glowSize) / 2, ($size - $glowSize) / 2, $glowSize, $glowSize)
        $glowBrush.Dispose()
    }

    # Four-point star (slightly concave edges via inner control points)
    $cx = $size / 2.0; $cy = $size / 2.0
    $outer = 100 * $s; $inner = 26 * $s
    $points = @()
    for ($k = 0; $k -lt 8; $k++) {
        $angle = [Math]::PI / 4 * $k - [Math]::PI / 2
        $r = if ($k % 2 -eq 0) { $outer } else { $inner }
        $points += New-Object System.Drawing.PointF(($cx + [Math]::Cos($angle) * $r), ($cy + [Math]::Sin($angle) * $r))
    }
    $starBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)), (New-Object System.Drawing.Point($size, $size)),
        [System.Drawing.Color]::FromArgb(255, 158, 128, 255), [System.Drawing.Color]::FromArgb(255, 108, 76, 240))
    $g.FillPolygon($starBrush, $points)

    # Small sparkle top-right
    $spx = $cx + 62 * $s; $spy = $cy - 62 * $s; $spo = 20 * $s; $spi = 6 * $s
    $sparkle = @()
    for ($k = 0; $k -lt 8; $k++) {
        $angle = [Math]::PI / 4 * $k - [Math]::PI / 2
        $r = if ($k % 2 -eq 0) { $spo } else { $spi }
        $sparkle += New-Object System.Drawing.PointF(($spx + [Math]::Cos($angle) * $r), ($spy + [Math]::Sin($angle) * $r))
    }
    $sparkleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 255, 255, 255))
    $g.FillPolygon($sparkleBrush, $sparkle)

    $g.Dispose()
    return $bmp
}

# PNGs (256 saved as logo.png for docs/site use)
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngBytes = @{}
foreach ($sz in $sizes) {
    $bmp = Draw-Logo $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes[$sz] = $ms.ToArray()
    $ms.Dispose()
    if ($sz -eq 256) { $bmp.Save((Join-Path $assets "logo.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# ICO container with PNG-compressed entries (Vista+ format)
$icoPath = Join-Path $assets "app.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($sz in $sizes) {
    $data = $pngBytes[$sz]
    $bw.Write([Byte]($(if ($sz -ge 256) { 0 } else { $sz })))  # width (0 = 256)
    $bw.Write([Byte]($(if ($sz -ge 256) { 0 } else { $sz })))  # height
    $bw.Write([Byte]0); $bw.Write([Byte]0)                     # colors, reserved
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)                # planes, bpp
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($sz in $sizes) { $bw.Write($pngBytes[$sz]) }
$bw.Dispose(); $fs.Dispose()

Write-Host "Wrote $icoPath and logo.png"
