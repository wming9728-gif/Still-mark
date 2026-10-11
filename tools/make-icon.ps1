# Generates src/InkPage/Assets/app.ico (PNG-compressed multi-size icon) with System.Drawing.
# Design: dark rounded tile (chrome colour of the chosen layout) holding a white page with a folded corner,
# a blue heading bar (accent #2F6FDE) and grey text lines.
param([string]$Out = "$PSScriptRoot\..\src\InkPage\Assets\app.ico")
Add-Type -AssemblyName System.Drawing

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Render([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $tile = New-RoundRect 0 0 ($s) ($s) ($s * 0.22)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#23272E'))), $tile)

    # page with folded top-right corner
    $px = $s * 0.24; $py = $s * 0.16; $pw = $s * 0.52; $ph = $s * 0.68; $f = $s * 0.15
    $page = New-Object System.Drawing.Drawing2D.GraphicsPath
    $page.AddLines([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF $px, $py),
        (New-Object System.Drawing.PointF ($px + $pw - $f), $py),
        (New-Object System.Drawing.PointF ($px + $pw), ($py + $f)),
        (New-Object System.Drawing.PointF ($px + $pw), ($py + $ph)),
        (New-Object System.Drawing.PointF $px, ($py + $ph))))
    $page.CloseFigure()
    $g.FillPath([System.Drawing.Brushes]::White, $page)
    $fold = New-Object System.Drawing.Drawing2D.GraphicsPath
    $fold.AddLines([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($px + $pw - $f), $py),
        (New-Object System.Drawing.PointF ($px + $pw - $f), ($py + $f)),
        (New-Object System.Drawing.PointF ($px + $pw), ($py + $f))))
    $fold.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#C9CED6'))), $fold)

    # heading bar + text lines
    $lx = $px + $s * 0.07; $lw = $pw - $s * 0.14; $lh = [Math]::Max(1.5, $s * 0.055)
    $blue = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#2F6FDE'))
    $grey = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#9AA1AB'))
    $g.FillPath($blue, (New-RoundRect $lx ($py + $s * 0.20) ($lw * 0.62) ($lh * 1.25) ($lh * 0.5)))
    if ($s -ge 24) {
        $g.FillPath($grey, (New-RoundRect $lx ($py + $s * 0.34) $lw $lh ($lh * 0.45)))
        $g.FillPath($grey, (New-RoundRect $lx ($py + $s * 0.44) ($lw * 0.85) $lh ($lh * 0.45)))
        $g.FillPath($grey, (New-RoundRect $lx ($py + $s * 0.54) ($lw * 0.7) $lh ($lh * 0.45)))
    } else {
        $g.FillRectangle($grey, $lx, $py + $s * 0.40, $lw, $lh)
        $g.FillRectangle($grey, $lx, $py + $s * 0.54, $lw * 0.7, $lh)
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @(); foreach ($s in $sizes) { $pngs += ,(Render $s) }

$dir = Split-Path $Out
New-Item -ItemType Directory -Force $dir | Out-Null
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
# preview png for docs
[System.IO.File]::WriteAllBytes((Join-Path $dir 'app-256.png'), $pngs[-1])
"wrote $Out"
