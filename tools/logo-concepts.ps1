# Renders 6 original Nova Client logo concepts into one labelled contact sheet.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$size = 300
$purpleA = [System.Drawing.Color]::FromArgb(255, 158, 128, 255)
$purpleB = [System.Drawing.Color]::FromArgb(255, 108, 76, 240)

function New-Tile {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0,0)), (New-Object System.Drawing.Point(0,$size)),
        [System.Drawing.Color]::FromArgb(255,30,33,48), [System.Drawing.Color]::FromArgb(255,12,13,18))
    $g.FillRectangle($bg, 0, 0, $size, $size)
    for ($i = 4; $i -ge 1; $i--) {
        $gs = 150 + $i * 22
        $gb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb((10*$i), 124, 92, 255))
        $g.FillEllipse($gb, ($size-$gs)/2, ($size-$gs)/2, $gs, $gs); $gb.Dispose()
    }
    return @($bmp, $g)
}

function Get-StarPoints([double]$cx, [double]$cy, [double]$outer, [double]$inner, [double]$rotDeg = 0) {
    $pts = @()
    for ($k = 0; $k -lt 8; $k++) {
        $a = [Math]::PI/4 * $k - [Math]::PI/2 + $rotDeg * [Math]::PI / 180
        $r = if ($k % 2 -eq 0) { $outer } else { $inner }
        $pts += New-Object System.Drawing.PointF(($cx + [Math]::Cos($a)*$r), ($cy + [Math]::Sin($a)*$r))
    }
    return $pts
}

function StarBrush { New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0,0)), (New-Object System.Drawing.Point($size,$size)), $purpleA, $purpleB) }

$tiles = @()

# 1 — Classic Nova (current)
$t = New-Tile; $b = $t[0]; $g = $t[1]
$g.FillPolygon((StarBrush), (Get-StarPoints 150 150 108 28))
$g.FillPolygon((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)), (Get-StarPoints 216 84 22 7))
$g.Dispose(); $tiles += ,$b

# 2 — Comet (star with motion trail)
$t = New-Tile; $b = $t[0]; $g = $t[1]
for ($i = 8; $i -ge 1; $i--) {
    $alpha = 18 * $i; $off = $i * 13
    $tb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($alpha, 124, 92, 255))
    $g.FillPolygon($tb, (Get-StarPoints (108 - $off*0.7 + 70) (192 + $off*0.5 - 40) (78 - $i*6) (20 - $i*1.5))); $tb.Dispose()
}
$g.FillPolygon((StarBrush), (Get-StarPoints 185 115 88 24))
$g.Dispose(); $tiles += ,$b

# 3 — Orbit (star with ring)
$t = New-Tile; $b = $t[0]; $g = $t[1]
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(190,255,255,255), 7)
$state = $g.Save(); $g.TranslateTransform(150,150); $g.RotateTransform(-24)
$g.DrawEllipse($pen, -128, -52, 256, 104); $g.Restore($state); $pen.Dispose()
$g.FillPolygon((StarBrush), (Get-StarPoints 150 150 92 25))
$g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)), 252, 96, 14, 14)
$g.Dispose(); $tiles += ,$b

# 4 — Pixel Nova (blocky Minecraft-style star)
$t = New-Tile; $b = $t[0]; $g = $t[1]
$px = 24; $grid = @(
 "....X....", "....X....", "...XXX...", "..XXXXX..", "XXXXXXXXX",
 "..XXXXX..", "...XXX...", "....X....", "....X....")
$sb = StarBrush
for ($r = 0; $r -lt $grid.Count; $r++) { for ($c = 0; $c -lt $grid[$r].Length; $c++) {
    if ($grid[$r][$c] -eq 'X') { $g.FillRectangle($sb, 42 + $c*$px, 42 + $r*$px, $px-2, $px-2) } } }
$g.Dispose(); $tiles += ,$b

# 5 — Nova Shield (PvP)
$t = New-Tile; $b = $t[0]; $g = $t[1]
$shield = New-Object System.Drawing.Drawing2D.GraphicsPath
$shield.AddLines(@(
    (New-Object System.Drawing.PointF(150, 34)), (New-Object System.Drawing.PointF(252, 74)),
    (New-Object System.Drawing.PointF(252, 158))))
$shield.AddBezier(252,158, 252,220, 205,252, 150,272)
$shield.AddBezier(150,272, 95,252, 48,220, 48,158)
$shield.AddLine(48,158, 48,74); $shield.CloseFigure()
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(230,255,255,255), 9)
$g.DrawPath($pen, $shield); $pen.Dispose()
$g.FillPolygon((StarBrush), (Get-StarPoints 150 152 74 20))
$g.Dispose(); $tiles += ,$b

# 6 — N-Star monogram
$t = New-Tile; $b = $t[0]; $g = $t[1]
$font = New-Object System.Drawing.Font("Segoe UI Black", 150, [System.Drawing.FontStyle]::Bold)
$fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = "Center"; $fmt.LineAlignment = "Center"
$g.DrawString("N", $font, (StarBrush), (New-Object System.Drawing.RectangleF(0, 8, $size, $size)), $fmt)
$g.FillPolygon((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)), (Get-StarPoints 234 66 28 8))
$font.Dispose()
$g.Dispose(); $tiles += ,$b

# Contact sheet 3x2 with number badges
$pad = 18; $cols = 3
$sheet = New-Object System.Drawing.Bitmap(($cols*($size+$pad)+$pad), (2*($size+$pad)+$pad))
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.Clear([System.Drawing.Color]::FromArgb(255,8,9,12))
$badgeFont = New-Object System.Drawing.Font("Segoe UI", 22, [System.Drawing.FontStyle]::Bold)
for ([int]$n = 0; $n -lt $tiles.Count; $n++) {
    [int]$x = $pad + ($n % $cols) * ($size + $pad)
    [int]$y = $pad + [Math]::Floor($n / $cols) * ($size + $pad)
    $sg.DrawImage([System.Drawing.Bitmap]$tiles[$n], $x, $y)
    $badgeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(230,124,92,255))
    $sg.FillEllipse($badgeBrush, ($x+10), ($y+10), 44, 44); $badgeBrush.Dispose()
    $fmt2 = New-Object System.Drawing.StringFormat; $fmt2.Alignment = "Center"; $fmt2.LineAlignment = "Center"
    [string]$label = [string]($n + 1)
    $rect = New-Object System.Drawing.RectangleF(($x+10), ($y+10), 44, 44)
    $sg.DrawString($label, $badgeFont, [System.Drawing.Brushes]::White, $rect, $fmt2)
}
$out = Join-Path $PSScriptRoot "..\dist\nova-logo-concepts.png"
$sheet.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$sg.Dispose(); $sheet.Dispose()
Write-Host "Saved $out"
