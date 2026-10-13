$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDir = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Path $assetDir -Force | Out-Null
$iconStream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($iconStream)
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(24, 38, 65))
    $paper = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(235, 242, 255))
    $accent = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(72, 210, 184))
    $graphics.FillEllipse($background, 4, 4, 248, 248)
    $graphics.FillRectangle($paper, 64, 58, 128, 152)
    $graphics.FillRectangle($accent, 96, 42, 64, 28)
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(24, 38, 65), 12)
    $graphics.DrawLine($pen, 86, 110, 167, 110)
    $graphics.DrawLine($pen, 153, 96, 167, 110)
    $graphics.DrawLine($pen, 153, 124, 167, 110)
    $graphics.DrawLine($pen, 170, 157, 89, 157)
    $graphics.DrawLine($pen, 103, 143, 89, 157)
    $graphics.DrawLine($pen, 103, 171, 89, 157)
    $png = [System.IO.MemoryStream]::new()
    $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,$png.ToArray()
    if ($size -eq 256) { $bitmap.Save((Join-Path $assetDir 'ClipboardBridge.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $png.Dispose(); $pen.Dispose(); $background.Dispose(); $paper.Dispose(); $accent.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
[System.IO.File]::WriteAllBytes((Join-Path $assetDir 'ClipboardBridge.ico'), $iconStream.ToArray())
$writer.Dispose(); $iconStream.Dispose()
