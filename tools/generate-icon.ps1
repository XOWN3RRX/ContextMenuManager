<#
.SYNOPSIS
    Draws the app icon (a context menu card with a mouse pointer) and saves it as a
    multi-size .ico file. The icon is original artwork made only from code, so it has
    no third-party license.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\generate-icon.ps1
#>
param(
    [string]$OutFile = (Join-Path $PSScriptRoot '..\src\ContextMenuManager\Assets\AppIcon.ico')
)

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconFrame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # Background tile: blue gradient
    $tile = New-RoundedPath (8 * $s) (8 * $s) (240 * $s) (240 * $s) (56 * $s)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 0x2E, 0x9B, 0xFF)), ([System.Drawing.Color]::FromArgb(255, 0x1A, 0x4F, 0xD6)), 45.0
    $g.FillPath($bg, $tile)

    # Menu card
    $card = New-RoundedPath (52 * $s) (44 * $s) (132 * $s) (164 * $s) (18 * $s)
    $shadow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(60, 0, 0, 0))
    $g.TranslateTransform(0, 6 * $s)
    $g.FillPath($shadow, $card)
    $g.ResetTransform()
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.FillPath($white, $card)

    # Menu rows; the middle one is highlighted (the "new" entry)
    $line = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xB8, 0xC4, 0xD6))
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x1F, 0x6F, 0xEB))
    $highlight = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xDC, 0xEB, 0xFF))

    if ($size -ge 32) {
        $g.FillRectangle($line, 72 * $s, 70 * $s, 92 * $s, 14 * $s)
        $hl = New-RoundedPath (62 * $s) (104 * $s) (112 * $s) (36 * $s) (8 * $s)
        $g.FillPath($highlight, $hl)
        $g.FillRectangle($accent, 72 * $s, 115 * $s, 74 * $s, 14 * $s)
        $g.FillRectangle($line, 72 * $s, 162 * $s, 80 * $s, 14 * $s)
    } else {
        $g.FillRectangle($line, 70 * $s, 72 * $s, 96 * $s, 22 * $s)
        $g.FillRectangle($accent, 70 * $s, 115 * $s, 96 * $s, 24 * $s)
        $g.FillRectangle($line, 70 * $s, 158 * $s, 96 * $s, 22 * $s)
    }

    # Mouse pointer
    $pts = @(
        (New-Object System.Drawing.PointF (150 * $s), (120 * $s)),
        (New-Object System.Drawing.PointF (150 * $s), (222 * $s)),
        (New-Object System.Drawing.PointF (174 * $s), (198 * $s)),
        (New-Object System.Drawing.PointF (190 * $s), (232 * $s)),
        (New-Object System.Drawing.PointF (206 * $s), (224 * $s)),
        (New-Object System.Drawing.PointF (190 * $s), (191 * $s)),
        (New-Object System.Drawing.PointF (222 * $s), (191 * $s))
    )
    $outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0x10, 0x1A, 0x2E)), ([Math]::Max(1.0, 10 * $s))
    $outline.LineJoin = 'Round'
    $g.FillPolygon($white, $pts)
    $g.DrawPolygon($outline, $pts)

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($size in $sizes) {
    $bmp = New-IconFrame $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
}

# ICO file: header, directory, then PNG-compressed images (supported since Windows Vista).
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$f.Bytes.Length); $w.Write([uint32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $w.Write($f.Bytes) }
$w.Flush()

$full = [System.IO.Path]::GetFullPath($OutFile)
New-Item -ItemType Directory -Force -Path (Split-Path $full) | Out-Null
[System.IO.File]::WriteAllBytes($full, $out.ToArray())
Write-Host "Wrote $full"
