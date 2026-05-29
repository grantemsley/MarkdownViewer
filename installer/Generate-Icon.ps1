<#
.SYNOPSIS
  Generate src/app.ico — an "M down-arrow" mark on a Windows-accent tile.

.DESCRIPTION
  Draws the icon at several sizes with System.Drawing and assembles a
  multi-resolution .ico (PNG-compressed entries). Re-run to regenerate.
  PowerShell 5.1 compatible.
#>
[CmdletBinding()]
param(
    [string]$OutPath = (Join-Path $PSScriptRoot '..\src\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$accent = [System.Drawing.Color]::FromArgb(255, 0, 120, 212)   # #0078D4

function New-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $g.Clear([System.Drawing.Color]::Transparent)

        # Rounded-square tile.
        $pad = [Math]::Max(1, [int]($s * 0.055))
        $rad = [Math]::Max(2, [int]($s * 0.20))
        $x = $pad; $y = $pad; $w = $s - 2 * $pad; $h = $s - 2 * $pad
        $d = $rad * 2
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($x, $y, $d, $d, 180, 90)
        $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
        $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
        $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
        $path.CloseFigure()
        $tile = New-Object System.Drawing.SolidBrush($accent)
        $g.FillPath($tile, $path)

        $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
        $penW = [Math]::Max(1.0, $s * 0.085)
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penW)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

        # "M" on the left ~55% of the content box, drawn as strokes.
        $cx = $s * 0.30; $top = $s * 0.34; $bot = $s * 0.66
        $mw = $s * 0.30
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF([single]($cx - $mw / 2), [single]$bot)),
            (New-Object System.Drawing.PointF([single]($cx - $mw / 2), [single]$top)),
            (New-Object System.Drawing.PointF([single]$cx,             [single]($top + ($bot - $top) * 0.55))),
            (New-Object System.Drawing.PointF([single]($cx + $mw / 2), [single]$top)),
            (New-Object System.Drawing.PointF([single]($cx + $mw / 2), [single]$bot))
        )
        $g.DrawLines($pen, $pts)

        # Down-arrow on the right.
        $ax = $s * 0.70
        $shaftTop = $s * 0.34; $shaftBot = $s * 0.56
        $g.DrawLine($pen, [single]$ax, [single]$shaftTop, [single]$ax, [single]$shaftBot)
        $head = $s * 0.13
        $tri = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF([single]($ax - $head), [single]($shaftBot - $head * 0.2))),
            (New-Object System.Drawing.PointF([single]($ax + $head), [single]($shaftBot - $head * 0.2))),
            (New-Object System.Drawing.PointF([single]$ax,           [single]($s * 0.70)))
        )
        $g.FillPolygon($white, $tri)

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return , $ms.ToArray()
    }
    finally { $g.Dispose(); $bmp.Dispose() }
}

$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = New-IconPng $s }

$full = [System.IO.Path]::GetFullPath($OutPath)
$fs = [System.IO.File]::Create($full)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    $bw.Write([uint16]0)            # reserved
    $bw.Write([uint16]1)            # type: icon
    $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    foreach ($s in $sizes) {
        $len = $pngs[$s].Length
        $dim = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$dim)       # width
        $bw.Write([byte]$dim)       # height
        $bw.Write([byte]0)          # palette count
        $bw.Write([byte]0)          # reserved
        $bw.Write([uint16]1)        # color planes
        $bw.Write([uint16]32)       # bits per pixel
        $bw.Write([uint32]$len)     # image data size
        $bw.Write([uint32]$offset)  # image data offset
        $offset += $len
    }
    foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
}
finally { $bw.Dispose(); $fs.Dispose() }

Write-Host "Wrote $full ($([Math]::Round((Get-Item $full).Length/1kb,1)) KB, $($sizes.Count) sizes)"
