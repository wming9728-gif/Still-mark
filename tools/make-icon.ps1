# Generates src/StillMark/Assets/app.ico (PNG-compressed multi-size icon) and docs/icon-256.png with System.Drawing.
# Design: dark rounded tile holding a rice-paper page with a folded corner; the lower part of the page is washed
# with blue ink, and a red seal with the character U+58A8 (Chinese "ink") sits on the upper part. Below 24 px the text lines are
# left out, below 32 px the seal is a plain red square.
# The seal character is drawn with LXGW WenKai Medium (SIL OFL 1.1), downloaded once into tools\vendor-cache (not
# committed) and checked against its SHA-256. No installed font is used, so the icon comes out the same on every
# machine and contains no glyphs from fonts that are licensed only for use within Windows.
param([string]$Out = "$PSScriptRoot\..\src\StillMark\Assets\app.ico", [string]$Preview = "$PSScriptRoot\..\docs\icon-256.png")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function C([string]$h) { [System.Drawing.ColorTranslator]::FromHtml($h) }
function B([string]$h) { New-Object System.Drawing.SolidBrush (C $h) }
function P([float]$x, [float]$y) { New-Object System.Drawing.PointF $x, $y }

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

$fontUrl = "https://github.com/lxgw/LxgwWenKai/releases/download/v1.522/LXGWWenKai-Medium.ttf"
$fontSha256 = "d4bdeb38a39151d74d084cba5090f8cb7d20bf83eedb78c35939ae70b9f4e3f6"
$fontFile = Join-Path $PSScriptRoot "vendor-cache\lxgw-wenkai-v1.522\LXGWWenKai-Medium.ttf"
if (-not (Test-Path $fontFile)) {
    New-Item -ItemType Directory -Force (Split-Path $fontFile) | Out-Null
    $ProgressPreference = "SilentlyContinue"
    Invoke-WebRequest -UseBasicParsing $fontUrl -OutFile $fontFile
}
if ((Get-FileHash $fontFile -Algorithm SHA256).Hash -ne $fontSha256) { throw "$fontFile does not match the expected SHA-256; delete it and run again." }
$fontCollection = New-Object System.Drawing.Text.PrivateFontCollection
$fontCollection.AddFontFile($fontFile)
$sealFont = $fontCollection.Families[0]

function Render([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $g.FillPath((B '#1F2937'), (New-RoundRect 0 0 $s $s ($s * 0.22)))

    # page with folded top-right corner and slightly rounded other corners
    $px = $s * 0.22; $py = $s * 0.14; $pw = $s * 0.56; $ph = $s * 0.72; $f = $s * 0.15; $r = $s * 0.03
    $page = New-Object System.Drawing.Drawing2D.GraphicsPath
    $page.AddArc($px, $py, 2 * $r, 2 * $r, 180, 90)
    $page.AddLine(($px + $r), $py, ($px + $pw - $f), $py)
    $page.AddLine(($px + $pw - $f), $py, ($px + $pw), ($py + $f))
    $page.AddArc(($px + $pw - 2 * $r), ($py + $ph - 2 * $r), 2 * $r, 2 * $r, 0, 90)
    $page.AddArc($px, ($py + $ph - 2 * $r), 2 * $r, 2 * $r, 90, 90)
    $page.CloseFigure()
    $g.FillPath((B '#FBF8F2'), $page)
    $fold = New-Object System.Drawing.Drawing2D.GraphicsPath
    $fold.AddLines([System.Drawing.PointF[]]@((P ($px + $pw - $f) $py), (P ($px + $pw - $f) ($py + $f)), (P ($px + $pw) ($py + $f))))
    $fold.CloseFigure()
    $g.FillPath((B '#D8D0C2'), $fold)

    # ink wash: a wave across the lower part of the page, darker towards the bottom
    $wy = $py + $ph * 0.62; $amp = $s * 0.055
    $ink = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 ($wy - $s * 0.06)), (P 0 ($py + $ph)), (C '#3B6FD8'), (C '#1E3A8A')
    $wave = New-Object System.Drawing.Drawing2D.GraphicsPath
    $wave.AddLine(($px - 2), ($py + $ph + 2), ($px - 2), $wy)
    $wave.AddBezier((P ($px - 2) $wy), (P ($px + $pw * 0.3) ($wy - 2 * $amp)), (P ($px + $pw * 0.6) ($wy + 2 * $amp)), (P ($px + $pw + 2) ($wy - $amp * 0.5)))
    $wave.AddLine(($px + $pw + 2), ($wy - $amp * 0.5), ($px + $pw + 2), ($py + $ph + 2))
    $wave.CloseFigure()
    $clip = $g.Clip; $g.SetClip($page); $g.FillPath($ink, $wave); $g.Clip = $clip

    # heading bar + one text line, kept clear of the seal
    if ($s -ge 24) {
        $lh = [Math]::Max(1.5, $s * 0.05); $lx = $px + $s * 0.08; $lw = $pw - $s * 0.16
        $g.FillPath((B '#1F2937'), (New-RoundRect $lx ($py + $s * 0.15) ($lw * 0.55) ($lh * 1.2) ($lh * 0.6)))
        $g.FillPath((B '#A3ABB7'), (New-RoundRect $lx ($py + $s * 0.27) ($lw * 0.5) $lh ($lh * 0.5)))
    }

    # red seal
    $ss = $s * 0.20; $sx = $px + $pw - $ss - $s * 0.06; $sy = $py + $s * 0.24
    $g.FillPath((B '#D0392B'), (New-RoundRect $sx $sy $ss $ss ($ss * 0.12)))
    if ($s -ge 32) {
        $font = New-Object System.Drawing.Font $sealFont, ($ss * 0.70), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $sf = New-Object System.Drawing.StringFormat
        $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
        $g.TextRenderingHint = 'AntiAliasGridFit'
        $g.DrawString([string][char]0x58A8, $font, (B '#FBF8F2'), (New-Object System.Drawing.RectangleF $sx, ($sy + $ss * 0.04), $ss, $ss), $sf)
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
[System.IO.File]::WriteAllBytes($Preview, $pngs[-1])
"wrote $Out (seal font: $($sealFont.Name))"
